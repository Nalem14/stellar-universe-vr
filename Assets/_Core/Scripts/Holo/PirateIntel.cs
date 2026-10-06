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

        /// <summary>
        /// Under another empire's ship token: whose it is and where we stand with them (ally / neutral / enemy), so a
        /// ship is never attacked by mistake. Empty for ours and for raiders (their own caption says what they are).
        /// </summary>
        public static string OwnerCaption(FocusFleet fleet)
        {
            if (fleet == null || fleet.IsPirate || fleet.IsOwnedBy(FocusContext.OwnedUserId()))
                return string.Empty;
            var stance = DiplomacyIndex.ResolveFleet(fleet);
            if (stance is EmpireStance.Owned or EmpireStance.Pirate)
                return string.Empty;
            var empire = DiplomacyIndex.TryIdentity(fleet.UserId, out var n, out _) && !string.IsNullOrEmpty(n)
                ? n
                : DiplomacyIndex.EmpireName(fleet.EmpireId);
            var key = stance switch
            {
                EmpireStance.Ally => "ally",
                EmpireStance.Neutral => "neutral",
                EmpireStance.Enemy => "enemy",
                _ => "unknown"
            };
            return "\n<size=60%><color=#" + ColorUtility.ToHtmlStringRGB(DiplomacyIndex.Tint(stance)) + "><noparse>" +
                   (string.IsNullOrEmpty(empire) ? "?" : empire) + "</noparse>  ·  " + Trans.Get(key) + "</color></size>";
        }

        /// <summary>A warning before hitting a ship that is not an enemy's (ally or neutral): the relation pays for it.</summary>
        public static string FriendlyFireWarning(FocusFleet foe)
        {
            if (foe == null || foe.IsPirate)
                return string.Empty;
            var stance = DiplomacyIndex.ResolveFleet(foe);
            return stance is EmpireStance.Ally or EmpireStance.Neutral or EmpireStance.Unknown
                ? "<color=#ffb866>" + Trans.Format("vr.hunt.notEnemy", Trans.Get(stance == EmpireStance.Ally ? "ally" : stance == EmpireStance.Neutral ? "neutral" : "unknown")) + "</color>"
                : string.Empty;
        }

        static void Add(List<string> parts, string label, JToken perLevel, int level)
        {
            // Rounded as the server pays it (a level-50 raider at 0.2 Nova per level: 10).
            var n = (float)System.Math.Round(FocusContext.AsFloat(perLevel) * level, System.MidpointRounding.AwayFromZero);
            if (n > 0f)
                parts.Add(label + " +" + ScreenKit.Num(n));
        }

        /// <summary>
        /// "Dégâts 340 · Armure 1 200 · Bouclier 400", then how it weighs against our ship (favourable / even /
        /// dangerous) when one is given. Any hostile ship, not only raiders.
        /// </summary>
        public static string Threat(FocusFleet foe, FocusFleet mine)
        {
            if (foe == null || foe.DamageTotal + Hull(foe) + foe.ShieldTotal <= 0f)
                return string.Empty;
            var line = Trans.Get("damage") + " " + ScreenKit.Num(foe.DamageTotal) + "  ·  " +
                       Trans.Get("vr.battle.hull") + " " + ScreenKit.Num(Hull(foe)) + "  ·  " +
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
            Mathf.Sqrt(Mathf.Max(0f, f.DamageTotal) * Mathf.Max(1f, Hull(f) + f.ShieldTotal));

        /// <summary>The hull it fights with (hullFleet), the armor sum on an older server.</summary>
        static float Hull(FocusFleet f) => f.HullTotal > 0f ? f.HullTotal : f.ArmorTotal;
    }
}
