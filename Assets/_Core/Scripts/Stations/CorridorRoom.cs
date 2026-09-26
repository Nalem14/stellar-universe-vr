using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The corridor behind the bridge: the bridge has one exit, and every other room opens off this passage — the
    /// lab and the dry dock first, the diplomacy chamber and the captain's quarters further on, the gate base at the
    /// far end — so going anywhere is a walk through the ship (or the station). A hexagonal hall swept in one
    /// profile (kick, wall, chamfer, lit ceiling channel) with ribs, portholes onto the shared exterior (the hall
    /// is placed over the bridge like the other rooms with windows), floor runners and a lit channel overhead.
    /// Ship = cyan, station = amber. Local: +z from the bridge door (z = 0) to the gate door (z = <see cref="Length"/>).
    /// </summary>
    public sealed class CorridorRoom : MonoBehaviour
    {
        public const float Length = 15f;
        const float HalfWidth = 1.6f;
        const float WallTop = 2.65f;
        const float CeilingY = 3.2f;
        const float ChannelY = 3.34f;
        const float ChannelHalf = 0.45f;
        const float PortholeY = 1.55f;
        const float PortholeSize = 0.72f;
        static readonly Vector3 WorldOrigin = new(-160f, -3000f, -200f);
        static readonly Vector3 Stand = new(0f, 0f, 1.3f);
        static readonly float[] Portholes = { 2.3f, 7.25f, 12.4f };

        public enum Slot
        {
            LabPort,
            DockStarboard,
            DiplomacyPort,
            QuartersStarboard,
            GateEnd
        }

        public static CorridorRoom Instance { get; private set; }
        public static bool Inside { get; private set; }

        CicArtKit _art;
        FocusContext _focus;
        MeshRenderer[] _accents;
        bool _station;

        /// <summary>Where a room's door stands in the corridor (door local +z faces into the corridor).</summary>
        public static (Vector3 pos, float yaw) DoorPose(Slot slot) => slot switch
        {
            Slot.LabPort => (new Vector3(-HalfWidth + 0.12f, 0f, 4.6f), 90f),
            Slot.DockStarboard => (new Vector3(HalfWidth - 0.12f, 0f, 4.6f), -90f),
            Slot.DiplomacyPort => (new Vector3(-HalfWidth + 0.12f, 0f, 9.9f), 90f),
            Slot.QuartersStarboard => (new Vector3(HalfWidth - 0.12f, 0f, 9.9f), -90f),
            _ => (new Vector3(0f, 0f, Length - 0.12f), 180f)
        };

        public static CorridorRoom Build(CicArtKit art, FocusContext focus)
        {
            var go = new GameObject("ShipCorridor");
            go.transform.position = WorldOrigin;
            var room = go.AddComponent<CorridorRoom>();
            room._art = art;
            room._focus = focus;
            room.BuildShell();
            room.BuildLights();
            RoomDoor.Build(go.transform, "DoorToBridge", new Vector3(0f, 0f, 0.12f), 0f, Trans.Get("CommandBridge"), CicArtKit.Cyan,
                art, () => Inside, () => AsyncTap.Run(room.LeaveToBridge()));
            go.SetActive(false);
            return room;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ── Shell ────────────────────────────────────────────────────────────────

        /// <summary>Half cross-section (x ≥ 0), floor centre to ceiling centre, with baked occlusion.</summary>
        static readonly Vector2[] Section =
        {
            new(0f, 0f), new(HalfWidth - 0.14f, 0f), new(HalfWidth, 0.14f), new(HalfWidth, WallTop), new(HalfWidth - 0.55f, CeilingY),
            new(ChannelHalf, CeilingY), new(ChannelHalf, ChannelY), new(0f, ChannelY)
        };

        static readonly float[] SectionAo = { 1f, 0.8f, 0.55f, 1f, 0.8f, 0.7f, 0.85f, 1f };

        void BuildShell()
        {
            var wall = _art.Hull(_art.Panel, new Color(0.55f, 0.61f, 0.69f), new Vector2(1.5f, 0.66f));
            var dark = _art.Hull(_art.Panel, new Color(0.26f, 0.3f, 0.35f), new Vector2(0.75f, 0.5f), seam: 0.55f);
            var deck = _art.Hull(_art.DeckRib != null ? _art.DeckRib : _art.Floor, new Color(0.6f, 0.66f, 0.74f), new Vector2(1.5f, 1.5f),
                tiling: 0.42f, seam: 0.35f);
            var rib = _art.Hull(_art.Panel, new Color(0.62f, 0.68f, 0.76f), new Vector2(40f, 0.5f), seam: 0.3f, lift: 0.08f);
            var frame = _art.Hull(_art.Panel, new Color(0.42f, 0.47f, 0.54f), new Vector2(40f, 40f), seam: 0f, lift: 0.06f);

            var floor = new ShellMesh();
            var walls = new ShellMesh();
            var upper = new ShellMesh();
            for (var side = -1; side <= 1; side += 2)
            for (var i = 0; i < Section.Length - 1; i++)
            {
                var a = Section[i];
                var b = Section[i + 1];
                var target = i == 0 ? floor : i == 2 ? walls : i == 1 ? floor : upper;
                if (i == 2)
                {
                    SideWall(walls, side);
                    continue;
                }

                var pa0 = new Vector3(side * a.x, a.y, 0f);
                var pb0 = new Vector3(side * b.x, b.y, 0f);
                var pa1 = new Vector3(side * a.x, a.y, Length);
                var pb1 = new Vector3(side * b.x, b.y, Length);
                var s = (b - a).normalized;
                var hint = new Vector3(-side * s.y, s.x, 0f);
                if (i == 0)
                    hint = Vector3.up;
                var v0 = V(i);
                var v1 = V(i + 1);
                target.Quad(pa0, pb0, pb1, pa1, hint, new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(Length, v1), new Vector2(Length, v0),
                    SectionAo[i], SectionAo[i + 1], SectionAo[i + 1], SectionAo[i]);
            }

            // End walls: the section closed as a fan at z = 0 (facing +z) and at the far end (facing −z).
            foreach (var (z, facing) in new[] { (0f, Vector3.forward), (Length, Vector3.back) })
            {
                var c = new Vector3(0f, 1.6f, z);
                for (var side = -1; side <= 1; side += 2)
                for (var i = 0; i < Section.Length - 1; i++)
                {
                    var a = new Vector3(side * Section[i].x, Section[i].y, z);
                    var b = new Vector3(side * Section[i + 1].x, Section[i + 1].y, z);
                    walls.Tri(c, a, b, facing, new Vector2(c.x, c.y), new Vector2(a.x, a.y), new Vector2(b.x, b.y), 1f, SectionAo[i], SectionAo[i + 1]);
                }
            }

            Part("Deck", floor.ToMesh("SU_CorridorDeck"), deck, true);
            Part("Walls", walls.ToMesh("SU_CorridorWalls"), wall, true);
            Part("Upper", upper.ToMesh("SU_CorridorUpper"), dark, false);

            // Ribs every 1.5 m (none across a door), portholes with lit frames.
            var ribs = new ShellMesh();
            for (var z = 0.75f; z < Length - 0.3f; z += 1.5f)
            {
                if (Mathf.Abs(z - 4.6f) < 1.05f || Mathf.Abs(z - 9.9f) < 1.05f)
                    continue;
                Rib(ribs, z);
            }

            Part("Ribs", ribs.ToMesh("SU_CorridorRibs"), rib, false);

            var frames = new ShellMesh();
            var glow = new ShellMesh();
            foreach (var z in Portholes)
            for (var side = -1; side <= 1; side += 2)
                Porthole(frames, glow, side, z);
            Part("Frames", frames.ToMesh("SU_CorridorFrames"), frame, false);
            Part("FrameStrip", glow.ToMesh("SU_CorridorFrameGlow"), _art.CyanEmit(3f), false);

            // Light lines: the ceiling channel, two floor runners, the kick on both sides.
            var lights = new ShellMesh();
            Strip(lights, new Vector3(-0.1f, ChannelY - 0.004f, 0.2f), new Vector3(0.1f, ChannelY - 0.004f, Length - 0.2f), Vector3.down);
            foreach (var x in new[] { -0.62f, 0.62f })
                Strip(lights, new Vector3(x - 0.025f, 0.004f, 0.4f), new Vector3(x + 0.025f, 0.004f, Length - 0.4f), Vector3.up);
            var ceilingLight = Part("CeilingStrip", lights.ToMesh("SU_CorridorStrips"), _art.CyanEmit(1.2f), false);

            var kick = new ShellMesh();
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * (HalfWidth - 0.05f);
                kick.Quad(new Vector3(x, 0.06f, 0.2f), new Vector3(x, 0.09f, 0.2f), new Vector3(x, 0.09f, Length - 0.2f), new Vector3(x, 0.06f, Length - 0.2f),
                    new Vector3(-side, 0.4f, 0f), Vector2.zero, Vector2.up, Vector2.one, Vector2.right, 1f, 1f, 1f, 1f);
            }

            var kickLight = Part("KickStrip", kick.ToMesh("SU_CorridorKick"), _art.CyanEmit(1f), false);

            // Cable conduits along the chamfers.
            var conduit = _art.Hull(_art.Panel, new Color(0.2f, 0.23f, 0.27f), new Vector2(40f, 40f), seam: 0f);
            foreach (var x in new[] { -1.28f, 1.28f })
            {
                var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pipe.name = "Conduit";
                Destroy(pipe.GetComponent<Collider>());
                pipe.transform.SetParent(transform, false);
                pipe.transform.localPosition = new Vector3(x, 2.86f, Length * 0.5f);
                pipe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                pipe.transform.localScale = new Vector3(0.07f, Length * 0.5f - 0.1f, 0.07f);
                pipe.GetComponent<MeshRenderer>().sharedMaterial = conduit;
            }

            // Solid hull boxes behind the side walls (a character body never slips through a one-sided mesh).
            for (var side = -1; side <= 1; side += 2)
                Box(new Vector3(side * (HalfWidth + 0.3f), 1.6f, Length * 0.5f), new Vector3(0.6f, 3.6f, Length + 1f));
            Box(new Vector3(0f, 1.6f, -0.3f), new Vector3(HalfWidth * 2f + 1f, 3.6f, 0.6f));
            Box(new Vector3(0f, 1.6f, Length + 0.3f), new Vector3(HalfWidth * 2f + 1f, 3.6f, 0.6f));

            _accents = new[] { ceilingLight, kickLight };
        }

        static float V(int i)
        {
            var v = 0f;
            for (var k = 1; k <= i; k++)
                v += Vector2.Distance(Section[k - 1], Section[k]);
            return v;
        }

        /// <summary>The vertical wall of one side, with the portholes cut out.</summary>
        void SideWall(ShellMesh m, int side)
        {
            var x = side * HalfWidth;
            var n = new Vector3(-side, 0f, 0f);
            var y0 = Section[2].y;
            var y1 = Section[3].y;
            var v0 = V(2);
            var h = PortholeSize * 0.5f;

            void Rect(float z0, float z1, float ya, float yb)
            {
                if (z1 - z0 < 1e-3f || yb - ya < 1e-3f)
                    return;
                m.Quad(new Vector3(x, ya, z0), new Vector3(x, ya, z1), new Vector3(x, yb, z1), new Vector3(x, yb, z0), n,
                    new Vector2(z0, v0 + ya - y0), new Vector2(z1, v0 + ya - y0), new Vector2(z1, v0 + yb - y0), new Vector2(z0, v0 + yb - y0),
                    1f, 1f, 1f, 1f);
            }

            var at = 0f;
            foreach (var z in Portholes)
            {
                Rect(at, z - h, y0, y1);
                Rect(z - h, z + h, y0, PortholeY - h);
                Rect(z - h, z + h, PortholeY + h, y1);
                at = z + h;
            }

            Rect(at, Length, y0, y1);
        }

        void Rib(ShellMesh m, float z)
        {
            const float hw = 0.07f;
            const float depth = 0.08f;
            for (var side = -1; side <= 1; side += 2)
                for (var i = 1; i < Section.Length - 3; i++)
                {
                    var a = new Vector3(side * Section[i].x, Section[i].y, z);
                    var b = new Vector3(side * Section[i + 1].x, Section[i + 1].y, z);
                    var s = (Section[i + 1] - Section[i]).normalized;
                    var inward = new Vector3(-side * s.y, s.x, 0f);
                    if (i == 1)
                        continue;
                    var fa = a + inward * depth;
                    var fb = b + inward * depth;
                    m.Quad(fa + Vector3.back * hw, fa + Vector3.forward * hw, fb + Vector3.forward * hw, fb + Vector3.back * hw, inward,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
                    m.Quad(a + Vector3.forward * hw, fa + Vector3.forward * hw, fb + Vector3.forward * hw, b + Vector3.forward * hw, Vector3.forward,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.6f, 0.9f, 0.9f, 0.6f);
                    m.Quad(a + Vector3.back * hw, fa + Vector3.back * hw, fb + Vector3.back * hw, b + Vector3.back * hw, Vector3.back,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.6f, 0.9f, 0.9f, 0.6f);
                }

            // Across the ceiling between the two chamfers.
            var l = new Vector3(-(HalfWidth - 0.55f), CeilingY, z);
            var r = new Vector3(HalfWidth - 0.55f, CeilingY, z);
            m.Quad(l + new Vector3(0f, -depth, -hw), r + new Vector3(0f, -depth, -hw), r + new Vector3(0f, -depth, hw), l + new Vector3(0f, -depth, hw),
                Vector3.down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
        }

        void Porthole(ShellMesh frames, ShellMesh glow, int side, float z)
        {
            var x = side * HalfWidth;
            var n = new Vector3(-side, 0f, 0f);
            var h = PortholeSize * 0.5f;
            Vector3 P(Vector2 p, float inset) => new(x + n.x * inset, p.y, p.x);
            var inner = BridgeShell.Chamfered(z - h, z + h, PortholeY - h, PortholeY + h, 0.2f);
            var outer = BridgeShell.Chamfered(z - h - 0.14f, z + h + 0.14f, PortholeY - h - 0.14f, PortholeY + h + 0.14f, 0.3f);
            var lip = BridgeShell.Chamfered(z - h - 0.02f, z + h + 0.02f, PortholeY - h - 0.02f, PortholeY + h + 0.02f, 0.21f);
            var centre = new Vector3(x, PortholeY, z);
            for (var i = 0; i < 8; i++)
            {
                var j = (i + 1) % 8;
                frames.Quad(P(outer[i], 0.09f), P(outer[j], 0.09f), P(inner[j], 0.09f), P(inner[i], 0.09f), n,
                    outer[i], outer[j], inner[j], inner[i], 1f, 1f, 0.9f, 0.9f);
                var mid = (P(outer[i], 0f) + P(outer[j], 0f)) * 0.5f - centre;
                frames.Quad(P(outer[i], 0f), P(outer[j], 0f), P(outer[j], 0.09f), P(outer[i], 0.09f), new Vector3(0f, mid.y, mid.z),
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.55f, 0.55f, 0.9f, 0.9f);
                var imid = centre - (P(inner[i], 0f) + P(inner[j], 0f)) * 0.5f;
                frames.Quad(P(inner[i], 0.09f), P(inner[j], 0.09f), P(inner[j], -0.35f), P(inner[i], -0.35f), new Vector3(0f, imid.y, imid.z),
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.9f, 0.9f, 0.4f, 0.4f);
                glow.Quad(P(lip[i], 0.092f), P(lip[j], 0.092f), P(inner[j], 0.092f), P(inner[i], 0.092f), n,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
            }
        }

        static void Strip(ShellMesh m, Vector3 a, Vector3 b, Vector3 normal)
        {
            m.Quad(new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z), normal,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
        }

        MeshRenderer Part(string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return r;
        }

        void Box(Vector3 centre, Vector3 size)
        {
            var go = new GameObject("HullCollider");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = centre;
            go.AddComponent<BoxCollider>().size = size;
        }

        void BuildLights()
        {
            var names = new[] { "CorridorA", "CorridorB", "CorridorC", "CorridorD" };
            for (var i = 0; i < names.Length; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 2.9f, 1.6f + i * 4f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.72f, 0.86f, 1f);
                l.intensity = 1.05f;
                l.range = 5.5f;
                l.shadows = LightShadows.None;
            }

            RoomLightRig.Attach(transform, names).Radius = Length;
        }

        /// <summary>Ship corridor = cyan, station corridor = amber (like the bridge dressing).</summary>
        void ApplyDressing()
        {
            var station = _focus == null || _focus.ViewFleetId <= 0;
            if (_accents == null || station == _station && _accents[0].sharedMaterial != null && _dressed)
                return;
            _station = station;
            _dressed = true;
            var accent = station ? CicArtKit.Amber : CicArtKit.Cyan;
            _accents[0].sharedMaterial = _art.Lit(Texture2D.whiteTexture, accent, station ? 1.1f : 1.2f);
            _accents[1].sharedMaterial = _art.Lit(Texture2D.whiteTexture, accent, 1f);
        }

        bool _dressed;

        // ── Walking in and out ───────────────────────────────────────────────────

        /// <summary>From the bridge's aft door.</summary>
        public async Task Enter()
        {
            if (DiplomacyRoom.AnyRoomInside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            Arrive(Stand, Vector3.forward);
            await fade.FadeIn();
            CicCue.Ok(transform.position + Vector3.up);
        }

        /// <summary>
        /// Back from a room (called by that room while the view is faded out): stand in front of its door, facing
        /// down the corridor's axis away from it.
        /// </summary>
        public void ReturnFrom(Slot slot)
        {
            var (pos, yaw) = DoorPose(slot);
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Arrive(pos + forward * 0.95f, forward);
        }

        /// <summary>Into a room from here (called by that room while faded out).</summary>
        public void Depart()
        {
            Inside = false;
            // Unhook the player first: a rig left under the hall would be switched off with it (and then
            // be invisible to FindFirstObjectByType for the room that takes over).
            var rig = GetComponentInChildren<XROrigin>();
            if (rig != null)
                rig.transform.SetParent(null, true);
            gameObject.SetActive(false);
        }

        /// <summary>Move the hall over the ship first, then put the head on a hall-local spot (never the other way round).</summary>
        void Arrive(Vector3 localHead, Vector3 localFacing)
        {
            PlaceOverShip();
            ApplyDressing();
            gameObject.SetActive(true);
            var rig = FindFirstObjectByType<XROrigin>();
            if (rig != null)
            {
                rig.transform.SetParent(transform, false);
                rig.transform.localPosition = Stand;
                rig.transform.localRotation = Quaternion.identity;
                XrPlacement.PlaceHead(rig, transform.TransformPoint(localHead), transform.TransformDirection(localFacing));
            }

            Inside = true;
        }

        void PlaceOverShip() => RoomPlacement.OverShip(transform, Vector3.right);

        async Task LeaveToBridge()
        {
            if (!Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            Depart();
            var rig = FindFirstObjectByType<XROrigin>();
            var bridge = FindFirstObjectByType<BridgeViewRig>();
            if (rig != null && bridge != null && bridge.BridgeMount != null)
            {
                rig.transform.SetParent(bridge.BridgeMount, false);
                bridge.PutPlayerOnDeck();
                // In through the aft door, not teleported to the chair.
                XrPlacement.PlaceHead(rig, bridge.BridgeMount.TransformPoint(new Vector3(0f, 0f, -WorldScale.CicDeck * 0.5f + 1.1f)),
                    bridge.BridgeMount.forward);
            }

            await fade.FadeIn();
        }

        /// <summary>For a room's Leave(): into the corridor if there is one, else straight onto the bridge.</summary>
        public static void ReturnPlayer(Slot slot)
        {
            if (Instance != null)
            {
                Instance.ReturnFrom(slot);
                return;
            }

            var rig = FindFirstObjectByType<XROrigin>();
            var bridge = FindFirstObjectByType<BridgeViewRig>();
            if (rig != null && bridge != null && bridge.BridgeMount != null)
            {
                rig.transform.SetParent(bridge.BridgeMount, false);
                bridge.PutPlayerOnDeck();
            }
        }
    }
}
