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

        [Header("涌浪驱动（参考 C17）")]
        public bool waveOn = true;
        public float waveAmp = 0.85f;
        public float waveLen = 74f;
        public Vector2 waveDir = new Vector2(-0.175f, 0.985f);
        public float waveSpeed = 1f;
        [Range(0f, 1f)] public float waveForce = 0.9f;

        [Header("数值")]
        [Range(1, 60)] public int simHz = 60;
        [Range(0.1f, 3f)] public float flowK = 0.9f;
        [Range(0.9f, 1f)] public float damping = 0.997f;
        [Range(0f, 0.3f)] public float edgeBand = 0.05f;
        [Range(0f, 1f)] public float edgeK = 0.85f;

        [Header("泡沫历史场（P1-2 / 参考 S10 C18–C20）")]
        [Tooltip("关闭则泡沫历史 RT 不推进（水面看到的残留泡沫随之消失）")]
        public bool foamOn = true;
        [Tooltip("衰减 /秒：浪过留痕能撑多久（越大褪得越快）")]
        [Range(0f, 5f)] public float foamDecay = 0.8f;
        [Tooltip("生成速率（源 × 速率 × dt 累加进历史场）")]
        [Range(0f, 10f)] public float foamGen = 0.6f;
        [Tooltip("平流倍率：泡沫随水流漂移的强度")]
        [Range(0f, 20f)] public float foamAdvect = 1.0f;
        [Range(0f, 3f)] public float foamShore = 1.0f;
        [Tooltip("近岸生成水深（米）：浅于此开始出沫）")]
        [Range(0.1f, 20f)] public float foamShoreDepth = 2.0f;
        [Tooltip("浪峰曲率权重（水面拉普拉斯 → 白沫）")]
        [Range(0f, 5f)] public float foamCrest = 0.8f;
        [Tooltip("水面坡度权重")]
        [Range(0f, 5f)] public float foamSlope = 0.6f;
        [Tooltip("流速生成权重")]
        [Range(0f, 5f)] public float foamFlow = 0.8f;
        [Tooltip("流速参考值（超过它开始出沫）")]
        [Range(0.001f, 1f)] public float foamFlowRef = 0.05f;
        [Tooltip("噪声分量：把生成源咬成丝缕状（1=全噪声，0=均匀白板）")]
        [Range(0f, 1f)] public float foamNoise = 0.75f;
        [Tooltip("噪声尺度（1/米）：越大越碎")]
        [Range(0.005f, 0.5f)] public float foamNoiseScale = 0.08f;

        [Header("可视化")]
        [Range(1, 5)] public int vizMode = 1;

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
        static readonly int P_WaveOn  = Shader.PropertyToID("_SWWaveOn");
        static readonly int P_WaveAmp = Shader.PropertyToID("_SWWaveAmp");
        static readonly int P_WaveLen = Shader.PropertyToID("_SWWaveLen");
        static readonly int P_WaveDir = Shader.PropertyToID("_SWWaveDir");
        static readonly int P_WaveSpd = Shader.PropertyToID("_SWWaveSpeed");
        static readonly int P_WaveFrc = Shader.PropertyToID("_SWWaveForce");
        static readonly int P_SWTime  = Shader.PropertyToID("_SWTime");
        static readonly int P_Foam    = Shader.PropertyToID("_SWFoam");
        static readonly int P_FoamPrev= Shader.PropertyToID("_SWFoamPrev");
        static readonly int P_FoamDt  = Shader.PropertyToID("_SWFoamDt");
        static readonly int P_FoamDecay = Shader.PropertyToID("_SWFoamDecay");
        static readonly int P_FoamGen   = Shader.PropertyToID("_SWFoamGen");
        static readonly int P_FoamAdvect= Shader.PropertyToID("_SWFoamAdvect");
        static readonly int P_FoamShore = Shader.PropertyToID("_SWFoamShore");
        static readonly int P_FoamShoreD= Shader.PropertyToID("_SWFoamShoreD");
        static readonly int P_FoamCrest = Shader.PropertyToID("_SWFoamCrest");
        static readonly int P_FoamSlope = Shader.PropertyToID("_SWFoamSlope");
        static readonly int P_FoamFlow  = Shader.PropertyToID("_SWFoamFlow");
        static readonly int P_FoamFlowRef = Shader.PropertyToID("_SWFoamFlowRef");
        static readonly int P_FoamNoise   = Shader.PropertyToID("_SWFoamNoise");
        static readonly int P_FoamNoiseScale = Shader.PropertyToID("_SWFoamNoiseScale");

        Material _mat;
        RenderTexture _stateA, _stateB, _velA, _velB;
        RenderTexture _stateCur, _velCur;
        RenderTexture _foamA, _foamB, _foamCur;
        float _acc;
        bool _ready;
        float _simTime;

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
            _foamA  = NewRT("SW_FoamA");  _foamB  = NewRT("SW_FoamB");
            _stateCur = _stateA; _velCur = _velA; _foamCur = _foamA;

            InitState();
            InitFoam();
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

        void InitFoam()
        {
            // 泡沫历史清零：blit 一张全黑小图（避免首帧读到未定义内容）
            var t = new Texture2D(4, 4, TextureFormat.RGBAHalf, false);
            var st = new Color[16];
            for (int i = 0; i < 16; i++) st[i] = new Color(0, 0, 0, 1);
            t.SetPixels(st); t.Apply();
            Graphics.Blit(t, _foamCur);
            DestroyImmediate(t);
        }

        void PublishGlobals()
        {
            if (_mat == null) return;
            Shader.SetGlobalTexture(P_State, _stateCur);
            Shader.SetGlobalTexture(P_Vel, _velCur);
            Shader.SetGlobalTexture(P_Foam, _foamCur);
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

            // 涌浪驱动（参考 C17）
            _mat.SetFloat(P_WaveOn, waveOn ? 1f : 0f);
            _mat.SetFloat(P_WaveAmp, waveAmp);
            _mat.SetFloat(P_WaveLen, waveLen);
            _mat.SetVector(P_WaveDir, new Vector4(waveDir.x, waveDir.y, 0f, 0f));
            _mat.SetFloat(P_WaveSpd, waveSpeed);
            _mat.SetFloat(P_WaveFrc, waveForce);
            _mat.SetFloat(P_SWTime, _simTime);
            _simTime += 1f / Mathf.Max(simHz, 1);

            // Height：stateCur → stateNext
            _mat.SetTexture(P_State, _stateCur);
            Graphics.Blit(null, _stateCur == _stateA ? _stateB : _stateA, _mat, 0);
            _stateCur = (_stateCur == _stateA) ? _stateB : _stateA;

            // Vel：stateCur → velNext
            _mat.SetTexture(P_State, _stateCur);
            Graphics.Blit(null, _velCur == _velA ? _velB : _velA, _mat, 1);
            _velCur = (_velCur == _velA) ? _velB : _velA;

            StepFoam(1f / Mathf.Max(simHz, 1));

            PublishGlobals();
        }

        // 泡沫历史场：生成 + 半拉格朗日平流 + 衰减（Pass 3）
        void StepFoam(float dt)
        {
            if (!foamOn) return;
            _mat.SetTexture(P_State, _stateCur);
            _mat.SetTexture(P_Vel,   _velCur);
            _mat.SetTexture(P_FoamPrev, _foamCur);
            _mat.SetVector(P_Domain, new Vector4(minX, minZ, sizeX, sizeZ));
            _mat.SetFloat(P_GridN, gridN);
            _mat.SetFloat(P_FoamDt, dt);
            _mat.SetFloat(P_FoamDecay, foamDecay);
            _mat.SetFloat(P_FoamGen, foamGen);
            _mat.SetFloat(P_FoamAdvect, foamAdvect);
            _mat.SetFloat(P_FoamShore, foamShore);
            _mat.SetFloat(P_FoamShoreD, foamShoreDepth);
            _mat.SetFloat(P_FoamCrest, foamCrest);
            _mat.SetFloat(P_FoamSlope, foamSlope);
            _mat.SetFloat(P_FoamFlow, foamFlow);
            _mat.SetFloat(P_FoamFlowRef, foamFlowRef);
            _mat.SetFloat(P_FoamNoise, foamNoise);
            _mat.SetFloat(P_FoamNoiseScale, foamNoiseScale);
            _mat.SetFloat(P_SWTime, _simTime);
            Graphics.Blit(null, _foamCur == _foamA ? _foamB : _foamA, _mat, 3);
            _foamCur = (_foamCur == _foamA) ? _foamB : _foamA;
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
            _mat.SetTexture(P_Foam, _foamCur);
            if (terrainTex != null) _mat.SetTexture(P_Terrain, terrainTex);
            _mat.SetFloat(P_GridN, gridN);
            _mat.SetFloat(P_VizMode, mode);
            var outRT = RenderTexture.GetTemporary(gridN, gridN, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(null, outRT, _mat, 2);
            return outRT;
        }

        public void Release()
        {
            foreach (var rt in new[] { _stateA, _stateB, _velA, _velB, _foamA, _foamB })
                if (rt) { rt.Release(); DestroyImmediate(rt); }
            _stateA = _stateB = _velA = _velB = _foamA = _foamB = null;
            _ready = false;
        }

        void OnDisable()
        {
            Shader.SetGlobalTexture(P_State, null);
            Shader.SetGlobalTexture(P_Vel, null);
            Shader.SetGlobalTexture(P_Foam, null);
        }
        void OnDestroy() { Release(); if (_mat) DestroyImmediate(_mat); }
    }
}
