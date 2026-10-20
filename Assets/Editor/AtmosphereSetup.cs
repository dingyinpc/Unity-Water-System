// AtmosphereSetup.cs — P2-3（参考 S14 云影 / S15 远景雾）编辑器菜单：Tools/Ocean/170~175
//
// 纪律：云影/雾的**开关与参数**只写在 `Ocean_Water.mat`（唯一写入点）；
//   云影图由场景里的 `AtmosphereField` 组件逐帧烘（域/云参数在组件上）。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using WaterProject.Logic;

namespace WaterProject.EditorTools
{
    public static class AtmosphereSetup
    {
        const string MatPath = "Assets/Art/Materials/Ocean_Water.mat";

        static void WriteProbe(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static Material WaterMat()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null) WriteProbe("AF REFUSED: Ocean_Water.mat 不存在\n");
            return m;
        }

        // ═════════════ 170 建大气场（组件 + 云影图）═════════════
        [MenuItem("Tools/Ocean/170 建大气场(云影 RT)")]
        public static void BuildField()
        {
            var field = Object.FindObjectOfType<AtmosphereField>();
            if (field == null)
            {
                var go = new GameObject("AtmosphereField");
                field = go.AddComponent<AtmosphereField>();
                Undo.RegisterCreatedObjectUndo(go, "Create AtmosphereField");
            }
            // 域必须盖住**画面里能看到的整片海**（含远海）——否则水面 cuv 落到域外，云影采样直接跳过。
            field.minX = -1200f; field.minZ = -1600f; field.sizeX = 2400f; field.sizeZ = 2000f;
            // 云尺度要**按域大小**给：0.006 → 基准格 ~165m，3 倍频最低频 ~40m（≈10 纹素）→ 不糊成椒盐
            field.cloudScale = 0.006f;
            field.cloudCover = 0.52f;
            field.optical = 1.8f;
            field.Build();
            EditorUtility.SetDirty(field);

            var m = WaterMat();
            if (m != null) { m.SetFloat("_CloudShadowOn", 1f); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }

            WriteProbe($"MENU170 ok: AtmosphereField res={field.res} 域({field.minX},{field.minZ},{field.sizeX},{field.sizeZ}) "
                     + $"云 尺度{field.cloudScale} 量{field.cloudCover} 厚{field.optical} | 水面 _CloudShadowOn={m?.GetFloat("_CloudShadowOn")}\n");
        }

