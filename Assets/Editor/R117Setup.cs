// R117Setup.cs — R117：把 AI 生成的沙贴图接到 Ocean 的沙滩上（端到端验证）
//
// 判定标准：贴图**本身无缝**（已在 Python 里量过：接缝比 1.13 / 0.75），
//   接进来以后只需要确认：①画面不变脏 ②六场景无回归 ③观感是否更像真沙。
// 全部可逆：90 装 / 90b 拆。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R117Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static Material SandMat()
        {
            foreach (var guid in AssetDatabase.FindAssets("Ocean_Sand t:Material"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == "Ocean_Sand")
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return null;
        }

        [MenuItem("Tools/R117/90 沙滩贴 AI 沙贴图(强度0.75)")]
        public static void Apply()
        {
            var m = SandMat();
            if (m == null) { P("R117/90 REFUSED: 找不到 Ocean_Sand\n"); return; }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/AI/Proc/Sand_Fine_AI_albedo.png");
            if (tex == null) tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/AI/Raw/Sand_Fine_AI.jpg");
            if (tex == null) { P("R117/90 REFUSED: 找不到 Sand_Fine_AI_albedo.png（先跑 R118/90）\n"); return; }

            m.SetTexture("_SandTex", tex);
            m.SetTexture("_SandNormal", null);
            m.SetFloat("_SandTexScale", 0.35f);
            m.SetFloat("_SandTexAmount", 0.75f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R117/90 Ocean_Sand: 贴图={tex.name} 密度 0.35 强度 0.75\n");
        }

        [MenuItem("Tools/R117/90b 沙滩贴图还原(强度0)")]
        public static void Revert()
        {
            var m = SandMat();
            if (m == null) { P("R117/90b REFUSED\n"); return; }
            m.SetFloat("_SandTexAmount", 0f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P("R117/90b Ocean_Sand: 贴图强度 -> 0（回到纯程序化）\n");
        }


        // R121：换成"细沙粒+贝壳碎屑"那一版（原图接缝 1.64/2.00，已被 texai.py 自动修到 1.04/1.16）
        [MenuItem("Tools/R121/90 沙滩换成细沙2(贝壳碎屑版)")]
        public static void ApplySand2()
        {
            var m = SandMat();
            if (m == null) { P("R121/90 REFUSED\n"); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/AI/Proc/Sand_Fine2_AI_albedo.png");
            if (tex == null) { P("R121/90 REFUSED: 没有 Sand_Fine2_AI_albedo.png（先跑 R118/90）\n"); return; }
            m.SetTexture("_SandTex", tex);
            m.SetFloat("_SandTexScale", 0.55f);
            m.SetFloat("_SandTexAmount", 0.75f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P("R121/90 Ocean_Sand -> Sand_Fine2_AI（密度 0.55 强度 0.75）\n");
        }

        [MenuItem("Tools/R121/91 沙滩换回细沙1")]
        public static void ApplySand1()
        {
            var m = SandMat();
            if (m == null) return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/AI/Proc/Sand_Fine_AI_albedo.png");
            if (tex == null) { P("R121/91 REFUSED\n"); return; }
            m.SetTexture("_SandTex", tex);
            m.SetFloat("_SandTexScale", 0.35f);
            m.SetFloat("_SandTexAmount", 0.75f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P("R121/91 Ocean_Sand -> Sand_Fine_AI（密度 0.35 强度 0.75）\n");
        }

        [MenuItem("Tools/R117/91 抗重复：贴图密度=0.18(更大)")]
        public static void Scale18()
        {
            var m = SandMat();
            if (m == null) return;
            m.SetFloat("_SandTexScale", 0.18f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P("R117/91 沙贴图密度 -> 0.18\n");
        }

        [MenuItem("Tools/R117/99 探针")]
        public static void Probe()
        {
            var m = SandMat();
            if (m == null) { P("R117/99 无 Ocean_Sand\n"); return; }
            var t = m.GetTexture("_SandTex");
            P($"R117 PROBE Ocean_Sand tex={(t == null ? "null" : t.name)} " +
              $"scale={m.GetFloat("_SandTexScale"):F2} amount={m.GetFloat("_SandTexAmount"):F2}\n");
        }
    }
}
#endif
