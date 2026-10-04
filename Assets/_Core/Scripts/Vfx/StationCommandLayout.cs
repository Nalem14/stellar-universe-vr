using System.Collections.Generic;
using Core.App;
using Core.UI;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// What moves when the CIC turns from a ship's bridge into a station's command hall (<see cref="BridgeDressing"/>).
    /// Aboard a ship the six crew posts ring the table in a horseshoe and the captain has a chair. At a station
    /// there is no command chair: the commander stands at the table inside a podium rail whose two lecterns carry
    /// the arm panels (same keys, same tablets, turned to the standing eye), and the crew work three to a tier on
    /// the raised crescents under the data walls, facing them (<see cref="StationCommandShell.Tiers"/>) — read over
    /// their shoulders from the pit. Poses are swapped, never rebuilt: every system holding a post, a pad or an
    /// officer keeps its transform.
    /// </summary>
    public static class StationCommandLayout
    {
        static readonly string[] ChairParts =
        {
            "CaptainDais", "CaptainDaisEdge", "CaptainRailL", "CaptainRailR", "CaptainRailBar", "CaptainFoot",
            "CaptainColumn", "CaptainSeatShell", "CaptainSeat", "CaptainBackFrame", "CaptainArmPostL", "CaptainArmPostR",
            "CaptainArmL", "CaptainArmR"
        };

        /// <summary>Tier posts: starboard fore → aft Tactical, Engineering, Ops; port fore → aft Helm, Science, Comms.</summary>
        static readonly (string name, float deg)[] Posts =
        {
            ("CrewTactical", 74f), ("CrewEngineering", 90f), ("CrewOps", 106f),
            ("CrewHelm", 286f), ("CrewScience", 270f), ("CrewComms", 254f)
        };

        /// <summary>Console line on the tier (the operator sits 0.28 m nearer the pit, on the flat top).</summary>
        const float PostRadius = 5.75f;
        /// <summary>How far each arm panel swings out to the commander's side, about the standing eye.</summary>
        const float PodiumTurn = 60f;
        const float PodiumEyeHeight = 1.5f;
        /// <summary>
        /// The condition selector, from the standing point: ahead-right of the right lectern, outside the podium's
        /// floor ring and lectern, clear of the table's projection cone (≈0.15 m).
        /// </summary>
        const float ConditionBearing = 80f;
        const float ConditionReach = 0.95f;

        static readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> Ship = new();
        static Transform _room;
        static Transform _podium;

        public static void Apply(Transform room, CicArtKit art, ViewMode mode)
        {
            var station = mode == ViewMode.Station;
            var city = mode == ViewMode.City;
            if (room == null)
                return;
            if (_room != room)
            {
                Ship.Clear();
                _room = room;
                _podium = null;
            }

            Index(room);
            // A station has no chair: whoever sat in it stands up at the table first.
            var cmd = CaptainCommandMode.Instance;
            if (station && cmd != null && cmd.IsCommandMode)
                Core.Utils.AsyncTap.Run(cmd.ExitCommandMode());
            // In the citadel the chair is the throne (CitadelHall): same seat, same arm consoles, same stand-up
            // key — only the bridge chair's own shell is hidden, its colliders kept.
            foreach (var name in ChairParts)
            {
                var t = Find(name);
                if (t == null)
                    continue;
                t.gameObject.SetActive(!station);
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    r.enabled = !city;
            }

            var sit = Find("ExitCommand");
            if (sit != null)
                sit.gameObject.SetActive(!station);

            foreach (var (name, deg) in Posts)
            {
                var t = Find(name);
                if (t == null)
                    continue;
                Remember(t);
                if (city && CitadelHall.PostPose(name, out var at, out var face))
                {
                    t.SetLocalPositionAndRotation(at, face);
                }
                else if (station)
                {
                    var dir = LatheMesh.Dir(deg);
                    t.localPosition = StationCommandShell.OnTier(deg, PostRadius);
                    t.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                }
                else
                {
                    Restore(t);
                }
            }

            // Arm pads: the seated pose turned about the eye onto the standing commander's sides.
            var seatedEye = new Vector3(0f, 1.34f, WorldScale.CicCaptainChairZ - 0.13f);
            var standEye = WorldScale.CicCaptainStand + Vector3.up * PodiumEyeHeight;
            var pads = new List<Transform>(2);
            foreach (var (name, side) in new[] { ("ArmPadL", -1f), ("ArmPadR", 1f) })
            {
                var pad = Find(name);
                if (pad == null)
                    continue;
                Remember(pad);
                if (!station)
                {
                    Restore(pad);
                    continue;
                }

                var (p0, r0) = Ship[pad];
                var turn = Quaternion.Euler(0f, side * PodiumTurn, 0f);
                pad.localPosition = standEye + turn * (p0 - seatedEye);
                pad.localRotation = turn * r0;
                pads.Add(pad);
            }

            // The condition selector: ahead-right of the right lectern, keys to the standing commander.
            var condition = Find("ConditionPanel");
            if (condition != null)
            {
                Remember(condition);
                if (station && pads.Count == 2)
                {
                    var at = WorldScale.CicCaptainStand + LatheMesh.Dir(ConditionBearing) * ConditionReach;
                    at.y = 0f;
                    condition.GetComponent<AlertConditionPanel>()?.Place(at, standEye);
                }
                else
                {
                    Restore(condition);
                }
            }

            if (station && _podium == null && pads.Count == 2 && art != null)
                _podium = BuildPodium(room, art, pads[0].localPosition, pads[1].localPosition);
        }

        /// <summary>
        /// Lecterns under the two pads, a waist rail behind the commander joining them, a lit ring on the floor
        /// round the stand. Under the hall's shell, so it hides with it aboard a ship.
        /// </summary>
        static Transform BuildPodium(Transform room, CicArtKit art, Vector3 left, Vector3 right)
        {
            var shell = room.Find("StationShell");
            var root = new GameObject("CommandPodium").transform;
            root.SetParent(shell != null ? shell : room, false);
            var stand = WorldScale.CicCaptainStand;

            foreach (var (pad, accent) in new[] { (left, CicArtKit.Cyan), (right, CicArtKit.Amber) })
            {
                var top = pad.y - 0.03f;
                Piece(root, "PodiumFoot", new Vector3(0.34f, 0.04f, 0.34f), 0.015f, new Vector3(pad.x, 0.02f, pad.z), accent, 0.4f);
                Piece(root, "PodiumColumn", new Vector3(0.1f, top - 0.04f, 0.1f), 0.04f, new Vector3(pad.x, (top + 0.04f) * 0.5f, pad.z),
                    accent, 0.15f);
                Piece(root, "PodiumHead", new Vector3(0.2f, 0.04f, 0.17f), 0.015f, new Vector3(pad.x, top - 0.005f, pad.z), accent, 0.6f);
            }

            var rail = new LatheMesh(stand) { Step = 6f };
            var lit = new LatheMesh(stand) { Step = 6f };
            float Angle(Vector3 p) => Mathf.Repeat(Mathf.Atan2(p.x - stand.x, p.z - stand.z) * Mathf.Rad2Deg, 360f);
            var a0 = Angle(right);
            var a1 = Angle(left);
            var r = 0.5f * (new Vector2(left.x - stand.x, left.z - stand.z).magnitude + new Vector2(right.x - stand.x, right.z - stand.z).magnitude);
            const float y = 0.95f;
            rail.Revolve(new[] { new Vector2(r + 0.03f, y), new Vector2(r - 0.03f, y) }, a0, a1, false);
            rail.Revolve(new[] { new Vector2(r - 0.03f, y), new Vector2(r - 0.03f, y - 0.06f) }, a0, a1, false);
            rail.Revolve(new[] { new Vector2(r + 0.03f, y - 0.06f), new Vector2(r + 0.03f, y) }, a0, a1, false);
            rail.Bar(180f, r - 0.025f, r + 0.025f, 0f, y - 0.06f, 0.025f);
            lit.Revolve(new[] { new Vector2(r - 0.034f, y - 0.045f), new Vector2(r - 0.034f, y - 0.02f) }, a0 + 2f, a1 - 2f, true);
            lit.Revolve(new[] { new Vector2(0.66f, 0.004f), new Vector2(0.62f, 0.004f) }, 0f, 360f, false);
            LatheMesh.Part(root, "PodiumRail", rail.ToMesh("SU_StationPodiumRail"), art.MetalPanel(0.22f));
            LatheMesh.Part(root, "PodiumGlow", lit.ToMesh("SU_StationPodiumGlow"), art.Lit(Texture2D.whiteTexture, StationCommandShell.Glow, 2f));
            return root;
        }

        static void Piece(Transform parent, string name, Vector3 size, float radius, Vector3 pos, Color accent, float mul)
        {
            var go = UiKit.MeshPiece(parent, name, UiMeshes.RoundedBox(size, radius), UiKit.Chassis, pos);
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, mul);
            var mr = go.GetComponent<MeshRenderer>();
            mr.SetPropertyBlock(block);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Remember(Transform t)
        {
            if (!Ship.ContainsKey(t))
                Ship[t] = (t.localPosition, t.localRotation);
        }

        static void Restore(Transform t)
        {
            if (Ship.TryGetValue(t, out var pose))
                t.SetLocalPositionAndRotation(pose.pos, pose.rot);
        }

        static readonly Dictionary<string, Transform> Named = new();

        /// <summary>One pass over the room (active or not), first of each name — only on a view change.</summary>
        static void Index(Transform room)
        {
            Named.Clear();
            foreach (var t in room.GetComponentsInChildren<Transform>(true))
                Named.TryAdd(t.name, t);
        }

        static Transform Find(string name) => Named.TryGetValue(name, out var t) ? t : null;
    }
}
