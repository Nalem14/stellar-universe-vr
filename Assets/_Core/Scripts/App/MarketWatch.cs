using System.Collections.Generic;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Follows our marketplace convoys on the fleet snapshot (fleets.tradeMissionId, no extra poll): a hull that
    /// vanishes mid-mission was intercepted and its cargo looted (Tactical), a hull whose mission tag clears came
    /// home with the goods (Ops). It also reads the loot of a won fight off our holds (Engineering + a flare on the
    /// battle board). The crew says it wherever the captain is; the exchange shows the details.
    /// </summary>
    public sealed class MarketWatch : MonoBehaviour
    {
        FocusContext _focus;
        readonly Dictionary<int, (int mission, string name)> _flying = new();
        readonly Dictionary<int, (int mission, string name)> _now = new();
        /// <summary>Our hulls in a fight and their hold when it started (a won fight can fill it with the wreck's cargo).</summary>
        readonly Dictionary<int, (int mineral, int crystal, int biomass)> _fighting = new();
        readonly List<int> _done = new();
        bool _seeded;

        public static MarketWatch Build(Transform host, FocusContext focus)
        {
            var w = host.gameObject.AddComponent<MarketWatch>();
            w._focus = focus;
            if (focus != null)
            {
                focus.FleetsChanged += w.Rebuild;
                focus.Changed += w.Rebuild;
            }

            return w;
        }

        void OnDestroy()
        {
            if (_focus == null)
                return;
            _focus.FleetsChanged -= Rebuild;
            _focus.Changed -= Rebuild;
        }

        void Rebuild()
        {
            // An empty snapshot is a failed read, not every ship lost.
            if (_focus == null || _focus.Fleets.Count == 0)
                return;
            var me = FocusContext.OwnedUserId();
            _now.Clear();
            foreach (var f in _focus.Fleets)
                if (f.IsOwnedBy(me) && f.IsTrading)
                    _now[f.Id] = (f.TradeMissionId, f.Name);

            if (_seeded)
            {
                var bark = Core.Crew.BarkDirector.Instance;
                foreach (var kv in _flying)
                {
                    if (_now.ContainsKey(kv.Key))
                        continue;
                    var still = _focus.FindFleet(kv.Key);
                    if (still == null)
                        bark?.Say(CrewDialogue.Role.Tactical, "convoyLost", 3, kv.Value.name);
                    else if (OwnedPlanets.Contains(still.PlanetId))
                        bark?.Say(CrewDialogue.Role.Ops, "marketReturned", 2, PlanetName(still.PlanetId));
                }
            }

            _flying.Clear();
            foreach (var kv in _now)
                _flying[kv.Key] = kv.Value;
            WatchLoot(me);
            _seeded = true;
        }

        /// <summary>
        /// Cargo looting (web <c>HandleCombatFleetDestruction</c>): the victors' holds take the wreck's minerals,
        /// crystal and biomass. Nothing logs the amounts, so the hold is read before and after the fight.
        /// </summary>
        void WatchLoot(int me)
        {
            foreach (var f in _focus.Fleets)
                if (f.IsOwnedBy(me) && f.IsInBattle && !_fighting.ContainsKey(f.Id))
                    _fighting[f.Id] = (f.MineralCargo, f.CrystalCargo, f.BiomassCargo);

            _done.Clear();
            foreach (var kv in _fighting)
            {
                var f = _focus.FindFleet(kv.Key);
                if (f != null && f.IsInBattle)
                    continue;
                _done.Add(kv.Key);
                if (f == null)
                    continue;
                var m = f.MineralCargo - kv.Value.mineral;
                var c = f.CrystalCargo - kv.Value.crystal;
                var b = f.BiomassCargo - kv.Value.biomass;
                if (m <= 0 && c <= 0 && b <= 0)
                    continue;
                var parts = new List<string>(3);
                if (m > 0)
                    parts.Add("+" + m.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " " + Trans.Get("mineralResource"));
                if (c > 0)
                    parts.Add("+" + c.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " " + Trans.Get("crystalResource"));
                if (b > 0)
                    parts.Add("+" + b.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " " + Trans.Get("biomassResource"));
                var haul = string.Join(" · ", parts);
                HexBattleController.Instance?.ShowLoot(f.Id, Trans.Get("market_cargo_looted") + "\n" + haul);
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Engineering, "cargoLooted", 3, haul, f.Name);
            }

            foreach (var id in _done)
                _fighting.Remove(id);
        }

        static string PlanetName(int id)
        {
            foreach (var p in OwnedPlanets.All)
                if (p.Id == id)
                    return p.Name;
            return string.Empty;
        }
    }
}
