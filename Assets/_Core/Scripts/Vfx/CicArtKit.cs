using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Shared CIC materials — one Material instance per visual key (Quest draw-call budget).
    /// </summary>
    public sealed class CicArtKit
    {
        public static readonly Color Cyan = new(0.25f, 0.92f, 1f, 1f);
        public static readonly Color Amber = new(1f, 0.62f, 0.22f, 1f);
        public static readonly Color Metal = new(0.34f, 0.42f, 0.5f, 1f);
        public static readonly Color DarkMetal = new(0.16f, 0.2f, 0.24f, 1f);
        public static readonly Color SoftMetal = new(0.28f, 0.34f, 0.4f, 1f);
        public static readonly Color DeckTint = new(0.55f, 0.62f, 0.7f, 1f);

        public Texture Floor { get; private set; }
        public Texture Wall { get; private set; }
        public Texture Panel { get; private set; }
        public Texture DeckRib { get; private set; }
        public Texture ScreenIdle { get; private set; }
        public Texture Vent { get; private set; }
        public Texture HoloPlate { get; private set; }
        public Texture OrbitPlate { get; private set; }
        public Texture TokenSystem { get; private set; }
        public Texture TokenPlanet { get; private set; }
        public Texture TokenFleet { get; private set; }
        public Texture ProjectorGlow { get; private set; }
        public Texture Stars { get; private set; }
        public Texture Title { get; private set; }
        public AudioClip Ambient { get; private set; }

        Shader _emissive;
        Shader _holo;
        Shader _fallback;
        readonly Dictionary<string, Material> _cache = new();

        public void Load()
        {
            Floor = Resources.Load<Texture2D>("CIC/Floor") ?? Resources.Load<Texture2D>("CIC/DeckRib");
            Wall = Resources.Load<Texture2D>("CIC/Wall");
            Panel = Resources.Load<Texture2D>("CIC/PanelBrushed") ?? Wall;
            DeckRib = Resources.Load<Texture2D>("CIC/DeckRib") ?? Floor;
            ScreenIdle = Resources.Load<Texture2D>("CIC/ScreenIdle");
            Vent = Resources.Load<Texture2D>("CIC/VentGrill");
            HoloPlate = Resources.Load<Texture2D>("CIC/HoloTable");
            OrbitPlate = Resources.Load<Texture2D>("Holo/OrbitPlate");
            TokenSystem = Resources.Load<Texture2D>("Holo/TokenSystem");
            TokenPlanet = Resources.Load<Texture2D>("Holo/TokenPlanet");
            TokenFleet = Resources.Load<Texture2D>("Holo/TokenFleet");
            ProjectorGlow = Resources.Load<Texture2D>("Holo/ProjectorGlow");
            Stars = Resources.Load<Texture2D>("CIC/ViewportStars");
            Title = Resources.Load<Texture2D>("CIC/BootTitle");
            Ambient = Resources.Load<AudioClip>("CIC/ambient");

            _emissive = Shader.Find("SU/UnlitEmissive");
            _holo = Shader.Find("SU/HoloSurface");
            _fallback = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Universal Render Pipeline/Unlit");
        }

        public Material Lit(Texture tex, Color tint, float emissionMul, float tiling = 1f)
        {
            var key = $"L|{(tex != null ? tex.GetInstanceID() : 0)}|{ColorKey(tint)}|{emissionMul:F2}|{tiling:F2}";
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var shader = _emissive != null ? _emissive : _fallback;
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = tex;
            if (mat.HasProperty("_MainTex"))
                mat.SetTextureScale("_MainTex", Vector2.one * tiling);
            // Unlit path: albedo carries most of the "light"; emission is a single mul (no square).
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Emission"))
                mat.SetColor("_Emission", tint);
            if (mat.HasProperty("_EmissionMul"))
                mat.SetFloat("_EmissionMul", Mathf.Max(0f, emissionMul) * 0.4f);
            _cache[key] = mat;
            return mat;
        }

        public Material Holo(Texture tex, Color tint)
        {
            var key = $"H|{(tex != null ? tex.GetInstanceID() : 0)}|{ColorKey(tint)}";
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var shader = _holo != null ? _holo : (_emissive != null ? _emissive : _fallback);
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = tex;
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Emission"))
                mat.SetColor("_Emission", new Color(tint.r, tint.g, tint.b, 1f));
            if (mat.HasProperty("_ScanSpeed"))
                mat.SetFloat("_ScanSpeed", 0.35f);
            if (mat.HasProperty("_Fresnel"))
                mat.SetFloat("_Fresnel", 2.4f);
            _cache[key] = mat;
            return mat;
        }

        public Material MetalPanel(float emission = 0.55f) => Lit(Panel ?? Wall, Metal, emission, 1.4f);
        public Material DarkPanel(float emission = 0.35f) => Lit(Panel ?? Wall, DarkMetal, emission, 1.2f);
        public Material SoftPanel(float emission = 0.45f) => Lit(Wall, SoftMetal, emission, 1.3f);
        public Material DeckMat(float emission = 0.5f) =>
            Lit(DeckRib != null ? DeckRib : Floor, DeckTint, emission, 5f);
        public Material CyanEmit(float mul = 3.2f) => Lit(Texture2D.whiteTexture, Cyan, mul);
        public Material AmberEmit(float mul = 2.4f) => Lit(Texture2D.whiteTexture, Amber, mul);

        static string ColorKey(Color c) =>
            $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}";
    }
}
