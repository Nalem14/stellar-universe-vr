using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Miniature focused-system scene inside Bridge hublot mounts.
    /// </summary>
    public class ViewportSystemView : MonoBehaviour
    {
        static readonly Color Cyan = new(0.25f, 0.92f, 1f, 1f);
        static readonly Color Amber = new(1f, 0.62f, 0.22f, 1f);

        FocusContext _focus;
        readonly List<Transform> _mounts = new();
        Shader _emissive;

        public void Bind(FocusContext focus, IReadOnlyList<Transform> mounts)
        {
            _focus = focus;
            _mounts.Clear();
            if (mounts != null)
            {
                foreach (var mount in mounts)
                {
                    if (mount != null)
                        _mounts.Add(mount);
                }
            }

            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.Changed += OnFocusChanged;
            }

            Rebuild();
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
        }

        void OnFocusChanged()
        {
            Rebuild();
        }

        public void Rebuild()
        {
            _emissive = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            foreach (var mount in _mounts)
            {
                if (mount == null)
                    continue;
                ClearChildren(mount);
                BuildInto(mount, _focus);
            }
        }

        void BuildInto(Transform mount, FocusContext focus)
        {
            // Backdrop starfield plate (local, fills the window).
            var backdrop = CreateQuad(mount, "Backdrop", Vector3.zero, new Vector3(1f, 1f, 1f),
                StarColor(focus != null ? focus.SystemType : 0) * 0.12f, 0.8f);
            backdrop.transform.localPosition = new Vector3(0f, 0f, 0.02f);

            if (focus == null || !focus.HasSystem)
            {
                CreateSphere(mount, "IdleStar", Vector3.zero, 0.12f, Cyan, 3.5f);
                return;
            }

            var starColor = StarColor(focus.SystemType);
            CreateSphere(mount, "Star", Vector3.zero, 0.16f, starColor, 5.5f);
            CreateSphere(mount, "StarGlow", new Vector3(0f, 0f, 0.01f), 0.28f, starColor * 0.45f, 2.2f);

            var maxSlot = 1;
            foreach (var planet in focus.Planets)
                maxSlot = Mathf.Max(maxSlot, Mathf.Max(1, planet.Slot));
            foreach (var rock in focus.Asteroids)
                maxSlot = Mathf.Max(maxSlot, Mathf.Max(1, rock.Slot));

            var orbitBase = 0.28f;
            var orbitStep = maxSlot > 4 ? 0.11f : 0.14f;
            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;

            foreach (var planet in focus.Planets)
            {
                var slot = Mathf.Max(1, planet.Slot);
                var radius = orbitBase + (slot - 1) * orbitStep;
                var angle = SlotAngle(slot, planet.Id);
                var pos = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.55f, -0.02f);
                var tint = planet.UserId > 0 && planet.UserId == owned
                    ? Cyan
                    : planet.UserId > 0
                        ? Amber
                        : new Color(0.55f, 0.7f, 0.85f);
                var size = 0.045f + 0.008f * Mathf.Clamp(slot, 1, 6);
                CreateSphere(mount, "Planet_" + planet.Id, pos, size, tint, planet.UserId > 0 ? 2.8f : 1.4f);

                // Thin orbit ring hint
                CreateOrbitRing(mount, "Orbit_" + planet.Id, radius, tint * 0.35f);
            }

            foreach (var rock in focus.Asteroids)
            {
                var slot = Mathf.Max(1, rock.Slot);
                var radius = orbitBase + (slot - 1) * orbitStep + 0.05f;
                var angle = SlotAngle(slot, rock.Id + 17);
                var pos = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.55f, -0.015f);
                CreateSphere(mount, "Asteroid_" + rock.Id, pos, 0.02f, new Color(0.65f, 0.55f, 0.4f), 1.1f);
            }

            var fleetIndex = 0;
            foreach (var fleet in focus.Fleets)
            {
                Vector3 pos;
                if (fleet.PlanetId > 0)
                {
                    var planet = FindPlanet(focus, fleet.PlanetId);
                    var slot = planet != null ? Mathf.Max(1, planet.Slot) : 1;
                    var radius = orbitBase + (slot - 1) * orbitStep;
                    var angle = SlotAngle(slot, fleet.Id) + 0.35f + fleetIndex * 0.2f;
                    pos = new Vector3(Mathf.Cos(angle) * (radius + 0.06f),
                        Mathf.Sin(angle) * (radius + 0.06f) * 0.55f, -0.04f);
                }
                else
                {
                    // Open space near the star
                    var angle = fleetIndex * 0.9f;
                    pos = new Vector3(Mathf.Cos(angle) * 0.2f, Mathf.Sin(angle) * 0.12f, -0.05f);
                }

                var mine = owned > 0 && fleet.UserId == owned;
                CreateSphere(mount, "Fleet_" + fleet.Id, pos, 0.018f,
                    mine ? Cyan : new Color(1f, 0.35f, 0.3f), mine ? 4f : 2.5f);
                fleetIndex++;
            }
        }

        static FocusPlanet FindPlanet(FocusContext focus, int planetId)
        {
            foreach (var planet in focus.Planets)
            {
                if (planet.Id == planetId)
                    return planet;
            }

            return null;
        }

        static float SlotAngle(int slot, int salt)
        {
            return (slot * 1.7f + (salt % 7) * 0.45f) % (Mathf.PI * 2f);
        }

        static Color StarColor(int type)
        {
            return (Mathf.Abs(type) % 5) switch
            {
                1 => new Color(0.45f, 0.7f, 1f),
                2 => new Color(1f, 0.45f, 0.35f),
                3 => new Color(1f, 0.85f, 0.55f),
                4 => new Color(0.75f, 0.55f, 1f),
                _ => new Color(1f, 0.92f, 0.65f)
            };
        }

        GameObject CreateSphere(Transform parent, string name, Vector3 localPos, float radius, Color tint,
            float emission)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * (radius * 2f);
            var col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(tint, emission);
            return go;
        }

        GameObject CreateQuad(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color tint,
            float emission)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(tint, emission);
            return go;
        }

        void CreateOrbitRing(Transform parent, string name, float radius, Color tint)
        {
            const int segments = 16;
            for (var i = 0; i < segments; i++)
            {
                var a0 = (i / (float)segments) * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius * 0.55f, 0.015f);
                var bead = CreateSphere(parent, name + "_" + i, pos, 0.006f, tint, 0.8f);
                bead.transform.localScale = new Vector3(0.012f, 0.012f, 0.012f);
            }
        }

        Material Mat(Color tint, float emissionMul)
        {
            var mat = new Material(_emissive != null ? _emissive : Shader.Find("Sprites/Default"));
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Emission"))
                mat.SetColor("_Emission", tint * Mathf.Max(0f, emissionMul) * 0.25f);
            if (mat.HasProperty("_EmissionMul"))
                mat.SetFloat("_EmissionMul", emissionMul);
            return mat;
        }

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }
    }
}
