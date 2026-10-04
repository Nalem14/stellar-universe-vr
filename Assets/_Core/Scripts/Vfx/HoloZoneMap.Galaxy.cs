using System.Collections.Generic;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Galaxy overview on the holo table (web scenes/galaxy.js adapted to a table you lean over):
    /// every system of GetSystems at its drawn position (GalaxyCatalog.Star.MapX / MapY, web north = far side),
    /// tinted by who holds it (web territories). LOD for Quest:
    /// <list type="bullet">
    /// <item>all systems = one procedural quad mesh (SU/HoloStarField), rebuilt only when the view moves;</item>
    /// <item>a small pool of interactive system tokens (drop targets + coordinate label) follows the
    /// systems nearest the view centre;</item>
    /// <item>the captain's own ships sit on their star, grabbable — drop one on another star for a quoted
    /// jump on the lectern.</item>
    /// </list>
    /// Pan / zoom come from <see cref="HoloMapController"/> (two-hand grip, thumbstick).
    /// </summary>
    public partial class HoloZoneMap
    {
        const int GalaxyTokenPool = 24;
        const float GalaxyRebuildInterval = 0.05f;
        /// <summary>Wanted spacing between neighbouring stars at full zoom-in (m on the table).</summary>
        const float GalaxyNearSpacing = 0.13f;
        const float GalaxyViewRadiusFactor = 0.97f;
        /// <summary>Ship tokens shrink on the galaxy so a star keeps its read under them.</summary>
        const float GalaxyFleetScale = 0.8f;

        static readonly Color StarEmpty = new(0.5f, 0.6f, 0.78f, 0.55f);
        static readonly Color StarHere = new(1f, 0.78f, 0.3f, 1f);

        Vector2 _gCentre;
        float _gScale;
        float _gMinScale;
        float _gMaxScale;
        bool _gDirty;
        float _gNextBuild;
        Vector2 _gSelCentre;
        float _gSelScale;
        bool _gHasSelection;

        Mesh _gMesh;
        GameObject _gField;
        readonly List<Vector3> _gVerts = new();
        readonly List<Color32> _gColors = new();
        readonly List<Vector2> _gUvs = new();
        readonly List<int> _gTris = new();

        readonly List<HoloToken> _gPool = new();
        /// <summary>Star held by each pooled token (index-aligned with <see cref="_gPool"/>).</summary>
        GalaxyCatalog.Star[] _gSlot = System.Array.Empty<GalaxyCatalog.Star>();
        bool[] _gSlotUsed = System.Array.Empty<bool>();
        readonly List<(float d, int i)> _gRank = new();
        readonly List<(GameObject Root, int SystemId, int Index, int FromSystem, long DestTime)> _gFleets = new();
        /// <summary>When each ship was first seen under way between stars (the server keeps no departure time).</summary>
        readonly Dictionary<int, long> _gUnderWaySince = new();
        bool _gHasMovers;
        readonly Dictionary<int, int> _gFleetsPerSystem = new();

        public bool ShowingGalaxy => _showingGalaxy;
        public bool GalaxyAtMaxZoom => _showingGalaxy && _gScale >= _gMaxScale * 0.999f;
        public bool GalaxyAtMinZoom => _showingGalaxy && _gScale <= _gMinScale * 1.001f;

        void BuildGalaxyMap()
        {
            var stars = GalaxyCatalog.All;
            if (stars.Count == 0)
                return;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < stars.Count; i++)
            {
                var s = stars[i];
                minX = Mathf.Min(minX, s.MapX);
                maxX = Mathf.Max(maxX, s.MapX);
                minY = Mathf.Min(minY, s.MapY);
                maxY = Mathf.Max(maxY, s.MapY);
            }

            var dx = Mathf.Max(1f, maxX - minX);
            var dy = Mathf.Max(1f, maxY - minY);
            var r = WorldScale.HoloDiscRadius * 0.9f;
            // Whole galaxy fits the disc at min zoom; at max zoom neighbours are a hand-width apart.
            _gMinScale = r / (Mathf.Sqrt(dx * dx + dy * dy) * 0.5f);
            var spacing = Mathf.Sqrt(dx * dy / Mathf.Max(1, stars.Count));
            _gMaxScale = Mathf.Max(_gMinScale * 2f, GalaxyNearSpacing / Mathf.Max(1e-3f, spacing));

            // Open on the inhabited system, close enough to see its neighbourhood.
            var focus = _focus ?? FocusContext.Current;
            _gCentre = focus != null && GalaxyCatalog.TryGet(focus.SystemId, out var here)
                ? new Vector2(here.MapX, here.MapY)
                : new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            // Close enough that neighbouring stars are a couple of fingers apart (aiming one is easy).
            _gScale = Mathf.Clamp(_gMaxScale * 0.7f, _gMinScale, _gMaxScale);
            // Our ship between stars: frame the whole trip, the origin and the destination both on the table.
            var ship = focus?.FindFleet(focus.ViewFleetId);
            var nowUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (ship != null && ship.IsMoving(nowUnix) && ship.FromSystemId > 0 && ship.DestSystemId > 0 &&
                ship.FromSystemId != ship.DestSystemId && GalaxyCatalog.TryGet(ship.FromSystemId, out var o) &&
                GalaxyCatalog.TryGet(ship.DestSystemId, out var d))
            {
                _gCentre = new Vector2((o.MapX + d.MapX) * 0.5f, (o.MapY + d.MapY) * 0.5f);
                var half = Mathf.Max(1f, new Vector2(o.MapX - d.MapX, o.MapY - d.MapY).magnitude * 0.5f);
                _gScale = Mathf.Clamp(Mathf.Min(_gScale, r * 0.7f / half), _gMinScale, _gMaxScale);
            }
            ClampGalaxyCentre();
            _gHasSelection = false;

            EnsureGalaxyField();
            BuildStarGrid();
            BuildTerritories();
            EnsureGalaxyPool();
            RebuildGalaxyView(true);
            RefreshGalaxyFleets();
        }

        /// <summary>Slide the galaxy under the hands (table-local metres on the plate plane).</summary>
        public void GalaxyPan(Vector2 deltaLocal)
        {
            if (!_showingGalaxy || _gScale <= 0f)
                return;
            // Screen y (web) grows toward the captain on the table, so local +z = map −y.
            _gCentre -= new Vector2(deltaLocal.x, -deltaLocal.y) / _gScale;
            ClampGalaxyCentre();
            _gDirty = true;
        }

        /// <summary>Scale around a table-local pivot (the midpoint between the hands).</summary>
        public void GalaxyZoom(float factor, Vector2 pivotLocal)
        {
            if (!_showingGalaxy || _gScale <= 0f || factor <= 0f)
                return;
            var pivotMap = LocalToMap(pivotLocal);
            _gScale = Mathf.Clamp(_gScale * factor, _gMinScale, _gMaxScale);
            // Keep the pivot star under the hands.
            var after = LocalToMap(pivotLocal);
            _gCentre += pivotMap - after;
            ClampGalaxyCentre();
            _gDirty = true;
        }

        /// <summary>Hands released: settle the interactive tokens on what is now in front of the captain.</summary>
        public void GalaxyGestureEnd()
        {
            if (_showingGalaxy)
                RebuildGalaxyView(true);
        }

        Vector2 LocalToMap(Vector2 local) =>
            _gCentre + new Vector2(local.x, -local.y) / _gScale;

        Vector3 MapToLocal(float mx, float my, float lift) =>
            new((mx - _gCentre.x) * _gScale, lift, -(my - _gCentre.y) * _gScale);

        void ClampGalaxyCentre()
        {
            var stars = GalaxyCatalog.All;
            if (stars.Count == 0)
                return;
            // Never pan the galaxy entirely off the table.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < stars.Count; i++)
            {
                minX = Mathf.Min(minX, stars[i].MapX);
                maxX = Mathf.Max(maxX, stars[i].MapX);
                minY = Mathf.Min(minY, stars[i].MapY);
                maxY = Mathf.Max(maxY, stars[i].MapY);
            }

            // The view may go past the galaxy edge by 40 % of the disc at most: zoomed in, an edge star can
            // still come near the centre; zoomed out, the whole galaxy is drawn back onto the plate.
            var reach = WorldScale.HoloDiscRadius * 0.6f / Mathf.Max(1e-6f, _gScale);
            _gCentre.x = ClampAxis(_gCentre.x, minX + reach, maxX - reach);
            _gCentre.y = ClampAxis(_gCentre.y, minY + reach, maxY - reach);
        }

        static float ClampAxis(float v, float lo, float hi) =>
            lo > hi ? (lo + hi) * 0.5f : Mathf.Clamp(v, lo, hi);

        void LateUpdate()
        {
            if (!_showingGalaxy)
            {
                HideStarCard();
                return;
            }

            TickGalaxyFlight();
            TickGalaxyMovers();
            if (!_gDirty || Time.unscaledTime < _gNextBuild)
                return;
            RebuildGalaxyView(false);
        }

        void EnsureGalaxyField()
        {
            if (_gMesh == null)
            {
                _gMesh = new Mesh { name = "HoloStarField" };
                _gMesh.MarkDynamic();
            }

            _gField = new GameObject("GalaxyField");
            _gField.transform.SetParent(_root, false);
            _gField.AddComponent<MeshFilter>().sharedMesh = _gMesh;
            var mr = _gField.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _art.StarField();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _tokenRoots.Add(_gField);
        }

        void EnsureGalaxyPool()
        {
            _gPool.Clear();
            for (var i = 0; i < GalaxyTokenPool; i++)
            {
                var go = new GameObject("TokenSystem");
                go.transform.SetParent(_root, false);
                _tokenRoots.Add(go);

                // No glyph of its own: the star is drawn by the field; the token is the drop target + label.
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(0.06f, 0.06f, 0.06f);
                col.center = new Vector3(0f, 0.02f, 0f);

                AddTokenLabel(go.transform, "(0, 0)", 0.05f, new Color(0.85f, 0.96f, 1f, 0.98f),
                    plate: true, startVisible: false);

                var token = go.AddComponent<HoloToken>();
                token.Kind = HoloTokenKind.System;
                _gPool.Add(token);
                _tokens.Add(token);
                go.SetActive(false);
            }

            _gSlot = new GalaxyCatalog.Star[_gPool.Count];
            _gSlotUsed = new bool[_gPool.Count];
        }

        /// <param name="reselect">Re-pick which systems get an interactive token (after a gesture / on open).</param>
        void RebuildGalaxyView(bool reselect)
        {
            _gDirty = false;
            _gNextBuild = Time.unscaledTime + GalaxyRebuildInterval;
            var stars = GalaxyCatalog.All;
            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            var limitSq = limit * limit;
            var focus = _focus ?? FocusContext.Current;
            var hereId = focus != null ? focus.SystemId : 0;
            var lift = DioramaLift;

            _gVerts.Clear();
            _gColors.Clear();
            _gUvs.Clear();
            _gSizes.Clear();
            _gTris.Clear();
            for (var i = 0; i < stars.Count; i++)
            {
                var s = stars[i];
                var p = StarLocal(s, lift);
                if (p.x * p.x + p.z * p.z > limitSq)
                    continue;
                // The star in its own colour (web galaxy.js: blue / white / yellow / orange / red), brighter and
                // larger when someone holds it; the holder shows as a thin ring in its colour around it.
                var c = StarColor(s);
                float size;
                var mark = false;
                if (s.Id == hereId)
                {
                    size = 0.064f;
                    mark = true;
                }
                else if (s.OwnerId > 0)
                {
                    size = 0.046f;
                }
                else
                {
                    size = 0.036f;
                    c.a = 0.85f;
                }

                AddStarQuad(p, size, c, Seed01(s.Id), mark);
                if (s.OwnerId > 0)
                    AddStarQuad(p, size * 1.35f, OwnerColor(s.OwnerId), Seed01(s.Id), false, ringOnly: true);
            }

            _gMesh.Clear();
            _gMesh.indexFormat = _gVerts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _gMesh.SetVertices(_gVerts);
            _gMesh.SetColors(_gColors);
            _gMesh.SetUVs(0, _gUvs);
            _gMesh.SetUVs(1, _gSizes);
            _gMesh.SetTriangles(_gTris, 0, false);
            _gMesh.bounds = new Bounds(new Vector3(0f, lift, 0f), new Vector3(limit * 2f + 0.2f, 0.4f, limit * 2f + 0.2f));

            UpdateTerritoryView(lift);
            UpdateRadarField(lift);
            UpdatePrlRange(lift);
            if (!_interactionLock && (reselect || !_gHasSelection || SelectionDrifted()))
                SelectGalaxyTokens(stars, limitSq, lift, hereId);
            else
                LayoutGalaxyTokens(limitSq, lift);
            if (!_interactionLock)
                LayoutGalaxyFleets(lift);
        }

        readonly List<Vector2> _gSizes = new();

        /// <summary>One star = four vertices at its centre; the shader turns them into a camera-facing glow.</summary>
        /// <param name="seed">Twinkle phase (0–1); <paramref name="mark"/> adds the "you are here" ring.</param>
        void AddStarQuad(Vector3 p, float size, Color c, float seed, bool mark, bool ringOnly = false)
        {
            var b = _gVerts.Count;
            Color32 c32 = c;
            var extra = seed + (ringOnly ? 4f : mark ? 2f : 0f);
            for (var k = 0; k < 4; k++)
            {
                _gVerts.Add(p);
                _gColors.Add(c32);
                _gSizes.Add(new Vector2(size, extra));
            }

            _gUvs.Add(new Vector2(0f, 0f));
            _gUvs.Add(new Vector2(0f, 1f));
            _gUvs.Add(new Vector2(1f, 1f));
            _gUvs.Add(new Vector2(1f, 0f));
            _gTris.Add(b);
            _gTris.Add(b + 1);
            _gTris.Add(b + 2);
            _gTris.Add(b);
            _gTris.Add(b + 2);
            _gTris.Add(b + 3);
        }

        static float Seed01(int id)
        {
            unchecked
            {
                return ((uint)id * 2654435761u >> 8) % 997 / 997f;
            }
        }

        /// <summary>The web's star colours by systems.type (objects/star.js), else a stable spectral guess.</summary>
        static Color StarColor(in GalaxyCatalog.Star s)
        {
            var k = s.Kind ?? string.Empty;
            if (k.Contains("blue")) return new Color(0.36f, 0.7f, 1f, 1f);
            if (k.Contains("red")) return new Color(1f, 0.4f, 0.36f, 1f);
            if (k.Contains("orange")) return new Color(1f, 0.62f, 0.25f, 1f);
            if (k.Contains("yellow")) return new Color(1f, 0.86f, 0.35f, 1f);
            if (k.Contains("white")) return new Color(0.82f, 0.97f, 1f, 1f);
            var c = SpectralTint(s.Id);
            c.a = 1f;
            return c;
        }

        /// <summary>A stable spectral class per system: mostly white-yellow, some blue giants, orange and red dwarfs.</summary>
        static Color SpectralTint(int id)
        {
            unchecked
            {
                var h = ((uint)id * 374761393u + 668265263u) >> 12;
                var r = h % 100;
                if (r < 10) return new Color(0.62f, 0.78f, 1f);
                if (r < 40) return new Color(0.95f, 0.96f, 1f);
                if (r < 70) return new Color(1f, 0.93f, 0.72f);
                if (r < 88) return new Color(1f, 0.74f, 0.45f);
                return new Color(1f, 0.52f, 0.38f);
            }
        }

        /// <summary>Stars float at slightly different heights (stable per system): a volume, not a print.</summary>
        static float StarRise(int id)
        {
            unchecked
            {
                var h = (uint)id * 2246822519u;
                return (h % 1000) / 1000f * 0.06f;
            }
        }

        Vector3 StarLocal(in GalaxyCatalog.Star s, float lift) => MapToLocal(s.MapX, s.MapY, lift + StarRise(s.Id));

        bool SelectionDrifted()
        {
            var moved = (_gCentre - _gSelCentre).magnitude * _gScale;
            var zoomed = _gScale / Mathf.Max(1e-6f, _gSelScale);
            return moved > WorldScale.HoloDiscRadius * 0.3f || zoomed > 1.3f || zoomed < 0.77f;
        }

        void SelectGalaxyTokens(IReadOnlyList<GalaxyCatalog.Star> stars, float limitSq, float lift, int hereId)
        {
            _gSelCentre = _gCentre;
            _gSelScale = _gScale;
            _gHasSelection = true;

            // Nearest systems to the view centre that are on the plate; the inhabited one always first.
            _gRank.Clear();
            for (var i = 0; i < stars.Count; i++)
            {
                var p = MapToLocal(stars[i].MapX, stars[i].MapY, lift);
                var d = p.x * p.x + p.z * p.z;
                if (d > limitSq)
                    continue;
                _gRank.Add((stars[i].Id == hereId ? -1f : d, i));
            }

            _gRank.Sort((a, b) => a.d.CompareTo(b.d));
            for (var i = 0; i < _gPool.Count; i++)
            {
                if (i < _gRank.Count)
                    AssignGalaxyToken(i, stars[_gRank[i].i], hereId);
                else
                {
                    _gSlotUsed[i] = false;
                    _gPool[i].gameObject.SetActive(false);
                }
            }

            LayoutGalaxyTokens(limitSq, lift);
        }

        void AssignGalaxyToken(int slot, in GalaxyCatalog.Star star, int hereId)
        {
            var token = _gPool[slot];
            _gSlot[slot] = star;
            _gSlotUsed[slot] = true;
            token.Id = star.Id;
            token.Slot = 0;
            token.GalaxyX = star.X;
            token.GalaxyY = star.Y;
            token.DisplayName = star.Label;
            SetTokenLabelText(token, star.Label);
            token.gameObject.SetActive(true);
            SetTokenLabelVisible(token, star.Id == hereId);
        }

        /// <summary>
        /// Drop target under a dragged ship: the star nearest the hand among ALL systems on the plate
        /// (not only the pooled ones) — a pooled token is moved onto it on demand.
        /// </summary>
        /// <summary>The star token of <paramref name="systemId"/> on the galaxy plate (null off the plate).</summary>
        int _gLastPick;

        public HoloToken GalaxyTargetForSystem(int systemId)
        {
            if (!_showingGalaxy || _root == null || !GalaxyCatalog.TryGet(systemId, out var star))
                return null;
            return GalaxyTargetNear(_root.TransformPoint(MapToLocal(star.MapX, star.MapY, 0f)), 0.02f);
        }

        public HoloToken GalaxyTargetNear(Vector3 worldPos, float maxMeters)
        {
            if (!_showingGalaxy || _root == null || _gPool.Count == 0)
                return null;
            var local = _root.InverseTransformPoint(worldPos);
            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            var stars = GalaxyCatalog.All;
            var best = -1;
            // Magnet: a system holding one of our ships or worlds wins from twice as far, and the star already
            // under the aim holds until another is clearly nearer (no flicker between neighbours).
            var bestSq = maxMeters * maxMeters * 4f;
            for (var i = 0; i < stars.Count; i++)
            {
                var p = MapToLocal(stars[i].MapX, stars[i].MapY, 0f);
                if (p.x * p.x + p.z * p.z > limit * limit)
                    continue;
                var dx = p.x - local.x;
                var dz = p.z - local.z;
                var d = dx * dx + dz * dz;
                var id = stars[i].Id;
                var ours = _gFleetsPerSystem.ContainsKey(id) || OwnedPlanets.InSystem(id);
                if (!ours && d > maxMeters * maxMeters)
                    continue;
                var w = d * (ours ? 0.25f : 1f) * (id == _gLastPick ? 0.6f : 1f);
                if (w < bestSq)
                {
                    bestSq = w;
                    best = i;
                }
            }

            if (best < 0)
                return null;
            _gLastPick = stars[best].Id;
            return TokenForStar(stars[best], local);
        }

        /// <summary>The pooled token holding <paramref name="star"/>, or the one farthest from <paramref name="local"/> moved onto it.</summary>
        HoloToken TokenForStar(in GalaxyCatalog.Star star, Vector3 local)
        {
            for (var i = 0; i < _gPool.Count; i++)
            {
                if (_gSlotUsed[i] && _gSlot[i].Id == star.Id)
                    return _gPool[i];
            }

            // Recycle the pooled token farthest from the hand (never the inhabited system's).
            var focus = _focus ?? FocusContext.Current;
            var hereId = focus != null ? focus.SystemId : 0;
            var victim = -1;
            var victimSq = -1f;
            for (var i = 0; i < _gPool.Count; i++)
            {
                if (!_gSlotUsed[i])
                {
                    victim = i;
                    break;
                }

                if (_gSlot[i].Id == hereId || AimHeld(_gPool[i]))
                    continue;
                var p = _gPool[i].transform.localPosition;
                var d = (p.x - local.x) * (p.x - local.x) + (p.z - local.z) * (p.z - local.z);
                if (d > victimSq)
                {
                    victimSq = d;
                    victim = i;
                }
            }

            if (victim < 0)
                return null;
            AssignGalaxyToken(victim, star, hereId);
            var token = _gPool[victim];
            token.transform.localPosition = StarLocal(star, DioramaLift);
            token.CaptureHome();
            return token;
        }

        void LayoutGalaxyTokens(float limitSq, float lift)
        {
            for (var i = 0; i < _gPool.Count; i++)
            {
                if (!_gSlotUsed[i])
                    continue;
                var token = _gPool[i];
                var star = _gSlot[i];
                var p = StarLocal(star, lift);
                var inside = p.x * p.x + p.z * p.z <= limitSq;
                if (token.gameObject.activeSelf != inside)
                    token.gameObject.SetActive(inside);
                token.transform.localPosition = p;
                token.CaptureHome();
            }
        }

        static void SetTokenLabelText(HoloToken token, string text)
        {
            var label = token.transform.Find("Label");
            var tmp = label != null ? label.GetComponentInChildren<TMP_Text>(true) : null;
            if (tmp != null)
                tmp.text = text;
        }

        /// <summary>
        /// Every ship the server lets us see on the galaxy map, redrawn on each fleet delta: GetAllFleets answers
        /// only what our vision covers (web 5021101 GetVisibleFleetsForUser: our own anywhere, foreign and pirate
        /// ones in the systems we hold or fly in, and within our scanners' reach). Ours are tokens to take and
        /// send; the others are tinted by their stance, to read only. What we see is drawn by the radar field.
        /// </summary>
        void RefreshGalaxyFleets()
        {
            if (!_showingGalaxy || _root == null || _art == null)
                return;
            foreach (var f in _gFleets)
            {
                if (f.Root == null)
                    continue;
                _tokenRoots.Remove(f.Root);
                var t = f.Root.GetComponent<HoloToken>();
                if (t != null)
                    _tokens.Remove(t);
                Destroy(f.Root);
            }

            _gFleets.Clear();
            _gFleetsPerSystem.Clear();
            var focus = _focus ?? FocusContext.Current;
            if (focus != null)
            {
                var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var fleet in focus.Fleets)
                {
                    var stance = DiplomacyIndex.ResolveFleet(fleet);
                    var owned = stance == EmpireStance.Owned;
                    // Under way between stars: on its way from one to the other (busy, not draggable until it
                    // arrives), with its course laid to the destination star.
                    var systemId = fleet.IsMoving(now) && fleet.DestSystemId > 0 ? fleet.DestSystemId : fleet.SystemId;
                    if (!GalaxyCatalog.TryGet(systemId, out _))
                        continue;
                    var from = fleet.IsMoving(now) && fleet.FromSystemId > 0 && fleet.FromSystemId != systemId &&
                               GalaxyCatalog.TryGet(fleet.FromSystemId, out _)
                        ? fleet.FromSystemId
                        : 0;
                    if (from > 0 && (!_gUnderWaySince.TryGetValue(fleet.Id, out var since) || since > now))
                        _gUnderWaySince[fleet.Id] = now;
                    var index = 0;
                    if (from == 0)
                    {
                        _gFleetsPerSystem.TryGetValue(systemId, out index);
                        _gFleetsPerSystem[systemId] = index + 1;
                    }
                    var name = string.IsNullOrEmpty(fleet.Name) ? "ship" : fleet.Name;
                    var go = PlaceFleet(1, fleet.Id, DiplomacyIndex.Tint(stance), fleet.Id * 17, owned,
                        !fleet.IsIdle(now), name, false, stance);
                    go.transform.localScale = Vector3.one * GalaxyFleetScale;
                    if (from > 0)
                        Core.Holo.HoloGlide.Follow(go, true, _art.MoveGhost);
                    _gFleets.Add((go, systemId, index, from, fleet.DestTime));
                }
            }

            LayoutGalaxyFleets(DioramaLift);
            UpdateRadarField(DioramaLift);
            UpdatePrlRange(DioramaLift);
            TokensRebuilt?.Invoke();
        }

        /// <summary>Trip progress of a ship between stars: the voyage's own clock for the inhabited ship.</summary>
        float GalaxyTripProgress(int fleetId, long destTime)
        {
            var focus = _focus ?? FocusContext.Current;
            var voyage = ShipVoyage.Instance;
            if (voyage != null && voyage.Active && focus != null && fleetId == focus.ViewFleetId)
                return voyage.Progress;
            var now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (!_gUnderWaySince.TryGetValue(fleetId, out var since) || destTime <= since)
                return 1f;
            return Mathf.Clamp01((float)((now - since) / (destTime - since)));
        }

        /// <summary>Every frame while ships are between stars: slide them along and redraw their course.</summary>
        void TickGalaxyMovers()
        {
            if (!_gHasMovers)
                return;
            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            foreach (var f in _gFleets)
            {
                if (f.Root == null || f.FromSystem <= 0 || !GalaxyCatalog.TryGet(f.SystemId, out var to) ||
                    !GalaxyCatalog.TryGet(f.FromSystem, out var from))
                    continue;
                var a = StarLocal(from, DioramaLift + 0.025f);
                var b = StarLocal(to, DioramaLift + 0.025f);
                var glide = f.Root.GetComponent<Core.Holo.HoloGlide>();
                var t = GalaxyTripProgress(FleetIdOf(f.Root), f.DestTime);
                if (glide != null)
                    glide.Drive(a, b, t);
                var p = Vector3.Lerp(a, b, t);
                var inside = new Vector2(p.x, p.z).magnitude <= limit;
                if (f.Root.activeSelf != inside)
                    f.Root.SetActive(inside);
            }
        }

        static int FleetIdOf(GameObject root)
        {
            var token = root.GetComponent<HoloToken>();
            return token != null ? token.Id : 0;
        }

        void LayoutGalaxyFleets(float lift)
        {
            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            _gHasMovers = false;
            foreach (var f in _gFleets)
            {
                if (f.FromSystem > 0)
                {
                    _gHasMovers = true;
                    continue;
                }

                if (f.Root == null || !GalaxyCatalog.TryGet(f.SystemId, out var star))
                    continue;
                // Several ships on one star fan out around it, nose outward.
                var ang = f.Index * 1.9f + 0.6f;
                var offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.045f;
                var p = StarLocal(star, lift + 0.025f) + offset;
                var inside = new Vector2(p.x, p.z).magnitude <= limit;
                if (f.Root.activeSelf != inside)
                    f.Root.SetActive(inside);
                var spin = f.Root.GetComponent<HoloSpin>();
                if (spin != null)
                    spin.SetOrigin(p);
                else
                    f.Root.transform.localPosition = p;
                f.Root.transform.localRotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
                var token = f.Root.GetComponent<HoloToken>();
                if (token != null)
                    token.CaptureHome();
            }
        }
    
        // ── Hover card, star menu, focus flights ─────────────────────────────────

        GameObject _gCard;
        TMP_Text _gCardTitle;
        TMP_Text _gCardBody;
        int _gCardFor;
        bool _gFlying;
        Vector2 _gFlyFrom;
        Vector2 _gFlyTo;
        float _gFlyScaleFrom;
        float _gFlyScaleTo;
        float _gFlyT;

        /// <summary>The galaxy plate point under a ray (table-local x / z), for zooming toward the aim.</summary>
        public bool GalaxyAimLocal(Vector3 origin, Vector3 dir, out Vector2 local)
        {
            local = Vector2.zero;
            if (!_showingGalaxy || _root == null || Mathf.Abs(dir.y) < 1e-3f)
                return false;
            var planeY = _root.TransformPoint(new Vector3(0f, DioramaLift + 0.03f, 0f)).y;
            var t = (planeY - origin.y) / dir.y;
            if (t <= 0f || t > 6f)
                return false;
            var p = _root.InverseTransformPoint(origin + dir * t);
            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            if (p.x * p.x + p.z * p.z > limit * limit)
                return false;
            local = new Vector2(p.x, p.z);
            return true;
        }

        /// <summary>Fly the view onto a star (centred, closer): the star menu's "zoom here", Recentre.</summary>
        public void GalaxyFocus(int systemId, float zoomFactor)
        {
            if (!_showingGalaxy || !GalaxyCatalog.TryGet(systemId, out var star))
                return;
            _gFlyFrom = _gCentre;
            _gFlyTo = new Vector2(star.MapX, star.MapY);
            _gFlyScaleFrom = _gScale;
            _gFlyScaleTo = Mathf.Clamp(_gScale * zoomFactor, _gMinScale, _gMaxScale);
            _gFlyT = 0f;
            _gFlying = true;
        }

        void TickGalaxyFlight()
        {
            if (!_gFlying)
                return;
            _gFlyT = Mathf.Min(1f, _gFlyT + Time.unscaledDeltaTime * 1.6f);
            var u = MotionEase.SmoothInOut(_gFlyT);
            _gCentre = Vector2.Lerp(_gFlyFrom, _gFlyTo, u);
            _gScale = Mathf.Lerp(_gFlyScaleFrom, _gFlyScaleTo, u);
            ClampGalaxyCentre();
            _gDirty = true;
            if (_gFlyT >= 1f)
            {
                _gFlying = false;
                RebuildGalaxyView(true);
            }
        }

        /// <summary>
        /// A floating card over the star under the aim: its coordinates, who holds it, how many worlds, our ships
        /// there, and — a ship picked — the trip's time by sub-light / hyperspace (the lectern's own quotes).
        /// </summary>
        public void ShowStarCard(HoloToken token, FocusFleet picked)
        {
            if (!_showingGalaxy || token == null || token.Kind != HoloTokenKind.System ||
                !GalaxyCatalog.TryGet(token.Id, out var star))
            {
                HideStarCard();
                return;
            }

            EnsureStarCard();
            _gCardFor = token.Id;
            _gCardTitle.text = star.Label;
            var lines = new List<string>(4);
            if (star.OwnerId <= 0)
                lines.Add(Trans.Get("vr.survey.owner") + "  <color=#9fb8c8>" + Trans.Get("vr.survey.unclaimed") + "</color>");
            else
            {
                var stance = DiplomacyIndex.Resolve(star.OwnerId);
                var name = DiplomacyIndex.TryIdentity(star.OwnerId, out var n, out _) && !string.IsNullOrEmpty(n) ? n : "#" + star.OwnerId;
                lines.Add(Trans.Get("vr.survey.owner") + "  <color=#" + ColorUtility.ToHtmlStringRGB(DiplomacyIndex.Tint(stance)) + ">" +
                          name + "</color>");
            }

            lines.Add(Cap(Trans.Get("planets")) + "  " + star.PlanetCount +
                      (star.ClaimedCount > 0 ? "  <size=80%><color=#9fb8c8>(" + star.ClaimedCount + " " + Trans.Get("vr.survey.claimed") + ")</color></size>" : string.Empty));
            _gFleetsPerSystem.TryGetValue(star.Id, out var ours);
            if (ours > 0)
                lines.Add(Trans.Get("fleets") + "  <color=#7dffb0>" + ours + "</color>");
            if (picked != null && picked.SystemId != star.Id)
            {
                var sub = Core.Holo.TravelPlanner.Quote(picked, star.X, star.Y, Core.Holo.TravelMode.Sublight);
                var hyp = Core.Holo.TravelPlanner.Quote(picked, star.X, star.Y, Core.Holo.TravelMode.Hyperspace);
                // Hyperspace only when it really runs as such (enough drives and crystal); otherwise it is the
                // sub-light time anyway, and the warning says why.
                var eta = (sub.Available ? Trans.Get("sublight") + " " + Core.Holo.TravelPlanner.TimeText(sub.EtaSeconds) : string.Empty) +
                          (hyp.Available && hyp.FallbackKey == null
                              ? "   " + Trans.Get("hyperdrive") + " " + Core.Holo.TravelPlanner.TimeText(hyp.EtaSeconds)
                              : string.Empty);
                if (eta.Length > 0)
                    lines.Add("<color=#ffc766>" + eta.Trim() + "</color>");
                var warn = hyp.Available && hyp.FallbackKey != null ? Core.Holo.TravelPlanner.Warning(hyp)
                    : Core.Holo.TravelPlanner.Warning(sub);
                if (warn.Length > 0)
                    lines.Add("<size=85%><color=#ffb866>" + warn + "</color></size>");
                // Bond PRL: distance over reach, as the web star menu prints it, green in range, red beyond.
                if (picked.HasPrlBond)
                {
                    var prl = Core.Holo.TravelPlanner.Quote(picked, star.X, star.Y, Core.Holo.TravelMode.PrlBond);
                    if (prl.MaxRange > 0f)
                        lines.Add("<color=#" + (prl.Distance <= prl.MaxRange ? "6fd8ff" : "ff7a8a") + ">" +
                                  Core.Holo.TravelPlanner.Describe(prl) + "</color>");
                }
            }

            _gCardBody.text = string.Join("\n", lines);
            _gCard.SetActive(true);
            PlaceStarCard(token);
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public void HideStarCard()
        {
            _gCardFor = 0;
            if (_gCard != null && _gCard.activeSelf)
                _gCard.SetActive(false);
        }

        void EnsureStarCard()
        {
            if (_gCard != null)
                return;
            _gCard = new GameObject("StarCard");
            _gCard.transform.SetParent(transform, false);
            var px = new Vector2(450f, 240f);
            var canvas = DiegeticUi.WorldCanvas(_gCard.transform, "Canvas", px, Vector3.zero, Quaternion.identity, 0.00085f);
            canvas.sortingOrder = 30;
            var frame = DiegeticUi.HoloFrame(canvas.transform, px);
            frame.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _gCardTitle = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(0f, 87f), new Vector2(420f, 40f), 28f, UI.UiKit.Cyan);
            _gCardTitle.fontStyle = FontStyles.Bold;
            _gCardBody = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(0f, -22f), new Vector2(420f, 170f), 19f, UI.UiKit.TextBright,
                TextAlignmentOptions.Top);
            _gCardBody.richText = true;
            _gCard.SetActive(false);
        }

        void PlaceStarCard(HoloToken token)
        {
            var cam = Camera.main;
            if (_gCard == null || token == null || cam == null)
                return;
            // Above the star, a little toward the captain, turned to face them (yaw + gentle pitch).
            var at = token.transform.position + Vector3.up * 0.15f;
            var toEye = cam.transform.position - at;
            at += Vector3.ProjectOnPlane(toEye, Vector3.up).normalized * 0.03f;
            _gCard.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
        }
}
}
