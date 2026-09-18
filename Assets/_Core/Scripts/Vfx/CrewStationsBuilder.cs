using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Helm / Tactical / Engineering desks + mannequins.
    /// Helm hosts the context order board for the inhabited ship.
    /// </summary>
    public static class CrewStationsBuilder
    {
        public static void Build(CicEnvironment host, CicArtKit art, ViewFleetOrders orders,
            HexBattleController hex, HoloZoneMap map = null, FleetPoller poller = null)
        {
            BuildStation(host, art, "CrewHelm", new Vector3(-1.6f, 0f, 2.4f), CicArtKit.Cyan,
                CrewRole.Helm, orders, hex, map, poller);
            BuildStation(host, art, "CrewTactical", new Vector3(0f, 0f, 2.85f), CicArtKit.Amber,
                CrewRole.Tactical, orders, hex, map, poller);
            BuildStation(host, art, "CrewEngineering", new Vector3(1.6f, 0f, 2.4f),
                new Color(0.4f, 0.9f, 0.55f), CrewRole.Engineering, orders, hex, map, poller);
        }

        static void BuildStation(CicEnvironment host, CicArtKit art, string name, Vector3 pos,
            Color accent, CrewRole role, ViewFleetOrders orders, HexBattleController hex,
            HoloZoneMap map, FleetPoller poller)
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
            BuildMannequin(host, art, pos + new Vector3(0f, 0.55f, -0.35f), accent);

            if (role == CrewRole.Helm)
            {
                BuildHelmBoard(root.transform, art, accent, map, poller);
                return;
            }

            // Tactical / Engineering — pads appear only when the action is feasible.
            var radial = new GameObject(name + "Radial");
            radial.transform.SetParent(root.transform, false);
            radial.transform.localPosition = new Vector3(0f, 1.15f, 0.35f);
            var pad = radial.AddComponent<CrewRolePad>();
            if (role == CrewRole.Tactical)
                pad.Bind(FocusContext.Current, art, accent, CrewRolePad.Role.Tactical, radial.transform,
                    orders, hex);
            else if (role == CrewRole.Engineering)
                pad.Bind(FocusContext.Current, art, accent, CrewRolePad.Role.Engineering, radial.transform,
                    orders, hex);
        }

        static void BuildHelmBoard(Transform station, CicArtKit art, Color accent, HoloZoneMap map,
            FleetPoller poller)
        {
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "HelmBoard";
            board.transform.SetParent(station, false);
            board.transform.localPosition = new Vector3(0f, 1.2f, 0.42f);
            board.transform.localRotation = Quaternion.Euler(12f, 180f, 0f);
            board.transform.localScale = new Vector3(0.85f, 0.7f, 0.04f);
            CicEnvironment.DropColliderStatic(board);
            board.GetComponent<MeshRenderer>().sharedMaterial = art.DarkPanel(0.12f);

            var titleGo = new GameObject("HelmTitle");
            titleGo.transform.SetParent(board.transform, false);
            titleGo.transform.localPosition = new Vector3(0f, 0.38f, -0.6f);
            titleGo.transform.localScale = new Vector3(0.03f / 0.85f, 0.03f / 0.7f, 0.03f);
            var title = titleGo.AddComponent<TextMeshPro>();
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 6f;
            title.color = accent;
            title.text = Trans.Get("CommandBridge");

            var list = new GameObject("HelmList").transform;
            list.SetParent(board.transform, false);
            list.localPosition = new Vector3(0f, 0.12f, -0.55f);
            // Un-scale so row cubes keep readable world size.
            list.localScale = new Vector3(1f / 0.85f, 1f / 0.7f, 1f);

            var console = station.gameObject.AddComponent<CrewHelmConsole>();
            console.Bind(FocusContext.Current, art, map, poller, list, title);
        }

        static void BuildMannequin(CicEnvironment host, CicArtKit art, Vector3 seatPos, Color accent)
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

        enum CrewRole
        {
            Helm,
            Tactical,
            Engineering
        }
    }

    /// <summary>Thin bridge so crew radials share ViewFleetOrders without making all methods public.</summary>
    public static class CrewOrderBridge
    {
        public static async System.Threading.Tasks.Task Stance(ViewFleetOrders orders, string pos)
        {
            if (orders == null)
                return;
            var focus = FocusContext.Current;
            var fleet = focus?.FindViewFleet();
            if (fleet == null)
                return;
            await Core.Utils.ActionJs.Get("UpdateFleetDefendPosition", new System.Collections.Generic.Dictionary<string, string>
            {
                { "id", fleet.Id.ToString() },
                { "position", pos }
            });
        }

        public static async System.Threading.Tasks.Task Siege(ViewFleetOrders orders)
        {
            var focus = FocusContext.Current;
            var fleet = focus?.FindViewFleet();
            if (fleet == null || fleet.PlanetId <= 0)
                return;
            await Core.Utils.ActionJs.Get("FleetAttackPlanet", new System.Collections.Generic.Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "planet", fleet.PlanetId.ToString() }
            });
        }

        public static async System.Threading.Tasks.Task Mine(ViewFleetOrders orders)
        {
            var focus = FocusContext.Current;
            var fleet = focus?.FindViewFleet();
            if (fleet == null || fleet.AsteroidId <= 0)
                return;
            await Core.Utils.ActionJs.Get("HarvestAsteroid", new System.Collections.Generic.Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "asteroid", fleet.AsteroidId.ToString() }
            });
        }
    }
}
