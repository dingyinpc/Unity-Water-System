// ============================================================================
// Water_Pond.shader — 一潭静水
//
// 与 Water_Advanced.shader（"浪漫化的池子里塞了大洋的波浪统计"）刻意不同：
//
//   Water_Advanced                Water_Pond
//   ───────────────────────────   ──────────────────────────────
//   6 波 λ 1.2~12.6m              3 波 λ 1.5~3.9m
//   A 0.12~0.28 m（涌）            A 0.006~0.014 m（细波）
//   DX11 曲面细分                  无细分（1cm 位移细分是浪费，全靠法线）
//   反射 = 双色天空渐变            反射 = 平面镜像 RT（视差正确）+ 天空兜底
//
//   核心差别：这一版**会照出岸上的石头和树**。人眼判断"这是不是一潭水"的第一
//   线索就是倒影，不是波浪。
//
// 纪律（来自本项目踩过的坑，见《05》A-1）：
//   屏幕空间采样一律用自己传的 clipPos，绝不蹭 SV_POSITION（片元阶段那是像素坐标）。
// ============================================================================
Shader "WaterProject/Water_Pond"
{
    Properties
    {
        // ── 颜色 ──
        _DeepColor        ("深水色", Color) = (0.020, 0.150, 0.160, 1)
        _ShallowColor     ("浅水色", Color) = (0.150, 0.430, 0.400, 1)
        _FoamColor        ("泡沫色", Color) = (0.92, 0.97, 0.97, 1)
        _SkyZenith        ("天空顶部色", Color) = (0.10, 0.28, 0.40, 1)
        _SkyHorizon       ("天空水平色", Color) = (0.55, 0.70, 0.85, 1)
        _SunColor         ("太阳高光色", Color) = (1.0, 0.95, 0.84, 1)

        // ── 深度 ──
        _Absorb           ("吸收系数（按池深标定！）", Float) = 0.9
        _Opacity          ("整体不透明度", Float) = 0.82
        _MaxDepth         ("归一化最大水深(米)", Float) = 1.6

        // ── 水底散射 RayMarch（P2-1 / 参考 S6 C11）──
        _WaterScatter     ("水体散射色", Color) = (0.34, 0.70, 0.78, 1)
        _ScatterMode      ("体散射 0关/1解析(旧)/2RayMarch", Float) = 2
        _ScatterSteps     ("RayMarch 步数", Range(1, 32)) = 10
        _ScatterMaxDist   ("RayMarch 最大光程(米)", Float) = 6
        _ScatterSigma     ("消光系数倍率(×_Absorb)", Float) = 1.0
        _ScatterGain      ("RayMarch 散射增益", Float) = 1.4
        _ScatterStrength  ("体散射强度", Range(0, 1)) = 0.35

        // ── 细波（几何）──
        _WaveAmp          ("几何波幅(米)", Float) = 0.010
        _WaveScale        ("波频率倍率", Float) = 1.0
        _WaveSpeed        ("波速倍率", Float) = 0.55

        // ── 表面皮肤（法线，潭水的主信号）──
        _DetailStrength   ("细节法线强度", Float) = 0.55
        _DetailScale      ("细节法线尺度", Float) = 2.6
        _DetailFlow       ("细节流动速度", Float) = 0.10
        _RippleStrength   ("毛细涟漪强度", Float) = 0.30
        _RippleScale      ("毛细涟漪尺度", Float) = 9.0

        // ── 折射 ──
        _RefractStrength  ("折射偏移", Float) = 0.12
        _MaxUVOffset      ("折射偏移上限", Range(0, 0.1)) = 0.012
        _Eta              ("折射率", Float) = 1.33
        _DepthSharp       ("深度保护锐度", Float) = 4.0

        // ── 反射 ──
        _PlanarRef        ("平面反射RT (R)", 2D) = "black" {}
        _PlanarStrength   ("平面反射强度", Range(0, 2)) = 1.0
        _ReflDistort      ("倒影扭曲(沿法线xz)", Float) = 0.035
        _ReflFade         ("远处倒影淡出(米)", Float) = 45.0
        _FresnelPow       ("菲涅尔指数", Float) = 4.0
        _SkyReflect       ("天空反射权重", Range(0, 2)) = 0.9

        // ── 高光 ──
        _SpecPower        ("高光锐度", Float) = 320.0
        _SpecStrength     ("高光强度", Float) = 1.1
        _SunGloss         ("太阳反光锐度", Float) = 260.0
        _SunStrength      ("太阳反光强度", Float) = 0.9

        // ── 泡沫 ──
        _CrestJ           ("浪尖压缩阈值 J", Float) = 0.55
        _CrestSoft        ("浪尖柔边", Float) = 0.30
        _CrestFoam        ("浪尖泡沫", Float) = 0.10
        _ShoreFoam        ("岸边泡沫", Float) = 0.75
        _ShoreWidth       ("岸边泡沫带宽(米)", Float) = 0.55

        // ── 环境 ──
        _AmbientStrength  ("环境光强度", Float) = 0.40
        _DebugMode        ("调试 0关 1平面反射 2深度/菲涅尔 3扭曲", Range(0,3)) = 0
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
            // P2-1：体散射 RayMarch 在路径采样点取主光阴影
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _FoamColor, _SkyZenith, _SkyHorizon, _SunColor;
                float  _Absorb, _Opacity, _MaxDepth;
                float  _WaveAmp, _WaveScale, _WaveSpeed;
                float  _DetailStrength, _DetailScale, _DetailFlow, _RippleStrength, _RippleScale;
                float  _RefractStrength, _MaxUVOffset, _Eta, _DepthSharp;
                float  _PlanarStrength, _ReflDistort, _ReflFade, _FresnelPow, _SkyReflect;
                float  _SpecPower, _SpecStrength, _SunGloss, _SunStrength;
                float  _CrestJ, _CrestSoft, _CrestFoam, _ShoreFoam, _ShoreWidth;
                float  _AmbientStrength, _DebugMode;
                float4 _WaterScatter;
                float  _ScatterMode, _ScatterSteps, _ScatterMaxDist, _ScatterSigma, _ScatterGain, _ScatterStrength;
            CBUFFER_END

            // ── 水底散射 RayMarch [S6 / C11]（与 Water_Advanced 同款实现）──
            float3 VolumeScatterRM(float3 surfPos, float2 screenUV, float3 V, Light L,
                                   float sigmaT, int steps, float maxDist, out float pathLenOut)
            {
                float rawDepth = SampleSceneDepth(screenUV);
                float3 endPos = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                float3 delta = endPos - surfPos;
                float pathLen = min(length(delta), maxDist);
                pathLenOut = pathLen;
                if (pathLen <= 1e-4) return float3(0, 0, 0);

                float N = (float)steps;
                float seg = pathLen / N;
                float phase = 0.5 + 0.5 * dot(V, L.direction);
                float sunH  = saturate(0.5 + L.direction.y);

                float3 acc = 0;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float t = ((float)i + 0.5) / N;
                    float3 p = surfPos + delta * t;
                    float d  = pathLen * t;
                    float Tr = exp(-d * sigmaT);
                    float shadow = GetMainLight(TransformWorldToShadowCoord(p)).shadowAttenuation;
                    acc += Tr * shadow * seg;
                }
                return acc * L.color * phase * sunH;
            }

            // 由 PondPlanarReflection 每帧写入：世界坐标 → 镜像相机裁剪空间
            float4x4 _WorldToReflClip;
            TEXTURE2D(_PlanarRef); SAMPLER(sampler_PlanarRef);

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;   // 光栅化用
                float4 clipPos    : TEXCOORD0;     // ★ 屏幕空间采样必须用这个
                float3 worldPos   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  crestJ     : TEXCOORD3;
            };

            // ───────────── 三条短波：fetch 受限的潭，不是大洋涌 ─────────────
            // λ = 2π/ω → 3.9m / 2.4m / 1.5m；A 6~14mm。
            // 一个 16m 的潭不可能有 12m 波长的浪，这是 Water_Advanced 最失真之处。
            struct WaveOut { float3 pos; float3 normal; float J; };

            WaveOut SmallWaves(float3 wp, float t)
            {
                float3 P = wp;
                float dPxdx = 1, dPxdz = 0, dPzdx = 0, dPzdz = 1, dPydx = 0, dPydz = 0;

                UNITY_UNROLL for (int i = 0; i < 3; i++)
                {
                    float A, w, Q; float2 d;
                    if      (i == 0) { A = 1.00; w = 1.60; Q = 0.30; d = normalize(float2( 1.00f,  0.30f)); }
                    else if (i == 1) { A = 0.62; w = 2.60; Q = 0.35; d = normalize(float2(-0.40f,  0.92f)); }
                    else             { A = 0.40; w = 4.10; Q = 0.40; d = normalize(float2( 0.80f, -0.60f)); }

                    // 空间上的缓慢起伏，避免整池一样密
                    float m = 0.65 + 0.35 * sin(dot(wp.xz, float2(0.23, 0.17)) + t * 0.15);
                    A *= _WaveAmp * m;
                    w *= _WaveScale;
                    Q *= _WaveScale;                       // 保持 QωA < 1，永不卷浪

                    float phase = dot(wp.xz, d) * w + (float)i * 2.1 - t * w * _WaveSpeed;
                    float c = cos(phase), s = sin(phase);

                    P.x -= Q * A * d.x * c;
                    P.z -= Q * A * d.y * c;
                    P.y += A * s;

                    float wcx = w * c;
                    dPxdx -= Q * A * d.x * d.x * wcx;   dPxdz -= Q * A * d.x * d.y * wcx;
                    dPzdx -= Q * A * d.y * d.x * wcx;   dPzdz -= Q * A * d.y * d.y * wcx;
                    dPydx += A * d.x * wcx;             dPydz += A * d.y * wcx;
                }

                float3 T = float3(dPxdx, dPydx, dPzdx);
                float3 B = float3(dPxdz, dPydz, dPzdz);
                WaveOut o;
                o.pos    = P;
                o.normal = normalize(cross(B, T));
                o.J      = dPxdx * dPzdz - dPxdz * dPzdx;
                return o;
            }

            // ───────────── 表面皮肤：潭水 90% 的观感在这里 ─────────────
            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 梯度用前向差分，不用解析导数 —— 解析导数在格边界归零会画成棋盘格（《05》D-9）
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
            float ValueNoise(float2 p)

            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash12(i), b = Hash12(i + float2(1,0));
                float c = Hash12(i + float2(0,1)), e = Hash12(i + float2(1,1));
                float va = dot(float2(cos(a*6.28318), sin(a*6.28318)), f);
                float vb = dot(float2(cos(b*6.28318), sin(b*6.28318)), f - float2(1,0));
                float vc = dot(float2(cos(c*6.28318), sin(c*6.28318)), f - float2(0,1));
                float vd = dot(float2(cos(e*6.28318), sin(e*6.28318)), f - float2(1,1));
                return lerp(lerp(va, vb, u.x), lerp(vc, vd, u.x), u.y);
            }

            float2 NoiseSlope(float2 xz, float scale, float2 flow)
            {
                float2 q = xz * scale + flow;
                const float eps = 0.25;
                float n0 = ValueNoise(q);
                return float2(ValueNoise(q + float2(eps, 0)) - n0,
                              ValueNoise(q + float2(0, eps)) - n0) / eps;
            }

            // 两层：中频缓波 + 高频毛细涟漪。没有低频大平片 —— 那正是"整块一起亮"的元凶
            // fineW 把高频层单独拉出一个旋钮：掠射时它必须衰减，见片元里的注释。
            float3 SkinNormal(float3 wp, float t, float fineW)
            {
                float2 mid   = NoiseSlope(wp.xz, _DetailScale, float2(t * _DetailFlow, t * _DetailFlow * 0.7));
                float2 fine  = NoiseSlope(wp.xz, _RippleScale, float2(-t * _DetailFlow * 1.6, t * _DetailFlow * 1.2));
                float2 total = mid * _DetailStrength + fine * (_RippleStrength * fineW);
                return normalize(float3(-total.x, 1.0, -total.y));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float t = _Time.y;

                WaveOut w = SmallWaves(wp, t);
                o.positionCS = TransformWorldToHClip(w.pos);
                o.clipPos    = o.positionCS;      // ★ 见文件头纪律
                o.worldPos   = w.pos;
                o.normalWS   = w.normal;          // 几何解析法线；皮肤在片元里再叠
                o.crestJ     = w.J;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 worldPos = IN.worldPos;
                float t = _Time.y;

                // ⚠ 皮肤法线必须**逐像素**算。水面网格顶点间距 0.3~0.6m，而涟漪噪声的
                //    特征尺度约 0.1m —— 放到顶点里算再插值会严重欠采样，
                //    整个潭面会裂成一圈圈明暗相间的"风车"扇区。
                float3 V = normalize(_WorldSpaceCameraPos - worldPos);

                // 水面是平面 ⇒ |V.y| 越小就是越远、越掠射。此时 1/_RippleScale≈0.18m 的
                // 毛细格在屏幕上只剩 1~2 px，前向差分梯度直接混叠成一排排等距暗纹
                // （"灯芯绒"）。所以高频层按视角衰减：脚下全量、远处只剩中频缓波。
                float fineW = lerp(0.20, 1.0, smoothstep(0.05, 0.40, abs(V.y)));
                float3 skin = SkinNormal(worldPos, t, fineW);

                float3 N = normalize(lerp(normalize(IN.normalWS), skin, 0.6));
                N = normalize(lerp(N, ShimmerN(worldPos.xz, t, 2.4), 0.55));

                // ★ 屏幕空间 UV 用 clipPos，不用 SV_Position
                float4 sp = IN.clipPos * 0.5;
                sp.xy = float2(sp.x, sp.y * _ProjectionParams.x) + sp.w;
                float2 screenUV = sp.xy / sp.w;

                // ── 水深 ──
                // gap 是【视深】。这里显式换算出垂直水深 vd 供泡沫与吸收使用（《05》D-2/D-3）
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterDepth = IN.clipPos.w;
                float gap = max(sceneDepth - waterDepth, 0.0);
                float vd  = gap * max(abs(V.y), 0.08);
                float depthNorm = saturate(vd / max(_MaxDepth, 0.1));
                float depthMix  = 1.0 - exp(-gap * _Absorb);

                float fres = pow(1.0 - saturate(dot(V, N)), _FresnelPow);

                // ── ① 底下：水自身颜色 × 光照 ──
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(N, mainLight.direction));
                half3 ambient = SampleSH(N);

                float3 waterBase = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthMix);
                float3 col = waterBase * (ambient * _AmbientStrength + mainLight.color * ndl);

                // 浅滩：阳光打穿到沙底再返上来
                float trans = exp(-gap * _Absorb * 0.6);
                col += _ShallowColor.rgb * (1.0 - trans) * 0.35;

                // ── 体散射（P2-1 / 参考 S6 C11）：0关 / 1解析(旧) / 2RayMarch ──
                if (_ScatterMode > 1.5)
                {
                    float pathLen;
                    float3 sc = VolumeScatterRM(worldPos, screenUV, V, mainLight,
                                                max(_Absorb, 1e-3) * _ScatterSigma,
                                                (int)clamp(_ScatterSteps, 1.0, 32.0),
                                                _ScatterMaxDist, pathLen);
                    // 有界混合（不是 col +=）：避免暗场景里水被顶成发光板
                    float pathW = saturate(pathLen / max(_ScatterMaxDist, 1e-3)) * 0.6 + 0.4;
                    float w = saturate(length(sc) * _ScatterGain) * _ScatterStrength * pathW;
                    col = lerp(col, _WaterScatter.rgb * (0.6 + 0.8 * saturate(sc)), w);
                }
                else if (_ScatterMode > 0.5)
                {
                    // 解析近似（旧基线）：视向朝太阳更亮、深水衰减
                    float viewSun = saturate(dot(V, mainLight.direction)) * 0.5 + 0.5;
                    col += _WaterScatter.rgb * (0.30 + 0.70 * viewSun) * 0.35 * (0.35 + 0.65 * trans);
                }

                // ── ② 折射：把水底搬上来（浅水才搬）──
                float3 refr = refract(-V, N, _Eta);
                if (dot(refr, refr) < 1e-5) refr = -V;
                float2 refrDir = normalize(refr.xy + 1e-6);
                float2 uvOff = refrDir * _RefractStrength * (0.5 / max(abs(refr.z), 0.05));
                uvOff = clamp(uvOff, -_MaxUVOffset, _MaxUVOffset);
                float depthBlend = 1.0 - exp(-gap * _DepthSharp);
                float2 refrUV = screenUV + uvOff * depthBlend * (1.0 - fres * 0.75);
                col = lerp(col, SampleSceneColor(refrUV), (1.0 - fres) * (1.0 - depthMix) * 0.62);

                // ── ③ 上面：平面倒影（这一版的灵魂）──
                float3 reflDir = reflect(-V, N);
                reflDir.y = max(reflDir.y, 0.02);           // 别反射到水面以下

                float4 rclip = mul(_WorldToReflClip, float4(worldPos, 1.0));
                // w<=0 = 该点落在镜像相机背后，除法会翻符号、max() 会把负 w 夹成极小正数让 UV 爆掉。
                // 显式判掉，让它退回天空兜底。
                float inFront = step(1e-4, rclip.w);
                float2 ruv = rclip.xy / max(abs(rclip.w), 1e-4) * 0.5 + 0.5;
                // 扭曲量随掠射角增大：远处倒影被波纹打碎得更厉害，近处相对干净
                float distort = _ReflDistort * (0.35 + 0.65 * fres);
                ruv += reflDir.xz * distort;

                float dist = distance(_WorldSpaceCameraPos, worldPos);
                float fade = saturate(1.0 - dist / max(_ReflFade, 1.0)) * inFront;
                float3 planar = SAMPLE_TEXTURE2D(_PlanarRef, sampler_PlanarRef, ruv).rgb;
                float3 planarCol = lerp(_SkyHorizon.rgb, planar, fade);   // 远处/背后没东西可倒 → 退回天空

                // 天空渐变兜底：掠射取地平线色（亮），低头取天顶色（暗）
                float3 skyCol = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, saturate(reflDir.y));

                float3 reflection = lerp(skyCol, planarCol, saturate(_PlanarStrength));
                // ★ 反射是【替换】不是【累加】。`col += refl*fres` 永远只能变亮，于是映深色山坡
                //    的那片水面反而被洗成 pale —— 恰恰是掠射区、反射最该显形的地方。
                //    掠射时水面该"变成镜子"，能量守恒写法是 lerp。
                float reflW = saturate(fres * _SkyReflect);
                col = lerp(col, reflection, reflW);
                // 太阳反光带留在累加：碎闪是加光，且拆出来才不会在 planar 接管时被上面那条 lerp 掉
                col += _SunColor.rgb * pow(saturate(dot(reflDir, mainLight.direction)), _SunGloss)
                     * _SunStrength * saturate(0.25 + reflW);

                // ── ④ 表面：高光。lerp 不用 +=（无界累加会削平成死白，《05》D-1）──
                float3 H = normalize(mainLight.direction + V);
                float spec = saturate(pow(saturate(dot(N, H)), _SpecPower) * _SpecStrength);
                col = lerp(col, _SunColor.rgb, spec);

                // ── ⑤ 表面：泡沫。岸边用【垂直】水深，带宽才与视角无关 ──
                float crestFoam = saturate((_CrestJ - IN.crestJ) / max(_CrestSoft, 1e-4));
                float shoreFoam = saturate(1.0 - vd / max(_ShoreWidth, 0.05)) * (1.0 - fres);
                float foam = saturate(crestFoam * _CrestFoam + shoreFoam * _ShoreFoam);
                col = lerp(col, _FoamColor.rgb, foam);

                // ── 调试：先看通道通不通，再解释数值（《06》附 B）──
                if (_DebugMode > 2.5)   // R=扭曲后的反射UV  G=原始UV  B=fres
                    return half4(saturate(ruv.x), saturate(screenUV.y), saturate(fres), 1.0);
                if (_DebugMode > 1.5)   // R=gap G=depthMix B=fres
                    return half4(saturate(gap / _MaxDepth), saturate(depthMix), saturate(fres), 1.0);
                if (_DebugMode > 0.5)   // 只看平面反射：灰=没采到
                    return half4(planar, 1.0);

                float alpha = saturate(_Opacity + fres * 0.12 - foam * 0.2);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
