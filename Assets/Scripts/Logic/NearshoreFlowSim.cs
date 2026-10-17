// NearshoreFlowSim.cs — 逻辑层：近岸浅水流场模拟驱动（P0-1）
//
// 职责：
//   1) 持有 ping-pong RT（水深/地形状态 + 速度场），逐帧跑 ShallowWaterSim.shader；
//   2) 初始化地形（由 Editor 端采样真实网格烘焙的 SW_Terrain 贴图，遵守"岸线只写一处"纪律）；
//   3) 向全局暴露 _SWState / _SWVel / _SWTerrain / _SWDomain，供水面 Shader 与调试图读取。
//
// 与参考视频（S9 / C14–C17）的对应：
//   干湿边界 = 水深 h>0；水位 = 地形+水深；速度 = 净通量方向（高度差推动）。
//   边界吸收 = shader 里 EdgeW()；摩擦阻尼 = _SWDamping。
//   （外海浪 FFT 驱动 = M3，见 Docs/学习路径与知识库/检测报告/R01_*.md §4。）
using UnityEngine;

namespace WaterProject.Logic
{
    public class NearshoreFlowSim : MonoBehaviour
    {
        [Header("模拟域（世界 XZ 矩形）")]
        public float minX = -420f, minZ = -120f, sizeX = 840f, sizeZ = 240f;
        [Tooltip("网格分辨率（方形）")]
        public int gridN = 256;

        [Header("水")]
        public float waterLevel = 0f;      // 静水位 y（世界米）
        [Tooltip("潮差倾斜：海面离岸越高（>0）→ 产生向岸水位梯度（参考 C15 用）")]
        public float tideSlope = 0.012f;
        [Header("地形（Editor 端采样真实网格烘焙；R=地形 y）")]
        public Texture2D terrainTex;
        [Tooltip("初始状态贴图（Editor 烘焙：R=h G=terrain B=wet A=0）；留空则由 terrainTex 现算")]
        public Texture2D state0Tex;

        [Header("数值")]
        [Range(1, 60)] public int simHz = 60;
        [Range(0.1f, 3f)] public float flowK = 0.9f;
        [Range(0.9f, 1f)] public float damping = 0.997f;
        [Range(0f, 0.3f)] public float edgeBand = 0.05f;
        [Range(0f, 1f)] public float edgeK = 0.85f;

        [Header("可视化")]
        [Range(1, 3)] public int vizMode = 1;

        const string ShaderName = "WaterProject/ShallowWaterSim";
        static readonly int P_State   = Shader.PropertyToID("_SWState");
        static readonly int P_Vel     = Shader.PropertyToID("_SWVel");
        static readonly int P_Terrain = Shader.PropertyToID("_SWTerrain");
        static readonly int P_Domain  = Shader.PropertyToID("_SWDomain");
        static readonly int P_GridN   = Shader.PropertyToID("_SWGridN");
        static readonly int P_WaterY  = Shader.PropertyToID("_SWWaterY");
        static readonly int P_Dt      = Shader.PropertyToID("_SWDt");
        static readonly int P_FlowK   = Shader.PropertyToID("_SWFlowK");
        static readonly int P_Damping = Shader.PropertyToID("_SWDamping");
        static readonly int P_EdgeBand= Shader.PropertyToID("_SWEdgeBand");
        static readonly int P_EdgeK   = Shader.PropertyToID("_SWEdgeK");
        static readonly int P_VizMode = Shader.PropertyToID("_SWVizMode");

        Material _mat;
        RenderTexture _stateA, _stateB, _velA, _velB;
        RenderTexture _stateCur, _velCur;
        float _acc;
        bool _ready;

        public bool Ready => _ready;
        public RenderTexture StateCur => _stateCur;

