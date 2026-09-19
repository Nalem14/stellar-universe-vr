using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Sole MoveFleet order console — rail on the table rim facing the captain.
    /// </summary>
    public static class CaptainOrdersRail
    {
        public static void Build(CicEnvironment host, CicArtKit art, HoloZoneMap map, FleetPoller poller)
        {
            if (host == null || art == null)
                return;

            var rail = new GameObject("CaptainOrdersRail");
            rail.transform.SetParent(host.transform, false);
            rail.transform.localPosition = new Vector3(0.55f, 1.05f, 0.35f);
            rail.transform.localRotation = Quaternion.Euler(18f, -25f, 0f);

            DiegeticUi.Panel(rail.transform, "OrdersPanel", Vector3.zero,
                new Vector3(0.55f, 0.72f, 0.05f), art, out _);

            var title = DiegeticUi.Label(rail.transform, "OrdersTitle", Trans.Get("CommandBridge"),
                new Vector3(0f, 0.3f, -0.04f), 0.03f, 6f, CicArtKit.Cyan);
            title.rectTransform.sizeDelta = new Vector2(24f, 6f);

            var list = new GameObject("OrdersList").transform;
            list.SetParent(rail.transform, false);
            list.localPosition = new Vector3(0f, 0.05f, -0.04f);

            var console = rail.AddComponent<CrewHelmConsole>();
            console.Bind(FocusContext.Current, art, map, poller, list, title);

            host.KeyLight("OrdersLamp", new Vector3(0.55f, 1.45f, 0.35f), CicArtKit.Cyan, 0.55f, 2.2f);
        }
    }
}
