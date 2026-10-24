// FFTCausticsFloor.shader — R2 的 S7 演示：单层 FFT 焦散池底
//
// 参考视频 0:42–0:46"单层FFT焦散（纯演示）"：水下平面上，由**单一层**位移场的
// 梯度聚焦出焦散网。这里取 L1（60m 级）梯度做单层源（L0 太钝、L2 太碎）。
// 池底深色调 + 焦散亮网 + 距离衰减，全程不参与光照（演示件）。
Shader "WaterProject/FFTCausticsFloor"
{
    Properties
    {
        _BaseColor ("池底色", Color) = (0.03, 0.10, 0.13, 1)
        _CausticColor ("焦散色", Color) = (0.55, 0.95, 0.90, 1)
        _CausticScale ("焦散增益", Range(0.2, 8)) = 2.6
        _CausticPow ("焦散锐度", Range(0.5, 6)) = 1.8
        _Fade ("可见距离", Float) = 60
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            Texture2D _FFTL1Grad; SamplerState sampler_FFTL1Grad;
            float4 _FFTL1Info;   // (L, fadeA, fadeB, amp)
            Texture2D _FFTL2Grad; SamplerState sampler_FFTL2Grad;
            float4 _FFTL2Info;

            float4 _BaseColor, _CausticColor;
            float _CausticScale, _CausticPow, _Fade;

            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };

            v2f vert(float4 v : POSITION)
            {
                v2f o;
                o.wp = mul(unity_ObjectToWorld, v).xyz;
                o.pos = TransformWorldToHClip(o.wp);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float dist = distance(i.wp.xz, _WorldSpaceCameraPos.xz);
                // 焦散亮网在表面**凹处**（光线被聚焦）：用梯度场的散度=∇²h，
                // 坡度本身太平（0.02 量级）出不了可见网，曲率才是物理对的量
                float2 uv = i.wp.xz / _FFTL1Info.x;
                float2 e = _FFTL1Info.x / 256.0;
                float2 gx1 = _FFTL1Grad.Sample(sampler_FFTL1Grad, uv + float2(e.x, 0)).xy;
                float2 gx0 = _FFTL1Grad.Sample(sampler_FFTL1Grad, uv - float2(e.x, 0)).xy;
                float2 gy1 = _FFTL1Grad.Sample(sampler_FFTL1Grad, uv + float2(0, e.y)).xy;
                float2 gy0 = _FFTL1Grad.Sample(sampler_FFTL1Grad, uv - float2(0, e.y)).xy;
                float curv = ((gx1.x - gx0.x) + (gy1.y - gy0.y)) * 0.5;   // ∇²h
                float c = pow(saturate(-curv * _CausticScale), _CausticPow);
                float3 col = _BaseColor.rgb + _CausticColor.rgb * c;
                col *= saturate(1.0 - dist / _Fade);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
