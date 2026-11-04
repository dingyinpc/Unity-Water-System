// R93Setup.cs — R93：云的"观感"三档（光学厚度 / 云底亮暗）
//
// 现象：Ocean 的云读成"灰色圆角方块"。形状问题要靠换噪声（工程量大），
//   但**"太实、太暗"**是观感上更直接的一刀：真实积云是透光的白团，
//   我们的云 40 步就吃到 78% 不透明度，看起来像纸板剪的。
//
// 可逆：90 回当前档，91/92 是两档"更薄更亮"。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R93Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Set(string prop, float v, string tag)
        {
            var sky = RenderSettings.skybox;
            if (sky == null || !sky.HasProperty(prop))
            {
                P($"R93/{tag} REFUSED: 没有 {prop}\n");
                return;
            }
            float before = sky.GetFloat(prop);
            sky.SetFloat(prop, v);
            EditorUtility.SetDirty(sky);
            if (Application.isPlaying) P($"R93/{tag} {prop} {before} -> {v}（运行时）\n");
            else { AssetDatabase.SaveAssetIfDirty(sky); P($"R93/{tag} {prop} {before} -> {v}（已存）\n"); }
        }

        static void SetColor(string prop, Color c, string tag)
        {
            var sky = RenderSettings.skybox;
            if (sky == null || !sky.HasProperty(prop)) { P($"R93/{tag} REFUSED: 没有 {prop}\n"); return; }
            Color before = sky.GetColor(prop);
            sky.SetColor(prop, c);
            EditorUtility.SetDirty(sky);
            if (Application.isPlaying) P($"R93/{tag} {prop} {before} -> {c}（运行时）\n");
            else { AssetDatabase.SaveAssetIfDirty(sky); P($"R93/{tag} {prop} {before} -> {c}（已存）\n"); }
        }

        [MenuItem("Tools/R93/90 云=当前档(厚/暗)")]
        public static void Cur()
        {
            Set("_CloudOptical", 1.0f, "90");
            SetColor("_CloudShadow", new Color(0.52f, 0.57f, 0.65f, 1f), "90");
            Set("_CloudBaseDark", 0.75f, "90");
        }

        [MenuItem("Tools/R93/91 云=薄一档(0.55)")]
        public static void Thin()
        {
            Set("_CloudOptical", 0.55f, "91");
            SetColor("_CloudShadow", new Color(0.66f, 0.70f, 0.76f, 1f), "91");
            Set("_CloudBaseDark", 0.55f, "91");
        }

        [MenuItem("Tools/R93/92 云=很薄(0.30)")]
        public static void Thinner()
        {
            Set("_CloudOptical", 0.30f, "92");
            SetColor("_CloudShadow", new Color(0.78f, 0.81f, 0.85f, 1f), "92");
            Set("_CloudBaseDark", 0.35f, "92");
        }




        [MenuItem("Tools/R94/96 云光学=0.80")]
        public static void Opt80() => Set("_CloudOptical", 0.80f, "R94-96");

        [MenuItem("Tools/R94/97 云光学=1.00")]
        public static void Opt100() => Set("_CloudOptical", 1.00f, "R94-97");

        [MenuItem("Tools/R94/98 云光学=1.40")]
        public static void Opt140() => Set("_CloudOptical", 1.40f, "R94-98");

        [MenuItem("Tools/R94/92 云量=0.30")]
        public static void Cover30() => Set("_CloudCover", 0.30f, "R94-92");

        [MenuItem("Tools/R94/93 云量=0.45")]
        public static void Cover45() => Set("_CloudCover", 0.45f, "R94-93");

        [MenuItem("Tools/R94/94 云量=0.60")]
        public static void Cover60() => Set("_CloudCover", 0.60f, "R94-94");

        [MenuItem("Tools/R94/95 云量=0.15(当前)")]
        public static void Cover15() => Set("_CloudCover", 0.15f, "R94-95");


        [MenuItem("Tools/R100/90 垂直变化=0(旧)")]
        public static void Vert0() => Set("_CloudVertWarp", 0f, "R100-90");

        [MenuItem("Tools/R100/91 垂直变化=0.0015")]
        public static void Vert15() => Set("_CloudVertWarp", 0.0015f, "R100-91");

        [MenuItem("Tools/R100/92 垂直变化=0.0030")]
        public static void Vert30() => Set("_CloudVertWarp", 0.0030f, "R100-92");

        [MenuItem("Tools/R100/93 垂直变化=0.0050")]
        public static void Vert50() => Set("_CloudVertWarp", 0.0050f, "R100-93");

        [MenuItem("Tools/R94/90 云噪声=value(旧)")]
        public static void NoiseValue() => Set("_CloudNoiseMode", 0f, "R94-90");

        [MenuItem("Tools/R94/91 云噪声=梯度Perlin")]
        public static void NoisePerlin() => Set("_CloudNoiseMode", 1f, "R94-91");

        [MenuItem("Tools/R93/93 探针(云光学/阴影色/底暗)")]
        public static void Probe()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) { P("R93 PROBE sky=null\n"); return; }
            float F(string p) => sky.HasProperty(p) ? sky.GetFloat(p) : -999f;
            Color C(string p) => sky.HasProperty(p) ? sky.GetColor(p) : Color.magenta;
            P($"R93 PROBE optical={F("_CloudOptical"):F2} shadow={C("_CloudShadow")} baseDark={F("_CloudBaseDark"):F2} " +
              $"scale={F("_CloudScale"):F5} steps={F("_CloudSteps"):F0}\n");
        }
    }
}
#endif
