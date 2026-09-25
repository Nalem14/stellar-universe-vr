using System.Collections.Generic;
using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Crew
{
    /// <summary>
    /// The crew talks: one intercom, one officer at a time. Lines come from order results (who issued
    /// what, success / failure / server notice) and from world changes seen in the polls (arrival, hostile
    /// contact, battle, survey done), plus a greeting and sparse idle chatter. Each line: radio chirp at
    /// the officer, visor pulse + head turn to the captain, subtitle on <see cref="CrewSubtitle"/>.
    /// Text is always Trans(crew.&lt;role&gt;.&lt;event&gt;.&lt;n&gt;); variants never repeat back to back.
    /// World checks run on a 1 s tick, never per frame.
    /// </summary>
    public sealed class BarkDirector : MonoBehaviour
    {
        public static BarkDirector Instance { get; private set; }

        struct Bark
        {
            public CrewDialogue.Role Role;
            public string Event;
            public object[] Args;
            public int Priority;
            public float Queued;
        }

        const float MaxQueueAge = 8f;
        const float Gap = 0.5f;
        const float IdleAfter = 80f;

        readonly Dictionary<CrewDialogue.Role, CrewOfficer> _officers = new();
        readonly Dictionary<CrewDialogue.Role, string> _stationTitleKeys = new();
        readonly Dictionary<string, int> _lastVariant = new();
        readonly Dictionary<string, float> _cooldownUntil = new();
        readonly List<Bark> _queue = new();
        readonly HashSet<int> _seenHostiles = new();

        FocusContext _focus;
        CrewSubtitle _subtitle;
        Transform _room;
        float _busyUntil;
        float _lastLineAt;
        float _nextIdle;
        float _nextWorldTick;
        bool _greeted;
        int _trackedSystem = -1;
        int _trackedFleet = -1;
        bool _wasMoving;
        bool _wasInBattle;
        bool _wasExploring;
        int _exploringPlanet;
        EconomyService _economy;

        void OnBuildingCompleted(PlanetEconomy planet, string type) =>
            Say(CrewDialogue.Role.Ops, "buildDone", 2, Trans.Get(type),
                string.IsNullOrEmpty(planet.Name) ? "#" + planet.Id : planet.Name);

        void OnResearchCompleted(string tech) =>
            Say(CrewDialogue.Role.Science, "researchDone", 2, Trans.Get(tech));

        public static BarkDirector Build(Transform room, FocusContext focus)
        {
            var go = new GameObject("BarkDirector");
            go.transform.SetParent(room, false);
            var director = go.AddComponent<BarkDirector>();
            director._room = room;
            director._focus = focus;
            director._subtitle = CrewSubtitle.Build(room);
            director._nextIdle = Time.time + IdleAfter;
            if (focus != null)
                focus.Changed += director.OnViewChanged;
            Instance = director;
            var economy = EconomyService.Ensure(room);
            economy.BuildingCompleted += director.OnBuildingCompleted;
            economy.ResearchCompleted += director.OnResearchCompleted;
            director._economy = economy;
            return director;
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnViewChanged;
            if (_economy != null)
            {
                _economy.BuildingCompleted -= OnBuildingCompleted;
                _economy.ResearchCompleted -= OnResearchCompleted;
            }
            if (Instance == this)
                Instance = null;
        }

        /// <summary>The officer manning <paramref name="role"/>'s station, if any.</summary>
        public CrewOfficer Officer(CrewDialogue.Role role) => _officers.TryGetValue(role, out var o) ? o : null;

        public void Register(CrewDialogue.Role role, CrewOfficer officer, string stationTitleKey)
        {
            if (officer == null)
                return;
            _officers[role] = officer;
            _stationTitleKeys[role] = stationTitleKey;
        }

        /// <summary>Queue a line. Higher priority speaks first; stale low-priority lines are dropped.</summary>
        public void Say(CrewDialogue.Role role, string evt, int priority = 1, params object[] args)
        {
            if (CrewLines.Count(role, evt) <= 0)
                return;
            var cdKey = CrewLines.RoleKey(role) + "." + evt;
            if (_cooldownUntil.TryGetValue(cdKey, out var until) && Time.time < until)
                return;
            _cooldownUntil[cdKey] = Time.time + 4f;
            _queue.Add(new Bark { Role = role, Event = evt, Args = args, Priority = priority, Queued = Time.time });
            enabled = true;
        }

        /// <summary>
        /// Voice the outcome of an order issued from <paramref name="role"/>'s station or the table.
        /// <paramref name="target"/> fills {0} (destination, planet, rock, ship).
        /// </summary>
        public void OrderResult(CrewDialogue.Role role, string action, ApiResult result, string target)
        {
            if (!result.Ok)
            {
                Say(role, "fail", 3);
                return;
            }

            var notice = CrewLines.NoticeEvent(result.NoticeKey);
            if (notice != null)
            {
                Say(CrewDialogue.Role.Helm, notice, 3, target);
                return;
            }

            var evt = CrewLines.AckEvent(action);
            if (evt == null)
                return;
            if (CrewLines.Count(role, evt) <= 0)
                evt = "ack";
            Say(role, evt, 2, target);
        }

        void OnViewChanged()
        {
            // New system or new inhabited view: re-seed world tracking silently, then report what is here.
            _seenHostiles.Clear();
            _trackedSystem = -1;
            _trackedFleet = -1;
            _nextWorldTick = Time.time + 1.5f;
        }

        void Update()
        {
            if (Time.time >= _nextWorldTick)
            {
                _nextWorldTick = Time.time + 1f;
                WatchWorld();
            }

            if (Time.time >= _nextIdle && _queue.Count == 0 && Time.time - _lastLineAt > IdleAfter)
            {
                _nextIdle = Time.time + IdleAfter + Random.value * 60f;
                SayIdle();
            }

            if (_queue.Count == 0 || Time.time < _busyUntil)
                return;
            SpeakNext();
        }

        void SpeakNext()
        {
            var best = -1;
            for (var i = _queue.Count - 1; i >= 0; i--)
            {
                var b = _queue[i];
                if (Time.time - b.Queued > MaxQueueAge && b.Priority < 3)
                {
                    _queue.RemoveAt(i);
                    continue;
                }
            }

            for (var i = 0; i < _queue.Count; i++)
            {
                if (best < 0 || _queue[i].Priority > _queue[best].Priority)
                    best = i;
            }

            if (best < 0)
                return;
            var bark = _queue[best];
            _queue.RemoveAt(best);
            Speak(bark);
        }

        void Speak(Bark bark)
        {
            if (!_officers.TryGetValue(bark.Role, out var officer) || officer == null ||
                !officer.gameObject.activeInHierarchy)
                return;

            var count = CrewLines.Count(bark.Role, bark.Event);
            var slot = CrewLines.RoleKey(bark.Role) + "." + bark.Event;
            var pick = 1 + Random.Range(0, count);
            if (count > 1 && _lastVariant.TryGetValue(slot, out var last) && last == pick)
                pick = pick % count + 1;
            _lastVariant[slot] = pick;

            var args = bark.Args ?? System.Array.Empty<object>();
            var text = args.Length > 0
                ? Trans.Format(CrewLines.Key(bark.Role, bark.Event, pick), args)
                : Trans.Get(CrewLines.Key(bark.Role, bark.Event, pick));
            var speaker = _stationTitleKeys.TryGetValue(bark.Role, out var titleKey) ? Trans.Get(titleKey) : string.Empty;

            var duration = _subtitle != null ? _subtitle.Show(speaker, officer.Accent, text) : 3f;
            var cam = Camera.main;
            officer.Speak(duration, cam != null ? cam.transform.position : officer.transform.position);
            CicCue.RadioOpen(officer.MouthPosition);
            _busyUntil = Time.time + duration + Gap;
            _lastLineAt = Time.time;
            Invoke(nameof(CloseChannel), Mathf.Max(0.2f, duration - 0.25f));
            _closeAt = officer.MouthPosition;
        }

        Vector3 _closeAt;

        void CloseChannel() => CicCue.RadioClose(_closeAt);

        void SayIdle()
        {
            var roles = new List<CrewDialogue.Role>();
            foreach (var kv in _officers)
            {
                if (kv.Value != null && kv.Value.gameObject.activeInHierarchy && CrewLines.Count(kv.Key, "idle") > 0)
                    roles.Add(kv.Key);
            }

            if (roles.Count > 0)
                Say(roles[Random.Range(0, roles.Count)], "idle", 0);
        }

        /// <summary>Poll-derived triggers (FocusContext is already refreshed by FleetPoller).</summary>
        void WatchWorld()
        {
            if (_focus == null || !_focus.HasSystem)
                return;
            var now = FleetOrderGate.UnixNow();
            var ship = _focus.FindViewFleet();
            var seeding = _trackedSystem != _focus.SystemId || _trackedFleet != (ship != null ? ship.Id : 0);

            if (!_greeted)
            {
                _greeted = true;
                Say(ship != null ? CrewDialogue.Role.Helm : CrewDialogue.Role.Ops, "greet", 1);
            }

            if (ship != null)
            {
                var moving = ship.IsMoving(now);
                var exploring = ship.IsExploring(now);
                if (!seeding)
                {
                    if (_wasMoving && !moving)
                        Say(CrewDialogue.Role.Helm, "arrive", 2, SystemName());
                    if (!_wasInBattle && ship.IsInBattle)
                        Say(CrewDialogue.Role.Tactical, "battleStart", 3);
                    if (_wasExploring && !exploring)
                        Say(CrewDialogue.Role.Science, "exploreDone", 2, PlanetName(_exploringPlanet));
                }

                _wasMoving = moving;
                _wasInBattle = ship.IsInBattle;
                _wasExploring = exploring;
                if (exploring)
                    _exploringPlanet = ship.PlanetId;
            }

            // Hostiles entering (or already here when we arrive: one report, not one per ship).
            var newHostile = (FocusFleet)null;
            var newCount = 0;
            foreach (var fleet in _focus.Fleets)
            {
                if (!fleet.VisibleIn(_focus.SystemId, now))
                    continue;
                var stance = DiplomacyIndex.ResolveFleet(fleet);
                if (stance != EmpireStance.Enemy && stance != EmpireStance.Pirate && !fleet.IsPirate)
                    continue;
                if (_seenHostiles.Add(fleet.Id))
                {
                    newHostile ??= fleet;
                    newCount++;
                }
            }

            if (newHostile != null)
            {
                var name = string.IsNullOrEmpty(newHostile.Name) ? "#" + newHostile.Id : newHostile.Name;
                var pirate = newHostile.IsPirate || DiplomacyIndex.ResolveFleet(newHostile) == EmpireStance.Pirate;
                if (pirate)
                    Say(CrewDialogue.Role.Tactical, "pirate", 3, name, string.Empty);
                else
                    Say(CrewDialogue.Role.Tactical, seeding ? "hostile" : "contact", 3, name);
            }

            _trackedSystem = _focus.SystemId;
            _trackedFleet = ship != null ? ship.Id : 0;
        }

        string SystemName() => BridgeViewscreen.SystemLabel(_focus);

        string PlanetName(int planetId)
        {
            var p = planetId > 0 ? _focus.FindPlanet(planetId) : null;
            return p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : planetId > 0 ? "#" + planetId : string.Empty;
        }
    }
}
