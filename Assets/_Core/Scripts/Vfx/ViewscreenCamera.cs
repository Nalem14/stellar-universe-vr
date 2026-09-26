using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Core.Vfx
{
    /// <summary>
    /// The hull camera behind the main viewscreen: a mono camera outside the room shell that films the shared
    /// <see cref="SystemExterior"/> — straight ahead, or framing one subject — into the render texture the
    /// curved screen shows. The screen HUD is a Screen Space - Camera canvas on its own layer (<see cref="HudLayer"/>),
    /// drawn by this camera only. Quest budget: 1152×400, no post, no shadows, no MSAA, a frame every 1/24 s and
    /// only while <see cref="Active"/>; a frame is rendered by enabling the camera for that one frame (no
    /// Camera.Render in URP).
    /// </summary>
    public sealed class ViewscreenCamera : MonoBehaviour
    {
        public const int Width = 1152;
        public const int Height = 400;
        /// <summary>Layer of the screen HUD: seen by this camera, culled from the player's.</summary>
        public const int HudLayer = 31;
        const float Interval = 1f / 24f;
        /// <summary>Metres out from the bridge centre along the line of sight: clear of the room shell.</summary>
        const float HullStandoff = 9f;
        /// <summary>Vertical field of view straight ahead (matches the screen's angular size from the chair, ×1.6).</summary>
        public const float ForwardFov = 22f;

        Camera _cam;
        RenderTexture _rt;
        Transform _bridge;
        Transform _subject;
        float _radius = 10f;
        float _fov = ForwardFov;
        float _next;
        bool _armed;

        public RenderTexture Texture => _rt;
        public Camera Camera => _cam;
        public bool Active { get; set; }
        public Transform Subject => _subject;
        /// <summary>True on the frame the camera renders (move HUD elements just before).</summary>
        public bool RendersThisFrame { get; private set; }

        public static ViewscreenCamera Create(Transform bridge)
        {
            var go = new GameObject("ViewscreenCamera");
            go.transform.SetParent(bridge, false);
            var vc = go.AddComponent<ViewscreenCamera>();
            vc._bridge = bridge;
            vc._rt = new RenderTexture(Width, Height, 16, RenderTextureFormat.ARGB32)
            {
                name = "SU_ViewscreenFeed", antiAliasing = 1, useMipMap = false, wrapMode = TextureWrapMode.Clamp
            };
            vc._rt.Create();

            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = vc._rt;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.004f, 0.008f, 0.016f, 1f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = WorldScale.BridgeFarClip * 1.4f;
            cam.fieldOfView = ForwardFov;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            vc._cam = cam;
            vc.Place();
            return vc;
        }

        /// <summary>Frame <paramref name="subject"/> (radius in world metres); null = straight ahead.</summary>
        public void Track(Transform subject, float radius)
        {
            _subject = subject;
            _radius = Mathf.Max(1f, radius);
        }

        void Update()
        {
            // Decide in Update so the HUD (LateUpdate) positions its markers for the frame that renders.
            if (_armed)
            {
                _cam.enabled = false;
                _armed = false;
            }

            RendersThisFrame = Active && Time.unscaledTime >= _next;
            if (!RendersThisFrame)
                return;
            _next = Time.unscaledTime + Interval;
            Place();
            _cam.enabled = true;
            _armed = true;
        }

        void Place()
        {
            var origin = _bridge.TransformPoint(new Vector3(0f, 1.6f, 0f));
            Vector3 dir;
            float fov;
            var start = HullStandoff;
            var subject = _subject != null && _subject.gameObject.activeInHierarchy ? _subject : null;
            if (subject == null)
            {
                dir = _bridge.forward;
                fov = ForwardFov;
            }
            else
            {
                var to = subject.position - origin;
                var dist = Mathf.Max(1f, to.magnitude);
                dir = to / dist;
                start = Mathf.Clamp(Mathf.Min(HullStandoff, dist - _radius * 2.5f), 0f, HullStandoff);
                fov = Mathf.Clamp(2f * Mathf.Atan(_radius * 1.8f / (dist - start)) * Mathf.Rad2Deg, 1.2f, ForwardFov * 2f);
            }

            // Zoom and slew settle over ~1 s; a slow drift keeps the feed alive (optics on a moving hull).
            var k = 1f - Mathf.Exp(-Interval * 3.2f);
            _fov = Mathf.Lerp(_fov, fov, k);
            var look = Vector3.Slerp(transform.forward, dir, k * 1.4f);
            if (look.sqrMagnitude < 1e-4f)
                look = dir;
            var t = Time.unscaledTime;
            var drift = Quaternion.Euler(Mathf.Sin(t * 0.21f) * _fov * 0.02f, Mathf.Sin(t * 0.17f + 1.3f) * _fov * 0.025f, 0f);
            var rot = Quaternion.LookRotation(look, _bridge.up);
            transform.SetPositionAndRotation(origin + look * start, rot * drift);
            _cam.fieldOfView = _fov;
        }

        void OnDestroy()
        {
            if (_rt != null)
                _rt.Release();
        }
    }
}
