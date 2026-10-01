using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>
    /// What the table aim shows: a lock ring round the snapped target (billboard, slow turn, pulse on snap),
    /// a precise crosshair where each ray meets the map plane, a faint tether from that point to the snapped
    /// target, and on the galaxy the grid coordinates under the crosshair. Two procedural textures made once,
    /// shared kit materials (one per tint), no allocation per frame.
    /// </summary>
    public sealed class HoloAimReticle : MonoBehaviour
    {
        const int MaxPoints = 2;
        static Texture2D s_Ring;
        static Texture2D s_Cross;

        CicArtKit _art;
        Transform _ring;
        MeshRenderer _ringMr;
        Color _ringTint = new(0f, 0f, 0f, 0f);
        float _ringPulse;
        float _ringSpin;
        readonly Transform[] _points = new Transform[MaxPoints];
        readonly MeshRenderer[] _pointMr = new MeshRenderer[MaxPoints];
        readonly Color[] _pointTint = new Color[MaxPoints];
        LineRenderer _tether;
        Color _tetherTint = new(0f, 0f, 0f, 0f);
        Transform _labelRoot;
        TextMeshPro _label;
        int _labelX = int.MinValue;
        int _labelY = int.MinValue;

        public static HoloAimReticle Build(Transform parent, CicArtKit art)
        {
            var go = new GameObject("TableAimReticle");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<HoloAimReticle>();
            r._art = art;
            r.BuildParts();
            return r;
        }

        void BuildParts()
        {
            if (s_Ring == null)
                s_Ring = MakeTexture(128, RingCover, "HoloAimRing");
            if (s_Cross == null)
                s_Cross = MakeTexture(64, CrossCover, "HoloAimCross");

            _ring = Quad("LockRing", out _ringMr);
            _ring.gameObject.SetActive(false);
            for (var i = 0; i < MaxPoints; i++)
            {
                _points[i] = Quad("AimPoint" + i, out _pointMr[i]);
                _points[i].gameObject.SetActive(false);
            }

            var line = new GameObject("AimTether");
            line.transform.SetParent(transform, false);
            _tether = line.AddComponent<LineRenderer>();
            _tether.positionCount = 2;
            _tether.useWorldSpace = true;
            _tether.widthMultiplier = 0.0016f;
            _tether.numCapVertices = 2;
            _tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tether.receiveShadows = false;
            _tether.enabled = false;

            _labelRoot = new GameObject("AimCoords").transform;
            _labelRoot.SetParent(transform, false);
            _labelRoot.gameObject.AddComponent<BillboardFace>();
            _label = UiKit.Label(_labelRoot, "Text", string.Empty, Vector3.zero, 0.3f, 0.012f, UiKit.TextBright);
            _label.enableAutoSizing = false;
            _label.fontSize = _label.fontSizeMax;
            _label.overflowMode = TextOverflowModes.Overflow;
            _label.outlineWidth = 0.22f;
            _label.outlineColor = new Color32(2, 10, 16, 230);
            _labelRoot.gameObject.SetActive(false);
        }

        Transform Quad(string name, out MeshRenderer mr)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        static void SetWorldScale(Transform t, float meters)
        {
            var p = t.parent != null ? Mathf.Max(1e-5f, t.parent.lossyScale.x) : 1f;
            t.localScale = Vector3.one * (meters / p);
        }

        /// <summary>Lock ring round a target (<paramref name="radius"/> = world metres), facing the eye.</summary>
        public void ShowRing(Vector3 world, float radius, Color tint, bool snapped, Vector3 eye)
        {
            if (!_ring.gameObject.activeSelf)
                _ring.gameObject.SetActive(true);
            if (tint != _ringTint)
            {
                _ringTint = tint;
                _ringMr.sharedMaterial = _art.RadarIcon(s_Ring, tint);
            }

            if (snapped)
                _ringPulse = 1f;
            var dt = Time.unscaledDeltaTime;
            _ringPulse = Mathf.Max(0f, _ringPulse - dt * 7f);
            _ringSpin = (_ringSpin + dt * 40f) % 360f;
            var toEye = eye - world;
            var look = toEye.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-toEye.normalized, Vector3.up) : Quaternion.identity;
            // Slightly toward the eye so the ring never sinks into the body it circles.
            var pos = world + (toEye.sqrMagnitude > 1e-6f ? toEye.normalized * Mathf.Min(radius * 0.6f, 0.02f) : Vector3.zero);
            _ring.SetPositionAndRotation(pos, look * Quaternion.Euler(0f, 0f, _ringSpin));
            var breathe = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f);
            SetWorldScale(_ring, radius * 2.3f * (breathe + _ringPulse * 0.6f));
        }

        public void HideRing()
        {
            if (_ring.gameObject.activeSelf)
                _ring.gameObject.SetActive(false);
        }

        /// <summary>Precise crosshair flat on the map plane (<paramref name="flat"/> = the plane's rotation).</summary>
        public void ShowPoint(int slot, Vector3 world, Quaternion flat, float size, Color tint)
        {
            if (slot < 0 || slot >= MaxPoints)
                return;
            var t = _points[slot];
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            if (tint != _pointTint[slot])
            {
                _pointTint[slot] = tint;
                _pointMr[slot].sharedMaterial = _art.RadarIcon(s_Cross, tint);
            }

            t.SetPositionAndRotation(world, flat * Quaternion.Euler(90f, 0f, 0f));
            SetWorldScale(t, size);
        }

        public void HidePoint(int slot)
        {
            if (slot >= 0 && slot < MaxPoints && _points[slot].gameObject.activeSelf)
                _points[slot].gameObject.SetActive(false);
        }

        /// <summary>Faint line from where the ray really points to the target the assist picked.</summary>
        public void ShowTether(Vector3 from, Vector3 to, Color tint)
        {
            if (tint != _tetherTint)
            {
                _tetherTint = tint;
                _tether.sharedMaterial = _art.Lit(Texture2D.whiteTexture, new Color(tint.r, tint.g, tint.b, 0.7f), 1.6f);
            }

            _tether.SetPosition(0, from);
            _tether.SetPosition(1, to);
            if (!_tether.enabled)
                _tether.enabled = true;
        }

        public void HideTether()
        {
            if (_tether.enabled)
                _tether.enabled = false;
        }

        /// <summary>Galaxy grid coordinates beside the crosshair (text rebuilt only when the tenth changes).</summary>
        public void ShowCoords(Vector3 world, Vector2 grid, float lift)
        {
            var x = Mathf.RoundToInt(grid.x * 10f);
            var y = Mathf.RoundToInt(grid.y * 10f);
            if (x != _labelX || y != _labelY)
            {
                _labelX = x;
                _labelY = y;
                _label.text = GalaxyCatalog.Coordinates(x / 10f, y / 10f);
            }

            if (!_labelRoot.gameObject.activeSelf)
                _labelRoot.gameObject.SetActive(true);
            _labelRoot.position = world + Vector3.up * lift;
            SetWorldScale(_labelRoot, 1f);
        }

        public void HideCoords()
        {
            if (_labelRoot.gameObject.activeSelf)
                _labelRoot.gameObject.SetActive(false);
        }

        public void HideAll()
        {
            HideRing();
            for (var i = 0; i < MaxPoints; i++)
                HidePoint(i);
            HideTether();
            HideCoords();
        }

        // ── Procedural glyphs (built once) ───────────────────────────────────────

        delegate float Cover(float x, float y);

        static Texture2D MakeTexture(int size, Cover cover, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };
            var px = new Color32[size * size];
            for (var j = 0; j < size; j++)
            {
                for (var i = 0; i < size; i++)
                {
                    // Centred coordinates, radius 1 at the texture edge; 2×2 supersampling for clean edges.
                    var acc = 0f;
                    for (var s = 0; s < 4; s++)
                    {
                        var x = ((i + 0.25f + 0.5f * (s & 1)) / size) * 2f - 1f;
                        var y = ((j + 0.25f + 0.5f * (s >> 1)) / size) * 2f - 1f;
                        acc += cover(x, y);
                    }

                    var c = (byte)Mathf.RoundToInt(Mathf.Clamp01(acc * 0.25f) * 255f);
                    px[j * size + i] = new Color32(c, c, c, c);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Band(float v, float lo, float hi) => v >= lo && v <= hi ? 1f : 0f;

        /// <summary>Lock ring: four arcs with gaps on the diagonals, four ticks pointing in, a faint inner circle.</summary>
        static float RingCover(float x, float y)
        {
            var r = Mathf.Sqrt(x * x + y * y);
            var ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            var diag = Mathf.Abs(Mathf.DeltaAngle(ang, 45f + Mathf.Round((ang - 45f) / 90f) * 90f));
            var card = Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 90f) * 90f));
            var arcs = Band(r, 0.8f, 0.9f) * (diag > 11f ? 1f : 0f);
            // Ticks: a wedge narrowing toward the centre.
            var tickHalf = Mathf.Lerp(1.5f, 6f, Mathf.InverseLerp(0.6f, 0.8f, r));
            var ticks = Band(r, 0.6f, 0.8f) * (card < tickHalf ? 1f : 0f);
            var inner = Band(r, 0.5f, 0.53f) * 0.4f;
            return Mathf.Max(arcs, Mathf.Max(ticks, inner));
        }

        /// <summary>Crosshair: bright centre dot, thin circle, four ticks out.</summary>
        static float CrossCover(float x, float y)
        {
            var r = Mathf.Sqrt(x * x + y * y);
            var dot = r < 0.14f ? 1f : 0f;
            var circle = Band(r, 0.46f, 0.56f) * 0.85f;
            var tick = Band(r, 0.68f, 0.96f) * ((Mathf.Abs(x) < 0.06f || Mathf.Abs(y) < 0.06f) ? 1f : 0f);
            return Mathf.Max(dot, Mathf.Max(circle, tick));
        }
    }
}
