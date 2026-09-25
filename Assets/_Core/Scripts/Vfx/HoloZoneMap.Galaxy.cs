using System.Collections.Generic;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Galaxy overview on the holo table (web scenes/galaxy.js adapted to a table you lean over):
    /// every system of GetSystems at its drawn position (visual_x/y, else x/y + 100, web north = far side),
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
        readonly List<(GameObject Root, int SystemId, int Index)> _gFleets = new();
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
            _gScale = Mathf.Clamp(_gMaxScale * 0.4f, _gMinScale, _gMaxScale);
            ClampGalaxyCentre();
            _gHasSelection = false;

            EnsureGalaxyField();
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
            if (!_showingGalaxy || !_gDirty || Time.unscaledTime < _gNextBuild)
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
                Color c;
                float size;
                if (s.Id == hereId)
                {
                    c = StarHere;
                    size = 0.05f;
                }
                else if (s.OwnerId > 0)
                {
                    // Held systems: bright, tinted lightly by the owner (the territory carries the colour).
                    c = Color.Lerp(Color.white, OwnerColor(s.OwnerId), 0.35f);
                    c.a = 1f;
                    size = 0.03f;
                }
                else
                {
                    c = StarEmpty;
                    size = 0.02f;
                }

                AddStarQuad(p, size, c);
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
            if (!_interactionLock && (reselect || !_gHasSelection || SelectionDrifted()))
                SelectGalaxyTokens(stars, limitSq, lift, hereId);
            else
                LayoutGalaxyTokens(limitSq, lift);
            if (!_interactionLock)
                LayoutGalaxyFleets(lift);
        }

        readonly List<Vector2> _gSizes = new();

        /// <summary>One star = four vertices at its centre; the shader turns them into a camera-facing glow.</summary>
        void AddStarQuad(Vector3 p, float size, Color c)
        {
            var b = _gVerts.Count;
            Color32 c32 = c;
            for (var k = 0; k < 4; k++)
            {
                _gVerts.Add(p);
                _gColors.Add(c32);
                _gSizes.Add(new Vector2(size, 0f));
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
        public HoloToken GalaxyTargetNear(Vector3 worldPos, float maxMeters)
        {
            if (!_showingGalaxy || _root == null || _gPool.Count == 0)
                return null;
            var local = _root.InverseTransformPoint(worldPos);
            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            var stars = GalaxyCatalog.All;
            var best = -1;
            var bestSq = maxMeters * maxMeters;
            for (var i = 0; i < stars.Count; i++)
            {
                var p = MapToLocal(stars[i].MapX, stars[i].MapY, 0f);
                if (p.x * p.x + p.z * p.z > limit * limit)
                    continue;
                var dx = p.x - local.x;
                var dz = p.z - local.z;
                var d = dx * dx + dz * dz;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = i;
                }
            }

            if (best < 0)
                return null;
            var star = stars[best];
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

                if (_gSlot[i].Id == hereId)
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

        /// <summary>The captain's own ships on the galaxy map (few): redrawn on each fleet delta.</summary>
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
                    if (DiplomacyIndex.ResolveFleet(fleet) != EmpireStance.Owned)
                        continue;
                    // Under way: drawn at its destination, busy (not draggable) until it arrives.
                    var systemId = fleet.IsMoving(now) && fleet.DestSystemId > 0 ? fleet.DestSystemId : fleet.SystemId;
                    if (!GalaxyCatalog.TryGet(systemId, out _))
                        continue;
                    _gFleetsPerSystem.TryGetValue(systemId, out var index);
                    _gFleetsPerSystem[systemId] = index + 1;
                    var name = string.IsNullOrEmpty(fleet.Name) ? "ship" : fleet.Name;
                    var go = PlaceFleet(1, fleet.Id, DiplomacyIndex.Tint(EmpireStance.Owned), fleet.Id * 17, true,
                        !fleet.CanIssueMove(now), name, false, EmpireStance.Owned);
                    go.transform.localScale = Vector3.one * GalaxyFleetScale;
                    _gFleets.Add((go, systemId, index));
                }
            }

            LayoutGalaxyFleets(DioramaLift);
            TokensRebuilt?.Invoke();
        }

        void LayoutGalaxyFleets(float lift)
        {
            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            foreach (var f in _gFleets)
            {
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
    }
}
