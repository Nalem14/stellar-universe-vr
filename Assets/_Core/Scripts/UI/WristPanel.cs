using Core.App;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// A holo readout on the left wrist, the captain's watch: bring either side of the wrist to your face and it
    /// fades in, always upright toward the eyes. Ship time, the alert state, Nova, unread transmissions, and
    /// the three nearest affairs with a timer (a trip, a survey, a siege, a harvest, a build, a research, a
    /// battle) — each with what it is, where, a countdown and a progress bar. Follows the left controller or
    /// the tracked left hand; refreshed once a second while shown, nothing otherwise. On the Watch layer, so it
    /// stays with the captain on watch too.
    /// </summary>
    public sealed class WristPanel : MonoBehaviour
    {
        const float ShowDot = 0.55f;
        const int Rows = 3;
        static readonly Vector2 Px = new(300f, 262f);

        Transform _controller;
        Transform _hand;
        CanvasGroup _group;
        TMP_Text _time;
        TMP_Text _alert;
        Image _alertDot;
        TMP_Text _nova;
        TMP_Text _comms;
        TMP_Text _idle;
        readonly Row[] _rows = new Row[Rows];
        float _alpha;
        float _nextRefresh;

        sealed class Row
        {
            public GameObject Root;
            public TMP_Text Title;
            public TMP_Text Detail;
            public TMP_Text Left;
            public RectTransform Fill;
        }

        public static WristPanel Build(Transform left, Transform leftHand, int layer)
        {
            var go = new GameObject("WristPanel");
            var w = go.AddComponent<WristPanel>();
            w._controller = left;
            w._hand = leftHand;
            var canvas = DiegeticUi.WorldCanvas(go.transform, "WristCanvas", Px, Vector3.zero, Quaternion.identity, 0.0005f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            var img = frame.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0.45f, 0.85f, 1f, 0.72f);
            w._group = canvas.gameObject.AddComponent<CanvasGroup>();
            w._group.alpha = 0f;
            w._group.interactable = false;
            w._group.blocksRaycasts = false;

            // Header: clock, alert, Nova and transmissions.
            const float top = 100f;
            w._time = Label(frame, new Vector2(-78f, top), new Vector2(120f, 40f), 32f, UiKit.TextBright, TextAlignmentOptions.MidlineLeft);
            w._time.fontStyle = FontStyles.Bold;
            w._alertDot = new GameObject("AlertDot", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            w._alertDot.transform.SetParent(frame, false);
            w._alertDot.rectTransform.sizeDelta = new Vector2(13f, 13f);
            w._alertDot.rectTransform.anchoredPosition = new Vector2(-4f, top);
            w._alertDot.sprite = DiegeticUi.SprBtn;
            w._alertDot.raycastTarget = false;
            w._alert = Label(frame, new Vector2(70f, top), new Vector2(130f, 30f), 14f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft);
            w._nova = Label(frame, new Vector2(-62f, top - 30f), new Vector2(150f, 26f), 17f, UiKit.Amber, TextAlignmentOptions.MidlineLeft);
            w._comms = Label(frame, new Vector2(78f, top - 30f), new Vector2(120f, 26f), 15f, UiKit.Cyan, TextAlignmentOptions.MidlineRight);

            // The affairs with a timer.
            for (var i = 0; i < Rows; i++)
                w._rows[i] = BuildRow(frame, 38f - i * 52f);
            w._idle = Label(frame, new Vector2(0f, -10f), new Vector2(270f, 40f), 16f, UiKit.TextDim, TextAlignmentOptions.Center);
            SetLayer(go.transform, layer);
            return w;
        }

        static Row BuildRow(RectTransform frame, float y)
        {
            var root = new GameObject("Affair", typeof(RectTransform));
            root.transform.SetParent(frame, false);
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(276f, 48f);
            rt.anchoredPosition = new Vector2(0f, y);
            var r = new Row
            {
                Root = root,
                Title = Label(rt, new Vector2(-40f, 12f), new Vector2(196f, 22f), 16f, UiKit.TextBright, TextAlignmentOptions.MidlineLeft),
                Detail = Label(rt, new Vector2(-40f, -8f), new Vector2(196f, 18f), 12.5f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft),
                Left = Label(rt, new Vector2(98f, 4f), new Vector2(80f, 26f), 16f, UiKit.Amber, TextAlignmentOptions.MidlineRight)
            };
            r.Title.fontStyle = FontStyles.Bold;

            // Progress: a thin rail with a lit fill.
            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            rail.transform.SetParent(rt, false);
            rail.rectTransform.sizeDelta = new Vector2(270f, 4f);
            rail.rectTransform.anchoredPosition = new Vector2(0f, -21f);
            rail.color = new Color(0.3f, 0.6f, 0.75f, 0.35f);
            rail.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(rail.transform, false);
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = new Vector2(0f, 0f);
            fill.color = UiKit.Cyan;
            fill.raycastTarget = false;
            r.Fill = fill.rectTransform;
            return r;
        }

        static TMP_Text Label(Transform parent, Vector2 pos, Vector2 size, float font, Color c, TextAlignmentOptions align)
        {
            var t = DiegeticUi.HoloLabel(parent, string.Empty, pos, size, font, c, align);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = false;
            return t;
        }

        static void SetLayer(Transform t, int layer)
        {
            if (layer < 0)
                return;
            t.gameObject.layer = layer;
            for (var i = 0; i < t.childCount; i++)
                SetLayer(t.GetChild(i), layer);
        }

        void LateUpdate()
        {
            var anchor = _hand != null && _hand.gameObject.activeInHierarchy ? _hand : _controller;
            var cam = Camera.main;
            if (anchor == null || cam == null || !anchor.gameObject.activeInHierarchy)
            {
                Fade(0f);
                return;
            }

            // On the wrist, just behind the grip, on whichever side (back or inside) is turned to the eyes;
            // the face always upright toward them, so it reads like a watch.
            var eye = cam.transform.position;
            var wrist = anchor.TransformPoint(new Vector3(0f, -0.015f, -0.105f));
            var side = anchor.right;
            var toEye = eye - wrist;
            var dist = toEye.magnitude;
            var facing = dist > 1e-4f ? Vector3.Dot(side, toEye / dist) : 0f;
            var outward = facing >= 0f ? side : -side;
            var at = wrist + outward * 0.05f;
            var look = at - eye;
            if (look.sqrMagnitude > 1e-6f)
                transform.SetPositionAndRotation(at, Quaternion.LookRotation(look, cam.transform.up));
            var shown = dist < 0.75f && Mathf.Abs(facing) > ShowDot;
            Fade(shown ? 1f : 0f);
            if (_alpha > 0.01f && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 1f;
                Refresh();
            }
        }

        void Fade(float target)
        {
            _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 6f);
            if (_group != null && Mathf.Abs(_group.alpha - _alpha) > 0.001f)
                _group.alpha = _alpha;
        }

        static string KindLabel(WatchKind kind) => Trans.Get(kind switch
        {
            WatchKind.Battle => "vr.watch.battle",
            WatchKind.Transit => "vr.watch.transit",
            WatchKind.Siege => "vr.watch.siege",
            WatchKind.Harvest => "vr.watch.harvest",
            WatchKind.Explore => "vr.watch.explore",
            WatchKind.Building => "vr.watch.building",
            _ => "vr.watch.research"
        });

        void Refresh()
        {
            _time.text = System.DateTime.Now.ToString("HH:mm");
            var level = AlertState.Level;
            _alertDot.color = level == AlertLevel.Red ? new Color(1f, 0.3f, 0.26f) : level == AlertLevel.Amber ? UiKit.Amber : UiKit.Ok;
            _alert.text = level == AlertLevel.Red ? Trans.Get("vr.screen.redAlert") : level == AlertLevel.Amber ? Trans.Get("vr.console.amberAlert") : string.Empty;
            var eco = EconomyService.Instance;
            _nova.text = eco != null ? Trans.Get("nova") + "  " + eco.Nova.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) : string.Empty;
            var comms = CommsService.Instance?.Total ?? 0;
            _comms.text = comms > 0 ? Trans.Format("vr.screen.comms", comms) : string.Empty;

            var watch = WatchMode.Instance;
            var now = FleetOrderGate.UnixNow();
            var count = watch != null ? Mathf.Min(Rows, watch.Affairs.Count) : 0;
            for (var i = 0; i < Rows; i++)
            {
                var row = _rows[i];
                if (i >= count)
                {
                    if (row.Root.activeSelf)
                        row.Root.SetActive(false);
                    continue;
                }

                var a = watch.Affairs[i];
                if (!row.Root.activeSelf)
                    row.Root.SetActive(true);
                // What it is (and whose), then where / what exactly, and how long is left.
                var kind = KindLabel(a.Kind);
                row.Title.text = a.Kind == WatchKind.Building || a.Kind == WatchKind.Research ? a.Title : kind + " · " + a.Title;
                row.Detail.text = a.Kind == WatchKind.Building ? kind + " · " + a.Detail : a.Detail;
                row.Left.text = a.End > now ? Core.Holo.TravelPlanner.TimeText(a.End - now) : string.Empty;
                var p = a.Start > 0 ? a.Progress(now) : 0f;
                row.Fill.sizeDelta = new Vector2(270f * p, 0f);
                row.Fill.gameObject.SetActive(a.Start > 0);
            }

            _idle.gameObject.SetActive(count == 0);
            if (count == 0)
                _idle.text = Trans.Get("vr.watch.idle");
        }
    }
}
