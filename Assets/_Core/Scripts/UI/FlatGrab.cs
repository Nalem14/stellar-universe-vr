using System;
using Core.App;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.UI
{
    /// <summary>What a flat-screen grab carries: it follows the aim, then lands on a target or goes home.</summary>
    public interface IFlatGrab
    {
        /// <summary>Each frame while held: the aim ray (crosshair / mouse on PC, screen centre on mobile).</summary>
        void Move(Ray aim);

        /// <summary>The drop click / tap along <paramref name="aim"/>; true = handled (dropped or refused with feedback), the grab ends.</summary>
        bool Drop(Ray aim);

        /// <summary>Put it back where it was, no order.</summary>
        void Cancel();
    }

    /// <summary>
    /// Marks an object as grabbable on a flat screen (the headset grabs it with the hand as before): PC aims at it
    /// and presses <b>E</b>, mobile taps it. <see cref="Begin"/> returns the carried thing (null = not now).
    /// </summary>
    public sealed class FlatGrabbable : MonoBehaviour
    {
        public Func<IFlatGrab> Begin;
        /// <summary>Optional name in the prompt ("[E] Grab {name}").</summary>
        public Func<string> Label;
    }

    /// <summary>
    /// The flat-screen grab, as in any FPS: PC — <b>E</b> on a grabbable picks it up, it follows the crosshair,
    /// <b>left click</b> drops it on the target, <b>right click / Esc</b> puts it back; mobile — tap to pick up, it
    /// follows the screen centre, tap the target to drop, the HUD's Cancel button puts it back. Driven by
    /// <see cref="PcInteractionRaycaster"/> / <see cref="MobileTouchController"/>; one grab at a time.
    /// </summary>
    public static class FlatGrab
    {
        static IFlatGrab _current;
        static int _beganFrame;

        public static bool Holding => _current != null;

        public static event Action Changed;

        public static bool TryBegin(FlatGrabbable target)
        {
            if (_current != null || target == null || target.Begin == null)
                return false;
            var grab = target.Begin();
            if (grab == null)
                return false;
            _current = grab;
            _beganFrame = Time.frameCount;
            FlatPointer.Consume();
            Changed?.Invoke();
            return true;
        }

        public static void Cancel()
        {
            if (_current == null)
                return;
            var g = _current;
            _current = null;
            try
            {
                g.Cancel();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            Changed?.Invoke();
        }

        /// <summary>Called once per frame by the platform's controller while something is held.</summary>
        internal static void Tick()
        {
            if (_current == null)
                return;
            if (!FlatPointer.TryAim(out var aim))
                return;
            try
            {
                _current.Move(aim);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Cancel();
                return;
            }

            if (Time.frameCount == _beganFrame || PcPlatformBoot.IsTyping)
                return;

            if (PcPlatformBoot.IsDesktop)
            {
                var kb = Keyboard.current;
                if (FlatPointer.SecondaryPressed || kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    FlatPointer.Consume();
                    Cancel();
                    return;
                }
            }

            if (!FlatPointer.TryClick(out var drop))
                return;
            var g = _current;
            bool done;
            try
            {
                done = g.Drop(drop);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                done = false;
                g.Cancel();
                _current = null;
                Changed?.Invoke();
                return;
            }

            if (done && _current == g)
            {
                _current = null;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// A carried object for the common case: it rides the aim — onto what the aim meets (minus itself), or at
    /// arm's length — optionally snapped by <see cref="Snap"/>; the drop and the cancel are the owner's.
    /// </summary>
    public sealed class FlatCarry : IFlatGrab
    {
        readonly Transform _item;
        readonly Action _drop;
        readonly Action _cancel;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>Optional: a better spot for this aim (a slot, an intake); null = the default.</summary>
        public Func<Ray, Vector3?> Snap;
        public float ArmLength = 0.9f;

        public FlatCarry(Transform item, Action drop, Action cancel)
        {
            _item = item;
            _drop = drop;
            _cancel = cancel;
        }

        public void Move(Ray aim)
        {
            if (_item == null)
                return;
            var snapped = Snap?.Invoke(aim);
            if (snapped.HasValue)
            {
                _item.position = snapped.Value;
                return;
            }

            var at = aim.origin + aim.direction * ArmLength;
            var n = Physics.RaycastNonAlloc(aim, Hits, ArmLength, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.transform.IsChildOf(_item) || h.distance >= best)
                    continue;
                best = h.distance;
                at = h.point - aim.direction * 0.06f;
            }

            _item.position = at;
        }

        public bool Drop(Ray aim)
        {
            Move(aim);
            _drop?.Invoke();
            return true;
        }

        public void Cancel() => _cancel?.Invoke();
    }
}
