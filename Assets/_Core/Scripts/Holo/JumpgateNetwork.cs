using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>One of our other Jumpgate worlds (GetJumpgateDestinations row + its system).</summary>
    public readonly struct JumpgateDest
    {
        public readonly int Id;
        public readonly string Name;
        public readonly int SystemId;

        public JumpgateDest(int id, string name, int systemId)
        {
            Id = id;
            Name = name;
            SystemId = systemId;
        }
    }

    /// <summary>Lectern payload: jump through the origin gate to this planet.</summary>
    public sealed class JumpChoice
    {
        public readonly JumpgateDest Dest;
        public JumpChoice(JumpgateDest dest) => Dest = dest;
    }

    /// <summary>
    /// The empire's private Jumpgate network (actionjs.php GetJumpgateDestinations / SendFleetToJumpgate,
    /// web objects/planet.js jumpFleet): a ship docked at one of our worlds with a Jumpgate folds to any
    /// other of our Jumpgate worlds, whatever the distance. Flat cost from the origin planet's stock, a
    /// cooldown on the origin gate (planets.jumpgateReadyAt), a short transit during which the ship is busy.
    /// Refusals mirror the server's checks in the same order so the lectern says why before sending.
    /// </summary>
    public static class JumpgateNetwork
    {
        const float CacheSeconds = 20f;

        static readonly Dictionary<int, (float at, List<JumpgateDest> list)> Cache = new();
        static readonly Dictionary<int, Task<List<JumpgateDest>>> Pending = new();

        /// <summary>A ship left through a gate: (fleet, origin planet, target planet).</summary>
        public static event Action<int, int, int> Jumped;

        /// <summary>The Jumpgate world this ship is docked at, or null (not docked, not ours, no gate).</summary>
        public static PlanetEconomy Origin(FocusFleet fleet)
        {
            var eco = EconomyService.Instance;
            if (fleet == null || eco == null || fleet.PlanetId <= 0 || fleet.IsMoving(FleetOrderGate.UnixNow()))
                return null;
            return eco.Planets.TryGetValue(fleet.PlanetId, out var p) && p.Level("jumpgate") > 0 ? p : null;
        }

        /// <summary>Seconds before the origin gate can fold again (0 = ready).</summary>
        public static long RechargeLeft(PlanetEconomy origin) =>
            origin == null ? 0 : Math.Max(0, FocusContext.AsLong(origin.Raw?["jumpgateReadyAt"]) - FleetOrderGate.UnixNow());

        public static bool TryCached(int planetId, out List<JumpgateDest> list)
        {
            list = null;
            return Cache.TryGetValue(planetId, out var c) && Time.unscaledTime - c.at < CacheSeconds && (list = c.list) != null;
        }

        /// <summary>Other gate worlds reachable from <paramref name="planetId"/> (cached a few seconds).</summary>
        public static Task<List<JumpgateDest>> Destinations(int planetId)
        {
            if (TryCached(planetId, out var hit))
                return Task.FromResult(hit);
            if (Pending.TryGetValue(planetId, out var running))
                return running;
            var task = Fetch(planetId);
            Pending[planetId] = task;
            return task;
        }

        static async Task<List<JumpgateDest>> Fetch(int planetId)
        {
            var list = new List<JumpgateDest>();
            try
            {
                var r = await ActionJs.Get("GetJumpgateDestinations", new Dictionary<string, string>
                {
                    { "planet", planetId.ToString() }
                });
                if (r.Ok && !string.IsNullOrEmpty(r.Body))
                {
                    try
                    {
                        if (JToken.Parse(r.Body) is JArray rows)
                            foreach (var row in rows)
                            {
                                var id = FocusContext.AsInt(row["id"]);
                                if (id > 0)
                                    list.Add(new JumpgateDest(id, FocusContext.AsString(row["name"]), SystemOf(id)));
                            }
                    }
                    catch
                    {
                        // Unexpected body: no destinations offered.
                    }
                }

                Cache[planetId] = (Time.unscaledTime, list);
                return list;
            }
            finally
            {
                Pending.Remove(planetId);
            }
        }

        static int SystemOf(int planetId)
        {
            var eco = EconomyService.Instance;
            if (eco != null && eco.Planets.TryGetValue(planetId, out var p) && p.SystemId > 0)
                return p.SystemId;
            foreach (var o in OwnedPlanets.All)
                if (o.Id == planetId)
                    return o.SystemId;
            return 0;
        }

        /// <summary>Destinations matching a table target: that planet, or our gate worlds in that system.</summary>
        public static void Matching(List<JumpgateDest> all, HoloToken target, List<JumpgateDest> into)
        {
            into.Clear();
            if (all == null || target == null)
                return;
            foreach (var d in all)
                if (target.Kind == HoloTokenKind.Planet ? d.Id == target.Id
                    : target.Kind == HoloTokenKind.System && d.SystemId > 0 && d.SystemId == target.Id)
                    into.Add(d);
        }

        /// <summary>Why the server would refuse this jump now (native key), or null.</summary>
        public static string BlockKey(FocusFleet fleet, PlanetEconomy origin)
        {
            if (fleet == null || origin == null)
                return "noJumpgate";
            if (fleet.IsInBattle)
                return "fleetAlreadyInBattle";
            if (!fleet.CanIssueMove(FleetOrderGate.UnixNow()))
                return FleetOrderGate.BusyKey(fleet);
            if (RechargeLeft(origin) > 0)
                return "jumpgateRecharging";
            if (GameConfig.JumpgateCost != null)
                foreach (var kv in GameConfig.JumpgateCost)
                    if (FocusContext.AsFloat(origin.Raw?[kv.Key]) < FocusContext.AsFloat(kv.Value))
                        return "notEnoughRessource";
            return null;
        }

        /// <summary>"3 000 Mineral · 4 000 Crystal" from GetConfigs.jumpgate, or empty when unknown.</summary>
        public static string CostText()
        {
            if (GameConfig.JumpgateCost == null)
                return string.Empty;
            var parts = new List<string>();
            foreach (var kv in GameConfig.JumpgateCost)
            {
                var n = FocusContext.AsFloat(kv.Value);
                if (n > 0f)
                    parts.Add(n.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " " + Trans.Get(kv.Key + "Resource"));
            }

            return string.Join("  ·  ", parts);
        }

        /// <summary>Lectern option for one destination: name, flat cost, or the refusal (recharge time).</summary>
        public static OrderConsole.Option Option(FocusFleet fleet, PlanetEconomy origin, JumpgateDest dest)
        {
            var head = Trans.Get("jumpgate") + "  →  " + (string.IsNullOrEmpty(dest.Name) ? "#" + dest.Id : dest.Name);
            var block = BlockKey(fleet, origin);
            if (block == "jumpgateRecharging")
                return new OrderConsole.Option(head + "  ·  " + TravelPlanner.TimeText(RechargeLeft(origin)), false,
                    UiKit.Danger, null);
            if (block != null)
                return new OrderConsole.Option(head + "  ·  " + Trans.Get(block), false, UiKit.Danger, null);
            var cost = CostText();
            return new OrderConsole.Option(cost.Length > 0 ? head + "  ·  " + cost : head, true, JumpTint,
                new JumpChoice(dest));
        }

        /// <summary>Folded-space violet: the gate's colour on the table, the lectern and outside.</summary>
        public static readonly Color JumpTint = new(0.66f, 0.5f, 1f, 1f);

        public static async Task<ApiResult> Send(FocusFleet fleet, JumpgateDest dest)
        {
            var origin = fleet.PlanetId;
            var result = await ActionJs.Get("SendFleetToJumpgate", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "targetPlanet", dest.Id.ToString() }
            });
            if (result.Ok)
            {
                Cache.Remove(origin);
                Jumped?.Invoke(fleet.Id, origin, dest.Id);
                var eco = EconomyService.Instance;
                if (eco != null)
                    AsyncTap.Run(eco.RefreshNow());
            }

            return result;
        }
    }
}
