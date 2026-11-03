// R63Setup.cs — R63：远景大气透视（雾密度）
//
// 现象：Ocean 远处 `OceanHeadland_far`（420m 外、475×47×450）在天空前读成一块**灰色平板**。
// 现实里这种距离的陆地会被空气散射冲淡、融进天空 —— 加大雾密度就是最省事、最物理的对策。
//
// 只改 RenderSettings.fogDensity（场景级），不做别的。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R63Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void SetFog(float d, string tag)
        {
            if (!EditorPrefs.HasKey("R63.fog")) EditorPrefs.SetFloat("R63.fog", RenderSettings.fogDensity);
            RenderSettings.fogDensity = d;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R63/{tag} fogDensity {EditorPrefs.GetFloat("R63.fog"):F5} -> {d:F5}\n");
        }

        [MenuItem("Tools/R63/90 雾密度=0.0035(远景更透)")]
        public static void Fog35() => SetFog(0.0035f, "90");

        [MenuItem("Tools/R63/91 雾密度=0.0050(更强)")]
        public static void Fog50() => SetFog(0.0050f, "91");

        [MenuItem("Tools/R63/92 雾密度还原")]
        public static void FogRestore()
        {
            SetFog(EditorPrefs.GetFloat("R63.fog", 0.0022f), "92");
            EditorPrefs.DeleteKey("R63.fog");
        }

        [MenuItem("Tools/R63/96 两个崖体材质还原到 Ocean_Cliff")]
        public static void RestoreBothCliffs()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_Cliff.mat");
            if (mat == null) { P("R63/96 REFUSED: 找不到 Ocean_Cliff.mat\n"); return; }
            foreach (var n in new[] { "OceanHeadland", "OceanHeadland_far" })
            {
                var go = GameObject.Find(n);
                var r = go != null ? go.GetComponent<Renderer>() : null;
                if (r == null) { P($"R63/96 跳过 {n}\n"); continue; }
                r.sharedMaterial = mat;
                EditorUtility.SetDirty(r);
                P($"R63/96 {n} -> Ocean_Cliff\n");
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
        static void ToggleRenderer(string name, bool on, string tag)
        {
            var go = GameObject.Find(name);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) { P($"R63/{tag} 找不到 {name}\n"); return; }
            r.enabled = on;
            EditorUtility.SetDirty(r);
            P($"R63/{tag} {name}.renderer = {on}\n");
        }

        [MenuItem("Tools/R63/97 隐藏 OceanBeach")]
        public static void HideBeach() => ToggleRenderer("OceanBeach", false, "97");

        [MenuItem("Tools/R63/97b 显示 OceanBeach")]
        public static void ShowBeach() => ToggleRenderer("OceanBeach", true, "97b");
        [MenuItem("Tools/R63/93 探针")]
        public static void Probe()
            => P($"R63 PROBE fog={RenderSettings.fog} density={RenderSettings.fogDensity:F5} color={RenderSettings.fogColor}\n");

        // ── 远景陆地：单独换一个"被大气冲淡"的材质 ──
        //   实测：雾密度调到 0.0035 画面逐项不变 ⇒ 这块料不吃雾（或 URP 侧没开）。
        //   那就直接给它的**材质**换成偏天空的浅灰蓝，视觉上等价于大气透视。
        const string FarKey = "R63.farMat";
        static string FarName = "";   // 空=OceanHeadland（近处崖体）

        [MenuItem("Tools/R63/94 远景陆地=大气灰蓝")]
        public static void FarHaze()
        {
            var go = GameObject.Find(string.IsNullOrEmpty(FarName) ? "OceanHeadland" : FarName);
            if (go == null) { P("R63/94 REFUSED: 找不到 OceanHeadland_far\n"); return; }
            var r = go.GetComponent<Renderer>();
            if (r == null) { P("R63/94 REFUSED: 没有 Renderer\n"); return; }

            if (!EditorPrefs.HasKey(FarKey) && r.sharedMaterial != null)
                EditorPrefs.SetString(FarKey, r.sharedMaterial.name);

            string p = "Assets/Art/Materials/Ocean_Cliff_Far.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(sh) { name = "Ocean_Cliff_Far" };
                m.SetColor("_BaseColor", new Color(0.52f, 0.58f, 0.66f));   // 偏天空的浅灰蓝
                m.SetFloat("_Smoothness", 0.05f);
                AssetDatabase.CreateAsset(m, p);
            }
            r.sharedMaterial = m;
            EditorUtility.SetDirty(r);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R63/94 OceanHeadland_far 材质 -> {m.name}（原 {EditorPrefs.GetString(FarKey, "?")}）\n");
        }

        [MenuItem("Tools/R63/94b 远景陆地材质还原")]
        public static void FarHazeRevert()
        {
            var go = GameObject.Find(string.IsNullOrEmpty(FarName) ? "OceanHeadland" : FarName);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) { P("R63/94b REFUSED\n"); return; }
            string old = EditorPrefs.GetString(FarKey, "");
            if (string.IsNullOrEmpty(old)) { P("R63/94b 无存档\n"); return; }
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art/Materials" }))
            {
                var mm = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (mm != null && mm.name == old) { r.sharedMaterial = mm; break; }
            }
            EditorUtility.SetDirty(r);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R63/94b 材质还原 -> {old}\n");
        }
    }
}
#endif
