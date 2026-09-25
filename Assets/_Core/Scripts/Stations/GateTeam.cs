using System.Collections.Generic;
using Core.UI;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// What goes through the gate when a mission is dispatched, played out on the causeway: a squad of
    /// armoured troopers jogging up in two files (attack / pillage / explore / troop transfer), hover
    /// pallets of crates (resources, a raid's empty sleds), a colonist convoy. Built from the crew's rounded
    /// kit meshes (shared materials, accent per piece through one property block), walked procedurally
    /// (legs, arms, bob — no Animator), and each one ripples the horizon as it steps in and is gone.
    /// </summary>
    public sealed class GateWalker : MonoBehaviour
    {
        const float JogSpeed = 1.9f;
        const float SledSpeed = 1.4f;

        readonly List<Vector3> _path = new();
        GateRing _gate;
        Transform _legL, _legR, _armL, _armR, _body;
        bool _sled;
        float _delay;
        float _phase;
        int _leg;
        float _speed;

        /// <summary>Spawn one team for <paramref name="missionType"/> along <paramref name="path"/> (room local → world).</summary>
        public static int Dispatch(Transform room, GateRing gate, string missionType, int troopQty, IReadOnlyList<Vector3> path)
        {
            var troopers = 0;
            var sleds = 0;
            var accent = new Color(0.35f, 0.9f, 1f, 1f);
            switch (missionType)
            {
                case "attack":
                    troopers = Mathf.Clamp(Mathf.CeilToInt(troopQty / 8f), 4, 8);
                    accent = new Color(1f, 0.4f, 0.25f, 1f);
                    break;
                case "pillage":
                    troopers = Mathf.Clamp(Mathf.CeilToInt(troopQty / 10f), 3, 6);
                    sleds = 1;
                    accent = new Color(1f, 0.62f, 0.2f, 1f);
                    break;
                case "explore":
                    troopers = Mathf.Clamp(Mathf.CeilToInt(troopQty / 10f), 2, 4);
                    accent = new Color(0.45f, 1f, 0.6f, 1f);
                    break;
                case "sendTroops":
                    troopers = Mathf.Clamp(Mathf.CeilToInt(troopQty / 10f), 2, 8);
                    break;
                case "sendResources":
                    sleds = 3;
                    accent = new Color(1f, 0.72f, 0.35f, 1f);
                    break;
                case "colonize":
                    troopers = 3;
                    sleds = 2;
                    accent = new Color(0.85f, 0.92f, 1f, 1f);
                    break;
            }

            var n = 0;
            for (var i = 0; i < troopers; i++, n++)
                Spawn(room, gate, path, false, accent, n * 0.55f, (i % 2 == 0 ? -1f : 1f) * 0.42f);
            for (var i = 0; i < sleds; i++, n++)
                Spawn(room, gate, path, true, accent, n * 0.55f + 0.3f, 0f);
            return n;
        }

        static void Spawn(Transform room, GateRing gate, IReadOnlyList<Vector3> path, bool sled, Color accent, float delay,
            float lane)
        {
            var go = new GameObject(sled ? "GateSled" : "GateTrooper");
            go.transform.SetParent(room, false);
            var w = go.AddComponent<GateWalker>();
            w._gate = gate;
            w._sled = sled;
            w._delay = delay;
            w._speed = sled ? SledSpeed : JogSpeed * Random.Range(0.94f, 1.06f);
            w._phase = Random.value * 6f;
            // Two files on the causeway: offset each waypoint sideways (path runs along +z).
            foreach (var p in path)
                w._path.Add(p + new Vector3(lane, 0f, 0f));
            go.transform.localPosition = w._path[0];
            if (sled)
                w.BuildSled(accent);
            else
                w.BuildTrooper(accent);
            go.SetActive(delay <= 0f);
            if (delay > 0f)
                room.GetComponent<MonoBehaviour>()?.StartCoroutine(w.WakeLater(delay));
        }

        System.Collections.IEnumerator WakeLater(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (this != null)
                gameObject.SetActive(true);
        }

        // ── Bodies ────────────────────────────────────────────────────────────────

        static void Part(Transform parent, string name, Vector3 size, float radius, Vector3 pos, Color accent, float mul,
            Material mat = null)
        {
            var go = UiKit.MeshPiece(parent, name, UiMeshes.RoundedBox(size, radius), mat != null ? mat : UiKit.Uniform, pos);
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, mul);
            var r = go.GetComponent<MeshRenderer>();
            r.SetPropertyBlock(block);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform Pivot(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        void BuildTrooper(Color accent)
        {
            _body = Pivot(transform, "Body", Vector3.zero);
            // Armour bulk: a touch broader and deeper than the seated crew.
            _body.localScale = new Vector3(1.14f, 1f, 1.12f);
            for (var side = -1; side <= 1; side += 2)
            {
                var hip = Pivot(_body, side < 0 ? "LegL" : "LegR", new Vector3(side * 0.11f, 0.92f, 0f));
                Part(hip, "Thigh", new Vector3(0.15f, 0.46f, 0.17f), 0.06f, new Vector3(0f, -0.23f, 0f), accent, 0.18f);
                Part(hip, "KneePad", new Vector3(0.13f, 0.1f, 0.06f), 0.03f, new Vector3(0f, -0.45f, 0.08f), accent, 0.6f,
                    UiKit.Chassis);
                Part(hip, "Shin", new Vector3(0.12f, 0.42f, 0.14f), 0.05f, new Vector3(0f, -0.66f, 0f), accent, 0.18f);
                Part(hip, "Boot", new Vector3(0.13f, 0.09f, 0.25f), 0.03f, new Vector3(0f, -0.88f, 0.04f), accent, 0.05f,
                    UiKit.Chassis);
                if (side < 0) _legL = hip;
                else _legR = hip;
            }

            Part(_body, "Pelvis", new Vector3(0.34f, 0.16f, 0.24f), 0.06f, new Vector3(0f, 0.95f, 0f), accent, 0.2f);
            Part(_body, "Torso", new Vector3(0.4f, 0.5f, 0.26f), 0.08f, new Vector3(0f, 1.28f, 0.01f), accent, 0.25f);
            Part(_body, "ChestPlate", new Vector3(0.34f, 0.26f, 0.06f), 0.03f, new Vector3(0f, 1.34f, 0.15f), accent, 0.5f,
                UiKit.Chassis);
            Part(_body, "Pack", new Vector3(0.32f, 0.4f, 0.18f), 0.06f, new Vector3(0f, 1.3f, -0.21f), accent, 0.3f,
                UiKit.Chassis);
            Part(_body, "PackLight", new Vector3(0.18f, 0.03f, 0.02f), 0.01f, new Vector3(0f, 1.42f, -0.305f), accent, 2.4f,
                UiKit.Chassis);
            for (var side = -1; side <= 1; side += 2)
            {
                var shoulder = Pivot(_body, side < 0 ? "ArmL" : "ArmR", new Vector3(side * 0.26f, 1.48f, 0f));
                Part(shoulder, "Pauldron", new Vector3(0.16f, 0.1f, 0.2f), 0.04f, new Vector3(side * 0.02f, 0.02f, 0f), accent,
                    0.5f, UiKit.Chassis);
                Part(shoulder, "UpperArm", new Vector3(0.1f, 0.3f, 0.11f), 0.045f, new Vector3(0f, -0.17f, 0f), accent, 0.2f);
                Part(shoulder, "Forearm", new Vector3(0.09f, 0.09f, 0.3f), 0.04f, new Vector3(-side * 0.04f, -0.32f, 0.13f),
                    accent, 0.2f);
                if (side < 0) _armL = shoulder;
                else _armR = shoulder;
            }

            // Rifle held across the chest, glowing muzzle and sight.
            Part(_armR, "Rifle", new Vector3(0.06f, 0.1f, 0.62f), 0.02f, new Vector3(-0.2f, -0.3f, 0.3f), accent, 0.08f,
                UiKit.Chassis);
            Part(_armR, "Sight", new Vector3(0.03f, 0.03f, 0.08f), 0.01f, new Vector3(-0.2f, -0.23f, 0.22f), accent, 2.6f,
                UiKit.Chassis);

            Part(_body, "Neck", new Vector3(0.1f, 0.08f, 0.1f), 0.04f, new Vector3(0f, 1.58f, 0f), accent, 0.1f);
            Part(_body, "Helmet", new Vector3(0.24f, 0.27f, 0.28f), 0.1f, new Vector3(0f, 1.74f, 0f), accent, 0.35f, UiKit.Chassis);
            Part(_body, "Visor", new Vector3(0.2f, 0.07f, 0.05f), 0.022f, new Vector3(0f, 1.75f, 0.13f), accent, 2.4f,
                UiKit.Chassis);
        }

        void BuildSled(Color accent)
        {
            _body = Pivot(transform, "Body", new Vector3(0f, 0.45f, 0f));
            Part(_body, "Deck", new Vector3(1.1f, 0.14f, 1.4f), 0.05f, Vector3.zero, accent, 0.3f, UiKit.Chassis);
            Part(_body, "Skirt", new Vector3(1.0f, 0.1f, 1.3f), 0.04f, new Vector3(0f, -0.1f, 0f), accent, 1.6f, UiKit.Chassis);
            Part(_body, "Crate0", new Vector3(0.5f, 0.42f, 0.55f), 0.05f, new Vector3(-0.26f, 0.28f, -0.32f), accent, 0.25f);
            Part(_body, "Crate1", new Vector3(0.5f, 0.42f, 0.55f), 0.05f, new Vector3(0.26f, 0.28f, -0.32f), accent, 0.25f);
            Part(_body, "Crate2", new Vector3(0.9f, 0.34f, 0.5f), 0.05f, new Vector3(0f, 0.24f, 0.36f), accent, 0.25f);
            Part(_body, "Strap", new Vector3(0.96f, 0.04f, 0.04f), 0.01f, new Vector3(0f, 0.44f, 0.36f), accent, 2f, UiKit.Chassis);
            Part(_body, "Beacon", new Vector3(0.08f, 0.08f, 0.08f), 0.03f, new Vector3(0f, 0.56f, -0.32f), accent, 3f, UiKit.Chassis);
        }

        // ── Walk ──────────────────────────────────────────────────────────────────

        void Update()
        {
            if (_leg >= _path.Count - 1)
                return;
            var dt = Time.deltaTime;
            var to = _path[_leg + 1];
            var pos = transform.localPosition;
            var step = Vector3.MoveTowards(pos, to, _speed * dt);
            var dir = to - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
                transform.localRotation = Quaternion.Slerp(transform.localRotation,
                    Quaternion.LookRotation(dir.normalized, Vector3.up), dt * 8f);
            transform.localPosition = step;

            _phase += dt * _speed * (_sled ? 1.2f : 5.2f);
            if (_sled)
            {
                _body.localPosition = new Vector3(0f, 0.45f + Mathf.Sin(_phase) * 0.03f, 0f);
            }
            else
            {
                var swing = Mathf.Sin(_phase) * 32f;
                _legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
                _legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                _armL.localRotation = Quaternion.Euler(-swing * 0.6f - 18f, 0f, 0f);
                _armR.localRotation = Quaternion.Euler(swing * 0.25f - 30f, 0f, 0f);
                _body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Cos(_phase)) * 0.05f, 0f);
                _body.localRotation = Quaternion.Euler(6f, 0f, 0f);
            }

            if ((transform.localPosition - to).sqrMagnitude < 1e-4f)
            {
                _leg++;
                // The last leg ends inside the horizon: ripple where they stepped in, then gone.
                if (_leg >= _path.Count - 1)
                {
                    _gate?.Ripple(transform.position + Vector3.up * (_sled ? 0.6f : 1.2f), _sled ? 1.4f : 1f);
                    CicCue.Zap(transform.position, _sled ? 0.7f : 1.1f);
                    Destroy(gameObject);
                }
            }
        }
    }
}
