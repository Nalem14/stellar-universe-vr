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
    /// Science planetary survey (web planet info panel, GetPlanet): the Science officer brings it up in front of
    /// the captain for the planets of the system in view — owner stance, habitability, fields, defense, garrison,
    /// ships in orbit, key buildings, and the research points still to collect by exploring (ExplorePlanet).
    /// One GetPlanet per planet shown (server cache 5 s); nothing polled while closed.
    /// </summary>
    public sealed class PlanetSurvey : MonoBehaviour
    {
        static readonly Vector2 Size = new(1.05f, 0.72f);
        static readonly Color Accent = new(0.7f, 0.5f, 1f, 1f);
        const float Reach = 1.25f;
        const float MaxBearing = 20f;

        static readonly string[] KeyBuildings =
        {
            "home", "mineralMine", "crystalMine", "solarPlant", "researchLab", "orbitShipyard", "academy",
            "defenseFactory", "stargate", "jumpgate"
        };

        public static PlanetSurvey Instance { get; private set; }

        HoloScreen _screen;
        RectTransform _body;
        TMP_Text _title;
        Transform _anchor;
        readonly List<FocusPlanet> _planets = new();
        int _index;
        bool _busy;
        JObject _data;

        public static PlanetSurvey Build(Transform room)
        {
            var rig = new GameObject("PlanetSurveyRig").transform;
            rig.SetParent(room, false);
            var survey = rig.gameObject.AddComponent<PlanetSurvey>();
            survey.BuildScreen(rig);
            rig.gameObject.SetActive(false);
            return survey;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void BuildScreen(Transform rig)
        {
            _screen = HoloScreen.Create(rig, "PlanetSurvey", Size, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.survey.title"));
            _screen.SetAccent(Accent, 0.5f);
            var frame = _screen.Content;
            DiegeticUi.HoloButton(frame, "‹", new Vector2(-465f, 245f), new Vector2(64f, 46f), () => Step(-1),
                DiegeticUi.BtnStyle.Ghost);
            _title = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(-150f, 245f), new Vector2(560f, 46f), 26f,
                UiKit.TextBright);
            _title.fontStyle = FontStyles.Bold;
            _title.enableAutoSizing = true;
            _title.fontSizeMin = 18f;
            _title.fontSizeMax = 26f;
            _title.richText = true;
            DiegeticUi.HoloButton(frame, "›", new Vector2(165f, 245f), new Vector2(64f, 46f), () => Step(1),
                DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(frame, Trans.Get("close"), new Vector2(425f, 245f), new Vector2(140f, 46f), Close,
                DiegeticUi.BtnStyle.Ghost);
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(frame, false);
            _body = bodyGo.GetComponent<RectTransform>();
            _body.sizeDelta = new Vector2(1000f, 480f);
            _body.anchoredPosition = new Vector2(0f, -30f);
        }

        /// <summary>Open on <paramref name="planetId"/> (or the first planet of the system).</summary>
        public void Open(Transform officerAnchor, FocusContext focus, int planetId)
        {
            _anchor = officerAnchor;
            _planets.Clear();
            if (focus != null)
                _planets.AddRange(focus.Planets);
            _planets.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            _index = Mathf.Max(0, _planets.FindIndex(p => p.Id == planetId));
            gameObject.SetActive(true);
            Place();
            CicCue.Ok(transform.position);
            AsyncTap.Run(Load());
        }

        public void Close()
        {
            if (_anchor != null)
                _anchor.GetComponentInParent<CrewOfficer>()?.LookAt(null);
            gameObject.SetActive(false);
        }

        void Step(int d)
        {
            if (_planets.Count == 0 || _busy)
                return;
            _index = (_index + d + _planets.Count) % _planets.Count;
            AsyncTap.Run(Load());
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
            p.y = eye.y - 0.15f;
            transform.position = p;
            ScreenMount.FaceViewer(transform, eye, 1f, 6f);
        }

        async Task Load()
        {
            if (_planets.Count == 0)
            {
                _data = null;
                _title.text = Trans.Get("vr.survey.none");
                Render();
                return;
            }

            var planet = _planets[_index];
            _title.text = Label(planet) + "  <size=65%><color=#b9a4ff>" + (_index + 1) + " / " + _planets.Count +
                          "</color></size>";
            _busy = true;
            _data = null;
            Render();
            try
            {
                var r = await ActionJs.Get("GetPlanet", new Dictionary<string, string> { { "id", planet.Id.ToString() } });
                if (r.Ok)
                {
                    try
                    {
                        _data = JObject.Parse(r.Body);
                        // Owner account row is never kept on the client.
                        _data.Remove("user");
                    }
                    catch
                    {
                        _data = null;
                    }
                }
                else
                {
                    Status(string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error);
                    return;
                }
            }
            finally
            {
                _busy = false;
            }

            Render();
        }

        static string Label(FocusPlanet p) =>
            string.IsNullOrEmpty(p.Name) ? Trans.Get("planet") + " " + p.Slot : p.Name;

        void Clear()
        {
            for (var i = _body.childCount - 1; i >= 0; i--)
                DestroyImmediate(_body.GetChild(i).gameObject);
        }

        void Status(string text)
        {
            Clear();
            Text(text, 0f, 40f, 900f, 22f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
        }

        void Render()
        {
            Clear();
            if (_data == null)
            {
                Text(Trans.Get(_busy ? "Loading" : "vr.survey.none"), 0f, 40f, 900f, 22f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
                return;
            }

            var owner = FocusContext.AsInt(_data["userid"]);
            var stance = DiplomacyIndex.Resolve(owner);
            var tint = DiplomacyIndex.Tint(stance);
            var ownerText = owner <= 0
                ? Trans.Get("vr.survey.unclaimed")
                : FocusContext.AsString(_data["empire"]?["name"]) is { Length: > 0 } empire
                    ? empire
                    : Trans.Get("vr.survey.claimed");
            const float l = -470f;
            const float r = 30f;
            var y = 190f;
            Row(Trans.Get("vr.survey.owner"), "<color=#" + ColorUtility.ToHtmlStringRGB(tint) + ">" + ownerText + "</color>",
                l, y);
            var hab = FocusContext.AsFloat(_data["_habitability"] ?? _data["habitability"]);
            Row(Trans.Get("habitability"), Tone(hab.ToString("0.#"), hab >= 6f), r, y);
            y -= 44f;
            var given = FocusContext.AsInt(_data["fieldGiven"]);
            var free = FocusContext.AsInt(_data["freeField"]);
            Row(Trans.Get("vr.survey.fields"), free + " / " + given, l, y);
            Row(Trans.Get("defense"), FocusContext.AsInt(_data["defense"]).ToString("N0"), r, y);
            y -= 44f;
            Row(Trans.Get("vr.survey.garrison"), Count(_data["troops"], "qty").ToString("N0"), l, y);
            Row(Trans.Get("vr.survey.orbit"), (_data["orbit"] as JArray)?.Count.ToString() ?? "0", r, y);
            y -= 44f;

            // Science's own read: points still on the surface for an ExplorePlanet survey.
            var points = FocusContext.AsInt(_data["researchPoints"]);
            Row(Trans.Get("vr.survey.explorePoints"), Tone(points.ToString("N0"), points > 0), l, y);
            var gate = FocusContext.AsString(_data["stargateAddress"]);
            if (!string.IsNullOrEmpty(gate))
                Row(Trans.Get("vr.survey.stargate"), gate, r, y);
            y -= 58f;

            Text(Trans.Get("buildings"), l, y, 400f, 18f, DiegeticUi.CyanDim);
            y -= 36f;
            var col = 0;
            foreach (var b in KeyBuildings)
            {
                var level = FocusContext.AsInt(_data[b]);
                if (level <= 0)
                    continue;
                var x = l + (col % 3) * 320f;
                Text(Trans.Get(b) + "  <b>" + level + "</b>", x, y - (col / 3) * 34f, 300f, 19f, UiKit.TextBright);
                col++;
            }

            if (col == 0)
                Text("—", l, y, 300f, 19f, DiegeticUi.CyanDim);
        }

        static int Count(JToken rows, string field)
        {
            var n = 0;
            if (rows is JArray a)
                foreach (var row in a)
                    n += FocusContext.AsInt(row[field]);
            return n;
        }

        static string Tone(string t, bool ok) => ok ? "<color=#7dffa0>" + t + "</color>" : "<color=#ff9a6a>" + t + "</color>";

        void Row(string label, string value, float x, float y)
        {
            Text(label, x, y, 250f, 19f, DiegeticUi.CyanDim);
            Text(value, x + 250f, y, 220f, 21f, UiKit.TextBright, TextAlignmentOptions.MidlineRight);
        }

        TMP_Text Text(string text, float x, float y, float width, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var left = align is TextAlignmentOptions.MidlineLeft;
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(left ? x + width * 0.5f : x, y),
                new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            return t;
        }
    }
}
