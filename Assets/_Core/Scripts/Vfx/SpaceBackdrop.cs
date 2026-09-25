using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The distant star sky behind every window (bridge hublots, the diplomacy chamber's bays): one mesh of small
    /// glowing quads on a sphere that follows the eye (never its rotation), a denser band across it for the
    /// galactic plane. Drawn in the Background queue with no depth write, so the system exterior, hulls and
    /// rooms all draw over it: one draw call, a few thousand tiny quads, no full-screen pass (Quest fill-rate).
    /// Decorative only — not the universe (fixed seed, no gameplay meaning).
    /// </summary>
    public sealed class SpaceBackdrop : MonoBehaviour
    {
        const int Stars = 1800;
        const float Radius = 1000f;
        const int Seed = 90210;

        static SpaceBackdrop _instance;
        Material _mat;
        Mesh _mesh;

        public static void Ensure(Transform parent)
        {
            if (_instance != null)
                return;
            var go = new GameObject("SpaceBackdrop");
            go.transform.SetParent(parent, false);
            _instance = go.AddComponent<SpaceBackdrop>();
            _instance.Build();
        }

        void Build()
        {
            _mesh = new Mesh { name = "SU_StarSky" };
            var v = new Vector3[Stars * 4];
            var uv = new Vector2[Stars * 4];
            var col = new Color[Stars * 4];
            var tris = new int[Stars * 6];
            var rnd = new System.Random(Seed);
            // Galactic plane: tilted great circle; 45 % of the stars crowd within a few degrees of it.
            var bandNormal = Quaternion.Euler(62f, 18f, 0f) * Vector3.up;
            for (var i = 0; i < Stars; i++)
            {
                Vector3 dir;
                if (rnd.NextDouble() < 0.45)
                {
                    var along = Quaternion.AngleAxis((float)rnd.NextDouble() * 360f, bandNormal) *
                                Vector3.Cross(bandNormal, Vector3.right).normalized;
                    var spread = (float)(Gauss(rnd) * 7.0);
                    dir = Quaternion.AngleAxis(spread, Vector3.Cross(bandNormal, along)) * along;
                }
                else
                {
                    var z = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    var a = (float)(rnd.NextDouble() * Mathf.PI * 2.0);
                    var r = Mathf.Sqrt(1f - z * z);
                    dir = new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
                }

                dir.Normalize();
                // Mostly faint pin-points, a few bright ones.
                var t = (float)rnd.NextDouble();
                var size = Radius * Mathf.Deg2Rad * Mathf.Lerp(0.1f, 0.26f, t * t) * (t > 0.97f ? 1.8f : 1f);
                var bright = Mathf.Lerp(0.18f, 0.75f, t * t * t) + (t > 0.97f ? 0.3f : 0f);
                var hue = (float)rnd.NextDouble();
                var tint = hue < 0.15f ? new Color(1f, 0.78f, 0.55f) : hue < 0.45f ? new Color(0.72f, 0.84f, 1f) : Color.white;
                tint.a = Mathf.Clamp01(bright);

                var right = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized * size;
                var up = Vector3.Cross(right, dir).normalized * size;
                var c = dir * Radius;
                var b = i * 4;
                v[b] = c - right - up;
                v[b + 1] = c - right + up;
                v[b + 2] = c + right + up;
                v[b + 3] = c + right - up;
                uv[b] = new Vector2(0f, 0f);
                uv[b + 1] = new Vector2(0f, 1f);
                uv[b + 2] = new Vector2(1f, 1f);
                uv[b + 3] = new Vector2(1f, 0f);
                for (var k = 0; k < 4; k++)
                    col[b + k] = tint;
                var ti = i * 6;
                tris[ti] = b;
                tris[ti + 1] = b + 1;
                tris[ti + 2] = b + 2;
                tris[ti + 3] = b;
                tris[ti + 4] = b + 2;
                tris[ti + 5] = b + 3;
            }

            _mesh.vertices = v;
            _mesh.uv = uv;
            _mesh.colors = col;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * Radius * 2.2f);

            // Own copy of the shared glow (additive, no depth write), pushed to the back of the frame.
            _mat = new Material(CombatFxKit.Glow()) { name = "SU_StarSky", renderQueue = 1000 };
            if (_mat.HasProperty("_Color"))
                _mat.SetColor("_Color", Color.white);
            if (_mat.HasProperty("_EmissionMul"))
                _mat.SetFloat("_EmissionMul", 1.4f);

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static double Gauss(System.Random rnd)
        {
            var u1 = 1.0 - rnd.NextDouble();
            var u2 = rnd.NextDouble();
            return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
                transform.SetPositionAndRotation(cam.transform.position, Quaternion.identity);
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            if (_mat != null)
                Destroy(_mat);
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
