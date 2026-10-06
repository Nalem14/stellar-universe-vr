using UnityEngine;

namespace Core.Vfx
{
    public enum HoloTokenKind
    {
        Fleet,
        Planet,
        Asteroid,
        System,
        Anomaly
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
        public string DisplayName = string.Empty;
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

        /// <summary>
        /// A detached, inactive copy of what this token stands for (kind, id, galaxy coordinates, name, place), for
        /// an order that waits on the lectern. Galaxy star tokens are pooled: while the captain aims at the lectern
        /// the token clicked may be moved onto another star, and an order that read it after Confirm flew the ship
        /// there (or, half reassigned, to "no such system"). Destroy it with <see cref="Release"/>.
        /// </summary>
        public static HoloToken Snapshot(HoloToken t)
        {
            if (t == null)
                return null;
            var go = new GameObject("HoloTargetSnapshot");
            go.SetActive(false);
            go.transform.SetPositionAndRotation(t.transform.position, t.transform.rotation);
            var s = go.AddComponent<HoloToken>();
            s.Kind = t.Kind;
            s.Id = t.Id;
            s.Slot = t.Slot;
            s.Owned = t.Owned;
            s.Busy = t.Busy;
            s.DisplayName = t.DisplayName;
            s.GalaxyX = t.GalaxyX;
            s.GalaxyY = t.GalaxyY;
            s.HomeLocalPos = t.HomeLocalPos;
            s.HomeLocalRot = t.HomeLocalRot;
            return s;
        }

        public static void Release(HoloToken snapshot)
        {
            if (snapshot != null)
                Destroy(snapshot.gameObject);
        }
    }
}
