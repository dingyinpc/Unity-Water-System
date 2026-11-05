// R135Setup.cs — R135 天空模糊后处理的安装/强度/探针
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R135Setup
    {
        const string RendPath = "Assets/Settings/URP/WaterURPRenderer.asset";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static UnityEngine.Rendering.Universal.ScriptableRendererData Rend()
        {
            var r = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(RendPath);
            if (r == null) P("R135 REFUSED: 找不到 " + RendPath + "\n");
            return r;
        }

        [MenuItem("Tools/R135/90 天空模糊=安装")]
        public static void Install()
        {
            var rend = Rend(); if (rend == null) return;
            foreach (var f in rend.rendererFeatures)
                if (f is WaterProject.Render.SkyBlurFeature)
                { P("R135 already installed\n"); return; }

            var feat = ScriptableObject.CreateInstance<WaterProject.Render.SkyBlurFeature>();
            feat.name = "SkyBlurFeature";
            feat.strength = 0.85f;
            AssetDatabase.AddObjectToAsset(feat, rend);
            rend.rendererFeatures.Add(feat);
            EditorUtility.SetDirty(rend);
            AssetDatabase.SaveAssetIfDirty(rend);
            P($"R135 installed: features={rend.rendererFeatures.Count}\n");
        }

        [MenuItem("Tools/R135/90b 天空模糊=卸载")]
        public static void Uninstall()
        {
            var rend = Rend(); if (rend == null) return;
            int n = 0;
            for (int i = rend.rendererFeatures.Count - 1; i >= 0; i--)
                if (rend.rendererFeatures[i] is WaterProject.Render.SkyBlurFeature)
                {
                    var f = rend.rendererFeatures[i];
                    rend.rendererFeatures.RemoveAt(i);
                    AssetDatabase.RemoveObjectFromAsset(f);
                    Object.DestroyImmediate(f);
                    n++;
                }
            if (n > 0) { EditorUtility.SetDirty(rend); AssetDatabase.SaveAssetIfDirty(rend); }
            P($"R135 removed {n}\n");
        }

        [MenuItem("Tools/R135/91 天空模糊强度=0.5")]
        public static void S50() => SetStrength(0.5f);

        [MenuItem("Tools/R135/91b 天空模糊强度=0.85")]
        public static void S85() => SetStrength(0.85f);

        [MenuItem("Tools/R135/91c 天空模糊强度=1.0")]
        public static void S100() => SetStrength(1f);

        static void SetStrength(float s)
        {
            var rend = Rend(); if (rend == null) return;
            int n = 0;
            foreach (var f in rend.rendererFeatures)
                if (f is WaterProject.Render.SkyBlurFeature sb) { sb.strength = s; n++; }
            WaterProject.Render.SkyBlurFeature.StrengthOverride = s;   // 运行中立刻生效
            if (n > 0) { EditorUtility.SetDirty(rend); AssetDatabase.SaveAssetIfDirty(rend); }
            P($"R135 strength={s:F2}（命中 {n} 个特性，静态覆盖已设置）\n");
        }

        [MenuItem("Tools/R135/92 探针")]
        public static void Probe()
        {
            var rend = Rend(); if (rend == null) return;
            var sb = new System.Text.StringBuilder("R135/92 RendererFeatures:\n");
            foreach (var f in rend.rendererFeatures)
            {
                sb.Append("  ").Append(f != null ? f.name : "null");
                if (f is WaterProject.Render.SkyBlurFeature s) sb.Append($" strength={s.strength:F2}");
                sb.Append('\n');
            }
            P(sb.ToString());
        }
    }
}
#endif
