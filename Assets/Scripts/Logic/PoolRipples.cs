// PoolRipples.cs — 泳池波动方程的双缓冲管理器（R21 手感 v2）
//
// 职责：
//   1) 持有三张 RGHalf 高度场 RT（12×6m 池域 → 256×128，方格 4.7cm），
//      R=长波主环(c≈1.15m/s) / G=细纹理尾波(c≈0.45m/s)，两场不同速=色散；
//   2) 以**固定 60Hz 累加器**步进（v1 是每帧×2 步 → 波速 3.98m/s 且随帧率漂，
//      现在波速=dx·√K/dt 只由参数决定，采集掉到 30fps 也不变手感）；
//   3) 收集交互（鼠标点水 / 自动演示 / 外部 API SpawnDrop）→ 环形冲量注入
//      （负核+正沿：水被压下去、沿口隆起，见 PoolWaveUpdate.DropShape）；
//   4) 全局暴露 _PoolWaveTex(RG) / _PoolWaveRect 供水面 shader 采样。
//
// 与主线 WaterInteractionManager 的关系：**并行不替换**。主线是"解析贴花"
// （无传播），泳池是"波动场"（会传播/反射/干涉）。
//
// 注波语义：冲量只在"本批步进的第一步"注入一次（pending 清空），
// 不是持续外力——持续外力会把波动方程推成单调水位漂移。
using UnityEngine;

namespace WaterProject.Logic
{
    public class PoolRipples : MonoBehaviour
    {
        public static PoolRipples Instance { get; private set; }

        [Header("模拟域（池内水面，米）")]
        public Vector2 fieldSize = new Vector2(12f, 6f);

        [Header("模拟参数")]
        public int texW = 256;
        public int texH = 128;                    // 12:6 = 2:1 ⇒ 方格 4.7cm
        [Range(24f, 120f)] public float simHz = 60f;   // 固定步进率（波速与帧率解耦）
        public int maxCatchup = 3;                // 掉帧时单帧最多补的步数（再慢就让模拟慢放）

        [Header("双场参数（R21 手感 v2 —— 594 参数包是唯一写入点）")]
        [Tooltip("长波场波速系数 K：c = dx·√K/dt，0.167 → 1.15 m/s")]
        [Range(0.01f, 0.45f)] public float waveKA = 0.167f;
        [Tooltip("细纹理场波速系数 K：0.026 → 0.45 m/s")]
        [Range(0.005f, 0.45f)] public float waveKB = 0.026f;
        [Tooltip("长波场反棋盘/粘性 ε（稳定域 K+ε/4≤0.5）")]
        [Range(0f, 0.2f)] public float smoothA = 0.05f;
        [Tooltip("细纹理场反棋盘/粘性 ε（细场给大些=毛细碎波散得快）")]
        [Range(0f, 0.2f)] public float smoothB = 0.09f;
        [Tooltip("长波场每步阻尼（0.9939 → 幅度半衰期≈1.9s）")]
        [Range(0.98f, 1f)] public float dampA = 0.9939f;
        [Tooltip("细纹理场每步阻尼（0.985 → 半衰期≈0.7s，碎波先死）")]
        [Range(0.96f, 1f)] public float dampB = 0.985f;
        [Tooltip("长波冲量半径（米）——主环的大小")]
        public float sigmaA = 0.26f;
        [Tooltip("细纹理冲量半径（米）——尾波碎纹的大小")]
        public float sigmaB = 0.11f;
        [Tooltip("长波冲量增益")]
        public float gainA = 1.0f;
        [Tooltip("细纹理冲量增益")]
        public float gainB = 1.5f;
        public float dropStrength = 0.85f;

        [Header("输入")]
        public bool mouseClick = true;            // 左键点水
        public bool autoDemo = true;              // 自动演示滴落
        public float demoInterval = 2.4f;

