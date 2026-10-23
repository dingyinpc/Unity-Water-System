// CinematicLetterbox.cs — 电影遮幅（上下黑边，2.35:1）
//
// 为什么不用 ScreenSpaceOverlay 的两条 UI 黑边：
//   验收截图走 GameViewCapture，读的是**相机的 render texture**，Overlay UI 压根不在里面
//   —— 实测"开了遮幅"与"没开"两张图逐位相同（0.00% 差异）。UI 只在人眼看 Game 视图时存在。
//
// 现在用**双相机**方案，画面里真实存在、截图也拍得到：
//   ① 黑底相机：depth = 主相机 - 1，cullingMask = 0，SolidColor 黑，整屏；
//   ② 主相机：viewport rect 收到 2.35:1（上下留出黑边）。
//
// ⚠ 主相机的 rect 会改变有效画幅，竖 FOV 不变 ⇒ 横向视野变窄（与"长焦"同向，符合预期）。
using UnityEngine;

[ExecuteAlways]
public class CinematicLetterbox : MonoBehaviour
{
    [Tooltip("目标画幅比（2.35 = 宽银幕；小于屏幕比时不遮）")]
    public float targetAspect = 2.35f;
    public Color barColor = Color.black;

    Camera _bg;
    Camera _cam;
    Rect _origRect = new Rect(0, 0, 1, 1);

    void OnEnable() { Apply(); }
    void Update() { Apply(); }
    void OnDisable() { Restore(); }

    void Apply()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
            _origRect = _cam.rect;
        }

        float a = (float)Screen.width / Mathf.Max(1, Screen.height);
        float f = a < targetAspect ? 0.5f * (1f - a / targetAspect) : 0f;

        if (f <= 0.0005f) { Restore(); return; }

        if (_bg == null)
        {
            var go = new GameObject("LetterboxBackdrop");
            go.transform.SetParent(transform, false);
            _bg = go.AddComponent<Camera>();
            _bg.cullingMask = 0;
            _bg.clearFlags = CameraClearFlags.SolidColor;
            _bg.backgroundColor = barColor;
            _bg.depth = _cam.depth - 1f;
            _bg.allowHDR = false;
            _bg.allowMSAA = false;
            _bg.rect = new Rect(0, 0, 1, 1);
        }

        _cam.rect = new Rect(0f, f, 1f, 1f - 2f * f);
    }

    void Restore()
    {
        if (_cam != null) _cam.rect = _origRect;
        if (_bg != null)
        {
            if (Application.isPlaying) Destroy(_bg.gameObject);
            else DestroyImmediate(_bg.gameObject);
            _bg = null;
        }
    }
}
