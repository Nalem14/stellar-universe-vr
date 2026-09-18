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
    /// <summary>
    /// Diegetic view teleporter: arche + screen tabs Spaceships / Planets.
    /// </summary>
    public class BridgeViewTeleporter : MonoBehaviour
    {
        BridgeSystemLoader _loader;
        FocusContext _focus;
        Transform _listRoot;
        TMP_Text _title;
        bool _shipsTab = true;
        readonly List<GameObject> _rows = new();
        CicArtKit _art;

        public void Bind(BridgeSystemLoader loader, FocusContext focus, CicArtKit art)
        {
            _loader = loader;
            _focus = focus;
            _art = art;
        }

        public static BridgeViewTeleporter Build(CicEnvironment host, CicArtKit art)
        {
            var half = WorldScale.CicDeck * 0.5f;
            var root = new GameObject("ViewTeleporter");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, -half + 1.35f);

            // Pad ring
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "TeleportPad";
            pad.transform.SetParent(root.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            pad.transform.localScale = new Vector3(1.4f, 0.02f, 1.4f);
            pad.GetComponent<MeshRenderer>().sharedMaterial =
                art.Holo(Texture2D.whiteTexture, new Color(0.15f, 0.85f, 1f, 0.45f));

            // Pillars + arch
            host.Box("TpPillarL", new Vector3(-0.85f, 1.2f, -half + 1.35f),
                new Vector3(0.18f, 2.2f, 0.18f), art.MetalPanel(0.1f), keepCollider: true);
            host.Box("TpPillarR", new Vector3(0.85f, 1.2f, -half + 1.35f),
                new Vector3(0.18f, 2.2f, 0.18f), art.MetalPanel(0.1f), keepCollider: true);
            host.Box("TpArch", new Vector3(0f, 2.25f, -half + 1.35f),
                new Vector3(2.0f, 0.16f, 0.22f), art.CyanEmit(2.4f), keepCollider: false);

            // Screen
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "TpScreen";
            screen.transform.SetParent(root.transform, false);
            screen.transform.localPosition = new Vector3(0f, 1.45f, -0.15f);
            screen.transform.localScale = new Vector3(1.5f, 0.95f, 1f);
            CicEnvironment.DropColliderStatic(screen);
            screen.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(art.ScreenIdle != null ? art.ScreenIdle : Texture2D.whiteTexture,
                    new Color(0.05f, 0.12f, 0.18f), 0.85f);

            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bezel.name = "TpBezel";
            bezel.transform.SetParent(root.transform, false);
            bezel.transform.localPosition = new Vector3(0f, 1.45f, -0.18f);
            bezel.transform.localScale = new Vector3(1.65f, 1.1f, 0.06f);
            bezel.GetComponent<MeshRenderer>().sharedMaterial = art.DarkPanel(0.08f);

            var titleGo = new GameObject("TpTitle");
            titleGo.transform.SetParent(root.transform, false);
            titleGo.transform.localPosition = new Vector3(0f, 1.85f, -0.12f);
            titleGo.transform.localScale = Vector3.one * 0.01f;
            var title = titleGo.AddComponent<TextMeshPro>();
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 8f;
            title.color = new Color(0.55f, 0.95f, 1f);
            title.text = "TP";

            var list = new GameObject("TpList").transform;
            list.SetParent(root.transform, false);
            list.localPosition = new Vector3(0f, 1.35f, -0.11f);

            // Tabs
            MakeTab(root.transform, art, new Vector3(-0.4f, 1.72f, -0.11f), "Ships", true, out var tabShips);
            MakeTab(root.transform, art, new Vector3(0.4f, 1.72f, -0.11f), "Planets", false, out var tabPlanets);

            var tp = root.AddComponent<BridgeViewTeleporter>();
            tp._title = title;
            tp._listRoot = list;
            tp._art = art;

            tabShips.selectEntered.AddListener(_ =>
            {
                tp._shipsTab = true;
                _ = tp.RefreshList();
            });
            tabPlanets.selectEntered.AddListener(_ =>
            {
                tp._shipsTab = false;
                _ = tp.RefreshList();
            });

            host.KeyLight("TpLamp", new Vector3(0f, 2.4f, -half + 1.35f), CicArtKit.Cyan, 1.1f, 4.5f);
            return tp;
        }

        static void MakeTab(Transform parent, CicArtKit art, Vector3 localPos, string label,
            bool ships, out XRSimpleInteractable interact)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Tab_" + label;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(0.55f, 0.1f, 0.04f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(Texture2D.whiteTexture, ships ? CicArtKit.Cyan : CicArtKit.Amber, 1.6f);
            interact = go.AddComponent<XRSimpleInteractable>();

            var tmpGo = new GameObject("Label");
            tmpGo.transform.SetParent(go.transform, false);
            tmpGo.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            tmpGo.transform.localScale = Vector3.one * 0.02f;
            var tmp = tmpGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 6f;
            tmp.color = Color.white;
            tmp.text = label;
        }

        public async Task RefreshList()
        {
            ClearRows();
            if (_title != null)
                _title.text = _shipsTab ? "Spaceships" : "Planets";

            if (_shipsTab)
                await LoadShips();
            else
                await LoadPlanets();
        }

        async Task LoadShips()
        {
            var result = await ActionJs.Get("GetAllFleets");
            if (!result.Ok)
            {
                if (_title != null)
                    _title.text = result.Error;
                return;
            }

            try
            {
                var root = Newtonsoft.Json.Linq.JToken.Parse(result.Body);
                var arr = root as Newtonsoft.Json.Linq.JArray
                          ?? root["fleets"] as Newtonsoft.Json.Linq.JArray;
                if (arr == null)
                    return;
                var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
                var i = 0;
                foreach (var f in arr)
                {
                    if (owned > 0 && FocusContext.AsInt(f["userid"]) != owned)
                        continue;
                    var id = FocusContext.AsInt(f["id"]);
                    var sys = FocusContext.AsInt(f["systemid"]);
                    var name = FocusContext.AsString(f["name"]);
                    if (string.IsNullOrEmpty(name))
                        name = "ship " + id;
                    AddRow(name + " · " + sys, () => _ = ConfirmShip(id, sys));
                    i++;
                    if (i >= 8)
                        break;
                }
            }
            catch
            {
                if (_title != null)
                    _title.text = "GetAllFleets";
            }
        }

        async Task LoadPlanets()
        {
            var result = await ActionJs.Get("GetEmpirePlanets");
            if (!result.Ok)
            {
                // Fallback: planets in current focus
                if (_focus != null)
                {
                    foreach (var p in _focus.Planets)
                    {
                        var label = string.IsNullOrEmpty(p.Name) ? "planet " + p.Id : p.Name;
                        var planetId = p.Id;
                        var sys = _focus.SystemId;
                        AddRow(label, () => _ = ConfirmPlanet(planetId, sys));
                    }
                }

                return;
            }

            try
            {
                var root = Newtonsoft.Json.Linq.JToken.Parse(result.Body);
                var arr = root as Newtonsoft.Json.Linq.JArray
                          ?? root["planets"] as Newtonsoft.Json.Linq.JArray;
                if (arr == null)
                    return;
                var i = 0;
                foreach (var p in arr)
                {
                    var id = FocusContext.AsInt(p["id"]);
                    var sys = FocusContext.AsInt(p["systemid"]);
                    var name = FocusContext.AsString(p["name"]);
                    if (string.IsNullOrEmpty(name))
                        name = "planet " + id;
                    AddRow(name + " · " + sys, () => _ = ConfirmPlanet(id, sys));
                    i++;
                    if (i >= 8)
                        break;
                }
            }
            catch
            {
                if (_title != null)
                    _title.text = "GetEmpirePlanets";
            }
        }

        void AddRow(string label, System.Action onSelect)
        {
            if (_listRoot == null || _art == null)
                return;
            var y = 0.12f - _rows.Count * 0.1f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Row";
            go.transform.SetParent(_listRoot, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = new Vector3(1.2f, 0.08f, 0.03f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                _art.Lit(Texture2D.whiteTexture, new Color(0.1f, 0.25f, 0.32f), 0.9f);
            var interact = go.AddComponent<XRSimpleInteractable>();
            interact.selectEntered.AddListener(_ => onSelect());

            var tmpGo = new GameObject("Txt");
            tmpGo.transform.SetParent(go.transform, false);
            tmpGo.transform.localPosition = new Vector3(0f, 0f, -0.7f);
            tmpGo.transform.localScale = Vector3.one * 0.018f;
            var tmp = tmpGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 5.5f;
            tmp.color = new Color(0.7f, 0.95f, 1f);
            tmp.text = label;
            _rows.Add(go);
        }

        void ClearRows()
        {
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i] != null)
                    Destroy(_rows[i]);
            _rows.Clear();
        }

        async Task ConfirmShip(int fleetId, int systemId)
        {
            if (_loader == null)
                return;
            if (_title != null)
                _title.text = Trans.Get("Loading");
            var ok = await _loader.LoadShipView(fleetId, systemId);
            if (_title != null)
                _title.text = ok ? "Spaceships" : "error";
        }

        async Task ConfirmPlanet(int planetId, int systemId)
        {
            if (_loader == null)
                return;
            if (_title != null)
                _title.text = Trans.Get("Loading");
            var ok = await _loader.LoadPlanetStation(planetId, systemId);
            if (_title != null)
                _title.text = ok ? "Planets" : "error";
        }

        void Start()
        {
            _ = RefreshList();
        }
    }
}
