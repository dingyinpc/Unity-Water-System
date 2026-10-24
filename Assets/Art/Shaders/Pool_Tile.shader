// ============================================================================
// Pool_Tile.shader — 泳池瓷砖（程序化，零美术资产）
//
// 主平面投影：法线朝上 → (x,z) 格；朝 ±x → (z,y)；朝 ±z → (x,y)。
// 一份 shader 通吃池底/池壁，瓷砖 0.2m + 灰缝 + 哈希明度扰动。
// 水线分色：y<0 湿区（更深更蓝），y>0 干区（亮暖白）——水位就是分界，
// 泳池水线那条"上干下湿"的色带是泳池最不能少的识别特征。
// ============================================================================
Shader "WaterProject/Pool_Tile"
{
    Properties
    {
        _TileDry   ("干瓷砖色", Color) = (0.78, 0.85, 0.86, 1)
        _TileWet   ("湿瓷砖色", Color) = (0.42, 0.62, 0.68, 1)
        _GroutColor("灰缝色", Color) = (0.55, 0.58, 0.60, 1)
        _TileSize  ("瓷砖尺寸(米)", Float) = 0.20
        _GroutWidth("灰缝宽(米)", Float) = 0.012
        _TintJitter("逐砖明度扰动", Range(0,1)) = 0.10
        _AmbStrength("环境光强度", Float) = 0.55
        _FillLight ("补光", Float) = 0.18
        _MicroAmt  ("微细节(3cm斑驳+砖缝AO)", Range(0,1)) = 0
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
                float4 _TileDry, _TileWet, _GroutColor;
                float _TileSize, _GroutWidth, _TintJitter, _AmbStrength, _FillLight;
                float _MicroAmt;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.worldPos = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = normalize(TransformObjectToWorldNormal(v.normalOS));
                o.positionCS = TransformWorldToHClip(o.worldPos);
                return o;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // R20 瓷砖微细节用的值噪声（Hash21 双线性插值）
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);

                // 主平面投影选格坐标
                float2 tuv;
                if (n.y > 0.5)       tuv = IN.worldPos.xz;
                else if (abs(n.x) > abs(n.z)) tuv = float2(IN.worldPos.z, IN.worldPos.y);
                else                 tuv = IN.worldPos.xz;

                float2 cell = floor(tuv / _TileSize);
                float2 f    = frac(tuv / _TileSize);
                float  gw   = saturate(_GroutWidth / _TileSize);
                float2 edge = min(f, 1.0 - f);
                float  eMin = min(edge.x, edge.y);
                float  grout = step(eMin, gw * 0.5);

                // 逐砖明度扰动（含灰缝对齐）
                float h = Hash21(cell + 0.5);
                float3 tile = lerp(_TileDry.rgb, _TileWet.rgb, step(IN.worldPos.y, 0.0));
                tile *= 1.0 + (h - 0.5) * 2.0 * _TintJitter;
                float3 col = lerp(tile, _GroutColor.rgb, grout);

                // R20 瓷砖微细节（_MicroAmt 默认 0 零回归；菜单 586 是唯一写入点）：
                // ① 陶砖烧制不匀的 ~3cm 斑驳；② 砖缝边缘一圈浅 AO（缝隙吃光）
                float mot = VNoise(tuv * 34.0 + h * 17.0);
                col *= 1.0 - _MicroAmt * 0.10 * (mot - 0.5);
                col *= 1.0 - _MicroAmt * 0.22 * smoothstep(gw * 0.5 + 0.07, gw * 0.5, eMin);

                Light L = GetMainLight();
                float ndl = saturate(dot(n, L.direction));
                half3 amb = SampleSH(n);
                col = col * (L.color * ndl + amb * _AmbStrength + _FillLight);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
