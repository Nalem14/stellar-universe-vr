using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Stations;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>
    /// Finish a ship's current travel for Nova (web Helper.promptSpeedupFleetTravel → SpeedupFleetTravel):
    /// same price curve as every other speedup (free under a minute), quoted on the lectern, sent on Confirm.
    /// Refused before sending when the Nova balance is short, as the web does.
    /// </summary>
    public static class TravelSpeedup
    {
        public static long Remaining(FocusFleet fleet) =>
            fleet == null ? 0 : System.Math.Max(0, fleet.DestTime - FleetOrderGate.UnixNow());

        public static int Cost(FocusFleet fleet) => BuildingCatalog.SpeedupCost(Remaining(fleet));

        /// <summary>A ship of ours under way, not in battle: its travel can be finished now.</summary>
        public static bool Offered(FocusFleet fleet) =>
            fleet != null && !fleet.IsInBattle && Remaining(fleet) > 0 && fleet.IsOwnedBy(FocusContext.OwnedUserId());

        public static string Label(FocusFleet fleet)
        {
            var cost = Cost(fleet);
            return cost == 0
                ? Trans.Get("finishFree")
                : Trans.Get("speedupFleetTravel") + "  ·  " + ScreenKit.Num(cost) + " " + Trans.Get("nova");
        }

        static bool Confirmed(string body)
        {
            if (string.IsNullOrEmpty(body))
                return false;
            try
            {
                return Newtonsoft.Json.Linq.JToken.Parse(body) is Newtonsoft.Json.Linq.JObject o &&
                       o["ok"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)o["ok"];
            }
            catch
            {
                return false;
            }
        }

        static int Nova
        {
            get
            {
                var eco = EconomyService.Instance;
                if (eco != null && eco.Empire != null)
                    return eco.Nova;
                return FocusContext.AsInt(AuthManager.Ensure().Empire?["nova"]);
            }
        }

        /// <summary>
        /// Quote beside <paramref name="at"/> (or on the rim lectern), send on Confirm. Returns (sent, result);
        /// sent = false when cancelled or nothing to finish.
        /// </summary>
        public static async Task<(bool sent, ApiResult result)> AskAndSend(FocusFleet fleet, Vector3? at)
        {
            if (!Offered(fleet))
                return (false, default);
            var console = OrderConsole.Instance;
            var cost = Cost(fleet);
            var affordable = cost == 0 || Nova >= cost;
            var label = Label(fleet);
            if (!affordable)
                label += "  ·  " + Trans.Get("notEnoughNova") + " (" + ScreenKit.Num(Nova) + ")";
            var options = new List<OrderConsole.Option>
            {
                new(label, affordable, affordable ? UiKit.Amber : UiKit.Danger, "speedup")
            };
            var name = (string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name) + "  ·  " +
                       TravelPlanner.TimeText(Remaining(fleet));
            if (console != null)
            {
                var choice = at.HasValue ? await console.AskAt(at.Value, name, options) : await console.Ask(name, options);
                if (choice == null)
                    return (false, default);
            }

            // The quote may have gone stale while the lectern was open (arrived meanwhile).
            if (!Offered(fleet))
                return (false, default);
            var result = await ActionJs.Get("SpeedupFleetTravel", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() }
            });
            // Contract: JSON {ok, cost, nova, fleet}. Anything else (an empty body is what the server sends for
            // an action it does not know) is not a finished travel.
            if (result.Ok && !Confirmed(result.Body))
                result = ApiResult.Fail(Trans.Get("vr.common.error"));
            if (result.Ok && EconomyService.Instance != null)
                AsyncTap.Run(EconomyService.Instance.RefreshNow());
            return (true, result);
        }
    }
}
