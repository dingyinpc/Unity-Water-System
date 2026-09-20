// WaterPondSetup.cs — 潭水演示场景（菜单 60~67）
//
// 这是《06_从零写一个水体材质》最后那个问题的答案：
//   "Water_Advanced 是把大洋的波浪统计塞进了一个水坑，而且它不照东西。"
// 于是另起一个场景，做一潭**尺寸正确的静水 + 真的倒影**。
//
// 与 Main.unity 完全隔离，不动复刻那条线。
//
// 复用约定：
//   岩石网格/放置、平面着色辅助 都来自 WaterProjectSetup（已放开 internal）。
//   岸线只写 **一处**（PondR），水盘和沙岸都调它 —— 这正是 Main 那边踩过的
//   "半径公式在四个菜单里各写一遍然后漂移"的教训（见《05》E-7）。
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WaterProject.Presentation;

namespace WaterProject.EditorTools
{
    public static class WaterPondSetup
    {
        const string ScenePath   = "Assets/Scenes/PondScene.unity";
        const string MeshFolder  = "Assets/Art/Meshes";
        const string MatFolder   = "Assets/Art/Materials";
        const float  WaterY      = 0.02f;
        const int    WaterLayer  = 4;      // TagManager 里已存在的 "Water" 层

        // ───────── 唯一岸线函数：水盘与沙岸共用，杜绝漂移 ─────────
        static float PondR(float th)
        {
            return 7.0f
                 + 0.62f * Mathf.Sin(th * 2f + 0.9f)
                 + 0.34f * Mathf.Sin(th * 3f + 2.4f)
                 + 0.16f * Mathf.Sin(th * 7f + 0.3f);
        }

        // 沙岸高度：水下 -0.10 → 在 r≈PondR+2 处冒出水面 → 岸唇 +0.95 → 远处缓升到 +1.5
        // 原先只抬到 0.30，相机几乎与岸齐平，整潭读成"沙滩上摆的浅碟"而不是"地里挖的潭"。
        static float BankY(float r, float th)
        {
            float inner = PondR(th);
            float t = Mathf.InverseLerp(inner - 0.5f, inner + 3.2f, r);
            float h = Mathf.Lerp(-0.10f, 0.95f, Mathf.SmoothStep(0f, 1f, t));
            // 岸外再缓慢隆起，避免"一张饼"
            h += Mathf.InverseLerp(inner + 3.2f, 34f, r) * 0.55f;
            h += 0.16f * Mathf.Sin(th * 5f + 1.2f) * Mathf.InverseLerp(inner, inner + 6f, r);
            return h;
        }

        // ═════════════════════════ 60 ═════════════════════════
        [MenuItem("Tools/WaterPond/60 建潭骨架(场景+盆+岸+灯+天空)")]
        public static void BuildPondShell()
        {
            // Play 模式下 NewScene 会抛 InvalidOperationException("This cannot be used during play mode")，
            // 而 runMenuItem 一律回 ✅，失败完全看不见。更糟的是 Unity 会把编译推迟到退出 Play，
            // 于是跑的还是旧代码。必须开头就挡掉。
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU60 REFUSED: 在 Play 模式，先跑 Tools/WaterProject/26 退出\n"); return; }

            // Single 模式新建：避免"未命名场景不能 Additive 创建"那个坑
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 盆底：中心 -1.45 → 边缘 -0.06（★ 必须低于水面，否则破水而出成"水泥台阶"）──
            // ⚠ 用**平滑法线**而不是 FacetedFrom：逐面法线在锥面上会画出一圈圈同心带，
            //    水一变清就成了"靶心"。盆底在水下，低多边形的硬朗感在这里没有收益。
            {
                int seg = 128, rings = 12;
                var v = new List<Vector3>(); var idx = new List<int>();
                for (int j = 0; j <= rings; j++)
                {
                    float tr = (float)j / rings;
                    for (int i = 0; i <= seg; i++)
                    {
                        float th = Mathf.PI * 2f * i / seg;
                        float r = Mathf.Lerp(0.25f, PondR(th) + 0.10f, tr);
                        // 幂 1.5：盆底趋平、向岸坡度最大 —— 挖出来的坑就是这个剖面
                        float y = -1.45f + 1.39f * Mathf.Pow(tr, 1.5f);
                        v.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                    }
                }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = j * (seg + 1) + i, b = a + 1;
                        int c = (j + 1) * (seg + 1) + i, d = c + 1;
                        idx.Add(a); idx.Add(c); idx.Add(b);
                        idx.Add(b); idx.Add(c); idx.Add(d);
                    }
                var basin = new Mesh();
                basin.vertices = v.ToArray();
                basin.triangles = idx.ToArray();
                basin.RecalculateNormals();
                basin.RecalculateBounds();
                SaveMeshAsset(basin, MeshFolder + "/PoolBasin.asset", "PoolBasin");
                AttachExistingMesh("PoolBasin", GetMat("Pond_SandWet", new Color(0.095f, 0.118f, 0.070f)));
            }

