using Core.App;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// Mobile touch HUD overlay:
    /// - Dynamic virtual joystick (outer ring + thumb knob)
    /// - Onscreen Action button for center interactions
    /// - Tactical command buttons bar when seated in the Captain's Chair
    /// - Quick access Menu button
    /// </summary>
    public sealed class MobileHud : MonoBehaviour
    {
        public static MobileHud Instance { get; private set; }

        Canvas _canvas;
        RectTransform _joystickBase;
        RectTransform _joystickKnob;
        Button _actionButton;
        TMP_Text _actionLabel;
        GameObject _commandBar;
        Button _menuBtn;

        public static void Ensure()
        {
            if (Instance != null)
                return;
            var go = new GameObject("MobileHud");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MobileHud>();
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
            _canvas.sortingOrder = 998;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            BuildJoystickVisuals();
            BuildActionButton();
            BuildCommandBar();
            BuildMenuButton();
        }

        void BuildJoystickVisuals()
        {
            // Base ring
            var baseGo = new GameObject("JoystickBase", typeof(RectTransform), typeof(Image));
            baseGo.transform.SetParent(transform, false);
            _joystickBase = baseGo.GetComponent<RectTransform>();
            _joystickBase.sizeDelta = new Vector2(160f, 160f);
            var baseImg = baseGo.GetComponent<Image>();
            baseImg.color = new Color(0.2f, 0.7f, 0.9f, 0.25f);
            baseImg.raycastTarget = false;

            // Knob
            var knobGo = new GameObject("JoystickKnob", typeof(RectTransform), typeof(Image));
            knobGo.transform.SetParent(transform, false);
            _joystickKnob = knobGo.GetComponent<RectTransform>();
            _joystickKnob.sizeDelta = new Vector2(65f, 65f);
            var knobImg = knobGo.GetComponent<Image>();
            knobImg.color = new Color(0.4f, 0.95f, 1f, 0.75f);
            knobImg.raycastTarget = false;

            ShowJoystick(false, Vector2.zero);
        }

        void BuildActionButton()
        {
            var btnGo = new GameObject("ActionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(140f, 75f);
            rt.anchoredPosition = new Vector2(-40f, 140f);

            var img = btnGo.GetComponent<Image>();
            img.color = new Color(0.08f, 0.25f, 0.35f, 0.85f);

            _actionButton = btnGo.GetComponent<Button>();
            _actionButton.onClick.AddListener(() =>
            {
                MobileTouchController.Instance?.PerformCenterAction();
            });

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(btnGo.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.sizeDelta = Vector2.zero;

            _actionLabel = labelGo.GetComponent<TextMeshProUGUI>();
            _actionLabel.text = string.Empty;
            _actionLabel.fontStyle = FontStyles.UpperCase;
            _labels.Add((_actionLabel, "action"));
            _actionLabel.alignment = TextAlignmentOptions.Center;
            _actionLabel.fontSize = 18f;
            _actionLabel.color = Color.white;
            _actionLabel.raycastTarget = false;
        }

        void BuildCommandBar()
        {
            _commandBar = new GameObject("MobileCommandBar", typeof(RectTransform));
            _commandBar.transform.SetParent(transform, false);
            var rt = _commandBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(980f, 70f);
            rt.anchoredPosition = new Vector2(0f, 25f);

            float startX = -400f;
            float stepX = 160f;

            AddCmdButton("fleets", new Vector2(startX + 0 * stepX, 0f), () =>
            {
                var tp = FindFirstObjectByType<BridgeViewTeleporter>();
                if (tp != null) Core.Utils.AsyncTap.Run(tp.RefreshList());
            });

            AddCmdButton("vr.mobile.map", new Vector2(startX + 1 * stepX, 0f), () =>
            {
                var map = FindFirstObjectByType<HoloMapController>();
                if (map != null) map.SetMode(map.Mode == HoloMapMode.System ? HoloMapMode.Galaxy : HoloMapMode.System);
            });

            AddCmdButton("vr.station.comms", new Vector2(startX + 2 * stepX, 0f), () =>
            {
                var comms = Core.Stations.CommsConsole.Instance;
                if (comms != null) { if (comms.IsOpen) comms.Close(); else comms.Open(null); }
            });

            AddCmdButton("vr.station.ops", new Vector2(startX + 3 * stepX, 0f), () =>
            {
                var ops = Core.Stations.OpsConsole.Instance;
                if (ops != null) { if (ops.IsOpen) ops.Close(); else ops.Open(null, FocusContext.Current != null ? FocusContext.Current.ViewPlanetId : 0); }
            });

            AddCmdButton("vr.station.tactical", new Vector2(startX + 4 * stepX, 0f), () =>
            {
                var armory = Core.Stations.ArmoryConsole.Instance;
                if (armory != null) { if (armory.IsOpen) armory.Close(); else armory.Open(null, 0, 0); }
            });

            AddCmdButton("vr.pc.standUp", new Vector2(startX + 5 * stepX, 0f), () =>
            {
                CaptainCommandMode.Instance?.ExitCommandMode();
            }, isAmber: true);

            _commandBar.SetActive(false);
        }

        /// <summary>Every on-screen label and its <see cref="Trans"/> key (re-read when the language changes).</summary>
        readonly System.Collections.Generic.List<(TMP_Text text, string key)> _labels = new();
        string _lang;

        void Relabel()
        {
            _lang = Trans.IsReady ? Trans.Lang : null;
            foreach (var (text, key) in _labels)
                if (text != null)
                    text.text = Trans.Get(key);
        }

        void AddCmdButton(string labelKey, Vector2 pos, UnityEngine.Events.UnityAction onClick, bool isAmber = false)
        {
            var btnGo = new GameObject(labelKey + "_Btn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(_commandBar.transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140f, 55f);
            rt.anchoredPosition = pos;

            var img = btnGo.GetComponent<Image>();
            img.color = isAmber ? new Color(0.45f, 0.25f, 0.05f, 0.9f) : new Color(0.06f, 0.28f, 0.36f, 0.9f);

            var btn = btnGo.GetComponent<Button>();
            btn.onClick.AddListener(onClick);

            var lGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            lGo.transform.SetParent(btnGo.transform, false);
            var lrt = lGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.sizeDelta = Vector2.zero;

            var tmp = lGo.GetComponent<TextMeshProUGUI>();
            tmp.text = Trans.Get(labelKey);
            tmp.fontStyle = FontStyles.UpperCase;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10f;
            tmp.fontSizeMax = 16f;
            _labels.Add((tmp, labelKey));
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = isAmber ? new Color(1f, 0.8f, 0.4f) : Color.white;
            tmp.raycastTarget = false;
        }

        void BuildMenuButton()
        {
            var btnGo = new GameObject("MobileMenuBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(110f, 50f);
            rt.anchoredPosition = new Vector2(25f, -25f);

            var img = btnGo.GetComponent<Image>();
            img.color = new Color(0.05f, 0.2f, 0.28f, 0.8f);

            _menuBtn = btnGo.GetComponent<Button>();
            _menuBtn.onClick.AddListener(() =>
            {
                var qm = FindFirstObjectByType<QuickMenu>();
                if (qm != null)
                    qm.SendMessage("Toggle", UnityEngine.SendMessageOptions.DontRequireReceiver);
            });

            var lGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            lGo.transform.SetParent(btnGo.transform, false);
            var lrt = lGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.sizeDelta = Vector2.zero;

            var tmp = lGo.GetComponent<TextMeshProUGUI>();
            tmp.text = Trans.Get("menu");
            tmp.fontStyle = FontStyles.UpperCase;
            _labels.Add((tmp, "menu"));
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 16f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }

        public void ShowJoystick(bool show, Vector2 screenPos)
        {
            if (_joystickBase == null || _joystickKnob == null)
                return;

            _joystickBase.gameObject.SetActive(show);
            _joystickKnob.gameObject.SetActive(show);

            if (show)
            {
                _joystickBase.position = screenPos;
                _joystickKnob.position = screenPos;
            }
        }

        public void UpdateJoystickVisual(Vector2 origin, Vector2 knobPos)
        {
            if (_joystickBase == null || _joystickKnob == null)
                return;

            _joystickBase.position = origin;
            _joystickKnob.position = knobPos;
        }

        void Update()
        {
            if (!PcPlatformBoot.IsMobile)
            {
                if (_canvas != null && _canvas.enabled)
                    _canvas.enabled = false;
                return;
            }

            if (_canvas != null && !_canvas.enabled)
                _canvas.enabled = true;
            if (Trans.IsReady && _lang != Trans.Lang)
                Relabel();

            bool isSeated = CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode;
            if (_commandBar != null && _commandBar.activeSelf != isSeated)
            {
                _commandBar.SetActive(isSeated);
            }
        }
    }
}
