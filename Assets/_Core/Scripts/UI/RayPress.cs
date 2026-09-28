using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.UI
{
    /// <summary>
    /// A canvas button the controller ray can always press: a thin collider on the button's rect and an XRI
    /// simple interactable that clicks it (trigger via <see cref="TriggerSelect"/>, grip, or pinch). For
    /// buttons that the UI ray does not reach reliably — e.g. the watch cluster, alone on the Watch layer over
    /// passthrough, where only a finger poke used to work. Hover lights the button as the UI hover would.
    /// </summary>
    public sealed class RayPress : MonoBehaviour
    {
        Button _button;
        float _lastPress;

        public static RayPress Add(Button button)
        {
            if (button == null)
                return null;
            var rp = button.GetComponent<RayPress>();
            if (rp == null)
                rp = button.gameObject.AddComponent<RayPress>();
            rp._button = button;
            var rt = (RectTransform)button.transform;
            var col = button.GetComponent<BoxCollider>();
            if (col == null)
                col = button.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(rt.rect.width, rt.rect.height, 8f);
            col.center = new Vector3((0.5f - rt.pivot.x) * rt.rect.width, (0.5f - rt.pivot.y) * rt.rect.height, 0f);
            var xi = button.GetComponent<XRSimpleInteractable>();
            if (xi == null)
                xi = button.gameObject.AddComponent<XRSimpleInteractable>();
            xi.colliders.Clear();
            xi.colliders.Add(col);
            xi.selectEntered.AddListener(_ => rp.Press());
            xi.hoverEntered.AddListener(_ => rp.Hover(true));
            xi.hoverExited.AddListener(_ => rp.Hover(false));
            return rp;
        }

        void Press()
        {
            // A poke also clicks through the UI: never twice for one touch.
            if (_button == null || !_button.IsInteractable() || Time.unscaledTime - _lastPress < 0.35f)
                return;
            _lastPress = Time.unscaledTime;
            _button.onClick.Invoke();
        }

        void Hover(bool on)
        {
            if (_button == null || !_button.IsInteractable())
                return;
            var img = _button.targetGraphic as Image;
            if (img == null)
                return;
            var st = _button.spriteState;
            if (_button.transition == Selectable.Transition.SpriteSwap && st.highlightedSprite != null)
                img.overrideSprite = on ? st.highlightedSprite : null;
        }
    }
}
