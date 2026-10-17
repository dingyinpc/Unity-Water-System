// WaterOceanSetup.cs — 海岸演示场景（菜单 80~86）
//
// 第三条独立水线。前两条各自回答了一个问题：
//   Main / Water_Advanced —— "视频里那种池子怎么亮"
//   PondScene / Water_Pond —— "静水怎么把岸上的东西倒进来"
// 这一条回答的是：**水面一大、一有浪、一有潮汐，会发生什么**。
// 具体就是三件前两条线里根本不存在的事：浅水变形、碎浪、会呼吸的湿沙边界。
//
// 隔离与命名（沿用《05》E-7 与素材库里那条 ⚠ 命名规范）：
//   资产前缀一律 **Ocean***。潭线用 Pool*（网格）/Pond_*（材质），Main 线用 Pond*（网格）。
//   三条线共用同一个 Assets/ 目录，前缀撞了就会互相覆盖网格，而且两边都"能渲染"，
//   只在切回另一个场景时表现为凭空多一层 —— 这种错查起来极贵，所以从第一行就分开。
//
// 岸线只写一处：C# 的 CShoreZ/CSandY 生成网格，HLSL 的 ShoreZ/SandY 做着色，
// 两者必须同式。两份式子由菜单80 的 SELF-CHECK 行对账（把 C# 版在固定采样点的值
// 写进探针，改任何系数都要回来核这几行）。标记：同式 v1。
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class WaterOceanSetup
    {
        const string ScenePath  = "Assets/Scenes/OceanScene.unity";
        const string MeshFolder = "Assets/Art/Meshes";
        const string MatFolder  = "Assets/Art/Materials";

        // ───────── 岸线与剖面（同式 v1，另一份在 OceanShore.hlsl）─────────
        // 岸线是 z = f(x) 的单值函数，所以不能有把海遮在后面的岬角。
        // 三个谐波的 |dZ/dx| 上限 = Σ A·B = 0.0733+0.0750+0.0711 = 0.220
        // 压在 0.25 以内 ⇒ "沿 z 量的离岸距离"与真正的垂直距离误差 < 3%。
        static readonly Vector4 Shore  = new Vector4(-400f, 0f, 0f, 0f);
        static readonly Vector4 ShoreA = new Vector4(22f, 9f, 3.2f, 0f);
        static readonly Vector4 ShoreB = new Vector4(1f / 300f, 1f / 120f, 1f / 45f, 0f);

        const float ShoreSlope = 0.055f;   // 每离岸 1m 加深 5.5cm
        const float BeachRise  = 0.055f;   // 每往岸上 1m 抬 5.5cm
        const float BeachCurve = 0.0022f;  // 越往里越陡（沙丘脚）

        const float SwashReach  = 9.5f;    // 水膜平均能冲上多远
        const float SwashSpeed  = 0.13f;   // 呼吸快慢
        const float SwashFade   = 3.0f;    // 边界退透距离

        // 浪**传播**的方向（不是"从哪吹来"）：朝岸 = -z，带一点沿岸分量
        static readonly Vector4 WaveDir = new Vector4(-0.175f, 0f, -0.985f, 0f);

        // ───────── 大气 ─────────
        // ⚠ 画幅里可见的那一段天空（0°~21° 仰角）几乎就是 _HorizonColor 本身，
        //   天顶色要 40° 以上才明显介入。所以"天空太暗"要动的**主要是地平色**。
        //   而 ACES + 曝光 0.85 吃线性值很狠：线性 0.52 进去只出 124（实测），
        //   想拿到 190+ 必须把线性值推到接近白，不能靠微调。
        static readonly Color SkyZenith  = new Color(0.220f, 0.440f, 0.880f);
        // 地平色 == 雾色（唯一来源）：它一变就是三处一起变 —— 天空下缘、远景雾、
        // 水面反射的兜底色。提亮它时必须同步下调 _SkyReflect，否则海会跟着泛白。
        static readonly Color SkyHorizon = new Color(0.700f, 0.800f, 0.910f);
        // 环境光是 Skybox 模式 ⇒ 这一档就是"沙面向上的反弹光"，给暖沙色而不是中性灰
        static readonly Color SkyGround  = new Color(0.300f, 0.265f, 0.205f);
        const float FogDensity = 0.0022f;   // ExponentialSquared

        // ───────── 网格尺度 ─────────
        const float Xmin = -420f, Xmax = 420f;
        const int   COLS = 256;              // 沿岸 3.28m 一档
        const float UpBack = 20f;            // 水膜往岸上铺 20m（沫带要能爬上去）
        const float SeaOut = 900f;           // 离岸 900m：雾 98%，地平线自然收掉
        const int   OCEAN_ROWS = 168;
        const float BeachBack = 70f, BeachFront = 16f;
        const int   BEACH_ROWS = 90;

        // ───────── 机位 ─────────
        const float CamX = -30f;
        // 26m 而不是 15m：15m 处滩面只抬到 1.3m，相机眼高 2.94m，而三条浪叠加振幅
        // 就有 1.9m、再乘浅水放大能到 4m ⇒ 相机直接泡在浪头里，画面左 60% 全是水。
        // 26m 是正常"滩肩"的高度（沙面已高于水位 2.7m），站那儿既干又能俯瞰整条水线。
        const float CamOff = -26f;
        const float EyeH = 1.62f;
        static readonly Vector3 CamEuler = new Vector3(-4.5f, 58f, 0f);

        // ───────── 对外访问器（唯一来源：近岸流模拟等模块引用这两支）─────────
        public static float ShoreZAt(float x) => CShoreZ(x);
        public static float SandYAt(float x, float off) => CSandY(x, off);
        // 机位常量（近岸流模拟定域中心用，避免依赖"当前打开的是哪个场景"）
        public static float CamXPublic => CamX;
        public static float CamOffPublic => CamOff;
        public static float EyeHPublic => EyeH;
        public static Vector3 CamEulerPublic => CamEuler;

        static float CShoreZ(float x)
        {
            float u = x - Shore.x;
            return Shore.y
                 + ShoreA.x * Mathf.Sin(u * ShoreB.x)
                 + ShoreA.y * Mathf.Sin(u * ShoreB.y + 1.7f)
                 + ShoreA.z * Mathf.Sin(u * ShoreB.z + 0.4f);
        }

        // 沙面（也是海床）。⚠ 沿岸起伏必须并进来，不能只加在建网格的代码里：
        // 贴沙的那层水膜用的是这个解析值，两边一旦分家，水膜就浮在沙上或陷进沙里。
        // 同式 v2，另一份在 OceanShore.hlsl。
        static float CSandY(float x, float off)
        {
            float baseY = (off >= 0f)
                ? -(off * ShoreSlope)
                : (-off) * BeachRise + (-off) * (-off) * BeachCurve;
            return baseY + SandRipple(x, off);
        }

        // 沿岸起伏。湿区只给极小量（k=0.06 ⇒ 最大 5cm），越往里越大，读成沙丘脚。
        static float SandRipple(float x, float off)
        {
            float up = Mathf.Max(-off, 0f);
            float k = Mathf.Clamp01((up - 12f) / 18f);
            k = 0.06f + 0.94f * k;
            return k * (0.55f * Mathf.Sin(x * 0.013f + 2.1f)
                      + 0.16f * Mathf.Sin(x * 0.061f - 0.7f) * Mathf.Sin(up * 0.19f)
                      + 0.10f * Mathf.Sin(up * 0.42f + x * 0.03f));
        }

        static Vector3 At(float x, float off)
        {
            return new Vector3(x, CSandY(x, off), CShoreZ(x) + off);
        }

        // ═════════════════════════ 80 ═════════════════════════
        [MenuItem("Tools/Ocean/80 建海岸骨架(场景+相机+太阳+雾+天空)")]
        public static void BuildShell()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU80 REFUSED: 在 Play 模式，先跑 Tools/WaterProject/26 退出\n"); return; }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 太阳 ──
            var sunGo = new UnityEngine.GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = UnityEngine.LightType.Directional;
            sun.intensity = 2.9f;
            sun.color = new Color(1f, 0.955f, 0.885f);
            // 仰角 12°：碎闪是几何条件，太阳一高镜面带就跑出画面（《05》D-4 同源）。
            // 方位 236° 是**算出来的不是试出来的**：相机 yaw 58° ⇒ 视线方位 58°，
            // 而 Unity 的 light.transform.rotation=Euler(x,y,0) 里"朝太阳的方向"
            // = -forward，y=236 时它落在 56°，与视线只差 2° ⇒ 碎闪带正对画面中央。
            // 上一版给的是 y=30（朝太阳方向 228°，等于太阳在相机正后方），
            // 结果掠射水面成一面纯净的浅蓝镜子，海和天糊成一片、完全没有碎闪。
            sun.transform.rotation = Quaternion.Euler(12f, 236f, 0f);
            sun.shadows = UnityEngine.LightShadows.None;   // 开阔海岸没有需要收的投影

            // ── 天空 / 雾 / 环境光 ──
            RenderSettings.skybox = OpenCoastSky();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // 雾色 == 天空地平色，这是地平线不留硬边的唯一条件。两个都来自 SkyHorizon。
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = AmbientMode.Skybox;   // 开阔地：天光就是主填充
            DynamicGI.UpdateEnvironment();

            // ── 相机 ──
            var camGo = new UnityEngine.GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = new Vector3(CamX, CSandY(CamX, CamOff) + EyeH, CShoreZ(CamX) + CamOff);
            camGo.transform.rotation = Quaternion.Euler(CamEuler);
            camGo.AddComponent<AudioListener>();
            var cd = cam.GetUniversalAdditionalCameraData();
            if (cd == null) cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;

            // ── 后处理 ──
            var volGo = new UnityEngine.GameObject("GlobalVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = OceanVolume();

            SelfCheck();
            WriteProbe("MENU80 shell built\n");
        }

        // 两份式子对账：把 C# 版在固定采样点的值写进探针。
        // HLSL 版没法从 C# 调用，所以这里是"人眼可核"的输出，不是自动断言。
        static void SelfCheck()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("SELF-CHECK 同式 v2  C# 采样（要和 OceanShore.hlsl 逐项对得上）\n");
            float[] xs = { -400f, -30f, 137.5f, 300f, 420f };
            foreach (var x in xs)
                sb.Append("   ShoreZ(").Append(x.ToString("F1")).Append(") = ")
                  .Append(CShoreZ(x).ToString("F4")).Append('\n');
            float[] offs = { -70f, -16f, -7.5f, 0f, 6f, 90f };
            // 两个不同 x：v2 起 SandY 含沿岸起伏，只在一个 x 上核等于没核这一项
            foreach (var sx in new[] { -30f, 137.5f })
                foreach (var o in offs)
                    sb.Append("   SandY(").Append(sx.ToString("F1")).Append(", ")
                      .Append(o.ToString("F1")).Append(") = ")
                      .Append(CSandY(sx, o).ToString("F4")).Append('\n');
            float maxSlope = 0f;
            for (float x = Xmin; x <= Xmax; x += 0.5f)
                maxSlope = Mathf.Max(maxSlope, Mathf.Abs((CShoreZ(x + 0.5f) - CShoreZ(x)) / 0.5f));
            sb.Append("   岸线最大斜率 ").Append(maxSlope.ToString("F3"))
              .Append("（要求 < 0.25，否则离岸距离会系统性偏大）\n");
            WriteProbe(sb.ToString());
        }

        // ═════════════════════════ 81 ═════════════════════════
        [MenuItem("Tools/Ocean/81 海面网格与材质")]
        public static void BuildSea()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU81 REFUSED: 在 Play 模式\n"); return; }

            // 行距按幂次往海上拉开：近岸密（碎浪全在那儿）、远处稀到几米一档。
            // 几何只负责涌浪（λ 24~150m），细碎的水皮是片元法线，所以远处粗无所谓。
            int seg = COLS, rows = OCEAN_ROWS;
            var v = new List<Vector3>(rows * seg + rows);
            for (int j = 0; j <= rows; j++)
            {
                float t = (float)j / rows;
                float off = -UpBack + (SeaOut + UpBack) * Mathf.Pow(t, 1.85f);
                for (int i = 0; i <= seg; i++)
                {
                    float x = Mathf.Lerp(Xmin, Xmax, (float)i / seg);
                    v.Add(new Vector3(x, 0f, CShoreZ(x) + off));
                }
            }
            int rowLen = seg + 1;
            var idx = new List<int>((rows) * seg * 6);
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * rowLen + i, b = a + 1;
                    int c = (j + 1) * rowLen + i, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }

            // ⚠ 顶点数上限：2022.3 脚本建的网格默认 UInt16 索引，43433 顶点还在范围内，
            //   超过 65535 会直接抛错。想加 COLS/rows 之前先算 v.Count。
            //   （本版本托管层里取不到 MeshIndexFormat 类型，改不了格式，所以只能靠这条约束。）
            var mesh = new Mesh { name = "OceanWater" };
            mesh.SetVertices(v);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var pad = mesh.bounds; pad.Expand(new Vector3(0f, 8f, 0f));   // 顶点在着色期会被浪推走
            mesh.bounds = pad;
            SaveMeshAsset(mesh, MeshFolder + "/OceanWater.asset", "OceanWater");

            var go = GetOrCreate("OceanWater");
            var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/OceanWater.asset");
            mr.sharedMaterial = OceanWaterMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU81 sea mesh " + v.Count + " verts / " + (idx.Count / 3) + " tris\n");
        }

        // ═════════════════════════ 82 ═════════════════════════
        [MenuItem("Tools/Ocean/82 沙滩网格与材质")]
        public static void BuildBeach()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU82 REFUSED: 在 Play 模式\n"); return; }

            int seg = COLS, rows = BEACH_ROWS;
            var v = new List<Vector3>((rows + 1) * (seg + 1));
            for (int j = 0; j <= rows; j++)
            {
                float off = Mathf.Lerp(-BeachBack, BeachFront, (float)j / rows);
                for (int i = 0; i <= seg; i++)
                {
                    float x = Mathf.Lerp(Xmin, Xmax, (float)i / seg);
                    v.Add(At(x, off));
                }
            }
            int rowLen = seg + 1;
            var idx = new List<int>(rows * seg * 6);
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * rowLen + i, b = a + 1;
                    int c = (j + 1) * rowLen + i, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }

            // 2.3 万顶点，离 65535 的默认索引上限还有一倍余量（同上海面那条约束）
            var mesh = new Mesh { name = "OceanBeach" };
            mesh.SetVertices(v);
            mesh.SetTriangles(idx, 0);
            // 沙滩用**平滑法线**：逐面法线会把 3.3m 的网格格子画成一片片瓷砖，
            // 沙是软的，它的形变感应该来自材质里的沙纹而不是三角面（潭线盆底同理）。
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = GetOrCreate("OceanBeach");
            var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = SaveAndReturn(mesh, MeshFolder + "/OceanBeach.asset", "OceanBeach");
            mr.sharedMaterial = OceanSandMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU82 beach mesh " + v.Count + " verts, 水线 x 采样 z="
                       + CShoreZ(CamX).ToString("F2") + "\n");
        }

        // ═════════════════════════ 83 ═════════════════════════
        [MenuItem("Tools/Ocean/83 岸上道具(礁石+卵石+漂流木+远岬)")]
        public static void BuildProps()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU83 REFUSED: 在 Play 模式\n"); return; }

            var rng = new System.Random(202609251);
            foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name.StartsWith("OceanRock_") || go.name.StartsWith("OceanPebble_")
                                   || go.name.StartsWith("OceanLog_") || go.name.StartsWith("OceanHeadland")))
                    Object.DestroyImmediate(go);

            var rockMat  = GetLitMat("Ocean_Rock",     new Color(0.085f, 0.088f, 0.092f));
            var pebMat   = GetLitMat("Ocean_Pebble",   new Color(0.150f, 0.140f, 0.125f));
            var woodMat  = GetLitMat("Ocean_Driftwood", new Color(0.115f, 0.090f, 0.068f));
            // 太阳在正前方 ⇒ 岬角是纯逆光剪影。剪影是对的，但反照率一旦压到
            // 0.04 这种程度，雾就托不起它，直接读成"漂在海上的黑板"。
            // ⚠ 反过来，雾色（= 地平色）大幅提亮之后它会被托成一片白、失去分离，
            //   所以这个值必须跟着 SkyHorizon 一起调，不能定死。
            var cliffMat = GetLitMat("Ocean_Cliff",    new Color(0.070f, 0.086f, 0.112f));

            var rocks = new Mesh[4];
            for (int i = 0; i < 4; i++) rocks[i] = WaterProjectSetup.BuildFacetedRock(i * 5 + 2, 0.85f);
            var pebbles = new Mesh[3];
            for (int i = 0; i < 3; i++) pebbles[i] = WaterProjectSetup.BuildFacetedRock(i * 3 + 11, 0.28f);

            // 礁石：一半泡在碎浪带里。碎浪判据看的是水深，所以它们的落点直接决定
            // "白沫绕着石头炸开"这个画面 —— 这是海岸最有说服力的一笔。
            int n = 0;
            for (int k = 0; k < 11; k++)
            {
                float x = Mathf.Lerp(Xmin + 60f, Xmax - 60f, (float)k / 10f)
                        + (float)(rng.NextDouble() - 0.5) * 40f;
                float off = 4f + (float)rng.NextDouble() * 26f;      // 水深 0.22~1.43m
                float s = 0.9f + (float)rng.NextDouble() * 1.9f;
                PlaceBlob("OceanRock_" + n++, At(x, off), s, s * (0.55f + (float)rng.NextDouble() * 0.5f),
                          rocks[rng.Next(rocks.Length)], rockMat, rng);
            }
            // 岸上一小丛：给前景一个"能摸到"的锚点。要盖住相机所在的 up≈26，
            // 并往视线方向（+x）铺，否则全落在身后，右下角空成一片。
            for (int k = 0; k < 6; k++)
            {
                float x = CamX + 4f + (float)rng.NextDouble() * 120f;
                float off = -8f - (float)rng.NextDouble() * 22f;
                float s = 0.6f + (float)rng.NextDouble() * 1.3f;
                PlaceBlob("OceanRock_" + n++, At(x, off), s, s * 0.7f,
                          rocks[rng.Next(rocks.Length)], rockMat, rng);
            }

            // 卵石：沿干湿交界撒一条，勾出历史上最高水位那条线。
            // 上界要盖过机位（CamOff = -26），否则整片前景干沙是空的。
            int pebCount = 0;
            for (int k = 0; k < 190; k++)
            {
                float x = Mathf.Lerp(Xmin, Xmax, (float)rng.NextDouble());
                float off = -34f + (float)rng.NextDouble() * 40f;
                float s = 0.16f + (float)rng.NextDouble() * 0.30f;
                PlaceBlob("OceanPebble_" + k, At(x, off), s, s * 0.62f,
                          pebbles[rng.Next(pebbles.Length)], pebMat, rng);
                pebCount++;
            }

            // 漂流木：沿长轴拉扁的石头，横在干沙上
            var logMesh = WaterProjectSetup.BuildFacetedRock(1, 0.30f);
            for (int k = 0; k < 5; k++)
            {
                float x = CamX + 6f + (float)rng.NextDouble() * 220f;
                float off = -13f - (float)rng.NextDouble() * 22f;
                var at = At(x, off);
                var go = new UnityEngine.GameObject("OceanLog_" + k);
                go.transform.position = at + new Vector3(0f, 0.18f, 0f);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f,
                                                         (float)(rng.NextDouble() - 0.5) * 7f);
                go.transform.localScale = new Vector3(3.2f + (float)rng.NextDouble() * 2.6f, 0.40f, 0.38f);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = logMesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = woodMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // 远岬：把"海到天际无限远"这件事收住。放在雾里，靠雾自动退成剪影。
            // 一定要**两座一近一远**：只有一座时它读起来是"漂在海上的巨石"而不是
            // "陆地的一角"；第二座被雾多吃掉一层，深度关系才成立。
            // lump 给到 1.25（比岸上的礁石还高）：默认那档的棱线太光滑，
            // 拉成 240m 宽之后读成"一块切了顶的板"，不像被海蚀过的岩脊。
            var cliff = WaterProjectSetup.BuildFacetedRock(3, 1.25f);
            PlaceHeadland("OceanHeadland", cliff, cliffMat, rng, 300f, 260f, 6f,  240f, 30f, 150f, 24f);
            PlaceHeadland("OceanHeadland_far", cliff, cliffMat, rng, -250f, 430f, 2f, 190f, 22f, 130f, -37f);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU83 props rocks " + n + " pebbles " + pebCount
                       + " logs 5 headland 2\n");
        }

        static void PlaceHeadland(string nm, Mesh m, Material mat, System.Random rng,
                                  float x, float off, float cy, float sx, float sy, float sz, float yaw)
        {
            var go = new UnityEngine.GameObject(nm);
            go.transform.position = new Vector3(x, cy, CShoreZ(x) + off);
            go.transform.rotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 3f, yaw,
                                                     (float)(rng.NextDouble() - 0.5) * 3f);
            go.transform.localScale = new Vector3(sx, sy, sz);
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void PlaceBlob(string nm, Vector3 at, float sxz, float sy, Mesh m, Material mat, System.Random rng)
        {
            var go = new UnityEngine.GameObject(nm);
            go.transform.position = at + new Vector3(0f, sy * 0.30f, 0f);
            go.transform.rotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 14f,
                                                     (float)rng.NextDouble() * 360f,
                                                     (float)(rng.NextDouble() - 0.5) * 14f);
            go.transform.localScale = new Vector3(sxz * (0.85f + (float)rng.NextDouble() * 0.35f),
                                                  sy,
                                                  sxz * (0.85f + (float)rng.NextDouble() * 0.35f));
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ═════════════════════════ 84 ═════════════════════════
        [MenuItem("Tools/Ocean/84 整链建海岸(80→83)")]
        public static void BuildWholeCoast()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU84 REFUSED: 在 Play 模式，先跑 Tools/WaterProject/26 退出\n"); return; }

            WriteProbe("MENU84 begin\n");
            var steps = new System.Collections.Generic.KeyValuePair<string, System.Action>[]
            {
                new System.Collections.Generic.KeyValuePair<string, System.Action>("80 骨架", BuildShell),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("81 海", BuildSea),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("82 滩", BuildBeach),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("83 道具", BuildProps),
            };
            for (int i = 0; i < steps.Length; i++)
            {
                WriteProbe("MENU84 STEP " + steps[i].Key + "\n");
                try { steps[i].Value(); }
                catch (System.Exception e) { WriteProbe("MENU84 FAIL " + steps[i].Key + " :: " + e.Message + "\n"); }
            }
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            WriteProbe("MENU84 done -> " + ScenePath + "\n");
        }

        // ═════════════════════════ 85 ═════════════════════════
        [MenuItem("Tools/Ocean/85 Play 中截图 Game 视图")]
        public static void CaptureCoast()
        {
            GameViewCapture.Request("MENU85", "Docs/screenshots/_ocean_view.png");
        }

        // ═════════════════════════ 86 ═════════════════════════
        [MenuItem("Tools/Ocean/86 切换海面调试(0关/1水深/2破碎/3沫带)")]
        public static void CycleDebug()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Ocean_Water.mat");
            if (m == null) { WriteProbe("MENU86 REFUSED: Ocean_Water.mat 不存在\n"); return; }
            float cur = m.HasProperty("_DebugMode") ? m.GetFloat("_DebugMode") : 0f;
            float next = cur >= 3f ? 0f : cur + 1f;
            m.SetFloat("_DebugMode", next);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
            WriteProbe("MENU86 _DebugMode -> " + next
                       + "  (1:R=水深 G=透光 B=菲涅尔 / 2:R=破碎度 G=浅水增益 B=swash / 3:R=沫带 G=碎浪 B=总泡沫)\n");
        }

        // ═════════════════════════ 材质 ═════════════════════════
        static string BeachShaderName()
        {
            var sh = Shader.Find("WaterProject/Beach_Sand");
            if (sh == null) WriteProbe("!! WaterProject/Beach_Sand 没编译过，沙滩会走内建默认材质\n");
            return sh != null ? sh.name : "Universal Render Pipeline/Lit";
        }

        static Material GetMat(string name, string shaderName)
        {
            string p = MatFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find(shaderName);
            if (sh == null) { WriteProbe("!! shader " + shaderName + " NOT FOUND\n"); return m; }
            if (m == null) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            return m;
        }

        static Material GetLitMat(string name, Color linearRGBA)
        {
            var m = GetMat(name, "Universal Render Pipeline/Lit");
            if (m == null) return null;
            m.SetColor("_BaseColor", linearRGBA);
            m.SetColor("_Color", linearRGBA);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material OpenCoastSky()
        {
            string p = MatFolder + "/Sky_OpenCoast.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            // Sky_Coast 而不是复用 SkySimpleBlue：海岸画幅里天空占 55%，
            // 纯色渐变就是一块蓝布。潭线能复用是因为那边只剩顶上一条带。
            var sh = Shader.Find("WaterProject/Sky_Coast");
            if (sh == null) { WriteProbe("!! Sky_Coast NOT FOUND，退回 SkySimpleBlue\n");
                              sh = Shader.Find("WaterProject/SkySimpleBlue"); }
            if (sh == null) { WriteProbe("!! 找不到任何天空着色器\n"); return null; }
            if (m == null) { m = new Material(sh) { name = "Sky_OpenCoast" }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            m.SetColor("_ZenithColor",  SkyZenith);
            // 地平色 == 雾色，这是海/天交界不留硬边的唯一条件，两处都只能来自这一个常量
            m.SetColor("_HorizonColor", SkyHorizon);
            m.SetColor("_GroundColor",  SkyGround);
            if (m.HasProperty("_CloudColor"))
            {
                m.SetColor("_CloudColor",  new Color(1.000f, 0.998f, 0.995f));
                // ⚠ 云底只能给"浅灰蓝"，不能给深蓝灰：用户抱怨的就是"太暗"，
                //   靠压暗云底把团块拉出来正中反面。对比要靠形状和数量，不靠明度。
                m.SetColor("_CloudShadow", new Color(0.430f, 0.510f, 0.630f));
                m.SetFloat("_CloudCover",  0.68f);
                // ⚠ 0.045 是差了一个数量级的错值。代进去算：画幅顶缘投影半径
                //   |d.xz|/d.y ≈ 2.5，经 comp 压缩后 2.1，乘 0.045 只有 0.096；
                //   到地平也才 0.14 —— 整片天落在**同一个噪声格子**里，f 几乎恒定，
                //   所以"云"从来就没有过结构，之前看到的那条带其实是日晕 + 贴地衰减项。
                //   0.9 让天顶到地平横跨约 2~6 个格子，才是能分辨的团块。
                m.SetFloat("_CloudScale",  0.9f);
                m.SetFloat("_CloudDrift",  0.010f);
                m.SetFloat("_SunGlow",     0.30f);
            }
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        // ⚠ 两个材质都必须写这一组：Beach_Sand 和 Water_Ocean include 的是同一个
        //   OceanShore.hlsl、读的是同一个 cbuffer。沙滩材质漏写 ⇒ _ShoreA/_ShoreB/
        //   _ShoreSlope 全为 0 ⇒ OffDist 退化成 wp.z、岸线变成 z=0 的直线，
        //   湿沙带整片错位，而海面看着完全正常 —— 最难查的那种错。
        static void ApplyShoreParams(Material m)
        {
            m.SetVector("_Shore", Shore);
            m.SetVector("_ShoreA", ShoreA);
            m.SetVector("_ShoreB", ShoreB);
            m.SetFloat("_ShoreSlope", ShoreSlope);
            m.SetFloat("_BeachRise", BeachRise);
            m.SetFloat("_BeachCurve", BeachCurve);
            m.SetFloat("_SwashReach", SwashReach);
            m.SetFloat("_SwashSpeed", SwashSpeed);
            m.SetFloat("_SwashFade", SwashFade);
        }

        static Material OceanSandMaterial()
        {
            var m = GetMat("Ocean_Sand", BeachShaderName());
            if (m == null) return null;
            ApplyShoreParams(m);
            m.SetColor("_DryColor",   new Color(0.360f, 0.285f, 0.180f));
            m.SetColor("_WetColor",   new Color(0.150f, 0.116f, 0.075f));
            m.SetColor("_FoamColor",  new Color(0.940f, 0.965f, 0.975f));
            m.SetFloat("_RippleTint", 0.22f);
            m.SetFloat("_WetGloss",   0.55f);
            m.SetFloat("_SpecPower",  90f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material OceanWaterMaterial()
        {
            var m = GetMat("Ocean_Water", "WaterProject/Water_Ocean");
            if (m == null) return null;

            // 岸线/剖面/沫带：与 Beach_Sand 写同一组值，且必须与 OceanShore.hlsl 同式
            ApplyShoreParams(m);
            m.SetVector("_WindDir", WaveDir);

            m.SetColor("_SkyZenith", SkyZenith);
            m.SetColor("_SkyHorizon", SkyHorizon);
            m.SetColor("_SunColor", new Color(1.0f, 0.955f, 0.885f));
            m.SetColor("_DeepColor",    new Color(0.004f, 0.024f, 0.052f));
            m.SetColor("_ShallowColor", new Color(0.115f, 0.470f, 0.495f));
            m.SetColor("_FoamColor",    new Color(0.940f, 0.965f, 0.975f));

            m.SetFloat("_Absorb", 0.30f);
            m.SetFloat("_Opacity", 0.95f);
            m.SetFloat("_MaxDepth", 10.0f);

            // 振幅的上限是**机位高度**定的，不是审美定的：crest 最大 = Σa·_SwellAmp·_ShoalGain。
            // 相机还站在湿沙带里（up=15m）时这里只能给 0.75，否则一个浪头就把镜头埋了；
            // 退到滩肩（眼高 4.5m+）之后 1.0 才安全（crest 2.2m）。
            m.SetFloat("_SwellAmp", 1.0f);
            m.SetFloat("_SwellScale", 1.0f);
            m.SetFloat("_SwellSpeed", 1.0f);
            m.SetFloat("_WindAlign", 0.80f);
            m.SetFloat("_Steep", 0.62f);

            m.SetFloat("_ShoalGain", 1.7f);
            m.SetFloat("_ShoalRange", 7.0f);
            // 0.75 → 破碎线从离岸约 13m 推到 60m 开外，短浪条带才有地方摆出好几排白线。
            // 再低就会重演"整片海都在碎"那张白纸（见《05》D-19）。
            m.SetFloat("_BreakerIndex", 0.72f);
            m.SetFloat("_BreakFoam", 0.95f);
            m.SetFloat("_Whitecap", 0.20f);

            m.SetFloat("_SwashFoam", 0.75f);

            // 皮肤尺度比潭线大一个量级是刻意的：海要的是"远处还读得出纹理"。
            // 潭那边把高频按掠射压到 0.20 是为了杀灯芯绒（《05》D-17），
            // 这里格子本身有 1~3m，掠射时还有十几个像素，可以留更高的地板。
            m.SetFloat("_DetailStrength", 0.30f);
            m.SetFloat("_DetailScale", 0.9f);
            m.SetFloat("_DetailFlow", 0.16f);
            m.SetFloat("_RippleStrength", 0.10f);
            m.SetFloat("_RippleScale", 3.0f);

            // 4 → 5：让"水面变成镜子"推迟到更掠射的角度，中段多露出水体本身的深蓝
            m.SetFloat("_FresnelPow", 5.0f);
            // 不给到 1.0：掠射时"完全照镜子"会让海和天在明度上糊成一片，
            // 而"海在天下面一条深色带"正是这张图最需要的对比。
            // ⚠ 这个值和 SkyHorizon 是**联动**的：地平色（= 雾色 = 水面反射兜底色）一提亮，
            //   这里就必须往下收，否则远海跟着一起泛白，海天反而更糊。
            m.SetFloat("_SkyReflect", 0.56f);
            m.SetFloat("_SpecPower", 420f);
            m.SetFloat("_SpecStrength", 0.7f);
            m.SetFloat("_SunGloss", 200f);
            m.SetFloat("_SunStrength", 2.2f);
            m.SetFloat("_DebugMode", 0f);

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static UnityEngine.Rendering.VolumeProfile OceanVolume()
        {
            string p = "Assets/Settings/URP/OceanVolume.profile.asset";
            var prof = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(p);
            if (prof == null)
            {
                prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(prof, p);
            }
            // ⚠ Add<T>() 不 AddObjectToAsset 就永远不落盘，profile 里只留 fileID:0 空槽
            prof.components.RemoveAll(c => c == null);

            if (!prof.TryGet<Tonemapping>(out var ton))
            { ton = prof.Add<Tonemapping>(); AssetDatabase.AddObjectToAsset(ton, prof); }
            ton.mode.overrideState = true; ton.mode.value = TonemappingMode.ACES;

            // 开阔日光是亮的：潭线那套 postExposure 1.5 是给"暗底 + 少量亮部"补的，
            // 照抄过来会把海洗成牛奶。这里压回 0.85。
            if (!prof.TryGet<ColorAdjustments>(out var col))
            { col = prof.Add<ColorAdjustments>(); AssetDatabase.AddObjectToAsset(col, prof); }
            col.postExposure.overrideState = true; col.postExposure.value = 0.85f;
            col.saturation.overrideState = true;   col.saturation.value = 14f;
            col.contrast.overrideState = true;     col.contrast.value = 12f;

            // 只吃白沫和太阳碎闪；阈值低于 1 会把整片海面一起提亮，画面立刻发灰
            if (!prof.TryGet<Bloom>(out var bl))
            { bl = prof.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bl, prof); }
            bl.threshold.overrideState = true;  bl.threshold.value = 1.20f;
            bl.intensity.overrideState = true;  bl.intensity.value = 0.28f;
            bl.scatter.overrideState = true;    bl.scatter.value = 0.60f;

            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        // ═════════════════════════ 共用 ═════════════════════════
        static UnityEngine.GameObject GetOrCreate(string nm)
        {
            var go = UnityEngine.GameObject.Find(nm);
            if (go == null) go = new UnityEngine.GameObject(nm);
            return go;
        }

        static Mesh SaveAndReturn(Mesh m, string path, string name)
        {
            SaveMeshAsset(m, path, name);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        static void SaveMeshAsset(Mesh m, string path, string name)
        {
            m.name = name;
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old == null) AssetDatabase.CreateAsset(m, path);
            else
            {
                // 就地覆写：CopySerialized 会留空 GPU 缓冲
                old.Clear();
                old.SetVertices(m.vertices);
                old.SetNormals(m.normals);
                old.SetTriangles(m.triangles, 0);
                old.bounds = m.bounds;
                old.RecalculateBounds();
                EditorUtility.SetDirty(old);
                Object.DestroyImmediate(m);
            }
            AssetDatabase.SaveAssets();
        }

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }
    }
}
#endif
