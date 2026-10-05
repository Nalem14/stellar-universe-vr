using System.Collections.Generic;
using Core.App;
using Core.Stations;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.Holo
{
    /// <summary>
    /// What a pirate's level means, said where a target is picked (viewscreen contact, interception menu, the
    /// arc's quote). On the server the level (GetConfigs.pirates minLevel..maxLevel) sets both the raider's
    /// loadout (GetPirateLoadout: more and heavier guns, more defences) and the bounty a win pays per level
    /// (AwardPirateDefeatRewards: XP, mineral, crystal, biomass, Nova). The loadout itself is read from the
    /// fleet's real stats (GetAllFleets stats), set against the ship that would go.
    /// </summary>
    public static class PirateIntel
    {
        /// <summary>"Niveau 12 / 50 : armement et butin" — the scale the level sits on.</summary>
        public static string LevelLine(FocusFleet foe)
        {
            if (foe == null || !foe.IsPirate || foe.PirateLevel <= 0)
                return string.Empty;
            var max = FocusContext.AsInt(GameConfig.Pirates?["maxLevel"]);
            return Trans.Format("vr.pirate.levelInfo", foe.PirateLevel, max > 0 ? max : foe.PirateLevel);
        }

        /// <summary>"Butin : Minerai +2 400 · Cristal +1 200 · … · XP +240 · Nova +24" (empty until the server serves it).</summary>
        public static string Loot(FocusFleet foe)
        {
            if (foe == null || !foe.IsPirate || foe.PirateLevel <= 0 || !(GameConfig.Pirates is JObject p))
                return string.Empty;
            var parts = new List<string>();
            Add(parts, Trans.Get("vr.res.mineral"), p["mineralPerLevel"], foe.PirateLevel);
            Add(parts, Trans.Get("vr.res.crystal"), p["crystalPerLevel"], foe.PirateLevel);
            Add(parts, Trans.Get("vr.res.biomass"), p["biomassPerLevel"], foe.PirateLevel);
            Add(parts, Trans.Get("xp"), p["xpPerLevel"], foe.PirateLevel);
            Add(parts, Trans.Get("nova"), p["novaPerLevel"], foe.PirateLevel);
            return parts.Count > 0 ? Trans.Format("vr.pirate.loot", string.Join("  ·  ", parts)) : string.Empty;
        }

        /// <summary>
        /// Two small lines under a raider's token label: its bounty, then what the level stands for. Empty for
        /// anything but a pirate with a level.
        /// </summary>
        public static string Caption(FocusFleet foe)
        {
            if (foe == null || !foe.IsPirate || foe.PirateLevel <= 0)
                return string.Empty;
            var loot = Loot(foe);
            return "\n<size=58%>" + (loot.Length > 0 ? "<color=#ffd98a>" + loot + "</color>\n" : string.Empty) +
                   "<color=#9fdcff>" + Trans.Get("vr.pirate.levelShort") + "</color></size>";
        }

        static void Add(List<string> parts, string label, JToken perLevel, int level)
        {
            var n = FocusContext.AsFloat(perLevel) * level;
            if (n > 0f)
                parts.Add(label + " +" + ScreenKit.Num(n));
        }

        /// <summary>
        /// "Dégâts 340 · Armure 1 200 · Bouclier 400", then how it weighs against our ship (favourable / even /
        /// dangerous) when one is given. Any hostile ship, not only raiders.
        /// </summary>
        public static string Threat(FocusFleet foe, FocusFleet mine)
        {
            if (foe == null || foe.DamageTotal + foe.ArmorTotal + foe.ShieldTotal <= 0f)
                return string.Empty;
            var line = Trans.Get("damage") + " " + ScreenKit.Num(foe.DamageTotal) + "  ·  " +
                       Trans.Get("armor") + " " + ScreenKit.Num(foe.ArmorTotal) + "  ·  " +
                       Trans.Get("shield") + " " + ScreenKit.Num(foe.ShieldTotal);
            var odds = Odds(foe, mine);
            return odds.Length > 0 ? line + "\n" + odds : line;
        }

        /// <summary>Our ship against theirs: firepower × staying power, each side; colour-coded verdict.</summary>
        public static string Odds(FocusFleet foe, FocusFleet mine)
        {
            if (foe == null || mine == null)
                return string.Empty;
            var theirs = Power(foe);
            var ours = Power(mine);
            if (theirs <= 0f || ours <= 0f)
                return string.Empty;
            var ratio = ours / theirs;
            var (key, colour) = ratio >= 1.5f ? ("vr.pirate.odds.good", "#7dffa0")
                : ratio >= 0.75f ? ("vr.pirate.odds.even", "#ffb866")
                : ("vr.pirate.odds.bad", "#ff6a5a");
            var name = string.IsNullOrEmpty(mine.Name) ? "#" + mine.Id : mine.Name;
            return "<color=" + colour + ">" + Trans.Format(key, name) + "</color>";
        }

        static float Power(FocusFleet f) =>
            Mathf.Sqrt(Mathf.Max(0f, f.DamageTotal) * Mathf.Max(1f, f.ArmorTotal + f.ShieldTotal));
    }
}
