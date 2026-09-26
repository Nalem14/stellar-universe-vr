using System;
using Core.App;
using Core.Holo;
using Core.Utils;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Flies the inhabited ship from one system to the next, so the trip is lived from the bridge rather
    /// than cut to black. The server stamps systemid = destination the moment an order is accepted (with
    /// from / desttime), so without this the bridge would sit in the target system for the whole trip.
    /// <list type="number">
    /// <item><b>Depart</b> — still in the origin system: the ship swings onto its course and pulls away
    /// (sub-light), spools its drive (hyperspace), closes the bond lattice (PRL) or dives through the gate.</item>
    /// <item><b>Cruise</b> — between systems: the origin exterior is put away and the destination is loaded
    /// behind the transit effect (no fade); sub-light dust with the target star swelling ahead, the hyperspace
    /// vortex, the gold PRL fold or the violet gate fold. The holo table already shows where we are going.</item>
    /// <item><b>Arrive</b> — timed to end exactly at desttime: the new system appears and the ship closes on
    /// its destination, decelerating (from the edge of the system, out of hyperspace, out of the bond, or out
    /// of the destination gate).</item>
    /// </list>
    /// Drive = order sent from here (<see cref="VoyageLog"/>), else inferred. Boarding a ship already under
    /// way joins the trip at its current phase. In-system hops are left to <see cref="BridgeViewRig"/>.
    /// </summary>
    public sealed class ShipVoyage : MonoBehaviour
    {
        public enum Phase
        {
            Idle,
            Depart,
            Cruise,
            Arrive
        }

        public static ShipVoyage Instance { get; private set; }

        /// <summary>A trip of the inhabited ship began: (drive, destination system or 0 while unknown).</summary>
        public static event Action<VoyageMode, int> Departed;
        /// <summary>The ship entered the transit (jump, fold, bond, or left the system under sub-light).</summary>
        public static event Action<VoyageMode, int> Transit;
        /// <summary>The ship dropped into its destination system and is closing in.</summary>
        public static event Action<VoyageMode, int> Arriving;

        const float SublightOut = 1000f;
        const float SublightIn = 1050f;
        const float HyperIn = 650f;
        const float BondIn = 60f;
        /// <summary>Sub-light ion sheath: the transit tube at a fraction, sparse lines only.</summary>
        const float SublightSheath = 0.5f;

        FocusContext _focus;
        SystemExterior _exterior;
        BridgeViewRig _rig;
        BridgeSystemLoader _loader;
        FleetPoller _poller;
        VoyageFx _fx;
        VoyageFx.Look _look;

        Phase _phase;
        VoyageMode _mode;
        int _fleetId;
        int _originSystem;
        int _destSystem;
        int _gateFrom;
        int _gateTo;
        double _destTime;
        double _startedUnix;
        float _phaseAt;
        double _arriveFrom;
        float _departSeconds;
        float _arriveSeconds;
        Vector3 _start;
        Quaternion _startRot;
        Vector3 _depDir;
        Vector3 _arrDir;
        bool _arrDirSet;
        Vector3 _holdPos;
        Quaternion _holdRot;
        Vector3 _gateAxis;
        Vector3 _lastPos;
        Quaternion _lastRot;
        bool _swapping;
        bool _swapped;
        int _swapFails;
        float _retryAt;
        int _watchFleet;
        int _watchSystem;
        bool _joined;

        public Phase Current => _phase;
        public VoyageMode Mode => _mode;
        public bool Active => _phase != Phase.Idle;
        /// <summary>Destination system of the current trip (0 = none / not known yet).</summary>
        public int DestinationSystem => _destSystem;
        /// <summary>The exterior is put away (between systems): nothing out there to frame.</summary>
        public bool BetweenSystems => _phase == Phase.Cruise;

        /// <summary>0..1 of the whole trip (departure to desttime).</summary>
        public float Progress
        {
            get
            {
                if (_phase == Phase.Idle)
                    return 0f;
                var span = _destTime - _startedUnix;
                return span <= 0.01 ? 1f : Mathf.Clamp01((float)((Now() - _startedUnix) / span));
            }
        }

        public static ShipVoyage Build(Transform host, FocusContext focus, SystemExterior exterior, BridgeViewRig rig,
            BridgeSystemLoader loader, FleetPoller poller)
        {
            var v = host.gameObject.AddComponent<ShipVoyage>();
            v._focus = focus;
            v._exterior = exterior;
            v._rig = rig;
            v._loader = loader;
            v._poller = poller;
            v._fx = VoyageFx.Create(rig.ViewShip);
            Instance = v;
            VoyageLog.Ensure();
            VoyageLog.Ordered += v.OnOrdered;
            JumpgateNetwork.Jumped += v.OnGateJumped;
            if (focus != null)
            {
                focus.Changed += v.Watch;
                focus.FleetsChanged += v.Watch;
            }

            v.Watch();
            return v;
        }

        void OnDestroy()
        {
            VoyageLog.Ordered -= OnOrdered;
            JumpgateNetwork.Jumped -= OnGateJumped;
            if (_focus != null)
            {
                _focus.Changed -= Watch;
                _focus.FleetsChanged -= Watch;
            }

            if (Instance == this)
                Instance = null;
        }

        // ── Detection ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The loader asks before following the inhabited ship into another system: true = this trip is
        /// flown here (the swap happens behind the transit effect, not behind a fade).
        /// </summary>
        public bool Claim(FocusFleet ship)
        {
            Watch();
            return ship != null && Active && ship.Id == _fleetId;
        }

        void OnOrdered(int fleetId, VoyageMode mode, int system)
        {
            if (_focus == null || fleetId != _focus.ViewFleetId)
                return;
            if (_phase == Phase.Idle && mode != VoyageMode.Jumpgate &&
                (mode == VoyageMode.PrlBond || (system > 0 && system != _focus.SystemId)))
                Begin(mode, system);
            // The server dropped its fleet cache with the order: read the new state now, not in 3.5 s.
            if (_poller != null)
                AsyncTap.Run(_poller.PollNow());
        }

        void OnGateJumped(int fleetId, int originPlanet, int targetPlanet)
        {
            if (_focus == null || fleetId != _focus.ViewFleetId || _phase != Phase.Idle)
                return;
            _gateFrom = originPlanet;
            _gateTo = targetPlanet;
            var system = GalaxyCatalog.TryGetPlanet(targetPlanet, out var p) ? p.SystemId : 0;
            Begin(VoyageMode.Jumpgate, system);
            _gateFrom = originPlanet;
            _gateTo = targetPlanet;
        }

        void Watch()
        {
            if (_focus == null)
                return;
            var f = _focus.FindViewFleet();
            var now = Now();
            var unix = (long)now;
            if (f == null)
            {
                if (Active)
                    Stop();
                _watchFleet = 0;
                return;
            }

            if (f.Id != _watchFleet)
            {
                // Boarded another ship: leave the old trip, join this one if it is between systems.
                if (Active && f.Id != _fleetId)
                    Stop();
                _watchFleet = f.Id;
                _watchSystem = f.SystemId;
                if (!Active && f.SystemId == _focus.SystemId && VoyageLog.IsInterstellar(f, unix))
                    Join(f);
                return;
            }

            if (_phase == Phase.Idle)
            {
                // Stamped into another system while we still show this one: it just left.
                if (f.IsMoving(unix) && f.SystemId > 0 && _focus.SystemId > 0 && f.SystemId != _focus.SystemId)
                    Begin(VoyageLog.Resolve(f, unix), f.SystemId);
            }
            else if (f.Id == _fleetId)
            {
                if (_destSystem <= 0 && f.SystemId > 0 && f.SystemId != _originSystem)
                    _destSystem = f.SystemId;
                // Live arrival time (a Nova speed-up brings it forward), once the server has the trip.
                if (f.IsMoving(unix) && (f.SystemId == _destSystem || _swapped))
                    _destTime = f.DestTime;
                else if (!f.IsMoving(unix) && _swapped && _phase == Phase.Cruise)
                    _destTime = Math.Min(_destTime, now);
            }

            _watchSystem = f.SystemId;
        }

        void Begin(VoyageMode mode, int destSystem)
        {
            var f = _focus.FindViewFleet();
            if (f == null)
                return;
            var now = Now();
            _fleetId = f.Id;
            _mode = mode;
            _look = VoyageFx.LookOf(mode);
            _originSystem = _focus.SystemId;
            _destSystem = destSystem;
            _gateFrom = f.PlanetId;
            _gateTo = 0;
            _startedUnix = now;
            _destTime = f.IsMoving((long)now) && f.SystemId != _originSystem
                ? f.DestTime
                : now + mode switch
                {
                    VoyageMode.PrlBond => GameConfig.PrlTransitSeconds > 0f ? GameConfig.PrlTransitSeconds : 10f,
                    VoyageMode.Jumpgate => GameConfig.JumpgateTransitSeconds > 0f ? GameConfig.JumpgateTransitSeconds : 30f,
                    _ => 600f // learnt from the next poll
                };
            _swapped = false;
            _swapping = false;
            _swapFails = 0;
            _arrDirSet = false;
            _joined = false;
            var ship = _rig.ViewShip;
            _start = ship.position;
            _startRot = ship.rotation;
            _depDir = DepartureHeading(_start, destSystem);
            var span = (float)(_destTime - now);
            _departSeconds = Mathf.Min(mode switch
            {
                VoyageMode.Sublight => 14f,
                VoyageMode.Hyperspace => 5.5f,
                VoyageMode.PrlBond => 2.6f,
                _ => 3.2f
            }, Mathf.Max(1.2f, span * 0.35f));
            _fx.SetLook(mode);
            SetPhase(Phase.Depart);
            _fx.Cue(VoyageAudio.Spool, mode == VoyageMode.Sublight ? 0.35f : 0.6f);
            Departed?.Invoke(mode, destSystem);
        }

        /// <summary>Boarding a ship already between systems (the loader has put us in its destination).</summary>
        void Join(FocusFleet f)
        {
            var now = Now();
            _fleetId = f.Id;
            _mode = VoyageLog.Resolve(f, (long)now);
            _look = VoyageFx.LookOf(_mode);
            _originSystem = f.FromSystemId;
            _destSystem = f.SystemId;
            _gateFrom = 0;
            _gateTo = f.PlanetId;
            _destTime = f.DestTime;
            _startedUnix = now - 1.0;
            _swapped = true;
            _swapping = false;
            _arrDirSet = false;
            _joined = true;
            _fx.SetLook(_mode);
            _holdPos = _rig.ViewShip.position;
            _holdRot = _rig.ViewShip.rotation;
            if (_destTime - now > ArriveSecondsFor(_mode) + 1.5)
                EnterCruise(quiet: true);
            else
                EnterArrive();
        }

        void Stop()
        {
            _phase = Phase.Idle;
            _fx.Off();
            if (_exterior != null)
                _exterior.SetContentVisible(true);
        }

        // ── Phases ────────────────────────────────────────────────────────────────

        void SetPhase(Phase p)
        {
            _phase = p;
            _phaseAt = Time.unscaledTime;
        }

        void EnterCruise(bool quiet = false)
        {
            _holdPos = _lastPos == default ? _rig.ViewShip.position : _lastPos;
            _holdRot = _lastPos == default ? _rig.ViewShip.rotation : _lastRot;
            SetPhase(Phase.Cruise);
            _exterior.SetContentVisible(false);
            if (!quiet)
            {
                switch (_mode)
                {
                    case VoyageMode.Hyperspace:
                        _fx.Flash(_look.Bright, 1f);
                        _fx.Cue(VoyageAudio.Jump, 0.8f);
                        break;
                    case VoyageMode.PrlBond:
                        _fx.Flash(_look.Bright, 1.1f, 120f);
                        _fx.Cue(VoyageAudio.Bond, 0.7f);
                        _fx.Cue(VoyageAudio.Jump, 0.45f);
                        break;
                    case VoyageMode.Jumpgate:
                        _fx.Flash(_look.Bright, 0.9f, 80f);
                        _fx.Cue(VoyageAudio.Jump, 0.7f);
                        break;
                }
            }

            Transit?.Invoke(_mode, _destSystem);
            TrySwap();
        }

        void EnterArrive()
        {
            var now = Now();
            _arriveFrom = now;
            var left = (float)(_destTime - now);
            _arriveSeconds = Mathf.Clamp(left, 1.2f, ArriveSecondsFor(_mode));
            SetPhase(Phase.Arrive);
            _exterior.SetContentVisible(true);
            switch (_mode)
            {
                case VoyageMode.Hyperspace:
                    _fx.Flash(_look.Bright, 1f, 180f);
                    _fx.Shock(_look.Bright);
                    _fx.Cue(VoyageAudio.DropOut, 0.75f);
                    break;
                case VoyageMode.PrlBond:
                    _fx.Flash(_look.Bright, 1f, 90f);
                    _fx.Shock(_look.Bright, 18f, 140f);
                    _fx.Cue(VoyageAudio.Bond, 0.6f);
                    break;
                case VoyageMode.Jumpgate:
                    ExteriorJumpgateFx.Instance?.OpenGate(ArrivalGatePlanet());
                    _fx.Flash(_look.Bright, 0.8f, 60f);
                    _fx.Cue(VoyageAudio.DropOut, 0.6f);
                    break;
            }

            Arriving?.Invoke(_mode, _destSystem);
        }

        static float ArriveSecondsFor(VoyageMode mode) => mode switch
        {
            VoyageMode.Sublight => 18f,
            VoyageMode.Hyperspace => 4.5f,
            VoyageMode.PrlBond => 2.8f,
            _ => 5f
        };

        void TrySwap()
        {
            if (_swapped || _swapping || Time.unscaledTime < _retryAt)
                return;
            if (_destSystem > 0 && _focus.SystemId == _destSystem)
            {
                _swapped = true;
                return;
            }

            if (_destSystem <= 0 || _loader == null)
                return;
            _swapping = true;
            AsyncTap.Run(Swap());
        }

        async System.Threading.Tasks.Task Swap()
        {
            var ok = false;
            try
            {
                ok = await _loader.LoadShipView(_fleetId, _destSystem, fade: false);
            }
            finally
            {
                _swapping = false;
            }

            if (_phase == Phase.Idle)
                return;
            if (ok && _focus.SystemId == _destSystem)
            {
                _swapped = true;
                _arrDirSet = false;
                return;
            }

            _retryAt = Time.unscaledTime + 2f;
            if (++_swapFails >= 4)
                HandOver();
        }

        /// <summary>Could not fly this trip through: let the loader take the ship where it is, its own way.</summary>
        void HandOver()
        {
            Stop();
            var f = _focus.FindViewFleet();
            if (f != null && f.SystemId > 0 && f.SystemId != _focus.SystemId)
                AsyncTap.Run(_loader.LoadShipView(f.Id, f.SystemId));
        }

        // ── Frame ─────────────────────────────────────────────────────────────────

        void Update()
        {
            if (_phase == Phase.Idle)
                return;
            var now = Now();
            var t = Time.unscaledTime - _phaseAt;
            switch (_phase)
            {
                case Phase.Depart:
                    DepartFx(Mathf.Clamp01(t / _departSeconds));
                    if (t >= _departSeconds)
                        EnterCruise();
                    break;

                case Phase.Cruise:
                    CruiseFx(t);
                    TrySwap();
                    if (_swapped && !_swapping && now >= _destTime - ArriveSecondsFor(_mode))
                        EnterArrive();
                    else if (!_swapped && !_swapping && now > _destTime + 15.0)
                        HandOver(); // never learnt where the ship went
                    break;

                case Phase.Arrive:
                    var u = Mathf.Clamp01((float)((now - _arriveFrom) / _arriveSeconds));
                    ArriveFx(u);
                    if (u >= 1f)
                        Finish();
                    break;
            }
        }

        void Finish()
        {
            _phase = Phase.Idle;
            _fx.Off();
            _exterior.SetContentVisible(true);
        }

        void DepartFx(float u)
        {
            switch (_mode)
            {
                case VoyageMode.Sublight:
                    _fx.StreakRate = Mathf.Lerp(0f, 110f, u);
                    _fx.StreakSpeed = Mathf.Lerp(40f, 260f, u * u);
                    _fx.StreakStretch = Mathf.Lerp(0.04f, 0.15f, u);
                    _fx.TunnelOpen = SublightSheath * Mathf.Clamp01((u - 0.6f) / 0.4f);
                    _fx.TunnelFlow = 0.35f;
                    _fx.HumVolume = 0.1f;
                    _fx.HumPitch = Mathf.Lerp(0.7f, 1f, u);
                    _fx.Wash = _look.Wash * u;
                    break;
                case VoyageMode.Hyperspace:
                    // Stars start to smear as the drive winds up.
                    _fx.StreakRate = Mathf.Lerp(10f, 160f, u);
                    _fx.StreakSpeed = Mathf.Lerp(120f, 900f, u * u);
                    _fx.StreakStretch = Mathf.Lerp(0.04f, 0.22f, u * u);
                    // The vortex starts to form around the hull before the jump tears it open.
                    _fx.TunnelOpen = 0.35f * Mathf.SmoothStep(0f, 1f, (u - 0.45f) / 0.55f);
                    _fx.TunnelFlow = Mathf.Lerp(0.2f, 1.2f, u);
                    _fx.HumVolume = 0.22f * u;
                    _fx.HumPitch = Mathf.Lerp(0.6f, 1.1f, u);
                    _fx.Wash = _look.Wash * (u * 0.8f);
                    break;
                case VoyageMode.PrlBond:
                    // The bond lattice closes on the hull.
                    _fx.CageOpen = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 2.5f));
                    _fx.CageRadius = Mathf.Lerp(150f, 34f, u * u);
                    _fx.StreakRate = 0f;
                    _fx.HumVolume = 0.2f * u;
                    _fx.HumPitch = Mathf.Lerp(1f, 1.6f, u);
                    _fx.Wash = _look.Wash * u;
                    break;
                case VoyageMode.Jumpgate:
                    _fx.StreakRate = Mathf.Lerp(0f, 60f, u);
                    _fx.StreakSpeed = Mathf.Lerp(80f, 500f, u);
                    _fx.StreakColor = _look.Streak;
                    _fx.StreakStretch = Mathf.Lerp(0.04f, 0.12f, u);
                    _fx.HumVolume = 0.15f * u;
                    _fx.Wash = _look.Wash * (u * u);
                    break;
            }
        }

        void CruiseFx(float t)
        {
            var open = Mathf.Clamp01(t / 0.7f);
            switch (_mode)
            {
                case VoyageMode.Sublight:
                    _fx.StreakRate = 110f;
                    _fx.StreakSpeed = 260f;
                    _fx.StreakStretch = 0.15f;
                    _fx.TunnelOpen = SublightSheath;
                    _fx.TunnelFlow = 0.35f;
                    _fx.HumVolume = 0.1f;
                    _fx.HumPitch = 1f;
                    _fx.Wash = _look.Wash;
                    // The star we left shrinks astern, the one we head for swells ahead.
                    _fx.GlintAft = Mathf.Clamp01(1f - t / 45f) * 0.55f;
                    _fx.GlintAhead = Mathf.Lerp(0.05f, 0.75f, Progress);
                    _fx.GlintAheadColor = StarColor();
                    break;
                case VoyageMode.Hyperspace:
                    _fx.TunnelOpen = open;
                    _fx.TunnelFlow = 1.5f;
                    _fx.StreakRate = Mathf.Lerp(160f, 55f, open);
                    _fx.StreakSpeed = 2400f;
                    _fx.StreakStretch = 0.12f;
                    _fx.StreakColor = _look.Streak;
                    _fx.HumVolume = 0.28f;
                    _fx.HumPitch = 1f;
                    _fx.Wash = _look.Wash * (0.85f + 0.15f * Mathf.Sin(t * 1.7f));
                    break;
                case VoyageMode.PrlBond:
                    _fx.TunnelOpen = open;
                    _fx.TunnelFlow = 2.8f;
                    _fx.CageOpen = Mathf.Clamp01(1f - t / 0.5f);
                    _fx.StreakRate = 40f;
                    _fx.StreakSpeed = 3000f;
                    _fx.StreakStretch = 0.1f;
                    _fx.StreakColor = _look.Streak;
                    _fx.HumVolume = 0.24f;
                    _fx.HumPitch = 1.35f;
                    _fx.Wash = _look.Wash;
                    break;
                case VoyageMode.Jumpgate:
                    _fx.TunnelOpen = open;
                    _fx.TunnelFlow = 1.9f;
                    _fx.StreakRate = 50f;
                    _fx.StreakSpeed = 1800f;
                    _fx.StreakStretch = 0.12f;
                    _fx.HumVolume = 0.25f;
                    _fx.HumPitch = 0.85f;
                    _fx.Wash = _look.Wash * (0.9f + 0.1f * Mathf.Sin(t * 2.3f));
                    break;
            }
        }

        void ArriveFx(float u)
        {
            var k = 1f - u;
            _fx.TunnelOpen = (_mode == VoyageMode.Sublight ? SublightSheath : 1f) * Mathf.Clamp01(1f - u * (_mode == VoyageMode.Sublight ? 2f : 5f));
            _fx.CageOpen = 0f;
            _fx.GlintAhead = 0f;
            _fx.GlintAft = 0f;
            _fx.HumVolume = 0.25f * k;
            _fx.Wash = _look.Wash * (k * k);
            switch (_mode)
            {
                case VoyageMode.Sublight:
                    _fx.StreakRate = 110f * k;
                    _fx.StreakSpeed = Mathf.Lerp(260f, 30f, u);
                    _fx.StreakStretch = Mathf.Lerp(0.15f, 0.04f, u);
                    _fx.HumVolume = 0.1f * k;
                    break;
                default:
                    // Light-lines collapse back into stars.
                    _fx.StreakRate = u < 0.25f ? 90f * (1f - u * 4f) : 0f;
                    _fx.StreakSpeed = Mathf.Lerp(1600f, 60f, Mathf.Clamp01(u * 3f));
                    _fx.StreakStretch = Mathf.Lerp(0.12f, 0.02f, Mathf.Clamp01(u * 3f));
                    break;
            }
        }

        Color StarColor()
        {
            if (_focus == null || _focus.SystemId != _destSystem)
                return new Color(1f, 0.92f, 0.78f);
            var kit = SystemBodyKit.Star(_focus.SystemTypeKey, _focus.SystemType);
            return Color.Lerp(kit.Color, Color.white, 0.35f);
        }

        // ── Pose (read by BridgeViewRig every frame) ──────────────────────────────

        /// <summary>
        /// Where the inhabited ship is this frame while a trip is flown here. <paramref name="rest"/> is where it
        /// would sit otherwise (its orbit / berth in the focused system): the arrival ends exactly there.
        /// </summary>
        public bool TryPose(Vector3 rest, out Vector3 pos, out Quaternion rot)
        {
            pos = rest;
            rot = Quaternion.identity;
            if (_phase == Phase.Idle)
                return false;
            var now = Now();
            switch (_phase)
            {
                case Phase.Depart:
                    DepartPose(Mathf.Clamp01((Time.unscaledTime - _phaseAt) / _departSeconds), out pos, out rot);
                    break;
                case Phase.Cruise:
                    if (_swapped)
                    {
                        // Parked where the approach will begin; the bow comes round onto the new course.
                        ArrivePose(rest, 0f, out pos, out var arriveRot);
                        var turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - _phaseAt) / 8f));
                        rot = _joined ? arriveRot : Quaternion.Slerp(_holdRot, arriveRot, turn);
                    }
                    else
                    {
                        pos = _holdPos;
                        rot = _holdRot;
                    }

                    break;
                case Phase.Arrive:
                    ArrivePose(rest, Mathf.Clamp01((float)((now - _arriveFrom) / _arriveSeconds)), out pos, out rot);
                    break;
            }

            _lastPos = pos;
            _lastRot = rot;
            return true;
        }

        void DepartPose(float u, out Vector3 pos, out Quaternion rot)
        {
            var course = Quaternion.LookRotation(_depDir, Vector3.up);
            switch (_mode)
            {
                case VoyageMode.Sublight:
                    rot = Quaternion.Slerp(_startRot, course, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 2f)));
                    pos = _start + _depDir * (SublightOut * Mathf.Pow(u, 2.4f));
                    return;
                case VoyageMode.PrlBond:
                    rot = Quaternion.Slerp(_startRot, course, Mathf.SmoothStep(0f, 1f, u));
                    pos = _start + _depDir * (10f * u);
                    return;
                case VoyageMode.Jumpgate:
                    if (ExteriorJumpgateFx.Instance != null && ExteriorJumpgateFx.Instance.TryGatePose(_gateFrom, out var gp, out var gr))
                    {
                        var axis = gr * Vector3.forward;
                        if (Vector3.Dot(gp - _start, axis) < 0f)
                            axis = -axis;
                        _gateAxis = axis;
                        var s = Mathf.Pow(u, 1.6f);
                        var p1 = gp - axis * 110f;
                        var p2 = gp + axis * 30f;
                        pos = Bezier(_start, p1, p2, s);
                        var tangent = BezierTangent(_start, p1, p2, s);
                        var along = tangent.sqrMagnitude > 0.01f ? Quaternion.LookRotation(tangent.normalized, Vector3.up) : course;
                        rot = Quaternion.Slerp(_startRot, along, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 3f)));
                        return;
                    }

                    goto default;
                default:
                    rot = Quaternion.Slerp(_startRot, course, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.6f)));
                    var k = Mathf.Clamp01((u - 0.4f) / 0.6f);
                    pos = _start + _depDir * (160f * k * k);
                    return;
            }
        }

        void ArrivePose(Vector3 rest, float u, out Vector3 pos, out Quaternion rot)
        {
            if (!_arrDirSet)
            {
                _arrDir = ArrivalHeading(rest);
                _arrDirSet = true;
            }

            rot = Quaternion.LookRotation(_arrDir, Vector3.up);
            var k = 1f - u;
            switch (_mode)
            {
                case VoyageMode.Sublight:
                    pos = rest - _arrDir * (SublightIn * k * k);
                    return;
                case VoyageMode.PrlBond:
                    pos = rest - _arrDir * (BondIn * k * k);
                    return;
                case VoyageMode.Jumpgate:
                    if (ExteriorJumpgateFx.Instance != null &&
                        ExteriorJumpgateFx.Instance.TryGatePose(ArrivalGatePlanet(), out var gp, out var gr))
                    {
                        var axis = gr * Vector3.forward;
                        if (Vector3.Dot(rest - gp, axis) < 0f)
                            axis = -axis;
                        var s = 1f - k * k;
                        var p0 = gp - axis * 25f;
                        var p1 = gp + axis * 90f;
                        pos = Bezier(p0, p1, rest, s);
                        var tangent = BezierTangent(p0, p1, rest, s);
                        if (tangent.sqrMagnitude > 0.01f)
                            rot = Quaternion.Slerp(Quaternion.LookRotation(tangent.normalized, Vector3.up), rot, s * s);
                        return;
                    }

                    goto default;
                default:
                    pos = rest - _arrDir * (HyperIn * k * k * k);
                    return;
            }
        }

        int ArrivalGatePlanet()
        {
            if (_gateTo > 0)
                return _gateTo;
            var f = _focus?.FindViewFleet();
            return f != null ? f.PlanetId : 0;
        }

        /// <summary>Out of the system: away from the star, leaning toward the destination's galactic bearing.</summary>
        Vector3 DepartureHeading(Vector3 from, int destSystem)
        {
            var star = _exterior.transform.position;
            var outward = Flat(from - star);
            if (outward.sqrMagnitude < 0.01f)
                outward = Flat(_startRot * Vector3.forward);
            if (outward.sqrMagnitude < 0.01f)
                outward = Vector3.forward;
            if (destSystem > 0 && GalaxyCatalog.TryGet(_originSystem, out var a) && GalaxyCatalog.TryGet(destSystem, out var b))
            {
                var bearing = Flat(new Vector3(b.X - a.X, 0f, b.Y - a.Y));
                if (bearing.sqrMagnitude > 0.01f)
                {
                    var lean = (outward + bearing * 0.5f).normalized;
                    if (Vector3.Dot(lean, outward) > 0.35f)
                        return lean;
                }
            }

            return outward;
        }

        /// <summary>Into the system toward our berth, a little off the star so it rises beside the target.</summary>
        Vector3 ArrivalHeading(Vector3 rest)
        {
            var inward = Flat(_exterior.transform.position - rest);
            if (inward.sqrMagnitude < 0.01f)
                inward = Vector3.forward;
            var side = (_fleetId & 1) == 0 ? 1f : -1f;
            return Quaternion.AngleAxis(22f * side, Vector3.up) * inward;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero;
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            var u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        static Vector3 BezierTangent(Vector3 a, Vector3 b, Vector3 c, float t) =>
            2f * (1f - t) * (b - a) + 2f * t * (c - b);

        static double Now() =>
            (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}
