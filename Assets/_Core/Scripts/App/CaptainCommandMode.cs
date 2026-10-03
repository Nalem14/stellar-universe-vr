using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;

namespace Core.App
{
    /// <summary>
    /// The captain's chair → Command Mode (holo map enlarged and brought within the seated captain's reach,
    /// locomotion off). Point at the chair and pull the trigger (or grip) to sit, or press the arm key; stand
    /// up (or jump) to leave, or press the arm key again. Seated, the view is set so the eyes land at a seated
    /// captain's height over the cushion whether one plays standing or sitting for real.
    /// Never breaks XR → BridgeMount → ViewShip parenting.
    /// </summary>
    public class CaptainCommandMode : MonoBehaviour
    {
        public static CaptainCommandMode Instance { get; private set; }

        Transform _table;
        Transform _seat;
        Transform _exitPad;
        XROrigin _xr;
        /// <summary>Seated, the map grows this much (relative to its size at the table).</summary>
        const float CmdScale = 1.3f;
        /// <summary>Seated, the enlarged map's near edge stops this far ahead of the eyes (m): past the arm tablets.</summary>
        const float NearGap = 0.85f;
        /// <summary>Seated eyes above the seat cushion (m).</summary>
        const float EyeAboveSeat = 0.72f;
        /// <summary>Cushion top above the seat's centre, and the dais floor below the cushion top (m).</summary>
        const float CushionTop = 0.035f;
        const float SeatAboveFloor = 0.47f;
        /// <summary>Head rise over the seated height that means "standing up / jumping" (m).</summary>
        const float StandRise = 0.25f;
        Vector3 _scaleBeforeCmd = Vector3.one;
        Vector3 _posBeforeCmd;
        Vector3 _standLocalPos = WorldScale.CicCaptainStand;
        bool _command;
        bool _animating;
        MonoBehaviour[] _locomotion;
        Transform _ring;
        Transform _prompt;
        float _chairLift;
        float _seatedHead;
        float _riseSince = -1f;

        public bool IsCommandMode => _command;
        public event System.Action<bool> CommandModeChanged;

        /// <summary>The chair's seat (its forward faces the table), or null before binding.</summary>
        public Transform Seat => _seat;

        /// <summary>Where a seated captain's eyes are (world).</summary>
        public Vector3 SeatedEye() =>
            _seat.TransformPoint(new Vector3(0f, CushionTop, -0.13f)) + Vector3.up * EyeAboveSeat;

        /// <summary>The view lift the chair wants right now (m), or null off the chair (<see cref="ComfortSettings"/>).</summary>
        public float? ViewLift { get; private set; }

