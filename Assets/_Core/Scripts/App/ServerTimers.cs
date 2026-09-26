using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// The server's own progress of a running job (actionjs.php CheckBuildingQueue / CheckShipQueue /
    /// CheckResearchQueue): elapsed / total from planets.workingStart (buildings), empires.workingStart
    /// (research) or SHIPSTATS time (shipyard), with the server's fallback for legacy rows. Read on demand by
    /// a progress bar, then every 15 s while it is shown; between reads the bar advances at the rate the read
    /// implies (total = remaining / (1 − percent)). No read = null, the caller keeps its local estimate.
    /// </summary>
    public static class ServerTimers
    {
        const float Refresh = 15f;

        sealed class Entry
        {
            public float FetchedAt = -999f;
            public float Percent;
            public float Remaining;
            public bool Has;
            public bool Busy;
        }

        static readonly Dictionary<(string, int), Entry> Entries = new();

        public static float? Building(int planetId) => Progress("CheckBuildingQueue", planetId);
        public static float? Shipyard(int planetId) => Progress("CheckShipQueue", planetId);
        public static float? Research() => Progress("CheckResearchQueue", 0);

        /// <summary>Forget a job's read (it was sped up, cancelled or replaced).</summary>
        public static void Invalidate() => Entries.Clear();

        static float? Progress(string action, int planetId)
        {
            var key = (action, planetId);
            if (!Entries.TryGetValue(key, out var e))
                Entries[key] = e = new Entry();
            var age = Time.unscaledTime - e.FetchedAt;
            if (!e.Busy && age > Refresh)
                AsyncTap.Run(Fetch(action, planetId, e));
            if (!e.Has)
                return null;
            if (e.Percent >= 1f)
                return 1f;
            var total = e.Remaining / Mathf.Max(0.0001f, 1f - e.Percent);
            var left = e.Remaining - age;
            if (left < -2f)
            {
                // That job is over (the next one may already run): read again rather than show a full bar.
                e.Has = false;
                e.FetchedAt = -999f;
                return null;
            }
            return total <= 0f ? 1f : Mathf.Clamp01(1f - left / total);
        }

        static async Task Fetch(string action, int planetId, Entry e)
        {
            e.Busy = true;
            try
            {
                var q = planetId > 0 ? new Dictionary<string, string> { { "planet", planetId.ToString() } } : null;
                var r = await ActionJs.Get(action, q);
                e.FetchedAt = Time.unscaledTime;
                // Empty body = nothing running there.
                if (!r.Ok || string.IsNullOrEmpty(r.Body))
                {
                    e.Has = false;
                    return;
                }

                try
                {
                    var o = JObject.Parse(r.Body);
                    e.Percent = Mathf.Clamp01(FocusContext.AsFloat(o["percent"]) / 100f);
                    e.Remaining = Mathf.Max(0f, FocusContext.AsFloat(o["time"]));
                    e.Has = true;
                }
                catch
                {
                    e.Has = false;
                }
            }
            finally
            {
                e.Busy = false;
            }
        }
    }
}
