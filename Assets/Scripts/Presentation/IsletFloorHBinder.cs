// IsletFloorHBinder.cs —— 把 menu 91 烘焙的解析地面高度图挂成全局纹理
//
// 为什么存在（2026-10-04 轮）：URP 14 的 _CameraDepthTexture 在本机两条取帧
// 通路里都不可信 —— 离屏 cam.Render() 全远平面；GameView 通路近场正确、
// ~10m 外读数膨胀、~20m 归零（查遍 MSAA/RT/QualitySettings/RendererFeature
// 无果，判为 URP 内部问题）。而本项目的几何真值本来就是 LandHeight 解析函数，
// menu 91 把它烘成 R16 高度图，水在 shader 里直接算 vd = 0 − floorY，
// 从此与深度纹理零耦合。
//
// 这个组件只做一件事：进入 Play 时把纹理和参数写进全局槽
//（编辑器域里 Shader.SetGlobalTexture 不跨域重载持久，所以必须挂在
//  场景物体上每次 OnEnable 重写；menu 91/92 在编辑模式也会写一次，
//  让非 Play 的预览同样正确）。

using UnityEngine;

namespace WaterProject.Presentation
{
    public class IsletFloorHBinder : MonoBehaviour
    {
        [Tooltip("menu 91 烘焙的地面高度图（R16，已归一化）")]
        public Texture2D floorH;

        [Tooltip("(originX, originZ, sizeXZ, hMin)")]
        public Vector4 rect = new Vector4(-256f, -120f, 512f, -8f);

        [Tooltip("高度上界（编码时 r=1 对应的真实 y）")]
        public float heightMax = 6f;

        static readonly int P_Tex  = Shader.PropertyToID("_IsletFloorH");
        static readonly int P_Rect = Shader.PropertyToID("_IsletFloorHRect");
        static readonly int P_HMax = Shader.PropertyToID("_IsletFloorHH");

        void OnEnable() => Bind();

        /// <summary>menu 91/92 也调用：编辑模式下立刻生效。</summary>
        public void Bind()
        {
            if (floorH == null) return;
            Shader.SetGlobalTexture(P_Tex, floorH);
            Shader.SetGlobalVector(P_Rect, rect);
            Shader.SetGlobalFloat(P_HMax, heightMax);
        }
    }
}
