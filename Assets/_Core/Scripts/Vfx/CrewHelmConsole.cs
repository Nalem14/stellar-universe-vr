using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Helm order board — only feasible MoveFleet* rows for the inhabited ship.
    /// </summary>
    public sealed class CrewHelmConsole : MonoBehaviour
    {
        FocusContext _focus;
        CicArtKit _art;
        HoloZoneMap _map;
        FleetPoller _poller;
        Transform _list;
        TMP_Text _title;
        readonly List<GameObject> _rows = new();
        readonly List<GalaxyCatalog.Star> _near = new();
        long _lastBusySig = -1;
        float _nextPoll;

        public void Bind(FocusContext focus, CicArtKit art, HoloZoneMap map, FleetPoller poller,
            Transform list, TMP_Text title)
        {
            _focus = focus;
            _art = art;
            _map = map;
            _poller = poller;
            _list = list;
            _title = title;
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.Changed += OnFocusChanged;
            }

            Core.Utils.AsyncTap.Run(RebuildAsync());
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
        }

        void OnFocusChanged() => Core.Utils.AsyncTap.Run(RebuildAsync());

        void Update()
        {
            if (Time.unscaledTime < _nextPoll)
                return;
            _nextPoll = Time.unscaledTime + 0.5f;
            var fleet = _focus?.FindViewFleet();
            var sig = fleet == null
                ? 0
                : fleet.DestTime ^ (fleet.PlanetId * 17) ^ (fleet.AsteroidId * 31) ^
                  (fleet.IsInBattle ? 1 : 0) ^ fleet.AttackEndTime ^ fleet.HarvestEndTime ^
                  fleet.ExploreEndTime;
            if (sig == _lastBusySig)
                return;
            _lastBusySig = sig;
            Core.Utils.AsyncTap.Run(RebuildAsync());
        }

        async Task RebuildAsync()
        {
            ClearRows();
            if (_focus == null || _art == null || _list == null)
                return;

            var fleet = _focus.FindViewFleet();
            if (fleet == null)
            {
                if (_title != null)
                    _title.text = Trans.Get("planets");
                AddDeadRow(Trans.Get("spaceships"));
                return;
            }

            var shipLabel = string.IsNullOrEmpty(fleet.Name) ? "ship " + fleet.Id : fleet.Name;
            if (_title != null)
                _title.text = shipLabel;

            if (fleet.IsInBattle)
            {
                AddDeadRow(Trans.Get("battle"));
                return;
            }

            if (!FleetOrderGate.CanMove(fleet))
            {
                AddDeadRow(Trans.Get(FleetOrderGate.BusyKey(fleet)));
                return;
            }

            await GalaxyCatalog.EnsureLoaded();

            // Star — only when leaving an orbit / asteroid.
            if (FleetOrderGate.CanGoToStar(fleet) &&
                GalaxyCatalog.TryGet(_focus.SystemId, out var here))
            {
                var sysName = !string.IsNullOrEmpty(_focus.SystemName) ? _focus.SystemName : here.Name;
                if (string.IsNullOrEmpty(sysName))
                    sysName = "system " + _focus.SystemId;
                var hx = here.X;
                var hy = here.Y;
                AddRow(sysName, CicArtKit.Amber, () =>
                    Core.Utils.AsyncTap.Run(MoveToSystem(fleet.Id, hx, hy)));
            }

            foreach (var planet in _focus.Planets)
            {
                if (!FleetOrderGate.CanMoveToPlanet(fleet, planet.Id))
                    continue;
                var label = string.IsNullOrEmpty(planet.Name) ? "planet " + planet.Id : planet.Name;
                var pid = planet.Id;
                AddRow(label, CicArtKit.Cyan, () =>
                    Core.Utils.AsyncTap.Run(MoveToPlanet(fleet.Id, pid)));
            }

            foreach (var rock in _focus.Asteroids)
            {
                if (!FleetOrderGate.CanMoveToAsteroid(fleet, rock.Id))
                    continue;
                var aid = rock.Id;
                AddRow("asteroid " + aid, new Color(0.7f, 0.75f, 0.8f), () =>
                    Core.Utils.AsyncTap.Run(MoveToAsteroid(fleet.Id, aid)));
            }

            if (FleetOrderGate.CanJumpSystem(fleet))
            {
                GalaxyCatalog.CollectNearest(_focus.SystemId, 4, _near);
                for (var i = 0; i < _near.Count; i++)
                {
                    var star = _near[i];
                    var label = string.IsNullOrEmpty(star.Name)
                        ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}", star.X, star.Y)
                        : star.Name;
                    var sx = star.X;
                    var sy = star.Y;
                    AddRow(label, new Color(0.55f, 0.4f, 0.95f), () =>
                        Core.Utils.AsyncTap.Run(MoveToSystem(fleet.Id, sx, sy)));
                }
            }

            if (_rows.Count == 0)
                AddDeadRow(Trans.Get("ok"));
        }

        void AddDeadRow(string label)
        {
            AddRow(label, new Color(0.2f, 0.25f, 0.3f), null, interact: false);
        }

        void AddRow(string label, Color accent, System.Action act, bool interact = true)
        {
            var y = 0.02f - _rows.Count * 0.095f;
            if (_art == null || _list == null)
                return;
            DiegeticUi.Button(_list, "Order_" + _rows.Count, label, new Vector3(0f, y, 0f),
                new Vector3(0.48f, 0.08f, 0.03f), _art, accent, act, interact);
            var mark = new GameObject("Mark_" + _rows.Count);
            mark.transform.SetParent(_list, false);
            _rows.Add(mark);
        }

        void ClearRows()
        {
            if (_list != null)
            {
                for (var i = _list.childCount - 1; i >= 0; i--)
                {
                    var c = _list.GetChild(i).gameObject;
                    if (Application.isPlaying)
                        Destroy(c);
                    else
                        DestroyImmediate(c);
                }
            }

            _rows.Clear();
        }

        async Task MoveToPlanet(int fleetId, int planetId)
        {
            await Issue("MoveFleetToPlanet", new Dictionary<string, string>
            {
                { "fleet", fleetId.ToString() },
                { "planet", planetId.ToString() }
            });
        }

        async Task MoveToAsteroid(int fleetId, int asteroidId)
        {
            await Issue("MoveFleetToAsteroid", new Dictionary<string, string>
            {
                { "fleet", fleetId.ToString() },
                { "asteroid", asteroidId.ToString() }
            });
        }

        async Task MoveToSystem(int fleetId, float x, float y)
        {
            await Issue("MoveFleetToSystem", new Dictionary<string, string>
            {
                { "fleet", fleetId.ToString() },
                {
                    "pos", string.Format(CultureInfo.InvariantCulture, "{0}.{1}", x, y)
                }
            });
        }

        async Task Issue(string action, Dictionary<string, string> query)
        {
            _map?.SetReadout(Trans.Get("Loading"));
            var result = await ActionJs.Get(action, query);
            if (!result.Ok)
            {
                CicCue.Fail(transform.position);
                _map?.SetReadout(string.IsNullOrEmpty(result.Error) ? action : result.Error);
                Core.Utils.AsyncTap.Run(RebuildAsync());
                return;
            }

            CicCue.Ok(transform.position);
            var suffix = !string.IsNullOrEmpty(result.Body) && result.Body.StartsWith("ok")
                ? result.Body
                : "ok";
            _map?.SetReadout($"{Trans.Get("CommandBridge")} · {action} · {suffix}");
            if (_poller != null)
                await _poller.PollNow();
            Core.Utils.AsyncTap.Run(RebuildAsync());
        }
    }
}
