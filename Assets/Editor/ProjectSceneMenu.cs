// ProjectSceneMenu.cs — 场景切换 + 通用 SSR A/B（P0-2 收尾用）
//
// 为什么自己写：桥的 `openScene` 在本机两次让编辑器崩溃/退出（手动打开正常）。
// 现在 Unity 可由 `Tools/launch_unity.py` 自行拉起，所以这条路可以试；同时把所有
// SSR A/B 做成**按当前场景自动找水面材质**，不必为每个场景写一套菜单。
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class ProjectSceneMenu
    {
        static void Probe(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        // ═════════════════ 120~125 场景切换 ═════════════════
        static void Open(string relPath, string tag)
        {
            if (Application.isPlaying)
            { Probe($"MENU{tag} REFUSED: 在 Play 模式，先跑 Tools/Play/91\n"); return; }
            string full = "Assets/Scenes/" + relPath;
            if (!System.IO.File.Exists(Path.Combine(Application.dataPath, "Scenes", relPath)))
            { Probe($"MENU{tag} REFUSED: 找不到 {full}\n"); return; }
            var scene = EditorSceneManager.OpenScene(full, OpenSceneMode.Single);
            Probe($"MENU{tag} opened {full} active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n");
        }

        [MenuItem("Tools/Project/120 打开 Main（复刻视频主线）")] public static void OpenMain() => Open("Main.unity", "120");
        [MenuItem("Tools/Project/121 打开 OceanScene")]          public static void OpenOcean() => Open("OceanScene.unity", "121");
        [MenuItem("Tools/Project/122 打开 PondScene")]           public static void OpenPond() => Open("PondScene.unity", "122");
        [MenuItem("Tools/Project/123 打开 PoolScene")]           public static void OpenPool() => Open("PoolScene.unity", "123");
        [MenuItem("Tools/Project/124 打开 IsletScene")]          public static void OpenIslet() => Open("IsletScene.unity", "124");
        // R24 补缺：FFTScene 一直没有开场景菜单（R22 性能表缺席项）
        [MenuItem("Tools/Project/126 打开 FFTScene")]            public static void OpenFFT() => Open("FFTScene.unity", "126");

        // R24 补缺：198 构图档只改内存不落盘（原值存 EditorPrefs 可 199 还原），
        // 需要一个显式存场景的入口把构图固化进场景
        [MenuItem("Tools/Project/128 保存当前场景(显式)")]
        public static void SaveCurrent()
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(s.path)) { Probe("MENU128 REFUSED: 场景未保存过(无路径)\n"); return; }
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            Probe($"MENU128 saved {s.path}\n");
        }

        [MenuItem("Tools/Project/125 当前场景(写探针)")]
        public static void ShowCurrent()
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var waters = Object.FindObjectsOfType<Renderer>()
                .Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null
                         && r.sharedMaterial.shader.name.StartsWith("WaterProject/Water_"))
                .Select(r => r.sharedMaterial.shader.name + " @ " + r.gameObject.name)
                .Distinct().ToArray();
            Probe($"MENU125 当前场景={s.name} 水面渲染器={waters.Length} :: {string.Join(" | ", waters)}\n");
        }

        // ═════════════════ 130~133 通用 SSR A/B（按当前场景自动找水面材质）═════════════════
        static Material ResolveWaterMat()
        {
            var r = Object.FindObjectsOfType<Renderer>().FirstOrDefault(x =>
                x.sharedMaterial != null && x.sharedMaterial.shader != null &&
                x.sharedMaterial.shader.name.StartsWith("WaterProject/Water_"));
            return r != null ? r.sharedMaterial : null;
        }

        static void SetSSR(float strength, float dbg)
        {
            var m = ResolveWaterMat();
            if (m == null) { Probe("SSRAB(main) REFUSED: 当前场景找不到 WaterProject/Water_* 材质\n"); return; }
            if (strength >= 0f) m.SetFloat("_SSRStrength", strength);
            if (dbg >= 0f) m.SetFloat("_SSRDebug", dbg);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);   // ⚠ 只存这一个：SaveAssets() 会把别的脏资产一起刷（曾把昼夜色误写回）
            Probe($"SSRAB(main) {m.shader.name} -> strength={m.GetFloat("_SSRStrength")} debug={m.GetFloat("_SSRDebug")}\n");
        }

        [MenuItem("Tools/Project/130 SSR=0(关)")]            public static void SsrOff() => SetSSR(0f, 0f);
        [MenuItem("Tools/Project/131 SSR=1(开)")]            public static void SsrOn() => SetSSR(1f, 0f);
        [MenuItem("Tools/Project/132 SSR调试:命中可信度")]    public static void SsrConf() => SetSSR(1f, 1f);
        [MenuItem("Tools/Project/133 SSR调试:SSR色")]         public static void SsrCol() => SetSSR(1f, 2f);
        [MenuItem("Tools/Project/134 SSR还原(0.85/调试关)")]  public static void SsrReset() => SetSSR(0.85f, 0f);

        // ── 135/136 SSR 追踪参数粗调（Main 的池子浅、机位俯视，默认偏海面 → 命中稀疏）──
        static void SetSSRParam(float steps, float maxDist, float thickness, string tag)
        {
            var m = ResolveWaterMat();
            if (m == null) { Probe("SSRPARAM(main) REFUSED: 找不到水面材质\n"); return; }
            if (steps >= 0f) m.SetFloat("_SSRSteps", steps);
            if (maxDist >= 0f) m.SetFloat("_SSRMaxDist", maxDist);
            if (thickness >= 0f) m.SetFloat("_SSRThickness", thickness);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);   // ⚠ 只存这一个：SaveAssets() 会把别的脏资产一起刷（曾把昼夜色误写回）
            Probe($"SSRPARAM(main) {tag} -> steps={m.GetFloat("_SSRSteps")} dist={m.GetFloat("_SSRMaxDist")} thick={m.GetFloat("_SSRThickness")}\n");
        }

        [MenuItem("Tools/Project/135 SSR 密采(48步/120m/厚1.5)")] public static void SsrParamFine() => SetSSRParam(48f, 120f, 1.5f, "fine");
        [MenuItem("Tools/Project/136 SSR 默认(24步/40m/厚0.5)")]  public static void SsrParamDefault() => SetSSRParam(24f, 40f, 0.5f, "default");

        // ── 150~152 体散射 A/B（P2-1 / 参考 S6）：按当前场景自动找水面材质 ──
        static void SetScatter(float mode, string tag)
        {
            var m = ResolveWaterMat();
            if (m == null) { Probe("SCATAB REFUSED: 当前场景找不到 WaterProject/Water_* 材质\n"); return; }
            m.SetFloat("_ScatterMode", mode);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);   // ⚠ 只存这一个：SaveAssets() 会把别的脏资产一起刷（曾把昼夜色误写回）
            Probe($"SCATAB(main) {m.shader.name} _ScatterMode -> {m.GetFloat("_ScatterMode")} ({tag})\n");
        }

        [MenuItem("Tools/Project/150 体散射=关(0)")]        public static void ScatOff()   => SetScatter(0f, "off");
        [MenuItem("Tools/Project/151 体散射=解析(旧,1)")]    public static void ScatAnalytic() => SetScatter(1f, "analytic");
        [MenuItem("Tools/Project/152 体散射=RayMarch(2)")]   public static void ScatRM()    => SetScatter(2f, "raymarch");

        // ── 160~162 焦散 A/B（P2-2 / 参考 S8）：按当前场景自动找水面材质 ──
        static void SetCaustic(float mode, string tag)
        {
            var m = ResolveWaterMat();
            if (m == null) { Probe("CAUSAB REFUSED: 当前场景找不到 WaterProject/Water_* 材质\n"); return; }
            if (!m.HasProperty("_CausticMode")) { Probe($"CAUSAB REFUSED: {m.shader.name} 没有 _CausticMode\n"); return; }
            m.SetFloat("_CausticMode", mode);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            Probe($"CAUSAB(main) {m.shader.name} _CausticMode -> {m.GetFloat("_CausticMode")} ({tag})\n");
        }

        [MenuItem("Tools/Project/160 焦散=关(0)")]          public static void CausOff()      => SetCaustic(0f, "off");
        [MenuItem("Tools/Project/161 焦散=单层(旧,1)")]      public static void CausSingle()   => SetCaustic(1f, "single");
        [MenuItem("Tools/Project/162 焦散=多尺度+色散(2)")]  public static void CausMulti()    => SetCaustic(2f, "multi");

        // 焦散参数定稿档（细网为主，去平铺适度）——唯一写入点 = 材质
        static void SetCausticParams(float depthMix, float detile, float disperse, string tag)
        {
            var m = ResolveWaterMat();
            if (m == null || !m.HasProperty("_CausticDepthMix")) { Probe("CAUSP REFUSED\n"); return; }
            m.SetFloat("_CausticMode", 2f);            // ⚠ 必须一起置 2：否则只是改了参数、模式还停在上一次（本轮踩到：161→163 截图逐字节相同）
            m.SetFloat("_CausticDepthMix", depthMix);
            m.SetFloat("_CausticDetile", detile);
            m.SetFloat("_CausticDisperse", disperse);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            Probe($"CAUSP(main) depthMix={m.GetFloat("_CausticDepthMix")} detile={m.GetFloat("_CausticDetile")} disperse={m.GetFloat("_CausticDisperse")} ({tag})\n");
        }

        [MenuItem("Tools/Project/163 焦散=细网定稿(0.15/0.6/0.5)")] public static void CausFine() => SetCausticParams(0.15f, 0.6f, 0.5f, "fine");
        [MenuItem("Tools/Project/164 焦散=大斑档(0.7/1.0/0.6)")]    public static void CausCoarse() => SetCausticParams(0.7f, 1.0f, 0.6f, "coarse");
    }
}
#endif
