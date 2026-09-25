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
    /// The diplomacy chamber — web WarsWindowUI + AllianceWindowUI as a place. A round hall: the empire orrery
    /// ahead (our flag at the heart, every other empire around it, tethers in stance colour), a hemicycle with
    /// a seat per empire, our alliance's banners and crest on the far wall. Two desks at the stand:
    /// the Dossier (left) — the empire picked on the orrery: relation (GetRelation), standing, and what we can do
    /// to it: DeclareWar with demands (GetEmpirePlanets → planets, resource quantities), InviteToAlliance /
    /// CancelAllianceInvite; the Chancellery (right) — Conflicts (GetMyWars, GetWarDetails: peace offers,
    /// surrender / capitulation), Alliance (GetMyAlliance: description, roles, kick, transfer, applications,
    /// leave / disband, or invitations + founding when we have none) and the Registry (GetAlliances, apply /
    /// withdraw). Irreversible orders take two presses. Wars, invitations and the alliance come from
    /// <see cref="DiplomacyService"/>; the hall's lights turn red while the empire is at war.
    /// </summary>
    public sealed class DiplomacyRoom : MonoBehaviour
    {
        enum Tab
        {
            Wars,
            Alliance,
            Registry
        }

        static readonly Vector3 WorldOrigin = new(0f, -3000f, -200f);
        static readonly Vector3 Stand = Vector3.zero;
        static readonly Vector3 Centre = new(0f, 0f, 3.4f);
        const float Radius = 6.6f;
        /// <summary>Height of the chamber over the bridge (clear of our hull and of the world we orbit).</summary>
        const float AboveShip = 24f;
        const float Height = 6f;
        public static readonly Color Accent = new(1f, 0.78f, 0.42f, 1f);
        static readonly Color WarRed = new(1f, 0.3f, 0.26f, 1f);
        static readonly Vector2 ScreenSize = new(1.1f, 0.7f);
        const float ConfirmWindow = 4f;
        const int WarsPerPage = 4;
        const int MembersPerPage = 4;
        const int AlliancesPerPage = 5;
        const int PlanetsPerPage = 8;
        const string SentKey = "su.diplo.sent.v1";
        static readonly string[] Res = { "mineral", "crystal", "biomass" };
        static readonly int[] ResSteps = { -10000, -1000, 1000, 10000 };

        public static DiplomacyRoom Instance { get; private set; }
        public static bool Inside { get; private set; }

        CicArtKit _art;
        DiplomacyDecor.Refs _decor;
        EmpireOrrery _orrery;
        Material _calm;
        Material _war;
        bool _warLit;
        Light _light;

        HoloScreen _dossier;
        RectTransform _dossierBody;
        TMP_Text _dossierName;
        TMP_Text _dossierStatus;
        HoloScreen _chancellery;
        RectTransform _chancelleryBody;
        TMP_Text _chancelleryStatus;
        readonly Button[] _tabs = new Button[3];
        GameObject _descGroup;
        TMP_InputField _desc;
        GameObject _createGroup;
        TMP_InputField _allianceName;
        TMP_InputField _allianceTag;

        JArray _empires = new();
        JArray _alliances = new();
        int _selected;
        float _relation = -1f;
        bool _composing;
        JArray _targetPlanets;
        readonly HashSet<int> _demandPlanets = new();
        readonly float[] _demand = new float[3];
        int _demandRes;
        int _planetPage;
        Tab _tab;
        int _warSel;
        int _warPage;
        int _memberPage;
        int _registryPage;
        string _confirm;
        float _confirmUntil;
        bool _busy;
        readonly Dictionary<int, int> _sentInvites = new();
        readonly Dictionary<int, int> _sentApplications = new();

        // ── Build ─────────────────────────────────────────────────────────────────

        public static DiplomacyRoom Build(CicArtKit art)
        {
            var go = new GameObject("DiplomacyChamber");
            go.transform.position = WorldOrigin;
            var room = go.AddComponent<DiplomacyRoom>();
            room._art = art;
            room.BuildHall();
            room.BuildScreens();
            room.LoadSent();
            go.SetActive(false);
            return room;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (DiplomacyService.Instance != null)
                DiplomacyService.Instance.Changed -= OnDiplomacyChanged;
            if (Instance == this)
                Instance = null;
        }

        void BuildHall()
        {
            _decor = DiplomacyDecor.Build(transform, _art, Centre, Radius, Height, Accent);
            _calm = _art.Lit(Texture2D.whiteTexture, Accent, 2.6f);
            _war = _art.Lit(Texture2D.whiteTexture, WarRed, 3f);
            _orrery = EmpireOrrery.Build(transform, Centre, _art, Accent, Pick);

            var fill = new GameObject("HallLight").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.transform.localPosition = Centre + new Vector3(0f, Height - 0.8f, 0f);
            fill.type = LightType.Point;
            fill.range = 14f;
            fill.intensity = 1.4f;
            fill.color = new Color(1f, 0.9f, 0.75f);
            fill.shadows = LightShadows.None;
            _light = fill;

            RoomDoor.Build(transform, "DoorToBridge", new Vector3(0f, 0f, Centre.z - Radius + 0.35f), 0f,
                Trans.Get("vr.diplo.leave"), UiKit.Amber, _art, () => Inside, () => AsyncTap.Run(Leave()));
        }

        void BuildScreens()
        {
            _dossier = HoloScreen.Create(transform, "Dossier", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.diplo.dossier"));
            _dossier.SetAccent(Accent, 0.5f);
            GateRoomDecor.SeatOnArm(_dossier.transform, _decor.DossierMount, ScreenSize.y);
            var df = _dossier.Content;
            DiegeticUi.HoloButton(df, "‹", new Vector2(-505f, 245f), new Vector2(64f, 46f), () => StepEmpire(-1),
                DiegeticUi.BtnStyle.Ghost);
            _dossierName = DiegeticUi.HoloLabel(df, string.Empty, new Vector2(-195f, 245f), new Vector2(540f, 46f), 24f,
                UiKit.TextBright);
            _dossierName.richText = true;
            _dossierName.fontStyle = FontStyles.Bold;
            DiegeticUi.HoloButton(df, "›", new Vector2(115f, 245f), new Vector2(64f, 46f), () => StepEmpire(1),
                DiegeticUi.BtnStyle.Ghost);
            _dossierBody = Body(df);
            _dossierStatus = DiegeticUi.HoloLabel(df, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);

            _chancellery = HoloScreen.Create(transform, "Chancellery", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.diplo.title"));
            _chancellery.SetAccent(Accent, 0.5f);
            GateRoomDecor.SeatOnArm(_chancellery.transform, _decor.ChancelleryMount, ScreenSize.y);
            var cf = _chancellery.Content;
            var tabKeys = new[] { "vr.diplo.tab.wars", "vr.diplo.tab.alliance", "vr.diplo.tab.registry" };
            for (var i = 0; i < _tabs.Length; i++)
            {
                var tab = (Tab)i;
                _tabs[i] = DiegeticUi.HoloButton(cf, Trans.Get(tabKeys[i]), new Vector2(-360f + i * 360f, 245f),
                    new Vector2(348f, 48f), () => SetTab(tab), DiegeticUi.BtnStyle.Ghost);
            }

            _chancelleryBody = Body(cf);
            _chancelleryStatus = DiegeticUi.HoloLabel(cf, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f),
                18f, DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);

            // Typing fields live outside the rebuilt body so a refresh never steals the keyboard.
            _descGroup = Group(cf, "DescriptionBar");
            _desc = DiegeticUi.HoloField(_descGroup.transform, "Description", Trans.Get("allianceDescription"),
                new Vector2(-95f, 132f), new Vector2(840f, 46f), TouchScreenKeyboardType.Default);
            _desc.characterLimit = 255;
            _desc.gameObject.AddComponent<RectMask2D>();
            _desc.onSubmit.AddListener(_ => Run(SaveDescription()));
            DiegeticUi.HoloButton(_descGroup.transform, Trans.Get("vr.diplo.save"), new Vector2(435f, 132f),
                new Vector2(170f, 46f), () => Run(SaveDescription()), DiegeticUi.BtnStyle.Cyan);

            _createGroup = Group(cf, "CreateBar");
            _allianceName = DiegeticUi.HoloField(_createGroup.transform, "AllianceName", Trans.Get("allianceName"),
                new Vector2(-250f, -120f), new Vector2(540f, 50f), TouchScreenKeyboardType.Default);
            _allianceName.characterLimit = 32;
            _allianceTag = DiegeticUi.HoloField(_createGroup.transform, "AllianceTag", Trans.Get("allianceTag"),
                new Vector2(130f, -120f), new Vector2(180f, 50f), TouchScreenKeyboardType.ASCIICapable);
            _allianceTag.characterLimit = 5;
            DiegeticUi.HoloButton(_createGroup.transform, Trans.Get("createAlliance"), new Vector2(385f, -120f),
                new Vector2(290f, 50f), () => Run(CreateAlliance()), DiegeticUi.BtnStyle.Cyan);
        }

        static RectTransform Body(RectTransform frame)
        {
            var go = new GameObject("Body", typeof(RectTransform));
            go.transform.SetParent(frame, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1060f, 420f);
            return rt;
        }

        GameObject Group(RectTransform frame, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(frame, false);
            go.GetComponent<RectTransform>().sizeDelta = _chancellery.PixelSize;
            go.SetActive(false);
            return go;
        }

        // ── Enter / leave ─────────────────────────────────────────────────────────

        public static bool AnyRoomInside => Inside || GateRoom.Inside || ResearchLab.Inside || DryDock.Inside;

        public async Task Enter()
        {
            if (Inside || GateRoom.Inside || DryDock.Inside || ResearchLab.Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            PlaceOverShip();
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
            _composing = false;
            _confirm = null;
            if (DiplomacyService.Instance != null)
            {
                DiplomacyService.Instance.Changed -= OnDiplomacyChanged;
                DiplomacyService.Instance.Changed += OnDiplomacyChanged;
            }

            await Load();
            await fade.FadeIn();
            CicCue.Ok(transform.position + Vector3.up);
        }

        /// <summary>
        /// The chamber is a wing above the ship (or station) we stand on: its windows look on the same, shared
        /// system exterior as the bridge — our world or the star beyond, fleets passing — with no second copy of
        /// it. Flank windows face the star.
        /// </summary>
        void PlaceOverShip()
        {
            var bridge = FindFirstObjectByType<BridgeViewRig>();
            if (bridge == null || bridge.BridgeMount == null)
                return;
            var pos = bridge.BridgeMount.position + Vector3.up * AboveShip;
            var ext = FindFirstObjectByType<SystemExterior>();
            var toStar = (ext != null ? ext.transform.position : Vector3.zero) - pos;
            toStar.y = 0f;
            if (toStar.sqrMagnitude < 1f)
                toStar = Vector3.right;
            // Turn the hall so that, from the stand, the star sits in the middle of the right-hand windows.
            var look = DiplomacyDecor.RightWindowCentre(Centre, Radius) - Stand;
            look.y = 0f;
            transform.SetPositionAndRotation(pos,
                Quaternion.LookRotation(toStar.normalized, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(look.normalized, Vector3.up)));
        }

        async Task Leave()
        {
            if (!Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            Inside = false;
            if (DiplomacyService.Instance != null)
                DiplomacyService.Instance.Changed -= OnDiplomacyChanged;
            var rig = FindFirstObjectByType<XROrigin>();
            var bridge = FindFirstObjectByType<BridgeViewRig>();
            if (rig != null && bridge != null && bridge.BridgeMount != null)
            {
                rig.transform.SetParent(bridge.BridgeMount, false);
                bridge.PutPlayerOnDeck();
            }

            gameObject.SetActive(false);
            await fade.FadeIn();
        }

        void OnDiplomacyChanged()
        {
            if (!Inside || _busy)
                return;
            SyncHall();
            RenderAll();
        }

        static void Run(Task t) => AsyncTap.Run(t);

        // ── Data ──────────────────────────────────────────────────────────────────

        async Task Load()
        {
            var empires = ActionJs.Get("GetEmpires");
            var alliances = ActionJs.Get("GetAlliances");
            var service = DiplomacyService.Instance != null ? DiplomacyService.Instance.Refresh() : Task.CompletedTask;
            await Task.WhenAll(empires, alliances, service);
            if (empires.Result.Ok)
            {
                _empires = ParseArray(empires.Result.Body);
                DiplomacyIndex.IngestEmpiresBody(empires.Result.Body);
            }

            if (alliances.Result.Ok)
                _alliances = ParseArray(alliances.Result.Body);
            if (Empire(_selected) == null || _selected == DiplomacyService.MyEmpireId)
                _selected = FirstOther();
            await LoadRelation();
            SyncHall();
            RenderAll();
        }

        int FirstOther()
        {
            foreach (var e in OrreryOrder())
                return FocusContext.AsInt(e["id"]);
            return 0;
        }

        JToken Empire(int id)
        {
            foreach (var e in _empires)
                if (FocusContext.AsInt(e["id"]) == id)
                    return e;
            return null;
        }

        /// <summary>Everyone but us: at war first, then our alliance, then by score (the server's order).</summary>
        List<JToken> OrreryOrder()
        {
            var me = DiplomacyService.MyEmpireId;
            var svc = DiplomacyService.Instance;
            var war = new List<JToken>();
            var ally = new List<JToken>();
            var rest = new List<JToken>();
            foreach (var e in _empires)
            {
                var id = FocusContext.AsInt(e["id"]);
                if (id == me)
                    continue;
                if (svc != null && svc.ActiveWarWith(id) != null)
                    war.Add(e);
                else if (svc != null && svc.IsMember(id))
                    ally.Add(e);
                else
                    rest.Add(e);
            }

            war.AddRange(ally);
            war.AddRange(rest);
            return war;
        }

        async Task LoadRelation()
        {
            _relation = -1f;
            var e = Empire(_selected);
            var me = AuthManager.Ensure().User?.id ?? 0;
            if (e == null || me <= 0)
                return;
            var id = _selected;
            var res = await ActionJs.Get("GetRelation", new Dictionary<string, string>
            {
                { "user1", me.ToString() },
                { "user2", FocusContext.AsString(e["userid"]) }
            });
            if (id != _selected || !res.Ok)
                return;
            try
            {
                _relation = FocusContext.AsFloat(JObject.Parse(res.Body)["relation"]);
            }
            catch
            {
                _relation = FocusContext.AsFloat(e["relation"]);
            }
        }

        async Task LoadTargetPlanets()
        {
            var id = _selected;
            _targetPlanets = null;
            var res = await ActionJs.Get("GetEmpirePlanets", new Dictionary<string, string> { { "empire", id.ToString() } });
            if (id != _selected)
                return;
            _targetPlanets = ParseArray(res);
            if (!res.Ok)
                SetStatus(_dossierStatus, res.Error, true);
            RenderDossier();
        }

        // ── The hall follows the data ─────────────────────────────────────────────

        void SyncHall()
        {
            var svc = DiplomacyService.Instance;
            var me = DiplomacyService.MyEmpireId;
            var mine = Empire(me);
            var entries = new List<EmpireOrrery.Entry>();
            foreach (var e in OrreryOrder())
            {
                var id = FocusContext.AsInt(e["id"]);
                entries.Add(new EmpireOrrery.Entry(id, FocusContext.AsString(e["name"]), FocusContext.AsString(e["flag"]),
                    DiplomacyIndex.Resolve(FocusContext.AsInt(e["userid"])), svc?.ActiveWarWith(id) != null,
                    svc != null && svc.IsMember(id)));
            }

            _orrery.SetEmpires(me, FocusContext.AsString(mine?["flag"]), entries);
            _orrery.Select(_selected);

            // A seat per empire in the hemicycle, the server's ranking left to right.
            for (var i = 0; i < DiplomacyDecor.Seats; i++)
            {
                var plate = _decor.SeatPlates[i];
                var label = _decor.SeatNames[i];
                if (i < _empires.Count)
                {
                    var e = _empires[i];
                    var id = FocusContext.AsInt(e["id"]);
                    plate.sharedMaterial = _orrery.FlagMaterial(id, FocusContext.AsString(e["flag"]));
                    label.text = "<noparse>" + FocusContext.AsString(e["name"]) + "</noparse>";
                    label.color = id == me ? UiKit.Cyan
                        : svc?.ActiveWarWith(id) != null ? WarRed
                        : Color.Lerp(DiplomacyIndex.Tint(DiplomacyIndex.Resolve(FocusContext.AsInt(e["userid"]))), Color.white, 0.3f);
                }
                else
                {
                    plate.sharedMaterial = _art.DarkPanel(0.28f);
                    label.text = string.Empty;
                }
            }

            // Banners: our alliance's members (flags), or our own flag flanking the crest when unaligned.
            var alliance = svc?.Alliance;
            var flags = new List<(int Id, string Flag)>();
            if (alliance?["members"] is JArray members)
                foreach (var m in members)
                {
                    var id = FocusContext.AsInt(m["empireId"]);
                    flags.Add((id, FocusContext.AsString(Empire(id)?["flag"])));
                }
            else if (mine != null)
                flags.Add((me, FocusContext.AsString(mine["flag"])));

            // Fill outward from the crest: 2, 4, 1, 5, 0, 6.
            var order = new[] { 2, 4, 1, 5, 0, 6 };
            for (var k = 0; k < order.Length; k++)
            {
                var banner = _decor.Banners[order[k]];
                if (banner == null)
                    continue;
                banner.sharedMaterial = k < flags.Count
                    ? _orrery.FlagMaterial(flags[k].Id, flags[k].Flag)
                    : _art.DarkPanel(0.28f);
            }

            _decor.Crest.text = alliance != null
                ? "<size=120%>[" + Verbatim(FocusContext.AsString(alliance["tag"])) + "]</size>\n<size=45%>" +
                  Verbatim(FocusContext.AsString(alliance["name"])) + "</size>"
                : "<size=55%>" + Trans.Get("vr.diplo.unaligned") + "</size>\n<size=40%>" +
                  Verbatim(FocusContext.AsString(mine?["name"])) + "</size>";

            var atWar = svc != null && svc.ActiveWars > 0;
            if (atWar != _warLit)
            {
                _warLit = atWar;
                foreach (var r in _decor.StateLights)
                    if (r != null)
                        r.sharedMaterial = atWar ? _war : _calm;
                _light.color = atWar ? new Color(1f, 0.55f, 0.45f) : new Color(1f, 0.9f, 0.75f);
            }
        }

        void Pick(int empireId)
        {
            if (empireId == _selected)
                return;
            _selected = empireId;
            _composing = false;
            _confirm = null;
            _orrery.Select(empireId);
            SetStatus(_dossierStatus, string.Empty);
            RenderDossier();
            Run(RelationThenRender());
        }

        async Task RelationThenRender()
        {
            await LoadRelation();
            RenderDossier();
        }

        void StepEmpire(int delta)
        {
            var list = OrreryOrder();
            if (list.Count == 0)
                return;
            var i = list.FindIndex(e => FocusContext.AsInt(e["id"]) == _selected);
            Pick(FocusContext.AsInt(list[((i < 0 ? 0 : i) + delta + list.Count) % list.Count]["id"]));
        }

        // ── Orders ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Send an order; on success refresh wars / alliance (and GetEmpires when stances move), re-render.
        /// Returns the parsed success body (null on failure).
        /// </summary>
        async Task<JObject> Order(TMP_Text status, string action, Dictionary<string, string> args, string okText,
            bool stances = false)
        {
            if (_busy)
                return null;
            _busy = true;
            _confirm = null;
            JObject body = null;
            try
            {
                var res = await ActionJs.Get(action, args);
                if (!res.Ok)
                {
                    SetStatus(status, string.IsNullOrEmpty(res.Error) ? Trans.Get("vr.common.error") : res.Error, true);
                    CicCue.Fail(status.transform.position);
                    Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "fail", 2);
                    return null;
                }

                try
                {
                    body = string.IsNullOrEmpty(res.Body) ? new JObject() : JObject.Parse(res.Body);
                }
                catch
                {
                    body = new JObject();
                }

                SetStatus(status, okText);
                CicCue.Ok(status.transform.position);
                if (DiplomacyService.Instance != null)
                    await DiplomacyService.Instance.Refresh();
                if (stances)
                {
                    var empires = await ActionJs.Get("GetEmpires");
                    if (empires.Ok)
                    {
                        _empires = ParseArray(empires.Body);
                        DiplomacyIndex.IngestEmpiresBody(empires.Body);
                    }
                }

                var alliances = await ActionJs.Get("GetAlliances");
                if (alliances.Ok)
                    _alliances = ParseArray(alliances.Body);
                return body;
            }
            finally
            {
                _busy = false;
                SyncHall();
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

        async Task DeclareWar()
        {
            var target = _selected;
            var name = EmpireName(target);
            var planets = new List<string>();
            foreach (var p in _demandPlanets)
                planets.Add(p.ToString());
            var ok = await Order(_dossierStatus, "DeclareWar", Args(
                "target", target.ToString(),
                "planets", string.Join(",", planets),
                "mineral", Mathf.RoundToInt(_demand[0]).ToString(),
                "crystal", Mathf.RoundToInt(_demand[1]).ToString(),
                "biomass", Mathf.RoundToInt(_demand[2]).ToString()), Trans.Format("vr.diplo.declared", name), true);
            if (ok == null)
                return;
            _composing = false;
            _demandPlanets.Clear();
            Array.Clear(_demand, 0, _demand.Length);
            Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "warLaunched", 3, name);
            CicCue.Klaxon(_orrery.transform.position + Vector3.up * 1.6f);
            _warSel = FocusContext.AsInt(ok["warId"]);
            _tab = Tab.Wars;
            RenderAll();
        }

        async Task Invite()
        {
            var target = _selected;
            var ok = await Order(_dossierStatus, "InviteToAlliance", Args("target", target.ToString()),
                Trans.Format("vr.diplo.invited", EmpireName(target)));
            if (ok == null)
                return;
            _sentInvites[target] = FocusContext.AsInt(ok["inviteId"]);
            SaveSent();
            Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "sent", 1);
            RenderDossier();
        }

        async Task CancelInvite(int empireId)
        {
            if (!_sentInvites.TryGetValue(empireId, out var invite))
                return;
            var ok = await Order(_dossierStatus, "CancelAllianceInvite", Args("invite", invite.ToString()),
                Trans.Get("vr.diplo.inviteCancelled"));
            // Gone either way (cancelled now, or already answered / expired server-side).
            _sentInvites.Remove(empireId);
            SaveSent();
            if (ok == null)
                return;
            RenderDossier();
        }

        async Task Apply(int allianceId, string name)
        {
            var ok = await Order(_chancelleryStatus, "ApplyToAlliance", Args("alliance", allianceId.ToString()),
                Trans.Format("vr.diplo.applied", name));
            if (ok == null)
                return;
            _sentApplications[allianceId] = FocusContext.AsInt(ok["inviteId"]);
            SaveSent();
            Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "sent", 1);
            RenderChancellery();
        }

        async Task CancelApplication(int allianceId)
        {
            if (!_sentApplications.TryGetValue(allianceId, out var invite))
                return;
            await Order(_chancelleryStatus, "CancelAllianceInvite", Args("invite", invite.ToString()),
                Trans.Get("vr.diplo.applicationCancelled"));
            _sentApplications.Remove(allianceId);
            SaveSent();
            RenderChancellery();
        }

        async Task WarOrder(string action, int war, string okKey)
        {
            var w = War(war);
            var name = w != null ? EmpireName(DiplomacyService.Opponent(w)) : string.Empty;
            var ended = action is "AcceptPeaceOffer" or "SurrenderWar" or "AcceptWarDemands";
            var ok = await Order(_chancelleryStatus, action, Args("war", war.ToString()), Trans.Format(okKey, name), ended);
            if (ok == null)
                return;
            // Ends (peace, surrender, capitulation) are voiced by DiplomacyService when it sees the war close.
            if (!ended)
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "sent", 1);
        }

        async Task SaveDescription()
        {
            await Order(_chancelleryStatus, "UpdateAllianceDescription",
                Args("description", (_desc.text ?? string.Empty).Trim()), Trans.Get("vr.diplo.saved"));
        }

        async Task CreateAlliance()
        {
            var name = (_allianceName.text ?? string.Empty).Trim();
            var tag = (_allianceTag.text ?? string.Empty).Trim();
            if (name.Length == 0 || tag.Length == 0)
            {
                SetStatus(_chancelleryStatus, Trans.Get("vr.diplo.nameTagRequired"), true);
                return;
            }

            var ok = await Order(_chancelleryStatus, "CreateAlliance", Args("name", name, "tag", tag),
                Trans.Format("vr.diplo.founded", name), true);
            if (ok == null)
                return;
            _allianceName.text = string.Empty;
            _allianceTag.text = string.Empty;
            Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "sent", 1);
        }

        Task Member(string action, int empireId, string okKey, string role = null)
        {
            var args = Args("target", empireId.ToString());
            if (role != null)
                args["role"] = role;
            return Order(_chancelleryStatus, action, args, Trans.Format(okKey, EmpireName(empireId)),
                action is "KickAllianceMember");
        }

        // ── Render ────────────────────────────────────────────────────────────────

        void RenderAll()
        {
            RenderDossier();
            RenderChancellery();
        }

        TMP_Text Line(RectTransform parent, string text, float x, float y, float size, Color color, float width,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = DiegeticUi.HoloLabel(parent, text, new Vector2(x, y), new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static void Clear(RectTransform body)
        {
            for (var i = body.childCount - 1; i >= 0; i--)
                DestroyImmediate(body.GetChild(i).gameObject);
        }

        static Button Small(Button b, float min = 11f, float max = 18f)
        {
            var l = b.GetComponentInChildren<TMP_Text>();
            l.enableAutoSizing = true;
            l.fontSizeMin = min;
            l.fontSizeMax = max;
            return b;
        }

        /// <summary>Two presses for an irreversible order: the first arms it (the label asks to confirm).</summary>
        Button Confirmable(RectTransform parent, string key, string label, Vector2 pos, Vector2 size, Func<Task> action,
            DiegeticUi.BtnStyle style = DiegeticUi.BtnStyle.Danger)
        {
            var armed = _confirm == key && Time.unscaledTime < _confirmUntil;
            return Small(DiegeticUi.HoloButton(parent, armed ? Trans.Get("vr.diplo.confirm") : label, pos, size, () =>
            {
                if (_confirm == key && Time.unscaledTime < _confirmUntil)
                {
                    _confirm = null;
                    Run(action());
                    return;
                }

                _confirm = key;
                _confirmUntil = Time.unscaledTime + ConfirmWindow;
                RenderAll();
            }, armed ? DiegeticUi.BtnStyle.Amber : style));
        }

        /// <summary>A horizontal gauge (0..1) with a caption centred on it.</summary>
        void Gauge(RectTransform parent, float x, float y, float width, float value, Color fill, string caption)
        {
            var bg = new GameObject("Gauge", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(parent, false);
            var rt = bg.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, 22f);
            var img = bg.GetComponent<Image>();
            img.color = new Color(0.1f, 0.18f, 0.22f, 0.85f);
            img.raycastTarget = false;
            var fg = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fg.transform.SetParent(bg.transform, false);
            var ft = fg.GetComponent<RectTransform>();
            ft.anchorMin = new Vector2(0f, 0f);
            ft.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            ft.offsetMin = new Vector2(2f, 2f);
            ft.offsetMax = new Vector2(-2f, -2f);
            var fi = fg.GetComponent<Image>();
            fi.color = fill;
            fi.raycastTarget = false;
            Line(parent, caption, x, y, 15f, UiKit.TextBright, width, TextAlignmentOptions.Center);
        }

        void RenderDossier()
        {
            Clear(_dossierBody);
            var e = Empire(_selected);
            if (e == null)
            {
                _dossierName.text = string.Empty;
                Line(_dossierBody, Trans.Get(_empires.Count == 0 ? "Loading" : "vr.diplo.pickHint"), 0f, 60f, 20f,
                    DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            var svc = DiplomacyService.Instance;
            var id = _selected;
            var name = FocusContext.AsString(e["name"]);
            _dossierName.text = Verbatim(name);

            // Flag, identity, standing.
            var flag = new GameObject("Flag", typeof(RectTransform), typeof(RawImage));
            flag.transform.SetParent(_dossierBody, false);
            var frt = flag.GetComponent<RectTransform>();
            frt.anchoredPosition = new Vector2(-440f, 150f);
            frt.sizeDelta = new Vector2(150f, 100f);
            var raw = flag.GetComponent<RawImage>();
            raw.texture = _orrery.FlagMaterial(id, FocusContext.AsString(e["flag"])).mainTexture;
            raw.raycastTarget = false;

            var key = FocusContext.AsString(e["relation_key"]);
            var atWar = svc?.ActiveWarWith(id);
            var member = svc != null && svc.IsMember(id);
            var stanceColor = atWar != null ? WarRed : member ? UiKit.Ok : DiplomacyIndex.Tint(DiplomacyIndex.Resolve(FocusContext.AsInt(e["userid"])));
            Line(_dossierBody, "<b>" + Verbatim(name) + "</b>  <size=75%><color=#9fdcff>" +
                               Verbatim(FocusContext.AsString(e["username"])) + "</color></size>", 70f, 180f, 24f,
                UiKit.TextBright, 820f);
            var leader = (FocusContext.AsString(e["leaderTitle"]) + " " + FocusContext.AsString(e["leaderName"])).Trim();
            Line(_dossierBody, (leader.Length > 0 ? Verbatim(leader) + "   ·   " : string.Empty) + "<color=#" +
                               ColorUtility.ToHtmlStringRGB(stanceColor) + "><b>" + Trans.Get(string.IsNullOrEmpty(key) ? "unknown" : key) +
                               "</b></color>", 70f, 143f, 18f, UiKit.TextDim, 820f);
            Line(_dossierBody, Trans.Format("vr.diplo.stats", FocusContext.AsString(e["planets"]),
                FocusContext.AsString(e["fleets"]), Num(FocusContext.AsFloat(e["score"]), 1)), 70f, 110f, 17f, DiegeticUi.CyanDim, 820f);
            var rel = _relation >= 0f ? _relation : FocusContext.AsFloat(e["relation"]);
            var relColor = rel <= DiplomacyIndex.EnemyThreshold ? WarRed
                : rel >= DiplomacyIndex.GoodThreshold ? UiKit.Ok : new Color(0.55f, 0.75f, 0.9f);
            relColor.a = 0.85f;
            Gauge(_dossierBody, -130f, 72f, 580f, rel / 100f, relColor, Trans.Format("vr.diplo.relation", Num(rel, 0)));

            if (atWar != null)
            {
                RenderDossierWar(atWar);
                return;
            }

            if (member)
            {
                Line(_dossierBody, Trans.Get("vr.diplo.member"), 0f, 0f, 20f, UiKit.Ok, 1000f, TextAlignmentOptions.Center);
                return;
            }

            if (_composing)
            {
                RenderComposer(e);
                return;
            }

            Small(DiegeticUi.HoloButton(_dossierBody, Trans.Get("declareWar"), new Vector2(-300f, -20f), new Vector2(400f, 58f),
                () =>
                {
                    _composing = true;
                    _planetPage = 0;
                    _demandPlanets.Clear();
                    Array.Clear(_demand, 0, _demand.Length);
                    Run(LoadTargetPlanets());
                    RenderDossier();
                }, DiegeticUi.BtnStyle.Danger), 14f, 22f);

            if (svc != null && svc.CanManage)
            {
                if (_sentInvites.ContainsKey(id))
                    Small(DiegeticUi.HoloButton(_dossierBody, Trans.Get("vr.diplo.cancelInvite"), new Vector2(200f, -20f),
                        new Vector2(400f, 58f), () => Run(CancelInvite(id)), DiegeticUi.BtnStyle.Ghost), 14f, 22f);
                else
                    Small(DiegeticUi.HoloButton(_dossierBody, Trans.Get("inviteToAlliance"), new Vector2(200f, -20f),
                        new Vector2(400f, 58f), () => Run(Invite()), DiegeticUi.BtnStyle.Cyan), 14f, 22f);
            }

            Line(_dossierBody, Trans.Get("vr.diplo.dossierHint"), 0f, -120f, 16f, UiKit.TextDim, 1000f,
                TextAlignmentOptions.Center);
        }

        void RenderDossierWar(JToken war)
        {
            var attacker = FocusContext.AsInt(war["attackerEmpireId"]) == DiplomacyService.MyEmpireId;
            Line(_dossierBody, "<b>" + Trans.Get("war") + "</b>  <size=80%>" +
                               Trans.Get(attacker ? "vr.diplo.attacker" : "vr.diplo.defender") + "</size>", 0f, 10f, 22f, WarRed,
                1000f, TextAlignmentOptions.Center);
            var score = FocusContext.AsFloat(war["warScoreAttacker"]);
            var target = Mathf.Max(1f, FocusContext.AsFloat(war["warScoreTarget"]));
            Gauge(_dossierBody, 0f, -35f, 600f, score / target, new Color(1f, 0.35f, 0.3f, 0.85f),
                Trans.Get("warScore") + "  " + Num(score, 0) + " / " + Num(target, 0));
            Line(_dossierBody, Demands(war), 0f, -75f, 16f, UiKit.TextDim, 1000f, TextAlignmentOptions.Center);
            var warId = FocusContext.AsInt(war["id"]);
            DiegeticUi.HoloButton(_dossierBody, Trans.Get("vr.diplo.seeWar"), new Vector2(0f, -150f), new Vector2(380f, 56f), () =>
            {
                _tab = Tab.Wars;
                _warSel = warId;
                RenderChancellery();
            }, DiegeticUi.BtnStyle.Amber);
        }

        void RenderComposer(JToken e)
        {
            Line(_dossierBody, "<b>" + Trans.Get("vr.diplo.compose") + "</b>", -300f, 22f, 19f, UiKit.Amber, 440f);

            // Planets demanded: toggles over the target's worlds (GetEmpirePlanets).
            if (_targetPlanets == null)
                Line(_dossierBody, Trans.Get("Loading"), 230f, 22f, 16f, DiegeticUi.CyanDim, 560f);
            else if (_targetPlanets.Count == 0)
                Line(_dossierBody, Trans.Get("noPlanetsFound"), 230f, 22f, 16f, DiegeticUi.CyanDim, 560f);
            else
            {
                var stock = new float[3];
                foreach (var p in _targetPlanets)
                    for (var r = 0; r < 3; r++)
                        stock[r] += FocusContext.AsFloat(p[Res[r]]);
                Line(_dossierBody, Trans.Format("vr.diplo.stock", Num(stock[0], 0), Num(stock[1], 0), Num(stock[2], 0)), 230f, 22f,
                    15f, DiegeticUi.CyanDim, 560f, TextAlignmentOptions.MidlineRight);

                var pages = Mathf.Max(1, Mathf.CeilToInt(_targetPlanets.Count / (float)PlanetsPerPage));
                _planetPage = Mathf.Clamp(_planetPage, 0, pages - 1);
                var first = _planetPage * PlanetsPerPage;
                for (var i = first; i < _targetPlanets.Count && i < first + PlanetsPerPage; i++)
                {
                    var p = _targetPlanets[i];
                    var pid = FocusContext.AsInt(p["id"]);
                    var k = i - first;
                    var on = _demandPlanets.Contains(pid);
                    var pname = FocusContext.AsString(p["name"]);
                    Small(DiegeticUi.HoloButton(_dossierBody, (on ? "■ " : "□ ") + Verbatim(string.IsNullOrEmpty(pname) ? "#" + pid : pname),
                        new Vector2(-398f + (k % 4) * 245f, -25f - (k / 4) * 46f), new Vector2(236f, 40f), () =>
                        {
                            if (!_demandPlanets.Remove(pid))
                                _demandPlanets.Add(pid);
                            RenderDossier();
                        }, on ? DiegeticUi.BtnStyle.Danger : DiegeticUi.BtnStyle.Ghost), 11f, 16f);
                }

                if (pages > 1)
                    Small(DiegeticUi.HoloButton(_dossierBody, (_planetPage + 1) + "/" + pages + " ›", new Vector2(495f, -48f),
                        new Vector2(64f, 86f), () =>
                        {
                            _planetPage = (_planetPage + 1) % pages;
                            RenderDossier();
                        }, DiegeticUi.BtnStyle.Ghost), 11f, 15f);
            }

            // Resources demanded: pick one, step it; the three totals stay on the pickers.
            for (var r = 0; r < 3; r++)
            {
                var k = r;
                Small(DiegeticUi.HoloButton(_dossierBody,
                    Trans.Get("vr.res." + Res[r]) + "  <b>" + Num(_demand[r], 0) + "</b>",
                    new Vector2(-398f + r * 245f, -125f), new Vector2(236f, 40f), () =>
                    {
                        _demandRes = k;
                        RenderDossier();
                    }, _demandRes == r ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost), 11f, 16f);
            }

            for (var j = 0; j < ResSteps.Length; j++)
            {
                var step = ResSteps[j];
                Small(DiegeticUi.HoloButton(_dossierBody, (step > 0 ? "+" : "−") + Num(Mathf.Abs(step), 0),
                    new Vector2(-420f + j * 112f, -172f), new Vector2(104f, 40f), () =>
                    {
                        _demand[_demandRes] = Mathf.Max(0f, _demand[_demandRes] + step);
                        RenderDossier();
                    }, DiegeticUi.BtnStyle.Ghost), 11f, 16f);
            }

            DiegeticUi.HoloButton(_dossierBody, Trans.Get("cancel"), new Vector2(80f, -172f), new Vector2(150f, 44f), () =>
            {
                _composing = false;
                _confirm = null;
                RenderDossier();
            }, DiegeticUi.BtnStyle.Ghost);

            var any = _demandPlanets.Count > 0 || _demand[0] > 0f || _demand[1] > 0f || _demand[2] > 0f;
            var go = Confirmable(_dossierBody, "declare", Trans.Get("declareWar"), new Vector2(345f, -172f),
                new Vector2(350f, 50f), DeclareWar);
            go.interactable = any && !_busy;
        }

        // ── Chancellery ───────────────────────────────────────────────────────────

        void SetTab(Tab tab)
        {
            _tab = tab;
            _confirm = null;
            SetStatus(_chancelleryStatus, string.Empty);
            RenderChancellery();
        }

        void RenderChancellery()
        {
            for (var i = 0; i < _tabs.Length; i++)
                _tabs[i].GetComponent<Image>().color = i == (int)_tab ? new Color(0.6f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.45f);
            Clear(_chancelleryBody);
            var svc = DiplomacyService.Instance;
            var inAlliance = svc?.Alliance != null;
            _descGroup.SetActive(_tab == Tab.Alliance && inAlliance && svc.CanManage);
            _createGroup.SetActive(_tab == Tab.Alliance && svc != null && svc.Loaded && !inAlliance);
            if (svc == null || !svc.Loaded)
            {
                Line(_chancelleryBody, Trans.Get("Loading"), 0f, 60f, 20f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            switch (_tab)
            {
                case Tab.Wars:
                    RenderWars(svc);
                    break;
                case Tab.Alliance:
                    if (inAlliance)
                        RenderAlliance(svc);
                    else
                        RenderUnaligned(svc);
                    break;
                default:
                    RenderRegistry(svc);
                    break;
            }
        }

        JToken War(int id)
        {
            var svc = DiplomacyService.Instance;
            if (svc == null)
                return null;
            foreach (var w in svc.Wars)
                if (FocusContext.AsInt(w["id"]) == id)
                    return w;
            return null;
        }

        void RenderWars(DiplomacyService svc)
        {
            var list = new List<JToken>();
            foreach (var w in svc.Wars)
                if (DiplomacyService.IsActive(w))
                    list.Add(w);
            foreach (var w in svc.Wars)
                if (!DiplomacyService.IsActive(w))
                    list.Add(w);
            if (list.Count == 0)
            {
                Line(_chancelleryBody, Trans.Get("vr.diplo.noWar"), 0f, 60f, 20f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                Line(_chancelleryBody, Trans.Get("vr.diplo.noWarHint"), 0f, 10f, 16f, UiKit.TextDim, 1000f,
                    TextAlignmentOptions.Center);
                return;
            }

            if (War(_warSel) == null)
                _warSel = FocusContext.AsInt(list[0]["id"]);
            var pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)WarsPerPage));
            _warPage = Mathf.Clamp(_warPage, 0, pages - 1);
            var first = _warPage * WarsPerPage;
            var me = DiplomacyService.MyEmpireId;
            for (var i = first; i < list.Count && i < first + WarsPerPage; i++)
            {
                var w = list[i];
                var id = FocusContext.AsInt(w["id"]);
                var y = 180f - (i - first) * 52f;
                var picked = id == _warSel;
                var active = DiplomacyService.IsActive(w);
                var row = DiegeticUi.HoloButton(_chancelleryBody, string.Empty, new Vector2(0f, y), new Vector2(1060f, 46f), () =>
                {
                    _warSel = id;
                    _confirm = null;
                    RenderChancellery();
                    Run(WarDetails(id));
                }, picked ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                row.GetComponent<Image>().color = picked ? new Color(0.6f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.22f);
                var attacker = FocusContext.AsInt(w["attackerEmpireId"]) == me;
                Line(_chancelleryBody, "<b>" + Verbatim(EmpireName(DiplomacyService.Opponent(w))) + "</b>  <size=80%><color=" +
                                       (active ? "#ff7a6b" : "#8fb3c4") + ">" +
                                       Trans.Get(attacker ? "vr.diplo.attacker" : "vr.diplo.defender") + " · " +
                                       Trans.Get("warStatus_" + FocusContext.AsString(w["status"])) + "</color></size>",
                    -250f, y, 18f, UiKit.TextBright, 540f);
                var score = FocusContext.AsFloat(w["warScoreAttacker"]);
                var target = Mathf.Max(1f, FocusContext.AsFloat(w["warScoreTarget"]));
                Gauge(_chancelleryBody, 330f, y, 360f, score / target, new Color(1f, 0.35f, 0.3f, active ? 0.85f : 0.35f),
                    Num(score, 0) + " / " + Num(target, 0));
            }

            if (pages > 1)
                Small(DiegeticUi.HoloButton(_chancelleryBody, (_warPage + 1) + "/" + pages + " ›", new Vector2(470f, -22f),
                    new Vector2(110f, 40f), () =>
                    {
                        _warPage = (_warPage + 1) % pages;
                        RenderChancellery();
                    }, DiegeticUi.BtnStyle.Ghost), 12f, 16f);

            var sel = War(_warSel);
            if (sel == null)
                return;
            var warId = FocusContext.AsInt(sel["id"]);
            Line(_chancelleryBody, Demands(sel), -60f, -22f, 16f, UiKit.TextDim, 920f);
            if (!DiplomacyService.IsActive(sel))
            {
                var ended = FocusContext.AsLong(sel["endedAt"]);
                Line(_chancelleryBody, Trans.Format("vr.diplo.endedOn", Date(ended)), 0f, -110f, 17f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                return;
            }

            var offerBy = FocusContext.AsInt(sel["pendingPeaceBy"]);
            var name = EmpireName(DiplomacyService.Opponent(sel));
            var peaceText = offerBy <= 0 ? string.Empty
                : offerBy == me ? Trans.Get("vr.diplo.peaceFromMe") : Trans.Format("vr.diplo.peaceFromThem", name);
            if (peaceText.Length > 0)
                Line(_chancelleryBody, peaceText, 0f, -70f, 17f, offerBy == me ? DiegeticUi.CyanDim : UiKit.Ok, 1000f,
                    TextAlignmentOptions.Center);

            if (offerBy == me)
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("cancelPeaceOffer"), new Vector2(-330f, -150f),
                    new Vector2(380f, 54f), () => Run(WarOrder("CancelPeaceOffer", warId, "vr.diplo.peaceWithdrawn")),
                    DiegeticUi.BtnStyle.Ghost), 13f, 19f);
            else if (offerBy > 0)
            {
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("acceptPeaceOffer"), new Vector2(-400f, -150f),
                    new Vector2(250f, 54f), () => Run(WarOrder("AcceptPeaceOffer", warId, "vr.diplo.peaceSigned")),
                    DiegeticUi.BtnStyle.Cyan), 13f, 19f);
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("declinePeaceOffer"), new Vector2(-140f, -150f),
                    new Vector2(250f, 54f), () => Run(WarOrder("DeclinePeaceOffer", warId, "vr.diplo.peaceRefused")),
                    DiegeticUi.BtnStyle.Ghost), 13f, 19f);
            }
            else
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("offerPeace"), new Vector2(-330f, -150f),
                    new Vector2(380f, 54f), () => Run(WarOrder("OfferPeace", warId, "vr.diplo.peaceOffered")),
                    DiegeticUi.BtnStyle.Cyan), 13f, 19f);

            // Attacker gives up (no reparations); defender capitulates (the attacker gets every demand now).
            var attackerSide = FocusContext.AsInt(sel["attackerEmpireId"]) == me;
            Confirmable(_chancelleryBody, "end" + warId, Trans.Get(attackerSide ? "surrenderWar" : "acceptWarDemands"),
                new Vector2(300f, -150f), new Vector2(400f, 54f),
                () => WarOrder(attackerSide ? "SurrenderWar" : "AcceptWarDemands", warId,
                    attackerSide ? "vr.diplo.surrendered" : "vr.diplo.capitulated"));
            Line(_chancelleryBody, Trans.Get(attackerSide ? "vr.diplo.surrenderHint" : "vr.diplo.capitulateHint"), 300f, -195f,
                13f, UiKit.TextDim, 440f, TextAlignmentOptions.Center);
        }

        async Task WarDetails(int id)
        {
            var res = await ActionJs.Get("GetWarDetails", Args("war", id.ToString()));
            if (!res.Ok || DiplomacyService.Instance == null)
                return;
            JObject fresh;
            try
            {
                fresh = JObject.Parse(res.Body);
            }
            catch
            {
                return;
            }

            var wars = DiplomacyService.Instance.Wars;
            for (var i = 0; i < wars.Count; i++)
                if (FocusContext.AsInt(wars[i]["id"]) == id)
                    wars[i] = fresh;
            if (Inside && _tab == Tab.Wars && _warSel == id)
                RenderChancellery();
        }

        /// <summary>The attacker's demands: planets (named when we can) and resource quantities.</summary>
        string Demands(JToken war)
        {
            var parts = new List<string>();
            try
            {
                var ids = JArray.Parse(FocusContext.AsString(war["demandPlanetIds"]) is { Length: > 0 } s ? s : "[]");
                if (ids.Count > 0)
                    parts.Add(Trans.Format("vr.diplo.planetsN", ids.Count));
            }
            catch
            {
                // No planet demand.
            }

            foreach (var r in Res)
            {
                var v = FocusContext.AsFloat(war["demand" + char.ToUpperInvariant(r[0]) + r.Substring(1)]);
                if (v > 0f)
                    parts.Add(Num(v, 0) + " " + Trans.Get("vr.res." + r));
            }

            return parts.Count == 0 ? string.Empty : Trans.Format("vr.diplo.demands", string.Join(" · ", parts));
        }

        void RenderAlliance(DiplomacyService svc)
        {
            var a = svc.Alliance;
            var role = svc.MyRole;
            var members = a["members"] as JArray ?? new JArray();
            Line(_chancelleryBody, "<b>" + Verbatim(FocusContext.AsString(a["name"])) + "</b>  <color=#ffd27a>[" +
                                   Verbatim(FocusContext.AsString(a["tag"])) + "]</color>  <size=75%><color=#9fdcff>" +
                                   Trans.Get("role_" + role) + " · " + members.Count + " " + Trans.Get("allianceMembers") +
                                   "</color></size>", 0f, 185f, 21f, UiKit.TextBright, 1040f);
            if (!svc.CanManage)
                Line(_chancelleryBody, Verbatim(FocusContext.AsString(a["description"])), 0f, 140f, 15f, UiKit.TextDim, 1040f);
            else if (!_desc.isFocused && _desc.text != FocusContext.AsString(a["description"]))
                _desc.text = FocusContext.AsString(a["description"]);

            var me = DiplomacyService.MyEmpireId;
            var isLeader = role == "leader";
            var pages = Mathf.Max(1, Mathf.CeilToInt(members.Count / (float)MembersPerPage));
            _memberPage = Mathf.Clamp(_memberPage, 0, pages - 1);
            var first = _memberPage * MembersPerPage;
            for (var i = first; i < members.Count && i < first + MembersPerPage; i++)
            {
                var m = members[i];
                var eid = FocusContext.AsInt(m["empireId"]);
                var mrole = FocusContext.AsString(m["role"]);
                var self = eid == me;
                var y = 88f - (i - first) * 48f;
                Line(_chancelleryBody, "<b>" + Verbatim(FocusContext.AsString(m["empireName"])) + "</b>" +
                                       (self ? "  <size=75%><color=#7fd8ff>" + Trans.Get("vr.diplo.you") + "</color></size>" : string.Empty) +
                                       "  <size=78%><color=#ffd27a>" + Trans.Get("role_" + mrole) + "</color></size>",
                    -290f, y, 18f, UiKit.TextBright, 480f);

                // Web rules (AllianceWindowUI): officers kick members, the leader promotes / demotes / transfers.
                var x = 510f;
                var canKick = svc.CanManage && !self && mrole != "leader" && (isLeader || mrole == "member");
                if (canKick)
                {
                    Confirmable(_chancelleryBody, "kick" + eid, Trans.Get("kickMember"), new Vector2(x - 75f, y), new Vector2(140f, 40f),
                        () => Member("KickAllianceMember", eid, "vr.diplo.kicked"));
                    x -= 150f;
                }

                if (isLeader && !self)
                {
                    Confirmable(_chancelleryBody, "lead" + eid, Trans.Get("transferLeadership"), new Vector2(x - 90f, y),
                        new Vector2(170f, 40f), () => Member("TransferAllianceLeadership", eid, "vr.diplo.transferred"),
                        DiegeticUi.BtnStyle.Amber);
                    x -= 180f;
                    if (mrole == "member")
                        Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("promoteToOfficer"), new Vector2(x - 90f, y),
                            new Vector2(170f, 40f), () => Run(Member("SetAllianceMemberRole", eid, "vr.diplo.promoted", "officer")),
                            DiegeticUi.BtnStyle.Ghost));
                    else if (mrole == "officer")
                        Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("demoteToMember"), new Vector2(x - 90f, y),
                            new Vector2(170f, 40f), () => Run(Member("SetAllianceMemberRole", eid, "vr.diplo.demoted", "member")),
                            DiegeticUi.BtnStyle.Ghost));
                }
            }

            if (pages > 1)
                Small(DiegeticUi.HoloButton(_chancelleryBody, (_memberPage + 1) + "/" + pages + " ›", new Vector2(-470f, -190f),
                    new Vector2(110f, 40f), () =>
                    {
                        _memberPage = (_memberPage + 1) % pages;
                        RenderChancellery();
                    }, DiegeticUi.BtnStyle.Ghost), 12f, 16f);

            // Applications waiting (officers): the first one with its answer buttons, and how many more.
            if (svc.CanManage && a["applications"] is JArray apps && apps.Count > 0)
            {
                var app = apps[0];
                var appId = FocusContext.AsInt(app["id"]);
                Line(_chancelleryBody, "<color=#ffd27a>" + Trans.Get("allianceApplications") + " (" + apps.Count + ")</color>  <b>" +
                                       Verbatim(FocusContext.AsString(app["empireName"])) + "</b>", -250f, -118f, 17f,
                    UiKit.TextBright, 540f);
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("accept"), new Vector2(180f, -118f), new Vector2(160f, 42f),
                    () => Run(Order(_chancelleryStatus, "AcceptAllianceApplication", Args("application", appId.ToString()),
                        Trans.Get("vr.diplo.applicationAccepted"), true)), DiegeticUi.BtnStyle.Cyan));
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("decline"), new Vector2(350f, -118f), new Vector2(160f, 42f),
                    () => Run(Order(_chancelleryStatus, "DeclineAllianceApplication", Args("application", appId.ToString()),
                        Trans.Get("vr.diplo.applicationDeclined"))), DiegeticUi.BtnStyle.Ghost));
            }

            if (isLeader)
                Confirmable(_chancelleryBody, "disband", Trans.Get("disbandAlliance"), new Vector2(360f, -190f),
                    new Vector2(320f, 46f), () => Order(_chancelleryStatus, "DisbandAlliance", new Dictionary<string, string>(),
                        Trans.Get("vr.diplo.disbanded"), true));
            else
                Confirmable(_chancelleryBody, "leave", Trans.Get("leaveAlliance"), new Vector2(360f, -190f),
                    new Vector2(320f, 46f), () => Order(_chancelleryStatus, "LeaveAlliance", new Dictionary<string, string>(),
                        Trans.Get("vr.diplo.left"), true));
        }

        void RenderUnaligned(DiplomacyService svc)
        {
            Line(_chancelleryBody, "<b>" + Trans.Get("noAlliance") + "</b>", 0f, 185f, 20f, UiKit.TextBright, 1040f,
                TextAlignmentOptions.Center);
            var invites = svc.Invites;
            if (invites.Count == 0)
                Line(_chancelleryBody, Trans.Get("vr.diplo.noInvite"), 0f, 130f, 16f, UiKit.TextDim, 1040f,
                    TextAlignmentOptions.Center);
            for (var i = 0; i < invites.Count && i < 3; i++)
            {
                var inv = invites[i];
                var id = FocusContext.AsInt(inv["id"]);
                var y = 130f - i * 50f;
                Line(_chancelleryBody, "<color=#ffd27a>" + Trans.Get("allianceInvites") + "</color>  <b>" +
                                       Verbatim(FocusContext.AsString(inv["allianceName"])) + "</b> [" +
                                       Verbatim(FocusContext.AsString(inv["allianceTag"])) + "]", -250f, y, 18f,
                    UiKit.TextBright, 540f);
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("accept"), new Vector2(180f, y), new Vector2(160f, 42f),
                    () => Run(Order(_chancelleryStatus, "AcceptAllianceInvite", Args("invite", id.ToString()),
                        Trans.Get("vr.diplo.joined"), true)), DiegeticUi.BtnStyle.Cyan));
                Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("decline"), new Vector2(350f, y), new Vector2(160f, 42f),
                    () => Run(Order(_chancelleryStatus, "DeclineAllianceInvite", Args("invite", id.ToString()),
                        Trans.Get("vr.diplo.inviteDeclined"))), DiegeticUi.BtnStyle.Ghost));
            }

            Line(_chancelleryBody, "<b>" + Trans.Get("createAlliance") + "</b>", -300f, -70f, 18f, UiKit.Amber, 440f);
            Line(_chancelleryBody, Trans.Get("vr.diplo.registryHint"), 0f, -185f, 15f, UiKit.TextDim, 1040f,
                TextAlignmentOptions.Center);
        }

        void RenderRegistry(DiplomacyService svc)
        {
            if (_alliances.Count == 0)
            {
                Line(_chancelleryBody, Trans.Get("vr.diplo.noAlliances"), 0f, 60f, 19f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                return;
            }

            var mine = FocusContext.AsInt(svc.Alliance?["id"]);
            var pages = Mathf.Max(1, Mathf.CeilToInt(_alliances.Count / (float)AlliancesPerPage));
            _registryPage = Mathf.Clamp(_registryPage, 0, pages - 1);
            var first = _registryPage * AlliancesPerPage;
            for (var i = first; i < _alliances.Count && i < first + AlliancesPerPage; i++)
            {
                var a = _alliances[i];
                var id = FocusContext.AsInt(a["id"]);
                var y = 178f - (i - first) * 72f;
                var name = FocusContext.AsString(a["name"]);
                Line(_chancelleryBody, "<b>" + Verbatim(name) + "</b>  <color=#ffd27a>[" + Verbatim(FocusContext.AsString(a["tag"])) +
                                       "]</color>  <size=78%><color=#9fdcff>" + FocusContext.AsString(a["memberCount"]) + " " +
                                       Trans.Get("allianceMembers") + "</color></size>", -170f, y + 12f, 18f, UiKit.TextBright, 700f);
                Line(_chancelleryBody, Verbatim(FocusContext.AsString(a["description"])), -170f, y - 14f, 14f, UiKit.TextDim, 700f);
                if (id == mine)
                    Line(_chancelleryBody, Trans.Get("myAlliance"), 380f, y, 17f, UiKit.Ok, 280f, TextAlignmentOptions.Center);
                else if (_sentApplications.ContainsKey(id))
                    Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("vr.diplo.cancelApply"), new Vector2(380f, y),
                        new Vector2(280f, 46f), () => Run(CancelApplication(id)), DiegeticUi.BtnStyle.Ghost));
                else if (mine == 0)
                    Small(DiegeticUi.HoloButton(_chancelleryBody, Trans.Get("applyToAlliance"), new Vector2(380f, y),
                        new Vector2(280f, 46f), () => Run(Apply(id, name)), DiegeticUi.BtnStyle.Cyan));
            }

            if (pages > 1)
                Small(DiegeticUi.HoloButton(_chancelleryBody, (_registryPage + 1) + "/" + pages + " ›", new Vector2(470f, -200f),
                    new Vector2(110f, 40f), () =>
                    {
                        _registryPage = (_registryPage + 1) % pages;
                        RenderChancellery();
                    }, DiegeticUi.BtnStyle.Ghost), 12f, 16f);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        string EmpireName(int empireId)
        {
            var e = Empire(empireId);
            var n = FocusContext.AsString(e?["name"]);
            return string.IsNullOrEmpty(n) ? DiplomacyIndex.EmpireName(empireId) : n;
        }

        static void SetStatus(TMP_Text status, string text, bool error = false)
        {
            status.text = text ?? string.Empty;
            status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        static string Verbatim(string s) =>
            "<noparse>" + (s ?? string.Empty).Replace("</noparse>", "</ noparse>").Replace('\n', ' ') + "</noparse>";

        static string Num(float v, int decimals) =>
            v.ToString(decimals == 0 ? "N0" : "N" + decimals, CultureInfo.GetCultureInfo("fr-FR"));

        static string Date(long unix) =>
            unix <= 0 ? "—" : DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        static JArray ParseArray(ApiResult res) => res.Ok ? ParseArray(res.Body) : new JArray();

        static JArray ParseArray(string body)
        {
            if (string.IsNullOrEmpty(body))
                return new JArray();
            try
            {
                return JToken.Parse(body) as JArray ?? new JArray();
            }
            catch
            {
                return new JArray();
            }
        }

        /// <summary>
        /// Invitations and applications we sent (their ids come back only in the order's answer: the server lists
        /// neither — docs/PARITY.md), kept on the headset so they can be withdrawn later.
        /// </summary>
        void LoadSent()
        {
            try
            {
                var o = JObject.Parse(PlayerPrefs.GetString(SentKey, "{}"));
                if (o["inv"] is JObject inv)
                    foreach (var p in inv.Properties())
                        _sentInvites[int.Parse(p.Name)] = (int)p.Value;
                if (o["app"] is JObject app)
                    foreach (var p in app.Properties())
                        _sentApplications[int.Parse(p.Name)] = (int)p.Value;
            }
            catch
            {
                // Nothing remembered.
            }
        }

        void SaveSent()
        {
            var inv = new JObject();
            foreach (var kv in _sentInvites)
                inv[kv.Key.ToString()] = kv.Value;
            var app = new JObject();
            foreach (var kv in _sentApplications)
                app[kv.Key.ToString()] = kv.Value;
            PlayerPrefs.SetString(SentKey, new JObject { ["inv"] = inv, ["app"] = app }.ToString(Newtonsoft.Json.Formatting.None));
            PlayerPrefs.Save();
        }

        void Update()
        {
            if (!Inside)
                return;
            if (_confirm != null && Time.unscaledTime >= _confirmUntil)
            {
                _confirm = null;
                RenderAll();
            }
        }
    }
}
