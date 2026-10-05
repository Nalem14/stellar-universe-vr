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
        /// <summary>GetConfigs.fleet.sublightSpeedFactor: share of a hull's real speed kept at sub-light (0 = old servers).</summary>
        public static float SublightSpeedFactor { get; private set; }

        /// <summary>GetConfigs.fleet.sublightSpeedSoftCap / hyperspaceSpeedCap: the soft ceilings speeds tend to (0 = none).</summary>
        public static float SublightSpeedSoftCap { get; private set; }
        public static float HyperspaceSpeedCap { get; private set; }

        /// <summary>
        /// Server SublightSpeed(): max(min(speed, cap), speed × factor), then past the flat cap diminishing returns
        /// toward the soft cap — before the booster and the integer.
        /// </summary>
        public static float SublightSpeed(float speed)
        {
            var cap = SublightSpeedCap > 0f ? System.Math.Min(speed, SublightSpeedCap) : speed;
            var raw = System.Math.Max(cap, speed * SublightSpeedFactor);
            if (SublightSpeedCap > 0f && raw > SublightSpeedCap && SublightSpeedSoftCap > SublightSpeedCap)
                raw = System.Math.Max(SublightSpeedCap, SublightSpeedSoftCap * (1f - (float)System.Math.Exp(-raw / SublightSpeedSoftCap)));
            return raw;
        }

        /// <summary>Server HyperspaceSpeed(): the hull's speed with diminishing returns toward the hyperspace cap.</summary>
        public static float HyperspaceSpeed(float speed) =>
            HyperspaceSpeedCap > 0f ? HyperspaceSpeedCap * (1f - (float)System.Math.Exp(-speed / HyperspaceSpeedCap)) : speed;
        public static float HyperspaceCrystalPerDistance { get; private set; }
        /// <summary>
        /// Crystal per distance unit for a faster-than-1 sub-light trip (web f03197d); 0 = not exposed by
        /// GetConfigs yet — the quote then cannot price it and only the server's fallback notice tells.
        /// </summary>
        public static float SublightCrystalPerDistance { get; private set; }
        public static float TravelSecondsPerDistance { get; private set; }
        public static float TravelDurationMin { get; private set; }
        /// <summary>Docked fleets allowed per owned planet (FLEET.MAX_PER_PLANET).</summary>
        public static int AllowedFleetPerPlanet { get; private set; }
        /// <summary>GetConfigs.fleet.shipRecycleRefund: share of a module's build cost a scrapped hangar module
        /// gives back to its planet (DelShip); 1 until the server serves it.</summary>
        public static float ShipRecycleRefund { get; private set; } = 1f;
        /// <summary>GetConfigs.pirates: {minLevel, maxLevel, xpPerLevel, mineralPerLevel, crystalPerLevel,
        /// biomassPerLevel, novaPerLevel} — what a pirate level means (loadout and bounty); null until served.</summary>
        public static JObject Pirates { get; private set; }

        // GetConfigs.scanner (web 5021101): a ship with a DeepSpaceScanner / SensorArray sees foreign and pirate
        // fleets within baseRange + rangePerResearchLevel × radarTech (galaxy map units), beyond the systems we hold.
        public static float ScannerBaseRange { get; private set; }
        public static float ScannerRangePerLevel { get; private set; }

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
        /// <summary>GetConfigs.jobsPerLevel: citizens one level of a production building employs (log-scaled).</summary>
        public static JObject JobsPerLevel { get; private set; }
        /// <summary>GetConfigs.galaxy.gridEpoch: when the grid coordinates last changed meaning (0 = never); echoed as `grid`.</summary>
        public static long GridEpoch { get; private set; }
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
        /// <summary>GetConfigs.researchEffects (server $RESEARCH_EFFECTS): {tech: [{stat, perLevel, modules?, cap?, flat?}]} —
        /// what each level gives; null until the server serves it.</summary>
        public static JObject ResearchEffects { get; private set; }
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
                    SublightSpeedFactor = FocusContext.AsFloat(fleet["sublightSpeedFactor"]);
                    SublightSpeedSoftCap = FocusContext.AsFloat(fleet["sublightSpeedSoftCap"]);
                    HyperspaceSpeedCap = FocusContext.AsFloat(fleet["hyperspaceSpeedCap"]);
                    HyperspaceCrystalPerDistance = FocusContext.AsFloat(fleet["hyperspaceCrystalCostPerDistance"]);
                    SublightCrystalPerDistance = FocusContext.AsFloat(fleet["sublightCrystalCostPerDistance"]);
                    TravelSecondsPerDistance = FocusContext.AsFloat(fleet["systemTravelSecondsPerDistance"]);
                    TravelDurationMin = FocusContext.AsFloat(fleet["systemTravelDurationMin"]);
                    AllowedFleetPerPlanet = FocusContext.AsInt(fleet["allowedFleetPerPlanet"]);
                    if (fleet["shipRecycleRefund"] != null)
                        ShipRecycleRefund = System.Math.Max(0f, FocusContext.AsFloat(fleet["shipRecycleRefund"]));
                }

                Pirates = root["pirates"] as JObject;
                if (root["prlBond"] is JObject prl)
                {
                    PrlBaseRange = FocusContext.AsFloat(prl["baseRange"]);
                    PrlRangePerLevel = FocusContext.AsFloat(prl["rangePerResearchLevel"]);
                    PrlCrystalPerDistance = FocusContext.AsFloat(prl["crystalCostPerDistance"]);
                    PrlTransitSeconds = FocusContext.AsFloat(prl["transitTime"]);
                }

                if (root["scanner"] is JObject scanner)
                {
                    ScannerBaseRange = FocusContext.AsFloat(scanner["baseRange"]);
                    ScannerRangePerLevel = FocusContext.AsFloat(scanner["rangePerResearchLevel"]);
                }

                if (root["jumpgate"] is JObject gate)
                {
                    JumpgateCost = gate["jumpCost"] as JObject;
                    JumpgateCooldown = FocusContext.AsFloat(gate["cooldown"]);
                    JumpgateTransitSeconds = FocusContext.AsFloat(gate["transitTime"]);
                }

                Upgrade = root["upgrade"] as JObject;
                JobsPerLevel = root["jobsPerLevel"] as JObject;
                GridEpoch = FocusContext.AsLong((root["galaxy"] as JObject)?["gridEpoch"]);
                Factory = root["factory"] as JObject;
                Storage = root["storage"] as JObject;
                ShipStats = root["shipstats"] as JObject;
                JumpModuleRequirement = root["jumpModuleRequirement"] as JObject;
                Research = root["researchs"] as JObject;
                ResearchEffects = root["researchEffects"] as JObject;
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
