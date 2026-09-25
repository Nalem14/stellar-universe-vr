using Core.Utils;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Deploy animation of a holo table token: it waits <see cref="Delay"/>, then grows out of the plate with
    /// a slight overshoot and removes itself. Used once per token per system (HoloZoneMap.Tag), so the map
    /// unfolds from the star outward instead of popping in.
    /// </summary>
    public sealed class HoloDeploy : MonoBehaviour
    {
        public float Delay;
        const float Seconds = 0.45f;
        Vector3 _scale;
        float _t = -1f;

        void Start()
        {
            _scale = transform.localScale;
            transform.localScale = _scale * 0.001f;
        }

        void Update()
        {
            if (_t < 0f)
            {
                Delay -= Time.unscaledDeltaTime;
                if (Delay > 0f)
                    return;
                _t = 0f;
            }

            _t += Time.unscaledDeltaTime / Seconds;
            var u = Mathf.Clamp01(_t);
            // Ease out with a touch of overshoot (1.08 at ~70 %), settling on the rest scale.
            var k = MotionEase.SmoothOut(u) + Mathf.Sin(u * Mathf.PI) * 0.08f;
            transform.localScale = _scale * Mathf.Max(0.001f, k);
            if (u >= 1f)
            {
                transform.localScale = _scale;
                Destroy(this);
            }
        }
    }
}
