using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Idle face of a crew station screen: inhabited ship (or orbital station) and its current state
    /// (native busy keys: moving, attacking, harvesting, exploring, in battle — else standing by).
    /// Refreshed on focus events and on a slow 2 s tick for timers that expire server-side; no per-frame work.
    /// </summary>
    public sealed class CrewStationScreen : MonoBehaviour
    {
        FocusContext _focus;
        TMP_Text _primary;
        TMP_Text _status;

        public void Bind(HoloScreen screen, FocusContext focus, Color accent)
        {
            _focus = focus;
            var px = screen.PixelSize;
            _primary = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(0f, px.y * 0.08f),
                new Vector2(px.x * 0.9f, 70f), 48f, UiKit.TextBright);
            _status = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(0f, -px.y * 0.22f),
                new Vector2(px.x * 0.9f, 56f), 34f, accent);
            if (_focus != null)
            {
                _focus.Changed += Refresh;
                _focus.FleetsChanged += Refresh;
            }

            InvokeRepeating(nameof(Refresh), 0.5f, 2f);
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
            if (_primary == null || _focus == null)
                return;
            var fleet = _focus.FindViewFleet();
            if (fleet != null)
            {
                _primary.text = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
                _status.text = FleetOrderGate.CanMove(fleet)
                    ? Trans.Get("vr.crew.standby")
                    : Trans.Get(FleetOrderGate.BusyKey(fleet));
                return;
            }

            // Virtual orbital station: no ship under the crew, stations stand by (Helm can board a ship).
            _primary.text = _focus.ViewPlanetId > 0
                ? Trans.Format("vr.view.orbiting", BridgeViewscreen.StationPlanetName(_focus))
                : BridgeViewscreen.SystemLabel(_focus);
            _status.text = Trans.Get(_focus.HasSystem ? "vr.view.stationHeader" : "Loading");
        }
    }
}
