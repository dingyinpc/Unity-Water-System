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


        // R123：树干树皮（AI）。原贴图是 512² 近白（均值 228），树干颜色全靠 _BaseColor 撑；
        //   AI 树皮已归一化到相近亮度（均值 197），所以换上去色调不会跳。
        [MenuItem("Tools/R123/90 树干贴 AI 树皮")]
        public static void BarkAI()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ProcDir}/Bark_Palm_AI_norm.png");
            if (tex == null) { P("R123/90 REFUSED: 没有 Bark_Palm_AI_norm.png\n"); return; }

            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_PalmBark.mat");
            if (m == null) { P("R123/90 REFUSED: 找不到 Islet_PalmBark\n"); return; }

            var prev = m.GetTexture("_BaseMap");
            EditorPrefs.SetString("R123.barkPrev", prev != null ? AssetDatabase.GetAssetPath(prev) : "");
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", new Vector2(1f, 3f));   // 树干纵向拉长（模型 UV 是 0..3）
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R123/90 Islet_PalmBark._BaseMap = {tex.name}（原 {prev?.name}）平铺 (1,3)\n");
        }

        [MenuItem("Tools/R123/90b 树皮还原")]
        public static void BarkBack()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_PalmBark.mat");
            if (m == null) return;
            string path = EditorPrefs.GetString("R123.barkPrev", "");
            var tex = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { P("R123/90b 没有记录原贴图\n"); return; }
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", Vector2.one);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R123/90b Islet_PalmBark._BaseMap 还原为 {tex.name}\n");
        }

        [MenuItem("Tools/R123/91 树皮平铺=(1,1)")]
        public static void BarkTile1()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_PalmBark.mat");
            if (m == null) return;
            m.SetTextureScale("_BaseMap", Vector2.one);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P("R123/91 树皮平铺 -> (1,1)\n");
        }


        // R123/92：临时把相机放到"看得见树干"的位置（验证完用 R122/91 还原验收机位）
        [MenuItem("Tools/R123/92 机位=看树干(临时)")]
        public static void CamTrunk()
        {
            var cam = Camera.main;
            if (cam == null) { P("R123/92 REFUSED\n"); return; }

            // 找最近的一棵棕榈，把相机放到离它 4.5 米、抬头 8° 的位置
            GameObject best = null;
            float bestD = float.MaxValue;
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
            {
                if (!go.name.ToLower().Contains("palm")) continue;
                var r = go.GetComponent<Renderer>();
                if (r == null) continue;
                float d = Vector3.Distance(r.bounds.center, cam.transform.position);
                if (d < bestD) { bestD = d; best = go; }
            }
            if (best == null) { P("R123/92 REFUSED: 没找到棕榈\n"); return; }

            var b = best.GetComponent<Renderer>().bounds;
            Vector3 look = new Vector3(b.center.x, b.min.y + b.size.y * 0.25f, b.center.z);
            Vector3 eye = look + new Vector3(-3.2f, 1.2f, -3.0f);
            cam.transform.position = eye;
            cam.transform.LookAt(look);
            cam.fieldOfView = 45f;
            P($"R123/92 机位 -> 看 {best.name} 树干  eye=({eye.x:F1},{eye.y:F1},{eye.z:F1}) look=({look.x:F1},{look.y:F1},{look.z:F1})\n");
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
