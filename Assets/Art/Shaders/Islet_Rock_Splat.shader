// ============================================================================
// Islet_Rock_Splat.shader — 潟湖岛巨岩/礁石的四色按像素混合（第 10 轮）
//
// 为什么不再用 URP Lit + 4 个材质槽（用户 2026-10-04："石头做得太粗糙了"）：
//   旧做法的苔/岩边界是**按整面涂**的——Blender 侧 paint_slots 给每个大面块
//   钉一个槽，1~3m 的面在 182m 外就是一格格 5~15px 的纯色块，边界笔直，
//   读成像素画。真正的过渡要**按像素**平滑 ⇒ 权重必须逐顶点插值：
//   build_rock.paint_slots 现在把苔权重烘进顶点色 R（BYTE_COLOR/POINT，
//   与椰树风权重同一套通道），本 shader 按像素 lerp。
//
// 岩体三档色不再用槽分（同一个按整面涂的病）：按**局部高度**插值——
//   建模侧的高度轴在局部 Z（StandUpZ 立起来靠实例旋转），_HMin/_HMax 对应
//   build_rock.py PROFILE 的 z 范围，由菜单 94 写入，改 PROFILE 必须同步。
//
// 光照 = Lambert + 球谐环境 + **_FillLight 平坦天光填充**——这是第 7 轮
//   Emission E=0.20 实验的移植：填充本来就是"albedo × 常数"的加光项，
//   放进 shader 里比 URP Lit 的 _EMISSION 关键字干净（那里关不掉的教训
//   见 ShadedMat 注释）。巨岩在 182m 外、超出阴影距离 140 ⇒ 不采阴影贴图。
// ============================================================================
Shader "WaterProject/IsletRockSplat"
{
    Properties
    {
        _RockLo      ("岩·低(湿)", Color) = (0.0135, 0.0884, 0.1456, 1)
        _RockMid     ("岩·中", Color) = (0.0160, 0.1008, 0.1636, 1)
        _RockHi      ("岩·高", Color) = (0.0190, 0.1181, 0.1870, 1)
        _MossColor   ("苔", Color) = (0.0140, 0.1160, 0.0770, 1)
        _HMin        ("高度下(m, 局部Z)", Float) = -3
        _HMax        ("高度上(m, 局部Z)", Float) = 50
        _MidPoint    ("中档中心(0-1)", Float) = 0.35
        _FillLight   ("天光填充(假GI)", Float) = 0.20
        _AmbStrength ("环境光权重", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RockLo, _RockMid, _RockHi, _MossColor;
                float  _HMin, _HMax, _MidPoint, _FillLight, _AmbStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // R = 苔权重（build_rock.paint_slots 烘焙）
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS      : TEXCOORD0;
                float3 nrmWS      : TEXCOORD1;
                float4 col        : COLOR;
                float  posZ       : TEXCOORD2;  // 局部 Z = 高度轴（建模侧约定）
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.nrmWS = TransformObjectToWorldNormal(v.normalOS);
                o.col   = v.color;
                o.posZ  = v.positionOS.z;
                o.positionCS = TransformWorldToHClip(o.posWS);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ── 高度三档（替代旧 Lo/Mid/Hi 三个按整面涂的槽）──
                float h01 = saturate((IN.posZ - _HMin) / max(_HMax - _HMin, 1e-4));
                float t1  = saturate(h01 / max(_MidPoint, 1e-4));
                float t2  = saturate((h01 - _MidPoint) / max(1.0 - _MidPoint, 1e-4));
                float3 rock = lerp(lerp(_RockLo.rgb, _RockMid.rgb, t1), _RockHi.rgb, t2);

                // ── 苔：顶点色权重按像素插值 —— 粗糙感的正解就在这一行 ──
                float3 albedo = lerp(rock, _MossColor.rgb, saturate(IN.col.r));

                Light L = GetMainLight();
                float ndl = saturate(dot(normalize(IN.nrmWS), L.direction));
                half3 amb = SampleSH(normalize(IN.nrmWS)) * _AmbStrength;

                // 填充 = albedo × 常数（第 7 轮 E=0.20 的等价移植）
                float3 col = albedo * (L.color * ndl + amb + _FillLight);
                col = MixFog(col, IN.posWS);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
