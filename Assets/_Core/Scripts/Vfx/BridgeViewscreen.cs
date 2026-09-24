using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Main viewscreen over the centre hublot, aimed at the captain: focused system, inhabited ship
    /// (or orbital station) and its state, and what is around (planets / ships in system).
    /// Event-driven on FocusContext plus a 2 s tick for server timers; nothing per frame.
    /// </summary>
    public sealed class BridgeViewscreen : MonoBehaviour
    {
        FocusContext _focus;
        HoloScreen _screen;
        TMP_Text _system;
        TMP_Text _ship;
        TMP_Text _context;

        public static BridgeViewscreen Build(CicEnvironment host, FocusContext focus)
        {
            var half = WorldScale.CicDeck * 0.5f;
            var screen = HoloScreen.Create(host.transform, "MainViewscreen", new Vector2(2.6f, 0.72f),
                new Vector3(0f, WorldScale.CicHublotCenterY + WorldScale.CicHublotHeight * 0.5f + 0.43f, half - 0.3f),
                Quaternion.identity, Trans.Get("CommandBridge"));
            screen.SetAccent(CicArtKit.Cyan, 0.5f);
            ScreenMount.FaceViewer(screen.transform,
                host.transform.TransformPoint(WorldScale.CicCaptainStand + Vector3.up * WorldScale.EyeStanding), 1f);

            var view = screen.gameObject.AddComponent<BridgeViewscreen>();
            view._focus = focus;
            view._screen = screen;
            var px = screen.PixelSize;
            // Read from the captain spot ~6.5 m away: ~2° cap height for the system, ~1.2° for details.
            view._system = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(0f, px.y * 0.14f),
                new Vector2(px.x * 0.92f, 200f), 170f, UiKit.TextBright);
            view._ship = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(0f, -px.y * 0.16f),
                new Vector2(px.x * 0.92f, 120f), 96f, UiKit.Cyan);
            view._context = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(0f, -px.y * 0.37f),
                new Vector2(px.x * 0.92f, 90f), 68f, UiKit.TextDim);
            if (focus != null)
            {
                focus.Changed += view.Refresh;
                focus.FleetsChanged += view.Refresh;
            }

            view.InvokeRepeating(nameof(Refresh), 0.5f, 2f);
            return view;
        }

        /// <summary>Planet the virtual station orbits (name, else #id).</summary>
        public static string StationPlanetName(FocusContext focus)
        {
            if (focus == null || focus.ViewPlanetId <= 0)
                return string.Empty;
            var planet = focus.FindPlanet(focus.ViewPlanetId);
            return planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : "#" + focus.ViewPlanetId;
        }

        void OnDestroy()
        {
            if (_focus == null)
                return;
            _focus.Changed -= Refresh;
            _focus.FleetsChanged -= Refresh;
        }

        void Refresh()
        {
            if (_focus == null || _system == null)
                return;
            _system.text = _focus.HasSystem ? _focus.SystemName : Trans.Get("Loading");

            // Two inhabited modes (docs/VISION-VR.md §3.2): a real ship's bridge, or a virtual
            // orbital station over a planet (no fleet orders there; Helm offers boarding a ship).
            var fleet = _focus.FindViewFleet();
            if (fleet != null)
            {
                _screen.SetHeader(Trans.Get("CommandBridge"));
                var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
                var state = FleetOrderGate.CanMove(fleet)
                    ? Trans.Get("vr.crew.standby")
                    : Trans.Get(FleetOrderGate.BusyKey(fleet));
                _ship.text = name + "  ·  " + state;
            }
            else
            {
                _screen.SetHeader(Trans.Get("vr.view.stationHeader"));
                _ship.text = Trans.Format("vr.view.orbiting", StationPlanetName(_focus));
            }

            var now = (long)(System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
            _context.text = Trans.Get("planets") + "  " + _focus.Planets.Count + "    ·    " +
                            Trans.Get("fleets") + "  " + _focus.CountVisibleInFocus(now);
        }
    }
}
