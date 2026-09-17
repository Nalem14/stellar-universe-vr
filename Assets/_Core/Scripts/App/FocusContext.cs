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
    }

    public sealed class FocusAsteroid
    {
        public int Id;
        public int Slot;
    }

    /// <summary>
    /// Bridge camera focus: system the hublots show after SessionBoot.
    /// </summary>
    public sealed class FocusContext
    {
        public static FocusContext Current { get; private set; } = new();

        public int SystemId { get; private set; }
        public string SystemName { get; private set; } = string.Empty;
        public int SystemType { get; private set; }
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
            SystemType = 0;
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();
            Current = this;
            Changed?.Invoke();
        }

        public void SetFromApi(int systemId, string systemsBody, string fleetsBody)
        {
            SystemId = systemId;
            SystemName = string.Empty;
            SystemType = 0;
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();

            if (systemId > 0)
                TryParseSystem(systemsBody, systemId);

            TryParseFleets(fleetsBody);
            Current = this;
            Changed?.Invoke();
        }

        void TryParseSystem(string systemsBody, int systemId)
        {
            if (string.IsNullOrEmpty(systemsBody))
                return;
            try
            {
                var root = JToken.Parse(systemsBody);
                var systems = root as JArray ?? root["systems"] as JArray;
                if (systems == null)
                    return;

                foreach (var system in systems)
                {
                    var id = system.Value<int?>("id") ?? 0;
                    if (id != systemId)
                        continue;

                    SystemName = system.Value<string>("name")
                                 ?? system.Value<string>("systemname")
                                 ?? string.Empty;
                    SystemType = system.Value<int?>("type") ?? 0;

                    if (system["planets"] is JArray planets)
                    {
                        foreach (var planet in planets)
                        {
                            _planets.Add(new FocusPlanet
                            {
                                Id = planet.Value<int?>("id") ?? 0,
                                Name = planet.Value<string>("name") ?? string.Empty,
                                Slot = planet.Value<int?>("slot") ?? 0,
                                UserId = planet.Value<int?>("userid") ?? 0
                            });
                        }
                    }

                    if (system["asteroids"] is JArray asteroids)
                    {
                        foreach (var rock in asteroids)
                        {
                            _asteroids.Add(new FocusAsteroid
                            {
                                Id = rock.Value<int?>("id") ?? 0,
                                Slot = rock.Value<int?>("slot") ?? rock.Value<int?>("orbit") ?? 0
                            });
                        }
                    }

                    _planets.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                    return;
                }
            }
            catch
            {
                // Shape varies; viewport still works with fleets-only / empty.
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
                    _fleets.Add(new FocusFleet
                    {
                        Id = fleet.Value<int?>("id") ?? 0,
                        Name = fleet.Value<string>("name") ?? string.Empty,
                        PlanetId = fleet.Value<int?>("planetid") ?? 0,
                        AsteroidId = fleet.Value<int?>("asteroidid") ?? 0,
                        UserId = fleet.Value<int?>("userid") ?? 0,
                        SystemId = fleet.Value<int?>("systemid") ?? SystemId
                    });
                }
            }
            catch
            {
                // Ignore malformed fleets; hublots still show the star field.
            }
        }
    }
}
