// SkyBlur.shader — R135 天空轻微模糊（只作用于天空，深度感知，不跨边缘渗色）
//
// 为什么要它：Islet/Ocean 的云在掠射角会出"逐像素椒盐点"（R133 已证实来自云的采样，
// 加密步进/多相位平均/密度下限都清不掉）。这类问题需要**空间域滤波**。
//
// 设计要点（第一版踩过的坑都写在这里）：
//   ① 只做 **全分辨率 3×3**，不做半分辨率两趟 —— 半分辨率会在"天/水"边缘跨边界取样，
//      把深色海水糊进天空，地平线上出现一块块黑方点。
//   ② 每个采样点都查深度，**只混天空像素**（权重=0 的邻居不参与），这样云边/山脊不会被糊开。
//   ③ 强度用**材质属性**（不是全局），否则材质自带默认值 1 会盖掉全局。
Shader "Hidden/WaterProject/SkyBlur"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    TEXTURE2D_X(_BlitTexture);
    SAMPLER(sampler_LinearClamp);

    float4 _BlurStep;     // (1/w, 1/h, 0, 0) —— 全分辨率
    // ⚠ 用**全局**参数而不是材质属性：材质自带默认值会盖掉全局，
    //   导致菜单改强度时画面纹丝不动（实测 0.5 与 0.85 逐像素相同）。
    float  _SkyBlurStrength;

    struct Attributes { uint vertexID : SV_VertexID; };
    struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

    Varyings Vert(Attributes v)
    {
        Varyings o;
        o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
        o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
        return o;
    }

    bool IsSkyAt(float2 uv, float far)
    {
        float raw = SampleSceneDepth(uv);
        return LinearEyeDepth(raw, _ZBufferParams) > far * 0.97;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "BlurSky"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            half4 FragBlur(Varyings i) : SV_Target
            {
                float far = _ProjectionParams.z;
                half3 orig = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv).rgb;

                // 非天空像素：原样返回（水面/地形/植被一点不碰）
                if (!IsSkyAt(i.uv, far)) return half4(orig, 1);

                float2 t = _BlurStep.xy * 1.15;      // 略大于 1 像素，覆盖 1~2px 的椒盐
                half3 acc = orig * 4.0;
                float wsum = 4.0;

                [unroll] for (int k = 0; k < 8; k++)
                {
                    float2 o = float2((k % 3) - 1, (k / 3) - 1);   // k=0..7 跳过中心 (1,1)
                    float2 uv = i.uv + o * t;
                    if (!IsSkyAt(uv, far)) continue;               // 邻居不是天空 → 不参与
                    float w = (o.x == 0 || o.y == 0) ? 2.0 : 1.0;  // 十字权重更高
                    acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * w;
                    wsum += w;
                }

                half3 blur = acc / max(wsum, 1e-3);
                return half4(lerp(orig, blur, _SkyBlurStrength), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
