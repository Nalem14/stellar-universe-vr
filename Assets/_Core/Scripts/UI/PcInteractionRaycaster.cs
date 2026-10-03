using Core.App;
using Core.Stations;
using Core.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.UI
{
    /// <summary>
    /// Performs raycasts from the PC camera (center screen in locked FPS mode, mouse cursor in unlocked mode)
    /// to trigger 3D interactables (PokeButton, XRSimpleInteractable, Captain Seat, Room Doors) with mouse or E key.
    /// Updates PcHud with contextual action prompts.
    /// </summary>
    public sealed class PcInteractionRaycaster : MonoBehaviour
    {
        /// <summary>Far enough for the exchange's crates over the pit (5–7 m) and the holo table from the seat.</summary>
        public float MaxInteractionDistance = 9f;

        Camera _cam;
        XRSimpleInteractable _hoveredInteractable;
        PokeButton _hoveredButton;
        XRSimpleInteractable _heldInteractable;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
                _cam = Camera.main;
        }

        void Update()
        {
            if (!PcPlatformBoot.IsPcDesktop)
                return;

            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null)
                    return;
            }

            var ctrl = PcDesktopController.Instance;
            bool isLocked = ctrl != null && ctrl.IsCursorLocked;

            Ray ray;
            var mouse = Mouse.current;
            if (isLocked || mouse == null)
            {
                ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            }
            else
            {
                ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            }

            // Raycast against physical colliders
            if (Physics.Raycast(ray, out var hit, MaxInteractionDistance))
            {
                HandleHit(hit);
            }
            else
            {
                ClearHover();
            }

            HandleInput();
        }

        void HandleHit(RaycastHit hit)
        {
            var hitGo = hit.collider.gameObject;

            // 1. Check for PokeButton
            var poke = hitGo.GetComponentInParent<PokeButton>();

            // 2. Check for XRSimpleInteractable
            var interactable = hitGo.GetComponentInParent<XRSimpleInteractable>();

            // 3. Check for RoomDoor
            var door = hitGo.GetComponentInParent<RoomDoor>();

            // 4. Check for Captain Seat
            bool isSeat = hitGo.name.Contains("CaptainSeat") || hitGo.name.Contains("SitZone");

            if (poke != null)
            {
                SetHover(interactable, poke, GetPromptForButton(poke));
            }
            else if (isSeat)
            {
                SetHover(interactable, null, Prompt(Trans.Get("vr.pc.sit")));
            }
            else if (door != null)
            {
                SetHover(interactable, null, Prompt(Trans.Get("vr.pc.door")));
            }
            else if (interactable != null)
            {
                SetHover(interactable, null, Prompt(Trans.Get("vr.pc.interact")));
            }
            else
            {
                ClearHover();
            }
        }

        string GetPromptForButton(PokeButton btn)
        {
            var label = btn.Label != null && !string.IsNullOrEmpty(btn.Label.text)
                ? btn.Label.text
                : btn.gameObject.name;
            return Prompt(label);
        }

        /// <summary>"[E] or [Left click] {action}", in the player's language.</summary>
        static string Prompt(string action) => Trans.Format("vr.pc.prompt", action);

        void SetHover(XRSimpleInteractable interactable, PokeButton poke, string prompt)
        {
            if (_hoveredInteractable != interactable)
            {
                if (_hoveredInteractable != null)
                {
                    try { _hoveredInteractable.hoverExited?.Invoke(new HoverExitEventArgs { interactableObject = _hoveredInteractable }); } catch { }
                }

                _hoveredInteractable = interactable;

                if (_hoveredInteractable != null)
                {
                    try { _hoveredInteractable.hoverEntered?.Invoke(new HoverEnterEventArgs { interactableObject = _hoveredInteractable }); } catch { }
                }
            }

            _hoveredButton = poke;

            if (PcHud.Instance != null)
            {
                PcHud.Instance.SetHover(true, prompt);
            }
        }

        void ClearHover()
        {
            if (_hoveredInteractable != null)
            {
                try { _hoveredInteractable.hoverExited?.Invoke(new HoverExitEventArgs { interactableObject = _hoveredInteractable }); } catch { }
                _hoveredInteractable = null;
            }

            _hoveredButton = null;

            if (PcHud.Instance != null)
            {
                PcHud.Instance.SetHover(false, null);
            }
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            // The E key is a letter when a field has the keyboard.
            if (PcPlatformBoot.IsTyping)
                kb = null;

            bool pressDown = (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                             (kb != null && kb.eKey.wasPressedThisFrame);

            bool pressUp = (mouse != null && mouse.leftButton.wasReleasedThisFrame) ||
                           (kb != null && kb.eKey.wasReleasedThisFrame);

            if (pressDown && _hoveredInteractable != null)
            {
                _heldInteractable = _hoveredInteractable;
                try
                {
                    _heldInteractable.selectEntered?.Invoke(new SelectEnterEventArgs
                    {
                        interactableObject = _heldInteractable
                    });
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[PcInteractionRaycaster] Exception during selectEntered: {ex.Message}");
                }
            }
            else if (pressUp && _heldInteractable != null)
            {
                try
                {
                    _heldInteractable.selectExited?.Invoke(new SelectExitEventArgs
                    {
                        interactableObject = _heldInteractable
                    });
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[PcInteractionRaycaster] Exception during selectExited: {ex.Message}");
                }
                _heldInteractable = null;
            }
        }

        void OnDisable()
        {
            ClearHover();
        }
    }
}
