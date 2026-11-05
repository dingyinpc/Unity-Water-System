// SkyBlurFeature.cs — R135 天空轻微模糊（只作用于天空）
//
// 注入点 AfterRenderingSkybox：此刻天空已经画好、透明物（水面）还没画，
// 所以这一步模糊到不了水面和地形上。
// 三步：①原图拷到 src ②降到半分辨率做 横+竖 两趟高斯 ③按深度掩码把模糊结果混回原图。
//
// 安装/卸载走菜单 Tools/R135/90 / 90b（唯一写入点）。
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.Render
{
    public class SkyBlurFeature : ScriptableRendererFeature
    {
        class Pass : ScriptableRenderPass
        {
            internal RTHandle srcRT, halfA, halfB;
            Material _mat;
            float _strength;

            static readonly int P_Blur = Shader.PropertyToID("_BlurTex");
            static readonly int P_Step = Shader.PropertyToID("_BlurStep");
            static readonly int P_Str  = Shader.PropertyToID("_SkyBlurStrength");

            public Pass(Material mat)
            {
                _mat = mat;
                // R135：注入点必须选在**MSAA 已解析之后**。AfterRenderingSkybox 时相机颜色还是
                //   4x MSAA 的纹理，直接采样得到全黑（实测整屏亮度 131 → 49）。
                //   BeforeRenderingPostProcessing 时颜色已解析成普通纹理，且水面虽然画过了，
                //   但深度掩码只让"远平面"的像素被模糊，水面（背后有海底深度）不受影响。
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }

            public void SetStrength(float s) => _strength = Mathf.Clamp01(s);

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_mat == null) return;
                var cam = renderingData.cameraData;
                if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;

                var cmd = CommandBufferPool.Get("SkyBlur");
                var desc = cam.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;

                RenderingUtils.ReAllocateIfNeeded(ref srcRT, desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "SkyBlurSrc");

                var hd = desc;
                hd.width  = Mathf.Max(desc.width  >> 1, 16);
                hd.height = Mathf.Max(desc.height >> 1, 16);
                RenderingUtils.ReAllocateIfNeeded(ref halfA, hd, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "SkyBlurA");
                RenderingUtils.ReAllocateIfNeeded(ref halfB, hd, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "SkyBlurB");

                var target = cam.renderer.cameraColorTargetHandle;

                // ⚠ 强度必须写到**材质属性**：写成全局 (_Strength) 会被材质自带默认值 1 盖掉，
                //   实测 0.5 和 0.85 两档画面逐像素相同。
                _mat.SetVector(P_Step, new Vector4(1f / desc.width, 1f / desc.height, 0, 0));
                cmd.SetGlobalFloat(P_Str, _strength);

                Blitter.BlitCameraTexture(cmd, target, srcRT);              // ① 原图拷贝
                Blitter.BlitCameraTexture(cmd, srcRT, target, _mat, 0);     // ② 深度感知 3×3

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        public Shader blurShader;
        [Range(0f, 1f)] public float strength = 0.85f;

        // 菜单改这里（静态字段）：Play 中保存渲染器资产会触发重导入，
        // 资产上的实例会被换掉，改资产`strength`经常"看起来写了却不生效"。
        public static float StrengthOverride = 0.85f;

        Pass _pass;

        public override void Create()
        {
            if (blurShader == null) blurShader = Shader.Find("Hidden/WaterProject/SkyBlur");
            var mat = blurShader != null ? new Material(blurShader) { name = "SkyBlur(runtime)" } : null;
            _pass = new Pass(mat);
            _pass.SetStrength(strength);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null) return;
            var cam = renderingData.cameraData;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            _pass.SetStrength(StrengthOverride >= 0f ? StrengthOverride : strength);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.srcRT?.Release();
            _pass?.halfA?.Release();
            _pass?.halfB?.Release();
            _pass = null;
        }
    }
}
