using System;
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
    /// <summary>Wires diegetic alcoves: planet / yard / research / comms / galaxy colonize helpers.</summary>
    public static class AlcoveSystems
    {
        public static void Wire(CicEnvironment host, FocusContext focus, FleetPoller poller,
            HexBattleController hex)
        {
            var art = host.Art;
            WirePlanet(host, art, focus, poller);
            WireShipyard(host, art, focus);
            WireResearch(host, art, focus);
            WireComms(host, art);
            WireDiplomacy(host, art);
            WireIntendance(host, art);
            WireStargate(host, art, focus);
            WireOrderQueue(host, art, focus, poller);
            WireGalaxyColonize(host, art, focus, poller, hex);
            WirePassthroughStub(host, art);
            BridgeDressing.Apply(host, focus);
        }

        static void WireDiplomacy(CicEnvironment host, CicArtKit art)
        {
            var half = WorldScale.CicDeck * 0.5f;
            var root = new GameObject("SalonDiplomatie");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, -half + 0.8f);
            host.Box("DiploTable", new Vector3(0f, 0.85f, -half + 0.8f),
                new Vector3(1.4f, 0.08f, 0.9f), art.MetalPanel(0.12f), keepCollider: true);
            host.Box("DiploAccent", new Vector3(0f, 0.9f, -half + 0.45f),
                new Vector3(1.2f, 0.03f, 0.04f), art.AmberEmit(2.2f), keepCollider: false);
            AddPoke(root.transform, art, "Wars", new Vector3(-0.25f, 1.0f, 0.2f), async () =>
            {
                await ActionJs.Get("GetMyWars");
            });
            AddPoke(root.transform, art, "Alliance", new Vector3(0.25f, 1.0f, 0.2f), async () =>
            {
                await ActionJs.Get("GetMyAlliance");
            });
            AddPoke(root.transform, art, "Empires", new Vector3(0f, 1.0f, 0.35f), async () =>
            {
                await ActionJs.Get("GetEmpires");
            });
        }

        static void WireIntendance(CicEnvironment host, CicArtKit art)
        {
            var half = WorldScale.CicDeck * 0.5f;
            var root = new GameObject("Intendance");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = new Vector3(half - 1.2f, 0f, -1.2f);
            host.Box("ShopVitrine", new Vector3(half - 1.2f, 1.3f, -1.2f),
                new Vector3(0.08f, 1.0f, 1.2f),
                art.Lit(art.ScreenIdle != null ? art.ScreenIdle : Texture2D.whiteTexture,
                    new Color(0.2f, 0.15f, 0.35f), 0.8f), keepCollider: false);
            AddPoke(root.transform, art, "Shop", new Vector3(0f, 1.0f, 0.4f), async () =>
            {
                await ActionJs.Get("GetShopData");
            });
            AddPoke(root.transform, art, "Goals", new Vector3(0.2f, 1.0f, 0.4f), async () =>
            {
                await ActionJs.Get("GetDailyObjectives");
            });
        }

        static void WireStargate(CicEnvironment host, CicArtKit art, FocusContext focus)
        {
            var half = WorldScale.CicDeck * 0.5f;
            var root = new GameObject("BaieStargate");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = new Vector3(half - 1.5f, 0f, half - 3.5f);
            host.Cylinder("SgVortex", new Vector3(half - 1.5f, 1.4f, half - 3.2f),
                new Vector3(0.55f, 0.08f, 0.55f),
                art.Holo(Texture2D.whiteTexture, new Color(0.4f, 0.2f, 1f, 0.55f)), keepCollider: false);
            host.Box("SgDial", new Vector3(half - 1.5f, 0.95f, half - 3.7f),
                new Vector3(0.8f, 0.1f, 0.5f), art.DarkPanel(0.1f), keepCollider: true);
            AddPoke(root.transform, art, "Addresses", new Vector3(0f, 1.05f, 0f), async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("GetKnownAddresses", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() }
                });
            });
            AddPoke(root.transform, art, "Jumpgate", new Vector3(0.25f, 1.05f, 0f), async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("GetJumpgateDestinations", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() }
                });
            });
        }

        static void WireOrderQueue(CicEnvironment host, CicArtKit art, FocusContext focus, FleetPoller poller)
        {
            if (host.Table == null)
                return;
            var root = new GameObject("OrderQueuePearls");
            root.transform.SetParent(host.Table.transform, false);
            root.transform.localPosition = new Vector3(-0.45f, 0.08f, -0.25f);
            for (var i = 0; i < 3; i++)
            {
                var pearl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pearl.name = "Pearl_" + i;
                pearl.transform.SetParent(root.transform, false);
                pearl.transform.localPosition = new Vector3(i * 0.08f, 0f, 0f);
                pearl.transform.localScale = Vector3.one * 0.045f;
                pearl.GetComponent<MeshRenderer>().sharedMaterial =
                    art.Holo(Texture2D.whiteTexture, new Color(0.3f, 1f, 0.85f, 0.8f));
            }

            AddPoke(root.transform, art, "OrderQueue", new Vector3(0.12f, 0.08f, 0.1f), async () =>
            {
                var fleet = focus?.FindViewFleet();
                if (fleet == null)
                    return;
                await ActionJs.Get("ClearFleetOrderQueue", new Dictionary<string, string>
                {
                    { "fleet", fleet.Id.ToString() }
                });
                if (fleet.PlanetId > 0)
                {
                    await ActionJs.Get("AddFleetOrderStep", new Dictionary<string, string>
                    {
                        { "fleet", fleet.Id.ToString() },
                        { "step", "{\"type\":\"explorePlanet\",\"targetId\":" + fleet.PlanetId + "}" }
                    });
                }

                if (poller != null)
                    await poller.PollNow();
            });
        }

        static void WirePlanet(CicEnvironment host, CicArtKit art, FocusContext focus, FleetPoller poller)
        {
            var alcove = host.transform.Find("AlcovePlanet");
            if (alcove == null)
                return;
            var go = new GameObject("PlanetAlcoveUI");
            go.transform.SetParent(alcove, false);
            go.transform.localPosition = new Vector3(0f, 1.2f, 0.4f);
            AddPoke(go.transform, art, "Upgrade", new Vector3(0f, 0f, 0f), async () =>
            {
                var planet = PickPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("GetResource", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() },
                    { "raw", "1" }
                });
                await ActionJs.Get("UpgradeBuilding", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() },
                    { "buildingtype", "metalMine" }
                });
                if (poller != null)
                    await poller.PollNow();
            });
            AddPoke(go.transform, art, "Troops", new Vector3(0.2f, 0f, 0f), async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("RecruitTroop", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() },
                    { "type", "Infantry" },
                    { "qty", "1" }
                });
            });
            AddPoke(go.transform, art, "Defense", new Vector3(0.4f, 0f, 0f), async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("BuildDefenseUnit", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() },
                    { "type", "MissileTurret" },
                    { "qty", "1" }
                });
            });
            AddPoke(go.transform, art, "Decide", new Vector3(0.2f, 0.12f, 0f), async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("GetPlanetDecisions", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() }
                });
            });
        }

        static void WireShipyard(CicEnvironment host, CicArtKit art, FocusContext focus)
        {
            var alcove = host.transform.Find("AlcoveShipyard");
            if (alcove == null)
                return;
            var go = new GameObject("YardUI");
            go.transform.SetParent(alcove, false);
            go.transform.localPosition = new Vector3(0f, 1.15f, 0.4f);
            AddPoke(go.transform, art, "AddCore", Vector3.zero, async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("AddShip", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() },
                    { "type", "ShipCore" }
                });
            });
            AddPoke(go.transform, art, "Layout", new Vector3(0.2f, 0f, 0f), async () =>
            {
                var fleet = focus?.FindViewFleet();
                if (fleet == null)
                    return;
                await ActionJs.Get("GetShipLayout", new Dictionary<string, string>
                {
                    { "fleet", fleet.Id.ToString() }
                });
            });
            AddPoke(go.transform, art, "ShipQueue", new Vector3(0.4f, 0f, 0f), async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("CheckShipQueue", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() }
                });
            });

            // 9×9 module affordance grid — PlaceShipModule needs hangar ship id from queue/layout.
            var grid = new GameObject("YardGrid9x9");
            grid.transform.SetParent(alcove, false);
            grid.transform.localPosition = new Vector3(0f, 1.05f, 0.55f);
            for (var y = 0; y < 9; y++)
            for (var x = 0; x < 9; x++)
            {
                var cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cell.name = $"Cell_{x}_{y}";
                cell.transform.SetParent(grid.transform, false);
                cell.transform.localPosition = new Vector3((x - 4) * 0.045f, (y - 4) * 0.045f, 0f);
                cell.transform.localScale = new Vector3(0.038f, 0.038f, 0.01f);
                var core = x == 4 && y == 4;
                cell.GetComponent<MeshRenderer>().sharedMaterial = core
                    ? art.AmberEmit(2.4f)
                    : art.Holo(Texture2D.whiteTexture, new Color(0.2f, 0.7f, 0.9f, 0.35f));
                var gx = x;
                var gy = y;
                var interact = cell.AddComponent<XRSimpleInteractable>();
                interact.selectEntered.AddListener(_ => Core.Utils.AsyncTap.Run(PlaceAt(focus, gx, gy)));
            }
        }

        static async Task PlaceAt(FocusContext focus, int gx, int gy)
        {
            var fleet = focus?.FindViewFleet();
            if (fleet == null)
                return;
            // Hangar module id: use first non-core module slot id when present; else skip.
            var shipId = 0;
            foreach (var m in fleet.Modules)
            {
                if (m.Id > 0 && (m.Type == null || m.Type.IndexOf("Core", System.StringComparison.OrdinalIgnoreCase) < 0))
                {
                    shipId = m.Id;
                    break;
                }
            }

            if (shipId <= 0)
                return;
            await ActionJs.Get("PlaceShipModule", new Dictionary<string, string>
            {
                { "ship", shipId.ToString() },
                { "fleet", fleet.Id.ToString() },
                { "gx", gx.ToString() },
                { "gy", gy.ToString() }
            });
        }

        static void WireResearch(CicEnvironment host, CicArtKit art, FocusContext focus)
        {
            // New lab prop near astrometry
            var half = WorldScale.CicDeck * 0.5f;
            var root = new GameObject("AlcoveResearch");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = new Vector3(-4.0f, 0f, half - 4.2f);
            host.Box("ResearchDesk", new Vector3(-4.0f, 0.95f, half - 4.2f),
                new Vector3(1.2f, 0.1f, 0.7f), art.MetalPanel(0.12f), keepCollider: true);
            host.Box("ResearchTree", new Vector3(-4.0f, 1.5f, half - 4.35f),
                new Vector3(0.08f, 0.9f, 0.08f), art.CyanEmit(2.2f), keepCollider: false);
            AddPoke(root.transform, art, "Research", new Vector3(0f, 1.2f, 0.35f), async () =>
            {
                var planet = PickOwnedPlanet(focus);
                if (planet == null)
                    return;
                await ActionJs.Get("ImproveResearch", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() },
                    { "research", "combustion" }
                });
            });
            AddPoke(root.transform, art, "Explore", new Vector3(0.2f, 1.2f, 0.35f), async () =>
            {
                var fleet = focus?.FindViewFleet();
                var planet = PickPlanet(focus);
                if (fleet == null || planet == null)
                    return;
                await ActionJs.Get("ExplorePlanet", new Dictionary<string, string>
                {
                    { "fleet", fleet.Id.ToString() },
                    { "planet", planet.Id.ToString() }
                });
            });
        }

        static void WireComms(CicEnvironment host, CicArtKit art)
        {
            var screen = host.transform.Find("CommsScreen");
            var parent = screen != null ? screen.parent : host.transform;
            var go = new GameObject("CommsLCARS");
            go.transform.SetParent(host.transform, false);
            var half = WorldScale.CicDeck * 0.5f;
            go.transform.localPosition = new Vector3(half - 0.55f, 1.55f, -2.8f);

            var tmpGo = new GameObject("ChatFeed");
            tmpGo.transform.SetParent(go.transform, false);
            tmpGo.transform.localPosition = new Vector3(0f, 0f, 0f);
            tmpGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            tmpGo.transform.localScale = Vector3.one * 0.008f;
            var tmp = tmpGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.fontSize = 6f;
            tmp.color = new Color(0.4f, 0.95f, 0.7f);
            tmp.text = Trans.Get("chatPanel");
            tmp.rectTransform.sizeDelta = new Vector2(120f, 80f);

            AddPoke(go.transform, art, "Chat", new Vector3(0f, -0.5f, 0f), async () =>
            {
                var chat = await ActionJs.Get("GetChat");
                if (chat.Ok)
                    tmp.text = TrimBody(chat.Body, 180);
                else
                    tmp.text = chat.Error;
            });
            AddPoke(go.transform, art, "Mail", new Vector3(0.15f, -0.5f, 0f), async () =>
            {
                var mail = await ActionJs.Get("GetMails");
                tmp.text = mail.Ok ? TrimBody(mail.Body, 180) : mail.Error;
            });
        }

        static void WireGalaxyColonize(CicEnvironment host, CicArtKit art, FocusContext focus,
            FleetPoller poller, HexBattleController hex)
        {
            // Table-side controls for galaxy / colonize / make battle
            if (host.Table == null)
                return;
            var root = new GameObject("TableOrdersExtra");
            root.transform.SetParent(host.Table.transform, false);
            root.transform.localPosition = new Vector3(0.55f, 0.05f, -0.35f);
            AddPoke(root.transform, art, "Colonize", Vector3.zero, async () =>
            {
                var fleet = focus?.FindViewFleet();
                var planet = PickPlanet(focus);
                if (fleet == null || planet == null)
                    return;
                // ColonyShip id unknown without hangar parse — attempt with fleet id as ship when API allows.
                var ships = fleet.Modules;
                var shipId = 0;
                foreach (var m in ships)
                {
                    if (m.Type != null && m.Type.IndexOf("Colony", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        shipId = m.Id;
                        break;
                    }
                }

                if (shipId <= 0 && ships.Count > 0)
                    shipId = ships[0].Id;
                if (shipId <= 0)
                    return;
                await ActionJs.Get("Colonize", new Dictionary<string, string>
                {
                    { "ship", shipId.ToString() },
                    { "planet", planet.Id.ToString() }
                });
                if (poller != null)
                    await poller.PollNow();
            });
            AddPoke(root.transform, art, "Battle", new Vector3(0.2f, 0f, 0f), async () =>
            {
                if (hex == null || focus == null)
                    return;
                var ids = new List<int>();
                var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var f in focus.Fleets)
                {
                    if (owned > 0 && f.IsOwnedBy(owned) && f.VisibleIn(focus.SystemId, now))
                        ids.Add(f.Id);
                }

                if (ids.Count == 0 && focus.ViewFleetId > 0)
                    ids.Add(focus.ViewFleetId);
                await hex.MakeBattle(ids);
            });
        }

        static void WirePassthroughStub(CicEnvironment host, CicArtKit art)
        {
            // Quart mode stub marker — full MR later
            var go = new GameObject("PassthroughQuartStub");
            go.transform.SetParent(host.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, -4.5f);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "QuartPad";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localScale = new Vector3(0.6f, 0.02f, 0.6f);
            marker.GetComponent<MeshRenderer>().sharedMaterial =
                art.Holo(Texture2D.whiteTexture, new Color(0.6f, 0.4f, 1f, 0.35f));
        }

        static FocusPlanet PickPlanet(FocusContext focus)
        {
            if (focus == null)
                return null;
            if (focus.ViewPlanetId > 0)
            {
                var p = focus.FindPlanet(focus.ViewPlanetId);
                if (p != null)
                    return p;
            }

            return PickOwnedPlanet(focus) ?? (focus.Planets.Count > 0 ? focus.Planets[0] : null);
        }

        static FocusPlanet PickOwnedPlanet(FocusContext focus)
        {
            if (focus == null)
                return null;
            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            foreach (var p in focus.Planets)
            {
                if (owned > 0 && p.UserId == owned)
                    return p;
            }

            return null;
        }

        /// <summary>
        /// Poke id → GetTranslations key. Native web keys first (docs/i18n/missing-keys.md §1);
        /// vr.* keys are listed there for the server until they land.
        /// </summary>
        static readonly Dictionary<string, string> PokeLabelKeys = new()
        {
            { "AddCore", "vr.engineering.addCore" },
            { "Addresses", "vr.stargate.addresses" },
            { "Alliance", "alliance" },
            { "Battle", "battle" },
            { "Chat", "chatPanel" },
            { "Colonize", "Colonize" },
            { "Decide", "vr.ops.decide" },
            { "Defense", "defense" },
            { "Empires", "empires" },
            { "Explore", "explorePlanet" },
            { "Goals", "dailyObjectives" },
            { "Jumpgate", "jumpgate" },
            { "Layout", "vr.engineering.layout" },
            { "Mail", "mailTitle" },
            { "OrderQueue", "orderQueue" },
            { "Research", "research" },
            { "ShipQueue", "shipyardQueue" },
            { "Shop", "shop" },
            { "Troops", "vr.tactical.troops" },
            { "Upgrade", "upGrade" },
            { "Wars", "wars" }
        };

        static void AddPoke(Transform parent, CicArtKit art, string name, Vector3 local, System.Func<Task> act)
        {
            var labelKey = PokeLabelKeys.TryGetValue(name, out var key) ? key : name;
            // 0.16 m caps on a 0.2 m pitch: a finger-sized gap between neighbours.
            var xi = DiegeticUi.Button(parent, "Poke_" + name, Trans.Get(labelKey), local,
                new Vector3(0.16f, 0.055f, 0.05f), art, CicArtKit.Cyan,
                () => Core.Utils.AsyncTap.Run(act()));

            // Alcoves sit on walls turned every way: face the bridge centre at standing eye height,
            // so the readable front never points at the wall (kit front is -Z).
            var room = parent.GetComponentInParent<CicEnvironment>();
            var mount = xi.transform.parent;
            if (room != null && mount != null)
                Core.UI.ScreenMount.FaceViewer(mount, room.transform.TransformPoint(new Vector3(0f, 1.6f, 0.4f)), 0.4f);
        }

        static string TrimBody(string body, int max)
        {
            if (string.IsNullOrEmpty(body))
                return string.Empty;
            body = body.Replace("\n", " ");
            return body.Length <= max ? body : body.Substring(0, max) + "…";
        }
    }
}
