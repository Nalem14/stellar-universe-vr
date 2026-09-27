using System.Collections.Generic;
using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// Every one-shot in the game goes through here: a pool of <see cref="Voices"/> 3D sources (each voice keeps
    /// its own position and pitch — no more cues jumping to the last caller), a per-clip cooldown against hover
    /// spam, voice stealing by priority, and ducking of the beds (klaxon, crew voice, transmissions) read by
    /// <see cref="AmbienceDirector"/>. A cue far from the listener that still matters (crew, alerts) is heard on
    /// the ship's intercom: 2D, a little quieter, instead of not at all. Mutes with the app (Quest menu, headset off).
    /// </summary>
    public sealed class SfxBus : MonoBehaviour
    {
        const int Voices = 14;

        public enum Priority
        {
            Ui = 0,
            Fx = 1,
            Voice = 2,
            Alert = 3
        }

        static SfxBus _instance;

        AudioSource[] _pool;
        float[] _endAt;
        Priority[] _prio;
        readonly Dictionary<AudioClip, float> _last = new();
        float _duck;
        float _duckTarget;
        float _duckUntil;

        /// <summary>0 = beds at full level, 1 = fully ducked (smoothed).</summary>
        public static float Duck => _instance != null ? _instance._duck : 0f;

        public static SfxBus Ensure()
        {
            if (_instance != null)
                return _instance;
            var go = new GameObject("SfxBus");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SfxBus>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            _pool = new AudioSource[Voices];
            _endAt = new float[Voices];
            _prio = new Priority[Voices];
            for (var i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Linear;
                _pool[i] = s;
            }
        }

        /// <summary>
        /// Play <paramref name="clip"/> at <paramref name="pos"/>. <paramref name="range"/> is where it fades
        /// out; <paramref name="intercom"/> carries it 2D when the listener is out of range.
        /// </summary>
        public static AudioSource Play(AudioClip clip, Vector3 pos, float volume, float pitch = 1f, float range = 12f,
            Priority priority = Priority.Fx, float cooldown = 0.035f, bool intercom = false, float spatial = 1f)
        {
            if (clip == null || volume <= 0.001f)
                return null;
            var bus = Ensure();
            var now = Time.unscaledTime;
            if (cooldown > 0f && bus._last.TryGetValue(clip, out var at) && now - at < cooldown)
                return null;
            bus._last[clip] = now;

            var far = false;
            if (spatial > 0f)
            {
                var listener = Listener();
                far = listener.HasValue && (listener.Value - pos).sqrMagnitude > range * range;
                if (far && !intercom)
                    return null;
            }

            var v = bus.Pick(priority, now);
            if (v < 0)
                return null;
            var src = bus._pool[v];
            src.transform.position = pos;
            src.spatialBlend = far ? 0f : spatial;
            src.minDistance = Mathf.Min(0.6f, range * 0.2f);
            src.maxDistance = range;
            src.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            src.volume = far ? volume * 0.55f : volume;
            src.clip = clip;
            src.loop = false;
            src.time = 0f;
            src.Play();
            bus._endAt[v] = now + clip.length / src.pitch + 0.05f;
            bus._prio[v] = priority;
            return src;
        }

        /// <summary>2D, everywhere on the ship (intercom announcements, alerts heard from any room).</summary>
        public static AudioSource Play2D(AudioClip clip, float volume, float pitch = 1f, Priority priority = Priority.Fx,
            float cooldown = 0.05f) =>
            Play(clip, Vector3.zero, volume, pitch, 12f, priority, cooldown, false, 0f);

        /// <summary>Lower the ambience beds by <paramref name="depth"/> (0–1) for <paramref name="seconds"/>.</summary>
        public static void DuckBeds(float depth, float seconds)
        {
            var bus = Ensure();
            bus._duckTarget = Mathf.Max(bus._duckUntil > Time.unscaledTime ? bus._duckTarget : 0f, Mathf.Clamp01(depth));
            bus._duckUntil = Mathf.Max(bus._duckUntil, Time.unscaledTime + seconds);
        }

        int Pick(Priority priority, float now)
        {
            var steal = -1;
            var stealScore = float.MaxValue;
            for (var i = 0; i < _pool.Length; i++)
            {
                if (now >= _endAt[i] || !_pool[i].isPlaying)
                    return i;
                // Steal the least important, nearest to its end.
                if (_prio[i] > priority)
                    continue;
                var score = (int)_prio[i] * 100f + (_endAt[i] - now);
                if (score < stealScore)
                {
                    stealScore = score;
                    steal = i;
                }
            }

            return steal;
        }

        static Vector3? Listener()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : null;
        }

        void Update()
        {
            var target = Time.unscaledTime < _duckUntil ? _duckTarget : 0f;
            // Duck fast, recover slowly (a breath after the klaxon / the line).
            _duck = Mathf.MoveTowards(_duck, target, Time.unscaledDeltaTime * (target > _duck ? 4f : 0.8f));
        }

        void OnApplicationPause(bool paused) => AudioListener.pause = paused;

        void OnApplicationFocus(bool focus) => AudioListener.pause = !focus && !Application.isEditor;
    }
}
