using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// The holo table's projector: a spatial loop at the table (audible within ~3 m), louder while the table
    /// is being worked (a grab, a pick, the board unfolding — <see cref="Excite"/>).
    /// </summary>
    public sealed class HoloHum : MonoBehaviour
    {
        const float Rest = 0.07f;

        static HoloHum _instance;
        AudioSource _src;
        Transform _at;
        float _excite;

        public void Bind(Transform room, Vector3 local)
        {
            _instance = this;
            var go = new GameObject("HoloHum");
            go.transform.SetParent(room, false);
            go.transform.localPosition = local;
            _at = go.transform;
        }

        /// <summary>The projector working harder for a moment.</summary>
        public static void Excite(float amount = 1f)
        {
            if (_instance != null)
                _instance._excite = Mathf.Max(_instance._excite, Mathf.Clamp01(amount));
        }

        void Update()
        {
            if (_src == null)
            {
                var clip = SfxSynth.BedClip(SfxSynth.Bed.Holo);
                if (clip == null || _at == null)
                    return;
                _src = _at.gameObject.AddComponent<AudioSource>();
                _src.clip = clip;
                _src.loop = true;
                _src.spatialBlend = 1f;
                _src.rolloffMode = AudioRolloffMode.Linear;
                _src.minDistance = 0.5f;
                _src.maxDistance = 3.2f;
                _src.dopplerLevel = 0f;
                _src.volume = Rest;
                _src.Play();
            }

            _excite = Mathf.MoveTowards(_excite, 0f, Time.deltaTime * 0.6f);
            _src.volume = Rest + _excite * 0.12f;
            _src.pitch = 1f + _excite * 0.06f;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
