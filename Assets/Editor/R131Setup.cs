// R131Setup.cs — R131 潟湖碎闪"逗号阵"（摩尔纹）
//
// 根因：ShimmerN 两层噪声同向、只差 1.73 倍焦距 ⇒ 同向晶格叠出规则摩尔纹，
//       再被 pow(...,420) 的锐利高光"显影"成等距小逗号。
// 90  修复开（第二层旋转 37° + 低频斑块调制）
// 90b 旧路径（A/B）
// 91  只旋转 / 91b 只斑块（定位是谁在起作用）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R131Setup
    {
        const string Mat = "Assets/Art/Materials/Islet_LagoonWater.mat";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Set(float v, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R131 REFUSED: 无 Islet_LagoonWater.mat\n"); return; }
            if (!m.HasProperty("_GlitterFix")) { P("R131 REFUSED: 材质还没有 _GlitterFix\n"); return; }
            m.SetFloat("_GlitterFix", v);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            // 水面挂在 Renderer 上，不用像天空盒那样重赋值；这里保险起见也做一次 SetFloat 回读
            P($"R131 {tag}: _GlitterFix={m.GetFloat("_GlitterFix"):F1}\n");
        }

        [MenuItem("Tools/R131/90 碎闪=去摩尔纹(开)")]
        public static void On() => Set(1f, "90 开");

        [MenuItem("Tools/R131/90b 碎闪=旧路径(关)")]
        public static void Off() => Set(0f, "90b 关");

        [MenuItem("Tools/R131/92 探针: 碎闪参数")]
        public static void Probe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R131/92 无材质\n"); return; }
            P($"R131/92 fix={m.GetFloat("_GlitterFix"):F1} pow={m.GetFloat("_GlitterPow"):F0} "
              + $"str={m.GetFloat("_GlitterStrength"):F2} band={m.GetFloat("_GlitterBand"):F2}\n");
        }
    }
}
#endif
