using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Feeds up to four of the room's point lights to SU/HullInterior as globals (URP additional lights stay
    /// off on Quest). Reads the Light components themselves every frame, so whatever drives them — red alert,
    /// hull hits, blackout (<see cref="BridgeCombatFx"/>) — shades the walls too. Two SetGlobalVectorArray a frame, by the room the player is in.
    /// </summary>
    public sealed class RoomLightRig : MonoBehaviour
    {
        static readonly int PosId = Shader.PropertyToID("_SU_RoomLightPos");
        static readonly int ColId = Shader.PropertyToID("_SU_RoomLightCol");

        Light[] _lights;

        /// <summary>Only the room the player's eyes are in writes the globals (rooms share one set).</summary>
        public float Radius = 10f;
        readonly Vector4[] _pos = new Vector4[4];
        readonly Vector4[] _col = new Vector4[4];

        public static RoomLightRig Attach(Transform room, params string[] lightNames)
        {
            var rig = room.gameObject.AddComponent<RoomLightRig>();
            rig._lights = new Light[Mathf.Min(4, lightNames.Length)];
            for (var i = 0; i < rig._lights.Length; i++)
            {
                var t = room.Find(lightNames[i]);
                rig._lights[i] = t != null ? t.GetComponent<Light>() : null;
            }

            return rig;
        }

        void LateUpdate()
        {
            var eye = Camera.main;
            if (eye == null || _lights == null || _lights.Length == 0)
                return;
            var centre = Vector3.zero;
            var count = 0;
            foreach (var l in _lights)
                if (l != null)
                {
                    centre += l.transform.position;
                    count++;
                }

            if (count == 0 || (eye.transform.position - centre / count).sqrMagnitude > Radius * Radius)
                return;

            for (var i = 0; i < 4; i++)
            {
                var l = _lights != null && i < _lights.Length ? _lights[i] : null;
                if (l == null || !l.isActiveAndEnabled)
                {
                    _pos[i] = new Vector4(0f, -9999f, 0f, 1f);
                    _col[i] = Vector4.zero;
                    continue;
                }

                var p = l.transform.position;
                _pos[i] = new Vector4(p.x, p.y, p.z, 1f / Mathf.Max(0.01f, l.range * l.range));
                var c = l.color * l.intensity;
                _col[i] = new Vector4(c.r, c.g, c.b, 0f);
            }

            Shader.SetGlobalVectorArray(PosId, _pos);
            Shader.SetGlobalVectorArray(ColId, _col);
        }
    }
}
