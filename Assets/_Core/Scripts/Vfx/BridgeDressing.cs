using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Ship bridge vs orbital station on the same CIC kit. Aboard a ship: the horseshoe bridge, cyan. At a
    /// station: the round command hall (<see cref="StationCommandShell"/>) with the ring outside its bays, the
    /// commander's podium and the crew tiers (<see cref="StationCommandLayout"/>), the door and aft displays on
    /// its curved wall, the lights lifted into the dome, cool white-cyan accents.
    /// </summary>
    public static class BridgeDressing
    {
        static readonly string[] ShipOnly = { "BridgeShell", "CommandPlate", "DeckStrip", "BridgeDecor" };

        public static void Apply(CicEnvironment host, FocusContext focus)
        {
            if (host == null)
                return;
            var ship = focus != null && focus.ViewFleetId > 0;
            ApplyLayout(host, !ship);
            var accent = ship ? CicArtKit.Cyan : StationCommandShell.Glow;
            foreach (var r in host.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || r.sharedMaterial == null || host.Art == null)
                    continue;
                var n = r.gameObject.name;
                // The sky panel over the table: cool daylight on a ship, a softer white under the station's dome.
                if (n == "SkyPanel")
                {
                    r.sharedMaterial = host.Art.Lit(Texture2D.whiteTexture, ship ? new Color(0.7f, 0.88f, 1f) : new Color(0.82f, 0.94f, 1f), 1.25f);
                    continue;
                }

                if (n.IndexOf("Strip", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Accent", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Trim", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // Floor lines stay soft; wall and frame lines carry the accent.
                var floor = n is "KickStrip" or "DeckStrip";
                r.sharedMaterial = host.Art.Lit(Texture2D.whiteTexture, accent, floor ? 1f : ship ? 2.4f : 2f);
            }
        }

        static bool _layoutSet;
        static bool _station;
        static CicEnvironment _host;

        static void ApplyLayout(CicEnvironment host, bool station)
        {
            if (_layoutSet && _station == station && _host == host)
                return;
            _layoutSet = true;
            _station = station;
            _host = host;
            var room = host.transform;
            foreach (var name in ShipOnly)
            {
                var t = room.Find(name);
                if (t != null)
                    t.gameObject.SetActive(!station);
            }

            var hall = room.Find("StationShell");
            if (hall != null)
                hall.gameObject.SetActive(station);

            // The corridor door: in the aft bulkhead, or in its portal on the hall's curved wall.
            var door = room.Find("CorridorDoor");
            if (door != null)
                door.localPosition = station ? StationCommandShell.OnWall(180f, 0.12f, 0f) : new Vector3(0f, 0f, -WorldScale.CicDeck * 0.5f + 0.12f);

            // The four room lights: under the bridge's sky panel, the bow wash, two warm pools aft — or high in
            // the dome, the monolith's wash, and the credenzas either side of the door.
            Light("Fill", station ? new Vector3(0f, 6.6f, WorldScale.CicTableCenterZ) : new Vector3(0f, 3.4f, 0.5f),
                station ? new Color(0.72f, 0.88f, 1f) : new Color(0.62f, 0.8f, 1f), station ? 1.6f : 1.2f, station ? 15f : 10f);
            Light("HublotWash", station ? new Vector3(0f, 2.3f, 5.6f) : new Vector3(0f, 1.9f, 4.9f), CicArtKit.Cyan, 0.9f,
                station ? 6.5f : 5.5f);
            Light("Warm", station ? StationCommandShell.OnWall(200f, 1.4f, 2.5f) : new Vector3(-3.4f, 2.5f, -4.3f),
                station ? new Color(0.62f, 0.86f, 1f) : CicArtKit.Amber, 0.85f, station ? 6.5f : 5.5f);
            Light("WarmStbd", station ? StationCommandShell.OnWall(160f, 1.4f, 2.5f) : new Vector3(3.4f, 2.5f, -4.3f),
                station ? new Color(0.62f, 0.86f, 1f) : CicArtKit.Amber, 0.85f, station ? 6.5f : 5.5f);
            var rig = host.GetComponent<RoomLightRig>();
            if (rig != null)
                rig.Radius = station ? WorldScale.StationHallRadius + 3f : 10f;

            // No command chair at a station: a standing podium, the crew up on the tiers.
            StationCommandLayout.Apply(room, host.Art, station);

            var displays = host.GetComponentInChildren<BridgeWallDisplays>(true);
            if (displays != null)
                displays.SetStationLayout(station);
            var fx = host.GetComponentInChildren<BridgeCombatFx>(true);
            if (fx != null)
                fx.SetStationLayout(station);
            var crew = host.GetComponentInChildren<Core.Crew.CrewLife>(true);
            if (crew != null)
                crew.SetStationLayout(station);
            var view = Object.FindFirstObjectByType<BridgeViewRig>();
            if (view != null)
                view.SetStationLayout(station);

            void Light(string name, Vector3 pos, Color color, float intensity, float range)
            {
                var t = room.Find(name);
                var l = t != null ? t.GetComponent<UnityEngine.Light>() : null;
                if (l == null)
                    return;
                t.localPosition = pos;
                l.color = color;
                l.intensity = intensity;
                l.range = range;
            }
        }
    }
}
