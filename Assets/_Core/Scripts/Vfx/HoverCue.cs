using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// A soft tick when the ray or finger comes onto a holo button (only pointer-enter: drags and scrolls still
    /// reach the list under it). Silent on a disabled button.
    /// </summary>
    public sealed class HoverCue : MonoBehaviour, IPointerEnterHandler
    {
        Selectable _sel;

        void Awake() => _sel = GetComponent<Selectable>();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_sel != null && !_sel.IsInteractable())
                return;
            CicCue.Hover(transform.position);
        }
    }
}
