// IsletFirstPerson.cs — 表现层：潟湖岛第一人称漫游（WASD 移动 + 鼠标视角 + 地形贴合）
//
// 用户需求（2026-10-04）："以第一人称视角，按住 WASD 去移动，鼠标滑动去移动摄像机
// 位置来实现第一人称观察风景的效果。"
//
// 用法：挂在 IsletScene 的 Main Camera 上（菜单 90 会自愈式补挂）。
//   · WASD        相机水平朝向移动（忽略俯仰，不会"往天上飞"）
//   · Shift       冲刺 ×2.5
//   · 鼠标滑动    转视角——**先在 Game 视图里点一下左键**锁定光标（编辑器里
//                 不锁定的话，鼠标一划到别的窗口视角会跟着乱转）；ESC 释放。
//   · 高度        始终贴地形：眼高 1.72m（与机位标定同值），可以走进潟湖"涉水"，
//                 水面 y=0、Terrain 高度场是唯一真值（SampleHeight）。
//
// 已知取舍：不做重力/跳跃（风景观察不需要）；不做碰撞（椰树/岩石可穿——
// 要挡人得给 80 棵树配碰撞体，观赏场景不值得）。
using UnityEngine;

namespace WaterProject.Presentation
{
    [RequireComponent(typeof(Camera))]
    public class IsletFirstPerson : MonoBehaviour
    {
        [Header("移动")]
        public float moveSpeed = 6f;          // m/s，步行
        public float sprintMultiplier = 2.5f; // Shift 冲刺
        [Header("视角")]
        public float lookSpeed = 2.2f;        // 鼠标灵敏度（度/像素）
        public float pitchClamp = 85f;        // 俯仰上限，防止翻转
        [Header("贴地")]
        public float eyeHeight = 1.72f;       // 眼高（与机位标定同一值）

        float _yaw, _pitch;
        Terrain _terrain;

        void Start()
        {
            var e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;   // 归一到 [-180,180]，俯仰单独存
        }

        void Update()
        {
            // ── 视角：仅当光标被锁定（用户在 Game 视图点过左键）才吃鼠标增量 ──
            if (Input.GetMouseButtonDown(0))
                Cursor.lockState = CursorLockMode.Locked;
            if (Input.GetKeyDown(KeyCode.Escape))
                Cursor.lockState = CursorLockMode.None;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                _yaw += Input.GetAxis("Mouse X") * lookSpeed;
                _pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
                _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            // ── 移动：WASD 相对相机水平朝向；俯仰不参与（按住 W 不会往天上游）──
            var fwd = transform.rotation * new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            fwd.y = 0f;
            bool moving = fwd.sqrMagnitude > 1e-6f;
            if (!moving) return;   // ⚠ 不动就完全不写 transform —— 否则第一次贴地
                                   //   会把电影机位 (0,1.72,-34) 拽到湖底
                                   //   （那里地形 -1.87m，terrain+1.72=-0.15），
                                   //   验收图当场变"半张水下"。实测踩过。

            if (fwd.sqrMagnitude > 1f) fwd.Normalize();
            float sp = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);
            Vector3 pos = transform.position + fwd * (sp * Time.deltaTime);

            // ── 贴地：只在移动时贴合，且贴合面取 max(地形, 水面 0) ——
            //    在岛上沿地形走，走进潟湖在水面滑行（不做潜水视角）。
            //    Terrain 高度场是唯一真值，不允许手动 y。
            if (_terrain == null) _terrain = Object.FindFirstObjectByType<Terrain>();
            if (_terrain != null)
            {
                var sz = _terrain.terrainData.size;
                var o = _terrain.transform.position;
                // 别走出地形边界（出界 SampleHeight 会钳到边缘，走出去就是瞬移）
                pos.x = Mathf.Clamp(pos.x, o.x + 2f, o.x + sz.x - 2f);
                pos.z = Mathf.Clamp(pos.z, o.z + 2f, o.z + sz.z - 2f);
                float h = Mathf.Max(_terrain.SampleHeight(pos) + o.y, 0f);
                pos.y = h + eyeHeight;
            }
            transform.position = pos;
        }
    }
}
