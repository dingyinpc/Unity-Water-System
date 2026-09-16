// WaterInteractionManager.cs — 逻辑层：水体交互系统
// 职责：
//   1) 维护交互事件池（数据层 struct）；
//   2) 每帧把交互点 Blit 进交互 RenderTexture（正交、覆盖相机周围区域）；
//   3) 向全局暴露 _InteractionTex / _InteractionRect / _RippleSources，供水体 Shader 采样；
//   4) 演示模式：自动下雨点 + 程序化 Ripple 源。
using UnityEngine;
using WaterProject.Data;

namespace WaterProject.Logic
{
    public class WaterInteractionManager : MonoBehaviour
    {
        [Header("交互纹理")]
        public int texSize = 256;
        public float halfExtent = 12f;          // RT 覆盖半径（米）
        public float interactionLife = 1.8f;

        [Header("交互中心（演示/RT 采样中心）")]
        [Tooltip("演示用默认以水面原点为中心；真实玩法可将此绑定到玩家位置。")]
        public Vector3 interactionCenter = Vector3.zero;

        [Header("演示")]
        public bool autoDemo = true;
        public float demoInterval = 0.45f;

        [Header("程序化 Ripple")]
        public int maxRipples = 8;
        public float rippleLife = 2.2f;

        public static WaterInteractionManager Instance { get; private set; }

        const string BlitShaderName = "WaterProject/InteractionBlit";

        static readonly int P_Interactions = Shader.PropertyToID("_Interactions");
        static readonly int P_Count        = Shader.PropertyToID("_InteractionCount");
        static readonly int P_Time         = Shader.PropertyToID("_CurrentTime");
        static readonly int P_Center       = Shader.PropertyToID("_InteractionCenter");
        static readonly int P_Life         = Shader.PropertyToID("_InteractionLife");
        static readonly int P_Tex          = Shader.PropertyToID("_InteractionTex");
        static readonly int P_Rect         = Shader.PropertyToID("_InteractionRect");
        static readonly int P_Ripples      = Shader.PropertyToID("_RippleSources");
        static readonly int P_RippleCount  = Shader.PropertyToID("_RippleCount");
        static readonly int P_RippleLife   = Shader.PropertyToID("_RippleLife");

        RenderTexture _rt;
        Material _blit;
        int _interactionCap = 32;
        Vector4[] _interactionBuf;
        int _interactionCount;

        Vector4[] _rippleBuf;
        int _rippleIndex;
        int _rippleCount;

        float _nextDemoTime;

        // 帧耗时诊断
        int _diagFrames;
        float _diagAccum;

        void Awake()
        {
            Instance = this;
            _interactionBuf = new Vector4[_interactionCap];
            _rippleBuf = new Vector4[maxRipples];

            var shader = Shader.Find(BlitShaderName);
            if (shader == null)
            {
                Debug.LogError("[WaterInteractionManager] 找不到 Shader: " + BlitShaderName);
                return;
            }
            _blit = new Material(shader);
            Shader.SetGlobalFloat(P_RippleLife, rippleLife);
            Shader.SetGlobalFloat(P_RippleCount, 0);
        }

        void OnEnable() => EnsureRT();

        void OnDisable()
        {
            Shader.SetGlobalTexture(P_Tex, null);
        }

        void OnDestroy()
        {
            if (_blit) Destroy(_blit);
            if (_rt) _rt.Release();
            Instance = null;
        }

        void EnsureRT()
        {
            if (_rt) return;
            _rt = new RenderTexture(texSize, texSize, 0, RenderTextureFormat.ARGB32)
            {
                name = "WaterInteractionRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
        }

        // ── 对外接口（表现层/玩法层调用）────────────────────────
        /// <summary>产生一次入水交互（涟漪+泡沫）。</summary>
        public void SpawnInteraction(Vector3 worldPos, float strength = 1f)
        {
            if (_interactionCount >= _interactionCap) _interactionCount = 0; // 环形覆盖
            _interactionBuf[_interactionCount++] =
                new Vector4(worldPos.x, worldPos.y, worldPos.z, Time.time);
        }

        /// <summary>产生程序化 Ripple 源。[7]</summary>
        public void SpawnRipple(Vector3 worldPos)
        {
            if (maxRipples <= 0) return;
            _rippleBuf[_rippleIndex] = new Vector4(worldPos.x, 0, worldPos.z, Time.time);
            _rippleIndex = (_rippleIndex + 1) % maxRipples;
            _rippleCount = Mathf.Min(_rippleCount + 1, maxRipples);
        }

        void Update()
        {
            EnsureRT();

            // 帧耗时诊断：每 60 帧打印一次真实 ms/帧
            _diagFrames++;
            _diagAccum += Time.unscaledDeltaTime;
            if (_diagFrames >= 60)
            {
                float ms = _diagAccum / _diagFrames * 1000f;
                Debug.Log($"[WaterProject] FRAME {ms:F1} ms ({1000f / Mathf.Max(ms, 0.001f):F0} FPS)");
                _diagFrames = 0;
                _diagAccum = 0f;
            }

            // 演示模式：随机落点（以交互中心为圆心）
            if (autoDemo && Time.time >= _nextDemoTime)
            {
                Vector3 p = new Vector3(
                    interactionCenter.x + Random.Range(-halfExtent * 0.7f, halfExtent * 0.7f),
                    0,
                    interactionCenter.z + Random.Range(-halfExtent * 0.7f, halfExtent * 0.7f));
                SpawnInteraction(p, Random.Range(0.5f, 1f));
                SpawnRipple(p);
                _nextDemoTime = Time.time + demoInterval;
            }

            if (_blit == null) return;

            // ── 1) 交互 RT 更新（以交互中心为采样中心）──
            Vector3 center = new Vector3(interactionCenter.x, 0, interactionCenter.z);

            _blit.SetVectorArray(P_Interactions, _interactionBuf);
            _blit.SetInt(P_Count, _interactionCount);
            _blit.SetFloat(P_Time, Time.time);
            _blit.SetVector(P_Center, new Vector4(center.x, center.y, center.z, halfExtent));
            _blit.SetFloat(P_Life, interactionLife);

            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(null, _rt, _blit);
            RenderTexture.active = prev;

            // ── 2) 全局暴露给水体 Shader ──
            Shader.SetGlobalTexture(P_Tex, _rt);
            Shader.SetGlobalVector(P_Rect, new Vector4(center.x, center.z, halfExtent, 0));
            Shader.SetGlobalVectorArray(P_Ripples, _rippleBuf);
            Shader.SetGlobalFloat(P_RippleCount, _rippleCount);
        }
    }
}