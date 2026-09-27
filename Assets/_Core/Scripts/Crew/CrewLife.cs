using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Crew
{
    /// <summary>
    /// The rest of the watch: crew who are not at the six stations. Operators work the auxiliary console banks
    /// on the bridge's side walls; two hands walk the outer ring of the bridge (lockers, server racks,
    /// sideboards — never across the middle, where the captain and the table are), stopping to work at each;
    /// one walks the corridor between its wall screens and consoles. They step aside for the player (stop,
    /// look, turn back after a moment). The ship's alert changes them: amber, a quicker pace and shorter
    /// stops; red, everyone runs to the nearest post and stays at it, typing fast, until the alert clears.
    /// Pure dressing: no gameplay, no network; a few draw calls each (<see cref="CrewExtra"/>).
    /// </summary>
    public sealed class CrewLife : MonoBehaviour
    {
        struct Spot
        {
            public Vector3 At;
            public Vector3 Facing;
            public bool Post;
        }

        sealed class Walker
        {
            public CrewExtra Body;
            public Spot[] Ring;
            public int Index;
            public int Step = 1;
            public float Dwell;
            public float Blocked;
            public bool Moving;
            public bool PingPong;
        }

        readonly List<Walker> _walkers = new();
        readonly List<CrewExtra> _operators = new();
        AlertLevel _alert;
        Transform _bridge;

        static readonly Color Engineering = new(0.45f, 1f, 0.55f);
        static readonly Color Science = new(0.72f, 0.5f, 1f);
        static readonly Color Ops = new(0.35f, 0.62f, 1f);
        static readonly Color Tactical = new(1f, 0.62f, 0.22f);
        static readonly Color Comms = new(0.4f, 0.95f, 0.9f);

        public static CrewLife Build(Transform bridge, Transform corridor)
        {
            var go = new GameObject("CrewLife");
            go.transform.SetParent(bridge, false);
            var life = go.AddComponent<CrewLife>();
            life._bridge = bridge;
            var family = CrewSpecies.Ours;

            // Operators at the two side-wall console banks (BridgeDecor), facing the wall.
            life.Operator(bridge, "AuxScience", new Vector3(-4.95f, 0f, -0.1f), Vector3.left, Science, family);
            life.Operator(bridge, "AuxEngineering", new Vector3(4.95f, 0f, -0.1f), Vector3.right, Engineering, family);

            // The outer ring of the bridge, port bank to starboard bank round the aft wall.
            var ring = new[]
            {
                S(-4.9f, -2.0f, Vector3.left, true), S(-4.9f, -2.85f, Vector3.left, true),
                S(-4.25f, -4.55f, new Vector3(-1f, 0f, -1f), true), S(-2.35f, -5.2f, Vector3.back, true),
                S(2.35f, -5.2f, Vector3.back, true), S(4.25f, -4.55f, new Vector3(1f, 0f, -1f), true),
                S(4.9f, -2.85f, Vector3.right, true), S(4.9f, -2.0f, Vector3.right, true)
            };
            life.Walk(bridge, "DeckHandOps", ring, 1, Ops, family, false);
            life.Walk(bridge, "DeckHandTactical", ring, 6, Tactical, family, false);

            if (corridor != null)
            {
                // Corridor: along the walls between the screens, the bench and the far console (CorridorDecor).
                var hall = new[]
                {
                    S(1.05f, 1.25f, Vector3.right, false), S(-1.05f, 3.25f, Vector3.left, false),
                    S(-1.0f, 6.0f, Vector3.left, false), S(1.05f, 8.75f, Vector3.right, false),
                    S(1.0f, 13.55f, Vector3.right, true)
                };
                life.Walk(corridor, "CorridorHand", hall, 0, Comms, family, true);
            }

            AlertState.Changed += life.OnAlert;
            life._alert = AlertState.Level;
            return life;
        }

        void OnDestroy() => AlertState.Changed -= OnAlert;

        static Spot S(float x, float z, Vector3 facing, bool post) =>
            new() { At = new Vector3(x, 0f, z), Facing = facing.normalized, Post = post };

        void Operator(Transform parent, string name, Vector3 at, Vector3 facing, Color accent, CrewSpecies.Family family)
        {
            var body = CrewExtra.Build(parent, name, accent, family);
            body.transform.localPosition = at;
            body.transform.localRotation = Quaternion.LookRotation(facing, Vector3.up);
            body.Current = CrewExtra.Pose.Work;
            _operators.Add(body);
        }

        void Walk(Transform parent, string name, Spot[] ring, int start, Color accent, CrewSpecies.Family family, bool pingPong)
        {
            var body = CrewExtra.Build(parent, name, accent, family);
            body.transform.localPosition = ring[start].At;
            body.transform.localRotation = Quaternion.LookRotation(ring[start].Facing, Vector3.up);
            body.Current = CrewExtra.Pose.Work;
            _walkers.Add(new Walker
            {
                Body = body, Ring = ring, Index = start, Dwell = Random.Range(2f, 8f), PingPong = pingPong,
                Step = Random.value < 0.5f ? 1 : -1
            });
        }

        void OnAlert(AlertLevel from, AlertLevel to)
        {
            _alert = to;
            foreach (var o in _operators)
                o.Urgency = to == AlertLevel.Red ? 1f : to == AlertLevel.Amber ? 0.4f : 0f;
            // Red: whoever is between posts cuts the stop short and heads for the nearest one.
            if (to != AlertLevel.Red)
                return;
            foreach (var w in _walkers)
            {
                w.Dwell = 0f;
                if (!w.Ring[w.Index].Post)
                    w.Step = NearestPostStep(w);
            }
        }

        static int NearestPostStep(Walker w)
        {
            for (var d = 1; d < w.Ring.Length; d++)
            {
                if (w.Index + d < w.Ring.Length && w.Ring[w.Index + d].Post)
                    return 1;
                if (w.Index - d >= 0 && w.Ring[w.Index - d].Post)
                    return -1;
            }

            return w.Step;
        }

        void Update()
        {
            var dt = Time.deltaTime;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : Vector3.one * 1e6f;
            var red = _alert == AlertLevel.Red;
            var amber = _alert == AlertLevel.Amber;

            foreach (var o in _operators)
                o.LookAt(red && Mathf.Repeat(Time.time + o.GetInstanceID() * 0.1f, 9f) < 1.5f
                    ? BridgeScreen()
                    : null);

            foreach (var w in _walkers)
            {
                var body = w.Body;
                if (!body.isActiveAndEnabled)
                    continue;
                var t = body.transform;
                var spot = w.Ring[w.Index];
                if (!w.Moving)
                {
                    // At a post: work it; in a red alert, stay put until it clears.
                    body.Current = CrewExtra.Pose.Work;
                    body.Urgency = red ? 1f : amber ? 0.4f : 0f;
                    t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.LookRotation(spot.Facing, Vector3.up), 1f - Mathf.Exp(-4f * dt));
                    if (red && spot.Post)
                        continue;
                    w.Dwell -= dt;
                    if (w.Dwell > 0f)
                        continue;
                    var next = NextIndex(w);
                    // Never two hands at one post: wait here a little longer instead.
                    if (Taken(w, next))
                    {
                        w.Dwell = 1.5f;
                        w.Step = -w.Step;
                        continue;
                    }

                    w.Index = next;
                    w.Moving = true;
                    continue;
                }

                // Walking to the next spot, slowing near it; stepping aside for the player.
                var target = w.Ring[w.Index].At;
                var here = t.localPosition;
                var to = target - here;
                to.y = 0f;
                var dist = to.magnitude;
                var speed = red ? 2.1f : amber ? 1.5f : 1.1f;
                var ahead = t.parent.InverseTransformPoint(eye) - here;
                ahead.y = 0f;
                var blocked = ahead.magnitude < 0.9f && dist > 0.01f && Vector3.Dot(ahead.normalized, to / dist) > 0.3f;
                if (blocked)
                {
                    body.Current = CrewExtra.Pose.Stand;
                    body.LookAt(eye);
                    w.Blocked += dt;
                    if (w.Blocked > 2.5f)
                    {
                        // Give way: turn back to the spot behind.
                        w.Step = -w.Step;
                        w.Index = Mathf.Clamp(w.Index + w.Step, 0, w.Ring.Length - 1);
                        w.Blocked = 0f;
                    }

                    continue;
                }

                w.Blocked = 0f;
                body.LookAt(null);
                if (dist < 0.05f)
                {
                    w.Moving = false;
                    var s = w.Ring[w.Index];
                    w.Dwell = amber ? Random.Range(2f, 5f) : Random.Range(4f, 12f);
                    if (!s.Post && red)
                        w.Step = NearestPostStep(w);
                    continue;
                }

                body.Current = CrewExtra.Pose.Walk;
                body.Speed = speed * Mathf.Clamp01(dist / 0.6f + 0.35f);
                t.localPosition = here + to / dist * Mathf.Min(dist, body.Speed * dt);
                t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.LookRotation(to / dist, Vector3.up), 1f - Mathf.Exp(-6f * dt));
            }
        }

        bool Taken(Walker self, int index)
        {
            foreach (var o in _walkers)
                if (o != self && o.Ring == self.Ring && o.Index == index)
                    return true;
            return false;
        }

        int NextIndex(Walker w)
        {
            var next = w.Index + w.Step;
            if (next < 0 || next >= w.Ring.Length)
            {
                w.Step = -w.Step;
                next = w.Index + w.Step;
            }
            else if (!w.PingPong && Random.value < 0.25f)
            {
                // Now and then a hand turns round instead of carrying on.
                w.Step = -w.Step;
                next = Mathf.Clamp(w.Index + w.Step, 0, w.Ring.Length - 1);
            }

            return next;
        }

        Vector3 BridgeScreen() => _bridge != null ? _bridge.TransformPoint(new Vector3(0f, 2f, 5.8f)) : Vector3.zero;
    }
}
