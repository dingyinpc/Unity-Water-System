using UnityEditor;

// 一次性工具：把两个岩石 FBX 的法线改成 **Unity 自己按角度算**。
//
// 为什么不走"导入文件法线"：Blender 侧 FBX 导出器 mesh_smooth_type="OFF"
// 到底有没有把 normals_split_custom_set 写进文件，我无法从 Unity 侧证实；
// 之前拿"顶点数 3262→1805"当证据是**误判** —— 那批竖条纹同时改了材质槽分配，
// 子网格边界一变，Unity 拆重复顶点的数量本来就跟着变，跟法线无关。
// 所以改成让 Unity 用 smoothingAngle 现算：这条路不依赖导出器行为，确定性更高。
public static class FixRockNormals
{
    [MenuItem("Tools/Islet/97b 岩石法线改为 Unity 按角度计算")]
    public static void Run()
    {
        string[] paths =
        {
            "Assets/Art/Models/Islet/SM_Rock_Cliff_01.fbx",
            "Assets/Art/Models/Islet/SM_Rock_Reef_01.fbx",
        };
        string log = "";
        foreach (var p in paths)
        {
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) { log += " " + p + ":MISSING"; continue; }
            mi.normals = ModelImporterNormals.Calculate;
            mi.smoothingAngle = 60f;
            mi.SaveAndReimport();
            var go = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
            var mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
            var m = mf != null ? mf.sharedMesh : null;
            log += " " + go.name + "[verts=" + (m == null ? 0 : m.vertexCount) + "]";
        }
        System.IO.File.AppendAllText("Logs/water_probe.txt", "MENU97B normals=Calculate angle=60" + log + "\n");
    }
}
