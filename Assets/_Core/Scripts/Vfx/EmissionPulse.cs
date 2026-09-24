using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Alive engine / weapon glow. Drives _EmissionMul through a MaterialPropertyBlock so the
    /// shared hull material stays batched (no per-module material instance).
    /// </summary>
    public class EmissionPulse : MonoBehaviour
    {
        public float Min = 2.4f;
        public float Max = 6.2f;
        public float Speed = 3.4f;
        public float Phase;

        static readonly int EmissionMulId = Shader.PropertyToID("_EmissionMul");

        Renderer _renderer;
        MaterialPropertyBlock _block;

        void Start()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null || _renderer.sharedMaterial == null ||
                !_renderer.sharedMaterial.HasProperty(EmissionMulId))
            {
                enabled = false;
                return;
            }

            _block = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (!_renderer.isVisible)
                return;
            var t = (Mathf.Sin(Time.time * Speed + Phase) + 1f) * 0.5f;
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(EmissionMulId, Mathf.Lerp(Min, Max, t));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
