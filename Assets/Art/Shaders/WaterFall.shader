// WaterFall.shader — R43 程序化瀑布（低模风格：滚动条纹 + 落水泡沫）
//
// 两种用法（_Mode）：0=竖直瀑布带（略外弓的 ribbon，条纹沿 -Y 滚动）
//                   1=落水泡沫盘（平铺水面，径向衰减 + 慢速翻滚）
// 白青双色 × 双层值噪声；队列 Transparent+5（画在水面之上）。
Shader "WaterProject/WaterFall"
{
    Properties
    {
        _Mode       ("0=瀑布带 1=泡沫盘", Float) = 0
        _ColorHi    ("亮纹色", Color) = (0.88, 0.95, 0.98, 1)
        _ColorLo    ("暗纹色", Color) = (0.42, 0.62, 0.72, 1)
        _Speed      ("滚动速度", Float) = 1.4
        _StreakScale("条纹密度", Float) = 5.0
        _Alpha      ("整体透明度", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "IgnoreProjector"="True"
               "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode, _Speed, _StreakScale, _Alpha;
                float4 _ColorHi, _ColorLo;
            CBUFFER_END

            struct a2v { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

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
                float a = Hash21(i), b = Hash21(i + float2(1,0));
                float c = Hash21(i + float2(0,1)), d = Hash21(i + float2(1,1));
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }

            v2f vert(a2v v)
            {
                v2f o;
                o.wp = TransformObjectToWorld(v.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 uv = i.uv;
                float alpha = _Alpha;

                if (_Mode < 0.5)
                {
                    // 瀑布带：uv.y 1=顶 0=底。条纹 = x 方向细 y 方向拉长的噪声沿 y 滚动
                    float2 q = float2(uv.x * _StreakScale, uv.y * _StreakScale * 0.22 - t);
                    float n1 = VNoise(q);
                    float n2 = VNoise(q * 2.3 + float2(7.7, -t * 1.6));
                    float streak = saturate(n1 * 0.65 + n2 * 0.45);
                    float3 col = lerp(_ColorLo.rgb, _ColorHi.rgb, streak);
                    // 底部溅白加亮、两侧边淡出
                    col = lerp(col, _ColorHi.rgb, saturate(0.25 - uv.y) * 2.0);
                    alpha *= smoothstep(0.0, 0.08, uv.x) * smoothstep(1.0, 0.92, uv.x);
                    alpha *= 0.55 + 0.45 * streak;
                    return half4(col, alpha);
                }
                else
                {
                    // 泡沫盘：径向 uv，慢速翻滚
                    float2 c = uv - 0.5;
                    float r = length(c) * 2.0;
                    float ang = atan2(c.y, c.x) * 1.5915 + 0.5;
                    float2 q = float2(ang * _StreakScale, r * _StreakScale - t * 0.6);
                    float n = VNoise(q) * 0.6 + VNoise(q * 2.7 + 4.2 + t * 0.2) * 0.4;
                    float foam = saturate(n * 1.4 - 0.25);
                    float3 col = lerp(_ColorLo.rgb, _ColorHi.rgb, foam);
                    alpha *= foam * saturate(1.0 - r) * 0.9;
                    return half4(col, alpha);
                }
            }
            ENDHLSL
        }
    }
}
