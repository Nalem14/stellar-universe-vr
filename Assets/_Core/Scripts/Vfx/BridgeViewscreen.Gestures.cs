using Core.App;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.Vfx
{
    /// <summary>
    /// The captain's hands on the main screen, and a second picture on it.
    /// <list type="bullet">
    /// <item><b>Grab and drag</b> (grip, aiming at the screen): orbit the framed subject — or pan the forward
    /// view. <b>Both hands</b>: spread / pinch to zoom. A <b>double grab</b> gives the picture back to the
    /// director. A hand on the shot holds the subject <see cref="ManualHold"/> s like a table pick.</item>
    /// <item><b>Forward view / Auto</b> on the left arm console of the chair.</item>
    /// <item><b>Picture-in-picture</b>: when an event would take the picture while the captain holds it (table,
    /// officer, hands), it plays in an inset instead of being dropped — a small second hull camera, 12 Hz.</item>
    /// </list>
    /// </summary>
    public sealed partial class BridgeViewscreen
    {
        const float ManualHold = 25f;
        const float ForwardHold = 30f;
        const float DegreesPerMetre = 55f;
        const int PipWidth = 384;
        const int PipHeight = 216;

        NearFarInteractor[] _hands = System.Array.Empty<NearFarInteractor>();
        float _nextHandScan;
        readonly Grab[] _grabs = { new(), new() };
        float _lastTap = -9f;
        float _manualUntil;
        float _forwardUntil;
        float _pinch;

        sealed class Grab
        {
            public NearFarInteractor Hand;
            public bool Held;
            public Vector2 Last;
            public Vector2 Down;
            public float DownAt;
        }

        ViewscreenCamera _pip;
        Image _pipFrame;
        RawImage _pipImage;
        TMP_Text _pipLabel;
        Shot _pipShot;
        Transform _pipAnchor;
        float _pipUntil;

        bool ManualHeld => Time.unscaledTime < _manualUntil;
        bool ForwardHeld => Time.unscaledTime < _forwardUntil;

        void BindGestures(Transform room)
        {
            _pip = ViewscreenCamera.Create(room, PipWidth, PipHeight, 1f / 12f, "ViewscreenPip");
            _pipFrame = NewBar(_hud, "PipFrame", new Vector2(-W * 0.5f + 214f, -30f), new Vector2(PipWidth * 0.86f + 8f, PipHeight * 0.86f + 34f),
                new Color(1f, 1f, 1f, 0.9f));
            var inner = NewBar(_pipFrame.transform, "PipBack", new Vector2(0f, -13f), new Vector2(PipWidth * 0.86f, PipHeight * 0.86f),
                new Color(0f, 0f, 0f, 1f));
            var img = new GameObject("PipFeed", typeof(RectTransform), typeof(RawImage));
            img.transform.SetParent(inner.transform, false);
            img.GetComponent<RectTransform>().sizeDelta = new Vector2(PipWidth * 0.86f, PipHeight * 0.86f);
            _pipImage = img.GetComponent<RawImage>();
            _pipImage.texture = _pip.Texture;
            _pipImage.raycastTarget = false;
            _pipLabel = Text(_pipFrame.transform, new Vector2(0f, PipHeight * 0.43f + 2f), new Vector2(PipWidth * 0.84f, 24f), 16f,
                TextAlignmentOptions.Center, true);
            _pipLabel.color = new Color(0.01f, 0.03f, 0.05f, 1f);
            SetLayer(_pipFrame.transform, ViewscreenCamera.HudLayer);
            _pipFrame.gameObject.SetActive(false);
            _pipAnchor = new GameObject("PipAnchor").transform;
            _pipAnchor.SetParent(transform, false);
        }

        // ── Chair buttons (BridgeDirector) ───────────────────────────────────────

        /// <summary>Straight ahead, and stay there a while (table / events keep to the card and the inset).</summary>
        public void ForceForward()
        {
            _tableKey = -1;
            _tableUntil = 0f;
            _gazeUntil = 0f;
            _forwardUntil = Time.unscaledTime + ForwardHold;
            _manualUntil = 0f;
            _cam.ResetManual();
            _nextEval = 0f;
            CicCue.Ok(transform.parent.TransformPoint(_screenCentre));
        }

        /// <summary>Give the picture back to the director.</summary>
        public void Auto()
        {
            _tableKey = -1;
            _tableUntil = 0f;
            _gazeUntil = 0f;
            _forwardUntil = 0f;
            _manualUntil = 0f;
            _commsRequested = false;
            _commsUntil = 0f;
            _cam.ResetManual();
            _nextEval = 0f;
            CicCue.Ok(transform.parent.TransformPoint(_screenCentre));
        }

        // ── Hands ────────────────────────────────────────────────────────────────

        void TickGestures()
        {
            if (Time.unscaledTime >= _nextHandScan)
            {
                _nextHandScan = Time.unscaledTime + 2f;
                _hands = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            }

            var held = 0;
            for (var i = 0; i < _grabs.Length; i++)
            {
                var g = _grabs[i];
                g.Hand = i < _hands.Length ? _hands[i] : null;
                var hand = g.Hand;
                var on = hand != null && hand.isActiveAndEnabled && hand.selectInput.ReadIsPerformed();
                var hit = on && ScreenPoint(hand.transform.position, hand.transform.forward, out var p) ? p : (Vector2?)null;
                if (on && !g.Held && hit.HasValue && !hand.hasSelection)
                {
                    g.Held = true;
                    g.Last = g.Down = hit.Value;
                    g.DownAt = Time.unscaledTime;
                }
                else if (g.Held && !on)
                {
                    g.Held = false;
                    // A short grab without a drag is a tap; two taps give the picture back.
                    if (Time.unscaledTime - g.DownAt < 0.35f && (g.Last - g.Down).magnitude < 0.08f)
                    {
                        if (Time.unscaledTime - _lastTap < 0.55f)
                        {
                            _lastTap = -9f;
                            Auto();
                        }
                        else
                        {
                            _lastTap = Time.unscaledTime;
                        }
                    }
                }

                if (g.Held && hit.HasValue)
                    held++;
            }

            if (held == 0)
            {
                _pinch = 0f;
                return;
            }

            if (held == 2)
            {
                Vector2 a = default, b = default;
                var k = 0;
                foreach (var g in _grabs)
                    if (g.Held && g.Hand != null && ScreenPoint(g.Hand.transform.position, g.Hand.transform.forward, out var p))
                    {
                        if (k++ == 0) a = p;
                        else b = p;
                        g.Last = p;
                    }

                var spread = (a - b).magnitude;
                if (_pinch > 0.05f && spread > 0.05f)
                    _cam.Zoom = Mathf.Clamp(_cam.Zoom * _pinch / spread, 0.3f, 2.5f);
                _pinch = spread;
                HoldByHand();
                return;
            }

            _pinch = 0f;
            foreach (var g in _grabs)
            {
                if (!g.Held || g.Hand == null || !ScreenPoint(g.Hand.transform.position, g.Hand.transform.forward, out var p))
                    continue;
                var d = p - g.Last;
                g.Last = p;
                if (d.sqrMagnitude < 1e-6f)
                    continue;
                // Drag the scene: pull right and the subject turns right (we swing left round it).
                _cam.OrbitYaw = Mathf.Clamp(_cam.OrbitYaw - d.x * DegreesPerMetre, -ViewscreenCamera.MaxOrbitYaw, ViewscreenCamera.MaxOrbitYaw);
                _cam.OrbitPitch = Mathf.Clamp(_cam.OrbitPitch + d.y * DegreesPerMetre, -60f, 60f);
                HoldByHand();
            }
        }

        /// <summary>A hand on the shot keeps its subject, whatever the director had in mind.</summary>
        void HoldByHand()
        {
            _manualUntil = Time.unscaledTime + ManualHold;
            if (_subject != null && _subject.T != null && _subject.Key != PairKey)
            {
                _tableKey = _subject.Key;
                _tableUntil = _manualUntil;
                _tablePreview = null;
            }
            else if (_subject == null)
            {
                _forwardUntil = Mathf.Max(_forwardUntil, _manualUntil);
            }
        }

        /// <summary>Where a ray meets the screen, in metres from its centre (x along the wall, y up).</summary>
        bool ScreenPoint(Vector3 origin, Vector3 dir, out Vector2 at)
        {
            at = default;
            var room = transform.parent;
            var o = room.InverseTransformPoint(origin);
            var d = room.InverseTransformDirection(dir);
            var n = BridgeShell.EdgeNormal(BridgeShell.Forward);
            var denom = Vector3.Dot(d, n);
            if (Mathf.Abs(denom) < 1e-4f)
                return false;
            var t = Vector3.Dot(_screenCentre - o, n) / denom;
            if (t <= 0f || t > 20f)
                return false;
            var p = o + d * t - _screenCentre;
            var along = BridgeShell.EdgeDir(BridgeShell.Forward);
            at = new Vector2(Vector3.Dot(p, along), p.y);
            return Mathf.Abs(at.x) < PanelWidth * 0.5f + 0.25f && Mathf.Abs(at.y) < (PanelTop - PanelBottom) * 0.5f + 0.25f;
        }

        // ── Picture-in-picture ───────────────────────────────────────────────────

        /// <summary>An event the captain's hold kept off the main picture plays in the inset.</summary>
        void ToInset(Shot shot)
        {
            if (shot == null || shot.Forward || shot == _pipShot)
                return;
            if (_pipShot == null)
                CicCue.Pip(transform.parent.TransformPoint(_screenCentre));
            _pipShot = shot;
            // An inset is glanced at, not watched: it stays a little after the event.
            _pipUntil = shot.Until + 2f;
        }

        void TickInset(bool mainActive)
        {
            var on = mainActive && _pipShot != null && Time.unscaledTime < _pipUntil && InsetSubject(out _, out _);
            if (_pipFrame.gameObject.activeSelf != on)
                _pipFrame.gameObject.SetActive(on);
            _pip.Active = on;
            if (!on)
            {
                _pipShot = null;
                return;
            }

            InsetSubject(out var subject, out var r);
            _pip.Track(subject, r);
            _pipFrame.color = Color.Lerp(_pipShot.Tint, Color.white, 0.2f) * (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f));
            Set(_pipLabel, _pipShot.Caption ?? string.Empty);
        }

        bool InsetSubject(out Transform t, out float radius)
        {
            t = null;
            radius = 1f;
            if (_pipShot.Pair)
            {
                var a = FindTarget(_pipShot.A);
                var b = FindTarget(_pipShot.B);
                if (a?.T == null || b?.T == null)
                    return false;
                _pipAnchor.position = (a.T.position + b.T.position) * 0.5f;
                t = _pipAnchor;
                radius = Vector3.Distance(a.T.position, b.T.position) * 0.5f + WorldScale.ShipSpan * 0.5f;
                return true;
            }

            var s = FindTarget(_pipShot.Key);
            if (s?.T == null)
                return false;
            t = s.T;
            radius = s.Radius;
            return true;
        }
    }
}
