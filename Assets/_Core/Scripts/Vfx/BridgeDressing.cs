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
                if (r == null || r.sharedMaterial == null)
                    continue;
                var n = r.gameObject.name;
                if (n.IndexOf("Strip", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Accent", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Trim", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (host.Art == null)
                    continue;
                r.sharedMaterial = host.Art.Lit(Texture2D.whiteTexture, accent * mul, ship ? 3.2f : 2.2f);
            }
        }
    }
}
