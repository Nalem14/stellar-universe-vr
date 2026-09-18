using Core.App;
using Core.Utils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Helm / Tactical / Engineering desks + mannequins + radials (no seat-swap).
    /// </summary>
    public static class CrewStationsBuilder
    {
        public static void Build(CicEnvironment host, CicArtKit art, ViewFleetOrders orders,
            HexBattleController hex)
        {
            BuildStation(host, art, "CrewHelm", new Vector3(-1.6f, 0f, 2.4f), CicArtKit.Cyan,
                orders, hex, CrewRole.Helm);
            BuildStation(host, art, "CrewTactical", new Vector3(0f, 0f, 2.85f), CicArtKit.Amber,
                orders, hex, CrewRole.Tactical);
            BuildStation(host, art, "CrewEngineering", new Vector3(1.6f, 0f, 2.4f),
                new Color(0.4f, 0.9f, 0.55f), orders, hex, CrewRole.Engineering);
        }

        static void BuildStation(CicEnvironment host, CicArtKit art, string name, Vector3 pos,
            Color accent, ViewFleetOrders orders, HexBattleController hex, CrewRole role)
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

            // Seat + mannequin
            host.Box(name + "Seat", pos + new Vector3(0f, 0.5f, -0.35f),
                new Vector3(0.45f, 0.1f, 0.45f), art.DarkPanel(0.1f), keepCollider: true);
            BuildMannequin(host, art, pos + new Vector3(0f, 0.55f, -0.35f), accent);

            // Radial poke buttons facing captain
            var radial = new GameObject(name + "Radial");
            radial.transform.SetParent(root.transform, false);
            radial.transform.localPosition = new Vector3(0f, 1.15f, 0.35f);
            AddRadial(radial.transform, art, "A", new Vector3(-0.2f, 0f, 0f), accent, () =>
            {
                if (role == CrewRole.Helm && hex != null && hex.IsActive)
                    _ = hex.EndTurn();
                else if (role == CrewRole.Helm)
                    _ = orders != null ? Trigger(orders, "Flee") : null;
            });
            AddRadial(radial.transform, art, "B", new Vector3(0.2f, 0f, 0f), accent, () =>
            {
                if (role == CrewRole.Tactical)
                    _ = orders != null ? Trigger(orders, "Siege") : null;
                else if (role == CrewRole.Engineering)
                    _ = orders != null ? Trigger(orders, "Mine") : null;
            });
        }

        static System.Threading.Tasks.Task Trigger(ViewFleetOrders orders, string which)
        {
            // Use public API via reflection-free dedicated methods — poke panel buttons instead.
            // Crew radials call the same ActionJs as panel through thin wrappers:
            return which switch
            {
                "Flee" => CrewOrderBridge.Stance(orders, "RUN_AWAY"),
                "Siege" => CrewOrderBridge.Siege(orders),
                "Mine" => CrewOrderBridge.Mine(orders),
                _ => System.Threading.Tasks.Task.CompletedTask
            };
        }

        static void AddRadial(Transform parent, CicArtKit art, string name, Vector3 local, Color accent,
            System.Action act)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Radial_" + name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * 0.08f;
            go.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(Texture2D.whiteTexture, accent, 2.2f);
            var interact = go.AddComponent<XRSimpleInteractable>();
            interact.selectEntered.AddListener(_ => act());
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

            // Closed helmet — no pale sphere (reads as placeholder in VR).
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

            // Arms on the desk — readable crew silhouette from behind.
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

