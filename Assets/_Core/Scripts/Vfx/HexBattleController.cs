using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Tactical combat on the holo table (docs/design/HOLOTABLE.md §2.8, web scenes/battle.js).
    /// When one of our ships is in a battle (GetAllFleets isInBattle → GetMyBattles), the fight takes the
    /// table: the system diorama folds away and the hex board unfolds in its place. The fight gives the
    /// table back when it ends (victory / defeat banner, then fold) or when the captain leaves it — leaving
    /// never surrenders (web ✕); a pulsing rim button rejoins it.
    /// Server contract as the web: GetBattleState every 2.5 s, UpdateBattle poked while pending or past a
    /// turn deadline, BattleDoAction (subaction move / skill), BattleEndFleetTurn, SetFleetState ready,
    /// RemoveFleetFromBattle (withdraw while pending). Rendering is diffed per poll: one grid mesh, one
    /// holo hull per ship, beams replayed from the new battle_actions rows.
    /// </summary>
    public partial class HexBattleController : MonoBehaviour
    {
        const float PollSeconds = 2.5f;
        const float DetectSeconds = 5f;
        const float CloseDelay = 6f;

        FocusContext _focus;
        HoloMapController _mapCtrl;
        Transform _mount;
        CicArtKit _art;

        int _battleId;
        int _fleetId;
        BattleSnapshot _state;
        bool _visible;
        float _pollAt;
        float _detectAt;
        bool _polling;
        bool _detecting;
        int _lastLogId = -1;
        bool _outcomeShown;
        float _closeAt = -1f;

        /// <summary>A battle of ours the table could show (rejoin button when the board is closed).</summary>
        int _ongoingBattle;
        int _ongoingFleet;
        readonly HashSet<int> _dismissed = new();
        readonly HashSet<int> _finished = new();

        public bool IsActive => _visible && _battleId > 0;
        public int BattleId => _battleId;
        public BattleSnapshot State => _state;

        public void Bind(FocusContext focus, HoloMapController mapCtrl, Transform tableMount, CicArtKit art)
        {
            _focus = focus;
            _mapCtrl = mapCtrl;
            _art = art;
            _mount = tableMount;
            BuildBoard();
        }

        // ── Map mode hooks (HoloMapController.SetMode) ───────────────────────────

        public void Show()
        {
            _visible = true;
            if (_boardRoot != null)
                _boardRoot.gameObject.SetActive(true);
            StartReveal(true);
            SetRejoinVisible(false);
        }

        public void Hide()
        {
            if (_battleId > 0 && !_finished.Contains(_battleId))
                _dismissed.Add(_battleId);
            _visible = false;
            _closeAt = -1f;
            if (_boardRoot != null)
                _boardRoot.gameObject.SetActive(false);
            ClearShips();
            ClearFx();
            _state = null;
            _battleId = 0;
            _fleetId = 0;
            _lastLogId = -1;
            _outcomeShown = false;
            ResetAim();
            _detectAt = 0f;
            CombatEvents.SetEngaged(false);
#if UNITY_EDITOR
            EditorOffline = false;
#endif
        }

        /// <summary>Put <paramref name="battleId"/> on the table, playing as <paramref name="fleetId"/>.</summary>
        public void Open(int battleId, int fleetId)
        {
            if (battleId <= 0 || fleetId <= 0)
                return;
            if (_visible && _battleId == battleId)
                return;
            _dismissed.Remove(battleId);
            ClearShips();
            ClearFx();
            _battleId = battleId;
            _fleetId = fleetId;
            _state = null;
            _lastLogId = -1;
            _outcomeShown = false;
            _closeAt = -1f;
            _pollAt = 0f;
            SetHeader(Trans.Get("Loading"), string.Empty);
            if (_mapCtrl != null && _mapCtrl.Mode != HoloMapMode.HexBattle)
                _mapCtrl.SetMode(HoloMapMode.HexBattle);
            else
                Show();
        }

        /// <summary>Give the table back to the system map (never surrenders).</summary>
        public void Leave()
        {
            if (!_visible)
                return;
            BeginFold();
        }

        void Update()
        {
            if (_focus == null)
                return;
            AnimateBoard();
            if (!_visible)
            {
                if (Time.unscaledTime >= _detectAt && !_detecting)
                {
                    _detectAt = Time.unscaledTime + DetectSeconds;
                    AsyncTap.Run(Detect());
                }

                PulseRejoin();
                return;
            }

            if (_folding)
                return;

            UpdateInput();
            if (_closeAt > 0f && Time.unscaledTime >= _closeAt)
            {
                _closeAt = -1f;
                BeginFold();
                return;
            }

#if UNITY_EDITOR
            if (EditorOffline)
            {
                TickHeader();
                return;
            }
#endif
            if (_battleId > 0 && !_polling && !_outcomeShown && Time.unscaledTime >= _pollAt)
            {
                _pollAt = Time.unscaledTime + PollSeconds;
                AsyncTap.Run(PollState());
            }

            TickHeader();
        }

#if UNITY_EDITOR
        /// <summary>Editor checks: a GetBattleState body applied without the server (no poll, no orders).</summary>
        public static bool EditorOffline;

        public void EditorApply(string body, int fleetId)
        {
            EditorOffline = true;
            if (!_visible)
                Open(424242, fleetId);
            var snap = Parse(body);
            if (snap != null)
                Apply(snap);
        }
#endif

        // ── Detection: our ships' battles (isInBattle → GetMyBattles, web loadMyBattles) ─────────────

        bool AnyMineInBattle()
        {
            foreach (var f in _focus.Fleets)
                if (f.IsInBattle && _focus.IsMine(f))
                    return true;
            return false;
        }

        async Task Detect()
        {
            if (!AnyMineInBattle())
            {
                _ongoingBattle = 0;
                SetRejoinVisible(false);
                return;
            }

            _detecting = true;
            try
            {
                var res = await ActionJs.Get("GetMyBattles");
                if (!res.Ok || string.IsNullOrEmpty(res.Body) || _visible)
                    return;
                JArray arr;
                try
                {
                    arr = JToken.Parse(res.Body) as JArray;
                }
                catch
                {
                    return;
                }

                if (arr == null)
                    return;
                // Prefer the ship we are aboard, then a fight in the system in view, then any.
                int pickBattle = 0, pickFleet = 0, rank = -1;
                foreach (var b in arr)
                {
                    var id = FocusContext.AsInt(b["id"]);
                    var fleet = FocusContext.AsInt(b["myFleetId"]);
                    if (id <= 0 || fleet <= 0 || _finished.Contains(id))
                        continue;
                    var r = fleet == _focus.ViewFleetId ? 2
                        : FocusContext.AsInt(b["systemid"]) == _focus.SystemId ? 1
                        : 0;
                    if (r <= rank)
                        continue;
                    rank = r;
                    pickBattle = id;
                    pickFleet = fleet;
                }

                _ongoingBattle = pickBattle;
                _ongoingFleet = pickFleet;
                if (pickBattle <= 0)
                {
                    SetRejoinVisible(false);
                    return;
                }

                // A fight here takes the table by itself (once); elsewhere, or once left, it waits on the rim.
                if (rank >= 1 && !_dismissed.Contains(pickBattle))
                    Open(pickBattle, pickFleet);
                else
                    SetRejoinVisible(true);
            }
            finally
            {
                _detecting = false;
            }
        }

        void Rejoin()
        {
            if (_ongoingBattle > 0)
                Open(_ongoingBattle, _ongoingFleet);
        }

        // ── State ─────────────────────────────────────────────────────────────────

        async Task PollState()
        {
            if (_battleId <= 0 || _fleetId <= 0)
                return;
            _polling = true;
            var battle = _battleId;
            try
            {
                var res = await ActionJs.Get("GetBattleState", new Dictionary<string, string>
                {
                    { "battleid", battle.ToString(CultureInfo.InvariantCulture) },
                    { "fleetid", _fleetId.ToString(CultureInfo.InvariantCulture) }
                });
                if (battle != _battleId || !_visible)
                    return;
                if (!res.Ok)
                {
                    if (res.Error != null && res.Error.Contains("battleNotFound"))
                        Finish(null);
                    else
                        SetHeader(ErrorText(res.Error), string.Empty);
                    return;
                }

                var snap = Parse(res.Body);
                if (snap == null)
                    return;
                Apply(snap);

                // Like the web: nudge the lazy server while it waits to start or sits past a turn deadline.
                var now = FleetOrderGate.UnixNow();
                if (snap.State == BattleSnapshot.Pending ||
                    (snap.State == BattleSnapshot.Active && snap.TurnDeadline > 0 && now > snap.TurnDeadline))
                {
                    await ActionJs.Get("UpdateBattle", new Dictionary<string, string>
                    {
                        { "battleid", battle.ToString(CultureInfo.InvariantCulture) }
                    });
                    _pollAt = Mathf.Min(_pollAt, Time.unscaledTime + 0.8f);
                }
            }
            finally
            {
                _polling = false;
            }
        }

        BattleSnapshot Parse(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;
            try
            {
                var root = JToken.Parse(body);
                // BattleDoAction wraps the state with the action result.
                if (root["state"] is JObject inner)
                    root = inner;
                return BattleSnapshot.Parse(root, _fleetId);
            }
            catch
            {
                return null;
            }
        }

        void Apply(BattleSnapshot snap)
        {
            var prev = _state;
            _state = snap;
            OrientBoard(snap.MyTeam);
            SyncShips(prev, snap);
            ReplayLog(snap, prev == null);

            var turnStarted = snap.MyTurn && (prev == null || !prev.MyTurn || prev.ActiveId != snap.ActiveId);
            if (turnStarted)
            {
                _pendingSkill = null;
                CicCue.Chime(_boardRoot.position + Vector3.up * 0.2f);
            }
            else if (!snap.MyTurn)
                _pendingSkill = null;

            if (snap.State == BattleSnapshot.Done)
                Finish(snap);

            // Red alert aboard while the ship we stand on is fighting.
            var aboard = false;
            if (snap.State == BattleSnapshot.Active)
                foreach (var sh in snap.Ships)
                    aboard |= sh.Alive && sh.FleetId == _focus.ViewFleetId;
            CombatEvents.SetEngaged(aboard);

            RefreshConsole();
            RefreshGrid();
            TickHeader(force: true);
        }

        void Finish(BattleSnapshot snap)
        {
            if (_outcomeShown)
                return;
            _outcomeShown = true;
            _finished.Add(_battleId);
            _dismissed.Remove(_battleId);
            var mineAlive = false;
            var anyMine = false;
            if (snap != null)
                foreach (var s in snap.Ships)
                {
                    if (!s.IsMine)
                        continue;
                    anyMine = true;
                    mineAlive |= s.Alive;
                }

            var key = snap == null || !anyMine ? "vr.battle.over" : mineAlive ? "vr.battle.victory" : "vr.battle.defeat";
            ShowOutcome(Trans.Get(key), key == "vr.battle.victory" ? UiKit.Ok : key == "vr.battle.defeat" ? UiKit.Danger : UiKit.Amber);
            var at = _boardRoot.position + Vector3.up * 0.25f;
            if (key == "vr.battle.victory")
                CicCue.Victory(at);
            else if (key == "vr.battle.defeat")
                CicCue.Defeat(at);
            else
                CicCue.Ok(at);
            _closeAt = Time.unscaledTime + CloseDelay;
            RefreshConsole();
        }

        // ── Orders ────────────────────────────────────────────────────────────────

        bool _acting;

        async Task Act(string subaction, BattleSkill skill, int targetShip, int q, int r)
        {
            var src = _state?.ActiveShip;
            if (_acting || src == null || !src.IsMine || !_state.MyTurn)
                return;
            _acting = true;
            try
            {
                var query = new Dictionary<string, string>
                {
                    { "battleid", _battleId.ToString(CultureInfo.InvariantCulture) },
                    { "fleetid", _fleetId.ToString(CultureInfo.InvariantCulture) },
                    { "bship_id", src.Id.ToString(CultureInfo.InvariantCulture) },
                    { "subaction", subaction },
                    { "battle_subaction", subaction },
                    { "target_bship_id", targetShip.ToString(CultureInfo.InvariantCulture) },
                    { "target_q", q.ToString(CultureInfo.InvariantCulture) },
                    { "target_r", r.ToString(CultureInfo.InvariantCulture) }
                };
                if (skill != null)
                    query["skill_id"] = skill.Id;
                var res = await ActionJs.Get("BattleDoAction", query);
                _pendingSkill = null;
                if (!res.Ok)
                {
                    CicCue.Fail(_boardRoot.position);
                    Toast(ErrorText(res.Error));
                    RefreshGrid();
                    RefreshConsole();
                    return;
                }

                var snap = Parse(res.Body);
                if (snap != null)
                    Apply(snap);
                _pollAt = Time.unscaledTime + PollSeconds;
            }
            finally
            {
                _acting = false;
            }
        }

        public async Task EndTurn()
        {
            if (_battleId <= 0 || _fleetId <= 0 || _acting)
                return;
            _acting = true;
            try
            {
                var res = await ActionJs.Get("BattleEndFleetTurn", new Dictionary<string, string>
                {
                    { "battleid", _battleId.ToString(CultureInfo.InvariantCulture) },
                    { "fleetid", _fleetId.ToString(CultureInfo.InvariantCulture) }
                });
                _pendingSkill = null;
                if (!res.Ok)
                {
                    CicCue.Fail(_boardRoot.position);
                    Toast(ErrorText(res.Error));
                    return;
                }

                CicCue.Ok(_boardRoot.position);
                var snap = Parse(res.Body);
                if (snap != null)
                    Apply(snap);
            }
            finally
            {
                _acting = false;
            }
        }

        async Task ReadyUp()
        {
            var res = await ActionJs.Get("SetFleetState", new Dictionary<string, string>
            {
                { "battleid", _battleId.ToString(CultureInfo.InvariantCulture) },
                { "fleetid", _fleetId.ToString(CultureInfo.InvariantCulture) },
                { "auto", "0" },
                { "ready", "1" }
            });
            if (res.Ok)
                CicCue.Ok(_boardRoot.position);
            else
            {
                CicCue.Fail(_boardRoot.position);
                Toast(ErrorText(res.Error));
            }

            _pollAt = 0f;
        }

        async Task Withdraw()
        {
            var battle = _battleId;
            var res = await ActionJs.Get("RemoveFleetFromBattle", new Dictionary<string, string>
            {
                { "battleid", battle.ToString(CultureInfo.InvariantCulture) },
                { "fleetid", _fleetId.ToString(CultureInfo.InvariantCulture) }
            });
            if (!res.Ok)
            {
                CicCue.Fail(_boardRoot.position);
                Toast(ErrorText(res.Error));
                return;
            }

            CicCue.Ok(_boardRoot.position);
            _finished.Add(battle);
            BeginFold();
        }

        /// <summary>
        /// Engage like the web (objects/fleet.js startTacticalBattle): <paramref name="fleetIds"/> = my ship
        /// first, then the targets; <paramref name="planetId"/> = the orbit fought over, 0 in open space
        /// (pirates) — only sent when non-zero. MakeBattle already marks our side ready; UpdateBattle starts
        /// it, then the table turns into the board.
        /// </summary>
        public async Task<ApiResult> MakeBattle(IList<int> fleetIds, int planetId = 0)
        {
            if (_focus == null || fleetIds == null || fleetIds.Count < 2)
                return ApiResult.Fail(Trans.Get("vr.common.error"));
            var query = new Dictionary<string, string>
            {
                { "systemid", _focus.SystemId.ToString(CultureInfo.InvariantCulture) },
                { "fleets", string.Join(",", fleetIds) }
            };
            if (planetId > 0)
                query["planetid"] = planetId.ToString(CultureInfo.InvariantCulture);
            var result = await ActionJs.Get("MakeBattle", query);
            if (!result.Ok)
                return result;

            var battleId = 0;
            try
            {
                battleId = FocusContext.AsInt(JToken.Parse(result.Body)["id"]);
            }
            catch
            {
                int.TryParse(result.Body, out battleId);
            }

            if (battleId > 0)
            {
                await ActionJs.Get("UpdateBattle", new Dictionary<string, string>
                {
                    { "battleid", battleId.ToString(CultureInfo.InvariantCulture) }
                });
                Open(battleId, fleetIds[0]);
            }

            return result;
        }

        /// <summary>Raw battle codes (no_pm, out_of_range…) read as native keys; other errors are already text.</summary>
        static string ErrorText(string error)
        {
            if (string.IsNullOrEmpty(error))
                return Trans.Get("vr.common.error");
            var code = error.Trim();
            if (code.IndexOf(' ') >= 0)
                return code;
            if (code is "notYourTurn" or "notYourFleet" or "battleNotFound" or "fleetNotInBattle" or "battleAlreadyStarted")
                return Trans.Get(code);
            return Trans.Get("vr.battle.err." + code);
        }
    }
}
