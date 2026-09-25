using System.Collections.Generic;
using Core.App;
using Core.Vfx;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>
    /// Sieges and troop movements on the holo table, mirroring what the windows show
    /// (<see cref="ExteriorTacticalFx"/>): a pulsing red ring round a besieged world with a tether to each
    /// attacker token (amber when the attackers are ours), a flash on the world at each salvo, and a spark
    /// running between a ship token and its planet when the troop bay cycles. Lives under the diorama
    /// root, so the battle board hides it with the rest. Pooled lines, one shared glow material.
    /// </summary>
    public sealed class HoloTacticalMarkers : MonoBehaviour
    {
        const int RingSegments = 40;
        const int MaxSieges = 4;
        const int MaxTethers = 6;
        const int MaxSparks = 4;

        HoloZoneMap _map;
        FocusContext _focus;
        ParticleSystem _flash;
        readonly List<LineRenderer> _rings = new();
        readonly List<LineRenderer> _tethers = new();
        readonly List<(LineRenderer Line, int Fleet, int Planet, bool ToPlanet, float T)> _sparks = new();
        readonly List<(int Planet, bool Ours, List<int> Attackers)> _sieges = new();

        public static HoloTacticalMarkers Attach(HoloZoneMap map, FocusContext focus)
        {
            if (map == null || map.ContentRoot == null)
                return null;
            var go = new GameObject("TacticalMarkers");
            go.transform.SetParent(map.ContentRoot, false);
            var m = go.AddComponent<HoloTacticalMarkers>();
            m._map = map;
            m._focus = focus;
            m.Build();
            if (SiegeWatch.Instance != null)
                SiegeWatch.Instance.Changed += m.Refresh;
            map.TokensRebuilt += m.Refresh;
            CombatEvents.Bombard += m.OnBombard;
            CombatEvents.TroopTransfer += m.OnTroops;
            return m;
        }

        void OnDestroy()
        {
            if (SiegeWatch.Instance != null)
                SiegeWatch.Instance.Changed -= Refresh;
            if (_map != null)
                _map.TokensRebuilt -= Refresh;
            CombatEvents.Bombard -= OnBombard;
            CombatEvents.TroopTransfer -= OnTroops;
        }

        LineRenderer NewLine(string name, bool loop, int count)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = count;
            lr.numCapVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = CombatFxKit.Beam();
            lr.enabled = false;
            return lr;
        }

        void Build()
        {
            for (var i = 0; i < MaxSieges; i++)
                _rings.Add(NewLine("SiegeRing" + i, true, RingSegments));
            for (var i = 0; i < MaxTethers; i++)
                _tethers.Add(NewLine("SiegeTether" + i, false, 2));
            for (var i = 0; i < MaxSparks; i++)
                _sparks.Add((NewLine("TroopSpark" + i, false, 2), 0, 0, false, -1f));
            _flash = CombatFxKit.Burst(transform, "TableSiegeFlash", 24, 0.5f, gravity: false, stretch: false);
        }

        void Refresh()
        {
            _sieges.Clear();
            var watch = SiegeWatch.Instance;
            if (watch == null || _focus == null)
                return;
            foreach (var s in watch.Sieges)
            {
                if (s.SystemId != _focus.SystemId || _sieges.Count >= MaxSieges)
                    continue;
                _sieges.Add((s.PlanetId, s.OurAttack, new List<int>(s.Attackers)));
            }
        }

        Transform Token(HoloTokenKind kind, int id)
        {
            foreach (var t in _map.Tokens)
                if (t != null && t.Kind == kind && t.Id == id)
                    return t.transform;
            return null;
        }

        void OnBombard(int fleet, int planet, bool toPlanet)
        {
            var at = Token(toPlanet ? HoloTokenKind.Planet : HoloTokenKind.Fleet, toPlanet ? planet : fleet);
            if (at == null || !at.gameObject.activeInHierarchy)
                return;
            var color = toPlanet ? new Color(1f, 0.45f, 0.2f, 1f) : new Color(0.35f, 0.95f, 1f, 1f);
            CombatFxKit.Emit(_flash, at.position, color, 0.05f, 0.35f);
        }

        void OnTroops(int fleet, int planet, bool toPlanet, int units)
        {
            for (var i = 0; i < _sparks.Count; i++)
            {
                if (_sparks[i].T >= 0f)
                    continue;
                _sparks[i] = (_sparks[i].Line, fleet, planet, toPlanet, 0f);
                return;
            }
        }

        void LateUpdate()
        {
            var pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 4f);
            var scale = _map.ContentRoot != null ? _map.ContentRoot.lossyScale.x : 1f;
            var tether = 0;
            for (var i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                var planet = i < _sieges.Count ? Token(HoloTokenKind.Planet, _sieges[i].Planet) : null;
                if (planet == null || !planet.gameObject.activeInHierarchy)
                {
                    ring.enabled = false;
                    continue;
                }

                var s = _sieges[i];
                var color = s.Ours ? new Color(1f, 0.62f, 0.22f, 1f) : new Color(1f, 0.25f, 0.2f, 1f);
                var r = WorldScale.HoloPlanetRadius * 1.9f * scale * (1f + 0.06f * Mathf.Sin(Time.time * 4f));
                var c = planet.position;
                for (var k = 0; k < RingSegments; k++)
                {
                    var a = k / (float)RingSegments * Mathf.PI * 2f;
                    ring.SetPosition(k, c + (_map.ContentRoot.right * Mathf.Cos(a) + _map.ContentRoot.forward * Mathf.Sin(a)) * r);
                }

                ring.widthMultiplier = 0.006f * scale;
                ring.startColor = ring.endColor = new Color(color.r, color.g, color.b, 0.5f + 0.5f * pulse);
                ring.enabled = true;

                foreach (var id in s.Attackers)
                {
                    if (tether >= _tethers.Count)
                        break;
                    var ship = Token(HoloTokenKind.Fleet, id);
                    if (ship == null || !ship.gameObject.activeInHierarchy)
                        continue;
                    var line = _tethers[tether++];
                    line.SetPosition(0, ship.position);
                    line.SetPosition(1, c + (ship.position - c).normalized * r);
                    line.widthMultiplier = 0.003f * scale;
                    line.startColor = new Color(color.r, color.g, color.b, 0.25f);
                    line.endColor = new Color(color.r, color.g, color.b, 0.35f + 0.5f * pulse);
                    line.enabled = true;
                }
            }

            for (; tether < _tethers.Count; tether++)
                _tethers[tether].enabled = false;

            for (var i = 0; i < _sparks.Count; i++)
            {
                var sp = _sparks[i];
                if (sp.T < 0f)
                {
                    sp.Line.enabled = false;
                    continue;
                }

                var ship = Token(HoloTokenKind.Fleet, sp.Fleet);
                var planet = Token(HoloTokenKind.Planet, sp.Planet);
                if (ship == null || planet == null)
                {
                    _sparks[i] = (sp.Line, 0, 0, false, -1f);
                    continue;
                }

                var t = sp.T + Time.deltaTime / 1.6f;
                var from = sp.ToPlanet ? ship.position : planet.position;
                var to = sp.ToPlanet ? planet.position : ship.position;
                // A short comet running along the hop, three times (a shuttle wave).
                var u = t * 3f % 1f;
                var head = Vector3.Lerp(from, to, u) + _map.ContentRoot.up * (Mathf.Sin(u * Mathf.PI) * 0.03f * scale);
                var tail = Vector3.Lerp(from, to, Mathf.Max(0f, u - 0.18f));
                sp.Line.SetPosition(0, tail);
                sp.Line.SetPosition(1, head);
                sp.Line.widthMultiplier = 0.005f * scale;
                sp.Line.startColor = new Color(0.45f, 0.9f, 1f, 0f);
                sp.Line.endColor = new Color(0.6f, 1f, 1f, 1f);
                sp.Line.enabled = true;
                _sparks[i] = t >= 1f ? (sp.Line, 0, 0, false, -1f) : (sp.Line, sp.Fleet, sp.Planet, sp.ToPlanet, t);
            }
        }
    }
}
