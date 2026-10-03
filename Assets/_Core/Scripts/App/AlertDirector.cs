using Core.Audio;
using Core.Crew;
using Core.Stations;
using Core.Vfx;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Sets the ship's alert condition (<see cref="AlertState"/>) once a second and makes every room live it:
    /// <list type="bullet">
    /// <item><b>Red</b> — our ship in a live fight (<see cref="CombatEvents.IsEngaged"/>): battle-stations
    /// whoops on the intercom, shields humming (<see cref="AmbienceDirector"/>), a red pulse in the walls of
    /// every room (global <c>_SU_AlertTint</c> read by <c>SU/HullInterior</c>) on top of the bridge's own red
    /// alert lighting (<see cref="BridgeCombatFx"/>).</item>
    /// <item><b>Amber</b> — enemy or pirate ships in the system, a siege on one of our worlds here, an
    /// unscheduled wormhole at the gate: a two-tone chime and a sonar sweep, a slow amber breath in the walls.</item>
    /// <item>Back to normal: the all-clear.</item>
    /// </list>
    /// The commander may take the condition in hand (<see cref="Condition"/>: stand down, yellow, red) or leave it
    /// on Auto. Every room lives it: a wash and a turning beacon sweep on all surfaces (<c>SUAlert.cginc</c>), and
    /// the <see cref="AlertBeacon"/>s. Tactical calls each change. No per-frame allocation; shader globals only.
    /// </summary>
    public sealed class AlertDirector : MonoBehaviour
    {
        static readonly int TintId = Shader.PropertyToID("_SU_AlertTint");
        static readonly int ColorId = Shader.PropertyToID("_SU_AlertColor");
        static readonly int SweepId = Shader.PropertyToID("_SU_AlertSweep");
        public static readonly Color RedTint = new(1f, 0.1f, 0.06f, 1f);
        public static readonly Color AmberTint = new(1f, 0.62f, 0.1f, 1f);
        const float SweepReach = 24f;

        public enum Mode
        {
            /// <summary>The ship judges its condition itself (contacts, sieges, the gate, a fight).</summary>
            Auto,
            /// <summary>The commander stands the ship down: no alert, whatever the scope says.</summary>
            StandDown,
            Yellow,
            Red
        }

        /// <summary>The commander's condition selector (<see cref="Core.UI.AlertConditionPanel"/>); Auto at every boot.</summary>
        public static Mode Condition { get; set; } = Mode.Auto;

        /// <summary>
        /// A room's own alarm (the gate base while the gate dials or stands open): lights and beacons only, the
        /// ship's condition and the crew's calls are unchanged. Cleared by the room on leaving.
        /// </summary>
        public static AlertLevel RoomAlarm { get; set; }

        /// <summary>What the lights show: the ship's condition or a room's own alarm, whichever is higher.</summary>
        public static AlertLevel VisualLevel => AlertState.Level > RoomAlarm ? AlertState.Level : RoomAlarm;

        /// <summary>Heading of the beacon beams (degrees): every gyrophare turns in step with the sweep on the walls.</summary>
        public static float BeaconAngle { get; private set; }

        FocusContext _focus;
        float _next;
        Mode _appliedCondition;
        bool _seeded;
        float _nextWhoop;
        int _whoops;
        float _nextChime;
        int _chimes;
        float _tint;
        float _sweep;
        Color _tintColor = RedTint;

        public static AlertDirector Build(Transform host, FocusContext focus)
        {
            var d = host.gameObject.AddComponent<AlertDirector>();
            d._focus = focus;
            return d;
        }

        void OnEnable() => AlertState.Changed += OnChanged;

        void OnDisable()
        {
            AlertState.Changed -= OnChanged;
            AlertState.Set(AlertLevel.Normal);
            RoomAlarm = AlertLevel.Normal;
            Shader.SetGlobalColor(TintId, Color.clear);
            Shader.SetGlobalVector(SweepId, Vector4.zero);
        }

        void Update()
        {
            var now = Time.unscaledTime;
            if (now >= _next || Condition != _appliedCondition)
            {
                _next = now + 1f;
                _appliedCondition = Condition;
                AlertState.Set(Condition switch
                {
                    Mode.StandDown => AlertLevel.Normal,
                    Mode.Yellow => AlertLevel.Amber,
                    Mode.Red => AlertLevel.Red,
                    _ => Evaluate()
                });
                _seeded = true;
            }

            var level = AlertState.Level;
            if (level == AlertLevel.Red && now >= _nextWhoop)
            {
                // Three whoops after the klaxon, then one every 20 s while the fight lasts.
                _whoops++;
                _nextWhoop = now + (_whoops < 3 ? 1.3f : 20f);
                SfxBus.Play2D(SfxSynth.AlertWhoop, 0.2f, priority: SfxBus.Priority.Alert, cooldown: 0.8f);
                SfxBus.DuckBeds(0.4f, 1f);
            }

            if (level == AlertLevel.Amber && _chimes > 0 && now >= _nextChime)
            {
                // Yellow alert: the two-tone repeats a few times, then once every half minute while it stands.
                _chimes--;
                _nextChime = now + (_chimes > 0 ? 3.2f : 30f);
                if (_chimes == 0)
                    _chimes = 1;
                SfxBus.Play2D(SfxSynth.AmberChime, 0.22f, priority: SfxBus.Priority.Alert, cooldown: 2.5f);
            }

            UpdateLights(now);
        }

        /// <summary>
        /// Every room lives the alert: a wash over all surfaces (red pulses ~1.2 s, yellow breathes) and the
        /// sweep of the beacons turning round the room. The light adds, never darkens; consoles stay readable.
        /// </summary>
        void UpdateLights(float now)
        {
            var visual = VisualLevel;
            var want = visual switch
            {
                AlertLevel.Red => 0.2f + 0.2f * (0.5f + 0.5f * Mathf.Sin(now * 5.2f)),
                AlertLevel.Amber => 0.1f + 0.06f * (0.5f + 0.5f * Mathf.Sin(now * 1.6f)),
                _ => 0f
            };
            var sweepWant = visual switch
            {
                AlertLevel.Red => 0.9f,
                AlertLevel.Amber => 0.55f,
                _ => 0f
            };
            if (visual != AlertLevel.Normal)
                _tintColor = visual == AlertLevel.Red ? RedTint : AmberTint;
            var dt = Time.unscaledDeltaTime;
            _tint = Mathf.MoveTowards(_tint, want, dt * 0.8f);
            _sweep = Mathf.MoveTowards(_sweep, sweepWant, dt * 1.5f);
            BeaconAngle = Mathf.Repeat(BeaconAngle + dt * (visual == AlertLevel.Red ? 250f : 160f), 360f);
            Shader.SetGlobalColor(TintId, _tintColor * _tint);
            Shader.SetGlobalColor(ColorId, _tintColor);
            var a = BeaconAngle * Mathf.Deg2Rad;
            Shader.SetGlobalVector(SweepId, new Vector4(Mathf.Sin(a), Mathf.Cos(a), _sweep, SweepReach));
        }

        AlertLevel Evaluate()
        {
            if (CombatEvents.IsEngaged)
                return AlertLevel.Red;
            if (_focus == null || !_focus.HasSystem)
                return AlertLevel.Normal;

            var gate = GateRoom.Instance;
            if (gate != null && gate.AlarmOn)
                return AlertLevel.Amber;

            if (SiegeWatch.Instance != null)
                foreach (var sg in SiegeWatch.Instance.Sieges)
                    if (sg.SystemId == _focus.SystemId && (sg.OurPlanet || sg.OurAttack))
                        return AlertLevel.Amber;

            var now = FleetOrderGate.UnixNow();
            foreach (var f in _focus.Fleets)
            {
                if (!_focus.VisibleInFocus(f, now))
                    continue;
                var st = DiplomacyIndex.ResolveFleet(f);
                if (st is EmpireStance.Enemy or EmpireStance.Pirate || f.IsPirate)
                    return AlertLevel.Amber;
            }

            return AlertLevel.Normal;
        }

        void OnChanged(AlertLevel was, AlertLevel now)
        {
            // Boarding into a condition already standing: the lights say it, the crew's contact report says why.
            if (!_seeded)
                return;
            var bark = BarkDirector.Instance;
            switch (now)
            {
                case AlertLevel.Red:
                    _whoops = 0;
                    // The klaxon (BridgeCombatFx) goes first.
                    _nextWhoop = Time.unscaledTime + 2.6f;
                    bark?.Say(CrewDialogue.Role.Tactical, "alertRed", 4);
                    break;
                case AlertLevel.Amber:
                    SfxBus.Play2D(SfxSynth.AmberChime, 0.3f, priority: SfxBus.Priority.Alert, cooldown: 3f);
                    _chimes = 3;
                    _nextChime = Time.unscaledTime + 3.2f;
                    if (was == AlertLevel.Normal)
                        CicCue.Contact(ListenerPos());
                    bark?.Say(CrewDialogue.Role.Tactical, "alertAmber", 3);
                    break;
                default:
                    SfxBus.Play2D(SfxSynth.AllClear, 0.3f, priority: SfxBus.Priority.Alert, cooldown: 3f);
                    bark?.Say(CrewDialogue.Role.Tactical, "alertClear", 2);
                    break;
            }
        }

        static Vector3 ListenerPos()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }
    }
}
