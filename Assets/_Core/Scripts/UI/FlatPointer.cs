using Core.App;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Core.UI
{
    /// <summary>
    /// The flat-screen pointer (PC, mobile): the ray the player aims with and the click it makes, for the systems
    /// that in the headset read a controller ray (holo table, hex board, viewscreen…). PC: the crosshair while
    /// the cursor is captured, the mouse when it is free; a click = left button or E. Mobile: the screen centre
    /// for aiming, the finger for a tap. A click taken by a nearer 3D object or a holo screen is consumed
    /// (<see cref="Consume"/>) before the world systems read it — <see cref="PcInteractionRaycaster"/> and
    /// <see cref="MobileTouchController"/> run first.
    /// </summary>
    public static class FlatPointer
    {
        static int _consumedFrame = -1;
        static int _tapFrame = -10;
        static Ray _tapRay;
        static bool _tapTaken = true;
        static Vector2 _tapScreen;
        static int _uiFrame = -1;
        static float _uiDistance;
        static readonly System.Collections.Generic.List<RaycastResult> UiHits = new();

        public static bool Active => PcPlatformBoot.IsFlatScreen;

        /// <summary>This frame's click belongs to an object / screen already handled (no world click).</summary>
        public static void Consume() => _consumedFrame = Time.frameCount;

        public static bool Consumed => _consumedFrame == Time.frameCount;

        /// <summary>Where the player aims now: crosshair or mouse (PC), screen centre (mobile).</summary>
        public static bool TryAim(out Ray ray)
        {
            ray = default;
            if (!Active)
                return false;
            var cam = Camera.main;
            if (cam == null)
                return false;
            var mouse = Mouse.current;
            var locked = PcDesktopController.Instance == null || PcDesktopController.Instance.IsCursorLocked;
            if (PcPlatformBoot.IsDesktop && !locked && mouse != null)
                ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            else
                ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        /// <summary>
        /// A world click this frame (not taken by an object, a screen or a field): PC left button or E along the aim
        /// ray; mobile, a tap along the finger's ray. Read once: the caller owns it (call <see cref="Peek"/> to look
        /// without taking).
        /// </summary>
        public static bool TryClick(out Ray ray)
        {
            ray = default;
            if (!Active || Consumed || PcPlatformBoot.IsTyping)
                return false;
            if (PcPlatformBoot.IsMobile)
            {
                if (_tapTaken || Time.frameCount - _tapFrame > 1)
                    return false;
                _tapTaken = true;
                ray = _tapRay;
                Consume();
                return true;
            }

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            var pressed = mouse != null && mouse.leftButton.wasPressedThisFrame || kb != null && kb.eKey.wasPressedThisFrame;
            if (!pressed)
                return false;
            // A free cursor over a holo screen: the UI module has the click.
            var locked = PcDesktopController.Instance == null || PcDesktopController.Instance.IsCursorLocked;
            if (!locked && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return false;
            if (!TryAim(out ray))
                return false;
            // One owner per click: the first world system that reads it takes it.
            Consume();
            return true;
        }

        /// <summary>Where the pointer is on screen: crosshair (centre), free mouse, or the last tap (mobile).</summary>
        public static Vector2 ScreenPosition
        {
            get
            {
                if (PcPlatformBoot.IsMobile)
                    return Time.frameCount - _tapFrame <= 1 ? _tapScreen : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                var locked = PcDesktopController.Instance == null || PcDesktopController.Instance.IsCursorLocked;
                var mouse = Mouse.current;
                return !locked && mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
        }

        /// <summary>
        /// Distance to the nearest holo-screen widget under the pointer (MaxValue = none): a panel in front of the
        /// table or the board owns the pointer there. Once per frame.
        /// </summary>
        public static float UiDistance()
        {
            if (_uiFrame == Time.frameCount)
                return _uiDistance;
            _uiFrame = Time.frameCount;
            _uiDistance = float.MaxValue;
            var es = EventSystem.current;
            if (es == null)
                return _uiDistance;
            var ped = new PointerEventData(es) { position = ScreenPosition };
            UiHits.Clear();
            es.RaycastAll(ped, UiHits);
            foreach (var r in UiHits)
            {
                if (r.gameObject == null || r.distance <= 0f)
                    continue;
                if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(r.gameObject) == null &&
                    ExecuteEvents.GetEventHandler<IPointerDownHandler>(r.gameObject) == null)
                    continue;
                _uiDistance = Mathf.Min(_uiDistance, r.distance);
            }

            return _uiDistance;
        }

        /// <summary>Is there an untaken world click this frame (without taking it)?</summary>
        public static bool Peek(out Ray ray)
        {
            ray = default;
            if (!Active || Consumed || PcPlatformBoot.IsTyping)
                return false;
            if (PcPlatformBoot.IsMobile)
            {
                if (_tapTaken || Time.frameCount - _tapFrame > 1)
                    return false;
                ray = _tapRay;
                return true;
            }

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            var pressed = mouse != null && mouse.leftButton.wasPressedThisFrame || kb != null && kb.eKey.wasPressedThisFrame;
            return pressed && TryAim(out ray);
        }

        /// <summary>A secondary press this frame (PC right button / mobile none): cancels, orbits.</summary>
        public static bool SecondaryPressed =>
            PcPlatformBoot.IsDesktop && !PcPlatformBoot.IsTyping && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        static readonly RaycastHit[] Hits = new RaycastHit[24];

        /// <summary>
        /// The nearest thing that reacts along <paramref name="ray"/>: a grabbable, a button, an interactable, a door
        /// or the seat. Trigger volumes that react to nothing (holo zones, rooms) are looked through; a solid
        /// collider that reacts to nothing (a wall) stops the ray.
        /// </summary>
        public static bool FindTarget(Ray ray, float reach, out RaycastHit best)
        {
            best = default;
            var n = Physics.RaycastNonAlloc(ray, Hits, reach, ~0, QueryTriggerInteraction.Collide);
            var bestDistance = float.MaxValue;
            var found = false;
            for (var i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.distance >= bestDistance)
                    continue;
                var go = h.collider.gameObject;
                var reacts = go.GetComponentInParent<FlatGrabbable>() != null || go.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>() != null ||
                             go.GetComponentInParent<Core.Stations.RoomDoor>() != null || go.name.Contains("CaptainSeat") || go.name.Contains("SitZone");
                if (!reacts && h.collider.isTrigger)
                    continue;
                bestDistance = h.distance;
                best = h;
                found = reacts;
            }

            return found;
        }

        /// <summary>Mobile: a tap no object took — offered to the world systems for this frame and the next.</summary>
        internal static void PostTap(Ray ray, Vector2 screen)
        {
            _tapScreen = screen;
            _tapRay = ray;
            _tapFrame = Time.frameCount;
            _tapTaken = false;
        }
    }
}
