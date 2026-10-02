using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Our orbital station seen from its own windows (and from the concourse in its ring): a hub whose crown is
    /// the command rotunda, a habitat ring round it <see cref="WorldScale.StationRingDrop"/> under the deck, four
    /// spokes, a docking spire under the hub and blinking navigation lights. Surfaces of revolution on two shared
    /// materials (SU/HullMetal with its lit portholes on the ring's flanks, an unlit emissive for glazing and
    /// light lines) plus one blinking beacon material: five draw calls. Hub-local metres; the hub's axis is the
    /// rotunda's centre. Built once, on the first station view, under the bridge's ride (it moves with the view).
    /// </summary>
    public sealed class StationExterior : MonoBehaviour
    {
        static Material _hull;
        static Material _dark;
        static Material _glass;
        static Material _beacon;
        static readonly int EmissionMulId = Shader.PropertyToID("_EmissionMul");

        public static StationExterior Build(Transform ride)
        {
            var go = new GameObject("StationExterior");
            go.transform.SetParent(ride, false);
            var ext = go.AddComponent<StationExterior>();
            ext.BuildHub();
            ext.BuildRing();
            ext.BuildSpokes();
            ext.BuildBeacons();
            return ext;
        }

        static Vector3 Axis => new(0f, 0f, WorldScale.CicTableCenterZ);

        static Material HullMat()
        {
            if (_hull != null)
                return _hull;
            var shader = Shader.Find("SU/HullMetal") ?? Shader.Find("SU/UnlitEmissive");
            _hull = new Material(shader) { name = "SU_StationHull" };
            var tex = Resources.Load<Texture2D>("Ships/Hull");
            if (tex != null)
            {
                _hull.mainTexture = tex;
                _hull.mainTextureScale = Vector2.one * 0.3f;
            }

            Set(_hull, "_Color", new Color(2.3f, 2.35f, 2.5f));
            Set(_hull, "_RimColor", new Color(0.55f, 0.9f, 1f) * 0.45f);
            Set(_hull, "_PanelScale", 0.32f);
            Set(_hull, "_Groove", 0.1f);
            Set(_hull, "_Wear", 0.06f);
            Set(_hull, "_Windows", 2.2f);
            Set(_hull, "_WindowColor", new Color(0.75f, 0.92f, 1f));
            Set(_hull, "_WindowScale", 0.42f);
            return _hull;
        }

        static Material DarkMat()
        {
            if (_dark != null)
                return _dark;
            _dark = new Material(HullMat()) { name = "SU_StationHullDark", mainTextureScale = Vector2.one * 0.12f };
            Set(_dark, "_Color", new Color(1.5f, 1.58f, 1.72f));
            Set(_dark, "_PanelScale", 0.5f);
            Set(_dark, "_Windows", 0f);
            return _dark;
        }

        static Material GlassMat()
        {
            if (_glass != null)
                return _glass;
            var shader = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            _glass = new Material(shader) { name = "SU_StationGlazing" };
            Set(_glass, "_Color", StationCommandShell.Glow * 0.6f);
            Set(_glass, "_Emission", StationCommandShell.Glow);
            _glass.SetFloat(EmissionMulId, 0.45f);
            return _glass;
        }

        static Material BeaconMat()
        {
            if (_beacon != null)
                return _beacon;
            _beacon = new Material(GlassMat()) { name = "SU_StationBeacon" };
            Set(_beacon, "_Color", new Color(1f, 0.35f, 0.25f));
            Set(_beacon, "_Emission", new Color(1f, 0.35f, 0.25f));
            return _beacon;
        }

        static void Set(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop))
                m.SetColor(prop, c);
        }

        static void Set(Material m, string prop, float f)
        {
            if (m.HasProperty(prop))
                m.SetFloat(prop, f);
        }

        // ── Hub ─────────────────────────────────────────────────────────────────

        void BuildHub()
        {
            // Walked from the spire's tip up the outside: every face looks out (inward = false).
            Vector2[] body =
            {
                new(0.4f, -52f), new(1.8f, -47f), new(3.6f, -40f), new(4.4f, -30f), new(6.8f, -28.5f), new(6.8f, -24f),
                new(4.8f, -22f), new(4.8f, -12f), new(7.6f, -10.5f), new(9.6f, -8f), new(9.9f, -1.2f), new(9.9f, 3.7f),
                new(9.4f, 4.7f), new(8.1f, 5.7f), new(5.6f, 7.15f), new(3.2f, 7.85f), new(3.0f, 8.7f), new(1.3f, 9.1f),
                new(0.55f, 12.5f), new(0.18f, 21f)
            };
            var hub = new LatheMesh(Axis) { Step = 6f };
            // Flat-ish runs between the creases: one revolve per run keeps the creases crisp.
            int[] creases = { 0, 3, 5, 7, 9, 11, 13, 16, 19 };
            for (var k = 0; k < creases.Length - 1; k++)
            {
                var run = new Vector2[creases[k + 1] - creases[k] + 1];
                System.Array.Copy(body, creases[k], run, 0, run.Length);
                hub.Revolve(run, 0f, 360f, false);
            }

            LatheMesh.Part(transform, "Hub", hub.ToMesh("SU_StationHub"), DarkMat());

            // The command deck's bays, lit from inside (seen from the ring), and light lines round the hub.
            var glass = new LatheMesh(Axis) { Step = 4f };
            foreach (var w in new[] { new Vector2(24f, 66f), new Vector2(114f, 148f), new Vector2(212f, 246f), new Vector2(294f, 336f) })
                glass.Revolve(new[] { new Vector2(9.92f, 0.45f), new Vector2(9.92f, 2.95f) }, w.x, w.y, false);
            var lines = new LatheMesh(Axis) { Step = 4f };
            foreach (var y in new[] { -7.4f, -24.6f, 3.95f })
            {
                var r = y < -20f ? 6.82f : y < 0f ? 9.66f : 9.82f;
                lines.Revolve(new[] { new Vector2(r, y), new Vector2(r, y + 0.18f) }, 0f, 360f, false);
            }

            LatheMesh.Part(transform, "HubLights", lines.ToMesh("SU_StationHubLights"), GlassMat());
            var bays = new Material(GlassMat()) { name = "SU_StationHubBays" };
            Set(bays, "_Color", new Color(0.16f, 0.32f, 0.42f));
            bays.SetFloat(EmissionMulId, 0.22f);
            LatheMesh.Part(transform, "HubGlazing", glass.ToMesh("SU_StationHubGlazing"), bays);
        }

        // ── Ring ────────────────────────────────────────────────────────────────

        void BuildRing()
        {
            var rc = WorldScale.StationRingRadius;
            var half = WorldScale.StationRingSection * 0.5f;
            var yc = -WorldScale.StationRingDrop;
            const float corner = 1.8f;
            // Rounded-rectangle section, walked so the faces look out of the tube: bottom outward, up the outer
            // flank, back across the roof, down the inner flank (toward the hub), and home along the floor.
            var pts = new System.Collections.Generic.List<Vector2> { new(rc, yc - half.y) };
            void Corner(Vector2 c, float a0, float a1)
            {
                for (var i = 0; i <= 4; i++)
                {
                    var a = Mathf.Lerp(a0, a1, i / 4f) * Mathf.Deg2Rad;
                    pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * corner);
                }
            }

            Corner(new Vector2(rc + half.x - corner, yc - half.y + corner), -90f, 0f);
            Corner(new Vector2(rc + half.x - corner, yc + half.y - corner), 0f, 90f);
            Corner(new Vector2(rc - half.x + corner, yc + half.y - corner), 90f, 180f);
            Corner(new Vector2(rc - half.x + corner, yc - half.y + corner), 180f, 270f);
            pts.Add(new Vector2(rc, yc - half.y));

            var ring = new LatheMesh(Axis) { Step = 2.5f };
            ring.Revolve(pts.ToArray(), 0f, 360f, false);
            // Frames round the tube every 7.5° (a hand's breadth proud): they give the ring its scale.
            var ribs = new LatheMesh(Axis) { Step = 0.6f };
            var proud = new Vector2[pts.Count];
            for (var i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                var c = new Vector2(rc, yc);
                proud[i] = c + (p - c) + (p - c).normalized * 0.28f;
            }

            for (var k = 0; k < 48; k++)
                ribs.Revolve(proud, k * 7.5f - 0.5f, k * 7.5f + 0.5f, false);
            LatheMesh.Part(transform, "Ring", ring.ToMesh("SU_StationRing"), HullMat());
            LatheMesh.Part(transform, "RingFrames", ribs.ToMesh("SU_StationRingFrames"), DarkMat());

            // The concourse windows on the inner flank (lit warm from inside, they look at the hub), one pane per
            // bay between the frames, and light lines along the roof edges.
            var panes = new LatheMesh(Axis) { Step = 2.5f };
            var inner = rc - half.x - 0.03f;
            for (var k = 0; k < 48; k++)
                panes.Revolve(new[] { new Vector2(inner, yc - 1.5f), new Vector2(inner, yc + 1.7f) }, k * 7.5f + 0.9f, k * 7.5f + 6.6f, true,
                    LatheMesh.Uv.Normalised);
            LatheMesh.Part(transform, "RingWindows", panes.ToMesh("SU_StationRingWindows"), PaneMat());
            var glass = new LatheMesh(Axis) { Step = 2.5f };
            foreach (var r in new[] { rc - half.x + 1.2f, rc + half.x - 1.2f })
                glass.Revolve(new[] { new Vector2(r + 0.15f, yc + half.y + 0.03f), new Vector2(r - 0.15f, yc + half.y + 0.03f) }, 0f, 360f, false);
            LatheMesh.Part(transform, "RingGlazing", glass.ToMesh("SU_StationRingGlazing"), GlassMat());
        }

        static Material _pane;

        static Material PaneMat()
        {
            if (_pane != null)
                return _pane;
            _pane = new Material(GlassMat()) { name = "SU_StationRingPanes", mainTexture = StationScreens.RingPane() };
            Set(_pane, "_Color", Color.white * 1.25f);
            Set(_pane, "_Emission", Color.black);
            _pane.SetFloat(EmissionMulId, 0f);
            return _pane;
        }

        // ── Spokes ──────────────────────────────────────────────────────────────

        void BuildSpokes()
        {
            var m = new LatheMesh(Axis);
            var r0 = 9.2f;
            var r1 = WorldScale.StationRingRadius - WorldScale.StationRingSection.x * 0.5f + 0.4f;
            var y = -WorldScale.StationRingDrop;
            foreach (var deg in new[] { 45f, 135f, 225f, 315f })
            {
                var d = LatheMesh.Dir(deg);
                Tube(m, Axis + d * r0 + Vector3.up * y, Axis + d * r1 + Vector3.up * y, 1.35f, 10);
                // Collars at both ends and an elevator housing halfway.
                Tube(m, Axis + d * (r1 - 2.2f) + Vector3.up * y, Axis + d * r1 + Vector3.up * y, 2.1f, 10);
                Tube(m, Axis + d * r0 + Vector3.up * y, Axis + d * (r0 + 1.6f) + Vector3.up * y, 2.0f, 10);
                var mid = (r0 + r1) * 0.5f;
                Tube(m, Axis + d * (mid - 1.6f) + Vector3.up * y, Axis + d * (mid + 1.6f) + Vector3.up * y, 1.9f, 10);
            }

            // Docking arms under the hub, three ships' berths.
            foreach (var deg in new[] { 30f, 150f, 270f })
            {
                var d = LatheMesh.Dir(deg);
                var a = Axis + d * 6.4f + Vector3.up * -26.2f;
                var b = Axis + d * 17f + Vector3.up * -26.2f;
                Tube(m, a, b, 0.9f, 8);
                Tube(m, b - d * 1.2f, b + d * 0.6f, 1.5f, 8);
            }

            LatheMesh.Part(transform, "Spokes", m.ToMesh("SU_StationSpokes"), DarkMat());
        }

        static void Tube(LatheMesh m, Vector3 a, Vector3 b, float radius, int sides)
        {
            var axis = (b - a).normalized;
            var u = Vector3.Cross(axis, Vector3.up).normalized;
            if (u.sqrMagnitude < 0.5f)
                u = Vector3.right;
            var v = Vector3.Cross(u, axis);
            for (var i = 0; i < sides; i++)
            {
                var a0 = i * Mathf.PI * 2f / sides;
                var a1 = (i + 1) * Mathf.PI * 2f / sides;
                var o0 = (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * radius;
                var o1 = (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * radius;
                m.Quad(a + o0, a + o1, b + o1, b + o0, (o0 + o1).normalized, 1f);
                m.Tri(a, a + o1, a + o0, -axis, 0.8f);
                m.Tri(b, b + o0, b + o1, axis, 0.8f);
            }
        }

        // ── Navigation lights ───────────────────────────────────────────────────

        Material _blink;

        void BuildBeacons()
        {
            var m = new LatheMesh(Axis);
            void Lamp(Vector3 p, float s)
            {
                foreach (var n in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                {
                    var t1 = Vector3.Cross(n, n == Vector3.up || n == Vector3.down ? Vector3.forward : Vector3.up).normalized * s;
                    var t2 = Vector3.Cross(n, t1).normalized * s;
                    var c = p + n * s;
                    m.Quad(c - t1 - t2, c + t1 - t2, c + t1 + t2, c - t1 + t2, n, 1f);
                }
            }

            Lamp(Axis + Vector3.up * 21.2f, 0.35f);
            var rc = WorldScale.StationRingRadius;
            var top = -WorldScale.StationRingDrop + WorldScale.StationRingSection.y * 0.5f + 0.3f;
            for (var k = 0; k < 8; k++)
                Lamp(Axis + LatheMesh.Dir(k * 45f + 22.5f) * (rc + WorldScale.StationRingSection.x * 0.5f - 0.6f) + Vector3.up * top, 0.3f);
            foreach (var deg in new[] { 30f, 150f, 270f })
                Lamp(Axis + LatheMesh.Dir(deg) * 17.8f + Vector3.up * -26.2f, 0.28f);
            LatheMesh.Part(transform, "Beacons", m.ToMesh("SU_StationBeacons"), BeaconMat());
            _blink = BeaconMat();
        }

        void Update()
        {
            if (_blink == null)
                return;
            // A double flash every 2.4 s.
            var t = Time.time % 2.4f;
            var on = t < 0.08f || t > 0.28f && t < 0.36f;
            _blink.SetFloat(EmissionMulId, on ? 3.2f : 0.12f);
        }
    }
}
