// NearshoreFlowSetup.cs — 近岸浅水流场（P0-1）的编辑器菜单：Tools/Ocean/90~92
//
// 纪律：
//   · 地形剖面**不重写岸线公式**——直接调用 WaterOceanSetup.CSandY/CShoreZ（同式 v2 的唯一来源），
//     避免 Main 线"半径公式在四个菜单各写一遍然后漂移"的老病（《03》/《05》）。
//   · 调试图导出（观测通道先于被观测量，《05》★G-15）。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using WaterProject.Logic;

namespace WaterProject.EditorTools
{
    public static class NearshoreFlowSetup
    {
        const string TexFolder = "Assets/Art/Textures";
        const string TerrainPath = "Assets/Art/Textures/OceanSW_Terrain.asset";
        const int N = 256;

        static void WriteProbe(string s)
        {
            File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

        // ═════════════════════════ 90 ═════════════════════════
        [MenuItem("Tools/Ocean/90 建近岸流模拟(烘焙地形+建组件+导出调试图)")]
        public static void BuildSim()
        {
            if (Application.isPlaying)
            { WriteProbe("MENU90 REFUSED: 在 Play 模式，先退出再建\n"); return; }

            // ── 域：以相机为参考，覆盖近岸带（海侧多、岸侧少）──
            var cam = Camera.main;
            float camX = cam ? cam.transform.position.x : -30f;
            var simGo = GameObject.Find("NearshoreFlowSim");
            if (simGo == null) simGo = new GameObject("NearshoreFlowSim");
            var sim = simGo.GetComponent<NearshoreFlowSim>() ?? simGo.AddComponent<NearshoreFlowSim>();

            float centerZ = WaterOceanSetup.ShoreZAt(camX);
            sim.minX = camX - 220f; sim.sizeX = 440f;
            sim.minZ = centerZ - 140f; sim.sizeZ = 170f;
            sim.gridN = N; sim.waterLevel = 0f;
            sim.simHz = 60;

            // ── 烘焙地形（采样真实岸线剖面 CShoreZ/CSandY，唯一来源）──
            var tex = BakeTerrain(sim.minX, sim.minZ, sim.sizeX, sim.sizeZ);
            EnsureFolder(TexFolder);
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(TerrainPath);
            if (old != null) AssetDatabase.DeleteAsset(TerrainPath);
            tex.name = "OceanSW_Terrain";
            AssetDatabase.CreateAsset(tex, TerrainPath);
            AssetDatabase.SaveAssets();
            sim.terrainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TerrainPath);
            sim.state0Tex = null;

            EditorUtility.SetDirty(sim);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(simGo.scene);

            // ── 建 + 导出两张调试图（对齐参考 f1700 / f1850）──
            sim.Build();
            ExportViz(sim, 1, "Docs/screenshots/_sw_viz_wetdry.png");
            ExportViz(sim, 2, "Docs/screenshots/_sw_viz_flow.png");

            WriteProbe($"MENU90 ok: domain({sim.minX:F1},{sim.minZ:F1}) size({sim.sizeX:F1},{sim.sizeZ:F1}) N={N} "
                     + $"centerZ={centerZ:F2} waterLevel={sim.waterLevel}\n");
        }

        // ═════════════════════════ 91 ═════════════════════════
        [MenuItem("Tools/Ocean/91 导出流场调试图(1干湿/2水位速度/3水深)")]
        public static void ExportVizMenu()
        {
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim == null) { WriteProbe("MENU91 REFUSED: 场景里没有 NearshoreFlowSim\n"); return; }
            sim.Build();
            ExportViz(sim, sim.vizMode, $"Docs/screenshots/_sw_viz_m{sim.vizMode}.png");
            WriteProbe($"MENU91 ok: viz mode {sim.vizMode}\n");
        }

        // ═════════════════════════ 92 ═════════════════════════
        [MenuItem("Tools/Ocean/92 切换流场可视化(1→2→3→1)")]
        public static void CycleViz()
        {
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim == null) { WriteProbe("MENU92 REFUSED: 场景里没有 NearshoreFlowSim\n"); return; }
            sim.vizMode = sim.vizMode >= 3 ? 1 : sim.vizMode + 1;
            EditorUtility.SetDirty(sim);
            WriteProbe($"MENU92 vizMode -> {sim.vizMode} (1干湿网格/2水位+速度/3水深)\n");
        }

        // ═════════════════════════ helpers ═════════════════════════
        static Texture2D BakeTerrain(float minX, float minZ, float sizeX, float sizeZ)
        {
            var px = new Color[N * N];
            float minT = float.MaxValue, maxT = float.MinValue;
            for (int j = 0; j < N; j++)
            {
                float z = minZ + (j + 0.5f) / N * sizeZ;
                for (int i = 0; i < N; i++)
                {
                    float x = minX + (i + 0.5f) / N * sizeX;
                    float off = z - WaterOceanSetup.ShoreZAt(x);     // 有符号离岸距离
                    float terr = WaterOceanSetup.SandYAt(x, off);    // 沙面=海床
                    px[j * N + i] = new Color(terr, 0, 0, 1);
                    if (terr < minT) minT = terr;
                    if (terr > maxT) maxT = terr;
                }
            }
            var t = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true) { name = "OceanSW_Terrain" };
            t.SetPixels(px); t.Apply();
            WriteProbe($"  terrain range [{minT:F2}, {maxT:F2}] m\n");
            return t;
        }

        static void ExportViz(NearshoreFlowSim sim, int mode, string relPath)
        {
            var rt = sim.RenderViz(mode);
            if (rt == null) { WriteProbe("  ExportViz: RenderViz 返回 null\n"); return; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            string abs = Path.Combine(Application.dataPath, "..", relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            WriteProbe($"  exported {relPath}\n");
        }

        static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                string leaf = Path.GetFileName(path);
                if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets", "Art");
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
#endif
