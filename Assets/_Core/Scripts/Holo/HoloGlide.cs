using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Core.Vfx;

namespace Core.Holo
{
    /// <summary>
    /// A holo-table token under way: it glides from where it was to where it is going, timed on the server's
    /// arrival (desttime), instead of jumping onto its destination when the order is polled. An owned ship also
    /// lays its course: a green line pulsing from the token to the destination, chevrons flowing along it, a
    /// ring breathing at the end. Positions are in the map root's space (the token's parent), so pan / zoom /
    /// command-mode scaling carry it. Removes itself (line included) on arrival.
    /// </summary>
    public sealed class HoloGlide : MonoBehaviour
    {
        public static readonly Color Course = new(0.35f, 1f, 0.45f, 1f);
        static Material s_Flow;

        Vector3 _from;
        Vector3 _to;
        double _start;
        double _end;
        HoloSpin _spin;
        HoloToken _token;
        XRGrabInteractable _grab;
        LineRenderer _line;
        Transform _ring;
        readonly Vector3[] _pts = new Vector3[12];
        bool _external;

        public Vector3 Destination => _to;

        static double Now() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        /// <summary>Glide <paramref name="token"/> (already placed at its destination) from <paramref name="fromLocal"/>.</summary>
        public static HoloGlide Start(GameObject token, Vector3 fromLocal, long endUnix, bool course, Texture flow)
        {
            if (token == null)
                return null;
            var g = token.GetComponent<HoloGlide>();
            if (g == null)
                g = token.AddComponent<HoloGlide>();
            g._to = token.transform.localPosition;
            g._from = fromLocal;
            g._start = Now();
            g._end = System.Math.Max(g._start + 0.5, endUnix);
            g._spin = token.GetComponent<HoloSpin>();
            g._token = token.GetComponent<HoloToken>();
            g._grab = token.GetComponent<XRGrabInteractable>();
            if (course && g._line == null)
                g.BuildCourse(flow);
            g.Apply(0f);
            return g;
        }

        /// <summary>
        /// A token whose position its owner computes (the galaxy map: between two stars, on the trip's
        /// progress). Call <see cref="Drive"/> each frame; the course is drawn the same way.
        /// </summary>
        public static HoloGlide Follow(GameObject token, bool course, Texture flow)
        {
            if (token == null)
                return null;
            var g = token.GetComponent<HoloGlide>();
            if (g == null)
                g = token.AddComponent<HoloGlide>();
            g._external = true;
            g._spin = token.GetComponent<HoloSpin>();
            g._token = token.GetComponent<HoloToken>();
            g._grab = token.GetComponent<XRGrabInteractable>();
            if (course && g._line == null)
                g.BuildCourse(flow);
            return g;
        }

        /// <summary>External mode: the token at <paramref name="progress"/> from one point to the other.</summary>
        public void Drive(Vector3 fromLocal, Vector3 toLocal, float progress)
        {
            _from = fromLocal;
            _to = toLocal;
            if (_ring != null)
                _ring.localPosition = toLocal;
            // Linear between stars: the trip's own clock, not an ease.
            var t = Mathf.Clamp01(progress);
            ApplyAt(Vector3.Lerp(fromLocal, toLocal, t));
        }

        void BuildCourse(Texture flow)
        {
            if (s_Flow == null)
            {
                var shader = Shader.Find("SU/ParticleGlow");
                s_Flow = shader != null ? new Material(shader) { name = "SU_CourseFlow" } : CombatFxKit.Beam();
                if (s_Flow.HasProperty("_MainTex"))
                    s_Flow.mainTexture = flow != null ? flow : CombatFxKit.BeamTexture();
                if (s_Flow.HasProperty("_Color"))
                    s_Flow.SetColor("_Color", Color.white);
                if (s_Flow.HasProperty("_EmissionMul"))
                    s_Flow.SetFloat("_EmissionMul", 2.4f);
            }

            var go = new GameObject("Course");
            go.transform.SetParent(transform.parent, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.sharedMaterial = s_Flow;
            _line.textureMode = LineTextureMode.Tile;
            _line.widthMultiplier = 0.009f;
            _line.numCapVertices = 2;
            _line.positionCount = _pts.Length;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            // The destination: a flat ring that breathes.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "CourseEnd";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(go.transform, false);
            ring.transform.localPosition = _to;
            ring.transform.localScale = new Vector3(0.028f, 0.0008f, 0.028f);
            var mr = ring.GetComponent<MeshRenderer>();
            mr.sharedMaterial = s_Flow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", Course);
            mr.SetPropertyBlock(block);
            _ring = ring.transform;
        }

        void Update()
        {
            if (_external)
            {
                ApplyAt(null);
                return;
            }

            var span = _end - _start;
            var t = span > 0 ? Mathf.Clamp01((float)((Now() - _start) / span)) : 1f;
            Apply(t);
            if (t >= 1f)
            {
                _token?.CaptureHome();
                Destroy(this);
            }
        }

        void Apply(float t)
        {
            var k = t * t * (3f - 2f * t);
            ApplyAt(Vector3.Lerp(_from, _to, k));
        }

        /// <summary>Token at <paramref name="at"/> (null: leave it where it is), then the course from it.</summary>
        void ApplyAt(Vector3? at)
        {
            // Held by the captain: leave the token in the hand, keep the course drawn.
            var held = _grab != null && _grab.isSelected;
            var p = at ?? transform.localPosition;
            if (!held && at.HasValue)
            {
                if (_spin != null)
                    _spin.SetOrigin(p);
                else
                    transform.localPosition = p;
                var dir = _to - _from;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-8f)
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(dir, Vector3.up),
                        1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                if (_token != null)
                    _token.HomeLocalPos = p;
            }

            if (_line == null)
                return;
            // From the token to the destination, a shallow arc; pulsing green, chevrons flowing toward the end.
            var a = held ? p : transform.localPosition;
            var lift = Mathf.Min(0.05f, Vector3.Distance(a, _to) * 0.18f);
            for (var i = 0; i < _pts.Length; i++)
            {
                var u = i / (float)(_pts.Length - 1);
                _pts[i] = Vector3.Lerp(a, _to, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * lift);
            }

            _line.SetPositions(_pts);
            var pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
            var c = new Color(Course.r, Course.g, Course.b, pulse);
            _line.startColor = new Color(c.r, c.g, c.b, c.a * 0.5f);
            _line.endColor = c;
            s_Flow.mainTextureOffset = new Vector2(-Time.unscaledTime * 0.8f, 0f);
            if (_ring != null)
            {
                var s = 0.024f + 0.008f * pulse;
                _ring.localScale = new Vector3(s, 0.0008f, s);
            }
        }

        void OnDestroy()
        {
            if (_line != null)
                Destroy(_line.gameObject);
        }
    }
}
