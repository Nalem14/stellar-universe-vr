using System.Collections.Generic;
using Core.App;
using Newtonsoft.Json.Linq;

namespace Core.Vfx
{
    /// <summary>
    /// One combat skill of a battle ship (model/battle.php BATTLE_SKILL_DEFS, stacked by module count in
    /// GetSkillsForFleet). How it is aimed on the table follows the server's BattleResolveAction.
    /// </summary>
    public sealed class BattleSkill
    {
        public string Id = string.Empty;
        public string Type = string.Empty;
        public string ModuleType = string.Empty;
        public int Ap;
        public int Damage;
        public int Heal;
        public int Shield;
        public int RangeMin;
        public int Range;
        public int Aoe;
        public int Cooldown;
        public int CooldownLeft;
        public int Count = 1;

        /// <summary>
        /// Fires at once on the ship itself: range 0 skills, shield regen (the server always shields the
        /// caster) and the self-centred pulse (debuff_aoe hits around the caster).
        /// </summary>
        public bool SelfCast => Type == "heal_shield" || Type == "debuff_aoe" || Range <= 0;

        public bool TargetsEnemy => !SelfCast &&
                                    Type is "attack" or "attack_status" or "debuff" or "cyber_hack";

        public bool TargetsAlly => !SelfCast && Type == "heal";

        /// <summary>Aimed at a cell: area strike (any cell in range) or jump (an empty one).</summary>
        public bool TargetsHex => !SelfCast && Type is "attack_aoe" or "teleport";

        public bool Hostile => Type is "attack" or "attack_aoe" or "attack_status" or "debuff" or "debuff_aoe"
            or "cyber_hack";

        public static BattleSkill Parse(JToken t) => new()
        {
            Id = FocusContext.AsString(t["id"]),
            Type = FocusContext.AsString(t["type"]),
            ModuleType = FocusContext.AsString(t["module_type"]),
            Ap = FocusContext.AsInt(t["ap"]),
            Damage = FocusContext.AsInt(t["damage"]),
            Heal = FocusContext.AsInt(t["heal"]),
            Shield = FocusContext.AsInt(t["shield"]),
            RangeMin = FocusContext.AsInt(t["range_min"]),
            Range = FocusContext.AsInt(t["range"]),
            Aoe = FocusContext.AsInt(t["aoe_radius"]),
            Cooldown = FocusContext.AsInt(t["cooldown"]),
            CooldownLeft = FocusContext.AsInt(t["cooldown_left"]),
            Count = System.Math.Max(1, FocusContext.AsInt(t["count"]))
        };
    }

    /// <summary>A row of battle_ships as enriched by GetBattleFullState.</summary>
    public sealed class BattleShip
    {
        public int Id;
        public int FleetId;
        public int Team;
        public int Q;
        public int R;
        public int Hp;
        public int MaxHp;
        public int Shield;
        public int MaxShield;
        public int Ap;
        public int MaxAp;
        public int Pm;
        public int MaxPm;
        public int Initiative;
        public int BuffArmor;
        public bool Alive;
        public bool IsMine;
        public bool IsActive;
        public string Name = string.Empty;
        public readonly Dictionary<string, int> Status = new();
        public readonly List<BattleSkill> Skills = new();
        public readonly List<FocusShipModule> Modules = new();

        public int StatusTurns(string effect) => Status.TryGetValue(effect, out var v) ? v : 0;

        public static BattleShip Parse(JToken t)
        {
            var s = new BattleShip
            {
                Id = FocusContext.AsInt(t["id"]),
                FleetId = FocusContext.AsInt(t["fleet_id"]),
                Team = FocusContext.AsInt(t["team"]),
                Q = FocusContext.AsInt(t["hex_q"]),
                R = FocusContext.AsInt(t["hex_r"]),
                Hp = FocusContext.AsInt(t["hp"]),
                MaxHp = System.Math.Max(1, FocusContext.AsInt(t["max_hp"])),
                Shield = FocusContext.AsInt(t["shield_cur"]),
                MaxShield = FocusContext.AsInt(t["max_shield"]),
                Ap = FocusContext.AsInt(t["ap_left"]),
                MaxAp = FocusContext.AsInt(t["max_ap"]),
                Pm = FocusContext.AsInt(t["pm_left"]),
                MaxPm = FocusContext.AsInt(t["max_pm"]),
                Initiative = FocusContext.AsInt(t["initiative"]),
                BuffArmor = FocusContext.AsInt(t["buff_armor"]),
                Alive = FocusContext.AsBool(t["alive"]),
                IsMine = FocusContext.AsBool(t["is_mine"]),
                IsActive = FocusContext.AsBool(t["is_active"]),
                Name = FocusContext.AsString(t["fleet_name"])
            };
            if (t["status"] is JObject st)
                foreach (var p in st.Properties())
                    s.Status[p.Name] = FocusContext.AsInt(p.Value);
            if (t["skills"] is JArray sk)
                foreach (var k in sk)
                    s.Skills.Add(BattleSkill.Parse(k));
            if (t["modules"] is JArray mods)
                foreach (var m in mods)
                {
                    s.Modules.Add(new FocusShipModule
                    {
                        Id = FocusContext.AsInt(m["id"]),
                        Type = FocusContext.AsString(m["type"]),
                        GridX = m["grid_x"] == null || m["grid_x"].Type == JTokenType.Null ? -1 : FocusContext.AsInt(m["grid_x"]),
                        GridY = m["grid_y"] == null || m["grid_y"].Type == JTokenType.Null ? -1 : FocusContext.AsInt(m["grid_y"])
                    });
                }

            return s;
        }
    }

