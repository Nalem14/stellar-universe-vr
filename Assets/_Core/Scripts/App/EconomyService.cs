using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>One owned planet as GetResource returns it (the server accumulates production on that call).</summary>
    public sealed class PlanetEconomy
    {
        public int Id;
        public int SystemId;
        public string Name = string.Empty;
        public float Energy;
        public float EnergyUsed;
        public float Mineral;
        public float Crystal;
        public float Biomass;
        public float MineralStorage;
        public float CrystalStorage;
        public float BiomassStorage;
        public int Citizen;
        public int Mood;
        public int Jobs;
        public int Employed;
        public int FreeField;
        public float Habitability;
        public int Defense;
        /// <summary>Raw planet row: building levels (mineralMine, home…), working state, troops, queues.</summary>
        public JObject Raw;

        public int Level(string building) => FocusContext.AsInt(Raw?[building]);
    }

    /// <summary>
    /// Economy heartbeat: GetResource on every owned planet (csv, ≤ 50 per call) at a slow cadence.
    /// This is what makes the server accumulate production and resolve lazy work (buildings, stargate
    /// missions) — the web does the same from its resource bar. Consoles read <see cref="Planets"/> and
    /// listen to <see cref="Changed"/>; nothing here rebuilds the room.
    /// </summary>
    public sealed class EconomyService : MonoBehaviour
    {
        public const float Interval = 10f;
        const int MaxPerCall = 50;

        public static EconomyService Instance { get; private set; }

        readonly Dictionary<int, PlanetEconomy> _planets = new();
        readonly List<GalaxyCatalog.PlanetRef> _owned = new();
        readonly StringBuilder _csv = new();
        Coroutine _loop;
        bool _busy;

        public IReadOnlyDictionary<int, PlanetEconomy> Planets => _planets;
        /// <summary>Empire row GetResource attaches (research levels, nova) — refreshed with the planets.</summary>
        public JObject Empire { get; private set; }
        public int Nova => FocusContext.AsInt(Empire?["nova"]);
        public int ResearchLevel(string key) => FocusContext.AsInt(Empire?[key]);
        public event Action Changed;
        /// <summary>A building finished on a planet (planet, building type) — the Ops officer reports it.</summary>
        public event Action<PlanetEconomy, string> BuildingCompleted;
        readonly Dictionary<int, string> _lastWork = new();
        readonly List<(PlanetEconomy, string)> _completed = new();

        public static EconomyService Ensure(Transform host)
        {
            if (Instance != null)
                return Instance;
            var go = new GameObject("EconomyService");
            go.transform.SetParent(host, false);
            return go.AddComponent<EconomyService>();
        }

        void Awake() => Instance = this;

        void OnEnable() => _loop = StartCoroutine(Loop());

        void OnDisable()
        {
            if (_loop != null)
                StopCoroutine(_loop);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public bool TryGet(int planetId, out PlanetEconomy planet) => _planets.TryGetValue(planetId, out planet);

        /// <summary>Owned planet ids, stable order (GetSystems ownership, like the web planetsList).</summary>
        public void CollectOwned(List<GalaxyCatalog.PlanetRef> into)
        {
            GalaxyCatalog.CollectOwnedPlanets(FocusContext.OwnedUserId(), into);
            into.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        IEnumerator Loop()
        {
            var wait = new WaitForSeconds(Interval);
            while (true)
            {
                if (AuthManager.Ensure().IsLoggedIn)
                {
                    var task = RefreshNow();
                    while (!task.IsCompleted)
                        yield return null;
                }

                yield return wait;
            }
        }

        /// <summary>Refresh now (after an order); overlapping calls collapse into the running one.</summary>
        public async Task RefreshNow()
        {
            if (_busy)
            {
                while (_busy)
                    await Task.Yield();
                return;
            }

            _busy = true;
            try
            {
                await GalaxyCatalog.EnsureLoaded();
                CollectOwned(_owned);
                if (_owned.Count == 0)
                    return;

                var changed = false;
                for (var start = 0; start < _owned.Count; start += MaxPerCall)
                {
                    _csv.Clear();
                    for (var i = start; i < _owned.Count && i < start + MaxPerCall; i++)
                    {
                        if (_csv.Length > 0)
                            _csv.Append(',');
                        _csv.Append(_owned[i].Id);
                    }

                    var result = await ActionJs.Get("GetResource", new Dictionary<string, string>
                    {
                        { "planet", _csv.ToString() },
                        // raw: plain numbers (not "1.2K") + per-second earn rates, as the web planet screen.
                        { "raw", "1" }
                    });
                    if (!result.Ok || string.IsNullOrEmpty(result.Body))
                        continue;
                    changed |= Apply(result.Body);
                }

                if (changed)
                    Changed?.Invoke();
                foreach (var (planet, type) in _completed)
                    BuildingCompleted?.Invoke(planet, type);
                _completed.Clear();
            }
            finally
            {
                _busy = false;
            }
        }

        bool Apply(string body)
        {
            JToken root;
            try
            {
                root = JToken.Parse(body);
            }
            catch
            {
                return false;
            }

            var any = false;
            if (root is JArray arr)
            {
                foreach (var row in arr)
                    any |= ApplyRow(row as JObject);
            }
            else
            {
                any = ApplyRow(root as JObject);
            }

            return any;
        }

        bool ApplyRow(JObject row)
        {
            if (row == null)
                return false;
            var id = FocusContext.AsInt(row["id"]);
            if (id <= 0)
                return false;
            // Never keep the account row the server attaches (credentials): only planet + empire data.
            row.Remove("user");
            if (row["empire"] is JObject empire)
                Empire = empire;
            row.Remove("empire");
            if (!_planets.TryGetValue(id, out var p))
            {
                p = new PlanetEconomy { Id = id };
                _planets[id] = p;
            }

            p.SystemId = FocusContext.AsInt(row["systemid"]);
            p.Name = FocusContext.AsString(row["name"]);
            p.Energy = FocusContext.AsFloat(row["energy"]);
            p.EnergyUsed = FocusContext.AsFloat(row["energyused"]);
            p.Mineral = FocusContext.AsFloat(row["mineral"]);
            p.Crystal = FocusContext.AsFloat(row["crystal"]);
            p.Biomass = FocusContext.AsFloat(row["biomass"]);
            p.MineralStorage = FocusContext.AsFloat(row["mineralStorage"]);
            p.CrystalStorage = FocusContext.AsFloat(row["crystalStorage"]);
            p.BiomassStorage = FocusContext.AsFloat(row["biomassStorage"]);
            p.Citizen = FocusContext.AsInt(row["citizen"]);
            p.Mood = FocusContext.AsInt(row["mood"]);
            p.Jobs = FocusContext.AsInt(row["jobs"]);
            p.Employed = FocusContext.AsInt(row["employed"]);
            p.FreeField = FocusContext.AsInt(row["freeField"]);
            p.Habitability = FocusContext.AsFloat(row["_habitability"] ?? row["habitability"]);
            p.Defense = FocusContext.AsInt(row["defense"]);
            p.Raw = row;

            // The server stores the new level at start; a finished build = working type gone or replaced.
            var working = FocusContext.AsLong(row["working"]) > Core.Vfx.FleetOrderGate.UnixNow()
                ? FocusContext.AsString(row["workingtype"])
                : string.Empty;
            if (_lastWork.TryGetValue(id, out var before) && !string.IsNullOrEmpty(before) && before != working)
                _completed.Add((p, before));
            _lastWork[id] = working;
            return true;
        }
    }
}
