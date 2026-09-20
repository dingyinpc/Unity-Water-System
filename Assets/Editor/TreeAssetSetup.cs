// TreeAssetSetup.cs — 按 E:\Blender\交付_树资产\Unity导入说明.md 落地的导入器（菜单 70~73）
//
// 文档里每一步的"实际值"都写进 Logs/water_probe.txt，因为 MCP 桥读不到 Console。
// 文档要求"有任何一步对不上就停下来报告实际值" —— 菜单73 就是干这个的。
//
// 2022.3 里几个 ModelImporter 字段在不同小版本间改名，凡是没把握的公共属性一律走
// SerializedObject + 空值检查，宁可报"没设上"也不要编译不过或静默设错。
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class TreeAssetSetup
    {
        const string Root      = "Assets/Trees";
        const string TexFolder = Root + "/Textures";
        const string MatFolder = Root + "/Materials";
        const string PrefFolder = Root + "/Prefabs";
        const string Probe = "Logs/water_probe.txt";

        static readonly string[] FbxPaths =
        {
            Root + "/FBX/SM_Tree_Broadleaf_01.fbx",
            Root + "/FBX/SM_Tree_Broadleaf_02.fbx",
            Root + "/FBX/SM_Tree_Broadleaf_03.fbx",
            Root + "/FBX/SM_Tree_Broadleaf_04.fbx",
            Root + "/FBX/SM_Tree_Broadleaf_05.fbx",
        };

        // 交付说明第 1 节的表格，菜单73 拿它逐项对账
        static readonly Vector2[] DocSpec =
        {
            new Vector2(9504f, 12.6f), new Vector2(14272f, 15.4f), new Vector2(11066f, 10.8f),
            new Vector2(5342f, 7.4f),  new Vector2(10454f, 9.4f),
        };

        static void W(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "..", Probe), s);
        }

        static bool SetBool(SerializedObject so, string field, bool v, string tag)
        {
            var p = so.FindProperty(field);
            if (p == null) { W("  .. " + tag + " 字段缺失: " + field + "\n"); return false; }
            p.boolValue = v;
            return true;
        }

        // ═════════════════════════ 70 ═════════════════════════
        [MenuItem("Tools/Trees/70 交付树资产：导入设置(FBX+贴图)")]
        public static void ApplyImportSettings()
        {
            if (UnityEngine.Application.isPlaying)
            { W("MENU70 REFUSED: 在 Play 模式，先退出\n"); return; }

            W("MENU70 begin\n");
            foreach (var path in FbxPaths) ConfigureFbx(path);
            ConfigureTextures();
            AssetDatabase.SaveAssets();
            W("MENU70 done\n");
        }

        static void ConfigureFbx(string path)
        {
            if (!System.IO.File.Exists(path)) { W("MENU70 NOFILE " + path + "\n"); return; }
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { W("MENU70 无 ModelImporter(没进资产库?) " + path + "\n"); return; }

            imp.globalScale = 1f;
            imp.useFileScale = false;             // Scale Factor = 1、Convert Units 关
            imp.importNormals = ModelImporterNormals.Import;
            imp.normalSmoothingAngle = 45f;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.isReadable = false;               // 文档：不需要运行时合并网格
            imp.importBlendShapes = false;
            imp.importVisibility = false;

            var so = new SerializedObject(imp);
            // ★ optimizeMeshData 开着会剥掉没人用的顶点通道，COLOR 一起没了 → 树永远不动
            SetBool(so, "optimizeMeshData", false, "optimizeMeshData");
            SetBool(so, "importVertexColors", true, "importVertexColors");
            so.ApplyModifiedProperties();

            AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var m = PickMesh(path);
            W("MENU70 " + System.IO.Path.GetFileName(path)
              + " verts " + (m != null ? m.vertexCount : -1)
              + " tris " + (m != null ? m.triangles.Length / 3 : -1)
              + " vcol " + (m != null && HasVertexColor(m) ? "有" : "无")
              + " size " + (m != null ? m.bounds.size.ToString("F2") : "-") + "\n");
        }

        static void ConfigureTextures()
        {
            ConfigureTex(TexFolder + "/T_TreeBark_BaseColor.png", TextureImporterType.Default, true, false);
            ConfigureTex(TexFolder + "/T_TreeBark_Roughness.png", TextureImporterType.Default, false, false);
            ConfigureTex(TexFolder + "/T_TreeBark_Normal.png", TextureImporterType.NormalMap, false, false);
            ConfigureTex(TexFolder + "/T_TreeLeaf_Atlas.png", TextureImporterType.Default, true, true);
        }

        static void ConfigureTex(string path, TextureImporterType type, bool srgb, bool needAlpha)
        {
            if (!System.IO.File.Exists(path)) { W("MENU70 NOFILE " + path + "\n"); return; }
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { W("MENU70 无 TextureImporter " + path + "\n"); return; }

            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaSource = needAlpha ? TextureImporterAlphaSource.FromInput
                                       : TextureImporterAlphaSource.None;

            if (type == TextureImporterType.NormalMap)
            {
                // Blender 烘的是 OpenGL 绿朝上 → 不翻 G。该字段在不同小版本里不总是公共属性
                var so = new SerializedObject(ti);
                var p = so.FindProperty("flipGreenOnImport");
                if (p != null) { p.boolValue = false; so.ApplyModifiedProperties(); }
                else W("  .. flipGreenOnImport 字段缺失，需在 Inspector 确认\n");
            }

            // 文档要 BC7。桌面平台的 format 只能走平台覆盖
            try
            {
                var ps = ti.GetPlatformTextureSettings("Standalone");
                ps.overridden = true;
                ps.format = TextureImporterFormat.BC7;
                ps.maxTextureSize = 2048;
                ti.SetPlatformTextureSettings(ps);
            }
            catch (System.Exception e) { W("  .. BC7 覆盖失败 " + path + " :: " + e.Message + "\n"); }

            AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            W("MENU70 tex " + System.IO.Path.GetFileName(path)
              + " type " + ti.textureType + " sRGB " + ti.sRGBTexture
              + " alpha " + ti.alphaSource + " wrap " + ti.wrapMode
              + " standalone " + ti.GetPlatformTextureSettings("Standalone").format + "\n");
        }

        // ═════════════════════════ 71 ═════════════════════════
        [MenuItem("Tools/Trees/71 交付树资产：建两个材质")]
        public static void BuildMaterials()
        {
            if (UnityEngine.Application.isPlaying)
            { W("MENU71 REFUSED: 在 Play 模式，先退出\n"); return; }

            W("MENU71 begin\n");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { W("MENU71 FAIL: URP/Lit 找不到\n"); return; }

            var bark = ForceShader(lit, MatFolder + "/M_Tree_Bark.mat", "M_Tree_Bark");
            bark.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "/T_TreeBark_BaseColor.png"));
            // 交付文档没规定 tint，默认就是纯白；但潭场景把环境光压到约 1/4、太阳抬到 3.0 之后，
            // 浅驼色贴图在暗底里亮成一根"白色开瓶器"，枝桩轮廓反而最抢眼。
            // 给一个暖褐滤镜把它压回中段亮度 —— 只动色，不动 Metallic/Glossiness/法线。
            bark.SetColor("_BaseColor", new Color(0.60f, 0.54f, 0.48f, 1f));
            bark.SetFloat("_Surface", 0f);      // Opaque
            bark.SetFloat("_Cull", 2f);         // Front
            bark.SetFloat("_Metallic", 0f);     // 工作流 Metallic，不用 Mask Map
            // 文档第 4 节允许"常数 0.65 / 接 Roughness 图"二选一，这里取常数：
            // URP 14 的 Mask Map 通道选择在不同小版本里字段名不一致，脚本盲设风险高。
            bark.SetFloat("_GlossMapScale", 0.35f);
            bark.SetFloat("_Glossiness", 0.65f);
            bark.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "/T_TreeBark_Normal.png"));
            bark.SetFloat("_BumpScale", 1f);
            bark.EnableKeyword("_NORMALMAP");

            // 叶片不用内置 Lit —— 它没有 transmission 通道，逆光的叶子只会黑成剪影。
            var leafSh = Shader.Find("WaterProject/TreeLeaf");
            bool custom = leafSh != null;
            if (!custom) W("MENU71 ⚠ WaterProject/TreeLeaf 找不到（多半是没编译过），叶片退回 URP/Lit\n");
            var leaf = ForceShader(custom ? leafSh : lit, MatFolder + "/M_Tree_Leaf.mat", "M_Tree_Leaf");
            leaf.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "/T_TreeLeaf_Atlas.png"));
            leaf.SetColor("_BaseColor", Color.white);
            if (custom)
            {
                leaf.SetFloat("_Cutoff", 0.5f);
                leaf.SetColor("_TransColor", new Color(0.55f, 0.85f, 0.35f));
                leaf.SetFloat("_TransStrength", 1.6f);
                leaf.SetFloat("_TransPower", 10f);
                leaf.SetFloat("_WindStrength", 0.12f);
                leaf.SetFloat("_WindFreq", 1.1f);
                // 交付说明第 5 节建议 windDir = (1,0,0.35) 归一化
                leaf.SetVector("_WindDir", new Vector4(0.943f, 0f, 0.330f, 0f));
            }
            else
            {
                leaf.SetFloat("_Surface", 0f);
                leaf.SetFloat("_AlphaClip", 1f);
                leaf.SetFloat("_Cutoff", 0.5f);
                leaf.SetFloat("_Cull", 0f);
                leaf.SetFloat("_Metallic", 0f);
                leaf.SetFloat("_Glossiness", 0.42f);
                leaf.EnableKeyword("_ALPHATEST_ON");
            }

            W("MENU71 bark smoothness " + bark.GetFloat("_Glossiness")
              + " leaf shader " + leaf.shader.name
              + " trans " + (custom ? leaf.GetFloat("_TransStrength").ToString("F2") : "N/A")
              + " wind " + (custom ? leaf.GetFloat("_WindStrength").ToString("F2") : "N/A")
              + " baseMap " + (leaf.GetTexture("_BaseMap") != null) + "\n");
        }

        static Material ForceShader(Shader sh, string path, string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = sh;
            return m;
        }

        // ═════════════════════════ 72 ═════════════════════════
        [MenuItem("Tools/Trees/72 交付树资产：生成 Prefab(材质+胶囊碰撞)")]
        public static void BuildPrefabs()
        {
            if (UnityEngine.Application.isPlaying)
            { W("MENU72 REFUSED: 在 Play 模式，先退出\n"); return; }

            var bark = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/M_Tree_Bark.mat");
            var leaf = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/M_Tree_Leaf.mat");
            if (bark == null || leaf == null) { W("MENU72 REFUSED: 先跑 71 建材质\n"); return; }

            W("MENU72 begin\n");
            EnsureFolder(PrefFolder);
            // 临时加空场景，避免把实例污染进 PondScene
            var tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            W("MENU72 temp scene ok\n");
            try
            {
                foreach (var path in FbxPaths)
                {
                    try { BuildOnePrefab(path, bark, leaf); }
                    catch (System.Exception e)
                    { W("MENU72 THREW " + path + " :: " + e.GetType().Name + " :: " + e.Message + "\n"); }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(tmp, true);
            }
            AssetDatabase.SaveAssets();
            W("MENU72 done -> " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + "\n");
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static void BuildOnePrefab(string path, Material bark, Material leaf)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (inst == null) { W("MENU72 FAIL instantiate " + path + "\n"); return; }
            inst.name = System.IO.Path.GetFileNameWithoutExtension(path);

            // 材质槽按**名字**对，不按位置猜 —— 文档专门警告"反了会树干长叶子"
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            int bound = 0;
            foreach (var r in rs)
            {
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string sn = slots[i] != null ? slots[i].name : "";
                    if (sn.Contains("Bark"))      { slots[i] = bark; bound++; }
                    else if (sn.Contains("Leaf")) { slots[i] = leaf; bound++; }
                    else W("MENU72 未识别槽 " + r.name + "[" + i + "] = '" + sn + "'\n");
                }
                r.sharedMaterials = slots;
            }

            if (bound == 0)
            {
                W("MENU72 FAIL 一个材质槽都没绑上 " + path + " renderers " + rs.Length + "\n");
                UnityEngine.Object.DestroyImmediate(inst);
                return;
            }

            var m = PickMesh(path);
            // ★ 树高不能读 mesh.bounds.y：Blender 是 Z-up，-90° 轴旋转在根节点 transform 上，
            //   局部 Y 其实是"进深"。01 号局部 (7.94, 9.68, 12.56) 而真高 12.56 在局部 Z 上。
            //   只有 Renderer 的世界包围盒才是竖起来的尺寸。
            float topY, botY, wid;
            MeasureWorld(inst, out topY, out botY, out wid);

            var cc = inst.AddComponent<CapsuleCollider>();
            cc.direction = 1;                    // Y
            cc.radius = 0.35f;
            cc.height = topY * 0.6f;
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);   // 原点在树根底部

            string outPath = PrefFolder + "/Prefab_" + inst.name + ".prefab";
            string nm = inst.name;
            // ★ 这些读数必须在 DestroyImmediate 之前取，否则 rs 里的 Renderer 已销毁
            int slotsTotal = TotalSlots(rs);
            int meshVerts = m != null ? m.vertexCount : -1;
            PrefabUtility.SaveAsPrefabAsset(inst, outPath);
            UnityEngine.Object.DestroyImmediate(inst);
            W("MENU72 " + nm + " slots bound " + bound + "/" + slotsTotal
              + " worldH " + topY.ToString("F2") + " rootY " + botY.ToString("F3")
              + " w " + wid.ToString("F2") + " meshVerts " + meshVerts
              + " -> " + outPath + "\n");
        }

        static int TotalSlots(Renderer[] rs)
        {
            int n = 0;
            foreach (var r in rs) n += r.sharedMaterials.Length;
            return n;
        }

        // 世界包围盒才是"竖起来"的尺寸（见 BuildOnePrefab 里的 Z-up 说明）
        static void MeasureWorld(GameObject go, out float topY, out float botY, out float width)
        {
            topY = float.MinValue; botY = float.MaxValue; width = 0f;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                if (b.max.y > topY) topY = b.max.y;
                if (b.min.y < botY) botY = b.min.y;
                float w = Mathf.Max(b.size.x, b.size.z);
                if (w > width) width = w;
            }
            if (topY < botY) { topY = 0f; botY = 0f; }
        }

        // ═════════════════════════ 73 ═════════════════════════
        [MenuItem("Tools/Trees/73 交付树资产：验收对账(对文档第1/8节)")]
        public static void Verify()
        {
            W("MENU73 验收 begin\n");
            var tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                for (int k = 0; k < FbxPaths.Length; k++)
                {
                    string nm = "SM_Tree_Broadleaf_" + (k + 1).ToString("00");
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefFolder + "/Prefab_" + nm + ".prefab");
                    if (pf == null) { W("MENU73 FAIL 没有 Prefab " + nm + "\n"); continue; }

                    var inst = PrefabUtility.InstantiatePrefab(pf) as GameObject;
                    inst.transform.position = Vector3.zero;
                    // ★ 不要复位 rotation：模型 Prefab 根上带着导入器的 -90° X 轴转换
                    //   （Blender 是 Z-up）。设成 identity 会把树放倒，"树高"就读成了水平宽度。

                    float topY, botY, wid;
                    MeasureWorld(inst, out topY, out botY, out wid);

                    int tris = 0, verts = 0;
                    var meshNames = new List<string>();
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (var s in r.sharedMaterials) meshNames.Add(s != null ? s.name : "NULL");
                        var me = r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                        if (me == null) continue;
                        verts += me.vertexCount;
                        for (int i = 0; i < me.subMeshCount; i++) tris += me.GetTriangles(i).Length / 3;
                    }
                    var cc = inst.GetComponent<CapsuleCollider>();
                    var main = PickMesh(FbxPaths[k]);

                    W("MENU73 [" + (k + 1) + "] " + nm
                      + " H " + topY.ToString("F2") + " (doc " + DocSpec[k].y + ", "
                      + (Mathf.Abs(topY - DocSpec[k].y) / DocSpec[k].y <= 0.05f ? "OK" : "DIFF") + ")"
                      + " | W " + wid.ToString("F2")
                      + " | rootY " + botY.ToString("F3")
                      + (Mathf.Abs(botY) < 0.05f ? " OK(贴根)" : " 不贴根")
                      + " | tris " + tris + " (doc " + DocSpec[k].x + ", "
                      + (Mathf.Abs(tris - DocSpec[k].x) / DocSpec[k].x <= 0.05f ? "OK" : "DIFF") + ")"
                      + " | verts " + verts
                      + " | vcol " + (main != null && HasVertexColor(main) ? "有" : "无")
                      + " | mats [" + string.Join(",", meshNames) + "]"
                      + " | collider " + (cc != null ? cc.radius.ToString("F2") + "r x " + cc.height.ToString("F2") + "h" : "无")
                      + "\n");
                    UnityEngine.Object.DestroyImmediate(inst);
                }
            }
            finally { EditorSceneManager.CloseScene(tmp, true); }

            foreach (var p in new[] { TexFolder + "/T_TreeBark_BaseColor.png", TexFolder + "/T_TreeBark_Roughness.png",
                                      TexFolder + "/T_TreeBark_Normal.png", TexFolder + "/T_TreeLeaf_Atlas.png" })
            {
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) { W("MENU73 FAIL 贴图缺失 " + p + "\n"); continue; }
                W("MENU73 tex " + System.IO.Path.GetFileName(p)
                  + " type=" + ti.textureType + " sRGB=" + ti.sRGBTexture
                  + " alpha=" + ti.alphaSource + " wrap=" + ti.wrapMode
                  + " mip=" + ti.mipmapEnabled
                  + " fmt=" + ti.GetPlatformTextureSettings("Standalone").format + "\n");
            }
            W("MENU73 end\n");
        }

        // 2022.3 没有 Mesh.GetChannelCount；GetVertexAttributes 不需要 Read/Write 就能查通道
        static bool HasVertexColor(Mesh m)
        {
            if (m == null) return false;
            foreach (var a in m.GetVertexAttributes())
                if (a.attribute == UnityEngine.Rendering.VertexAttribute.Color) return true;
            return false;
        }
        // FBX 里可能有多网格；取顶点最多的那个作为主网格
        static Mesh PickMesh(string fbxPath)
        {
            Mesh best = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var m = o as Mesh;
                if (m != null && (best == null || m.vertexCount > best.vertexCount)) best = m;
            }
            return best;
        }
    }
}
#endif
