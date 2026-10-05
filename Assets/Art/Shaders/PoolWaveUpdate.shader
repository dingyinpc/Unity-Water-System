// ============================================================================
// PoolWaveUpdate.shader — 泳池波动方程的一步更新（B 档交互，见《07》§1）
//
//   next = (curr 四邻之和) × 0.5 − prev，再乘阻尼，再叠本帧交互冲量（高斯）
//
// 双缓冲：curr=上一步、prev=上上步，**写回目标就是 prev 那张**
//（prev 在被读走一个中心点后就不再需要），写完交换角色——两张 RT 轮转，
// 不需要第三张。采样用 Clamp：池壁 = Neumann 边界 = **波到墙自动反弹**，
// 这是泳池线白送的物理。
//
// 离散稳定性：系数 0.5 恰好是 2D 波动方程显式格式的 CFL 边界（无条件稳定于
// 此系数），只可能"太平"不可能"爆炸"；要更快传播就每帧多跑一步（PoolRipples）。
//
// 为什么不用 compute shader：Graphics.Blit(null, rt, mat) 与主线
// WaterInteractionManager 的先例同构，探针/桥工具链零新增。
// ============================================================================
Shader "WaterProject/PoolWaveUpdate"
{
    Properties
    {
        _Curr ("当前高度场", 2D) = "black" {}
        _Prev ("上一步高度场", 2D) = "black" {}
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
            float  _Damping;
            float4 _Drops[8];           // (uv.x, uv.y, 冲量强度, 0)
            int    _DropCount;
            float  _DropSigma;          // 高斯半径（米）

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

            half4 frag(Varyings i) : SV_Target
            {
                float2 tx = _Texel.xy;
                float c = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv, 0).r;
                float p = SAMPLE_TEXTURE2D_LOD(_Prev, sampler_Prev, i.uv, 0).r;
                float l = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv - float2(tx.x, 0), 0).r;
                float r = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv + float2(tx.x, 0), 0).r;
                float u = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv + float2(0, tx.y), 0).r;
                float d = SAMPLE_TEXTURE2D_LOD(_Curr, sampler_Curr, i.uv - float2(0, tx.y), 0).r;

                float n = (l + r + u + d) * 0.5 - p;
                n *= _Damping;
                // 安全钳制：本方案在 CFL 边界（k=0.5）天然 |g|=1，数值异常时
                // 场会整体漂移把水面顶上天（实测一次域重载竞态）。涟漪正常幅度
                // <0.5，±1.5 的钳制永远碰不到，只兜底。
                n = clamp(n, -1.5, 1.5);

                // 本帧交互：一次性高斯冲量（uv 距离换算成米再量半径）
                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    if (k >= _DropCount) break;
                    float2 dd = (i.uv - _Drops[k].xy) * _FieldSize;   // 米
                    float  m2 = dot(dd, dd);
                    n += _Drops[k].z * exp(-m2 / (2.0 * _DropSigma * _DropSigma));
                }

                return half4(n, 0, 0, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
