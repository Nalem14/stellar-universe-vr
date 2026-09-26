using System.Collections.Generic;
using System.Globalization;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.App
{
    /// <summary>How a ship crosses between systems (drives what the bridge shows on the way).</summary>
    public enum VoyageMode
    {
        Sublight,
        Hyperspace,
        PrlBond,
        Jumpgate
    }

    /// <summary>
    /// Which drive carried each ship on its current interstellar trip. The server stores no travel mode (a
    /// jump stamps systemid = destination, from, dest and desttime — actionjs.php MoveFleetToSystem /
    /// MoveFleetToPlanet / MoveFleetToAsteroid / PrlBondFleetToSystem / SendFleetToJumpgate), so orders sent
    /// from this headset are recorded as they succeed, and a trip started elsewhere (web, server queue) is
    /// inferred from its timings: PRL and gate transits are short fixed windows, hyperspace is as fast as the
    /// ship's summed speed, sub-light is capped (config sublightSpeedCap).
    /// </summary>
    public static class VoyageLog
    {
        struct Entry
        {
            public VoyageMode Mode;
            public float At;
            /// <summary>Target system when the order names one (0 = learnt from the next poll).</summary>
            public int SystemId;
        }

        static readonly Dictionary<int, Entry> Orders = new();
        static bool _hooked;

        /// <summary>An interstellar order of ours just succeeded: (fleet, mode, target system or 0).</summary>
        public static event System.Action<int, VoyageMode, int> Ordered;

        public static void Ensure()
        {
            if (_hooked)
                return;
            _hooked = true;
            ActionJs.Succeeded += OnSucceeded;
        }

        static void OnSucceeded(string action, IDictionary<string, string> query, ApiResult result)
        {
            if (query == null || !query.TryGetValue("fleet", out var raw) ||
                !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fleetId))
                return;
            var body = result.Body ?? string.Empty;
            // hyperspace defaults to 1 server-side; a fallback answers ok:sublight_*.
            var hyper = !query.TryGetValue("hyperspace", out var h) || h != "0";
            if (body.StartsWith("ok:sublight", System.StringComparison.Ordinal))
                hyper = false;
            VoyageMode mode;
            var system = 0;
            switch (action)
            {
                case "MoveFleetToSystem":
                    mode = hyper ? VoyageMode.Hyperspace : VoyageMode.Sublight;
                    system = SystemAtPos(query);
                    break;
                case "MoveFleetToPlanet":
                case "MoveFleetToAsteroid":
                    // Only a trip to another system is a voyage; an in-system hop keeps the view as is.
                    mode = hyper ? VoyageMode.Hyperspace : VoyageMode.Sublight;
                    break;
                case "PrlBondFleetToSystem":
                    mode = VoyageMode.PrlBond;
                    system = query.TryGetValue("system", out var s) && int.TryParse(s, out var sid) ? sid : SystemAtPos(query);
                    break;
                case "SendFleetToJumpgate":
                    mode = VoyageMode.Jumpgate;
                    break;
                default:
                    return;
            }

            Orders[fleetId] = new Entry { Mode = mode, At = Time.realtimeSinceStartup, SystemId = system };
            Ordered?.Invoke(fleetId, mode, system);
        }

        static int SystemAtPos(IDictionary<string, string> query)
        {
            if (!query.TryGetValue("pos", out var pos) || string.IsNullOrEmpty(pos))
                return 0;
            var dot = pos.IndexOf('.');
            if (dot <= 0 ||
                !float.TryParse(pos.Substring(0, dot), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(pos.Substring(dot + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return 0;
            return GalaxyCatalog.TryGetAt(x, y, out var star) ? star.Id : 0;
        }

        /// <summary>Recorded mode of an order sent in the last few minutes, else false.</summary>
        public static bool TryRecorded(int fleetId, out VoyageMode mode)
        {
            mode = VoyageMode.Sublight;
            if (!Orders.TryGetValue(fleetId, out var e) || Time.realtimeSinceStartup - e.At > 600f)
                return false;
            mode = e.Mode;
            return true;
        }

        /// <summary>The ship is between systems (server stamps systemid = dest on departure).</summary>
        public static bool IsInterstellar(FocusFleet f, long now)
        {
            if (f == null || !f.IsMoving(now))
                return false;
            if (f.FromSystemId > 0 && f.DestSystemId == f.SystemId && f.FromSystemId != f.DestSystemId)
                return true;
            // A gate jump leaves from/dest untouched: a short transit docked at the target world.
            return LooksLikeGate(f, now);
        }

        public static VoyageMode Resolve(FocusFleet f, long now)
        {
            if (f != null && TryRecorded(f.Id, out var recorded))
                return recorded;
            if (f == null)
                return VoyageMode.Sublight;
            var left = f.DestTime - now;
            var prl = GameConfig.PrlTransitSeconds > 0f ? GameConfig.PrlTransitSeconds : 10f;
            if (f.PrlBondReadyAt > now && left <= prl + 3f)
                return VoyageMode.PrlBond;
            if (LooksLikeGate(f, now))
                return VoyageMode.Jumpgate;
            if (!f.HasHyperdrive || !f.EnoughHyperdrive)
                return VoyageMode.Sublight;
            // With the drive: faster than the capped sub-light time for this distance = hyperspace.
            if (GameConfig.Loaded && GalaxyCatalog.TryGet(f.FromSystemId, out var a) && GalaxyCatalog.TryGet(f.DestSystemId, out var b))
            {
                var d = Mathf.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                var cap = GameConfig.SublightSpeedCap > 0f ? GameConfig.SublightSpeedCap : f.Speed;
                var slow = Mathf.Max(GameConfig.TravelDurationMin, d * GameConfig.TravelSecondsPerDistance / Mathf.Max(1f, Mathf.Min(f.Speed, cap)));
                var fast = Mathf.Max(GameConfig.TravelDurationMin, d * GameConfig.TravelSecondsPerDistance / Mathf.Max(1f, f.Speed));
                if (slow - fast > 5f)
                    return left > (slow + fast) * 0.5f ? VoyageMode.Sublight : VoyageMode.Hyperspace;
            }

            return VoyageMode.Hyperspace;
        }

        static bool LooksLikeGate(FocusFleet f, long now)
        {
            var gate = GameConfig.JumpgateTransitSeconds > 0f ? GameConfig.JumpgateTransitSeconds : 30f;
            return f.PlanetId > 0 && f.DestTime - now <= gate + 5f && (f.FromSystemId == 0 || f.DestSystemId != f.SystemId) &&
                   !(TryRecorded(f.Id, out var m) && m != VoyageMode.Jumpgate);
        }

        /// <summary>Native label of the drive (same keys as the travel lectern).</summary>
        public static string ModeKey(VoyageMode mode) => mode switch
        {
            VoyageMode.Hyperspace => "hyperdrive",
            VoyageMode.PrlBond => "bondPrlJump",
            VoyageMode.Jumpgate => "jumpgate",
            _ => "sublight"
        };
    }
}
