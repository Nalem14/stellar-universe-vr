using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace Core.App
{
    public class SessionBoot : MonoBehaviour
    {
        TMP_Text _readout;
        FocusContext _focus;
        FleetPoller _poller;

        public void BindReadout(TMP_Text readout)
        {
            _readout = readout;
        }

        public void BindFocus(FocusContext focus)
        {
            _focus = focus;
        }

        public void BindPoller(FleetPoller poller)
        {
            _poller = poller;
        }

        public async void Run()
        {
            await Trans.EnsureLoaded();
            var auth = AuthManager.Ensure();
            if (!auth.IsLoggedIn)
            {
                Say(Trans.Get("error_not_logged_in"));
                SceneFlow.Go(SceneFlow.Menu);
                return;
            }

            Say(Trans.Get("Loading"));
            if (!await Step("GetConfigs"))
                return;

            var me = await auth.FetchMe();
            if (!me.Ok)
            {
                Say(ActionStatus("GetMeEmpire", me.Error));
                return;
            }

            var systems = await ActionJs.Get("GetSystems");
            if (!systems.Ok)
            {
                Say(ActionStatus("GetSystems", systems.Error));
                return;
            }

            var focusId = ResolveOwnedSystem(auth.Empire, systems.Body,
                auth.User != null ? auth.User.systemid : 0);
            if (focusId > 0)
            {
                var change = await ActionJs.Get("changesystem", new Dictionary<string, string>
                {
                    { "id", focusId.ToString() }
                });
                if (!change.Ok)
                    Say(ActionStatus("changesystem", change.Error));
            }

            var fleets = await ActionJs.Get("GetAllFleetsAround");
            if (!fleets.Ok)
            {
                Say(ActionStatus("GetAllFleetsAround", fleets.Error));
                return;
            }

            var focus = _focus ?? new FocusContext();
            focus.SetFromApi(focusId, systems.Body, fleets.Body);
            _focus = focus;

            if (_poller != null)
                _poller.Bind(_focus);

            var label = !string.IsNullOrEmpty(focus.SystemName)
                ? focus.SystemName
                : focusId.ToString();
            var view = focus.ViewFleetId > 0 ? $" · fleet {focus.ViewFleetId}" : " · station";
            Say($"{Trans.Get("CommandBridge")} · {label} · {focus.Fleets.Count}{view}");
        }

        async Task<bool> Step(string action)
        {
            var result = await ActionJs.Get(action);
            if (result.Ok)
                return true;
            Say(ActionStatus(action, result.Error));
            return false;
        }

        static string ActionStatus(string action, string error)
        {
            return $"{action} · {LocalizedApiError(error)}";
        }

        static string LocalizedApiError(string error)
        {
            if (string.IsNullOrEmpty(error))
                return Trans.Get("error_not_logged_in");

            var key = error.Trim().Trim('{', '}');
            if (key.StartsWith("error_", System.StringComparison.Ordinal) ||
                key == "error_not_logged_in")
            {
                var translated = Trans.Get(key);
                if (translated != key)
                    return translated;
            }

            return error;
        }

        static int ResolveOwnedSystem(JObject empire, string systemsBody, int userSystemId)
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
                // GetSystems shape can vary; boot still succeeds without a camera focus.
            }

            return 0;
        }

        void Say(string line)
        {
            Debug.Log("[SU] " + line);
            if (_readout != null)
                _readout.text = line;
        }
    }
}
