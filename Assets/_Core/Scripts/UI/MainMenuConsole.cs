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
    /// Diegetic CIC login console.
    /// Auto LoginToken when saved → Hub (continue / quit / logout).
    /// Forms otherwise; Login/Register land on Hub (not auto-Bridge).
    /// </summary>
    public class MainMenuConsole : MonoBehaviour
    {
        enum FormMode
        {
            SignIn,
            SignUp
        }

        enum PanelMode
        {
            Forms,
            Hub,
            Create
        }

        static readonly Color Cyan = new(0.45f, 0.95f, 1f, 1f);
        static readonly Color CyanDim = new(0.25f, 0.7f, 0.85f, 1f);
        static readonly Color Amber = new(1f, 0.72f, 0.35f, 1f);
        static readonly Color PanelGlass = new(0.015f, 0.045f, 0.07f, 0.96f);
        static readonly Color FieldBg = new(0.03f, 0.1f, 0.14f, 0.92f);
        static readonly Color BtnPrimary = new(0.06f, 0.42f, 0.48f, 0.95f);
        static readonly Color BtnSecondary = new(0.08f, 0.22f, 0.28f, 0.95f);
        static readonly Color BtnDanger = new(0.42f, 0.12f, 0.14f, 0.95f);

        TMP_InputField _email;
        TMP_InputField _password;
        TMP_InputField _username;
        TMP_Text _status;
        TMP_Text _modeHint;
        TMP_Text _hubGreeting;
        Button _loginTab;
        Button _signUpTab;
        Button _submit;
        RectTransform _formsRoot;
        RectTransform _hubRoot;
        EmpireCreationWizard _creation;
        SasTransmissions _board;
        TMP_Text _latest;
        TMP_Text _header;
        Canvas _canvas;
        CicEnvironment _env;
        FormMode _mode = FormMode.SignIn;
        PanelMode _panel = PanelMode.Forms;
        bool _busy;

        public void Bind(CicEnvironment env)
        {
            _env = env;
        }

        public async void Build()
        {
            await Trans.EnsureLoaded();
            EnsureEventSystem();
            _canvas = CreateCanvas();
            var root = Panel(_canvas.transform, new Vector2(1100f, 640f));

            _formsRoot = SubPanel(root, "Forms");
            _hubRoot = SubPanel(root, "Hub");
            _hubRoot.gameObject.SetActive(false);

            BuildForms(_formsRoot);
            BuildHub(_hubRoot);

            _status = Label(root, string.Empty, 18f, FontStyles.Normal, new Vector2(0f, -290f), Amber);
            _creation = EmpireCreationWizard.Create(root, _status,
                _env != null && _env.ConsoleMount != null ? _env.ConsoleMount : transform,
                () => ShowPanel(PanelMode.Hub), OnEmpireCreated);

            if (_env != null)
            {
                _board = SasTransmissions.Build(_env.transform, _env.Art);
                // The boarding door: walking through it (or its jamb control) is the same as Continue.
                var a = SasShell.DoorAngle * Mathf.Deg2Rad;
                var door = SasShell.Centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (SasShell.Radius - 0.1f);
                Core.Stations.RoomDoor.Build(_env.transform, "BridgeDoor", door, SasShell.DoorAngle + 180f,
                    Trans.Get("vr.sas.toBridge"), UiKit.Cyan, _env.Art, () => _panel == PanelMode.Hub && !_busy, EnterBridge);
            }

            SetMode(FormMode.SignIn);
            await TryAutoLogin();
        }

        void BuildForms(RectTransform root)
        {
            _modeHint = Label(root, Trans.Get("connectToUniverse"), 20f, FontStyles.Italic, new Vector2(0f, 175f),
                CyanDim);

            _email = Field(root, "email", Trans.Get("email"), new Vector2(0f, 95f),
                TouchScreenKeyboardType.EmailAddress);
            _password = Field(root, "password", Trans.Get("password"), new Vector2(0f, 15f),
                TouchScreenKeyboardType.Default, true);
            _username = Field(root, "username", Trans.Get("username"), new Vector2(0f, -65f),
                TouchScreenKeyboardType.Default);

            _loginTab = Button(root, Trans.Get("login"), new Vector2(-250f, -155f), () => SetMode(FormMode.SignIn),
                DiegeticUi.BtnStyle.Cyan);
            _signUpTab = Button(root, Trans.Get("createAccount"), new Vector2(0f, -155f),
                () => SetMode(FormMode.SignUp), DiegeticUi.BtnStyle.Ghost);
            _submit = Button(root, Trans.Get("login"), new Vector2(250f, -155f), Submit, DiegeticUi.BtnStyle.Cyan);
        }

        void BuildHub(RectTransform root)
        {
            _hubGreeting = Label(root, Trans.Get("welcome"), 22f, FontStyles.Italic, new Vector2(0f, 120f),
                CyanDim);

            _latest = Label(root, string.Empty, 17f, FontStyles.Normal, new Vector2(0f, 78f), Amber);
            _latest.richText = true;
            Button(root, Trans.Get("vr.menu.continue"), new Vector2(0f, 30f), EnterBridge, DiegeticUi.BtnStyle.Cyan);
            Button(root, Trans.Get("quit"), new Vector2(0f, -55f), QuitApp, DiegeticUi.BtnStyle.Ghost);
            Button(root, Trans.Get("logout"), new Vector2(0f, -140f), DoLogout, DiegeticUi.BtnStyle.Danger);
        }

        async Task TryAutoLogin()
        {
            var auth = AuthManager.Ensure();
            if (!auth.HasSavedToken)
            {
                ShowPanel(PanelMode.Forms);
                return;
            }

            SetStatus(Trans.Get("Loading"), Cyan);
            var result = await auth.LoginToken();
            if (!result.Ok)
            {
                SetStatus(FriendlyError(result.Error), new Color(1f, 0.4f, 0.35f));
                ShowPanel(PanelMode.Forms);
                return;
            }

            RefreshHubGreeting();
            SetStatus(Trans.Get("welcome"), new Color(0.45f, 1f, 0.7f));
            ShowPanel(PanelMode.Hub);
        }

        void ShowPanel(PanelMode panel)
        {
            _panel = panel;
            if (_formsRoot != null)
                _formsRoot.gameObject.SetActive(panel == PanelMode.Forms);
            if (_hubRoot != null)
                _hubRoot.gameObject.SetActive(panel == PanelMode.Hub);
            if (_creation != null)
            {
                if (panel == PanelMode.Create)
                    _creation.Open();
                else
                    _creation.Hide();
            }

            if (panel == PanelMode.Hub)
            {
                _board?.Show();
                Core.Utils.AsyncTap.Run(ShowLatest());
            }
            else
            {
                _board?.Hide();
            }

            if (_header == null && _formsRoot != null)
                _header = _formsRoot.parent.Find("Header/HeaderLabel")?.GetComponent<TMP_Text>();
            if (_header != null)
                _header.text = Trans.Get(panel == PanelMode.Create ? "create-empire" : "connectToUniverse");
        }

        /// <summary>Headline of the newest news under the greeting (GetLatestNews); the board holds the rest.</summary>
        async Task ShowLatest()
        {
            if (_latest == null)
                return;
            var r = await ActionJs.Get("GetLatestNews");
            var news = Core.Stations.ScreenKit.Object(r);
            if (_latest == null)
                return;
            var title = Core.Stations.ScreenKit.Localized(news, "title");
            _latest.text = title.Length > 0
                ? Trans.Format("vr.sas.latest", Core.Stations.ScreenKit.Verbatim(title))
                : string.Empty;
        }

        void RefreshHubGreeting()
        {
            if (_hubGreeting == null)
                return;
            var user = AuthManager.Ensure().User;
            var name = user != null && !string.IsNullOrEmpty(user.username)
                ? user.username
                : Trans.Get("welcome");
            _hubGreeting.text = name;
        }

        void SetMode(FormMode mode)
        {
            _mode = mode;
            var signUp = mode == FormMode.SignUp;
            if (_username != null)
                _username.gameObject.SetActive(signUp);

            if (_password != null)
                _password.GetComponent<RectTransform>().anchoredPosition =
                    signUp ? new Vector2(0f, 15f) : new Vector2(0f, -10f);
            if (_email != null)
                _email.GetComponent<RectTransform>().anchoredPosition =
                    signUp ? new Vector2(0f, 95f) : new Vector2(0f, 70f);

            if (_modeHint != null)
                _modeHint.text = signUp ? Trans.Get("createAccount") : Trans.Get("login");

            StyleTab(_loginTab, !signUp);
            StyleTab(_signUpTab, signUp);

            if (_submit != null)
            {
                var label = _submit.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.text = signUp ? Trans.Get("createAccount") : Trans.Get("login");
            }

            SetStatus(string.Empty, Amber);
        }

        void StyleTab(Button button, bool active)
        {
            if (button == null)
                return;
            var img = button.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = active ? DiegeticUi.SprTabActive : DiegeticUi.SprTabIdle;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
        }

        void Submit()
        {
            if (_mode == FormMode.SignUp)
                SignUp();
            else
                SignIn();
        }

        async void SignIn()
        {
            await RunAuth(() => AuthManager.Ensure().Login(_email.text.Trim(), _password.text));
        }

        async void SignUp()
        {
            await RunAuth(() => AuthManager.Ensure().Register(_email.text.Trim(), _password.text,
                _username.text.Trim()));
        }

        async Task RunAuth(System.Func<Task<ApiResult>> work)
        {
            if (_busy)
                return;
            _busy = true;
            SetStatus(Trans.Get("Loading"), Cyan);
            var result = await work();
            _busy = false;
            if (!result.Ok)
            {
                SetStatus(FriendlyError(result.Error), new Color(1f, 0.4f, 0.35f));
                ShowPanel(PanelMode.Forms);
                return;
            }

            RefreshHubGreeting();
            SetStatus(Trans.Get("welcome"), new Color(0.45f, 1f, 0.7f));
            ShowPanel(PanelMode.Hub);
        }

        void EnterBridge() => Core.Utils.AsyncTap.Run(EnterBridgeAsync());

        async Task EnterBridgeAsync()
        {
            var auth = AuthManager.Ensure();
            if (!auth.IsLoggedIn)
            {
                SetStatus(Trans.Get("error_not_logged_in"), new Color(1f, 0.4f, 0.35f));
                ShowPanel(PanelMode.Forms);
                return;
            }

            if (_busy)
                return;
            _busy = true;
            SetStatus(Trans.Get("Loading"), Cyan);
            var me = await auth.FetchMe();
            _busy = false;
            if (!me.Ok && !auth.NoEmpire)
            {
                CicCue.Fail(transform.position);
                SetStatus(FriendlyError(me.Error), new Color(1f, 0.4f, 0.35f));
                return;
            }

            // No empire yet: found it here, in the airlock (CreateEmpire). Never send the player to the website.
            if (!auth.HasEmpire)
            {
                SetStatus(Trans.Get("vr.menu.noEmpire"), Amber);
                ShowPanel(PanelMode.Create);
                return;
            }

            CicCue.Ok(transform.position);
            SceneFlow.Go(SceneFlow.Bridge);
        }

        async void OnEmpireCreated()
        {
            // The server just gave us a homeworld (users.systemid, planet ownership): re-read both.
            await AuthManager.Ensure().LoginToken();
            var me = await AuthManager.Ensure().FetchMe();
            await OwnedPlanets.Refresh();
            if (!me.Ok || !AuthManager.Ensure().HasEmpire)
            {
                SetStatus(FriendlyError(me.Error), new Color(1f, 0.4f, 0.35f));
                return;
            }

            SceneFlow.Go(SceneFlow.Bridge);
        }

        void DoLogout()
        {
            AuthManager.Ensure().Logout();
            SetStatus(Trans.Get("logout"), Amber);
            ShowPanel(PanelMode.Forms);
            SetMode(FormMode.SignIn);
        }

        void QuitApp()
        {
            CicCue.Ok(transform.position);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
            go.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.00105f;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(1100f, 660f);
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2.5f;
            return canvas;
        }

        static RectTransform Panel(Transform parent, Vector2 size)
        {
            DiegeticUi.EnsureEventSystem();
            var frame = DiegeticUi.HoloFrame(parent, size, Trans.Get("connectToUniverse"));
            return frame;
        }

        static RectTransform SubPanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = Vector2.zero;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
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
            TouchScreenKeyboardType keyboard, bool hidden = false) =>
            DiegeticUi.HoloField(parent, name, placeholder, pos, new Vector2(700f, 58f), keyboard, hidden);

        static Button Button(Transform parent, string label, Vector2 pos,
            UnityEngine.Events.UnityAction click, DiegeticUi.BtnStyle style)
        {
            return DiegeticUi.HoloButton(parent, label, pos, new Vector2(320f, 64f), click, style);
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
