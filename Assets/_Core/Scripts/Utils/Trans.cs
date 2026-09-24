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

        static Dictionary<string, string> _en = new();
        static Dictionary<string, string> _fr = new();
        static readonly HashSet<string> _missingLogged = new(StringComparer.Ordinal);
        static bool _loaded;
        static string _missingLogPath;

        static string _lang;
        static Task _loading;

        public static string Lang
        {
            get
            {
                if (_lang != null)
                    return _lang;
                var saved = PlayerPrefs.GetString(LangPref, string.Empty);
                if (!string.IsNullOrEmpty(saved))
                    return _lang = saved;
                var sys = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
                return _lang = sys == "fr" ? "fr" : "en";
            }
            set
            {
                _lang = value == "fr" ? "fr" : "en";
                PlayerPrefs.SetString(LangPref, _lang);
                PlayerPrefs.Save();
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
                _en = ToMap(root["en"] as JObject);
                _fr = ToMap(root["fr"] as JObject);
                _loaded = _en.Count > 0 || _fr.Count > 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[SU] GetTranslations ok ({_fr.Count} fr / {_en.Count} en)");
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

            var table = Lang == "fr" ? _fr : _en;
            if (table != null && table.TryGetValue(key, out var hit) && !string.IsNullOrEmpty(hit))
                return hit;

            var other = Lang == "fr" ? _en : _fr;
            if (other != null && other.TryGetValue(key, out hit) && !string.IsNullOrEmpty(hit))
                return hit;

            LogMissing(key);
            return key;
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
