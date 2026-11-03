// DistantLand.shader — 远景低模陆地的"读法修复"材质
//
// 问题（R63/R64）：OceanScene 的 `OceanHeadland` 是一块 579×55×484 的低模体，
//   平顶 + 均匀色 ⇒ 在天空前读成一块**灰白平板**。换基色只能改颜色，改不了"板感"。
//
// 本材质做三件事，全部只为"读起来像远处的陆地"：
//   1) **高度渐变**：底部深、顶部浅（空气散射在低处更厚）；
//   2) **噪声明暗**：低频斑驳，破掉均匀色块（不增加几何）；
//   3) **雾**：接 MixFog，与水面/地形同一条雾。
// 光照用最简 Lambert + 球谐环境光，不做高光。
Shader "WaterProject/DistantLand"
{
    Properties
    {
        _LowColor   ("底部色", Color) = (0.16, 0.18, 0.20, 1)
        _HighColor  ("顶部色", Color) = (0.34, 0.39, 0.46, 1)
        _HazeColor  ("高处雾色", Color) = (0.58, 0.66, 0.76, 1)
        _HazeHeight ("雾色起始高度(米)", Float) = 18
        _HazeSoft   ("雾色过渡(米)", Float) = 26
        _NoiseScale ("斑驳尺度(1/米)", Float) = 0.06
        _NoiseAmount("斑驳强度", Range(0, 1)) = 0.35
        _BaseY      ("基准高度(世界Y)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LowColor, _HighColor, _HazeColor;
                float  _HazeHeight, _HazeSoft, _NoiseScale, _NoiseAmount, _BaseY;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionCS.xyz;
                o.positionWS = p.positionWS;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float h = saturate((i.positionWS.y - _BaseY) / max(_HazeHeight, 1.0));
                float3 albedo = lerp(_LowColor.rgb, _HighColor.rgb, h);

                // 低频斑驳：破掉"一整块均匀色"
                float n = VNoise(i.positionWS.xz * _NoiseScale) * 0.65
                        + VNoise(i.positionWS.xz * _NoiseScale * 3.7) * 0.35;
                albedo *= lerp(1.0, 0.7 + 0.6 * n, _NoiseAmount);

                Light L = GetMainLight();
                float ndl = saturate(dot(normalize(i.normalWS), L.direction));
                float3 col = albedo * (SampleSH(normalize(i.normalWS)) + L.color * ndl * 0.8);

                // 高处往雾色靠（空气散射）
                float haze = smoothstep(_HazeHeight, _HazeHeight + _HazeSoft, i.positionWS.y - _BaseY);
                col = lerp(col, _HazeColor.rgb, haze * 0.75);

                col = MixFog(col, i.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
