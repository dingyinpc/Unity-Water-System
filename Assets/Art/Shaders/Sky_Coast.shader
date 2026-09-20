// ============================================================================
// Sky_Coast.shader — 开阔海岸的天空：渐变 + 程序云 + 太阳晕
//
// 为什么不复用 Sky_SimpleBlue（潭线就是这么复用的）：
//   潭线的画幅里天空只占顶上一条带，而且被远景林线挡掉大半，纯色渐变够用。
//   海岸线没有东西挡 —— 天空占了画面 55%，纯色渐变就是一块蓝布，
//   和潭线当时"天空带是全画面最没内容的区域"是同一个病，只是这次面积大五倍。
//
// 三个部件：
//   ① 双色渐变：和 Sky_SimpleBlue 同一条曲线，保证地平色可以继续当雾色用
//   ② 程序云：把视线投到一张固定高度的平面（dir.xz / dir.y）上做 fbm。
//      这个投影自带"越靠地平越密"的透视压缩，所以远处会自然堆成一条云带，
//      不需要任何额外的高度衰减。
//   ③ 太阳晕：方向直接取 GetMainLight()，不在这里再存一份太阳角 ——
//      潭线就是靠这条约定躲过"太阳仰角有两个写入点"那个坑的（《05》C-4）。
// ============================================================================
Shader "WaterProject/Sky_Coast"
{
    Properties
    {
        _HorizonColor ("地平线颜色", Color) = (0.700, 0.800, 0.900, 1)
        _ZenithColor  ("天顶颜色", Color) = (0.200, 0.430, 0.780, 1)
        _GroundColor  ("地面半色", Color) = (0.230, 0.250, 0.240, 1)
        _CloudColor   ("云色", Color) = (1.00, 0.995, 0.985, 1)
        _CloudShadow  ("云底阴影色", Color) = (0.520, 0.570, 0.650, 1)
        _CloudCover   ("云量", Range(0, 1)) = 0.52
        _CloudScale   ("云尺度", Float) = 0.045
        _CloudDrift   ("漂移速度", Float) = 0.010
        _SunGlow      ("日晕强度", Range(0, 2)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox"
               "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off Lighting Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HorizonColor, _ZenithColor, _GroundColor, _CloudColor, _CloudShadow;
                float  _CloudCover, _CloudScale, _CloudDrift, _SunGlow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // ⚠ 天空盒的视线方向**只能直接取物体空间顶点**。
                //   写成 TransformObjectToWorld(v) - _WorldSpaceCameraPos 会得到一片黑：
                //   URP 的 DrawSkyboxPass 走 context.CreateSkyboxRendererList，
                //   那颗立方体的物体矩阵并不是"平移到相机"的可依赖形式，
                //   减完剩下的是一个近似常量的向量，normalize 之后整片天空算成黑。
                //   工程里已经在跑的 Sky_SimpleBlue 就是取 v.vertex.xyz —— 照它来。
                o.dir = v.positionOS.xyz;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1,0));
                float c = Hash21(i + float2(0,1)), e = Hash21(i + float2(1,1));
                return lerp(lerp(a, b, u.x), lerp(c, e, u.x), u.y);
            }
            // 4 个倍频、每个倍频各自漂一点：只有主频在动的话云会整体平移、没有内部变化
            float Fbm(float2 p)
            {
                float s = 0.0, amp = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    s += amp * ValueNoise(p);
                    p = p * 2.07 + float2(13.1, 7.3) + k * 0.31;
                    amp *= 0.5;
                }
                return s;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                if (d.y < 0.0) return half4(_GroundColor.rgb, 1.0);

                float up = saturate(d.y);
                // ⚠ 指数方向容易搞反：pow(up, e) 里 e **越小越偏向天顶（深色）**。
                //   上一轮为了"提亮"把 2.0 改成 1.45，结果画幅里整段天更暗
                //   （右上实测 124 → 113）。本机位画幅上缘只有约 21° 仰角，
                //   e = 2 时那里离天顶色还远，正好让可见这一段停在较亮的地平色上。
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, up * up);

                // 视线投到单位高度的平面上。d.y 越小投影越远 ⇒ 地平方向自动堆成一条密云带
                float2 plane = d.xz / max(d.y, 0.055);
                // ⚠ 同一个投影在 d.y→0 时半径趋于无穷，噪声频率跟着爆炸，
                //   地平那圈就碎成横向拉丝、读成一条霾带而不是一团团云。
                //   按半径压一下，等于把远处的云"放远放大"。
                float comp = 1.0 / (1.0 + length(plane) * 0.035);
                float t = _Time.y * _CloudDrift;
                float f = Fbm(plane * comp * comp * _CloudScale + float2(t, t * 0.35));

                // fbm 四倍频值噪声的取值集中在 0.55~0.72，阈值映射必须落在这个区间里，
                // 否则 cover=0.55 再加 0.22 的过渡带几乎没人越得过 —— 云就"不存在"了。
                // 过渡带 0.14：再窄就会在高空压缩下读成一堆剪纸，云需要一点软边
                float cover = lerp(0.72, 0.28, _CloudCover);
                float cloud = smoothstep(cover, cover + 0.14, f);
                // 贴地平那几朵要压，但压到 9.5° 以上才允许有云就把整片天赶光了
                cloud *= saturate(d.y * 3.2 + 0.12);

                // 云的明暗必须用"超出阈值的厚度"，不能用 f 的绝对值。
                // 上一版写 saturate(f*1.9-0.32)：_CloudCover 一抬高，f 几乎处处越过
                // 那个门槛 ⇒ 整片云都取到最白那端，天上就是一层均匀白洗、没有团块。
                // 刚过阈值 = 薄 = 吃天光偏蓝暗，越厚越白，边和底才立得起来。
                float thick = saturate((f - cover) / 0.28);
                float3 ccol = lerp(_CloudShadow.rgb, _CloudColor.rgb, thick);
                col = lerp(col, ccol, cloud);

                // 日晕：方向取场景里那盏主光，不在材质里再存一份太阳角。
                // 宽项原来 pow(sd,9)*0.34 会把半个画幅洗成白，和另一侧的深蓝撞出
                // 刺眼对比；改成更窄更弱，让"亮"集中在太阳附近而不是铺开。
                float3 sunDir = GetMainLight().direction;
                float sd = saturate(dot(d, sunDir));
                col += _CloudColor.rgb * (pow(sd, 500.0) * 0.45 + pow(sd, 16.0) * 0.18) * _SunGlow;
                // 云在太阳那一侧会被打亮一点（逆光镶边），不加这个云会像贴上去的纸片
                col += _CloudColor.rgb * cloud * pow(sd, 5.0) * 0.30;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
