using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.App
{
    public class SessionBoot : MonoBehaviour
    {
        TMP_Text _readout;
        FocusContext _focus;
        FleetPoller _poller;
        BridgeSystemLoader _loader;

        public void BindReadout(TMP_Text readout) => _readout = readout;
        public void BindFocus(FocusContext focus) => _focus = focus;
        public void BindPoller(FleetPoller poller) => _poller = poller;
        public void BindLoader(BridgeSystemLoader loader) => _loader = loader;

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

            if (_loader != null)
            {
                var ok = await _loader.BootFromAnchorOrDefault(systems.Body, auth.Empire,
                    auth.User != null ? auth.User.systemid : 0);
                _focus = _loader.Focus ?? _focus;
                if (!ok)
                {
                    Say(Trans.Get("BootFromAnchorOrDefault"));
                    return;
                }
            }
            else
            {
                // Fallback without loader
                var focusId = BridgeSystemLoader.ResolveOwnedSystem(auth.Empire, systems.Body,
                    auth.User != null ? auth.User.systemid : 0);
                if (focusId > 0)
                {
                    await ActionJs.Get("changesystem", new Dictionary<string, string>
                    {
                        { "id", focusId.ToString() }
                    });
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
            }

            var label = _focus != null && !string.IsNullOrEmpty(_focus.SystemName)
                ? _focus.SystemName
                : (_focus != null ? _focus.SystemId.ToString() : "?");
            var view = _focus != null && _focus.ViewFleetId > 0
                ? $" · ship {_focus.ViewFleetId}"
                : _focus != null && _focus.ViewPlanetId > 0
                    ? $" · station {_focus.ViewPlanetId}"
                    : " · station";
            var count = _focus != null ? _focus.Fleets.Count : 0;
            Say($"{Trans.Get("CommandBridge")} · {label} · {count}{view}");
        }

        async Task<bool> Step(string action)
        {
            var result = await ActionJs.Get(action);
            if (result.Ok)
                return true;
            Say(ActionStatus(action, result.Error));
            return false;
        }

        static string ActionStatus(string action, string error) =>
            $"{action} · {LocalizedApiError(error)}";

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

        void Say(string line)
        {
            Debug.Log("[SU] " + line);
            if (_readout != null)
                _readout.text = line;
        }
    }
}
