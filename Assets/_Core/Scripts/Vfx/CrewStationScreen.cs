using System.Collections.Generic;
using System.Globalization;
using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// What each crew station shows its operator (and the captain over the shoulder), live from the game:
    /// Helm the course (destination, arrival, a progress bar — or the orbit and the jump drive), Tactical the
    /// contacts in the system (hostiles, pirates, our ships, sieges), Engineering the ship (modules, cargo,
    /// troops) or at a station the shipyard, Science the anomalies here and the research under way, Comms the
    /// traffic (unread, wars, the last correspondent, the alliance), Ops the world's stocks, energy and
    /// construction. In an alert a band across the top turns amber or red (battle stations) and Tactical's
    /// contacts go red. Refreshed on focus events and every 2 s; nothing per frame.
    /// </summary>
    public sealed class CrewStationScreen : MonoBehaviour
    {
        FocusContext _focus;
        CrewDialogue.Role _role;
        HoloScreen _screen;
        Color _accent;
        TMP_Text _primary;
        readonly TMP_Text[] _lines = new TMP_Text[3];
        RectTransform _barFill;
        GameObject _bar;
        Image _alertBand;
        TMP_Text _alertText;
        readonly Dictionary<int, long> _underWaySince = new();

        static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        static readonly Color Red = new(1f, 0.3f, 0.26f);

        public void Bind(HoloScreen screen, FocusContext focus, Color accent, CrewDialogue.Role role)
        {
            _screen = screen;
            _focus = focus;
            _accent = accent;
            _role = role;
            var px = screen.PixelSize;
            var c = screen.Content;

            _alertBand = new GameObject("AlertBand", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _alertBand.transform.SetParent(c, false);
            var art = _alertBand.rectTransform;
            art.sizeDelta = new Vector2(px.x * 0.92f, 34f);
            art.anchoredPosition = new Vector2(0f, px.y * 0.5f - 62f);
            _alertBand.raycastTarget = false;
            _alertText = DiegeticUi.HoloLabel(_alertBand.transform, string.Empty, Vector2.zero, art.sizeDelta, 22f, Color.white);
            _alertText.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            _alertText.characterSpacing = 6f;
            _alertBand.gameObject.SetActive(false);

            // Read from the captain's place too (~3 m): few lines, large type.
            _primary = DiegeticUi.HoloLabel(c, string.Empty, new Vector2(0f, px.y * 0.17f), new Vector2(px.x * 0.92f, 58f), 48f,
                UiKit.TextBright);
            _primary.enableAutoSizing = true;
            _primary.fontSizeMin = 28f;
            _primary.fontSizeMax = 48f;
            _primary.richText = false;
            for (var i = 0; i < _lines.Length; i++)
            {
                _lines[i] = DiegeticUi.HoloLabel(c, string.Empty, new Vector2(0f, px.y * 0.02f - i * 40f), new Vector2(px.x * 0.9f, 38f),
                    30f, i == 0 ? accent : UiKit.TextBright, TextAlignmentOptions.Center);
                _lines[i].enableAutoSizing = true;
                _lines[i].fontSizeMin = 18f;
                _lines[i].fontSizeMax = i == 0 ? 32f : 27f;
                _lines[i].richText = false;
            }

            _bar = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            _bar.transform.SetParent(c, false);
            var brt = _bar.GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(px.x * 0.74f, 12f);
            brt.anchoredPosition = new Vector2(0f, -px.y * 0.39f);
            var bimg = _bar.GetComponent<Image>();
            bimg.color = new Color(0.2f, 0.4f, 0.5f, 0.6f);
            bimg.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(_bar.transform, false);
            _barFill = fill.GetComponent<RectTransform>();
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0.3f, 1f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;
            var fimg = fill.GetComponent<Image>();
            fimg.color = accent;
            fimg.raycastTarget = false;
            _bar.SetActive(false);

            if (_focus != null)
            {
                _focus.Changed += Refresh;
                _focus.FleetsChanged += Refresh;
            }

            AlertState.Changed += OnAlert;
            InvokeRepeating(nameof(Refresh), 0.5f, 2f);
        }

        void OnDestroy()
        {
            AlertState.Changed -= OnAlert;
            if (_focus == null)
                return;
            _focus.Changed -= Refresh;
            _focus.FleetsChanged -= Refresh;
        }

        void OnAlert(AlertLevel from, AlertLevel to) => Refresh();

        void Refresh()
        {
            if (_primary == null || _focus == null)
                return;
            var now = FleetOrderGate.UnixNow();
            for (var i = 0; i < _lines.Length; i++)
                _lines[i].text = string.Empty;
            _primary.color = UiKit.TextBright;
            _bar.SetActive(false);

            switch (_role)
            {
                case CrewDialogue.Role.Helm:
                    Helm(now);
                    break;
                case CrewDialogue.Role.Tactical:
                    Tactical(now);
                    break;
                case CrewDialogue.Role.Engineering:
                    Engineering(now);
                    break;
                case CrewDialogue.Role.Science:
                    Science(now);
                    break;
                case CrewDialogue.Role.Comms:
                    Comms();
                    break;
                default:
                    Ops(now);
                    break;
            }

            Alert();
        }

        // ── Roles ───────────────────────────────────────────────────────────────

        void Helm(long now)
        {
            var fleet = _focus.FindViewFleet();
            if (fleet == null)
            {
                _primary.text = BridgeViewscreen.SystemLabel(_focus);
                _lines[0].text = Trans.Get("vr.view.stationHeader");
                return;
            }

            _primary.text = Name(fleet);
            if (fleet.DestTime > now)
            {
                if (!_underWaySince.TryGetValue(fleet.Id, out var since) || since > now)
                    _underWaySince[fleet.Id] = since = now;
                _lines[0].text = Trans.Format("vr.screen.destination", GalaxyCatalog.Label(fleet.DestSystemId > 0 ? fleet.DestSystemId : fleet.SystemId));
                _lines[1].text = Trans.Format("vr.travel.eta", Core.Holo.TravelPlanner.TimeText(fleet.DestTime - now));
                Bar(since, fleet.DestTime, now);
                return;
            }

            _underWaySince.Remove(fleet.Id);
            var orbit = _focus.FindPlanet(fleet.PlanetId);
            _lines[0].text = orbit != null
                ? Trans.Format("vr.screen.orbit", string.IsNullOrEmpty(orbit.Name) ? "#" + orbit.Id : orbit.Name)
                : Trans.Get(FleetOrderGate.IsIdle(fleet) ? "vr.screen.idle" : FleetOrderGate.BusyKey(fleet));
            _lines[1].text = Trans.Get("hyperdrive") + " · " + Trans.Get(fleet.HasHyperdrive && fleet.EnoughHyperdrive ? "vr.console.ready" : "vr.console.unavailable");
            if (fleet.Speed > 0f)
                _lines[2].text = Trans.Get("speed") + " " + fleet.Speed.ToString("0.#", Fr);
        }

        void Tactical(long now)
        {
            var me = FocusContext.OwnedUserId();
            int hostiles = 0, pirates = 0, ours = 0, sieges = 0;
            foreach (var f in _focus.Fleets)
            {
                if (f == null || f.SystemId != _focus.SystemId || f.DestTime > now)
                    continue;
                if (f.UserId == me)
                {
                    ours++;
                    if (f.IsSieging(now))
                        sieges++;
                    continue;
                }

                if (f.IsPirate)
                    pirates++;
                else if (DiplomacyIndex.ResolveFleet(f) is EmpireStance.Enemy or EmpireStance.Pirate)
                    hostiles++;
            }

            var threat = hostiles + pirates;
            _primary.text = threat > 0 ? Trans.Format("vr.screen.hostiles", threat) : Trans.Get("vr.console.clear");
            _primary.color = threat > 0 ? Red : UiKit.TextBright;
            _lines[0].text = Trans.Format("vr.screen.oursHere", ours);
            if (pirates > 0)
                _lines[1].text = Trans.Get("vr.screen.pirate") + " · " + pirates;
            if (sieges > 0)
                _lines[2].text = Trans.Get(FleetOrderGate.BusyKey(FirstSieging(me, now)));
        }

        FocusFleet FirstSieging(int me, long now)
        {
            foreach (var f in _focus.Fleets)
                if (f != null && f.UserId == me && f.IsSieging(now))
                    return f;
            return null;
        }

        void Engineering(long now)
        {
            var fleet = _focus.FindViewFleet();
            if (fleet != null)
            {
                // Mining: the extraction under way first (the hold fills as it runs), then the hold itself.
                if (fleet.IsHarvesting(now))
                {
                    _primary.text = Trans.Get("vr.mining.active");
                    _primary.color = UiKit.Amber;
                    var rock = _focus.FindAsteroid(fleet.AsteroidId);
                    _lines[0].text = Trans.Get("asteroidField") + " #" + (rock != null && rock.Slot > 0 ? rock.Slot : fleet.AsteroidId) +
                                     " · " + Trans.Format("vr.screen.remaining", Core.Holo.TravelPlanner.TimeText(fleet.HarvestEndTime - now));
                    var haul = SurveyBanner.Harvest(fleet.Id, fleet.HarvestEndTime);
                    if (haul.start > 0)
                        Bar(haul.start, fleet.HarvestEndTime, now);
                }
                else
                {
                    _primary.text = Trans.Get("modules") + " · " + fleet.Modules.Count;
                    _lines[0].text = HoldLine(fleet);
                }

                _lines[1].text = HoldContents(fleet);
                _lines[2].text = fleet.TroopsAboard > 0 ? Trans.Format("vr.armory.aboard", fleet.TroopsAboard) : Name(fleet);
                return;
            }

            // At a station: the world's shipyard, and what it is building.
            var p = Planet();
            _primary.text = Trans.Get("shipyard");
            if (p == null)
                return;
            _lines[0].text = string.IsNullOrEmpty(p.Name) ? "#" + p.Id : p.Name;
            Working(p, now, 1);
        }

        /// <summary>"Hold 1 200 / 3 000" — what is aboard over the capacity.</summary>
        internal static string HoldLine(FocusFleet f) =>
            Trans.Get("cargo") + " · " + f.CargoUsed.ToString("N0", Fr) + " / " + f.Cargo.ToString("N0", Fr);

        /// <summary>"Mineral 800 · Crystal 400 · Biomass 0".</summary>
        internal static string HoldContents(FocusFleet f) =>
            Trans.Get("vr.res.mineral") + " " + f.MineralCargo.ToString("N0", Fr) + "  ·  " +
            Trans.Get("vr.res.crystal") + " " + f.CrystalCargo.ToString("N0", Fr) + "  ·  " +
            Trans.Get("vr.res.biomass") + " " + f.BiomassCargo.ToString("N0", Fr);

        void Science(long now)
        {
            var here = AnomalyService.Instance != null ? AnomalyService.Instance.For(_focus.SystemId) : null;
            var n = here?.Count ?? 0;
            _primary.text = Trans.Get("anomalies") + " · " + n;
            var eco = EconomyService.Instance;
            var end = FocusContext.AsLong(eco?.Empire?["working"]);
            if (end > now)
            {
                _lines[0].text = Trans.Get(FocusContext.AsString(eco.Empire["workingtype"]));
                _lines[1].text = Trans.Format("vr.screen.remaining", Core.Holo.TravelPlanner.TimeText(end - now));
                Bar(FocusContext.AsLong(eco.Empire["workingStart"]), end, now);
            }
            else
            {
                _lines[0].text = Trans.Get("vr.watch.research") + " · " + Trans.Get("vr.screen.idle");
            }

            var fleet = _focus.FindViewFleet();
            if (fleet != null && fleet.ExploreEndTime > now)
                _lines[2].text = Trans.Get(FleetOrderGate.BusyKey(fleet));
        }

        void Comms()
        {
            var comms = CommsService.Instance;
            var total = comms?.Total ?? 0;
            _primary.text = total > 0 ? Trans.Format("vr.screen.comms", total) : Trans.Get("vr.console.noTraffic");
            var dip = DiplomacyService.Instance;
            if (dip != null)
            {
                _lines[0].text = Trans.Get("wars") + " · " + dip.Wars.Count;
                var name = FocusContext.AsString(dip.Alliance?["name"]);
                if (!string.IsNullOrEmpty(name))
                    _lines[2].text = Trans.Get("alliance") + " · " + name;
            }

            var last = comms?.Last;
            if (last != null && !string.IsNullOrEmpty(last.Username))
                _lines[1].text = Trans.Get("vr.screen.transmission") + " · " + last.Username;
        }

        void Ops(long now)
        {
            var p = Planet();
            if (p == null)
            {
                _primary.text = BridgeViewscreen.SystemLabel(_focus);
                return;
            }

            _primary.text = string.IsNullOrEmpty(p.Name) ? "#" + p.Id : p.Name;
            _lines[0].text = Trans.Get("vr.res.mineral") + " " + Short(p.Mineral) + "   " + Trans.Get("vr.res.crystal") + " " + Short(p.Crystal) +
                             "   " + Trans.Get("vr.res.biomass") + " " + Short(p.Biomass);
            _lines[1].text = Trans.Get("energy") + " " + Short(p.EnergyUsed) + " / " + Short(p.Energy);
            _lines[1].color = p.EnergyUsed > p.Energy ? Red : UiKit.TextDim;
            Working(p, now, 2);
        }

        /// <summary>The station's world, the world our ship orbits, else our first world.</summary>
        PlanetEconomy Planet()
        {
            var eco = EconomyService.Instance;
            if (eco == null)
                return null;
            var id = _focus.ViewPlanetId;
            if (id <= 0)
                id = _focus.FindViewFleet()?.PlanetId ?? 0;
            if (id > 0 && eco.Planets.TryGetValue(id, out var p))
                return p;
            foreach (var any in eco.Planets.Values)
                return any;
            return null;
        }

        void Working(PlanetEconomy p, long now, int line)
        {
            var end = FocusContext.AsLong(p.Raw?["working"]);
            if (end <= now)
                return;
            _lines[line].text = Trans.Get(FocusContext.AsString(p.Raw["workingtype"])) + " · " +
                                Trans.Format("vr.screen.remaining", Core.Holo.TravelPlanner.TimeText(end - now));
            Bar(FocusContext.AsLong(p.Raw["workingStart"]), end, now);
        }

        void Bar(long start, long end, long now)
        {
            if (start <= 0 || end <= start)
                return;
            _bar.SetActive(true);
            _barFill.anchorMax = new Vector2(Mathf.Clamp(Mathf.InverseLerp(start, end, now), 0.02f, 1f), 1f);
        }

        void Alert()
        {
            var level = AlertState.Level;
            var on = level != AlertLevel.Normal;
            _alertBand.gameObject.SetActive(on);
            if (on)
            {
                var red = level == AlertLevel.Red;
                _alertBand.color = red ? new Color(0.6f, 0.06f, 0.05f, 0.85f) : new Color(0.55f, 0.32f, 0.04f, 0.85f);
                _alertText.text = red
                    ? Trans.Get("vr.screen.redAlert") + " · " + Trans.Get("vr.console.battleStations")
                    : Trans.Get("vr.console.amberAlert");
            }

            _screen.SetAccent(level == AlertLevel.Red ? Red : level == AlertLevel.Amber ? UiKit.Amber : _accent,
                on ? 0.9f : 0.45f);
        }

        static string Name(FocusFleet f) => string.IsNullOrEmpty(f.Name) ? "#" + f.Id : f.Name;

        static string Short(float v)
        {
            if (v >= 1e6f)
                return (v / 1e6f).ToString("0.#", Fr) + " M";
            if (v >= 1e4f)
                return (v / 1e3f).ToString("0", Fr) + " k";
            return Mathf.RoundToInt(v).ToString("N0", Fr);
        }
    }
}
