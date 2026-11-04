// R118Setup.cs — R118：AI 贴图的"一条龙"自动化
//
// 目标：把图丢进 `Assets/Art/Textures/AI/Raw/` 之后，一条菜单跑完：
//   ① 调 Python 后处理（无缝检测/修复、法线推导、白底抠图）→ `AI/Proc/`
//   ② 刷新并**批量设置导入参数**（法线图设成 NormalMap、关 sRGB、Repeat、各向异性）
//   ③ 把岩石贴图接到岩石材质（三平面投影，礁石没有 UV 也能用）
//
// 可逆：90 处理+导入（不改场景） / 91 接岩石贴图 / 91b 还原 / 99 探针
#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R118Setup
    {
        const string RawDir  = "Assets/Art/Textures/AI/Raw";
        const string ProcDir = "Assets/Art/Textures/AI/Proc";

        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        [MenuItem("Tools/R118/90 AI贴图 处理+导入(一条龙)")]
        public static void ProcessAndImport()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var py = Path.Combine(root, "Tools/TexAI/texai.py");
            if (!File.Exists(py)) { P("R118/90 REFUSED: 找不到 Tools/TexAI/texai.py\n"); return; }
            if (!Directory.Exists(Path.Combine(root, RawDir))) { P($"R118/90 REFUSED: 没有 {RawDir}\n"); return; }

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"-u \"{py}\" --in \"{RawDir}\" --out \"{ProcDir}\" --mode auto",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var proc = Process.Start(psi))
            {
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                P("R118/90 python 输出：\n" + stdout + (string.IsNullOrWhiteSpace(stderr) ? "" : "\n[stderr] " + stderr));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            // 批量设导入参数
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ProcDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    bool isNormal = path.EndsWith("_normal.png");
                    bool isRgba   = path.EndsWith("_rgba.png");

                    ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = !isNormal;
                    ti.wrapMode = isRgba ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    ti.alphaIsTransparency = isRgba;
                    ti.mipmapEnabled = true;
                    ti.anisoLevel = 8;
                    ti.filterMode = FilterMode.Trilinear;
                    ti.maxTextureSize = 2048;
                    ti.SaveAndReimport();
                    n++;
                }
            }
            P($"R118/90 导入参数已设：{n} 张（normal→NormalMap/关sRGB，rgba→Clamp+alphaIsTransparency，其余 Repeat+aniso8）\n");
        }

        static Material RockMat()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet_RockDetail.mat");
            if (m != null) return m;
            return AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_RockDetail.mat");
        }

        [MenuItem("Tools/R118/91 岩石贴 AI 岩贴图(三平面, 强度0.7)")]
        public static void ApplyRock()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ProcDir}/Rock_Basalt_AI_albedo.png");
            if (tex == null) { P("R118/91 REFUSED: 没有 Rock_Basalt_AI_albedo.png（先跑 90）\n"); return; }

            int n = 0;
            foreach (var path in new[]
                     {
                         "Assets/Art/Materials/Islet_RockDetail.mat",
                         "Assets/Art/Materials/Ocean_RockDetail.mat",
                         "Assets/Art/Materials/Ocean_CliffDetail.mat",
                     })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                if (!m.HasProperty("_RockTex")) { P($"R118/91 跳过 {m.name}（shader 没有 _RockTex）\n"); continue; }
                m.SetTexture("_RockTex", tex);
                m.SetFloat("_RockTexScale", 1.2f);
                m.SetFloat("_RockTexAmount", 0.7f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"R118/91 岩贴图已接到 {n} 个材质（密度 1.2，强度 0.7）\n");
        }

        [MenuItem("Tools/R118/91b 岩石贴图还原(强度0)")]
        public static void RevertRock()
        {
            int n = 0;
            foreach (var path in new[]
                     {
                         "Assets/Art/Materials/Islet_RockDetail.mat",
                         "Assets/Art/Materials/Ocean_RockDetail.mat",
                         "Assets/Art/Materials/Ocean_CliffDetail.mat",
                     })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !m.HasProperty("_RockTexAmount")) continue;
                m.SetFloat("_RockTexAmount", 0f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"R118/91b 岩贴图强度 -> 0（{n} 个材质）\n");
        }


        // R119：大物体（崖体/远景山）贴 AI 岩贴图。
        //   小礁石上贴图看不出是**频率失配**（R118 实测），大物体分辨率才够。
        [MenuItem("Tools/R119/90 崖体贴 AI 岩贴图(250m/周期)")]
        public static void CliffTex()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ProcDir}/Rock_Basalt_AI_albedo.png");
            if (tex == null) { P("R119/90 REFUSED: 没有 Rock_Basalt_AI_albedo.png（先跑 R118/90）\n"); return; }

            int n = 0;
            foreach (var path in new[]
                     {
                         "Assets/Art/Materials/Ocean_FarLand.mat",
                         "Assets/Art/Materials/Ocean_Cliff_Far.mat",
                     })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { P($"R119/90 跳过（不存在）{path}\n"); continue; }
                if (!m.HasProperty("_RockTex")) { P($"R119/90 跳过 {m.name}（shader 没有 _RockTex）\n"); continue; }
                m.SetTexture("_RockTex", tex);
                m.SetFloat("_RockTexScale", 0.004f);
                m.SetFloat("_RockTexAmount", 0.85f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
                P($"R119/90 {m.name} 贴图密度 0.004（250m/周期）强度 0.85\n");
            }
            P($"R119/90 共 {n} 个材质\n");
        }

        [MenuItem("Tools/R119/90b 崖体贴图还原")]
        public static void CliffTexBack()
        {
            int n = 0;
            foreach (var path in new[]
                     {
                         "Assets/Art/Materials/Ocean_FarLand.mat",
                         "Assets/Art/Materials/Ocean_Cliff_Far.mat",
                     })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !m.HasProperty("_RockTexAmount")) continue;
                m.SetFloat("_RockTexAmount", 0f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"R119/90b 崖体贴图强度 -> 0（{n} 个材质）\n");
        }

        [MenuItem("Tools/R119/91 崖体贴图密度=0.010(100m/周期)")]
        public static void CliffTexDense()
        {
            int n = 0;
            foreach (var path in new[] { "Assets/Art/Materials/Ocean_FarLand.mat", "Assets/Art/Materials/Ocean_Cliff_Far.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !m.HasProperty("_RockTexScale")) continue;
                m.SetFloat("_RockTexScale", 0.010f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"R119/91 崖体贴图密度 -> 0.010（{n} 个材质）\n");
        }


        // R120：棕榈叶贴 AI 叶片（背景已填成叶片平均色的版本，**不用它的轮廓**）
        //   模型是真实棕榈（SM_Palm_Coconut_01，77k 顶点），叶片按网格 UV 采样 _BaseMap；
        //   AI 叶片自带 alpha 轮廓，直接换会把叶片"切碎"，所以先用扁平化版本。
        [MenuItem("Tools/R120/91 棕榈叶贴 AI 叶片(扁平版)")]
        public static void PalmLeafAI()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ProcDir}/Palm_Frond_AI_flat.png");
            if (tex == null) { P("R120/91 REFUSED: 没有 Palm_Frond_AI_flat.png\n"); return; }

            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_PalmLeaf.mat");
            if (m == null) { P("R120/91 REFUSED: 找不到 Islet_PalmLeaf\n"); return; }

            var prev = m.GetTexture("_BaseMap");
            EditorPrefs.SetString("R120.palmLeafPrev", prev != null ? AssetDatabase.GetAssetPath(prev) : "");
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R120/91 Islet_PalmLeaf._BaseMap = {tex.name}（原 {prev?.name}）\n");
        }

        [MenuItem("Tools/R120/91b 棕榈叶贴图还原")]
        public static void PalmLeafBack()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_PalmLeaf.mat");
            if (m == null) return;
            string path = EditorPrefs.GetString("R120.palmLeafPrev", "");
            if (string.IsNullOrEmpty(path)) { P("R120/91b 没有记录原贴图\n"); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { P($"R120/91b 找不到 {path}\n"); return; }
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R120/91b Islet_PalmLeaf._BaseMap 还原为 {tex.name}\n");
        }

        [MenuItem("Tools/R120/99 探针(叶片贴图)")]
        public static void PalmProbe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_PalmLeaf.mat");
            if (m == null) { P("R120/99 无 Islet_PalmLeaf\n"); return; }
            var t = m.GetTexture("_BaseMap");
            P($"R120 PROBE PalmLeaf baseMap={(t == null ? "null" : t.name)} " +
              $"detail={m.GetFloat("_DetailAmount"):F2} frondVar={m.GetFloat("_FrondVar"):F2}\n");
        }

        [MenuItem("Tools/R118/99 探针")]
        public static void Probe()
        {
            P("R118 PROBE 处理产物：\n");
            string procAbs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ProcDir));
            if (Directory.Exists(procAbs))
                foreach (var f in Directory.GetFiles(procAbs, "*.png"))
                    P($"   {Path.GetFileName(f)}  {new FileInfo(f).Length / 1024}KB\n");
            var m = RockMat();
            if (m != null)
                P($"R118 PROBE 岩石材质={m.name} tex={(m.GetTexture("_RockTex") ? m.GetTexture("_RockTex").name : "null")} " +
                  $"amount={(m.HasProperty("_RockTexAmount") ? m.GetFloat("_RockTexAmount") : -1):F2}\n");
        }
    }
}
#endif
