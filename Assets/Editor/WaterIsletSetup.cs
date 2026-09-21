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
        static readonly Color SkyZenith  = new Color(0.004f, 0.300f, 0.800f);
        // 地平色在加了 _GradNorm 之后可以回到"淡"：它现在只负责画幅下缘那一小段，
        // 不再被迫承担整片天的颜色（上一版把 R 压到 0.20 就是因为这个）。
        static readonly Color SkyHorizon = new Color(0.300f, 0.620f, 0.850f);
        static readonly Color SkyGround  = new Color(0.180f, 0.230f, 0.260f);
        const float SkyGradPow  = 1.4f;
        const float SkyGradNorm = 0.42f;   // ≈ 仰角 25°，把天顶色落到画幅上缘
        // 雾色 == 天空地平色（唯一来源）。热带空气干净，密度比海岸线低一个量级：
        // 这里要的是"远处岛退成淡影"，不是"整片天糊白"。
        const float FogDensity = 0.00085f;

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
        const float SunPitch = 24f;
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
            m.SetColor("_CloudLit",   new Color(1.600f, 1.660f, 1.720f));
            m.SetColor("_CloudShade", new Color(0.260f, 0.460f, 0.660f));
            m.SetFloat("_CloudBase",   1400f);
            m.SetFloat("_CloudTop",    4200f);
            m.SetFloat("_CloudFreq",   0.00042f);
            // 0.42 而不是 0.55：参考3/4 里云之间是**大片纯蓝**，不是连成片的阴天。
            // 阈值映射 t = lerp(0.74, 0.40, cover)，cover 越小阈值越高、云越碎。
            m.SetFloat("_CloudCover",  0.42f);
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
