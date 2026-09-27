using System.Collections.Generic;
using Core.UI;
using UnityEngine;

namespace Core.Crew
{
    /// <summary>
    /// A standing crew member who is not at one of the six stations: works the auxiliary consoles or walks the
    /// ship. Same uniform and kit as the seated officers (species headgear included), but each limb is merged
    /// into one mesh (legs, arms, torso, trim, helmet, lit parts: eight draws) and posed procedurally —
    /// walking (legs and arms swing, a little bob), working at a console (hands on the desk, tapping, head
    /// down, glances), or standing. Feet at the root, facing +z.
    /// </summary>
    public sealed class CrewExtra : MonoBehaviour
    {
        public enum Pose
        {
            Stand,
            Walk,
            Work
        }

        Transform _chest;
        Transform _head;
        Transform _legL;
        Transform _legR;
        Transform _armL;
        Transform _armR;
        Renderer _probe;
        float _phase;
        float _stride;
        float _glance;
        float _glanceTarget;
        float _nextGlance;
        Vector3? _look;
        bool _posed;

        public Pose Current { get; set; } = Pose.Stand;
        /// <summary>Walking speed (m/s) drives the stride; typing speed rises in an alert.</summary>
        public float Speed { get; set; } = 1.1f;
        public float Urgency { get; set; }

        const float HipY = 0.92f;

        public static CrewExtra Build(Transform parent, string name, Color accent, CrewSpecies.Family family)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var x = root.gameObject.AddComponent<CrewExtra>();
            x._phase = Random.value * 10f;

            // Legs from the hip pivots.
            x._legL = Pivot(root, "LegL", new Vector3(-0.1f, HipY, 0f));
            x._legR = Pivot(root, "LegR", new Vector3(0.1f, HipY, 0f));
            foreach (var leg in new[] { x._legL, x._legR })
            {
                var parts = new List<(Vector3, float, Vector3, Vector3)>
                {
                    (new Vector3(0.14f, 0.46f, 0.15f), 0.06f, new Vector3(0f, -0.22f, 0f), Vector3.zero),
                    (new Vector3(0.11f, 0.44f, 0.12f), 0.05f, new Vector3(0f, -0.66f, 0f), Vector3.zero),
                    (new Vector3(0.12f, 0.07f, 0.24f), 0.03f, new Vector3(0f, -0.88f, 0.04f), Vector3.zero)
                };
                Group(leg, "Leg", parts, UiKit.Uniform, accent, 0.18f);
            }

            // Upper body.
            x._chest = Pivot(root, "Chest", new Vector3(0f, HipY + 0.02f, 0f));
            Group(x._chest, "Torso", new List<(Vector3, float, Vector3, Vector3)>
            {
                (new Vector3(0.34f, 0.16f, 0.24f), 0.06f, new Vector3(0f, 0.02f, 0f), Vector3.zero),
                (new Vector3(0.38f, 0.5f, 0.24f), 0.08f, new Vector3(0f, 0.32f, 0f), Vector3.zero),
                (new Vector3(0.1f, 0.08f, 0.1f), 0.04f, new Vector3(0f, 0.6f, 0f), Vector3.zero)
            }, UiKit.Uniform, accent, 0.25f);
            Group(x._chest, "Trim", new List<(Vector3, float, Vector3, Vector3)>
            {
                (new Vector3(0.46f, 0.1f, 0.2f), 0.05f, new Vector3(0f, 0.54f, 0f), Vector3.zero),
                (new Vector3(0.24f, 0.12f, 0.02f), 0.008f, new Vector3(0f, 0.42f, 0.125f), Vector3.zero)
            }, UiKit.Chassis, accent, 0.9f);

            x._armL = Pivot(x._chest, "ArmL", new Vector3(-0.25f, 0.52f, 0f));
            x._armR = Pivot(x._chest, "ArmR", new Vector3(0.25f, 0.52f, 0f));
            foreach (var arm in new[] { x._armL, x._armR })
                Group(arm, "Arm", new List<(Vector3, float, Vector3, Vector3)>
                {
                    (new Vector3(0.09f, 0.3f, 0.09f), 0.045f, new Vector3(0f, -0.15f, 0f), Vector3.zero),
                    (new Vector3(0.08f, 0.28f, 0.08f), 0.04f, new Vector3(0f, -0.43f, 0f), Vector3.zero),
                    (new Vector3(0.08f, 0.09f, 0.06f), 0.025f, new Vector3(0f, -0.61f, 0f), Vector3.zero)
                }, UiKit.Uniform, accent, 0.2f);

