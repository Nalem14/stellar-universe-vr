using System.Collections.Generic;
using Core.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.Vfx
{
    /// <summary>
    /// Aiming on the battle board, point → point like the rest of the table: the active ship is ours to
    /// play, reachable cells breathe green; trigger (ray) or finger (poke) on a cell moves there. A skill
    /// button arms that skill: cells in range light up (red hostile, green friendly, violet jump), the
    /// area of a blast previews under the aim; trigger on the target fires. Self skills fire at once.
    /// Everything is analytic (ray ∩ board plane, closest approach to hulls): no collider per cell.
    /// </summary>
    public partial class HexBattleController
    {
        const float RayLength = 4f;
        const float ShipPickRadius = 0.032f;
        const float PokeHeight = 0.04f;

        BattleSkill _pendingSkill;
        Vector2Int? _hoverCell;
        int _inspect;
        NearFarInteractor[] _rays = System.Array.Empty<NearFarInteractor>();
        XRPokeInteractor[] _pokes = System.Array.Empty<XRPokeInteractor>();
        readonly Dictionary<XRPokeInteractor, Vector2Int?> _touch = new();
        float _nextScan;

#if UNITY_EDITOR
        /// <summary>Editor checks (no headset): forced aim cell.</summary>
        public static Vector2Int? EditorAim;
#endif

        void ResetAim()
        {
            _pendingSkill = null;
            _hoverCell = null;
            _inspect = 0;
            _touch.Clear();
        }

        void UpdateInput()
        {
            if (_state == null || _boardRoot == null)
                return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                _rays = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
                _pokes = FindObjectsByType<XRPokeInteractor>(FindObjectsSortMode.None);
            }

            Vector2Int? aimed = null;
            foreach (var ray in _rays)
            {
                if (ray == null || !ray.isActiveAndEnabled)
                    continue;
                // A ray resting on a console button or a panel belongs to it.
                if (ray.interactablesHovered.Count > 0 ||
                    (ray.TryGetCurrentUIRaycastResult(out RaycastResult ui) && ui.isValid))
                    continue;
                var cell = RayCell(ray.transform.position, ray.transform.forward);
                if (cell.HasValue && !aimed.HasValue)
                    aimed = cell;
                if (ray.activateInput.ReadWasPerformedThisFrame())
                {
                    if (cell.HasValue)
                        ClickCell(cell.Value.x, cell.Value.y);
                    break;
                }
            }

            foreach (var poke in _pokes)
            {
                if (poke == null || !poke.isActiveAndEnabled)
                    continue;
                var tip = poke.attachTransform != null ? poke.attachTransform.position : poke.transform.position;
                var local = _frame.InverseTransformPoint(tip);
                Vector2Int? cell = null;
                if (local.y > -0.01f && local.y < PokeHeight)
                {
                    var c = LocalHex(local);
                    if (BattleSnapshot.InGrid(c.x, c.y))
                        cell = c;
                }

                _touch.TryGetValue(poke, out var before);
                if (cell.HasValue && cell != before)
                    ClickCell(cell.Value.x, cell.Value.y);
                _touch[poke] = cell;
                if (cell.HasValue && !aimed.HasValue)
                    aimed = cell;
            }

#if UNITY_EDITOR
            if (EditorAim.HasValue)
                aimed = EditorAim;
#endif
            if (aimed != _hoverCell)
            {
                _hoverCell = aimed;
                if (aimed.HasValue)
                    CicCue.Hover(_frame.TransformPoint(HexLocal(aimed.Value.x, aimed.Value.y)));
                RefreshGrid();
                UpdateAimCue();
            }
        }

        /// <summary>Hull under the ray first (closest approach), else the cell where it meets the board.</summary>
        Vector2Int? RayCell(Vector3 origin, Vector3 dir)
        {
            var best = float.MaxValue;
            Vector2Int? hit = null;
            foreach (var v in _ships.Values)
            {
                if (v.Root == null || !v.Root.activeSelf || v.Data == null || !v.Data.Alive)
                    continue;
                var c = v.Root.transform.position;
                var t = Vector3.Dot(c - origin, dir);
                if (t <= 0f || t > RayLength)
                    continue;
                var d = (origin + dir * t - c).magnitude;
                if (d < ShipPickRadius && t < best)
                {
                    best = t;
                    hit = new Vector2Int(v.Data.Q, v.Data.R);
                }
            }

            if (hit.HasValue)
                return hit;

            var o = _frame.InverseTransformPoint(origin);
            var dl = _frame.InverseTransformDirection(dir);
            if (Mathf.Abs(dl.y) < 1e-4f)
                return null;
            var k = -o.y / dl.y;
            if (k <= 0f || k > RayLength)
                return null;
            var cell = LocalHex(o + dl * k);
            return BattleSnapshot.InGrid(cell.x, cell.y) ? cell : null;
        }

        /// <summary>Arm a skill (second press disarms); self skills fire at once on the active ship.</summary>
        public void ChooseSkill(BattleSkill skill)
        {
            var src = _state?.MyTurn == true ? _state.ActiveShip : null;
            if (src == null || skill == null)
                return;
            if (skill.SelfCast)
            {
                _pendingSkill = null;
                AsyncTap.Run(Act("skill", skill, src.Id, src.Q, src.R));
                return;
            }

            _pendingSkill = _pendingSkill != null && _pendingSkill.Id == skill.Id ? null : skill;
            CicCue.Hover(_console.position);
            RefreshGrid();
            RefreshConsole();
            UpdateAimCue();
        }

        /// <summary>Trigger / poke on a cell (public for Editor checks).</summary>
        public void ClickCell(int q, int r)
        {
            var s = _state;
            if (s == null || _acting || _outcomeShown)
                return;
            var ship = s.At(q, r);
            var src = s.MyTurn && s.State == BattleSnapshot.Active ? s.ActiveShip : null;
            if (src == null)
            {
                Inspect(ship);
                return;
            }

            var d = BattleSnapshot.Dist(src.Q, src.R, q, r);
            var skill = _pendingSkill;
            if (skill != null)
            {
                if (ship != null && ship.Id == src.Id)
                {
                    // Aiming our own hull disarms the skill (back to moving).
                    _pendingSkill = null;
                    RefreshGrid();
                    RefreshConsole();
                    return;
                }

                if (skill.Range > 0 && d > skill.Range)
                {
                    Refuse(Trans.Format("vr.battle.outOfRange", d, skill.Range));
                    return;
                }

                if (d < skill.RangeMin)
                {
                    Refuse(Trans.Format("vr.battle.tooClose", skill.RangeMin));
                    return;
                }

                if (skill.TargetsEnemy)
                {
                    if (ship == null || ship.Team == src.Team)
                    {
                        Refuse(Trans.Get("vr.battle.pickTarget"));
                        return;
                    }

                    Fire(skill, ship.Id, q, r);
                    return;
                }

                if (skill.TargetsAlly)
                {
                    if (ship == null || ship.Team != src.Team)
                    {
                        Refuse(Trans.Get("vr.battle.pickAlly"));
                        return;
                    }

                    Fire(skill, ship.Id, q, r);
                    return;
                }

                if (skill.Type == "teleport" && ship != null)
                {
                    Refuse(Trans.Get("vr.battle.err.hex_occupied"));
                    return;
                }

                Fire(skill, ship?.Id ?? 0, q, r);
                return;
            }

            if (ship != null)
            {
                Inspect(ship.Id == src.Id ? null : ship);
                return;
            }

            if (src.StatusTurns("gravity") > 0)
            {
                Refuse(Trans.Get("vr.battle.err.gravity_field"));
                return;
            }

            if (d <= 0)
                return;
            if (d > src.Pm)
            {
                Refuse(Trans.Format("vr.battle.tooFar", d, src.Pm));
                return;
            }

            CicCue.Ok(_frame.TransformPoint(HexLocal(q, r)));
            AsyncTap.Run(Act("move", null, 0, q, r));
        }

        void Fire(BattleSkill skill, int target, int q, int r)
        {
            CicCue.Ok(_frame.TransformPoint(HexLocal(q, r)));
            AsyncTap.Run(Act("skill", skill, target, q, r));
        }

        void Refuse(string text)
        {
            CicCue.Fail(_boardRoot.position);
            Toast(text);
        }

        void Inspect(BattleShip ship)
        {
            _inspect = ship != null ? ship.Id : 0;
            if (ship != null)
                CicCue.Hover(_boardRoot.position);
            RefreshCard();
        }
    }
}
