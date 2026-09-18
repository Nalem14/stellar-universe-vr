using UnityEngine;

namespace Core.Vfx
{
    /// <summary>Cheap camera-facing billboard for star corona / nebula accents.</summary>
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
            var cam = Camera.main;
            if (cam == null)
            {
                foreach (var c in Camera.allCameras)
                {
                    if (c != null && c.isActiveAndEnabled)
                    {
                        cam = c;
                        break;
                    }
                }
            }

            if (cam == null)
                return;

            if (Mode == FaceMode.Camera)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }
    }
}
