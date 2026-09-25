using System.Collections.Generic;
using Core.UI;
using Core.Utils;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Combat effects, all pooled: beams replayed from the new battle_actions rows (ours and the enemy's),
    /// one shared particle system for impact flashes, rising damage / repair numbers, kills, and the aim
    /// arc from the active ship to the cell under the ray. Colours follow the web (scenes/battle.js
    /// _playActionFX): repair green, ion blue, plasma violet, turrets amber, the rest red.
    /// </summary>
    public partial class HexBattleController
    {
        const int BeamPool = 6;
        const int FloaterPool = 8;

        class Beam
        {
            public LineRenderer Line;
            public Vector3 From;
            public Vector3 To;
            public float T = -1f;
            public float Travel;
            public Color Color;
            public bool Impacted;
            public bool Heavy;
        }

        class Floater_
        {
            public Transform Root;
            public TextMeshPro Text;
            public float T = -1f;
            public Vector3 Start;
        }

        readonly List<Beam> _beams = new();
        readonly List<Floater_> _floaters = new();
        ParticleSystem _flash;
        Material _beamMat;
        LineRenderer _aimLine;
        TextMeshPro _aimLabel;

        void EnsureFx()
        {
            if (_flash != null || _boardRoot == null)
                return;
            _beamMat = CombatFxKit.Beam();
            _flash = CombatFxKit.Burst(_boardRoot, "BattleFlash", 64, 0.45f, gravity: false, stretch: false);

            for (var i = 0; i < BeamPool; i++)
            {
                var b = new GameObject("Beam" + i).AddComponent<LineRenderer>();
                b.transform.SetParent(_boardRoot, false);
                b.useWorldSpace = true;
                b.positionCount = 2;
                b.numCapVertices = 2;
                b.textureMode = LineTextureMode.Stretch;
                b.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.sharedMaterial = _beamMat;
                b.enabled = false;
                _beams.Add(new Beam { Line = b });
            }

            for (var i = 0; i < FloaterPool; i++)
            {
                var root = new GameObject("Floater" + i).transform;
                root.SetParent(_boardRoot, false);
                root.gameObject.AddComponent<BillboardFace>();
                var t = UiKit.Label(root, "Text", string.Empty, Vector3.zero, 0.3f, 0.016f, UiKit.TextBright);
                t.fontStyle = FontStyles.Bold;
                t.outlineWidth = 0.22f;
                t.outlineColor = new Color32(2, 8, 12, 230);
                root.gameObject.SetActive(false);
                _floaters.Add(new Floater_ { Root = root, Text = t });
            }

            var aim = new GameObject("AimArc");
            aim.transform.SetParent(_boardRoot, false);
            _aimLine = aim.AddComponent<LineRenderer>();
            _aimLine.useWorldSpace = true;
            _aimLine.positionCount = 16;
            _aimLine.widthMultiplier = 0.005f;
            _aimLine.numCapVertices = 2;
            _aimLine.textureMode = LineTextureMode.Stretch;
            _aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _aimLine.sharedMaterial = _beamMat;
            _aimLine.enabled = false;
            var labelRoot = new GameObject("AimLabel").transform;
            labelRoot.SetParent(_boardRoot, false);
            labelRoot.gameObject.AddComponent<BillboardFace>();
            _aimLabel = UiKit.Label(labelRoot, "Text", string.Empty, Vector3.zero, 0.3f, 0.014f, UiKit.TextBright);
            _aimLabel.outlineWidth = 0.2f;
            _aimLabel.outlineColor = new Color32(2, 8, 12, 230);
            labelRoot.gameObject.SetActive(false);
        }

        // ── Log replay ─────────────────────────────────────────────────────────────

        void ReplayLog(BattleSnapshot snap, bool initial)
        {
            EnsureFx();
            var max = _lastLogId;
            foreach (var e in snap.Log)
            {
                if (e.Id <= _lastLogId)
                    continue;
                max = Mathf.Max(max, e.Id);
                if (initial || _lastLogId < 0)
                    continue;
                if (e.Action == "skill")
                    PlaySkill(e);
            }

            _lastLogId = Mathf.Max(max, 0);
        }

        void PlaySkill(BattleLogEntry e)
        {
            if (!_ships.TryGetValue(e.Src, out var src) || src.Root == null)
                return;
            var r = e.Result;
            var from = src.Root.transform.position;
            var skillId = SkillIdOf(src.Data, r);
            var color = SkillColor(skillId, r);
            var heavy = skillId.Contains("missile") || skillId.Contains("torpedo");
            var any = false;

            if (r?["hits"] is JArray hits)
            {
                foreach (var h in hits)
                {
                    var id = h.Type == JTokenType.Object ? FocusContext_AsInt(h["target"]) : FocusContext_AsInt(h);
                    if (_ships.TryGetValue(id, out var tv) && tv.Root != null)
                    {
                        FireBeam(from, tv.Root.transform.position, color, heavy);
                        CombatEvents.RaiseShot(src.Data.FleetId, tv.Data.FleetId, color, heavy);
                        any = true;
                    }
                }

                if (!any)
                {
                    Flash(from, color, 0.12f);
                    CombatEvents.RaiseSelf(src.Data.FleetId, color);
                }

                return;
            }

            var target = r?["target"] != null ? FocusContext_AsInt(r["target"]) : e.Target;
            if (target > 0 && target != e.Src && _ships.TryGetValue(target, out var t) && t.Root != null)
            {
                FireBeam(from, t.Root.transform.position, color, heavy);
                CombatEvents.RaiseShot(src.Data.FleetId, t.Data.FleetId, color, heavy);
                return;
            }

            if (r?["moved_to"] is JArray to && to.Count >= 2)
            {
                // Jump: flare out here, flare in there (the hull glides on the next diff).
                Flash(from, new Color(0.75f, 0.5f, 1f, 1f), 0.1f);
                var dest = _frame.TransformPoint(HexLocal(FocusContext_AsInt(to[0]), FocusContext_AsInt(to[1])) +
                                                 Vector3.up * ShipHover);
                Flash(dest, new Color(0.75f, 0.5f, 1f, 1f), 0.1f);
                CicCue.Zap(from, 1.5f);
                CombatEvents.RaiseSelf(src.Data.FleetId, new Color(0.75f, 0.5f, 1f, 1f));
                return;
            }

            // Self skill: shield / armour / engines / repair around the caster.
            Flash(from, color, 0.11f);
            CicCue.Zap(from, 1.7f);
            CombatEvents.RaiseSelf(src.Data.FleetId, color);
        }

        static int FocusContext_AsInt(JToken t) => Core.App.FocusContext.AsInt(t);

        static string SkillIdOf(BattleShip src, JObject r)
        {
            if (r == null || src == null)
                return string.Empty;
            // Rows logged since the server traces the fired skill carry its id; older rows are inferred.
            var traced = Core.App.FocusContext.AsString(r["skill"]);
            if (!string.IsNullOrEmpty(traced))
                return traced;
            if (r["healed"] != null)
                return "repair";
            if (r["shield"] != null)
                return "shield_regen";
            if (r["effect"] != null)
            {
                var fx = Core.App.FocusContext.AsString(r["effect"]);
                return fx == "ionized" ? "ion" : fx;
            }

            var dmg = Core.App.FocusContext.AsInt(r["damage"]);
            foreach (var k in src.Skills)
                if (k.Damage > 0 && Mathf.Abs(k.Damage - dmg) <= k.Damage / 2)
                    return k.Id;
            return string.Empty;
        }

        static Color SkillColor(string id, JObject r)
        {
            if (r != null && (r["healed"] != null || r["shield"] != null))
                return new Color(0.3f, 1f, 0.6f, 1f);
            if (id.Contains("ion") || id.Contains("emp") || id.Contains("jammed") || id.Contains("energy"))
                return new Color(0.3f, 0.75f, 1f, 1f);
            if (id.Contains("plasma") || id.Contains("gravity"))
                return new Color(0.7f, 0.4f, 1f, 1f);
            if (id.Contains("defense") || id.Contains("turret") || id.Contains("heat") || id.Contains("overheated"))
                return new Color(1f, 0.65f, 0.2f, 1f);
            return new Color(1f, 0.28f, 0.22f, 1f);
        }

        void FireBeam(Vector3 from, Vector3 to, Color color, bool heavy)
        {
            Beam free = null;
            foreach (var b in _beams)
                if (b.T < 0f)
                {
                    free = b;
                    break;
                }

            free ??= _beams[0];
            free.From = from;
            free.To = to;
            free.Color = color;
            free.Heavy = heavy;
            free.Travel = heavy ? 0.45f : 0.1f;
            free.T = 0f;
            free.Impacted = false;
            free.Line.enabled = true;
            CicCue.Zap(from, heavy ? 0.7f : color.b > 0.8f ? 1.3f : 1f);
        }

        void Flash(Vector3 at, Color color, float size) => CombatFxKit.Emit(_flash, at, color, size, 0.5f);

        void Floater(Vector3 at, string text, Color color)
        {
            EnsureFx();
            Floater_ free = null;
            foreach (var f in _floaters)
                if (f.T < 0f)
                {
                    free = f;
                    break;
                }

            free ??= _floaters[0];
            free.Start = at;
            free.T = 0f;
            free.Text.text = text;
            free.Text.color = color;
            free.Root.position = at;
            free.Root.gameObject.SetActive(true);
        }

        void Destroyed(ShipView v)
        {
            v.DeathT = 0f;
            var at = v.Root.transform.position;
            Flash(at, new Color(1f, 0.6f, 0.25f, 1f), 0.22f);
            Flash(at, new Color(1f, 0.9f, 0.7f, 1f), 0.1f);
            CicCue.Boom(at, 0.8f);
            Floater(at + Vector3.up * 0.07f, Trans.Get("vr.battle.destroyed"), UiKit.Danger);
            if (v.Data != null)
                CombatEvents.RaiseDestroyed(v.Data.FleetId);
        }

        void UpdateFx()
        {
            var dt = Time.unscaledDeltaTime;
            foreach (var b in _beams)
            {
                if (b.T < 0f)
                    continue;
                b.T += dt;
                var grow = Mathf.Clamp01(b.T / b.Travel);
                var fade = Mathf.Clamp01((b.T - b.Travel) / 0.3f);
                if (b.Heavy)
                {
                    // Torpedo: a short bright slug crossing the board, no continuous beam.
                    var head = Vector3.Lerp(b.From, b.To, grow);
                    var tail = Vector3.Lerp(b.From, b.To, Mathf.Max(0f, grow - 0.18f));
                    b.Line.SetPosition(0, tail);
                    b.Line.SetPosition(1, head);
                    b.Line.widthMultiplier = 0.012f;
                }
                else
                {
                    b.Line.SetPosition(0, b.From);
                    b.Line.SetPosition(1, Vector3.Lerp(b.From, b.To, grow));
                    b.Line.widthMultiplier = 0.009f * (1f + Mathf.Sin(b.T * 60f) * 0.15f);
                }

                var c = b.Color;
                c.a = 1f - fade;
                b.Line.startColor = c;
                b.Line.endColor = new Color(1f, 1f, 1f, c.a);
                if (!b.Impacted && grow >= 1f)
                {
                    b.Impacted = true;
                    Flash(b.To, b.Color, b.Heavy ? 0.16f : 0.09f);
                    CicCue.Boom(b.To, b.Heavy ? 0.6f : 0.3f);
                }

                if (fade >= 1f)
                {
                    b.T = -1f;
                    b.Line.enabled = false;
                }
            }

            foreach (var f in _floaters)
            {
                if (f.T < 0f)
                    continue;
                f.T += dt;
                var u = f.T / 1.2f;
                f.Root.position = f.Start + Vector3.up * (0.06f * MotionEase.SmoothOut(Mathf.Clamp01(u)));
                f.Text.alpha = 1f - Mathf.Clamp01((u - 0.6f) / 0.4f);
                if (u >= 1f)
                {
                    f.T = -1f;
                    f.Root.gameObject.SetActive(false);
                }
            }

            foreach (var v in _ships.Values)
            {
                if (v.DeathT < 0f || v.Root == null || !v.Root.activeSelf)
                    continue;
                v.DeathT += dt;
                var k = Mathf.Clamp01(v.DeathT / 0.9f);
                v.Root.transform.localScale = Vector3.one * (1f - MotionEase.SmoothInOut(k));
                v.Root.transform.localPosition = v.To - Vector3.up * (k * 0.02f);
                if (k >= 1f)
                    v.Root.SetActive(false);
            }
        }

        void ClearFx()
        {
            foreach (var b in _beams)
            {
                b.T = -1f;
                if (b.Line != null)
                    b.Line.enabled = false;
            }

            foreach (var f in _floaters)
            {
                f.T = -1f;
                if (f.Root != null)
                    f.Root.gameObject.SetActive(false);
            }

            if (_flash != null)
                _flash.Clear();
            if (_aimLine != null)
                _aimLine.enabled = false;
            if (_aimLabel != null)
                _aimLabel.transform.parent.gameObject.SetActive(false);
        }

        // ── Aim arc (active ship → cell under the aim) ─────────────────────────────

        void UpdateAimCue()
        {
            EnsureFx();
            var s = _state;
            var src = s != null && s.MyTurn && s.State == BattleSnapshot.Active ? s.ActiveShip : null;
            string label = null;
            var color = Reach;
            if (src != null && _hoverCell.HasValue && _ships.TryGetValue(src.Id, out var sv) && sv.Root != null)
            {
                var c = _hoverCell.Value;
                var d = BattleSnapshot.Dist(src.Q, src.R, c.x, c.y);
                var target = s.At(c.x, c.y);
                var skill = _pendingSkill;
                if (skill == null)
                {
                    if (target == null && d > 0 && d <= src.Pm && src.StatusTurns("gravity") <= 0)
                        label = Trans.Format("vr.battle.pmCost", d);
                }
                else if (InRange(src, skill, c))
                {
                    if (skill.TargetsEnemy && target != null && target.Team != src.Team)
                    {
                        color = Hostile;
                        label = skill.Damage > 0 ? "-" + skill.Damage : Trans.Get("battleSkill_" + skill.Id);
                    }
                    else if (skill.TargetsAlly && target != null && target.Team == src.Team)
                    {
                        color = Friendly;
                        label = "+" + skill.Heal;
                    }
                    else if (skill.TargetsHex && (skill.Type != "teleport" || target == null))
                    {
                        color = skill.Type == "teleport" ? Jump : Blast;
                        label = skill.Damage > 0 ? "-" + skill.Damage : Trans.Get("battleSkill_" + skill.Id);
                    }
                }

                if (label != null)
                {
                    var from = sv.Root.transform.position;
                    var to = _frame.TransformPoint(HexLocal(c.x, c.y) + Vector3.up * (target != null ? ShipHover : 0.004f));
                    var lift = Mathf.Clamp((to - from).magnitude * 0.35f, 0.03f, 0.18f);
                    for (var i = 0; i < _aimLine.positionCount; i++)
                    {
                        var u = i / (_aimLine.positionCount - 1f);
                        _aimLine.SetPosition(i, Vector3.Lerp(from, to, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * lift));
                    }

                    _aimLine.startColor = new Color(color.r, color.g, color.b, 0.5f);
                    _aimLine.endColor = new Color(color.r, color.g, color.b, 1f);
                    _aimLine.enabled = true;
                    _aimLabel.text = label;
                    _aimLabel.color = Color.Lerp(color, Color.white, 0.4f);
                    _aimLabel.transform.parent.position = Vector3.Lerp(from, to, 0.5f) + Vector3.up * (lift + 0.02f);
                    _aimLabel.transform.parent.gameObject.SetActive(true);
                    return;
                }
            }

            _aimLine.enabled = false;
            _aimLabel.transform.parent.gameObject.SetActive(false);
        }
    }
}
