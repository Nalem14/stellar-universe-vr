using UnityEngine;

namespace Core.Vfx
{
    public class HoloSpin : MonoBehaviour
    {
        public float DegreesPerSecond = 8f;
        public float BobMeters = 0.02f;

        Vector3 _origin;

        void Start()
        {
            _origin = transform.localPosition;
        }

        void Update()
        {
            if (Mathf.Abs(DegreesPerSecond) > 0.01f)
                transform.Rotate(Vector3.up, DegreesPerSecond * Time.deltaTime, Space.World);
            if (BobMeters > 0f)
            {
                var y = Mathf.Sin(Time.time * 1.15f) * BobMeters;
                transform.localPosition = _origin + new Vector3(0f, y, 0f);
            }
        }
    }
}
