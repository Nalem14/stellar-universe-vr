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
    /// Galactic contracts board of the science lab (web BountiesWindowUI, model/bounty.php): open contracts to
    /// accept (ClaimBounty) and ours in progress to hand in (CompleteBounty). The server requires a fleet of
    /// this empire in the target system (reward_credits is paid as crystal). Contracts are named by target type.
    /// </summary>
    public sealed class BountyBoard
    {
        const int PerPage = 5;
        const float Poll = 20f;

        sealed class Bounty
        {
            public int Id;
            public int SystemId;
            public string Type;
            public int Mineral;
            public int Credits;
            public int Research;
            public int State;
            public long ExpiresAt;
        }

        readonly RectTransform _body;
        readonly FocusContext _focus;
        readonly Action<string, bool> _status;
        readonly List<Bounty> _list = new();
        int _page;
        bool _busy;
        float _nextPoll;

        public BountyBoard(RectTransform body, FocusContext focus, Action<string, bool> status)
        {
            _body = body;
            _focus = focus;
            _status = status;
        }

        public void Tick()
        {
            if (!_busy && Time.unscaledTime >= _nextPoll)
                AsyncTap.Run(Refresh());
        }

        public async Task Refresh()
        {
            if (_busy)
                return;
            _busy = true;
            _nextPoll = Time.unscaledTime + Poll;
            try
            {
                var r = await ActionJs.Get("GetBounties");
                if (r.Ok)
                    Parse(r.Body);
            }
            finally
            {
                _busy = false;
            }

            Render();
        }

        void Parse(string body)
        {
            _list.Clear();
            JArray rows;
            try
            {
                rows = JToken.Parse(body) as JArray;
            }
            catch
            {
                return;
            }

            if (rows == null)
                return;
            foreach (var row in rows)
            {
                var id = FocusContext.AsInt(row["id"]);
                if (id <= 0)
                    continue;
                _list.Add(new Bounty
                {
                    Id = id,
                    SystemId = FocusContext.AsInt(row["target_systemid"]),
                    Type = FocusContext.AsString(row["target_type"]),
                    Mineral = FocusContext.AsInt(row["reward_mineral"]),
                    Credits = FocusContext.AsInt(row["reward_credits"]),
                    Research = FocusContext.AsInt(row["reward_research"]),
                    State = FocusContext.AsInt(row["state"]),
                    ExpiresAt = FocusContext.AsLong(row["expires_at"])
                });
            }

            // Ours in progress first, then open ones (newest first, as the server lists them).
            _list.Sort((a, b) => b.State != a.State ? b.State.CompareTo(a.State) : b.Id.CompareTo(a.Id));
        }

        /// <summary>One of our idle ships in the contract's target system.</summary>
        FocusFleet OnSite(int systemId)
        {
            if (_focus == null)
                return null;
            var me = FocusContext.OwnedUserId();
            var now = FleetOrderGate.UnixNow();
            foreach (var f in _focus.Fleets)
                if (f.UserId == me && f.SystemId == systemId && !f.IsMoving(now))
                    return f;
            return null;
        }

        public void Render()
        {
            for (var i = _body.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_body.GetChild(i).gameObject);
            var w = _body.sizeDelta.x;
            var left = -w * 0.5f + 25f;
            if (_list.Count == 0)
            {
                Text(Trans.Get(_busy ? "Loading" : "vr.bounty.none"), 0f, 40f, w - 60f, 24f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
                return;
            }

            var pages = Mathf.Max(1, Mathf.CeilToInt(_list.Count / (float)PerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var y = 262f;
            var now = FleetOrderGate.UnixNow();
            for (var i = _page * PerPage; i < _list.Count && i < (_page + 1) * PerPage; i++)
            {
                var b = _list[i];
                var claimed = b.State == 1;
                var accent = claimed ? UiKit.Amber : new Color(1f, 0.45f, 0.4f, 1f);
                Chip(new Vector2(left - 12f, y - 16f), new Vector2(8f, 72f), accent);
                Text("<b>" + Trans.Get("bounty_" + b.Type) + "</b>  <size=80%><color=#7fd8ff>" +
                     GalaxyCatalog.Label(b.SystemId) + "</color></size>", left, y, w - 360f, 27f, UiKit.TextBright);
                var reward = "<color=#c9a4ff>+" + b.Research.ToString("N0") + " " + Trans.Get("vr.research.pts") +
                             "</color>   <color=#7fd8ff>+" + b.Mineral.ToString("N0") + " " + Trans.Get("vr.res.mineral") +
                             "</color>" + (b.Credits > 0 ? "   <color=#ffd27a>+" + b.Credits.ToString("N0") + " " +
                                           Trans.Get("credits") + "</color>" : string.Empty) + "   <size=85%>" + Trans.Get(claimed ? "bountyClaimed" : "bountyOpen") + " · " +
                             Core.Holo.TravelPlanner.TimeText(b.ExpiresAt - now) + "</size>";
                Text(reward, left, y - 34f, w - 360f, 21f, new Color(0.8f, 0.88f, 0.95f, 1f));

                var id = b.Id;
                if (!claimed)
                {
                    Btn(Trans.Get("claimBounty"), w * 0.5f - 170f, y - 16f, 300f, 60f,
                        () => AsyncTap.Run(Order("ClaimBounty", id, "bounty_claimed_successfully")),
                        DiegeticUi.BtnStyle.Cyan);
                }
                else
                {
                    var ship = OnSite(b.SystemId);
                    var btn = Btn(ship != null ? Trans.Get("completeBounty") : Trans.Get("vr.bounty.goThere"),
                        w * 0.5f - 170f, y - 16f, 300f, 60f,
                        () => AsyncTap.Run(Order("CompleteBounty", id, "bounty_completed_success")),
                        ship != null ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
                    btn.interactable = ship != null;
                }

                y -= 100f;
            }

            if (pages > 1)
            {
                Btn("‹", -80f, -300f, 80f, 50f, () => { _page = (_page - 1 + pages) % pages; Render(); },
                    DiegeticUi.BtnStyle.Ghost);
                Text((_page + 1) + " / " + pages, 0f, -300f, 90f, 22f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                Btn("›", 80f, -300f, 80f, 50f, () => { _page = (_page + 1) % pages; Render(); },
                    DiegeticUi.BtnStyle.Ghost);
            }
        }

        async Task Order(string action, int bountyId, string okKey)
        {
            if (_busy)
                return;
            _busy = true;
            ApiResult r;
            try
            {
                r = await ActionJs.Get(action, new Dictionary<string, string> { { "bounty", bountyId.ToString() } });
            }
            finally
            {
                _busy = false;
            }

            if (r.Ok)
            {
                CicCue.Ok(_body.position);
                var text = Trans.Get(okKey);
                if (action == "CompleteBounty")
                {
                    try
                    {
                        var o = JObject.Parse(r.Body);
                        text += "  " + Trans.Format("vr.bounty.rewards", FocusContext.AsInt(o["reward_research"]),
                            FocusContext.AsInt(o["reward_mineral"]), FocusContext.AsInt(o["reward_xp"]));
                        var credits = FocusContext.AsInt(o["reward_credits"]);
                        if (credits > 0)
                            text += "  +" + credits.ToString("N0") + " " + Trans.Get("credits");
                    }
                    catch
                    {
                        // Keep the plain line.
                    }

                    var eco = EconomyService.Instance;
                    if (eco != null)
                        await eco.RefreshNow();
                }

                _status(text, false);
            }
            else
            {
                CicCue.Fail(_body.position);
                _status(string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error, true);
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Science, "fail", 3);
            }

            await Refresh();
        }

        // ── Widgets ───────────────────────────────────────────────────────────────

        TMP_Text Text(string text, float x, float y, float width, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var leftAlign = align is TextAlignmentOptions.MidlineLeft;
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(leftAlign ? x + width * 0.5f : x, y),
                new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            return t;
        }

        Button Btn(string label, float x, float y, float w, float h, Action act, DiegeticUi.BtnStyle style)
        {
            var b = DiegeticUi.HoloButton(_body, label, new Vector2(x, y), new Vector2(w, h),
                () => { if (!_busy) act(); }, style);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.enableAutoSizing = true;
            t.fontSizeMin = 11f;
            t.fontSizeMax = Mathf.Min(26f, h * 0.45f);
            return b;
        }

        void Chip(Vector2 pos, Vector2 size, Color color)
        {
            var chip = new GameObject("Chip", typeof(RectTransform), typeof(Image));
            chip.transform.SetParent(_body, false);
            var rt = chip.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = chip.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }
    }
}
