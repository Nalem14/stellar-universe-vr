using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.UI
{
    /// <summary>
    /// One rule for the controllers: the trigger clicks, the grip grabs. XRI selects with the grip, so the 3D
    /// clickables (poke buttons, officers, lab nodes, dock cells, orrery worlds, the command chair...) answered
    /// the grip while every canvas answered the trigger. Here the trigger, pressed while a ray hovers an
    /// <see cref="XRSimpleInteractable"/>, raises its select-enter (and select-exit on release) — the same
    /// events those objects already listen to. Grab interactables (tokens, crates, samples) stay on the grip,
    /// except those marked <see cref="TriggerGrab"/>: the trigger carries them along the ray (select-enter on the
    /// press, select-exit on the release), as the hint on them says. Poke and pinch are untouched.
    /// </summary>
    public sealed class TriggerSelect : MonoBehaviour
    {
        readonly List<NearFarInteractor> _rays = new();
        readonly Dictionary<NearFarInteractor, XRSimpleInteractable> _held = new();
        readonly Dictionary<NearFarInteractor, (XRGrabInteractable grab, float distance)> _carried = new();
        static TriggerSelect _instance;
        float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("TriggerSelect");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TriggerSelect>();
        }

        /// <summary>Let go of <paramref name="carried"/> without a drop (leaving a room with it in hand).</summary>
        public static void Drop(GameObject carried)
        {
            if (_instance == null || carried == null)
                return;
            foreach (var kv in _instance._carried)
                if (kv.Value.grab != null && kv.Value.grab.gameObject == carried)
                {
                    _instance._carried.Remove(kv.Key);
                    return;
                }
        }

        void OnEnable() => SceneManager.sceneLoaded += OnScene;
        void OnDisable() => SceneManager.sceneLoaded -= OnScene;

        void OnScene(Scene s, LoadSceneMode m)
        {
            _rays.Clear();
            _held.Clear();
            _carried.Clear();
            _nextScan = 0f;
        }

        void Update()
        {
            if (_rays.Count == 0 || _rays[0] == null)
            {
                if (Time.unscaledTime < _nextScan)
                    return;
                _nextScan = Time.unscaledTime + 2f;
                _rays.Clear();
                _rays.AddRange(FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None));
                if (_rays.Count == 0)
                    return;
            }

            for (var i = 0; i < _rays.Count; i++)
            {
                var ray = _rays[i];
                if (ray == null || !ray.isActiveAndEnabled)
                    continue;
                var input = ray.activateInput;
                if (_carried.TryGetValue(ray, out var carried))
                {
                    Carry(ray, input, carried.grab, carried.distance);
                    continue;
                }

                if (input.ReadWasPerformedThisFrame() && !_held.ContainsKey(ray))
                {
                    var grab = HoveredGrab(ray);
                    if (grab != null)
                    {
                        var origin = ray.transform;
                        var distance = Mathf.Max(0.25f, Vector3.Dot(grab.transform.position - origin.position, origin.forward));
                        _carried[ray] = (grab, distance);
                        grab.selectEntered.Invoke(new SelectEnterEventArgs
                        {
                            interactorObject = ray, interactableObject = grab, manager = ray.interactionManager
                        });
                        continue;
                    }

                    var target = Hovered(ray);
                    if (target != null)
                    {
                        _held[ray] = target;
                        target.selectEntered.Invoke(new SelectEnterEventArgs
                        {
                            interactorObject = ray, interactableObject = target, manager = ray.interactionManager
                        });
                    }
                }
                else if (_held.TryGetValue(ray, out var held) && !input.ReadIsPerformed())
                {
                    _held.Remove(ray);
                    if (held != null)
                        held.selectExited.Invoke(new SelectExitEventArgs
                        {
                            interactorObject = ray, interactableObject = held, manager = ray.interactionManager
                        });
                }
            }
        }

        /// <summary>
        /// A trigger-carried object follows the ray at its distance; the release lets it go where it is (its
        /// select-exit decides: set down on a target, or back home).
        /// </summary>
        void Carry(NearFarInteractor ray, UnityEngine.XR.Interaction.Toolkit.Inputs.Readers.XRInputButtonReader input, XRGrabInteractable grab, float distance)
        {
            if (grab == null || !grab.gameObject.activeInHierarchy)
            {
                _carried.Remove(ray);
                return;
            }

            var origin = ray.transform;
            grab.transform.position = origin.position + origin.forward * distance;
            if (input.ReadIsPerformed())
                return;
            _carried.Remove(ray);
            grab.selectExited.Invoke(new SelectExitEventArgs
            {
                interactorObject = ray, interactableObject = grab, manager = ray.interactionManager
            });
        }

        /// <summary>A trigger-carriable grab interactable under this ray, not already in a hand.</summary>
        static XRGrabInteractable HoveredGrab(NearFarInteractor ray)
        {
            if (ray.hasSelection)
                return null;
            var hovered = ray.interactablesHovered;
            for (var i = 0; i < hovered.Count; i++)
                if (hovered[i] is XRGrabInteractable grab && grab.isActiveAndEnabled && !grab.isSelected && grab.GetComponent<TriggerGrab>() != null)
                    return grab;
            return null;
        }

        /// <summary>The clickable under this ray (grab interactables and already-selected ones excluded).</summary>
        static XRSimpleInteractable Hovered(NearFarInteractor ray)
        {
            if (ray.hasSelection)
                return null;
            var hovered = ray.interactablesHovered;
            for (var i = 0; i < hovered.Count; i++)
                if (hovered[i] is XRSimpleInteractable simple && simple.isActiveAndEnabled && !simple.isSelected)
                    return simple;
            return null;
        }
    }
}
