using System.Collections.Generic;
using Core.App;
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

        public static string DescKey(string tech) =>
            string.IsNullOrEmpty(tech) ? string.Empty : "desc" + char.ToUpperInvariant(tech[0]) + tech.Substring(1);

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
            if (cfg?["cost"] is JObject cost)
                q.Points = FocusContext.AsInt(cost[Points]) * q.TargetLevel;
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

        /// <summary>
        /// What the tech opens, read from the server configs instead of the web's hard-coded list: ship modules,
        /// troops and defenses whose requiert names it, and buildings gated on it (UpgradeBuilding).
        /// </summary>
        public static List<(string key, int level, string kindKey)> Unlocks(string tech)
        {
            var list = new List<(string, int, string)>();
            foreach (var def in BuildingCatalog.All)
                if (def.ResearchKey == tech)
                    list.Add((def.Type, def.ResearchLevel, "vr.research.kind.building"));
            Collect(GameConfig.ShipStats, tech, "vr.research.kind.module", list);
            Collect(GameConfig.DefenseStats, tech, "vr.research.kind.defense", list);
            Collect(GameConfig.TroopStats, tech, "vr.research.kind.troop", list);
            return list;
        }

        static void Collect(JObject stats, string tech, string kindKey, List<(string, int, string)> into)
        {
            if (stats == null)
                return;
            foreach (var p in stats.Properties())
            {
                if (!(p.Value?["requiert"] is JObject req) || req[tech] == null)
                    continue;
                into.Add((p.Name, FocusContext.AsInt(req[tech]), kindKey));
            }
        }
    }
}
