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

namespace Core.Holo
{
    /// <summary>
    /// The cargo transfer pad: what goes down to the planet (DepositCargo) or up into the hold (WithdrawCargo),
    /// resource by resource and to the unit — like the web's three-field popup (mineral / crystal / biomass).
    /// Shows the hold (aboard / capacity) and the planet's stock; each row has 0, −, +, Max and a step
    /// (10 → 10 000). Opens in front of the captain; <see cref="Ask"/> returns the amounts, or null when cancelled.
    /// The server caps both ways anyway (storage on deposit, free hold on withdraw).
    /// </summary>
    public sealed class CargoPad : MonoBehaviour
    {
        public readonly struct Amounts
        {
            public readonly int Mineral;
            public readonly int Crystal;
            public readonly int Biomass;

            public Amounts(int mineral, int crystal, int biomass)
            {
                Mineral = mineral;
                Crystal = crystal;
                Biomass = biomass;
            }

            public bool Any => Mineral > 0 || Crystal > 0 || Biomass > 0;

            public void AddTo(Dictionary<string, string> query)
            {
                query["mineral"] = Mineral.ToString(CultureInfo.InvariantCulture);
                query["crystal"] = Crystal.ToString(CultureInfo.InvariantCulture);
                query["biomass"] = Biomass.ToString(CultureInfo.InvariantCulture);
            }
        }

        static CargoPad s_Instance;
        static readonly Vector2 Px = new(700f, 560f);
        static readonly string[] ResourceKeys = { "mineral", "crystal", "biomass" };
        static readonly int[] Steps = { 10, 100, 1000, 10000 };
        static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        GameObject _panel;
        TMP_Text _header;
        TMP_Text _hold;
        TMP_Text _stock;
        readonly TMP_Text[] _name = new TMP_Text[3];
        readonly TMP_Text[] _value = new TMP_Text[3];
        readonly Image[] _stepBox = new Image[4];
        readonly int[] _amount = new int[3];
        readonly int[] _max = new int[3];
        int _step = 2;
        bool _withdraw;
        int _free;
        TaskCompletionSource<Amounts?> _pending;

        /// <summary>
        /// Ask how much to move between <paramref name="fleet"/> and <paramref name="planetId"/> (orbited, ours).
        /// <paramref name="withdraw"/>: planet → hold; else hold → planet.
        /// </summary>
        public static async Task<Amounts?> Ask(FocusFleet fleet, int planetId, string planetName, bool withdraw)
        {
            if (fleet == null || planetId <= 0)
                return null;
            if (s_Instance == null)
            {
                var go = new GameObject("CargoPad");
                s_Instance = go.AddComponent<CargoPad>();
                s_Instance.Build();
            }

            var stock = await Stock(planetId);
            return await s_Instance.Open(fleet, planetName, stock, withdraw);
        }

        /// <summary>The planet's stock right now (GetResource raw=1, as the web reads it before the popup).</summary>
        static async Task<(int mineral, int crystal, int biomass)> Stock(int planetId)
        {
            var r = await ActionJs.Get("GetResource", new Dictionary<string, string>
            {
                { "planet", planetId.ToString() },
                { "raw", "1" }
            });
            if (r.Ok && !string.IsNullOrEmpty(r.Body))
                try
                {
                    var o = JToken.Parse(r.Body);
                    if (o is JArray a && a.Count > 0)
                        o = a[0];
                    if (o is JObject p)
                        return (FocusContext.AsInt(p["mineral"]), FocusContext.AsInt(p["crystal"]), FocusContext.AsInt(p["biomass"]));
                }
                catch
                {
                    // Fall back on the economy heartbeat below.
                }

            var eco = EconomyService.Instance;
            if (eco != null && eco.Planets.TryGetValue(planetId, out var e))
                return ((int)e.Mineral, (int)e.Crystal, (int)e.Biomass);
            return (0, 0, 0);
        }

        Task<Amounts?> Open(FocusFleet fleet, string planetName, (int mineral, int crystal, int biomass) stock, bool withdraw)
        {
            _pending?.TrySetResult(null);
            _pending = new TaskCompletionSource<Amounts?>();
            _withdraw = withdraw;
            _free = fleet.CargoFree;
            var aboard = new[] { fleet.MineralCargo, fleet.CrystalCargo, fleet.BiomassCargo };
            var onPlanet = new[] { Mathf.Max(0, stock.mineral), Mathf.Max(0, stock.crystal), Mathf.Max(0, stock.biomass) };
            _header.text = Trans.Get(withdraw ? "withdrawCargo" : "depositCargo") + "  ·  " + planetName;
            _hold.text = Trans.Get("cargo") + "  " + fleet.CargoUsed.ToString("N0", Fr) + " / " + fleet.Cargo.ToString("N0", Fr) +
                         "   —   " + Trans.Get("vr.res.mineral") + " " + aboard[0].ToString("N0", Fr) + " · " +
                         Trans.Get("vr.res.crystal") + " " + aboard[1].ToString("N0", Fr) + " · " +
                         Trans.Get("vr.res.biomass") + " " + aboard[2].ToString("N0", Fr);
            _stock.text = planetName + "  —   " + Trans.Get("vr.res.mineral") + " " + onPlanet[0].ToString("N0", Fr) + " · " +
                          Trans.Get("vr.res.crystal") + " " + onPlanet[1].ToString("N0", Fr) + " · " +
                          Trans.Get("vr.res.biomass") + " " + onPlanet[2].ToString("N0", Fr);

            // Defaults as on the web: unload everything aboard; load what fits, mineral first.
            var room = _free;
            for (var i = 0; i < 3; i++)
            {
                _max[i] = withdraw ? onPlanet[i] : aboard[i];
                if (withdraw)
                {
                    _amount[i] = Mathf.Min(_max[i], room);
                    room -= _amount[i];
                }
                else
                {
                    _amount[i] = _max[i];
                }
            }

            var cam = Camera.main;
            if (cam != null)
            {
                var fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f)
                    fwd = Vector3.forward;
                fwd.Normalize();
                var at = cam.transform.position + fwd * 0.55f + Vector3.down * 0.16f;
                _panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
                foreach (var t in _panel.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = cam.gameObject.layer;
            }

            Refresh();
            _panel.SetActive(true);
            CicCue.Ok(_panel.transform.position);
            return _pending.Task;
        }

