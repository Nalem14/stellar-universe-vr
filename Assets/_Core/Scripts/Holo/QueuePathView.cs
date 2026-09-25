using System.Collections.Generic;
using Core.App;
using Core.Vfx;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>
    /// The inhabited ship's planned route on the holo table: one amber line from its token through every
    /// pending queue step that has a token in this system (planets, asteroid fields). One shared-material
    /// LineRenderer, rebuilt only on map rebuilds / fleet data changes — never per frame.
    /// </summary>
    public sealed class QueuePathView : MonoBehaviour
    {
        const float Lift = 0.05f;

        HoloZoneMap _map;
        FocusContext _focus;
        LineRenderer _line;
        readonly List<Vector3> _points = new();

        public static QueuePathView Attach(HoloZoneMap map, FocusContext focus, CicArtKit art)
        {
            if (map == null)
                return null;
            var go = new GameObject("QueuePath");
            go.transform.SetParent(map.transform, false);
            var view = go.AddComponent<QueuePathView>();
            view._map = map;
            view._focus = focus;
            view._line = go.AddComponent<LineRenderer>();
            view._line.useWorldSpace = false;
            view._line.sharedMaterial = art.Holo(art.MoveGhost != null ? art.MoveGhost : Texture2D.whiteTexture,
                new Color(1f, 0.72f, 0.28f, 0.85f));
            view._line.textureMode = LineTextureMode.Tile;
            view._line.startWidth = 0.012f;
            view._line.endWidth = 0.012f;
            view._line.startColor = new Color(1f, 0.7f, 0.25f, 0.9f);
            view._line.endColor = new Color(1f, 0.85f, 0.45f, 0.6f);
            view._line.numCornerVertices = 2;
            view._line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view._line.enabled = false;

            map.TokensRebuilt += view.Rebuild;
            if (focus != null)
                focus.FleetsChanged += view.Rebuild;
            return view;
        }

        void OnDestroy()
        {
            if (_map != null)
                _map.TokensRebuilt -= Rebuild;
            if (_focus != null)
                _focus.FleetsChanged -= Rebuild;
        }

        void Rebuild()
        {
            _points.Clear();
            var ship = _focus?.FindViewFleet();
            var from = ship != null ? Find(HoloTokenKind.Fleet, ship.Id) : null;
            if (ship != null && from != null && ship.Queue.Count > 0)
            {
                _points.Add(Local(from));
                var start = ship.QueueLoop ? 0 : Mathf.Clamp(ship.QueueIndex, 0, ship.Queue.Count);
                for (var i = start; i < ship.Queue.Count; i++)
                {
                    var step = ship.Queue[i];
                    var kind = step.Type is "moveToPlanet" or "explorePlanet" ? HoloTokenKind.Planet
                        : step.Type is "moveToAsteroid" or "harvestAsteroid" ? HoloTokenKind.Asteroid
                        : (HoloTokenKind?)null;
                    if (kind == null)
                        continue;
                    var token = Find(kind.Value, step.TargetId);
                    if (token == null)
                        continue;
                    var p = Local(token);
                    if (_points.Count == 0 || (_points[_points.Count - 1] - p).sqrMagnitude > 1e-6f)
                        _points.Add(p);
                }

                // A loop closes back on the first leg.
                if (ship.QueueLoop && _points.Count > 2)
                    _points.Add(_points[1]);
            }

            _line.positionCount = _points.Count;
            if (_points.Count >= 2)
                _line.SetPositions(_points.ToArray());
            _line.enabled = _points.Count >= 2;
        }

        HoloToken Find(HoloTokenKind kind, int id)
        {
            foreach (var t in _map.Tokens)
            {
                if (t != null && t.Kind == kind && t.Id == id)
                    return t;
            }

            return null;
        }

        Vector3 Local(HoloToken token) => transform.InverseTransformPoint(token.transform.position) + Vector3.up * Lift;
    }
}
