using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The corridor door to the gate base, at the far end of the corridor. The stargate is a planet building:
    /// the door is there only when we stand on a station over one of our worlds that has one (as the dry dock
    /// only opens over our worlds); aboard a ship, the corridor end stays a plain bulkhead.
    /// </summary>
    public sealed class GateDoor : MonoBehaviour
    {
        static (Vector3 pos, float yaw) Pose => CorridorRoom.DoorPose(CorridorRoom.Slot.GateEnd);

        FocusContext _focus;
        RoomDoor _door;
        float _next;

        public static GateDoor Build(Transform corridor, CicArtKit art, FocusContext focus)
        {
            var go = new GameObject("GateDoorPresence");
            go.transform.SetParent(corridor, false);
            var presence = go.AddComponent<GateDoor>();
            presence._focus = focus;
            presence._door = RoomDoor.Build(corridor, "GateDoor", Pose.pos, Pose.yaw, Trans.Get("vr.gate.enter"), GateRoom.Accent, art,
                presence.CanPass, presence.Pass);
            presence.Apply();
            return presence;
        }

        int StationPlanet => _focus != null && _focus.ViewFleetId <= 0 ? _focus.ViewPlanetId : 0;

        bool Available => GateRoom.Instance != null && GateRoom.Instance.HasGate(StationPlanet) && AuthManager.Ensure().Empire != null;

        bool CanPass() => Available && !GateRoom.Inside && !ResearchLab.Inside && !DryDock.Inside && CorridorRoom.Inside;

        void Pass()
        {
            if (CanPass())
                AsyncTap.Run(GateRoom.Instance.Enter(StationPlanet));
        }

        // The view and the buildings both change it (a station hop, a gate finished): a cheap check every 2 s.
        void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 2f;
            Apply();
        }

        void Apply()
        {
            var show = Available || GateRoom.Inside;
            if (_door != null && _door.gameObject.activeSelf != show)
                _door.gameObject.SetActive(show);
        }
    }
}
