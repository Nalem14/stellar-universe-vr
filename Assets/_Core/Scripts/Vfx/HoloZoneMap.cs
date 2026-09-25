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
    public partial class HoloZoneMap : MonoBehaviour
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
        bool _syncPending;
        /// <summary>Galaxy overview on the table: system events are dropped, ShowSystemMap redraws fresh.</summary>
        bool _showingGalaxy;

        /// <summary>Fleet token per fleet id + its visual signature — polls only touch what changed.</summary>
        readonly Dictionary<int, (GameObject Root, string Sig)> _fleetViews = new();
        readonly HashSet<int> _seenFleets = new();
        readonly List<int> _staleFleets = new();

        public Transform VolumeRoot => _volume;
        /// <summary>The diorama content (tokens, orbits, ecliptic): what the captain grabs, turns and scales.</summary>
        public Transform ContentRoot => _root;
        public TMP_Text Readout => _readout;
        public IReadOnlyList<HoloToken> Tokens => _tokens;
        public event Action TokensRebuilt;
        /// <summary>A fleet token is in hand (or an order is being quoted): no map gestures.</summary>
        public bool InteractionLocked => _interactionLock;

        /// <summary>Hold Focus/diplomacy rebuilds while the player is grabbing a fleet token.</summary>
        public void SetInteractionLock(bool locked)
        {
            _interactionLock = locked;
            if (locked)
                return;
            if (_rebuildPending)
            {
                _rebuildPending = false;
                _syncPending = false;
                Rebuild();
            }
            else if (_syncPending)
            {
                _syncPending = false;
                SyncFleets();
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
                _focus.FleetsChanged -= OnFleetsChanged;
                _focus.FleetsChanged += OnFleetsChanged;
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
                _focus.FleetsChanged -= OnFleetsChanged;
            }
            DiplomacyIndex.Changed -= OnDiplomacyChanged;
            if (_gMesh != null)
                Destroy(_gMesh);
        }

        /// <summary>System / inhabited view changed: the whole system map is redrawn.</summary>
        void OnFocusChanged() => RequestRebuild();

        /// <summary>Diplomacy re-tints planets and fleets alike (rare: every ~45 s at most).</summary>
        void OnDiplomacyChanged() => RequestRebuild();

        void RequestRebuild()
        {
            if (_showingGalaxy)
                return;

            if (_interactionLock)
            {
                _rebuildPending = true;
                return;
            }

            Rebuild();
        }

        /// <summary>Fleet poll delta: add / drop / redraw only the fleet tokens whose state changed.</summary>
        void OnFleetsChanged()
        {
            if (_interactionLock)
            {
                _syncPending = true;
                return;
            }

            SyncFleets();
        }

        void SyncFleets()
        {
            if (_showingGalaxy)
            {
                RefreshGalaxyFleets();
                return;
            }

            var focus = _focus ?? FocusContext.Current;
            if (_root == null || _art == null || focus == null || !focus.HasSystem || _galaxyStub ||
                focus.SystemId != _builtForSystem)
            {
                Rebuild();
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var viewId = focus.ViewFleetId;
            var changed = false;
            _seenFleets.Clear();
            foreach (var fleet in focus.Fleets)
            {
                if (!fleet.VisibleIn(focus.SystemId, now))
                    continue;
                _seenFleets.Add(fleet.Id);
                var view = DescribeFleet(fleet, focus, now, viewId);
                if (_fleetViews.TryGetValue(fleet.Id, out var existing))
                {
                    if (existing.Sig == view.Sig)
                        continue;
                    RemoveFleetView(fleet.Id);
                }

                AddFleetView(fleet.Id, view);
                changed = true;
            }

            _staleFleets.Clear();
            foreach (var id in _fleetViews.Keys)
            {
                if (!_seenFleets.Contains(id))
                    _staleFleets.Add(id);
            }

            foreach (var id in _staleFleets)
            {
                RemoveFleetView(id);
                changed = true;
            }

            if (changed)
                TokensRebuilt?.Invoke();
        }

        readonly struct FleetView
        {
            public readonly int Slot;
            public readonly Color Color;
            public readonly bool Owned;
            public readonly bool Busy;
            public readonly bool Active;
            public readonly EmpireStance Stance;
            public readonly string Name;
            public readonly string Sig;

            public FleetView(int slot, Color color, bool owned, bool busy, bool active, EmpireStance stance,
                string name)
            {
                Slot = slot;
                Color = color;
                Owned = owned;
                Busy = busy;
                Active = active;
                Stance = stance;
                Name = name;
                Sig = slot + "|" + (int)stance + "|" + (busy ? 1 : 0) + (active ? 1 : 0) + (owned ? 1 : 0) + "|" + name;
            }
        }

        static FleetView DescribeFleet(FocusFleet fleet, FocusContext focus, long now, int viewId)
        {
            var stance = DiplomacyIndex.ResolveFleet(fleet);
            return new FleetView(ResolveFleetSlot(fleet, focus), DiplomacyIndex.Tint(stance),
                stance == EmpireStance.Owned, !fleet.CanIssueMove(now), viewId > 0 && fleet.Id == viewId, stance,
                string.IsNullOrEmpty(fleet.Name) ? "ship" : fleet.Name);
        }

        void AddFleetView(int id, in FleetView v)
        {
            var go = PlaceFleet(v.Slot, id, v.Color, id * 17, v.Owned, v.Busy, v.Name, v.Active, v.Stance);
            _fleetViews[id] = (go, v.Sig);
        }

        void RemoveFleetView(int id)
        {
            if (!_fleetViews.TryGetValue(id, out var view))
                return;
            _fleetViews.Remove(id);
            if (view.Root == null)
                return;
            _tokenRoots.Remove(view.Root);
            var token = view.Root.GetComponent<HoloToken>();
            if (token != null)
                _tokens.Remove(token);
            Destroy(view.Root);
        }

        GameObject _plate;

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

            _plate = plate;
            BuildProjection();

            // Compact readout on the near rim of the disc — not a floating mid-air billboard.
            var canvas = DiegeticUi.WorldCanvas(transform, "HoloReadoutCanvas", new Vector2(520f, 56f),
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
            SetEclipticVisible(true);
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
            PlaceAnomalies(focus.SystemId);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var viewId = focus.ViewFleetId;
            foreach (var fleet in focus.Fleets)
            {
                if (fleet.VisibleIn(focus.SystemId, now))
                    AddFleetView(fleet.Id, DescribeFleet(fleet, focus, now, viewId));
            }

        }

        /// <summary>No system data yet: the star alone (never a fake system that looks real).</summary>
        void BuildSynthetic(int systemId)
        {
            PlaceStar(systemId % 5);
        }

        public async void ShowGalaxyAsync()
        {
            _showingGalaxy = true;
            await GalaxyCatalog.EnsureLoaded();
            if (_root == null || _art == null)
                return;
            if (!_showingGalaxy)
                return;
            ClearTokens();
            _galaxyStub = true;
            BuildGalaxyMap();
            SetReadout(Trans.Get("galaxy"));
            TokensRebuilt?.Invoke();
        }

        public void ShowSystemMap()
        {
            _showingGalaxy = false;
            _rebuildPending = false;
            _syncPending = false;
            Rebuild();
        }

        static string FormatEntityLabel(string name, int id)
        {
            if (string.IsNullOrEmpty(name))
                return "#" + id;
            return name + "  #" + id;
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

        public static void SetTokenLabelVisible(HoloToken token, bool visible)
        {
            if (token == null)
                return;
            var label = token.transform.Find("Label");
            if (label != null)
                label.gameObject.SetActive(visible);
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
            foreach (var m in _ownedMeshes)
                if (m != null)
                    Destroy(m);
            _ownedMeshes.Clear();
            _fleetViews.Clear();
            _gFleets.Clear();
            _gPool.Clear();
            _gSlot = System.Array.Empty<GalaxyCatalog.Star>();
            _gSlotUsed = System.Array.Empty<bool>();
            _gField = null;
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