            // ── 沙岸 + 外场：r = 岸线-0.5 → 40 ──
            {
                int seg = 160;
                var rings = new List<float>();
                for (int j = 0; j <= 40; j++) rings.Add(j / 40f);
                var v = new List<Vector3>(); var idx = new List<int>();
                foreach (var t in rings)
                    for (int i = 0; i <= seg; i++)
                    {
                        float th = Mathf.PI * 2f * i / seg;
                        float inner = PondR(th);
                        float r = Mathf.Lerp(inner - 0.5f, 40f, t);
                        v.Add(new Vector3(Mathf.Cos(th) * r, BankY(r, th), Mathf.Sin(th) * r));
                    }
                int rowLen = seg + 1;
                for (int j = 0; j < rings.Count - 1; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = j * rowLen + i, b = a + 1;
                        int c = (j + 1) * rowLen + i, d = c + 1;
                        idx.Add(a); idx.Add(c); idx.Add(b);
                        idx.Add(b); idx.Add(c); idx.Add(d);
                    }
                AttachMesh("PoolBank", WaterProjectSetup.FacetedFrom(v, idx), GetMat("Pond_Sand", new Color(0.150f, 0.135f, 0.082f)));
            }

            // ── 绿坡环：把"沙漠里一个洞"变成"林间一潭" ──
            // 没有它，镜像相机基本只拍到天空，平面反射等于白做。
            {
                int seg = 200;
                float[] rp = { 13f, 18f, 30f, 60f };
                System.Func<float, float> Ridge = th =>
                    1.10f + 0.55f * Mathf.Sin(th * 3f + 1.4f) + 0.30f * Mathf.Sin(th * 7f + 0.2f);
                var v = new List<Vector3>(); var idx = new List<int>();
                for (int i = 0; i <= seg; i++)
                {
                    float th = Mathf.PI * 2f * i / seg;
                    float h = Ridge(th);
                    v.Add(new Vector3(Mathf.Cos(th) * rp[0], BankY(rp[0], th) - 0.3f, Mathf.Sin(th) * rp[0]));
                    v.Add(new Vector3(Mathf.Cos(th) * rp[1], h,        Mathf.Sin(th) * rp[1]));
                    v.Add(new Vector3(Mathf.Cos(th) * rp[2], h + 2.2f, Mathf.Sin(th) * rp[2]));
                    v.Add(new Vector3(Mathf.Cos(th) * rp[3], h + 5.2f, Mathf.Sin(th) * rp[3]));
                    if (i == seg) break;
                    int a = i * 4;
                    for (int k = 0; k < 3; k++)
                    {
                        int i0 = a + k, i1 = a + 4 + k, i2 = a + 5 + k, i3 = a + k + 1;
                        idx.Add(i0); idx.Add(i1); idx.Add(i2);
                        idx.Add(i0); idx.Add(i2); idx.Add(i3);
                    }
                }
                var hills = AttachMesh("PoolHills", WaterProjectSetup.FacetedFrom(v, idx),
                                       GetMat("Pond_Grass", new Color(0.040f, 0.130f, 0.058f)));
                var hmr = hills != null ? hills.GetComponent<MeshRenderer>() : null;
                if (hmr != null) hmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            SetupLighting();
            SetupCamera();
            WriteProbe("MENU60 shell built (pondR 6.0~8.1)\n");
        }

        // 站在岸边、几乎贴着水面平视 —— 这个角度倒影最占画面，也最容易看出反射对不对。
        static void SetupCamera()
        {
            var cam = GetOrCreate("Main Camera");
            cam.tag = "MainCamera";
            var c = cam.GetComponent<Camera>(); if (c == null) c = cam.AddComponent<Camera>();
            c.fieldOfView = 46f; c.nearClipPlane = 0.1f; c.farClipPlane = 220f;
            c.depth = 0;
            // 机位高度就是"倒影 vs 环境"的取舍旋钮：
            //   y=3.6 俯12° → 看得见绿坡和树，但水面大半在照头顶的空天，倒影只剩远岸一条窄带
            //   y=1.9 俯 7° → 掠射角覆盖到潭心，对岸的树整片倒进来
            // 潭水的主角就是倒影，所以取低机位；环境靠两侧和上方仍然入画。
            cam.transform.localPosition = new Vector3(-2.2f, 1.9f, -9.6f);
            cam.transform.localRotation = Quaternion.Euler(7f, 7f, 0f);

            var audio = cam.GetComponent<AudioListener>();
            if (audio == null) cam.AddComponent<AudioListener>();

            var cd = c.GetUniversalAdditionalCameraData();
            if (cd == null) cd = cam.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
        }

        // ═════════════════════════ 61 ═════════════════════════
        [MenuItem("Tools/WaterPond/61 潭水网格与材质")]
        public static void BuildPondWater()
        {
            // 水盘：用【同一个】PondR，所以和沙岸严丝合缝
            int seg = 128, rings = 22;
            var v = new List<Vector3>(); var idx = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float tr = (float)j / rings;
                for (int i = 0; i <= seg; i++)
                {
                    float th = Mathf.PI * 2f * i / seg;
                    float r = Mathf.Lerp(0.0f, PondR(th) - 0.10f, tr);
                    v.Add(new Vector3(Mathf.Cos(th) * r, WaterY, Mathf.Sin(th) * r));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + 1;
                    int c = (j + 1) * (seg + 1) + i, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }
            var mesh = new Mesh();
            var pos = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++) pos[i] = v[i];
            mesh.vertices = pos;
            mesh.triangles = idx.ToArray();
            mesh.RecalculateBounds();
            SaveMeshAsset(mesh, MeshFolder + "/PoolWater.asset", "PoolWater");

