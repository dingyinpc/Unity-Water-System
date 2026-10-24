// ============================================================================
// PoolWaveUpdate.shader — 泳池波动方程的一步更新（R21 手感 v2：双场 + 物理波速）
//
// v1 的手感三宗罪（R21 诊断）：
//   ① 波速 3.98 m/s（CFL 上限 × 120 步/秒）——真实泳池涟漪 0.8~1.3 m/s，
//     环像鼓面一样"啪"地弹出去；R13 测到的 1.33Hz 闪烁主频正是 3.98/3m
//     的池域驻波（本版降到 ~0.38Hz，出闪烁带）。
//   ② 无色散——所有波长同速，环串永远等距，不像真水大小环会分离。
//   ③ 冲量是纯高斯（只会"鼓包"），没有"水被压下去、沿口隆起"的入水读感。
//
// v2 方案：
//   · 同一张 RGhalf：R=长波场（可见的主涟漪环，c≈1.15 m/s）、
//     G=细纹理场（c≈0.45 m/s，跟在主环后面的毛细样碎波）——两场不同速
//     = 天然色散，成本零增加（还是一次 Blit）。
//   · 递推里加"轴向均值耦合"项：压掉棋盘/奇偶驻波（v1 高频闪烁的数值根源），
//     顺带当高频粘性用，场更"水"更少"格"。
//   · 冲量改成环形剖面：负核 + 正沿（脚/人把水压下去，四周隆起一圈）。
//
// 递推（每通道同构，K/ε/阻尼不同）：
//   next = 2·curr − prev + K·(四邻和−4·curr) + ε·(四邻均值−curr)，再乘阻尼
//   波速 c = dx·√K/dt（dx=4.6875cm，dt=1/60 ⇒ K=0.167 → c≈1.15，K=0.026 → c≈0.45）
//   稳定域 K+ε/4 ≤ 0.5（vNeuman），A 档 0.180 / B 档 0.049，余量充足。
//
// 步进节奏由 PoolRipples 以固定 60Hz 累加器驱动（不再每帧×2），
// 波速从此与帧率解耦、也不再吃 CFL 上限。
// ============================================================================
Shader "WaterProject/PoolWaveUpdate"
{
    Properties
    {
        _Curr ("当前高度场(RG)", 2D) = "black" {}
        _Prev ("上一步高度场(RG)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Curr); SAMPLER(sampler_Curr);
            TEXTURE2D(_Prev); SAMPLER(sampler_Prev);

            float4 _Texel;              // (1/w, 1/h, w, h)——PoolRipples 显式传入，
                                        // 不用 _Curr_TexelSize（Blit(null,dst) 通路实测不填充）
            float2 _FieldSize;          // 模拟域世界尺寸（米），把像素距离换算成米
            float4 _Damps;              // (阻尼A, 阻尼B, 0, 0) 每步乘性阻尼
            float4 _WaveKs;             // (K_A, K_B, 0, 0) 波速系数 c = dx·√K/dt
            float4 _Smoothes;           // (ε_A, ε_B, 0, 0) 轴向均值耦合（反棋盘+粘性）
            float4 _Drops[8];           // (uv.x, uv.y, 冲量强度, 0)
            int    _DropCount;
            float4 _DropShapes;         // (σ_A, σ_B, 增益A, 增益B)

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings  { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                // 全屏三角，不依赖任何网格
                float2 uv = float2((v.vertexID << 1) & 2, v.vertexID & 2);
                Varyings o;
                o.pos = float4(uv * 2.0 - 1.0, 0, 1);
                o.uv = uv;
                return o;
            }

            // 入水冲量剖面：正沿(0.55σ 处的隆起) − 0.55·负核(中心压低)。
            // 纯高斯(v1)只会鼓包；这个剖面第一步就直接长成"环"，入水/落脚读感立现。
            float2 DropShape(float dMeter)
            {
                float sA = _DropShapes.x, sB = _DropShapes.y;
                float2 ring = exp(-pow((dMeter - 0.55 * float2(sA, sB)) / (0.32 * float2(sA, sB)), 2.0));
                float2 core = exp(-pow(dMeter / (0.60 * float2(sA, sB)), 2.0));
                return ring - 0.55 * core;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 tx = _Texel.xy;
                float2 c = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv, 0).rg;
                float2 p = SAMPLE_TEXTURE2D_LOD(_Prev, sampler_Prev, i.uv, 0).rg;
                float2 l = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv - float2(tx.x, 0), 0).rg;
                float2 r = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv + float2(tx.x, 0), 0).rg;
                float2 u = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv + float2(0, tx.y), 0).rg;
                float2 d = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv - float2(0, tx.y), 0).rg;

                float2 sum4 = l + r + u + d;
                // 波动项 + 反棋盘/粘性项（ε 对棋盘模式是强负反馈，对正常波只是轻微平滑）
                float2 n = 2.0 * c - p
                         + (sum4 - 4.0 * c) * _WaveKs.xy
                         + (sum4 * 0.25 - c) * _Smoothes.xy;
                n *= _Damps.xy;
                // 安全钳制：涟漪正常幅度 <0.5，±1.5 只兜底数值异常（域重载竞态等）
                n = clamp(n, -1.5, 1.5);

                // 本帧交互：一次性环形冲量（uv 距离换算成米再量半径）
                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    if (k >= _DropCount) break;
                    float2 dd = (i.uv - _Drops[k].xy) * _FieldSize;   // 米
                    n += _Drops[k].z * DropShape(length(dd)) * _DropShapes.zw;
                }

                return half4(n, 0, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
