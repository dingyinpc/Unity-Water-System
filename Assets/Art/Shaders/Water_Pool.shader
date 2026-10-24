// ============================================================================
// Water_Pool.shader — 第五条水线：泳池（PoolScene）
//
// 泳池水和潟湖的判据几乎处处相反：
//   潟湖：水色随深度、反射抢戏、碎闪是主角    泳池：**清水见底、焦散、涟漪是主角**
//   深度 = 地形高度场                          深度 = 解析常数（池底平，-1.35m）
//   波 = 中频噪声 + 毛细                       波 = 波动方程高度场（PoolRipples 模拟）
//
// 三条主信号：
//   ① 见底：瓷砖经屏幕空间折射搬上来，隔一层清水（吸收小、透明度高）；
//   ② 焦散：程序化亮网打在"搬上来的池底"上，**强度用波场梯度调制**——
//      涟漪过处焦散晃动，这是"会呼吸的泳池"的钥匙；
//   ③ 涟漪：高度场梯度 → 法线（主）+ 顶点位移（辅，厘米级）；
//      无交互时叠两层微幅正弦（idle），泳池不能是死玻璃。
//
// 纪律（沿用《05》全家族坑）：
//   · 屏幕空间采样用 clipPos，绝不蹭 SV_POSITION（A-1）；
//   · 反射 lerp 替换不累加（D-1）；法线高频掠射衰减（D-9）；
//   · **不读 _CameraDepthTexture** —— 本机两条取帧通路都不可信（IsletScene
//     2026-10-04 轮实锤），池底是解析平面，深度是常数，没什么可读的。
// ============================================================================
Shader "WaterProject/Water_Pool"
{
    Properties
    {
        // ── 清水 ──
        _WaterColor      ("水体色(线性)", Color) = (0.10, 0.55, 0.62, 1)
        _Absorb          ("吸收系数(1/m)", Float) = 0.55
        _FloorDepth      ("池深(米)", Float) = 1.35
        _Opacity         ("不透明度", Range(0,1)) = 0.30

        // ── 折射 ──
        _RefractStrength ("折射偏移", Float) = 0.12
        _MaxUVOffset     ("偏移上限", Range(0, 0.1)) = 0.02

        // ── 涟漪（波动方程场）──
        _WaveAmp         ("顶点位移幅度(米)", Float) = 0.055
        _NormalStrength  ("法线强度", Float) = 1.6
        _IdleStrength    ("静水微波强度", Float) = 1.0
        _CrestFoam       ("波峰白沫强度", Range(0,1)) = 0.22

        // ── 焦散 ──
        _CausticsStrength("焦散强度", Float) = 0.55
        _CausticsScale   ("焦散密度", Float) = 0.55
        _CausticsSpeed   ("焦散速度", Float) = 1.0
        // 多尺度 + 色散（P2-2 / 参考 S8 C12–C13）
        _CausticMode     ("焦散 0关/1单层(旧)/2多尺度色散", Float) = 2
        _CausticDepthMix ("焦散深度混合 0细网/1大斑", Range(0,1)) = 0.35
        _CausticDisperse ("焦散色散强度", Range(0,1)) = 0.6
        _CausticDetile   ("三角去平铺强度", Range(0,1)) = 1.0

        // ── 反射/高光 ──
        _SkyColor        ("天空反射色", Color) = (0.55, 0.75, 0.92, 1)
        _FresnelPow      ("菲涅尔指数", Float) = 4.0
        _SkyReflect      ("反射权重", Range(0,2)) = 0.55
        _SunColor        ("太阳高光色", Color) = (1.0, 0.96, 0.85, 1)
        _SunGloss        ("高光锐度", Float) = 240.0
        _SunStrength     ("高光强度", Float) = 0.9

        _DebugMode       ("调试 0关 1波高 2梯度模长", Range(0,2)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_PoolWaveTex); SAMPLER(sampler_PoolWaveTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor, _SkyColor, _SunColor;
                float  _Absorb, _FloorDepth, _Opacity;
                float  _RefractStrength, _MaxUVOffset;
                float  _WaveAmp, _NormalStrength, _IdleStrength, _CrestFoam;
                float  _CausticsStrength, _CausticsScale, _CausticsSpeed;
                float  _CausticMode, _CausticDepthMix, _CausticDisperse, _CausticDetile;
                float  _FresnelPow, _SkyReflect, _SunGloss, _SunStrength;
                float  _DebugMode;
            CBUFFER_END

            float4 _PoolWaveRect;       // PoolRipples 每帧写：(minX, minZ, sizeX, sizeZ)
                                        // ⚠ 三方（vert/frag/PoolRipples.WorldToUv）共用这套语义，
                                        //   任何一边自作主张加 ±0.5 都会错位成半场镜像

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 clipPos    : TEXCOORD0;   // ★ 屏幕空间采样用这个（A-1）
                float3 worldPos   : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                // 顶点位移：读波动方程高度场（场随动画持续更新，逐帧不同）
                float2 wuv = (wp.xz - _PoolWaveRect.xy) / _PoolWaveRect.zw;
                float h = SAMPLE_TEXTURE2D_LOD(_PoolWaveTex, sampler_PoolWaveTex, saturate(wuv), 0).r;
                wp.y += h * _WaveAmp;

                o.positionCS = TransformWorldToHClip(wp);
                o.clipPos    = o.positionCS;
                o.worldPos   = wp;
                return o;
            }

            // 静水微波：两层慢速正弦的坡度（无交互时泳池不能是死玻璃）
            float2 IdleSlope(float2 xz, float t)
            {
                float2 s = 0;
                s += float2(cos(xz.x * 6.7 + t * 2.1), -sin(xz.y * 5.9 - t * 1.6)) * 0.010;
                s += float2(cos((xz.x + xz.y) * 4.1 - t * 1.2), sin((xz.x - xz.y) * 4.7 + t * 1.8)) * 0.008;
                return s * _IdleStrength;
            }

            // ==================== 多尺度焦散（P2-2 / 参考 S8 C12–C13）====================

            // R16-G1 水面微光：双对向滚动干涉纹 + 时间域弯曲 = 原地演化的细结构
            //（参考水面能量来自频谱演化，静态滚动给不出——R13 归因 R15 证实）
            float ShimmerNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }
            float3 ShimmerN(float2 xz, float t, float scale)
            {
                float2 q1 = xz * scale + float2(t * 0.13, t * 0.08);
                float2 q2 = xz * scale * 1.73 - float2(t * 0.10, t * 0.14);
                q1 += 0.30 * sin(q2.yx * 2.3 + t * 0.27);
                q2 += 0.30 * sin(q1.yx * 1.9 - t * 0.21);
                float2 e = float2(0.30, 0.0);
                float f0 = ShimmerNoise(q1) * 0.62 + ShimmerNoise(q2) * 0.38;
                float fx = ShimmerNoise(q1 + e) * 0.62 + ShimmerNoise(q2 + e) * 0.38;
                float fy = ShimmerNoise(q1 + e.yx) * 0.62 + ShimmerNoise(q2 + e.yx) * 0.38;
                return normalize(float3((f0 - fx) * 22.0, 1.0, (f0 - fy) * 22.0));
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // 单层焦散图案：两个正弦网格取峰 → 亮网
            float CausticPat(float2 p, float t, float speed)
            {
                float a = sin(p.x * 5.3 + t * 1.05 * speed + sin(p.y * 3.9 - t * 0.75 * speed));
                float b = sin(p.y * 4.7 - t * 0.85 * speed + sin(p.x * 4.1 + t * 0.63 * speed));
                return pow(saturate(1.0 - abs(a * b)), 3.0);
            }

            // 三角随机平移 + 重心权重混合（C12）：把平面划成三角格，三顶点各取固定随机偏移采样，
            // 按重心权重混合 → 打破周期性平铺（同 S8 的"三角形偏移"签名）。
            float CausticDeTile(float2 p, float t, float speed, float detile)
            {
                const float K1 = 0.366025404, K2 = 0.211324865;
                float2 i = floor(p + (p.x + p.y) * K1);
                float2 a = p - i + (i.x + i.y) * K2;
                float o = step(a.y, a.x);
                float2 b = a - float2(o, 1.0 - o) + K2;
                float2 c = a - 1.0 + 2.0 * K2;

                float2 oa = (Hash22(i) * 2.0 - 1.0) * detile;
                float2 ob = (Hash22(i + float2(o, 1.0 - o)) * 2.0 - 1.0) * detile;
                float2 oc = (Hash22(i + 1.0) * 2.0 - 1.0) * detile;

                float wa = max(0.5 - dot(a, a), 0.0);
                float wb = max(0.5 - dot(b, b), 0.0);
                float wc = max(0.5 - dot(c, c), 0.0);
                float ws = max(wa + wb + wc, 1e-4);

                return (wa * CausticPat(p + oa, t, speed)
                      + wb * CausticPat(p + ob, t, speed)
                      + wc * CausticPat(p + oc, t, speed)) / ws;
            }

            // RGB 色散（C13）：三通道用略微不同的尺度/偏移采样 → 水底彩虹边缘
            // ⚠ detile 由调用方按层给：三角偏移在**低频层**会在格边露出块状拼接（本轮实测水面出现大块 facet），
            //   所以只让**高频细网层**去平铺（它的格小、接缝近乎像素级）。
            float3 CausticRGB(float2 p, float t, float speed, float disp, float detile)
            {
                float cr = CausticDeTile(p * (1.0 + 0.05 * disp) + float2( 0.02, 0.0) * disp, t, speed, detile);
                float cg = CausticDeTile(p, t, speed, detile);
                float cb = CausticDeTile(p * (1.0 - 0.05 * disp) + float2(-0.02, 0.01) * disp, t, speed, detile);
                return float3(cr, cg, cb);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;
                float t = _Time.y;
                float3 V = normalize(_WorldSpaceCameraPos - wp);

                // ── 波动方程高度场：中心 + 梯度 ──
                float2 wuv = (wp.xz - _PoolWaveRect.xy) / _PoolWaveRect.zw;
                float2 px = 2.0 / _PoolWaveRect.zw;             // 隔两格差分，噪声更稳
                float h  = SAMPLE_TEXTURE2D(_PoolWaveTex, sampler_PoolWaveTex, saturate(wuv)).r;
                float hx1 = SAMPLE_TEXTURE2D(_PoolWaveTex, sampler_PoolWaveTex, saturate(wuv + float2(px.x, 0))).r;
                float hx0 = SAMPLE_TEXTURE2D(_PoolWaveTex, sampler_PoolWaveTex, saturate(wuv - float2(px.x, 0))).r;
                float hz1 = SAMPLE_TEXTURE2D(_PoolWaveTex, sampler_PoolWaveTex, saturate(wuv + float2(0, px.y))).r;
                float hz0 = SAMPLE_TEXTURE2D(_PoolWaveTex, sampler_PoolWaveTex, saturate(wuv - float2(0, px.y))).r;
                float2 grad = float2(hx1 - hx0, hz1 - hz0);
                float gradMag = length(grad);

                // ── 法线：波场坡度（主）+ 静水微坡 ──
                float2 slope = grad * _NormalStrength + IdleSlope(wp.xz, t);
                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));
                N = normalize(lerp(N, ShimmerN(wp.xz, t, 1.8), 0.55));

                // ── 屏幕空间 UV（clipPos，《05》A-1）──
                float4 sp = IN.clipPos * 0.5;
                sp.xy = float2(sp.x, sp.y * _ProjectionParams.x) + sp.w;
                float2 screenUV = sp.xy / sp.w;

                // ── ① 折射：搬池底上来（解析深度 = 常数，不看深度纹理）──
                float3 refr = refract(-V, N, 1.33);
                if (dot(refr, refr) < 1e-5) refr = -V;
                float2 refrDir = normalize(refr.xy + 1e-6);
                float2 uvOff = clamp(refrDir * _RefractStrength * (0.5 / max(abs(refr.z), 0.05)),
                                     -_MaxUVOffset, _MaxUVOffset);
                float3 bottom = SampleSceneColor(saturate(screenUV + uvOff)).rgb;
                // 清水隔层：池深 1.35m 的吸收把池底压向水色（Beer–Lambert）
                // ── ② 焦散（P2-2 / 参考 S8 C12–C13）：0关 / 1单层(旧) / 2多尺度+RGB色散 ──
                // 梯度调制：波谷(负梯度一侧)聚光、波峰散光；再吃一点"有波才晃"的静态底
                float gradMod = 0.45 + 0.9 * saturate(gradMag * 6.0 + 0.25);
                float3 caustic = 0;
                if (_CausticMode > 1.5)
                {
                    float2 q = wp.xz * _CausticsScale;
                    float d = saturate(_CausticDepthMix);          // 0 细网(浅) → 1 大斑(深)
                    float wF = 1.0 - smoothstep(0.0, 0.5, d);
                    float wM = 1.0 - abs(d * 2.0 - 1.0);
                    float wC = smoothstep(0.5, 1.0, d);
                    // 三层：细网 / 中 / 大斑，加权合成（C12 水深连续混合）
                    // 去平铺强度按层递减：细网层满强度、中层次之、大斑层不用（避免块状接缝）
                    float3 cF = CausticRGB(q * 2.2, t, _CausticsSpeed * 1.30, _CausticDisperse, _CausticDetile * 1.00);
                    float3 cM = CausticRGB(q * 1.0, t, _CausticsSpeed * 1.00, _CausticDisperse, _CausticDetile * 0.35);
                    float3 cC = CausticRGB(q * 0.45, t, _CausticsSpeed * 0.70, _CausticDisperse, _CausticDetile * 0.00);
                    caustic = (cF * wF + cM * wM + cC * wC) / max(wF + wM + wC, 1e-4);
                }
                else if (_CausticMode > 0.5)
                {
                    float2 cp = wp.xz * _CausticsScale;
                    float c1 = sin(cp.x * 5.3 + t * 1.05 * _CausticsSpeed + sin(cp.y * 3.9 - t * 0.75));
                    float c2 = sin(cp.y * 4.7 - t * 0.85 * _CausticsSpeed + sin(cp.x * 4.1 + t * 0.63));
                    caustic = pow(saturate(1.0 - abs(c1 * c2)), 3.0).xxx;
                }
                // ⚠ 焦散**乘性打在池底**：亮网处的底更亮、网眼更暗。
                //   纯加法会被已经很亮的白色池底削平（本轮实测：加法 A/B 最大通道差只 23，几乎看不出网）。
                float3 bottomLit = bottom * (1.0 + caustic * _CausticsStrength * 1.3 * gradMod);

                // 清水隔层：池深 1.35m 的吸收把池底压向水色（Beer–Lambert）
                float see = exp(-_FloorDepth * _Absorb);
                float3 body = lerp(_WaterColor.rgb, bottomLit * (_WaterColor.rgb * 1.35 + 0.08), see);
                // 水柱里再补一点散射亮，避免"只有底亮、水不亮"
                body += _SunColor.rgb * caustic * _CausticsStrength * 0.22 * gradMod;

                // ── ③ 反射：lerp 替换（D-1），泳池克制 ──
                float fres = pow(1.0 - saturate(dot(V, N)), _FresnelPow);
                float reflW = saturate(fres * _SkyReflect);
                body = lerp(body, _SkyColor.rgb, reflW);

                // ── ④ 太阳高光 ──
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(N, H)), _SunGloss) * _SunStrength;
                body += _SunColor.rgb * spec * L.color * 0.35;

                // ── 波峰白沫：涟漪峰顶淡淡一线 ──
                float crest = saturate(h * 2.4 - 0.55) * _CrestFoam;
                body = lerp(body, float3(0.95, 0.98, 0.98), crest);

                // ── 调试 ──
                if (_DebugMode > 1.5) return half4(saturate(gradMag * 8), 0, 0, 1);
                if (_DebugMode > 0.5) return half4(saturate(h * 2 + 0.5), 0, 0, 1);

                // 水线接缝：网格贴壁边缘的折射采样 clamp 出一圈亮线，
                // 边缘 0.15m 内 alpha 渐隐让水"融"进池壁
                float2 eDist = min(wuv, 1.0 - wuv);
                float edgeFade = saturate(min(eDist.x, eDist.y) / 0.03);
                edgeFade = edgeFade * edgeFade * (3.0 - 2.0 * edgeFade);
                float alpha = saturate(_Opacity + fres * 0.55) * lerp(0.25, 1.0, edgeFade);
                return half4(body, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
