using Core.App;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// A small holo readout on the inside of the left wrist, the captain's "pulse": turn the wrist toward your
    /// face and it fades in — ship time, the alert state, Nova, the next affair to watch (with its countdown)
    /// and unread transmissions. Follows the left controller, or the tracked left hand; refreshed once a
    /// second while shown, nothing otherwise. On the Watch layer, so it stays with the captain on watch too.
    /// </summary>
    public sealed class WristPanel : MonoBehaviour
    {
        const float ShowDot = 0.62f;
        static readonly Vector2 Px = new(260f, 170f);

        Transform _controller;
        Transform _hand;
        CanvasGroup _group;
        TMP_Text _time;
        TMP_Text _alert;
        Image _alertDot;
        TMP_Text _nova;
        TMP_Text _affair;
        TMP_Text _comms;
        float _alpha;
        float _nextRefresh;

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
            img.color = new Color(0.45f, 0.85f, 1f, 0.7f);
            w._group = canvas.gameObject.AddComponent<CanvasGroup>();
            w._group.alpha = 0f;
            w._group.interactable = false;
            w._group.blocksRaycasts = false;

            w._time = Label(frame, new Vector2(-40f, 56f), new Vector2(160f, 40f), 34f, UiKit.TextBright, TextAlignmentOptions.MidlineLeft);
            w._time.fontStyle = FontStyles.Bold;
            w._alertDot = new GameObject("AlertDot", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            w._alertDot.transform.SetParent(frame, false);
            w._alertDot.rectTransform.sizeDelta = new Vector2(14f, 14f);
            w._alertDot.rectTransform.anchoredPosition = new Vector2(62f, 56f);
            w._alertDot.sprite = DiegeticUi.SprBtn;
            w._alertDot.raycastTarget = false;
            w._alert = Label(frame, new Vector2(100f, 56f), new Vector2(60f, 30f), 15f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft);
            w._nova = Label(frame, new Vector2(0f, 20f), new Vector2(230f, 30f), 22f, UiKit.Amber, TextAlignmentOptions.MidlineLeft);
            w._affair = Label(frame, new Vector2(0f, -18f), new Vector2(230f, 40f), 17f, UiKit.TextBright, TextAlignmentOptions.MidlineLeft);
            w._affair.textWrappingMode = TextWrappingModes.Normal;
            w._comms = Label(frame, new Vector2(0f, -58f), new Vector2(230f, 28f), 17f, UiKit.Cyan, TextAlignmentOptions.MidlineLeft);
            SetLayer(go.transform, layer);
            return w;
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

            // On the inside of the wrist, just behind the grip: the face looks out of the palm side (+x for a
            // left hand), text running up the forearm.
            transform.SetPositionAndRotation(anchor.TransformPoint(new Vector3(0.055f, -0.02f, -0.105f)),
                anchor.rotation * Quaternion.LookRotation(Vector3.left, Vector3.forward));
            var toEye = cam.transform.position - transform.position;
            var face = -transform.forward;
            var shown = toEye.magnitude < 0.75f && Vector3.Dot(face, toEye.normalized) > ShowDot;
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

        void Refresh()
        {
            _time.text = System.DateTime.Now.ToString("HH:mm");
            var level = AlertState.Level;
            _alertDot.color = level == AlertLevel.Red ? new Color(1f, 0.3f, 0.26f) : level == AlertLevel.Amber ? UiKit.Amber : UiKit.Ok;
            _alert.text = level == AlertLevel.Red ? Trans.Get("vr.screen.redAlert") : level == AlertLevel.Amber ? Trans.Get("vr.console.amberAlert") : string.Empty;
            var eco = EconomyService.Instance;
            _nova.text = eco != null ? Trans.Get("nova") + "  " + eco.Nova.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) : string.Empty;

            var watch = WatchMode.Instance;
            var now = FleetOrderGate.UnixNow();
            if (watch != null && watch.Affairs.Count > 0)
            {
                var a = watch.Affairs[0];
                var left = a.End > now ? "  ·  " + Core.Holo.TravelPlanner.TimeText(a.End - now) : string.Empty;
                _affair.text = a.Title + left;
            }
            else
            {
                _affair.text = Trans.Get("vr.watch.idle");
            }

            var comms = CommsService.Instance?.Total ?? 0;
            _comms.text = comms > 0 ? Trans.Format("vr.screen.comms", comms) : string.Empty;
        }
    }
}
