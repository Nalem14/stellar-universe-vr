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
    /// A holo numeric pad for galaxy coordinates ("12, -12"): two readouts (X, Y — tap one to type into it),
    /// digits, sign, backspace, Validate / Cancel. Opens in front of the captain; <see cref="Ask"/> returns the
    /// typed pair, or null when cancelled. Systems are known by their coordinates only (web galaxy), so this
    /// is how a captain names a far star without hunting for it on the table.
    /// </summary>
    public sealed class CoordPad : MonoBehaviour
    {
        static CoordPad s_Instance;
        static readonly Vector2 Px = new(520f, 560f);
        const int MaxDigits = 4;

        GameObject _panel;
        readonly string[] _values = { string.Empty, string.Empty };
        readonly bool[] _negative = new bool[2];
        readonly TMP_Text[] _read = new TMP_Text[2];
        readonly Image[] _box = new Image[2];
        TMP_Text _header;
        int _axis;
        TaskCompletionSource<Vector2Int?> _pending;

        public static Task<Vector2Int?> Ask(string header)
        {
            if (s_Instance == null)
            {
                var go = new GameObject("CoordPad");
                s_Instance = go.AddComponent<CoordPad>();
                s_Instance.Build();
            }

            return s_Instance.Open(header);
        }

        Task<Vector2Int?> Open(string header)
        {
            _pending?.TrySetResult(null);
            _pending = new TaskCompletionSource<Vector2Int?>();
            _values[0] = _values[1] = string.Empty;
            _negative[0] = _negative[1] = false;
            _axis = 0;
            _header.text = string.IsNullOrEmpty(header) ? Trans.Get("vr.coords.title") : header;
            var cam = Camera.main;
            if (cam != null)
            {
                var fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f)
                    fwd = Vector3.forward;
                fwd.Normalize();
                var at = cam.transform.position + fwd * 0.5f + Vector3.down * 0.16f;
                _panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
                foreach (var t in _panel.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = cam.gameObject.layer;
            }

            Refresh();
            _panel.SetActive(true);
            CicCue.Ok(_panel.transform.position);
            return _pending.Task;
        }

        void Build()
        {
            _panel = new GameObject("CoordPadPanel");
            _panel.transform.SetParent(transform, false);
            var canvas = DiegeticUi.WorldCanvas(_panel.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, 0.00075f);
            canvas.sortingOrder = 45;
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            frame.GetComponent<Image>().raycastTarget = false;
            _header = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(0f, 240f), new Vector2(480f, 40f), 24f, UiKit.Cyan);
            _header.fontStyle = FontStyles.Bold;
            _header.textWrappingMode = TextWrappingModes.NoWrap;
            _header.overflowMode = TextOverflowModes.Ellipsis;

            // The two readouts: "X" and "Y", the active one lit; tap to switch.
            for (var a = 0; a < 2; a++)
            {
                var axis = a;
                var b = DiegeticUi.HoloButton(frame, string.Empty, new Vector2(a == 0 ? -120f : 120f, 172f), new Vector2(220f, 70f),
                    () =>
                    {
                        _axis = axis;
                        Refresh();
                    }, DiegeticUi.BtnStyle.Ghost);
                _box[a] = b.GetComponent<Image>();
                _read[a] = b.GetComponentInChildren<TMP_Text>();
                _read[a].fontSize = 34f;
                _read[a].richText = true;
            }

            // Digits 1–9, then sign, 0, backspace.
            for (var i = 0; i < 12; i++)
            {
                var col = i % 3;
                var row = i / 3;
                var pos = new Vector2(-150f + col * 150f, 92f - row * 76f);
                string label;
                System.Action act;
                if (i < 9)
                {
                    var d = (char)('1' + i);
                    label = d.ToString();
                    act = () => Digit(d);
                }
                else if (i == 9)
                {
                    label = "±";
                    act = Sign;
                }
                else if (i == 10)
                {
                    label = "0";
                    act = () => Digit('0');
                }
                else
                {
                    label = "←";
                    act = Back;
                }

                var key = DiegeticUi.HoloButton(frame, label, pos, new Vector2(136f, 66f), () => act(),
                    i >= 9 && i != 10 ? DiegeticUi.BtnStyle.Ghost : DiegeticUi.BtnStyle.Cyan);
                key.GetComponentInChildren<TMP_Text>().fontSize = 32f;
                key.navigation = new Navigation { mode = Navigation.Mode.None };
            }

            DiegeticUi.HoloButton(frame, Trans.Get("cancel"), new Vector2(-122f, -236f), new Vector2(220f, 60f),
                () => Close(null), DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(frame, Trans.Get("validate"), new Vector2(122f, -236f), new Vector2(220f, 60f),
                Validate, DiegeticUi.BtnStyle.Amber);
            _panel.SetActive(false);
        }

        void Digit(char d)
        {
            if (_values[_axis].Length >= MaxDigits)
                return;
            _values[_axis] += d;
            Refresh();
        }

        void Sign()
        {
            _negative[_axis] = !_negative[_axis];
            Refresh();
        }

        void Back()
        {
            var v = _values[_axis];
            if (v.Length > 0)
                _values[_axis] = v.Substring(0, v.Length - 1);
            else
                _negative[_axis] = false;
            Refresh();
        }

        void Validate()
        {
            if (_values[0].Length == 0 && _axis == 0)
            {
                CicCue.Fail(_panel.transform.position);
                return;
            }

            // Typing X then Validate moves on to Y first.
            if (_axis == 0 && _values[1].Length == 0)
            {
                _axis = 1;
                Refresh();
                return;
            }

            if (_values[0].Length == 0 || _values[1].Length == 0)
            {
                CicCue.Fail(_panel.transform.position);
                return;
            }

            Close(new Vector2Int(Value(0), Value(1)));
        }

        int Value(int axis) => (int.TryParse(_values[axis], out var v) ? v : 0) * (_negative[axis] ? -1 : 1);

        void Close(Vector2Int? result)
        {
            _panel.SetActive(false);
            var p = _pending;
            _pending = null;
            p?.TrySetResult(result);
        }

        void Refresh()
        {
            for (var a = 0; a < 2; a++)
            {
                var text = _values[a].Length > 0 ? (_negative[a] ? "-" : string.Empty) + _values[a] : (_negative[a] ? "-" : "_");
                _read[a].text = "<size=60%><color=#7fd8ff>" + (a == 0 ? "X" : "Y") + "</color></size>  " + text;
                _box[a].color = a == _axis ? new Color(1f, 0.85f, 0.5f, 1f) : Color.white;
            }
        }

        void OnDisable()
        {
            _pending?.TrySetResult(null);
            _pending = null;
        }
    }
}
