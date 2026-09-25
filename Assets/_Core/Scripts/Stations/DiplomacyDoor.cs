using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The bridge door to the diplomacy chamber, in the port corner of the aft bulkhead (past the lab door and
    /// the lockers). Diplomacy is the empire's, not the ship's: it opens from any bridge, ship or station.
    /// </summary>
    public static class DiplomacyDoor
    {
        static readonly Vector3 Position = new(-5.1f, 0f, -WorldScale.CicDeck * 0.5f + 0.12f);

        public static RoomDoor Build(Transform bridge, CicArtKit art) =>
            RoomDoor.Build(bridge, "DiplomacyDoor", Position, 0f, Trans.Get("vr.diplo.enter"), DiplomacyRoom.Accent, art,
                CanPass, () => AsyncTap.Run(DiplomacyRoom.Instance.Enter()));

        static bool CanPass() => DiplomacyRoom.Instance != null && !DiplomacyRoom.AnyRoomInside &&
                                 AuthManager.Ensure().HasEmpire;
    }
}
