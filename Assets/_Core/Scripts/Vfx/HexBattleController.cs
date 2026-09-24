using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Hex TBT combat on the holo table. Schema fields from GetBattleState are mapped defensively
    /// after live dump — do not invent required fields beyond API purpose (ships, hexes, log, active_bship_id, skills).
    /// </summary>
    public class HexBattleController : MonoBehaviour
    {
        FocusContext _focus;
        HoloMapController _mapCtrl;
        Transform _root;
        TMP_Text _log;
        readonly List<GameObject> _cells = new();
        readonly List<GameObject> _pieces = new();
        int _battleId;
        int _fleetId;
        int _activeBship;
        bool _visible;
        float _pollAt;
        CicArtKit _art;
        bool _detecting;

        public bool IsActive => _visible && _battleId > 0;

        public void Bind(FocusContext focus, HoloMapController mapCtrl, Transform tableMount, CicArtKit art)
        {
            _focus = focus;
            _mapCtrl = mapCtrl;
            _art = art;
            EnsureRoot(tableMount);
        }

        void EnsureRoot(Transform tableMount)
        {
            if (_root != null || tableMount == null)
                return;
            _root = new GameObject("HexBattleBoard").transform;
            _root.SetParent(tableMount, false);
            _root.localPosition = new Vector3(0f, 0.12f, 0f);
            _root.gameObject.SetActive(false);

            var logGo = new GameObject("BattleLog");
            logGo.transform.SetParent(_root, false);
            logGo.transform.localPosition = new Vector3(0f, 0.02f, -0.42f);
            logGo.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            logGo.transform.localScale = Vector3.one * 0.008f;
            _log = logGo.AddComponent<TextMeshPro>();
            _log.alignment = TextAlignmentOptions.Center;
            _log.fontSize = 7f;
            _log.color = new Color(1f, 0.75f, 0.35f, 0.95f);
            _log.text = string.Empty;
        }

        public void Show()
        {
            if (_root != null)
                _root.gameObject.SetActive(true);
            _visible = true;
        }

        public void Hide()
        {
            _visible = false;
            if (_root != null)
                _root.gameObject.SetActive(false);
            ClearBoard();
            _battleId = 0;
        }

        void Update()
        {
            if (_focus == null)
                return;

            // Detect server-started battles even while the hex board is hidden.
            if (!_visible)
            {
                if (_battleId <= 0 && !_detecting && Time.time >= _pollAt)
                {
                    _pollAt = Time.time + 2f;
                    Core.Utils.AsyncTap.Run(TryDetectBattle());
                }

                return;
            }

            if (_battleId <= 0 && !_detecting)
                Core.Utils.AsyncTap.Run(TryDetectBattle());

            if (_battleId > 0 && Time.time >= _pollAt)
            {
                _pollAt = Time.time + 1.5f;
                Core.Utils.AsyncTap.Run(PollState());
            }
        }

        async Task TryDetectBattle()
        {
            if (_detecting)
                return;
            _detecting = true;
            try
            {
                var view = _focus.FindViewFleet();
                if (view == null || !view.IsInBattle)
                    return;

                var mine = await ActionJs.Get("GetMyBattles");
                if (!mine.Ok || string.IsNullOrEmpty(mine.Body))
                    return;

                try
                {
                    var root = JToken.Parse(mine.Body);
                    var arr = root as JArray ?? root["battles"] as JArray ?? root["data"] as JArray;
                    if (arr == null)
                        return;
                    foreach (var b in arr)
                    {
                        var id = FocusContext.AsInt(b["id"] ?? b["battleid"]);
                        if (id <= 0)
                            continue;
                        _battleId = id;
                        _fleetId = view.Id;
                        _mapCtrl?.SetMode(HoloMapMode.HexBattle);
                        if (_log != null)
                            _log.text = Trans.Get("Loading");
                        return;
                    }
                }
                catch
                {
                    // Shape varies — dump live before hardening renderer.
                }
            }
            finally
            {
                _detecting = false;
            }
        }

        async Task PollState()
        {
            if (_battleId <= 0 || _fleetId <= 0)
                return;
            var state = await ActionJs.Get("GetBattleState", new Dictionary<string, string>
            {
                { "battleid", _battleId.ToString() },
                { "fleetid", _fleetId.ToString() }
            });
            if (!state.Ok)
            {
                if (_log != null)
                    _log.text = state.Error;
                return;
            }

            ApplyState(state.Body);
        }

        void ApplyState(string body)
        {
            if (string.IsNullOrEmpty(body))
                return;
            try
            {
                var root = JToken.Parse(body);
                _activeBship = FocusContext.AsInt(root["active_bship_id"]);
                ClearBoard();
                BuildHexGrid(root["hexes"] as JArray ?? root["hex"] as JArray);
                BuildShips(root["ships"] as JArray ?? root["battle_ships"] as JArray);
                var log = root["log"] as JArray;
                if (_log != null)
                {
                    if (log != null && log.Count > 0)
                        _log.text = FocusContext.AsString(log[log.Count - 1]?["msg"] ?? log[log.Count - 1]);
                    else
                        _log.text = Trans.Get("fleets") + " #" + _activeBship;
                }
            }
            catch
            {
                if (_log != null)
                    _log.text = Trans.Get("vr.common.error");
            }
        }

        void BuildHexGrid(JArray hexes)
        {
            const int fallbackRings = 3;
            if (hexes != null && hexes.Count > 0)
            {
                foreach (var h in hexes)
                {
                    var q = FocusContext.AsInt(h["q"]);
                    var r = FocusContext.AsInt(h["r"]);
                    SpawnCell(q, r);
                }

                return;
            }

            for (var q = -fallbackRings; q <= fallbackRings; q++)
            for (var r = -fallbackRings; r <= fallbackRings; r++)
            {
                if (Mathf.Abs(q + r) > fallbackRings)
                    continue;
                SpawnCell(q, r);
            }
        }

        void SpawnCell(int q, int r)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"Hex_{q}_{r}";
            go.transform.SetParent(_root, false);
            var pos = HexToLocal(q, r);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(0.055f, 0.004f, 0.055f);
            if (_art != null)
                go.GetComponent<MeshRenderer>().sharedMaterial =
                    _art.Holo(_art.HexGrid != null ? _art.HexGrid : Texture2D.whiteTexture,
                        new Color(1f, 0.55f, 0.2f, 0.45f));
            _cells.Add(go);

            var col = go.GetComponent<Collider>();
            if (col == null)
                col = go.AddComponent<MeshCollider>();
            col.enabled = true;

            var baseScale = go.transform.localScale;
            var interact = go.AddComponent<XRSimpleInteractable>();
            var qq = q;
            var rr = r;
            interact.hoverEntered.AddListener(_ =>
            {
                go.transform.localScale = baseScale * 1.25f;
                CicCue.Hover(go.transform.position);
            });
            interact.hoverExited.AddListener(_ => { go.transform.localScale = baseScale; });
            interact.selectEntered.AddListener(_ =>
            {
                CicCue.Ok(go.transform.position);
                Core.Utils.AsyncTap.Run(TryMove(qq, rr));
            });
        }

        void BuildShips(JArray ships)
        {
            if (ships == null)
                return;
            foreach (var s in ships)
            {
                var id = FocusContext.AsInt(s["id"] ?? s["bship_id"]);
                var q = FocusContext.AsInt(s["q"]);
                var r = FocusContext.AsInt(s["r"]);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "BShip_" + id;
                go.transform.SetParent(_root, false);
                go.transform.localPosition = HexToLocal(q, r) + Vector3.up * 0.03f;
                var baseScale = Vector3.one * (id == _activeBship ? 0.055f : 0.042f);
                go.transform.localScale = baseScale;
                var mine = FocusContext.AsInt(s["fleetid"] ?? s["userid"]) == _fleetId;
                var tint = id == _activeBship
                    ? new Color(1f, 0.95f, 0.4f, 0.95f)
                    : mine
                        ? new Color(0.2f, 0.95f, 1f, 0.9f)
                        : new Color(1f, 0.35f, 0.2f, 0.9f);
                if (_art != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(Texture2D.whiteTexture, tint);

                var shipId = id;
                var interact = go.AddComponent<XRSimpleInteractable>();
                interact.hoverEntered.AddListener(_ =>
                {
                    go.transform.localScale = baseScale * 1.2f;
                    CicCue.Hover(go.transform.position);
                });
                interact.hoverExited.AddListener(_ => { go.transform.localScale = baseScale; });
                interact.selectEntered.AddListener(_ =>
                {
                    CicCue.Ok(go.transform.position);
                    _activeBship = shipId;
                    if (_log != null)
                        _log.text = Trans.Get("fleets") + " #" + shipId;
                });
                _pieces.Add(go);
            }
        }

        async Task TryMove(int q, int r)
        {
            if (_activeBship <= 0 || _battleId <= 0)
                return;
            var result = await ActionJs.Get("BattleDoAction", new Dictionary<string, string>
            {
                { "battleid", _battleId.ToString() },
                { "fleetid", _fleetId.ToString() },
                { "bship_id", _activeBship.ToString() },
                // "action" is the endpoint's own key: the server reads the battle verb from subaction.
                { "subaction", "move" },
                { "battle_subaction", "move" },
                // skill_id is required non-empty; "0" is PHP-empty so the server ignores it for moves.
                { "skill_id", "0" },
                { "target_bship_id", "0" },
                { "target_q", q.ToString() },
                { "target_r", r.ToString() }
            });
            if (_log != null)
                _log.text = result.Ok ? Trans.Get("vr.common.ok") : result.Error;
            if (result.Ok)
                await PollState();
        }

        public async Task EndTurn()
        {
            if (_battleId <= 0 || _fleetId <= 0)
                return;
            var result = await ActionJs.Get("BattleEndFleetTurn", new Dictionary<string, string>
            {
                { "battleid", _battleId.ToString() },
                { "fleetid", _fleetId.ToString() }
            });
            if (_log != null)
                _log.text = result.Ok ? Trans.Get("vr.tactical.endTurn") : result.Error;
            if (result.Ok)
                await PollState();
        }

        public async Task MakeBattle(IList<int> fleetIds)
        {
            if (_focus == null || fleetIds == null || fleetIds.Count == 0)
                return;
            var csv = string.Join(",", fleetIds);
            var result = await ActionJs.Get("MakeBattle", new Dictionary<string, string>
            {
                { "systemid", _focus.SystemId.ToString() },
                { "fleets", csv }
            });
            if (!result.Ok)
            {
                if (_log != null)
                    _log.text = result.Error;
                return;
            }

            try
            {
                var root = JToken.Parse(result.Body);
                _battleId = FocusContext.AsInt(root["id"] ?? root["battleid"] ?? root);
            }
            catch
            {
                int.TryParse(result.Body, out _battleId);
            }

            _fleetId = fleetIds[0];
            if (_battleId > 0)
            {
                await ActionJs.Get("SetFleetState", new Dictionary<string, string>
                {
                    { "battleid", _battleId.ToString() },
                    { "fleetid", _fleetId.ToString() },
                    { "auto", "0" },
                    { "ready", "1" }
                });
                await ActionJs.Get("UpdateBattle", new Dictionary<string, string>
                {
                    { "battleid", _battleId.ToString() }
                });
                _mapCtrl?.SetMode(HoloMapMode.HexBattle);
            }
        }

        static Vector3 HexToLocal(int q, int r)
        {
            const float size = 0.06f;
            var x = size * (Mathf.Sqrt(3f) * q + Mathf.Sqrt(3f) / 2f * r);
            var z = size * (1.5f * r);
            return new Vector3(x, 0f, z);
        }

        void ClearBoard()
        {
            for (var i = 0; i < _cells.Count; i++)
                if (_cells[i] != null)
                    Object.Destroy(_cells[i]);
            for (var i = 0; i < _pieces.Count; i++)
                if (_pieces[i] != null)
                    Object.Destroy(_pieces[i]);
            _cells.Clear();
            _pieces.Clear();
        }
    }
}
