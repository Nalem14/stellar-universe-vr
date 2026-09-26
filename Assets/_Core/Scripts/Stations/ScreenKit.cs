using System;
using System.Globalization;
using System.Threading.Tasks;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// Small helpers shared by the room consoles (1060 × 420 px bodies rebuilt on each render): single-line
    /// labels, gauges, auto-sized buttons, tab rows, verbatim player text, French number formatting, and a
    /// two-press guard for irreversible orders.
    /// </summary>
    public static class ScreenKit
    {
        static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static RectTransform Body(RectTransform frame, float y = 0f)
        {
            var go = new GameObject("Body", typeof(RectTransform));
            go.transform.SetParent(frame, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1060f, 420f);
            rt.anchoredPosition = new Vector2(0f, y);
            return rt;
        }

        public static void Clear(RectTransform body)
        {
            for (var i = body.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(body.GetChild(i).gameObject);
        }

        /// <summary>One line of rich text, centred at (x, y), ellipsis past <paramref name="width"/>.</summary>
        public static TMP_Text Line(RectTransform parent, string text, float x, float y, float size, Color color, float width,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = DiegeticUi.HoloLabel(parent, text, new Vector2(x, y), new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>Wrapped paragraph in a box of <paramref name="width"/> × <paramref name="height"/>.</summary>
        public static TMP_Text Para(RectTransform parent, string text, float x, float y, float size, Color color, float width,
            float height, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = DiegeticUi.HoloLabel(parent, text, new Vector2(x, y), new Vector2(width, height), size, color, align);
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        public static Button Small(Button b, float min = 11f, float max = 18f)
        {
            var l = b.GetComponentInChildren<TMP_Text>();
            l.enableAutoSizing = true;
            l.fontSizeMin = min;
            l.fontSizeMax = max;
            return b;
        }

        public static Button Btn(RectTransform parent, string label, float x, float y, float w, float h, Action onClick,
            DiegeticUi.BtnStyle style = DiegeticUi.BtnStyle.Ghost, bool interactable = true)
        {
            var b = Small(DiegeticUi.HoloButton(parent, label, new Vector2(x, y), new Vector2(w, h), () => onClick(), style),
                10f, Mathf.Min(19f, h * 0.42f));
            b.interactable = interactable;
            return b;
        }

        /// <summary>A horizontal gauge (0..1) with a caption centred on it.</summary>
        public static void Gauge(RectTransform parent, float x, float y, float width, float value, Color fill, string caption,
            float height = 22f)
        {
            var bg = new GameObject("Gauge", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(parent, false);
            var rt = bg.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
            var img = bg.GetComponent<Image>();
            img.color = new Color(0.1f, 0.18f, 0.22f, 0.85f);
            img.raycastTarget = false;
            var fg = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fg.transform.SetParent(bg.transform, false);
            var ft = fg.GetComponent<RectTransform>();
            ft.anchorMin = new Vector2(0f, 0f);
            ft.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            ft.offsetMin = new Vector2(2f, 2f);
            ft.offsetMax = new Vector2(-2f, -2f);
            var fi = fg.GetComponent<Image>();
            fi.color = fill;
            fi.raycastTarget = false;
            if (!string.IsNullOrEmpty(caption))
                Line(parent, caption, x, y, Mathf.Min(15f, height * 0.7f), UiKit.TextBright, width, TextAlignmentOptions.Center);
        }

        /// <summary>Row of tab buttons along the top of a screen; the active one lit.</summary>
        public static Button[] Tabs(RectTransform frame, string[] keys, float y, Action<int> onPick)
        {
            var buttons = new Button[keys.Length];
            var w = 1060f / keys.Length;
            for (var i = 0; i < keys.Length; i++)
            {
                var index = i;
                buttons[i] = Small(DiegeticUi.HoloButton(frame, Trans.Get(keys[i]), new Vector2(-530f + w * (i + 0.5f), y),
                    new Vector2(w - 10f, 46f), () => onPick(index), DiegeticUi.BtnStyle.Ghost), 11f, 18f);
            }

            return buttons;
        }

        public static void LightTabs(Button[] tabs, int active)
        {
            for (var i = 0; i < tabs.Length; i++)
                tabs[i].GetComponent<Image>().color = i == active ? new Color(0.6f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.45f);
        }

        public static string Verbatim(string s) =>
            "<noparse>" + (s ?? string.Empty).Replace("</noparse>", "</ noparse>").Replace('\n', ' ') + "</noparse>";

        public static string Num(float v, int decimals = 0) => v.ToString(decimals == 0 ? "N0" : "N" + decimals, Fr);

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static JArray Array(ApiResult r)
        {
            if (!r.Ok || string.IsNullOrEmpty(r.Body))
                return null;
            try
            {
                return JToken.Parse(r.Body) as JArray;
            }
            catch
            {
                return null;
            }
        }

        public static JObject Object(ApiResult r)
        {
            if (!r.Ok || string.IsNullOrEmpty(r.Body))
                return null;
            try
            {
                return JToken.Parse(r.Body) as JObject;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Server text in the player's language when it carries both (<c>name</c> / <c>name_en</c>).</summary>
        public static string Localized(JToken o, string field)
        {
            if (o == null)
                return string.Empty;
            var en = Core.App.FocusContext.AsString(o[field + "_en"]);
            var fr = Core.App.FocusContext.AsString(o[field]);
            return Trans.Lang == "en" && en.Length > 0 ? en : fr;
        }

        /// <summary>Time left as the rest of the bridge shows it (<see cref="Core.Holo.TravelPlanner.TimeText"/>).</summary>
        public static string Remaining(long seconds) => Core.Holo.TravelPlanner.TimeText(Math.Max(0, seconds));
    }

    /// <summary>
    /// Two presses for an irreversible order: the first arms the button (its label asks to confirm) for a few
    /// seconds; the second runs it. One per screen; <see cref="Tick"/> from Update drops a stale arming.
    /// </summary>
    public sealed class TwoPress
    {
        const float Window = 4f;
        string _armed;
        float _until;
        readonly Action _rerender;

        public TwoPress(Action rerender) => _rerender = rerender;

        public bool IsArmed(string key) => _armed == key && Time.unscaledTime < _until;

        public void Reset() => _armed = null;

        public Button Make(RectTransform parent, string key, string label, float x, float y, float w, float h,
            Func<Task> action, DiegeticUi.BtnStyle style = DiegeticUi.BtnStyle.Danger)
        {
            var armed = IsArmed(key);
            return ScreenKit.Btn(parent, armed ? Trans.Get("vr.diplo.confirm") : label, x, y, w, h, () =>
            {
                if (IsArmed(key))
                {
                    _armed = null;
                    AsyncTap.Run(action());
                    return;
                }

                _armed = key;
                _until = Time.unscaledTime + Window;
                _rerender();
            }, armed ? DiegeticUi.BtnStyle.Amber : style);
        }

        public void Tick()
        {
            if (_armed != null && Time.unscaledTime >= _until)
            {
                _armed = null;
                _rerender();
            }
        }
    }
}