    /// <summary>One battle_actions row (GetBattleActionLog): the table replays new ones as beams.</summary>
    public sealed class BattleLogEntry
    {
        public int Id;
        public int Src;
        public int Target;
        public string Action = string.Empty;
        public JObject Result;
    }

    /// <summary>GetBattleState / BattleDoAction.state / BattleEndFleetTurn (GetBattleFullState).</summary>
    public sealed class BattleSnapshot
    {
        public const int Pending = 0;
        public const int Active = 1;
        public const int Done = -1;

        public int BattleId;
        public int State;
        public int Round = 1;
        public int ActiveId;
        public int MyTeam = -1;
        public bool MyTurn;
        public int MyFleetId;
        public long StartDeadline;
        public long TurnDeadline;
        public bool MyReady;
        public readonly List<BattleShip> Ships = new();
        public readonly List<int> Timeline = new();
        public readonly List<BattleLogEntry> Log = new();

        public BattleShip Find(int id)
        {
            foreach (var s in Ships)
                if (s.Id == id)
                    return s;
            return null;
        }

        public BattleShip At(int q, int r)
        {
            foreach (var s in Ships)
                if (s.Alive && s.Q == q && s.R == r)
                    return s;
            return null;
        }

        public BattleShip ActiveShip => Find(ActiveId);

        public static BattleSnapshot Parse(JToken root, int myFleetId)
        {
            if (root == null || root.Type != JTokenType.Object)
                return null;
            var b = root["battle"];
            var s = new BattleSnapshot
            {
                BattleId = FocusContext.AsInt(b?["id"]),
                State = FocusContext.AsInt(b?["state"]),
                Round = System.Math.Max(1, FocusContext.AsInt(root["round"])),
                ActiveId = FocusContext.AsInt(root["active_bship_id"]),
                MyTeam = root["my_team"] != null ? FocusContext.AsInt(root["my_team"]) : -1,
                MyTurn = FocusContext.AsBool(root["my_turn"]),
                MyFleetId = FocusContext.AsInt(root["my_fleet_id"]),
                StartDeadline = FocusContext.AsLong(root["start_deadline"]),
                TurnDeadline = FocusContext.AsLong(root["turn_deadline"])
            };
            if (root["ships"] is JArray ships)
                foreach (var t in ships)
                    s.Ships.Add(BattleShip.Parse(t));
            if (root["timeline"] is JArray tl)
                foreach (var t in tl)
                    s.Timeline.Add(FocusContext.AsInt(t["id"]));
            if (root["teams"] is JArray teams)
                foreach (var t in teams)
                    if (FocusContext.AsInt(t["fleetid"]) == myFleetId)
                        s.MyReady = FocusContext.AsInt(t["ready"]) == 1;
            if (root["log"] is JArray log)
                foreach (var t in log)
                {
                    JObject result = null;
                    var raw = t["result"];
                    if (raw is JObject o)
                        result = o;
                    else if (raw != null && raw.Type == JTokenType.String)
                    {
                        try
                        {
                            result = JToken.Parse((string)raw) as JObject;
                        }
                        catch
                        {
                            // Legacy rows may hold plain text.
                        }
                    }

                    s.Log.Add(new BattleLogEntry
                    {
                        Id = FocusContext.AsInt(t["id"]),
                        Src = FocusContext.AsInt(t["src_bship_id"]),
                        Target = FocusContext.AsInt(t["target_bship_id"]),
                        Action = FocusContext.AsString(t["action"]),
                        Result = result
                    });
                }

            return s;
        }

        /// <summary>Hex distance on the axial grid (model/battle.php HexDist).</summary>
        public static int Dist(int q1, int r1, int q2, int r2) =>
            (System.Math.Abs(q1 - q2) + System.Math.Abs(q1 + r1 - q2 - r2) + System.Math.Abs(r1 - r2)) / 2;

        public const int GridQ = 5;
        public const int GridR = 4;

        public static bool InGrid(int q, int r) => q >= -GridQ && q <= GridQ && r >= -GridR && r <= GridR;
    }
}
