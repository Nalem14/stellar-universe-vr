using System.Collections.Generic;
using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// An order that leaves the ship is heard leaving: the dispatch burst on every successful movement order
    /// (move, bond, gate, order queue), wherever it was given from (table, lectern, crew, queue path).
    /// </summary>
    public static class OrderCues
    {
        static bool _hooked;

        static readonly HashSet<string> Dispatches = new()
        {
            "MoveFleetToSystem", "MoveFleetToPlanet", "MoveFleetToAsteroid", "PrlBondFleetToSystem", "SendFleetToJumpgate",
            "SetFleetOrderQueue", "AddFleetToBattle", "ExplorePlanet"
        };

        public static void Ensure()
        {
            if (_hooked)
                return;
            _hooked = true;
            ActionJs.Succeeded += OnSucceeded;
        }

        static void OnSucceeded(string action, IDictionary<string, string> query, ApiResult result)
        {
            if (action == null || !Dispatches.Contains(action))
                return;
            var cam = Camera.main;
            CicCue.Dispatch(cam != null ? cam.transform.position + cam.transform.forward * 0.6f : Vector3.zero);
        }
    }
}
