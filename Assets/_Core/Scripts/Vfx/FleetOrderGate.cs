using System;
using Core.App;

namespace Core.Vfx
{
    /// <summary>
    /// Feasibility gates for diegetic orders — only show what ActionJs can accept now.
    /// </summary>
    public static class FleetOrderGate
    {
        public static long UnixNow() =>
            (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        public static bool HasShip(FocusContext focus) =>
            focus != null && focus.FindViewFleet() != null;

        public static bool CanMove(FocusFleet fleet) =>
            fleet != null && fleet.CanIssueMove(UnixNow());

        public static bool AtStar(FocusFleet fleet) =>
            fleet != null && fleet.PlanetId <= 0 && fleet.AsteroidId <= 0;

        public static bool AtPlanet(FocusFleet fleet, int planetId) =>
            fleet != null && planetId > 0 && fleet.PlanetId == planetId;

        public static bool AtAsteroid(FocusFleet fleet, int asteroidId) =>
            fleet != null && asteroidId > 0 && fleet.AsteroidId == asteroidId;

        public static bool InAnyOrbit(FocusFleet fleet) =>
            fleet != null && (fleet.PlanetId > 0 || fleet.AsteroidId > 0);

        /// <summary>Leave orbit / park at current system star.</summary>
        public static bool CanGoToStar(FocusFleet fleet) =>
            CanMove(fleet) && InAnyOrbit(fleet);

        public static bool CanMoveToPlanet(FocusFleet fleet, int planetId) =>
            CanMove(fleet) && planetId > 0 && !AtPlanet(fleet, planetId);

        public static bool CanMoveToAsteroid(FocusFleet fleet, int asteroidId) =>
            CanMove(fleet) && asteroidId > 0 && !AtAsteroid(fleet, asteroidId);

        public static bool CanJumpSystem(FocusFleet fleet) =>
            CanMove(fleet);

        /// <summary>Stance buttons — docked at a planet. A fortress's stance is locked (defends its planet).</summary>
        public static bool CanStance(FocusFleet fleet) =>
            fleet != null && !fleet.IsStation && fleet.PlanetId > 0 && !fleet.IsInBattle;

        /// <summary>Nothing running on this hull (a fortress included) — survey, assault.</summary>
        public static bool IsIdle(FocusFleet fleet) =>
            fleet != null && fleet.IsIdle(UnixNow());

        /// <summary>
        /// Why the server refuses any move / queue / gate / cargo / auto order to this hull before looking further:
        /// an orbital fortress (stationCannotMove), a marketplace convoy (fleetIsBusy, web 0417693). Null = neither.
        /// </summary>
        public static string LockKey(FocusFleet fleet) =>
            fleet == null ? null : fleet.IsStation ? "stationCannotMove" : fleet.IsTrading ? "fleetIsBusy" : null;

        /// <summary>Orbital fortress: every MoveFleet* / queue / gate / PRL order is refused (stationCannotMove).</summary>
        public static bool IsAnchored(FocusFleet fleet) =>
            fleet != null && fleet.IsStation;

        /// <summary>
        /// Our ship can open a tactical battle on that one (actionjs MakeBattle): same system, neither travelling nor
        /// already fighting, a hostile (enemy empire or pirate). A pirate is fought in open space wherever both
        /// are in the system; anyone else only where both are (same orbit, or both off any world).
        /// </summary>
        public static bool CanEngage(FocusFleet mine, FocusFleet target, long now)
        {
            if (mine == null || target == null || mine.Id == target.Id || mine.UserId == target.UserId)
                return false;
            if (mine.IsInBattle || target.IsInBattle || mine.IsMoving(now) || target.IsMoving(now) ||
                mine.SystemId <= 0 || mine.SystemId != target.SystemId)
                return false;
            var stance = DiplomacyIndex.ResolveFleet(target);
            var pirate = target.IsPirate || stance == EmpireStance.Pirate;
            if (!pirate && stance != EmpireStance.Enemy)
                return false;
            return pirate || target.PlanetId == mine.PlanetId;
        }

        /// <summary>MakeBattle's spot: open space against pirates, else the orbit both hold (web startTacticalBattle).</summary>
        public static int EngagePlanet(FocusFleet mine, FocusFleet target) =>
            target.IsPirate || DiplomacyIndex.ResolveFleet(target) == EmpireStance.Pirate ? 0 : mine.PlanetId;

        public static bool CanMine(FocusFleet fleet) =>
            CanMove(fleet) && fleet.AsteroidId > 0;

        public static bool CanExplore(FocusFleet fleet) =>
            IsIdle(fleet) && fleet.PlanetId > 0;

        public static bool CanSiege(FocusFleet fleet, FocusContext focus)
        {
            if (!IsIdle(fleet) || fleet.PlanetId <= 0 || focus == null)
                return false;
            var planet = focus.FindPlanet(fleet.PlanetId);
            if (planet == null)
                return false;
            var me = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            return planet.UserId > 0 && me > 0 && planet.UserId != me;
        }

        public static bool CanCargo(FocusFleet fleet, FocusContext focus)
        {
            // A convoy's hold carries the payment or the goods: DepositCargo / WithdrawCargo refuse it (web 0417693).
            if (fleet == null || fleet.PlanetId <= 0 || focus == null || fleet.IsTrading)
                return false;
            var planet = focus.FindPlanet(fleet.PlanetId);
            if (planet == null)
                return false;
            var me = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            return me > 0 && planet.UserId == me;
        }

        public static string BusyKey(FocusFleet fleet)
        {
            // Native GetTranslations keys (web fleet.js busy toasts).
            if (fleet == null)
                return "noFleet";
            var now = UnixNow();
            if (fleet.IsInBattle)
                return "fleetIsInBattle";
            // A marketplace convoy flies the guild's route until it is home (the server lets it be re-ordered).
            if (fleet.IsTrading)
                return "fleetIsBusy";
            if (fleet.IsMoving(now))
                return "fleetIsMoving";
            if (fleet.IsSieging(now))
                return "fleetIsAttacking";
            if (fleet.IsHarvesting(now))
                return "fleetIsHarvesting";
            if (fleet.IsExploring(now))
                return "fleetIsExploring";
            if (fleet.IsStation)
                return "stationCannotMove";
            return "Loading";
        }
    }
}
