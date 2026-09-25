using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.App
{
    /// <summary>One orbital siege touching us: our ships attacking a world, or hostiles attacking one of ours.</summary>
    public sealed class Siege
    {
        public int PlanetId;
        public int SystemId;
        public string PlanetName = string.Empty;
        public readonly List<int> Attackers = new();
        /// <summary>Unix seconds when the assault lands (fleet attackEndTime).</summary>
        public long EndTime;
        public bool OurPlanet;
        public bool OurAttack;
    }

    /// <summary>
    /// Orbital sieges (FleetAttackPlanet) are resolved lazily by the server: nothing happens when the
    /// attack timer runs out until someone calls CheckPlanetAttack on the planet (web: opening the planet
    /// window). The bridge does it for every siege it is part of, as soon as the timer is up —
    /// "wip" = the fight is still being simulated (retry), "ok" = resolved, "ko" = no attack left.
    /// Also voices a new attack on one of our worlds and the outcome.
    /// </summary>
    public sealed class SiegeWatch : MonoBehaviour
    {
        const float WipRetry = 5f;
        const float IdleRetry = 30f;

        public static SiegeWatch Instance { get; private set; }

        FocusContext _focus;
        FleetPoller _poller;
        readonly List<Siege> _sieges = new();
        readonly Dictionary<int, float> _nextCheck = new();
        readonly HashSet<int> _announced = new();
        bool _inFlight;
        bool _seeded;
        float _tick;

        public IReadOnlyList<Siege> Sieges => _sieges;
        public event System.Action Changed;

        public static SiegeWatch Build(Transform host, FocusContext focus, FleetPoller poller)
        {
            var w = host.gameObject.AddComponent<SiegeWatch>();
            w._focus = focus;
            w._poller = poller;
            if (focus != null)
                focus.FleetsChanged += w.Rebuild;
            return w;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (_focus != null)
                _focus.FleetsChanged -= Rebuild;
            if (Instance == this)
                Instance = null;
        }

        public bool TryGet(int planetId, out Siege siege)
        {
            foreach (var s in _sieges)
            {
                if (s.PlanetId == planetId)
                {
                    siege = s;
                    return true;
                }
            }

            siege = null;
            return false;
        }

        void Rebuild()
        {
            _sieges.Clear();
            if (_focus == null)
                return;
            var me = FocusContext.OwnedUserId();
            foreach (var f in _focus.Fleets)
            {
                if (f.AttackEndTime <= 0 || f.PlanetId <= 0)
                    continue;
                var ourPlanet = OwnedPlanets.Contains(f.PlanetId);
                var mine = f.UserId == me;
                // The owner's own ships at the planet never attack it.
                if (mine && ourPlanet)
                    continue;
                if (!mine && !ourPlanet)
                    continue;
                if (!TryGet(f.PlanetId, out var s))
                {
                    s = new Siege
                    {
                        PlanetId = f.PlanetId,
                        SystemId = f.SystemId,
                        OurPlanet = ourPlanet,
                        PlanetName = PlanetName(f.PlanetId)
                    };
                    _sieges.Add(s);
                }

                s.Attackers.Add(f.Id);
                s.OurAttack |= mine;
                s.EndTime = s.EndTime == 0 ? f.AttackEndTime : System.Math.Min(s.EndTime, f.AttackEndTime);
            }

            foreach (var s in _sieges)
            {
                // Hostiles over one of our worlds: Tactical calls it once (not on the first read at boot).
                if (s.OurPlanet && !s.OurAttack && _announced.Add(s.PlanetId) && _seeded)
                    Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Tactical, "underAttack", 3, s.PlanetName);
            }

            _announced.RemoveWhere(id => !TryGet(id, out _));
            _seeded = true;
            Changed?.Invoke();
        }

        string PlanetName(int planetId)
        {
            var p = _focus.FindPlanet(planetId);
            if (p != null && !string.IsNullOrEmpty(p.Name))
                return p.Name;
            if (GalaxyCatalog.TryGetPlanet(planetId, out var r) && !string.IsNullOrEmpty(r.Name))
                return r.Name;
            return "#" + planetId;
        }

        void Update()
        {
            if (_inFlight || Time.unscaledTime < _tick)
                return;
            _tick = Time.unscaledTime + 1f;
            var now = FleetOrderGate.UnixNow();
            foreach (var s in _sieges)
            {
                if (s.EndTime > now)
                    continue;
                if (_nextCheck.TryGetValue(s.PlanetId, out var at) && Time.unscaledTime < at)
                    continue;
                AsyncTap.Run(Check(s));
                return;
            }
        }

        async Task Check(Siege s)
        {
            _inFlight = true;
            try
            {
                var res = await ActionJs.Get("CheckPlanetAttack", new Dictionary<string, string>
                {
                    { "planet", s.PlanetId.ToString() }
                });
                var body = res.Ok ? (res.Body ?? string.Empty).Trim() : string.Empty;
                if (body == "wip")
                {
                    _nextCheck[s.PlanetId] = Time.unscaledTime + WipRetry;
                    return;
                }

                _nextCheck[s.PlanetId] = Time.unscaledTime + IdleRetry;
                if (body != "ok")
                    return;
                await Resolved(s);
            }
            finally
            {
                _inFlight = false;
            }
        }

        async Task Resolved(Siege s)
        {
            if (s.OurPlanet)
                await OwnedPlanets.Refresh();
            if (_poller != null)
                await _poller.PollNow();
            if (EconomyService.Instance != null)
                await EconomyService.Instance.RefreshNow();

            // The losing side's ships are deleted server-side (DelFleet): who is left tells the outcome.
            bool attackersWon;
            if (s.OurAttack)
            {
                attackersWon = false;
                foreach (var id in s.Attackers)
                    attackersWon |= _focus.FindFleet(id) != null;
            }
            else
            {
                attackersWon = !OwnedPlanets.Contains(s.PlanetId);
            }

            CombatEvents.RaiseSiegeResolved(s.PlanetId, attackersWon);
            string evt;
            if (s.OurAttack)
                evt = attackersWon ? "siegeWon" : "siegeFailed";
            else
                evt = attackersWon ? "planetLost" : "planetHeld";
            Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Tactical, evt, 3, s.PlanetName);
        }
    }
}
