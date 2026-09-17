// WaterProjectSetup.cs — 编辑器一键设置（通过 MCP runMenuItem 调用）
// 收录：URP 管线资产创建/默认规格设置、Opaque/Depth 纹理开启、水面材质创建
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class WaterProjectSetup
    {
        const string SettingsFolder = "Assets/Settings/URP";
        const string PipelinePath = SettingsFolder + "/WaterURPAsset.asset";
        const string RendererPath = SettingsFolder + "/WaterURPRenderer.asset";
        const string ShaderFolder = "Assets/Art/Shaders";

        [MenuItem("Tools/WaterProject/04 生成圆形池塘网格(半径20m)")]
        public static void CreateHighSegWaterMesh()
        {
            // 圆形池塘圆盘：半径 20m（原 40m 见方改圆），48 环 × 96 径向
            const int RINGS = 48;
            const int RADIAL = 96;
            const float RADIUS = 20f;
            string path = "Assets/Art/Meshes/LakeWaterMesh.asset";
            if (!Directory.Exists("Assets/Art/Meshes")) Directory.CreateDirectory("Assets/Art/Meshes");

            var mesh = new Mesh { name = "LakeWater_Disc" };
            // 1 中心点 + RINGS × RADIAL 环顶点
            var verts = new List<Vector3>(1 + RINGS * RADIAL);
            var uvs = new List<Vector2>(1 + RINGS * RADIAL);
            verts.Add(Vector3.zero);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int r = 1; r <= RINGS; r++)
            {
                float radius = RADIUS * r / RINGS;
                for (int j = 0; j < RADIAL; j++)
                {
                    float ang = (j / (float)RADIAL) * Mathf.PI * 2f;
                    float x = Mathf.Cos(ang) * radius;
                    float z = Mathf.Sin(ang) * radius;
                    verts.Add(new Vector3(x, 0f, z));
                    // UV 与世界 XZ 对齐（±20m → 0..1），与交互纹理 rect 严格一致
                    uvs.Add(new Vector2((x + RADIUS) / (2f * RADIUS), (z + RADIUS) / (2f * RADIUS)));
                }
            }
            int nR = verts.Count;
            var tris = new List<int>(RADIAL * 3 + (RINGS - 1) * RADIAL * 6);
            // 内圈扇形：中心 + 第 1 环
            for (int j = 0; j < RADIAL; j++)
            {
                int b = 1 + ((j + 1) % RADIAL);
                tris.Add(0); tris.Add(b); tris.Add(1 + j);
            }
            // 环间四边形拆两三角
            for (int r = 1; r < RINGS; r++)
            {
                for (int j = 0; j < RADIAL; j++)
                {
                    int a = 1 + (r - 1) * RADIAL + j;
                    int b = 1 + (r - 1) * RADIAL + ((j + 1) % RADIAL);
                    int c = 1 + r * RADIAL + j;
                    int d = 1 + r * RADIAL + ((j + 1) % RADIAL);
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var water = GameObject.Find("Water");
            if (water != null)
            {
                var mf = water.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    // 关键：从磁盘加载资产引用再赋值（内存 Mesh 对象无法序列化，保存后引用会丢失）
                    var assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (assetMesh != null)
                    {
                        mf.sharedMesh = assetMesh;
                        // 池塘模式下网格即真实尺寸，取消 60x 缩放（旧大湖缩放）
                        water.transform.localScale = Vector3.one;
                        AssetDatabase.SaveAssets();
                        Debug.Log($"[WaterProject] 圆形池塘网格已应用：半径{RADIUS}m，{RINGS}环x{RADIAL}径向={mesh.vertexCount}顶点/{mesh.triangles.Length/3}三角（资产引用: {path}）");
                    }
                    else
                    {
                        Debug.LogError("[WaterProject] 网格资产加载失败：" + path);
                    }
                }
            }
            else
            {
                Debug.Log($"[WaterProject] 网格已生成 {path}（未找到 Water 对象，未应用）");
                AssetDatabase.SaveAssets();
            }
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/WaterProject/05 近距离特写")]
        [MenuItem("Tools/WaterProject/05 近距离特写水景视角 (Scene View)")]
        public static void FrameSceneViewClose()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.orthographic = false;
            // 贴近水面低角度，展现波浪/曲面细分级配与日光闪烁（池塘特写）
            sv.LookAt(new Vector3(0f, 0.6f, 0f), Quaternion.Euler(-2f, 180f, 0f), 4.5f);
            sv.Repaint();
            Debug.Log("[WaterProject] SceneView framed close-up.");
        }

        [MenuItem("Tools/WaterProject/10 导入外部unitypackage(读Logs/import_path.txt)")]
        public static void ImportExternalPackage()
        {
            string pf = Path.Combine(Application.dataPath, "../Logs/import_path.txt");
            pf = Path.GetFullPath(pf);
            if (!File.Exists(pf)) { UnityEngine.Debug.LogWarning($"[WaterProject] 未找到 {pf}，请先写入要导入的 unitypackage 完整路径。"); return; }
            string pkgPath = File.ReadAllText(pf).Trim().Trim('"');
            if (!File.Exists(pkgPath)) { UnityEngine.Debug.LogError($"[WaterProject] 包文件不存在：{pkgPath}"); return; }
            // false = 不弹对话框直接导入到当前项目
            AssetDatabase.ImportPackage(pkgPath, false);
            UnityEngine.Debug.Log($"[WaterProject] 已触发导入：{pkgPath}（导入完成后请运行 03 或手动检查 Assets/ 下新目录）");
        }

        [MenuItem("Tools/WaterProject/12 一键外场景与水体厚度(池壁+池底)")]
        public static void BuildOuterScene()
        {
            // 1) 地面：60×60m、顶面抬到水线 y=0（沙岸与水齐平，消除"水面悬空"）
            var ground = UnityEngine.GameObject.Find("Ground");
            if (ground != null)
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(6f, 1f, 6f); // Cube 底高1m → 顶面 y=0
            }

            string groundMatPath = "Assets/Art/Materials/Ground_Lake.mat";
            var sandMat = AssetDatabase.LoadAssetAtPath<Material>(groundMatPath);

            // 2) 池底（湖床）：半径20m 圆盘，顶面 y=-1.0（水深 1m，折射深度感）
            var oldFloor = UnityEngine.GameObject.Find("PondFloor");
            if (oldFloor != null) UnityEngine.Object.DestroyImmediate(oldFloor);
            var floor = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cylinder);
            floor.name = "PondFloor";
            floor.transform.position = new Vector3(0f, -1.05f, 0f);    // 缩放后顶盖 y≈-1.0
            floor.transform.localScale = new Vector3(40f, 0.05f, 40f);  // 半径 20m
            if (sandMat != null) floor.GetComponent<MeshRenderer>().sharedMaterial = sandMat;
            var floorCol = floor.GetComponent<Collider>();
            if (floorCol != null) UnityEngine.Object.DestroyImmediate(floorCol);

            // 3) 池壁（水体厚度侧壁）：半径20m 竖壁 y∈[-1.02,-0.02]（顶盖藏于水面下 2cm 防 Z 冲）
            var oldWall = UnityEngine.GameObject.Find("PondWall");
            if (oldWall != null) UnityEngine.Object.DestroyImmediate(oldWall);
            string wallMatPath = "Assets/Art/Materials/PondWall.mat";
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>(wallMatPath);
            if (wallMat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    if (!System.IO.Directory.Exists("Assets/Art/Materials")) System.IO.Directory.CreateDirectory("Assets/Art/Materials");
                    wallMat = new Material(lit);
                    wallMat.SetColor("_BaseColor", new Color(0.10f, 0.36f, 0.34f, 1f)); // 水体侧壁青色
                    AssetDatabase.CreateAsset(wallMat, wallMatPath);
                }
            }
            var wall = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cylinder);
            wall.name = "PondWall";
            wall.transform.position = new Vector3(0f, -0.52f, 0f);      // 跨度 -1.02..-0.02
            wall.transform.localScale = new Vector3(40f, 0.5f, 40f);     // 高 1.02m
            if (wallMat != null) wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
            var wallCol = wall.GetComponent<Collider>();
            if (wallCol != null) UnityEngine.Object.DestroyImmediate(wallCol);

            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 外场景重建完成：Ground 60×60m 顶面0；PondFloor(-1.0)；PondWall(0..-1m)。");
        }

        [MenuItem("Tools/WaterProject/13 对标视频外景绿化(环池树木+草地)")]
        public static void BuildSceneVegetation()
        {
            string treePath = "Assets/Art/StylePacks/正式素材/Trees/Trees_Quaternius.glb";
            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(treePath);
            if (treePrefab == null) { UnityEngine.Debug.LogError("[WaterProject] 未找到树木预制体：" + treePath + "（请先导入 GLB）"); return; }

            // 1) 草地地面：Ground 换草绿；PondFloor 湖底仍用沙色（折射湖底质感）
            string grassPath = "Assets/Art/Materials/GrassGround.mat";
            var grass = AssetDatabase.LoadAssetAtPath<Material>(grassPath);
            if (grass == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    if (!System.IO.Directory.Exists("Assets/Art/Materials")) System.IO.Directory.CreateDirectory("Assets/Art/Materials");
                    grass = new Material(lit);
                    grass.SetColor("_BaseColor", new Color(0.42f, 0.55f, 0.30f, 1f)); // 草绿（对标视频远岸植被色）
                    AssetDatabase.CreateAsset(grass, grassPath);
                }
            }
            var ground = UnityEngine.GameObject.Find("Ground");
            if (ground != null && grass != null) ground.GetComponent<MeshRenderer>().sharedMaterial = grass;

            // 2) 环池绿植：清理旧树，重建双环密度（主环 20.5~25m + 内环 21~22.5m 错位插空），高低错落成"绿墙"
            var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.Transform>();
            foreach (var tr in all) if (tr != null && tr.name != null && tr.name.StartsWith("TreeRing_")) UnityEngine.Object.DestroyImmediate(tr.gameObject);

            var rnd = new System.Random(20260917);
            int count = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int n = (pass == 0) ? 22 : 12;   // 主环22株 + 内环12株插空
                for (int i = 0; i < n; i++)
                {
                    float ang = ((i + (pass == 1 ? 0.5f : 0f)) / n) * Mathf.PI * 2f;
                    float radius = (pass == 0)
                        ? 20.8f + (float)rnd.NextDouble() * 4.2f
                        : 22.2f + (float)rnd.NextDouble() * 1.2f;
                    float x = Mathf.Cos(ang) * radius;
                    float z = Mathf.Sin(ang) * radius;
                    var inst = PrefabUtility.InstantiatePrefab(treePrefab) as GameObject;
                    if (inst == null) continue;
                    inst.name = string.Format("TreeRing_{0:00}", count++);
                    inst.transform.position = new Vector3(x, 0f, z);
                    inst.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                    float s = 1.1f + (float)rnd.NextDouble() * 1.7f;  // 1.1~2.8（视频远岸树带较高较密）
                    inst.transform.localScale = new Vector3(s, s, s);
                    if (i % 5 == 2) // 近岸小灌丛：贴水线、更小更矮
                    {
                        inst.transform.position = new Vector3(x * 0.8f, 0f, z * 0.8f);
                        inst.transform.localScale = inst.transform.localScale * 0.6f;
                    }
                }
            }
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 环池绿化完成：34 株树木双环(1.1~2.8x)环绕半径20.8~26m + 草地地面（湖底仍沙色）。");
        }

        [MenuItem("Tools/WaterProject/14 太阳升高至正午视角(收树影)")]
        public static void RaiseSunNoon()
        {
            var sun = UnityEngine.GameObject.Find("Directional Light");
            if (sun == null) { UnityEngine.Debug.LogWarning("[WaterProject] 未找到 Directional Light"); return; }
            // 抬高仰角缩短环树投影（原 42°→65°），池塘内侧得以全亮，走镜视频整体受光效果
            sun.transform.rotation = Quaternion.Euler(65f, -90f, 0f);
            var light = sun.GetComponent<Light>();
            if (light != null) light.intensity = 1.4f;
            EditorUtility.SetDirty(sun);
            UnityEngine.Debug.Log("[WaterProject] 太阳仰角→65°，强度→1.4（树影收短，水面全亮）。");
        }

        [MenuItem("Tools/WaterProject/15 水面上浮2cm+主相机对准(修复共面)")]
        public static void FixWaterCoplanarAndFrameCam()
        {
            // 1) 水面与 60×60 草地地面共面 y=0 → z-fighting（陡视角下草地吃掉水面）
            //    水上浮 2cm 彻底分离（荷叶/交互对象原样，相对厚度无感）
            var water = GameObject.Find("Water");
            if (water != null)
            {
                var p = water.transform.position;
                water.transform.position = new Vector3(p.x, 0.02f, p.z);
            }

            // 2) 主相机：原俯角 -24° 拍的全是近岸草地 → 改为视频式低机位横贯池塘
            //    (0, 1.8, -15) 微微下俯 -1.5°，16:9 下水面占下部、远岸绿带在上 1/3
            var cam = GameObject.Find("Main Camera");
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 1.8f, -15f);
                cam.transform.rotation = Quaternion.Euler(-1.5f, 0f, 0f);
            }

            EditorUtility.SetDirty(water);
            EditorUtility.SetDirty(cam);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 水面 y=0.02（脱离地面共面）；主相机 (0,1.8,-15) 俯角-1.5° 对准池塘。");
        }

        [MenuItem("Tools/WaterProject/09 水体颜色探针(洋红/恢复)")]
        public static void ProbeWaterColor()
        {
            string matPath = "Assets/Art/Materials/Water_Advanced.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { UnityEngine.Debug.LogError("[WaterProject] 材质不存在：" + matPath); return; }
            if (System.IO.File.Exists(matPath + ".probe"))
            {
                mat.SetColor("_ShallowColor", new Color(0.14f, 0.50f, 0.42f, 1f));
                mat.SetColor("_DeepColor", new Color(0.03f, 0.18f, 0.19f, 1f));
                mat.SetFloat("_Opacity", 0.97f);
                System.IO.File.Delete(matPath + ".probe");
                UnityEngine.Debug.Log("[WaterProject] PROBE restored pond colors");
            }
            else
            {
                mat.SetColor("_ShallowColor", new Color(1f, 0f, 1f, 1f));
                mat.SetColor("_DeepColor", new Color(1f, 0f, 1f, 1f));
                mat.SetFloat("_Opacity", 1f);
                System.IO.File.WriteAllText(matPath + ".probe", "1");
                UnityEngine.Debug.Log("[WaterProject] PROBE magenta on");
            }
            EditorUtility.SetDirty(mat);
        }

        [MenuItem("Tools/WaterProject/08 查询主相机状态")]
        public static void DumpCameraState()
        {
            var cam = Camera.main;
            if (cam == null) { UnityEngine.Debug.Log("[WaterProject] CAM No Camera.main!"); return; }
            var t = cam.transform;
            UnityEngine.Debug.Log($"[WaterProject] CAM pos={t.position} rot={t.eulerAngles} fov={cam.fieldOfView} near={cam.nearClipPlane} far={cam.farClipPlane} cull={cam.cullingMask} clear={cam.clearFlags} targetTex={(cam.targetTexture != null ? cam.targetTexture.name : "null")} active={cam.enabled} gameView?={cam.isActiveAndEnabled}");
        }

        [MenuItem("Tools/WaterProject/07 读取演示FPS (Play中)")]
        public static void ReadDemoFps()
        {
            var ui = UnityEngine.Object.FindFirstObjectByType<WaterProject.Presentation.WaterDemoUI>();
            float fps = WaterProject.Presentation.WaterDemoUI.LastFps;
            UnityEngine.Debug.Log($"[WaterProject] FPS={fps:F1} | uiFound={(ui != null)} | isPlaying={Application.isPlaying}");
        }

        [MenuItem("Tools/WaterProject/06 查询编辑器/播放状态")]
        public static void QueryState()
        {
            UnityEngine.Debug.Log($"[WaterProject] STATE isPlaying={Application.isPlaying} | isEditor={Application.isEditor} | timeScale={Time.timeScale}");
        }

        [MenuItem("Tools/WaterProject/00 检查Unity账号登录状态")]
        public static void CheckUnityLogin()
        {
            try
            {
                string name = UnityEditor.CloudProjectSettings.userName;
                string core = UnityEditor.CloudProjectSettings.userId;
                bool token = !string.IsNullOrEmpty(UnityEditor.CloudProjectSettings.accessToken);
                string msg = $"[WaterProject] Unity账号登录状态：userName={(string.IsNullOrEmpty(name) ? "(空)" : name)} | userId={(string.IsNullOrEmpty(core) ? "(空)" : core)} | token={token}";
                UnityEngine.Debug.Log(msg);
                EditorUtility.DisplayDialog("Unity 账号状态", msg, "确定");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.Log("[WaterProject] 账号查询失败: " + ex.Message);
            }
        }

        [MenuItem("Tools/WaterProject/03 生成水体系统预制体")]
        public static void CreateWaterPrefabs()
        {
            bool dirty = false;
            TrySavePrefab("Water", "Assets/Prefabs/Water_Lake.prefab", ref dirty);
            TrySavePrefab("WaterInteraction", "Assets/Prefabs/WaterInteractionSystem.prefab", ref dirty);
            TrySavePrefab("WaterDemoUI", "Assets/Prefabs/WaterDemoUI.prefab", ref dirty);

            if (dirty) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
            Debug.Log("[WaterProject] 水体预制体生成完成。");
        }

        static void TrySavePrefab(string sceneGoName, string prefabPath, ref bool dirty)
        {
            var go = GameObject.Find(sceneGoName);
            if (go == null)
            {
                Debug.LogWarning($"[WaterProject] 场景中未找到 '{sceneGoName}'，跳过预制体。");
                return;
            }
            if (!Directory.Exists("Assets/Prefabs")) Directory.CreateDirectory("Assets/Prefabs");
            var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            dirty |= saved != null;
            Debug.Log($"[WaterProject] 预制体: {prefabPath} -> {(saved != null ? "OK" : "失败")}");
        }

        [MenuItem("Tools/WaterProject/02 全景取景")]
        [MenuItem("Tools/WaterProject/02 对齐水景视角 (Scene View)")]
        public static void FrameSceneView()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.orthographic = false;
            // 相机对准池塘中心：从 -Z 侧高处俯瞰（距离收紧适配小池塘）
            sv.LookAt(new Vector3(0f, 1.2f, 21f), Quaternion.Euler(-1.5f, 180f, 0f), 38f);
            sv.FrameSelected(false);
            sv.Repaint();
            Debug.Log("[WaterProject] SceneView framed to water.");
        }

        [MenuItem("Tools/WaterProject/01 一键URP与资源设置")]
        public static void SetupURPAndAssets()
        {
            // ── 1) 创建/复用 URP 管线资产 ─────────────────────────────
            if (!Directory.Exists(SettingsFolder)) Directory.CreateDirectory(SettingsFolder);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            // ── 2) Opaque / Depth 纹理（折射必需）─────────────────────
            var so = new SerializedObject(pipeline);
            var op = so.FindProperty("m_RequireOpaqueTexture");
            if (op != null) op.boolValue = true;
            var dp = so.FindProperty("m_RequireDepthTexture");
            if (dp != null) dp.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            // ── 3) 设为默认管线（Graphics + Quality）────────────────
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            // ── 4) 创建水面材质（引用 WaterProject/Water_Advanced）─────
            string matPath = "Assets/Art/Materials/Water_Advanced.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                var shader = Shader.Find("WaterProject/Water_Advanced");
                if (shader != null)
                {
                    if (!Directory.Exists("Assets/Art/Materials")) Directory.CreateDirectory("Assets/Art/Materials");
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    Debug.LogWarning("[WaterProject] 未找到 WaterProject/Water_Advanced shader，材质稍后手动创建。");
                }
            }

            // ── 5) 默认格斯特纳波参数（材质可覆盖）───────────────────
            if (mat != null)
            {
                mat.SetFloat("_WaveCount", 4);
                // 翡翠-青绿色调（对照目标视频观感）
                mat.SetColor("_ShallowColor", new Color(0.20f, 0.72f, 0.82f, 1f));
                mat.SetColor("_DeepColor", new Color(0.09f, 0.38f, 0.47f, 1f));
                mat.SetFloat("_SunStrength", 0.3f);
                // 透光感参数（阳光透射 + 体积散射）
                mat.SetColor("_WaterScatter", new Color(0.42f, 0.75f, 0.88f, 1f));
                mat.SetFloat("_ScatterStrength", 0.38f);
                mat.SetFloat("_SunTransmission", 0.7f);
                mat.SetFloat("_TransGloss", 48f);
                mat.SetFloat("_WaveTimeScale", 1.8f);
                // (振幅 A, 角频率 ω, 陡度 Q, -)
                mat.SetVectorArray("_Waves", new Vector4[]
                {
                    new Vector4(0.55f, 0.85f, 0.80f, 0),
                    new Vector4(0.24f, 1.90f, 0.85f, 0),
                    new Vector4(0.10f, 3.40f, 0.90f, 0),
                    new Vector4(0.045f, 5.60f, 0.95f, 0),
                });
                // (方向 dx, dz, 相位, -)
                mat.SetVectorArray("_WaveDirs", new Vector4[]
                {
                    new Vector4(1.00f, 0.18f, 0.6f, 0),
                    new Vector4(-0.35f, 0.94f, 2.1f, 0),
                    new Vector4(0.88f, -0.47f, 3.5f, 0),
                    new Vector4(-0.55f, -0.83f, 5.0f, 0),
                });
                EditorUtility.SetDirty(mat);
            }

            // ── 6) 地面/湖底沙色材质（URP Lit；存在时也强制应用标准色）──
            string groundMatPath = "Assets/Art/Materials/Ground_Lake.mat";
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>(groundMatPath);
            if (groundMat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    groundMat = new Material(lit);
                    AssetDatabase.CreateAsset(groundMat, groundMatPath);
                }
            }
            if (groundMat != null)
            {
                groundMat.SetColor("_BaseColor", new Color(0.64f, 0.54f, 0.40f, 1f)); // 中等棕褐沙地
                EditorUtility.SetDirty(groundMat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WaterProject] URP 设置完成: " + PipelinePath + " (Opaque=" + pipeline.supportsCameraOpaqueTexture
                + ", Depth=" + pipeline.supportsCameraDepthTexture + ", 默认管线=" + (GraphicsSettings.defaultRenderPipeline != null) + ")");
        }
    }
}
#endif