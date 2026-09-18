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

            Rebuild();
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
        }

        void OnFocusChanged() => Rebuild();

        public void EnsureScaffold(Transform tableTop, CicArtKit art)
        {
            _art = art;
            if (_root != null)
                return;

            _root = new GameObject("HoloZoneMap").transform;
            _root.SetParent(tableTop, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;

            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "HoloVolume";
            col.transform.SetParent(_root, false);
            col.transform.localPosition = new Vector3(0f, WorldScale.HoloVolumeHeight * 0.5f, 0f);
            col.transform.localScale = new Vector3(
                WorldScale.HoloDiscRadius * 1.05f,
                WorldScale.HoloVolumeHeight * 0.5f,
                WorldScale.HoloDiscRadius * 1.05f);
            DropCollider(col);
            var volMat = art.Holo(art.OrbitPlate != null ? art.OrbitPlate : art.HoloPlate,
                new Color(0.12f, 0.7f, 1f, 0.1f));
            col.GetComponent<MeshRenderer>().sharedMaterial = volMat;
            var spin = col.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = -6f;
            spin.BobMeters = 0.015f;
            _volume = col.transform;

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plate.name = "OrbitPlate";
            plate.transform.SetParent(_root, false);
            plate.transform.localPosition = new Vector3(0f, 0.008f, 0f);
            plate.transform.localScale = new Vector3(
                WorldScale.HoloDiscRadius * 2f, 0.006f, WorldScale.HoloDiscRadius * 2f);
            DropCollider(plate);
            plate.GetComponent<MeshRenderer>().sharedMaterial = art.Holo(
                art.OrbitPlate != null ? art.OrbitPlate : art.HoloPlate,
                new Color(0.25f, 0.9f, 1f, 0.65f));

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "ProjectorGlow";
            glow.transform.SetParent(tableTop, false);
            glow.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
            DropCollider(glow);
            glow.GetComponent<MeshRenderer>().sharedMaterial = art.Holo(
                art.ProjectorGlow != null ? art.ProjectorGlow : Texture2D.whiteTexture,
                new Color(0.2f, 0.9f, 1f, 0.4f));

            var readoutGo = new GameObject("HoloReadout");
            readoutGo.transform.SetParent(_root, false);
            readoutGo.transform.localPosition = new Vector3(0f, WorldScale.HoloVolumeHeight + 0.04f, 0f);
            readoutGo.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            readoutGo.transform.localScale = Vector3.one * 0.014f;
            _readout = readoutGo.AddComponent<TextMeshPro>();
            _readout.alignment = TextAlignmentOptions.Center;
            _readout.fontSize = 7f;
            _readout.color = new Color(0.55f, 0.95f, 1f, 0.92f);
            _readout.text = Trans.Get("Loading");
            _readout.raycastTarget = false;
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
                var owned = AuthManager.Ensure().User != null &&
                            planet.UserId == AuthManager.Ensure().User.id && planet.UserId > 0;
                var foe = planet.UserId > 0 && !owned;
                var color = owned ? CicArtKit.Cyan : foe ? CicArtKit.Amber : new Color(0.45f, 0.7f, 0.85f, 1f);
                var pname = string.IsNullOrEmpty(planet.Name) ? "planet " + planet.Id : planet.Name;
                PlacePlanet(planet.Slot, planet.Id, color, WorldScale.HoloPlanetTokenRadius(planet.Slot), pname);
            }

            foreach (var rock in focus.Asteroids)
                PlaceAsteroid(rock.Slot, rock.Id, "asteroid " + rock.Id);

            var myId = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var fleet in focus.Fleets)
            {
                if (fleet.SystemId > 0 && fleet.SystemId != focus.SystemId)
                    continue;
                var owned = myId > 0 && fleet.UserId == myId;
                var color = owned ? CicArtKit.Cyan : CicArtKit.Amber;
                var slot = ResolveFleetSlot(fleet, focus);
                var busy = !fleet.CanIssueMove(now);
                var fname = string.IsNullOrEmpty(fleet.Name) ? "ship " + fleet.Id : fleet.Name;
                PlaceFleet(slot, fleet.Id, color, fleet.Id * 17, owned, busy, fname);
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
                PlacePlanet(slot, i + 1, color, WorldScale.HoloPlanetTokenRadius(slot), "planet " + (i + 1));
            }

            for (var i = 0; i < 2 + systemId % 3; i++)
                PlaceAsteroid(1 + (i * 2 + systemId) % 6, 100 + i, "asteroid " + (100 + i));

            for (var i = 0; i < 2; i++)
            {
                var color = i == 0 ? CicArtKit.Cyan : CicArtKit.Amber;
                PlaceFleet(1 + i * 2, 200 + i, color, systemId * 31 + i * 47, owned: i == 0, busy: false,
                    displayName: "ship " + (200 + i));
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
            token.DisplayName = string.Format(
                System.Globalization.CultureInfo.InvariantCulture, "{0}.{1}", gx, gy);
            token.CaptureHome();
            AddTokenLabel(go.transform, token.DisplayName, 0.04f);
            _tokens.Add(token);
        }

        void PlaceStar(int typeHint)
        {
            var tint = typeHint % 3 == 0
                ? new Color(1f, 0.92f, 0.55f, 0.85f)
                : typeHint % 3 == 1
                    ? new Color(1f, 0.7f, 0.45f, 0.85f)
                    : new Color(0.7f, 0.85f, 1f, 0.85f);
            var star = TokenVisual("TokenStar", PrimitiveType.Sphere,
                new Vector3(0f, WorldScale.HoloTokenLift + WorldScale.HoloStarRadius, 0f),
                Vector3.one * (WorldScale.HoloStarRadius * 2f),
                _art.Holo(_art.TokenSystem != null ? _art.TokenSystem : Texture2D.whiteTexture, tint),
                keepCollider: true);
            var spin = star.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 12f;
            spin.BobMeters = 0.015f;

            // Drop = leave orbit / park at star (MoveFleetToSystem current pos).
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
            token.DisplayName = label;
            token.CaptureHome();
            AddTokenLabel(star.transform, label, WorldScale.HoloStarRadius + 0.05f);
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
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Orbit_" + slot;
            ring.transform.SetParent(_root, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(r * 2f, 0.003f, r * 2f);
            DropCollider(ring);
            ring.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(
                Texture2D.whiteTexture, new Color(0.25f, 0.85f, 1f, 0.42f));
            _tokenRoots.Add(ring);
        }

        void PlacePlanet(int slot, int id, Color color, float radius, string displayName)
        {
            var r = WorldScale.HoloOrbitRadius(Mathf.Max(1, slot));
            var ang = StableAngle(id * 97 + slot * 13);
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift + radius, Mathf.Sin(ang) * r);
            var go = TokenVisual("TokenPlanet_" + id, PrimitiveType.Sphere, pos, Vector3.one * (radius * 2f),
                _art.Holo(_art.TokenPlanet != null ? _art.TokenPlanet : Texture2D.whiteTexture,
                    new Color(color.r, color.g, color.b, 0.8f)),
                keepCollider: true);
            var spin = go.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 8f + slot;
            spin.BobMeters = 0.006f;
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "Band";
            band.transform.SetParent(go.transform, false);
            band.transform.localPosition = Vector3.zero;
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            band.transform.localScale = new Vector3(1.15f, 0.08f, 1.15f);
            DropCollider(band);
            band.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(
                Texture2D.whiteTexture, new Color(color.r, color.g, color.b, 0.55f));
            AddStem(go.transform, pos.y);
            AddTokenLabel(go.transform, displayName, radius + 0.04f);
            Tag(go, HoloTokenKind.Planet, id, slot, owned: false, busy: false, displayName);
        }

        void PlaceAsteroid(int slot, int id, string displayName)
        {
            var r = WorldScale.HoloOrbitRadius(Mathf.Max(1, slot)) * 0.92f;
            var ang = StableAngle(id * 53 + 7);
            var pos = new Vector3(Mathf.Cos(ang) * r, WorldScale.HoloTokenLift, Mathf.Sin(ang) * r);
            var go = TokenVisual("TokenRock_" + id, PrimitiveType.Cube, pos,
                Vector3.one * (WorldScale.HoloAsteroidRadius * 2f),
                _art.Lit(Texture2D.whiteTexture, new Color(0.55f, 0.58f, 0.62f), 0.4f),
                keepCollider: true);
            go.transform.localRotation = Quaternion.Euler(ang * Mathf.Rad2Deg, id * 20f, 15f);
            AddTokenLabel(go.transform, displayName, 0.05f);
            Tag(go, HoloTokenKind.Asteroid, id, slot, owned: false, busy: false, displayName);
        }

        void PlaceFleet(int slot, int id, Color color, int seed, bool owned, bool busy, string displayName)
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

            var s = WorldScale.HoloFleetSize;
            AddBox(go.transform, "Hull", new Vector3(0f, 0f, 0.01f), new Vector3(s * 0.55f, s * 0.22f, s * 1.1f),
                _art.Holo(_art.TokenFleet != null ? _art.TokenFleet : Texture2D.whiteTexture,
                    new Color(color.r, color.g, color.b, 0.85f)));
            AddBox(go.transform, "WingL", new Vector3(-s * 0.35f, 0f, -s * 0.15f),
                new Vector3(s * 0.2f, s * 0.12f, s * 0.7f),
                _art.Holo(Texture2D.whiteTexture, new Color(color.r, color.g, color.b, 0.7f)));
            AddBox(go.transform, "WingR", new Vector3(s * 0.35f, 0f, -s * 0.15f),
                new Vector3(s * 0.2f, s * 0.12f, s * 0.7f),
                _art.Holo(Texture2D.whiteTexture, new Color(color.r, color.g, color.b, 0.7f)));
            AddBox(go.transform, "Engine", new Vector3(0f, 0f, -s * 0.55f),
                new Vector3(s * 0.18f, s * 0.18f, s * 0.2f),
                busy ? _art.AmberEmit(4.2f) : _art.AmberEmit(3.5f));
            AddStem(go.transform, pos.y);
            if (busy)
                AddBusyRing(go.transform, s);
            if (owned)
                AddGrabHalo(go.transform, s);

            // Parent collider for grab — generous for VR hand / ray.
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(s * 2.8f, s * 1.8f, s * 3.0f);
            col.center = new Vector3(0f, 0f, 0f);

            var spin = go.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 0f;
            spin.BobMeters = busy ? 0.018f : 0.01f;
            AddTokenLabel(go.transform, displayName, s * 0.9f);
            Tag(go, HoloTokenKind.Fleet, id, slot, owned, busy, displayName);
        }

        void PlaceGalaxyStubRing()
        {
            var r = WorldScale.HoloDiscRadius * 0.98f;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = _galaxyStub ? "GalaxyLevelStub" : "ZoneRim";
            ring.transform.SetParent(_root, false);
            ring.transform.localPosition = new Vector3(0f, 0.018f, 0f);
            ring.transform.localScale = new Vector3(r * 2f, 0.0025f, r * 2f);
            DropCollider(ring);
            ring.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(
                Texture2D.whiteTexture, new Color(0.35f, 0.55f, 0.75f, 0.12f));
            _tokenRoots.Add(ring);
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

        void AddTokenLabel(Transform parent, string text, float height)
        {
            if (string.IsNullOrEmpty(text) || _art == null)
                return;
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.008f;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 5f;
            tmp.color = new Color(0.75f, 0.95f, 1f, 0.95f);
            tmp.text = text;
            tmp.raycastTarget = false;
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
