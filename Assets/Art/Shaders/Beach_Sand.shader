// ============================================================================
// Beach_Sand.shader — 沙滩：干沙 / 湿沙 / 沫边一条带
//
// 为什么不用 URP/Lit + 两张材质拼：湿沙边界必须和海水沫带**同一瞬间、同一个函数**
// 算出来，否则水退潮了沙还湿着，画面立刻假。所以直接 include OceanShore.hlsl，
// 用同一个 SwashReach(t)。
//
// 三段：
//   ① 干沙      亮、哑光
//   ② 湿沙      压暗 + 提一点光滑（水面膜的反光），这是"刚被冲过"的唯一线索
//   ③ 沫边      湿沙外沿一条薄白线，带噪声指状边
// 再叠一层低频沙纹（涟漪痕），否则整片沙就是一张色卡。
// ============================================================================
Shader "WaterProject/Beach_Sand"
{
    Properties
    {
        _DryColor   ("干沙色", Color) = (0.360, 0.285, 0.180, 1)
        _WetColor   ("湿沙色", Color) = (0.098, 0.076, 0.050, 1)
        _FoamColor  ("沫边色", Color) = (0.94, 0.97, 0.98, 1)
        _RippleTint ("沙纹深浅", Range(0, 0.4)) = 0.14
        _WetGloss   ("湿沙光滑度", Range(0, 1)) = 0.55
        _SpecPower  ("湿沙反光锐度", Float) = 90
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "BeachForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OceanShore.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DryColor, _WetColor, _FoamColor;
                float  _RippleTint, _WetGloss, _SpecPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 worldN     : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash12(i), b = Hash12(i + float2(1,0));
                float c = Hash12(i + float2(0,1)), e = Hash12(i + float2(1,1));
                float va = dot(float2(cos(a*6.28318), sin(a*6.28318)), f);
                float vb = dot(float2(cos(b*6.28318), sin(b*6.28318)), f - float2(1,0));
                float vc = dot(float2(cos(c*6.28318), sin(c*6.28318)), f - float2(0,1));
                float vd = dot(float2(cos(e*6.28318), sin(e*6.28318)), f - float2(1,1));
                return lerp(lerp(va, vb, u.x), lerp(vc, vd, u.x), u.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.worldPos = TransformObjectToWorld(v.positionOS.xyz);
                o.worldN = TransformObjectToWorldNormal(v.normalOS);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float3 N = normalize(IN.worldN);
                float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
                Light L = GetMainLight();

                float up = max(-OffDist(IN.worldPos), 0.0);    // 往岸上多远
                float reach = SwashReach(t);

                // 湿→干过渡不是一个硬台阶：外沿湿得透，往里逐渐干。
                // 湿区宽度刻意**不超过沫带本身**（1.0R 而不是 1.35R）：滩本来就不宽，
                // 一铺过头，从脚下到远处全成湿的，整片沙读成一块均匀深色平板。
                float wet = saturate((reach - up) / max(reach * 0.55, 0.5));
                // 沫边：贴在湿区外沿的一条窄带，用噪声咬成指状
                // ⚠ 别把这个变量叫 line —— HLSL 里 line 是保留的语义关键字，
                //   编译器不会说"重名"，只会在下一行报 unexpected token 'line'。
                float band = saturate(1.0 - abs(up - reach) / max(_SwashFade * 0.55, 0.25));
                float gn = ValueNoise(float2(IN.worldPos.x * 0.10, up * 0.55 - t * 0.75));
                float foamLine = saturate(band * (0.35 + 0.95 * gn)) * wet;

                // 低频沙纹：浪冲过留下的平行痕，越靠水线越明显
                float rip = ValueNoise(float2(IN.worldPos.x * 0.045, up * 0.85));
                float3 base = lerp(_DryColor.rgb, _WetColor.rgb, wet);
                base *= 1.0 + (rip - 0.5) * _RippleTint * saturate(1.0 - up * 0.05);

                float ndl = saturate(dot(N, L.direction));
                // 湿沙反照率低但**不是黑**：它靠反射天空发亮，所以 ambient 给足
                float3 col = base * (SampleSH(N) + L.color * ndl * (1.0 - wet * 0.35));

                // 湿沙那层水膜：高光只给湿区，干沙完全哑光
                float3 H = normalize(L.direction + V);
                float gloss = _WetGloss * wet;
                col += L.color * pow(saturate(dot(N, H)), _SpecPower) * gloss * 0.6;

                col = lerp(col, _FoamColor.rgb, foamLine * 0.85);

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
