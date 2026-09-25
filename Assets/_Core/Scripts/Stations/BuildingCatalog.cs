using Core.App;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>A constructible planet building (web scenes/planet.js BUILDINGS_META, same order).</summary>
    public readonly struct BuildingDef
    {
        public readonly string Type;
        public readonly Color Accent;
        /// <summary>Empire research field the server requires (actionjs.php UpgradeBuilding), or null.</summary>
        public readonly string ResearchKey;
        public readonly int ResearchLevel;
        /// <summary>0 = uncapped; stargate / jumpgate stop at 1.</summary>
        public readonly int MaxLevel;

        public BuildingDef(string type, Color accent, string researchKey = null, int researchLevel = 0, int maxLevel = 0)
        {
            Type = type;
            Accent = accent;
            ResearchKey = researchKey;
            ResearchLevel = researchLevel;
            MaxLevel = maxLevel;
        }

        /// <summary>Native key = building type (fr.json "mineralMine": "Mine minérale"…).</summary>
        public string NameKey => Type;
        /// <summary>Native desc key from GetTranslations (buildingDesc_&lt;type&gt;).</summary>
        public string DescKey => "buildingDesc_" + Type;
    }

    /// <summary>What the next level of a building costs, quoted like the server will charge it.</summary>
    public struct BuildingQuote
    {
        public int Level;
        public int TargetLevel;
        public int Mineral;
        public int Crystal;
        public int Biomass;
        public float Seconds;
        /// <summary>Native / vr key of why it cannot be ordered now, or null.</summary>
        public string BlockKey;
        public bool Affordable;
        public bool UnderConstruction;
    }

    /// <summary>
    /// Buildings and their pricing. The server has no building list (the web hard-codes it); costs and times
    /// come from GetConfigs.upgrade, applied as actionjs.php UpgradeBuilding does:
    /// target = level + queued of that type + 1; cost = base × target; time = base × target × (100 − (computer + 1)) / 100, ≥ 5 s.
    /// Species traits (± buildingCost / buildingTime %) are server-side only, so quotes are shown as estimates.
    /// </summary>
    public static class BuildingCatalog
    {
        static readonly Color Mineral = new(0.85f, 0.56f, 0.36f, 1f);
        static readonly Color Crystal = new(0.66f, 0.5f, 1f, 1f);
        static readonly Color Bio = new(0.5f, 0.8f, 0.38f, 1f);
        static readonly Color Energy = new(0.95f, 0.85f, 0.3f, 1f);
        static readonly Color Science = new(0.3f, 0.78f, 0.8f, 1f);
        static readonly Color Military = new(0.35f, 0.65f, 0.9f, 1f);
        static readonly Color Danger = new(0.9f, 0.38f, 0.38f, 1f);
        static readonly Color Gate = new(0.76f, 0.4f, 0.95f, 1f);

        public static readonly BuildingDef[] All =
        {
            new("home", new Color(0.92f, 0.76f, 0.3f, 1f)),
            new("farm", Bio),
            new("mineralMine", Mineral),
            new("crystalMine", Crystal),
            new("mineralWarehouse", Mineral),
            new("crystalWarehouse", Crystal),
            new("biomassWarehouse", Bio),
            new("solarPlant", Energy),
            new("nuclearPlant", new Color(0.78f, 0.88f, 0.3f, 1f), "energy", 3),
            new("researchLab", Science),
            new("orbitShipyard", new Color(1f, 0.5f, 0.38f, 1f)),
            new("academy", Military, "weapon", 1),
            new("defenseFactory", Danger, "shield", 1),
            new("stargate", Gate, "stargateDiscovery", 1, 1),
            new("jumpgate", new Color(0.2f, 0.82f, 0.88f, 1f), "spatialFolding", 1, 1)
        };

        public static bool TryGet(string type, out BuildingDef def)
        {
            foreach (var d in All)
            {
                if (d.Type != type)
                    continue;
                def = d;
                return true;
            }

            def = default;
            return false;
        }

        /// <summary>Queued rows (planet_building_queue) of the planet: {id, buildingtype, target_level, duration}.</summary>
        public static JArray Queue(PlanetEconomy planet) => planet?.Raw?["buildingQueue"] as JArray;

        public static int MaxQueue(PlanetEconomy planet)
        {
            var n = FocusContext.AsInt(planet?.Raw?["maxQueue"]);
            return n > 0 ? n : 2;
        }

        public static string ActiveType(PlanetEconomy planet)
        {
            var t = FocusContext.AsString(planet?.Raw?["workingtype"]);
            return FocusContext.AsLong(planet?.Raw?["working"]) > Core.Vfx.FleetOrderGate.UnixNow() ? t : string.Empty;
        }

        public static long ActiveEnd(PlanetEconomy planet) => FocusContext.AsLong(planet?.Raw?["working"]);

        /// <summary>Active build + queued rows (the server counts both against maxQueue).</summary>
        public static int Occupied(PlanetEconomy planet)
        {
            var q = Queue(planet);
            return (string.IsNullOrEmpty(ActiveType(planet)) ? 0 : 1) + (q?.Count ?? 0);
        }

        public static BuildingQuote Quote(PlanetEconomy planet, in BuildingDef def, EconomyService economy)
        {
            var q = new BuildingQuote();
            var level = planet?.Level(def.Type) ?? 0;
            // While building, the server already stored the new level (production uses level − 1).
            q.UnderConstruction = ActiveType(planet) == def.Type;
            q.Level = q.UnderConstruction ? Mathf.Max(0, level - 1) : level;

            var queued = 0;
            var rows = Queue(planet);
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (FocusContext.AsString(row["buildingtype"]) == def.Type)
                        queued++;
                }
            }

            q.TargetLevel = level + queued + 1;
            var upgrade = GameConfig.Upgrade;
            var cost = upgrade?["cost"]?[def.Type];
            q.Mineral = FocusContext.AsInt(cost?["mineral"]) * q.TargetLevel;
            q.Crystal = FocusContext.AsInt(cost?["crystal"]) * q.TargetLevel;
            q.Biomass = FocusContext.AsInt(cost?["biomass"]) * q.TargetLevel;
            var computer = economy != null ? economy.ResearchLevel("computer") : 0;
            var baseTime = FocusContext.AsFloat(upgrade?["time"]?[def.Type]?["time"]);
            q.Seconds = Mathf.Max(5f, baseTime * q.TargetLevel * (100f - (computer + 1)) / 100f);

            q.Affordable = planet != null && planet.Mineral >= q.Mineral && planet.Crystal >= q.Crystal &&
                           planet.Biomass >= q.Biomass;

            if (planet == null)
                q.BlockKey = "vr.common.error";
            else if (def.MaxLevel > 0 && q.TargetLevel > def.MaxLevel)
                q.BlockKey = def.Type == "stargate" ? "stargateMaxLevel" : "jumpgateMaxLevel";
            else if (def.ResearchKey != null && (economy?.ResearchLevel(def.ResearchKey) ?? 0) < def.ResearchLevel)
                q.BlockKey = "notEnoughtResearchLevel";
            else if (Occupied(planet) >= MaxQueue(planet))
                q.BlockKey = "queueFull";
            else if (!q.Affordable)
                q.BlockKey = "notEnoughRessource";
            else if (planet.FreeField <= 0)
                q.BlockKey = "notEnoughField";
            return q;
        }

        /// <summary>Nova to finish the active build now (Helper.php CalculateNovaSpeedupCost).</summary>
        public static int SpeedupCost(float remainingSeconds)
        {
            if (remainingSeconds <= 60f)
                return 0;
            return Mathf.Max(10, Mathf.CeilToInt(6f * Mathf.Pow(remainingSeconds / 60f, 0.82f)));
        }
    }
}
