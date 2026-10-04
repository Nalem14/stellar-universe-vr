using System.Collections.Generic;
using Core.App;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Stations
{
    public enum ModuleFamily
    {
        Core,
        Weapon,
        Engine,
        Defense,
        Cargo,
        Troop,
        Colony,
        Science,
        Special,
        Life
    }

    /// <summary>Summed design stats, as the web ShipBuilderUI previews them (config values, no research bonus).</summary>
    public struct DesignStats
    {
        public int Modules;
        public float Armor;
        public float Shield;
        public float Damage;
        public float Speed;
        public float Cargo;
        public float TroopCargo;
        public int Hyperdrives;
        public int PrlBonds;
        /// <summary>Jump modules the server requires for this many modules (⌈modules / modulesPerJumpModule⌉).</summary>
        public int JumpRequired;
    }

    /// <summary>Which hulls may carry a module (web moduleShapes.getModuleCompatibility, 600347f).</summary>
    public readonly struct ModuleCompat
    {
        public readonly bool Ship;
        public readonly bool Station;

        public ModuleCompat(bool ship, bool station)
        {
            Ship = ship;
            Station = station;
        }

        public bool Fits(bool stationHull) => stationHull ? Station : Ship;
    }

    /// <summary>
    /// Ship modules from GetConfigs.shipstats (server $SHIPSTATS). One cell each on the 9×9 grid; the core
    /// (ShipCore, or StationCore for an orbital fortress) is set at (4,4) by AddToFleet and never moves; every
    /// other module must touch the structure (web ShipBuilderUI _adj).
    /// Names: native key = module type; descriptions: "desc" + Type (fr.json descShipCore…).
    /// </summary>
    public static class ModuleCatalog
    {
        public const string Core = "ShipCore";
        public const string StationCore = "StationCore";

        /// <summary>Modules only an orbital fortress may carry (server PlaceShipModule moduleOnlyForStations).</summary>
        static readonly HashSet<string> StationOnlyTypes = new()
        {
            "StationCore", "OrbitalDefenseBattery", "PlanetaryShieldProjector", "OrbitalJammingArray",
            "OrbitalGantry", "CitadelReactor"
        };

        /// <summary>Propulsion a fortress refuses (server stationCannotEquipPropulsion list; speed &gt; 0 too).</summary>
        static readonly HashSet<string> PropulsionTypes = new()
        {
            "SpeedBooster", "CombustionThruster", "ImpulsionThruster", "FusionThruster", "HyperspaceDrive",
            "BondPRLModule", "JumpgateDrive"
        };

        /// <summary>Ship-only hull starters (web SHIP_ONLY_CORES).</summary>
        static readonly HashSet<string> ShipOnlyTypes = new() { "ShipCore", "colonyShip", "ColonyShip" };

        /// <summary>A hull core: placed by AddToFleet at (4,4), never by hand (cannotPlaceCore), never removed.</summary>
        public static bool IsCore(string type) => type == Core || type == StationCore;

        public static bool IsStationOnly(string type) => type != null && StationOnlyTypes.Contains(type);

        public static bool IsPropulsion(string type)
        {
            if (string.IsNullOrEmpty(type))
                return false;
            return PropulsionTypes.Contains(type) || FocusContext.AsFloat(Stats(type)?["speed"]) > 0f;
        }

        public static ModuleCompat Compat(string type)
        {
            if (IsStationOnly(type))
                return new ModuleCompat(false, true);
            if ((type != null && ShipOnlyTypes.Contains(type)) || IsPropulsion(type))
                return new ModuleCompat(true, false);
            return new ModuleCompat(true, true);
        }

        public static readonly Color ShipTagColor = new(0.22f, 0.74f, 0.97f, 1f);
        public static readonly Color StationTagColor = new(0.98f, 0.75f, 0.14f, 1f);

        /// <summary>Rich-text compatibility tags (web 🚀 SHIP / 🛰 STATION pills): cyan ship, amber station.</summary>
        public static string CompatTags(string type)
        {
            var c = Compat(type);
            var sb = new System.Text.StringBuilder(96);
            if (c.Ship)
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ShipTagColor)).Append("><size=75%>[")
                    .Append(Trans.Get("vr.module.compatShip")).Append("]</size></color>");
            if (c.Station)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(StationTagColor)).Append("><size=75%>[")
                    .Append(Trans.Get("vr.module.compatStation")).Append("]</size></color>");
            }

            return sb.ToString();
        }

        /// <summary>Can this hangar module be set on that hull's grid by hand (never a core)?</summary>
        public static bool Placeable(string type, bool stationHull) => !IsCore(type) && Compat(type).Fits(stationHull);
        public const int Grid = 9;
        public const int CoreCell = 4;

        public static JObject Stats(string type) => GameConfig.ShipStats?[type] as JObject;

        public static IEnumerable<string> Types()
        {
            if (GameConfig.ShipStats == null)
                yield break;
            foreach (var p in GameConfig.ShipStats.Properties())
                yield return p.Name;
        }

        public static string NameKey(string type) => type;

        public static string DescKey(string type) =>
            "desc" + (string.IsNullOrEmpty(type) ? string.Empty : char.ToUpperInvariant(type[0]) + type.Substring(1));

        public static ModuleFamily Family(string type)
        {
            if (string.IsNullOrEmpty(type))
                return ModuleFamily.Life;
            switch (type)
            {
                // Fortress modules first: their names would fall to the wrong family below
                // (battery / reactor read as Defense, the core as Life). Web moduleShapes categories.
                case StationCore: return ModuleFamily.Core;
                case "OrbitalDefenseBattery": return ModuleFamily.Weapon;
                case "PlanetaryShieldProjector": return ModuleFamily.Defense;
                case "OrbitalJammingArray": return ModuleFamily.Special;
                case "OrbitalGantry": return ModuleFamily.Special;
                case "CitadelReactor": return ModuleFamily.Defense;
            }

            var t = type.ToLowerInvariant();
            if (t == "shipcore") return ModuleFamily.Core;
            if (t.Contains("colony")) return ModuleFamily.Colony;
            if (t.Contains("troop")) return ModuleFamily.Troop;
            if (t.Contains("cannon") || t.Contains("turret") || t.Contains("dronebay") || t.Contains("iem"))
                return ModuleFamily.Weapon;
            if (t.Contains("thruster") || t.Contains("booster") || t.Contains("drive") || t.Contains("prl"))
                return ModuleFamily.Engine;
            if (t.Contains("shield") || t.Contains("armor") || t.Contains("battery") || t.Contains("solar") ||
                t.Contains("reactor"))
                return ModuleFamily.Defense;
            if (t.Contains("cargo") || t.Contains("bay") || t.Contains("kitchen") || t.Contains("toilet"))
                return ModuleFamily.Cargo;
            if (t.Contains("science") || t.Contains("scanner") || t.Contains("researchlab"))
                return ModuleFamily.Science;
            if (t.Contains("hack") || t.Contains("stealth") || t.Contains("teleport") || t.Contains("mind") ||
                t.Contains("communication") || t.Contains("repair"))
                return ModuleFamily.Special;
            return ModuleFamily.Life;
        }

        public static Color Accent(ModuleFamily f) => f switch
        {
            ModuleFamily.Core => new Color(1f, 0.8f, 0.3f, 1f),
            ModuleFamily.Weapon => new Color(1f, 0.38f, 0.32f, 1f),
            ModuleFamily.Engine => new Color(0.35f, 0.8f, 1f, 1f),
            ModuleFamily.Defense => new Color(0.45f, 0.95f, 0.6f, 1f),
            ModuleFamily.Cargo => new Color(0.85f, 0.65f, 0.4f, 1f),
            ModuleFamily.Troop => new Color(0.5f, 0.6f, 1f, 1f),
            ModuleFamily.Colony => new Color(0.4f, 1f, 0.85f, 1f),
            ModuleFamily.Science => new Color(0.7f, 0.5f, 1f, 1f),
            ModuleFamily.Special => new Color(1f, 0.45f, 0.9f, 1f),
            _ => new Color(0.75f, 0.82f, 0.88f, 1f)
        };

        public static string FamilyKey(ModuleFamily f) => "vr.module.family." + f.ToString().ToLowerInvariant();

        // ── Per-module stats (web ShipBuilderUI _statsLine: only the non-zero ones) ─────────────────

        /// <summary>
        /// shipstats field → label key (native web keys: armor / shield / damage / speed / cargo / crystalUsage;
        /// the dock's own vr.dock.troops), colour (combat warm, defence cool, logistics earthy)
        /// and whether the value adds to the hull (speed reads "+n", as the web builder shows it).
        /// </summary>
        static readonly (string Field, string Key, string Hex, bool Plus)[] StatFields =
        {
            ("damage", "damage", "ff6a5a", false),
            ("armor", "armor", "ffb04a", false),
            ("shield", "shield", "5ad8ff", false),
            ("speed", "speed", "8fb4ff", true),
            ("cargo", "cargo", "e0b070", false),
            ("troopCargo", "vr.dock.troops", "9aa4ff", false),
            ("crystalUsage", "crystalUsage", "d78cff", false)
        };

        static readonly Dictionary<string, string> StatsCache = new();
        static readonly System.Text.StringBuilder StatsSb = new(160);
        static JObject _statsSource;
        static string _statsLang;
        static bool _statsReady;
        static JToken _statsFactors;

        /// <summary>
        /// What our empire makes of a module stat (GetMeEmpire.moduleFactors: research, species, politics — exactly
        /// GetShipStats' multiplier), so readouts show the real figure, not the catalogue's. 1 before it is read.
        /// </summary>
        public static float Factor(string type, string stat)
        {
            var f = AuthManager.Ensure().Empire?["moduleFactors"]?[type]?[stat];
            var v = f != null ? FocusContext.AsFloat(f) : 1f;
            return v > 0f ? v : 1f;
        }

        static bool Scaled(string field) => field is "damage" or "armor" or "shield" or "speed";

        /// <summary>
        /// "Dégâts 2 000 · Taille de coque 5 · Cristal utilisé 12": every non-zero shipstats figure of one module
        /// (damage / armor / shield / speed as our empire really gets them: <see cref="Factor"/>), each tinted by its kind. Empty when the
        /// module has none. Cached per type; rebuilt when the config, the language or the dump changes.
        /// </summary>
        public static string StatsLine(string type)
        {
            if (string.IsNullOrEmpty(type))
                return string.Empty;
            var factors = AuthManager.Ensure().Empire?["moduleFactors"];
            if (!ReferenceEquals(_statsSource, GameConfig.ShipStats) || _statsLang != Trans.Lang ||
                _statsReady != Trans.IsReady || !ReferenceEquals(_statsFactors, factors))
            {
                StatsCache.Clear();
                _statsFactors = factors;
                _statsSource = GameConfig.ShipStats;
                _statsLang = Trans.Lang;
                _statsReady = Trans.IsReady;
            }

            if (StatsCache.TryGetValue(type, out var hit))
                return hit;
            var st = Stats(type);
            StatsSb.Clear();
            if (st != null)
            {
                var culture = StatCulture();
                foreach (var (field, key, hex, plus) in StatFields)
                {
                    var v = FocusContext.AsFloat(st[field]);
                    if (Scaled(field))
                        v *= Factor(type, field);
                    if (Mathf.Approximately(v, 0f))
                        continue;
                    if (StatsSb.Length > 0)
                        StatsSb.Append("  <color=#5d7c8c>·</color>  ");
                    StatsSb.Append("<color=#").Append(hex).Append('>').Append(Trans.Get(key)).Append(" <b>");
                    if (plus && v > 0f)
                        StatsSb.Append('+');
                    StatsSb.Append(Mathf.Abs(v - Mathf.Round(v)) < 0.01f ? Mathf.RoundToInt(v).ToString("N0", culture)
                        : v.ToString("0.##", culture)).Append("</b></color>");
                }
            }

            var line = StatsSb.ToString();
            StatsCache[type] = line;
            return line;
        }

        /// <summary>Description of one module (fr.json descShipCore…), as the dock's status line shows it.</summary>
        public static string Description(string type) => Trans.Get(DescKey(type));

        static System.Globalization.CultureInfo StatCulture()
        {
            try
            {
                return System.Globalization.CultureInfo.GetCultureInfo(Trans.Lang);
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                return System.Globalization.CultureInfo.InvariantCulture;
            }
        }

        /// <summary>4-neighbour adjacency to an occupied cell (the web builder's placement rule).</summary>
        public static bool CanPlace(bool[,] occupied, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Grid || y >= Grid || occupied[x, y])
                return false;
            return Occ(occupied, x - 1, y) || Occ(occupied, x + 1, y) || Occ(occupied, x, y - 1) || Occ(occupied, x, y + 1);
        }

        /// <summary>
        /// Removing (x,y) must not orphan modules from the core (the server does not check it; the web leaves
        /// islands behind — the VR keeps the ship in one piece, as pirate layouts are).
        /// </summary>
        public static bool KeepsConnected(bool[,] occupied, int x, int y)
        {
            var copy = (bool[,])occupied.Clone();
            copy[x, y] = false;
            var seen = new bool[Grid, Grid];
            var stack = new Stack<(int, int)>();
            if (!copy[CoreCell, CoreCell])
                return false;
            stack.Push((CoreCell, CoreCell));
            seen[CoreCell, CoreCell] = true;
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
                {
                    if (!Occ(copy, nx, ny) || seen[nx, ny])
                        continue;
                    seen[nx, ny] = true;
                    stack.Push((nx, ny));
                }
            }

            for (var i = 0; i < Grid; i++)
            for (var j = 0; j < Grid; j++)
                if (copy[i, j] && !seen[i, j])
                    return false;
            return true;
        }

        static bool Occ(bool[,] o, int x, int y) => x >= 0 && y >= 0 && x < Grid && y < Grid && o[x, y];

        public static DesignStats Sum(IReadOnlyList<FocusShipModule> modules)
        {
            var s = new DesignStats();
            if (modules == null)
                return s;
            foreach (var m in modules)
            {
                if (m == null || !m.OnGrid)
                    continue;
                s.Modules++;
                var st = Stats(m.Type);
                s.Armor += FocusContext.AsFloat(st?["armor"]) * Factor(m.Type, "armor");
                s.Shield += FocusContext.AsFloat(st?["shield"]) * Factor(m.Type, "shield");
                s.Damage += FocusContext.AsFloat(st?["damage"]) * Factor(m.Type, "damage");
                s.Speed += FocusContext.AsFloat(st?["speed"]) * Factor(m.Type, "speed");
                s.Cargo += FocusContext.AsFloat(st?["cargo"]);
                s.TroopCargo += FocusContext.AsFloat(st?["troopCargo"]);
                if (m.Type == "HyperspaceDrive")
                    s.Hyperdrives++;
                if (m.Type == "BondPRLModule")
                    s.PrlBonds++;
            }

            var per = FocusContext.AsInt(GameConfig.JumpModuleRequirement?["modulesPerJumpModule"]);
            s.JumpRequired = per > 0 ? Mathf.CeilToInt(s.Modules / (float)per) : 0;
            return s;
        }
    }
}
