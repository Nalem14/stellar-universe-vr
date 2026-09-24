using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// A console screen as a physical object: rounded gunmetal housing with a lit bevel, and a
    /// world-space canvas (1 px = 1 mm) sitting on its glass. Content is authored in pixels inside
    /// <see cref="Content"/>; the housing never scales the canvas. Front is local -Z.
    /// Place it with <see cref="ScreenMount"/> (socket + FaceViewer), never with a billboard.
    /// </summary>
    public sealed class HoloScreen : MonoBehaviour
    {
        public const float PixelsPerMeter = 1000f;
        const float Border = 0.022f;
        const float Depth = 0.035f;

        public Canvas Canvas { get; private set; }
        public RectTransform Content { get; private set; }
        public TMP_Text Header { get; private set; }
        public Vector2 PixelSize { get; private set; }

        MeshRenderer _housing;
        MaterialPropertyBlock _block;

        public static HoloScreen Create(Transform parent, string name, Vector2 sizeMeters, Vector3 localPos,
            Quaternion localRot, string header = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;
            var screen = root.AddComponent<HoloScreen>();

            var housing = UiKit.MeshPiece(root.transform, "Housing",
                UiMeshes.RoundedBox(new Vector3(sizeMeters.x + Border * 2f, sizeMeters.y + Border * 2f, Depth), 0.012f),
                UiKit.Chassis, new Vector3(0f, 0f, Depth * 0.5f));
            screen._housing = housing.GetComponent<MeshRenderer>();
            screen._block = new MaterialPropertyBlock();

            var px = sizeMeters * PixelsPerMeter;
            screen.PixelSize = px;
            screen.Canvas = DiegeticUi.WorldCanvas(root.transform, "Canvas", px,
                new Vector3(0f, 0f, -0.0015f), Quaternion.identity, 1f / PixelsPerMeter);

            var frame = DiegeticUi.HoloFrame(screen.Canvas.transform, px, header);
            screen.Content = frame;
            if (!string.IsNullOrEmpty(header))
            {
                screen.Header = frame.Find("Header/HeaderLabel")?.GetComponent<TMP_Text>();
                // The header band is 10% of the frame height: size its text to the screen, not a fixed 22 px.
                if (screen.Header != null)
                    screen.Header.fontSize = Mathf.Max(18f, px.y * 0.062f);
            }

            // Screens are read, not clicked through: only the widgets inside raycast.
            var img = frame.GetComponent<Image>();
            if (img != null)
                img.raycastTarget = false;
            return screen;
        }

        /// <summary>Tint the housing bevel (station colour, alert state).</summary>
        public void SetAccent(Color accent, float intensity = 0.35f)
        {
            _housing.GetPropertyBlock(_block);
            _block.SetColor(UiKit.AccentId, accent);
            _block.SetFloat(UiKit.AccentMulId, intensity);
            _housing.SetPropertyBlock(_block);
        }

        public void SetHeader(string text)
        {
            if (Header != null)
                Header.text = text ?? string.Empty;
        }
    }
}
