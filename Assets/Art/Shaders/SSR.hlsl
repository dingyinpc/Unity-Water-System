// ============================================================================
// SSR.hlsl — 屏幕空间反射的共享实现（P0-2，参考 S3 / C8）
//
// 沿反射方向在屏幕空间射线步进 → 与 _CameraDepthTexture 比较 → 命中后二分精化；
// 可信度 = 深度差(命中厚度) × 追踪距离 × 屏幕边缘（参考 C8 三因子）。
// miss ⇒ conf=0，由调用方回退到既有反射源（天空渐变 / CubeMap）。
//
// ⚠ 参数走**显式传参**：不依赖调用方在何处声明 uniform（避免 include 顺序契约
//   这种隐性耦合）。调用方仍必须 include DeclareDepthTexture/DeclareOpaqueTexture。
// ⚠ 调用方传入的裁剪坐标必须来自另存的 TEXCOORD（不能用片元 SV_POSITION，《05》A-1）
// ============================================================================

// NDC → 采样 UV。⚠ 必须**逐步照抄** Unity 的 ComputeScreenPos：
//     o = pos*0.5; o.xy = float2(o.x, o.y*_ProjectionParams.x) + o.w; o.zw = pos.zw;
//   最后那行 o.zw = pos.zw 是**还原 w**，漏掉它分母就变成 0.5*w ⇒ UV 变成 (ndc+1)，
//   深度/颜色全采到画面外——本步第一次实现就栽在这，表现为"命中可信度处处为 0"。
float2 ClipToScreenUV(float4 c)
{
    float4 o = c * 0.5;
    o.xy = float2(o.x, o.y * _ProjectionParams.x) + o.w;
    o.zw = c.zw;
    return o.xy / o.w;
}

float3 TraceSSR(float3 wp, float3 rd, float maxDist, float stepsF,
                float thickness, float edgeFade, out float conf)
{
    conf = 0.0;
    float3 col = float3(0, 0, 0);
    float3 P1 = wp + rd * maxDist;
    float4 c0 = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
    float4 c1 = mul(UNITY_MATRIX_VP, float4(P1, 1.0));
    float vz0 = -mul(UNITY_MATRIX_V, float4(wp, 1.0)).z;
    float vz1 = -mul(UNITY_MATRIX_V, float4(P1, 1.0)).z;
    if (c0.w <= 1e-3 || c1.w <= 1e-3 || vz1 <= vz0 + 1e-3) return col;

    float2 u0 = ClipToScreenUV(c0), u1 = ClipToScreenUV(c1);
    int steps = (int)clamp(stepsF, 4.0, 48.0);
    float tHit = -1.0, tPrev = 0.0;
    [loop]
    for (int i = 1; i <= steps; i++)
    {
        float t = (float)i / steps;
        float2 uv = lerp(u0, u1, t);
        if (uv.x < -0.03 || uv.x > 1.03 || uv.y < -0.03 || uv.y > 1.03) { tPrev = t; break; }
        float sceneVz = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
        float vz = lerp(vz0, vz1, t);
        if (vz > sceneVz - thickness)
        {
            float lo = tPrev, hi = t;
            [loop]
            for (int j = 0; j < 6; j++)
            {
                float m = (lo + hi) * 0.5;
                float svm = LinearEyeDepth(SampleSceneDepth(lerp(u0, u1, m)), _ZBufferParams);
                if (lerp(vz0, vz1, m) > svm - thickness) hi = m; else lo = m;
            }
            tHit = (lo + hi) * 0.5;
            break;
        }
        tPrev = t;
    }
    if (tHit < 0.0) return col;

    float2 uvH = lerp(u0, u1, tHit);
    float sceneH = LinearEyeDepth(SampleSceneDepth(uvH), _ZBufferParams);
    float vzH = lerp(vz0, vz1, tHit);
    float cThick = saturate(1.0 - abs(vzH - sceneH) / max(thickness, 1e-3));
    float cDist  = 1.0 - tHit * tHit;
    float2 e = min(uvH, 1.0 - uvH);
    float cEdge = saturate(min(e.x, e.y) * edgeFade);
    conf = cThick * cDist * cEdge;
    return SampleSceneColor(uvH);
}

