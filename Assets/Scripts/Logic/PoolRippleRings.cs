// PoolRippleRings.cs — 入水涟漪环（以入水点为中心的可见同心扩散环）
//
// 为什么补这一层：波动方程高度场（PoolRipples）是"物理正确"的传播/反射涟漪，
// 但游戏相机高 3.6m，顶点位移+法线扰动读起来偏淡——用户要的是"跳下去以后以人
// 为中心散开涟漪"的直感。这里发一组**保证可见**的泡沫环：从入水点扩散、变薄、
// 淡出，与高度场叠加（高度场继续负责传播/贴壁反射的余波）。
//
// 全程序化（项目纪律：表现参数只看这一处，无贴图无预制体）：
//   · 环 = 软边环带网格（内缘→峰→峰→外缘 四圈顶点，靠顶点色 alpha 柔边）；
//   · 半径 easeOut 扩散、环带随扩散变薄、alpha 平方淡出；
//   · 环池复用（同屏最多 3 环×2 次跳水 = 个位数对象，不逐次 new/Destroy）。
// 用法：PoolRippleRings.Instance?.Rings(入水点, 强度)——strength 0.3=球，1.0=跳水。
using System.Collections.Generic;
using UnityEngine;

namespace WaterProject.Logic
{
    public class PoolRippleRings : MonoBehaviour
    {
        public static PoolRippleRings Instance { get; private set; }

        [Header("环形态")]
        [Tooltip("整环段数（软边环带 = 每段 4 圈顶点）")]
        public int segments = 64;
        [Tooltip("单环从出生到消失的扩散时长（秒）")]
        public float life = 1.45f;
        [Tooltip("相邻环的错拍（秒）：第 2/3 环晚这一点再出生")]
        public float stagger = 0.26f;
        [Tooltip("满强度时最外环的最大半径（米）")]
        public float maxRadius = 2.3f;
        [Tooltip("环带世界宽度（米，随扩散收到约 45%）")]
        public float bandWidth = 0.44f;
        [Tooltip("泡沫色（alpha 是满强度的峰值不透明度）")]
        public Color foamColor = new Color(0.92f, 0.98f, 1.0f, 0.85f);

        Material _mat;
        readonly List<Ring> _live = new List<Ring>(8);
        readonly Stack<Ring> _free = new Stack<Ring>(8);
        // 重建顶点的共享缓冲（所有环轮流用，零每帧 GC）
        readonly List<Vector3> _verts = new List<Vector3>(512);
        readonly List<Color32> _cols = new List<Color32>(512);

        class Ring
        {
            public GameObject go;
            public Mesh mesh;
            public float t;      // <0 = 还没到出生拍（stagger 期间）
            public float str;
            public bool logged;  // 取证：每个环只打一条渲染自检
        }

        void Awake()
        {
            Instance = this;
            _mat = MakeMat();
        }

