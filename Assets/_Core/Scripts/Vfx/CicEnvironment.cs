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

        public void Build()
        {
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
                    SasShell.Build(transform, _art);
                    KeyLight("Fill", new Vector3(0f, 3.3f, 0.9f), CicArtKit.Cyan, 0.55f, 9f);
                    KeyLight("Warm", new Vector3(-1.8f, 2.1f, -1.2f), CicArtKit.Amber, 0.45f, 6f);
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
            // The airlock's window looks out on the star sky and a planet (SasShell): same reach as the bridge.
            var far = Layout is CicLayout.Bridge or CicLayout.MenuDeck ? WorldScale.BridgeFarClip : 40f;
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

        void BuildMenuObservatory()
        {
            KeyLight("HublotGlow", new Vector3(0f, 1.9f, 3.9f), CicArtKit.Cyan, 2.2f, 7f);
            KeyLight("HublotAmber", new Vector3(1.4f, 1.5f, 3.4f), CicArtKit.Amber, 0.9f, 5f);

            const float z = 1.55f;
            // Terminal: a turned pedestal and column (no boxes), the housing a rounded slab.
            SasTerminal.Build(transform, _art, new Vector3(0f, 0f, z));

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
