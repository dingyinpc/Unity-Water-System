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
                else { WriteMeshFrom(oldAsset, m); }
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
                else { WriteMeshFrom(oldAsset, m); }
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
                else { WriteMeshFrom(oldAsset, m); }
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
                else { WriteMeshFrom(oldAsset, m); }
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
                else { WriteMeshFrom(oldAsset, m); }
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
                else { WriteMeshFrom(oldAsset, m); }
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
                else { WriteMeshFrom(oldAsset, m); }
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
                            // 镜像绕序：闭合圆顶需要朝外法线，否则 avgNrmY 为负、远山渲染成黑锥
                            tris.Add(a); tris.Add(b); tris.Add(c);
                            tris.Add(b); tris.Add(d); tris.Add(c);
                        }
                    }
                }
                var hm = new Mesh { name = "DistantHill" };
                hm.SetVertices(verts); hm.SetTriangles(tris, 0);
                hm.RecalculateNormals(); hm.RecalculateBounds();
                var oldH = AssetDatabase.LoadAssetAtPath<Mesh>(hillPath);
                if (oldH == null) AssetDatabase.CreateAsset(hm, hillPath);
                else { WriteMeshFrom(oldH, hm); }
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
                        var tPos = new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r);
                        inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                        inst.transform.localScale = new Vector3(sc, sc * (0.9f + (float)rng.NextDouble() * 0.3f), sc);
                        if (inst.transform.childCount > 0)
                        {
                            var keep = inst.transform.GetChild(rng.Next(inst.transform.childCount));
                            for (int c = inst.transform.childCount - 1; c >= 0; c--)
                            {
                                var ch = inst.transform.GetChild(c);
                                if (ch != keep) UnityEngine.Object.DestroyImmediate(ch.gameObject);
                            }
                            inst.transform.position = Vector3.zero;
                            inst.transform.position = tPos - keep.position;
                        }
                        else inst.transform.position = tPos;
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
    [MenuItem("Tools/WaterProject/22 对标视频修正(低机位/绿墙远岸/灰青水面/稀疏石/近滩)")]
        public static void AlignToVideo()
        {
            KillVolumes();
            System.Random rng = new System.Random(20260922);
            float S(float th) => ShoreR(th) * 0.6f;

            // —— 1) 主相机贴近水面（前景水面占大半，对标视频低机位） ——
            var cam = UnityEngine.GameObject.Find("Main Camera");
            if (cam != null)
            {
                cam.transform.localPosition = new Vector3(0f, 1.35f, -14.6f);
                cam.transform.localRotation = Quaternion.Euler(-2.5f, 0f, 0f);
            }
            // 太阳转前侧斜照：前方沙坡顺光亮起（避免背光阴面）
            var sun = UnityEngine.GameObject.Find("Sun") ?? UnityEngine.GameObject.Find("Directional Light");
            if (sun != null) sun.transform.rotation = Quaternion.Euler(60f, 25f, 0f);

            // —— 1b) 沙盘重生成（前侧 +z 挖缺口：相机视线越过矮坡看水面+绿墙+远山） ——
            string ringPath = "Assets/Art/Meshes/ShoreSandRing.asset";
            {
                float gapK(float th) => 1f - 0.75f * Mathf.SmoothStep(0.3f, 0.92f, Mathf.Cos(th));
                int seg2 = 120, rings2 = 14;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= rings2; gr++)
                {
                    float rr = (float)gr / rings2;
                    for (int gs = 0; gs <= seg2; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg2;
                        float rIn = Mathf.Max(7f, S(th) - 0.72f);
                        float r = Mathf.Lerp(rIn, 26f, rr);
                        float y;
                        if (r < 13.5f)
                        {
                            y = -0.005f + Mathf.InverseLerp(rIn, 13.5f, r) * 0.035f;
                        }
                        else if (r < 17.5f)
                        {
                            y = 0.03f + Mathf.InverseLerp(13.5f, 17.5f, r) * 1.22f;
                        }
                        else if (r < 21.5f)
                        {
                            y = 1.25f + (Mathf.PerlinNoise(th * 1.3f + 3.0f, 1.7f) - 0.5f) * 0.35f;
                        }
                        else
                        {
                            y = 1.25f + (Mathf.PerlinNoise(th * 1.3f + 3.0f, 1.7f) - 0.5f) * 0.35f
                                - Mathf.InverseLerp(21.5f, 26f, r) * 1.13f;
                        }
                        if (r >= 13.5f) y *= gapK(th);            // 前侧缺口压低 75%
                        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(13f, 18f, r));
                        y += (Mathf.PerlinNoise(Mathf.Cos(th) * 1.8f + 8f, Mathf.Sin(th) * 1.8f + 2f) - 0.5f) * 0.28f * w;
                        if (r >= 13.5f) y *= (1f - 0.45f * Mathf.SmoothStep(0.3f, 0.92f, Mathf.Cos(th))); // 缺口侧低频起伏同缩
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / seg2, rr));
                    }
                }
                for (int gr = 0; gr < rings2; gr++)
                    for (int gs = 0; gs < seg2; gs++)
                    {
                        int a = gr * (seg2 + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (seg2 + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "ShoreSandRing" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldA = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
                if (oldA == null) AssetDatabase.CreateAsset(m, ringPath);
                else { WriteMeshFrom(oldA, m); }
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

            // —— 1d) 水域向相机侧(+z)凸出 2m：水淹缺口坡 → 画面底部成浅水（透橄榄池底暗绿灰） ——
            string waterPath2 = "Assets/Art/Meshes/LakeWaterIrregular.asset";
            {
                int segW = 120;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                int ringC = 26;
                for (int gr = 0; gr <= ringC; gr++)
                {
                    float rr = (float)gr / ringC;
                    for (int gs = 0; gs <= segW; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / segW;
                        float rBase = Mathf.Max(12f, 20.5f * (0.88f + 0.12f * Mathf.Sin(th * 2f + 1.3f)
                                  + 0.09f * Mathf.Sin(th * 4f + 0.6f) + 0.07f * Mathf.Sin(th * 7f + 2.4f))) * 0.6f;
                        // 半径必须随圈插值，否则 27 圈重合在同一岸线上 → 所有 quad 面积为 0（只剩轮廓）
                        float rEdge = rBase + 2.0f * Mathf.SmoothStep(0.55f, 1.0f, Mathf.Cos(th)); // +z 相机侧凸出 2m
                        float r = Mathf.Lerp(0.4f, rEdge, rr);
                        verts.Add(new Vector3(Mathf.Cos(th) * r, 0.02f, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / segW, rr));
                    }
                }
                for (int gr = 0; gr < ringC; gr++)
                    for (int gs = 0; gs < segW; gs++)
                    {
                        int a = gr * (segW + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (segW + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "LakeWaterIrregular" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldW = AssetDatabase.LoadAssetAtPath<Mesh>(waterPath2);
                if (oldW == null) AssetDatabase.CreateAsset(m, waterPath2);
                else { WriteMeshFrom(oldW, m); }
                AssetDatabase.SaveAssets();
                var wgo = UnityEngine.GameObject.Find("Water");
                if (wgo != null)
                {
                    var wfilter = wgo.GetComponent<MeshFilter>();
                    if (wfilter != null) wfilter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(waterPath2);
                    wgo.transform.position = Vector3.zero;
                    EditorUtility.SetDirty(wgo);
                }
            }

            // —— 1c) 池底重生成：缓坡外推（前景 r13 水深≈0.5m，消除透沙爆亮，反射主导暗青灰） ——
            string floorPath2 = "Assets/Art/Meshes/PondFloorShore.asset";
            {
                int segF = 120, ringsF = 30;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= ringsF; gr++)
                {
                    float rr = (float)gr / ringsF;
                    for (int gs = 0; gs <= segF; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / segF;
                        float r = Mathf.Lerp(0.4f, S(th) + 0.72f, rr);
                        float shore = Mathf.SmoothStep(6f, 10.5f, r);     // 经典缓坡（rc7 验证值）
                        float y = Mathf.Lerp(-1.03f, -0.10f, shore);
                        float bump = (Mathf.PerlinNoise(r * 0.22f * Mathf.Cos(th) + 41.7f, r * 0.22f * Mathf.Sin(th) + 13.3f) - 0.5f) * 0.22f;
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y + bump, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / segF, rr));
                    }
                }
                for (int gr = 0; gr < ringsF; gr++)
                    for (int gs = 0; gs < segF; gs++)
                    {
                        int a = gr * (segF + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (segF + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "PondFloorShore" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldA = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath2);
                if (oldA == null) AssetDatabase.CreateAsset(m, floorPath2);
                else { WriteMeshFrom(oldA, m); }
                AssetDatabase.SaveAssets();
                var floor = UnityEngine.GameObject.Find("PondFloor");
                if (floor != null)
                {
                    var fmf = floor.GetComponent<MeshFilter>();
                    if (fmf != null) fmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath2);
                    floor.transform.position = new Vector3(0, 0, 0);
                    EditorUtility.SetDirty(floor);
                }
            }

            // —— 2) 远岸绿墙：连续厚实树冠带（视频=无间隙绿墙） ——
            string wallPath = "Assets/Art/Meshes/GreenWall.asset";
            {
                int seg = 96;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                // 3 环：内壁(24.5 0->4.5+起伏)，顶(24.5->27 at 4.5+起伏)，外壁(27 4.5->0)
                float Top(float th) => 3.6f + (Mathf.PerlinNoise(th * 1.7f + 3f, 11f) - 0.5f) * 0.8f;
                for (int gs = 0; gs <= seg; gs++)
                {
                    float th = Mathf.PI * 2f * gs / seg;
                    float ct = Mathf.Cos(th), st = Mathf.Sin(th);
                    verts.Add(new Vector3(ct * 24.5f, 0f, st * 24.5f));
                    verts.Add(new Vector3(ct * 24.5f, Top(th), st * 24.5f));
                    verts.Add(new Vector3(ct * 24.5f, Top(th), st * 24.5f));
                    verts.Add(new Vector3(ct * 27f, Top(th), st * 27f));
                    verts.Add(new Vector3(ct * 27f, Top(th), st * 27f));
                    verts.Add(new Vector3(ct * 27f, 0f, st * 27f));
                    uvs.Add(new Vector2((float)gs / seg, 0f));
                    uvs.Add(new Vector2((float)gs / seg, 1f));
                    uvs.Add(new Vector2((float)gs / seg, 1f));
                    uvs.Add(new Vector2((float)gs / seg, 1f));
                    uvs.Add(new Vector2((float)gs / seg, 1f));
                    uvs.Add(new Vector2((float)gs / seg, 0f));
                    if (gs < seg)
                    {
                        int a = gs * 6;
                        for (int k = 0; k < 2; k++)
                        {
                            int i0 = a + k * 3, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3 + (k == 0 ? 0 : -2);
                            tris.Add(i0); tris.Add(i1); tris.Add(i2);
                            tris.Add(i0); tris.Add(i2); tris.Add(i3);
                        }
                    }
                }
                var m = new Mesh { name = "GreenWall" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                // 该网格有重合顶点→退化三角→RecalculateNormals 留下零法线（实测 294/582），故显式给外向法线
                var gn = new List<Vector3>(verts.Count);
                for (int i = 0; i < verts.Count; i++)
                {
                    var dir = new Vector3(verts[i].x, 0f, verts[i].z);
                    dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.right;
                    gn.Add((dir * 0.55f + Vector3.up * 0.835f).normalized);
                }
                m.SetNormals(gn);
                m.RecalculateBounds();
                var oldG = AssetDatabase.LoadAssetAtPath<Mesh>(wallPath);
                if (oldG == null) AssetDatabase.CreateAsset(m, wallPath);
                else { WriteMeshFrom(oldG, m); }
                AssetDatabase.SaveAssets();
                string greenMatPath = "Assets/Art/Materials/GreenWall_Distant.mat";
                var greenMat = AssetDatabase.LoadAssetAtPath<Material>(greenMatPath);
                if (greenMat == null)
                {
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit != null) { greenMat = new Material(lit); AssetDatabase.CreateAsset(greenMat, greenMatPath); }
                }
                if (greenMat != null)
                {
                    greenMat.SetColor("_BaseColor", new Color(0.13f, 0.24f, 0.15f, 1f));   // 厚实深绿
                    greenMat.SetFloat("_Smoothness", 0.3f);
                    EditorUtility.SetDirty(greenMat);
                }
                var wall = UnityEngine.GameObject.Find("GreenWall");
                if (wall == null) wall = new UnityEngine.GameObject("GreenWall");
                var wmf = wall.GetComponent<MeshFilter>();
                if (wmf == null) wmf = wall.AddComponent<MeshFilter>();
                var wmr = wall.GetComponent<MeshRenderer>();
                if (wmr == null) wmr = wall.AddComponent<MeshRenderer>();
                wmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(wallPath);
                wmr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(greenMatPath);
                wall.transform.position = Vector3.zero;
            }

            // —— 3) 水面色调朝亮蓝（用户要求：水必须是蓝色的，别再灰青） ——
            var wmat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (wmat != null)
            {
                wmat.SetColor("_ShallowColor", new Color(0.05f, 0.35f, 1.00f, 1f));   // 亮蓝（用户要求水是蓝色的）
                wmat.SetColor("_DeepColor", new Color(0.04f, 0.20f, 0.70f, 1f));
                wmat.SetColor("_WaterScatter", new Color(0.10f, 0.50f, 1.00f, 1f));
                wmat.SetFloat("_ScatterStrength", 0.65f);
                wmat.SetFloat("_SpecStrength", 0.38f);   // 波光冷白（不稀释蓝色）
                wmat.SetFloat("_SunStrength", 0.28f);
                wmat.SetFloat("_WaveAmpScale", 0.65f);   // 波浪幅度回升（波动明显）
                wmat.SetColor("_SkyZenith", new Color(0.12f, 0.55f, 1.00f, 1f));       // 反射天顶蓝
                wmat.SetColor("_SkyHorizon", new Color(0.44f, 0.70f, 0.96f, 1f));      // 反射地平蓝
                wmat.SetColor("_SunColor", new Color(0.92f, 0.97f, 1.00f, 1f));        // 高光冷白
                EditorUtility.SetDirty(wmat);

                // Water 渲染器强制挂载最新 Water_Advanced 资产（消除实例快照漂移）
                var waterGo = UnityEngine.GameObject.Find("Water");
                if (waterGo != null)
                {
                    var wmr = waterGo.GetComponent<MeshRenderer>();
                    if (wmr != null) wmr.sharedMaterial = wmat;
                    EditorUtility.SetDirty(waterGo);
                }

                // 全局亮化：水要显蓝（去灰雾暗调，天空提亮偏白）
                var sunL = sun != null ? sun.GetComponent<Light>() : null;
                if (sunL != null) { sunL.intensity = 1.0f; EditorUtility.SetDirty(sun); }
                RenderSettings.ambientIntensity = 1.0f;
                if (RenderSettings.skybox != null)
                {
                    RenderSettings.skybox.SetFloat("_Exposure", 0.9f);
                    RenderSettings.skybox.SetColor("_Tint", new Color(0.96f, 0.97f, 1.0f, 1f));   // 天更白
                }
                var sandMat2 = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sand_Shore.mat");
                if (sandMat2 != null)
                {
                    sandMat2.SetColor("_BaseColor", new Color(0.44f, 0.46f, 0.38f, 1f));   // 沙橄榄绿（近岸偏绿观感）
                    EditorUtility.SetDirty(sandMat2);
                }
                var groundLake = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ground_Lake.mat");
                if (groundLake != null)
                {
                    groundLake.SetColor("_BaseColor", new Color(0.34f, 0.42f, 0.36f, 1f));  // 池底橄榄灰绿（浅水透出暗绿，根治近岸暖黄）
                    groundLake.SetColor("_Color", new Color(0.34f, 0.42f, 0.36f, 1f));
                    EditorUtility.SetDirty(groundLake);
                }
            }

            // —— 4) 石头稀疏化：删 32 规整铺边 → 14 块自然散置（3簇+水中2+散4） ——
            var allGo = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in allGo)
                if (go != null && go.name.StartsWith("EdgeStone_")) UnityEngine.Object.DestroyImmediate(go);
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Stone.mat");
            var stones = new[] {
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_01.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_02.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_03.asset") };
            for (int i = 0; i < 14; i++)
            {
                float th, r;
                if (i < 9)   // 3 簇（前侧/右前/左），每簇3块
                {
                    float[] cl = { -1.35f, 2.0f, 0.7f };
                    int ci = i / 3;
                    th = cl[ci] + (float)rng.NextDouble() * 0.5f - 0.25f;
                    r = S(th) - 0.3f - (float)rng.NextDouble() * 1.1f;
                }
                else if (i < 11) // 水中微露
                {
                    th = i == 9 ? 0.2f : 2.6f;
                    r = 8.5f + (float)rng.NextDouble() * 1.5f;
                }
                else
                {
                    th = (float)rng.NextDouble() * Mathf.PI * 2f;
                    r = S(th) - 1.2f - (float)rng.NextDouble() * 0.8f;
                }
                float sc = 0.42f + (float)rng.NextDouble() * 0.45f;
                var go = new UnityEngine.GameObject("EdgeStone_" + i.ToString("00"));
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = stones[rng.Next(stones.Length)];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = stoneMat;
                go.transform.position = new Vector3(Mathf.Cos(th) * r, 0.004f + (float)rng.NextDouble() * 0.03f, Mathf.Sin(th) * r);
                go.transform.localScale = new Vector3(sc, sc * 0.8f, sc);
                go.transform.rotation = Quaternion.Euler(rng.Next(0, 20), rng.Next(0, 360), rng.Next(-16, 16));
            }

            // —— 5) 相机侧近岸透沙浅滩：1 块小扇面（前景"浅水见沙"，先删旧） ——
            var oldShoals = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in oldShoals)
                if (go != null && go.name.StartsWith("ForeShoal_")) UnityEngine.Object.DestroyImmediate(go);
            var sandMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sand_Shore.mat");
            {
                var disk = UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                UnityEngine.Object.DestroyImmediate(disk.GetComponent<Collider>());
                disk.name = "ForeShoal_0";
                float w = 3.2f;                                     // 缩小并移至画面右侧边缘（避免前景过曝）
                disk.transform.position = new Vector3(-4.2f, 0.004f, 12.9f);
                disk.transform.localScale = new Vector3(w, 0.025f, w * 0.6f);
                disk.transform.rotation = Quaternion.Euler(0, 80f, 0);
                var dmr = disk.GetComponent<MeshRenderer>();
                dmr.sharedMaterial = sandMat;
            }

            // —— 6) 荷叶减稀：重建 12 片 + 3 花 ——
            var allL = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in allL)
                if (go != null && (go.name.StartsWith("SmallLily_") || go.name.StartsWith("SmallLotus_")))
                    UnityEngine.Object.DestroyImmediate(go);
            string lilyPath = "Assets/Art/StylePacks/正式素材/Lotus/WaterLilies_GooglePoly.glb";
            var lily = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lilyPath);
            if (lily != null)
            {
                for (int i = 0; i < 12; i++)
                {
                    float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = 0.8f + (float)rng.NextDouble() * 6.5f;
                    float sc = 0.05f + (float)rng.NextDouble() * 0.03f;
                    var inst = PrefabUtility.InstantiatePrefab(lily) as UnityEngine.GameObject;
                    if (inst == null) continue;
                    inst.name = "SmallLily_" + i.ToString("00");
                    inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0.012f, Mathf.Sin(th) * r);
                    inst.transform.localScale = new Vector3(sc, sc, sc);
                    inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                }
            }
            string lotusPath = "Assets/Art/StylePacks/正式素材/Lotus/LotusFlower_SeanTarrant.glb";
            var lotus = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lotusPath);
            if (lotus != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = 2f + (float)rng.NextDouble() * 5f;
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
            UnityEngine.Debug.Log("[WaterProject] 对标视频完成：低机位(0,1.35,-14.6)/绿墙远岸/水面灰青/石头14散置/近滩/荷叶12。");
        }

        // —— 23) 强制重挂场景全部材质（锚定资产最新值，根治实例缓存漂移） ——
        [MenuItem("Tools/WaterProject/23 强制重挂场景全部材质")]
        public static void RebindAllMaterials()
        {
            int n = 0;
            var all = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in all)
            {
                var mrs = go.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var mr in mrs)
                {
                    var mats = mr.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        var asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/" + m.name + ".mat");
                        if (asset != null && !ReferenceEquals(asset, m)) { mats[i] = asset; changed = true; n++; }
                    }
                    if (changed) mr.sharedMaterials = mats;
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 材质重挂 " + n + " 处。");
        }
    // —— 24) 浅碗沙滩重构：水区微凹碗状装水、无水沙滩大面积平铺扩散、绕水平地环树、水中3堆鹅卵石 ——
        [MenuItem("Tools/WaterProject/24 浅碗沙滩重构(碗形水域/环树/水中卵石3堆)")]
        public static void RebuildShallowBowlBeach()
        {
            KillVolumes();
            System.Random rng = new System.Random(20260924);
            float S(float th) => ShoreR(th) * 0.6f;

            // 1) 相机：碗外沙滩带低机位（近处沙滩+前方水面中带+远景树墙），水面占画面中部主导
            var cam = UnityEngine.GameObject.Find("Main Camera");
            if (cam != null)
            {
                cam.transform.localPosition = new Vector3(0f, 1.2f, -16.5f);
                cam.transform.localRotation = Quaternion.Euler(-2.5f, 0f, 0f);
            }
            // 1a) 全局亮化：水要显蓝（去灰雾暗调，太阳足、天空白）
            var sun = UnityEngine.GameObject.Find("Sun") ?? UnityEngine.GameObject.Find("Directional Light");
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(60f, 25f, 0f);
                var sunL = sun.GetComponent<Light>();
                if (sunL != null) { sunL.intensity = 1.0f; EditorUtility.SetDirty(sun); }
            }
            RenderSettings.ambientIntensity = 1.0f;
            if (RenderSettings.skybox != null)
            {
                RenderSettings.skybox.SetFloat("_Exposure", 0.9f);
                RenderSettings.skybox.SetColor("_Tint", new Color(0.96f, 0.97f, 1.0f, 1f));
            }

            // 2) 池底：浅碗（中心 -0.55m → 岸边 0.02 微凹，Perlin 缓起伏）
            string floorPath = "Assets/Art/Meshes/PondFloorShore.asset";
            {
                int segF = 120, ringsF = 24;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= ringsF; gr++)
                {
                    float rr = (float)gr / ringsF;
                    for (int gs = 0; gs <= segF; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / segF;
                        float rIn2 = Mathf.Max(12f, S(th));
                        float r = Mathf.Lerp(0.4f, rIn2 + 0.5f, rr);
                        // 碗形交给 PondWall 的 45° 沙滩斜坡去表达；池底做平底盆，保证 1.02m 净空容纳波谷
                        float y = -1.0f;
                        float bump = (Mathf.PerlinNoise(r * 0.28f * Mathf.Cos(th) + 41.7f, r * 0.28f * Mathf.Sin(th) + 13.3f) - 0.5f) * 0.08f;
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y + bump, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / segF, rr));
                    }
                }
                for (int gr = 0; gr < ringsF; gr++)
                    for (int gs = 0; gs < segF; gs++)
                    {
                        int a = gr * (segF + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (segF + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var oldA = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath);
                if (oldA == null)
                {
                    var m = new Mesh { name = "PondFloorShore" };
                    m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                    m.RecalculateNormals(); m.RecalculateBounds();
                    AssetDatabase.CreateAsset(m, floorPath);
                }
                else
                {
                    oldA.Clear();
                    oldA.vertices = verts.ToArray();
                    oldA.uv = uvs.ToArray();
                    oldA.triangles = tris.ToArray();
                    oldA.RecalculateNormals();
                    oldA.RecalculateBounds();
                    EditorUtility.SetDirty(oldA);
                }
                AssetDatabase.SaveAssets();
                var floor = UnityEngine.GameObject.Find("PondFloor");
                if (floor == null) floor = new UnityEngine.GameObject("PondFloor");
                var fmf = floor.GetComponent<MeshFilter>();
                if (fmf == null) fmf = floor.AddComponent<MeshFilter>();
                var fmr = floor.GetComponent<MeshRenderer>();
                if (fmr == null) fmr = floor.AddComponent<MeshRenderer>();
                fmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(floorPath);
                fmr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ground_Lake.mat");
                floor.transform.position = new Vector3(0, 0, 0);
                floor.transform.localScale = Vector3.one;   // 菜单12 遗留 (40,0.05,40) 会把浅碗压成 2.7cm 深
            }

            // 2b) PondWall：实心圆柱 → 45° 凹沙滩斜坡环（它的职责是"沙滩入水"，不是堵一块平板）
            string slopePath = "Assets/Art/Meshes/BeachSlope.asset";
            {
                int segS = 120, ringsS = 8;
                float run = 1.10f, drop = 1.02f;          // ≈42.8°，视觉即 45° 入水坡
                var sv = new List<Vector3>();
                var su = new List<Vector2>();
                var st = new List<int>();
                for (int gr = 0; gr <= ringsS; gr++)
                {
                    float u = (float)gr / ringsS;
                    for (int gs = 0; gs <= segS; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / segS;
                        float rIn2 = Mathf.Max(12f, S(th));
                        float r = (rIn2 - run) + u * run;
                        float y = -1.0f + u * drop;
                        float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 4f);
                        y += (Mathf.PerlinNoise(Mathf.Cos(th) * 2.2f + 7f, Mathf.Sin(th) * 2.2f + 3f) - 0.5f) * 0.06f * fade;
                        sv.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                        su.Add(new Vector2((float)gs / segS, u));
                    }
                }
                for (int gr = 0; gr < ringsS; gr++)
                    for (int gs = 0; gs < segS; gs++)
                    {
                        int a = gr * (segS + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (segS + 1) + gs, d = c + 1;
                        st.Add(a); st.Add(c); st.Add(b);
                        st.Add(b); st.Add(c); st.Add(d);
                    }
                var slopeNormals = new List<Vector3>(sv.Count);
                for (int i = 0; i < sv.Count; i++)
                {
                    var rd = new Vector3(sv[i].x, 0f, sv[i].z);
                    rd = rd.sqrMagnitude > 1e-8f ? rd.normalized : Vector3.right;
                    slopeNormals.Add((rd * 0.60f + Vector3.up * 0.80f).normalized);
                }
                var slopeAsset = AssetDatabase.LoadAssetAtPath<Mesh>(slopePath);
                if (slopeAsset == null)
                {
                    slopeAsset = new Mesh { name = "BeachSlope" };
                    slopeAsset.SetVertices(sv); slopeAsset.SetUVs(0, su); slopeAsset.SetTriangles(st, 0);
                    slopeAsset.SetNormals(slopeNormals);
                    slopeAsset.RecalculateBounds();
                    AssetDatabase.CreateAsset(slopeAsset, slopePath);
                }
                else
                {
                    slopeAsset.Clear();
                    slopeAsset.vertices = sv.ToArray();
                    slopeAsset.uv = su.ToArray();
                    slopeAsset.triangles = st.ToArray();
                    slopeAsset.SetNormals(slopeNormals);
                    slopeAsset.RecalculateBounds();
                    EditorUtility.SetDirty(slopeAsset);
                }
                AssetDatabase.SaveAssets();

                var wall = UnityEngine.GameObject.Find("PondWall");
                if (wall == null) wall = new UnityEngine.GameObject("PondWall");
                var wmf2 = wall.GetComponent<MeshFilter>();
                if (wmf2 == null) wmf2 = wall.AddComponent<MeshFilter>();
                var wmr2 = wall.GetComponent<MeshRenderer>();
                if (wmr2 == null) wmr2 = wall.AddComponent<MeshRenderer>();
                wmf2.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(slopePath);
                // 这条环带的角色是"入水沙滩"，用沙滩材质；PondWall.mat 只作兜底
                var wmat2 = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sand_Shore.mat");
                if (wmat2 == null) wmat2 = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/PondWall.mat");
                if (wmat2 != null) wmr2.sharedMaterial = wmat2;
                wall.transform.position = Vector3.zero;
                wall.transform.localScale = Vector3.one;   // 清掉菜单12 的 (24,0.5,24) 圆柱缩放
                var wcol2 = wall.GetComponent<Collider>();
                if (wcol2 != null) UnityEngine.Object.DestroyImmediate(wcol2);
                EditorUtility.SetDirty(wall);
            }

            // 3) 沙滩：平缓浅碗外扩（水边 0.02 → 外缘 0.37 慢坡，无墙无脊，大面积平铺）
            string ringPath = "Assets/Art/Meshes/ShoreSandRing.asset";
            {
                int segR = 120, ringsR = 16;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int gr = 0; gr <= ringsR; gr++)
                {
                    float rr = (float)gr / ringsR;
                    for (int gs = 0; gs <= segR; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / segR;
                        float rIn2 = Mathf.Max(12f, S(th));
                        float r = Mathf.Lerp(rIn2, 27.5f, rr);
                        float y;
                        if (r < 16f)      y = 0.02f + Mathf.InverseLerp(rIn2, 16f, r) * 0.10f;
                        else if (r < 22f) y = 0.12f + Mathf.InverseLerp(16f, 22f, r) * 0.10f;
                        else              y = 0.22f + Mathf.InverseLerp(22f, 27.5f, r) * 0.15f;
                        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rIn2 + 1f, 24f, r));
                        y += (Mathf.PerlinNoise(Mathf.Cos(th) * 1.6f + 8f, Mathf.Sin(th) * 1.6f + 2f) - 0.5f) * 0.10f * w;
                        verts.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                        uvs.Add(new Vector2((float)gs / segR, rr));
                    }
                }
                for (int gr = 0; gr < ringsR; gr++)
                    for (int gs = 0; gs < segR; gs++)
                    {
                        int a = gr * (segR + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (segR + 1) + gs, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                var m = new Mesh { name = "ShoreSandRing" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                var oldA = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
                if (oldA == null) AssetDatabase.CreateAsset(m, ringPath);
                else { WriteMeshFrom(oldA, m); }
                AssetDatabase.SaveAssets();
                var ring = UnityEngine.GameObject.Find("ShoreSandRing");
                if (ring == null) ring = new UnityEngine.GameObject("ShoreSandRing");
                var rmf = ring.GetComponent<MeshFilter>();
                if (rmf == null) rmf = ring.AddComponent<MeshFilter>();
                var rmr = ring.GetComponent<MeshRenderer>();
                if (rmr == null) rmr = ring.AddComponent<MeshRenderer>();
                rmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ringPath);
                rmr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Sand_Shore.mat");
                ring.transform.position = Vector3.zero;
            }

            // 4) 树：以水为中心，沙滩平地环状 2 圈（r19.5-21.7 + r23.6-25.8），随机抖动不过密。预留 r15-19.5 宽沙滩机位带。
            string treePrefab = "Assets/Art/StylePacks/正式素材/Trees/Trees_Quaternius.glb";
            var trees = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(treePrefab);
            var allGo1 = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in allGo1)
                if (go != null && (go.name.StartsWith("Tree_ring_") || go.name.StartsWith("Trees_") || go.name.StartsWith("Tree_")))
                    UnityEngine.Object.DestroyImmediate(go);
            if (trees != null)
            {
                int idx = 0;
                for (int ringI = 0; ringI < 2; ringI++)
                {
                    int count = ringI == 0 ? 12 : 14;
                    float r0 = ringI == 0 ? 19.5f : 23.6f;
                    for (int i = 0; i < count; i++)
                    {
                        float th = (float)i / count * Mathf.PI * 2f + (float)rng.NextDouble() * 0.35f;
                        float r = r0 + (float)rng.NextDouble() * 2.2f;
                        float sc = ringI == 0 ? 1.0f + (float)rng.NextDouble() * 0.4f
                                             : 1.2f + (float)rng.NextDouble() * 0.5f;
                        var inst = PrefabUtility.InstantiatePrefab(trees) as UnityEngine.GameObject;
                        if (inst == null) continue;
                        inst.name = "Tree_ring_" + idx.ToString("00");
                        var target = new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r);
                        inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                        inst.transform.localScale = new Vector3(sc, sc, sc);
                        // 整包内含 5 棵相距上百米的树；只留一棵，否则其余会被甩到地平线
                        if (inst.transform.childCount > 0)
                        {
                            var keep = inst.transform.GetChild(rng.Next(inst.transform.childCount));
                            for (int c = inst.transform.childCount - 1; c >= 0; c--)
                            {
                                var ch = inst.transform.GetChild(c);
                                if (ch != keep) UnityEngine.Object.DestroyImmediate(ch.gameObject);
                            }
                            inst.transform.position = Vector3.zero;
                            inst.transform.position = target - keep.position;
                        }
                        else inst.transform.position = target;
                        idx++;
                    }
                }
            }

            // 5) 鹅卵石：水中 3 堆不规则散布（r3-10，簇内相挤，微露水面）
            var allGo2 = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in allGo2)
                if (go != null && (go.name.StartsWith("EdgeStone_") || go.name.StartsWith("Pebble_") || go.name.StartsWith("ForeShoal_")))
                    UnityEngine.Object.DestroyImmediate(go);
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Stone.mat");
            var stones = new[] {
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_01.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_02.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Stone_03.asset") };
            float[][] piles = {
                new float[] { 0.6f, 3.2f },         // 堆A 中心(θ,r)
                new float[] { 2.5f, 6.4f },         // 堆B
                new float[] { 4.7f, 9.6f } };       // 堆C
            for (int p = 0; p < piles.Length; p++)
            {
                int count = 4;                        // 每堆4块，共12块
                for (int i = 0; i < count; i++)
                {
                    float cth = piles[p][0], cr = piles[p][1];
                    float th = cth + ((float)rng.NextDouble() - 0.5f) * 0.5f;
                    float r = cr + ((float)rng.NextDouble() - 0.5f) * 0.9f;
                    float sc = 0.40f + (float)rng.NextDouble() * 0.35f;
                    var go = new UnityEngine.GameObject("Pebble_P" + p + "_" + i.ToString("00"));
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = stones[rng.Next(stones.Length)];
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = stoneMat;
                    go.transform.position = new Vector3(Mathf.Cos(th) * r, 0.012f + (float)rng.NextDouble() * 0.02f, Mathf.Sin(th) * r);
                    go.transform.localScale = new Vector3(sc, sc * 0.72f, sc);
                    go.transform.rotation = Quaternion.Euler(rng.Next(0, 18), rng.Next(0, 360), rng.Next(-14, 14));
                }
            }

            // 6) 荷花：水面漂浮（12 片 + 3 花，分散水中间 r1.5-11）
            var allL = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in allL)
                if (go != null && (go.name.StartsWith("SmallLily_") || go.name.StartsWith("SmallLotus_")))
                    UnityEngine.Object.DestroyImmediate(go);
            string lilyPath = "Assets/Art/StylePacks/正式素材/Lotus/WaterLilies_GooglePoly.glb";
            var lily = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lilyPath);
            if (lily != null)
            {
                for (int i = 0; i < 12; i++)
                {
                    float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = 1.5f + (float)rng.NextDouble() * 9.5f;
                    float sc = 0.09f + (float)rng.NextDouble() * 0.05f;   // 俯瞰可辨档（9-14cm）
                    var inst = PrefabUtility.InstantiatePrefab(lily) as UnityEngine.GameObject;
                    if (inst == null) continue;
                    inst.name = "SmallLily_" + i.ToString("00");
                    inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0.022f, Mathf.Sin(th) * r);
                    inst.transform.localScale = new Vector3(sc, sc, sc);
                    inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                }
            }
            string lotusPath = "Assets/Art/StylePacks/正式素材/Lotus/LotusFlower_SeanTarrant.glb";
            var lotus = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lotusPath);
            if (lotus != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = 2.5f + (float)rng.NextDouble() * 7f;
                    float sc = 0.10f + (float)rng.NextDouble() * 0.04f;   // 俯瞰可辨档
                    var inst = PrefabUtility.InstantiatePrefab(lotus) as UnityEngine.GameObject;
                    if (inst == null) continue;
                    inst.name = "SmallLotus_" + i.ToString("00");
                    inst.transform.position = new Vector3(Mathf.Cos(th) * r, 0.022f, Mathf.Sin(th) * r);
                    inst.transform.localScale = new Vector3(sc, sc, sc);
                    inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] 浅碗沙滩完成：碗形水域/平滩外扩27.5m/环树26株/水中卵石3堆/荷12花3。");
        }
    // —— 25) 诊断：恢复 Water + 红色立方体标记（验证桥 Game 截图渲染的是否为当前实时场景） ——
        [MenuItem("Tools/WaterProject/25 诊断恢复Water+红盒标记")]
        public static void DiagRestoreWaterAndRedBox()
        {
            // 1) 恢复 Water 对象：激话 + 归位 + 强制挂 URP/Lit 纯蓝（绕开自定义 shader）
            var w = UnityEngine.GameObject.Find("Water");
            if (w != null)
            {
                w.SetActive(true);
                w.transform.position = new Vector3(0f, 0.02f, 0f);
                var wm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
                var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (lit != null)
                {
                    lit.name = "_DiagBlue";
                    lit.SetColor("_BaseColor", new Color(0.05f, 0.35f, 1.0f, 1f));
                    var wmr = w.GetComponent<MeshRenderer>();
                    if (wmr != null) wmr.sharedMaterial = lit;
                }
                UnityEngine.Debug.Log("[WaterProject] DIAG Water restored + URP/Lit pure blue attached.");
            }
            else UnityEngine.Debug.Log("[WaterProject] DIAG Water NOT FOUND!");

            // 2) 红盒子放相机正前方（入镜即可见）
            var box = UnityEngine.GameObject.Find("_DiagRedBox");
            if (box == null)
            {
                box = UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "_DiagRedBox";
            }
            box.transform.position = new Vector3(0f, 1.5f, -5f);
            box.transform.localScale = new Vector3(1f, 3f, 1f);
            var bmr = box.GetComponent<MeshRenderer>();
            var redMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            redMat.name = "_DiagRed";
            redMat.SetColor("_BaseColor", new Color(1f, 0.05f, 0.05f, 1f));
            bmr.sharedMaterial = redMat;
            UnityEngine.Debug.Log("[WaterProject] DIAG red box at (0,1.5,-5).");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    // —— 26) 切换播放（诊断用：Play 中 Game 截图=真实渲染） ——
        [MenuItem("Tools/WaterProject/26 切换播放(诊断用)")]
        public static void TogglePlayDiag()
        {
            var box = UnityEngine.GameObject.Find("_DiagRedBox");
            if (box != null) UnityEngine.Object.DestroyImmediate(box);
            var w = UnityEngine.GameObject.Find("Water");
            if (w != null) w.SetActive(true);
            EditorApplication.isPlaying = !EditorApplication.isPlaying;
            UnityEngine.Debug.Log("[WaterProject] toggled play -> " + EditorApplication.isPlaying);
        }
    // —— 27) 诊断：打印 Water 渲染器当前材质身份（Play 内也有效） ——
        [MenuItem("Tools/WaterProject/27 打印Water材质身份(诊断)")]
        public static void DiagDumpWaterMaterial()
        {
            var w = UnityEngine.GameObject.Find("Water");
            if (w == null) { UnityEngine.Debug.Log("[WaterProject] DIAG Water NOT FOUND"); return; }
            var mr = w.GetComponent<Renderer>();
            if (mr == null) { UnityEngine.Debug.Log("[WaterProject] DIAG no renderer"); return; }
            var m = mr.sharedMaterial;
            string path = m != null ? AssetDatabase.GetAssetPath(m) : "null";
            string shaderName = m != null && m.shader != null ? m.shader.name : "null";
            var mf = w.GetComponent<MeshFilter>();
            string meshPath = mf != null && mf.sharedMesh != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : "null";
            UnityEngine.Debug.Log($"[WaterProject] DIAG Water name={w.name} mat={m?.name} path={path} shader={shaderName} mesh={meshPath} isInst={ (m != null && AssetDatabase.GetAssetPath(m) == "") }");
        }
    // —— 28) 诊断：打印 Water_Advanced 资产的真实属性值 ——
        [MenuItem("Tools/WaterProject/28 打印Water资产属性(诊断)")]
        public static void DiagDumpWaterAssetProps()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m == null) { UnityEngine.Debug.Log("[WaterProject] DIAG asset load FAILED"); return; }
            var sc = m.HasProperty("_ShallowColor") ? m.GetColor("_ShallowColor") : Color.magenta;
            var de = m.HasProperty("_DeepColor") ? m.GetColor("_DeepColor") : Color.magenta;
            var en = m.HasProperty("_WaterScatter") ? m.GetColor("_WaterScatter") : Color.magenta;
            var sz = m.HasProperty("_SkyZenith") ? m.GetColor("_SkyZenith") : Color.magenta;
            var sh = m.HasProperty("_SkyHorizon") ? m.GetColor("_SkyHorizon") : Color.magenta;
            var sun = m.HasProperty("_SunColor") ? m.GetColor("_SunColor") : Color.magenta;
            var ss = m.HasProperty("_ScatterStrength") ? m.GetFloat("_ScatterStrength") : -1f;
            var sa = m.HasProperty("_WaveAmpScale") ? m.GetFloat("_WaveAmpScale") : -1f;
            UnityEngine.Debug.Log($"[WaterProject] DIAG asset Shallow={sc} Deep={de} Scatter={en} Zen={sz} Hor={sh} Sun={sun} ScatStr={ss} Amp={sa}");
        }
    // —— 29) 深度诊断：Water 物体全部状态（transform/渲染器/材质属性块/网格） ——
        [MenuItem("Tools/WaterProject/29 深度诊断Water物体(诊断)")]
        public static void DiagDeepWater()
        {
            var w = UnityEngine.GameObject.Find("Water");
            if (w == null) { UnityEngine.Debug.Log("[WaterProject] DEEP Water NOT FOUND"); return; }
            var t = w.transform;
            UnityEngine.Debug.Log($"[WaterProject] DEEP pos={t.position} rot={t.eulerAngles} scale={t.localScale} active={w.activeInHierarchy} layer={LayerMask.LayerToName(w.layer)}({w.layer})");
            var mr = w.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mats = mr.sharedMaterials;
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < mats.Length; i++) names.Append((mats[i] != null ? mats[i].name : "NULL") + "|");
                var mpb = new MaterialPropertyBlock();
                mr.GetPropertyBlock(mpb);
                UnityEngine.Debug.Log($"[WaterProject] DEEP renderer enabled={mr.enabled} cast={mr.shadowCastingMode} mot={mr.motionVectorGenerationMode} matCount={mats.Length} mats={names} mpbEmpty={mpb.isEmpty}");
                var sm = mr.sharedMaterial;
                if (sm != null)
                    UnityEngine.Debug.Log($"[WaterProject] DEEP sharedMat={sm.name} shader={sm.shader.name} Shallow={sm.GetColor("_ShallowColor")} Deep={sm.GetColor("_DeepColor")} ScatStr={sm.GetFloat("_ScatterStrength")}");
            }
            else UnityEngine.Debug.Log("[WaterProject] DEEP no MeshRenderer");
            var mf = w.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                UnityEngine.Debug.Log($"[WaterProject] DEEP mesh={mf.sharedMesh.name} verts={mf.sharedMesh.vertexCount} tris={mf.sharedMesh.triangles.Length / 3} bounds={mf.sharedMesh.bounds} path={AssetDatabase.GetAssetPath(mf.sharedMesh)}");
            else UnityEngine.Debug.Log("[WaterProject] DEEP no/missing MeshFilter mesh");
            var col = w.GetComponent<Collider>();
            UnityEngine.Debug.Log($"[WaterProject] DEEP collider={(col != null ? col.GetType().Name : "none")}");
        }

        // —— 30) 重建 Water 物体：全新 GameObject 直挂资产（绕开 prefab 实例/MPB/override 一切可能） ——
        [MenuItem("Tools/WaterProject/30 重建Water物体(直挂资产)")]
        public static void RebuildWaterObject()
        {
            var old = UnityEngine.GameObject.Find("Water");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/LakeWaterIrregular.asset");
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            var go = new UnityEngine.GameObject("Water");
            go.transform.position = new Vector3(0f, 0.02f, 0f);
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var mf = go.AddComponent<MeshFilter>();
            if (mesh != null) mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>();
            if (mesh != null) mc.sharedMesh = mesh;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEngine.Debug.Log($"[WaterProject] REBUILT Water 挂 mesh={((mesh != null) ? mesh.name : "NULL")} mat={((mat != null) ? mat.name : "NULL")}");
        }
    // —— 31) 自渲染截图写盘（绕开桥截图通道，直接验证渲染真相） ——
        [MenuItem("Tools/WaterProject/31 自渲染截图写盘(诊断)")]
        public static void DiagSelfRenderToDisk()
        {
            var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null) { UnityEngine.Debug.Log("[WaterProject] SELF no camera"); return; }
            int w = Mathf.Max(4, cam.pixelWidth);
            int h = Mathf.Max(4, cam.pixelHeight);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            byte[] bytes = tex.EncodeToPNG();
            string path = System.IO.Path.Combine(Application.dataPath, "../Docs/screenshots/_self.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, bytes);
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Debug.Log("[WaterProject] SELF shot written: " + path + " (" + w + "x" + h + ")");
        }
    // —— 32) 诊断：Water 换 URP/Lit 纯蓝不透明（二分法验证） ——
        [MenuItem("Tools/WaterProject/32 Water换URP纯蓝(诊断)")]
        public static void DiagWaterLitBlue()
        {
            var w = UnityEngine.GameObject.Find("Water");
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            lit.name = "_DiagLitBlue";
            lit.SetColor("_BaseColor", new Color(0f, 0f, 1f, 1f));
            lit.SetColor("_Color", new Color(0f, 0f, 1f, 1f));
            lit.SetFloat("_Surface", 0f);      // 不透明
            lit.SetFloat("_AlphaClip", 0f);
            if (w != null)
            {
                var mr = w.GetComponent<MeshRenderer>();
                if (mr != null) { mr.sharedMaterial = lit; mr.enabled = true; }
                UnityEngine.Debug.Log("[WaterProject] DIAG Water -> URP/Lit opaque pure blue.");
            }
            else UnityEngine.Debug.Log("[WaterProject] DIAG Water NOT FOUND");
        }
    // —— 通用：失效场景中全部 Volume（URP Volume 会锁死天空/后处理/材质观感，导致改 RenderSettings 无效） ——
        private static void KillVolumes()
        {
            var all = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            int volCount = 0;
            foreach (var go in all)
            {
                if (go == null) continue;
                var comps = go.GetComponents<UnityEngine.Component>();
                foreach (var comp in comps)
                {
                    if (comp != null && comp.GetType().Name == "Volume")
                    {
                        volCount++;
                        var w = comp.GetType().GetProperty("weight");
                        if (w != null) w.SetValue(comp, 0f);
                        var en = comp.GetType().GetProperty("enabled");
                        if (en != null) en.SetValue(comp, false);
                        UnityEngine.Debug.Log("[WaterProject] VOLUME disabled: " + go.name);
                    }
                }
            }
            if (volCount > 0) UnityEngine.Debug.Log("[WaterProject] VOLUME total disabled: " + volCount);
        }

        // —— 33) 诊断：失效全部 Volume + 强换天空纯色（渲染环境验证） ——
        [MenuItem("Tools/WaterProject/33 失效Volume+强换天空(诊断)")]
        public static void DiagKillVolumesAndSkybox()
        {
            KillVolumes();

            // 强换天空：新建纯天蓝 skybox 材质（Unlit color）并赋给 RenderSettings
            var sky = new Material(Shader.Find("Unlit/Color"));
            sky.name = "_DiagSkyPure";
            sky.SetColor("_Color", new Color(0.45f, 0.72f, 1.0f, 1f));
            RenderSettings.skybox = sky;
            RenderSettings.ambientLight = new Color(0.5f, 0.6f, 0.8f);
            UnityEngine.Debug.Log("[WaterProject] DIAG skybox replaced with pure sky-blue Unlit.");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        // —— 34) 恢复正式天空盒（内置 DefaultSkybox + 曝光0.9白调）+ Water 归位 y0.02 ——
        [MenuItem("Tools/WaterProject/34 恢复正式天空+Water归位")]
        public static void RestoreProperSkyAndWater()
        {
            KillVolumes();
            var sky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                if (sky.HasProperty("_SkyTint"))
                {
                    sky.SetColor("_SkyTint", new Color(0.50f, 0.50f, 0.50f, 1f));   // 默认中性（避免 HDR>1 tint 引发环境色翻转）
                    sky.SetColor("_GroundColor", new Color(0.35f, 0.35f, 0.35f, 1f));
                    sky.SetFloat("_Exposure", 1.0f);
                    sky.SetFloat("_SunSize", 0.04f);
                    sky.SetFloat("_SunSizeConvergence", 5f);
                    sky.SetFloat("_AtmosphereThickness", 1.0f);
                }
                else if (sky.HasProperty("_Tint"))
                {
                    sky.SetColor("_Tint", new Color(0.75f, 0.88f, 1.15f, 1f));
                    sky.SetFloat("_Exposure", 1.05f);
                }
                EditorUtility.SetDirty(sky);
                UnityEngine.Debug.Log("[WaterProject] sky DEFAULT-PROCEDURAL: _SkyTint 天蓝 曝光1.05");
            }
            else UnityEngine.Debug.LogWarning("[WaterProject] Default-Skybox.mat not found!");
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            var w = UnityEngine.GameObject.Find("Water");
            if (w != null)
            {
                w.SetActive(true);
                w.transform.position = new Vector3(0f, 0.02f, 0f);
                var mr = w.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.enabled = true;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
                    if (mat != null) mr.sharedMaterial = mat;
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    // —— 35) 天空换"简单天蓝天空盒"（自定义 shader，天顶亮蓝/地平浅白；根治水面反射灰青） ——
        [MenuItem("Tools/WaterProject/35 简单天蓝天空盒")]
        public static void ApplySimpleBlueSky()
        {
            KillVolumes();
            var shader = Shader.Find("WaterProject/SkySimpleBlue");
            var sky = (shader != null)
                ? new Material(shader)
                : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            sky.name = "Sky_SimpleBlue";
            sky.SetColor("_HorizonColor", new Color(0.72f, 0.84f, 0.96f, 1f));
            sky.SetColor("_ZenithColor", new Color(0.36f, 0.65f, 1.0f, 1f));
            sky.SetColor("_GroundColor", new Color(0.26f, 0.32f, 0.38f, 1f));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.55f, 0.90f, 1f);
            // 太阳保持斜照以打出高光
            var sun = UnityEngine.GameObject.Find("Sun") ?? UnityEngine.GameObject.Find("Directional Light");
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(52f, 28f, 0f);
                var dl = sun.GetComponent<Light>();
                if (dl != null) dl.intensity = 1.0f;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEngine.Debug.Log("[WaterProject] sky -> SkySimpleBlue (天顶亮蓝/地平浅白)");
        }

    // —— 36) 重建水域网格：修掉 rc8 起 27 圈同半径的退化面（只剩岸线轮廓、无水面） ——
        [MenuItem("Tools/WaterProject/36 重建水域网格(修复退化环)")]
        public static void RebuildWaterDiskFixed()
        {
            string waterPath = "Assets/Art/Meshes/LakeWaterIrregular.asset";
            int seg = 120, rings = 26;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int gr = 0; gr <= rings; gr++)
            {
                float rr = (float)gr / rings;
                for (int gs = 0; gs <= seg; gs++)
                {
                    float th = Mathf.PI * 2f * gs / seg;
                    // 水面铺到 45° 沙滩斜坡顶端内侧，水陆交界落在坡面上形成随波冲蚀带
                    float rEdge = Mathf.Max(12f, ShoreR(th) * 0.6f) - 0.05f;
                    float r = Mathf.Lerp(0.4f, rEdge, rr);
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
            var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(waterPath);
            if (oldMesh == null)
            {
                var m = new Mesh { name = "LakeWaterIrregular" };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                AssetDatabase.CreateAsset(m, waterPath);
            }
            else
            {
                // 就地重写同一资产对象：CopySerialized 只搬 CPU 侧数据，GPU 顶点缓冲可能留空
                oldMesh.Clear();
                oldMesh.vertices = verts.ToArray();
                oldMesh.uv = uvs.ToArray();
                oldMesh.triangles = tris.ToArray();
                oldMesh.RecalculateNormals();
                oldMesh.RecalculateBounds();
                EditorUtility.SetDirty(oldMesh);
            }
            AssetDatabase.SaveAssets();

            var w = UnityEngine.GameObject.Find("Water");
            if (w == null) w = new UnityEngine.GameObject("Water");
            w.SetActive(true);
            w.transform.position = new Vector3(0f, 0.02f, 0f);
            var wmf = w.GetComponent<MeshFilter>();
            if (wmf == null) wmf = w.AddComponent<MeshFilter>();
            wmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(waterPath);
            var wmr = w.GetComponent<MeshRenderer>();
            if (wmr == null) wmr = w.AddComponent<MeshRenderer>();
            var wmat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (wmat != null) wmr.sharedMaterial = wmat;   // 清掉诊断期挂上的 URP/Lit
            var wcol = w.GetComponent<MeshCollider>();
            if (wcol != null) wcol.sharedMesh = wmf.sharedMesh;
            EditorUtility.SetDirty(w);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEngine.Debug.Log("[WaterProject] water disk rebuilt: verts=" + verts.Count + " tris=" + (tris.Count / 3));
        }

    // —— 37) 水面渲染探针：把"为什么一个像素都不出"的实况写文件（桥的 Debug.Log 读不回来） ——
        [MenuItem("Tools/WaterProject/37 水面渲染探针(写文件)")]
        public static void ProbeWaterRenderer()
        {
            var sb = new System.Text.StringBuilder();
            System.Action<string> L = s => sb.AppendLine(s);

            var w = UnityEngine.GameObject.Find("Water");
            if (w == null) L("Water: GameObject.Find NULL (inactive?)");
            else
            {
                L("Water activeSelf=" + w.activeSelf + " activeInHierarchy=" + w.activeInHierarchy
                  + " layer=" + LayerMask.LayerToName(w.layer) + "(" + w.layer + ")");
                L("Water pos=" + w.transform.position + " scale=" + w.transform.lossyScale);

                var mf = w.GetComponent<MeshFilter>();
                if (mf == null) L("MeshFilter: none");
                else if (mf.sharedMesh == null) L("MeshFilter.sharedMesh: NULL");
                else
                {
                    var m = mf.sharedMesh;
                    L("mesh=" + m.name + " verts=" + m.vertexCount + " subMeshes=" + m.subMeshCount
                      + " tris0=" + (m.GetTriangles(0).Length / 3) + " bounds=" + m.bounds
                      + " path=" + AssetDatabase.GetAssetPath(m));
                }

                var mr = w.GetComponent<MeshRenderer>();
                if (mr == null) L("MeshRenderer: none");
                else
                {
                    L("renderer enabled=" + mr.enabled + " shadowCasting=" + mr.shadowCastingMode
                      + " bounds=" + mr.bounds + " lightmapIndex=" + mr.lightmapIndex
                      + " receiveGI=" + mr.receiveGI);
                    var mats = mr.sharedMaterials;
                    L("sharedMaterials.Length=" + mats.Length);
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mm = mats[i];
                        if (mm == null) { L("  mat[" + i + "]=NULL"); continue; }
                        var sh = mm.shader;
                        L("  mat[" + i + "]=" + mm.name + " shader=" + (sh != null ? sh.name : "NULL")
                          + " shaderSupported=" + (sh != null && sh.isSupported)
                          + " shaderPath=" + (sh != null ? AssetDatabase.GetAssetPath(sh) : "-")
                          + " hidden=" + (sh != null && sh.hideFlags != UnityEngine.HideFlags.None)
                          + " matAssetPath=" + AssetDatabase.GetAssetPath(mm));
                    }
                }
            }

            var cam = Camera.main;
            if (cam == null) L("Camera.main: NULL");
            else
            {
                L("cam=" + cam.name + " enabled=" + cam.enabled + " ortho=" + cam.orthographic
                  + " near=" + cam.nearClipPlane + " far=" + cam.farClipPlane
                  + " cullingMask=" + cam.cullingMask + " targetTexture=" + (cam.targetTexture != null));
                var wl = w != null ? w.layer : -1;
                L("water layer in cullingMask=" + (wl >= 0 && (cam.cullingMask & (1 << wl)) != 0));
                L("cam pos=" + cam.transform.position + " rot=" + cam.transform.eulerAngles);
            }

            var grp = GraphicsSettings.currentRenderPipeline;
            var qal = QualitySettings.renderPipeline;
            L("GraphicsSettings.currentRenderPipeline=" + (grp != null ? grp.name : "NULL(内置管线)"));
            L("QualitySettings.renderPipeline=" + (qal != null ? qal.name : "NULL")
              + " currentQuality=" + QualitySettings.GetQualityLevel() + "(" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ")");
            L("Shader.Find(URP/Lit)=" + (Shader.Find("Universal Render Pipeline/Lit") != null));
            L("Shader.Find(WaterProject/Water_Advanced)=" + (Shader.Find("WaterProject/Water_Advanced") != null));

            // 谁在 y=0.02 水面上方/共面 → 遮挡嫌疑清单
            L("---- renderers reaching y>=-0.05 (world bounds) ----");
            var pf = UnityEngine.GameObject.Find("PondFloor");
            if (pf != null)
            {
                var pmf = pf.GetComponent<MeshFilter>();
                var pm = pmf != null ? pmf.sharedMesh : null;
                L("PondFloor localScale=" + pf.transform.localScale + " mesh=" + (pm != null ? pm.name : "NULL")
                  + " localBounds=" + (pm != null ? pm.bounds.ToString() : "-"));
                if (pm != null)
                {
                    var vs = pm.vertices;
                    float ymin = float.MaxValue, ymax = float.MinValue;
                    for (int i = 0; i < vs.Length; i++) { if (vs[i].y < ymin) ymin = vs[i].y; if (vs[i].y > ymax) ymax = vs[i].y; }
                    L("PondFloor verts=" + vs.Length + " yMin=" + ymin.ToString("F3") + " yMax=" + ymax.ToString("F3")
                      + " path=" + AssetDatabase.GetAssetPath(pm));
                    for (int i = 0; i < vs.Length && i < 4; i++) L("  v[" + i + "]=" + vs[i]);
                    int mid = vs.Length / 2;
                    L("  v[" + mid + "]=" + vs[mid] + "  v[last]=" + vs[vs.Length - 1]);
                }
            }
            // 黑渲染诊断：法线缺失/NaN/朝下 会让 N·L<=0 只剩环境光
            L("---- normal sanity ----");
            foreach (var nm in new[] { "DistantHill_00", "GreenWall", "ShoreSandRing", "Boulder_00" })
            {
                var go = UnityEngine.GameObject.Find(nm);
                if (go == null) { L(nm + ": NOT FOUND"); continue; }
                var mf3 = go.GetComponent<MeshFilter>();
                var ms = mf3 != null ? mf3.sharedMesh : null;
                if (ms == null) { L(nm + ": no mesh"); continue; }
                var ns = ms.normals; var uvs3 = ms.uv; var v3 = ms.vertices;
                int zero = 0, nan = 0; float avgY = 0f;
                for (int i = 0; i < ns.Length; i++)
                {
                    var nn = ns[i];
                    if (float.IsNaN(nn.x) || float.IsNaN(nn.y) || float.IsNaN(nn.z)) { nan++; continue; }
                    if (nn.sqrMagnitude < 1e-6f) zero++;
                    avgY += nn.y;
                }
                if (ns.Length > 0) avgY /= ns.Length;
                var mr3 = go.GetComponent<MeshRenderer>();
                var mt3 = mr3 != null ? mr3.sharedMaterial : null;
                L(string.Format("{0}: mesh={1} verts={2} uv={3} normals={4} zeroNrm={5} nanNrm={6} avgNrmY={7:F3} mat={8} shader={9}",
                    nm, ms.name, v3.Length, uvs3 != null ? uvs3.Length : -1, ns.Length, zero, nan, avgY,
                    mt3 != null ? mt3.name : "NULL", mt3 != null && mt3.shader != null ? mt3.shader.name : "-"));
            }

            var all = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var rows = new List<MeshRenderer>();
            foreach (var r in all) if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.bounds.max.y >= -0.05f) rows.Add(r);
            rows.Sort((a, b) => a.bounds.center.y.CompareTo(b.bounds.center.y));
            foreach (var r in rows)
            {
                var m0 = r.sharedMaterial;
                L(string.Format("{0,-22} c=({1,7:F2},{2,7:F2},{3,7:F2}) ext=({4,6:F2},{5,6:F2},{6,6:F2}) yTop={7:F3} mat={8} layer={9}",
                    r.gameObject.name, r.bounds.center.x, r.bounds.center.y, r.bounds.center.z,
                    r.bounds.extents.x, r.bounds.extents.y, r.bounds.extents.z, r.bounds.max.y,
                    m0 != null ? m0.name : "NULL", LayerMask.LayerToName(r.gameObject.layer)));
            }

            string path = System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            UnityEngine.Debug.Log("[WaterProject] probe written: " + path);
        }

    // —— 38) 照明回到 rc9 基线：清掉菜单24/35 的过曝残留 ——
        [MenuItem("Tools/WaterProject/38 照明回到rc9基线(清诊断残留)")]
        public static void RestoreRc9LightingBaseline()
        {
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.9f;
            RenderSettings.ambientSkyColor = new Color(0.212f, 0.227f, 0.259f, 1f);
            RenderSettings.fog = false;

            var sun = UnityEngine.GameObject.Find("Sun") ?? UnityEngine.GameObject.Find("Directional Light");
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(60f, 25f, 0f);
                var l = sun.GetComponent<Light>();
                if (l != null)
                {
                    l.intensity = 0.85f;
                    l.color = new Color(1f, 0.95686275f, 0.8392157f, 1f);
                    EditorUtility.SetDirty(sun);
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEngine.Debug.Log("[WaterProject] lighting -> rc9 baseline (Default-Skybox / ambient 0.9 / sun 0.85 @60,25)");
        }

    // —— 39) 对标参考帧：蓝灰棱角巨石群（原创程序化，零版权） ——
        [MenuItem("Tools/WaterProject/39 参考帧巨石群重构(蓝灰棱角岩)")]
        public static void RebuildReferenceBoulders()
        {
            System.Random rng = new System.Random(2026091839);
            float S(float th) => ShoreR(th) * 0.6f;

            // 1) 三种棱角岩网格：非索引三角 → 每面独立法线 → 低多边形平面着色
            var rocks = new Mesh[3];
            for (int v = 0; v < 3; v++)
            {
                string rp = "Assets/Art/Meshes/Boulder_0" + (v + 1) + ".asset";
                var built = BuildFacetedRock(v);
                var asset = AssetDatabase.LoadAssetAtPath<Mesh>(rp);
                if (asset == null)
                {
                    built.name = "Boulder_0" + (v + 1);
                    AssetDatabase.CreateAsset(built, rp);
                    asset = built;
                }
                else
                {
                    asset.Clear();
                    asset.vertices = built.vertices;
                    asset.normals = built.normals;
                    asset.triangles = built.triangles;
                    asset.RecalculateBounds();
                    EditorUtility.SetDirty(asset);
                }
                rocks[v] = asset;
            }
            AssetDatabase.SaveAssets();

            // 2) 原创蓝灰板岩材质
            string matPath = "Assets/Art/Materials/Slate_BlueRock.mat";
            var rockMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (rockMat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                rockMat = new Material(lit) { name = "Slate_BlueRock" };
                AssetDatabase.CreateAsset(rockMat, matPath);
            }
            rockMat.SetColor("_BaseColor", new Color(0.470f, 0.500f, 0.550f, 1f));
            rockMat.SetFloat("_Smoothness", 0.12f);
            rockMat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(rockMat);
            AssetDatabase.SaveAssets();

            // 3) 清掉旧鹅卵石与上一轮巨石（保证可重复执行）
            foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name.StartsWith("Pebble_") || go.name.StartsWith("Boulder_"))) UnityEngine.Object.DestroyImmediate(go);

            // 4) 成簇散布：岸线 10 簇 + 水中 4 簇 + 3 块巨型平台岩（参考帧是密集石群 + 少数大平板岩）
            int made = 0;
            for (int cl = 0; cl < 10; cl++)
            {
                float baseTh = Mathf.PI * 2f * cl / 10f + (float)rng.NextDouble() * 0.4f;
                int cnt = 8 + rng.Next(6);
                for (int k = 0; k < cnt; k++)
                {
                    float th = baseTh + (float)(rng.NextDouble() - 0.5) * 0.75f;
                    float rIn2 = Mathf.Max(12f, S(th));
                    float r = rIn2 + (float)(rng.NextDouble() - 0.35) * 4.2f;
                    made += PlaceBoulder(rocks, rockMat, rng, "Boulder_", made,
                        new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r), 0.18f, 0.95f);
                }
            }
            for (int cl = 0; cl < 4; cl++)
            {
                float baseTh = Mathf.PI * 2f * (cl + 0.5f) / 4f + 0.7f;
                for (int k = 0; k < 5; k++)
                {
                    float th = baseTh + (float)(rng.NextDouble() - 0.5) * 0.7f;
                    float r = 3.0f + cl * 2.4f + (float)(rng.NextDouble() - 0.5) * 1.6f;
                    made += PlaceBoulder(rocks, rockMat, rng, "Boulder_", made,
                        new Vector3(Mathf.Cos(th) * r, -0.30f, Mathf.Sin(th) * r), 0.25f, 0.70f);
                }
            }
            for (int k = 0; k < 3; k++)
            {
                float th = Mathf.PI * 2f * (k + 0.25f) / 3f + 0.35f;
                int shelfIdx = made;
                made += PlaceBoulder(rocks, rockMat, rng, "Boulder_", made,
                    new Vector3(Mathf.Cos(th) * (Mathf.Max(12f, S(th)) + 2.4f), 0.05f,
                                Mathf.Sin(th) * (Mathf.Max(12f, S(th)) + 2.4f)), 1.6f, 2.4f);
                var shelf = UnityEngine.GameObject.Find("Boulder_" + shelfIdx.ToString("00"));
                if (shelf != null)
                {
                    var s = shelf.transform.localScale;
                    shelf.transform.localScale = new Vector3(s.x * 1.7f, s.y * 0.42f, s.z * 1.5f);
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEngine.Debug.Log("[WaterProject] boulders rebuilt: " + made);
        }

        // 非索引低多边形卧石：方向哈希扰动 → 同位置顶点一致，裂开后仍是封闭体
        static Mesh BuildFacetedRock(int variant, float lump = 1f)
        {
            const int segs = 9, rings = 6;
            float seed = 11.3f + variant * 7.7f;
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var idx = new List<int>();

            System.Func<int, int, Vector3> pt = (i, j) =>
            {
                float phi = Mathf.PI * 2f * i / segs;
                float theta = Mathf.PI * 0.5f * (2f * j / rings - 1f);
                float ct = Mathf.Cos(theta);
                float cx = ct * Mathf.Cos(phi), cy = Mathf.Sin(theta), cz = ct * Mathf.Sin(phi);
                // lump 缩放三档噪声幅度：1 = 棱角巨石，0.3 左右 = 圆润卵石
                float n = 1f
                    + 0.26f * lump * Mathf.Sin(3.1f * cx + seed) * Mathf.Cos(2.7f * cy + seed * 0.6f)
                    + 0.17f * lump * Mathf.Sin(5.3f * cy + seed * 1.3f) * Mathf.Cos(4.9f * cz + seed)
                    + 0.10f * lump * Mathf.Sin(7.7f * cz + seed * 1.7f);
                return new Vector3(cx, cy * 0.72f, cz) * n;
            };

            for (int j = 0; j < rings; j++)
                for (int i = 0; i < segs; i++)
                {
                    AddFacet(pos, nrm, pt(i, j), pt(i, j + 1), pt(i + 1, j));
                    AddFacet(pos, nrm, pt(i + 1, j), pt(i, j + 1), pt(i + 1, j + 1));
                }

            var m = new Mesh();
            m.SetVertices(pos);
            m.SetNormals(nrm);
            for (int i = 0; i < pos.Count; i++) idx.Add(i);
            m.SetTriangles(idx, 0);
            m.RecalculateBounds();
            return m;
        }

        // 就地重写已存在的 Mesh 资产：CopySerialized 只复制序列化字段，GPU 顶点/法线缓冲会留空
        static void WriteMeshFrom(Mesh dst, Mesh src)
        {
            int vc = src.vertexCount;
            dst.Clear();
            dst.subMeshCount = Mathf.Max(1, src.subMeshCount);
            dst.SetVertices(src.vertices);
            if (src.uv != null && src.uv.Length == vc) dst.SetUVs(0, src.uv);
            if (src.normals != null && src.normals.Length == vc) dst.SetNormals(src.normals);
            if (src.tangents != null && src.tangents.Length == vc) dst.SetTangents(src.tangents);
            for (int s = 0; s < dst.subMeshCount; s++) dst.SetTriangles(src.GetTriangles(s), s);
            dst.RecalculateBounds();
            EditorUtility.SetDirty(dst);
        }

        // 地面类网格：绕序朝上 + 显式朝上法线（沙环曾因绕序反+背面剔除，整片沙滩从上往下看被剔掉）
        static void FaceGroundUp(Mesh m)
        {
            if (m == null) return;
            var v = m.vertices;
            var tri = m.triangles;
            if (v.Length == 0 || tri.Length < 3) return;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                sum += Vector3.Cross(b - a, c - a);
            }
            if (sum.y < 0f)
            {
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    var t = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t;
                }
                m.triangles = tri;
            }

            var nrm = new List<Vector3>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                var rd = new Vector3(v[i].x, 0f, v[i].z);
                rd = rd.sqrMagnitude > 1e-8f ? rd.normalized : Vector3.right;
                nrm.Add((rd * 0.22f + Vector3.up).normalized);
            }
            m.SetNormals(nrm);
        }

        static void AddFacet(List<Vector3> pos, List<Vector3> nrm, Vector3 a, Vector3 b, Vector3 c)
        {
            var centroid = (a + b + c) / 3f;
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            if (Vector3.Dot(n, centroid) < 0f)          // 法线朝外，绕序随之翻转
            {
                n = -n; var t = b; b = c; c = t;
            }
            pos.Add(a); nrm.Add(n);
            pos.Add(b); nrm.Add(n);
            pos.Add(c); nrm.Add(n);
        }

        static int PlaceBoulder(Mesh[] rocks, Material mat, System.Random rng, string prefix, int index, Vector3 at, float sMin, float sMax)
        {
            var go = new UnityEngine.GameObject(prefix + index.ToString("00"));
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 16f,
                                                     (float)rng.NextDouble() * 360f,
                                                     (float)(rng.NextDouble() - 0.5) * 16f);
            float s = sMin + (float)rng.NextDouble() * (sMax - sMin);
            go.transform.localScale = new Vector3(s * (0.85f + (float)rng.NextDouble() * 0.4f),
                                                  s * (0.65f + (float)rng.NextDouble() * 0.3f),
                                                  s * (0.85f + (float)rng.NextDouble() * 0.4f));
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = rocks[rng.Next(rocks.Length)];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return 1;
        }

    // —— 40) 应用 rc4 水面基准（03c1ad8）：菜单22/24 会覆写水材质，重建链必须以本菜单收尾 ——
        [MenuItem("Tools/WaterProject/40 应用rc4水面基准(03c1ad8)")]
        public static void ApplyRc4WaterBaseline()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m == null) { UnityEngine.Debug.Log("[WaterProject] rc4 baseline: Water_Advanced.mat NOT FOUND"); return; }

            var floats = new System.Collections.Generic.Dictionary<string, float>
            {
                { "_Absorb", 0.9f }, { "_AmbientStrength", 0.5f },
                { "_CrestFoam", 1f }, { "_CrestJ", 0.6f }, { "_CrestSoft", 0.35f },
                { "_DepthSharp", 3f }, { "_Eta", 1.33f }, { "_MaxUVOffset", 0.12f },
                { "_DetailStrength", 3f }, { "_DetailScale", 1.6f }, { "_DetailStart", 2f }, { "_DetailEnd", 40f },
                { "_FoamLift", 0.03f }, { "_FresnelPow", 6f }, { "_Opacity", 0.95f },
                { "_InteractionAmp", 0.22f }, { "_InteractionFoam", 0.45f }, { "_InteractionNorm", 1.4f },
                { "_MaxTess", 6f }, { "_TessNear", 6f }, { "_TessFar", 32f }, { "_TessTargetSize", 0.35f },
                { "_RefractStrength", 0.9f }, { "_ScatterStrength", 0.38f },
                { "_SpecPower", 180f }, { "_SpecStrength", 0.5f },
                { "_ShoreFoam", 0.8f }, { "_SunGloss", 500f }, { "_SunStrength", 0.3f },
                { "_SunTransmission", 0.7f }, { "_TransGloss", 48f },
                { "_WaveCount", 4f }, { "_WaveAmpScale", 0.5f }, { "_WaveTimeScale", 1.8f },
            };
            var colors = new System.Collections.Generic.Dictionary<string, Color>
            {
                { "_DeepColor", new Color(0.09f, 0.38f, 0.47f, 1f) },
                { "_ShallowColor", new Color(0.20f, 0.72f, 0.82f, 1f) },
                { "_WaterScatter", new Color(0.42f, 0.75f, 0.88f, 1f) },
                { "_SkyZenith", new Color(0.42f, 0.70f, 1.00f, 1f) },
                { "_SkyHorizon", new Color(0.80f, 0.92f, 1.00f, 1f) },
                { "_SunColor", new Color(1.00f, 0.93f, 0.75f, 1f) },
                { "_FoamColor", new Color(0.95f, 0.97f, 0.98f, 1f) },
            };
            int n = 0;
            foreach (var kv in floats) if (m.HasProperty(kv.Key)) { m.SetFloat(kv.Key, kv.Value); n++; }
            foreach (var kv in colors) if (m.HasProperty(kv.Key)) { m.SetColor(kv.Key, kv.Value); n++; }
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WaterProject] rc4 water baseline applied: " + n + " props");
        }

    // —— 41) 明亮天空 + 梯度环境光（环境光与天空解耦，避免 rc10 那种整体过曝） ——
        [MenuItem("Tools/WaterProject/41 明亮天空+梯度环境光")]
        public static void ApplyBrightSkyGradientAmbient()
        {
            var sb = new System.Text.StringBuilder();
            var sh = Shader.Find("WaterProject/SkySimpleBlue");
            if (sh == null)
            {
                sb.AppendLine("SkySimpleBlue shader NOT FOUND -> skybox unchanged");
            }
            else
            {
                var sky = new Material(sh) { name = "Sky_SimpleBlue" };
                sky.SetColor("_ZenithColor", new Color(0.243f, 0.522f, 0.918f, 1f));
                sky.SetColor("_HorizonColor", new Color(0.784f, 0.878f, 0.961f, 1f));
                sky.SetColor("_GroundColor", new Color(0.420f, 0.500f, 0.520f, 1f));   // 地形仅60m，此色即地平线远雾，勿用暗灰
                RenderSettings.skybox = sky;
                sb.AppendLine("skybox -> SkySimpleBlue zenith(0.243,0.522,0.918) horizon(0.784,0.878,0.961) ground(0.42,0.50,0.52) ");
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.300f, 0.360f, 0.440f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.260f, 0.290f, 0.320f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.200f, 0.190f, 0.170f, 1f);
            RenderSettings.ambientIntensity = 0.75f;
            sb.AppendLine("ambient -> Trilight sky(0.30,0.36,0.44) equator(0.26,0.29,0.32) ground(0.20,0.19,0.17) int=0.75 ");

            var sun = UnityEngine.GameObject.Find("Sun") ?? UnityEngine.GameObject.Find("Directional Light");
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(52f, 10f, 0f);
                var l = sun.GetComponent<Light>();
                if (l != null) { l.intensity = 1.25f; EditorUtility.SetDirty(sun); }
                sb.AppendLine("sun -> Euler(52,10,0) intensity 1.25 (暖色直射压过青色环境光，沙地才能回暖白)");
            }
            else sb.AppendLine("sun NOT FOUND");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU41 " + sb.ToString().Replace("\n", "\nMENU41 "));
        }

    // —— 42) 水色对标参考帧（去奶白；不动 rc4 基准的波浪与不透明度） ——
        [MenuItem("Tools/WaterProject/42 水色对标参考帧(去奶白)")]
        public static void TuneWaterToReferenceFrame()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (m == null) { UnityEngine.Debug.Log("[WaterProject] water tune: material NOT FOUND"); return; }

            var floats = new System.Collections.Generic.Dictionary<string, float>
            {
                { "_ScatterStrength", 0.22f },   // 体积自发光降下来，奶白主因之一
                { "_SpecStrength", 0.35f }, { "_SunStrength", 0.22f },
                { "_Absorb", 1.6f }, { "_DepthSharp", 5f },   // 深度更快饱和，浅底不再往上泛白
                { "_RefractStrength", 0.55f },               // 少把亮沙底掺进水色
                { "_CrestJ", 0.28f }, { "_CrestFoam", 0.22f }, { "_CrestSoft", 0.22f },  // 泡沫只在真正的浪尖成线，不再铺成片
                { "_ShoreFoam", 0.30f }, { "_SunTransmission", 0.45f },                   // 俯视机位下 R 仍偏高：削暖色来源
            };
            var colors = new System.Collections.Generic.Dictionary<string, Color>
            {
                { "_SkyHorizon", new Color(0.42f, 0.60f, 0.68f, 1f) },   // 掠射角反射压暗
                { "_SkyZenith", new Color(0.20f, 0.46f, 0.60f, 1f) },
            };
            var log = new System.Text.StringBuilder();
            foreach (var kv in floats) if (m.HasProperty(kv.Key)) { m.SetFloat(kv.Key, kv.Value); log.Append(kv.Key + "=" + kv.Value + " "); }
            foreach (var kv in colors) if (m.HasProperty(kv.Key)) { m.SetColor(kv.Key, kv.Value); log.Append(kv.Key + "=" + kv.Value + " "); }
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU42 water tune: " + log + " (参考帧深水目标 0.231,0.600,0.667)\n");
        }

    // —— 43) 真实 Game 视图截图（必须在 Play 中；菜单31 的 cam.Render 路径可能没有深度纹理） ——
        [MenuItem("Tools/WaterProject/43 Game视图截图(Play中)")]
        public static void CaptureRealGameView()
        {
            string note;
            if (!UnityEngine.Application.isPlaying)
            {
                note = "MENU43 REFUSED: not in Play mode (先跑菜单26 进 Play)\n";
            }
            else
            {
                string outPath = "Docs/screenshots/_gameview.png";   // 相对工程根
                ScreenCapture.CaptureScreenshot(outPath);
                note = "MENU43 captured " + outPath + " frame=" + UnityEngine.Time.frameCount
                       + " screen=" + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height
                       + " editor=" + UnityEngine.Application.isEditor
                       + " (真实 Game 帧缓冲，与菜单31 的 cam.Render 路径可对比)\n";
            }
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), note);
        }

    // —— 44) 切换水面深度调试输出（R=gap G=depthMix B=fres） ——
        [MenuItem("Tools/WaterProject/44 切换水面深度调试输出")]
        public static void ToggleWaterDepthDebug()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            string probe = System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt");
            if (m == null) { System.IO.File.AppendAllText(probe, "MENU44 material NOT FOUND\n"); return; }
            if (!m.HasProperty("_DepthDebug")) { System.IO.File.AppendAllText(probe, "MENU44 shader has no _DepthDebug (未编译/属性缺失)\n"); return; }

            float nxt = m.GetFloat("_DepthDebug") > 0.5f ? 0f : 1f;
            m.SetFloat("_DepthDebug", nxt);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            System.IO.File.AppendAllText(probe, "MENU44 _DepthDebug -> " + nxt + "\n");
        }

    // —— 45) 对标参考帧：俯视机位 + 石/沙/远角色对齐实测值 ——
        [MenuItem("Tools/WaterProject/45 对标参考帧(俯视机位+石沙色)")]
        public static void AlignToReferenceFrames()
        {
            var log = new System.Text.StringBuilder();

            var cam = UnityEngine.GameObject.Find("Main Camera");
            if (cam != null)
            {
                cam.transform.localPosition = new Vector3(0f, 7.0f, -14.5f);
                // Unity 里 +X 旋转才是俯视；本项目多个菜单用负值当"俯角"是反的（rc9 验收图只拍到天空即此因）
                cam.transform.localRotation = Quaternion.Euler(26f, 0f, 0f);
                EditorUtility.SetDirty(cam);
                log.Append("cam=(0,7,-14.5) Euler(+26,0,0) ");
            }

            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (water != null && water.HasProperty("_SkyReflect"))
            {
                water.SetFloat("_SkyReflect", 0.45f);
                EditorUtility.SetDirty(water);
                log.Append("_SkyReflect=0.45 ");
            }

            var targets = new System.Collections.Generic.Dictionary<string, Color>
            {
                { "Slate_BlueRock",    new Color(0.575f, 0.673f, 0.689f, 1f) },   // 参考帧岩石蓝灰
                { "Sand_Shore",        new Color(0.702f, 0.648f, 0.600f, 1f) },   // 参考帧沙滩暖白
                { "Hill_Distant",      new Color(0.419f, 0.529f, 0.573f, 1f) },   // 远山雾蓝
                { "GreenWall_Distant", new Color(0.243f, 0.545f, 0.286f, 1f) },   // 远岸草坡饱和绿（先前误取水色→死灰带）
            };
            foreach (var kv in targets)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/" + kv.Key + ".mat");
                if (mat == null) { log.Append(kv.Key + ":MISS "); continue; }
                mat.SetColor("_BaseColor", kv.Value);
                EditorUtility.SetDirty(mat);
                log.Append(kv.Key + "=ok ");
            }
            AssetDatabase.SaveAssets();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU45 " + log + "(参考帧实测: 深水0.245,0.611,0.662 / 沙0.702,0.648,0.600 / 岩0.575,0.673,0.689 / 远岸0.419,0.529,0.573)\n");
        }

    // —— 46) 地面类网格绕序修正（修"俯视时沙滩被背面剔除整片消失"） ——
        [MenuItem("Tools/WaterProject/46 地面网格绕序修正(沙环池底)")]
        public static void FixGroundMeshWinding()
        {
            var sb = new System.Text.StringBuilder();
            string[] paths =
            {
                "Assets/Art/Meshes/ShoreSandRing.asset",
                "Assets/Art/Meshes/PondFloorShore.asset",
                "Assets/Art/Meshes/BeachSlope.asset",
            };
            foreach (var p in paths)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                if (mesh == null) { sb.Append(System.IO.Path.GetFileName(p) + ":MISS "); continue; }
                float before = AvgNormalY(mesh);
                FaceGroundUp(mesh);
                EditorUtility.SetDirty(mesh);
                sb.Append(System.IO.Path.GetFileName(p) + " avgNrmY " + before.ToString("F3") + "->" + AvgNormalY(mesh).ToString("F3")
                          + " tris=" + (mesh.triangles.Length / 3) + "  ");
            }
            AssetDatabase.SaveAssets();
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU46 " + sb + "\n");
        }

        static float AvgNormalY(Mesh m)
        {
            var ns = m.normals;
            if (ns == null || ns.Length == 0) return float.NaN;
            float s = 0f;
            for (int i = 0; i < ns.Length; i++) s += ns[i].y;
            return s / ns.Length;
        }

    // —— 47) 加 URP Volume（ACES 色调映射 + 色彩调整）；必须在 24 之后跑（KillVolumes 会停用 Volume） ——
        [MenuItem("Tools/WaterProject/47 加URP Volume(ACES+色彩调整)")]
        public static void ApplyGrasslandsVolume()
        {
            var sb = new System.Text.StringBuilder();
            string profPath = "Assets/Settings/URP/GrasslandsVolume.profile.asset";
            var prof = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(profPath);
            if (prof == null)
            {
                prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(prof, profPath);
                sb.Append("profile created ");
            }
            else sb.Append("profile reused ");

            // Add<T>() 只建内存对象；不调 AddObjectToAsset 就永远不落盘，
            // profile 里只会留下 fileID: 0 的空槽 → 整条后处理链是静默空操作
            prof.components.RemoveAll(c => c == null);

            if (!prof.TryGet<UnityEngine.Rendering.Universal.Tonemapping>(out var ton))
            {
                ton = prof.Add<UnityEngine.Rendering.Universal.Tonemapping>();
                AssetDatabase.AddObjectToAsset(ton, prof);
                sb.Append("tonemap+subasset ");
            }
            ton.mode.overrideState = true; ton.mode.value = UnityEngine.Rendering.Universal.TonemappingMode.ACES;

            if (!prof.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var col))
            {
                col = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
                AssetDatabase.AddObjectToAsset(col, prof);
                sb.Append("colorAdj+subasset ");
            }
            col.postExposure.overrideState = true; col.postExposure.value = 1.15f;
            col.saturation.overrideState = true;  col.saturation.value = 14f;
            col.contrast.overrideState = true;    col.contrast.value = 0f;
            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();

            var go = UnityEngine.GameObject.Find("GlobalVolume");
            if (go == null) { go = new UnityEngine.GameObject("GlobalVolume"); sb.Append("volume created "); }
            var vol = go.GetComponent<UnityEngine.Rendering.Volume>();
            if (vol == null) vol = go.AddComponent<UnityEngine.Rendering.Volume>();
            vol.enabled = true; vol.isGlobal = true; vol.sharedProfile = prof;
            EditorUtility.SetDirty(go);
            sb.Append("ACES postExp=" + col.postExposure.value + " sat=" + col.saturation.value + " con=" + col.contrast.value);

            var cam = UnityEngine.GameObject.Find("Main Camera");
            if (cam != null)
            {
                var cd = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (cd == null) cd = cam.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                cd.renderPostProcessing = true;
                EditorUtility.SetDirty(cam);
                sb.Append(" cam.postProcessing=on");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU47 " + sb + "\n");
        }

    // —— 48) 对标参考帧：曝光/反照率/水色重标定（必须在 41/42/45 之后跑） ——
        [MenuItem("Tools/WaterProject/48 对标参考帧(曝光+反照率+水色)")]
        public static void RecalibrateToReference()
        {
            var sb = new System.Text.StringBuilder();

            // 太阳：仰角必须≈相机俯角+若干度，既保住水面碎闪（镜面反射仰角=像素俯角），
            // 又不至于把所有朝上面的 N·L 压到 0.24 —— 菜单54 曾把仰角压到 14°，岩石顶面因此全黑
            var sun = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (sun != null && sun.type == UnityEngine.LightType.Directional)
            {
                sun.intensity = 2.5f;
                sun.color = new Color(1f, 0.975f, 0.95f);   // 暖色直射乘到每个受光面上，是沙滩发橙再被 ACES 削成白的推手
                sun.transform.rotation = Quaternion.Euler(30f, 208f, 0f);
                sun.shadows = UnityEngine.LightShadows.Soft;
                EditorUtility.SetDirty(sun);
                sb.Append("sun(2.5,E30/208) ");
            } else sb.Append("sun:MISS ");

            // 环境光：参考帧实测受光岩 med(103,130,154) ≈ 阴影岩 med(88,122,151)，
            // 明暗差不到 1.2 倍 → 天光填充极强、几乎是平涂风格；
            // 旧值 0.22/0.27/0.335 在 ACES 下把阴影面压到 (30,41,53)，差 8 倍线性亮度，是"不像"的首要原因
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor    = new Color(0.500f, 0.580f, 0.680f);
            RenderSettings.ambientEquatorColor = new Color(0.440f, 0.500f, 0.470f);
            RenderSettings.ambientGroundColor  = new Color(0.290f, 0.270f, 0.230f);
            sb.Append("ambient=trilight-fill ");

            // 阴影距离：50m 会把远岸/树影整段裁掉
            var urp = QualitySettings.renderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset
                      ?? GraphicsSettings.renderPipelineAsset as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null)
            {
                var so = new SerializedObject(urp);
                var d = so.FindProperty("m_ShadowDistance");
                var r = so.FindProperty("m_MainLightShadowmapResolution");
                var c = so.FindProperty("m_ShadowCascadeCount");
                if (d != null) { d.floatValue = 140f; }
                if (r != null) { r.intValue = 2048; }
                if (c != null) { c.intValue = 3; }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
                sb.Append("shadowDist=140/2048/c3 ");
            } else sb.Append("urp:MISS ");

            // 反照率：脚本 SetColor 走线性空间；以下值全部经 Play 真实 Game 视图（含 ACES）取样校准
            var albedo = new System.Collections.Generic.Dictionary<string, Color>
            {
                { "Slate_BlueRock",    new Color(0.320f, 0.420f, 0.540f) },   // 参考帧实测受光 med(103,130,154)/阴影 med(88,122,151)：中调蓝灰，不是黑
                { "Stone",             new Color(0.310f, 0.400f, 0.510f) },
                { "Sand_Shore",        new Color(0.500f, 0.440f, 0.370f) },   // 近中性会把沙滩洗成蓝白；参考(180,166,154) 要暖调
                { "PondWall",          new Color(0.470f, 0.415f, 0.350f) },
                { "Ground_Lake",       new Color(0.280f, 0.340f, 0.190f) },   // 池底沙色：绿需反超红，否则折射出来的浅滩是灰的不是薄荷绿
                { "GrassGround",       new Color(0.120f, 0.250f, 0.125f) },
                { "GreenWall_Distant", new Color(0.022f, 0.250f, 0.100f) },   // 实测(52,149,82) vs 参考草带(26,116,77)：红要再砍一半以上才够饱和
                { "Hill_Distant",      new Color(0.340f, 0.420f, 0.460f) },   // 大气透视：蓝>绿（此值原先只改在 .mat 上，菜单一跑就回退）
                { "Ground_FarHaze",    new Color(0.150f, 0.245f, 0.215f) },   // 远处地面专用（与池底解耦）
                { "NormalTree_Leaves", new Color(0.300f, 0.520f, 0.215f) },   // 覆盖 glb 内嵌；逆光剪影靠环境光抬，不靠提反照率
                { "NormalTree_Bark",   new Color(0.170f, 0.110f, 0.075f) },
            };
            foreach (var kv in albedo)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/" + kv.Key + ".mat");
                if (mat == null) { sb.Append(kv.Key + ":MISS "); continue; }
                mat.SetColor("_BaseColor", kv.Value);
                EditorUtility.SetDirty(mat);
            }
            sb.Append("albedo x" + albedo.Count + " ");

            // 低多边形平涂：地形一律无光泽。Ground_Lake 原 _Smoothness 0.5 会在擦地视角把远处地面洗成淡青白带
            foreach (var mname in new[] { "Ground_Lake", "GrassGround", "Sand_Shore", "PondWall",
                                          "GreenWall_Distant", "Hill_Distant", "Ground_FarHaze",
                                          "NormalTree_Leaves", "NormalTree_Bark" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/" + mname + ".mat");
                if (mat != null && mat.HasProperty("_Smoothness"))
                {
                    mat.SetFloat("_Smoothness", 0f);
                    EditorUtility.SetDirty(mat);
                }
            }
            sb.Append("matte x9 ");

            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Water_Advanced.mat");
            if (water != null)
            {
                // 曝光从 0.55 抬到 1.15 后这三色过冲：实测水中 (79,205,219) vs 参考帧 (71,154,158)
                // / (78,158,157)，G/B 各强 25~30%；保持色相比例整体压 0.55
                water.SetColor("_DeepColor",    new Color(0.078f, 0.340f, 0.430f));
                water.SetColor("_ShallowColor", new Color(0.200f, 0.520f, 0.480f));
                water.SetColor("_WaterScatter", new Color(0.090f, 0.370f, 0.400f));
                water.SetColor("_SkyZenith",    new Color(0.100f, 0.280f, 0.400f));
                // 掠射反射取的就是这个值（shader 里 lerp(_SkyHorizon,_SkyZenith,reflDir.y)）；
                // 旧值 0.30/0.44/0.50 让低机位的大半水面（reflDir.y≈0）整体发黑发闷
                water.SetColor("_SkyHorizon",   new Color(0.550f, 0.700f, 0.850f));
                water.SetColor("_SunColor",     new Color(1f, 0.950f, 0.820f));
                water.SetColor("_FoamColor",    new Color(0.88f, 0.96f, 0.96f));
                SetIf(water, "_ScatterStrength", 0.45f);
                SetIf(water, "_Absorb", 0.85f);
                // 池子只有 ~1.3m 深，而 gap 是视深：1.9 的吸收系数把 depthMix 顶到 0.91，整池都读成深水、
                // 近岸反而比塘心还暗（参考帧正相反：近岸淡薄荷绿、塘心才转青）
                SetIf(water, "_Opacity", 0.76f);
                SetIf(water, "_AmbientStrength", 0.35f);
                // 透射光是近白色的，是红通道的主要污染源 → 压低才能保住青蓝饱和度
                SetIf(water, "_SunTransmission", 0.2f);
                // 透彻感：_Opacity 决定"看清湖底"的权重(1-_Opacity)，0.95 时只剩 5% → 像蒙了层色膜
                // _MaxUVOffset 是折射位移上限（占屏幕比例），0.12 会把湖底抹成一团糊
                SetIf(water, "_MaxUVOffset", 0.025f);
                SetIf(water, "_RefractStrength", 0.3f);
                // 碎闪：参考帧亮带实测 med(208,218,201)（近白），我们一路从 (69,149,171) 抬到 (95,177,175)
                // 才像样。真正的瓶颈不是高光强度，而是**掠射反射根本没建立起来**：
                //   _SunGloss 700 → pow(cosδ,700) 半高宽只有 ±1.8°，太阳高光被压成一条细线；
                //   _FresnelPow 6 → 水面俯角 10° 时 fres 才 0.33，要贴着地平线才趋近 1，
                //     于是画面里绝大部分水面都没在"照镜子"，天空反射根本吃不满。
                // 两者一起放宽后，中水面才出现成片的高亮反射场。
                // 不会退回根因 8：细节法线已是三 octave 且 _DetailScale 7.6，面元足够小，
                // 放宽波瓣只会让点变多变亮，不会连成一整块死白。
                SetIf(water, "_FresnelPow", 4f);      // 菜单40 会把这项塞回 6，必须在后面覆盖一次
                SetIf(water, "_SkyReflect", 1.0f);
                SetIf(water, "_SpecStrength", 2.0f);
                SetIf(water, "_SpecPower", 200f);
                SetIf(water, "_SunStrength", 2.2f);
                SetIf(water, "_SunGloss", 90f);
                // 细节法线：3 会把法线掀到近水平、菲涅尔饱和；0.35 又平得没有碎闪
                // _DetailScale 提到 7.6 + 强度 1.4：波纹更小更密，碎闪点才够密
                SetIf(water, "_DetailStrength", 1.4f);
                SetIf(water, "_DetailScale", 7.6f);
                SetIf(water, "_DepthDebug", 0f);
                EditorUtility.SetDirty(water);
                sb.Append("water=teal+glitter ");
            } else sb.Append("water:MISS ");

            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU48 " + sb + "\n");
        }

        static void SetIf(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

    // —— 49) 把 Scene 视图对到主相机（桥截图通道 = 带后处理的真实构图） ——
        [MenuItem("Tools/WaterProject/49 Scene视图对齐主相机")]
        public static void AlignSceneViewToMainCamera()
        {
            var cam = UnityEngine.GameObject.Find("Main Camera");
            string note = "MENU49 ";
            var sv = SceneView.lastActiveSceneView;
            if (cam == null || sv == null)
            {
                note += "cam or sceneview MISSING (sv=" + (sv == null ? "null" : "ok") + ")";
            }
            else
            {
                sv.orthographic = false;
                sv.rotation = cam.transform.rotation;
                sv.pivot = cam.transform.position;
                var mc = cam.GetComponent<Camera>();
                if (mc != null) sv.camera.fieldOfView = mc.fieldOfView;
                sv.size = 12f;
                // Scene 视图相机默认不跑后处理，桥截图会静默忽略 Volume —— 必须显式打开才是可测通道
                var scd = sv.camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (scd == null) scd = sv.camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                scd.renderPostProcessing = true;
                note += " sceneViewPost=on ";
                sv.Repaint();
                note += "pivot=" + cam.transform.position + " euler=" + cam.transform.eulerAngles;
            }
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), note + "\n");
        }

    // —— 50) 远景/荷叶布局落盘（本轮用桥手工改的部分，重建链跑完后必须再跑这个） ——
        [MenuItem("Tools/WaterProject/50 远景压平+荷叶分簇落盘")]
        public static void ApplyFarPlaneAndLilyLayout()
        {
            var sb = new System.Text.StringBuilder();

            // 远山：参考帧远景是"低缓台地"，没有尖峰；原锥体 Y/XZ 比≈0.6 会读成黑锥
            // 每项 = x,y,z, sx,sy,sz
            float[][] hills =
            {
                new float[]{ 33.95f,0f,  4.19f, 20.27f, 2.96f, 20.27f },
                new float[]{ 17.74f,0f, 29.31f, 14.88f, 3.78f, 14.88f },
                new float[]{  3.10f,0f, 33.70f, 15.79f, 2.97f, 15.79f },
                new float[]{-26.74f,0f, 24.86f, 17.95f, 3.44f, 17.95f },
                new float[]{-34.24f,0f,  3.52f, 15.08f, 2.75f, 15.08f },
                new float[]{-23.73f,0f,-28.72f, 14.60f, 4.13f, 14.60f },
                new float[]{ -4.35f,0f,-34.02f, 20.25f, 2.93f, 20.25f },
                new float[]{ 30.77f,0f,-20.53f, 18.83f, 2.56f, 18.83f },
            };
            for (int i = 0; i < hills.Length; i++)
            {
                var go = UnityEngine.GameObject.Find("DistantHill_" + i.ToString("00"));
                if (go == null) { sb.Append("H" + i + ":MISS "); continue; }
                var h = hills[i];
                go.transform.localPosition = new Vector3(h[0], h[1], h[2]);
                go.transform.localScale = new Vector3(h[3], h[4], h[5]);
                EditorUtility.SetDirty(go);
            }

            // 荷叶：y=0.022 埋在波体里会读成半透明描边，抬到 0.15（波峰之上）
            // 菜单52 改了盆底剖面后，原 r=11.5~13.1 的四株落到了干滩上，已按水陆界内收到 r≈9
            // 每项 = x,z,scale
            float[][] lily =
            {
                new float[]{ -6.2f,  2.6f, 0.16f }, new float[]{ -7.4f,  3.6f, 0.13f },
                new float[]{ -5.4f,  3.9f, 0.115f }, new float[]{ -6.9f,  4.8f, 0.15f },
                new float[]{ -5.8f,  5.4f, 0.10f }, new float[]{ -7.18f,  5.74f, 0.14f },
                new float[]{ -6.52f,  6.76f, 0.11f }, new float[]{ -6.39f,  6.19f, 0.125f },
                new float[]{ -4.6f, -4.8f, 0.14f }, new float[]{ -3.2f, -6.2f, 0.11f },
                new float[]{  5.5f,  2.0f, 0.15f }, new float[]{  6.8f,  3.4f, 0.11f },
            };
            for (int i = 0; i < lily.Length; i++)
            {
                var go = UnityEngine.GameObject.Find("SmallLily_" + i.ToString("00"));
                if (go == null) { sb.Append("L" + i + ":MISS "); continue; }
                var t = lily[i];
                go.transform.localPosition = new Vector3(t[0], 0.15f, t[1]);
                go.transform.localScale = new Vector3(t[2], t[2], t[2]);
                EditorUtility.SetDirty(go);
            }

            // 荷花原 scale 0.115-0.14 实测只有 ~10cm，而荷叶直径 3-4m → 完全看不见；按探针放大到 1.3-1.65
            float[][] lotus =
            {
                new float[]{ -6.6f,  3.2f, 1.5f }, new float[]{ -6.67f,  6.06f, 1.3f },
                new float[]{ -4.9f, -5.2f, 1.65f },
            };
            for (int i = 0; i < lotus.Length; i++)
            {
                var go = UnityEngine.GameObject.Find("SmallLotus_" + i.ToString("00"));
                if (go == null) { sb.Append("F" + i + ":MISS "); continue; }
                var t = lotus[i];
                go.transform.localPosition = new Vector3(t[0], 0.15f, t[1]);
                go.transform.localScale = new Vector3(t[2], t[2], t[2]);
                EditorUtility.SetDirty(go);
            }

            var ground = UnityEngine.GameObject.Find("Ground");
            if (ground != null)
            {
                ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(110f, 1f, 110f);   // 收窄地平线处天空盒下半球露出的灰带
                EditorUtility.SetDirty(ground);
                sb.Append("ground=110 ");
            } else sb.Append("ground:MISS ");

            // 绿墙顶缘网格有被拉长的退化 quad，画成淡青横带+斜向尖刺；A/B 实测去掉后层次更好。
            // 重造顶缘前停车；重造后把这里改回 y=0。
            var gw = UnityEngine.GameObject.Find("GreenWall");
            if (gw != null)
            {
                gw.transform.localPosition = new Vector3(0f, -500f, 0f);
                EditorUtility.SetDirty(gw);
                sb.Append("greenwall=parked ");
            } else sb.Append("greenwall:MISS ");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU50 " + sb + "\n");
        }

    // —— 51) 加距离雾（柔化地平线硬边 + 大气透视）——
    // 注意：URP 14 没有 Fog 的 VolumeComponent（Overrides 里只有 Tonemapping/ColorAdjustments 等），
    //       雾只能走 RenderSettings；且本项目水面 shader 不接雾，前景水面保持清晰。
        [MenuItem("Tools/WaterProject/51 加距离雾(柔化地平线)")]
        public static void ApplyDistanceFog()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = UnityEngine.FogMode.Exponential;
            // 雾色必须等于天空盒在地平线处的实际出色，否则"雾满"的远地仍会与天空撞出一条硬边：
            // sky = lerp(_HorizonColor(0.72,0.82,0.95), _ZenithColor(0.36,0.62,1.0), up*up=0.25) ≈ (0.63,0.77,0.96)
            RenderSettings.fogColor = new Color(0.63f, 0.77f, 0.96f);
            RenderSettings.fogDensity = 0.0045f;
            // Exponential: f=exp(-(d*density)^2) → 27m 处仅 1.5% 雾（池塘不受影响），200m 56%，550m 99.8%

            // 地平线最后一条硬边来自"地面平面边缘之外"的天空盒下半球 _GroundColor；
            // 让它等于雾色 → 地形/雾/天空同色，接缝消失。两者必须一处改、一起改。
            var sky = RenderSettings.skybox;
            if (sky != null)
            {
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", RenderSettings.fogColor);
                if (sky.HasProperty("_HorizonColor")) sky.SetColor("_HorizonColor", new Color(0.72f, 0.83f, 0.97f));
                EditorUtility.SetDirty(sky);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU51 fog=on exp density=0.0045 color=(0.63,0.77,0.96) skyGround=synced sky=" + (sky != null ? sky.name + "/" + sky.shader.name : "NULL") + "\n");
        }

    // —— 53) 道具重贴合地面：盆底改了剖面后，石头按新地面重新坐落 ——
        [MenuItem("Tools/WaterProject/53 石头重贴合地面(按实际网格采样)")]
        public static void ReseatPropsToGround()
        {
            var sb = new System.Text.StringBuilder();

            // 直接采样"场景里真实存在"的地面网格顶点做高度场。
            // 不复制菜单52/菜单24 的高度公式——水面与沙环的半径公式在多个菜单里已经漂移过，
            // 复制一份只会再制造一次不一致。
            string[] groundObjs = { "PondBasin", "BasinStep_00", "BasinStep_01", "BasinStep_02", "ShoreSandRing" };
            var gx = new List<float>(); var gy = new List<float>(); var gz = new List<float>();
            foreach (var n in groundObjs)
            {
                var go = UnityEngine.GameObject.Find(n);
                if (go == null) { sb.Append(n + ":MISS "); continue; }
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { sb.Append(n + ":NOMESH "); continue; }
                var mv = mf.sharedMesh.vertices;
                var ltw = go.transform.localToWorldMatrix;
                for (int i = 0; i < mv.Length; i++)
                {
                    Vector3 w = ltw.MultiplyPoint3x4(mv[i]);
                    gx.Add(w.x); gy.Add(w.y); gz.Add(w.z);
                }
            }
            sb.Append("samples " + gx.Count + " ");
            if (gx.Count == 0) { WriteProbe("MENU53 " + sb + " ABORT(no ground)\n"); return; }

            // 取覆盖范围内所有顶点的最高 y：盆底/台地/干沙滩谁在上面算谁
            System.Func<float, float, float> groundAt = (x, z) =>
            {
                float best = float.NegativeInfinity;
                for (int i = 0; i < gx.Count; i++)
                {
                    float dx = gx[i] - x, dz = gz[i] - z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > 1.6f) continue;                       // 1.26m 水平容差
                    if (gy[i] > best) best = gy[i];
                }
                return best;
            };

            int moved = 0, skipped = 0;
            var all = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in all)
            {
                if (go == null) continue;
                if (!(go.name.StartsWith("Boulder_") || go.name.StartsWith("SandBlob_")
                      || go.name.StartsWith("Pebble_"))) continue;
                var t = go.transform;
                float g = groundAt(t.position.x, t.position.z);
                if (float.IsNegativeInfinity(g)) { skipped++; continue; }

                var er = go.GetComponent<Renderer>();
                float ey = er != null ? er.bounds.extents.y : 0.3f;
                // 埋进地面 25% 的自身高度：既不悬空也不整个吞掉
                float want = g + ey * 0.75f;
                if (Mathf.Abs(want - t.position.y) < 0.01f) { skipped++; continue; }
                t.position = new Vector3(t.position.x, want, t.position.z);
                EditorUtility.SetDirty(go);
                moved++;
            }
            sb.Append("rocks moved " + moved + " kept " + skipped + " ");

            // 荷叶/荷花：漂在干地上就是错位，报出来（半径要按水陆界重排，不能瞎挪）
            foreach (var go in all)
            {
                if (go == null) continue;
                if (!(go.name.StartsWith("SmallLily_") || go.name.StartsWith("SmallLotus_"))) continue;
                float g = groundAt(go.transform.position.x, go.transform.position.z);
                if (float.IsNegativeInfinity(g)) continue;
                if (g > -0.04f)   // 地面已不低于水面 → 这株其实坐在沙滩上
                    sb.Append("DRY " + go.name + " r=" + Mathf.Sqrt(go.transform.position.x * go.transform.position.x + go.transform.position.z * go.transform.position.z).ToString("F1") + " g=" + g.ToString("F2") + " ");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU53 " + sb + "\n");
        }

        static void WriteProbe(string s)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);
        }

    // —— 54) 构图A 机位：站在近岸沙滩上俯看整池，水面占下 2/3、远岸绿坡占顶部（不是天空） ——
        [MenuItem("Tools/WaterProject/54 视频低机位(贴近水面)")]
        public static void ApplyVideoCameraLow()
        {
            var cam = UnityEngine.GameObject.Find("Main Camera");
            if (cam == null) { WriteProbe("MENU54 cam:MISS\n"); return; }
            // 俯角 20° 时画面顶缘在地平线上 6°，正好把 r19 起、高 3~4m 的远岸草坡收进顶部 1/4；
            // 机位退到 r≈15（水域边缘约 13.4）才能把近岸沙唇留在画面下缘，否则整幅都是水
            cam.transform.localPosition = new Vector3(-3.2f, 3.4f, -14.5f);
            cam.transform.localRotation = Quaternion.Euler(20f, 9f, 0f);
            var c = cam.GetComponent<Camera>();
            if (c != null) { c.fieldOfView = 52f; c.nearClipPlane = 0.1f; }
            EditorUtility.SetDirty(cam);

            // 太阳仰角归菜单48 单一来源：这里曾重复写 Euler(14,208)，把菜单48 的 E30 静默改回去，
            // 于是所有朝上面的 N·L 掉到 0.24、岩石顶全黑。几何耦合不等于要重复赋值。
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU54 camA=(-3.2,3.4,-14.5) Euler(20,9,0) fov52 sun=menu48\n");
        }

    // —— 55) 荷叶密集化：改成参考帧那样"多簇、小片、缀粉花" ——
        [MenuItem("Tools/WaterProject/55 荷叶密集化(多簇小片+粉花)")]
        public static void DensifyLilyClusters()
        {
            var sb = new System.Text.StringBuilder();
            var rng = new System.Random(20260922);

            // 探针实测：scale=0.15 时 SmallLily_03 直径 4.26m → 该 glb 在 scale=1 下约 28.4m。
            // 旧注释写的"9-14cm"是按错误单位估的，所以一直偏大；这里按目标直径反算 scale。
            const float lilyDiamAt1 = 28.4f, lotusDiamAt1 = 0.83f;

            var all = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            foreach (var go in all)
                if (go != null && (go.name.StartsWith("SmallLily_") || go.name.StartsWith("SmallLotus_")))
                    UnityEngine.Object.DestroyImmediate(go);

            string lilyPath = "Assets/Art/StylePacks/正式素材/Lotus/WaterLilies_GooglePoly.glb";
            string lotusPath = "Assets/Art/StylePacks/正式素材/Lotus/LotusFlower_SeanTarrant.glb";
            var lily = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lilyPath);
            var lotus = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lotusPath);
            if (lily == null) { WriteProbe("MENU55 lily glb MISSING\n"); return; }

            // 簇心：构图A 的主簇在"近半池"贴着相机，不是漂在塘心；相机在 (-3.2,3.4,-14.5) 朝 +z
            Vector2[] cen = { new Vector2(-2.6f, -5.6f), new Vector2(-8.4f, 1.2f),
                              new Vector2(5.8f, 3.4f), new Vector2(1.6f, -1.0f) };
            int idx = 0;
            for (int c = 0; c < cen.Length; c++)
            {
                int n = 13 + rng.Next(5);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rad = (float)rng.NextDouble() * 2.3f;
                    float d = 0.72f + (float)rng.NextDouble() * 0.62f;   // 目标直径 0.72~1.34m
                    float sc = d / lilyDiamAt1;
                    var inst = PrefabUtility.InstantiatePrefab(lily) as UnityEngine.GameObject;
                    if (inst == null) continue;
                    inst.name = "SmallLily_" + (idx++).ToString("00");
                    inst.transform.position = new Vector3(cen[c].x + Mathf.Cos(a) * rad, 0.055f,
                                                          cen[c].y + Mathf.Sin(a) * rad);
                    inst.transform.localScale = new Vector3(sc, sc, sc);
                    inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                }
            }
            sb.Append("pads " + idx + " ");

            if (lotus != null)
            {
                int fi = 0;
                for (int c = 0; c < cen.Length; c++)
                {
                    int n = 3 + rng.Next(2);
                    for (int i = 0; i < n; i++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float rad = (float)rng.NextDouble() * 1.5f;
                        float d = 0.30f + (float)rng.NextDouble() * 0.22f;  // 花 0.30~0.52m
                        float sc = d / lotusDiamAt1;
                        var inst = PrefabUtility.InstantiatePrefab(lotus) as UnityEngine.GameObject;
                        if (inst == null) continue;
                        inst.name = "SmallLotus_" + (fi++).ToString("00");
                        inst.transform.position = new Vector3(cen[c].x + Mathf.Cos(a) * rad, 0.065f,
                                                              cen[c].y + Mathf.Sin(a) * rad);
                        inst.transform.localScale = new Vector3(sc, sc, sc);
                        inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                    }
                }
                sb.Append("flowers " + fi + " ");
            } else sb.Append("lotus:MISS ");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU55 " + sb + " (lily scale=1 → 28.4m 实测反推；簇心移到近半池)\n");
        }

    // —— 56) 远岸草坡 + 灌木带上岸 + 荷/莲材质强制覆盖（对标构图A：顶部是绿岸，不是天空） ——
        [MenuItem("Tools/WaterProject/56 远岸草坡重构+荷花粉色覆盖")]
        public static void BuildFarBankAndOverrideFlora()
        {
            var sb = new System.Text.StringBuilder();

            // 1) 草坡剖面：埋入地下 → 草脚（约30°）→ 极缓的远坡 → 外沿（被雾吃掉，只为挡地平线）
            //    第一版内壁 15.6→19.4 抬 3.7m（近于垂直），画面顶部成了一道峡谷壁；
            //    且 11x/23x 高频起伏在平面着色下拍打成同心明暗条纹。改成推远 + 放缓 + 低次谐波。
            float[] rProf = { 20f, 24f, 34f, 60f };
            System.Func<float, float> Hedge = th =>
                1.70f + 0.60f * Mathf.Sin(th * 3f + 0.7f) + 0.35f * Mathf.Sin(th * 7f + 2.1f);
            System.Func<float, float, float> BankY = (r, th) =>
            {
                float h = Hedge(th);
                if (r <= rProf[1]) return Mathf.Lerp(-0.6f, h, Mathf.InverseLerp(rProf[0], rProf[1], r));
                if (r <= rProf[2]) return Mathf.Lerp(h, h + 2.4f, Mathf.InverseLerp(rProf[1], rProf[2], r));
                return Mathf.Lerp(h + 2.4f, h + 8.5f, Mathf.InverseLerp(rProf[2], rProf[3], r));
            };

            const int seg = 256;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int gs = 0; gs <= seg; gs++)
            {
                float th = Mathf.PI * 2f * gs / seg;
                float ct = Mathf.Cos(th), st = Mathf.Sin(th);
                verts.Add(new Vector3(ct * rProf[0], -0.6f, st * rProf[0]));
                verts.Add(new Vector3(ct * rProf[1], Hedge(th), st * rProf[1]));
                verts.Add(new Vector3(ct * rProf[2], Hedge(th) + 2.4f, st * rProf[2]));
                verts.Add(new Vector3(ct * rProf[3], Hedge(th) + 8.5f, st * rProf[3]));
                if (gs == seg) break;
                int a = gs * 4;
                for (int k = 0; k < 3; k++)
                {
                    int i0 = a + k, i1 = a + 4 + k, i2 = a + 5 + k, i3 = a + k + 1;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i0); tris.Add(i2); tris.Add(i3);
                }
            }
            var bankMesh = FacetedFrom(verts, tris);
            SaveMesh(bankMesh, "Assets/Art/Meshes/GreenWall.asset", "GreenWall");

            var bankMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/GreenWall_Distant.mat");
            var bank = GetOrCreate("GreenWall");
            var bmf = bank.GetComponent<MeshFilter>(); if (bmf == null) bmf = bank.AddComponent<MeshFilter>();
            var bmr = bank.GetComponent<MeshRenderer>(); if (bmr == null) bmr = bank.AddComponent<MeshRenderer>();
            bmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/GreenWall.asset");
            if (bankMat != null) bmr.sharedMaterial = bankMat;
            // 140m 阴影距离下整圈草坡会在沙滩上投出一道硬阴带；它只是平面着色的背景，不需要自阴影
            bmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bmr.receiveShadows = true;
            bank.transform.localPosition = Vector3.zero;   // 菜单50 把它停在 y=-500，这里放回
            sb.Append("bank seg" + seg + " y0 ");

            // 2) 树挪上草坡、缩成灌木：参考帧远岸是一排低矮树冠，不是逆光大树
            var trs = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None);
            int tn = 0;
            var rngT = new System.Random(20260923);
            foreach (var go in trs)
            {
                if (go == null || !go.name.StartsWith("Tree_ring_")) continue;
                var t = go.transform;
                if (t.childCount == 0) continue;
                var keep = t.GetChild(0);
                float th = Mathf.Atan2(keep.position.z, keep.position.x);
                float r = 30f + (float)rngT.NextDouble() * 20f;
                float sc = 0.42f + (float)rngT.NextDouble() * 0.22f;
                var target = new Vector3(Mathf.Cos(th) * r, BankY(r, th) - 0.25f, Mathf.Sin(th) * r);
                // 先改缩放再平移：keep.position 每次现算世界矩阵，所以父节点旋转/缩放都被自动算进 delta
                t.localScale = new Vector3(sc, sc, sc);
                t.position = t.position + (target - keep.position);
                // 仰角 30° + 阴影距离 140m ⇒ 每棵远树都在草坡上拖出一道长黑影，参考帧的远岸没有这种条纹。
                // 这个距离上它们只需要读成灌木剪影，不需要自投影
                foreach (var shR in t.GetComponentsInChildren<Renderer>(true))
                    shR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tn++;
            }
            sb.Append("trees " + tn + " ");

            // 3) 荷/莲材质覆盖：菜单48 的 albedo 字典只认自建 .mat，glb 内嵌材质它碰不到
            var lilyMat  = ForceMat("Lily_Green",  new Color(0.130f, 0.450f, 0.140f));
            // 饱和洋红 (0.90,0.26,0.50) 会被 ACES 的色相限制压成砖红；参考帧的花是浅粉，必须提亮降饱和才保得住
            var lotusMat = ForceMat("Lotus_Pink",  new Color(0.920f, 0.560f, 0.660f));
            int ln = 0, fn = 0, diag = 0;
            foreach (var go in trs)
            {
                if (go == null) continue;
                bool isLily = go.name.StartsWith("SmallLily_");
                bool isLotus = go.name.StartsWith("SmallLotus_");
                if (!isLily && !isLotus) continue;
                var pick = isLily ? lilyMat : lotusMat;
                foreach (var rr in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (diag < 6)
                    {
                        var old = rr.sharedMaterial;
                        sb.Append("[" + rr.name + "<" + (old == null ? "null" : old.name) + "] ");
                        diag++;
                    }
                    rr.sharedMaterial = pick;
                    if (isLily) ln++; else fn++;
                }
            }
            sb.Append("lilyRnd " + ln + " lotusRnd " + fn + " ");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU56 " + sb + "\n");
        }

        // 建/取一个纯色 URP/Lit 双面材质（glb 内嵌材质不可写，只能整只 Renderer 换掉）
        static Material ForceMat(string assetName, Color c)
        {
            string p = "Assets/Art/Materials/" + assetName + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(lit);
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // 荷叶背面是黄色胶层，双面+同色一并解决
            EditorUtility.SetDirty(m);
            return m;
        }

    // —— 58) 沿岸补撒圆润小卵石：参考帧两岸是"大量小圆石成簇"，我们只有稀疏大棱角石 ——
        [MenuItem("Tools/WaterProject/58 沿岸补撒圆润小卵石")]
        public static void ScatterShorePebbles()
        {
            var rng = new System.Random(202609241);
            float S(float th) => ShoreR(th) * 0.6f;

            // 1) 两颗圆润卵石网格：同一个球机，但 lump 把噪声压到 0.3，读成被水磨圆的小石
            var pebbles = new Mesh[2];
            for (int v = 0; v < 2; v++)
            {
                string p = "Assets/Art/Meshes/Pebble_0" + (v + 1) + ".asset";
                var built = BuildFacetedRock(3 + v, 0.30f);
                var asset = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                if (asset == null)
                {
                    built.name = "Pebble_0" + (v + 1);
                    AssetDatabase.CreateAsset(built, p);
                    asset = built;
                }
                else
                {
                    asset.Clear();
                    asset.vertices = built.vertices;
                    asset.normals = built.normals;
                    asset.triangles = built.triangles;
                    asset.RecalculateBounds();
                    EditorUtility.SetDirty(asset);
                }
                pebbles[v] = asset;
            }
            AssetDatabase.SaveAssets();

            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Slate_BlueRock.mat");
            if (mat == null) { WriteProbe("MENU58 mat MISSING\n"); return; }

            // 2) 清掉上一轮（菜单39 的清除循环也认 Pebble_，两边都能保证可重复执行）
            foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None))
                if (go != null && go.name.StartsWith("Pebble_")) UnityEngine.Object.DestroyImmediate(go);

            // 3) 15 簇沿水线成簇散布；y 先给个近似值，真正的贴合交给菜单53（它采样实际地面网格）
            //    尺度：0.055~0.175 时远岸卵石只有 1~3 px，等于没撒。参考帧的卵石是能数出来的块
            int made = 0;
            for (int cl = 0; cl < 15; cl++)
            {
                float baseTh = Mathf.PI * 2f * cl / 15f + (float)rng.NextDouble() * 0.35f;
                int cnt = 8 + rng.Next(7);
                for (int k = 0; k < cnt; k++)
                {
                    float th = baseTh + (float)(rng.NextDouble() - 0.5) * 0.85f;
                    float rIn2 = Mathf.Max(12f, S(th));
                    // 偏向干滩一侧：参考帧卵石主要铺在浪线以上的滩面上
                    float r = rIn2 + (float)(rng.NextDouble() - 0.38) * 3.4f;
                    made += PlaceBoulder(pebbles, mat, rng, "Pebble_", made,
                        new Vector3(Mathf.Cos(th) * r, 0.04f, Mathf.Sin(th) * r), 0.16f, 0.36f);
                }
            }
            // 相机 yaw 9° → 视线朝 +z，也就是 θ≈π/2 那一侧的岸。额外补 5 簇，
            // 否则密度全撒在画面外的近岸与两侧，远岸看着仍然光秃
            for (int cl = 0; cl < 5; cl++)
            {
                float baseTh = Mathf.PI * 0.5f + (cl - 2) * 0.30f + (float)rng.NextDouble() * 0.12f;
                int cnt = 9 + rng.Next(6);
                for (int k = 0; k < cnt; k++)
                {
                    float th = baseTh + (float)(rng.NextDouble() - 0.5) * 0.42f;
                    float rIn2 = Mathf.Max(12f, S(th));
                    float r = rIn2 + (float)rng.NextDouble() * 3.2f - 0.7f;
                    made += PlaceBoulder(pebbles, mat, rng, "Pebble_", made,
                        new Vector3(Mathf.Cos(th) * r, 0.04f, Mathf.Sin(th) * r), 0.16f, 0.40f);
                }
            }
            // 浅水里再撒几堆，参考帧水下的石头是读出清澈感的关键线索之一
            for (int cl = 0; cl < 5; cl++)
            {
                float baseTh = Mathf.PI * 2f * (cl + 0.3f) / 5f + 1.1f;
                for (int k = 0; k < 6; k++)
                {
                    float th = baseTh + (float)(rng.NextDouble() - 0.5) * 0.7f;
                    float r = 5.5f + cl * 1.3f + (float)(rng.NextDouble() - 0.5) * 1.4f;
                    made += PlaceBoulder(pebbles, mat, rng, "Pebble_", made,
                        new Vector3(Mathf.Cos(th) * r, -0.22f, Mathf.Sin(th) * r), 0.12f, 0.26f);
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU58 pebbles " + made + "\n");
        }

    // —— 52) 凹陷沙坑重构：一个平底盆读不出凹陷，改用"锥形盆底 + 同心台地 + 湿沙分色" ——
        [MenuItem("Tools/WaterProject/52 凹陷沙坑重构(锥形盆底+同心台地)")]
        public static void RebuildBasinDepression()
        {
            var sb = new System.Text.StringBuilder();
            const string basinPath = "Assets/Art/Meshes/PondBasin.asset";

            // ---- 湿沙 / 潮沙材质：按深度分色是"读出凹陷"最强的一条线索 ----
            Material WetOf(string name, Color linear)
            {
                string p = "Assets/Art/Materials/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                    AssetDatabase.CreateAsset(m, p);
                }
                m.SetColor("_BaseColor", linear);
                m.SetFloat("_Smoothness", 0.08f);
                m.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(m);
                return m;
            }
            var wet  = WetOf("Sand_Wet",  new Color(0.280f, 0.300f, 0.175f));   // 折射层会把沙底大量混进水色：红>绿 的暖沙会把浅滩拖成灰，绿要反超红
            var damp = WetOf("Sand_Damp", new Color(0.360f, 0.400f, 0.235f));   // 中间台地：半湿
            sb.Append("mats ");

            // ---- 1) 锥形盆底：中心 -1.30 → 岸边略低于水面，指数 1.55 让坡度向岸最大、盆底趋平 ----
            int seg = 128, rings = 34;
            // Rim 必须低于水面（水面在 y≈+0.02）：原先取 0.02 正好与水面共面，水一变透，
            // 盆体外缘就破水而出，在近岸糊出一道灰白"水泥台阶"
            float Depth = 1.30f, Rim = -0.07f, Pow = 1.55f;
            var pos = new List<Vector3>();
            var tri = new List<int>();
            var vq  = new List<Vector3>();
            for (int gr = 0; gr <= rings; gr++)
            {
                float t = (float)gr / rings;
                for (int gs = 0; gs <= seg; gs++)
                {
                    float th = Mathf.PI * 2f * gs / seg;
                    float rEdge = Mathf.Max(12f, ShoreR(th) * 0.6f) - 0.05f;   // 与水面网格同一岸线
                    float r = Mathf.Lerp(0.3f, rEdge, t);
                    // 等深线随角度摆动：正圆台地会读成"靶心"，挖出来的沙坑不该是同心圆
                    float te = Mathf.Clamp01(t + WobA(th, 1.7f + t * 2f) * 0.85f);
                    float y = -Depth + (Depth + Rim) * Mathf.Pow(te, Pow);
                    // 盆底起伏：靠岸处收敛到 0，避免与 ShoreSandRing 抢位
                    float n = (Hash11(gs * 7.1f + gr * 13.7f) - 0.5f) * 0.11f * Mathf.Pow(t, 0.6f);
                    vq.Add(new Vector3(Mathf.Cos(th) * r, y + n, Mathf.Sin(th) * r));
                }
            }
            for (int gr = 0; gr < rings; gr++)
                for (int gs = 0; gs < seg; gs++)
                {
                    int a = gr * (seg + 1) + gs, b = a + 1;
                    int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                    tri.Add(a); tri.Add(c); tri.Add(b);
                    tri.Add(b); tri.Add(c); tri.Add(d);
                }
            var basin = FacetedFrom(vq, tri);          // 非索引化 → 每面独立法线 → 平面着色台阶感
            sb.Append("basin v" + basin.vertexCount + " avgNrmY " + AvgNormalY(basin).ToString("F3") + " ");
            SaveMesh(basin, basinPath, "PondBasin");

            var bgo = GetOrCreate("PondBasin");
            var bmf = bgo.GetComponent<MeshFilter>(); if (bmf == null) bmf = bgo.AddComponent<MeshFilter>();
            var bmr = bgo.GetComponent<MeshRenderer>(); if (bmr == null) bmr = bgo.AddComponent<MeshRenderer>();
            bmf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(basinPath);
            bmr.sharedMaterial = wet;
            bmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 水下不需要投影，避免自阴影噪点
            bmr.receiveShadows = true;
            bgo.transform.localPosition = Vector3.zero;
            bgo.transform.localScale = Vector3.one;

            // ---- 2) 同心台地：三圈略高于盆底的窄环带，制造"一层一层往下陷"的硬边 ----
            float[][] bands = { new float[]{0.34f,0.46f}, new float[]{0.56f,0.68f}, new float[]{0.76f,0.88f} };
            var stepMeshes = new List<Mesh>();
            for (int k = 0; k < bands.Length; k++)
            {
                float t0 = bands[k][0], t1 = bands[k][1];
                var sp = new List<Vector3>(); var st = new List<int>(); var sv = new List<Vector3>();
                int srings = 7;
                for (int gr = 0; gr <= srings; gr++)
                {
                    float t = Mathf.Lerp(t0, t1, (float)gr / srings);
                    for (int gs = 0; gs <= seg; gs++)
                    {
                        float th = Mathf.PI * 2f * gs / seg;
                        float w = WobA(th, 1.7f + t * 2f);
                        float tw = Mathf.Clamp01(t + w * 0.85f);
                        float tband = Mathf.Clamp01(t + w * 0.85f);
                        // 台地自身外缘略高，形成向内的陡坎（凹陷感主要来自这一道坎的明暗）
                        // 再压到原来的 1/4：水一透，每圈台地的外缘都是一次深度突变，
                        // 在近岸画出一条几乎笔直的分界线；凹陷已由锥形盆底承担，台地只需若隐若现
                        float y = -Depth + (Depth + Rim) * Mathf.Pow(tw, Pow) + 0.010f + 0.016f * tband;
                        float rEdge = Mathf.Max(12f, ShoreR(th) * 0.6f) - 0.05f;
                        float r = Mathf.Lerp(0.3f, rEdge, tband) * (1f + 0.012f * (Hash11(gs + k * 31f) - 0.5f));
                        sv.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                    }
                }
                for (int gr = 0; gr < srings; gr++)
                    for (int gs = 0; gs < seg; gs++)
                    {
                        int a = gr * (seg + 1) + gs, b = a + 1;
                        int c = (gr + 1) * (seg + 1) + gs, d = c + 1;
                        st.Add(a); st.Add(c); st.Add(b);
                        st.Add(b); st.Add(c); st.Add(d);
                    }
                var sm = FacetedFrom(sv, st);
                string path = "Assets/Art/Meshes/BasinStep_0" + k + ".asset";
                SaveMesh(sm, path, "BasinStep_0" + k);
                var sgo = GetOrCreate("BasinStep_0" + k);
                var smf = sgo.GetComponent<MeshFilter>(); if (smf == null) smf = sgo.AddComponent<MeshFilter>();
                var smr = sgo.GetComponent<MeshRenderer>(); if (smr == null) smr = sgo.AddComponent<MeshRenderer>();
                smf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                smr.sharedMaterial = damp;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = true;
                sgo.transform.localPosition = Vector3.zero;
                sgo.transform.localScale = Vector3.one;
                stepMeshes.Add(sm);
            }
            sb.Append("steps x" + stepMeshes.Count + " ");

            // ---- 3) 退役旧件：平底盆下沉当兜底、45°窄池壁关掉（它会盖住新的缓坡读感）----
            var old = UnityEngine.GameObject.Find("PondFloor");
            if (old != null) { old.transform.localPosition = new Vector3(0f, -1.9f, 0f); EditorUtility.SetDirty(old); sb.Append("floor=lowered "); }
            var wall = UnityEngine.GameObject.Find("PondWall");
            if (wall != null)
            {
                var wm = wall.GetComponent<MeshRenderer>();
                if (wm != null) wm.enabled = false;
                sb.Append("wall=off ");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"),
                "MENU52 " + sb + " basin depth=" + Depth + " pow=" + Pow + " (旧 PondWall 关闭 / PondFloor 沉到 -1.9)\n");
        }

    // —— 57) 按已验证依赖顺序整链重建（省掉每个菜单一次 MCP 往返；失败点看 probe 的 STEP 行） ——
        [MenuItem("Tools/WaterProject/57 整链重建(按验证顺序)")]
        public static void RebuildAllInOrder()
        {
            var steps = new System.Collections.Generic.KeyValuePair<string, System.Action>[]
            {
                new System.Collections.Generic.KeyValuePair<string, System.Action>("21 沙丘环湖", BuildDuneHollowScape),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("22 对标修正", AlignToVideo),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("24 浅碗沙滩", RebuildShallowBowlBeach),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("52 凹陷沙坑", RebuildBasinDepression),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("46 地面绕序", FixGroundMeshWinding),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("36 水域网格", RebuildWaterDiskFixed),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("38 照明基线", RestoreRc9LightingBaseline),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("39 巨石群", RebuildReferenceBoulders),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("58 小卵石", ScatterShorePebbles),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("53 石头贴合", ReseatPropsToGround),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("40 rc4水基准", ApplyRc4WaterBaseline),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("41 天空环境光", ApplyBrightSkyGradientAmbient),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("42 水色", TuneWaterToReferenceFrame),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("45 俯视机位", AlignToReferenceFrames),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("47 Volume", ApplyGrasslandsVolume),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("48 曝光反照率", RecalibrateToReference),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("50 远景压平", ApplyFarPlaneAndLilyLayout),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("51 距离雾", ApplyDistanceFog),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("55 荷叶簇", DensifyLilyClusters),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("56 远岸草坡", BuildFarBankAndOverrideFlora),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("54 构图A机位", ApplyVideoCameraLow),
                new System.Collections.Generic.KeyValuePair<string, System.Action>("23 重挂材质", RebindAllMaterials),
            };
            WriteProbe("MENU57 begin (" + steps.Length + " steps)\n");
            for (int i = 0; i < steps.Length; i++)
            {
                WriteProbe("MENU57 STEP " + i + " " + steps[i].Key + "\n");
                try { steps[i].Value(); }
                catch (System.Exception e) { WriteProbe("MENU57 FAIL " + steps[i].Key + " :: " + e.Message + "\n"); }
            }
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            WriteProbe("MENU57 done\n");
        }

        // 非索引化：每个三角形独立顶点，RecalculateNormals 即得逐面法线（平面着色）；顺带把绕序统一成朝上
        static Mesh FacetedFrom(List<Vector3> src, List<int> idx)
        {
            var pos = new List<Vector3>(idx.Count);
            for (int i = 0; i + 2 < idx.Count; i += 3)
            {
                Vector3 a = src[idx[i]], b = src[idx[i + 1]], c = src[idx[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                // 朝下就换一对顶点，保证从上方看是正面（否则会被背面剔除整片消失）
                if (n.y < 0f) { pos.Add(a); pos.Add(c); pos.Add(b); }
                else          { pos.Add(a); pos.Add(b); pos.Add(c); }
            }
            var m = new Mesh();
            if (pos.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(pos);
            var order = new int[pos.Count];
            for (int i = 0; i < pos.Count; i++) order[i] = i;
            m.SetTriangles(order, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // 稳定哈希（同一 seed 每次生成同样的起伏，保证可复现）
        static float Hash11(float v)
        {
            v = Mathf.Sin(v * 12.9898f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        // 平滑角向扰动：三组正弦、相位来自稳定哈希
        // 用途：让等深线与台地边界不再是正圆（正圆读成"靶心"，不像挖出来的沙坑）
        static float WobA(float th, float seed)
        {
            float p1 = Hash11(seed * 3.1f) * 6.2832f;
            float p2 = Hash11(seed * 7.7f) * 6.2832f;
            float p3 = Hash11(seed * 13.3f) * 6.2832f;
            return 0.050f * Mathf.Sin(2f * th + p1) + 0.032f * Mathf.Sin(5f * th + p2) + 0.019f * Mathf.Sin(9f * th + p3);
        }

        static void SaveMesh(Mesh m, string path, string name)
        {
            m.name = name;
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old == null) AssetDatabase.CreateAsset(m, path);
            else { WriteMeshFrom(old, m); Object.DestroyImmediate(m); }
        }

        static UnityEngine.GameObject GetOrCreate(string name)
        {
            var go = UnityEngine.GameObject.Find(name);
            if (go == null) go = new UnityEngine.GameObject(name);
            return go;
        }
    }
}
#endif