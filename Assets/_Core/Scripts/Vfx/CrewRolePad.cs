using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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

            _pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _pad.name = "Pad_" + label;
            _pad.transform.SetParent(_mount, false);
            _pad.transform.localPosition = Vector3.zero;
            _pad.transform.localScale = new Vector3(0.42f, 0.08f, 0.04f);
            _pad.GetComponent<MeshRenderer>().sharedMaterial =
                _art.Lit(Texture2D.whiteTexture, _accent, 2.0f);
            var interact = _pad.AddComponent<XRSimpleInteractable>();
            interact.hoverEntered.AddListener(_ =>
            {
                _pad.transform.localScale = new Vector3(0.46f, 0.09f, 0.045f);
                CicCue.Hover(_pad.transform.position);
            });
            interact.hoverExited.AddListener(_ =>
            {
                _pad.transform.localScale = new Vector3(0.42f, 0.08f, 0.04f);
            });
            interact.selectEntered.AddListener(_ =>
            {
                CicCue.Ok(_pad.transform.position);
                act();
            });

            var tmpGo = new GameObject("T");
            tmpGo.transform.SetParent(_pad.transform, false);
            tmpGo.transform.localPosition = new Vector3(0f, 0f, -0.65f);
            tmpGo.transform.localScale = Vector3.one * 0.03f;
            var tmp = tmpGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 5f;
            tmp.color = Color.white;
            tmp.text = label;
        }
    }
}
