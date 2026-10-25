// FFTOceanSim.cs — R2 FFT 多级海洋的运行时管理器（《08》S1/C1~C4）
//
// 职责：
//   1) 持三级频谱资产（L=250/60/15m，N=256）：h0 初始化一次，谱随时间演化；
//   2) 每固定步（1/60s × 时间倍率，参考 C4 的统一时钟）跑完整 GPU 管线：
//      UpdateSpec → BitRev → 16 级蝶形（两轴×8）×2 字段 → Pack → GradJacob；
//   3) 全局发布 _FFTL{i}Disp/_Grad/_Info 供水面 shader 采样（uv=世界XZ/L，Repeat 平铺）；
//   4) 水面网格跟随相机 XZ（uv 用世界坐标 ⇒ 波形世界锚定，跟随不游泳）；
//   5) SelfTest()：δ谱单元自检——单一 k 的谱必须给出解析平面波（幅值+色散 ω=√(gk)），
//      这是 M1 的门禁（FFT/位反转/符号/归一化任何一环错都会在这里现形）。
using UnityEngine;

namespace WaterProject.Logic
{
    public class FFTOceanSim : MonoBehaviour
    {
        public static FFTOceanSim Instance { get; private set; }

        [Header("风况（C1）")]
        public float windSpeed = 9f;
        [Range(0, 360)] public float windDirDeg = 35f;
        [Tooltip("整体波高缩放（探针定标 985：L0 显著波高 ~0.8m，海况3；menu90 每次重写）")]
        public float ampScale = 985f;

        [Header("时间（C4 统一时钟：60Hz 固定步长 + 倍率）")]
        [Tooltip("1.1 = R1 四判据对参考 S1 定标（能量过线且闪烁主频留带内余量）")]
        public float timeMultiplier = 1.1f;
        public float fixedStep = 1f / 60f;

        [Header("水平挤压 Choppy（S2 演示开关）")]
        [Range(0, 1.5f)] public float choppy = 1f;

        [Header("浪峰泡沫（S5，Jacobian 阈值在 shader 侧）")]
        [Range(0, 2)] public float foamScale = 1f;

        [Header("水面网格（跟随相机）")]
        public Transform waterPlane;

        [Header("资产引用（菜单 90 赋值，唯一写入点）")]
        public ComputeShader computeShader;

        const int N = 256;
        const int LOG2N = 8;

        // 级配置：边长 L、幅值系数、随距离淡出（R1 实测：远机位下细级淡出太早，
        // 水面像素能量只有参考 S1 的 2%——淡出距离按"近景机位 y6/z-16"放宽）
        static readonly (float L, float amp, float fadeA, float fadeB)[] Levels =
        {
            (250f, 1.00f, 1e9f, 1e9f),
            (60f,  0.55f, 120f, 350f),
            (15f,  0.34f, 40f,  140f),   // R32 细级 0.28→0.34：浪峰碎粒感+细尺度折叠（泡沫颗粒）
        };

        ComputeShader _cs;
        int _kInit, _kUpdate, _kBitRev, _kStage, _kPack, _kGrad;

        class LevelRT
        {
            public RenderTexture H0, SpecA, SpecB;
            public RenderTexture A0, A1, B0, B1;      // FFT ping-pong
            public RenderTexture Disp, Grad;          // 输出（Repeat 平铺）
        }
        LevelRT[] _rts;

        float _simTime;
        float _acc;
        bool _inited;

        // 自检转储（探针取证用）
        float[] _dbgRow; int _dbgBi; float _dbgMax, _dbgMin;

        // FPS 探针
        int _fpsN; float _fpsAcc;
        public float AvgMs { get; private set; }

        static readonly int P_Sim = Shader.PropertyToID("_Sim");
        static readonly int P_Wind = Shader.PropertyToID("_Wind");
        static readonly int P_Grid = Shader.PropertyToID("_Grid");
        static readonly int P_Pass = Shader.PropertyToID("_Pass");
        static readonly int P_Axis = Shader.PropertyToID("_Axis");
        static readonly int P_Sign = Shader.PropertyToID("_Sign");
        static readonly int P_Src = Shader.PropertyToID("_Src");
        static readonly int P_SrcB = Shader.PropertyToID("_SrcB");
        static readonly int P_Dst = Shader.PropertyToID("_Dst");
        static readonly int P_H0 = Shader.PropertyToID("_H0");
        static readonly int P_SpecA = Shader.PropertyToID("_SpecA");
        static readonly int P_SpecB = Shader.PropertyToID("_SpecB");
        static readonly int P_Disp = Shader.PropertyToID("_Disp");
        static readonly int P_Grad = Shader.PropertyToID("_Grad");
        static readonly int P_SunDir = Shader.PropertyToID("_FFTSunDir");
        static readonly int P_Global = Shader.PropertyToID("_FFTGlobal");
        static readonly int P_Delta = Shader.PropertyToID("_Delta");

