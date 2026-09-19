// Tree_Leaf.shader — 叶片卡片：背光透射 + 风摆动
//
// 为什么不用 URP 内置 Lit：Lit 没有 transmission 通道，逆光的叶子只会黑成剪影，
// 而"光穿过树叶"正是这个场景要的感觉。这里只写叶片需要的那几项，不做完整 PBR：
//   Lambert 漫反射 + 球谐环境光 + 背光透射 + alpha 裁剪 + 雾。
//
// 顶点色是交付包烘好的（见 E:\Blender\交付_树资产\Unity导入说明.md 第 5 节）：
//   R = 柔韧度，树根 0 → 叶梢 1  ← 同时当"叶片薄厚"的代理：越靠梢的叶越薄越透
//   G = 每根枝条的随机相位
//   B = 叶片抖动开关（树皮 0，叶片 1）
//
// 已知取舍：不采样阴影贴图（树冠本来就该在光里，且叶片早关了投影）、不做高光。
// 树皮继续用 URP/Lit，不吃这个 shader。
Shader "WaterProject/TreeLeaf"
{
    Properties
    {
        _BaseMap   ("叶簇图集", 2D) = "white" {}
        _BaseColor ("底色", Color) = (1, 1, 1, 1)
        _Cutoff    ("Alpha 裁剪", Range(0, 1)) = 0.5

        _TransColor("透光色", Color) = (0.55, 0.85, 0.35, 1)
        _TransStrength ("透光强度", Range(0, 4)) = 1.6
        _TransPower    ("透光锐度", Range(1, 64)) = 10

        _WindStrength ("风幅度(米)", Range(0, 0.6)) = 0.12
        _WindFreq     ("风频率", Range(0, 5)) = 1.1
        _WindDir      ("风向(归一化)", Vector) = (0.92, 0, 0.32, 0)
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest"
               "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }

        // 卡片是单片四边形，必须双面 —— 从树下往上看才不会整块消失
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "TreeLeafForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // 交付包的风权重，缺了它树永远不动
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 vcol       : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // SRP Batcher 要求所有材质常量进同一个 CBUFFER
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Cutoff;
                float4 _TransColor;
                float  _TransStrength;
                float  _TransPower;
                float  _WindStrength;
                float  _WindFreq;
                float4 _WindDir;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;

                // ★ 位移必须在【变换到世界空间之后】做，否则物体自转会带着风向一起转
                float3 wpos = TransformObjectToWorld(v.positionOS.xyz);
                float3 nrm  = TransformObjectToWorldNormal(v.normalOS);

                float flex    = v.color.r;                       // 根 0 → 梢 1
                float phase   = v.color.g;
                float flutter = v.color.b;                       // 树皮 0，叶片 1

                float sway = 0.5 + 0.5 * sin(_Time.y * _WindFreq + phase * 6.28318);
                float3 off = _WindDir.xyz * (_WindStrength * flex * sway);

                // 叶片抖动：交付说明要 noise(worldPos*8 + _Time.y*3)*0.06，
                // 这里用三轴错相的正弦当廉价噪声 —— 视觉等价，省一次噪声实现
                float3 n = wpos * 8.0 + _Time.y * 3.0;
                off += flutter * 0.06 * float3(sin(n.x + n.z), sin(n.y + n.x), sin(n.z + n.y));

                wpos += off;

                o.positionWS = wpos;
                o.normalWS   = nrm;
                o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
                o.vcol       = v.color;
                o.positionCS = TransformWorldToHClip(wpos);
                o.fogCoord   = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(tex.a - _Cutoff);

                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 N = normalize(IN.normalWS);
                // 双面卡片：背面那侧的法线要翻过来，否则背面直接算成逆光黑
                N *= (dot(N, V) < 0.0) ? -1.0 : 1.0;

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(N, mainLight.direction));

                // 背光透射：视线与"光的来向"越接近重合，光越是穿过叶片射进眼睛
                float backlit = pow(saturate(dot(-mainLight.direction, V)), _TransPower);
                float thickness = saturate(IN.vcol.r);            // 叶梢薄 → 更透

                float3 albedo = tex.rgb * _BaseColor.rgb;
                float3 ambient = SampleSH(N);

                float3 col = albedo * (ambient + mainLight.color * ndl)
                           + albedo * _TransColor.rgb * (backlit * _TransStrength * thickness)
                             * mainLight.color;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
