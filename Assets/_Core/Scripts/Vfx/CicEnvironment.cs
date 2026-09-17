using System;
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

        static readonly Color Cyan = new(0.25f, 0.92f, 1f, 1f);
        static readonly Color Amber = new(1f, 0.62f, 0.22f, 1f);
        static readonly Color Metal = new(0.22f, 0.28f, 0.34f, 1f);
        static readonly Color DarkMetal = new(0.08f, 0.1f, 0.12f, 1f);

        Texture _floor;
        Texture _wall;
        Texture _holo;
        Texture _stars;
        Texture _title;
        Shader _holoShader;
        Shader _emissiveShader;

        public GameObject Table { get; private set; }
        public Transform ConsoleMount { get; private set; }

        public void Build()
        {
            LoadArt();
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
                    BuildRoom(size: 12f, hublots: 3, ceiling: 3.1f);
                    BuildHoloTable();
                    break;
            }

            SpawnDust();
            EnsureVolume();
        }

        void LoadArt()
        {
            _floor = Resources.Load<Texture2D>("CIC/Floor");
            _wall = Resources.Load<Texture2D>("CIC/Wall");
            _holo = Resources.Load<Texture2D>("CIC/HoloTable");
            _stars = Resources.Load<Texture2D>("CIC/ViewportStars");
            _title = Resources.Load<Texture2D>("CIC/BootTitle");
            _holoShader = Shader.Find("SU/HoloSurface");
            _emissiveShader = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Texture");
        }

        void ApplyAtmosphere()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.01f, 0.02f, 0.04f);
            RenderSettings.fogDensity = Layout == CicLayout.BootVoid ? 0.04f :
                Layout == CicLayout.MenuDeck ? 0.028f : 0.02f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.025f, 0.04f, 0.055f);
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null)
                    continue;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.008f, 0.012f, 0.02f);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 40f);
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
                    n.IndexOf("Passthrough", StringComparison.OrdinalIgnoreCase) >= 0)
                    t.gameObject.SetActive(false);
            }
        }

        void BuildBootVoid()
        {
            var deck = Quad("BootDeck", new Vector3(0f, 0f, 0f), new Vector3(12f, 12f, 1f), null, Color.clear, 0f,
                rotateX: 90f, keepCollider: true);
            var deckRenderer = deck.GetComponent<MeshRenderer>();
            if (deckRenderer != null)
                deckRenderer.enabled = false;

            var sky = Quad("Nebula", new Vector3(0f, 1.4f, 6.5f), new Vector3(9.6f, 5.4f, 1f), _stars, Cyan * 0.15f, 1.1f);
            sky.transform.LookAt(Vector3.zero);

            if (_title != null)
            {
                var plate = Quad("TitleHolo", new Vector3(0f, 1.55f, 2.4f), new Vector3(2.4f, 1.35f, 1f), _title, Color.white, 1.8f);
                var holo = plate.AddComponent<HoloSpin>();
                holo.DegreesPerSecond = 0f;
                holo.BobMeters = 0.04f;
            }

            KeyLight("Key", new Vector3(0.4f, 2.2f, -1.2f), Cyan, 2.4f, 8f);
            KeyLight("Rim", new Vector3(-1.6f, 1.8f, 1.5f), Amber, 1.1f, 7f);
        }

        void BuildRoom(float size, int hublots, float ceiling)
        {
            var half = size * 0.5f;
            var wallH = ceiling;
            var wallMid = wallH * 0.5f;

            Quad("Deck", new Vector3(0f, 0f, 0f), new Vector3(size, size, 1f), _floor, Color.white, 0.14f,
                tiling: 4f, rotateX: 90f, keepCollider: true);
            Quad("Overhead", new Vector3(0f, wallH, 0f), new Vector3(size, size, 1f), _wall,
                new Color(0.12f, 0.15f, 0.18f), 0.06f, tiling: 3f, rotateX: -90f);

            // Floor / ceiling trim rings
            TrimRing("FloorTrim", 0.04f, size - 0.2f, Cyan * 0.55f, 1.8f);
            TrimRing("CeilTrim", wallH - 0.04f, size - 0.2f, Amber * 0.4f, 1.2f);

            // Thick box walls — no backface holes / grey void leaks.
            Box("Fwd", new Vector3(0f, wallMid, half), new Vector3(size + 0.2f, wallH, 0.18f), Metal, 0.08f);
            Box("Aft", new Vector3(0f, wallMid, -half), new Vector3(size + 0.2f, wallH, 0.18f), Metal, 0.08f);
            Box("Port", new Vector3(-half, wallMid, 0f), new Vector3(0.18f, wallH, size + 0.2f), Metal, 0.08f);
            Box("Starboard", new Vector3(half, wallMid, 0f), new Vector3(0.18f, wallH, size + 0.2f), Metal, 0.08f);

            // Corner ribs
            float rib = half - 0.12f;
            foreach (var xz in new[]
                     {
                         new Vector3(rib, wallMid, rib), new Vector3(-rib, wallMid, rib),
                         new Vector3(rib, wallMid, -rib), new Vector3(-rib, wallMid, -rib)
                     })
            {
                Box("Rib", xz, new Vector3(0.14f, wallH, 0.14f), DarkMetal, 0.05f);
            }

            for (var i = 0; i < hublots; i++)
            {
                var t = (i + 1f) / (hublots + 1f);
                var x = Mathf.Lerp(-half + 1.4f, half - 1.4f, t);
                Viewport(new Vector3(x, 1.65f, half - 0.04f), new Vector3(1.8f, 1.1f, 1f));
            }

            StripLight(new Vector3(0f, wallH - 0.05f, 0f), size * 0.7f);
            KeyLight("Fill", new Vector3(0f, wallH - 0.7f, 0f), Cyan, 0.55f, size);
            KeyLight("Warm", new Vector3(-1.8f, 2.1f, -1.2f), Amber, 0.45f, 6f);
        }

        void BuildMenuObservatory()
        {
            // Panoramic forward viewport — space is the backdrop of the airlock.
            Viewport(new Vector3(0f, 1.85f, 4.46f), new Vector3(5.6f, 2.2f, 1f));
            Viewport(new Vector3(-2.9f, 1.7f, 4.46f), new Vector3(1.5f, 1.5f, 1f));
            Viewport(new Vector3(2.9f, 1.7f, 4.46f), new Vector3(1.5f, 1.5f, 1f));
            KeyLight("HublotGlow", new Vector3(0f, 1.9f, 3.6f), Cyan, 2.8f, 7f);
            KeyLight("HublotAmber", new Vector3(1.4f, 1.5f, 3.2f), Amber, 0.9f, 5f);

            Box("WalkPlate", new Vector3(0f, 0.03f, 0.6f), new Vector3(3.4f, 0.06f, 3.8f), Metal, 0.08f);

            // Upright CIC terminal. Player at origin looks +Z.
            // Canvas stays rotation identity — yaw 180 mirrors the form.
            const float z = 1.55f;
            Box("TerminalBase", new Vector3(0f, 0.08f, z), new Vector3(1.7f, 0.16f, 0.55f), Metal, 0.1f);
            Box("TerminalColumn", new Vector3(0f, 0.7f, z + 0.08f), new Vector3(0.35f, 1.2f, 0.28f), DarkMetal, 0.06f);
            Box("TerminalHousing", new Vector3(0f, 1.35f, z), new Vector3(1.55f, 0.95f, 0.12f), DarkMetal, 0.12f);
            Box("TerminalBezel", new Vector3(0f, 1.35f, z - 0.07f), new Vector3(1.38f, 0.82f, 0.04f), Metal, 0.2f);

            Box("EdgeL", new Vector3(-0.72f, 1.35f, z - 0.09f), new Vector3(0.03f, 0.78f, 0.02f), Cyan, 3.5f);
            Box("EdgeR", new Vector3(0.72f, 1.35f, z - 0.09f), new Vector3(0.03f, 0.78f, 0.02f), Cyan, 3.5f);
            Box("EdgeT", new Vector3(0f, 1.72f, z - 0.09f), new Vector3(1.4f, 0.03f, 0.02f), Cyan, 3.5f);

            Cylinder("PylonL", new Vector3(-1.15f, 0.55f, z), new Vector3(0.12f, 0.55f, 0.12f), _wall, 0.15f);
            Cylinder("PylonR", new Vector3(1.15f, 0.55f, z), new Vector3(0.12f, 0.55f, 0.12f), _wall, 0.15f);
            Sphere("BeaconL", new Vector3(-1.15f, 1.15f, z), 0.07f, Amber, 4f);
            Sphere("BeaconR", new Vector3(1.15f, 1.15f, z), 0.07f, Cyan, 4f);

            var halo = Cylinder("Halo", new Vector3(0f, 2.55f, 1.35f), new Vector3(1.8f, 0.02f, 1.8f), null, 2.2f);
            halo.GetComponent<MeshRenderer>().sharedMaterial = EmissiveMaterial(Texture2D.whiteTexture, Cyan, 2.2f);
            KeyLight("ConsoleKey", new Vector3(0f, 1.7f, 0.7f), Cyan, 1.8f, 4.5f);
            KeyLight("ConsoleWarm", new Vector3(0.55f, 1.45f, 0.85f), Amber, 0.55f, 3.5f);

            // Mount in front of housing (toward player). Identity rotation.
            var mount = new GameObject("ConsoleMount");
            mount.transform.SetParent(transform, false);
            mount.transform.SetPositionAndRotation(new Vector3(0f, 1.35f, z - 0.14f), Quaternion.identity);
            ConsoleMount = mount.transform;

            // Plate behind the canvas so UI is never buried in the mesh.
            var plate = Quad("ConsolePlate", Vector3.zero, new Vector3(1.28f, 0.72f, 1f), _wall, DarkMetal, 0.2f);
            plate.transform.SetParent(ConsoleMount, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            plate.transform.localRotation = Quaternion.identity;
        }

        void BuildHoloTable()
        {
            var pedestal = Cylinder("Pedestal", new Vector3(0f, 0.42f, 1.35f), new Vector3(0.55f, 0.42f, 0.55f), _wall, 0.04f);
            var rim = Cylinder("TableRim", new Vector3(0f, 0.86f, 1.35f), new Vector3(1.45f, 0.03f, 1.45f), _wall, 0.2f);
            Table = Cylinder("HoloTable", new Vector3(0f, 0.9f, 1.35f), new Vector3(1.38f, 0.012f, 1.38f), _holo, 0f);
            var holoMat = HoloMaterial(_holo);
            Table.GetComponent<MeshRenderer>().sharedMaterial = holoMat;
            rim.GetComponent<MeshRenderer>().sharedMaterial = holoMat;

            var spin = Table.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 6f;
            spin.BobMeters = 0.015f;

            KeyLight("TableGlow", new Vector3(0f, 0.95f, 1.35f), Cyan, 3.2f, 4.5f);
            KeyLight("TableAmber", new Vector3(0.4f, 1.1f, 1.1f), Amber, 0.7f, 3f);

            pedestal.name = "Pedestal";
        }

        void Viewport(Vector3 pos, Vector3 scale)
        {
            var frame = Quad("HublotFrame", pos + new Vector3(0f, 0f, 0.03f), scale + new Vector3(0.22f, 0.22f, 0f),
                _wall, DarkMetal, 0.04f);
            frame.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var inner = Quad("HublotInner", pos + new Vector3(0f, 0f, 0.02f), scale + new Vector3(0.08f, 0.08f, 0f),
                null, Cyan * 0.35f, 1.4f);
            inner.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var glass = Quad("Hublot", pos, scale, _stars, Color.white, 1.8f);
            glass.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        void StripLight(Vector3 pos, float width)
        {
            Quad("Strip", pos, new Vector3(width, 0.06f, 1f), null, Cyan, 4.2f, rotateX: 90f);
        }

        void TrimRing(string name, float y, float size, Color color, float emission)
        {
            Box(name + "N", new Vector3(0f, y, size * 0.5f - 0.05f), new Vector3(size, 0.03f, 0.08f), color, emission);
            Box(name + "S", new Vector3(0f, y, -size * 0.5f + 0.05f), new Vector3(size, 0.03f, 0.08f), color, emission);
            Box(name + "E", new Vector3(size * 0.5f - 0.05f, y, 0f), new Vector3(0.08f, 0.03f, size), color, emission);
            Box(name + "W", new Vector3(-size * 0.5f + 0.05f, y, 0f), new Vector3(0.08f, 0.03f, size), color, emission);
        }

        GameObject Quad(string name, Vector3 pos, Vector3 scale, Texture tex, Color tint, float emission,
            float tiling = 1f, float rotateX = 0f, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            if (Mathf.Abs(rotateX) > 0.01f)
                go.transform.rotation = Quaternion.Euler(rotateX, 0f, 0f);
            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null)
                    Destroy(col);
            }

            go.GetComponent<MeshRenderer>().sharedMaterial = EmissiveMaterial(tex, tint, emission, tiling);
            return go;
        }

        GameObject Box(string name, Vector3 pos, Vector3 scale, Color tint, float emission)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = EmissiveMaterial(_wall, tint, emission, 1.2f);
            return go;
        }

        GameObject Cylinder(string name, Vector3 pos, Vector3 scale, Texture tex, float emission)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = EmissiveMaterial(tex, Color.white, emission, 1.5f);
            return go;
        }

        GameObject Sphere(string name, Vector3 pos, float radius, Color tint, float emission)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * (radius * 2f);
            var col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = EmissiveMaterial(Texture2D.whiteTexture, tint, emission);
            return go;
        }

        void KeyLight(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
        }

        void SpawnDust()
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 1.4f, 1f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 8f;
            main.startSpeed = 0.02f;
            main.startSize = 0.012f;
            main.startColor = new Color(0.4f, 0.9f, 1f, 0.35f);
            main.maxParticles = Layout == CicLayout.BootVoid ? 120 : Layout == CicLayout.MenuDeck ? 100 : 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = Layout == CicLayout.MenuDeck ? 10f : 8f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(4f, 2f, 4f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = EmissiveMaterial(Texture2D.whiteTexture, Cyan, 2f);
        }

        void EnsureVolume()
        {
            if (FindFirstObjectByType<Volume>() != null)
                return;
            var go = new GameObject("CIC Volume");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            if (profile.TryGet(out Bloom bloom) == false)
                bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(Layout == CicLayout.MenuDeck ? 0.72f : 0.55f);
            bloom.threshold.Override(0.78f);
            bloom.scatter.Override(0.72f);
            if (profile.TryGet(out ChromaticAberration chroma) == false)
                chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0.1f);
            if (profile.TryGet(out Vignette vignette) == false)
                vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.32f);
            vignette.color.Override(new Color(0.02f, 0.05f, 0.08f));
            volume.sharedProfile = profile;
        }

        Material EmissiveMaterial(Texture tex, Color tint, float emissionMul, float tiling = 1f)
        {
            var shader = _emissiveShader != null ? _emissiveShader : Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = tex;
            if (mat.HasProperty("_MainTex"))
                mat.SetTextureScale("_MainTex", Vector2.one * tiling);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Emission"))
                mat.SetColor("_Emission", tint * Mathf.Max(0f, emissionMul) * 0.25f);
            if (mat.HasProperty("_EmissionMul"))
                mat.SetFloat("_EmissionMul", emissionMul);
            return mat;
        }

        Material HoloMaterial(Texture tex)
        {
            var shader = _holoShader != null ? _holoShader : Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = tex;
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", new Color(0.2f, 0.85f, 1f, 0.55f));
            if (mat.HasProperty("_Emission"))
                mat.SetColor("_Emission", Cyan);
            return mat;
        }
    }
}
