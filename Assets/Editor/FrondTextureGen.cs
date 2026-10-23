// FrondTextureGen.cs — 程序化生成棕榈叶细节贴图（免费方案，不依赖任何外部资源）
//
// 背景：Islet_PalmLeaf / Islet_ScrubLeaf 的 _BaseMap 指向 **Islet_SolidWhite.png**（纯白），
//   也就是说棕榈完全没有贴图——叶子是"纯几何 + 纯色"，近看必然是一块平滑渐变。
//
// 本工具生成一张**灰阶细节图**（r=g=b∈[0.8,1.2], a=1）：
//   · 主脉：沿 U 方向的低频亮线
//   · 侧脉：斜向条纹
//   · 斑驳：两级 value noise（打破均匀）
// 关键：**不动 alpha、不动轮廓**，所以即使 UV 分布不理想，最坏情况也只是整体略微变深/变浅，
//   不会出现"叶片被切碎"的事故。可随时用菜单 206 还原纯白。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class FrondTextureGen
    {
        const int W = 512, H = 512;
        const string OutDir  = "Assets/Art/Textures/Islet";
        const string OutPath = OutDir + "/Islet_FrondDetail_Proc.png";

        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        [MenuItem("Tools/Style/205 生成叶脉细节贴图并应用")]
        public static void GenerateAndApply()
        {
            float ValueNoise(float x, float y)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                float xf = x - xi, yf = y - yi;
                float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
                float H2(int a, int b)
                {
                    float n = Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f;
                    return n - Mathf.Floor(n);
                }
                return Mathf.Lerp(Mathf.Lerp(H2(xi, yi), H2(xi + 1, yi), u),
                                  Mathf.Lerp(H2(xi, yi + 1), H2(xi + 1, yi + 1), u), v);
            }
            float Fbm(float x, float y)
            {
                float s = 0f, a = 0.5f;
                for (int i = 0; i < 3; i++) { s += a * ValueNoise(x, y); x *= 2.07f; y *= 2.03f; a *= 0.5f; }
                return s;
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (float)y / (H - 1);
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / (W - 1);

                    // 主脉：沿 u 的亮带（宽度 3%），让叶片有"中轴"
                    float rib = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.012f, 2f));

                    // 侧脉：斜向条纹（~34 条），用 sin 的高次幂做出"细线"
                    float vein = Mathf.Pow(Mathf.Abs(Mathf.Sin((u * 34f + v * 6f) * Mathf.PI)), 26f);

                    // 斑驳：两级噪声
                    float mott = Fbm(u * 9f, v * 9f) * 0.12f + Fbm(u * 31f, v * 31f) * 0.06f;

                    float d = 0.94f + mott + rib * 0.16f + vein * 0.10f;   // 围绕 1.0 波动
                    d = Mathf.Clamp(d, 0.78f, 1.18f);
                    byte c = (byte)Mathf.Clamp(Mathf.RoundToInt(d * 255f / 1.18f * 1.18f), 0, 255);
                    px[y * W + x] = new Color32(c, c, c, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Art/Textures/Islet"));
            File.WriteAllBytes(Path.Combine(Application.dataPath, "Art/Textures/Islet/Islet_FrondDetail_Proc.png"),
                               tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(OutPath);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.sRGBTexture = true;
            imp.alphaIsTransparency = false;
            imp.SaveAndReimport();

            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(OutPath);
            int n = 0;
            foreach (var m in FindLeafMats())
            {
                if (t != null) m.SetTexture("_BaseMap", t);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
                P($"    205: {m.name} _BaseMap -> {OutPath}\n");
            }
            P($"MENU205 生成并应用叶脉贴图: {OutPath}（{W}x{H}，改了 {n} 个材质）\n");
        }

        [MenuItem("Tools/Style/206 还原纯白贴图")]
        public static void RestoreWhite()
        {
            var white = AssetDatabase.LoadAssetAtPath<Texture2D>(OutDir + "/Islet_SolidWhite.png");
            if (white == null) { P("MENU206 REFUSED: 找不到 Islet_SolidWhite.png\n"); return; }
            int n = 0;
            foreach (var m in FindLeafMats())
            {
                m.SetTexture("_BaseMap", white);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"MENU206 已还原纯白贴图（{n} 个材质）\n");
        }

        static System.Collections.Generic.List<Material> FindLeafMats()
        {
            var list = new System.Collections.Generic.List<Material>();
            foreach (var m in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art/Materials" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(m));
                if (mat != null && mat.shader != null && mat.shader.name == "WaterProject/TreeLeaf"
                    && mat.name.StartsWith("Islet_"))
                    list.Add(mat);
            }
            return list;
        }
    }
}
#endif
