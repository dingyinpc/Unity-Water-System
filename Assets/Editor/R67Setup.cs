// R67Setup.cs — R67：Main 的"绿色墙"= 地面平面太短
//
// 现象：Main 画面上方是一整面平绿色，顶部一条**硬地平线**。
// 诊断（R63/93 探针）：雾是开的、密度 0.0045（很强）、雾色是天空蓝；
//   而 `Ground` 平面只有 **1100×1100**（半径 550m）——远边缘落在"雾还没吃透"的距离上，
//   所以既没被雾化成天空色，又在 550m 处**直接断掉**，于是读成一堵绿墙。
//
// 修法：把地面放大到 4400×4400（半径 2200m）。雾在 1000m 处已经 exp(-4.5)≈1%，
//   地平线会被雾彻底化进天空，不再有硬边。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R67Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Toggle(string name, bool on, string tag)
        {
            var go = GameObject.Find(name);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) { P($"R67/{tag} 找不到 {name}\n"); return; }
            r.enabled = on;
            EditorUtility.SetDirty(r);
            P($"R67/{tag} {name}.renderer = {on}\n");
        }

        [MenuItem("Tools/R67/91 隐藏 GreenWall")]
        public static void HideWall() => Toggle("GreenWall", false, "91");

        [MenuItem("Tools/R67/91b 显示 GreenWall")]
        public static void ShowWall() => Toggle("GreenWall", true, "91b");
        [MenuItem("Tools/R67/90 Main 地面放大 ×4(消除硬地平线)")]
        public static void ScaleGround()
        {
            var go = GameObject.Find("Ground");
            if (go == null) { P("R67/90 REFUSED: 找不到 Ground\n"); return; }

            if (!EditorPrefs.HasKey("R67.sx"))
            {
                EditorPrefs.SetFloat("R67.sx", go.transform.localScale.x);
                EditorPrefs.SetFloat("R67.sy", go.transform.localScale.y);
                EditorPrefs.SetFloat("R67.sz", go.transform.localScale.z);
            }

            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(s.x * 4f, s.y, s.z * 4f);
            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R67/90 Ground scale {s} -> {go.transform.localScale}\n");
        }

        [MenuItem("Tools/R67/90b Main 地面还原")]
        public static void ScaleGroundRevert()
        {
            var go = GameObject.Find("Ground");
            if (go == null) { P("R67/90b REFUSED\n"); return; }
            if (!EditorPrefs.HasKey("R67.sx")) { P("R67/90b 无存档\n"); return; }
            go.transform.localScale = new Vector3(EditorPrefs.GetFloat("R67.sx", 1f),
                                                  EditorPrefs.GetFloat("R67.sy", 1f),
                                                  EditorPrefs.GetFloat("R67.sz", 1f));
            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R67/90b Ground scale 还原 -> {go.transform.localScale}\n");
        }
    }
}
#endif
