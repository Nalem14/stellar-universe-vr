using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// Procedural sound design: short one-shots (doors, teleport, welding, alerts, boot power-up…) built once on
    /// the main thread (a few thousand samples each), and seamless room beds (ventilation, reactor, hangar,
    /// lab pad, holo projector) synthesised on a worker thread — tones on whole cycles of the loop, noise
    /// cross-faded tail into head — so a bed never hitches the frame. Mono, 22 050 Hz.
    /// </summary>
    public static class SfxSynth
    {
        public const int Rate = 22050;

        // ── One-shots ─────────────────────────────────────────────────────────────

        static readonly Dictionary<string, AudioClip> Shots = new();

        public static AudioClip DoorOpen => Shot("su_door_open", 0.95f, d => Door(d, true));
        public static AudioClip DoorClose => Shot("su_door_close", 0.85f, d => Door(d, false));
        public static AudioClip Teleport => Shot("su_teleport", 1.1f, TeleportWave);
        public static AudioClip Weld => Shot("su_weld", 0.4f, WeldWave);
        public static AudioClip Clunk => Shot("su_clunk", 0.35f, ClunkWave);
        public static AudioClip Crystal => Shot("su_crystal", 0.7f, CrystalWave);
        public static AudioClip KeyTick => Shot("su_key", 0.035f, KeyWave);
        public static AudioClip PowerUp => Shot("su_powerup", 3.2f, PowerUpWave);
        public static AudioClip AlertWhoop => Shot("su_whoop", 1.0f, WhoopWave);
        public static AudioClip AmberChime => Shot("su_amber", 1.1f, d => Bell(d, new[] { 659.3f, 880f }, 0.22f, 0.2f));
        public static AudioClip AllClear => Shot("su_clear", 1.2f, d => Bell(d, new[] { 587.3f, 740f, 880f }, 0.16f, 0.15f));
        public static AudioClip Incoming => Shot("su_incoming", 0.75f, IncomingWave);
        public static AudioClip GateAlarm => Shot("su_gate_alarm", 2.6f, GateAlarmWave);
        public static AudioClip GateCharge => Shot("su_gate_charge", 2.4f, GateChargeWave);
        public static AudioClip Pip => Shot("su_pip", 0.09f, d => Bell(d, new[] { 1318.5f }, 0.1f, 0.12f));
        public static AudioClip Synth => Shot("su_synth", 1.6f, SynthWave);

        /// <summary>Build every one-shot now (call behind a fade, not mid-action).</summary>
        public static void Prewarm()
        {
            _ = DoorOpen;
            _ = DoorClose;
            _ = Teleport;
            _ = Weld;
            _ = Clunk;
            _ = Crystal;
            _ = KeyTick;
            _ = AlertWhoop;
            _ = AmberChime;
            _ = AllClear;
            _ = Incoming;
            _ = Pip;
            _ = Synth;
        }

        static AudioClip Shot(string name, float seconds, Action<float[]> fill)
        {
            if (Shots.TryGetValue(name, out var clip) && clip != null)
                return clip;
            var data = new float[Mathf.Max(64, (int)(Rate * seconds))];
            fill(data);
            clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            Shots[name] = clip;
            return clip;
        }

        static void Door(float[] d, bool open)
        {
            var rng = new System.Random(open ? 11 : 12);
            float lp = 0f, lp2 = 0f, phase = 0f;
            var n = d.Length;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var u = i / (float)n;
                // Servo: a geared whine gliding up (open) or down (close), low-passed saw.
                var hz = open ? Mathf.Lerp(150f, 380f, u) : Mathf.Lerp(360f, 140f, u);
                phase += hz / Rate;
                var saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                lp += (saw - lp) * 0.08f;
                var motor = lp * Mathf.Clamp01(u * 12f) * Mathf.Clamp01((0.82f - u) * 8f) * 0.32f;
                // Pneumatic hiss at the start (seal breaking) or end (sealing).
                lp2 += ((float)rng.NextDouble() * 2f - 1f - lp2) * 0.55f;
                var hissAt = open ? Mathf.Exp(-t * 7f) : Mathf.Exp(-Mathf.Abs(u - 0.78f) * 14f);
                var hiss = lp2 * hissAt * 0.22f;
                // Clunk of the leaves meeting their stops.
                var tc = t - (open ? 0.78f : 0.7f);
                var clunk = tc > 0f ? (Mathf.Sin(2f * Mathf.PI * 62f * tc) * 0.6f + Mathf.Sin(2f * Mathf.PI * 910f * tc) * 0.12f) * Mathf.Exp(-tc * 22f) : 0f;
                d[i] = motor + hiss + clunk * 0.55f;
            }
        }

        static void TeleportWave(float[] d)
        {
            var rng = new System.Random(21);
            float lp = 0f;
            var n = d.Length;
            float p1 = 0f, p2 = 0f, p3 = 0f;
            for (var i = 0; i < n; i++)
            {
                var u = i / (float)n;
                var env = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f));
                var hz = Mathf.Lerp(380f, 2300f, u * u);
                p1 += hz / Rate;
                p2 += hz * 1.503f / Rate;
                p3 += hz * 2.01f / Rate;
                var shimmer = (Mathf.Sin(p1 * 6.2832f) + Mathf.Sin(p2 * 6.2832f) * 0.5f + Mathf.Sin(p3 * 6.2832f) * 0.3f) *
                              (0.6f + 0.4f * Mathf.Sin(u * 90f));
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * Mathf.Lerp(0.05f, 0.6f, u);
                d[i] = (shimmer * 0.12f + lp * 0.18f) * env;
            }
        }

        static void WeldWave(float[] d)
        {
            var rng = new System.Random(31);
            float bp = 0f, bp2 = 0f;
            var n = d.Length;
            for (var i = 0; i < n; i++)
            {
                var u = i / (float)n;
                // Sparse crackle: random impulses through a resonant band-pass (~2.5 kHz).
                var imp = rng.NextDouble() < 0.035 ? ((float)rng.NextDouble() * 2f - 1f) : 0f;
                var x = imp + ((float)rng.NextDouble() * 2f - 1f) * 0.08f;
                bp += (x - bp) * 0.6f;
                bp2 += (bp - bp2) * 0.35f;
                d[i] = (bp - bp2) * 1.4f * Mathf.Clamp01(u * 30f) * (1f - u);
            }
        }

        static void ClunkWave(float[] d)
        {
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                d[i] = (Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.55f * Mathf.Exp(-t * 18f) +
                        (Mathf.Sin(2f * Mathf.PI * 913f * t) + Mathf.Sin(2f * Mathf.PI * 1371f * t) * 0.6f) * 0.1f * Mathf.Exp(-t * 26f)) *
                       Mathf.Clamp01(t * 800f);
            }
        }

        static void CrystalWave(float[] d)
        {
            // Glassy: inharmonic partials, the high ones dying first.
            float[] f = { 1567.98f, 2349.3f, 3520f, 4186f };
            float[] a = { 0.14f, 0.09f, 0.05f, 0.03f };
            float[] k = { 4f, 6f, 9f, 13f };
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                var s = 0f;
                for (var p = 0; p < f.Length; p++)
                    s += Mathf.Sin(2f * Mathf.PI * f[p] * t) * a[p] * Mathf.Exp(-t * k[p]);
                d[i] = s * Mathf.Clamp01(t * 600f);
            }
        }

        static void KeyWave(float[] d)
        {
            var rng = new System.Random(41);
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                d[i] = (((float)rng.NextDouble() * 2f - 1f) * 0.3f + Mathf.Sin(2f * Mathf.PI * 2600f * t) * 0.25f) * Mathf.Exp(-t * 180f);
            }
        }

        static void PowerUpWave(float[] d)
        {
            var n = d.Length;
            float phase = 0f;
            var rng = new System.Random(51);
            float lp = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var u = i / (float)n;
                // Generators spinning up: a harmonic hum rising in pitch and level, then the systems-online chime.
                var hz = Mathf.Lerp(28f, 112f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.25f)));
                phase += hz / Rate;
                var w = phase * 6.2832f;
                var hum = (Mathf.Sin(w) + Mathf.Sin(w * 2f) * 0.5f + Mathf.Sin(w * 3f) * 0.3f + Mathf.Sin(w * 5f) * 0.12f) *
                          Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.4f)) * Mathf.Clamp01((1f - u) * 5f);
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.04f;
                var tc = t - 2.25f;
                var thunk = tc > 0f ? Mathf.Sin(2f * Mathf.PI * 55f * tc) * Mathf.Exp(-tc * 10f) * 0.5f : 0f;
                var chime = tc > 0.08f
                    ? (Mathf.Sin(2f * Mathf.PI * 880f * (tc - 0.08f)) + Mathf.Sin(2f * Mathf.PI * 1318.5f * (tc - 0.08f)) * 0.6f) *
                      Mathf.Exp(-(tc - 0.08f) * 3.5f) * 0.1f
                    : 0f;
                d[i] = hum * 0.16f + lp * 0.3f * u + thunk + chime;
            }
        }

        static void WhoopWave(float[] d)
        {
            var n = d.Length;
            float phase = 0f;
            for (var i = 0; i < n; i++)
            {
                var u = i / (float)n;
                // The classic battle-stations whoop: a square-ish rise, soft-clipped, fast release.
                var hz = Mathf.Lerp(280f, 820f, Mathf.Pow(u, 0.8f));
                phase += hz / Rate;
                var w = phase * 6.2832f;
                var s = Mathf.Sin(w) + Mathf.Sin(w * 3f) * 0.3f + Mathf.Sin(w * 5f) * 0.15f;
                var env = Mathf.Clamp01(u * 20f) * Mathf.Clamp01((1f - u) * 6f);
                d[i] = (float)Math.Tanh(s * 1.3f) * env * 0.2f;
            }
        }

        static void Bell(float[] d, float[] notes, float noteSeconds, float amp)
        {
            var per = (int)(Rate * noteSeconds);
            for (var nIdx = 0; nIdx < notes.Length; nIdx++)
            {
                var start = nIdx * per;
                for (var i = start; i < d.Length; i++)
                {
                    var t = (i - start) / (float)Rate;
                    var w = 2f * Mathf.PI * notes[nIdx] * t;
                    d[i] += (Mathf.Sin(w) + Mathf.Sin(w * 2.76f) * 0.2f + Mathf.Sin(w * 5.4f) * 0.06f) * Mathf.Exp(-t * 4.2f) *
                            Mathf.Clamp01(t * 400f) * amp;
                }
            }
        }

        /// <summary>
        /// The base's gate-activation warning: a procedure notice, not a danger alarm. A low, soft two-note chime
        /// falling a fifth (G3 then C3), each note two slightly detuned sines with a faint octave, slow bloom and long
        /// decay, over a quiet sub swell. Calm and grave; the red incoming-wormhole klaxon stays the alarm.
        /// </summary>
        static void GateAlarmWave(float[] d)
        {
            var n = d.Length;
            (float at, float hz)[] notes = { (0f, 196f), (0.62f, 130.81f) };
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var s = 0f;
                foreach (var (at, hz) in notes)
                {
                    var local = t - at;
                    if (local < 0f)
                        continue;
                    var w = 2f * Mathf.PI * hz * local;
                    var tone = Mathf.Sin(w) * 0.5f + Mathf.Sin(w * 1.004f) * 0.5f + Mathf.Sin(w * 2f) * 0.18f * Mathf.Exp(-local * 3f) +
                               Mathf.Sin(w * 3f) * 0.05f * Mathf.Exp(-local * 5f);
                    var env = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(local / 0.05f)) * Mathf.Exp(-local * 1.5f);
                    s += tone * env;
                }

                var swell = Mathf.Sin(2f * Mathf.PI * 65.41f * t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 2.4f)) * 0.25f;
                var fade = Mathf.Clamp01((n - i) / (Rate * 0.15f));
                d[i] = (s * 0.24f + swell * 0.24f) * fade;
            }
        }

        /// <summary>The ring charging before the dial: a rising electric hum with crackle, ending on a click.</summary>
        static void GateChargeWave(float[] d)
        {
            var n = d.Length;
            var rng = new System.Random(23);
            float phase = 0f, lp = 0f;
            for (var i = 0; i < n; i++)
            {
                var u = i / (float)n;
                var hz = Mathf.Lerp(55f, 220f, u * u);
                phase += hz / Rate;
                var w = phase * 6.2832f;
                var hum = Mathf.Sin(w) + Mathf.Sin(w * 2f) * 0.5f + Mathf.Sin(w * 3.01f) * 0.25f;
                var noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.25f;
                var crackle = rng.NextDouble() < 0.002 + u * 0.01 ? noise * 2.5f : 0f;
                var env = Mathf.Clamp01(u * 4f) * Mathf.Clamp01((1f - u) * 25f);
                d[i] = ((float)Math.Tanh(hum * 0.9f) * 0.16f * (0.4f + u) + lp * 0.05f * u + crackle * 0.08f) * env;
            }
        }

        static void IncomingWave(float[] d)
        {
            for (var c = 0; c < 3; c++)
            {
                var start = (int)(Rate * c * 0.16f);
                for (var i = start; i < d.Length; i++)
                {
                    var t = (i - start) / (float)Rate;
                    if (t > 0.3f)
                        break;
                    var hz = 1320f + (c == 2 ? 440f : 0f);
                    d[i] += Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-t * 16f) * Mathf.Clamp01(t * 500f) * 0.2f;
                }
            }
        }

        static void SynthWave(float[] d)
        {
            // The lab synthesiser taking a sample: a resonant sweep over a bubbling arpeggio, ending on a fifth.
            var n = d.Length;
            float p = 0f;
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var u = i / (float)n;
                var step = Mathf.FloorToInt(t * 14f);
                var arp = notes[step % 4] * (u > 0.8f ? 1.5f : 1f);
                p += arp / Rate;
                var local = t * 14f - step;
                var s = Mathf.Sin(p * 6.2832f) * Mathf.Exp(-local * 4f) * 0.12f;
                var sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 360f, u) * t) * 0.06f;
                d[i] = (s + sweep) * Mathf.Clamp01(u * 10f) * Mathf.Clamp01((1f - u) * 6f);
            }
        }

        // ── Beds (worker thread) ─────────────────────────────────────────────────

        public enum Bed
        {
            Air,
            Reactor,
            Hangar,
            LabPad,
            Holo,
            /// <summary>The citadel's bays open on the city: high wind, the city's low rumble, distant fly-bys.</summary>
            City
        }

        static readonly Dictionary<Bed, Task<float[]>> Jobs = new();
        static readonly Dictionary<Bed, AudioClip> Beds = new();

        /// <summary>The bed's loop once ready (null while it is being synthesised — ask again next frame).</summary>
        public static AudioClip BedClip(Bed bed)
        {
            if (Beds.TryGetValue(bed, out var clip) && clip != null)
                return clip;
            if (!Jobs.TryGetValue(bed, out var job))
            {
                Jobs[bed] = Task.Run(() => Synthesise(bed));
                return null;
            }

            if (!job.IsCompleted)
                return null;
            Jobs.Remove(bed);
            if (job.IsFaulted || job.Result == null)
                return null;
            clip = AudioClip.Create("su_bed_" + bed, job.Result.Length, 1, Rate, false);
            clip.SetData(job.Result, 0);
            Beds[bed] = clip;
            return clip;
        }

        static float[] Synthesise(Bed bed)
        {
            return bed switch
            {
                Bed.Air => Loop(6f, AirWave),
                Bed.Reactor => Loop(4f, ReactorWave),
                Bed.Hangar => Loop(9f, HangarWave),
                Bed.LabPad => Loop(8f, LabWave),
                Bed.City => Loop(12f, CityWave),
                _ => Loop(4f, HoloWave)
            };
        }

        /// <summary>Seamless loop of <paramref name="seconds"/>: rendered with a head start, tail cross-faded into the head.</summary>
        static float[] Loop(float seconds, Action<float[], float> render)
        {
            var len = (int)(Rate * seconds);
            var xf = Rate / 4;
            var raw = new float[len + xf];
            render(raw, seconds);
            var outp = new float[len];
            for (var i = 0; i < len; i++)
                outp[i] = raw[i];
            for (var i = 0; i < xf; i++)
            {
                var k = i / (float)xf;
                // The first quarter second is the continuation of the last: equal-power blend.
                outp[i] = raw[len + i] * Mathf.Cos(k * Mathf.PI * 0.5f) + raw[i] * Mathf.Sin(k * Mathf.PI * 0.5f);
            }

            return outp;
        }

        /// <summary>A frequency near <paramref name="hz"/> that fits whole cycles in the loop.</summary>
        static float Fit(float hz, float loop) => Mathf.Max(1f, Mathf.Round(hz * loop)) / loop;

        static void AirWave(float[] d, float loop)
        {
            var rng = new System.Random(101);
            float a = 0f, b = 0f, c = 0f;
            var lfo = Fit(0.34f, loop);
            var lfo2 = Fit(0.11f, loop);
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                var x = (float)rng.NextDouble() * 2f - 1f;
                a += (x - a) * 0.12f;
                b += (a - b) * 0.12f;
                c += (x - c) * 0.5f;
                var breath = 0.75f + 0.15f * Mathf.Sin(2f * Mathf.PI * lfo * t) + 0.1f * Mathf.Sin(2f * Mathf.PI * lfo2 * t + 1.3f);
                d[i] = (b * 0.55f + (c - a) * 0.05f) * breath;
            }
        }

        static void ReactorWave(float[] d, float loop)
        {
            var f0 = Fit(47f, loop);
            var f1 = Fit(94.5f, loop);
            var f2 = Fit(141f, loop);
            var whine = Fit(1240f, loop);
            var beat = Fit(0.5f, loop);
            var rng = new System.Random(102);
            float n = 0f;
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                var s = Mathf.Sin(2f * Mathf.PI * f0 * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.3f +
                        Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.14f;
                n += ((float)rng.NextDouble() * 2f - 1f - n) * 0.03f;
                var pulse = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * beat * t);
                d[i] = (s * pulse + n * 0.5f) * 0.35f + Mathf.Sin(2f * Mathf.PI * whine * t) * 0.006f;
            }
        }

        static void HangarWave(float[] d, float loop)
        {
            var rng = new System.Random(103);
            float a = 0f, b = 0f;
            var f0 = Fit(38f, loop);
            // Distant work in the bay: two metal clanks and a ratchet, echoing.
            float[] hits = { 1.7f, 4.9f, 7.2f };
            float[] pitch = { 1f, 0.78f, 1.32f };
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                a += ((float)rng.NextDouble() * 2f - 1f - a) * 0.02f;
                b += (a - b) * 0.05f;
                var s = b * 1.6f + Mathf.Sin(2f * Mathf.PI * f0 * t) * 0.08f;
                for (var h = 0; h < hits.Length; h++)
                {
                    for (var echo = 0; echo < 3; echo++)
                    {
                        var tc = t - hits[h] - echo * 0.19f;
                        if (tc < 0f || tc > 0.8f)
                            continue;
                        var g = Mathf.Pow(0.42f, echo);
                        s += (Mathf.Sin(2f * Mathf.PI * 523f * pitch[h] * tc) + Mathf.Sin(2f * Mathf.PI * 1187f * pitch[h] * tc) * 0.5f) *
                             Mathf.Exp(-tc * 9f) * 0.05f * g + Mathf.Sin(2f * Mathf.PI * 80f * tc) * Mathf.Exp(-tc * 14f) * 0.08f * g;
                    }
                }

                d[i] = s;
            }
        }

        static void CityWave(float[] d, float loop)
        {
            // Wind round the tower top (band-passed noise breathing in gusts), the city's rumble far below
            // (traffic, machinery: a low brown noise with a faint 55 Hz hum), and two craft passing far off
            // (a soft Doppler swell each).
            var rng = new System.Random(107);
            float a = 0f, b = 0f, c = 0f, r = 0f;
            var gust = Fit(0.09f, loop);
            var gust2 = Fit(0.23f, loop);
            var hum = Fit(55f, loop);
            float[] pass = { 2.4f, 7.9f };
            float[] passHz = { 182f, 131f };
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                var x = (float)rng.NextDouble() * 2f - 1f;
                a += (x - a) * 0.08f;
                b += (a - b) * 0.08f;
                var wind = (a - b) * (0.55f + 0.3f * Mathf.Sin(2f * Mathf.PI * gust * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * gust2 * t + 2.1f));
                c += (x - c) * 0.01f;
                r += (c - r) * 0.02f;
                var rumble = r * 2.4f + Mathf.Sin(2f * Mathf.PI * hum * t) * 0.012f;
                var craft = 0f;
                for (var k = 0; k < pass.Length; k++)
                {
                    var tc = t - pass[k];
                    if (tc < -1.6f || tc > 1.6f)
                        continue;
                    var env = Mathf.Exp(-tc * tc * 1.6f);
                    var hz = passHz[k] * (1f - tc * 0.06f);
                    craft += (Mathf.Sin(2f * Mathf.PI * hz * t) * 0.6f + (float)rng.NextDouble() * 0.4f) * env * 0.035f;
                }

                d[i] = wind * 0.9f + rumble + craft;
            }
        }

        static void LabWave(float[] d, float loop)
        {
            float[] chord = { 110f, 164.81f, 220f, 261.63f, 329.63f, 493.88f };
            var f = new float[chord.Length];
            for (var k = 0; k < chord.Length; k++)
                f[k] = Fit(chord[k], loop);
            var lfo = Fit(0.25f, loop);
            var bub = Fit(3f, loop);
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                var s = 0f;
                for (var k = 0; k < f.Length; k++)
                    s += Mathf.Sin(2f * Mathf.PI * f[k] * t + k * 0.7f) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * lfo * t + k * 1.1f)) / (1f + k * 0.35f);
                // Cryo coolant bubbling in the columns.
                var ph = t * bub - Mathf.Floor(t * bub);
                var bubble = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(700f, 1300f, ph) * t) * Mathf.Exp(-ph * 30f) * 0.04f;
                d[i] = s * 0.07f + bubble;
            }
        }

        static void HoloWave(float[] d, float loop)
        {
            var f0 = Fit(120f, loop);
            var sh = Fit(2400f, loop);
            var sh2 = Fit(2413f, loop);
            var lfo = Fit(0.75f, loop);
            for (var i = 0; i < d.Length; i++)
            {
                var t = i / (float)Rate;
                var buzz = Mathf.Sin(2f * Mathf.PI * f0 * t) + Mathf.Sin(2f * Mathf.PI * f0 * 2f * t) * 0.4f + Mathf.Sin(2f * Mathf.PI * f0 * 3f * t) * 0.2f;
                var shimmer = (Mathf.Sin(2f * Mathf.PI * sh * t) + Mathf.Sin(2f * Mathf.PI * sh2 * t)) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * lfo * t));
                d[i] = buzz * 0.12f + shimmer * 0.015f;
            }
        }
    }
}
