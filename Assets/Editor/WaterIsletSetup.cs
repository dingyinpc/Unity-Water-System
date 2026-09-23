// ============================================================================
// WaterIsletSetup.cs — 第四条水线：热带环礁潟湖（`IsletScene`，`Tools/Islet/90-96`）
//
// 对标：Docs/参考视频帧/参考1~6.png（抖音 @雨鱼鱼雨雨雨，标签 #体积云 #海岛风光）。
//      目标值表在 Docs/参考视频帧/海岛目标色板.json，验收跑 Tools/compare_reference.py。
//
// 为什么是**第四条**线而不是改海岸线（海岸线已经建在 OceanScene）：
//   OceanScene 回答的是"一道岸 + 涌浪 + 碎浪 + 冲滩"，是**开海**。
//   这六张参考里没有一道连续的岸 —— 是**群岛与潟湖**：水占满画面下半，
//   陆地是几座浮在浅滩上的沙洲小岛 + 右侧一座带海蚀巨岩的大岛。
//   于是三个机制和海岸线正相反：
//     · 海岸线的水深由"离岸多远"决定（一条岸线函数就够）；
//       这里每座岛都有自己的浅滩，水深必须是**一张高度场**，不能是单值函数。
//     · 海岸线的浪要碎；潟湖里有岛挡浪，近处几乎只有缓涌，
//       观感全靠"水色随深度变化 + 太阳碎闪"，不是靠白沫。
//     · 海岸线的天空只占上半；这里天空要**体积云**（参考作者自己打的标签），
//       是本轮最重的一块，单独写在 Sky_Tropical.shader。
//
// 资产前缀 **Islet***：三条老线已经占了 Pond*（Main）/ Pool*、Pond_*（潭）/
//   Ocean*（海岸），撞名会互相覆盖网格且两边都"能渲染"——
//   这是 Tools/audit_materials.py 第⑥项专门盯的事。
//
// 用户约束：不考虑性能、越精细越好、建模走 Blender、以 FBX 导入。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;       // 水面网格要 List<Vector3>/List<int>
using UnityEngine;
using UnityEngine.Rendering;            // AmbientMode / Volume / VolumeProfile 都在这
using UnityEngine.Rendering.Universal;

// ⚠ 必须是 WaterProject.EditorTools —— GameViewCapture（取带后处理的 Game 视图）
//   就在这个命名空间里，写成 WaterProject 会报 "GameViewCapture does not exist"。
namespace WaterProject.EditorTools
{
    public static class WaterIsletSetup
    {
        const string ScenePath  = "Assets/Scenes/IsletScene.unity";
        const string MeshFolder = "Assets/Art/Meshes";
        const string MatFolder  = "Assets/Art/Materials";
        const string FbxFolder  = "Assets/Art/Models/Islet";

        // ───────── 大气 ─────────
        // 目标取自 Docs/参考视频帧/海岛目标色板.json 里**平视构图**那两张（参考1/参考6）。
        // 第一版我误用了仰拍的参考5 的天顶值 (3,118,200)，量出来偏了一整个色相。
        //
        // 实测→目标的差距在 R：渲出 (97,153,203)，目标 (65,174,224)。
        // 而画幅上缘仰角只有 ~22° ⇒ `up` 最大 0.38，可见那一段**几乎全是地平色**，
        // 天顶色根本进不来。所以要动的是**地平色**（降 R、抬 G/B）+ 渐变指数，
        // 不是天顶色。（《05》C-10 同一类方向错误：先想清楚哪个旋钮真的在管这段。）
        // 天顶/地平色**不能照抄色板的显示值**，中间隔着 ACES + saturation 22，
        // 而 ACES 在这个量级上压红压得最狠：
        //   喂进去 HorizonColor (0.30,0.62,0.85) → 屏上地平带实测 (66,150,210)
        //   —— R 从 0.30 掉到 0.26 线性，B 却几乎没动，蓝色被相对放大了。
        //   Tools/compare_reference.py 报 horizon_haze (110,163,171) vs 目标
        //   (165,205,231)、sky_mid (74,153,215) vs (100,182,224)，都是同一个病：
        //   **不是不够蓝，是红和整体亮度不够**。
        // 所以这里把地平色的 R 大幅抬、三通道一起抬亮度；天顶补 G（修 sky_upper
        // 实测 G 只有 149 而目标 174）。
        static readonly Color SkyZenith  = new Color(0.010f, 0.420f, 0.800f);
        // 地平色在加了 _GradNorm 之后可以回到"淡"：它现在只负责画幅下缘那一小段，
        // 不再被迫承担整片天的颜色（上一版把 R 压到 0.20 就是因为这个）。
        // ── 这一档是在**探针重新对准之后**重测得到的 ──
        //   先把 horizon_haze 收到"地平线上方左三分之一"（开阔天），
        //   然后做了一件必须做的事：**拿同一把尺量参考帧自己**——
        //     参考1 @horizon 0.45 → (162,203,235) vs 目标 (165,205,231)，**偏 4**。
        //   这条带从此才算可信。而我这边是 (127,177,219)：
        //     R 缺 38、G 缺 28、B 缺 12 —— **三通道均匀缺口、红最狠**，
        //     也就是"地平圈太暗"，既不是偏暖也不是偏蓝。
        //   ⇒ 本文件最上面那句旧判断（"不是不够蓝，是红和整体亮度不够"）重新成立；
        //     它之前是被**污染带**否掉的。
        //   ⚠ 上一轮"只抬 B 到 0.950 → 偏差变 52"的结论**作废**：
        //     那个 52 量在压着远岛的带上，不能归因给颜色方向。
        //   ⚠ far_at_horizon **不当判据**：参考帧自己都复现不出它的目标（偏 45），
        //     尺子没校准，先不往它上调。
        static readonly Color SkyHorizon = new Color(0.600f, 0.760f, 0.930f);
        static readonly Color SkyGround  = new Color(0.180f, 0.230f, 0.260f);
        const float SkyGradPow  = 1.4f;
        // 0.46 是**过头了**：地平色修亮之后确实把 horizon_haze 从偏差 60 拉到 38，
        // 但放宽的渐变让那片淡色一路爬进画幅顶缘，sky_upper 从 (69,149,215)
        // 变成 (114,171,219) —— 目标 R 只有 65，红超了 49。
        // 两个旋钮各管一头：**地平色管下缘亮度，GradNorm 管淡霾有多厚**。
        // 下缘已经靠颜色修好了，这里就收回去，让饱和天顶重新占住上半幅。
        //
        // ⚠ "抬亮地平色会顺带把 sky_upper 顶坏、所以要配对收紧 GradNorm"
        //   这个假设**已被实测打掉**：0.40 → 0.28 之后 sky_upper 一字不变
        //   （两次都是 (129,179,231)），说明这条带根本不受渐变宽度管。
        //   本文件下面 _CloudLit 那段注释早就写明了真旋钮是 **_CloudCover**
        //   （"要动的是 _CloudCover"），我这次没往下读完就动手了。
        //   ⇒ 回 0.40。sky_upper 这一轮**不再猜**：它既有云噪声 ±35 的记录，
        //     又不受这里控制，要动就单独为 _CloudCover 开一轮，别和地平色混做。
        const float SkyGradNorm = 0.40f;
        // 雾色 == 天空地平色（唯一来源）。热带空气干净，密度比海岸线低一个量级：
        // 这里要的是"远处岛退成淡影"，不是"整片天糊白"。
        const float FogDensity = 0.00085f;
        // 天光强度：Skybox 模式积分出来的环境光要压一档，
        // 否则"太阳 3.2"在立面上根本赢不过那一片蓝。
        // 0.22 这个数是**量出来的**，不是猜的（Tools/Islet/97 免重建循环）：
        //   0.50 → 沙滩偏蓝，B 压过 R
        //   0.00 → 同一片沙 (203,201,187) 暖调正确，但背光面直接死黑
        // 取一个"暖调刚立得住、背光面还没黑"的中间档。
        const float AmbientIntensity = 0.22f;

        // ───────── 机位 ─────────
        // 对标参考6：左前景一座两棵椰树的小沙洲，右侧带海蚀巨岩的大岛，
        // 中间是潟湖。眼高 1.72m、俯角 ≈ -2.5° ⇒ 地平线落在画面 0.58 附近，
        // 与 compare_reference.py 里 --horizon 的默认值一致。
        const float CamEyeH = 1.72f;
        static readonly Vector3 CamPos   = new Vector3(0f, CamEyeH, -34f);
        static readonly Vector3 CamEuler = new Vector3(-2.5f, 0f, 0f);
        const float CamFov = 50f;

        // ───────── 太阳 ─────────
        // 俯角从 24° 提到 38°。24° 时太阳几乎只照得到**水平顶面**，
        //   于是所有朝向相机的那一面只剩蓝色天光 —— 实测沙洲洲顶 (119,141,155)、
        //   大岛 (43,64,84)，B 都压着 R，沙子渲成了冰。参考1 的沙是 (243,230,203)
        //   暖调、植被是 (36,122,35) 绿，都要求斜射的暖光真的打到立面上。
        //   碎闪带不依赖这个俯角：静水镜面点确实只在脚前 1.72/tan(elev)，
        //   但参考里那条贯穿画面的亮带是**浪面把法线打散**铺开的，抬太阳不会弄丢它。
        const float SunPitch = 38f;

        // 方位：两个互相打架的推导，这里记下**为什么让后者赢**。
        // ① 旧值 180 来自"碎闪带正对画面中央"：相机 yaw=0 ⇒ 视线方位 0°，
        //    而 Euler(x,y,0) 的 forward 是**光行进方向**，"朝太阳"= y+180，
        //    要朝太阳 ≈ 视线 ⇒ y=180。（海岸线那台相机 yaw 58，所以那边算出 236。）
        // ② 新值 322 来自巨岩的受光面。巨岩在 (64,130)、相机在 (0,1.72,-34)
        //    ⇒ 朝相机的法线水平方位 n=(-0.363,-0.932)。受光量
        //      NdotL = n·(-sinθ,-cosθ) = cos(θ - 21.3°)
        //    θ=180 时 = -0.93 ⇒ **钳到 0**：整座 26m 高的立面只剩天光，
        //    六层台地/苔盖全部平成一块黑斑 —— 实测巨岩框内
        //    p25(8,13,18) 中位(10,16,22) p75(12,19,27)，中位到 p75 只差 1.5 倍；
        //    同一套矩形探针量参考1 的岩体是 中位(13,44,57)、p25(7,31,48)→p75(23,76,62)，
        //    差 2.2 倍。**分层是靠明暗差读出来的，不是靠颜色差**，
        //    所以没有方向光就没有层，建模白做。
        //    要 NdotL≈0.5 ⇒ θ = 21.3±60 ⇒ 81 或 **-38.7(=322)**。
        //    取 322 而不是 81：322 时太阳在**前右方**、方位角离视线 38.7°
        //    ≈ 画幅右缘（半宽视场 39.7°），天空的日辉/银边还在画面里；
        //    81 时太阳在相机正左后方，日辉整个出画，horizon_haze 本来就已经
        //    偏暗（实测 110 vs 目标 165），不能再雪上加霜。
        //    顺带三条独立证据都指向 322：沙洲上椰树的影子偏左、
        //    碎闪带偏在画面中右、岩体色是 B>G>R 的冷调（说明它只吃到一部分光）。
        // ⚠ 这条改的是**全场**：水色/天空探针会跟着动，所以改完必须重跑
        //    Tools/compare_reference.py 的全带报告，不能只看岩石那一个框。
        const float SunYaw   = 322f;

        // ═════════════════════════ 90 ═════════════════════════
        [MenuItem("Tools/Islet/90 建骨架(场景+相机+太阳+体积云天空)")]
        public static void BuildShell()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU90 REFUSED: 在 Play 模式，先跑 Tools/WaterProject/26 退出\n"); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 太阳 ──
            var sunGo = new UnityEngine.GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = UnityEngine.LightType.Directional;
            sun.intensity = 3.2f;
            sun.color = new Color(1f, 0.965f, 0.900f);
            sun.transform.rotation = Quaternion.Euler(SunPitch, SunYaw, 0f);
            sun.shadows = UnityEngine.LightShadows.None;

            // ── 天空 / 雾 / 环境光 ──
            RenderSettings.skybox = TropicalSky();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            // 天光强度单独压一档：Sky_Tropical 本身很亮又偏蓝，直接按 1.0 积分成
            // 环境光会把太阳那一路完全压过去 —— 上一版沙地渲成 (119,141,155)
            // 这种"蓝灰"就是它，不是反照率的问题（反照率当时已经降到 0.62 了）。
            RenderSettings.ambientIntensity = AmbientIntensity;
            DynamicGI.UpdateEnvironment();

            // ── 相机 ──
            var camGo = new UnityEngine.GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = CamFov;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 4000f;      // 体积云在 4km 高空，远裁剪面必须盖过云层顶
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = CamPos;
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
            vol.sharedProfile = IsletVolume();

            // 不存盘的话 EditorSceneManager.GetActiveScene().path 是空的，
            // 截图探针里就会打出 `scene=`（本轮实际发生过），事后根本分不清
            // 截的是哪个场景。骨架建完立刻落盘。
            EditorSceneManager.SaveScene(scene, ScenePath);

            WriteProbe("MENU90 shell built  sky=" +
                       (RenderSettings.skybox == null ? "NULL" : RenderSettings.skybox.shader.name) +
                       "  camYaw=" + CamEuler.y + "  sunYaw=" + SunYaw +
                       "  -> " + ScenePath + "\n");
        }

