// ============================================================================
// Water_Ocean.shader — 开阔海岸的水面
//
// 三条水线是三个不同的物理尺度，不是同一份代码改颜色：
//
//   Water_Advanced      Water_Pond          Water_Ocean
//   ────────────────    ─────────────────   ──────────────────────────
//   λ 1.2~12.6m         λ 1.5~3.9m          λ 24~150m（涌）+ 浅水放大
//   浪漫化的池水         fetch 正确的细波     风区够长的真涌
//   反射 = 天空渐变      反射 = 平面镜像 RT    反射 = 天空渐变（海没有可倒的近物）
//   无雾                无雾（16m 用不上）    **必须带雾**，否则地平线是一条硬边
//
//   海的主角不是波纹，是**浪走向岸、变陡、碎掉、变成一道白沫冲上沙滩再退回去**。
//   缺了这条，多大面积的水都只会读成"大池塘"。所以本文件有三段别人没有的东西：
//     ① 波高随水深放大（浅水变形）+ 有界陡度 Q 让浪头变尖
//     ② 破碎判据 H/depth > breakerIndex（哪里该白）
//     ③ 一层贴着沙面上冲再退回的水膜（swash），湿沙边界会呼吸
//
// 纪律（来自本项目踩过的坑，见《05》A-1、D-13、D-17）：
//   · 反射/透射/泡沫全部 lerp，不用 +=（无界累加只会单调变亮）。
//   · 高频皮肤法线按视角衰减，掠射区会混叠成灯芯绒。
//   · 岸线/剖面/沫带全部来自 OceanShore.hlsl，沙滩材质共用同一份，不会错位。
//
// 几何参数（_Shore* / _ShoreSlope / _Beach* / _Swash*）**必须列进 Properties{}**：
//   只在 cbuffer 里声明的 uniform 不会被 Unity 写进 .mat，菜单 SetFloat 在当前会话
//   生效、看着一切正常，重开工程却全部归零（这是本文件第一版真实踩到的坑，
//   由 Tools/audit_materials.py 第⑧项抓到）。
//   防手调漂移不靠"藏起来"，靠检测项②（磁盘值 ≠ 菜单值就报）。
//   代价：这些项不在 UnityPerMaterial 里统一收口，SRP Batcher 仍判不兼容 ——
//   本场景一共两个水/沙 draw call，兼容性在这里没有意义。
// ============================================================================
Shader "WaterProject/Water_Ocean"
{
    Properties
    {
        // ── 颜色 ──
        _DeepColor        ("远海深色", Color) = (0.012, 0.075, 0.130, 1)
        _ShallowColor     ("近岸浅水色", Color) = (0.150, 0.560, 0.580, 1)
        _FoamColor        ("浪沫色", Color) = (0.94, 0.97, 0.98, 1)
        _SkyZenith        ("天空顶部色", Color) = (0.240, 0.520, 0.860, 1)
        _SkyHorizon       ("天空地平色", Color) = (0.700, 0.820, 0.920, 1)
        _SunColor         ("太阳高光色", Color) = (1.0, 0.96, 0.88, 1)

        // ── 着色 ──
        _Absorb           ("吸收系数", Float) = 0.16
        _Opacity          ("整体不透明度", Float) = 0.95
        _MaxDepth         ("归一化最大水深(米)", Float) = 10.0

        // ── 几何涌浪（3 波，λ 24~150m）──
        _SwellAmp         ("涌浪总幅(米)", Float) = 1.5
        _SwellScale       ("波长倍率", Float) = 1.0
        _SwellSpeed       ("传播速度倍率", Float) = 1.0
        _WindAlign        ("浪向对齐岸线的程度", Range(0, 1)) = 0.80
        _Steep            ("浪头尖度", Range(0, 1)) = 0.62

        // ── 浅水变形与碎浪 ──
        _ShoalGain        ("浅水放大增益", Range(1, 4)) = 2.1
        _ShoalRange       ("开始变形的深度(米)", Float) = 7.0
        _BreakerIndex     ("破碎阈值 H/水深", Range(0.4, 1.4)) = 0.82
        _BreakFoam        ("碎浪泡沫强度", Range(0, 1)) = 0.85
        _Whitecap         ("远海白帽强度", Range(0, 1)) = 0.20

        // ── 近岸流场（P0-1 / 参考 C17）──
        _FlowAdvect       ("泡沫随流位移(米/单位速度)", Float) = 30
        _FlowFoamGain     ("流速→泡沫增益", Range(0, 200)) = 60
        _FlowFoamStrength ("流场泡沫强度", Range(0, 1)) = 0.35

        // ── 泡沫历史场（P1-2 / 参考 S10 C18–C20）：残留泡沫从 _SWFoam 采样 ──
        _FoamHistStrength ("泡沫历史强度", Range(0, 1)) = 0.6
        _FoamHistBias     ("泡沫历史阈值(低于它不显示)", Range(0, 0.9)) = 0.3

        // ── 大气耦合（P2-3 / 参考 S14 云影 + S15 远景雾）──
        _CloudShadowOn    ("云影衰减高光 0关/1开", Float) = 1
        _CloudShadowStrength ("云影强度", Range(0, 1)) = 0.85
        _FogOn            ("远景雾 0关/1开", Float) = 0
        _FogDensity       ("雾密度(1/米)", Float) = 0.012
        _FogFalloff       ("雾高度衰减(1/米)", Float) = 0.05
        _FogBaseY         ("雾基准高度(米)", Float) = -2
        _FogColor         ("雾色", Color) = (0.72, 0.78, 0.82, 1)
        _FogSunLen        ("雾中太阳光程(米)", Float) = 260

        // ── 冲滩沫带的"样子"（"能冲多远"在 OceanShore 里，与沙滩共用）──
        _SwashFoam        ("沫带强度", Range(0, 1)) = 0.75

        // ── 表面皮肤（法线）──
        _DetailStrength   ("中频法线强度", Float) = 0.34
        _DetailScale      ("中频法线尺度", Float) = 0.30
        _DetailFlow       ("流动速度", Float) = 0.16
        _RippleStrength   ("高频皮肤强度", Float) = 0.16
        _RippleScale      ("高频皮肤尺度", Float) = 1.1

        // ── 反射与高光 ──
        _FresnelPow       ("菲涅尔指数", Float) = 4.0
        _SkyReflect       ("天空反射权重", Range(0, 2)) = 1.0
        _SpecPower        ("镜面高光锐度", Float) = 420
        _SpecStrength     ("镜面高光强度", Range(0, 2)) = 0.7
        _SunGloss         ("太阳碎闪锐度", Float) = 200
        _SunStrength      ("太阳碎闪强度", Range(0, 3)) = 1.5

        // ── SSR 屏幕空间反射（P0-2 / 参考 C7–C8）──
        _SSRStrength      ("SSR 反射强度", Range(0, 1)) = 0.85
        _SSRMaxDist       ("SSR 追踪最大距离(米)", Float) = 90
        _SSRSteps         ("SSR 步进数", Range(4, 48)) = 24
        _SSRThickness     ("命中厚度容差(米)", Float) = 0.8
        _SSREdgeFade      ("屏幕边缘淡出", Float) = 24
        _SSRDebug         ("SSR 调试 0关/1可信度/2SSR色", Float) = 0

        // ── 调试 ──
        _DebugMode        ("调试输出(0关/1水深/2破碎/3沫带)", Float) = 0

        // ⚠ 岸线 / 剖面 / 沫带：默认值必须与 WaterOceanSetup 的 C# 常量一致。
        //   这一组**必须列进 Properties{}** —— 只在 cbuffer 里声明的 uniform，
        //   Unity 的材质序列化器根本不写它：菜单 SetFloat 在当前会话里生效、画面
        //   看着完全正常，但值不落盘，重开工程后岸线退回 z=0、水深恒为 0。
        //   （合理化检测 Tools/audit_materials.py 第⑧项就是为抓这个写的）
        //   手调它们不会立刻出事（跑一次菜单84 就冲回来），但会让"磁盘上的材质"
        //   和"菜单声称的材质"不一致 —— 检测项②会报。
        _Shore      ("岸线基准 X起点/Z基准 (菜单写)", Vector) = (-400, 0, 0, 0)
        _ShoreA     ("岸线谐波幅度 (菜单写)", Vector) = (22, 9, 3.2, 0)
        _ShoreB     ("岸线谐波频率 (菜单写)", Vector) = (0.0033333, 0.0083333, 0.0222222, 0)
        _WindDir    ("浪传播方向 (菜单写)", Vector) = (-0.175, 0, -0.985, 0)
        _ShoreSlope ("海床坡度 (菜单写)", Float) = 0.055
        _BeachRise  ("滩面抬升 (菜单写)", Float) = 0.055
        _BeachCurve ("滩面二次 (菜单写)", Float) = 0.0022
        _SwashReach ("沫带上冲 (菜单写)", Float) = 9.5
        _SwashSpeed ("沫带周期 (菜单写)", Float) = 0.13
        _SwashFade  ("沫带退透 (菜单写)", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True"
               "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ⚠ 顺序有讲究：OceanShore.hlsl 的函数体直接用这 10 个名字，
            //   所以它必须在块**之后**才 include。详见该文件头部的契约说明。
            CBUFFER_START(UnityPerMaterial)
                float4 _Shore, _ShoreA, _ShoreB, _WindDir;
                float  _ShoreSlope, _BeachRise, _BeachCurve;
                float  _SwashReach, _SwashSpeed, _SwashFade;

                float4 _DeepColor, _ShallowColor, _FoamColor, _SkyZenith, _SkyHorizon, _SunColor;
                float  _Absorb, _Opacity, _MaxDepth;
                float  _SwellAmp, _SwellScale, _SwellSpeed, _WindAlign, _Steep;
                float  _ShoalGain, _ShoalRange, _BreakerIndex, _BreakFoam, _Whitecap;
                float  _SwashFoam;
                float  _DetailStrength, _DetailScale, _DetailFlow, _RippleStrength, _RippleScale;
                float  _FresnelPow, _SkyReflect, _SpecPower, _SpecStrength, _SunGloss, _SunStrength;
                float  _FlowAdvect, _FlowFoamGain, _FlowFoamStrength;
                float  _FoamHistStrength, _FoamHistBias;
                float  _CloudShadowOn, _CloudShadowStrength, _FogOn, _FogDensity, _FogFalloff, _FogBaseY, _FogSunLen;
                float4 _FogColor;
                float  _SSRStrength, _SSRMaxDist, _SSRSteps, _SSRThickness, _SSREdgeFade, _SSRDebug;
                float  _DebugMode;
            CBUFFER_END

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "SSR.hlsl"

            #include "OceanShore.hlsl"

            // ── 近岸流场（P0-1）：NearshoreFlowSim 每帧 SetGlobalTexture/Vector ──
            TEXTURE2D(_SWState); SAMPLER(sampler_SWState);
            TEXTURE2D(_SWVel);   SAMPLER(sampler_SWVel);
            TEXTURE2D(_SWFoam);  SAMPLER(sampler_SWFoam);   // 泡沫历史密度场（P1-2）
            float4 _SWDomain;    // (minX, minZ, sizeX, sizeZ)

            // 大气场（P2-3 / S14）：AtmosphereField 组件逐帧写
            TEXTURE2D(_AFCloudTex); SAMPLER(sampler_AFCloudTex);
            float4 _AFRect;      // (minX, minZ, sizeX, sizeZ)
            float  _AFOn;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // ⚠ 本项目最贵的坑（《05》A-1）：片元里不能用 SV_POSITION 当裁剪坐标，
                //   屏幕空间采样必须另带一份。SSR 的深度比较全靠它。
                float4 clipPos    : TEXCOORD3;
                float3 worldPos   : TEXCOORD0;
                float  offDist    : TEXCOORD1;   // 有符号离岸距离：>0 海侧，<0 岸侧
                float  fogCoord   : TEXCOORD2;
            };

            // ───────── 涌浪：GPU Gems 1 累加 Gerstner ─────────
            // 三条浪分开写，不用数组：方向里含运行期量，初始化列表在部分编译
            // 平台上会退化成分支。
            struct WaveOut { float3 pos; float3 normal; float hSum; };

            void AddWave(float3 dir, float waveLen, float amp, float speed, float t,
                         float gain, inout float3 p, inout float3 tang, inout float3 binor,
                         inout float hSum)
            {
                float k = 6.2831853 / max(waveLen * _SwellScale, 0.1);
                float a = amp * gain;
                // 有界陡度：Q*A*k*N <= 1 才不会打环。N = 3 条浪。
                float q = min(_Steep / max(3.0 * k * a, 1e-4), 1.0);
                float c = sqrt(9.81 / k) * speed * _SwellSpeed;
                float ph = k * dot(dir.xz, p.xz) - c * k * t;
                float s = sin(ph), co = cos(ph);

                p.xz += q * a * dir.xz * co;
                p.y  += a * s;
                tang += float3(-q * a * k * dir.x * dir.x * s,  a * k * dir.x * co,
                               -q * a * k * dir.x * dir.y * s);
                binor+= float3(-q * a * k * dir.x * dir.y * s,  a * k * dir.y * co,
                               -q * a * k * dir.y * dir.y * s);
                hSum += 2.0 * a;
            }

            WaveOut Swell(float3 wp, float t, float gain)
            {
                float3 dir0 = _WindDir.xyz;
                float3 dir1 = normalize(lerp(dir0, float3( 0.36, 0.0, -0.93), 1.0 - _WindAlign));
                float3 dir2 = normalize(lerp(dir0, float3(-0.71, 0.0,  0.70), 1.0 - _WindAlign));

                float3 p = wp;
                float3 tang = float3(1.0, 0.0, 0.0);
                float3 binor = float3(0.0, 0.0, 1.0);
                float hSum = 0.0;

                AddWave(dir0, 150.0, 0.62 * _SwellAmp, 1.00, t, gain, p, tang, binor, hSum);
                AddWave(dir1,  74.0, 0.40 * _SwellAmp, 0.88, t, gain, p, tang, binor, hSum);
                AddWave(dir2,  24.0, 0.26 * _SwellAmp, 1.22, t, gain, p, tang, binor, hSum);

                WaveOut o;
                o.pos = p;
                o.normal = normalize(cross(binor, tang));
                o.hSum = hSum;
                return o;
            }

            // ───────── 皮肤法线（逐像素）─────────
            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
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
            // 梯度用前向差分，不用解析导数 —— 解析导数在格边界归零会画成棋盘格（《05》D-9）
            float2 NoiseSlope(float2 xz, float scale, float2 flow)
            {
                float2 q = xz * scale + flow;
                const float eps = 0.25;
                float n0 = ValueNoise(q);
                return float2(ValueNoise(q + float2(eps, 0)) - n0,
                              ValueNoise(q + float2(0, eps)) - n0) / eps;
            }
            float3 SkinNormal(float3 wp, float t, float fineW)
            {
                float2 mid  = NoiseSlope(wp.xz, _DetailScale, float2(t * _DetailFlow, t * _DetailFlow * 0.7));
                float2 fine = NoiseSlope(wp.xz, _RippleScale, float2(-t * _DetailFlow * 1.6, t * _DetailFlow * 1.2));
                float2 tot  = mid * _DetailStrength + fine * (_RippleStrength * fineW);
                return normalize(float3(-tot.x, 1.0, -tot.y));
            }


            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float t = _Time.y;

                float offDist = OffDist(wp);
                float depth = WaterDepth(wp.x, offDist);

                // 浅水变形：水变浅 → 波速降、波高升（能量通量守恒的粗略形式）
                float gain = lerp(1.0, _ShoalGain, saturate(1.0 - depth / max(_ShoalRange, 0.1)));
                WaveOut w = Swell(wp, t, gain);

                // 关键一行：岸上沙面会顶到 waveY 之上，取 max 就让水自动"改贴沙面"。
                // 不需要分支、不需要第二套网格，水陆交界就是天然的解析水线。
                w.pos.y = max(w.pos.y, SandY(wp.x, offDist) + 0.035);

                o.positionCS = TransformWorldToHClip(w.pos);
                o.clipPos    = o.positionCS;      // 另存一份裁剪坐标（SSR 用，勿用 SV_POSITION）
                o.worldPos   = w.pos;
                o.offDist    = offDist;
                o.fogCoord   = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 worldPos = IN.worldPos;
                float t = _Time.y;
                float offDist = IN.offDist;
                float depth = WaterDepth(worldPos.x, offDist);
                float up = max(-offDist, 0.0);                 // 往岸上爬了多远

                // ── 近岸流场采样（P0-1）：泡沫随水流平流 + 流活跃处增沫 ──
                float2 swUV = (worldPos.xz - _SWDomain.xy) / max(_SWDomain.zw, 1e-4);
                float2 flow = float2(0, 0);
                float flowFoamHist = 0.0;
                if (swUV.x > 0 && swUV.x < 1 && swUV.y > 0 && swUV.y < 1)
                {
                    flow = SAMPLE_TEXTURE2D_LOD(_SWVel, sampler_SWVel, swUV, 0).rg;
                    // 泡沫历史场（P1-2 / S10）：世界 XZ 采样"残留泡沫"密度。
                    // ⚠ 必须**卡一个阈值**再上：历史场里大片低密度泡沫一旦线性叠加，
                    //   整片海会糊成一张白纸（本轮实测 0.6 强度即如此）。只留丝缕/残留的强密度。
                    float hRaw = SAMPLE_TEXTURE2D_LOD(_SWFoam, sampler_SWFoam, swUV, 0).r;
                    flowFoamHist = saturate((hRaw - _FoamHistBias) / max(1.0 - _FoamHistBias, 1e-3)) * _FoamHistStrength;
                }
                float flowMag = length(flow);
                float2 adv = flow * _FlowAdvect;               // 泡沫图案随水流位移（米）

                float3 V = normalize(_WorldSpaceCameraPos - worldPos);

                // 掠射即等价于远处：高频皮肤要按视角衰减，否则混叠成灯芯绒（《05》D-17）。
                // 但**地板比潭线高得多（0.45 而不是 0.20）**：海远处必须还读得出纹理，
                // 压太狠就变成一片光滑渐变、和天糊在一起。这里格子本身 0.3~1m，
                // 掠射时还有十几个像素，混叠风险比潭的 0.18m 低一个量级。
                float fineW = lerp(0.45, 1.0, smoothstep(0.03, 0.30, abs(V.y)));

                float gain = lerp(1.0, _ShoalGain, saturate(1.0 - depth / max(_ShoalRange, 0.1)));
                WaveOut w = Swell(worldPos, t, gain);
                float3 skin = SkinNormal(worldPos, t, fineW);
                float3 N = normalize(lerp(w.normal, skin, 0.5));

                Light mainLight = GetMainLight();

                // ── 大气耦合（P2-3 / 参考 S14 云影 + S15 雾）：云影透射率 × 雾中太阳可见度 ──
                //   ⚠ 必须同时衰减**直射漫反射**：本机位水面高光近 0，只乘 GGX 的话 A/B 测不出（实测 0.01%）。
                float cloudTrans = 1.0;
                if (_CloudShadowOn > 0.5 && _AFOn > 0.5)
                {
                    float2 cuv = (worldPos.xz - _AFRect.xy) / max(_AFRect.zw, 1e-3);
                    if (all(cuv == saturate(cuv)))
                    {
                        float tr = SAMPLE_TEXTURE2D_LOD(_AFCloudTex, sampler_AFCloudTex, cuv, 0).r;
                        float2 e = min(cuv, 1.0 - cuv);
                        float ew = saturate(min(e.x, e.y) * 8.0);   // 边缘平滑衰减，防锐边
                        cloudTrans = lerp(1.0, tr, ew * _CloudShadowStrength);
                    }
                }
                float fogVis = (_FogOn > 0.5) ? exp(-_FogDensity * _FogSunLen) : 1.0;
                float sunAtt = saturate(cloudTrans * fogVis);

                // ──  水色：Beer–Lambert 按【垂直】水深 ──
                float depthMix = 1.0 - exp(-depth * _Absorb);
                float3 waterBase = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthMix);
                float ndl = saturate(dot(N, mainLight.direction));
                // 云影既衰减**直射**也衰减一部分**天光**（云整体遮暗天空→水面变暗）。
                // ⚠ 本机位太阳很低、水面直射≈0，只衰减直射的话云影完全看不出来（实测 0.00%）。
                float3 illum = SampleSH(N) * lerp(1.0, sunAtt, 0.6)
                             + mainLight.color * ndl * 0.30 * sunAtt;
                float3 col = waterBase * illum;

                // 浅水透光：阳光打到沙底再返上来
                float trans = exp(-depth * _Absorb * 0.5);
                col = lerp(col, _ShallowColor.rgb * 1.30, saturate(trans - 0.45) * 0.7);

                // ── ② 反射：开阔海没有可倒的近物，双色天空渐变就够 ──
                float fres = pow(1.0 - saturate(dot(V, N)), _FresnelPow);
                float3 reflDir = reflect(-V, N);
                reflDir.y = max(reflDir.y, 0.02);
                float3 skyCol = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, saturate(reflDir.y * 2.2));
                // 云影也改**反射的天空色**：云下天更灰更亮（不是深蓝）→ 水面倒影随之变化。
                // ⚠ 本机位太阳低、水面直射≈0、主色由反射主导：只衰减漫反射的话云影测不出（实测 0.00%）。
                skyCol = lerp(skyCol, skyCol * 0.72 + 0.22, saturate(1.0 - cloudTrans) * _CloudShadowStrength * 0.9);

                // ── SSR（P0-2 / 参考 C8）：命中处用真实场景色替换天空兜底 ──
                float ssrConf = 0.0;
                float3 ssrCol = TraceSSR(worldPos, reflDir, _SSRMaxDist, _SSRSteps, _SSRThickness, _SSREdgeFade, ssrConf);
                float3 reflCol = lerp(skyCol, ssrCol, ssrConf * _SSRStrength);   // miss ⇒ 天空渐变兜底
                col = lerp(col, reflCol, saturate(fres * _SkyReflect));

                // ── ③ 太阳碎闪：pow 自带衰减，累加是合法的；乘 sunAtt 受大气遮挡（sunAtt 见上）──
                col += _SunColor.rgb * pow(saturate(dot(reflDir, mainLight.direction)), _SunGloss)
                     * _SunStrength * saturate(0.2 + fres) * sunAtt;
                float3 H = normalize(mainLight.direction + V);
                col = lerp(col, _SunColor.rgb,
                           saturate(pow(saturate(dot(N, H)), _SpecPower) * _SpecStrength * sunAtt));

                // ── ④ 碎浪 —— 这是"海"最硬的识别特征 ──
                // 破碎深度按**主浪**的波高算（γ = H/d ≈ _BreakerIndex）。
                // ⚠ 不能用三条浪的波高之和：那会把破碎线推到离岸几十米外，
                //   配上 0.055 的缓坡就等于"整片海都在碎"，画面直接变一张白纸
                //   （第一版就是这么翻车的）。
                // ⚠ 条带相位挂在**最短那条浪**上，不是主涌。破碎带从外缘到水线只有
                //   几十米，λ=150m 在这个区间里连一个波峰都放不下 ⇒ 读不出"一排排白线"，
                //   只剩水线边一道糊白。λ=24m 能塞进 2~3 个波峰，而海滩上真正先碎的
                //   本来就是沙坝上的短浪。破碎深度仍按主浪算，两者各管各的。
                float k0 = 6.2831853 / max(24.0 * _SwellScale, 0.1);
                float c0 = sqrt(9.81 / k0) * 1.22 * _SwellSpeed;
                float ph0 = k0 * dot(_WindDir.xz, worldPos.xz) - c0 * k0 * t;
                // 白沫只挂在浪头上：pow 把 0~1 的相位压成窄峰
                float crest = pow(0.5 + 0.5 * sin(ph0), 2.2);
                float hDom = 2.0 * 0.62 * _SwellAmp * gain;
                float dBreak = hDom / max(_BreakerIndex, 0.1);
                float inSurf = saturate((dBreak - depth) / max(dBreak * 0.5, 0.25));
                // 破碎带不是干净的一条线，用噪声把它咬成指状
                float bite = ValueNoise(float2(worldPos.x * 0.05, t * 0.30)) * 0.6 + 0.4;
                float breakage = saturate(inSurf * (0.18 + 1.5 * crest) * bite);
                float breakFoam = breakage * _BreakFoam;

                // 远海白帽：只在够深的地方出现，否则会和碎浪带糊成一片
                float wc = ValueNoise(worldPos.xz * 0.07 + float2(t * 0.20, t * 0.05));
                float whitecap = saturate((wc - 0.55) * 3.2) * _Whitecap * saturate(depth - 3.0) * 0.6;

                // ── ⑤ 冲滩沫带：会呼吸的湿沙边界 ──
                // ⚠ 必须用**有符号**的 offDist。上一版写的是 `1 - up/reach`，
                //   而 up = max(-offDist, 0) 在海侧恒为 0 ⇒ swash 恒为 1 ⇒
                //   整片洋面被盖一层沫、海直接变成一张白纸。
                // 沫带真正的形状：岸上往回衰减 reach 米，海侧只留几米（刚退下去的
                // 那层湿沫），再往外就该是干净的水。
                float reach = SwashReach(t);
                float swash = (offDist <= 0.0)
                            ? saturate(1.0 + offDist / max(reach, 0.5))
                            : saturate(1.0 - offDist / 3.0);
                float sn = ValueNoise(float2(worldPos.x * 0.09 + adv.x, offDist * 0.5 - t * 0.8 + adv.y));
                // 卡阈值出蕾丝前缘，而不是平滑渐变的雾白 —— 后者会被浅水提亮整个盖掉，
                // 于是"浪冲到哪儿了"这条信息在画面上根本读不出来。
                float washFoam = smoothstep(0.30, 0.92, swash * (0.30 + 1.15 * sn)) * _SwashFoam;

                // 流场泡沫（P0-1）：流速活跃的近岸浅水处增沫（只在模拟域内、且水深不深时）
                float flowFoam = saturate(flowMag * _FlowFoamGain) * _FlowFoamStrength
                               * saturate(1.0 - depth / 8.0);

                // 泡沫历史场（P1-2）：残留泡沫（随流漂移、缓慢褪去）叠进总沫量
                float foam = saturate(breakFoam + whitecap + washFoam + flowFoam + flowFoamHist);
                col = lerp(col, _FoamColor.rgb, foam);

                // ── 调试通道：先看通道通不通，再解释数值（《06》附 B）──
                if (_DebugMode > 2.5) return half4(saturate(washFoam), saturate(breakFoam), saturate(foam), 1.0);
                if (_DebugMode > 1.5) return half4(saturate(breakage), saturate(gain / _ShoalGain), saturate(swash), 1.0);
                if (_DebugMode > 0.5) return half4(saturate(depth / _MaxDepth), saturate(trans), saturate(fres), 1.0);
                // SSR 调试（独立于 _DebugMode，0/1/2 = 关/可信度/SSR 色）
                if (_SSRDebug > 1.5) return half4(ssrCol.rgb, 1.0);
                if (_SSRDebug > 0.5) return half4(ssrConf.xxx, 1.0);

                // 水膜在"上冲极限"处透过去，露出干沙 —— 边界因此会随潮汐呼吸。
                // 岸上（up>0）才需要这个衰减；海侧始终实心，否则近岸浅水会莫名变淡。
                float edge = saturate((reach + _SwashFade - up) / max(_SwashFade, 0.3));
                float alpha = saturate(_Opacity * lerp(1.0, edge, step(0.001, up))
                                       + fres * 0.05 - washFoam * 0.20);
                // 冲上来的那圈沫本身要更接近实心白，否则薄薄一层看不出"水到过这里"
                alpha = max(alpha, washFoam * 0.85 * step(0.001, up));

                // ── 远景雾（P2-3 / 参考 S15）：沿视线到水面的**解析高度雾**（Beer–Lambert 积分）──
                //   密度随高度指数衰减：d(y)=exp(-(y-_FogBaseY)*_FogFalloff)；
                //   取相机/水面两点平均，光程 = density × 距离。参考是全 raymarch+3D噪声，此处为闭式近似。
                if (_FogOn > 0.5)
                {
                    float distV = length(worldPos - _WorldSpaceCameraPos);
                    float dCam  = exp(-max(_WorldSpaceCameraPos.y - _FogBaseY, 0.0) * _FogFalloff);
                    float dSurf = exp(-max(worldPos.y - _FogBaseY, 0.0) * _FogFalloff);
                    float opt = _FogDensity * distV * (dCam + dSurf) * 0.5;
                    col = lerp(_FogColor.rgb, col, exp(-opt));
                }

                col = MixFog(col, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}

