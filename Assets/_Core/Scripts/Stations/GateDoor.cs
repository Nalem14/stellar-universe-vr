using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The bridge door to the gate base, centre of the aft bulkhead (lab to port, dry dock to starboard).
    /// The base links to the Stargate of any of our worlds, so it opens from a ship's bridge as from a station.
    /// </summary>
    public static class GateDoor
    {
        static readonly Vector3 Position = new(0f, 0f, -WorldScale.CicDeck * 0.5f + 0.12f);

        public static RoomDoor Build(Transform bridge, CicArtKit art) =>
            RoomDoor.Build(bridge, "GateDoor", Position, 0f, Trans.Get("vr.gate.enter"), GateRoom.Accent, art,
                CanPass, () => AsyncTap.Run(GateRoom.Instance.Enter()));

        static bool CanPass() => GateRoom.Instance != null && !GateRoom.Inside && !ResearchLab.Inside &&
                                 !DryDock.Inside && !DiplomacyRoom.AnyRoomInside && AuthManager.Ensure().Empire != null;
    }
}
