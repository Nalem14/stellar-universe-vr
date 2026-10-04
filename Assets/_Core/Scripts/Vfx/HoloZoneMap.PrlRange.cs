using System.Collections.Generic;
using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Bond PRL reach of the ship picked on the galaxy table (web PanelUnitsUI prlRangeCircle): a dashed ring of
    /// radius GetConfigs.prlBond baseRange + prlBond level × rangePerResearchLevel (map units, measured from the
    /// ship's system as PrlBondFleetToSystem does), with its reach written on it. Drawn only while the module can
    /// jump (enough bond modules, not recharging). A thin line, never a field: it cannot pass for the radar's
    /// dotted screen or a territory. Clipped to the plate; one mesh rebuilt on pick / pan / zoom only.
    /// </summary>
    public partial class HoloZoneMap
    {
        static readonly Color PrlTint = new(0.45f, 0.7f, 1f, 1f);
        const float PrlLineWidth = 0.0045f;
        const float PrlDash = 0.02f;

        int _prlFleetId;
        float _prlLift;
        GameObject _prlRing;
        Mesh _prlMesh;
        TextMeshPro _prlLabel;
        readonly List<Vector3> _prlVerts = new();
        readonly List<int> _prlTris = new();

        /// <summary>Show the Bond PRL reach of this ship on the galaxy (null or no usable module = hide).</summary>
        public void ShowPrlRange(FocusFleet fleet)
        {
            var id = fleet != null && Core.Holo.TravelPlanner.PrlReady(fleet) ? fleet.Id : 0;
            if (id == _prlFleetId && (id == 0 || _prlRing != null))
                return;
            _prlFleetId = id;
            UpdatePrlRange(_prlLift);
        }

        void UpdatePrlRange(float lift)
        {
            _prlLift = lift;
            var focus = _focus ?? FocusContext.Current;
            var fleet = _prlFleetId > 0 && focus != null ? focus.FindFleet(_prlFleetId) : null;
            if (!_showingGalaxy || fleet == null || !Core.Holo.TravelPlanner.PrlReady(fleet) ||
                !GalaxyCatalog.TryGet(fleet.SystemId, out var star))
            {
                if (_prlRing != null && _prlRing.activeSelf)
                    _prlRing.SetActive(false);
                return;
            }

            EnsurePrlRing();
            var plate = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor * 0.985f;
            var c = MapToLocal(star.MapX, star.MapY, lift + 0.003f);
            var r = Core.Holo.TravelPlanner.PrlMaxRange() * _gScale;
            BuildDashedRing(new Vector2(c.x, c.z), r, plate, c.y, out var labelAt, out var anyVisible);
            _prlRing.SetActive(anyVisible);
            if (!anyVisible)
                return;
            _prlLabel.text = Trans.Get("bondPrlJump") + "  " +
                             Mathf.RoundToInt(Core.Holo.TravelPlanner.PrlMaxRange()).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _prlLabel.transform.localPosition = labelAt + Vector3.up * 0.03f;
        }

        void EnsurePrlRing()
        {
            if (_prlRing != null)
                return;
            _prlRing = new GameObject("PrlRange");
            _prlRing.transform.SetParent(_root, false);
            _prlMesh ??= new Mesh { name = "PrlRange" };
            _prlRing.AddComponent<MeshFilter>().sharedMesh = _prlMesh;
            var mr = _prlRing.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _art.RadarIcon(Texture2D.whiteTexture, PrlTint);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _prlLabel = UiKit.Label(_prlRing.transform, "Reach", string.Empty, Vector3.zero, 0.36f, 0.022f, PrlTint);
            _prlLabel.fontStyle = FontStyles.Bold;
            _prlLabel.outlineWidth = 0.2f;
            _prlLabel.outlineColor = new Color32(2, 10, 16, 230);
            _prlLabel.gameObject.AddComponent<BillboardFace>();
            // Rebuilt with the galaxy tokens (ClearTokens destroys it; the next update makes a new one).
            _tokenRoots.Add(_prlRing);
        }

        /// <summary>Dashes along the circle, each kept only where it lies over the plate.</summary>
        void BuildDashedRing(Vector2 centre, float radius, float plate, float y, out Vector3 labelAt, out bool any)
        {
            _prlVerts.Clear();
            _prlTris.Clear();
            labelAt = new Vector3(centre.x, y, centre.y);
            any = false;
            var bestToEye = float.MaxValue;
            var eye = Camera.main != null ? _root.InverseTransformPoint(Camera.main.transform.position) : Vector3.back;
            var count = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / PrlDash), 24, 720);
            if (count % 2 == 1)
                count++;
            var inner = radius - PrlLineWidth * 0.5f;
            var outer = radius + PrlLineWidth * 0.5f;
            for (var i = 0; i < count; i += 2)
            {
                var a0 = i / (float)count * Mathf.PI * 2f;
                var a1 = (i + 1.25f) / count * Mathf.PI * 2f;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                var p0 = centre + d0 * radius;
                var p1 = centre + d1 * radius;
                if (p0.sqrMagnitude > plate * plate || p1.sqrMagnitude > plate * plate)
                    continue;
                any = true;
                var b = _prlVerts.Count;
                _prlVerts.Add(new Vector3(centre.x + d0.x * inner, y, centre.y + d0.y * inner));
                _prlVerts.Add(new Vector3(centre.x + d0.x * outer, y, centre.y + d0.y * outer));
                _prlVerts.Add(new Vector3(centre.x + d1.x * inner, y, centre.y + d1.y * inner));
                _prlVerts.Add(new Vector3(centre.x + d1.x * outer, y, centre.y + d1.y * outer));
                _prlTris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2, b, b + 3, b + 1, b, b + 2, b + 3 });
                // The reach is written on the stretch of ring nearest the captain.
                var toEye = (new Vector2(eye.x, eye.z) - p0).sqrMagnitude;
                if (toEye < bestToEye)
                {
                    bestToEye = toEye;
                    labelAt = new Vector3(p0.x, y, p0.y);
                }
            }

            _prlMesh.Clear();
            _prlMesh.SetVertices(_prlVerts);
            _prlMesh.SetTriangles(_prlTris, 0);
            _prlMesh.RecalculateBounds();
        }
    }
}
