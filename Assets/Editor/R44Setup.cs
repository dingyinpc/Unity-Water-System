// R44Setup.cs —— Pond 背景水底档（R41 B3：水下地形不可见 + 背景死黑）
// A: Water_Pond _Opacity 0.82→0.62（R33 水草/河床透出来）
// B: 潭外围一圈低山（复用 R39_DistantIsle 网格 + URP/Lit 苔绿）打破死黑天际
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R44Setup
    {
        const string HillMat = "Assets/Art/Materials/R44_Hill.mat";

        [MenuItem("Tools/R44/90 Pond 背景水底档")]
        static void Tier()
        {
            var w = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Pond.mat");
            if (w != null)
            {
                w.SetFloat("_Opacity", 0.62f);   // 旧 0.82：河床几乎不透
                AssetDatabase.SaveAssetIfDirty(w);
            }

            // 旧物拆除（幂等）
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && go.name.StartsWith("R44_Hill_")) Object.DestroyImmediate(go);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(HillMat);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(sh) { name = "R44_Hill" };
                AssetDatabase.CreateAsset(mat, HillMat);
            }
            mat.SetColor("_BaseColor", new Color(0.16f, 0.24f, 0.16f));   // 苔绿（受光）

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/R39_DistantIsle.asset");
            if (mesh == null) { Write("R44 REFUSED: 找不到 R39_DistantIsle 网格\n"); return; }
            // 一圈 7 座，r≈48m，高矮错落；缺口对着主视线两角让天空透出来
            float[] yaw = { 15f, 65f, 110f, 165f, 215f, 262f, 310f };
            float[] sc  = { 11f, 8f, 13f, 9f, 12f, 8f, 10f };
            for (int i = 0; i < yaw.Length; i++)
            {
                var go = new GameObject("R44_Hill_" + i);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rr = go.AddComponent<MeshRenderer>(); rr.sharedMaterial = mat;
                go.transform.position = new Vector3(Mathf.Sin(yaw[i] * Mathf.Deg2Rad) * 48f, 0f,
                                                     Mathf.Cos(yaw[i] * Mathf.Deg2Rad) * 48f);
                go.transform.rotation = Quaternion.Euler(0, yaw[i] * 3.7f, 0);
                go.transform.localScale = new Vector3(sc[i], sc[i] * 0.55f, sc[i]);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R44 tier: opacity 0.62 + 7 hills\n");
        }

        [MenuItem("Tools/R44/90b Pond 还原(0.82+拆山)")]
        static void Revert()
        {
            var w = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Pond.mat");
            if (w != null) { w.SetFloat("_Opacity", 0.82f); AssetDatabase.SaveAssetIfDirty(w); }
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && go.name.StartsWith("R44_Hill_")) Object.DestroyImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R44 revert\n");
        }

        [MenuItem("Tools/R44/99 探针(只读)")]
        static void Probe()
        {
            var w = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Pond.mat");
            int n = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name.StartsWith("R44_Hill_")) n++;
            Write($"R44 PROBE opacity={(w != null ? w.GetFloat("_Opacity").ToString() : "?")} hills={n}\n");
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
