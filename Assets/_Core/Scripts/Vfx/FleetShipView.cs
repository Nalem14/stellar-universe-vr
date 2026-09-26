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
            _engines = null;
        }

        EngineBurn[] _engines;
        TrailRenderer _wake;
        float _throttle = 1f;

        /// <summary>
        /// Engine burn of this hull (1 = idle). Under way the flames stretch and a light wake streams behind;
        /// engines and wake are looked up once per hull build, never per frame.
        /// </summary>
        public void SetThrottle(float throttle)
        {
            if (Mathf.Abs(throttle - _throttle) < 0.02f && _engines != null)
                return;
            _throttle = throttle;
            _engines ??= GetComponentsInChildren<EngineBurn>(true);
            for (var i = 0; i < _engines.Length; i++)
                if (_engines[i] != null)
                    _engines[i].Throttle = throttle;
            var wake = throttle > 1.25f;
            if (wake && _wake == null)
                _wake = BuildWake();
            if (_wake != null && _wake.emitting != wake)
                _wake.emitting = wake;
        }

        TrailRenderer BuildWake()
        {
            var go = new GameObject("Wake");
            go.transform.SetParent(transform, false);
            // Aft of the 9×9 hull (grid rows run along +Z).
            go.transform.localPosition = new Vector3(0f, 0f, -WorldScale.ShipSpan * 0.5f);
            var t = go.AddComponent<TrailRenderer>();
            t.time = 2.2f;
            t.minVertexDistance = 2.5f;
            t.widthMultiplier = WorldScale.ShipSpan * 0.22f;
            t.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var tint = _owned ? new Color(0.35f, 0.85f, 1f) : new Color(1f, 0.62f, 0.3f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(tint, Color.white, 0.5f), 0f), new GradientColorKey(tint, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            t.colorGradient = g;
            t.sharedMaterial = CombatFxKit.Beam();
            t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.receiveShadows = false;
            t.emitting = false;
            return t;
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
