using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.App
{
    /// <summary>
    /// Balancing the client needs to quote orders before sending them, straight from GetConfigs
    /// (server config.production.php, overridden by DB). Never hard-code these values.
    /// <see cref="Loaded"/> is false until the boot read succeeds; quotes must then say "unknown".
    /// </summary>
    public static class GameConfig
    {
        public static bool Loaded { get; private set; }

        // GetConfigs.fleet
        public static float SublightSpeedCap { get; private set; }
        public static float HyperspaceCrystalPerDistance { get; private set; }
        public static float TravelSecondsPerDistance { get; private set; }
        public static float TravelDurationMin { get; private set; }
        /// <summary>Sum of module <c>size</c> allowed on one hull (FLEET.MAX_SIZE).</summary>
        public static int MaxFleetSize { get; private set; }
        /// <summary>Docked fleets allowed per owned planet (FLEET.MAX_PER_PLANET).</summary>
        public static int AllowedFleetPerPlanet { get; private set; }

        // GetConfigs.prlBond
        public static float PrlBaseRange { get; private set; }
        public static float PrlRangePerLevel { get; private set; }
        public static float PrlCrystalPerDistance { get; private set; }
        public static float PrlTransitSeconds { get; private set; }

        // GetConfigs.jumpgate (server $JUMPGATE) — absent until the server exposes it: quotes then omit the cost.
        /// <summary>Flat cost per jump, drawn from the origin planet's stock: {mineral: n, crystal: n}.</summary>
        public static JObject JumpgateCost { get; private set; }
        public static float JumpgateCooldown { get; private set; }
        public static float JumpgateTransitSeconds { get; private set; }

        /// <summary>GetConfigs.upgrade: {time:{type:{time}}, energy:{type:n}, cost:{type:{res:n}}} — per level.</summary>
        public static JObject Upgrade { get; private set; }
        /// <summary>GetConfigs.factory (production per level) and .storage (warehouse multipliers).</summary>
        public static JObject Factory { get; private set; }
        public static JObject Storage { get; private set; }
        /// <summary>GetConfigs.shipstats: module type → {armor, shield, damage, speed, cargo, size, crystalUsage,
        /// troopCargo?, requiert:{orbitShipyard|academy|research: lvl}, cost:{mineral,crystal}, time}.</summary>
        public static JObject ShipStats { get; private set; }
        /// <summary>GetConfigs.jumpModuleRequirement: {modulesPerJumpModule} (hyperspace / PRL jump ratio).</summary>
        public static JObject JumpModuleRequirement { get; private set; }
        /// <summary>GetConfigs.researchs (server $RESEARCH): {tech: {requiert:{researchLab|tech: lvl}, time, cost:{researchPoints}, maxLevel?}}.</summary>
        public static JObject Research { get; private set; }
        /// <summary>GetConfigs.troopstats / defensestats: {type: {requiert, cost, time, …}}.</summary>
        public static JObject TroopStats { get; private set; }
        public static JObject DefenseStats { get; private set; }

        /// <summary>GetConfigs.languages: [{code, native, label, html, font}] —
        /// every language the game ships (server registry, include/languages.php).
        /// Empty until the boot read succeeds: the UI then falls back to the
        /// codes Trans knows about.</summary>
        public static JArray Languages { get; private set; }
        /// <summary>Language codes in display order (empty until loaded).</summary>
        public static List<string> LanguageCodes
        {
            get
            {
                var codes = new List<string>();
                if (Languages != null)
                    foreach (var entry in Languages)
                        if (entry["code"]?.ToString() is string code && !string.IsNullOrEmpty(code))
                            codes.Add(code);
                return codes;
            }
        }
        /// <summary>Native name of a language ("Deutsch", "한국어"), or the code
        /// itself when the registry has not arrived yet.</summary>
        public static string LanguageNativeName(string code)
        {
            if (Languages != null)
                foreach (var entry in Languages)
                    if (entry["code"]?.ToString() == code)
                        return entry["native"]?.ToString() ?? code;
            return code;
        }
        /// <summary>Font-stack hint for a language: "latin" (shipped face) or
        /// "cjk" (needs a fallback font asset).</summary>
        public static string LanguageFontHint(string code)
        {
            if (Languages != null)
                foreach (var entry in Languages)
                    if (entry["code"]?.ToString() == code)
                        return entry["font"]?.ToString() ?? "latin";
            return "latin";
        }

        public static void Ingest(string configsBody)
        {
            if (string.IsNullOrEmpty(configsBody))
                return;
            try
            {
                var root = JObject.Parse(configsBody);
                Languages = root["languages"] as JArray;
                if (root["fleet"] is JObject fleet)
                {
                    SublightSpeedCap = FocusContext.AsFloat(fleet["sublightSpeedCap"]);
                    HyperspaceCrystalPerDistance = FocusContext.AsFloat(fleet["hyperspaceCrystalCostPerDistance"]);
                    TravelSecondsPerDistance = FocusContext.AsFloat(fleet["systemTravelSecondsPerDistance"]);
                    TravelDurationMin = FocusContext.AsFloat(fleet["systemTravelDurationMin"]);
                    MaxFleetSize = FocusContext.AsInt(fleet["maxFleetSize"]);
                    AllowedFleetPerPlanet = FocusContext.AsInt(fleet["allowedFleetPerPlanet"]);
                }

                if (root["prlBond"] is JObject prl)
                {
                    PrlBaseRange = FocusContext.AsFloat(prl["baseRange"]);
                    PrlRangePerLevel = FocusContext.AsFloat(prl["rangePerResearchLevel"]);
                    PrlCrystalPerDistance = FocusContext.AsFloat(prl["crystalCostPerDistance"]);
                    PrlTransitSeconds = FocusContext.AsFloat(prl["transitTime"]);
                }

                if (root["jumpgate"] is JObject gate)
                {
                    JumpgateCost = gate["jumpCost"] as JObject;
                    JumpgateCooldown = FocusContext.AsFloat(gate["cooldown"]);
                    JumpgateTransitSeconds = FocusContext.AsFloat(gate["transitTime"]);
                }

                Upgrade = root["upgrade"] as JObject;
                Factory = root["factory"] as JObject;
                Storage = root["storage"] as JObject;
                ShipStats = root["shipstats"] as JObject;
                JumpModuleRequirement = root["jumpModuleRequirement"] as JObject;
                Research = root["researchs"] as JObject;
                TroopStats = root["troopstats"] as JObject;
                DefenseStats = root["defensestats"] as JObject;

                Loaded = TravelSecondsPerDistance > 0f;
            }
            catch
            {
                // Shape varies; quotes stay unavailable rather than guessed.
            }
        }
    }
}
