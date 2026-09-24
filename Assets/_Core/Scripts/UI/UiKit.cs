using TMPro;
using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// Shared look of the bridge UI kit: palette, the few SU/ConsoleMetal materials every console
    /// piece reuses (batched, never instanced per button) and 3D label creation.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Cyan = new(0.25f, 0.92f, 1f, 1f);
        public static readonly Color Amber = new(1f, 0.64f, 0.24f, 1f);
        public static readonly Color Danger = new(1f, 0.32f, 0.28f, 1f);
        public static readonly Color Ok = new(0.45f, 1f, 0.62f, 1f);
        public static readonly Color TextBright = new(0.92f, 0.98f, 1f, 1f);
        public static readonly Color TextDim = new(0.55f, 0.78f, 0.88f, 1f);

        public static readonly int AccentId = Shader.PropertyToID("_Accent");
        public static readonly int AccentMulId = Shader.PropertyToID("_AccentMul");

        static Shader _console;
        static Material _bezel;
        static Material _cap;
        static Material _chassis;

        static Shader ConsoleShader =>
            _console != null ? _console : _console = Shader.Find("SU/ConsoleMetal");

        /// <summary>Dark gunmetal frame around buttons and screens; faint cyan bevel.</summary>
        public static Material Bezel => _bezel != null ? _bezel : _bezel = Make("UiBezel",
            new Color(0.11f, 0.13f, 0.16f), Cyan, 0.22f, 0.45f, 0f, 0.5f, 56f);

        /// <summary>Button cap: lighter brushed metal, accent bevel driven per renderer (MPB).</summary>
        public static Material Cap => _cap != null ? _cap : _cap = Make("UiCap",
            new Color(0.2f, 0.24f, 0.29f), Cyan, 0.55f, 0.5f, 0.06f, 0.35f, 40f);

        /// <summary>Console / screen housing: large brushed panels, very low accent.</summary>
        public static Material Chassis => _chassis != null ? _chassis : _chassis = Make("UiChassis",
            new Color(0.16f, 0.19f, 0.23f), Cyan, 0.12f, 0.2f, 0f, 0.55f, 28f);

        static Material Make(string name, Color baseColor, Color accent, float accentMul, float bevel,
            float faceGlow, float brush, float gloss)
        {
            var shader = ConsoleShader;
            if (shader == null)
            {
                // SU/ConsoleMetal is Always Included; reaching this means a broken build setup.
                Debug.LogWarning("[SU] UiKit: SU/ConsoleMetal missing, falling back to SU/UnlitEmissive.");
                shader = Shader.Find("SU/UnlitEmissive");
            }

            var mat = new Material(shader) { name = name, enableInstancing = true };
            SetIf(mat, "_Color", baseColor);
            SetIf(mat, "_Accent", accent);
            SetIf(mat, "_AccentMul", accentMul);
            SetIf(mat, "_BevelWidth", bevel);
            SetIf(mat, "_FaceGlow", faceGlow);
            SetIf(mat, "_Brush", brush);
            SetIf(mat, "_Gloss", gloss);
            return mat;
        }

        static void SetIf(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop))
                m.SetColor(prop, c);
        }

        static void SetIf(Material m, string prop, float f)
        {
            if (m.HasProperty(prop))
                m.SetFloat(prop, f);
        }

        /// <summary>
        /// 3D label in metres: <paramref name="heightMeters"/> is the cap height of the text, the rect
        /// is <paramref name="widthMeters"/> wide. Faces local -Z (readable from the front of its parent).
        /// Parent must be unscaled — the kit never puts text under a stretched primitive.
        /// </summary>
        public static TextMeshPro Label(Transform parent, string name, string text, Vector3 localPos,
            float widthMeters, float heightMeters, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool wrap = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            // 1 TMP unit = 1 cm: font sizes stay in a sane range and rects are authored in cm.
            go.transform.localScale = Vector3.one * 0.01f;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text ?? string.Empty;
            tmp.alignment = align;
            tmp.color = color;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = wrap ? TextOverflowModes.Ellipsis : TextOverflowModes.Truncate;
            tmp.enableAutoSizing = true;
            // TMP 3D: fontSize 10 ≈ 1 local unit of line height; cap height ≈ 0.7 line → ×14 per unit.
            tmp.fontSizeMax = heightMeters * 100f * 14f;
            tmp.fontSizeMin = tmp.fontSizeMax * 0.55f;
            tmp.fontSize = tmp.fontSizeMax;
            tmp.raycastTarget = false;
            tmp.rectTransform.sizeDelta = new Vector2(widthMeters * 100f, heightMeters * 100f * (wrap ? 3f : 1.6f));
            return tmp;
        }

        /// <summary>Unscaled child that keeps world metres under any parent (see <see cref="ScreenMount"/>).</summary>
        public static GameObject MeshPiece(Transform parent, string name, Mesh mesh, Material mat,
            Vector3 localPos)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }
}
