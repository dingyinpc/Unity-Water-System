// GameViewCapture.cs — 取"带后处理的真实 Game 视图画面"的唯一实现
//
// 为什么单独一个类：这条通路前后栽过三次（《05》G-15 / G-18 / G-19），
// 每一次都是因为"取图"这件事散在几个菜单里各写一遍，于是修好一处、另一处继续
// 喂过期画面。潭线菜单65 和海岸线菜单85 现在共用这一份。
//
// 三条硬约束，改代码前先读：
//   ① 不能用 ScreenCapture.CaptureScreenshot —— 它只在编辑器处于前台时才落盘。
//   ② 不能用 cam.Render() —— 那条路不走 URP 后处理，ACES/Bloom 全无，
//      拿它当验收依据会得出整套错结论（《05》G-18）。
//   ③ 必须自证"拿到的是新帧"。Project Settings 里 runInBackground=0 时编辑器
//      一失焦 Play 循环就停，GameView 的 m_RenderTexture 会**停在最后一次前台
//      渲染上继续被读出来**（《05》G-19）。所以判据是 Time.renderedFrameCount
//      有没有前进，不是文件 mtime —— mtime 前进只证明文件被重写过。
//      超时宁可写 STALE 也不覆盖旧图。
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class GameViewCapture
    {
        static bool s_pending;
        static int  s_target;
        static double s_deadline;
        static string s_tag, s_outPath;

        /// <summary>异步取帧：置 runInBackground、等 renderedFrameCount 前进若干帧，再抓。</summary>
        internal static void Request(string tag, string outPath, int framesToWait = 3, double timeoutSec = 8.0)
        {
            if (!Application.isPlaying)
            {
                Write(tag + " REFUSED: 不在 Play 模式\n");
                return;
            }
            if (s_pending) { Write(tag + " 已有一次抓取在排队，忽略\n"); return; }

            // 失焦即停摆的根因就在这行（见文件头 ③）
            Application.runInBackground = true;

            // s_tag 必须先赋好 —— EnsureGameView 里的日志要用它
            s_tag = tag;
            s_outPath = outPath;

            // 没有 GameView 就没有 m_RenderTexture 可读 —— 而 GameView 会在切场景、
            // 退出 Play 等操作中被关掉（本轮实际遇到："没有打开的 GameView"）。
            // 在这里补开一个，让后面等帧的过程顺便把它渲染出来。
            EnsureGameView();

            s_pending = true;
            int from = Time.renderedFrameCount;
            s_target = from + Mathf.Max(2, framesToWait);
            s_deadline = EditorApplication.timeSinceStartup + timeoutSec;
            Write(tag + " request renderedFrame=" + from
                  + " isPlaying=" + Application.isPlaying + "\n");
            EditorApplication.update += GrabWhenFresh;
        }

        static void EnsureGameView()
        {
            var gvType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            if (gvType == null) { Write(s_tag + " 找不到 UnityEditor.GameView 类型\n"); return; }
            var views = Resources.FindObjectsOfTypeAll(gvType);
            if (views != null && views.Length > 0) return;
            try
            {
                EditorWindow.GetWindow(gvType, false, "Game", false);
                Write(s_tag + " 没有 GameView，已补开一个\n");
            }
            catch (System.Exception e)
            { Write(s_tag + " 补开 GameView 失败: " + e.GetType().Name + " " + e.Message + "\n"); }
        }

        static void GrabWhenFresh()
        {
            if (!Application.isPlaying)
            {
                Stop();
                Write(s_tag + " STALE: 抓取途中退出了 Play 模式，未覆盖旧图\n");
                return;
            }

            int now = Time.renderedFrameCount;
            bool fresh = now >= s_target;
            if (!fresh && EditorApplication.timeSinceStartup > s_deadline)
            {
                Stop();
                // 宁可不出图，也不能拿过期画面当验收依据
                Write(s_tag + " STALE renderedFrame=" + now + " 目标 " + s_target
                      + " —— 未覆盖旧图\n");
                return;
            }
            if (!fresh) return;

            Stop();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Write(s_tag + " grab at renderedFrame=" + now + " scene=" + scene.path + "\n");

            bool viaView = false;
            try { viaView = GrabFromGameView(s_outPath); }
            catch (System.Exception e)
            { Write(s_tag + " grab THREW " + e.GetType().Name + " :: " + e.Message + "\n"); }

            if (!viaView)
            {
                // ⚠ 这里**绝不能**写 s_outPath。ScreenCapture 没有"这是新帧"的证明，
                //   它要么需要编辑器在前台、要么就把上一张缓冲原样吐出来 ——
                //   本轮正是它把一张过期图写进了验收路径，看起来"改了没反应"。
                //   退路只写一个明确改名的旁证文件，验收路径保持不动。
                string alt = s_outPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
                           ? s_outPath.Substring(0, s_outPath.Length - 4) + ".unverified.png"
                           : s_outPath + ".unverified";
                try
                {
                    ScreenCapture.CaptureScreenshot(alt);
                    Write(s_tag + " UNVERIFIED: GameView RT 拿不到，ScreenCapture 无新鲜度证明，"
                          + "只写 " + alt + "；**验收路径 " + s_outPath + " 未覆盖**\n");
                }
                catch (System.Exception e)
                { Write(s_tag + " UNVERIFIED 抓取也失败: " + e.GetType().Name + " " + e.Message + "\n"); }
                return;
            }
            Write(s_tag + " grabbed " + s_outPath + "\n");
        }

        static void Stop() { s_pending = false; EditorApplication.update -= GrabWhenFresh; }

        static bool GrabFromGameView(string outPath)
        {
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

            // UnityEditor.GameView 是 internal 类型，只能全程反射
            var gvType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gvType == null) { Write(s_tag + " 找不到 UnityEditor.GameView 类型\n"); return false; }

            var views = Resources.FindObjectsOfTypeAll(gvType);
            if (views == null || views.Length == 0) { Write(s_tag + " 没有打开的 GameView\n"); return false; }

            // 只读字段，不 Invoke 任何方法 —— 2022.3 的 GameView.RenderView() 是带参数的，
            // Invoke(null) 会抛 TargetParameterCountException
            var rtField = gvType.GetField("m_RenderTexture", F);
            if (rtField == null) { Write(s_tag + " GameView 没有 m_RenderTexture 字段\n"); return false; }

            RenderTexture rt = null;
            foreach (var v in views)
            {
                rt = rtField.GetValue(v) as RenderTexture;
                if (rt != null && rt.IsCreated() && rt.width > 16 && rt.height > 16) break;
                rt = null;
            }
            if (rt == null) { Write(s_tag + " m_RenderTexture 里没有有效 RT\n"); return false; }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // GameView 的 RT 是底朝上的行序，直接编码会上下颠倒
            var px = tex.GetPixels32();
            var flipped = new Color32[px.Length];
            int row = tex.width;
            for (int y = 0; y < tex.height; y++)
                System.Array.Copy(px, (tex.height - 1 - y) * row, flipped, y * row, row);
            var outTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            outTex.SetPixels32(flipped);
            outTex.Apply();
            System.IO.File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            Object.Destroy(outTex);
            Object.Destroy(tex);

            // 只报格式。graphicsFormat 是 R8G8B8A8_UNorm 说明这是**后处理之后**的那张
            // （HDR 中间缓冲是 RGBA17564/RGBAHalf），所以它是"吃到 ACES"的凭据。
            Write(s_tag + " rt " + rt.width + "x" + rt.height + " fmt=" + rt.graphicsFormat
                  + " -> " + outPath + "\n");
            return true;
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }
    }
}
#endif
