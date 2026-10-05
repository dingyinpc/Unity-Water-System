// ============================================================================
// WaterPoolSetup.cs — 第五条水线：泳池（`PoolScene`，`Tools/Pool/90-96`）
//
// 用户需求（2026-10-04）："单独开个泳池场景，做可交互的水"。
// 技术选型与调研见《Docs/学习路径与知识库/07_水交互技术调研.md》：
//   交互 = B 档离散波动方程（ping-pong RT，池壁反射白送），不是主线那种解析贴花。
//
// 资产前缀 **Pool***：Pond*（潭）/ Ocean*（海岸）/ Islet*（潟湖）已占，
//   撞名会互相覆盖网格——Tools/audit_materials.py 第⑥项盯这事。
//
// 为什么泳池水**不读深度纹理**：池底是解析平面（-1.35m 常数），没有东西可读；
//   而且 IsletScene 2026-10-04 轮实锤本机两条取帧通路的 _CameraDepthTexture
//   都不可信——泳池线从出生就不欠这个债。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;                       // clip 挑选用 OfType/Where/FirstOrDefault
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class WaterPoolSetup
    {
        const string ScenePath = "Assets/Scenes/PoolScene.unity";
        const string MatFolder = "Assets/Art/Materials";
        const string MeshFolder = "Assets/Art/Meshes";
        const string VolumePath = "Assets/Settings/URP/PoolVolume.profile.asset";

        // ── 池体尺寸（米）──
        const float PoolL = 12f;     // x 方向
        const float PoolW = 6f;      // z 方向
        const float PoolD = 1.35f;   // 水深（水面 y=0）
        const float WallTop = 0.25f; // 池壁高出水面的沿
        const float DeckW = 2.0f;    // 四周压沿台宽

        // ── 机位 ──
        // 俯角 +22°：Unity 的 X 旋转**正向朝下**（Euler(90,0,0)=垂直向下）。
        // 第一版写 -22 朝上看天，画面纯天空（实拍教训）。
        static readonly Vector3 CamPos   = new Vector3(0f, 3.2f, -7.6f);
        static readonly Vector3 CamEuler = new Vector3(22f, 0f, 0f);
        const float CamFov = 50f;

        // ── 太阳：方位要在机位**前方**（+z 天空），高光带才会铺向相机 ──
        // yaw 168° ⇒ 光朝 -z 走，反射高光对着镜头；俯角 38°（家族惯例）。
        const float SunPitch = 38f;
        const float SunYaw = 168f;
        const float SunIntensity = 3.0f;

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

        static bool EnsureNotPlaying(string tag)
        {
            if (!Application.isPlaying) return true;
            WriteProbe(tag + " REFUSED: 在 Play 模式\n");
            return false;
        }

        static void EnsureScene()
        {
            // ⚠ 无头驱动下 Unity 绝不能弹任何模态框——实测 NewScene 撞上脏场景会弹
            //   "另存为"文件框，主线程卡死、桥失联（解堵靠 BM_CLICK 取消，见探针）。
            //   所以：脏场景**先静默存到目标路径**，之后要么 OpenScene 要么
            //   NewScene（此时场面上已无脏场景，不会再弹）。
            var cur = EditorSceneManager.GetActiveScene();
            if (cur.IsValid() && cur.isDirty)
                EditorSceneManager.SaveScene(cur, ScenePath);

            var sc = EditorSceneManager.GetSceneByPath(ScenePath);
            if (!sc.IsValid() || !sc.isLoaded)
            {
                if (System.IO.File.Exists(ScenePath))
                    sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                else
                    sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            EditorSceneManager.SetActiveScene(sc);
        }

        static void SaveScene()
        {
            var sc = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            EditorSceneManager.SaveScene(sc);
            WriteProbe("POOL scene saved -> " + ScenePath + "\n");
        }

        // 远程调菜单的唯一报错通道就是探针——所有菜单体套 try/catch，
        // 异常原文落盘（控制台在无头驱动下看不见）。
        static void Run(string tag, System.Action body)
        {
            try { body(); }
            catch (System.Exception e)
            { WriteProbe(tag + " THREW " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace + "\n"); }
        }

        // ═════════════════════════ 90 ═════════════════════════
        [MenuItem("Tools/Pool/90 建骨架(场景+相机+太阳+体积)")]
        public static void BuildShell()
        {
            if (!EnsureNotPlaying("POOL90")) return;
            Run("POOL90", () =>
            {
            EnsureScene();

            // ⚠ 不能用 GameObject.Find("Main Camera")：NewScene(Single) 的旧场景
            //   teardown 是延迟销毁，同帧 Find 会捞到刚销毁的假对象，而 ??
            //   运算符不认 Unity 的假 null（MissingComponentException 实锤）。
            //   FindObjectOfType 走 Unity 重载的 ==，只会拿到活组件。
            var cam = Object.FindObjectOfType<Camera>();
            GameObject camGo;
            if (cam == null)
            {
                camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            else camGo = cam.gameObject;
            cam.transform.position = CamPos;
            cam.transform.rotation = Quaternion.Euler(CamEuler);
            cam.fieldOfView = CamFov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            // 同款假 null 陷阱：这里用 Unity 重载的 == 判
            var cd = cam.GetUniversalAdditionalCameraData();
            if (cd == null) cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;

            // ── 太阳 ──
            var sun = Object.FindObjectOfType<Light>();
            GameObject lightGo;
            if (sun == null)
            {
                lightGo = new GameObject("Directional Light");
                sun = lightGo.AddComponent<Light>();
            }
            else lightGo = sun.gameObject;
            sun.type = LightType.Directional;
            sun.intensity = SunIntensity;
            sun.color = new Color(1f, 0.965f, 0.900f);
            sun.transform.rotation = Quaternion.Euler(SunPitch, SunYaw, 0f);
            sun.shadows = LightShadows.None;

            // ── 天空：天顶/地平双色的简单蓝天（家族里最便宜的一片天）──
            var skySh = Shader.Find("WaterProject/Sky_SimpleBlue");
            if (skySh != null)
            {
                var skyMat = new Material(skySh) { name = "PoolSky" };
                // 地面半球默认蓝灰在 ACES+饱和下偏暖棕（历轮截图的"棕色带"），
                // 显式压成远海青蓝——泳池周围的"地平线以下"读成远处海面
                skyMat.SetColor("_GroundColor", new Color(0.15f, 0.28f, 0.36f));
                RenderSettings.skybox = skyMat;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.56f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.46f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.24f, 0.22f);

            // ── 后处理体积：ACES + 轻饱和（泳池要"干净"，不像潟湖那样重饱和）──
            var volGo = GameObject.Find("PoolVolume");
            if (volGo == null) volGo = new GameObject("PoolVolume");
            var vol = volGo.GetComponent<Volume>() ?? volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (prof == null)
            {
                prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, VolumePath);
            }
            if (!prof.TryGet<Tonemapping>(out var tm))
            { tm = prof.Add<Tonemapping>(); AssetDatabase.AddObjectToAsset(tm, prof); }
            tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;
            if (!prof.TryGet<ColorAdjustments>(out var col))
            { col = prof.Add<ColorAdjustments>(); AssetDatabase.AddObjectToAsset(col, prof); }
            col.postExposure.overrideState = true; col.postExposure.value = 1.0f;
            col.saturation.overrideState = true;   col.saturation.value = 12f;
            col.contrast.overrideState = true;     col.contrast.value = 5f;
            if (!prof.TryGet<Bloom>(out var bl))
            { bl = prof.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bl, prof); }
            bl.threshold.overrideState = true;  bl.threshold.value = 1.25f;
            bl.intensity.overrideState = true;  bl.intensity.value = 0.30f;
            vol.sharedProfile = prof;

            SaveScene();
            WriteProbe("POOL90 shell built  cam=" + CamPos + "  sun=" + SunPitch + "/" + SunYaw + "\n");
            });
        }

        // ═════════════════════════ 91 ═════════════════════════
        [MenuItem("Tools/Pool/91 建泳池(池体+瓷砖+压沿台)")]
        public static void BuildPool()
        {
            if (!EnsureNotPlaying("POOL91")) return;
            if (EditorSceneManager.GetSceneByPath(ScenePath).isLoaded == false
                && EditorSceneManager.GetActiveScene().path != ScenePath)
            { WriteProbe("POOL91 REFUSED: 先跑 Tools/Pool/90\n"); return; }

            // 重建前清旧
            foreach (var n in new[] { "PoolShell" })
            {
                var old = GameObject.Find(n);
                if (old != null) Object.DestroyImmediate(old);
            }

            var mat = PoolTileMaterial();
            var root = new GameObject("PoolShell");

            // ── 池底 ──
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "PoolFloor";
            floor.transform.SetParent(root.transform);
            floor.transform.localScale = new Vector3(PoolL + 0.6f, 0.3f, PoolW + 0.6f);
            floor.transform.position = new Vector3(0f, -PoolD - 0.15f, 0f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // ── 四壁（厚 0.3，从池底到 WallTop）──
            float wallH = PoolD + WallTop;
            float wallCy = -PoolD + wallH * 0.5f;
            void Wall(string name, float x, float z, float sx, float sz)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = name;
                w.transform.SetParent(root.transform);
                w.transform.localScale = new Vector3(sx, wallH, sz);
                w.transform.position = new Vector3(x, wallCy, z);
                w.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Wall("PoolWallN", 0f, (PoolW + 0.3f) * 0.5f, PoolL + 0.6f, 0.3f);
            Wall("PoolWallS", 0f, -(PoolW + 0.3f) * 0.5f, PoolL + 0.6f, 0.3f);
            Wall("PoolWallE", (PoolL + 0.3f) * 0.5f, 0f, 0.3f, PoolW + 0.6f);
            Wall("PoolWallW", -(PoolL + 0.3f) * 0.5f, 0f, 0.3f, PoolW + 0.6f);

            // ── 压沿台（四条，顶面 +WallTop）──
            var deckMat = DeckMaterial();
            void Deck(string name, float x, float z, float sx, float sz)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
                d.name = name;
                d.transform.SetParent(root.transform);
                d.transform.localScale = new Vector3(sx, WallTop + 0.3f, sz);
                d.transform.position = new Vector3(x, WallTop - (WallTop + 0.3f) * 0.5f, z);
                d.GetComponent<MeshRenderer>().sharedMaterial = deckMat;
            }
            Deck("PoolDeckN", 0f, PoolW * 0.5f + DeckW * 0.5f, PoolL + DeckW * 2f, DeckW);
            Deck("PoolDeckS", 0f, -PoolW * 0.5f - DeckW * 0.5f, PoolL + DeckW * 2f, DeckW);
            Deck("PoolDeckE", PoolL * 0.5f + DeckW * 0.5f, 0f, DeckW, PoolW);
            Deck("PoolDeckW", -PoolL * 0.5f - DeckW * 0.5f, 0f, DeckW, PoolW);

            SaveScene();
            WriteProbe("POOL91 pool built  L=" + PoolL + " W=" + PoolW + " D=" + PoolD + "\n");
        }

        static Material PoolTileMaterial()
        {
            string p = MatFolder + "/Pool_Tile.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/Pool_Tile");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            return m;
        }

        static Material DeckMaterial()
        {
            string p = MatFolder + "/Pool_Deck.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(sh);
                m.SetColor("_BaseColor", new Color(0.60f, 0.57f, 0.52f));
                m.SetFloat("_Smoothness", 0.15f);
                AssetDatabase.CreateAsset(m, p);
            }
            return m;
        }

        // ═════════════════════════ 92 ═════════════════════════
        [MenuItem("Tools/Pool/92 建水面(网格+材质+涟漪系统)")]
        public static void BuildWater()
        {
            if (!EnsureNotPlaying("POOL92")) return;
            Run("POOL92", () =>
            {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            { WriteProbe("POOL92 REFUSED: 先跑 Tools/Pool/90\n"); return; }

            // ── 水面网格：12×6 均匀网格 96×48 格（顶点位移需要细分）──
            const int nx = 96, nz = 48;
            var v = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[v.Length];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    int k = j * (nx + 1) + i;
                    v[k] = new Vector3((i / (float)nx - 0.5f) * PoolL, 0f, (j / (float)nz - 0.5f) * PoolW);
                    uv[k] = new Vector2(i / (float)nx, j / (float)nz);
                }
            var idx = new int[nx * nz * 6];
            int t = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    idx[t++] = a; idx[t++] = c; idx[t++] = b;
                    idx[t++] = b; idx[t++] = c; idx[t++] = d;
                }
            var mesh = new Mesh { name = "PoolWaterMesh" };
            mesh.SetVertices(new System.Collections.Generic.List<Vector3>(v));
            mesh.SetUVs(0, new System.Collections.Generic.List<Vector2>(uv));
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.bounds.Expand(new Vector3(0f, 0.4f, 0f));   // 顶点会被波场推走

            string mp = MeshFolder + "/PoolWaterMesh.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if (old != null) AssetDatabase.DeleteAsset(mp);
            AssetDatabase.CreateAsset(mesh, mp);

            // ── 水面对象 ──
            // ⚠ 组件获取一律用 Unity 重载的 == （?? 不认假 null，重跑会踩）
            var go = GameObject.Find("PoolWater");
            if (go == null) go = new GameObject("PoolWater");
            // ⚠ 残跑可能留下任意变换（实测：水面立成一面墙）——每次重建都归位
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            mr.sharedMaterial = PoolWaterMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // ── 涟漪系统（波动方程管理器）──
            if (go.GetComponent<WaterProject.Logic.PoolRipples>() == null)
                go.AddComponent<WaterProject.Logic.PoolRipples>();

            SaveScene();
            WriteProbe("POOL92 water built  verts=" + v.Length + "  tris=" + (idx.Length / 6) + "\n");
            });
        }

        static Material PoolWaterMaterial()
        {
            string p = MatFolder + "/Water_Pool.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/Water_Pool");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            return m;
        }

        // ═════════════════════════ 93 ═════════════════════════
        // 第三人称人物（KayKit Knight，CC0，来源与许可见 Docs/外接组件记录.md）。
        // FBX 由 Blender 无头管线从 GLB 转出（E:/Blender/Blender临时/build_character2.py，
        // 76 个动作全烘焙成 takes）。本菜单做四件事：实例化+缩放、URP 材质贴图、
        // 程序化 AnimatorController（Idle/Walk/Run 三态 Speed 阈值）、挂 PoolThirdPerson。
        [MenuItem("Tools/Pool/93 建人物(模型+动画机+涟漪联动)")]
        public static void BuildCharacter()
        {
            if (!EnsureNotPlaying("POOL93")) return;
            Run("POOL93", () =>
            {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            { WriteProbe("POOL93 REFUSED: 先跑 Tools/Pool/90\n"); return; }

            const string FbxPath = "Assets/Art/Models/Pool/Pool_Character.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (prefab == null)
            { WriteProbe("POOL93 REFUSED: 找不到 " + FbxPath + "（先跑 Blender 转换）\n"); return; }

            var old = GameObject.Find("PoolCharacter");
            if (old != null) Object.DestroyImmediate(old);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = "PoolCharacter";
            // 模型原生高 ~2.0m（报告 knight_dims.y），1.35m 水深要淹到大腿根 → 缩到 1.45m
            go.transform.position = new Vector3(0f, -PoolD, 1.0f);
            go.transform.localScale = Vector3.one * 0.9f;   // 1.8m 高：1.35m 水淹到大腿上部（0.72 时淹到脖子）

            // ── 材质：FBX 不带贴图，自建 URP/Lit 贴 KayKit 渐变图集 ──
            var mat = PoolKnightMaterial();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = mat;

            // ── 动画机：程序化三态控制器 ──
            var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
                .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
            WriteProbe("POOL93 clips(" + clips.Length + "): " +
                       string.Join(", ", clips.Select(c => c.name).Take(12).ToArray()) +
                       (clips.Length > 12 ? " ..." : "") + "\n");
            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildCharacterCtrl(clips);
            // 就地剪辑 + 自定义物理：根运动必须关——Jump_Full_Long 带烘焙位移，
            // 开着的话动画每帧把根位置写回烘焙轨迹，覆盖跳跃物理（实测 Y 钉死）
            animator.applyRootMotion = false;

            // ── 控制脚本 ──
            if (go.GetComponent<WaterProject.Logic.PoolThirdPerson>() == null)
                go.AddComponent<WaterProject.Logic.PoolThirdPerson>();

            SaveScene();
            WriteProbe("POOL93 character built at (0,-PoolD,1.0)\n");
            });
        }

        static Material PoolKnightMaterial()
        {
            string p = MatFolder + "/Pool_Knight_Mat.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Pool/Pool_Knight_Tex.png");
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", 0.10f);
            return m;
        }

        static UnityEditor.Animations.AnimatorController BuildCharacterCtrl(AnimationClip[] clips)
        {
            // CreateAnimatorControllerAtPath 不会自建目录（实测 UnityException）
            if (!AssetDatabase.IsValidFolder("Assets/Art/Animations"))
                AssetDatabase.CreateFolder("Assets/Art", "Animations");
            string p = "Assets/Art/Animations/PoolCharacter.controller";
            if (AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(p) != null)
                AssetDatabase.DeleteAsset(p);
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(p);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;

            // 取**最短**匹配名：包含 "Idle" 的有 2H_Melee_Idle/Unarmed_Idle/Idle……
            //   字母序会挑到 2H_Melee_Idle（拿剑站姿），最短才是纯净 Idle
            System.Func<string, string, AnimationClip> find = (must, fallback) =>
                clips.FirstOrDefault(c => c.name == must || c.name == "Rig|" + must)
                ?? clips.Where(c => c.name.IndexOf(fallback, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(c => c.name.Length).FirstOrDefault();
            var idleClip = find("Unarmed_Idle", "Idle");
            var walkClip = find("Walking_A", "Walking");
            var runClip = find("Running_A", "Running");
            WriteProbe("POOL93 ctrl clips: idle=" + (idleClip != null ? idleClip.name : "!!") +
                       " walk=" + (walkClip != null ? walkClip.name : "!!") +
                       " run=" + (runClip != null ? runClip.name : "!!") + "\n");

            var idle = sm.AddState("Idle"); idle.motion = idleClip;
            var walk = sm.AddState("Walk"); walk.motion = walkClip;
            var run = sm.AddState("Run"); run.motion = runClip;
            sm.defaultState = idle;
            void Link(UnityEditor.Animations.AnimatorState a, UnityEditor.Animations.AnimatorState b,
                      UnityEditor.Animations.AnimatorConditionMode mode, float th)
            {
                var t = a.AddTransition(b);
                t.AddCondition(mode, th, "Speed");
                t.hasExitTime = false;
                t.duration = 0.15f;
            }
            Link(idle, walk, UnityEditor.Animations.AnimatorConditionMode.Greater, 0.15f);
            Link(walk, idle, UnityEditor.Animations.AnimatorConditionMode.Less, 0.10f);
            Link(walk, run, UnityEditor.Animations.AnimatorConditionMode.Greater, 2.0f);
            Link(run, walk, UnityEditor.Animations.AnimatorConditionMode.Less, 2.0f);

            // Jump：三态都能被 Trigger 打断，播完回 Idle（Speed 仍会立刻接走走路态）
            var jumpClip = find("Jump_Full_Long", "Jump_Full");
            WriteProbe("POOL93 jump clip: " + (jumpClip != null ? jumpClip.name : "!!") + "\n");
            var jump = sm.AddState("Jump");
            jump.motion = jumpClip;
            jump.speed = 1.15f;   // 片长约 1.2s，压到 ~1.05s 贴合腾空时间
            void JumpLink(UnityEditor.Animations.AnimatorState a)
            {
                var t = a.AddTransition(jump);
                t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Jump");
                t.hasExitTime = false;
                t.duration = 0.08f;
            }
            JumpLink(idle); JumpLink(walk); JumpLink(run);
            var back = jump.AddTransition(idle);
            back.exitTime = 0.96f; back.duration = 0.10f;
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        // ═════════════════════════ 94 ═════════════════════════
        // 沙滩球：程序化条纹材质 + PoolBall 浮力物理（Kerner 球版，见 PoolBall.cs 头注释）
        [MenuItem("Tools/Pool/94 建球(沙滩球+浮力)")]
        public static void BuildBall()
        {
            if (!EnsureNotPlaying("POOL94")) return;
            Run("POOL94", () =>
            {
            var old = GameObject.Find("PoolBall");
            if (old != null) Object.DestroyImmediate(old);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PoolBall";
            // 直径 0.48m → PoolBall.radius 0.24（字段默认一致）
            go.transform.localScale = Vector3.one * 0.48f;
            go.transform.position = new Vector3(0f, 1.6f, 0f);
            var col = go.GetComponent<SphereCollider>();
            if (col != null) Object.DestroyImmediate(col);   // 自定义物理，碰撞体无用
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = PoolBallMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (go.GetComponent<WaterProject.Logic.PoolBall>() == null)
                go.AddComponent<WaterProject.Logic.PoolBall>();

            SaveScene();
            WriteProbe("POOL94 ball built\n");
            });
        }

        static Material PoolBallMaterial()
        {
            string p = MatFolder + "/Pool_Ball.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/Pool_Ball");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            return m;
        }

        // ═════════════════════════ 98 ═════════════════════════
        // 测试抛球（Play 中）：从人物胸口沿面向斜上抛——等价于按一次 B 键，
        // 供桥取帧用（桥按不了键盘）
        [MenuItem("Tools/Pool/98 测试抛球")]
        public static void TestToss()
        {
            var ball = Object.FindObjectOfType<WaterProject.Logic.PoolBall>();
            var ch = Object.FindObjectOfType<WaterProject.Logic.PoolThirdPerson>();
            if (ball == null) { WriteProbe("POOL98 REFUSED: 没有 PoolBall（先跑 Tools/Pool/94）\n"); return; }
            Vector3 from, dir;
            if (ch != null)
            {
                from = ch.transform.position + Vector3.up * 1.2f;
                dir = ch.transform.forward * 0.75f + Vector3.up * 0.65f;
            }
            else
            {
                from = new Vector3(0f, 0.8f, -2f);
                dir = new Vector3(0f, 0.65f, 0.75f);
            }
            ball.Toss(from, dir, 4.2f);
            WriteProbe("POOL98 tossed from " + from + "\n");
        }

        // ═════════════════════════ 97 ═════════════════════════
        // 人物自动巡游开关（Play 中切；桥取帧验证用——桥没法替人按 WASD）
        [MenuItem("Tools/Pool/97 人物自动巡游开关")]
        public static void ToggleAutoWalk()
        {
            var ch = Object.FindObjectOfType<WaterProject.Logic.PoolThirdPerson>();
            if (ch == null) { WriteProbe("POOL97 REFUSED: 场景里没有 PoolThirdPerson\n"); return; }
            ch.autoWalk = !ch.autoWalk;
            WriteProbe("POOL97 autoWalk -> " + ch.autoWalk + "\n");
        }

        // ═════════════════════════ 99 ═════════════════════════
        // 测试跳水（Play 中）：等价按一次空格——起跳→顶破水面→落水溅花
        [MenuItem("Tools/Pool/99 测试跳水")]
        public static void TestDive()
        {
            var ch = Object.FindObjectOfType<WaterProject.Logic.PoolThirdPerson>();
            if (ch == null) { WriteProbe("POOL99 REFUSED: 场景里没有 PoolThirdPerson\n"); return; }
            ch.TestJump();
            WriteProbe("POOL99 dive started\n");
        }

        // ═════════════════════════ 95 ═════════════════════════
        // 泳池取帧走**离屏固定比例**通路：水深是解析常数，验收不依赖深度纹理
        //（IsletScene 那台病根在这条线不成立）。1920×1080 钉死，比例框跨次可比。
        [MenuItem("Tools/Pool/95 Play 中截图(离屏固定比例)")]
        public static void CapturePool()
        {
            GameViewCapture.Request("POOL95", "Docs/screenshots/_pool_view.png",
                                    4, 25.0, 1920, 1080);
        }

        // ═════════════════════════ 96 ═════════════════════════
        // 验证注波：中心 + 贴两壁各一颗 —— 静帧应看到：中心扩散环、
        // 贴壁环撞墙折返的弧段（《07》§5 判据 1/2 的可截图版本）。
        [MenuItem("Tools/Pool/96 注波测试(中心+贴壁两点)")]
        public static void TestDrops()
        {
            var rip = Object.FindObjectOfType<WaterProject.Logic.PoolRipples>();
            if (rip == null) { WriteProbe("POOL96 REFUSED: 场景里没有 PoolRipples\n"); return; }
            rip.SpawnDrop(new Vector3(0f, 0f, 0f), 1.0f);
            rip.SpawnDrop(new Vector3(-PoolL * 0.38f, 0f, PoolW * 0.30f), 1.0f);
            rip.SpawnDrop(new Vector3(PoolL * 0.40f, 0f, -PoolW * 0.32f), 1.0f);
            WriteProbe("POOL96 test drops spawned\n");
        }
    }
}
#endif
