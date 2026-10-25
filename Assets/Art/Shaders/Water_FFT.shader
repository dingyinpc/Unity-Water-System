// Water_FFT.shader — R2 FFT 海洋水面（《08》S1 多级合成 / S2 choppy / S5 浪峰）
//
// 顶点：世界 XZ 采样三级位移 RT（_FFTL{i}Disp：x=Dx,y=Dz,z=h），按距离权重合成
//       （细级远处淡出防混叠）；水平挤压乘 _FFTGlobal.x（choppy，S2 演示开关）。
// 片元：三级梯度合成法线；菲涅尔混程序天空（地平线/天顶双色）；GGX 型太阳高光；
//       Jacobian 泡沫（J<0.85 起沫，_FFTGlobal.y 缩放）；远距雾归一到地平线色。
// 全部采样全局纹理（FFTOceanSim 发布）——材质上只有观感参数，模拟参数不在此写。
Shader "WaterProject/Water_FFT"
{
    Properties
    {
        _DeepColor    ("水体深色", Color) = (0.015, 0.09, 0.13, 1)
        _ScatterColor ("浪背散射色", Color) = (0.05, 0.42, 0.40, 1)
        _HorizonColor ("地平线色", Color) = (0.62, 0.75, 0.85, 1)
        _ZenithColor  ("天顶色", Color) = (0.18, 0.42, 0.72, 1)
        _SunColor     ("太阳色", Color) = (1.0, 0.93, 0.82, 1)
        _SpecPow      ("高光锐度", Range(16, 2048)) = 320
        _SpecInt      ("高光强度", Range(0, 8)) = 2.2
        _FoamColor    ("泡沫色", Color) = (0.92, 0.96, 0.98, 1)
        _FogStart     ("雾起距离", Float) = 160
        _FogEnd       ("雾满距离", Float) = 560
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            Texture2D _FFTL0Disp; SamplerState sampler_FFTL0Disp;
            Texture2D _FFTL1Disp; SamplerState sampler_FFTL1Disp;
            Texture2D _FFTL2Disp; SamplerState sampler_FFTL2Disp;
            Texture2D _FFTL0Grad; SamplerState sampler_FFTL0Grad;
            Texture2D _FFTL1Grad; SamplerState sampler_FFTL1Grad;
            Texture2D _FFTL2Grad; SamplerState sampler_FFTL2Grad;
            float4 _FFTL0Info;    // (L, fadeA, fadeB, amp)
            float4 _FFTL1Info;
            float4 _FFTL2Info;
            float4 _FFTGlobal;    // (choppy, foamScale, simTime, 0)
            float4 _FFTSunDir;

            float4 _DeepColor, _ScatterColor, _HorizonColor, _ZenithColor, _SunColor, _FoamColor;
            float _SpecPow, _SpecInt, _FogStart, _FogEnd;

            // R32 泡沫颗粒：J 场是平滑场，直接上屏是"糊白带"——用双倍频值噪声
            // 把沫侵蚀成碎粒/云絮（参考的沫有明确颗粒感）
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float FoamNoise(float2 xz, float t)
            {
                float2 q = xz * 0.85 + float2(t * 0.22, -t * 0.17);
                float2 i = floor(q), f = frac(q);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                float n1 = lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
                float n2 = Hash21(i * 1.93 + 7.7);
                return n1 * 0.72 + n2 * 0.28;
            }

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wp : TEXCOORD0;
            };

            // 距离权重：细级远处淡出（fadeA→fadeB smoothstep 归零；粗级 fade=1e9 恒 1）
            float LevelWeight(float4 info, float dist)
            {
                return 1.0 - smoothstep(info.y, info.z, dist);
            }

            v2f vert(float4 v : POSITION)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v).xyz;
                float dist = distance(wp.xz, _WorldSpaceCameraPos.xz);
                float3 disp = 0;
                float w;
                w = LevelWeight(_FFTL0Info, dist);
                disp += _FFTL0Disp.SampleLevel(sampler_FFTL0Disp, wp.xz / _FFTL0Info.x, 0).xyz
                        * float3(_FFTGlobal.x, 1, _FFTGlobal.x) * _FFTL0Info.w * w;
                w = LevelWeight(_FFTL1Info, dist);
                disp += _FFTL1Disp.SampleLevel(sampler_FFTL1Disp, wp.xz / _FFTL1Info.x, 0).xyz
                        * float3(_FFTGlobal.x, 1, _FFTGlobal.x) * _FFTL1Info.w * w;
                w = LevelWeight(_FFTL2Info, dist);
                disp += _FFTL2Disp.SampleLevel(sampler_FFTL2Disp, wp.xz / _FFTL2Info.x, 0).xyz
                        * float3(_FFTGlobal.x, 1, _FFTGlobal.x) * _FFTL2Info.w * w;
                // 平面无非均匀变换 ⇒ 位移可直接加到局部顶点（等价世界系）
                v.xyz += disp;
                float3 wp2 = mul(unity_ObjectToWorld, v).xyz;
                o.pos = TransformWorldToHClip(wp2);
                o.wp = wp2;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float dist = distance(i.wp.xz, _WorldSpaceCameraPos.xz);
                float3 vd = normalize(_WorldSpaceCameraPos - i.wp);

                float w0 = LevelWeight(_FFTL0Info, dist);
                float w1 = LevelWeight(_FFTL1Info, dist);
                float w2 = LevelWeight(_FFTL2Info, dist);
                float2 uv0 = i.wp.xz / _FFTL0Info.x;
                float2 uv1 = i.wp.xz / _FFTL1Info.x;
                float2 uv2 = i.wp.xz / _FFTL2Info.x;
                float4 g0 = _FFTL0Grad.Sample(sampler_FFTL0Grad, uv0);
                float4 g1 = _FFTL1Grad.Sample(sampler_FFTL1Grad, uv1);
                float4 g2 = _FFTL2Grad.Sample(sampler_FFTL2Grad, uv2);
                float2 grad = g0.xy * _FFTL0Info.w * w0 + g1.xy * _FFTL1Info.w * w1
                            + g2.xy * _FFTL2Info.w * w2;
                float foam = saturate((g0.w * w0 + g1.w * w1 + g2.w * w2) * _FFTGlobal.y);
                // R32：白沫成团——线性叠加偏"蕾丝网"，smoothstep 把中间密度推成
                // 云絮状斑块（参考浪尖的沫是一团团的，不是均匀网）。
                // ⚠ 端点别收太紧：首版 (0.12,0.85) 把中密度全灭、只剩饱和点（实测白沫又不可见）
                foam = smoothstep(0.15, 0.9, foam);
                // 颗粒侵蚀：噪声均值≈0.8，既碎化又把覆盖从 ~25% 压回 ~15%
                foam = saturate(foam * (0.30 + 0.95 * FoamNoise(i.wp.xz, _FFTGlobal.z)));

                float3 n = normalize(float3(-grad.x, 1.0, -grad.y));

                // 菲涅尔（Schlick）
                float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, vd)), 5.0);

                // 程序天空反射色：地平线→天顶 + 太阳方向光晕
                float3 rd = reflect(-vd, n);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, saturate(rd.y));
                sky += _SunColor.rgb * pow(max(dot(rd, _FFTSunDir.xyz), 0.0), 320.0) * 12.0;

                // 水体色 + 浪背散射（波峰朝太阳时的透射感）
                float hgt = g0.z * _FFTL0Info.w;
                float3 body = _DeepColor.rgb + _ScatterColor.rgb
                              * saturate(hgt * 1.6) * 0.35 * saturate(dot(vd.xz, -_FFTSunDir.xz));

                // GGX 型太阳高光
                float3 hv = normalize(vd + _FFTSunDir.xyz);
                float spec = pow(saturate(dot(n, hv)), _SpecPow) * _SpecInt;

                float3 col = lerp(body, sky, fres) + _SunColor.rgb * spec;
                col = lerp(col, _FoamColor.rgb, foam);

                // 远距雾归一（远景接天空地平线，藏网格边）
                float fog = smoothstep(_FogStart, _FogEnd, dist);
                col = lerp(col, _HorizonColor.rgb, fog);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
