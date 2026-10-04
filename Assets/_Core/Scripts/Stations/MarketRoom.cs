using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// The galactic exchange — web MarketplaceUI as a place. A trading floor over our ship: the offers of the
    /// current page float as guild crates over the exchange pit (resource cores in their colour, modules in
    /// miniature), quote boards scroll on the walls, freight containers stand at the bay on space. Two desks at
    /// the stand: the Exchange (left) — GetMarketplaceData: category, sort, search, pages, pick an offer and buy
    /// it; the Counter (right) — one of our worlds (GetPlanetTradeStatus): publish a sale from its stock or hangar
    /// (CreateMarketOffer, escrowed at once), its convoys in flight and its offers (CancelMarketOffer, two
    /// presses), and the convoy composer of a purchase (DispatchMarketConvoy: orbiting ships with free hold, the
    /// hold the server asks for, the price due, the leg's estimate). Reads only while inside (the server resolves
    /// arrived convoys on them); every failure is the server's own translated <c>error:</c>.
    /// </summary>
    public sealed class MarketRoom : MonoBehaviour
    {
        enum Mode
        {
            Create,
            Convoys,
            Dispatch
        }

        static readonly Vector3 WorldOrigin = new(0f, -3000f, 200f);
        static readonly Vector3 Stand = Vector3.zero;
        public static readonly Color Accent = new(0.36f, 1f, 0.66f, 1f);
        static readonly Vector2 ScreenSize = new(1.1f, 0.7f);
        const int PageSize = 5;
        const float ConfirmWindow = 4f;
        const float RefreshEvery = 20f;
        static readonly string[] Categories = { "all", "mineral", "module" };
        static readonly string[] CategoryKeys = { "market_all", "market_minerals", "market_modules" };
        static readonly string[] Sorts = { "recent", "price_asc", "price_desc", "distance_asc" };
        static readonly string[] SortKeys = { "market_sort_recent", "market_sort_price_asc", "market_sort_price_desc", "market_sort_distance" };
        static readonly int[] ResourceSteps = { -10000, -1000, -100, 100, 1000, 10000 };
        static readonly int[] ModuleSteps = { -10, -5, -1, 1, 5, 10 };

        public static MarketRoom Instance { get; private set; }
        public static bool Inside { get; private set; }

        CicArtKit _art;
        MarketDecor.Refs _decor;
        FocusContext _focus;

        HoloScreen _exchange;
        RectTransform _exchangeBody;
        TMP_Text _exchangeStatus;
        readonly Button[] _categoryTabs = new Button[3];
        Button _sortButton;
        TMP_InputField _search;
        HoloScreen _counter;
        RectTransform _counterBody;
        TMP_Text _counterStatus;
        Button[] _modeTabs;
        TMP_Text _planetLabel;

        // Exchange state.
        int _category;
        int _sort;
        int _page = 1;
        string _query = string.Empty;
        MarketPage _listings;
        int _selectedListing;

        // Counter state.
        int _planetId;
        MarketContext _ctx;
        /// <summary>The convoy's port of departure (web planet selector): the market's world unless another is picked.</summary>
        MarketContext _origin;
        bool _originLoading;
        Mode _mode = Mode.Create;
        int _sellKind;
        int _moduleIndex;
        float _quantity;
        int _currency = 1;
        float _price;
        MarketListing _buying;
        readonly HashSet<int> _haulers = new();
        string _confirm;
        float _confirmUntil;
        bool _busy;
        bool _transit;
        float _nextRefresh;
        float _nextTick;
        readonly List<(TMP_Text label, long arrival)> _countdowns = new();

        MarketPit _pit;

        // ── Build ─────────────────────────────────────────────────────────────────

        public static MarketRoom Build(CicArtKit art, FocusContext focus)
        {
            var go = new GameObject("MarketExchange");
            go.transform.position = WorldOrigin;
            var room = go.AddComponent<MarketRoom>();
            room._art = art;
            room._focus = focus;
            room.BuildHall();
            room.BuildScreens();
            room._pit = MarketPit.Build(go.transform, art, Accent, room.SelectListing, room.BuyHeld, room.PutBack);
            go.SetActive(false);
            return room;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void BuildHall()
        {
            _decor = MarketDecor.Build(transform, _art, Accent);
            var fill = new GameObject("HallLight").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.transform.localPosition = new Vector3(0f, MarketDecor.Height - 0.7f, 4.2f);
            fill.type = LightType.Point;
            // One light for the floor (Quest): warm white, strong enough for the far containers.
            fill.range = 15f;
            fill.intensity = 3.2f;
            fill.color = new Color(0.92f, 1f, 0.94f);
            fill.shadows = LightShadows.None;

            RoomDoor.Build(transform, "DoorToCorridor", new Vector3(0f, 0f, MarketDecor.BackZ + 0.18f), 0f,
                Trans.Get("vr.dock.leave"), UiKit.Cyan, _art, () => Inside, () => AsyncTap.Run(Leave()));
        }

        void BuildScreens()
        {
            _exchange = HoloScreen.Create(transform, "Exchange", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("market_browse"));
            _exchange.SetAccent(Accent, 0.5f);
            GateRoomDecor.SeatOnArm(_exchange.transform, _decor.BrowseMount, ScreenSize.y);
            var ef = _exchange.Content;
            for (var i = 0; i < _categoryTabs.Length; i++)
            {
                var index = i;
                _categoryTabs[i] = ScreenKit.Small(DiegeticUi.HoloButton(ef, Trans.Get(CategoryKeys[i]), new Vector2(-385f + i * 250f, 245f),
                    new Vector2(240f, 46f), () => SetCategory(index), DiegeticUi.BtnStyle.Ghost), 11f, 18f);
            }

            _sortButton = ScreenKit.Small(DiegeticUi.HoloButton(ef, Trans.Get(SortKeys[0]), new Vector2(400f, 245f),
                new Vector2(250f, 46f), CycleSort, DiegeticUi.BtnStyle.Ghost), 11f, 17f);
            // Typing field outside the rebuilt body: a refresh never steals the keyboard.
            _search = DiegeticUi.HoloField(ef, "Search", Trans.Get("market_search_ph"), new Vector2(-40f, 188f),
                new Vector2(900f, 44f), TouchScreenKeyboardType.Default);
            _search.characterLimit = 48;
            _search.gameObject.AddComponent<RectMask2D>();
            _search.onSubmit.AddListener(_ => ApplySearch());
            _search.onEndEdit.AddListener(_ => ApplySearch());
            DiegeticUi.HoloButton(ef, "×", new Vector2(470f, 188f), new Vector2(60f, 44f), ClearSearch, DiegeticUi.BtnStyle.Ghost);
            _exchangeBody = ScreenKit.Body(ef, -42f);
            _exchangeStatus = DiegeticUi.HoloLabel(ef, string.Empty, new Vector2(-30f, -305f), new Vector2(980f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);

            _counter = HoloScreen.Create(transform, "Counter", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("market_title"));
            _counter.SetAccent(Accent, 0.5f);
            GateRoomDecor.SeatOnArm(_counter.transform, _decor.CounterMount, ScreenSize.y);
            var cf = _counter.Content;
            _modeTabs = new[]
            {
                ScreenKit.Small(DiegeticUi.HoloButton(cf, Trans.Get("market_create"), new Vector2(-265f, 245f), new Vector2(520f, 46f),
                    () => SetMode(Mode.Create), DiegeticUi.BtnStyle.Ghost), 11f, 18f),
                ScreenKit.Small(DiegeticUi.HoloButton(cf, Trans.Get("market_convoys"), new Vector2(265f, 245f), new Vector2(520f, 46f),
                    () => SetMode(Mode.Convoys), DiegeticUi.BtnStyle.Ghost), 11f, 18f)
            };
            _planetLabel = ScreenKit.Line(cf, string.Empty, 0f, 190f, 20f, UiKit.TextBright, 880f, TextAlignmentOptions.Center);
            _counterBody = ScreenKit.Body(cf, -48f);
            _counterStatus = DiegeticUi.HoloLabel(cf, string.Empty, new Vector2(-30f, -305f), new Vector2(980f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);
        }

        // ── Enter / leave ─────────────────────────────────────────────────────────

        /// <summary>Into the exchange of <paramref name="planetId"/> (the world under the station or city we stand in).</summary>
        public async Task Enter(int planetId)
        {
            if (DiplomacyRoom.InRoomBeyondCorridor || _transit)
                return;
            _transit = true;
            var fade = ViewFade.Ensure();
            try
            {
                await fade.FadeOut();
                if (CorridorRoom.Inside)
                    CorridorRoom.Instance.Depart();
                // Over our ship, the star in the bay at the far end.
                RoomPlacement.OverShip(transform, MarketDecor.Pit + Vector3.forward * 5f - Stand);
                gameObject.SetActive(true);
                var rig = FindFirstObjectByType<XROrigin>();
                if (rig != null)
                {
                    rig.transform.SetParent(transform, false);
                    rig.transform.localPosition = Stand;
                    rig.transform.localRotation = Quaternion.identity;
                    XrPlacement.PlaceHead(rig, transform.TransformPoint(Stand), transform.forward);
                }

                Inside = true;
                _confirm = null;
                if (_planetId != planetId)
                {
                    _ctx = null;
                    _haulers.Clear();
                }

                _planetId = planetId;
                if (_mode == Mode.Dispatch)
                    _mode = Mode.Create;
                RenderAll();
            }
            finally
            {
                // Whatever happened above, the veil lifts: never leave the player blind on the floor.
                await fade.FadeIn();
                _transit = false;
            }

            CicCue.Ok(transform.position + Vector3.up);
            await LoadAll();
        }

        async Task Leave()
        {
            if (!Inside || _transit)
                return;
            _transit = true;
            var fade = ViewFade.Ensure();
            try
            {
                await fade.FadeOut();
                Inside = false;
                _pit.Hide();
                _buying = null;
                _selectedListing = 0;
                CorridorRoom.ReturnPlayer(CorridorRoom.Slot.MarketPort);
                gameObject.SetActive(false);
            }
            finally
            {
                await fade.FadeIn();
                _transit = false;
            }
        }

        int PlanetSystem(int planetId)
        {
            foreach (var p in OwnedPlanets.All)
                if (p.Id == planetId)
                    return p.SystemId;
            return 0;
        }

        static void Run(Task t) => AsyncTap.Run(t);

#if UNITY_EDITOR
        public MarketPit EditorPit => _pit;

        /// <summary>Verification: dress the floor from injected reads (no server, no XR), as if just entered.</summary>
        public void EditorShow(MarketPage page, MarketContext ctx)
        {
            gameObject.SetActive(true);
            Inside = true;
            _planetId = ctx.PlanetId;
            _ctx = ctx;
            if (_origin != null && ctx != null && _origin.PlanetId == ctx.PlanetId)
                _origin = ctx;
            _listings = page;
            _pit.SetHome(ctx.SystemId, ctx.PlanetName);
            _pit.SetMissions(ctx.Missions);
            _pit.ShowOffers(page.Listings);
            _pit.SetRibbon(page.Listings);
            RenderAll();
        }

        /// <summary>Verification: draft a sale (counter + pad), pick an offer (cradle), or open the composer.</summary>
        public void EditorDraft(int kind, float quantity, float price)
        {
            _mode = Mode.Create;
            _sellKind = kind;
            _quantity = quantity;
            _price = price;
            RenderCounter();
        }

        public void EditorSelect(int listingId) => SelectListing(listingId);
        public void EditorBuy(int listingId) => BeginPurchase(listingId);

        public static void EditorReset() => Inside = false;
#endif

        // ── Data ──────────────────────────────────────────────────────────────────

        async Task LoadAll()
        {
            var ctx = LoadContext();
            var page = LoadListings();
            await Task.WhenAll(ctx, page);
            _nextRefresh = Time.time + RefreshEvery;
        }

        async Task LoadContext()
        {
            if (_planetId <= 0)
            {
                _ctx = null;
                RenderCounter();
                return;
            }

            var asked = _planetId;
            var (ctx, error) = await MarketService.Context(asked);
            if (!Inside || asked != _planetId)
                return;
            if (ctx == null)
            {
                _counterStatus.text = ScreenKit.Verbatim(error);
                return;
            }

            _ctx = ctx;
            if (_origin != null && ctx != null && _origin.PlanetId == ctx.PlanetId)
                _origin = ctx;
            _pit.SetHome(ctx.SystemId > 0 ? ctx.SystemId : PlanetSystem(asked), ctx.PlanetName);
            _pit.SetMissions(ctx.Missions);
            if (_buying != null)
                _haulers.RemoveWhere(id => ctx.Haulers.Find(h => h.Id == id) == null);
            ClampDraft();
            RenderCounter();
        }

        async Task LoadListings()
        {
            var (page, error) = await MarketService.Browse(Categories[_category], _query, Sorts[_sort], _page, PageSize,
                PlanetSystem(_planetId));
            if (!Inside)
                return;
            if (page == null)
            {
                _exchangeStatus.text = ScreenKit.Verbatim(error);
                return;
            }

            _listings = page;
            if (_page > page.Pages)
            {
                _page = page.Pages;
                await LoadListings();
                return;
            }

            if (_selectedListing > 0 && FindListing(_selectedListing) == null)
                _selectedListing = 0;
            _pit.ShowOffers(page.Listings);
            _pit.SetRibbon(page.Listings);
            RenderExchange();
        }

        void Update()
        {
            if (!Inside)
                return;
            // Quote boards scroll (the pit animates itself).
            if (_decor.Ticker != null)
                _decor.Ticker.mainTextureOffset = new Vector2(0f, Time.time * 0.018f % 1f);

            if (Time.time >= _nextTick)
            {
                _nextTick = Time.time + 1f;
                var now = FleetOrderGate.UnixNow();
                foreach (var (label, arrival) in _countdowns)
                    if (label != null)
                        label.text = ScreenKit.Remaining(arrival - now);
                if (_confirm != null && Time.time > _confirmUntil)
                    _confirm = null;
            }

            if (Time.time >= _nextRefresh && !_busy)
            {
                _nextRefresh = Time.time + RefreshEvery;
                Run(LoadAll());
            }
        }

        // ── Exchange (left) ───────────────────────────────────────────────────────

        void SetCategory(int index)
        {
            _category = index;
            _page = 1;
            CicCue.Ok(_exchange.transform.position);
            RenderExchange();
            Run(LoadListings());
        }

        void CycleSort()
        {
            _sort = (_sort + 1) % Sorts.Length;
            _page = 1;
            CicCue.Ok(_exchange.transform.position);
            RenderExchange();
            Run(LoadListings());
        }

        void ApplySearch()
        {
            var q = (_search.text ?? string.Empty).Trim();
            if (q == _query)
                return;
            _query = q;
            _page = 1;
            Run(LoadListings());
        }

        void ClearSearch()
        {
            _search.SetTextWithoutNotify(string.Empty);
            ApplySearch();
        }

        void StepPage(int delta)
        {
            if (_listings == null)
                return;
            var next = Mathf.Clamp(_page + delta, 1, _listings.Pages);
            if (next == _page)
                return;
            _page = next;
            CicCue.Ok(_exchange.transform.position);
            Run(LoadListings());
        }

        void RenderAll()
        {
            RenderExchange();
            RenderCounter();
        }

        void RenderExchange()
        {
            ScreenKit.Clear(_exchangeBody);
            ScreenKit.LightTabs(_categoryTabs, _category);
            _sortButton.GetComponentInChildren<TMP_Text>().text = Trans.Get(SortKeys[_sort]);
            var body = _exchangeBody;
            if (_listings == null)
            {
                ScreenKit.Line(body, Trans.Get("market_syncing"), 0f, 40f, 20f, UiKit.TextDim, 900f, TextAlignmentOptions.Center);
                return;
            }

            if (_listings.Listings.Count == 0)
            {
                ScreenKit.Line(body, Trans.Get("market_no_match"), 0f, 60f, 22f, UiKit.TextBright, 900f, TextAlignmentOptions.Center);
                ScreenKit.Para(body, Trans.Get("market_be_first"), 0f, 0f, 17f, UiKit.TextDim, 760f, 60f, TextAlignmentOptions.Top);
            }

            var me = FocusContext.OwnedUserId();
            for (var i = 0; i < _listings.Listings.Count && i < PageSize; i++)
            {
                var l = _listings.Listings[i];
                var y = 168f - i * 70f;
                var color = MarketDecor.CategoryColor(l.Category, l.ItemKey);
                var picked = l.Id == _selectedListing;
                var id = l.Id;
                var row = ScreenKit.Btn(body, string.Empty, -95f, y, 860f, 62f, () => SelectListing(id),
                    picked ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                row.GetComponent<Image>().color = picked ? new Color(0.6f, 1f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.18f);
                ScreenKit.Line(body, "<b><color=" + ScreenKit.Hex(color) + ">" + ScreenKit.Verbatim(l.ItemName) + "</color></b>  ×" +
                                     ScreenKit.Num(l.Quantity), -330f, y + 13f, 19f, UiKit.TextBright, 380f);
                var where = ScreenKit.Verbatim(l.SellerName) + (string.IsNullOrEmpty(l.EmpireName) ? string.Empty : " · " + ScreenKit.Verbatim(l.EmpireName)) +
                            " · " + ScreenKit.Verbatim(l.PlanetName) + " · " + ScreenKit.Num(l.Distance, 1) + " " + Trans.Get("market_ly");
                ScreenKit.Line(body, where, -330f, y - 15f, 14f, UiKit.TextDim, 380f);
                ScreenKit.Line(body, "<b>" + ScreenKit.Num(l.PriceAmount) + "</b> " + Trans.Get(MarketService.ResourceKey(l.PriceCurrency)),
                    150f, y + 10f, 18f, UiKit.Amber, 300f);
                ScreenKit.Line(body, ScreenKit.Num(RequiredHold(l)) + " m³", 150f, y - 16f, 14f, UiKit.TextDim, 300f);
                if (l.UserId == me)
                    ScreenKit.Line(body, Trans.Get("market_your_offer"), 440f, y, 15f, Accent, 170f, TextAlignmentOptions.Center);
                else
                    ScreenKit.Btn(body, Trans.Get("market_buy_btn"), 440f, y, 170f, 56f, () => BeginPurchase(id), DiegeticUi.BtnStyle.Cyan);
            }

            ScreenKit.Btn(body, "‹", -200f, -190f, 70f, 44f, () => StepPage(-1), DiegeticUi.BtnStyle.Ghost, _page > 1);
            ScreenKit.Line(body, Trans.Format("market_page", _listings.Page, _listings.Pages, _listings.Total), 0f, -190f, 17f,
                UiKit.TextDim, 300f, TextAlignmentOptions.Center);
            ScreenKit.Btn(body, "›", 200f, -190f, 70f, 44f, () => StepPage(1), DiegeticUi.BtnStyle.Ghost, _page < _listings.Pages);
        }

        /// <summary>A row or a crate picked: the offer comes to the cradle (picked again: it goes back).</summary>
        void SelectListing(int id)
        {
            var l = FindListing(id);
            _selectedListing = l == null || _selectedListing == id ? 0 : id;
            _pit.Inspect(_selectedListing > 0 ? l : null, l != null && l.UserId == FocusContext.OwnedUserId());
            if (_selectedListing == 0 && _mode == Mode.Dispatch)
            {
                _mode = Mode.Create;
                _buying = null;
                RenderCounter();
            }

            RenderExchange();
        }

        /// <summary>The cradle's buy key: the counter opens the convoy composer for the offer in hand.</summary>
        void BuyHeld()
        {
            var l = _pit.Held;
            if (l != null && l.UserId != FocusContext.OwnedUserId())
                BeginPurchase(l.Id);
        }

        /// <summary>The cradle's back key: the crate returns to the carousel.</summary>
        void PutBack()
        {
            _selectedListing = 0;
            _pit.Inspect(null, false);
            if (_mode == Mode.Dispatch)
            {
                _mode = Mode.Create;
                _buying = null;
                RenderCounter();
            }

            RenderExchange();
        }

        MarketListing FindListing(int id)
        {
            if (_listings == null)
                return null;
            foreach (var l in _listings.Listings)
                if (l.Id == id)
                    return l;
            return null;
        }

        /// <summary>The hold the server asks of a convoy: the goods home or the payment out, whichever is larger.</summary>
        static float RequiredHold(MarketListing l) =>
            l.RequiredCargo > 0 ? l.RequiredCargo : Mathf.Max(l.CargoVolume, Mathf.Ceil(l.PriceAmount));

        void BeginPurchase(int id)
        {
            var l = FindListing(id);
            if (l == null)
                return;
            if (_pit.Held == null || _pit.Held.Id != id)
                _pit.Inspect(l, false);
            _selectedListing = id;
            _buying = l;
            _origin = _ctx;
            PrePick();
            _mode = Mode.Dispatch;
            CicCue.Ok(_counter.transform.position);
            RenderExchange();
            RenderCounter();
        }

        /// <summary>Pre-pick the departure world's ships that cover the hold, roomiest first (the web panel does the same).</summary>
        void PrePick()
        {
            _haulers.Clear();
            var o = _origin ?? _ctx;
            if (o == null || _buying == null)
                return;
            var need = RequiredHold(_buying);
            var acc = 0f;
            var sorted = new List<MarketHauler>(o.Haulers);
            sorted.Sort((a, b) => b.CargoFree.CompareTo(a.CargoFree));
            foreach (var h in sorted)
            {
                if (acc >= need || h.CargoFree <= 0f || h.IsStation)
                    continue;
                _haulers.Add(h.Id);
                acc += h.CargoFree;
            }
        }

        /// <summary>Another of our worlds as the port of departure (‹ ›): its ships, its stock, its distance.</summary>
        async Task StepOrigin(int delta)
        {
            var o = _origin ?? _ctx;
            if (_originLoading || o == null || _ctx == null || _ctx.UserPlanets.Count < 2)
                return;
            var i = _ctx.UserPlanets.FindIndex(w => w.id == o.PlanetId);
            var next = _ctx.UserPlanets[(Mathf.Max(0, i) + delta + _ctx.UserPlanets.Count) % _ctx.UserPlanets.Count];
            _originLoading = true;
            CicCue.Ok(_counter.transform.position);
            RenderCounter();
            try
            {
                var (ctx, error) = next.id == _ctx.PlanetId ? (_ctx, null) : await MarketService.Context(next.id);
                if (ctx == null)
                {
                    _counterStatus.text = ScreenKit.Verbatim(error);
                    return;
                }

                _origin = ctx;
                PrePick();
            }
            finally
            {
                _originLoading = false;
            }

            if (_mode == Mode.Dispatch)
                RenderCounter();
        }

        /// <summary>Light-years from the departure world's system to the seller's (the server's straight line on the grid).</summary>
        static float Distance(MarketListing l, MarketContext origin)
        {
            if (origin == null || origin.SystemId <= 0 || l.SystemId <= 0)
                return l.Distance;
            if (origin.SystemId == l.SystemId)
                return 0f;
            if (!Core.Vfx.GalaxyCatalog.TryGet(origin.SystemId, out var a) || !Core.Vfx.GalaxyCatalog.TryGet(l.SystemId, out var b))
                return l.Distance;
            return Mathf.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        // ── Counter (right) ───────────────────────────────────────────────────────

        void SetMode(Mode mode)
        {
            _mode = mode;
            _buying = mode == Mode.Dispatch ? _buying : null;
            _confirm = null;
            CicCue.Ok(_counter.transform.position);
            RenderCounter();
        }

        void RenderCounter()
        {
            ScreenKit.Clear(_counterBody);
            _countdowns.Clear();
            ScreenKit.LightTabs(_modeTabs, _mode == Mode.Convoys ? 1 : _mode == Mode.Create ? 0 : -1);
            var name = _ctx != null ? _ctx.PlanetName : PlanetName(_planetId);
            _planetLabel.text = "<b>" + ScreenKit.Verbatim(name) + "</b>" + (_ctx != null
                ? "   <color=" + ScreenKit.Hex(UiKit.TextDim) + ">" + (_ctx.OrbitCargoFree > 0f
                    ? Trans.Format("market_orbit_free", ScreenKit.Num(_ctx.OrbitCargoFree))
                    : Trans.Get("market_orbit_none")) + "</color>"
                : string.Empty);
            if (_planetId <= 0)
            {
                ScreenKit.Para(_counterBody, Trans.Get("planetNotYours"), 0f, 40f, 20f, UiKit.TextDim, 800f, 80f, TextAlignmentOptions.Center);
                return;
            }

            if (_ctx == null)
            {
                ScreenKit.Line(_counterBody, Trans.Get("market_syncing"), 0f, 40f, 20f, UiKit.TextDim, 900f, TextAlignmentOptions.Center);
                return;
            }

            ShowDraft();
            switch (_mode)
            {
                case Mode.Create:
                    RenderCreate(_counterBody);
                    break;
                case Mode.Convoys:
                    RenderConvoys(_counterBody);
                    break;
                default:
                    RenderDispatch(_counterBody);
                    break;
            }
        }

        static string PlanetName(int id)
        {
            foreach (var p in OwnedPlanets.All)
                if (p.Id == id)
                    return p.Name;
            return string.Empty;
        }

        // Create ----------------------------------------------------------------

        /// <summary>The sale being drafted stands on the consignment pad as its crate (nothing to sell: the pad is bare).</summary>
        void ShowDraft()
        {
            if (_mode != Mode.Create || _ctx == null || _quantity <= 0f || (SellingModule && _ctx.Hangar.Count == 0))
            {
                _pit.Consign(null, null, null, null);
                return;
            }

            var (category, key, name) = DraftItem();
            var price = _price > 0f
                ? "<color=" + ScreenKit.Hex(UiKit.Amber) + ">" + ScreenKit.Num(_price) + " " +
                  Trans.Get(MarketService.ResourceKey(MarketService.Resources[_currency])) + "</color>"
                : string.Empty;
            _pit.Consign(category, key, ScreenKit.Verbatim(name) + " ×" + ScreenKit.Num(_quantity), price);
        }

        (string category, string key, string name) DraftItem()
        {
            if (SellingModule)
            {
                var m = _ctx.Hangar[_moduleIndex];
                return ("module", m.type, m.name);
            }

            var res = MarketService.Resources[_sellKind];
            return ("mineral", res, Trans.Get(MarketService.ResourceKey(res)));
        }

        bool SellingModule => _sellKind == 3;

        void ClampDraft()
        {
            if (_ctx == null)
                return;
            if (SellingModule)
            {
                _moduleIndex = _ctx.Hangar.Count == 0 ? 0 : Mathf.Clamp(_moduleIndex, 0, _ctx.Hangar.Count - 1);
                var stock = _ctx.Hangar.Count > 0 ? _ctx.Hangar[_moduleIndex].count : 0;
                _quantity = Mathf.Clamp(Mathf.Round(_quantity), stock > 0 ? 1f : 0f, stock);
            }
            else
            {
                var stock = Mathf.Floor(_ctx.Stock(MarketService.Resources[_sellKind]));
                _quantity = Mathf.Clamp(_quantity, 0f, stock);
            }

            _price = Mathf.Max(0f, _price);
        }

        void RenderCreate(RectTransform body)
        {
            ScreenKit.Line(body, Trans.Get("market_step_type"), -505f + 250f, 196f, 16f, Accent, 500f);
            for (var i = 0; i < 4; i++)
            {
                var kind = i;
                string label;
                if (i < 3)
                    label = Trans.Get(MarketService.ResourceKey(MarketService.Resources[i])) + "\n<size=70%>" +
                            ScreenKit.Num(_ctx.Stock(MarketService.Resources[i])) + "</size>";
                else
                    label = Trans.Get("market_modules");
                var b = ScreenKit.Btn(body, label, -390f + i * 260f, 152f, 245f, 56f, () => SetSellKind(kind),
                    _sellKind == i ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                b.GetComponentInChildren<TMP_Text>().richText = true;
            }

            if (SellingModule)
            {
                if (_ctx.Hangar.Count == 0)
                {
                    ScreenKit.Para(body, Trans.Get("market_hangar_empty_sale"), 0f, 82f, 15f, UiKit.TextDim, 960f, 56f, TextAlignmentOptions.Center);
                    return;
                }

                var m = _ctx.Hangar[_moduleIndex];
                ScreenKit.Btn(body, "‹", -330f, 96f, 60f, 40f, () => StepModule(-1));
                ScreenKit.Line(body, "<b>" + ScreenKit.Verbatim(m.name) + "</b>  " + Trans.Format("market_in_stock", m.count), 0f, 96f, 18f,
                    UiKit.TextBright, 580f, TextAlignmentOptions.Center);
                ScreenKit.Btn(body, "›", 330f, 96f, 60f, 40f, () => StepModule(1));
            }
            else
            {
                ScreenKit.Line(body, Trans.Format("market_in_stock", ScreenKit.Num(_ctx.Stock(MarketService.Resources[_sellKind]))), 0f, 96f, 17f,
                    UiKit.TextDim, 700f, TextAlignmentOptions.Center);
            }

            ScreenKit.Line(body, Trans.Get("market_step_qty"), -255f, 52f, 16f, Accent, 500f);
            Stepper(body, 14f, ScreenKit.Num(_quantity), SellingModule ? ModuleSteps : ResourceSteps, d => Nudge(ref _quantity, d, true));

            ScreenKit.Line(body, Trans.Get("market_step_currency"), -255f, -36f, 16f, Accent, 500f);
            for (var i = 0; i < 3; i++)
            {
                var cur = i;
                ScreenKit.Btn(body, Trans.Get(MarketService.ResourceKey(MarketService.Resources[i])), 60f + i * 160f, -36f, 150f, 40f,
                    () => SetCurrency(cur), _currency == i ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
            }

            ScreenKit.Line(body, Trans.Get("market_price_asked"), -255f, -80f, 16f, Accent, 500f);
            Stepper(body, -118f, ScreenKit.Num(_price), ResourceSteps, d => Nudge(ref _price, d, false));

            var unit = _quantity > 0f ? _price / _quantity : 0f;
            var volume = SellingModule && _ctx.Hangar.Count > 0 ? ModuleVolume(_ctx.Hangar[_moduleIndex].type) * _quantity : Mathf.Ceil(_quantity);
            ScreenKit.Line(body, Trans.Get("market_unit_price") + " : <b>" + ScreenKit.Num(unit, 2) + "</b> " +
                                 Trans.Get(MarketService.ResourceKey(MarketService.Resources[_currency])) + " " + Trans.Get("market_per_unit"),
                -250f, -168f, 15f, UiKit.TextBright, 540f);
            ScreenKit.Line(body, Trans.Get("market_buyer_volume") + " : " + ScreenKit.Num(Mathf.Max(volume, Mathf.Ceil(_price))) + " m³",
                -250f, -195f, 14f, UiKit.TextDim, 540f);
            ScreenKit.Btn(body, Trans.Get("market_publish"), 300f, -182f, 440f, 52f, () => Run(Publish()), DiegeticUi.BtnStyle.Cyan,
                !_busy && _quantity > 0f && _price > 0f);
        }

        /// <summary>A module's freight volume as the server counts it (size × 500 per unit).</summary>
        static float ModuleVolume(string type)
        {
            var stats = GameConfig.ShipStats?[type];
            var size = stats != null ? FocusContext.AsFloat(stats["size"]) : 1f;
            return Mathf.Max(1f, size) * 500f;
        }

        void Stepper(RectTransform body, float y, string value, int[] steps, Action<int> onStep)
        {
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                var x = i < 3 ? -470f + i * 115f : 120f + (i - 3) * 115f;
                ScreenKit.Btn(body, (step > 0 ? "+" : "−") + ScreenKit.Num(Mathf.Abs(step)), x, y, 105f, 42f, () => onStep(step));
            }

            ScreenKit.Line(body, "<b>" + value + "</b>", -60f, y, 22f, UiKit.TextBright, 180f, TextAlignmentOptions.Center);
        }

        void Nudge(ref float value, int delta, bool quantity)
        {
            value = Mathf.Max(0f, value + delta);
            if (quantity)
                ClampDraft();
            CicCue.Ok(_counter.transform.position);
            RenderCounter();
        }

        void SetSellKind(int kind)
        {
            _sellKind = kind;
            _quantity = 0f;
            _moduleIndex = 0;
            ClampDraft();
            if (SellingModule && _quantity <= 0f && _ctx != null && _ctx.Hangar.Count > 0)
                _quantity = 1f;
            CicCue.Ok(_counter.transform.position);
            RenderCounter();
        }

        void StepModule(int delta)
        {
            if (_ctx == null || _ctx.Hangar.Count == 0)
                return;
            _moduleIndex = (_moduleIndex + delta + _ctx.Hangar.Count) % _ctx.Hangar.Count;
            _quantity = 1f;
            ClampDraft();
            CicCue.Ok(_counter.transform.position);
            RenderCounter();
        }

        void SetCurrency(int cur)
        {
            _currency = cur;
            CicCue.Ok(_counter.transform.position);
            RenderCounter();
        }

        async Task Publish()
        {
            if (_busy || _ctx == null || _quantity <= 0f || _price <= 0f)
                return;
            string itemKey;
            if (SellingModule)
            {
                if (_ctx.Hangar.Count == 0)
                {
                    _counterStatus.text = Trans.Get("market_pick_item");
                    return;
                }

                itemKey = _ctx.Hangar[_moduleIndex].type;
            }
            else
                itemKey = MarketService.Resources[_sellKind];

            _busy = true;
            _counterStatus.text = Trans.Get("market_publishing");
            try
            {
                var error = await MarketService.Create(_ctx.PlanetId, SellingModule ? "module" : "mineral", itemKey, _quantity,
                    MarketService.Resources[_currency], _price);
                if (error != null)
                {
                    CicCue.Fail(_counter.transform.position);
                    _counterStatus.text = ScreenKit.Verbatim(error);
                    return;
                }

                CicCue.Success(_counter.transform.position);
                _pit.ConsignPublished();
                _counterStatus.text = Trans.Get("market_published_ok");
                _quantity = 0f;
                _price = 0f;
                _mode = Mode.Convoys;
            }
            finally
            {
                _busy = false;
            }

            if (Inside)
                await LoadAll();
        }

        // Convoys & my offers ---------------------------------------------------

        void RenderConvoys(RectTransform body)
        {
            var now = FleetOrderGate.UnixNow();
            ScreenKit.Line(body, "<b>" + Trans.Format("market_transit_title", _ctx.Missions.Count) + "</b>", -20f, 196f, 17f, Accent, 1000f);
            if (_ctx.Missions.Count == 0)
                ScreenKit.Line(body, Trans.Get("market_no_transit"), -20f, 150f, 15f, UiKit.TextDim, 1000f);
            for (var i = 0; i < _ctx.Missions.Count && i < 3; i++)
            {
                var m = _ctx.Missions[i];
                var y = 152f - i * 58f;
                var lost = m.Status == "intercepted";
                var outbound = m.Status == "traveling_to";
                var color = lost ? UiKit.Danger : outbound ? UiKit.Cyan : UiKit.Ok;
                var state = lost ? Trans.Get("market_status_lost") : outbound ? Trans.Get("market_status_out") : Trans.Get("market_status_back");
                var fleetName = string.IsNullOrEmpty(m.FleetName) ? Trans.Format("market_fleet_num", m.FleetId) : ScreenKit.Verbatim(m.FleetName);
                ScreenKit.Line(body, "<b>" + fleetName + "</b>  <color=" + ScreenKit.Hex(color) + ">" + state + "</color>", -200f, y + 12f, 16f,
                    UiKit.TextBright, 640f);
                ScreenKit.Line(body, ScreenKit.Verbatim(m.OriginName) + " → " + ScreenKit.Verbatim(m.TargetName) + " · " +
                                     ScreenKit.Verbatim(m.ItemName) + " ×" + ScreenKit.Num(m.Quantity), -200f, y - 13f, 14f, UiKit.TextDim, 640f);
                if (lost)
                    ScreenKit.Line(body, "<b>" + Trans.Get("market_cargo_looted") + "</b>", 340f, y, 16f, UiKit.Danger, 340f, TextAlignmentOptions.Center);
                else
                {
                    var arrival = outbound ? m.OutboundArrival : m.InboundArrival;
                    ScreenKit.Line(body, Trans.Get("market_eta"), 340f, y + 12f, 12f, UiKit.TextDim, 340f, TextAlignmentOptions.Center);
                    var eta = ScreenKit.Line(body, ScreenKit.Remaining(arrival - now), 340f, y - 12f, 18f, UiKit.Amber, 340f,
                        TextAlignmentOptions.Center);
                    _countdowns.Add((eta, arrival));
                }
            }

            ScreenKit.Line(body, "<b>" + Trans.Format("market_my_listings", _ctx.MyListings.Count) + "</b>", -20f, -20f, 17f, Accent, 1000f);
            if (_ctx.MyListings.Count == 0)
                ScreenKit.Line(body, Trans.Get("market_none_here"), -20f, -62f, 15f, UiKit.TextDim, 1000f);
            for (var i = 0; i < _ctx.MyListings.Count && i < 3; i++)
            {
                var l = _ctx.MyListings[i];
                var y = -64f - i * 52f;
                var color = MarketDecor.CategoryColor(l.Category, l.ItemKey);
                ScreenKit.Line(body, "<b><color=" + ScreenKit.Hex(color) + ">" + ScreenKit.Verbatim(l.ItemName) + "</color></b> ×" +
                                     ScreenKit.Num(l.Quantity) + "  ·  " + Trans.Get("market_price_fixed") + " : <b>" + ScreenKit.Num(l.PriceAmount) +
                                     " " + Trans.Get(MarketService.ResourceKey(l.PriceCurrency)) + "</b>", -200f, y + 10f, 15f, UiKit.TextBright, 640f);
                ScreenKit.Line(body, Trans.Format("market_hold_buyer", ScreenKit.Num(RequiredHold(l))), -200f, y - 13f, 13f, UiKit.TextDim, 640f);
                var id = l.Id;
                var key = "cancel:" + id;
                var armed = _confirm == key;
                ScreenKit.Btn(body, armed ? Trans.Get("vr.diplo.confirm") : Trans.Get("market_cancel_recover"), 340f, y, 340f, 44f,
                    () => Run(CancelOffer(id)),
                    armed ? DiegeticUi.BtnStyle.Danger : DiegeticUi.BtnStyle.Ghost, !_busy);
            }
        }

        async Task CancelOffer(int listingId)
        {
            var key = "cancel:" + listingId;
            if (_confirm != key)
            {
                // Irreversible orders take two presses.
                _confirm = key;
                _confirmUntil = Time.time + ConfirmWindow;
                CicCue.Ok(_counter.transform.position);
                _counterStatus.text = Trans.Get("market_cancel_recover") + " — " + Trans.Get("vr.diplo.confirm");
                RenderCounter();
                return;
            }

            _confirm = null;
            if (_busy)
                return;
            _busy = true;
            try
            {
                var withdrawn = _ctx?.MyListings.Find(x => x.Id == listingId);
                var error = await MarketService.Cancel(listingId);
                if (error != null)
                {
                    CicCue.Fail(_counter.transform.position);
                    _counterStatus.text = ScreenKit.Verbatim(error);
                    return;
                }

                CicCue.Success(_counter.transform.position);
                if (withdrawn != null)
                    _pit.Recall(withdrawn.Category, withdrawn.ItemKey, ScreenKit.Verbatim(withdrawn.ItemName) + " ×" + ScreenKit.Num(withdrawn.Quantity));
                _counterStatus.text = Trans.Get("market_cancelled_ok");
            }
            finally
            {
                _busy = false;
            }

            if (Inside)
                await LoadAll();
        }

        // Purchase: the convoy composer ----------------------------------------------

        void RenderDispatch(RectTransform body)
        {
            var l = _buying;
            if (l == null)
            {
                _mode = Mode.Create;
                RenderCreate(body);
                return;
            }

            var o = _origin ?? _ctx;
            var distance = Distance(l, o);
            var color = MarketDecor.CategoryColor(l.Category, l.ItemKey);
            ScreenKit.Line(body, "<b>" + Trans.Get("market_convoy_title") + "</b>", -20f, 196f, 18f, Accent, 1000f);
            ScreenKit.Line(body, "<b><color=" + ScreenKit.Hex(color) + ">" + ScreenKit.Verbatim(l.ItemName) + "</color></b> ×" + ScreenKit.Num(l.Quantity) +
                                 "  ·  " + ScreenKit.Verbatim(l.SellerName) + " · " + ScreenKit.Verbatim(l.PlanetName) + " · " +
                                 ScreenKit.Num(distance, 1) + " " + Trans.Get("market_ly"), -20f, 160f, 16f, UiKit.TextBright, 1000f);

            var stock = o.Stock(l.PriceCurrency);
            var funds = stock >= l.PriceAmount;
            var currency = Trans.Get(MarketService.ResourceKey(l.PriceCurrency));
            ScreenKit.Line(body, Trans.Get("market_price_due") + " : <b>" + ScreenKit.Num(l.PriceAmount) + " " + currency + "</b>", -270f, 122f, 16f,
                UiKit.Amber, 500f);
            ScreenKit.Line(body, Trans.Format("market_your_balance", ScreenKit.Num(stock)) + "  <b>" +
                                 Trans.Get(funds ? "market_enough" : "market_funds_short") + "</b>", 250f, 122f, 15f,
                funds ? UiKit.Ok : UiKit.Danger, 520f);

            var need = RequiredHold(l);
            ScreenKit.Line(body, Trans.Format("market_hold_buyer", ScreenKit.Num(need)), -270f, 88f, 15f, UiKit.TextDim, 500f);

            // Port of departure: any of our worlds, its ships in orbit carry the payment out and the goods home.
            var many = _ctx.UserPlanets.Count > 1;
            ScreenKit.Line(body, Trans.Get("market_from") + " : <b>" + ScreenKit.Verbatim(o.PlanetName) + "</b>", 160f, 88f, 15f,
                _originLoading ? UiKit.TextDim : UiKit.TextBright, 330f);
            ScreenKit.Btn(body, "‹", 370f, 88f, 60f, 36f, () => Run(StepOrigin(-1)), DiegeticUi.BtnStyle.Ghost, many && !_originLoading);
            ScreenKit.Btn(body, "›", 440f, 88f, 60f, 36f, () => Run(StepOrigin(1)), DiegeticUi.BtnStyle.Ghost, many && !_originLoading);

            var haulers = new List<MarketHauler>();
            foreach (var h in o.Haulers)
                if (!h.IsStation)
                    haulers.Add(h);
            if (haulers.Count == 0)
            {
                ScreenKit.Line(body, Trans.Format("market_no_orbit_of", ScreenKit.Verbatim(o.PlanetName)), -20f, 40f, 17f, UiKit.Danger, 1000f);
                ScreenKit.Para(body, Trans.Get("market_orbit_none_hint"), -20f, -20f, 15f, UiKit.TextDim, 1000f, 80f);
            }
            else
            {
                ScreenKit.Line(body, Trans.Get("market_pick_orbit"), -20f, 54f, 15f, Accent, 1000f);
                for (var i = 0; i < haulers.Count && i < 6; i++)
                {
                    var h = haulers[i];
                    var on = _haulers.Contains(h.Id);
                    var x = i % 2 == 0 ? -265f : 265f;
                    var y = 12f - i / 2 * 50f;
                    var id = h.Id;
                    var b = ScreenKit.Btn(body, "<b>" + ScreenKit.Verbatim(h.Name) + "</b>  " + Trans.Format("market_hold_free", ScreenKit.Num(h.CargoFree)),
                        x, y, 510f, 44f, () => ToggleHauler(id), on ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost, h.CargoFree > 0f);
                    b.GetComponentInChildren<TMP_Text>().richText = true;
                }
            }

            var (picked, slowest, hyper) = Selection(haulers);
            var enough = picked >= need;
            ScreenKit.Gauge(body, -250f, -150f, 520f, need > 0f ? picked / need : 1f, enough ? UiKit.Ok : UiKit.Amber,
                ScreenKit.Num(picked) + " / " + ScreenKit.Num(need) + " m³");
            if (_haulers.Count > 0)
            {
                var leg = LegSeconds(distance, slowest, hyper);
                ScreenKit.Line(body, Trans.Get("market_travel_time") + " : " + Trans.Format("market_eta_legs", Core.Holo.TravelPlanner.TimeText(leg).TrimStart('~'),
                    Core.Holo.TravelPlanner.TimeText(leg * 2f).TrimStart('~')), -250f, -188f, 14f, UiKit.TextDim, 520f);
            }

            ScreenKit.Btn(body, Trans.Get("back"), 120f, -168f, 160f, 50f, () => SetMode(Mode.Create));
            // Greyed, it says why (as the web button does).
            var label = !funds ? "market_funds_short" : !enough || _haulers.Count == 0 ? "market_insufficient_cargo" : "market_confirm_dispatch";
            ScreenKit.Btn(body, Trans.Get(label), 380f, -168f, 330f, 50f, () => Run(Dispatch()),
                DiegeticUi.BtnStyle.Cyan, !_busy && !_originLoading && funds && enough && _haulers.Count > 0);
        }

        (float cargo, float slowest, bool hyper) Selection(List<MarketHauler> haulers)
        {
            var cargo = 0f;
            var slowest = float.MaxValue;
            var hyper = true;
            foreach (var h in haulers)
            {
                if (!_haulers.Contains(h.Id))
                    continue;
                cargo += h.CargoFree;
                slowest = Mathf.Min(slowest, h.Speed);
                hyper &= h.HasHyperdrive;
            }

            return (cargo, slowest == float.MaxValue ? 1f : slowest, hyper);
        }

        /// <summary>One leg of the convoy as the server times it (DispatchTradeConvoy): interstellar + in-system approach.</summary>
        static float LegSeconds(float distance, float speed, bool hyper)
        {
            var cap = GameConfig.SublightSpeedCap > 0f ? GameConfig.SublightSpeedCap : 10f;
            var v = Mathf.Max(1, (int)(hyper ? speed : Mathf.Min(speed, cap)));
            var perDistance = GameConfig.TravelSecondsPerDistance > 0f ? GameConfig.TravelSecondsPerDistance : 120f;
            var min = GameConfig.TravelDurationMin > 0f ? GameConfig.TravelDurationMin : 30f;
            var interstellar = (int)Mathf.Max(min, distance * perDistance / v);
            return Mathf.Max(30f, interstellar + (int)(600f / v));
        }

        void ToggleHauler(int id)
        {
            if (!_haulers.Remove(id))
                _haulers.Add(id);
            CicCue.Ok(_counter.transform.position);
            RenderCounter();
        }

        async Task Dispatch()
        {
            var l = _buying;
            var origin = _origin ?? _ctx;
            if (_busy || l == null || origin == null || _haulers.Count == 0)
                return;
            _busy = true;
            _counterStatus.text = Trans.Get("market_launching");
            var ids = new List<int>(_haulers);
            try
            {
                var (ok, error, ships) = await MarketService.Dispatch(l.Id, origin.PlanetId, ids);
                if (!ok)
                {
                    CicCue.Fail(_counter.transform.position);
                    _counterStatus.text = ScreenKit.Verbatim(error);
                    return;
                }

                _pit.Launch(ships);
                _counterStatus.text = Trans.Format("market_launched", ships);
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Ops, "marketDispatch", 2, l.PlanetName);
                _buying = null;
                _origin = null;
                _haulers.Clear();
                _selectedListing = 0;
                _mode = Mode.Convoys;
            }
            finally
            {
                _busy = false;
            }

            if (Inside)
                await LoadAll();
        }
    }
}
