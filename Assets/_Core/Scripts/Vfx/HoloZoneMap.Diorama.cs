using System.Collections.Generic;
using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Holo table v2 (docs/design/HOLOTABLE.md): the system as a miniature diorama floating in a projection
    /// cone above the plate — a lit star, planets as real little worlds on thin orbits, rock belts, ships as
    /// holo silhouettes of their own 9×9 hull. Labels only where they help (our ships, the station world);
    /// the rest shows on hover / selection. One shared mesh / material per kind (Quest budget).
    /// </summary>
    public partial class HoloZoneMap
    {
        /// <summary>Height of the diorama's ecliptic above the plate (m, map local).</summary>
        public const float DioramaLift = 0.2f;
        const float StarRadius = 0.048f;
        const float ShipScale = 0.0042f;
        const float LabelHeight = 0.02f;

        static readonly Dictionary<int, Mesh> OrbitMeshes = new();
        static Mesh _coneMesh;
        static Mesh _ringMesh;
        Material _shipMat;
        Material _orbitMat;
        MaterialPropertyBlock _mpb;

        public static float DioramaY => DioramaLift;

        // ── Scaffold: projection cone + ecliptic ──────────────────────────────────

        void BuildProjection()
        {
            // Light cone from the projector lens to the ecliptic: HoloSurface scanlines read as a beam.
            // The beam belongs to the table (projector), not to the grabbable content.
            var cone = new GameObject("ProjectionCone");
            cone.transform.SetParent(transform, false);
            cone.AddComponent<MeshFilter>().sharedMesh = _coneMesh ??= Cone(0.1f, WorldScale.HoloDiscRadius * 0.98f,
                DioramaLift, 40);
            var cr = cone.AddComponent<MeshRenderer>();
            cr.sharedMaterial = _art.Holo(Texture2D.whiteTexture, new Color(0.3f, 0.85f, 1f, 0.035f));
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Ecliptic: a faint plate the system sits on, and its rim.
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "Ecliptic";
            plane.transform.SetParent(_root, false);
            plane.transform.localPosition = new Vector3(0f, DioramaLift - 0.004f, 0f);
            plane.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            plane.transform.localScale = Vector3.one * (WorldScale.HoloDiscRadius * 2f);
            DropCollider(plane);
            plane.GetComponent<MeshRenderer>().sharedMaterial = _art.HoloDetail(
                _art.OrbitPlate != null ? _art.OrbitPlate : Texture2D.whiteTexture, new Color(0.45f, 0.8f, 1f, 0.16f), 0.08f);
            _ecliptic = plane;

            var rim = new GameObject("EclipticRim");
            rim.transform.SetParent(_root, false);
            rim.transform.localPosition = new Vector3(0f, DioramaLift - 0.003f, 0f);
            rim.AddComponent<MeshFilter>().sharedMesh = Annulus(WorldScale.HoloDiscRadius * 0.985f, 0.006f, 96);
            var rr = rim.AddComponent<MeshRenderer>();
            rr.sharedMaterial = _art.Lit(Texture2D.whiteTexture, new Color(0.3f, 0.9f, 1f, 1f), 2.2f);
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _eclipticRim = rim;
        }

        GameObject _ecliptic;
        GameObject _eclipticRim;

        /// <summary>The galaxy keeps its own flat plate: the ecliptic only shows with the system.</summary>
        void SetEclipticVisible(bool on)
        {
            if (_plate != null)
                _plate.SetActive(!on);
            if (_ecliptic != null)
                _ecliptic.SetActive(on);
            if (_eclipticRim != null)
                _eclipticRim.SetActive(on);
        }

        // ── Placement ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Orbit radius on the table: the slots this system actually uses are spread evenly from the star to
        /// the rim (raw slot numbers go up to 12 and would fall off the table or crowd the centre).
        /// </summary>
        static readonly SortedSet<int> UsedSlots = new();

        static void IndexSlots(FocusContext focus)
        {
            UsedSlots.Clear();
            if (focus == null)
                return;
            foreach (var p in focus.Planets)
                if (p.Slot > 0)
                    UsedSlots.Add(p.Slot);
            foreach (var a in focus.Asteroids)
                if (a.Slot > 0)
                    UsedSlots.Add(a.Slot);
        }

        const float InnerOrbit = 0.2f;
        const float OuterOrbit = 0.8f;

        static readonly List<int> SlotList = new();

        static float OrbitR(float slot)
        {
            if (UsedSlots.Count == 0)
                return Mathf.Lerp(InnerOrbit, OuterOrbit, Mathf.InverseLerp(1f, 8f, slot));
            if (UsedSlots.Count == 1)
                return (InnerOrbit + OuterOrbit) * 0.5f + Mathf.Clamp(slot - UsedSlots.Min, -1f, 1f) * 0.08f;
            SlotList.Clear();
            SlotList.AddRange(UsedSlots);
            // Rank of the slot among used ones, fractional between two (anomalies sit between orbits).
            float rank;
            if (slot <= SlotList[0])
                rank = 0f;
            else if (slot >= SlotList[SlotList.Count - 1])
                rank = SlotList.Count - 1 + Mathf.Clamp01((slot - SlotList[SlotList.Count - 1]) * 0.5f) * 0.4f;
            else
            {
                var i = 1;
                while (SlotList[i] < slot)
                    i++;
                rank = i - 1 + Mathf.InverseLerp(SlotList[i - 1], SlotList[i], slot);
            }

            return Mathf.Lerp(InnerOrbit, OuterOrbit, rank / (SlotList.Count - 1));
        }

        static Vector3 PlanetLocal(int slot, int id)
        {
            var r = OrbitR(Mathf.Max(1, slot));
            var ang = StableAngle(id * 97 + slot * 13);
            return new Vector3(Mathf.Cos(ang) * r, DioramaLift, Mathf.Sin(ang) * r);
        }

        static Vector3 AsteroidLocal(int slot, int id)
        {
            var r = OrbitR(Mathf.Max(1, slot));
            var ang = StableAngle(id * 53 + 7);
            return new Vector3(Mathf.Cos(ang) * r, DioramaLift, Mathf.Sin(ang) * r);
        }

        static float PlanetSize(int slot) => 0.03f + Mathf.Clamp(slot, 1, 10) * 0.0018f;

        // ── Star / orbits ─────────────────────────────────────────────────────────

        void PlaceStar(int typeHint)
        {
            var focus = _focus ?? FocusContext.Current;
            var kit = SystemBodyKit.Star(focus?.SystemTypeKey, focus != null ? focus.SystemType : typeHint);
            var go = new GameObject("TokenStar");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, DioramaLift + 0.02f, 0f);
            _tokenRoots.Add(go);

            var body = Sphere(go.transform, "StarBody", StarRadius, kit.Surface);
            body.AddComponent<BodySpin>();
            var corona = GameObject.CreatePrimitive(PrimitiveType.Quad);
            corona.name = "Corona";
            corona.transform.SetParent(go.transform, false);
            corona.transform.localScale = Vector3.one * (StarRadius * 2f * Mathf.Max(2.2f, kit.CoronaScale));
            DropCollider(corona);
            corona.AddComponent<BillboardFace>();
            corona.GetComponent<MeshRenderer>().sharedMaterial = kit.Corona;

            var col = go.AddComponent<SphereCollider>();
            col.radius = StarRadius * 1.3f;
            col.isTrigger = true;

            var token = go.AddComponent<HoloToken>();
            token.Kind = HoloTokenKind.System;
            token.Id = focus != null ? focus.SystemId : 0;
            // Slot -1 = the local star: not a MoveFleetToSystem target from its own system map.
            token.Slot = -1;
            if (focus != null && GalaxyCatalog.TryGet(focus.SystemId, out var here))
            {
                token.GalaxyX = here.X;
                token.GalaxyY = here.Y;
            }

            token.DisplayName = focus != null ? GalaxyCatalog.Label(focus.SystemId) : string.Empty;
            token.CaptureHome();
            AddTokenLabel(go.transform, token.DisplayName, StarRadius + 0.05f, new Color(1f, 0.93f, 0.7f, 1f),
                startVisible: false);
            _tokens.Add(token);
        }

        void PlaceOrbitRings(FocusContext focus)
        {
            IndexSlots(focus);
            var seen = new HashSet<int>();
            foreach (var p in focus.Planets)
                if (p.Slot > 0 && seen.Add(p.Slot))
                    PlaceOrbitRing(p.Slot);
            foreach (var a in focus.Asteroids)
                if (a.Slot > 0 && seen.Add(a.Slot))
                    PlaceOrbitRing(a.Slot, belt: true);
        }

        void PlaceOrbitRing(int slot, bool belt = false)
        {
            var r = OrbitR(slot);
            var go = new GameObject("Orbit_" + slot);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, DioramaLift, 0f);
            var key = Mathf.RoundToInt(r * 1000f);
            if (!OrbitMeshes.TryGetValue(key, out var mesh) || mesh == null)
                OrbitMeshes[key] = mesh = Annulus(r, 0.0022f, 128);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _orbitMat ??= _art.Lit(Texture2D.whiteTexture, new Color(0.22f, 0.55f, 0.7f, 1f), 1.4f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tokenRoots.Add(go);
        }

        // ── Planets ───────────────────────────────────────────────────────────────

        void PlacePlanet(int slot, int id, Color color, float radius, string displayName, bool stationView)
        {
            var focus = _focus ?? FocusContext.Current;
            var planet = focus?.FindPlanet(id);
            var go = new GameObject("TokenPlanet_" + id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = PlanetLocal(slot, id);
            _tokenRoots.Add(go);

            var size = PlanetSize(slot);
            var kind = SystemBodyKit.ClassifyPlanet(slot, id, planet?.Habitability ?? 0);
            var own = SystemBodyKit.ResolveOwnership(planet?.UserId ?? 0, FocusContext.OwnedUserId());
            var body = Sphere(go.transform, "Body", size, SystemBodyKit.PlanetMat(kind, own));
            body.AddComponent<BodySpin>();

            // Owner stance as a thin ring round the world (none for free worlds).
            if ((planet?.UserId ?? 0) > 0)
                FlatRing(go.transform, "OwnerRing", size * 1.75f, 0.0028f, color, 2.4f);

            if (stationView)
                AddStationModel(go.transform, size);

            var col = go.AddComponent<SphereCollider>();
            col.radius = Mathf.Max(size * 1.6f, 0.035f);
            col.isTrigger = true;

            var label = string.IsNullOrEmpty(displayName) || displayName == "planet"
                ? Trans.Get("planet") + " " + slot
                : displayName;
            AddTokenLabel(go.transform, label, size + 0.035f,
                stationView ? CicArtKit.Amber : new Color(0.85f, 0.96f, 1f, 1f), bold: stationView,
                startVisible: stationView);
            Tag(go, HoloTokenKind.Planet, id, slot, owned: false, busy: false, label);
        }

        /// <summary>Our orbital station beside the world we stand on, with a "you are here" beam.</summary>
        void AddStationModel(Transform planet, float size)
        {
            var st = new GameObject("OrbitalStation").transform;
            st.SetParent(planet, false);
            st.localPosition = new Vector3(size * 2.1f, size * 0.6f, 0f);
            var ring = FlatRing(st, "StationRing", 0.012f, 0.003f, CicArtKit.Amber, 3f);
            ring.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var hub = Sphere(st, "Hub", 0.005f, _art.AmberEmit(3f));
            hub.transform.localScale = Vector3.one * 0.01f;
            var spin = st.gameObject.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 40f;
            spin.BobMeters = 0f;
            HereBeam(planet, CicArtKit.Amber);
        }

        /// <summary>Vertical beam down to the ecliptic: where the player is.</summary>
        void HereBeam(Transform parent, Color tint)
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "HereBeam";
            DropCollider(beam);
            beam.transform.SetParent(parent, false);
            beam.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            beam.transform.localScale = new Vector3(0.004f, 0.07f, 0.004f);
            beam.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(Texture2D.whiteTexture,
                new Color(tint.r, tint.g, tint.b, 0.6f));
        }

        // ── Asteroids ─────────────────────────────────────────────────────────────

        void PlaceAsteroid(int slot, int id)
        {
            var go = new GameObject("TokenRock_" + id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = AsteroidLocal(slot, id);
            _tokenRoots.Add(go);

            // A small field of rocks, combined into one mesh.
            var parts = new List<CombineInstance>();
            var rng = new System.Random(id * 7919);
            for (var i = 0; i < 9; i++)
            {
                var off = new Vector3((float)(rng.NextDouble() - 0.5) * 0.06f, (float)(rng.NextDouble() - 0.5) * 0.012f,
                    (float)(rng.NextDouble() - 0.5) * 0.06f);
                var s = 0.004f + (float)rng.NextDouble() * 0.006f;
                parts.Add(new CombineInstance
                {
                    mesh = SystemBodyKit.AsteroidMesh(id + i),
                    transform = Matrix4x4.TRS(off, Quaternion.Euler(rng.Next(360), rng.Next(360), 0f), Vector3.one * s)
                });
            }

            var field = new GameObject("Rocks");
            field.transform.SetParent(go.transform, false);
            var mesh = new Mesh { name = "HoloRocks" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            field.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = field.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SystemBodyKit.AsteroidMat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ownedMeshes.Add(mesh);
            var spin = field.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 5f;
            spin.BobMeters = 0f;

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.045f;
            col.isTrigger = true;
            var label = Trans.Get("asteroidField");
            AddTokenLabel(go.transform, label, 0.045f, new Color(0.9f, 0.86f, 0.75f, 1f), startVisible: false);
            Tag(go, HoloTokenKind.Asteroid, id, slot, owned: false, busy: false, label);
        }

        readonly List<Mesh> _ownedMeshes = new();

        // ── Ships ─────────────────────────────────────────────────────────────────

        GameObject PlaceFleet(int slot, int id, Color color, int seed, bool owned, bool busy, string displayName,
            bool active, EmpireStance stance = EmpireStance.Unknown)
        {
            var focus = _focus ?? FocusContext.Current;
            var fleet = focus?.FindFleet(id);
            var go = new GameObject("TokenFleet_" + id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = FleetLocal(fleet, focus, slot, id, out var heading);
            go.transform.localRotation = Quaternion.LookRotation(heading, Vector3.up);
            _tokenRoots.Add(go);

            var hull = new GameObject("Hull");
            hull.transform.SetParent(go.transform, false);
            var scale = ShipScale * (active ? 1.35f : 1f);
            // Flattened hulls read badly from above: holo ships get a little extra height.
            hull.transform.localScale = new Vector3(scale, scale * 1.8f, scale);
            var mesh = ShipHullBuilder.SilhouetteMesh(fleet?.Modules ?? (IReadOnlyList<FocusShipModule>)System.Array.Empty<FocusShipModule>(), id);
            hull.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = hull.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ShipMat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var tint = active ? new Color(0.45f, 1f, 1f, 1f) : color;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetColor("_Color", tint * 0.55f);
            _mpb.SetColor("_Emission", tint);
            _mpb.SetFloat("_EmissionMul", active ? 1.5f : 1f);
            _mpb.SetFloat("_Rim", 2.2f);
            _mpb.SetFloat("_Pulse", busy ? 0.9f : 0.2f);
            mr.SetPropertyBlock(_mpb);

            // Hover ring (HoloFleetOrders scales it on hover) and state marks.
            var ring = FlatRing(go.transform, "HoverRing", 0.03f * (active ? 1.35f : 1f), 0.0022f,
                new Color(tint.r, tint.g, tint.b, owned ? 0.9f : 0.5f), 1.8f);
            ring.transform.localPosition = new Vector3(0f, -0.006f, 0f);
            if (busy)
                FlatRing(go.transform, "Busy", 0.022f, 0.003f, CicArtKit.Amber, 3f);
            if (active)
                HereBeam(go.transform, CicArtKit.Cyan);

            var bob = go.AddComponent<HoloSpin>();
            bob.DegreesPerSecond = 0f;
            bob.BobMeters = active ? 0.006f : 0.004f;

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.04f;
            col.isTrigger = true;

            var name = string.IsNullOrEmpty(displayName) || displayName == "ship" ? "#" + id : displayName;
            AddTokenLabel(go.transform, name, 0.035f,
                active ? CicArtKit.Cyan : new Color(tint.r * 0.7f + 0.3f, tint.g * 0.7f + 0.3f, tint.b * 0.7f + 0.3f, 1f),
                bold: active, startVisible: owned);
            Tag(go, HoloTokenKind.Fleet, id, slot, owned, busy, name);
            return go;
        }

        Material ShipMat()
        {
            if (_shipMat != null)
                return _shipMat;
            var shader = Shader.Find("SU/HoloCrystal");
            _shipMat = shader != null ? new Material(shader) : _art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan, 1.4f);
            _shipMat.enableInstancing = true;
            return _shipMat;
        }

        /// <summary>A ship at a world parks beside it; at a rock field, among the rocks; else on its orbit ring.</summary>
        static Vector3 FleetLocal(FocusFleet fleet, FocusContext focus, int slot, int id, out Vector3 heading)
        {
            var ang = StableAngle(id * 17);
            if (fleet != null && fleet.PlanetId > 0 && focus?.FindPlanet(fleet.PlanetId) is { } p)
            {
                var c = PlanetLocal(p.Slot, p.Id);
                var r = PlanetSize(p.Slot) * 2.6f + 0.012f;
                var off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                heading = new Vector3(-off.z, 0f, off.x);
                return c + off * r + Vector3.up * 0.012f;
            }

            if (fleet != null && fleet.AsteroidId > 0 && focus?.FindAsteroid(fleet.AsteroidId) is { } a)
            {
                var c = AsteroidLocal(a.Slot, a.Id);
                var off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                heading = new Vector3(-off.z, 0f, off.x);
                return c + off * 0.05f + Vector3.up * 0.015f;
            }

            var rr = OrbitR(Mathf.Max(1, slot)) + 0.04f;
            var pos = new Vector3(Mathf.Cos(ang) * rr, DioramaLift + 0.012f, Mathf.Sin(ang) * rr);
            heading = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            return pos;
        }

        // ── Labels ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 3D text over a token, facing the viewer. Child named "Label" (toggled by
        /// <see cref="SetTokenLabelVisible"/>). <paramref name="plate"/> kept for callers; text only.
        /// </summary>
        void AddTokenLabel(Transform parent, string text, float height, Color? color = null, bool bold = false,
            bool plate = false, bool startVisible = true)
        {
            if (string.IsNullOrEmpty(text) || _art == null)
                return;
            var root = new GameObject("Label");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, height, 0f);
            root.AddComponent<BillboardFace>();
            var tmp = UiKit.Label(root.transform, "Text", text, Vector3.zero, 0.4f, LabelHeight,
                color ?? new Color(0.85f, 0.98f, 1f, 1f));
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.outlineWidth = 0.18f;
            tmp.outlineColor = new Color32(2, 10, 16, 220);
            root.SetActive(startVisible);
        }

        // ── Mesh helpers ──────────────────────────────────────────────────────────

        GameObject Sphere(Transform parent, string name, float radius, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * (radius * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = SphereMesh.Smooth;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        GameObject FlatRing(Transform parent, string name, float radius, float width, Color tint, float glow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _ringMesh ??= Annulus(1f, 0.08f, 48);
            go.transform.localScale = new Vector3(radius, 1f, radius);
            go.AddComponent<MeshFilter>().sharedMesh = width / radius > 0.12f ? Annulus(1f, width / radius, 48) : _ringMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _art.Lit(Texture2D.whiteTexture, tint, glow);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Flat ring in the XZ plane, double-sided.</summary>
        static Mesh Annulus(float radius, float width, int segments)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var inner = radius - width * 0.5f;
            var outer = radius + width * 0.5f;
            for (var i = 0; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(d * inner);
                verts.Add(d * outer);
            }

            for (var i = 0; i < segments; i++)
            {
                var b = i * 2;
                tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
                tris.AddRange(new[] { b, b + 3, b + 1, b, b + 2, b + 3 });
            }

            var m = new Mesh { name = "Annulus" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Open truncated cone (no caps), bottom radius at y 0, top radius at y height.</summary>
        static Mesh Cone(float bottom, float top, float height, int segments)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var u = i / (float)segments;
                var a = u * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(d * bottom);
                verts.Add(d * top + Vector3.up * height);
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
            }

            for (var i = 0; i < segments; i++)
            {
                var b = i * 2;
                tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }

            var m = new Mesh { name = "ProjectionCone" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
