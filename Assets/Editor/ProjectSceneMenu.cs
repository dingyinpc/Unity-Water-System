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
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
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
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
            Probe($"SSRPARAM(main) {tag} -> steps={m.GetFloat("_SSRSteps")} dist={m.GetFloat("_SSRMaxDist")} thick={m.GetFloat("_SSRThickness")}\n");
        }

        [MenuItem("Tools/Project/135 SSR 密采(48步/120m/厚1.5)")] public static void SsrParamFine() => SetSSRParam(48f, 120f, 1.5f, "fine");
        [MenuItem("Tools/Project/136 SSR 默认(24步/40m/厚0.5)")]  public static void SsrParamDefault() => SetSSRParam(24f, 40f, 0.5f, "default");
    }
}
#endif
