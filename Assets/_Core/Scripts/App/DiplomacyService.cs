using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Diplomatic traffic, bridge-wide: GetMyWars, GetAllianceInvites and GetMyAlliance every 30 s (web
    /// WarsWindowUI / AllianceWindowUI read them on open; the bridge keeps them warm so the crew can speak).
    /// The Comms officer reports what changed since the last read: a war declared on us, a peace offer from
    /// the other side, a war won / lost / ended in peace, an alliance invitation, an application to our
    /// alliance (officers). A change of wars or membership refreshes <see cref="DiplomacyIndex"/>, so tokens on
    /// the table and hulls outside take the new stance. The diplomacy chamber reads its state from here.
    /// </summary>
    public sealed class DiplomacyService : MonoBehaviour
    {
        public const float Interval = 30f;

        public static DiplomacyService Instance { get; private set; }

        public JArray Wars { get; private set; } = new();
        public JArray Invites { get; private set; } = new();
        /// <summary>GetMyAlliance.alliance — null when the empire belongs to none.</summary>
        public JObject Alliance { get; private set; }
        /// <summary>GetMyAlliance.myApplications — our pending applications while unaligned.</summary>
        public JArray MyApplications { get; private set; } = new();
        public bool Loaded { get; private set; }
        public event Action Changed;

        bool _seeded;
        bool _refreshing;
        Coroutine _loop;

        public static int MyEmpireId => FocusContext.AsInt(AuthManager.Ensure().Empire?["id"]);

        public string MyRole => FocusContext.AsString(Alliance?["myRole"]);
        public bool CanManage => MyRole is "leader" or "officer";

        public int ActiveWars
        {
            get
            {
                var n = 0;
                foreach (var w in Wars)
                    if (IsActive(w))
                        n++;
                return n;
            }
        }

        public static bool IsActive(JToken war) => FocusContext.AsString(war?["status"]) == "active";

        public static int Opponent(JToken war)
        {
            var me = MyEmpireId;
            var a = FocusContext.AsInt(war?["attackerEmpireId"]);
            return a == me ? FocusContext.AsInt(war?["defenderEmpireId"]) : a;
        }

        /// <summary>The active war between us and <paramref name="empireId"/>, if any.</summary>
        public JToken ActiveWarWith(int empireId)
        {
            foreach (var w in Wars)
                if (IsActive(w) && Opponent(w) == empireId)
                    return w;
            return null;
        }

        /// <summary>Id of our alliance's pending invitation to <paramref name="empireId"/> (officers), or 0.</summary>
        public int InviteTo(int empireId)
        {
            if (Alliance?["invites"] is JArray invites)
                foreach (var i in invites)
                    if (FocusContext.AsInt(i["empireId"]) == empireId)
                        return FocusContext.AsInt(i["id"]);
            return 0;
        }

        /// <summary>Id of our pending application to <paramref name="allianceId"/>, or 0.</summary>
        public int ApplicationTo(int allianceId)
        {
            foreach (var a in MyApplications)
                if (FocusContext.AsInt(a["allianceId"]) == allianceId)
                    return FocusContext.AsInt(a["id"]);
            return 0;
        }

        public bool IsMember(int empireId)
        {
            if (Alliance?["members"] is not JArray members)
                return false;
            foreach (var m in members)
                if (FocusContext.AsInt(m["empireId"]) == empireId)
                    return true;
            return false;
        }

        public static DiplomacyService Build(Transform room)
        {
            var go = new GameObject("DiplomacyService");
            go.transform.SetParent(room, false);
            return go.AddComponent<DiplomacyService>();
        }

        void Awake() => Instance = this;

        void OnEnable() => _loop = StartCoroutine(Loop());

        void OnDisable()
        {
            if (_loop != null)
                StopCoroutine(_loop);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        IEnumerator Loop()
        {
            var wait = new WaitForSeconds(Interval);
            yield return new WaitForSeconds(6f);
            while (true)
            {
                if (AuthManager.Ensure().HasEmpire)
                {
                    var task = Refresh();
                    while (!task.IsCompleted)
                        yield return null;
                }

                yield return wait;
            }
        }

        /// <summary>Read wars, invitations and the alliance now (after an order, on entering the chamber).</summary>
        public async Task Refresh()
        {
            if (_refreshing)
                return;
            _refreshing = true;
            try
            {
                var wars = ActionJs.Get("GetMyWars");
                var invites = ActionJs.Get("GetAllianceInvites");
                var alliance = ActionJs.Get("GetMyAlliance");
                await Task.WhenAll(wars, invites, alliance);
                if (!wars.Result.Ok && !invites.Result.Ok && !alliance.Result.Ok)
                    return;

                var newWars = wars.Result.Ok ? ParseArray(wars.Result.Body) : Wars;
                var newInvites = invites.Result.Ok ? ParseArray(invites.Result.Body) : Invites;
                var newAlliance = Alliance;
                if (alliance.Result.Ok)
                {
                    try
                    {
                        var root = JObject.Parse(alliance.Result.Body);
                        newAlliance = root["alliance"] as JObject;
                        MyApplications = root["myApplications"] as JArray ?? new JArray();
                    }
                    catch
                    {
                        // Keep the previous read.
                    }

                    DiplomacyIndex.IngestAllianceBody(alliance.Result.Body);
                }

                if (_seeded)
                    Report(newWars, newInvites, newAlliance);
                var stanceMoved = !_seeded || WarKey(newWars) != WarKey(Wars) ||
                                  MemberKey(newAlliance) != MemberKey(Alliance);
                Wars = newWars;
                Invites = newInvites;
                Alliance = newAlliance;
                _seeded = true;
                Loaded = true;
                if (stanceMoved)
                    await DiplomacyIndex.Refresh();
                Changed?.Invoke();
            }
            finally
            {
                _refreshing = false;
            }
        }

        // ── What the Comms officer says ───────────────────────────────────────────

        void Report(JArray wars, JArray invites, JObject alliance)
        {
            var me = MyEmpireId;
            var bark = Core.Crew.BarkDirector.Instance;
            if (bark == null)
                return;
            var old = new Dictionary<int, JToken>();
            foreach (var w in Wars)
                old[FocusContext.AsInt(w["id"])] = w;

            foreach (var w in wars)
            {
                var id = FocusContext.AsInt(w["id"]);
                var name = DiplomacyIndex.EmpireName(Opponent(w));
                if (!old.TryGetValue(id, out var before))
                {
                    if (IsActive(w) && FocusContext.AsInt(w["defenderEmpireId"]) == me)
                    {
                        bark.Say(CrewDialogue.Role.Comms, "warDeclared", 3, name);
                        CicCue.Klaxon(transform.position);
                    }

                    continue;
                }

                if (IsActive(before) && !IsActive(w))
                {
                    var status = FocusContext.AsString(w["status"]);
                    var attacker = FocusContext.AsInt(w["attackerEmpireId"]) == me;
                    var won = status == "attacker_won" ? attacker : status == "defender_won" && !attacker;
                    bark.Say(CrewDialogue.Role.Comms, status == "peace" ? "peace" : won ? "warWon" : "warLost", 3, name);
                    continue;
                }

                var offerBy = FocusContext.AsInt(w["pendingPeaceBy"]);
                if (IsActive(w) && offerBy > 0 && offerBy != me && FocusContext.AsInt(before["pendingPeaceBy"]) != offerBy)
                    bark.Say(CrewDialogue.Role.Comms, "peaceOffer", 2, name);
            }

            var seen = new HashSet<int>();
            foreach (var i in Invites)
                seen.Add(FocusContext.AsInt(i["id"]));
            foreach (var i in invites)
                if (!seen.Contains(FocusContext.AsInt(i["id"])))
                {
                    bark.Say(CrewDialogue.Role.Comms, "allianceInvite", 2);
                    CicCue.RadioOpen(transform.position);
                    break;
                }

            seen.Clear();
            if (Alliance?["applications"] is JArray oldApps)
                foreach (var a in oldApps)
                    seen.Add(FocusContext.AsInt(a["id"]));
            if (alliance?["applications"] is JArray apps)
                foreach (var a in apps)
                    if (!seen.Contains(FocusContext.AsInt(a["id"])))
                    {
                        bark.Say(CrewDialogue.Role.Comms, "application", 2, FocusContext.AsString(a["empireName"]));
                        break;
                    }
        }

        static string WarKey(JArray wars)
        {
            var parts = new List<string>();
            foreach (var w in wars)
                if (IsActive(w))
                    parts.Add(FocusContext.AsString(w["id"]));
            return string.Join(",", parts);
        }

        static string MemberKey(JObject alliance)
        {
            if (alliance?["members"] is not JArray members)
                return string.Empty;
            var parts = new List<string>();
            foreach (var m in members)
                parts.Add(FocusContext.AsString(m["empireId"]));
            return FocusContext.AsString(alliance["id"]) + ":" + string.Join(",", parts);
        }

        static JArray ParseArray(string body)
        {
            if (string.IsNullOrEmpty(body))
                return new JArray();
            try
            {
                return JToken.Parse(body) as JArray ?? new JArray();
            }
            catch
            {
                return new JArray();
            }
        }
    }
}
