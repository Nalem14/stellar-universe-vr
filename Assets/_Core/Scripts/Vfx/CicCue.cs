using Core.Audio;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The game's sound cues, by meaning (ok, fail, door, dispatch, hull hit…): procedural clips and the
    /// recorded ones in <c>Resources/Audio</c>, all played through <see cref="SfxBus"/> (pooled voices,
    /// cooldowns, ducking, intercom when far).
    /// </summary>
    public static class CicCue
    {
        static AudioClip s_Ok;
        static AudioClip s_Hover;
        static AudioClip s_Fail;
        static AudioClip s_RadioOpen;
        static AudioClip s_RadioClose;

        public static void Ok(Vector3 worldPos) => Play(OkClip(), worldPos, 0.5f, range: 8f, priority: SfxBus.Priority.Ui, cooldown: 0.06f);
        public static void Hover(Vector3 worldPos) => Play(HoverClip(), worldPos, 0.24f, range: 5f, priority: SfxBus.Priority.Ui, cooldown: 0.07f);
        public static void Fail(Vector3 worldPos) => Play(FailClip(), worldPos, 0.45f, range: 8f, priority: SfxBus.Priority.Ui, cooldown: 0.08f);

        /// <summary>Crew comm channel opening (squelch + rising two-tone), at the speaking officer — on the intercom from another room.</summary>
        public static void RadioOpen(Vector3 worldPos)
        {
            Play(s_RadioOpen != null ? s_RadioOpen : s_RadioOpen = Radio("su_radio_open", 1150f, 1550f, 0.2f), worldPos, 0.5f,
                range: 14f, priority: SfxBus.Priority.Voice, intercom: true);
            SfxBus.DuckBeds(0.35f, 2.5f);
        }

        /// <summary>Crew comm channel closing (short falling blip).</summary>
        public static void RadioClose(Vector3 worldPos) =>
            Play(s_RadioClose != null ? s_RadioClose : s_RadioClose = Radio("su_radio_close", 1400f, 950f, 0.09f), worldPos, 0.35f,
                range: 14f, priority: SfxBus.Priority.Voice, intercom: true);

        // ── Ship sounds (recorded where we have them, procedural otherwise) ─────────

        /// <summary>A room door's servo and seal (open / close).</summary>
        public static void Door(Vector3 worldPos, bool open) =>
            Play(open ? SfxSynth.DoorOpen : SfxSynth.DoorClose, worldPos, 0.55f, range: 9f, cooldown: 0.3f);

        /// <summary>Transfer to another ship / station (the view changes hull).</summary>
        public static void Teleport(Vector3 worldPos) =>
            Play(SfxSynth.Teleport, worldPos, 0.45f, range: 6f, priority: SfxBus.Priority.Fx, cooldown: 0.5f, spatial: 0f);

        /// <summary>An order leaving the ship (move, jump, bond, gate): the dispatch burst.</summary>
        public static void Dispatch(Vector3 worldPos) =>
            Play(Lib(SfxLibrary.Dispatch) ?? SfxSynth.Pip, worldPos, 0.32f, range: 10f, cooldown: 0.4f, intercom: true);

        /// <summary>Work finished (a building, a tech, a ship off the line).</summary>
        public static void Success(Vector3 worldPos) =>
            Play(Lib(SfxLibrary.Success) ?? SfxSynth.AllClear, worldPos, 0.4f, range: 14f, priority: SfxBus.Priority.Voice, cooldown: 1f, intercom: true);

        /// <summary>A contact on the scanners (sonar sweep).</summary>
        public static void Contact(Vector3 worldPos) =>
            Play(Lib(SfxLibrary.Contact) ?? SfxSynth.Incoming, worldPos, 0.3f, range: 14f, priority: SfxBus.Priority.Alert, cooldown: 8f, intercom: true);

        /// <summary>An incoming transmission (message, mail): three chirps.</summary>
        public static void Incoming(Vector3 worldPos) =>
            Play(SfxSynth.Incoming, worldPos, 0.4f, range: 14f, priority: SfxBus.Priority.Voice, cooldown: 2f, intercom: true);

        public static void HullImpact(Vector3 worldPos, float volume = 0.7f) =>
            Play(Lib(SfxLibrary.HullImpact) ?? BoomClip(), worldPos, volume, range: 20f, priority: SfxBus.Priority.Alert, cooldown: 0.12f, spatial: 0.6f);

        public static void ShieldImpact(Vector3 worldPos) =>
            Play(Lib(SfxLibrary.ShieldImpact) ?? ZapClip(), worldPos, 0.5f, range: 20f, priority: SfxBus.Priority.Fx, cooldown: 0.1f, spatial: 0.6f);

        /// <summary>A ship blowing apart (volume by how close it is).</summary>
        public static void Explosion(Vector3 worldPos, float volume = 0.6f) =>
            Play(Lib(SfxLibrary.Explosion) ?? BoomClip(), worldPos, volume, range: 30f, priority: SfxBus.Priority.Alert, cooldown: 0.25f, spatial: 0.5f);

        /// <summary>Our battery firing, heard through the hull.</summary>
        public static void Laser(Vector3 worldPos, float pitch = 1f) =>
            Play(Lib(SfxLibrary.Laser) ?? ZapClip(), worldPos, 0.5f, pitch, range: 20f, cooldown: 0.08f, spatial: 0.5f);

        public static void Weld(Vector3 worldPos) =>
            Play(SfxSynth.Weld, worldPos, 0.3f, Random.Range(0.85f, 1.2f), range: 10f, cooldown: 0.09f);

        /// <summary>Something heavy set down / picked up (crate, module, sample).</summary>
        public static void Clunk(Vector3 worldPos) => Play(SfxSynth.Clunk, worldPos, 0.5f, range: 6f, cooldown: 0.15f);

        /// <summary>A research crystal touched / seated.</summary>
        public static void Crystal(Vector3 worldPos) => Play(SfxSynth.Crystal, worldPos, 0.35f, Random.Range(0.94f, 1.08f), range: 6f, cooldown: 0.1f);

        /// <summary>The lab synthesiser taking a sample.</summary>
        public static void Synth(Vector3 worldPos) => Play(SfxSynth.Synth, worldPos, 0.5f, range: 8f, cooldown: 0.5f);

        /// <summary>A key on the holo keyboard / a field taking focus.</summary>
        public static void Key(Vector3 worldPos) =>
            Play(SfxSynth.KeyTick, worldPos, 0.25f, Random.Range(0.9f, 1.15f), range: 4f, priority: SfxBus.Priority.Ui, cooldown: 0.03f);

        /// <summary>A small screen event (a picture-in-picture opening, a marker).</summary>
        public static void Pip(Vector3 worldPos) => Play(SfxSynth.Pip, worldPos, 0.25f, range: 8f, priority: SfxBus.Priority.Ui, cooldown: 0.3f);

        static AudioClip Lib(string name)
        {
            var c = SfxLibrary.Get(name);
            return c != null ? c : null;
        }

        /// <summary>Build every procedural clip now (behind a fade), so no cue synthesises mid-action.</summary>
        public static void Prewarm()
        {
            OkClip();
            HoverClip();
            FailClip();
            s_RadioOpen ??= Radio("su_radio_open", 1150f, 1550f, 0.2f);
            s_RadioClose ??= Radio("su_radio_close", 1400f, 950f, 0.09f);
            s_Klaxon ??= Horn("su_klaxon");
            ZapClip();
            BoomClip();
            s_Whoosh ??= Sweep("su_whoosh", 180f, 420f, 0.35f, 0.08f, 0.5f);
            s_Chime ??= Notes("su_chime", new[] { 784f, 1175f }, 0.11f);
            s_Deploy ??= Sweep("su_deploy", 220f, 880f, 0.6f, 0.18f, 0.12f);
            SfxSynth.Prewarm();
            SfxLibrary.Get(SfxLibrary.HullImpact);
            SfxLibrary.Get(SfxLibrary.ShieldImpact);
            SfxLibrary.Get(SfxLibrary.Explosion);
            SfxLibrary.Get(SfxLibrary.Laser);
            SfxLibrary.Get(SfxLibrary.Dispatch);
            SfxLibrary.Get(SfxLibrary.Success);
        }

        static AudioClip ZapClip() => s_Zap != null ? s_Zap : s_Zap = Sweep("su_zap", 1800f, 260f, 0.22f, 0.3f, 0.08f);
        static AudioClip BoomClip() => s_Boom != null ? s_Boom : s_Boom = Burst("su_boom", 0.7f);

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
        public static void Klaxon(Vector3 worldPos)
        {
            Play(s_Klaxon != null ? s_Klaxon : s_Klaxon = Horn("su_klaxon"), worldPos, 0.4f, range: 16f, priority: SfxBus.Priority.Alert,
                cooldown: 0.5f, intercom: true);
            SfxBus.DuckBeds(0.6f, 1.2f);
        }

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
        public static void Zap(Vector3 worldPos, float pitch = 1f) =>
            Play(ZapClip(), worldPos, 0.45f, Mathf.Clamp(pitch, 0.5f, 2f), cooldown: 0.05f);

        /// <summary>Hull hit / ship destroyed: low noise burst with a sub thump.</summary>
        public static void Boom(Vector3 worldPos, float volume = 0.6f) =>
            Play(BoomClip(), worldPos, volume, range: 14f, cooldown: 0.08f);

        /// <summary>Thruster glide of a ship changing hex.</summary>
        public static void Whoosh(Vector3 worldPos) =>
            Play(s_Whoosh != null ? s_Whoosh : s_Whoosh = Sweep("su_whoosh", 180f, 420f, 0.35f, 0.08f, 0.5f), worldPos, 0.4f);

        /// <summary>Your turn: rising two-note chime.</summary>
        public static void Chime(Vector3 worldPos) =>
            Play(s_Chime != null ? s_Chime : s_Chime = Notes("su_chime", new[] { 784f, 1175f }, 0.11f), worldPos, 0.5f);

        public static void Victory(Vector3 worldPos) =>
            Play(s_Victory != null ? s_Victory : s_Victory = Notes("su_victory", new[] { 523f, 659f, 784f, 1047f }, 0.16f), worldPos, 0.6f,
                range: 14f, priority: SfxBus.Priority.Alert, intercom: true);

        public static void Defeat(Vector3 worldPos) =>
            Play(s_Defeat != null ? s_Defeat : s_Defeat = Notes("su_defeat", new[] { 440f, 349f, 262f }, 0.24f), worldPos, 0.55f,
                range: 14f, priority: SfxBus.Priority.Alert, intercom: true);

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

        static void Play(AudioClip clip, Vector3 worldPos, float volume, float pitch = 1f, float range = 10f,
            SfxBus.Priority priority = SfxBus.Priority.Fx, float cooldown = 0.035f, bool intercom = false, float spatial = 1f) =>
            SfxBus.Play(clip, worldPos, volume, pitch, range, priority, cooldown, intercom, spatial);

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