        void Awake()
        {
            Instance = this;
            _cs = computeShader;
        }

        void Start() => Init();

        public void Init()
        {
            // 编辑器菜单也可能调 Init（Awake 未跑、_cs 还是 null）——这里补赋一次
            _cs = computeShader;
            if (_cs == null)
            {
                Debug.LogError("[FFTOceanSim] computeShader 未赋值（菜单 Tools/FFT/90 会赋）");
                enabled = false;
                return;
            }
            if (_rts != null) OnDestroyRelease();   // 重入先放旧 RT，防泄漏

            _kInit = _cs.FindKernel("InitH0");
            _kUpdate = _cs.FindKernel("UpdateSpec");
            _kBitRev = _cs.FindKernel("BitRev");
            _kStage = _cs.FindKernel("FFTStage");
            _kPack = _cs.FindKernel("Pack");
            _kGrad = _cs.FindKernel("GradJacob");

            _rts = new LevelRT[Levels.Length];
            for (int i = 0; i < Levels.Length; i++)
            {
                var r = new LevelRT
                {
                    H0 = MakeRT(N, FilterMode.Point, TextureWrapMode.Repeat),
                    SpecA = MakeRT(N, FilterMode.Point, TextureWrapMode.Repeat),
                    SpecB = MakeRT(N, FilterMode.Point, TextureWrapMode.Repeat),
                    A0 = MakeRT(N, FilterMode.Point, TextureWrapMode.Repeat),
                    A1 = MakeRT(N, FilterMode.Point, TextureWrapMode.Repeat),
                    B0 = MakeRT(N, FilterMode.Point, TextureWrapMode.Repeat),
                    B1 = MakeRT(N, FilterMode.Point, TextureWrapMode.Repeat),
                    Disp = MakeRT(N, FilterMode.Bilinear, TextureWrapMode.Repeat),
                    Grad = MakeRT(N, FilterMode.Bilinear, TextureWrapMode.Repeat),
                };
                _rts[i] = r;

                // h0 只生成一次
                float ra = Mathf.Deg2Rad * windDirDeg;
                _cs.SetVector(P_Sim, new Vector4(0f, Mathf.Sin(ra), Mathf.Cos(ra), choppy));
                _cs.SetVector(P_Wind, new Vector4(windSpeed, ampScale, 0f, 7.7f + i * 3.1f));
                _cs.SetVector(P_Grid, new Vector4(N, Levels[i].L, 0f, 0f));
                _cs.SetTexture(_kInit, P_H0, r.H0);
                _cs.Dispatch(_kInit, N / 8, N / 8, 1);
            }
            _inited = true;
        }

        static RenderTexture MakeRT(int n, FilterMode fm, TextureWrapMode wrap)
        {
            var rt = new RenderTexture(n, n, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = true,
                filterMode = fm,
                wrapMode = wrap,
                name = "FFT_RT",
            };
            rt.Create();
            return rt;
        }

        void OnDestroy()
        {
            OnDestroyRelease();
            if (Instance == this) Instance = null;
        }

        void OnDestroyRelease()
        {
            if (_rts == null) return;
            foreach (var r in _rts)
            {
                if (r == null) continue;
                r.H0?.Release(); r.SpecA?.Release(); r.SpecB?.Release();
                r.A0?.Release(); r.A1?.Release(); r.B0?.Release(); r.B1?.Release();
                r.Disp?.Release(); r.Grad?.Release();
            }
            _rts = null;
        }

