// PoolBall.cs — 泳池沙滩球：无 Rigidbody 的自定义浮力物理 + 涟漪联动
//
// 浮力 = Kerner 排水体积思路的球版（《07》§1 E 档引用的 Kerner 模型只取
// 浸没体积那一步，船舶的切片采样在这里退化成一个球）：
//   浮力加速度 = g × 浸没比 / 密度比，密度比 0.18 → 平衡时浸没 18%，
//   沙滩球浮得高、入水"噗通"一下弹回——观感优先，不是流体仿真。
//
// 涟漪联动三件事：
//   · 入水瞬间：一颗大水滴（强度随入水速度）；
//   · 漂浮摇摆期间：|vel|>0.25 就每 0.12s 一颗小水滴 → 球周围持续的环纹；
//   · 视觉滚动：按水平速度绕垂直于速度的轴转（无物理引擎，纯表现）。
using UnityEngine;

namespace WaterProject.Logic
{
    public class PoolBall : MonoBehaviour
    {
        [Header("物理")]
        public float radius = 0.24f;
        [Range(0.05f, 0.5f)] public float densityRatio = 0.18f;  // 沙滩球≈充气塑料
        public float waterDrag = 3.5f;
        public float airDrag = 0.08f;
        public float wallRestitution = 0.45f;
        public float floorRestitution = 0.30f;
        public Vector2 poolHalf = new Vector2(5.3f, 2.3f);       // 与人物同范围
        public float floorY = -1.35f;

        [Header("涟漪")]
        public float wakeInterval = 0.12f;
        public float splashMin = 0.5f;

        public static PoolBall Instance { get; private set; }

        Vector3 _vel;
        float _prevY;
        WaterProject.Logic.PoolThirdPerson _char;
        bool _wasAbove;
        float _nextWake;

        void Awake()
        {
            Instance = this;
            _char = FindObjectOfType<WaterProject.Logic.PoolThirdPerson>();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>从世界点沿方向抛出（PoolThirdPerson 的 B 键 / menu98 测试）。</summary>
        public void Toss(Vector3 from, Vector3 dir, float speed)
        {
            transform.position = from;
            _vel = dir.normalized * speed;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);   // 卡顿帧别让物理穿底

            // ── 重力 ──
            _vel.y += -9.81f * dt;

            // ── 浮力 + 水阻（Kerner 浸没体积，球版）──
            float submerged = Mathf.Clamp01((0f - (transform.position.y - radius)) / (2f * radius));
            if (submerged > 0f)
            {
                _vel.y += 9.81f * (submerged / Mathf.Max(densityRatio, 0.05f)) * dt;
                float dragK = Mathf.Exp(-waterDrag * submerged * dt);
                _vel *= dragK;
            }
            else
            {
                _vel *= Mathf.Exp(-airDrag * dt);
            }

            Vector3 pos = transform.position + _vel * dt;

            // ── 入水检测（溅花）──
            bool above = pos.y - radius * 0.4f > 0f;
            if (_wasAbove && !above && PoolRipples.Instance != null)
            {
                float impact = Mathf.Max(0f, -_vel.y);
                PoolRipples.Instance.SpawnDrop(new Vector3(pos.x, 0f, pos.z),
                                               Mathf.Max(splashMin, impact * 0.35f));
                if (PoolSplashFX.Instance != null)
                    PoolSplashFX.Instance.Burst(new Vector3(pos.x, 0f, pos.z),
                                                Mathf.Clamp(impact * 0.30f, 0.3f, 1.1f));
                // 球入水的涟漪环（比跳水小一号）
                if (PoolRippleRings.Instance != null)
                    PoolRippleRings.Instance.Rings(new Vector3(pos.x, 0f, pos.z),
                                                   Mathf.Clamp(impact * 0.30f, 0.35f, 0.9f));
            }
            _wasAbove = above;

            // ── 边界：池壁弹回 ──
            if (pos.x > poolHalf.x - radius) { pos.x = poolHalf.x - radius; _vel.x = -_vel.x * wallRestitution; }
            if (pos.x < -poolHalf.x + radius) { pos.x = -poolHalf.x + radius; _vel.x = -_vel.x * wallRestitution; }
            if (pos.z > poolHalf.y - radius) { pos.z = poolHalf.y - radius; _vel.z = -_vel.z * wallRestitution; }
            if (pos.z < -poolHalf.y + radius) { pos.z = -poolHalf.y + radius; _vel.z = -_vel.z * wallRestitution; }
            // 池底
            if (pos.y < floorY + radius)
            {
                pos.y = floorY + radius;
                if (_vel.y < 0f) _vel.y = -_vel.y * floorRestitution;
            }

            transform.position = pos;

            // ── 人物推球：重叠沿分离方向推开，球吃人物动量，顶得狠还会蹦 ──
            if (_char != null)
            {
                Vector3 d = transform.position - _char.transform.position; d.y = 0f;
                float dist = d.magnitude;
                float minDist = radius + 0.42f;
                if (dist < minDist && dist > 1e-4f)
                {
                    Vector3 n = d / dist;
                    float push = minDist - dist;
                    transform.position += n * push;
                    float into = Vector3.Dot(_vel, n);
                    if (into < 0f) _vel -= n * into;
                    _vel += n * (_char.CurrentSpeed * 1.1f + 0.3f);
                    _vel.y += push * 2.0f;
                    if (PoolRipples.Instance != null && push > 0.02f)
                        PoolRipples.Instance.SpawnDrop(new Vector3(transform.position.x, 0f, transform.position.z), 0.45f);
                }
            }

            // ── 漂浮尾迹涟漪 ──
            if (PoolRipples.Instance != null && submerged > 0.02f && _vel.magnitude > 0.25f
                && Time.time >= _nextWake)
            {
                _nextWake = Time.time + wakeInterval;
                PoolRipples.Instance.SpawnDrop(new Vector3(pos.x, 0f, pos.z),
                                               0.15f + _vel.magnitude * 0.12f);
            }

            // ── 视觉滚动：水平速度绕垂直轴转 ──
            var h = new Vector3(_vel.x, 0f, _vel.z);
            if (h.sqrMagnitude > 0.01f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, h.normalized);
                transform.Rotate(axis, (-h.magnitude * dt / radius) * Mathf.Rad2Deg, Space.World);
            }
        }
    }
}
