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
