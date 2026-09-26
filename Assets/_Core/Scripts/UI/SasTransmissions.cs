using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Stations;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// The airlock's transmissions board (web scenes/ui.js loadOverlay → AnnouncementsModalUI): after login,
    /// GetGameAnnouncements gives the live seasonal events (with the world boss) and the latest news / patch
    /// notes. A lectern beside the login terminal, turned to the captain: tabs Events / News, the list on the
    /// left, the reader on the right (picture from the server, body converted from its HTML, paged). Unread
    /// news (latest_id vs the last one read on this headset) opens the board on it with an amber header, like
    /// the web opening its overlay once per new item. Titles and bodies follow the player's language
    /// (<c>_en</c> fields).
    /// </summary>
    public sealed class SasTransmissions : MonoBehaviour
    {
        const string ReadKey = "su.news.lastRead";
        const string Site = "https://www.stellar-universe.com";
        static readonly Vector2 Size = new(1.1f, 0.66f);
        const int MaxItems = 6;

        enum Tab
        {
            Events,
            News
        }

        HoloScreen _screen;
        Button[] _tabs;
        RectTransform _body;
        JArray _events = new();
        JArray _news = new();
        int _latestId;
        Tab _tab;
        int _selected;
        int _page = 1;
        TMP_Text _reader;
        TMP_Text _pageLabel;
        bool _loaded;
        readonly Dictionary<string, Texture2D> _images = new();
        string _wantImage;
        RawImage _picture;

        public static SasTransmissions Build(Transform room, CicArtKit art)
        {
            // Left of the terminal, a step forward, turned to the captain's standing spot.
            var mount = SasTerminal.Lectern(room, art, new Vector3(-1.3f, 0f, 0.72f), 0f, 1.42f);
            var board = mount.gameObject.AddComponent<SasTransmissions>();
            board._screen = HoloScreen.Create(mount, "TransmissionsBoard", Size, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.sas.transmissions"));
            board._screen.SetAccent(UiKit.Cyan, 0.45f);
            var eye = room.TransformPoint(new Vector3(0f, WorldScale.EyeStanding, 0f));
            ScreenMount.FaceViewer(mount.parent, new Vector3(eye.x, mount.parent.position.y, eye.z), 0f);
            ScreenMount.FaceViewer(board._screen.transform, eye, 1f, 10f);
            board._tabs = ScreenKit.Tabs(board._screen.Content, new[] { "vr.sas.events", "news" }, 232f,
                i => board.SetTab((Tab)i));
            board._body = ScreenKit.Body(board._screen.Content);
            board._screen.gameObject.SetActive(false);
            return board;
        }

        /// <summary>Signed in: read the announcements and light the board.</summary>
        public void Show() => AsyncTap.Run(Load());

        public void Hide()
        {
            if (_screen != null)
                _screen.gameObject.SetActive(false);
        }

        async Task Load()
        {
            _screen.gameObject.SetActive(true);
            if (!_loaded)
            {
                ScreenKit.Clear(_body);
                ScreenKit.Line(_body, Trans.Get("Loading"), 0f, 0f, 22f, UiKit.TextDim, 900f, TextAlignmentOptions.Center);
            }

            var r = await ActionJs.Get("GetGameAnnouncements");
            var root = ScreenKit.Object(r);
            if (this == null)
                return;
            if (root == null)
            {
                ScreenKit.Clear(_body);
                ScreenKit.Line(_body, r.Ok ? Trans.Get("noNewsYet") : r.Error, 0f, 0f, 22f, UiKit.TextDim, 900f,
                    TextAlignmentOptions.Center);
                return;
            }

            _events = root["events"] as JArray ?? new JArray();
            _news = root["news"] as JArray ?? new JArray();
            _latestId = FocusContext.AsInt(root["latest_id"]);
            _loaded = true;

            var unread = _latestId > 0 && PlayerPrefs.GetInt(ReadKey, 0) != _latestId;
            _screen.SetAccent(unread ? UiKit.Amber : UiKit.Cyan, unread ? 0.9f : 0.45f);
            _screen.SetHeader(Trans.Get(unread ? "vr.sas.newTransmission" : "vr.sas.transmissions"));
            if (unread)
                CicCue.RadioOpen(_screen.transform.position);
            _selected = 0;
            SetTab(unread || _events.Count == 0 ? Tab.News : Tab.Events);
        }

        void SetTab(Tab tab)
        {
            _tab = tab;
            _selected = Mathf.Clamp(_selected, 0, Math.Max(0, Items.Count - 1));
            _page = 1;
            ScreenKit.LightTabs(_tabs, (int)tab);
            Render();
        }

        JArray Items => _tab == Tab.Events ? _events : _news;

        void Select(int i)
        {
            _selected = i;
            _page = 1;
            Render();
        }

        // ── Render ────────────────────────────────────────────────────────────────

        void Render()
        {
            ScreenKit.Clear(_body);
            _picture = null;
            var items = Items;
            if (items.Count == 0)
            {
                ScreenKit.Line(_body, Trans.Get(_tab == Tab.Events ? "vr.quarters.noEvent" : "noNewsYet"), 0f, 0f, 22f,
                    UiKit.TextDim, 900f, TextAlignmentOptions.Center);
                return;
            }

            // List: newest first (server order), the open item lit.
            for (var i = 0; i < items.Count && i < MaxItems; i++)
            {
                var index = i;
                var it = items[i];
                var title = ScreenKit.Verbatim(HtmlText.StripGlyphs(ScreenKit.Localized(it, "title")));
                var when = _tab == Tab.News ? Date(it["date"]) : Remaining(it["end_at"]);
                ScreenKit.Btn(_body, title + "\n<size=70%><color=#7fb7c4>" + when + "</color></size>", -365f, 180f - i * 64f,
                    320f, 58f, () => Select(index), i == _selected ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            }

            var item = items[_selected];
            if (_tab == Tab.News)
                RenderNews(item);
            else
                RenderEvent(item);

            if (_tab == Tab.News && FocusContext.AsInt(item["id"]) == _latestId && _latestId > 0)
                MarkRead();
        }

        void RenderNews(JToken n)
        {
            ScreenKit.Line(_body, "<b>" + ScreenKit.Verbatim(HtmlText.StripGlyphs(ScreenKit.Localized(n, "title"))) + "</b>", 175f, 190f, 26f,
                UiKit.TextBright, 680f);
            var version = FocusContext.AsString(n["version"]);
            ScreenKit.Line(_body, Date(n["date"]) + (version.Length > 0 ? "  ·  v" + ScreenKit.Verbatim(version) : string.Empty),
                175f, 158f, 17f, UiKit.TextDim, 680f);
            var html = ScreenKit.Localized(n, "body");
            var image = FocusContext.AsString(n["image"]);
            if (image.Length == 0)
                image = HtmlText.FirstImage(html) ?? string.Empty;
            Reader(HtmlText.ToTmp(html), image, -178f);
        }

        void RenderEvent(JToken e)
        {
            ScreenKit.Line(_body, "<b>" + ScreenKit.Verbatim(HtmlText.StripGlyphs(ScreenKit.Localized(e, "title"))) + "</b>", 175f, 190f, 26f,
                UiKit.TextBright, 680f);
            var faction = FocusContext.AsString(e["enemy_faction_name"]);
            var color = FocusContext.AsString(e["enemy_faction_color"]);
            var line = Remaining(e["end_at"]);
            if (faction.Length > 0)
                line += "  ·  <color=" + (ColorUtility.TryParseHtmlString(color, out var c) ? ScreenKit.Hex(c) : "#ff8a70") + ">" +
                        ScreenKit.Verbatim(faction) + "</color>";
            ScreenKit.Line(_body, line, 175f, 158f, 17f, UiKit.TextDim, 680f);

            var text = HtmlText.ToTmp(ScreenKit.Localized(e, "description"));
            if (e["world_boss"] is JObject boss)
            {
                var hp = Mathf.Clamp01(FocusContext.AsFloat(boss["hp_percent"]) / 100f);
                var name = ScreenKit.Verbatim(ScreenKit.Localized(boss, "name"));
                ScreenKit.Gauge(_body, 175f, -160f, 680f, hp, new Color(1f, 0.35f, 0.3f, 0.9f),
                    name + "  ·  " + ScreenKit.Verbatim(FocusContext.AsString(boss["current_hp_fmt"])) + " / " +
                    ScreenKit.Verbatim(FocusContext.AsString(boss["max_hp_fmt"])), 26f);
                text += "\n\n" + Trans.Format("vr.quarters.bossPool", ScreenKit.Num(FocusContext.AsFloat(boss["reward_nova_pool"])),
                    ScreenKit.Num(FocusContext.AsFloat(boss["reward_xp_pool"])));
            }

            Reader(text, FocusContext.AsString(e["image"]), e["world_boss"] is JObject ? -140f : -178f);
        }

        /// <summary>Picture band on top (when the server gives one), the body under it, paged.</summary>
        void Reader(string text, string imageUrl, float bottom)
        {
            var top = 136f;
            if (!string.IsNullOrEmpty(imageUrl))
            {
                var go = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(_body, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(175f, 80f);
                rt.sizeDelta = new Vector2(680f, 104f);
                _picture = go.GetComponent<RawImage>();
                _picture.raycastTarget = false;
                _picture.color = new Color(1f, 1f, 1f, 0f);
                ShowImage(Absolute(imageUrl));
                top = 22f;
            }

            _reader = ScreenKit.Para(_body, text, 175f, (top + bottom) * 0.5f, 18f, UiKit.TextBright, 680f, top - bottom);
            _reader.overflowMode = TextOverflowModes.Page;
            _reader.pageToDisplay = _page;
            _reader.ForceMeshUpdate();
            var pages = Mathf.Max(1, _reader.textInfo.pageCount);
            if (pages <= 1)
                return;
            ScreenKit.Btn(_body, Trans.Get("previous"), 20f, -202f, 150f, 34f, () => Turn(-1), DiegeticUi.BtnStyle.Ghost,
                _page > 1);
            _pageLabel = ScreenKit.Line(_body, _page + " / " + pages, 175f, -202f, 17f, UiKit.TextDim, 120f,
                TextAlignmentOptions.Center);
            ScreenKit.Btn(_body, Trans.Get("next"), 330f, -202f, 150f, 34f, () => Turn(1), DiegeticUi.BtnStyle.Ghost,
                _page < pages);
        }

        void Turn(int d)
        {
            _page = Mathf.Max(1, _page + d);
            Render();
        }

        void MarkRead()
        {
            if (PlayerPrefs.GetInt(ReadKey, 0) == _latestId)
                return;
            PlayerPrefs.SetInt(ReadKey, _latestId);
            PlayerPrefs.Save();
            _screen.SetAccent(UiKit.Cyan, 0.45f);
            _screen.SetHeader(Trans.Get("vr.sas.transmissions"));
        }

        // ── Pictures ──────────────────────────────────────────────────────────────

        static string Absolute(string url) =>
            url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : Site + (url.StartsWith("/") ? url : "/" + url);

        void ShowImage(string url)
        {
            _wantImage = url;
            if (_images.TryGetValue(url, out var tex))
            {
                Apply(tex);
                return;
            }

            AsyncTap.Run(Fetch(url));
        }

        async Task Fetch(string url)
        {
            using var req = UnityWebRequestTexture.GetTexture(url, true);
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();
            if (this == null || req.result != UnityWebRequest.Result.Success)
                return;
            var tex = DownloadHandlerTexture.GetContent(req);
            if (tex == null)
                return;
            tex.wrapMode = TextureWrapMode.Clamp;
            // A handful of pictures at most; the oldest goes.
            if (_images.Count >= 6)
                foreach (var k in new List<string>(_images.Keys))
                {
                    Destroy(_images[k]);
                    _images.Remove(k);
                    break;
                }

            _images[url] = tex;
            if (_wantImage == url)
                Apply(tex);
        }

        /// <summary>Cover the band, cropping the picture's excess height (no stretch).</summary>
        void Apply(Texture2D tex)
        {
            if (_picture == null || tex == null)
                return;
            _picture.texture = tex;
            _picture.color = Color.white;
            var band = _picture.rectTransform.sizeDelta;
            var texAspect = tex.width / (float)Mathf.Max(1, tex.height);
            var bandAspect = band.x / band.y;
            _picture.uvRect = texAspect > bandAspect
                ? new Rect((1f - bandAspect / texAspect) * 0.5f, 0f, bandAspect / texAspect, 1f)
                : new Rect(0f, (1f - texAspect / bandAspect) * 0.5f, 1f, texAspect / bandAspect);
        }

        void OnDestroy()
        {
            foreach (var t in _images.Values)
                if (t != null)
                    Destroy(t);
            _images.Clear();
        }

        // ── Text helpers ──────────────────────────────────────────────────────────

        static string Date(JToken t)
        {
            var s = FocusContext.AsString(t);
            return DateTime.TryParse(s, out var d) ? d.ToString("dd/MM/yyyy") : ScreenKit.Verbatim(s);
        }

        static string Remaining(JToken endAt)
        {
            var s = FocusContext.AsString(endAt);
            if (!DateTime.TryParse(s, out var end))
                return string.Empty;
            var left = (long)(end - DateTime.Now).TotalSeconds;
            return Trans.Format("vr.quarters.endsIn", ScreenKit.Remaining(left));
        }
    }
}
