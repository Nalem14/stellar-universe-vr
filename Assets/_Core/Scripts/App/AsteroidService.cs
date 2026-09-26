using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Live reserves of the focused system's asteroid fields. The fields listed by GetSystems are a snapshot
    /// cached up to an hour server-side, so — like web scenes/system.js on entering a system — GetSystemAsteroids
    /// is read fresh: on each system visit, then every 30 s while one of our ships mines there (reserves fall
    /// on each harvest), else every 2 min. Fields the server no longer returns are mined out: marked
    /// <see cref="FocusAsteroid.Gone"/> so the table and the windows drop them.
    /// </summary>
    public sealed class AsteroidService : MonoBehaviour
    {
        const float MiningInterval = 30f;
        const float IdleInterval = 120f;

        public static AsteroidService Instance { get; private set; }

        FocusContext _focus;
        readonly Dictionary<int, JArray> _bySystem = new();
        int _lastSystem = -1;
        float _next;
        bool _busy;

        /// <summary>Reserves of the focused system were applied (table labels, rock sizes outside).</summary>
        public event Action Updated;

        public static AsteroidService Build(Transform host, FocusContext focus)
        {
            var go = new GameObject("AsteroidService");
            go.transform.SetParent(host, false);
            var s = go.AddComponent<AsteroidService>();
            s._focus = focus;
            if (focus != null)
                focus.Changed += s.OnFocusChanged;
            Instance = s;
            s.OnFocusChanged();
            return s;
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
            if (Instance == this)
                Instance = null;
        }

        void OnFocusChanged()
        {
            var sys = _focus != null ? _focus.SystemId : 0;
            if (sys <= 0)
                return;
            // The system read rebuilds the field list: put the last known reserves back at once.
            if (_bySystem.TryGetValue(sys, out var known))
                Apply(known);
            if (sys != _lastSystem)
            {
                _lastSystem = sys;
                _next = 0f;
            }
        }

        void Update()
        {
            if (_busy || _focus == null || _focus.SystemId <= 0 || _focus.Asteroids.Count == 0 || Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + (MiningHere() ? MiningInterval : IdleInterval);
            AsyncTap.Run(Refresh());
        }

        bool MiningHere()
        {
            var now = FleetOrderGate.UnixNow();
            var me = FocusContext.OwnedUserId();
            foreach (var f in _focus.Fleets)
                if (f.SystemId == _focus.SystemId && f.AsteroidId > 0 && f.IsOwnedBy(me) && f.IsHarvesting(now))
                    return true;
            return false;
        }

        /// <summary>Read the focused system's fields now (after a harvest order, on entering the system).</summary>
        public async Task Refresh()
        {
            var sys = _focus != null ? _focus.SystemId : 0;
            if (_busy || sys <= 0 || !AuthManager.Ensure().IsLoggedIn)
                return;
            _busy = true;
            try
            {
                var r = await ActionJs.Get("GetSystemAsteroids", new Dictionary<string, string>
                {
                    { "systemid", sys.ToString() }
                });
                if (!r.Ok || string.IsNullOrEmpty(r.Body))
                    return;
                JArray rows;
                try
                {
                    rows = JToken.Parse(r.Body) as JArray;
                }
                catch
                {
                    return;
                }

                if (rows == null)
                    return;
                _bySystem[sys] = rows;
                if (_focus.SystemId == sys)
                    Apply(rows);
            }
            finally
            {
                _busy = false;
            }
        }

        void Apply(JArray rows)
        {
            var byId = new Dictionary<int, JToken>();
            foreach (var row in rows)
                byId[FocusContext.AsInt(row["id"])] = row;
            foreach (var a in _focus.Asteroids)
            {
                if (!byId.TryGetValue(a.Id, out var row))
                {
                    a.Gone = true;
                    continue;
                }

                a.Gone = false;
                a.Live = true;
                a.Mineral = FocusContext.AsFloat(row["mineral"]);
                a.MineralMax = FocusContext.AsFloat(row["mineralMax"]);
                a.Crystal = FocusContext.AsFloat(row["crystal"]);
                a.CrystalMax = FocusContext.AsFloat(row["crystalMax"]);
            }

            Updated?.Invoke();
        }

        /// <summary>"12 400 Mineral · 3 100 Crystal" — or the mined-out notice; empty until read.</summary>
        public static string Reserves(FocusAsteroid a)
        {
            if (a == null || !a.Live)
                return string.Empty;
            if (a.Mineral + a.Crystal <= 0f)
                return Trans.Get("asteroidDepleted");
            return Core.Stations.ScreenKit.Num(a.Mineral) + " " + Trans.Get("mineralResource") + "  ·  " +
                   Core.Stations.ScreenKit.Num(a.Crystal) + " " + Trans.Get("crystalResource");
        }
    }
}
