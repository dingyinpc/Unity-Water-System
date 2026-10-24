// ============================================================================
// Water_Lagoon.shader — 第四条水线：热带环礁潟湖（`IsletScene`）
//
// 对标 Docs/参考视频帧/参考1~6.png。这六张里的水**不是**开海，判据完全不同：
//
//                     Water_Ocean            Water_Lagoon（本文件）
//   ───────────────  ─────────────────────  ─────────────────────────────
//   一道连续岸        几座岛各自一圈浅滩     没有"岸线函数"可用
//   观感靠碎浪白沫    观感靠**水色随深度**    潟湖里有岛挡浪，近处几乎只有缓涌
//   浪要卷要碎        λ 十几米的长涌         几何位移只有几厘米，全靠法线
//   水深=离岸距离     水深=地形高度场         ⇒ 深度从 _CameraDepthTexture 取，
//                                             地形是**唯一**的几何真值
//
// 三条主信号，按参考帧里的重要程度排：
//   ① 浅滩透色：水下的白沙洲把光返上来 → 近岸一条 (185,218,212) 的亮带，
//      往外 (120,200,190) → (95,190,196) → 深水 (30,160,170)。
//      R 通道一路压死、G≈B —— 这是"青绿"，和天空的"湛蓝(B>>G)"必须分得开。
//   ② 太阳碎闪：太阳在相机正前方（顺光），碎闪带落在画面中央。
//      静水的镜面反射点其实只在脚前 ~4m（眼高 1.72 / tan24°），
//      参考里那条贯穿画面的亮带**完全是浪把法线打散**的结果 ——
//      所以碎闪必须用**扰动后的法线**算 pow，绝不能用几何平均法线，
//      否则渲出来是一整片均匀的白，而不是"碎"闪。
//   ③ 掠射处变镜子：远处水面贴地平线那一段基本只反射天空的淡霾。
//
// 纪律（本项目踩过的坑，见《05》）：
//   · 屏幕空间采样一律用自己传的 clipPos，绝不蹭 SV_POSITION（A-1）。
//   · 反射是 lerp 替换不是 += 累加，否则掠射区被洗白（D-1/D-5）。
//   · 高频法线必须逐像素算，且掠射时要衰减，否则混叠成灯芯绒条纹（D-9）。
//   · 带宽用**垂直**水深，不用视深，否则泡沫带随视角变宽窄（D-2/D-3）。
//   · CBUFFER 里每个名字只出现一次（曾经重复声明直接编译失败，画面全品红）。
// ============================================================================
Shader "WaterProject/Water_Lagoon"
{
    Properties
    {
        // ── 水色三档（线性值，不是显示值；显示目标见文件头①）──
        _ShallowColor     ("浅滩水色", Color) = (0.42, 0.80, 0.74, 1)
        _MidColor         ("潟湖主水色", Color) = (0.10, 0.55, 0.56, 1)
        _DeepColor        ("深水区水色", Color) = (0.020, 0.260, 0.320, 1)
        _SandReturn       ("沙底返色", Color) = (0.62, 0.78, 0.70, 1)

        // ── 深度 ──
        _Absorb           ("吸收系数(1/m)", Float) = 0.42
        _SandAbsorb       ("沙底可见深度系数", Float) = 0.30
        _Opacity          ("整体不透明度", Range(0,1)) = 0.92

        // ── 缓涌（几何位移）──
        _SwellAmp         ("涌幅(米)", Float) = 0.055
        // ── 波纹法线贴图（两层同图不同缩放/方向/速度）──
        // ⚠ _WaveStrength 默认 **0**：这份 shader 只被 IsletScene 用，
        //   但"加了属性就把画面改了"是最难查的回归，所以默认必须是空操作。
        //   贴图默认 "bump" 是 Unity 内置的中性法线 (0.5,0.5,1)，
        //   即使忘了赋值也只会得到"不平坦"= 无影响，不会出棋盘格。
        _WaveMap          ("波纹法线(自产无缝)", 2D) = "bump" {}
        _WaveStrength     ("波纹强度", Range(0, 2)) = 0
        _WaveTileA        ("波纹缩放A(每米)", Float) = 0.55
        _WaveTileB        ("波纹缩放B(每米)", Float) = 1.60
        _WaveSpeedA       ("波纹速度A", Float) = 0.16
        _WaveSpeedB       ("波纹速度B", Float) = 0.30
        _SwellScale       ("涌频率倍率", Float) = 1.0
        _SwellSpeed       ("涌速度倍率", Float) = 0.35

        // ── 表面皮肤（法线，碎闪的来源）──
        _SlopeStrength    ("中频坡度强度", Float) = 0.30
        _SlopeScale       ("中频尺度", Float) = 0.55
        _FlowSpeed        ("流动速度", Float) = 0.22
        _CapStrength      ("毛细强度(碎闪主力)", Float) = 0.22
        _CapScale         ("毛细尺度", Float) = 2.6
        _CapToNg          ("毛细进漫反射法线的份额", Float) = 1.0

        // ── 折射（把水底搬上来）──
        _RefractStrength  ("折射偏移", Float) = 0.16
        _MaxUVOffset      ("折射偏移上限", Range(0, 0.1)) = 0.022
        _Eta              ("折射率", Float) = 1.33
        _BottomBlend      ("水底搬运权重", Range(0,1)) = 0.70

        // ── 天空反射 ──
        _SkyZenith        ("反射·天顶", Color) = (0.10, 0.42, 0.72, 1)
        _SkyHorizon       ("反射·地平", Color) = (0.65, 0.80, 0.90, 1)
        _FresnelPow       ("菲涅尔指数", Float) = 4.2
        _SkyReflect       ("反射权重", Range(0,2)) = 1.0

        // ── 太阳碎闪 ──
        _SunColor         ("碎闪色", Color) = (1.0, 0.96, 0.86, 1)
        _GlitterPow       ("碎闪锐度", Float) = 420.0
        _GlitterStrength  ("碎闪强度", Float) = 2.6
        _GlitterBand      ("碎闪带宽度(法线放大)", Float) = 1.9
        _SheenPow         ("日光盘锐度", Float) = 60.0
        _SheenStrength    ("日光盘强度", Float) = 0.35

        // ── 岸边泡沫（潟湖要**克制**，参考里几乎没有白沫）──
        _FoamColor        ("泡沫色", Color) = (0.94, 0.98, 0.98, 1)
        _FoamBand         ("泡沫带宽(米)", Float) = 0.30
        _FoamStrength     ("泡沫强度", Range(0,1)) = 0.30

        // ── 环境 ──
        _AmbientStrength  ("环境光强度", Float) = 0.55
        _DepthSource      ("水深来源 0深度纹理 1解析地面", Range(0,1)) = 0
        _DebugMode        ("调试 0关 1水深 2菲涅尔 3只用皮肤法线", Range(0,3)) = 0
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // 贴图与采样器**不能**进 CBUFFER（那是 uniform 的地盘），单独声明
            TEXTURE2D(_WaveMap);
            SAMPLER(sampler_WaveMap);

            // menu 91 烘焙的解析地面高度图 + 参数（IsletFloorHBinder 写的全局，
            // 不进 UnityPerMaterial —— 全局纹理/向量有自己的槽）
            TEXTURE2D(_IsletFloorH);
            SAMPLER(sampler_IsletFloorH);
            float4 _IsletFloorHRect;    // (originX, originZ, sizeXZ, hMin)
            float  _IsletFloorHH;       // 高度上界

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _MidColor, _DeepColor, _SandReturn;
                float  _Absorb, _SandAbsorb, _Opacity;
                float  _SwellAmp, _SwellScale, _SwellSpeed;
                float  _WaveStrength, _WaveTileA, _WaveTileB, _WaveSpeedA, _WaveSpeedB;
                float  _SlopeStrength, _SlopeScale, _FlowSpeed, _CapStrength, _CapScale, _CapToNg;
                float  _RefractStrength, _MaxUVOffset, _Eta, _BottomBlend;
                float4 _SkyZenith, _SkyHorizon;
                float  _FresnelPow, _SkyReflect;
                float4 _SunColor;
                float  _GlitterPow, _GlitterStrength, _GlitterBand, _SheenPow, _SheenStrength;
                float4 _FoamColor;
                float  _FoamBand, _FoamStrength;
                float  _AmbientStrength, _DebugMode, _DepthSource;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;   // 光栅化用
                float4 clipPos    : TEXCOORD0;     // ★ 屏幕空间采样必须用这个（《05》A-1）
                float3 worldPos   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;     // 几何（缓涌）解析法线
            };

            // ───────────── 缓涌：三条长波，位移只有厘米级 ─────────────
            // 潟湖被岛挡着，没有涌浪可碎。这里要的是"整片水面缓慢起伏"，
            // 幅度小到几乎看不出几何、但足以让反射方向在画面上慢慢爬。
            struct WaveOutLagoon { float3 pos; float3 normal; float J; };

            WaveOutLagoon Swell(float3 wp, float t)
            {
                float3 P = wp;
                float dPxdx = 1, dPxdz = 0, dPzdx = 0, dPzdz = 1, dPydx = 0, dPydz = 0;

                UNITY_UNROLL for (int i = 0; i < 3; i++)
                {
                    float A, w, Q; float2 d;
                    if      (i == 0) { A = 1.00; w = 0.30; Q = 0.55; d = normalize(float2( 1.00f,  0.22f)); }
                    else if (i == 1) { A = 0.55; w = 0.52; Q = 0.50; d = normalize(float2(-0.35f,  0.94f)); }
                    else             { A = 0.30; w = 0.95; Q = 0.45; d = normalize(float2( 0.78f, -0.62f)); }

                    // 空间调制：让起伏成片，而不是整张图一样密
                    float m = 0.60 + 0.40 * sin(dot(wp.xz, float2(0.017, 0.023)) + t * 0.09);
                    A *= _SwellAmp * m;
                    w *= _SwellScale;
                    Q *= _SwellScale;

                    float phase = dot(wp.xz, d) * w + (float)i * 1.9 - t * w * _SwellSpeed * 2.2;
                    float c = cos(phase), s = sin(phase);

                    P.x -= Q * A * d.x * c;
                    P.z -= Q * A * d.y * c;
                    P.y += A * s;

                    float wcx = w * c;
                    dPxdx -= Q * A * d.x * d.x * wcx;   dPxdz -= Q * A * d.x * d.y * wcx;
                    dPzdx -= Q * A * d.y * d.x * wcx;   dPzdz -= Q * A * d.y * d.y * wcx;
                    dPydx += A * d.x * wcx;             dPydz += A * d.y * wcx;
                }

                WaveOutLagoon o;
                o.pos    = P;
                o.normal = normalize(float3(dPydx, 1.0, dPydz));   // 缓坡近似，见下方说明
                o.J      = dPxdx * dPzdz - dPxdz * dPzdx;
                return o;
            }

            // ───────────── 噪声皮肤 ─────────────
            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 梯度用前向差分。解析导数在格边界归零会画成棋盘格（《05》D-9）
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

            // 返回**两组坡度**（float4）：xy = 毛细全量（给碎闪 Ns），zw = 毛细按
            // _CapToNg 衰减后的温和版（给 Ng）。一份噪声求值喂两个用法，省一半采样。
            // 为什么要拆：碎闪需要陡法线，而漫反射的 ndl = saturate(dot(Ng,L)) 需要
            // 平法线 —— 毛细的逐像素抖动被 saturate 单向削顶后均值被压低，
            // 实测整片水因此暗 1.6×（2026-10-04 轮）。_CapToNg=1.0 等价旧路径。
            // 两层：中频缓坡 + 高频毛细。**毛细那一层是碎闪的全部来源**。
            // fineW 让毛细在掠射时衰减：远处 1/_CapScale≈0.4m 的格在屏上只剩 1~2px，
            // 不衰减就会混叠成一排排等距暗纹（潭水那轮实测过，《05》D-9）。
            float4 SkinGrad(float3 wp, float t, float fineW)
            {
                float2 mid = NoiseSlope(wp.xz, _SlopeScale, float2(t * _FlowSpeed, t * _FlowSpeed * 0.7)) * _SlopeStrength;
                float2 cap = NoiseSlope(wp.xz, _CapScale,   float2(-t * _FlowSpeed * 1.7, t * _FlowSpeed * 1.1)) * (_CapStrength * fineW);
                return float4(mid + cap, mid + cap * _CapToNg);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                WaveOutLagoon w = Swell(wp, _Time.y);

                o.positionCS = TransformWorldToHClip(w.pos);
                o.clipPos    = o.positionCS;      // ★ 见文件头纪律
                o.worldPos   = w.pos;
                o.normalWS   = w.normal;
                return o;
            }

            // ── 波纹法线 → 本管线用的"斜率" ─────────────────────────────
            // 两层采**同一张图**，但缩放/方向/速度都不同。
            // ⚠ 方向必须不同：两层同方向只是把坡度乘二，看着仍是"一叠平行波"；
            //   方向错开才会互相打断，出现真实水面的交叉波状。
            float2 WaveSlope(float3 worldPos, float t)
            {
                if (_WaveStrength <= 0.0) return float2(0.0, 0.0);   // 默认=空操作

                float2 uvA = worldPos.xz * _WaveTileA + float2( 1.00, 0.37) * (t * _WaveSpeedA);
                float2 uvB = worldPos.xz * _WaveTileB + float2(-0.62, 1.00) * (t * _WaveSpeedB);
                float3 a = UnpackNormal(SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, uvA));
                float3 b = UnpackNormal(SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, uvB));
                float3 m = normalize(a + b);

                // 水面近似水平面 ⇒ 切线框直接就是 (世界X, 世界Z, 世界Y)，不用真算 TBN。
                // 贴图切线空间里 x 沿 U、y 沿 V、z 沿法线；这里 U=世界X、V=世界Z，
                // 所以世界法线 = (m.x, m.z, m.y)。
                // 而本管线已有的写法是 Ng = normalize(-grad.x, 1, -grad.y)，
                // 两式对齐 (除以 m.z) 就得到 grad = (-m.x, -m.y) / m.z。
                return float2(-m.x, -m.y) / max(m.z, 1e-3) * _WaveStrength;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 worldPos = IN.worldPos;
                float t = _Time.y;

                float3 V = normalize(_WorldSpaceCameraPos - worldPos);

                float fineW = lerp(0.18, 1.0, smoothstep(0.04, 0.35, abs(V.y)));
                // 程序化皮肤 + 贴图波纹。加在这里而不是另开一份法线，
                // 是因为下面 Ng（反射/折射）和 Ns（碎闪）**都**从 grad 派生，
                // 一处相加就同时拿到"波纹让反射动起来"和"波纹让碎闪跳起来"。
                float4 g = SkinGrad(worldPos, t, fineW);
                float2 grad   = g.xy + WaveSlope(worldPos, t);   // 碎闪用（毛细全量）
                float2 gradNg = g.zw + WaveSlope(worldPos, t);   // 漫反射/反射/折射用

                // 两份法线：反射/折射用温和的，碎闪用放大过的。
                // 静态混一份的话，pow 420 之下毛细那点坡度会被抹平，碎闪直接消失。
                float3 swellN = normalize(IN.normalWS);
                float3 Ng = normalize(lerp(swellN, float3(-gradNg.x, 1.0, -gradNg.y), 0.75));
                float3 Ns = normalize(float3(-grad.x * _GlitterBand, 1.0, -grad.y * _GlitterBand));
                Ns = normalize(lerp(Ns, ShimmerN(worldPos.xz, t, 2.6), 0.62));

                // ── 屏幕空间 UV：用 clipPos ──
                float4 sp = IN.clipPos * 0.5;
                sp.xy = float2(sp.x, sp.y * _ProjectionParams.x) + sp.w;
                float2 screenUV = sp.xy / sp.w;

                // ── 水深 ──
                // _DepthSource=1：vd = 0 − 解析地面高度（menu 91 烘焙的 R16，与
                // TerrainData 同一次 LandHeight）。默认 0 = 深度纹理旧路径（空操作）。
                // 换源的原因见 IsletFloorHBinder.cs 文件头：URP14 的深度纹理在本机
                // 两条取帧通路里都不可信（离屏全远平面；GameView ~10m 外膨胀→20m 归零）。
                // gap 是【视深】；vd 才是垂直水深，带宽与吸收都用它（《05》D-2/D-3）
                float vd;
                if (_DepthSource > 0.5)
                {
                    // 足印外（clamp 到边缘）= rim 的 -7.5m → vd 7.5m：出礁盘即"深水"，物理正确
                    float2 fuv = (worldPos.xz - _IsletFloorHRect.xy) / _IsletFloorHRect.z;
                    float floorY = lerp(_IsletFloorHRect.w, _IsletFloorHH,
                                        SAMPLE_TEXTURE2D_LOD(_IsletFloorH, sampler_IsletFloorH, fuv, 0).r);
                    vd = max(-floorY, 0.0);
                }
                else
                {
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float waterDepth = IN.clipPos.w;
                    float gap = max(sceneDepth - waterDepth, 0.0);
                    vd  = gap * max(abs(V.y), 0.06);
                }

                float depthMix = 1.0 - exp(-vd * _Absorb);          // 水色：浅→深
                float sandMix  = exp(-vd * _SandAbsorb);            // 沙底还能返多少光

                float fres = pow(1.0 - saturate(dot(V, Ng)), _FresnelPow);

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(Ng, mainLight.direction));
                half3 ambient = SampleSH(Ng);

                // ── ① 水体自身：三段深度插值 ──
                float3 waterBody = lerp(_ShallowColor.rgb, _MidColor.rgb, saturate(depthMix * 1.6));
                waterBody = lerp(waterBody, _DeepColor.rgb, saturate(depthMix * 1.6 - 0.6));
                // ⚠ 自迭代第 1 轮曾把 0.55→0.72（对着坏深度时代的暗带补偿）——
                //   深度修好（MSAA 关）后回退：浅水本体×0.72 直接过曝成"沙克隆"。
                //   深度可用的基线必须重新标定，别拿坏时代的补偿值叠新状态。
                float3 col = waterBody * (ambient * _AmbientStrength + mainLight.color * ndl * 0.55);

                // 沙底返色：浅水处阳光穿到白沙再返上来，这一下决定"透不透"
                // 0.55→0.35（2026-10-04 轮）：解析深度接通后 sandMix 在全场都有
                // 0.4~0.7，原系数把 G/B 一起抬超目标 ~20%；实测三带同步收敛。
                col += _SandReturn.rgb * sandMix * (ambient * 0.5 + mainLight.color * ndl) * 0.35;

                // ── ② 折射：真的把水底搬上来（浅水才搬）──
                // 参考帧里沙洲之所以看得见，是因为水底**有形状有明暗**，
                // 不是靠上面那条 sandMix 均匀加亮。所以这一项不能省。
                float3 refr = refract(-V, Ng, _Eta);
                if (dot(refr, refr) < 1e-5) refr = -V;
                float2 refrDir = normalize(refr.xy + 1e-6);
                float2 uvOff = refrDir * _RefractStrength * (0.5 / max(abs(refr.z), 0.05));
                uvOff = clamp(uvOff, -_MaxUVOffset, _MaxUVOffset);
                // 越浅越搬；深水本来就该看不见底，搬了反而糊
                float shallow = 1.0 - saturate(depthMix);
                float2 refrUV = screenUV + uvOff * shallow * (1.0 - fres * 0.7);
                float3 bottom = SampleSceneColor(refrUV).rgb;
                col = lerp(col, bottom * (waterBody * 1.6 + 0.05),
                           shallow * _BottomBlend * (1.0 - fres));

                // ── ③ 反射：lerp 替换，不是累加（《05》D-1）──
                float3 reflDir = reflect(-V, Ng);
                reflDir.y = max(reflDir.y, 0.015);      // 别反射到水面以下
                float3 skyCol = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, saturate(reflDir.y));
                float reflW = saturate(fres * _SkyReflect);
                col = lerp(col, skyCol, reflW);

                // ── ④ 太阳碎闪：必须用扰动法线，且是**累加**（加光合法）──
                // 用 Ns（坡度放大过的那份法线）：静水镜面点只在脚前 ~4m
                //（眼高 1.72 / tan24°），参考里那条贯穿画面的亮带全靠毛细把
                // 法线打散才铺得开。
                // ⚠ **绝不能再乘 mainLight.color**：URP 里它已经 = light.color ×
                //   light.intensity（太阳 3.2）。乘了就是 2.6×3.2=8.3 的峰值，
                //   整片近景直接冲到 (157,255,255) 死白 —— 实测踩过。
                float3 Hs = normalize(mainLight.direction + V);
                float gl = pow(saturate(dot(Ns, Hs)), _GlitterPow);
                col += _SunColor.rgb * gl * _GlitterStrength;
                // 日光盘：低锐度高强度的一层，给碎闪带一个底
                col += _SunColor.rgb * pow(saturate(dot(reflDir, mainLight.direction)), _SheenPow)
                     * _SheenStrength;

                // ──  岸边泡沫：潟湖克制，只在潮间带一线 ──
                float foam = saturate(1.0 - vd / max(_FoamBand, 0.02)) * (1.0 - fres);
                foam *= 0.55 + 0.45 * ValueNoise(worldPos.xz * 3.1 + t * 0.35);
                col = lerp(col, _FoamColor.rgb, saturate(foam) * _FoamStrength);

                // ── 调试：先看通道通不通，再解释数值 ──
                // （mode 3 曾在 2026-10-04 轮临时征用为深度链路解剖 R/G/B 三件套，
                //   排查完 URP14 深度纹理问题后已复原；那次排查的结论与替代方案
                //   见 IsletFloorHBinder.cs 文件头与 _DepthSource 注释）
                if (_DebugMode > 2.5) return half4(grad.x * 0.5 + 0.5, grad.y * 0.5 + 0.5, fres, 1);
                if (_DebugMode > 1.5) return half4(reflW, saturate(gl * 4), sandMix, 1);
                if (_DebugMode > 0.5) return half4(saturate(vd / 6.0), saturate(depthMix), saturate(shallow), 1);

                float alpha = saturate(_Opacity + fres * 0.08 - sandMix * 0.10);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
