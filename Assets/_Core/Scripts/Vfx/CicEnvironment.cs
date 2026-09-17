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

        Texture _floor;
        Texture _wall;
        Texture _holo;
        Texture _stars;
        Texture _title;
        Shader _holoShader;
        Shader _emissiveShader;

        public GameObject Table { get; private set; }

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
                    BuildRoom(size: 8f, hublots: 2);
                    BuildMenuDais();
                    break;
                default:
                    BuildRoom(size: 12f, hublots: 3);
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
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.015f, 0.03f, 0.05f);
            RenderSettings.fogDensity = Layout == CicLayout.BootVoid ? 0.04f : 0.018f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.03f, 0.05f, 0.07f);
            if (Camera.main != null)
                Camera.main.backgroundColor = new Color(0.01f, 0.015f, 0.03f);
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

        void BuildRoom(float size, int hublots)
        {
            var half = size * 0.5f;
            Quad("Deck", new Vector3(0f, 0f, 0f), new Vector3(size, size, 1f), _floor, Color.white, 0.12f, tiling: 4f, rotateX: 90f, keepCollider: true);
            Quad("Overhead", new Vector3(0f, 3.1f, 0f), new Vector3(size, size, 1f), _wall, new Color(0.15f, 0.18f, 0.22f), 0.05f, tiling: 3f, rotateX: -90f);

            Wall("Fwd", new Vector3(0f, 1.55f, half), new Vector3(size, 3.1f, 1f));
            Wall("Aft", new Vector3(0f, 1.55f, -half), new Vector3(size, 3.1f, 1f), 180f);
            Wall("Port", new Vector3(-half, 1.55f, 0f), new Vector3(size, 3.1f, 1f), 90f);
            Wall("Starboard", new Vector3(half, 1.55f, 0f), new Vector3(size, 3.1f, 1f), -90f);

            for (var i = 0; i < hublots; i++)
            {
                var t = (i + 1f) / (hublots + 1f);
                var x = Mathf.Lerp(-half + 1.4f, half - 1.4f, t);
                Viewport(new Vector3(x, 1.65f, half - 0.04f), new Vector3(1.8f, 1.1f, 1f));
            }

            StripLight(new Vector3(0f, 3.05f, 0f), size * 0.7f);
            KeyLight("Fill", new Vector3(0f, 2.4f, 0f), Cyan, 0.55f, size);
            KeyLight("Warm", new Vector3(-1.8f, 2.1f, -1.2f), Amber, 0.45f, 6f);
        }

        void BuildMenuDais()
        {
            var dais = Cylinder("Dais", new Vector3(0f, 0.08f, 1.15f), new Vector3(1.6f, 0.08f, 1.6f), _floor, 0.08f);
            dais.isStatic = false;
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
            var frame = Quad("HublotFrame", pos + new Vector3(0f, 0f, 0.02f), scale + new Vector3(0.18f, 0.18f, 0f), _wall, Color.black, 0.02f);
            frame.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var glass = Quad("Hublot", pos, scale, _stars, Color.white, 1.6f);
            glass.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        void Wall(string name, Vector3 pos, Vector3 scale, float yaw = 0f)
        {
            var wall = Quad(name, pos, scale, _wall, Color.white, 0.08f, tiling: 2f);
            wall.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void StripLight(Vector3 pos, float width)
        {
            Quad("Strip", pos, new Vector3(width, 0.06f, 1f), null, Cyan, 4f, rotateX: 90f);
        }

        GameObject Quad(string name, Vector3 pos, Vector3 scale, Texture tex, Color tint, float emission, float tiling = 1f, float rotateX = 0f, bool keepCollider = false)
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
            main.maxParticles = Layout == CicLayout.BootVoid ? 120 : 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 8f;
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
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.7f);
            if (profile.TryGet(out ChromaticAberration chroma) == false)
                chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0.08f);
            if (profile.TryGet(out Vignette vignette) == false)
                vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.28f);
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
            return mat;
        }
    }
}
