// WaterPlant.shader — R33 水下植被（水草）的着色与摇摆
//
// 定位：放在水下、透过水面折射被看到的内容资产（R31 内容资产轮的 Pond#2/Main#2）。
//   · 顶点：两频正弦摇摆，弯度按"高度比例²"加权（根不动梢最摆），相位取世界坐标哈希
//     ——成片草各自相位不同，看起来是"一群在流里晃"，不是"一块布在飘"。
//   · 片元：主光 Lambert + 天光近似；颜色 = 根暗→梢亮的高度渐变 × 每株顶点色变体；
//     再按**水深**往水色调压（越深越绿越暗，模拟水体吸收——不做真折射，透过
//     Water_* 的透明混合与折射偏移已经足够"水下感"）。
//   · 队列 Geometry（先于透明水面画，水面叠在上面做折射/倒影）。
// 网格由菜单（R33ContentSetup）程序化生成：世界坐标合并网格，mesh 在原点。
Shader "WaterProject/WaterPlant"
{
    Properties
    {
        _BaseColor   ("根部色", Color) = (0.035, 0.085, 0.030, 1)
        _TipColor    ("梢部色", Color) = (0.150, 0.300, 0.105, 1)
        _WaterTint   ("水下压制色", Color) = (0.030, 0.100, 0.090, 1)
        _TintDepth   ("水下压制深度(米)", Float) = 1.1
        _SwayAmp     ("摇摆幅度(米)", Float) = 0.055
        _SwaySpeed   ("摇摆速度", Float) = 1.15
        _TipLight    ("梢部透光", Range(0, 2)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Off   // 叶片双面：背面全黑会闪，双面同色最稳

        Pass
        {
            Name "PlantForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _TipColor, _WaterTint;
                float  _TintDepth, _SwayAmp, _SwaySpeed, _TipLight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // r=每株明度变体
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  hFrac      : TEXCOORD2;  // 0=根 1=梢
                float  variant    : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);

                // 摇摆：两频 + 世界哈希相位；弯度 ∝ 高度²（根稳梢摆）
                float ph1 = _Time.y * _SwaySpeed + wp.x * 0.9 + wp.z * 0.7;
                float ph2 = _Time.y * _SwaySpeed * 1.73 + wp.z * 1.3 - wp.x * 0.4;
                float bend = v.uv.y * v.uv.y;
                float2 sway = float2(sin(ph1) * 0.8 + sin(ph2) * 0.45,
                                     cos(ph1 * 0.87) * 0.6 + sin(ph2 * 1.31) * 0.35);
                wp.xz += sway * _SwayAmp * bend;

                o.worldPos = wp;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.hFrac = v.uv.y;
                o.variant = v.color.r;
                o.positionCS = TransformWorldToHClip(wp);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Light L = GetMainLight();
                float3 N = normalize(i.normalWS);
                float ndl = saturate(dot(N, L.direction)) * 0.75 + abs(N.y) * 0.25;  // 双面近似

                // 根暗梢亮 + 梢部透光（太阳穿过薄叶的感觉）
                float3 col = lerp(_BaseColor.rgb, _TipColor.rgb, i.hFrac) * i.variant;
                col += _TipColor.rgb * _TipLight * i.hFrac * i.hFrac * saturate(0.35 + ndl);

                float3 ambient = SampleSH(N) * 0.9;
                col = col * (ambient + L.color * ndl * 0.85);

                // 水深压制：越深越往水色调（草在水下不该有水面上的鲜绿）
                float sub = saturate(-i.worldPos.y / max(_TintDepth, 0.1));
                col = lerp(col, _WaterTint.rgb, sub * 0.55);

                col = MixFog(col, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
