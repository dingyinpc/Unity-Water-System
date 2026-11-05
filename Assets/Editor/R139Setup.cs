// R139Setup.cs — R139 悬崖"光板灰薄饼"
//
// 实测：Ocean_Cliff.mat 的 _BaseMap 是空槽（fileID 0）⇒ 整面崖体是**纯色**，没有任何表面细节，
// 只剩几何台阶 + 光照 ⇒ 读成"一摞灰色薄饼"。
// 90  接上玄武岩 albedo + normal（平铺按崖体尺寸换算）
// 90b 还原
// 91  只接 albedo / 91b 只接 normal（定位谁在起作用）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R139Setup
    {
        const string Mat = "Assets/Art/Materials/Islet/Islet_Rock_Splat.mat";
        const string Alb = "Assets/Art/Textures/AI/Proc/Rock_Basalt_AI_albedo.png";
        const string Nrm = "Assets/Art/Textures/AI/Proc/Rock_Basalt_AI_normal.png";
        // 崖体约 579×484 m；让一格约 10m ⇒ 平铺 ~58×48
        static readonly Vector2 Tiling = new Vector2(58f, 48f);

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static Material M()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) P("R139 REFUSED: 无 Islet_Rock_Splat.mat\n");
            return m;
        }

        static void Save(Material m)
        {
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }

        [MenuItem("Tools/R139/90 悬崖=接上玄武岩(albedo+normal)")]
        public static void Both()
        {
            var m = M(); if (m == null) return;
            var a = AssetDatabase.LoadAssetAtPath<Texture2D>(Alb);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(Nrm);
            if (a != null) { m.SetTexture("_BaseMap", a); m.SetTextureScale("_BaseMap", Tiling); }
            if (n != null) { m.SetTexture("_BumpMap", n); m.SetTextureScale("_BumpMap", Tiling); m.SetFloat("_BumpScale", 1.0f); }
            m.SetFloat("_Smoothness", 0.12f);
            Save(m);
            P($"R139/90 悬崖 albedo={(a != null)} normal={(n != null)} tiling={Tiling}\n");
        }

        [MenuItem("Tools/R139/91 悬崖=只接 albedo")]
        public static void OnlyAlbedo()
        {
            var m = M(); if (m == null) return;
            var a = AssetDatabase.LoadAssetAtPath<Texture2D>(Alb);
            if (a != null) { m.SetTexture("_BaseMap", a); m.SetTextureScale("_BaseMap", Tiling); }
            m.SetTexture("_BumpMap", null);
            Save(m);
            P("R139/91 只 albedo\n");
        }

        [MenuItem("Tools/R139/91b 悬崖=只接 normal")]
        public static void OnlyNormal()
        {
            var m = M(); if (m == null) return;
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(Nrm);
            m.SetTexture("_BaseMap", null);
            if (n != null) { m.SetTexture("_BumpMap", n); m.SetTextureScale("_BumpMap", Tiling); m.SetFloat("_BumpScale", 1.0f); }
            Save(m);
            P("R139/91b 只 normal\n");
        }

        [MenuItem("Tools/R139/90b 悬崖=还原(空槽)")]
        public static void Revert()
        {
            var m = M(); if (m == null) return;
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_BumpMap", null);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureScale("_BumpMap", Vector2.one);
            Save(m);
            P("R139/90b 已还原\n");
        }

        [MenuItem("Tools/R139/93 探针: 机位附近的大物体")]
        public static void ProbeBig()
        {
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            var sb = new System.Text.StringBuilder("R139/93 机位附近大物体\n");
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var b = r.bounds;
                float d = Vector3.Distance(b.center, eye);
                if (d > 600f) continue;
                float size = b.size.magnitude;
                if (size < 60f) continue;
                var m = r.sharedMaterial;
                sb.Append($"  {r.gameObject.name,-26} size=({b.size.x:F0},{b.size.y:F0},{b.size.z:F0})")
                  .Append($" d={d:F0}m mat={(m != null ? m.name : "null")}")
                  .Append($" shader={(m != null && m.shader != null ? m.shader.name : "?")}\n");
            }
            P(sb.ToString());
        }


        static void SetR(string key, float v, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_Rock_Splat.mat");
            if (m == null) { P("R139 REFUSED\n"); return; }
            m.SetFloat(key, v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            P($"R139 {tag}: {key}={v:F2}\n");
        }

        [MenuItem("Tools/R139/94 层理起伏=开(0.5)")]
        public static void Wob5() => SetR("_StrataWobble", 0.5f, "94");

        [MenuItem("Tools/R139/94b 层理起伏=开(1.0)")]
        public static void Wob10() => SetR("_StrataWobble", 1f, "94b");

        [MenuItem("Tools/R139/94c 层理起伏=关(旧)")]
        public static void Wob0() => SetR("_StrataWobble", 0f, "94c");

        [MenuItem("Tools/R139/95 层理强度=0.5")]
        public static void Amt5() => SetR("_StrataAmt", 0.5f, "95");

        [MenuItem("Tools/R139/95b 层理强度=1.0(旧)")]
        public static void Amt10() => SetR("_StrataAmt", 1f, "95b");

        [MenuItem("Tools/R139/96 探针")]
        public static void P2()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet/Islet_Rock_Splat.mat");
            if (m == null) return;
            P($"R139/96 wob={m.GetFloat("_StrataWobble"):F2} amt={m.GetFloat("_StrataAmt"):F2}\n");
        }

        [MenuItem("Tools/R139/92 探针")]
        public static void Probe()
        {
            var m = M(); if (m == null) return;
            var a = m.GetTexture("_BaseMap");
            var n = m.GetTexture("_BumpMap");
            P($"R139/92 cliff base={(a != null ? a.name : "null")} bump={(n != null ? n.name : "null")} "
              + $"scale={m.GetTextureScale("_BaseMap")} smooth={m.GetFloat("_Smoothness"):F2}\n");
        }
    }
}
#endif
