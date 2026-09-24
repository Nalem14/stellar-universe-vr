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
            // Port side of the captain's dais, ~2 m from the standing spot, turned to face the captain:
            // in view without turning around (was on the aft wall behind the chair).
            var root = new GameObject("ViewTeleporter");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = TeleporterPos;
            Core.UI.ScreenMount.FaceViewer(root.transform,
                host.transform.TransformPoint(WorldScale.CicCaptainStand + Vector3.up * WorldScale.EyeStanding), 0f);

            // Platform: brushed rounded disc hardware with a thin lit rim (no saturated additive disc).
            Core.UI.UiKit.MeshPiece(root.transform, "TeleportPad",
                Core.UI.UiMeshes.RoundedBox(new Vector3(1.15f, 0.06f, 1.15f), 0.028f),
                Core.UI.UiKit.Chassis, new Vector3(0f, 0.03f, 0.35f));
            var padRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            padRing.name = "PadRing";
            padRing.transform.SetParent(root.transform, false);
            padRing.transform.localPosition = new Vector3(0f, 0.062f, 0.35f);
            padRing.transform.localScale = new Vector3(0.95f, 0.002f, 0.95f);
            CicEnvironment.DropColliderStatic(padRing);
            padRing.GetComponent<MeshRenderer>().sharedMaterial = art.Holo(art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture,
                new Color(0.2f, 0.85f, 1f, 0.35f));

            // Arch: two rounded pillars and a lintel, cyan bevels.
            foreach (var x in new[] { -0.72f, 0.72f })
            {
                var pillar = Core.UI.UiKit.MeshPiece(root.transform, x < 0f ? "TpPillarL" : "TpPillarR",
                    Core.UI.UiMeshes.RoundedBox(new Vector3(0.16f, 2.3f, 0.2f), 0.05f), Core.UI.UiKit.Chassis,
                    new Vector3(x, 1.15f, 0.35f));
                pillar.AddComponent<BoxCollider>().size = new Vector3(0.16f, 2.3f, 0.2f);
                Tint(pillar, CicArtKit.Cyan, 0.6f);
            }

            var lintel = Core.UI.UiKit.MeshPiece(root.transform, "TpArch",
                Core.UI.UiMeshes.RoundedBox(new Vector3(1.6f, 0.12f, 0.22f), 0.05f), Core.UI.UiKit.Chassis,
                new Vector3(0f, 2.3f, 0.35f));
            Tint(lintel, CicArtKit.Cyan, 1.4f);

            // Face the pad / captain — World Space holographic console.
            // Kit front is -Z: root faces away from the captain, so the canvas needs no flip.
            var canvas = DiegeticUi.WorldCanvas(root.transform, "TpCanvas", new Vector2(900f, 720f),
                new Vector3(0f, 1.42f, 0.2f), Quaternion.identity, 0.00115f);
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
            return tp;
        }

        /// <summary>Deck position of the view teleporter (port of the captain's dais).</summary>
        static readonly Vector3 TeleporterPos = new(-2.05f, 0f, WorldScale.CicCaptainChairZ + 0.1f);

        static void Tint(GameObject go, Color accent, float mul)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(Core.UI.UiKit.AccentId, accent);
            block.SetFloat(Core.UI.UiKit.AccentMulId, mul);
            go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
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
