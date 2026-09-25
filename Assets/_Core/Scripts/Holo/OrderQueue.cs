using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;

namespace Core.Holo
{
    /// <summary>
    /// A ship's server order queue (automation: go, harvest, deposit, loop — model/fleet_queue.php).
    /// Step JSON matches what ProcessFleetQueue reads: targetId for planets / asteroids, x / y for
    /// moveToSystem (the server ignores targetX / targetY). Labels are rebuilt client-side from native keys.
    /// </summary>
    public static class OrderQueue
    {
        public static Task<ApiResult> AddPlanetStep(FocusFleet fleet, string type, int planetId) =>
            Add(fleet, new JObject { ["type"] = type, ["targetId"] = planetId });

        public static Task<ApiResult> AddAsteroidStep(FocusFleet fleet, string type, int asteroidId) =>
            Add(fleet, new JObject { ["type"] = type, ["targetId"] = asteroidId });

        public static Task<ApiResult> AddSystemStep(FocusFleet fleet, float x, float y) =>
            Add(fleet, new JObject { ["type"] = "moveToSystem", ["x"] = (int)x, ["y"] = (int)y });

        public static Task<ApiResult> Remove(FocusFleet fleet, int stepIndex) =>
            ActionJs.Get("RemoveFleetOrderStep", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "stepIndex", stepIndex.ToString(CultureInfo.InvariantCulture) }
            });

        public static Task<ApiResult> Clear(FocusFleet fleet) =>
            ActionJs.Get("ClearFleetOrderQueue", new Dictionary<string, string> { { "fleet", fleet.Id.ToString() } });

        public static Task<ApiResult> SetLoop(FocusFleet fleet, bool loop) =>
            ActionJs.Get("ToggleFleetQueueLoop", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "loop", loop ? "1" : "0" }
            });

        static Task<ApiResult> Add(FocusFleet fleet, JObject step) =>
            ActionJs.Get("AddFleetOrderStep", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "step", step.ToString(Newtonsoft.Json.Formatting.None) }
            });

        /// <summary>Native step key (stepMoveToPlanet …) as the web queue tab labels it.</summary>
        public static string StepKey(string type) => type switch
        {
            "moveToPlanet" => "stepMoveToPlanet",
            "moveToAsteroid" => "stepMoveToAsteroid",
            "moveToSystem" => "stepMoveToSystem",
            "harvestAsteroid" => "stepHarvestAsteroid",
            "depositCargo" => "stepDepositCargo",
            "withdrawCargo" => "stepWithdrawCargo",
            "explorePlanet" => "stepExplorePlanet",
            _ => "orderQueue"
        };

        public static string Describe(FocusQueueStep step, FocusContext focus)
        {
            var verb = Trans.Get(StepKey(step.Type));
            switch (step.Type)
            {
                case "moveToSystem":
                    return verb + "  " + GalaxyCatalog.Coordinates(step.X, step.Y);
                case "moveToAsteroid":
                case "harvestAsteroid":
                    return verb + "  #" + step.TargetId;
                case "moveToPlanet":
                case "explorePlanet":
                    var p = focus?.FindPlanet(step.TargetId);
                    return verb + "  " + (p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "#" + step.TargetId);
                default:
                    return verb;
            }
        }
    }
}
