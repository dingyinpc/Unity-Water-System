// SSRTemporalFeature.cs — R22 C7 抗闪：SSR 时域累积的渲染器特性
//
// 注入点 BeforeRenderingTransparents，每帧三步：
//   ① 打开全局关键字 _SSR_WRITE，把**透明队列**重画一遍到 _SSRTraceRT——
//      只有 Water_Advanced 带该变体分支（片元算完 N/反射向后早退，只输出
//      half4(SSR色, conf)），其它透明 shader 没有这个分支、按原样画（Main
//      场景透明物体只有水面，互不污染）。不带深度目标：水面单层无自遮挡，
//      且 _SSR_WRITE 分支里追踪还要**采样**相机深度，绑成深度目标会打架。
//   ② _SSRTrace + 历史 → EMA（Hidden/WaterProject/SSR_TemporalEMA）→ _SSRAccum。
//   ③ _SSRAccum 设为全局纹理 _SSRAccumTex，水面主 pass 里 _SSRTemporalOn>0.5
//      时直接查表不再逐像素追踪（关=旧内联路径，逐位一致零回归）。
//
// ⚠ 静态相机假设：EMA 在屏幕 UV 空间逐像素复用历史（Main 验收机位满足）；
//   动起来会拖鬼影，动态场景请保持 _SSRTemporalOn=0（菜单 583）。
// 安装/卸载/开关全部走菜单 Tools/Fix/583（唯一写入点）。
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.Render
{
    public class SSRTemporalFeature : ScriptableRendererFeature
    {
        class Pass : ScriptableRenderPass
        {
            internal RTHandle traceRT, accumRT, histRT;
            Material _ema;
            ShaderTagId _tag = new ShaderTagId("UniversalForward");
            FilteringSettings _filter;

            static readonly int P_Trace = Shader.PropertyToID("_TraceTex");
            static readonly int P_Hist  = Shader.PropertyToID("_HistTex");
            static readonly int P_Accum = Shader.PropertyToID("_SSRAccumTex");
            static readonly int P_On    = Shader.PropertyToID("_SSRTemporalOn");

            public Pass(Material ema)
            {
                _ema = ema;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                _filter = new FilteringSettings(RenderQueueRange.transparent);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("SSR Temporal");
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                int w = Mathf.Max(desc.width >> 1, 16), h = Mathf.Max(desc.height >> 1, 16);

                // 半分辨率足够（EMA 本来就是模糊化的），半幅省 3/4 带宽
                var rtDesc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0)
                { msaaSamples = 1 };
                RenderingUtils.ReAllocateIfNeeded(ref traceRT, rtDesc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "SSRTrace");
                RenderingUtils.ReAllocateIfNeeded(ref accumRT, rtDesc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "SSRAccum");
                RenderingUtils.ReAllocateIfNeeded(ref histRT, rtDesc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "SSRHist");

                // ① 预通道：_SSR_WRITE 开 ⇒ Water_Advanced 早退输出 (色, conf)
                //    RenderStateBlock 强制不混合（水材质自带的 SrcAlpha 混合会把色乘 conf）
                //    + 不写深度不测深度（单层水面无自遮挡；追踪还要采样相机深度）
                Shader.EnableKeyword("_SSR_WRITE");
                cmd.SetRenderTarget(traceRT);
                cmd.ClearRenderTarget(false, true, Color.clear);
                context.ExecuteCommandBuffer(cmd); cmd.Clear();
                var draw = CreateDrawingSettings(_tag, ref renderingData, SortingCriteria.CommonTransparent);
                var stateBlock = new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth)
                {
                    blendState = new BlendState(),                                // 默认 = 不混合(One/Zero)
                    depthState = new DepthState(false, CompareFunction.Always),   // 不测不写
                };
                context.DrawRenderers(renderingData.cullResults, ref draw, ref _filter, ref stateBlock);
                Shader.DisableKeyword("_SSR_WRITE");

                // ② EMA：trace + hist → accum
                cmd.SetGlobalTexture(P_Trace, traceRT);
                cmd.SetGlobalTexture(P_Hist, histRT);
                Blitter.BlitCameraTexture(cmd, traceRT, accumRT, _ema, 0);

                // ③ accum 拷回 hist（下一帧的历史）+ 对外全局
                Blitter.BlitCameraTexture(cmd, accumRT, histRT);
                cmd.SetGlobalTexture(P_Accum, accumRT);
                cmd.SetGlobalFloat(P_On, 1f);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        public Material emaMaterial;   // 菜单 583 赋值（Hidden/WaterProject/SSR_TemporalEMA）
        Pass _pass;

        public override void Create()
        {
            if (emaMaterial == null)
            {
                var sh = Shader.Find("Hidden/WaterProject/SSR_TemporalEMA");
                if (sh != null) emaMaterial = new Material(sh) { name = "SSR_TemporalEMA(runtime)" };
            }
            _pass = new Pass(emaMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (emaMaterial == null) return;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.traceRT?.Release();
            _pass?.accumRT?.Release();
            _pass?.histRT?.Release();
            _pass = null;
        }
    }
}
