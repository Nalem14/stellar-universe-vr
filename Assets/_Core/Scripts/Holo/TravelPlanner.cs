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
        /// <summary>Crystal the refused option would have needed (warnings: "42 needed, 10 in the hold").</summary>
        public int CrystalNeeded;
        /// <summary>The sub-light leg runs on the free conventional drive (speed 1): not enough crystal for more.</summary>
        public bool Conventional;
        /// <summary>Bond PRL recharging: seconds left before the module can jump again.</summary>
        public float ReadyIn;
    }

    /// <summary>
    /// Interstellar travel quotes and orders, mirroring the server exactly (actionjs.php MoveFleetToSystem /
    /// PrlBondFleetToSystem, web objects/fleet.js getHyperspaceQuoteTo / getPrlBondQuoteTo):
    /// distance = hypot on galaxy x/y (PRL: on visual_x/visual_y, as the server measures it); speed = ship speed (hyperspace) or min(speed, sublightSpeedCap);
    /// time = max(systemTravelDurationMin, distance × systemTravelSecondsPerDistance / speed);
    /// hyperspace crystal = ⌈distance × cost⌉ (falls back to sub-light if short); PRL range = base + level × step.
    /// The move_speed Nova booster is applied as the server does (<see cref="Boosters"/>); ETAs stay estimates (~).
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

        /// <summary>PRL distance: the server measures it on the map (map units), not the grid (Star.BondX / BondY).</summary>
        static bool TryBondDistance(FocusFleet fleet, float tx, float ty, out float distance)
        {
            distance = 0f;
            if (fleet == null || !GalaxyCatalog.TryGet(fleet.SystemId, out var here) ||
                !GalaxyCatalog.TryGetAt(tx, ty, out var there))
                return false;
            var dx = here.BondX - there.BondX;
            var dy = here.BondY - there.BondY;
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

            switch (mode)
            {
                case TravelMode.Sublight:
                    Sublight(fleet, q.Distance, ref q);
                    break;

                case TravelMode.Hyperspace:
                    if (!fleet.HasHyperdrive)
                    {
                        q.Available = false;
                        q.BlockKey = "notEnoughHyperspaceModules";
                        break;
                    }

                    // Server order: too few drives for the hull, else too little crystal → the trip falls back to
                    // sub-light (capped speed, then the sub-light crystal rule, down to the free speed-1 drive).
                    var hyperCost = Mathf.CeilToInt(q.Distance * GameConfig.HyperspaceCrystalPerDistance);
                    if (!fleet.EnoughHyperdrive)
                        q.FallbackKey = "vr.travel.warn.hyperModules";
                    else if (fleet.CrystalCargo < hyperCost)
                    {
                        q.FallbackKey = "vr.travel.warn.hyperCrystal";
                        q.CrystalNeeded = hyperCost;
                    }

                    if (q.FallbackKey == null)
                    {
                        q.CrystalCost = hyperCost;
                        q.EtaSeconds = Eta(q.Distance, ServerSpeed(fleet.Speed));
                        break;
                    }

                    var hyperWarn = q.FallbackKey;
                    var needed = q.CrystalNeeded;
                    Sublight(fleet, q.Distance, ref q);
                    // The sub-light leg may itself drop to the conventional drive: say both.
                    q.FallbackKey = hyperWarn;
                    q.CrystalNeeded = needed;
                    break;

                case TravelMode.PrlBond:
                    if (TryBondDistance(fleet, tx, ty, out var bond))
                        q.Distance = bond;
                    q.MaxRange = PrlMaxRange();
                    q.ReadyIn = Mathf.Max(0f, fleet.PrlBondReadyAt - FleetOrderGate.UnixNow());
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

        /// <summary>
        /// Before quoting: a ship with a Bond PRL needs the research level as it is now (a level finished outside
        /// the lab would otherwise shorten the range the lectern shows, and block a jump the server accepts).
        /// </summary>
        public static Task Prepare(FocusFleet fleet) =>
            fleet != null && fleet.HasPrlBond ? AuthManager.Ensure().FreshenEmpire(30f) : Task.CompletedTask;

        /// <summary>Bond PRL reach in map units: GetConfigs.prlBond baseRange + prlBond level × rangePerResearchLevel.</summary>
        public static float PrlMaxRange() => GameConfig.PrlBaseRange + PrlLevel() * GameConfig.PrlRangePerLevel;

        /// <summary>The ship can Bond-PRL jump right now (module, ratio, recharge) — its range ring is worth drawing.</summary>
        public static bool PrlReady(FocusFleet fleet) =>
            fleet != null && fleet.HasPrlBond && fleet.EnoughPrlBond && !fleet.IsStation &&
            fleet.PrlBondReadyAt <= FleetOrderGate.UnixNow() && GameConfig.PrlBaseRange > 0f;

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
            if (FleetOrderGate.LockKey(fleet) is { } locked)
                return Task.FromResult(ApiResult.Fail(Trans.Get(locked)));
            var pos = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", tx, ty);
            if (mode == TravelMode.PrlBond)
            {
                var prl = new Dictionary<string, string> { { "fleet", fleet.Id.ToString() }, { "pos", pos } };
                if (systemId > 0)
                    prl["system"] = systemId.ToString();
                return ActionJs.Get("PrlBondFleetToSystem", prl);
            }

            // hyperspace defaults to 1 server-side: sub-light must say 0 explicitly. The star by id when known
            // (preferred over pos by the server, so stale coordinates can never misroute the ship).
            var move = new Dictionary<string, string>
            {
                { "fleet", fleet.Id.ToString() },
                { "pos", pos },
                { "hyperspace", mode == TravelMode.Hyperspace ? "1" : "0" }
            };
            if (systemId > 0)
                move["system"] = systemId.ToString();
            return ActionJs.Get("MoveFleetToSystem", move);
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
        /// <param name="near">Where the lectern opens (beside that point, e.g. the star aimed on the table);
        /// null = in front of the captain (orders from a crew console).</param>
        public static async Task<(bool sent, ApiResult result, string barkAction)> AskAndSend(FocusFleet fleet,
            int systemId, float tx, float ty, string destLabel, UnityEngine.Vector3? near = null)
        {
            if (FleetOrderGate.LockKey(fleet) is { } locked)
                return (true, ApiResult.Fail(Trans.Get(locked)), null);

            var mode = TravelMode.Sublight;
            var console = OrderConsole.Instance;
            if (console != null)
            {
                var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
                var header = name + "  →  " + destLabel;
                await Prepare(fleet);
                var options = SystemOptions(fleet, tx, ty);
                var choice = await (near.HasValue ? console.AskAt(near.Value, header, options) : console.AskHere(header, options));
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
            TravelMode.Hyperspace => "hyperdrive",
            TravelMode.PrlBond => "bondPrlJump",
            _ => "sublight"
        };

        /// <summary>"Hyperspace · ~3:20 · 42 Crystal" — or the refusal reason. Bond PRL always shows distance / range
        /// first, as the web star menu does ("Bond PRL · 412/800 · 206 Crystal").</summary>
        public static string Describe(TravelQuote q)
        {
            var mode = Trans.Get(ModeKey(q.Mode));
            if (q.Mode == TravelMode.PrlBond && q.MaxRange > 0f && q.BlockKey != "noPrlBondModule" &&
                q.BlockKey != "notEnoughPrlBondModules")
                mode += "  ·  " + RangeText(q.Distance, q.MaxRange);
            if (!q.Available)
            {
                var why = mode + "  ·  " + Trans.Get(q.BlockKey ?? "vr.common.error");
                if (q.BlockKey == "prlBondRecharging" && q.ReadyIn > 0f)
                    why += "  " + TimeText(q.ReadyIn);
                else if (q.BlockKey == "notEnoughCrystalPrlBond")
                    why += "  (" + q.CrystalCost.ToString(CultureInfo.InvariantCulture) + " " + Trans.Get("crystalResource") + ")";
                return why;
            }

            var text = mode;
            if (q.EtaSeconds > 0f)
                text += "  ·  " + TimeText(q.EtaSeconds);
            if (q.CrystalCost > 0)
                text += "  ·  " + q.CrystalCost.ToString(CultureInfo.InvariantCulture) + " " + Trans.Get("crystalResource");
            var warn = Warning(q);
            return warn.Length > 0 ? text + "\n<size=78%><color=#ffb866>" + warn + "</color></size>" : text;
        }

        /// <summary>
        /// What the server will do instead of what was asked, said before the order (the web only toasts it after):
        /// too few hyperspace drives or too little crystal → sub-light; too little crystal for sub-light speed → the
        /// free conventional drive at speed 1. Empty when the trip runs as quoted.
        /// </summary>
        public static string Warning(TravelQuote q)
        {
            if (q.FallbackKey == null)
                return string.Empty;
            var text = q.FallbackKey == "vr.travel.warn.hyperModules"
                ? Trans.Get(q.FallbackKey)
                : Trans.Format(q.FallbackKey, q.CrystalNeeded.ToString(CultureInfo.InvariantCulture));
            if (q.Conventional && q.FallbackKey != "vr.travel.warn.conventional")
                text += "  ·  " + Trans.Get("vr.travel.warn.speedOne");
            return "<b>!</b> " + text;
        }

        /// <summary>"412/800": distance over reach, map units rounded (web star.js quote).</summary>
        public static string RangeText(float distance, float maxRange) =>
            Mathf.RoundToInt(distance).ToString(CultureInfo.InvariantCulture) + "/" +
            Mathf.RoundToInt(maxRange).ToString(CultureInfo.InvariantCulture);

        /// <summary>Estimate: ~45s, ~3:05, ~1:02:10, ~88 days 12:15.</summary>
        public static string TimeText(float seconds)
        {
            var s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            if (s < 60)
                return "~" + s + "s";
            // Seasonal events run for weeks: past two days, "~88 days 12:15" reads better than 2124:15:06.
            if (s >= 172800)
                return string.Format(CultureInfo.InvariantCulture, "~{0} {1} {2}:{3:00}", s / 86400, Trans.Get("days"),
                    s / 3600 % 24, s / 60 % 60);
            var h = s / 3600;
            var m = s / 60 % 60;
            var sec = s % 60;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "~{0}:{1:00}:{2:00}", h, m, sec)
                : string.Format(CultureInfo.InvariantCulture, "~{0}:{1:00}", m, sec);
        }

        /// <summary>
        /// Sub-light as the server runs it (actionjs.php MoveFleetToSystem): SublightSpeed (a share of the hull's
        /// speed, never under the flat cap), booster, integer; faster than 1 burns ⌈speed × distance × rate⌉ crystal
        /// from the hold — a hold that cannot pay falls back to the flat cap first, then to the free speed 1.
        /// </summary>
        static void Sublight(FocusFleet fleet, float distance, ref TravelQuote q)
        {
            var speed = ServerSpeed(GameConfig.SublightSpeed(Mathf.Max(1f, fleet.Speed)));
            q.CrystalCost = 0;
            if (speed > 1 && GameConfig.SublightCrystalPerDistance > 0f)
            {
                var cost = Mathf.CeilToInt(speed * distance * GameConfig.SublightCrystalPerDistance);
                var capSpeed = Mathf.Max(1, (int)Mathf.Min(speed, GameConfig.SublightSpeedCap));
                if (fleet.CrystalCargo < cost && capSpeed > 1 && capSpeed < speed &&
                    fleet.CrystalCargo >= Mathf.CeilToInt(capSpeed * distance * GameConfig.SublightCrystalPerDistance))
                {
                    speed = capSpeed;
                    cost = Mathf.CeilToInt(speed * distance * GameConfig.SublightCrystalPerDistance);
                }

                if (fleet.CrystalCargo < cost)
                {
                    q.FallbackKey = "vr.travel.warn.conventional";
                    q.CrystalNeeded = cost;
                    q.Conventional = true;
                    speed = 1;
                }
                else
                    q.CrystalCost = cost;
            }

            q.EtaSeconds = Eta(distance, speed);
        }

        /// <summary>Server: speed /= move_speed booster, then max(1, (int)speed).</summary>
        static int ServerSpeed(float speed) =>
            Mathf.Max(1, Mathf.FloorToInt(speed / Mathf.Max(0.01f, Boosters.MoveTimeFactor)));

        /// <summary>Server: max(systemTravelDurationMin, distance × systemTravelSecondsPerDistance / speed).</summary>
        static float Eta(float distance, int speed) =>
            Mathf.Max(GameConfig.TravelDurationMin, distance * GameConfig.TravelSecondsPerDistance / Mathf.Max(1, speed));

        static float PrlLevel()
        {
            var empire = AuthManager.Ensure().Empire;
            return empire != null ? FocusContext.AsFloat(empire["prlBond"]) : 0f;
        }
    }
}
