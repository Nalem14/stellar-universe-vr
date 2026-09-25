using System.Collections.Generic;
using Core.Vfx;

namespace Core.Crew
{
    /// <summary>
    /// Bark catalogue: (role, event) → number of variants. Text lives server-side as
    /// crew.&lt;role&gt;.&lt;event&gt;.&lt;n&gt; keys (docs/i18n/missing-keys.md §3) and is always read through Trans.
    /// Adding a variant = add the key server-side and bump the count here.
    /// </summary>
    public static class CrewLines
    {
        static readonly Dictionary<string, int> Variants = new()
        {
            { "helm.greet", 2 }, { "helm.ack", 3 }, { "helm.hyperspace", 2 }, { "helm.sublightFallback", 1 },
            { "helm.sublightModules", 1 }, { "helm.prlBond", 1 }, { "helm.prlRecharge", 1 }, { "helm.arrive", 2 },
            { "helm.busy", 1 }, { "helm.fail", 1 }, { "helm.idle", 2 },

            { "tactical.greet", 1 }, { "tactical.contact", 1 }, { "tactical.hostile", 2 }, { "tactical.pirate", 1 },
            { "tactical.battleStart", 1 }, { "tactical.yourTurn", 2 }, { "tactical.hit", 1 }, { "tactical.damaged", 1 },
            { "tactical.victory", 1 }, { "tactical.defeat", 1 }, { "tactical.siege", 1 }, { "tactical.stance", 1 },
            { "tactical.underAttack", 1 }, { "tactical.fail", 1 }, { "tactical.idle", 1 },

            { "engineering.greet", 1 }, { "engineering.ack", 1 }, { "engineering.harvest", 1 },
            { "engineering.cargoFull", 1 }, { "engineering.moduleBuilt", 1 }, { "engineering.modulePlaced", 1 },
            { "engineering.queueDone", 1 }, { "engineering.queueFull", 1 }, { "engineering.fail", 1 },
            { "engineering.idle", 2 },

            { "science.greet", 1 }, { "science.explore", 1 }, { "science.exploreDone", 1 }, { "science.anomaly", 1 },
            { "science.scan", 1 }, { "science.researchDone", 1 }, { "science.researchStart", 1 },
            { "science.queueFull", 1 }, { "science.fail", 1 }, { "science.idle", 1 },

            { "comms.greet", 1 }, { "comms.newMail", 1 }, { "comms.chat", 1 }, { "comms.warDeclared", 1 },
            { "comms.peaceOffer", 1 }, { "comms.allianceInvite", 1 }, { "comms.sent", 1 },
            { "comms.stargateOpen", 1 }, { "comms.fail", 1 }, { "comms.idle", 1 },

            { "ops.greet", 1 }, { "ops.buildStart", 1 }, { "ops.buildDone", 1 }, { "ops.queueFull", 1 },
            { "ops.decision", 1 }, { "ops.colonize", 1 }, { "ops.lowResources", 1 }, { "ops.cargoDeposited", 1 },
            { "ops.fail", 1 }, { "ops.idle", 1 }
        };

        public static string RoleKey(CrewDialogue.Role role) => role switch
        {
            CrewDialogue.Role.Helm => "helm",
            CrewDialogue.Role.Tactical => "tactical",
            CrewDialogue.Role.Engineering => "engineering",
            CrewDialogue.Role.Science => "science",
            CrewDialogue.Role.Comms => "comms",
            _ => "ops"
        };

        public static int Count(CrewDialogue.Role role, string evt) =>
            Variants.TryGetValue(RoleKey(role) + "." + evt, out var n) ? n : 0;

        /// <summary>Server key of variant <paramref name="index"/> (1-based).</summary>
        public static string Key(CrewDialogue.Role role, string evt, int index) =>
            "crew." + RoleKey(role) + "." + evt + "." + index;

        /// <summary>
        /// Bark event for a successful order, by API action (null = no line for it).
        /// Fleet orders stay with the station that issued them.
        /// </summary>
        public static string AckEvent(string action) => action switch
        {
            "MoveFleetToSystem" or "MoveFleetToPlanet" or "MoveFleetToAsteroid" => "ack",
            "HyperspaceJump" => "hyperspace",
            "PrlBondFleetToSystem" => "prlBond",
            "UpdateFleetDefendPosition" => "stance",
            "FleetAttackPlanet" => "siege",
            "HarvestAsteroid" => "harvest",
            "ExplorePlanet" => "explore",
            "DepositCargo" or "WithdrawCargo" => "cargoDeposited",
            "Colonize" => "colonize",
            "MakeBattle" => "battleStart",
            _ => null
        };

        /// <summary>Server notice (ok:…) → dedicated Helm line.</summary>
        public static string NoticeEvent(string noticeKey) => noticeKey switch
        {
            "notEnoughCrystalForHyperspace" => "sublightFallback",
            "notEnoughHyperspaceModules" => "sublightModules",
            _ => null
        };
    }
}
