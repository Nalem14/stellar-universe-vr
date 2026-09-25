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
            _focus.Clear(); // register as FocusContext.Current

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
            orders.BindConsole(Core.Holo.OrderConsole.Build(interior.transform));
            Core.Holo.QueuePathView.Attach(_zoneMap, _focus, env.Art);

            Core.Crew.BarkDirector.Build(interior.transform, _focus);
            var economy = EconomyService.Ensure(interior.transform);
            Core.Stations.OpsConsole.Build(interior.transform, economy);
            CrewStationsBuilder.Build(env, env.Art, hex, _zoneMap, _poller, _focus, _loader);
            BridgeViewscreen.Build(env, _focus);

            var teleporter = BridgeViewTeleporter.Build(env, env.Art);
            teleporter.Bind(_loader, _focus, env.Art);

            // Left arm console: refresh the ship list of the view teleporter (poke from the seat).
            var armL = FindNamed(interior.transform, "ArmPadL");
            if (armL != null)
            {
                Core.UI.PokeButton.Create(armL, "ArmPadL_Refresh", Trans.Get("fleets"),
                    ArmPadTop, ArmPadFaceUp, new Vector2(0.13f, 0.06f), CicArtKit.Cyan,
                    () => Core.Utils.AsyncTap.Run(teleporter.RefreshList()));
            }

            // Command mode scales a parent of HoloMapMount so zoom (child localScale) stays independent.
            var seat = FindNamed(interior.transform, "CaptainSeat");
            var arm = FindNamed(interior.transform, "ArmPadR");
            var cmd = interior.AddComponent<CaptainCommandMode>();
            Transform holoCmdRoot = null;
            if (_zoneMap != null)
            {
                var parent = _zoneMap.transform.parent;
                var rootGo = new GameObject("HoloCmdRoot");
                holoCmdRoot = rootGo.transform;
                holoCmdRoot.SetParent(parent, false);
                holoCmdRoot.localPosition = _zoneMap.transform.localPosition;
                holoCmdRoot.localRotation = _zoneMap.transform.localRotation;
                holoCmdRoot.localScale = Vector3.one;
                _zoneMap.transform.SetParent(holoCmdRoot, false);
                _zoneMap.transform.localPosition = Vector3.zero;
                _zoneMap.transform.localRotation = Quaternion.identity;
            }
            else if (env.Table != null)
            {
                holoCmdRoot = env.Table.transform;
            }

            cmd.Bind(holoCmdRoot, seat, arm);

            BridgeDressing.Apply(env, _focus);
            if (_focus != null)
            {
                _focus.Changed += () => BridgeDressing.Apply(env, _focus);
            }

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

        /// <summary>Top face of the 0.03 m arm console, a hair above it, slightly toward the knee.</summary>
        internal static readonly Vector3 ArmPadTop = new(0f, 0.017f, 0.04f);

        /// <summary>Kit front is -Z; +90° about X turns it to face up (+Y), text reading from the seat.</summary>
        internal static readonly Quaternion ArmPadFaceUp = Quaternion.Euler(90f, 0f, 0f);

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
