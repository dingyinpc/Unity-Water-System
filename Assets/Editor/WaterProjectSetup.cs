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

        [MenuItem("Tools/WaterProject/16 波浪幅度/流速(诊断+落盘)")]
        public static void ForcePersistWaves()
        {
            string matPath = "Assets/Art/Materials/Water_Advanced.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { UnityEngine.Debug.LogWarning("[WaterProject] 材质不存在"); return; }

            // 波浪已内置 shader 固定四波（不依赖数组序列化），此处只落盘标量旋钮
            mat.SetFloat("_WaveCount", 4);
            mat.SetFloat("_WaveAmpScale", 0.5f);
            mat.SetFloat("_WaveTimeScale", 1.8f);
            mat.SetFloat("_InteractionAmp", 0.22f);
            mat.SetFloat("_InteractionFoam", 0.45f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UnityEngine.Debug.Log($"[WaterProject] 标量旋钮落盘: _WaveAmpScale={mat.GetFloat("_WaveAmpScale")} _WaveTimeScale={mat.GetFloat("_WaveTimeScale")} _InteractionAmp={mat.GetFloat("_InteractionAmp")} _InteractionFoam={mat.GetFloat("_InteractionFoam")}");
        }

        [MenuItem("Tools/WaterProject/17 沙滩与缓坡池底(浅水见沙)")]
        public static void BuildShoreBeach()
        {
            // —— 1) 缓坡池底：中心 -1.03m 深 → 岸边 -0.06m 极浅（浅水见沙，对标视频沙滩岸） ——
            string floorPath = "Assets/Art/Meshes/PondFloorShore.asset";
            {
                int seg = 96, rings = 30;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= rings; gr++)
                {
                    float r = 20f * gr / rings;
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float shore = Mathf.SmoothStep(10f, 17.8f, r);
                        float y = Mathf.Lerp(-1.03f, -0.06f, shore);
                        float bump = (Mathf.PerlinNoise(r * 0.22f * Mathf.Cos(th) + 41.7f, r * 0.22f * Mathf.Sin(th) + 13.3f) - 0.5f) * 0.22f;
                        y += bump;
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2(r / 20f, (float)gs / seg));
                    }
                }
                for (int gr = 0; gr < rings; gr++)
                    for (int gs = 0; gs < seg; gs++)
                    {
                        int a = gr * (seg + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "PondFloorShore" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath);
                if (oldAsset == null) AssetDatabase.CreateAsset(m, floorPath);
                else { EditorUtility.CopySerialized(m, oldAsset); EditorUtility.SetDirty(oldAsset); }
                AssetDatabase.SaveAssets();
                var floor = UnityEngine.GameObject.Find("PondFloor");
                if (floor != null)
                {
                    var mf = floor.GetComponent<MeshFilter>();
                    if (mf != null) mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath);
                    floor.transform.position = new Vector3(0, 0, 0);
                    EditorUtility.SetDirty(floor);
                }
            }

            // —— 2) 沙滩环带：r17.2→20.8，内缘水下 -0.04 → 外缘岸上 +0.14（覆盖原池壁顶，形成缓坡） ——
            string sandRingPath = "Assets/Art/Meshes/ShoreSandRing.asset";
            {
                int seg = 96;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= 1; gr++)
                {
                    float tt = gr;
                    float r = Mathf.Lerp(17.2f, 20.8f, tt);
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float yEdge = Mathf.Lerp(-0.04f, 0.14f, tt);
                        float wob = (Mathf.PerlinNoise(th * 2.1f + 5f, tt * 7f) - 0.5f) * 0.06f;
                        verts.Add(new Vector3(Mathf.Cos(th) * r, yEdge + wob, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / seg, tt));
                    }
                }
                for (int gs = 0; gs < seg; gs++)
                {
                    int a = gs, b = gs + 1;
                    int c = seg + 1 + gs, d = seg + 2 + gs;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
                var m = new Mesh { name = "ShoreSandRing" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(sandRingPath);
                if (oldAsset == null) AssetDatabase.CreateAsset(m, sandRingPath);
                else { EditorUtility.CopySerialized(m, oldAsset); EditorUtility.SetDirty(oldAsset); }
                AssetDatabase.SaveAssets();

                var ring = UnityEngine.GameObject.Find("ShoreSandRing");
                if (ring == null) ring = new UnityEngine.GameObject("ShoreSandRing");
                var mr = ring.GetComponent<MeshRenderer>();
                if (mr == null) mr = ring.AddComponent<MeshRenderer>();
                var mf = ring.GetComponent<MeshFilter>();
                if (mf == null) mf = ring.AddComponent<MeshFilter>();
                mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(sandRingPath);
                // 沙材质（亮暖沙）
                string sandMatPath = "Assets/Art/Materials/Sand_Shore.mat";
                var sand = AssetDatabase.LoadAssetAtPath<Material>(sandMatPath);
                if (sand == null)
                {
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit != null) { sand = new Material(lit); AssetDatabase.CreateAsset(sand, sandMatPath); }
                }
                if (sand != null)
                {
                    sand.SetColor("_BaseColor", new Color(0.82f, 0.73f, 0.55f, 1f));
                    sand.SetFloat("_Smoothness", 0.3f);
                    EditorUtility.SetDirty(sand);
                }
                mr.sharedMaterial = sand;
                ring.transform.position = Vector3.zero;
            }

            // —— 3) 池壁改沙色（近岸一体） ——
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/PondWall.mat");
            if (wallMat != null) { wallMat.SetColor("_BaseColor", new Color(0.68f, 0.58f, 0.44f, 1f)); EditorUtility.SetDirty(wallMat); }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 沙滩完成：缓坡池底(岸边-0.06浅水见沙) + 沙滩环带(内水下-0.04→外岸上+0.14) + 池壁沙色。");
        }

        [MenuItem("Tools/WaterProject/18 岸边与水中卵石")]
        public static void BuildPebbles()
        {
            // 卵石 mesh ×3（Perlin 变形 + 压扁），资产化便于调色
            string[] stonePaths = { "Assets/Art/Meshes/Stone_01.asset", "Assets/Art/Meshes/Stone_02.asset", "Assets/Art/Meshes/Stone_03.asset" };
            int[] stoneSeeds = { 20260901, 20260902, 20260903 };
            var stoneMesh = new Mesh[3];
            for (int s = 0; s < 3; s++)
            {
                int phi = 7, theta = 8;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                for (int p = 0; p <= phi; p++)
                {
                    float pa = Mathf.PI * p / phi;
                    for (int t = 0; t <= theta; t++)
                    {
                        float ta = Mathf.PI * 2f * t / theta;
                        float x = Mathf.Sin(pa) * Mathf.Cos(ta);
                        float y = Mathf.Cos(pa);
                        float z = Mathf.Sin(pa) * Mathf.Sin(ta);
                        float n = Mathf.PerlinNoise(x * 2.2f + stoneSeeds[s] * 0.0037f, z * 2.2f + stoneSeeds[s] * 0.0091f);
                        float rr = 0.82f + 0.38f * n;
                        verts.Add(new Vector3(x * rr, y * rr * 0.62f, z * rr));
                        if (p < phi && t < theta)
                        {
                            int a = p * (theta + 1) + t, b = a + 1;
                            int c = (p + 1) * (theta + 1) + t, d = c + 1;
                            tris.Add(a); tris.Add(c); tris.Add(b);
                            tris.Add(b); tris.Add(c); tris.Add(d);
                        }
                    }
                }
                var m = new Mesh { name = "Stone_" + (s + 1) };
                m.SetVertices(verts); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(stonePaths[s]);
                if (oldAsset == null) AssetDatabase.CreateAsset(m, stonePaths[s]);
                else { EditorUtility.CopySerialized(m, oldAsset); EditorUtility.SetDirty(oldAsset); }
                stoneMesh[s] = AssetDatabase.LoadAssetAtPath<Mesh>(stonePaths[s]);
            }
            AssetDatabase.SaveAssets();

            // 石材质（中性灰）
            string stoneMatPath = "Assets/Art/Materials/Stone.mat";
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>(stoneMatPath);
            if (stoneMat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) { stoneMat = new Material(lit); AssetDatabase.CreateAsset(stoneMat, stoneMatPath); }
            }
            if (stoneMat != null)
            {
                stoneMat.SetColor("_BaseColor", new Color(0.52f, 0.53f, 0.55f, 1f));
                stoneMat.SetFloat("_Smoothness", 0.35f);
                EditorUtility.SetDirty(stoneMat);
            }

            var rng = new System.Random(20260918);
            // 组：沙滩 4 / 水陆交界 3 / 水中浅滩 3
            for (int g = 0; g < 3; g++)
            {
                int count = g == 0 ? 4 : (g == 1 ? 3 : 3);
                float rMin = g == 0 ? 18.4f : (g == 1 ? 17.2f : 13.0f);
                float rMax = g == 0 ? 20.4f : (g == 1 ? 18.8f : 16.8f);
                float yBase = g == 0 ? 0.16f : (g == 1 ? 0.035f : -0.012f);
                float scMin = g == 0 ? 0.45f : (g == 1 ? 0.3f : 0.24f);
                float scMax = g == 0 ? 0.85f : (g == 1 ? 0.6f : 0.5f);
                for (int i = 0; i < count; i++)
                {
                    float th = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
                    float r = rMin + (float)rng.NextDouble() * (rMax - rMin);
                    float sc = scMin + (float)rng.NextDouble() * (scMax - scMin);
                    var go = new UnityEngine.GameObject("Pebble_" + g + "_" + i);
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = stoneMesh[rng.Next(stoneMesh.Length)];
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = stoneMat;
                    go.transform.position = new Vector3(Mathf.Cos(th) * r, yBase + (float)rng.NextDouble() * 0.02f, Mathf.Sin(th) * r);
                    go.transform.localScale = new Vector3(sc, sc * 0.7f, sc);
                    go.transform.rotation = Quaternion.Euler(rng.Next(0, 15), rng.Next(0, 360), rng.Next(-12, 12));
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 卵石完成：沙滩4 + 水陆交界3 + 水中浅滩3（seed 20260918）。");
        }

        [MenuItem("Tools/WaterProject/19 小荷叶群落(小而美)")]
        public static void BuildSmallLilies()
        {
            string lilyPath = "Assets/Art/StylePacks/正式素材/Lotus/WaterLilies_GooglePoly.glb";
            string lotusPath = "Assets/Art/StylePacks/正式素材/Lotus/LotusFlower_SeanTarrant.glb";
            var lily = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lilyPath);
            var lotus = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lotusPath);
            if (lily == null) { UnityEngine.Debug.LogWarning("[WaterProject] 找不到荷叶预制体: " + lilyPath); return; }

            var rng = new System.Random(20260919);
            // 12 片小荷叶：r 2~16m 散布，避开中央视口正前近景
            for (int i = 0; i < 12; i++)
            {
                float th = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
                float r = 2.5f + (float)rng.NextDouble() * 13.5f;
                float sc = 0.26f + (float)rng.NextDouble() * 0.24f;
                var inst = PrefabUtility.InstantiatePrefab(lily) as UnityEngine.GameObject;
                if (inst == null) continue;
                inst.name = "SmallLily_" + i.ToString("00");
                inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0.04f, Mathf.Sin(th) * r);
                inst.transform.localScale = new Vector3(sc, sc, sc);
                inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            }
            // 3 株小荷花
            if (lotus != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    float th = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
                    float r = 4f + (float)rng.NextDouble() * 11f;
                    float sc = 0.28f + (float)rng.NextDouble() * 0.14f;
                    var inst = PrefabUtility.InstantiatePrefab(lotus) as UnityEngine.GameObject;
                    if (inst == null) continue;
                    inst.name = "SmallLotus_" + i.ToString("00");
                    inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0.04f, Mathf.Sin(th) * r);
                    inst.transform.localScale = new Vector3(sc, sc, sc);
                    inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 小荷叶完成：12 片小荷叶 + 3 株小荷花（seed 20260919）。");
        }

        // 不规则岸线函数：水盘/沙洲/池底共用（多波瓣 + 破碎感；基准 20.5 保持 20m 级水面）
        static float ShoreR(float th)
        {
            float w1 = 0.88f + 0.12f * Mathf.Sin(th * 2f + 1.3f);
            float w2 = 0.09f * Mathf.Sin(th * 4f + 0.6f);
            float w3 = 0.07f * Mathf.Sin(th * 7f + 2.4f);
            return Mathf.Max(14f, 20.5f * (w1 + w2 + w3));
        }

        [MenuItem("Tools/WaterProject/20 沙滩+不规则水域重组(沙洲含水·小池0.6)")]
        public static void RebuildSandscapeWithWater()
        {
            System.Random rng = new System.Random(20260920);
            // 池塘整体缩小 0.6：水/沙/石/荷叶全部乘 0.6（水面 r≈12.3m 级）
            float S(float th) => ShoreR(th) * 0.6f;
            // 先清旧拼形物与荷叶（幂等）
            var oldAll = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in oldAll)
            {
                if (go != null && (go.name.StartsWith("SmallLily_") || go.name.StartsWith("SmallLotus_")
                    || go.name.StartsWith("WaterLilies") || go.name.StartsWith("LotusFlower")
                    || go.name.StartsWith("Pebble_") || go.name.StartsWith("SandBlob_")))
                { UnityEngine.Object.DestroyImmediate(go); }
            }

            // —— 1) 不规则水域盘（替换 Water 圆盘：岸线多波瓣自然弯曲） ——
            string waterPath = "Assets/Art/Meshes/LakeWaterIrregular.asset";
            {
                int seg = 120, rings = 24;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= rings; gr++)
                {
                    float rr = (float)gr / rings;
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float r = Mathf.Lerp(0.4f, S(th), rr);
                        verts.Add(new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / seg, rr));
                    }
                }
                for (int gr = 0; gr < rings; gr++)
                    for (int gs = 0; gs < seg; gs++)
                    {
                        int a = gr * (seg + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "LakeWaterIrregular" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(waterPath);
                if (oldAsset == null) AssetDatabase.CreateAsset(m, waterPath);
                else { EditorUtility.CopySerialized(m, oldAsset); EditorUtility.SetDirty(oldAsset); }
                AssetDatabase.SaveAssets();
                var water = UnityEngine.GameObject.Find("Water");
                if (water != null)
                {
                    var wmf = water.GetComponent<MeshFilter>();
                    if (wmf != null) wmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(waterPath);
                    EditorUtility.SetDirty(water);
                }
            }

            // —— 2) 池底同岸线缓坡（中心 -1.03 → 岸边 -0.06，浅水见沙） ——
            string floorPath = "Assets/Art/Meshes/PondFloorShore.asset";
            {
                int seg = 120, rings = 30;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= rings; gr++)
                {
                    float rr = (float)gr / rings;
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float r = Mathf.Lerp(0.4f, S(th) + 0.72f, rr);
                        float shore = Mathf.SmoothStep(6f, 10.5f, r);
                        float y = Mathf.Lerp(-1.03f, -0.06f, shore);
                        float bump = (Mathf.PerlinNoise(r * 0.22f * Mathf.Cos(th) + 41.7f, r * 0.22f * Mathf.Sin(th) + 13.3f) - 0.5f) * 0.22f;
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y + bump, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / seg, rr));
                    }
                }
                for (int gr = 0; gr < rings; gr++)
                    for (int gs = 0; gs < seg; gs++)
                    {
                        int a = gr * (seg + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "PondFloorShore" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath);
                if (oldAsset == null) AssetDatabase.CreateAsset(m, floorPath);
                else { EditorUtility.CopySerialized(m, oldAsset); EditorUtility.SetDirty(oldAsset); }
                AssetDatabase.SaveAssets();
                var floor = UnityEngine.GameObject.Find("PondFloor");
                if (floor != null)
                {
                    var fmf = floor.GetComponent<MeshFilter>();
                    if (fmf != null) fmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath);
                    floor.transform.position = new Vector3(0, 0, 0);
                    EditorUtility.SetDirty(floor);
                }
            }

            // —— 3) 沙洲主体：岸线内收 0.72m 藏水沙嘴(-0.005 微露，波浪下时隐时现) → 外缘 14.2m 岸上沙丘(+0.10)，Perlin 丘 ——
            string ringPath = "Assets/Art/Meshes/ShoreSandRing.asset";
            {
                int seg = 120, rings = 10;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= rings; gr++)
                {
                    float tt = (float)gr / rings;
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float rIn = Mathf.Max(7f, S(th) - 0.72f);
                        float r = Mathf.Lerp(rIn, 14.2f, tt);
                        float y = Mathf.Lerp(-0.005f, 0.10f, tt);
                        float bn = Mathf.PerlinNoise(Mathf.Cos(th) * 2.0f + 8.0f, Mathf.Sin(th) * 2.0f + tt * 3.0f);
                        y += (bn - 0.5f) * 0.08f * (0.4f + tt);
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / seg, tt));
                    }
                }
                for (int gr = 0; gr < rings; gr++)
                    for (int gs = 0; gs < seg; gs++)
                    {
                        int a = gr * (seg + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "ShoreSandRing" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
                if (oldAsset == null) AssetDatabase.CreateAsset(m, ringPath);
                else { EditorUtility.CopySerialized(m, oldAsset); EditorUtility.SetDirty(oldAsset); }
                AssetDatabase.SaveAssets();
                var ring = UnityEngine.GameObject.Find("ShoreSandRing");
                if (ring == null) ring = new UnityEngine.GameObject("ShoreSandRing");
                var rmf = ring.GetComponent<MeshFilter>();
                if (rmf == null) rmf = ring.AddComponent<MeshFilter>();
                var rmr = ring.GetComponent<MeshRenderer>();
                if (rmr == null) rmr = ring.AddComponent<MeshRenderer>();
                rmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
                var sand = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sand_Shore.mat");
                rmr.sharedMaterial = sand;
                ring.transform.position = Vector3.zero;
            }

            // —— 4) 拼形沙洲：1 半岛沙嘴 + 2 离岸小沙洲（压扁圆柱碟；旧 SandBlob_ 已在开头清除） ——
            var sand2 = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sand_Shore.mat");
            for (int i = 0; i < 3; i++)
            {
                var disk = UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                UnityEngine.Object.DestroyImmediate(disk.GetComponent<Collider>());
                disk.name = "SandBlob_" + i;
                float th = i == 0 ? -1.1f : (i == 1 ? 2.3f : -2.6f);
                float r = i == 0 ? 10.5f : (i == 1 ? 11.5f : 12.2f);
                float rr = i == 0 ? 3.3f : (i == 1 ? 2.4f : 1.8f);
                float sc = 1.0f + (float)rng.NextDouble() * 0.35f;
                disk.transform.position = new Vector3(Mathf.Cos(th) * r, i == 0 ? 0.02f : 0.03f, Mathf.Sin(th) * r);
                disk.transform.localScale = new Vector3(rr * sc, 0.10f, rr * sc * 0.8f);
                disk.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                var dmr = disk.GetComponent<MeshRenderer>();
                dmr.sharedMaterial = sand2;
            }

            // —— 4b) 石头铺边：沿岸线一圈 32 块卵石（半浸水边微露，替代旧 Pebble 点缀） ——
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Stone.mat");
            var stones = new[] {
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_01.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_02.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_03.asset") };
            for (int i = 0; i < 32; i++)
            {
                float th = i * (Mathf.PI * 2f / 32f) + (float)rng.NextDouble() * 0.2f;
                float rr = S(th) - 0.55f - (float)rng.NextDouble() * 0.4f;
                float sc = 0.34f + (float)rng.NextDouble() * 0.26f;
                var go = new UnityEngine.GameObject("EdgeStone_" + i.ToString("00"));
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = stones[rng.Next(stones.Length)];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = stoneMat;
                go.transform.position = new Vector3(Mathf.Cos(th) * rr, 0.004f + (float)rng.NextDouble() * 0.024f, Mathf.Sin(th) * rr);
                go.transform.localScale = new Vector3(sc, sc * 0.78f, sc);
                go.transform.rotation = Quaternion.Euler(rng.Next(0, 18), rng.Next(0, 360), rng.Next(-14, 14));
            }

            // —— 4c) 池壁跟随缩小（r20 → r12，被沙洲盖住不外露） ——
            var wallObj = UnityEngine.GameObject.Find("PondWall");
            if (wallObj != null) wallObj.transform.localScale = new Vector3(24f, 0.5f, 24f);

            // —— 5) 小荷叶全部重建：删旧（含 1/10 前的大荷叶） → 24 片 scale 0.026-0.05 + 6 株小荷花 ——
            var all = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in all)
            {
                if (go != null && (go.name.StartsWith("SmallLily_") || go.name.StartsWith("SmallLotus_")
                    || go.name.StartsWith("WaterLilies") || go.name.StartsWith("LotusFlower")))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            string lilyPath = "Assets/Art/StylePacks/正式素材/Lotus/WaterLilies_GooglePoly.glb";
            string lotusPath = "Assets/Art/StylePacks/正式素材/Lotus/LotusFlower_SeanTarrant.glb";
            var lily = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lilyPath);
            if (lily == null) { UnityEngine.Debug.LogWarning("[WaterProject] 找不到荷叶预制体"); return; }
            for (int i = 0; i < 24; i++)
            {
                float th = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
                float r = 0.8f + (float)rng.NextDouble() * 7.0f;        // 重摆：适配缩小后的水面（r0.8-7.8）
                float sc = 0.05f + (float)rng.NextDouble() * 0.03f;    // 幼荷叶 ≈5-8cm（1920p 约 6-10px 可见的小点簇）
                var inst = PrefabUtility.InstantiatePrefab(lily) as UnityEngine.GameObject;
                if (inst == null) continue;
                inst.name = "SmallLily_" + i.ToString("00");
                inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0.012f, Mathf.Sin(th) * r);
                inst.transform.localScale = new Vector3(sc, sc, sc);
                inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            }
            var lotus = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lotusPath);
            if (lotus != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    float th = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
                    float r = 1.2f + (float)rng.NextDouble() * 6.0f;
                    float sc = 0.06f + (float)rng.NextDouble() * 0.03f;
                    var inst = PrefabUtility.InstantiatePrefab(lotus) as UnityEngine.GameObject;
                    if (inst == null) continue;
                    inst.name = "SmallLotus_" + i.ToString("00");
                    inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0.012f, Mathf.Sin(th) * r);
                    inst.transform.localScale = new Vector3(sc, sc, sc);
                    inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 沙洲重组完成(小池0.6)：不规则水域 r≈12.3m + 沙洲含水(沙嘴-0.005微露) + 半岛/离岸沙洲 + 石头铺边32块 + 小荷叶重摆24片(5-8cm)。");
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
                mat.SetFloat("_WaveAmpScale", 0.5f);
                // 交互项收敛：低强度涟漪 → 白色大圈减弱（配合演示低频 + shader 泡沫权重）
                mat.SetFloat("_InteractionAmp", 0.22f);
                mat.SetFloat("_InteractionFoam", 0.45f);
                // (振幅 A, 角频率 ω, 陡度 Q, -) —— 微微荡漾：主波加长波长 11.4m，全谱 Σ(Q·ω·A)=0.635<1 无卷浪
                mat.SetVectorArray("_Waves", new Vector4[]
                {
                    new Vector4(0.55f, 0.55f, 0.45f, 0),
                    new Vector4(0.30f, 1.30f, 0.50f, 0),
                    new Vector4(0.12f, 2.60f, 0.55f, 0),
                    new Vector4(0.05f, 4.40f, 0.60f, 0),
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

        [MenuItem("Tools/WaterProject/21 立体沙坑+远景完善(沙丘环湖)")]
        public static void BuildDuneHollowScape()
        {
            System.Random rng = new System.Random(20260921);
            float S(float th) => ShoreR(th) * 0.6f;   // 与菜单20一致的缩小0.6岸线

            // —— 1) 立体沙盘：水边(-0.005) → 坑壁缓升 → 沙脊(+1.25,Perlin) → 背坡回落草地 ——
            string ringPath = "Assets/Art/Meshes/ShoreSandRing.asset";
            {
                int seg = 120, rings = 14;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= rings; gr++)
                {
                    float rr = (float)gr / rings;   // 0=水边, 1=外缘26m
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float rIn = Mathf.Max(7f, S(th) - 0.72f);
                        float r = Mathf.Lerp(rIn, 26f, rr);
                        float y;
                        if (r < 13.5f)
                        {
                            y = -0.005f + Mathf.InverseLerp(rIn, 13.5f, r) * 0.035f;          // 水边缓沙微露
                        }
                        else if (r < 17.5f)
                        {
                            y = 0.03f + Mathf.InverseLerp(13.5f, 17.5f, r) * 1.22f;           // 坑壁：0.03→1.25
                        }
                        else if (r < 21.5f)
                        {
                            y = 1.25f + (Mathf.PerlinNoise(th * 1.3f + 3.0f, 1.7f) - 0.5f) * 0.35f; // 沙脊
                        }
                        else
                        {
                            y = 1.25f + (Mathf.PerlinNoise(th * 1.3f + 3.0f, 1.7f) - 0.5f) * 0.35f
                                - Mathf.InverseLerp(21.5f, 26f, r) * 1.13f;                   // 背坡→草地 0.12
                        }
                        // 全局低频起伏（坑壁以上渐强）
                        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(13f, 18f, r));
                        y += (Mathf.PerlinNoise(Mathf.Cos(th) * 1.8f + 8f, Mathf.Sin(th) * 1.8f + 2f) - 0.5f) * 0.28f * w;
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / seg, rr));
                    }
                }
                for (int gr = 0; gr < rings; gr++)
                    for (int gs = 0; gs < seg; gs++)
                    {
                        int a = gr * (seg + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "ShoreSandRing" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
                if (oldAsset == null) AssetDatabase.CreateAsset(m, ringPath);
                else { EditorUtility.CopySerialized(m, oldAsset); EditorUtility.SetDirty(oldAsset); }
                AssetDatabase.SaveAssets();
                var ring = UnityEngine.GameObject.Find("ShoreSandRing");
                if (ring == null) ring = new UnityEngine.GameObject("ShoreSandRing");
                var rmf = ring.GetComponent<MeshFilter>();
                if (rmf == null) rmf = ring.AddComponent<MeshFilter>();
                var rmr = ring.GetComponent<MeshRenderer>();
                if (rmr == null) rmr = ring.AddComponent<MeshRenderer>();
                rmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
                var sand = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sand_Shore.mat");
                rmr.sharedMaterial = sand;
                ring.transform.position = Vector3.zero;
            }

            // —— 2) 远山（程序生成圆顶山 ×8，沙丘带外一圈，背景层次） ——
            string hillPath = "Assets/Art/Meshes/DistantHill.asset";
            {
                int seg = 40, rings = 7;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                for (int gr = 0; gr <= rings; gr++)
                {
                    float rr = (float)gr / rings;
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float x = Mathf.Cos(th) * rr, z = Mathf.Sin(th) * rr;
                        float y = (1f - rr) * (1f - rr);
                        y += (Mathf.PerlinNoise(x * 1.5f + 5f, z * 1.5f + 9f) - 0.5f) * 0.18f;
                        verts.Add(new Vector3(x, Mathf.Max(0f, y), z));
                        if (gr < rings && gs < seg)
                        {
                            int a = gr * (seg + 1) + gs, b = a + 1;
                            int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                            tris.Add(a); tris.Add(c); tris.Add(b);
                            tris.Add(b); tris.Add(c); tris.Add(d);
                        }
                    }
                }
                var hm = new Mesh { name = "DistantHill" };
                hm.SetVertices(verts); hm.SetTriangles(tris, 0);
                hm.RecalculateNormals(); hm.RecalculateBounds();
                var oldH = AssetDatabase.LoadAssetAtPath<Mesh>(hillPath);
                if (oldH == null) AssetDatabase.CreateAsset(hm, hillPath);
                else { EditorUtility.CopySerialized(hm, oldH); EditorUtility.SetDirty(oldH); }
                AssetDatabase.SaveAssets();

                string hillMatPath = "Assets/Art/Materials/Hill_Distant.mat";
                var hillMat = AssetDatabase.LoadAssetAtPath<Material>(hillMatPath);
                if (hillMat == null)
                {
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit != null) { hillMat = new Material(lit); AssetDatabase.CreateAsset(hillMat, hillMatPath); }
                }
                if (hillMat != null)
                {
                    hillMat.SetColor("_BaseColor", new Color(0.30f, 0.42f, 0.46f, 1f)); // 雾蓝苍绿远山
                    hillMat.SetFloat("_Smoothness", 0.25f);
                    EditorUtility.SetDirty(hillMat);
                }
                var hillMesh = AssetDatabase.LoadAssetAtPath<Mesh>(hillPath);
                var oldHills = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
                foreach (var go in oldHills)
                    if (go != null && go.name.StartsWith("DistantHill_")) UnityEngine.Object.DestroyImmediate(go);
                for (int i = 0; i < 8; i++)
                {
                    float th = i * (Mathf.PI * 2f / 8f) + (float)rng.NextDouble() * 0.5f - 0.25f;
                    float r = 26.5f + (float)rng.NextDouble() * 3.5f;
                    float h = 6f + (float)rng.NextDouble() * 8f;
                    float w = 9f + (float)rng.NextDouble() * 6f;
                    var go = new UnityEngine.GameObject("DistantHill_" + i.ToString("00"));
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = hillMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = hillMat;
                    go.transform.position = new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r);
                    go.transform.localScale = new Vector3(w, h, w);
                    go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                }
            }

            // —— 3) 远树带重建：内圈10(沙脊外缘) + 外圈20(山脚前)，删旧 ——
            var allObjs = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in allObjs)
                if (go != null && (go.name.StartsWith("Trees_") || go.name.StartsWith("Tree_")))
                    UnityEngine.Object.DestroyImmediate(go);
            string treePath = "Assets/Art/StylePacks/正式素材/Trees/Trees_Quaternius.glb";
            var treePrefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(treePath);
            if (treePrefab != null)
            {
                for (int ring = 0; ring < 2; ring++)
                {
                    int n = ring == 0 ? 10 : 20;
                    for (int i = 0; i < n; i++)
                    {
                        float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float r = ring == 0 ? (22f + (float)rng.NextDouble() * 2.5f) : (25f + (float)rng.NextDouble() * 4f);
                        float sc = ring == 0 ? (0.9f + (float)rng.NextDouble() * 0.3f) : (1.2f + (float)rng.NextDouble() * 0.7f);
                        var inst = PrefabUtility.InstantiatePrefab(treePrefab) as UnityEngine.GameObject;
                        if (inst == null) continue;
                        inst.name = "Tree_" + ring + "_" + i.ToString("00");
                        inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r);
                        inst.transform.localScale = new Vector3(sc, sc * (0.9f + (float)rng.NextDouble() * 0.3f), sc);
                        inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                    }
                }
            }

            // —— 4) 主相机微调：略高远俯视（看到沙丘环绕的水洼全景） ——
            var cam = UnityEngine.GameObject.Find("Main Camera");
            if (cam != null)
            {
                cam.transform.localPosition = new Vector3(0f, 2.2f, -17f);
                cam.transform.localRotation = Quaternion.Euler(-4f, 0f, 0f);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 立体沙坑完成：水边缓沙→坑壁1.25m→沙脊→背坡回落；远山8座+树带重建(10+20)+相机(0,2.2,-17)。");
        }
    }
}
#endif