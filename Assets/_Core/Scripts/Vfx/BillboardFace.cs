using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Camera-facing billboard for holomap labels / order chips.
    /// Full face (not yaw-only) so chips stay readable when looking down at the table.
    /// </summary>
    public class BillboardFace : MonoBehaviour
    {
        public enum FaceMode
        {
            Camera,
            StarAxis
        }

        public FaceMode Mode = FaceMode.Camera;

        void LateUpdate()
        {
            FaceNow();
        }

        public void FaceNow()
        {
            var cam = ResolveCamera();
            if (cam == null)
                return;

            if (Mode != FaceMode.Camera)
                return;

            var toCam = cam.transform.position - transform.position;
            if (toCam.sqrMagnitude < 1e-6f)
                return;

            // +Z away from camera so TMP / UGUI front faces the player (avoids mirrored labels).
            transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        static Camera ResolveCamera()
        {
            var cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled)
                return cam;

            Camera best = null;
            var bestDepth = float.NegativeInfinity;
            var cams = Camera.allCameras;
            for (var i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null || !c.isActiveAndEnabled)
                    continue;
                if (c.depth >= bestDepth)
                {
                    bestDepth = c.depth;
                    best = c;
                }
            }

            return best;
        }
    }
}
