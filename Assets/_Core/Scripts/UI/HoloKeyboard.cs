using System.Collections.Generic;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// Text entry on the headset. Every <see cref="DiegeticUi.HoloField"/> is attached here instead of letting
    /// TMP_InputField drive the Quest keyboard itself: TMP closes its soft keyboard on the first frame the
    /// overlay is not yet "Visible" (the Meta keyboard takes a moment to rise, and the app loses focus while it
    /// does), so on Quest 3 / 3S it never shows. Here the system keyboard is opened and given time to appear,
    /// its text mirrored into the field; if it does not come up (or is not supported), a holo keyboard opens in
    /// front of the player instead — so no field is ever a dead end. In the Editor the physical keyboard is used.
    /// </summary>
    public sealed class HoloKeyboard : MonoBehaviour
    {
        /// <summary>Editor only: route fields to the holo keyboard (to test it without a headset).</summary>
        public static bool EditorHolo;

        const float SystemGrace = 2.5f;
        const float Scale = 0.0008f;
        const float KeyPx = 60f;
        const float Stride = 66f;
        static readonly Vector2 Px = new(800f, 420f);

        static HoloKeyboard s_Instance;
        static readonly HashSet<TMP_InputField> Attached = new();

        TMP_InputField _field;
        TouchScreenKeyboard _system;
        float _openedAt;
        bool _seenVisible;
        string _original;

        GameObject _panel;
        TMP_Text _preview;
        readonly List<(TMP_Text label, int row, int col)> _letters = new();
        bool _shift;
        bool _symbols;

        static string[] Letters =>
            Trans.Lang == "fr"
                ? new[] { "azertyuiop", "qsdfghjklm", "wxcvbn.-_@" }
                : new[] { "qwertyuiop", "asdfghjkl@", "zxcvbnm.-_" };

        static readonly string[] Symbols = { "!?#$%&*()+", "=/:;,'\"<>|", "éèêàâçùô€~" };

        static bool Active => Application.platform == RuntimePlatform.Android || (Application.isEditor && EditorHolo);

        /// <summary>Route this field's text entry through the headset keyboard.</summary>
        public static void Attach(TMP_InputField field)
        {
            if (field == null)
                return;
            if (Application.platform == RuntimePlatform.Android)
                field.shouldHideSoftKeyboard = true;
            Attached.Add(field);
            Ensure();
        }

        static void Ensure()
        {
            if (s_Instance != null)
                return;
            var go = new GameObject("HoloKeyboard");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<HoloKeyboard>();
        }

        void Update()
        {
            if (!Active)
                return;

            // A newly selected field (poked, clicked, or activated from code) takes the keyboard.
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && (_field == null || selected != _field.gameObject))
            {
                var f = selected.GetComponent<TMP_InputField>();
                if (f != null && Attached.Contains(f) && f.interactable && !f.readOnly)
                    Begin(f);
            }

            if (_field == null)
            {
                if (_system != null || (_panel != null && _panel.activeSelf))
                    End(false);
                return;
            }

            if (_system != null)
                PumpSystem();
            else if (_panel != null && _panel.activeSelf)
                RefreshPreview();
        }

        void Begin(TMP_InputField f)
        {
            End(false);
            _field = f;
            _original = f.text;
            if (Application.platform == RuntimePlatform.Android && TouchScreenKeyboard.isSupported)
            {
                _system = TouchScreenKeyboard.Open(f.text, f.keyboardType, false, f.lineType != TMP_InputField.LineType.SingleLine,
                    f.contentType == TMP_InputField.ContentType.Password, false, string.Empty, f.characterLimit);
                _openedAt = Time.unscaledTime;
                _seenVisible = false;
                if (_system != null)
                    return;
                Debug.LogWarning("[HoloKeyboard] system keyboard failed to open; using the holo keyboard");
            }

            ShowHolo();
        }

        void PumpSystem()
        {
            var status = _system.status;
            if (status == TouchScreenKeyboard.Status.Visible)
            {
                _seenVisible = true;
                Mirror(_system.text);
                return;
            }

            if (!_seenVisible)
            {
                // Still rising (or never will): give it a moment, then fall back to the holo keyboard.
                if (status != TouchScreenKeyboard.Status.Canceled && Time.unscaledTime - _openedAt < SystemGrace)
                    return;
                Debug.LogWarning("[HoloKeyboard] system keyboard did not show (status " + status + "); using the holo keyboard");
                _system.active = false;
                _system = null;
                ShowHolo();
                return;
            }

            // Closed by the player: keep what they typed; Enter submits.
            Mirror(_system.text);
            End(status == TouchScreenKeyboard.Status.Done);
        }

        void Mirror(string text)
        {
            if (_field == null || text == null || _field.text == text)
                return;
            _field.text = _field.characterLimit > 0 && text.Length > _field.characterLimit ? text.Substring(0, _field.characterLimit) : text;
        }

        void End(bool submit)
        {
            var f = _field;
            _field = null;
            if (_system != null)
            {
                if (_system.status == TouchScreenKeyboard.Status.Visible)
                    _system.active = false;
                _system = null;
            }

            if (_panel != null && _panel.activeSelf)
                _panel.SetActive(false);
            if (f == null)
                return;
            // Release the selection, or the poll above would reopen the keyboard at once.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == f.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            f.DeactivateInputField();
            if (submit)
                f.onSubmit?.Invoke(f.text);
        }

        // --- Holo keyboard ---

        void ShowHolo()
        {
            if (_panel == null)
                BuildPanel();
            var cam = Camera.main;
            if (cam != null)
            {
                // At the hands, a little below the eyes, tilted up to them like a desk console.
                var fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f)
                    fwd = Vector3.forward;
                fwd.Normalize();
                var at = cam.transform.position + fwd * 0.36f + Vector3.down * 0.34f;
                _panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
            }

            var layer = _field.gameObject.layer;
            foreach (var t in _panel.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
            _shift = false;
            _symbols = false;
            Relabel();
            _panel.SetActive(true);
            RefreshPreview();
            CicCue.Ok(_panel.transform.position);
        }

        void BuildPanel()
        {
            _panel = new GameObject("HoloKeyboardPanel");
            _panel.transform.SetParent(transform, false);
            var canvas = DiegeticUi.WorldCanvas(_panel.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, Scale);
            canvas.sortingOrder = 40;
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            frame.GetComponent<Image>().raycastTarget = false;

            var top = Px.y * 0.5f - 44f;
            var well = DiegeticUi.HoloSelectTray(frame, new Vector2(0f, top), new Vector2(Px.x - 60f, 50f));
            _preview = DiegeticUi.HoloLabel(well, string.Empty, Vector2.zero, new Vector2(Px.x - 100f, 44f), 26f, Color.white,
                TextAlignmentOptions.MidlineLeft);
            _preview.textWrappingMode = TextWrappingModes.NoWrap;
            _preview.overflowMode = TextOverflowModes.Ellipsis;
            _preview.richText = false;

            float Row(int r) => top - 70f - r * Stride;
            float Col(int c, int count) => (c - (count - 1) * 0.5f) * Stride;

            // Digits and backspace.
            for (var c = 0; c < 10; c++)
            {
                var ch = (char)('0' + (c + 1) % 10);
                Key(frame, ch.ToString(), new Vector2(Col(c, 11), Row(0)), KeyPx, () => Type(ch));
            }

            Key(frame, "←", new Vector2(Col(10, 11), Row(0)), KeyPx, Backspace, DiegeticUi.BtnStyle.Amber);

            // Three rows of letters (or symbols), relabelled in place.
            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 10; c++)
            {
                var row = r;
                var col = c;
                var b = Key(frame, string.Empty, new Vector2(Col(c, 10), Row(r + 1)), KeyPx, () => TypeAt(row, col));
                _letters.Add((b.GetComponentInChildren<TMP_Text>(), r, c));
            }

            // Shift, symbols, space, OK, close.
            var y = Row(4);
            Key(frame, "↑", new Vector2(-340f, y), 72f, () =>
            {
                _shift = !_shift;
                Relabel();
            }, DiegeticUi.BtnStyle.Ghost);
            Key(frame, "#+=", new Vector2(-250f, y), 90f, () =>
            {
                _symbols = !_symbols;
                Relabel();
            }, DiegeticUi.BtnStyle.Ghost);
            Key(frame, string.Empty, new Vector2(-40f, y), 320f, () => Type(' '));
            Key(frame, Trans.Get("validate"), new Vector2(185f, y), 120f, () => End(true), DiegeticUi.BtnStyle.Amber);
            Key(frame, Trans.Get("close"), new Vector2(315f, y), 120f, () => End(false), DiegeticUi.BtnStyle.Ghost);
            _panel.SetActive(false);
        }

        Button Key(Transform parent, string label, Vector2 pos, float width, UnityEngine.Events.UnityAction press,
            DiegeticUi.BtnStyle style = DiegeticUi.BtnStyle.Cyan)
        {
            var b = DiegeticUi.HoloButton(parent, label, pos, new Vector2(width, KeyPx), press, style);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var t = b.GetComponentInChildren<TMP_Text>();
            t.fontSize = 26f;
            t.fontStyle = FontStyles.Normal;
            return b;
        }

        void Relabel()
        {
            var set = _symbols ? Symbols : Letters;
            foreach (var (label, row, col) in _letters)
            {
                var ch = set[row][col];
                label.text = (_shift && !_symbols ? char.ToUpperInvariant(ch) : ch).ToString();
            }
        }

        void TypeAt(int row, int col)
        {
            var ch = (_symbols ? Symbols : Letters)[row][col];
            if (_shift && !_symbols)
            {
                ch = char.ToUpperInvariant(ch);
                _shift = false;
                Relabel();
            }

            Type(ch);
        }

        void Type(char ch)
        {
            if (_field == null)
                return;
            if (_field.characterLimit > 0 && _field.text.Length >= _field.characterLimit)
            {
                CicCue.Fail(_panel.transform.position);
                return;
            }

            _field.text += ch;
            RefreshPreview();
        }

        void Backspace()
        {
            if (_field == null || _field.text.Length == 0)
                return;
            _field.text = _field.text.Substring(0, _field.text.Length - 1);
            RefreshPreview();
        }

        void RefreshPreview()
        {
            if (_preview == null || _field == null)
                return;
            var text = _field.text ?? string.Empty;
            var shown = _field.contentType == TMP_InputField.ContentType.Password ? new string('•', text.Length) : text;
            shown += "|";
            if (_preview.text != shown)
                _preview.text = shown;
        }
    }
}
