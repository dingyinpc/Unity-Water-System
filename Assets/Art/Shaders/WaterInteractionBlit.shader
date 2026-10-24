Shader "WaterProject/InteractionBlit"
{
    // 屏幕空间交互图 Blit：把交互点（RGBA：法线xy + 强度）累计写入交互 RT（正交、覆盖相机周围区域）
    Properties
    {
        [HideInInspector] _ScreenParams_placeholder("", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 交互数据（C# 侧每帧上传）
            // xyz = 世界位置（xz 有意义），w = 出生时间
            float4 _Interactions[32];
            int    _InteractionCount;
            float  _CurrentTime;
            // 批次信息：中心、半宽、持续时间
            float4 _InteractionCenter;   // xyz=中心世界坐标, w=半宽
            float  _InteractionLife;     // 单个交互的生命周期

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // 编码一次涟漪：dir 方向（xy 平面朝外）→ 法线扰动；径向包络 → 强度
            float4 EncodeBlob(float2 uvCenter, float2 dir, float ageNorm, float radius)
            {
                float r = length(dir);
                float env = smoothstep(radius, 0.0, r);                 // 中心强、向外衰减
                float ring = max(sin((r / max(radius, 1e-5)) * 6.28318 * 2.0 - ageNorm * 6.28318 * 1.5) * 0.5 + 0.5, 0.0);
                float strength = env * (0.7 + 0.3 * ring);
                float2 n = dir / max(r, 1e-4);
                float2 normalXY = n * (env * 1.2);                     // 指向外侧的法线扰动
                return float4(normalXY * strength, 0.0, strength);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 屏幕空间 → 交互空间（世界坐标）
                float2 uv = IN.uv;
                float2 worldXZ = _InteractionCenter.xz + (uv - 0.5) * (_InteractionCenter.w * 2.0);

                half4 acc = half4(0, 0, 0, 0);
                for (int i = 0; i < _InteractionCount; i++)
                {
                    float4 it = _Interactions[i];
                    float2 dir = worldXZ - it.xz;
                    float age = _CurrentTime - it.w;
                    if (age < 0.0 || age > _InteractionLife) continue;
                    float ageNorm = age / max(_InteractionLife, 1e-4);
                    float radius = _InteractionCenter.w * 0.35;        // 交互半径（空间归一）
                    half4 b = EncodeBlob(it.xz, dir, ageNorm, radius);
                    // R29-P3：出生 0.22s ease-in——autoDemo 交互整幅瞬现是 Main D2 尖峰的
                    // 实锤元凶（t≈2.4s 单帧 +2.4 灰阶跳变定位到本路径），缓入摊平出生拍
                    acc += b * (1.0 - ageNorm) * smoothstep(0.0, 0.22, age);
                }
                // 中性灰 = 无交互（法线 0.5,0.5, 强度 0）
                return half4(acc.xy * 0.5 + 0.5, 0.0, saturate(acc.w));
            }
            ENDHLSL
        }
    }
}