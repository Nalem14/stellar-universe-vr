using System.Collections.Generic;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// The viewscreen's live direction, on top of the mode it is in: what just happened takes the picture for a
    /// few seconds with a lower-third caption — a salvo (framed between shooter and target), a kill, a
    /// bombardment, a new contact entering the system, our own departure or arrival. The captain's intent (what
    /// he points at on the table, or asked an officer to put on screen) is never taken away by it; only the
    /// caption shows. Officers put their subject on screen on request ("On screen, Commander").
    /// </summary>
    public sealed partial class BridgeViewscreen
    {
        public static BridgeViewscreen Instance { get; private set; }

        const float CrewHold = 20f;
        const float RepeatGap = 6f;
        const int PairKey = 9_000_000;

        sealed class Shot
        {
            public int Key = -1;
            public int A;
            public int B;
            public bool Pair;
            public bool Forward;
            public bool Urgent;
            public string Caption;
            public Color Tint;
            public float Until;
        }

        Shot _shot;
        Transform _anchor;
        Target _pair;
        TMP_Text _caption;
        Image _captionBar;
        readonly HashSet<int> _contacts = new();
        readonly Dictionary<string, float> _lastEvent = new();
        int _contactsSystem = -1;
        int _voyageSystem = -1;
        bool _voyageMoving;

        void BindDirection()
        {
            _anchor = new GameObject("ShotAnchor").transform;
            _anchor.SetParent(transform, false);
            _pair = new Target { Key = PairKey, Kind = 5, T = _anchor, Radius = 20f, Name = string.Empty, Tint = Color.white };

            _captionBar = NewBar(_hud, "CaptionBar", new Vector2(0f, -H * 0.5f + 74f), new Vector2(620f, 40f), new Color(0.01f, 0.03f, 0.05f, 0.7f));
            var go = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_captionBar.transform, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 38f);
            _caption = go.GetComponent<TextMeshProUGUI>();
            _caption.fontSize = 24f;
            _caption.fontStyle = FontStyles.Bold;
            _caption.alignment = TextAlignmentOptions.Center;
            _caption.raycastTarget = false;
            _caption.overflowMode = TextOverflowModes.Ellipsis;
            SetLayer(_captionBar.transform, ViewscreenCamera.HudLayer);
            _captionBar.gameObject.SetActive(false);

            CombatEvents.Shot += OnShot;
            CombatEvents.Destroyed += OnDestroyed;
            CombatEvents.Bombard += OnBombard;
        }

        void UnbindDirection()
        {
            CombatEvents.Shot -= OnShot;
            CombatEvents.Destroyed -= OnDestroyed;
            CombatEvents.Bombard -= OnBombard;
        }

        static Image NewBar(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // ── Events ───────────────────────────────────────────────────────────────

        void Play(Shot shot, string repeatKey)
        {
            // The same kind of event on the same subjects does not re-cut the picture every salvo.
            if (repeatKey != null && _lastEvent.TryGetValue(repeatKey, out var at) && Time.unscaledTime - at < RepeatGap)
                return;
            if (repeatKey != null)
                _lastEvent[repeatKey] = Time.unscaledTime;
            // A lesser event never cuts an urgent one short.
            if (_shot != null && _shot.Urgent && !shot.Urgent && Time.unscaledTime < _shot.Until)
                return;
            _shot = shot;
            _nextEval = 0f;
        }

        void OnShot(int src, int dst, Color color, bool heavy)
        {
            if (_targetsDirty)
                RebuildTargets();
            var a = FindTarget(2_000_000 + src);
            var b = FindTarget(2_000_000 + dst);
            if (a == null || b == null)
                return;
            Play(new Shot
            {
                Pair = true, A = a.Key, B = b.Key, Caption = Trans.Format("vr.screen.salvo", Plain(a.Name), Plain(b.Name)),
                Tint = color, Until = Time.unscaledTime + (heavy ? 3.2f : 2.4f)
            }, "shot:" + src + ">" + dst);
        }

        void OnDestroyed(int fleet)
        {
            if (_targetsDirty)
                RebuildTargets();
            var t = FindTarget(2_000_000 + fleet);
            if (t == null && fleet != _focus?.ViewFleetId)
                return;
            Play(new Shot
            {
                Key = t?.Key ?? -1, Forward = t == null, Urgent = true, Caption = Trans.Format("vr.screen.destroyed", Plain(t?.Name ?? "#" + fleet)),
                Tint = Red, Until = Time.unscaledTime + 4f
            }, null);
        }

        void OnBombard(int fleet, int planet, bool toPlanet)
        {
            if (!toPlanet)
                return;
            if (_targetsDirty)
                RebuildTargets();
            var t = FindTarget(1_000_000 + planet);
            if (t == null)
                return;
            Play(new Shot
            {
                Key = t.Key, Caption = Trans.Format("vr.screen.bombard", Plain(t.Name)), Tint = new Color(1f, 0.5f, 0.2f, 1f),
                Until = Time.unscaledTime + 3f
            }, "bombard:" + planet);
        }

        /// <summary>
        /// On every focus change: a foreign ship newly in the system is a contact; our ship leaving for another
        /// system, or arriving in a new one, is announced (the picture goes straight ahead).
        /// </summary>
        void WatchVoyage()
        {
            if (_focus == null)
                return;
            RebuildTargets();
            var now = UnixNow();
            var fleet = _focus.FindViewFleet();
            var system = _focus.SystemId;

            if (_voyageSystem > 0 && system > 0 && system != _voyageSystem)
                Play(new Shot { Forward = true, Caption = Trans.Format("vr.screen.arrival", SystemLabel(_focus)), Tint = _accent, Until = Time.unscaledTime + 5f },
                    null);
            var moving = fleet != null && fleet.IsMoving(now) && fleet.DestSystemId > 0 && fleet.DestSystemId != system;
            if (moving && !_voyageMoving && _voyageSystem == system)
                Play(new Shot
                {
                    Forward = true, Caption = Trans.Format("vr.screen.departure", GalaxyCatalog.Label(fleet.DestSystemId)), Tint = _accent,
                    Until = Time.unscaledTime + 5f
                }, null);
            _voyageMoving = moving;
            _voyageSystem = system;

            // Contacts: seed silently on a new system, then announce each foreign ship that appears.
            var seed = _contactsSystem != system;
            _contactsSystem = system;
            if (seed)
                _contacts.Clear();
            foreach (var f in _focus.Fleets)
            {
                if (_focus.IsMine(f) || !_focus.VisibleInFocus(f, now) || !_contacts.Add(f.Id) || seed)
                    continue;
                var t = FindTarget(2_000_000 + f.Id);
                if (t == null)
                    continue;
                var stance = DiplomacyIndex.ResolveFleet(f);
                Play(new Shot
                {
                    Key = t.Key, Urgent = stance is EmpireStance.Enemy or EmpireStance.Pirate,
                    Caption = Trans.Format("vr.screen.contact", Plain(t.Name)), Tint = t.Tint, Until = Time.unscaledTime + 4.5f
                }, null);
            }
        }

        // ── Resolution ───────────────────────────────────────────────────────────

        /// <summary>The event's subject for this evaluation (null + true = straight ahead).</summary>
        bool DirectedShot(bool intent, out Target subject)
        {
            subject = null;
            if (_shot == null || Time.unscaledTime >= _shot.Until)
                return false;
            if (intent && !_shot.Urgent)
                return false;
            if (_shot.Forward)
                return true;
            if (_shot.Pair)
            {
                if (!PlacePair())
                    return false;
                subject = _pair;
                return true;
            }

            subject = FindTarget(_shot.Key);
            return subject != null;
        }

        /// <summary>Between shooter and target, sized to hold both.</summary>
        bool PlacePair()
        {
            var a = FindTarget(_shot.A);
            var b = FindTarget(_shot.B);
            if (a?.T == null || b?.T == null)
                return false;
            var pa = a.T.position;
            var pb = b.T.position;
            _anchor.position = (pa + pb) * 0.5f;
            _pair.Radius = Vector3.Distance(pa, pb) * 0.5f + WorldScale.ShipSpan * 0.5f;
            return true;
        }

        /// <summary>Per frame while a salvo plays: keep the frame on both ships as they move.</summary>
        void TickDirection()
        {
            if (_shot != null && _shot.Pair && Time.unscaledTime < _shot.Until)
                PlacePair();
            if (_shot != null && Time.unscaledTime >= _shot.Until && _captionBar.gameObject.activeSelf)
            {
                _shot = null;
                _nextEval = 0f;
            }
        }

        void ShowCaption()
        {
            var on = _shot != null && Time.unscaledTime < _shot.Until && !string.IsNullOrEmpty(_shot.Caption);
            if (_captionBar.gameObject.activeSelf != on)
                _captionBar.gameObject.SetActive(on);
            if (!on)
                return;
            Set(_caption, _shot.Caption);
            _caption.color = Color.Lerp(_shot.Tint, Color.white, 0.25f);
        }

        static string Plain(string s) => "<noparse>" + (s ?? string.Empty) + "</noparse>";

        // ── Crew requests ────────────────────────────────────────────────────────

        /// <summary>
        /// An officer puts his subject on screen: Helm our destination (or straight ahead), Tactical the nearest
        /// threat, Science an anomaly or the most habitable free world, Engineering our other ships (or a field),
        /// Ops our worlds here. Held <see cref="CrewHold"/> s like a table pick. Returns whether there was one.
        /// </summary>
        public bool OnScreen(CrewDialogue.Role role)
        {
            if (_focus == null)
                return false;
            if (_targetsDirty)
                RebuildTargets();
            var key = SubjectFor(role);
            if (key < 0 && role != CrewDialogue.Role.Helm)
            {
                // Nothing of theirs out there: say so, leave the picture as it is.
                Core.Crew.BarkDirector.Instance?.Say(role, "noScreen", 2);
                return false;
            }

            if (key < 0)
            {
                _tableKey = -1;
                _tableUntil = 0f;
            }
            else
            {
                _tableKey = key;
                _tableUntil = Time.unscaledTime + CrewHold;
                _tablePreview = null;
            }

            Play(new Shot
            {
                Key = key, Forward = key < 0, Caption = Trans.Get("vr.screen.onScreen"), Tint = _accent, Until = Time.unscaledTime + 1.6f
            }, null);
            Core.Crew.BarkDirector.Instance?.Say(role, "onScreen", 2);
            return true;
        }

        int SubjectFor(CrewDialogue.Role role)
        {
            var now = UnixNow();
            var fleet = _focus.FindViewFleet();
            switch (role)
            {
                case CrewDialogue.Role.Helm:
                    return fleet != null ? ShipObjective(fleet)?.Key ?? -1 : 1_000_000 + _focus.ViewPlanetId;
                case CrewDialogue.Role.Tactical:
                {
                    var any = -1;
                    foreach (var f in _focus.Fleets)
                    {
                        if (_focus.IsMine(f) || !_focus.VisibleInFocus(f, now) || FindTarget(2_000_000 + f.Id) == null)
                            continue;
                        if (DiplomacyIndex.ResolveFleet(f) is EmpireStance.Enemy or EmpireStance.Pirate)
                            return 2_000_000 + f.Id;
                        if (any < 0)
                            any = 2_000_000 + f.Id;
                    }

                    return any;
                }
                case CrewDialogue.Role.Science:
                {
                    foreach (var t in _targets)
                        if (t.Kind == 4)
                            return t.Key;
                    var best = -1;
                    var hab = -1;
                    foreach (var p in _focus.Planets)
                        if (p.UserId <= 0 && p.Habitability > hab && FindTarget(1_000_000 + p.Id) != null)
                        {
                            hab = p.Habitability;
                            best = 1_000_000 + p.Id;
                        }

                    return best;
                }
                case CrewDialogue.Role.Engineering:
                {
                    foreach (var f in _focus.Fleets)
                        if (_focus.IsMine(f) && f.Id != _focus.ViewFleetId && FindTarget(2_000_000 + f.Id) != null)
                            return 2_000_000 + f.Id;
                    foreach (var t in _targets)
                        if (t.Kind == 3)
                            return t.Key;
                    return -1;
                }
                case CrewDialogue.Role.Ops:
                {
                    var me = FocusContext.OwnedUserId();
                    foreach (var p in _focus.Planets)
                        if (me > 0 && p.UserId == me && FindTarget(1_000_000 + p.Id) != null)
                            return 1_000_000 + p.Id;
                    return _focus.ViewPlanetId > 0 ? 1_000_000 + _focus.ViewPlanetId : -1;
                }
                default:
                    return -1;
            }
        }
    }
}
