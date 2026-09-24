using System;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.UI
{
    /// <summary>
    /// Physical console button: gunmetal bezel + rounded cap that travels in when pressed, with an
    /// accent-lit bevel and an engraved label. Pressed by a finger (XRPokeFilter, poke along +Z into
    /// the panel) or selected with the ray. Front face is local -Z, like every kit piece.
    /// Shared meshes (<see cref="UiMeshes"/>) and materials (<see cref="UiKit"/>); per-button state goes
    /// through one MaterialPropertyBlock, so a console of buttons stays batched.
    /// </summary>
    public sealed class PokeButton : MonoBehaviour
    {
        const float Travel = 0.006f;
        const float IdleGlow = 0.55f;
        const float HoverGlow = 1.25f;
        const float PressGlow = 2.4f;
        const float DisabledGlow = 0.12f;

        Transform _cap;
        MeshRenderer _capRenderer;
        TextMeshPro _label;
        MaterialPropertyBlock _block;
        Color _accent;
        Action _onPress;
        XRSimpleInteractable _interactable;
        float _glow;
        float _glowTarget;
        float _depth;
        float _depthTarget;
        bool _hovered;
        bool _interactive = true;
        Vector3 _capRest;

        public TextMeshPro Label => _label;

        public bool Interactive
        {
            get => _interactive;
            set
            {
                _interactive = value;
                if (_interactable != null)
                    _interactable.enabled = value;
                _glowTarget = value ? (_hovered ? HoverGlow : IdleGlow) : DisabledGlow;
                if (_label != null)
                    _label.color = value ? UiKit.TextBright : UiKit.TextDim * 0.7f;
                enabled = true;
            }
        }

        /// <summary>
        /// Build a button in metres under <paramref name="parent"/> (which must be unscaled — use
        /// <see cref="ScreenMount.Socket"/> on stretched furniture). <paramref name="size"/> is the cap
        /// face (width, height); depth is derived.
        /// </summary>
        public static PokeButton Create(Transform parent, string name, string label, Vector3 localPos,
            Quaternion localRot, Vector2 size, Color accent, Action onPress)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;

            var capDepth = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.28f, 0.008f, 0.018f);
            var radius = Mathf.Min(size.x, size.y) * 0.18f;

            // Bezel: slightly larger, recessed behind the cap.
            var bezelSize = new Vector3(size.x + 0.012f, size.y + 0.012f, capDepth * 0.6f);
            UiKit.MeshPiece(root.transform, "Bezel", UiMeshes.RoundedBox(bezelSize, radius + 0.004f),
                UiKit.Bezel, new Vector3(0f, 0f, bezelSize.z * 0.5f));

            var capGo = UiKit.MeshPiece(root.transform, "Cap",
                UiMeshes.RoundedBox(new Vector3(size.x, size.y, capDepth), radius), UiKit.Cap,
                new Vector3(0f, 0f, -capDepth * 0.35f));

            var button = root.AddComponent<PokeButton>();
            button._cap = capGo.transform;
            button._capRest = capGo.transform.localPosition;
            button._capRenderer = capGo.GetComponent<MeshRenderer>();
            button._accent = accent;
            button._onPress = onPress;
            button._block = new MaterialPropertyBlock();

            button._label = UiKit.Label(capGo.transform, "Label", label,
                new Vector3(0f, 0f, -capDepth * 0.5f - 0.0006f), size.x * 0.86f,
                Mathf.Min(size.y * 0.36f, 0.018f), UiKit.TextBright);

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, capDepth + Travel * 2f);
            col.center = new Vector3(0f, 0f, -capDepth * 0.25f);

            var interactable = root.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(col);
            var poke = root.AddComponent<XRPokeFilter>();
            poke.pokeCollider = col;
            poke.pokeConfiguration = new PokeThresholdDatumProperty(new PokeThresholdData
            {
                pokeDirection = PokeAxis.Z,
                interactionDepthOffset = 0f,
                enablePokeAngleThreshold = true,
                pokeAngleThreshold = 60f
            });
            button._interactable = interactable;
            interactable.hoverEntered.AddListener(_ => button.OnHover(true));
            interactable.hoverExited.AddListener(_ => button.OnHover(false));
            interactable.selectEntered.AddListener(_ => button.OnPress());
            interactable.selectExited.AddListener(_ => button.OnRelease());

            button._glow = button._glowTarget = IdleGlow;
            button.Apply();
            return button;
        }

        public void SetLabel(string text)
        {
            if (_label != null)
                _label.text = text ?? string.Empty;
        }

        public void SetAccent(Color accent)
        {
            _accent = accent;
            Apply();
        }

        void OnHover(bool on)
        {
            _hovered = on;
            if (!_interactive)
                return;
            _glowTarget = on ? HoverGlow : IdleGlow;
            if (on)
                CicCue.Hover(transform.position);
            enabled = true;
        }

        void OnPress()
        {
            if (!_interactive)
                return;
            _depthTarget = Travel;
            _glow = PressGlow;
            _glowTarget = _hovered ? HoverGlow : IdleGlow;
            CicCue.Ok(transform.position);
            enabled = true;
            _onPress?.Invoke();
        }

        void OnRelease()
        {
            _depthTarget = 0f;
            enabled = true;
        }

        void Update()
        {
            // Animates only while something changes, then sleeps (no per-frame cost on idle consoles).
            var k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            _glow = Mathf.Lerp(_glow, _glowTarget, k);
            _depth = Mathf.Lerp(_depth, _depthTarget, k * 1.4f);
            _cap.localPosition = _capRest + new Vector3(0f, 0f, _depth);
            Apply();
            if (Mathf.Abs(_glow - _glowTarget) < 0.01f && Mathf.Abs(_depth - _depthTarget) < 0.0001f)
                enabled = false;
        }

        void Apply()
        {
            if (_capRenderer == null)
                return;
            _capRenderer.GetPropertyBlock(_block);
            _block.SetColor(UiKit.AccentId, _accent);
            _block.SetFloat(UiKit.AccentMulId, _glow);
            _capRenderer.SetPropertyBlock(_block);
        }
    }
}
