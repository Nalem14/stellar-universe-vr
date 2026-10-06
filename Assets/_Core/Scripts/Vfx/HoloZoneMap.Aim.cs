using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Aim support for the table pointer (<see cref="Core.Holo.TacticalCommand"/>): every galaxy star is a
    /// target without a collider of its own — a map-space grid of buckets (built once per catalog) narrows the
    /// stars near a ray, then the star nearest the ray in angle wins. Also the aim radius of each token kind and
    /// the point of the map plane under a ray (the precise hit dot, with its galaxy coordinates).
    /// All math in the content root's local space: the table's scale (seated / standing, grab-scale) never
    /// changes an angle. No allocation per query.
    /// </summary>
    public partial class HoloZoneMap
    {
        const int AimGridCells = 40;
        /// <summary>Aim radius of a star's bright core (m, map local) — the glow quad is larger.</summary>
        const float StarAimRadius = 0.011f;
        const float HereStarAimRadius = 0.018f;
        /// <summary>Highest <see cref="StarRise"/> (m, map local): stars float in a slab, not on a sheet.</summary>
        const float StarRiseMax = 0.06f;

        int[] _aStart = System.Array.Empty<int>();
        int[] _aItems = System.Array.Empty<int>();
        int[] _aFill = System.Array.Empty<int>();
        float _aMinX, _aMinY, _aCellW = 1f, _aCellH = 1f;
        int _aBuiltCount = -1;

        readonly HoloToken[] _gAimHold = new HoloToken[2];

        /// <summary>Pooled star tokens under the table rays: never recycled for another star while held.</summary>
        public void HoldGalaxyTokens(HoloToken a, HoloToken b)
        {
            _gAimHold[0] = a;
            _gAimHold[1] = b;
        }

        bool AimHeld(HoloToken t) => t != null && (t == _gAimHold[0] || t == _gAimHold[1]);

        /// <summary>Galaxy zoom as 0 (whole galaxy) … 1 (closest), log-scaled; 1 off the galaxy.</summary>
        public float GalaxyZoom01
        {
            get
            {
                if (!_showingGalaxy || _gMaxScale <= _gMinScale || _gMinScale <= 0f)
                    return 1f;
                return Mathf.Clamp01(Mathf.Log(_gScale / _gMinScale) / Mathf.Log(_gMaxScale / _gMinScale));
            }
        }

        /// <summary>Aim radius of a token (m, map local, before the token's own scale).</summary>
        public float AimRadiusOf(HoloToken t)
        {
            if (t == null)
                return 0f;
            switch (t.Kind)
            {
                case HoloTokenKind.Planet:
                    return PlanetSize(t.Slot) * 1.15f;
                case HoloTokenKind.Asteroid:
                    return 0.034f;
                case HoloTokenKind.Anomaly:
                    return 0.024f;
                case HoloTokenKind.System:
                    return _showingGalaxy ? StarAimRadius : StarRadius * 1.1f;
                default:
                    // Ship silhouettes are a few cm long (9×9 modules × ShipScale).
                    return ShipScale * 6f;
            }
        }

        /// <summary>(Re)build the star buckets on the map plane (map units, static under pan / zoom).</summary>
        void BuildStarGrid()
        {
            var stars = GalaxyStars;
            var n = stars.Count;
            _aBuiltCount = n;
            if (n == 0)
                return;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < n; i++)
            {
                var s = stars[i];
                minX = Mathf.Min(minX, s.MapX);
                maxX = Mathf.Max(maxX, s.MapX);
                minY = Mathf.Min(minY, s.MapY);
                maxY = Mathf.Max(maxY, s.MapY);
            }

            _aMinX = minX;
            _aMinY = minY;
            _aCellW = Mathf.Max(1e-3f, (maxX - minX) / AimGridCells * 1.0001f);
            _aCellH = Mathf.Max(1e-3f, (maxY - minY) / AimGridCells * 1.0001f);
            const int cells = AimGridCells * AimGridCells;
            if (_aStart.Length != cells + 1)
            {
                _aStart = new int[cells + 1];
                _aFill = new int[cells];
            }

            if (_aItems.Length < n)
                _aItems = new int[n];
            System.Array.Clear(_aStart, 0, _aStart.Length);
            for (var i = 0; i < n; i++)
                _aStart[CellOf(stars[i].MapX, stars[i].MapY) + 1]++;
            for (var c = 0; c < cells; c++)
                _aStart[c + 1] += _aStart[c];
            System.Array.Copy(_aStart, _aFill, cells);
            for (var i = 0; i < n; i++)
                _aItems[_aFill[CellOf(stars[i].MapX, stars[i].MapY)]++] = i;
        }

        int CellOf(float mx, float my)
        {
            var cx = Mathf.Clamp((int)((mx - _aMinX) / _aCellW), 0, AimGridCells - 1);
            var cy = Mathf.Clamp((int)((my - _aMinY) / _aCellH), 0, AimGridCells - 1);
            return cy * AimGridCells + cx;
        }

        /// <summary>
        /// The galaxy star a ray points at: the smallest angle to the ray once the star's own angular radius
        /// (at least <paramref name="minAngleDeg"/>) is counted, within <paramref name="coneDeg"/> of it.
        /// Score = angle / (radius + cone), below 1 to count; <paramref name="stickyId"/> keeps its score × <paramref name="sticky"/>
        /// (hysteresis), and systems holding our ships or worlds get a light pull.
        /// </summary>
        /// <param name="maxDistance">World metres along the ray (solid room geometry in front stops the aim).</param>
        /// <returns>Index in <see cref="GalaxyStars"/>, or −1.</returns>
        public int GalaxyPickRay(Vector3 originWorld, Vector3 dirWorld, float maxDistance, float coneDeg, float minAngleDeg,
            int stickyId, float sticky, out float score, out Vector3 starWorld)
        {
            score = float.MaxValue;
            starWorld = Vector3.zero;
            if (!_showingGalaxy || _root == null || _gScale <= 0f)
                return -1;
            var stars = GalaxyStars;
            if (stars.Count == 0)
                return -1;
            if (_aBuiltCount != stars.Count)
                BuildStarGrid();

            var k = Mathf.Max(1e-5f, _root.lossyScale.x);
            var o = _root.InverseTransformPoint(originWorld);
            var d = _root.InverseTransformDirection(dirWorld).normalized;
            var maxT = maxDistance / k;
            if (Mathf.Abs(d.y) < 0.02f)
                return -1;

            // Where the ray crosses the star slab (local y from lift to lift + rise), widened by the cone there.
            var lift = DioramaLift;
            var t0 = (lift - o.y) / d.y;
            var t1 = (lift + StarRiseMax - o.y) / d.y;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            t0 = Mathf.Max(0f, t0);
            t1 = Mathf.Min(maxT, t1);
            if (t1 <= 0f || t0 > maxT)
                return -1;
            t0 = Mathf.Min(t0, t1);
            var a = o + d * t0;
            var b = o + d * t1;
            var reach = t1 * Mathf.Tan((coneDeg + minAngleDeg) * Mathf.Deg2Rad) + HereStarAimRadius + 0.01f;
            var lxMin = Mathf.Min(a.x, b.x) - reach;
            var lxMax = Mathf.Max(a.x, b.x) + reach;
            var lzMin = Mathf.Min(a.z, b.z) - reach;
            var lzMax = Mathf.Max(a.z, b.z) + reach;
            // Local → map: x = centre + lx / scale, y = centre − lz / scale.
            var mx0 = _gCentre.x + lxMin / _gScale;
            var mx1 = _gCentre.x + lxMax / _gScale;
            var my0 = _gCentre.y - lzMax / _gScale;
            var my1 = _gCentre.y - lzMin / _gScale;
            var cx0 = Mathf.Clamp((int)Mathf.Floor((mx0 - _aMinX) / _aCellW), 0, AimGridCells - 1);
            var cx1 = Mathf.Clamp((int)Mathf.Floor((mx1 - _aMinX) / _aCellW), 0, AimGridCells - 1);
            var cy0 = Mathf.Clamp((int)Mathf.Floor((my0 - _aMinY) / _aCellH), 0, AimGridCells - 1);
            var cy1 = Mathf.Clamp((int)Mathf.Floor((my1 - _aMinY) / _aCellH), 0, AimGridCells - 1);
            if (mx1 < _aMinX || my1 < _aMinY || mx0 > _aMinX + _aCellW * AimGridCells ||
                my0 > _aMinY + _aCellH * AimGridCells)
                return -1;

            var limit = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            var limitSq = limit * limit;
            var focus = _focus ?? FocusContext.Current;
            var hereId = focus != null ? focus.SystemId : 0;
            var best = -1;
            var bestLocal = Vector3.zero;
            for (var cy = cy0; cy <= cy1; cy++)
            {
                for (var cx = cx0; cx <= cx1; cx++)
                {
                    var cell = cy * AimGridCells + cx;
                    for (var j = _aStart[cell]; j < _aStart[cell + 1]; j++)
                    {
                        var i = _aItems[j];
                        var s = stars[i];
                        var p = StarLocal(s, lift);
                        if (p.x * p.x + p.z * p.z > limitSq)
                            continue;
                        var v = p - o;
                        var along = Vector3.Dot(v, d);
                        if (along <= 1e-3f || along > maxT)
                            continue;
                        var perp = (v - d * along).magnitude;
                        var ang = Mathf.Atan2(perp, along) * Mathf.Rad2Deg;
                        var r = s.Id == hereId ? HereStarAimRadius : StarAimRadius;
                        var angR = Mathf.Max(Mathf.Atan2(r, along) * Mathf.Rad2Deg, minAngleDeg);
                        var sc = ang / (angR + coneDeg);
                        if (sc >= 1.2f)
                            continue;
                        if (s.Id == stickyId)
                            sc *= sticky;
                        if (_gFleetsPerSystem.ContainsKey(s.Id) || OwnedPlanets.InSystem(s.Id))
                            sc *= 0.88f;
                        if (sc < 1f && sc < score)
                        {
                            score = sc;
                            best = i;
                            bestLocal = p;
                        }
                    }
                }
            }

            if (best >= 0)
                starWorld = _root.TransformPoint(bestLocal);
            return best;
        }

        /// <summary>The interactive token of a galaxy star (a pooled one is moved onto it when needed).</summary>
        public HoloToken GalaxyTokenForStar(int index)
        {
            var stars = GalaxyStars;
            if (!_showingGalaxy || index < 0 || index >= stars.Count || _gPool.Count == 0)
                return null;
            var star = stars[index];
            return TokenForStar(star, StarLocal(star, DioramaLift));
        }

        /// <summary>
        /// Where a ray meets the map plane (the ecliptic; the middle of the star slab on the galaxy), inside the
        /// disc. <paramref name="mapLocal"/> = content-root local point.
        /// </summary>
        public bool AimPlanePoint(Vector3 originWorld, Vector3 dirWorld, float maxDistance, out Vector3 world,
            out Vector3 mapLocal)
        {
            world = Vector3.zero;
            mapLocal = Vector3.zero;
            if (_root == null || !_root.gameObject.activeInHierarchy)
                return false;
            var o = _root.InverseTransformPoint(originWorld);
            var d = _root.InverseTransformDirection(dirWorld).normalized;
            if (Mathf.Abs(d.y) < 1e-3f)
                return false;
            var y = _showingGalaxy ? DioramaLift + StarRiseMax * 0.5f : DioramaLift;
            var t = (y - o.y) / d.y;
            var k = Mathf.Max(1e-5f, _root.lossyScale.x);
            if (t <= 0f || t * k > maxDistance)
                return false;
            var p = o + d * t;
            var limit = WorldScale.HoloDiscRadius * (_showingGalaxy ? GalaxyViewRadiusFactor : 1f);
            if (p.x * p.x + p.z * p.z > limit * limit)
                return false;
            mapLocal = p;
            world = _root.TransformPoint(p);
            return true;
        }

        /// <summary>Galaxy grid coordinates (MoveFleetToSystem's x / y basis) of a content-local point.</summary>
        public Vector2 GalaxyGridAt(Vector3 mapLocal)
        {
            var m = LocalToMap(new Vector2(mapLocal.x, mapLocal.z));
            return new Vector2(m.x / GalaxyCatalog.MapCell - GalaxyCatalog.GridHalf,
                m.y / GalaxyCatalog.MapCell - GalaxyCatalog.GridHalf);
        }
    }
}
