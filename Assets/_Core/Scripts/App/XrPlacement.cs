using Unity.XR.CoreUtils;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Puts the player's HEAD on a spot, not the XR origin: the headset sits anywhere in the real play space,
    /// so moving the origin alone can drop the player into a wall or off the floor.
    /// </summary>
    public static class XrPlacement
    {
        /// <param name="floorPoint">World point on the floor where the player should stand.</param>
        /// <param name="forward">World direction the player should face.</param>
        public static void PlaceHead(XROrigin rig, Vector3 floorPoint, Vector3 forward)
        {
            if (rig == null)
                return;
            var body = rig.GetComponentInChildren<CharacterController>();
            if (body != null)
                body.enabled = false;
            var flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f && rig.Camera != null)
                rig.MatchOriginUpCameraForward(Vector3.up, flat.normalized);
            if (rig.Camera != null)
            {
                // Keep the head's real height above the floor; only slide the play space horizontally.
                var head = rig.Camera.transform.position;
                var delta = floorPoint - new Vector3(head.x, rig.transform.position.y, head.z);
                rig.transform.position += delta;
            }
            else
            {
                rig.transform.position = floorPoint;
            }

            if (body != null)
                body.enabled = true;
            FallGuard.Of(rig).Placed(floorPoint, flat.sqrMagnitude > 1e-4f ? flat.normalized : rig.transform.forward);
        }

        /// <summary>Same move without telling the guard (the guard's own corrections).</summary>
        internal static void Snap(XROrigin rig, Vector3 floorPoint, Vector3 forward)
        {
            var body = rig.GetComponentInChildren<CharacterController>();
            if (body != null)
                body.enabled = false;
            var flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f && rig.Camera != null)
                rig.MatchOriginUpCameraForward(Vector3.up, flat.normalized);
            var head = rig.Camera != null ? rig.Camera.transform.position : rig.transform.position;
            rig.transform.position += floorPoint - new Vector3(head.x, rig.transform.position.y, head.z);
            if (body != null)
                body.enabled = true;
        }
    }
}
