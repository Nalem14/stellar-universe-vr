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
    /// Drop a generated TMP font asset at <c>Resources/Fonts/CJK SDF</c> (dynamic
    /// atlas, built from a CJK face such as Noto Sans CJK / Source Han Sans — see
    /// docs/PARITY.md, section « Langues ») and this wires it into the fallback
    /// list of the font asset the UI already uses: no per-text change, no
    /// pre-baked glyph atlas. Until the asset exists we log once and keep the
    /// shipped face.
    /// </summary>
    public static class TmpFonts
    {
        const string CjkResourcePath = "Fonts/CJK SDF";

        static bool _warned;
        static bool _wired;

        /// <summary>Call with the language code after it changes (and at boot):
        /// a no-op for the Latin/Cyrillic languages.</summary>
        public static void EnsureForLanguage(string code)
        {
            if (_wired || Core.App.GameConfig.LanguageFontHint(code) != "cjk")
                return;

            var cjk = Resources.Load<TMP_FontAsset>(CjkResourcePath);
            if (cjk == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning($"[SU] no CJK font asset at Resources/{CjkResourcePath}: "
                        + "Korean / Japanese / Chinese will render as missing glyphs. "
                        + "See docs/PARITY.md (Langues).");
                }
                return;
            }

            var targets = new List<TMP_FontAsset>();
            if (TMP_Settings.defaultFontAsset != null)
                targets.Add(TMP_Settings.defaultFontAsset);
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font != null && !targets.Contains(font))
                    targets.Add(font);

            foreach (var font in targets)
            {
                if (font == cjk)
                    continue;
                font.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
                if (!font.fallbackFontAssetTable.Contains(cjk))
                    font.fallbackFontAssetTable.Add(cjk);
            }

            _wired = true;
        }
    }
}
