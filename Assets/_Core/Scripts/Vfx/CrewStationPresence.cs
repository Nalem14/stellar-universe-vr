using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Shows a crew station only in the inhabited modes where it has a job. Helm steers the inhabited
    /// ship: on a virtual orbital station (nothing to steer — boarding goes through the view teleporter)
    /// its console and officer are removed from the room. Driven by FocusContext.Changed (view switches),
    /// never per frame.
    /// </summary>
    public sealed class CrewStationPresence : MonoBehaviour
    {
        FocusContext _focus;
        CrewDialogue.Role _role;

        public static bool IsNeeded(CrewDialogue.Role role, FocusContext focus)
        {
            var onStation = focus != null && focus.ViewFleetId <= 0 && focus.ViewPlanetId > 0;
            return !(onStation && role == CrewDialogue.Role.Helm);
        }

        public void Bind(FocusContext focus, CrewDialogue.Role role)
        {
            _focus = focus;
            _role = role;
            if (_focus != null)
                _focus.Changed += Apply;
            Apply();
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= Apply;
        }

        void Apply()
        {
            var needed = IsNeeded(_role, _focus);
            if (gameObject.activeSelf == needed)
                return;
            if (!needed)
            {
                foreach (var dialogue in GetComponentsInChildren<CrewDialogue>(true))
                    dialogue.Close();
            }

            gameObject.SetActive(needed);
        }
    }
}
