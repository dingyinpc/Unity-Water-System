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
                AttachExistingMesh("PoolBasin", GetMat("Pond_SandWet", new Color(0.30f, 0.33f, 0.22f)));
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
                AttachMesh("PoolBank", WaterProjectSetup.FacetedFrom(v, idx), GetMat("Pond_Sand", new Color(0.50f, 0.45f, 0.37f)));
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
                                       GetMat("Pond_Grass", new Color(0.075f, 0.250f, 0.115f)));
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

            var rockMat = GetMat("Pond_Rock", new Color(0.32f, 0.42f, 0.54f));
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

            // 树绕潭一圈 —— 平面反射要有内容可倒，三棵排成一排是"布景"，七棵散开才是"林子"
            var trees = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
                "Assets/Art/StylePacks/正式素材/Trees/Trees_Quaternius.glb");
            int placed = 0;
            if (trees != null)
            {
                for (int k = 0; k < 7; k++)
                {
                    // 0 号固定在相机视线方向（+z 偏左），保证主倒影一定在画面里
                    float th = k == 0 ? Mathf.PI * 0.5f + 0.15f
                                      : Mathf.PI * 2f * k / 7f + (float)rng.NextDouble() * 0.4f;
                    // 树要**贴着岸**长。倒影占多少水面是几何问题不是着色问题：
                    // 树在岸外 3.6~9.1m 时离相机 20~26m，只张 8° 仰角，就只能在远岸一条窄带里倒出来；
                    // 收到 1.6~4.8m 后张角翻倍，潭面大半都能映到树。
                    float r = PondR(th) + 1.6f + (float)rng.NextDouble() * 3.2f;
                    var inst = PrefabUtility.InstantiatePrefab(trees) as UnityEngine.GameObject;
                    if (inst == null) continue;
                    inst.name = "PondTree_0" + k;
                    if (inst.transform.childCount == 0) continue;
                    var keep = inst.transform.GetChild(0);
                    for (int c = inst.transform.childCount - 1; c >= 0; c--)
                        if (inst.transform.GetChild(c) != keep)
                            UnityEngine.Object.DestroyImmediate(inst.transform.GetChild(c).gameObject);

                    float sc = 0.80f + (float)rng.NextDouble() * 0.45f;
                    inst.transform.localScale = Vector3.one * sc;
                    var target = new Vector3(Mathf.Cos(th) * r, BankY(r, th) - 0.2f, Mathf.Sin(th) * r);
                    inst.transform.position = Vector3.zero;
                    inst.transform.position = target - Vector3.Scale(keep.localPosition, inst.transform.localScale);

                    // 远树不必自投影：仰角 30° 下它们会在草坡上拖出长条黑影
                    foreach (var shR in inst.GetComponentsInChildren<Renderer>(true))
                        shR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    placed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU63 props " + n + " rocks/trees " + placed + (trees == null ? " TREES-MISSING" : "") + "\n");
        }

        // ═════════════════════════ 64 ═════════════════════════
        [MenuItem("Tools/WaterPond/64 整链建潭(60→63)")]
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
            ScreenCapture.CaptureScreenshot(outPath);
            WriteProbe("MENU65 captured " + outPath + " scene=" + cur +
                       " frame=" + UnityEngine.Time.frameCount + "\n");
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
            // 顺光位下受光面 N·L≈0.92（逆光时只有 0.16），沿用 2.5 会直接把沙地打到截顶
            // （实测 med(255,255,239)、岩石 (171,225,255)）。1.4 让同反照率的岩石落回
            // Main 的实测族 (103,130,154)。
            sun.intensity = 1.4f;
            sun.color = new Color(1f, 0.975f, 0.95f);
            // 太阳放在【相机后上方】而不是对岸逆光位。
            // 逆光（Euler 30/208）时远岸沙坡朝 -z、坡度 23°，N·L 只有 0.16 → 被蓝色三光源环境光
            // 主导，在水线上方横出一条失饱和的灰带（实测 (110,118,119)→(99,106,106)，而它后面
            // 平坦岸顶是 (211,201,177)）—— 与 Main 的"水泥台阶"同一类故障。
            // 顺光后远岸坡面 N·L≈0.92，且平面反射里映出的也是受光的坡面而不是暗块。
            // 代价：掠射碎闪本来要求太阳仰角≈视线俯角（这台机位约 5~7°），而 _SunGloss 260 的
            // 半高宽只有 ±0.3°，抬到 48° 后水面基本不再有太阳反光带 —— 林间静潭本就该如此。
            sun.transform.rotation = Quaternion.Euler(48f, 336f, 0f);
            sun.shadows = UnityEngine.LightShadows.Soft;

            // 潭水的观感一半靠天光：水面反射的是天空，环境光照的是岸。
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor    = new Color(0.500f, 0.580f, 0.680f);
            RenderSettings.ambientEquatorColor = new Color(0.440f, 0.500f, 0.470f);
            RenderSettings.ambientGroundColor  = new Color(0.290f, 0.270f, 0.230f);

            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sky_SimpleBlue.mat");
            if (sky != null) RenderSettings.skybox = sky;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.63f, 0.77f, 0.96f);
            RenderSettings.fogDensity = 0.0060f;

            // 后处理：ACES + 高曝光，和 Main 同一套标定
            var volGo = GetOrCreate("GlobalVolume");
            var vol = volGo.GetComponent<UnityEngine.Rendering.Volume>();
            if (vol == null) vol = volGo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true; vol.enabled = true;
            vol.sharedProfile = PondVolume();
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
            col.postExposure.overrideState = true; col.postExposure.value = 1.15f;
            col.saturation.overrideState = true;   col.saturation.value = 14f;
            col.contrast.overrideState = true;     col.contrast.value = 0f;

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

            m.SetColor("_DeepColor",    new Color(0.055f, 0.290f, 0.310f));
            m.SetColor("_ShallowColor", new Color(0.260f, 0.580f, 0.540f));
            m.SetColor("_SkyZenith",    new Color(0.100f, 0.280f, 0.400f));
            m.SetColor("_SkyHorizon",   new Color(0.550f, 0.700f, 0.850f));
            m.SetColor("_SunColor",     new Color(1.000f, 0.950f, 0.840f));
            m.SetColor("_FoamColor",    new Color(0.920f, 0.970f, 0.970f));

            // 潭深 ~1.45m。_Absorb 0.9 在这个深度给出 depthMix 0.73 → 整潭近黑绿，
            // 把倒影整个吞掉。清水潭的吸收必须很低，深度感主要靠"看得见底"而不是压暗。
            m.SetFloat("_Absorb", 0.30f);
            m.SetFloat("_MaxDepth", 1.6f);
            m.SetFloat("_Opacity", 0.66f);

            // 几何波：fetch 正确的短波细浪
            m.SetFloat("_WaveAmp", 0.010f);
            m.SetFloat("_WaveScale", 1.0f);
            m.SetFloat("_WaveSpeed", 0.55f);

            // 表面皮肤才是潭水的主信号 —— 但强度必须克制，否则倒影会被打碎成噪点
            m.SetFloat("_DetailStrength", 0.28f);
            m.SetFloat("_DetailScale", 1.9f);
            m.SetFloat("_DetailFlow", 0.10f);
            m.SetFloat("_RippleStrength", 0.11f);
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
            m.SetFloat("_SkyReflect", 0.95f);

            m.SetFloat("_SpecPower", 320f);
            m.SetFloat("_SpecStrength", 1.1f);
            m.SetFloat("_SunGloss", 260f);
            m.SetFloat("_SunStrength", 0.9f);

            m.SetFloat("_CrestJ", 0.55f);   m.SetFloat("_CrestSoft", 0.30f);
            m.SetFloat("_CrestFoam", 0.10f);
            m.SetFloat("_ShoreFoam", 0.75f); m.SetFloat("_ShoreWidth", 0.55f);
            m.SetFloat("_AmbientStrength", 0.40f);
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
