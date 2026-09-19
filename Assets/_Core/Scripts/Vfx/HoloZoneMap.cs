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

        public Transform VolumeRoot => _volume;
        public TMP_Text Readout => _readout;
        public IReadOnlyList<HoloToken> Tokens => _tokens;
        public event Action TokensRebuilt;

        public void Bind(FocusContext focus, CicArtKit art)
        {
            _focus = focus;
            _art = art;
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.Changed += OnFocusChanged;
            }

            DiplomacyIndex.Changed -= OnDiplomacyChanged;
            DiplomacyIndex.Changed += OnDiplomacyChanged;

            Rebuild();
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
            DiplomacyIndex.Changed -= OnDiplomacyChanged;
        }

        void OnFocusChanged() => Rebuild();
        void OnDiplomacyChanged() => Rebuild();

        public void EnsureScaffold(Transform tableTop, CicArtKit art)
        {
            _art = art;
            if (_root != null)
                return;

            _root = new GameObject("HoloZoneMap").transform;
            _root.SetParent(tableTop, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;

            // Soft volume column (very transparent).
            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "HoloVolume";
            col.transform.SetParent(_root, false);
            col.transform.localPosition = new Vector3(0f, WorldScale.HoloVolumeHeight * 0.5f, 0f);
            col.transform.localScale = new Vector3(
                WorldScale.HoloDiscRadius * 1.02f,
                WorldScale.HoloVolumeHeight * 0.5f,
                WorldScale.HoloDiscRadius * 1.02f);
            DropCollider(col);
            col.GetComponent<MeshRenderer>().sharedMaterial = art.Holo(
                art.ProjectorGlow != null ? art.ProjectorGlow : Texture2D.whiteTexture,
                new Color(0.15f, 0.75f, 1f, 0.08f));
            var spin = col.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = -4f;
            spin.BobMeters = 0.01f;
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
                new Color(0.75f, 0.95f, 1f, 0.92f), 0.18f);

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
                new Color(0.4f, 0.95f, 1f, 0.35f));
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

            // World Space readout bar (Bridge Crew style).
            var canvas = DiegeticUi.WorldCanvas(_root, "HoloReadoutCanvas", new Vector2(720f, 72f),
                new Vector3(0f, WorldScale.HoloVolumeHeight + 0.06f, 0f),
                Quaternion.Euler(18f, 0f, 0f), 0.0012f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, new Vector2(700f, 64f));
            var frameImg = frame.GetComponent<UnityEngine.UI.Image>();
            if (frameImg != null && DiegeticUi.SprReadout != null)
            {
                frameImg.sprite = DiegeticUi.SprReadout;
                frameImg.type = UnityEngine.UI.Image.Type.Sliced;
            }

            _readout = DiegeticUi.HoloLabel(frame, Trans.Get("Loading"), Vector2.zero,
                new Vector2(660f, 48f), 22f, DiegeticUi.Cyan);
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
            SetReadout("Galaxy");
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
                _art.Holo(_art.TokenSystem != null ? _art.TokenSystem : Texture2D.whiteTexture,
                    new Color(0.85f, 0.95f, 1f, 0.9f)),
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
                _art.Holo(_art.TokenStar != null ? _art.TokenStar : _art.TokenSystem, tint),
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
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift + radius, Mathf.Sin(ang) * r);
            var go = TokenVisual("TokenPlanet_" + id, PrimitiveType.Sphere, pos, Vector3.one * (radius * 2f),
                _art.Holo(_art.TokenPlanet != null ? _art.TokenPlanet : Texture2D.whiteTexture,
                    new Color(color.r, color.g, color.b, 0.85f)),
                keepCollider: true);
            var spin = go.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 8f + slot;
            spin.BobMeters = 0.006f;
            // Thin equatorial ring — planet silhouette cue (not asteroid cube / not fleet chevron).
            var band = GameObject.CreatePrimitive(PrimitiveType.Quad);
            band.name = "Band";
            band.transform.SetParent(go.transform, false);
            band.transform.localPosition = Vector3.zero;
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            band.transform.localScale = Vector3.one * 1.55f;
            DropCollider(band);
            band.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(
                _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture,
                new Color(color.r, color.g, color.b, 0.75f));
            if (stationView)
                AddActiveHalo(go.transform, radius * 2.4f, CicArtKit.Amber);
            AddStem(go.transform, pos.y);
            var label = FormatEntityLabel(displayName, id);
            AddTokenLabel(go.transform, label, radius + 0.05f,
                stationView ? CicArtKit.Amber : new Color(0.8f, 0.95f, 1f, 0.98f), bold: stationView);
            Tag(go, HoloTokenKind.Planet, id, slot, owned: false, busy: false, label);
        }

        void PlaceAsteroid(int slot, int id)
        {
            var r = WorldScale.HoloOrbitRadius(Mathf.Max(1, slot)) * 0.92f;
            var ang = StableAngle(id * 53 + 7);
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift + 0.01f, Mathf.Sin(ang) * r);
            var rockTex = _art.TokenAsteroid != null ? _art.TokenAsteroid : Texture2D.whiteTexture;
            var go = TokenVisual("TokenRock_" + id, PrimitiveType.Cube, pos,
                new Vector3(WorldScale.HoloAsteroidRadius * 2.2f, WorldScale.HoloAsteroidRadius * 1.6f,
                    WorldScale.HoloAsteroidRadius * 2.8f),
                _art.Lit(rockTex, new Color(0.72f, 0.7f, 0.66f), 0.55f),
                keepCollider: true);
            go.transform.localRotation = Quaternion.Euler(22f + id % 40, id * 37f, 11f);
            // Second shard — jagged rock silhouette vs smooth planet sphere.
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Shard";
            shard.transform.SetParent(go.transform, false);
            shard.transform.localPosition = new Vector3(0.35f, 0.2f, -0.15f);
            shard.transform.localRotation = Quaternion.Euler(35f, 50f, 10f);
            shard.transform.localScale = new Vector3(0.55f, 0.4f, 0.7f);
            DropCollider(shard);
            shard.GetComponent<MeshRenderer>().sharedMaterial =
                _art.Lit(rockTex, new Color(0.55f, 0.52f, 0.48f), 0.35f);
            AddStem(go.transform, pos.y);
            var label = FormatEntityLabel(Trans.Get("asteroid"), id);
            AddTokenLabel(go.transform, label, 0.055f, new Color(0.85f, 0.82f, 0.75f, 0.95f));
            Tag(go, HoloTokenKind.Asteroid, id, slot, owned: false, busy: false, label);
        }

        void PlaceFleet(int slot, int id, Color color, int seed, bool owned, bool busy, string displayName,
            bool active, EmpireStance stance = EmpireStance.Unknown)
        {
            var r = WorldScale.HoloOrbitRadius(Mathf.Max(1, slot)) * 1.08f;
            var ang = StableAngle(seed);
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift + 0.02f, Mathf.Sin(ang) * r);
            var go = new GameObject("TokenFleet_" + id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.LookRotation(
                new Vector3(Mathf.Cos(ang + 1.2f), 0f, Mathf.Sin(ang + 1.2f)), Vector3.up);
            _tokenRoots.Add(go);

            var s = WorldScale.HoloFleetSize * (active ? 1.35f : 1f);
            var billboard = GameObject.CreatePrimitive(PrimitiveType.Quad);
            billboard.name = "Chevron";
            billboard.transform.SetParent(go.transform, false);
            billboard.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            billboard.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            billboard.transform.localScale = new Vector3(s * 1.6f, s * 1.6f, 1f);
            DropCollider(billboard);
            billboard.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(
                _art.TokenFleet != null ? _art.TokenFleet : Texture2D.whiteTexture,
                new Color(color.r, color.g, color.b, active ? 1f : 0.95f));

            AddBox(go.transform, "Hull", new Vector3(0f, 0f, 0.01f), new Vector3(s * 0.35f, s * 0.14f, s * 0.9f),
                _art.Holo(Texture2D.whiteTexture, new Color(color.r, color.g, color.b, 0.55f)));
            AddBox(go.transform, "Engine", new Vector3(0f, 0f, -s * 0.5f),
                new Vector3(s * 0.14f, s * 0.14f, s * 0.16f),
                EngineMatForStance(stance, busy, active));
            AddStem(go.transform, pos.y);
            if (busy)
                AddBusyRing(go.transform, s);
            if (active)
                AddActiveHalo(go.transform, s * 2.8f, CicArtKit.Cyan);
            else if (owned)
                AddGrabHalo(go.transform, s);
            else
                AddStanceAura(go.transform, s, stance);

            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(s * 3.6f, s * 2.4f, s * 3.8f);
            col.center = Vector3.zero;

            var spin = go.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = StanceSpin(stance, active);
            spin.BobMeters = active ? 0.022f : busy ? 0.018f : StanceBob(stance);
            var label = FormatEntityLabel(displayName, id);
            var labelColor = active ? CicArtKit.Cyan : new Color(color.r, color.g, color.b, 0.98f);
            AddTokenLabel(go.transform, label, s * 1.15f, labelColor, bold: active || stance == EmpireStance.Enemy);
            Tag(go, HoloTokenKind.Fleet, id, slot, owned, busy, label);
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

        static float StanceSpin(EmpireStance stance, bool active)
        {
            if (active)
                return 12f;
            switch (stance)
            {
                case EmpireStance.Enemy:
                    return 28f;
                case EmpireStance.Pirate:
                    return 42f;
                case EmpireStance.Ally:
                    return 8f;
                default:
                    return 0f;
            }
        }

        static float StanceBob(EmpireStance stance)
        {
            switch (stance)
            {
                case EmpireStance.Pirate:
                    return 0.016f;
                case EmpireStance.Enemy:
                    return 0.012f;
                default:
                    return 0.01f;
            }
        }

        void AddStanceAura(Transform parent, float s, EmpireStance stance)
        {
            if (stance == EmpireStance.Owned || stance == EmpireStance.Unknown)
                return;
            var tint = DiplomacyIndex.Tint(stance);
            var diameter = s * (stance == EmpireStance.Enemy || stance == EmpireStance.Pirate ? 2.6f : 2.2f);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "StanceAura_" + stance;
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, -0.005f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * diameter;
            DropCollider(ring);
            var tex = _art.TokenActive != null ? _art.TokenActive : Texture2D.whiteTexture;
            var alpha = stance == EmpireStance.Neutral ? 0.45f : 0.85f;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                _art.Holo(tex, new Color(tint.r, tint.g, tint.b, alpha));
            var pulse = ring.AddComponent<HoloSpin>();
            pulse.DegreesPerSecond = stance == EmpireStance.Pirate ? 70f :
                stance == EmpireStance.Enemy ? 48f :
                stance == EmpireStance.Ally ? 18f : 10f;
            pulse.BobMeters = stance == EmpireStance.Enemy || stance == EmpireStance.Pirate ? 0.008f : 0.003f;
        }

        static string FormatEntityLabel(string name, int id)
        {
            if (string.IsNullOrEmpty(name))
                return "#" + id;
            return name + "\n#" + id;
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
                _art.Holo(tex, new Color(tint.r, tint.g, tint.b, 0.95f));
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
            if (!keepCollider)
                DropCollider(go);
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

        void AddTokenLabel(Transform parent, string text, float height, Color? color = null, bool bold = false)
        {
            if (string.IsNullOrEmpty(text) || _art == null)
                return;
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            go.transform.localScale = Vector3.one * (bold ? 0.014f : 0.012f);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.fontSize = bold ? 7.5f : 5.5f;
            tmp.color = color ?? new Color(0.75f, 0.95f, 1f, 0.95f);
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.text = text;
            tmp.raycastTarget = false;
            tmp.rectTransform.sizeDelta = new Vector2(36f, 12f);
        }

        void AddGrabHalo(Transform parent, float s)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "GrabHalo";
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, -s * 0.1f, 0f);
            ring.transform.localScale = new Vector3(s * 2.4f, 0.004f, s * 2.4f);
            DropCollider(ring);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                _art.Holo(Texture2D.whiteTexture, new Color(0.15f, 1f, 0.95f, 0.55f));
            var pulse = ring.AddComponent<HoloSpin>();
            pulse.DegreesPerSecond = 35f;
            pulse.BobMeters = 0.004f;

            var tip = DiegeticUi.Label(parent, "GrabHint", Trans.Get("CommandBridge"),
                new Vector3(0f, s * 1.6f, 0f), 0.012f, 4f, CicArtKit.Cyan);
            tip.rectTransform.sizeDelta = new Vector2(20f, 4f);
        }

        static void AddBox(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            DropCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
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
