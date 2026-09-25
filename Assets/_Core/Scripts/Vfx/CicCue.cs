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
        static AudioClip s_RadioOpen;
        static AudioClip s_RadioClose;

        public static void Ok(Vector3 worldPos) => Play(OkClip(), worldPos, 0.55f);
        public static void Hover(Vector3 worldPos) => Play(HoverClip(), worldPos, 0.28f);
        public static void Fail(Vector3 worldPos) => Play(FailClip(), worldPos, 0.45f);

        /// <summary>Crew comm channel opening (squelch + rising two-tone), at the speaking officer.</summary>
        public static void RadioOpen(Vector3 worldPos) =>
            Play(s_RadioOpen != null ? s_RadioOpen : s_RadioOpen = Radio("su_radio_open", 1150f, 1550f, 0.2f), worldPos, 0.5f);

        /// <summary>Crew comm channel closing (short falling blip).</summary>
        public static void RadioClose(Vector3 worldPos) =>
            Play(s_RadioClose != null ? s_RadioClose : s_RadioClose = Radio("su_radio_close", 1400f, 950f, 0.09f), worldPos, 0.35f);

        // ── Combat table (procedural, built once on first use) ────────────────────
        static AudioClip s_Zap;
        static AudioClip s_Boom;
        static AudioClip s_Whoosh;
        static AudioClip s_Chime;
        static AudioClip s_Victory;
        static AudioClip s_Defeat;
        static AudioClip s_Deploy;
        static AudioClip s_Klaxon;

        /// <summary>Red alert klaxon: a two-tone horn, harsh but short.</summary>
        public static void Klaxon(Vector3 worldPos) =>
            Play(s_Klaxon != null ? s_Klaxon : s_Klaxon = Horn("su_klaxon"), worldPos, 0.4f);

        static AudioClip Horn(string name)
        {
            var rate = 22050;
            var samples = (int)(rate * 0.7f);
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            var data = new float[samples];
            var phase = 0f;
            for (var i = 0; i < samples; i++)
            {
                var u = i / (float)samples;
                var hz = u < 0.5f ? 520f : 390f;
                phase += 2f * Mathf.PI * hz / rate;
                // Soft-clipped saw-ish horn: odd harmonics, gentle attack / release.
                var w = Mathf.Sin(phase) + Mathf.Sin(phase * 3f) * 0.33f + Mathf.Sin(phase * 5f) * 0.18f;
                var env = Mathf.Clamp01(u * 25f) * Mathf.Clamp01((1f - u) * 10f);
                data[i] = (float)System.Math.Tanh(w * 1.4f) * env * 0.22f;
            }

            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Energy weapon discharge (falling sweep); <paramref name="pitch"/> sets the family.</summary>
        public static void Zap(Vector3 worldPos, float pitch = 1f)
        {
            EnsureSource();
            s_Source.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            Play(s_Zap != null ? s_Zap : s_Zap = Sweep("su_zap", 1800f, 260f, 0.22f, 0.3f, 0.08f), worldPos, 0.45f);
            s_Source.pitch = 1f;
        }

        /// <summary>Hull hit / ship destroyed: low noise burst with a sub thump.</summary>
        public static void Boom(Vector3 worldPos, float volume = 0.6f) =>
            Play(s_Boom != null ? s_Boom : s_Boom = Burst("su_boom", 0.7f), worldPos, volume);

        /// <summary>Thruster glide of a ship changing hex.</summary>
        public static void Whoosh(Vector3 worldPos) =>
            Play(s_Whoosh != null ? s_Whoosh : s_Whoosh = Sweep("su_whoosh", 180f, 420f, 0.35f, 0.08f, 0.5f), worldPos, 0.4f);

        /// <summary>Your turn: rising two-note chime.</summary>
        public static void Chime(Vector3 worldPos) =>
            Play(s_Chime != null ? s_Chime : s_Chime = Notes("su_chime", new[] { 784f, 1175f }, 0.11f), worldPos, 0.5f);

        public static void Victory(Vector3 worldPos) =>
            Play(s_Victory != null ? s_Victory : s_Victory = Notes("su_victory", new[] { 523f, 659f, 784f, 1047f }, 0.16f), worldPos, 0.6f);

        public static void Defeat(Vector3 worldPos) =>
            Play(s_Defeat != null ? s_Defeat : s_Defeat = Notes("su_defeat", new[] { 440f, 349f, 262f }, 0.24f), worldPos, 0.55f);

        /// <summary>Holo projector spinning up (board unfolding / folding).</summary>
        public static void Deploy(Vector3 worldPos) =>
            Play(s_Deploy != null ? s_Deploy : s_Deploy = Sweep("su_deploy", 220f, 880f, 0.6f, 0.18f, 0.12f), worldPos, 0.4f);

        static AudioClip Sweep(string name, float fromHz, float toHz, float seconds, float amp, float noise)
        {
            var rate = 22050;
            var samples = Mathf.Max(64, (int)(rate * seconds));
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            var data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            var phase = 0f;
            var lp = 0f;
            for (var i = 0; i < samples; i++)
            {
                var u = i / (float)samples;
                var hz = Mathf.Lerp(fromHz, toHz, 1f - (1f - u) * (1f - u));
                phase += 2f * Mathf.PI * hz / rate;
                var env = Mathf.Clamp01(u * 40f) * (1f - u) * (1f - u);
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.2f;
                data[i] = (Mathf.Sin(phase) + Mathf.Sin(phase * 1.5f) * 0.3f) * amp * env + lp * noise * env;
            }

            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Burst(string name, float seconds)
        {
            var rate = 22050;
            var samples = Mathf.Max(64, (int)(rate * seconds));
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            var data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            var lp = 0f;
            var phase = 0f;
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)rate;
                var u = i / (float)samples;
                // The rumble darkens as it decays (cut-off falls with time).
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * Mathf.Lerp(0.5f, 0.04f, u);
                phase += 2f * Mathf.PI * Mathf.Lerp(90f, 38f, u) / rate;
                var env = Mathf.Clamp01(t * 300f) * Mathf.Exp(-u * 5f);
                data[i] = (lp * 0.9f + Mathf.Sin(phase) * 0.5f) * env * 0.6f;
            }

            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Notes(string name, float[] hz, float noteSeconds)
        {
            var rate = 22050;
            var per = (int)(rate * noteSeconds);
            var samples = per * hz.Length + rate / 3;
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            var data = new float[samples];
            for (var n = 0; n < hz.Length; n++)
            {
                var start = n * per;
                for (var i = start; i < samples; i++)
                {
                    var t = (i - start) / (float)rate;
                    var env = Mathf.Clamp01(t * 200f) * Mathf.Exp(-t * 5f);
                    var w = 2f * Mathf.PI * hz[n] * t;
                    data[i] += (Mathf.Sin(w) + Mathf.Sin(w * 2f) * 0.18f) * env * 0.16f;
                }
            }

            clip.SetData(data, 0);
            return clip;
        }

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

        /// <summary>Band-limited squelch noise under a two-step tone glide — reads as a bridge intercom.</summary>
        static AudioClip Radio(string name, float fromHz, float toHz, float seconds)
        {
            var rate = 22050;
            var samples = Mathf.Max(64, (int)(rate * seconds));
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            var data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            var lp = 0f;
            var phase = 0f;
            for (var i = 0; i < samples; i++)
            {
                var u = i / (float)samples;
                var hz = u < 0.5f ? fromHz : toHz;
                phase += 2f * Mathf.PI * hz / rate;
                var attack = Mathf.Clamp01(u * 30f);
                var env = attack * Mathf.Exp(-u * 3.2f);
                // One-pole low-passed noise = soft squelch, strongest at the very start.
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.35f;
                var squelch = lp * Mathf.Exp(-u * 9f) * 0.35f;
                data[i] = (Mathf.Sin(phase) * 0.28f + Mathf.Sin(phase * 2f) * 0.06f) * env + squelch;
            }

            clip.SetData(data, 0);
            return clip;
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
