using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Jump-outs and drop-ins of the other ships seen through the windows (the inhabited one has
    /// <see cref="VoyageFx"/>). One pooled glow burst, a handful of light-line trails and one audio source for
    /// the whole exterior: a flash coloured by the drive, a light-line shooting off (or in), a shock ring.
    /// Sub-light ships simply pull away into the dark / come out of it, no flash.
    /// </summary>
    public sealed class ExteriorTransitFx : MonoBehaviour
    {
        const int TrailPool = 4;
        const float LineLength = 520f;
        const float LineSeconds = 0.18f;

        static ExteriorTransitFx _instance;

        ParticleSystem _burst;
        AudioSource _audio;
        readonly Line[] _lines = new Line[TrailPool];

        sealed class Line
        {
            public TrailRenderer Trail;
            public Vector3 A;
            public Vector3 B;
            public float T = -1f;
        }

        static ExteriorTransitFx Get(Transform exterior)
        {
            if (_instance != null)
                return _instance;
            var go = new GameObject("TransitFx");
            go.transform.SetParent(exterior, false);
            _instance = go.AddComponent<ExteriorTransitFx>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            _burst = CombatFxKit.Burst(transform, "TransitBurst", 220, 0.9f, gravity: false, stretch: false);
            for (var i = 0; i < TrailPool; i++)
            {
                var go = new GameObject("TransitLine" + i);
                go.transform.SetParent(transform, false);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.45f;
                trail.minVertexDistance = 4f;
                trail.widthMultiplier = 2.6f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                trail.sharedMaterial = CombatFxKit.Beam();
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
                go.SetActive(false);
                _lines[i] = new Line { Trail = trail };
            }

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        /// <summary>A ship leaving the system at <paramref name="pos"/> along <paramref name="dir"/>.</summary>
        public static void JumpOut(Transform exterior, Vector3 pos, Vector3 dir, VoyageMode mode)
        {
            if (mode == VoyageMode.Sublight)
                return;
            var fx = Get(exterior);
            var look = VoyageFx.LookOf(mode);
            fx.Flash(pos, dir, look.Bright, mode == VoyageMode.PrlBond);
            if (mode != VoyageMode.PrlBond)
                fx.Shoot(pos, pos + dir * LineLength, look.Bright);
            fx.Sound(pos, mode == VoyageMode.PrlBond ? VoyageAudio.Bond : VoyageAudio.Jump);
        }

        /// <summary>A ship dropping into the system at <paramref name="pos"/>, heading <paramref name="dir"/>.</summary>
        public static void DropIn(Transform exterior, Vector3 pos, Vector3 dir, VoyageMode mode)
        {
            if (mode == VoyageMode.Sublight)
                return;
            var fx = Get(exterior);
            var look = VoyageFx.LookOf(mode);
            if (mode != VoyageMode.PrlBond)
                fx.Shoot(pos - dir * LineLength, pos, look.Bright);
            fx.Flash(pos, dir, look.Bright, true);
            fx.Sound(pos, mode == VoyageMode.PrlBond ? VoyageAudio.Bond : VoyageAudio.DropOut);
        }

        void Flash(Vector3 pos, Vector3 dir, Color color, bool ring)
        {
            CombatFxKit.Emit(_burst, pos, Color.Lerp(color, Color.white, 0.6f), 70f, 0.4f);
            CombatFxKit.Emit(_burst, pos, color, 130f, 0.7f);
            if (!ring)
                return;
            var side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f)
                side = Vector3.right;
            side.Normalize();
            var up = Vector3.Cross(side, dir).normalized;
            for (var i = 0; i < 28; i++)
            {
                var a = i / 28f * Mathf.PI * 2f;
                var r = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                CombatFxKit.Emit(_burst, pos + r * 6f, color, 7f, 0.9f, r * 55f);
            }
        }

        void Shoot(Vector3 from, Vector3 to, Color color)
        {
            foreach (var l in _lines)
            {
                if (l.T >= 0f)
                    continue;
                l.A = from;
                l.B = to;
                l.T = 0f;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                l.Trail.colorGradient = g;
                l.Trail.transform.position = from;
                l.Trail.Clear();
                l.Trail.gameObject.SetActive(true);
                l.Trail.emitting = true;
                enabled = true;
                return;
            }
        }

        void Sound(Vector3 pos, AudioClip clip)
        {
            var cam = Camera.main;
            if (cam == null || clip == null)
                return;
            // Heard through the hull, faint and only when close enough to matter.
            var d = Vector3.Distance(cam.transform.position, pos);
            var v = 0.22f * Mathf.Clamp01(1f - d / 700f);
            if (v > 0.02f)
                _audio.PlayOneShot(clip, v);
        }

        void Update()
        {
            var any = false;
            var dt = Time.deltaTime;
            foreach (var l in _lines)
            {
                if (l.T < 0f)
                    continue;
                any = true;
                l.T += dt;
                var k = Mathf.Clamp01(l.T / LineSeconds);
                l.Trail.transform.position = Vector3.Lerp(l.A, l.B, k * k);
                if (k >= 1f && l.Trail.emitting)
                    l.Trail.emitting = false;
                if (l.T > LineSeconds + l.Trail.time)
                {
                    l.T = -1f;
                    l.Trail.gameObject.SetActive(false);
                }
            }

            if (!any)
                enabled = false;
        }
    }
}
