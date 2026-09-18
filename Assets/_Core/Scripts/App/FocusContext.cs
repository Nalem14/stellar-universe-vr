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
    }

    public sealed class FocusFleet
    {
        public int Id;
        public string Name = string.Empty;
        public int PlanetId;
        public int AsteroidId;
        public int UserId;
        public int SystemId;
        public int FromSystemId;
        public int DestSystemId;
        /// <summary>Unix seconds arrival; 0 = idle.</summary>
        public long DestTime;
        public string Pos = string.Empty;
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
        public IReadOnlyList<FocusPlanet> Planets => _planets;
        public IReadOnlyList<FocusAsteroid> Asteroids => _asteroids;
        public IReadOnlyList<FocusFleet> Fleets => _fleets;
        public bool HasSystem => SystemId > 0;

        public event Action Changed;

        readonly List<FocusPlanet> _planets = new();
        readonly List<FocusAsteroid> _asteroids = new();
        readonly List<FocusFleet> _fleets = new();

        public void Clear()
        {
            SystemId = 0;
            SystemName = string.Empty;
            SystemTypeKey = string.Empty;
            SystemType = 0;
            ViewFleetId = 0;
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();
            Current = this;
            Changed?.Invoke();
        }

        public void SetFromApi(int systemId, string systemsBody, string fleetsBody, int preferredFleetId = 0)
        {
            SystemId = systemId;
            SystemName = string.Empty;
            SystemTypeKey = string.Empty;
            SystemType = 0;
            ViewFleetId = 0;
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();

            if (systemId > 0)
                TryParseSystem(systemsBody, systemId);

            TryParseFleets(fleetsBody);
            ViewFleetId = ResolveViewFleetId(preferredFleetId);
            Current = this;
            Changed?.Invoke();
        }

        public void ApplyFleetsBody(string fleetsBody)
        {
            _fleets.Clear();
            TryParseFleets(fleetsBody);
            if (ViewFleetId > 0)
            {
                var stillThere = false;
                foreach (var fleet in _fleets)
                {
                    if (fleet.Id == ViewFleetId)
                    {
                        stillThere = true;
                        break;
                    }
                }

                if (!stillThere)
                    ViewFleetId = ResolveViewFleetId(0);
            }
            else
            {
                ViewFleetId = ResolveViewFleetId(0);
            }

            Changed?.Invoke();
        }

        int ResolveViewFleetId(int preferred)
        {
            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            if (preferred > 0)
            {
                foreach (var fleet in _fleets)
                {
                    if (fleet.Id == preferred && (owned <= 0 || fleet.UserId == owned))
                        return fleet.Id;
                }
            }

            if (owned > 0)
            {
                foreach (var fleet in _fleets)
                {
                    if (fleet.UserId == owned)
                        return fleet.Id;
                }
            }

            return 0;
        }

        public FocusFleet FindViewFleet()
        {
            if (ViewFleetId <= 0)
                return null;
            foreach (var fleet in _fleets)
            {
                if (fleet.Id == ViewFleetId)
                    return fleet;
            }

            return null;
        }

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
                        _planets.Add(new FocusPlanet
                        {
                            Id = AsInt(planet["id"]),
                            Name = AsString(planet["name"]),
                            Slot = AsInt(planet["slot"]),
                            UserId = AsInt(planet["userid"])
                        });
                    }
                }
                else if (system["planets"] is JObject planetMap)
                {
                    foreach (var prop in planetMap.Properties())
                    {
                        var planet = prop.Value;
                        _planets.Add(new FocusPlanet
                        {
                            Id = AsInt(planet["id"]) != 0 ? AsInt(planet["id"]) : AsInt(prop.Name),
                            Name = AsString(planet["name"]),
                            Slot = AsInt(planet["slot"]),
                            UserId = AsInt(planet["userid"])
                        });
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
                    _fleets.Add(new FocusFleet
                    {
                        Id = AsInt(fleet["id"]),
                        Name = AsString(fleet["name"]),
                        PlanetId = AsInt(fleet["planetid"]),
                        AsteroidId = AsInt(fleet["asteroidid"]),
                        UserId = AsInt(fleet["userid"]),
                        SystemId = fleetSystem > 0 ? fleetSystem : SystemId,
                        FromSystemId = AsInt(fleet["from"]),
                        DestSystemId = AsInt(fleet["dest"]),
                        DestTime = AsLong(fleet["desttime"]),
                        Pos = AsString(fleet["pos"])
                    });
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
