using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// The captain's favourite worlds, per account on this headset (PlayerPrefs, like the inhabited view): they
    /// head every planet list (<see cref="OwnedPlanets"/> sorts them first). The server keeps no such list.
    /// </summary>
    public static class PlanetFavorites
    {
        const string KeyPrefix = "su.planetFavorites.";

        static readonly HashSet<int> Ids = new();
        static int _forUser = -1;

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
        }

        static void Load()
        {
            var user = FocusContext.OwnedUserId();
            if (user == _forUser)
                return;
            _forUser = user;
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
