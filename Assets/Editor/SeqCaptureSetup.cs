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
    }
}
#endif
