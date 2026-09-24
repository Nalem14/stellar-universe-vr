using Core.UI;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Seated bridge officer built from shared rounded kit meshes (no Unity capsules): uniform fabric,
    /// role colour on seams / chest badge / helmet visor through one MaterialPropertyBlock per piece.
    /// Root sits on the seat top and faces +Z (its console). Idle life: breathing and slow glances,
    /// evaluated only while visible. <see cref="LookAt"/> lets dialogue turn the head to the captain.
    /// </summary>
    public sealed class CrewOfficer : MonoBehaviour
    {
        Transform _chest;
        Transform _head;
        Vector3 _chestRest;
        float _phase;
        float _glanceYaw;
        float _glanceTarget;
        float _nextGlance;
        Vector3? _lookWorld;
        Renderer _probe;

        public static Transform Build(Transform parent, Vector3 seatTopLocal, Color accent)
        {
            var root = new GameObject("Officer").transform;
            root.SetParent(parent, false);
            root.localPosition = seatTopLocal;

            // Legs, seated: thighs forward, shins down to the deck, boots.
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 0.1f;
                Part(root, "Thigh", new Vector3(0.14f, 0.13f, 0.44f), 0.06f, new Vector3(x, 0.07f, 0.2f), Vector3.zero, accent, 0.18f);
                Part(root, "Shin", new Vector3(0.11f, 0.44f, 0.12f), 0.05f, new Vector3(x, -0.2f, 0.4f), Vector3.zero, accent, 0.18f);
                Part(root, "Boot", new Vector3(0.12f, 0.07f, 0.22f), 0.03f, new Vector3(x, -0.43f, 0.46f), Vector3.zero, accent, 0.05f, UiKit.Chassis);
            }

            Part(root, "Pelvis", new Vector3(0.34f, 0.14f, 0.28f), 0.06f, new Vector3(0f, 0.07f, -0.02f), Vector3.zero, accent, 0.2f);

            // Upper body leans slightly toward the console; arms rest on the desk edge.
            var chest = new GameObject("Chest").transform;
            chest.SetParent(root, false);
            chest.localPosition = new Vector3(0f, 0.14f, -0.03f);
            chest.localRotation = Quaternion.Euler(8f, 0f, 0f);

            Part(chest, "Torso", new Vector3(0.38f, 0.48f, 0.24f), 0.08f, new Vector3(0f, 0.24f, 0f), Vector3.zero, accent, 0.25f);
            Part(chest, "Badge", new Vector3(0.24f, 0.12f, 0.02f), 0.008f, new Vector3(0f, 0.34f, 0.125f), Vector3.zero, accent, 1.8f, UiKit.Chassis);
            Part(chest, "Shoulders", new Vector3(0.46f, 0.1f, 0.2f), 0.05f, new Vector3(0f, 0.46f, 0f), Vector3.zero, accent, 0.45f, UiKit.Chassis);
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 0.25f;
                Part(chest, "UpperArm", new Vector3(0.09f, 0.3f, 0.09f), 0.045f, new Vector3(x, 0.32f, 0.06f), new Vector3(-28f, 0f, 0f), accent, 0.2f);
                Part(chest, "Forearm", new Vector3(0.08f, 0.08f, 0.3f), 0.04f, new Vector3(side * 0.22f, 0.2f, 0.26f), new Vector3(10f, -side * 8f, 0f), accent, 0.2f);
                Part(chest, "Glove", new Vector3(0.08f, 0.05f, 0.09f), 0.02f, new Vector3(side * 0.2f, 0.17f, 0.43f), Vector3.zero, accent, 0.08f, UiKit.Chassis);
            }

            Part(chest, "Neck", new Vector3(0.1f, 0.08f, 0.1f), 0.04f, new Vector3(0f, 0.54f, 0f), Vector3.zero, accent, 0.1f);
            var head = new GameObject("Head").transform;
            head.SetParent(chest, false);
            head.localPosition = new Vector3(0f, 0.58f, 0f);
            Part(head, "Helmet", new Vector3(0.21f, 0.25f, 0.25f), 0.1f, new Vector3(0f, 0.12f, 0f), Vector3.zero, accent, 0.35f, UiKit.Chassis);
            Part(head, "Visor", new Vector3(0.18f, 0.07f, 0.05f), 0.022f, new Vector3(0f, 0.13f, 0.11f), Vector3.zero, accent, 2.2f, UiKit.Chassis);
            Part(head, "Comm", new Vector3(0.03f, 0.05f, 0.13f), 0.012f, new Vector3(0.115f, 0.15f, -0.01f), Vector3.zero, accent, 1.2f, UiKit.Chassis);

            var officer = root.gameObject.AddComponent<CrewOfficer>();
            officer._chest = chest;
            officer._chestRest = chest.localPosition;
            officer._head = head;
            officer._phase = Random.value * 10f;
            officer._nextGlance = Time.time + 2f + Random.value * 4f;
            officer._probe = chest.GetComponentInChildren<Renderer>();
            return root;
        }

        /// <summary>Turn the head toward a world point (null = back to idle glances).</summary>
        public void LookAt(Vector3? world) => _lookWorld = world;

        void Update()
        {
            if (_probe != null && !_probe.isVisible)
                return;
            var t = Time.time + _phase;
            _chest.localPosition = _chestRest + new Vector3(0f, Mathf.Sin(t * 1.35f) * 0.004f, 0f);

            float yaw;
            if (_lookWorld.HasValue)
            {
                var local = transform.InverseTransformPoint(_lookWorld.Value);
                yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -75f, 75f);
            }
            else
            {
                if (Time.time >= _nextGlance)
                {
                    _glanceTarget = Random.value < 0.6f ? 0f : Random.Range(-35f, 35f);
                    _nextGlance = Time.time + 2.5f + Random.value * 5f;
                }

                yaw = _glanceTarget;
            }

            _glanceYaw = Mathf.LerpAngle(_glanceYaw, yaw, 1f - Mathf.Exp(-3f * Time.deltaTime));
            _head.localRotation = Quaternion.Euler(-4f, _glanceYaw, 0f);
        }

        static void Part(Transform parent, string name, Vector3 size, float radius, Vector3 pos, Vector3 euler,
            Color accent, float accentMul, Material mat = null)
        {
            var go = UiKit.MeshPiece(parent, name, UiMeshes.RoundedBox(size, radius), mat != null ? mat : UiKit.Uniform, pos);
            go.transform.localRotation = Quaternion.Euler(euler);
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, accentMul);
            go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }
    }
}
