using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Cached GetSystems (lite=1: the compact galaxy, ~5 000 systems since the 2026-10 growth): every system (grid
    /// X / Y = its coordinates and travel basis; MapX / MapY = where it is drawn, visual_x / visual_y; GalaxyId) for
    /// the holomap Galaxy mode, and every planet's owner, the same source the web uses for "my planets"
    /// (galaxyScene.planetsList filtered on userid). Lookups by id, by grid cell and by planet id are indexed.
    /// </summary>
    public static class GalaxyCatalog
    {
        /// <summary>Grid half-extent and map units per grid cell (GetConfigs galaxy.gridHalf / mapCell, model/system.php).</summary>
        public static float GridHalf => Core.App.GameConfig.GalaxyGridHalf;
        public static float MapCell => Core.App.GameConfig.GalaxyMapCell;

        public struct Star
        {
            public int Id;
            public string Name;
            public float X;
            public float Y;
            /// <summary>systems.visual_x / visual_y (0 = unset) — the drawn galaxy layout.</summary>
            public float VisualX;
            public float VisualY;
            public int Type;
            /// <summary>systems.type as the web draws it: blue, white, yellow, orange, red (galaxy.js star sprites).</summary>
            public string Kind;
            /// <summary>Worlds in the system, and how many are held by someone.</summary>
            public int PlanetCount;
            public int ClaimedCount;
            /// <summary>User holding most of the system's planets (0 = unclaimed) — the web territory tint.</summary>
            public int OwnerId;
            /// <summary>systems.galaxy_id: the galaxy it belongs to (grid coordinates are unique per galaxy).</summary>
            public int GalaxyId;

            /// <summary>
            /// Where the system is drawn (map units, 0..(2 × GridHalf + 1) × MapCell): visual_x / visual_y, else its grid
            /// cell's centre — SystemMapPos() server-side and Helper.systemMapPos() on the web. The grid (X / Y,
            /// −GridHalf..GridHalf) is the same place in cells of MapCell map units: sub-light / hyperspace distances use the grid, Bond PRL range
            /// the map. Both agree since the server realigned the grid on the map (MigrateSystemsGridFromMap).
            /// </summary>
            public float MapX => Drawn ? VisualX : (X + GridHalf) * MapCell;
            public float MapY => Drawn ? VisualY : (Y + GridHalf) * MapCell;

            /// <summary>Bond PRL distance basis (actionjs PrlBondFleetToSystem → SystemMapDistance): the map position.</summary>
            public float BondX => MapX;
            public float BondY => MapY;

            bool Drawn => VisualX > 0f || VisualY > 0f;

            /// <summary>Systems carry no names in the game — they are known by galaxy coordinates.</summary>
            public string Label => Coordinates(X, Y);
        }

        /// <summary>"(x, y)" in galaxy units, integers when whole (same grid as MoveFleetToSystem pos).</summary>
        public static string Coordinates(float x, float y) =>
            "(" + Num(x) + ", " + Num(y) + ")";

        /// <summary>Label of a system id: its coordinates, else #id while the catalogue loads.</summary>
        public static string Label(int systemId) =>
            TryGet(systemId, out var star) ? star.Label : "#" + systemId;

        static string Num(float v) =>
            Mathf.Approximately(v, Mathf.Round(v))
                ? ((int)Mathf.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        public struct PlanetRef
        {
            public int Id;
            public int SystemId;
            public int UserId;
            public string Name;
        }

        static List<Star> Stars = new();
        static List<PlanetRef> Planets = new();
        static Dictionary<int, int> _byId = new();
        static Dictionary<long, int> _byCell = new();
        static Dictionary<int, int> _planetById = new();
        static readonly Dictionary<int, List<Star>> ByGalaxy = new();
        static bool _loaded;
        static Task _loading;
        /// <summary>Grid epoch the catalogue was read under: a newer one (grid realigned) means re-read it.</summary>
        static long _loadedEpoch;

        /// <summary>Every system of every galaxy.</summary>
        public static IReadOnlyList<Star> All => Stars;

        /// <summary>Raised once a (re)load has swapped in a new catalogue.</summary>
        public static event System.Action Loaded;

        /// <summary>The systems of one galaxy (empty while loading).</summary>
        public static IReadOnlyList<Star> InGalaxy(int galaxyId)
        {
            if (ByGalaxy.TryGetValue(galaxyId, out var list))
                return list;
            return System.Array.Empty<Star>();
        }

        /// <summary>The galaxy a system is in (1 when unknown).</summary>
        public static int GalaxyOf(int systemId) => TryGet(systemId, out var s) && s.GalaxyId > 0 ? s.GalaxyId : 1;

        /// <summary>Planets owned by <paramref name="userId"/> across the galaxy.</summary>
        public static void CollectOwnedPlanets(int userId, List<PlanetRef> into)
        {
            into.Clear();
            if (userId <= 0)
                return;
            for (var i = 0; i < Planets.Count; i++)
            {
                if (Planets[i].UserId == userId)
                    into.Add(Planets[i]);
            }
        }

        /// <summary>A planet known to GetSystems (any owner).</summary>
        public static bool TryGetPlanet(int planetId, out PlanetRef planet)
        {
            if (_planetById.TryGetValue(planetId, out var i))
            {
                planet = Planets[i];
                return true;
            }

            planet = default;
            return false;
        }

        public static bool TryGet(int systemId, out Star star)
        {
            if (_byId.TryGetValue(systemId, out var i))
            {
                star = Stars[i];
                return true;
            }

            star = default;
            return false;
        }

        static long CellKey(int galaxyId, int x, int y) => ((long)galaxyId << 40) ^ ((long)(x + 100000) << 20) ^ (uint)(y + 100000);

        /// <summary>System at grid x/y (the server's GetSystemByXY for a pos order), in <paramref name="galaxyId"/> (0 = the
        /// one in view, else the spawn one).</summary>
        public static bool TryGetAt(float x, float y, out Star star, int galaxyId = 0)
        {
            if (galaxyId <= 0)
                galaxyId = CurrentGalaxyId();
            if (_byCell.TryGetValue(CellKey(galaxyId, Mathf.RoundToInt(x), Mathf.RoundToInt(y)), out var i) &&
                Mathf.Approximately(Stars[i].X, x) && Mathf.Approximately(Stars[i].Y, y))
            {
                star = Stars[i];
                return true;
            }

            star = default;
            return false;
        }

        /// <summary>The galaxy of the system in view (the bridge's), 1 before anything is known.</summary>
        public static int CurrentGalaxyId()
        {
            var focus = FocusContext.Current;
            return focus != null && focus.SystemId > 0 ? GalaxyOf(focus.SystemId) : 1;
        }

        public static void CollectNearest(int fromSystemId, int max, List<Star> into)
        {
            into.Clear();
            if (!TryGet(fromSystemId, out var origin) || Stars.Count == 0)
                return;
            // Same galaxy only (another one is a hyperspace crossing, never a neighbour).
            var pool = InGalaxy(origin.GalaxyId);
            _nearest.Clear();
            for (var i = 0; i < pool.Count; i++)
            {
                var s = pool[i];
                if (s.Id == fromSystemId)
                    continue;
                var dx = s.X - origin.X;
                var dy = s.Y - origin.Y;
                _nearest.Add((dx * dx + dy * dy, i));
            }

            _nearest.Sort((a, b) => a.d.CompareTo(b.d));
            var n = Mathf.Min(max, _nearest.Count);
            for (var i = 0; i < n; i++)
                into.Add(pool[_nearest[i].i]);
        }

        static readonly List<(float d, int i)> _nearest = new();

        /// <param name="force">Re-read ownership (after Colonize / conquest).</param>
        public static Task EnsureLoaded(bool force = false)
        {
            if (!force && _loaded && Stars.Count > 0 && _loadedEpoch == Core.App.GameConfig.GridEpoch)
                return Task.CompletedTask;
            // One read at a time: callers arriving meanwhile wait on the same one.
            if (_loading != null && !_loading.IsCompleted)
                return _loading;
            return _loading = Load();
        }

        static async Task Load()
        {
            var epoch = Core.App.GameConfig.GridEpoch;
            var result = await ActionJs.Get("GetSystems", new Dictionary<string, string> { { "lite", "1" } });
            if (!result.Ok)
                return;
            var stars = new List<Star>(Mathf.Max(64, Stars.Count));
            var planets = new List<PlanetRef>(Mathf.Max(64, Planets.Count));
            try
            {
                var root = JToken.Parse(result.Body);
                if (root is JObject lite && lite["v"] != null && lite["systems"] is JArray rows)
                    ParseLite(lite, rows, stars, planets);
                else
                    ParseFull(root as JArray ?? root["systems"] as JArray, stars, planets);
            }
            catch (System.Exception e)
            {
                // Keep the catalogue we had rather than a half-read one.
                Debug.LogWarning("[SU] GetSystems: " + e.Message);
                return;
            }

            if (stars.Count == 0)
                return;
            Swap(stars, planets);
            _loaded = true;
            _loadedEpoch = epoch;
            Loaded?.Invoke();
        }

        /// <summary>GetSystems lite=1 (model/galaxy.php GalaxyLitePayload): arrays, planets listed apart.</summary>
        static void ParseLite(JObject root, JArray rows, List<Star> stars, List<PlanetRef> planets)
        {
            foreach (var t in rows)
            {
                if (!(t is JArray r) || r.Count < 7)
                    continue;
                var kind = (string)r[3] ?? string.Empty;
                stars.Add(new Star
                {
                    Id = (int)r[0],
                    X = (float)r[1],
                    Y = (float)r[2],
                    Kind = kind.ToLowerInvariant(),
                    VisualX = (float)r[4],
                    VisualY = (float)r[5],
                    GalaxyId = Mathf.Max(1, (int)r[6])
                });
            }

            if (root["planets"] is JArray ps)
                foreach (var t in ps)
                {
                    if (!(t is JArray p) || p.Count < 4)
                        continue;
                    planets.Add(new PlanetRef
                    {
                        Id = (int)p[0],
                        SystemId = (int)p[1],
                        UserId = (int)p[2],
                        Name = (string)p[3]
                    });
                }
        }

        /// <summary>The full GetSystems rows (servers before the lite payload).</summary>
        static void ParseFull(JArray arr, List<Star> stars, List<PlanetRef> planets)
        {
            if (arr == null)
                return;
            foreach (var s in arr)
            {
                var systemId = FocusContext.AsInt(s["id"]);
                stars.Add(new Star
                {
                    Id = systemId,
                    Name = FocusContext.AsString(s["name"]),
                    X = FocusContext.AsFloat(s["x"]),
                    Y = FocusContext.AsFloat(s["y"]),
                    VisualX = FocusContext.AsFloat(s["visual_x"]),
                    VisualY = FocusContext.AsFloat(s["visual_y"]),
                    Type = FocusContext.AsInt(s["type"]),
                    Kind = (FocusContext.AsString(s["type"]) ?? string.Empty).ToLowerInvariant(),
                    GalaxyId = Mathf.Max(1, FocusContext.AsInt(s["galaxy_id"]))
                });
                if (s["planets"] is JArray planetArr)
                {
                    foreach (var p in planetArr)
                        AddPlanet(planets, p, systemId, 0);
                }
                else if (s["planets"] is JObject planetMap)
                {
                    foreach (var prop in planetMap.Properties())
                        AddPlanet(planets, prop.Value, systemId, FocusContext.AsInt(prop.Name));
                }
            }
        }

        /// <summary>Indexes and per-system ownership built on the new lists, then swapped in at once.</summary>
        static void Swap(List<Star> stars, List<PlanetRef> planets)
        {
            var byId = new Dictionary<int, int>(stars.Count);
            var byCell = new Dictionary<long, int>(stars.Count);
            for (var i = 0; i < stars.Count; i++)
            {
                byId[stars[i].Id] = i;
                byCell[CellKey(stars[i].GalaxyId, Mathf.RoundToInt(stars[i].X), Mathf.RoundToInt(stars[i].Y))] = i;
            }

            var planetById = new Dictionary<int, int>(planets.Count);
            var bySystem = new Dictionary<int, List<int>>();
            for (var i = 0; i < planets.Count; i++)
            {
                planetById[planets[i].Id] = i;
                if (!bySystem.TryGetValue(planets[i].SystemId, out var l))
                    bySystem[planets[i].SystemId] = l = new List<int>(4);
                l.Add(i);
            }

            for (var i = 0; i < stars.Count; i++)
            {
                var star = stars[i];
                if (bySystem.TryGetValue(star.Id, out var idx))
                {
                    star.PlanetCount = idx.Count;
                    star.OwnerId = DominantOwner(planets, idx);
                    foreach (var k in idx)
                        if (planets[k].UserId > 0)
                            star.ClaimedCount++;
                }

                stars[i] = star;
            }

            Stars = stars;
            Planets = planets;
            _byId = byId;
            _byCell = byCell;
            _planetById = planetById;
            ByGalaxy.Clear();
            foreach (var star in stars)
            {
                if (!ByGalaxy.TryGetValue(star.GalaxyId, out var l))
                    ByGalaxy[star.GalaxyId] = l = new List<Star>();
                l.Add(star);
            }
        }

        static readonly Dictionary<int, int> OwnerCounts = new();

        static int DominantOwner(List<PlanetRef> planets, List<int> idx)
        {
            OwnerCounts.Clear();
            int best = 0, bestCount = 0;
            foreach (var i in idx)
            {
                var uid = planets[i].UserId;
                if (uid <= 0)
                    continue;
                OwnerCounts.TryGetValue(uid, out var n);
                OwnerCounts[uid] = ++n;
                if (n > bestCount)
                {
                    best = uid;
                    bestCount = n;
                }
            }

            return best;
        }

        static void AddPlanet(List<PlanetRef> planets, JToken p, int systemId, int fallbackId)
        {
            if (p == null || p.Type != JTokenType.Object)
                return;
            var id = FocusContext.AsInt(p["id"]);
            if (id == 0)
                id = fallbackId;
            if (id <= 0)
                return;
            var sys = FocusContext.AsInt(p["systemid"]);
            planets.Add(new PlanetRef
            {
                Id = id,
                SystemId = sys > 0 ? sys : systemId,
                UserId = FocusContext.AsInt(p["userid"]),
                Name = FocusContext.AsString(p["name"])
            });
        }
    }
}
