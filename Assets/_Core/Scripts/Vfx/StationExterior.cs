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
    /// The fortress's own fitting (web 69d40af station modules: batteries, shield projectors, jammers, gantries,
    /// citadel reactors…) stands on the ring, each module its <see cref="ShipHullBuilder.ModuleMesh"/> silhouette
    /// at its grid bearing — merged into one mesh and one draw call, rebuilt only when the layout changes.
    /// </summary>
    public sealed class StationExterior : MonoBehaviour
    {
        static Material _hull;
        static Material _dark;
        static Material _glass;
        static Material _beacon;
        static Material _cHull;
        static Material _cDark;
        static Material _cGlass;
        static Material _cBeacon;

        /// <summary>
        /// A city's crown (<see cref="CityExterior"/>): the tower's flared head in warm stone-metal, the citadel
        /// gallery's arched windows in its flank (<see cref="BuildGalleryWindows"/>), a broad terrace on top, and on
        /// it the palace that holds the throne hall (<see cref="CitadelHall"/>) — aisles and clerestory, buttresses,
        /// a rose on the west front, an apse, a gilt flèche (<see cref="BuildPalace"/>). No ring, spokes or arms.
        /// </summary>
        bool _citadel;
        static readonly int EmissionMulId = Shader.PropertyToID("_EmissionMul");

        public static StationExterior Build(Transform ride, bool citadel = false)
        {
            var go = new GameObject(citadel ? "CitadelCrown" : "StationExterior");
            go.transform.SetParent(ride, false);
            var ext = go.AddComponent<StationExterior>();
            ext._citadel = citadel;
            if (citadel)
            {
                ext.BuildCrownBase();
                ext.BuildGalleryWindows();
                ext.BuildPalace();
            }
            else
            {
                ext.BuildHub();
                ext.BuildRing();
                ext.BuildSpokes();
            }

            ext.BuildBeacons();
            return ext;
        }

        static Vector3 Axis => new(0f, 0f, WorldScale.CicTableCenterZ);

        /// <summary>Foot of the citadel's crown (hub profile cut here; the tower's shaft continues it down).</summary>
        public const float CrownFoot = -10.5f;

        Material Hull() => _citadel ? CitadelHull() : HullMat();
        Material Dark() => _citadel ? CitadelDark() : DarkMat();
        Material Glass() => _citadel ? CitadelGlass() : GlassMat();
        Material Beacon() => _citadel ? CitadelBeacon() : BeaconMat();

        /// <summary>Citadel stone-metal: warm pale plating, gold rim, lit windows warm.</summary>
        public static Material CitadelHull()
        {
            if (_cHull != null)
                return _cHull;
            _cHull = new Material(HullMat()) { name = "SU_CitadelHull" };
            Set(_cHull, "_Color", new Color(2.55f, 2.4f, 2.15f));
            Set(_cHull, "_RimColor", CityExterior.CitadelGold * 0.4f);
            Set(_cHull, "_WindowColor", new Color(1f, 0.84f, 0.58f));
            Set(_cHull, "_Windows", 2.6f);
            return _cHull;
        }

        public static Material CitadelDark()
        {
            if (_cDark != null)
                return _cDark;
            _cDark = new Material(DarkMat()) { name = "SU_CitadelHullDark" };
            Set(_cDark, "_Color", new Color(1.75f, 1.62f, 1.45f));
            Set(_cDark, "_RimColor", CityExterior.CitadelGold * 0.25f);
            return _cDark;
        }

        public static Material CitadelGlass()
        {
            if (_cGlass != null)
                return _cGlass;
            _cGlass = new Material(GlassMat()) { name = "SU_CitadelGlazing" };
            Set(_cGlass, "_Color", CityExterior.CitadelGold * 0.6f);
            Set(_cGlass, "_Emission", CityExterior.CitadelGold);
            _cGlass.SetFloat(EmissionMulId, 0.55f);
            return _cGlass;
        }

        static Material _cStone;

        /// <summary>The palace's dressed stone: the citadel plating without its hull windows (the lancets are its windows).</summary>
        static Material PalaceStone()
        {
            if (_cStone != null)
                return _cStone;
            _cStone = new Material(CitadelHull()) { name = "SU_CitadelPalaceStone" };
            Set(_cStone, "_Windows", 0f);
            Set(_cStone, "_PanelScale", 0.6f);
            return _cStone;
        }

        static Material CitadelBeacon()
        {
            if (_cBeacon != null)
                return _cBeacon;
            _cBeacon = new Material(BeaconMat()) { name = "SU_CitadelBeacon" };
            return _cBeacon;
        }

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
                new(4.8f, -22f), new(4.8f, -12f), new(7.6f, -10.5f), new(9.6f, -8f), new(9.9f, -1.2f), new(9.9f, 5.7f),
                new(9.4f, 6.7f), new(8.1f, 7.7f), new(5.6f, 9.15f), new(3.2f, 9.85f), new(3.0f, 10.7f), new(1.3f, 11.1f),
                new(0.55f, 14.5f), new(0.18f, 23f)
            };
            var hub = new LatheMesh(Axis) { Step = 6f };
            // Flat-ish runs between the creases: one revolve per run keeps the creases crisp. A citadel's crown
            // starts at its tower (index 8: (7.6, −10.5)), the spire and docking collar are a fortress's.
            int[] creases = _citadel ? new[] { 8, 9, 11, 13, 16, 19 } : new[] { 0, 3, 5, 7, 9, 11, 13, 16, 19 };
            for (var k = 0; k < creases.Length - 1; k++)
            {
                var run = new Vector2[creases[k + 1] - creases[k] + 1];
                System.Array.Copy(body, creases[k], run, 0, run.Length);
                hub.Revolve(run, 0f, 360f, false);
            }

            LatheMesh.Part(transform, "Hub", hub.ToMesh("SU_StationHub"), Dark());

            // The command deck's bays, lit from inside (seen from the ring), and light lines round the hub.
            var glass = new LatheMesh(Axis) { Step = 4f };
            foreach (var w in new[] { new Vector2(24f, 66f), new Vector2(114f, 148f), new Vector2(212f, 246f), new Vector2(294f, 336f) })
                glass.Revolve(new[] { new Vector2(9.92f, 0.45f), new Vector2(9.92f, 2.95f) }, w.x, w.y, false);
            var lines = new LatheMesh(Axis) { Step = 4f };
            foreach (var y in _citadel ? new[] { -7.4f, 3.95f, 5.95f } : new[] { -7.4f, -24.6f, 3.95f, 5.95f })
            {
                var r = y < -20f ? 6.82f : y < 0f ? 9.66f : 9.82f;
                lines.Revolve(new[] { new Vector2(r, y), new Vector2(r, y + 0.18f) }, 0f, 360f, false);
            }

            LatheMesh.Part(transform, "HubLights", lines.ToMesh("SU_StationHubLights"), Glass());
            var bays = new Material(Glass()) { name = "SU_StationHubBays" };
            Set(bays, "_Color", new Color(0.16f, 0.32f, 0.42f));
            bays.SetFloat(EmissionMulId, 0.22f);
            LatheMesh.Part(transform, "HubGlazing", glass.ToMesh("SU_StationHubGlazing"), bays);
        }

        // ── Citadel crown: the tower's head and its terrace ──────────────────────

        /// <summary>Terrace (the palace's ground) over the tower's flared head.</summary>
        const float TerraceR = 16.2f;
        /// <summary>The crown's flank radius at the gallery's floor (it leans out 0.3 m up to the terrace).</summary>
        const float HeadR = Core.Stations.CitadelGallery.ROut + 0.65f;
        const float TerraceY = -0.62f;

        void BuildCrownBase()
        {
            var head = new LatheMesh(Axis) { Step = 6f };
            Vector2[][] runs =
            {
                // The head flares wide enough to hold the gallery (CitadelGallery.ROut + its reveals) under the terrace.
                new[] { new Vector2(7.6f, CrownFoot), new Vector2(HeadR, -8.6f) },
                new[] { new Vector2(HeadR, -8.6f), new Vector2(HeadR + 0.3f, -1.2f) },
                new[] { new Vector2(HeadR + 0.3f, -1.2f), new Vector2(TerraceR, -1.0f) },
                new[] { new Vector2(TerraceR, -1.0f), new Vector2(TerraceR, TerraceY) },
                new[] { new Vector2(TerraceR, TerraceY), new Vector2(0f, TerraceY) }
            };
            foreach (var run in runs)
                head.Revolve(run, 0f, 360f, false);
            LatheMesh.Part(transform, "Hub", head.ToMesh("SU_CitadelCrown"), Dark());

            // Light lines round the head and the terrace's lit edge, a parapet of merlons round it.
            var lines = new LatheMesh(Axis) { Step = 4f };
            lines.Revolve(new[] { new Vector2(HeadR + 0.07f, -8.3f), new Vector2(HeadR + 0.07f, -8.12f) }, 0f, 360f, false);
            lines.Revolve(new[] { new Vector2(TerraceR + 0.01f, -0.92f), new Vector2(TerraceR + 0.01f, -0.82f) }, 0f, 360f, false);
            LatheMesh.Part(transform, "HubLights", lines.ToMesh("SU_CitadelCrownLights"), Glass());
            var parapet = new MeshBatch();
            for (var deg = 0f; deg < 360f; deg += 6f)
                parapet.Box(Axis + LatheMesh.Dir(deg) * (TerraceR - 0.15f) + Vector3.up * (TerraceY + 0.45f), new Vector3(0.7f, 0.9f, 0.3f), Dark(),
                    Quaternion.LookRotation(LatheMesh.Dir(deg)));
            parapet.Build(transform, "Parapet");
        }

        // ── Citadel palace (the throne hall's outside) ──────────────────────────

        /// <summary>
        /// The throne hall seen from the city: one-sided outward shells round <see cref="CitadelHall"/>'s volume
        /// (invisible from inside, where the hall's own walls are), its windows lit gold from within.
        /// </summary>
        void BuildPalace()
        {
            const float wx = CitadelHall.WallX + 0.3f;
            const float nx = CitadelHall.NaveX + 0.75f;
            const float z0 = CitadelHall.NarthexZ - 0.55f;
            const float z1 = CitadelHall.ApseZ;
            const float y0 = TerraceY;
            const float eaves = 7.9f;
            const float lean = 8.8f;
            const float naveTop = 11.0f;
            const float ridge = 14.8f;
            const float apseR = CitadelHall.ApseR + 0.5f;
            var walls = new LatheMesh(Vector3.zero) { UvOf = p => new Vector2(p.x + p.z, p.y) };
            var roofs = new LatheMesh(Vector3.zero) { UvOf = p => new Vector2(p.x + p.y, p.z) };
            var panes = new LatheMesh(Vector3.zero);
            var gold = new LatheMesh(Vector3.zero);

            foreach (var s in new[] { -1f, 1f })
            {
                var o = new Vector3(s, 0f, 0f);
                // Aisle wall, clerestory wall, the aisle's lean-to roof.
                walls.Quad(new Vector3(s * wx, y0, z0), new Vector3(s * wx, y0, z1), new Vector3(s * wx, eaves, z1), new Vector3(s * wx, eaves, z0), o, 1f);
                walls.Quad(new Vector3(s * nx, lean - 0.1f, z0), new Vector3(s * nx, lean - 0.1f, z1), new Vector3(s * nx, naveTop, z1),
                    new Vector3(s * nx, naveTop, z0), o, 1f);
                roofs.Quad(new Vector3(s * (wx + 0.35f), eaves - 0.2f, z0 - 0.3f), new Vector3(s * (wx + 0.35f), eaves - 0.2f, z1),
                    new Vector3(s * nx, lean, z1), new Vector3(s * nx, lean, z0 - 0.3f), Vector3.up + o * 0.3f, 1f);
                // Nave roof slope, gold cresting along the eaves.
                roofs.Quad(new Vector3(s * (nx + 0.4f), naveTop - 0.25f, z0 - 0.4f), new Vector3(s * (nx + 0.4f), naveTop - 0.25f, z1 + 0.2f),
                    new Vector3(0f, ridge, z1 + 0.2f), new Vector3(0f, ridge, z0 - 0.4f), Vector3.up + o * 0.5f, 1f);
                gold.Quad(new Vector3(s * (nx + 0.41f), naveTop - 0.33f, z0 - 0.4f), new Vector3(s * (nx + 0.41f), naveTop - 0.33f, z1 + 0.2f),
                    new Vector3(s * (nx + 0.41f), naveTop - 0.22f, z1 + 0.2f), new Vector3(s * (nx + 0.41f), naveTop - 0.22f, z0 - 0.4f), o, 1f);

                // The east face between the aisle's wall and the apse, and between the clerestory and the apse.
                walls.Quad(new Vector3(s * wx, y0, z1), new Vector3(s * apseR, y0, z1), new Vector3(s * apseR, eaves, z1), new Vector3(s * wx, eaves, z1),
                    Vector3.forward, 1f);
                walls.Quad(new Vector3(s * nx, eaves, z1), new Vector3(s * apseR, eaves, z1), new Vector3(s * apseR, naveTop, z1), new Vector3(s * nx, naveTop, z1),
                    Vector3.forward, 1f);
                walls.Tri(new Vector3(s * wx, eaves, z1), new Vector3(s * nx, eaves, z1), new Vector3(s * nx, lean, z1), Vector3.forward, 1f);
                walls.Tri(new Vector3(s * wx, eaves, z0), new Vector3(s * nx, eaves, z0), new Vector3(s * nx, lean, z0), Vector3.back, 1f);

                // Lancets: aisle and tribune on the outer wall, the clerestory above.
                for (var i = 0; i < Piers.Length - 1; i++)
                {
                    var zc = (Piers[i] + Piers[i + 1]) * 0.5f;
                    var bay = Piers[i + 1] - Piers[i];
                    Lancet(panes, gold, new Vector3(s * (wx + 0.01f), 0f, zc), Vector3.forward, o, Mathf.Min(0.62f, bay * 0.28f), 0.8f, 3.5f);
                    Lancet(panes, gold, new Vector3(s * (wx + 0.01f), 0f, zc), Vector3.forward, o, Mathf.Min(0.42f, bay * 0.2f), 5.8f, 6.5f);
                    Lancet(panes, gold, new Vector3(s * (nx + 0.01f), 0f, zc), Vector3.forward, o, Mathf.Min(0.55f, bay * 0.22f), 8.3f, 9.7f);
                }
            }

            // West front: the aisles' fronts, the nave's front and its gable, the rose; the east gable over the apse.
            walls.Quad(new Vector3(-wx, y0, z0), new Vector3(wx, y0, z0), new Vector3(wx, eaves, z0), new Vector3(-wx, eaves, z0), Vector3.back, 1f);
            walls.Quad(new Vector3(-nx, eaves, z0), new Vector3(nx, eaves, z0), new Vector3(nx, naveTop, z0), new Vector3(-nx, naveTop, z0), Vector3.back, 1f);
            walls.Tri(new Vector3(-nx - 0.4f, naveTop - 0.25f, z0 - 0.01f), new Vector3(nx + 0.4f, naveTop - 0.25f, z0 - 0.01f), new Vector3(0f, ridge, z0 - 0.01f),
                Vector3.back, 1f);
            walls.Tri(new Vector3(-nx - 0.4f, naveTop - 0.25f, z1 + 0.2f), new Vector3(nx + 0.4f, naveTop - 0.25f, z1 + 0.2f), new Vector3(0f, ridge, z1 + 0.2f),
                Vector3.forward, 1f);
            // Its back too: seen over the ridge, above the apse's roof.
            roofs.Tri(new Vector3(-nx - 0.4f, naveTop - 0.25f, z1 + 0.19f), new Vector3(nx + 0.4f, naveTop - 0.25f, z1 + 0.19f), new Vector3(0f, ridge, z1 + 0.19f),
                Vector3.back, 0.7f);
            Rose(panes, gold, new Vector3(0f, 8.4f, z0 - 0.02f), 1.9f);
            Lancet(panes, gold, new Vector3(0f, 0f, z0 - 0.02f), Vector3.right, Vector3.back, 1.0f, y0 + 0.1f, 2.9f);

            // The apse: its drum, a ribbed half-dome roof, five tall lancets.
            var apse = new LatheMesh(new Vector3(0f, 0f, z1));
            apse.Revolve(new[] { new Vector2(apseR, y0), new Vector2(apseR, naveTop) }, -90f, 90f, false);
            var dome = new Vector2[9];
            for (var i = 0; i < dome.Length; i++)
            {
                var t = i / (dome.Length - 1f);
                dome[i] = new Vector2((apseR + 0.3f) * Mathf.Cos(t * Mathf.PI * 0.5f), naveTop - 0.1f + 2.9f * Mathf.Sin(t * Mathf.PI * 0.5f));
            }

            apse.Revolve(dome, -90f, 90f, false);
            LatheMesh.Part(transform, "PalaceApse", apse.ToMesh("SU_CitadelPalaceApse"), Dark());
            foreach (var deg in new[] { -60f, -30f, 0f, 30f, 60f })
            {
                var d = LatheMesh.Dir(deg);
                Lancet(panes, gold, new Vector3(0f, 0f, z1) + d * (apseR + 0.01f), Vector3.Cross(Vector3.up, d) * -1f, d, 0.62f, 3.2f, 7.6f);
            }

            LatheMesh.Part(transform, "PalaceWalls", walls.ToMesh("SU_CitadelPalaceWalls"), PalaceStone());
            LatheMesh.Part(transform, "PalaceRoofs", roofs.ToMesh("SU_CitadelPalaceRoofs"), Dark());
            var lit = new Material(Glass()) { name = "SU_CitadelPalaceWindows" };
            Set(lit, "_Color", new Color(0.62f, 0.46f, 0.26f));
            lit.SetFloat(EmissionMulId, 0.55f);
            LatheMesh.Part(transform, "PalaceWindows", panes.ToMesh("SU_CitadelPalaceWindows"), lit);
            LatheMesh.Part(transform, "PalaceTracery", gold.ToMesh("SU_CitadelPalaceTracery"), Dark());

            // Buttresses at every pier: a stepped pier on the aisle wall with a pinnacle, a flyer to the clerestory.
            var b = new MeshBatch();
            foreach (var s in new[] { -1f, 1f })
                foreach (var z in Piers)
                {
                    var px = s * (wx + 0.4f);
                    b.Box(new Vector3(px, (y0 + 6f) * 0.5f, z), new Vector3(0.8f, 6f - y0, 0.7f), Dark());
                    b.Box(new Vector3(px - s * 0.1f, 7.4f, z), new Vector3(0.6f, 2.8f, 0.6f), Dark());
                    b.Strut(new Vector3(px, 8.8f, z), new Vector3(px, 10.6f, z), 0.32f, Dark());
                    b.Strut(new Vector3(px, 10.6f, z), new Vector3(px, 11.3f, z), 0.12f, Glass());
                    b.Strut(new Vector3(px - s * 0.2f, 8.4f, z), new Vector3(s * (nx + 0.05f), 10.3f, z), 0.3f, Dark());
                }

            // The flèche over the apse's chord: an octagonal gilt spire, lit rings, the beacon at its tip.
            var spire = new LatheMesh(new Vector3(0f, 0f, z1 - 0.6f)) { Step = 45f };
            spire.Revolve(new[]
            {
                new Vector2(1.1f, ridge - 0.8f), new Vector2(1.1f, ridge + 1.6f), new Vector2(0.8f, ridge + 1.9f), new Vector2(0.8f, ridge + 3.4f),
                new Vector2(0.55f, ridge + 3.6f), new Vector2(0.06f, ridge + 10.5f)
            }, 0f, 360f, false);
            LatheMesh.Part(transform, "Fleche", spire.ToMesh("SU_CitadelFleche"), Glass());
            b.Build(transform, "Buttresses");
        }

        /// <summary>The tip of the palace's flèche (exterior local metres), where a home world's mast rises.</summary>
        public static Vector3 FlecheTip => new(0f, 14.8f + 10.7f, CitadelHall.ApseZ - 0.6f);
        static float[] Piers => CitadelHall.Piers;

        /// <summary>A pointed lit pane at <paramref name="foot"/> (x/z), along <paramref name="along"/>, facing <paramref name="face"/>, gold-framed.</summary>
        static void Lancet(LatheMesh panes, LatheMesh gold, Vector3 foot, Vector3 along, Vector3 face, float half, float sill, float spring)
        {
            var apex = spring + half * 1.7f;
            Vector3 P(float u, float y, float lift = 0f) => new Vector3(foot.x, y, foot.z) + along * u + face * lift;
            float W(float y) => y <= spring ? half : half * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow((y - spring) / (apex - spring), 1.7f)), 0.62f);
            panes.Quad(P(-half, sill), P(half, sill), P(half, spring), P(-half, spring), face, 1f);
            var py = spring;
            var pw = half;
            for (var k = 1; k <= 6; k++)
            {
                var y = spring + (apex - spring) * k / 6f;
                var w = W(Mathf.Min(y, apex - 0.001f));
                panes.Quad(P(-pw, py), P(pw, py), P(w, y), P(-w, y), face, 1f);
                foreach (var sd in new[] { -1f, 1f })
                    gold.Quad(P(sd * pw, py, 0.01f), P(sd * (pw + 0.08f), py, 0.01f), P(sd * (w + 0.08f), y, 0.01f), P(sd * w, y, 0.01f), face, 1f);
                py = y;
                pw = w;
            }

            foreach (var sd in new[] { -1f, 1f })
                gold.Quad(P(sd * half, sill, 0.01f), P(sd * (half + 0.08f), sill, 0.01f), P(sd * (half + 0.08f), spring, 0.01f), P(sd * half, spring, 0.01f), face, 1f);
            gold.Quad(P(-0.03f, sill, 0.015f), P(0.03f, sill, 0.015f), P(0.03f, apex - 0.05f, 0.015f), P(-0.03f, apex - 0.05f, 0.015f), face, 1f);
            gold.Quad(P(-half - 0.12f, sill - 0.12f, 0.01f), P(half + 0.12f, sill - 0.12f, 0.01f), P(half + 0.12f, sill, 0.01f), P(-half - 0.12f, sill, 0.01f), face, 1f);
        }

        /// <summary>The west front's rose: a lit disc, gold spokes and rings, facing −z.</summary>
        static void Rose(LatheMesh panes, LatheMesh gold, Vector3 c, float r)
        {
            Vector3 P(float a, float rr, float lift = 0f) => c + new Vector3(Mathf.Sin(a) * rr, Mathf.Cos(a) * rr, -lift);
            for (var k = 0; k < 32; k++)
            {
                var a0 = k * Mathf.PI / 16f;
                var a1 = (k + 1) * Mathf.PI / 16f;
                panes.Tri(c, P(a0, r), P(a1, r), Vector3.back, 1f);
                gold.Quad(P(a0, r, 0.01f), P(a1, r, 0.01f), P(a1, r + 0.16f, 0.01f), P(a0, r + 0.16f, 0.01f), Vector3.back, 1f);
                gold.Quad(P(a0, r * 0.6f, 0.01f), P(a1, r * 0.6f, 0.01f), P(a1, r * 0.6f + 0.06f, 0.01f), P(a0, r * 0.6f + 0.06f, 0.01f), Vector3.back, 1f);
            }

            for (var k = 0; k < 12; k++)
            {
                var a = k * Mathf.PI / 6f;
                var n = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0f) * 0.035f;
                gold.Quad(P(a, 0.3f, 0.012f) - n, P(a, 0.3f, 0.012f) + n, P(a, r, 0.012f) + n, P(a, r, 0.012f) - n, Vector3.back, 1f);
            }
        }

        // ── Citadel gallery windows ─────────────────────────────────────────────

        /// <summary>
        /// The gallery's arched windows in the crown's flank, lit warm from inside (one per bay of
        /// <see cref="Core.Stations.CitadelGallery"/>, on the hub's own surface: nothing is added to the tower).
        /// One-sided, facing out: from inside the gallery they are not there, the city is.
        /// </summary>
        void BuildGalleryWindows()
        {
            const float deck = -Core.Stations.CitadelGallery.FloorBelowDeck;
            const float sill = deck + 0.8f;
            const float spring = deck + 2.55f;
            const float hw = 0.7f;
            const int arc = 10;
            float R(float y) => HeadR + 0.3f * Mathf.Clamp01((y + 8.6f) / 7.4f) + 0.035f;
            var panes = new LatheMesh(Axis);
            var rims = new LatheMesh(Axis);
            for (var deg = Core.Stations.CitadelGallery.A0 + Core.Stations.CitadelGallery.BayDeg * 0.5f; deg < Core.Stations.CitadelGallery.A1;
                 deg += Core.Stations.CitadelGallery.BayDeg)
            {
                var d = LatheMesh.Dir(deg);
                var side = Vector3.Cross(Vector3.up, d);
                Vector3 P(float u, float y, float lift = 0f) => Axis + d * (R(y) + lift) + side * u + Vector3.up * y;
                panes.Quad(P(-hw, sill), P(hw, sill), P(hw, spring), P(-hw, spring), d, 1f);
                for (var i = 0; i < arc; i++)
                {
                    var a0 = Mathf.PI * i / arc;
                    var a1 = Mathf.PI * (i + 1) / arc;
                    panes.Tri(P(0f, spring), P(-Mathf.Cos(a0) * hw, spring + Mathf.Sin(a0) * hw), P(-Mathf.Cos(a1) * hw, spring + Mathf.Sin(a1) * hw), d, 1f);
                    var o0 = new Vector2(-Mathf.Cos(a0) * (hw + 0.09f), spring + Mathf.Sin(a0) * (hw + 0.09f));
                    var o1 = new Vector2(-Mathf.Cos(a1) * (hw + 0.09f), spring + Mathf.Sin(a1) * (hw + 0.09f));
                    var i0 = new Vector2(-Mathf.Cos(a0) * hw, spring + Mathf.Sin(a0) * hw);
                    var i1 = new Vector2(-Mathf.Cos(a1) * hw, spring + Mathf.Sin(a1) * hw);
                    rims.Quad(P(i0.x, i0.y, 0.01f), P(i1.x, i1.y, 0.01f), P(o1.x, o1.y, 0.01f), P(o0.x, o0.y, 0.01f), d, 1f);
                }

                foreach (var u in new[] { -hw - 0.09f, hw })
                    rims.Quad(P(u, sill, 0.01f), P(u + 0.09f, sill, 0.01f), P(u + 0.09f, spring, 0.01f), P(u, spring, 0.01f), d, 1f);
                rims.Quad(P(-hw - 0.15f, sill - 0.1f, 0.01f), P(hw + 0.15f, sill - 0.1f, 0.01f), P(hw + 0.15f, sill, 0.01f), P(-hw - 0.15f, sill, 0.01f), d, 1f);
                // The tracery seen from outside: mullion and transom, dark against the lit pane.
                rims.Quad(P(-0.025f, sill, 0.015f), P(0.025f, sill, 0.015f), P(0.025f, spring + hw, 0.015f), P(-0.025f, spring + hw, 0.015f), d, 1f);
            }

            var lit = new Material(Glass()) { name = "SU_CitadelGalleryWindows" };
            Set(lit, "_Color", new Color(0.55f, 0.42f, 0.26f));
            lit.SetFloat(EmissionMulId, 0.45f);
            LatheMesh.Part(transform, "GalleryWindows", panes.ToMesh("SU_CitadelGalleryWindows"), lit);
            LatheMesh.Part(transform, "GalleryWindowFrames", rims.ToMesh("SU_CitadelGalleryFrames"), Dark());
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

            // A fortress only: a city's hall is inside its tower.
            const float a0 = 0f;
            const float a1 = 360f;
            var ring = new LatheMesh(Axis) { Step = 2.5f };
            ring.Revolve(pts.ToArray(), a0, a1, false);
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
            LatheMesh.Part(transform, "Ring", ring.ToMesh("SU_StationRing"), Hull());
            LatheMesh.Part(transform, "RingFrames", ribs.ToMesh("SU_StationRingFrames"), Dark());

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
                glass.Revolve(new[] { new Vector2(r + 0.15f, yc + half.y + 0.03f), new Vector2(r - 0.15f, yc + half.y + 0.03f) }, a0, a1, false);
            LatheMesh.Part(transform, "RingGlazing", glass.ToMesh("SU_StationRingGlazing"), Glass());
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

            // Docking arms under the hub, three ships' berths (a fortress only: the citadel's tower is under it).
            foreach (var deg in new[] { 30f, 150f, 270f })
            {
                var d = LatheMesh.Dir(deg);
                var a = Axis + d * 6.4f + Vector3.up * -26.2f;
                var b = Axis + d * 17f + Vector3.up * -26.2f;
                Tube(m, a, b, 0.9f, 8);
                Tube(m, b - d * 1.2f, b + d * 0.6f, 1.5f, 8);
            }

            LatheMesh.Part(transform, "Spokes", m.ToMesh("SU_StationSpokes"), Dark());
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

            Lamp(_citadel ? FlecheTip : Axis + Vector3.up * 21.2f, 0.35f);
            var rc = WorldScale.StationRingRadius;
            var top = -WorldScale.StationRingDrop + WorldScale.StationRingSection.y * 0.5f + 0.3f;
            if (!_citadel)
                for (var k = 0; k < 8; k++)
                    Lamp(Axis + LatheMesh.Dir(k * 45f + 22.5f) * (rc + WorldScale.StationRingSection.x * 0.5f - 0.6f) + Vector3.up * top, 0.3f);
            if (!_citadel)
                foreach (var deg in new[] { 30f, 150f, 270f })
                    Lamp(Axis + LatheMesh.Dir(deg) * 17.8f + Vector3.up * -26.2f, 0.28f);
            LatheMesh.Part(transform, "Beacons", m.ToMesh("SU_StationBeacons"), Beacon());
            _blink = Beacon();
        }

        // ── Fitted modules ──────────────────────────────────────────────────────

        const float ModuleScale = 3.2f;
        static Material _moduleMat;
        GameObject _fitted;
        Mesh _fittedMesh;
        string _fitSig;

        /// <summary>Shared material of fitted / distant fortress modules (SU/ModuleBlock, vertex-coloured silhouettes).</summary>
        public static Material ModuleMat()
        {
            if (_moduleMat != null)
                return _moduleMat;
            var shader = Shader.Find("SU/ModuleBlock") ?? Shader.Find("SU/HullInterior");
            _moduleMat = new Material(shader) { name = "SU_StationModules" };
            Set(_moduleMat, "_Accent", StationCommandShell.Glow);
            Set(_moduleMat, "_Sky", new Color(0.95f, 0.98f, 1.05f));
            Set(_moduleMat, "_Ground", new Color(0.42f, 0.46f, 0.52f));
            Set(_moduleMat, "_LightGain", 0.6f);
            return _moduleMat;
        }

        /// <summary>
        /// Set the fortress's modules on its ring: grid (4,4) is the hub itself, every other cell stands at its
        /// bearing round the hub (atan2 of its grid offset), farther cells farther out along the ring's crown.
        /// </summary>
        public void Fit(Core.App.FocusFleet fleet)
        {
            var sig = fleet != null ? ShipHullBuilder.Signature(fleet.Modules) : "none";
            if (sig == _fitSig)
                return;
            _fitSig = sig;
            if (_fittedMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_fittedMesh);
                else
                    DestroyImmediate(_fittedMesh);
            }

            _fittedMesh = null;
            if (_fitted != null)
                _fitted.SetActive(false);
            if (fleet == null)
                return;

            var parts = new System.Collections.Generic.List<CombineInstance>();
            var top = -WorldScale.StationRingDrop + WorldScale.StationRingSection.y * 0.5f;
            var ringR = WorldScale.StationRingRadius;
            foreach (var m in fleet.Modules)
            {
                if (m == null || !m.OnGrid || Core.Stations.ModuleCatalog.IsCore(m.Type))
                    continue;
                var mesh = ShipHullBuilder.ModuleMesh(m.Type);
                if (mesh == null)
                    continue;
                var dx = m.GridX - WorldScale.ShipCoreCell;
                var dy = m.GridY - WorldScale.ShipCoreCell;
                var bearing = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                var ring = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                // Inner cells on the ring's inner edge, outer ones toward its rim (the crown is 12 m wide).
                var r = ringR + Mathf.Lerp(-3.6f, 3.6f, (ring - 1) / 3f);
                var at = Axis + LatheMesh.Dir(bearing) * r + Vector3.up * top;
                var rot = Quaternion.LookRotation(LatheMesh.Dir(bearing), Vector3.up);
                parts.Add(new CombineInstance
                {
                    mesh = mesh,
                    transform = Matrix4x4.TRS(at, rot, Vector3.one * ModuleScale)
                });
            }

            if (parts.Count == 0)
                return;
            _fittedMesh = new Mesh { name = "SU_StationFitting", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _fittedMesh.CombineMeshes(parts.ToArray(), true, true);
            _fittedMesh.RecalculateBounds();
            _fittedMesh.UploadMeshData(true);
            if (_fitted == null)
            {
                _fitted = new GameObject("Fitting");
                _fitted.transform.SetParent(transform, false);
                _fitted.AddComponent<MeshFilter>();
                var mr = _fitted.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ModuleMat();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            _fitted.GetComponent<MeshFilter>().sharedMesh = _fittedMesh;
            _fitted.SetActive(true);
        }

        void OnDestroy()
        {
            if (_fittedMesh != null)
                Destroy(_fittedMesh);
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
