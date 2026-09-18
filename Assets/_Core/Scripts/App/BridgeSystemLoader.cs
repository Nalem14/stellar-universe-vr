using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Single path for view TP / boot: changesystem → clean/spawn exterior+holo → snap rig.
    /// Same Unity Bridge scene — CIC furniture stays.
    /// </summary>
    public class BridgeSystemLoader : MonoBehaviour
    {
        FocusContext _focus;
        SystemExterior _exterior;
        BridgeViewRig _viewRig;
        FleetPoller _poller;
        HoloZoneMap _zoneMap;
        bool _busy;
        int _lastSystemId = -1;

        public bool IsBusy => _busy;
        public FocusContext Focus => _focus;

        public void Bind(FocusContext focus, SystemExterior exterior, BridgeViewRig viewRig,
            FleetPoller poller, HoloZoneMap zoneMap)
        {
            _focus = focus;
            _exterior = exterior;
            _viewRig = viewRig;
            _poller = poller;
            _zoneMap = zoneMap;
        }

        public async Task<bool> LoadShipView(int fleetId, int systemId, bool fade = true)
        {
            if (_busy || fleetId <= 0 || systemId <= 0)
                return false;
            return await RunSwap(systemId, preferredFleetId: fleetId, viewPlanetId: 0, fade);
        }

        public async Task<bool> LoadPlanetStation(int planetId, int systemId, bool fade = true)
        {
            if (_busy || planetId <= 0 || systemId <= 0)
                return false;
            return await RunSwap(systemId, preferredFleetId: 0, viewPlanetId: planetId, fade);
        }

        public async Task<bool> BootFromAnchorOrDefault(string systemsBody, JObject empire, int userSystemId)
        {
            var kind = BridgeViewAnchor.Kind;
            var entity = BridgeViewAnchor.EntityId;
            var savedSystem = BridgeViewAnchor.SystemId;

            if (kind == BridgeViewKind.Ship && entity > 0)
            {
                var sys = savedSystem > 0 ? savedSystem : userSystemId;
                if (sys <= 0)
                    sys = ResolveOwnedSystem(empire, systemsBody, userSystemId);
                if (sys > 0 && await LoadShipView(entity, sys, fade: false))
                    return true;
                BridgeViewAnchor.Clear();
            }

            if (kind == BridgeViewKind.Planet && entity > 0)
            {
                var sys = savedSystem > 0 ? savedSystem : userSystemId;
                if (sys <= 0)
                    sys = ResolveOwnedSystem(empire, systemsBody, userSystemId);
                if (sys > 0)
                {
                    var changePlanet = await ActionJs.Get("changeplanet", new Dictionary<string, string>
                    {
                        { "id", entity.ToString() }
                    });
                    if (changePlanet.Ok && await LoadPlanetStation(entity, sys, fade: false))
                        return true;
                }

                BridgeViewAnchor.Clear();
            }

            var focusId = ResolveOwnedSystem(empire, systemsBody, userSystemId);
            if (focusId <= 0)
            {
                _focus?.Clear();
                return false;
            }

            return await RunSwap(focusId, preferredFleetId: 0, viewPlanetId: 0, fade: false);
        }

        async Task<bool> RunSwap(int systemId, int preferredFleetId, int viewPlanetId, bool fade)
        {
            _busy = true;
            var fadeFx = fade ? ViewFade.Ensure() : null;
            try
            {
                if (fadeFx != null)
                    await fadeFx.FadeOut();

                if (viewPlanetId > 0)
                {
                    var cp = await ActionJs.Get("changeplanet", new Dictionary<string, string>
                    {
                        { "id", viewPlanetId.ToString() }
                    });
                    if (!cp.Ok)
                    {
                        Debug.LogWarning("[SU] changeplanet " + cp.Error);
                        if (fadeFx != null)
                            await fadeFx.FadeIn();
                        return false;
                    }
                }

                var sameSystem = _lastSystemId == systemId && _focus != null && _focus.SystemId == systemId;
                if (!sameSystem)
                {
                    var change = await ActionJs.Get("changesystem", new Dictionary<string, string>
                    {
                        { "id", systemId.ToString() }
                    });
                    if (!change.Ok)
                    {
                        Debug.LogWarning("[SU] changesystem " + change.Error);
                        if (fadeFx != null)
                            await fadeFx.FadeIn();
                        return false;
                    }
                }

                var systems = await ActionJs.Get("GetSystems");
                if (!systems.Ok)
                {
                    if (fadeFx != null)
                        await fadeFx.FadeIn();
                    return false;
                }

                var fleets = await ActionJs.Get("GetAllFleetsAround");
                if (!fleets.Ok)
                {
                    if (fadeFx != null)
                        await fadeFx.FadeIn();
                    return false;
                }

                if (_focus == null)
                    _focus = new FocusContext();

                if (sameSystem && preferredFleetId > 0)
                {
                    _focus.ApplyFleetsBody(fleets.Body);
                    _focus.SetViewFleet(preferredFleetId);
                }
                else
                {
                    _focus.SetFromApi(systemId, systems.Body, fleets.Body, preferredFleetId);
                    if (viewPlanetId > 0)
                        _focus.SetViewPlanet(viewPlanetId);
                }

                _lastSystemId = systemId;

                if (preferredFleetId > 0 && _focus.ViewFleetId == preferredFleetId)
                    BridgeViewAnchor.SaveShip(preferredFleetId, systemId);
                else if (viewPlanetId > 0)
                    BridgeViewAnchor.SavePlanet(viewPlanetId, systemId);
                else if (_focus.ViewFleetId > 0)
                    BridgeViewAnchor.SaveShip(_focus.ViewFleetId, systemId);

                if (_poller != null)
                    _poller.Bind(_focus);

                if (fadeFx != null)
                    await fadeFx.FadeIn();
                return true;
            }
            finally
            {
                _busy = false;
            }
        }

        public static int ResolveOwnedSystem(JObject empire, string systemsBody, int userSystemId)
        {
            if (userSystemId > 0)
                return userSystemId;
            if (empire != null)
            {
                var planets = empire["planets"] as JArray;
                if (planets != null)
                {
                    foreach (var planet in planets)
                    {
                        var systemId = FocusContext.AsInt(planet["systemid"]);
                        if (systemId > 0)
                            return systemId;
                    }
                }
            }

            try
            {
                var root = JToken.Parse(systemsBody);
                var systems = root as JArray ?? root["systems"] as JArray;
                if (systems == null)
                    return 0;
                foreach (var system in systems)
                {
                    var planets = system["planets"] as JArray;
                    if (planets == null)
                        continue;
                    foreach (var planet in planets)
                    {
                        if (FocusContext.AsInt(planet["userid"]) > 0)
                            return FocusContext.AsInt(system["id"]);
                    }
                }
            }
            catch
            {
                // Shape varies.
            }

            return 0;
        }
    }
}
