// R105Setup.cs — R105：Pool 场景换天空盒（默认天空盒的下半球是深褐色，读作"没做完"）
//
// Pool 的 `RenderSettings.skybox` 一直是 Unity 自带的 `Default-Skybox`：
//   · 相机略俯视 ⇒ 画面上一大半是天空盒的**下半球**（深褐/土色）
//   · 没有云、没有渐变，和其他五个场景完全不是一套视觉
//
// 这里换成项目自己的 `Sky_OpenCoast`（有体积云/分层渐变），可逆。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R105Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void SetSky(string matName, string tag)
        {
            var prev = RenderSettings.skybox;
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/{matName}.mat");
            if (mat == null) { P($"R105/{tag} REFUSED: 找不到 {matName}\n"); return; }
            RenderSettings.skybox = mat;
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                DynamicGI.UpdateEnvironment();
            }
            P($"R105/{tag} 天空盒 {prev?.name ?? "null"} -> {mat.name}\n");
        }

        [MenuItem("Tools/R105/90 Pool 天空=项目天空(可逆)")]
        public static void PoolSky() => SetSky("Sky_OpenCoast", "90");

        // Pool 单独一份天空材质：复用 Sky_OpenCoast 的全部参数，只把**下半球**改成浅雾色。
        //   直接改共享材质会把 Ocean 的地平线也一起改掉，所以必须复制。
        [MenuItem("Tools/R105/90c Pool 天空=项目天空+浅雾下半球(推荐)")]
        public static void PoolSkyHaze()
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sky_OpenCoast.mat");
            if (src == null) { P("R105/90c REFUSED: 找不到 Sky_OpenCoast\n"); return; }

            string path = "Assets/Art/Materials/Pool_Sky.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(src) { name = "Pool_Sky" };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.CopyPropertiesFromMaterial(src);
            }
            mat.SetColor("_GroundColor", new Color(0.62f, 0.68f, 0.74f, 1f));
            mat.SetFloat("_CloudCover", 0.22f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);

            RenderSettings.skybox = mat;
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                DynamicGI.UpdateEnvironment();
            }
            P($"R105/90c 天空盒 -> {mat.name}（下半球 0.62/0.68/0.74）\n");
        }

        [MenuItem("Tools/R105/90b Pool 天空=Unity 默认")]
        public static void PoolSkyBack()
        {
            var def = Resources.GetBuiltinResource<Material>("Default-Skybox.mat");
            if (def == null) { P("R105/90b REFUSED: 拿不到 Default-Skybox\n"); return; }
            RenderSettings.skybox = def;
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                DynamicGI.UpdateEnvironment();
            }
            P("R105/90b 天空盒 -> Default-Skybox\n");
        }

        [MenuItem("Tools/R105/91 探针")]
        public static void Probe()
        {
            var sky = RenderSettings.skybox;
            P($"R105 PROBE skybox={(sky == null ? "null" : sky.name)} " +
              $"ambientMode={RenderSettings.ambientMode} ambientIntensity={RenderSettings.ambientIntensity:F2} " +
              $"scene={EditorSceneManager.GetActiveScene().name}\n");
        }
    }
}
#endif
