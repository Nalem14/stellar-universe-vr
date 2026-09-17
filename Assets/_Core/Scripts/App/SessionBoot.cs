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

        public void BindReadout(TMP_Text readout)
        {
            _readout = readout;
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

            var focus = ResolveOwnedSystem(auth.Empire, systems.Body,
                auth.User != null ? auth.User.systemid : 0);
            if (focus > 0)
            {
                var change = await ActionJs.Get("changesystem", new Dictionary<string, string>
                {
                    { "id", focus.ToString() }
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

            // Native keys + technical system id (id is not a locale string).
            Say($"{Trans.Get("CommandBridge")} · {focus}");
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

            // Server may return a translation key (optionally wrapped in {}).
            var key = error.Trim().Trim('{', '}');
            if (key.StartsWith("error_", System.StringComparison.Ordinal) ||
                key == "error_not_logged_in")
            {
                var translated = Trans.Get(key);
                if (translated != key)
                    return translated;
            }

            // Body after error: is already localized by actionjs.
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
                        var systemId = planet.Value<int?>("systemid") ?? 0;
                        if (systemId > 0)
                            return systemId;
                    }
                }
            }

            try
            {
                var systems = JArray.Parse(systemsBody);
                foreach (var system in systems)
                {
                    var planets = system["planets"] as JArray;
                    if (planets == null)
                        continue;
                    foreach (var planet in planets)
                    {
                        if ((planet.Value<int?>("userid") ?? 0) > 0)
                            return system.Value<int?>("id") ?? 0;
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
