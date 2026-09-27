using Core.Vfx;
using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// The captain's arm consoles: a tilted panel on each armrest (turned 20° toward the seat, so it is read and
    /// poked from the chair without craning), a dark bezel plate with a lit edge, and a tidy 2 × 2 grid of
    /// real-size buttons. Left = the view (ship list, watch, forward view, auto direction); right = the post
    /// (stand up, guide, glow). One socket per pad; callers ask for a slot.
    /// </summary>
    public static class ArmConsole
    {
        /// <summary>Button cap face (m): big enough to poke with a hand, labels readable seated.</summary>
        public static readonly Vector2 ButtonSize = new(0.086f, 0.058f);

        const float Tilt = 20f;
        const float PlateY = 0.05f;
        /// <summary>One type size for every label (m, cap height), two lines allowed: no giant "Auto" beside a cut-off label.</summary>
        const float LabelHeight = 0.0105f;

        /// <summary>The panel over <paramref name="pad"/> (built once); <paramref name="side"/> −1 left, +1 right.</summary>
        public static Transform Socket(Transform pad, int side, Color accent)
        {
            if (pad == null)
                return null;
            var existing = pad.Find("ArmConsole");
            if (existing != null)
                return existing;
            // The console replaces the flat pad: a wedge housing whose top is tilted toward the seat (the captain
            // sits between the pads), its low edge sitting on the armrest, a lit rim on the inner edge.
            var r = pad.GetComponent<MeshRenderer>();
            if (r != null)
                r.enabled = false;
            var socket = ScreenMount.Socket(pad, "ArmConsole", new Vector3(0f, PlateY, 0.01f), Quaternion.Euler(0f, 0f, side * Tilt));
            UiKit.MeshPiece(socket, "Housing", UiMeshes.RoundedBox(new Vector3(0.2f, 0.07f, 0.2f), 0.014f), UiKit.Chassis,
                new Vector3(0f, -0.036f, 0f));
            UiKit.MeshPiece(socket, "Plate", UiMeshes.RoundedBox(new Vector3(0.186f, 0.006f, 0.186f), 0.006f), UiKit.Bezel,
                Vector3.zero);
            var edge = UiKit.MeshPiece(socket, "Edge", UiMeshes.RoundedBox(new Vector3(0.006f, 0.008f, 0.18f), 0.003f), UiKit.Cap,
                new Vector3(-side * 0.1f, -0.002f, 0f));
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, 2.4f);
            edge.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            return socket;
        }

        /// <summary>Slot <paramref name="index"/> of the 2 × 2 grid (row by row, nearest the knee first).</summary>
        public static Vector3 Slot(int index)
        {
            var col = index % 2;
            var row = index / 2;
            return new Vector3(col == 0 ? -0.045f : 0.045f, 0.006f, 0.042f - row * 0.084f);
        }

        /// <summary>A button in slot <paramref name="index"/> of the console on <paramref name="pad"/>.</summary>
        public static PokeButton Button(Transform pad, int side, int index, string name, string label, Color accent, System.Action onPress)
        {
            var socket = Socket(pad, side, side < 0 ? CicArtKit.Cyan : CicArtKit.Amber);
            if (socket == null)
                return null;
            var b = PokeButton.Create(socket, name, label, Slot(index), Quaternion.Euler(90f, 0f, 0f), ButtonSize, accent, onPress);
            var t = b.Label;
            if (t != null)
            {
                t.enableAutoSizing = false;
                t.fontSize = LabelHeight * 100f * 14f;
                t.textWrappingMode = TMPro.TextWrappingModes.Normal;
                t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                t.rectTransform.sizeDelta = new Vector2(ButtonSize.x * 90f, ButtonSize.y * 85f);
                t.lineSpacing = -18f;
            }

            return b;
        }
    }
}
