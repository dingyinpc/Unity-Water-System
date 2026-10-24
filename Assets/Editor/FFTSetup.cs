// FFTSetup.cs — R2 FFT 海洋场景的编辑器菜单（唯一操作入口，参数唯一写入点）
//
// 90 建场景（天空/太阳/相机/水面网格/三级模拟/S7 焦散地板[默认隐藏]）
// 91 choppy 开关（S2 演示：关=纯竖直波，开=水平挤压）
// 92 单元自检（δ谱→解析平面波：幅值+色散 ω=√(gk)，M1 门禁）
// 93 场统计（各级 max|rms 波高 + FPS —— ampScale 定标用）
// 94 焦散地板开关（S7 演示）
// 95 Play 中截图（离屏 1920×1080，与全线验收同通路）
// 96/97 波幅升/降档（×1.5 / ÷1.5）
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    internal static class FFTSetup
    {
        const string ScenePath = "Assets/Scenes/FFTScene.unity";
        const string MatFolder = "Assets/Art/Materials/FFT";
        const string MeshPath = "Assets/Art/Meshes/FFT_WaterGrid.asset";

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

        static bool PlayOnly(string tag)
        {
            if (Application.isPlaying) return true;
            WriteProbe(tag + " REFUSED: 仅限 Play 模式（先 Tools/Play/90）\n");
            return false;
        }

        static void Run(string tag, System.Action body)
        {
            try { body(); }
            catch (System.Exception e)
            {
                WriteProbe(tag + " THREW " + e.GetType().Name + " :: " + e.Message
                           + "\n" + e.StackTrace + "\n");
            }
        }

        // ── 场景准备：绝不弹模态框（NewScene 撞脏场景会弹"另存为"卡死主线程）──
        static void EnsureScene()
        {
            var cur = EditorSceneManager.GetActiveScene();
            if (cur.IsValid() && cur.isDirty)
                EditorSceneManager.SaveScene(cur, cur.path);

            var sc = EditorSceneManager.GetSceneByPath(ScenePath);
            if (sc.IsValid() && sc.isLoaded) return;
            if (System.IO.File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                            NewSceneMode.Single);
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            }
        }

        static void SaveScene()
        {
            var sc = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            EditorSceneManager.SaveScene(sc);
            WriteProbe("FFT scene saved -> " + ScenePath + "\n");
        }

        // GameObject.Find 找不到未激活对象（地板默认就是 inactive）——
        // 用场景根遍历（含 inactive），否则 94 永远 REFUSED、90 会重复建地板
        static GameObject FindAny(string name)
        {
            var sc = EditorSceneManager.GetActiveScene();
            foreach (var go in sc.GetRootGameObjects())
            {
                if (go.name == name) return go;
                var t = go.transform.Find(name);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        [MenuItem("Tools/FFT/90 建场景(天空+相机+太阳+水面+三级模拟)")]
        public static void BuildScene() => Run("FFT90", () =>
        {
            EnsureScene();

            // ── 天空：复用海岸天空（程序云+太阳晕），存独立材质不污染别线 ──
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets/Art/Materials", "FFT");
            var skySh = Shader.Find("WaterProject/Sky_Coast");
            var skyMat = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/FFT_Sky.mat");
            if (skyMat == null)
            {
                skyMat = new Material(skySh);
                AssetDatabase.CreateAsset(skyMat, MatFolder + "/FFT_Sky.mat");
            }
            RenderSettings.skybox = skyMat;

            // ── 太阳 ──
            // NewScene(DefaultGameObjects) 自带的"Directional Light"会和 FFTSun 双光源——清掉
            var defLight = GameObject.Find("Directional Light");
            if (defLight != null) Object.DestroyImmediate(defLight);
            var sunGo = GameObject.Find("FFTSun");
            if (sunGo == null) sunGo = new GameObject("FFTSun");
            sunGo.transform.eulerAngles = new Vector3(38f, 200f, 0f);
            // ⚈ 组件获取一律 Unity 重载 == null，?? 不认假 null（NewScene 同帧
            //   延迟销毁会让 GetComponent 吐出"看起来存在实则已死"的引用）
            var sun = sunGo.GetComponent<Light>();
            if (sun == null) sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.None;
            sun.color = new Color(1f, 0.96f, 0.9f);
            RenderSettings.sun = sun;

            // ── 相机 ──
            var camGo = GameObject.Find("Main Camera");
            if (camGo == null) camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            if (cam == null) cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 6f, -16f);
            cam.transform.eulerAngles = new Vector3(20f, 0f, 0f);
            cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2000f;
            if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();
            // ⚠ 相机级后处理开关：不打开它 Volume 里写什么截图都逐位不变
            //（泳池线当年的坑；离屏 cam.Render() 同样吃这个开关）
            var cd = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (cd == null) cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;   // URP14 属性名（postProcessing 是无效旧名）
            var cdCheck = camGo.GetComponent<UniversalAdditionalCameraData>();
            WriteProbe("FFT90 camData 自检: cams=" + Object.FindObjectsOfType<Camera>(true).Length
                       + " hasData=" + (cdCheck != null)
                       + " rpp=" + (cdCheck != null && cdCheck.renderPostProcessing) + "\n");

            // ── 水面网格：600×600m，256 段 ──
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                mesh = BuildGrid(256, 600f);
                if (!AssetDatabase.IsValidFolder("Assets/Art/Meshes"))
                    AssetDatabase.CreateFolder("Assets/Art", "Meshes");
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }
            var waterMat = WaterMaterial();
            var water = GameObject.Find("FFTWater");
            if (water == null) water = new GameObject("FFTWater");
            water.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var mf = water.GetComponent<MeshFilter>();
            if (mf == null) mf = water.AddComponent<MeshFilter>();
            var mr = water.GetComponent<MeshRenderer>();
            if (mr == null) mr = water.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = waterMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // ── 模拟器 ──
            var simGo = GameObject.Find("FFTSim");
            if (simGo == null) simGo = new GameObject("FFTSim");
            var sim = simGo.GetComponent<WaterProject.Logic.FFTOceanSim>();
            if (sim == null) sim = simGo.AddComponent<WaterProject.Logic.FFTOceanSim>();
            sim.computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Art/Shaders/FFTOcean.compute");
            sim.waterPlane = water.transform;
            // 探针定标每次重写（场景序列化的旧值不追新代码默认；与泳池涟漪环同惯例）
            sim.ampScale = 985f;
            sim.timeMultiplier = 1.1f;

            // ── 后处理体积（R5#7：挂 R08 调色档的前提——190 只写已存在的 Volume）──
            var volGo = FindAny("FFTVolume");
            if (volGo == null) volGo = new GameObject("FFTVolume");
            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profPath = MatFolder + "/FFTVolume.profile";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (prof == null)
            {
                prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, profPath);
            }
            if (!prof.TryGet<Tonemapping>(out var tm))
            { tm = prof.Add<Tonemapping>(); AssetDatabase.AddObjectToAsset(tm, prof); }
            tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;
            vol.sharedProfile = prof;

            // ── S7 焦散池底（默认隐藏，menu 94 开）──
            var floor = FindAny("FFTCausticsFloor");
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "FFTCausticsFloor";
                Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.transform.localScale = new Vector3(4f, 1f, 4f);   // Plane 原生 10m → 40m
                floor.transform.position = new Vector3(0f, -4.5f, 0f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = CausticsMaterial();
                floor.SetActive(false);
            }

            SaveScene();
            WriteProbe("FFT90 built. sky=" + skyMat.name + " sim="
                       + (sim.computeShader != null ? "compute ok" : "COMPUTE MISSING") + "\n");
        });

        static Material WaterMaterial()
        {
            string p = MatFolder + "/Water_FFT.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/Water_FFT");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            return m;
        }

        static Material CausticsMaterial()
        {
            string p = MatFolder + "/FFT_Caustics.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("WaterProject/FFTCausticsFloor");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            else m.shader = sh;
            return m;
        }

        static Mesh BuildGrid(int seg, float size)
        {
            int n = seg + 1;
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            float step = size / seg;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    v[z * n + x] = new Vector3((x - seg * 0.5f) * step, 0f, (z - seg * 0.5f) * step);
                    uv[z * n + x] = new Vector2((float)x / seg, (float)z / seg);
                }
            var idx = new int[seg * seg * 6];
            int w = 0;
            for (int z = 0; z < seg; z++)
                for (int x = 0; x < seg; x++)
                {
                    int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                    idx[w++] = a; idx[w++] = c; idx[w++] = b;
                    idx[w++] = b; idx[w++] = c; idx[w++] = d;
                }
            var mesh = new Mesh
            {
                name = "FFT_WaterGrid",
                // ⚠ 257×257=66049 顶点 > 65535，16 位索引会整块网格渲染失败
                indexFormat = IndexFormat.UInt32,
            };
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = idx;
            mesh.RecalculateNormals();
            return mesh;
        }

        [MenuItem("Tools/FFT/91 choppy 开关(S2 演示)")]
        public static void ToggleChoppy() => Run("FFT91", () =>
        {
            if (!PlayOnly("FFT91")) return;
            var sim = Object.FindObjectOfType<WaterProject.Logic.FFTOceanSim>();
            if (sim == null) { WriteProbe("FFT91 REFUSED: 无 FFTOceanSim\n"); return; }
            sim.choppy = sim.choppy > 0.5f ? 0f : 1f;
            WriteProbe("FFT91 choppy -> " + sim.choppy + "\n");
        });

        [MenuItem("Tools/FFT/92 单元自检(δ谱→解析平面波)")]
        public static void SelfTest() => Run("FFT92", () =>
        {
            if (!PlayOnly("FFT92")) return;
            var sim = Object.FindObjectOfType<WaterProject.Logic.FFTOceanSim>();
            if (sim == null) { WriteProbe("FFT92 REFUSED: 无 FFTOceanSim\n"); return; }
            WriteProbe(sim.SelfTest() + "\n");
        });

        [MenuItem("Tools/FFT/93 场统计(波高+FPS, 定标用)")]
        public static void Stats() => Run("FFT93", () =>
        {
            if (!PlayOnly("FFT93")) return;
            var sim = Object.FindObjectOfType<WaterProject.Logic.FFTOceanSim>();
            if (sim == null) { WriteProbe("FFT93 REFUSED\n"); return; }
            WriteProbe("FFT93 " + sim.ReadStats() + "avgMs=" + sim.AvgMs.ToString("F1") + "\n");
        });

        [MenuItem("Tools/FFT/94 焦散地板开关(S7 演示)")]
        public static void ToggleFloor() => Run("FFT94", () =>
        {
            if (!PlayOnly("FFT94")) return;
            var f = FindAny("FFTCausticsFloor");
            if (f == null) { WriteProbe("FFT94 REFUSED: 无地板（先跑 90）\n"); return; }
            bool on = !f.activeSelf;
            f.SetActive(on);
            // S7 是"纯演示"：水面不透明（Geometry 队列）挡住池底——开地板时
            // 同步关掉水面网格，直接展示单层 FFT 焦散网（参考 0:42–0:46 的看法）
            var water = FindAny("FFTWater");
            if (water != null) water.GetComponent<MeshRenderer>().enabled = !on;
            // 焦散观感参数每次重写（材质资产序列化的是创建当天的默认值）
            var fmr = f.GetComponent<MeshRenderer>();
            if (fmr != null && fmr.sharedMaterial != null)
            {
                // 纯演示件，允许非物理增益：L1 曲率 ~0.01 量级，×300 才能整网点亮
                fmr.sharedMaterial.SetFloat("_CausticScale", 300.0f);
                fmr.sharedMaterial.SetFloat("_CausticPow", 1.5f);
                fmr.sharedMaterial.SetFloat("_Fade", 120f);
            }
            // 演示机位：朝下看地板（38°），关闭时还原常规海面机位（20°）
            var cam = Camera.main;
            if (cam != null) cam.transform.eulerAngles = new Vector3(on ? 38f : 20f, 0f, 0f);
            WriteProbe("FFT94 floor -> " + on + " waterMesh -> " + !on + "\n");
        });

        [MenuItem("Tools/FFT/95 Play 中截图(离屏固定比例)")]
        public static void Capture() => Run("FFT95", () =>
        {
            GameViewCapture.Request("FFT95", "Docs/screenshots/_fft_view.png", 4, 25.0, 1920, 1080);
        });

        [MenuItem("Tools/FFT/96 波幅升档(×1.5)")]
        public static void AmpUp() => Run("FFT96", () =>
        {
            if (!PlayOnly("FFT96")) return;
            var sim = Object.FindObjectOfType<WaterProject.Logic.FFTOceanSim>();
            if (sim == null) { WriteProbe("FFT96 REFUSED\n"); return; }
            sim.ampScale *= 1.5f;
            sim.Init();
            WriteProbe("FFT96 ampScale -> " + sim.ampScale.ToString("F3") + " " + sim.ReadStats() + "\n");
        });

        [MenuItem("Tools/FFT/97 波幅降档(÷1.5)")]
        public static void AmpDown() => Run("FFT97", () =>
        {
            if (!PlayOnly("FFT97")) return;
            var sim = Object.FindObjectOfType<WaterProject.Logic.FFTOceanSim>();
            if (sim == null) { WriteProbe("FFT97 REFUSED\n"); return; }
            sim.ampScale /= 1.5f;
            sim.Init();
            WriteProbe("FFT97 ampScale -> " + sim.ampScale.ToString("F3") + " " + sim.ReadStats() + "\n");
        });
    }
}
#endif