        void Build()
        {
            _panel = new GameObject("CargoPadPanel");
            _panel.transform.SetParent(transform, false);
            var canvas = DiegeticUi.WorldCanvas(_panel.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, 0.00072f);
            canvas.sortingOrder = 45;
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            frame.GetComponent<Image>().raycastTarget = false;
            _header = Label(frame, new Vector2(0f, 240f), new Vector2(640f, 40f), 25f, UiKit.Cyan);
            _header.fontStyle = FontStyles.Bold;
            _hold = Label(frame, new Vector2(0f, 200f), new Vector2(640f, 28f), 17f, UiKit.TextBright);
            _stock = Label(frame, new Vector2(0f, 172f), new Vector2(640f, 28f), 17f, UiKit.TextDim);

            // One row per resource: name (and max), 0, −, amount, +, Max.
            for (var r = 0; r < 3; r++)
            {
                var row = r;
                var y = 110f - r * 78f;
                _name[r] = Label(frame, new Vector2(-235f, y), new Vector2(190f, 60f), 19f, UiKit.TextBright, TextAlignmentOptions.MidlineLeft);
                _name[r].textWrappingMode = TextWrappingModes.Normal;
                Key(frame, "0", new Vector2(-100f, y), new Vector2(62f, 58f), () => Set(row, 0), DiegeticUi.BtnStyle.Ghost);
                Key(frame, "−", new Vector2(-30f, y), new Vector2(62f, 58f), () => Set(row, _amount[row] - Steps[_step]), DiegeticUi.BtnStyle.Cyan);
                _value[r] = Label(frame, new Vector2(80f, y), new Vector2(140f, 58f), 26f, UiKit.Amber);
                _value[r].fontStyle = FontStyles.Bold;
                Key(frame, "+", new Vector2(190f, y), new Vector2(62f, 58f), () => Set(row, _amount[row] + Steps[_step]), DiegeticUi.BtnStyle.Cyan);
                Key(frame, Trans.Get("vr.cargo.max"), new Vector2(275f, y), new Vector2(92f, 58f), () => Set(row, int.MaxValue), DiegeticUi.BtnStyle.Ghost);
            }

            // The step of − / +.
            Label(frame, new Vector2(-235f, -134f), new Vector2(190f, 40f), 17f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft).text =
                Trans.Get("vr.cargo.step");
            for (var i = 0; i < Steps.Length; i++)
            {
                var index = i;
                var b = Key(frame, Steps[i].ToString("N0", Fr), new Vector2(-70f + i * 112f, -134f), new Vector2(104f, 48f), () =>
                {
                    _step = index;
                    Refresh();
                }, DiegeticUi.BtnStyle.Ghost);
                _stepBox[i] = b.GetComponent<Image>();
            }

            DiegeticUi.HoloButton(frame, Trans.Get("cancel"), new Vector2(-150f, -222f), new Vector2(260f, 62f),
                () => Close(null), DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(frame, Trans.Get("validate"), new Vector2(150f, -222f), new Vector2(260f, 62f),
                Validate, DiegeticUi.BtnStyle.Amber);
            _panel.SetActive(false);
        }

        static TMP_Text Label(Transform parent, Vector2 pos, Vector2 size, float font, Color c,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = DiegeticUi.HoloLabel(parent, string.Empty, pos, size, font, c, align);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = false;
            return t;
        }

        static Button Key(Transform parent, string label, Vector2 pos, Vector2 size, System.Action act, DiegeticUi.BtnStyle style)
        {
            var b = DiegeticUi.HoloButton(parent, label, pos, size, () => act(), style);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            return b;
        }

        /// <summary>Clamp to what is there, and on a withdraw to what still fits in the hold.</summary>
        void Set(int row, int value)
        {
            var cap = _max[row];
            if (_withdraw)
            {
                var others = 0;
                for (var i = 0; i < 3; i++)
                    if (i != row)
                        others += _amount[i];
                cap = Mathf.Min(cap, Mathf.Max(0, _free - others));
            }

            _amount[row] = Mathf.Clamp(value, 0, cap);
            Refresh();
        }

        void Validate()
        {
            var a = new Amounts(_amount[0], _amount[1], _amount[2]);
            if (!a.Any)
            {
                CicCue.Fail(_panel.transform.position);
                return;
            }

            Close(a);
        }

        void Close(Amounts? result)
        {
            _panel.SetActive(false);
            var p = _pending;
            _pending = null;
            p?.TrySetResult(result);
        }

        void Refresh()
        {
            for (var r = 0; r < 3; r++)
            {
                _name[r].text = Trans.Get("vr.res." + ResourceKeys[r]) + "\n" + Trans.Get("vr.cargo.max") + " " + _max[r].ToString("N0", Fr);
                _value[r].text = _amount[r].ToString("N0", Fr);
            }

            for (var i = 0; i < _stepBox.Length; i++)
                _stepBox[i].color = i == _step ? new Color(1f, 0.85f, 0.5f, 1f) : Color.white;
        }

        void OnDisable()
        {
            _pending?.TrySetResult(null);
            _pending = null;
        }
    }
}