        void Update()
        {
            if (!_inited) return;

            // FPS 探针（采集的是无截图干扰的正常运行帧）
            _fpsN++; _fpsAcc += Time.unscaledDeltaTime;
            if (_fpsN >= 90) { AvgMs = _fpsAcc / _fpsN * 1000f; _fpsN = 0; _fpsAcc = 0f; }

            // 统一时钟：固定步长 × 倍率；帧率掉了也不让模拟变慢（封顶防螺旋）
            _acc += Time.deltaTime;
            int guard = 0;
            while (_acc >= fixedStep && guard < 4)
            {
                _simTime += fixedStep * timeMultiplier;
                StepAll();
                _acc -= fixedStep;
                guard++;
            }
            if (guard >= 4) _acc = 0f;

            PublishGlobals();
        }

        void StepAll()
        {
            float ra = Mathf.Deg2Rad * windDirDeg;
            for (int i = 0; i < Levels.Length; i++)
            {
                var r = _rts[i];
                _cs.SetVector(P_Sim, new Vector4(_simTime, Mathf.Sin(ra), Mathf.Cos(ra), choppy));
                _cs.SetVector(P_Wind, new Vector4(windSpeed, ampScale, 0f, 7.7f + i * 3.1f));
                _cs.SetVector(P_Grid, new Vector4(N, Levels[i].L, 0f, 0f));

                _cs.SetTexture(_kUpdate, P_H0, r.H0);
                _cs.SetTexture(_kUpdate, P_SpecA, r.SpecA);
                _cs.SetTexture(_kUpdate, P_SpecB, r.SpecB);
                _cs.Dispatch(_kUpdate, N / 8, N / 8, 1);

                var aFin = FFTField(r.SpecA, r.A0, r.A1);
                var bFin = FFTField(r.SpecB, r.B0, r.B1);

                _cs.SetTexture(_kPack, P_Src, aFin);
                _cs.SetTexture(_kPack, P_SrcB, bFin);
                _cs.SetTexture(_kPack, P_Disp, r.Disp);
                _cs.Dispatch(_kPack, N / 8, N / 8, 1);

                _cs.SetTexture(_kGrad, P_Disp, r.Disp);
                _cs.SetTexture(_kGrad, P_Grad, r.Grad);
                _cs.Dispatch(_kGrad, N / 8, N / 8, 1);
            }
        }

        /// <summary>一个字段集合的完整二维 IFFT：位反转 → 8 行级 + 8 列级（ping-pong）。返回最终落点。</summary>
        RenderTexture FFTField(RenderTexture src, RenderTexture p0, RenderTexture p1)
        {
            _cs.SetInt(P_Sign, 1);                       // 逆变换（谱→空间）
            _cs.SetInt(P_Axis, 0);
            _cs.SetTexture(_kBitRev, P_Src, src);
            _cs.SetTexture(_kBitRev, P_Dst, p0);
            _cs.Dispatch(_kBitRev, N / 8, N / 8, 1);

            var read = p0; var write = p1;
            for (int axis = 0; axis < 2; axis++)
            {
                _cs.SetInt(P_Axis, axis);
                for (int p = 0; p < LOG2N; p++)
                {
                    _cs.SetInt(P_Pass, p);
                    _cs.SetTexture(_kStage, P_Src, read);
                    _cs.SetTexture(_kStage, P_Dst, write);
                    _cs.Dispatch(_kStage, N / 8, N / 8, 1);
                    (read, write) = (write, read);
                }
            }
            return read;
        }

        void PublishGlobals()
        {
            for (int i = 0; i < Levels.Length; i++)
            {
                Shader.SetGlobalTexture("_FFTL" + i + "Disp", _rts[i].Disp);
                Shader.SetGlobalTexture("_FFTL" + i + "Grad", _rts[i].Grad);
                Shader.SetGlobalVector("_FFTL" + i + "Info",
                    new Vector4(Levels[i].L, Levels[i].fadeA, Levels[i].fadeB, Levels[i].amp));
            }
            var sun = RenderSettings.sun;
            if (sun == null)
            {
                var l = FindFirstObjectByType<Light>();
                if (l != null && l.type == LightType.Directional) sun = l;
            }
            if (sun != null)
                Shader.SetGlobalVector(P_SunDir, -sun.transform.forward);
            Shader.SetGlobalVector(P_Global, new Vector4(choppy, foamScale, _simTime, 0f));
        }

        void LateUpdate()
        {
            // 水面网格跟随相机（uv 取世界坐标 ⇒ 波形不动，只是网格包住相机周围）
            var cam = Camera.main;
            if (cam != null && waterPlane != null)
            {
                var p = waterPlane.position;
                p.x = cam.transform.position.x;
                p.z = cam.transform.position.z;
                waterPlane.position = p;
            }
        }

