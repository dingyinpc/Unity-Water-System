// DayNightCycle.cs — 逻辑层：昼夜系统（P1-1，参考 S11–S12）
//
// 一句话：拿一个 0~1 的"时间"当唯一旋钮，同时驱动【太阳/月亮的角度与颜色】【天空三段渐变】
//         【云色】【雾色】【水色】【环境光】，使整幅画面从夜晚→日出→正午→黄昏→夜晚连续变化。
//
// 参考视频的做法（08 文档 S11）：逐帧更新太阳世界方向 → 由太阳高度得到"昼夜采样时间" →
//   统一驱动天空颜色/散射/直射光曲线；月亮方向与太阳相差半圈，两套方向光各自按曲线更新。
//
// 本实现的关键取舍（都是本轮实测踩出来的）：
//   · 天空 shader 用 GetMainLight() 取太阳方向（Sky_Coast 自带约定）⇒ **不需要再同步一份太阳角**
//     （《05》C-4"太阳仰角有两个写入点"就是这么踩的）。
//   · 环境光保持 Skybox 模式 + DynamicGI.UpdateEnvironment()（节流），让天光**自然跟着天空变色**。
//   · ⚠ 颜色曲线**不用 `Gradient`**：本机实测 `Gradient` 字段在编辑器里被还原成默认白渐变
//     （`colorKeys.Length` 读回 2，Evaluate 恒为白），导致"天空永远是白的"。
//     改用**朴素 Color[]/float[] 数组 + 手写线性插值**，可序列化、行为可预测。
using UnityEngine;

namespace WaterProject.Logic
{
    [System.Serializable]
    public struct ColorKey
    {
        public float t;
        public Color c;
        public ColorKey(float t, Color c) { this.t = t; this.c = c; }
    }

    [ExecuteAlways]
    public class DayNightCycle : MonoBehaviour
    {
        public const string Desc = "0=午夜 0.25=日出 0.5=正午 0.75=黄昏 1=午夜";

        [Header("时间（0=午夜 0.25=日出 0.5=正午 0.75=黄昏 1=午夜）")]
        [Range(0f, 1f)] public float timeOfDay = 0.35f;
        [Tooltip("自动循环一圈所需秒数；<=0 表示不自动走")]
        public float cycleSeconds = 0f;

        [Header("灯光")]
        public Light sun;
        public Light moon;
        public float sunAzimuth = 236f;      // 沿用 OceanScene 调好的方位（保碎闪带不出画）
        public float sunMaxIntensity = 2.9f;
        public float moonMaxIntensity = 0.30f;
        public float sunTilt = 75f;          // 正午最高仰角

        [Header("材质（为空则跳过）")]
        public Material skyMat;              // Sky_OpenCoast（Shader: WaterProject/Sky_Coast）
        public Material waterMat;            // Ocean_Water

        [Header("颜色曲线（按 timeOfDay 采样）")]
        public ColorKey[] skyZenith;
        public ColorKey[] skyHorizon;
        public ColorKey[] sunTint;
        public ColorKey[] waterDeep;
        public ColorKey[] waterShallow;

        float _lastEnvTime = -1f;

