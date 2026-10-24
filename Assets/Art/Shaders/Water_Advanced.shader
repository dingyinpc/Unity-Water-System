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
        // ── 水底散射 RayMarch（P2-1 / 参考 S6 C11）──
        _ScatterMode      ("体散射 0关/1解析(旧)/2RayMarch", Float) = 2
        _ScatterSteps     ("RayMarch 步数", Range(1, 32)) = 10
        _ScatterMaxDist   ("RayMarch 最大光程(米)", Float) = 12
        _ScatterSigma     ("消光系数倍率(×_Absorb)", Float) = 1.0
        _ScatterGain      ("RayMarch 散射增益", Float) = 1.6

        // ── 格斯特纳波 [2] ──
        _WaveCount        ("波数量", Float) = 4
        _WaveAmpScale     ("波浪幅度倍率", Float) = 1.0
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
        _InteractionAmp   ("交互位移幅度", Float) = 0.22
        _InteractionNorm  ("交互法线强度", Float) = 1.4
        _InteractionFoam  ("交互泡沫强度", Float) = 0.45

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
        _DepthDebug       ("深度调试输出 1:R=gap G=depthMix B=fres 2:rawDepth/scene/eye 3:screenUV", Range(0,3)) = 0
        _SkyReflect       ("天空反射权重", Range(0,2)) = 1
        // ── SSR 屏幕空间反射（P0-2 / 参考 C7–C8）──
        _SSRStrength      ("SSR 反射强度", Range(0, 1)) = 0.85
        _SSRMaxDist       ("SSR 追踪最大距离(米)", Float) = 40
        _SSRSteps         ("SSR 步进数", Range(4, 48)) = 24
        _SSRThickness     ("命中厚度容差(米)", Float) = 0.5
        _SSREdgeFade      ("屏幕边缘淡出", Float) = 24
        _SSRDebug         ("SSR 调试 0关/1可信度/2SSR色", Float) = 0
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
            // 体散射 RayMarch 要在路径采样点上取主光阴影（参考 C11「结合主光阴影」）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

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
            float _ScatterMode, _ScatterSteps, _ScatterMaxDist, _ScatterSigma, _ScatterGain;
                float  _Absorb, _Opacity;
                float  _WaveCount, _WaveTimeScale, _WaveAmpScale;
                float4 _Waves[MAX_WAVES];
                float4 _WaveDirs[MAX_WAVES];
                float  _MaxTess, _TessTargetSize, _TessNear, _TessFar;
                float  _RefractStrength, _Eta, _DepthSharp, _MaxUVOffset;
                float  _DetailStrength, _DetailScale, _DetailStart, _DetailEnd;
                float  _InteractionAmp, _InteractionNorm, _InteractionFoam;
                float  _CrestJ, _CrestSoft, _CrestFoam, _ShoreFoam, _FoamLift;
                float  _SpecPower, _SpecStrength, _SunGloss, _SunStrength, _FresnelPow, _AmbientStrength, _DepthDebug, _SkyReflect;
                float  _SSRStrength, _SSRMaxDist, _SSRSteps, _SSRThickness, _SSREdgeFade, _SSRDebug;
            CBUFFER_END

            #include "SSR.hlsl"

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
                // SV_Position 在片元阶段是像素坐标而非裁剪空间，屏幕空间折射/深度必须另带一份裁剪坐标
                float4 clipPos    : TEXCOORD3;
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

                // 不规则感：跨池低频幅值调制（约 57m/34m 两种波长的正弦叠加），
                // 使波形在空间上忽强忽弱、随时间缓慢漂移，避免固定波叠加的"整齐重复"感
                float phaseM = dot(worldPos.xz * 0.11, float2(0.7, 0.3)) + t * 0.11;
                float ampMod = 0.72 + 0.28 * (sin(phaseM) * 0.6 + sin(phaseM * 1.7 + 2.1) * 0.4);

                // 六波叠加（互不调和的频率/方向/相位 → 不规则）：Σ(Q·ω·A·mod) < 1 无卷浪
                UNITY_UNROLL for (int i = 0; i < 6; i++)
                {
                    float A, w, Q; float2 d; float phase0;
                    if (i == 0) { A = 0.55f; w = 0.50f; Q = 0.40f; d = normalize(float2(1.00f, 0.18f)); phase0 = 0.9f; }
                    else if (i == 1) { A = 0.30f; w = 1.25f; Q = 0.45f; d = normalize(float2(-0.30f, 0.95f)); phase0 = 1.7f; }
                    else if (i == 2) { A = 0.13f; w = 2.40f; Q = 0.50f; d = normalize(float2(0.85f, -0.53f)); phase0 = 3.1f; }
                    else if (i == 3) { A = 0.06f; w = 4.20f; Q = 0.55f; d = normalize(float2(-0.60f, -0.80f)); phase0 = 4.2f; }
                    else if (i == 4) { A = 0.09f; w = 3.05f; Q = 0.50f; d = normalize(float2(0.62f, 0.78f)); phase0 = 5.3f; }
                    else            { A = 0.04f; w = 5.30f; Q = 0.55f; d = normalize(float2(-0.95f, 0.31f)); phase0 = 2.8f; }

                    A *= max(_WaveAmpScale, 0.0f) * ampMod;
                    // 相位速度微漂移（±10%）：波族相对滑动，进一步打散规律性
                    float speed = _WaveTimeScale * (1.0f + 0.10f * sin(phase0 * 7.0f + t * 0.05f));
                    float phase = dot(float2(worldPos.x, worldPos.z), d) * w + phase0 - t * w * speed;
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

            // 连续值噪声（Perlin 插值本体）；梯度改由差分求，保证跨格边界连续
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

                float ga = Hash12(i);
                float gb = Hash12(i + float2(1, 0));
                float gc = Hash12(i + float2(0, 1));
                float gd = Hash12(i + float2(1, 1));

                float va = dot(float2(cos(ga * 6.28318), sin(ga * 6.28318)), f);
                float vb = dot(float2(cos(gb * 6.28318), sin(gb * 6.28318)), f - float2(1, 0));
                float vc = dot(float2(cos(gc * 6.28318), sin(gc * 6.28318)), f - float2(0, 1));
                float vd = dot(float2(cos(gd * 6.28318), sin(gd * 6.28318)), f - float2(1, 1));

                return lerp(lerp(va, vb, u.x), lerp(vc, vd, u.x), u.y);
            }

            // 噪声空间前向差分斜率（原解析 grad 在格边界归零 → 每格一个朝向 = 棋盘格）
            float2 NoiseSlope(float2 worldXZ, float scale, float2 flow)
            {
                float2 q = worldXZ * scale + flow;
                const float eps = 0.25;
                float n0 = ValueNoise(q);
                float nx = ValueNoise(q + float2(eps, 0.0));
                float nz = ValueNoise(q + float2(0.0, eps));
                return float2(nx - n0, nz - n0) / eps;
            }

            float3 DetailNormal(float3 worldPos, float t)
            {
                // 流动感：细节法线以 ~0.35-0.45 m/s 世界速度平移；第二层反向抖动制造多向流动
                // 第三层高频小波：把"同朝向的大片"打碎成密集小面，碎闪才会是点状而非整块白斑
                // 低频八度从 0.7 降到 0.4：它的大平片是"整块一起亮"的元凶；能量挪给两层高频
                float2 total = NoiseSlope(worldPos.xz, _DetailScale,        float2(t * 0.42, t * 0.33)) * 0.40
                             + NoiseSlope(worldPos.xz, _DetailScale * 2.1f, -float2(t * 0.33, t * 0.42)) * 0.55
                             + NoiseSlope(worldPos.xz, _DetailScale * 5.4f, float2(t * 0.55, -t * 0.47)) * 0.50;
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
                o.clipPos = o.positionCS;
                o.uv = uv;
                o.worldPos = gr.pos;
                o.viewDir = _WorldSpaceCameraPos - gr.pos;
                return o;
            }

            // ============================ 水底散射 RayMarch [S6 / C11] ============================
            // 沿"水面点 → 屏幕深度重建的场景点"的**可见水体路径**定步长采样：
            //   每步用 Beer–Lambert 累积透射率（越深越暗），并采样**主光阴影**（波影/障碍遮挡）；
            //   散射颜色 = Σ 透射率 × 阴影 × 前向相位 × 步长。
            // ⚠ 终点用 SampleSceneDepth 重建世界坐标（精确），不用 surfPos + V*depth（V 是径向、depth 是轴向，离屏中心会偏）。
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
                float phase = 0.5 + 0.5 * dot(V, L.direction);       // 前向散射：视向朝太阳更亮
                float sunH  = saturate(0.5 + L.direction.y);          // 太阳高度调制（晨昏偏暗）

                float3 acc = 0;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float t = ((float)i + 0.5) / N;
                    float3 p = surfPos + delta * t;
                    float d  = pathLen * t;
                    float Tr = exp(-d * sigmaT);                      // 到该采样点的透射率
                    float shadow = GetMainLight(TransformWorldToShadowCoord(p)).shadowAttenuation;
                    acc += Tr * shadow * seg;
                }
                return acc * L.color * phase * sunH;
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
                N = normalize(lerp(N, ShimmerN(worldPos.xz, t, 3.0), 0.55));

                // ── 屏幕空间折射 [3] ──
                float4 posCS = IN.clipPos;
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
                // reflDir.y≈0 是掠射反射，应当取"地平线色"（亮）；旧映射 *0.5+0.5 会把它推到天顶暗色 → 低机位水面发黑发闷
                float3 skyCol = lerp(_SkyHorizon, _SkyZenith, saturate(reflDir.y));
                // ── SSR（P0-2 / 参考 C8）：conf=0 时 reflCol==skyCol ⇒ 与旧路径逐位一致，零回归 ──
                float ssrConf = 0.0;
                float3 ssrCol = TraceSSR(worldPos, reflDir, _SSRMaxDist, _SSRSteps, _SSRThickness, _SSREdgeFade, ssrConf);
                // _SSRStrength 是 SSR 的**总开关**（0=纯天空兜底，1=命中处全用场景色）：
                // 不乘它的话 A/B 菜单改了强度但画面不变（P0-2 收尾时抓到的真 bug）。
                float3 reflBase = lerp(skyCol, ssrCol, ssrConf * _SSRStrength);
                // 太阳碎闪不跟 SSR 走（《05》原文：碎闪要拆出来单独，否则会被"场景色"整条抹掉）
                float3 reflCol = reflBase + _SunColor * pow(saturate(dot(reflDir, mainLight.direction)), _SunGloss) * _SunStrength;
                col += reflCol * fres * _SkyReflect * (1.0 - interA * 0.4);

                // ── 透光感 [Beer–Lambert]：阳光沿反射反方向穿透水体，随深度指数衰减 ──
                float trans = exp(-max(gap, 0.0) * _Absorb * 0.6);          // 透射率 0浅→1深
                float sunTrans = pow(saturate(dot(reflDir, mainLight.direction)), _TransGloss);
                col += _SunColor * _SunTransmission * sunTrans * (0.2 + 0.8 * trans);

                // ── 透光感：体积散射（P2-1 / 参考 S6 C11）──
                //   _ScatterMode：0=关 / 1=解析近似(旧基线) / 2=RayMarch（逐点主光阴影 + 逐级透射累积）
                if (_ScatterMode > 1.5)
                {
                    float pathLen;
                    float3 sc = VolumeScatterRM(worldPos, screenUV, V, mainLight,
                                                max(_Absorb, 1e-3) * _ScatterSigma,
                                                (int)clamp(_ScatterSteps, 1.0, 32.0),
                                                _ScatterMaxDist, pathLen);
                    // ⚠ 用**有界混合**而不是 `col +=`：加法在暗场景里会把水顶成发光板（Pond 实测过）。
                    //   浅水路径短 → 权重小、能看穿湖底；深水路径长 → 水体色接管。
                    float pathW = saturate(pathLen / max(_ScatterMaxDist, 1e-3)) * 0.6 + 0.4;
                    float w = saturate(length(sc) * _ScatterGain) * _ScatterStrength * pathW;
                    // 散射色随主光（晨昏偏暖）微调 → 满足 C11「结合太阳高度」
                    col = lerp(col, _WaterScatter.rgb * (0.6 + 0.8 * saturate(sc)), w);
                }
                else if (_ScatterMode > 0.5)
                {
                    float viewSun = saturate(dot(V, mainLight.direction)) * 0.5 + 0.5;
                    col += _WaterScatter.rgb * (0.30 + 0.70 * viewSun) * _ScatterStrength * (0.35 + 0.65 * trans);
                }

                // 浅滩（gap 小）额外泛光，光能透到底的提亮
                col += _ShallowColor * _ScatterStrength * 0.28 * (1.0 - trans);

                // ── 高光 ── 用 lerp 而非累加：累加会把已经挺亮的水色顶过 1.0 → 整块削平的死白
                float3 H = normalize(mainLight.direction + V);
                float spec = saturate(pow(saturate(dot(N, H)), _SpecPower) * _SpecStrength);
                col = lerp(col, _SunColor.rgb, spec);

                // ── 折射层混合：浅水看湖底（沙色），深水收敛到深水色 ──
                float depthMix = 1.0 - exp(-max(gap, 0.0) * _Absorb);   // 0浅→1深
                col = lerp(col, refrCol, (1.0 - fres) * (1.0 - depthMix) * 0.58);
                col = lerp(col, sceneCol, (1.0 - fres) * (1.0 - _Opacity));

                // ── 泡沫 [8]：浪尖(J) + 岸边(深度差) + 交互 ──
                float crestFoam  = saturate((_CrestJ - gr.J) / max(_CrestSoft, 1e-4));
                // gap 是"沿视线的视深"；低机位下视线接近水平，1m 垂直水深会被换算成 ~4m 视深，
                // 泡沫带因此窄到看不见。先换算回垂直水深，带宽才与观察角度无关。
                float vd = gap * max(V.y, 0.12);
                float shoreFoam  = saturate((1.0 - vd * 1.7) * (1.0 - fres) * 0.75);
                float interFoam  = interA * _InteractionFoam;
                float foam = saturate(crestFoam * _CrestFoam + shoreFoam * _ShoreFoam + interFoam);

                // 浪尖泡沫抬升的亮边
                float3 foamCol = _FoamColor * (1.0 + crestFoam * 0.5);
                col = lerp(col, foamCol, foam);

                // 浪尖几何增强：在顶点处已由 Gerstner 水平位移自然卷曲 [8]
                // 调试1：R=gap/4  G=depthMix  B=fres —— 红绿全黑只有蓝 ⇒ 深度取不到（gap=0）
                if (_DepthDebug > 2.5)
                    return half4(saturate(screenUV.x), saturate(screenUV.y), saturate(SampleSceneDepth(screenUV)), 1.0);
                if (_DepthDebug > 1.5)
                    return half4(saturate(SampleSceneDepth(screenUV)), saturate(sceneDepth * 0.04), saturate(waterDepth * 0.04), 1.0);
                if (_DepthDebug > 0.5)
                    return half4(saturate(gap * 0.25), saturate(depthMix), saturate(fres), 1.0);

                // SSR 调试（独立于 _DepthDebug；0/1/2 = 关/命中可信度/SSR 色）
                // 与 Water_Ocean 对齐，Main 侧 SSR 验收要用它看"到底有没有命中"。
                if (_SSRDebug > 1.5) return half4(ssrCol.rgb, 1.0);
                if (_SSRDebug > 0.5) return half4(ssrConf.xxx, 1.0);

                float alpha = saturate(_Opacity + fres * 0.1 - foam * 0.2);
                return half4(col + _FoamColor * interFoam * 0.12, alpha);   // 交互泡沫收敛，避免突兀白圈
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}// touch: 强制重导 SSR include 顺序修正

// touch: 强制重编（Unity 不追踪自定义 #include 依赖）

// touch2

// touch3: SSR.hlsl 修正后强制重编