        // ── M1 门禁：δ谱单元自检 ────────────────────────────────
        // 单一 k0 谱 ⇒ h(x) 必须是解析平面波 (2a/N²)·(−1)^(x+y)·cos(k0x+ωt)；
        // 由 t1/t2 两拍波峰位移反推相速度，与 √(gk0)/k0 理论比对。
        public string SelfTest()
        {
            if (!_inited) Init();
            var r = _rts[0];
            float L = Levels[0].L;
            int dx = 8;                                    // k0 = 2π·8/L（波长 31.25m）
            float k0 = 2f * Mathf.PI * dx / L;
            float omega = Mathf.Sqrt(9.81f * k0);

            // GPU 上直接写 δ 谱（行波构造）：
            //   本格 h0(k0)=(a,0)、conj(h0(−k0))=0 ⇒ H(k0,t)=a·e^{iωt}（纯正向频率）
            //   镜像格 h0(−k0)=0、conj(h0(k0))=(a,0) ⇒ H(−k0,t)=a·e^{−iωt}
            //   ⇒ h(x) = (2a/N²)·cos(k0x+ωt)，向 −x 传播
            // ⚈ 曾用 CPU Texture2D 绑 _H0（RW 槽）——静默失败，谱恒空
            const float a = 8f;   // 幅值缩放探针：读数应随 a 线性（否则读的不是 δ 波）
            int kInitDelta = _cs.FindKernel("InitDelta");

            float ReadCrest(float t, out float amp)
            {
                _simTime = t;
                float ra = Mathf.Deg2Rad * windDirDeg;
                _cs.SetVector(P_Sim, new Vector4(t, Mathf.Sin(ra), Mathf.Cos(ra), choppy));
                _cs.SetVector(P_Wind, new Vector4(windSpeed, ampScale, 0f, 0f));
                _cs.SetVector(P_Grid, new Vector4(N, L, 0f, 0f));
                _cs.SetVector(P_Delta, new Vector4(N / 2 + dx, N / 2, a, 0f));
                _cs.SetTexture(kInitDelta, P_H0, r.H0);
                _cs.Dispatch(kInitDelta, N / 8, N / 8, 1);
                _cs.SetTexture(_kUpdate, P_H0, r.H0);
                _cs.SetTexture(_kUpdate, P_SpecA, r.SpecA);
                _cs.SetTexture(_kUpdate, P_SpecB, r.SpecB);
                _cs.Dispatch(_kUpdate, N / 8, N / 8, 1);
                var aFin = FFTField(r.SpecA, r.A0, r.A1);
                var bFin = FFTField(r.SpecB, r.B0, r.B1);
                _cs.SetTexture(_kPack, P_Src, aFin);
                _cs.SetTexture(_kPack, P_SrcB, bFin);
                _cs.SetTexture(_kPack, P_Disp, r.Disp);
                _cs.Dispatch(_kPack, N / 8, N / 8, 1);

                // 读回中线行 h
                var prev = RenderTexture.active;
                var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false);
                RenderTexture.active = r.Disp;
                tex.ReadPixels(new Rect(0, 0, N, N), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                int y = N / 2;
                float best = -1e9f; int bi = 0; float mx = 0f, mn = 0f;
                var row = new float[N];
                for (int x = 0; x < N; x++)
                {
                    // Pack 已做过棋盘频移校正，读回直接用（再做一次=双重校正，
                    // 实测把 λ=31m 行波读成奈奎斯特交替斑马纹）
                    float h = tex.GetPixel(x, y).b;
                    row[x] = h;
                    if (h > best) { best = h; bi = x; }
                    mx = Mathf.Max(mx, Mathf.Abs(h));
                    mn = Mathf.Min(mn, h);
                }
                Object.Destroy(tex);
                _dbgRow = row; _dbgBi = bi; _dbgMax = mx; _dbgMin = mn;
                amp = best;
                return bi * L / N;
            }

            float amp1, amp2;
            float x1 = ReadCrest(0f, out amp1);          // t=0：谱全实，期望幅值无相位歧义
            float x2 = ReadCrest(1.75f, out amp2);
            int bi1 = _dbgBi;
            float x2b = ReadCrest(1.75f, out _);          // 复读一次看确定性
            int bi2 = _dbgBi;
            // 相速度可能反向（符号约定 x 减小）——按环回最短差取
            float d = x2 - x1;
            if (d > L * 0.5f) d -= L;
            if (d < -L * 0.5f) d += L;
            float vMeas = Mathf.Abs(d) / 1.75f;
            float vTheory = omega / k0;
            // t=0 峰值 = 2a/N²（行波单边频率构造）
            float expectAmp = 2f * a / (N * N);

            // 恢复正常谱，重跑一帧
            Init();

            var sb = new System.Text.StringBuilder();
            sb.Append($"FFTSelfTest a={a} k0={k0:F4} λ={2f * Mathf.PI / k0:F1}m bi1={bi1} bi2={bi2}(复测{_dbgBi}) | " +
                      $"相速度 实测 {vMeas:F3} m/s vs 理论 √(g/k)={vTheory:F3} m/s " +
                      $"误差 {Mathf.Abs(vMeas - vTheory) / vTheory * 100f:F1}% | " +
                      $"幅值 实测 {amp1:F5} vs 解析 {expectAmp:F5} " +
                      $"误差 {Mathf.Abs(Mathf.Abs(amp1) - expectAmp) / expectAmp * 100f:F1}% | ");
            if (_dbgRow != null)
            {
                sb.Append($"row[bi±3..0..3]=");
                for (int q = 3; q >= 1; q--) sb.Append(_dbgRow[(_dbgBi - q + N) % N].ToString("F4") + " ");
                sb.Append("[" + _dbgRow[_dbgBi].ToString("F4") + "] ");
                for (int q = 1; q <= 3; q++) sb.Append(_dbgRow[(_dbgBi + q) % N].ToString("F4") + " ");
                sb.Append($"min={_dbgMin:F4} maxAbs={_dbgMax:F4} | ");
            }
            sb.Append(Mathf.Abs(vMeas - vTheory) / vTheory < 0.1f &&
                      Mathf.Abs(Mathf.Abs(amp1) - expectAmp) / expectAmp < 0.15f ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>各级场统计（定标 ampScale 用）：最大/均方波高。</summary>
        public string ReadStats()
        {
            if (!_inited) return "FFTSim not inited";
            var sb = new System.Text.StringBuilder();
            var prev = RenderTexture.active;
            for (int i = 0; i < Levels.Length; i++)
            {
                var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false);
                RenderTexture.active = _rts[i].Disp;
                tex.ReadPixels(new Rect(0, 0, N, N), 0, 0);
                tex.Apply();
                float mx = 0f, sum2 = 0f;
                var cols = tex.GetPixels();
                foreach (var c in cols)
                {
                    mx = Mathf.Max(mx, Mathf.Abs(c.b));
                    sum2 += c.b * c.b;
                }
                Object.Destroy(tex);
                sb.Append($"L{Levels[i].L:F0}m max|h|={mx:F3} rms={Mathf.Sqrt(sum2 / cols.Length):F3}  ");
            }
            RenderTexture.active = prev;
            return sb.ToString();
        }

        /// <summary>R32：_Grad 场统计（Jacobian 分布 + 泡沫通道）——白沫可见性定标用。
        /// 返回每级 J 的 min/p05/p50 与 foam 的 p50/p95/max，探针直读。</summary>
        public string ReadGradStats()
        {
            if (!_inited) return "FFTSim not inited";
            var sb = new System.Text.StringBuilder();
            var prev = RenderTexture.active;
            for (int i = 0; i < Levels.Length; i++)
            {
                var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false);
                RenderTexture.active = _rts[i].Grad;
                tex.ReadPixels(new Rect(0, 0, N, N), 0, 0);
                tex.Apply();
                var cols = tex.GetPixels();
                int n = cols.Length;
                var js = new float[n]; var fs = new float[n];
                for (int q = 0; q < n; q++) { js[q] = cols[q].b; fs[q] = cols[q].a; }
                System.Array.Sort(js); System.Array.Sort(fs);
                Object.Destroy(tex);
                sb.Append($"L{Levels[i].L:F0} Jmin={js[0]:F3} J05={js[n / 20]:F3} J50={js[n / 2]:F3} " +
                          $"foam50={fs[n / 2]:F3} foam95={fs[(int)(n * 0.95f)]:F3} foamMax={fs[n - 1]:F3}  ");
            }
            RenderTexture.active = prev;
            return sb.ToString();
        }
    }
}
