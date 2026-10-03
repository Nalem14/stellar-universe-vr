using Core.App;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// Subtle, diegetic HUD overlay for PC Desktop mode:
    /// - Center crosshair (subtle cyan dot/ring that reacts on interactable hover)
    /// - Contextual action prompt ([E] / [Clic G] S'asseoir, Ouvrir, etc.)
    /// - Tactical shortcut bar when seated in the Captain's Chair
    /// - General key hints ([Tab] Curseur libre, [Échap] Menu)
    /// </summary>
    public sealed class PcHud : MonoBehaviour
    {
        public static PcHud Instance { get; private set; }

        Canvas _canvas;
        Image _crosshair;
        TMP_Text _prompt;
        TMP_Text _commandBar;
        TMP_Text _cursorHint;

        float _hoverScale = 1f;
        float _targetHoverScale = 1f;

        public static void Ensure()
        {
            if (Instance != null)
                return;
            var go = new GameObject("PcHud");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PcHud>();
            Instance.Build();
        }

        void Awake()
        {
            if (Instance == null)
                Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            // Crosshair (center screen)
            var xhairGo = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
            xhairGo.transform.SetParent(transform, false);
            var xhairRt = xhairGo.GetComponent<RectTransform>();
            xhairRt.sizeDelta = new Vector2(10f, 10f);
            xhairRt.anchoredPosition = Vector2.zero;
            _crosshair = xhairGo.GetComponent<Image>();
            _crosshair.color = new Color(0.4f, 0.9f, 1f, 0.65f);
            _crosshair.raycastTarget = false;

            // Prompt text (bottom-center)
            var promptGo = new GameObject("ActionPrompt", typeof(RectTransform), typeof(TextMeshProUGUI));
            promptGo.transform.SetParent(transform, false);
            var promptRt = promptGo.GetComponent<RectTransform>();
            promptRt.anchorMin = new Vector2(0.5f, 0f);
            promptRt.anchorMax = new Vector2(0.5f, 0f);
            promptRt.pivot = new Vector2(0.5f, 0f);
            promptRt.sizeDelta = new Vector2(800f, 40f);
            promptRt.anchoredPosition = new Vector2(0f, 90f);
            _prompt = promptGo.GetComponent<TextMeshProUGUI>();
            _prompt.alignment = TextAlignmentOptions.Center;
            _prompt.fontSize = 20f;
            _prompt.color = Color.white;
            _prompt.raycastTarget = false;
            _prompt.richText = true;

            // Command bar (bottom when seated)
            var cmdGo = new GameObject("CommandBar", typeof(RectTransform), typeof(TextMeshProUGUI));
            cmdGo.transform.SetParent(transform, false);
            var cmdRt = cmdGo.GetComponent<RectTransform>();
            cmdRt.anchorMin = new Vector2(0.5f, 0f);
            cmdRt.anchorMax = new Vector2(0.5f, 0f);
            cmdRt.pivot = new Vector2(0.5f, 0f);
            cmdRt.sizeDelta = new Vector2(1200f, 35f);
            cmdRt.anchoredPosition = new Vector2(0f, 25f);
            _commandBar = cmdGo.GetComponent<TextMeshProUGUI>();
            _commandBar.alignment = TextAlignmentOptions.Center;
            _commandBar.fontSize = 17f;
            _commandBar.color = new Color(0.45f, 0.95f, 1f, 0.85f);
            _commandBar.raycastTarget = false;
            _commandBar.richText = true;
            _commandBar.text = "<b>[F]</b> Flottes  •  <b>[M]</b> Carte Galaxie/Système  •  <b>[C]</b> Comms  •  <b>[O]</b> Opérations  •  <b>[T]</b> Tactique  •  <b>[Espace]</b> Se lever";
            _commandBar.gameObject.SetActive(false);

            // Cursor hint (bottom-right)
            var hintGo = new GameObject("CursorHint", typeof(RectTransform), typeof(TextMeshProUGUI));
            hintGo.transform.SetParent(transform, false);
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(1f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(1f, 0f);
            hintRt.sizeDelta = new Vector2(300f, 30f);
            hintRt.anchoredPosition = new Vector2(-20f, 15f);
            _cursorHint = hintGo.GetComponent<TextMeshProUGUI>();
            _cursorHint.alignment = TextAlignmentOptions.Right;
            _cursorHint.fontSize = 14f;
            _cursorHint.color = new Color(0.7f, 0.8f, 0.9f, 0.5f);
            _cursorHint.raycastTarget = false;
            _cursorHint.richText = true;
            _cursorHint.text = "<b>[Tab]</b> Curseur libre  •  <b>[Échap]</b> Menu";
        }

        public void SetHover(bool isHovering, string promptText = null)
        {
            _targetHoverScale = isHovering ? 1.5f : 1.0f;
            if (_crosshair != null)
            {
                _crosshair.color = isHovering
                    ? new Color(1f, 0.75f, 0.35f, 0.95f) // Amber on hover
                    : new Color(0.4f, 0.9f, 1f, 0.65f);  // Cyan idle
            }

            if (_prompt != null)
            {
                _prompt.text = promptText ?? string.Empty;
            }
        }

        void Update()
        {
            if (!PcPlatformBoot.IsPcDesktop)
            {
                if (_canvas != null && _canvas.enabled)
                    _canvas.enabled = false;
                return;
            }

            if (_canvas != null && !_canvas.enabled)
                _canvas.enabled = true;

            var ctrl = PcDesktopController.Instance;
            bool isLocked = ctrl != null && ctrl.IsCursorLocked;

            // Only show crosshair when in mouse-look mode
            if (_crosshair != null)
            {
                _crosshair.gameObject.SetActive(isLocked);
                _hoverScale = Mathf.Lerp(_hoverScale, _targetHoverScale, 15f * Time.unscaledDeltaTime);
                _crosshair.rectTransform.localScale = Vector3.one * _hoverScale;
            }

            // Command bar visibility depends on command mode
            bool isSeated = CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode;
            if (_commandBar != null && _commandBar.gameObject.activeSelf != isSeated)
            {
                _commandBar.gameObject.SetActive(isSeated);
            }
        }
    }
}