// ────────────────────────────────────────────────────────────────────────────
// 为什么水深走解析而不是 SampleSceneDepth（潭线用的是后者）：
//   潭只有 16m，深度纹理分辨率够用，而且潭底是任意网格、写不出解析式。
//   海的剖面是**我们自己按 ShoreZ(x) + SandY(v) 造出来的**，再回头去深度缓冲里
//   "猜"它，等于把已知量换成噪声量：掠射区一个纹素跨好几米，湿沙边界会糊成一团，
//   而湿沙边界正是这张图里最该锐利的地方。
// 代价：岸线必须是"z = f(x)"的单值函数（不能有岬角把海遮在后面）。远岬单独
//   用一个不透明网格画，不参与水的深度计算。
// 已知不精确处：顶点里那个 max() 会在"浪面 = 沙面+3.5cm"处折一道硬棱，而片元的
//   法线来自没被裁的解析浪面，所以水线附近明暗与几何会有轻微不一致。那里恰好
//   被沫带整片盖住，肉眼读不出来 —— 记下来，别在别的水体上照抄这个 max()。
// ────────────────────────────────────────────────────────────────────────────

// touch: 强制重编（Unity 不追踪自定义 #include 依赖）

// touch2

// touch3: SSR.hlsl 修正后强制重编
