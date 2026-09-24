using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Cached GetSystems: galaxy points for holomap Galaxy mode (systems.x/y) and every planet's owner,
    /// the same source the web uses for "my planets" (galaxyScene.planetsList filtered on userid).
    /// </summary>
    public static class GalaxyCatalog
    {
        public struct Star
        {
            public int Id;
            public string Name;
            public float X;
            public float Y;
        }

        public struct PlanetRef
        {
            public int Id;
            public int SystemId;
            public int UserId;
            public string Name;
        }

        static readonly List<Star> Stars = new();
        static readonly List<PlanetRef> Planets = new();
        static bool _loaded;

        public static IReadOnlyList<Star> All => Stars;

        /// <summary>Planets owned by <paramref name="userId"/> across the galaxy.</summary>
        public static void CollectOwnedPlanets(int userId, List<PlanetRef> into)
        {
            into.Clear();
            if (userId <= 0)
                return;
            for (var i = 0; i < Planets.Count; i++)
            {
                if (Planets[i].UserId == userId)
                    into.Add(Planets[i]);
            }
        }

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

        /// <param name="force">Re-read ownership (after Colonize / conquest).</param>
        public static async Task EnsureLoaded(bool force = false)
        {
            if (!force && _loaded && Stars.Count > 0)
                return;
            var result = await ActionJs.Get("GetSystems");
            if (!result.Ok)
                return;
            Stars.Clear();
            Planets.Clear();
            try
            {
                var root = JToken.Parse(result.Body);
                var arr = root as JArray ?? root["systems"] as JArray;
                if (arr == null)
                    return;
                foreach (var s in arr)
                {
                    var systemId = FocusContext.AsInt(s["id"]);
                    Stars.Add(new Star
                    {
                        Id = systemId,
                        Name = FocusContext.AsString(s["name"]),
                        X = FocusContext.AsFloat(s["x"]),
                        Y = FocusContext.AsFloat(s["y"])
                    });

                    if (s["planets"] is JArray planetArr)
                    {
                        foreach (var p in planetArr)
                            AddPlanet(p, systemId, 0);
                    }
                    else if (s["planets"] is JObject planetMap)
                    {
                        foreach (var prop in planetMap.Properties())
                            AddPlanet(prop.Value, systemId, FocusContext.AsInt(prop.Name));
                    }
                }

                _loaded = true;
            }
            catch
            {
                // Shape varies.
            }
        }

        static void AddPlanet(JToken p, int systemId, int fallbackId)
        {
            if (p == null || p.Type != JTokenType.Object)
                return;
            var id = FocusContext.AsInt(p["id"]);
            if (id == 0)
                id = fallbackId;
            if (id <= 0)
                return;
            var sys = FocusContext.AsInt(p["systemid"]);
            Planets.Add(new PlanetRef
            {
                Id = id,
                SystemId = sys > 0 ? sys : systemId,
                UserId = FocusContext.AsInt(p["userid"]),
                Name = FocusContext.AsString(p["name"])
            });
        }
    }
}
