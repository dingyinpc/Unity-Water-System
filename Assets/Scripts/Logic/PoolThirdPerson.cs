// PoolThirdPerson.cs — 泳池第三人称：WASD 涉水 + 脚步涟漪 + 跟随相机
//
// 人物踩在池底（floorY），1.35m 水深淹到大腿根——"涉水"而不是"游泳"，
// 模型脚底即根节点（KayKit 骨骼习惯）。涟漪联动：
//   · 移动时每 0.24s 在脚边注一颗随速度变强的水滴 → 身后拖出涟漪尾迹；
//   · 静止时偶发小涟漪（人物在水中轻微活动）。
// 相机：固定偏移跟随（偏移向 = 相机初始朝向），WASD 即屏幕方向，无需轨道旋转。
// autoWalk：自动绕圈（无头取帧验证用——桥没法替你按 WASD）。
using UnityEngine;

namespace WaterProject.Logic
{
    public class PoolThirdPerson : MonoBehaviour
    {
        [Header("移动（涉水）")]
        public float walkSpeed = 1.4f;
        public float runSpeed = 2.6f;
        public float turnLerp = 10f;
        public Vector2 poolHalf = new Vector2(5.3f, 2.3f);  // 活动范围半宽（离内壁 0.7m）
        public float floorY = -1.35f;                       // 池底

        [Header("涟漪联动")]
        public float moveRippleInterval = 0.24f;
        public float idleRippleInterval = 1.9f;

        [Header("相机跟随")]
        public Vector3 camOffset = new Vector3(0f, 3.6f, -6.0f);
        public float lookAhead = 1.2f;

        [Header("验证")]
        public bool autoWalk = false;      // 自动绕圈（桥取帧用，绕椭圆一圈）
        public float autoWalkRadiusX = 3.2f;
        public float autoWalkRadiusZ = 1.8f;

        Animator _anim;
        Transform _camT;
        Vector3 _camPos;
        float _nextMoveRipple, _nextIdleRipple, _autoAngle;

        static readonly int P_Speed = Animator.StringToHash("Speed");

        void Start()
        {
            _anim = GetComponentInChildren<Animator>();
            var cam = Camera.main;
            if (cam != null) { _camT = cam.transform; _camPos = _camT.position; }
        }

        void Update()
        {
            float speed = 0f;

            if (autoWalk)
            {
                // 椭圆巡游：往下一个路径点走，走到就切下一个
                _autoAngle += Time.deltaTime * walkSpeed /
                              Mathf.Max(0.5f, Mathf.PI * 2f * 0.5f * (autoWalkRadiusX + autoWalkRadiusZ) / Mathf.PI);
                Vector3 target = new Vector3(Mathf.Sin(_autoAngle) * autoWalkRadiusX, floorY,
                                             Mathf.Cos(_autoAngle) * autoWalkRadiusZ);
                Vector3 to = target - transform.position; to.y = 0f;
                if (to.magnitude > 0.08f)
                {
                    StepToward(to.normalized, walkSpeed);
                    speed = walkSpeed;
                }
            }
            else
            {
                var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                var dir = new Vector3(input.x, 0f, input.y);
                if (dir.sqrMagnitude > 1e-6f)
                {
                    bool run = Input.GetKey(KeyCode.LeftShift);
                    speed = Mathf.Min(dir.magnitude, 1f) * (run ? runSpeed : walkSpeed);
                    StepToward(dir.normalized, speed);
                }
            }

            if (_anim != null) _anim.SetFloat(P_Speed, speed);

            // ── 涟漪联动 ──
            if (PoolRipples.Instance != null)
            {
                if (speed > 0.15f)
                {
                    if (Time.time >= _nextMoveRipple)
                    {
                        _nextMoveRipple = Time.time + moveRippleInterval;
                        Vector3 rp = transform.position;
                        rp.x += Random.Range(-0.22f, 0.22f);
                        rp.z += Random.Range(-0.22f, 0.22f);
                        rp.y = 0f;
                        PoolRipples.Instance.SpawnDrop(rp, 0.35f + speed * 0.22f);
                    }
                }
                else if (Time.time >= _nextIdleRipple)
                {
                    _nextIdleRipple = Time.time + idleRippleInterval;
                    PoolRipples.Instance.SpawnDrop(transform.position, 0.16f);
                }
            }

            // ── 相机跟随 ──
            if (_camT != null)
            {
                Vector3 want = transform.position + camOffset;
                _camPos = Vector3.Lerp(_camPos, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
                _camT.position = _camPos;
                Vector3 lookAt = transform.position + Vector3.up * 0.9f + Vector3.forward * (lookAhead * 0.5f);
                Quaternion wantRot = Quaternion.LookRotation(lookAt - _camT.position, Vector3.up);
                _camT.rotation = Quaternion.Slerp(_camT.rotation, wantRot, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
        }

        void StepToward(Vector3 dir, float speed)
        {
            Vector3 p = transform.position + dir * (speed * Time.deltaTime);
            p.x = Mathf.Clamp(p.x, -poolHalf.x, poolHalf.x);
            p.z = Mathf.Clamp(p.z, -poolHalf.y, poolHalf.y);
            p.y = floorY;
            transform.position = p;
            Quaternion look = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z), Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look,
                                                  1f - Mathf.Exp(-turnLerp * Time.deltaTime));
        }
    }
}
