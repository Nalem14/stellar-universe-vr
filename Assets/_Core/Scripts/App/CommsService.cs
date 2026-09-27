using System;
using System.Collections;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Incoming traffic, bridge-wide: GetMailUnreadCount every 15 s (web PanelChatUI.checkPmBadge /
    /// MailWindowUI badge). A rise voices the Comms officer ("transmission entrante") with a radio chirp,
    /// and a "message waiting" beacon pulses over the Comms station while anything is unread — the
    /// captain sees it from the chair without opening the console.
    /// </summary>
    public sealed class CommsService : MonoBehaviour
    {
        public const float Interval = 15f;

        public static CommsService Instance { get; private set; }

        public int MailUnread { get; private set; }
        public int PmUnread { get; private set; }
        public int Total => MailUnread + PmUnread;
        public event Action Changed;
        /// <summary>A new message or mail came in: its sender, read from the newest unread item.</summary>
        public event Action<Correspondent> Incoming;
        /// <summary>The last correspondent heard from (null until one writes).</summary>
        public Correspondent Last { get; private set; }

        bool _seeded;
        Coroutine _loop;
        Transform _beacon;
        TextMeshPro _count;
        Material _beaconMat;

        public static CommsService Build(Transform room)
        {
            var go = new GameObject("CommsService");
            go.transform.SetParent(room, false);
            return go.AddComponent<CommsService>();
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
            if (_beaconMat != null)
                Destroy(_beaconMat);
        }

        IEnumerator Loop()
        {
            var wait = new WaitForSeconds(Interval);
            yield return new WaitForSeconds(4f);
            while (true)
            {
                if (AuthManager.Ensure().IsLoggedIn)
                {
                    var task = Refresh();
                    while (!task.IsCompleted)
                        yield return null;
                }

                yield return wait;
            }
        }

        /// <summary>Read the counters now (after reading a mail / a thread).</summary>
        public async System.Threading.Tasks.Task Refresh()
        {
            var res = await ActionJs.Get("GetMailUnreadCount");
            if (!res.Ok || string.IsNullOrEmpty(res.Body) || res.Body[0] != '{')
                return;
            int mail, pm;
            try
            {
                var o = JObject.Parse(res.Body);
                mail = FocusContext.AsInt(o["mail_unread"]);
                pm = FocusContext.AsInt(o["pm_unread"]);
            }
            catch
            {
                return;
            }

            var rose = _seeded && mail + pm > MailUnread + PmUnread;
            // Read before the counters move (it compared the new count with itself: always false).
            var pmRose = _seeded && pm > PmUnread;
            var changed = mail != MailUnread || pm != PmUnread;
            MailUnread = mail;
            PmUnread = pm;
            _seeded = true;
            if (rose)
            {
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "newMail", 2, string.Empty, Total);
                var at = _beacon != null ? _beacon.position : transform.position;
                CicCue.Incoming(at);
            }

            if (changed)
                Changed?.Invoke();
            if (rose)
                await Identify(pmRose);
        }

        /// <summary>Who wrote: the newest unread thread (GetPrivateConversations), else the newest unread mail.</summary>
        async System.Threading.Tasks.Task Identify(bool pm)
        {
            Correspondent who = null;
            try
            {
                if (pm)
                {
                    var res = await ActionJs.Get("GetPrivateConversations");
                    if (res.Ok && JToken.Parse(res.Body) is JArray rows)
                        foreach (var r in rows)
                            if (FocusContext.AsInt(r["unread_count"]) > 0)
                            {
                                who = new Correspondent
                                {
                                    UserId = FocusContext.AsInt(r["contact_id"]),
                                    Username = FocusContext.AsString(r["contact_username"]),
                                    Text = FocusContext.AsString(r["last_message"])
                                };
                                break;
                            }
                }

                if (who == null)
                {
                    var res = await ActionJs.Get("GetMails", new System.Collections.Generic.Dictionary<string, string> { { "folder", "inbox" } });
                    if (res.Ok && JToken.Parse(res.Body) is JArray rows)
                        foreach (var m in rows)
                            if (FocusContext.AsInt(m["is_read"]) == 0)
                            {
                                who = new Correspondent
                                {
                                    UserId = FocusContext.AsInt(m["sender_id"]),
                                    Username = FocusContext.AsString(m["sender_username"]),
                                    Text = FocusContext.AsString(m["subject"]),
                                    IsMail = true
                                };
                                break;
                            }
                }
            }
            catch
            {
                // Shape varies: no face on the screen this time, the officer still reports it.
            }

            if (who == null)
                return;
            who.At = UnityEngine.Time.unscaledTime;
            await Correspondent.LoadEmpire(who.UserId);
            Last = who;
            Incoming?.Invoke(who);
        }

        // ── Beacon over the Comms station ─────────────────────────────────────────

        void EnsureBeacon()
        {
            if (_beacon != null)
                return;
            var officer = Core.Crew.BarkDirector.Instance?.Officer(CrewDialogue.Role.Comms);
            if (officer == null)
                return;
            var root = new GameObject("CommsMessageBeacon").transform;
            root.SetParent(officer.transform, false);
            root.localPosition = new Vector3(0f, 2.05f, 0f);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Glow";
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(root, false);
            glow.transform.localScale = Vector3.one * 0.34f;
            _beaconMat = new Material(CombatFxKit.Glow()) { name = "SU_CommsBeacon" };
            var r = glow.GetComponent<MeshRenderer>();
            r.sharedMaterial = _beaconMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var label = new GameObject("Count").AddComponent<TextMeshPro>();
            label.transform.SetParent(root, false);
            label.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            label.fontSize = 1.1f;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(1f, 0.95f, 0.85f, 1f);
            label.rectTransform.sizeDelta = new Vector2(0.4f, 0.2f);
            _count = label;
            _beacon = root;
        }

        void LateUpdate()
        {
            EnsureBeacon();
            if (_beacon == null)
                return;
            var on = Total > 0;
            if (_beacon.gameObject.activeSelf != on)
                _beacon.gameObject.SetActive(on);
            if (!on)
                return;
            var cam = Camera.main;
            if (cam != null)
                _beacon.rotation = Quaternion.LookRotation(_beacon.position - cam.transform.position, Vector3.up);
            var pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.2f);
            var c = new Color(1f, 0.62f, 0.22f, 1f) * (0.6f + 0.6f * pulse);
            c.a = 1f;
            if (_beaconMat.HasProperty("_Color"))
                _beaconMat.SetColor("_Color", c);
            var text = Total > 99 ? "99+" : Total.ToString();
            if (_count.text != text)
                _count.text = text;
        }
    }
}
