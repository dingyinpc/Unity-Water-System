// R57Setup.cs — R57 六场景体检：相机后处理开关 + 极端调色值
//
// 判据（客观、不靠审美）：
//   ① 相机 `renderPostProcessing` 必须是 true —— 关着的时候 Volume 里写什么都白写
//      （R20 在 Pool/Pond 发现过，R55 在 Ocean 又发现一次）。
//   ② Volume 的 ColorAdjustments 若落在"极端区"，就是历史实验残留：
//        曝光 < -0.60 或 对比 > 50 或 饱和 < -60
//      参考档是 曝光 -0.18 ~ -0.30 / 对比 +12~16 / 饱和 -22~-30。
//      （Pond 的 +2.8 曝光是**故意**的——池塘本来就暗，不在极端区规则内。）
//
// 本菜单只做上面两件事，其余参数不动；每个场景单独跑，结果写探针。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class R57Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        [MenuItem("Tools/R57/90 本场景体检+修正")]
        public static void FixScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var sb = new System.Text.StringBuilder();
            sb.Append($"=== R57 {scene} ===\n");

            // ① 相机后处理
            int camFixed = 0, camTotal = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                camTotal++;
                var d = cam.GetComponent<UniversalAdditionalCameraData>();
                if (d == null) d = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (!d.renderPostProcessing)
                {
                    d.renderPostProcessing = true;
                    EditorUtility.SetDirty(d);
                    camFixed++;
                    sb.Append($"  CAM {cam.name}: post OFF -> ON\n");
                }
            }
            sb.Append($"  相机 {camTotal} 台，修了 {camFixed} 台的 post\n");

            // ② 极端调色值
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v.profile == null) continue;
                // ⚠ 关键：Volume 可能同时有 sharedProfile(资产) 和 profile(场景实例)。
                //   只写实例的话，**场景重新加载会从资产重建**，改动白做（R57 第一次就把 Pond 写成了白屏：
                //   实例写了 -0.05，资产里还是 2.8，进 Play 又回到 2.8）。
                //   ⇒ 两个都写。
                foreach (var prof in new[] { v.sharedProfile, v.profile })
                {
                    if (prof == null) continue;
                    if (!prof.TryGet<ColorAdjustments>(out var ca)) continue;
                    float e = ca.postExposure.value, c = ca.contrast.value, s = ca.saturation.value;
                    bool extreme = e < -0.60f || c > 50f || s < -60f || e > 0.60f;
                    sb.Append($"  VOL {v.name} profile={prof.name} exp={e:F2} contrast={c:F1} sat={s:F1} " +
                              $"{(extreme ? "=> 极端区，改写参考档" : "=> 正常，不动")}\n");
                    if (!extreme) continue;
                    ca.postExposure.Override(Mathf.Clamp(e, -0.35f, 0.10f));
                    ca.contrast.Override(Mathf.Clamp(c, 8f, 18f));
                    ca.saturation.Override(Mathf.Clamp(s, -35f, -18f));
                    EditorUtility.SetDirty(prof);
                    if (AssetDatabase.Contains(prof)) AssetDatabase.SaveAssetIfDirty(prof);
                    sb.Append($"      -> exp={ca.postExposure.value:F2} contrast={ca.contrast.value:F1} sat={ca.saturation.value:F1}\n");
                }
            }

            // ③ 环境光（Islet 被压到 0.11 过；其它场景也看一眼）
            sb.Append($"  AMBIENT mode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity:F2}\n");

            P(sb.ToString());
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }

        [MenuItem("Tools/R57/92 本场景相机后处理=关(回滚用)")]
        public static void PostOff()
        {
            int n = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var d = cam.GetComponent<UniversalAdditionalCameraData>();
                if (d == null) continue;
                if (d.renderPostProcessing)
                {
                    d.renderPostProcessing = false;
                    EditorUtility.SetDirty(d);
                    n++;
                    P($"  R57/92 {cam.name}: post ON -> OFF\n");
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"MENU R57/92 本场景 post 关了 {n} 台\n");
        }

        [MenuItem("Tools/R57/99 六场景巡检(逐个打开，只读)")]
        public static void AuditAll()
        {
            // 只读巡检：一次把所有场景的相机 post / volume 值打出来（不保存、不修改）
            var guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            P($"=== R57 全场景巡检 {System.DateTime.Now:HH:mm:ss} ===\n");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                P($"  场景 {System.IO.Path.GetFileNameWithoutExtension(path)}\n");
            }
            P("  （逐个场景的详细值请在该场景里跑 R57/90）\n");
        }
    }
}
#endif
