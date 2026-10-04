using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Our field of view on the galaxy table (web 5021101: GetAllFleets answers only the foreign and pirate ships
    /// we can see). The server's rule drawn as one shape (SU/RadarField, one quad over the plate):
    /// <list type="bullet">
    /// <item>a bubble on every system we hold a world in or fly a ship in (or toward): we see that system;</item>
    /// <item>a wide disc around every ship carrying a scanner (DeepSpaceScanner / SensorArray), its reach
    /// = GetConfigs.scanner baseRange + radarTech × rangePerResearchLevel, with a turning radar sweep.</item>
    /// </list>
    /// The circles fuse into one field with a bright rim and outward ripples; outside it a light veil dims the
    /// plate. Rebuilt with the galaxy view (pan, zoom, fleet delta): only the source list changes.
    /// </summary>
    public partial class HoloZoneMap
    {
        const int MaxRadarSources = 24;
        static readonly Vector4[] RadarSources = new Vector4[MaxRadarSources];
        static readonly int SrcId = Shader.PropertyToID("_Src");
        static readonly int SrcCountId = Shader.PropertyToID("_SrcCount");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int BlendId = Shader.PropertyToID("_Blend");

        GameObject _radarQuad;
        int _radarCount;
        Material _radarMat;
        readonly List<(int SystemId, bool Scanner)> _radarWanted = new();
        readonly HashSet<long> _radarSeen = new();
        readonly List<(Vector4 Src, float Rank)> _radarRanked = new();

        /// <summary>Scanner reach in galaxy map units: base range + radarTech × range per level (GetConfigs.scanner).</summary>
        static float ScanRangeMap()
        {
            var empire = AuthManager.Ensure().Empire;
            var level = empire != null ? FocusContext.AsFloat(empire["radarTech"]) : 0f;
            var baseRange = GameConfig.ScannerBaseRange > 0f ? GameConfig.ScannerBaseRange : 400f;
            return baseRange + level * GameConfig.ScannerRangePerLevel;
        }

        void EnsureRadarField()
        {
            if (_radarQuad != null || _root == null)
                return;
            var shader = Shader.Find("SU/RadarField");
            if (shader == null)
            {
                // Always Included in GraphicsSettings; reaching this means a broken build setup.
                Debug.LogWarning("[SU] HoloZoneMap: SU/RadarField missing, no vision field on the galaxy.");
                return;
            }

            _radarMat ??= new Material(shader) { name = "SU_RadarField" };
            _radarQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _radarQuad.name = "RadarField";
            DropCollider(_radarQuad);
            _radarQuad.transform.SetParent(_root, false);
            _radarQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var mr = _radarQuad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _radarMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _tokenRoots.Add(_radarQuad);
        }

        /// <summary>Gather our vision sources and hand the ones over the plate to the field shader.</summary>
        void UpdateRadarField(float lift)
        {
            if (!_showingGalaxy)
                return;
            EnsureRadarField();
            if (_radarQuad == null)
                return;

            var plate = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor;
            _radarQuad.transform.localPosition = new Vector3(0f, lift - 0.001f, 0f);
            _radarQuad.transform.localScale = new Vector3(plate * 2f, plate * 2f, 1f);

            // Sources: systems we see (worlds, ships here or bound here), then our scanners' reach.
            _radarWanted.Clear();
            _radarSeen.Clear();
            var focus = _focus ?? FocusContext.Current;
            var me = FocusContext.OwnedUserId();
            if (focus != null)
            {
                foreach (var f in focus.Fleets)
                {
                    if (me <= 0 || !f.IsOwnedBy(me))
                        continue;
                    AddRadarSource(f.SystemId, false);
                    if (f.DestSystemId > 0)
                        AddRadarSource(f.DestSystemId, false);
                    if (f.HasScanner)
                    {
                        AddRadarSource(f.SystemId, true);
                        if (f.DestSystemId > 0)
                            AddRadarSource(f.DestSystemId, true);
                    }
                }
            }

            foreach (var p in OwnedPlanets.All)
                AddRadarSource(p.SystemId, false);

            // Bubble: about one neighbour gap (the influence disc is ~2.4 gaps), i.e. "this system" on the map.
            var bubble = (_terrInfluence > 0f ? _terrInfluence * 0.38f : 50f) * _gScale;
            var reach = ScanRangeMap() * _gScale;
            _radarRanked.Clear();
            foreach (var (systemId, scanner) in _radarWanted)
            {
                if (!GalaxyCatalog.TryGet(systemId, out var star))
                    continue;
                var c = MapToLocal(star.MapX, star.MapY, 0f);
                var r = scanner ? reach : bubble;
                var off = new Vector2(c.x, c.z).magnitude;
                if (off - r > plate)
                    continue;
                // Scanners first (their sweep), then whatever lies nearest the middle of the plate.
                _radarRanked.Add((new Vector4(c.x, c.z, r, scanner ? 1f : 0f), (scanner ? 0f : 10f) + off));
            }

            _radarRanked.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            var n = Mathf.Min(MaxRadarSources, _radarRanked.Count);
            for (var i = 0; i < MaxRadarSources; i++)
                RadarSources[i] = i < n ? _radarRanked[i].Src : Vector4.zero;
            _radarMat.SetVectorArray(SrcId, RadarSources);
            _radarMat.SetFloat(SrcCountId, n);
            _radarCount = n;
            _radarMat.SetFloat(RadiusId, plate);
            // Fuse neighbours a little more when zoomed out (bubbles are small then).
            _radarMat.SetFloat(BlendId, Mathf.Clamp(bubble * 0.6f, 0.008f, 0.04f));
            if (_radarQuad.activeSelf != n > 0)
                _radarQuad.SetActive(n > 0);
        }

        /// <summary>
        /// A foreign ship between stars is known only by where it is bound (the server keys its vision on systemid,
        /// which is the destination in flight): along its course a → b (table-local), the first point inside our
        /// field. The token waits there, on the rim, as an inbound contact, instead of showing where no radar
        /// reaches. 0 = already inside (or no field drawn: nothing to clamp to).
        /// </summary>
        float RadarEntry(Vector3 a, Vector3 b)
        {
            if (_radarCount <= 0 || _radarQuad == null || !_radarQuad.activeSelf)
                return 0f;
            var pa = new Vector2(a.x, a.z);
            var d = new Vector2(b.x - a.x, b.z - a.z);
            var dd = Vector2.Dot(d, d);
            var best = 1f;
            var any = false;
            for (var i = 0; i < _radarCount; i++)
            {
                var src = RadarSources[i];
                var m = pa - new Vector2(src.x, src.y);
                var c = Vector2.Dot(m, m) - src.z * src.z;
                if (c <= 0f)
                    return 0f;
                if (dd < 1e-8f)
                    continue;
                var bq = Vector2.Dot(m, d);
                var disc = bq * bq - dd * c;
                if (disc < 0f)
                    continue;
                var t = (-bq - Mathf.Sqrt(disc)) / dd;
                if (t < 0f || t > 1f)
                    continue;
                any = true;
                best = Mathf.Min(best, t);
            }

            return any ? best : 0f;
        }

        void AddRadarSource(int systemId, bool scanner)
        {
            if (systemId <= 0)
                return;
            var key = ((long)systemId << 1) | (scanner ? 1L : 0L);
            if (_radarSeen.Add(key))
                _radarWanted.Add((systemId, scanner));
        }
    }
}
