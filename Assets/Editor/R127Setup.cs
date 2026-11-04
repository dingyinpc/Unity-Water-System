// R127Setup.cs — R127 AI 云密度贴图（把程序化 Fbm 的"团块云"换成真实积云形状）
//
// 90  接上 AI 云贴图（三张天空材质一起写：海岸/FFT/泳池共用 Sky_Coast）
// 90b 关回纯程序化（A/B）
// 91  贴图铺开尺寸（米/格）扫描
// 92  探针：打印各天空材质当前云参数
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R127Setup
    {
        const string TexPath = "Assets/Art/Textures/AI/Proc/CloudField_AI_albedo.png";
        static readonly string[] SkyMats =
        {
            "Assets/Art/Materials/Sky_OpenCoast.mat",
            "Assets/Art/Materials/FFT/FFT_Sky.mat",
            "Assets/Art/Materials/Pool_Sky.mat",
        };

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void EnsureTexImport()
        {
            var ti = AssetImporter.GetAtPath(TexPath) as TextureImporter;
            if (ti == null) { P("R127 REFUSED: 贴图不在工程里\n"); return; }
            bool dirty = false;
            if (ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }   // 密度图当线性数据用
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
            if (ti.textureCompression != TextureImporterCompression.CompressedHQ)
            { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
            if (dirty) { ti.SaveAndReimport(); P("R127 云贴图导入参数已修正(linear/repeat/mip)\n"); }
        }

        static void ApplyAll(System.Action<Material> f, string tag)
        {
            int n = 0;
            foreach (var path in SkyMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { P($"R127 跳过(不存在): {path}\n"); continue; }
                f(m);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                // R127 关键：**天空盒材质改完必须重新赋值**，否则 RenderSettings 里缓存的还是旧属性，
                //   画面一动不动（本轮实测：改 _CloudCover 0.52→0.30 像素差 0.01%）。
                if (RenderSettings.skybox == m)
                {
                    RenderSettings.skybox = null;
                    RenderSettings.skybox = m;
                }
                n++;
            }
            P($"R127 {tag}: 已写 {n} 张天空材质\n");
        }

        [MenuItem("Tools/R127/90 云=AI 密度贴图(接上)")]
        public static void CloudTexOn()
        {
            EnsureTexImport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            if (tex == null) { P("R127/90 REFUSED: 没有 CloudField_AI_albedo.png\n"); return; }
            ApplyAll(m => { m.SetTexture("_CloudTex", tex); m.SetFloat("_CloudTexAmt", 1f); }, "90 接上 AI 云贴图");
        }

        [MenuItem("Tools/R127/90b 云=纯程序化(A/B)")]
        public static void CloudTexOff() => ApplyAll(m => m.SetFloat("_CloudTexAmt", 0f), "90b 纯程序化");

        [MenuItem("Tools/R127/90c 云=半贴图(0.5)")]
        public static void CloudTexHalf() => ApplyAll(m => m.SetFloat("_CloudTexAmt", 0.5f), "90c 半贴图");

        [MenuItem("Tools/R127/91 贴图铺开=4km")]
        public static void TexWorld4k() => ApplyAll(m => m.SetFloat("_CloudTexWorld", 4000f), "91 4km");

        [MenuItem("Tools/R127/91b 贴图铺开=8km")]
        public static void TexWorld8k() => ApplyAll(m => m.SetFloat("_CloudTexWorld", 8000f), "91b 8km");

        [MenuItem("Tools/R127/91c 贴图铺开=16km")]
        public static void TexWorld16k() => ApplyAll(m => m.SetFloat("_CloudTexWorld", 16000f), "91c 16km");

        [MenuItem("Tools/R127/93 探针: 运行时天空来源")]
        public static void ProbeSkySource()
        {
            var sb = new System.Text.StringBuilder("R127/93 运行时天空来源\n");
            var sky = RenderSettings.skybox;
            sb.Append($"  RenderSettings.skybox = {(sky != null ? sky.name : "null")}")
              .Append(sky != null ? $" (instanceID {sky.GetInstanceID()})\n" : "\n");
            foreach (var cam in Object.FindObjectsOfType<Camera>())
                sb.Append($"  Camera {cam.name}: clearFlags={cam.clearFlags} inPlay={Application.isPlaying}\n");
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                if (m.shader == null) continue;
                sb.Append($"  Renderer {r.gameObject.name} layer={r.gameObject.layer} mat={m.name}")
                  .Append($" shader={m.shader.name}")
                  .Append($" cover={(m.HasProperty("_CloudCover") ? m.GetFloat("_CloudCover") : -99):F2}")
                  .Append($" amt={(m.HasProperty("_CloudTexAmt") ? m.GetFloat("_CloudTexAmt") : -99):F2}\n");
            }
            P(sb.ToString());
        }

        [MenuItem("Tools/R127/94 天空变红(诊断:材质是否真的在渲染)")]
        public static void SkyRed()
        {
            foreach (var path in SkyMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                m.SetColor("_ZenithColor", Color.red);
                m.SetColor("_HorizonColor", Color.red);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                if (RenderSettings.skybox == m) { RenderSettings.skybox = null; RenderSettings.skybox = m; }
            }
            P("R127/94 天空已改红(诊断)\n");
        }

        [MenuItem("Tools/R127/95 云贴图 mip=0(最锐)")]
        public static void TexLod0() => ApplyAll(m => m.SetFloat("_CloudTexLod", 0f), "95 mip0");

        [MenuItem("Tools/R127/95b 云贴图 mip=2")]
        public static void TexLod2() => ApplyAll(m => m.SetFloat("_CloudTexLod", 2f), "95b mip2");

        [MenuItem("Tools/R127/95c 云贴图 mip=3(默认)")]
        public static void TexLod3() => ApplyAll(m => m.SetFloat("_CloudTexLod", 3f), "95c mip3");

        [MenuItem("Tools/R127/95d 云贴图 mip=4(最柔)")]
        public static void TexLod4() => ApplyAll(m => m.SetFloat("_CloudTexLod", 4f), "95d mip4");

        [MenuItem("Tools/R127/92 探针: 天空材质云参数")]
        public static void Probe()
        {
            var sb = new System.Text.StringBuilder("R127/92 天空材质\n");
            foreach (var path in SkyMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { sb.Append($"  {path}: 不存在\n"); continue; }
                var t = m.GetTexture("_CloudTex");
                sb.Append($"  {System.IO.Path.GetFileName(path)}: tex={(t != null ? t.name : "null")}")
                  .Append($" amt={m.GetFloat("_CloudTexAmt"):F2}")
                  .Append($" world={m.GetFloat("_CloudTexWorld"):F0}")
                  .Append($" cover={m.GetFloat("_CloudCover"):F2}")
                  .Append($" band={m.GetFloat("_CloudBand"):F2}\n");
            }
            P(sb.ToString());
        }
    }
}
#endif
