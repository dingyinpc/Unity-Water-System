// ============================================================================
// SSR_AntiFlicker.hlsl — SSR 屏幕空间反射抗闪增强系统（P0-2-C7）
//
// 融合参考视频 S3-C7/C8 的两段抗闪烁方案：
//   1. 法线端：混合原始水面坡度与 Mip 过滤后的梯度
//   2. 采样端：ddx/ddy 计算屏幕足迹，SampleGrad 自动选 LoD
//
// 设计原则：
//   - 向后兼容：保留原有 TraceSSR 签名，新增 TraceSSR_Mip 版本
//   - 渐进式切换：通过_SSRAntiFlicker 参数控制开关
//   - 性能优先：Mip 采样加速降低射线步数需求
// ============================================================================

#ifndef SSR_ANTIFLICKER_INCLUDED
#define SSR_ANTIFLICKER_INCLUDED

// -----------------------------------------------------------------------------
// 新增声明：梯度纹理与 Mip 链
// -----------------------------------------------------------------------------
extern float4 _GradTexMip0;      // R10G10B10A2_UNORM 全分辨率梯度 RT
extern sampler s_gradMip0;       // Mip 0 采样器（点采样 + 各向异性）

// 压缩数组声明（Mip 1-7，最多 7 层覆盖 128×缩放）
extern float4 _GradTexMip1[8];   // [R, G, B, unused]
extern sampler s_gradMip1_7;     // Mip 1-7共享采样器（硬件插值）

uniform float _SSRMipBlend;      // Mip 融合强度 (0~1)
uniform float _SSRFineW;         // 原始梯度权重 (0~1)  
uniform float _SSRAntiFlicker;   // 总开关 (0=关 1=开)
uniform float _SSRSampleRate;    // 采样率 (0.5~1.0, 降低射线密度提升性能)

// -----------------------------------------------------------------------------
// 工具函数：屏幕足迹计算
// -----------------------------------------------------------------------------
float2 CalculateScreenFootprint(float2 uv, float2 dUDx, float2 dUDy)
{
    // 计算像素 footprint 面积
    float2 dx = abs(dUDx);
    float2 dy = abs(dUDy);
    return max(dx, dy) * 2.0; // 2×安全系数
}

// -----------------------------------------------------------------------------
// 核心改进 1: Mip 梯度采样（替代原 SampleGradient）
// -----------------------------------------------------------------------------
float3 SampleGradientWithMip(float2 uv, float2 gradMag, float screenFootprint, bool useMip)
{
    if (!useMip || _SSRAntiFlicker < 0.5)
        return gradMag.xxx; // fallback to original
    
    // 计算合适的 Mip 层级（log2 基于 footprint 大小）
    float mipLevel = log2(max(screenFootprint.x, screenFootprint.y) * 64.0);
    mipLevel = saturate(mipLevel); // clamp to [0, 7]
    
    int baseMip = floor(mipLevel);
    float t = frac(mipLevel);
    
    float3 gradOrig = gradMag.xxx;
    float3 gradFiltered = gradOrig;
    
    if (baseMip == 0)
    {
        // Mip 0：点采样（无过滤）
        gradFiltered = tex2Dlod(s_gradMip0, float4(uv, 0, 0)).xxx;
    }
    else if (baseMip >= 1 && baseMip <= 7)
    {
        // Mip 1-7：硬件线性插值
        gradFiltered = tex2Dlod(s_gradMip1_7, float4(uv, baseMip - 1, 0)).xxx;
        
        // 如果在中途，进行二次插值（可选）
        if (t > 0.1)
        {
            int nextMip = min(baseMip + 1, 7);
            float3 gradNext = tex2Dlod(s_gradMip1_7, float4(uv, nextMip - 1, 0)).xxx;
            gradFiltered = lerp(gradFiltered, gradNext, t);
        }
    }
    
    // 距离加权融合（近处偏原始，远处偏 Mip）
    float distWeight = saturate(1.0 - length(gradOrig) * 0.5);
    return lerp(gradOrig, gradFiltered, _SSRMipBlend * distWeight);
}

// -----------------------------------------------------------------------------
// 核心改进 2: Screen Space Derivatives（ddx_ddy）
// -----------------------------------------------------------------------------
void ComputeScreenDerivatives(float2 uv, out float2 dUDx, out float2 dUDy)
{
#if defined(UNITY_USE_RASTERIZER_MODEL)
    // DX11+：使用内在函数
    dUDx = ddx(uv);
    dUDy = ddy(uv);
#else
    // Fallback：手动计算
    float2 pixelSize = float2(1.0 / _ScreenWidth, 1.0 / _ScreenHeight);
    dUDx = float2(pixelSize.x, 0.0);
    dUDy = float2(0.0, pixelSize.y);
#endif
}

