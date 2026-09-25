using UnityEngine;

namespace Core.App
{
    public enum BridgeViewKind
    {
        None = 0,
        Ship = 1,
        Planet = 2
    }

    /// <summary>
    /// Local persistence of inhabited entity. Server only stores system (web god-cam);
    /// VR needs ship (fleets.id) or planet→orbital station.
    /// </summary>
    public static class BridgeViewAnchor
    {
        const string KindKey = "su.view.kind";
        const string EntityKey = "su.view.entityId";
        const string SystemKey = "su.view.systemId";
        /// <summary>Account that saved the view: another login / a fresh sign-up never inherits it.</summary>
        const string UserKey = "su.view.userId";

        static bool OwnedByCurrentUser()
        {
            var uid = FocusContext.OwnedUserId();
            return uid > 0 && PlayerPrefs.GetInt(UserKey, 0) == uid;
        }

        public static BridgeViewKind Kind
        {
            get
            {
                if (!OwnedByCurrentUser())
                    return BridgeViewKind.None;
                var raw = PlayerPrefs.GetString(KindKey, string.Empty);
                if (raw == "ship")
                    return BridgeViewKind.Ship;
                if (raw == "planet")
                    return BridgeViewKind.Planet;
                return BridgeViewKind.None;
            }
        }

        public static int EntityId => PlayerPrefs.GetInt(EntityKey, 0);
        public static int SystemId => PlayerPrefs.GetInt(SystemKey, 0);

        public static void SaveShip(int fleetId, int systemId)
        {
            if (fleetId <= 0)
                return;
            PlayerPrefs.SetString(KindKey, "ship");
            PlayerPrefs.SetInt(UserKey, FocusContext.OwnedUserId());
            PlayerPrefs.SetInt(EntityKey, fleetId);
            PlayerPrefs.SetInt(SystemKey, systemId);
            PlayerPrefs.Save();
        }

        public static void SavePlanet(int planetId, int systemId)
        {
            if (planetId <= 0)
                return;
            PlayerPrefs.SetString(KindKey, "planet");
            PlayerPrefs.SetInt(UserKey, FocusContext.OwnedUserId());
            PlayerPrefs.SetInt(EntityKey, planetId);
            PlayerPrefs.SetInt(SystemKey, systemId);
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(KindKey);
            PlayerPrefs.DeleteKey(EntityKey);
            PlayerPrefs.DeleteKey(SystemKey);
            PlayerPrefs.DeleteKey(UserKey);
            PlayerPrefs.Save();
        }
    }
}
