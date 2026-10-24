// SeqCapture.cs — R1 动态对比基线：我方连帧采集（≥3 秒 JPG 序列）
//
// 为什么存在：验收体系此前全是"冻结静帧"（铁律 5 特意剔除运动），回答不了
// "动起来像不像"。本组件在 Play 中以固定 1920×1080 离屏渲染通路（与
// GameViewCapture 验收静帧同一条：URP 完整后处理、MSAA=1、aspect 钉死）
// 逐帧落 JPG，供 Tools/Video/seq_compare.py 与参考视频做"3 秒逐帧对比"
//（《10》§3 协议：运动能量/连续性/特征速度/闪烁主频 四判据）。
//
// 关键取舍：
//   · JPG(q75) 而非 PNG——编码快一个量级，采集期 FPS 不至于崩（验收看的是
//     亮度曲线不是无损像素）；
//   · 每帧文件名带游戏时刻 t（f000012_t3.456.jpg）——采集帧间隔不均匀时，
//     对比端按时间归一化（重采样到 30Hz 网格），不假设等间隔；
//   · 每帧渲染-读回-编码全在主线程，代价 ≈15~30ms/帧 ⇒ 采集期 FPS 会降到
//     30 上下，3 秒 ≈ 90~110 帧，正好覆盖协议要求；模拟时间不受影响
//     （timeScale=1，水位/云相位照常走）。
using System.IO;
using UnityEngine;

namespace WaterProject.Logic
{
    public class SeqCapture : MonoBehaviour
    {
        public static SeqCapture Instance { get; private set; }

        [Header("采集参数（《10》§3 协议）")]
        public int frames = 96;                 // ≈3 秒 @30fps
        public int width = 1920;
        public int height = 1080;
        [Range(30, 100)] public int jpgQuality = 75;
        public float warmup = 0.6f;             // 起播稳定期（水面相位/曝光）不计帧

        public bool Busy { get; private set; }
        public int Done { get; private set; }
        public string LastDir { get; private set; }

        RenderTexture _rt;
        Texture2D _tex;
        float _t0;
        int _idx;
        float _tStart;
        readonly System.Text.StringBuilder _meta = new();

        void Awake() { Instance = this; }

        void OnDestroy()
        {
            Release();
            if (Instance == this) Instance = null;
        }

        void Release()
        {
            if (_rt) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_tex) { Destroy(_tex); _tex = null; }
        }

        /// <summary>开始一次采集。dirTag 会拼进输出目录名（默认时间戳）。</summary>
        public void StartCapture(string dirTag = null)
        {
            if (Busy) return;
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[SeqCapture] 没有 Camera.main"); return; }

            // 失焦即停摆的老坑（GameViewCapture ③）：采集期间必须后台继续跑
            Application.runInBackground = true;

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string tag = string.IsNullOrEmpty(dirTag) ? System.DateTime.Now.ToString("HHmmss") : dirTag;
            LastDir = Path.Combine(Application.dataPath, "../Docs/screenshots",
                                   "seq_" + scene + "_" + tag);
            Directory.CreateDirectory(LastDir);
            // 清掉同名残留——混入旧帧会污染曲线
            foreach (var f in Directory.GetFiles(LastDir, "*.jpg")) File.Delete(f);

            _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf,
                                    RenderTextureReadWrite.Default) { antiAliasing = 1 };
            _rt.Create();
            _tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

            _idx = 0; Done = 0;
            _tStart = Time.time;
            _t0 = Time.time + warmup;
            _meta.Clear();
            _meta.AppendLine("seq_capture_v1");
            _meta.AppendLine("scene=" + scene + " tag=" + tag);
            _meta.AppendLine("frames_target=" + frames + " size=" + width + "x" + height + " jpg_q=" + jpgQuality);
            Busy = true;
            Debug.Log("[SeqCapture] start dir=" + LastDir + " target=" + frames + "帧");
        }

        void LateUpdate()
        {
            if (!Busy) return;
            if (Time.time < _t0) return;
            if (_idx >= frames) { Finish(); return; }

            var cam = Camera.main;
            if (cam == null) { Finish(); return; }

            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            float prevAspect = cam.aspect;
            try
            {
                cam.aspect = width / (float)height;
                cam.targetTexture = _rt;
                cam.Render();                       // 与验收静帧同一条完整 URP 通路

                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                _tex.Apply();
                float t = Time.time;
                File.WriteAllBytes(Path.Combine(LastDir,
                    string.Format("f{0:D6}_t{1:F3}.jpg", _idx, t)),
                    _tex.EncodeToJPG(jpgQuality));
                _meta.AppendLine(string.Format("{0:D6},{1:F3}", _idx, t));
                _idx++; Done = _idx;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.aspect = prevAspect;
                RenderTexture.active = prevActive;
            }
        }

        void Finish()
        {
            Busy = false;
            float wall = Time.realtimeSinceStartup;
            _meta.AppendLine(string.Format("captured={0} wall_fps_note=见上行各帧t差", _idx));
            File.WriteAllText(Path.Combine(LastDir, "meta.txt"), _meta.ToString());
            Release();
            Debug.Log("[SeqCapture] done dir=" + LastDir + " frames=" + _idx
                      + " 采集起止 t=" + _tStart.ToString("F2") + "~" + Time.time.ToString("F2"));
#if UNITY_EDITOR
            File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "SEQ done dir=" + LastDir + " frames=" + _idx + "\n");
#endif
        }
    }
}
