using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core.Vfx
{
    /// <summary>
    /// Our own ship seen from outside on the screen (a drone shot far enough out to have the bridge in frame):
    /// the hull cameras see our hull — hidden to the player, who is inside it — flown at the bridge's pose, and
    /// never the room: a depth-only box round the room hides it (and it holds nothing we'd want seen from
    /// outside). From inside, the box's faces are back faces, so neither the player nor a near hull camera
    /// looking outward ever draws it. The hull shows for the one render only (begin / end camera rendering).
    /// </summary>
    public sealed partial class BridgeViewscreen
    {
        /// <summary>Beyond this from the bridge a hull camera may have the ship in frame.</summary>
        const float OutsideDistance = 20f;

        Transform _room;
        Transform _ownShip;
        int _ownShipId;
        float _ownScanAt;
        readonly List<Renderer> _ownRenderers = new();
        /// <summary>Opaque hull materials and their queue: drawn ahead of the mask for the outside shot (the hull sits inside it).</summary>
        readonly List<Material> _ownMats = new();
        readonly List<int> _ownQueues = new();
        static readonly List<Renderer> Scratch = new();
        const int AheadOfMask = 1980;
        bool _ownShown;
        static Material _maskMat;

        void BindOutside(Transform room)
        {
            _room = room;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "OutsideMask";
            var col = box.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            box.transform.SetParent(room, false);
            // The room's renderers all sit within ±7 m, deck −0.6 m to coffer 4.4 m (measured).
            box.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            box.transform.localScale = new Vector3(14.6f, 5.4f, 14.6f);
            // Not in the citadel's hall, closed by the palace's walls (BridgeDressing toggles it with the layout).
            var hall = room.Find("CitadelShell");
            box.SetActive(hall == null || !hall.gameObject.activeSelf);
            var r = box.GetComponent<MeshRenderer>();
            r.sharedMaterial = MaskMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        void UnbindOutside()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }

        static Material MaskMaterial()
        {
            if (_maskMat != null)
                return _maskMat;
            var shader = Shader.Find("SU/DepthMask");
            // No mask shader in the build: an unlit black box reads as hull in silhouette.
            _maskMat = shader != null ? new Material(shader) : new Material(Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color"));
            _maskMat.name = "SU_OutsideMask";
            if (shader == null && _maskMat.HasProperty("_Color"))
                _maskMat.SetColor("_Color", new Color(0.03f, 0.035f, 0.045f));
            return _maskMat;
        }

        void OnBeginCamera(ScriptableRenderContext _, Camera cam)
        {
            if (!IsHullCamera(cam) || _room == null)
                return;
            if ((cam.transform.position - _room.TransformPoint(new Vector3(0f, 1.6f, 0f))).sqrMagnitude < OutsideDistance * OutsideDistance)
                return;
            if (!OwnShip())
                return;
            _ownShip.SetPositionAndRotation(_room.position, _room.rotation);
            for (var i = 0; i < _ownRenderers.Count; i++)
                if (_ownRenderers[i] != null)
                    _ownRenderers[i].enabled = true;
            // Shared with the other hulls: they draw a little earlier this render too, which changes nothing.
            for (var i = 0; i < _ownMats.Count; i++)
                if (_ownMats[i] != null)
                    _ownMats[i].renderQueue = AheadOfMask;
            _ownShown = true;
        }

        void OnEndCamera(ScriptableRenderContext _, Camera cam)
        {
            if (!_ownShown)
                return;
            _ownShown = false;
            for (var i = 0; i < _ownRenderers.Count; i++)
                if (_ownRenderers[i] != null)
                    _ownRenderers[i].enabled = false;
            for (var i = 0; i < _ownMats.Count; i++)
                if (_ownMats[i] != null)
                    _ownMats[i].renderQueue = _ownQueues[i];
        }

        bool IsHullCamera(Camera cam) =>
            cam != null && ((_cam != null && cam == _cam.Camera) || (_pip != null && cam == _pip.Camera));

        /// <summary>Our inhabited hull in the exterior (hidden there), its mesh renderers cached per ship.</summary>
        bool OwnShip()
        {
            var id = _focus != null ? _focus.ViewFleetId : 0;
            if (id <= 0 || _exterior == null || !_exterior.TryGetFleet(id, out var ship))
            {
                _ownShip = null;
                _ownShipId = 0;
                return false;
            }

            // Re-read now and then: a refit rebuilds the hull's modules.
            if (ship != _ownShip || id != _ownShipId || Time.unscaledTime >= _ownScanAt)
            {
                _ownShip = ship;
                _ownShipId = id;
                _ownScanAt = Time.unscaledTime + 3f;
                _ownRenderers.Clear();
                _ownMats.Clear();
                _ownQueues.Clear();
                // Hull, modules and engine glow; not the wake (a trail would streak across the jump to the pose).
                ship.GetComponentsInChildren(true, Scratch);
                foreach (var r in Scratch)
                {
                    if (r is not MeshRenderer)
                        continue;
                    _ownRenderers.Add(r);
                    var m = r.sharedMaterial;
                    if (m != null && m.renderQueue < 2500 && !_ownMats.Contains(m))
                    {
                        _ownMats.Add(m);
                        _ownQueues.Add(m.renderQueue);
                    }
                }

                Scratch.Clear();
            }

            return _ownRenderers.Count > 0;
        }
    }
}
