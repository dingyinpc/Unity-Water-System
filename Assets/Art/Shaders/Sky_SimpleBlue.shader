// Sky_SimpleBlue.shader — 极简天蓝天空盒：地平线浅白 → 天顶亮蓝 渐变（无 HDR，杜绝色溢出）
// 供菜单 35 使用；低机位水面反射它即可显蓝。
Shader "WaterProject/SkySimpleBlue"
{
    Properties
    {
        _HorizonColor ("地平线颜色", Color) = (0.72, 0.82, 0.95, 1)
        _ZenithColor  ("天顶颜色", Color) = (0.36, 0.62, 1.0, 1)
        _GroundColor  ("地面半色", Color) = (0.28, 0.34, 0.42, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _HorizonColor;
            fixed4 _ZenithColor;
            fixed4 _GroundColor;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float3 dir : TEXCOORD0; float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;   // skybox 立方体顶点方向即视线方向
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y * 0.5 + 0.5;                     // 0=地平 0.5=天顶
                fixed3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, up * up);
                fixed3 col = d.y < 0 ? _GroundColor.rgb : sky;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}