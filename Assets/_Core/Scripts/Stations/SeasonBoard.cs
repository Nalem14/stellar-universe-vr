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
    /// The season board over the captain's desk — web ProgressionWindowUI "Saison & Suprématie" tab and the
    /// news of the announcements hub. Season: name, time left, cycle gauge, our tier / rank / points and their
    /// breakdown, the next tier to reach, what earns supremacy points (the season's objectives) and the tier
    /// ladder with its Nova bonuses (GetActiveSeason). Leaderboard and alliances (GetSeasonLeaderboard), the
    /// pantheon of closed seasons (GetSeasonPantheon), our honours with EquipEmpireTitle and the end-of-season
    /// rewards (GetEmpireAccolades), and the latest news (the airlock's reader, <see cref="SasTransmissions.Embed"/>).
    /// Read when the captain walks in, then every two minutes while he stays — never per frame.
    /// </summary>
    public sealed class SeasonBoard : MonoBehaviour
    {
        enum Tab { Season, Leaderboard, Alliances, Pantheon, Honours, News }

        const float RefreshEvery = 120f;
        const int RowsPerPage = 7;
        const int AccoladesPerPage = 4;
        static readonly Vector2 ScreenSize = new(1.1f, 0.7f);
        // Read from a step back (over the desk): a little larger than the desk consoles.
        const float ScreenScale = 1.15f;
        static readonly Color Gold = new(1f, 0.84f, 0.3f, 1f);
        static readonly string[] PodiumHex = { "#ffd700", "#cbd5e1", "#cd7f32" };

        /// <summary>Fallback ladder until GetActiveSeason sends tiers / scoring / end_rewards.</summary>
        static readonly (string Key, int MinScore, int Nova, string Hex)[] DefaultTiers =
        {
            ("grandmaster", 20000, 500, "#ffd700"),
            ("master", 12000, 300, "#a855f7"),
            ("diamond", 7000, 150, "#38bdf8"),
            ("platinum", 3500, 75, "#2dd4bf"),
            ("gold", 1500, 40, "#f59e0b"),
            ("silver", 500, 20, "#94a3b8"),
            ("bronze", 0, 0, "#cd7f32")
        };

        /// <summary>Supremacy points per deed (AddEmpireSupremacyScore call sites: battle, stargate, anomaly, bounty, objectives).</summary>
        static readonly (string Key, int Points)[] DefaultScoring =
        {
            ("battleWon", 30), ("planetConquered", 100), ("defenseHeld", 60), ("stargateCapture", 50),
            ("stargateColony", 40), ("anomaly", 15), ("bounty", 25), ("daily", 10), ("weekly", 30), ("monthly", 100)
        };

        /// <summary>End-of-season rewards by final rank (RunSeasonCheckCycle), with the title each one carries.</summary>
        static readonly (string Key, int Nova, int Credits)[] DefaultPodium =
        {
            ("first", 1000, 3000000), ("second", 600, 1500000), ("third", 400, 800000),
            ("top10", 200, 400000), ("top20", 100, 200000)
        };

        HoloScreen _screen;
        RectTransform _body;
        TMP_Text _status;
        Button[] _tabs;
        TextMeshPro _header;
        SasTransmissions _news;
        Action _titleChanged;

        Tab _tab;
        int _page;
        bool _busy;
        bool _loading;
        float _refreshAt;
        float _loadedAt;

        (string Key, int MinScore, int Nova, string Hex)[] Tiers = DefaultTiers;
        (string Key, int Points)[] Scoring = DefaultScoring;
        (string Key, int Nova, int Credits)[] Podium = DefaultPodium;

        JObject _season;
        JObject _stats;
        JArray _empires;
        JArray _alliances;
        JArray _pantheon;
        JArray _accolades;

        /// <summary>The board on its wall mount; <paramref name="titleChanged"/> runs after a title is equipped.</summary>
        public static SeasonBoard Build(Transform mount, TextMeshPro header, Action titleChanged)
        {
            var screen = HoloScreen.Create(mount, "SeasonConsole", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("seasonSupremacy"));
            screen.transform.localScale = Vector3.one * ScreenScale;
            screen.SetAccent(Gold, 0.55f);
            var board = screen.gameObject.AddComponent<SeasonBoard>();
            board._screen = screen;
            board._header = header;
            board._titleChanged = titleChanged;
            board._tabs = ScreenKit.Tabs(screen.Content,
                new[] { "season", "seasonLeaderboard", "seasonAlliances", "seasonPantheon", "seasonAccolades", "news" }, 245f,
                i => board.SetTab((Tab)i));
            board._body = ScreenKit.Body(screen.Content);
            board._news = SasTransmissions.Embed(screen);
            board._status = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);
            board.Render();
            return board;
        }

        // ── Data ──────────────────────────────────────────────────────────────────

        /// <summary>Walked in: the whole board, and the news once.</summary>
        public async Task Open()
        {
            _news.Show();
            await Load(true);
        }

        /// <summary>Slow cadence while the captain is in the quarters (from QuartersRoom.Update).</summary>
        public void Tick()
        {
            if (_loading || _busy || Time.unscaledTime < _refreshAt)
                return;
            AsyncTap.Run(Load(false));
        }

        void ApplyCatalog(JObject payload)
        {
            var tiers = payload["tiers"] as JArray;
            if (tiers != null && tiers.Count > 0)
            {
                var list = new List<(string Key, int MinScore, int Nova, string Hex)>();
                foreach (var row in tiers)
                    list.Add((FocusContext.AsString(row["key"]), FocusContext.AsInt(row["min_score"]),
                        FocusContext.AsInt(row["nova_reward"]), FocusContext.AsString(row["color"])));
                Tiers = list.ToArray();
            }

            var scoring = payload["scoring"] as JArray;
            if (scoring != null && scoring.Count > 0)
            {
                var list = new List<(string Key, int Points)>();
                foreach (var row in scoring)
                {
                    var id = FocusContext.AsString(row["id"]);
                    list.Add((ScoreLabel(id), FocusContext.AsInt(row["points"])));
                }
                Scoring = list.ToArray();
            }

            var rewards = payload["end_rewards"] as JArray;
            if (rewards != null && rewards.Count > 0)
            {
                var list = new List<(string Key, int Nova, int Credits)>();
                foreach (var row in rewards)
                    list.Add((PodiumLabel(FocusContext.AsInt(row["max_rank"])), FocusContext.AsInt(row["nova"]),
                        FocusContext.AsInt(row["credits"])));
                Podium = list.ToArray();
            }
        }

        static string ScoreLabel(string id)
        {
            switch (id)
            {
                case "battle_win": return "battleWon";
                case "conquest": return "planetConquered";
                case "defense": return "defenseHeld";
                case "stargate_capture": return "stargateCapture";
                case "stargate_colony": return "stargateColony";
                case "stargate_explore": return "stargateExplore";
                case "objective_daily": return "daily";
                case "objective_weekly": return "weekly";
                case "objective_monthly": return "monthly";
                default: return id;
            }
        }

        static string PodiumLabel(int maxRank)
        {
            switch (maxRank)
            {
                case 1: return "first";
                case 2: return "second";
                case 3: return "third";
                case 10: return "top10";
                case 20: return "top20";
                default: return "top" + maxRank;
            }
        }

        async Task Load(bool all)
        {
            if (_loading)
                return;
            _loading = true;
            try
            {
                var season = ActionJs.Get("GetActiveSeason");
                var empires = ActionJs.Get("GetSeasonLeaderboard", Args("category", "empires", "limit", "20"));
                var alliances = ActionJs.Get("GetSeasonLeaderboard", Args("category", "alliances", "limit", "20"));
                var accolades = ActionJs.Get("GetEmpireAccolades");
                // Closed seasons only change at a season's end: once per visit.
                var pantheon = all ? ActionJs.Get("GetSeasonPantheon") : Task.FromResult(default(ApiResult));
                await Task.WhenAll(season, empires, alliances, accolades, pantheon);
                if (this == null)
                    return;

                var s = Success(season.Result);
                if (s != null)
                {
                    _season = s["season"] as JObject;
                    _stats = s["player_stats"] as JObject;
                    ApplyCatalog(s);
                    _loadedAt = Time.unscaledTime;
                }

                _empires = Success(empires.Result)?["leaderboard"] as JArray ?? _empires;
                _alliances = Success(alliances.Result)?["leaderboard"] as JArray ?? _alliances;
                _accolades = Success(accolades.Result)?["accolades"] as JArray ?? _accolades;
                if (all)
                    _pantheon = Success(pantheon.Result)?["pantheon"] as JArray ?? _pantheon;
                if (!season.Result.Ok)
                    SetStatus(season.Result.Error, true);
            }
            finally
            {
                _loading = false;
                _refreshAt = Time.unscaledTime + RefreshEvery;
            }

            SyncHeader();
            Render();
        }

        /// <summary>A season reply is <c>{"status":"success", …}</c>; anything else counts as nothing.</summary>
        static JObject Success(ApiResult r)
        {
            var o = ScreenKit.Object(r);
            return o != null && FocusContext.AsString(o["status"]) == "success" ? o : null;
        }

        static Dictionary<string, string> Args(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (var i = 0; i + 1 < kv.Length; i += 2)
                d[kv[i]] = kv[i + 1];
            return d;
        }

        async Task Equip(int accoladeId)
        {
            if (_busy)
                return;
            _busy = true;
            try
            {
                var res = await ActionJs.Get("EquipEmpireTitle", Args("accolade_id", accoladeId.ToString()));
                if (this == null)
                    return;
                if (!res.Ok)
                {
                    SetStatus(string.IsNullOrEmpty(res.Error) ? Trans.Get("vr.common.error") : res.Error, true);
                    CicCue.Fail(_screen.transform.position);
                    return;
                }

                // Web onEquipTitle: one title worn at a time.
                if (_accolades != null)
                    foreach (var a in _accolades)
                        a["is_equipped"] = FocusContext.AsInt(a["id"]) == accoladeId ? 1 : 0;
                SetStatus(Trans.Get("vr.quarters.equipped"));
                CicCue.Ok(_screen.transform.position);
                _titleChanged?.Invoke();
            }
            finally
            {
                _busy = false;
                Render();
            }
        }

        // ── Render ────────────────────────────────────────────────────────────────

        void SetTab(Tab tab)
        {
            _tab = tab;
            _page = 0;
            SetStatus(string.Empty);
            Render();
        }

        void SetStatus(string text, bool error = false)
        {
            _status.text = text ?? string.Empty;
            _status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        void SyncHeader()
        {
            if (_header == null)
                return;
            _header.text = _season != null
                ? ScreenKit.Verbatim(FocusContext.AsString(_season["name"]))
                : Trans.Get("seasonSupremacy");
        }

        TMP_Text L(string text, float x, float y, float size, Color color, float width,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) =>
            ScreenKit.Line(_body, text, x, y, size, color, width, align);

        void Render()
        {
            ScreenKit.LightTabs(_tabs, (int)_tab);
            ScreenKit.Clear(_body);
            var news = _tab == Tab.News;
            _body.gameObject.SetActive(!news);
            _news.SetVisible(news);
            if (news)
                return;
            if (_season == null)
            {
                L(Trans.Get(_loadedAt > 0f ? "vr.season.none" : "Loading"), 0f, 60f, 20f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                return;
            }

            switch (_tab)
            {
                case Tab.Season:
                    RenderSeason();
                    break;
                case Tab.Leaderboard:
                    RenderEmpires();
                    break;
                case Tab.Alliances:
                    RenderAlliances();
                    break;
                case Tab.Pantheon:
                    RenderPantheon();
                    break;
                default:
                    RenderHonours();
                    break;
            }
        }

        long SecondsLeft() =>
            Math.Max(0L, FocusContext.AsLong(_season["remaining_seconds"]) - (long)(Time.unscaledTime - _loadedAt));

        void RenderSeason()
        {
            L("<b>" + ScreenKit.Verbatim(FocusContext.AsString(_season["name"])) + "</b>", -170f, 192f, 22f, Gold, 700f);
            L(Trans.Format("vr.quarters.endsIn", ScreenKit.Remaining(SecondsLeft())), 360f, 192f, 15f, UiKit.TextDim, 320f,
                TextAlignmentOptions.MidlineRight);
            var progress = FocusContext.AsFloat(_season["progress_percent"]);
            ScreenKit.Gauge(_body, 0f, 160f, 1040f, progress / 100f, new Color(1f, 0.72f, 0.2f, 0.8f),
                Trans.Format("vr.season.cycle", Mathf.RoundToInt(progress)), 20f);

            // Left: where we stand.
            var score = FocusContext.AsInt(_stats?["total_score"]);
            var tier = TierOf(score);
            L("<b>" + Trans.Get("vr.season.standing") + "</b>", -265f, 124f, 15f, DiegeticUi.CyanDim, 500f);
            L("<color=" + Tiers[tier].Hex + "><b>" + TierName(Tiers[tier].Key) + "</b></color>", -390f, 92f, 22f, UiKit.TextBright, 250f);
            var rank = FocusContext.AsString(_stats?["rank"]);
            L(Trans.Format("vr.season.rankPoints", rank.Length > 0 ? rank : "-", ScreenKit.Num(score)), -140f, 92f, 16f, UiKit.TextBright,
                250f, TextAlignmentOptions.MidlineRight);
            L(Trans.Format("vr.season.breakdown", ScreenKit.Num(FocusContext.AsInt(_stats?["combat_score"])),
                ScreenKit.Num(FocusContext.AsInt(_stats?["conquest_score"])), ScreenKit.Num(FocusContext.AsInt(_stats?["mission_score"]))),
                -265f, 60f, 14f, UiKit.TextDim, 500f);
            if (tier > 0)
            {
                var next = Tiers[tier - 1];
                var floor = Tiers[tier].MinScore;
                ScreenKit.Gauge(_body, -265f, 26f, 500f, (score - floor) / (float)Mathf.Max(1, next.MinScore - floor),
                    new Color(0.3f, 0.8f, 1f, 0.75f),
                    Trans.Format("vr.season.nextTier", TierName(next.Key), ScreenKit.Num(Mathf.Max(0, next.MinScore - score))), 22f);
            }
            else
            {
                L("<b>" + Trans.Get("vr.season.maxTier") + "</b>", -265f, 26f, 16f, UiKit.Ok, 500f, TextAlignmentOptions.Center);
            }

            L(Trans.Format("vr.season.novaClaimed", ScreenKit.Num(FocusContext.AsInt(_stats?["claimed_nova"]))), -265f, -8f, 14f,
                UiKit.Amber, 500f);
            ScreenKit.Para(_body, Trans.Get("vr.season.allianceHint"), -265f, -62f, 13f, UiKit.TextDim, 500f, 70f);

            // Right: the season's objectives — what earns supremacy points.
            L("<b>" + Trans.Get("vr.season.objectives") + "</b>", 265f, 124f, 15f, DiegeticUi.CyanDim, 500f);
            for (var i = 0; i < Scoring.Length; i++)
            {
                var y = 98f - i * 21f;
                L(Trans.Get("vr.season.score." + Scoring[i].Key), 210f, y, 13f, UiKit.TextBright, 390f);
                L("+" + Scoring[i].Points, 470f, y, 13f, Gold, 90f, TextAlignmentOptions.MidlineRight);
            }

            // Bottom: the tier ladder, ours lit; each tier's Nova is granted once, on reaching it.
            const float w = 144f;
            for (var t = Tiers.Length - 1; t >= 0; t--)
            {
                var col = Tiers.Length - 1 - t;
                var x = -444f + col * 148f;
                var mine = t == tier;
                var reached = score >= Tiers[t].MinScore;
                var c = FlagPainter.Hex(Tiers[t].Hex, Gold);
                Panel(x, -162f, w, 76f, new Color(c.r, c.g, c.b, mine ? 0.32f : reached ? 0.14f : 0.06f));
                L("<color=" + Tiers[t].Hex + "><b>" + TierName(Tiers[t].Key) + "</b></color>", x, -138f, 13f, UiKit.TextBright, w - 8f,
                    TextAlignmentOptions.Center);
                L(Trans.Format("vr.season.points", ScreenKit.Num(Tiers[t].MinScore)), x, -160f, 12f, reached ? UiKit.TextBright : UiKit.TextDim,
                    w - 8f, TextAlignmentOptions.Center);
                L(Tiers[t].Nova > 0 ? "+" + ScreenKit.Num(Tiers[t].Nova) + " " + Trans.Get("nova") : "—", x, -182f, 12f,
                    reached ? UiKit.Ok : UiKit.Amber, w - 8f, TextAlignmentOptions.Center);
            }
        }

        void RenderEmpires()
        {
            var rows = _empires;
            if (rows == null || rows.Count == 0)
            {
                L(Trans.Get("vr.season.noScores"), 0f, 60f, 17f, UiKit.TextDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            Head(new[]
            {
                ("vr.season.rank", -490f, 70f), ("empire", -250f, 400f), ("alliance", 70f, 200f), ("vr.season.tier", 270f, 160f),
                ("vr.season.score", 470f, 110f)
            });
            var me = FocusContext.AsInt(AuthManager.Ensure().Empire?["id"]);
            var first = Page(rows.Count);
            for (var i = first; i < rows.Count && i < first + RowsPerPage; i++)
            {
                var r = rows[i];
                var y = 150f - (i - first) * 40f;
                var rank = FocusContext.AsInt(r["rank"]);
                if (FocusContext.AsInt(r["empire_id"]) == me)
                    Panel(0f, y, 1050f, 36f, new Color(1f, 0.64f, 0.24f, 0.16f));
                L(RankText(rank), -490f, y, 15f, UiKit.TextBright, 70f);
                var title = FocusContext.AsString(r["active_title"]);
                L("<b>" + ScreenKit.Verbatim(FocusContext.AsString(r["empire_name"])) + "</b>" +
                  (title.Length > 0 ? "  <size=75%><color=#ffd24d>" + ScreenKit.Verbatim(title) + "</color></size>" : string.Empty),
                    -250f, y, 15f, UiKit.TextBright, 400f);
                var tag = FocusContext.AsString(r["alliance_tag"]);
                L(tag.Length > 0 ? "<color=#38bdf8>[" + ScreenKit.Verbatim(tag) + "]</color> " + ScreenKit.Verbatim(FocusContext.AsString(r["alliance_name"])) : "—",
                    70f, y, 13f, UiKit.TextDim, 200f);
                var t = Tiers[TierIndex(FocusContext.AsString(r["tier"]))];
                L("<color=" + t.Hex + ">" + TierName(t.Key) + "</color>", 270f, y, 13f, UiKit.TextBright, 160f);
                L("<b>" + ScreenKit.Num(FocusContext.AsInt(r["total_score"])) + "</b>", 470f, y, 15f, UiKit.Ok, 110f,
                    TextAlignmentOptions.MidlineRight);
            }

            var myRank = FocusContext.AsString(_stats?["rank"]);
            L(Trans.Format("vr.season.rankPoints", myRank.Length > 0 ? myRank : "-", ScreenKit.Num(FocusContext.AsInt(_stats?["total_score"]))),
                -200f, -205f, 14f, UiKit.Amber, 640f);
            Pager(rows.Count, RowsPerPage);
        }

        void RenderAlliances()
        {
            var rows = _alliances;
            if (rows == null || rows.Count == 0)
            {
                L(Trans.Get("vr.season.noAlliances"), 0f, 60f, 17f, UiKit.TextDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            Head(new[] { ("vr.season.rank", -490f, 70f), ("alliance", -130f, 560f), ("vr.season.members", 260f, 120f), ("vr.season.score", 470f, 110f) });
            var first = Page(rows.Count);
            for (var i = first; i < rows.Count && i < first + RowsPerPage; i++)
            {
                var r = rows[i];
                var y = 150f - (i - first) * 40f;
                L(RankText(FocusContext.AsInt(r["rank"])), -490f, y, 15f, UiKit.TextBright, 70f);
                var tag = FocusContext.AsString(r["alliance_tag"]);
                L("<b><color=#38bdf8>" + ScreenKit.Verbatim(FocusContext.AsString(r["alliance_name"])) + "</color></b>" +
                  (tag.Length > 0 ? "  <size=80%>[" + ScreenKit.Verbatim(tag) + "]</size>" : string.Empty), -130f, y, 15f, UiKit.TextBright, 560f);
                L(ScreenKit.Num(FocusContext.AsInt(r["member_count"])), 260f, y, 13f, UiKit.TextDim, 120f);
                L("<b>" + ScreenKit.Num(FocusContext.AsInt(r["total_score"])) + "</b>", 470f, y, 15f, UiKit.Ok, 110f,
                    TextAlignmentOptions.MidlineRight);
            }

            L(Trans.Get("vr.season.allianceHint"), -200f, -205f, 13f, UiKit.TextDim, 640f);
            Pager(rows.Count, RowsPerPage);
        }

        /// <summary>One closed season per page: its top empires and alliances, with their honours.</summary>
        void RenderPantheon()
        {
            if (_pantheon == null || _pantheon.Count == 0)
            {
                ScreenKit.Para(_body, Trans.Get("vr.season.pantheonEmpty"), 0f, 60f, 16f, UiKit.TextDim, 900f, 80f,
                    TextAlignmentOptions.Center);
                return;
            }

            _page = Mathf.Clamp(_page, 0, _pantheon.Count - 1);
            var s = _pantheon[_page];
            L("<b>" + ScreenKit.Verbatim(FocusContext.AsString(s["season_name"])) + "</b>", 0f, 192f, 20f, Gold, 1000f,
                TextAlignmentOptions.Center);
            var empires = s["empires"] as JArray;
            var alliances = s["alliances"] as JArray;
            L("<b>" + Trans.Get("empire") + "</b>", -265f, 158f, 14f, DiegeticUi.CyanDim, 500f);
            L("<b>" + Trans.Get("alliance") + "</b>", 265f, 158f, 14f, DiegeticUi.CyanDim, 500f);
            for (var i = 0; empires != null && i < empires.Count && i < 6; i++)
                Champion(empires[i], -265f, 120f - i * 56f);
            for (var i = 0; alliances != null && i < alliances.Count && i < 5; i++)
                Champion(alliances[i], 265f, 120f - i * 56f);
            Pager(_pantheon.Count, 1);
        }

        void Champion(JToken r, float x, float y)
        {
            var rank = FocusContext.AsInt(r["final_rank"]);
            L(RankText(rank) + "  <b>" + ScreenKit.Verbatim(FocusContext.AsString(r["entity_name"])) + "</b>", x - 60f, y + 12f, 15f,
                UiKit.TextBright, 380f);
            L(Trans.Format("vr.season.points", ScreenKit.Num(FocusContext.AsInt(r["score"]))), x + 190f, y + 12f, 13f, Gold, 120f,
                TextAlignmentOptions.MidlineRight);
            var reward = FocusContext.AsString(r["reward_desc"]);
            L("<color=#ffd24d>" + ScreenKit.Verbatim(FocusContext.AsString(r["badge_title"])) + "</color>" +
              (reward.Length > 0 ? "  <size=85%>" + ScreenKit.Verbatim(reward) + "</size>" : string.Empty), x, y - 12f, 12f, UiKit.TextDim, 500f);
        }

        /// <summary>The end-of-season rewards by rank, then our titles (one worn at a time: EquipEmpireTitle).</summary>
        void RenderHonours()
        {
            L("<b>" + Trans.Get("vr.season.endRewards") + "</b>", 0f, 192f, 15f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
            for (var i = 0; i < Podium.Length; i++)
            {
                var x = -424f + i * 212f;
                var hex = i < PodiumHex.Length ? PodiumHex[i] : "#38bdf8";
                var c = FlagPainter.Hex(hex, Gold);
                Panel(x, 140f, 204f, 64f, new Color(c.r, c.g, c.b, 0.14f));
                L("<color=" + hex + "><b>" + Trans.Get("vr.season.podium." + Podium[i].Key) + "</b></color>", x, 158f, 14f, UiKit.TextBright,
                    196f, TextAlignmentOptions.Center);
                L("+" + ScreenKit.Num(Podium[i].Nova) + " " + Trans.Get("nova") + "  ·  " + ScreenKit.Num(Podium[i].Credits) + " " +
                  Trans.Get("credits"), x, 128f, 11f, UiKit.Ok, 196f, TextAlignmentOptions.Center);
            }

            L(Trans.Get("vr.season.titleHint"), 0f, 88f, 12f, UiKit.TextDim, 1040f, TextAlignmentOptions.Center);
            if (_accolades == null || _accolades.Count == 0)
            {
                ScreenKit.Para(_body, Trans.Get("vr.season.noAccolades"), 0f, -10f, 15f, UiKit.TextDim, 900f, 70f,
                    TextAlignmentOptions.Center);
                return;
            }

            var first = Page(_accolades.Count, AccoladesPerPage);
            for (var i = first; i < _accolades.Count && i < first + AccoladesPerPage; i++)
            {
                var a = _accolades[i];
                var y = 46f - (i - first) * 58f;
                var on = FocusContext.AsInt(a["is_equipped"]) == 1;
                Panel(-110f, y, 820f, 52f, new Color(1f, 0.82f, 0.3f, on ? 0.18f : 0.06f));
                // Titles and season names are stored by the server as written (docs/PARITY.md): shown verbatim.
                L("<b>" + ScreenKit.Verbatim(FocusContext.AsString(a["title_label"])) + "</b>", -150f, y + 11f, 16f,
                    on ? Gold : UiKit.TextBright, 720f);
                L(ScreenKit.Verbatim(FocusContext.AsString(a["season_name"])), -150f, y - 13f, 12f, UiKit.TextDim, 720f);
                var id = FocusContext.AsInt(a["id"]);
                if (on)
                    L("<b>" + Trans.Get("shopEquipped") + "</b>", 420f, y, 16f, UiKit.Ok, 200f, TextAlignmentOptions.Center);
                else
                    ScreenKit.Btn(_body, Trans.Get("shopEquip"), 420f, y, 190f, 44f, () => AsyncTap.Run(Equip(id)), DiegeticUi.BtnStyle.Amber,
                        !_busy);
            }

            Pager(_accolades.Count, AccoladesPerPage);
        }

        // ── Pieces ────────────────────────────────────────────────────────────────

        /// <summary>Column titles, each on the same centre and width as its column's cells.</summary>
        void Head((string Key, float X, float W)[] cols)
        {
            foreach (var (key, x, w) in cols)
                L(Trans.Get(key), x, 190f, 12f, UiKit.TextDim, w,
                    x > 400f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
        }

        int Page(int count, int per = RowsPerPage)
        {
            var pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)per));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            return _page * per;
        }

        void Pager(int count, int per)
        {
            var pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)per));
            if (pages <= 1)
                return;
            ScreenKit.Btn(_body, (_page + 1) + "/" + pages + " ›", 470f, -205f, 110f, 36f, () =>
            {
                _page = (_page + 1) % pages;
                Render();
            });
        }

        void Panel(float x, float y, float w, float h, Color c)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_body, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            // Behind the row's text and buttons.
            go.transform.SetAsFirstSibling();
        }

        static string RankText(int rank) =>
            rank >= 1 && rank <= 3 ? "<color=" + PodiumHex[rank - 1] + "><b>#" + rank + "</b></color>" : "#" + rank;

        /// <summary>DetermineSeasonTier: the highest tier whose threshold the score reaches.</summary>
        int TierOf(int score)
        {
            for (var i = 0; i < Tiers.Length; i++)
                if (score >= Tiers[i].MinScore)
                    return i;
            return Tiers.Length - 1;
        }

        int TierIndex(string key)
        {
            for (var i = 0; i < Tiers.Length; i++)
                if (Tiers[i].Key == key)
                    return i;
            return Tiers.Length - 1;
        }

        static string TierName(string key) => Trans.Get("seasonTier_" + key);
    }
}
