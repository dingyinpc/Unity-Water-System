// ============================================================================
// Water_Advanced.shader — Unity URP 高级水体
// 复刻目标：抖音「雨鱼鱼雨雨雨」unity水体shader（参考地平线零之曙光水体现路）
// 八项技术对照：
//   [1] 自适应曲面细分   → Hull/Domain + 距离判据
//   [2] 格斯特纳波       → GerstnerEval（位移+解析法线+压缩度J）
//   [3] 屏幕空间折射     → _CameraOpaqueTexture 偏移采样
//   [4] 折射深度保护     → 深度差因子 + 菲涅尔收敛 + 偏移上限
//   [5] 动态水体交互     → _InteractionTex（交互RT，逻辑层写入）
//   [6] RNW 法线混合     → 解析法线 + 程序化细节法线 按距离加权混合
//   [7] 程序化 Ripple    → 径向衰减正弦环（_RippleSources）
//   [8] 浪尖效果         → Gerstner 压缩度 J → 白沫（+岸边/交互泡沫）
// ============================================================================
Shader "WaterProject/Water_Advanced"
{
    Properties
    {
        // ── 颜色 ──
        _DeepColor        ("深水色", Color) = (0.02, 0.12, 0.16, 1)
        _ShallowColor     ("浅水色", Color) = (0.10, 0.38, 0.34, 1)
        _FoamColor        ("泡沫色", Color) = (0.95, 0.97, 0.98, 1)
        _SkyZenith        ("天空顶部色", Color) = (0.32, 0.55, 0.80, 1)
        _SkyHorizon       ("天空水平色", Color) = (0.72, 0.83, 0.90, 1)
        _SunColor         ("太阳高光色", Color) = (1.0, 0.93, 0.75, 1)
        _Absorb           ("吸收系数（越深越暗）", Float) = 0.9
        _Opacity          ("整体不透明度", Float) = 0.97
        // ── 透光感（Beer–Lambert 透射 + 阳光穿透 + 体积散射）──
        _WaterScatter     ("水体散射色", Color) = (0.42, 0.75, 0.88, 1)
        _ScatterStrength  ("体积散射强度", Float) = 0.45
        _SunTransmission  ("阳光透射强度", Float) = 0.9
        _TransGloss       ("透射高光锐度", Float) = 48.0

        // ── 格斯特纳波 [2] ──
        _WaveCount        ("波数量", Float) = 4
        _Waves            ("波参数 (A, ω, Q, -)", Vector) = (0,0,0,0)
        _WaveDirs         ("波方向 (dx, dz, 相位, -)", Vector) = (0,0,0,0)
        _WaveTimeScale    ("波速倍率", Float) = 1.0

        // ── 曲面细分 [1] ──
        _MaxTess          ("最大细分因子", Float) = 12
        _TessTargetSize   ("细分目标尺寸(米)", Float) = 0.25
        _TessNear         ("细分近距", Float) = 6.0
        _TessFar          ("细分远距", Float) = 60.0

        // ── 折射 [3][4] ──
        _RefractStrength  ("折射强度", Float) = 0.9
        _Eta              ("折射率", Float) = 1.33
        _DepthSharp       ("深度保护锐度", Float) = 3.0
        _MaxUVOffset      ("折射偏移上限", Range(0, 0.3)) = 0.12

        // ── 法线混合 [6] ──
        _DetailStrength   ("细节法线强度", Float) = 3.0
        _DetailScale      ("细节法线尺度", Float) = 1.6
        _DetailStart      ("细节混合起点(距相机)", Float) = 2.0
        _DetailEnd        ("细节混合终点(距相机)", Float) = 40.0

        // ── 交互 [5] ──
        _InteractionAmp   ("交互位移幅度", Float) = 0.35
        _InteractionNorm  ("交互法线强度", Float) = 1.4
        _InteractionFoam  ("交互泡沫强度", Float) = 0.9

        // ── 浪尖/泡沫 [8] ──
        _CrestJ           ("浪尖压缩阈值 J", Float) = 0.6
        _CrestSoft        ("浪尖泡沫柔边", Float) = 0.35
        _CrestFoam        ("浪尖泡沫强度", Float) = 1.0
        _ShoreFoam        ("岸边泡沫强度", Float) = 0.8
        _FoamLift         ("泡沫抬升", Float) = 0.03

        // ── 光照 ──
        _SpecPower        ("高光锐度", Float) = 180.0
        _SpecStrength     ("高光强度", Float) = 0.55
        _SunGloss         ("太阳反光锐度", Float) = 400.0
        _SunStrength      ("太阳反光强度", Float) = 0.35
        _FresnelPow       ("菲涅尔指数", Float) = 4.0
        _AmbientStrength  ("环境光强度", Float) = 0.35
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
            #pragma vertex tessVertex
            #pragma hull hull
            #pragma domain domain
            #pragma fragment frag
            #pragma target 5.0

            #define MAX_WAVES 8
            #define MAX_RIPPLES 8

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ============================ 属性 ============================
            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _FoamColor, _SkyZenith, _SkyHorizon, _SunColor;
            float4 _WaterScatter;
            float _ScatterStrength, _SunTransmission, _TransGloss;
                float  _Absorb, _Opacity;
                float  _WaveCount, _WaveTimeScale;
                float4 _Waves[MAX_WAVES];
                float4 _WaveDirs[MAX_WAVES];
                float  _MaxTess, _TessTargetSize, _TessNear, _TessFar;
                float  _RefractStrength, _Eta, _DepthSharp, _MaxUVOffset;
                float  _DetailStrength, _DetailScale, _DetailStart, _DetailEnd;
                float  _InteractionAmp, _InteractionNorm, _InteractionFoam;
                float  _CrestJ, _CrestSoft, _CrestFoam, _ShoreFoam, _FoamLift;
                float  _SpecPower, _SpecStrength, _SunGloss, _SunStrength, _FresnelPow, _AmbientStrength;
            CBUFFER_END

            // 全局（交互系统每帧提供）
            TEXTURE2D(_InteractionTex); SAMPLER(sampler_InteractionTex);
            float4 _InteractionRect;        // (centerX, centerZ, halfW, 0)
            float4 _RippleSources[MAX_RIPPLES];
            float  _RippleCount, _RippleLife;

            // ============================ 结构体 ============================
            struct WaterAppData
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct TessControlPoint
            {
                float3 positionOS : INTERNALTESSPOS;   // 必须：tessellation 语义
                float2 uv        : TEXCOORD0;
            };

            struct TessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            struct WaterVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 worldPos   : TEXCOORD1;
                float3 viewDir    : TEXCOORD2;
            };

            // ============================ 波函数 ============================
            struct GerstnerResult
            {
                float3 pos;      // 位移后世界位置
                float3 normal;   // 解析法线
                float  J;        // 水平压缩度（1=平静，<1 压缩=浪尖）
            };

            GerstnerResult GerstnerEval(float3 worldPos, float t)
            {
                float3 P = worldPos;
                float dPxdx = 1, dPxdz = 0, dPzdx = 0, dPzdz = 1, dPydx = 0, dPydz = 0;

                int n = (int)_WaveCount;
                UNITY_UNROLL for (int i = 0; i < MAX_WAVES; i++)
                {
                    if (i >= n) break;
                    float  A = _Waves[i].x;
                    float  w = _Waves[i].y;
                    float  Q = _Waves[i].z;
                    float2 d = normalize(_WaveDirs[i].xy);
                    if (length(_WaveDirs[i].xy) < 1e-4) d = float2(1, 0);
                    float phase = dot(float2(worldPos.x, worldPos.z), d) * w + _WaveDirs[i].z - t * w * _WaveTimeScale;
                    float c = cos(phase), s = sin(phase);

                    P.x -= Q * A * d.x * c;
                    P.z -= Q * A * d.y * c;
                    P.y += A * s;

                    float wcx = w * c;
                    dPxdx -= Q * A * d.x * d.x * wcx;
                    dPxdz -= Q * A * d.x * d.y * wcx;
                    dPzdx -= Q * A * d.y * d.x * wcx;
                    dPzdz -= Q * A * d.y * d.y * wcx;
                    dPydx += A * d.x * wcx;
                    dPydz += A * d.y * wcx;
                }

                float3 T = float3(dPxdx, dPydx, dPzdx);
                float3 B = float3(dPxdz, dPydz, dPzdz);

                GerstnerResult r;
                r.pos    = P;
                r.normal = normalize(cross(B, T));
                r.J      = dPxdx * dPzdz - dPxdz * dPzdx;
                return r;
            }

            // ============================ 程序化噪声法线 [6] ============================
            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // IQ 梯度噪声（带回解析导数）
            float GradientNoise(float2 p, out float2 grad)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float ga = Hash12(i);
                float gb = Hash12(i + float2(1, 0));
                float gc = Hash12(i + float2(0, 1));
                float gd = Hash12(i + float2(1, 1));

                float va = dot(float2(cos(ga * 6.28318), sin(ga * 6.28318)), f);
                float vb = dot(float2(cos(gb * 6.28318), sin(gb * 6.28318)), f - float2(1, 0));
                float vc = dot(float2(cos(gc * 6.28318), sin(gc * 6.28318)), f - float2(0, 1));
                float vd = dot(float2(cos(gd * 6.28318), sin(gd * 6.28318)), f - float2(1, 1));

                float2 du = 6.0 * f * (1.0 - f);
                float n = lerp(lerp(va, vb, u.x), lerp(vc, vd, u.x), u.y);
                grad = float2(du.x * (vb - va) + du.y * (vd - vc),
                              du.y * (vc - va) + du.x * (vd - vb));
                return n;
            }

            float3 DetailNormal(float3 worldPos, float t)
            {
                float2 p = worldPos.xz * _DetailScale;
                // 流动感：细节法线以 ~0.35-0.45 m/s 世界速度平移（原 0.12 几乎静止）
                p += float2(t * 0.42, t * 0.33);
                float2 g;
                GradientNoise(p, g);
                // 反向抖动细节，制造多向流动
                float2 p2 = worldPos.xz * _DetailScale * 2.1 - float2(t * 0.33, t * 0.42);
                float2 g2;
                GradientNoise(p2, g2);
                float2 total = g * 0.7 + g2 * 0.5;
                float3 n = float3(-total.x * _DetailStrength, 1.0, -total.y * _DetailStrength);
                return normalize(n);
            }

            // ============================ 程序化 Ripple [7] ============================
            float ProceduralRipple(float3 worldPos, float t)
            {
                float sum = 0.0;
                UNITY_UNROLL for (int i = 0; i < MAX_RIPPLES; i++)
                {
                    if (i >= (int)_RippleCount) break;
                    float4 src = _RippleSources[i];
                    float2 delta = worldPos.xz - src.xy;
                    float r = length(delta);
                    float age = t - src.w;
                    if (age < 0.0 || age > _RippleLife) continue;
                    float lifeN = age / max(_RippleLife, 1e-4);
                    float k = 6.28318 * 3.0;
                    float wSpeed = 2.2;
                    float amp = 0.14 * (1.0 - lifeN);
                    sum += sin(k * r - wSpeed * age) * exp(-r * 1.2) * amp;
                    sum += sin(k * 1.6 * r - wSpeed * 0.7 * age) * exp(-r * 0.8) * amp * 0.5;
                }
                return sum;
            }

            // ============================ 顶点/细分 [1][2][5][7] ============================
            TessControlPoint tessVertex(WaterAppData v)
            {
                TessControlPoint o;
                o.positionOS = v.positionOS.xyz;
                o.uv = v.uv;
                return o;
            }

            // 近密远疏 + 按基边长度自适应的细分因子 [1]
            // factor ≈ (基边长度 / 目标尺寸)，近处满级、远处衰减到 1——任何网格分辨率都收敛到 ~_TessTargetSize。
            float TessFactorForEdge(float3 a, float3 b, float3 cam)
            {
                float d = distance((a + b) * 0.5, cam);
                float k = saturate((d - _TessNear) / max(_TessFar - _TessNear, 1e-4));
                float sizeFactor = distance(a, b) / max(_TessTargetSize, 1e-4);
                float f = sizeFactor * (1.0 - k);
                return max(1.0, ceil(min(f, _MaxTess)));
            }

            TessFactors hullConstant(InputPatch<TessControlPoint, 3> patch, uint pid : SV_PrimitiveID)
            {
                TessFactors f;
                float3 w0 = TransformObjectToWorld(patch[0].positionOS);
                float3 w1 = TransformObjectToWorld(patch[1].positionOS);
                float3 w2 = TransformObjectToWorld(patch[2].positionOS);
                float3 cam = _WorldSpaceCameraPos;

                f.edge[0] = TessFactorForEdge(w0, w1, cam);
                f.edge[1] = TessFactorForEdge(w1, w2, cam);
                f.edge[2] = TessFactorForEdge(w2, w0, cam);
                f.inside  = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("hullConstant")]
            [outputcontrolpoints(3)]
            [maxtessfactor(64.0)]
            TessControlPoint hull(InputPatch<TessControlPoint, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            WaterVaryings domain(TessFactors factors, const OutputPatch<TessControlPoint, 3> patch, float3 bary : SV_DomainLocation)
            {
                WaterVaryings o;

                float3 positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
                float2 uv = patch[0].uv * bary.x + patch[1].uv * bary.y + patch[2].uv * bary.z;

                float3 worldPos = TransformObjectToWorld(positionOS);
                float t = _Time.y;

                // 格斯特纳位移 [2]
                GerstnerResult gr = GerstnerEval(worldPos, t);

                // 交互位移 [5]
                float2 iuv = (worldPos.xz - _InteractionRect.xy) / (max(_InteractionRect.z * 2.0, 1e-4)) + 0.5;
                float4 inter = SAMPLE_TEXTURE2D_LOD(_InteractionTex, sampler_InteractionTex, iuv, 0);
                float2 interN = (inter.xy - 0.5) * 2.0;   // 编码的法线扰动
                float interA = inter.a;

                // 程序化 ripple 位移（微小）[7]
                float rip = ProceduralRipple(worldPos, t);

                gr.pos.y += interN.y * _InteractionAmp * interA * 0.4 + rip;
                gr.pos.x += interN.x * _InteractionAmp * interA * 0.4 * 0.5;

                o.positionCS = TransformWorldToHClip(gr.pos);
                o.uv = uv;
                o.worldPos = gr.pos;
                o.viewDir = _WorldSpaceCameraPos - gr.pos;
                return o;
            }

            // ============================ 片段 ============================
            half4 frag(WaterVaryings IN) : SV_Target
            {
                float3 worldPos = IN.worldPos;
                float3 V = normalize(IN.viewDir);
                float t = _Time.y;

                // ── 法线：解析 + 细节 + 交互 [2][5][6] ──
                GerstnerResult gr = GerstnerEval(worldPos, t);     // 重算法线与压缩度
                float3 N = gr.normal;

                float distCam = distance(worldPos, _WorldSpaceCameraPos);
                float detailW = saturate((distCam - _DetailStart) / max(_DetailEnd - _DetailStart, 1e-4));
                float3 nDetail = DetailNormal(worldPos, t);
                N = normalize(lerp(N, nDetail, detailW));          // [6] RNW 法线混合

                // 交互法线 [5]
                float2 iuv = (worldPos.xz - _InteractionRect.xy) / (max(_InteractionRect.z * 2.0, 1e-4)) + 0.5;
                float4 inter = SAMPLE_TEXTURE2D(_InteractionTex, sampler_InteractionTex, iuv);
                float2 interN = (inter.xy - 0.5) * 2.0;
                float interA = inter.a;
                // 把交互法线转到世界（近似：xz 扰动）
                float3 interNWorld = normalize(float3(interN.x, 1.0, interN.y) - float3(0, 1, 0));
                N = normalize(N + interNWorld * interN.x * _InteractionNorm * interA + float3(0,0,interN.y) * _InteractionNorm * interA);

                // ── 屏幕空间折射 [3] ──
                float4 posCS = IN.positionCS;
                float4 sp = posCS * 0.5;
                sp.xy = float2(sp.x, sp.y * _ProjectionParams.x) + sp.w;
                float2 screenUV = sp.xy / sp.w;

                // 深度差 → 折射权重 [4]
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterDepth = posCS.w;                        // 水面视深
                float gap = max(sceneDepth - waterDepth, 0.0);
                float depthBlend = 1.0 - exp(-gap * _DepthSharp);

                float fres = pow(1.0 - saturate(dot(V, N)), _FresnelPow);

                float3 refr = refract(-V, N, _Eta);
                if (dot(refr, refr) < 1e-5) refr = -V;            // 全反射兜底
                float2 refrDir = normalize(refr.xy + 1e-6);
                float2 uvOff = refrDir * _RefractStrength * (0.5 / max(abs(refr.z), 0.05));
                uvOff = clamp(uvOff, -_MaxUVOffset, _MaxUVOffset); // 偏移上限 [4]

                float2 refrUV = screenUV + uvOff * depthBlend * (1.0 - fres * 0.75);
                float3 refrCol = SampleSceneColor(refrUV);
                float3 sceneCol = SampleSceneColor(screenUV);

                // ── 主光/环境 ──
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(N, mainLight.direction));
                half3 ambient = SampleSH(half3(N));

                // ── 水面基色按深度吸收 ──
                float3 waterBase = lerp(_ShallowColor, _DeepColor, 1.0 - exp(-max(gap, 0.0) * _Absorb));
                float3 col = waterBase * (ambient * _AmbientStrength + mainLight.color * ndl);

                // ── 天空反射（菲涅尔）[3] ──
                float3 reflDir = reflect(-V, N);
                float3 skyCol = lerp(_SkyHorizon, _SkyZenith, saturate(reflDir.y * 0.5 + 0.5));
                // 太阳闪烁与场景主光方向对齐（不硬编码方向）
                skyCol += _SunColor * pow(saturate(dot(reflDir, mainLight.direction)), _SunGloss) * _SunStrength;
                col += skyCol * fres * (1.0 - interA * 0.4);

                // ── 透光感 [Beer–Lambert]：阳光沿反射反方向穿透水体，随深度指数衰减 ──
                float trans = exp(-max(gap, 0.0) * _Absorb * 0.6);          // 透射率 0浅→1深
                float sunTrans = pow(saturate(dot(reflDir, mainLight.direction)), _TransGloss);
                col += _SunColor * _SunTransmission * sunTrans * (0.2 + 0.8 * trans);

                // ── 透光感：体积散射（水体自身泛光，视向朝向太阳更亮，深水衰减）──
                float viewSun = saturate(dot(V, mainLight.direction)) * 0.5 + 0.5;
                col += _WaterScatter.rgb * (0.30 + 0.70 * viewSun) * _ScatterStrength * (0.35 + 0.65 * trans);

                // 浅滩（gap 小）额外泛光，光能透到底的提亮
                col += _ShallowColor * _ScatterStrength * 0.28 * (1.0 - trans);

                // ── 高光 ──
                float3 H = normalize(mainLight.direction + V);
                col += mainLight.color * pow(saturate(dot(N, H)), _SpecPower) * _SpecStrength * (1.0 - fres * 0.5);

                // ── 折射层混合：浅水看湖底（沙色），深水收敛到深水色 ──
                float depthMix = 1.0 - exp(-max(gap, 0.0) * _Absorb);   // 0浅→1深
                col = lerp(col, refrCol, (1.0 - fres) * (1.0 - depthMix) * 0.58);
                col = lerp(col, sceneCol, (1.0 - fres) * (1.0 - _Opacity));

                // ── 泡沫 [8]：浪尖(J) + 岸边(深度差) + 交互 ──
                float crestFoam  = saturate((_CrestJ - gr.J) / max(_CrestSoft, 1e-4));
                float shoreFoam  = saturate((1.0 - gap * 0.6) * (1.0 - fres) * 0.6);
                float interFoam  = interA * _InteractionFoam;
                float foam = saturate(crestFoam * _CrestFoam + shoreFoam * _ShoreFoam + interFoam);

                // 浪尖泡沫抬升的亮边
                float3 foamCol = _FoamColor * (1.0 + crestFoam * 0.5);
                col = lerp(col, foamCol, foam);

                // 浪尖几何增强：在顶点处已由 Gerstner 水平位移自然卷曲 [8]
                float alpha = saturate(_Opacity + fres * 0.1 - foam * 0.2);
                return half4(col + _FoamColor * interFoam * 0.4, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}