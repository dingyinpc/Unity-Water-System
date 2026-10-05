// ============================================================================
// Pool_Ball.shader — 沙滩球：6 色瓣 + 极点白帽，程序化零贴图
//
// 瓣纹 = 方位角 atan2(z,x) 均分 6 瓣 + 哈希明度微扰；极圈按 |y| 淡白。
// 光照：主光 ndl + SH 环境 + 菲涅尔边缘亮——泳池玩具要"轻、亮、塑料感"。
// ============================================================================
Shader "WaterProject/Pool_Ball"
{
    Properties
    {
        _ColA ("瓣色A", Color) = (0.95, 0.30, 0.28, 1)
        _ColB ("瓣色B", Color) = (0.98, 0.82, 0.25, 1)
        _ColC ("瓣色C", Color) = (0.25, 0.65, 0.92, 1)
        _Pole ("极帽白", Range(0, 0.5)) = 0.18
        _AmbStrength ("环境光强度", Float) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColA, _ColB, _ColC;
                float _Pole, _AmbStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos  : TEXCOORD0;
                float3 normalWS  : TEXCOORD1;
                float3 localPos  : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.worldPos = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = normalize(TransformObjectToWorldNormal(v.normalOS));
                o.localPos = v.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(o.worldPos);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 局部坐标 → 球面瓣纹（球本体半径 0.5，归一后与缩放无关）
                float3 n = normalize(IN.localPos);
                float az = atan2(n.z, n.x);                 // -π..π
                float wedges = (az / 6.2831853 + 0.5) * 6.0;
                float wi = floor(frac(wedges) * 3.0);       // 0/1/2 轮换三色
                float3 col = wi < 0.5 ? _ColA.rgb : (wi < 1.5 ? _ColB.rgb : _ColC.rgb);
                // 极帽
                float pole = saturate((abs(n.y) - (1.0 - _Pole)) / _Pole);
                col = lerp(col, float3(0.96, 0.96, 0.95), pole);

                Light L = GetMainLight();
                float3 N = normalize(IN.normalWS);
                float ndl = saturate(dot(N, L.direction));
                half3 amb = SampleSH(N);
                float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0) * 0.25;   // 塑料边缘亮
                float3 outCol = col * (L.color * ndl + amb * _AmbStrength) + rim * L.color;
                return half4(outCol, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
