// SkyVolumetricSetup.cs — R3 体积云参数的唯一写入点
//
// 为什么需要：天空材质（Ocean_Sky / FFT_Sky / …）序列化着旧 2D 云时代的参数
// （_CloudCover=0.52 等），改 shader 默认值追不到已存在的材质；且 R3 云速要
// 按 R1 量测定标（风参数一处改、所有场景同步）。
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class SkyVolumetricSetup
    {
        // 定标值来源：R1 实测参考 S13 云速 0.84 %画宽/秒 ↔ 云层高度风速 ≈18 m/s
        public static class Preset
        {
            public const float Cover = 0.45f;
            public const float WindDir = 200f;
            public const float WindSpeed = 15f;
            public const float Shear = 0.006f;
            public const float Base = 900f;
            public const float Top = 2000f;
            public const float Steps = 20f;
        }

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

        [MenuItem("Tools/Sky/90 应用体积云参数(全部 Sky_Coast 材质)")]
        public static void Apply()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null || m.shader.name != "WaterProject/Sky_Coast") continue;
                m.SetFloat("_CloudCover", Preset.Cover);
                m.SetFloat("_CloudWindDir", Preset.WindDir);
                m.SetFloat("_CloudWindSpeed", Preset.WindSpeed);
                m.SetFloat("_CloudShear", Preset.Shear);
                m.SetFloat("_CloudBase", Preset.Base);
                m.SetFloat("_CloudTop", Preset.Top);
                m.SetFloat("_CloudSteps", Preset.Steps);
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            WriteProbe($"SKY90 applied to {n} materials: cover={Preset.Cover} wind={Preset.WindDir}deg/{Preset.WindSpeed}m/s shear={Preset.Shear}\n");
        }

        [MenuItem("Tools/Sky/91 风速调参(对全部材质, 速度从探针读)")]
        public static void SetSpeedFromProbe()
        {
            // 桥菜单无参——用 EditorPrefs 当参数通道：外部脚本先 EditorPrefs.SetFloat 再调本菜单
            float v = EditorPrefs.GetFloat("SkyWindSpeed", 18f);
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null || m.shader.name != "WaterProject/Sky_Coast") continue;
                m.SetFloat("_CloudWindSpeed", v);
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            WriteProbe($"SKY91 windSpeed -> {v} on {n} materials\n");
        }
        [MenuItem("Tools/Sky/92 天空状态(只写探针)")]
        public static void Status()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("SKY92 scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            var sky = RenderSettings.skybox;
            if (sky == null) sb.Append(" skybox=null(默认天空)");
            else
            {
                sb.Append(" skybox=" + sky.name + " shader=" + sky.shader.name);
                if (sky.HasProperty("_CloudWindSpeed"))
                    sb.Append($" wind={sky.GetFloat("_CloudWindSpeed")} cover={sky.GetFloat("_CloudCover")}");
            }
            var sun = RenderSettings.sun;
            sb.Append(" sun=" + (sun == null ? "null" : sun.transform.eulerAngles.ToString("F0")) + "\n");
            WriteProbe(sb.ToString());
        }
    }
}
#endif
