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

            // ── 第 10b 轮（用户："大礁石也太粗糙了"）：程序化风化细节 ──
            // 真实海蚀岩的表面 = 层理条带（风化沿地层推进）+ 大小风化斑。
            // 全用正弦/值噪声，零贴图（潟湖线"不考虑性能、零外部素材"口径）。
            // ⚠ 世界空间驱动：细节不随实例旋转走（巨岩绕 Y 摆放过，物体空间
            //   会让同一次"风化"在不同朝向的实例上读成不同图案）。
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 nrm = normalize(IN.nrmWS);
                float2  wxz = IN.posWS.xz;

                // ── 高度三档（替代旧 Lo/Mid/Hi 三个按整面涂的槽）──
                float h01 = saturate((IN.posZ - _HMin) / max(_HMax - _HMin, 1e-4));
                float t1  = saturate(h01 / max(_MidPoint, 1e-4));
                float t2  = saturate((h01 - _MidPoint) / max(1.0 - _MidPoint, 1e-4));
                float3 rock = lerp(lerp(_RockLo.rgb, _RockMid.rgb, t1), _RockHi.rgb, t2);

                // ── 层理条带：三个尺度都只随高度变，水平方向缓慢倾斜漂移 ──
                // （岩层近似水平 ⇒ 条带天然横贯；漂移让它不至于像等高线）
                float drift = (wxz.x + wxz.y) * 0.018 + sin(h01 * 9.4) * 0.35;
                float strata = 0.055 * sin(h01 * 220.0 + drift)
                             + 0.075 * sin(h01 * 71.0 + drift * 1.7 + 2.1)
                             + 0.060 * sin(h01 * 23.0 + drift * 0.6 + 4.4);
                // ── 风化斑：两倍频 fbm（大面积渍 + 小麻点）──
                float blot = (VNoise(wxz * 0.55) - 0.5) * 0.13
                           + (VNoise(wxz * 2.30) - 0.5) * 0.07;
                rock *= clamp(1.0 + strata + blot, 0.78, 1.22);

                // 离色：每个中尺度条带内"上缘干灰(B↑G↓)/下缘湿暗"，±4% ——
                // 纯明度条带没有颜色变化时读成塑料刻线
                float bandPh = frac(h01 * 23.0 + drift * 0.6);
                rock *= lerp(float3(0.965, 0.985, 1.045), float3(1.030, 1.015, 0.960),
                             smoothstep(0.0, 1.0, bandPh));

                // ── 苔：顶点色权重按像素插值（粗糙感的正解）+ 噪声破边 ──
                // 噪声让苔的边界不再跟随顶点渐变的等值线（那条线仍隐约可读）
                float mossW = saturate(IN.col.r)
                            * (0.72 + 0.56 * VNoise(wxz * 2.2 + 13.7));
                float3 albedo = lerp(rock, _MossColor.rgb, mossW);

                Light L = GetMainLight();
                float ndl = saturate(dot(nrm, L.direction));
                half3 amb = SampleSH(nrm) * _AmbStrength;

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
