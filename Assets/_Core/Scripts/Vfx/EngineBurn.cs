using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Live engine thrust: breath + flicker on flame, glow, and plume.
    /// One instance per engine. MaterialPropertyBlock, no Update allocs.
    /// </summary>
    [ExecuteAlways]
    public class EngineBurn : MonoBehaviour
    {
        static readonly int EmissionMulId = Shader.PropertyToID("_EmissionMul");

        public Transform Plasma;
        public Transform Flame;
        public Transform Wash;
        public ParticleSystem Plume;
        public float Breath = 2.15f;
        public float Flicker = 13.4f;
        public float Phase;

        Vector3 _plasmaScale, _flameScale, _washScale;
        Vector3 _plasmaPos, _flamePos, _washPos;
        Renderer _plasmaR, _flameR, _washR;
        MaterialPropertyBlock _block;
        float _rate = 22f;
        float _speedMin = 3.4f;
        float _speedMax = 5.1f;
        float _sizeMin = 0.16f;
        float _sizeMax = 0.34f;
        bool _cached;

        void OnEnable()
        {
            _cached = false;
        }

        void Cache()
        {
            Grab(Plasma, ref _plasmaPos, ref _plasmaScale, ref _plasmaR);
            Grab(Flame, ref _flamePos, ref _flameScale, ref _flameR);
            Grab(Wash, ref _washPos, ref _washScale, ref _washR);
            if (Plume != null)
            {
                _rate = Plume.emission.rateOverTime.constant;
                if (_rate < 1f)
                    _rate = 22f;
                ReadRange(Plume.main.startSpeed, 3.4f, 5.1f, out _speedMin, out _speedMax);
                ReadRange(Plume.main.startSize, 0.16f, 0.34f, out _sizeMin, out _sizeMax);
            }

            _block ??= new MaterialPropertyBlock();
            _cached = true;
        }

        static void ReadRange(ParticleSystem.MinMaxCurve curve, float fallbackMin, float fallbackMax, out float min,
            out float max)
        {
            if (curve.mode == ParticleSystemCurveMode.TwoConstants)
            {
                min = curve.constantMin;
                max = curve.constantMax;
            }
            else
            {
                min = max = curve.constant;
            }

            if (min < 0.02f)
                min = fallbackMin;
            if (max < 0.02f)
                max = fallbackMax;
        }

        static void Grab(Transform t, ref Vector3 pos, ref Vector3 scale, ref Renderer rend)
        {
            if (t == null)
                return;
            pos = t.localPosition;
            scale = t.localScale;
            rend = t.GetComponent<Renderer>();
        }

        void LateUpdate()
        {
            Tick(Application.isPlaying ? Time.deltaTime : 0.0166f, !Application.isPlaying);
        }

        public void Warm(float seconds)
        {
            if (!_cached)
                Cache();
            if (Plume == null)
                return;
            Plume.Simulate(Mathf.Max(0.05f, seconds), true, true);
            Tick(0f, false);
        }

        void Tick(float dt, bool simulatePlume)
        {
            if (!_cached)
                Cache();
            var t = Time.time + Phase;
            var breath = 0.5f + 0.5f * Mathf.Sin(t * Breath);
            var flick = 0.5f + 0.5f * Mathf.Sin(t * Flicker) * Mathf.Sin(t * Flicker * 1.618f);
            var stutter = 0.5f + 0.5f * Mathf.Sin(t * 29.3f + Phase * 2.1f);
            var drive = Mathf.Clamp01(breath * 0.54f + flick * 0.33f + stutter * 0.13f);
            var longZ = Mathf.Lerp(0.88f, 1.28f, drive);
            var fat = Mathf.Lerp(0.9f, 1.1f, drive);

            Apply(Plasma, _plasmaR, _plasmaPos, _plasmaScale, fat, Mathf.Lerp(0.94f, 1.14f, drive), 0f, drive, 4.2f,
                8.8f);
            Apply(Flame, _flameR, _flamePos, _flameScale, fat, longZ, -0.06f * drive, drive, 2.4f, 6.8f);
            Apply(Wash, _washR, _washPos, _washScale, fat * 0.96f, longZ * 1.08f, -0.1f * drive, drive, 1.2f, 4.2f);

            if (Plume == null)
                return;
            var em = Plume.emission;
            em.rateOverTime = _rate * Mathf.Lerp(0.48f, 1.32f, drive);
            var main = Plume.main;
            var thrust = Mathf.Lerp(0.68f, 1.28f, drive);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_speedMin * thrust, _speedMax * thrust);
            var swell = Mathf.Lerp(0.82f, 1.2f, drive);
            main.startSize = new ParticleSystem.MinMaxCurve(_sizeMin * swell, _sizeMax * swell);
            if (simulatePlume)
                Plume.Simulate(Mathf.Max(dt, 0.008f), true, false);
        }

        void Apply(Transform tr, Renderer rend, Vector3 pos, Vector3 scale, float fat, float longZ, float aft,
            float drive, float glowMin, float glowMax)
        {
            if (tr == null)
                return;
            tr.localScale = new Vector3(scale.x * fat, scale.y * fat, scale.z * longZ);
            tr.localPosition = pos + new Vector3(0f, 0f, aft);
            if (rend == null || rend.sharedMaterial == null || !rend.sharedMaterial.HasProperty(EmissionMulId))
                return;
            rend.GetPropertyBlock(_block);
            _block.SetFloat(EmissionMulId, Mathf.Lerp(glowMin, glowMax, drive));
            rend.SetPropertyBlock(_block);
        }
    }
}
