using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;

namespace Core.App
{
    /// <summary>
    /// The captain's planets, fresh from the database: one GetEmpirePlanets call (uncached, with systemid
    /// since web c94c803), read at boot and after a founding — never on a timer. GetSystems ownership can
    /// lag a brand-new world; it is only the fallback if the fresh read fails. Favourite worlds come first.
    /// </summary>
    public static class OwnedPlanets
    {
        static readonly List<GalaxyCatalog.PlanetRef> Planets = new();
        static readonly HashSet<int> Ids = new();
        static readonly HashSet<int> Systems = new();
        static int _forUser;
        static bool _fresh;

        public static IReadOnlyList<GalaxyCatalog.PlanetRef> All => Planets;

        /// <summary>The list was re-read (a world founded, conquered or lost).</summary>
        public static event System.Action Changed;

        public static bool Contains(int planetId) => Ids.Contains(planetId);

        /// <summary>One of our worlds is in <paramref name="systemId"/>.</summary>
        public static bool InSystem(int systemId) => Systems.Contains(systemId);

        /// <summary>First owned planet, preferring <paramref name="systemId"/> when given.</summary>
        public static bool TryFirst(int systemId, out GalaxyCatalog.PlanetRef planet)
        {
            foreach (var p in Planets)
            {
                if (systemId <= 0 || p.SystemId == systemId)
                {
                    planet = p;
                    return true;
                }
            }

            planet = default;
            return false;
        }

        public static async Task Refresh()
        {
            var auth = AuthManager.Ensure();
            var user = FocusContext.OwnedUserId();
            var empireId = FocusContext.AsInt(auth.Empire?["id"]);
            if (user <= 0 || empireId <= 0)
            {
                Set(null, user);
                return;
            }

            var list = await ActionJs.Get("GetEmpirePlanets", new Dictionary<string, string>
            {
                { "empire", empireId.ToString() }
            });
            var fresh = new List<GalaxyCatalog.PlanetRef>();
            if (list.Ok && TryArray(list.Body, out var rows))
            {
                foreach (var r in rows)
                {
                    var id = FocusContext.AsInt(r["id"]);
                    var sys = FocusContext.AsInt(r["systemid"]);
                    if (id > 0 && sys > 0)
                        fresh.Add(new GalaxyCatalog.PlanetRef
                        {
                            Id = id, SystemId = sys, UserId = user, Name = FocusContext.AsString(r["name"])
                        });
                }

                // A server without GetEmpirePlanets.systemid (before web c94c803) falls through to GetSystems.
                if (fresh.Count > 0 || rows.Count == 0)
                {
                    Set(fresh, user);
                    _fresh = true;
                    return;
                }
            }

            // Fresh read failed: GetSystems ownership (may lag up to an hour for new planets).
            await GalaxyCatalog.EnsureLoaded();
            GalaxyCatalog.CollectOwnedPlanets(user, fresh);
            Set(fresh, user);
            _fresh = false;
        }

        /// <summary>Refresh only when the account changed or the last read fell back to GetSystems.</summary>
        public static Task EnsureLoaded() =>
            _forUser == FocusContext.OwnedUserId() && _fresh ? Task.CompletedTask : Refresh();

        static void Set(List<GalaxyCatalog.PlanetRef> list, int user)
        {
            Planets.Clear();
            Ids.Clear();
            Systems.Clear();
            _forUser = user;
            if (list == null)
                return;
            foreach (var p in list)
            {
                Planets.Add(p);
                Ids.Add(p.Id);
                Systems.Add(p.SystemId);
            }

            Sort();
            Changed?.Invoke();
        }

        /// <summary>Favourites first (<see cref="PlanetFavorites"/>), then id order.</summary>
        static void Sort()
        {
            if (!_hooked)
            {
                _hooked = true;
                PlanetFavorites.Changed += Sort;
            }

            Planets.Sort((a, b) =>
            {
                var fa = PlanetFavorites.Contains(a.Id);
                var fb = PlanetFavorites.Contains(b.Id);
                return fa != fb ? (fa ? -1 : 1) : a.Id.CompareTo(b.Id);
            });
        }

        static bool _hooked;

        static bool TryArray(string body, out JArray rows)
        {
            rows = null;
            if (string.IsNullOrEmpty(body))
                return false;
            try
            {
                rows = JToken.Parse(body) as JArray;
                return rows != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
