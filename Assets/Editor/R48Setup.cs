// R48Setup.cs — R48：沙洲贴图抗重复（Terrain 图层尺度）
//
// 现象（R47 俯视实拍 + 6× 对比度拉伸）：沙洲是一层**规则"鳞片"**，间距约 2.6 米。
// 根因：Islet 的沙层 terrainlayer `tileSize=2.6`，即**每 2.6 米重复一次**；
//   平视时被透视压缩看不出来，俯视一大片就是摩尔纹。
//
// 修法取舍：TerrainLayer 不支持旋转，所以只能**放大 tile**（重复周期变长、图案不再成"鳞"）。
//   代价是近看沙纹变粗——把 tile 提到 6.5~7.5 米（2.6 的 2.6~2.9 倍），
//   近景仍然有纹理，俯视的规则感基本消失。
//   原值存 EditorPrefs，90b 可完整还原。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R48Setup
    {
        const string LayerDir = "Assets/Art/TerrainLayers/Islet";
        const string K = "R48.TerrainTiles";

        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static readonly string[] Names = { "SandDry", "SandWet", "Rock", "Grass" };
        static readonly float[] NewTile = { 7.5f, 6.5f, 10.0f, 6.0f };

        [MenuItem("Tools/R48/90 沙洲贴图抗重复(tile x2.6)")]
        public static void BigTiles()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                string path = $"{LayerDir}/Islet_{Names[i]}.terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null) { P($"R48/90 SKIP 找不到 {path}\n"); continue; }

                if (!EditorPrefs.HasKey($"{K}.{Names[i]}"))
                {
                    EditorPrefs.SetFloat($"{K}.{Names[i]}.x", l.tileSize.x);
                    EditorPrefs.SetFloat($"{K}.{Names[i]}.y", l.tileSize.y);
                }
                l.tileSize = new Vector2(NewTile[i], NewTile[i]);
                EditorUtility.SetDirty(l);
                AssetDatabase.SaveAssetIfDirty(l);
                P($"R48/90 {Names[i]} tile -> {NewTile[i]}（原 {EditorPrefs.GetFloat($"{K}.{Names[i]}.x")}）\n");
            }
            EditorPrefs.SetBool(K + ".applied", true);
            P("R48/90 完成（需要场景重绘：Terrain 的 terrainLayers 引用没变，值会自动生效）\n");
        }

        [MenuItem("Tools/R48/90b 沙洲贴图还原")]
        public static void RestoreTiles()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                string path = $"{LayerDir}/Islet_{Names[i]}.terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null) continue;
                if (!EditorPrefs.HasKey($"{K}.{Names[i]}")) continue;
                l.tileSize = new Vector2(EditorPrefs.GetFloat($"{K}.{Names[i]}.x"),
                                         EditorPrefs.GetFloat($"{K}.{Names[i]}.y"));
                EditorUtility.SetDirty(l);
                AssetDatabase.SaveAssetIfDirty(l);
                P($"R48/90b {Names[i]} tile 还原 -> {l.tileSize.x}\n");
            }
        }

        // ═══════════ 沙贴图重生成（去掉低频"沙丘包"）═══════════
        // 真因：原沙贴图里有 `dune = Fbm(u*0.9)` —— 每张图只有 1 个低频大包。
        //   平铺后这个大包变成**规则网格**（俯视看就是"绗缝/鳞片"），
        //   放大 tile 只会把网格拉大，不会消失（R48 第一轮实测：moz 反而 +14%）。
        // 修法：把低频包降到 1/6 以下，保留高频颗粒与细沙纹（近景质感靠它们）。
        // 还原：`git checkout -- Assets/Art/Textures/Islet/Islet_SandDry.png Islet_SandWet.png`
        [MenuItem("Tools/R48/91 重生成沙贴图(去低频包)")]
        public static void RegenSand()
        {
            Regen($"{TexDir}/Islet_SandDry.png", true);
            Regen($"{TexDir}/Islet_SandWet.png", false);
            P("R48/91 沙贴图已重生成（低频包 0.05→0.008 / 0.10→0.015）\n");
        }

        const string TexDir = "Assets/Art/Textures/Islet";

        static float Hash(float x, float y)
        {
            float n = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return n - Mathf.Floor(n);
        }

        static float VNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), u),
                              Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), u), v);
        }

        static float Fbm(float x, float y, int oct)
        {
            float s = 0f, a = 0.5f;
            for (int i = 0; i < oct; i++) { s += a * VNoise(x, y); x *= 2.07f; y *= 2.03f; a *= 0.5f; }
            return s / (1f - Mathf.Pow(0.5f, oct)) * 0.5f;   // 归一到 ~0..1
        }

        static void Regen(string path, bool dry)
        {
            const int S = 512;
            const float TAU = 6.2831853f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                float b = TAU * y / S;
                float p = Mathf.Cos(b) + 2f, q = Mathf.Sin(b) + 2f;
                for (int x = 0; x < S; x++)
                {
                    float a = TAU * x / S;
                    float u = Mathf.Cos(a) + 2f + p, v = Mathf.Sin(a) + 2f + q;
                    Color c;
                    if (dry)
                    {
                        float dune  = Fbm(u * 0.9f, v * 0.9f, 3);
                        float grain = Fbm(u * 9.0f, v * 9.0f, 2);
                        float rip   = 0.5f + 0.5f * Mathf.Sin((u * 7.0f + v * 2.0f) * 3.0f + dune * 4f);
                        float k = 0.480f + 0.008f * dune + 0.055f * (grain - 0.5f) + 0.045f * rip;
                        c = new Color(k, k * 0.965f, k * 0.855f);
                    }
                    else
                    {
                        float dune = Fbm(u * 1.1f + 5f, v * 1.1f - 3f, 3);
                        float film = Fbm(u * 5.5f - 2f, v * 5.5f + 8f, 2);
                        float k = 0.355f + 0.015f * dune + 0.06f * film;
                        c = new Color(k * 0.94f, k * 0.93f, k * 0.88f);
                    }
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply(false);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            P($"     R48/91 写入 {path}\n");
        }

        [MenuItem("Tools/R48/93 探针(图层 tile / 礁石珊瑚)")]
        public static void Probe()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerDir}/Islet_{Names[i]}.terrainlayer");
                P($"R48 PROBE layer {Names[i]} tile={(l != null ? l.tileSize.ToString() : "-")}\n");
            }
            int rocks = 0, corals = 0, coralLobes = 0;
            foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (g.name.StartsWith("R47_Rock_")) rocks++;
                else if (g.name.StartsWith("R47_Coral_")) corals++;
            }
            P($"R48 PROBE content rocks={rocks} corals={corals} lobes={coralLobes}\n");
        }
    }
}
#endif
