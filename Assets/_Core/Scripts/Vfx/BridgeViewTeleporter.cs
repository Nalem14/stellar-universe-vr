using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// Diegetic view teleporter — World Space holographic canvas (Bridge Crew style).
    /// </summary>
    public class BridgeViewTeleporter : MonoBehaviour
    {
        BridgeSystemLoader _loader;
        FocusContext _focus;
        RectTransform _listRoot;
        TMP_Text _title;
        TMP_Text _hint;
        bool _shipsTab = true;
        readonly List<GameObject> _rows = new();
        CicArtKit _art;
        Button _tabShips;
        Button _tabPlanets;

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

            // Face the pad / captain — World Space holographic console.
            var canvas = DiegeticUi.WorldCanvas(root.transform, "TpCanvas", new Vector2(900f, 720f),
                new Vector3(0f, 1.45f, -0.12f), Quaternion.Euler(0f, 180f, 0f), 0.00115f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, new Vector2(880f, 700f),
                Trans.Get("CommandBridge"));

            var tp = root.AddComponent<BridgeViewTeleporter>();
            tp._art = art;
            tp._title = DiegeticUi.HoloLabel(frame, Trans.Get("fleets"), new Vector2(0f, 220f),
                new Vector2(800f, 40f), 26f, DiegeticUi.Cyan);
            tp._hint = DiegeticUi.HoloLabel(frame, Trans.Get("CommandBridge"), new Vector2(0f, -300f),
                new Vector2(800f, 36f), 18f, DiegeticUi.CyanDim);

            tp._tabShips = DiegeticUi.HoloButton(frame, Trans.Get("fleets"),
                new Vector2(-200f, 160f), new Vector2(280f, 56f), () =>
                {
                    tp._shipsTab = true;
                    tp.StyleTabs();
                    Core.Utils.AsyncTap.Run(tp.RefreshList());
                });
            tp._tabPlanets = DiegeticUi.HoloButton(frame, Trans.Get("planets"),
                new Vector2(200f, 160f), new Vector2(280f, 56f), () =>
                {
                    tp._shipsTab = false;
                    tp.StyleTabs();
                    Core.Utils.AsyncTap.Run(tp.RefreshList());
                }, amber: true);

            var listGo = new GameObject("TpList", typeof(RectTransform));
            listGo.transform.SetParent(frame, false);
            tp._listRoot = listGo.GetComponent<RectTransform>();
            tp._listRoot.sizeDelta = new Vector2(820f, 400f);
            tp._listRoot.anchoredPosition = new Vector2(0f, -40f);

            tp.StyleTabs();
            host.KeyLight("TpLamp", new Vector3(0f, 2.55f, -half + 1.35f), CicArtKit.Cyan, 1.4f, 5f);
            return tp;
        }

        void StyleTabs()
        {
            if (_tabShips != null)
                _tabShips.GetComponent<Image>().color = _shipsTab
                    ? Color.white
                    : new Color(0.55f, 0.65f, 0.7f, 0.85f);
            if (_tabPlanets != null)
                _tabPlanets.GetComponent<Image>().color = !_shipsTab
                    ? Color.white
                    : new Color(0.55f, 0.65f, 0.7f, 0.85f);
        }

        public async Task RefreshList()
        {
            ClearRows();
            if (_title != null)
                _title.text = _shipsTab ? Trans.Get("fleets") : Trans.Get("planets");

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

            UpdateHint();
        }

        void UpdateHint()
        {
            if (_hint == null)
                return;
            if (_focus != null && _focus.ViewFleetId > 0)
            {
                var fleet = _focus.FindViewFleet();
                var name = fleet != null && !string.IsNullOrEmpty(fleet.Name)
                    ? fleet.Name
                    : "#" + _focus.ViewFleetId;
                _hint.text = name + "  ·  #" + _focus.ViewFleetId;
                return;
            }

            if (_focus != null && _focus.ViewPlanetId > 0)
            {
                var planet = _focus.FindPlanet(_focus.ViewPlanetId);
                var name = planet != null && !string.IsNullOrEmpty(planet.Name)
                    ? planet.Name
                    : "#" + _focus.ViewPlanetId;
                _hint.text = name + "  ·  #" + _focus.ViewPlanetId;
                return;
            }

            _hint.text = _shipsTab ? Trans.Get("fleets") : Trans.Get("planets");
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

            if (_focus != null)
                _focus.ApplyFleetsBody(result.Body);
            await GalaxyCatalog.EnsureLoaded();

            try
            {
                var owned = FocusContext.OwnedUserId();
                var viewId = _focus != null ? _focus.ViewFleetId : 0;
                var pending = new List<(int id, int sys, string name, bool active)>();
                if (_focus != null)
                {
                    foreach (var fleet in _focus.Fleets)
                    {
                        if (owned > 0 && !fleet.IsOwnedBy(owned))
                            continue;
                        var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
                        pending.Add((fleet.Id, fleet.SystemId, name, viewId > 0 && fleet.Id == viewId));
                    }
                }

                pending.Sort((a, b) =>
                {
                    if (a.active != b.active)
                        return a.active ? -1 : 1;
                    return string.CompareOrdinal(a.name, b.name);
                });

                var i = 0;
                foreach (var row in pending)
                {
                    var id = row.id;
                    var sys = row.sys;
                    var star = GalaxyCatalog.TryGet(sys, out var s) ? s.Name : string.Empty;
                    AddRow(row.name, star, row.active,
                        () => Core.Utils.AsyncTap.Run(ConfirmShip(id, sys)));
                    i++;
                    if (i >= 8)
                        break;
                }
            }
            catch
            {
                if (_title != null)
                    _title.text = Trans.Get("vr.common.error");
            }
        }

        static readonly List<GalaxyCatalog.PlanetRef> OwnedScratch = new();

        /// <summary>
        /// Own planets across the galaxy, as the web builds its planets window: GetSystems planets
        /// whose userid is mine (GetEmpirePlanets has no systemid, so it cannot drive a TP).
        /// </summary>
        async Task LoadPlanets()
        {
            var viewPlanet = _focus != null && _focus.ViewFleetId <= 0 ? _focus.ViewPlanetId : 0;
            await GalaxyCatalog.EnsureLoaded();
            GalaxyCatalog.CollectOwnedPlanets(FocusContext.OwnedUserId(), OwnedScratch);

            var pending = new List<(int id, int sys, string name, string star, bool active)>(OwnedScratch.Count);
            foreach (var p in OwnedScratch)
            {
                var name = string.IsNullOrEmpty(p.Name) ? "#" + p.Id : p.Name;
                var star = GalaxyCatalog.TryGet(p.SystemId, out var s) ? s.Name : string.Empty;
                pending.Add((p.Id, p.SystemId, name, star, viewPlanet > 0 && p.Id == viewPlanet));
            }

            pending.Sort((a, b) =>
            {
                if (a.active != b.active)
                    return a.active ? -1 : 1;
                return string.CompareOrdinal(a.name, b.name);
            });

            var i = 0;
            foreach (var row in pending)
            {
                var id = row.id;
                var sys = row.sys;
                AddRow(row.name, row.star, row.active, () => Core.Utils.AsyncTap.Run(ConfirmPlanet(id, sys)));
                if (++i >= 8)
                    break;
            }
        }

        void AddRow(string primary, string secondary, bool active, System.Action onSelect)
        {
            if (_listRoot == null)
                return;
            var y = 150f - _rows.Count * 70f;
            var mark = active ? "●  " : "";
            var label = string.IsNullOrEmpty(secondary)
                ? mark + primary
                : mark + primary + "  ·  " + secondary;
            var btn = DiegeticUi.HoloButton(_listRoot, label, new Vector2(0f, y), new Vector2(780f, 60f),
                () => onSelect?.Invoke(), amber: false);
            if (active)
            {
                var img = btn.GetComponent<Image>();
                if (img != null)
                    img.color = new Color(0.45f, 1f, 0.95f, 1f);
            }

            _rows.Add(btn.gameObject);
        }

        void AddStatusRow(string label)
        {
            if (_listRoot == null)
                return;
            var tmp = DiegeticUi.HoloLabel(_listRoot, label, new Vector2(0f, 40f), new Vector2(780f, 48f),
                22f, DiegeticUi.Cyan);
            _rows.Add(tmp.gameObject);
        }

        void ClearRows()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] == null)
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(_rows[i]);
                else
                    Object.DestroyImmediate(_rows[i]);
            }

            _rows.Clear();
            if (_listRoot == null)
                return;
            for (var i = _listRoot.childCount - 1; i >= 0; i--)
            {
                var c = _listRoot.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Object.Destroy(c);
                else
                    Object.DestroyImmediate(c);
            }
        }

        async Task ConfirmShip(int fleetId, int systemId)
        {
            if (_loader == null)
                return;
            if (_title != null)
                _title.text = Trans.Get("Loading");
            var ok = await _loader.LoadShipView(fleetId, systemId);
            if (_title != null)
                _title.text = ok ? Trans.Get("fleets") : Trans.Get("vr.common.error");
            if (ok)
            {
                CicCue.Ok(transform.position);
                await RefreshList();
            }
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
                _title.text = ok ? Trans.Get("planets") : Trans.Get("vr.common.error");
            if (ok)
            {
                CicCue.Ok(transform.position);
                await RefreshList();
            }
            else
                CicCue.Fail(transform.position);
        }

        void Start() => Core.Utils.AsyncTap.Run(RefreshList());
    }
}
