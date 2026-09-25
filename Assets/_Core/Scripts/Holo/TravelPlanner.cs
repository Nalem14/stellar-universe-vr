using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Holo
{
    public enum TravelMode
    {
        Sublight,
        Hyperspace,
        PrlBond
    }

    /// <summary>What an interstellar order will cost before it is sent.</summary>
    public struct TravelQuote
    {
        public TravelMode Mode;
        /// <summary>False = the server would refuse it (see <see cref="BlockKey"/>).</summary>
        public bool Available;
        /// <summary>Native key of the refusal (out of range, recharging, no module, not enough crystal).</summary>
        public string BlockKey;
        /// <summary>Native key of a server fallback to sub-light (hyperspace without crystal / modules).</summary>
        public string FallbackKey;
        public float Distance;
        public float EtaSeconds;
        public int CrystalCost;
        public float MaxRange;
    }

    /// <summary>
    /// Interstellar travel quotes and orders, mirroring the server exactly (actionjs.php MoveFleetToSystem /
    /// PrlBondFleetToSystem, web objects/fleet.js getHyperspaceQuoteTo / getPrlBondQuoteTo):
    /// distance = hypot on galaxy x/y; speed = ship speed (hyperspace) or min(speed, sublightSpeedCap);
    /// time = max(systemTravelDurationMin, distance × systemTravelSecondsPerDistance / speed);
    /// hyperspace crystal = ⌈distance × cost⌉ (falls back to sub-light if short); PRL range = base + level × step.
    /// Speed boosters are server-side only, so ETAs are shown as estimates (~).
    /// </summary>
    public static class TravelPlanner
    {
        public static bool TryDistance(FocusFleet fleet, float tx, float ty, out float distance)
        {
            distance = 0f;
            if (fleet == null || !GalaxyCatalog.TryGet(fleet.SystemId, out var here))
                return false;
            var dx = here.X - tx;
            var dy = here.Y - ty;
            distance = Mathf.Sqrt(dx * dx + dy * dy);
            return true;
        }

        public static TravelQuote Quote(FocusFleet fleet, float tx, float ty, TravelMode mode)
        {
            var q = new TravelQuote { Mode = mode, Available = true };
            if (fleet == null || !GameConfig.Loaded || !TryDistance(fleet, tx, ty, out q.Distance))
            {
                q.Available = mode == TravelMode.Sublight;
                return q;
            }

            var speed = Mathf.Max(1f, fleet.Speed);
            switch (mode)
            {
                case TravelMode.Sublight:
                    if (GameConfig.SublightSpeedCap > 0f)
                        speed = Mathf.Max(1f, Mathf.Min(speed, GameConfig.SublightSpeedCap));
                    q.EtaSeconds = Eta(q.Distance, speed);
                    break;

                case TravelMode.Hyperspace:
                    if (!fleet.HasHyperdrive)
                    {
                        q.Available = false;
                        q.BlockKey = "notEnoughHyperspaceModules";
                        break;
                    }

                    q.CrystalCost = Mathf.CeilToInt(q.Distance * GameConfig.HyperspaceCrystalPerDistance);
                    if (!fleet.EnoughHyperdrive)
                        q.FallbackKey = "notEnoughHyperspaceModules";
                    else if (fleet.CrystalCargo < q.CrystalCost)
                        q.FallbackKey = "notEnoughCrystalForHyperspace";
                    var hyperSpeed = q.FallbackKey == null
                        ? speed
                        : Mathf.Max(1f, Mathf.Min(speed, GameConfig.SublightSpeedCap > 0f ? GameConfig.SublightSpeedCap : speed));
                    q.EtaSeconds = Eta(q.Distance, hyperSpeed);
                    break;

                case TravelMode.PrlBond:
                    q.MaxRange = GameConfig.PrlBaseRange + PrlLevel() * GameConfig.PrlRangePerLevel;
                    q.CrystalCost = Mathf.CeilToInt(q.Distance * GameConfig.PrlCrystalPerDistance);
                    q.EtaSeconds = GameConfig.PrlTransitSeconds;
                    q.BlockKey = !fleet.HasPrlBond ? "noPrlBondModule"
                        : !fleet.EnoughPrlBond ? "notEnoughPrlBondModules"
                        : fleet.PrlBondReadyAt > FleetOrderGate.UnixNow() ? "prlBondRecharging"
                        : q.Distance > q.MaxRange ? "outOfPrlBondRange"
                        : fleet.CrystalCargo < q.CrystalCost ? "notEnoughCrystalPrlBond"
                        : null;
                    q.Available = q.BlockKey == null;
                    break;
            }

            return q;
        }

        /// <summary>Modes worth offering for this ship (PRL / hyperspace only with the module on board).</summary>
        public static void Modes(FocusFleet fleet, List<TravelMode> into)
        {
            into.Clear();
            into.Add(TravelMode.Sublight);
            if (fleet != null && fleet.HasHyperdrive)
                into.Add(TravelMode.Hyperspace);
            if (fleet != null && fleet.HasPrlBond)
                into.Add(TravelMode.PrlBond);
        }

        public static Task<ApiResult> Send(FocusFleet fleet, int systemId, float tx, float ty, TravelMode mode)
        {
            var pos = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", tx, ty);
            if (mode == TravelMode.PrlBond)
            {
                var prl = new Dictionary<string, string> { { "fleet", fleet.Id.ToString() }, { "pos", pos } };
                if (systemId > 0)
                    prl["system"] = systemId.ToString();
                return ActionJs.Get("PrlBondFleetToSystem", prl);
            }

            // hyperspace defaults to 1 server-side: sub-light must say 0 explicitly.
            return ActionJs.Get("MoveFleetToSystem", new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "pos", pos },
                { "hyperspace", mode == TravelMode.Hyperspace ? "1" : "0" }
            });
        }

        /// <summary>One lectern option per travel mode this ship has (web star menu: move / hyperspace / PRL).</summary>
        public static List<OrderConsole.Option> SystemOptions(FocusFleet fleet, float tx, float ty)
        {
            var options = new List<OrderConsole.Option>();
            var modes = new List<TravelMode>();
            Modes(fleet, modes);
            foreach (var m in modes)
            {
                var q = Quote(fleet, tx, ty, m);
                var accent = !q.Available ? Core.UI.UiKit.Danger
                    : q.FallbackKey != null ? Core.UI.UiKit.Amber
                    : m == TravelMode.Sublight ? Core.UI.UiKit.Cyan : Core.UI.UiKit.Ok;
                options.Add(new OrderConsole.Option(Describe(q), q.Available, accent, m));
            }

            return options;
        }

        /// <summary>
        /// Quote on the lectern, send on Confirm. Returns (sent, result, barkAction); sent=false = cancelled.
        /// Without a lectern it sends sub-light (never the server's implicit hyperspace default).
        /// </summary>
        public static async Task<(bool sent, ApiResult result, string barkAction)> AskAndSend(FocusFleet fleet,
            int systemId, float tx, float ty, string destLabel)
        {
            var mode = TravelMode.Sublight;
            var console = OrderConsole.Instance;
            if (console != null)
            {
                var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
                var choice = await console.Ask(name + "  →  " + destLabel, SystemOptions(fleet, tx, ty));
                if (!(choice is TravelMode chosen))
                    return (false, default, null);
                mode = chosen;
            }

            var result = await Send(fleet, systemId, tx, ty, mode);
            return (true, result, BarkAction(mode));
        }

        /// <summary>CrewLines action id for a successful jump of this mode.</summary>
        public static string BarkAction(TravelMode mode) => mode switch
        {
            TravelMode.PrlBond => "PrlBondFleetToSystem",
            TravelMode.Hyperspace => "HyperspaceJump",
            _ => "MoveFleetToSystem"
        };

        public static string ModeKey(TravelMode mode) => mode switch
        {
            TravelMode.Hyperspace => "hyperspaceJump",
            TravelMode.PrlBond => "bondPrlJump",
            _ => "sublight"
        };

        /// <summary>"Hyperspace · ~3:20 · 42 Crystal" — or the refusal reason.</summary>
        public static string Describe(TravelQuote q)
        {
            var mode = Trans.Get(ModeKey(q.Mode));
            if (!q.Available)
                return mode + "  ·  " + Trans.Get(q.BlockKey ?? "vr.common.error");
            var text = mode;
            if (q.EtaSeconds > 0f)
                text += "  ·  " + TimeText(q.EtaSeconds);
            if (q.CrystalCost > 0)
                text += "  ·  " + q.CrystalCost.ToString(CultureInfo.InvariantCulture) + " " + Trans.Get("crystalResource");
            return text;
        }

        /// <summary>Locale-neutral estimate: ~45s, ~3:05, ~1:02:10.</summary>
        public static string TimeText(float seconds)
        {
            var s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            if (s < 60)
                return "~" + s + "s";
            var h = s / 3600;
            var m = s / 60 % 60;
            var sec = s % 60;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "~{0}:{1:00}:{2:00}", h, m, sec)
                : string.Format(CultureInfo.InvariantCulture, "~{0}:{1:00}", m, sec);
        }

        static float Eta(float distance, float speed) =>
            Mathf.Max(GameConfig.TravelDurationMin, distance * GameConfig.TravelSecondsPerDistance / Mathf.Max(1f, speed));

        static float PrlLevel()
        {
            var empire = AuthManager.Ensure().Empire;
            return empire != null ? FocusContext.AsFloat(empire["prlBond"]) : 0f;
        }
    }
}
