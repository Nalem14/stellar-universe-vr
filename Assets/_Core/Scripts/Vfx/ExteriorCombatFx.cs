using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The fight outside the windows: every shot the battle board replays is also fired between the real
    /// ships of the shared <see cref="SystemExterior"/> — beam or torpedo slug, impact flare on the hull,
    /// a blue shimmer when the shield takes it, a fireball and debris when a ship dies. Our own hull is
    /// hidden aboard, so our shots leave from where we are. Pooled: 8 lines, 2 particle systems.
    /// </summary>
    public sealed class ExteriorCombatFx : MonoBehaviour
    {
        const int Pool = 8;

        SystemExterior _exterior;
        ParticleSystem _flares;
        ParticleSystem _debris;

        class Shot
        {
            public LineRenderer Line;
            public Transform Src;
            public Transform Dst;
            public Vector3 Offset;
            /// <summary>Planet shooter only: the battery's spot on the surface (ships fire from their centre).</summary>
            public Vector3 SrcOffset;
            public Color Color;
            public float T = -1f;
            public float Travel;
            public float Width;
            public bool Heavy;
            public bool Hit;
        }

        readonly List<Shot> _shots = new();

        public static ExteriorCombatFx Attach(SystemExterior exterior)
        {
            var fx = exterior.gameObject.AddComponent<ExteriorCombatFx>();
            fx._exterior = exterior;
            fx.Build();
            CombatEvents.Shot += fx.OnShot;
            CombatEvents.SelfCast += fx.OnSelf;
            CombatEvents.Hit += fx.OnHit;
            CombatEvents.Destroyed += fx.OnDestroyed;
            return fx;
        }

        void OnDestroy()
        {
            CombatEvents.Shot -= OnShot;
            CombatEvents.SelfCast -= OnSelf;
            CombatEvents.Hit -= OnHit;
            CombatEvents.Destroyed -= OnDestroyed;
        }

        void Build()
        {
            _flares = CombatFxKit.Burst(transform, "CombatFlares", 48, 0.7f, gravity: false, stretch: false);
            _debris = CombatFxKit.Burst(transform, "CombatDebris", 96, 2.2f, gravity: false, stretch: true);
            for (var i = 0; i < Pool; i++)
            {
                var lr = new GameObject("ExtBeam" + i).AddComponent<LineRenderer>();
                lr.transform.SetParent(transform, false);
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCapVertices = 2;
                lr.textureMode = LineTextureMode.Stretch;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = CombatFxKit.Beam();
                lr.enabled = false;
                _shots.Add(new Shot { Line = lr });
            }
        }

        /// <summary>Hit point on a hull: spread over the ship's span so a volley does not stack on one pixel.</summary>
        static Vector3 HullOffset() => Random.insideUnitSphere * (WorldScale.ShipSpan * 0.18f);

        /// <summary>
        /// A combatant outside: a fleet's ship, or (id &lt; 0) the besieged world itself — its ground batteries fire
        /// from, and shots land on, its surface (planetary siege, web 69d40af).
        /// </summary>
        bool TryGetCombatant(int id, out Transform t, out float surface)
        {
            surface = 0f;
            if (id < 0)
            {
                if (!_exterior.TryGetPlanet(-id, out t))
                    return false;
                surface = t.lossyScale.x * 0.5f;
                return true;
            }

            return _exterior.TryGetFleet(id, out t);
        }

        /// <summary>Where a shot leaves / lands on <paramref name="t"/> seen from <paramref name="other"/>.</summary>
        static Vector3 FaceToward(Transform t, float surface, Vector3 other) =>
            surface > 0f ? (other - t.position).normalized * (surface * 1.01f) + Random.insideUnitSphere * (surface * 0.12f)
                : HullOffset();

        void OnShot(int src, int dst, Color color, bool heavy)
        {
            if (!TryGetCombatant(src, out var a, out var aSurface) || !TryGetCombatant(dst, out var b, out var bSurface))
                return;
            Shot free = null;
            foreach (var s in _shots)
                if (s.T < 0f)
                {
                    free = s;
                    break;
                }

            free ??= _shots[0];
            free.Src = a;
            free.Dst = b;
            free.SrcOffset = aSurface > 0f ? FaceToward(a, aSurface, b.position) : Vector3.zero;
            free.Offset = FaceToward(b, bSurface, a.position);
            free.Color = color;
            free.Heavy = heavy;
            var dist = (b.position - a.position).magnitude;
            // Same pacing as the board: lasers read as instant, torpedoes cross.
            free.Travel = heavy ? 1.1f : 0.14f;
            free.Width = Mathf.Clamp(dist * 0.012f, 0.8f, 6f) * (heavy ? 2.2f : 1f);
            free.T = 0f;
            free.Hit = false;
            free.Line.enabled = true;
            // Muzzle flare at the gun.
            CombatFxKit.Emit(_flares, a.position + (aSurface > 0f ? free.SrcOffset : HullOffset() * 0.5f), color,
                WorldScale.ShipSpan * (aSurface > 0f ? 0.8f : 0.35f), 0.3f);
        }

        void OnSelf(int fleet, Color color)
        {
            if (!TryGetCombatant(fleet, out var t, out var surface))
                return;
            if (surface > 0f)
            {
                // A world raising its garrison shields: a wide shimmer hugging the globe.
                CombatFxKit.Emit(_flares, t.position, color * 0.6f, surface * 2.3f, 0.9f);
                return;
            }

            // A bubble of light around the hull (shield up, engines overcharged, repair drones…).
            for (var i = 0; i < 3; i++)
                CombatFxKit.Emit(_flares, t.position + HullOffset() * 0.4f, color * 0.8f, WorldScale.ShipSpan * (1.1f + i * 0.3f),
                    0.6f + i * 0.15f);
        }

        void OnHit(int fleet, int hull, int shield)
        {
            if (!TryGetCombatant(fleet, out var t, out var surface))
                return;
            if (surface > 0f)
            {
                // The planetary shield flickers over the globe; what gets through scars the surface.
                var spot = t.position + Random.onUnitSphere * surface;
                if (shield > 0)
                    CombatFxKit.Emit(_flares, t.position, new Color(0.35f, 0.7f, 1f, 0.6f), surface * 2.25f, 0.5f);
                if (hull > 0)
                    CombatFxKit.Emit(_flares, spot, new Color(1f, 0.55f, 0.2f, 1f), WorldScale.ShipSpan * 1.4f, 0.8f);
                return;
            }

            // Shield took it: a wide blue shimmer; hull took it: a hot small flare and a few sparks.
            if (shield > 0)
                CombatFxKit.Emit(_flares, t.position, new Color(0.35f, 0.7f, 1f, 0.8f), WorldScale.ShipSpan * 1.6f, 0.5f);
            if (hull > 0)
            {
                var at = t.position + HullOffset();
                CombatFxKit.Emit(_flares, at, new Color(1f, 0.55f, 0.2f, 1f), WorldScale.ShipSpan * 1f, 0.6f);
                for (var i = 0; i < 6; i++)
                    CombatFxKit.Emit(_debris, at, new Color(1f, 0.7f, 0.35f, 1f), 0.5f, 1.2f,
                        Random.onUnitSphere * Random.Range(6f, 16f));
            }
        }

        void OnDestroyed(int fleet)
        {
            if (!_exterior.TryGetFleet(fleet, out var t))
                return;
            var at = t.position;
            var cam = Camera.main;
            var v = cam != null ? 0.45f * Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, at) / 700f) : 0f;
            if (v > 0.03f)
                Core.Audio.SfxBus.Play2D(Core.Audio.SfxLibrary.Get(Core.Audio.SfxLibrary.Explosion), v,
                    Random.Range(0.85f, 1.05f), Core.Audio.SfxBus.Priority.Alert, 0.2f);
            CombatFxKit.Emit(_flares, at, new Color(1f, 0.85f, 0.6f, 1f), WorldScale.ShipSpan * 2.2f, 0.9f);
            CombatFxKit.Emit(_flares, at, new Color(1f, 0.45f, 0.15f, 1f), WorldScale.ShipSpan * 3.2f, 1.6f);
            for (var i = 0; i < 40; i++)
                CombatFxKit.Emit(_debris, at + Random.insideUnitSphere * 2f,
                    Color.Lerp(new Color(1f, 0.5f, 0.15f, 1f), new Color(1f, 0.9f, 0.6f, 1f), Random.value),
                    Random.Range(0.4f, 1.2f), Random.Range(1.2f, 2.4f), Random.onUnitSphere * Random.Range(8f, 30f));
        }

        void Update()
        {
            var dt = Time.deltaTime;
            foreach (var s in _shots)
            {
                if (s.T < 0f)
                    continue;
                if (s.Src == null || s.Dst == null)
                {
                    s.T = -1f;
                    s.Line.enabled = false;
                    continue;
                }

                s.T += dt;
                var from = s.Src.position + s.SrcOffset;
                var to = s.Dst.position + s.Offset;
                var grow = Mathf.Clamp01(s.T / s.Travel);
                var fade = Mathf.Clamp01((s.T - s.Travel) / 0.35f);
                if (s.Heavy)
                {
                    var head = Vector3.Lerp(from, to, grow);
                    var tail = Vector3.Lerp(from, to, Mathf.Max(0f, grow - 0.12f));
                    s.Line.SetPosition(0, tail);
                    s.Line.SetPosition(1, head);
                }
                else
                {
                    s.Line.SetPosition(0, from);
                    s.Line.SetPosition(1, Vector3.Lerp(from, to, grow));
                }

                s.Line.widthMultiplier = s.Width * (1f + Mathf.Sin(s.T * 50f) * 0.12f);
                var c = s.Color;
                c.a = 1f - fade;
                s.Line.startColor = c;
                s.Line.endColor = new Color(1f, 1f, 1f, c.a);
                if (!s.Hit && grow >= 1f)
                {
                    s.Hit = true;
                    CombatFxKit.Emit(_flares, to, s.Color, WorldScale.ShipSpan * (s.Heavy ? 1.6f : 0.9f), 0.5f);
                }

                if (fade >= 1f)
                {
                    s.T = -1f;
                    s.Line.enabled = false;
                }
            }
        }
    }
}
