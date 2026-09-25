using System.Collections.Generic;
using Core.App;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>One recruitable ground troop (Academy) or static planetary defence (Defense Factory).</summary>
    public sealed class UnitDef
    {
        public string Type = string.Empty;
        public int Attack;
        public int Defense;
        /// <summary>Building gating it (academy / defenseFactory) and the level it needs.</summary>
        public string Building = string.Empty;
        public int Level;
        public float Mineral;
        public float Crystal;
        public float Biomass;
        /// <summary>Seconds per unit in a batch.</summary>
        public float Time;
    }

    public readonly struct UnitQuote
    {
        public readonly float Mineral;
        public readonly float Crystal;
        public readonly float Biomass;
        public readonly float Seconds;
        public readonly bool Affordable;
        /// <summary>Largest batch the planet's stock pays for (server cap 500).</summary>
        public readonly int MaxQty;

        public UnitQuote(float mineral, float crystal, float biomass, float seconds, bool affordable, int maxQty)
        {
            Mineral = mineral;
            Crystal = crystal;
            Biomass = biomass;
            Seconds = seconds;
            Affordable = affordable;
            MaxQty = maxQty;
        }
    }

    /// <summary>
    /// GetConfigs.troopstats / defensestats read as unit cards, with the server's batch rules
    /// (actionjs.php RecruitTroop / BuildDefenseUnit): qty clamped 1–500, cost × qty, one batch per planet
    /// at a time, duration = time × qty × (100 − (computer + 1)) / 100 (Helper.php CalculateBuildingTime).
    /// Species buildingCost traits apply server-side only, so costs are estimates like the building quotes.
    /// </summary>
    public static class TroopCatalog
    {
        public const int MaxBatch = 500;

        static List<UnitDef> _troops;
        static List<UnitDef> _defenses;
        static JObject _troopSrc;
        static JObject _defenseSrc;

        public static IReadOnlyList<UnitDef> Troops
        {
            get
            {
                if (_troops == null || _troopSrc != GameConfig.TroopStats)
                {
                    _troopSrc = GameConfig.TroopStats;
                    _troops = Parse(_troopSrc, "academy");
                }

                return _troops;
            }
        }

        public static IReadOnlyList<UnitDef> Defenses
        {
            get
            {
                if (_defenses == null || _defenseSrc != GameConfig.DefenseStats)
                {
                    _defenseSrc = GameConfig.DefenseStats;
                    _defenses = Parse(_defenseSrc, "defenseFactory");
                }

                return _defenses;
            }
        }

        static List<UnitDef> Parse(JObject src, string building)
        {
            var list = new List<UnitDef>();
            if (src == null)
                return list;
            foreach (var kv in src)
            {
                if (kv.Value is not JObject o)
                    continue;
                var def = new UnitDef
                {
                    Type = kv.Key,
                    Attack = FocusContext.AsInt(o["attack"]),
                    Defense = FocusContext.AsInt(o["defense"]),
                    Building = building,
                    Time = FocusContext.AsFloat(o["time"])
                };
                if (o["requiert"] is JObject req)
                {
                    foreach (var r in req)
                    {
                        def.Building = r.Key;
                        def.Level = FocusContext.AsInt(r.Value);
                        break;
                    }
                }

                if (o["cost"] is JObject cost)
                {
                    def.Mineral = FocusContext.AsFloat(cost["mineral"]);
                    def.Crystal = FocusContext.AsFloat(cost["crystal"]);
                    def.Biomass = FocusContext.AsFloat(cost["biomass"]);
                }

                list.Add(def);
            }

            list.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Defense.CompareTo(b.Defense));
            return list;
        }

        public static UnitQuote Quote(PlanetEconomy planet, UnitDef def, int qty, int computerLevel)
        {
            qty = Mathf.Clamp(qty, 1, MaxBatch);
            var m = def.Mineral * qty;
            var c = def.Crystal * qty;
            var b = def.Biomass * qty;
            var seconds = def.Time * qty * (100f - (computerLevel + 1)) / 100f;
            var affordable = planet != null && planet.Mineral >= m && planet.Crystal >= c && planet.Biomass >= b;
            var max = MaxBatch;
            if (planet != null)
            {
                if (def.Mineral > 0f) max = Mathf.Min(max, Mathf.FloorToInt(planet.Mineral / def.Mineral));
                if (def.Crystal > 0f) max = Mathf.Min(max, Mathf.FloorToInt(planet.Crystal / def.Crystal));
                if (def.Biomass > 0f) max = Mathf.Min(max, Mathf.FloorToInt(planet.Biomass / def.Biomass));
            }
            else
            {
                max = 0;
            }

            return new UnitQuote(m, c, b, seconds, affordable, Mathf.Max(0, max));
        }

        /// <summary>{type: qty} from a GetResource / GetPlanet troops[] or defenseUnits[] array.</summary>
        public static Dictionary<string, int> Counts(JToken rows)
        {
            var map = new Dictionary<string, int>();
            if (rows is not JArray arr)
                return map;
            foreach (var r in arr)
            {
                var type = FocusContext.AsString(r["type"]);
                var qty = FocusContext.AsInt(r["qty"]);
                if (string.IsNullOrEmpty(type) || qty <= 0)
                    continue;
                map.TryGetValue(type, out var n);
                map[type] = n + qty;
            }

            return map;
        }

        public static int Total(Dictionary<string, int> counts)
        {
            var n = 0;
            foreach (var kv in counts)
                n += kv.Value;
            return n;
        }

        /// <summary>Tier 0..n of a defence type (index in the level-sorted list) — drives platform size outside.</summary>
        public static int DefenseTier(string type)
        {
            var list = Defenses;
            for (var i = 0; i < list.Count; i++)
                if (list[i].Type == type)
                    return i;
            return 0;
        }

        /// <summary>
        /// Web _renderBaseMilitaire: an Academy / Defense Factory level counts only once built — the level is
        /// incremented as soon as the upgrade is queued, so the one in construction is subtracted.
        /// </summary>
        public static int BuiltLevel(PlanetEconomy planet, string building)
        {
            if (planet == null)
                return 0;
            var level = planet.Level(building);
            var raw = planet.Raw;
            if (raw != null && FocusContext.AsString(raw["workingtype"]) == building &&
                FocusContext.AsLong(raw["working"]) > FleetOrderGate.UnixNow())
                level--;
            return Mathf.Max(0, level);
        }
    }
}
