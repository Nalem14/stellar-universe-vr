using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Crew
{
    /// <summary>
    /// The first-steps guide, told by the crew: the web client's 16 steps (assets/js/src/scripts/tutorial.js)
    /// in the same order, with the same titles (native <c>tutorial.stepN.title</c>), the text rewritten for the
    /// bridge (<c>vr.tutorial.stepN.text</c>) and the web spotlight become a beacon on what the step is about —
    /// the officer who handles it, the holo table, the corridor door, the Guide button. Each step is spoken by
    /// that officer (radio chirp, turns to the captain). Screen on a stand at the captain's front right, or on an
    /// arm at the chair's right while seated;
    /// Previous / Skip / Next (End on the last), like the web window. Client-side only, as on the web: the step
    /// is kept locally per account; the Guide button on the right arm pad replays it from step 1.
    /// </summary>
    public sealed class TutorialGuide : MonoBehaviour
    {
        public static TutorialGuide Instance { get; private set; }

        enum Mark
        {
            None,
            Officer,
            Table,
            Corridor,
            GuideButton
        }

        readonly struct Step
        {
            public readonly CrewDialogue.Role Role;
            public readonly Mark Mark;

            public Step(CrewDialogue.Role role, Mark mark)
            {
                Role = role;
                Mark = mark;
            }
        }

        // Web order: welcome, homeworld, mines, shipyard, first ship, research, explore, colonise, troops,
        // defences, jumpgate, stargate, empire, shop, progression, conclusion.
        static readonly Step[] Steps =
        {
            new(CrewDialogue.Role.Comms, Mark.None),
            new(CrewDialogue.Role.Ops, Mark.Officer),
            new(CrewDialogue.Role.Ops, Mark.Officer),
            new(CrewDialogue.Role.Ops, Mark.Officer),
            new(CrewDialogue.Role.Engineering, Mark.Corridor),
            new(CrewDialogue.Role.Science, Mark.Corridor),
            new(CrewDialogue.Role.Science, Mark.Table),
            new(CrewDialogue.Role.Helm, Mark.Table),
            new(CrewDialogue.Role.Tactical, Mark.Officer),
            new(CrewDialogue.Role.Tactical, Mark.Officer),
            new(CrewDialogue.Role.Helm, Mark.Table),
            new(CrewDialogue.Role.Comms, Mark.Corridor),
            new(CrewDialogue.Role.Comms, Mark.Corridor),
            new(CrewDialogue.Role.Comms, Mark.Corridor),
            new(CrewDialogue.Role.Comms, Mark.Corridor),
            new(CrewDialogue.Role.Comms, Mark.GuideButton)
        };

        const string PrefKey = "su.tutorialStep.";
        /// <summary>Wait for the bridge to settle (boot readout, greeting) before the first step.</summary>
        const float StartDelay = 7f;

        Transform _room;
        Transform _table;
        Transform _door;
        Transform _guideButton;
        FocusContext _focus;
        GameObject _stand;
        HoloScreen _screen;
        TMP_Text _speaker;
        Image _chip;
        TMP_Text _title;
        TMP_Text _body;
        Button _prev;
        Button _skip;
        TMP_Text _nextLabel;
        TutorialBeacon _beacon;
        int _step = -1;
        int _uid;
        float _startAt;
        CrewOfficer _speaking;
        CrewDialogue.Role _speakerRole;

        public bool IsActive => _step >= 0 && _step < Steps.Length;

        public static TutorialGuide Build(Transform room, FocusContext focus, Transform table)
        {
            var go = new GameObject("TutorialGuide");
            go.transform.SetParent(room, false);
            var g = go.AddComponent<TutorialGuide>();
            g._room = room;
            g._focus = focus;
            g._table = table;
            g._door = Find(room, "CorridorDoor");
            g._beacon = TutorialBeacon.Build(room, UiKit.Cyan);
            g.BuildScreen();
            g._startAt = Time.time + StartDelay;
            Instance = g;
            return g;
        }

        void OnDestroy()
        {
            if (_chair != null)
                _chair.CommandModeChanged -= Reseat;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>The Guide button (right arm pad): the beacon of the last step points at it.</summary>
        public void BindGuideButton(Transform button) => _guideButton = button;

        /// <summary>Replay from step 1 (the web "Guide" button).</summary>
        public void Restart()
        {
            if (!ResolveUser())
                return;
            Show(0);
        }

        void Update()
        {
            // First run: start once the account is known and the bridge has settled; a finished or skipped
            // guide stays closed until the Guide button.
            if (_step >= 0 || Time.time < _startAt)
                return;
            if (!ResolveUser() || _focus == null || !_focus.HasSystem)
                return;
            var saved = PlayerPrefs.GetInt(PrefKey + _uid, 0);
            enabled = false;
            if (saved < Steps.Length)
                Show(saved);
            else
                _step = Steps.Length;
        }

        bool ResolveUser()
        {
            if (_uid > 0)
                return true;
            _uid = FocusContext.OwnedUserId();
            return _uid > 0;
        }

        void Show(int step)
        {
            _step = Mathf.Clamp(step, 0, Steps.Length - 1);
            Persist();
            var s = Steps[_step];
            var officer = Speaker(s.Role, out var role);
            _speakerRole = role;

            _screen.SetHeader(Trans.Get("guide").ToUpperInvariant() + "   " + (_step + 1) + " / " + Steps.Length);
            var accent = officer != null ? officer.Accent : UiKit.Cyan;
            _chip.color = accent;
            _speaker.color = accent;
            _speaker.text = Trans.Get(StationKey(role));
            _title.text = Trans.Get("tutorial.step" + (_step + 1) + ".title");
            _body.text = Trans.Get("vr.tutorial.step" + (_step + 1) + ".text");
            _prev.gameObject.SetActive(_step > 0);
            // As on the web: the last step has End instead of Skip + Next.
            _skip.gameObject.SetActive(_step < Steps.Length - 1);
            _nextLabel.text = Trans.Get(_step == Steps.Length - 1 ? "end" : "next");
            _stand.SetActive(true);
            HookChair();
            BarkDirector.Instance?.Hush(true);

            Point(s.Mark, officer);
            Voice(officer);
        }

        void Close(bool done)
        {
            _step = Steps.Length;
            Persist();
            _stand.SetActive(false);
            if (_chairArm != null)
                _chairArm.gameObject.SetActive(false);
            _beacon.Clear();
            BarkDirector.Instance?.Hush(false);
            if (_speaking != null)
                _speaking.LookAt(null);
            _speaking = null;
            if (done)
                CicCue.Success(_stand.transform.position);
        }

        void Persist()
        {
            if (_uid > 0)
                PlayerPrefs.SetInt(PrefKey + _uid, _step);
        }

        void Next()
        {
            if (_step >= Steps.Length - 1)
                Close(true);
            else
                Show(_step + 1);
        }

        void Prev()
        {
            if (_step > 0)
                Show(_step - 1);
        }

        /// <summary>The step's officer; at a station Helm is not aboard, so Ops (then Comms) speaks for it.</summary>
        CrewOfficer Speaker(CrewDialogue.Role wanted, out CrewDialogue.Role role)
        {
            var crew = BarkDirector.Instance;
            role = wanted;
            if (crew == null)
                return null;
            foreach (var r in new[] { wanted, CrewDialogue.Role.Ops, CrewDialogue.Role.Comms })
            {
                var o = crew.Officer(r);
                if (o != null && o.gameObject.activeInHierarchy)
                {
                    role = r;
                    return o;
                }
            }

            return null;
        }

        static string StationKey(CrewDialogue.Role role) => role switch
        {
            CrewDialogue.Role.Helm => "vr.station.helm",
            CrewDialogue.Role.Tactical => "vr.station.tactical",
            CrewDialogue.Role.Engineering => "vr.station.engineering",
            CrewDialogue.Role.Science => "vr.station.science",
            CrewDialogue.Role.Comms => "vr.station.comms",
            _ => "vr.station.ops"
        };

        void Point(Mark mark, CrewOfficer officer)
        {
            switch (mark)
            {
                case Mark.Officer when officer != null:
                    _beacon.Point(officer.transform, Vector3.zero, 0.55f, 1.8f);
                    break;
                case Mark.Table when _table != null:
                    _beacon.Point(_table, Vector3.zero, WorldScale.CicTableDiameter * 0.5f + 0.18f, 1.75f);
                    break;
                case Mark.Corridor when _door != null:
                    // On the deck just inside the aft door.
                    _beacon.Point(_door, _door.forward * 0.6f, 0.6f, 2.5f);
                    break;
                case Mark.GuideButton when _guideButton != null:
                    _beacon.Point(_guideButton, Vector3.zero, 0.3f, 1.05f);
                    break;
                default:
                    _beacon.Clear();
                    break;
            }
        }

        void Voice(CrewOfficer officer)
        {
            if (_speaking != null && _speaking != officer)
                _speaking.LookAt(null);
            _speaking = officer;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : _stand.transform.position;
            if (officer == null)
            {
                CicCue.Ok(_stand.transform.position);
                return;
            }

            // Long enough to read the step; the officer then keeps facing the captain until the next one.
            var talk = Mathf.Clamp(_body.text.Length * 0.04f, 2.5f, 7f);
            officer.Speak(talk, eye);
            officer.LookAt(eye);
            CicCue.RadioOpen(officer.MouthPosition);
            BarkDirector.SpeakBabble(officer.MouthPosition, _speakerRole, talk - 0.5f, _step);
        }

        // ── Seated ──────────────────────────────────────────────────────────────

        CaptainCommandMode _chair;
        Transform _chairArm;
        Transform _chairMount;
        Vector3 _standPos;
        Quaternion _standRot;

        /// <summary>Follow the captain into the chair: the screen moves onto an arm at the seat's right.</summary>
        void HookChair()
        {
            var chair = CaptainCommandMode.Instance;
            if (chair == _chair)
            {
                Reseat(chair != null && chair.IsCommandMode);
                return;
            }

            if (_chair != null)
                _chair.CommandModeChanged -= Reseat;
            _chair = chair;
            if (_chair != null)
                _chair.CommandModeChanged += Reseat;
            Reseat(_chair != null && _chair.IsCommandMode);
        }

        void Reseat(bool seated)
        {
            if (_screen == null)
                return;
            if (seated && _chair != null && _chair.Seat != null)
            {
                EnsureChairArm(_chair.Seat);
                _chairArm.gameObject.SetActive(_stand.activeSelf);
                _screen.transform.SetParent(_chairMount, false);
                _screen.transform.localPosition = Vector3.zero;
                _screen.transform.localRotation = Quaternion.identity;
                return;
            }

            if (_chairArm != null)
                _chairArm.gameObject.SetActive(false);
            _screen.transform.SetParent(_stand.transform, false);
            _screen.transform.localPosition = _standPos;
            _screen.transform.localRotation = _standRot;
        }

        /// <summary>
        /// A slim articulated arm rising from the chair's right armrest, the guide screen at its head: a hand's
        /// reach ahead and to the right of the seated eyes, below the eye line, clear of the map.
        /// </summary>
        void EnsureChairArm(Transform seat)
        {
            if (_chairArm != null)
                return;
            var head = new Vector3(0.5f, 0.6f, 0.3f);
            var root = ScreenMount.Socket(seat, "GuideChairArm", Vector3.zero, Quaternion.identity);
            _chairArm = root;
            var foot = new Vector3(0.36f, 0.2f, -0.02f);
            var elbow = new Vector3(0.52f, 0.3f, 0.18f);
            var under = head + new Vector3(0f, -0.25f, 0.02f);
            Strut(root, foot, elbow, 0.045f);
            Strut(root, elbow, under, 0.035f);
            UiKit.MeshPiece(root, "Elbow", UiMeshes.RoundedBox(Vector3.one * 0.07f, 0.03f), UiKit.Chassis, elbow);
            var cap = UiKit.MeshPiece(root, "Wrist", UiMeshes.RoundedBox(new Vector3(0.16f, 0.03f, 0.06f), 0.012f), UiKit.Cap, under);
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, UiKit.Cyan);
            block.SetFloat(UiKit.AccentMulId, 1.1f);
            cap.GetComponent<MeshRenderer>().SetPropertyBlock(block);

            _chairMount = new GameObject("GuideScreenMount").transform;
            _chairMount.SetParent(root, false);
            _chairMount.localPosition = head;
            ScreenMount.FaceViewer(_chairMount, _chair.SeatedEye(), 0.8f);
        }

        static void Strut(Transform root, Vector3 a, Vector3 b, float thick)
        {
            var d = b - a;
            var piece = UiKit.MeshPiece(root, "Strut", UiMeshes.RoundedBox(new Vector3(thick, thick, d.magnitude), thick * 0.4f),
                UiKit.Chassis, (a + b) * 0.5f);
            piece.transform.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        // ── Screen ──────────────────────────────────────────────────────────────

        void BuildScreen()
        {
            // A slim stand on the deck at the captain's front right, between the chair and the starboard stations.
            _stand = new GameObject("TutorialStand");
            _stand.transform.SetParent(_room, false);
            var floor = WorldScale.CicCaptainStand + new Vector3(0.95f, 0f, 0.45f);
            _stand.transform.localPosition = floor;
            UiKit.MeshPiece(_stand.transform, "Base", UiMeshes.RoundedBox(new Vector3(0.34f, 0.04f, 0.26f), 0.02f),
                UiKit.Chassis, new Vector3(0f, 0.02f, 0f));
            UiKit.MeshPiece(_stand.transform, "Post", UiMeshes.RoundedBox(new Vector3(0.06f, 0.86f, 0.06f), 0.02f),
                UiKit.Chassis, new Vector3(0f, 0.47f, 0.03f));
            var trim = UiKit.MeshPiece(_stand.transform, "Trim", UiMeshes.RoundedBox(new Vector3(0.02f, 0.74f, 0.065f), 0.008f),
                UiKit.Cap, new Vector3(0f, 0.47f, 0.03f));
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, UiKit.Cyan);
            block.SetFloat(UiKit.AccentMulId, 1.2f);
            trim.GetComponent<MeshRenderer>().SetPropertyBlock(block);

            // A lectern below the eye line: read by looking down, it never hides the starboard stations.
            _screen = HoloScreen.Create(_stand.transform, "TutorialScreen", new Vector2(0.72f, 0.46f),
                new Vector3(0f, 1.1f, 0f), Quaternion.identity, Trans.Get("guide"));
            ScreenMount.FaceViewer(_screen.transform,
                _room.TransformPoint(WorldScale.CicCaptainStand + Vector3.up * WorldScale.EyeStanding), 1f);
            _screen.SetAccent(UiKit.Cyan, 0.5f);
            _standPos = _screen.transform.localPosition;
            _standRot = _screen.transform.localRotation;

            var px = _screen.PixelSize;
            var content = _screen.Content;
            var chipGo = new GameObject("SpeakerChip", typeof(RectTransform), typeof(Image));
            chipGo.transform.SetParent(content, false);
            var chipRt = chipGo.GetComponent<RectTransform>();
            chipRt.sizeDelta = new Vector2(12f, 30f);
            chipRt.anchoredPosition = new Vector2(-px.x * 0.5f + 40f, px.y * 0.5f - 88f);
            _chip = chipGo.GetComponent<Image>();
            _chip.sprite = DiegeticUi.SprBtn;
            _chip.type = Image.Type.Sliced;
            _chip.raycastTarget = false;

            _speaker = DiegeticUi.HoloLabel(content, string.Empty, new Vector2(-px.x * 0.5f + 200f, px.y * 0.5f - 88f),
                new Vector2(300f, 34f), 22f, UiKit.Cyan, TextAlignmentOptions.MidlineLeft);
            _speaker.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            _speaker.characterSpacing = 6f;

            _title = DiegeticUi.HoloLabel(content, string.Empty, new Vector2(0f, px.y * 0.5f - 130f),
                new Vector2(px.x - 70f, 48f), 34f, UiKit.Amber, TextAlignmentOptions.MidlineLeft);
            _title.fontStyle = FontStyles.Bold;
            _title.enableAutoSizing = true;
            _title.fontSizeMin = 24f;
            _title.fontSizeMax = 34f;

            // Between the title (top ~76 px) and the button row (top ~-160 px).
            _body = DiegeticUi.HoloLabel(content, string.Empty, new Vector2(0f, -44f),
                new Vector2(px.x - 70f, 200f), 24f, UiKit.TextBright, TextAlignmentOptions.TopLeft);
            _body.textWrappingMode = TextWrappingModes.Normal;
            _body.enableAutoSizing = true;
            _body.fontSizeMin = 17f;
            _body.fontSizeMax = 24f;
            _body.lineSpacing = 6f;

            var y = -px.y * 0.5f + 44f;
            _prev = DiegeticUi.HoloButton(content, Trans.Get("previous"), new Vector2(-px.x * 0.5f + 110f, y),
                new Vector2(170f, 52f), Prev, DiegeticUi.BtnStyle.Ghost);
            _skip = DiegeticUi.HoloButton(content, Trans.Get("skip"), new Vector2(40f, y), new Vector2(150f, 52f),
                () => Close(false), DiegeticUi.BtnStyle.Ghost);
            var next = DiegeticUi.HoloButton(content, Trans.Get("next"), new Vector2(px.x * 0.5f - 110f, y),
                new Vector2(170f, 52f), Next, DiegeticUi.BtnStyle.Amber);
            _nextLabel = next.GetComponentInChildren<TMP_Text>();
            _stand.SetActive(false);
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var f = Find(root.GetChild(i), name);
                if (f != null)
                    return f;
            }

            return null;
        }
    }
}
