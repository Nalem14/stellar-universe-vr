using System.Collections.Generic;
using Core.App;
using Core.Stations;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The Tactical station seen through the windows, in the shared <see cref="SystemExterior"/>:
    /// <list type="bullet">
    /// <item>defence platforms in orbit of our worlds — one per defence type held (two or three for a big
    /// stock), sized by tier, on a slowly turning inclined ring; a new batch deploys with a flash;</item>
    /// <item>troop shuttles between a docked ship and its planet when the troop bay loads or unloads;</item>
    /// <item>orbital sieges: attackers bombard the surface, the world's defences answer (from its platforms
    /// when it is one of ours), and the outcome ends in a fireball on the losing side.</item>
    /// </list>
    /// Pooled lines and trails, two particle systems, shared meshes / materials.
    /// </summary>
    public sealed class ExteriorTacticalFx : MonoBehaviour
    {
        const int BeamPool = 6;
        const int ShuttlePool = 8;
        const int MaxPlatforms = 12;
        const float ShuttleSeconds = 3.4f;

        SystemExterior _exterior;
        FocusContext _focus;
        EconomyService _eco;
        Transform _root;
        ParticleSystem _flares;
        ParticleSystem _debris;
        string _platformSig = string.Empty;
        int _platformSystem = -1;

        sealed class Platform
        {
            public int PlanetId;
            public Transform Tr;
            public float Radius;
            public float Angle;
            public float Speed;
            public float Size;
            public float Tilt;
            public float Deploy = -1f;
        }

        sealed class Beam
        {
            public LineRenderer Line;
            public Vector3 From;
            public Vector3 To;
            public Color Color;
            public float T = -1f;
            public float Width;
        }

        sealed class Shuttle
        {
            public Transform Tr;
            public TrailRenderer Trail;
            public Vector3 P0, P1, P2;
            public float T = -1f;
            public float Delay;
        }

        sealed class Assault
        {
            public int Fleet;
            public int Planet;
            public bool OurPlanet;
            public float NextSalvo;
            public float NextReply;
            public Vector3 LastPos;
        }

        readonly List<Platform> _platforms = new();
        readonly List<Beam> _beams = new();
        readonly List<Shuttle> _shuttles = new();
        readonly List<Assault> _assaults = new();
        readonly Dictionary<int, Vector3> _lastAttackerPos = new();
        readonly Dictionary<string, int> _lastCounts = new();

        public static ExteriorTacticalFx Attach(SystemExterior exterior, FocusContext focus, EconomyService eco)
        {
            var fx = exterior.gameObject.AddComponent<ExteriorTacticalFx>();
            fx._exterior = exterior;
            fx._focus = focus;
            fx._eco = eco;
            fx.Build();
            if (focus != null)
            {
                focus.FleetsChanged += fx.RefreshAssaults;
                focus.Changed += fx.OnFocus;
            }

            if (eco != null)
                eco.Changed += fx.RefreshPlatforms;
            CombatEvents.TroopTransfer += fx.OnTroops;
            CombatEvents.SiegeResolved += fx.OnSiegeResolved;
            return fx;
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.FleetsChanged -= RefreshAssaults;
                _focus.Changed -= OnFocus;
            }

            if (_eco != null)
                _eco.Changed -= RefreshPlatforms;
            CombatEvents.TroopTransfer -= OnTroops;
            CombatEvents.SiegeResolved -= OnSiegeResolved;
        }

        void Build()
        {
            _root = new GameObject("TacticalFx").transform;
            _root.SetParent(transform, false);
            _flares = CombatFxKit.Burst(_root, "TacticalFlares", 64, 0.8f, gravity: false, stretch: false);
            _debris = CombatFxKit.Burst(_root, "TacticalDebris", 96, 2.2f, gravity: false, stretch: true);
            for (var i = 0; i < BeamPool; i++)
            {
                var lr = new GameObject("SiegeBeam" + i).AddComponent<LineRenderer>();
                lr.transform.SetParent(_root, false);
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = CombatFxKit.Beam();
                lr.enabled = false;
                _beams.Add(new Beam { Line = lr });
            }

            for (var i = 0; i < ShuttlePool; i++)
            {
                var go = new GameObject("TroopShuttle" + i);
                go.transform.SetParent(_root, false);
                go.transform.localScale = Vector3.one * 2.4f;
                // Hull: a platform-kit disc squashed into a lander silhouette; glow = engine band.
                var body = DefensePlatformKit.Create(go.transform, "Lander", true);
                body.transform.localScale = new Vector3(0.55f, 0.7f, 1f);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.9f;
                trail.minVertexDistance = 0.4f;
                trail.widthMultiplier = 1.6f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.sharedMaterial = CombatFxKit.Beam();
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
                go.SetActive(false);
                _shuttles.Add(new Shuttle { Tr = go.transform, Trail = trail });
            }
        }

        void OnFocus()
        {
            if (_focus != null && _focus.SystemId != _platformSystem)
                RefreshPlatforms();
            RefreshAssaults();
        }

        // ── Planet geometry ───────────────────────────────────────────────────────

        bool Planet(int planetId, out Vector3 center, out float radius)
        {
            center = default;
            radius = 0f;
            if (!_exterior.TryGetPlanet(planetId, out var tr))
                return false;
            center = tr.position;
            var p = _focus?.FindPlanet(planetId);
            radius = WorldScale.PlanetRadius(p != null ? p.Slot : 1);
            return true;
        }

        static Vector3 Surface(Vector3 center, float radius, Vector3 toward, float spread)
        {
            var dir = (toward - center).normalized;
            if (dir.sqrMagnitude < 0.5f)
                dir = Vector3.up;
            dir = (dir + Random.insideUnitSphere * spread).normalized;
            return center + dir * radius;
        }

        // ── Defence platforms ─────────────────────────────────────────────────────

        void RefreshPlatforms()
        {
            if (_focus == null || _eco == null)
                return;
            var sig = new System.Text.StringBuilder();
            sig.Append(_focus.SystemId).Append(':');
            var wanted = new List<(int planet, string type, int copies, int tier)>();
            foreach (var p in _focus.Planets)
            {
                if (!OwnedPlanets.Contains(p.Id) || !_eco.TryGet(p.Id, out var eco))
                    continue;
                var counts = TroopCatalog.Counts(eco.Raw?["defenseUnits"]);
                foreach (var kv in counts)
                {
                    var copies = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(kv.Value + 1, 6f)), 1, 3);
                    wanted.Add((p.Id, kv.Key, copies, TroopCatalog.DefenseTier(kv.Key)));
                    sig.Append(p.Id).Append('/').Append(kv.Key).Append('=').Append(copies).Append(';');
                }
            }

            var s = sig.ToString();
            if (s == _platformSig)
                return;
            var sameSystem = _platformSystem == _focus.SystemId;
            _platformSig = s;
            _platformSystem = _focus.SystemId;

            // Which (planet, type) pairs grew since the last read: those platforms deploy with a flash.
            var grown = new HashSet<string>();
            foreach (var w in wanted)
            {
                var key = w.planet + "/" + w.type;
                if (sameSystem && (!_lastCounts.TryGetValue(key, out var before) || before < w.copies))
                    grown.Add(key);
            }

            _lastCounts.Clear();
            foreach (var w in wanted)
                _lastCounts[w.planet + "/" + w.type] = w.copies;

            foreach (var pl in _platforms)
                if (pl.Tr != null)
                    Destroy(pl.Tr.gameObject);
            _platforms.Clear();

            var perPlanet = new Dictionary<int, int>();
            foreach (var w in wanted)
                perPlanet[w.planet] = (perPlanet.TryGetValue(w.planet, out var n) ? n : 0) + w.copies;
            var placed = new Dictionary<int, int>();
            foreach (var w in wanted)
            {
                for (var c = 0; c < w.copies && _platforms.Count < MaxPlatforms; c++)
                {
                    var p = _focus.FindPlanet(w.planet);
                    var radius = WorldScale.PlanetRadius(p != null ? p.Slot : 1);
                    var idx = placed.TryGetValue(w.planet, out var k) ? k : 0;
                    placed[w.planet] = idx + 1;
                    var total = Mathf.Max(1, perPlanet[w.planet]);
                    var go = DefensePlatformKit.Create(_root, "DefensePlatform_" + w.type + "_" + c, true);
                    var size = 2.6f + w.tier * 0.75f;
                    go.transform.localScale = Vector3.one * size;
                    var pl = new Platform
                    {
                        PlanetId = w.planet,
                        Tr = go.transform,
                        Radius = radius * 1.38f + (idx % 2) * radius * 0.12f,
                        Angle = idx / (float)total * 360f + w.planet % 37,
                        Speed = 5f - w.tier * 0.4f,
                        Size = size,
                        Tilt = 14f + (w.planet % 9),
                        Deploy = grown.Contains(w.planet + "/" + w.type) ? 0f : -1f
                    };
                    _platforms.Add(pl);
                }
            }
        }

        bool PlatformPose(Platform pl, out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = Quaternion.identity;
            if (!_exterior.TryGetPlanet(pl.PlanetId, out var planet))
                return false;
            var a = pl.Angle * Mathf.Deg2Rad;
            var local = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * pl.Radius;
            var tilt = Quaternion.Euler(pl.Tilt, pl.PlanetId % 90, 0f);
            pos = planet.position + tilt * local;
            // Deck to space, barrels along the orbit.
            var outward = (pos - planet.position).normalized;
            var along = tilt * new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            rot = Quaternion.LookRotation(along, outward);
            return true;
        }

        // ── Troop shuttles ────────────────────────────────────────────────────────

        void OnTroops(int fleet, int planet, bool toPlanet, int units)
        {
            if (!_exterior.TryGetFleet(fleet, out var ship) || !Planet(planet, out var center, out var radius))
                return;
            var count = Mathf.Clamp(Mathf.CeilToInt(units / 25f), 2, 5);
            var delay = 0f;
            foreach (var s in _shuttles)
            {
                if (count <= 0)
                    break;
                if (s.T >= 0f)
                    continue;
                var hull = ship.position + Random.insideUnitSphere * (WorldScale.ShipSpan * 0.2f);
                var ground = Surface(center, radius * 1.01f, ship.position, 0.25f);
                s.P0 = toPlanet ? hull : ground;
                s.P2 = toPlanet ? ground : hull;
                var mid = (s.P0 + s.P2) * 0.5f;
                var span = (s.P2 - s.P0).magnitude;
                s.P1 = mid + (mid - center).normalized * span * 0.45f + Random.insideUnitSphere * span * 0.15f;
                s.T = 0f;
                s.Delay = delay;
                delay += 0.35f;
                s.Tr.position = s.P0;
                s.Tr.gameObject.SetActive(true);
                s.Trail.Clear();
                s.Trail.emitting = false;
                count--;
            }
        }

        // ── Sieges ────────────────────────────────────────────────────────────────

        void RefreshAssaults()
        {
            _assaults.Clear();
            if (_focus == null)
                return;
            var now = FleetOrderGate.UnixNow();
            foreach (var f in _focus.Fleets)
            {
                if (f.AttackEndTime <= 0 || f.PlanetId <= 0 || !f.VisibleIn(_focus.SystemId, now))
                    continue;
                var planet = _focus.FindPlanet(f.PlanetId);
                // Only a fleet over someone else's world is an attacker (the owner's ships defend).
                if (planet == null || planet.UserId == f.UserId)
                    continue;
                _assaults.Add(new Assault
                {
                    Fleet = f.Id,
                    Planet = f.PlanetId,
                    OurPlanet = OwnedPlanets.Contains(f.PlanetId),
                    NextSalvo = Time.time + Random.Range(0.3f, 1.4f),
                    NextReply = Time.time + Random.Range(1.5f, 3f)
                });
            }
        }

        void TickAssaults()
        {
            var now = Time.time;
            foreach (var a in _assaults)
            {
                if (!_exterior.TryGetFleet(a.Fleet, out var ship) || !Planet(a.Planet, out var center, out var radius))
                    continue;
                a.LastPos = ship.position;
                _lastAttackerPos[a.Fleet] = ship.position;
                if (now >= a.NextSalvo)
                {
                    a.NextSalvo = now + Random.Range(1.4f, 2.6f);
                    var hit = Surface(center, radius, ship.position, 0.35f);
                    Fire(ship.position + Random.insideUnitSphere * (WorldScale.ShipSpan * 0.15f), hit,
                        new Color(1f, 0.45f, 0.2f, 1f), 3.2f);
                    CombatEvents.RaiseBombard(a.Fleet, a.Planet, true);
                }

                if (now >= a.NextReply)
                {
                    a.NextReply = now + Random.Range(2.2f, 3.6f);
                    // Our world answers from its platforms; any other from its surface batteries.
                    var from = Surface(center, radius, ship.position, 0.5f);
                    foreach (var pl in _platforms)
                    {
                        if (pl.PlanetId != a.Planet || pl.Tr == null || Random.value < 0.4f)
                            continue;
                        from = pl.Tr.position;
                        break;
                    }

                    Fire(from, ship.position + Random.insideUnitSphere * (WorldScale.ShipSpan * 0.2f),
                        a.OurPlanet ? new Color(0.35f, 0.95f, 1f, 1f) : new Color(1f, 0.3f, 0.25f, 1f), 2.2f);
                    CombatEvents.RaiseBombard(a.Fleet, a.Planet, false);
                    CombatEvents.RaiseHit(a.Fleet, 0, 1);
                }
            }
        }

        void Fire(Vector3 from, Vector3 to, Color color, float width)
        {
            Beam free = null;
            foreach (var b in _beams)
            {
                if (b.T < 0f)
                {
                    free = b;
                    break;
                }
            }

            free ??= _beams[0];
            free.From = from;
            free.To = to;
            free.Color = color;
            free.Width = width;
            free.T = 0f;
            free.Line.enabled = true;
            CombatFxKit.Emit(_flares, from, color, 4f, 0.3f);
        }

        void OnSiegeResolved(int planet, bool attackersWon)
        {
            if (!Planet(planet, out var center, out var radius))
                return;
            if (attackersWon)
            {
                // The world falls: fires across the day side.
                for (var i = 0; i < 8; i++)
                {
                    var at = Surface(center, radius, center + Random.onUnitSphere * 100f, 0.9f);
                    CombatFxKit.Emit(_flares, at, new Color(1f, 0.5f, 0.2f, 1f), radius * 0.35f, 1.6f + i * 0.1f);
                }
            }

            // The losing attackers are gone from the poll: blow them up where they last stood.
            var gone = new List<int>();
            foreach (var kv in _lastAttackerPos)
                if (_focus.FindFleet(kv.Key) == null)
                    gone.Add(kv.Key);
            foreach (var id in gone)
            {
                var at = _lastAttackerPos[id];
                _lastAttackerPos.Remove(id);
                CombatFxKit.Emit(_flares, at, new Color(1f, 0.85f, 0.6f, 1f), WorldScale.ShipSpan * 2.2f, 0.9f);
                CombatFxKit.Emit(_flares, at, new Color(1f, 0.45f, 0.15f, 1f), WorldScale.ShipSpan * 3f, 1.6f);
                for (var i = 0; i < 30; i++)
                    CombatFxKit.Emit(_debris, at + Random.insideUnitSphere * 2f, new Color(1f, 0.7f, 0.35f, 1f),
                        Random.Range(0.4f, 1.1f), Random.Range(1.2f, 2.2f), Random.onUnitSphere * Random.Range(8f, 26f));
            }
        }

        // ── Frame ─────────────────────────────────────────────────────────────────

        void LateUpdate()
        {
            var dt = Time.deltaTime;

            foreach (var pl in _platforms)
            {
                if (pl.Tr == null)
                    continue;
                pl.Angle += pl.Speed * dt;
                if (!PlatformPose(pl, out var pos, out var rot))
                {
                    pl.Tr.gameObject.SetActive(false);
                    continue;
                }

                pl.Tr.gameObject.SetActive(true);
                pl.Tr.SetPositionAndRotation(pos, rot);
                if (pl.Deploy >= 0f)
                {
                    if (pl.Deploy == 0f)
                        CombatFxKit.Emit(_flares, pos, new Color(0.4f, 0.95f, 1f, 1f), pl.Size * 3f, 0.8f);
                    pl.Deploy += dt / 1.2f;
                    var k = Utils.MotionEase.SmoothOut(pl.Deploy) + Mathf.Sin(Mathf.Clamp01(pl.Deploy) * Mathf.PI) * 0.1f;
                    pl.Tr.localScale = Vector3.one * (pl.Size * Mathf.Max(0.01f, k));
                    if (pl.Deploy >= 1f)
                    {
                        pl.Deploy = -1f;
                        pl.Tr.localScale = Vector3.one * pl.Size;
                    }
                }
            }

            TickAssaults();

            foreach (var b in _beams)
            {
                if (b.T < 0f)
                    continue;
                b.T += dt;
                // Crosses in 0.22 s, holds 0.25 s, fades over 0.5 s: reads at orbital range.
                var grow = Mathf.Clamp01(b.T / 0.22f);
                var fade = Mathf.Clamp01((b.T - 0.47f) / 0.5f);
                b.Line.SetPosition(0, b.From);
                b.Line.SetPosition(1, Vector3.Lerp(b.From, b.To, grow));
                b.Line.widthMultiplier = b.Width * (1f + Mathf.Sin(b.T * 45f) * 0.15f);
                var c = b.Color;
                c.a = 1f - fade;
                b.Line.startColor = c;
                b.Line.endColor = new Color(1f, 1f, 1f, c.a);
                if (grow >= 1f && b.T - dt < 0.22f)
                {
                    CombatFxKit.Emit(_flares, b.To, b.Color, 9f, 0.7f);
                    CombatFxKit.Emit(_flares, b.To, new Color(1f, 0.9f, 0.7f, 1f), 4f, 0.35f);
                    for (var i = 0; i < 8; i++)
                        CombatFxKit.Emit(_debris, b.To, new Color(1f, 0.7f, 0.35f, 1f), 0.4f, 1f,
                            Random.onUnitSphere * Random.Range(4f, 10f));
                }

                if (fade >= 1f)
                {
                    b.T = -1f;
                    b.Line.enabled = false;
                }
            }

            foreach (var s in _shuttles)
            {
                if (s.T < 0f)
                    continue;
                if (s.Delay > 0f)
                {
                    s.Delay -= dt;
                    continue;
                }

                s.Trail.emitting = true;
                s.T += dt / ShuttleSeconds;
                var u = Utils.MotionEase.SmoothInOut(Mathf.Clamp01(s.T));
                var p = Bezier(s.P0, s.P1, s.P2, u);
                var ahead = Bezier(s.P0, s.P1, s.P2, Mathf.Min(1f, u + 0.02f));
                s.Tr.position = p;
                if ((ahead - p).sqrMagnitude > 1e-4f)
                    s.Tr.rotation = Quaternion.LookRotation(ahead - p, Vector3.up);
                var k = Mathf.Sin(Mathf.Clamp01(s.T) * Mathf.PI);
                s.Trail.startColor = new Color(0.45f, 0.9f, 1f, 0.9f * k + 0.1f);
                s.Trail.endColor = new Color(0.45f, 0.9f, 1f, 0f);
                if (s.T >= 1f)
                {
                    CombatFxKit.Emit(_flares, s.P2, new Color(0.5f, 0.95f, 1f, 1f), 3f, 0.5f);
                    s.T = -1f;
                    s.Trail.emitting = false;
                    s.Tr.gameObject.SetActive(false);
                }
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            var u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }
    }
}
