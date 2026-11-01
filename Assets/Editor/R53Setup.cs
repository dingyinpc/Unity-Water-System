// R53Setup.cs — R53 逐项可视化：一次只输出一个中间量，看谁在域边界"一刀切"
//
// 背景：水面那条硬边（step≈120 灰阶 / width≈60px / ratio≈1.8）已经排除：
//   阴影、海底、纯深度渐变、颜色混色。剩下的怀疑对象是**水色链路里的某一个中间量**。
//   → 逐个把它单独输出成灰度，用 Tools/R51/r51_edge_probe.py 锁定同一条线量 ratio，
//     **ratio 最大的那一项就是真凶**。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R53Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void SetDebug(float v, string tag)
        {
            var seen = new HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_DebugMode")) continue;
                m.SetFloat("_DebugMode", v);
                EditorUtility.SetDirty(m);
                n++;
            }
            P($"MENU R53/{tag} _DebugMode={v}（{n} 个材质）\n");
        }

        [MenuItem("Tools/R53/90 输出=sandMix")]     public static void M4() => SetDebug(4f, "90");
        [MenuItem("Tools/R53/91 输出=depthMix")]    public static void M5() => SetDebug(5f, "91");
        [MenuItem("Tools/R53/92 输出=水色本体")]     public static void M6() => SetDebug(6f, "92");
        [MenuItem("Tools/R53/93 输出=菲涅尔")]       public static void M7() => SetDebug(7f, "93");
        [MenuItem("Tools/R53/94 输出=雾前亮度")]     public static void M8() => SetDebug(8f, "94");
        [MenuItem("Tools/R53/95 输出=雾前颜色")]     public static void M9() => SetDebug(9f, "95");
        [MenuItem("Tools/R53/99 恢复(关调试)")]      public static void M0() => SetDebug(0f, "99");

        // ═════════ R53/96：把地面高度图从 512m 扩到 2048m ═════════
        // 根因（R53 实测）：`depthMix` 在域边界上的过渡宽度是 **0 像素**（逐项可视化里唯一"一刀切"的项），
        //   而它由 vd 决定 ⇒ vd 在 512m 足迹边界上断掉。
        //   边界之所以存在，只是因为**高度图只烘了 512m**；而 `IsletTerrain.LandHeight` 是解析函数，
        //   全世界都有定义。⇒ 直接把高度图烘到 2048m，边界就跑到画面外，不需要任何"过渡补丁"。
        [MenuItem("Tools/R53/96 高度图扩到 2048m(消除域边界)")]
        public static void BakeBig()
        {
            if (Application.isPlaying) { P("R53/96 REFUSED: 先退 Play\n"); return; }

            const float R = 2048f;          // 覆盖范围（米）
            const int N = 1025;             // 分辨率（≤2048 时约 2m/texel）
            const float HMin = -8f, HMax = 6f;

            var tex = new Texture2D(N, N, TextureFormat.R16, false, true)
            { name = "Islet_FloorH_R16_2k", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var raw = new ushort[N * N];
            float stepM = R / (N - 1);
            float x0 = -R * 0.5f, z0 = -R * 0.5f;
            for (int zi = 0; zi < N; zi++)
            {
                float wz = z0 + zi * stepM;
                for (int xi = 0; xi < N; xi++)
                {
                    float wx = x0 + xi * stepM;
                    float y = IsletTerrain.LandHeight(wx, wz);
                    raw[zi * N + xi] = (ushort)(Mathf.Clamp01((y - HMin) / (HMax - HMin)) * 65535f);
                }
            }
            tex.SetPixelData(raw, 0);
            tex.Apply(false, false);

            string fp = "Assets/Art/Textures/Islet/Islet_FloorH_R16_2k.asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(fp);
            if (old != null) AssetDatabase.DeleteAsset(fp);
            AssetDatabase.CreateAsset(tex, fp);
            AssetDatabase.SaveAssets();

            var water = GameObject.Find("IsletWater");
            if (water == null) { P("R53/96 REFUSED: 找不到 IsletWater\n"); return; }
            var b = water.GetComponent<WaterProject.Presentation.IsletFloorHBinder>();
            if (b == null) { P("R53/96 REFUSED: 没有 IsletFloorHBinder\n"); return; }

            // 记原值（只用 EditorPrefs 存不了资产引用，所以原纹理名写进探针）
            if (!EditorPrefs.HasKey("R53.rect.x"))
            {
                EditorPrefs.SetFloat("R53.rect.x", b.rect.x);
                EditorPrefs.SetFloat("R53.rect.y", b.rect.y);
                EditorPrefs.SetFloat("R53.rect.z", b.rect.z);
                P($"R53/96 原 rect={b.rect} 原贴图={(b.floorH != null ? b.floorH.name : "null")}\n");
            }

            b.floorH = AssetDatabase.LoadAssetAtPath<Texture2D>(fp);
            b.rect = new Vector4(x0, z0, R, HMin);
            b.heightMax = HMax;
            b.Bind();
            EditorUtility.SetDirty(b);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R53/96 已换成 2k 高度图 rect=({x0},{z0},{R},{HMin})  res={N}\n");
        }

        [MenuItem("Tools/R53/96b 高度图还原(512m)")]
        public static void BakeBigRevert()
        {
            var water = GameObject.Find("IsletWater");
            var b = water != null ? water.GetComponent<WaterProject.Presentation.IsletFloorHBinder>() : null;
            if (b == null) { P("R53/96b REFUSED\n"); return; }
            var small = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Islet/Islet_FloorH_R16.asset");
            if (small == null) { P("R53/96b 找不到原高度图资产\n"); return; }
            b.floorH = small;
            b.rect = new Vector4(EditorPrefs.GetFloat("R53.rect.x", -256f),
                                 EditorPrefs.GetFloat("R53.rect.y", -120f),
                                 EditorPrefs.GetFloat("R53.rect.z", 512f), -8f);
            b.heightMax = 6f;
            b.Bind();
            EditorUtility.SetDirty(b);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R53/96b 已还原 512m 高度图 rect={b.rect}\n");
        }
    }
}
#endif
