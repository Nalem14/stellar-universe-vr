using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Holo
{
    /// <summary>
    /// Captain's order lectern on the near rim of the holo table: after a fleet token is dropped, the order
    /// is quoted here (destination + one flat holo touch button per option, with ETA / crystal / refusal
    /// reason) and only leaves on Confirm. Cancel — or 20 s without an answer — puts the token back.
    /// Hidden when idle; reachable standing (poke) or seated (ray), both through the world canvas.
    /// </summary>
    public sealed class OrderConsole : MonoBehaviour
    {
        public readonly struct Option
        {
            public readonly string Label;
            public readonly bool Enabled;
            public readonly Color Accent;
            public readonly object Payload;

            public Option(string label, bool enabled, Color accent, object payload)
            {
                Label = label;
                Enabled = enabled;
                Accent = accent;
                Payload = payload;
            }
        }

        const int MaxOptions = 5;
        const float Timeout = 20f;
        /// <summary>
        /// A plate that opens under the hand must not take the press that was already there: options arm
        /// only after this delay (and after the finger has left them).
        /// </summary>
        const float ArmDelay = 0.6f;
        float _armAt;
        static readonly Vector2 Size = new(0.64f, 0.46f);
        const float OptionH = 48f;
        const float OptionStep = 54f;

        GameObject _screenGo;
        HoloScreen _screen;
        TMPro.TMP_Text _dest;
        readonly Button[] _options = new Button[MaxOptions];
        readonly TMP_Text[] _optionLabels = new TMP_Text[MaxOptions];
        Button _cancel;
        TaskCompletionSource<object> _pending;
        float _deadline;

        public bool IsOpen => _pending != null;

        /// <summary>
        /// The lectern closed a moment ago: the press or tap that answered it must not also land on the table
        /// underneath (a phone tap is handed to the world on finger-up, once the plate is already gone).
        /// </summary>
        public bool JustClosed => Time.unscaledTime - _closedAt < CloseGuard;

        const float CloseGuard = 0.35f;
        float _closedAt = -10f;

        /// <summary>The bridge's single lectern (crew Helm jumps and holomap drops share it).</summary>
        public static OrderConsole Instance { get; private set; }

        void Awake() => Instance = this;

        public static OrderConsole Build(Transform room)
        {
            var rig = new GameObject("OrderConsoleRig").transform;
            rig.SetParent(room, false);
            var console = rig.gameObject.AddComponent<OrderConsole>();

            // Over the near rim, ~0.45 m in front of the standing captain and ~40° under eye line:
            // read by lowering the eyes, poked with a relaxed arm; the map's near edge stays visible below.
            var rimZ = WorldScale.CicTableCenterZ - WorldScale.CicTableDiameter * 0.5f + 0.26f;
            console._screen = HoloScreen.Create(rig, "OrderConsole", Size, new Vector3(0f, 1.2f, rimZ),
                Quaternion.identity, Trans.Get("vr.order.confirm"));
            ScreenMount.FaceViewer(console._screen.transform,
                room.TransformPoint(WorldScale.CicCaptainStand + Vector3.up * WorldScale.EyeStanding), 1f, 8f);
            console._screen.SetAccent(UiKit.Cyan, 0.55f);
            console._screenGo = console._screen.gameObject;

            var px = console._screen.PixelSize;
            var content = console._screen.Content;
            console._dest = DiegeticUi.HoloLabel(content, string.Empty,
                new Vector2(0f, px.y * 0.3f), new Vector2(px.x * 0.9f, 40f), 26f, UiKit.TextBright);
            console._dest.enableAutoSizing = true;
            console._dest.fontSizeMin = 16f;
            console._dest.fontSizeMax = 26f;

            // Flat holo touch buttons on the glass (poke through the canvas, or ray): one per option.
            var top = px.y * 0.3f - 52f;
            for (var i = 0; i < MaxOptions; i++)
            {
                var index = i;
                var b = DiegeticUi.HoloButton(content, string.Empty, new Vector2(0f, top - i * OptionStep),
                    new Vector2(px.x - 60f, OptionH), () => console.Choose(index), DiegeticUi.BtnStyle.Cyan);
                b.name = "Option" + i;
                console._options[i] = b;
                console._optionLabels[i] = b.GetComponentInChildren<TMP_Text>();
                console._optionLabels[i].enableAutoSizing = true;
                console._optionLabels[i].fontSizeMin = 14f;
                console._optionLabels[i].fontSizeMax = 21f;
            }

            console._cancel = DiegeticUi.HoloButton(content, Trans.Get("cancel"),
                new Vector2(px.x * 0.5f - 130f, -px.y * 0.5f + 34f), new Vector2(200f, 42f),
                () => console.Resolve(null), DiegeticUi.BtnStyle.Danger);

            console._screenGo.SetActive(false);
            console.enabled = false;
            return console;
        }

        readonly List<Option> _current = new();
        Vector3 _homePos;
        Quaternion _homeRot;
        bool _homeSaved;

        /// <summary>
        /// Holo table v2: the quote opens beside the destination (above it, turned to the captain), so the
        /// eye never leaves the target. Falls back to the rim lectern when no point is given.
        /// </summary>
        public Task<object> AskAt(Vector3 worldTarget, string destination, IReadOnlyList<Option> options)
        {
            Resolve(null);
            var t = _screen.transform;
            if (!_homeSaved)
            {
                _homePos = t.localPosition;
                _homeRot = t.localRotation;
                _homeSaved = true;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                var eye = cam.transform.position;
                var toEye = eye - worldTarget;
                toEye.y = 0f;
                var near = toEye.sqrMagnitude > 1e-4f ? toEye.normalized : Vector3.back;
                // A little toward the captain and above the target, never inside the diorama.
                var p = worldTarget + near * 0.1f + Vector3.up * 0.3f;
                // Keep it within comfortable reach of the eye.
                var d = p - eye;
                if (d.magnitude > 1.1f)
                    p = eye + d.normalized * 1.1f;
                t.position = p;
                ScreenMount.FaceViewer(t, eye, 1f, 8f);
            }

            return Ask(destination, options);
        }

        /// <summary>
        /// The quote in front of the captain (a little low, facing them): for orders given away from the table —
        /// a crew console, a pad — where the rim lectern would open out of sight and time out unseen.
        /// </summary>
        public Task<object> AskHere(string destination, IReadOnlyList<Option> options)
        {
            var cam = Camera.main;
            if (cam == null)
                return Ask(destination, options);
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
                fwd = Vector3.forward;
            fwd.Normalize();
            // AskAt lifts its point by 0.3 m: aim below the eyes so the screen lands just under eye level.
            var target = cam.transform.position + fwd * 0.55f + Vector3.down * 0.45f;
            return AskAt(target, destination, options);
        }

        void RestoreHome()
        {
            if (!_homeSaved || _screen == null)
                return;
            _screen.transform.localPosition = _homePos;
            _screen.transform.localRotation = _homeRot;
        }

        /// <summary>Ask the captain. Resolves with the chosen option's payload, or null if cancelled.</summary>
        public Task<object> Ask(string destination, IReadOnlyList<Option> options)
        {
            if (_pending != null)
            {
                Resolve(null);
            }
            _current.Clear();
            for (var i = 0; i < options.Count && i < MaxOptions; i++)
                _current.Add(options[i]);

            _dest.text = destination ?? string.Empty;
            for (var i = 0; i < MaxOptions; i++)
            {
                var b = _options[i];
                var has = i < _current.Count;
                b.gameObject.SetActive(has);
                if (!has)
                    continue;
                _optionLabels[i].text = _current[i].Label ?? string.Empty;
                DiegeticUi.Restyle(b, StyleOf(_current[i].Accent));
                b.interactable = _current[i].Enabled;
                _optionLabels[i].color = _current[i].Enabled ? Color.white : UiKit.TextDim * 0.8f;
            }

            _pending = new TaskCompletionSource<object>();
            _deadline = Time.unscaledTime + Timeout;
            // Presses land only once armed (Choose checks the clock): the buttons keep their look meanwhile.
            _armAt = Time.unscaledTime + ArmDelay;
            _screenGo.SetActive(true);
            enabled = true;
            CicCue.Hover(_screen.transform.position);
            return _pending.Task;
        }

        /// <summary>Option accents → the three holo button skins (warm red = danger, warm = amber, else cyan).</summary>
        static DiegeticUi.BtnStyle StyleOf(Color c)
        {
            if (c.r > 0.8f && c.g < 0.5f)
                return DiegeticUi.BtnStyle.Danger;
            if (c.r > 0.8f && c.b < 0.6f)
                return DiegeticUi.BtnStyle.Amber;
            return DiegeticUi.BtnStyle.Cyan;
        }

        void Choose(int index)
        {
            if (_pending == null || index >= _current.Count || !_current[index].Enabled || Time.unscaledTime < _armAt)
                return;
            Resolve(_current[index].Payload);
        }

        void Resolve(object payload)
        {
            var pending = _pending;
            _pending = null;
            if (pending != null)
                _closedAt = Time.unscaledTime;
            if (_screenGo != null)
                _screenGo.SetActive(false);
            if (pending != null)
                RestoreHome();
            enabled = false;
            pending?.TrySetResult(payload);
        }

        void Update()
        {
            if (_pending != null && Time.unscaledTime > _deadline)
                Resolve(null);
        }

        void OnDestroy()
        {
            Resolve(null);
            if (Instance == this)
                Instance = null;
        }
    }
}
