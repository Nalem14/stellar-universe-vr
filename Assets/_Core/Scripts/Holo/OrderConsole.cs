using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>
    /// Captain's order lectern on the near rim of the holo table: after a fleet token is dropped, the order
    /// is quoted here (destination + one physical button per travel option, with ETA / crystal / refusal
    /// reason) and only leaves on Confirm. Cancel — or 20 s without an answer — puts the token back.
    /// Hidden when idle; reachable standing (poke) or seated (ray).
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

        const int MaxOptions = 3;
        const float Timeout = 20f;
        static readonly Vector2 Size = new(0.62f, 0.36f);

        GameObject _screenGo;
        HoloScreen _screen;
        TMPro.TMP_Text _dest;
        readonly PokeButton[] _options = new PokeButton[MaxOptions];
        PokeButton _cancel;
        TaskCompletionSource<object> _pending;
        float _deadline;

        public bool IsOpen => _pending != null;

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
            console._dest = DiegeticUi.HoloLabel(console._screen.Content, string.Empty,
                new Vector2(0f, px.y * 0.3f), new Vector2(px.x * 0.9f, 36f), 28f, UiKit.TextBright);

            var t = console._screen.transform;
            for (var i = 0; i < MaxOptions; i++)
            {
                var index = i;
                console._options[i] = PokeButton.Create(t, "Option" + i, string.Empty,
                    new Vector3(0f, 0.06f - i * 0.066f, -0.014f), Quaternion.identity, new Vector2(0.54f, 0.052f),
                    UiKit.Cyan, () => console.Choose(index));
            }

            console._cancel = PokeButton.Create(t, "Cancel", Trans.Get("cancel"),
                new Vector3(0.17f, -0.145f, -0.014f), Quaternion.identity, new Vector2(0.2f, 0.042f),
                UiKit.Danger, () => console.Resolve(null));

            console._screenGo.SetActive(false);
            console.enabled = false;
            return console;
        }

        readonly List<Option> _current = new();

        /// <summary>Ask the captain. Resolves with the chosen option's payload, or null if cancelled.</summary>
        public Task<object> Ask(string destination, IReadOnlyList<Option> options)
        {
            Resolve(null);
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
                b.SetLabel(_current[i].Label);
                b.SetAccent(_current[i].Accent);
                b.Interactive = _current[i].Enabled;
            }

            _pending = new TaskCompletionSource<object>();
            _deadline = Time.unscaledTime + Timeout;
            _screenGo.SetActive(true);
            enabled = true;
            CicCue.Hover(_screen.transform.position);
            return _pending.Task;
        }

        void Choose(int index)
        {
            if (_pending == null || index >= _current.Count || !_current[index].Enabled)
                return;
            Resolve(_current[index].Payload);
        }

        void Resolve(object payload)
        {
            var pending = _pending;
            _pending = null;
            if (_screenGo != null)
                _screenGo.SetActive(false);
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
