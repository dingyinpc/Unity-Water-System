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

        // ═════════════════════════ 94~96 · A/B 验证（P0-1 接水面）═════════════════════════
        const string OceanMatPath = "Assets/Art/Materials/Ocean_Water.mat";

        static void SetFlowFoam(float v)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath);
            if (m == null) { WriteProbe("SWAB REFUSED: Ocean_Water.mat 不存在\n"); return; }
            m.SetFloat("_FlowFoamStrength", v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);   // 只存本材质（SaveAssets 会连带刷别的脏资产）
            WriteProbe($"SWAB _FlowFoamStrength -> {v}\n");
        }

        [MenuItem("Tools/Ocean/94 流场泡沫=0(关)")] public static void FlowFoamOff() => SetFlowFoam(0f);
        [MenuItem("Tools/Ocean/95 流场泡沫=1(强)")] public static void FlowFoamOn() => SetFlowFoam(1f);
        [MenuItem("Tools/Ocean/96 流场泡沫还原(0.35)")] public static void FlowFoamRestore() => SetFlowFoam(0.35f);

        // ═════════════════ 140~143 · 泡沫历史场（P1-2 / 参考 S10 C18–C20）═════════════════
        static void SetFoamHist(float v, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath);
            if (m == null) { WriteProbe("FOAMAB REFUSED: Ocean_Water.mat 不存在\n"); return; }
            m.SetFloat("_FoamHistStrength", v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);   // 只存本材质（SaveAssets 会连带刷别的脏资产）
            WriteProbe($"FOAMAB {tag} _FoamHistStrength -> {m.GetFloat("_FoamHistStrength")}\n");
        }

        [MenuItem("Tools/Ocean/140 泡沫历史=0(关)")]  public static void FoamHistOff() => SetFoamHist(0f, "off");
        [MenuItem("Tools/Ocean/141 泡沫历史=1(强)")]  public static void FoamHistOn()  => SetFoamHist(1f, "on");
        [MenuItem("Tools/Ocean/142 泡沫历史还原(0.6)")] public static void FoamHistRestore() => SetFoamHist(0.6f, "restore");

        [MenuItem("Tools/Ocean/143 导出泡沫历史调试图(写 PNG)")]
        public static void ExportFoamViz()
        {
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim == null) { WriteProbe("MENU143 REFUSED: 场景里没有 NearshoreFlowSim\n"); return; }
            sim.Build();
            sim.Warmup(600);                          // 让泡沫历史生成/平流/衰减跑起来
            ExportViz(sim, 5, "Docs/screenshots/foam_history.png");
            WriteProbe($"MENU143 ok: 泡沫历史调试图 (viz mode 5)\n");
        }

        [MenuItem("Tools/Ocean/144 泡沫历史再推进300步并导出(看平流/衰减)")]
        public static void AdvanceFoamViz()
        {
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim == null) { WriteProbe("MENU144 REFUSED: 场景里没有 NearshoreFlowSim\n"); return; }
            if (!sim.Ready) sim.Build();
            sim.Warmup(300);                          // 不重建：在既有历史上继续推进（证明是"历史"而非每帧重算）
            ExportViz(sim, 5, "Docs/screenshots/foam_history_t300.png");
            WriteProbe($"MENU144 ok: 泡沫历史再推进 300 步 -> foam_history_t300.png\n");
        }

        // ── SSR A/B（P0-2）：strength<0 = 不改强度；dbg<0 = 不改调试位 ──
        static void SetSSR(float strength, float dbg)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath);
            if (m == null) { WriteProbe("SSRAB REFUSED: Ocean_Water.mat 不存在\n"); return; }
            if (strength >= 0) m.SetFloat("_SSRStrength", strength);
            if (dbg >= 0) m.SetFloat("_SSRDebug", dbg);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);   // 只存本材质（SaveAssets 会连带刷别的脏资产）
            WriteProbe($"SSRAB _SSRStrength -> {m.GetFloat("_SSRStrength")}  _SSRDebug -> {m.GetFloat("_SSRDebug")}\n");
        }

        [MenuItem("Tools/Ocean/97 SSR=0(关)")]               public static void SSROff() => SetSSR(0f, 0f);
        [MenuItem("Tools/Ocean/98 SSR=1(开)")]               public static void SSROn() => SetSSR(1f, 0f);
        [MenuItem("Tools/Ocean/99 SSR调试:命中可信度")]        public static void SSRConf() => SetSSR(1f, 1f);
        [MenuItem("Tools/Ocean/100 SSR调试:SSR色")]           public static void SSRCol() => SetSSR(1f, 2f);
        [MenuItem("Tools/Ocean/101 SSR还原(0.85/调试关)")]     public static void SSRRestore() => SetSSR(0.85f, 0f);

        // ── SSR 机制验证（P0-2）：临时高大近岸几何，证明"命中→反射真实场景色"通路 ──
        static GameObject _probe;
        [MenuItem("Tools/Ocean/102 SSR 机制验证柱(临时建)")]
        public static void SSRProbeOn()
        {
            var cam = Camera.main;
            float x = cam ? cam.transform.position.x + 12f : -18f;
            // ⚠ 位置是**算出来的**：镜中像与水面视线的交点 = D·e/(e+h)（D=相机到物距离，e=眼高，h=物高于水面）。
            //   本机位沿用默认参数时 e≈4.5、相机在岸侧 26m ⇒ 交点要落到水上，物必须够远/够矮。
            //   放到离岸 ~40m（D≈66）后交点 ≈47m，正好在岸线外 ~21m 的水面上。
            float z = WaterOceanSetup.ShoreZAt(x) + 40f;
            SSRProbeOff();
            _probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _probe.name = "SSRProbePillar";
            _probe.transform.position = new Vector3(x, -0.4f, z);
            _probe.transform.localScale = new Vector3(2.2f, 6.0f, 2.2f);
            var mr = _probe.GetComponent<MeshRenderer>();
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = new Color(0.62f, 0.58f, 0.52f, 1f);      // 亮一点，倒影更可读
            mr.sharedMaterial = m;
            WriteProbe($"SSRPROBE on at ({x:F1},{z:F1}) 离岸40m\n");
        }
        [MenuItem("Tools/Ocean/103 SSR 机制验证柱(临时删)")]
        public static void SSRProbeOff()
        {
            var old = GameObject.Find("SSRProbePillar");
            if (old != null) Object.DestroyImmediate(old);
            WriteProbe("SSRPROBE off\n");
        }

        // ── SSR 参数粗调（P0-2 混叠诊断：步数太少会跳过细目标的屏幕区）──
        static void SetSSRParam(float steps, float maxDist, float thickness, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath);
            if (m == null) return;
            if (steps >= 0) m.SetFloat("_SSRSteps", steps);
            if (maxDist >= 0) m.SetFloat("_SSRMaxDist", maxDist);
            if (thickness >= 0) m.SetFloat("_SSRThickness", thickness);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);   // 只存本材质（SaveAssets 会连带刷别的脏资产）
            WriteProbe($"SSRPARAM {tag} -> steps={m.GetFloat("_SSRSteps")} dist={m.GetFloat("_SSRMaxDist")} thick={m.GetFloat("_SSRThickness")}\n");
        }
        [MenuItem("Tools/Ocean/106 SSR 密采(48步/200m/厚1.5)")] public static void SSRParamFine() => SetSSRParam(48f, 200f, 1.5f, "fine");
        [MenuItem("Tools/Ocean/107 SSR 还原(24步/90m/厚0.8)")] public static void SSRParamDefault() => SetSSRParam(24f, 90f, 0.8f, "default");

        // ── 决定性 A/B：冻结时间 + 停流场，消除动画相位干扰 ──
        [MenuItem("Tools/Ocean/108 冻结(暂停波浪+流场)")]
        public static void FreezeOn()
        {
            Time.timeScale = 0f;
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim != null) sim.enabled = false;
            WriteProbe("FREEZE on (timeScale=0, sim disabled)\n");
        }
        [MenuItem("Tools/Ocean/109 解冻(恢复波浪+流场)")]
        public static void FreezeOff()
        {
            Time.timeScale = 1f;
            var sim = Object.FindObjectOfType<NearshoreFlowSim>();
            if (sim != null) sim.enabled = true;
            WriteProbe("FREEZE off\n");
        }

        static Vector3 _camSaved;
        static bool _camSavedValid;
        [MenuItem("Tools/Ocean/104 SSR 机位升高(临时,视线向下)")]
        public static void CamUpOn()
        {
            var cam = Camera.main; if (cam == null) return;
            if (!_camSavedValid) { _camSaved = cam.transform.position; _camSavedValid = true; }
            // ⚠ 本机实测定标（《05》"先验证观测通道"）：Euler x 取**正**才是向下俯视
            //   （探针 fwd.y 才会变负）。负值是抬头，上一版因此拍了一整屏天空。
            cam.transform.position = _camSaved + Vector3.up * 9f;
            cam.transform.rotation = Quaternion.Euler(26f, cam.transform.eulerAngles.y, 0f);
            WriteProbe($"SSRCAM up pos=({cam.transform.position.x:F2},{cam.transform.position.y:F2},{cam.transform.position.z:F2}) "
                     + $"euler=({cam.transform.eulerAngles.x:F1},{cam.transform.eulerAngles.y:F1}) "
                     + $"fwd=({cam.transform.forward.x:F2},{cam.transform.forward.y:F2},{cam.transform.forward.z:F2})\n");
        }
        [MenuItem("Tools/Ocean/105 SSR 机位还原")]
        public static void CamUpOff()
        {
            var cam = Camera.main; if (cam == null) return;
            if (!_camSavedValid) return;
            cam.transform.position = _camSaved;
            cam.transform.rotation = Quaternion.Euler(WaterOceanSetup.CamEulerPublic);
            _camSavedValid = false;
            WriteProbe("SSRCAM restore\n");
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