            var go = GetOrCreate("PoolWater");
            go.layer = WaterLayer;                       // ← 镜像相机按这层把自己剔掉
            var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/PoolWater.asset");
            mr.sharedMaterial = PondWaterMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.localPosition = Vector3.zero;

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU61 water mesh " + v.Count + " verts\n");
        }

        // ═════════════════════════ 62 ═════════════════════════
        [MenuItem("Tools/WaterPond/62 平面反射相机")]
        public static void BuildPlanarReflection()
        {
            var water = UnityEngine.GameObject.Find("PoolWater");
            var waterMr = water != null ? water.GetComponent<MeshRenderer>() : null;
            if (waterMr == null) { WriteProbe("MENU62 REFUSED: PondWater 不存在，先跑 61\n"); return; }

            // 相机由菜单60 的 SetupCamera 负责，这里不再重复配置（同一物理量只有一处写入）
            var refl = UnityEngine.GameObject.Find("PondReflectionCam");
            if (refl == null) refl = new UnityEngine.GameObject("PondReflectionCam");
            var comp = refl.GetComponent<PondPlanarReflection>();
            if (comp == null) comp = refl.AddComponent<PondPlanarReflection>();
            comp.waterHeight = WaterY;
            comp.waterRenderer = waterMr;

            WriteProbe("MENU62 reflection cam wired to " + waterMr.name + " layer" + water.layer + "\n");
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        // ═════════════════════════ 63 ═════════════════════════
        [MenuItem("Tools/WaterPond/63 岸上道具(巨石+树+卵石)")]
        public static void BuildPondProps()
        {
            // 倒影要有内容，否则"平面反射"这件事根本看不出来。
            var rng = new System.Random(202609242);

            var rockMat = GetMat("Pond_Rock", new Color(0.110f, 0.165f, 0.135f));
            var rocks = new Mesh[3];
            for (int v = 0; v < 3; v++)
                rocks[v] = WaterProjectSetup.BuildFacetedRock(v, 1f);

            foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name.StartsWith("PondRock_") || go.name.StartsWith("PondPebble_")
                                   || go.name.StartsWith("PondTree_")))
                    UnityEngine.Object.DestroyImmediate(go);

            // 5 块近岸巨石：立在水线上下，是倒影的主体之一
            int n = 0;
            for (int k = 0; k < 5; k++)
            {
                float th = Mathf.PI * 2f * k / 5f + 0.6f + (float)rng.NextDouble() * 0.3f;
                float r = PondR(th) + 0.15f + (float)rng.NextDouble() * 0.7f;
                float y = BankY(r, th);
                WaterProjectSetup.PlaceBoulder(rocks, rockMat, rng, "PondRock_", n++,
                    new Vector3(Mathf.Cos(th) * r, y + 0.35f, Mathf.Sin(th) * r), 0.55f, 1.15f);
            }

            // 40 颗卵石沿水线
            for (int cl = 0; cl < 10; cl++)
            {
                float bth = Mathf.PI * 2f * cl / 10f;
                for (int k = 0; k < 4; k++)
                {
                    float th = bth + (float)(rng.NextDouble() - 0.5) * 0.5f;
                    float r = PondR(th) + (float)(rng.NextDouble() - 0.35) * 1.6f;
                    float y = BankY(r, th);
                    WaterProjectSetup.PlaceBoulder(rocks, rockMat, rng, "PondPebble_", n++,
                        new Vector3(Mathf.Cos(th) * r, y + 0.05f, Mathf.Sin(th) * r), 0.16f, 0.34f);
                }
            }