        // ═════════════════════════ 92 ═════════════════════════
        // 水面高度 = 世界 y=0。这不是随手取的：IsletTerrain.ShoreProfile 把
        // "离岸距离 s=0" 定义成高度 0，所以 y=0 就是那条潮间线，
        // 沙/岩/草的分界和水陆分界天然对齐。
        const float WaterY = 0f;
        const float WaterR = 3000f;      // 半径：要让水一直铺到雾都吃干净的远处
        const int   WaterN = 241;        // 单边顶点数 ⇒ 58081 顶点，仍在 uint16 索引内

        [MenuItem("Tools/Islet/92 建潟湖水面(网格+材质)")]
        public static void BuildWater()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU92 REFUSED: 在 Play 模式\n"); return; }
            if (!GuardIsletScene("MENU92")) return;

            // ── 网格：以相机为中心的**二次分布**方格 ──
            // 为什么不用均匀大平面：6km/65535 顶点 ⇒ 间距 74m，近处浪全没了；
            // 而把平面做小（比如 1km）又会在远处露出一条直边 ——
            // 这里雾密度只有 0.00085，1km 处还有 93% 不透明度，根本藏不住。
            // 二次分布让脚下 ~5cm、100m 处 ~1m、400m 处 ~18m：
            // 远处的浪本来就小于一个像素，几何粗一点无所谓，法线才是主力。
            int n = WaterN;
            var v = new List<Vector3>(n * n);
            var uv = new List<Vector2>(n * n);
            for (int j = 0; j < n; j++)
            {
                float tz = (2f * j / (n - 1)) - 1f;
                for (int i = 0; i < n; i++)
                {
                    float tx = (2f * i / (n - 1)) - 1f;
                    float px = Mathf.Sign(tx) * Mathf.Pow(Mathf.Abs(tx), 2f) * WaterR;
                    float pz = Mathf.Sign(tz) * Mathf.Pow(Mathf.Abs(tz), 2f) * WaterR;
                    v.Add(new Vector3(CamPos.x + px, WaterY, CamPos.z + pz));
                    uv.Add(new Vector2(i / (float)(n - 1), j / (float)(n - 1)));
                }
            }
            var idx = new List<int>((n - 1) * (n - 1) * 6);
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1;
                    int c = (j + 1) * n + i, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }

            var mesh = new Mesh { name = "IsletLagoonWater" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
            // 顶点在着色期会被涌推走，包围盒不垫高会被整体裁掉
            var bb = mesh.bounds; bb.Expand(new Vector3(0f, 4f, 0f)); mesh.bounds = bb;

            string mp = MeshFolder + "/IsletLagoonWater.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if (old != null) AssetDatabase.DeleteAsset(mp);
            AssetDatabase.CreateAsset(mesh, mp);

            var go = FindOrCreateWater();
            var mf = go.GetComponent<MeshFilter>();  if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            mr.sharedMaterial = LagoonWaterMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU92 water built  verts=" + v.Count + "  tris=" + (idx.Count / 3) +
                       "  y=" + WaterY + "  R=" + WaterR + "  -> " + mp + "\n");
        }

        static UnityEngine.GameObject FindOrCreateWater()
        {
            var go = UnityEngine.GameObject.Find("IsletWater");
            if (go == null)
            {
                go = new UnityEngine.GameObject("IsletWater");
                go.isStatic = true;
            }
            return go;
        }

        // 水色怎么定：Docs/参考视频帧/海岛目标色板.json 给的是**显示空间**
        // （已过原作者的色调映射 + 抖音压缩），这里填的是**线性**反照率，
        // 中间隔着 ACES + postExposure + saturation 22，所以数值必须比目标更饱和。
        // 判据来自色板自己那句：水是 G≈B 且 R 明显低（青绿），天空是 B>>G（湛蓝）。
        static Material LagoonWaterMaterial()
        {
            string p = MatFolder + "/Islet_LagoonWater.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/Water_Lagoon");
            if (sh == null) { WriteProbe("!! Water_Lagoon NOT FOUND\n"); return null; }
            if (m == null) { m = new Material(sh) { name = "Islet_LagoonWater" }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;

            // 实测→目标（**显示空间**，与 Tools/compare_reference.py 同口径）：
            //   近水  目标 (91,164,183)   v2 (85,191,203)   ← 这一档已经对上了
            //   中带  目标 (145,207,213)  v2 (73,121,143)   ← 压过头，暗了一整档
            //   远水  目标 (210,243,217)  v2 (62,95,122)    ← 同上，而且 B>G 反了
            // 上一版为了修 (157,255,255) 的爆顶，把三档一起往下压，
            // 结果近水刚好、中远水全塌。真正的病根是**潟湖底做错了方向**
            // （远处越深 → depthMix 顶到 DeepColor），那个已经在 IsletTerrain 里改成
            // "近深远浅"；这里只需要把中/远两档抬回亮度，并把反射色做成
            // **G 略高于 B**（参考的远水是 243/217，不是湛蓝）。
            // 两个方向相反的偏差同时出现，说明**深度坡度太陡**：
            //   shallow_over_sand 目标 (120,200,190)  实测 (116,235,227) ← 近处过亮
            //   lagoon_mid        目标 ( 95,190,196)  实测 ( 83,149,155) ← 中段偏暗
            // 近处顶上去的是碎闪叠在浅水上，中段塌下去的是 depthMix 已经爬到
            // Mid/Deep 而沙底返色又已经衰减光。所以：
            //   _Absorb 0.46→0.30、_SandAbsorb 0.34→0.24 —— 让"亮青绿"这段拉长，
            //   中段还吃得到沙底；_GlitterStrength 1.6→1.05 收近处的过冲。
            m.SetColor("_ShallowColor", new Color(0.300f, 0.780f, 0.740f));
            // ⚠ _MidColor 0.64→0.82 是**拧错了旋钮**：lagoon_mid 只从偏差 45 挪到 43，
            //   而 shallow_over_sand 从 37 恶化到 57。抬上来的那点亮度全从
            //   折射/反射链漏到近处去了。回退。
            //   回头看探针定义才明白：shallow_over_sand 取的是**亮度第 78 分位**，
            //   量的根本不是"水体的颜色"，而是**碎闪最亮的那批像素**。
            //   所以近处爆顶该动 _GlitterStrength，动水色一律无效。
            m.SetColor("_MidColor",     new Color(0.150f, 0.640f, 0.660f));
            m.SetColor("_DeepColor",    new Color(0.020f, 0.300f, 0.360f));
            m.SetColor("_SandReturn",   new Color(0.460f, 0.680f, 0.620f));
            // ⚠ 这条改动**方向搞反了，已回退**：上一轮想把中段抬亮，
            //   于是把 _Absorb 0.46→0.30、_SandAbsorb 0.34→0.24 —— 但这两个数
            //   越**小**代表衰减越**慢**，等于把沙底返色在近处也铺满了，
            //   结果 shallow_over_sand 从偏差 37 恶化到 61，实测 (135,255,251) 顶到爆。
            //   中段要抬亮不该靠削弱吸收 —— 潟湖底已经改成"近深远浅"
            //   （IsletTerrain.LagoonFloor），中段本来就是浅水，让它自己亮上来。
            m.SetFloat("_Absorb",      0.46f);
            m.SetFloat("_SandAbsorb",  0.34f);
            m.SetFloat("_Opacity",     0.94f);

            // ── 自产波纹法线贴图 ──
            // ⚠ 必须先确保导入器是 **NormalMap** 类型再加载。默认(Default)会按
            //   sRGB 颜色数据解释，shader 里 UnpackNormal 拿到的就是错的向量
            //   （表现为水面整体朝一个方向歪 / 高光发脏），而且画面看着"像是对的"。
            //   和 FBX 那个 0.01 文件比例是同一类坑：**导入设置**决定资产含义。
            const string WaveTex = "Assets/Art/Textures/Islet/T_WaveNormal.png";
            string waveDiag = "";
            var wi = AssetImporter.GetAtPath(WaveTex) as TextureImporter;
            if (wi == null) { waveDiag = " WAVE_TEX_MISSING:" + WaveTex; }
            else
            {
                if (wi.textureType != TextureImporterType.NormalMap)
                {
                    wi.textureType = TextureImporterType.NormalMap;
                    wi.wrapMode = TextureWrapMode.Repeat;   // 无缝平铺靠这个
                    wi.SaveAndReimport();
                    waveDiag = " waveTex->NormalMap(已改)";
                }
                else waveDiag = " waveTex=NormalMap(本来就对)";
                var wt = AssetDatabase.LoadAssetAtPath<Texture2D>(WaveTex);
                m.SetTexture("_WaveMap", wt);
                // shader 里 _WaveStrength 默认 0（空操作），**是这一行才打开的**。
                // 0.55 的起点来自贴图自己的量级：生成器实测斜率 p50=0.150/p90=0.272
                // （≈9°~15°，水面毛细波的正常范围），再乘 0.55 落到 Ng 上。
                m.SetFloat("_WaveStrength", 0.55f);
                m.SetFloat("_WaveTileA",    0.55f);
                m.SetFloat("_WaveTileB",    1.60f);
                m.SetFloat("_WaveSpeedA",   0.16f);
                m.SetFloat("_WaveSpeedB",   0.30f);
            }
            WriteProbe("WAVE " + waveDiag + " strength=" + m.GetFloat("_WaveStrength") + "\n");

            m.SetFloat("_SwellAmp",    0.055f);
            m.SetFloat("_SwellScale",  1.0f);
            m.SetFloat("_SwellSpeed",  0.35f);
            m.SetFloat("_SlopeStrength", 0.26f);
            m.SetFloat("_SlopeScale",    0.55f);
            m.SetFloat("_FlowSpeed",     0.22f);
            // 毛细尺度 2.6→6.5：上一版碎闪是**一大片一大片的白斑**，
            // 因为 1/2.6≈0.4m 的格在脚前几米就是几十个像素。参考里是细密星点。
            m.SetFloat("_CapStrength",   0.20f);
            m.SetFloat("_CapScale",      6.5f);

            m.SetFloat("_RefractStrength", 0.16f);
            m.SetFloat("_MaxUVOffset",     0.022f);
            m.SetFloat("_Eta",             1.33f);
            m.SetFloat("_BottomBlend",     0.45f);

            // 掠射水面反射的是**地平线那一段淡霾**，不是天顶的湛蓝，
            // 所以这里合法地比 Sky_Tropical 的天顶更淡更偏青绿 ——
            // 判据是参考1 的远水 (210,243,217)：G 高于 B。
            // 但仍要贴着天空地平色的量级，否则水面和它正上方那片天之间
            // 会裂出一条肉眼可见的接缝（《05》C-4 那一类方向错误）。
            //
            // ⚠ 消融实测（把 _SkyReflect 打到 0，其余不动）：
            //   lagoon_mid 中位 (74,137,147) → (11,85,97)，中位亮度 124 → 70。
            //   ⇒ 这条带的亮度**主要是反射项抬起来的**，水体+沙底只给到 70。
            // ⚠ 顺着这个结论试过"让水反射真的天空地平色 (0.600,0.760,0.930)"：
            //   far_at_horizon 偏差 30→**17**（真的修好了），
            //   但 lagoon_mid 54→62、shallow_over_sand 58→**81**，
            //   超差探针从 1 条变 3 条 —— 净结果更坏，**已回退**。
            //   也就是说：远带吃反射色，近带吃的是别的东西，一个旋钮管不了两段。
            // ⚠ 这里还留着一处**未解决的依据冲突**：上面那句"参考1 远水 (210,243,217)
            //   G 高于 B"和尺子自己的 far_at_horizon 目标 (168,206,231) **B>G** 相反。
            //   两者不是同一个取样，谁对没定 —— 近带调不下来，很可能就卡在这。
            m.SetColor("_SkyZenith",  new Color(0.120f, 0.440f, 0.700f));
            m.SetColor("_SkyHorizon", new Color(0.700f, 0.860f, 0.840f));
            m.SetFloat("_FresnelPow",  4.2f);
            m.SetFloat("_SkyReflect",  1.0f);

            m.SetColor("_SunColor", new Color(1.0f, 0.96f, 0.86f));
            // ⚠ 碎闪 shader 里**没有**再乘 mainLight.color（那会把 3.2 乘两遍），
            //   所以这里的强度就是最终峰值。
            // 旧注释写着"1.05 会把 p78 顶到 (114,255,247) 所以下降到 0.6" ——
            //   那条是在**构图修好之前**量的（巨岩还躺着、根物体整体偏 -10374），
            //   近景带里装的东西和现在根本不是同一批像素，不能拿来当依据。
            // 构图定下来之后重量（shallow_over_sand 取该带**第 78 亮分位**，
            //   量的是碎闪亮尾，不是水体色 —— 所以这一档只能动这里，动水色无效）：
            //   0.60 → (65,131,140)  偏差 69      目标 (120,200,190)
            //   1.15 → (95,146,149)  偏差 54
            // ⚠ 这个 1.15 的数是**补测**的：第一次记的是 (67,142,153)/58，
            //   但那一帧其实是在 _SkyHorizon 已被改成 (0.5,0.86,0.9) 的状态下抓的，
            //   拿它当"原反射色 + 1.15"的结论是错的。三组对比重测之后：
            //     glitter 0.6  + 原反射色   → mid 57  shallow 69   超差 1
            //     glitter 1.15 + 天空同源色 → mid 62  shallow 81   超差 2
            //     glitter 1.15 + 原反射色   → mid 54  shallow 54   **超差 0** ← 烘的是这组
            // ⚠ 水面是动画的，所以这几条近景带**逐帧都在抖**。实测连拍三帧的极差
            //   只有 R2~8 / G3~8 / B3~8 ⇒ 噪声底约 ±8。上面 15 分的增益过了噪声。
            m.SetFloat("_GlitterPow",      700f);
            m.SetFloat("_GlitterStrength",  1.15f);
            m.SetFloat("_GlitterBand",       2.4f);
            m.SetFloat("_SheenPow",          60f);
            m.SetFloat("_SheenStrength",    0.18f);

            m.SetColor("_FoamColor", new Color(0.94f, 0.98f, 0.98f));
            m.SetFloat("_FoamBand",     0.30f);
            m.SetFloat("_FoamStrength", 0.30f);
            m.SetFloat("_AmbientStrength", 0.40f);
            m.SetFloat("_DebugMode", 0f);

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        // ═════════════════════════ 97 ═════════════════════════
        // 这个菜单只为了一件事：**把"陆地渲成蓝灰"归因到正确的旋钮上**。
        // 沙的反照率是暖的 (R>G>B)，可实测洲顶渲成 (116,127,131) —— B 压着 R，
        // 说明照在上面的光里蓝色那一路赢了。赢家要么是太阳（不可能，它是暖的），
        // 要么是 AmbientMode.Skybox 从天球积分下来的环境光。
        // 但这两者都不能靠改代码来验证：菜单 90/91/92 一条链跑下来要几分钟，
        // 而 ambientIntensity 是**运行时就能改**的渲染设置。所以单独开一个
        // 免重建的循环菜单，截四张图一比就知道该动谁。
        static readonly float[] AmbientSteps = { 1.00f, 0.50f, 0.25f, 0.00f };

        [MenuItem("Tools/Islet/97 循环天光强度(1.0/0.5/0.25/0)")]
        public static void CycleAmbient()
        {
            if (!GuardIsletScene("MENU97")) return;

            float cur = RenderSettings.ambientIntensity;
            int i = 0;
            for (int k = 0; k < AmbientSteps.Length; k++)
                if (Mathf.Abs(AmbientSteps[k] - cur) < 0.02f) { i = k; break; }
            int nxt = (i + 1) % AmbientSteps.Length;
            RenderSettings.ambientIntensity = AmbientSteps[nxt];
            // 不重建 probe 的话，改的只是那个数，采样到的还是旧天球积分
            DynamicGI.UpdateEnvironment();

            var sun = UnityEngine.Object.FindObjectOfType<Light>();
            WriteProbe("MENU97 ambient -> " + AmbientSteps[nxt] +
                       (sun == null ? "  sun=NULL" :
                        "  sun=" + sun.intensity + " col=" +
                        Mathf.RoundToInt(sun.color.r * 255) + "," +
                        Mathf.RoundToInt(sun.color.g * 255) + "," +
                        Mathf.RoundToInt(sun.color.b * 255) +
                        " pitch=" + Mathf.RoundToInt(sun.transform.rotation.eulerAngles.x)) + "\n");
        }

        // ═════════════════════════ 98 ═════════════════════════
        // 只把**大气相关的常量**重新推一遍进当前场景，不 NewScene、不重建几何。
        // 存在的理由很实际：菜单 90→91→92 一条链要跑好几分钟还会清空场景，
        // 而调天空/雾/曝光这轮改的只是几个颜色常数 —— 为了看一眼效果
        // 付一次全重建的代价不划算。
        // ⚠ 这些常数（SkyZenith / SkyHorizon / FogDensity / AmbientIntensity …）
        //   是**编译期**值，所以改了 .cs 之后仍要等重编译，本菜单只负责把
        //   已经编进去的新值落到场景里，不能替代改代码。
        [MenuItem("Tools/Islet/98 重刷参数(天空/雾/天光/水体材质，不重建几何)")]
        public static void RefreshAtmosphere()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU98 REFUSED: 在 Play 模式\n"); return; }

            if (!GuardIsletScene("MENU98")) return;

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            var sky = TropicalSky();
            if (sky != null) RenderSettings.skybox = sky;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = AmbientIntensity;
            DynamicGI.UpdateEnvironment();

            // 太阳俯角/方位也在这里重刷：它和天空是同一组"大气"参数，
            // 分开刷会出现"改了俯角但要重建地形才看得见"的坑
            var sun = UnityEngine.Object.FindObjectOfType<Light>();
            if (sun != null) sun.transform.rotation = Quaternion.Euler(SunPitch, SunYaw, 0f);

            // 根变换归位。构图**不能**是编辑器里手摆出来的场景状态：
            //   本次 IsletTerrain 在 y=-10386（代码给的是 -12）、IsletWater 在 -10374（应为 0）、
            //   相机在 -10372.28（应为 1.72）—— 三者偏差都是**同一个 -10374**，
            //   也就是有一批根物体被整体平移过（编辑器里拖的，且被存进了 .unity）。
            //   而 94/96 之后重摆的岩石灌木没事，因为 LandHeight 返回的是绝对世界 y ——
            //   于是画面变成"地形在 10km 之下，石头花草悬在灰天里"。
            //   菜单92 只重建水的网格（顶点是世界坐标），FindOrCreateWater 从不复位变换，
            //   所以漂移的水物体重建多少次都回不来。
            // 归位前先把旧值写进探针：下次再漂，日志里看得见，不用靠看图反推。
            var camGo = UnityEngine.GameObject.Find("Main Camera");
            var camFix = new System.Text.StringBuilder();
            if (camGo != null)
            {
                var cTr = camGo.transform;
                camFix.Append("cam " + Was(cTr.position) + "->" + Was(CamPos));
                cTr.position = CamPos;
                cTr.rotation = Quaternion.Euler(CamEuler);
                var cm = cTr.GetComponent<Camera>();
                if (cm != null)
                {
                    cm.fieldOfView   = CamFov;
                    cm.nearClipPlane = 0.15f;
                    cm.farClipPlane  = 4000f;   // 体积云在 4km 高空，远裁剪面必须盖过云层顶
                }
            }

            var terrGo = UnityEngine.GameObject.Find("IsletTerrain");
            if (terrGo != null)
            {
                camFix.Append(" | terr " + Was(terrGo.transform.position) +
                              "->" + Was(IsletTerrain.Origin));
                terrGo.transform.position = IsletTerrain.Origin;
            }
            else camFix.Append(" | terr MISSING");

            var watGo = UnityEngine.GameObject.Find("IsletWater");
            if (watGo != null)
            {
                camFix.Append(" | water " + Was(watGo.transform.position) + "->(0.00,0.00,0.00)");
                watGo.transform.position = Vector3.zero;
            }
            else camFix.Append(" | water MISSING");

            var vol = UnityEngine.Object.FindObjectOfType<Volume>();
            if (vol != null) vol.sharedProfile = IsletVolume();

            // 水体材质也一起重刷。水色这一组常数改得最频繁，
            // 而菜单 92 里真正变的只有材质 —— 网格那 5.8 万个顶点重建是白付的时间。
            string wmat = "none";
            var waterGo = UnityEngine.GameObject.Find("IsletWater");
            if (waterGo != null)
            {
                var wmr = waterGo.GetComponent<MeshRenderer>();
                var wm = LagoonWaterMaterial();
                if (wmr != null && wm != null) { wmr.sharedMaterial = wm; wmat = wm.name; }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            WriteProbe("MENU98 params refreshed  fog=" + FogDensity +
                       "  ambient=" + AmbientIntensity +
                       "  sunPitch=" + SunPitch +
                       "  sky=" + (RenderSettings.skybox == null ? "NULL" : RenderSettings.skybox.name) +
                       "  vol=" + (vol == null ? "NONE" : "ok") +
                       "  water=" + wmat + "  cam=" + camFix + "\n");
        }

        // ═════════════════════════ 93 ═════════════════════════
        // 椰树：Blender 出的 SM_Palm_Coconut_01.fbx（`E:\Blender\Blender工程\palms\`）。
        // 叶片材质**复用已有的 Tree_Leaf**，不另写一个：它的顶点色契约
        // （R=flex 根0→梢1 / G=phase / B=flutter 树皮0叶片1）正是建模侧写的，
        // 换 shader 就等于把背光透光 + 风摆动那套重做一遍。
        const string PalmFbx = FbxFolder + "/SM_Palm_Coconut_01.fbx";

        [MenuItem("Tools/Islet/93 导入椰树(材质+Prefab+摆到沙洲/大岛)")]
        public static void PlacePalms()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU93 REFUSED: 在 Play 模式\n"); return; }
            if (!GuardIsletScene("MENU93")) return;

            var fbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(PalmFbx);
            if (fbx == null) { WriteProbe("MENU93 REFUSED: 找不到 " + PalmFbx + "\n"); return; }

            // ── 单位比例：**必须在这里修，且必须在建 Prefab 之前** ──
            // 两次实测才把这个数定死（单靠猜会一直错下去）：
            //   原始网格真实尺寸 = **8.73m，本来就是对的**
            //   ① useFileScale=true , globalScale=1   → boundsLocal (0.08,0.08,0.09)
            //   ② useFileScale=false, globalScale=100 → boundsLocal (810,808,873)
            //   ② / ① = 10000 ⇒ FBX 容器自带一个 **0.01** 的文件比例
            //   （Blender 存的是米，FBX 规范默认单位是厘米，导出时把这个换算写进了文件）。
            //   所以正解是**忽略文件自带比例**、globalScale 保持 1，
            //   而不是拿 100 去乘 —— 乘 100 只是把 0.01 补回来，再叠上
            //   useFileScale=false 就变成乘了两次，直接冲出 873。
            string fixInfo = "";
            var mi = AssetImporter.GetAtPath(PalmFbx) as ModelImporter;
            if (mi != null)
            {
                mi.useFileScale = false;
                mi.globalScale  = 1f;
                // ⚠ 和菜单94 的巨岩**同一个根因**：Blender 侧沿 +Z 建高度，
                //   不烘轴转换进 Unity 就是躺着的。证据是菜单93 自己打印的
                //     boundsLocal = (5.55, 5.59, **9.00**)
                //   —— 9 m 是树干长度，它落在 **Z** 上；立着的树应该是 Y=9。
                //   所以 PlacePalm 的 Euler(lean, yaw, 0) 不是在"把树放歪"，
                //   而是让一根已经躺着的树干绕着视线转：lean 38° 的那棵在画面里
                //   就是一根横在沙丘上的棍子。构图探针量到的 tilt=1~30° 之所以
                //   看着"没躺"，是因为它量的是 t.up —— 而 t.up 根本不是树干的轴。
                //   改完之后 boundsLocal 必须变成 (5.55, 9.00, 5.59)，这是验收点。
                mi.bakeAxisConversion = true;
                mi.SaveAndReimport();
                // ⚠ **必须重新 Load 一次**：SaveAndReimport 会把原来那份资产对象换掉，
                //   继续用 598 行那份 fbx 去建 Prefab，等于导入器的改动白设。
                //   上一轮就是卡在这：改了 bakeAxisConversion、.meta 也确实被写了，
                //   但打印出来的 boundsLocal 纹丝不动 —— 不是"轴转换没用"，是**根本没吃到**。
                fbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(PalmFbx);
                var rmf = fbx != null ? fbx.GetComponentInChildren<MeshFilter>() : null;
                fixInfo = " scaleFix(file=off,g=1,axis=baked) reimportBounds=" +
                          (rmf != null && rmf.sharedMesh != null
                              ? rmf.sharedMesh.bounds.size.ToString("F2") : "n/a");
            }

            IsletTerrain.EnsureFolder(MatFolder + "/Islet");
            var white = SolidWhiteTex();
            var leafSh = Shader.Find("WaterProject/TreeLeaf");
            if (leafSh == null) WriteProbe("!! MENU93 TreeLeaf NOT FOUND —— 叶片会退回 URP Lit，不会随风动\n");

            var bark = ShadedMat("Islet_PalmBark", Shader.Find("Universal Render Pipeline/Lit"),
                                 new Color(0.230f, 0.170f, 0.128f), 0.72f, 0.05f, white);
            var coco = ShadedMat("Islet_PalmCoco", Shader.Find("Universal Render Pipeline/Lit"),
                                 new Color(0.230f, 0.290f, 0.130f), 0.62f, 0.08f, white);
            // 上一版 (0.135,0.400,0.115) 是"草坪绿"：R 太低、B 也低，
            // 配上过曝的太阳直接饱和成一团荧光绿。参考1 的受光羽叶偏**黄绿**，
            // 抬 R、压 B，让明暗分层有可压的空间（而不是从饱和往更饱和走）。
            // 再抬两档 (0.255,0.430,0.105) → (0.40,0.70,0.16)：
            // 参考1 冠内亮度**中位数 142**，我实测只有 **53** —— 差 2.7 倍，
            // 而 _Dbg=2 量出来受光总量峰值就有 2.5，说明缺的就是反照率本身。
            // 参考的暗部是靠**自遮挡**（下层叶在阴影里）做低的，不是靠把整团压暗，
            // 所以这里只抬亮端，暗部交给 _HeightShade / _FrondVar 两个梯度。
            // ⚠ B 再抬一档 0.16 → 0.26：这不是口味，是量出来的 ——
            //   冠内叶像素的**蓝通道中位数我 12 / 参考 54**，差 4 倍，
            //   所以同样亮度下我这边是"荧光酸绿"，参考是"带点青的草绿"。
            //   G 从 0.70 收到 0.66 把 (G-R) 拉回参考的 51（我实测 61）。
            var leaf = ShadedMat("Islet_PalmLeaf", leafSh != null ? leafSh
                                 : Shader.Find("Universal Render Pipeline/Lit"),
                                 new Color(0.400f, 0.660f, 0.260f), 0.55f, 0.02f, white);
            if (leafSh != null)
            {
                // 风参数：和潭/森林那批树同一档，全场才像被同一阵风吹
                leaf.SetFloat("_WindStrength", 0.16f);
                leaf.SetFloat("_WindFreq",     0.95f);
                leaf.SetVector("_WindDir", new Vector4(0.92f, 0f, 0.32f, 0f));
                leaf.SetFloat("_TransStrength", 1.6f);   // 底色提亮后透射要退一档，不然叶梢过曝
                leaf.SetFloat("_TransPower",   8.0f);
                leaf.SetFloat("_Cutoff",       0.35f);
                leaf.SetColor("_TransColor", new Color(0.58f, 0.82f, 0.26f));

                // ── 冠子的明暗分层 ──
                // 这几项在 shader 里的默认值都是"不改变画面"（白 / 0），
                // 所以潭线那批共用 Tree_Leaf 的树材质**不会**被这次改动波及。
                //
                // 开**薄片双面受光**：小叶绕序 100% 朝下，旧路径把法线翻向相机之后
                // 既取不到太阳（ndl≈0）又只拿到地面那半个球的环境光，
                // 整团冠子塌成一片近黑剪影 —— 实测 luma_spread=14（参考1 是 145）。
                // 开成 1 之后同一次截图 luma_spread 14 → **80**，hue_spread 10 → **32**。
                // 默认 0，所以潭线那批森林材质不受这次改动影响。
                leaf.SetFloat("_SheetLit",    1.0f);
                // 调试通道关掉（排查时直接改 Assets/Art/Materials/Islet/Islet_PalmLeaf.mat
                // 里的 _Dbg 值即可，不必动这里）。
                leaf.SetFloat("_Dbg",         0.0f);
                // ⚠ **叶背色保持白**（实测回退）：上一轮把它开成 (0.42,0.52,0.34)，
                //   luma_spread 80 → **41**、hue_spread 32 → **17** —— 反而更平。
                //   原因是 luma_spread 量的是显示空间亮度的**绝对差**，
                //   而这一项是"整片压暗"，把亮部一起拖下来，差值自然缩小。
                leaf.SetColor("_UnderColor", Color.white);
                leaf.SetFloat("_TipLift",     1.00f);
                // 0.82 → 0.60。用 _Dbg=2 把"受光总量"单独画出来量过：
                //   叶面亮度分位 p5/p50/p95 = 25/116/175，反推受光总量 ≈ 0.4 / 1.2 / 2.5，
                //   峰值 2.5 正好是 sun 3.2 × sin(仰角 52°) ⇒ **太阳是到得了叶片的**。
                //   所以冠子暗**不是受光问题**，是我自己把反照率乘没了：
                //   h01 在冠内只有 0.5~1.0，0.82 强度把中位像素压到只剩 0.36 倍。
                leaf.SetFloat("_HeightShade", 0.60f);
                // 9.0 = 网格**物体空间**的 y 向尺寸（Blender 侧实测 9.00）。
                // 必须是本地高度而不是世界高度：实例还带 0.82~1.12 的缩放，
                // 用世界高度的话同一棵树会因为摆得大而明暗分布不一样。
                leaf.SetFloat("_CrownY",      9.0f);
                leaf.SetFloat("_FrondVar",    0.75f);
            }

            string pp = FbxFolder + "/Islet_Palm_Coconut_01.prefab";
            var tmp = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(fbx);
            var r = tmp.GetComponentInChildren<MeshRenderer>();
            if (r == null) { UnityEngine.Object.DestroyImmediate(tmp); WriteProbe("MENU93 REFUSED: FBX 里没有 MeshRenderer\n"); return; }
            // 按**槽名**认领，不按序号：Blender 导出的槽顺序会随材质创建顺序变
            var src = r.sharedMaterials;
            var bound = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string n = src[i] != null ? src[i].name : "";
                bound[i] = n.Contains("Leaf") ? leaf : n.Contains("Coco") ? coco : bark;
            }
            r.sharedMaterials = bound;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            PrefabUtility.SaveAsPrefabAsset(tmp, pp);
            UnityEngine.Object.DestroyImmediate(tmp);

            // ── 摆放 ──
            // 高度一律问 IsletTerrain.LandHeight，不手写 y：地形是唯一的几何真值，
            // 手填的 y 在下次菜单91 重长之后就会飘在半空或埋进沙里
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(pp);
            int placed = 0;
            // ⚠ 这里原来是 `s_palmDiag = "";` —— 把上面导入器写的 scaleFix **擦掉了**，
            //   于是探针里永远看不到它，"看不出跑没跑"被当成了"没跑"，白查一轮。
            s_palmDiag = fixInfo;

            // 重跑要**替换**而不是再叠一排（和建模侧同一个道理）
            foreach (var old in UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>())
                if (old.name.StartsWith("Palm_")) UnityEngine.Object.DestroyImmediate(old);

            // 参考1 的前景沙洲是**两棵从同一处根部长出来、朝两边反向倾斜**的椰子树
            PlacePalm(prefab, "Palm_Islet_L", new Vector3(-12.0f, 0f, -8.6f),  38f, -14f, 1.00f, ref placed);
            PlacePalm(prefab, "Palm_Islet_R", new Vector3(-10.2f, 0f, -7.4f), -34f,  12f, 0.92f, ref placed);

            // 大岛的绿肩膀上撒一圈椰林（参考1 右侧那片）
            var grove = new Vector3[]
            {
                new Vector3(36f, 0f, 74f), new Vector3(45f, 0f, 68f), new Vector3(55f, 0f, 66f),
                new Vector3(64f, 0f, 72f), new Vector3(41f, 0f, 84f), new Vector3(60f, 0f, 86f),
                new Vector3(50f, 0f, 78f), new Vector3(70f, 0f, 80f),
            };
            for (int i = 0; i < grove.Length; i++)
                PlacePalm(prefab, "Palm_Isle_" + i, grove[i],
                          (i * 47f) % 60f - 30f, (i * 83f) % 360f, 0.82f + 0.30f * ((i * 37 % 10) / 9f), ref placed);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU93 palms placed=" + placed + "  leafShader=" +
                       (leafSh == null ? "FALLBACK-Lit" : "TreeLeaf") + "  prefab=" + pp +
                       s_palmDiag + "\n");
        }

        static string s_palmDiag = "";

        /// <summary>把"树干沿局部 Z 轴"转成"沿 +Y"的直立旋转。
        /// 判据：摆完之后 <c>bounds.max.y - 根部 y</c> 要 ≈ 树高 9，且
        /// <c>boundsWorld.y</c> 档 ≈ 9~10。
        /// ⚠ 符号是**量出来的**：先用 -90，Palm_Islet_L 量到 boundsWorld.y=10.53
        ///   但 bounds.max.y 只比根部高 1.9 ⇒ 整棵树从根部**往下挂**了 7 m，
        ///   倒插在沙里 —— 说明树干在局部 <b>-Z</b>。所以这里给 +90。</summary>
        static readonly Quaternion UprightPalm = Quaternion.Euler(90f, 0f, 0f);

        static void PlacePalm(UnityEngine.GameObject prefab, string nm, Vector3 xz,
                              float lean, float yaw, float scale, ref int placed)
        {
            float y = IsletTerrain.LandHeight(xz.x, xz.z);
            // 落在深水里的直接跳过：那是摆到了潟湖底，树会插在水下
            if (y < 0.25f) return;
            var g = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.name = nm;
            g.transform.position = new Vector3(xz.x, y - 0.10f, xz.z);   // 吃进土 10cm，别站在水膜上
            // ⚠ 树干在**局部 +Z** 上，不在 +Y：build_palm 是拿 Blender 的 Y 当高度建的，
            //   而 build_rock / build_scrub 用的是 Z —— 我自己在同一批资产里不一致。
            //   后果：Euler(lean, yaw, 0) 里的 lean 被当成了"**离地仰角**"而不是
            //   "偏离竖直多少"，lean=1° 的那棵等于平躺在沙丘上（画面上就是横着的棍子），
            //   而前景那棵 lean=38° 反而"看起来像树"，所以一直没被发现。
            //   bakeAxisConversion 证实救不了：开了之后 reimportBounds 仍是 (5.55,5.59,9.00)
            //   —— 因为 FBX 导出时 axis 参数已经换算过，Unity 侧没东西可烘。
            //   所以在这里右乘一个直立旋转，把"立着"和"倾斜"分开，lean 才回到字面意思。
            g.transform.rotation = Quaternion.Euler(lean, yaw, 0f) * UprightPalm;
            g.transform.localScale = Vector3.one * scale;
            placed++;

            // "placed=10" 只证明对象建出来了，**不证明它看得见**。
            // 上一轮就是这么被骗过一次：10 棵全在场景里，画面上一棵都没有。
            // 所以第一棵必须把"网格有没有顶点、世界包围盒多大、材质绑没绑上"打出来 ——
            // 这三种失败（没导入 / 单位比例错了 / 材质不可见）症状一样，只有数字能分开。
            if (placed == 1)
            {
                var mf = g.GetComponent<MeshFilter>();
                var mr = g.GetComponent<MeshRenderer>();
                var m = mf != null ? mf.sharedMesh : null;
                s_palmDiag += " mesh=" + (m == null ? "NULL" : m.name) +
                             " verts=" + (m == null ? 0 : m.vertexCount) +
                             " sub=" + (m == null ? 0 : m.subMeshCount) +
                             " boundsLocal=" + (m == null ? "n/a" : m.bounds.size.ToString("F2")) +
                             " boundsWorld=" + (mr == null ? "n/a" : mr.bounds.size.ToString("F2")) +
                             " mats=[" + string.Join(",", System.Array.ConvertAll(
                                 mr != null ? mr.sharedMaterials : new Material[0],
                                 x => x == null ? "NULL" : x.name)) + "]";
                if (m != null)
                {
                    // 风权重契约的**往返**验证。建模侧写的是 BYTE_COLOR，
                    // 但"写了"不等于"FBX 带过来了"—— 少了这一段，
                    // "树一动不动"这个故障只能靠肉眼盯动画才发现。
                    // 期望：R 从干的 0 铺到叶梢的 255；B>200 的顶点数 = 叶片顶点数。
                    var c32 = m.colors32;
                    if (c32 == null || c32.Length == 0)
                    {
                        s_palmDiag += " vcol=EMPTY!! 风权重没过来，树不会动";
                    }
                    else
                    {
                        int rmin = 255, rmax = 0, leafy = 0;
                        for (int i = 0; i < c32.Length; i++)
                        {
                            if (c32[i].r < rmin) rmin = c32[i].r;
                            if (c32[i].r > rmax) rmax = c32[i].r;
                            if (c32[i].b > 200) leafy++;
                        }
                        s_palmDiag += " vcolR=[" + rmin + ".." + rmax + "] leafVerts=" + leafy;
                    }
                }
            }
        }

        // ═════════════════════════ 94 ═════════════════════════
        // 海蚀巨岩 + 礁石：Blender 出的 SM_Rock_Cliff_01 / SM_Rock_Reef_01
        //   （建模脚本 E:\Blender\Blender临时\build_rock.py，自证判据 rock_report.json）。
        // 两个 FBX 两个 Prefab，不是一个 FBX 装两个网格：菜单93 那条教训
        //   —— `GetComponentInChildren<MeshRenderer>()` 是单数，第二个网格会静默消失。
        //
        // 为什么分四个材质槽而不是一张岩石贴图：巨岩的"层"是**几何**（六层台地），
        //   要的是"越高的层晒得越白"这种随层号走的反照率，一张 UV 贴图对不上台地。
        //   苔单独一槽是因为 URP Lit 没有坡度混合通道，而且垂出台阶唇的那圈
        //   必须有真厚度才读得出"一坨苔"，顶点色在 flat shading 下只会糊成一条线。
        const string CliffFbx = FbxFolder + "/SM_Rock_Cliff_01.fbx";
        const string ReefFbx  = FbxFolder + "/SM_Rock_Reef_01.fbx";

        [MenuItem("Tools/Islet/94 导入海蚀巨岩与礁石(材质+Prefab+摆放)")]
        public static void PlaceRocks()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU94 REFUSED: 在 Play 模式\n"); return; }
            if (!GuardIsletScene("MENU94")) return;

            var cliffFbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(CliffFbx);
            var reefFbx  = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(ReefFbx);
            if (cliffFbx == null || reefFbx == null)
            {
                WriteProbe("MENU94 REFUSED: 找不到 FBX cliff=" + (cliffFbx != null) +
                           " reef=" + (reefFbx != null) + "\n");
                return;
            }

            // 和菜单93 同一个坑同一个修法：FBX 容器自带 0.01 的文件比例，
            // 正解是**忽略文件比例 + globalScale=1**，不是拿 100 去乘。
            // 且必须在建 Prefab 之前 —— Prefab 存的是导入后的网格。
            foreach (var p in new[] { CliffFbx, ReefFbx })
            {
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) continue;
                mi.useFileScale = false;
                mi.globalScale  = 1f;
                // ⚠ **巨岩一直是在地上躺着的**，这不是材质问题：
                //   建模侧 height_m=34、Blender dims=[26.78, 24.96, 34.0]（高度在 z），
                //   而 Unity 导入回来 sizeL=(26.78, 24.96, 34.00) —— **轴顺序一模一样**，
                //   说明 Blender 的 Z-up→Y-up 根本没烘进网格，34 那根"高度"轴躺在水平方向。
                //   于是画面里它像个横放的木桶，而本该是水平台阶的苔带
                //   （普查 moss avg_nz=0.68，建模侧是对的）被侧躺投影成了"竖条纹"。
                //   之前几轮把它当材质/凹槽问题调，方向整个是歪的。
                //   属性名按 2022.3 UnityEditor.xml 核过：ModelImporter.bakeAxisConversion。
                mi.bakeAxisConversion = true;
                // ⚠ 名字全部按 2022.3 的 UnityEditor.xml 核过，不是凭记忆：
                //   属性叫 **importNormals**（类型 ModelImporterNormals：None/Import/Calculate），
                //   角度叫 **normalSmoothingAngle**。
                //   上一版我写的 `mi.normals` / `mi.smoothingAngle` **根本不存在**（CS1061），
                //   而一个编辑器脚本编译不过会**连带冻住整个 Assembly-CSharp-Editor**，
                //   于是后面所有菜单都在跑**上一版 dll**，还照样回 "✅ Executed"、
                //   照样打 MENU94 那行报告 —— 本轮在这上面白烧了三轮。
                //
                // **Import 已被实测证伪，改用 Calculate**：
                //   先按"用文件法线"跑了一轮（建模侧已经 smooth_by_angle 算好、
                //   导出 mesh_smooth_type="OFF"），竖条纹**一字没变**。
                //   判据来自逐列色相：各列 H 稳在 ~196°、只有 V 在 0.19~0.87 之间跳
                //   ⇒ 这是**受光梯度**不是材质边界（之前那次 H 跳到 137° 是灌木挡在前面，
                //     我一度把它读成"材质槽被按经线切开"，也是错的）。
                //   结论：Blender 的 mesh_smooth_type="OFF" 并没有把
                //   normals_split_custom_set 的结果写进 FBX。
                //   所以不再依赖导出器行为，改成让 **Unity 按角度现算**。
                mi.importNormals = ModelImporterNormals.Calculate;
                mi.normalSmoothingAngle = 40f;   // 台肩/底盖 rim 仍保持硬，竖直接缝参与平滑
                mi.SaveAndReimport();
            }
            cliffFbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(CliffFbx);
            reefFbx  = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(ReefFbx);

            IsletTerrain.EnsureFolder(MatFolder + "/Islet");
            var white = SolidWhiteTex();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { WriteProbe("MENU94 REFUSED: URP Lit 找不到\n"); return; }

            // ── 反照率 + 镜面反射：两件事分开做，别混成一次改动 ──
            // ① 上一版渲成**青色糖纸条纹**。根因不在反照率：URP Lit 的
            //    `[ToggleOff] _SpecularHighlights` 默认 1，而 smoothness 给了
            //    0.16~0.42 ⇒ NR=30 的每个平面刻面各自把整片亮天空镜一次，
            //    相邻面法线差 12° 就是一条亮暗带 —— 竖条纹是这么来的，
            //    跟反照率无关（证据：反照率整体降 4 倍，中位只从 29 掉到 27）。
            // ⚠ 这里**没有** `_Specular` 这个属性（Lit.shader 全文只有
            //    `_SpecularHighlights`）。上一版写的 `HasProperty("_Specular")`
            //    对四个材质全部返回 false ⇒ 整段静默跳过，改了个不存在的键。
            // ⚠ 而且它是 **[ToggleOff] 关键字驱动**的：只 SetFloat(…, 0) 不会
            //    换 shader variant，必须同时 EnableKeyword("_SPECULARHIGHLIGHTS_OFF")。
            // ② 通道比例：参考的岩是 R/G≈0.30、B/G≈1.30 的**冷调深蓝灰**，
            //    上一版给的是 R/G=0.50 ⇒ 暗面发灰。苔同理，参考 (23,76,62)
            //    的 B/G=0.82，上一版只有 0.475 ⇒ 渲成荧光绿。
            // ③ 整体再压暗：上一版亮端量到 (161,209,251)，参考亮端 (23,76,62)。
            //    网格也换了（台地→柱状），大平板正对太阳的过曝面已经不存在，
            //    所以这次不能拿旧数字外推，渲完必须重新量。
            //
            // ⚠ 上一版这里是**照着过滤后的中位数反解**把四档一起提亮（×1.34/×1.42），
            //   方向是反的。原因：compare_reference.probe_box 为了把云和海从岩框里剔掉，
            //   会丢掉 lum>150 和 B-R>55 的像素 —— 而"太亮太青的岩石"恰好同时命中这两条，
            //   于是**被量具判成水的正是岩石本身**，存活下来的那 20% 只是它身上不亮的边角。
            //   拿这个自我选择的集合当中位数去反解，就会每轮都"还差一点亮度"、越调越亮。
            //   这就是"好得离谱 = 仪器坏了"那一类，只是这次坏在掩码偏置而不是节奏。
            // 这一版按色板的**线性目标**放：
            //   sea_rock  显示 (13,44,57) → 线性 (0.0040,0.0252,0.0409)
            //   moss_cap  显示 (23,76,62) → 线性 (0.0086,0.0723,0.0482)
            //   三档岩色在 sea_rock 上下留 ±40% 的小跨度（跨度太大就又变成糖果条），
            //   相对旧值是 R×0.11 G×0.10 B×0.11 —— **B 砍得和 G 一样狠**，
            //   因为旧值的问题是 B 比 G 高一大截（青），而参考帧岩体 B 只比 G 高 13。
            // ── 第 3 轮：色相已对，整体抬亮 —— 这次终于有可用的标定点 ──
            // 上一版按色板线性目标放下去之后，sea_rock 框量到
            //   保留 42%  p25(6,12,17) 中位(7,19,29) p75(9,33,48)   目标 (13,44,57)
            // 色相方向对了（B>G>R 冷调，B-G 差 10 vs 参考 13），只剩**亮度低一档**。
            // ⚠ 为什么这个数第一次能用来标定：前两轮岩石又亮又青，同时命中过滤器的
            //   两条规则（lum>150、B-R>55）⇒ **被量具判成水的就是岩石本身**，
            //   存活集里根本没有岩石。所以那两轮"反照率降 8.7 倍、中位只从 36 掉到 29"
            //   这种迟钝不是岩石不响应，是两次的存活集压根不是同一批像素。
            //   现在岩石暗下来了、能通过过滤，42% 的存活集就是岩石本体。
            // 乘子怎么定：显示要 ×1.97（B 29→57），而 sRGB + ACES toe 段
            //   显示 ≈ 线性^0.45 ⇒ 线性要乘 1.97^(1/0.45) ≈ 4.3，取 4.0。
            //   四档**同乘一个系数**：层间对比保持不变，不然又变成糖果条。
            s_rockDiag = "";
            var rockLo  = ShadedMat("Islet_Rock_Lo",  lit, new Color(0.0104f, 0.0680f, 0.1120f), 0.10f, 0.0f, white);
            var rockMid = ShadedMat("Islet_Rock_Mid", lit, new Color(0.0160f, 0.1008f, 0.1636f), 0.07f, 0.0f, white);
            var rockHi  = ShadedMat("Islet_Rock_Hi",  lit, new Color(0.0232f, 0.1440f, 0.2280f), 0.05f, 0.0f, white);
            var moss    = ShadedMat("Islet_RockMoss", lit, new Color(0.0344f, 0.2892f, 0.1928f), 0.04f, 0.0f, white);
            foreach (var mm in new[] { rockLo, rockMid, rockHi, moss })
            {
                if (mm == null || !mm.HasProperty("_SpecularHighlights")) continue;
                mm.SetFloat("_SpecularHighlights", 0f);
                mm.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                EditorUtility.SetDirty(mm);
                s_rockDiag += string.Format(" {0}[specHl={1} kw={2} sm={3}]",
                    mm.name, mm.GetFloat("_SpecularHighlights"),
                    mm.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"),
                    mm.GetFloat("_Smoothness"));
            }
            AssetDatabase.SaveAssets();

            var cliffPrefab = RockPrefab(cliffFbx, "Islet_Rock_Cliff_01", rockLo, rockMid, rockHi, moss);
            var reefPrefab  = RockPrefab(reefFbx,  "Islet_Rock_Reef_01",  rockLo, rockMid, rockHi, moss);
            if (cliffPrefab == null || reefPrefab == null) return;

            // 重跑要替换而不是再叠一份
            foreach (var old in UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>())
                if (old.name.StartsWith("Rock_") || old.name.StartsWith("Reef_"))
                    UnityEngine.Object.DestroyImmediate(old);

            int placed = 0;

            // ── 巨岩 ──
            // (64,130) 就是 IsletTerrain 那个岩核的中心（大岛中心 (52,104) 的 +x/+z 侧
            // ⇒ **背对相机**），和参考一致："前绿后岩"，岩在岛背后露头。
            // 偏航：新版网格（build_rock.py 第二版）没有"台地相位"这个方向量了 ——
            //   苔是按**台面高度**分布的，绕一圈都有，所以 yaw 只决定两件事：
            //   侧倾 LEAN_DIR=150° 倒向哪边，以及角向噪声的斑块朝向。111° 是
            //   上一版按"陡壁朝相机"算的，先留着，渲出来不满意再按侧倾重算。
            // 31m 高的网格坐在被一起改矮的岩核上（IsletTerrain 里 14.0f → 3.2f）。
            // ringCheck=10：底圈半径 10.6（× 角向起伏 0.78~1.22），量的是"脚底 vs 地形"，
            //   取 10m 正好落在**最窄**那一侧之外 ⇒ 报出来的差值是保守的。
            // ⚠ 等比 1.8 已经**被形状量否掉**：它把凸出从 35 px 抬到 158 px（目标 284），
            //   但同时把岩放成 34.4% 画幅宽，而参考只有 12.2% —— 参考的岩是**窄高**
            //   (宽高比 0.39)，我这块是矮宽 (1.76)。等比放大在错的方向上。
            //   现在水平回到 1.0，只把高度拉到 2.6：
            //     目标凸出 284 px = 13.1°；我的林线仰角 atan(11.5/150) ≈ 4.4°
            //     ⇒ 岩顶要到 17.5° ⇒ 离地 tan(17.5°)·188 ≈ 56 m
            //     ⇒ 现在露出地面 20 m ⇒ 2.6 倍。渲完用同一把尺复量，不看感觉。
            PlaceRock(cliffPrefab, "Rock_Monolith", new Vector3(64f, 0f, 130f),
                      111f, 1.00f, 6.0f, 12.0f, ref placed, 2.6f);

            // ── 礁石 ──
            // 不手写坐标，改成"绕岛心朝某个方位角往外找那条 h≈0.45 的水线"：
            // 写死坐标的话下次菜单91 重长地形，它们全飘半空或沉进潟湖（菜单93 同源）。
            // 方位角约定 dir=(sin b, cos b) ⇒ b=180 是 -z（正对相机），b=270 是 -x。
            // 只撒朝向相机的这半边岸（105°~260°），背面看不见就别占顶点。
            for (int i = 0; i < 11; i++)
                PlaceReefAtWaterline(reefPrefab, "Reef_Big_" + i, 52f, 104f,
                                     105f + i * 15.5f, 4f,
                                     0.70f + 0.60f * ((i * 37 % 10) / 9f), ref placed);
            for (int i = 0; i < 5; i++)
                PlaceReefAtWaterline(reefPrefab, "Reef_Islet_" + i, -11f, -8f,
                                     118f + i * 20f, 2.5f,
                                     0.34f + 0.34f * ((i * 53 % 10) / 9f), ref placed);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU94 rocks placed=" + placed +
                       "  prefab=Islet_Rock_Cliff_01+Islet_Rock_Reef_01" + s_rockDiag + "\n");
        }

        static string s_rockDiag = "";

        /// <summary>从一个单网格 FBX 造 Prefab，材质槽**按槽名**绑（序号会随 Blender 创建顺序变）。</summary>
        static UnityEngine.GameObject RockPrefab(UnityEngine.GameObject fbx, string nm,
                                                 Material lo, Material mid, Material hi, Material moss)
        {
            string pp = FbxFolder + "/" + nm + ".prefab";
            var tmp = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(fbx);
            var r = tmp.GetComponentInChildren<MeshRenderer>();
            if (r == null)
            {
                UnityEngine.Object.DestroyImmediate(tmp);
                WriteProbe("MENU94 REFUSED: " + nm + " 里没有 MeshRenderer\n");
                return null;
            }
            var src = r.sharedMaterials;
            var bound = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string n = src[i] != null ? src[i].name : "";
                bound[i] = n.Contains("Moss") ? moss
                         : n.Contains("Hi")   ? hi
                         : n.Contains("Mid")  ? mid : lo;
            }
            r.sharedMaterials = bound;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;

            // "绑了 4 个材质"不等于"网格有 4 个子网格"：Blender 侧礁石的高层面积是 0，
            // Unity 可能只给 3 个 subMesh，第 4 个材质就永远用不上 —— 不报错，只是白绑。
            // 所以把 verts/sub/slots/尺寸/最低点打出来，尺寸对不对、原点在哪一目了然。
            var mfc = r.GetComponent<MeshFilter>();
            var m = mfc != null ? mfc.sharedMesh : null;
            s_rockDiag += " " + nm + "[verts=" + (m == null ? 0 : m.vertexCount) +
                          " sub=" + (m == null ? 0 : m.subMeshCount) +
                          " slots=" + src.Length +
                          " sizeL=" + (m == null ? "n/a" : m.bounds.size.ToString("F2")) +
                          " minL=" + (m == null ? "n/a" : m.bounds.min.ToString("F2")) + "]";

            PrefabUtility.SaveAsPrefabAsset(tmp, pp);
            UnityEngine.Object.DestroyImmediate(tmp);
            return AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(pp);
        }

        /// <summary>
        /// 摆一块岩：y 一律问 LandHeight，并且用**网格自己的 bounds.min** 反推落地高度，
        /// 不假设原点在哪 —— 巨岩原点在轴心、最低点 -3，礁石原点在几何中心偏上、最低点
        /// 约 -0.8，两者差一套约定，写死偏移必错一个。
        /// ringCheck&gt;0 时在这圈半径上量"脚底 vs 地形"，正数=埋得住，负数=悬空（会看见缝）。
        /// </summary>
        static void PlaceRock(UnityEngine.GameObject prefab, string nm, Vector3 xz,
                              float yaw, float scale, float bury, float ringCheck,
                              ref int placed, float yScale = 1f)
        {
            if (prefab == null) return;
            var mfc = prefab.GetComponentInChildren<MeshFilter>();
            var m = mfc != null ? mfc.sharedMesh : null;
            if (m == null) { s_rockDiag += " " + nm + ":NO-MESH"; return; }

            // ⚠ yScale 只拉长**高度**，水平不动。存在的理由：探针量到
            //   参考的岩体是"窄高"（占画幅宽 12.2%、高 229 px、宽高比 0.39），
            //   我这块是"矮宽"（34.4% 宽、142 px、宽高比 1.76）。
            //   等比放大 1.8 把凸出从 35 px 抬到 158 px（目标 284），
            //   但同时把岩放得更宽 —— 方向错，所以要的是**只长高**。
            float sy = scale * yScale;
            float h = IsletTerrain.LandHeight(xz.x, xz.z);
            float y = h - bury - m.bounds.min.y * sy;

            var g = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.name = nm;
            g.transform.position = new Vector3(xz.x, y, xz.z);
            g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            g.transform.localScale = new Vector3(scale, sy, scale);
            placed++;

            float footY = y + m.bounds.min.y * sy;   // ⚠ 用 sy：脚底随高度缩放一起动
            if (ringCheck > 0f)
            {
                float worst = float.MaxValue;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    float t = IsletTerrain.LandHeight(xz.x + Mathf.Sin(a) * ringCheck,
                                                      xz.z + Mathf.Cos(a) * ringCheck);
                    worst = Mathf.Min(worst, t - footY);
                }
                s_rockDiag += " " + nm + "[h=" + h.ToString("F1") + " foot=" + footY.ToString("F1") +
                              " terrainMinusFoot@R" + ringCheck.ToString("F0") + "=" + worst.ToString("F1") +
                              (worst < 0f ? " 悬空!!" : " 埋得住") + "]";
            }
            else
                s_rockDiag += " " + nm + "[h=" + h.ToString("F1") + " foot=" + footY.ToString("F1") + "]";
        }

        /// <summary>绕 (cx,cz) 沿方位角往外找水线（h≈limit 那一圈），在那儿摆一块礁石。</summary>
        static void PlaceReefAtWaterline(UnityEngine.GameObject prefab, string nm,
                                         float cx, float cz,
                                         float bearing, float jitter, float scale, ref int placed)
        {
            if (prefab == null) return;
            float rad = bearing * Mathf.Deg2Rad;
            float dx = Mathf.Sin(rad), dz = Mathf.Cos(rad);
            for (float rr = 4f; rr < 90f; rr += 0.5f)
            {
                // 只往**岸外**找：还在岸上就继续走，一走出去就落定。
                // limit 给 0.45 而不是 0：礁石要**泡在脚边浅水里**才是参考那种黑石头，
                // 落在 h=0 那条线上等于埋进水面膜里，反而看不见。
                if (IsletTerrain.LandHeight(cx + dx * rr, cz + dz * rr) > 0.45f) continue;
                float r2 = rr + jitter;
                PlaceRock(prefab, nm, new Vector3(cx + dx * r2, 0f, cz + dz * r2),
                          (bearing * 7f) % 360f, scale, 0.55f * scale, 0f, ref placed);
                return;
            }
            s_rockDiag += " " + nm + ":skip-no-waterline@" + Mathf.RoundToInt(bearing);
        }

        // ═════════════════════════ 96 ═════════════════════════
        // 热带植被：Blender 出的 SM_Bush_Tropical_01 / SM_Grass_Tuft_01
        // （`E:\Blender\Blender工程\scrub\`，由 scrub_pipeline.py 导出）。
        //
        // 叶片材质**复用 Tree_Leaf**（和椰树同一条理由）：建模侧已经按
        //   R=flex / G=phase / B=flutter 写进顶点色，换 shader 等于把
        //   背光透射 + 风摆动那套重做一遍。scrub_pipeline 的回读判据
        //   vcol_survived 就是专门确认 "Col" 真的跟着 FBX 出来了 ——
        //   Blender 物体上有 color_attributes ≠ FBX 里有。
        static string s_scrubDiag = "";
        const string BushFbx = FbxFolder + "/SM_Bush_Tropical_01.fbx";
        const string TuftFbx = FbxFolder + "/SM_Grass_Tuft_01.fbx";

        [MenuItem("Tools/Islet/96 导入热带植被(灌木丛+草团，撒到大岛绿肩)")]
        public static void PlaceScrub()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU96 REFUSED: 在 Play 模式\n"); return; }
            if (!GuardIsletScene("MENU96")) return;

            var bushFbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(BushFbx);
            var tuftFbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(TuftFbx);
            if (bushFbx == null || tuftFbx == null)
            { WriteProbe("MENU96 REFUSED: 找不到 " + BushFbx + " 或 " + TuftFbx + "\n"); return; }

            // 和菜单93/94 同一个坑：FBX 容器自带 0.01 的文件比例，
            // 正解是忽略文件比例 + globalScale=1，且必须在建 Prefab 之前。
            foreach (var p in new[] { BushFbx, TuftFbx })
            {
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) continue;
                mi.useFileScale = false;
                mi.globalScale  = 1f;
                mi.SaveAndReimport();
            }
            bushFbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(BushFbx);
            tuftFbx = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(TuftFbx);

            IsletTerrain.EnsureFolder(MatFolder + "/Islet");
            var white = SolidWhiteTex();
            var leafSh = Shader.Find("WaterProject/TreeLeaf");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (leafSh == null) WriteProbe("!! MENU96 TreeLeaf NOT FOUND —— 植被会退回 URP Lit，不会随风动\n");

            // 反照率按色板 foliage_lit (50,112,71) 的**通道比例**反解，不是目测：
            //   R/G = 0.45、B/G = 0.63。上一版椰叶 (0.40,0.66,0.26) 的 B/G 只有
            //   0.39 ⇒ 同样亮度下偏"荧光酸绿"。灌木在参考里是**背光的深绿肩**，
            //   亮度取 foliage_lit 与 foliage_dark 之间，比椰叶暗一档。
            //
            // ── 第 3 轮"压过曝"**已回退**，两个独立原因 ──
            //   ① **画面退步**：底色 0.210→0.160、透光 1.2→0.55 之后，
            //     灌木从"看得见的一坨薄荷绿"变成几乎看不见，150m 外不成质量，
            //     岛又退回一片白 —— 把刚建好的绿肩抹掉了，比"过曝"更糟。
            //   ② **量具同时也不成立**：far_sharp 的掩码是"绿通道最大"，
            //     而我把叶子压暗恰恰会让很多像素**掉出这个掩码**
            //     （颜色样本 21438 → 16085，掉了 25%）。
            //     所以"亮度 p50 212→214 没动"是**换了一批像素**的结果，
            //     不是"压亮度无效"的证据。这正是《备忘》第 4 条：
            //     改动把像素搬出样本 ⇒ 改进会被读成退步，退步也会被读成没动。
            //   ⇒ "远处植被是不是过曝"这个问题**仍然开着**，
            //     但要用一个**不依赖被调量**的掩码重做
            //     （例如按物体名只量 Bush_/Tuft_ 的像素，或按屏幕位置固定框）。
            var scrub = ShadedMat("Islet_ScrubLeaf", leafSh != null ? leafSh : lit,
                                  new Color(0.210f, 0.460f, 0.290f), 0.35f, 0.0f, white);
            if (leafSh != null)
            {
                // 风参数和椰树同一档 —— 全场必须像被同一阵风吹
                scrub.SetFloat("_WindStrength", 0.16f);
                scrub.SetFloat("_WindFreq",     0.95f);
                scrub.SetVector("_WindDir", new Vector4(0.92f, 0f, 0.32f, 0f));
                scrub.SetFloat("_TransStrength", 1.2f);
                scrub.SetFloat("_TransPower",   8.0f);
                scrub.SetFloat("_Cutoff",       0.35f);
                scrub.SetColor("_TransColor", new Color(0.42f, 0.66f, 0.24f));
                // ⚠ 必须写在代码里：菜单96 整份重写这个材质，只 sed 改 .mat
                //   下一次重建灌木就静默丢掉。
                // 薄片双面受光开成 1 是**量出来的**（颜色无关掩码 = _Dbg=5 开/关差分）：
                //   0 → 植物像素里 **33.0%** 近黑（亮度<25），中位 (70,150,93)
                //   1 → 近黑降到 **12.2%**，中位 (43,94,61)
                // 机理在 shader 里：_SheetLit=0 走 Nview（朝相机翻正），相机平视
                //   ⇒ 翻正后的法线接近水平 ⇒ 太阳在高处 ⇒ ndl≈0 ⇒ 只剩 SampleSH，
                //   而 SH 取的是偏蓝的下半球 ⇒ 那 33% 的近黑里 B≈G。
                // 顺带记一条**证伪**：_TransStrength 从 1.2 打到 0，中位只动
                //   (43,94,61)→(44,96,62)，G 通道饱和率 19.2%→19.3% —— 没动。
                //   因为 backlit = pow(dot(-sunDir, V), p)，而色板 composition 里
                //   太阳在**相机后侧** ⇒ 这一项处处≈0。所以"透射把叶子打过曝"
                //   这个猜测在本场景不成立，透射是个死项（潭线逆光才吃它）。
                scrub.SetFloat("_SheetLit",     1.0f);
            }
            EditorUtility.SetDirty(scrub);
            AssetDatabase.SaveAssets();

            s_scrubDiag = "";
            var bushPrefab = LeafPrefab(bushFbx, "Islet_Bush_Tropical_01", scrub);
            var tuftPrefab = LeafPrefab(tuftFbx, "Islet_Grass_Tuft_01", scrub);
            if (bushPrefab == null || tuftPrefab == null) return;

            foreach (var old in UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>())
                if (old.name.StartsWith("Bush_") || old.name.StartsWith("Tuft_"))
                    UnityEngine.Object.DestroyImmediate(old);

            // 撒点：大岛 (52,104) r=44，绿肩在**背对相机**的那半边缓坡。
            // 方位角沿用 PlaceReefAtWaterline 的约定 dir=(sin b, cos b) ⇒ b=0 是 +z（岛背后）。
            // 相机在 -z 侧，正面那半边要留成参考那样的白沙滩。
            //
            // ⚠ 尺寸/密度这一档是**量出来的**，不是调口味：肩区框量 foliage_lit 时
            //   只有 **9%** 的像素通过过滤（剩下 91% 还是沙和天），而通过的这部分
            //   p75 (45,99,63) 已经贴到目标 (50,112,71)、中位却只有 (13,29,22)。
            //   ⇒ 颜色是对的，**单株太小太稀**：1.3m 的灌木在 160m 外撑不出轮廓，
            //     岛脊上自然还是白的。所以放大 + 加密 + 放宽方位窗一起给。
            var rnd = new System.Random(20260922);
            int placed = 0, rejH = 0, rejSlope = 0, rejCore = 0, tried = 0, rejRock = 0, rejBear = 0;
            string s_wedgeFirst = "";
            float wedgeMax = -9999f;
            // ── 巨岩剪影闸 ──
            // 实测（本轮基线）：Rock_Monolith 的屏幕框 (0.596,0.382,0.788,0.560) 里
            // **51.4%** 的像素是灌木 ⇒ 26 m 的海蚀巨岩只露出 2.8% 画幅高的一条脊，
            // 而参考里它是右岛的**主体**。所以"该看见岩的那块屏幕"不许再撒植物。
            // 判的是**植物顶**不是植物根：相机 1.72 m 平视、灌木连scale有 ~5 m 高，
            // 根部还在岩脚下、梢部已经压在岩面上，一样把轮廓吃掉。
            var vcam = Camera.main;
            Vector2 rockC = Vector2.zero; float rockDist = 0f, rockHalf = 0f, rockMid = 0f, camY = 0f;
            Vector2 eyeXZ = Vector2.zero;
            bool haveRock = vcam != null &&
                            MonolithWedge(vcam, out rockC, out rockDist, out rockHalf, out rockMid);
            if (vcam != null)
            {
                eyeXZ = new Vector2(vcam.transform.position.x, vcam.transform.position.z);
                camY = vcam.transform.position.y;
            }
            if (vcam == null) WriteProbe("MENU96 !! 没有 Camera.main —— 巨岩方位闸跳过\n");
            else if (!haveRock) WriteProbe("MENU96 !! 找不到 Rock_Monolith —— 方位闸跳过（先跑菜单94）\n");
            // 覆盖率现在已知是**主要缺口**（见下面 sc 那段的实测：岛体中位亮度
            //   我 197 vs 参考 30），但这里**先不顺手改**：
            //   原来这个抬密度是和"缩小单株"配对的，那个改动已被测量判死回退，
            //   再留着一个未验证的第二变量，就又是"一次改三个参数"的老错。
            //   下一轮把"加密/放宽高度窗"当**单变量**做，改前后各量一次固定框中位。
            const float PlantTop = 5.0f;
            // 一株植被"顶到地面"多高，给上面的高度线用：
            //   最大一档灌木 = sc 上限 4.0 × 网格高 1.64 ≈ 6.6 m，
            //   但埋深 bury = 0.45·sc ≈ 1.8 m，外圈叶还往下垂 ⇒ 可见顶高 ≈ 5 m。
            //   取最大档是**保守**的：宁可多拒一株，也别让梢部压在岩轮廓上。
            for (int i = 0; i < 5000 && placed < 700; i++)
            {
                float b = (float)(rnd.NextDouble() * 140.0 - 70.0);
                // 22~45 → **4~44**：原来只扫外环，岛心 22 m 圆盘全空（见下面高度闸那段）。
                float rr = (float)(4.0 + rnd.NextDouble() * 40.0);
                float x = 52f + Mathf.Sin(b * Mathf.Deg2Rad) * rr;
                float z = 104f + Mathf.Cos(b * Mathf.Deg2Rad) * rr;
                tried++;

                float h = IsletTerrain.LandHeight(x, z);
                // 上限 6.2→8.5、半径 22~45→4~44：这两条是**算出来的**，不是试出来的。
                //   ShoreProfile(s,34,26,5.0,16,3.4) 在岛内饱和到 26*0.055+5.0 = **6.43 m**，
                //   再叠 `+4.0*clamp01((s-34)/22)`（岛心 s≈44 ⇒ +1.82）≈ **8.25 m**。
                //   所以老上限 6.2 把**整片岛顶高原**都拒了，植物只拿到沙滩到肩膀那条窄带。
                //   而 rr=22+23 只扫外环，岛半径 44 ⇒ **中心 22 m 圆盘（占岛面积 25%）一株没有**。
                //   ⇒ 这就是"岛基本是座白沙丘"（岛体固定框中位亮度 我 197 / 参考 30）的成因。
                // 岩核不靠这条高度闸保护：另有"距 (64,130) <13 m"的距离闸 + 坡度闸 0.55。
                if (h < 0.9f || h > 8.5f) { rejH++; continue; }
                if (SlopeAt(x, z) > 0.55f) { rejSlope++; continue; } // 陡坡留裸岩，草站不住
                if (Mathf.Sqrt((x - 64f) * (x - 64f) + (z - 130f) * (z - 130f)) < 13f)
                { rejCore++; continue; }                              // 别把巨岩埋进灌木里
                if (haveRock)
                {
                    // 三维方位闸：候选比岩**近**、且和岩落在对相机的同一条视线上
                    // （夹角 < 岩自身的角半径）⇒ 它会挡在岩前面，吃掉岩对着天空的轮廓。
                    // 屏幕矩形表达不了"谁在谁前面"，所以这条只能在 XZ 里做。
                    var toP = new Vector2(x, z) - eyeXZ;
                    var toR = rockC - eyeXZ;
                    float dp = toP.magnitude;
                    if (dp < rockDist && dp > 0.01f)
                    {
                        float cross = toP.x * toR.y - toP.y * toR.x;
                        float dot   = Vector2.Dot(toP, toR);
                        // sin(夹角) < sin(角半径) 且同向（dot>0）才算挡住；
                        // 用叉积/点积求角，不走 acos（ acos 在 0/180° 附近数值退化）
                        float sinAng = Mathf.Abs(cross) / (dp * rockDist);
                        float sinHalf = rockHalf / Mathf.Sqrt(rockHalf * rockHalf + 1f);
                        if (dot > 0f && sinAng < sinHalf)
                        {
                            // 再加一条**高度线**：从相机看向"岩露出地面那段的半高"
                            // 的这条视线，拉到植物所在距离上离地多高。植物顶低于它，
                            // 就只挡到岩的**下半截** —— 参考里岩脚前面本来就有绿，
                            // 上一版没有这条，把岩脚前的沙滩一起清成了大白楔子。
                            float lineY = camY + (rockMid - camY) * (dp / rockDist);
                            rejBear++;
                            // 诊断：521 命中方位、0 被判太高 —— 光看 0 分不出是
                            // lineY 算高了还是植物顶算低了，所以把**第一个样本**和
                            // 整轮的**最大余量**都打出来（不等到真拒才打）。
                            if (rejBear == 1)
                                s_wedgeFirst = " 首样本: dp=" + dp.ToString("0") + "/" +
                                               rockDist.ToString("0") +
                                               " lineY=" + lineY.ToString("0.0") +
                                               " 顶=" + (h + PlantTop).ToString("0.0") +
                                               " 岩半高=" + rockMid.ToString("0.0");
                            float margin = h + PlantTop - lineY;
                            if (margin > wedgeMax) wedgeMax = margin;
                            if (margin > 0f) { rejRock++; continue; }
                        }
                    }
                }

                bool tuft = rnd.NextDouble() < 0.45;
                // ⚠ 这里**试过错，已回退**：上一版把尺度从 2.20~4.00 压到 1.00~1.80，
                //   理由是"灌木 FBX 本来就只有 3.25 m 宽 / 1.29 m 高，乘 4 倍会长成
                //   13 m 宽、41 片叶子散成尖扇"。当时用固定框量岛体（x0.560~0.870,
                //   y0.500~0.560）得出"我 71.5% 像素亮爆 ⇒ 岛是座白沙丘 ⇒ 约束是覆盖率"，
                //   于是把缩小判死了。
                // ✗ **那条判决本身不成立**：y0.500~0.560 这个框是**从截图上手画的**，
                //   后来用菜单99 从场景数据算 vegbox 时证明它压在**白沙带**上，
                //   量到的"亮爆 71.5%"就是沙。所以"覆盖率才是约束"是拿错的框推出来的。
                // ✓ 本轮换成**颜色无关**的掩码（_Dbg=5 常亮品红，在 MixFog 之前 return，
                //   只跟"这个像素有没有植物片元赢下深度测试"有关）重测：
                //     植物像素占 vegbox **42.6%**；岛面上沙只占 **1.6%**，而参考是 **11.7%**。
                //   ⇒ 覆盖率**不是**缺口（反而偏满）。缺口是**剪影形状**：
                //     轮廓完全由最外圈叶梢决定，渲出来岛脊是一道锯齿绿墙。
                //   所以这一轮**不动尺度**（动了就得靠加数量补覆盖，又回到多变量），
                //   改在建模侧给灌木加一个**叶丘内核**（见 build_scrub.py 的 add_dome），
                //   让穹顶接管轮廓、叶子只负责破边。
                // ⚠ 顺带废掉一个判据：rock_probe 里"G>B>R 灰绿 %"**不能当覆盖代理**——
                //   白沙 (193,199,194) 天然满足 G>B>R，会把沙子全数成绿植（实测报 73.7%）。
                float sc  = tuft ? (float)(1.60 + rnd.NextDouble() * 1.00)
                                 : (float)(2.20 + rnd.NextDouble() * 1.80);
                // bury：灌木的**外圈叶是往下垂的**（Blender z 到 -0.45），
                // 拿 bounds.min 落地等于把垂梢插进沙里、丛心悬空 0.45m。
                // 所以按"垂深"整体往下埋，让**根**贴地而不是最低点贴地 ——
                // 乘 sc 是因为那是网格局部量，缩放后垂深同比变。
                PlaceRock(tuft ? tuftPrefab : bushPrefab,
                          (tuft ? "Tuft_" : "Bush_") + i,
                          new Vector3(x, 0f, z),
                          (float)(rnd.NextDouble() * 360.0), sc,
                          tuft ? 0.05f * sc : 0.45f * sc, 0f, ref placed);
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            WriteProbe("MENU96 scrub placed=" + placed + " tried=" + tried +
                       " 拒:高度" + rejH + " 陡坡" + rejSlope + " 岩核" + rejCore +
                       " 方位命中" + rejBear + " 其中太高" + rejRock + s_wedgeFirst +
                       " 最大余量=" + wedgeMax.ToString("0.0") +
                       " 岩心=" + (haveRock ? string.Format("({0:0.0},{1:0.0}) d={2:0.0} 半角={3:0.0}°",
                                                            rockC.x, rockC.y, rockDist,
                                                            Mathf.Rad2Deg * rockHalf) : "无") +
                       " cam=" + Was(vcam != null ? vcam.transform.position : Vector3.zero) +
                       " 叶=" + (leafSh != null ? "TreeLeaf" : "LIT(风失效!)") +
                       s_scrubDiag + "\n");
        }

        /// <summary>FBX → 单材质 Prefab。一个 FBX 一个物体，不然
        /// GetComponentInChildren&lt;MeshRenderer&gt;() 只认第一个，第二个静默消失。</summary>
        static UnityEngine.GameObject LeafPrefab(UnityEngine.GameObject fbx, string nm, Material leaf)
        {
            string pp = FbxFolder + "/" + nm + ".prefab";
            var tmp = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(fbx);
            var r = tmp.GetComponentInChildren<MeshRenderer>();
            if (r == null)
            {
                UnityEngine.Object.DestroyImmediate(tmp);
                WriteProbe("MENU96 REFUSED: " + nm + " 里没有 MeshRenderer\n");
                return null;
            }
            var src = r.sharedMaterials;
            var bound = new Material[src.Length];
            for (int i = 0; i < bound.Length; i++) bound[i] = leaf;
            r.sharedMaterials = bound;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;

            var mfc = r.GetComponent<MeshFilter>();
            var m = mfc != null ? mfc.sharedMesh : null;
            s_scrubDiag += " " + nm + "[verts=" + (m == null ? 0 : m.vertexCount) +
                           " slots=" + src.Length +
                           " sizeL=" + (m == null ? "n/a" : m.bounds.size.ToString("F2")) +
                           " minL=" + (m == null ? "n/a" : m.bounds.min.ToString("F2")) + "]";

            PrefabUtility.SaveAsPrefabAsset(tmp, pp);
            UnityEngine.Object.DestroyImmediate(tmp);
            return AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(pp);
        }

        /// <summary>地面在该点的坡度（|梯度|，无量纲）。1.2m 中心差分，
        /// 比单点差稳，又不至于把沙滩的小起伏全抹平。</summary>
        static float SlopeAt(float x, float z)
        {
            const float e = 1.2f;
            float gx = (IsletTerrain.LandHeight(x + e, z) - IsletTerrain.LandHeight(x - e, z)) / (2f * e);
            float gz = (IsletTerrain.LandHeight(x, z + e) - IsletTerrain.LandHeight(x, z - e)) / (2f * e);
            return Mathf.Sqrt(gx * gx + gz * gz);
        }

        /// <summary>巨岩对相机的"方位遮蔽楔"：岩心 XZ、岩心到相机的距离、岩的角半径。
        /// 撒点侧的判据是三维的 —— 植物只有**比岩近、且和岩在同一方位**，
        /// 才会吃掉岩对着天空的那圈轮廓。
        /// ⚠ 屏幕矩形那条路已经被实测否掉，两次都不对：
        ///   · 不切底边 → 矩形盖到沙滩，2200 拒 1290、只撒下 49 株（绿肩全没）；
        ///   · 按"岩心处地面 + 林线"切底边 → <b>rejRock=0</b>，闸完全失效。
        ///     因为近处的高原林线**投得比岩脚更高**（同世界高度、更近 ⇒ 仰角更大），
        ///     真正压住岩轮廓的那批植物全落在切完的矩形**上沿之外**。
        ///   屏幕空间表达不了"谁在谁前面"，这个关系只在三维里成立。</summary>
        static bool MonolithWedge(Camera cam, out Vector2 c, out float dist,
                                  out float halfAng, out float yMid)
        {
            c = Vector2.zero; dist = 0f; halfAng = 0f; yMid = 0f;
            var go = UnityEngine.GameObject.Find("Rock_Monolith");
            var r = go != null ? go.GetComponentInChildren<Renderer>() : null;
            if (r == null) return false;
            var b = r.bounds;
            var eye = new Vector2(cam.transform.position.x, cam.transform.position.z);
            c = new Vector2(b.center.x, b.center.z);
            dist = Vector2.Distance(eye, c);
            if (dist < 1f) return false;
            // 用包围盒的水平半宽当岩的半径：这块网格不是圆的，
            // 取 max(extents.x, extents.z) 是**保守**的（宁可多让开一点，也别把轮廓吃掉）
            float R = Mathf.Max(b.extents.x, b.extents.z);
            halfAng = Mathf.Atan2(R, dist);
            // ⚠ 只按方位挡会把**岩脚前的沙滩**一起清空（实测：岛正面一道大白楔子），
            //   而参考里岩脚前面是有绿的 ⇒ 要加一条高度线。
            //   但这条线取在哪里**是量出来的，不是拍的**：
            //     取"露出地面那段的半高"→ 521 个命中方位的候选里，最高的那个
            //     还差 **1.1 m** 才够到线（最大余量 = −1.1），闸等于完全不生效。
            //   参考里巨岩是**上半截对着天空**、林线只压到它下缘约 1/3，
            //   所以线取在"露出地面那段从下往上 1/3"处。
            float ground = IsletTerrain.LandHeight(c.x, c.y);
            float foot = Mathf.Max(b.min.y, ground);
            yMid = foot + (b.max.y - foot) / 3f;
            return true;
        }

        // 4×4 纯白贴图。存在的理由：Tree_Leaf 要采 _BaseMap 做 alpha 裁剪，
        // 槽位空着 URP 会渲成品红 —— 那看起来像"shader 坏了"，其实是"没贴图"。
        // 椰叶是真几何叶片（不是卡片贴图），所以给它一张不裁剪的白底就够了。
        static Texture2D SolidWhiteTex()
        {
            string p = IsletTerrain.TexFolder + "/Islet_SolidWhite.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t != null) return t;
            var tmp = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tmp.SetPixels(px); tmp.Apply();
            System.IO.File.WriteAllBytes(p, tmp.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tmp);
            AssetDatabase.ImportAsset(p);
            var imp = (TextureImporter)AssetImporter.GetAtPath(p);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.sRGBTexture = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        static Material ShadedMat(string nm, Shader sh, Color rgb, float smooth, float metal, Texture2D baseMap)
        {
            string p = MatFolder + "/Islet/" + nm + ".mat";
            if (sh == null) { WriteProbe("!! ShadedMat " + nm + " shader NULL\n"); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(sh) { name = nm }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            m.SetColor("_BaseColor", rgb);
            if (baseMap != null) m.SetTexture("_BaseMap", baseMap);
            if (m.HasProperty("_Smoothness"))  m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic"))    m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        // ═════════════════════════ 95 ═════════════════════════
        [MenuItem("Tools/Islet/95 Play 中截图(离屏固定比例，尺寸见代码)")]
        public static void CaptureIslet()
        {
            // 体积云单像素约 300 次噪声，第一帧会慢，所以等帧数给足、超时放宽
            // 钉死 1920x1080（真 16:9）。尺寸必须固定，否则
            // compare_reference 的比例具名框会随 GameView 漂移而量到不同东西
            // （实测 GameView 漂过 957x627 / 1902x836 / 898x379）。
            //
            // 关于比例，两件事分开看，别混：
            //   · 参考1 **文件**是 1920x1080 = 16:9；但它里面视频画面只有
            //     y 111..1000（顶是无色 chrome，行均亮度在 y=1002 从 108.9 塌到 16.7），
            //     即**内容区 1920x890 ≈ 2.157:1**。
            //   · 所以"按内容区取 2.157"是对图最忠实的选法；
            //     但产品目标是 16:9 的游戏，就按 16:9 钉。
            //
            // ⚠ 代价要记下来，而且**不该用改比例去绕**：
            //   16:9 比 2.157 窄，横向视野少约 18%，结果**右侧巨岩被裁到画面外**
            //   （Rock_Monolith 确实在场景里且 active）。
            //   对一款 16:9 的游戏来说这就是真的构图缺陷 —— 该改的是**场景**
            //   （相机后移 / 拉宽 fieldOfView / 把巨岩往画面内侧挪），
            //   而不是把出图比例调宽让它"看起来没问题"。
            GameViewCapture.Request("MENU95", "Docs/screenshots/_islet_view.png",
                                    4, 25.0, 1920, 1080);
        }

        // ═════════════════════════ 96 ═════════════════════════
        [MenuItem("Tools/Islet/96 切换天空调试(0正常/1仅云密度/2仅透射/3仅云高)")]
        public static void CycleSkyDebug()
        {
            var m = TropicalSky();
            if (m == null) { WriteProbe("MENU96 REFUSED: 天空材质取不到\n"); return; }
            if (!m.HasProperty("_SkyDebug"))
            { WriteProbe("MENU96 Sky_Tropical 没有 _SkyDebug 通道\n"); return; }
            int nxt = (Mathf.RoundToInt(m.GetFloat("_SkyDebug")) + 1) % 4;
            m.SetFloat("_SkyDebug", nxt);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            WriteProbe("MENU96 sky debug -> " + nxt + "\n");
        }

        // ───────── 天空材质 ─────────
        static Material TropicalSky()
        {
            string p = MatFolder + "/Sky_IsletTropical.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/Sky_Tropical");
            if (sh == null) { WriteProbe("!! Sky_Tropical NOT FOUND\n"); return null; }
            if (m == null) { m = new Material(sh) { name = "Sky_IsletTropical" }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;

            m.SetColor("_ZenithColor",  SkyZenith);
            // 地平色 == 雾色，两处只能来自同一个常量（《05》C-4 那个病的预防写法）
            m.SetColor("_HorizonColor", SkyHorizon);
            m.SetColor("_GroundColor",  SkyGround);
            m.SetFloat("_GradPow",     SkyGradPow);
            m.SetFloat("_GradNorm",    SkyGradNorm);
            // 云向阳色要**超过 1**：实测云顶只有 (159,191,223)，目标 (228,241,250)。
            // ACES 会把 1.0 上下的线性值压成浅灰蓝，只有给到 HDR 才落得回近白。
            // 这个 1.42 是**两次过冲之间夹出来的**：
            //   1.72 → sunlit_top (255,255,255) 整片爆掉（且顶穿 Bloom 阈值 1.25）
            //   1.18 → sunlit_top (181,233,255) 又太暗，R 差 47
            // ⚠ 顺带**推翻我自己上一条假设**：我以为是爆云喂给 bloom 才把
            //   sky_upper 的 R 抬到 108 的，把云压到 1.18 之后 R 还是 108 ——
            //   说明不是光晕，是**云太多**：sky_upper 取的是画幅顶缘最暗的 15%，
            //   覆盖率 0.42 时那 15% 里仍然是云而不是蓝缝。要动的是 _CloudCover。
            m.SetColor("_CloudLit",   new Color(1.420f, 1.500f, 1.600f));
            m.SetColor("_CloudShade", new Color(0.260f, 0.460f, 0.660f));
            m.SetFloat("_CloudBase",   1400f);
            m.SetFloat("_CloudTop",    4200f);
            m.SetFloat("_CloudFreq",   0.00042f);
            // 0.42 而不是 0.55：参考3/4 里云之间是**大片纯蓝**，不是连成片的阴天。
            // 阈值映射 t = lerp(0.74, 0.40, cover)，cover 越小阈值越高、云越碎。
            // 再降到 0.30：sky_upper 连测三轮 R 都卡在 108~110（目标 65），
            // 而它取的是顶缘最暗 15% —— 0.42 覆盖率下那 15% 依然是云。
            //
            // ⚠ 上面这段把 _CloudCover 写成"还没验的真旋钮"，其实**已经验过三轮**
            //   而且没推动。这次改用 shader 自带的 `_SkyDebug=3`（只留渐变、云全关）
            //   把两件事彻底分开量，拿到的是一个**夹逼区间**：
            //     云关 → sky_upper = (3,107,203)   R=**3**
            //     云开 → sky_upper = (129,179,231)  R=**129**
            //     目标            = (65,174,224)  R=**65**
            //   ⇒ 那份红**确实是云给的**（不是渐变、不是 bloom 摊的、也不是地平色爬上来，
            //     这三条分别被 _SkyDebug / scatter 试验 / GradNorm 试验排除了）。
            //   但**纯渐变又太蓝**（R 只有 3，比目标低 62），所以不能靠"把云去掉/
            //     压 cover"解决 —— 目标 65 正落在 3 和 129 中间。
            //   真正要动的是**上部那一条带里的云量或云亮度**，而不是全局 cover 阈值：
            //     cover 从 0.55 走到 0.30 都没推动 R，说明卡住它的不是阈值。
            //   （量完已把 _SkyDebug 恢复 0。）
            m.SetFloat("_CloudCover",  0.30f);
            m.SetFloat("_CloudDetail", 1.0f);
            // 云速按用户要求**降 3 倍**：(7,0,3) → (2.333,0,1)。
            // 顺带解决一个测量问题：云在两帧之间一直在移动，所以凡是"带里含云"的
            // 天空探针（sky_upper / sunlit_top）都取不到可复现的数 ——
            // 同一份配置连抓两帧，sunlit_top 能从偏差 19 跳到 69。
            // 漂得慢，帧与帧之间的云型就更接近，判据才有可比性。
            m.SetVector("_CloudDrift", new Vector4(2.333f, 0f, 1f, 0f));
            // 消光按**整列光学厚度 ≈ 6** 反推，不是凭手感：
            //   满云列厚 ≈ coverage×(顶-底) ≈ 0.8×2800 ≈ 2240 m
            //   要 tau≈6 ⇒ sigma ≈ 6/2240 ≈ 0.0027 /m
            // 上一版给 0.055 是照"每步衰减"的感觉拍的，一步 90m 就 exp(-4.9)，
            // 等于第一层就把云烧成一张不透光的白纸。
            m.SetFloat("_Extinct",     0.0028f);
            m.SetFloat("_SunPow",      2.6f);
            m.SetFloat("_SilverLining",0.55f);
            m.SetFloat("_SunGlow",     0.45f);
            m.SetFloat("_Steps",       48f);
            // 云积分平均 4 个相位。 shader 里默认是 1（= 单遍，不改画面），
            // 必须在这里显式抬才生效。
            // 为什么抬：逐像素抖动是为了打掉等距切片的同心环带，但它换来的细噪点
            // **并没有**像老注释说的那样"被雾与压缩吃掉" —— 实测天空带高频能量
            // 3.83，参考帧只有 1.26（3 倍）。多相位平均能在保住"无环带"的同时
            // 把噪点抵消掉（相位仍逐像素打散，只是每像素取 4 个相位求均值）。
            m.SetFloat("_MarchPasses", 4f);
            m.SetFloat("_ShadeSteps",  4f);
            // 整链重建一律退回正常视图，别把上一轮调试通道留在盘上
            m.SetFloat("_SkyDebug",    0f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        // ───────── 后处理 ─────────
        // ⚠ `prof.Add<T>()` 单独用**不落盘**（《05》★B-1）：Add 只把组件挂在
        //   ScriptableObject 的内存对象上，不登记成子资产，保存后 profile 里
        //   只剩 fileID:0 的空槽，整条后处理静默空操作 —— 曾经因此连续几轮
        //   怀疑"ACES 把画面压暗了"，而 ACES 根本没生效。
        //   必须 Add 之后立刻 AssetDatabase.AddObjectToAsset。
        static UnityEngine.Rendering.VolumeProfile IsletVolume()
        {
            string p = "Assets/Settings/URP/IsletVolume.profile.asset";
            var prof = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(p);
            if (prof == null)
            {
                prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(prof, p);
            }
            prof.components.RemoveAll(c => c == null);

            if (!prof.TryGet<Tonemapping>(out var ton))
            { ton = prof.Add<Tonemapping>(); AssetDatabase.AddObjectToAsset(ton, prof); }
            ton.mode.overrideState = true; ton.mode.value = TonemappingMode.ACES;

            // 参考帧的饱和度极高（天顶 (3,118,200) —— R 几乎为 0）。ACES 会把饱和色
            // 往灰里压（《05》C-7），所以这里 saturation 给得比海岸线（14）更高。
            // 曝光先给 1.0：上一轮 0.85 是为了压住开海的大片高光，
            // 这里水面面积小、天空要保住蓝色，压狠了天就泛白。
            if (!prof.TryGet<ColorAdjustments>(out var col))
            { col = prof.Add<ColorAdjustments>(); AssetDatabase.AddObjectToAsset(col, prof); }
            col.postExposure.overrideState = true; col.postExposure.value = 1.00f;
            col.saturation.overrideState = true;   col.saturation.value = 22f;
            col.contrast.overrideState = true;     col.contrast.value = 8f;

            // 阈值高于 1 只吃云顶和太阳；低于 1 会把整片天空一起提亮，画面立刻发灰
            if (!prof.TryGet<Bloom>(out var bl))
            { bl = prof.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bl, prof); }
            bl.threshold.overrideState = true;  bl.threshold.value = 1.25f;
            bl.intensity.overrideState = true;  bl.intensity.value = 0.35f;
            // ⚠ scatter 0.55 → 0.35 的试验**已回退**：假设被证伪了。
            //   想验的是"天空彩度铺不开是不是 bloom 摊白洗的"，
            //   实测天空带彩度 p90 **0.433 → 0.434**、p50 0.398 → 0.391 ——
            //   等于没动。（高频那个数也从 2.32 变 2.66，但这次 GameView
            //   自己漂到 1902×836 了，二阶差分的量纲随分辨率变，不作数。）
            //   ⇒ 彩度平不是 bloom 摊的。真正的签名是**上部天空红超标**
            //     （sky_upper R 129 vs 目标 65、sky_mid R 137 vs 100），
            //     而 R 高本身就等于 S 低。红从哪来：GradNorm 已实测无关，
            //     剩下的就是**云铺满了那条带**（本文件 _CloudLit 处早就写着
            //     "要动的是 _CloudCover"）。下一轮单独验 _CloudCover。
            bl.scatter.overrideState = true;    bl.scatter.value = 0.55f;

            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        static string Was(Vector3 v)
        {
            return string.Format("({0:0.00},{1:0.00},{2:0.00})", v.x, v.y, v.z);
        }

        // 所有 Islet 菜单共用的场景闸门。
        // 起因：一次脚本编译后的域重载把活动场景换成了 PondScene，菜单98 照跑不误，
        //   把 Islet 的雾色/雾密度/天光模式/天空盒/Volume/太阳俯角/相机 全写进 PondScene
        //   并且**存了盘**。素材库早就记过一条同类教训（海岸/潭/Main 资产前缀撞了会互相
        //   覆盖，而且两个场景都"能渲染"）—— 这次撞的是场景，代价一样贵。
        internal static bool GuardIsletScene(string tag)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (sc.path == ScenePath) return true;
            WriteProbe(tag + " REFUSED: 活动场景是 " +
                       (string.IsNullOrEmpty(sc.path) ? "<未保存的新场景>" : sc.path) +
                       "，不是 " + ScenePath + " —— 先打开它\n");
            return false;
        }

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }
    }
}
#endif

