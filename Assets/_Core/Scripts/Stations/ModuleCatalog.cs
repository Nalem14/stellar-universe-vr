using System.Collections.Generic;
using Core.App;
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
        public float Size;
        public int Hyperdrives;
        public int PrlBonds;
        /// <summary>Jump modules the server requires for this many modules (⌈modules / modulesPerJumpModule⌉).</summary>
        public int JumpRequired;
    }

    /// <summary>
    /// Ship modules from GetConfigs.shipstats (server $SHIPSTATS). One cell each on the 9×9 grid; ShipCore
    /// sits at (4,4) and cannot leave; every other module must touch the structure (web ShipBuilderUI _adj).
    /// Names: native key = module type; descriptions: "desc" + Type (fr.json descShipCore…).
    /// </summary>
    public static class ModuleCatalog
    {
        public const string Core = "ShipCore";
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
                s.Armor += FocusContext.AsFloat(st?["armor"]);
                s.Shield += FocusContext.AsFloat(st?["shield"]);
                s.Damage += FocusContext.AsFloat(st?["damage"]);
                s.Speed += FocusContext.AsFloat(st?["speed"]);
                s.Cargo += FocusContext.AsFloat(st?["cargo"]);
                s.TroopCargo += FocusContext.AsFloat(st?["troopCargo"]);
                s.Size += FocusContext.AsFloat(st?["size"]);
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
