using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.App
{
    public class BridgeDirector : MonoBehaviour
    {
        FocusContext _focus;
        SystemExterior _exterior;
        BridgeViewRig _viewRig;
        FleetPoller _poller;
        HoloZoneMap _zoneMap;
        BridgeSystemLoader _loader;

        async void Awake()
        {
            AuthManager.Ensure();
            await Trans.EnsureLoaded();
        }

        void Start()
        {
            _focus = new FocusContext();

            var world = new GameObject("SystemWorld");
            world.transform.position = Vector3.zero;

            var exteriorGo = new GameObject("Exterior");
            exteriorGo.transform.SetParent(world.transform, false);
            _exterior = exteriorGo.AddComponent<SystemExterior>();

            _viewRig = world.AddComponent<BridgeViewRig>();
            _poller = world.AddComponent<FleetPoller>();
            _loader = world.AddComponent<BridgeSystemLoader>();

            var interior = new GameObject("BridgeInterior");
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();

            _zoneMap = env.ZoneMap;
            if (_zoneMap != null)
                _zoneMap.Bind(_focus, env.Art);

            _exterior.Bind(_focus);
            _viewRig.Bind(_focus, _exterior, interior.transform);
            _loader.Bind(_focus, _exterior, _viewRig, _poller, _zoneMap);

            var mapCtrl = interior.AddComponent<HoloMapController>();
            mapCtrl.Bind(_zoneMap, _focus, _poller);

            var hex = interior.AddComponent<HexBattleController>();
            var tableMount = _zoneMap != null ? _zoneMap.transform : interior.transform;
            hex.Bind(_focus, mapCtrl, tableMount, env.Art);
            mapCtrl.BindHex(hex);

            var orders = interior.AddComponent<HoloFleetOrders>();
            orders.Bind(_zoneMap, _focus, _poller, mapCtrl);

            var viewOrders = interior.AddComponent<ViewFleetOrders>();
            viewOrders.Bind(_focus, _poller, _zoneMap, env.Art,
                env.Table != null ? env.Table.transform : interior.transform, hex);

            CrewStationsBuilder.Build(env, env.Art, viewOrders, hex);

            var teleporter = BridgeViewTeleporter.Build(env, env.Art);
            teleporter.Bind(_loader, _focus, env.Art);

            // Command mode: find seat + arm pad + table
            var seat = FindNamed(interior.transform, "CaptainSeat");
            var arm = FindNamed(interior.transform, "ArmPadR");
            var cmd = interior.AddComponent<CaptainCommandMode>();
            cmd.Bind(env.Table != null ? env.Table.transform : null, seat, arm);

            // Shortcut TP on left arm pad
            var armL = FindNamed(interior.transform, "ArmPadL");
            if (armL != null)
            {
                var interact = armL.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
                interact.selectEntered.AddListener(_ => _ = teleporter.RefreshList());
            }

            AlcoveSystems.Wire(env, _focus, _poller, hex);

            var readout = _zoneMap != null ? _zoneMap.Readout : CreateReadout(
                env.Table != null ? env.Table.transform : interior.transform);
            if (readout != null)
                readout.text = Trans.Get("Loading");

            var boot = interior.AddComponent<SessionBoot>();
            boot.BindReadout(readout);
            boot.BindFocus(_focus);
            boot.BindPoller(_poller);
            boot.BindLoader(_loader);
            boot.Run();
        }

        static Transform FindNamed(Transform root, string name)
        {
            if (root == null)
                return null;
            if (root.name == name)
                return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamed(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        static TMP_Text CreateReadout(Transform parent)
        {
            var go = new GameObject("HoloReadout");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.012f;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 8f;
            tmp.color = new Color(0.55f, 0.95f, 1f, 0.9f);
            tmp.text = string.Empty;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
