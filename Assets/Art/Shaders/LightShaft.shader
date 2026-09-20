// LightShaft.shader — 林间光柱（加性、不写深度）
//
// 只做一件事：把"上方有光透下来"这件事画成一个可见的体积暗示。
// 不是真体积光，是一组加性双面四边形，靠 UV 衰减把边缘软掉。
//
// 关键约束：
//   · 加性混合 + ZWrite Off —— 否则会把后面的树遮挡成黑块
//   · 必须排在透明队列最后面，让它叠在水面/树之上而不是被它们盖掉
//   · 强度刻意压得很低：光柱一旦显眼就变成"舞台灯"，不再是林间漏光
Shader "WaterProject/LightShaft"
{
    Properties
    {
        _ShaftColor ("光色", Color) = (1.0, 0.92, 0.72, 1)
        _Intensity  ("强度", Range(0, 1)) = 0.14
        _Softness   ("边缘软硬", Range(0.2, 4)) = 1.6
        _Flicker    ("闪烁幅度", Range(0, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20"
               "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }

        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha One            // 加性

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShaftColor;
                float  _Intensity;
                float  _Softness;
                float  _Flicker;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // uv.x 沿柱体截面（两端软），uv.y 从底(0)到顶(1)
                float side = abs(IN.uv.x * 2.0 - 1.0);
                float edge = pow(1.0 - side, _Softness);

                // 顶端最亮（刚出冠层），往下散掉；底部几乎看不见
                float along = pow(saturate(IN.uv.y), 1.4) * (1.0 - 0.55 * saturate(IN.uv.y - 0.75) * 4.0);

                float flick = 1.0 + _Flicker * sin(_Time.y * 0.7 + IN.uv.y * 3.0);

                half3 col = _ShaftColor.rgb * (_Intensity * edge * along * flick);
                return half4(col, 1.0);   // 加性：alpha 无意义，亮度全在 rgb
            }
            ENDHLSL
        }
    }
    FallBack Off
}
