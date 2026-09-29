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

        /// <summary>A moveToSystem step naming the star by id too (the server prefers it over x / y).</summary>
        public static Task<ApiResult> AddSystemStep(FocusFleet fleet, float x, float y, int systemId = 0)
        {
            var step = new JObject { ["type"] = "moveToSystem", ["x"] = (int)x, ["y"] = (int)y };
            if (systemId > 0)
                step["system"] = systemId;
            return Add(fleet, step);
        }

        /// <summary>
        /// "Go there, then work there" in one press: a moveToPlanet step followed by <paramref name="work"/>
        /// (depositCargo / withdrawCargo / explorePlanet, or null for the move alone). The move is always
        /// queued first — the server completes it at once when the ship is already in that orbit, and it keeps
        /// the work step on the right world whatever the steps before it do. Sent as the web's queue menus do:
        /// one AddFleetOrderStep per step, in order, stopping at the first refusal.
        /// </summary>
        public static async Task<ApiResult> AddPlanetChain(FocusFleet fleet, int planetId, string work = null)
        {
            var r = await AddPlanetStep(fleet, "moveToPlanet", planetId);
            if (!r.Ok || string.IsNullOrEmpty(work))
                return r;
            return await AddPlanetStep(fleet, work, planetId);
        }

        /// <summary>Rock field: moveToAsteroid, then harvestAsteroid when asked (web FleetOrdersPanel onQueueHarvest).</summary>
        public static async Task<ApiResult> AddAsteroidChain(FocusFleet fleet, int asteroidId, bool harvest)
        {
            var r = await AddAsteroidStep(fleet, "moveToAsteroid", asteroidId);
            if (!r.Ok || !harvest)
                return r;
            return await AddAsteroidStep(fleet, "harvestAsteroid", asteroidId);
        }

        /// <summary>Lectern label of a chain: "Add to queue : Go to planet → Deposit cargo".</summary>
        public static string ChainLabel(string firstType, string thenType = null)
        {
            var label = Trans.Get("addToQueue") + "  :  " + Trans.Get(StepKey(firstType));
            return string.IsNullOrEmpty(thenType) ? label : label + "  →  " + Trans.Get(StepKey(thenType));
        }

        /// <summary>The step the server will dispatch next (orderQueueIndex; a finished loop wraps to 0).</summary>
        public static int NextIndex(FocusFleet f)
        {
            var n = f.Queue.Count;
            var idx = Mathf.Max(0, f.QueueIndex);
            return idx >= n ? (f.QueueLoop && n > 0 ? 0 : n) : idx;
        }

        /// <summary>
        /// The step on screen as "running": <see cref="RunningStep"/>, and also — the server moving the index
        /// past a harvest / survey as soon as it starts it — the work step still going on before the index.
        /// </summary>
        public static int ActiveStep(FocusFleet f)
        {
            var n = f.Queue.Count;
            if (n == 0)
                return 0;
            var now = FleetOrderGate.UnixNow();
            var idx = f.QueueIndex;
            if (!f.IsMoving(now) && idx > 0 && idx <= n)
            {
                var prev = f.Queue[idx - 1].Type;
                if ((f.IsHarvesting(now) && prev == "harvestAsteroid") || (f.IsExploring(now) && prev == "explorePlanet"))
                    return idx - 1;
            }

            var run = RunningStep(f);
            return run >= n && f.QueueLoop ? 0 : run;
        }

        /// <summary>True when the first step of <see cref="RunOrder"/> is already under way (flight, harvest, survey).</summary>
        public static bool FirstUnderWay(FocusFleet f) =>
            f.Queue.Count > 0 && ActiveStep(f) != NextIndex(f);

        /// <summary>Queue index of each <see cref="RunOrder"/> entry.</summary>
        public static int IndexOfRun(FocusFleet f, int run)
        {
            var n = f.Queue.Count;
            var start = ActiveStep(f);
            return f.QueueLoop && n > 0 ? (start + run) % n : start + run;
        }

        /// <summary>
        /// The steps still to run, in run order (the one under way first): to the end of the queue, or — looping —
        /// the whole cycle rotated to start there.
        /// </summary>
        public static List<FocusQueueStep> RunOrder(FocusFleet f)
        {
            var list = new List<FocusQueueStep>();
            var n = f.Queue.Count;
            if (n == 0)
                return list;
            var cur = ActiveStep(f);
            if (!f.QueueLoop)
            {
                for (var i = cur; i < n; i++)
                    list.Add(f.Queue[i]);
                return list;
            }

            cur = Mathf.Clamp(cur, 0, n - 1);
            for (var k = 0; k < n; k++)
                list.Add(f.Queue[(cur + k) % n]);
            return list;
        }

        /// <summary>
        /// Swap two steps of the run order and write the queue back (SetFleetOrderQueue, which restarts at step 0
        /// and runs it at once if the ship is idle). A step already under way is not re-sent — its flight / harvest
        /// goes on and would otherwise run twice; looping, it closes the cycle instead. It cannot be swapped.
        /// </summary>
        public static Task<ApiResult> Swap(FocusFleet fleet, int runA, int runB)
        {
            var order = RunOrder(fleet);
            var under = FirstUnderWay(fleet);
            var min = under ? 1 : 0;
            if (runA < min || runB < min || runA >= order.Count || runB >= order.Count || runA == runB)
                return Task.FromResult(ApiResult.Fail(Trans.Get("vr.common.error")));
            (order[runA], order[runB]) = (order[runB], order[runA]);
            var steps = new JArray();
            for (var k = min; k < order.Count; k++)
                steps.Add(ToJson(order[k]));
            if (under && fleet.QueueLoop)
                steps.Add(ToJson(order[0]));
            return Replace(fleet, steps, fleet.QueueLoop);
        }

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
