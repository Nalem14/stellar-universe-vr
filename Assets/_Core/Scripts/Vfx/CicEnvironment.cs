using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Core.Vfx
{
    public enum CicLayout
    {
        BootVoid,
        MenuDeck,
        Bridge
    }

    public class CicEnvironment : MonoBehaviour
    {
        public CicLayout Layout = CicLayout.Bridge;

        CicArtKit _art;

        public GameObject Table { get; set; }
        public Transform ConsoleMount { get; private set; }
        public HoloZoneMap ZoneMap { get; private set; }
        public CicArtKit Art => _art;
        public IReadOnlyList<Transform> HublotMounts => _hublotMounts;

        readonly List<Transform> _hublotMounts = new();

        public void Build()
        {
            _hublotMounts.Clear();
            ZoneMap = null;
            Table = null;
            _art = new CicArtKit();
            _art.Load();
            ApplyAtmosphere();
            StripTemplateJunk();
            switch (Layout)
            {
                case CicLayout.BootVoid:
                    BuildBootVoid();
                    break;
                case CicLayout.MenuDeck:
                    BuildRoom(size: 9f, hublots: 0, ceiling: 3.4f);
                    BuildMenuObservatory();
                    break;
                default:
                    ZoneMap = CicBridgeInterior.Build(this, _art);
                    break;
            }

            SpawnDust();
            EnsureVolume();
        }

        void ApplyAtmosphere()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.01f, 0.02f, 0.04f);
            RenderSettings.fogDensity = Layout == CicLayout.BootVoid ? 0.04f :
                Layout == CicLayout.MenuDeck ? 0.028f : WorldScale.BridgeFogDensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Layout == CicLayout.Bridge
                ? new Color(0.12f, 0.16f, 0.22f)
                : new Color(0.025f, 0.04f, 0.055f);
            var far = Layout == CicLayout.Bridge ? WorldScale.BridgeFarClip : 40f;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null)
                    continue;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.008f, 0.012f, 0.02f);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, far);
            }
        }

        void StripTemplateJunk()
        {
            foreach (var name in new[]
                     {
                         "CoachingCardRoot", "Interactables", "Plane", "Environment",
                         "Teleport Area Setup", "AR Session", "XR Simulation"
                     })
            {
                var found = GameObject.Find(name);
                if (found != null)
                    found.SetActive(false);
            }

            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null)
                    continue;
                var n = t.name;
                if (n.IndexOf("Coaching", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Passthrough", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Teleport Area", StringComparison.OrdinalIgnoreCase) >= 0)
                    t.gameObject.SetActive(false);
            }

            if (Layout != CicLayout.Bridge)
                return;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null)
                    continue;
                var n = t.name;
                if (n.Equals("Teleportation", StringComparison.Ordinal) ||
                    n.IndexOf("Teleport Interactor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Climb Teleport", StringComparison.OrdinalIgnoreCase) >= 0)
                    t.gameObject.SetActive(false);
            }

            // Bridge uses keyed point lights + emissives — kill template directional fill.
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light == null || light.type != LightType.Directional)
                    continue;
                light.intensity = 0.04f;
                light.color = new Color(0.35f, 0.45f, 0.55f);
            }
        }

        void BuildBootVoid()
        {
            var deck = Quad("BootDeck", new Vector3(0f, 0f, 0f), new Vector3(12f, 12f, 1f), null, Color.clear, 0f,
                rotateX: 90f, keepCollider: true);
            var deckRenderer = deck.GetComponent<MeshRenderer>();
            if (deckRenderer != null)
                deckRenderer.enabled = false;

            var sky = Quad("Nebula", new Vector3(0f, 1.4f, 6.5f), new Vector3(9.6f, 5.4f, 1f), _art.Stars,
                CicArtKit.Cyan * 0.15f, 1.1f);
            sky.transform.LookAt(Vector3.zero);

            if (_art.Title != null)
            {
                var plate = Quad("TitleHolo", new Vector3(0f, 1.55f, 2.4f), new Vector3(2.4f, 1.35f, 1f),
                    _art.Title, Color.white, 1.8f);
                var holo = plate.AddComponent<HoloSpin>();
                holo.DegreesPerSecond = 0f;
                holo.BobMeters = 0.04f;
            }

            KeyLight("Key", new Vector3(0.4f, 2.2f, -1.2f), CicArtKit.Cyan, 2.4f, 8f);
            KeyLight("Rim", new Vector3(-1.6f, 1.8f, 1.5f), CicArtKit.Amber, 1.1f, 7f);
        }

        void BuildRoom(float size, int hublots, float ceiling)
        {
            var half = size * 0.5f;
            var wallH = ceiling;
            var wallMid = wallH * 0.5f;

            Quad("Deck", new Vector3(0f, 0f, 0f), new Vector3(size, size, 1f), _art.Floor, Color.white, 0.14f,
                tiling: 4f, rotateX: 90f, keepCollider: true);
            Quad("Overhead", new Vector3(0f, wallH, 0f), new Vector3(size, size, 1f), _art.Wall,
                new Color(0.12f, 0.15f, 0.18f), 0.06f, tiling: 3f, rotateX: -90f);

            TrimRing("FloorTrim", 0.04f, size - 0.2f, CicArtKit.Cyan * 0.55f, 1.8f);
            TrimRing("CeilTrim", wallH - 0.04f, size - 0.2f, CicArtKit.Amber * 0.4f, 1.2f);

            Box("Fwd", new Vector3(0f, wallMid, half), new Vector3(size + 0.2f, wallH, 0.18f),
                _art.MetalPanel(0.08f), keepCollider: true);
            Box("Aft", new Vector3(0f, wallMid, -half), new Vector3(size + 0.2f, wallH, 0.18f),
                _art.MetalPanel(0.08f), keepCollider: true);
            Box("Port", new Vector3(-half, wallMid, 0f), new Vector3(0.18f, wallH, size + 0.2f),
                _art.MetalPanel(0.08f), keepCollider: true);
            Box("Starboard", new Vector3(half, wallMid, 0f), new Vector3(0.18f, wallH, size + 0.2f),
                _art.MetalPanel(0.08f), keepCollider: true);

            float rib = half - 0.12f;
            foreach (var xz in new[]
                     {
                         new Vector3(rib, wallMid, rib), new Vector3(-rib, wallMid, rib),
                         new Vector3(rib, wallMid, -rib), new Vector3(-rib, wallMid, -rib)
                     })
            {
                Box("Rib", xz, new Vector3(0.14f, wallH, 0.14f), _art.DarkPanel(0.05f), keepCollider: true);
            }

            for (var i = 0; i < hublots; i++)
            {
                var t = (i + 1f) / (hublots + 1f);
                var x = Mathf.Lerp(-half + 1.4f, half - 1.4f, t);
                Viewport(new Vector3(x, 1.65f, half - 0.04f), new Vector3(1.8f, 1.1f, 1f));
            }

            StripLight(new Vector3(0f, wallH - 0.05f, 0f), size * 0.7f);
            KeyLight("Fill", new Vector3(0f, wallH - 0.7f, 0f), CicArtKit.Cyan, 0.55f, size);
            KeyLight("Warm", new Vector3(-1.8f, 2.1f, -1.2f), CicArtKit.Amber, 0.45f, 6f);
        }

        void BuildMenuObservatory()
        {
            Viewport(new Vector3(0f, 1.85f, 4.46f), new Vector3(5.6f, 2.2f, 1f));
            Viewport(new Vector3(-2.9f, 1.7f, 4.46f), new Vector3(1.5f, 1.5f, 1f));
            Viewport(new Vector3(2.9f, 1.7f, 4.46f), new Vector3(1.5f, 1.5f, 1f));
            KeyLight("HublotGlow", new Vector3(0f, 1.9f, 3.6f), CicArtKit.Cyan, 2.8f, 7f);
            KeyLight("HublotAmber", new Vector3(1.4f, 1.5f, 3.2f), CicArtKit.Amber, 0.9f, 5f);

            Box("WalkPlate", new Vector3(0f, 0.03f, 0.6f), new Vector3(3.4f, 0.06f, 3.8f),
                _art.MetalPanel(0.08f), keepCollider: true);

            const float z = 1.55f;
            Box("TerminalBase", new Vector3(0f, 0.08f, z), new Vector3(1.7f, 0.16f, 0.55f),
                _art.MetalPanel(0.1f), keepCollider: true);
            Box("TerminalColumn", new Vector3(0f, 0.7f, z + 0.08f), new Vector3(0.35f, 1.2f, 0.28f),
                _art.DarkPanel(0.06f), keepCollider: true);
            Box("TerminalHousing", new Vector3(0f, 1.35f, z), new Vector3(1.55f, 0.95f, 0.12f),
                _art.DarkPanel(0.12f), keepCollider: true);
            Box("TerminalBezel", new Vector3(0f, 1.35f, z - 0.07f), new Vector3(1.38f, 0.82f, 0.04f),
                _art.MetalPanel(0.2f), keepCollider: false);

            Box("EdgeL", new Vector3(-0.72f, 1.35f, z - 0.09f), new Vector3(0.03f, 0.78f, 0.02f),
                _art.CyanEmit(3.5f), keepCollider: false);
            Box("EdgeR", new Vector3(0.72f, 1.35f, z - 0.09f), new Vector3(0.03f, 0.78f, 0.02f),
                _art.CyanEmit(3.5f), keepCollider: false);
            Box("EdgeT", new Vector3(0f, 1.72f, z - 0.09f), new Vector3(1.4f, 0.03f, 0.02f),
                _art.CyanEmit(3.5f), keepCollider: false);

            Cylinder("PylonL", new Vector3(-1.15f, 0.55f, z), new Vector3(0.12f, 0.55f, 0.12f),
                _art.SoftPanel(0.15f), keepCollider: false);
            Cylinder("PylonR", new Vector3(1.15f, 0.55f, z), new Vector3(0.12f, 0.55f, 0.12f),
                _art.SoftPanel(0.15f), keepCollider: false);
            Sphere("BeaconL", new Vector3(-1.15f, 1.15f, z), 0.07f, CicArtKit.Amber, 4f);
            Sphere("BeaconR", new Vector3(1.15f, 1.15f, z), 0.07f, CicArtKit.Cyan, 4f);

            var halo = Cylinder("Halo", new Vector3(0f, 2.55f, 1.35f), new Vector3(1.8f, 0.02f, 1.8f),
                _art.CyanEmit(2.2f), keepCollider: false);
            _ = halo;
            KeyLight("ConsoleKey", new Vector3(0f, 1.7f, 0.7f), CicArtKit.Cyan, 1.8f, 4.5f);
            KeyLight("ConsoleWarm", new Vector3(0.55f, 1.45f, 0.85f), CicArtKit.Amber, 0.55f, 3.5f);

            var mount = new GameObject("ConsoleMount");
            mount.transform.SetParent(transform, false);
            mount.transform.localPosition = new Vector3(0f, 1.35f, z - 0.14f);
            mount.transform.localRotation = Quaternion.identity;
            ConsoleMount = mount.transform;

            var plate = Quad("ConsolePlate", Vector3.zero, new Vector3(1.28f, 0.72f, 1f), _art.Wall,
                CicArtKit.DarkMetal, 0.2f);
            plate.transform.SetParent(ConsoleMount, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            plate.transform.localRotation = Quaternion.identity;
        }

        public void BuildWindowedForwardWall(float half, float wallH, float wallMid, int hublots)
        {
            const float winW = WorldScale.CicHublotWidth;
            const float winH = WorldScale.CicHublotHeight;
            const float winY = WorldScale.CicHublotCenterY;
            var z = half;
            var thickness = 0.22f;

            Box("FwdSill", new Vector3(0f, (winY - winH * 0.5f) * 0.5f, z),
                new Vector3(half * 2f + 0.25f, winY - winH * 0.5f, thickness),
                _art.MetalPanel(0.08f), keepCollider: true);
            var lintelY = winY + winH * 0.5f;
            var lintelH = wallH - lintelY;
            Box("FwdLintel", new Vector3(0f, lintelY + lintelH * 0.5f, z),
                new Vector3(half * 2f + 0.25f, lintelH, thickness),
                _art.MetalPanel(0.08f), keepCollider: true);

            var xs = new float[hublots];
            for (var i = 0; i < hublots; i++)
            {
                var t = (i + 1f) / (hublots + 1f);
                xs[i] = Mathf.Lerp(-half + 1.4f, half - 1.4f, t);
            }

            float prev = -half - 0.1f;
            for (var i = 0; i < hublots; i++)
            {
                var left = xs[i] - winW * 0.5f;
                var mid = (prev + left) * 0.5f;
                var width = Mathf.Max(0.12f, left - prev);
                Box("FwdMullion_" + i, new Vector3(mid, winY, z), new Vector3(width, winH, thickness),
                    _art.MetalPanel(0.08f), keepCollider: true);
                prev = xs[i] + winW * 0.5f;
            }

            var rightEdge = half + 0.1f;
            var midR = (prev + rightEdge) * 0.5f;
            var widthR = Mathf.Max(0.12f, rightEdge - prev);
            Box("FwdMullion_R", new Vector3(midR, winY, z), new Vector3(widthR, winH, thickness),
                _art.MetalPanel(0.08f), keepCollider: true);
        }

        public void Viewport(Vector3 pos, Vector3 scale, bool registerMount = false, bool openHole = false)
        {
            if (openHole)
            {
                var hw = scale.x * 0.5f;
                var hh = scale.y * 0.5f;
                const float rim = 0.08f;
                Box("HublotRimL", pos + new Vector3(-hw - rim * 0.5f, 0f, 0.02f),
                    new Vector3(rim, scale.y + rim * 2f, 0.12f), _art.DarkPanel(0.15f), keepCollider: true);
                Box("HublotRimR", pos + new Vector3(hw + rim * 0.5f, 0f, 0.02f),
                    new Vector3(rim, scale.y + rim * 2f, 0.12f), _art.DarkPanel(0.15f), keepCollider: true);
                Box("HublotRimT", pos + new Vector3(0f, hh + rim * 0.5f, 0.02f),
                    new Vector3(scale.x + rim * 2f, rim, 0.12f), _art.DarkPanel(0.15f), keepCollider: true);
                Box("HublotRimB", pos + new Vector3(0f, -hh - rim * 0.5f, 0.02f),
                    new Vector3(scale.x + rim * 2f, rim, 0.12f), _art.DarkPanel(0.15f), keepCollider: true);
                Box("HublotGlowL", pos + new Vector3(-hw - 0.02f, 0f, -0.02f),
                    new Vector3(0.03f, scale.y * 0.92f, 0.03f), _art.CyanEmit(3.5f), keepCollider: false);
                Box("HublotGlowR", pos + new Vector3(hw + 0.02f, 0f, -0.02f),
                    new Vector3(0.03f, scale.y * 0.92f, 0.03f), _art.CyanEmit(3.5f), keepCollider: false);
                KeyLight("HublotLamp", pos + new Vector3(0f, 0f, -0.55f), CicArtKit.Cyan, 1.35f, 3.8f);

                if (registerMount)
                {
                    var holeMount = new GameObject("HublotMount");
                    holeMount.transform.SetParent(transform, false);
                    holeMount.transform.localPosition = pos + new Vector3(0f, 0f, -0.12f);
                    holeMount.transform.localRotation = Quaternion.identity;
                    // mounts stay in local CIC space (parent may orbit the star)
                    _hublotMounts.Add(holeMount.transform);
                }

                return;
            }

            var frame = Quad("HublotFrame", pos + new Vector3(0f, 0f, 0.04f), scale + new Vector3(0.28f, 0.28f, 0f),
                _art.Wall, CicArtKit.DarkMetal, 0.06f);
            frame.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var bezel = Quad("HublotBezel", pos + new Vector3(0f, 0f, 0.03f), scale + new Vector3(0.12f, 0.12f, 0f),
                null, CicArtKit.Cyan * 0.55f, 2.8f);
            bezel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var glass = Quad("Hublot", pos, scale, _art.Stars, new Color(0.55f, 0.75f, 1f, 1f),
                registerMount ? 0.9f : 2.2f);
            glass.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            KeyLight("HublotLamp", pos + new Vector3(0f, 0f, -0.55f), CicArtKit.Cyan, registerMount ? 1.1f : 2.2f,
                3.5f);

            if (!registerMount)
                return;

            var mount = new GameObject("HublotMount");
            mount.transform.SetParent(glass.transform, false);
            mount.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            mount.transform.localRotation = Quaternion.identity;
            mount.transform.localScale = Vector3.one;
            _hublotMounts.Add(mount.transform);
        }

        public void StripLight(Vector3 pos, float width)
        {
            Quad("Strip", pos, new Vector3(width, 0.06f, 1f), null, CicArtKit.Cyan, 4.2f, rotateX: 90f);
        }

        public void TrimRing(string name, float y, float size, Color color, float emission)
        {
            Box(name + "N", new Vector3(0f, y, size * 0.5f - 0.05f), new Vector3(size, 0.03f, 0.08f),
                _art.Lit(Texture2D.whiteTexture, color, emission), keepCollider: false);
            Box(name + "S", new Vector3(0f, y, -size * 0.5f + 0.05f), new Vector3(size, 0.03f, 0.08f),
                _art.Lit(Texture2D.whiteTexture, color, emission), keepCollider: false);
            Box(name + "E", new Vector3(size * 0.5f - 0.05f, y, 0f), new Vector3(0.08f, 0.03f, size),
                _art.Lit(Texture2D.whiteTexture, color, emission), keepCollider: false);
            Box(name + "W", new Vector3(-size * 0.5f + 0.05f, y, 0f), new Vector3(0.08f, 0.03f, size),
                _art.Lit(Texture2D.whiteTexture, color, emission), keepCollider: false);
        }

        public GameObject Quad(string name, Vector3 pos, Vector3 scale, Texture tex, Color tint, float emission,
            float tiling = 1f, float rotateX = 0f, bool keepCollider = false, Material materialOverride = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (Mathf.Abs(rotateX) > 0.01f)
                go.transform.localRotation = Quaternion.Euler(rotateX, 0f, 0f);
            if (!keepCollider)
                DropColliderStatic(go);

            go.GetComponent<MeshRenderer>().sharedMaterial =
                materialOverride != null ? materialOverride : _art.Lit(tex, tint, emission, tiling);
            return go;
        }

        public GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (!keepCollider)
                DropColliderStatic(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Rounded console hardware at true size (unscaled transform, shared mesh) — the kit way to
        /// build anything that carries buttons, labels or screens. Accent lights its bevels.
        /// </summary>
        public GameObject Rounded(string name, Vector3 pos, Vector3 size, float radius, Color accent,
            float accentMul = 0.35f, bool keepCollider = false)
        {
            var go = Core.UI.UiKit.MeshPiece(transform, name, Core.UI.UiMeshes.RoundedBox(size, radius),
                Core.UI.UiKit.Chassis, pos);
            var block = new MaterialPropertyBlock();
            block.SetColor(Core.UI.UiKit.AccentId, accent);
            block.SetFloat(Core.UI.UiKit.AccentMulId, accentMul);
            go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            if (keepCollider)
                go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        public GameObject Cylinder(string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (!keepCollider)
                DropColliderStatic(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        GameObject Sphere(string name, Vector3 pos, float radius, Color tint, float emission)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * (radius * 2f);
            DropColliderStatic(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _art.Lit(Texture2D.whiteTexture, tint, emission);
            return go;
        }

        public void KeyLight(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        void SpawnDust()
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.4f, 1f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 8f;
            main.startSpeed = 0.02f;
            main.startSize = 0.012f;
            main.startColor = new Color(0.4f, 0.9f, 1f, 0.35f);
            main.maxParticles = Layout == CicLayout.BootVoid ? 120 : Layout == CicLayout.MenuDeck ? 100 : 70;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = Layout == CicLayout.MenuDeck ? 10f : 7f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = Layout == CicLayout.Bridge ? new Vector3(5f, 2.2f, 5f) : new Vector3(4f, 2f, 4f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan, 2f);
        }

        void EnsureVolume()
        {
            EnableCameraPost();
            if (FindFirstObjectByType<Volume>() != null)
                return;
            var go = new GameObject("CIC Volume");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            if (profile.TryGet(out Bloom bloom) == false)
                bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(Layout == CicLayout.MenuDeck ? 0.72f : 0.42f);
            bloom.threshold.Override(Layout == CicLayout.Bridge ? 0.88f : 0.8f);
            bloom.scatter.Override(0.7f);
            // No chromatic aberration: full-screen fill-rate on Quest and uncomfortable in a headset.
            if (profile.TryGet(out Vignette vignette) == false)
                vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(Layout == CicLayout.Bridge ? 0.22f : 0.34f);
            vignette.color.Override(new Color(0.02f, 0.05f, 0.08f));
            volume.sharedProfile = profile;
        }

        /// <summary>
        /// The XR rig camera ships with post-processing off, so the Volume never reached the headset.
        /// Quest trade-off: LDR bloom + vignette in URP's single uber pass (~1 ms), no HDR target.
        /// </summary>
        static void EnableCameraPost()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null)
                data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
        }

        public static void DropColliderStatic(GameObject go)
        {
            if (go == null)
                return;
            var col = go.GetComponent<Collider>();
            if (col == null)
                return;
            Drop(col);
        }

        static void Drop(UnityEngine.Object o)
        {
            if (o == null)
                return;
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }
    }
}
