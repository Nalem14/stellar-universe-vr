using UnityEngine;

namespace Core.Vfx
{
    public class HoloSpin : MonoBehaviour
    {
        public float DegreesPerSecond = 8f;
        public float BobMeters = 0.02f;

        Vector3 _origin;
        bool _hasOrigin;

        void Start()
        {
            if (!_hasOrigin)
                _origin = transform.localPosition;
            _hasOrigin = true;
        }

        /// <summary>Move the rest position (token relaid out while the map pans).</summary>
        public void SetOrigin(Vector3 localPos)
        {
            _origin = localPos;
            _hasOrigin = true;
            transform.localPosition = localPos;
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
