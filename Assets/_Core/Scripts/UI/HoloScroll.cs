using Core.App;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// Scrolling for a list on a World Space holo screen (teleporter, crew repeaters): the list's rows stay where
    /// their screen lays them out, the list is clipped to its area (a row out of view neither shows nor takes a
    /// click), and a slim column on its right edge moves it — ▲ / ▼ (ray + trigger, click, tap) with a thumb
    /// showing where the view sits. On a PC the mouse wheel over the list scrolls it too. The column only shows
    /// when the rows overflow. The offset survives a rebuild of the rows (clamped to the new length).
    /// Wrap the list once, right after creating it: <see cref="Wrap"/>.
    /// </summary>
    public sealed class HoloScroll : MonoBehaviour
    {
        const float ColumnW = 40f;
        const float Pad = 10f;

        RectTransform _viewport;
        RectTransform _content;
        RectTransform _column;
        RectTransform _thumb;
        RectTransform _track;
        Button _up;
        Button _down;
        float _offset;
        float _max;
        int _lastChildren = -1;
        float _nextMeasure;
        float _step;
        static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>The list's scroll offset (0 = top).</summary>
        public float Offset => _offset;

        /// <summary>
        /// Put <paramref name="list"/> in a clipped viewport of the same rect, with the scroll column at
        /// <paramref name="columnX"/> (list-local x of the column's centre). The list keeps its own coordinates.
        /// </summary>
        public static HoloScroll Wrap(RectTransform list, float columnX, Color accent)
        {
            var parent = list.parent;
            var go = new GameObject(list.name + "Viewport", typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(list.GetSiblingIndex());
            var vp = go.GetComponent<RectTransform>();
            vp.anchorMin = list.anchorMin;
            vp.anchorMax = list.anchorMax;
            vp.pivot = list.pivot;
            vp.sizeDelta = list.sizeDelta;
            vp.anchoredPosition = list.anchoredPosition;
            list.SetParent(vp, false);
            list.anchoredPosition = Vector2.zero;

            var s = go.AddComponent<HoloScroll>();
            s._viewport = vp;
            s._content = list;
            s._step = Mathf.Max(60f, list.sizeDelta.y * 0.6f);
            s.BuildColumn(parent, columnX, accent);
            s.Apply();
            return s;
        }

        void BuildColumn(Transform parent, float columnX, Color accent)
        {
            // Beside the viewport (not inside it): never clipped, never scrolled.
            var go = new GameObject("ScrollColumn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _column = go.GetComponent<RectTransform>();
            _column.sizeDelta = new Vector2(ColumnW, _viewport.sizeDelta.y);
            _column.anchoredPosition = _viewport.anchoredPosition + new Vector2(columnX, 0f);
            var h = _viewport.sizeDelta.y;
            const float btnH = 44f;
            _up = DiegeticUi.HoloButton(_column, "▲", new Vector2(0f, h * 0.5f - btnH * 0.5f), new Vector2(ColumnW, btnH),
                () => Scroll(-_step), DiegeticUi.BtnStyle.Ghost);
            _down = DiegeticUi.HoloButton(_column, "▼", new Vector2(0f, -h * 0.5f + btnH * 0.5f), new Vector2(ColumnW, btnH),
                () => Scroll(_step), DiegeticUi.BtnStyle.Ghost);
            foreach (var b in new[] { _up, _down })
            {
                var t = b.GetComponentInChildren<TMP_Text>();
                if (t != null)
                {
                    t.enableAutoSizing = false;
                    t.fontSize = 18f;
                }
            }

            // Track and thumb between the arrows.
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(_column, false);
            _track = track.GetComponent<RectTransform>();
            _track.sizeDelta = new Vector2(6f, h - 2f * (btnH + 8f));
            var ti = track.GetComponent<Image>();
            ti.color = new Color(accent.r, accent.g, accent.b, 0.18f);
            ti.raycastTarget = false;
            var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            thumb.transform.SetParent(_track, false);
            _thumb = thumb.GetComponent<RectTransform>();
            var th = thumb.GetComponent<Image>();
            th.color = new Color(accent.r, accent.g, accent.b, 0.85f);
            th.raycastTarget = false;
        }

        /// <summary>Re-measure the rows now (after a rebuild); the offset is kept, clamped to the new length.</summary>
        public void Refresh()
        {
            Measure();
            Apply();
        }

        /// <summary>Back to the top (a new list, another tab).</summary>
        public void ToTop()
        {
            _offset = 0f;
            Apply();
        }

        void Scroll(float delta)
        {
            Measure();
            var before = _offset;
            _offset = Mathf.Clamp(_offset + delta, 0f, _max);
            if (!Mathf.Approximately(before, _offset))
                CicCue.Pip(transform.position);
            Apply();
        }

        /// <summary>Lowest row bottom below the viewport's bottom edge, in list units.</summary>
        void Measure()
        {
            _lastChildren = _content.childCount;
            var bottom = 0f;
            var any = false;
            for (var i = 0; i < _content.childCount; i++)
            {
                var child = _content.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf)
                    continue;
                child.GetWorldCorners(Corners);
                for (var k = 0; k < 4; k++)
                {
                    var y = _content.InverseTransformPoint(Corners[k]).y;
                    if (!any || y < bottom)
                        bottom = y;
                    any = true;
                }
            }

            var floor = -_viewport.sizeDelta.y * 0.5f;
            _max = any ? Mathf.Max(0f, floor - bottom + Pad) : 0f;
            _offset = Mathf.Clamp(_offset, 0f, _max);
        }

        void Apply()
        {
            _content.anchoredPosition = new Vector2(0f, _offset);
            var scrolls = _max > 0.5f;
            if (_column != null && _column.gameObject.activeSelf != scrolls)
                _column.gameObject.SetActive(scrolls);
            if (!scrolls)
                return;
            _up.interactable = _offset > 0.5f;
            _down.interactable = _offset < _max - 0.5f;
            var view = _viewport.sizeDelta.y;
            var trackH = _track.sizeDelta.y;
            var thumbH = Mathf.Max(18f, trackH * view / (view + _max));
            _thumb.sizeDelta = new Vector2(6f, thumbH);
            var t = _max > 0f ? _offset / _max : 0f;
            _thumb.anchoredPosition = new Vector2(0f, (trackH - thumbH) * (0.5f - t));
        }

        void LateUpdate()
        {
            // Rows are rebuilt by their screen (sometimes over several frames): follow the count, and look again
            // now and then for rows that changed size in place (an opened tray).
            if (_content.childCount != _lastChildren || Time.unscaledTime >= _nextMeasure)
            {
                _nextMeasure = Time.unscaledTime + 0.5f;
                Measure();
                Apply();
            }

            if (_max <= 0.5f || !PcPlatformBoot.IsDesktop || PcPlatformBoot.IsTyping)
                return;
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null)
                return;
            var wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) < 0.01f)
                return;
            if (RectTransformUtility.RectangleContainsScreenPoint(_viewport, FlatPointer.ScreenPosition, cam))
                Scroll(-Mathf.Sign(wheel) * 70f);
        }
    }
}
