using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Panel + API orders for the inhabited ship — buttons only when feasible.
    /// </summary>
    public class ViewFleetOrders : MonoBehaviour
    {
        FocusContext _focus;
        FleetPoller _poller;
        HoloZoneMap _map;
        HexBattleController _hex;
        Transform _panel;
        Transform _btnRoot;
        TMP_Text _label;
        CicArtKit _art;
        readonly List<GameObject> _buttons = new();
        long _lastSig = -1;
        float _nextPoll;

        public void Bind(FocusContext focus, FleetPoller poller, HoloZoneMap map, CicArtKit art,
            Transform mount, HexBattleController hex = null)
        {
            _focus = focus;
            _poller = poller;
            _map = map;
            _art = art;
            _hex = hex;
            BuildShell(mount);
            if (_focus != null)
            {
                _focus.Changed -= Rebuild;
                _focus.Changed += Rebuild;
            }

            Rebuild();
        }

        public void SetCombatRemap(bool on) => Rebuild();

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= Rebuild;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll)
                return;
            _nextPoll = Time.unscaledTime + 0.5f;
            var fleet = _focus?.FindViewFleet();
            var sig = Signature(fleet);
            if (sig == _lastSig)
                return;
            _lastSig = sig;
            Rebuild();
        }

        long Signature(FocusFleet fleet)
        {
            if (fleet == null)
                return _focus != null && _focus.ViewPlanetId > 0 ? -2 : -3;
            var hex = _hex != null && _hex.IsActive ? 1 : 0;
            return fleet.DestTime ^ (fleet.PlanetId * 17) ^ (fleet.AsteroidId * 31) ^
                   (fleet.IsInBattle ? 1 : 0) ^ fleet.AttackEndTime ^ fleet.HarvestEndTime ^
                   fleet.ExploreEndTime ^ (hex * 997) ^ (_focus.SystemId * 13);
        }

        void BuildShell(Transform mount)
        {
            if (mount == null || _panel != null)
                return;
            _panel = new GameObject("ViewFleetPanel").transform;
            _panel.SetParent(mount, false);
            _panel.localPosition = new Vector3(0.55f, 0.35f, 0.1f);
            _panel.localRotation = Quaternion.Euler(25f, -20f, 0f);

            DiegeticUi.Panel(_panel, "Board", Vector3.zero, new Vector3(0.35f, 0.45f, 0.03f), _art,
                out _);

            _label = DiegeticUi.Label(_panel, "Label", string.Empty, new Vector3(0f, 0.18f, -0.03f),
                0.025f, 6f, new Color(0.6f, 0.95f, 1f));
            _label.rectTransform.sizeDelta = new Vector2(20f, 6f);

            _btnRoot = new GameObject("Buttons").transform;
            _btnRoot.SetParent(_panel, false);
            _btnRoot.localPosition = Vector3.zero;
        }

        void Rebuild()
        {
            ClearButtons();
            if (_btnRoot == null || _focus == null)
                return;

            var fleet = _focus.FindViewFleet();
            if (fleet == null)
            {
                if (_label != null)
                    _label.text = _focus.ViewPlanetId > 0 ? Trans.Get("planets") : "—";
                return;
            }

            if (_label != null)
            {
                _label.text = string.IsNullOrEmpty(fleet.Name) ? "ship " + fleet.Id : fleet.Name;
                if (fleet.IsInBattle)
                    _label.text += " · " + Trans.Get("battle");
            }

            var y = 0.08f;
            const float step = 0.08f;

            if (_hex != null && _hex.IsActive)
            {
                AddButton(Trans.Get("EndTurn"), "EndTurn", new Vector3(0f, y, -0.02f),
                    () => _ = EndTurnCombat());
                y -= step;
            }

            if (FleetOrderGate.CanStance(fleet))
            {
                AddButton(Trans.Get("Flee"), "Flee", new Vector3(0f, y, -0.02f),
                    () => _ = Stance("RUN_AWAY"));
                y -= step;
                AddButton(Trans.Get("Defend"), "Defend", new Vector3(0f, y, -0.02f),
                    () => _ = Stance("ATTACK_ATTACKER"));
                y -= step;
                AddButton(Trans.Get("Attack"), "Attack", new Vector3(0f, y, -0.02f),
                    () => _ = Stance("ATTACK_PLANET"));
                y -= step;
            }

            if (FleetOrderGate.CanMine(fleet))
            {
                AddButton(Trans.Get("Mine"), "Mine", new Vector3(0f, y, -0.02f),
                    () => _ = Mine());
                y -= step;
            }

            if (FleetOrderGate.CanSiege(fleet, _focus))
            {
                AddButton(Trans.Get("Siege"), "Siege", new Vector3(0f, y, -0.02f),
                    () => _ = Siege());
                y -= step;
            }

            if (FleetOrderGate.CanExplore(fleet))
            {
                AddButton(Trans.Get("Explore"), "Explore", new Vector3(0f, y, -0.02f),
                    () => _ = Explore());
                y -= step;
            }

            if (FleetOrderGate.CanCargo(fleet, _focus))
            {
                AddButton(Trans.Get("Deposit"), "Deposit", new Vector3(-0.12f, y, -0.02f),
                    () => _ = Cargo(true));
                AddButton(Trans.Get("Withdraw"), "Withdraw", new Vector3(0.12f, y, -0.02f),
                    () => _ = Cargo(false));
                y -= step;
            }

            if (_buttons.Count == 0 && !FleetOrderGate.CanMove(fleet))
            {
                AddButton(Trans.Get(FleetOrderGate.BusyKey(fleet)), "Busy", new Vector3(0f, 0.08f, -0.02f),
                    null, interact: false);
            }
        }

        void ClearButtons()
        {
            for (var i = 0; i < _buttons.Count; i++)
                if (_buttons[i] != null)
                    Destroy(_buttons[i]);
            _buttons.Clear();
        }

        void AddButton(string label, string name, Vector3 local, System.Action act, bool interact = true)
        {
            var xi = DiegeticUi.Button(_btnRoot, "Btn_" + name, label, local,
                new Vector3(0.28f, 0.06f, 0.02f), _art, CicArtKit.Cyan, act, interact);
            _buttons.Add(xi.gameObject);
        }

        async Task Stance(string position)
        {
            var fleet = _focus?.FindViewFleet();
            if (!FleetOrderGate.CanStance(fleet))
                return;
            await Call("UpdateFleetDefendPosition", new Dictionary<string, string>
            {
                { "id", fleet.Id.ToString() },
                { "position", position }
            });
        }

        async Task Mine()
        {
            var fleet = _focus?.FindViewFleet();
            if (!FleetOrderGate.CanMine(fleet))
                return;
            await Call("HarvestAsteroid", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "asteroid", fleet.AsteroidId.ToString() }
            });
        }

        async Task Siege()
        {
            var fleet = _focus?.FindViewFleet();
            if (!FleetOrderGate.CanSiege(fleet, _focus))
                return;
            await Call("CheckPlanetAttack", new Dictionary<string, string>
            {
                { "planet", fleet.PlanetId.ToString() }
            });
            await Call("FleetAttackPlanet", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "planet", fleet.PlanetId.ToString() }
            });
        }

        async Task Explore()
        {
            var fleet = _focus?.FindViewFleet();
            if (!FleetOrderGate.CanExplore(fleet))
                return;
            await Call("ExplorePlanet", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "planet", fleet.PlanetId.ToString() }
            });
        }

        async Task Cargo(bool deposit)
        {
            var fleet = _focus?.FindViewFleet();
            if (!FleetOrderGate.CanCargo(fleet, _focus))
                return;
            await Call(deposit ? "DepositCargo" : "WithdrawCargo", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "planet", fleet.PlanetId.ToString() }
            });
        }

        async Task EndTurnCombat()
        {
            if (_hex != null)
                await _hex.EndTurn();
            Rebuild();
        }

        async Task Call(string action, Dictionary<string, string> query)
        {
            Say(Trans.Get("Loading"));
            var result = await ActionJs.Get(action, query);
            Say(result.Ok ? action : (result.Error ?? action));
            if (result.Ok && _poller != null)
                await _poller.PollNow();
            Rebuild();
        }

        void Say(string msg)
        {
            if (_map != null)
                _map.SetReadout(msg);
            if (_label != null)
                _label.text = msg;
        }
    }
}
