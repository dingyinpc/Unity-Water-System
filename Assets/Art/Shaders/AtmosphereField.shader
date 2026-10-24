// ============================================================================
// AtmosphereField.shader — 大气场辅助 RT（P2-3 / 参考 S14 云影 RT）
//
// 参考 S14：体积云在 raymarch 时顺路把「太阳光程透射率」落到一张云影 RT；
//   水面按 **世界 XZ** 映射采样该 RT，用透射率衰减 **GGX 直射高光**——
//   云挡住太阳时水面高光变暗、云移开又回来。
//
// 我们的体积云是天空 shader 里的 2D fbm（投影到固定高度平面），没有逐像素光程，
//   所以这里单独烘一张**世界 XZ 云影图**：把同一套 fbm 沿太阳方向投影到地面，
//   smoothstep 出云量 → Beer–Lambert 出透射率 → R 通道。
//   这样云影与天空的云形状同源（同 fbm/同 cover 映射）。
//
// Pass 0 "AFCloud"：rect 域内逐纹素 → worldXZ → 沿太阳投影 → 透射率
// ============================================================================
Shader "WaterProject/AtmosphereField"
{
    Properties
    {
        _AFRect       ("域 (minX,minZ,sizeX,sizeZ)", Vector) = (-500,-300,1000,600)
        _AFSunDir     ("太阳方向 (x,y,z)", Vector) = (0, 0.6, 0.8, 0)
        _AFCloudScale ("云尺度(1/米,与Sky_Coast同源0.00042)", Float) = 0.00042
        _AFCloudCover ("云量", Range(0,1)) = 0.52
        _AFWindDir    ("风向(度,与Sky_Coast同源)", Float) = 200
        _AFWindSpeed  ("风速(m/s,与Sky_Coast同源)", Float) = 18
        _AFShear      ("风切变(米/米高,与Sky_Coast同源)", Float) = 0.006
        _AFHeight     ("云层高度(米,=云底)", Float) = 900
        _AFOptical    ("云光学厚度", Float) = 1.8
        _AFViz        ("可视化 0=透射率 1=云量", Float) = 0
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "AFCloud"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _AFRect, _AFSunDir;
            float _AFCloudScale, _AFCloudCover, _AFWindDir, _AFWindSpeed, _AFShear, _AFHeight, _AFOptical, _AFViz;

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1,0)), u.x),
                            lerp(Hash12(i + float2(0,1)), Hash12(i + float2(1,1)), u.x), u.y);
            }
            // 与 Sky_Coast **逐系数同构**的 fbm（R3 起天上是体积云，云影取其
            // 云层中高切片：同 lattice/同风偏移/同阈值 ⇒ 影子和天上的云同形同速）
            float Fbm(float2 p)
            {
                float s = 0.0, amp = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    s += amp * VNoise(p);
                    p = p * 2.07 + float2(13.1, 7.3) + k * 0.31;
                    amp *= 0.5;
                }
                return s;
            }

            float4 frag(V i) : SV_Target
            {
                float2 uv = i.uv;
                float2 worldXZ = _AFRect.xy + uv * _AFRect.zw;

                // 沿太阳方向把云**中层切片**投影到地面（太阳越低，影子拉得越长）
                float midH = 550.0;                       // 云底 900 + 层厚 1100 的中部
                float sy = max(_AFSunDir.y, 0.15);
                float2 proj = worldXZ - _AFSunDir.xz * (midH / sy);

                // 与 Sky_Coast.WindOffset(midH) 同式：windDir·(speed·t + h·shear)
                float ra = radians(_AFWindDir);
                float2 woff = float2(sin(ra), cos(ra)) * (_AFWindSpeed * _Time.y + midH * _AFShear);
                float f = Fbm((proj + woff) * _AFCloudScale);
                // 与 Sky_Coast.CloudDensity 同阈值（cover 映射 + 过渡带 0.18）
                float cover = lerp(0.70, 0.24, _AFCloudCover);
                float cloud = smoothstep(cover, cover + 0.18, f);

                float trans = exp(-cloud * _AFOptical);     // Beer–Lambert 光程透射率
                // 太阳越低云影越柔（但**不要**把它乘到 0，否则太阳低时整张图恒为"无云"，A/B 看不出）：
                trans = lerp(1.0, trans, saturate(0.35 + _AFSunDir.y * 2.0));

                return float4(_AFViz > 0.5 ? cloud : trans, cloud, 0, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
