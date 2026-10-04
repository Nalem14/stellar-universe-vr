using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// Marks a grab interactable the controller trigger may carry too (<see cref="TriggerSelect"/>): held, it
    /// rides the ray at the distance it was taken from, and its select-enter / select-exit fire as for the grip.
    /// For objects one picks off a rack and sets down elsewhere (the dry dock's module blocks).
    /// </summary>
    public sealed class TriggerGrab : MonoBehaviour
    {
    }
}
