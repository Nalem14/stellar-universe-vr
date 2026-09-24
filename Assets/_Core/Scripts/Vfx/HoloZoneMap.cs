using System;
using System.Collections.Generic;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Strategic zone map on the CIC holo table — system disc, orbits, planet/fleet tokens.
    /// Uses WorldScale.Holo* only (never OrbitBase / world radii).
    /// </summary>
    public class HoloZoneMap : MonoBehaviour
    {
        FocusContext _focus;
        CicArtKit _art;
        Transform _root;
        Transform _volume;
        TMP_Text _readout;
        readonly List<GameObject> _tokenRoots = new();
        readonly List<HoloToken> _tokens = new();
        int _builtForSystem = -1;
        bool _galaxyStub;
        bool _interactionLock;
        bool _rebuildPending;

        public Transform VolumeRoot => _volume;
        public TMP_Text Readout => _readout;
        public IReadOnlyList<HoloToken> Tokens => _tokens;
        public event Action TokensRebuilt;

        /// <summary>Hold Focus/diplomacy rebuilds while the player is grabbing a fleet token.</summary>
        public void SetInteractionLock(bool locked)
        {
            _interactionLock = locked;
            if (!locked && _rebuildPending)
            {
                _rebuildPending = false;
                Rebuild();
            }
        }

        public void Bind(FocusContext focus, CicArtKit art)
        {
            _focus = focus;
            _art = art;
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.Changed += OnFocusChanged;
                _focus.FleetsChanged -= OnFocusChanged;
                _focus.FleetsChanged += OnFocusChanged;
            }

            DiplomacyIndex.Changed -= OnDiplomacyChanged;
            DiplomacyIndex.Changed += OnDiplomacyChanged;

            Rebuild();
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.FleetsChanged -= OnFocusChanged;
            }
            DiplomacyIndex.Changed -= OnDiplomacyChanged;
        }

        void OnFocusChanged()
        {
            if (_interactionLock)
            {
                _rebuildPending = true;
                return;
            }

            Rebuild();
        }

        void OnDiplomacyChanged()
        {
            if (_interactionLock)
            {
                _rebuildPending = true;
                return;
            }

            Rebuild();
        }

        public void EnsureScaffold(Transform tableTop, CicArtKit art)
        {
            _art = art;
            if (_root != null)
                return;

            _root = new GameObject("HoloZoneMap").transform;
            _root.SetParent(tableTop, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;

            // Soft volume column — depth cue only; keep invisible (additive wash drowned tokens).
            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "HoloVolume";
            col.transform.SetParent(_root, false);
            col.transform.localPosition = new Vector3(0f, WorldScale.HoloVolumeHeight * 0.5f, 0f);
            col.transform.localScale = new Vector3(
                WorldScale.HoloDiscRadius * 1.02f,
                WorldScale.HoloVolumeHeight * 0.5f,
                WorldScale.HoloDiscRadius * 1.02f);
            DropCollider(col);
            var volRend = col.GetComponent<MeshRenderer>();
            if (volRend != null)
                volRend.enabled = false;
            _volume = col.transform;

            // Main plate — flat quad with radar texture (not a fat cylinder).
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "OrbitPlate";
            plate.transform.SetParent(_root, false);
            plate.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            plate.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            plate.transform.localScale = Vector3.one * (WorldScale.HoloDiscRadius * 2f);
            DropCollider(plate);
            plate.GetComponent<MeshRenderer>().sharedMaterial = art.HoloDetail(
                art.OrbitPlate != null ? art.OrbitPlate : art.HoloPlate,
                new Color(0.55f, 0.85f, 1f, 0.78f), 0.12f);

            // Compass overlay (slow spin) — additive holo on top.
            var compass = GameObject.CreatePrimitive(PrimitiveType.Quad);
            compass.name = "CompassRose";
            compass.transform.SetParent(_root, false);
            compass.transform.localPosition = new Vector3(0f, 0.014f, 0f);
            compass.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            compass.transform.localScale = Vector3.one * (WorldScale.HoloDiscRadius * 2.05f);
            DropCollider(compass);
            compass.GetComponent<MeshRenderer>().sharedMaterial = art.Holo(
                art.CompassRose != null ? art.CompassRose : Texture2D.whiteTexture,
                new Color(0.4f, 0.95f, 1f, 0.22f));
            var cspin = compass.AddComponent<HoloSpin>();
            cspin.DegreesPerSecond = 3f;
            cspin.BobMeters = 0f;

            // Outer rim ring (thin).
            PlaceRingVisual("ZoneRim", WorldScale.HoloDiscRadius * 0.99f, 0.016f,
                new Color(0.35f, 0.9f, 1f, 0.7f), permanent: true);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "ProjectorGlow";
            glow.transform.SetParent(tableTop, false);
            glow.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localScale = new Vector3(1.1f, 1.1f, 1f);
            DropCollider(glow);
            glow.GetComponent<MeshRenderer>().sharedMaterial = art.Holo(
                art.ProjectorGlow != null ? art.ProjectorGlow : Texture2D.whiteTexture,
                new Color(0.25f, 0.9f, 1f, 0.55f));

            // Compact readout on the near rim of the disc — not a floating mid-air billboard.
            var canvas = DiegeticUi.WorldCanvas(_root, "HoloReadoutCanvas", new Vector2(520f, 56f),
                new Vector3(0f, 0.07f, -WorldScale.HoloDiscRadius * 0.72f),
                Quaternion.Euler(28f, 0f, 0f), 0.0007f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, new Vector2(500f, 48f));
            var frameImg = frame.GetComponent<UnityEngine.UI.Image>();
            if (frameImg != null && DiegeticUi.SprReadout != null)
            {
                frameImg.sprite = DiegeticUi.SprReadout;
                frameImg.type = UnityEngine.UI.Image.Type.Sliced;
            }

            _readout = DiegeticUi.HoloLabel(frame, Trans.Get("CommandBridge"), Vector2.zero,
                new Vector2(470f, 36f), 18f, DiegeticUi.Cyan);
        }

        void PlaceRingVisual(string name, float radius, float y, Color tint, bool permanent)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = name;
            ring.transform.SetParent(_root, false);
            ring.transform.localPosition = new Vector3(0f, y, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // OrbitRing texture is an annulus near the edge — scale so the ring sits at `radius`.
            ring.transform.localScale = Vector3.one * (radius * 2f / 0.9f);
            DropCollider(ring);
            if (_art != null)
                ring.GetComponent<MeshRenderer>().sharedMaterial = _art.HoloDetail(
                    _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture,
                    tint, 0.35f);
            if (permanent)
                return;
            _tokenRoots.Add(ring);
        }

        public void SetReadout(string text)
        {
            if (_readout != null)
                _readout.text = text;
        }

        void Rebuild()
        {
            if (_root == null || _art == null)
                return;

            ClearTokens();
            var focus = _focus ?? FocusContext.Current;
            if (focus != null && focus.HasSystem)
            {
                _builtForSystem = focus.SystemId;
                _galaxyStub = false;
                BuildFromFocus(focus);
                var label = !string.IsNullOrEmpty(focus.SystemName)
                    ? focus.SystemName
                    : focus.SystemId.ToString();
                SetReadout($"{Trans.Get("CommandBridge")} · {label}");
            }
            else
            {
                var seed = focus != null && focus.SystemId > 0 ? focus.SystemId : 7;
                _builtForSystem = seed;
                _galaxyStub = true;
                BuildSynthetic(seed);
                SetReadout(Trans.Get("Loading"));
            }

            TokensRebuilt?.Invoke();
        }

        void BuildFromFocus(FocusContext focus)
        {
            PlaceStar(focus.SystemType);
            PlaceOrbitRings(focus);

            foreach (var planet in focus.Planets)
            {
                var stance = DiplomacyIndex.Resolve(planet.UserId);
                var color = DiplomacyIndex.Tint(stance);
                var pname = string.IsNullOrEmpty(planet.Name) ? "planet" : planet.Name;
                var station = focus.ViewPlanetId == planet.Id && focus.ViewFleetId <= 0;
                PlacePlanet(planet.Slot, planet.Id, color, WorldScale.HoloPlanetTokenRadius(planet.Slot),
                    pname, station);
            }

            foreach (var rock in focus.Asteroids)
                PlaceAsteroid(rock.Slot, rock.Id);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var viewId = focus.ViewFleetId;
            foreach (var fleet in focus.Fleets)
            {
                if (!fleet.VisibleIn(focus.SystemId, now))
                    continue;
                var stance = DiplomacyIndex.ResolveFleet(fleet);
                var color = DiplomacyIndex.Tint(stance);
                var owned = stance == EmpireStance.Owned;
                var slot = ResolveFleetSlot(fleet, focus);
                var busy = !fleet.CanIssueMove(now);
                var fname = string.IsNullOrEmpty(fleet.Name) ? "ship" : fleet.Name;
                var active = viewId > 0 && fleet.Id == viewId;
                PlaceFleet(slot, fleet.Id, color, fleet.Id * 17, owned, busy, fname, active, stance);
            }

            PlaceGalaxyStubRing();
        }

        void BuildSynthetic(int systemId)
        {
            PlaceStar(systemId % 5);
            var planetCount = 3 + systemId % 4;
            for (var i = 0; i < planetCount; i++)
            {
                var slot = i + 1;
                PlaceOrbitRing(slot);
                var tone = (systemId + i) % 3;
                var color = tone == 0 ? CicArtKit.Cyan :
                    tone == 1 ? new Color(0.45f, 0.7f, 0.85f, 1f) : CicArtKit.Amber;
                PlacePlanet(slot, i + 1, color, WorldScale.HoloPlanetTokenRadius(slot), "planet", false);
            }

            for (var i = 0; i < 2 + systemId % 3; i++)
                PlaceAsteroid(1 + (i * 2 + systemId) % 6, 100 + i);

            for (var i = 0; i < 2; i++)
            {
                var stance = i == 0 ? EmpireStance.Owned : EmpireStance.Enemy;
                var color = DiplomacyIndex.Tint(stance);
                PlaceFleet(1 + i * 2, 200 + i, color, systemId * 31 + i * 47, owned: i == 0, busy: false,
                    displayName: "ship", active: i == 0, stance: stance);
            }

            PlaceGalaxyStubRing();
        }

        public async void ShowGalaxyAsync()
        {
            await GalaxyCatalog.EnsureLoaded();
            if (_root == null || _art == null)
                return;
            ClearTokens();
            _galaxyStub = true;
            BuildGalaxyMap();
            SetReadout(Trans.Get("galaxy"));
            TokensRebuilt?.Invoke();
        }

        public void ShowSystemMap()
        {
            Rebuild();
        }

        void BuildGalaxyMap()
        {
            var stars = GalaxyCatalog.All;
            if (stars.Count == 0)
            {
                PlaceGalaxyStubRing();
                return;
            }

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var s in stars)
            {
                minX = Mathf.Min(minX, s.X);
                maxX = Mathf.Max(maxX, s.X);
                minY = Mathf.Min(minY, s.Y);
                maxY = Mathf.Max(maxY, s.Y);
            }

            var dx = Mathf.Max(1f, maxX - minX);
            var dy = Mathf.Max(1f, maxY - minY);
            var scale = WorldScale.HoloDiscRadius * 0.85f / Mathf.Max(dx, dy) * 2f;
            var cx = (minX + maxX) * 0.5f;
            var cy = (minY + maxY) * 0.5f;
            var count = 0;
            foreach (var s in stars)
            {
                if (count++ > 120)
                    break;
                var lx = (s.X - cx) * scale;
                var lz = (s.Y - cy) * scale;
                PlaceSystemToken(s.Id, s.X, s.Y, new Vector3(lx, WorldScale.HoloTokenLift, lz));
            }
        }

        void PlaceSystemToken(int id, float gx, float gy, Vector3 localPos)
        {
            var go = TokenVisual("TokenSystem_" + id, PrimitiveType.Sphere, localPos,
                Vector3.one * 0.035f,
                _art.RadarIcon(_art.TokenSystem != null ? _art.TokenSystem : Texture2D.whiteTexture,
                    new Color(0.85f, 0.95f, 1f, 0.95f)),
                keepCollider: true);
            var token = go.GetComponent<HoloToken>();
            if (token == null)
                token = go.AddComponent<HoloToken>();
            token.Kind = HoloTokenKind.System;
            token.Id = id;
            token.Slot = 0;
            token.Owned = false;
            token.Busy = false;
            token.GalaxyX = gx;
            token.GalaxyY = gy;
            var name = GalaxyCatalog.TryGet(id, out var star) && !string.IsNullOrEmpty(star.Name)
                ? star.Name
                : null;
            token.DisplayName = FormatEntityLabel(name, id);
            token.CaptureHome();
            AddTokenLabel(go.transform, token.DisplayName, 0.04f);
            _tokens.Add(token);
        }

        void PlaceStar(int typeHint)
        {
            var tint = typeHint % 3 == 0
                ? new Color(1f, 0.92f, 0.55f, 0.95f)
                : typeHint % 3 == 1
                    ? new Color(1f, 0.7f, 0.45f, 0.95f)
                    : new Color(0.75f, 0.9f, 1f, 0.95f);
            var star = TokenVisual("TokenStar", PrimitiveType.Quad,
                new Vector3(0f, WorldScale.HoloTokenLift + 0.04f, 0f),
                Vector3.one * (WorldScale.HoloStarRadius * 2.6f),
                _art.RadarIcon(_art.TokenStar != null ? _art.TokenStar : _art.TokenSystem, tint),
                keepCollider: true);
            star.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            PlaceRingVisual("StarCorona", WorldScale.HoloStarRadius * 1.7f, 0.018f,
                new Color(tint.r, tint.g, tint.b, 0.5f), permanent: false);
            var spin = star.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 22f;
            spin.BobMeters = 0.012f;

            var focus = _focus ?? FocusContext.Current;
            var token = star.GetComponent<HoloToken>();
            if (token == null)
                token = star.AddComponent<HoloToken>();
            token.Kind = HoloTokenKind.System;
            token.Id = focus != null ? focus.SystemId : 0;
            token.Owned = false;
            token.Busy = false;
            if (focus != null && GalaxyCatalog.TryGet(focus.SystemId, out var here))
            {
                token.GalaxyX = here.X;
                token.GalaxyY = here.Y;
            }

            var label = focus != null && !string.IsNullOrEmpty(focus.SystemName)
                ? focus.SystemName
                : "star";
            if (focus != null && focus.SystemId > 0)
                label = FormatEntityLabel(label, focus.SystemId);
            token.DisplayName = label;
            token.CaptureHome();
            AddTokenLabel(star.transform, label, WorldScale.HoloStarRadius + 0.06f,
                new Color(1f, 0.95f, 0.7f, 0.98f), bold: true);
            _tokens.Add(token);
        }

        void PlaceOrbitRings(FocusContext focus)
        {
            var seen = new HashSet<int>();
            foreach (var p in focus.Planets)
            {
                if (p.Slot <= 0 || !seen.Add(p.Slot))
                    continue;
                PlaceOrbitRing(p.Slot);
            }

            if (seen.Count == 0)
            {
                for (var s = 1; s <= 4; s++)
                    PlaceOrbitRing(s);
            }
        }

        void PlaceOrbitRing(int slot)
        {
            var r = WorldScale.HoloOrbitRadius(slot);
            PlaceRingVisual("Orbit_" + slot, r, 0.015f + slot * 0.0008f,
                new Color(0.3f, 0.9f, 1f, 0.55f), permanent: false);
        }

        void PlacePlanet(int slot, int id, Color color, float radius, string displayName, bool stationView)
        {
            var r = WorldScale.HoloOrbitRadius(Mathf.Max(1, slot));
            var ang = StableAngle(id * 97 + slot * 13);
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift + 0.01f, Mathf.Sin(ang) * r);
            var go = new GameObject("TokenPlanet_" + id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            _tokenRoots.Add(go);

            var glyph = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glyph.name = "PlanetGlyph";
            glyph.transform.SetParent(go.transform, false);
            glyph.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            glyph.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glyph.transform.localScale = Vector3.one * (radius * 2.4f);
            DropCollider(glyph);
            glyph.GetComponent<MeshRenderer>().sharedMaterial = _art.RadarIcon(
                _art.TokenPlanet != null ? _art.TokenPlanet : Texture2D.whiteTexture,
                new Color(color.r, color.g, color.b, stationView ? 1f : 0.95f));

            var band = GameObject.CreatePrimitive(PrimitiveType.Quad);
            band.name = "Band";
            band.transform.SetParent(go.transform, false);
            band.transform.localPosition = new Vector3(0f, 0.013f, 0f);
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            band.transform.localScale = Vector3.one * (radius * 3.2f);
            DropCollider(band);
            band.GetComponent<MeshRenderer>().sharedMaterial = _art.RadarIcon(
                _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture,
                new Color(color.r, color.g, color.b, stationView ? 0.75f : 0.55f));

            if (stationView)
            {
                AddActiveHalo(go.transform, radius * 3.6f, CicArtKit.Amber);
                AddOrbitalStationMarker(go.transform, radius, ang);
            }

            var col = go.AddComponent<SphereCollider>();
            col.radius = radius * 1.6f;
            col.center = new Vector3(0f, 0.04f, 0f);
            col.isTrigger = true;

            var spin = go.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = stationView ? 14f : 8f + slot;
            spin.BobMeters = stationView ? 0.01f : 0.006f;

            var label = stationView
                ? Trans.Get("CommandBridge") + "  ·  " + FormatEntityLabel(displayName, id)
                : FormatEntityLabel(displayName, id);
            AddTokenLabel(go.transform, label, radius + (stationView ? 0.14f : 0.1f),
                stationView ? CicArtKit.Amber : new Color(0.8f, 0.95f, 1f, 0.98f), bold: stationView,
                plate: true, startVisible: stationView);
            Tag(go, HoloTokenKind.Planet, id, slot, owned: false, busy: false, label);
        }

        /// <summary>
        /// Fake orbital station pip beside the inhabited planet — “you are here” when ViewPlanetId.
        /// </summary>
        void AddOrbitalStationMarker(Transform planetRoot, float planetRadius, float orbitAng)
        {
            var station = new GameObject("OrbitalStation");
            station.transform.SetParent(planetRoot, false);
            // Offset along orbit tangent so it reads as a dock beside the world, not on top of it.
            var tangential = new Vector3(-Mathf.Sin(orbitAng), 0f, Mathf.Cos(orbitAng));
            var radial = new Vector3(Mathf.Cos(orbitAng), 0f, Mathf.Sin(orbitAng));
            var offset = tangential * (planetRadius * 1.55f) + radial * (planetRadius * 0.15f);
            station.transform.localPosition = new Vector3(offset.x, 0.06f, offset.z);

            var s = Mathf.Max(WorldScale.HoloFleetSize * 1.35f, planetRadius * 0.55f);

            var pad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pad.name = "StationPad";
            pad.transform.SetParent(station.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            pad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pad.transform.localScale = Vector3.one * (s * 4.8f);
            DropCollider(pad);
            var padTex = _art.TokenActive != null ? _art.TokenActive :
                (_art.TokenPad != null ? _art.TokenPad : Texture2D.whiteTexture);
            pad.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(padTex, new Color(1f, 0.78f, 0.25f, 1f));
            var padSpin = pad.AddComponent<HoloSpin>();
            padSpin.DegreesPerSecond = 55f;
            padSpin.BobMeters = 0f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Quad);
            body.name = "StationGlyph";
            body.transform.SetParent(station.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(s * 2.1f, s * 1.4f, 1f);
            DropCollider(body);
            // Diamond-ish read via TokenActive / pad art — amber station vs cyan ship.
            var bodyTex = _art.TokenActive != null ? _art.TokenActive : Texture2D.whiteTexture;
            body.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(bodyTex, new Color(1f, 0.92f, 0.55f, 1f));

            var link = GameObject.CreatePrimitive(PrimitiveType.Quad);
            link.name = "DockLink";
            link.transform.SetParent(station.transform, false);
            link.transform.localPosition = -offset * 0.45f + new Vector3(0f, 0.03f, 0f);
            link.transform.localRotation = Quaternion.Euler(90f, 0f, Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg);
            link.transform.localScale = new Vector3(s * 0.35f, planetRadius * 1.2f, 1f);
            DropCollider(link);
            link.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(Texture2D.whiteTexture, new Color(1f, 0.75f, 0.3f, 0.55f));

            var bob = station.AddComponent<HoloSpin>();
            bob.DegreesPerSecond = 0f;
            bob.BobMeters = 0.016f;
        }

        void PlaceAsteroid(int slot, int id)
        {
            var r = WorldScale.HoloOrbitRadius(Mathf.Max(1, slot)) * 0.92f;
            var ang = StableAngle(id * 53 + 7);
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift + 0.01f, Mathf.Sin(ang) * r);
            var rockTex = _art.TokenAsteroid != null ? _art.TokenAsteroid : Texture2D.whiteTexture;
            var s = WorldScale.HoloAsteroidRadius * 2.4f;
            var go = new GameObject("TokenRock_" + id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            _tokenRoots.Add(go);

            var glyph = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glyph.name = "RockGlyph";
            glyph.transform.SetParent(go.transform, false);
            glyph.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            glyph.transform.localRotation = Quaternion.Euler(90f, id * 37f, 0f);
            glyph.transform.localScale = new Vector3(s * 1.1f, s * 1.1f, 1f);
            DropCollider(glyph);
            glyph.GetComponent<MeshRenderer>().sharedMaterial = _art.RadarIcon(rockTex,
                new Color(0.85f, 0.78f, 0.62f, 0.95f));

            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(s * 1.4f, s * 0.8f, s * 1.4f);
            col.center = new Vector3(0f, 0.05f, 0f);
            col.isTrigger = true;

            var spin = go.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 6f;
            spin.BobMeters = 0.005f;

            var label = FormatEntityLabel(Trans.Get("asteroidField"), id);
            AddTokenLabel(go.transform, label, 0.09f, new Color(0.9f, 0.86f, 0.75f, 0.95f),
                plate: false, startVisible: false);
            Tag(go, HoloTokenKind.Asteroid, id, slot, owned: false, busy: false, label);
        }

        void PlaceFleet(int slot, int id, Color color, int seed, bool owned, bool busy, string displayName,
            bool active, EmpireStance stance = EmpireStance.Unknown)
        {
            var r = WorldScale.HoloOrbitRadius(Mathf.Max(1, slot)) * 1.08f;
            var ang = StableAngle(seed);
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift + 0.01f, Mathf.Sin(ang) * r);
            var go = new GameObject("TokenFleet_" + id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            // Nose points tangential on the orbit — readable as a ship heading.
            var heading = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            go.transform.localRotation = Quaternion.LookRotation(heading, Vector3.up);
            _tokenRoots.Add(go);

            // Inhabited bridge ship (ViewFleetId) must read instantly — not "owned" in general.
            var s = WorldScale.HoloFleetSize * (active ? 1.55f : 1f);
            var shipTex = stance is EmpireStance.Enemy or EmpireStance.Pirate
                ? (_art.TokenFleetAmber != null ? _art.TokenFleetAmber : _art.TokenFleet)
                : _art.TokenFleet;
            if (shipTex == null)
                shipTex = Texture2D.whiteTexture;

            // Flat radar glyph — readable pip, never bigger than a planet token.
            var glyph = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glyph.name = "ShipGlyph";
            glyph.transform.SetParent(go.transform, false);
            glyph.transform.localPosition = new Vector3(0f, active ? 0.055f : 0.04f, 0f);
            glyph.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glyph.transform.localScale = new Vector3(s * (active ? 2.35f : 2.0f), s * (active ? 2.35f : 2.0f), 1f);
            DropCollider(glyph);
            var glyphRend = glyph.GetComponent<MeshRenderer>();
            var glyphTint = active
                ? new Color(0.35f, 1f, 1f, 1f)
                : new Color(color.r, color.g, color.b, 0.92f);
            glyphRend.sharedMaterial = _art.RadarIcon(shipTex, glyphTint);
            glyphRend.sortingOrder = active ? 28 : 20;

            // Engine glow tip (aft) — tiny pip, not a second hull.
            var eng = GameObject.CreatePrimitive(PrimitiveType.Quad);
            eng.name = "EngineGlow";
            eng.transform.SetParent(go.transform, false);
            eng.transform.localPosition = new Vector3(0f, active ? 0.056f : 0.041f, -s * 0.7f);
            eng.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            eng.transform.localScale = new Vector3(s * (active ? 0.7f : 0.45f), s * (active ? 0.4f : 0.28f), 1f);
            DropCollider(eng);
            eng.GetComponent<MeshRenderer>().sharedMaterial = EngineMatForStance(stance, busy, active);

            AddFleetPad(go.transform, s, stance, active, owned);
            if (active)
                AddInhabitedBeacon(go.transform, s);
            if (busy)
                AddBusyRing(go.transform, s);

            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(s * 3.6f, s * 2.4f, s * 3.8f);
            col.center = new Vector3(0f, s * 0.45f, 0f);
            // Non-trigger: XR rays ignore triggers (QueryTriggerInteraction.Ignore).
            col.isTrigger = false;

            // Bob only — spinning a flat radar glyph makes it unreadable.
            var spin = go.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 0f;
            spin.BobMeters = active ? 0.02f : busy ? 0.012f : 0.006f;

            var label = active
                ? Trans.Get("CommandBridge") + "  ·  " + FormatEntityLabel(displayName, id)
                : FormatEntityLabel(displayName, id);
            // Inhabited ship always labeled; other owned ships soft-label; foes on hover.
            AddTokenLabel(go.transform, label, active ? 0.12f : 0.1f,
                active ? CicArtKit.Cyan : new Color(color.r * 0.85f + 0.15f, color.g * 0.85f + 0.15f,
                    color.b * 0.85f + 0.15f, 1f),
                bold: true, plate: true, startVisible: active || owned);
            Tag(go, HoloTokenKind.Fleet, id, slot, owned, busy, label);
        }

        void AddFleetPad(Transform parent, float s, EmpireStance stance, bool active, bool owned)
        {
            var padTex = active && _art.TokenActive != null
                ? _art.TokenActive
                : stance is EmpireStance.Enemy or EmpireStance.Pirate
                    ? (_art.TokenPadEnemy != null ? _art.TokenPadEnemy : _art.TokenPad)
                    : _art.TokenPad;
            if (padTex == null)
                padTex = _art.TokenActive != null ? _art.TokenActive : Texture2D.whiteTexture;
            var tint = active ? CicArtKit.Cyan : DiplomacyIndex.Tint(stance);

            var pad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pad.name = active ? "InhabitedPad" : "HoverRing";
            pad.transform.SetParent(parent, false);
            pad.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            pad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pad.transform.localScale = Vector3.one * (s * (active ? 5.6f : owned ? 3.4f : 3.0f));
            DropCollider(pad);
            var alpha = active ? 1f : owned ? 0.65f : 0.45f;
            pad.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(padTex, new Color(tint.r, tint.g, tint.b, alpha));
            var pulse = pad.AddComponent<HoloSpin>();
            pulse.DegreesPerSecond = active ? 70f : owned ? 14f : 6f;
            pulse.BobMeters = 0f;
        }

        /// <summary>Pulsing halo + vertical pip — “you are on this ship’s bridge”.</summary>
        void AddInhabitedBeacon(Transform parent, float s)
        {
            AddActiveHalo(parent, s * 6.2f, CicArtKit.Cyan);

            var pip = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pip.name = "InhabitedPip";
            pip.transform.SetParent(parent, false);
            pip.transform.localPosition = new Vector3(0f, 0.11f, 0f);
            pip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pip.transform.localScale = new Vector3(s * 0.55f, s * 0.55f, 1f);
            DropCollider(pip);
            var tex = _art.TokenActive != null ? _art.TokenActive : Texture2D.whiteTexture;
            pip.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(tex, new Color(1f, 1f, 1f, 1f));
            var bob = pip.AddComponent<HoloSpin>();
            bob.DegreesPerSecond = 0f;
            bob.BobMeters = 0.018f;
        }

        Material EngineMatForStance(EmpireStance stance, bool busy, bool active)
        {
            if (busy)
                return _art.AmberEmit(4.5f);
            if (active)
                return _art.CyanEmit(5.2f);
            switch (stance)
            {
                case EmpireStance.Owned:
                    return _art.CyanEmit(3.8f);
                case EmpireStance.Ally:
                    return _art.Lit(Texture2D.whiteTexture, DiplomacyIndex.Tint(stance), 3.6f);
                case EmpireStance.Enemy:
                    return _art.Lit(Texture2D.whiteTexture, DiplomacyIndex.Tint(stance), 4.2f);
                case EmpireStance.Pirate:
                    return _art.AmberEmit(4.8f);
                default:
                    return _art.Lit(Texture2D.whiteTexture, DiplomacyIndex.Tint(stance), 2.4f);
            }
        }

        static string FormatEntityLabel(string name, int id)
        {
            if (string.IsNullOrEmpty(name))
                return "#" + id;
            return name + "  #" + id;
        }

        void AddActiveHalo(Transform parent, float diameter, Color tint)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "ActiveHalo";
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * diameter;
            DropCollider(ring);
            var tex = _art.TokenActive != null ? _art.TokenActive : Texture2D.whiteTexture;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(tex, new Color(tint.r, tint.g, tint.b, 0.95f));
            var pulse = ring.AddComponent<HoloSpin>();
            pulse.DegreesPerSecond = 55f;
            pulse.BobMeters = 0.006f;
        }

        void PlaceGalaxyStubRing()
        {
            if (!_galaxyStub)
                return;
            PlaceRingVisual("GalaxyLevelStub", WorldScale.HoloDiscRadius * 0.92f, 0.017f,
                new Color(0.55f, 0.45f, 1f, 0.45f), permanent: false);
        }

        static int ResolveFleetSlot(FocusFleet fleet, FocusContext focus)
        {
            if (fleet.PlanetId > 0)
            {
                var p = focus.FindPlanet(fleet.PlanetId);
                if (p != null && p.Slot > 0)
                    return p.Slot;
            }

            if (fleet.AsteroidId > 0)
            {
                var a = focus.FindAsteroid(fleet.AsteroidId);
                if (a != null && a.Slot > 0)
                    return a.Slot;
            }

            return 1 + (fleet.Id % 5);
        }

        GameObject TokenVisual(string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat,
            bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_root, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            // Quads ship MeshColliders that cannot be triggers — always swap to a BoxCollider trigger
            // so rays don't collide with the star/plate mid-drag.
            DropCollider(go);
            if (keepCollider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = Vector3.zero;
                box.size = Vector3.one;
            }

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            _tokenRoots.Add(go);
            return go;
        }

        void Tag(GameObject go, HoloTokenKind kind, int id, int slot, bool owned, bool busy,
            string displayName = null)
        {
            var marker = go.GetComponent<HoloToken>();
            if (marker == null)
                marker = go.AddComponent<HoloToken>();
            marker.Kind = kind;
            marker.Id = id;
            marker.Slot = slot;
            marker.Owned = owned;
            marker.Busy = busy;
            marker.DisplayName = displayName ?? string.Empty;
            marker.CaptureHome();
            _tokens.Add(marker);
        }

        void AddTokenLabel(Transform parent, string text, float height, Color? color = null, bool bold = false,
            bool plate = false, bool startVisible = true)
        {
            if (string.IsNullOrEmpty(text) || _art == null)
                return;

            var root = new GameObject("Label");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, height, 0f);
            root.transform.localRotation = Quaternion.identity;
            root.AddComponent<BillboardFace>();

            var canvas = DiegeticUi.WorldCanvas(root.transform, "LabelCanvas", new Vector2(280f, 48f),
                Vector3.zero, Quaternion.identity, 0.00055f);
            var ray = canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            if (ray != null)
                ray.enabled = false;
            var gr = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (gr != null)
                gr.enabled = false;

            if (plate)
            {
                var frame = DiegeticUi.HoloFrame(canvas.transform, new Vector2(260f, 40f));
                var img = frame.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    if (DiegeticUi.SprReadout != null)
                    {
                        img.sprite = DiegeticUi.SprReadout;
                        img.type = UnityEngine.UI.Image.Type.Sliced;
                    }

                    img.color = new Color(0.06f, 0.18f, 0.24f, 0.92f);
                    img.raycastTarget = false;
                }

                var tmp = DiegeticUi.HoloLabel(frame, text, Vector2.zero, new Vector2(240f, 32f),
                    bold ? 16f : 14f, color ?? DiegeticUi.Cyan);
                if (tmp != null)
                {
                    tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                    tmp.raycastTarget = false;
                }
            }
            else
            {
                var tmp = DiegeticUi.HoloLabel(canvas.transform, text, Vector2.zero, new Vector2(260f, 36f),
                    bold ? 15f : 13f, color ?? new Color(0.85f, 0.98f, 1f, 1f));
                if (tmp != null)
                {
                    tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                    tmp.raycastTarget = false;
                }
            }

            root.SetActive(startVisible);
        }

        public static void SetTokenLabelVisible(HoloToken token, bool visible)
        {
            if (token == null)
                return;
            var label = token.transform.Find("Label");
            if (label != null)
                label.gameObject.SetActive(visible);
        }

        void AddBusyRing(Transform parent, float s)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Busy";
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, s * 0.55f, 0f);
            ring.transform.localScale = new Vector3(s * 1.4f, 0.004f, s * 1.4f);
            DropCollider(ring);
            ring.GetComponent<MeshRenderer>().sharedMaterial = _art.AmberEmit(3.8f);
        }

        void AddStem(Transform parent, float tokenY)
        {
            var h = Mathf.Max(0.02f, tokenY);
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            stem.transform.SetParent(parent, false);
            stem.transform.localPosition = new Vector3(0f, -h * 0.5f, 0f);
            stem.transform.localScale = new Vector3(0.008f, h * 0.5f, 0.008f);
            DropCollider(stem);
            stem.GetComponent<MeshRenderer>().sharedMaterial =
                _art.Holo(Texture2D.whiteTexture, new Color(0.3f, 0.85f, 1f, 0.35f));
        }

        void ClearTokens()
        {
            for (var i = 0; i < _tokenRoots.Count; i++)
            {
                if (_tokenRoots[i] == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(_tokenRoots[i]);
                else
                    DestroyImmediate(_tokenRoots[i]);
            }

            _tokenRoots.Clear();
            _tokens.Clear();
        }

        static float StableAngle(int seed)
        {
            unchecked
            {
                var x = (uint)seed * 2654435761u;
                return (x % 6283) / 1000f;
            }
        }

        static void DropCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null)
                return;
            if (Application.isPlaying)
                Destroy(col);
            else
                DestroyImmediate(col);
        }
    }
}
