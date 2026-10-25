// R34ReflectSetup.cs —— Main 平面镜像反射兜底（R31 Main#1 反射碎片化的结构级解）
//
// 思路：SSR 只能映"屏幕里有的东西"，出屏即碎（R33 加密步数只缓解）。
// 潭（Pond）早就有整套平面镜像：镜像相机 + 斜投影裁剪 + _PlanarRef RT（视差正确）。
// 本轮把**同一个组件**（PondPlanarReflection，全场景通用）接到 Main：
//   SSR 命中 → 用 SSR（屏幕空间色最准）；SSR miss → 用平面镜像（连续完整的倒影）；
//   再 miss（镜像相机背后）→ 原天空渐变。
// 组件写 sharedMaterial 的 _WorldToReflClip/_PlanarRef —— Main 用 Water_Advanced.mat、
// 潭用 Water_Pond.mat，两个资产互不串扰；_PlanarStrength 默认 0 ⇒ 不装相机零回归。
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WaterProject.Presentation;

namespace WaterProject.EditorTools
{
    internal static class R34ReflectSetup
    {
        const int WaterLayer = 4;   // TagManager 现成 "Water" 层（潭同款，镜像相机按它自剔）

        [MenuItem("Tools/R34/90 Main 平面镜像兜底(装)")]
        static void Install()
        {
            // 按材质找水（GO 名字随菜单版本变过，材质是稳定锚点）
            MeshRenderer mr = null; var water = GameObject.Find("Water");
            if (water != null) mr = water.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null ||
                !mr.sharedMaterial.shader.name.Contains("Water_Advanced"))
            {
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Water_Advanced"))
                    { mr = r; break; }
            }
            if (mr == null) { Write("R34 REFUSED: 场景里找不到用 Water_Advanced 的水面（先开 Main 场景、退出 Play）\n"); return; }
            water = mr.gameObject;

            water.layer = WaterLayer;   // 镜像相机按这层把水皮剔出倒影（潭同款手法）

            var go = GameObject.Find("MainPlanarCam");
            if (go == null) go = new GameObject("MainPlanarCam");
            var cam = go.GetComponent<Camera>();
            if (cam == null) cam = go.AddComponent<Camera>();
            var comp = go.GetComponent<PondPlanarReflection>();
            if (comp == null) comp = go.AddComponent<PondPlanarReflection>();
            comp.waterHeight = 0.02f;
            comp.waterRenderer = mr;
            comp.resolutionScale = 0.5f;

            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m != null)
            {
                m.SetFloat("_PlanarStrength", 0.85f);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write($"R34 install: cam wired to {water.name}(layer{water.layer}), _PlanarStrength=0.85\n");
        }

        [MenuItem("Tools/R34/90b Main 平面镜像兜底(拆)")]
        static void Uninstall()
        {
            var go = GameObject.Find("MainPlanarCam");
            if (go != null) Object.DestroyImmediate(go);
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m != null && m.HasProperty("_PlanarStrength"))
            {
                m.SetFloat("_PlanarStrength", 0f);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R34 uninstall done（_PlanarStrength=0，shader 零回归）\n");
        }

        [MenuItem("Tools/R34/99 探针回读(只读)")]
        static void Probe()
        {
            var go = GameObject.Find("MainPlanarCam");
            var comp = go != null ? go.GetComponent<PondPlanarReflection>() : null;
            Write($"R34 PROBE cam={(go != null ? go.name : "<none>")} comp={(comp != null ? "ok" : "<none>")}\n");
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m != null)
                Write($"R34 PROBE planarStrength={(m.HasProperty("_PlanarStrength") ? m.GetFloat("_PlanarStrength").ToString() : "NO-PROP")} " +
                      $"ssr={m.GetFloat("_SSRSteps")}/{m.GetFloat("_SSRMaxDist")}/{m.GetFloat("_SSRThickness")}\n");
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
