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
    /// Player-facing strings from GetTranslations (action-api.json).
    /// Missing key → show the key itself, and append it to a missing-keys log for later server add.
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

        public static string Lang
        {
            get
            {
                var saved = PlayerPrefs.GetString(LangPref, string.Empty);
                if (!string.IsNullOrEmpty(saved))
                    return saved;
                var sys = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
                return sys == "fr" ? "fr" : "en";
            }
            set
            {
                PlayerPrefs.SetString(LangPref, value == "fr" ? "fr" : "en");
                PlayerPrefs.Save();
            }
        }

        public static bool IsReady => _loaded;

        public static string MissingLogPath =>
            _missingLogPath ??= Path.Combine(Application.persistentDataPath, MissingLogFile);

        public static async Task EnsureLoaded()
        {
            if (_loaded)
                return;

            var result = await ActionJs.Get("GetTranslations", withToken: false);
            if (result.Ok)
            {
                try
                {
                    var root = JObject.Parse(result.Body);
                    _en = ToMap(root["en"] as JObject);
                    _fr = ToMap(root["fr"] as JObject);
                    _loaded = true;
                    Debug.Log($"[SU] GetTranslations ok ({_fr.Count} fr / {_en.Count} en)");
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SU] GetTranslations parse: " + e.Message);
                }
            }
            else
            {
                Debug.LogWarning("[SU] GetTranslations fail: " + result.Error);
            }

            _loaded = true;
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
            if (!_missingLogged.Add(key))
                return;

            Debug.LogWarning("[SU] missing Trans key: " + key);
            try
            {
                var line = $"{DateTime.UtcNow:o}\t{key}{Environment.NewLine}";
                File.AppendAllText(MissingLogPath, line);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SU] could not write missing Trans log: " + e.Message);
            }
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
