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
    /// Helm order board for the inhabited ship: planets / asteroids / star / nearby jumps.
    /// Hidden when view is a fake orbital station (no ViewFleet).
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

            await GalaxyCatalog.EnsureLoaded();

            // Star / open space of current system.
            if (GalaxyCatalog.TryGet(_focus.SystemId, out var here))
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
                var label = string.IsNullOrEmpty(planet.Name) ? "planet " + planet.Id : planet.Name;
                var pid = planet.Id;
                AddRow(label, CicArtKit.Cyan, () =>
                    Core.Utils.AsyncTap.Run(MoveToPlanet(fleet.Id, pid)));
            }

            foreach (var rock in _focus.Asteroids)
            {
                var aid = rock.Id;
                AddRow("asteroid " + aid, new Color(0.7f, 0.75f, 0.8f), () =>
                    Core.Utils.AsyncTap.Run(MoveToAsteroid(fleet.Id, aid)));
            }

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

            if (_rows.Count == 0)
                AddDeadRow(Trans.Get("Loading"));
        }

        void AddDeadRow(string label)
        {
            AddRow(label, new Color(0.2f, 0.25f, 0.3f), null, interact: false);
        }

        void AddRow(string label, Color accent, System.Action act, bool interact = true)
        {
            var y = 0.05f - _rows.Count * 0.09f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Order_" + _rows.Count;
            go.transform.SetParent(_list, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = new Vector3(0.72f, 0.075f, 0.03f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                _art.Lit(Texture2D.whiteTexture, accent * 0.55f, 1.5f);

            if (interact && act != null)
            {
                var xi = go.AddComponent<XRSimpleInteractable>();
                xi.hoverEntered.AddListener(_ =>
                {
                    go.transform.localScale = new Vector3(0.76f, 0.082f, 0.035f);
                    CicCue.Hover(go.transform.position);
                    _map?.SetReadout(label);
                });
                xi.hoverExited.AddListener(_ =>
                {
                    go.transform.localScale = new Vector3(0.72f, 0.075f, 0.03f);
                });
                xi.selectEntered.AddListener(_ => act());
            }
            else
            {
                CicEnvironment.DropColliderStatic(go);
            }

            var tmpGo = new GameObject("T");
            tmpGo.transform.SetParent(go.transform, false);
            tmpGo.transform.localPosition = new Vector3(0f, 0f, -0.65f);
            tmpGo.transform.localScale = Vector3.one * 0.022f;
            var tmp = tmpGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 5f;
            tmp.color = Color.white;
            tmp.text = label;
            _rows.Add(go);
        }

        void ClearRows()
        {
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i] != null)
                    Destroy(_rows[i]);
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
                return;
            }

            CicCue.Ok(transform.position);
            var suffix = !string.IsNullOrEmpty(result.Body) && result.Body.StartsWith("ok")
                ? result.Body
                : "ok";
            _map?.SetReadout($"{Trans.Get("CommandBridge")} · {action} · {suffix}");
            if (_poller != null)
                await _poller.PollNow();
        }
    }
}
