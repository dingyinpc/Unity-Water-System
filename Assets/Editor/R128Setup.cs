// R128Setup.cs — R128 云的花椰菜结构（Worley billow），保留动态
//
// 90  花椰菜噪声 关（旧软团块）
// 90b 花椰菜噪声 开（Worley）
// 91  尺度扫描 / 92 强度扫描 / 93 探针
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R128Setup
    {
        static readonly string[] SkyMats =
        {
            "Assets/Art/Materials/Sky_OpenCoast.mat",
            "Assets/Art/Materials/FFT/FFT_Sky.mat",
            "Assets/Art/Materials/Pool_Sky.mat",
        };

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Set(string key, float v, string tag)
        {
            int n = 0;
            foreach (var path in SkyMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                m.SetFloat(key, v);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                if (RenderSettings.skybox == m) { RenderSettings.skybox = null; RenderSettings.skybox = m; }
                n++;
            }
            P($"R128 {tag}: {key}={v:F2} 写入 {n} 张材质\n");
        }

        [MenuItem("Tools/R128/90 花椰菜=关(旧软团块)")]
        public static void BillowOff() => Set("_CloudBillow", 0f, "90 关");

        [MenuItem("Tools/R128/90b 花椰菜=开(Worley)")]
        public static void BillowOn() => Set("_CloudBillow", 1f, "90b 开");

        [MenuItem("Tools/R128/90c 花椰菜=半")]
        public static void BillowHalf() => Set("_CloudBillow", 0.5f, "90c 半");

        [MenuItem("Tools/R128/91 花椰菜尺度=3.0")]
        public static void Scale3() => Set("_BillowScale", 3f, "91 尺度3.0");

        [MenuItem("Tools/R128/91b 花椰菜尺度=5.7(默认)")]
        public static void Scale57() => Set("_BillowScale", 5.7f, "91b 尺度5.7");

        [MenuItem("Tools/R128/91c 花椰菜尺度=9.0")]
        public static void Scale9() => Set("_BillowScale", 9f, "91c 尺度9.0");

        [MenuItem("Tools/R128/92 花椰菜强度=0.6")]
        public static void Amt06() => Set("_BillowAmt", 0.6f, "92 强度0.6");

        [MenuItem("Tools/R128/92b 花椰菜强度=1.0(默认)")]
        public static void Amt10() => Set("_BillowAmt", 1f, "92b 强度1.0");

        [MenuItem("Tools/R128/92c 花椰菜强度=1.5")]
        public static void Amt15() => Set("_BillowAmt", 1.5f, "92c 强度1.5");

        [MenuItem("Tools/R128/93 探针: 云参数")]
        public static void Probe()
        {
            var sb = new System.Text.StringBuilder("R128/93 云参数\n");
            foreach (var path in SkyMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                sb.Append($"  {System.IO.Path.GetFileName(path)}: billow={m.GetFloat("_CloudBillow"):F2}")
                  .Append($" scale={m.GetFloat("_BillowScale"):F1}")
                  .Append($" amt={m.GetFloat("_BillowAmt"):F2}")
                  .Append($" cover={m.GetFloat("_CloudCover"):F2}")
                  .Append($" steps={m.GetFloat("_CloudSteps"):F0}\n");
            }
            P(sb.ToString());
        }
    }
}
#endif
