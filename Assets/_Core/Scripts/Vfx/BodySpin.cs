using UnityEngine;

namespace Core.Vfx
{
    /// <summary>Slow axial spin for planets / rocks. Seeded phase, no Random.</summary>
    public class BodySpin : MonoBehaviour
    {
        public float DegreesPerSecond = 4.5f;
        public Vector3 Axis = Vector3.up;
        public float Phase;

        void Update()
        {
            transform.Rotate(Axis, DegreesPerSecond * Time.deltaTime, Space.Self);
        }

        public void Configure(int seed, float degPerSec, float tiltDeg)
        {
            Phase = (seed % 360) * 0.017453292f;
            DegreesPerSecond = degPerSec;
            var tilt = tiltDeg * 0.017453292f;
            Axis = Quaternion.Euler(tilt, 0f, tilt * 0.35f) * Vector3.up;
            transform.Rotate(Axis, Phase * Mathf.Rad2Deg, Space.Self);
        }
    }
}
