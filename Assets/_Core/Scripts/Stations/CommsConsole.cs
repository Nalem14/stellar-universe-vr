using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// Comms station console — web PanelChatUI (galactic channel + private conversations) and
    /// MailWindowUI (inbox / sent / compose / read), on one holo screen the Comms officer brings up.
    /// Channel: GetChat every 3 s while shown (lastid), AddChat ("console:" = server notice, "pm_sent:" =
    /// /w whisper). Private: GetPrivateConversations, SearchPlayers to start one, thread by
    /// GetPrivateMessages (lastid) + SendPrivateMessage. Mail: GetMails (folder, inbox filter), GetMail
    /// (marks read), SendMail (recipient = name or id), DeleteMail in two presses, reply prefills "Re:".
    /// Text typed with the Quest keyboard; player text is shown verbatim (no rich-text injection).
    /// </summary>
    public sealed class CommsConsole : MonoBehaviour
    {
        enum Tab
        {
            Channel,
            Private,
            Mail
        }

        enum MailView
        {
            Inbox,
            Sent,
            Read,
            Compose
        }

        static readonly Vector2 Size = new(1.1f, 0.7f);
        static readonly Color Accent = new(0.55f, 0.85f, 0.45f, 1f);
        static readonly string[] Filters = { string.Empty, "player", "system", "battle", "diplomacy" };
        const float Reach = 1.25f;
        const float MaxBearing = 20f;
        const float ChatEvery = 3f;
        const int ChatLines = 8;
        const int ThreadLines = 7;
        const int MailsPerPage = 6;

        public static CommsConsole Instance { get; private set; }
        public bool IsOpen => _open;

        HoloScreen _screen;
        RectTransform _frame;
        RectTransform _body;
        TMP_Text _status;
        TMP_Text _chips;
        readonly Button[] _tabs = new Button[3];

        GameObject _sayGroup;
        TMP_InputField _say;
        GameObject _searchGroup;
        TMP_InputField _search;
        GameObject _composeGroup;
        TMP_InputField _to;
        TMP_InputField _subject;
        TMP_InputField _content;

        Transform _anchor;
        bool _open;
        Tab _tab;
        MailView _mailView;
        int _filter;
        int _page;
        bool _busy;
        float _pollAt;
        bool _polling;

        readonly List<JObject> _chat = new();
        int _chatLast;
        JArray _conversations;
        JArray _searchResults;
        int _contactId;
        string _contactName = string.Empty;
        readonly List<JObject> _thread = new();
        int _threadLast;
        JArray _mails;
        JObject _mail;
        int _confirmDelete;
        float _confirmUntil;

        public static CommsConsole Build(Transform room)
        {
            var rig = new GameObject("CommsConsoleRig").transform;
            rig.SetParent(room, false);
            var console = rig.gameObject.AddComponent<CommsConsole>();
            console.BuildScreen(rig);
            rig.gameObject.SetActive(false);
            return console;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (CommsService.Instance != null)
                CommsService.Instance.Changed -= RenderChips;
            if (Instance == this)
                Instance = null;
        }

        // ── Shell ──────────────────────────────────────────────────────────────────

        void BuildScreen(Transform rig)
        {
            _screen = HoloScreen.Create(rig, "CommsConsole", Size, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.comms.title"));
            _screen.SetAccent(Accent, 0.5f);
            _frame = _screen.Content;

            _chips = DiegeticUi.HoloLabel(_frame, string.Empty, new Vector2(-250f, 245f), new Vector2(540f, 46f), 20f,
                UiKit.TextBright, TextAlignmentOptions.MidlineLeft);
            _chips.richText = true;
            DiegeticUi.HoloButton(_frame, Trans.Get("close"), new Vector2(465f, 245f), new Vector2(140f, 46f), Close,
                DiegeticUi.BtnStyle.Ghost);
            // Wars and alliances have their own room: the officer walks the captain there.
            DiegeticUi.HoloButton(_frame, Trans.Get("vr.diplo.open"), new Vector2(290f, 245f), new Vector2(190f, 46f), () =>
            {
                if (DiplomacyRoom.Instance == null || DiplomacyRoom.AnyRoomInside)
                    return;
                Close();
                Run(DiplomacyRoom.Instance.Enter());
            }, DiegeticUi.BtnStyle.Amber);

            var tabKeys = new[] { "vr.comms.tab.channel", "vr.comms.tab.private", "vr.comms.tab.mail" };
            for (var i = 0; i < _tabs.Length; i++)
            {
                var tab = (Tab)i;
                _tabs[i] = DiegeticUi.HoloButton(_frame, Trans.Get(tabKeys[i]), new Vector2(-360f + i * 360f, 186f),
                    new Vector2(348f, 48f), () => SetTab(tab), DiegeticUi.BtnStyle.Ghost);
            }

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(_frame, false);
            _body = bodyGo.GetComponent<RectTransform>();
            _body.sizeDelta = new Vector2(1060f, 420f);

            // Typing fields live outside the rebuilt body so a poll never steals the keyboard.
            _sayGroup = Group("SayBar");
            _say = DiegeticUi.HoloField(_sayGroup.transform, "Say", Trans.Get("vr.comms.say"), new Vector2(-95f, -245f),
                new Vector2(850f, 50f), TouchScreenKeyboardType.Default);
            _say.characterLimit = 500;
            _say.onSubmit.AddListener(_ => Run(Send()));
            DiegeticUi.HoloButton(_sayGroup.transform, Trans.Get("send"), new Vector2(435f, -245f), new Vector2(170f, 50f),
                () => Run(Send()), DiegeticUi.BtnStyle.Cyan);

            _searchGroup = Group("SearchBar");
            _search = DiegeticUi.HoloField(_searchGroup.transform, "Search", Trans.Get("vr.comms.searchPlayer"),
                new Vector2(-435f, 110f), new Vector2(190f, 46f), TouchScreenKeyboardType.Default);
            _search.characterLimit = 32;
            _search.onSubmit.AddListener(_ => Run(Search()));
            DiegeticUi.HoloButton(_searchGroup.transform, Trans.Get("vr.comms.find"), new Vector2(-265f, 110f), new Vector2(96f, 46f),
                () => Run(Search()), DiegeticUi.BtnStyle.Ghost);

            _composeGroup = Group("Compose");
            _to = DiegeticUi.HoloField(_composeGroup.transform, "To", Trans.Get("recipient"), new Vector2(-235f, 82f),
                new Vector2(560f, 48f), TouchScreenKeyboardType.Default);
            _to.characterLimit = 64;
            _subject = DiegeticUi.HoloField(_composeGroup.transform, "Subject", Trans.Get("subject"),
                new Vector2(-95f, 26f), new Vector2(840f, 48f), TouchScreenKeyboardType.Default);
            _subject.characterLimit = 255;
            _content = DiegeticUi.HoloField(_composeGroup.transform, "Content", Trans.Get("vr.comms.content"),
                new Vector2(-95f, -125f), new Vector2(840f, 230f), TouchScreenKeyboardType.Default);
            _content.lineType = TMP_InputField.LineType.MultiLineNewline;
            _content.characterLimit = 4000;
            _content.textComponent.alignment = TextAlignmentOptions.TopLeft;
            _content.textComponent.fontSize = 19f;
            ((TMP_Text)_content.placeholder).alignment = TextAlignmentOptions.TopLeft;
            DiegeticUi.HoloButton(_composeGroup.transform, Trans.Get("send"), new Vector2(435f, -210f),
                new Vector2(170f, 54f), () => Run(SendMail()), DiegeticUi.BtnStyle.Cyan);

            _status = DiegeticUi.HoloLabel(_frame, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f),
                18f, DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);
        }

        GameObject Group(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_frame, false);
            go.GetComponent<RectTransform>().sizeDelta = _screen.PixelSize;
            go.SetActive(false);
            return go;
        }

        // ── Open / close ───────────────────────────────────────────────────────────

        public void Open(Transform officerAnchor)
        {
            _anchor = officerAnchor;
            _open = true;
            gameObject.SetActive(true);
            Place();
            if (CommsService.Instance != null)
            {
                CommsService.Instance.Changed -= RenderChips;
                CommsService.Instance.Changed += RenderChips;
            }

            // Unread private traffic first, then unread mail, else the channel.
            var comms = CommsService.Instance;
            var tab = comms != null && comms.PmUnread > 0 ? Tab.Private
                : comms != null && comms.MailUnread > 0 ? Tab.Mail : Tab.Channel;
            _mailView = MailView.Inbox;
            _mails = null;
            _conversations = null;
            SetStatus(string.Empty);
            SetTab(tab);
            CicCue.RadioOpen(transform.position);
        }

        public void Close()
        {
            if (_open && _anchor != null)
                _anchor.GetComponentInParent<CrewOfficer>()?.LookAt(null);
            _open = false;
            if (CommsService.Instance != null)
                CommsService.Instance.Changed -= RenderChips;
            gameObject.SetActive(false);
        }

        void Place()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var eye = cam.transform.position;
            var look = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (look.sqrMagnitude < 0.01f)
                look = Vector3.forward;
            look.Normalize();
            var dir = look;
            if (_anchor != null)
            {
                var toOfficer = Vector3.ProjectOnPlane(_anchor.position - eye, Vector3.up);
                if (toOfficer.sqrMagnitude > 0.01f)
                {
                    var angle = Mathf.Clamp(Vector3.SignedAngle(look, toOfficer.normalized, Vector3.up), -MaxBearing,
                        MaxBearing);
                    dir = Quaternion.AngleAxis(angle, Vector3.up) * look;
                }
            }

            var p = eye + dir * Reach;
            // Above the table rim (its buttons would otherwise cut the lower rows).
            p.y = eye.y - 0.06f;
            transform.position = p;
            ScreenMount.FaceViewer(transform, eye, 1f, 6f);
        }

        void SetTab(Tab tab)
        {
            _tab = tab;
            _page = 0;
            _pollAt = 0f;
            SetStatus(string.Empty);
            Render();
        }

        void SetStatus(string text, bool error = false)
        {
            _status.text = text ?? string.Empty;
            _status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        static void Run(Task task) => AsyncTap.Run(task);

        // ── Render ────────────────────────────────────────────────────────────────

        void Render()
        {
            if (!_open)
                return;
            for (var i = _body.childCount - 1; i >= 0; i--)
                DestroyImmediate(_body.GetChild(i).gameObject);
            for (var i = 0; i < _tabs.Length; i++)
                SetButtonStyle(_tabs[i], (Tab)i == _tab);
            RenderChips();

            _sayGroup.SetActive(_tab == Tab.Channel || (_tab == Tab.Private && _contactId > 0));
            _searchGroup.SetActive(_tab == Tab.Private);
            _composeGroup.SetActive(_tab == Tab.Mail && _mailView == MailView.Compose);

            switch (_tab)
            {
                case Tab.Channel:
                    RenderChannel();
                    break;
                case Tab.Private:
                    RenderPrivate();
                    break;
                case Tab.Mail:
                    RenderMail();
                    break;
            }
        }

        void RenderChips()
        {
            var c = CommsService.Instance;
            if (c == null || _chips == null)
                return;
            _chips.text = Trans.Format("vr.comms.mailUnread", c.MailUnread) + "     " +
                          Trans.Format("vr.comms.pmUnread", c.PmUnread);
            _chips.color = c.Total > 0 ? UiKit.Amber : DiegeticUi.CyanDim;
        }

        static void SetButtonStyle(Button b, bool active)
        {
            var label = b.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.color = active ? UiKit.Cyan : new Color(0.7f, 0.85f, 0.92f, 0.8f);
            var img = b.GetComponent<Image>();
            if (img != null)
                img.color = active ? new Color(0.6f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.55f);
        }

        TMP_Text Line(string text, float x, float y, float size, Color color, float width,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(x, y), new Vector2(width, size * 1.9f), size, color,
                align);
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>Player text shown as-is: no TMP tag in it may style or hide anything.</summary>
        static string Verbatim(string s) =>
            "<noparse>" + (s ?? string.Empty).Replace("</noparse>", "</ noparse>").Replace('\n', ' ') + "</noparse>";

        static string Clock(long unix, bool withDay)
        {
            if (unix <= 0)
                return string.Empty;
            var t = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime();
            return withDay && t.Date != DateTime.Now.Date ? t.ToString("dd/MM HH:mm") : t.ToString("HH:mm");
        }

        static int Me() => FocusContext.OwnedUserId();

        // ── Channel ───────────────────────────────────────────────────────────────

        void RenderChannel()
        {
            if (_chat.Count == 0)
            {
                Line(Trans.Get(_polling ? "Loading" : "vr.comms.noChat"), 0f, 20f, 22f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                return;
            }

            var first = Mathf.Max(0, _chat.Count - ChatLines);
            for (var i = first; i < _chat.Count; i++)
            {
                var m = _chat[i];
                var y = 100f - (i - first) * 40f;
                var mine = FocusContext.AsInt(m["userid"]) == Me();
                var name = FocusContext.AsString(m["contact_name"]);
                if (m["console"] != null)
                {
                    Line(Trans.Format("vr.comms.console", Verbatim(FocusContext.AsString(m["message"]))), -20f, y, 18f,
                        new Color(0.6f, 0.68f, 0.72f, 1f), 1020f);
                    continue;
                }

                Line("<color=#6fa9bd>" + Clock(FocusContext.AsLong(m["date"]), false) + "</color>  <b><color=" +
                     (mine ? "#7dffb0" : "#7fd8ff") + ">" + Verbatim(name) + "</color></b>  " +
                     Verbatim(FocusContext.AsString(m["message"])), -20f, y, 19f, UiKit.TextBright, 1020f);
            }
        }

        async Task PollChat()
        {
            var res = await ActionJs.Get("GetChat", new Dictionary<string, string> { { "lastid", _chatLast.ToString() } });
            if (!res.Ok || string.IsNullOrEmpty(res.Body))
                return;
            JArray rows;
            try
            {
                rows = JToken.Parse(res.Body) as JArray;
            }
            catch
            {
                return;
            }

            if (rows == null || rows.Count == 0)
                return;
            foreach (var r in rows)
            {
                if (r is not JObject o)
                    continue;
                var id = FocusContext.AsInt(o["id"]);
                if (id <= _chatLast)
                    continue;
                _chatLast = id;
                _chat.Add(o);
            }

            while (_chat.Count > 60)
                _chat.RemoveAt(0);
            if (_open && _tab == Tab.Channel)
                Render();
        }

        async Task Send()
        {
            var text = (_say.text ?? string.Empty).Trim();
            if (text.Length == 0 || _busy)
                return;
            _busy = true;
            try
            {
                if (_tab == Tab.Private && _contactId > 0)
                {
                    var r = await ActionJs.Get("SendPrivateMessage", new Dictionary<string, string>
                    {
                        { "contact_id", _contactId.ToString() },
                        { "message", text }
                    });
                    Feedback(r, "vr.comms.sent");
                    if (r.Ok)
                    {
                        _say.text = string.Empty;
                        await PollThread();
                    }

                    return;
                }

                var res = await ActionJs.Get("AddChat", new Dictionary<string, string> { { "message", text } });
                if (!res.Ok)
                {
                    Feedback(res, null);
                    return;
                }

                _say.text = string.Empty;
                var body = res.Body ?? string.Empty;
                if (body.StartsWith("console:", StringComparison.Ordinal))
                {
                    // Server notice (muted, unknown whisper target, admin command output): shown in the channel only.
                    _chat.Add(new JObject { ["console"] = true, ["message"] = body.Substring(8) });
                }
                else if (body.StartsWith("pm_sent:", StringComparison.Ordinal))
                {
                    var name = string.Empty;
                    try
                    {
                        name = FocusContext.AsString(JObject.Parse(body.Substring(8))["target_username"]);
                    }
                    catch
                    {
                        // Keep the generic line.
                    }

                    SetStatus(Trans.Format("vr.comms.pmSent", name));
                    CicCue.Ok(transform.position);
                }
                else
                {
                    CicCue.Ok(transform.position);
                    Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "sent", 1);
                }

                await PollChat();
                Render();
            }
            finally
            {
                _busy = false;
            }
        }

        // ── Private ───────────────────────────────────────────────────────────────

        void RenderPrivate()
        {
            // Left column: search results when a search is active, else recent conversations.
            var list = _searchResults ?? _conversations;
            if (list == null)
            {
                if (_conversations == null)
                    Run(LoadConversations());
                Line(Trans.Get("Loading"), -380f, 40f, 19f, DiegeticUi.CyanDim, 260f);
            }
            else if (list.Count == 0)
            {
                Line(Trans.Get(_searchResults != null ? "vr.comms.noPlayer" : "vr.comms.noConversation"), -380f, 40f, 18f,
                    DiegeticUi.CyanDim, 280f);
            }
            else
            {
                for (var i = 0; i < list.Count && i < 6; i++)
                {
                    var row = list[i];
                    var id = FocusContext.AsInt(row["contact_id"] ?? row["id"]);
                    var name = FocusContext.AsString(row["contact_username"] ?? row["username"]);
                    var unread = FocusContext.AsInt(row["unread_count"]);
                    var y = 52f - i * 52f;
                    var picked = id == _contactId;
                    var b = DiegeticUi.HoloButton(_body, string.Empty, new Vector2(-400f, y), new Vector2(250f, 46f),
                        () => PickContact(id, name), picked ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                    b.GetComponent<Image>().color = picked ? new Color(0.6f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.3f);
                    Line(Verbatim(name) + (unread > 0 ? "  <color=#ffb347><b>" + unread + "</b></color>" : string.Empty),
                        -400f, y, 18f, UiKit.TextBright, 220f);
                }
            }

            if (_searchResults != null)
                DiegeticUi.HoloButton(_body, Trans.Get("vr.comms.back"), new Vector2(-400f, -262f), new Vector2(250f, 42f),
                    () =>
                    {
                        _searchResults = null;
                        Render();
                    }, DiegeticUi.BtnStyle.Ghost);

            // Right: the open thread.
            if (_contactId <= 0)
            {
                Line(Trans.Get("vr.comms.pickContact"), 150f, 40f, 21f, DiegeticUi.CyanDim, 700f,
                    TextAlignmentOptions.Center);
                return;
            }

            Line("<b>" + Verbatim(_contactName) + "</b>", 175f, 110f, 22f, UiKit.Cyan, 680f);
            if (_thread.Count == 0)
            {
                Line(Trans.Get("vr.comms.noMessage"), 150f, 40f, 19f, DiegeticUi.CyanDim, 720f,
                    TextAlignmentOptions.Center);
                return;
            }

            var first = Mathf.Max(0, _thread.Count - ThreadLines);
            for (var i = first; i < _thread.Count; i++)
            {
                var m = _thread[i];
                var y = 66f - (i - first) * 40f;
                var mine = FocusContext.AsInt(m["sender_id"]) == Me();
                var who = mine ? Trans.Get("vr.comms.you") : Verbatim(FocusContext.AsString(m["sender_username"]));
                Line("<color=#6fa9bd>" + Clock(FocusContext.AsLong(m["date"]), true) + "</color>  <b><color=" +
                     (mine ? "#7dffb0" : "#7fd8ff") + ">" + who + "</color></b>  " +
                     Verbatim(FocusContext.AsString(m["message"])), 150f, y, 18f, UiKit.TextBright, 740f);
            }
        }

        void PickContact(int id, string name)
        {
            if (id <= 0 || id == Me())
                return;
            _contactId = id;
            _contactName = name ?? string.Empty;
            _thread.Clear();
            _threadLast = 0;
            _pollAt = 0f;
            Render();
        }

        async Task LoadConversations()
        {
            var res = await ActionJs.Get("GetPrivateConversations");
            _conversations = ParseArray(res);
            if (_open && _tab == Tab.Private)
                Render();
        }

        async Task Search()
        {
            var q = (_search.text ?? string.Empty).Trim();
            if (q.Length == 0)
            {
                _searchResults = null;
                Render();
                return;
            }

            var res = await ActionJs.Get("SearchPlayers", new Dictionary<string, string> { { "query", q } });
            var rows = ParseArray(res);
            // Not ourselves: the server refuses a thread with oneself.
            var me = Me();
            var list = new JArray();
            foreach (var r in rows)
                if (FocusContext.AsInt(r["id"]) != me)
                    list.Add(r);
            _searchResults = list;
            Render();
        }

        async Task PollThread()
        {
            if (_contactId <= 0)
                return;
            var contact = _contactId;
            var res = await ActionJs.Get("GetPrivateMessages", new Dictionary<string, string>
            {
                { "contact_id", contact.ToString() },
                { "lastid", _threadLast.ToString() }
            });
            if (contact != _contactId)
                return;
            var rows = ParseArray(res);
            var added = false;
            foreach (var r in rows)
            {
                if (r is not JObject o)
                    continue;
                var id = FocusContext.AsInt(o["id"]);
                if (id <= _threadLast)
                    continue;
                _threadLast = id;
                _thread.Add(o);
                added = true;
            }

            // Reading a thread marks it read server-side: refresh the badge and the list.
            if (added)
            {
                _conversations = null;
                if (CommsService.Instance != null)
                    await CommsService.Instance.Refresh();
            }

            if (_open && _tab == Tab.Private && (added || _conversations == null))
                Render();
        }

        // ── Mail ──────────────────────────────────────────────────────────────────

        void RenderMail()
        {
            var views = new[] { ("inbox", MailView.Inbox), ("sent", MailView.Sent), ("compose", MailView.Compose) };
            for (var i = 0; i < views.Length; i++)
            {
                var (key, view) = views[i];
                var on = _mailView == view || (view == MailView.Inbox && _mailView == MailView.Read);
                var b = DiegeticUi.HoloButton(_body, Trans.Get(key), new Vector2(-440f + i * 180f, 128f),
                    new Vector2(170f, 42f), () => SetMailView(view), DiegeticUi.BtnStyle.Ghost);
                SetButtonStyle(b, on);
            }

            switch (_mailView)
            {
                case MailView.Inbox:
                case MailView.Sent:
                    RenderMailList();
                    break;
                case MailView.Read:
                    RenderMailRead();
                    break;
                case MailView.Compose:
                    break;
            }
        }

        void SetMailView(MailView view)
        {
            _mailView = view;
            _page = 0;
            _mails = null;
            _confirmDelete = 0;
            SetStatus(string.Empty);
            Render();
        }

        void RenderMailList()
        {
            var inbox = _mailView == MailView.Inbox;
            if (inbox)
            {
                var keys = new[] { "all", "player", "system", "battle", "diplomacy" };
                for (var i = 0; i < keys.Length; i++)
                {
                    var f = i;
                    var b = DiegeticUi.HoloButton(_body, Trans.Get(keys[i]), new Vector2(118f + i * 92f, 128f),
                        new Vector2(88f, 38f), () =>
                        {
                            _filter = f;
                            _mails = null;
                            _page = 0;
                            Render();
                        }, DiegeticUi.BtnStyle.Ghost);
                    SetButtonStyle(b, _filter == i);
                    var label = b.GetComponentInChildren<TMP_Text>();
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 10f;
                    label.fontSizeMax = 15f;
                }
            }

            if (_mails == null)
            {
                Run(LoadMails());
                Line(Trans.Get("Loading"), 0f, 20f, 21f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            if (_mails.Count == 0)
            {
                Line(Trans.Get(inbox ? "noMails" : "noSentMails"), 0f, 20f, 21f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                return;
            }

            var pages = Mathf.Max(1, Mathf.CeilToInt(_mails.Count / (float)MailsPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var first = _page * MailsPerPage;
            for (var i = first; i < _mails.Count && i < first + MailsPerPage; i++)
            {
                var m = _mails[i];
                var y = 70f - (i - first) * 52f;
                var id = FocusContext.AsInt(m["id"]);
                var unread = inbox && FocusContext.AsInt(m["is_read"]) == 0;
                var row = DiegeticUi.HoloButton(_body, string.Empty, new Vector2(0f, y), new Vector2(1060f, 46f),
                    () => Run(OpenMail(id)), DiegeticUi.BtnStyle.Ghost);
                row.GetComponent<Image>().color = new Color(1f, 1f, 1f, unread ? 0.4f : 0.18f);
                var who = inbox ? Sender(m) : Verbatim(FocusContext.AsString(m["recipient_username"]));
                Line((unread ? "<color=#ffb347>•</color> " : "   ") + "<b>" + Verbatim(FocusContext.AsString(m["subject"])) +
                     "</b>", -250f, y, 19f, unread ? UiKit.TextBright : UiKit.TextDim, 520f);
                Line("<color=#7fd8ff>" + Trans.Get(inbox ? "from" : "to") + "</color> " + who + "   <size=85%>" +
                     TypeLabel(m) + "</size>", 245f, y, 16f, UiKit.TextDim, 360f);
                Line(Clock(FocusContext.AsLong(m["created_at"]), true), 470f, y, 16f, DiegeticUi.CyanDim, 110f,
                    TextAlignmentOptions.MidlineRight);
            }

            if (pages > 1)
            {
                DiegeticUi.HoloButton(_body, "‹", new Vector2(310f, -305f), new Vector2(64f, 44f), () =>
                {
                    _page = (_page - 1 + pages) % pages;
                    Render();
                }, DiegeticUi.BtnStyle.Ghost);
                Line((_page + 1) + " / " + pages, 400f, -305f, 20f, DiegeticUi.CyanDim, 100f,
                    TextAlignmentOptions.Center);
                DiegeticUi.HoloButton(_body, "›", new Vector2(490f, -305f), new Vector2(64f, 44f), () =>
                {
                    _page = (_page + 1) % pages;
                    Render();
                }, DiegeticUi.BtnStyle.Ghost);
            }
        }

        /// <summary>Server writes "SYSTÈME" for sender 0: the VR shows its own localized label.</summary>
        static string Sender(JToken m) =>
            FocusContext.AsInt(m["sender_id"]) == 0
                ? Trans.Get("system")
                : Verbatim(FocusContext.AsString(m["sender_username"]));

        static string TypeLabel(JToken m)
        {
            var type = FocusContext.AsString(m["type"]);
            return string.IsNullOrEmpty(type) ? string.Empty : Trans.Get(type);
        }

        void RenderMailRead()
        {
            if (_mail == null)
            {
                Line(Trans.Get("Loading"), 0f, 20f, 21f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            var m = _mail;
            var incoming = FocusContext.AsInt(m["recipient_id"]) == Me();
            Line("<b>" + Verbatim(FocusContext.AsString(m["subject"])) + "</b>", -60f, 80f, 23f, UiKit.TextBright, 900f);
            Line("<color=#7fd8ff>" + Trans.Get("from") + "</color> " + Sender(m) + "     <color=#7fd8ff>" +
                 Trans.Get("to") + "</color> " + Verbatim(FocusContext.AsString(m["recipient_username"])) + "     " +
                 Clock(FocusContext.AsLong(m["created_at"]), true) + "     " + TypeLabel(m), -60f, 44f, 16f,
                UiKit.TextDim, 900f);

            var body = DiegeticUi.HoloLabel(_body, FocusContext.AsString(m["content"]), new Vector2(-60f, -95f),
                new Vector2(900f, 240f), 18f, new Color(0.85f, 0.94f, 0.98f, 1f), TextAlignmentOptions.TopLeft);
            body.richText = false;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Ellipsis;
            body.enableAutoSizing = true;
            body.fontSizeMin = 13f;
            body.fontSizeMax = 18f;

            var id = FocusContext.AsInt(m["id"]);
            // Reply only to a player (sender 0 is the game).
            if (incoming && FocusContext.AsInt(m["sender_id"]) > 0)
                DiegeticUi.HoloButton(_body, Trans.Get("reply"), new Vector2(440f, 60f), new Vector2(170f, 46f),
                    () => Reply(m), DiegeticUi.BtnStyle.Cyan);
            var confirming = _confirmDelete == id && Time.unscaledTime < _confirmUntil;
            DiegeticUi.HoloButton(_body, Trans.Get(confirming ? "validate" : "delete"), new Vector2(440f, 0f),
                new Vector2(170f, 46f), () => Run(Delete(id)),
                confirming ? DiegeticUi.BtnStyle.Danger : DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(_body, Trans.Get("vr.comms.back"), new Vector2(440f, -60f), new Vector2(170f, 46f),
                () => SetMailView(_mail != null && FocusContext.AsInt(_mail["sender_id"]) == Me() && !incoming
                    ? MailView.Sent
                    : MailView.Inbox), DiegeticUi.BtnStyle.Ghost);
        }

        void Reply(JObject m)
        {
            var subject = FocusContext.AsString(m["subject"]);
            // Web MailWindowUI.onReplyMail: "Re: " once.
            _to.text = FocusContext.AsString(m["sender_username"]);
            _subject.text = subject.StartsWith("Re:", StringComparison.Ordinal) ? subject : "Re: " + subject;
            _content.text = string.Empty;
            SetMailView(MailView.Compose);
        }

        async Task LoadMails()
        {
            var args = new Dictionary<string, string> { { "folder", _mailView == MailView.Sent ? "sent" : "inbox" } };
            if (_mailView == MailView.Inbox && _filter > 0)
                args["filter"] = Filters[_filter];
            var view = _mailView;
            var res = await ActionJs.Get("GetMails", args);
            if (view != _mailView)
                return;
            _mails = ParseArray(res);
            if (!res.Ok)
                SetStatus(res.Error, true);
            if (_open && _tab == Tab.Mail)
                Render();
        }

        async Task OpenMail(int id)
        {
            _mail = null;
            _mailView = MailView.Read;
            _confirmDelete = 0;
            Render();
            var res = await ActionJs.Get("GetMail", new Dictionary<string, string> { { "id", id.ToString() } });
            if (!res.Ok)
            {
                Feedback(res, null);
                SetMailView(MailView.Inbox);
                return;
            }

            try
            {
                _mail = JObject.Parse(res.Body);
            }
            catch
            {
                SetMailView(MailView.Inbox);
                return;
            }

            Render();
            if (CommsService.Instance != null)
                await CommsService.Instance.Refresh();
        }

        async Task Delete(int id)
        {
            if (_confirmDelete != id || Time.unscaledTime >= _confirmUntil)
            {
                _confirmDelete = id;
                _confirmUntil = Time.unscaledTime + 4f;
                SetStatus(Trans.Get("vr.comms.confirmDelete"), true);
                Render();
                return;
            }

            _confirmDelete = 0;
            var res = await ActionJs.Get("DeleteMail", new Dictionary<string, string> { { "id", id.ToString() } });
            Feedback(res, "vr.comms.mailDeleted");
            if (res.Ok)
                SetMailView(MailView.Inbox);
        }

        async Task SendMail()
        {
            if (_busy)
                return;
            var to = (_to.text ?? string.Empty).Trim();
            var subject = (_subject.text ?? string.Empty).Trim();
            var content = (_content.text ?? string.Empty).Trim();
            if (to.Length == 0 || subject.Length == 0 || content.Length == 0)
            {
                SetStatus(Trans.Get("vr.comms.fillAll"), true);
                CicCue.Fail(transform.position);
                return;
            }

            _busy = true;
            try
            {
                var res = await ActionJs.Get("SendMail", new Dictionary<string, string>
                {
                    { "recipient", to },
                    { "subject", subject },
                    { "content", content }
                });
                Feedback(res, "vr.comms.mailSent");
                if (!res.Ok)
                    return;
                _to.text = string.Empty;
                _subject.text = string.Empty;
                _content.text = string.Empty;
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "sent", 1);
                SetMailView(MailView.Sent);
            }
            finally
            {
                _busy = false;
            }
        }

        // ── Shared ────────────────────────────────────────────────────────────────

        static JArray ParseArray(ApiResult res)
        {
            if (!res.Ok || string.IsNullOrEmpty(res.Body))
                return new JArray();
            try
            {
                return JToken.Parse(res.Body) as JArray ?? new JArray();
            }
            catch
            {
                return new JArray();
            }
        }

        void Feedback(ApiResult res, string okKey)
        {
            if (res.Ok)
            {
                CicCue.Ok(transform.position);
                if (okKey != null)
                    SetStatus(Trans.Get(okKey));
                return;
            }

            CicCue.Fail(transform.position);
            // Several handlers answer French text after error: (web bug in PARITY.md); shown as the server sent it.
            SetStatus(string.IsNullOrEmpty(res.Error) ? Trans.Get("vr.common.error") : res.Error, true);
            Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "fail", 2);
        }

        void Update()
        {
            if (!_open)
                return;
            if (_confirmDelete != 0 && Time.unscaledTime >= _confirmUntil)
            {
                _confirmDelete = 0;
                SetStatus(string.Empty);
                Render();
            }

            if (_polling || _busy || Time.unscaledTime < _pollAt)
                return;
            _pollAt = Time.unscaledTime + ChatEvery;
            // Live feeds only for what is on screen (plan: GetChat 3 s, only while Comms is open).
            if (_tab == Tab.Channel)
                Run(Poll(PollChat()));
            else if (_tab == Tab.Private && _contactId > 0)
                Run(Poll(PollThread()));
        }

        async Task Poll(Task inner)
        {
            _polling = true;
            try
            {
                await inner;
            }
            finally
            {
                _polling = false;
            }
        }
    }
}
