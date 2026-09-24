using Core.App;
using Core.UI;
using Core.Utils;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Bridge Crew horseshoe: six crewed stations on an arc around the holo table
    /// (<see cref="WorldScale.CicStationArcRadius"/>), consoles facing the forward bulkhead, operator
    /// seated between console and table. Left → right from the captain:
    /// Comms, Science, Helm | Tactical, Engineering, Ops (Helm / Tactical front centre).
    /// Everything is rounded kit hardware (<see cref="UiMeshes"/>, SU/ConsoleMetal) with the station
    /// colour on bevels via MaterialPropertyBlock — one shared material for the whole crew.
    /// Orders: ray-select an officer → <see cref="CrewDialogue"/> repeater within the captain's reach.
    /// </summary>
    public static class CrewStationsBuilder
    {
        public readonly struct StationDef
        {
            public readonly string Name;
            public readonly CrewDialogue.Role Role;
            public readonly float AngleDeg;
            public readonly Color Accent;
            public readonly string TitleKey;

            public StationDef(string name, CrewDialogue.Role role, float angleDeg, Color accent, string titleKey)
            {
                Name = name;
                Role = role;
                AngleDeg = angleDeg;
                Accent = accent;
                TitleKey = titleKey;
            }
        }

        static readonly Color Science = new(0.62f, 0.5f, 1f, 1f);
        static readonly Color Engineering = new(0.4f, 0.95f, 0.55f, 1f);
        static readonly Color Comms = new(1f, 0.85f, 0.35f, 1f);
        static readonly Color Ops = new(0.35f, 0.75f, 1f, 1f);

        public static readonly StationDef[] Stations =
        {
            new("CrewComms", CrewDialogue.Role.Comms, -75f, Comms, "vr.station.comms"),
            new("CrewScience", CrewDialogue.Role.Science, -45f, Science, "vr.station.science"),
            new("CrewHelm", CrewDialogue.Role.Helm, -15f, CicArtKit.Cyan, "vr.station.helm"),
            new("CrewTactical", CrewDialogue.Role.Tactical, 15f, CicArtKit.Amber, "vr.station.tactical"),
            new("CrewEngineering", CrewDialogue.Role.Engineering, 45f, Engineering, "vr.station.engineering"),
            new("CrewOps", CrewDialogue.Role.Ops, 75f, Ops, "vr.station.ops")
        };

        public static void Build(CicEnvironment host, CicArtKit art,
            HexBattleController hex, HoloZoneMap map, FleetPoller poller, FocusContext focus,
            BridgeSystemLoader loader)
        {
            var centre = new Vector3(0f, 0f, WorldScale.CicTableCenterZ);
            foreach (var def in Stations)
            {
                var dir = Quaternion.Euler(0f, def.AngleDeg, 0f) * Vector3.forward;
                var pos = centre + dir * WorldScale.CicStationArcRadius;
                BuildStation(host, art, def, pos, Quaternion.LookRotation(dir, Vector3.up), hex, map, poller,
                    focus, loader);
            }
        }

        static void BuildStation(CicEnvironment host, CicArtKit art, StationDef def, Vector3 pos, Quaternion rot,
            HexBattleController hex, HoloZoneMap map, FleetPoller poller, FocusContext focus,
            BridgeSystemLoader loader)
        {
            var root = new GameObject(def.Name).transform;
            root.SetParent(host.transform, false);
            root.localPosition = pos;
            root.localRotation = rot;

            // Console: pedestal, sloped desk, accent lip, screen — local +Z = away from the table.
            Piece(root, "Pedestal", new Vector3(0.72f, 0.7f, 0.42f), 0.05f, new Vector3(0f, 0.35f, 0.42f),
                Quaternion.identity, def.Accent, 0.12f, collider: true);
            Piece(root, "Desk", new Vector3(1.05f, 0.05f, 0.52f), 0.02f, new Vector3(0f, 0.76f, 0.36f),
                Quaternion.Euler(-12f, 0f, 0f), def.Accent, 0.3f, collider: true);
            Piece(root, "Lip", new Vector3(1.0f, 0.025f, 0.035f), 0.012f, new Vector3(0f, 0.72f, 0.1f),
                Quaternion.identity, def.Accent, 1.6f);
            Piece(root, "ScreenArm", new Vector3(0.08f, 0.32f, 0.06f), 0.03f, new Vector3(0f, 0.98f, 0.62f),
                Quaternion.identity, def.Accent, 0.1f);

            var screen = HoloScreen.Create(root, "StationScreen", new Vector2(0.64f, 0.34f),
                new Vector3(0f, 1.22f, 0.6f), Quaternion.identity, Trans.Get(def.TitleKey));
            screen.SetAccent(def.Accent, 0.45f);
            // Read by its operator (seated) and, over the shoulder, by the captain behind.
            ScreenMount.FaceViewer(screen.transform,
                root.TransformPoint(new Vector3(0f, WorldScale.EyeSeated, -0.3f)), 0.7f);
            var status = screen.gameObject.AddComponent<CrewStationScreen>();
            status.Bind(screen, focus, def.Accent);

            // Operator chair between console and table.
            Piece(root, "SeatPost", new Vector3(0.08f, 0.42f, 0.08f), 0.035f, new Vector3(0f, 0.21f, -0.28f),
                Quaternion.identity, def.Accent, 0.05f);
            Piece(root, "Seat", new Vector3(0.48f, 0.08f, 0.46f), 0.035f, new Vector3(0f, 0.46f, -0.28f),
                Quaternion.identity, def.Accent, 0.2f, collider: true);
            Piece(root, "SeatBack", new Vector3(0.46f, 0.56f, 0.07f), 0.03f, new Vector3(0f, 0.8f, -0.54f),
                Quaternion.Euler(-8f, 0f, 0f), def.Accent, 0.35f);

            var officer = CrewOfficer.Build(root, new Vector3(0f, 0.5f, -0.28f), def.Accent);
            CrewDialogue.Attach(officer, host, art, def.Accent, def.Role, hex, map, poller, focus, loader);
        }

        static GameObject Piece(Transform parent, string name, Vector3 size, float radius, Vector3 pos,
            Quaternion rot, Color accent, float accentMul, bool collider = false)
        {
            var go = UiKit.MeshPiece(parent, name, UiMeshes.RoundedBox(size, radius), UiKit.Chassis, pos);
            go.transform.localRotation = rot;
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, accentMul);
            go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            if (collider)
                go.AddComponent<BoxCollider>().size = size;
            return go;
        }
    }
}
