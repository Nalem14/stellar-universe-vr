using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Alive engine / weapon glow. Uses an instance material.
    /// </summary>
    public class EmissionPulse : MonoBehaviour
    {
        public float Min = 2.4f;
        public float Max = 6.2f;
        public float Speed = 3.4f;
        public float Phase;

        Material _mat;

        void Start()
        {
            var r = GetComponent<Renderer>();
            if (r != null)
                _mat = r.material;
        }

        void Update()
        {
            if (_mat == null || !_mat.HasProperty("_EmissionMul"))
                return;
            var t = (Mathf.Sin(Time.time * Speed + Phase) + 1f) * 0.5f;
            _mat.SetFloat("_EmissionMul", Mathf.Lerp(Min, Max, t));
        }
    }
}
