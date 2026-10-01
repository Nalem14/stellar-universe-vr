using Core.Vfx;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.Holo
{
    /// <summary>
    /// One controller's aim on the holo table: its ray direction steadied by a One-Euro filter (still hand =
    /// jitter removed, quick flick = no lag), the target it is snapped to and the point of the map plane under
    /// it. Table aim only — the XR ray line keeps the raw pose.
    /// </summary>
    public sealed class HoloAimRay
    {
        /// <summary>Cutoff at rest (Hz): lower = steadier, laggier.</summary>
        const float MinCutoff = 2.2f;
        /// <summary>Cutoff gain per degree / second of turn: fast moves pass through unfiltered.</summary>
        const float Beta = 0.05f;
        const float SpeedCutoff = 1.5f;
        /// <summary>A jump this large in one frame (snap turn, tracking loss) resets the filter (degrees).</summary>
        const float ResetDegrees = 25f;

        public NearFarInteractor Ray;
        public Vector3 Origin;
        public Vector3 Dir = Vector3.forward;

        /// <summary>Snapped target (null = none) and its identity (kind + id: pooled star tokens get recycled).</summary>
        public HoloToken Target;
        public int TargetKey;
        /// <summary>Target score (angle / (radius + cone), 0 = dead centre, &lt; 1 = inside the assist).</summary>
        public float Score = float.MaxValue;
        public float LastSeen;
        /// <summary>The target before the last change, how long it was held, and when it changed (trigger dip).</summary>
        public HoloToken Previous;
        public int PreviousKey;
        public float PreviousHeld;
        public float ChangedAt;
        /// <summary>Trigger travel now (0–1), and when the target last changed: the dip only happens mid-pull.</summary>
        public float Trigger;
        float _triggerAtChange;
        float _heldSince;

        public bool OnUi;
        public bool HasPoint;
        public Vector3 Point;
        public Vector3 PointLocal;

        Vector3 _raw;
        float _speed;
        float _last = -1f;

        /// <summary>Follow <paramref name="ray"/> (null = free slot): filter and target start fresh.</summary>
        public void Bind(NearFarInteractor ray, float now)
        {
            Ray = ray;
            _last = -1f;
            SetTarget(null, 0f, now);
            Previous = null;
            PreviousKey = 0;
            HasPoint = false;
            OnUi = false;
        }

        public static int KeyOf(HoloToken t) => t == null ? 0 : (((int)t.Kind + 1) << 24) ^ t.Id;

        public void Filter(Vector3 origin, Vector3 dir, float now)
        {
            Origin = origin;
            var dt = now - _last;
            _last = now;
            if (dt <= 0f || dt > 0.25f || dir.sqrMagnitude < 1e-6f)
            {
                Reset(dir);
                return;
            }

            var deg = Vector3.Angle(_raw, dir);
            _raw = dir;
            if (deg > ResetDegrees)
            {
                Reset(dir);
                return;
            }

            _speed = Mathf.Lerp(_speed, deg / dt, Alpha(dt, SpeedCutoff));
            var a = Alpha(dt, MinCutoff + Beta * _speed);
            Dir = Vector3.Slerp(Dir, dir, a).normalized;
        }

        void Reset(Vector3 dir)
        {
            _raw = dir;
            Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            _speed = 0f;
        }

        static float Alpha(float dt, float cutoff)
        {
            var tau = 1f / (2f * Mathf.PI * Mathf.Max(0.01f, cutoff));
            return 1f / (1f + tau / dt);
        }

        /// <summary>Snap onto <paramref name="t"/> (or clear), remembering the previous target for a click.</summary>
        public void SetTarget(HoloToken t, float score, float now)
        {
            var key = KeyOf(t);
            if (t != null)
                LastSeen = now;
            Score = t != null ? score : float.MaxValue;
            if (key == TargetKey)
            {
                // Same body (the table redraws its tokens on polls): follow the new token, not a new snap.
                Target = t;
                return;
            }

            Previous = Target;
            PreviousKey = TargetKey;
            PreviousHeld = now - _heldSince;
            ChangedAt = now;
            _triggerAtChange = Trigger;
            _heldSince = now;
            Target = t;
            TargetKey = key;
        }

        /// <summary>
        /// The target a trigger press means: pulling a trigger dips the controller, so a target held steadily
        /// that the aim left mid-pull, a few frames before the click, still wins.
        /// </summary>
        public HoloToken ClickTarget(float now, float memory, float steady)
        {
            if (Previous != null && Previous.isActiveAndEnabled && KeyOf(Previous) == PreviousKey &&
                now - ChangedAt < memory && PreviousHeld >= steady && _triggerAtChange > 0.08f)
                return Previous;
            return Target != null && KeyOf(Target) == TargetKey ? Target : null;
        }
    }
}
