using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Nested spatial scales. Full contract: docs/SCALE.md.
    /// Room = 1:1 VR. System = stylized hublot space. Galaxy = holo table only.
    /// </summary>
    public static class WorldScale
    {
        public const float CicDeck = 12f;
        public const float CicCeiling = 3.1f;

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
        public const float StarLightRange = 900f;

        public static float PlanetRadius(int slot)
        {
            return PlanetRadiusMin + (Mathf.Clamp(Mathf.Max(1, slot), 1, 12) - 1) * PlanetRadiusStep;
        }

        public static float FleetStandoff(float bodyRadius)
        {
            return bodyRadius + CicDeck * 0.5f + FleetParkPadding;
        }

        public static float OrbitRadius(int slot)
        {
            return OrbitBase + (Mathf.Max(1, slot) - 1) * OrbitStep;
        }
    }
}
