using Core.App;
using Core.Utils;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Helm / Tactical / Engineering desks + mannequins.
    /// Orders: ray-select crew → <see cref="CrewDialogue"/> (Bridge Crew style).
    /// Holomap grab remains the tabletop MoveFleet path.
    /// </summary>
    public static class CrewStationsBuilder
    {
        public static void Build(CicEnvironment host, CicArtKit art,
            HexBattleController hex, HoloZoneMap map, FleetPoller poller, FocusContext focus,
            BridgeSystemLoader loader)
        {
            BuildStation(host, art, "CrewHelm", new Vector3(-1.6f, 0f, 2.4f), CicArtKit.Cyan,
                CrewDialogue.Role.Helm, hex, map, poller, focus, loader);
            BuildStation(host, art, "CrewTactical", new Vector3(0f, 0f, 2.85f), CicArtKit.Amber,
                CrewDialogue.Role.Tactical, hex, map, poller, focus, loader);
            BuildStation(host, art, "CrewEngineering", new Vector3(1.6f, 0f, 2.4f),
                new Color(0.4f, 0.9f, 0.55f), CrewDialogue.Role.Engineering, hex, map, poller,
                focus, loader);
        }

        static void BuildStation(CicEnvironment host, CicArtKit art, string name, Vector3 pos,
            Color accent, CrewDialogue.Role role, HexBattleController hex,
            HoloZoneMap map, FleetPoller poller, FocusContext focus, BridgeSystemLoader loader)
        {
            var root = new GameObject(name);
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = pos;

            host.Box(name + "Desk", pos + new Vector3(0f, 0.95f, 0.1f),
                new Vector3(1.1f, 0.1f, 0.65f), art.MetalPanel(0.12f), keepCollider: true);
            host.Box(name + "Screen", pos + new Vector3(0f, 1.25f, -0.05f),
                new Vector3(0.7f, 0.45f, 0.04f),
                art.Lit(art.ScreenIdle != null ? art.ScreenIdle : Texture2D.whiteTexture,
                    new Color(0.1f, 0.3f, 0.4f), 0.7f), keepCollider: false);
            host.Box(name + "Accent", pos + new Vector3(0f, 1.5f, -0.05f),
                new Vector3(0.7f, 0.03f, 0.03f),
                art.Lit(Texture2D.whiteTexture, accent, 2.5f), keepCollider: false);

            host.Box(name + "Seat", pos + new Vector3(0f, 0.5f, -0.35f),
                new Vector3(0.45f, 0.1f, 0.45f), art.DarkPanel(0.1f), keepCollider: true);
            var mannequin = BuildMannequin(host, art, pos + new Vector3(0f, 0.55f, -0.35f), accent);
            CrewDialogue.Attach(mannequin, host, art, accent, role, hex, map, poller, focus, loader);
        }

        static Transform BuildMannequin(CicEnvironment host, CicArtKit art, Vector3 seatPos, Color accent)
        {
            var root = new GameObject("Mannequin");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = seatPos;

            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform, false);
            torso.transform.localPosition = new Vector3(0f, 0.52f, 0.02f);
            torso.transform.localScale = new Vector3(0.32f, 0.34f, 0.22f);
            CicEnvironment.DropColliderStatic(torso);
            torso.GetComponent<MeshRenderer>().sharedMaterial = art.DarkPanel(0.18f);

            var shoulders = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shoulders.name = "Shoulders";
            shoulders.transform.SetParent(root.transform, false);
            shoulders.transform.localPosition = new Vector3(0f, 0.78f, 0.02f);
            shoulders.transform.localScale = new Vector3(0.42f, 0.08f, 0.16f);
            CicEnvironment.DropColliderStatic(shoulders);
            shoulders.GetComponent<MeshRenderer>().sharedMaterial = art.MetalPanel(0.2f);

            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = "Collar";
            collar.transform.SetParent(root.transform, false);
            collar.transform.localPosition = new Vector3(0f, 0.86f, 0.02f);
            collar.transform.localScale = new Vector3(0.16f, 0.04f, 0.16f);
            CicEnvironment.DropColliderStatic(collar);
            collar.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(Texture2D.whiteTexture, accent, 1.8f);

            var helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            helmet.name = "Helmet";
            helmet.transform.SetParent(root.transform, false);
            helmet.transform.localPosition = new Vector3(0f, 0.98f, 0.03f);
            helmet.transform.localScale = new Vector3(0.2f, 0.22f, 0.22f);
            CicEnvironment.DropColliderStatic(helmet);
            helmet.GetComponent<MeshRenderer>().sharedMaterial = art.MetalPanel(0.08f);

            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            visor.transform.SetParent(root.transform, false);
            visor.transform.localPosition = new Vector3(0f, 0.98f, 0.12f);
            visor.transform.localScale = new Vector3(0.16f, 0.06f, 0.03f);
            CicEnvironment.DropColliderStatic(visor);
            visor.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(Texture2D.whiteTexture, accent, 3.2f);

            Arm(root.transform, art, new Vector3(-0.22f, 0.62f, 0.18f), -18f);
            Arm(root.transform, art, new Vector3(0.22f, 0.62f, 0.18f), 18f);

            var bob = root.AddComponent<HoloSpin>();
            bob.DegreesPerSecond = 0f;
            bob.BobMeters = 0.006f;
            return root.transform;
        }

        static void Arm(Transform parent, CicArtKit art, Vector3 localPos, float yaw)
        {
            var arm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            arm.name = "Arm";
            arm.transform.SetParent(parent, false);
            arm.transform.localPosition = localPos;
            arm.transform.localRotation = Quaternion.Euler(75f, yaw, 0f);
            arm.transform.localScale = new Vector3(0.07f, 0.16f, 0.07f);
            CicEnvironment.DropColliderStatic(arm);
            arm.GetComponent<MeshRenderer>().sharedMaterial = art.DarkPanel(0.2f);
        }
    }
}
