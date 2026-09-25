using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

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

        /// <summary>
        /// Rewrite the whole queue (SetFleetOrderQueue): the server restarts it at step 0 and runs the first
        /// one if the ship is idle, so callers pass only the steps still to do (or the full loop).
        /// </summary>
        public static Task<ApiResult> Replace(FocusFleet fleet, JArray steps, bool loop) =>
            ActionJs.Get("SetFleetOrderQueue", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "queue", steps.ToString(Newtonsoft.Json.Formatting.None) },
                { "loop", loop ? "1" : "0" }
            });

        /// <summary>A step JSON pointing at another target (keeps any extra server fields).</summary>
        public static JObject Retarget(FocusQueueStep step, string type, int targetId)
        {
            var o = step.Raw != null ? (JObject)step.Raw.DeepClone() : new JObject();
            o["type"] = type;
            o["targetId"] = targetId;
            o.Remove("skipped");
            return o;
        }

        public static JObject ToJson(FocusQueueStep step)
        {
            if (step.Raw != null)
                return (JObject)step.Raw.DeepClone();
            var o = new JObject { ["type"] = step.Type };
            if (step.Type == "moveToSystem")
            {
                o["x"] = (int)step.X;
                o["y"] = (int)step.Y;
            }
            else
                o["targetId"] = step.TargetId;
            return o;
        }

        /// <summary>
        /// The step being carried out now. The server moves orderQueueIndex past a move as soon as it
        /// dispatches it, so while the ship travels the running step is the one before the index.
        /// </summary>
        public static int RunningStep(FocusFleet f)
        {
            var n = f.Queue.Count;
            if (n == 0)
                return 0;
            var idx = f.QueueIndex;
            if (f.IsMoving(FleetOrderGate.UnixNow()))
                idx = idx > 0 ? idx - 1 : f.QueueLoop ? n - 1 : 0;
            return Mathf.Clamp(idx, 0, n);
        }

        /// <summary>1 = running now, then run order (a loop wraps around).</summary>
        public static int RunRank(FocusFleet f, int step)
        {
            var n = f.Queue.Count;
            if (n == 0)
                return step + 1;
            var cur = Mathf.Clamp(RunningStep(f), 0, n - 1);
            return ((step - cur) % n + n) % n + 1;
        }

        public static bool TargetsPlanet(string type) =>
            type is "moveToPlanet" or "explorePlanet" or "depositCargo" or "withdrawCargo";

        public static bool TargetsAsteroid(string type) => type is "moveToAsteroid" or "harvestAsteroid";

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
