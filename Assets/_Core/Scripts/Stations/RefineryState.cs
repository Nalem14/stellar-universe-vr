using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;

namespace Core.Stations
{
    /// <summary>
    /// A planet's fuel refinery as the server reports it (GetRefinery, model/fuel.php): five stages, the stock, what
    /// it eats and makes, the empire's fuel quality. Orders: UpgradeRefinery / SpeedupRefinery. Shared by the refinery
    /// room and the fleet orders (fill-up).
    /// </summary>
    public sealed class RefineryState
    {
        public static readonly string[] Stages = { "culture", "nanobots", "catalysis", "cryo", "pump" };

        public sealed class Stage
        {
            public string Key;
            public int Level;
            public int Max;
            /// <summary>Next level: cost by resource, seconds, research needs; null at max.</summary>
            public Dictionary<string, int> NextCost;
            public int NextSeconds;
            public Dictionary<string, int> NextRequires;
            public bool AtMax => NextCost == null;
            public string NameKey => "refineryStage_" + Key;
            public string DescKey => "refineryStageDesc_" + Key;
        }

        public int PlanetId;
        public bool Built;
        public bool Unlocked;
        public float Fuel;
        public float Capacity;
        public float FuelPerHour;
        public float CrystalPerHour;
        public float BiomassPerHour;
        public float SpeedFactor = 1f;
        public float CostFactor = 1f;
        public int Catalysis;
        public float PumpPerMinute;
        public string WorkingStage;
        public long WorkingUntil;
        public long WorkingStart;
        public readonly Dictionary<string, Stage> ByKey = new();

        public bool Busy => !string.IsNullOrEmpty(WorkingStage) && WorkingUntil > Core.Vfx.FleetOrderGate.UnixNow();
        public float Fill => Capacity > 0f ? UnityEngine.Mathf.Clamp01(Fuel / Capacity) : 0f;
        public int Level(string stage) => ByKey.TryGetValue(stage, out var s) ? s.Level : 0;

        public static RefineryState Parse(string body)
        {
            if (string.IsNullOrEmpty(body) || !body.TrimStart().StartsWith("{"))
                return null;
            JObject o;
            try
            {
                o = JObject.Parse(body);
            }
            catch
            {
                return null;
            }

            var r = new RefineryState
            {
                PlanetId = FocusContext.AsInt(o["planet"]),
                Built = FocusContext.AsBool(o["built"]),
                Unlocked = FocusContext.AsBool(o["unlocked"]),
                Fuel = FocusContext.AsFloat(o["fuel"]),
                Capacity = FocusContext.AsFloat(o["capacity"]),
                PumpPerMinute = FocusContext.AsFloat(o["pumpPerMinute"]),
            };
            if (o["perHour"] is JObject ph)
            {
                r.FuelPerHour = FocusContext.AsFloat(ph["fuel"]);
                r.CrystalPerHour = FocusContext.AsFloat(ph["crystal"]);
                r.BiomassPerHour = FocusContext.AsFloat(ph["biomass"]);
            }

            if (o["quality"] is JObject q)
            {
                r.SpeedFactor = FocusContext.AsFloat(q["speedFactor"]);
                r.CostFactor = FocusContext.AsFloat(q["costFactor"]);
                r.Catalysis = FocusContext.AsInt(q["catalysis"]);
            }

            if (o["working"] is JObject w)
            {
                r.WorkingStage = FocusContext.AsString(w["stage"]);
                r.WorkingUntil = FocusContext.AsLong(w["until"]);
                r.WorkingStart = FocusContext.AsLong(w["start"]);
            }

            if (o["stages"] is JObject stages)
            {
                foreach (var key in Stages)
                {
                    if (stages[key] is not JObject s)
                        continue;
                    var st = new Stage { Key = key, Level = FocusContext.AsInt(s["level"]), Max = FocusContext.AsInt(s["max"]) };
                    if (s["next"] is JObject next)
                    {
                        st.NextSeconds = FocusContext.AsInt(next["time"]);
                        st.NextCost = new Dictionary<string, int>();
                        if (next["cost"] is JObject cost)
                            foreach (var p in cost.Properties())
                                st.NextCost[p.Name] = FocusContext.AsInt(p.Value);
                        st.NextRequires = new Dictionary<string, int>();
                        if (next["requiert"] is JObject req)
                            foreach (var p in req.Properties())
                                st.NextRequires[p.Name] = FocusContext.AsInt(p.Value);
                    }

                    r.ByKey[key] = st;
                }
            }

            return r;
        }

        static Dictionary<string, string> Q(int planetId) => new() { { "planet", planetId.ToString() } };

        /// <summary>GetRefinery: the refinery brought up to now (null on error).</summary>
        public static async Task<RefineryState> Fetch(int planetId)
        {
            if (planetId <= 0)
                return null;
            var r = await ActionJs.Get("GetRefinery", Q(planetId));
            return r.Ok ? Parse(r.Body) : null;
        }

        /// <summary>UpgradeRefinery: the reply carries the new state; the error is the server's (native key text).</summary>
        public static async Task<(RefineryState state, ApiResult result)> Upgrade(int planetId, string stage)
        {
            var q = Q(planetId);
            q["stage"] = stage;
            var r = await ActionJs.Get("UpgradeRefinery", q);
            return (r.Ok ? Parse(r.Body) : null, r);
        }

        public static async Task<(RefineryState state, ApiResult result)> Speedup(int planetId)
        {
            var r = await ActionJs.Get("SpeedupRefinery", Q(planetId));
            return (r.Ok ? Parse(r.Body) : null, r);
        }

        /// <summary>LoadFuel / UnloadFuel on a fleet parked at a world with a refinery (all that fits).</summary>
        public static Task<ApiResult> Transfer(int fleetId, bool load) =>
            ActionJs.Get(load ? "LoadFuel" : "UnloadFuel", new Dictionary<string, string> { { "fleet", fleetId.ToString() } });

        public static Task<ApiResult> SetFuelMode(int fleetId, bool auto) =>
            ActionJs.Get("SetFleetFuelMode", new Dictionary<string, string>
            {
                { "fleet", fleetId.ToString() },
                { "mode", auto ? "1" : "0" }
            });
    }
}