// -----------------------------------------------------------------------------
// 核心改进 3: 增强的 TraceSSR_Mip（带抗闪的完整版本）
// -----------------------------------------------------------------------------
float3 TraceSSR_Mip(float3 wp, float3 rd, float maxDist, float stepsF,
                    float thickness, float edgeFade, out float conf,
                    float3 gradient, float3 gradNormal)
{
    conf = 0.0;
    float3 col = float3(0, 0, 0);
    
    // 性能优化：降低采样率
    float sampleRate = saturate(_SSRSampleRate);
    if (sampleRate < 1.0 && fmod(_Time.y, (1.0/sampleRate)) > 0.0)
        return col; // 跳帧采样
    
    float3 P1 = wp + rd * maxDist;
    float4 c0 = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
    float4 c1 = mul(UNITY_MATRIX_VP, float4(P1, 1.0));
    float vz0 = -mul(UNITY_MATRIX_V, float4(wp, 1.0)).z;
    float vz1 = -mul(UNITY_MATRIX_V, float4(P1, 1.0)).z;
    if (c0.w <= 1e-3 || c1.w <= 1e-3 || vz1 <= vz0 + 1e-3) return col;

    float2 u0 = ClipToScreenUV(c0), u1 = ClipToScreenUV(c1);
    int steps = (int)clamp(stepsF * sampleRate, 4.0, 24.0); // 减半步数！
    
    float tHit = -1.0, tPrev = 0.0;
    float2 hitUV = float2(0, 0);
    
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
            for (int j = 0; j < 4; j++) // 减少二分次数
            {
                float m = (lo + hi) * 0.5;
                float svm = LinearEyeDepth(SampleSceneDepth(lerp(u0, u1, m)), _ZBufferParams);
                if (lerp(vz0, vz1, m) > svm - thickness) hi = m; else lo = m;
            }
            tHit = (lo + hi) * 0.5;
            hitUV = lerp(u0, u1, tHit);
            break;
        }
        tPrev = t;
    }
    
    if (tHit < 0.0) return col;

    // 计算可信度
    float2 uvH = hitUV;
    float sceneH = LinearEyeDepth(SampleSceneDepth(uvH), _ZBufferParams);
    float vzH = lerp(vz0, vz1, tHit);
    float cThick = saturate(1.0 - abs(vzH - sceneH) / max(thickness, 1e-3));
    float cDist  = 1.0 - tHit * tHit;
    float2 e = min(uvH, 1.0 - uvH);
    float cEdge = saturate(min(e.x, e.y) * edgeFade);
    conf = cThick * cDist * cEdge;
    
    // 核心改进：使用 Mip 梯度修正反射方向
    float3 finalGradient = gradient;
    
    if (_SSRAntiFlicker > 0.5 && stepCount == 1) // stepCount 为调试标志
    {
        // 计算屏幕导数
        float2 dUDx, dUDy;
        ComputeScreenDerivatives(uvH, dUDx, dUDy);
        
        // 计算 footprint
        float2 footprint = CalculateScreenFootprint(uvH, dUDx, dUDy);
        
        // 采样 Mip 梯度
        finalGradient = SampleGradientWithMip(uvH, gradNormal, max(footprint.x, footprint.y), true);
        
        // 平滑反射方向
        float3 reflDir = reflect(rd, normalize(finalGradient));
        col = SampleSceneColor(uvH) * lerp(1.0, 0.7, 1.0 - conf); // 可信度越低越暗
        
        // 防闪烁：距离衰减
        float dist = distance(wp, P1 * tHit);
        col *= saturate(1.0 - dist * 0.01);
    }
    else
    {
        // 原版逻辑
        col = SampleSceneColor(uvH) * lerp(1.0, 0.7, 1.0 - conf);
    }
    
    return col;
}

// -----------------------------------------------------------------------------
// 简化版封装（供现有代码调用）
// -----------------------------------------------------------------------------
float3 TraceSSR_Enhanced(float3 wp, float3 rd, float maxDist, float stepsF,
                         float thickness, float edgeFade, out float conf)
{
    // 默认参数（假设已有梯度信息）
    float3 dummyGrad = float3(0, 1, 0);
    float3 dummyNorm = float3(0, 0, 1);
    return TraceSSR_Mip(wp, rd, maxDist, stepsF, thickness, edgeFade, conf, dummyGrad, dummyNorm);
}

#endif // SSR_ANTIFLICKER_INCLUDED
