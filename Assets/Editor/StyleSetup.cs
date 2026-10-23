// StyleSetup.cs — 风格对标调色档（R08 用）：Tools/Style/190~192
//
// 为什么需要它：把 5 个场景和参考视频逐帧量色后，硬指标是
//   饱和度：我们 122~131 vs 参考 46~83（高 50%~130%）
//   对比度：我们 28~61  vs 参考 78~93（低 40%~70%）
//   亮度  ：我们 115~184 vs 参考 96~119（偏亮）
// 这三条是"塑料感/卡通感"的主要来源，且**不需要重做资产**就能大幅改善。
//
// 纪律：只改当前场景的 Volume profile（作用域内唯一写入点），不碰材质与灯光；
//   参数一次写成整档，A/B 只需 190/191 两条菜单。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class StyleSetup
    {
        static void Probe(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static VolumeProfile Profile()
        {
            var vols = Object.FindObjectsOfType<Volume>();
            foreach (var v in vols) if (v.sharedProfile != null) return v.sharedProfile;
            return null;
        }

        // ═════════ 200/201 电影遮幅 2.35:1 ═════════
        [MenuItem("Tools/Style/200 电影遮幅 2.35:1 (开)")]
        public static void BarsOn()
        {
            var go = GameObject.Find("CinematicBars");
            if (go == null) go = new GameObject("CinematicBars");
            var c = go.GetComponent<CinematicLetterbox>();
            if (c == null) c = go.AddComponent<CinematicLetterbox>();
            c.enabled = true;
            c.targetAspect = 2.35f;
            EditorUtility.SetDirty(c);
            Probe($"MENU200 电影遮幅开: {c.targetAspect:F2}:1（Screen {Screen.width}x{Screen.height}）\n");
        }

        [MenuItem("Tools/Style/201 电影遮幅 (关)")]
        public static void BarsOff()
        {
            var c = Object.FindObjectOfType<CinematicLetterbox>(true);
            if (c == null) { Probe("MENU201: 场景里没有 CinematicLetterbox\n"); return; }
            c.enabled = false;
            EditorUtility.SetDirty(c);
            Probe("MENU201 电影遮幅关\n");
        }

        // ═════════ 202/203 叶面高频细节（G6 资产替代）═════════
        // 低多边形叶片的图集是平滑渐变，长焦拉近就是"一片纸"。这条给所有用 Tree_Leaf 的材质
        // 打开 _DetailAmount（反照率高频噪声 + 裁剪边缘毛刺）。默认 0 ⇒ 旧行为不变。
        static int SetLeafDetail(float v, string tag)
        {
            var seen = new System.Collections.Generic.HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var m = r.sharedMaterial;
                if (m == null || !m.HasProperty("_DetailAmount") || !seen.Add(m)) continue;
                m.SetFloat("_DetailAmount", v);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);   // 只存这一个，不用 SaveAssets()
                n++;
                Probe($"      {tag}: 材质 {m.name}  shader={m.shader.name}\n");
            }
            Probe($"MENU{tag} 叶面细节 -> {v:F2}（改了 {n} 个材质）\n");
            return n;
        }
        [MenuItem("Tools/Style/202 叶面细节=1(开)")]  public static void LeafOn()  => SetLeafDetail(1f, "202");
        [MenuItem("Tools/Style/203 叶面细节=0(关)")]  public static void LeafOff() => SetLeafDetail(0f, "203");

        // ═════════ 198/199 构图：逆光剪影（R08 S2）═════════
        // 依据 R08 §7.3：参考画面近黑像素占 36%，我们只有 6% —— 缺的不是光照强度，是
        // "画幅里有没有大面积背光物体"。把太阳放到**画面前方**，岛与棕榈就会自然变成剪影。
        // 方位换算：方向光的**光源方位 = Euler.y + 180°**；相机朝 +Z（yaw 0）⇒ 太阳在画面前方要 yaw = 180°。
        [MenuItem("Tools/Style/198 构图=参考(逆光剪影)")]
        public static void ApplyBacklight()
        {
            string key = "WaterProject.Backlight." + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var sun = FirstDirectional();
            if (sun == null) { Probe("MENU198 REFUSED: 没有方向光\n"); return; }

            if (!EditorPrefs.HasKey(key + ".saved"))
            {
                var e = sun.transform.eulerAngles;
                EditorPrefs.SetFloat(key + ".x", e.x);
                EditorPrefs.SetFloat(key + ".y", e.y);
                EditorPrefs.SetFloat(key + ".i", sun.intensity);
                EditorPrefs.SetBool(key + ".saved", true);
            }

            sun.transform.rotation = Quaternion.Euler(24f, 180f, 0f);   // 光源在 +Z（画面前方）
            sun.intensity = EditorPrefs.GetFloat(key + ".i", sun.intensity) * 1.10f;

            // ③ 收紧构图：参考是"主体占满画面"，我们是"广角明信片"（岛只占 ~15%）。
            //   长焦 + 略俯角 ⇒ 岛/棕榈/礁石在画幅里变大，暗部占比才可能上去。
            var cam = Camera.main;
            if (cam != null)
            {
                if (!EditorPrefs.HasKey(key + ".fov"))
                {
                    EditorPrefs.SetFloat(key + ".fov", cam.fieldOfView);
                    var ce = cam.transform.eulerAngles;
                    EditorPrefs.SetFloat(key + ".cx", ce.x);
                    EditorPrefs.SetFloat(key + ".cy", ce.y);
                    EditorPrefs.SetFloat(key + ".amb", RenderSettings.ambientIntensity);
                }
                cam.fieldOfView = 26f;
                cam.transform.rotation = Quaternion.Euler(-6f, EditorPrefs.GetFloat(key + ".cy", 0f), 0f);
                // 背光构图下天光还太强：植被会被"补"成亮黄绿，剪影立不住。
                // ⚠ ambientIntensity 只在 Skybox 模式有效（见 AGENTS 第 10 条）
                RenderSettings.ambientIntensity = 0.11f;
            }

            Probe($"MENU198 逆光+收紧: 太阳 rot={sun.transform.eulerAngles} 强度{sun.intensity:F2} "
                + $"| 相机 FOV={(cam != null ? cam.fieldOfView.ToString("F0") : "-")} "
                + $"（原 FOV {EditorPrefs.GetFloat(key + ".fov"):F0}）\n");
        }

        [MenuItem("Tools/Style/199 构图=还原(读回原太阳)")]
        public static void RestoreBacklight()
        {
            string key = "WaterProject.Backlight." + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!EditorPrefs.HasKey(key + ".saved")) { Probe("MENU199 REFUSED: 没有保存过原值\n"); return; }
            var sun = FirstDirectional();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(
                    EditorPrefs.GetFloat(key + ".x"), EditorPrefs.GetFloat(key + ".y"), 0f);
                sun.intensity = EditorPrefs.GetFloat(key + ".i");
            }
            var cam = Camera.main;
            if (cam != null && EditorPrefs.HasKey(key + ".fov"))
            {
                cam.fieldOfView = EditorPrefs.GetFloat(key + ".fov");
                cam.transform.rotation = Quaternion.Euler(EditorPrefs.GetFloat(key + ".cx"), EditorPrefs.GetFloat(key + ".cy"), 0f);
                RenderSettings.ambientIntensity = EditorPrefs.GetFloat(key + ".amb", RenderSettings.ambientIntensity);
            }
            EditorPrefs.DeleteKey(key + ".saved");
            Probe("MENU199 构图已还原\n");
        }

        // ═════════ 195/196 光比（S1：降天光、加主光、开阴影）═════════
        // 为什么这条最关键：调色只能压整体亮度，**拉不开明暗差**。
        // 参考画面对比度 std 78~93，我们 45 —— 差在"没有暗部"，而暗部来自光比。
        [MenuItem("Tools/Style/195 光比=参考(天光↓/主光↑/阴影开)")]
        public static void ApplyLightRatio()
        {
            string key = "WaterProject.LightRatio." + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // 首次执行先记下原值（供 196 还原；也写进探针便于复盘）
            if (!EditorPrefs.HasKey(key + ".saved"))
            {
                EditorPrefs.SetFloat(key + ".ambInt", RenderSettings.ambientIntensity);
                EditorPrefs.SetFloat(key + ".reflInt", RenderSettings.reflectionIntensity);
                EditorPrefs.SetInt(key + ".ambMode", (int)RenderSettings.ambientMode);
                var l0 = FirstDirectional();
                if (l0 != null) { EditorPrefs.SetFloat(key + ".sunInt", l0.intensity); EditorPrefs.SetInt(key + ".sunShadow", (int)l0.shadows); }
                EditorPrefs.SetBool(key + ".saved", true);
            }

            // ① 天光：**必须用 Skybox 模式**——Trilight 模式下 ambientIntensity 是无效的
            //   （只有 Skybox 才吃这个系数；上一版改 Trilight + 0.45，实测反而更亮）
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.07f;     // 原 0.22 → 0.07（只压天光，不动主光）
            RenderSettings.reflectionIntensity = 0.60f;

            // ② 主光：用**保存的原值**做基准（绝对档，重复执行不会越叠越亮）
            var sun = FirstDirectional();
            if (sun != null)
            {
                sun.intensity = EditorPrefs.GetFloat(key + ".sunInt", sun.intensity) * 1.0f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.90f;
            }

            Probe($"MENU195 光比档({UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}): "
                + $"环境Trilight 强度{RenderSettings.ambientIntensity:F2} 反射{RenderSettings.reflectionIntensity:F2} | "
                + $"主光{(sun != null ? sun.intensity.ToString("F2") : "null")} 阴影{(sun != null ? sun.shadows.ToString() : "-")}\n");
        }

        [MenuItem("Tools/Style/196 光比=还原(读回首次执行前)")]
        public static void RestoreLightRatio()
        {
            string key = "WaterProject.LightRatio." + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!EditorPrefs.HasKey(key + ".saved")) { Probe("MENU196 REFUSED: 没有保存过原值\n"); return; }
            RenderSettings.ambientMode = (AmbientMode)EditorPrefs.GetInt(key + ".ambMode");
            RenderSettings.ambientIntensity = EditorPrefs.GetFloat(key + ".ambInt");
            RenderSettings.reflectionIntensity = EditorPrefs.GetFloat(key + ".reflInt");
            var sun = FirstDirectional();
            if (sun != null)
            {
                sun.intensity = EditorPrefs.GetFloat(key + ".sunInt");
                sun.shadows = (LightShadows)EditorPrefs.GetInt(key + ".sunShadow");
            }
            EditorPrefs.DeleteKey(key + ".saved");
            Probe($"MENU196 光比已还原（{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}）\n");
        }

        [MenuItem("Tools/Style/197 回读光照(写探针)")]
        public static void DumpLighting()
        {
            var sun = FirstDirectional();
            Probe($"MENU197 光照: ambientMode={RenderSettings.ambientMode} 强度{RenderSettings.ambientIntensity:F2} "
                + $"天空色{RenderSettings.ambientSkyColor} 反射{RenderSettings.reflectionIntensity:F2} | "
                + $"主光{(sun != null ? $"{sun.intensity:F2} 阴影{sun.shadows} 强度{sun.shadowStrength:F2}" : "null")}\n");
        }

        static Light FirstDirectional()
        {
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) return l;
            return null;
        }

        // ═════════ 190 参考风格调色档 ═════════
        [MenuItem("Tools/Style/190 调色档=参考风格(ACES+去饱和+加对比+暗角)")]
        public static void ApplyReferenceLook()
        {
            var p = Profile();
            if (p == null) { Probe("MENU190 REFUSED: 当前场景没有 Volume/profile\n"); return; }

            // ① 色调映射：ACES（参考的高光滚降和"电影感"主要来自它）
            var tm = Get<Tonemapping>(p); tm.mode.Override(TonemappingMode.ACES);

            // ② 曝光：整体压暗（我们 115~184，参考 96~119）
            var ca = Get<ColorAdjustments>(p);
            ca.postExposure.Override(-0.18f);
            ca.contrast.Override(12f);        // 拉对比：28~61 → 目标 78
            ca.saturation.Override(-22f);     // 去饱和：122~131 → 目标 46~85

            // ③ 白平衡：高光偏暖、整体略暖（参考是热带日照）
            var wb = Get<WhiteBalance>(p);
            wb.temperature.Override(8f);
            wb.tint.Override(-3f);

            // ④ 分离色调（teal-orange）：暗部偏青、高光偏暖 —— 参考最典型的"电影色"
            var smh = Get<ShadowsMidtonesHighlights>(p);
            smh.shadows.Override(new Vector4(0.88f, 0.94f, 1.08f, 0f));   // 暗部略深、偏青
            smh.midtones.Override(new Vector4(1.01f, 1.00f, 0.99f, 0f));
            smh.highlights.Override(new Vector4(1.06f, 1.02f, 0.96f, 0f)); // 高光略暖

            // ⑤ 泛光：参考的太阳/浪花有明显辉光
            var bl = Get<Bloom>(p);
            bl.threshold.Override(1.00f);
            bl.intensity.Override(0.42f);
            bl.scatter.Override(0.70f);

            // ⑥ 暗角：把注意力收到中心（参考几乎所有镜头都有轻微暗角）
            var vg = Get<Vignette>(p);
            vg.intensity.Override(0.30f);
            vg.smoothness.Override(0.45f);

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssetIfDirty(p);
            Probe($"MENU190 参考风格已写入 {p.name}: ACES / 曝光-0.30 / 对比+16 / 饱和-30 / 暖+8 / 暗角0.30 / Bloom0.55\n");
        }

        // ═════════ 191 关闭（A/B 用）═════════
        [MenuItem("Tools/Style/191 调色档=关闭(所有 override 置 0)")]
        public static void DisableLook()
        {
            var p = Profile();
            if (p == null) { Probe("MENU191 REFUSED: 当前场景没有 Volume/profile\n"); return; }
            if (p.TryGet(out Tonemapping tm)) tm.mode.Override(TonemappingMode.None);
            if (p.TryGet(out ColorAdjustments ca)) { ca.postExposure.Override(0f); ca.contrast.Override(0f); ca.saturation.Override(0f); }
            if (p.TryGet(out WhiteBalance wb)) { wb.temperature.Override(0f); wb.tint.Override(0f); }
            if (p.TryGet(out Bloom bl)) bl.intensity.Override(0f);
            if (p.TryGet(out Vignette vg)) vg.intensity.Override(0f);
            if (p.TryGet(out ShadowsMidtonesHighlights smh))
            {
                smh.shadows.Override(new Vector4(1f, 1f, 1f, 0f));
                smh.highlights.Override(new Vector4(1f, 1f, 1f, 0f));
            }
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssetIfDirty(p);
            Probe($"MENU191 调色档已关闭 {p.name}\n");
        }

        // ═════════ 194 修复：给 URP Renderer 挂上 PostProcessData ═════════
        // 本机实测：WaterURPRenderer.asset 的 postProcessData 是 {fileID: 0} ⇒
        // URP 直接跳过整个后处理栈。表现是"Volume 里怎么调都没反应、截图逐位相同"。
        [MenuItem("Tools/Style/194 修复后处理(Renderer 挂 PostProcessData)")]
        public static void FixPostData()
        {
            const string rendPath = "Assets/Settings/URP/WaterURPRenderer.asset";
            var rend = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendPath);
            if (rend == null) { Probe($"MENU194 REFUSED: 找不到 {rendPath}\n"); return; }
            var data = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (data == null) { Probe("MENU194 REFUSED: 找不到 URP 自带的 PostProcessData.asset\n"); return; }

            var so = new SerializedObject(rend);
            var prop = so.FindProperty("postProcessData");
            if (prop == null) { Probe("MENU194 REFUSED: 序列化字段 postProcessData 不存在（URP 版本差异）\n"); return; }
            prop.objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rend);
            AssetDatabase.SaveAssetIfDirty(rend);
            Probe($"MENU194 ok: {rendPath} 的 postProcessData -> {data.name}\n");
        }

        // ═════════ 193 诊断：Volume 与相机后处理是否真的在生效链路上 ═════════
        [MenuItem("Tools/Style/193 诊断(Volume/相机后处理/URP资源)")]
        public static void Diagnose()
        {
            string s = "MENU193 诊断:\n";
            var vols = Object.FindObjectsOfType<Volume>(true);
            s += $"  Volume 数量={vols.Length}\n";
            foreach (var v in vols)
            {
                s += $"   - GO='{v.gameObject.name}' active={v.gameObject.activeInHierarchy} enabled={v.enabled} "
                   + $"isGlobal={v.isGlobal} weight={v.weight} priority={v.priority} profile={(v.sharedProfile ? v.sharedProfile.name : "null")}\n";
            }
            var cam = Camera.main;
            if (cam == null) s += "  ⚠ 没有 MainCamera 标签的相机\n";
            else
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                string pp = data != null ? data.renderPostProcessing.ToString() : "无 AdditionalCameraData";
                string aa = data != null ? data.antialiasing.ToString() : "-";
                s += $"  相机 '{cam.name}' postProcessing={pp} hdr={cam.allowHDR} msaa={aa}\n";
                if (data != null)
                {
                    s += $"  相机 volumeLayerMask={data.volumeLayerMask.value} volumeTrigger={(data.volumeTrigger ? data.volumeTrigger.name : "null")}\n";
                    var rend = data.scriptableRenderer;
                    string rn = rend != null ? rend.GetType().Name : "null";
                    s += $"  渲染器={rn}\n";
                }
            }
            foreach (var v in vols)
                s += $"  Volume 所在 Layer={LayerMask.LayerToName(v.gameObject.layer)}({v.gameObject.layer})\n";
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            s += $"  URP资源={(rp ? rp.name : "null")}\n";
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            s += $"  当前场景={active.name}\n";
            Probe(s);
        }

        // ═════════ 192 回读（证明真的写进去了）═════════
        [MenuItem("Tools/Style/192 回读调色参数(写探针)")]
        public static void DumpLook()
        {
            var p = Profile();
            if (p == null) { Probe("MENU192: 无 profile\n"); return; }
            string s = $"MENU192 {p.name}: ";
            if (p.TryGet(out Tonemapping tm)) s += $"tone={tm.mode.value} ";
            if (p.TryGet(out ColorAdjustments ca)) s += $"exp={ca.postExposure.value:F2} con={ca.contrast.value:F1} sat={ca.saturation.value:F1} ";
            if (p.TryGet(out WhiteBalance wb)) s += $"temp={wb.temperature.value:F0} ";
            if (p.TryGet(out Bloom bl)) s += $"bloom={bl.intensity.value:F2} ";
            if (p.TryGet(out Vignette vg)) s += $"vig={vg.intensity.value:F2}";
            Probe(s + "\n");
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet<T>(out var c)) c = p.Add<T>(true);
            foreach (var f in typeof(T).GetFields())
                if (f.GetValue(c) is VolumeParameter vp) vp.overrideState = true;
            return c;
        }
    }
}
#endif
