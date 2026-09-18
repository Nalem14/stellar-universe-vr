using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>Cached galaxy points for holomap Galaxy mode (systems.x/y).</summary>
    public static class GalaxyCatalog
    {
        public struct Star
        {
            public int Id;
            public string Name;
            public float X;
            public float Y;
        }

        static readonly List<Star> Stars = new();
        static bool _loaded;

        public static IReadOnlyList<Star> All => Stars;

        public static bool TryGet(int systemId, out Star star)
        {
            for (var i = 0; i < Stars.Count; i++)
            {
                if (Stars[i].Id != systemId)
                    continue;
                star = Stars[i];
                return true;
            }

            star = default;
            return false;
        }

        public static void CollectNearest(int fromSystemId, int max, List<Star> into)
        {
            into.Clear();
            if (!TryGet(fromSystemId, out var origin) || Stars.Count == 0)
                return;
            // Linear scan — galaxy catalog is small enough for Quest.
            var scored = new List<(float d, Star s)>(Stars.Count);
            for (var i = 0; i < Stars.Count; i++)
            {
                var s = Stars[i];
                if (s.Id == fromSystemId)
                    continue;
                var dx = s.X - origin.X;
                var dy = s.Y - origin.Y;
                scored.Add((dx * dx + dy * dy, s));
            }

            scored.Sort((a, b) => a.d.CompareTo(b.d));
            var n = Mathf.Min(max, scored.Count);
            for (var i = 0; i < n; i++)
                into.Add(scored[i].s);
        }

        public static async Task EnsureLoaded()
        {
            if (_loaded && Stars.Count > 0)
                return;
            var result = await ActionJs.Get("GetSystems");
            if (!result.Ok)
                return;
            Stars.Clear();
            try
            {
                var root = JToken.Parse(result.Body);
                var arr = root as JArray ?? root["systems"] as JArray;
                if (arr == null)
                    return;
                foreach (var s in arr)
                {
                    Stars.Add(new Star
                    {
                        Id = FocusContext.AsInt(s["id"]),
                        Name = FocusContext.AsString(s["name"]),
                        X = FocusContext.AsFloat(s["x"]),
                        Y = FocusContext.AsFloat(s["y"])
                    });
                }

                _loaded = true;
            }
            catch
            {
                // Shape varies.
            }
        }
    }
}
