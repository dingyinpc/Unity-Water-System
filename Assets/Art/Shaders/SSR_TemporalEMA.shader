// ============================================================================
// SSR_TemporalEMA.shader — R22 C7 抗闪：SSR 结果的时域累积（历史帧 EMA）
//
// 输入：_TraceTex（本帧水面预通道写的 rgb=SSR色 a=conf）
//       _HistTex（上一帧累积结果）
// 输出：rgb=累积色 a=累积conf
//
// 融合策略（静态相机假设，Main 验收机位满足）：
//   · conf 门控：新帧 conf≈0（追踪 miss）不立刻清零历史——闪灭正是椒盐来源；
//     但历史连续 miss 时指数遗忘（0.88/帧 ⇒ ~8 帧淡出）；
//   · 新帧 conf 高时快跟（0.45），低时慢跟（0.15）——高置信信息优先。
// ============================================================================
Shader "Hidden/WaterProject/SSR_TemporalEMA"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_TraceTex); SAMPLER(sampler_TraceTex);
            TEXTURE2D(_HistTex);  SAMPLER(sampler_HistTex);

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings  { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                float2 uv = float2((v.vertexID << 1) & 2, v.vertexID & 2);
                Varyings o;
                o.pos = float4(uv * 2.0 - 1.0, 0, 1);
                o.uv = uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 tr = SAMPLE_TEXTURE2D(_TraceTex, sampler_TraceTex, i.uv);
                float4 hi = SAMPLE_TEXTURE2D(_HistTex,  sampler_HistTex,  i.uv);

                // 新帧权重：新 conf 越高跟越快
                float w = lerp(0.15, 0.45, saturate(tr.a));
                // 历史 conf 的遗忘/保持：新帧 miss 时历史按 0.88 衰减（不闪灭）
                float histKeep = hi.a * (tr.a > 0.02 ? 1.0 : 0.88);
                float conf = lerp(histKeep, tr.a, w);

                // 色：按两侧 conf 加权；两侧都 miss ⇒ 输出黑（调用方有天空兜底）
                float wa = tr.a * w, wb = histKeep * (1.0 - w);
                float3 col = (tr.rgb * wa + hi.rgb * wb) / max(wa + wb, 1e-3);
                conf = saturate(conf);
                return half4(col, conf);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
