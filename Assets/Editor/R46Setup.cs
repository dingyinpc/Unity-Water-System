// R46Setup.cs — 「口径错位」修复：给 Islet / FFT 各补一组**公平机位**
//
// 背景（R41 审计的总表里自己写了这句）：
//   Islet 7→4、FFT 6→4 的掉分**主因不是退步，是审计帧的机位撞了**：
//     · Islet 审计帧用了 R40 的「低太阳暖光档（暗调剪影）」+ 片头低机位；
//       参考 S0 却是**高空俯视的亮调**；
//     · FFT 审计帧是**低机位贴水**，参考画面里有天空和地平线。
//   ⇒ 拿暗调低机位去对亮调俯视，分数没有可比性。本文件补的就是这两组"同视角"档。
//
// 两个档都**可逆**：首次运行把原值写进 EditorPrefs，`b` 系菜单原样还原。
// 机位只动 Camera 的 pos/euler/fov，不动光照/材质/后处理 —— 那些由 199(还原S2) + 113(正午) 负责，
// 避免"同一参数两个写入点"（铁律 6）。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R46Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        const string KI = "R46.IsletAerial";
        const string KF = "R46.FFTSky";

        static void Save(Camera c, string k)
        {
            if (EditorPrefs.HasKey(k + ".saved")) return;
            EditorPrefs.SetFloat(k + ".x",  c.transform.position.x);
            EditorPrefs.SetFloat(k + ".y",  c.transform.position.y);
            EditorPrefs.SetFloat(k + ".z",  c.transform.position.z);
            EditorPrefs.SetFloat(k + ".rx", c.transform.eulerAngles.x);
            EditorPrefs.SetFloat(k + ".ry", c.transform.eulerAngles.y);
            EditorPrefs.SetFloat(k + ".fov", c.fieldOfView);
            EditorPrefs.SetBool(k + ".saved", true);
        }

        static bool Load(Camera c, string k)
        {
            if (!EditorPrefs.HasKey(k + ".saved")) return false;
            c.transform.position = new Vector3(EditorPrefs.GetFloat(k + ".x"),
                                               EditorPrefs.GetFloat(k + ".y"),
                                               EditorPrefs.GetFloat(k + ".z"));
            c.transform.eulerAngles = new Vector3(EditorPrefs.GetFloat(k + ".rx"),
                                                  EditorPrefs.GetFloat(k + ".ry"), 0f);
            c.fieldOfView = EditorPrefs.GetFloat(k + ".fov");
            return true;
        }

        static void Report(Camera c, string tag)
        {
            P($"      {tag}: pos=({c.transform.position.x:F1},{c.transform.position.y:F1},{c.transform.position.z:F1}) " +
              $"euler=({c.transform.eulerAngles.x:F1},{c.transform.eulerAngles.y:F1}) fov={c.fieldOfView:F1}\n");
        }

        // ═══════════ Islet：高空俯视（对准参考 S0 的"亮调俯视"）═══════════
        // 岛在原点附近；相机拉到 (55,48,78) 俯角 25.3°、yaw 215.2°（正对岛心），FOV 55。
        // 这样礁盘、浅滩、环礁水面同时进画，天空只留顶部一条 —— 与参考 S0 的取景结构一致。
        [MenuItem("Tools/R46/90 Islet 机位=高空俯视亮调档(可逆)")]
        public static void IsletAerial()
        {
            var cam = Camera.main;
            if (cam == null) { P("R46/90 REFUSED: 无 Camera.main\n"); return; }
            Save(cam, KI);
            // ⚠ 第一版 (55,48,78)/俯角25° 拍出来是"全水面+地平线在画面上缘外"，
            //   而且岛只占 1/8 —— 和参考 S0（近垂直俯视、水面铺满、沙洲贴右缘）不可比。
            //   这里改成**更陡更近**：水平距离 29.2m、俯角 60.7°，岛/礁盘占画面主体。
            cam.transform.position = new Vector3(15f, 55f, 25f);
            cam.transform.eulerAngles = new Vector3(60.7f, 211f, 0f);
            cam.fieldOfView = 60f;
            P("R46/90 Islet 俯视档已应用（v2: 近垂直俯视）\n");
            Report(cam, "Islet aerial");
        }

        [MenuItem("Tools/R46/90b Islet 机位=还原(读 EditorPrefs)")]
        public static void IsletAerialRevert()
        {
            var cam = Camera.main;
            if (cam == null) { P("R46/90b REFUSED: 无 Camera.main\n"); return; }
            if (!Load(cam, KI)) { P("R46/90b 无存档\n"); return; }
            P("R46/90b Islet 机位已还原\n");
            Report(cam, "Islet revert");
        }

        // ═══════════ FFT：带天空（对准参考"水+天空+地平线"）═══════════
        // 原档 (0,6,-16) pitch 20°（低头）⇒ 画面全是水，没有天空可比。
        // 这里后撤到 -28、抬高到 8、pitch 只留 2°，FOV 62 ⇒ 地平线略低于画面中线，天空约占上半。
        // ⚠ 本机约定：Euler.x **正=俯视，负=抬头**（AGENTS 铁律旁注），所以 2° 仍是"轻微向下"。
        [MenuItem("Tools/R46/91 FFT 机位=带天空档(可逆)")]
        public static void FFTSky()
        {
            var cam = Camera.main;
            if (cam == null) { P("R46/91 REFUSED: 无 Camera.main\n"); return; }
            Save(cam, KF);
            cam.transform.position = new Vector3(0f, 8f, -28f);
            cam.transform.eulerAngles = new Vector3(2f, 0f, 0f);
            cam.fieldOfView = 62f;
            P("R46/91 FFT 带天空档已应用\n");
            Report(cam, "FFT sky");
        }

        [MenuItem("Tools/R46/91b FFT 机位=还原(读 EditorPrefs)")]
        public static void FFTSkyRevert()
        {
            var cam = Camera.main;
            if (cam == null) { P("R46/91b REFUSED: 无 Camera.main\n"); return; }
            if (!Load(cam, KF)) { P("R46/91b 无存档\n"); return; }
            P("R46/91b FFT 机位已还原\n");
            Report(cam, "FFT revert");
        }

        // ⚠ 91 这个"带天空档"是**按常识推的，不是按参考事实**：查过原片 f1500，
        //   参考里 FFT 对应的段同样是**俯视水面**（没有地平线、没有天空）。
        //   所以真正同口径的是 92（俯视），91 只作为"想看海面平视"的备用档保留。
        [MenuItem("Tools/R46/92 FFT 机位=参考俯视水面档(可逆)")]
        public static void FFTAerial()
        {
            var cam = Camera.main;
            if (cam == null) { P("R46/92 REFUSED: 无 Camera.main\n"); return; }
            Save(cam, KF);
            cam.transform.position = new Vector3(0f, 42f, 0f);
            cam.transform.eulerAngles = new Vector3(88f, 0f, 0f);   // 近垂直俯视
            cam.fieldOfView = 60f;
            P("R46/92 FFT 俯视档已应用\n");
            Report(cam, "FFT aerial");
        }

        [MenuItem("Tools/R46/92b FFT 俯视档还原(读 EditorPrefs)")]
        public static void FFTAerialRevert()
        {
            var cam = Camera.main;
            if (cam == null) { P("R46/92b REFUSED: 无 Camera.main\n"); return; }
            if (!Load(cam, KF)) { P("R46/92b 无存档\n"); return; }
            P("R46/92b FFT 机位已还原\n");
            Report(cam, "FFT revert");
        }
    }
}
#endif
