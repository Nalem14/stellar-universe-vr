using System.Collections.Generic;
using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// What our ship in orbit is doing, spelled out above the holo table while one of ours is busy surveying a
    /// world (ExplorePlanet, by hand or by auto-exploration) or mining an asteroid field (HarvestAsteroid) — the
    /// inhabited ship, or one of ours in the system on the table (elsewhere it is on the wrist readout): "Survey
    /// in progress" / "Mining in progress", which ship on which world or field, the time left with a progress
    /// bar, and what it brings in (research points, or the haul of mineral and crystal — the server credits
    /// both at the start; the VR knows the amounts when the order came from here). When it ends the banner says
    /// so for a few seconds, then folds away. Refreshed twice a second while shown.
    /// </summary>
    public sealed class SurveyBanner : MonoBehaviour
    {
        static readonly Vector2 Px = new(820f, 170f);
        const float DoneHold = 6f;

        /// <summary>Surveys ordered from this headset: fleet → (research points, start, end).</summary>
        static readonly Dictionary<int, (int points, long start, long end)> Ordered = new();

        /// <summary>Harvests ordered from this headset: fleet → (mineral, crystal, start, end).</summary>
        static readonly Dictionary<int, (int mineral, int crystal, long start, long end)> Hauls = new();

        FocusContext _focus;
        CanvasGroup _group;
        TMP_Text _title;
        TMP_Text _what;
        TMP_Text _left;
        TMP_Text _points;
        RectTransform _fill;
        Image _fillImage;
        readonly Dictionary<int, long> _firstSeen = new();
        int _fleet;
        bool _mining;
        string _lastWhat = string.Empty;
        float _doneUntil;
        float _alpha;
        float _next;

        /// <summary>An ExplorePlanet answered: remember what it brings for the banner.</summary>
        public static void Record(int fleetId, string body)
        {
            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(body);
                var end = FocusContext.AsLong(o["exploreEndTime"]);
                var duration = FocusContext.AsLong(o["duration"]);
                Ordered[fleetId] = (FocusContext.AsInt(o["researchPoints"]), end - duration, end);
            }
            catch
            {
                // Not JSON (older server): the banner still shows the timer.
            }
        }

        /// <summary>A HarvestAsteroid answered ({mineral, crystal, duration}): remember the haul for the banner.</summary>
        public static void RecordHarvest(int fleetId, string body)
        {
            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(body);
                var now = FleetOrderGate.UnixNow();
                Hauls[fleetId] = (FocusContext.AsInt(o["mineral"]), FocusContext.AsInt(o["crystal"]), now,
                    now + FocusContext.AsLong(o["duration"]));
            }
            catch
            {
                // Not JSON: the banner still shows the timer.
            }
        }

        /// <summary>The haul of a harvest ending at <paramref name="end"/>, when it was ordered here (else start 0).</summary>
        public static (int mineral, int crystal, long start) Harvest(int fleetId, long end)
        {
            // Local clock vs the server's end stamp: a few seconds apart at most.
            if (Hauls.TryGetValue(fleetId, out var h) && System.Math.Abs(h.end - end) <= 5)
                return (h.mineral, h.crystal, h.start);
            return (0, 0, 0);
        }

        public static SurveyBanner Build(Transform room, FocusContext focus, Vector3 local, Vector3 faceLocal, int layer)
        {
            var go = new GameObject("SurveyBanner");
            go.transform.SetParent(room, false);
            go.transform.localPosition = local;
            var face = room.TransformPoint(faceLocal) - go.transform.position;
            go.transform.rotation = Quaternion.LookRotation(-face, Vector3.up);
            var b = go.AddComponent<SurveyBanner>();
            b._focus = focus;

            var canvas = DiegeticUi.WorldCanvas(go.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, 0.0015f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            var img = frame.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0.5f, 0.95f, 1f, 0.78f);
            b._group = canvas.gameObject.AddComponent<CanvasGroup>();
            b._group.alpha = 0f;
            b._group.interactable = false;
            b._group.blocksRaycasts = false;

            b._title = Label(frame, new Vector2(-150f, 48f), new Vector2(480f, 58f), 36f, UiKit.Cyan, TextAlignmentOptions.MidlineLeft);
            b._title.fontStyle = FontStyles.Bold;
            b._left = Label(frame, new Vector2(270f, 48f), new Vector2(220f, 60f), 40f, UiKit.Amber, TextAlignmentOptions.MidlineRight);
            b._left.fontStyle = FontStyles.Bold;
            b._what = Label(frame, new Vector2(-110f, 4f), new Vector2(560f, 48f), 29f, UiKit.TextBright, TextAlignmentOptions.MidlineLeft);
            b._points = Label(frame, new Vector2(250f, 4f), new Vector2(260f, 48f), 27f, new Color(0.85f, 0.78f, 1f), TextAlignmentOptions.MidlineRight);

            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            rail.transform.SetParent(frame, false);
            rail.rectTransform.sizeDelta = new Vector2(760f, 10f);
            rail.rectTransform.anchoredPosition = new Vector2(0f, -44f);
            rail.color = new Color(0.3f, 0.6f, 0.75f, 0.35f);
            rail.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(rail.transform, false);
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.sizeDelta = Vector2.zero;
            fill.color = UiKit.Cyan;
            fill.raycastTarget = false;
            b._fill = fill.rectTransform;
            b._fillImage = fill;

            if (layer >= 0)
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = layer;
            return b;
        }

        static TMP_Text Label(Transform parent, Vector2 pos, Vector2 size, float font, Color c, TextAlignmentOptions align)
        {
            var t = DiegeticUi.HoloLabel(parent, string.Empty, pos, size, font, c, align);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = false;
            return t;
        }

        void Update()
        {
            _alpha = Mathf.MoveTowards(_alpha, Showing() ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            if (Mathf.Abs(_group.alpha - _alpha) > 0.001f)
                _group.alpha = _alpha;
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 0.5f;
            Refresh();
        }

        bool Showing() => _fleet > 0 || Time.unscaledTime < _doneUntil;

        void Refresh()
        {
            var now = FleetOrderGate.UnixNow();
            var me = FocusContext.OwnedUserId();
            FocusFleet best = null;
            var bestMining = false;
            if (_focus != null && me > 0)
                foreach (var f in _focus.Fleets)
                {
                    if (f == null || f.UserId != me)
                        continue;
                    var exploring = f.IsExploring(now);
                    var mining = !exploring && f.IsHarvesting(now);
                    if (!exploring && !mining)
                        continue;
                    // What the bridge is looking at: the inhabited ship, or ours in the system on the table.
                    // Elsewhere it belongs on the wrist (every affair with a timer), not on this bridge.
                    if (f.Id != _focus.ViewFleetId && f.SystemId != _focus.SystemId)
                        continue;
                    // The inhabited ship first, then the one finishing soonest.
                    if (best == null || f.Id == _focus.ViewFleetId ||
                        (best.Id != _focus.ViewFleetId && End(f, mining) < End(best, bestMining)))
                    {
                        best = f;
                        bestMining = mining;
                    }
                }

            if (best == null)
            {
                if (_fleet > 0)
                {
                    // Just finished: say so, keep the last line and what it brought a moment.
                    _fleet = 0;
                    _doneUntil = Time.unscaledTime + DoneHold;
                    _title.text = Trans.Get(_mining ? "vr.mining.done" : "vr.survey.done");
                    _left.text = string.Empty;
                    _fill.sizeDelta = new Vector2(760f, 0f);
                    CicCue.Success(transform.position);
                }

                return;
            }

            if (_fleet != best.Id || _mining != bestMining)
            {
                _fleet = best.Id;
                _mining = bestMining;
                _fillImage.color = _mining ? UiKit.Amber : UiKit.Cyan;
                CicCue.Ok(transform.position);
            }

            var end = End(best, _mining);
            if (!_firstSeen.TryGetValue(best.Id, out var seen) || seen > now || seen >= end)
                _firstSeen[best.Id] = seen = now;
            var start = seen;
            var ship = string.IsNullOrEmpty(best.Name) ? "#" + best.Id : best.Name;
            string where;
            if (_mining)
            {
                var haul = Harvest(best.Id, end);
                if (haul.start > 0)
                    start = haul.start;
                var rock = _focus.FindAsteroid(best.AsteroidId);
                where = Trans.Get("asteroidField") + " #" + (rock != null && rock.Slot > 0 ? rock.Slot : best.AsteroidId);
                _points.text = haul.mineral + haul.crystal > 0
                    ? Trans.Format("vr.mining.haul", haul.mineral.ToString("N0"), haul.crystal.ToString("N0"))
                    : CrewStationScreen.HoldLine(best);
                _title.text = Trans.Get("vr.mining.active");
            }
            else
            {
                var points = 0;
                if (Ordered.TryGetValue(best.Id, out var o) && o.end == end)
                {
                    start = o.start;
                    points = o.points;
                }

                var planet = _focus.FindPlanet(best.PlanetId);
                where = planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : "#" + best.PlanetId;
                _points.text = points > 0 ? Trans.Format("vr.survey.gained", points.ToString("N0")) : string.Empty;
                _title.text = Trans.Get("vr.survey.active");
            }

            _lastWhat = ship + "  →  " + where;
            _what.text = _lastWhat;
            _left.text = Core.Holo.TravelPlanner.TimeText(end - now);
            var span = end - start;
            var p = span > 0 ? Mathf.Clamp01((now - start) / (float)span) : 0f;
            _fill.sizeDelta = new Vector2(760f * p, 0f);
        }

        static long End(FocusFleet f, bool mining) => mining ? f.HarvestEndTime : f.ExploreEndTime;
    }
}
