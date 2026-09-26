using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>Ship bridge vs fake orbital station dressing on the same CIC kit.</summary>
    public static class BridgeDressing
    {
        public static void Apply(CicEnvironment host, FocusContext focus)
        {
            if (host == null)
                return;
            var ship = focus != null && focus.ViewFleetId > 0;
            // Accent strips: cyan ship / amber station
            var accent = ship ? CicArtKit.Cyan : CicArtKit.Amber;
            var mul = ship ? 1f : 0.85f;
            foreach (var r in host.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || r.sharedMaterial == null || host.Art == null)
                    continue;
                var n = r.gameObject.name;
                // The sky panel over the table: cool daylight on a ship, a warmer lamp at a station.
                if (n == "SkyPanel")
                {
                    r.sharedMaterial = host.Art.Lit(Texture2D.whiteTexture, ship ? new Color(0.7f, 0.88f, 1f) : new Color(1f, 0.86f, 0.66f), 1.25f);
                    continue;
                }

                if (n.IndexOf("Strip", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Accent", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Trim", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // Floor lines stay soft; wall and frame lines carry the accent.
                var floor = n is "KickStrip" or "DeckStrip";
                r.sharedMaterial = host.Art.Lit(Texture2D.whiteTexture, accent * mul, floor ? 1f : ship ? 2.4f : 2f);
            }
        }
    }
}
