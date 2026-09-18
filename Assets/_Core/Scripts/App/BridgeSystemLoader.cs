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
            var previousSystemId = _lastSystemId;
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

                var sameSystem = previousSystemId == systemId && _focus != null && _focus.SystemId == systemId;
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
                    await RevertServerSystem(sameSystem, previousSystemId);
                    if (fadeFx != null)
                        await fadeFx.FadeIn();
                    return false;
                }

                var fleets = await ActionJs.Get("GetAllFleetsAround");
                if (!fleets.Ok)
                {
                    await RevertServerSystem(sameSystem, previousSystemId);
                    if (fadeFx != null)
                        await fadeFx.FadeIn();
                    return false;
                }

                // Validate requested entity before mutating FocusContext / last-system.
                if (preferredFleetId > 0 && !BodyHasOwnedFleet(fleets.Body, preferredFleetId))
                {
                    await RevertServerSystem(sameSystem, previousSystemId);
                    if (fadeFx != null)
                        await fadeFx.FadeIn();
                    return false;
                }

                if (_focus == null)
                    _focus = new FocusContext();

                var prevFleetId = _focus.ViewFleetId;
                var prevPlanetId = _focus.ViewPlanetId;

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

                if (preferredFleetId > 0 && _focus.ViewFleetId != preferredFleetId)
                {
                    await RevertServerSystem(sameSystem, previousSystemId);
                    await RestoreFocus(previousSystemId, prevFleetId, prevPlanetId);
                    if (fadeFx != null)
                        await fadeFx.FadeIn();
                    return false;
                }

                if (viewPlanetId > 0 && _focus.ViewPlanetId != viewPlanetId)
                {
                    await RevertServerSystem(sameSystem, previousSystemId);
                    await RestoreFocus(previousSystemId, prevFleetId, prevPlanetId);
                    if (fadeFx != null)
                        await fadeFx.FadeIn();
                    return false;
                }

                _lastSystemId = systemId;

                if (preferredFleetId > 0 && _focus.ViewFleetId == preferredFleetId)
                    BridgeViewAnchor.SaveShip(preferredFleetId, systemId);
                else if (viewPlanetId > 0 && _focus.ViewPlanetId == viewPlanetId)
                    BridgeViewAnchor.SavePlanet(viewPlanetId, systemId);
                else if (preferredFleetId <= 0 && viewPlanetId <= 0 && _focus.ViewFleetId > 0)
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

        static async Task RevertServerSystem(bool sameSystem, int previousSystemId)
        {
            if (sameSystem || previousSystemId <= 0)
                return;
            await ActionJs.Get("changesystem", new Dictionary<string, string>
            {
                { "id", previousSystemId.ToString() }
            });
        }

        async Task RestoreFocus(int systemId, int fleetId, int planetId)
        {
            if (systemId <= 0 || _focus == null)
                return;
            var systems = await ActionJs.Get("GetSystems");
            var fleets = await ActionJs.Get("GetAllFleetsAround");
            if (!systems.Ok || !fleets.Ok)
                return;
            if (planetId > 0)
            {
                _focus.SetFromApi(systemId, systems.Body, fleets.Body, 0);
                _focus.SetViewPlanet(planetId);
            }
            else
            {
                _focus.SetFromApi(systemId, systems.Body, fleets.Body, fleetId);
            }

            if (_poller != null)
                _poller.Bind(_focus);
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

        static bool BodyHasOwnedFleet(string fleetsBody, int fleetId)
        {
            if (string.IsNullOrEmpty(fleetsBody) || fleetId <= 0)
                return false;
            try
            {
                var root = JToken.Parse(fleetsBody);
                var arr = root as JArray ?? root["fleets"] as JArray ?? root["data"] as JArray;
                if (arr == null)
                    return false;
                var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
                foreach (var f in arr)
                {
                    if (FocusContext.AsInt(f["id"]) != fleetId)
                        continue;
                    if (owned <= 0 || FocusContext.AsInt(f["userid"]) == owned)
                        return true;
                }
            }
            catch
            {
                // Shape varies.
            }

            return false;
        }
    }
}
