// WaterDemoUI.cs — 表现层：演示 UI（FPS/交互计数/操作提示）
// 职责：只做"显示"与"输入转发"，业务全部调用逻辑层接口。
using UnityEngine;
using UnityEngine.UI;
using WaterProject.Logic;

namespace WaterProject.Presentation
{
    public class WaterDemoUI : MonoBehaviour
    {
        Text _info;
        WaterInteractionManager _mgr;
        float _fpsAccum;
        int _fpsFrames;
        float _fpsTick;

        void Start()
        {
            _mgr = FindFirstObjectByType<WaterInteractionManager>();
            BuildCanvas();
        }

        void BuildCanvas()
        {
            var canvasGo = new GameObject("WaterDemoUI_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var textGo = new GameObject("InfoText");
            textGo.transform.SetParent(canvasGo.transform, false);
            _info = textGo.AddComponent<Text>();
            _info.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _info.fontSize = 22;
            _info.color = new Color(1, 1, 1, 0.85f);
            _info.alignment = TextAnchor.UpperLeft;
            var rt = _info.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(14, -140);
            rt.offsetMax = new Vector2(-14, -14);
        }

        void Update()
        {
            // 表现层：输入 → 调用逻辑层
            if (_mgr == null)
            {
                _mgr = FindFirstObjectByType<WaterInteractionManager>();
                if (_mgr == null) return;
            }

            if (Input.GetMouseButtonDown(0)) SpawnAtMouse();
            if (Input.GetKeyDown(KeyCode.R)) _mgr.SpawnRipple(RandomAroundCamera());
            if (Input.GetKeyDown(KeyCode.Space)) _mgr.SpawnInteraction(RandomAroundCamera(), 1f);

            // 表现层：状态文本
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (Time.unscaledTime - _fpsTick >= 0.5f)
            {
                float fps = _fpsFrames / Mathf.Max(Time.unscaledTime - _fpsTick, 1e-4f);
                _fpsTick = Time.unscaledTime;
                _fpsFrames = 0;
                if (_info != null)
                {
                    _info.text =
                        $"Unity 水体复刻演示 (URP 2022.3)\n" +
                        $"FPS = {fps:F1}\n" +
                        $"操作：左键点击水面=交互涟漪；R=程序化Ripple；空格=入水(交互+泡沫)\n" +
                        $"技术：自适应曲面细分 / 格斯特纳波 / 屏幕空间折射 / 折射深度保护 / 动态水体交互 / RNW法线混合 / 程序化Ripple / 浪尖白沫";
                }
            }
        }

        void SpawnAtMouse()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            // 与水面 y=0 求交
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return;
            Vector3 hit = ray.origin + ray.direction * t;
            _mgr.SpawnInteraction(hit, 1f);
            _mgr.SpawnRipple(hit);
        }

        Vector3 RandomAroundCamera()
        {
            // 环绕交互中心（与交互 RT 覆盖一致），默认水面原点
            Vector3 c = _mgr != null ? _mgr.interactionCenter : Vector3.zero;
            return new Vector3(c.x + Random.Range(-6f, 6f), 0, c.z + Random.Range(-6f, 6f));
        }
    }
}