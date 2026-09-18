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
            WireGalaxyColonize(host, art, focus, poller, hex);
            WirePassthroughStub(host, art);
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
                // Upgrade first common building key if server accepts — sourced enums.buildings.
                await ActionJs.Get("UpgradeBuilding", new Dictionary<string, string>
                {
                    { "planet", planet.Id.ToString() },
                    { "buildingtype", "metalMine" }
                });
                if (poller != null)
                    await poller.PollNow();
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
            tmp.text = "LCARS";
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
                foreach (var f in focus.Fleets)
                {
                    if (owned > 0 && f.UserId == owned)
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

        static void AddPoke(Transform parent, CicArtKit art, string name, Vector3 local, System.Func<Task> act)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Poke_" + name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(0.18f, 0.05f, 0.05f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan, 1.8f);
            var interact = go.AddComponent<XRSimpleInteractable>();
            interact.selectEntered.AddListener(_ => _ = act());
            var t = new GameObject("L");
            t.transform.SetParent(go.transform, false);
            t.transform.localPosition = new Vector3(0f, 0.8f, -0.6f);
            t.transform.localScale = Vector3.one * 0.03f;
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 4f;
            tmp.color = Color.white;
            tmp.text = name;
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
