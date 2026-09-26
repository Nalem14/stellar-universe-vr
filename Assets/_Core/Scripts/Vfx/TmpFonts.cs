using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Text Mesh Pro font fallback per language.
    ///
    /// The shipped face (LiberationSans SDF) carries Latin and Cyrillic, so
    /// French, English, German, Spanish, Italian, Portuguese and Russian render
    /// as-is. Korean, Japanese and Chinese need a font that actually has the
    /// glyphs — without one every CJK string shows as missing-glyph boxes.
    ///
    /// The three faces ship as Resources/Fonts/NotoSans{JP,KR,SC}-Regular.otf
    /// (Noto Sans subset OTF, SIL OFL 1.1 — licence next to them). One face per
    /// language on purpose: a Japanese face renders Chinese with Japanese glyph
    /// forms, so the subset is what keeps the Han variants right.
    ///
    /// The TMP asset is built at runtime (dynamic atlas, glyphs rasterized on
    /// demand), so the build carries the .otf (~4-8 MB) instead of a pre-baked
    /// atlas, and there is no asset to regenerate when a face is updated.
    /// </summary>
    public static class TmpFonts
    {
        static readonly Dictionary<string, string> CjkFaceByLanguage = new()
        {
            { "ja", "Fonts/NotoSansJP-Regular" },
            { "ko", "Fonts/NotoSansKR-Regular" },
            { "zh", "Fonts/NotoSansSC-Regular" },
        };

        static readonly Dictionary<string, TMP_FontAsset> _built = new();
        static bool _warned;

        /// <summary>Hooks the face a CJK language needs into the fallback list of
        /// the font asset the UI uses. No-op for every other language; call it at
        /// boot and whenever the language changes.</summary>
        public static void EnsureForLanguage(string code)
        {
            if (code == null || !CjkFaceByLanguage.TryGetValue(code, out var resourcePath))
                return;

            var cjk = Build(resourcePath);
            if (cjk == null)
                return;

            foreach (var font in TargetFonts())
            {
                if (font == cjk)
                    continue;
                font.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
                if (!font.fallbackFontAssetTable.Contains(cjk))
                    font.fallbackFontAssetTable.Add(cjk);
            }
        }

        /// <summary>Dynamic TMP asset for a shipped face, built once and kept.</summary>
        static TMP_FontAsset Build(string resourcePath)
        {
            if (_built.TryGetValue(resourcePath, out var cached))
                return cached;

            var source = Resources.Load<Font>(resourcePath);
            if (source == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning($"[SU] no font at Resources/{resourcePath}: "
                        + "Korean / Japanese / Chinese will render as missing glyphs.");
                }
                return null;
            }

            var asset = TMP_FontAsset.CreateFontAsset(source);
            if (asset == null)
                return null;

            asset.name = source.name + " SDF";
            _built[resourcePath] = asset;
            return asset;
        }

        static IEnumerable<TMP_FontAsset> TargetFonts()
        {
            if (TMP_Settings.defaultFontAsset != null)
                yield return TMP_Settings.defaultFontAsset;

            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font != null)
                    yield return font;
        }
    }
}
