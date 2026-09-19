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
        Vector3 _standLocalPos = new Vector3(0f, 0f, 0.4f);
        Vector3 _sitLocalPos = new Vector3(0f, 0.15f, -0.35f);
        bool _command;
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
                go.transform.localPosition = new Vector3(0f, 0.55f, 0.1f);
                go.transform.localScale = new Vector3(0.85f, 0.08f, 0.85f);
                Object.Destroy(go.GetComponent<Collider>());
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.7f;
                var art = seat.GetComponentInParent<CicEnvironment>()?.Art;
                if (art != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial =
                        art.Holo(Texture2D.whiteTexture, new Color(0.2f, 0.95f, 1f, 0.45f));
                zoneT = go.transform;

                var prompt = DiegeticUi.Label(go.transform, "SitPrompt", Trans.Get("CommandBridge"),
                    new Vector3(0f, 4f, 0f), 0.12f, 5f, Color.white);
                prompt.rectTransform.sizeDelta = new Vector2(30f, 6f);
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
                zoneT.localScale = new Vector3(0.95f, 0.09f, 0.95f);
                CicCue.Hover(zoneT.position);
            });
            interact.hoverExited.AddListener(_ =>
            {
                zoneT.localScale = new Vector3(0.85f, 0.08f, 0.85f);
            });
        }

        void WireExit(Transform pad)
        {
            if (pad == null)
                return;
            if (pad.GetComponent<Collider>() == null)
            {
                var box = pad.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.2f, 0.05f, 0.3f);
            }

            var interact = pad.GetComponent<XRSimpleInteractable>();
            if (interact == null)
                interact = pad.gameObject.AddComponent<XRSimpleInteractable>();
            var baseScale = pad.localScale;
            interact.selectEntered.AddListener(_ => Core.Utils.AsyncTap.Run(ExitCommandMode()));
            interact.hoverEntered.AddListener(_ =>
            {
                pad.localScale = baseScale * 1.1f;
                CicCue.Hover(pad.position);
            });
            interact.hoverExited.AddListener(_ => { pad.localScale = baseScale; });
            DiegeticUi.Label(pad, "ExitLabel", Trans.Get("quit"), new Vector3(0f, 0.08f, 0f),
                0.04f, 4f, Color.white);
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
