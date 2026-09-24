using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.App
{
    public sealed class FocusPlanet
    {
        public int Id;
        public string Name = string.Empty;
        public int Slot;
        public int UserId;
        /// <summary>Server habitability when present (_habitability / habitability). 0 = unknown.</summary>
        public int Habitability;
    }

    public sealed class FocusShipModule
    {
        public int Id;
        public string Type = string.Empty;
        public int GridX = -1;
        public int GridY = -1;
        public bool OnGrid => GridX >= 0 && GridX < 9 && GridY >= 0 && GridY < 9;
    }

    public sealed class FocusFleet
    {
        public int Id;
        public string Name = string.Empty;
        public int PlanetId;
        public int AsteroidId;
        public int UserId;
        public int EmpireId;
        public int SystemId;
        public int FromSystemId;
        public int DestSystemId;
        /// <summary>Unix seconds arrival; 0 = idle.</summary>
        public long DestTime;
        public long AttackEndTime;
        public long HarvestEndTime;
        public long ExploreEndTime;
        public bool IsInBattle;
        public bool IsPirate;
        public string Pos = string.Empty;
        public readonly List<FocusShipModule> Modules = new();

        public bool IsMoving(long unixNow) => DestTime > unixNow;
        public bool IsSieging(long unixNow) => AttackEndTime > unixNow;
        public bool IsHarvesting(long unixNow) => HarvestEndTime > unixNow;
        public bool IsExploring(long unixNow) => ExploreEndTime > unixNow;

        /// <summary>Sourced interpret_game_state.can_issue_move (minus ships.length check).</summary>
        public bool CanIssueMove(long unixNow) =>
            !IsMoving(unixNow) && !IsExploring(unixNow) && !IsHarvesting(unixNow) &&
            !IsSieging(unixNow) && !IsInBattle;

        public bool IsOwnedBy(int userId) => userId > 0 && UserId == userId;

        public bool IsPresentIn(int systemId) => systemId > 0 && SystemId == systemId;

        /// <summary>
        /// Jump / sublight inbound. Covers both dest already stamped on systemid
        /// and origin still on systemid while dest/time point here.
        /// </summary>
        public bool IsArrivingTo(int systemId, long unixNow) =>
            systemId > 0 && DestTime > unixNow && DestSystemId == systemId &&
            (FromSystemId == 0 || FromSystemId != DestSystemId);

        public bool VisibleIn(int systemId, long unixNow) =>
            IsPresentIn(systemId) || IsArrivingTo(systemId, unixNow);
    }

    public sealed class FocusAsteroid
    {
        public int Id;
        public int Slot;
    }

    /// <summary>
    /// Client view focus for the Bridge exterior. Server has no "camera on ship";
    /// Unity picks a viewed fleet / station and renders the focused system around it.
    /// </summary>
    public sealed class FocusContext
    {
        public static FocusContext Current { get; private set; } = new();

        public int SystemId { get; private set; }
        public string SystemName { get; private set; } = string.Empty;
        public string SystemTypeKey { get; private set; } = string.Empty;
        public int SystemType { get; private set; }
        public int ViewFleetId { get; private set; }
        /// <summary>When &gt; 0 and ViewFleetId == 0: fake orbital station over this planet.</summary>
        public int ViewPlanetId { get; private set; }
        public IReadOnlyList<FocusPlanet> Planets => _planets;
        public IReadOnlyList<FocusAsteroid> Asteroids => _asteroids;
        /// <summary>Full GetAllFleets catalog. Filter with IsMine / VisibleInFocus.</summary>
        public IReadOnlyList<FocusFleet> Fleets => _fleets;
        public bool HasSystem => SystemId > 0;

        public event Action Changed;

        readonly List<FocusPlanet> _planets = new();
        readonly List<FocusAsteroid> _asteroids = new();
        readonly List<FocusFleet> _fleets = new();
        static readonly Dictionary<int, List<FocusShipModule>> LayoutByFleet = new();

        public void Clear()
        {
            SystemId = 0;
            SystemName = string.Empty;
            SystemTypeKey = string.Empty;
            SystemType = 0;
            ViewFleetId = 0;
            ViewPlanetId = 0;
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();
            Current = this;
            Changed?.Invoke();
        }

        public void SetViewFleet(int fleetId)
        {
            ViewPlanetId = 0;
            ViewFleetId = ResolveViewFleetId(fleetId);
            if (ViewFleetId <= 0)
                EnsureBridgeView(preferredFleetId: 0, preferredPlanetId: 0);
            else
            {
                Current = this;
                Changed?.Invoke();
            }
        }

        public void SetViewPlanet(int planetId)
        {
            ViewFleetId = 0;
            ViewPlanetId = planetId > 0 && FindPlanet(planetId) != null ? planetId : 0;
            if (ViewPlanetId <= 0)
                EnsureBridgeView(preferredFleetId: 0, preferredPlanetId: 0);
            else
            {
                Current = this;
                Changed?.Invoke();
            }
        }

        public void SetFromApi(int systemId, string systemsBody, string fleetsBody, int preferredFleetId = 0,
            int preferredPlanetId = 0)
        {
            SystemId = systemId;
            SystemName = string.Empty;
            SystemTypeKey = string.Empty;
            SystemType = 0;
            ViewFleetId = 0;
            ViewPlanetId = 0;
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();

            if (systemId > 0)
                TryParseSystem(systemsBody, systemId);

            TryParseFleets(fleetsBody);
            EnsureBridgeView(preferredFleetId, preferredPlanetId);
        }

        public void ApplyFleetsBody(string fleetsBody)
        {
            _fleets.Clear();
            TryParseFleets(fleetsBody);
            if (ViewFleetId > 0 && FindFleet(ViewFleetId) == null)
                ViewFleetId = 0;
            // Bridge must stay bound to a ship or a planet.
            EnsureBridgeView(ViewFleetId, ViewPlanetId);
        }

        /// <summary>
        /// Bridge is always inhabited: a real ship, or a virtual orbital station over a planet.
        /// Ships win over station except when a planet view is explicitly requested (TP).
        /// Station has no MoveFleet* — crew orders need a ship.
        /// </summary>
        public void EnsureBridgeView(int preferredFleetId = 0, int preferredPlanetId = 0)
        {
            if (preferredFleetId > 0)
            {
                var fleetId = ResolveViewFleetId(preferredFleetId);
                if (fleetId > 0)
                {
                    ViewFleetId = fleetId;
                    ViewPlanetId = 0;
                    Current = this;
                    Changed?.Invoke();
                    return;
                }
            }

            // Explicit planet TP (virtual station) — only when caller asked for it.
            if (preferredPlanetId > 0 && FindPlanet(preferredPlanetId) != null)
            {
                ViewFleetId = 0;
                ViewPlanetId = preferredPlanetId;
                Current = this;
                Changed?.Invoke();
                return;
            }

            if (ViewFleetId > 0 && FindFleet(ViewFleetId) != null)
            {
                ViewPlanetId = 0;
                Current = this;
                Changed?.Invoke();
                return;
            }

            ViewFleetId = 0;

            // Prefer a real ship over staying/landing on a virtual station.
            var anyShip = ResolveViewFleetId(0);
            if (anyShip > 0)
            {
                ViewFleetId = anyShip;
                ViewPlanetId = 0;
                Current = this;
                Changed?.Invoke();
                return;
            }

            if (ViewPlanetId > 0 && FindPlanet(ViewPlanetId) != null)
            {
                Current = this;
                Changed?.Invoke();
                return;
            }

            ViewPlanetId = ResolveDefaultPlanetId();
            Current = this;
            Changed?.Invoke();
        }

        int ResolveDefaultPlanetId()
        {
            var owned = OwnedUserId();
            if (owned > 0)
            {
                foreach (var planet in _planets)
                {
                    if (planet.UserId == owned)
                        return planet.Id;
                }
            }

            return _planets.Count > 0 ? _planets[0].Id : 0;
        }

        int ResolveViewFleetId(int preferred)
        {
            var owned = OwnedUserId();
            if (preferred > 0)
            {
                var preferredFleet = FindFleet(preferred);
                if (preferredFleet != null && (owned <= 0 || preferredFleet.IsOwnedBy(owned)))
                    return preferredFleet.Id;
            }

            if (owned > 0)
            {
                foreach (var fleet in _fleets)
                {
                    if (fleet.IsOwnedBy(owned) && (SystemId <= 0 || fleet.IsPresentIn(SystemId)))
                        return fleet.Id;
                }
            }

            return 0;
        }

        public static int OwnedUserId()
        {
            var user = AuthManager.Ensure().User;
            return user != null ? user.id : 0;
        }

        public bool IsMine(FocusFleet fleet) => fleet != null && fleet.IsOwnedBy(OwnedUserId());

        public bool HasInhabitedView => ViewFleetId > 0 || ViewPlanetId > 0;

        public bool VisibleInFocus(FocusFleet fleet, long unixNow) =>
            fleet != null && fleet.VisibleIn(SystemId, unixNow);

        public int CountVisibleInFocus(long unixNow)
        {
            var n = 0;
            for (var i = 0; i < _fleets.Count; i++)
            {
                if (_fleets[i].VisibleIn(SystemId, unixNow))
                    n++;
            }

            return n;
        }

        public FocusFleet FindFleet(int fleetId)
        {
            if (fleetId <= 0)
                return null;
            foreach (var fleet in _fleets)
            {
                if (fleet.Id == fleetId)
                    return fleet;
            }

            return null;
        }

        public FocusFleet FindViewFleet() => FindFleet(ViewFleetId);

        public FocusPlanet FindPlanet(int planetId)
        {
            foreach (var planet in _planets)
            {
                if (planet.Id == planetId)
                    return planet;
            }

            return null;
        }

        public FocusAsteroid FindAsteroid(int asteroidId)
        {
            foreach (var rock in _asteroids)
            {
                if (rock.Id == asteroidId)
                    return rock;
            }

            return null;
        }

        void TryParseSystem(string systemsBody, int systemId)
        {
            if (string.IsNullOrEmpty(systemsBody))
                return;
            try
            {
                var root = JToken.Parse(systemsBody);
                var system = FindSystemToken(root, systemId);
                if (system == null)
                    return;

                SystemName = AsString(system["name"]);
                if (string.IsNullOrEmpty(SystemName))
                    SystemName = AsString(system["systemname"]);

                SystemTypeKey = AsString(system["type"]);
                SystemType = AsInt(system["type"]);
                if (SystemType == 0 && !string.IsNullOrEmpty(SystemTypeKey))
                    SystemType = HashType(SystemTypeKey);

                if (system["planets"] is JArray planets)
                {
                    foreach (var planet in planets)
                    {
                        _planets.Add(ParsePlanet(planet, 0));
                    }
                }
                else if (system["planets"] is JObject planetMap)
                {
                    foreach (var prop in planetMap.Properties())
                    {
                        var planet = prop.Value;
                        var fallbackId = AsInt(prop.Name);
                        _planets.Add(ParsePlanet(planet, fallbackId));
                    }
                }

                if (system["asteroids"] is JArray asteroids)
                {
                    var index = 0;
                    foreach (var rock in asteroids)
                    {
                        index++;
                        var slot = AsInt(rock["slot"]);
                        if (slot <= 0)
                            slot = AsInt(rock["orbit"]);
                        if (slot <= 0)
                            slot = index;
                        _asteroids.Add(new FocusAsteroid
                        {
                            Id = AsInt(rock["id"]),
                            Slot = slot
                        });
                    }
                }

                _planets.Sort((a, b) => a.Slot.CompareTo(b.Slot));

                if (string.IsNullOrEmpty(SystemName))
                {
                    foreach (var planet in _planets)
                    {
                        if (!string.IsNullOrEmpty(planet.Name) && planet.UserId > 0)
                        {
                            SystemName = planet.Name;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Shape varies; exterior still works with fleets-only / empty.
            }
        }

        void TryParseFleets(string fleetsBody)
        {
            if (string.IsNullOrEmpty(fleetsBody))
                return;
            try
            {
                var root = JToken.Parse(fleetsBody);
                JArray fleets = root as JArray;
                if (fleets == null && root is JObject obj)
                {
                    fleets = obj["fleets"] as JArray
                             ?? obj["data"] as JArray
                             ?? obj["items"] as JArray;
                }

                if (fleets == null)
                    return;

                foreach (var fleet in fleets)
                {
                    var fleetSystem = AsInt(fleet["systemid"]);
                    var row = new FocusFleet
                    {
                        Id = AsInt(fleet["id"]),
                        Name = AsString(fleet["name"]),
                        PlanetId = AsInt(fleet["planetid"]),
                        AsteroidId = AsInt(fleet["asteroidid"]),
                        UserId = AsInt(fleet["userid"]),
                        EmpireId = AsInt(fleet["empireid"]),
                        SystemId = fleetSystem > 0 ? fleetSystem : SystemId,
                        FromSystemId = AsInt(fleet["from"]),
                        DestSystemId = AsInt(fleet["dest"]),
                        DestTime = AsLong(fleet["desttime"]),
                        AttackEndTime = AsLong(fleet["attackEndTime"]),
                        HarvestEndTime = AsLong(fleet["harvestEndTime"]),
                        ExploreEndTime = AsLong(fleet["exploreEndTime"]),
                        IsInBattle = AsBool(fleet["isInBattle"]),
                        IsPirate = AsBool(fleet["isPirate"]),
                        Pos = AsString(fleet["pos"])
                    };
                    ParseShipModules(fleet, row.Modules);
                    if (!HasGrid(row.Modules) && LayoutByFleet.TryGetValue(row.Id, out var cached))
                    {
                        row.Modules.Clear();
                        CloneModules(cached, row.Modules);
                    }
                    else if (HasGrid(row.Modules))
                    {
                        CacheLayout(row.Id, row.Modules);
                    }

                    _fleets.Add(row);
                }
            }
            catch
            {
                // Ignore malformed fleets.
            }
        }

        static JToken FindSystemToken(JToken root, int systemId)
        {
            if (root is JArray systems)
            {
                foreach (var system in systems)
                {
                    if (AsInt(system["id"]) == systemId)
                        return system;
                    if (string.Equals(AsString(system["id"]), systemId.ToString(), StringComparison.Ordinal))
                        return system;
                }

                return null;
            }

            if (root is JObject obj)
            {
                if (obj["systems"] != null)
                    return FindSystemToken(obj["systems"], systemId);

                var keyed = obj[systemId.ToString()];
                if (keyed != null)
                    return keyed;

                foreach (var prop in obj.Properties())
                {
                    if (AsInt(prop.Name) == systemId || AsInt(prop.Value?["id"]) == systemId)
                        return prop.Value;
                }
            }

            return null;
        }

        public static int AsInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0;
            if (token.Type == JTokenType.Integer)
                return token.Value<int>();
            if (token.Type == JTokenType.Float)
                return (int)token.Value<double>();
            var s = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            return int.TryParse(s, out var v) ? v : 0;
        }

        public static long AsLong(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0;
            if (token.Type == JTokenType.Integer)
                return token.Value<long>();
            if (token.Type == JTokenType.Float)
                return (long)token.Value<double>();
            var s = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            return long.TryParse(s, out var v) ? v : 0;
        }

        public static string AsString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return string.Empty;
            if (token.Type == JTokenType.String)
                return token.Value<string>() ?? string.Empty;
            return token.ToString();
        }

        public static bool AsBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return false;
            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();
            if (token.Type == JTokenType.Integer)
                return token.Value<int>() != 0;
            var s = AsString(token);
            if (string.Equals(s, "true", System.StringComparison.OrdinalIgnoreCase))
                return true;
            return int.TryParse(s, out var v) && v != 0;
        }

        public static float AsFloat(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0f;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<float>();
            var s = AsString(token);
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : 0f;
        }

        static FocusPlanet ParsePlanet(JToken planet, int fallbackId)
        {
            var id = AsInt(planet["id"]);
            if (id == 0)
                id = fallbackId;
            var habit = AsInt(planet["_habitability"]);
            if (habit == 0)
                habit = AsInt(planet["habitability"]);
            return new FocusPlanet
            {
                Id = id,
                Name = AsString(planet["name"]),
                Slot = AsInt(planet["slot"]),
                UserId = AsInt(planet["userid"]),
                Habitability = habit
            };
        }

        public static void ParseShipModules(JToken source, List<FocusShipModule> into)
        {
            if (source == null || into == null)
                return;
            JArray arr = source as JArray;
            if (arr == null && source is JObject obj)
            {
                arr = obj["ships"] as JArray
                      ?? obj["modules"] as JArray
                      ?? obj["layout"] as JArray
                      ?? obj["data"] as JArray;
                if (arr == null && obj["ships"] is JObject map)
                {
                    foreach (var prop in map.Properties())
                        ParseOneModule(prop.Value, into, prop.Name);
                    return;
                }
            }

            if (arr == null)
                return;
            foreach (var ship in arr)
                ParseOneModule(ship, into, null);
        }

        public static void CacheLayout(int fleetId, IEnumerable<FocusShipModule> modules)
        {
            if (fleetId <= 0 || modules == null)
                return;
            var copy = new List<FocusShipModule>();
            CloneModules(modules, copy);
            if (HasGrid(copy))
                LayoutByFleet[fleetId] = copy;
        }

        public static bool HasGrid(IReadOnlyList<FocusShipModule> modules)
        {
            if (modules == null)
                return false;
            for (var i = 0; i < modules.Count; i++)
            {
                if (modules[i] != null && modules[i].OnGrid)
                    return true;
            }

            return false;
        }

        static void ParseOneModule(JToken ship, List<FocusShipModule> into, string keyName)
        {
            if (ship == null || ship.Type == JTokenType.Null)
                return;
            var type = AsString(ship["type"]);
            if (string.IsNullOrEmpty(type))
                type = AsString(ship["shiptype"]);
            if (string.IsNullOrEmpty(type))
                type = AsString(ship["ship_type"]);
            if (string.IsNullOrEmpty(type))
                type = AsString(ship["key"]);
            var id = AsInt(ship["id"]);
            if (id == 0 && int.TryParse(keyName, out var keyed))
                id = keyed;
            into.Add(new FocusShipModule
            {
                Id = id,
                Type = type,
                GridX = ReadGridCoord(ship, "grid_x", "gridX", "gx", "x"),
                GridY = ReadGridCoord(ship, "grid_y", "gridY", "gy", "y")
            });
        }

        static int ReadGridCoord(JToken ship, params string[] keys)
        {
            for (var i = 0; i < keys.Length; i++)
            {
                var token = ship[keys[i]];
                if (token == null || token.Type == JTokenType.Null)
                    continue;
                if (token.Type == JTokenType.Float)
                {
                    var d = token.Value<double>();
                    if (Math.Abs(d - Math.Floor(d)) > 0.001)
                        continue;
                }

                int v;
                if (token.Type == JTokenType.Integer)
                    v = token.Value<int>();
                else if (token.Type == JTokenType.Float)
                    v = (int)token.Value<double>();
                else if (!int.TryParse(AsString(token), out v))
                    continue;
                if (v >= 0 && v <= 8)
                    return v;
            }

            return -1;
        }

        static void CloneModules(IEnumerable<FocusShipModule> source, List<FocusShipModule> into)
        {
            foreach (var m in source)
            {
                if (m == null)
                    continue;
                into.Add(new FocusShipModule
                {
                    Id = m.Id,
                    Type = m.Type,
                    GridX = m.GridX,
                    GridY = m.GridY
                });
            }
        }

        static int HashType(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;
            unchecked
            {
                var h = 0;
                foreach (var c in key)
                    h = h * 31 + char.ToLowerInvariant(c);
                return Math.Abs(h);
            }
        }
    }
}
