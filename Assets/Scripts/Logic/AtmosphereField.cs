// AtmosphereField.cs — 逻辑层：大气场辅助 RT（P2-3 / 参考 S14 云影 RT）
//
// 职责：逐帧把「世界 XZ 云影透射率图」烘出来，全局暴露：
//   _AFCloudTex  （R = 太阳透射率 0..1，1=无云遮挡）
//   _AFRect      （minX, minZ, sizeX, sizeZ）
//   _AFOn        （0/1 总开关）
// 水面 shader 按 世界XZ → _AFRect 映射采样，用透射率衰减 GGX 直射高光（S14）。
//
// 与参考的差异：参考是"体积云 raymarch 时顺路写云影 RT"；我们的云在天空 shader 里，
//   故这里用同一套 fbm 单独烘一张投影图（形状同源）。
using UnityEngine;

namespace WaterProject.Logic
{
    public class AtmosphereField : MonoBehaviour
    {
        [Header("域（世界 XZ 矩形）")]
        public float minX = -500f, minZ = -300f, sizeX = 1000f, sizeZ = 600f;
        [Tooltip("云影图分辨率")]
        public int res = 256;

        [Header("云（与 Sky_Coast 体积云同源：R3 起云影=天上的云的中层切片）")]
        public float cloudScale = 0.00042f;
        [Range(0f, 1f)] public float cloudCover = 0.52f;
        public float windDeg = 200f;
        public float windSpeed = 15f;
        public float shear = 0.006f;
        [Tooltip("云层高度（米）：太阳越低影子拉得越长")]
        public float cloudHeight = 900f;
        [Tooltip("云光学厚度：越大云影越暗")]
        public float optical = 1.8f;

        [Header("太阳（留空 = 用场景主方向光）")]
        public Light sunLight;

        const string ShaderName = "WaterProject/AtmosphereField";
        static readonly int P_Rect   = Shader.PropertyToID("_AFRect");
        static readonly int P_SunDir = Shader.PropertyToID("_AFSunDir");
        static readonly int P_Scale  = Shader.PropertyToID("_AFCloudScale");
        static readonly int P_Cover  = Shader.PropertyToID("_AFCloudCover");
        static readonly int P_WindDir = Shader.PropertyToID("_AFWindDir");
        static readonly int P_WindSpd = Shader.PropertyToID("_AFWindSpeed");
        static readonly int P_Shear  = Shader.PropertyToID("_AFShear");
        static readonly int P_Height = Shader.PropertyToID("_AFHeight");
        static readonly int P_Opt    = Shader.PropertyToID("_AFOptical");
        static readonly int P_Tex    = Shader.PropertyToID("_AFCloudTex");
        static readonly int P_On     = Shader.PropertyToID("_AFOn");

        Material _mat;
        RenderTexture _rt;
        public bool Ready => _rt != null;

        public void Build()
        {
            Release();
            var sh = Shader.Find(ShaderName);
            if (sh == null) { Debug.LogError("[AtmosphereField] 找不到 " + ShaderName); return; }
            _mat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _rt = new RenderTexture(Mathf.Max(32, res), Mathf.Max(32, res), 0, RenderTextureFormat.ARGBHalf)
            { name = "AF_CloudShadow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _rt.Create();
            Render();
            Shader.SetGlobalTexture(P_Tex, _rt);
            Shader.SetGlobalVector(P_Rect, new Vector4(minX, minZ, sizeX, sizeZ));
            Shader.SetGlobalFloat(P_On, 1f);
        }

        void Render()
        {
            if (_mat == null || _rt == null) return;
            Light L = sunLight != null ? sunLight : RenderSettings.sun;
            Vector3 d = (L != null) ? -L.transform.forward : new Vector3(0f, 0.6f, 0.8f);
            _mat.SetVector(P_Rect, new Vector4(minX, minZ, sizeX, sizeZ));
            _mat.SetVector(P_SunDir, new Vector4(d.x, d.y, d.z, 0f));
            _mat.SetFloat(P_Scale, cloudScale);
            _mat.SetFloat(P_Cover, cloudCover);
            _mat.SetFloat(P_WindDir, windDeg);
            _mat.SetFloat(P_WindSpd, windSpeed);
            _mat.SetFloat(P_Shear, shear);
            _mat.SetFloat(P_Height, cloudHeight);
            _mat.SetFloat(P_Opt, optical);
            Graphics.Blit(null, _rt, _mat, 0);
        }

        /// <summary>导出云影透射率图（供检测报告）。</summary>
        public RenderTexture RenderViz(float viz)
        {
            if (_mat == null) Build();
            if (_mat == null) return null;
            _mat.SetFloat(Shader.PropertyToID("_AFViz"), viz);
            var outRT = RenderTexture.GetTemporary(_rt.width, _rt.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(null, outRT, _mat, 0);
            return outRT;
        }

        void Update()
        {
            if (!Ready)
            {
                if (!Application.isPlaying) return;   // 编辑模式不自动建，等菜单 170
                Build();
                if (!Ready) return;
            }
            if (Application.isPlaying) Render();      // 编辑模式静态保持
        }

        public void Release()
        {
            if (_rt) { _rt.Release(); DestroyImmediate(_rt); _rt = null; }
            Shader.SetGlobalFloat(P_On, 0f);
        }

        void OnDisable() { Shader.SetGlobalFloat(P_On, 0f); }
        void OnDestroy() { Release(); if (_mat) DestroyImmediate(_mat); }
    }
}
