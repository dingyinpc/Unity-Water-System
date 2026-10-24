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

        [MenuItem("Tools/Fix/594 Pool 波速档(stepsPerFrame 2↔1, Play)")]
        public static void PoolStep()
        {
            if (!Application.isPlaying) { WriteProbe("R594 REFUSED: 仅 Play\n"); return; }
            var rip = Object.FindObjectOfType<WaterProject.Logic.PoolRipples>();
            if (rip == null) { WriteProbe("R594 REFUSED: 无 PoolRipples\n"); return; }
            rip.stepsPerFrame = rip.stepsPerFrame >= 2 ? 1 : 2;
            WriteProbe($"R594 stepsPerFrame -> {rip.stepsPerFrame}\n");
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
    }
}
#endif
