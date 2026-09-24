using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Bridge hook for inhabited-ship order APIs shared with <see cref="CrewDialogue"/>.
    /// Visual order UI lives on crew mannequins, not a table panel.
    /// </summary>
    public class ViewFleetOrders : MonoBehaviour
    {
        FocusContext _focus;
        FleetPoller _poller;
        HoloZoneMap _map;
        HexBattleController _hex;
        CicArtKit _art;

        public void Bind(FocusContext focus, FleetPoller poller, HoloZoneMap map, CicArtKit art,
            Transform mount, HexBattleController hex = null)
        {
            _focus = focus;
            _poller = poller;
            _map = map;
            _art = art;
            _hex = hex;
            if (mount != null)
            {
                var bridge = new GameObject("ViewFleetOrdersBridge").transform;
                bridge.SetParent(mount, false);
                bridge.gameObject.SetActive(false);
            }
        }

        public void SetCombatRemap(bool on)
        {
            // CrewDialogue polls feasibility; nothing to remap here.
        }
    }
}