        public void Bind(Transform table, Transform seat, Transform exitPad)
        {
            Instance = this;
            _table = table;
            _seat = seat;
            _exitPad = exitPad;

            WireSeat(seat);
            WireExit(exitPad);
            CacheLocomotion();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void WireSeat(Transform seat)
        {
            if (seat == null)
                return;

            // A faint ring on the cushion and a "Sit" prompt over it, lit when the ray is on the chair.
            if (_ring == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "SitZone";
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(seat, false);
                go.transform.localPosition = new Vector3(0f, CushionTop + 0.004f, 0f);
                go.transform.localScale = new Vector3(0.5f, 0.002f, 0.46f);
                var art = seat.GetComponentInParent<CicEnvironment>()?.Art;
                go.GetComponent<MeshRenderer>().sharedMaterial = art != null
                    ? art.Holo(art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture, new Color(0.2f, 0.95f, 1f, 0.3f))
                    : CombatFxKit.Glow();
                _ring = go.transform;

                // Prompt in metres on an unscaled socket above the seat, turned toward the standing captain.
                var promptMount = Core.UI.ScreenMount.Socket(seat, "SitPromptMount",
                    new Vector3(0f, 0.5f, 0.12f), Quaternion.identity);
                var standEye = seat.parent != null
                    ? seat.parent.TransformPoint(_standLocalPos + Vector3.up * WorldScale.EyeStanding)
                    : seat.position + Vector3.up * 1.6f;
                Core.UI.ScreenMount.FaceViewer(promptMount, standEye, 0.6f);
                Core.UI.UiKit.Label(promptMount, "SitPrompt", Trans.Get("vr.seat.sit"), Vector3.zero,
                    0.5f, 0.045f, Core.UI.UiKit.TextBright);
                _prompt = promptMount;
                SetHover(false);
            }

            // The whole chair is the target: trigger (TriggerSelect) or grip on it sits down.
            var interact = seat.GetComponent<XRSimpleInteractable>();
            if (interact == null)
                interact = seat.gameObject.AddComponent<XRSimpleInteractable>();
            interact.selectEntered.RemoveAllListeners();
            interact.selectEntered.AddListener(_ =>
            {
                if (!_command)
                    Core.Utils.AsyncTap.Run(EnterCommandMode());
            });
            interact.hoverEntered.AddListener(_ =>
            {
                if (_command)
                    return;
                SetHover(true);
                CicCue.Hover(seat.position);
            });
            interact.hoverExited.AddListener(_ => SetHover(false));
            _seatInteract = interact;
            _seatColliders = seat.GetComponents<Collider>();
        }

        XRSimpleInteractable _seatInteract;
        Collider[] _seatColliders;

        /// <summary>
        /// The sit box wraps cushion and back, so seated the hands rest in and around it: the near caster would
        /// hover the chair and the far rays would start inside it. Seated, the chair is no target at all.
        /// </summary>
        void SetSeatTarget(bool on)
        {
            if (_seatInteract != null)
                _seatInteract.enabled = on;
            if (_seatColliders == null)
                return;
            foreach (var c in _seatColliders)
                if (c != null)
                    c.enabled = on;
        }

        void SetHover(bool on)
        {
            if (_ring != null)
                _ring.localScale = on ? new Vector3(0.56f, 0.002f, 0.52f) : new Vector3(0.5f, 0.002f, 0.46f);
            if (_prompt != null)
                _prompt.localScale = Vector3.one * (on ? 1.15f : 1f);
        }

        void WireExit(Transform pad)
        {
            if (pad == null)
                return;
            // Physical poke on the right arm console (unscaled rounded pad): sit down / stand up.
            _seatButton = Core.UI.ArmConsole.Button(pad, 1, 0, "ExitCommand", Trans.Get("vr.seat.sit"), CicArtKit.Amber,
                () => Core.Utils.AsyncTap.Run(_command ? ExitCommandMode() : EnterCommandMode()));
            CommandModeChanged += _ => RelabelSeatButton();
        }

        Core.UI.ArmKey _seatButton;

        void RelabelSeatButton()
        {
            if (_seatButton != null && _seatButton.Label != null)
                _seatButton.Label.text = Trans.Get(_command ? "vr.seat.stand" : "vr.seat.sit");
        }

        void CacheLocomotion()
        {
            _xr = FindFirstObjectByType<XROrigin>();
            if (_xr == null)
                return;
            var list = new System.Collections.Generic.List<MonoBehaviour>();
            foreach (var mb in _xr.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var n = mb.GetType().Name;
                if (n.IndexOf("Locomotion", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("ContinuousMove", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("DynamicMove", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("TeleportationProvider", System.StringComparison.Ordinal) >= 0)
                    list.Add(mb);
            }

            _locomotion = list.ToArray();
        }

        /// <summary>The head's real height over the play-space floor (m), the view lift taken out.</summary>
        float RealHeadHeight(Camera cam) => cam.transform.position.y - _xr.transform.position.y - ComfortSettings.CurrentLift;

        /// <summary>
        /// Seated: standing up for real, or a jump, lifts the captain out of the chair. The seated height is the
        /// lowest the head has settled (slowly let back up, so a straightened back is not a rise).
        /// </summary>
        void Update()
        {
            if (!_command || _animating)
                return;

            if (PcPlatformBoot.IsPcDesktop && !PcPlatformBoot.IsTyping && !Core.UI.FlatGrab.Holding)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    {
                        Core.Utils.AsyncTap.Run(ExitCommandMode());
                        return;
                    }

                    if (kb.fKey.wasPressedThisFrame)
                    {
                        var tp = Object.FindFirstObjectByType<BridgeViewTeleporter>();
                        if (tp != null)
                            Core.Utils.AsyncTap.Run(tp.RefreshList());
                    }
                    else if (kb.mKey.wasPressedThisFrame)
                    {
                        var mapCtrl = Object.FindFirstObjectByType<HoloMapController>();
                        if (mapCtrl != null)
                            mapCtrl.SetMode(mapCtrl.Mode == HoloMapMode.System ? HoloMapMode.Galaxy : HoloMapMode.System);
                    }
                    else if (kb.cKey.wasPressedThisFrame)
                    {
                        var comms = Core.Stations.CommsConsole.Instance;
                        if (comms != null)
                        {
                            if (comms.IsOpen) comms.Close();
                            else comms.Open(null);
                        }
                    }
                    else if (kb.oKey.wasPressedThisFrame)
                    {
                        var ops = Core.Stations.OpsConsole.Instance;
                        if (ops != null)
                        {
                            if (ops.IsOpen) ops.Close();
                            else ops.Open(null, FocusContext.Current != null ? FocusContext.Current.ViewPlanetId : 0);
                        }
                    }
                    else if (kb.tKey.wasPressedThisFrame)
                    {
                        var armory = Core.Stations.ArmoryConsole.Instance;
                        if (armory != null)
                        {
                            if (armory.IsOpen) armory.Close();
                            else armory.Open(null, 0, 0);
                        }
                    }
                }
            }

            var cam = Camera.main;
            if (cam == null || _xr == null)
                return;
            var h = RealHeadHeight(cam);
            _seatedHead = Mathf.Min(h, _seatedHead + 0.02f * Time.deltaTime);
            if (h > _seatedHead + StandRise)
            {
                if (_riseSince < 0f)
                    _riseSince = Time.time;
                else if (Time.time - _riseSince > 0.08f)
                {
                    _riseSince = -1f;
                    Core.Utils.AsyncTap.Run(ExitCommandMode());
                }
            }
            else
            {
                _riseSince = -1f;
            }
        }

        public async Task EnterCommandMode()
        {
            if (_command || _animating)
                return;
            _xr = _xr != null ? _xr : FindFirstObjectByType<XROrigin>();
            var cam = Camera.main;
            if (_xr == null || cam == null || _seat == null)
                return;
            _animating = true;
            SetLocomotion(false);
            SetHover(false);
            SetSeatTarget(false);

            // Eyes over the back third of the cushion, facing the table; the view lifted (or lowered) so they sit
            // EyeAboveSeat over it, whatever the player's real posture.
            var cushion = _seat.TransformPoint(new Vector3(0f, CushionTop, 0f)).y;
            var floor = cushion - SeatAboveFloor;
            var head = _seat.TransformPoint(new Vector3(0f, 0f, -0.13f));
            var real = RealHeadHeight(cam);
            _chairLift = Mathf.Clamp(cushion + EyeAboveSeat - floor - real, -0.9f, 0.7f);
            var fromLift = ComfortSettings.CurrentLift;
            _xr.MatchOriginUpCameraForward(Vector3.up, Vector3.ProjectOnPlane(_seat.forward, Vector3.up).normalized);
            var eye = cam.transform.position;
            var toOrigin = _xr.transform.position + new Vector3(head.x - eye.x, 0f, head.z - eye.z);
            toOrigin.y = floor;
            await AnimatePose(true, toOrigin, fromLift, _chairLift);
            _seatedHead = real;
            _riseSince = -1f;
            _command = true;
            _animating = false;
            SetSeatCueVisible(false);
            if (PcPlatformBoot.IsPcDesktop)
                PcDesktopController.Instance?.SetCursorLock(false);
            CommandModeChanged?.Invoke(true);
        }

        public async Task ExitCommandMode()
        {
            if (!_command || _animating)
                return;
            var cam = Camera.main;
            _animating = true;
            var parent = _xr != null ? _xr.transform.parent : null;
            var stand = parent != null ? parent.TransformPoint(_standLocalPos) : _xr.transform.position;
            var toOrigin = _xr.transform.position;
            if (cam != null)
            {
                var eye = cam.transform.position;
                toOrigin += new Vector3(stand.x - eye.x, 0f, stand.z - eye.z);
            }

            toOrigin.y = stand.y;
            await AnimatePose(false, toOrigin, _chairLift, ComfortSettings.StandingLift);
            ViewLift = null;
            SetLocomotion(true);
            _command = false;
            _animating = false;
            SetSeatCueVisible(true);
            SetSeatTarget(true);
            if (PcPlatformBoot.IsPcDesktop)
                PcDesktopController.Instance?.SetCursorLock(true);
            CommandModeChanged?.Invoke(false);
        }

        async Task AnimatePose(bool sit, Vector3 toOrigin, float fromLift, float toLift)
        {
            var duration = 0.45f;
            var t0 = Time.time;
            var fromScale = _table != null ? _table.localScale : Vector3.one;
            // Preserve current holomap zoom: enlarge relative to scale at enter, restore that on exit.
            if (sit)
                _scaleBeforeCmd = fromScale;
            var toScale = sit ? _scaleBeforeCmd * CmdScale : _scaleBeforeCmd;
            var fromTablePos = _table != null ? _table.localPosition : Vector3.zero;
            if (sit)
                _posBeforeCmd = fromTablePos;
            var toTablePos = sit ? _posBeforeCmd + SeatDirection() * SlideToSeat(toScale.x) : _posBeforeCmd;
            var fromOrigin = _xr != null ? _xr.transform.position : toOrigin;

            while (Time.time - t0 < duration)
            {
                var u = MotionEase.SmoothInOut((Time.time - t0) / duration);
                if (_table != null)
                {
                    _table.localScale = Vector3.Lerp(fromScale, toScale, u);
                    _table.localPosition = Vector3.Lerp(fromTablePos, toTablePos, u);
                }

                ViewLift = Mathf.Lerp(fromLift, toLift, u);
                if (_xr != null)
                    _xr.transform.position = Vector3.Lerp(fromOrigin, toOrigin, u);
                await Task.Yield();
            }

            if (_table != null)
            {
                _table.localScale = toScale;
                _table.localPosition = toTablePos;
            }

            ViewLift = toLift;
            if (_xr != null)
                _xr.transform.position = toOrigin;
        }

        /// <summary>How far the map slides toward the chair so its near edge stops <see cref="NearGap"/> ahead of the eyes.</summary>
        float SlideToSeat(float scale)
        {
            if (_table == null || _seat == null || _table.parent == null)
                return 0f;
            var eye = _table.parent.InverseTransformPoint(_seat.TransformPoint(new Vector3(0f, 0f, -0.13f)));
            var d = eye - _posBeforeCmd;
            d.y = 0f;
            return Mathf.Max(0f, d.magnitude - NearGap - WorldScale.HoloDiscRadius * scale);
        }

        /// <summary>Horizontal direction from the table to the chair, in the table's parent space.</summary>
        Vector3 SeatDirection()
        {
            if (_table == null || _seat == null || _table.parent == null)
                return Vector3.back;
            var d = _table.parent.InverseTransformPoint(_seat.position) - _table.localPosition;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.back;
        }

        /// <summary>The sit ring and its prompt only make sense standing.</summary>
        void SetSeatCueVisible(bool on)
        {
            if (_ring != null)
            {
                var r = _ring.GetComponent<MeshRenderer>();
                if (r != null)
                    r.enabled = on;
            }

            if (_prompt != null)
                _prompt.gameObject.SetActive(on);
        }

        void SetLocomotion(bool on)
        {
            if (_locomotion == null)
                return;
            for (var i = 0; i < _locomotion.Length; i++)
            {
                if (_locomotion[i] != null)
                    _locomotion[i].enabled = on;
            }
        }
    }
}
