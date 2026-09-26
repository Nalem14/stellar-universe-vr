using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The bridge door to the captain's quarters, in the starboard corner of the aft bulkhead (mirror of the
    /// diplomacy door). The quarters are the empire's office: they open from any bridge, ship or station.
    /// </summary>
    public static class QuartersDoor
    {
        static readonly Vector3 Position = new(5.1f, 0f, -WorldScale.CicDeck * 0.5f + 0.12f);

        public static RoomDoor Build(Transform bridge, CicArtKit art) =>
            RoomDoor.Build(bridge, "QuartersDoor", Position, 0f, Trans.Get("vr.quarters.enter"), QuartersRoom.Accent, art,
                CanPass, () => AsyncTap.Run(QuartersRoom.Instance.Enter()));

        static bool CanPass() => QuartersRoom.Instance != null && !DiplomacyRoom.AnyRoomInside && AuthManager.Ensure().HasEmpire;
    }
}
