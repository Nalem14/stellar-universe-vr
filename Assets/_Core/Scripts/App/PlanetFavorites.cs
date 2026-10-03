using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// The captain's favourite worlds. The headset keeps a copy (PlayerPrefs) so lists stay sorted before
    /// the next read; the server column <c>planets.is_favorite</c> is the shared list. A star calls
    /// TogglePlanetFavorite, and GetEmpirePlanets replaces the copy.
    /// </summary>
    public static class PlanetFavorites
    {
        const string KeyPrefix = "su.planetFavorites.";

        static readonly HashSet<int> Ids = new();
        static int _forUser = -1;
        static bool _uploadedLegacy;

        /// <summary>A world was starred or un-starred.</summary>
        public static event Action Changed;

        public static bool Contains(int planetId)
        {
            Load();
            return planetId > 0 && Ids.Contains(planetId);
        }

        public static void Toggle(int planetId)
        {
            if (planetId <= 0)
                return;
            Load();
            if (!Ids.Remove(planetId))
                Ids.Add(planetId);
            Save();
            Changed?.Invoke();
            _ = Push(planetId);
        }

        /// <summary>Replace the local copy with the planets this account owns. A column-less payload is ignored.</summary>
        public static void ApplyServer(JArray rows)
        {
            if (rows == null)
                return;
            Load();
            var saw = false;
            var serverOn = new HashSet<int>();
            foreach (var row in rows)
            {
                if (row["is_favorite"] == null)
                    continue;
                saw = true;
                var id = FocusContext.AsInt(row["id"]);
                if (id > 0 && FocusContext.AsInt(row["is_favorite"]) != 0)
                    serverOn.Add(id);
            }

            if (!saw)
                return;

            // Headset stars from before the column existed: upload them once, and keep them until the server has some.
            if (serverOn.Count == 0 && Ids.Count > 0)
            {
                if (!_uploadedLegacy)
                {
                    _uploadedLegacy = true;
                    foreach (var id in new List<int>(Ids))
                        _ = Push(id);
                }
                return;
            }

            Ids.Clear();
            foreach (var id in serverOn)
                Ids.Add(id);
            Save();
            Changed?.Invoke();
        }

        static async Task Push(int planetId)
        {
            var wantOn = Contains(planetId);
            var res = await ActionJs.Get("TogglePlanetFavorite", new Dictionary<string, string>
            {
                { "planet", planetId.ToString() }
            });
            // A later tap already changed the star: leave that one in charge.
            if (Contains(planetId) != wantOn)
                return;
            if (res.Ok && ServerOn(res) == wantOn)
                return;
            Load();
            if (wantOn)
                Ids.Remove(planetId);
            else
                Ids.Add(planetId);
            Save();
            Changed?.Invoke();
        }

        static bool ServerOn(ApiResult res)
        {
            try
            {
                return FocusContext.AsInt(JObject.Parse(res.Body)["is_favorite"]) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Load()
        {
            var user = FocusContext.OwnedUserId();
            if (user == _forUser)
                return;
            _forUser = user;
            _uploadedLegacy = false;
            Ids.Clear();
            if (user <= 0)
                return;
            var raw = PlayerPrefs.GetString(KeyPrefix + user, string.Empty);
            foreach (var part in raw.Split(','))
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                    Ids.Add(id);
        }

        static void Save()
        {
            if (_forUser <= 0)
                return;
            PlayerPrefs.SetString(KeyPrefix + _forUser, string.Join(",", Ids));
            PlayerPrefs.Save();
        }
    }
}
