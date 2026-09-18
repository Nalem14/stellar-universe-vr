using UnityEngine;

namespace Core.Vfx
{
    /// <summary>Short diegetic UI cues — procedural clips, no extra art pack.</summary>
    public static class CicCue
    {
        static AudioClip s_Ok;
        static AudioClip s_Hover;
        static AudioClip s_Fail;
        static AudioSource s_Source;

        public static void Ok(Vector3 worldPos) => Play(OkClip(), worldPos, 0.55f);
        public static void Hover(Vector3 worldPos) => Play(HoverClip(), worldPos, 0.28f);
        public static void Fail(Vector3 worldPos) => Play(FailClip(), worldPos, 0.45f);

        static void Play(AudioClip clip, Vector3 worldPos, float volume)
        {
            if (clip == null)
                return;
            EnsureSource();
            s_Source.transform.position = worldPos;
            s_Source.PlayOneShot(clip, volume);
        }

        static void EnsureSource()
        {
            if (s_Source != null)
                return;
            var go = new GameObject(nameof(CicCue));
            Object.DontDestroyOnLoad(go);
            s_Source = go.AddComponent<AudioSource>();
            s_Source.playOnAwake = false;
            s_Source.spatialBlend = 1f;
            s_Source.rolloffMode = AudioRolloffMode.Linear;
            s_Source.minDistance = 0.4f;
            s_Source.maxDistance = 6f;
        }

        static AudioClip OkClip()
        {
            if (s_Ok != null)
                return s_Ok;
            s_Ok = Tone("su_ok", 880f, 0.12f, 0.35f);
            return s_Ok;
        }

        static AudioClip HoverClip()
        {
            if (s_Hover != null)
                return s_Hover;
            s_Hover = Tone("su_hover", 660f, 0.045f, 0.22f);
            return s_Hover;
        }

        static AudioClip FailClip()
        {
            if (s_Fail != null)
                return s_Fail;
            s_Fail = Tone("su_fail", 220f, 0.14f, 0.3f);
            return s_Fail;
        }

        static AudioClip Tone(string name, float hz, float seconds, float amp)
        {
            var rate = 22050;
            var samples = Mathf.Max(64, (int)(rate * seconds));
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)rate;
                var env = Mathf.Exp(-t * (8f / Mathf.Max(0.04f, seconds)));
                data[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * env * amp;
            }

            clip.SetData(data, 0);
            return clip;
        }
    }
}
