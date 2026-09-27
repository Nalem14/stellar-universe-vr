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
    /// events those objects already listen to. Grab interactables (tokens, crates, samples) stay on the grip;
    /// poke and pinch are untouched.
    /// </summary>
    public sealed class TriggerSelect : MonoBehaviour
    {
        readonly List<NearFarInteractor> _rays = new();
        readonly Dictionary<NearFarInteractor, XRSimpleInteractable> _held = new();
        float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("TriggerSelect");
            DontDestroyOnLoad(go);
            go.AddComponent<TriggerSelect>();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnScene;
        void OnDisable() => SceneManager.sceneLoaded -= OnScene;

        void OnScene(Scene s, LoadSceneMode m)
        {
            _rays.Clear();
            _held.Clear();
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
                if (input.ReadWasPerformedThisFrame() && !_held.ContainsKey(ray))
                {
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
