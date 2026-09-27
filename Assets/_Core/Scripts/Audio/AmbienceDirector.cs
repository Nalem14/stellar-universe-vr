using Core.App;
using Core.Stations;
using Core.Vfx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Core.Audio
{
    /// <summary>
    /// What each place sounds like, cross-faded as the player moves between them: the bridge (ship ambience,
    /// reactor under the deck), the corridor and cabins (ventilation), the hangar (a vast bay, distant work),
    /// the lab (cryo pad, bubbling columns), the stargate base (power hum, the horizon's roar while it stands),
    /// the airlock and boot (music). Shields hum in every room at red alert. All beds are 2D loops, one source
    /// each (8 at most, most silent), ducked under the klaxon and the crew and dipped behind the fade.
    /// Replaces the single ambience source that played on the bridge and leaked into the rooms around it.
    /// </summary>
    public sealed class AmbienceDirector : MonoBehaviour
    {
        enum Zone
        {
            None,
            Boot,
            Menu,
            Bridge,
            Corridor,
            Quarters,
            Diplomacy,
            Gate,
            Lab,
            Dock,
            Watch
        }

        enum Layer
        {
            Ship,
            Music,
            Air,
            Reactor,
            Hangar,
            Lab,
            Portal,
            Shield,
            Count
        }

        const float Fade = 0.55f;

        static AmbienceDirector _instance;

        readonly AudioSource[] _src = new AudioSource[(int)Layer.Count];
        readonly float[] _target = new float[(int)Layer.Count];
        readonly float[] _level = new float[(int)Layer.Count];
        Zone _zone;
        float _nextZone;

        public static void Ensure()
        {
            if (_instance != null)
                return;
            var go = new GameObject("AmbienceDirector");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AmbienceDirector>();
            SfxBus.Ensure();
            // Start the slow beds now, behind the boot / menu, so they're ready when the ship needs them.
            SfxSynth.BedClip(SfxSynth.Bed.Air);
            SfxSynth.BedClip(SfxSynth.Bed.Reactor);
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextZone)
            {
                _nextZone = Time.unscaledTime + 0.25f;
                var zone = Detect();
                if (zone != _zone)
                {
                    _zone = zone;
                    Targets(zone);
                }

                // Conditions within a zone: the horizon in the gate base, shields at red alert.
                var gate = GateRoom.Instance;
                _target[(int)Layer.Portal] = zone == Zone.Gate && gate != null && gate.GateOpen ? 0.3f : 0f;
                var shipZone = zone >= Zone.Bridge;
                _target[(int)Layer.Shield] = shipZone && AlertState.Level == AlertLevel.Red ? 0.34f : 0f;
            }

            var duck = SfxBus.Duck;
            var veil = ViewFade.Alpha;
            var dt = Time.unscaledDeltaTime;
            for (var i = 0; i < (int)Layer.Count; i++)
            {
                var want = _target[i];
                _level[i] = Mathf.MoveTowards(_level[i], want, dt * Fade * Mathf.Max(0.15f, Mathf.Max(want, _level[i])) * 2f);
                var src = _src[i];
                if (_level[i] <= 0.001f)
                {
                    if (src != null && src.isPlaying)
                        src.Stop();
                    continue;
                }

                if (src == null && (src = Source((Layer)i)) == null)
                    continue;
                // Music ducks less than the ship's beds; everything dips behind the fade.
                var d = i == (int)Layer.Music ? duck * 0.4f : duck * 0.65f;
                src.volume = _level[i] * (1f - d) * (1f - veil * 0.7f);
                if (!src.isPlaying)
                    src.Play();
            }
        }

        static Zone Detect()
        {
            var scene = SceneManager.GetActiveScene().name;
            if (scene == SceneFlow.Boot)
                return Zone.Boot;
            if (scene == SceneFlow.Menu)
                return Zone.Menu;
            if (scene != SceneFlow.Bridge)
                return Zone.None;
            // On watch the player is in their own room: the ship falls away to a faint link hum.
            if (Core.App.WatchMode.Inside)
                return Zone.Watch;
            if (DryDock.Inside)
                return Zone.Dock;
            if (ResearchLab.Inside)
                return Zone.Lab;
            if (GateRoom.Inside)
                return Zone.Gate;
            if (QuartersRoom.Inside)
                return Zone.Quarters;
            if (DiplomacyRoom.Inside)
                return Zone.Diplomacy;
            if (CorridorRoom.Inside)
                return Zone.Corridor;
            return Zone.Bridge;
        }

        void Targets(Zone zone)
        {
            for (var i = 0; i < _target.Length; i++)
                _target[i] = 0f;
            switch (zone)
            {
                case Zone.Boot:
                    Set(Layer.Music, 0.08f);
                    Set(Layer.Ship, 0.16f);
                    break;
                case Zone.Menu:
                    Set(Layer.Music, 0.07f);
                    Set(Layer.Ship, 0.14f);
                    Set(Layer.Air, 0.07f);
                    break;
                case Zone.Bridge:
                    Set(Layer.Ship, 0.26f);
                    Set(Layer.Reactor, 0.1f);
                    Set(Layer.Air, 0.035f);
                    break;
                case Zone.Watch:
                    // The remote link to the ship, barely there: the real room is the sound.
                    Set(Layer.Ship, 0.035f);
                    break;
                case Zone.Corridor:
                    Set(Layer.Ship, 0.1f);
                    Set(Layer.Air, 0.2f);
                    Set(Layer.Reactor, 0.12f);
                    break;
                case Zone.Quarters:
                    Set(Layer.Ship, 0.12f);
                    Set(Layer.Air, 0.08f);
                    Set(Layer.Reactor, 0.04f);
                    break;
                case Zone.Diplomacy:
                    Set(Layer.Ship, 0.16f);
                    Set(Layer.Air, 0.06f);
                    Set(Layer.Lab, 0.05f);
                    break;
                case Zone.Gate:
                    Set(Layer.Air, 0.1f);
                    Set(Layer.Reactor, 0.16f);
                    Set(Layer.Hangar, 0.08f);
                    break;
                case Zone.Lab:
                    Set(Layer.Lab, 0.2f);
                    Set(Layer.Air, 0.08f);
                    Set(Layer.Ship, 0.06f);
                    break;
                case Zone.Dock:
                    Set(Layer.Hangar, 0.3f);
                    Set(Layer.Reactor, 0.1f);
                    Set(Layer.Air, 0.06f);
                    break;
            }
        }

        void Set(Layer layer, float volume) => _target[(int)layer] = volume;

        AudioSource Source(Layer layer)
        {
            var clip = layer switch
            {
                Layer.Ship => ShipBed(),
                Layer.Music => SfxLibrary.Get(SfxLibrary.Music),
                Layer.Air => SfxSynth.BedClip(SfxSynth.Bed.Air),
                Layer.Reactor => SfxSynth.BedClip(SfxSynth.Bed.Reactor),
                Layer.Hangar => SfxSynth.BedClip(SfxSynth.Bed.Hangar),
                Layer.Lab => SfxSynth.BedClip(SfxSynth.Bed.LabPad),
                Layer.Portal => SfxLibrary.Get(SfxLibrary.PortalIdle),
                Layer.Shield => SfxLibrary.Get(SfxLibrary.ShieldIdle),
                _ => null
            };
            if (clip == null)
                return null;
            var go = new GameObject("Bed_" + layer);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.priority = 40;
            src.volume = 0f;
            // Recorded beds start somewhere inside, so two scenes never open on the same bar.
            if (clip.loadType != AudioClipLoadType.Streaming)
                src.time = Random.Range(0f, clip.length * 0.9f);
            _src[(int)layer] = src;
            return src;
        }

        static AudioClip _ship;

        static AudioClip ShipBed() => _ship != null ? _ship : _ship = Resources.Load<AudioClip>("CIC/ambient");
    }
}
