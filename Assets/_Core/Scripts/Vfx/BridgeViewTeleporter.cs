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
    /// Diegetic view teleporter — CIC console facing the pad, labeled tabs + readable rows.
    /// </summary>
    public class BridgeViewTeleporter : MonoBehaviour
    {
        BridgeSystemLoader _loader;
        FocusContext _focus;
        Transform _listRoot;
        TMP_Text _title;
        TMP_Text _hint;
        bool _shipsTab = true;
        readonly List<GameObject> _rows = new();
        CicArtKit _art;
        Material _rowIdle;
        Material _rowHover;
        Material _tabShipIdle;
        Material _tabShipHover;
        Material _tabPlanetIdle;
        Material _tabPlanetHover;

        public void Bind(BridgeSystemLoader loader, FocusContext focus, CicArtKit art)
        {
            _loader = loader;
            _focus = focus;
            _art = art;
            CacheMats();
        }

        void CacheMats()
        {
            if (_art == null)
                return;
            _rowIdle = _art.Lit(_art.ScreenIdle != null ? _art.ScreenIdle : Texture2D.whiteTexture,
                new Color(0.08f, 0.28f, 0.36f), 1.6f);
            _rowHover = _art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan, 2.8f);
            _tabShipIdle = _art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan * 0.75f, 2.0f);
            _tabShipHover = _art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan, 3.4f);
            _tabPlanetIdle = _art.Lit(Texture2D.whiteTexture, CicArtKit.Amber * 0.75f, 2.0f);
            _tabPlanetHover = _art.Lit(Texture2D.whiteTexture, CicArtKit.Amber, 3.4f);
        }

        public static BridgeViewTeleporter Build(CicEnvironment host, CicArtKit art)
        {
            var half = WorldScale.CicDeck * 0.5f;
            var root = new GameObject("ViewTeleporter");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, -half + 1.35f);

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "TeleportPad";
            pad.transform.SetParent(root.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            pad.transform.localScale = new Vector3(1.5f, 0.025f, 1.5f);
            pad.GetComponent<MeshRenderer>().sharedMaterial =
                art.Holo(Texture2D.whiteTexture, new Color(0.15f, 0.85f, 1f, 0.55f));
            var padRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            padRing.name = "PadRing";
            padRing.transform.SetParent(root.transform, false);
            padRing.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            padRing.transform.localScale = new Vector3(1.65f, 0.008f, 1.65f);
            CicEnvironment.DropColliderStatic(padRing);
            padRing.GetComponent<MeshRenderer>().sharedMaterial = art.CyanEmit(2.8f);

            host.Box("TpPillarL", new Vector3(-0.95f, 1.25f, -half + 1.35f),
                new Vector3(0.2f, 2.4f, 0.2f), art.MetalPanel(0.12f), keepCollider: true);
            host.Box("TpPillarR", new Vector3(0.95f, 1.25f, -half + 1.35f),
                new Vector3(0.2f, 2.4f, 0.2f), art.MetalPanel(0.12f), keepCollider: true);
            host.Box("TpArch", new Vector3(0f, 2.4f, -half + 1.35f),
                new Vector3(2.2f, 0.14f, 0.22f), art.CyanEmit(2.6f), keepCollider: false);

            // Face pad / room.
            var face = new GameObject("TpFace").transform;
            face.SetParent(root.transform, false);
            face.localPosition = new Vector3(0f, 0f, -0.1f);
            face.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "Chassis";
            chassis.transform.SetParent(face, false);
            chassis.transform.localPosition = new Vector3(0f, 1.4f, 0.06f);
            chassis.transform.localScale = new Vector3(1.85f, 1.35f, 0.1f);
            chassis.GetComponent<MeshRenderer>().sharedMaterial = art.MetalPanel(0.15f);

            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "TpScreen";
            screen.transform.SetParent(face, false);
            screen.transform.localPosition = new Vector3(0f, 1.4f, 0.005f);
            screen.transform.localScale = new Vector3(1.65f, 1.15f, 1f);
            CicEnvironment.DropColliderStatic(screen);
            screen.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(art.ScreenIdle != null ? art.ScreenIdle : Texture2D.whiteTexture,
                    new Color(0.05f, 0.18f, 0.26f), 1.8f);

            var scan = GameObject.CreatePrimitive(PrimitiveType.Quad);
            scan.name = "TpScan";
            scan.transform.SetParent(face, false);
            scan.transform.localPosition = new Vector3(0f, 1.4f, -0.002f);
            scan.transform.localScale = new Vector3(1.62f, 1.12f, 1f);
            CicEnvironment.DropColliderStatic(scan);
            scan.GetComponent<MeshRenderer>().sharedMaterial =
                art.Holo(Texture2D.whiteTexture, new Color(0.25f, 0.95f, 1f, 0.1f));

            var title = DiegeticUi.Label(face, "TpTitle", Trans.Get("spaceships"),
                new Vector3(0f, 1.92f, -0.02f), 0.04f, 8f, new Color(0.65f, 0.98f, 1f));
            title.rectTransform.sizeDelta = new Vector2(50f, 8f);

            var hint = DiegeticUi.Label(face, "TpHint", Trans.Get("CommandBridge"),
                new Vector3(0f, 0.78f, -0.02f), 0.028f, 5f, new Color(0.45f, 0.75f, 0.85f));
            hint.rectTransform.sizeDelta = new Vector2(60f, 6f);

            var list = new GameObject("TpList").transform;
            list.SetParent(face, false);
            list.localPosition = new Vector3(0f, 1.25f, -0.02f);

            var tp = root.AddComponent<BridgeViewTeleporter>();
            tp._title = title;
            tp._hint = hint;
            tp._listRoot = list;
            tp._art = art;
            tp.CacheMats();

            MakeTab(face, tp, true, new Vector3(-0.42f, 1.72f, -0.03f));
            MakeTab(face, tp, false, new Vector3(0.42f, 1.72f, -0.03f));

            host.KeyLight("TpLamp", new Vector3(0f, 2.55f, -half + 1.35f), CicArtKit.Cyan, 1.4f, 5f);
            return tp;
        }

        static void MakeTab(Transform face, BridgeViewTeleporter tp, bool ships, Vector3 localPos)
        {
            var idle = ships ? tp._tabShipIdle : tp._tabPlanetIdle;
            var hover = ships ? tp._tabShipHover : tp._tabPlanetHover;
            var label = ships ? Trans.Get("spaceships") : Trans.Get("planets");
            DiegeticUi.Plate(face, ships ? "Tab_Ships" : "Tab_Planets", localPos,
                new Vector3(0.7f, 0.14f, 0.05f), idle, hover, () =>
                {
                    tp._shipsTab = ships;
                    Core.Utils.AsyncTap.Run(tp.RefreshList());
                }, out _);
            var tmp = DiegeticUi.Label(face, ships ? "TabLabel_Ships" : "TabLabel_Planets", label,
                localPos + new Vector3(0f, 0f, -0.04f), 0.035f, 6f, Color.white);
            tmp.rectTransform.sizeDelta = new Vector2(28f, 6f);
        }

        public async Task RefreshList()
        {
            ClearRows();
            if (_title != null)
                _title.text = _shipsTab ? Trans.Get("spaceships") : Trans.Get("planets");

            if (_shipsTab)
                await LoadShips();
            else
                await LoadPlanets();

            if (_rows.Count == 0)
            {
                var msg = _title != null && !string.IsNullOrEmpty(_title.text)
                    ? _title.text
                    : Trans.Get("Loading");
                AddStatusRow(msg);
            }

            if (_hint != null)
                _hint.text = _shipsTab ? Trans.Get("spaceships") : Trans.Get("planets");
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
                    AddRow(name, "sys " + sys, () => Core.Utils.AsyncTap.Run(ConfirmShip(id, sys)));
                    i++;
                    if (i >= 6)
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
                if (_focus != null)
                {
                    foreach (var p in _focus.Planets)
                    {
                        var label = string.IsNullOrEmpty(p.Name) ? "planet " + p.Id : p.Name;
                        var planetId = p.Id;
                        var sys = _focus.SystemId;
                        AddRow(label, "sys " + sys, () => Core.Utils.AsyncTap.Run(ConfirmPlanet(planetId, sys)));
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
                    AddRow(name, "sys " + sys, () => Core.Utils.AsyncTap.Run(ConfirmPlanet(id, sys)));
                    i++;
                    if (i >= 6)
                        break;
                }
            }
            catch
            {
                if (_title != null)
                    _title.text = "GetEmpirePlanets";
            }
        }

        void AddRow(string primary, string secondary, System.Action onSelect)
        {
            if (_listRoot == null || _art == null)
                return;
            CacheMats();
            var y = 0.08f - _rows.Count * 0.14f;
            DiegeticUi.Plate(_listRoot, "Row_" + _rows.Count, new Vector3(0f, y, 0f),
                new Vector3(1.45f, 0.12f, 0.04f), _rowIdle, _rowHover, onSelect, out _);
            var primaryT = DiegeticUi.Label(_listRoot, "P", primary,
                new Vector3(-0.15f, y, -0.035f), 0.032f, 5.5f, new Color(0.85f, 0.98f, 1f),
                TextAlignmentOptions.Left);
            primaryT.rectTransform.sizeDelta = new Vector2(36f, 6f);
            var secondaryT = DiegeticUi.Label(_listRoot, "S", secondary,
                new Vector3(0.5f, y, -0.035f), 0.028f, 4.5f, new Color(0.5f, 0.85f, 0.95f),
                TextAlignmentOptions.Right);
            secondaryT.rectTransform.sizeDelta = new Vector2(16f, 5f);
            // Track plate (first child of last add is awkward) — store a marker empty.
            var marker = new GameObject("RowMark_" + _rows.Count);
            marker.transform.SetParent(_listRoot, false);
            _rows.Add(marker);
        }

        void AddStatusRow(string label)
        {
            if (_listRoot == null || _art == null)
                return;
            CacheMats();
            DiegeticUi.Plate(_listRoot, "StatusRow", new Vector3(0f, 0.05f, 0f),
                new Vector3(1.45f, 0.16f, 0.04f), _rowIdle, _rowIdle, null, out _);
            var tmp = DiegeticUi.Label(_listRoot, "StatusTxt", label,
                new Vector3(0f, 0.05f, -0.035f), 0.034f, 5.5f, new Color(0.75f, 0.95f, 1f));
            tmp.rectTransform.sizeDelta = new Vector2(42f, 8f);
            var marker = new GameObject("StatusMark");
            marker.transform.SetParent(_listRoot, false);
            _rows.Add(marker);
        }

        void ClearRows()
        {
            if (_listRoot == null)
            {
                _rows.Clear();
                return;
            }

            for (var i = _listRoot.childCount - 1; i >= 0; i--)
            {
                var c = _listRoot.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(c);
                else
                    DestroyImmediate(c);
            }

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
                _title.text = ok ? Trans.Get("spaceships") : Trans.Get("error");
            if (ok)
                CicCue.Ok(transform.position);
            else
                CicCue.Fail(transform.position);
        }

        async Task ConfirmPlanet(int planetId, int systemId)
        {
            if (_loader == null)
                return;
            if (_title != null)
                _title.text = Trans.Get("Loading");
            var ok = await _loader.LoadPlanetStation(planetId, systemId);
            if (_title != null)
                _title.text = ok ? Trans.Get("planets") : Trans.Get("error");
            if (ok)
                CicCue.Ok(transform.position);
            else
                CicCue.Fail(transform.position);
        }

        void Start() => Core.Utils.AsyncTap.Run(RefreshList());
    }
}
