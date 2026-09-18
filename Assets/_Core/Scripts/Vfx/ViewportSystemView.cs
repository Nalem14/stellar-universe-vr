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
        Texture _stars;

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
            _stars = Resources.Load<Texture2D>("CIC/ViewportStars");
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
            // Deep space behind the diorama (into the wall). Stage in front toward the player.
            // Mount +Z faces the player (glass is yawed 180°).
            CreateQuad(mount, "Backdrop", new Vector3(0f, 0f, -0.05f), new Vector3(1.08f, 1.08f, 1f),
                new Color(0.015f, 0.03f, 0.07f, 1f), 0.2f, _stars);
            CreateQuad(mount, "Starfield", new Vector3(0f, 0f, -0.04f), new Vector3(1.02f, 1.02f, 1f),
                new Color(0.65f, 0.8f, 1f, 1f), 2.2f, _stars);

            var stage = new GameObject("Stage");
            stage.transform.SetParent(mount, false);
            stage.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            stage.transform.localScale = Vector3.one * 0.95f;
            var stageT = stage.transform;

            if (focus == null || !focus.HasSystem)
            {
                CreateSphere(stageT, "IdleStar", Vector3.zero, 0.14f, Cyan, 4.5f);
                return;
            }

            var starColor = StarColor(focus.SystemTypeKey, focus.SystemType);
            CreateSphere(stageT, "Star", Vector3.zero, 0.18f, starColor, 8f);
            CreateSphere(stageT, "StarGlow", new Vector3(0f, 0f, 0.015f), 0.26f, starColor * 0.7f, 3.5f);

            var maxSlot = 1;
            foreach (var planet in focus.Planets)
                maxSlot = Mathf.Max(maxSlot, Mathf.Max(1, planet.Slot));
            foreach (var rock in focus.Asteroids)
                maxSlot = Mathf.Max(maxSlot, Mathf.Max(1, rock.Slot));

            var orbitBase = 0.26f;
            var orbitStep = maxSlot > 8 ? 0.045f : maxSlot > 4 ? 0.07f : 0.11f;
            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;

            foreach (var planet in focus.Planets)
            {
                var slot = Mathf.Max(1, planet.Slot);
                var radius = Mathf.Min(0.48f, orbitBase + (slot - 1) * orbitStep);
                var angle = SlotAngle(slot, planet.Id);
                var pos = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.55f, -0.03f);
                var tint = planet.UserId > 0 && planet.UserId == owned
                    ? Cyan
                    : planet.UserId > 0
                        ? Amber
                        : new Color(0.55f, 0.72f, 0.9f);
                var size = 0.05f + 0.006f * Mathf.Clamp(slot, 1, 8);
                CreateSphere(stageT, "Planet_" + planet.Id, pos, size, tint, planet.UserId > 0 ? 3.4f : 1.6f);
                CreateOrbitRing(stageT, "Orbit_" + planet.Id, radius, tint * 0.45f);
            }

            foreach (var rock in focus.Asteroids)
            {
                var slot = Mathf.Max(1, rock.Slot);
                var radius = Mathf.Min(0.5f, orbitBase + (slot - 1) * orbitStep + 0.04f);
                var angle = SlotAngle(slot, rock.Id + 17);
                var pos = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.55f, -0.02f);
                CreateSphere(stageT, "Asteroid_" + rock.Id, pos, 0.024f, new Color(0.7f, 0.58f, 0.42f), 1.3f);
            }

            var fleetIndex = 0;
            foreach (var fleet in focus.Fleets)
            {
                Vector3 pos;
                if (fleet.PlanetId > 0)
                {
                    var planet = FindPlanet(focus, fleet.PlanetId);
                    var slot = planet != null ? Mathf.Max(1, planet.Slot) : 1;
                    var radius = Mathf.Min(0.48f, orbitBase + (slot - 1) * orbitStep);
                    var angle = SlotAngle(slot, fleet.Id) + 0.35f + fleetIndex * 0.2f;
                    pos = new Vector3(Mathf.Cos(angle) * (radius + 0.05f),
                        Mathf.Sin(angle) * (radius + 0.05f) * 0.55f, -0.05f);
                }
                else
                {
                    var angle = fleetIndex * 0.85f;
                    pos = new Vector3(Mathf.Cos(angle) * 0.18f, Mathf.Sin(angle) * 0.11f, -0.06f);
                }

                var mine = owned > 0 && fleet.UserId == owned;
                CreateSphere(stageT, "Fleet_" + fleet.Id, pos, 0.022f,
                    mine ? Cyan : new Color(1f, 0.35f, 0.3f), mine ? 5f : 3f);
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

        static Color StarColor(string typeKey, int typeHash)
        {
            if (!string.IsNullOrEmpty(typeKey))
            {
                switch (typeKey.Trim().ToLowerInvariant())
                {
                    case "blue":
                    case "b":
                        return new Color(0.45f, 0.7f, 1f);
                    case "red":
                    case "r":
                        return new Color(1f, 0.45f, 0.35f);
                    case "yellow":
                    case "y":
                    case "white":
                    case "w":
                        return new Color(1f, 0.92f, 0.65f);
                    case "orange":
                    case "o":
                        return new Color(1f, 0.7f, 0.35f);
                    case "purple":
                    case "violet":
                        return new Color(0.75f, 0.55f, 1f);
                    case "green":
                    case "g":
                        return new Color(0.45f, 1f, 0.65f);
                }
            }

            return (Mathf.Abs(typeHash) % 5) switch
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
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(tint, emission, null);
            return go;
        }

        GameObject CreateQuad(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color tint,
            float emission, Texture tex)
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
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(tint, emission, tex);
            return go;
        }

        void CreateOrbitRing(Transform parent, string name, float radius, Color tint)
        {
            const int segments = 20;
            for (var i = 0; i < segments; i++)
            {
                var a0 = (i / (float)segments) * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius * 0.55f, 0.01f);
                CreateSphere(parent, name + "_" + i, pos, 0.005f, tint, 1.1f);
            }
        }

        Material Mat(Color tint, float emissionMul, Texture tex)
        {
            var mat = new Material(_emissive != null ? _emissive : Shader.Find("Sprites/Default"));
            if (tex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = tex;
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Emission"))
                mat.SetColor("_Emission", tint * Mathf.Max(0f, emissionMul) * 0.35f);
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
