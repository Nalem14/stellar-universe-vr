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
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// Ops station console — planet stewardship (web scenes/planet.js: header strip + Bâtiments / Décisions /
    /// Stats tabs, adapted to a holo screen the Ops officer brings up in front of the captain).
    /// Planet picker over every owned planet; resources strip; buildings with server-exact quotes
    /// (upgrade / add to queue / downgrade with confirm); construction queue (active build with Nova finish,
    /// cancel queued items by queue row id); hourly planet decisions; planet report; rename.
    /// Data: <see cref="EconomyService"/> (GetResource on all planets), decisions fetched per planet on demand.
    /// </summary>
    public sealed class OpsConsole : MonoBehaviour
    {
        enum Tab
        {
            Buildings,
            Queue,
            Decisions,
            Report
        }

        static readonly Vector2 Size = new(1.1f, 0.7f);
        static readonly Color Accent = new(0.35f, 0.75f, 1f, 1f);
        const float Reach = 1.25f;
        const float MaxBearing = 20f;
        const int BuildingsPerPage = 6;
        const int DecisionsPerPage = 3;
        const int NameLimit = 32;

        public static OpsConsole Instance { get; private set; }
        public bool IsOpen => _open;

        HoloScreen _screen;
        RectTransform _frame;
        RectTransform _body;
        TMP_Text _planetLabel;
        TMP_Text _status;
        TMP_InputField _nameField;
        GameObject _renameGroup;
        GameObject _planetGroup;
        readonly TMP_Text[] _chips = new TMP_Text[5];
        readonly Button[] _tabs = new Button[4];
        readonly List<(TMP_Text Text, Func<string> Value)> _live = new();
        readonly List<(Image Fill, Func<float> Value)> _bars = new();
        readonly List<GalaxyCatalog.PlanetRef> _owned = new();

        EconomyService _eco;
        Transform _anchor;
        bool _open;
        int _planetId;
        Tab _tab;
        int _page;
        float _nextLive;
        string _confirmDowngrade;
        float _confirmUntil;
        bool _busy;

        JArray _decisions;
        long _decisionsNext;
        int _decisionsFor;
        bool _decisionsLoading;

        public static OpsConsole Build(Transform room, EconomyService eco)
        {
            var rig = new GameObject("OpsConsoleRig").transform;
            rig.SetParent(room, false);
            var console = rig.gameObject.AddComponent<OpsConsole>();
            console._eco = eco;
            console.BuildScreen(rig);
            rig.gameObject.SetActive(false);
            return console;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (_eco != null)
                _eco.Changed -= OnEconomy;
            if (Instance == this)
                Instance = null;
        }

        // ── Shell ──────────────────────────────────────────────────────────────────

        void BuildScreen(Transform rig)
        {
            _screen = HoloScreen.Create(rig, "OpsConsole", Size, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.station.ops"));
            _screen.SetAccent(Accent, 0.5f);
            _frame = _screen.Content;

            _planetGroup = Group("PlanetBar");
            Button(_planetGroup.transform, "‹", new Vector2(-505f, 245f), new Vector2(64f, 46f), () => StepPlanet(-1),
                DiegeticUi.BtnStyle.Ghost);
            _planetLabel = DiegeticUi.HoloLabel(_planetGroup.transform, string.Empty, new Vector2(-235f, 245f),
                new Vector2(460f, 46f), 26f, UiKit.TextBright);
            _planetLabel.fontStyle = FontStyles.Bold;
            _planetLabel.enableAutoSizing = true;
            _planetLabel.fontSizeMin = 18f;
            _planetLabel.fontSizeMax = 26f;
            Button(_planetGroup.transform, "›", new Vector2(35f, 245f), new Vector2(64f, 46f), () => StepPlanet(1),
                DiegeticUi.BtnStyle.Ghost);
            Button(_planetGroup.transform, Trans.Get("rename"), new Vector2(185f, 245f), new Vector2(190f, 46f),
                BeginRename, DiegeticUi.BtnStyle.Ghost);

            _renameGroup = Group("RenameBar");
            _nameField = DiegeticUi.HoloField(_renameGroup.transform, "PlanetName", Trans.Get("rename"),
                new Vector2(-235f, 245f), new Vector2(520f, 50f), TouchScreenKeyboardType.Default);
            _nameField.characterLimit = NameLimit;
            Button(_renameGroup.transform, Trans.Get("vr.common.ok"), new Vector2(115f, 245f), new Vector2(130f, 46f),
                () => Run(Rename()), DiegeticUi.BtnStyle.Cyan);
            Button(_renameGroup.transform, Trans.Get("cancel"), new Vector2(255f, 245f), new Vector2(130f, 46f),
                EndRename, DiegeticUi.BtnStyle.Ghost);
            _renameGroup.SetActive(false);

            Button(_frame, Trans.Get("close"), new Vector2(465f, 245f), new Vector2(140f, 46f), Close,
                DiegeticUi.BtnStyle.Ghost);

            for (var i = 0; i < _chips.Length; i++)
            {
                var chip = DiegeticUi.HoloSelectTray(_frame, new Vector2(-440f + i * 220f, 186f), new Vector2(208f, 58f));
                _chips[i] = DiegeticUi.HoloLabel(chip, string.Empty, Vector2.zero, new Vector2(196f, 54f), 17f,
                    UiKit.TextBright);
                _chips[i].lineSpacing = -12f;
            }

            var tabKeys = new[] { "buildings", "vr.ops.tab.queue", "vr.ops.tab.decisions", "stats" };
            for (var i = 0; i < _tabs.Length; i++)
            {
                var tab = (Tab)i;
                _tabs[i] = Button(_frame, Trans.Get(tabKeys[i]), new Vector2(-405f + i * 270f, 128f),
                    new Vector2(258f, 46f), () => SetTab(tab), DiegeticUi.BtnStyle.Ghost);
            }

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(_frame, false);
            _body = bodyGo.GetComponent<RectTransform>();
            _body.sizeDelta = new Vector2(1060f, 420f);

            _status = DiegeticUi.HoloLabel(_frame, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f),
                18f, DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);
        }

        GameObject Group(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_frame, false);
            go.GetComponent<RectTransform>().sizeDelta = _screen.PixelSize;
            return go;
        }

        static Button Button(Transform parent, string label, Vector2 pos, Vector2 size, Action onClick,
            DiegeticUi.BtnStyle style) =>
            DiegeticUi.HoloButton(parent, label, pos, size, () => onClick?.Invoke(), style);

        // ── Open / close ───────────────────────────────────────────────────────────

        /// <summary>
        /// Bring the console up in front of the captain, on the Ops officer's bearing.
        /// <paramref name="preferredPlanet"/>: the station's planet or the one in orbit when it is ours.
        /// </summary>
        public void Open(Transform officerAnchor, int preferredPlanet)
        {
            _anchor = officerAnchor;
            _open = true;
            gameObject.SetActive(true);
            Place();
            _eco.Changed -= OnEconomy;
            _eco.Changed += OnEconomy;
            _eco.CollectOwned(_owned);
            _planetId = PickPlanet(preferredPlanet);
            _page = 0;
            _tab = Tab.Buildings;
            EndRename();
            SetStatus(string.Empty);
            Render();
            CicCue.Ok(transform.position);
            Run(_eco.RefreshNow());
        }

        public void Close()
        {
            if (_open && _anchor != null)
                _anchor.GetComponentInParent<CrewOfficer>()?.LookAt(null);
            _open = false;
            if (_eco != null)
                _eco.Changed -= OnEconomy;
            gameObject.SetActive(false);
        }

        int PickPlanet(int preferred)
        {
            foreach (var p in _owned)
            {
                if (p.Id == preferred)
                    return preferred;
            }

            foreach (var p in _owned)
            {
                if (p.Id == _planetId)
                    return _planetId;
            }

            return _owned.Count > 0 ? _owned[0].Id : 0;
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

        void OnEconomy()
        {
            if (_open && !_busy)
                Render();
        }

        void StepPlanet(int delta)
        {
            if (_owned.Count == 0)
                return;
            var i = _owned.FindIndex(p => p.Id == _planetId);
            i = ((i < 0 ? 0 : i) + delta + _owned.Count) % _owned.Count;
            _planetId = _owned[i].Id;
            _page = 0;
            _decisions = null;
            EndRename();
            Render();
        }

        void SetTab(Tab tab)
        {
            _tab = tab;
            _page = 0;
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
            _live.Clear();
            _bars.Clear();
            for (var i = _body.childCount - 1; i >= 0; i--)
                DestroyImmediate(_body.GetChild(i).gameObject);

            for (var i = 0; i < _tabs.Length; i++)
                SetButtonStyle(_tabs[i], (Tab)i == _tab);

            _eco.TryGet(_planetId, out var planet);
            var star = GalaxyCatalog.TryGet(planet?.SystemId ?? 0, out var s) ? s.Label : string.Empty;
            var name = planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : "#" + _planetId;
            _planetLabel.text = _planetId <= 0
                ? Trans.Get("vr.ops.noPlanet")
                : name + "   <size=70%><color=#7fd8ff>" + star + "</color></size>";
            RenderChips(planet);

            if (planet == null)
            {
                Line(Trans.Get(_planetId <= 0 ? "vr.ops.noPlanet" : "Loading"), 0f, 60f, 22f, DiegeticUi.CyanDim);
                return;
            }

            switch (_tab)
            {
                case Tab.Buildings:
                    RenderBuildings(planet);
                    break;
                case Tab.Queue:
                    RenderQueue(planet);
                    break;
                case Tab.Decisions:
                    RenderDecisions(planet);
                    break;
                case Tab.Report:
                    RenderReport(planet);
                    break;
            }
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

        void RenderChips(PlanetEconomy p)
        {
            if (p == null)
            {
                foreach (var c in _chips)
                    c.text = string.Empty;
                return;
            }

            var deficit = p.EnergyUsed > p.Energy;
            _chips[0].text = Head("energy") + Tone(Num(p.EnergyUsed) + " / " + Num(p.Energy), deficit);
            _chips[1].text = Head("vr.res.mineral") + Tone(Num(p.Mineral) + " / " + Num(p.MineralStorage),
                p.Mineral >= p.MineralStorage);
            _chips[2].text = Head("vr.res.crystal") + Tone(Num(p.Crystal) + " / " + Num(p.CrystalStorage),
                p.Crystal >= p.CrystalStorage);
            _chips[3].text = Head("vr.res.biomass") + Tone(Num(p.Biomass) + " / " + Num(p.BiomassStorage),
                p.Biomass >= p.BiomassStorage);
            _chips[4].text = Head("population") + Num(p.Citizen) + "   <size=80%>" +
                             Trans.Format("vr.ops.jobs", p.Employed, p.Jobs) + "</size>";
        }

        static string Head(string key) => "<size=75%><color=#7fd8ff>" + Trans.Get(key) + "</color></size>\n";

        /// <summary>Full storage / energy deficit reads amber, like the web's capped bars.</summary>
        static string Tone(string text, bool warn) => warn ? "<color=#ffb347>" + text + "</color>" : text;

        static string Num(float v) => Mathf.RoundToInt(v).ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

        TMP_Text Line(string text, float x, float y, float size, Color color, float width = 1000f,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(x, y), new Vector2(width, size * 1.9f), size, color,
                align);
            t.richText = true;
            return t;
        }

        void Pager(int total, int perPage)
        {
            var pages = Mathf.Max(1, Mathf.CeilToInt(total / (float)perPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            if (pages <= 1)
                return;
            DiegeticUi.HoloButton(_body, "‹", new Vector2(310f, -305f), new Vector2(64f, 44f), () =>
            {
                _page = (_page - 1 + pages) % pages;
                Render();
            }, DiegeticUi.BtnStyle.Ghost);
            Line((_page + 1) + " / " + pages, 400f, -305f, 20f, DiegeticUi.CyanDim, 100f);
            DiegeticUi.HoloButton(_body, "›", new Vector2(490f, -305f), new Vector2(64f, 44f), () =>
            {
                _page = (_page + 1) % pages;
                Render();
            }, DiegeticUi.BtnStyle.Ghost);
        }

        // ── Buildings ─────────────────────────────────────────────────────────────

        void RenderBuildings(PlanetEconomy planet)
        {
            var all = BuildingCatalog.All;
            Pager(all.Length, BuildingsPerPage);
            var first = _page * BuildingsPerPage;
            var building = !string.IsNullOrEmpty(BuildingCatalog.ActiveType(planet));
            for (var i = first; i < all.Length && i < first + BuildingsPerPage; i++)
            {
                var def = all[i];
                var y = 72f - (i - first) * 62f;
                var q = BuildingCatalog.Quote(planet, def, _eco);

                var bar = new GameObject("Accent", typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(_body, false);
                var rt = bar.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(8f, 50f);
                rt.anchoredPosition = new Vector2(-522f, y);
                var img = bar.GetComponent<Image>();
                img.color = def.Accent;
                img.raycastTarget = false;

                // The name reads the building's role on the status line (web card description).
                var nameBtn = DiegeticUi.HoloButton(_body, string.Empty, new Vector2(-330f, y), new Vector2(360f, 54f),
                    () => SetStatus(Trans.Get(def.NameKey) + " — " + Trans.Get(def.DescKey)), DiegeticUi.BtnStyle.Ghost);
                nameBtn.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
                var level = q.UnderConstruction
                    ? Trans.Get("level") + " " + q.Level + " <color=#ffb347>→ " + (q.Level + 1) + "</color>"
                    : Trans.Get("level") + " " + q.Level;
                Line("<b>" + Trans.Get(def.NameKey) + "</b>   <size=80%>" + level + "</size>", -330f, y, 21f,
                    UiKit.TextBright, 340f, TextAlignmentOptions.MidlineLeft);

                var costColor = q.Affordable ? new Color(0.78f, 0.9f, 0.96f, 1f) : UiKit.Danger;
                Line(CostText(q), 40f, y, 18f, costColor, 360f, TextAlignmentOptions.MidlineLeft);

                string label;
                DiegeticUi.BtnStyle style;
                var enabled = q.BlockKey == null;
                if (!enabled)
                {
                    label = q.BlockKey == "notEnoughtResearchLevel"
                        ? Trans.Format("vr.ops.requires", Trans.Get(def.ResearchKey), def.ResearchLevel)
                        : Trans.Get(q.BlockKey);
                    style = DiegeticUi.BtnStyle.Ghost;
                }
                else if (building)
                {
                    label = Trans.Get("addToQueue");
                    style = DiegeticUi.BtnStyle.Amber;
                }
                else
                {
                    label = Trans.Get("vr.ops.upgrade");
                    style = DiegeticUi.BtnStyle.Cyan;
                }

                var type = def.Type;
                var up = DiegeticUi.HoloButton(_body, label, new Vector2(340f, y), new Vector2(230f, 50f),
                    () => Run(Upgrade(type)), style);
                up.interactable = enabled;
                var upLabel = up.GetComponentInChildren<TMP_Text>();
                upLabel.enableAutoSizing = true;
                upLabel.fontSizeMin = 12f;
                upLabel.fontSizeMax = 19f;

                if (q.Level > 0 && !q.UnderConstruction)
                {
                    var confirming = _confirmDowngrade == type && Time.unscaledTime < _confirmUntil;
                    var down = DiegeticUi.HoloButton(_body, confirming ? "?" : "−", new Vector2(492f, y),
                        new Vector2(58f, 50f), () => Downgrade(type),
                        confirming ? DiegeticUi.BtnStyle.Danger : DiegeticUi.BtnStyle.Ghost);
                    down.GetComponentInChildren<TMP_Text>().fontSize = 26f;
                }
            }

            SetFooter(planet);
        }

        void SetFooter(PlanetEconomy planet)
        {
            if (!string.IsNullOrEmpty(_status.text))
                return;
            _status.text = Trans.Format("vr.ops.fields", planet.FreeField) + "     " +
                           Trans.Format("vr.ops.queueSlots", BuildingCatalog.Occupied(planet), BuildingCatalog.MaxQueue(planet));
        }

        static string CostText(in BuildingQuote q)
        {
            var parts = new List<string>(4);
            if (q.Mineral > 0)
                parts.Add(Trans.Get("vr.res.mineral") + " " + Num(q.Mineral));
            if (q.Crystal > 0)
                parts.Add(Trans.Get("vr.res.crystal") + " " + Num(q.Crystal));
            if (q.Biomass > 0)
                parts.Add(Trans.Get("vr.res.biomass") + " " + Num(q.Biomass));
            parts.Add(Core.Holo.TravelPlanner.TimeText(q.Seconds));
            return string.Join("  ·  ", parts);
        }

        async Task Upgrade(string type)
        {
            if (_busy)
                return;
            _busy = true;
            var name = Trans.Get(type);
            try
            {
                SetStatus(Trans.Get("Loading"));
                var result = await ActionJs.Get("UpgradeBuilding", new Dictionary<string, string>
                {
                    { "buildingtype", type },
                    { "planet", _planetId.ToString() }
                });
                var (ok, message) = Interpret(result, IsQueued(result) ? "vr.ops.queued" : "upgradeRun");
                Feedback(ok, name + " · " + message);
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Ops, "UpgradeBuilding", result, name);
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
        }

        static bool IsQueued(ApiResult r)
        {
            if (!r.Ok || string.IsNullOrEmpty(r.Body) || r.Body[0] != '{')
                return false;
            try
            {
                return JObject.Parse(r.Body)["queued"]?.Value<bool>() == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Downgrade is instant and refunds nothing: first tap arms it, a second tap within 4 s confirms.</summary>
        void Downgrade(string type)
        {
            if (_confirmDowngrade != type || Time.unscaledTime >= _confirmUntil)
            {
                _confirmDowngrade = type;
                _confirmUntil = Time.unscaledTime + 4f;
                SetStatus(Trans.Format("vr.ops.confirmDowngrade", Trans.Get(type)), true);
                Render();
                return;
            }

            _confirmDowngrade = null;
            Run(DowngradeNow(type));
        }

        async Task DowngradeNow(string type)
        {
            _busy = true;
            try
            {
                var result = await ActionJs.Get("DowngradeBuilding", new Dictionary<string, string>
                {
                    { "buildingtype", type },
                    { "planet", _planetId.ToString() }
                });
                var (ok, message) = Interpret(result, "vr.ops.downgraded");
                Feedback(ok, Trans.Get(type) + " · " + message);
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
        }

        // ── Queue ─────────────────────────────────────────────────────────────────

        void RenderQueue(PlanetEconomy planet)
        {
            var active = BuildingCatalog.ActiveType(planet);
            Line(Trans.Format("vr.ops.queueSlots", BuildingCatalog.Occupied(planet), BuildingCatalog.MaxQueue(planet)),
                -330f, 75f, 20f, DiegeticUi.CyanDim, 360f, TextAlignmentOptions.MidlineLeft);

            if (string.IsNullOrEmpty(active))
            {
                Line(Trans.Get("vr.ops.queueIdle"), 0f, 0f, 22f, DiegeticUi.CyanDim);
                return;
            }

            var end = BuildingCatalog.ActiveEnd(planet);
            var level = planet.Level(active);
            var duration = ActiveDuration(active, level);
            var y = 10f;
            Line("<b>" + Trans.Get(active) + "</b>  <color=#ffb347>→ " + Trans.Get("level") + " " + level + "</color>",
                -250f, y + 16f, 22f, UiKit.TextBright, 520f, TextAlignmentOptions.MidlineLeft);
            var remain = Line(string.Empty, -250f, y - 20f, 18f, DiegeticUi.CyanDim, 520f, TextAlignmentOptions.MidlineLeft);
            _live.Add((remain, () => Core.Holo.TravelPlanner.TimeText(end - FleetOrderGate.UnixNow())));
            Bar(new Vector2(-250f, y - 44f), new Vector2(520f, 8f),
                () => duration <= 0f ? 0f : 1f - Mathf.Clamp01((end - FleetOrderGate.UnixNow()) / duration));

            var remaining = end - FleetOrderGate.UnixNow();
            var cost = BuildingCatalog.SpeedupCost(remaining);
            var canPay = cost == 0 || _eco.Nova >= cost;
            var finish = DiegeticUi.HoloButton(_body,
                cost == 0 ? Trans.Format("vr.ops.finish", Trans.Get("free"))
                    : Trans.Format("vr.ops.finish", cost + " " + Trans.Get("nova")),
                new Vector2(340f, y), new Vector2(300f, 54f), () => Run(Speedup()),
                canPay ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
            finish.interactable = canPay;

            var rows = BuildingCatalog.Queue(planet);
            if (rows == null)
                return;
            for (var i = 0; i < rows.Count && i < 4; i++)
            {
                var row = rows[i];
                var ry = -80f - i * 58f;
                var type = FocusContext.AsString(row["buildingtype"]);
                var target = FocusContext.AsInt(row["target_level"]);
                var seconds = FocusContext.AsFloat(row["duration"]);
                var id = FocusContext.AsInt(row["id"]);
                Line((i + 2) + ".  <b>" + Trans.Get(type) + "</b>  → " + Trans.Get("level") + " " + target +
                     "   <size=85%><color=#7fd8ff>" + Core.Holo.TravelPlanner.TimeText(seconds) + "</color></size>",
                    -200f, ry, 20f, UiKit.TextBright, 620f, TextAlignmentOptions.MidlineLeft);
                DiegeticUi.HoloButton(_body, Trans.Get("cancel"), new Vector2(390f, ry), new Vector2(200f, 48f),
                    () => Run(CancelQueued(id, type)), DiegeticUi.BtnStyle.Danger);
            }
        }

        /// <summary>Server duration of the build that reaches <paramref name="level"/> (for the progress bar).</summary>
        float ActiveDuration(string type, int level)
        {
            var baseTime = FocusContext.AsFloat(GameConfig.Upgrade?["time"]?[type]?["time"]);
            var computer = _eco.ResearchLevel("computer");
            return Mathf.Max(5f, baseTime * level * (100f - (computer + 1)) / 100f);
        }

        void Bar(Vector2 pos, Vector2 size, Func<float> value)
        {
            var back = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(_body, false);
            var brt = back.GetComponent<RectTransform>();
            brt.sizeDelta = size;
            brt.anchoredPosition = pos;
            var bimg = back.GetComponent<Image>();
            bimg.color = new Color(0.1f, 0.25f, 0.32f, 0.9f);
            bimg.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            var fimg = fill.GetComponent<Image>();
            fimg.color = UiKit.Amber;
            fimg.raycastTarget = false;
            _bars.Add((fimg, value));
        }

        async Task Speedup()
        {
            _busy = true;
            try
            {
                var result = await ActionJs.Get("SpeedupBuilding", new Dictionary<string, string>
                {
                    { "planet", _planetId.ToString() }
                });
                var (ok, message) = Interpret(result, "speedupSuccess");
                Feedback(ok, message);
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
        }

        async Task CancelQueued(int queueId, string type)
        {
            _busy = true;
            try
            {
                // Server reads `id` (the web's queue_id is a bug: docs/PARITY.md).
                var result = await ActionJs.Get("CancelQueuedBuilding", new Dictionary<string, string>
                {
                    { "id", queueId.ToString() }
                });
                var (ok, message) = Interpret(result, "vr.ops.cancelled");
                Feedback(ok, Trans.Get(type) + " · " + message);
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
        }

        // ── Decisions ─────────────────────────────────────────────────────────────

        void RenderDecisions(PlanetEconomy planet)
        {
            var now = FleetOrderGate.UnixNow();
            if (_decisions == null || _decisionsFor != planet.Id || (_decisionsNext > 0 && now >= _decisionsNext))
            {
                if (!_decisionsLoading)
                    Run(LoadDecisions(planet.Id));
                Line(Trans.Get("Loading"), 0f, 40f, 22f, DiegeticUi.CyanDim);
                return;
            }

            var next = _decisionsNext;
            var countdown = Line(string.Empty, -300f, 75f, 19f, DiegeticUi.CyanDim, 440f, TextAlignmentOptions.MidlineLeft);
            _live.Add((countdown, () => Trans.Format("vr.ops.nextBatch",
                Core.Holo.TravelPlanner.TimeText(next - FleetOrderGate.UnixNow()))));

            if (_decisions.Count == 0)
            {
                Line(Trans.Get("vr.ops.noDecision"), 0f, 0f, 22f, DiegeticUi.CyanDim);
                return;
            }

            Pager(_decisions.Count, DecisionsPerPage);
            var first = _page * DecisionsPerPage;
            for (var i = first; i < _decisions.Count && i < first + DecisionsPerPage; i++)
            {
                var d = _decisions[i];
                var y = 12f - (i - first) * 108f;
                var def = d["def"];
                var key = FocusContext.AsString(d["decision_key"]);
                var choice = FocusContext.AsString(d["choice"]);

                DiegeticUi.HoloSelectTray(_body, new Vector2(0f, y), new Vector2(1060f, 100f));
                // Title and text come localized from the server (model/decision.php DECISION_DEFS).
                Line("<b>" + FocusContext.AsString(def?["title"]) + "</b>", -170f, y + 30f, 20f, UiKit.TextBright, 700f,
                    TextAlignmentOptions.MidlineLeft);
                var desc = Line(FocusContext.AsString(def?["desc"]), -170f, y - 10f, 15f,
                    new Color(0.75f, 0.88f, 0.94f, 1f), 700f, TextAlignmentOptions.TopLeft);
                desc.rectTransform.sizeDelta = new Vector2(700f, 44f);
                desc.textWrappingMode = TextWrappingModes.Normal;
                desc.overflowMode = TextOverflowModes.Ellipsis;
                desc.fontSizeMin = 12f;
                desc.enableAutoSizing = true;
                desc.fontSizeMax = 15f;
                Line(Effect(def?["yes"]), -170f, y - 40f, 15f, UiKit.Amber, 700f, TextAlignmentOptions.MidlineLeft);

                if (!string.IsNullOrEmpty(choice))
                {
                    Line(Trans.Format("vr.ops.answered", Trans.Get(choice == "yes" ? "vr.ops.yes" : "vr.ops.no")),
                        390f, y, 19f, DiegeticUi.CyanDim, 260f);
                    continue;
                }

                var pid = planet.Id;
                DiegeticUi.HoloButton(_body, Trans.Get("vr.ops.yes"), new Vector2(330f, y + 22f), new Vector2(150f, 44f),
                    () => Run(Answer(pid, key, "yes")), DiegeticUi.BtnStyle.Cyan);
                var noLabel = Trans.Get("vr.ops.no");
                DiegeticUi.HoloButton(_body, noLabel, new Vector2(330f, y - 26f), new Vector2(150f, 44f),
                    () => Run(Answer(pid, key, "no")), DiegeticUi.BtnStyle.Ghost);
                if (def?["no"] != null)
                    Line(Effect(def["no"]), 470f, y - 26f, 13f, UiKit.Amber, 120f);
            }
        }

        /// <summary>"−10 % Biomass → Crystal ×0.6" from {take:{res,percent}, give:{res,ratio}}.</summary>
        static string Effect(JToken branch)
        {
            if (branch == null)
                return string.Empty;
            var take = branch["take"];
            var give = branch["give"];
            var text = "−" + Mathf.RoundToInt(FocusContext.AsFloat(take?["percent"]) * 100f) + " % " +
                       Trans.Get("vr.res." + FocusContext.AsString(take?["res"]));
            if (give != null)
                text += "  →  " + Trans.Get("vr.res." + FocusContext.AsString(give["res"])) + " ×" +
                        FocusContext.AsFloat(give["ratio"]).ToString("0.##", CultureInfo.InvariantCulture);
            return text;
        }

        async Task LoadDecisions(int planetId)
        {
            _decisionsLoading = true;
            try
            {
                var result = await ActionJs.Get("GetPlanetDecisions", new Dictionary<string, string>
                {
                    { "planet", planetId.ToString() }
                });
                _decisionsFor = planetId;
                _decisions = new JArray();
                _decisionsNext = FleetOrderGate.UnixNow() + 3600;
                if (result.Ok && !string.IsNullOrEmpty(result.Body))
                {
                    try
                    {
                        var root = JObject.Parse(result.Body);
                        _decisions = root["decisions"] as JArray ?? new JArray();
                        var next = FocusContext.AsLong(root["nextRefreshAt"]);
                        if (next > 0)
                            _decisionsNext = next;
                    }
                    catch
                    {
                        // Keep the empty batch; the next hour refetches.
                    }
                }
                else if (!result.Ok)
                {
                    SetStatus(result.Error, true);
                }
            }
            finally
            {
                _decisionsLoading = false;
            }

            if (_open && _tab == Tab.Decisions)
                Render();
        }

        async Task Answer(int planetId, string decisionKey, string choice)
        {
            if (_busy)
                return;
            _busy = true;
            try
            {
                // `decision` is the decision_key string (model/decision.php), not the numeric row id.
                var result = await ActionJs.Get("AnswerPlanetDecision", new Dictionary<string, string>
                {
                    { "planet", planetId.ToString() },
                    { "decision", decisionKey },
                    { "choice", choice }
                });
                var (ok, message) = Interpret(result, "decisionRecorded");
                Feedback(ok, message);
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Ops, "AnswerPlanetDecision", result,
                    string.Empty);
                if (ok)
                    _decisions = null;
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
            if (_open)
                Render();
        }

        // ── Report ────────────────────────────────────────────────────────────────

        void RenderReport(PlanetEconomy p)
        {
            var earn = p.Raw?["earn"];
            var rows = new List<(string, string)>
            {
                (Trans.Get("production") + " · " + Trans.Get("vr.res.mineral"), PerHour(earn?["mineral"])),
                (Trans.Get("production") + " · " + Trans.Get("vr.res.crystal"), PerHour(earn?["crystal"])),
                (Trans.Get("production") + " · " + Trans.Get("vr.res.biomass"), PerHour(earn?["biomass"])),
                (Trans.Get("energy"), Tone(Num(p.EnergyUsed) + " / " + Num(p.Energy), p.EnergyUsed > p.Energy)),
                (Trans.Get("population"), Num(p.Citizen) + "   " + Trans.Format("vr.ops.jobs", p.Employed, p.Jobs)),
                (Trans.Get("mood"), p.Mood + " %"),
                (Trans.Get("vr.ops.habitability"), Num(p.Habitability)),
                (Trans.Get("defense"), Num(p.Defense)),
                (Trans.Get("vr.ops.freeFields"), p.FreeField + " / " + FocusContext.AsInt(p.Raw?["fieldGiven"])),
                (Trans.Get("vr.ops.explorePool"), Num(FocusContext.AsFloat(p.Raw?["researchPoints"])))
            };

            for (var i = 0; i < rows.Count; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var x = col == 0 ? -270f : 270f;
                var y = 72f - row * 62f;
                DiegeticUi.HoloSelectTray(_body, new Vector2(x, y), new Vector2(520f, 54f));
                Line(rows[i].Item1, x - 10f, y, 18f, DiegeticUi.CyanDim, 480f, TextAlignmentOptions.MidlineLeft);
                Line(rows[i].Item2, x + 10f, y, 20f, UiKit.TextBright, 480f, TextAlignmentOptions.MidlineRight);
            }
        }

        /// <summary>GetResource raw earn is per second; the web shows it per hour.</summary>
        static string PerHour(JToken perSecond) =>
            "+" + Num(FocusContext.AsFloat(perSecond) * 3600f) + " " + Trans.Get("perHour");

        // ── Rename ────────────────────────────────────────────────────────────────

        void BeginRename()
        {
            if (!_eco.TryGet(_planetId, out var planet))
                return;
            _nameField.text = planet.Name;
            _planetGroup.SetActive(false);
            _renameGroup.SetActive(true);
            _nameField.Select();
            _nameField.ActivateInputField();
        }

        void EndRename()
        {
            if (_renameGroup == null)
                return;
            _renameGroup.SetActive(false);
            _planetGroup.SetActive(true);
        }

        async Task Rename()
        {
            var name = (_nameField.text ?? string.Empty).Trim();
            if (name.Length == 0)
                return;
            if (name.Length > NameLimit)
                name = name.Substring(0, NameLimit);
            var result = await ActionJs.Get("RenamePlanet", new Dictionary<string, string>
            {
                { "id", _planetId.ToString() },
                { "name", name }
            });
            var (ok, message) = Interpret(result, "vr.ops.renamed");
            Feedback(ok, message);
            if (ok)
            {
                EndRename();
                await _eco.RefreshNow();
            }
        }

        // ── Results ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Transport error → server's localized text. Some handlers answer JSON {ok:false, error:"key"}
        /// without the error: prefix (CancelQueuedBuilding) → that key through Trans.
        /// </summary>
        static (bool ok, string message) Interpret(ApiResult r, string okKey)
        {
            if (!r.Ok)
                return (false, string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error);
            if (!string.IsNullOrEmpty(r.Body) && r.Body[0] == '{')
            {
                try
                {
                    var o = JObject.Parse(r.Body);
                    if (o["ok"] != null && o["ok"].Type == JTokenType.Boolean && !o["ok"].Value<bool>())
                        return (false, Trans.Get(FocusContext.AsString(o["error"])));
                }
                catch
                {
                    // Plain success body.
                }
            }

            return (true, Trans.Get(okKey));
        }

        void Feedback(bool ok, string message)
        {
            if (ok)
                CicCue.Ok(transform.position);
            else
                CicCue.Fail(transform.position);
            SetStatus(message, !ok);
        }

        void Update()
        {
            if (!_open || Time.unscaledTime < _nextLive)
                return;
            _nextLive = Time.unscaledTime + 0.5f;
            foreach (var (text, value) in _live)
            {
                if (text != null)
                    text.text = value();
            }

            foreach (var (fill, value) in _bars)
            {
                if (fill == null)
                    continue;
                fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value()), 1f);
            }

            // A disarmed downgrade goes back to "−".
            if (_confirmDowngrade != null && Time.unscaledTime >= _confirmUntil)
            {
                _confirmDowngrade = null;
                SetStatus(string.Empty);
                if (_tab == Tab.Buildings)
                    Render();
            }
        }
    }
}
