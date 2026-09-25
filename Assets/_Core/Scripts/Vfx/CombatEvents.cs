using System;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Combat as the rest of the ship lives it. The battle board (<see cref="HexBattleController"/>) is the
    /// only reader of the fight; it raises these for the other layers: the real ships outside the
    /// windows (<see cref="ExteriorCombatFx"/>) and the bridge itself (<see cref="BridgeCombatFx"/>).
    /// Ids are fleet ids (fleets.id = one ship), not battle-ship ids.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>A weapon from one ship to another (colour = weapon family, heavy = torpedo).</summary>
        public static event Action<int, int, Color, bool> Shot;

        /// <summary>A ship used a skill on itself (shield, armour, engines, repair).</summary>
        public static event Action<int, Color> SelfCast;

        /// <summary>A ship lost hull and / or shield points (fleet, hull lost, shield lost).</summary>
        public static event Action<int, int, int> Hit;

        public static event Action<int> Destroyed;

        /// <summary>The ship we are aboard is in a live fight on the table (red alert) — or no longer.</summary>
        public static event Action<bool> Engaged;

        public static bool IsEngaged { get; private set; }

        public static void RaiseShot(int src, int dst, Color color, bool heavy) => Shot?.Invoke(src, dst, color, heavy);
        public static void RaiseSelf(int fleet, Color color) => SelfCast?.Invoke(fleet, color);
        public static void RaiseHit(int fleet, int hull, int shield) => Hit?.Invoke(fleet, hull, shield);
        public static void RaiseDestroyed(int fleet) => Destroyed?.Invoke(fleet);

        public static void SetEngaged(bool on)
        {
            if (IsEngaged == on)
                return;
            IsEngaged = on;
            Engaged?.Invoke(on);
        }
    }

    /// <summary>Shared combat materials: one soft glow, one beam profile (all layers batch on them).</summary>
    public static class CombatFxKit
    {
        static Texture2D _beamTex;
        static Material _glow;
        static Material _beam;

        /// <summary>Soft beam profile across the width (v), bright core: no hard line edges.</summary>
        public static Texture2D BeamTexture()
        {
            if (_beamTex != null)
                return _beamTex;
            const int h = 32;
            _beamTex = new Texture2D(4, h, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, name = "SU_BeamProfile" };
            for (var y = 0; y < h; y++)
            {
                var v = Mathf.Abs(y / (h - 1f) * 2f - 1f);
                var a = Mathf.Pow(1f - v, 2.2f) + Mathf.Pow(1f - v, 12f) * 0.8f;
                for (var x = 0; x < 4; x++)
                    _beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }

            _beamTex.Apply(false, true);
            return _beamTex;
        }

        /// <summary>Radial soft dot for flashes and sparks (procedural, no texture asset needed).</summary>
        static Texture2D _dot;

        public static Texture2D DotTexture()
        {
            if (_dot != null)
                return _dot;
            const int n = 64;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SU_GlowDot" };
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x - n * 0.5f + 0.5f, y - n * 0.5f + 0.5f).magnitude / (n * 0.5f);
                var a = Mathf.Clamp01(1f - d);
                a = a * a * 0.8f + Mathf.Pow(a, 10f) * 0.6f;
                _dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }

            _dot.Apply(false, true);
            return _dot;
        }

        public static Material Glow(Texture tex = null)
        {
            if (_glow != null)
                return _glow;
            _glow = Make(tex != null ? tex : DotTexture(), 2.2f);
            return _glow;
        }

        public static Material Beam()
        {
            if (_beam != null)
                return _beam;
            _beam = Make(BeamTexture(), 2.6f);
            return _beam;
        }

        static Material Make(Texture tex, float mul)
        {
            var shader = Shader.Find("SU/ParticleGlow") ?? Shader.Find("SU/UnlitEmissive");
            var m = new Material(shader) { name = "SU_CombatGlow" };
            if (m.HasProperty("_MainTex"))
                m.mainTexture = tex;
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", Color.white);
            if (m.HasProperty("_EmissionMul"))
                m.SetFloat("_EmissionMul", mul);
            return m;
        }

        /// <summary>Additive billboard burst system on the shared glow (pooled particles, emitted by hand).</summary>
        public static ParticleSystem Burst(Transform parent, string name, int max, float lifetime, bool gravity,
            bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = max;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.gravityModifier = gravity ? 0.6f : 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, stretch
                ? AnimationCurve.Linear(0f, 1f, 1f, 0.2f)
                : new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.4f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = Glow();
            pr.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretch)
            {
                pr.velocityScale = 0.04f;
                pr.lengthScale = 1f;
            }

            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
            return ps;
        }

        public static void Emit(ParticleSystem ps, Vector3 at, Color color, float size, float life,
            Vector3 velocity = default)
        {
            if (ps == null)
                return;
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = velocity,
                startColor = color,
                startSize = size,
                startLifetime = life,
                applyShapeToPosition = false
            }, 1);
        }
    }
}