        /// <summary>手写线性插值（数组按 t 升序）。</summary>
        public static Color Eval(ColorKey[] keys, float t)
        {
            if (keys == null || keys.Length == 0) return Color.magenta;   // 缺曲线时给刺眼色，便于发现
            if (t <= keys[0].t) return keys[0].c;
            if (t >= keys[keys.Length - 1].t) return keys[keys.Length - 1].c;
            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i].t)
                {
                    float span = Mathf.Max(keys[i].t - keys[i - 1].t, 1e-5f);
                    return Color.Lerp(keys[i - 1].c, keys[i].c, (t - keys[i - 1].t) / span);
                }
            }
            return keys[keys.Length - 1].c;
        }

        // 参考帧量色（f3050 紫粉黄昏 / f3200 暖日出 / f3400 深蓝夜）
        public void BuildDefaults()
        {
            skyZenith = new[]
            {
                new ColorKey(0.00f, new Color(0.015f, 0.025f, 0.080f)),
                new ColorKey(0.22f, new Color(0.100f, 0.100f, 0.280f)),
                new ColorKey(0.28f, new Color(0.160f, 0.180f, 0.420f)),
                new ColorKey(0.38f, new Color(0.200f, 0.380f, 0.780f)),
                new ColorKey(0.50f, new Color(0.220f, 0.440f, 0.880f)),
                new ColorKey(0.65f, new Color(0.200f, 0.360f, 0.760f)),
                new ColorKey(0.73f, new Color(0.240f, 0.120f, 0.360f)),
                new ColorKey(0.82f, new Color(0.080f, 0.050f, 0.190f)),
                new ColorKey(1.00f, new Color(0.015f, 0.025f, 0.080f)),
            };
            skyHorizon = new[]
            {
                new ColorKey(0.00f, new Color(0.050f, 0.080f, 0.180f)),
                new ColorKey(0.22f, new Color(0.450f, 0.280f, 0.420f)),
                new ColorKey(0.28f, new Color(0.850f, 0.520f, 0.480f)),
                new ColorKey(0.38f, new Color(0.760f, 0.800f, 0.880f)),
                new ColorKey(0.50f, new Color(0.700f, 0.800f, 0.910f)),
                new ColorKey(0.65f, new Color(0.780f, 0.760f, 0.820f)),
                new ColorKey(0.73f, new Color(0.950f, 0.420f, 0.320f)),
                new ColorKey(0.82f, new Color(0.350f, 0.160f, 0.300f)),
                new ColorKey(1.00f, new Color(0.050f, 0.080f, 0.180f)),
            };
            sunTint = new[]
            {
                new ColorKey(0.00f, new Color(0.55f, 0.62f, 0.85f)),
                new ColorKey(0.28f, new Color(1.00f, 0.62f, 0.42f)),
                new ColorKey(0.50f, new Color(1.00f, 0.955f, 0.885f)),
                new ColorKey(0.73f, new Color(1.00f, 0.48f, 0.30f)),
                new ColorKey(1.00f, new Color(0.55f, 0.62f, 0.85f)),
            };
            waterDeep = new[]
            {
                new ColorKey(0.00f, new Color(0.010f, 0.030f, 0.075f)),
                new ColorKey(0.28f, new Color(0.060f, 0.075f, 0.130f)),
                new ColorKey(0.50f, new Color(0.012f, 0.075f, 0.130f)),
                new ColorKey(0.73f, new Color(0.060f, 0.045f, 0.110f)),
                new ColorKey(1.00f, new Color(0.010f, 0.030f, 0.075f)),
            };
            waterShallow = new[]
            {
                new ColorKey(0.00f, new Color(0.030f, 0.070f, 0.130f)),
                new ColorKey(0.28f, new Color(0.180f, 0.180f, 0.230f)),
                new ColorKey(0.50f, new Color(0.150f, 0.560f, 0.580f)),
                new ColorKey(0.73f, new Color(0.230f, 0.150f, 0.190f)),
                new ColorKey(1.00f, new Color(0.030f, 0.070f, 0.130f)),
            };
        }

        void Reset() { BuildDefaults(); }

        void Awake()
        {
            if (skyZenith == null || skyZenith.Length < 2) BuildDefaults();
        }

        /// <summary>按 timeOfDay 一次性应用全部表现。</summary>
        public void Apply(bool updateEnvironment = true)
        {
            float t = Mathf.Repeat(timeOfDay, 1f);

            // ── 太阳 / 月亮：方向相反、强度按仰角、颜色按曲线 ──
            float elev = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f) * sunTilt;
            Color tint = Eval(sunTint, t);
            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(elev, sunAzimuth, 0f);
                sun.color = tint;
                sun.intensity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elev / 18f)) * sunMaxIntensity;
            }
            if (moon)
            {
                moon.transform.rotation = Quaternion.Euler(-elev, sunAzimuth + 180f, 0f);
                moon.color = new Color(0.62f, 0.72f, 0.95f);
                moon.intensity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-elev / 18f)) * moonMaxIntensity;
            }

            // ── 天空三段色 + 云色 ──
            Color zen = Eval(skyZenith, t);
            Color hor = Eval(skyHorizon, t);
            float day = Mathf.Clamp01((elev + 6f) / 26f);      // 0=夜 1=昼（由太阳高度决定）
            if (skyMat != null)
            {
                skyMat.SetColor("_ZenithColor", zen);
                skyMat.SetColor("_HorizonColor", hor);
                skyMat.SetColor("_GroundColor", hor * 0.35f);
                // ⚠ 云色必须按"日照因子"过渡：若写成 lerp(地平色,白,0.75)，夜里也是白云，
                //   整片天空发白、看不到夜色（第一版实测）。
                if (skyMat.HasProperty("_CloudColor"))
                    skyMat.SetColor("_CloudColor", Color.Lerp(hor * 0.30f, Color.Lerp(hor, Color.white, 0.80f), day));
                if (skyMat.HasProperty("_CloudShadow"))
                    skyMat.SetColor("_CloudShadow", Color.Lerp(zen * 0.25f, Color.Lerp(zen, hor, 0.55f), day));
                if (skyMat.HasProperty("_SunGlow"))
                    skyMat.SetFloat("_SunGlow", Mathf.Clamp01(1f - Mathf.Abs(elev) / 45f) * 1.1f + 0.15f);
            }

            // ── 雾色 == 地平色（地平线不留硬边的唯一条件）──
            RenderSettings.fogColor = hor;

            // ── 水色 ──
            if (waterMat != null)
            {
                waterMat.SetColor("_SkyZenith", zen);
                waterMat.SetColor("_SkyHorizon", hor);
                waterMat.SetColor("_DeepColor", Eval(waterDeep, t));
                waterMat.SetColor("_ShallowColor", Eval(waterShallow, t));
                if (waterMat.HasProperty("_SunColor")) waterMat.SetColor("_SunColor", tint);
            }

            // ── 环境光：Skybox 模式跟着天空走（节流；每帧 UpdateEnvironment 太贵）──
            if (updateEnvironment && Mathf.Abs(t - _lastEnvTime) > 0.002f)
            {
                _lastEnvTime = t;
                DynamicGI.UpdateEnvironment();
            }
        }

        void Update()
        {
            if (cycleSeconds > 0f && Application.isPlaying)
                timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime / cycleSeconds, 1f);
            Apply(true);
        }
    }
}