        static readonly int P_Curr      = Shader.PropertyToID("_Curr");
        static readonly int P_Prev      = Shader.PropertyToID("_Prev");
        static readonly int P_Texel     = Shader.PropertyToID("_Texel");
        static readonly int P_Field     = Shader.PropertyToID("_FieldSize");
        static readonly int P_Damps     = Shader.PropertyToID("_Damps");
        static readonly int P_WaveKs    = Shader.PropertyToID("_WaveKs");
        static readonly int P_Smoothes  = Shader.PropertyToID("_Smoothes");
        static readonly int P_Drops     = Shader.PropertyToID("_Drops");
        static readonly int P_DropCount = Shader.PropertyToID("_DropCount");
        static readonly int P_DropShapes= Shader.PropertyToID("_DropShapes");
        static readonly int P_WaveTex   = Shader.PropertyToID("_PoolWaveTex");
        static readonly int P_WaveRect  = Shader.PropertyToID("_PoolWaveRect");

        const string SimShaderName = "WaterProject/PoolWaveUpdate";

        // 三缓冲轮转：步进开始时 _x=当前场(t-1)、_y=前一场(t-2)，新场写入 _z
        //（两张 RT 读-写会撞，波动方程要同时读 curr 和 prev，输出必须去第三张）
        RenderTexture _x, _y, _z;
        Material _mat;
        readonly System.Collections.Generic.List<Vector4> _pending = new();
        float _nextDemo;
        float _simAccum;                           // 固定步进累加器
        Camera _cam;

        void Awake()
        {
            Instance = this;
            _x = MakeRT("PoolWaveX");
            _y = MakeRT("PoolWaveY");
            _z = MakeRT("PoolWaveZ");
            var sh = Shader.Find(SimShaderName);
            if (sh == null) { Debug.LogError("[PoolRipples] 找不到 " + SimShaderName); enabled = false; return; }
            _mat = new Material(sh);
            _cam = GetComponentInParent<Camera>();
            if (_cam == null) _cam = Camera.main;
        }

        RenderTexture MakeRT(string name)
        {
            var rt = new RenderTexture(texW, texH, 0, RenderTextureFormat.RGHalf)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,     // 池壁反射 = Neumann 边界
            };
            rt.Create();
            return rt;
        }

        void OnDestroy()
        {
            if (_x) _x.Release();
            if (_y) _y.Release();
            if (_z) _z.Release();
            if (_mat) Destroy(_mat);
            Instance = null;
        }

        /// <summary>外部玩法层入口：世界坐标注波（扔球、角色入水都调这里）。</summary>
        public void SpawnDrop(Vector3 worldPos, float strength = 1f)
        {
            Vector2 uv = WorldToUv(worldPos);
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return;
            _pending.Add(new Vector4(uv.x, uv.y, strength * dropStrength, 0f));
        }

        /// <summary>清空波动场（自测用：保证只有一个已知冲量在场上）。</summary>
        public void ClearField()
        {
            foreach (var rt in new[] { _x, _y, _z })
            {
                if (rt == null) continue;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = prev;
            }
            _pending.Clear();
        }

