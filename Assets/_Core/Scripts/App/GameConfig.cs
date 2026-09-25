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

        // GetConfigs.prlBond
        public static float PrlBaseRange { get; private set; }
        public static float PrlRangePerLevel { get; private set; }
        public static float PrlCrystalPerDistance { get; private set; }
        public static float PrlTransitSeconds { get; private set; }

        /// <summary>GetConfigs.upgrade: {time:{type:{time}}, energy:{type:n}, cost:{type:{res:n}}} — per level.</summary>
        public static JObject Upgrade { get; private set; }
        /// <summary>GetConfigs.factory (production per level) and .storage (warehouse multipliers).</summary>
        public static JObject Factory { get; private set; }
        public static JObject Storage { get; private set; }

        public static void Ingest(string configsBody)
        {
            if (string.IsNullOrEmpty(configsBody))
                return;
            try
            {
                var root = JObject.Parse(configsBody);
                if (root["fleet"] is JObject fleet)
                {
                    SublightSpeedCap = FocusContext.AsFloat(fleet["sublightSpeedCap"]);
                    HyperspaceCrystalPerDistance = FocusContext.AsFloat(fleet["hyperspaceCrystalCostPerDistance"]);
                    TravelSecondsPerDistance = FocusContext.AsFloat(fleet["systemTravelSecondsPerDistance"]);
                    TravelDurationMin = FocusContext.AsFloat(fleet["systemTravelDurationMin"]);
                }

                if (root["prlBond"] is JObject prl)
                {
                    PrlBaseRange = FocusContext.AsFloat(prl["baseRange"]);
                    PrlRangePerLevel = FocusContext.AsFloat(prl["rangePerResearchLevel"]);
                    PrlCrystalPerDistance = FocusContext.AsFloat(prl["crystalCostPerDistance"]);
                    PrlTransitSeconds = FocusContext.AsFloat(prl["transitTime"]);
                }

                Upgrade = root["upgrade"] as JObject;
                Factory = root["factory"] as JObject;
                Storage = root["storage"] as JObject;

                Loaded = TravelSecondsPerDistance > 0f;
            }
            catch
            {
                // Shape varies; quotes stay unavailable rather than guessed.
            }
        }
    }
}
