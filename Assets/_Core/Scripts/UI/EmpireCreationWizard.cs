using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// Empire founding in the airlock (web view/create-empire.php → CreateEmpire action), for an account with
    /// no empire yet. Same fields, same validations, split into steps a standing player can do on the console:
    /// identity + flag, government (authority, ethics ≤ max), species (name, type), traits (2 positive,
    /// 2 negative), homeworld + leader (lore), confirmation. The flag is painted like web empireFlag.js,
    /// on the console and as a hologram over it. Never sends the player to the website.
    /// </summary>
    public sealed class EmpireCreationWizard : MonoBehaviour
    {
        enum Step
        {
            Identity,
            Government,
            Species,
            Traits,
            Leader,
            Confirm
        }

        const int StepCount = 6;
        const int NameMin = 3;

        /// <summary>Swatches for the flag (web uses a free colour input; VR offers a curated palette incl. its defaults).</summary>
        static readonly string[] Palette =
        {
            "#001f3f", "#0074d9", "#7fdbff", "#39cccc", "#2ecc40", "#01ff70", "#ffdc00",
            "#ff851b", "#ff4136", "#f012be", "#b10dc9", "#ffffff", "#aaaaaa", "#111111"
        };

        static readonly string[] StepKeys =
            { "empire-name", "authority", "createSpecies", "vr.create.traits", "createLeader", "vr.create.confirm" };

        RectTransform _root;
        RectTransform _body;
        TMP_Text _status;
        readonly Button[] _stepButtons = new Button[StepCount];
        Button _back;
        Button _next;
        Action _onCancel;
        Action _onCreated;

        Step _step;
        int _visited;
        bool _busy;
        bool _catalogReady;

        // Choices
        readonly FlagSpec _flag = new();
        string _empireName = string.Empty;
        int _authority;
        readonly List<int> _ethics = new();
        string _speciesName = string.Empty;
        int _speciesType;
        readonly List<int> _pos = new();
        readonly List<int> _neg = new();
        string _planetName = string.Empty;
        string _shipPrefix = string.Empty;
        string _leaderName = string.Empty;
        string _leaderSex = string.Empty;
        string _leaderTitle = string.Empty;
        string _heirTitle = string.Empty;
        readonly List<string> _leaderTraits = new();
        string _bio = string.Empty;

        // Catalogs (server)
        JArray _authorities;
        JArray _policies;
        JArray _speciesTypes;
        JArray _traits;
        JArray _leaderTraitList;
        // GetConfigs.empire (web faf0614); the values until it answers match the server defaults.
        int _maxEthics = 2;
        int _leaderTraitsMax = 3;

        Texture2D _flagTex;
        RawImage _flagPreview;
        Transform _holoFlag;

        public static EmpireCreationWizard Create(RectTransform parent, TMP_Text status, Transform holoAnchor,
            Action onCancel, Action onCreated)
        {
            var go = new GameObject("EmpireCreation", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = parent.sizeDelta;
            var w = go.AddComponent<EmpireCreationWizard>();
            w._root = rt;
            w._status = status;
            w._onCancel = onCancel;
            w._onCreated = onCreated;
            w.BuildShell(holoAnchor);
            go.SetActive(false);
            return w;
        }

        void BuildShell(Transform holoAnchor)
        {
            for (var i = 0; i < StepCount; i++)
            {
                var step = (Step)i;
                _stepButtons[i] = DiegeticUi.HoloButton(_root, (i + 1) + ". " + Trans.Get(StepKeys[i]),
                    new Vector2(-455f + i * 182f, 238f), new Vector2(176f, 38f), () => GoTo(step),
                    DiegeticUi.BtnStyle.Ghost);
                var label = _stepButtons[i].GetComponentInChildren<TMP_Text>();
                label.enableAutoSizing = true;
                label.fontSizeMin = 11f;
                label.fontSizeMax = 15f;
            }

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(_root, false);
            _body = bodyGo.GetComponent<RectTransform>();
            _body.sizeDelta = new Vector2(1060f, 420f);

            _back = DiegeticUi.HoloButton(_root, Trans.Get("previous"), new Vector2(-400f, -238f), new Vector2(220f, 52f),
                () => GoTo(_step - 1), DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(_root, Trans.Get("cancel"), new Vector2(0f, -238f), new Vector2(200f, 52f),
                () => _onCancel?.Invoke(), DiegeticUi.BtnStyle.Ghost);
            _next = DiegeticUi.HoloButton(_root, Trans.Get("next"), new Vector2(400f, -238f), new Vector2(220f, 52f),
                Next, DiegeticUi.BtnStyle.Cyan);

            _flagTex = FlagPainter.Paint(_flag);

            // Hologram of the flag over the console: the banner you are founding, slowly turning.
            if (holoAnchor != null)
            {
                var holo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                holo.name = "FlagHologram";
                Destroy(holo.GetComponent<Collider>());
                holo.transform.SetParent(holoAnchor, false);
                holo.transform.localPosition = new Vector3(0f, 0.64f, 0.05f);
                holo.transform.localScale = new Vector3(0.36f, 0.24f, 1f);
                var art = FindFirstObjectByType<CicEnvironment>()?.Art;
                var mr = holo.GetComponent<MeshRenderer>();
                if (art != null)
                    // True colours (the holo shader would wash the banner out): unlit albedo, no added emission.
                    mr.sharedMaterial = art.Lit(_flagTex, Color.white, 0f);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var spin = holo.AddComponent<HoloSpin>();
                spin.DegreesPerSecond = 0f;
                spin.BobMeters = 0.012f;
                _holoFlag = holo.transform;
                holo.SetActive(false);
            }
        }

        public async void Open()
        {
            gameObject.SetActive(true);
            if (_holoFlag != null)
                _holoFlag.gameObject.SetActive(true);
            _step = Step.Identity;
            _visited = 0;
            Render();
            if (!_catalogReady)
                await LoadCatalogs();
            Render();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (_holoFlag != null)
                _holoFlag.gameObject.SetActive(false);
        }

        async Task LoadCatalogs()
        {
            SetStatus(Trans.Get("Loading"), false);
            var au = ActionJs.Get("GetAuthorities");
            var st = ActionJs.Get("GetSpeciesTypes");
            var tr = ActionJs.Get("GetSpeciesTraits");
            var lt = ActionJs.Get("GetLeaderTraits");
            var cfg = ActionJs.Get("GetConfigs");
            await Task.WhenAll(au, st, tr, lt, cfg);
            _authorities = Array(au.Result);
            _speciesTypes = Array(st.Result);
            _traits = Array(tr.Result);
            _leaderTraitList = Array(lt.Result);
            if (cfg.Result.Ok)
            {
                try
                {
                    var root = JObject.Parse(cfg.Result.Body);
                    _policies = root["policies"] as JArray ?? root["ethics"] as JArray;
                    var max = FocusContext.AsInt(root["empire"]?["maxPolicies"]);
                    if (max > 0)
                        _maxEthics = max;
                    var traits = FocusContext.AsInt(root["empire"]?["leaderTraitsMax"]);
                    if (traits > 0)
                        _leaderTraitsMax = traits;
                }
                catch
                {
                    _policies = null;
                }
            }

            _catalogReady = _authorities != null && _speciesTypes != null && _traits != null && _policies != null;
            if (_authorities != null && _authorities.Count > 0 && _authority == 0)
                _authority = FocusContext.AsInt(_authorities[0]["id"]);
            if (_speciesTypes != null && _speciesTypes.Count > 0 && _speciesType == 0)
                _speciesType = FocusContext.AsInt(_speciesTypes[0]["id"]);
            SetStatus(_catalogReady ? string.Empty : Trans.Get("vr.common.error"), !_catalogReady);
        }

        static JArray Array(ApiResult r)
        {
            if (!r.Ok || string.IsNullOrEmpty(r.Body))
                return null;
            try
            {
                return JToken.Parse(r.Body) as JArray;
            }
            catch
            {
                return null;
            }
        }

        // ── Navigation ────────────────────────────────────────────────────────────

        void GoTo(Step step)
        {
            if (step < Step.Identity || (int)step > _visited || _busy)
                return;
            _step = step;
            Render();
        }

        void Next()
        {
            if (_busy)
                return;
            var error = Validate(_step);
            if (error != null)
            {
                SetStatus(error, true);
                CicCue.Fail(transform.position);
                return;
            }

            if (_step == Step.Confirm)
            {
                AsyncTap.Run(Submit());
                return;
            }

            _step++;
            _visited = Mathf.Max(_visited, (int)_step);
            SetStatus(string.Empty, false);
            Render();
        }

        string Validate(Step step)
        {
            switch (step)
            {
                case Step.Identity:
                    var n = (_empireName ?? string.Empty).Trim();
                    if (n.Length == 0)
                        return Trans.Get("fillAllField");
                    if (n.Length < NameMin)
                        return Trans.Get("insert3Character");
                    return null;
                default:
                    return _catalogReady || step == Step.Identity ? null : Trans.Get("Loading");
            }
        }

        void SetStatus(string text, bool error)
        {
            if (_status == null)
                return;
            _status.text = text ?? string.Empty;
            _status.color = error ? new Color(1f, 0.45f, 0.38f) : new Color(0.45f, 0.95f, 1f);
        }

        // ── Render ────────────────────────────────────────────────────────────────

        void Render()
        {
            for (var i = _body.childCount - 1; i >= 0; i--)
                DestroyImmediate(_body.GetChild(i).gameObject);
            _flagPreview = null;

            for (var i = 0; i < StepCount; i++)
            {
                var b = _stepButtons[i];
                b.interactable = i <= _visited;
                var label = b.GetComponentInChildren<TMP_Text>();
                label.color = i == (int)_step ? UiKit.Cyan : i <= _visited ? UiKit.TextBright : new Color(1f, 1f, 1f, 0.35f);
            }

            _back.interactable = _step > Step.Identity;
            _next.GetComponentInChildren<TMP_Text>().text =
                Trans.Get(_step == Step.Confirm ? "create" : "next");

            switch (_step)
            {
                case Step.Identity:
                    RenderIdentity();
                    break;
                case Step.Government:
                    RenderGovernment();
                    break;
                case Step.Species:
                    RenderSpecies();
                    break;
                case Step.Traits:
                    RenderTraits();
                    break;
                case Step.Leader:
                    RenderLeader();
                    break;
                case Step.Confirm:
                    RenderConfirm();
                    break;
            }
        }

        TMP_Text Label(string text, float x, float y, float size, Color color, float width = 400f,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            // Left-aligned labels are placed by their left edge (x), centred ones by their centre.
            var left = align is TextAlignmentOptions.MidlineLeft or TextAlignmentOptions.TopLeft or TextAlignmentOptions.Left;
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(left ? x + width * 0.5f : x, y),
                new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            return t;
        }

        TMP_InputField Field(string placeholderKey, string value, float x, float y, float width, int limit,
            Action<string> onChange)
        {
            var f = DiegeticUi.HoloField(_body, placeholderKey, Trans.Get(placeholderKey), new Vector2(x, y),
                new Vector2(width, 48f), TouchScreenKeyboardType.Default);
            f.text = value ?? string.Empty;
            if (limit > 0)
                f.characterLimit = limit;
            f.onValueChanged.AddListener(v => onChange(v));
            return f;
        }

        Button Choice(string label, bool selected, float x, float y, Vector2 size, Action onClick)
        {
            var b = DiegeticUi.HoloButton(_body, label, new Vector2(x, y), size, () =>
                {
                    onClick();
                    Render();
                },
                selected ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.enableAutoSizing = true;
            t.fontSizeMin = 10f;
            t.fontSizeMax = Mathf.Min(18f, size.y * 0.42f);
            if (!selected)
                t.color = new Color(0.78f, 0.9f, 0.96f, 0.9f);
            return b;
        }

        void Swatches(float x0, float y, string current, Action<string> pick)
        {
            for (var i = 0; i < Palette.Length; i++)
            {
                var hex = Palette[i];
                var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_body, false);
                var rt = go.GetComponent<RectTransform>();
                var selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                rt.sizeDelta = selected ? new Vector2(38f, 38f) : new Vector2(30f, 30f);
                rt.anchoredPosition = new Vector2(x0 + i * 40f, y);
                var img = go.GetComponent<Image>();
                img.color = FlagPainter.Hex(hex, Color.white);
                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    CicCue.Ok(go.transform.position);
                    pick(hex);
                    RepaintFlag();
                    Render();
                });
                if (selected)
                {
                    var ring = new GameObject("Sel", typeof(RectTransform), typeof(Image));
                    ring.transform.SetParent(go.transform, false);
                    var r = ring.GetComponent<RectTransform>();
                    r.sizeDelta = new Vector2(46f, 6f);
                    r.anchoredPosition = new Vector2(0f, -26f);
                    var ri = ring.GetComponent<Image>();
                    ri.color = UiKit.Cyan;
                    ri.raycastTarget = false;
                }
            }
        }

        void RepaintFlag() => FlagPainter.Paint(_flag, _flagTex);

        void FlagPreview(float x, float y, float width)
        {
            var go = new GameObject("FlagPreview", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_body, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, width * FlagPainter.Height / FlagPainter.Width);
            rt.anchoredPosition = new Vector2(x, y);
            _flagPreview = go.GetComponent<RawImage>();
            _flagPreview.texture = _flagTex;
            _flagPreview.raycastTarget = false;
        }

        void RenderIdentity()
        {
            Label(Trans.Get("empire-name"), -510f, 190f, 18f, DiegeticUi.CyanDim);
            Field("empire-name", _empireName, -260f, 150f, 500f, 40, v => _empireName = v);

            Label(Trans.Get("createEmpireFlagHint"), -510f, 100f, 16f, DiegeticUi.CyanDim, 560f);
            Label(Trans.Get("vr.create.flagBackground"), -510f, 62f, 16f, UiKit.TextBright, 150f);
            Swatches(-330f, 62f, _flag.Bg, hex => _flag.Bg = hex);

            for (var i = 0; i < 3; i++)
            {
                var slot = i;
                var y = 4f - i * 64f;
                Label(Trans.Format("vr.create.flagShape", i + 1), -510f, y + 14f, 16f, UiKit.TextBright, 150f);
                var shapeIndex = Mathf.Max(0, System.Array.IndexOf(FlagSpec.Shapes, _flag.Shape[slot]));
                DiegeticUi.HoloButton(_body, "‹", new Vector2(-490f, y - 16f), new Vector2(40f, 34f), () =>
                {
                    _flag.Shape[slot] = FlagSpec.Shapes[(shapeIndex - 1 + FlagSpec.Shapes.Length) % FlagSpec.Shapes.Length];
                    RepaintFlag();
                    Render();
                }, DiegeticUi.BtnStyle.Ghost);
                Label(Trans.Get("flagShape_" + _flag.Shape[slot]), -400f, y - 16f, 16f, UiKit.Cyan, 130f,
                    TextAlignmentOptions.Center);
                DiegeticUi.HoloButton(_body, "›", new Vector2(-310f, y - 16f), new Vector2(40f, 34f), () =>
                {
                    _flag.Shape[slot] = FlagSpec.Shapes[(shapeIndex + 1) % FlagSpec.Shapes.Length];
                    RepaintFlag();
                    Render();
                }, DiegeticUi.BtnStyle.Ghost);
                Swatches(-250f, y - 16f, _flag.Color[slot], hex => _flag.Color[slot] = hex);
            }

            FlagPreview(390f, 140f, 210f);
        }

        void RenderGovernment()
        {
            Label(Trans.Get("authority"), -510f, 190f, 18f, DiegeticUi.CyanDim);
            if (_authorities != null)
            {
                for (var i = 0; i < _authorities.Count; i++)
                {
                    var id = FocusContext.AsInt(_authorities[i]["id"]);
                    var col = i % 4;
                    var row = i / 4;
                    Choice(Trans.Get(FocusContext.AsString(_authorities[i]["name"])), id == _authority,
                        -390f + col * 260f, 145f - row * 54f, new Vector2(250f, 46f), () => _authority = id);
                }
            }

            Label(Trans.Get("ethics") + "  ·  " + Trans.Get("chooseUpTo2Ethics").Replace("{max}",
                _maxEthics.ToString(CultureInfo.InvariantCulture)), -510f, 50f, 17f, DiegeticUi.CyanDim, 900f);
            if (_policies == null)
                return;
            for (var i = 0; i < _policies.Count; i++)
            {
                var id = FocusContext.AsInt(_policies[i]["id"]);
                var col = i % 3;
                var row = i / 3;
                var on = _ethics.Contains(id);
                Choice(Trans.Get(FocusContext.AsString(_policies[i]["name"])), on, -350f + col * 350f, 4f - row * 54f,
                    new Vector2(336f, 46f), () => Toggle(_ethics, id, _maxEthics));
            }
        }

        void Toggle<T>(List<T> list, T value, int max)
        {
            if (list.Remove(value))
                return;
            if (list.Count >= max)
            {
                SetStatus(Trans.Get("vr.create.limitReached"), true);
                CicCue.Fail(transform.position);
                return;
            }

            list.Add(value);
            SetStatus(string.Empty, false);
        }

        void RenderSpecies()
        {
            Label(Trans.Get("name") + "  <size=80%>(" + Trans.Get("optional") + ")</size>", -510f, 190f, 18f,
                DiegeticUi.CyanDim);
            Field("createSpecies", _speciesName, -260f, 150f, 500f, 40, v => _speciesName = v);
            Label(Trans.Get("type"), -510f, 100f, 18f, DiegeticUi.CyanDim);
            if (_speciesTypes == null)
                return;
            for (var i = 0; i < _speciesTypes.Count; i++)
            {
                var id = FocusContext.AsInt(_speciesTypes[i]["id"]);
                var col = i % 5;
                var row = i / 5;
                Choice(Trans.Get(FocusContext.AsString(_speciesTypes[i]["name"])), id == _speciesType,
                    -420f + col * 210f, 55f - row * 54f, new Vector2(200f, 46f), () => _speciesType = id);
            }
        }

        void RenderTraits()
        {
            if (_traits == null)
                return;
            Label(Trans.Get("positiveTraits") + "  (" + _pos.Count + "/2)", -510f, 195f, 17f, DiegeticUi.CyanDim);
            Label(Trans.Get("negativeTraits") + "  (" + _neg.Count + "/2)", -510f, -20f, 17f, DiegeticUi.CyanDim);
            int p = 0, n = 0;
            foreach (var t in _traits)
            {
                var id = FocusContext.AsInt(t["id"]);
                var positive = FocusContext.AsInt(t["type"]) == 1;
                var index = positive ? p++ : n++;
                var col = index % 5;
                var row = index / 5;
                var y = positive ? 160f - row * 42f : -55f - row * 42f;
                var list = positive ? _pos : _neg;
                var label = Trans.Get(FocusContext.AsString(t["name"])) + "  <size=75%><color=#ffb347>" +
                            Effect(t) + "</color></size>";
                Choice(label, list.Contains(id), -420f + col * 210f, y, new Vector2(204f, 38f),
                    () => Toggle(list, id, 2));
            }
        }

        /// <summary>"+15 %" + the affected stat (trait key → traitEffect_&lt;key&gt;).</summary>
        static string Effect(JToken trait)
        {
            var value = FocusContext.AsInt(trait["value"]);
            var sign = value > 0 ? "+" : string.Empty;
            return Trans.Get("traitEffect_" + FocusContext.AsString(trait["key"])) + " " + sign + value + " %";
        }

        void RenderLeader()
        {
            Label(Trans.Get("planetName"), -510f, 196f, 16f, DiegeticUi.CyanDim, 240f);
            Field("planetName", _planetName, -390f, 162f, 240f, 32, v => _planetName = v);
            Label(Trans.Get("shipPrefix"), -250f, 196f, 16f, DiegeticUi.CyanDim, 240f);
            Field("shipPrefixHint", _shipPrefix, -130f, 162f, 240f, 20, v => _shipPrefix = v);
            Label(Trans.Get("leaderName"), 10f, 196f, 16f, DiegeticUi.CyanDim, 240f);
            Field("leaderName", _leaderName, 130f, 162f, 240f, 40, v => _leaderName = v);

            Label(Trans.Get("leaderTitle"), -510f, 122f, 16f, DiegeticUi.CyanDim, 240f);
            Field("leaderTitle", _leaderTitle, -390f, 88f, 240f, 40, v => _leaderTitle = v);
            Label(Trans.Get("heirTitle"), -250f, 122f, 16f, DiegeticUi.CyanDim, 240f);
            Field("heirTitle", _heirTitle, -130f, 88f, 240f, 40, v => _heirTitle = v);

            Label(Trans.Get("leaderSex"), 10f, 122f, 16f, DiegeticUi.CyanDim, 240f);
            var sexes = new[] { "male", "female", "other" };
            for (var i = 0; i < sexes.Length; i++)
            {
                var s = sexes[i];
                Choice(Trans.Get(s), _leaderSex == s, 50f + i * 128f, 88f, new Vector2(120f, 42f),
                    () => _leaderSex = _leaderSex == s ? string.Empty : s);
            }

            Label(Trans.Get("leaderTraits") + "  ·  " + Trans.Get("chooseUpToNLeaderTraits").Replace("{max}",
                _leaderTraitsMax.ToString(CultureInfo.InvariantCulture)), -510f, 40f, 16f, DiegeticUi.CyanDim, 900f);
            if (_leaderTraitList != null)
            {
                for (var i = 0; i < _leaderTraitList.Count; i++)
                {
                    var trait = (string)_leaderTraitList[i];
                    var col = i % 6;
                    var row = i / 6;
                    Choice(Trans.Get(trait), _leaderTraits.Contains(trait), -440f + col * 176f, 2f - row * 48f,
                        new Vector2(168f, 42f), () => Toggle(_leaderTraits, trait, _leaderTraitsMax));
                }
            }

            Label(Trans.Get("bio") + "  <size=80%>(" + Trans.Get("optional") + ")</size>", -510f, -100f, 16f,
                DiegeticUi.CyanDim);
            Field("bio", _bio, 0f, -140f, 1040f, 400, v => _bio = v);
        }

        void RenderConfirm()
        {
            FlagPreview(-330f, 90f, 300f);
            var authority = NameOf(_authorities, _authority);
            var ethics = new List<string>();
            foreach (var id in _ethics)
                ethics.Add(NameOf(_policies, id));
            var traits = new List<string>();
            foreach (var id in _pos)
                traits.Add(NameOf(_traits, id));
            foreach (var id in _neg)
                traits.Add(NameOf(_traits, id));
            var species = string.IsNullOrWhiteSpace(_speciesName) ? _empireName.Trim() : _speciesName.Trim();

            var lines =
                "<size=130%><b>" + _empireName.Trim() + "</b></size>\n" +
                Trans.Get("authority") + " : " + authority + "\n" +
                Trans.Get("ethics") + " : " + (ethics.Count > 0 ? string.Join(", ", ethics) : "—") + "\n" +
                Trans.Get("createSpecies") + " : " + species + " · " + NameOf(_speciesTypes, _speciesType) + "\n" +
                (traits.Count > 0 ? string.Join(", ", traits) + "\n" : string.Empty) +
                (string.IsNullOrWhiteSpace(_planetName) ? string.Empty : Trans.Get("planetName") + " : " + _planetName.Trim() + "\n") +
                (string.IsNullOrWhiteSpace(_leaderName) ? string.Empty
                    : Trans.Get("leaderName") + " : " + (_leaderTitle.Trim() + " " + _leaderName.Trim()).Trim() + "\n");
            var t = Label(lines, -10f, 40f, 19f, UiKit.TextBright, 520f, TextAlignmentOptions.TopLeft);
            t.rectTransform.sizeDelta = new Vector2(520f, 300f);
            t.textWrappingMode = TextWrappingModes.Normal;

            var hint = Label(Trans.Get("createEmpireNextStepsHint"), 0f, -150f, 15f, DiegeticUi.CyanDim, 1000f,
                TextAlignmentOptions.Center);
            hint.textWrappingMode = TextWrappingModes.Normal;
        }

        static string NameOf(JArray list, int id)
        {
            if (list == null)
                return "—";
            foreach (var e in list)
            {
                if (FocusContext.AsInt(e["id"]) == id)
                    return Trans.Get(FocusContext.AsString(e["name"]));
            }

            return "—";
        }

        // ── Submit ────────────────────────────────────────────────────────────────

        async Task Submit()
        {
            _busy = true;
            SetStatus(Trans.Get("Loading"), false);
            try
            {
                var q = new Dictionary<string, string>
                {
                    { "empireName", _empireName.Trim() },
                    { "bgColor", _flag.Bg },
                    { "shape1", _flag.Shape[0] }, { "color1", _flag.Color[0] },
                    { "shape2", _flag.Shape[1] }, { "color2", _flag.Color[1] },
                    { "shape3", _flag.Shape[2] }, { "color3", _flag.Color[2] },
                    { "authority", _authority.ToString(CultureInfo.InvariantCulture) },
                    { "speciesType", _speciesType.ToString(CultureInfo.InvariantCulture) }
                };
                if (_ethics.Count > 0)
                    q["ethics"] = string.Join(",", _ethics);
                AddIf(q, "speciesName", _speciesName);
                if (_pos.Count > 0) q["traitPos1"] = _pos[0].ToString(CultureInfo.InvariantCulture);
                if (_pos.Count > 1) q["traitPos2"] = _pos[1].ToString(CultureInfo.InvariantCulture);
                if (_neg.Count > 0) q["traitNeg1"] = _neg[0].ToString(CultureInfo.InvariantCulture);
                if (_neg.Count > 1) q["traitNeg2"] = _neg[1].ToString(CultureInfo.InvariantCulture);
                AddIf(q, "planetName", _planetName);
                AddIf(q, "shipPrefix", _shipPrefix);
                AddIf(q, "leaderName", _leaderName);
                AddIf(q, "leaderSex", _leaderSex);
                AddIf(q, "leaderTitle", _leaderTitle);
                AddIf(q, "heirTitle", _heirTitle);
                if (_leaderTraits.Count > 0)
                    q["leaderTraits"] = string.Join(",", _leaderTraits);
                AddIf(q, "empireBio", _bio);

                var result = await ActionJs.Get("CreateEmpire", q);
                if (!result.Ok)
                {
                    CicCue.Fail(transform.position);
                    SetStatus(string.IsNullOrEmpty(result.Error) ? Trans.Get("vr.common.error") : result.Error, true);
                    return;
                }

                CicCue.Ok(transform.position);
                SetStatus(Trans.Get("vr.create.founded"), false);
                _onCreated?.Invoke();
            }
            finally
            {
                _busy = false;
            }
        }

        static void AddIf(Dictionary<string, string> q, string key, string value)
        {
            var v = (value ?? string.Empty).Trim();
            if (v.Length > 0)
                q[key] = v;
        }

        void Update()
        {
            if (_holoFlag == null || !_holoFlag.gameObject.activeSelf)
                return;
            // Slow turn to show it is a banner in space, never edge-on for long.
            var yaw = Mathf.Sin(Time.time * 0.35f) * 35f;
            _holoFlag.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
