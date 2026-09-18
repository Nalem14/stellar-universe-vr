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

            var interior = new GameObject("BridgeInterior");
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();

            _zoneMap = env.ZoneMap;
            if (_zoneMap != null)
                _zoneMap.Bind(_focus, env.Art);

            _exterior.Bind(_focus);
            _viewRig.Bind(_focus, _exterior, interior.transform);

            var readout = _zoneMap != null ? _zoneMap.Readout : CreateReadout(
                env.Table != null ? env.Table.transform : interior.transform);
            if (readout != null)
                readout.text = Trans.Get("Loading");

            var boot = interior.AddComponent<SessionBoot>();
            boot.BindReadout(readout);
            boot.BindFocus(_focus);
            boot.BindPoller(_poller);
            boot.Run();
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
