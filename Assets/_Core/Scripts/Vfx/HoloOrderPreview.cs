using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// Order chip beside a grabbed fleet — shows what fires on release (BattleGroup-style commit preview).
    /// </summary>
    public class HoloOrderPreview : MonoBehaviour
    {
        Canvas _canvas;
        Image _frame;
        Image _accent;
        Image _readyDot;
        TMP_Text _verb;
        TMP_Text _dest;

        public static HoloOrderPreview Ensure(Transform parent, CicArtKit art)
        {
            var existing = parent != null
                ? parent.GetComponentInChildren<HoloOrderPreview>(true)
                : null;
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return existing;
            }

            var go = new GameObject("OrderPreview");
            if (parent != null)
                go.transform.SetParent(parent, false);
            var preview = go.AddComponent<HoloOrderPreview>();
            preview.Build();
            return preview;
        }

        void Build()
        {
            // Slightly above / beside the glyph so it doesn't cover the ship silhouette.
            transform.localPosition = new Vector3(0.18f, 0.28f, 0f);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            gameObject.AddComponent<BillboardFace>();

            _canvas = DiegeticUi.WorldCanvas(transform, "ChipCanvas", new Vector2(440f, 128f),
                Vector3.zero, Quaternion.identity, 0.0008f);
            var ray = _canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            if (ray != null)
                ray.enabled = false;
            var gr = _canvas.GetComponent<GraphicRaycaster>();
            if (gr != null)
                gr.enabled = false;

            var frame = DiegeticUi.HoloFrame(_canvas.transform, new Vector2(420f, 110f));
            _frame = frame.GetComponent<Image>();
            if (_frame != null)
            {
                if (DiegeticUi.SprPanel != null)
                {
                    _frame.sprite = DiegeticUi.SprPanel;
                    _frame.type = Image.Type.Sliced;
                }
                else if (DiegeticUi.SprReadout != null)
                {
                    _frame.sprite = DiegeticUi.SprReadout;
                    _frame.type = Image.Type.Sliced;
                }

                _frame.color = new Color(0.08f, 0.22f, 0.28f, 0.96f);
                _frame.raycastTarget = false;
            }

            var accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGo.transform.SetParent(frame, false);
            var accentRt = accentGo.GetComponent<RectTransform>();
            accentRt.anchorMin = new Vector2(0f, 0f);
            accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.sizeDelta = new Vector2(12f, -10f);
            accentRt.anchoredPosition = new Vector2(6f, 0f);
            _accent = accentGo.GetComponent<Image>();
            _accent.color = CicArtKit.Cyan;
            _accent.raycastTarget = false;

            var dotGo = new GameObject("ReadyDot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(frame, false);
            var dotRt = dotGo.GetComponent<RectTransform>();
            dotRt.anchorMin = new Vector2(1f, 0.5f);
            dotRt.anchorMax = new Vector2(1f, 0.5f);
            dotRt.pivot = new Vector2(1f, 0.5f);
            dotRt.sizeDelta = new Vector2(18f, 18f);
            dotRt.anchoredPosition = new Vector2(-18f, 0f);
            _readyDot = dotGo.GetComponent<Image>();
            _readyDot.color = new Color(0.4f, 1f, 0.75f, 1f);
            _readyDot.raycastTarget = false;

            _verb = DiegeticUi.HoloLabel(frame, Trans.Get("CommandBridge"), new Vector2(-10f, 22f),
                new Vector2(320f, 34f), 18f, DiegeticUi.Cyan);
            _dest = DiegeticUi.HoloLabel(frame, "…", new Vector2(-10f, -20f),
                new Vector2(320f, 38f), 22f, Color.white);
            if (_dest != null)
                _dest.fontStyle = FontStyles.Bold;

            Hide();
        }

        public void ShowPending(HoloToken fleet, HoloToken target)
        {
            if (_canvas == null)
                Build();
            gameObject.SetActive(true);

            if (fleet != null && transform.parent != fleet.transform)
            {
                transform.SetParent(fleet.transform, false);
                transform.localPosition = new Vector3(0.18f, 0.28f, 0f);
                transform.localRotation = Quaternion.identity;
            }

            // Hide the fleet's own name label while the order chip is up.
            HoloZoneMap.SetTokenLabelVisible(fleet, false);

            if (target == null)
            {
                ApplyStyle(false);
                if (_verb != null)
                    _verb.text = Trans.Get("CommandBridge");
                if (_dest != null)
                    _dest.text = "…";
                return;
            }

            ApplyStyle(true);
            if (_verb != null)
                _verb.text = Trans.Get(ActionKey(target.Kind));
            if (_dest != null)
            {
                var dest = string.IsNullOrEmpty(target.DisplayName)
                    ? "#" + target.Id
                    : target.DisplayName.Replace('\n', ' ');
                _dest.text = "→  " + dest;
            }
        }

        public void Hide()
        {
            if (gameObject != null)
                gameObject.SetActive(false);
        }

        void ApplyStyle(bool armed)
        {
            if (_frame != null)
                _frame.color = armed
                    ? new Color(0.06f, 0.28f, 0.34f, 0.97f)
                    : new Color(0.12f, 0.14f, 0.16f, 0.92f);
            if (_accent != null)
                _accent.color = armed ? new Color(0.35f, 1f, 0.9f, 1f) : new Color(0.45f, 0.5f, 0.55f, 0.85f);
            if (_readyDot != null)
            {
                _readyDot.enabled = armed;
                _readyDot.color = armed ? new Color(0.4f, 1f, 0.75f, 1f) : Color.clear;
            }

            if (_verb != null)
                _verb.color = armed ? DiegeticUi.Cyan : new Color(0.65f, 0.7f, 0.75f, 0.95f);
            if (_dest != null)
                _dest.color = armed ? Color.white : new Color(0.7f, 0.75f, 0.8f, 0.9f);
        }

        static string ActionKey(HoloTokenKind kind)
        {
            switch (kind)
            {
                case HoloTokenKind.Planet:
                    return "MoveFleetToPlanet";
                case HoloTokenKind.Asteroid:
                    return "MoveFleetToAsteroid";
                case HoloTokenKind.System:
                    return "MoveFleetToSystem";
                default:
                    return "MoveFleet";
            }
        }
    }
}
