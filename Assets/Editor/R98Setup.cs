// R98Setup.cs — R98：给 Ocean 的礁石/岬角换上"有细节的程序化岩"材质
//
// 背景：Ocean 的礁石用的是 **URP/Lit + 无贴图 + 无 UV** 的低模，
//   于是每一面都是一块纯色平板 —— 提亮基色变"白纸片"，压暗变"黑纸片"，
//   在两种状态之间反复横跳（R81 白 → R95 深灰），说明问题不在基色，在**没有表面细节**。
//
// 解法：复用 Islet 那套 `WaterProject/IsletRockSplat` —— 它**不需要 UV**：
//   层理条带/风化斑/微颗粒全部用世界坐标噪声算，另外带一层**天光填充**（背光面不会死黑）。
//
// 全部可逆：90/90b 礁石，91/91b 岬角。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R98Setup
    {
        const string ShaderName = "WaterProject/IsletRockSplat";

        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static Material BuildMat(string fileName, float hMin, float hMax, float mid, float fill)
        {
            var sh = Shader.Find(ShaderName);
            if (sh == null) { P($"R98 REFUSED: 找不到 shader {ShaderName}\n"); return null; }

            string path = $"Assets/Art/Materials/{fileName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing.shader == sh)
            {
                existing.SetFloat("_HMin", hMin);
                existing.SetFloat("_HMax", hMax);
                existing.SetFloat("_MidPoint", mid);
                existing.SetFloat("_FillLight", fill);
                existing.SetColor("_RockLo",    new Color(0.140f, 0.141f, 0.148f, 1f));
                existing.SetColor("_RockMid",   new Color(0.220f, 0.221f, 0.230f, 1f));
                existing.SetColor("_RockHi",    new Color(0.300f, 0.301f, 0.310f, 1f));
                existing.SetColor("_MossColor", new Color(0.185f, 0.186f, 0.192f, 1f));
                existing.SetFloat("_MicroAmount", 1.0f);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                P($"R98 更新材质 {path}（H {hMin}..{hMax}, 填充 {fill}）\n");
                return existing;
            }

            var m = new Material(sh) { name = fileName };
            // 灰色岩：苔色取跟岩石同色 ⇒ 苔权重再大也看不出来
            m.SetColor("_RockLo",    new Color(0.140f, 0.141f, 0.148f, 1f));
            m.SetColor("_RockMid",   new Color(0.220f, 0.221f, 0.230f, 1f));
            m.SetColor("_RockHi",    new Color(0.300f, 0.301f, 0.310f, 1f));
            m.SetColor("_MossColor", new Color(0.185f, 0.186f, 0.192f, 1f));
            m.SetFloat("_HMin", hMin);
            m.SetFloat("_HMax", hMax);
            m.SetFloat("_MidPoint", mid);
            m.SetFloat("_FillLight", fill);      // 背光面靠天光填充，不再死黑
            m.SetFloat("_AmbStrength", 1.0f);
            m.SetFloat("_MicroAmount", 1.0f);    // 0.11m / 0.045m 两级麻点

            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R98 建材质 {path}（H {hMin}..{hMax}, 填充 {fill}）\n");
            return m;
        }

        static int SwapTo(string fromMatName, Material to)
        {
            int changed = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name != fromMatName) continue;
                    mats[i] = to;
                    dirty = true;
                }
                if (dirty) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); changed++; }
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return changed;
        }

        static int SwapBack(Material from, string toMatName)
        {
            var to = LoadMat(toMatName);
            if (to == null) { P($"R98 REFUSED: 找不到 {toMatName}\n"); return 0; }
            int changed = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != from) continue;
                    mats[i] = to;
                    dirty = true;
                }
                if (dirty) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); changed++; }
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return changed;
        }

        static Material LoadMat(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Material"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return null;
        }

        [MenuItem("Tools/R98/90 礁石→程序化岩(可逆)")]
        public static void Rocks()
        {
            var m = BuildMat("Ocean_RockDetail", -1.5f, 6.0f, 0.45f, 0.75f);
            if (m != null) P($"R98/90 礁石 改了 {SwapTo("Ocean_Rock", m)} 个渲染器\n");
        }

        [MenuItem("Tools/R98/90b 礁石还原")]
        public static void RocksBack()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_RockDetail.mat");
            if (m == null) { P("R98/90b 没有 Ocean_RockDetail\n"); return; }
            P($"R98/90b 礁石还原 改了 {SwapBack(m, "Ocean_Rock")} 个渲染器\n");
        }

        [MenuItem("Tools/R98/91 岬角→程序化岩(可逆)")]
        public static void Cliff()
        {
            var m = BuildMat("Ocean_CliffDetail", -6f, 60f, 0.35f, 0.55f);
            if (m != null) P($"R98/91 岬角 改了 {SwapTo("Ocean_Cliff", m)} 个渲染器\n");
        }

        [MenuItem("Tools/R98/91b 岬角还原")]
        public static void CliffBack()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_CliffDetail.mat");
            if (m == null) { P("R98/91b 没有 Ocean_CliffDetail\n"); return; }
            P($"R98/91b 岬角还原 改了 {SwapBack(m, "Ocean_Cliff")} 个渲染器\n");
        }

        [MenuItem("Tools/R98/99 探针(岩石/岬角材质)")]
        public static void Probe()
        {
            var seen = new Dictionary<string, int>();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    string key = $"{m.name}<{m.shader.name}>";
                    seen.TryGetValue(key, out int c);
                    seen[key] = c + 1;
                }
            P($"R98 PROBE 场景={EditorSceneManager.GetActiveScene().name}\n");
            foreach (var kv in seen)
                if (kv.Key.Contains("Rock") || kv.Key.Contains("Cliff"))
                    P($"   {kv.Key}  ×{kv.Value}\n");
        }
    }
}
#endif