        /// <summary>当前场快照（RGBAFloat，R=长波 G=细纹理；调用方负责 Destroy）。
        /// 波速自测(菜单101)用它逐时段量环半径——比从画面找环可靠一个量级。</summary>
        public Texture2D SnapshotField()
        {
            if (_x == null) return null;
            var tex = new Texture2D(texW, texH, TextureFormat.RGBAFloat, false);
            var prev = RenderTexture.active;
            RenderTexture.active = _x;
            tex.ReadPixels(new Rect(0, 0, texW, texH), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        Vector2 WorldToUv(Vector3 w)
        {
            // 与 shader 同一套映射：rect = (minX, minZ, sizeX, sizeZ)
            // ⚠ 语义必须是"左下角+尺寸"，不能是"中心+尺寸"——shader 端
            //   wuv=(wp.xy−rect.xy)/rect.zw 不做任何 ±0.5，两边各写各的 0.5
            //   迟早错位（实测错成半场镜像，水折叠成立墙）。
            Vector4 r = Shader.GetGlobalVector(P_WaveRect);
            if (r.z <= 0f) r = new Vector4(-fieldSize.x * 0.5f, -fieldSize.y * 0.5f, fieldSize.x, fieldSize.y);
            return new Vector2((w.x - r.x) / r.z,
                               (w.z - r.y) / r.w);
        }

        void Update()
        {
            if (_mat == null) return;

            // ── 收集输入 ──
            if (mouseClick && Input.GetMouseButtonDown(0) && _cam != null)
            {
                var ray = _cam.ScreenPointToRay(Input.mousePosition);
                // 与 y=0 平面求交（水面）
                float t = -ray.origin.y / Mathf.Min(ray.direction.y, -1e-4f);
                if (t > 0) SpawnDrop(ray.origin + ray.direction * t, 1f);
            }
            if (autoDemo && Time.time >= _nextDemo)
            {
                Vector3 p = new Vector3(Random.Range(-fieldSize.x * 0.42f, fieldSize.x * 0.42f),
                                        0f,
                                        Random.Range(-fieldSize.y * 0.42f, fieldSize.y * 0.42f));
                SpawnDrop(p, Random.Range(0.55f, 1.0f));
                _nextDemo = Time.time + demoInterval;
            }

            // ── 固定率步进（v2：波速不再跟着帧率走）──
            float dtStep = 1f / Mathf.Max(simHz, 10f);
            _simAccum += Mathf.Min(Time.deltaTime, 0.1f);
            int steps = Mathf.FloorToInt(_simAccum / dtStep);
            if (steps > maxCatchup) steps = maxCatchup;      // 大掉帧宁可慢放不追帧
            _simAccum -= steps * dtStep;
            if (_simAccum < 0f || _simAccum > dtStep) _simAccum = 0f;   // 数值护栏

            if (steps <= 0) { PublishGlobals(); return; }

            Vector4[] drops = new Vector4[8];
            int count = Mathf.Min(_pending.Count, 8);
            for (int i = 0; i < count; i++) drops[i] = _pending[i];
            if (_pending.Count > 8) _pending.RemoveRange(0, _pending.Count - 8);
            else _pending.Clear();

            for (int s = 0; s < steps; s++)
            {
                _mat.SetTexture(P_Curr, _x);
                _mat.SetTexture(P_Prev, _y);
                // ⚠ 显式传 texel：_Curr_TexelSize 自动填充在 Blit(null, dst) 通路里
                //   不可靠（实测为 0 → 四邻采样退化为中心 → 递推变 2h−p 线性漂移，
                //   水面整个漂上天）。texel 只跟 RT 尺寸有关，自己算最稳。
                _mat.SetVector(P_Texel, new Vector4(1f / texW, 1f / texH, texW, texH));
                _mat.SetVector(P_Field, fieldSize);
                _mat.SetVector(P_Damps,    new Vector4(dampA, dampB, 0f, 0f));
                _mat.SetVector(P_WaveKs,   new Vector4(waveKA, waveKB, 0f, 0f));
                _mat.SetVector(P_Smoothes, new Vector4(smoothA, smoothB, 0f, 0f));
                _mat.SetVector(P_DropShapes, new Vector4(sigmaA, sigmaB, gainA, gainB));
                // 冲量只随第一步注入（写回的是 _z，正好成为新的当前场）
                _mat.SetVectorArray(P_Drops, s == 0 ? drops : new Vector4[8]);
                _mat.SetInt(P_DropCount, s == 0 ? count : 0);
                Graphics.Blit(null, _z, _mat);
                (_x, _y, _z) = (_z, _x, _y);    // 轮转：新场进 curr 槽，最老的那张当写目标
            }

            PublishGlobals();
        }

        void PublishGlobals()
        {
            Shader.SetGlobalTexture(P_WaveTex, _x);
            // rect 语义 = (minX, minZ, sizeX, sizeZ)，与 WorldToUv / shader 三方一致
            Shader.SetGlobalVector(P_WaveRect, new Vector4(-fieldSize.x * 0.5f, -fieldSize.y * 0.5f,
                                                           fieldSize.x, fieldSize.y));
        }
    }
}
