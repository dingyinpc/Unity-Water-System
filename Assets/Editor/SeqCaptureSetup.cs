// SeqCaptureSetup.cs — R1 连帧采集的编辑器菜单（《10》§3 协议的"我方半边"）
//
// 用法（桥/无头）：切到目标场景 → Tools/Play/90 进 Play → 等 2~3s 水面相位稳定
// → Tools/Seq/91 采集 3 秒 → 等探针出 "SEQ done" → Tools/Play/91 退 Play。
// 产物：Docs/screenshots/seq_<场景>_<时间戳>/f******_t**.***.jpg + meta.txt
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class SeqCaptureSetup
    {
        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

        [MenuItem("Tools/Seq/91 采集3秒(96帧 1920x1080 JPG 序列)")]
        public static void Capture3s()
        {
            if (!Application.isPlaying)
            { WriteProbe("SEQ91 REFUSED: 仅限 Play 模式（先 Tools/Play/90）\n"); return; }

            var sc = Object.FindObjectOfType<WaterProject.Logic.SeqCapture>();
            if (sc == null)
            {
                var go = new GameObject("SeqCapture");
                go.AddComponent<WaterProject.Logic.SeqCapture>();
                sc = go.GetComponent<WaterProject.Logic.SeqCapture>();
            }
            if (sc.Busy) { WriteProbe("SEQ91 REFUSED: 上一次采集还没结束\n"); return; }
            sc.StartCapture();
            WriteProbe("SEQ91 started frames=" + sc.frames + "\n");
        }

        [MenuItem("Tools/Seq/92 采集器状态(只写探针)")]
        public static void Status()
        {
            var sc = Object.FindObjectOfType<WaterProject.Logic.SeqCapture>();
            WriteProbe(sc == null
                ? "SEQ92 no capturer in scene\n"
                : "SEQ92 busy=" + sc.Busy + " done=" + sc.Done + "/" + sc.frames
                  + " dir=" + sc.LastDir + "\n");
        }

        // ═══════════════ 95 ═══════════════
        // 阻塞式离屏静帧：R20 颗粒/焦散定标用 —— 显式 cam.Render() 当场出图，
        // 不等 renderedFrameCount（失焦时那个永远不会前进）。与验收静帧同通路：
        // URP 完整后处理（含 FilmGrain/ACES）、ARGBHalf、MSAA=1、1920×1080 钉死。
        [MenuItem("Tools/Seq/95 静帧(离屏阻塞1920x1080, 写Docs/Temp)")]
        public static void StillFrame()
        {
            var cam = Camera.main;
            if (cam == null) { WriteProbe("SEQ95 REFUSED: 无 Camera.main\n"); return; }
            const int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf,
                                       RenderTextureReadWrite.Default) { antiAliasing = 1 };
            rt.Create();
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active; float prevAspect = cam.aspect;
            try
            {
                cam.aspect = W / (float)H;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                string dir = System.IO.Path.Combine(Application.dataPath, "../Docs/Temp");
                System.IO.Directory.CreateDirectory(dir);
                string p = System.IO.Path.Combine(dir,
                    "still_" + scene + "_" + System.DateTime.Now.ToString("HHmmss") + ".png");
                System.IO.File.WriteAllBytes(p, tex.EncodeToPNG());
                WriteProbe("SEQ95 still " + p + " playing=" + Application.isPlaying + "\n");
            }
            finally
            {
                cam.targetTexture = prevTarget; cam.aspect = prevAspect; RenderTexture.active = prevActive;
                Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            }
        }
    }
}
#endif
