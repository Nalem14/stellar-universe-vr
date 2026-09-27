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
        const float MainInterval = 1f / 24f;
        /// <summary>Metres out from the bridge centre along the line of sight: clear of the room shell.</summary>
        const float HullStandoff = 9f;
        /// <summary>Vertical field of view straight ahead (matches the screen's angular size from the chair, ×1.6).</summary>
        public const float ForwardFov = 22f;
        /// <summary>Field of view a drone shot frames its subject in.</summary>
        const float DroneFov = 24f;

        Camera _cam;
        RenderTexture _rt;
        Transform _bridge;
        Transform _subject;
        float _radius = 10f;
        float _fov = ForwardFov;
        float _next;
        bool _armed;
        float _interval = MainInterval;

        /// <summary>Captain's hand on the shot: orbit around the subject (or pan the forward view), in degrees.</summary>
        public float OrbitYaw { get; set; }
        public float OrbitPitch { get; set; }
        /// <summary>Zoom factor: &lt; 1 closer / narrower, &gt; 1 wider (1 = the director's framing).</summary>
        public float Zoom { get; set; } = 1f;
        public bool Manual => Mathf.Abs(OrbitYaw) > 0.5f || Mathf.Abs(OrbitPitch) > 0.5f || Mathf.Abs(Zoom - 1f) > 0.02f;

        public void ResetManual()
        {
            OrbitYaw = 0f;
            OrbitPitch = 0f;
            Zoom = 1f;
        }

        public RenderTexture Texture => _rt;
        public Camera Camera => _cam;
        public bool Active { get; set; }
        public Transform Subject => _subject;
        /// <summary>True on the frame the camera renders (move HUD elements just before).</summary>
        public bool RendersThisFrame { get; private set; }

        /// <summary>
        /// The main feed (<see cref="Width"/>×<see cref="Height"/>, 24 Hz), or a smaller one (picture-in-picture:
        /// its own size and rate, blind to the HUD layer it is shown on).
        /// </summary>
        public static ViewscreenCamera Create(Transform bridge, int width = Width, int height = Height, float interval = MainInterval,
            string name = "ViewscreenCamera")
        {
            var go = new GameObject(name);
            go.transform.SetParent(bridge, false);
            var vc = go.AddComponent<ViewscreenCamera>();
            vc._bridge = bridge;
            vc._interval = interval;
            vc._rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "SU_" + name, antiAliasing = 1, useMipMap = false, wrapMode = TextureWrapMode.Clamp
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
            if (width != Width || height != Height)
                cam.cullingMask &= ~(1 << HudLayer);
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
            _next = Time.unscaledTime + _interval;
            Place();
            _cam.enabled = true;
            _armed = true;
        }

        void Place()
        {
            var origin = _bridge.TransformPoint(new Vector3(0f, 1.6f, 0f));
            var subject = _subject != null && _subject.gameObject.activeInHierarchy ? _subject : null;
            Vector3 pos;
            Vector3 look;
            float fov;
            if (subject == null)
            {
                // Straight ahead, from the hull just outside the room (the captain may pan it and zoom).
                look = Quaternion.AngleAxis(OrbitYaw, _bridge.up) * Quaternion.AngleAxis(-OrbitPitch, _bridge.right) * _bridge.forward;
                pos = origin + _bridge.forward * HullStandoff;
                fov = Mathf.Clamp(ForwardFov * Zoom, 6f, 70f);
            }
            else
            {
                // A drone shot: from our side of the subject, a quarter turn off the line of sight (lit, with
                // depth), close enough that the world we orbit never hides it. Never nearer to us than the hull.
                var to = subject.position - origin;
                var dist = Mathf.Max(1f, to.magnitude);
                var dir = to / dist;
                var side = Vector3.Cross(_bridge.up, dir);
                if (side.sqrMagnitude < 1e-4f)
                    side = _bridge.right;
                var approach = Quaternion.AngleAxis(20f + OrbitYaw, _bridge.up) * Quaternion.AngleAxis(OrbitPitch, side.normalized) * dir;
                var drone = _radius * 1.8f / Mathf.Tan(DroneFov * 0.5f * Mathf.Deg2Rad) * Zoom;
                pos = subject.position - approach * drone;
                if (Vector3.Dot(pos - origin, dir) < HullStandoff)
                    pos = origin + dir * Mathf.Clamp(Mathf.Min(HullStandoff, dist - _radius * 2.5f), 0f, HullStandoff);
                look = (subject.position - pos).normalized;
                fov = Mathf.Clamp(2f * Mathf.Atan(_radius * 1.8f / Mathf.Max(1f, Vector3.Distance(pos, subject.position))) * Mathf.Rad2Deg,
                    1.2f, ForwardFov * 2f);
            }

            // Moves, slews and zooms settle over ~1 s; a slow drift keeps the feed alive. Smoothed in the
            // bridge's frame: the ship flying at speed (departure, approach) carries the camera rigidly instead
            // of leaving it trailing back into the room.
            var k = 1f - Mathf.Exp(-_interval * 3.2f);
            _fov = Mathf.Lerp(_fov, fov, k);
            var local = _bridge.InverseTransformPoint(pos);
            var localLook = _bridge.InverseTransformDirection(look);
            _local = _placed ? Vector3.Lerp(_local, local, k) : local;
            _aim = _placed ? Vector3.Slerp(_aim, localLook, k * 1.4f) : localLook;
            _placed = true;
            if (_aim.sqrMagnitude < 1e-4f)
                _aim = localLook;
            var t = Time.unscaledTime;
            var drift = Quaternion.Euler(Mathf.Sin(t * 0.21f) * _fov * 0.02f, Mathf.Sin(t * 0.17f + 1.3f) * _fov * 0.025f, 0f);
            transform.SetPositionAndRotation(_bridge.TransformPoint(_local),
                Quaternion.LookRotation(_bridge.TransformDirection(_aim), _bridge.up) * drift);
            _cam.fieldOfView = _fov;
        }

        Vector3 _local;
        Vector3 _aim;
        bool _placed;

        void OnDestroy()
        {
            if (_rt != null)
                _rt.Release();
        }
    }
}
