using UnityEngine;

namespace Core.Utils
{
    /// <summary>Shared cinematic easing — no raw linear Lerp where the player looks.</summary>
    public static class MotionEase
    {
        public static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static float SmoothOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }

        public static float SmoothInOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f
                ? 2f * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
        }

        public static Vector3 Damp(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime)
        {
            return Vector3.SmoothDamp(current, target, ref velocity, Mathf.Max(0.01f, smoothTime));
        }

        public static Quaternion Damp(Quaternion current, Quaternion target, ref float velocity, float smoothTime)
        {
            var angle = Quaternion.Angle(current, target);
            if (angle < 0.01f)
                return target;
            var t = Mathf.SmoothDampAngle(0f, angle, ref velocity, Mathf.Max(0.01f, smoothTime));
            return Quaternion.RotateTowards(current, target, t);
        }
    }
}
