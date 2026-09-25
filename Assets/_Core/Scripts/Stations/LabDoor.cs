using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The bridge door to the science lab, port side of the aft bulkhead (the dry dock door is starboard).
    /// Research is empire-wide, so the lab is open from a ship's bridge as from a station.
    /// </summary>
    public static class LabDoor
    {
        static readonly Vector3 Position = new(-2.4f, 0f, -WorldScale.CicDeck * 0.5f + 0.12f);

        public static RoomDoor Build(Transform bridge, CicArtKit art) =>
            RoomDoor.Build(bridge, "LabDoor", Position, 0f, Trans.Get("vr.lab.enter"), ResearchLab.Accent, art,
                CanPass, () => AsyncTap.Run(ResearchLab.Instance.Enter()));

        static bool CanPass() => ResearchLab.Instance != null && !ResearchLab.Inside && !DryDock.Inside &&
                                 AuthManager.Ensure().Empire != null;
    }
}
