using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The corridor door to the captain's quarters (second door to starboard).
    /// </summary>
    public static class QuartersDoor
    {
        static (Vector3 pos, float yaw) Pose => CorridorRoom.DoorPose(CorridorRoom.Slot.QuartersStarboard);

        public static RoomDoor Build(Transform corridor, CicArtKit art) =>
            RoomDoor.Build(corridor, "QuartersDoor", Pose.pos, Pose.yaw, Trans.Get("vr.quarters.enter"), QuartersRoom.Accent, art,
                CanPass, () => AsyncTap.Run(QuartersRoom.Instance.Enter()));

        static bool CanPass() => QuartersRoom.Instance != null && CorridorRoom.Inside && AuthManager.Ensure().HasEmpire;
    }
}
