using System.Collections.Generic;
using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// The crew's voices, in "yaourt": speech-like babble with no words, so every language hears the same crew
    /// and the subtitle carries the meaning. Formant synthesis: a glottal buzz with vibrato and a falling
    /// contour through two vowel resonators (a / e / i / o / u), a consonant at each syllable's onset,
    /// words of two to four syllables with gaps, all through a radio band and a soft clip. Each station has
    /// its own timbre (pitch, vocal tract size, pace); the empire's species colours it (a synthetic ring, an
    /// insect's clicks, a lithoid's grind...). Clips are built once per voice and length band, then cached.
    /// </summary>
    public static class CrewVoice
    {
        public struct Timbre
        {
            public float F0;
            public float Tract;
            public float Pace;
            public int Seed;
        }

        /// <summary>Length bands (s): a line uses the longest band that fits under its subtitle time.</summary>
        static readonly float[] Bands = { 1.2f, 2f, 3f, 4.2f, 5.6f };
        static readonly Dictionary<long, AudioClip> Cache = new();

        /// <summary>Station voices, index = CrewDialogue.Role (Helm, Tactical, Engineering, Science, Comms, Ops).</summary>
        static readonly Timbre[] Stations =
        {
            new() { F0 = 118f, Tract = 1f, Pace = 1f, Seed = 11 },
            new() { F0 = 96f, Tract = 0.94f, Pace = 0.92f, Seed = 23 },
            new() { F0 = 132f, Tract = 1.04f, Pace = 1.08f, Seed = 37 },
            new() { F0 = 196f, Tract = 1.16f, Pace = 1.05f, Seed = 41 },
            new() { F0 = 214f, Tract = 1.2f, Pace = 1.12f, Seed = 53 },
            new() { F0 = 152f, Tract = 1.08f, Pace = 1f, Seed = 67 }
        };

        public static Timbre Station(int role) => Stations[Mathf.Clamp(role, 0, Stations.Length - 1)];

        /// <summary>A babble clip for <paramref name="seconds"/> of speech (null when too short to bother).</summary>
        public static AudioClip Line(Timbre voice, int family, float seconds, int variant)
        {
            var band = -1;
            for (var i = 0; i < Bands.Length; i++)
                if (Bands[i] <= seconds)
                    band = i;
            if (band < 0)
                return null;
            variant &= 3;
            var key = ((long)voice.Seed << 24) | ((long)family << 16) | ((long)band << 8) | (long)variant;
            if (Cache.TryGetValue(key, out var clip) && clip != null)
                return clip;
            var data = new float[(int)(Bands[band] * Core.Audio.SfxSynth.Rate)];
            Babble(data, voice, family, voice.Seed * 131 + band * 17 + variant * 7);
            clip = AudioClip.Create("su_voice_" + key, data.Length, 1, Core.Audio.SfxSynth.Rate, false);
            clip.SetData(data, 0);
            Cache[key] = clip;
            return clip;
        }

        // Vowel formants (Hz): a, e, i, o, u.
        static readonly Vector2[] Vowels =
        {
            new(730f, 1090f), new(530f, 1840f), new(300f, 2250f), new(570f, 840f), new(320f, 870f)
        };

        struct Res
        {
            float _y1, _y2, _b1, _b2, _a0;

            public void Tune(float freq, float bw, float rate)
            {
                var r = Mathf.Exp(-Mathf.PI * bw / rate);
                _b1 = 2f * r * Mathf.Cos(2f * Mathf.PI * freq / rate);
                _b2 = r * r;
                _a0 = 1f - r;
            }

            public float Step(float x)
            {
                var y = _a0 * x + _b1 * _y1 - _b2 * _y2;
                _y2 = _y1;
                _y1 = y;
                return y;
            }
        }

        static void Babble(float[] d, Timbre v, int family, int seed)
        {
            var rate = (float)Core.Audio.SfxSynth.Rate;
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();

            // Species colouring (Core.Crew.CrewSpecies.Family order).
            var f0Mul = 1f;
            var tractMul = 1f;
            var breath = 0.04f;
            var ring = 0f;
            var click = 0f;
            var grind = 0f;
            var wobble = 0f;
            switch (family)
            {
                case 1: ring = 70f; break;                                  // synthetic
                case 2: wobble = 0.08f; tractMul = 0.92f; break;            // aquatic
                case 3: f0Mul = 0.82f; breath = 0.12f; break;               // reptilian
                case 4: f0Mul = 1.55f; tractMul = 1.25f; break;             // avian
                case 5: tractMul = 1.35f; click = 0.5f; break;              // insectoid
                case 6: breath = 0.22f; f0Mul = 0.95f; break;               // sylvan
                case 7: f0Mul = 0.62f; grind = 0.35f; break;                // lithoid
            }

            var f0 = v.F0 * f0Mul;
            var tract = v.Tract * tractMul;
            Res r1 = default, r2 = default;
            float phase = 0f, hp = 0f, lpA = 0f, lpB = 0f, prevIn = 0f;
            var n = d.Length;
            var i = 0;
            var lead = (int)(0.05f * rate);
            while (i < n)
            {
                // A word: 2–4 syllables, then a short gap.
                var syllables = 2 + rng.Next(3);
                for (var s = 0; s < syllables && i < n; s++)
                {
                    var len = (int)(rate * (0.085f + R() * 0.11f) / v.Pace);
                    var vowel = Vowels[rng.Next(Vowels.Length)] * tract;
                    r1.Tune(vowel.x, 90f, rate);
                    r2.Tune(vowel.y, 130f, rate);
                    var onset = (int)(rate * (0.012f + R() * 0.02f));
                    var fricative = R() < 0.45f;
                    var pitchStart = f0 * (1f + (R() - 0.5f) * 0.18f);
                    for (var k = 0; k < len && i < n; k++, i++)
                    {
                        var t = i / rate;
                        var u = k / (float)len;
                        // Declination over the line, vibrato, the species' wobble.
                        var line = 1f - 0.12f * (i / (float)n);
                        var pitch = pitchStart * line * (1f + 0.02f * Mathf.Sin(t * 2f * Mathf.PI * 5.5f) + wobble * Mathf.Sin(t * 2f * Mathf.PI * 7f));
                        phase += pitch / rate;
                        if (phase >= 1f)
                            phase -= 1f;
                        var glottal = (phase * 2f - 1f) - 0.6f * (phase < 0.3f ? 1f : 0f);
                        var noise = R() * 2f - 1f;
                        var src = glottal * (1f - breath) + noise * breath;
                        if (k < onset)
                            src = fricative ? noise * 0.7f : (k < onset * 0.25f ? noise * 1.2f : 0f);
                        var voiced = r1.Step(src) * 1.1f + r2.Step(src) * 0.8f;
                        var env = Mathf.Clamp01(u / 0.12f) * Mathf.Clamp01((1f - u) / 0.28f);
                        var x = voiced * env;
                        if (ring > 0f)
                            x *= 0.55f + 0.45f * Mathf.Sin(t * 2f * Mathf.PI * ring);
                        if (click > 0f && k < 40)
                            x += click * noise * (1f - k / 40f);
                        if (grind > 0f)
                            x *= 1f - grind + grind * Mathf.Abs(Mathf.Sin(t * 2f * Mathf.PI * 34f));
                        d[i] = i < lead ? 0f : x;
                    }
                }

                var gap = (int)(rate * (0.05f + R() * 0.12f));
                for (var k = 0; k < gap && i < n; k++, i++)
                    d[i] = 0f;
            }

            // Radio: band-pass ~300–3000 Hz, a soft clip, normalised; fade the last 60 ms.
            var peak = 1e-4f;
            var aHp = Mathf.Exp(-2f * Mathf.PI * 300f / rate);
            var aLp = 1f - Mathf.Exp(-2f * Mathf.PI * 3000f / rate);
            for (var k = 0; k < n; k++)
            {
                var x = d[k];
                hp = aHp * (hp + x - prevIn);
                prevIn = x;
                lpA += aLp * (hp - lpA);
                lpB += aLp * (lpA - lpB);
                d[k] = lpB;
                peak = Mathf.Max(peak, Mathf.Abs(lpB));
            }

            var fade = (int)(0.06f * rate);
            for (var k = 0; k < n; k++)
            {
                var y = (float)System.Math.Tanh(d[k] / peak * 1.6f) * 0.8f;
                if (k > n - fade)
                    y *= (n - k) / (float)fade;
                d[k] = y;
            }
        }
    }
}
