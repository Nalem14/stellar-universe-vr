using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Where we are, on the bulkhead above the main screen like a ship's dedication plaque: what we stand on
    /// (ship / orbital station, cyan / amber like the rest of the bridge) and its name — the ship's name, or
    /// the world the station orbits — with the system under it. Follows every view change (a ship hop, a
    /// station TP), no per-frame work.
    /// </summary>
    public sealed class BridgeNameplate : MonoBehaviour
    {
        FocusContext _focus;
        TextMeshPro _kind;
        TextMeshPro _name;
        TextMeshPro _where;
        MeshRenderer _strip;
        CicArtKit _art;
        string _sig;

        public static BridgeNameplate Build(CicEnvironment host, FocusContext focus)
        {
            var go = new GameObject("BridgeNameplate");
            go.transform.SetParent(host.transform, false);
            // On the fascia over the screen hood, angled down toward the chair.
            go.transform.localPosition = new Vector3(0f, 3.14f, BridgeShell.Plan[BridgeShell.Forward].y - 0.7f);
            go.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            var plate = go.AddComponent<BridgeNameplate>();
            plate._focus = focus;
            plate._art = host.Art;
            plate.BuildPlate();
            if (focus != null)
            {
                focus.Changed += plate.Refresh;
                focus.FleetsChanged += plate.Refresh;
            }

            plate.Refresh();
            return plate;
        }

        void OnDestroy()
        {
            if (_focus == null)
                return;
            _focus.Changed -= Refresh;
            _focus.FleetsChanged -= Refresh;
        }

        void BuildPlate()
        {
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Plate";
            Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            back.transform.localScale = new Vector3(2.3f, 0.3f, 0.04f);
            back.GetComponent<MeshRenderer>().sharedMaterial = UI.UiKit.Chassis;

            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "Strip";
            Destroy(strip.GetComponent<Collider>());
            strip.transform.SetParent(transform, false);
            strip.transform.localPosition = new Vector3(0f, -0.155f, 0.02f);
            strip.transform.localScale = new Vector3(2.3f, 0.012f, 0.03f);
            _strip = strip.GetComponent<MeshRenderer>();

            _kind = UI.UiKit.Label(transform, "Kind", string.Empty, new Vector3(-0.62f, 0.06f, -0.001f), 0.95f, 0.07f,
                CicArtKit.Cyan, TextAlignmentOptions.Left);
            _kind.characterSpacing = 12f;
            _name = UI.UiKit.Label(transform, "Name", string.Empty, new Vector3(-0.62f, -0.045f, -0.001f), 0.95f, 0.13f,
                Color.white, TextAlignmentOptions.Left);
            _name.fontStyle = FontStyles.Bold;
            // Player-chosen names are shown as typed (no tags).
            _name.richText = false;
            _where = UI.UiKit.Label(transform, "Where", string.Empty, new Vector3(0.6f, -0.02f, -0.001f), 0.95f, 0.08f,
                new Color(0.75f, 0.88f, 0.95f), TextAlignmentOptions.Right);
        }

        void Refresh()
        {
            if (_focus == null)
                return;
            var fleet = _focus.FindViewFleet();
            string kind, name;
            if (fleet != null)
            {
                // A ship, or an orbital fortress (its server name already says "Station Orbitale <world>").
                kind = Trans.Get(fleet.IsStation ? "orbitalStation" : "ship");
                name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
            }
            else
            {
                kind = Trans.Get("vr.view.citadelHeader");
                name = BridgeViewscreen.StationPlanetName(_focus);
            }

            var where = _focus.HasSystem ? BridgeViewscreen.SystemLabel(_focus) : string.Empty;
            var sig = kind + "|" + name + "|" + where;
            if (sig == _sig)
                return;
            _sig = sig;
            var accent = _focus.Mode switch
            {
                ViewMode.City => CityExterior.CitadelGold,
                ViewMode.Station => CicArtKit.Amber,
                _ => CicArtKit.Cyan
            };
            _kind.text = kind.ToUpperInvariant();
            _kind.color = accent;
            _name.text = name;
            _where.text = where;
            if (_art != null)
                _strip.sharedMaterial = _art.Lit(Texture2D.whiteTexture, accent, 2.2f);
        }
    }
}
