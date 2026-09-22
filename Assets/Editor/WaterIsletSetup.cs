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
        static readonly Color SkyHorizon = new Color(0.500f, 0.700f, 0.870f);
        static readonly Color SkyGround  = new Color(0.180f, 0.230f, 0.260f);
        const float SkyGradPow  = 1.4f;
        // 0.46 是**过头了**：地平色修亮之后确实把 horizon_haze 从偏差 60 拉到 38，
        // 但放宽的渐变让那片淡色一路爬进画幅顶缘，sky_upper 从 (69,149,215)
        // 变成 (114,171,219) —— 目标 R 只有 65，红超了 49。
        // 两个旋钮各管一头：**地平色管下缘亮度，GradNorm 管淡霾有多厚**。
        // 下缘已经靠颜色修好了，这里就收回去，让饱和天顶重新占住上半幅。
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
            m.SetColor("_SkyZenith",  new Color(0.120f, 0.440f, 0.700f));
            m.SetColor("_SkyHorizon", new Color(0.700f, 0.860f, 0.840f));
            m.SetFloat("_FresnelPow",  4.2f);
            m.SetFloat("_SkyReflect",  1.0f);

            m.SetColor("_SunColor", new Color(1.0f, 0.96f, 0.86f));
            // ⚠ 碎闪 shader 里**没有**再乘 mainLight.color（那会把 3.2 乘两遍），
            //   所以这里的强度就是最终峰值。
            //   1.05 仍然让近处第 78 分位爆到 (114,255,247) —— 探针要的是
            //   (120,200,190)，也就是说**星点本身不该顶到 255**。降到 0.6。
            m.SetFloat("_GlitterPow",      700f);
            m.SetFloat("_GlitterStrength",  0.6f);
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

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            { WriteProbe("MENU98 REFUSED: 当前场景没存过，先跑 90\n"); return; }

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
                       "  water=" + wmat + "\n");
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
            var mi = AssetImporter.GetAtPath(PalmFbx) as ModelImporter;
            if (mi != null)
            {
                mi.useFileScale = false;
                mi.globalScale  = 1f;
                mi.SaveAndReimport();
                var rm = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(PalmFbx);
                var rmf = rm != null ? rm.GetComponentInChildren<MeshFilter>() : null;
                s_palmDiag = " scaleFix(file=off,g=1) reimportBounds=" +
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
            s_palmDiag = "";

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

        static void PlacePalm(UnityEngine.GameObject prefab, string nm, Vector3 xz,
                              float lean, float yaw, float scale, ref int placed)
        {
            float y = IsletTerrain.LandHeight(xz.x, xz.z);
            // 落在深水里的直接跳过：那是摆到了潟湖底，树会插在水下
            if (y < 0.25f) return;
            var g = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.name = nm;
            g.transform.position = new Vector3(xz.x, y - 0.10f, xz.z);   // 吃进土 10cm，别站在水膜上
            g.transform.rotation = Quaternion.Euler(lean, yaw, 0f);
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
            // ── 第 2 轮：按实测反解，G/B 各抬一档 ──
            // 上面那组值渲完，sea_rock 框（同一套过滤，保留 42%）量到
            //   p25 (9,16,22)  中位 (12,29,36)  p75 (20,51,64)   目标中位 (13,44,57)
            // 偏差 21（上一版 53）。R 已经对上了，**G 差 1.5 倍、B 差 1.6 倍**。
            // 反解：显示值 → sRGB 解码 → 反 ACES（toe 段 out≈0.21x，别用线性外推）
            //   当前 (12,29,36) → 线性 (0.0515, 0.094, 0.109)
            //   目标 (13,44,57) → 线性 (0.0545, 0.126, 0.155)
            //   ⇒ 乘子 R×1.06  G×1.34  B×1.42，四档一起乘（层间对比保持不变）。
            s_rockDiag = "";
            var rockLo  = ShadedMat("Islet_Rock_Lo",  lit, new Color(0.036f, 0.185f, 0.255f), 0.10f, 0.0f, white);
            var rockMid = ShadedMat("Islet_Rock_Mid", lit, new Color(0.050f, 0.264f, 0.357f), 0.07f, 0.0f, white);
            var rockHi  = ShadedMat("Islet_Rock_Hi",  lit, new Color(0.069f, 0.352f, 0.479f), 0.05f, 0.0f, white);
            var moss    = ShadedMat("Islet_RockMoss", lit, new Color(0.069f, 0.381f, 0.326f), 0.04f, 0.0f, white);
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
            PlaceRock(cliffPrefab, "Rock_Monolith", new Vector3(64f, 0f, 130f),
                      111f, 1.00f, 6.0f, 10.0f, ref placed);

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
                              float yaw, float scale, float bury, float ringCheck, ref int placed)
        {
            if (prefab == null) return;
            var mfc = prefab.GetComponentInChildren<MeshFilter>();
            var m = mfc != null ? mfc.sharedMesh : null;
            if (m == null) { s_rockDiag += " " + nm + ":NO-MESH"; return; }

            float h = IsletTerrain.LandHeight(xz.x, xz.z);
            float y = h - bury - m.bounds.min.y * scale;

            var g = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.name = nm;
            g.transform.position = new Vector3(xz.x, y, xz.z);
            g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            g.transform.localScale = Vector3.one * scale;
            placed++;

            float footY = y + m.bounds.min.y * scale;
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
            var scrub = ShadedMat("Islet_ScrubLeaf", leafSh != null ? leafSh : lit,
                                  new Color(0.210f, 0.460f, 0.290f), 0.35f, 0.0f, white);
            if (leafSh != null)
            {
                // 风参数和椰树同一档 —— 全场必须像被同一阵风吹
                scrub.SetFloat("_WindStrength", 0.16f);
                scrub.SetFloat("_WindFreq",     0.95f);
                scrub.SetVector("_WindDir", new Vector4(0.92f, 0f, 0.32f, 0f));
                scrub.SetFloat("_TransStrength", 1.2f);   // 灌木比椰叶暗，透射退一档
                scrub.SetFloat("_TransPower",   8.0f);
                scrub.SetFloat("_Cutoff",       0.35f);
                scrub.SetColor("_TransColor", new Color(0.42f, 0.66f, 0.24f));
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
            // 只取 b∈[-55°,55°]：相机在 -z 侧，正面那半边要留成参考那样的白沙滩。
            var rnd = new System.Random(20260922);
            int placed = 0, rejH = 0, rejSlope = 0, rejCore = 0, tried = 0;
            for (int i = 0; i < 900 && placed < 300; i++)
            {
                float b = (float)(rnd.NextDouble() * 110.0 - 55.0);
                float rr = (float)(24.0 + rnd.NextDouble() * 21.0);
                float x = 52f + Mathf.Sin(b * Mathf.Deg2Rad) * rr;
                float z = 104f + Mathf.Cos(b * Mathf.Deg2Rad) * rr;
                tried++;

                float h = IsletTerrain.LandHeight(x, z);
                if (h < 0.9f || h > 6.2f) { rejH++; continue; }      // 太低吃浪、太高是岩核
                if (SlopeAt(x, z) > 0.55f) { rejSlope++; continue; } // 陡坡留裸岩，草站不住
                if (Mathf.Sqrt((x - 64f) * (x - 64f) + (z - 130f) * (z - 130f)) < 13f)
                { rejCore++; continue; }                              // 别把巨岩埋进灌木里

                bool tuft = rnd.NextDouble() < 0.5;
                float sc  = tuft ? (float)(0.80 + rnd.NextDouble() * 0.70)
                                 : (float)(0.70 + rnd.NextDouble() * 0.80);
                // bury：灌木的**外圈叶是往下垂的**（Blender z 到 -0.45），
                // 拿 bounds.min 落地等于把垂梢插进沙里、丛心悬空 0.45m。
                // 所以按"垂深"整体往下埋，让**根**贴地而不是最低点贴地。
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
                       "  leaf=" + (leafSh != null ? "TreeLeaf" : "LIT(风失效!)") +
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
        [MenuItem("Tools/Islet/95 Play 中截图 Game 视图")]
        public static void CaptureIslet()
        {
            // 体积云单像素约 300 次噪声，第一帧会慢，所以等帧数给足、超时放宽
            GameViewCapture.Request("MENU95", "Docs/screenshots/_islet_view.png", 4, 25.0);
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
            m.SetFloat("_CloudCover",  0.30f);
            m.SetFloat("_CloudDetail", 1.0f);
            m.SetVector("_CloudDrift", new Vector4(7f, 0f, 3f, 0f));
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
            bl.scatter.overrideState = true;    bl.scatter.value = 0.55f;

            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }
    }
}
#endif
