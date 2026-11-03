// R82Setup.cs — R82：质量档 MSAA（QualitySettings.antiAliasing）
//
// 现象：URP 资产里 `m_MSAA=4`，但 `ProjectSettings/QualitySettings.asset` 里各档
//   `antiAliasing: 0` —— 两边没同步（R36 曾声称"URP+质量档同步开 4×"，实际质量档是 0）。
// 画面表现：棕榈叶缘、石头轮廓有明显阶梯锯齿。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R82Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        [MenuItem("Tools/R82/90 质量档 MSAA=4x")]
        public static void Msaa4()
        {
            int before = QualitySettings.antiAliasing;
            QualitySettings.antiAliasing = 4;
            P($"R82/90 QualitySettings.antiAliasing {before} -> {QualitySettings.antiAliasing}（当前质量档 {QualitySettings.names[QualitySettings.GetQualityLevel()]}）\n");
        }

        [MenuItem("Tools/R82/91 质量档 MSAA=0")]
        public static void Msaa0()
        {
            int before = QualitySettings.antiAliasing;
            QualitySettings.antiAliasing = 0;
            P($"R82/91 QualitySettings.antiAliasing {before} -> 0\n");
        }

        [MenuItem("Tools/R82/99 探针(URP MSAA / 质量档 / 相机)")]
        public static void Probe()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            string urpMsaa = "-";
            if (rp != null)
            {
                var prop = rp.GetType().GetProperty("msaaSampleCount");
                if (prop != null) urpMsaa = prop.GetValue(rp).ToString();
            }
            P($"R82 PROBE urpMSAA={urpMsaa} qualityAA={QualitySettings.antiAliasing} " +
              $"level={QualitySettings.names[QualitySettings.GetQualityLevel()]}\n");
        }
    }
}
#endif
