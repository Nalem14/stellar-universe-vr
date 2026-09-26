using Core.App;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// Rooms with windows on space are wings above the ship (or station) we stand on: while open they are moved
    /// over the bridge, so their windows look on the same, shared system exterior — no second copy of it.
    /// </summary>
    public static class RoomPlacement
    {
        /// <summary>Height over the bridge (clear of our hull and of the world we orbit).</summary>
        public const float AboveShip = 24f;

        /// <summary>
        /// Move <paramref name="room"/> over the bridge, turned so that the room-local direction
        /// <paramref name="viewLocal"/> (from the stand) looks at the system's star.
        /// </summary>
        public static void OverShip(Transform room, Vector3 viewLocal)
        {
            var bridge = Object.FindFirstObjectByType<BridgeViewRig>();
            if (bridge == null || bridge.BridgeMount == null)
                return;
            var pos = bridge.BridgeMount.position + Vector3.up * AboveShip;
            var ext = Object.FindFirstObjectByType<SystemExterior>();
            var toStar = (ext != null ? ext.transform.position : Vector3.zero) - pos;
            toStar.y = 0f;
            if (toStar.sqrMagnitude < 1f)
                toStar = Vector3.forward;
            viewLocal.y = 0f;
            if (viewLocal.sqrMagnitude < 1e-4f)
                viewLocal = Vector3.forward;
            room.SetPositionAndRotation(pos,
                Quaternion.LookRotation(toStar.normalized, Vector3.up) *
                Quaternion.Inverse(Quaternion.LookRotation(viewLocal.normalized, Vector3.up)));

            // The wing is part of the ship: it flies with it (departures, approaches, in-system hops), and the
            // player standing in it with it.
            var ride = room.GetComponent<RideShip>() ?? room.gameObject.AddComponent<RideShip>();
            ride.Latch(bridge.BridgeMount);
        }
    }

    /// <summary>Keeps a room at a fixed pose relative to the ship it was placed over (after the ship moved this frame).</summary>
    [DefaultExecutionOrder(100)]
    public sealed class RideShip : MonoBehaviour
    {
        Transform _mount;
        Vector3 _localPos;
        Quaternion _localRot;

        public void Latch(Transform mount)
        {
            _mount = mount;
            _localPos = mount.InverseTransformPoint(transform.position);
            _localRot = Quaternion.Inverse(mount.rotation) * transform.rotation;
        }

        void LateUpdate()
        {
            if (_mount == null)
                return;
            transform.SetPositionAndRotation(_mount.TransformPoint(_localPos), _mount.rotation * _localRot);
        }
    }
}