        RenderTexture NewRT(string nm)
        {
            var rt = new RenderTexture(gridN, gridN, 0, RenderTextureFormat.ARGBHalf)
            { name = nm, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        public void Build()
        {
            Release();
            var sh = Shader.Find(ShaderName);
            if (sh == null) { Debug.LogError("[NearshoreFlowSim] 找不到 " + ShaderName); return; }
            _mat = new Material(sh) { hideFlags = HideFlags.DontSave };

            _stateA = NewRT("SW_StateA"); _stateB = NewRT("SW_StateB");
            _velA   = NewRT("SW_VelA");   _velB   = NewRT("SW_VelB");
            _stateCur = _stateA; _velCur = _velA;

            InitState();
            PublishGlobals();
            _ready = true;
            Debug.Log($"[NearshoreFlowSim] built {gridN}x{gridN} domain({minX},{minZ},{sizeX},{sizeZ}) waterLevel={waterLevel}");
        }

        void InitState()
        {
            Texture2D src = state0Tex;
            if (src == null && terrainTex != null && terrainTex.isReadable)
            {
                // 由地形现算初始状态：h = max(waterLevel - terrain, 0)
                int n = terrainTex.width;
                var px = terrainTex.GetPixelData<Color>(0);
                var st = new Color[n * n];
                for (int i = 0; i < n; i++)
                {
                    float terr = px[i].r;
                    float h = Mathf.Max(waterLevel - terr, 0f);
                    st[i] = new Color(h, terr, h > 1e-4f ? 1f : 0f, 0f);
                }
                var t = new Texture2D(n, n, TextureFormat.RGBAHalf, false);
                t.SetPixels(st); t.Apply();
                Graphics.Blit(t, _stateCur);
                DestroyImmediate(t);
            }
            else if (src != null)
            {
                Graphics.Blit(src, _stateCur);
            }
            else
            {
                // 兜底：全干
                var t = new Texture2D(4, 4, TextureFormat.RGBAHalf, false);
                var st = new Color[16];
                for (int i = 0; i < 16; i++) st[i] = new Color(0, 0, 0, 0);
                t.SetPixels(st); t.Apply();
                Graphics.Blit(t, _stateCur);
                DestroyImmediate(t);
            }
            Graphics.Blit(_stateCur, _velCur); // 速度清零（用状态图占位，R/G 后续被覆写）
        }

        void PublishGlobals()
        {
            if (_mat == null) return;
            Shader.SetGlobalTexture(P_State, _stateCur);
            Shader.SetGlobalTexture(P_Vel, _velCur);
            if (terrainTex != null) Shader.SetGlobalTexture(P_Terrain, terrainTex);
            Shader.SetGlobalVector(P_Domain, new Vector4(minX, minZ, sizeX, sizeZ));
        }

        void Step()
        {
            if (_mat == null) return;
            _mat.SetVector(P_Domain, new Vector4(minX, minZ, sizeX, sizeZ));
            _mat.SetFloat(P_GridN, gridN);
            _mat.SetFloat(P_WaterY, waterLevel);
            _mat.SetFloat(P_Dt, 1f / Mathf.Max(simHz, 1));
            _mat.SetFloat(P_FlowK, flowK);
            _mat.SetFloat(P_Damping, damping);
            _mat.SetFloat(P_EdgeBand, edgeBand);
            _mat.SetFloat(P_EdgeK, edgeK);

            // Height：stateCur → stateNext
            _mat.SetTexture(P_State, _stateCur);
            Graphics.Blit(null, _stateCur == _stateA ? _stateB : _stateA, _mat, 0);
            _stateCur = (_stateCur == _stateA) ? _stateB : _stateA;

            // Vel：stateCur → velNext
            _mat.SetTexture(P_State, _stateCur);
            Graphics.Blit(null, _velCur == _velA ? _velB : _velA, _mat, 1);
            _velCur = (_velCur == _velA) ? _velB : _velA;

            PublishGlobals();
        }

        void Update()
        {
            if (!_ready)
            {
                if (!Application.isPlaying) return;   // 编辑模式不自动建，等菜单 90
                Build();
                if (!_ready) return;
            }
            _acc += Time.unscaledDeltaTime;
            float step = 1f / Mathf.Max(simHz, 1);
            int guard = 0;
            while (_acc >= step && guard++ < 8) { Step(); _acc -= step; }
        }

        /// <summary>编辑模式预热：手动推进 n 步（导出调试图前用，让速度场显现）。</summary>
        public void Warmup(int steps)
        {
            for (int i = 0; i < steps; i++) Step();
        }

        /// <summary>把当前状态可视化到一张 RT（供 Editor 导出 PNG 做检测报告）。</summary>
        public RenderTexture RenderViz(int mode)        {
            if (_mat == null) return null;
            _mat.SetTexture(P_State, _stateCur);
            _mat.SetTexture(P_Vel, _velCur);
            if (terrainTex != null) _mat.SetTexture(P_Terrain, terrainTex);
            _mat.SetFloat(P_GridN, gridN);
            _mat.SetFloat(P_VizMode, mode);
            var outRT = RenderTexture.GetTemporary(gridN, gridN, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(null, outRT, _mat, 2);
            return outRT;
        }

        public void Release()
        {
            foreach (var rt in new[] { _stateA, _stateB, _velA, _velB })
                if (rt) { rt.Release(); DestroyImmediate(rt); }
            _stateA = _stateB = _velA = _velB = null;
            _ready = false;
        }

        void OnDisable()
        {
            Shader.SetGlobalTexture(P_State, null);
            Shader.SetGlobalTexture(P_Vel, null);
        }
        void OnDestroy() { Release(); if (_mat) DestroyImmediate(_mat); }
    }
}
