using System.Collections.Generic;
using Core.Utils;
using Newtonsoft.Json.Linq;

namespace Core.App
{
    /// <summary>
    /// Active Nova boosters (GetShopData activeBoosters) the client needs for its estimates. Read from any
    /// GetShopData answer (the quarters shop included) and once at bridge boot. Time boosters are factors on
    /// durations: move_speed 0.5 = trips take half as long (the server divides the fleet speed by it).
    /// </summary>
    public static class Boosters
    {
        static readonly Dictionary<string, (float mult, long until)> Active = new();
        static bool _hooked;

        public static void Ensure()
        {
            if (_hooked)
                return;
            _hooked = true;
            ActionJs.Succeeded += OnSucceeded;
        }

        public static async System.Threading.Tasks.Task Refresh()
        {
            Ensure();
            await ActionJs.Get("GetShopData");
        }

        /// <summary>Multiplier of an active booster, else 1.</summary>
        public static float Get(string type)
        {
            if (!Active.TryGetValue(type, out var b) || b.until <= Core.Vfx.FleetOrderGate.UnixNow())
                return 1f;
            return b.mult > 0f ? b.mult : 1f;
        }

        /// <summary>Trip duration factor (move_speed): 0.5 = twice as fast.</summary>
        public static float MoveTimeFactor => Get("move_speed");

        static void OnSucceeded(string action, IDictionary<string, string> query, ApiResult result)
        {
            if (action != "GetShopData")
                return;
            try
            {
                if (JToken.Parse(result.Body)["activeBoosters"] is not JArray arr)
                    return;
                Active.Clear();
                foreach (var b in arr)
                {
                    var type = FocusContext.AsString(b["type"]);
                    if (!string.IsNullOrEmpty(type))
                        Active[type] = (FocusContext.AsFloat(b["multiplier"]), FocusContext.AsLong(b["expires_at"]));
                }
            }
            catch
            {
                // Shape varies: keep the last known boosters.
            }
        }
    }
}