            // Head: helmet and dim headgear in one mesh, the visor and lit headgear in another.
            x._head = Pivot(x._chest, "Head", new Vector3(0f, 0.64f, 0f));
            var dim = new List<(Vector3, float, Vector3, Vector3)>
            {
                (new Vector3(0.21f, 0.25f, 0.25f), 0.1f, new Vector3(0f, 0.12f, 0f), Vector3.zero),
                (new Vector3(0.03f, 0.05f, 0.13f), 0.012f, new Vector3(0.115f, 0.15f, -0.01f), Vector3.zero)
            };
            var lit = new List<(Vector3, float, Vector3, Vector3)>
            {
                (new Vector3(0.18f, 0.07f, 0.05f), 0.022f, new Vector3(0f, 0.13f, 0.11f), Vector3.zero)
            };
            CrewSpecies.Dress(family, (n, size, radius, pos, euler, glow) =>
                (glow > 1f ? lit : dim).Add((size, radius, pos, euler)));
            Group(x._head, "Helmet", dim, UiKit.Chassis, accent, 0.35f);
            Group(x._head, "Glow", lit, UiKit.Chassis, accent, 2.4f);

            x._probe = x._chest.GetComponentInChildren<Renderer>();
            x._nextGlance = Time.time + Random.value * 4f;
            return x;
        }

        static Transform Pivot(Transform parent, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        static void Group(Transform parent, string name, List<(Vector3 size, float radius, Vector3 pos, Vector3 euler)> parts,
            Material mat, Color accent, float accentMul)
        {
            var combine = new CombineInstance[parts.Count];
            for (var i = 0; i < parts.Count; i++)
                combine[i] = new CombineInstance
                {
                    mesh = UiMeshes.RoundedBoxSource(parts[i].size, parts[i].radius),
                    transform = Matrix4x4.TRS(parts[i].pos, Quaternion.Euler(parts[i].euler), Vector3.one)
                };
            var mesh = new Mesh { name = "SU_CrewExtra_" + name };
            mesh.CombineMeshes(combine, true, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, accentMul);
            r.SetPropertyBlock(block);
        }

        /// <summary>Turn the head toward a world point (null = idle glances).</summary>
        public void LookAt(Vector3? world) => _look = world;

        void Update()
        {
            var dt = Time.deltaTime;
            var walking = Current == Pose.Walk;
            _stride = Mathf.MoveTowards(_stride, walking ? 1f : 0f, dt * 4f);
            // Off screen and still: keep the last pose (but take it once, so no one stands in a T at start).
            if (_posed && !walking && _probe != null && !_probe.isVisible)
                return;
            _posed = true;
            _phase += dt * (walking ? Speed * 5.2f : 1f);
            var t = Time.time;

            // Legs and arms swing with the stride; at rest the arms hang or work the console.
            var swing = Mathf.Sin(_phase) * 30f * _stride;
            _legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            float armL, armR, elbowOut;
            if (Current == Pose.Work)
            {
                // Hands forward on the desk, fingers tapping (faster in an alert).
                var tap = 1.5f + Urgency * 3f;
                armL = -52f + Mathf.Sin(t * 7f * tap + _phase) * 4f;
                armR = -52f + Mathf.Sin(t * 7f * tap + _phase + 1.7f) * 4f;
                elbowOut = 8f;
            }
            else
            {
                armL = -swing * 0.75f;
                armR = swing * 0.75f;
                elbowOut = 4f;
            }

            _armL.localRotation = Quaternion.Euler(armL, 0f, -elbowOut);
            _armR.localRotation = Quaternion.Euler(armR, 0f, elbowOut);
            var bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.025f * _stride + Mathf.Sin(t * 1.35f + _phase) * 0.004f;
            _chest.localPosition = new Vector3(0f, HipY + 0.02f + bob, 0f);
            _chest.localRotation = Quaternion.Euler(Current == Pose.Work ? 10f : 3f * _stride, Mathf.Sin(_phase) * 4f * _stride, 0f);

            // Head: toward a point when asked, else down at the console with a glance now and then.
            float yaw, pitch = Current == Pose.Work ? 16f : 0f;
            if (_look.HasValue)
            {
                var local = transform.InverseTransformPoint(_look.Value);
                yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f);
                pitch = 0f;
            }
            else
            {
                if (Time.time >= _nextGlance)
                {
                    _glanceTarget = Random.value < 0.65f ? 0f : Random.Range(-40f, 40f);
                    _nextGlance = Time.time + 2.5f + Random.value * 5f;
                }

                yaw = _glanceTarget;
            }

            _glance = Mathf.LerpAngle(_glance, yaw, 1f - Mathf.Exp(-3f * dt));
            _head.localRotation = Quaternion.Euler(pitch, _glance, 0f);
        }
    }
}
