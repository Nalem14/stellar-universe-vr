using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Core.UI
{
    public class MainMenuConsole : MonoBehaviour
    {
        TMP_InputField _email;
        TMP_InputField _password;
        TMP_InputField _username;
        TMP_Text _status;
        Button _continue;
        Canvas _canvas;

        public async void Build()
        {
            await Trans.EnsureLoaded();
            EnsureEventSystem();
            _canvas = CreateCanvas();
            var root = Panel(_canvas.transform, new Vector2(920f, 640f));

            // Brand title is not a locale string.
            Label(root, "STELLAR UNIVERSE", 44f, FontStyles.Bold, new Vector2(0f, 250f));
            Label(root, Trans.Get("vr.accessConsole"), 22f, FontStyles.Italic, new Vector2(0f, 200f),
                new Color(0.6f, 0.85f, 0.95f));

            _email = Field(root, "email", Trans.Get("email"), new Vector2(0f, 110f),
                TouchScreenKeyboardType.EmailAddress);
            _password = Field(root, "password", Trans.Get("password"), new Vector2(0f, 30f),
                TouchScreenKeyboardType.Default, true);
            _username = Field(root, "username", Trans.Get("username"), new Vector2(0f, -50f),
                TouchScreenKeyboardType.Default);

            _continue = Button(root, Trans.Get("vr.continue"), new Vector2(-220f, -160f), Resume);
            Button(root, Trans.Get("login"), new Vector2(0f, -160f), SignIn);
            Button(root, Trans.Get("createAccount"), new Vector2(220f, -160f), SignUp);

            _status = Label(root, string.Empty, 20f, FontStyles.Normal, new Vector2(0f, -250f),
                new Color(1f, 0.72f, 0.35f));
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
            await Run(Trans.Get("vr.resumingSession"), () => AuthManager.Ensure().LoginToken());
        }

        async void SignIn()
        {
            await Run(Trans.Get("vr.signingIn"),
                () => AuthManager.Ensure().Login(_email.text.Trim(), _password.text));
        }

        async void SignUp()
        {
            await Run(Trans.Get("vr.creatingAccount"),
                () => AuthManager.Ensure().Register(_email.text.Trim(), _password.text,
                    _username.text.Trim()));
        }

        async Task Run(string pending, System.Func<Task<ApiResult>> work)
        {
            SetStatus(pending, new Color(0.6f, 0.9f, 1f));
            var result = await work();
            if (!result.Ok)
            {
                SetStatus(FriendlyError(result.Error), new Color(1f, 0.45f, 0.35f));
                RefreshContinue();
                return;
            }

            SetStatus(Trans.Get("vr.accessGranted"), new Color(0.45f, 1f, 0.7f));
            SceneFlow.Go(SceneFlow.Bridge);
        }

        static string FriendlyError(string error)
        {
            // Server error bodies are already localized when they come from actionjs.
            if (!string.IsNullOrEmpty(error) &&
                (error.IndexOf("token", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 error.IndexOf("ip", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 error == "no_token"))
                return Trans.Get("vr.sessionExpiredIp");
            if (string.IsNullOrEmpty(error) || error == "network" || error == "bad_json")
                return Trans.Get("vr.loginFailed");
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
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 1.25f, 1.35f);
            go.transform.localScale = Vector3.one * 0.00115f;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000f, 700f);
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            return canvas;
        }

        static RectTransform Panel(Transform parent, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.08f, 0.88f);
            return rt;
        }

        static TMP_Text Label(Transform parent, string text, float size, FontStyles style, Vector2 pos,
            Color? color = null)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(840f, 64f);
            rt.anchoredPosition = pos;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color ?? new Color(0.75f, 0.95f, 1f);
            tmp.raycastTarget = false;
            return tmp;
        }

        static TMP_InputField Field(Transform parent, string name, string placeholder, Vector2 pos,
            TouchScreenKeyboardType keyboard, bool hidden = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(640f, 64f);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = new Color(0.05f, 0.12f, 0.16f, 0.95f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>(), 16f);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.fontSize = 26f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            phGo.transform.SetParent(go.transform, false);
            Stretch(phGo.GetComponent<RectTransform>(), 16f);
            var ph = phGo.GetComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.fontSize = 26f;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(0.5f, 0.7f, 0.78f, 0.7f);
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
            return field;
        }

        static Button Button(Transform parent, string label, Vector2 pos,
            UnityEngine.Events.UnityAction click)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, 70f);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = new Color(0.08f, 0.35f, 0.42f, 0.95f);
            go.GetComponent<Button>().onClick.AddListener(click);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>());
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 22f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.8f, 0.98f, 1f);
            tmp.raycastTarget = false;
            return go.GetComponent<Button>();
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