            // 树绕潭一圈 —— 倒影要有内容，否则"平面反射"这件事根本看不出来。
            // 三棵排成一排是"布景"，七棵散开才是"林子"。
            // 树种来自 Blender 交付包（Assets/Trees/Prefabs），摆放逻辑与菜单68 共用 PlacePondTree。
            int placed = 0;
            for (int k = 0; k < 7; k++)
            {
                // 0 号固定在相机视线方向（+z 偏左），保证主倒影一定在画面里
                float th = k == 0 ? Mathf.PI * 0.5f + 0.15f
                                  : Mathf.PI * 2f * k / 7f + (float)rng.NextDouble() * 0.4f;
                // 树要**贴着岸**长。倒影占多少水面是几何问题不是着色问题：
                // 树在岸外 3.6~9.1m 时离相机 20~26m，只张 8° 仰角，就只能在远岸一条窄带里倒出来；
                // 收到 1.6~4.8m 后张角翻倍，潭面大半都能映到树。
                float r = PondR(th) + 1.6f + (float)rng.NextDouble() * 3.2f;
                if (PlacePondTree("PondTree_0" + k,
                        new Vector3(Mathf.Cos(th) * r, BankY(r, th), Mathf.Sin(th) * r),
                        rng, k, 0.85f, 1.15f) != null) placed++;
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU63 props " + n + " rocks/trees " + placed
                       + (placed < 7 ? " TREE-PREFAB-MISSING" : "") + "\n");
        }

        // ═════════════════════════ 64 ═════════════════════════
        [MenuItem("Tools/WaterPond/64 整链建潭(60→68)")]
        public static void BuildWholePond()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU64 REFUSED: 在 Play 模式，先退出\n"); return; }

            WriteProbe("MENU64 begin\n");
            var steps = new System.Collections.Generic.KeyValuePair<string, System.Action>[]
            {
                new System.Collections.Generic.KeyValuePair<string, System.Action>("60 骨架", BuildPondShell),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("61 水", BuildPondWater),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("63 道具", BuildPondProps),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("68 森林", BuildForest),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("62 反射", BuildPlanarReflection),
            };
            for (int i = 0; i < steps.Length; i++)
            {
                WriteProbe("MENU64 STEP " + steps[i].Key + "\n");
                try { steps[i].Value(); }
                catch (System.Exception e) { WriteProbe("MENU64 FAIL " + steps[i].Key + " :: " + e.Message + "\n"); }
            }
            AssetDatabase.SaveAssets();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            WriteProbe("MENU64 done -> " + ScenePath + "\n");
        }

        // ═════════════════════════ 65 ═════════════════════════
        [MenuItem("Tools/WaterPond/65 Play 中截图 Game 视图")]
        public static void CapturePond()
        {
            if (!UnityEngine.Application.isPlaying)
            { WriteProbe("MENU65 REFUSED: 不在 Play 模式\n"); return; }

            // 不在 Play 里 OpenScene —— 那会直接打断 Play 模式，截图永远拿不到。
            // 只报当前场景名，取错场景仍然能从探针看出来。
            string cur = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            string outPath = "Docs/screenshots/_pond_view.png";
            WriteProbe("MENU65 enter frame=" + UnityEngine.Time.frameCount + " scene=" + cur + "\n");

            // 主路径：直接从 Game 视图自己的渲染纹理里取。
            // ScreenCapture 只在编辑器处于前台时才落盘，而这条是同步写文件，不依赖焦点。
            bool viaView = false;
            try { viaView = GrabFromGameView(outPath); }
            catch (System.Exception e)
            { WriteProbe("MENU65 grab THREW " + e.GetType().Name + " :: " + e.Message + "\n"); }

            if (!viaView)
            {
                ScreenCapture.CaptureScreenshot(outPath);
                WriteProbe("MENU65 ⚠ 没拿到 GameView 渲染纹理，退回 ScreenCapture（需要编辑器在前台才会落盘）\n");
            }
            WriteProbe("MENU65 " + (viaView ? "grabbed" : "captured") + " " + outPath + "\n");
        }

        static bool GrabFromGameView(string outPath)
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic
              | System.Reflection.BindingFlags.Public
              | System.Reflection.BindingFlags.Instance;

            // GameView 是 internal 类型，只能全程反射
            var gvType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gvType == null) { WriteProbe("MENU65 找不到 UnityEditor.GameView 类型\n"); return false; }

            var views = UnityEngine.Resources.FindObjectsOfTypeAll(gvType);
            if (views == null || views.Length == 0) { WriteProbe("MENU65 没有打开的 GameView\n"); return false; }

            // 只读字段，不 Invoke 任何方法 —— 2022.3 的 GameView.RenderView() 是带参数的，
            // Invoke(null) 会抛 TargetParameterCountException
            var rtField = gvType.GetField("m_RenderTexture", F);
            if (rtField == null) { WriteProbe("MENU65 GameView 没有 m_RenderTexture 字段\n"); return false; }

            RenderTexture rt = null;
            foreach (var v in views)
            {
                rt = rtField.GetValue(v) as RenderTexture;
                if (rt != null && rt.IsCreated() && rt.width > 16 && rt.height > 16) break;
                rt = null;
            }
            if (rt == null) { WriteProbe("MENU65 m_RenderTexture 里没有有效 RT\n"); return false; }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // GameView 的 RT 是底朝上的行序，直接编码会上下颠倒
            var px = tex.GetPixels32();
            var flipped = new Color32[px.Length];
            int row = tex.width;
            for (int y = 0; y < tex.height; y++)
                System.Array.Copy(px, (tex.height - 1 - y) * row, flipped, y * row, row);
            var outTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            outTex.SetPixels32(flipped);
            outTex.Apply();
            System.IO.File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            UnityEngine.Object.Destroy(outTex);
            UnityEngine.Object.Destroy(tex);

            // 只报格式；"后处理有没有吃到"用与已知前台截图比色调特性来判断，
            // 不拿"四角比中心暗"当判据 —— 这台机位中心是暗水面、四角本来就是亮树与天，
            // Vignette 压三成仍然 ratio>1（实测 1.451），那条判据是错的。
            WriteProbe("MENU65 rt " + rt.width + "x" + rt.height
                       + " fmt=" + rt.graphicsFormat + " -> " + outPath + "\n");
            return true;
        }

        [MenuItem("Tools/WaterPond/66 切换反射调试(0关/1只看平面反射)")]
        public static void TogglePondDebug()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pond.mat");
            if (m == null) { WriteProbe("MENU66 REFUSED: Water_Pond.mat 不存在\n"); return; }
            float cur = m.HasProperty("_DebugMode") ? m.GetFloat("_DebugMode") : 0f;
            m.SetFloat("_DebugMode", cur > 0.5f ? 0f : 1f);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
            WriteProbe("MENU66 _DebugMode -> " + m.GetFloat("_DebugMode") + "\n");
        }

        [MenuItem("Tools/WaterPond/67 导出平面反射RT(Play 中)")]
        public static void DumpPlanarRenderTarget()
        {
            if (!UnityEngine.Application.isPlaying)
            { WriteProbe("MENU67 REFUSED: 需要在 Play 模式（RT 由镜像相机每帧渲染）\n"); return; }

            var go = UnityEngine.GameObject.Find("PondReflectionCam");
            var cam = go != null ? go.GetComponent<Camera>() : null;
            var rt = cam != null ? cam.targetTexture : null;
            if (rt == null) { WriteProbe("MENU67 REFUSED: PondReflectionCam 或它的 targetTexture 为空\n"); return; }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            string outPath = "Docs/screenshots/_pond_rt.png";
            System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            WriteProbe("MENU67 rt " + rt.width + "x" + rt.height + " -> " + outPath + "\n");
        }

        // ═════════════════════════ 68 ═════════════════════════
        [MenuItem("Tools/WaterPond/68 森林化(冠层+密树环+灌木+倒木)")]
        public static void BuildForest()
        {
            if (UnityEngine.Application.isPlaying)
            { WriteProbe("MENU68 REFUSED: 在 Play 模式，先退出\n"); return; }

            var rng = new System.Random(202609247);

            // 只清自己这一轮的产物：菜单63 的 PondTree_/PondRock_ 由它自己负责
            foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name.StartsWith("ForestTree_") || go.name.StartsWith("PondBush_")
                                   || go.name.StartsWith("PoolLog_")))
                    UnityEngine.Object.DestroyImmediate(go);

            int bushes = BuildUndergrowth(rng);
            int trees  = BuildTreeWall();
            int logs   = BuildFallenLogs(rng);
            BuildCanopyDome();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU68 forest trees " + trees + " bushes " + bushes + " logs " + logs + "\n");
        }

        // 冠层穹顶：一块"有洞的天花板"。
        //   天窗（t0）留在头顶，是画面里唯一的天；
        //   下缘（t1）抬到约 32° 仰角 —— 这台机位画幅上缘只到仰角 16°，
        //   下缘若停在 18° 就会正好压在顶边上，读成黑色洞顶而不是"头顶的树冠"。
        //   0~32° 那圈留给树干 + 漏光带，而这一圈正是低机位掠射水面会倒出来的高度。
        static void BuildCanopyDome()
        {
            const int AZ = 72, PO = 18;
            float t1 = 0.320f * Mathf.PI;
            int rowLen = AZ + 1;
            var v = new List<Vector3>();

            for (int j = 0; j <= PO; j++)
            {
                float fj = (float)j / PO;
                for (int i = 0; i <= AZ; i++)
                {
                    float p = Mathf.PI * 2f * i / AZ;
                    // 洞缘被树冠咬成不规则形，不给它一条干净的圆周
                    float t0 = 0.085f * Mathf.PI
                             + 0.020f * Mathf.Sin(p * 5f + 1.1f)
                             + 0.012f * Mathf.Sin(p * 11f - 0.4f);
                    float t = Mathf.Lerp(t0, t1, fj);
                    float rr = 26f * (1f + 0.10f * Mathf.Sin(p * 5f + t * 3f)
                                          + 0.07f * Mathf.Sin(p * 9f - 1.4f)
                                          + 0.05f * Mathf.Sin(t * 11f + 2.2f));
                    v.Add(new Vector3(Mathf.Sin(t) * Mathf.Cos(p) * rr,
                                      Mathf.Cos(t) * rr,
                                      Mathf.Sin(t) * Mathf.Sin(p) * rr));
                }
            }

            var idx = new List<int>();
            for (int j = 0; j < PO; j++)
                for (int i = 0; i < AZ; i++)
                {
                    int a = j * rowLen + i, b = a + 1, c = (j + 1) * rowLen + i, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }

            var dome = AttachMesh("PoolCanopy", WaterProjectSetup.FacetedFrom(v, idx),
                                  GetMat("Pond_Canopy", new Color(0.055f, 0.118f, 0.062f)));
            var mr = dome != null ? dome.GetComponent<MeshRenderer>() : null;
            if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 两圈密植：中圈把对岸封成树墙（也是倒影主体），外圈在雾里读成"林子还有多深"。
        static int BuildTreeWall()
        {
            var camT = UnityEngine.GameObject.Find("Main Camera");
            Vector3 camXZ = camT != null ? camT.transform.position : new Vector3(-2.2f, 1.9f, -9.6f);

            var rng = new System.Random(202609248);
            int n = 0;
            for (int ring = 0; ring < 3; ring++)
            {
                int count = ring == 0 ? 18 : (ring == 1 ? 26 : 32);
                float lo = ring == 0 ? 5.0f : (ring == 1 ? 12.0f : 21.0f);
                float span = ring == 0 ? 6.0f : (ring == 1 ? 8.0f : 12.0f);
                float sLo = ring == 0 ? 0.85f : (ring == 1 ? 0.95f : 1.05f);
                for (int k = 0; k < count; k++)
                {
                    float th = Mathf.PI * 2f * k / count + (float)rng.NextDouble() * 0.42f;
                    float r = PondR(th) + lo + (float)rng.NextDouble() * span;
                    var at = new Vector3(Mathf.Cos(th) * r, BankY(r, th), Mathf.Sin(th) * r);
                    // 别把树长在相机身上
                    if (new Vector2(at.x - camXZ.x, at.z - camXZ.z).magnitude < 3.5f) continue;
                    if (PlacePondTree("ForestTree_" + n.ToString("00"), at, rng, n,
                                      sLo, sLo + 0.30f) != null) n++;
                }
            }
            return n;
        }

        // 林下灌木：同一套石头机器，把噪声模块压到 0.22 → 读成一团圆叶而不是岩石
        static int BuildUndergrowth(System.Random rng)
        {
            var mat = GetMat("Pond_Bush", new Color(0.030f, 0.098f, 0.044f));
            var blobs = new Mesh[3];
            for (int v = 0; v < 3; v++) blobs[v] = WaterProjectSetup.BuildFacetedRock(v, 0.22f);

            int n = 0;
            for (int k = 0; k < 36; k++)
            {
                float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = PondR(th) + 1.2f + (float)rng.NextDouble() * 15f;
                float y = BankY(r, th) - 0.20f;
                // 注意：PlaceBoulder 返回的是"本次放置数 1"，索引要从外面传进去
                WaterProjectSetup.PlaceBoulder(blobs, mat, rng, "PondBush_", n,
                    new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r), 0.55f, 1.45f);
                n++;
            }
            return n;
        }

        // 倒木：把棱角巨石沿长轴拉成一条脊，一头搭进水里 —— 低多边形里最省事的"横倒的树"
        static int BuildFallenLogs(System.Random rng)
        {
            var mat = GetMat("Pond_Bark", new Color(0.062f, 0.040f, 0.026f));
            var logMesh = WaterProjectSetup.BuildFacetedRock(1, 0.30f);
            int n = 0;
            for (int k = 0; k < 3; k++)
            {
                float th = Mathf.PI * 2f * k / 3f + 0.9f + (float)rng.NextDouble() * 0.4f;
                float r = PondR(th) - 0.6f + (float)rng.NextDouble() * 2.4f;
                float y = BankY(Mathf.Max(r, PondR(th) + 0.2f), th) + 0.10f;
                var go = new UnityEngine.GameObject("PoolLog_" + k);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = logMesh;
                mr.sharedMaterial = mat;
                go.transform.position = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
                go.transform.rotation = Quaternion.Euler(0f, Mathf.PI * Mathf.Rad2Deg * th + 90f
                                                          + (float)rng.NextDouble() * 30f - 15f,
                                                         (float)rng.NextDouble() * 6f - 3f);
                go.transform.localScale = new Vector3(3.4f + (float)rng.NextDouble() * 1.6f,
                                                      0.42f, 0.40f);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                n++;
            }
            return n;
        }

        // 交付树 Prefab 摆放：变体轮换 + 随机绕 Y + 0.85~1.15 缩放（文档 §6.5 去重复感），
        // 叶片 Renderer 关投影（文档 §7.1：alpha-clip 阴影是森林场景的头号开销）。
        static UnityEngine.GameObject PlacePondTree(string nm, Vector3 target, System.Random rng,
                                                    int variant, float sMin, float sMax)
        {
            string ap = "Assets/Trees/Prefabs/Prefab_SM_Tree_Broadleaf_"
                      + (variant % 5 + 1).ToString("00") + ".prefab";
            var pf = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(ap);
            if (pf == null) { WriteProbe("PLACE-MISSING " + ap + "\n"); return null; }

            var inst = PrefabUtility.InstantiatePrefab(pf) as UnityEngine.GameObject;
            if (inst == null) return null;
            inst.name = nm;
            // ⚠ 根节点带着导入器给的 -90° X 轴转换（Blender 是 Z-up）。
            //   只能在世界系里【左乘】一个绕 Y 的旋转；直接赋 rotation 会把轴转换抹掉、树被放倒。
            // 交付说明写"原点在树根底部、不要再加偏移"——那只对**平地**成立。
            // 实测最低几何比 y=0 还低 0.17~0.31m（根盘外扩），照字面摆会把浅色根盘整个
            // 露在地表上、读成一堆骨头。统一埋 0.45m，让树干"从土里长出来"。
            inst.transform.position = target + new Vector3(0f, -0.45f, 0f);
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f)
                                    * inst.transform.rotation;
            inst.transform.localScale = Vector3.one
                * (sMin + (float)rng.NextDouble() * (sMax - sMin));

            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.Contains("Leaf"))
                    { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; break; }
            }
            return inst;
        }

        // ═════════════════════════ 共用 ═════════════════════════
        static void SetupLighting()
        {
            var sun = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (sun == null)
            {
                var go = new UnityEngine.GameObject("Directional Light");
                sun = go.AddComponent<Light>();
            }
            sun.name = "Directional Light";
            sun.type = UnityEngine.LightType.Directional;
            // ── 林间漏光 ──
            // 开阔地那套"太阳 1.4 @E48/336"在这里不成立：密林里没有成片顺光，
            // 只有一束从头顶天窗插下来的漏光。仰角给到 72°（接近顶光，才像穿过冠层），
            // 方位仍放相机后侧 —— 放到对岸就会把远岸重新打回那条失饱和灰带（见《05》D-16）。
            // 叶卡是双面 alpha 片，朝向太阳的只占少数，平均受光很低；2.2 下树冠与地平带
            // 一起塌到 (40,62,30) 附近。抬到 3.0 抬的是"被照到的那几簇"，环境光不动，
            // 所以增加的是反差而不是整体亮度 —— 幽深需要的是暗底 + 少量亮部。
            sun.intensity = 3.0f;
            sun.color = new Color(1f, 0.880f, 0.620f);   // 暖金，与绿底互补才读得出"光"
            sun.transform.rotation = Quaternion.Euler(72f, 340f, 0f);
            sun.shadows = UnityEngine.LightShadows.Soft;

            // 林内天光是被叶子反复反弹出来的绿，阴影面必须是"暗绿"而不是"暗灰" —— 幽深感主要靠它。
            // 但不能压太低：交付树的叶卡是双面 alpha 片，树冠下半球朝向太阳的很少，
            // 天空分量低于 ~0.15 时整片树冠会糊成一团没有形状的暗块。
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor    = new Color(0.150f, 0.230f, 0.160f);
            RenderSettings.ambientEquatorColor = new Color(0.100f, 0.150f, 0.100f);
            RenderSettings.ambientGroundColor  = new Color(0.032f, 0.042f, 0.028f);

            RenderSettings.skybox = ForestSkyMaterial();

            // 雾是"看不透的林子"这件事本身：颜色取**冷青灰**而不是绿灰 ——
            // 绿雾会把远处树干整片染成草绿、和幽蓝的水抢色；冷雾才把纵深拉开，
            // 同时远处树干仍是"融进雾里"而不是"变黑"。
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.058f, 0.100f, 0.112f);
            RenderSettings.fogDensity = 0.0260f;

            var volGo = GetOrCreate("GlobalVolume");
            var vol = volGo.GetComponent<UnityEngine.Rendering.Volume>();
            if (vol == null) vol = volGo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true; vol.enabled = true;
            vol.sharedProfile = PondVolume();
        }

        // 林冠天空盒：复用 SkySimpleBlue 的三段渐变着色器。
        // 明暗方向和开阔地**相反** —— 冠层的暗由 PoolCanopy 那层几何负责，
        // 天空盒在这里只代表"从缝隙漏进来的天"，所以天顶要亮、地平是中等雾绿。
        // 若把天顶压成浓绿，穹顶天窗里透出来的就是一块黑，整幅唯一的灯就没了。
        static Material ForestSkyMaterial()
        {
            string p = MatFolder + "/Sky_ForestCanopy.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/SkySimpleBlue");
            if (sh == null) { WriteProbe("!! SkySimpleBlue shader NOT FOUND\n"); return null; }
            if (m == null) { m = new Material(sh) { name = "Sky_ForestCanopy" }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            m.SetColor("_ZenithColor",  new Color(0.660f, 0.700f, 0.560f));  // 天窗里的天
            // 地平带必须是画面里最亮的区域之一 —— 幽深靠"整体暗 + 几处亮"，不是"全都一样暗"。
            // 压到 0.185 时它与树冠实测几乎同值（(42,59,36) vs (40,62,30)），整幅掉进一条窄绿带。
            m.SetColor("_HorizonColor", new Color(0.225f, 0.320f, 0.300f));  // 远处透光树冠墙（偏冷，与幽蓝水同调）
            m.SetColor("_GroundColor",  new Color(0.035f, 0.048f, 0.030f));  // 腐殖质
            EditorUtility.SetDirty(m);
            return m;
        }

        static UnityEngine.Rendering.VolumeProfile PondVolume()
        {
            string p = "Assets/Settings/URP/PondVolume.profile.asset";
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

            if (!prof.TryGet<ColorAdjustments>(out var col))
            { col = prof.Add<ColorAdjustments>(); AssetDatabase.AddObjectToAsset(col, prof); }
            col.postExposure.overrideState = true; col.postExposure.value = 1.50f;
            col.saturation.overrideState = true;   col.saturation.value = 24f;
            col.contrast.overrideState = true;     col.contrast.value = 18f;

            // 「上方有光透下来」的第一层不是光柱几何，是**受光叶簇自己发光**：
            // 阈值只吃最亮的那几簇，晕开之后才有"林冠漏光"的空气感。
            // 阈值给到 1.05 是刻意抬高 —— 低于 1 会把整幅暗部一起提亮，画面立刻发灰。
            if (!prof.TryGet<Bloom>(out var bl))
            { bl = prof.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bl, prof); }
            bl.threshold.overrideState = true;  bl.threshold.value = 1.05f;
            bl.intensity.overrideState = true;  bl.intensity.value = 0.45f;
            bl.scatter.overrideState = true;    bl.scatter.value = 0.70f;

            // 压四周把视线收到潭心，暗调场景没有它会有"整幅糊成一片"的感觉
            if (!prof.TryGet<Vignette>(out var vg))
            { vg = prof.Add<Vignette>(); AssetDatabase.AddObjectToAsset(vg, prof); }
            vg.intensity.overrideState = true;    vg.intensity.value = 0.30f;
            vg.smoothness.overrideState = true;   vg.smoothness.value = 0.45f;

            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        static Material PondWaterMaterial()
        {
            string p = MatFolder + "/Water_Pond.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/Water_Pond");
            if (sh == null) { WriteProbe("!! Water_Pond shader NOT FOUND (编译失败?)\n"); return m; }
            if (m == null) { m = new Material(sh) { name = "Water_Pond" }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;

            m.SetColor("_SkyZenith",    new Color(0.660f, 0.700f, 0.560f));   // = Sky_ForestCanopy 天顶
            m.SetColor("_SkyHorizon",   new Color(0.225f, 0.320f, 0.300f));   // = Sky_ForestCanopy 地平
            m.SetColor("_SunColor",     new Color(1.000f, 0.880f, 0.620f));   // = 灯的暖金色
            m.SetColor("_FoamColor",    new Color(0.620f, 0.680f, 0.560f));

            // 用户要"幽蓝"不是"墨绿" ⇒ 蓝通道必须反超绿。
            // 富营养浅潭是绿的（红被吸收），冷泉/深水才是蓝黑（红绿都被吃掉、只剩蓝）。
            // 所以这里不是调饱和度，是把吸收曲线往"绿也吃掉"的方向推。
            m.SetColor("_DeepColor",    new Color(0.016f, 0.060f, 0.108f));
            m.SetColor("_ShallowColor", new Color(0.062f, 0.185f, 0.245f));
            m.SetFloat("_Absorb", 0.62f);
            m.SetFloat("_MaxDepth", 1.6f);
            m.SetFloat("_Opacity", 0.76f);

            // 几何波：fetch 正确的短波细浪
            m.SetFloat("_WaveAmp", 0.010f);
            m.SetFloat("_WaveScale", 1.0f);
            m.SetFloat("_WaveSpeed", 0.55f);

            // 表面皮肤才是潭水的主信号 —— 但强度必须克制，否则倒影会被打碎成噪点
            m.SetFloat("_DetailStrength", 0.28f);
            m.SetFloat("_DetailScale", 1.9f);
            m.SetFloat("_DetailFlow", 0.10f);
            m.SetFloat("_RippleStrength", 0.085f);
            m.SetFloat("_RippleScale", 5.5f);

            m.SetFloat("_RefractStrength", 0.12f);
            m.SetFloat("_MaxUVOffset", 0.012f);
            m.SetFloat("_DepthSharp", 4.0f);

            // _ReflFade 从 45 抬到 120：树就在相机前方约 24m，45m 的淡出曲线
            // 恰好在"唯一值得倒的东西"那里把它抹掉一半 —— 淡出是为了远处没内容时兜底，
            // 不是为了削掉主体。
            m.SetFloat("_PlanarStrength", 1.6f);
            m.SetFloat("_ReflDistort", 0.016f);
            m.SetFloat("_ReflFade", 120.0f);
            m.SetFloat("_FresnelPow", 4.0f);
            // 林底不是一面干净的镜子：四周都在遮天，反射权重给不到 1；
            // 涟漪再强一点就会把倒影切成斑马纹，所以同时收 _RippleStrength。
            m.SetFloat("_SkyReflect", 0.90f);

            m.SetFloat("_SpecPower", 320f);
            m.SetFloat("_SpecStrength", 0.5f);
            m.SetFloat("_SunGloss", 260f);
            m.SetFloat("_SunStrength", 0.5f);

            m.SetFloat("_CrestJ", 0.55f);   m.SetFloat("_CrestSoft", 0.30f);
            m.SetFloat("_CrestFoam", 0.04f);
            m.SetFloat("_ShoreFoam", 0.28f); m.SetFloat("_ShoreWidth", 0.55f);
            // 环境光整体压到约 1/4 之后，水体自照项必须补偿，否则"幽深"会变成"一个黑洞"。
            m.SetFloat("_AmbientStrength", 1.0f);
            m.SetFloat("_DebugMode", 0f);

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material GetMat(string name, Color linearRGBA)
        {
            string p = MatFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", linearRGBA);
            m.SetColor("_Color", linearRGBA);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static UnityEngine.GameObject AttachMesh(string goName, Mesh m, Material mat)
        {
            string p = MeshFolder + "/" + goName + ".asset";
            SaveMeshAsset(m, p, goName);
            return AttachExistingMesh(goName, mat);
        }

        // 网格资产已经落盘，只做挂载
        static UnityEngine.GameObject AttachExistingMesh(string goName, Material mat)
        {
            string p = MeshFolder + "/" + goName + ".asset";
            var go = GetOrCreate(goName);
            var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (mat != null) mr.sharedMaterial = mat;
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;
            return go;
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
                old.vertices = m.vertices;
                old.triangles = m.triangles;
                if (m.uv != null && m.uv.Length == m.vertexCount) old.uv = m.uv;
                old.RecalculateBounds();
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(old);
            }
            AssetDatabase.SaveAssets();
        }

        static UnityEngine.GameObject GetOrCreate(string name)
        {
            var go = UnityEngine.GameObject.Find(name);
            if (go == null) go = new UnityEngine.GameObject(name);
            return go;
        }

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }
    }
}
#endif
