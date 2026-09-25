using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>A cosmic anomaly of the focused system (GetSystemAnomalies row, model/anomaly.php).</summary>
    public sealed class Anomaly
    {
        public int Id;
        public int SystemId;
        public string Type = string.Empty;
        public int Difficulty;
        public int Minerals;
        public int Crystals;
        public int Research;
        public long ExpiresAt;

        /// <summary>Player-facing name: GetTranslations key anomaly_&lt;type&gt; (not DB title).</summary>
        public string NameKey => "anomaly_" + Type;
        /// <summary>Player-facing description: anomalyDesc_&lt;type&gt;.</summary>
        public string DescKey => "anomalyDesc_" + Type;
    }

    /// <summary>
    /// Anomalies of the system in view. GetSystemAnomalies seeds one (45 %) whenever the system has none, so it
    /// is read once per system visit (web StarWindowUI: once per star window), never on a timer.
    /// ScanAnomaly needs one of our ships in that system carrying ScienceModule / SensorArray / DeepSpaceScanner;
    /// rewards go to the empire (research points) and the first planet (minerals, crystals).
    /// </summary>
    public sealed class AnomalyService : MonoBehaviour
    {
        /// <summary>Scanner modules ScanAnomaly accepts (model/anomaly.php).</summary>
        static readonly HashSet<string> Scanners = new() { "ScienceModule", "SensorArray", "DeepSpaceScanner" };
        const float RefetchAfter = 300f;

        public static AnomalyService Instance { get; private set; }

        FocusContext _focus;
        readonly Dictionary<int, List<Anomaly>> _bySystem = new();
        readonly Dictionary<int, float> _fetchedAt = new();
        readonly HashSet<int> _seen = new();
        int _lastSystem = -1;
        bool _busy;

        /// <summary>Raised with the system id whose anomaly list changed.</summary>
        public event Action<int> Changed;

        public static AnomalyService Build(Transform host, FocusContext focus)
        {
            var go = new GameObject("AnomalyService");
            go.transform.SetParent(host, false);
            var s = go.AddComponent<AnomalyService>();
            s._focus = focus;
            if (focus != null)
                focus.Changed += s.OnFocusChanged;
            Instance = s;
            s.OnFocusChanged();
            return s;
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
            if (Instance == this)
                Instance = null;
        }

        static readonly List<Anomaly> None = new();

        public IReadOnlyList<Anomaly> For(int systemId) =>
            _bySystem.TryGetValue(systemId, out var list) ? list : None;

        public Anomaly Find(int id)
        {
            foreach (var list in _bySystem.Values)
                foreach (var a in list)
                    if (a.Id == id)
                        return a;
            return null;
        }

        public static bool HasScanner(FocusFleet fleet)
        {
            if (fleet == null)
                return false;
            if (fleet.HasScienceModule)
                return true;
            foreach (var m in fleet.Modules)
                if (m != null && Scanners.Contains(m.Type))
                    return true;
            return false;
        }

        /// <summary>Our idle ship in <paramref name="systemId"/> able to scan (the inhabited one first).</summary>
        public static FocusFleet ScannerIn(FocusContext focus, int systemId)
        {
            if (focus == null)
                return null;
            var me = FocusContext.OwnedUserId();
            var now = FleetOrderGate.UnixNow();
            FocusFleet best = null;
            foreach (var f in focus.Fleets)
            {
                if (f.UserId != me || f.SystemId != systemId || f.IsMoving(now) || !HasScanner(f))
                    continue;
                if (f.Id == focus.ViewFleetId)
                    return f;
                best ??= f;
            }

            return best;
        }

        void OnFocusChanged()
        {
            var sys = _focus != null ? _focus.SystemId : 0;
            if (sys <= 0 || sys == _lastSystem)
                return;
            _lastSystem = sys;
            if (_fetchedAt.TryGetValue(sys, out var at) && Time.unscaledTime - at < RefetchAfter)
                return;
            AsyncTap.Run(Fetch(sys));
        }

        public async Task Fetch(int systemId)
        {
            if (_busy || systemId <= 0 || !AuthManager.Ensure().IsLoggedIn)
                return;
            _busy = true;
            try
            {
                var r = await ActionJs.Get("GetSystemAnomalies", new Dictionary<string, string>
                {
                    { "systemid", systemId.ToString() }
                });
                _fetchedAt[systemId] = Time.unscaledTime;
                if (!r.Ok)
                    return;
                var list = Parse(r.Body, systemId);
                _bySystem[systemId] = list;
                var fresh = false;
                foreach (var a in list)
                    fresh |= _seen.Add(a.Id);
                if (fresh && systemId == (_focus?.SystemId ?? 0))
                    Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Science, "anomaly", 2);
                Changed?.Invoke(systemId);
            }
            finally
            {
                _busy = false;
            }
        }

        static List<Anomaly> Parse(string body, int systemId)
        {
            var list = new List<Anomaly>();
            if (string.IsNullOrEmpty(body))
                return list;
            JArray rows;
            try
            {
                rows = JToken.Parse(body) as JArray;
            }
            catch
            {
                return list;
            }

            if (rows == null)
                return list;
            foreach (var row in rows)
            {
                var id = FocusContext.AsInt(row["id"]);
                if (id <= 0)
                    continue;
                list.Add(new Anomaly
                {
                    Id = id,
                    SystemId = FocusContext.AsInt(row["systemid"]) is var s && s > 0 ? s : systemId,
                    Type = FocusContext.AsString(row["type"]),
                    Difficulty = FocusContext.AsInt(row["difficulty"]),
                    Minerals = FocusContext.AsInt(row["reward_minerals"]),
                    Crystals = FocusContext.AsInt(row["reward_crystals"]),
                    Research = FocusContext.AsInt(row["reward_research"]),
                    ExpiresAt = FocusContext.AsLong(row["expires_at"])
                });
            }

            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        /// <summary>ScanAnomaly; on success the anomaly leaves the list and the economy / empire are re-read.</summary>
        public async Task<ApiResult> Scan(Anomaly anomaly, FocusFleet fleet)
        {
            var r = await ActionJs.Get("ScanAnomaly", new Dictionary<string, string>
            {
                { "anomaly", anomaly.Id.ToString() },
                { "fleet", fleet.Id.ToString() }
            });
            var bark = Core.Crew.BarkDirector.Instance;
            if (r.Ok)
            {
                if (_bySystem.TryGetValue(anomaly.SystemId, out var list))
                    list.RemoveAll(a => a.Id == anomaly.Id);
                bark?.Say(CrewDialogue.Role.Science, "scan", 2, Trans.Get(anomaly.NameKey));
                Changed?.Invoke(anomaly.SystemId);
                var eco = EconomyService.Instance;
                if (eco != null)
                    await eco.RefreshNow();
            }
            else
            {
                bark?.Say(CrewDialogue.Role.Science, "fail", 3);
            }

            return r;
        }

        /// <summary>"+620 research · +2 400 minerals · +1 800 crystals" from a ScanAnomaly reply.</summary>
        public static string RewardText(string body)
        {
            try
            {
                var o = JObject.Parse(body);
                return Trans.Format("vr.anomaly.rewards", FocusContext.AsInt(o["reward_research"]),
                    FocusContext.AsInt(o["reward_minerals"]), FocusContext.AsInt(o["reward_crystals"]),
                    FocusContext.AsInt(o["reward_xp"]));
            }
            catch
            {
                return Trans.Get("anomaly_scanned_success");
            }
        }
    }
}
