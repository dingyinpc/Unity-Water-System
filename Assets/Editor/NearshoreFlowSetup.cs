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

            // ── 域：以海岸机位常量为参考（不依赖当前打开哪个场景），覆盖近岸带 ──
            float camX = WaterOceanSetup.CamXPublic;
            var simGo = GameObject.Find("NearshoreFlowSim");
            if (simGo == null) simGo = new GameObject("NearshoreFlowSim");
            var sim = simGo.GetComponent<NearshoreFlowSim>() ?? simGo.AddComponent<NearshoreFlowSim>();

            // 岸线在 z = ShoreZ(x)；off>0 = 海侧（z 更大）。相机在岸侧 26m。
            // 域取"岸侧 40m → 海侧 160m"，正好覆盖机位与可见近岸带。
            float shoreZ = WaterOceanSetup.ShoreZAt(camX);
            sim.minX = camX - 220f; sim.sizeX = 440f;
            sim.minZ = shoreZ - 40f; sim.sizeZ = 200f;
            sim.gridN = N; sim.waterLevel = 0f;
            sim.simHz = 60;
            DomainMinX = sim.minX; DomainMinZ = sim.minZ;
            DomainSizeX = sim.sizeX; DomainSizeZ = sim.sizeZ;

            // ── 烘焙地形 + 初始状态（采样真实岸线剖面 CShoreZ/CSandY，唯一来源）──
            EnsureFolder(TexFolder);
            BakeBoth(sim.waterLevel, sim.tideSlope, out var tex, out var st0);
            SaveTexAsset(tex, TerrainPath, "OceanSW_Terrain");
            SaveTexAsset(st0, StatePath, "OceanSW_State0");
            sim.terrainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TerrainPath);
            sim.state0Tex  = AssetDatabase.LoadAssetAtPath<Texture2D>(StatePath);

            EditorUtility.SetDirty(sim);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(simGo.scene);

            // ── 建 + 预热 + 导出调试图（对齐参考 f1700 / f1850）──
            sim.Build();
            ExportViz(sim, 1, "Docs/screenshots/_sw_viz_wetdry.png");
            sim.Warmup(200);
            ExportViz(sim, 2, "Docs/screenshots/_sw_viz_flow_t1.png");
            sim.Warmup(200);
            ExportViz(sim, 2, "Docs/screenshots/_sw_viz_flow_t2.png");
            sim.Warmup(200);
            ExportViz(sim, 2, "Docs/screenshots/_sw_viz_flow.png");     // t3(600步)
            ExportViz(sim, 4, "Docs/screenshots/_sw_viz_speed.png");

            WriteProbe($"MENU90 ok: domain({sim.minX:F1},{sim.minZ:F1}) size({sim.sizeX:F1},{sim.sizeZ:F1}) N={N} "
                     + $"shoreZ={shoreZ:F2} waterLevel={sim.waterLevel}\n");
        }

        // ═════════════════════════ 91 ═════════════════════════
        [MenuItem("Tools/Ocean/91 导出流场调试图(1干湿/2水位速度/3水深)")]
        public static void ExportVizMenu()
        {
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim == null) { WriteProbe("MENU91 REFUSED: 场景里没有 NearshoreFlowSim\n"); return; }
            sim.Build();
            sim.Warmup(sim.vizMode >= 3 ? 0 : 600);   // 干湿/水位模式先预热出流场
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

        // ═════════════════════════ 93 ═════════════════════════
        [MenuItem("Tools/Ocean/93 移除近岸流模拟对象(清理)")]
        public static void RemoveSim()
        {
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim == null) { WriteProbe("MENU93 noop: 场景里没有 NearshoreFlowSim\n"); return; }
            Object.DestroyImmediate(sim.gameObject);
            WriteProbe("MENU93 removed NearshoreFlowSim\n");
        }

        // ═════════════════════════ helpers ═════════════════════════
        const string StatePath = "Assets/Art/Textures/OceanSW_State0.asset";

        // 一次遍历同时产出：地形（R=terrain）与初始状态（R=h G=terrain B=wet A=0）。
        // ⚠ 状态贴图存成 .asset 后 isReadable=false，所以运行时**不能**用 GetPixelData 现算，
        //   必须在 Editor 端烘好、由 Graphics.Blit（纯 GPU 拷贝，不需要可读）上传。
        static void BakeBoth(float waterLevel, float tideSlope, out Texture2D terrain, out Texture2D state0)
        {
            var tp = new Color[N * N];
            var sp = new Color[N * N];
            float minT = float.MaxValue, maxT = float.MinValue;
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int j = 0; j < N; j++)
            {
                float z = DomainMinZ + (j + 0.5f) / N * DomainSizeZ;
                for (int i = 0; i < N; i++)
                {
                    float x = DomainMinX + (i + 0.5f) / N * DomainSizeX;
                    float off = z - WaterOceanSetup.ShoreZAt(x);     // 有符号离岸距离（>0 海侧）
                    float terr = WaterOceanSetup.SandYAt(x, off);    // 沙面=海床
                    // 目标水位 = 静水位 + 潮差倾斜（离岸越高）→ 形成向岸的水位梯度（参考 C15）
                    float wTarget = waterLevel + tideSlope * off;
                    float h = Mathf.Max(wTarget - terr, 0f);
                    tp[j * N + i] = new Color(terr, off, 0, 1);   // R=地形y  G=有符号离岸距离(>0 海侧)
                    sp[j * N + i] = new Color(h, terr, h > 1e-4f ? 1f : 0f, 0f);
                    if (terr < minT) minT = terr; if (terr > maxT) maxT = terr;
                    if (h < minH) minH = h; if (h > maxH) maxH = h;
                }
            }
            terrain = MakeTex(tp, "OceanSW_Terrain");
            state0  = MakeTex(sp, "OceanSW_State0");
            WriteProbe($"  terrain [{minT:F2}, {maxT:F2}] m ; h [{minH:F2}, {maxH:F2}] m\n");
        }

        static Texture2D MakeTex(Color[] px, string nm)
        {
            var t = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true) { name = nm };
            t.SetPixels(px); t.Apply();
            return t;
        }

        static void SaveTexAsset(Texture2D t, string path, string nm)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) AssetDatabase.DeleteAsset(path);
            t.name = nm;
            AssetDatabase.CreateAsset(t, path);
            AssetDatabase.SaveAssets();
        }

        // 域参数（menu 90 写入组件前先生效，供 BakeBoth 用）
        static float DomainMinX, DomainMinZ, DomainSizeX, DomainSizeZ;

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
