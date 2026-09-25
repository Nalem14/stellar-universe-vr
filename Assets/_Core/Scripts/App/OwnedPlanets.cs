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
    /// lag a brand-new world; it is only the fallback if the fresh read fails.
    /// </summary>
    public static class OwnedPlanets
    {
        static readonly List<GalaxyCatalog.PlanetRef> Planets = new();
        static readonly HashSet<int> Ids = new();
        static int _forUser;
        static bool _fresh;

        public static IReadOnlyList<GalaxyCatalog.PlanetRef> All => Planets;

        public static bool Contains(int planetId) => Ids.Contains(planetId);

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
            _forUser = user;
            if (list == null)
                return;
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var p in list)
            {
                Planets.Add(p);
                Ids.Add(p.Id);
            }
        }

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
