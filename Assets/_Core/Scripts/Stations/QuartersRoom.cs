using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// The captain's quarters — web EmpireHubUI + ProgressionWindowUI + ShopWindowUI + ActivityWindowUI as a
    /// place: a cabin over our ship with a panoramic bay. The desk console runs the empire: identity
    /// (RenameEmpire with a rename token, UpdateEmpireFlag), government (SetAuthority, a reconfiguration token
    /// to change it; AddEmpirePolicy up to the limit), politics (GetPolitics / SetPolitics, free), species
    /// (UpdateSpecy — name free, type or traits one token; traits always sent whole) and the captain's log
    /// (GetActivity). The port console follows progression (GetProgressionObjectives, GetAchievements,
    /// GetEventData), mirrored by the trophy wall; the starboard console is the Nova shop (GetShopData,
    /// BuyShopItem in two presses, EquipShopItem, packs and history). The equipped title is on the desk
    /// nameplate and the showcase, the equipped fleet colour on the showcase ring and on our hulls outside.
    /// The Comms console sits on the starboard wall while the captain is here.
    /// </summary>
    public sealed class QuartersRoom : MonoBehaviour
    {
        enum EmpireTab { Identity, Government, Politics, Species, Log }
        enum ProgTab { Objectives, Achievements, Event }
        enum ShopTab { Boosters, Consumables, Cosmetics, Titles, Nova }

        static readonly Vector3 WorldOrigin = new(160f, -3000f, -200f);
        static readonly Vector3 Stand = Vector3.zero;
        public static readonly Color Accent = new(0.95f, 0.64f, 0.32f, 1f);
        static readonly Vector2 ScreenSize = new(1.1f, 0.7f);
        const float DeskRecline = 52f;
        const float ProgressEvery = 30f;
        const float LogEvery = 10f;
        const int AchievementsPerPage = 6;
        const int ItemsPerPage = 4;
        const int LogPerPage = 8;
        static readonly string[] Palette =
        {
            "#001f3f", "#0074d9", "#7fdbff", "#39cccc", "#2ecc40", "#01ff70", "#ffdc00",
            "#ff851b", "#ff4136", "#f012be", "#b10dc9", "#ffffff", "#aaaaaa", "#111111"
        };
        static readonly string[] AchievementCategories =
            { "all", "expansion", "science", "fleet", "combat", "infrastructure", "diplomacy", "prestige" };

        public static QuartersRoom Instance { get; private set; }
        public static bool Inside { get; private set; }

        CicArtKit _art;
        QuartersDecor.Refs _decor;
        PokeButton _commsWake;

        HoloScreen _empire;
        RectTransform _empireBody;
        TMP_Text _empireStatus;
        Button[] _empireTabs;
        GameObject _nameGroup;
        TMP_InputField _nameField;
        GameObject _specyGroup;
        TMP_InputField _specyField;

        HoloScreen _prog;
        RectTransform _progBody;
        TMP_Text _progStatus;
        Button[] _progTabs;

        HoloScreen _shop;
        RectTransform _shopBody;
        TMP_Text _shopStatus;
        Button[] _shopTabs;

        TwoPress _empireConfirm;
        TwoPress _shopConfirm;

        EmpireTab _empireTab;
        ProgTab _progTab;
        ShopTab _shopTab;
        int _politicsCategory;
        int _achievementCategory;
        int _achievementPage;
        int _itemPage;
        int _logPage;
        bool _busy;
        float _progressAt;
        float _logAt;

        // Server state
        JObject _me;
        JArray _authorities;
        JArray _policies;
        int _maxPolicies = 2;
        JArray _speciesTypes;
        JArray _traits;
        JObject _politics;
        JObject _shopData;
        JObject _objectives;
        JObject _achievements;
        JObject _event;
        JObject _packs;
        JArray _topups;
        readonly List<JObject> _log = new();
        int _logLast;

        // Edits in progress
        readonly FlagSpec _flag = new();
        Texture2D _flagTex;
        Material _flagMat;
        int _authority;
        int _specyType;
        readonly List<int> _pos = new();
        readonly List<int> _neg = new();

        // ── Build ─────────────────────────────────────────────────────────────────

        public static QuartersRoom Build(CicArtKit art)
        {
            var go = new GameObject("CaptainQuarters");
            go.transform.position = WorldOrigin;
            var room = go.AddComponent<QuartersRoom>();
            room._art = art;
            room._decor = QuartersDecor.Build(go.transform, art, Accent);
            room.BuildDoorAndLight();
            room.BuildScreens();
            go.SetActive(false);
            return room;
        }

        void Awake()
        {
            Instance = this;
            _empireConfirm = new TwoPress(RenderEmpire);
            _shopConfirm = new TwoPress(RenderShop);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_flagTex != null)
                Destroy(_flagTex);
            if (_flagMat != null)
                Destroy(_flagMat);
        }

        void BuildDoorAndLight()
        {
            var fill = new GameObject("CabinLight").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.transform.localPosition = new Vector3(0f, QuartersDecor.Height - 0.4f, 1.2f);
            fill.type = LightType.Point;
            fill.range = 9f;
            fill.intensity = 1.2f;
            fill.color = new Color(1f, 0.88f, 0.72f);
            fill.shadows = LightShadows.None;

            RoomDoor.Build(transform, "DoorToBridge", new Vector3(2.4f, 0f, QuartersDecor.Back + 0.12f), 0f,
                Trans.Get("vr.quarters.leave"), UiKit.Amber, _art, () => Inside, () => AsyncTap.Run(Leave()));

            _commsWake = PokeButton.Create(_decor.CommsWakeMount, "CommsWake", Trans.Get("vr.comms.title"), Vector3.zero,
                Quaternion.identity, new Vector2(0.28f, 0.07f), new Color(0.55f, 0.85f, 0.45f), () =>
                {
                    if (CommsConsole.Instance != null && !CommsConsole.Instance.IsOpen)
                        CommsConsole.Instance.Dock(_decor.CommsMount);
                });
        }

        void BuildScreens()
        {
            // Desk: the empire, low and reclined so the bay stays free above it.
            _empire = HoloScreen.Create(transform, "EmpireConsole", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.quarters.empire"));
            _empire.SetAccent(Accent, 0.5f);
            GateRoomDecor.SeatOnArm(_empire.transform, _decor.EmpireMount, ScreenSize.y, DeskRecline);
            _empireTabs = ScreenKit.Tabs(_empire.Content,
                new[] { "empireHub_identity", "empireHub_authority", "empireHub_politics", "vr.quarters.species", "vr.quarters.log" }, 245f,
                i => SetEmpireTab((EmpireTab)i));
            _empireBody = ScreenKit.Body(_empire.Content);
            _empireStatus = DiegeticUi.HoloLabel(_empire.Content, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f),
                18f, DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);

            // Typing fields live outside the rebuilt body so a refresh never steals the keyboard.
            _nameGroup = Group(_empire, "NameBar");
            _nameField = DiegeticUi.HoloField(_nameGroup.transform, "EmpireName", Trans.Get("empire-name"), new Vector2(-200f, 140f),
                new Vector2(560f, 46f), TouchScreenKeyboardType.Default);
            _nameField.characterLimit = 40;
            DiegeticUi.HoloButton(_nameGroup.transform, Trans.Get("rename"), new Vector2(250f, 140f), new Vector2(300f, 46f),
                () => AsyncTap.Run(Rename()), DiegeticUi.BtnStyle.Cyan);

            _specyGroup = Group(_empire, "SpecyBar");
            _specyField = DiegeticUi.HoloField(_specyGroup.transform, "SpecyName", Trans.Get("name"), new Vector2(-200f, 185f),
                new Vector2(560f, 44f), TouchScreenKeyboardType.Default);
            _specyField.characterLimit = 40;
            DiegeticUi.HoloButton(_specyGroup.transform, Trans.Get("vr.diplo.save"), new Vector2(250f, 185f), new Vector2(300f, 44f),
                () => AsyncTap.Run(SaveSpecy(false)), DiegeticUi.BtnStyle.Cyan);

            _prog = HoloScreen.Create(transform, "ProgressConsole", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("progression"));
            _prog.SetAccent(Accent, 0.45f);
            GateRoomDecor.SeatOnArm(_prog.transform, _decor.ProgressMount, ScreenSize.y);
            _progTabs = ScreenKit.Tabs(_prog.Content, new[] { "vr.quarters.objectives", "achievements", "vr.quarters.event" }, 245f,
                i => SetProgTab((ProgTab)i));
            _progBody = ScreenKit.Body(_prog.Content);
            _progStatus = DiegeticUi.HoloLabel(_prog.Content, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);

            _shop = HoloScreen.Create(transform, "ShopConsole", ScreenSize, Vector3.zero, Quaternion.identity, Trans.Get("shop"));
            _shop.SetAccent(Accent, 0.45f);
            GateRoomDecor.SeatOnArm(_shop.transform, _decor.ShopMount, ScreenSize.y);
            _shopTabs = ScreenKit.Tabs(_shop.Content,
                new[] { "shopBoosters", "shopConsumables", "shopCosmetics", "shopTitles", "nova" }, 245f,
                i => SetShopTab((ShopTab)i));
            _shopBody = ScreenKit.Body(_shop.Content);
            _shopStatus = DiegeticUi.HoloLabel(_shop.Content, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);

            _flagTex = FlagPainter.Paint(_flag);
            _flagMat = new Material(_art.Lit(_flagTex, Color.white, 0f)) { name = "SU_QuartersFlag" };
            _flagMat.mainTexture = _flagTex;
            _decor.ShowcaseFlag.sharedMaterial = _flagMat;
            foreach (var r in _decor.ShowcaseFlag.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = _flagMat;
        }

        static GameObject Group(HoloScreen screen, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(screen.Content, false);
            go.GetComponent<RectTransform>().sizeDelta = screen.PixelSize;
            go.SetActive(false);
            return go;
        }

        // ── Enter / leave ─────────────────────────────────────────────────────────

        public async Task Enter()
        {
            if (DiplomacyRoom.InRoomBeyondCorridor || Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            if (CorridorRoom.Inside)
                CorridorRoom.Instance.Depart();
            // Over our ship, the bay (ahead of the stand) facing the star.
            RoomPlacement.OverShip(transform, Vector3.forward);
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
            _empireConfirm.Reset();
            _shopConfirm.Reset();
            CommsConsole.Instance?.Dock(_decor.CommsMount);
            await LoadAll();
            await fade.FadeIn();
            CicCue.Ok(transform.position + Vector3.up);
        }

        async Task Leave()
        {
            if (!Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            Inside = false;
            CommsConsole.Instance?.Undock();
            // Out into the corridor, in front of this room's door.
            CorridorRoom.ReturnPlayer(CorridorRoom.Slot.QuartersStarboard);

            gameObject.SetActive(false);
            await fade.FadeIn();
        }

        // ── Data ──────────────────────────────────────────────────────────────────

        async Task LoadAll()
        {
            var me = AuthManager.Ensure().FetchMe();
            var au = ActionJs.Get("GetAuthorities");
            var st = ActionJs.Get("GetSpeciesTypes");
            var tr = ActionJs.Get("GetSpeciesTraits");
            var cfg = ActionJs.Get("GetConfigs");
            var pol = ActionJs.Get("GetPolitics");
            var shop = ActionJs.Get("GetShopData");
            var packs = ActionJs.Get("GetNovaTopupPacks");
            var hist = ActionJs.Get("GetNovaTopupHistory");
            await Task.WhenAll(me, au, st, tr, cfg, pol, shop, packs, hist);
            _authorities = ScreenKit.Array(au.Result) ?? _authorities;
            _speciesTypes = ScreenKit.Array(st.Result) ?? _speciesTypes;
            _traits = ScreenKit.Array(tr.Result) ?? _traits;
            var config = ScreenKit.Object(cfg.Result);
            if (config != null)
            {
                _policies = config["policies"] as JArray ?? config["ethics"] as JArray ?? _policies;
                var max = FocusContext.AsInt(config["empire"]?["maxPolicies"]);
                if (max > 0)
                    _maxPolicies = max;
            }

            _politics = ScreenKit.Object(pol.Result) ?? _politics;
            _shopData = ScreenKit.Object(shop.Result) ?? _shopData;
            _packs = ScreenKit.Object(packs.Result) ?? _packs;
            _topups = ScreenKit.Array(hist.Result) ?? _topups;
            AdoptEmpire();
            await LoadProgress();
            _log.Clear();
            _logLast = 0;
            await LoadLog();
            RenderAll();
        }

        /// <summary>Copy the fresh GetMeEmpire into the edit state (flag, authority, species) and the decor.</summary>
        void AdoptEmpire()
        {
            _me = AuthManager.Ensure().Empire;
            if (_me == null)
                return;
            var spec = FlagSpec.FromJson(FocusContext.AsString(_me["flag"]));
            _flag.Bg = spec.Bg;
            for (var i = 0; i < 3; i++)
            {
                _flag.Shape[i] = spec.Shape[i];
                _flag.Color[i] = spec.Color[i];
            }

            FlagPainter.Paint(_flag, _flagTex);
            _authority = FocusContext.AsInt(_me["authority_id"]);
            var specy = _me["specy"];
            _specyType = FocusContext.AsInt(specy?["type_id"]);
            _pos.Clear();
            _neg.Clear();
            if (specy?["traits"] is JArray traits)
                foreach (var t in traits)
                    (FocusContext.AsInt(t["type"]) == 1 ? _pos : _neg).Add(FocusContext.AsInt(t["id"]));
            if (!_nameField.isFocused)
                _nameField.text = FocusContext.AsString(_me["name"]);
            if (!_specyField.isFocused)
                _specyField.text = FocusContext.AsString(specy?["name"]);
            SyncDecor();
        }

        async Task LoadProgress()
        {
            var obj = ActionJs.Get("GetProgressionObjectives");
            var ach = ActionJs.Get("GetAchievements");
            var ev = ActionJs.Get("GetEventData");
            await Task.WhenAll(obj, ach, ev);
            _objectives = ScreenKit.Object(obj.Result) ?? _objectives;
            _achievements = ScreenKit.Object(ach.Result) ?? _achievements;
            _event = ScreenKit.Object(ev.Result) ?? _event;
            _progressAt = Time.unscaledTime + ProgressEvery;
            SyncTrophies();
        }

        /// <summary>GetActivity from the last id seen (0 = the latest 50), newest first.</summary>
        async Task LoadLog()
        {
            var res = await ActionJs.Get("GetActivity", new Dictionary<string, string> { { "lastid", _logLast.ToString() } });
            _logAt = Time.unscaledTime + LogEvery;
            var rows = ScreenKit.Array(res);
            if (rows == null || rows.Count == 0)
                return;
            var fresh = new List<JObject>();
            foreach (var r in rows)
                if (r is JObject o && FocusContext.AsInt(o["id"]) > _logLast)
                    fresh.Add(o);
            fresh.Sort((a, b) => FocusContext.AsInt(b["id"]).CompareTo(FocusContext.AsInt(a["id"])));
            _log.InsertRange(0, fresh);
            if (_log.Count > 200)
                _log.RemoveRange(200, _log.Count - 200);
            if (_log.Count > 0)
                _logLast = FocusContext.AsInt(_log[0]["id"]);
            if (Inside && _empireTab == EmpireTab.Log)
                RenderEmpire();
        }

        async Task RefreshShop()
        {
            var shop = await ActionJs.Get("GetShopData");
            _shopData = ScreenKit.Object(shop) ?? _shopData;
        }

        // ── Orders ────────────────────────────────────────────────────────────────

        /// <summary>Send an order, then re-read the empire (and what else it touches); status on its screen.</summary>
        async Task<ApiResult> Order(TMP_Text status, string action, Dictionary<string, string> args, string okText,
            bool shop = false, bool progress = false)
        {
            if (_busy)
                return default;
            _busy = true;
            try
            {
                var res = await ActionJs.Get(action, args);
                if (!res.Ok)
                {
                    SetStatus(status, string.IsNullOrEmpty(res.Error) ? Trans.Get("vr.common.error") : res.Error, true);
                    CicCue.Fail(status.transform.position);
                    return res;
                }

                SetStatus(status, okText);
                CicCue.Ok(status.transform.position);
                await AuthManager.Ensure().FetchMe();
                AdoptEmpire();
                if (shop)
                    await RefreshShop();
                if (progress)
                    await LoadProgress();
                return res;
            }
            finally
            {
                _busy = false;
                RenderAll();
            }
        }

        static Dictionary<string, string> Args(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (var i = 0; i + 1 < kv.Length; i += 2)
                d[kv[i]] = kv[i + 1];
            return d;
        }

        async Task Rename()
        {
            var name = (_nameField.text ?? string.Empty).Trim();
            if (name.Length == 0)
                return;
            await Order(_empireStatus, "RenameEmpire", Args("name", name), Trans.Get("empireRenamed"));
        }

        async Task SaveFlag()
        {
            await Order(_empireStatus, "UpdateEmpireFlag", Args("flag", _flag.ToJson()), Trans.Get("vr.quarters.flagSaved"));
        }

        async Task ApplyAuthority()
        {
            await Order(_empireStatus, "SetAuthority", Args("authority", _authority.ToString(CultureInfo.InvariantCulture)),
                Trans.Get("vr.quarters.authoritySet"));
        }

        async Task AddPolicy(int id)
        {
            await Order(_empireStatus, "AddEmpirePolicy", Args("policy", id.ToString(CultureInfo.InvariantCulture)),
                Trans.Get("vr.quarters.policyAdded"), progress: true);
        }

        async Task SetPolitic(string category, string option)
        {
            var res = await Order(_empireStatus, "SetPolitics", Args("category", category, "option", option),
                Trans.Get("vr.quarters.politicsSet"));
            if (!res.Ok)
                return;
            var pol = await ActionJs.Get("GetPolitics");
            _politics = ScreenKit.Object(pol) ?? _politics;
            RenderEmpire();
        }

        /// <summary>
        /// UpdateSpecy with name, type and the whole trait set every time: the server replaces the traits with
        /// whatever it is given (an omitted set would be rewritten from its own rows — docs/PARITY.md).
        /// </summary>
        async Task SaveSpecy(bool withTraits)
        {
            var id = FocusContext.AsString(_me?["id"]);
            if (id.Length == 0)
                return;
            var traits = new List<int>(_pos);
            traits.AddRange(_neg);
            if (withTraits && (_pos.Count != 2 || _neg.Count != 2))
            {
                SetStatus(_empireStatus, Trans.Get("vr.quarters.traitsNeeded"), true);
                return;
            }

            if (!withTraits)
            {
                // Name only: resend the current type and traits unchanged (free).
                var specy = _me["specy"];
                traits.Clear();
                if (specy?["traits"] is JArray current)
                    foreach (var t in current)
                        traits.Add(FocusContext.AsInt(t["id"]));
            }

            var type = withTraits ? _specyType : FocusContext.AsInt(_me["specy"]?["type_id"]);
            await Order(_empireStatus, "UpdateSpecy", Args(
                "empire", id,
                "name", (_specyField.text ?? string.Empty).Trim(),
                "type_id", type.ToString(CultureInfo.InvariantCulture),
                "traits", string.Join(",", traits)), Trans.Get("vr.quarters.specySaved"));
        }

        async Task Buy(string item)
        {
            var res = await Order(_shopStatus, "BuyShopItem", Args("item", item), Trans.Get("shopPurchaseComplete"), true, true);
            if (!res.Ok)
                return;
            var o = ScreenKit.Object(res);
            if (FocusContext.AsBool(o?["leveledUp"]))
            {
                SetStatus(_shopStatus, Trans.Get("shopLevelUpCongrats"));
                CicCue.Victory(_shop.transform.position);
            }
        }

        async Task Equip(string item)
        {
            var res = await Order(_shopStatus, "EquipShopItem", Args("item", item), Trans.Get("vr.quarters.equipped"), true, true);
            if (res.Ok)
                ApplyFleetColour();
        }

        /// <summary>The equipped fleet colour on our hulls outside (next rebuild of the shared exterior).</summary>
        void ApplyFleetColour()
        {
            if (ShipHullBuilder.SetOwnAccent(FocusContext.AsString(_me?["fleetColor"])))
                FindFirstObjectByType<SystemExterior>()?.RebuildAll();
        }

        // ── Decor follows the empire ──────────────────────────────────────────────

        void SyncDecor()
        {
            if (_me == null)
                return;
            var title = FocusContext.AsString(_me["equippedTitle"]);
            var leader = (FocusContext.AsString(_me["leaderTitle"]) + " " + FocusContext.AsString(_me["leaderName"])).Trim();
            _decor.Nameplate.text = (title.Length > 0 ? "<color=#ffd27a>" + Trans.Get("shopItemName_" + title) + "</color>  ·  " : string.Empty) +
                                    ScreenKit.Verbatim(leader.Length > 0 ? leader : FocusContext.AsString(_me["name"]));
            _decor.ShowcaseTitle.text = title.Length > 0
                ? Trans.Get("shopItemName_" + title)
                : ScreenKit.Verbatim(FocusContext.AsString(_me["name"]));
            var colour = FlagPainter.Hex("#" + FocusContext.AsString(_me["fleetColor"]), Accent);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", colour);
            block.SetColor("_Emission", colour);
            _decor.ShowcaseRing.SetPropertyBlock(block);
        }

        void SyncTrophies()
        {
            var all = _achievements?["all"] as JObject;
            var unlocked = _achievements?["unlocked"] as JArray;
            var got = new HashSet<string>();
            var order = new List<string>();
            if (unlocked != null)
                foreach (var u in unlocked)
                {
                    var key = FocusContext.AsString(u["achievement_key"]);
                    if (got.Add(key))
                        order.Add(key);
                }

            if (all != null)
                foreach (var p in all.Properties())
                    if (!got.Contains(p.Name))
                        order.Add(p.Name);

            _decor.TrophyHeader.text = Trans.Get("achievements") + "  <size=70%>" + got.Count + " / " + (all?.Count ?? 0) + "</size>";
            var block = new MaterialPropertyBlock();
            for (var i = 0; i < QuartersDecor.Trophies; i++)
            {
                var on = i < order.Count;
                var lit = on && got.Contains(order[i]);
                block.Clear();
                block.SetColor(UiKit.AccentId, lit ? Accent : new Color(0.25f, 0.3f, 0.35f));
                block.SetFloat(UiKit.AccentMulId, lit ? 1.1f : 0.15f);
                _decor.Plaques[i].SetPropertyBlock(block);
                _decor.Emblems[i].enabled = lit;
                _decor.TrophyNames[i].text = on ? AchievementName(order[i], all?[order[i]]) : string.Empty;
                _decor.TrophyNames[i].color = lit ? new Color(1f, 0.88f, 0.65f) : new Color(0.5f, 0.6f, 0.66f);
            }
        }

        static string AchievementName(string key, JToken def)
        {
            var t = Trans.Get("achievementName_" + key);
            return t != "achievementName_" + key ? t : FocusContext.AsString(def?["name"]);
        }

        static string AchievementDesc(string key, JToken def)
        {
            var t = Trans.Get("achievementDesc_" + key);
            return t != "achievementDesc_" + key ? t : FocusContext.AsString(def?["desc"]);
        }

        // ── Render ────────────────────────────────────────────────────────────────

        void RenderAll()
        {
            RenderEmpire();
            RenderProgress();
            RenderShop();
        }

        static void SetStatus(TMP_Text status, string text, bool error = false)
        {
            status.text = text ?? string.Empty;
            status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        TMP_Text L(RectTransform body, string text, float x, float y, float size, Color color, float width,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) =>
            ScreenKit.Line(body, text, x, y, size, color, width, align);

        void SetEmpireTab(EmpireTab tab)
        {
            _empireTab = tab;
            _empireConfirm.Reset();
            SetStatus(_empireStatus, string.Empty);
            RenderEmpire();
        }

        void RenderEmpire()
        {
            ScreenKit.LightTabs(_empireTabs, (int)_empireTab);
            ScreenKit.Clear(_empireBody);
            _nameGroup.SetActive(_empireTab == EmpireTab.Identity && _me != null);
            _specyGroup.SetActive(_empireTab == EmpireTab.Species && _me != null);
            if (_me == null)
            {
                L(_empireBody, Trans.Get("Loading"), 0f, 60f, 20f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            switch (_empireTab)
            {
                case EmpireTab.Identity:
                    RenderIdentity();
                    break;
                case EmpireTab.Government:
                    RenderGovernment();
                    break;
                case EmpireTab.Politics:
                    RenderPolitics();
                    break;
                case EmpireTab.Species:
                    RenderSpecies();
                    break;
                default:
                    RenderLog();
                    break;
            }
        }

        string Tokens() => Trans.Format("vr.quarters.tokens", FocusContext.AsInt(_me["renameTokens"]),
            FocusContext.AsInt(_me["reconfigTokens"]));

        void RenderIdentity()
        {
            L(_empireBody, "<b>" + ScreenKit.Verbatim(FocusContext.AsString(_me["name"])) + "</b>  <size=75%><color=#9fdcff>" +
                           Trans.Get("level") + " " + FocusContext.AsInt(_me["level"]) + "</color></size>", -10f, 190f, 22f,
                UiKit.TextBright, 620f);
            L(_empireBody, Tokens(), 330f, 190f, 15f, UiKit.Amber, 380f, TextAlignmentOptions.MidlineRight);

            // Flag: background and three layers (web empireFlag.js), preview on the right.
            L(_empireBody, Trans.Get("vr.create.flagBackground"), -440f, 80f, 15f, UiKit.TextBright, 160f);
            Swatches(-330f, 80f, _flag.Bg, hex => _flag.Bg = hex);
            for (var i = 0; i < 3; i++)
            {
                var slot = i;
                var y = 24f - i * 56f;
                var shapeIndex = Mathf.Max(0, System.Array.IndexOf(FlagSpec.Shapes, _flag.Shape[slot]));
                ScreenKit.Btn(_empireBody, "‹", -500f, y, 40f, 34f, () =>
                {
                    _flag.Shape[slot] = FlagSpec.Shapes[(shapeIndex - 1 + FlagSpec.Shapes.Length) % FlagSpec.Shapes.Length];
                    FlagPainter.Paint(_flag, _flagTex);
                    RenderEmpire();
                });
                L(_empireBody, Trans.Get("flagShape_" + _flag.Shape[slot]), -420f, y, 15f, UiKit.Cyan, 110f, TextAlignmentOptions.Center);
                ScreenKit.Btn(_empireBody, "›", -340f, y, 40f, 34f, () =>
                {
                    _flag.Shape[slot] = FlagSpec.Shapes[(shapeIndex + 1) % FlagSpec.Shapes.Length];
                    FlagPainter.Paint(_flag, _flagTex);
                    RenderEmpire();
                });
                Swatches(-290f + 0f, y, _flag.Color[slot], hex => _flag.Color[slot] = hex, 0.85f);
            }

            var preview = new GameObject("FlagPreview", typeof(RectTransform), typeof(RawImage));
            preview.transform.SetParent(_empireBody, false);
            var prt = preview.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(200f, 133f);
            prt.anchoredPosition = new Vector2(420f, 30f);
            var raw = preview.GetComponent<RawImage>();
            raw.texture = _flagTex;
            raw.raycastTarget = false;
            ScreenKit.Btn(_empireBody, Trans.Get("vr.quarters.saveFlag"), 420f, -80f, 220f, 46f, () => AsyncTap.Run(SaveFlag()),
                DiegeticUi.BtnStyle.Cyan);
            L(_empireBody, Trans.Get("vr.quarters.renameHint"), 0f, -185f, 14f, UiKit.TextDim, 1040f, TextAlignmentOptions.Center);
        }

        void Swatches(float x0, float y, string current, Action<string> pick, float scale = 1f)
        {
            for (var i = 0; i < Palette.Length; i++)
            {
                var hex = Palette[i];
                var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_empireBody, false);
                var rt = go.GetComponent<RectTransform>();
                var selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                rt.sizeDelta = (selected ? new Vector2(36f, 36f) : new Vector2(28f, 28f)) * scale;
                rt.anchoredPosition = new Vector2(x0 + i * 38f * scale, y);
                go.GetComponent<Image>().color = FlagPainter.Hex(hex, Color.white);
                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    CicCue.Ok(go.transform.position);
                    pick(hex);
                    FlagPainter.Paint(_flag, _flagTex);
                    RenderEmpire();
                });
            }
        }

        void RenderGovernment()
        {
            var current = FocusContext.AsInt(_me["authority_id"]);
            var tokens = FocusContext.AsInt(_me["reconfigTokens"]);
            L(_empireBody, "<b>" + Trans.Get("authority") + "</b>", -520f + 150f, 190f, 18f, DiegeticUi.CyanDim, 300f);
            L(_empireBody, Tokens(), 330f, 190f, 15f, UiKit.Amber, 380f, TextAlignmentOptions.MidlineRight);
            if (_authorities != null)
                for (var i = 0; i < _authorities.Count; i++)
                {
                    var id = FocusContext.AsInt(_authorities[i]["id"]);
                    var picked = id == _authority;
                    ScreenKit.Btn(_empireBody, Trans.Get(FocusContext.AsString(_authorities[i]["name"])) + (id == current ? "  •" : string.Empty),
                        -395f + i % 4 * 262f, 145f - i / 4 * 50f, 252f, 44f, () =>
                        {
                            _authority = id;
                            _empireConfirm.Reset();
                            RenderEmpire();
                        }, picked ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                }

            // Changing an authority already chosen costs a reconfiguration token (the first choice is free).
            if (_authority != current && _authority > 0)
            {
                var costs = current > 0;
                var go = _empireConfirm.Make(_empireBody, "authority", Trans.Get("vr.quarters.applyAuthority") +
                                                                    (costs ? "  <size=80%>(" + Trans.Get("vr.quarters.oneToken") + ")</size>" : string.Empty),
                    330f, 50f, 380f, 44f, ApplyAuthority, DiegeticUi.BtnStyle.Cyan);
                go.interactable = !costs || tokens > 0;
                if (costs && tokens == 0)
                    L(_empireBody, Trans.Get("reconfigLockNotice"), -190f, 50f, 13f, UiKit.Amber, 620f);
            }

            // Ethics: the empire's, and those it can still adopt (no way back: two presses).
            var have = new HashSet<int>();
            if (_me["policies"] is JArray mine)
                foreach (var p in mine)
                    have.Add(FocusContext.AsInt(p["policy_id"]));
            var max = _maxPolicies + (FocusContext.AsInt(_me["level"]) >= 10 ? 1 : 0);
            L(_empireBody, "<b>" + Trans.Get("ethics") + "</b>  <size=80%>" + have.Count + " / " + max + "</size>", -370f, 0f, 18f,
                DiegeticUi.CyanDim, 300f);
            if (_policies == null)
                return;
            var k = 0;
            foreach (var p in _policies)
            {
                var id = FocusContext.AsInt(p["id"]);
                var name = FocusContext.AsString(p["name"]);
                var on = have.Contains(id);
                var x = -355f + k % 3 * 355f;
                var y = -45f - k / 3 * 50f;
                k++;
                if (on)
                {
                    ScreenKit.Btn(_empireBody, "• " + Trans.Get(name), x, y, 345f, 44f, () => { }, DiegeticUi.BtnStyle.Amber);
                    continue;
                }

                var b = _empireConfirm.Make(_empireBody, "policy" + id, "+ " + Trans.Get(name), x, y, 345f, 44f, () => AddPolicy(id),
                    DiegeticUi.BtnStyle.Ghost);
                b.interactable = have.Count < max;
            }
        }

        void RenderPolitics()
        {
            var cats = _politics?["categories"] as JArray;
            var choices = _politics?["choices"] as JObject;
            if (cats == null || cats.Count == 0)
            {
                L(_empireBody, Trans.Get("Loading"), 0f, 60f, 20f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            _politicsCategory = Mathf.Clamp(_politicsCategory, 0, cats.Count - 1);
            for (var i = 0; i < cats.Count; i++)
            {
                var index = i;
                ScreenKit.Btn(_empireBody, ScreenKit.Verbatim(FocusContext.AsString(cats[i]["name"])), -400f, 190f - i * 50f, 250f, 44f,
                    () =>
                    {
                        _politicsCategory = index;
                        RenderEmpire();
                    }, i == _politicsCategory ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            }

            // Names and descriptions are the server's (config $POLITICS), shown as they come.
            var cat = cats[_politicsCategory];
            var catId = FocusContext.AsString(cat["id"]);
            var chosen = FocusContext.AsString(choices?[catId]);
            ScreenKit.Para(_empireBody, "<b>" + ScreenKit.Verbatim(FocusContext.AsString(cat["name"])) + "</b>\n<size=80%>" +
                                        ScreenKit.Verbatim(FocusContext.AsString(cat["description"])) + "</size>", 130f, 180f, 17f,
                UiKit.TextBright, 760f, 56f);
            if (cat["options"] is not JArray options)
                return;
            for (var j = 0; j < options.Count && j < 4; j++)
            {
                var o = options[j];
                var optId = FocusContext.AsString(o["id"]);
                var y = 105f - j * 78f;
                var on = optId == chosen;
                ScreenKit.Btn(_empireBody, string.Empty, 130f, y, 760f, 70f, () =>
                {
                    if (!on)
                        AsyncTap.Run(SetPolitic(catId, optId));
                }, on ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
                L(_empireBody, "<b>" + ScreenKit.Verbatim(FocusContext.AsString(o["name"])) + "</b>  <size=80%>" + Effects(o["effects"]) + "</size>",
                    130f, y + 14f, 17f, UiKit.TextBright, 730f);
                L(_empireBody, ScreenKit.Verbatim(FocusContext.AsString(o["description"])), 130f, y - 14f, 13f, UiKit.TextDim, 730f);
            }
        }

        /// <summary>"+10 % defense  −15 % trade" coloured by sign (bonus keys → traitEffect_&lt;key&gt;).</summary>
        static string Effects(JToken effects)
        {
            if (effects is not JObject o)
                return string.Empty;
            var parts = new List<string>();
            foreach (var p in o.Properties())
            {
                var v = FocusContext.AsInt(p.Value);
                var label = Trans.Get("traitEffect_" + p.Name);
                parts.Add("<color=" + (v >= 0 ? "#6dff9e" : "#ff7a6b") + ">" + (v > 0 ? "+" : string.Empty) + v + " % " + label + "</color>");
            }

            return string.Join("  ", parts);
        }

        void RenderSpecies()
        {
            var specy = _me["specy"];
            var tokens = FocusContext.AsInt(_me["reconfigTokens"]);
            L(_empireBody, Tokens(), 330f, 142f, 14f, UiKit.Amber, 380f, TextAlignmentOptions.MidlineRight);
            if (_speciesTypes != null)
                for (var i = 0; i < _speciesTypes.Count; i++)
                {
                    var id = FocusContext.AsInt(_speciesTypes[i]["id"]);
                    ScreenKit.Btn(_empireBody, Trans.Get(FocusContext.AsString(_speciesTypes[i]["name"])), -455f + i % 7 * 151f,
                        118f - i / 7 * 38f, 145f, 34f, () =>
                        {
                            _specyType = id;
                            RenderEmpire();
                        }, id == _specyType ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                }

            if (_traits != null)
            {
                L(_empireBody, Trans.Get("positiveTraits") + "  " + _pos.Count + "/2", -370f, 44f, 14f, DiegeticUi.CyanDim, 300f);
                L(_empireBody, Trans.Get("negativeTraits") + "  " + _neg.Count + "/2", -370f, -76f, 14f, DiegeticUi.CyanDim, 300f);
                int p = 0, n = 0;
                foreach (var t in _traits)
                {
                    var id = FocusContext.AsInt(t["id"]);
                    var positive = FocusContext.AsInt(t["type"]) == 1;
                    var index = positive ? p++ : n++;
                    if (index >= 18)
                        continue;
                    var list = positive ? _pos : _neg;
                    var y = (positive ? 16f : -104f) - index / 6 * 32f;
                    var value = FocusContext.AsInt(t["value"]);
                    ScreenKit.Btn(_empireBody, Trans.Get(FocusContext.AsString(t["name"])) + " <size=75%><color=#ffb347>" +
                                              (value > 0 ? "+" : string.Empty) + value + "%</color></size>",
                        -445f + index % 6 * 177f, y, 171f, 30f, () =>
                        {
                            if (!list.Remove(id))
                            {
                                if (list.Count >= 2)
                                    return;
                                list.Add(id);
                            }

                            RenderEmpire();
                        }, list.Contains(id) ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                }
            }

            // Type or traits changed: one reconfiguration token, sent whole.
            var currentType = FocusContext.AsInt(specy?["type_id"]);
            var current = new HashSet<int>();
            if (specy?["traits"] is JArray have)
                foreach (var t in have)
                    current.Add(FocusContext.AsInt(t["id"]));
            var picked = new HashSet<int>(_pos);
            picked.UnionWith(_neg);
            var changed = currentType != _specyType || !current.SetEquals(picked);
            if (!changed)
                return;
            var b = _empireConfirm.Make(_empireBody, "specy", Trans.Get("vr.quarters.applySpecies") + "  <size=80%>(" +
                                                          Trans.Get("vr.quarters.oneToken") + ")</size>",
                300f, -205f, 440f, 40f, () => SaveSpecy(true), DiegeticUi.BtnStyle.Cyan);
            b.interactable = tokens > 0;
            if (tokens == 0)
                L(_empireBody, Trans.Get("reconfigLockNotice"), -250f, -205f, 13f, UiKit.Amber, 520f);
        }

        void RenderLog()
        {
            if (_log.Count == 0)
            {
                L(_empireBody, Trans.Get("vr.quarters.noLog"), 0f, 60f, 18f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            var pages = Mathf.Max(1, Mathf.CeilToInt(_log.Count / (float)LogPerPage));
            _logPage = Mathf.Clamp(_logPage, 0, pages - 1);
            var first = _logPage * LogPerPage;
            for (var i = first; i < _log.Count && i < first + LogPerPage; i++)
            {
                var r = _log[i];
                var y = 190f - (i - first) * 48f;
                L(_empireBody, "<color=#7fd8ff>" + FocusContext.AsString(r["date"]) + "</color>", -420f, y, 14f, UiKit.TextDim, 200f);
                // Server text (stored as written, see docs/PARITY.md): shown verbatim.
                L(_empireBody, ScreenKit.Verbatim(FocusContext.AsString(r["activity"])), 110f, y, 16f, UiKit.TextBright, 820f);
            }

            if (pages > 1)
                ScreenKit.Btn(_empireBody, (_logPage + 1) + "/" + pages + " ›", 470f, -205f, 110f, 36f, () =>
                {
                    _logPage = (_logPage + 1) % pages;
                    RenderEmpire();
                });
        }

        // ── Progression ───────────────────────────────────────────────────────────

        void SetProgTab(ProgTab tab)
        {
            _progTab = tab;
            SetStatus(_progStatus, string.Empty);
            RenderProgress();
        }

        void RenderProgress()
        {
            ScreenKit.LightTabs(_progTabs, (int)_progTab);
            ScreenKit.Clear(_progBody);
            if (_me == null)
                return;

            // Level bar (web GetXPForNextLevel: 100·level^1.5).
            var level = FocusContext.AsInt(_me["level"]);
            var xp = FocusContext.AsFloat(_me["xp"]);
            var need = Mathf.Round(100f * Mathf.Pow(Mathf.Max(1, level), 1.5f));
            ScreenKit.Gauge(_progBody, 0f, 192f, 1040f, xp / Mathf.Max(1f, need), new Color(1f, 0.7f, 0.3f, 0.8f),
                Trans.Get("level") + " " + level + "   ·   " + Trans.Get("xp") + " " + ScreenKit.Num(xp) + " / " + ScreenKit.Num(need), 26f);

            switch (_progTab)
            {
                case ProgTab.Objectives:
                    RenderObjectives();
                    break;
                case ProgTab.Achievements:
                    RenderAchievements();
                    break;
                default:
                    RenderEvent();
                    break;
            }
        }

        void RenderObjectives()
        {
            var periods = new[] { ("daily", "dailyObjectives", "noObjectivesToday"), ("weekly", "weeklyObjectives", "noObjectivesWeekly"),
                ("monthly", "monthlyObjectives", "noObjectivesMonthly") };
            for (var c = 0; c < periods.Length; c++)
            {
                var (key, title, none) = periods[c];
                var x = -350f + c * 350f;
                L(_progBody, "<b>" + Trans.Get(title) + "</b>", x, 150f, 16f, UiKit.Amber, 340f, TextAlignmentOptions.Center);
                var reset = FocusContext.AsString(_objectives?["resets"]?[key]);
                if (DateTime.TryParseExact(reset, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at))
                    L(_progBody, Trans.Format("vr.quarters.resetsIn", ScreenKit.Remaining((long)(at - DateTime.Now).TotalSeconds)), x, 126f,
                        12f, UiKit.TextDim, 340f, TextAlignmentOptions.Center);
                if (_objectives?[key] is not JArray rows || rows.Count == 0)
                {
                    L(_progBody, Trans.Get(none), x, 60f, 13f, UiKit.TextDim, 330f, TextAlignmentOptions.Center);
                    continue;
                }

                for (var i = 0; i < rows.Count && i < 4; i++)
                {
                    var o = rows[i];
                    var y = 90f - i * 72f;
                    var done = FocusContext.AsInt(o["completed"]) == 1;
                    var progress = FocusContext.AsFloat(o["progress"]);
                    var target = Mathf.Max(1f, FocusContext.AsFloat(o["target"]));
                    L(_progBody, (done ? "<color=#6dff9e>•</color> " : string.Empty) + ScreenKit.Verbatim(ScreenKit.Localized(o, "name")),
                        x, y + 16f, 14f, done ? UiKit.Ok : UiKit.TextBright, 330f);
                    ScreenKit.Gauge(_progBody, x, y - 8f, 330f, progress / target, done ? new Color(0.4f, 1f, 0.6f, 0.7f) : new Color(0.3f, 0.8f, 1f, 0.7f),
                        ScreenKit.Num(Mathf.Min(progress, target)) + " / " + ScreenKit.Num(target), 18f);
                    L(_progBody, "+" + FocusContext.AsInt(o["xp"]) + " " + Trans.Get("xp") + "  ·  +" + FocusContext.AsInt(o["nova"]) + " " +
                                 Trans.Get("nova"), x, y - 28f, 12f, UiKit.Amber, 330f, TextAlignmentOptions.Center);
                }
            }
        }

        void RenderAchievements()
        {
            var all = _achievements?["all"] as JObject;
            var unlocked = _achievements?["unlocked"] as JArray;
            if (all == null)
            {
                L(_progBody, Trans.Get("Loading"), 0f, 60f, 18f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            var when = new Dictionary<string, long>();
            if (unlocked != null)
                foreach (var u in unlocked)
                    when[FocusContext.AsString(u["achievement_key"])] = FocusContext.AsLong(u["unlocked_at"]);
            for (var i = 0; i < AchievementCategories.Length; i++)
            {
                var index = i;
                ScreenKit.Btn(_progBody, Trans.Get("achievementCategory_" + AchievementCategories[i]), -462f + i * 132f, 145f, 126f, 36f, () =>
                {
                    _achievementCategory = index;
                    _achievementPage = 0;
                    RenderProgress();
                }, i == _achievementCategory ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            }

            var cat = AchievementCategories[_achievementCategory];
            var keys = new List<string>();
            foreach (var p in all.Properties())
                if (cat == "all" || FocusContext.AsString(p.Value["category"]) == cat)
                    keys.Add(p.Name);
            // Unlocked first (newest first), then the rest in the server's order.
            keys.Sort((a, b) =>
            {
                var ua = when.TryGetValue(a, out var ta);
                var ub = when.TryGetValue(b, out var tb);
                if (ua != ub)
                    return ua ? -1 : 1;
                return ua ? tb.CompareTo(ta) : 0;
            });
            var pages = Mathf.Max(1, Mathf.CeilToInt(keys.Count / (float)AchievementsPerPage));
            _achievementPage = Mathf.Clamp(_achievementPage, 0, pages - 1);
            var first = _achievementPage * AchievementsPerPage;
            for (var i = first; i < keys.Count && i < first + AchievementsPerPage; i++)
            {
                var key = keys[i];
                var def = all[key];
                var got = when.ContainsKey(key);
                var y = 95f - (i - first) * 50f;
                L(_progBody, (got ? "<color=#ffd27a>•</color> " : "<color=#56646c>•</color> ") + "<b>" + AchievementName(key, def) + "</b>  <size=78%>" +
                             AchievementDesc(key, def) + "</size>", -120f, y, 15f, got ? UiKit.TextBright : UiKit.TextDim, 800f);
                L(_progBody, "+" + FocusContext.AsInt(def["xp"]) + " " + Trans.Get("xp") + " · +" + FocusContext.AsInt(def["nova"]) + " " + Trans.Get("nova"),
                    410f, y, 13f, got ? UiKit.Amber : UiKit.TextDim, 220f, TextAlignmentOptions.MidlineRight);
            }

            L(_progBody, when.Count + " / " + all.Count, -440f, -205f, 15f, UiKit.Amber, 160f);
            if (pages > 1)
                ScreenKit.Btn(_progBody, (_achievementPage + 1) + "/" + pages + " ›", 470f, -205f, 110f, 36f, () =>
                {
                    _achievementPage = (_achievementPage + 1) % pages;
                    RenderProgress();
                });
        }

        void RenderEvent()
        {
            var ev = (_event?["data"] as JArray)?.Count > 0 ? _event["data"][0] : null;
            if (ev == null)
            {
                L(_progBody, Trans.Get("vr.quarters.noEvent"), 0f, 60f, 18f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            var end = FocusContext.AsString(ev["end_at"]);
            var left = DateTime.TryParseExact(end, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)
                ? Trans.Format("vr.quarters.endsIn", ScreenKit.Remaining((long)(at - DateTime.Now).TotalSeconds))
                : string.Empty;
            L(_progBody, "<b>" + ScreenKit.Verbatim(ScreenKit.Localized(ev, "title")) + "</b>", -120f, 150f, 18f, UiKit.Amber, 800f);
            L(_progBody, left, 410f, 150f, 13f, UiKit.TextDim, 220f, TextAlignmentOptions.MidlineRight);
            ScreenKit.Para(_progBody, ScreenKit.Verbatim(ScreenKit.Localized(ev, "description")), 0f, 105f, 13f, UiKit.TextDim, 1040f, 54f);

            if (ev["objectives"] is JArray objs)
                for (var i = 0; i < objs.Count && i < 4; i++)
                {
                    var o = objs[i];
                    var y = 50f - i * 44f;
                    var done = FocusContext.AsInt(o["completed"]) == 1;
                    L(_progBody, (done ? "<color=#6dff9e>•</color> " : string.Empty) + ScreenKit.Verbatim(ScreenKit.Localized(o, "title")),
                        -270f, y, 14f, done ? UiKit.Ok : UiKit.TextBright, 500f);
                    ScreenKit.Gauge(_progBody, 120f, y, 250f, FocusContext.AsFloat(o["percent"]) / 100f, new Color(0.3f, 0.8f, 1f, 0.7f),
                        FocusContext.AsString(o["progress"]) + " / " + FocusContext.AsString(o["target"]), 18f);
                    L(_progBody, "+" + FocusContext.AsInt(o["reward_xp"]) + " " + Trans.Get("xp") + " · +" + FocusContext.AsInt(o["reward_nova"]) + " " +
                                 Trans.Get("nova"), 410f, y, 12f, UiKit.Amber, 220f, TextAlignmentOptions.MidlineRight);
                }

            var boss = ev["world_boss"];
            if (boss == null || boss.Type == JTokenType.Null)
                return;
            var hp = FocusContext.AsFloat(boss["hp_percent"]) / 100f;
            L(_progBody, "<b>" + ScreenKit.Verbatim(ScreenKit.Localized(boss, "name")) + "</b>", -330f, -135f, 16f, UiKit.Danger, 380f);
            ScreenKit.Gauge(_progBody, 170f, -135f, 600f, hp, new Color(1f, 0.3f, 0.26f, 0.8f),
                FocusContext.AsString(boss["current_hp_fmt"]) + " / " + FocusContext.AsString(boss["max_hp_fmt"]), 22f);
            var mine = boss["my_damage"];
            var board = boss["leaderboard"] as JArray;
            var line = mine != null && mine.Type != JTokenType.Null
                ? Trans.Format("vr.quarters.myDamage", FocusContext.AsString(mine["damage_fmt"]), FocusContext.AsString(mine["pct"]))
                : Trans.Get("vr.quarters.noDamage");
            if (board != null && board.Count > 0)
                line += "   ·   #1 " + ScreenKit.Verbatim(FocusContext.AsString(board[0]["empire_name"])) + " " + FocusContext.AsString(board[0]["damage_fmt"]);
            L(_progBody, line, 0f, -175f, 13f, UiKit.TextDim, 1040f, TextAlignmentOptions.Center);
            L(_progBody, Trans.Format("vr.quarters.bossPool", FocusContext.AsString(boss["reward_nova_pool"]), FocusContext.AsString(boss["reward_xp_pool"])),
                0f, -200f, 12f, UiKit.Amber, 1040f, TextAlignmentOptions.Center);
        }

        // ── Shop ──────────────────────────────────────────────────────────────────

        void SetShopTab(ShopTab tab)
        {
            _shopTab = tab;
            _itemPage = 0;
            _shopConfirm.Reset();
            SetStatus(_shopStatus, string.Empty);
            RenderShop();
        }

        void RenderShop()
        {
            ScreenKit.LightTabs(_shopTabs, (int)_shopTab);
            ScreenKit.Clear(_shopBody);
            if (_shopData == null)
            {
                L(_shopBody, Trans.Get("Loading"), 0f, 60f, 20f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            var nova = FocusContext.AsInt(_shopData["nova"]);
            L(_shopBody, "<b>" + ScreenKit.Num(nova) + "</b> " + Trans.Get("nova"), -280f, 190f, 22f, UiKit.Amber, 500f);
            L(_shopBody, Trans.Format("vr.quarters.tokens", FocusContext.AsInt(_shopData["renameTokens"]),
                FocusContext.AsInt(_shopData["reconfigTokens"])), 330f, 190f, 14f, UiKit.TextDim, 380f, TextAlignmentOptions.MidlineRight);
            if (_shopTab == ShopTab.Nova)
            {
                RenderTopUp();
                return;
            }

            var category = _shopTab switch
            {
                ShopTab.Boosters => "booster",
                ShopTab.Consumables => "consumable",
                ShopTab.Cosmetics => "cosmetic",
                _ => "title"
            };
            var items = new List<JProperty>();
            if (_shopData["catalog"] is JObject catalog)
                foreach (var p in catalog.Properties())
                    if (FocusContext.AsString(p.Value["category"]) == category)
                        items.Add(p);
            var owned = new HashSet<string>();
            if (_shopData["inventory"] is JArray inv)
                foreach (var i in inv)
                    owned.Add((string)i);
            var boosters = _shopData["activeBoosters"] as JArray;
            var equippedTitle = FocusContext.AsString(_shopData["equippedTitle"]);
            var colour = FocusContext.AsString(_shopData["fleetColor"]);

            var pages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)ItemsPerPage));
            _itemPage = Mathf.Clamp(_itemPage, 0, pages - 1);
            var first = _itemPage * ItemsPerPage;
            for (var i = first; i < items.Count && i < first + ItemsPerPage; i++)
            {
                var key = items[i].Name;
                var def = items[i].Value;
                var price = FocusContext.AsInt(def["price"]);
                var y = 125f - (i - first) * 82f;
                if (category == "cosmetic")
                {
                    var sw = new GameObject("Colour", typeof(RectTransform), typeof(Image));
                    sw.transform.SetParent(_shopBody, false);
                    var rt = sw.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(40f, 40f);
                    rt.anchoredPosition = new Vector2(-500f, y);
                    var img = sw.GetComponent<Image>();
                    img.color = FlagPainter.Hex("#" + FocusContext.AsString(def["value"]), Color.white);
                    img.raycastTarget = false;
                }

                var x0 = category == "cosmetic" ? -80f : -110f;
                L(_shopBody, "<b>" + Trans.Get("shopItemName_" + key) + "</b>  <size=80%><color=#ffd27a>" + ScreenKit.Num(price) + " " +
                             Trans.Get("nova") + "</color></size>", x0, y + 16f, 17f, UiKit.TextBright, 760f);
                ScreenKit.Para(_shopBody, Trans.Get("shopItemDesc_" + key), x0, y - 16f, 12f, UiKit.TextDim, 760f, 34f);

                // Right: the state or the order.
                var bx = 420f;
                if (category == "booster")
                {
                    var active = ActiveBooster(boosters, def);
                    if (active > 0)
                        L(_shopBody, Trans.Get("shopActiveFor") + " " + ScreenKit.Remaining(active), bx, y + 24f, 12f, UiKit.Ok, 200f,
                            TextAlignmentOptions.Center);
                }

                var isOwned = owned.Contains(key);
                var equipped = category == "title" ? equippedTitle == key
                    : category == "cosmetic" && string.Equals(colour, FocusContext.AsString(def["value"]), StringComparison.OrdinalIgnoreCase);
                if (equipped)
                    L(_shopBody, "<b>" + Trans.Get("shopEquipped") + "</b>", bx, y, 16f, UiKit.Ok, 200f, TextAlignmentOptions.Center);
                else if (isOwned)
                    ScreenKit.Btn(_shopBody, Trans.Get("shopEquip"), bx, y, 190f, 46f, () => AsyncTap.Run(Equip(key)), DiegeticUi.BtnStyle.Cyan);
                else
                {
                    var b = _shopConfirm.Make(_shopBody, "buy" + key, Trans.Format("vr.quarters.buyFor", ScreenKit.Num(price)), bx, y - 4f, 190f, 42f,
                        () => Buy(key), DiegeticUi.BtnStyle.Amber);
                    b.interactable = nova >= price && !_busy;
                }
            }

            if (pages > 1)
                ScreenKit.Btn(_shopBody, (_itemPage + 1) + "/" + pages + " ›", 470f, -205f, 110f, 36f, () =>
                {
                    _itemPage = (_itemPage + 1) % pages;
                    RenderShop();
                });
        }

        /// <summary>Seconds left on the booster's first bonus (web ShopWindowUI), 0 when inactive.</summary>
        static long ActiveBooster(JArray active, JToken def)
        {
            if (active == null || def["bonuses"] is not JObject bonuses)
                return 0;
            string first = null;
            foreach (var p in bonuses.Properties())
            {
                first = p.Name;
                break;
            }

            foreach (var b in active)
                if (FocusContext.AsString(b["type"]) == first)
                    return Math.Max(0, FocusContext.AsLong(b["expires_at"]) - FleetOrderGate.UnixNow());
            return 0;
        }

        /// <summary>
        /// Nova packs and our top-up history. Paying goes through the web checkout today; the headset has no
        /// payment path yet (docs/PARITY.md), so the packs are shown without a buy order.
        /// </summary>
        void RenderTopUp()
        {
            L(_shopBody, Trans.Get("shopTopUpDesc"), 0f, 150f, 13f, UiKit.TextDim, 1040f, TextAlignmentOptions.Center);
            if (_packs?["packs"] is JObject packs)
            {
                var i = 0;
                foreach (var p in packs.Properties())
                {
                    var x = -424f + i * 212f;
                    var cents = FocusContext.AsInt(p.Value["amount_cents"]);
                    ScreenKit.Btn(_shopBody, "<b>" + ScreenKit.Num(FocusContext.AsInt(p.Value["nova"])) + "</b> " + Trans.Get("nova") +
                                             "\n<size=75%>" + (cents / 100f).ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")) + " " +
                                             FocusContext.AsString(p.Value["currency"]).ToUpperInvariant() + "</size>",
                        x, 80f, 200f, 70f, () => { }, DiegeticUi.BtnStyle.Ghost, false);
                    i++;
                }
            }

            L(_shopBody, Trans.Get("vr.quarters.topupUnavailable"), 0f, 20f, 14f, UiKit.Amber, 1040f, TextAlignmentOptions.Center);
            L(_shopBody, "<b>" + Trans.Get("vr.quarters.topupHistory") + "</b>", -370f, -25f, 15f, DiegeticUi.CyanDim, 300f);
            if (_topups == null || _topups.Count == 0)
            {
                L(_shopBody, Trans.Get("vr.quarters.noTopup"), 0f, -65f, 14f, UiKit.TextDim, 1040f, TextAlignmentOptions.Center);
                return;
            }

            for (var k = 0; k < _topups.Count && k < 4; k++)
            {
                var t = _topups[k];
                var date = DateTimeOffset.FromUnixTimeSeconds(FocusContext.AsLong(t["created_at"])).ToLocalTime().ToString("dd/MM/yyyy");
                L(_shopBody, date + "   +" + ScreenKit.Num(FocusContext.AsInt(t["nova_amount"])) + " " + Trans.Get("nova"), 0f, -60f - k * 34f, 14f,
                    UiKit.TextBright, 1000f, TextAlignmentOptions.Center);
            }
        }

        // ── Loop ──────────────────────────────────────────────────────────────────

        void Update()
        {
            if (!Inside)
                return;
            _empireConfirm.Tick();
            _shopConfirm.Tick();
            if (_busy)
                return;
            var now = Time.unscaledTime;
            if (_empireTab == EmpireTab.Log && now >= _logAt)
                AsyncTap.Run(LoadLog());
            if (now >= _progressAt)
            {
                _progressAt = now + ProgressEvery;
                AsyncTap.Run(ProgressThenRender());
            }
        }

        async Task ProgressThenRender()
        {
            await LoadProgress();
            if (Inside)
                RenderProgress();
        }
    }
}
