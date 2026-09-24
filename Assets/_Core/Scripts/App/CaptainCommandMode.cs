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
    /// Sit at CaptainSeat → Command Mode (table enlarge, locomotion off). Exit via arm pad.
    /// Never breaks XR → BridgeMount → ViewShip parenting.
    /// </summary>
    public class CaptainCommandMode : MonoBehaviour
    {
        public static CaptainCommandMode Instance { get; private set; }

        Transform _table;
        Transform _seat;
        Transform _exitPad;
        XROrigin _xr;
        Vector3 _tableCmdScale = new Vector3(1.55f, 1.55f, 1.55f);
        Vector3 _scaleBeforeCmd = Vector3.one;
        Vector3 _standLocalPos = WorldScale.CicCaptainStand;
        Vector3 _sitLocalPos = new Vector3(0f, 0.15f, -0.35f);
        bool _command;
        Vector3 _zoneRest = Vector3.one;
        bool _animating;
        MonoBehaviour[] _locomotion;

        public bool IsCommandMode => _command;
        public event System.Action<bool> CommandModeChanged;

        public void Bind(Transform table, Transform seat, Transform exitPad)
        {
            Instance = this;
            _table = table;
            _seat = seat;
            _exitPad = exitPad;

            WireSitZone(seat);
            WireExit(exitPad);
            CacheLocomotion();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void WireSitZone(Transform seat)
        {
            if (seat == null)
                return;

            // Large sit volume — obvious from standing, not a tiny seat collider.
            var zone = seat.Find("SitZone");
            Transform zoneT;
            if (zone == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "SitZone";
                go.transform.SetParent(seat, false);
                // Seat is a stretched box: size the disc in metres through its inverse scale.
                var inv = seat.lossyScale;
                go.transform.localPosition = new Vector3(0f, 0.5f + 0.004f / Mathf.Max(0.001f, inv.y), 0.05f);
                go.transform.localScale = new Vector3(0.56f / inv.x, 0.002f / inv.y, 0.52f / inv.z);
                _zoneRest = go.transform.localScale;
                Object.Destroy(go.GetComponent<Collider>());
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.7f;
                var art = seat.GetComponentInParent<CicEnvironment>()?.Art;
                // A faint ring on the cushion, not a saturated disc.
                if (art != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial =
                        art.Holo(art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture,
                            new Color(0.2f, 0.95f, 1f, 0.3f));
                zoneT = go.transform;

                // Prompt in metres on an unscaled socket above the seat, turned toward the standing
                // captain (never a child of the squashed sit disc).
                var promptMount = Core.UI.ScreenMount.Socket(seat, "SitPromptMount",
                    new Vector3(0f, 0.62f, 0.2f), Quaternion.identity);
                var standEye = seat.parent != null
                    ? seat.parent.TransformPoint(_standLocalPos + Vector3.up * 1.6f)
                    : seat.position + Vector3.up * 1.6f;
                Core.UI.ScreenMount.FaceViewer(promptMount, standEye, 0.6f);
                Core.UI.UiKit.Label(promptMount, "SitPrompt", Trans.Get("CommandBridge"), Vector3.zero,
                    0.5f, 0.045f, Core.UI.UiKit.TextBright);
            }
            else
            {
                zoneT = zone;
            }

            var interact = zoneT.GetComponent<XRSimpleInteractable>();
            if (interact == null)
                interact = zoneT.gameObject.AddComponent<XRSimpleInteractable>();
            interact.selectEntered.RemoveAllListeners();
            interact.selectEntered.AddListener(_ => Core.Utils.AsyncTap.Run(EnterCommandMode()));
            interact.hoverEntered.AddListener(_ =>
            {
                zoneT.localScale = Vector3.Scale(_zoneRest, new Vector3(1.12f, 1f, 1.12f));
                CicCue.Hover(zoneT.position);
            });
            interact.hoverExited.AddListener(_ =>
            {
                zoneT.localScale = _zoneRest;
            });
        }

        void WireExit(Transform pad)
        {
            if (pad == null)
                return;
            // Physical poke on the right arm console (unscaled rounded pad) — stand up / leave the seat.
            Core.UI.PokeButton.Create(pad, "ExitCommand", Trans.Get("quit"), BridgeDirector.ArmPadTop,
                BridgeDirector.ArmPadFaceUp, new Vector2(0.13f, 0.06f), CicArtKit.Amber,
                () => Core.Utils.AsyncTap.Run(ExitCommandMode()));
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
                    n.IndexOf("DynamicMove", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    list.Add(mb);
            }

            _locomotion = list.ToArray();
        }

        public async Task EnterCommandMode()
        {
            if (_command || _animating)
                return;
            _animating = true;
            _xr = _xr != null ? _xr : FindFirstObjectByType<XROrigin>();
            SetLocomotion(false);
            await AnimatePose(true);
            _command = true;
            _animating = false;
            CommandModeChanged?.Invoke(true);
        }

        public async Task ExitCommandMode()
        {
            if (!_command || _animating)
                return;
            _animating = true;
            await AnimatePose(false);
            SetLocomotion(true);
            _command = false;
            _animating = false;
            CommandModeChanged?.Invoke(false);
        }

        async Task AnimatePose(bool sit)
        {
            var duration = 0.4f;
            var t0 = Time.time;
            var fromScale = _table != null ? _table.localScale : Vector3.one;
            // Preserve current holomap zoom: enlarge relative to scale at enter, restore that on exit.
            if (sit)
                _scaleBeforeCmd = fromScale;
            var toScale = sit
                ? Vector3.Scale(_scaleBeforeCmd, _tableCmdScale)
                : _scaleBeforeCmd;
            var fromPos = _xr != null ? _xr.transform.localPosition : _standLocalPos;
            var toPos = sit ? ResolveSitLocal() : _standLocalPos;

            while (Time.time - t0 < duration)
            {
                var u = MotionEase.SmoothInOut((Time.time - t0) / duration);
                if (_table != null)
                    _table.localScale = Vector3.Lerp(fromScale, toScale, u);
                if (_xr != null)
                    _xr.transform.localPosition = Vector3.Lerp(fromPos, toPos, u);
                await Task.Yield();
            }

            if (_table != null)
                _table.localScale = toScale;
            if (_xr != null)
                _xr.transform.localPosition = toPos;
        }

        Vector3 ResolveSitLocal()
        {
            if (_seat == null || _xr == null || _xr.transform.parent == null)
                return _sitLocalPos;
            // Seat world → BridgeMount local (XR parent).
            var world = _seat.position + Vector3.up * 0.55f + _seat.forward * 0.15f;
            return _xr.transform.parent.InverseTransformPoint(world);
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
