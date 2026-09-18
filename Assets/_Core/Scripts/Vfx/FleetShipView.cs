using System.Collections;
using System.Collections.Generic;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Runtime hull for a fleet in system space. Rebuilds when the 9×9 layout changes.
    /// </summary>
    public class FleetShipView : MonoBehaviour
    {
        int _fleetId;
        bool _owned;
        string _sig = string.Empty;
        bool _layoutRequested;

        public void Bind(FocusFleet fleet, bool owned)
        {
            if (fleet == null)
                return;
            _fleetId = fleet.Id;
            _owned = owned;
            Apply(fleet.Modules, fleet.Id);
            if (owned && !HasGrid(fleet.Modules) && !_layoutRequested)
                StartCoroutine(PullLayout());
        }

        void Apply(IReadOnlyList<FocusShipModule> modules, int seed)
        {
            var sig = ShipHullBuilder.Signature(modules) + "|" + seed + (_owned ? "|own" : "|foe");
            if (sig == _sig && transform.childCount > 0)
                return;
            _sig = sig;
            ShipHullBuilder.Build(transform, modules, _owned, seed);
        }

        IEnumerator PullLayout()
        {
            _layoutRequested = true;
            var task = ActionJs.Get("GetShipLayout", new Dictionary<string, string>
            {
                { "fleet", _fleetId.ToString() }
            });
            while (!task.IsCompleted)
                yield return null;
            if (!task.Result.Ok || string.IsNullOrEmpty(task.Result.Body))
                yield break;

            List<FocusShipModule> parsed;
            try
            {
                parsed = new List<FocusShipModule>();
                FocusContext.ParseShipModules(JToken.Parse(task.Result.Body), parsed);
            }
            catch
            {
                yield break;
            }

            if (!HasGrid(parsed))
                yield break;
            FocusContext.CacheLayout(_fleetId, parsed);
            Apply(parsed, _fleetId);
        }

        static bool HasGrid(IReadOnlyList<FocusShipModule> modules)
        {
            if (modules == null)
                return false;
            for (var i = 0; i < modules.Count; i++)
            {
                if (modules[i] != null && modules[i].OnGrid)
                    return true;
            }

            return false;
        }
    }
}
