// WaterProjectSetup.cs — 编辑器一键设置（通过 MCP runMenuItem 调用）
// 收录：URP 管线资产创建/默认规格设置、Opaque/Depth 纹理开启、水面材质创建
#if UNITY_EDITOR
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

        [MenuItem("Tools/WaterProject/04 生成高细分水体网格(160x160)")]
        public static void CreateHighSegWaterMesh()
        {
            const int SEG = 160;          // 段数：600m / 160 = 3.75m/格
            const float SIZE = 600f;
            string path = "Assets/Art/Meshes/LakeWaterMesh.asset";
            if (!Directory.Exists("Assets/Art/Meshes")) Directory.CreateDirectory("Assets/Art/Meshes");

            var mesh = new Mesh { name = "LakeWater_HighSeg" };
            int n = SEG + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            var tris = new int[SEG * SEG * 6];
            int vi = 0, ti = 0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // 平面位于 XZ，中心在原点
                    verts[vi] = new Vector3((x / (float)SEG - 0.5f) * SIZE, 0f, (y / (float)SEG - 0.5f) * SIZE);
                    uvs[vi] = new Vector2(x / (float)SEG, y / (float)SEG);
                    vi++;
                }
            }
            for (int y = 0; y < SEG; y++)
            {
                for (int x = 0; x < SEG; x++)
                {
                    int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var water = GameObject.Find("Water");
            if (water != null)
            {
                var mf = water.GetComponent<MeshFilter>();
                if (mf != null) { mf.sharedMesh = mesh; AssetDatabase.SaveAssets(); }
                Debug.Log($"[WaterProject] 高细分网格已应用到 Water：{SEG}x{SEG} 段，{mesh.vertexCount} 顶点");
            }
            else
            {
                Debug.Log($"[WaterProject] 网格已生成 {path}（未找到 Water 对象，未应用）");
                AssetDatabase.SaveAssets();
            }
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/WaterProject/05 近距离特写水景视角 (Scene View)")]
        public static void FrameSceneViewClose()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.orthographic = false;
            // 贴近水面低角度，展现波浪/曲面细分级配与日光闪烁
            sv.LookAt(new Vector3(0f, 0.6f, 0f), Quaternion.Euler(-2f, 180f, 0f), 6f);
            sv.Repaint();
            Debug.Log("[WaterProject] SceneView framed close-up.");
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

        [MenuItem("Tools/WaterProject/02 对齐水景视角 (Scene View)")]
        public static void FrameSceneView()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.orthographic = false;
            // 相机对准湖面原点：从 -Z 侧高处俯瞰
            sv.LookAt(new Vector3(0f, 1f, 0f), Quaternion.Euler(16f, 180f, 0f), 26f);
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
                mat.SetColor("_ShallowColor", new Color(0.14f, 0.50f, 0.42f, 1f));
                mat.SetColor("_DeepColor", new Color(0.03f, 0.18f, 0.19f, 1f));
                mat.SetFloat("_SunStrength", 0.5f);
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