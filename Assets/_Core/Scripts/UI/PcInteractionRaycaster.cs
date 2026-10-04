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
    [DefaultExecutionOrder(-200)]
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

            // Something carried (FlatGrab): it follows the aim; left click drops, right click / Esc puts it back.
            if (FlatGrab.Holding)
            {
                ReleaseUi();
                ClearHover(keepPrompt: true);
                PcHud.Instance?.SetHover(true, Trans.Get("vr.pc.dropHint"));
                FlatGrab.Tick();
                return;
            }

            var ctrl = PcDesktopController.Instance;
            bool isLocked = ctrl != null && ctrl.IsCursorLocked;
            if (!FlatPointer.TryAim(out var ray))
                return;

            var found = FlatPointer.FindTarget(ray, MaxInteractionDistance, out var hit);

            // FPS view: the cursor is captured and the UI module ignores a locked mouse, so the holo screens are
            // aimed with the crosshair here (hover, press, click, field focus), whichever is nearer wins.
            if (isLocked && AimUi(found ? hit.distance : MaxInteractionDistance))
            {
                ClearHover(keepPrompt: true);
                HandleUiInput();
                return;
            }

            ReleaseUi();
            if (found)
                HandleHit(hit);
            else
                ClearHover();

            HandleInput();
        }



        // ── Crosshair on world-space UI (locked cursor) ─────────────────────────

        readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _uiHits = new();
        UnityEngine.EventSystems.PointerEventData _ped;
        GameObject _uiHover;
        GameObject _uiPress;

        /// <summary>The nearest UI element under the screen centre within reach, hovered; false if none (or a collider is nearer).</summary>
        bool AimUi(float physicalDistance)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null)
                return false;
            _ped ??= new UnityEngine.EventSystems.PointerEventData(es);
            _ped.Reset();
            _ped.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _ped.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
            _uiHits.Clear();
            es.RaycastAll(_ped, _uiHits);
            GameObject target = null;
            foreach (var r in _uiHits)
            {
                if (r.gameObject == null || r.distance > MaxInteractionDistance || r.distance > physicalDistance + 0.05f)
                    continue;
                // Only what reacts: a selectable / clickable / field up the hierarchy.
                var handler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(r.gameObject) ??
                              UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerDownHandler>(r.gameObject);
                if (handler == null)
                    continue;
                _ped.pointerCurrentRaycast = r;
                target = handler;
                break;
            }

            if (target != _uiHover)
            {
                if (_uiHover != null)
                    UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(_uiHover, _ped, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                _uiHover = target;
                if (_uiHover != null)
                    UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(_uiHover, _ped, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            }

            if (target == null)
                return false;
            if (PcHud.Instance != null)
            {
                var label = target.GetComponentInChildren<TMPro.TMP_Text>();
                var field = target.GetComponent<TMPro.TMP_InputField>();
                var what = field == null && label != null && !string.IsNullOrEmpty(label.text) ? label.text : Trans.Get("vr.pc.interact");
                PcHud.Instance.SetHover(true, Prompt(what));
            }

            return true;
        }

        void HandleUiInput()
        {
            var mouse = Mouse.current;
            var kb = PcPlatformBoot.IsTyping ? null : Keyboard.current;
            var down = mouse != null && mouse.leftButton.wasPressedThisFrame || kb != null && kb.eKey.wasPressedThisFrame;
            var up = mouse != null && mouse.leftButton.wasReleasedThisFrame || kb != null && kb.eKey.wasReleasedThisFrame;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (down && _uiHover != null)
            {
                _ped.pressPosition = _ped.position;
                _ped.pointerPressRaycast = _ped.pointerCurrentRaycast;
                _ped.eligibleForClick = true;
                _uiPress = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(_uiHover, _ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler) ?? _uiHover;
                _ped.pointerPress = _uiPress;
                FlatPointer.Consume();
                // A field takes the keyboard (the module would select on press; it does not see a locked mouse).
                var selectable = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.ISelectHandler>(_uiHover);
                if (es != null && selectable != null)
                    es.SetSelectedGameObject(selectable, _ped);
            }

            if (up && _uiPress != null)
            {
                UnityEngine.EventSystems.ExecuteEvents.Execute(_uiPress, _ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                var click = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(_uiHover);
                if (click != null && click == UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(_uiPress))
                    UnityEngine.EventSystems.ExecuteEvents.Execute(click, _ped, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                _uiPress = null;
                _ped.pointerPress = null;
                _ped.eligibleForClick = false;
            }
        }

        void ReleaseUi()
        {
            if (_uiPress != null && _ped != null)
                UnityEngine.EventSystems.ExecuteEvents.Execute(_uiPress, _ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            _uiPress = null;
            if (_uiHover != null && _ped != null)
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(_uiHover, _ped, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
            _uiHover = null;
        }

        FlatGrabbable _hoveredGrab;
        /// <summary>
        /// The grab interactable under the reticle, for its hover events only (the dock's store block shows its
        /// card on hover, as under a controller ray); taking it stays with <see cref="FlatGrabbable"/>.
        /// </summary>
        XRBaseInteractable _hoveredGrabInteractable;

        void SetGrabHover(XRBaseInteractable grab)
        {
            if (_hoveredGrabInteractable == grab)
                return;
            if (_hoveredGrabInteractable != null)
            {
                try { _hoveredGrabInteractable.hoverExited?.Invoke(new HoverExitEventArgs { interactableObject = _hoveredGrabInteractable }); } catch { }
            }

            _hoveredGrabInteractable = grab;
            if (grab != null)
            {
                try { grab.hoverEntered?.Invoke(new HoverEnterEventArgs { interactableObject = grab }); } catch { }
            }
        }

        void HandleHit(RaycastHit hit)
        {
            var hitGo = hit.collider.gameObject;
            var poke = hitGo.GetComponentInParent<PokeButton>();
            var interactable = hitGo.GetComponentInParent<XRSimpleInteractable>();
            var door = hitGo.GetComponentInParent<RoomDoor>();
            _hoveredGrab = hitGo.GetComponentInParent<FlatGrabbable>();
            SetGrabHover(_hoveredGrab != null ? _hoveredGrab.GetComponent<XRGrabInteractable>() : null);
            bool isSeat = hitGo.name.Contains("CaptainSeat") || hitGo.name.Contains("SitZone");

            if (_hoveredGrab != null)
            {
                var what = _hoveredGrab.Label != null ? _hoveredGrab.Label() : null;
                var grab = string.IsNullOrEmpty(what) ? Trans.Get("vr.pc.grabOnly") : Trans.Format("vr.pc.grabPrompt", what);
                SetHover(interactable, poke, grab);
            }
            else if (poke != null)
                SetHover(interactable, poke, GetPromptForButton(poke));
            else if (isSeat)
                SetHover(interactable, null, Prompt(Trans.Get("vr.pc.sit")));
            else if (door != null)
                SetHover(interactable, null, Prompt(Trans.Get("vr.pc.door")));
            else if (interactable != null)
                SetHover(interactable, null, Prompt(Trans.Get("vr.pc.interact")));
            else
                ClearHover();
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

        void ClearHover(bool keepPrompt = false)
        {
            _hoveredGrab = null;
            SetGrabHover(null);
            if (_hoveredInteractable != null)
            {
                try { _hoveredInteractable.hoverExited?.Invoke(new HoverExitEventArgs { interactableObject = _hoveredInteractable }); } catch { }
                _hoveredInteractable = null;
            }

            _hoveredButton = null;

            if (PcHud.Instance != null && !keepPrompt)
            {
                PcHud.Instance.SetHover(false, null);
            }
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            // The E key is a letter when a field has the keyboard.
            var kb = PcPlatformBoot.IsTyping ? null : Keyboard.current;
            var ePressed = kb != null && kb.eKey.wasPressedThisFrame;

            // E on a grabbable picks it up (FPS rule); a left click on it stays a click (select, give an order).
            if (ePressed && _hoveredGrab != null && FlatGrab.TryBegin(_hoveredGrab))
                return;

            bool pressDown = (mouse != null && mouse.leftButton.wasPressedThisFrame) || ePressed;
            bool pressUp = (mouse != null && mouse.leftButton.wasReleasedThisFrame) || (kb != null && kb.eKey.wasReleasedThisFrame);

            // A free cursor over a holo screen: the UI module has this click, not the object behind the screen.
            var locked = PcDesktopController.Instance == null || PcDesktopController.Instance.IsCursorLocked;
            if (pressDown && !locked && UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                pressDown = false;

            if (pressDown && _hoveredInteractable != null)
            {
                // This object owns the click: the world systems (table, board) do not see it.
                FlatPointer.Consume();
                _heldInteractable = _hoveredInteractable;
                try
                {
                    _heldInteractable.selectEntered?.Invoke(new SelectEnterEventArgs { interactableObject = _heldInteractable });
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            else if (pressUp && _heldInteractable != null)
            {
                try
                {
                    _heldInteractable.selectExited?.Invoke(new SelectExitEventArgs { interactableObject = _heldInteractable });
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }

                _heldInteractable = null;
            }
        }

        void OnDisable()
        {
            ReleaseUi();
            ClearHover();
        }
    }
}
