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

        /// <summary>Stance buttons — docked at a planet.</summary>
        public static bool CanStance(FocusFleet fleet) =>
            fleet != null && fleet.PlanetId > 0 && !fleet.IsInBattle;

        public static bool CanMine(FocusFleet fleet) =>
            CanMove(fleet) && fleet.AsteroidId > 0;

        public static bool CanExplore(FocusFleet fleet) =>
            CanMove(fleet) && fleet.PlanetId > 0;

        public static bool CanSiege(FocusFleet fleet, FocusContext focus)
        {
            if (!CanMove(fleet) || fleet.PlanetId <= 0 || focus == null)
                return false;
            var planet = focus.FindPlanet(fleet.PlanetId);
            if (planet == null)
                return false;
            var me = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            return planet.UserId > 0 && me > 0 && planet.UserId != me;
        }

        public static bool CanCargo(FocusFleet fleet, FocusContext focus)
        {
            if (fleet == null || fleet.PlanetId <= 0 || focus == null)
                return false;
            var planet = focus.FindPlanet(fleet.PlanetId);
            if (planet == null)
                return false;
            var me = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            return me > 0 && planet.UserId == me;
        }

        public static string BusyKey(FocusFleet fleet)
        {
            if (fleet == null)
                return "spaceships";
            var now = UnixNow();
            if (fleet.IsInBattle)
                return "battle";
            if (fleet.IsMoving(now))
                return "Loading";
            if (fleet.IsSieging(now))
                return "Siege";
            if (fleet.IsHarvesting(now))
                return "Mine";
            if (fleet.IsExploring(now))
                return "Explore";
            return "Loading";
        }
    }
}
