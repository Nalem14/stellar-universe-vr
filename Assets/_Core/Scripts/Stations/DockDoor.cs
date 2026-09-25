using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The bridge door to the station's dry dock: it only exists at a virtual orbital station over one of our
    /// worlds (a ship's bridge has no dock). Walking through it enters <see cref="DryDock"/> for that planet.
    /// </summary>
    public sealed class DockDoor : MonoBehaviour
    {
        /// <summary>Aft bulkhead, starboard of the captain's chair (TP is to port).</summary>
        static readonly Vector3 Position = new(2.4f, 0f, -WorldScale.CicDeck * 0.5f + 0.12f);

        FocusContext _focus;
        RoomDoor _door;

        public static DockDoor Build(Transform bridge, CicArtKit art, FocusContext focus)
        {
            var go = new GameObject("DockDoorPresence");
            go.transform.SetParent(bridge, false);
            var presence = go.AddComponent<DockDoor>();
            presence._focus = focus;
            presence._door = RoomDoor.Build(bridge, "DockDoor", Position, 0f, Trans.Get("vr.dock.enter"),
                new Color(0.4f, 0.95f, 0.55f, 1f), art, presence.CanPass, presence.Pass);
            if (focus != null)
                focus.Changed += presence.Apply;
            presence.Apply();
            return presence;
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= Apply;
        }

        int StationPlanet => _focus != null && _focus.ViewFleetId <= 0 ? _focus.ViewPlanetId : 0;

        bool CanPass() => StationPlanet > 0 && OwnedPlanets.Contains(StationPlanet) && DryDock.Instance != null &&
                          !DryDock.Inside;

        void Apply()
        {
            var show = StationPlanet > 0 && OwnedPlanets.Contains(StationPlanet);
            if (_door != null && _door.gameObject.activeSelf != show)
                _door.gameObject.SetActive(show);
        }

        void Pass()
        {
            if (CanPass())
                AsyncTap.Run(DryDock.Instance.Enter(StationPlanet, 0));
        }
    }
}
