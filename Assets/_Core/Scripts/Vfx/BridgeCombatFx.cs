using System.Collections.Generic;
using Core.App;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Core.Vfx
{
    /// <summary>
    /// The fight felt aboard the ship we stand on (never a camera shake — VR comfort):
    /// red alert (the room's own lights swing to a slow red pulse, klaxon on engage), hull hits (a boom
    /// from the struck side, the lights stutter, sparks rain from the ceiling), shield hits (a softer blue
    /// swell through the windows), our own salvos (a deep thrum and the window wash flares in the weapon's
    /// colour) and the ship's death (blackout). Drives the room's existing lights — no extra real-time light
    /// on Quest — and one pooled spark system.
    /// </summary>
    public sealed class BridgeCombatFx : MonoBehaviour
    {
        static readonly Color AlertRed = new(1f, 0.12f, 0.08f, 1f);

        FocusContext _focus;
        Light[] _lights = System.Array.Empty<Light>();
        Color[] _baseColor;
        float[] _baseIntensity;
        Light _wash;
        ParticleSystem _sparks;
        float _alert;
        float _alertTarget;
        float _flicker = -1f;
        float _flickerLen;
        float _blackout;
        float _washT = -1f;
        Color _washColor;
        float _klaxonAt = -1f;
        int _klaxons;

        struct Pending
        {
            public float At;
            public int Hull;
            public int Shield;
        }

        readonly List<Pending> _pending = new();

        // Red alert reaches the eye even where the room is lit by emissives: ceiling strips and the vignette.
        GameObject _strips;
        Material _stripMat;
        Vignette _vignette;
        Color _vignetteBase;
        float _vignetteBaseIntensity;
        float _hitFlash;
        readonly Dictionary<MeshRenderer, Material> _accents = new();
        float _accentCheck;

        public static BridgeCombatFx Build(Transform interior, FocusContext focus, CicArtKit art)
        {
            var fx = interior.gameObject.AddComponent<BridgeCombatFx>();
            fx._focus = focus;
            fx._art = art;
            fx._sparks = CombatFxKit.Burst(interior, "BridgeSparks", 80, 0.9f, gravity: true, stretch: true);
            AlertState.Changed += fx.OnAlert;
            CombatEvents.Hit += fx.OnHit;
            CombatEvents.Shot += fx.OnShot;
            CombatEvents.Destroyed += fx.OnDestroyed;
            CombatEvents.Bombard += fx.OnBombard;
            CombatEvents.TroopTransfer += fx.OnTroops;
            return fx;
        }

        void Start()
        {
            CacheLights();
            BuildStrips();
            var volume = FindFirstObjectByType<Volume>();
            if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out _vignette))
            {
                _vignetteBase = _vignette.color.value;
                _vignetteBaseIntensity = _vignette.intensity.value;
            }
        }

        void BuildStrips()
        {
            var shader = Shader.Find("SU/UnlitEmissive");
            _stripMat = shader != null ? new Material(shader) : CombatFxKit.Glow();
            _stripMat.name = "SU_AlertStrip";
            _strips = new GameObject("RedAlertBars");
            _strips.transform.SetParent(transform, false);
            _shipStrips = new GameObject("Ship").transform;
            _shipStrips.SetParent(_strips.transform, false);
            // Along the top of every wall of the shell, over the window heads (visible from the chair and the table).
            for (var e = 0; e < BridgeShell.Plan.Length; e++)
            {
                var len = BridgeShell.EdgeLength(e);
                var bar = Strip(BridgeShell.EdgePoint(e, len * 0.5f, 0.05f, BridgeShell.WallTop - 0.07f), new Vector3(len - 0.3f, 0.05f, 0.05f));
                // Cube x along the edge: LookRotation(normal) puts local x on up × normal = the edge direction.
                bar.localRotation = Quaternion.LookRotation(BridgeShell.EdgeNormal(e), Vector3.up);
            }

            // The station's command hall: one ring under the cornice, all the way round.
            var ring = new LatheMesh(StationCommandShell.Centre);
            var r = StationCommandShell.R - 0.06f;
            ring.Revolve(new[] { new Vector2(r, StationCommandShell.WallTop - 0.12f), new Vector2(r, StationCommandShell.WallTop - 0.04f) },
                0f, 360f, true);
            _stationStrips = LatheMesh.Part(_strips.transform, "Station", ring.ToMesh("SU_StationAlertRing"), _stripMat).transform;

            // The citadel's throne hall: along the nave's cornices under the vault and over the aisles' arcades.
            var hall = new LatheMesh(Vector3.zero);
            foreach (var side in new[] { -1f, 1f })
                foreach (var (x, y) in new[] { (CitadelHall.NaveX - 0.04f, 10.62f), (CitadelHall.NaveX - 0.04f, 5.05f), (CitadelHall.WallX - 0.04f, 4.75f) })
                    hall.Quad(new Vector3(side * x, y, CitadelHall.NarthexZ + 0.2f), new Vector3(side * x, y + 0.07f, CitadelHall.NarthexZ + 0.2f),
                        new Vector3(side * x, y + 0.07f, CitadelHall.ApseZ - 0.2f), new Vector3(side * x, y, CitadelHall.ApseZ - 0.2f), new Vector3(-side, 0f, 0f), 1f);
            _cityStrips = LatheMesh.Part(_strips.transform, "Citadel", hall.ToMesh("SU_CitadelAlertBars"), _stripMat).transform;
            BuildBeacons();
            ApplyLayout();
            _strips.SetActive(false);
        }

        /// <summary>
        /// Gyrophares high on the walls (dark until an alert): the four corner facets of the bridge; round the
        /// station hall over the gallery, between its bays.
        /// </summary>
        void BuildBeacons()
        {
            if (_art == null)
                return;
            _shipBeacons = new GameObject("ShipBeacons").transform;
            _shipBeacons.SetParent(transform, false);
            var phase = 0f;
            foreach (var e in new[] { BridgeShell.AftStarboard, BridgeShell.BowStarboard, BridgeShell.BowPort, BridgeShell.AftPort })
            {
                var wall = BridgeShell.EdgePoint(e, BridgeShell.EdgeLength(e) * 0.5f, 0f, BridgeShell.WallTop - 0.3f);
                AlertBeacon.MountOnWall(_shipBeacons, "Beacon", wall, BridgeShell.EdgeNormal(e), _art, phase);
                phase += 90f;
            }

            _stationBeacons = new GameObject("StationBeacons").transform;
            _stationBeacons.SetParent(transform, false);
            phase = 0f;
            foreach (var deg in new[] { 45f, 135f, 225f, 315f })
            {
                var wall = StationCommandShell.OnWall(deg, 0f, StationCommandShell.WallTop - 0.55f);
                AlertBeacon.MountOnWall(_stationBeacons, "Beacon", wall, -LatheMesh.Dir(deg), _art, phase);
                phase += 90f;
            }

            // In the citadel: on the nave's walls at the tribunes' height, two either side.
            _cityBeacons = new GameObject("CitadelBeacons").transform;
            _cityBeacons.SetParent(transform, false);
            phase = 0f;
            foreach (var (x, z) in new[] { (-1f, -4.7f), (1f, -4.7f), (1f, 3.1f), (-1f, 3.1f) })
            {
                var wall = new Vector3(x * (CitadelHall.NaveX + 0.6f), 7.4f, z);
                AlertBeacon.MountOnWall(_cityBeacons, "Beacon", wall, new Vector3(-x, 0f, 0f), _art, phase);
                phase += 90f;
            }
        }

        CicArtKit _art;
        Transform _cityStrips;
        Transform _cityBeacons;
        ViewMode _mode;
        Transform _shipStrips;
        Transform _stationStrips;
        Transform _shipBeacons;
        Transform _stationBeacons;
        bool _station;

        /// <summary>Ship bridge, station command hall or citadel throne hall: which room the alert bars run round.</summary>
        public void SetLayout(ViewMode mode)
        {
            _mode = mode;
            _station = mode == ViewMode.Station;
            ApplyLayout();
            // The room's lights were just re-dressed for this layout: that is their rest state now.
            if (_lights != null)
                CacheLights();
        }

        void ApplyLayout()
        {
            if (_shipStrips != null)
                _shipStrips.gameObject.SetActive(_mode == ViewMode.Ship);
            if (_stationStrips != null)
                _stationStrips.gameObject.SetActive(_station);
            if (_shipBeacons != null)
                _shipBeacons.gameObject.SetActive(_mode == ViewMode.Ship);
            if (_stationBeacons != null)
                _stationBeacons.gameObject.SetActive(_station);
            if (_cityStrips != null)
                _cityStrips.gameObject.SetActive(_mode == ViewMode.City);
            if (_cityBeacons != null)
                _cityBeacons.gameObject.SetActive(_mode == ViewMode.City);
        }

        Transform Strip(Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "AlertBar";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_shipStrips, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _stripMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        void OnDestroy()
        {
            AlertState.Changed -= OnAlert;
            CombatEvents.Hit -= OnHit;
            CombatEvents.Shot -= OnShot;
            CombatEvents.Destroyed -= OnDestroyed;
            CombatEvents.Bombard -= OnBombard;
            CombatEvents.TroopTransfer -= OnTroops;
        }

        void CacheLights()
        {
            _lights = GetComponentsInChildren<Light>(true);
            _baseColor = new Color[_lights.Length];
            _baseIntensity = new float[_lights.Length];
            for (var i = 0; i < _lights.Length; i++)
            {
                _baseColor[i] = _lights[i].color;
                _baseIntensity[i] = _lights[i].intensity;
                if (_lights[i].name == "HublotWash")
                    _wash = _lights[i];
            }
        }

        /// <summary>
        /// The combatant we stand in: our ship or fortress — or, in the citadel, the besieged world itself
        /// (CombatEvents −planetId): its batteries thump through the tower and its hits shake it.
        /// </summary>
        bool Aboard(int fleet) =>
            _focus != null && (fleet > 0 ? fleet == _focus.ViewFleetId
                : fleet < 0 && _focus.Mode == ViewMode.City && -fleet == _focus.ViewPlanetId);

        /// <summary>Red alert lighting and the klaxon follow the ship's condition (a fight, or the commander's call).</summary>
        void OnAlert(AlertLevel was, AlertLevel now)
        {
            var on = now == AlertLevel.Red;
            _alertTarget = on ? 1f : 0f;
            if (on)
            {
                _klaxons = 3;
                _klaxonAt = Time.unscaledTime;
            }
        }

        void OnHit(int fleet, int hull, int shield)
        {
            if (!Aboard(fleet))
                return;
            // The beam has to cross first (board and windows play it): land the hit a beat later.
            _pending.Add(new Pending { At = Time.unscaledTime + 0.16f, Hull = hull, Shield = shield });
        }

        void OnShot(int src, int dst, Color color, bool heavy)
        {
            if (!Aboard(src))
                return;
            var front = transform.TransformPoint(new Vector3(0f, 1.6f, WorldScale.CicDeck * 0.5f - 0.5f));
            // Our battery through the hull: the discharge, and a deep thump for a torpedo.
            CicCue.Laser(front, heavy ? 0.7f : Random.Range(0.95f, 1.1f));
            if (heavy)
                CicCue.Boom(front, 0.35f);
            _washColor = color;
            _washT = 0f;
        }

        /// <summary>Our ship's siege salvo: the same rumble and window glow as a battle shot, in bombardment orange.</summary>
        void OnBombard(int fleet, int planet, bool toPlanet)
        {
            if (!toPlanet || !Aboard(fleet))
                return;
            var front = transform.TransformPoint(new Vector3(0f, 1.6f, WorldScale.CicDeck * 0.5f - 0.5f));
            CicCue.Boom(front, 0.28f);
            _washColor = new Color(1f, 0.45f, 0.2f, 1f);
            _washT = 0f;
        }

        /// <summary>Troop bay cycling aboard: shuttle bay clamps and a launch whoosh.</summary>
        void OnTroops(int fleet, int planet, bool toPlanet, int units)
        {
            if (!Aboard(fleet))
                return;
            var bay = transform.TransformPoint(new Vector3(0f, 0.4f, -WorldScale.CicDeck * 0.5f + 1f));
            CicCue.Deploy(bay);
            CicCue.Whoosh(bay);
        }

        void OnDestroyed(int fleet)
        {
            if (fleet < 0 || !Aboard(fleet))
                return;
            _blackout = 1f;
            CicCue.Explosion(transform.TransformPoint(new Vector3(0f, 1.6f, 0f)), 1f);
            CicCue.Boom(transform.TransformPoint(new Vector3(0f, 1.6f, 0f)), 0.8f);
            for (var i = 0; i < 3; i++)
                Sparks(2f);
        }

        void Land(Pending p)
        {
            var dir = Random.insideUnitCircle.normalized;
            var side = transform.TransformPoint(new Vector3(dir.x * 4.5f, 1.8f, dir.y * 4.5f));
            if (p.Hull > 0)
            {
                var k = Mathf.Clamp01(p.Hull / 60f);
                CicCue.HullImpact(side, 0.5f + k * 0.5f);
                if (k > 0.5f)
                    CicCue.Boom(side, 0.3f + k * 0.3f);
                _hitFlash = 0.6f + k * 0.4f;
                _flicker = 0f;
                _flickerLen = 0.35f + k * 0.4f;
                Sparks(0.6f + k);
            }
            else if (p.Shield > 0)
            {
                // Shield took it: no damage aboard, a blue swell through the windows and a low hum.
                CicCue.ShieldImpact(side);
                _washColor = new Color(0.35f, 0.7f, 1f, 1f);
                _washT = 0f;
            }
        }

        void Sparks(float amount)
        {
            if (_sparks == null)
                return;
            // From the ceiling, away from the captain's head.
            var cam = Camera.main != null ? Camera.main.transform.position : transform.position;
            Vector3 at = default;
            for (var tries = 0; tries < 6; tries++)
            {
                at = transform.TransformPoint(new Vector3(Random.Range(-4f, 4f), WorldScale.CicCeiling - 0.12f,
                    Random.Range(-3f, 4.5f)));
                if ((at - cam).sqrMagnitude > 2.2f)
                    break;
            }

            var n = Mathf.RoundToInt(18 * amount);
            for (var i = 0; i < n; i++)
            {
                var v = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.6f), Random.Range(-1f, 1f)) *
                        Random.Range(0.8f, 2.6f);
                CombatFxKit.Emit(_sparks, at, Color.Lerp(new Color(1f, 0.55f, 0.15f, 1f), Color.white, Random.value * 0.6f),
                    Random.Range(0.02f, 0.04f), Random.Range(0.6f, 1.2f), v);
            }

            CombatFxKit.Emit(_sparks, at, new Color(1f, 0.7f, 0.35f, 1f), 0.45f, 0.25f);
        }

        void UpdateAlertVisuals(float now)
        {
            var pulse = 0.5f + 0.5f * Mathf.Sin(now * 2.6f);
            SwapAccents(_alert > 0f, now);
            if (_strips != null)
            {
                var on = _alert > 0f;
                if (_strips.activeSelf != on)
                    _strips.SetActive(on);
                if (on && _stripMat != null)
                {
                    var c = AlertRed * (0.3f + 2.7f * pulse) * _alert;
                    c.a = 1f;
                    _stripMat.SetColor("_Color", new Color(0.2f, 0.02f, 0.02f, 1f));
                    _stripMat.SetColor("_Emission", c);
                    _stripMat.SetFloat("_EmissionMul", 1.6f);
                }
            }

            if (_vignette != null)
            {
                var k = Mathf.Max(_alert * (0.35f + 0.25f * pulse), _hitFlash);
                _vignette.color.value = Color.Lerp(_vignetteBase, new Color(0.9f, 0.04f, 0.03f), Mathf.Clamp01(k * 1.8f));
                _vignette.intensity.value = Mathf.Lerp(_vignetteBaseIntensity, 0.5f, Mathf.Clamp01(k * 1.4f));
            }
        }

        /// <summary>
        /// The room's accent strips (BridgeDressing: Strip / Accent / Trim) take the pulsing alert material
        /// for the fight and get their own back after. Re-asserted every second: a view change re-dresses.
        /// </summary>
        void SwapAccents(bool on, float now)
        {
            if (on)
            {
                if (now < _accentCheck)
                    return;
                _accentCheck = now + 1f;
                foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r == null || r.sharedMaterial == _stripMat || r.transform.IsChildOf(_strips.transform))
                        continue;
                    var n = r.gameObject.name;
                    if (n.IndexOf("Strip", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Accent", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Trim", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    _accents[r] = r.sharedMaterial;
                    r.sharedMaterial = _stripMat;
                }

                return;
            }

            if (_accents.Count == 0)
                return;
            foreach (var kv in _accents)
                if (kv.Key != null && kv.Key.sharedMaterial == _stripMat)
                    kv.Key.sharedMaterial = kv.Value;
            _accents.Clear();
            _accentCheck = 0f;
        }

        void Update()
        {
            var now = Time.unscaledTime;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (now < _pending[i].At)
                    continue;
                Land(_pending[i]);
                _pending.RemoveAt(i);
            }

            if (_klaxons > 0 && now >= _klaxonAt)
            {
                CicCue.Klaxon(transform.TransformPoint(new Vector3(0f, WorldScale.CicCeiling - 0.3f, 0f)));
                _klaxons--;
                _klaxonAt = now + 0.9f;
            }

            _alert = Mathf.MoveTowards(_alert, _alertTarget, Time.unscaledDeltaTime * 0.8f);
            var dt = Time.unscaledDeltaTime;
            _hitFlash = Mathf.Max(0f, _hitFlash - dt * 2.5f);
            UpdateAlertVisuals(now);
            var idle = _alert <= 0f && _flicker < 0f && _blackout <= 0f && _washT < 0f;
            if (idle && _lights.Length > 0 && _lights[0] != null && _lights[0].color == _baseColor[0])
                return;

            var pulse = 0.55f + 0.45f * Mathf.Sin(now * 2.6f);
            var stutter = 1f;
            if (_flicker >= 0f)
            {
                _flicker += dt;
                stutter = Mathf.PerlinNoise(now * 38f, 0.3f) < 0.45f ? 0.15f : 1f;
                if (_flicker >= _flickerLen)
                    _flicker = -1f;
            }

            if (_blackout > 0f)
            {
                _blackout = Mathf.Max(0f, _blackout - dt * 0.5f);
                stutter *= 1f - _blackout * (0.7f + 0.3f * Mathf.PerlinNoise(now * 20f, 1.7f));
            }

            var wash = 0f;
            if (_washT >= 0f)
            {
                _washT += dt;
                wash = 1f - Mathf.Clamp01(_washT / 0.35f);
                if (_washT >= 0.35f)
                    _washT = -1f;
            }

            for (var i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (l == null)
                    continue;
                // Red alert: the room keeps a dim share of its own light so consoles stay readable.
                var c = Color.Lerp(_baseColor[i], AlertRed, _alert * 0.8f);
                var intensity = _baseIntensity[i] * Mathf.Lerp(1f, 0.45f + 0.55f * pulse, _alert) * stutter;
                if (l == _wash && wash > 0f)
                {
                    c = Color.Lerp(c, _washColor, wash);
                    intensity += wash * 2.2f;
                }

                l.color = c;
                l.intensity = intensity;
            }
        }
    }
}
