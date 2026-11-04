// R120Setup.cs — R120：棕榈几何探针（决定 AI 叶片贴图能不能直接用）
#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R120Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        [MenuItem("Tools/R120/90 棕榈探针(网格/UV/材质)")]
        public static void Probe()
        {
            var scene = EditorSceneManager.GetActiveScene();
            P($"R120 PROBE 场景={scene.name}\n");

            int shown = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                string mn = m.name.ToLower();
                string rn = r.name.ToLower();
                if (!(mn.Contains("palm") || mn.Contains("frond") || rn.Contains("palm") || rn.Contains("frond"))) continue;

                var mf = r.GetComponent<MeshFilter>();
                string meshInfo = "no-mesh";
                if (mf != null && mf.sharedMesh != null)
                {
                    var mesh = mf.sharedMesh;
                    string uvInfo = "-";
                    if (mesh.uv != null && mesh.uv.Length > 0)
                    {
                        Vector2 mn2 = new Vector2(9, 9), mx2 = new Vector2(-9, -9);
                        foreach (var uv in mesh.uv) { mn2 = Vector2.Min(mn2, uv); mx2 = Vector2.Max(mx2, uv); }
                        uvInfo = $"uv[{mn2.x:F2},{mn2.y:F2}]-[{mx2.x:F2},{mx2.y:F2}] n={mesh.uv.Length}";
                    }
                    meshInfo = $"{mesh.name} verts={mesh.vertexCount} sub={mesh.subMeshCount} {uvInfo}";
                }
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                P($"   {r.name,-24} mat={m.name,-20} shader={m.shader.name}\n" +
                  $"      {meshInfo}  tex={(tex == null ? "null" : tex.name)}  " +
                  $"alphaClip={(m.HasProperty("_AlphaClip") ? m.GetFloat("_AlphaClip") : -1)}\n");
                if (++shown >= 12) break;
            }
            if (shown == 0) P("   （没找到棕榈相关的渲染器）\n");
        }
    }
}
#endif
