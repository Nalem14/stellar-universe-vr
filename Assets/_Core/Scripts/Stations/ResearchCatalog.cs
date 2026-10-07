using System.Collections.Generic;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>Where a tech sits in the lab's constellation and how it glows (web scenes/research.js NODES).</summary>
    public readonly struct TechNode
    {
        public readonly string Id;
        /// <summary>Web tree layout (px): x across the branches, y down the tiers. Layout only — never rules.</summary>
        public readonly float X;
        public readonly float Y;
        public readonly Color Color;
        public readonly string CategoryKey;

        public TechNode(string id, float x, float y, int rgb, string category)
        {
            Id = id;
            X = x;
            Y = y;
            Color = new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
            CategoryKey = "researchCategory_" + category;
        }
    }

    /// <summary>What researching the next level of a tech costs and whether the server would accept it now.</summary>
    public struct ResearchQuote
    {
        public int Level;
        public int TargetLevel;
        public int Points;
        public float Seconds;
        public int MaxLevel;
        public bool AtMax;
        public bool Unlocked;
        public bool Affordable;
    }

    /// <summary>
    /// The research tree. Rules come from the server (GetConfigs.researchs: requiert, time, cost, maxLevel);
    /// the web hard-codes only the drawing (positions, colours, categories), reused here as the constellation
    /// layout. A tech the layout does not know yet still appears, in an extra column.
    /// ImproveResearch (actionjs.php): target = level + queued of that tech + 1; cost = base × target;
    /// time = base × target (± species trait / booster, server-side, so durations are estimates).
    /// </summary>
    public static class ResearchCatalog
    {
        public const string Lab = "researchLab";
        public const string Points = "researchPoints";

        static readonly TechNode[] Layout =
        {
            new("energy", 80, 30, 0xff8800, "energy"),
            new("computer", 720, 30, 0x00ccff, "calcul"),
            new("armor", 1110, 30, 0x8899cc, "structure"),
            new("weapon", 1460, 30, 0xff3333, "armement"),

            new("combustionDrive", 80, 260, 0xffaa22, "propulsion"),
            new("laser", 280, 260, 0xff4455, "armement"),
            new("solarTech", 490, 260, 0xffee00, "energy"),
            new("shield", 700, 260, 0x3388ff, "defense"),
            new("comms", 940, 260, 0x00aaff, "reseau"),
            new("nanite", 1200, 260, 0x44ffaa, "cybernetique"),

            new("impulsionDrive", 80, 490, 0xffcc33, "propulsion"),
            new("ion", 280, 490, 0xff2200, "armement"),
            new("thermodynamics", 490, 490, 0xff6600, "physique"),
            new("radarTech", 720, 490, 0x00ccff, "reseau"),
            new("drone", 940, 490, 0x22ddff, "robotique"),
            new("stargateDiscovery", 1180, 490, 0xc060f0, "structure"),

            new("colonisation", 80, 640, 0x44ff44, "expansion"),
            new("prlBond", 490, 640, 0x33e0c0, "propulsion"),
            new("stargateTriangulation", 1180, 640, 0xa040c0, "structure"),
            new("fusionDrive", 700, 620, 0xff9955, "propulsion"),
            new("hyperspaceDrive", 700, 760, 0x5566ff, "propulsion"),

            new("plasma", 280, 900, 0xff0044, "armement"),
            new("gravityTech", 700, 900, 0x9944ff, "physique"),
            new("biotech", 940, 900, 0x44ff88, "biologie"),
            // Fuel synthesis (model/fuel.php): the refinery and the Fuel Tank, late (Biotech 10, fusion 15).
            new("fuelSynthesis", 1180, 1130, 0x17c964, "biologie"),

            new("psiTech", 820, 1130, 0xcc44ff, "quantique"),
            new("spatialFolding", 760, 1360, 0x20d0e0, "megastructure")
        };

        public const float LayoutWidth = 1460f;
        public const float LayoutHeight = 1360f;

        static readonly List<TechNode> Nodes = new();
        static JObject _builtFrom;

        /// <summary>Every tech the server knows, placed (layout first, unknown ones in a column past the right edge).</summary>
        public static IReadOnlyList<TechNode> All()
        {
            if (_builtFrom == GameConfig.Research && Nodes.Count > 0)
                return Nodes;
            _builtFrom = GameConfig.Research;
            Nodes.Clear();
            var known = new HashSet<string>();
            foreach (var n in Layout)
            {
                if (GameConfig.Research != null && GameConfig.Research[n.Id] == null)
                    continue;
                Nodes.Add(n);
                known.Add(n.Id);
            }

            if (GameConfig.Research != null)
            {
                var extra = 0;
                foreach (var p in GameConfig.Research.Properties())
                {
                    if (known.Contains(p.Name))
                        continue;
                    Nodes.Add(new TechNode(p.Name, LayoutWidth + 200f, 30f + extra * 230f, 0x9fb8c8, "structure"));
                    extra++;
                }
            }

            return Nodes;
        }

        public static JObject Config(string tech) => GameConfig.Research?[tech] as JObject;

        static readonly Dictionary<string, List<ResearchEffect>> EffectCache = new();
        static JObject _effectsFrom;
        static float _effectsPrl;
        static float _effectsScan;

        /// <summary>
        /// What each level of a tech gives (GetConfigs.researchEffects), plus the ranges the server scales on the
        /// level (prlBond: Bond PRL reach, radarTech: scanner reach). Empty when the server does not serve them.
        /// </summary>
        public static IReadOnlyList<ResearchEffect> Effects(string tech)
        {
            if (string.IsNullOrEmpty(tech))
                return System.Array.Empty<ResearchEffect>();
            if (_effectsFrom != GameConfig.ResearchEffects || _effectsPrl != GameConfig.PrlRangePerLevel ||
                _effectsScan != GameConfig.ScannerRangePerLevel)
            {
                EffectCache.Clear();
                _effectsFrom = GameConfig.ResearchEffects;
                _effectsPrl = GameConfig.PrlRangePerLevel;
                _effectsScan = GameConfig.ScannerRangePerLevel;
            }

            if (EffectCache.TryGetValue(tech, out var cached))
                return cached;
            var list = new List<ResearchEffect>();
            if (GameConfig.ResearchEffects?[tech] is JArray arr)
                foreach (var e in arr)
                {
                    var stat = FocusContext.AsString(e["stat"]);
                    if (string.IsNullOrEmpty(stat))
                        continue;
                    var mods = new List<string>();
                    if (e["modules"] is JArray m)
                        foreach (var x in m)
                            mods.Add(FocusContext.AsString(x));
                    list.Add(new ResearchEffect(stat, FocusContext.AsFloat(e["perLevel"]), FocusContext.AsBool(e["flat"]),
                        e["cap"] != null, FocusContext.AsFloat(e["cap"]), mods.ToArray()));
                }

            if (tech == "prlBond" && GameConfig.PrlRangePerLevel > 0f)
                list.Add(new ResearchEffect("prlRange", GameConfig.PrlRangePerLevel, true, false, 0f, null));
            if (tech == "radarTech" && GameConfig.ScannerRangePerLevel > 0f)
                list.Add(new ResearchEffect("scannerRange", GameConfig.ScannerRangePerLevel, true, false, 0f, null));
            EffectCache[tech] = list;
            return list;
        }

        /// <summary>
        /// The empire-wide research bonus on one stat (effects with no module list), as the server sums it: every
        /// tech's level × perLevel, held to its cap; a tech still being researched counts one level less.
        /// </summary>
        public static float StatBonus(string stat)
        {
            var empire = AuthManager.Ensure().Empire;
            if (empire == null || GameConfig.ResearchEffects == null)
                return 0f;
            var running = FocusContext.AsLong(empire["working"]) > Core.Vfx.FleetOrderGate.UnixNow()
                ? FocusContext.AsString(empire["workingtype"])
                : null;
            var total = 0f;
            foreach (var p in GameConfig.ResearchEffects.Properties())
            {
                var level = FocusContext.AsInt(empire[p.Name]) - (running == p.Name ? 1 : 0);
                if (level <= 0)
                    continue;
                foreach (var e in Effects(p.Name))
                    if (e.Stat == stat && e.Modules.Length == 0)
                        total += e.Gain(level);
            }

            return total;
        }

        /// <summary>The server serves the effects table (else the lab falls back on the native descriptions).</summary>
        public static bool HasEffects => GameConfig.ResearchEffects != null;

        /// <summary>
        /// The effect text key, "desc" + Tech. The native dump spells one differently from the tech id
        /// (colonisation → descColonization): that native key is used as is rather than a duplicate.
        /// </summary>
        public static string DescKey(string tech) =>
            string.IsNullOrEmpty(tech) ? string.Empty
            : tech == "colonisation" ? "descColonization"
            : "desc" + char.ToUpperInvariant(tech[0]) + tech.Substring(1);

        public static bool TryNode(string tech, out TechNode node)
        {
            foreach (var n in All())
                if (n.Id == tech)
                {
                    node = n;
                    return true;
                }

            node = default;
            return false;
        }

        /// <summary>Requirements (requiert): researchLab is a planet building level, the rest empire techs.</summary>
        public static IEnumerable<(string key, int level)> Requirements(string tech)
        {
            if (!(Config(tech)?["requiert"] is JObject req))
                yield break;
            foreach (var r in req.Properties())
                yield return (r.Name, FocusContext.AsInt(r.Value));
        }

        /// <summary>Tech prerequisites only (the links drawn in the constellation).</summary>
        public static IEnumerable<(string tech, int level)> Parents(string tech)
        {
            foreach (var (key, level) in Requirements(tech))
                if (key != Lab)
                    yield return (key, level);
        }

        public static int MaxLevel(string tech)
        {
            var cfg = Config(tech);
            var max = FocusContext.AsInt(cfg?["maxLevel"]);
            // Web fallback: stargateDiscovery is a one-shot even when the config omits it.
            return max > 0 ? max : tech == "stargateDiscovery" ? 1 : 0;
        }

        /// <summary>A permanent bonus a research gives at a milestone level (GetConfigs.researchs[tech].masteries).</summary>
        public readonly struct Mastery
        {
            public readonly int Level;
            public readonly string Stat;
            public readonly float Value;

            public Mastery(int level, string stat, float value)
            {
                Level = level;
                Stat = stat;
                Value = value;
            }

            /// <summary>"+5 % de dégâts" — native masteryStat_* key with the signed value.</summary>
            public string Label()
            {
                var n = Mathf.Approximately(Value, Mathf.Round(Value))
                    ? Mathf.RoundToInt(Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                return Trans.Format("masteryStat_" + Stat, (Value > 0f ? "+" : Value < 0f ? "" : "") + n.Replace("-", "−"));
            }
        }

        /// <summary>Every mastery step of a research, by level (an "every" mastery expanded up to its upTo / max / 50).</summary>
        public static List<Mastery> Masteries(string tech)
        {
            var list = new List<Mastery>();
            if (Config(tech)?["masteries"] is not JArray arr)
                return list;
            var max = MaxLevel(tech);
            foreach (var t in arr)
            {
                if (t is not JObject m)
                    continue;
                var stat = FocusContext.AsString(m["stat"]);
                var value = FocusContext.AsFloat(m["value"]);
                if (m["at"] is JArray at)
                {
                    foreach (var l in at)
                        list.Add(new Mastery(FocusContext.AsInt(l), stat, value));
                    continue;
                }

                var every = FocusContext.AsInt(m["every"]);
                if (every <= 0)
                    continue;
                var upTo = FocusContext.AsInt(m["upTo"]);
                if (upTo <= 0)
                    upTo = max > 0 ? max : 50;
                for (var l = every; l <= upTo; l += every)
                    list.Add(new Mastery(l, stat, value));
            }

            list.Sort((a, b) => a.Level.CompareTo(b.Level));
            return list;
        }

        /// <summary>What our masteries add to an empire stat (mirror of ResearchMasteryGain, model/research.php).</summary>
        public static float MasteryGain(EconomyService eco, string stat)
        {
            if (eco == null || GameConfig.Research == null)
                return 0f;
            var gain = 0f;
            foreach (var p in GameConfig.Research.Properties())
            {
                var level = eco.ResearchLevel(p.Name);
                if (level <= 0 || p.Value?["masteries"] == null)
                    continue;
                foreach (var m in Masteries(p.Name))
                    if (m.Stat == stat && level >= m.Level)
                        gain += m.Value;
            }

            return gain;
        }

        /// <summary>Levels between two milestone refunds (0 = none on this server).</summary>
        public static int MilestoneStep => FocusContext.AsInt(GameConfig.ResearchMilestone?["step"]);

        public static float MilestoneRefundShare => FocusContext.AsFloat(GameConfig.ResearchMilestone?["refundShare"]);

        /// <summary>Points a milestone level pays back when it starts (0 when the level is no milestone).</summary>
        public static int MilestoneRefund(string tech, int level)
        {
            var step = MilestoneStep;
            if (step <= 0 || level <= 0 || level % step != 0)
                return 0;
            var basePoints = FocusContext.AsInt((Config(tech)?["cost"] as JObject)?[Points]);
            return Mathf.FloorToInt(basePoints * level * MilestoneRefundShare);
        }

        /// <summary>Best researchLab level among our planets — the lab ImproveResearch is sent to (web onImprove).</summary>
        public static int BestLab(EconomyService eco, out int planetId)
        {
            planetId = 0;
            var best = 0;
            if (eco == null)
                return 0;
            foreach (var p in eco.Planets.Values)
            {
                if (!OwnedPlanets.Contains(p.Id))
                    continue;
                var lab = p.Level(Lab);
                if (lab > best || planetId == 0 && lab > 0)
                {
                    best = lab;
                    planetId = p.Id;
                }
            }

            return best;
        }

        /// <summary>
        /// The next level as the server will charge it. <paramref name="queuedOfTech"/> = rows of this tech
        /// already in empire_research_queue; <paramref name="running"/> = it is the tech in progress (its
        /// level is already stored server-side, so the next order targets one more).
        /// </summary>
        public static ResearchQuote Quote(EconomyService eco, string tech, int level, int queuedOfTech, bool running,
            int labLevel, int points)
        {
            var q = new ResearchQuote { Level = level, MaxLevel = MaxLevel(tech) };
            q.TargetLevel = level + (running ? 1 : 0) + queuedOfTech + 1;
            q.AtMax = q.MaxLevel > 0 && q.TargetLevel > q.MaxLevel;
            var cfg = Config(tech);
            // Psi mastery lowers every research's price, as ImproveResearch charges it.
            var costFactor = Mathf.Max(0.1f, 1f + MasteryGain(eco, "researchCost") / 100f);
            if (cfg?["cost"] is JObject cost)
                q.Points = Mathf.CeilToInt(FocusContext.AsInt(cost[Points]) * q.TargetLevel * costFactor);
            q.Seconds = Mathf.Max(5f, FocusContext.AsFloat(cfg?["time"]) * q.TargetLevel);
            q.Unlocked = true;
            foreach (var (key, need) in Requirements(tech))
            {
                var have = key == Lab ? labLevel : eco?.ResearchLevel(key) ?? 0;
                if (have < need)
                    q.Unlocked = false;
            }

            q.Affordable = points >= q.Points;
            return q;
        }

        public const string KindBuilding = "vr.research.kind.building";
        public const string KindFeature = "vr.research.kind.feature";
        public const string KindModule = "vr.research.kind.module";
        public const string KindDefense = "vr.research.kind.defense";
        public const string KindTroop = "vr.research.kind.troop";
        public const string KindResearch = "vr.research.kind.research";

        /// <summary>
        /// What a module or a building lets you do, as the server decides it: the fleet order or planet
        /// action that checks for it (actionjs.php / Helper.php GetFleetStats has* flags). Names stay native keys.
        /// Explore is left out: the ScienceModule has no research gate, every empire starts with it.
        /// </summary>
        static readonly (string source, string featureKey)[] FeatureOf =
        {
            ("HyperspaceDrive", "hyperspaceJump"), // hyperspace moves (hasEnoughHyperdrive)
            ("BondPRLModule", "bondPrlJump"), // PrlBondFleetToSystem (hasPrlBond)
            ("colonyShip", "colonizePlanet"), // Colonize
            ("StealthFieldGenerator", "battleSkill_stealth_veil"), // hasStealth
            ("AdvancedHackingMatrix", "battleSkill_hack"), // hasHacking
            ("HackingModule", "battleSkill_hack"),
            ("stargate", "stargateFeature"), // DispatchStargateMission
            ("jumpgate", "vr.research.feature.jumpgate"), // SendFleetToJumpgate
            ("academy", "vr.research.feature.troops"), // RecruitTroop (troopstats requiert academy)
            ("defenseFactory", "vr.research.feature.defenses") // BuildDefenseUnit (defensestats requiert defenseFactory)
        };

        static readonly Dictionary<string, List<ResearchUnlock>> UnlockCache = new();
        static JObject _unlocksResearch;
        static JObject _unlocksShips;
        static JObject _unlocksDefenses;
        static JObject _unlocksTroops;

        /// <summary>
        /// Everything the tech opens, level by level, read from the server configs (web research.js
        /// unlocksForTech): buildings gated on it (UpgradeBuilding), ship modules / defenses / troops whose
        /// requiert names it, techs it leads to, and the features those unlocks bring (hyperspace, PRL bond,
        /// colonisation, gates…). Cached per configs read; the lab builds its text on selection only.
        /// </summary>
        public static IReadOnlyList<ResearchUnlock> Unlocks(string tech)
        {
            if (string.IsNullOrEmpty(tech))
                return System.Array.Empty<ResearchUnlock>();
            if (_unlocksResearch != GameConfig.Research || _unlocksShips != GameConfig.ShipStats ||
                _unlocksDefenses != GameConfig.DefenseStats || _unlocksTroops != GameConfig.TroopStats)
            {
                UnlockCache.Clear();
                _unlocksResearch = GameConfig.Research;
                _unlocksShips = GameConfig.ShipStats;
                _unlocksDefenses = GameConfig.DefenseStats;
                _unlocksTroops = GameConfig.TroopStats;
            }

            if (UnlockCache.TryGetValue(tech, out var cached))
                return cached;

            var list = new List<ResearchUnlock>();
            foreach (var def in BuildingCatalog.All)
                if (def.ResearchKey == tech)
                {
                    list.Add(new ResearchUnlock(def.Type, def.ResearchLevel, KindBuilding));
                    AddFeature(def.Type, def.ResearchLevel, list);
                }

            Collect(GameConfig.ShipStats, tech, KindModule, list, true);
            Collect(GameConfig.DefenseStats, tech, KindDefense, list, false);
            Collect(GameConfig.TroopStats, tech, KindTroop, list, false);

            // Effects the server scales on the level itself.
            if (tech == "prlBond" && GameConfig.PrlBaseRange > 0f && GameConfig.PrlRangePerLevel > 0f)
                list.Add(new ResearchUnlock("vr.research.feature.prlRange", 1, KindFeature,
                    Mathf.RoundToInt(GameConfig.PrlRangePerLevel / GameConfig.PrlBaseRange * 100f).ToString()));
            // Each level widens what a scanner-equipped ship sees (GetVisibleFleetsForUser, web 5021101).
            if (tech == "radarTech" && GameConfig.ScannerRangePerLevel > 0f)
                list.Add(new ResearchUnlock("vr.research.feature.radarRange", 1, KindFeature,
                    Mathf.RoundToInt(GameConfig.ScannerRangePerLevel).ToString()));
            // The refinery is no building of the catalogue: a room of its own on every owned world (model/fuel.php).
            if (tech == "fuelSynthesis")
                list.Add(new ResearchUnlock("fuelRefinery", 1, KindBuilding));
            if (tech == "stargateTriangulation") // ImproveResearch → GrantStargateTriangulationDiscovery
                list.Add(new ResearchUnlock("vr.research.feature.triangulation", 1, KindFeature));

            Collect(GameConfig.Research, tech, KindResearch, list, false);

            // By level, then kind (what you can do first, then what you can build, then where it leads).
            var order = new List<(ResearchUnlock u, int i)>(list.Count);
            for (var i = 0; i < list.Count; i++)
                order.Add((list[i], i));
            order.Sort((a, b) =>
            {
                var c = a.u.Level.CompareTo(b.u.Level);
                if (c == 0)
                    c = KindRank(a.u.KindKey).CompareTo(KindRank(b.u.KindKey));
                return c != 0 ? c : a.i.CompareTo(b.i);
            });
            list.Clear();
            foreach (var (u, _) in order)
                list.Add(u);

            UnlockCache[tech] = list;
            return list;
        }

        static int KindRank(string kind) => kind switch
        {
            KindFeature => 0,
            KindBuilding => 1,
            KindModule => 2,
            KindDefense => 3,
            KindTroop => 4,
            _ => 5
        };

        static void AddFeature(string source, int level, List<ResearchUnlock> into)
        {
            foreach (var (src, key) in FeatureOf)
            {
                if (!string.Equals(src, source, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                // Two modules bringing the same feature (hacking): keep the earliest level.
                for (var i = 0; i < into.Count; i++)
                    if (into[i].KindKey == KindFeature && into[i].Key == key)
                    {
                        if (level < into[i].Level)
                            into[i] = new ResearchUnlock(key, level, KindFeature);
                        return;
                    }

                into.Add(new ResearchUnlock(key, level, KindFeature));
                return;
            }
        }

        static void Collect(JObject stats, string tech, string kindKey, List<ResearchUnlock> into, bool features)
        {
            if (stats == null)
                return;
            foreach (var p in stats.Properties())
            {
                if (!(p.Value?["requiert"] is JObject req) || req[tech] == null)
                    continue;
                var level = FocusContext.AsInt(req[tech]);
                into.Add(new ResearchUnlock(p.Name, level, kindKey));
                if (features)
                    AddFeature(p.Name, level, into);
            }
        }
    }

    /// <summary>
    /// One effect of a research level (server $RESEARCH_EFFECTS, GetConfigs.researchEffects): a stat, a percent per
    /// level (or an absolute amount when <see cref="Flat"/>), held to <see cref="Cap"/>, on some module types only
    /// (none = every module, or an empire-wide bonus).
    /// </summary>
    public readonly struct ResearchEffect
    {
        public readonly string Stat;
        public readonly float PerLevel;
        public readonly bool Flat;
        public readonly bool HasCap;
        public readonly float Cap;
        public readonly string[] Modules;

        public ResearchEffect(string stat, float perLevel, bool flat, bool hasCap, float cap, string[] modules)
        {
            Stat = stat;
            PerLevel = perLevel;
            Flat = flat;
            HasCap = hasCap;
            Cap = cap;
            Modules = modules ?? System.Array.Empty<string>();
        }

        /// <summary>What <paramref name="level"/> levels give, held to the cap (as the server does).</summary>
        public float Gain(int level)
        {
            var g = Mathf.Max(0, level) * PerLevel;
            if (HasCap)
                g = Cap < 0f ? Mathf.Max(g, Cap) : Mathf.Min(g, Cap);
            return g;
        }

        /// <summary>"+40 %", "−15 %", "+300".</summary>
        public string Amount(float gain)
        {
            var abs = Mathf.Abs(gain);
            var n = Mathf.Approximately(abs, Mathf.Round(abs))
                ? Mathf.RoundToInt(abs).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : abs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return (gain < 0f ? "−" : "+") + n + (Flat ? string.Empty : " %");
        }

        /// <summary>Grouping key: same stat on the same modules sums up on the bonus wall.</summary>
        public string GroupKey => Stat + "|" + string.Join(",", Modules);

        /// <summary>The sentence of this effect with an amount: "+40 % speed — Fusion thruster".</summary>
        public string Describe(string amount)
        {
            var on = Modules.Length == 0 ? Trans.Get("vr.research.allModules") : ModuleNames();
            return Trans.Format("vr.research.effect." + Stat, amount, on);
        }

        string ModuleNames()
        {
            var names = new string[Modules.Length];
            for (var i = 0; i < Modules.Length; i++)
                names[i] = Trans.Get(Modules[i]);
            return string.Join(", ", names);
        }
    }

    /// <summary>One thing a tech opens: a name key (or a feature text key with its argument) at a level.</summary>
    public readonly struct ResearchUnlock
    {
        public readonly string Key;
        public readonly int Level;
        public readonly string KindKey;
        /// <summary>Format argument for a feature text ({0}); null for a plain name.</summary>
        public readonly string Arg;

        public ResearchUnlock(string key, int level, string kindKey, string arg = null)
        {
            Key = key;
            Level = level;
            KindKey = kindKey;
            Arg = arg;
        }

        public string Label => Arg != null ? Trans.Format(Key, Arg) : Trans.Get(Key);
    }
}
