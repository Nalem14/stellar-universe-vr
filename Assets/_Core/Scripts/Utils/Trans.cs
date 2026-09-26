using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Utils
{
    /// <summary>
    /// Player-facing strings from GetTranslations:
    /// https://www.stellar-universe.com/actionjs.php?action=GetTranslations
    /// Missing key → show the key itself; Editor appends to su-missing-trans-keys.txt.
    /// Never invent FR/EN fallback copy in the client.
    /// </summary>
    public static class Trans
    {
        const string LangPref = "su.vr.lang";
        const string MissingLogFile = "su-missing-trans-keys.txt";

        /// <summary>Table every lookup ends on — the server always sends it
        /// (GetTranslations serves the request's language plus this one).</summary>
        const string FallbackLang = "en";

        /// <summary>Languages the client can offer before GetConfigs answers.
        /// The authoritative list, with native names, is GetConfigs.languages.</summary>
        static readonly string[] BuiltInLanguages =
            { "fr", "en", "de", "es", "it", "pt", "ru", "ko", "ja", "zh" };

        static readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.Ordinal);
        static readonly HashSet<string> _missingLogged = new(StringComparer.Ordinal);
        static bool _loaded;
        static string _missingLogPath;

        static string _lang;
        static Task _loading;

        /// <summary>Codes to offer, server registry first. Empty registry (boot
        /// read not done yet) falls back to the codes compiled in.</summary>
        public static IReadOnlyList<string> SupportedLanguages
        {
            get
            {
                var fromServer = Core.App.GameConfig.LanguageCodes;
                return fromServer != null && fromServer.Count > 0 ? fromServer : BuiltInLanguages;
            }
        }

        /// <summary>Native name of a language ("Deutsch", "한국어") — what a
        /// selector shows.</summary>
        public static string NativeName(string code) => Core.App.GameConfig.LanguageNativeName(code);

        /// <summary>Font-stack hint: "latin" rides the shipped face, "cjk" needs
        /// a fallback font asset (see Core.Vfx.TmpFonts).</summary>
        public static string FontHint => Core.App.GameConfig.LanguageFontHint(Lang);

        static bool IsSupported(string code)
        {
            if (string.IsNullOrEmpty(code))
                return false;
            foreach (var supported in SupportedLanguages)
                if (supported == code)
                    return true;
            return false;
        }

        public static string Lang
        {
            get
            {
                if (_lang != null)
                    return _lang;
                var saved = PlayerPrefs.GetString(LangPref, string.Empty);
                if (!string.IsNullOrEmpty(saved) && IsSupported(saved))
                    return _lang = saved;
                var sys = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
                return _lang = IsSupported(sys) ? sys : FallbackLang;
            }
            set
            {
                var next = IsSupported(value) ? value : FallbackLang;
                if (next == _lang)
                    return;
                _lang = next;
                PlayerPrefs.SetString(LangPref, _lang);
                PlayerPrefs.Save();
                // The dump is served in the request's language (ActionJs appends
                // &lang): drop it so the next EnsureLoaded fetches the new table.
                // Screens already rendered keep their strings until rebuilt.
                _loaded = false;
                _tables.Clear();
            }
        }

        /// <summary>True once a GetTranslations dump was parsed. A failed load stays false and retries on the next call.</summary>
        public static bool IsReady => _loaded;

        public static string MissingLogPath =>
            _missingLogPath ??= Path.Combine(Application.persistentDataPath, MissingLogFile);

        /// <summary>Concurrent callers share one request; up to 3 attempts with backoff.</summary>
        public static Task EnsureLoaded()
        {
            if (_loaded)
                return Task.CompletedTask;
            return _loading ??= LoadWithRetry();
        }

        static async Task LoadWithRetry()
        {
            try
            {
                for (var attempt = 0; attempt < 3 && !_loaded; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(1000 * attempt);
                    await LoadOnce();
                }
            }
            finally
            {
                _loading = null;
            }
        }

        static async Task LoadOnce()
        {
            var result = await ActionJs.Get("GetTranslations", withToken: false);
            if (!result.Ok)
            {
                Debug.LogWarning("[SU] GetTranslations fail: " + result.Error);
                return;
            }

            try
            {
                var root = JObject.Parse(result.Body);
                _tables.Clear();
                foreach (var property in root.Properties())
                    if (property.Value is JObject table)
                        _tables[property.Name] = ToMap(table);
                _loaded = _tables.Count > 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var counts = new List<string>();
                foreach (var entry in _tables)
                    counts.Add($"{entry.Key}:{entry.Value.Count}");
                Debug.Log($"[SU] GetTranslations ok ({string.Join(" ", counts)})");
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SU] GetTranslations parse: " + e.Message);
            }
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            if (TryGet(key, out var hit))
                return hit;

            // The web lang keys are inconsistently cased: a module type like
            // "ScienceModule" is stored as "scienceModule", while "ShipCore" or
            // "TroopBay" keep their PascalCase. Mirror the web's own fallback
            // (ShipBuilderUI._t) and retry the opposite initial-letter case before
            // treating the key as missing.
            var lower = char.ToLowerInvariant(key[0]) + key.Substring(1);
            if (lower != key && TryGet(lower, out hit))
                return hit;

            var upper = char.ToUpperInvariant(key[0]) + key.Substring(1);
            if (upper != key && TryGet(upper, out hit))
                return hit;

            LogMissing(key);
            return key;
        }

        /// <summary>Exact lookup in the current language, then the fallback
        /// language — a key a translation has not covered yet still reads.</summary>
        static bool TryGet(string key, out string hit)
        {
            if (_tables.TryGetValue(Lang, out var table)
                && table.TryGetValue(key, out hit) && !string.IsNullOrEmpty(hit))
                return true;

            if (Lang != FallbackLang
                && _tables.TryGetValue(FallbackLang, out var fallback)
                && fallback.TryGetValue(key, out hit) && !string.IsNullOrEmpty(hit))
                return true;

            hit = null;
            return false;
        }

        public static string Format(string key, params object[] args)
        {
            var template = Get(key);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        static void LogMissing(string key)
        {
            // Before the dump arrives every key is "missing": that is not a server gap, do not log it.
            if (!_loaded || !_missingLogged.Add(key))
                return;

            Debug.LogWarning("[SU] missing Trans key: " + key);
#if UNITY_EDITOR
            try
            {
                var line = $"{DateTime.UtcNow:o}\t{key}{Environment.NewLine}";
                File.AppendAllText(MissingLogPath, line);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SU] could not write missing Trans log: " + e.Message);
            }
#endif
        }

        static Dictionary<string, string> ToMap(JObject obj)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (obj == null)
                return map;
            foreach (var prop in obj.Properties())
            {
                if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                    continue;
                map[prop.Name] = prop.Value.Type == JTokenType.String
                    ? prop.Value.Value<string>()
                    : prop.Value.ToString();
            }

            return map;
        }
    }
}
