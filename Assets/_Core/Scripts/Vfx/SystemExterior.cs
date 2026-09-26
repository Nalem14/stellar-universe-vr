using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// One shared system-scale exterior around the CIC. Windows look out onto this —
    /// not per-pane diorama clones.
    /// </summary>
    public class SystemExterior : MonoBehaviour
    {
        public const float OrbitBase = WorldScale.OrbitBase;
        public const float OrbitStep = WorldScale.OrbitStep;

        FocusContext _focus;
        int _builtSystemId = -1;
        Transform _content;
        Vector3 _starWorldPos;
        readonly Dictionary<int, Transform> _planets = new();
        readonly Dictionary<int, Transform> _asteroids = new();
        readonly Dictionary<int, Transform> _fleets = new();
        readonly Dictionary<int, FleetMotion> _fleetMotion = new();

        struct FleetMotion
        {
            public Vector3 From;
            public Vector3 To;
            public double StartUnix;
            public double EndUnix;
        }

        public void Bind(FocusContext focus)
        {
            SpaceBackdrop.Ensure(transform);
            _focus = focus;
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.Changed += OnFocusChanged;
                _focus.FleetsChanged -= OnFocusChanged;
                _focus.FleetsChanged += OnFocusChanged;
            }

            RebuildAll();
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.FleetsChanged -= OnFocusChanged;
            }
        }

        void OnFocusChanged()
        {
            if (_focus == null)
                return;
            if (_focus.SystemId != _builtSystemId)
                RebuildAll();
            else
                SyncFleets();
        }

        void Update()
        {
            if (_focus == null)
                return;
            TickFleetMotion();
        }

        public void RebuildAll()
        {
            EnsureContentRoot();
            ClearChildren(_content);
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();
            _fleetMotion.Clear();
            _builtSystemId = _focus != null ? _focus.SystemId : 0;

            BuildStar(_focus);
            BuildNebula(_focus);
            if (_focus == null || !_focus.HasSystem)
                return;

            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            foreach (var planet in _focus.Planets)
                BuildPlanet(planet, owned);

            foreach (var rock in _focus.Asteroids)
                BuildAsteroid(rock);

            // Light direction from star toward first planet / ecliptic
            var lightDir = Vector3.right;
            if (_planets.Count > 0)
            {
                foreach (var kv in _planets)
                {
                    lightDir = (kv.Value.position - _starWorldPos).normalized;
                    break;
                }
            }

            SystemBodyKit.ApplyStarLightDirection(lightDir);
            SyncFleets();
        }

        void EnsureContentRoot()
        {
            if (_content != null)
                return;
            var go = new GameObject("ExteriorContent");
            go.transform.SetParent(transform, false);
            _content = go.transform;
        }

        /// <summary>The exterior ship of <paramref name="fleetId"/> (our own hull is there too, hidden).</summary>
        public bool TryGetFleet(int fleetId, out Transform ship) =>
            _fleets.TryGetValue(fleetId, out ship) && ship != null;

        public bool TryGetPlanet(int planetId, out Transform planet) =>
            _planets.TryGetValue(planetId, out planet) && planet != null;

        public bool TryGetAsteroid(int asteroidId, out Transform rock) =>
            _asteroids.TryGetValue(asteroidId, out rock) && rock != null;

        public Vector3 ResolveFleetWorldPosition(FocusFleet fleet)
        {
            if (fleet == null)
                return transform.TransformPoint(new Vector3(OrbitBase * 0.55f, 0f, 0f));

            var now = UnixNow();
            if (_fleetMotion.TryGetValue(fleet.Id, out var motion) && motion.EndUnix > now)
            {
                var t = Mathf.Clamp01((float)((now - motion.StartUnix) /
                                              System.Math.Max(0.01, motion.EndUnix - motion.StartUnix)));
                return Vector3.Lerp(motion.From, motion.To, Smooth(t));
            }

            return IdleFleetPosition(fleet);
        }

        /// <summary>Stable travel heading from motion endpoints (not current→dest, which shrinks as the rig catches up).</summary>
        public bool TryGetFleetTravelDirection(FocusFleet fleet, out Vector3 flatDir)
        {
            flatDir = Vector3.forward;
            if (fleet == null)
                return false;
            var now = UnixNow();
            if (!_fleetMotion.TryGetValue(fleet.Id, out var motion) || motion.EndUnix <= now)
                return false;
            var d = motion.To - motion.From;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f)
                return false;
            flatDir = d.normalized;
            return true;
        }

        Vector3 IdleFleetPosition(FocusFleet fleet)
        {
            if (fleet.PlanetId > 0 && _planets.TryGetValue(fleet.PlanetId, out var planet))
            {
                var radial = (planet.position - transform.position).normalized;
                if (radial.sqrMagnitude < 0.01f)
                    radial = Vector3.right;
                var side = Vector3.Cross(Vector3.up, radial).normalized;
                var slot = 1;
                var body = _focus != null ? _focus.FindPlanet(fleet.PlanetId) : null;
                if (body != null)
                    slot = body.Slot;
                var standoff = WorldScale.FleetStandoff(WorldScale.PlanetRadius(slot));
                return planet.position + radial * standoff
                    + side * (WorldScale.FleetLateral + (fleet.Id % 5) * WorldScale.FleetLateralStep);
            }

            if (fleet.AsteroidId > 0 && _asteroids.TryGetValue(fleet.AsteroidId, out var rock))
                return rock.position
                    + Vector3.up * WorldScale.FleetStandoff(WorldScale.AsteroidRadius) * 0.35f
                    + Vector3.right * WorldScale.FleetLateral;

            var angle = (fleet.Id % 12) * 0.55f;
            var r = OrbitBase * 1.08f;
            return transform.TransformPoint(new Vector3(Mathf.Cos(angle) * r, WorldScale.EclipticHeight,
                Mathf.Sin(angle) * r));
        }

        void SyncFleets()
        {
            if (_focus == null)
                return;

            var seen = new HashSet<int>();
            var now = UnixNow();

            foreach (var fleet in _focus.Fleets)
            {
                if (!fleet.VisibleIn(_focus.SystemId, (long)now))
                    continue;
                seen.Add(fleet.Id);
                if (!_fleets.TryGetValue(fleet.Id, out var tf) || tf == null)
                {
                    var mine = DiplomacyIndex.ResolveFleet(fleet) == EmpireStance.Owned;
                    var go = CreateFleetShip(fleet, IdleFleetPosition(fleet), mine);
                    tf = go.transform;
                    _fleets[fleet.Id] = tf;
                }
                else
                {
                    var mine = DiplomacyIndex.ResolveFleet(fleet) == EmpireStance.Owned;
                    var view = tf.GetComponent<FleetShipView>();
                    if (view != null)
                        view.Bind(fleet, mine);
                }

                if (fleet.DestTime > now)
                {
                    if (!_fleetMotion.TryGetValue(fleet.Id, out var motion) ||
                        System.Math.Abs(motion.EndUnix - fleet.DestTime) > 0.5)
                    {
                        var to = IdleFleetPosition(fleet);
                        if (fleet.FromSystemId > 0 && fleet.DestSystemId > 0 &&
                            fleet.FromSystemId != fleet.DestSystemId)
                        {
                            var edge = transform.TransformPoint(new Vector3(OrbitBase + OrbitStep * 8f, 2f, 0f));
                            if (fleet.DestSystemId == _focus.SystemId)
                                to = IdleFleetPosition(fleet);
                            else
                                to = edge;
                        }

                        _fleetMotion[fleet.Id] = new FleetMotion
                        {
                            From = tf.position,
                            To = to,
                            StartUnix = now,
                            EndUnix = fleet.DestTime
                        };
                    }
                }
                else
                {
                    _fleetMotion.Remove(fleet.Id);
                    tf.position = IdleFleetPosition(fleet);
                }

                var hide = _focus.ViewFleetId > 0 && fleet.Id == _focus.ViewFleetId;
                SetVisible(tf, !hide);
            }

            var remove = new List<int>();
            foreach (var kv in _fleets)
            {
                if (!seen.Contains(kv.Key))
                {
                    if (kv.Value != null)
                        Destroy(kv.Value.gameObject);
                    remove.Add(kv.Key);
                }
            }

            foreach (var id in remove)
            {
                _fleets.Remove(id);
                _fleetMotion.Remove(id);
            }
        }

        void TickFleetMotion()
        {
            var now = UnixNow();
            foreach (var fleet in _focus.Fleets)
            {
                if (!fleet.VisibleIn(_focus.SystemId, (long)now))
                    continue;
                if (!_fleets.TryGetValue(fleet.Id, out var tf) || tf == null)
                    continue;
                // Own inhabited hull: hidden by SyncFleets on view / fleet change, never scanned per frame.
                if (_focus.ViewFleetId == fleet.Id)
                    continue;

                if (_fleetMotion.TryGetValue(fleet.Id, out var motion) && motion.EndUnix > now)
                {
                    var t = Mathf.Clamp01((float)((now - motion.StartUnix) /
                                                  System.Math.Max(0.01, motion.EndUnix - motion.StartUnix)));
                    tf.position = Vector3.Lerp(motion.From, motion.To, Smooth(t));
                    var dir = motion.To - motion.From;
                    if (dir.sqrMagnitude > 0.01f)
                        tf.rotation = Quaternion.Slerp(tf.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up),
                            Time.deltaTime * 3f);
                }
                else
                {
                    var target = IdleFleetPosition(fleet);
                    tf.position = Vector3.Lerp(tf.position, target, Time.deltaTime * 2.5f);
                }
            }
        }

        void BuildStar(FocusContext focus)
        {
            var kit = SystemBodyKit.Star(focus != null ? focus.SystemTypeKey : null,
                focus != null ? focus.SystemType : 0);
            _starWorldPos = transform.position;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Star";
            body.transform.SetParent(_content, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = Vector3.one * (WorldScale.StarRadius * 2f);
            StripCollider(body);
            body.GetComponent<MeshRenderer>().sharedMaterial = kit.Surface;

            // Soft billboard corona — avoids sphere-UV ring artifacts that wash as nested shells
            var coronaScale = WorldScale.StarRadius * 2f * kit.CoronaScale;
            CreateStarBillboard("StarCorona", coronaScale * 1.05f, kit.Corona);
            CreateStarBillboard("StarHalo", coronaScale * 1.35f, kit.Corona);

            var lightGo = new GameObject("StarLight");
            lightGo.transform.SetParent(_content, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.Lerp(kit.Color, Color.white, 0.15f);
            light.intensity = Mathf.Clamp(kit.Intensity * 1.15f, 1.6f, 3.2f);
            light.range = WorldScale.StarLightRange;
            // No realtime shadows on Quest (additional light shadows are off in the URP config anyway).
            light.shadows = LightShadows.None;
        }

        void CreateStarBillboard(string name, float size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(_content, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(size, size, 1f);
            StripCollider(go);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var face = go.AddComponent<BillboardFace>();
            face.Mode = BillboardFace.FaceMode.Camera;
        }

        void BuildNebula(FocusContext focus)
        {
            var systemId = focus != null ? focus.SystemId : 1;
            if (systemId <= 0)
                systemId = 1;
            var mat = SystemBodyKit.NebulaMat(systemId);
            // 2–3 soft billboards seeded by system id — immersion, not server data
            var count = 2 + (Mathf.Abs(systemId) % 2);
            for (var i = 0; i < count; i++)
            {
                var salt = systemId * 17 + i * 91;
                var angle = (salt % 360) * Mathf.Deg2Rad;
                var elev = ((salt / 7) % 40 - 20) * 0.02f;
                var dist = WorldScale.OrbitBase + WorldScale.OrbitStep * (4.5f + (salt % 5) * 0.6f);
            var pos = new Vector3(Mathf.Cos(angle) * dist, elev * dist + WorldScale.EclipticHeight * 2f,
                Mathf.Sin(angle) * dist);
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Nebula_" + i;
                go.transform.SetParent(_content, false);
                go.transform.localPosition = pos;
                var size = 140f + (salt % 60);
                go.transform.localScale = new Vector3(size, size * 0.7f, 1f);
                go.transform.LookAt(_content.position + Vector3.up * WorldScale.EclipticHeight);
                StripCollider(go);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        void BuildPlanet(FocusPlanet planet, int ownedUserId)
        {
            var slot = Mathf.Max(1, planet.Slot);
            var pos = OrbitPosition(slot, planet.Id);
            var radius = WorldScale.PlanetRadius(slot);
            var kind = SystemBodyKit.ClassifyPlanet(slot, planet.Id, planet.Habitability);
            var own = SystemBodyKit.ResolveOwnership(planet.UserId, ownedUserId);

            var root = new GameObject("Planet_" + planet.Id);
            root.transform.SetParent(_content, false);
            root.transform.localPosition = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Globe";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = Vector3.one * (radius * 2f);
            StripCollider(body);
            body.GetComponent<MeshFilter>().sharedMesh = SphereMesh.Smooth;
            body.GetComponent<MeshRenderer>().sharedMaterial = SystemBodyKit.PlanetMat(kind, own);

            var atmo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            atmo.name = "Atmosphere";
            atmo.transform.SetParent(root.transform, false);
            atmo.transform.localScale = Vector3.one * (radius * 2f * 1.055f);
            StripCollider(atmo);
            atmo.GetComponent<MeshFilter>().sharedMesh = SphereMesh.Smooth;
            var ar = atmo.GetComponent<MeshRenderer>();
            ar.sharedMaterial = SystemBodyKit.AtmosphereMat(kind, own);
            ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ar.receiveShadows = false;

            // Sparse ring only when seed says so and planet is large enough to read
            if (kind == SystemBodyKit.PlanetKind.Gas && (planet.Id % 5) == 0 && radius >= 16f)
                BuildRing(root.transform, radius, own);

            var spin = root.AddComponent<BodySpin>();
            var deg = kind == SystemBodyKit.PlanetKind.Gas ? 6.5f : 3.8f + (planet.Id % 5) * 0.35f;
            var tilt = 8f + (planet.Id % 17);
            spin.Configure(planet.Id, deg, tilt);

            _planets[planet.Id] = root.transform;
        }

        void BuildRing(Transform parent, float planetRadius, SystemBodyKit.Ownership own)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(parent, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var outer = planetRadius * 1.85f;
            ring.transform.localScale = new Vector3(outer * 2f, 0.08f, outer * 2f);
            StripCollider(ring);
            // Reuse nebula-ish soft material tinted cold / cyan
            var mat = SystemBodyKit.NebulaMat(own == SystemBodyKit.Ownership.Owned ? 2 : 0);
            var r = ring.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void BuildAsteroid(FocusAsteroid rock)
        {
            var slot = Mathf.Max(1, rock.Slot);
            var pos = OrbitPosition(slot, rock.Id + 31) * 1.05f;
            var go = new GameObject("Asteroid_" + rock.Id);
            go.transform.SetParent(_content, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * AsteroidScale(rock);
            go.transform.localRotation = Quaternion.Euler((rock.Id * 17) % 360, (rock.Id * 29) % 360, (rock.Id * 11) % 360);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = SystemBodyKit.AsteroidMesh(rock.Id);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SystemBodyKit.AsteroidMat();

            var spin = go.AddComponent<BodySpin>();
            spin.Configure(rock.Id, 12f + (rock.Id % 7), 25f + (rock.Id % 40));
            _asteroids[rock.Id] = go.transform;
            go.SetActive(!rock.Gone);
        }

        /// <summary>A mined field shrinks toward half its size (live reserves, AsteroidService).</summary>
        static float AsteroidScale(FocusAsteroid rock) =>
            WorldScale.AsteroidRadius * (0.85f + (Mathf.Abs(rock.Id) % 5) * 0.06f) * 2f * Mathf.Lerp(0.5f, 1f, rock.Fill);

        /// <summary>Reserves were read: resize the fields, drop the mined-out ones.</summary>
        public void ApplyAsteroidReserves()
        {
            if (_focus == null)
                return;
            foreach (var rock in _focus.Asteroids)
            {
                if (!_asteroids.TryGetValue(rock.Id, out var tr) || tr == null)
                    continue;
                tr.localScale = Vector3.one * AsteroidScale(rock);
                tr.gameObject.SetActive(!rock.Gone);
            }
        }

        public static Vector3 OrbitPosition(int slot, int salt)
        {
            var radius = WorldScale.OrbitRadius(slot);
            var angle = (slot * 1.7f + (salt % 7) * 0.45f) % (Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * 0.15f * radius * 0.05f,
                Mathf.Sin(angle) * radius);
        }

        GameObject CreateFleetShip(FocusFleet fleet, Vector3 worldPos, bool owned)
        {
            var root = new GameObject("Fleet_" + fleet.Id);
            root.transform.SetParent(_content, false);
            root.transform.position = worldPos;
            var view = root.AddComponent<FleetShipView>();
            view.Bind(fleet, owned);
            return root;
        }

        static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(col);
            else
                Object.DestroyImmediate(col);
        }

        static readonly List<Renderer> RendererScratch = new();

        static void SetVisible(Transform root, bool visible)
        {
            root.GetComponentsInChildren(true, RendererScratch);
            for (var i = 0; i < RendererScratch.Count; i++)
                RendererScratch[i].enabled = visible;
            RendererScratch.Clear();
        }

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        static double UnixNow()
        {
            return (System.DateTime.UtcNow - new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc))
                .TotalSeconds;
        }

        static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