        // ── 取证（探针/菜单读；不给逻辑用）──
        /// <summary>当前活着的环数（0 = Update 没在推进或 Rings 没发）。</summary>
        public int LiveCount => _live.Count;
        /// <summary>组件是否在参与更新（enabled 且挂在激活层级上）。</summary>
        public bool TickAlive => enabled && gameObject.activeInHierarchy;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_mat) Destroy(_mat);
        }

        Material MakeMat()
        {
            // URP 粒子无光照 shader：顶点色 × 白 _BaseColor，透明队列不写深度
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);                    // Transparent
            m.SetFloat("_Blend", 0f);                      // Alpha 混合
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            // ⚠ 队列必须晚于水面（Water_Pool = Transparent/3000）：透明物体按
            //   "bounds 中心距相机"由远及近画 —— 水面网格盖满全池，bounds 中心
            //   几乎总比环更近 ⇒ 水面排在环**之后**绘制，掠射角菲涅尔 alpha≈1
            //   把整环覆盖（实测：环自检 vis=False、画面无环）。画在 3100 让环
            //   合成在最终水色之上；深度测试仍在，池壁/压沿台等不透明体照常遮挡。
            m.renderQueue = 3100;
            return m;
        }

        /// <summary>入水涟漪：以入水点为中心发一圈同心扩散环。strength 0.3=球入水，1.0=跳水。</summary>
        public void Rings(Vector3 waterPos, float strength)
        {
            int n = strength >= 0.75f ? 3 : 2;             // 跳水 3 环 / 球 2 环
            Debug.Log($"[PoolRippleRings] Rings n={n} str={strength:F2} pos={waterPos} tickAlive={TickAlive}");
            for (int i = 0; i < n; i++)
            {
                var r = Get();
                r.t = -i * stagger;
                r.str = strength * (1f - i * 0.22f);       // 外圈依次弱一点
                // 逐环抬高一点点：环与环、环与波面都不互相 z-fight
                r.go.transform.position = new Vector3(waterPos.x,
                                                      waterPos.y + 0.045f + i * 0.006f,
                                                      waterPos.z);
                _live.Add(r);
            }
        }

        Ring Get()
        {
            while (_free.Count > 0)
            {
                var r = _free.Pop();
                if (r.go != null) return r;                // 池里可能是已销毁的引用
            }
            return MakeRing();
        }

        Ring MakeRing()
        {
            var go = new GameObject("RippleRing");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            var mesh = new Mesh { name = "RippleRing" };
            mesh.MarkDynamic();                            // 每帧改顶点：标记动态，避免内部缓冲反复重建
            mf.sharedMesh = mesh;
            BuildIndices(mesh);
            var r = new Ring { go = go, mesh = mesh };
            return r;
        }

        /// <summary>索引拓扑恒定（segments×4 顶点、三段条带），建一次终身用。
        /// ⚠ 绕序必须从上往下看是**顺时针**（Unity 左手系正面）：顶点角向 a 递增时
        /// (A1−A0)×(B0−A0) 朝 −Y，原绕序整环背面被剔除 —— 实测"环在场上却看不见"就是它。</summary>
        void BuildIndices(Mesh mesh)
        {
            var idx = new int[segments * 3 * 6];
            int w = 0;
            for (int s = 0; s < segments; s++)
            {
                int t = (s + 1) % segments;
                int A0 = s * 4, A1 = t * 4;    // 内缘
                int B0 = s * 4 + 1, B1 = t * 4 + 1;
                int C0 = s * 4 + 2, C1 = t * 4 + 2;
                int D0 = s * 4 + 3, D1 = t * 4 + 3; // 外缘
                idx[w++] = A0; idx[w++] = B0; idx[w++] = A1;
                idx[w++] = B0; idx[w++] = B1; idx[w++] = A1;
                idx[w++] = B0; idx[w++] = C0; idx[w++] = B1;
                idx[w++] = C0; idx[w++] = C1; idx[w++] = B1;
                idx[w++] = C0; idx[w++] = D0; idx[w++] = C1;
                idx[w++] = D0; idx[w++] = D1; idx[w++] = C1;
            }
            mesh.vertices = new Vector3[segments * 4];   // 先给顶点再给索引（Unity 校验顺序）
            mesh.triangles = idx;
            var normals = new Vector3[segments * 4];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var r = _live[i];
                r.t += dt;
                if (r.t < 0f)
                {
                    if (r.go.activeSelf) r.go.SetActive(false);   // stagger 等待期藏住
                    continue;
                }
                if (!r.go.activeSelf) r.go.SetActive(true);
                float k = r.t / life;
                if (k >= 1f)
                {
                    r.go.SetActive(false);
                    _live.RemoveAt(i);
                    _free.Push(r);
                    continue;
                }
                // 半径先快后慢（easeOutQuad）；环带收到 ~45%；alpha 指数 1.6 淡出
                //（平方淡出实测偏快：环龄过半就几乎读不到，白 foam 存在感不足）
                float ease = 1f - (1f - k) * (1f - k);
                float radius = Mathf.Lerp(0.10f, maxRadius * Mathf.Clamp(r.str, 0.35f, 1.4f), ease);
                float band = bandWidth * Mathf.Lerp(1f, 0.45f, k) * Mathf.Clamp(r.str, 0.4f, 1.2f);
                float alpha = foamColor.a * Mathf.Pow(1f - k, 1.6f);
                Rebuild(r.mesh, radius, Mathf.Max(band, 0.02f), alpha);
                if (!r.logged)   // 取证：首个重建拍打一次渲染自检（网格/剔除/材质队列/层）
                {
                    r.logged = true;
                    var mr2 = r.go.GetComponent<MeshRenderer>();
                    string sh = mr2.sharedMaterial != null && mr2.sharedMaterial.shader != null
                        ? mr2.sharedMaterial.shader.name : "null";
                    int q = mr2.sharedMaterial != null ? mr2.sharedMaterial.renderQueue : -1;
                    Debug.Log("[PoolRippleRings] ring r=" + radius.ToString("F2")
                        + " alpha=" + alpha.ToString("F2")
                        + " verts=" + r.mesh.vertexCount
                        + " bounds=" + r.mesh.bounds.size
                        + " vis=" + mr2.isVisible
                        + " shader=" + sh
                        + " queue=" + q
                        + " layer=" + LayerMask.LayerToName(r.go.layer));
                }
            }
        }

        /// <summary>按半径/带宽/alpha 重写环带顶点（位置+颜色；索引/法线不动）。</summary>
        void Rebuild(Mesh mesh, float radius, float band, float alpha)
        {
            _verts.Clear(); _cols.Clear();
            float rIn = Mathf.Max(radius - band, 0.02f);
            float r0 = Mathf.Max(rIn - band * 0.65f, 0.01f);   // 内缘（alpha 0）
            float r1 = rIn + (radius - rIn) * 0.35f;           // 内峰
            float r2 = radius + band * 0.35f;                  // 外峰
            float r3 = radius + band;                          // 外缘（alpha 0）
            byte peak = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
            var c = foamColor;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                _verts.Add(new Vector3(sin * r0, 0f, cos * r0));
                _verts.Add(new Vector3(sin * r1, 0f, cos * r1));
                _verts.Add(new Vector3(sin * r2, 0f, cos * r2));
                _verts.Add(new Vector3(sin * r3, 0f, cos * r3));
                _cols.Add(new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 0));
                _cols.Add(new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), peak));
                _cols.Add(new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), peak));
                _cols.Add(new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 0));
            }
            mesh.SetVertices(_verts);
            mesh.SetColors(_cols);
            // ⚠ SetVertices 不刷新 bounds——初建时顶点全在原点，bounds 退化成零尺寸，
            // 视锥剔除会把整个环裁掉（实测"环在场上却什么都看不到"就是它）。
            mesh.RecalculateBounds();
        }
    }
}
