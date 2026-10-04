using System.Collections.Generic;
using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>
    /// Interceptions ordered from the galaxy table: one of our ships flies to a hostile ship's system (the usual travel
    /// order); once it has arrived and the target is still there, the tactical battle opens (MakeBattle, as the
    /// Tactical officer's Attack). The server has no "attack" queue step, so the watch lives here, for this session:
    /// a target that leaves, vanishes from our sensors or is destroyed drops the order, and the officer says so.
    /// </summary>
    public sealed class HuntOrders : MonoBehaviour
    {
        public static HuntOrders Instance { get; private set; }

        /// <summary>Our ship → the hostile ship it is going after.</summary>
        readonly Dictionary<int, int> _hunts = new();
        readonly List<int> _scratch = new();
        float _next;
        bool _busy;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static HuntOrders Ensure(GameObject host) =>
            Instance != null ? Instance : host.AddComponent<HuntOrders>();

        /// <summary>The ship is on its way: watch for its arrival.</summary>
        public void Track(int fleetId, int targetId)
        {
            _hunts[fleetId] = targetId;
            _next = 0f;
        }

        /// <summary>The ship this one is hunting (0 = none).</summary>
        public int TargetOf(int fleetId) => _hunts.TryGetValue(fleetId, out var t) ? t : 0;

        public void Cancel(int fleetId) => _hunts.Remove(fleetId);

        void Update()
        {
            if (_hunts.Count == 0 || _busy || Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 2f;
            var focus = FocusContext.Current;
            if (focus == null)
                return;
            var now = FleetOrderGate.UnixNow();
            _scratch.Clear();
            _scratch.AddRange(_hunts.Keys);
            foreach (var id in _scratch)
            {
                var targetId = _hunts[id];
                var mine = focus.FindFleet(id);
                var foe = focus.FindFleet(targetId);
                if (mine == null)
                {
                    _hunts.Remove(id);
                    continue;
                }

                if (mine.IsMoving(now) || mine.IsInBattle)
                    continue;
                if (foe == null || foe.SystemId != mine.SystemId)
                {
                    // Gone, out of sight, or elsewhere by the time we came: the order lapses.
                    _hunts.Remove(id);
                    Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Tactical, "MakeBattle",
                        ApiResult.Fail(Trans.Get("vr.hunt.lost")), Name(foe, targetId));
                    continue;
                }

                if (!FleetOrderGate.CanEngage(mine, foe, now))
                    continue;
                _hunts.Remove(id);
                AsyncTap.Run(Engage(mine, foe));
                return;
            }
        }

        async System.Threading.Tasks.Task Engage(FocusFleet mine, FocusFleet foe)
        {
            var hex = HexBattleController.Instance;
            if (hex == null)
                return;
            _busy = true;
            try
            {
                var result = await hex.MakeBattle(new[] { mine.Id, foe.Id }, FleetOrderGate.EngagePlanet(mine, foe));
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Tactical, "MakeBattle", result, Name(foe, foe.Id));
            }
            finally
            {
                _busy = false;
            }
        }

        static string Name(FocusFleet f, int id) =>
            f != null && !string.IsNullOrEmpty(f.Name) ? f.Name : "#" + (f != null ? f.Id : id);
    }
}
