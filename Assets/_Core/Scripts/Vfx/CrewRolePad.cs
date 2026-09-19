using Core.App;
using Core.Utils;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Tactical / Engineering poke pad — shown only when the action is feasible.
    /// </summary>
    public sealed class CrewRolePad : MonoBehaviour
    {
        public enum Role
        {
            Tactical,
            Engineering
        }

        FocusContext _focus;
        CicArtKit _art;
        Color _accent;
        Role _role;
        ViewFleetOrders _orders;
        HexBattleController _hex;
        Transform _mount;
        GameObject _pad;
        long _lastSig = -1;
        float _nextPoll;

        public void Bind(FocusContext focus, CicArtKit art, Color accent, Role role,
            Transform mount, ViewFleetOrders orders, HexBattleController hex)
        {
            _focus = focus;
            _art = art;
            _accent = accent;
            _role = role;
            _mount = mount;
            _orders = orders;
            _hex = hex;
            if (_focus != null)
            {
                _focus.Changed -= Refresh;
                _focus.Changed += Refresh;
            }

            Refresh();
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= Refresh;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll)
                return;
            _nextPoll = Time.unscaledTime + 0.5f;
            var fleet = _focus?.FindViewFleet();
            long sig;
            if (fleet == null)
                sig = -1;
            else
            {
                var hex = _hex != null && _hex.IsActive ? 1 : 0;
                sig = fleet.PlanetId ^ (fleet.AsteroidId * 31) ^ (fleet.IsInBattle ? 1 : 0) ^
                      fleet.DestTime ^ fleet.AttackEndTime ^ fleet.HarvestEndTime ^ (hex * 97);
            }

            if (sig == _lastSig)
                return;
            _lastSig = sig;
            Refresh();
        }

        void Refresh()
        {
            if (_pad != null)
            {
                Destroy(_pad);
                _pad = null;
            }

            if (_mount == null || _art == null || _focus == null)
                return;

            var fleet = _focus.FindViewFleet();
            if (fleet == null)
                return;

            string label = null;
            System.Action act = null;

            if (_role == Role.Tactical)
            {
                if (_hex != null && _hex.IsActive)
                {
                    label = Trans.Get("EndTurn");
                    act = () => _ = _hex.EndTurn();
                }
                else if (FleetOrderGate.CanSiege(fleet, _focus))
                {
                    label = Trans.Get("Siege");
                    act = () => _ = CrewOrderBridge.Siege(_orders);
                }
            }
            else if (_role == Role.Engineering)
            {
                if (FleetOrderGate.CanMine(fleet))
                {
                    label = Trans.Get("Mine");
                    act = () => _ = CrewOrderBridge.Mine(_orders);
                }
            }

            if (label == null || act == null)
                return;

            var xi = DiegeticUi.Button(_mount, "Pad_" + label, label, Vector3.zero,
                new Vector3(0.42f, 0.08f, 0.04f), _art, _accent, act);
            _pad = xi.gameObject;
        }
    }
}
