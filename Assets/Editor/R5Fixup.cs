// R5Fixup.cs — R5 小项集中修复轮的参数包（一次刷四个水面材质）
//
// 数值与各 Setup 文件里的重建默认**同值互备**（WaterPondSetup/WaterOceanSetup/
// WaterProjectSetup/WaterPoolSetup）——改任一处必须同步另一处。
// 来源：R1 五场景四判据基线（R09 报告）的调参靶子。
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    internal static class R5Fixup
    {
        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

        [MenuItem("Tools/Fix/591 应用 R5 参数包(四水面节奏/平移)")]
        public static void Apply()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsSpeed", 0.5f);   // #1 S8 节奏 0.5×
            n += Set("Assets/Art/Materials/Water_Pond.mat", "_WaveSpeed", 0.22f);      // #2 S6 节奏 0.4×
            n += Set("Assets/Art/Materials/Ocean_Water.mat", "_SwellSpeed", 0.45f);    // #3 滚图 ÷4
            n += Set("Assets/Art/Materials/Ocean_Water.mat", "_ShimmerAmt", 1.0f);   // R16-G1 微光满档
            n += Set("Assets/Art/Materials/Ocean_Water.mat", "_ShimmerScale", 1.4f); // 远距离可分辨的特征尺度
            n += Set("Assets/Art/Materials/Water_Advanced.mat", "_WaveTimeScale", 0.45f); // #4 平移 ÷4
            WriteProbe($"R591 applied x{n}: Pool焦散0.5 Pond波0.22 Ocean涌0.45 Main波0.45\n");
        }

        [MenuItem("Tools/Fix/592 FFT 调色微调(曝光+1.6/饱和-45/对比+22)")]
        public static void FFTGradeTune()
        {
            // 从场景里的 Volume 取 sharedProfile（LoadAssetAtPath 对刚建的
            // .profile 偶发取不到——StyleSetup 190 也走场景对象）
            var vol = Object.FindObjectOfType<Volume>();
            if (vol == null || vol.sharedProfile == null)
            { WriteProbe("R592 REFUSED: 当前场景无 Volume/profile（先 90+190）\n"); return; }
            var prof = vol.sharedProfile;
            // 190 依赖已存在的覆盖；FFT 的 profile 曾被 90 重建过一次导致空壳——
            // 592 自建所需覆盖，缺什么补什么（自足，不依赖 190 先跑过）
            if (!prof.TryGet<ColorAdjustments>(out var col))
            {
                col = prof.Add<ColorAdjustments>(true);
                AssetDatabase.AddObjectToAsset(col, prof);
            }
            col.postExposure.overrideState = true;
            col.saturation.overrideState = true;
            col.contrast.overrideState = true;
            col.postExposure.value = 1.3f;   // 190 的 -0.30 把 FFT 压到亮度33，回抬
            col.saturation.value = -45f;
            col.contrast.value = 22f;
            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssetIfDirty(prof);
            WriteProbe($"R592 FFT 调色({prof.name}): exp=+1.3 sat=-45 contrast=+22\n");
        }

        [MenuItem("Tools/Fix/593 应用场景调色对表(读 Docs/Temp/grade.json)")]
        public static void GradeApply()
        {
            // 行格式：场景名 exp sat con（python 调参器写，菜单只管应用）
            string path = System.IO.Path.Combine(
                Application.dataPath, "../Docs/Temp/grade.json");
            if (!System.IO.File.Exists(path))
            { WriteProbe("R593 REFUSED: Docs/Temp/grade.json 不存在\n"); return; }
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            float exp = 0, sat = 0, con = 0; bool hit = false;
            foreach (var ln in System.IO.File.ReadAllLines(path))
            {
                var p = ln.Split(' ');
                if (p.Length >= 4 && p[0] == scene)
                { hit = true; float.TryParse(p[1], out exp); float.TryParse(p[2], out sat); float.TryParse(p[3], out con); break; }
            }
            if (!hit) { WriteProbe($"R593 REFUSED: json 无 {scene} 行\n"); return; }

            var vol = Object.FindObjectOfType<Volume>();
            bool created = false;
            if (vol == null)
            {
                var go = new GameObject("GradeVolume");
                vol = go.AddComponent<Volume>();
                vol.isGlobal = true;
                created = true;
            }
            if (vol.sharedProfile == null)
            {
                var prof = ScriptableObject.CreateInstance<VolumeProfile>();
                string pp = $"Assets/Art/Materials/{scene}_Grade.profile";
                AssetDatabase.CreateAsset(prof, pp);
                vol.sharedProfile = prof;
            }
            var pr = vol.sharedProfile;
            if (!pr.TryGet<Tonemapping>(out var tm))
            { tm = pr.Add<Tonemapping>(true); AssetDatabase.AddObjectToAsset(tm, pr); }
            tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;
            if (!pr.TryGet<ColorAdjustments>(out var ca))
            { ca = pr.Add<ColorAdjustments>(true); AssetDatabase.AddObjectToAsset(ca, pr); }
            ca.postExposure.overrideState = true; ca.postExposure.value = exp;
            ca.saturation.overrideState = true; ca.saturation.value = sat;
            ca.contrast.overrideState = true; ca.contrast.value = con;
            EditorUtility.SetDirty(pr); AssetDatabase.SaveAssetIfDirty(pr);
            if (created) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe($"R593 {scene}: exp={exp} sat={sat} con={con}{(created ? " (新建Volume已标脏)" : "")}\n");
        }

        [MenuItem("Tools/Fix/595 回读当前调色(写探针)")]
        public static void GradeDump()
        {
            var vol = Object.FindObjectOfType<Volume>();
            if (vol == null || vol.sharedProfile == null)
            { WriteProbe("R595 无 Volume/profile\n"); return; }
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (vol.sharedProfile.TryGet<ColorAdjustments>(out var ca))
                WriteProbe($"GRADE cur {scene} {ca.postExposure.value} {ca.saturation.value} {ca.contrast.value}\n");
            else WriteProbe($"GRADE cur {scene} 无ColorAdjustments\n");
        }

        // ── R21 涟漪手感 v2 参数包：组件/材质一次对齐（场景里序列化的旧值唯一刷新点）──
        [MenuItem("Tools/Fix/594 Pool 涟漪手感v2参数包(波速1.15/0.45+环同步, 存场景)")]
        public static void PoolFeelV2()
        {
            if (Application.isPlaying) { WriteProbe("R594 REFUSED: Play 中不存场景（退 Play 再跑）\n"); return; }
            var rip = Object.FindObjectOfType<WaterProject.Logic.PoolRipples>();
            if (rip == null) { WriteProbe("R594 REFUSED: 无 PoolRipples（先开 PoolScene/跑 Pool/92）\n"); return; }

            // 双场波速系数：c = dx·√K/dt（dx=4.6875cm, dt=1/60）→ A=1.15 m/s, B=0.45 m/s
            // （R23 曾试降冲量治 7.5Hz 振荡，实测无效且破坏 101 自测校准，已回 R21 定标值；
            //   拍频系泵采集(≈50fps)对固定率累加器的采样伪影，游戏 60fps 下不存在）
            rip.simHz = 60f;
            rip.maxCatchup = 3;
            rip.waveKA = 0.167f; rip.waveKB = 0.026f;
            rip.smoothA = 0.05f; rip.smoothB = 0.09f;
            rip.dampA = 0.9939f; rip.dampB = 0.985f;
            rip.sigmaA = 0.26f;  rip.sigmaB = 0.11f;
            rip.gainA = 1.0f;    rip.gainB = 1.5f;
            rip.dropStrength = 0.85f;

            var rings = Object.FindObjectOfType<WaterProject.Logic.PoolRippleRings>();
            if (rings != null)
            {
                rings.life = 1.9f; rings.stagger = 0.34f;
                rings.expandV0 = 1.9f; rings.decel = 0.45f;
                rings.bandWidth = 0.50f;
                rings.foamColor = new Color(0.92f, 0.98f, 1.0f, 0.9f);
            }

            // R24 尾迹加强档：步频 0.21s（场景序列化值由这里刷新）
            var ch = Object.FindObjectOfType<WaterProject.Logic.PoolThirdPerson>();
            if (ch != null) ch.moveRippleInterval = 0.21f;

            // 水面材质：细纹理尾波 + 微光动态退场（新属性缺省即此值，写一遍防漂移）
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Pool.mat");
            if (m != null)
            {
                m.SetFloat("_PoolFineAmp", 0.55f);
                m.SetFloat("_ShimmerMix", 0.55f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
            }

            EditorUtility.SetDirty(rip);
            if (rings != null) EditorUtility.SetDirty(rings);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rip.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(rip.gameObject.scene);
            float dts = 1f / rip.simHz, dxm = rip.fieldSize.x / rip.texW;
            WriteProbe($"R594 feelV2 stamped K=({rip.waveKA},{rip.waveKB}) "
                     + $"c=({dxm * Mathf.Sqrt(rip.waveKA) / dts:F2},{dxm * Mathf.Sqrt(rip.waveKB) / dts:F2})m/s "
                     + $"rings={(rings != null)} mat={(m != null)} sceneSaved\n");
        }

        [MenuItem("Tools/Fix/597 Main SSR强度切换(0↔0.85, 写材质)")]
        public static void MainSSRToggle()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m == null) { WriteProbe("R597 REFUSED\n"); return; }
            float v = m.GetFloat("_SSRStrength") > 0.1f ? 0f : 0.85f;
            m.SetFloat("_SSRStrength", v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            WriteProbe($"R597 _SSRStrength -> {v}\n");
        }

        [MenuItem("Tools/Fix/598 Main 波幅切换(0↔常值, 写材质)")]
        public static void MainWaveToggle()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m == null) { WriteProbe("R598 REFUSED\n"); return; }
            float v = m.GetFloat("_WaveAmpScale") > 0.1f ? 0f : 1.0f;
            m.SetFloat("_WaveAmpScale", v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            WriteProbe($"R598 _WaveAmpScale -> {v}\n");
        }

        [MenuItem("Tools/Fix/599 Main 交互演示 autoDemo 切换(Play)")]
        public static void MainDemoToggle()
        {
            if (!Application.isPlaying) { WriteProbe("R599 REFUSED: 仅 Play\n"); return; }
            var w = Object.FindObjectOfType<WaterProject.Logic.WaterInteractionManager>();
            if (w == null) { WriteProbe("R599 REFUSED: 无 WaterInteractionManager\n"); return; }
            w.autoDemo = !w.autoDemo;
            WriteProbe($"R599 autoDemo -> {w.autoDemo} (interval={w.demoInterval})\n");
        }

        [MenuItem("Tools/Fix/596 Main demoInterval 写 12 并存场景")]
        public static void MainDemoInterval()
        {
            var w = Object.FindObjectOfType<WaterProject.Logic.WaterInteractionManager>();
            if (w == null) { WriteProbe("R596 REFUSED: 场景无组件（先开 Main）\n"); return; }
            w.demoInterval = 12f; w.autoDemo = true;
            EditorUtility.SetDirty(w);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            WriteProbe("R596 demoInterval=12 已存场景\n");
        }

        [MenuItem("Tools/Fix/595b Main 水体探针(只读)")]
        public static void MainWaterProbe()
        {
            var go = GameObject.Find("Water");
            if (go == null) { WriteProbe("MWProbe: 无 Water 对象\n"); return; }
            var r = go.GetComponent<MeshRenderer>();
            var m = r != null ? r.sharedMaterial : null;
            var cam = Camera.main;
            WriteProbe("MWProbe: renderer=" + (r != null && r.enabled)
                + " mat=" + (m != null ? m.name + "/" + m.shader.name : "null")
                + " pos=" + go.transform.position.ToString("F1")
                + " cam=" + (cam != null ? cam.transform.position.ToString("F1") + " euler=" + cam.transform.eulerAngles.ToString("F0") : "null") + "\n");
        }

        // ── R20 高频密度：Pool 焦散参考档（S3 纹理密度向参考 S8 收）+ 瓷砖微细节 ──
        // 实测反转：S8 参考水区仅 0.6% 近白像素，我们 15%——不是缺高光，是水太亮太清。
        // 深青档：吸收↑/不透明度↑把白瓷砖底压下去，焦散回到中档（亮线要暗底才成立）。
        [MenuItem("Tools/Fix/584 Pool 水色=参考深青档(Absorb1.2/Opacity0.45/焦散0.65)")]
        public static void PoolWaterDeep()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Absorb", 1.2f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Opacity", 0.45f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsStrength", 0.65f);
            WriteProbe($"R584 water deep x{n}\n");
        }

        [MenuItem("Tools/Fix/584b Pool 水色=还原(0.55/0.30/0.55)")]
        public static void PoolWaterRevert()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Absorb", 0.55f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Opacity", 0.30f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsStrength", 0.55f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_WaterColor", new Color(0.10f, 0.55f, 0.62f, 1f));
            WriteProbe($"R584b water revert x{n}\n");
        }

        // 深青加强档：视觉判词"底黑网亮"+参考水区 p10=1(近黑)/p90=185 vs 我们 154/222
        // ——白亮底压到近黑，焦散网加亮浮上去，动态范围才有参考的形状
        // ── R22b 性能体检：当前场景 Play 中阻塞采样 150 帧（Step 泵墙钟）──
        // ⚠ 不能在菜单里等 Play 转换（阻塞主线程=自死锁，R582 首版全灭的教训）。
        //   编排：bash 逐场景 [开场景→Play/90→582→Play/91]。绝对值含编辑器开销=上界，
        //   场景间相对比较与历史数值对照使用。
        [MenuItem("Tools/Fix/582 性能采样(当前场景Play中,阻塞150帧,写探针)")]
        public static void PerfSample()
        {
            if (!Application.isPlaying) { WriteProbe("R582 REFUSED: 仅 Play（先 Tools/Play/90）\n"); return; }
            var times = new System.Collections.Generic.List<float>(160);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool wasPaused = EditorApplication.isPaused;
            EditorApplication.isPaused = true;
            try
            {
                for (int i = 0; i < 150; i++)
                {
                    if (!Application.isPlaying) break;
                    sw.Restart();
                    EditorApplication.Step();
                    System.Threading.Thread.Sleep(1);      // 让出编辑器消息泵
                    if (i >= 30) times.Add(sw.ElapsedMilliseconds);   // 前 30 帧当热身
                }
            }
            finally { if (!wasPaused) EditorApplication.isPaused = false; }

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (times.Count < 60) { WriteProbe($"R582 {scene}: 样本不足 n={times.Count}\n"); return; }
            var arr = times.ToArray();
            System.Array.Sort(arr);
            float avg = 0f; foreach (var t in arr) avg += t; avg /= arr.Length;
            WriteProbe($"R582 {scene,-10}: n={arr.Length} avg={avg:F1}ms p50={arr[arr.Length / 2]:F1}ms "
                     + $"p95={arr[(int)(arr.Length * 0.95f)]:F1}ms max={arr[arr.Length - 1]:F1}ms "
                     + $"({1000f / avg:F0} fps 上界)\n");
        }

        // ── R24 构图轮：暗角档（最低成本的低频杠杆——只压画幅边缘，不动全局曝光）──
        // 诊断：全线中低频超额 9~13×、近黑占比 1~17% vs 参考 35~40%；暗角把四角
        // 大尺度功率压下去 ⇒ S2 斜率变平（低频降=曲线更平），同时贡献近黑面积。
        [MenuItem("Tools/Fix/581 构图暗角=0.55(当前场景)")]
        public static void VignetteStrong()
        {
            var vol = Object.FindObjectOfType<Volume>();
            if (vol == null || vol.sharedProfile == null) { WriteProbe("R581 REFUSED: 无 Volume\n"); return; }
            var pr = vol.sharedProfile;
            if (!pr.TryGet<UnityEngine.Rendering.Universal.Vignette>(out var vg))
            { var g = pr.Add<UnityEngine.Rendering.Universal.Vignette>(true); AssetDatabase.AddObjectToAsset(g, pr); vg = g; }
            vg.intensity.Override(0.55f);
            vg.smoothness.Override(0.55f);
            EditorUtility.SetDirty(pr); AssetDatabase.SaveAssetIfDirty(pr);
            WriteProbe($"R581 vignette 0.55 ({UnityEngine.SceneManagement.SceneManager.GetActiveScene().name})\n");
        }

        [MenuItem("Tools/Fix/581b 构图暗角=还原0.30(当前场景)")]
        public static void VignetteRevert()
        {
            var vol = Object.FindObjectOfType<Volume>();
            if (vol == null || vol.sharedProfile == null) { WriteProbe("R581b REFUSED: 无 Volume\n"); return; }
            if (vol.sharedProfile.TryGet<UnityEngine.Rendering.Universal.Vignette>(out var vg))
            {
                vg.intensity.Override(0.30f);
                vg.smoothness.Override(0.45f);
                EditorUtility.SetDirty(vol.sharedProfile); AssetDatabase.SaveAssetIfDirty(vol.sharedProfile);
            }
            WriteProbe("R581b vignette 0.30\n");
        }

        // R24 插值档：Main 实测 0.30→近黑13.5% / 0.55→61.9%，目标 35.6% ⇒ ≈0.42
        [MenuItem("Tools/Fix/581c 构图暗角=0.42(当前场景)")]
        public static void VignetteMid()
        {
            var vol = Object.FindObjectOfType<Volume>();
            if (vol == null || vol.sharedProfile == null) { WriteProbe("R581c REFUSED: 无 Volume\n"); return; }
            var pr = vol.sharedProfile;
            if (!pr.TryGet<UnityEngine.Rendering.Universal.Vignette>(out var vg))
            { var g = pr.Add<UnityEngine.Rendering.Universal.Vignette>(true); AssetDatabase.AddObjectToAsset(g, pr); vg = g; }
            vg.intensity.Override(0.42f);
            vg.smoothness.Override(0.50f);
            EditorUtility.SetDirty(pr); AssetDatabase.SaveAssetIfDirty(pr);
            WriteProbe($"R581c vignette 0.42 ({UnityEngine.SceneManagement.SceneManager.GetActiveScene().name})\n");
        }

        // ── R22a C7 抗闪：SSRTemporalFeature 的安装/卸载（唯一写入点）──
        // 装上即生效：特性每帧把 _SSRTemporalOn 置 1（预通道+EMA+查表）；
        // 卸载后全局归 0，水面回旧内联追踪路径，逐位一致零回归。
        [MenuItem("Tools/Fix/583 SSR C7 时域累积=安装(注入 RendererFeature)")]
        public static void SSRTemporalInstall()
        {
            const string rendPath = "Assets/Settings/URP/WaterURPRenderer.asset";
            var rend = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(rendPath);
            if (rend == null) { WriteProbe("R583 REFUSED: 找不到 " + rendPath + "\n"); return; }

            // 幂等：已装则只报状态
            foreach (var f in rend.rendererFeatures)
                if (f is WaterProject.Render.SSRTemporalFeature)
                { WriteProbe("R583 already installed\n"); return; }

            var feat = ScriptableObject.CreateInstance<WaterProject.Render.SSRTemporalFeature>();
            feat.name = "SSRTemporalFeature";
            AssetDatabase.AddObjectToAsset(feat, rend);
            rend.rendererFeatures.Add(feat);
            EditorUtility.SetDirty(rend);
            AssetDatabase.SaveAssetIfDirty(rend);
            WriteProbe($"R583 installed: features={rend.rendererFeatures.Count} (saved)\n");
        }

        [MenuItem("Tools/Fix/583b SSR C7 时域累积=卸载(回旧内联路径)")]
        public static void SSRTemporalRemove()
        {
            const string rendPath = "Assets/Settings/URP/WaterURPRenderer.asset";
            var rend = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(rendPath);
            if (rend == null) { WriteProbe("R583b REFUSED: 找不到 " + rendPath + "\n"); return; }
            int removed = 0;
            for (int i = rend.rendererFeatures.Count - 1; i >= 0; i--)
                if (rend.rendererFeatures[i] is WaterProject.Render.SSRTemporalFeature)
                {
                    var f = rend.rendererFeatures[i];
                    rend.rendererFeatures.RemoveAt(i);
                    AssetDatabase.RemoveObjectFromAsset(f);
                    Object.DestroyImmediate(f);
                    removed++;
                }
            if (removed > 0)
            {
                Shader.SetGlobalFloat("_SSRTemporalOn", 0f);
                EditorUtility.SetDirty(rend);
                AssetDatabase.SaveAssetIfDirty(rend);
            }
            WriteProbe($"R583b removed={removed} features={rend.rendererFeatures.Count}\n");
        }

        // 底黑网亮·终档(v2 收敛)：v1(4.5×/指数7)静态百分位对上了参考但 D1 运动能量
        // ρ 0.308→17——亮线扫过暗底的逐帧摆幅过大。收敛：2.8×/指数5.5/漂移0.35。
        [MenuItem("Tools/Fix/584d Pool 水色=底黑网亮终档(Opacity0.88/焦散2.8/指数5.5/速0.35)")]
        public static void PoolWaterDeep3()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Absorb", 2.4f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Opacity", 0.88f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsStrength", 2.8f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticSharp", 5.5f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsSpeed", 0.35f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_WaterColor", new Color(0.010f, 0.075f, 0.100f, 1f));
            WriteProbe($"R584d water deep3 x{n}\n");
        }

        // 网锐度档：焦散图案 pow3 的谷底还有 ~0.4，强度 4.5 时把水柱散射项整体抬 +60 灰
        //（p10 卡 124 的最后一层）；指数 7 谷底压 0、网线保持——"底黑网亮"的形状
        [MenuItem("Tools/Fix/584e Pool 焦散网=锐利档(指数7)")]
        public static void PoolCausticSharp()
        {
            int n = Set("Assets/Art/Materials/Water_Pool.mat", "_CausticSharp", 7.0f);
            WriteProbe($"R584e caustic sharp x{n}\n");
        }

        // 深青加强档：视觉判词"底黑网亮"+参考水区 p10=1(近黑)/p90=185 vs 我们 154/222
        // ——白亮底压到近黑，焦散网加亮浮上去，动态范围才有参考的形状
        [MenuItem("Tools/Fix/584c Pool 水色=底黑网亮档(Absorb2.4/Opacity0.62/深青/焦散0.85)")]
        public static void PoolWaterDeep2()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Absorb", 2.4f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_Opacity", 0.62f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsStrength", 0.85f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_WaterColor", new Color(0.045f, 0.30f, 0.36f, 1f));
            WriteProbe($"R584c water deep2 x{n}\n");
        }

        [MenuItem("Tools/Fix/585 Pool 亮闪=热点档(S3 p90 14→参考带, 稀而炽亮)")]
        public static void PoolSparkleHot()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_SparkleGate", 0.978f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_SparkleGain", 4.0f);
            WriteProbe($"R585 sparkle hot x{n}\n");
        }

        [MenuItem("Tools/Fix/585b Pool 亮闪=还原(0.960/2.5)")]
        public static void PoolSparkleRevert()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_SparkleGate", 0.960f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_SparkleGain", 2.5f);
            WriteProbe($"R585b sparkle revert x{n}\n");
        }

        [MenuItem("Tools/Fix/587 Pool 焦散密度=参考S8档(强0.85/密0.85/偏细网0.22)")]
        public static void PoolCausticDense()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsStrength", 0.85f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsScale", 0.85f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticDepthMix", 0.22f);
            WriteProbe($"R587 caustics dense x{n}\n");
        }

        [MenuItem("Tools/Fix/587b Pool 焦散密度=还原(0.55/0.55/0.35)")]
        public static void PoolCausticRevert()
        {
            int n = 0;
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsStrength", 0.55f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticsScale", 0.55f);
            n += Set("Assets/Art/Materials/Water_Pool.mat", "_CausticDepthMix", 0.35f);
            WriteProbe($"R587b caustics revert x{n}\n");
        }

        [MenuItem("Tools/Fix/586 Pool 瓷砖微细节=1(3cm斑驳+砖缝AO)")]
        public static void PoolTileMicroOn()
        {
            int n = Set("Assets/Art/Materials/Pool_Tile.mat", "_MicroAmt", 1f);
            WriteProbe($"R586 tile micro on x{n}\n");
        }

        [MenuItem("Tools/Fix/586b Pool 瓷砖微细节=0(关)")]
        public static void PoolTileMicroOff()
        {
            int n = Set("Assets/Art/Materials/Pool_Tile.mat", "_MicroAmt", 0f);
            WriteProbe($"R586b tile micro off x{n}\n");
        }

        // ── R20 实锤修复：全场景相机 renderPostProcessing——Pool 主相机一直是 False，
        //    后处理整条链(ACES/调色/颗粒)在这台相机上从未生效过（离屏/GameView 都不生效；
        //    S2 颗粒 A/B 三档天空 std 一模一样 + 调色开/关 mean-abs-diff 仅 2.83 两条证据）。
        //    R13 只在个别场景修过属性名，没有全场景扫过一遍。
        [MenuItem("Tools/Fix/588 全场景相机后处理=开(renderPostProcessing, 存场景)")]
        public static void EnableCameraPostAllScenes()
        {
            if (Application.isPlaying) { WriteProbe("R588 REFUSED: Play 中不开场景\n"); return; }
            string[] scenes = { "Main", "OceanScene", "PondScene", "PoolScene", "IsletScene", "FFTScene" };
            var sb = new System.Text.StringBuilder("R588 camera post-processing sweep:\n");
            foreach (var s in scenes)
            {
                string path = "Assets/Scenes/" + s + ".unity";
                if (!System.IO.File.Exists(path)) { sb.Append("  " + s + ": 文件不存在，跳过\n"); continue; }
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                int fixedN = 0, camN = 0;
                foreach (var cam in Object.FindObjectsOfType<Camera>(true))
                {
                    camN++;
                    var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    if (data == null)
                        data = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    if (!data.renderPostProcessing) { data.renderPostProcessing = true; fixedN++; }
                }
                if (fixedN > 0)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                }
                sb.Append("  " + s + ": cameras=" + camN + " enabledNow=" + fixedN
                        + (fixedN > 0 ? " (saved)" : " (already on)") + "\n");
            }
            WriteProbe(sb.ToString());
        }

        // ── R20 感官复刻：观看尺度颗粒（2~4px blob lookup，Custom 档）──
        // R19 教训：URP 预设颗粒纹素=1 屏幕像素，对比降采样(1080p→432p)后平均掉了(仅+6%)。
        // 本档自造 lookup：alpha=周期值噪声(4px 主 + 2px 细节 + 8px 团块，格点数都是 256
        // 的整除因子 → 无缝平铺)，blob 上屏 2~4px，降采样后仍存活，S2 斜率才有得救。
        // URP 侧逐帧随机偏移(时序沸腾白送)；幅度=alpha 偏离 0.5(乘性，ApplyGrain)。
        static float LatticeNoise(int xi, int yi, int cells, uint seed)
        {
            int wx = ((xi % cells) + cells) % cells, wy = ((yi % cells) + cells) % cells;
            uint h = (uint)(wx * 73856093) ^ (uint)(wy * 19349663) ^ seed;
            h = (h ^ (h >> 13)) * 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777215f;
        }

        static float PeriodicValueNoise(float x, float y, int cells, uint seed)
        {
            float gx = x * cells, gy = y * cells;
            int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy);
            float fx = gx - x0, fy = gy - y0;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            float a = LatticeNoise(x0, y0, cells, seed),     b = LatticeNoise(x0 + 1, y0, cells, seed);
            float c = LatticeNoise(x0, y0 + 1, cells, seed), d = LatticeNoise(x0 + 1, y0 + 1, cells, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        const string GrainTexPath = "Assets/Art/Textures/GrainViewScale.png";
        static float s_grainIntensity = 0.5f;   // R20 定标：skyGrain≈11.7(4.6%调制)/S3 6×；0.7 过强

        [MenuItem("Tools/Fix/589 感官颗粒=观看尺度档(2~4px blob, Custom)")]
        public static void GrainViewScaleOn()
        {
            var vol = Object.FindObjectOfType<Volume>();
            if (vol == null || vol.sharedProfile == null)
            { WriteProbe("R589 REFUSED: 无 Volume\n"); return; }

            // ── 生成/复用 lookup 资产 ──
            const int N = 256;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GrainTexPath);
            if (tex == null)
            {
                var px = new Color32[N * N];
                // 归一化前先攒 float，再整体拉到 mean=0.5 / 目标 σ
                var vals = new float[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = x / (float)N, v = y / (float)N;
                        // 4px 主噪(64格) + 2px 细节(128格,弱) + 8px 团块(32格) + 16px 宏块(16格)
                        // —— R24：S2 斜率差的形状在中频段(r20-80)，16px 八度=压缩宏块噪，
                        //    参考视频(重度压缩手机录屏)频谱的签名层；格点数都是 256 整除因子
                        float n = PeriodicValueNoise(u, v, 64, 0x9e37u) * 1.00f
                                + PeriodicValueNoise(u, v, 128, 0x51edu) * 0.35f
                                + PeriodicValueNoise(u, v, 32, 0x7f4au) * 0.75f
                                + PeriodicValueNoise(u, v, 16, 0x2b99u) * 0.60f;
                        vals[y * N + x] = n;
                    }
                float mean = 0f; foreach (var f in vals) mean += f; mean /= vals.Length;
                float ss = 0f; foreach (var f in vals) ss += (f - mean) * (f - mean);
                float sd = Mathf.Sqrt(ss / vals.Length);
                const float targetSd = 0.090f;             // alpha σ：R20 定标(0.055 只到 S2 -2.74)
                for (int i = 0; i < vals.Length; i++)
                {
                    float a = 0.5f + (vals[i] - mean) / Mathf.Max(sd, 1e-5f) * targetSd;
                    byte ab = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    px[i] = new Color32(255, 255, 255, ab);
                }
                var t2 = new Texture2D(N, N, TextureFormat.RGBA32, false, true) { name = "GrainViewScale" };
                t2.SetPixels32(px); t2.Apply();
                System.IO.Directory.CreateDirectory("Assets/Art/Textures");
                System.IO.File.WriteAllBytes(GrainTexPath, t2.EncodeToPNG());
                Object.DestroyImmediate(t2);
                AssetDatabase.ImportAsset(GrainTexPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(GrainTexPath);
                if (ti != null)
                {
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.sRGBTexture = false;
                    ti.alphaIsTransparency = false;
                    ti.SaveAndReimport();
                }
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GrainTexPath);
            }
            if (tex == null) { WriteProbe("R589 REFUSED: lookup 生成失败\n"); return; }

            var pr = vol.sharedProfile;
            if (!pr.TryGet<FilmGrain>(out var fg))
            { fg = pr.Add<FilmGrain>(true); AssetDatabase.AddObjectToAsset(fg, pr); }
            fg.type.overrideState = true; fg.type.value = FilmGrainLookup.Custom;
            fg.texture.overrideState = true; fg.texture.value = tex;
            fg.intensity.overrideState = true; fg.intensity.value = s_grainIntensity;
            fg.response.overrideState = true; fg.response.value = 0.8f;
            EditorUtility.SetDirty(pr); AssetDatabase.SaveAssetIfDirty(pr);
            WriteProbe($"R589 view-scale grain on intensity={s_grainIntensity} "
                     + $"({UnityEngine.SceneManagement.SceneManager.GetActiveScene().name})\n");
        }

        [MenuItem("Tools/Fix/589b 感官颗粒强度档(0.2/0.35/0.5/0.7循环)")]
        public static void GrainIntensityCycle()
        {
            float next = s_grainIntensity switch
            { 0.20f => 0.35f, 0.35f => 0.50f, 0.50f => 0.70f, _ => 0.20f };
            s_grainIntensity = next;
            var vol = Object.FindObjectOfType<Volume>();
            if (vol != null && vol.sharedProfile != null &&
                vol.sharedProfile.TryGet<FilmGrain>(out var fg) && fg.intensity.value > 0f)
            {
                fg.intensity.value = next;
                EditorUtility.SetDirty(vol.sharedProfile); AssetDatabase.SaveAssetIfDirty(vol.sharedProfile);
            }
            WriteProbe($"R589b grain intensity -> {next}\n");
        }

        // 全场景应用：每个场景的 Volume profile 是独立资产，逐场景开门(需退 Play)。
        // lookup 资产由 589 生成过一次即可，这里只做赋值+定档 0.5。
        [MenuItem("Tools/Fix/589c 观看颗粒=全场景应用(intensity 0.5, 存 profile)")]
        public static void GrainAllScenes()
        {
            if (Application.isPlaying) { WriteProbe("R589c REFUSED: Play 中不开场景\n"); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GrainTexPath);
            if (tex == null) { WriteProbe("R589c REFUSED: 先跑一次 589 生成 lookup\n"); return; }
            string[] scenes = { "Main", "OceanScene", "PondScene", "PoolScene", "IsletScene", "FFTScene" };
            var sb = new System.Text.StringBuilder("R589c grain sweep (intensity 0.5):\n");
            foreach (var s in scenes)
            {
                string path = "Assets/Scenes/" + s + ".unity";
                if (!System.IO.File.Exists(path)) continue;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                var vol = Object.FindObjectOfType<Volume>();
                if (vol == null || vol.sharedProfile == null)
                { sb.Append("  " + s + ": 无 Volume/profile，跳过\n"); continue; }
                var pr = vol.sharedProfile;
                if (!pr.TryGet<FilmGrain>(out var fg))
                { fg = pr.Add<FilmGrain>(true); AssetDatabase.AddObjectToAsset(fg, pr); }
                fg.type.overrideState = true; fg.type.value = FilmGrainLookup.Custom;
                fg.texture.overrideState = true; fg.texture.value = tex;
                fg.intensity.overrideState = true; fg.intensity.value = 0.5f;
                fg.response.overrideState = true; fg.response.value = 0.8f;
                EditorUtility.SetDirty(pr); AssetDatabase.SaveAssetIfDirty(pr);
                sb.Append("  " + s + ": grain on (profile saved)\n");
            }
            WriteProbe(sb.ToString());
        }

        [MenuItem("Tools/Fix/590 感官颗粒=关")]
        public static void GrainOff()
        {
            var vol = Object.FindObjectOfType<Volume>();
            if (vol == null || vol.sharedProfile == null) { WriteProbe("R590 无 Volume\n"); return; }
            if (vol.sharedProfile.TryGet<FilmGrain>(out var fg))
            {
                fg.intensity.value = 0f;
                EditorUtility.SetDirty(vol.sharedProfile); AssetDatabase.SaveAssetIfDirty(vol.sharedProfile);
            }
            WriteProbe("R590 grain off\n");
        }

        static int Set(string path, string prop, float v)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || !m.HasProperty(prop))
            {
                WriteProbe($"R591 SKIP {path}::{prop}（不存在）\n");
                return 0;
            }
            m.SetFloat(prop, v);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return 1;
        }

        static int Set(string path, string prop, Color v)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || !m.HasProperty(prop))
            {
                WriteProbe($"R591 SKIP {path}::{prop}（不存在）\n");
                return 0;
            }
            m.SetColor(prop, v);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return 1;
        }
    }
}
#endif
