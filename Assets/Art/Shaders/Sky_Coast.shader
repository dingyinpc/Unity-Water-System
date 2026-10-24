// ============================================================================
// Sky_Coast.shader — 开阔海岸的天空：分层渐变 + 体积云 + 太阳晕/LensFlare
//
// R3 版（2026-10-24，替代 2D 平面投影云）：
//   ① 分层渐变（参考 S12 C24）：地平/低空/中空/天顶四层——低/中两色由
//      DayNightCycle 驱动的 _HorizonColor/_ZenithColor 插值派生（不新增写入点），
//      朝阳/夕阳双色：太阳侧按 dot(view.xz, sun.xz) × 低太阳权重向暖色偏。
//   ② 体积云（S13 C26）：视线与云底/云顶球壳求交（地球半径 6371km），
//      RayMarch 20 步，密度 = fbm 阈值化 × 高度剖面 − 细节侵蚀；风带切变
//      （woff = windDir·(speed·t + h·shear)，上下层差速漂移）；光照 = 一次
//      320m 朝太阳的阴影采样 + 前向散射银边；昼夜由 _NightAmount 压暗。
//      ——R1 量测动机：2D 贴图云天空能量只有参考 S13 的 1.4%、云速慢 5.6 倍。
//   ③ LensFlare（S11 C22）：白天型（小亮核+两枚鬼影）与黄昏型（大红斑）
//      按太阳高度交叉插值，乘视线云透射率（云遮太阳→耀斑弱）。
//   ④ 夜空（星星/银河/月面，P1-1）保留：月面透光改吃体积云透射率 T。
//   太阳方向一律 GetMainLight()（《05》C-4：不存第二份太阳角）。
// ============================================================================
Shader "WaterProject/Sky_Coast"
{
    Properties
    {
        _HorizonColor ("地平线颜色", Color) = (0.700, 0.800, 0.900, 1)
        _ZenithColor  ("天顶颜色", Color) = (0.200, 0.430, 0.780, 1)
        _GroundColor  ("地面半色", Color) = (0.230, 0.250, 0.240, 1)
        _CloudColor   ("云色", Color) = (1.00, 0.995, 0.985, 1)
        _CloudShadow  ("云底阴影色", Color) = (0.520, 0.570, 0.650, 1)
        _CloudCover   ("云量", Range(0, 1)) = 0.52
        _SunGlow      ("日晕强度", Range(0, 2)) = 0.55

        // ── 体积云（R3）：_CloudScale/_CloudDrift 旧参废弃保留兼容，不再参与 ──
        _CloudWindDir   ("风向(度)", Float) = 200
        _CloudWindSpeed ("风速(m/s@云层)", Float) = 18
        _CloudShear     ("风切变(米/米高)", Float) = 0.006
        _CloudBase      ("云底高度(米)", Float) = 900
        _CloudTop       ("云顶高度(米)", Float) = 2000
        _CloudSteps     ("RayMarch 步数", Float) = 20

        // ── 夜空（P1-1 / 参考 S12）：全部由 DayNightCycle 写入，白天为 0 ──
        _NightAmount  ("夜色强度(0昼1夜)", Range(0,1)) = 0
        _StarGrid     ("星空格数", Float) = 320
        _StarBright   ("星空亮度", Range(0,3)) = 1.60
        _GalaxyAmount ("银河强度", Range(0,1)) = 0.30
        _MoonDir      ("月亮方向(世界,指向月亮)", Vector) = (0, 0.6, -0.8, 0)
        _MoonColor    ("月亮颜色", Color) = (1.0, 0.97, 0.90, 1)
        _MoonSize     ("月面角半径(弧度)", Float) = 0.030
        _MoonPhase    ("月相(0新月 0.5满月)", Range(0,1)) = 0.62
        _MoonAmount   ("月面强度(0隐1显)", Range(0,1)) = 0

        // ── LensFlare（S11 C22）──
        _FlareAmount  ("耀斑总强度", Range(0, 2)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox"
               "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off Lighting Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HorizonColor, _ZenithColor, _GroundColor, _CloudColor, _CloudShadow;
                float  _CloudCover, _CloudScale, _CloudDrift, _SunGlow;
                float  _CloudWindDir, _CloudWindSpeed, _CloudShear, _CloudBase, _CloudTop, _CloudSteps;
                float  _NightAmount, _StarGrid, _StarBright, _GalaxyAmount;
                float4 _MoonDir, _MoonColor;
                float  _MoonSize, _MoonPhase, _MoonAmount;
                float  _FlareAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // ⚠ 天空盒的视线方向**只能直接取物体空间顶点**（URP 天空盒立方体的
                //   物体矩阵不是"平移到相机"的可依赖形式，TransformObjectToWorld
                //   再减相机位置会得到一片黑——Sky_SimpleBlue 的既定做法）。
                o.dir = v.positionOS.xyz;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1,0));
                float c = Hash21(i + float2(0,1)), e = Hash21(i + float2(1,1));
                return lerp(lerp(a, b, u.x), lerp(c, e, u.x), u.y);
            }
            // 4 倍频（AtmosphereField 云影图**必须逐系数同构**，云影才和天上的云同形）
            float Fbm(float2 p)
            {
                float s = 0.0, amp = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    s += amp * ValueNoise(p);
                    p = p * 2.07 + float2(13.1, 7.3) + k * 0.31;
                    amp *= 0.5;
                }
                return s;
            }
            float2 WindOffset(float hMeters)
            {
                float ra = radians(_CloudWindDir);
                float2 wd = float2(sin(ra), cos(ra));
                return wd * (_CloudWindSpeed * _Time.y + hMeters * _CloudShear);
            }
            #define CLOUD_SCALE 0.00042
            #define COVER_LO 0.70
            #define COVER_HI 0.24
            #define COVER_BAND 0.14

            float CloudDensity(float3 p, float h)
            {
                float covT = lerp(COVER_LO, COVER_HI, _CloudCover);
                float2 q = (p.xz + WindOffset(h)) * CLOUD_SCALE;
                // 原地翻腾（domain-warp 慢流动）：参考 S13 的云 3 秒窗内**全局位移为 0**
                // （同窗互相关峰 0.71 落在原点）——"云速 0.84 %W/s"实为局部形态演化。
                // 只平移不翻腾的云读起来是"贴图滑动"，正是"云有问题"观感的来源之一。
                q += (float2(ValueNoise(q * 0.7 + _Time.y * 0.3),
                             ValueNoise(q * 0.7 + 3.3 - _Time.y * 0.24)) - 0.5) * 0.5;
                float d = smoothstep(covT, covT + COVER_BAND, Fbm(q));
                // 高度剖面：底 120m 软入、顶 40% 起收
                float H = _CloudTop - _CloudBase;
                d *= smoothstep(0.0, 120.0, h) * smoothstep(H, H * 0.40, h);
                if (d > 0.004)
                    d = saturate(d - (ValueNoise(q * 6.3) + ValueNoise(q * 12.9) * 0.5) * 0.22);   // 细节侵蚀两级：云边碎化
                return d;
            }

            // ── 星空（P1-1 保留原样）──
            float3 StarLayer(float3 d, float night, float timeSec)
            {
                float2 uv = float2(atan2(d.z, d.x) * 0.15915494, asin(clamp(d.y, -1.0, 1.0)) * 0.31830989);
                float2 g  = uv * float2(_StarGrid, _StarGrid * 0.5);
                float2 id = floor(g), f = frac(g);
                float h = Hash21(id);
                float3 acc = 0;
                if (h > 0.966)
                {
                    float2 sp = float2(Hash21(id + 17.3), Hash21(id + 41.7));
                    float2 dd = (f - sp) * float2(1.0, 2.0);
                    float b  = (h - 0.966) / 0.034;
                    float tw = 0.65 + 0.35 * sin(timeSec * 2.3 + h * 61.0);
                    float3 tint = lerp(float3(0.78,0.86,1.0), float3(1.0,0.90,0.78), Hash21(id + 7.7));
                    acc = tint * smoothstep(0.18, 0.0, length(dd)) * (0.18 + 1.25 * b * b) * tw;
                }
                return acc * night * _StarBright;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                if (d.y < 0.0) return half4(_GroundColor.rgb, 1.0);

                float3 sunDir = GetMainLight().direction;
                float up = saturate(d.y);

                // ── ① 分层渐变（S12 C24）：低/中色由锚色派生，不新增写入点 ──
                float3 lowC = lerp(_HorizonColor.rgb, _ZenithColor.rgb, 0.32);
                float3 midC = lerp(_HorizonColor.rgb, _ZenithColor.rgb, 0.68);
                float3 col = lerp(_HorizonColor.rgb, lowC, smoothstep(0.00, 0.09, up));
                col = lerp(col, midC, smoothstep(0.07, 0.32, up));
                col = lerp(col, _ZenithColor.rgb, smoothstep(0.28, 0.72, up));
                // 朝/夕双色：太阳侧暖移，只在太阳低时起效（白天正午不变色）
                float sunSide = saturate(dot(normalize(d.xz + 1e-5), normalize(sunDir.xz + 1e-5)))
                                * saturate(1.0 - abs(sunDir.y) * 2.6);
                float3 warm = float3(1.0, 0.58, 0.34);
                col = lerp(col, lerp(col, warm, 0.38), sunSide * 0.65 * (1.0 - _NightAmount));

                // ── 夜空底（星星+银河）画在云前：云用透射率压它 ──
                float3 nightCol = 0;
                if (_NightAmount > 0.001)
                {
                    nightCol = StarLayer(d, _NightAmount, _Time.y);
                    float band = exp(-pow(dot(d, normalize(float3(0.42, 0.50, -0.76))), 2.0) * 16.0);
                    float milky = band * (0.45 + 0.55 * ValueNoise(d.xz * 7.0 + d.y * 4.0));
                    nightCol += float3(0.28, 0.33, 0.50) * milky * _NightAmount * _GalaxyAmount;
                }

                // ── ② 体积云：RayMarch（S13 C26）──
                float cloudT = 1.0;             // 视线云透射率（月面/耀斑共用）
                float3 cloudAcc = 0;
                if (d.y > 0.015)
                {
                    // ⚠ 不能用真球壳求交：Re=6.37e6 下 b²−|ro|²+R² 是 4e13 量级相减，
                    //   fp32 只有 7 位有效数字 ⇒ 判别式全是垃圾 ⇒ 步进永不进入、
                    //   天空只剩静态渐变（实测连拍 4.7s 逐位相同）。
                    //   平流层近似 t=(H−camH)/d.y：所有量 ≤1e5，fp32 安全，
                    //   地平线处的视觉与球壳无差（旧 2D 云也是平面投影）。
                    float camH = max(_WorldSpaceCameraPos.y, 2.0);
                    float t0 = (_CloudBase - camH) / d.y;
                    float t1 = (_CloudTop - camH) / d.y;
                    float2 camXZ = _WorldSpaceCameraPos.xz;
                    int N = (int)clamp(_CloudSteps, 8, 32);
                    float dt = (t1 - t0) / N;
                    float t = t0 + dt * Hash21(IN.positionCS.xy);   // 抖动起点去条带
                    float T = 1.0;
                    float nightDim = lerp(1.0, 0.24, _NightAmount);
                    for (int i = 0; i < N; i++)
                    {
                        float3 p = float3(camXZ.x, 0, camXZ.y) + d * t;
                        float h = (camH - _CloudBase) + t * d.y;     // 云层内相对高度 0..H
                        float dens = CloudDensity(p, h);
                        if (dens > 0.004)
                        {
                            // R5#9 三 tap 太阳行进（180/420/900m 加权）——单 tap 给不出
                            // "云底暗、云顶亮"的立体明暗，是对比度 ×3 缺口的主因之一
                            float dS1 = CloudDensity(p + sunDir * 180.0, h + sunDir.y * 180.0);
                            float dS2 = CloudDensity(p + sunDir * 420.0, h + sunDir.y * 420.0);
                            float dS3 = CloudDensity(p + sunDir * 900.0, h + sunDir.y * 900.0);
                            float dSun = dS1 * 0.5 + dS2 * 0.35 + dS3 * 0.25;
                            float shadow = exp(-(dens * 1.2 + dSun) * 2.6);
                            float phase = 0.62 + 0.38 * pow(saturate(dot(d, sunDir)), 3.0);
                            // 粉末效应：薄处（迎光边）提亮，厚处压住——云边的银线由此来
                            float powder = 1.0 - exp(-dens * 8.0);
                            float Hc = _CloudTop - _CloudBase;
                            // 云顶环境光：高处泛天光蓝白，与云底阴影拉开层次
                            float topAmb = smoothstep(0.25, 1.0, h / Hc) * (1.0 - _NightAmount);
                            float3 lit = lerp(_CloudShadow.rgb, _CloudColor.rgb * 1.45,
                                              saturate(shadow * (0.45 + 0.40 * phase) + powder * 0.30 * shadow));
                            lit += _CloudColor.rgb * 0.20 * topAmb * shadow;
                            // 太阳低时云缘向环境暖色过渡（S13 续卡"云体边缘向环境色"）
                            lit = lerp(lit, lit * float3(1.25, 0.86, 0.66),
                                       saturate(1.0 - sunDir.y * 2.2) * (1.0 - _NightAmount) * 0.5);
                            lit *= nightDim;
                            float a = 1.0 - exp(-dens * dt * 0.0016);
                            cloudAcc += T * a * lit;
                            T *= 1.0 - a;
                            if (T < 0.05) break;
                        }
                        t += dt;
                    }
                    cloudT = T;
                }
                col = col * cloudT + cloudAcc;
                col += nightCol * cloudT;      // 星/银河在云后

                // ── 月面（透光吃体积云透射率）──
                if (_MoonAmount > 0.001)
                {
                    float moonVis = 1.0 - (1.0 - cloudT) * 0.85;
                    float3 md = normalize(_MoonDir.xyz);
                    float mdDot = dot(d, md);
                    float cosR = cos(_MoonSize);
                    float disk = smoothstep(cosR, cosR + 0.00002, mdDot);
                    if (disk > 0.001)
                    {
                        float3 right = normalize(cross(float3(0.0, 1.0, 0.0), md));
                        float3 upv   = cross(md, right);
                        float sinR = sqrt(saturate(1.0 - cosR * cosR));
                        float2 q = float2(dot(d, right), dot(d, upv)) / max(sinR, 1e-4);
                        float k = 1.0 - abs(2.0 * _MoonPhase - 1.0);
                        float term = 1.0 - 2.0 * k;
                        float lit = smoothstep(term - 0.05, term + 0.05, q.x);
                        float maria = 0.86 + 0.14 * ValueNoise(q * 5.0 + 2.7);
                        float3 surf = lerp(_MoonColor.rgb * 0.035, _MoonColor.rgb * maria, lit);
                        col = lerp(col, surf, disk * _MoonAmount * moonVis);
                    }
                    col += _MoonColor.rgb * pow(saturate(mdDot), 260.0) * 0.45 * _MoonAmount * moonVis;
                }

                // ── 日晕 + 太阳附近云镶边 ──
                float sd = saturate(dot(d, sunDir));
                col += _CloudColor.rgb * (pow(sd, 500.0) * 0.45 + pow(sd, 16.0) * 0.18) * _SunGlow * cloudT;
                col += _CloudColor.rgb * (1.0 - cloudT) * pow(sd, 5.0) * 0.30 * (1.0 - _NightAmount);

                // ── ③ LensFlare（S11 C22）：白天型/黄昏型按太阳高度交叉插值 ──
                if (_FlareAmount > 0.01 && _NightAmount < 0.6 && sd > 0.55)
                {
                    float dayW = smoothstep(0.06, 0.18, sunDir.y);
                    float duskW = (1.0 - dayW) * saturate(sunDir.y * 6.0 + 0.4);
                    // 相机前向（鬼影画在"太阳-画面中心"连线上）
                    float3 fwd = normalize(-UNITY_MATRIX_V._m02_m12_m22);
                    float3 g1 = normalize(sunDir + (fwd - sunDir) * 0.85);
                    float3 g2 = normalize(sunDir + (fwd - sunDir) * 1.35);
                    float ghosts = pow(saturate(dot(d, g1)), 900.0) * 0.35
                                 + pow(saturate(dot(d, g2)), 1400.0) * 0.22;
                    // 白天型：小亮核 + 鬼影
                    col += float3(1.0, 0.92, 0.78)
                           * (pow(sd, 3000.0) * 1.6 + ghosts * 0.55) * dayW * cloudT * _FlareAmount;
                    // 黄昏型：大红斑 + 内亮环（R5#10 强度 ×2——max 138 偏柔实锤）
                    col += float3(1.0, 0.42, 0.20) * pow(sd, 24.0) * 1.1 * duskW * cloudT * _FlareAmount;
                    col += float3(1.0, 0.55, 0.30) * pow(sd, 150.0) * 1.7 * duskW * cloudT * _FlareAmount;
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
