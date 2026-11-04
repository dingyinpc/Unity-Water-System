// R122Setup.cs — R122：固定"验收机位"（解决跨会话构图漂移）
//
// 问题（R121 记档）：在编辑模式下多次 `保存当前场景`，会把某次 Play 之后的相机状态存进场景，
//   于是**同一条验收命令，前后两次的构图不一样** —— 跨会话的数字基线跟着构图漂移，
//   只能当"量级报警"，没法做精确回归。
//
// 做法：把每个场景的"验收机位"存成一份纯文本（Tools/verify/cameras.txt），
//   验收脚本在**开场景之后、进 Play 之前**先应用它，构图就恒定了。
//
// 用法：
//   ① 摆好机位 → `Tools/R122/90 记录当前机位`
//   ② `Tools/R122/91 应用验收机位`（verify_scenes.ps1 会自动调它）
#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R122Setup
    {
        const string RelPath = "Tools/verify/cameras.txt";

        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static string Abs()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelPath));

        static string SceneName() => EditorSceneManager.GetActiveScene().name;

        [MenuItem("Tools/R122/90 记录当前机位(写入 cameras.txt)")]
        public static void Record()
        {
            var cam = Camera.main;
            if (cam == null) { P("R122/90 REFUSED: 没有 Camera.main\n"); return; }

            string scene = SceneName();
            var p = cam.transform.position;
            var e = cam.transform.eulerAngles;
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0}|{1:F3},{2:F3},{3:F3}|{4:F2},{5:F2},{6:F2}|{7:F2}",
                scene, p.x, p.y, p.z, e.x, e.y, e.z, cam.fieldOfView);

            var path = Abs();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = File.Exists(path) ? new System.Collections.Generic.List<string>(File.ReadAllLines(path)) : new System.Collections.Generic.List<string>();
            lines.RemoveAll(l => l.StartsWith(scene + "|"));
            lines.Add(line);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));

            P($"R122/90 已记录 {scene}: pos=({p.x:F2},{p.y:F2},{p.z:F2}) euler=({e.x:F1},{e.y:F1},{e.z:F1}) fov={cam.fieldOfView:F1}\n");
        }

        [MenuItem("Tools/R122/91 应用验收机位(读 cameras.txt)")]
        public static void Apply()
        {
            var cam = Camera.main;
            if (cam == null) { P("R122/91 REFUSED: 没有 Camera.main\n"); return; }

            string scene = SceneName();
            var path = Abs();
            if (!File.Exists(path)) { P($"R122/91 没有 {RelPath}（先跑 90 记录）\n"); return; }

            foreach (var line in File.ReadAllLines(path))
            {
                if (!line.StartsWith(scene + "|")) continue;
                var f = line.Split('|');
                if (f.Length < 4) continue;
                var p = f[1].Split(',');
                var e = f[2].Split(',');
                cam.transform.position = new Vector3(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture),
                    float.Parse(p[2], CultureInfo.InvariantCulture));
                cam.transform.eulerAngles = new Vector3(
                    float.Parse(e[0], CultureInfo.InvariantCulture),
                    float.Parse(e[1], CultureInfo.InvariantCulture),
                    float.Parse(e[2], CultureInfo.InvariantCulture));
                cam.fieldOfView = float.Parse(f[3], CultureInfo.InvariantCulture);
                P($"R122/91 {scene}: 应用验收机位 pos=({p[0]},{p[1]},{p[2]}) euler=({e[0]},{e[1]},{e[2]}) fov={f[3]}\n");
                return;
            }
            P($"R122/91 {scene}: cameras.txt 里没有这个场景的机位\n");
        }

        [MenuItem("Tools/R122/99 探针(列出所有验收机位)")]
        public static void Probe()
        {
            var path = Abs();
            P($"R122 PROBE 当前场景={SceneName()} 文件={RelPath}\n");
            if (!File.Exists(path)) { P("   （文件不存在）\n"); return; }
            foreach (var line in File.ReadAllLines(path)) P("   " + line + "\n");
        }
    }
}
#endif
