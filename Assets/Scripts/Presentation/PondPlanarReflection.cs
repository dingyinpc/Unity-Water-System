// PondPlanarReflection.cs — 平面反射（镜像相机 + 斜投影裁剪面）
//
// URP 14 没有内置平面反射，官方文档给的做法就是"自己渲一台子相机到 RenderTexture"
// （见 universal@14.0.11/Documentation~/customize/inject-render-pass-via-script.md）。
//
// 为什么不用 Reflection Probe：探针是"单点立方图"，在**水平镜面**上视差错误非常明显
// —— 岸上的树会倒映在错误的位置，越靠近水面越穿帮。潭水几乎就是一个水平镜面，
// 所以这里用平面反射。
//
// 三步：
//   1) 把主相机关于水面平面对称摆放（位置 + 姿态）
//   2) 用斜投影把"水面以下"的半空间裁掉，否则池底的泥会被倒映到水面上
//   3) 渲到 RT，并把 世界→镜像裁剪空间 的矩阵交给水材质去采样
//
// 驱动方式：镜像相机是一台**正常启用**的相机，depth 比主相机低 1，所以 URP 每帧会
// 先渲它、主相机当帧就能用到新的 RT。我们只在 beginCameraRendering 里、且只针对
// "镜像相机自己"这一帧更新它的位姿和投影。
//   —— 不用 `camera.enabled=false` + 手动 `cam.Render()`：那条路在 URP 下要自己管
//      renderer 初始化，容易出"渲出全黑"这种难查的问题。
//
// 表现层脚本（Presentation）：只负责"画面怎么出来"，不含玩法逻辑。
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WaterProject.Presentation
{
    [RequireComponent(typeof(Camera))]
    public class PondPlanarReflection : MonoBehaviour
    {
        [Header("水面")]
        [Tooltip("水面所在高度（世界 Y）。反射平面就是它。")]
        public float waterHeight = 0.02f;

        [Tooltip("被反射的水面自身渲染器 —— 必须从镜像相机的 cullingMask 里排除，否则水皮被渲进倒影里。")]
        public Renderer waterRenderer;

        [Header("质量")]
        [Range(0.25f, 1f)] public float resolutionScale = 0.5f;
        [Tooltip("反射用的层掩码。留 0 = 跟随主相机再剔除水所在层。")]
        public LayerMask reflectLayers = 0;

        [Tooltip("关掉斜投影裁剪面可以做 A/B：水下的泥会不会被倒映上来。")]
        public bool useObliqueClip = true;

        [Tooltip("反射相机相对主相机的 depth 偏移（负数 = 更早渲染）。")]
        public int depthOffset = -1;

        [Header("调试")]
        public bool debugLogOnce = false;

        static readonly int ID_ReflClip  = Shader.PropertyToID("_WorldToReflClip");
        static readonly int ID_PlanarRef = Shader.PropertyToID("_PlanarRef");

        Camera _cam;
        RenderTexture _rt;
        int _rtW = -1, _rtH = -1;
        bool _logged;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.allowHDR = true;
            _cam.allowMSAA = false;
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.backgroundColor = new Color(0.5f, 0.7f, 0.85f, 0f);
            _cam.allowDynamicResolution = false;

            var data = _cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;   // 反射里不要再套一层 ACES
                data.renderShadows = false;          // 反射阴影太贵，潭面也看不出来
            }
        }

        void OnEnable()  { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering; }
        void OnDisable() { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering; }

        void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        }

        // 只处理"镜像相机自己即将渲染"这一次回调。
        // 主相机的回调要跳过 —— 否则每台相机都触发一次反射渲染，直接递归爆掉。
        void OnBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera source)
        {
            if (source != _cam) return;

            Camera main = MainCamera();
            if (main == null || main == _cam) return;

            EnsureRenderTexture(main);
            SetupMirrorCamera(main);
            PublishToWater();
        }

        Camera MainCamera()
        {
            var c = Camera.main;
            if (c != null && c != _cam) return c;
            foreach (var cam in Camera.allCameras)
                if (cam != _cam && cam.cameraType == CameraType.Game) return cam;
            return null;
        }

        void EnsureRenderTexture(Camera main)
        {
            int w = Mathf.Max(64, Mathf.RoundToInt(main.pixelWidth  * resolutionScale));
            int h = Mathf.Max(64, Mathf.RoundToInt(main.pixelHeight * resolutionScale));
            if (_rt != null && w == _rtW && h == _rtH) return;

            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default)
            {
                name = "PondPlanarReflection",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
            _rtW = w; _rtH = h;
            _cam.targetTexture = _rt;
        }

        void SetupMirrorCamera(Camera main)
        {
            _cam.depth = main.depth + depthOffset;

            Transform st = main.transform;

            // ── 1) 镜像：关于水平面 y = waterHeight 对称 ──
            // 水平面的镜像非常干净：y 翻折、pitch 取反、roll 取反、yaw 不变。
            Vector3 sp = st.position;
            Vector3 rp = new Vector3(sp.x, 2f * waterHeight - sp.y, sp.z);

            // ⚠ 不能写成 LookRotation(fwd 取反 y, up 取反 y)。反射后的 (right,up,fwd) 是
            //   【左手系】，LookRotation 强行右手化的效果是同时把 right 和 up 取反，
            //   等于再绕视轴滚 180° —— 于是 RT 里的倒影整个上下颠倒
            //   （菜单67 导出的 RT 会看到"地面在上、天空在下"）。
            //   直接对欧拉角动手：pitch 取反、roll 取反、yaw 不变。
            Vector3 e = st.rotation.eulerAngles;
            transform.SetPositionAndRotation(rp, Quaternion.Euler(-e.x, e.y, -e.z));

            // ── 2) 参数对齐主相机 ──
            _cam.nearClipPlane = main.nearClipPlane;
            _cam.farClipPlane  = main.farClipPlane;
            _cam.fieldOfView   = main.fieldOfView;
            _cam.aspect        = main.aspect;
            _cam.orthographic  = main.orthographic;
            _cam.orthographicSize = main.orthographicSize;
            _cam.rect          = main.rect;

            // 层掩码：跟随主相机，但一定要把水自己剔掉
            _cam.cullingMask = reflectLayers != 0 ? reflectLayers : main.cullingMask;
            if (waterRenderer != null)
                _cam.cullingMask &= ~(1 << waterRenderer.gameObject.layer);

            // ── 3) 斜投影：把水面以下的半空间裁掉 ──
            _cam.ResetWorldToCameraMatrix();
            _cam.ResetProjectionMatrix();
            if (useObliqueClip)
            {
                Vector4 worldPlane = new Vector4(0f, 1f, 0f, -waterHeight);
                Vector4 camPlane = WorldPlaneToCameraSpace(worldPlane, _cam);
                _cam.projectionMatrix = ObliqueNear(_cam.projectionMatrix, camPlane);
            }

            if (debugLogOnce && !_logged)
            {
                _logged = true;
                Debug.Log($"[PondPlanarReflection] rt={_rt.width}x{_rt.height} " +
                          $"src={sp} refl={rp} cull=0x{_cam.cullingMask:X8} oblique={useObliqueClip}");
            }
        }

        static Vector4 WorldPlaneToCameraSpace(Vector4 worldPlane, Camera cam)
        {
            // 平面是余向量：plane_cam = (M_w2c)^{-T} · plane_w = (M_c2w)^T · plane_w
            Matrix4x4 c2w = cam.cameraToWorldMatrix;
            Vector4 p = new Vector4(
                c2w.m00 * worldPlane.x + c2w.m10 * worldPlane.y + c2w.m20 * worldPlane.z + c2w.m30 * worldPlane.w,
                c2w.m01 * worldPlane.x + c2w.m11 * worldPlane.y + c2w.m21 * worldPlane.z + c2w.m31 * worldPlane.w,
                c2w.m02 * worldPlane.x + c2w.m12 * worldPlane.y + c2w.m22 * worldPlane.z + c2w.m32 * worldPlane.w,
                c2w.m03 * worldPlane.x + c2w.m13 * worldPlane.y + c2w.m23 * worldPlane.z + c2w.m33 * worldPlane.w);

            // ⚠ 只归一化，**不要**因为 p.w<0 就把整个平面取反。
            //   世界平面给的是 (0,1,-waterHeight)，即"水面上方为正"= 要保留的半空间。
            //   镜像相机站在水面以下，它到自己的近平面的带符号距离本来就是负的 ——
            //   这是近平面的正常状态（任何相机的近平面都在它前面），不是需要修正的异常。
            //   一旦取反，保留的半空间就翻成"水面以下"，RT 里映出的会是潭底自己的底盘：
            //   菜单67 导出的 RT 正中那块大椭圆暗块就是 PoolBasin，它把整条对岸倒影盖掉了。
            float len = new Vector3(p.x, p.y, p.z).magnitude;
            if (len > 1e-6f) p /= len;
            return p;
        }

        static float Sgn(float a) => a > 0f ? 1f : (a < 0f ? -1f : 1f);

        // "Plane Clipping in 3 Steps"：改写投影矩阵第三行，让近平面贴合任意裁剪平面
        static Matrix4x4 ObliqueNear(Matrix4x4 proj, Vector4 clipPlane)
        {
            Vector4 q = proj.inverse.MultiplyVector(
                new Vector4(Sgn(clipPlane.x), Sgn(clipPlane.y), 1f, 1f));
            Vector4 v = clipPlane * (2f / Vector4.Dot(q, clipPlane));

            // 索引是列主序：m[2]=m20, m[6]=m21, m[10]=m22, m[14]=m23 —— 即第三行
            proj[2]  = v.x - q.x;
            proj[6]  = v.y - q.y;
            proj[10] = v.z - q.z + 1f;
            proj[14] = v.w - q.w;
            return proj;
        }

        void PublishToWater()
        {
            if (waterRenderer == null) return;

            // 世界坐标 → 镜像相机裁剪空间。水材质拿它算反射 UV，
            // 这样倒影是**视差正确**的（直接拿屏幕 UV 去采会在近处错位）。
            // 2022.3 的 Camera 没有 gpuProjectionMatrix，要自己过一遍 GL 转换；
            // 第二个参数 true = 渲到 RenderTexture（决定 Y 轴翻转方向）。
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(_cam.projectionMatrix, true);
            Matrix4x4 worldToReflClip = gpuProj * _cam.worldToCameraMatrix;

            // 场景里只有一块潭水，直接写 sharedMaterial 最省事也最不容易出错。
            // （MaterialPropertyBlock 是"每渲染器"的，不能按材质槽循环设置。）
            foreach (var mat in waterRenderer.sharedMaterials)
            {
                if (mat == null) continue;
                // 这两个是 HLSL 里声明的、不在 Properties 块内，HasProperty 判不准；
                // 而设一个 shader 不认的属性本身是无害空操作，所以直接设。
                mat.SetMatrix(ID_ReflClip, worldToReflClip);
                mat.SetTexture(ID_PlanarRef, _rt);
            }
        }
    }
}
