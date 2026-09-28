using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>A touch key on a captain's arm panel (poke with a finger, or ray + trigger).</summary>
    public sealed class ArmKey : MonoBehaviour
    {
        public Button Button;
        public TMP_Text Label;

        /// <summary>Lit and pressable, or greyed out (e.g. the watch when nothing long is running).</summary>
        public bool Interactive
        {
            get => Button != null && Button.interactable;
            set
            {
                if (Button != null)
                    Button.interactable = value;
                if (Label != null)
                    Label.alpha = value ? 1f : 0.45f;
            }
        }
    }

    /// <summary>
    /// The captain's arm panels: on each armrest a short arm rises to a tilted holo tablet, turned toward the
    /// seated captain's eyes and within a finger's reach — a column of touch keys on a world-space canvas,
    /// pressed by poking or by the ray. Left = the view (ship list, watch, forward view, auto direction);
    /// right = the post (sit / stand, guide). One panel per armrest, built on first use; callers ask a slot.
    /// </summary>
    public static class ArmConsole
    {
        const int Slots = 4;
        /// <summary>Canvas: 1 px = 0.5 mm → a 14 × 21 cm tablet.</summary>
        const float MetersPerPixel = 0.0005f;
        static readonly Vector2 Px = new(280f, 420f);
        static readonly Vector2 KeyPx = new(248f, 80f);

        /// <summary>The panel's canvas over <paramref name="pad"/> (built once); <paramref name="side"/> −1 left, +1 right.</summary>
        public static RectTransform Panel(Transform pad, int side, Color accent)
        {
            if (pad == null)
                return null;
            var existing = pad.Find("ArmPanel_Socket/ArmPanel");
            if (existing != null)
                return existing.GetComponentInChildren<Canvas>(true).transform.GetChild(0) as RectTransform;

            // The arm: from the front of the armrest, up and a little outward, to the tablet.
            var socket = ScreenMount.Socket(pad, "ArmPanel", new Vector3(side * 0.03f, 0.02f, 0.1f), Quaternion.identity);
            var room = pad.parent;
            var eye = room != null
                ? room.TransformPoint(new Vector3(0f, 1.17f, WorldScale.CicCaptainChairZ - 0.05f))
                : socket.position + Vector3.up * 0.4f;
            var head = new GameObject("Tablet").transform;
            head.SetParent(socket, false);
            head.localPosition = new Vector3(side * 0.028f, 0.24f, 0f);
            ScreenMount.FaceViewer(head, eye, 0.85f, 6f);

            // The arm runs up behind the tablet (away from the captain), into the middle of its back.
            var back = socket.InverseTransformPoint(head.TransformPoint(new Vector3(0f, -0.02f, 0.03f)));
            var foot = new Vector3(0f, 0f, back.z);
            var stalk = UiKit.MeshPiece(socket, "Stalk",
                UiMeshes.RoundedBox(new Vector3(0.026f, (back - foot).magnitude, 0.026f), 0.011f), UiKit.Chassis,
                (back + foot) * 0.5f);
            stalk.transform.localRotation = Quaternion.FromToRotation(Vector3.up, back - foot);

            // Housing: a dark rounded slab, a lit rim on the side toward the captain.
            UiKit.MeshPiece(head, "Housing", UiMeshes.RoundedBox(new Vector3(0.158f, 0.232f, 0.014f), 0.012f), UiKit.Chassis,
                new Vector3(0f, 0f, 0.009f));
            var rim = UiKit.MeshPiece(head, "Rim", UiMeshes.RoundedBox(new Vector3(0.004f, 0.2f, 0.006f), 0.002f), UiKit.Cap,
                new Vector3(-side * 0.08f, 0f, 0.001f));
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, 2.4f);
            rim.GetComponent<MeshRenderer>().SetPropertyBlock(block);

            var canvas = DiegeticUi.WorldCanvas(head, "Canvas", Px, new Vector3(0f, 0f, -0.0015f), Quaternion.identity, MetersPerPixel);
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            var img = frame.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(accent.r * 0.6f + 0.4f, accent.g * 0.6f + 0.4f, accent.b * 0.6f + 0.4f, 0.85f);

            // A thin lit header line, the panel's colour.
            var line = new GameObject("Header", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            line.transform.SetParent(frame, false);
            line.rectTransform.sizeDelta = new Vector2(Px.x - 60f, 4f);
            line.rectTransform.anchoredPosition = new Vector2(0f, Px.y * 0.5f - 22f);
            line.color = accent;
            line.raycastTarget = false;
            return frame;
        }

        /// <summary>Centre of slot <paramref name="index"/> (top to bottom) on the tablet canvas.</summary>
        static Vector2 Slot(int index) => new(0f, 138f - Mathf.Clamp(index, 0, Slots - 1) * 94f);

        /// <summary>A key in slot <paramref name="index"/> of the panel on <paramref name="pad"/>.</summary>
        public static ArmKey Button(Transform pad, int side, int index, string name, string label, Color accent, System.Action onPress)
        {
            var frame = Panel(pad, side, side < 0 ? CicArtKit.Cyan : CicArtKit.Amber);
            if (frame == null)
                return null;
            var style = accent == CicArtKit.Amber ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Cyan;
            var b = DiegeticUi.HoloButton(frame, label, Slot(index), KeyPx, () => onPress?.Invoke(), style);
            b.name = name;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var t = b.GetComponentInChildren<TMP_Text>();
            t.enableAutoSizing = true;
            t.fontSizeMin = 16f;
            t.fontSizeMax = 26f;
            t.textWrappingMode = TextWrappingModes.Normal;
            var key = b.gameObject.AddComponent<ArmKey>();
            key.Button = b;
            key.Label = t;
            return key;
        }
    }
}
