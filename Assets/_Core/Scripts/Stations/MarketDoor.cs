using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The concourse door to the galactic exchange (third bay): like the dry dock, it only exists at one of our
    /// worlds — a fortress over it or its city — never aboard a ship. The exchange then trades from that world.
    /// </summary>
    public sealed class MarketDoor : MonoBehaviour
    {
        static (Vector3 pos, float yaw) Pose => CorridorRoom.DoorPose(CorridorRoom.Slot.MarketPort);

        FocusContext _focus;
        RoomDoor _door;

        public static MarketDoor Build(Transform corridor, CicArtKit art, FocusContext focus)
        {
            var go = new GameObject("MarketDoorPresence");
            go.transform.SetParent(corridor, false);
            var presence = go.AddComponent<MarketDoor>();
            presence._focus = focus;
            presence._door = RoomDoor.Build(corridor, "MarketDoor", Pose.pos, Pose.yaw, Trans.Get("market_title"), MarketRoom.Accent,
                art, presence.CanPass, presence.Pass);
            if (focus != null)
                focus.Changed += presence.Apply;
            OwnedPlanets.Changed += presence.Apply;
            presence.Apply();
            return presence;
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= Apply;
            OwnedPlanets.Changed -= Apply;
        }

        /// <summary>The world under the rotunda (the city's, or the fortress's anchor).</summary>
        int MarketPlanet => _focus != null && _focus.IsRotundaView ? _focus.RotundaPlanetId : 0;

        bool Open => MarketPlanet > 0 && OwnedPlanets.Contains(MarketPlanet);

        bool CanPass() => Open && MarketRoom.Instance != null && !MarketRoom.Inside && CorridorRoom.Inside &&
                          AuthManager.Ensure().HasEmpire;

        void Apply()
        {
            if (_door != null && _door.gameObject.activeSelf != Open)
                _door.gameObject.SetActive(Open);
        }

        void Pass()
        {
            if (CanPass())
                AsyncTap.Run(MarketRoom.Instance.Enter(MarketPlanet));
        }
    }
}
