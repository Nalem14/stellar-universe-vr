using Core.App;
using Core.Utils;
using Core.Stations;
using Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// The correspondent on the main screen, Bridge Crew style: an incoming transmission (a new message or
    /// mail, <see cref="CommsService.Incoming"/>), the private channel open on the Comms console, or the Comms
    /// officer's "on screen" puts the sender's empire up — flag, empire, leader, their last words. Drawn in the
    /// HUD over the hull feed (same render texture, no extra camera). System mail shows the command emblem.
    /// </summary>
    public sealed partial class BridgeViewscreen
    {
        const float TransmissionHold = 12f;

        Image _commsPanel;
        RawImage _commsFlag;
        RawImage _portrait;
        Material _portraitMat;
        Image _commsFlagRim;
        TMP_Text _commsHeader;
        TMP_Text _commsEmpire;
        TMP_Text _commsLeader;
        TMP_Text _commsQuote;
        Texture2D _flagTex;
        string _flagSig;

        Correspondent _commsWho;
        float _commsUntil;
        /// <summary>Asked for (open channel, Comms officer): holds over the table like any captain's request.</summary>
        bool _commsRequested;

        void BindComms()
        {
            _commsPanel = NewBar(_hud, "CommsPanel", new Vector2(-150f, -6f), new Vector2(810f, 286f), new Color(0.01f, 0.025f, 0.04f, 0.9f));
            var p = _commsPanel.transform;
            NewBar(p, "Rule", new Vector2(0f, 104f), new Vector2(780f, 2f), new Color(1f, 1f, 1f, 0.25f));
            _commsFlagRim = NewBar(p, "FlagRim", new Vector2(-250f, -22f), new Vector2(262f, 176f), Color.white);
            // The correspondent in person: a hologram of head and shoulders (SU/HoloPortrait), their flag as a
            // badge in the corner.
            var backGo = new GameObject("PortraitBack", typeof(RectTransform), typeof(Image));
            backGo.transform.SetParent(_commsFlagRim.transform, false);
            backGo.GetComponent<RectTransform>().sizeDelta = new Vector2(252f, 168f);
            var back = backGo.GetComponent<Image>();
            back.color = new Color(0.01f, 0.03f, 0.05f, 1f);
            back.raycastTarget = false;
            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
            portraitGo.transform.SetParent(_commsFlagRim.transform, false);
            portraitGo.GetComponent<RectTransform>().sizeDelta = new Vector2(252f, 168f);
            _portrait = portraitGo.GetComponent<RawImage>();
            _portrait.raycastTarget = false;
            var shader = Shader.Find("SU/HoloPortrait");
            if (shader != null)
            {
                _portraitMat = new Material(shader) { name = "SU_HoloPortrait" };
                _portrait.material = _portraitMat;
            }
            else
            {
                portraitGo.SetActive(false);
            }

            var flagGo = new GameObject("Flag", typeof(RectTransform), typeof(RawImage));
            flagGo.transform.SetParent(_commsFlagRim.transform, false);
            var flagRt = flagGo.GetComponent<RectTransform>();
            flagRt.sizeDelta = new Vector2(78f, 52f);
            flagRt.anchoredPosition = new Vector2(126f - 39f - 6f, -84f + 26f + 6f);
            _commsFlag = flagGo.GetComponent<RawImage>();
            _commsFlag.raycastTarget = false;
            _commsHeader = Text(p, new Vector2(-10f, 124f), new Vector2(760f, 30f), 20f, TextAlignmentOptions.Left, true);
            _commsEmpire = Text(p, new Vector2(140f, 50f), new Vector2(500f, 44f), 34f, TextAlignmentOptions.Left, true);
            _commsLeader = Text(p, new Vector2(140f, 8f), new Vector2(500f, 30f), 21f, TextAlignmentOptions.Left, false);
            _commsQuote = Text(p, new Vector2(140f, -70f), new Vector2(500f, 110f), 21f, TextAlignmentOptions.TopLeft, false);
            _commsQuote.fontStyle = FontStyles.Italic;
            SetLayer(p, ViewscreenCamera.HudLayer);
            _commsPanel.gameObject.SetActive(false);

            if (CommsService.Instance != null)
                CommsService.Instance.Incoming += OnIncoming;
            CommsConsole.ChannelChanged += OnChannel;
        }

        void UnbindComms()
        {
            if (CommsService.Instance != null)
                CommsService.Instance.Incoming -= OnIncoming;
            CommsConsole.ChannelChanged -= OnChannel;
            if (_flagTex != null)
                Destroy(_flagTex);
        }

        void OnIncoming(Correspondent who)
        {
            // An open channel with someone else keeps the screen; a new caller never cuts a captain's request.
            if (_commsRequested && _commsWho != null && _commsWho.UserId != who.UserId)
                return;
            _commsWho = who;
            _commsUntil = Time.unscaledTime + TransmissionHold;
            _nextEval = 0f;
        }

        void OnChannel(int userId, string name)
        {
            if (userId > 0)
            {
                _commsWho = new Correspondent { UserId = userId, Username = name ?? string.Empty, At = Time.unscaledTime };
                _commsRequested = true;
                _commsUntil = float.MaxValue;
                Core.Utils.AsyncTap.Run(LoadAndRefresh(userId));
            }
            else if (_commsRequested)
            {
                // Channel closed: the face lingers a moment, then the screen goes back to the scene.
                _commsRequested = false;
                _commsUntil = Time.unscaledTime + 4f;
            }

            _nextEval = 0f;
        }

        async System.Threading.Tasks.Task LoadAndRefresh(int userId)
        {
            await Correspondent.LoadEmpire(userId);
            _nextEval = 0f;
        }

        /// <summary>The Comms officer's "on screen": the open channel, else the last one who wrote.</summary>
        int CommsOnScreen()
        {
            var console = CommsConsole.Instance;
            if (console != null && console.OpenContact > 0)
            {
                OnChannel(console.OpenContact, console.OpenContactName);
                return 1;
            }

            var last = CommsService.Instance?.Last;
            if (last == null)
                return -1;
            _commsWho = last;
            _commsRequested = true;
            _commsUntil = Time.unscaledTime + CrewHold;
            _nextEval = 0f;
            return 1;
        }

        /// <summary>Is a correspondent on screen this evaluation (intent = the table already holds the picture)?</summary>
        bool CommsActive(bool intent)
        {
            if (_commsWho == null || Time.unscaledTime >= _commsUntil)
            {
                if (_commsRequested && Time.unscaledTime >= _commsUntil)
                    _commsRequested = false;
                return false;
            }

            return _commsRequested || !intent;
        }

        void ShowComms(bool on, System.Text.StringBuilder body)
        {
            if (_commsPanel.gameObject.activeSelf != on)
                _commsPanel.gameObject.SetActive(on);
            if (!on)
                return;

            var who = _commsWho;
            var empire = Correspondent.EmpireOf(who.UserId);
            var console = CommsConsole.Instance;
            var channel = console != null && console.OpenContact == who.UserId && who.UserId > 0;
            var blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f ? "●" : "○";
            Set(_commsHeader, "<color=#ffb347>" + blink + "</color>  " +
                              Trans.Get(channel ? "vr.screen.channel" : "vr.screen.transmission"));

            string empireName, leader;
            if (who.IsSystem)
            {
                empireName = Trans.Get("system");
                leader = string.Empty;
            }
            else
            {
                empireName = empire != null ? FocusContext.AsString(empire["name"]) : who.Username;
                var title = empire != null ? FocusContext.AsString(empire["leaderTitle"]) : string.Empty;
                var name = empire != null ? FocusContext.AsString(empire["leaderName"]) : string.Empty;
                var head = (title + " " + name).Trim();
                leader = string.IsNullOrEmpty(head)
                    ? Plain(who.Username)
                    : Plain(head) + (string.IsNullOrEmpty(who.Username) ? string.Empty : "   <color=#6fa9bd>@" + Plain(who.Username) + "</color>");
            }

            Set(_commsEmpire, Plain(string.IsNullOrEmpty(empireName) ? "#" + who.UserId : empireName));
            Set(_commsLeader, leader);
            var quote = channel ? console.LastFrom(who.UserId) : who.Text;
            Set(_commsQuote, string.IsNullOrEmpty(quote) ? string.Empty : "« " + Plain(quote) + " »");

            var stance = who.IsSystem ? EmpireStance.Neutral : DiplomacyIndex.Resolve(who.UserId);
            var tint = stance switch
            {
                EmpireStance.Ally => new Color(0.45f, 1f, 0.6f, 1f),
                EmpireStance.Enemy => Red,
                _ => CicArtKit.Cyan
            };
            _commsFlagRim.color = tint * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f));
            _commsEmpire.color = Color.Lerp(tint, Color.white, 0.35f);
            PaintFlag(who.IsSystem ? SystemEmblem : FocusContext.AsString(empire?["flag"]));
            Portrait(who, empire, tint);

            // The card: who they are to us.
            if (!who.IsSystem)
            {
                body.AppendLine(Owner(who.UserId));
                if (empire != null)
                {
                    var planets = FocusContext.AsInt(empire["planets"]);
                    var level = FocusContext.AsInt(empire["level"]);
                    if (level > 0)
                        body.AppendLine(Trans.Get("level") + " " + level);
                    if (planets > 0)
                        body.AppendLine(planets + " " + Trans.Get("planets"));
                }
            }

            if (who.IsMail)
                body.AppendLine(Trans.Get("inbox"));
        }

        static readonly int VariantId = Shader.PropertyToID("_Variant");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int TalkId = Shader.PropertyToID("_Talk");

        /// <summary>Their face: the silhouette of their species, proportions of their own, the mouth moving while
        /// their words are fresh; system mail shows the command emblem.</summary>
        void Portrait(Correspondent who, Newtonsoft.Json.Linq.JObject empire, Color tint)
        {
            if (_portraitMat == null)
                return;
            _portrait.color = Color.Lerp(tint, Color.white, 0.15f);
            // AI and legacy empires may have no species row (specy = null): the humanoid default then.
            var species = empire?["specy"] is Newtonsoft.Json.Linq.JObject sp ? FocusContext.AsInt(sp["type_id"]) : 0;
            _portraitMat.SetFloat(VariantId, who.IsSystem ? 8f : SpeciesSilhouette(species));
            _portraitMat.SetFloat(SeedId, Mathf.Repeat(who.UserId * 0.6180339f, 1f));
            var age = Time.unscaledTime - who.At;
            _portraitMat.SetFloat(TalkId, Mathf.Clamp01(1f - (age - 3f) / 2f));
        }

        /// <summary>species_types.id → silhouette family (SU/HoloPortrait _Variant).</summary>
        static float SpeciesSilhouette(int typeId) => typeId switch
        {
            2 => 1f,
            3 or 8 => 2f,
            5 or 13 => 3f,
            6 => 4f,
            7 or 14 => 5f,
            9 or 10 => 6f,
            11 => 7f,
            _ => 0f
        };

        /// <summary>Command emblem for the game's own mail: a gold star in a cyan ring.</summary>
        const string SystemEmblem = "{\"bg\":\"#04121c\",\"shapes\":[{\"shape\":\"ring\",\"color\":\"#39d7ff\"},{\"shape\":\"star\",\"color\":\"#ffb347\"},{\"shape\":\"none\",\"color\":\"#ffffff\"}]}";

        void PaintFlag(string json)
        {
            json ??= string.Empty;
            if (json == _flagSig && _flagTex != null)
                return;
            _flagSig = json;
            _flagTex = FlagPainter.Paint(FlagSpec.FromJson(json), _flagTex);
            _commsFlag.texture = _flagTex;
        }
    }
}
