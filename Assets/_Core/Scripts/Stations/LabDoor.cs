using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The corridor door to the science lab (first door to port).
    /// </summary>
    public static class LabDoor
    {
        static (Vector3 pos, float yaw) Pose => CorridorRoom.DoorPose(CorridorRoom.Slot.LabPort);

        public static RoomDoor Build(Transform corridor, CicArtKit art) =>
            RoomDoor.Build(corridor, "LabDoor", Pose.pos, Pose.yaw, Trans.Get("vr.lab.enter"), ResearchLab.Accent, art,
                CanPass, () => AsyncTap.Run(ResearchLab.Instance.Enter()));

        static bool CanPass() => ResearchLab.Instance != null && !ResearchLab.Inside && !DryDock.Inside && CorridorRoom.Inside &&
                                 AuthManager.Ensure().Empire != null;
    }
}
