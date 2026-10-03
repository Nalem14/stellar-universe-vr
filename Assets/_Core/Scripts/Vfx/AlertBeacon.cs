using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// A rotating alarm beacon (gyrophare): machined base, caged lens, two opposed light beams turning about its
    /// local up axis in step with the sweep the alert throws on every wall (<see cref="AlertDirector.BeaconAngle"/>).
    /// Dark at rest; yellow or red with <see cref="AlertDirector.VisualLevel"/>. Shared meshes and materials for
    /// every beacon in every room; the beams are two crossed additive cards each (no real-time light).
    /// </summary>
    public sealed class AlertBeacon : MonoBehaviour
    {
        const float BeamLength = 2.4f;

        static Mesh _base;
        static Mesh _lens;
        static Mesh _cage;
        static Mesh _beam;
        static Material _lensOff;
        static Material _lensAmber;
        static Material _lensRed;
        static Material _beamAmber;
        static Material _beamRed;
        static Texture2D _beamTex;

        MeshRenderer _lensRenderer;
        MeshRenderer[] _beams;
        Transform _pivot;
        AlertLevel _shown = (AlertLevel)(-1);
        float _phase;

        /// <summary>
        /// Mount a beacon under <paramref name="parent"/>: local +Y out of the surface it stands on (ceiling: down).
        /// <paramref name="phase"/> offsets its beams so neighbours do not flash in unison.
        /// </summary>
        public static AlertBeacon Mount(Transform parent, string name, Vector3 localPos, Quaternion localRot, CicArtKit art,
            float scale = 1f, float phase = 0f)
        {
            EnsureShared();
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;
            root.transform.localScale = Vector3.one * scale;
            var b = root.AddComponent<AlertBeacon>();
            b._phase = phase;
            var metal = art.DarkPanel(0.3f);
            LatheMesh.Part(root.transform, "Base", _base, metal);
            LatheMesh.Part(root.transform, "Cage", _cage, art.MetalPanel(0.45f));
            b._lensRenderer = LatheMesh.Part(root.transform, "Lens", _lens, _lensOff).GetComponent<MeshRenderer>();
            b._pivot = new GameObject("Beams").transform;
            b._pivot.SetParent(root.transform, false);
            b._pivot.localPosition = new Vector3(0f, 0.11f, 0f);
            b._beams = new MeshRenderer[2];
            for (var i = 0; i < 2; i++)
            {
                var beam = LatheMesh.Part(b._pivot, "Beam" + i, _beam, _beamAmber);
                beam.transform.localRotation = Quaternion.Euler(0f, i * 180f, 0f);
                b._beams[i] = beam.GetComponent<MeshRenderer>();
                b._beams[i].enabled = false;
            }

            return b;
        }

        /// <summary>
        /// An upright beacon on a wall bracket: <paramref name="wall"/> is the point on the wall face (parent space),
        /// <paramref name="inward"/> the horizontal normal into the room. Plate on the wall, arm out, beacon on top.
        /// </summary>
        public static AlertBeacon MountOnWall(Transform parent, string name, Vector3 wall, Vector3 inward, CicArtKit art,
            float phase = 0f, float scale = 1f)
        {
            inward.y = 0f;
            inward = inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector3.forward;
            var reach = 0.2f * scale;
            var face = Quaternion.LookRotation(inward, Vector3.up);
            var bracket = new GameObject(name + "Bracket").transform;
            bracket.SetParent(parent, false);
            bracket.localPosition = wall;
            bracket.localRotation = face;
            var metal = art.MetalPanel(0.4f);
            var dark = art.DarkPanel(0.3f);
            Block(bracket, "Plate", new Vector3(0f, -0.02f * scale, 0.012f * scale), new Vector3(0.16f, 0.2f, 0.024f) * scale, dark);
            Block(bracket, "Arm", new Vector3(0f, -0.045f * scale, reach * 0.5f), new Vector3(0.05f, 0.03f, reach), metal);
            var strut = Block(bracket, "Strut", new Vector3(0f, -0.1f * scale, reach * 0.42f),
                new Vector3(0.03f, 0.02f, reach * 0.95f), metal);
            strut.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            return Mount(bracket, name, new Vector3(0f, -0.03f * scale, reach), Quaternion.identity, art, scale, phase);
        }

        static Transform Block(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void OnEnable() => _shown = (AlertLevel)(-1);

        void Update()
        {
            var level = AlertDirector.VisualLevel;
            if (level != _shown)
            {
                _shown = level;
                var on = level != AlertLevel.Normal;
                _lensRenderer.sharedMaterial = !on ? _lensOff : level == AlertLevel.Red ? _lensRed : _lensAmber;
                foreach (var beam in _beams)
                {
                    beam.enabled = on;
                    beam.sharedMaterial = level == AlertLevel.Red ? _beamRed : _beamAmber;
                }
            }

            if (_shown != AlertLevel.Normal)
                _pivot.localRotation = Quaternion.Euler(0f, AlertDirector.BeaconAngle + _phase, 0f);
        }

        // ── Shared assets ─────────────────────────────────────────────────────────

        static void EnsureShared()
        {
            if (_base != null)
                return;

            var lathe = new LatheMesh(Vector3.zero) { Step = 15f };
            // Base: a flanged collar, bolted face, the lens seat on top.
            lathe.Revolve(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.13f, 0f), new Vector2(0.13f, 0.025f), new Vector2(0.105f, 0.04f),
                new Vector2(0.095f, 0.06f), new Vector2(0.082f, 0.06f), new Vector2(0f, 0.06f)
            }, 0f, 360f, false);
            _base = lathe.ToMesh("SU_BeaconBase");

            lathe = new LatheMesh(Vector3.zero) { Step = 12f };
            lathe.Revolve(new[]
            {
                new Vector2(0.078f, 0.06f), new Vector2(0.078f, 0.15f), new Vector2(0.066f, 0.185f), new Vector2(0.036f, 0.2f),
                new Vector2(0f, 0.204f)
            }, 0f, 360f, false);
            _lens = lathe.ToMesh("SU_BeaconLens");

            // Cage: four guard bars over the lens and a crown ring.
            lathe = new LatheMesh(Vector3.zero);
            for (var i = 0; i < 4; i++)
                lathe.Bar(45f + i * 90f, 0.082f, 0.094f, 0.06f, 0.19f, 0.007f);
            lathe.Step = 15f;
            lathe.Revolve(new[] { new Vector2(0.094f, 0.18f), new Vector2(0.094f, 0.195f), new Vector2(0.06f, 0.21f) }, 0f, 360f, false);
            _cage = lathe.ToMesh("SU_BeaconCage");

            _beam = BeamMesh();

            _lensOff = Emissive(new Color(0.22f, 0.12f, 0.06f), 0.25f);
            _lensAmber = Emissive(AlertDirector.AmberTint, 4.5f);
            _lensRed = Emissive(AlertDirector.RedTint, 4.5f);
            _beamAmber = Beam(new Color(1f, 0.65f, 0.15f, 1f));
            _beamRed = Beam(new Color(1f, 0.16f, 0.1f, 1f));
        }

        /// <summary>Two crossed cards widening from the lens: a soft cone seen from any side.</summary>
        static Mesh BeamMesh()
        {
            var v = new Vector3[8];
            var uv = new Vector2[8];
            var col = new Color[8];
            const float r0 = 0.05f;
            const float r1 = 0.55f;
            for (var k = 0; k < 2; k++)
            {
                var side = k == 0 ? Vector3.up : Vector3.forward;
                var o = k * 4;
                v[o] = -side * r0;
                v[o + 1] = side * r0;
                v[o + 2] = Vector3.right * BeamLength + side * r1;
                v[o + 3] = Vector3.right * BeamLength - side * r1;
                uv[o] = new Vector2(0f, 0f);
                uv[o + 1] = new Vector2(1f, 0f);
                uv[o + 2] = new Vector2(1f, 1f);
                uv[o + 3] = new Vector2(0f, 1f);
            }

            for (var i = 0; i < 8; i++)
                col[i] = Color.white;
            var m = new Mesh { name = "SU_BeaconBeam" };
            m.vertices = v;
            m.uv = uv;
            m.colors = col;
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.right * BeamLength * 0.5f, new Vector3(BeamLength, 1.2f, 1.2f));
            return m;
        }

        static Material Emissive(Color c, float mul)
        {
            var shader = Shader.Find("SU/UnlitEmissive");
            var m = shader != null ? new Material(shader) : new Material(CombatFxKit.Glow());
            m.name = "SU_BeaconLens";
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", c * 0.6f);
            if (m.HasProperty("_Emission"))
                m.SetColor("_Emission", c);
            if (m.HasProperty("_EmissionMul"))
                m.SetFloat("_EmissionMul", mul);
            return m;
        }

        static Material Beam(Color c)
        {
            var shader = Shader.Find("SU/ParticleGlow");
            var m = shader != null ? new Material(shader) : new Material(CombatFxKit.Glow());
            m.name = "SU_BeaconBeam";
            m.mainTexture = BeamTexture();
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", c);
            if (m.HasProperty("_EmissionMul"))
                m.SetFloat("_EmissionMul", 0.9f);
            return m;
        }

        /// <summary>u across the card (soft edges), v along the beam (hot at the lens, gone at the tip).</summary>
        static Texture2D BeamTexture()
        {
            if (_beamTex != null)
                return _beamTex;
            const int w = 32, h = 64;
            _beamTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SU_BeaconBeam", wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var across = 1f - Mathf.Abs(x / (w - 1f) * 2f - 1f);
                var along = 1f - y / (h - 1f);
                var a = Mathf.Pow(across, 1.6f) * Mathf.Pow(along, 1.8f);
                _beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }

            _beamTex.Apply(false, true);
            return _beamTex;
        }
    }
}
