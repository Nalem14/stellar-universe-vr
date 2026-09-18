using UnityEngine;

namespace Core.Vfx
{
    public enum HoloTokenKind
    {
        Fleet,
        Planet,
        Asteroid,
        System
    }

    /// <summary>
    /// Diegetic holo token identity for grab→drop fleet orders on the CIC table.
    /// </summary>
    public class HoloToken : MonoBehaviour
    {
        public HoloTokenKind Kind;
        public int Id;
        public int Slot;
        public bool Owned;
        public bool Busy;
        public float GalaxyX;
        public float GalaxyY;
        public Vector3 HomeLocalPos;
        public Quaternion HomeLocalRot = Quaternion.identity;

        public void CaptureHome()
        {
            HomeLocalPos = transform.localPosition;
            HomeLocalRot = transform.localRotation;
        }

        public void SnapHome()
        {
            transform.localPosition = HomeLocalPos;
            transform.localRotation = HomeLocalRot;
        }
    }
}
