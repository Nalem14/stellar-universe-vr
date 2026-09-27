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
    /// Tactical calls each change. No per-frame allocation; one global shader colour.
    /// </summary>
    public sealed class AlertDirector : MonoBehaviour
    {
        static readonly int TintId = Shader.PropertyToID("_SU_AlertTint");
        static readonly Color RedTint = new(1f, 0.1f, 0.06f, 1f);
        static readonly Color AmberTint = new(1f, 0.58f, 0.12f, 1f);

        FocusContext _focus;
        float _next;
        bool _seeded;
        float _nextWhoop;
        int _whoops;
        float _tint;
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
            Shader.SetGlobalColor(TintId, Color.clear);
        }

        void Update()
        {
            var now = Time.unscaledTime;
            if (now >= _next)
            {
                _next = now + 1f;
                AlertState.Set(Evaluate());
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

            // Walls: red pulses (~1.2 s), amber breathes; the light adds to the room, never darkens it.
            var want = level switch
            {
                AlertLevel.Red => 0.16f + 0.14f * (0.5f + 0.5f * Mathf.Sin(now * 5.2f)),
                AlertLevel.Amber => 0.06f + 0.035f * (0.5f + 0.5f * Mathf.Sin(now * 1.6f)),
                _ => 0f
            };
            if (level != AlertLevel.Normal)
                _tintColor = level == AlertLevel.Red ? RedTint : AmberTint;
            _tint = Mathf.MoveTowards(_tint, want, Time.unscaledDeltaTime * 0.6f);
            Shader.SetGlobalColor(TintId, _tintColor * _tint);
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
