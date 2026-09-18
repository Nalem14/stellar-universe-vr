using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Feasible orders for the inhabited ship — rail on the table rim facing the captain.
    /// </summary>
    public static class CaptainOrdersRail
    {
        public static void Build(CicEnvironment host, CicArtKit art, HoloZoneMap map, FleetPoller poller)
        {
            if (host == null || art == null)
                return;

            var rail = new GameObject("CaptainOrdersRail");
            rail.transform.SetParent(host.transform, false);
            // Between captain seat (~z=-0.9) and table (~z=1.15) — arm's reach.
            rail.transform.localPosition = new Vector3(0.55f, 1.05f, 0.35f);
            rail.transform.localRotation = Quaternion.Euler(18f, -25f, 0f);

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "OrdersBoard";
            board.transform.SetParent(rail.transform, false);
            board.transform.localPosition = Vector3.zero;
            board.transform.localScale = new Vector3(0.55f, 0.72f, 0.05f);
            CicEnvironment.DropColliderStatic(board);
            board.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(art.Panel != null ? art.Panel : Texture2D.whiteTexture,
                    new Color(0.12f, 0.18f, 0.24f), 0.9f);

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(rail.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.028f);
            frame.transform.localScale = new Vector3(0.58f, 0.75f, 0.02f);
            CicEnvironment.DropColliderStatic(frame);
            frame.GetComponent<MeshRenderer>().sharedMaterial = art.CyanEmit(2.2f);

            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glass.name = "Glass";
            glass.transform.SetParent(rail.transform, false);
            glass.transform.localPosition = new Vector3(0f, 0f, -0.03f);
            glass.transform.localScale = new Vector3(0.5f, 0.66f, 1f);
            CicEnvironment.DropColliderStatic(glass);
            glass.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(art.ScreenIdle != null ? art.ScreenIdle : Texture2D.whiteTexture,
                    new Color(0.05f, 0.2f, 0.28f), 1.5f);

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
