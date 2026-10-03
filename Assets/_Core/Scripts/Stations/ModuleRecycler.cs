using System;
using System.Threading.Tasks;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The dry dock's recycler, between the printer's console and the printer: a hazard-rimmed hopper with
    /// shredder rollers glowing in its maw. Drop a block taken from the module store into it and it hangs over
    /// the maw, turning, while the panel asks to confirm — Recycle = DelShip (no refund, as on the web hangar),
    /// Cancel or 15 s without an answer = nothing happens (the store already has the block back). Recycled: the
    /// block sinks into the rollers, dissolving. Quest budget: frame merged by static batching, the block in one
    /// draw of the shared module material, two labels, Update asleep while idle.
    /// Local frame: origin on the floor at the wall face, centre of the hopper; the room lies toward -Z.
    /// </summary>
    public sealed class ModuleRecycler : MonoBehaviour
    {
        const float Width = 0.62f;
        const float Depth = 0.56f;
        const float RimY = 0.95f;
        const float HoverY = 1.12f;
        const float OfferTime = 15f;
        const float ShredTime = 1.1f;
        const float Solid = 1.1f;
        static readonly Color Hot = new(1f, 0.42f, 0.18f, 1f);
        static readonly int AccentId = Shader.PropertyToID("_Accent");
        static readonly int HoverId = Shader.PropertyToID("_Hover");
        static readonly int GhostId = Shader.PropertyToID("_Ghost");
        static readonly int RevealId = Shader.PropertyToID("_Reveal");

        CicArtKit _art;
        Func<string, Task<string>> _recycle;
        MaterialPropertyBlock _mpb;
        Transform _block;
        MeshFilter _blockFilter;
        MeshRenderer _blockRenderer;
        Transform[] _rollers;
        MeshRenderer _maw;
        TextMeshPro _prompt;
        PokeButton _go;
        PokeButton _cancel;
        string _type;
        float _offerUntil;
        float _shredT = -1f;
        bool _busy;

        public static ModuleRecycler Build(Transform room, CicArtKit art, Vector3 localPos, Quaternion localRot,
            Func<string, Task<string>> recycle)
        {
            var go = new GameObject("ModuleRecycler");
            go.transform.SetParent(room, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var r = go.AddComponent<ModuleRecycler>();
            r._art = art;
            r._recycle = recycle;
            r._mpb = new MaterialPropertyBlock();
            r.BuildFrame();
            r.BuildPanel();
            r.Clear();
            return r;
        }

        /// <summary>A released block landed in the hopper's mouth (anywhere above the rim, inside its walls).</summary>
        public bool Catches(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < Width * 0.5f + 0.06f && p.y > RimY - 0.2f && p.y < RimY + 0.6f &&
                   p.z < 0.02f && p.z > -Depth - 0.08f;
        }

        public Vector3 MawWorld => transform.TransformPoint(new Vector3(0f, RimY, -Depth * 0.5f));

        // ── Build ─────────────────────────────────────────────────────────────────

        GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!solid)
                Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        void BuildFrame()
        {
            var frame = new GameObject("Frame").transform;
            frame.SetParent(transform, false);
            var wall = _art.MetalPanel(0.5f);
            var dark = _art.DarkPanel(0.3f);
            var amber = _art.AmberEmit(2f);
            const float hw = Width * 0.5f;
            const float cz = -Depth * 0.5f;

            // Cabinet: four walls up to the rim (the maw stays open), a plinth, a sloped feed lip toward the room.
            Box(frame, "Plinth", new Vector3(0f, 0.05f, cz), new Vector3(Width + 0.04f, 0.1f, Depth + 0.04f), dark, true);
            Box(frame, "WallFront", new Vector3(0f, RimY * 0.5f, -Depth + 0.025f), new Vector3(Width, RimY, 0.05f), wall, true);
            Box(frame, "WallBack", new Vector3(0f, RimY * 0.5f, -0.025f), new Vector3(Width, RimY, 0.05f), wall, true);
            foreach (var side in new[] { -1f, 1f })
                Box(frame, "WallSide" + side, new Vector3(side * (hw - 0.025f), RimY * 0.5f, cz), new Vector3(0.05f, RimY, Depth), wall, true);
            Box(frame, "Floor", new Vector3(0f, RimY - 0.42f, cz), new Vector3(Width - 0.1f, 0.02f, Depth - 0.1f), dark);

            // Hazard rim: alternating amber / dark blocks all round the mouth.
            const int n = 8;
            for (var i = 0; i < n; i++)
            {
                var mat = i % 2 == 0 ? amber : dark;
                var u = (i + 0.5f) / n - 0.5f;
                Box(frame, "RimF" + i, new Vector3(u * Width, RimY + 0.012f, -Depth + 0.025f), new Vector3(Width / n, 0.024f, 0.055f), mat);
                Box(frame, "RimB" + i, new Vector3(u * Width, RimY + 0.012f, -0.025f), new Vector3(Width / n, 0.024f, 0.055f), mat);
            }

            foreach (var side in new[] { -1f, 1f })
                Box(frame, "RimS" + side, new Vector3(side * (hw - 0.025f), RimY + 0.012f, cz), new Vector3(0.055f, 0.024f, Depth), amber);

            // Feed chute rising behind the hopper to the wall, with the panel on top.
            Box(frame, "Chute", new Vector3(0f, (RimY + 1.72f) * 0.5f, -0.06f), new Vector3(Width * 0.9f, 1.72f - RimY, 0.12f), dark, true);
            Box(frame, "ChuteSeam", new Vector3(0f, (RimY + 1.72f) * 0.5f, -0.121f), new Vector3(0.012f, (1.72f - RimY) * 0.7f, 0.004f), amber);
            // Front warning band.
            Box(frame, "Band", new Vector3(0f, 0.32f, -Depth - 0.002f), new Vector3(Width * 0.9f, 0.03f, 0.006f), amber);
            StaticBatchingUtility.Combine(frame.gameObject);

            // Maw: the shredder's glow below the rim, two rollers turning in it while it works.
            var maw = GameObject.CreatePrimitive(PrimitiveType.Quad);
            maw.name = "MawGlow";
            Destroy(maw.GetComponent<Collider>());
            maw.transform.SetParent(transform, false);
            maw.transform.localPosition = new Vector3(0f, RimY - 0.4f, cz);
            maw.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            maw.transform.localScale = new Vector3(Width - 0.12f, Depth - 0.12f, 1f);
            _maw = maw.GetComponent<MeshRenderer>();
            _maw.sharedMaterial = _art.Holo(_art.ProjectorGlow != null ? _art.ProjectorGlow : Texture2D.whiteTexture,
                new Color(Hot.r, Hot.g, Hot.b, 0.55f));
            _maw.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _rollers = new Transform[2];
            for (var i = 0; i < 2; i++)
            {
                var pivot = new GameObject("Roller" + i).transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = new Vector3(0f, RimY - 0.3f, cz + (i == 0 ? -0.09f : 0.09f));
                var drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                drum.name = "Drum";
                Destroy(drum.GetComponent<Collider>());
                drum.transform.SetParent(pivot, false);
                drum.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                drum.transform.localScale = new Vector3(0.12f, (Width - 0.12f) * 0.5f, 0.12f);
                drum.GetComponent<MeshRenderer>().sharedMaterial = wall;
                // Teeth: four blades along the drum, so the turning reads.
                for (var k = 0; k < 4; k++)
                {
                    var blade = Box(pivot, "Tooth" + k, Vector3.zero, new Vector3(Width - 0.14f, 0.025f, 0.03f), dark);
                    var a = k * 90f * Mathf.Deg2Rad;
                    blade.transform.localPosition = new Vector3(0f, Mathf.Sin(a) * 0.065f, Mathf.Cos(a) * 0.065f);
                    blade.transform.localRotation = Quaternion.Euler(k * 90f, 0f, 0f);
                }

                _rollers[i] = pivot;
            }

            // The offered block, hanging over the maw.
            var block = new GameObject("Offered");
            block.transform.SetParent(transform, false);
            block.transform.localPosition = new Vector3(0f, HoverY, cz);
            _block = block.transform;
            _blockFilter = block.AddComponent<MeshFilter>();
            _blockRenderer = block.AddComponent<MeshRenderer>();
            _blockRenderer.sharedMaterial = ModuleShelves.BlockMat();
            _blockRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _blockRenderer.receiveShadows = false;
        }

        void BuildPanel()
        {
            // Sign and prompt on the chute's face, the two buttons under them — hand height over the hopper.
            var face = -0.125f;
            var sign = UiKit.Label(transform, "Sign", Trans.Get("vr.dock.recycler.title"),
                new Vector3(0f, 1.66f, face - 0.002f), Width * 0.85f, 0.045f, UiKit.Amber);
            sign.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            _prompt = UiKit.Label(transform, "Prompt", string.Empty, new Vector3(0f, 1.52f, face - 0.002f), Width * 0.85f,
                0.022f, UiKit.TextBright, TextAlignmentOptions.Center, wrap: true);
            _prompt.richText = true;
            _prompt.rectTransform.sizeDelta = new Vector2(_prompt.rectTransform.sizeDelta.x, 14f);
            _go = PokeButton.Create(transform, "Recycle", Trans.Get("vr.dock.recycler.go"),
                new Vector3(-0.14f, 1.36f, face - 0.004f), Quaternion.identity, new Vector2(0.24f, 0.075f), UiKit.Danger, Confirm);
            _cancel = PokeButton.Create(transform, "Cancel", Trans.Get("cancel"),
                new Vector3(0.14f, 1.36f, face - 0.004f), Quaternion.identity, new Vector2(0.24f, 0.075f), UiKit.Cyan, Clear);
        }

        // ── Flow ──────────────────────────────────────────────────────────────────

        /// <summary>A store block dropped in the hopper: hold it over the maw and ask.</summary>
        public void Offer(string type)
        {
            if (_busy || _shredT >= 0f)
                return;
            _type = type;
            _offerUntil = Time.unscaledTime + OfferTime;
            _blockFilter.sharedMesh = ModuleShelves.BlockMesh(type);
            _block.localPosition = new Vector3(0f, HoverY, -Depth * 0.5f);
            _block.gameObject.SetActive(true);
            SetBlock(Solid, 0.6f);
            _prompt.text = Trans.Format("vr.dock.recycler.confirm", Trans.Get(ModuleCatalog.NameKey(type)));
            _prompt.color = UiKit.Amber;
            _go.Interactive = true;
            _cancel.Interactive = true;
            CicCue.Clunk(_block.position);
            enabled = true;
        }

        void SetBlock(float reveal, float hover)
        {
            _mpb.SetColor(AccentId, _type != null ? ModuleCatalog.Accent(ModuleCatalog.Family(_type)) : Hot);
            _mpb.SetFloat(HoverId, hover);
            _mpb.SetFloat(GhostId, 0f);
            _mpb.SetFloat(RevealId, reveal);
            _blockRenderer.SetPropertyBlock(_mpb);
        }

        void Confirm()
        {
            if (_type == null || _busy)
                return;
            AsyncTap.Run(Recycle(_type));
        }

        async Task Recycle(string type)
        {
            _busy = true;
            _go.Interactive = false;
            _cancel.Interactive = false;
            string error;
            try
            {
                error = await _recycle(type);
            }
            finally
            {
                _busy = false;
            }

            if (this == null)
                return;
            if (error != null)
            {
                Clear(error);
                return;
            }

            _prompt.text = Trans.Get("vr.dock.scrapped");
            _prompt.color = UiKit.Ok;
            _shredT = 0f;
            CicCue.Synth(MawWorld);
            enabled = true;
        }

        /// <summary>Nothing offered: hopper idle, buttons dimmed, the prompt saying what it is for (or why it refused).</summary>
        public void Clear() => Clear(null);

        void Clear(string error)
        {
            if (_busy)
                return;
            _type = null;
            _shredT = -1f;
            _block.gameObject.SetActive(false);
            _prompt.text = error ?? Trans.Get("vr.dock.recycler.hint");
            _prompt.color = error != null ? UiKit.Danger : UiKit.TextBright;
            _go.Interactive = false;
            _cancel.Interactive = false;
            enabled = false;
        }

        void Update()
        {
            var dt = Time.deltaTime;
            if (_shredT >= 0f)
            {
                _shredT += dt / ShredTime;
                var k = Mathf.Clamp01(_shredT);
                _block.localPosition = new Vector3(0f, Mathf.Lerp(HoverY, RimY - 0.3f, k * k), -Depth * 0.5f);
                _block.localRotation = Quaternion.Euler(0f, 115f + k * 160f, 0f);
                SetBlock(Solid * (1f - k), 1f);
                foreach (var r in _rollers)
                    r.localRotation *= Quaternion.Euler(720f * dt * (r == _rollers[0] ? 1f : -1f), 0f, 0f);
                if (_shredT >= 1f)
                {
                    _shredT = -1f;
                    Clear();
                }

                return;
            }

            if (_type == null)
            {
                enabled = false;
                return;
            }

            _block.localRotation = Quaternion.Euler(0f, 115f + Time.unscaledTime * 40f, 0f);
            if (!_busy && Time.unscaledTime > _offerUntil)
                Clear();
        }
    }
}
