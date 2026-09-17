using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Core.UI
{
    /// <summary>
    /// Diegetic CIC login console. Keys from GetTranslations:
    /// https://www.stellar-universe.com/actionjs.php?action=GetTranslations
    /// </summary>
    public class MainMenuConsole : MonoBehaviour
    {
        static readonly Color Cyan = new(0.45f, 0.95f, 1f, 1f);
        static readonly Color CyanDim = new(0.25f, 0.7f, 0.85f, 1f);
        static readonly Color Amber = new(1f, 0.72f, 0.35f, 1f);
        static readonly Color PanelGlass = new(0.015f, 0.045f, 0.07f, 0.96f);
        static readonly Color FieldBg = new(0.03f, 0.1f, 0.14f, 0.92f);
        static readonly Color BtnPrimary = new(0.06f, 0.42f, 0.48f, 0.95f);
        static readonly Color BtnSecondary = new(0.08f, 0.22f, 0.28f, 0.95f);

        TMP_InputField _email;
        TMP_InputField _password;
        TMP_InputField _username;
        TMP_Text _status;
        Button _continue;
        Canvas _canvas;
        CicEnvironment _env;

        public void Bind(CicEnvironment env)
        {
            _env = env;
        }

        public async void Build()
        {
            await Trans.EnsureLoaded();
            EnsureEventSystem();
            _canvas = CreateCanvas();
            var root = Panel(_canvas.transform, new Vector2(1100f, 620f));

            // Brand is product identity, not a locale string.
            Label(root, "STELLAR UNIVERSE", 40f, FontStyles.Bold, new Vector2(0f, 240f), Cyan);
            Hairline(root, new Vector2(0f, 205f), 720f, Cyan * 0.55f);
            Label(root, Trans.Get("connectToUniverse"), 20f, FontStyles.Italic, new Vector2(0f, 175f), CyanDim);

            _email = Field(root, "email", Trans.Get("email"), new Vector2(0f, 95f),
                TouchScreenKeyboardType.EmailAddress);
            _password = Field(root, "password", Trans.Get("password"), new Vector2(0f, 15f),
                TouchScreenKeyboardType.Default, true);
            _username = Field(root, "username", Trans.Get("username"), new Vector2(0f, -65f),
                TouchScreenKeyboardType.Default);

            _continue = Button(root, Trans.Get("gettingStarted"), new Vector2(-250f, -170f), Resume, true);
            Button(root, Trans.Get("login"), new Vector2(0f, -170f), SignIn, true);
            Button(root, Trans.Get("createAccount"), new Vector2(250f, -170f), SignUp, false);

            _status = Label(root, string.Empty, 18f, FontStyles.Normal, new Vector2(0f, -250f), Amber);
            RefreshContinue();
        }

        void RefreshContinue()
        {
            var has = AuthManager.Ensure().HasSavedToken;
            if (_continue != null)
                _continue.gameObject.SetActive(has);
        }

        async void Resume()
        {
            await Run(Trans.Get("Loading"), () => AuthManager.Ensure().LoginToken());
        }

        async void SignIn()
        {
            await Run(Trans.Get("Loading"),
                () => AuthManager.Ensure().Login(_email.text.Trim(), _password.text));
        }

        async void SignUp()
        {
            await Run(Trans.Get("Loading"),
                () => AuthManager.Ensure().Register(_email.text.Trim(), _password.text,
                    _username.text.Trim()));
        }

        async Task Run(string pending, System.Func<Task<ApiResult>> work)
        {
            SetStatus(pending, Cyan);
            var result = await work();
            if (!result.Ok)
            {
                SetStatus(FriendlyError(result.Error), new Color(1f, 0.4f, 0.35f));
                RefreshContinue();
                return;
            }

            SetStatus(Trans.Get("welcome"), new Color(0.45f, 1f, 0.7f));
            SceneFlow.Go(SceneFlow.Bridge);
        }

        static string FriendlyError(string error)
        {
            if (!string.IsNullOrEmpty(error) &&
                (error.IndexOf("token", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 error.IndexOf("ip", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 error == "no_token" || error == "network" || error == "bad_json" ||
                 string.IsNullOrEmpty(error)))
                return Trans.Get("error_not_logged_in");
            return error;
        }

        void SetStatus(string text, Color color)
        {
            if (_status == null)
                return;
            _status.text = text;
            _status.color = color;
        }

        Canvas CreateCanvas()
        {
            var go = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));

            var parent = _env != null && _env.ConsoleMount != null ? _env.ConsoleMount : transform;
            go.transform.SetParent(parent, false);
            // Local identity: mount faces the player. Negative Z = toward player (in front of plate).
            go.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.00105f;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(1100f, 640f);
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2.5f;
            return canvas;
        }

        static RectTransform Panel(Transform parent, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = PanelGlass;

            // Cyan frame
            Frame(rt, size, 4f, Cyan * 0.7f);
            Frame(rt, size - new Vector2(16f, 16f), 1.5f, CyanDim * 0.45f);
            return rt;
        }

        static void Frame(Transform parent, Vector2 size, float thickness, Color color)
        {
            void Edge(string name, Vector2 pos, Vector2 s)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = s;
                rt.anchoredPosition = pos;
                go.GetComponent<Image>().color = color;
                go.GetComponent<Image>().raycastTarget = false;
            }

            Edge("FrameT", new Vector2(0f, size.y * 0.5f), new Vector2(size.x, thickness));
            Edge("FrameB", new Vector2(0f, -size.y * 0.5f), new Vector2(size.x, thickness));
            Edge("FrameL", new Vector2(-size.x * 0.5f, 0f), new Vector2(thickness, size.y));
            Edge("FrameR", new Vector2(size.x * 0.5f, 0f), new Vector2(thickness, size.y));
        }

        static void Hairline(Transform parent, Vector2 pos, float width, Color color)
        {
            var go = new GameObject("Hairline", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 2f);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
        }

        static TMP_Text Label(Transform parent, string text, float size, FontStyles style, Vector2 pos,
            Color? color = null)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(980f, 56f);
            rt.anchoredPosition = pos;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color ?? Cyan;
            tmp.raycastTarget = false;
            return tmp;
        }

        static TMP_InputField Field(Transform parent, string name, string placeholder, Vector2 pos,
            TouchScreenKeyboardType keyboard, bool hidden = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(700f, 58f);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = FieldBg;

            // Left accent bar
            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(go.transform, false);
            var art = accent.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0f, 0f);
            art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(0f, 0.5f);
            art.sizeDelta = new Vector2(4f, 0f);
            art.anchoredPosition = Vector2.zero;
            accent.GetComponent<Image>().color = Cyan;
            accent.GetComponent<Image>().raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>(), 20f);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.fontSize = 24f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            phGo.transform.SetParent(go.transform, false);
            Stretch(phGo.GetComponent<RectTransform>(), 20f);
            var ph = phGo.GetComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.fontSize = 24f;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(0.45f, 0.7f, 0.78f, 0.65f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;

            var field = go.GetComponent<TMP_InputField>();
            field.textViewport = rt;
            field.textComponent = text;
            field.placeholder = ph;
            field.keyboardType = keyboard;
            field.contentType = hidden
                ? TMP_InputField.ContentType.Password
                : TMP_InputField.ContentType.Standard;
            field.shouldHideMobileInput = false;
            field.caretColor = Cyan;
            field.selectionColor = new Color(0.2f, 0.7f, 0.85f, 0.35f);
            return field;
        }

        static Button Button(Transform parent, string label, Vector2 pos,
            UnityEngine.Events.UnityAction click, bool primary)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(230f, 64f);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = primary ? BtnPrimary : BtnSecondary;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = primary ? Cyan * 0.85f : CyanDim * 0.5f;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(click);
            var colors = btn.colors;
            colors.highlightedColor = primary
                ? new Color(0.12f, 0.55f, 0.6f, 1f)
                : new Color(0.12f, 0.32f, 0.38f, 1f);
            colors.pressedColor = new Color(0.05f, 0.25f, 0.3f, 1f);
            btn.colors = colors;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>());
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 20f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Cyan;
            tmp.raycastTarget = false;
            return btn;
        }

        static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, 8f);
            rt.offsetMax = new Vector2(-pad, -8f);
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            DontDestroyOnLoad(go);
        }
    }
}
