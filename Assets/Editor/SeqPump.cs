// SeqPump.cs — 后台泵：编辑器失焦时让 Play 循环继续跑（无人值守采集的前提）
//
// 为什么需要它：2026-10-24 R21 轮实锤 —— 桥进 Play 后编辑器一失焦，player loop
// 就停在 frame=2/t=0.02（runInBackground=True 也救不了）。以前 R1~R19 的采集能
// 跑通，是因为当时 Unity 窗口恰好在前台。
// 实测：EditorApplication.update 失焦时仍会随桥命令触发，但
// QueuePlayerLoopUpdate() 排了队也没人执行（frame 纹丝不动）。
// 真正管用的是 EditorApplication.Step()（工具栏"逐帧"按钮的同款）：
// 暂停态下每调用一次 = 完整推进一帧（Update/LateUpdate/渲染都走）。
//
// 两个菜单：
//   93 开关   —— 每次桥命令唤醒 editor update 时步进一拍（配合轮询用）；
//   94 泵到采完 —— 一条命令内阻塞循环 Step，直到 SeqCapture.Busy=False。
// 无人值守采集标准流程：Seq/91 开始 → Seq/94 抽干 → 探针看 SEQ done。
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class SeqPump
    {
        static bool s_on;

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

        [MenuItem("Tools/Seq/93 后台泵开关(失焦续跑,写探针)")]
        public static void Toggle()
        {
            s_on = !s_on;
            if (s_on)
            {
                EnsureGameView();
                EditorApplication.update += Pump;
            }
            else EditorApplication.update -= Pump;
            WriteProbe($"SEQ93 background pump -> {s_on} frame={Time.renderedFrameCount} t={Time.time:F2}\n");
        }

        static void Pump()
        {
            if (!Application.isPlaying) return;
            // 暂停态下 Step 才是"推进一帧"；非暂停态 Step 是 no-op（工具栏按钮同款行为）
            bool wasPaused = EditorApplication.isPaused;
            if (!wasPaused) EditorApplication.isPaused = true;
            EditorApplication.Step();
            if (!wasPaused) EditorApplication.isPaused = false;
        }

        [MenuItem("Tools/Seq/94 泵到采完(阻塞,失焦采集用)")]
        public static void PumpUntilDone()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int steps = 0;
            var sc = Object.FindObjectOfType<WaterProject.Logic.SeqCapture>();
            bool wasPaused = EditorApplication.isPaused;
            EditorApplication.isPaused = true;            // 泵的每一拍都要有效
            try
            {
                while (sw.ElapsedMilliseconds < 25000)
                {
                    if (!Application.isPlaying) { WriteProbe("SEQ94 exited: not playing\n"); return; }
                    EditorApplication.Step();
                    steps++;
                    System.Threading.Thread.Sleep(8);     // 让 dt≈10~20ms，游戏时间按真实节奏走
                    if (sc == null || sc.Equals(null))
                        sc = Object.FindObjectOfType<WaterProject.Logic.SeqCapture>();
                    if (sc != null && !sc.Busy)
                    {
                        WriteProbe($"SEQ94 drained steps={steps} done={sc.Done} "
                                 + $"frame={Time.renderedFrameCount} wall={sw.ElapsedMilliseconds}ms\n");
                        return;
                    }
                }
                WriteProbe($"SEQ94 timeout steps={steps} busy={(sc != null ? sc.Busy.ToString() : "no-capturer")}\n");
            }
            finally
            {
                if (!wasPaused) EditorApplication.isPaused = false;
            }
        }

        // GameView 被关掉时补开一个（反射样板抄 GameViewCapture，那边是 private）
        static void EnsureGameView()
        {
            var gvType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            if (gvType == null) { WriteProbe("SEQ93 找不到 GameView 类型\n"); return; }
            var views = Resources.FindObjectsOfTypeAll(gvType);
            if (views != null && views.Length > 0) return;
            try
            {
                EditorWindow.GetWindow(gvType, false, "Game", false);
                WriteProbe("SEQ93 没有 GameView，已补开\n");
            }
            catch (System.Exception e)
            { WriteProbe("SEQ93 补开 GameView 失败: " + e.GetType().Name + " " + e.Message + "\n"); }
        }
    }
}
#endif
