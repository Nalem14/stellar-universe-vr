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
    /// Panel + API orders for the inhabited ship (ViewFleetId). Params sourced from action-api.json.
    /// </summary>
    public class ViewFleetOrders : MonoBehaviour
    {
        FocusContext _focus;
        FleetPoller _poller;
        HoloZoneMap _map;
        HexBattleController _hex;
        Transform _panel;
        TMP_Text _label;
        CicArtKit _art;
        bool _combatRemap;

        public void Bind(FocusContext focus, FleetPoller poller, HoloZoneMap map, CicArtKit art,
            Transform mount, HexBattleController hex = null)
        {
            _focus = focus;
            _poller = poller;
            _map = map;
            _art = art;
            _hex = hex;
            BuildPanel(mount);
        }

        public void SetCombatRemap(bool on) => _combatRemap = on;

        void BuildPanel(Transform mount)
        {
            if (mount == null || _panel != null)
                return;
            _panel = new GameObject("ViewFleetPanel").transform;
            _panel.SetParent(mount, false);
            _panel.localPosition = new Vector3(0.55f, 0.35f, 0.1f);
            _panel.localRotation = Quaternion.Euler(25f, -20f, 0f);

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            board.transform.SetParent(_panel, false);
            board.transform.localScale = new Vector3(0.35f, 0.45f, 0.03f);
            if (_art != null)
                board.GetComponent<MeshRenderer>().sharedMaterial = _art.DarkPanel(0.1f);

            var tmpGo = new GameObject("Label");
            tmpGo.transform.SetParent(_panel, false);
            tmpGo.transform.localPosition = new Vector3(0f, 0.18f, -0.02f);
            tmpGo.transform.localScale = Vector3.one * 0.006f;
            _label = tmpGo.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 7f;
            _label.color = new Color(0.6f, 0.95f, 1f);
            _label.text = string.Empty;

            AddButton("Flee", new Vector3(0f, 0.08f, -0.02f),
                () => _ = Stance("RUN_AWAY"));
            AddButton("Defend", new Vector3(0f, 0.0f, -0.02f),
                () => _ = Stance("ATTACK_ATTACKER"));
            AddButton("Attack", new Vector3(0f, -0.08f, -0.02f),
                () => _ = Stance("ATTACK_PLANET"));
            AddButton("Mine", new Vector3(0f, -0.16f, -0.02f),
                () => _ = Mine());
            AddButton("Siege", new Vector3(0f, -0.24f, -0.02f),
                () => _ = Siege());
            AddButton("Explore", new Vector3(0f, -0.32f, -0.02f),
                () => _ = Explore());
            AddButton("Deposit", new Vector3(-0.12f, -0.40f, -0.02f),
                () => _ = Cargo(true));
            AddButton("Withdraw", new Vector3(0.12f, -0.40f, -0.02f),
                () => _ = Cargo(false));
            AddButton("EndTurn", new Vector3(0f, 0.16f, -0.02f),
                () => _ = EndTurnCombat());
        }

        void AddButton(string name, Vector3 local, System.Action act)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Btn_" + name;
            go.transform.SetParent(_panel, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(0.28f, 0.06f, 0.02f);
            if (_art != null)
                go.GetComponent<MeshRenderer>().sharedMaterial =
                    _art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan * 0.5f, 1.4f);
            var interact = go.AddComponent<XRSimpleInteractable>();
            interact.selectEntered.AddListener(_ => act());

            var t = new GameObject("T");
            t.transform.SetParent(go.transform, false);
            t.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            t.transform.localScale = Vector3.one * 0.025f;
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 5f;
            tmp.color = Color.white;
            tmp.text = name;
        }

        void LateUpdate()
        {
            if (_label == null || _focus == null)
                return;
            var fleet = _focus.FindViewFleet();
            if (fleet == null)
            {
                _label.text = _focus.ViewPlanetId > 0 ? "station" : "—";
                return;
            }

            _label.text = string.IsNullOrEmpty(fleet.Name) ? "ship " + fleet.Id : fleet.Name;
            if (fleet.IsInBattle)
                _label.text += " · battle";
        }

        async Task Stance(string position)
        {
            var fleet = _focus?.FindViewFleet();
            if (fleet == null)
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
            if (fleet == null || fleet.AsteroidId <= 0)
            {
                Say("asteroid");
                return;
            }

            await Call("HarvestAsteroid", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "asteroid", fleet.AsteroidId.ToString() }
            });
        }

        async Task Siege()
        {
            var fleet = _focus?.FindViewFleet();
            if (fleet == null || fleet.PlanetId <= 0)
            {
                Say("planet");
                return;
            }

            await Call("FleetAttackPlanet", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "planet", fleet.PlanetId.ToString() }
            });
        }

        async Task Explore()
        {
            var fleet = _focus?.FindViewFleet();
            if (fleet == null || fleet.PlanetId <= 0)
            {
                Say("planet");
                return;
            }

            await Call("ExplorePlanet", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "planet", fleet.PlanetId.ToString() }
            });
        }

        async Task Cargo(bool deposit)
        {
            var fleet = _focus?.FindViewFleet();
            if (fleet == null || fleet.PlanetId <= 0)
            {
                Say("planet");
                return;
            }

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
        }

        async Task Call(string action, Dictionary<string, string> query)
        {
            Say(Trans.Get("Loading"));
            var result = await ActionJs.Get(action, query);
            Say(result.Ok ? action : (result.Error ?? action));
            if (result.Ok && _poller != null)
                await _poller.PollNow();
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
