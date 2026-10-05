// PoolRipples.cs — 泳池波动方程的双缓冲管理器（B 档交互，见《07》§1）
//
// 职责：
//   1) 持有两张 RHalf 高度场 RT（12×6m 池域 → 256×128，方格 4.7cm）；
//   2) 每帧跑 stepsPerFrame 步 PoolWaveUpdate（写回目标 = prev 槽，写完换位）；
//   3) 收集交互（鼠标点水 / 自动演示 / 外部 API SpawnDrop）→ 高斯冲量注入；
//   4) 全局暴露 _PoolWaveTex / _PoolWaveRect 供水面 shader 采样。
//
// 与主线 WaterInteractionManager 的关系：**并行不替换**。主线是"解析贴花"
// （无传播），泳池是"波动场"（会传播/反射/干涉）——前者服务开海大场面，
// 后者服务泳池特写，《07》§2 有选型论证。
//
// 注波语义：冲量只在"出生后的第一次步进"注入一次（pending 清空），
// 不是持续外力——持续外力会把波动方程推成单调水位漂移。
using UnityEngine;

namespace WaterProject.Logic
{
    public class PoolRipples : MonoBehaviour
    {
        [Header("模拟域（池内水面，米）")]
        public Vector2 fieldSize = new Vector2(12f, 6f);

        [Header("模拟参数")]
        public int texW = 256;
        public int texH = 128;                    // 12:6 = 2:1 ⇒ 方格 4.7cm
        [Range(0.95f, 1.0f)] public float damping = 0.996f;
        public int stepsPerFrame = 2;             // 步数越多传播越快，仍无条件稳定
        public float dropSigma = 0.24f;           // 注波高斯半径（米）
        public float dropStrength = 0.85f;

        [Header("输入")]
        public bool mouseClick = true;            // 左键点水
        public bool autoDemo = true;              // 自动演示滴落
        public float demoInterval = 2.4f;

        static readonly int P_Curr      = Shader.PropertyToID("_Curr");
        static readonly int P_Prev      = Shader.PropertyToID("_Prev");
        static readonly int P_Texel     = Shader.PropertyToID("_Texel");
        static readonly int P_Field     = Shader.PropertyToID("_FieldSize");
        static readonly int P_Damping   = Shader.PropertyToID("_Damping");
        static readonly int P_Drops     = Shader.PropertyToID("_Drops");
        static readonly int P_DropCount = Shader.PropertyToID("_DropCount");
        static readonly int P_Sigma     = Shader.PropertyToID("_DropSigma");
        static readonly int P_WaveTex   = Shader.PropertyToID("_PoolWaveTex");
        static readonly int P_WaveRect  = Shader.PropertyToID("_PoolWaveRect");

        const string SimShaderName = "WaterProject/PoolWaveUpdate";

        // 三缓冲轮转：步进开始时 _x=当前场(t-1)、_y=前一场(t-2)，新场写入 _z
        //（两张 RT 读-写会撞，波动方程要同时读 curr 和 prev，输出必须去第三张）
        RenderTexture _x, _y, _z;
        Material _mat;
        readonly System.Collections.Generic.List<Vector4> _pending = new();
        float _nextDemo;
        Camera _cam;

        public static PoolRipples Instance { get; private set; }

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
            var rt = new RenderTexture(texW, texH, 0, RenderTextureFormat.RHalf)
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
                if (t > 0f) SpawnDrop(ray.origin + ray.direction * t, 1f);
            }
            if (autoDemo && Time.time >= _nextDemo)
            {
                Vector3 p = new Vector3(Random.Range(-fieldSize.x * 0.42f, fieldSize.x * 0.42f),
                                        0f,
                                        Random.Range(-fieldSize.y * 0.42f, fieldSize.y * 0.42f));
                SpawnDrop(p, Random.Range(0.55f, 1.0f));
                _nextDemo = Time.time + demoInterval;
            }

            // ── 步进 ──
            Vector4[] drops = new Vector4[8];
            int count = Mathf.Min(_pending.Count, 8);
            for (int i = 0; i < count; i++) drops[i] = _pending[i];
            if (_pending.Count > 8) _pending.RemoveRange(0, _pending.Count - 8);
            else _pending.Clear();

            for (int s = 0; s < stepsPerFrame; s++)
            {
                _mat.SetTexture(P_Curr, _x);
                _mat.SetTexture(P_Prev, _y);
                // ⚠ 显式传 texel：_Curr_TexelSize 自动填充在 Blit(null, dst) 通路里
                //   不可靠（实测为 0 → 四邻采样退化为中心 → 递推变 2h−p 线性漂移，
                //   水面整个漂上天）。texel 只跟 RT 尺寸有关，自己算最稳。
                _mat.SetVector(P_Texel, new Vector4(1f / texW, 1f / texH, texW, texH));
                _mat.SetVector(P_Field, fieldSize);
                _mat.SetFloat(P_Damping, damping);
                _mat.SetFloat(P_Sigma, dropSigma);
                // 冲量只随第一步注入（写回的是 _z，正好成为新的当前场）
                _mat.SetVectorArray(P_Drops, s == 0 ? drops : new Vector4[8]);
                _mat.SetInt(P_DropCount, s == 0 ? count : 0);
                Graphics.Blit(null, _z, _mat);
                (_x, _y, _z) = (_z, _x, _y);    // 轮转：新场进 curr 槽，最老的那张当写目标
            }

            Shader.SetGlobalTexture(P_WaveTex, _x);
            // rect 语义 = (minX, minZ, sizeX, sizeZ)，与 WorldToUv / shader 三方一致
            Shader.SetGlobalVector(P_WaveRect, new Vector4(-fieldSize.x * 0.5f, -fieldSize.y * 0.5f,
                                                           fieldSize.x, fieldSize.y));
        }
    }
}
