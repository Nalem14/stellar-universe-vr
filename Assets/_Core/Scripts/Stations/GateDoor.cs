using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The corridor door to the gate base, at the far end of the corridor.
    /// </summary>
    public static class GateDoor
    {
        static (Vector3 pos, float yaw) Pose => CorridorRoom.DoorPose(CorridorRoom.Slot.GateEnd);

        public static RoomDoor Build(Transform corridor, CicArtKit art) =>
            RoomDoor.Build(corridor, "GateDoor", Pose.pos, Pose.yaw, Trans.Get("vr.gate.enter"), GateRoom.Accent, art,
                CanPass, () => AsyncTap.Run(GateRoom.Instance.Enter()));

        static bool CanPass() => GateRoom.Instance != null && !GateRoom.Inside && !ResearchLab.Inside &&
                                 !DryDock.Inside && CorridorRoom.Inside && AuthManager.Ensure().Empire != null;
    }
}
