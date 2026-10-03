using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Nested spatial scales. Full contract: docs/SCALE.md.
    /// Room = 1:1 VR. System = stylized hublot space. Galaxy / zone = holo table only.
    /// </summary>
    public static class WorldScale
    {
        public const float CicDeck = 12f;
        public const float CicCeiling = 3.4f;

        /// <summary>Table top height above deck (chest reach).</summary>
        public const float CicTableHeight = 0.88f;
        /// <summary>Holo table platter diameter.</summary>
        public const float CicTableDiameter = 2.4f;

        // --- Bridge floor plan (interior local metres, +Z = forward bulkhead / hublots). docs/SCALE.md ---

        /// <summary>Holo table centre on the deck (Z).</summary>
        public const float CicTableCenterZ = 0.6f;
        /// <summary>Captain chair centre (Z), on its dais aft of the table: 1.4 m of deck between seat and rim, so standing at the table never lands in it.</summary>
        public const float CicCaptainChairZ = -2.0f;
        /// <summary>Standing captain spot: just aft of the table rim, in front of the chair.</summary>
        public static readonly Vector3 CicCaptainStand = new(0f, 0f, -0.75f);
        /// <summary>Crew stations horseshoe: radius from the table centre.</summary>
        public const float CicStationArcRadius = 3.1f;
        /// <summary>Standing / seated eye heights used to aim screens.</summary>
        public const float EyeStanding = 1.6f;
        public const float EyeSeated = 1.18f;

        public const float CicHublotWidth = 1.8f;
        public const float CicHublotHeight = 1.1f;
        public const float CicHublotCenterY = 1.65f;

        /// <summary>Orbital station command hall: a rotunda centred on the holo table (the ship bridge is a 12 m octagon).</summary>
        public const float StationHallRadius = 8.6f;
        /// <summary>The station's habitat ring around its hub: centre-line radius and drop of its plane under the command deck.</summary>
        public const float StationRingRadius = 46f;
        public const float StationRingDrop = 6f;
        /// <summary>Ring cross-section (width across, height): the concourse runs inside it.</summary>
        public static readonly Vector2 StationRingSection = new(12f, 8f);

        public const int ShipGrid = 9;
        public const int ShipCoreCell = 4;
        public const float ShipCell = 2.2f;
        public const float ShipSpan = ShipGrid * ShipCell;

        public const float StarRadius = 36f;
        public const float PlanetRadiusMin = 14f;
        public const float PlanetRadiusStep = 1.6f;
        public const float AsteroidRadius = 3.6f;

        public const float OrbitBase = 120f;
        public const float OrbitStep = 58f;
        public const float EclipticHeight = 8f;

        public const float FleetParkPadding = 10f;
        public const float FleetLateral = 14f;
        public const float FleetLateralStep = 8f;

        public const float BridgeFarClip = 1100f;
        public const float BridgeFogDensity = 0.0032f;

        // ── City (planet view: the citadel on top of its tower) — a fourth layer, never mixed with the system's ──

        /// <summary>
        /// How far under the system scene the city stands (m): well past <see cref="BridgeFarClip"/> and
        /// <see cref="CityFarClip"/>, so no star, planet or ship of the system ever shows over the city.
        /// </summary>
        public const float CityDepth = 6000f;
        /// <summary>Command deck (the rotunda floor) above the city's ground: the citadel's tower.</summary>
        public const float CityTowerHeight = 260f;
        /// <summary>Radius of the tower's shaft at its foot, and where the first district ring starts.</summary>
        public const float CityTowerFoot = 34f;
        public const float CityPlazaRadius = 72f;
        /// <summary>Outer edge of the built city (the defence wall stands just outside).</summary>
        public const float CityRadius = 760f;
        /// <summary>The mountain / cloud-bank ring on the horizon, and the far edge of the land.</summary>
        public const float CityHorizon = 1150f;
        public const float CityLandEdge = 1400f;
        /// <summary>Sky dome radius (follows the eye) — inside the far clip.</summary>
        public const float CitySkyRadius = 1500f;
        /// <summary>The player's far clip in a city (the bridge's is restored on leaving).</summary>
        public const float CityFarClip = 1700f;
        /// <summary>Exp² haze over the city: the horizon at about half strength.</summary>
        public const float CityFogDensity = 0.00048f;
        /// <summary>Our fortresses seen in the city's sky: their altitude above the ground and their size ×.</summary>
        public const float CitySkyStationAltitude = 820f;
        public const float CitySkyStationScale = 5f;
        public const float StarLightRange = 900f;

        // --- Holo table (centimetres on the platter). Never reuse OrbitBase / world radii. ---

        /// <summary>Playable holo disc radius on the table surface.</summary>
        public const float HoloDiscRadius = 0.95f;
        /// <summary>Star token radius on the holo map.</summary>
        public const float HoloStarRadius = 0.09f;
        /// <summary>Planet token base radius (grows slightly with slot).</summary>
        public const float HoloPlanetRadius = 0.055f;
        public const float HoloPlanetRadiusStep = 0.006f;
        /// <summary>Asteroid pip radius.</summary>
        public const float HoloAsteroidRadius = 0.022f;
        /// <summary>Fleet pip half-extent — must stay smaller than planet / star tokens.</summary>
        public const float HoloFleetSize = 0.042f;
        /// <summary>First orbit radius on the platter (slot 1).</summary>
        public const float HoloOrbitBase = 0.22f;
        /// <summary>Orbit spacing per planet slot.</summary>
        public const float HoloOrbitStep = 0.1f;
        /// <summary>Volume height of the projected holo column above the plate.</summary>
        public const float HoloVolumeHeight = 0.28f;
        /// <summary>Vertical lift of tokens above the plate.</summary>
        public const float HoloTokenLift = 0.08f;

        public static float PlanetRadius(int slot)
        {
            return PlanetRadiusMin + (Mathf.Clamp(Mathf.Max(1, slot), 1, 12) - 1) * PlanetRadiusStep;
        }

        public static float FleetStandoff(float bodyRadius)
        {
            return bodyRadius + CicDeck * 0.5f + FleetParkPadding;
        }

        /// <summary>Hub of our orbital station from the world's centre: the whole ring clears the planet and the parked ships.</summary>
        public static float StationStandoff(float bodyRadius)
        {
            return bodyRadius + StationRingRadius + StationRingSection.x * 0.5f + FleetParkPadding * 2.4f;
        }

        public static float OrbitRadius(int slot)
        {
            return OrbitBase + (Mathf.Max(1, slot) - 1) * OrbitStep;
        }

        public static float HoloOrbitRadius(int slot)
        {
            return HoloOrbitBase + (Mathf.Max(1, slot) - 1) * HoloOrbitStep;
        }

        public static float HoloPlanetTokenRadius(int slot)
        {
            return HoloPlanetRadius + (Mathf.Clamp(Mathf.Max(1, slot), 1, 12) - 1) * HoloPlanetRadiusStep;
        }
    }
}
