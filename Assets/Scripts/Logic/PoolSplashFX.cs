// PoolSplashFX.cs — 入水水花粒子（一次性 Burst，程序化自建粒子系统）
//
// 为什么代码建而不是预制体：粒子参数是表现层的"材质常数"，按项目纪律
// 全部由代码给默认值（menu 只负责挂组件）——改参数只看这一处。
//
// 用法：PoolSplashFX.Instance?.Burst(世界点, 强度0..1.5)
//   · 跳水破水（PoolThirdPerson.Splash）→ 大 burst
//   · 球入水（PoolBall）→ 中 burst
// 实现要点：单系统复用（移 transform → Emit），粒子世界空间模拟
//（transform 移动不影响已发射粒子）、重力下坠、生命周期渐隐+缩小。
using UnityEngine;

namespace WaterProject.Logic
{
    public class PoolSplashFX : MonoBehaviour
    {
        public static PoolSplashFX Instance { get; private set; }

        [Tooltip("水花音效（menu92 从 Assets/Audio/Pool_Splash.wav 赋值）")]
        public AudioClip splashClip;

        ParticleSystem _ps;
        ParticleSystem.MainModule _main;
        AudioSource _src;

        void Awake()
        {
            Instance = this;
            _ps = gameObject.AddComponent<ParticleSystem>();
            _ps.loop = false;
            _ps.playOnAwake = false;

            var mu = _ps.main;
            mu.duration = 5f;
            mu.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            mu.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            mu.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
            mu.startColor = new Color(0.82f, 0.94f, 1.0f, 0.9f);
            mu.gravityModifier = 1.0f;
            mu.simulationSpace = ParticleSystemSimulationSpace.World;
            mu.maxParticles = 256;

            var em = _ps.emission;
            em.rateOverTime = 0f;                       // 纯 Burst 模式

            var sh = _ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 28f;
            sh.radius = 0.14f;
            sh.rotation = new Vector3(-90f, 0f, 0f);    // 锥口朝上

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.85f, 0.95f, 1f), 0f),
                        new GradientColorKey(new Color(0.65f, 0.85f, 0.95f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f),
                        new GradientAlphaKey(0.55f, 0.6f),
                        new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sz = _ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.Linear(0f, 1f, 1f, 0.45f));

            var rdr = _ps.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", new Color(0.85f, 0.95f, 1f, 0.9f));
            rdr.sharedMaterial = mat;
            rdr.renderMode = ParticleSystemRenderMode.Stretch;  // 速度方向拉长=水滴
            rdr.lengthScale = 1.8f;
            rdr.velocityScale = 0.12f;
            rdr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rdr.receiveShadows = false;

            _main = mu;
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0.6f;          // 一半立体：水花该有方位感
            _src.volume = 0.7f;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>在水面点炸一次水花。strength 0..1.5：0.3=球入水，1.0+=跳水。</summary>
        public void Burst(Vector3 waterPos, float strength)
        {
            transform.position = waterPos + Vector3.up * 0.06f;
            _main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f + strength * 1.6f,
                                                              2.2f + strength * 2.6f);
            int count = Mathf.Clamp(Mathf.RoundToInt(14f + strength * 26f), 8, 64);
            _ps.Emit(count);
            if (splashClip != null)
                _src.PlayOneShot(splashClip, Mathf.Clamp(0.35f + strength * 0.5f, 0.3f, 1f));
        }
    }
}
