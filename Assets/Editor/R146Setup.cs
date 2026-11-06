// R146Setup.cs — R146 Islet 云的"竖向拉丝"（柱状化）
//
// 根因：density = (cover - h) 本质是"每列一根云柱，柱高=cover"，垂直方向天然连贯，
//       掠射角看就是一道道竖向拉丝。修法：叠一层真正的三维噪声把柱子打碎。
// 90 破除=0.5 / 90b 破除=1.0 / 90c 关（旧）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R146Setup
    {
        const string Mat = "Assets/Art/Materials/Sky_IsletTropical.mat";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Set(string key, float v, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R146 REFUSED: 无 Sky_IsletTropical\n"); return; }
            if (!m.HasProperty(key)) { P($"R146 REFUSED: 材质没有 {key}\n"); return; }
            m.SetFloat(key, v);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            if (RenderSettings.skybox == m) { RenderSettings.skybox = null; RenderSettings.skybox = m; }
            P($"R146 {tag}: {key}={v:F2}\n");
        }

        [MenuItem("Tools/R146/90 柱状破除=0.5")]
        public static void B5() => Set("_CloudBreak", 0.5f, "90");

        [MenuItem("Tools/R146/90b 柱状破除=1.0")]
        public static void B10() => Set("_CloudBreak", 1f, "90b");

        [MenuItem("Tools/R146/90c 柱状破除=关(旧)")]
        public static void B0() => Set("_CloudBreak", 0f, "90c");

        [MenuItem("Tools/R146/91 探针")]
        public static void Probe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R146/91 无材质\n"); return; }
            P($"R146/91 break={m.GetFloat("_CloudBreak"):F2} steps={m.GetFloat("_Steps"):F0} "
              + $"cover={m.GetFloat("_CloudCover"):F2}\n");
        }
    }
}
#endif
