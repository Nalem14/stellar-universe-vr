using System.Collections.Generic;
using Core.App;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// Off the bridge (corridor, lab, dry dock, quarters, gate room, diplomacy chamber) the crew's lines are not
    /// lost: they come in on a small holo strip high in the captain's view, the station's name in its colour
    /// and the line under it, the way a comm badge would. The strip trails the head softly (no lock to the
    /// face), keeps the last two lines, and fades after a few seconds. On the bridge the subtitle over the
    /// table does this; on watch, the watch cluster. Nothing runs while it is empty.
    /// </summary>
    public sealed class CrewNoticeHud : MonoBehaviour
    {
        const int Slots = 2;
        const float Hold = 6.5f;
        static readonly Vector2 Px = new(760f, 190f);

        CanvasGroup _group;
        readonly Row[] _rows = new Row[Slots];
        readonly List<(string speaker, Color accent, string text, float until)> _lines = new();
        float _alpha;
        bool _placed;

        sealed class Row
        {
            public GameObject Root;
            public Image Bar;
            public TMP_Text Speaker;
            public TMP_Text Text;
        }

        public static CrewNoticeHud Build(Transform parent)
        {
            var go = new GameObject("CrewNoticeHud");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<CrewNoticeHud>();
            var canvas = DiegeticUi.WorldCanvas(go.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, 0.0011f);
            canvas.sortingOrder = 60;
            hud._group = canvas.gameObject.AddComponent<CanvasGroup>();
            hud._group.alpha = 0f;
            hud._group.interactable = false;
            hud._group.blocksRaycasts = false;
            for (var i = 0; i < Slots; i++)
                hud._rows[i] = BuildRow(canvas.transform, 45f - i * 92f);
            Core.Crew.BarkDirector.Spoke += hud.OnLine;
            return hud;
        }

        static Row BuildRow(Transform canvas, float y)
        {
            var frame = DiegeticUi.HoloFrame(canvas, new Vector2(Px.x, 84f));
            frame.anchoredPosition = new Vector2(0f, y);
            var img = frame.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0.5f, 0.92f, 1f, 0.82f);
            var bar = new GameObject("Accent", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bar.transform.SetParent(frame, false);
            bar.rectTransform.sizeDelta = new Vector2(8f, 62f);
            bar.rectTransform.anchoredPosition = new Vector2(-Px.x * 0.5f + 20f, 0f);
            bar.raycastTarget = false;
            var r = new Row
            {
                Root = frame.gameObject,
                Bar = bar,
                Speaker = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(10f, 20f), new Vector2(Px.x - 70f, 30f), 20f,
                    UiKit.Cyan, TextAlignmentOptions.MidlineLeft),
                Text = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(10f, -12f), new Vector2(Px.x - 70f, 40f), 25f,
                    UiKit.TextBright, TextAlignmentOptions.MidlineLeft)
            };
            r.Speaker.fontStyle = FontStyles.Bold;
            foreach (var t in new[] { r.Speaker, r.Text })
            {
                t.richText = false;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
            }

            frame.gameObject.SetActive(false);
            return r;
        }

        void OnDestroy() => Core.Crew.BarkDirector.Spoke -= OnLine;

        static bool OffBridge => Core.Stations.DiplomacyRoom.AnyRoomInside && !WatchMode.Inside;

        void OnLine(string speaker, Color accent, string text)
        {
            if (!OffBridge || string.IsNullOrEmpty(text))
                return;
            _lines.Add((speaker, accent, text, Time.unscaledTime + Hold));
            while (_lines.Count > Slots)
                _lines.RemoveAt(0);
            Layout();
            if (_alpha < 0.05f)
                _placed = false;
            CicCue.RadioOpen(transform.position);
        }

        void Layout()
        {
            // Newest on top.
            for (var i = 0; i < Slots; i++)
            {
                var row = _rows[i];
                var index = _lines.Count - 1 - i;
                if (index < 0)
                {
                    row.Root.SetActive(false);
                    continue;
                }

                var l = _lines[index];
                row.Root.SetActive(true);
                row.Bar.color = l.accent;
                row.Speaker.text = l.speaker;
                row.Speaker.color = Color.Lerp(l.accent, Color.white, 0.35f);
                row.Text.text = l.text;
            }
        }

        void LateUpdate()
        {
            var now = Time.unscaledTime;
            var expired = false;
            for (var i = _lines.Count - 1; i >= 0; i--)
                if (_lines[i].until <= now || !OffBridge)
                {
                    _lines.RemoveAt(i);
                    expired = true;
                }

            if (expired)
                Layout();
            _alpha = Mathf.MoveTowards(_alpha, _lines.Count > 0 ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            if (Mathf.Abs(_group.alpha - _alpha) > 0.001f)
                _group.alpha = _alpha;
            if (_alpha <= 0.001f)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;
            // High in the view, ahead of the eyes, trailing the head (a soft follow, never glued to it).
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
                fwd = Vector3.forward;
            fwd.Normalize();
            var target = cam.transform.position + fwd * 1.15f + Vector3.up * 0.42f;
            var k = _placed ? 1f - Mathf.Exp(-2.5f * Time.unscaledDeltaTime) : 1f;
            _placed = true;
            var p = Vector3.Lerp(transform.position, target, k);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - cam.transform.position, Vector3.up));
        }
    }
}
