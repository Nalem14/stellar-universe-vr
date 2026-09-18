using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// One shared system-scale exterior around the CIC. Windows look out onto this —
    /// not per-pane diorama clones.
    /// </summary>
    public class SystemExterior : MonoBehaviour
    {
        static readonly Color Cyan = new(0.25f, 0.92f, 1f, 1f);
        static readonly Color Amber = new(1f, 0.62f, 0.22f, 1f);

        public const float OrbitBase = 28f;
        public const float OrbitStep = 11f;

        FocusContext _focus;
        Shader _emissive;
        Texture _stars;
        int _builtSystemId = -1;
        Transform _content;
        readonly Dictionary<int, Transform> _planets = new();
        readonly Dictionary<int, Transform> _asteroids = new();
        readonly Dictionary<int, Transform> _fleets = new();
        readonly Dictionary<int, FleetMotion> _fleetMotion = new();

        struct FleetMotion
        {
            public Vector3 From;
            public Vector3 To;
            public double StartUnix;
            public double EndUnix;
        }

        public void Bind(FocusContext focus)
        {
            _focus = focus;
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.Changed += OnFocusChanged;
            }

            RebuildAll();
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
        }

        void OnFocusChanged()
        {
            if (_focus == null)
                return;
            if (_focus.SystemId != _builtSystemId)
                RebuildAll();
            else
                SyncFleets();
        }

        void Update()
        {
            if (_focus == null)
                return;
            TickFleetMotion();
        }

        public void RebuildAll()
        {
            _emissive = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            _stars = Resources.Load<Texture2D>("CIC/ViewportStars");
            EnsureContentRoot();
            ClearChildren(_content);
            _planets.Clear();
            _asteroids.Clear();
            _fleets.Clear();
            _fleetMotion.Clear();
            _builtSystemId = _focus != null ? _focus.SystemId : 0;

            // No sky dome mesh — camera clear + fog + star field objects are enough,
            // and an inverted sphere was washing windows to white under UnlitEmissive.
            BuildStar(_focus);
            if (_focus == null || !_focus.HasSystem)
                return;

            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            foreach (var planet in _focus.Planets)
            {
                var slot = Mathf.Max(1, planet.Slot);
                var pos = OrbitPosition(slot, planet.Id);
                var tint = planet.UserId > 0 && planet.UserId == owned
                    ? Cyan
                    : planet.UserId > 0
                        ? Amber
                        : new Color(0.55f, 0.72f, 0.9f);
                var size = 1.4f + 0.15f * Mathf.Clamp(slot, 1, 10);
                var go = CreateSphere("Planet_" + planet.Id, pos, size, tint, planet.UserId > 0 ? 2.8f : 1.4f);
                _planets[planet.Id] = go.transform;
            }

            foreach (var rock in _focus.Asteroids)
            {
                var slot = Mathf.Max(1, rock.Slot);
                var pos = OrbitPosition(slot, rock.Id + 31) * 1.05f;
                var go = CreateSphere("Asteroid_" + rock.Id, pos, 0.7f, new Color(0.7f, 0.58f, 0.42f), 1.1f);
                _asteroids[rock.Id] = go.transform;
            }

            SyncFleets();
        }

        void EnsureContentRoot()
        {
            if (_content != null)
                return;
            var go = new GameObject("ExteriorContent");
            go.transform.SetParent(transform, false);
            _content = go.transform;
        }

        public Vector3 ResolveFleetWorldPosition(FocusFleet fleet)
        {
            if (fleet == null)
                return transform.TransformPoint(new Vector3(OrbitBase * 0.55f, 0f, 0f));

            var now = UnixNow();
            if (_fleetMotion.TryGetValue(fleet.Id, out var motion) && motion.EndUnix > now)
            {
                var t = Mathf.Clamp01((float)((now - motion.StartUnix) /
                                              System.Math.Max(0.01, motion.EndUnix - motion.StartUnix)));
                return Vector3.Lerp(motion.From, motion.To, Smooth(t));
            }

            return IdleFleetPosition(fleet);
        }

        Vector3 IdleFleetPosition(FocusFleet fleet)
        {
            if (fleet.PlanetId > 0 && _planets.TryGetValue(fleet.PlanetId, out var planet))
            {
                var radial = (planet.position - transform.position).normalized;
                if (radial.sqrMagnitude < 0.01f)
                    radial = Vector3.right;
                var side = Vector3.Cross(Vector3.up, radial).normalized;
                return planet.position + radial * 3.2f + side * (1.2f + (fleet.Id % 5) * 0.55f);
            }

            if (fleet.AsteroidId > 0 && _asteroids.TryGetValue(fleet.AsteroidId, out var rock))
                return rock.position + Vector3.up * 1.5f + Vector3.right * 2f;

            // Open space — keep clear of the star body / glow so the CIC windows can see.
            var angle = (fleet.Id % 12) * 0.55f;
            var r = OrbitBase * 1.15f;
            return transform.TransformPoint(new Vector3(Mathf.Cos(angle) * r, 3f, Mathf.Sin(angle) * r));
        }

        void SyncFleets()
        {
            if (_focus == null)
                return;

            var seen = new HashSet<int>();
            var now = UnixNow();
            var ownedId = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;

            foreach (var fleet in _focus.Fleets)
            {
                seen.Add(fleet.Id);
                if (!_fleets.TryGetValue(fleet.Id, out var tf) || tf == null)
                {
                    var mine = ownedId > 0 && fleet.UserId == ownedId;
                    var go = CreateShipMarker("Fleet_" + fleet.Id, IdleFleetPosition(fleet),
                        mine ? Cyan : new Color(1f, 0.35f, 0.3f), mine);
                    tf = go.transform;
                    _fleets[fleet.Id] = tf;
                }

                if (fleet.DestTime > now)
                {
                    if (!_fleetMotion.TryGetValue(fleet.Id, out var motion) ||
                        System.Math.Abs(motion.EndUnix - fleet.DestTime) > 0.5)
                    {
                        var to = IdleFleetPosition(fleet);
                        if (fleet.FromSystemId > 0 && fleet.DestSystemId > 0 &&
                            fleet.FromSystemId != fleet.DestSystemId)
                        {
                            var edge = transform.TransformPoint(new Vector3(OrbitBase + OrbitStep * 8f, 2f, 0f));
                            if (fleet.DestSystemId == _focus.SystemId)
                                to = IdleFleetPosition(fleet);
                            else
                                to = edge;
                        }

                        _fleetMotion[fleet.Id] = new FleetMotion
                        {
                            From = tf.position,
                            To = to,
                            StartUnix = now,
                            EndUnix = fleet.DestTime
                        };
                    }
                }
                else
                {
                    _fleetMotion.Remove(fleet.Id);
                    tf.position = IdleFleetPosition(fleet);
                }

                var hide = _focus.ViewFleetId > 0 && fleet.Id == _focus.ViewFleetId;
                SetVisible(tf, !hide);
            }

            var remove = new List<int>();
            foreach (var kv in _fleets)
            {
                if (!seen.Contains(kv.Key))
                {
                    if (kv.Value != null)
                        Destroy(kv.Value.gameObject);
                    remove.Add(kv.Key);
                }
            }

            foreach (var id in remove)
            {
                _fleets.Remove(id);
                _fleetMotion.Remove(id);
            }
        }

        void TickFleetMotion()
        {
            var now = UnixNow();
            foreach (var fleet in _focus.Fleets)
            {
                if (!_fleets.TryGetValue(fleet.Id, out var tf) || tf == null)
                    continue;
                if (_focus.ViewFleetId == fleet.Id)
                {
                    SetVisible(tf, false);
                    continue;
                }

                if (_fleetMotion.TryGetValue(fleet.Id, out var motion) && motion.EndUnix > now)
                {
                    var t = Mathf.Clamp01((float)((now - motion.StartUnix) /
                                                  System.Math.Max(0.01, motion.EndUnix - motion.StartUnix)));
                    tf.position = Vector3.Lerp(motion.From, motion.To, Smooth(t));
                    var dir = motion.To - motion.From;
                    if (dir.sqrMagnitude > 0.01f)
                        tf.rotation = Quaternion.Slerp(tf.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up),
                            Time.deltaTime * 3f);
                }
                else
                {
                    var target = IdleFleetPosition(fleet);
                    tf.position = Vector3.Lerp(tf.position, target, Time.deltaTime * 2.5f);
                }
            }
        }

        void BuildStar(FocusContext focus)
        {
            var color = StarColor(focus != null ? focus.SystemTypeKey : null,
                focus != null ? focus.SystemType : 0);
            CreateSphere("Star", Vector3.zero, 4f, color, 7f);
            // Soft corona — keep small so it doesn't swallow nearby ships / CIC windows.
            CreateSphere("StarGlow", Vector3.zero, 5.5f, color * 0.4f, 1.8f);
            var lightGo = new GameObject("StarLight");
            lightGo.transform.SetParent(_content, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 2.8f;
            light.range = 180f;
            light.shadows = LightShadows.Soft;
        }

        public static Vector3 OrbitPosition(int slot, int salt)
        {
            var radius = OrbitBase + (Mathf.Max(1, slot) - 1) * OrbitStep;
            var angle = (slot * 1.7f + (salt % 7) * 0.45f) % (Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * 0.15f * radius * 0.05f,
                Mathf.Sin(angle) * radius);
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

        GameObject CreateShipMarker(string name, Vector3 worldPos, Color tint, bool owned)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_content, false);
            root.transform.position = worldPos;
            CreateSphereLocal(root.transform, "Core", Vector3.zero, owned ? 1.1f : 0.85f, tint, owned ? 4f : 2.5f);
            var nose = CreateSphereLocal(root.transform, "Nose", new Vector3(0f, 0f, 1.3f), 0.45f, tint * 0.85f, 2f);
            nose.transform.localScale = new Vector3(0.5f, 0.5f, 1.4f);
            return root;
        }

        GameObject CreateSphere(string name, Vector3 localPos, float radius, Color tint, float emission,
            Texture tex = null)
        {
            return CreateSphereLocal(_content, name, localPos, radius, tint, emission, tex);
        }

        GameObject CreateSphereLocal(Transform parent, string name, Vector3 localPos, float radius, Color tint,
            float emission, Texture tex = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * (radius * 2f);
            var col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(tint, emission, tex);
            return go;
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

        static void SetVisible(Transform root, bool visible)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        static double UnixNow()
        {
            return (System.DateTime.UtcNow - new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc))
                .TotalSeconds;
        }

        static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
