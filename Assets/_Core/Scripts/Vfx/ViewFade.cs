using System.Threading.Tasks;
using Core.Utils;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Full-view fade for teleporter / system swap. A small inside sphere parented to the XR camera,
    /// drawn last with ZTest Always (SU/ViewFade) — Screen Space Overlay canvases do not render in the headset.
    /// </summary>
    public class ViewFade : MonoBehaviour
    {
        const float Radius = 0.25f;
        static readonly Color Veil = new(0f, 0.02f, 0.05f, 1f);
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static ViewFade _instance;
        MeshRenderer _renderer;
        Material _material;
        int _ticket;

        public static ViewFade Ensure()
        {
            if (_instance != null)
                return _instance;
            var go = new GameObject("ViewFade");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ViewFade>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "FadeVeil";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * (Radius * 2f);
            Destroy(sphere.GetComponent<Collider>());

            _renderer = sphere.GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            var shader = Shader.Find("SU/ViewFade");
            if (shader == null)
            {
                // Without the veil shader a fade would draw behind the room: skip it, never flash a grey ball.
                Debug.LogWarning("[SU] ViewFade: SU/ViewFade shader missing, fades disabled.");
                enabled = false;
                sphere.SetActive(false);
                return;
            }

            _material = new Material(shader);
            SetAlpha(0f);
        }

        void LateUpdate()
        {
            // Follow whichever XR camera the current scene owns (rig is rebuilt per scene).
            var cam = Camera.main;
            if (cam == null)
                return;
            if (transform.parent != cam.transform)
            {
                transform.SetParent(cam.transform, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }

        public Task FadeOut(float duration = 0.35f) => Animate(1f, duration);

        public Task FadeIn(float duration = 0.4f) => Animate(0f, duration);

        /// <summary>
        /// Ease the veil from where it is now to <paramref name="to"/>. The latest request wins: an older fade still
        /// running stops where it is and its caller resumes. Never dropped (a dropped fade-in left the view black)
        /// and never restarted from a fixed end (a fade-out over an already black view flashed the scene).
        /// </summary>
        async Task Animate(float to, float duration)
        {
            if (_material == null)
                return;
            var ticket = ++_ticket;
            LateUpdate();
            var from = Alpha;
            var span = duration * Mathf.Abs(to - from);
            var t = 0f;
            while (t < span)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, to, MotionEase.SmoothInOut(t / span)));
                await Task.Yield();
                if (ticket != _ticket)
                    return;
            }

            SetAlpha(to);
        }

        /// <summary>Current veil opacity (0 clear – 1 black): the ambience dips with it.</summary>
        public static float Alpha { get; private set; }

        void SetAlpha(float a)
        {
            Alpha = a;
            var c = Veil;
            c.a = a;
            _material.SetColor(ColorId, c);
            _renderer.sharedMaterial = _material;
            // Hidden when clear: no overdraw sphere in front of the eyes.
            _renderer.enabled = a > 0.001f;
        }
    }
}
