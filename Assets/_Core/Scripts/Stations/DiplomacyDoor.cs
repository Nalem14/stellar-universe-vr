using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The corridor door to the diplomacy chamber (second door to port).
    /// </summary>
    public static class DiplomacyDoor
    {
        static (Vector3 pos, float yaw) Pose => CorridorRoom.DoorPose(CorridorRoom.Slot.DiplomacyPort);

        public static RoomDoor Build(Transform corridor, CicArtKit art) =>
            RoomDoor.Build(corridor, "DiplomacyDoor", Pose.pos, Pose.yaw, Trans.Get("vr.diplo.enter"), DiplomacyRoom.Accent, art,
                CanPass, () => AsyncTap.Run(DiplomacyRoom.Instance.Enter()));

        static bool CanPass() => DiplomacyRoom.Instance != null && CorridorRoom.Inside &&
                                 AuthManager.Ensure().HasEmpire;
    }
}
