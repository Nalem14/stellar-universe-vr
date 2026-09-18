using System.Threading.Tasks;
using Core.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>Full-view fade for teleporter / system swap (world-space quad in front of XR camera).</summary>
    public class ViewFade : MonoBehaviour
    {
        static ViewFade _instance;
        Canvas _canvas;
        Image _image;
        bool _busy;

        public static ViewFade Ensure()
        {
            if (_instance != null)
                return _instance;
            var go = new GameObject("ViewFade");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ViewFade>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.AddComponent<GraphicRaycaster>().enabled = false;

            var imgGo = new GameObject("Fade");
            imgGo.transform.SetParent(transform, false);
            _image = imgGo.AddComponent<Image>();
            _image.color = new Color(0f, 0.02f, 0.05f, 0f);
            var rt = _image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _image.raycastTarget = false;
        }

        public async Task FadeOut(float duration = 0.35f)
        {
            if (_busy)
                return;
            _busy = true;
            await Animate(0f, 1f, duration);
            _busy = false;
        }

        public async Task FadeIn(float duration = 0.4f)
        {
            if (_busy)
                return;
            _busy = true;
            await Animate(1f, 0f, duration);
            _busy = false;
        }

        async Task Animate(float from, float to, float duration)
        {
            if (_image == null)
                return;
            var t = 0f;
            var c = _image.color;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var u = MotionEase.SmoothInOut(t / duration);
                c.a = Mathf.Lerp(from, to, u);
                _image.color = c;
                await Task.Yield();
            }

            c.a = to;
            _image.color = c;
        }
    }
}