        // ═════════════ 171/172 云影 A/B ═════════════
        static void SetCloudShadow(float v, string tag)
        {
            var m = WaterMat(); if (m == null) return;
            m.SetFloat("_CloudShadowOn", v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            WriteProbe($"AFAB {tag} _CloudShadowOn -> {m.GetFloat("_CloudShadowOn")}\n");
        }
        [MenuItem("Tools/Ocean/171 云影=0(关)")] public static void CloudOff() => SetCloudShadow(0f, "cloudOff");
        [MenuItem("Tools/Ocean/172 云影=1(开)")] public static void CloudOn()  => SetCloudShadow(1f, "cloudOn");

        // ═════════════ 173/174 远景雾 A/B ═════════════
        static void SetFog(float v, string tag)
        {
            var m = WaterMat(); if (m == null) return;
            m.SetFloat("_FogOn", v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            WriteProbe($"AFAB {tag} _FogOn -> {m.GetFloat("_FogOn")}\n");
        }
        [MenuItem("Tools/Ocean/173 远景雾=0(关)")] public static void FogOff() => SetFog(0f, "fogOff");
        [MenuItem("Tools/Ocean/174 远景雾=1(开)")] public static void FogOn()  => SetFog(1f, "fogOn");

        // ═════════════ 176 云影诊断：全阴天（排除"覆盖率太低看不出"）═════════════
        [MenuItem("Tools/Ocean/176 云影诊断=全阴天(cover1/optical8)")]
        public static void CloudDiag()
        {
            var field = Object.FindObjectOfType<AtmosphereField>();
            if (field == null) { WriteProbe("MENU176 REFUSED: 无 AtmosphereField\n"); return; }
            field.cloudCover = 1f; field.optical = 8f; field.Build();
            EditorUtility.SetDirty(field);
            float afOn = Shader.GetGlobalFloat("_AFOn");
            WriteProbe($"MENU176 全阴天: cover=1 optical=8 | 全局 _AFOn={afOn} _AFRect={Shader.GetGlobalVector("_AFRect")}\n");
        }
        [MenuItem("Tools/Ocean/177 云影诊断=还原(cover0.52/optical1.8)")]
        public static void CloudDiagReset()
        {
            var field = Object.FindObjectOfType<AtmosphereField>();
            if (field == null) return;
            field.cloudCover = 0.52f; field.optical = 1.8f; field.Build();
            EditorUtility.SetDirty(field);
            WriteProbe("MENU177 云影还原\n");
        }

        // ═════════════ 178/179 雾统一 / S15 浓雾演示档 ═════════════
        // 参考 S15：远景被白雾吞掉、水面同时发白。要做到这点，水面与地形必须在**同一距离**
        // 衰减到**同一程度**，且雾色 = 地平色（否则远山是灰的、天是蓝的，交界出现硬边）。
        static void ApplyUnifiedFog(float density, string tag)
        {
            var m = WaterMat(); if (m == null) return;
            var dn = Object.FindObjectOfType<DayNightCycle>();
            if (dn == null) { WriteProbe($"MENU{tag} REFUSED: 场景里没有 DayNightCycle（先跑菜单110）\n"); return; }

            dn.driveFog = true;
            dn.fogDensity = density;

            // 1) 水面解析雾：开、密度与场景同值、太阳遮挡长度按新密度重算
            m.SetFloat("_FogOn", 1f);
            m.SetFloat("_FogDensity", density);
            m.SetFloat("_FogSunLen", 140f);        // exp(-0.0022*140)=0.73 → 雾里高光剩七成

            // 2) 场景雾：同样密度、颜色由昼夜系统按地平色写入
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = density;

            dn.Apply(true);                        // 会把 _FogColor / fogColor 统一成当前地平色
            m.SetColor("_FogColor", RenderSettings.fogColor);

            EditorUtility.SetDirty(dn); EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            WriteProbe($"MENU{tag} 雾统一: 密度={density:F5} | 场景 fogColor={RenderSettings.fogColor} | "
                     + $"水面 _FogColor={m.GetColor("_FogColor")} _FogDensity={m.GetFloat("_FogDensity"):F5} _FogOn={m.GetFloat("_FogOn")}\n");
        }

        [MenuItem("Tools/Ocean/178 雾统一(水面=场景, 0.0022)")]  public static void FogUnify() => ApplyUnifiedFog(0.0022f, "178");
        [MenuItem("Tools/Ocean/179 S15浓雾演示档(0.0035)")]      public static void FogDemo()  => ApplyUnifiedFog(0.0035f, "179");

        // ═════════════ 175 导出云影调试图 ═════════════
        [MenuItem("Tools/Ocean/175 导出云影调试图(写 PNG)")]
        public static void ExportCloudViz()
        {
            var field = Object.FindObjectOfType<AtmosphereField>();
            if (field == null) { WriteProbe("MENU175 REFUSED: 场景里没有 AtmosphereField\n"); return; }
            field.Build();
            var rt = field.RenderViz(0f);   // 0=透射率
            if (rt == null) { WriteProbe("MENU175: RenderViz null\n"); return; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            string abs = Path.Combine(Application.dataPath, "..", "Docs/screenshots/cloud_shadow_map.png");
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            WriteProbe("MENU175 ok: 云影透射率图 -> Docs/screenshots/cloud_shadow_map.png\n");
        }
    }
}
#endif
