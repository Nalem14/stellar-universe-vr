using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The concourse door to a world's fuel refinery (sixth bay; in the citadel, the last portal before the model):
    /// like the dry dock and the exchange, it only exists at one of our worlds — a fortress over it or its city —
    /// and only once the empire has researched Fuel Synthesis. Walking through it enters <see cref="RefineryRoom"/>
    /// for that world (a world without a refinery yet opens on the dormant bay and its build console).
    /// </summary>
    public sealed class RefineryDoor : MonoBehaviour
    {
        const string Research = "fuelSynthesis";

        static (Vector3 pos, float yaw) Pose => CorridorRoom.DoorPose(CorridorRoom.Slot.RefineryStarboard);

        FocusContext _focus;
        EconomyService _economy;
        RoomDoor _door;

        public static RefineryDoor Build(Transform corridor, CicArtKit art, FocusContext focus, EconomyService economy)
        {
            var go = new GameObject("RefineryDoorPresence");
            go.transform.SetParent(corridor, false);
            var presence = go.AddComponent<RefineryDoor>();
            presence._focus = focus;
            presence._economy = economy;
            presence._door = RoomDoor.Build(corridor, "RefineryDoor", Pose.pos, Pose.yaw, Trans.Get("fuelRefinery"), RefineryRoom.Accent,
                art, presence.CanPass, presence.Pass);
            if (focus != null)
                focus.Changed += presence.Apply;
            if (economy != null)
                economy.Changed += presence.Apply;
            OwnedPlanets.Changed += presence.Apply;
            presence.Apply();
            return presence;
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= Apply;
            if (_economy != null)
                _economy.Changed -= Apply;
            OwnedPlanets.Changed -= Apply;
        }

        /// <summary>The world under the rotunda (the city's, or the fortress's anchor).</summary>
        int RefineryPlanet => _focus != null && _focus.IsRotundaView ? _focus.RotundaPlanetId : 0;

        bool Open => RefineryPlanet > 0 && OwnedPlanets.Contains(RefineryPlanet) && _economy != null &&
                     _economy.ResearchLevel(Research) >= 1;

        bool CanPass() => Open && RefineryRoom.Instance != null && !RefineryRoom.Inside && CorridorRoom.Inside;

        void Apply()
        {
            if (_door != null && _door.gameObject.activeSelf != Open)
                _door.gameObject.SetActive(Open);
        }

        void Pass()
        {
            if (CanPass())
                AsyncTap.Run(RefineryRoom.Instance.Enter(RefineryPlanet));
        }
    }
}
