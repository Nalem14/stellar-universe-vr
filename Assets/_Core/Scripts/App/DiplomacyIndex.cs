using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Empire stance toward the logged-in player.
    /// Server source of truth: GetEmpires.relation_key (owned|ally|neutral|enemy).
    /// Thresholds from GetConfigs.relation (good/enemy) for numeric fallbacks.
    /// Fleet flag isPirate → Pirate (hostile NPC).
    /// </summary>
    public enum EmpireStance
    {
        Unknown = 0,
        Owned,
        Ally,
        Neutral,
        Enemy,
        Pirate
    }

    /// <summary>
    /// Cached diplomacy map keyed by users.id. Refresh via GetEmpires (cadenced).
    /// </summary>
    public static class DiplomacyIndex
    {
        static readonly Dictionary<int, EmpireStance> ByUser = new();
        static readonly HashSet<int> AllianceUsers = new();
        static float _goodThreshold = 70f;
        static float _enemyThreshold = 20f;
        static bool _thresholdsLoaded;
        static float _lastRefreshUnscaled = -999f;
        const float MinRefreshSeconds = 45f;

        public static event Action Changed;

        public static float GoodThreshold => _goodThreshold;
        public static float EnemyThreshold => _enemyThreshold;

        public static void Clear()
        {
            ByUser.Clear();
            AllianceUsers.Clear();
            _lastRefreshUnscaled = -999f;
        }

        public static void IngestConfigsBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return;
            try
            {
                var root = JToken.Parse(body);
                var rel = root["relation"];
                if (rel == null)
                    return;
                if (rel["good"] != null)
                    _goodThreshold = FocusContext.AsFloat(rel["good"]);
                if (rel["enemy"] != null)
                    _enemyThreshold = FocusContext.AsFloat(rel["enemy"]);
                _thresholdsLoaded = true;
            }
            catch
            {
                // Keep previous thresholds.
            }
        }

        public static async Task EnsureLoaded(bool force = false)
        {
            if (!AuthManager.Ensure().IsLoggedIn)
                return;
            if (!force && Time.unscaledTime - _lastRefreshUnscaled < MinRefreshSeconds && ByUser.Count > 0)
                return;
            await Refresh();
        }

        public static async Task Refresh()
        {
            if (!AuthManager.Ensure().IsLoggedIn)
                return;

            if (!_thresholdsLoaded)
            {
                var cfg = await ActionJs.Get("GetConfigs");
                if (cfg.Ok)
                    IngestConfigsBody(cfg.Body);
            }

            var empires = await ActionJs.Get("GetEmpires");
            if (empires.Ok)
                IngestEmpiresBody(empires.Body);

            var ally = await ActionJs.Get("GetMyAlliance");
            if (ally.Ok)
                IngestAllianceBody(ally.Body);

            _lastRefreshUnscaled = Time.unscaledTime;
            Changed?.Invoke();
        }

        public static void IngestEmpiresBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return;
            try
            {
                var root = JToken.Parse(body);
                var arr = root as JArray ?? root["empires"] as JArray;
                if (arr == null)
                    return;
                ByUser.Clear();
                Identity.Clear();
                EmpireById.Clear();
                foreach (var e in arr)
                {
                    var uid = FocusContext.AsInt(e["userid"]);
                    if (uid <= 0)
                        continue;
                    var key = FocusContext.AsString(e["relation_key"]);
                    if (string.IsNullOrEmpty(key))
                        key = FocusContext.AsString(e["relationship"]);
                    var score = FocusContext.AsFloat(e["relation"]);
                    ByUser[uid] = ParseKey(key, score);
                    Identity[uid] = (FocusContext.AsString(e["name"]), FocusContext.AsString(e["flag"]));
                    var eid = FocusContext.AsInt(e["id"]);
                    if (eid > 0)
                        EmpireById[eid] = uid;
                }
            }
            catch
            {
                // Keep previous map.
            }
        }

        public static void IngestAllianceBody(string body)
        {
            AllianceUsers.Clear();
            if (string.IsNullOrEmpty(body))
                return;
            try
            {
                var root = JToken.Parse(body);
                var members = root["alliance"]?["members"] as JArray
                              ?? root["members"] as JArray;
                if (members == null)
                    return;
                foreach (var m in members)
                {
                    var uid = FocusContext.AsInt(m["userid"]);
                    if (uid <= 0)
                        continue;
                    AllianceUsers.Add(uid);
                    if (!ByUser.ContainsKey(uid) || ByUser[uid] == EmpireStance.Neutral ||
                        ByUser[uid] == EmpireStance.Unknown)
                        ByUser[uid] = EmpireStance.Ally;
                }
            }
            catch
            {
                // Keep previous.
            }
        }

        /// <summary>Empire name + flag JSON (GetEmpires.name / .flag) per users.id — galaxy territories.</summary>
        static readonly Dictionary<int, (string Name, string Flag)> Identity = new();

        /// <summary>empires.id → users.id (wars and alliances speak empire ids, fleets speak user ids).</summary>
        static readonly Dictionary<int, int> EmpireById = new();

        /// <summary>Name of an empire by empires.id, or "#id" when GetEmpires has not listed it.</summary>
        public static string EmpireName(int empireId) =>
            EmpireById.TryGetValue(empireId, out var uid) && Identity.TryGetValue(uid, out var id) && !string.IsNullOrEmpty(id.Name)
                ? id.Name
                : "#" + empireId;

        public static int UserOfEmpire(int empireId) => EmpireById.TryGetValue(empireId, out var uid) ? uid : 0;

        public static bool TryIdentity(int userId, out string name, out string flagJson)
        {
            if (Identity.TryGetValue(userId, out var id))
            {
                name = id.Name;
                flagJson = id.Flag;
                return true;
            }

            name = null;
            flagJson = null;
            return false;
        }

        public static EmpireStance Resolve(int userId, bool isPirate = false)
        {
            if (isPirate)
                return EmpireStance.Pirate;
            if (userId <= 0)
                return EmpireStance.Neutral;

            var me = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            if (me > 0 && userId == me)
                return EmpireStance.Owned;

            if (AllianceUsers.Contains(userId))
                return EmpireStance.Ally;

            if (ByUser.TryGetValue(userId, out var stance) && stance != EmpireStance.Unknown)
                return stance;

            return EmpireStance.Neutral;
        }

        public static EmpireStance ResolveFleet(FocusFleet fleet)
        {
            if (fleet == null)
                return EmpireStance.Unknown;
            return Resolve(fleet.UserId, fleet.IsPirate);
        }

        public static EmpireStance FromRelationScore(float score)
        {
            if (score >= _goodThreshold)
                return EmpireStance.Ally;
            if (score <= _enemyThreshold)
                return EmpireStance.Enemy;
            return EmpireStance.Neutral;
        }

        public static Color Tint(EmpireStance stance)
        {
            switch (stance)
            {
                case EmpireStance.Owned:
                    return CicArtKitSafeCyan();
                case EmpireStance.Ally:
                    return new Color(0.35f, 1f, 0.55f, 1f);
                case EmpireStance.Enemy:
                    return new Color(1f, 0.28f, 0.32f, 1f);
                case EmpireStance.Pirate:
                    return new Color(1f, 0.5f, 0.12f, 1f);
                case EmpireStance.Neutral:
                default:
                    return new Color(0.55f, 0.68f, 0.78f, 1f);
            }
        }

        static Color CicArtKitSafeCyan()
        {
            // Avoid hard Core.Vfx dependency cycle: mirror CicArtKit.Cyan.
            return new Color(0.25f, 0.92f, 1f, 1f);
        }

        static EmpireStance ParseKey(string key, float score)
        {
            if (!string.IsNullOrEmpty(key))
            {
                switch (key.Trim().ToLowerInvariant())
                {
                    case "owned":
                    case "own":
                    case "me":
                        return EmpireStance.Owned;
                    case "ally":
                    case "alliance":
                        return EmpireStance.Ally;
                    // "good" is a warm relation, not an alliance: allies are real alliance members (web
                    // model/alliance.php AreEmpiresAllied), the server says "ally" for them.
                    case "good":
                        return EmpireStance.Neutral;
                    case "enemy":
                    case "foe":
                    case "hostile":
                    case "war":
                        return EmpireStance.Enemy;
                    case "neutral":
                    case "unowned":
                        return EmpireStance.Neutral;
                }
            }

            if (score > 0f || score < 0f || _thresholdsLoaded)
                return FromRelationScore(score);
            return EmpireStance.Unknown;
        }
    }
}
