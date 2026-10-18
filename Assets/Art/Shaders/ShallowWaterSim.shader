// ============================================================================
// ShallowWaterSim.shader — 近岸浅水流场模拟（P0-1，参考 S9 / C14–C17）
//
// 目标：给 OceanScene 补上"全场水流速度场"——参考视频整章（0:56–1:18）在讲的东西。
//   现状差距（见 Docs/学习路径与知识库/检测报告/R01_*.md）：
//     我们的水只有几何起伏（Gerstner/Swell），**没有速度场** ⇒ 水不"流"。
//
// 离散形式：有限体积**通量（管流）**形式 —— 每格与四邻按水位差交换水量，
//   水位 w = 地形 + 水深 h；Δh = dt·Σ k(w_nbr − w)；速度 = 净通量方向。
//   （参考用 MAC 交错网格；本版先用同位网格通量式拿到"会流的水位场"，
//     交错化留待 M3，见 R01 §4。）
//
// 数据布局：
//   _SWState : R=水深h(m)  G=地形y(m)  B=湿标记  A=泡沫密度(留给 P1-2)
//   _SWVel   : R=u  G=v（净通量，供可视化与后续泡沫平流）
//   _SWTerrain: R=地形y（初始化与可视化用；地形由 Editor 端采样真实网格烘焙，
//              遵守《05》"岸线只写一处 / 采样实际网格"纪律）
//
// Pass 0  Height  ：state(cur) → stateNext（含干湿钳制 / 边界吸收 / 摩擦阻尼）
// Pass 1  Vel     ：state(cur) → velNext
// Pass 2  Viz     ：state+vel → 可视化（1=干湿网格 蓝湿/橙干，2=水位+速度箭头，3=水深）
// ============================================================================
Shader "WaterProject/ShallowWaterSim"
{
    Properties
    {
        _SWDomain   ("域 (minX,minZ,sizeX,sizeZ)", Vector) = (0,0,1,1)
        _SWGridN    ("网格 N", Float) = 256
        _SWWaterY   ("静水位 y (m)", Float) = 0
        _SWDt       ("时间步 dt", Float) = 0.0166667
        _SWFlowK    ("流动系数 k", Float) = 0.9
        _SWDamping  ("摩擦阻尼/步", Range(0.9,1)) = 0.997
        _SWEdgeBand ("边界吸收带宽(UV)", Range(0,0.3)) = 0.05
        _SWEdgeK    ("边界吸收强度", Range(0,1)) = 0.85
        _SWVizMode  ("可视化 1干湿/2水位速度/3水深/4速度模长", Float) = 1
        _SWWaveOn   ("涌浪驱动 0关/1开", Float) = 1
        _SWWaveAmp  ("涌浪振幅 (m)", Float) = 0.85
        _SWWaveLen  ("涌浪波长 (m)", Float) = 74
        _SWWaveDir  ("涌浪传播方向 (x,z)", Vector) = (-0.175, 0.985, 0, 0)
        _SWWaveSpeed("涌浪传播速度倍率", Float) = 1
        _SWWaveForce("边界驱动接管强度", Range(0,1)) = 0.9
        _SWTime     ("模拟时间 (s)", Float) = 0

        // ── 泡沫历史场（P1-2 / 参考 S10 C18–C20）──
        _SWFoamDecay   ("泡沫衰减 /秒", Float) = 0.6
        _SWFoamGen     ("泡沫生成速率", Float) = 1.5
        _SWFoamAdvect  ("泡沫平流倍率", Float) = 1.0
        _SWFoamShore   ("近岸生成权重", Float) = 1.0
        _SWFoamShoreD  ("近岸生成水深(m)", Float) = 2.5
        _SWFoamCrest   ("浪峰曲率权重", Float) = 0.8
        _SWFoamSlope   ("水面坡度权重", Float) = 0.6
        _SWFoamFlow    ("流速生成权重", Float) = 0.8
        _SWFoamFlowRef ("流速参考值", Float) = 0.05
        _SWFoamNoise   ("泡沫噪声分量", Range(0,1)) = 0.75
        _SWFoamNoiseScale ("噪声尺度(1/米)", Float) = 0.08
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // ───────────────────────── Pass 0：水深更新 ─────────────────────────
        Pass
        {
            Name "SWHeight"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragHeight
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SWState);   SAMPLER(sampler_SWState);
            TEXTURE2D(_SWTerrain); SAMPLER(sampler_SWTerrain);
            float _SWGridN, _SWWaterY, _SWDt, _SWFlowK, _SWDamping, _SWEdgeBand, _SWEdgeK;
            float4 _SWDomain;
            float _SWWaveOn, _SWWaveAmp, _SWWaveLen, _SWWaveSpeed, _SWWaveForce, _SWTime;
            float4 _SWWaveDir;

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }

            float2 SurfAt(float2 uv, out float terr, out float h)
            {
                float4 s = SAMPLE_TEXTURE2D_LOD(_SWState, sampler_SWState, uv, 0);
                terr = s.g; h = s.r;
                return float2(terr + h, h);
            }

            // 边界吸收：越靠域边越强（防"波浪在模拟边界反弹"）
            float EdgeW(float2 uv)
            {
                float2 d = min(uv, 1.0 - uv);
                float e = saturate(min(d.x, d.y) / max(_SWEdgeBand, 1e-4));
                return lerp(1.0 - _SWEdgeK, 1.0, e);
            }

            float4 fragHeight(V i) : SV_Target
            {
                float2 uv = i.uv;
                float texel = 1.0 / max(_SWGridN, 1.0);

                float tC, hC; float2 sc = SurfAt(uv, tC, hC);
                float tL, hL; float2 sL = SurfAt(uv - float2(texel, 0), tL, hL);
                float tR, hR; float2 sR = SurfAt(uv + float2(texel, 0), tR, hR);
                float tU, hU; float2 sU = SurfAt(uv + float2(0, texel), tU, hU);
                float tD, hD; float2 sD = SurfAt(uv - float2(0, texel), tD, hD);

                // 四邻向本格的通量（正=水从该邻流入）
                float k = _SWFlowK;
                float gL = k * (sL.x - sc.x);
                float gR = k * (sR.x - sc.x);
                float gU = k * (sU.x - sc.x);
                float gD = k * (sD.x - sc.x);

                // 干格只进不出（避免把邻格抽成负水深）
                float outSum = max(-gL, 0) + max(-gR, 0) + max(-gU, 0) + max(-gD, 0);
                float inSum  = max( gL, 0) + max( gR, 0) + max( gU, 0) + max( gD, 0);
                if (hC <= 1e-4) outSum = 0.0;                 // 干格：不外流

                float dh = (inSum - outSum) * _SWDt * EdgeW(uv);
                float hNew = hC + dh;
                if (hNew < 0.0) hNew = 0.0;                    // 水深非负
                hNew *= _SWDamping;                            // 摩擦/能量耗散

                // ── 涌浪驱动（参考 C17）：外海按"岸线影响权重"把水位拉到涌浪高，波随模拟向岸传播 ──
                if (_SWWaveOn > 0.5)
                {
                    float off = SAMPLE_TEXTURE2D_LOD(_SWTerrain, sampler_SWTerrain, uv, 0).g;
                    float2 worldXZ = _SWDomain.xy + uv * _SWDomain.zw;
                    float k = 6.2831853 / max(_SWWaveLen, 1.0);
                    float omega = sqrt(9.81 * k) * max(_SWWaveSpeed, 0.01);
                    float2 wd = normalize(_SWWaveDir.xy + 1e-6);
                    float phase = k * dot(wd, worldXZ) - omega * _SWTime;
                    float wTarget = _SWWaveAmp * sin(phase);
                    // 离岸越远接管越强（岸线影响权重）；近岸让模拟自己演化
                    float infl = saturate((off - 30.0) / 60.0) * _SWWaveForce;
                    float wNow = tC + hNew;
                    hNew = max(lerp(wNow, wTarget, infl) - tC, 0.0);
                }

                // 水面若被地形顶到（新格转湿）由 hNew 自然表达；地形原样透传
                float wet = hNew > 1e-4 ? 1.0 : 0.0;
                float foam = SAMPLE_TEXTURE2D_LOD(_SWState, sampler_SWState, uv, 0).a * _SWDamping;
                return float4(hNew, tC, wet, foam);
            }
            ENDHLSL
        }

        // ───────────────────────── Pass 1：速度场 ─────────────────────────
        Pass
        {
            Name "SWVel"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragVel
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SWState);   SAMPLER(sampler_SWState);
            float _SWGridN, _SWFlowK, _SWDamping;

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }

            float Surf(float2 uv) { float4 s = SAMPLE_TEXTURE2D_LOD(_SWState, sampler_SWState, uv, 0); return s.g + s.r; }

            float4 fragVel(V i) : SV_Target
            {
                float2 uv = i.uv;
                float texel = 1.0 / max(_SWGridN, 1.0);
                float w  = Surf(uv);
                float wL = Surf(uv - float2(texel, 0));
                float wR = Surf(uv + float2(texel, 0));
                float wU = Surf(uv + float2(0, texel));
                float wD = Surf(uv - float2(0, texel));

                float k = _SWFlowK * _SWDamping;
                // 净通量方向：右邻更高 ⇒ 向 +x 流
                float u = k * (wR - wL) * 0.5;
                float v = k * (wU - wD) * 0.5;
                return float4(u, v, 0, 1);
            }
            ENDHLSL
        }

        // ───────────────────────── Pass 2：可视化 ─────────────────────────
        Pass
        {
            Name "SWViz"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragViz
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SWState);   SAMPLER(sampler_SWState);
            TEXTURE2D(_SWVel);     SAMPLER(sampler_SWVel);
            TEXTURE2D(_SWTerrain); SAMPLER(sampler_SWTerrain);
            TEXTURE2D(_SWFoam);    SAMPLER(sampler_SWFoam);
            float _SWGridN, _SWVizMode;

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }

            float4 fragViz(V i) : SV_Target
            {
                float2 uv = i.uv;
                float4 s = SAMPLE_TEXTURE2D(_SWState, sampler_SWState, uv);
                float2 vel = SAMPLE_TEXTURE2D(_SWVel, sampler_SWVel, uv).rg;

                if (_SWVizMode < 1.5)
                {
                    // 模式 1：干湿网格（参考 f1700「模拟网格与干湿边界」）
                    //   湿 = 蓝，干 = 橙，格线 = 白
                    float3 wetCol = float3(0.18, 0.62, 0.95);
                    float3 dryCol = float3(0.98, 0.45, 0.22);
                    float3 col = lerp(dryCol, wetCol, s.b);
                    float2 g = frac(uv * _SWGridN);
                    float gl = min(min(g.x, 1 - g.x), min(g.y, 1 - g.y));
                    col = lerp(float3(1, 1, 1), col, smoothstep(0.0, 0.06, gl));
                    return float4(col, 1);
                }
                else if (_SWVizMode < 2.5)
                {
                    // 模式 2：水位场 + 速度箭头（参考 f1850「高度差推动速度」）
                    float w = s.g + s.r;                       // 水位
                    float t = saturate(w * 0.5 + 0.5);         // 归一到 0..1（尺度可调）
                    float3 col = lerp(float3(0.15, 0.15, 0.55), float3(1.0, 0.25, 0.15), t);
                    // 每 8 格画一个速度箭头（青色）
                    float2 c = uv * _SWGridN / 8.0;
                    float2 loc = frac(c) - 0.5;
                    float2 dir = normalize(vel + 1e-6);
                    float len = saturate(length(vel) * 200.0) * 0.42;
                    float along = dot(loc, dir);
                    float perp  = abs(dot(loc, float2(-dir.y, dir.x)));
                    // 杆 + 箭头（沿方向的一段细线，加一个靠近末端的加宽三角）
                    float shaft = (abs(along) < len * 0.5 && perp < 0.045) ? 1.0 : 0.0;
                    float head  = (along > len * 0.2 && along < len * 0.5 && perp < (len * 0.5 - along) * 0.55 + 0.02) ? 1.0 : 0.0;
                    float arrow = saturate(shaft + head) * step(1e-4, length(vel));
                    col = lerp(col, float3(0.0, 0.95, 0.95), arrow);
                    return float4(col, 1);
                }
                else if (_SWVizMode < 3.5)
                {
                    // 模式 3：水深（灰）
                    return float4(s.rrr, 1);
                }
                else if (_SWVizMode < 4.5)
                {
                    // 模式 4：速度模长（灰：0→黑，≥0.2 m/s→白）——用于边界吸收量化
                    float sp = saturate(length(vel) / 0.2);
                    return float4(sp.xxx, 1);
                }
                else
                {
                    // 模式 5：泡沫历史密度（P1-2；灰=密度）
                    float foam = SAMPLE_TEXTURE2D(_SWFoam, sampler_SWFoam, uv).r;
                    return float4(foam.xxx, 1);
                }
            }
            ENDHLSL
        }

        // ───────────────────────── Pass 3：泡沫历史场（P1-2 / S10 C18–C20）─────────────────────────
        //   三元模型：① 多源生成（近岸浅水 + 水面曲率/坡度 + 流速）
        //             ② 半拉格朗日平流（沿流速回溯上一帧）
        //             ③ 线性/指数衰减（"浪过留痕、随时间淡去"）
        //   输出 R = 泡沫密度（世界 XZ 网格，供水面 shader 按世界坐标采样合成）
        Pass
        {
            Name "SWFoam"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFoam
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SWState);    SAMPLER(sampler_SWState);
            TEXTURE2D(_SWVel);      SAMPLER(sampler_SWVel);
            TEXTURE2D(_SWFoamPrev); SAMPLER(sampler_SWFoamPrev);
            float _SWGridN, _SWFlowK, _SWDamping, _SWTime;
            float4 _SWDomain;
            float _SWFoamDt, _SWFoamDecay, _SWFoamGen, _SWFoamAdvect;
            float _SWFoamShore, _SWFoamShoreD, _SWFoamCrest, _SWFoamSlope;
            float _SWFoamFlow, _SWFoamFlowRef, _SWFoamNoise, _SWFoamNoiseScale;

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }

            // 生成源要有**丝缕状结构**，否则整个冲浪带是一块均匀白板（本轮第二版）
            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1,0)), u.x),
                            lerp(Hash12(i + float2(0,1)), Hash12(i + float2(1,1)), u.x), u.y);
            }

            // 返回 float2(水面高 = 地形+水深, 水深)
            float2 SurfD(float2 uv)
            {
                float4 s = SAMPLE_TEXTURE2D_LOD(_SWState, sampler_SWState, uv, 0);
                return float2(s.g + s.r, s.r);
            }

            float4 fragFoam(V i) : SV_Target
            {
                float2 uv = i.uv;
                float texel = 1.0 / max(_SWGridN, 1.0);
                float4 sc = SAMPLE_TEXTURE2D_LOD(_SWState, sampler_SWState, uv, 0);
                float wet = sc.b, depth = sc.r;
                float2 vel = SAMPLE_TEXTURE2D_LOD(_SWVel, sampler_SWVel, uv, 0).rg;

                // ① 半拉格朗日平流：回溯一步（世界米 → UV）
                float2 back = vel * _SWFoamAdvect * _SWFoamDt;
                float2 prevUV = uv - back / max(_SWDomain.zw, 1e-3);
                prevUV = saturate(prevUV);
                float foam = SAMPLE_TEXTURE2D_LOD(_SWFoamPrev, sampler_SWFoamPrev, prevUV, 0).r;

                // ② 衰减（浪过留痕、随时间淡去）
                foam *= exp(-_SWFoamDecay * _SWFoamDt);

                // ③ 生成源
                float2 sC = SurfD(uv);
                float2 sL = SurfD(uv - float2(texel, 0));
                float2 sR = SurfD(uv + float2(texel, 0));
                float2 sU = SurfD(uv + float2(0, texel));
                float2 sD = SurfD(uv - float2(0, texel));
                float lap   = (sL.x + sR.x + sU.x + sD.x) - 4.0 * sC.x;   // 水面拉普拉斯（米）
                float2 grad = float2(sR.x - sL.x, sU.x - sD.x) * 0.5;      // 水面坡度（米）
                // ⚠ 生成必须**只发生在破碎区（浅水）**：否则深水区的涌浪曲率/坡度也会一直产沫，
                //   历史场被填满 → 海面糊成一张白纸（本轮第一版就是这么翻车的：34% 像素饱和）。
                float shoal    = saturate(1.0 - depth / max(_SWFoamShoreD, 1e-3)) * wet;  // 整个冲浪带
                float shoreBand= saturate(1.0 - depth / max(_SWFoamShoreD * 0.25, 0.05)); // 最浅的碎浪带
                float curvGen  = saturate(-lap * _SWFoamCrest);            // 浪峰（上凸）→ 正
                float slopeGen = saturate(length(grad) * _SWFoamSlope);
                float flowGen  = saturate(length(vel) / max(_SWFoamFlowRef, 1e-4) - 1.0);

                float gen = saturate(shoreBand * _SWFoamShore
                                   + shoal * (curvGen + slopeGen + flowGen * _SWFoamFlow));
                // 丝缕化：世界 XZ 上缓慢漂移的噪声把"生成源"咬成指纹状，随模拟时间缓慢漂移
                float2 wxz = _SWDomain.xy + uv * _SWDomain.zw;
                float nz = VNoise(wxz * _SWFoamNoiseScale + _SWTime * 0.12);
                gen *= lerp(1.0, saturate((nz - 0.30) * 1.8), _SWFoamNoise);
                foam = saturate(foam + gen * _SWFoamGen * _SWFoamDt) * step(1e-4, wet);

                return float4(foam, 0, 0, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
