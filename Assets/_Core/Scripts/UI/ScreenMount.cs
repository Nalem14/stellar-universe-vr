using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// Where a screen or control lives on a piece of furniture, and which way it faces.
    /// Kit convention: the readable front of every screen / button / label is local -Z
    /// (TMP and world-space canvases read from -Z), so "facing the viewer" means +Z points away.
    /// </summary>
    public static class ScreenMount
    {
        /// <summary>
        /// A metric, unscaled mount point on <paramref name="surface"/>, even when the surface is a
        /// stretched primitive (arm pad, desk slab). <paramref name="offsetMeters"/> is along the
        /// surface's own axes, in metres. Follows the surface if it moves.
        /// </summary>
        public static Transform Socket(Transform surface, string name, Vector3 offsetMeters,
            Quaternion localRotation)
        {
            // Compensator cancels the parent's (axis-aligned) non-uniform scale; the pivot beneath it
            // can then rotate freely without shear.
            var comp = new GameObject(name + "_Socket").transform;
            comp.SetParent(surface, false);
            var s = surface.lossyScale;
            var inv = new Vector3(SafeInv(s.x), SafeInv(s.y), SafeInv(s.z));
            comp.localScale = inv;
            comp.localPosition = Vector3.Scale(offsetMeters, inv);
            comp.localRotation = Quaternion.identity;

            var pivot = new GameObject(name).transform;
            pivot.SetParent(comp, false);
            pivot.localRotation = localRotation;
            return pivot;
        }

        /// <summary>
        /// Turn <paramref name="mount"/> so its front (-Z) faces <paramref name="viewerWorld"/>.
        /// <paramref name="pitchFollow"/> 0 = stays vertical (yaw only), 1 = aims straight at the eye.
        /// Extra <paramref name="reclineDeg"/> leans the top away (desk consoles, 15–25°).
        /// </summary>
        public static void FaceViewer(Transform mount, Vector3 viewerWorld, float pitchFollow = 0.5f,
            float reclineDeg = 0f)
        {
            var toScreen = mount.position - viewerWorld;
            var flat = Vector3.ProjectOnPlane(toScreen, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
                flat = mount.forward;
            var yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
            var fullPitch = Vector3.SignedAngle(flat.normalized, toScreen.normalized, yaw * Vector3.right);
            mount.rotation = yaw * Quaternion.Euler(fullPitch * Mathf.Clamp01(pitchFollow) + reclineDeg, 0f, 0f);
        }

        /// <summary>World position of a viewer standing / seated at a local point of <paramref name="room"/>.</summary>
        public static Vector3 EyeAt(Transform room, Vector3 floorLocal, float eyeHeight) =>
            room.TransformPoint(floorLocal + Vector3.up * eyeHeight);

        static float SafeInv(float v) => Mathf.Abs(v) > 1e-5f ? 1f / v : 1f;
    }
}
