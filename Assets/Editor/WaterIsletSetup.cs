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
        // 方位是**算出来的**：相机 yaw = 0 ⇒ 视线方位 0°。Unity 里
        // light.transform.rotation = Euler(x, y, 0) 的 forward 是**光行进方向**，
        // "朝太阳的方向"是它的反向，即 y + 180。要让碎闪带正对画面中央，
        // 朝太阳方向必须 ≈ 视线方位 ⇒ y = 180。
        // （海岸线那台相机 yaw 58，所以那边算出 y = 236；同一套算法，不是抄来的。）
        // ⚠ 俯角从 24° 提到 38°。24° 时太阳几乎只照得到**水平顶面**，
        //   于是所有朝向相机的那一面只剩蓝色天光 —— 实测沙洲洲顶 (119,141,155)、
        //   大岛 (43,64,84)，B 都压着 R，沙子渲成了冰。参考1 的沙是 (243,230,203)
        //   暖调、植被是 (36,122,35) 绿，都要求斜射的暖光真的打到立面上。
        //   碎闪带不依赖这个俯角：静水镜面点确实只在脚前 1.72/tan(elev)，
        //   但参考里那条贯穿画面的亮带是**浪面把法线打散**铺开的，抬太阳不会弄丢它。
        const float SunPitch = 38f;
        const float SunYaw   = 180f;

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
