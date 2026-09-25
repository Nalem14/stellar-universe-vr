using Core.UI;
using Core.Utils;
using Core.Vfx;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.Holo
{
    /// <summary>
    /// Holo table v2 navigation (docs/design/HOLOTABLE.md §2.5, BattleGroup VR style): grip in empty space
    /// grabs the diorama — one hand pulls it toward you / moves it, two hands scale it (spread) and turn it
    /// (rotate the hands round each other). Seated or standing, the map comes to the captain.
    /// Rim buttons: Recentre, and Galaxy ⟷ System. On the galaxy, one hand pans the star plate (two hands
    /// stay with <see cref="HoloMapController"/>). Grips that already hold something (a ship, the seat) are
    /// left alone.
    /// </summary>
    public sealed class MapManipulator : MonoBehaviour
    {
        const float MinScale = 0.5f;
        const float MaxScale = 3f;
        /// <summary>Hands farther than this from the table centre (m, horizontal, mount scale 1) don't grab.</summary>
        const float Reach = 1.7f;

        HoloZoneMap _map;
        HoloMapController _ctrl;
        NearFarInteractor[] _hands = System.Array.Empty<NearFarInteractor>();
        float _nextScan;

        NearFarInteractor _a;
        NearFarInteractor _b;
        Vector3 _prevA;
        Vector3 _prevB;
        float _resetT = -1f;
        Vector3 _resetFromPos;
        Quaternion _resetFromRot;
        Vector3 _resetFromScale;
        PokeButton _levelButton;
        PokeButton _recentreButton;

        Transform Content => _map != null ? _map.ContentRoot : null;

        public static MapManipulator Build(HoloZoneMap map, HoloMapController ctrl)
        {
            var m = map.gameObject.AddComponent<MapManipulator>();
            m._map = map;
            m._ctrl = ctrl;
            if (ctrl != null)
            {
                ctrl.SystemGesturesExternal = true;
                ctrl.ModeChanged += m.OnModeChanged;
            }

            m.BuildRimButtons();
            return m;
        }

        void OnDestroy()
        {
            if (_ctrl != null)
                _ctrl.ModeChanged -= OnModeChanged;
        }

        void BuildRimButtons()
        {
            // Beside the status strip on the near rim (captain side), tilted up to the eye.
            var z = -WorldScale.HoloDiscRadius * 0.72f;
            var tilt = Quaternion.Euler(50f, 0f, 0f);
            _recentreButton = PokeButton.Create(transform, "RecentreMap", Trans.Get("vr.table.recentre"), new Vector3(-0.44f, 0.06f, z),
                tilt, new Vector2(0.17f, 0.05f), UiKit.Cyan, Recentre);
            _levelButton = PokeButton.Create(transform, "MapLevel", Trans.Get("galaxy"), new Vector3(0.44f, 0.06f, z),
                tilt, new Vector2(0.17f, 0.05f), new Color(0.7f, 0.5f, 1f, 1f), ToggleLevel);
        }

        void ToggleLevel()
        {
            if (_ctrl == null)
                return;
            _ctrl.SetMode(_ctrl.Mode == HoloMapMode.Galaxy ? HoloMapMode.System : HoloMapMode.Galaxy);
        }

        void OnModeChanged(HoloMapMode mode)
        {
            _levelButton?.SetLabel(Trans.Get(mode == HoloMapMode.Galaxy ? "system" : "galaxy"));
            // The battle board brings its own rim console.
            var battle = mode == HoloMapMode.HexBattle;
            if (_levelButton != null)
                _levelButton.gameObject.SetActive(!battle);
            if (_recentreButton != null)
                _recentreButton.gameObject.SetActive(!battle);
            if (battle)
            {
                Release();
                return;
            }

            // The galaxy plate frames itself: the diorama's grab framing starts fresh on either side.
            Release();
            SnapIdentity();
            Unfold();
        }

        /// <summary>Level change: the new map unfolds from its centre (system ⟷ galaxy transition).</summary>
        void Unfold()
        {
            var c = Content;
            if (c == null)
                return;
            c.localScale = Vector3.one * 0.12f;
            _resetT = 0f;
            _resetFromPos = Vector3.zero;
            _resetFromRot = Quaternion.identity;
            _resetFromScale = c.localScale;
            CicCue.Hover(transform.position + Vector3.up * 0.2f);
        }

        /// <summary>Glide the diorama back to its place on the table.</summary>
        public void Recentre()
        {
            var c = Content;
            if (c == null)
                return;
            _resetT = 0f;
            _resetFromPos = c.localPosition;
            _resetFromRot = c.localRotation;
            _resetFromScale = c.localScale;
            CicCue.Ok(transform.position + Vector3.up * 0.2f);
        }

        void SnapIdentity()
        {
            var c = Content;
            if (c == null)
                return;
            c.localPosition = Vector3.zero;
            c.localRotation = Quaternion.identity;
            c.localScale = Vector3.one;
            _resetT = -1f;
        }

        void Release()
        {
            _a = null;
            _b = null;
        }

        void Update()
        {
            var c = Content;
            if (c == null)
                return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                _hands = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            }

            if (_resetT >= 0f)
            {
                _resetT = Mathf.Min(1f, _resetT + Time.deltaTime * 2.5f);
                var u = MotionEase.SmoothInOut(_resetT);
                c.localPosition = Vector3.Lerp(_resetFromPos, Vector3.zero, u);
                c.localRotation = Quaternion.Slerp(_resetFromRot, Quaternion.identity, u);
                c.localScale = Vector3.Lerp(_resetFromScale, Vector3.one, u);
                if (_resetT >= 1f)
                    _resetT = -1f;
                return;
            }

            var mode = _ctrl != null ? _ctrl.Mode : HoloMapMode.System;
            if (mode == HoloMapMode.HexBattle || (_map != null && _map.InteractionLocked))
            {
                Release();
                return;
            }

            // Who grips: free hands (holding nothing) near the table.
            NearFarInteractor first = null, second = null;
            foreach (var h in _hands)
            {
                if (!Grips(h))
                    continue;
                if (first == null)
                    first = h;
                else if (second == null)
                    second = h;
            }

            // Keep hands that started the gesture; a new grip joins as the second hand.
            if (_a != null && !Grips(_a))
                _a = null;
            if (_b != null && !Grips(_b))
                _b = null;
            if (_a == null && _b != null)
            {
                _a = _b;
                _b = null;
                _prevA = _a.transform.position;
            }

            if (_a == null && first != null && NearTable(first))
            {
                _a = first;
                _prevA = _a.transform.position;
                CicCue.Hover(_prevA);
            }

            if (_a != null && _b == null)
            {
                var other = first == _a ? second : first;
                if (other != null && NearTable(other))
                {
                    _b = other;
                    _prevB = _b.transform.position;
                    _prevA = _a.transform.position;
                }
            }

            if (_a == null)
                return;

            var pa = _a.transform.position;
            if (mode == HoloMapMode.Galaxy)
            {
                // Galaxy: one hand pans the plate; two hands are HoloMapController's zoom.
                if (_b == null)
                {
                    var la = transform.InverseTransformPoint(pa);
                    var lp = transform.InverseTransformPoint(_prevA);
                    _map.GalaxyPan(new Vector2(la.x - lp.x, la.z - lp.z));
                }

                _prevA = pa;
                if (_b != null)
                    _prevB = _b.transform.position;
                return;
            }

            if (_b == null)
            {
                // One hand: the map follows the hand (pull it close, lift it, push it away).
                c.position += pa - _prevA;
                _prevA = pa;
            }
            else
            {
                var pb = _b.transform.position;
                var prevMid = (_prevA + _prevB) * 0.5f;
                var mid = (pa + pb) * 0.5f;
                var prevFlat = Flat(_prevB - _prevA);
                var flat = Flat(pb - pa);
                var k = Mathf.Max(0.02f, (pb - pa).magnitude) / Mathf.Max(0.02f, (_prevB - _prevA).magnitude);
                var target = Mathf.Clamp(c.localScale.x * k, MinScale, MaxScale);
                k = target / c.localScale.x;
                var yaw = prevFlat.sqrMagnitude > 1e-4f && flat.sqrMagnitude > 1e-4f
                    ? Vector3.SignedAngle(prevFlat, flat, Vector3.up)
                    : 0f;
                var turn = Quaternion.AngleAxis(yaw, Vector3.up);
                // The point under the hands stays under the hands while turning / scaling.
                c.position = mid + turn * (c.position - prevMid) * k;
                c.rotation = turn * c.rotation;
                c.localScale = Vector3.one * target;
                _prevA = pa;
                _prevB = pb;
            }

            ClampContent(c);
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        bool Grips(NearFarInteractor h) =>
            h != null && h.isActiveAndEnabled && !h.hasSelection && h.selectInput.ReadIsPerformed();

        bool NearTable(NearFarInteractor h)
        {
            var local = transform.InverseTransformPoint(h.transform.position);
            return new Vector2(local.x, local.z).magnitude < Reach && local.y > -0.5f && local.y < 1.1f;
        }

        /// <summary>Never lose the map: stay over the table's reach, above the floor, below the ceiling.</summary>
        static void ClampContent(Transform c)
        {
            var p = c.localPosition;
            var flat = new Vector2(p.x, p.z);
            if (flat.magnitude > 1.4f)
                flat = flat.normalized * 1.4f;
            c.localPosition = new Vector3(flat.x, Mathf.Clamp(p.y, -0.35f, 0.7f), flat.y);
            // Turn only about the vertical: the ecliptic stays level.
            c.localRotation = Quaternion.Euler(0f, c.localEulerAngles.y, 0f);
        }
    }
}
