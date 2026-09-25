using System;
using System.Collections.Generic;
using Core.App;
using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Stations
{
    /// <summary>
    /// The chamber's centrepiece: a projector pedestal holding our empire's flag at the heart of a slow ring of
    /// every other empire's flag (GetEmpires), each framed in its stance colour and tethered to us by a light
    /// filament — red and pulsing at war, green for alliance members, faint otherwise. Point at a flag (ray or
    /// hand) to open that empire's dossier. One material per flag texture (cached by empire), everything else
    /// shared; the ring turns as one transform, flags yaw toward the viewer (holo labels only).
    /// </summary>
    public sealed class EmpireOrrery : MonoBehaviour
    {
        const float RingRadius = 1.45f;
        const float RingHeight = 1.32f;
        const float CoreHeight = 1.5f;
        const float SpinDegPerSec = 1.6f;
        const int MaxNodes = 24;
        static readonly Vector2 FlagSize = new(0.36f, 0.24f);
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public readonly struct Entry
        {
            public readonly int EmpireId;
            public readonly string Name;
            public readonly string Flag;
            public readonly EmpireStance Stance;
            public readonly bool AtWar;
            public readonly bool Member;

            public Entry(int empireId, string name, string flag, EmpireStance stance, bool atWar, bool member)
            {
                EmpireId = empireId;
                Name = name;
                Flag = flag;
                Stance = stance;
                AtWar = atWar;
                Member = member;
            }
        }

        sealed class Node
        {
            public int EmpireId;
            public Transform Root;
            public Transform Face;
            public MeshRenderer Flag;
            public MeshRenderer Frame;
            public TextMeshPro Label;
            public Transform Tether;
            public MeshRenderer TetherRenderer;
            public float Scale = 1f;
            public bool Hover;
        }

        CicArtKit _art;
        Transform _ring;
        Transform _core;
        MeshRenderer _coreFlag;
        MeshRenderer _coreFlagBack;
        Transform _bracket;
        Material _warTether;
        Material _allyTether;
        Material _quietTether;
        Material _beam;
        readonly List<Node> _nodes = new();
        readonly Dictionary<string, Material> _flagMats = new();
        MaterialPropertyBlock _block;
        int _selected;
        int _myEmpire;
        Action<int> _onPick;

        public static EmpireOrrery Build(Transform room, Vector3 localPos, CicArtKit art, Color accent, Action<int> onPick)
        {
            var go = new GameObject("EmpireOrrery");
            go.transform.SetParent(room, false);
            go.transform.localPosition = localPos;
            var o = go.AddComponent<EmpireOrrery>();
            o._art = art;
            o._onPick = onPick;
            o.BuildPedestal(accent);
            return o;
        }

        void OnDestroy()
        {
            if (_warTether != null)
                Destroy(_warTether);
            foreach (var m in _flagMats.Values)
                if (m != null)
                {
                    Destroy(m.mainTexture);
                    Destroy(m);
                }
        }

        void BuildPedestal(Color accent)
        {
            var chassis = UiKit.Chassis;
            var gold = _art.Lit(Texture2D.whiteTexture, accent, 2.6f);
            // Stepped plinth, projector drum, a lit lens under the hologram.
            GateRoomDecor.Rounded(transform, "Plinth", new Vector3(1.5f, 0.16f, 1.5f), 0.2f, new Vector3(0f, 0.08f, 0f), chassis,
                accent, 0.3f);
            GateRoomDecor.Rounded(transform, "Drum", new Vector3(0.9f, 0.62f, 0.9f), 0.2f, new Vector3(0f, 0.47f, 0f), chassis,
                accent, 0.22f);
            GateRoomDecor.Rounded(transform, "Crown", new Vector3(1.05f, 0.08f, 1.05f), 0.16f, new Vector3(0f, 0.82f, 0f), chassis,
                accent, 0.6f);
            for (var i = 0; i < 8; i++)
            {
                var a = i * 45f;
                var rib = GateRoomDecor.Box(transform, "DrumRib", Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.47f, 0.455f),
                    new Vector3(0.05f, 0.5f, 0.02f), gold);
                rib.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            }

            var lens = GateRoomDecor.Quad(transform, "Lens", new Vector3(0f, 0.865f, 0f), new Vector3(0.8f, 0.8f, 1f),
                _art.RadarIcon(_art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture,
                    new Color(accent.r, accent.g, accent.b, 0.9f)));
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Projection cone: a soft additive shaft from the lens up to the core flag.
            _beam = _art.Holo(Texture2D.whiteTexture, new Color(accent.r, accent.g, accent.b, 0.08f));
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cone.name = "ProjectionShaft";
            Destroy(cone.GetComponent<Collider>());
            cone.transform.SetParent(transform, false);
            cone.transform.localPosition = new Vector3(0f, (0.87f + CoreHeight) * 0.5f, 0f);
            cone.transform.localScale = new Vector3(0.55f, (CoreHeight - 0.87f) * 0.5f, 0.55f);
            var cr = cone.GetComponent<MeshRenderer>();
            cr.sharedMaterial = _beam;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Our flag at the heart, back to back so it reads from the benches too.
            _core = new GameObject("Core").transform;
            _core.SetParent(transform, false);
            _core.localPosition = new Vector3(0f, CoreHeight, 0f);
            _coreFlag = GateRoomDecor.Quad(_core, "Flag", Vector3.zero, new Vector3(0.48f, 0.32f, 1f), _beam)
                .GetComponent<MeshRenderer>();
            _coreFlagBack = GateRoomDecor.Quad(_core, "FlagBack", Vector3.zero, new Vector3(0.48f, 0.32f, 1f), _beam)
                .GetComponent<MeshRenderer>();
            _coreFlagBack.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var halo = GateRoomDecor.Quad(_core, "Halo", new Vector3(0f, -0.26f, 0f), new Vector3(0.62f, 0.62f, 1f),
                _art.RadarIcon(_art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture,
                    new Color(accent.r, accent.g, accent.b, 0.7f)));
            halo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            _ring = new GameObject("Ring").transform;
            _ring.SetParent(transform, false);
            _ring.localPosition = Vector3.zero;

            DiplomacyDecor.Ring(transform, "OrbitTrack", new Vector3(0f, RingHeight - 0.16f, 0f), RingRadius - 0.012f,
                RingRadius + 0.012f, true, _art.Lit(Texture2D.whiteTexture, accent, 1.4f));

            // Own instance: it pulses, and the kit's materials are shared by key.
            _warTether = new Material(_art.Holo(Texture2D.whiteTexture, new Color(1f, 0.25f, 0.2f, 0.7f))) { name = "SU_WarTether" };
            _allyTether = _art.Holo(Texture2D.whiteTexture, new Color(0.35f, 1f, 0.55f, 0.55f));
            _quietTether = _art.Holo(Texture2D.whiteTexture, new Color(0.55f, 0.7f, 0.85f, 0.12f));

            _bracket = GateRoomDecor.Quad(transform, "SelectBracket", Vector3.zero, new Vector3(0.55f, 0.55f, 1f),
                _art.RadarIcon(_art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture, new Color(1f, 0.85f, 0.45f, 0.95f)))
                .transform;
            _bracket.gameObject.SetActive(false);
        }

        /// <summary>Show these empires around ours (ring order as given; capped at <see cref="MaxNodes"/>).</summary>
        public void SetEmpires(int myEmpire, string myFlag, IList<Entry> others)
        {
            _myEmpire = myEmpire;
            var coreMat = FlagMaterial(myEmpire, myFlag);
            _coreFlag.sharedMaterial = coreMat;
            _coreFlagBack.sharedMaterial = coreMat;

            _block ??= new MaterialPropertyBlock();
            var count = Mathf.Min(others.Count, MaxNodes);
            while (_nodes.Count < count)
                _nodes.Add(MakeNode());
            for (var i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                var on = i < count;
                n.Root.gameObject.SetActive(on);
                n.Tether.gameObject.SetActive(on);
                if (!on)
                    continue;
                var e = others[i];
                n.EmpireId = e.EmpireId;
                var a = (i / (float)count) * Mathf.PI * 2f;
                n.Root.localPosition = new Vector3(Mathf.Sin(a) * RingRadius, RingHeight, Mathf.Cos(a) * RingRadius);
                n.Flag.sharedMaterial = FlagMaterial(e.EmpireId, e.Flag);
                n.Label.text = "<noparse>" + e.Name + "</noparse>";
                var tint = e.AtWar ? new Color(1f, 0.25f, 0.2f) : e.Member ? new Color(0.35f, 1f, 0.55f) : DiplomacyIndex.Tint(e.Stance);
                n.Label.color = Color.Lerp(tint, Color.white, 0.35f);
                _block.Clear();
                _block.SetColor(UiKit.AccentId, tint);
                _block.SetFloat(UiKit.AccentMulId, e.AtWar ? 1.8f : 0.9f);
                n.Frame.SetPropertyBlock(_block);

                // Tether from the core to the flag (ring local; the ring turns them together).
                var from = new Vector3(0f, CoreHeight, 0f);
                var to = n.Root.localPosition;
                n.Tether.localPosition = (from + to) * 0.5f;
                n.Tether.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
                var thick = e.AtWar ? 0.018f : e.Member ? 0.012f : 0.006f;
                n.Tether.localScale = new Vector3(thick, (to - from).magnitude * 0.5f, thick);
                n.TetherRenderer.sharedMaterial = e.AtWar ? _warTether : e.Member ? _allyTether : _quietTether;
            }

            Select(_selected);
        }

        public void Select(int empireId)
        {
            _selected = empireId;
            Node hit = null;
            foreach (var n in _nodes)
                if (n.Root.gameObject.activeSelf && n.EmpireId == empireId)
                    hit = n;
            _bracket.gameObject.SetActive(hit != null);
            if (hit != null)
            {
                _bracket.SetParent(hit.Root, false);
                _bracket.localPosition = new Vector3(0f, 0f, 0.02f);
                _bracket.localRotation = Quaternion.identity;
            }
        }

        Node MakeNode()
        {
            var n = new Node();
            n.Root = new GameObject("EmpireNode").transform;
            n.Root.SetParent(_ring, false);
            var face = new GameObject("Face").transform;
            face.SetParent(n.Root, false);
            n.Flag = GateRoomDecor.Quad(face, "Flag", Vector3.zero, new Vector3(FlagSize.x, FlagSize.y, 1f), _beam)
                .GetComponent<MeshRenderer>();
            n.Frame = GateRoomDecor.Rounded(face, "Frame", new Vector3(FlagSize.x + 0.03f, FlagSize.y + 0.03f, 0.012f), 0.01f,
                new Vector3(0f, 0f, 0.012f), UiKit.Chassis, Color.white, 0.9f).GetComponent<MeshRenderer>();
            n.Label = UiKit.Label(face, "Name", string.Empty, new Vector3(0f, -0.175f, 0f), 0.6f, 0.05f, UiKit.TextBright);
            n.Label.richText = true;
            n.Face = face;

            var col = n.Root.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(0.42f, 0.36f, 0.08f);
            var xi = n.Root.gameObject.AddComponent<XRSimpleInteractable>();
            xi.selectEntered.AddListener(_ =>
            {
                CicCue.Ok(n.Root.position);
                _onPick?.Invoke(n.EmpireId);
            });
            xi.hoverEntered.AddListener(_ =>
            {
                n.Hover = true;
                CicCue.Hover(n.Root.position);
            });
            xi.hoverExited.AddListener(_ => n.Hover = false);

            var tether = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tether.name = "Tether";
            Destroy(tether.GetComponent<Collider>());
            tether.transform.SetParent(_ring, false);
            n.Tether = tether.transform;
            n.TetherRenderer = tether.GetComponent<MeshRenderer>();
            n.TetherRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return n;
        }

        /// <summary>Material for an empire flag (painted once per empire + flag, shared by the hall: seats, banners).</summary>
        public Material FlagMaterial(int empireId, string flagJson)
        {
            var key = empireId + "|" + flagJson;
            if (_flagMats.TryGetValue(key, out var m))
                return m;
            var tex = FlagPainter.Paint(FlagSpec.FromJson(flagJson));
            // No emission: the kit's shader adds it flat, which would wash the colours out.
            m = _art.Lit(tex, Color.white, 0f);
            m.name = "SU_Flag_" + empireId;
            _flagMats[key] = m;
            return m;
        }

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            var now = Time.unscaledTime;
            _ring.localRotation = Quaternion.Euler(0f, now * SpinDegPerSec, 0f);
            _core.localRotation = Quaternion.Euler(0f, now * 9f, 0f);
            _core.localPosition = new Vector3(0f, CoreHeight + Mathf.Sin(now * 0.8f) * 0.015f, 0f);

            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : transform.position + transform.forward * -3f;
            foreach (var n in _nodes)
            {
                if (!n.Root.gameObject.activeSelf)
                    continue;
                // Flags turn to the viewer around the vertical only (front = −Z, like every kit piece).
                var d = n.Root.position - eye;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f)
                    n.Root.rotation = Quaternion.LookRotation(d, Vector3.up);
                var target = n.EmpireId == _selected ? 1.3f : n.Hover ? 1.18f : 1f;
                n.Scale = Mathf.MoveTowards(n.Scale, target, dt * 2.5f);
                n.Face.localScale = Vector3.one * n.Scale;
            }

            var pulse = 0.45f + 0.35f * Mathf.Sin(now * 4f);
            _warTether.SetColor(ColorId, new Color(1f, 0.25f, 0.2f, pulse));
        }

        public int MyEmpire => _myEmpire;
    }
}
