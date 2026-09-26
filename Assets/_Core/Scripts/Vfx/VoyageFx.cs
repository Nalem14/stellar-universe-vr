using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// What the windows show between two systems, riding the inhabited ship (child of the view ship).
    /// One of each part, built on the first voyage and switched off at rest (no idle cost):
    /// <list type="bullet">
    /// <item>star streaks — hand-emitted stretched glow particles in a ring 35–150 m off the hull (never
    /// through the rooms), flowing aft; slow dust under sub-light, long light-lines when a drive kicks in;</item>
    /// <item>the transit tube (SU/WarpTunnel) — blue vortex for hyperspace, violet rings for a gate fold,
    /// gold lattice for Bond PRL;</item>
    /// <item>the PRL cage — a lattice sphere that closes on the hull before the bond snaps;</item>
    /// <item>flashes and shock rings, the origin / destination star glints under sub-light;</item>
    /// <item>the light washing into the rooms (global _SU_VoyageTint read by SU/HullInterior) and the
    /// drive audio (procedural spool, jump, drop-out, cruise hum).</item>
    /// </list>
    /// No camera shake, no roll: the room stays still, only space moves (Quest comfort).
    /// </summary>
    public sealed class VoyageFx : MonoBehaviour
    {
        const float TunnelRadius = 62f;
        const float TunnelLength = 1500f;
        const float StreakAhead = 700f;
        const float StreakInner = 35f;
        const float StreakOuter = 150f;
        const int StreakMax = 600;

        static readonly int TintId = Shader.PropertyToID("_SU_VoyageTint");
        static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int ColAId = Shader.PropertyToID("_ColA");
        static readonly int ColBId = Shader.PropertyToID("_ColB");
        static readonly int TwistId = Shader.PropertyToID("_Twist");
        static readonly int DensityId = Shader.PropertyToID("_Density");
        static readonly int RibsId = Shader.PropertyToID("_Ribs");
        static readonly int HexId = Shader.PropertyToID("_Hex");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static Texture2D _noise;
        static Mesh _tube;
        static Mesh _sphere;

        Transform _tunnel;
        MeshRenderer _tunnelR;
        Material _tunnelMat;
        Transform _cage;
        MeshRenderer _cageR;
        Material _cageMat;
        ParticleSystem _streaks;
        ParticleSystemRenderer _streakR;
        ParticleSystem _bursts;
        Transform _glintAhead;
        Transform _glintAft;
        Material _glintMatAhead;
        Material _glintMatAft;
        AudioSource _hum;
        AudioSource _cue;
        bool _built;

        /// <summary>Palette of a drive: tube deep / bright, streaks, room wash.</summary>
        public struct Look
        {
            public Color Deep;
            public Color Bright;
            public Color Streak;
            public Color Wash;
        }

        public static Look LookOf(VoyageMode mode) => mode switch
        {
            VoyageMode.Hyperspace => new Look
            {
                Deep = new Color(0.03f, 0.08f, 0.38f), Bright = new Color(0.45f, 0.82f, 1f),
                Streak = new Color(0.7f, 0.9f, 1f), Wash = new Color(0.13f, 0.25f, 0.58f)
            },
            VoyageMode.PrlBond => new Look
            {
                Deep = new Color(0.3f, 0.14f, 0.02f), Bright = new Color(1f, 0.8f, 0.38f),
                Streak = new Color(1f, 0.86f, 0.55f), Wash = new Color(0.42f, 0.28f, 0.08f)
            },
            VoyageMode.Jumpgate => new Look
            {
                Deep = new Color(0.16f, 0.04f, 0.38f), Bright = new Color(0.78f, 0.58f, 1f),
                Streak = new Color(0.85f, 0.72f, 1f), Wash = new Color(0.26f, 0.12f, 0.42f)
            },
            _ => new Look
            {
                Deep = new Color(0.02f, 0.05f, 0.12f), Bright = new Color(0.5f, 0.75f, 1f),
                Streak = new Color(0.75f, 0.88f, 1f), Wash = new Color(0.03f, 0.05f, 0.09f)
            }
        };

        // ── Live parameters (the director writes, Update renders) ────────────────

        /// <summary>Streak flow speed (m/s) and emission rate (per second); 0 = none.</summary>
        public float StreakSpeed;
        public float StreakRate;
        /// <summary>Stretch of the streaks (seconds of travel drawn as length).</summary>
        public float StreakStretch = 0.03f;
        public Color StreakColor = Color.white;
        /// <summary>Tube visibility 0..1 and flow speed (uv per second).</summary>
        public float TunnelOpen;
        public float TunnelFlow = 1.2f;
        /// <summary>PRL cage: 0 = off; 1 = wide open; the director shrinks it onto the hull.</summary>
        public float CageOpen;
        public float CageRadius = 90f;
        /// <summary>Sub-light glints: size 0..1 of the star ahead (destination) and aft (origin).</summary>
        public float GlintAhead;
        public float GlintAft;
        public Color GlintAheadColor = new(1f, 0.92f, 0.78f);
        public Color GlintAftColor = new(1f, 0.92f, 0.78f);
        /// <summary>Room wash (added to the interior ambient) and a decaying flash on top of it.</summary>
        public Color Wash;
        public float HumVolume;
        public float HumPitch = 1f;

        float _flash;
        Color _flashColor = Color.white;
        float _scroll;
        float _cageScroll;
        float _emitCarry;
        float _flicker;

        public static VoyageFx Create(Transform ship)
        {
            var go = new GameObject("VoyageFx");
            go.transform.SetParent(ship, false);
            return go.AddComponent<VoyageFx>();
        }

        public void SetLook(VoyageMode mode)
        {
            Build();
            var look = LookOf(mode);
            _tunnelMat.SetColor(ColAId, look.Deep);
            _tunnelMat.SetColor(ColBId, look.Bright);
            _tunnelMat.SetFloat(TwistId, mode == VoyageMode.Hyperspace ? 0.45f : mode == VoyageMode.PrlBond ? 0.1f : mode == VoyageMode.Sublight ? 0f : 0.2f);
            // Sub-light: no vortex, only a thin sheath of ion lines sliding past (sparse, no cloud).
            _tunnelMat.SetFloat(DensityId, mode == VoyageMode.Sublight ? 0.32f : mode == VoyageMode.Jumpgate ? 0.45f : 0.3f);
            _tunnelMat.SetFloat(RibsId, mode == VoyageMode.Jumpgate ? 1f : 0f);
            _tunnelMat.SetFloat(HexId, mode == VoyageMode.PrlBond ? 0.55f : 0f);
            _tunnelMat.SetFloat("_HexWidth", 0.05f);
            _cageMat.SetColor(ColAId, look.Deep);
            _cageMat.SetColor(ColBId, look.Bright);
            StreakColor = look.Streak;
        }

        /// <summary>A white-hot bloom ahead plus the rooms lighting up (jump, drop-out, gate crossing).</summary>
        public void Flash(Color color, float strength = 1f, float ahead = 260f)
        {
            Build();
            _flash = Mathf.Max(_flash, strength);
            _flashColor = color;
            var at = transform.TransformPoint(new Vector3(0f, 0f, ahead));
            CombatFxKit.Emit(_bursts, at, Color.Lerp(color, Color.white, 0.55f), 520f * strength, 0.55f);
            CombatFxKit.Emit(_bursts, at, color, 900f * strength, 0.9f);
        }

        /// <summary>A ring of light thrown out around the hull (emerging from a fold or a bond).</summary>
        public void Shock(Color color, float radius = 26f, float speed = 110f)
        {
            Build();
            var up = transform.up;
            var fwd = transform.forward;
            var right = transform.right;
            for (var i = 0; i < 48; i++)
            {
                var a = i / 48f * Mathf.PI * 2f;
                var dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                var at = transform.position + dir * radius + fwd * 20f;
                CombatFxKit.Emit(_bursts, at, color, 16f, 1.3f, dir * speed);
            }
        }

        public void Cue(AudioClip clip, float volume)
        {
            Build();
            if (clip != null)
                _cue.PlayOneShot(clip, volume);
        }

        /// <summary>Back to rest: everything off, rooms unlit by space.</summary>
        public void Off()
        {
            StreakRate = 0f;
            TunnelOpen = 0f;
            CageOpen = 0f;
            GlintAhead = GlintAft = 0f;
            Wash = Color.clear;
            HumVolume = 0f;
        }

        public bool Idle =>
            StreakRate <= 0f && TunnelOpen <= 0.001f && CageOpen <= 0.001f && GlintAhead <= 0f && GlintAft <= 0f &&
            _flash <= 0.001f && HumVolume <= 0f && (_hum == null || _hum.volume <= 0.001f) &&
            (_streaks == null || _streaks.particleCount == 0) && (_bursts == null || _bursts.particleCount == 0);

        void OnDisable() => Shader.SetGlobalColor(TintId, Color.clear);

        void Update()
        {
            if (!_built)
                return;
            var dt = Time.deltaTime;
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 1.6f);

            RetimeStreaks();
            EmitStreaks(dt);
            _streakR.velocityScale = StreakStretch;

            // Tube: flow, a faint flicker of the drive field, the flash burning through it.
            _scroll += dt * TunnelFlow;
            _flicker = Mathf.Lerp(_flicker, Random.value, dt * 8f);
            var tubeOn = TunnelOpen > 0.001f;
            if (_tunnelR.enabled != tubeOn)
                _tunnelR.enabled = tubeOn;
            if (tubeOn)
            {
                _tunnelMat.SetFloat(ScrollId, _scroll);
                _tunnelMat.SetFloat(OpenId, TunnelOpen * (0.9f + _flicker * 0.1f));
                _tunnelMat.SetFloat(FlashId, _flash);
            }

            var cageOn = CageOpen > 0.001f;
            if (_cageR.enabled != cageOn)
                _cageR.enabled = cageOn;
            if (cageOn)
            {
                _cageScroll += dt * 0.6f;
                _cage.localScale = Vector3.one * Mathf.Max(8f, CageRadius);
                _cage.localRotation = Quaternion.Euler(0f, _cageScroll * 20f, 0f);
                _cageMat.SetFloat(ScrollId, _cageScroll);
                _cageMat.SetFloat(OpenId, CageOpen);
                _cageMat.SetFloat(FlashId, _flash);
            }

            Glint(_glintAhead, _glintMatAhead, GlintAhead, GlintAheadColor, 1f);
            Glint(_glintAft, _glintMatAft, GlintAft, GlintAftColor, -1f);

            var wash = Wash + _flashColor * (_flash * 0.9f);
            Shader.SetGlobalColor(TintId, wash);

            _hum.volume = Mathf.MoveTowards(_hum.volume, HumVolume, dt * 0.35f);
            _hum.pitch = Mathf.Lerp(_hum.pitch, HumPitch, dt * 1.5f);
            if (_hum.volume > 0.001f && !_hum.isPlaying)
                _hum.Play();
            else if (_hum.volume <= 0.001f && _hum.isPlaying)
                _hum.Stop();
        }

        ParticleSystem.Particle[] _live;
        float _liveSpeed;

        /// <summary>
        /// The whole flow shares one speed: when the drive changes it (spool, jump, drop-out), the streaks
        /// already out there follow, and leave when they pass astern (no slow crawlers filling the pool).
        /// </summary>
        void RetimeStreaks()
        {
            var speed = Mathf.Max(StreakSpeed, 20f);
            if (Mathf.Abs(speed - _liveSpeed) < _liveSpeed * 0.02f || _streaks.particleCount == 0)
            {
                if (_streaks.particleCount == 0)
                    _liveSpeed = speed;
                return;
            }

            _liveSpeed = speed;
            _live ??= new ParticleSystem.Particle[StreakMax];
            var n = _streaks.GetParticles(_live);
            for (var i = 0; i < n; i++)
            {
                var p = _live[i];
                p.velocity = new Vector3(0f, 0f, -speed);
                var left = (p.position.z + StreakAhead) / speed;
                if (left < p.remainingLifetime)
                    p.remainingLifetime = Mathf.Max(0.01f, left);
                _live[i] = p;
            }

            _streaks.SetParticles(_live, n);
        }

        void EmitStreaks(float dt)
        {
            if (StreakRate <= 0f || StreakSpeed <= 0f)
            {
                _emitCarry = 0f;
                return;
            }

            _emitCarry += StreakRate * dt;
            var n = Mathf.Min((int)_emitCarry, 64);
            _emitCarry -= n;
            var life = (StreakAhead * 2f) / StreakSpeed;
            for (var i = 0; i < n; i++)
            {
                var a = Random.value * Mathf.PI * 2f;
                // Uniform over the ring's area, so the light lines do not crowd the hull.
                var r = Mathf.Sqrt(Mathf.Lerp(StreakInner * StreakInner, StreakOuter * StreakOuter, Random.value));
                var z = StreakAhead - Random.value * StreakSpeed * dt * 2f;
                _streaks.Emit(new ParticleSystem.EmitParams
                {
                    position = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.8f, z),
                    velocity = new Vector3(0f, 0f, -StreakSpeed),
                    startColor = StreakColor * Random.Range(0.55f, 1f),
                    startSize = Random.Range(0.18f, 0.5f),
                    startLifetime = life,
                    applyShapeToPosition = false
                }, 1);
            }
        }

        void Glint(Transform tr, Material mat, float size, Color color, float side)
        {
            var on = size > 0.001f;
            if (tr.gameObject.activeSelf != on)
                tr.gameObject.SetActive(on);
            if (!on)
                return;
            tr.localPosition = new Vector3(side * 40f, 18f, side * 950f);
            tr.localScale = Vector3.one * Mathf.Lerp(6f, 90f, size);
            mat.SetColor(ColorId, color * Mathf.Lerp(0.6f, 1.4f, size));
        }

        // ── Build (once) ──────────────────────────────────────────────────────────

        void Build()
        {
            if (_built)
                return;
            _built = true;

            var shader = Shader.Find("SU/WarpTunnel");
            _tunnelMat = shader != null ? new Material(shader) { name = "SU_WarpTunnel" } : new Material(CombatFxKit.Glow()) { name = "SU_WarpTunnelFallback" };
            _tunnelMat.mainTexture = Noise;
            _cageMat = shader != null ? new Material(shader) { name = "SU_BondCage" } : new Material(CombatFxKit.Glow()) { name = "SU_BondCageFallback" };
            _cageMat.mainTexture = Noise;
            _cageMat.SetVector("_Tile", new Vector4(4f, 2f, 0f, 0f));
            _cageMat.SetFloat(HexId, 1f);
            _cageMat.SetVector("_HexCount", new Vector4(36f, 18f, 0f, 0f));
            _cageMat.SetFloat("_HexWidth", 0.035f);
            _cageMat.SetFloat(DensityId, 0.7f);
            _cageMat.SetVector("_EndFade", new Vector4(0.08f, 0.08f, 0f, 0f));

            _tunnel = Part("TransitTube", Tube, _tunnelMat, out _tunnelR);
            _tunnel.localScale = new Vector3(TunnelRadius, TunnelRadius, TunnelLength);
            _cage = Part("BondCage", Sphere, _cageMat, out _cageR);

            _streaks = CombatFxKit.Burst(transform, "StarStreaks", StreakMax, 3f, gravity: false, stretch: true);
            var main = _streaks.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var sol = _streaks.sizeOverLifetime;
            sol.enabled = false;
            var col = _streaks.colorOverLifetime;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            _streakR = _streaks.GetComponent<ParticleSystemRenderer>();
            _streakR.lengthScale = 1f;

            _bursts = CombatFxKit.Burst(transform, "VoyageBursts", 160, 1f, gravity: false, stretch: false);
            var bm = _bursts.main;
            bm.simulationSpace = ParticleSystemSimulationSpace.Local;

            _glintMatAhead = new Material(CombatFxKit.Glow()) { name = "SU_GlintAhead" };
            _glintMatAft = new Material(CombatFxKit.Glow()) { name = "SU_GlintAft" };
            _glintAhead = GlintQuad("GlintAhead", _glintMatAhead);
            _glintAft = GlintQuad("GlintAft", _glintMatAft);

            _hum = gameObject.AddComponent<AudioSource>();
            _hum.clip = VoyageAudio.Hum;
            _hum.loop = true;
            _hum.playOnAwake = false;
            _hum.spatialBlend = 0f;
            _hum.volume = 0f;
            _cue = gameObject.AddComponent<AudioSource>();
            _cue.playOnAwake = false;
            _cue.spatialBlend = 0f;
        }

        Transform Part(string name, Mesh mesh, Material mat, out MeshRenderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return go.transform;
        }

        Transform GlintQuad(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.AddComponent<BillboardFace>().Mode = BillboardFace.FaceMode.Camera;
            go.SetActive(false);
            return go.transform;
        }

        static Mesh _quad;

        static Mesh QuadMesh
        {
            get
            {
                if (_quad != null)
                    return _quad;
                _quad = new Mesh { name = "SU_GlintQuad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                _quad.RecalculateBounds();
                return _quad;
            }
        }

        /// <summary>Open tube, radius 1, z −0.5..0.5; uv.x around, uv.y aft → bow. Big bounds: always drawn around the eye.</summary>
        static Mesh Tube
        {
            get
            {
                if (_tube != null)
                    return _tube;
                const int around = 36;
                const int along = 28;
                var v = new Vector3[(around + 1) * (along + 1)];
                var uv = new Vector2[v.Length];
                var tris = new int[around * along * 6];
                for (var j = 0; j <= along; j++)
                for (var i = 0; i <= around; i++)
                {
                    var a = i / (float)around * Mathf.PI * 2f;
                    var k = j * (around + 1) + i;
                    v[k] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), j / (float)along - 0.5f);
                    uv[k] = new Vector2(i / (float)around, j / (float)along);
                }

                var t = 0;
                for (var j = 0; j < along; j++)
                for (var i = 0; i < around; i++)
                {
                    var a0 = j * (around + 1) + i;
                    var b0 = a0 + around + 1;
                    tris[t++] = a0; tris[t++] = a0 + 1; tris[t++] = b0;
                    tris[t++] = a0 + 1; tris[t++] = b0 + 1; tris[t++] = b0;
                }

                _tube = new Mesh { name = "SU_TransitTube", vertices = v, uv = uv, triangles = tris };
                _tube.RecalculateBounds();
                return _tube;
            }
        }

        /// <summary>UV sphere, radius 1 (uv.x around, uv.y pole to pole) for the Bond PRL cage.</summary>
        static Mesh Sphere
        {
            get
            {
                if (_sphere != null)
                    return _sphere;
                const int around = 40;
                const int rings = 20;
                var v = new Vector3[(around + 1) * (rings + 1)];
                var uv = new Vector2[v.Length];
                var tris = new int[around * rings * 6];
                for (var j = 0; j <= rings; j++)
                {
                    var phi = j / (float)rings * Mathf.PI;
                    for (var i = 0; i <= around; i++)
                    {
                        var a = i / (float)around * Mathf.PI * 2f;
                        var k = j * (around + 1) + i;
                        v[k] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(a), -Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(a));
                        uv[k] = new Vector2(i / (float)around, j / (float)rings);
                    }
                }

                var t = 0;
                for (var j = 0; j < rings; j++)
                for (var i = 0; i < around; i++)
                {
                    var a0 = j * (around + 1) + i;
                    var b0 = a0 + around + 1;
                    tris[t++] = a0; tris[t++] = b0; tris[t++] = a0 + 1;
                    tris[t++] = a0 + 1; tris[t++] = b0; tris[t++] = b0 + 1;
                }

                _sphere = new Mesh { name = "SU_BondCage", vertices = v, uv = uv, triangles = tris };
                _sphere.RecalculateBounds();
                return _sphere;
            }
        }

        /// <summary>
        /// Tileable streak noise: R = light lines (value noise stretched along the tube, sharpened),
        /// G = soft drifting clouds. Periodic lattice so the tube wraps without a seam.
        /// </summary>
        static Texture2D Noise
        {
            get
            {
                if (_noise != null)
                    return _noise;
                const int w = 128;
                const int h = 256;
                _noise = new Texture2D(w, h, TextureFormat.RGBA32, true)
                {
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "SU_WarpNoise", anisoLevel = 2
                };
                var px = new Color32[w * h];
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var u = x / (float)w;
                    var v = y / (float)h;
                    // Lines: fine across (32 cells), long along (4 cells).
                    var lines = Periodic(u * 32f, v * 4f, 32, 4, 11) * 0.65f + Periodic(u * 64f, v * 8f, 64, 8, 23) * 0.35f;
                    lines = Mathf.Pow(Mathf.Clamp01(lines), 2.2f) * 1.25f;
                    var cloud = Periodic(u * 6f, v * 6f, 6, 6, 5) * 0.6f + Periodic(u * 12f, v * 12f, 12, 12, 9) * 0.4f;
                    px[y * w + x] = new Color32((byte)(Mathf.Clamp01(lines) * 255f), (byte)(Mathf.Clamp01(cloud) * 255f), 0, 255);
                }

                _noise.SetPixels32(px);
                _noise.Apply(true, true);
                return _noise;
            }
        }

        /// <summary>Smooth value noise on a lattice that wraps every (px, py) cells.</summary>
        static float Periodic(float x, float y, int px, int py, int seed)
        {
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float H(int ix, int iy)
            {
                ix = ((ix % px) + px) % px;
                iy = ((iy % py) + py) % py;
                var n = ix * 374761393 + iy * 668265263 + seed * 1442695041;
                n = (n ^ (n >> 13)) * 1274126177;
                return ((n ^ (n >> 16)) & 0xffff) / 65535f;
            }

            var a = Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx);
            var b = Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }
    }

    /// <summary>Drive sounds, synthesised once (no audio pack): spool, jump, drop-out, cruise hum loop.</summary>
    public static class VoyageAudio
    {
        const int Rate = 22050;
        static AudioClip _spool;
        static AudioClip _jump;
        static AudioClip _dropOut;
        static AudioClip _hum;
        static AudioClip _bond;

        /// <summary>Drive winding up: a rising saw growl under a swelling hiss (3 s).</summary>
        public static AudioClip Spool => _spool != null ? _spool : _spool = Make("su_drive_spool", 3f, (t, u, s) =>
        {
            s.Phase += 2f * Mathf.PI * Mathf.Lerp(42f, 190f, u * u) / Rate;
            s.Lp += (s.Noise() - s.Lp) * Mathf.Lerp(0.02f, 0.25f, u);
            var saw = Mathf.Repeat(s.Phase / (2f * Mathf.PI), 1f) * 2f - 1f;
            var env = Mathf.Clamp01(u * 3f) * Mathf.Clamp01((1f - u) * 12f);
            return (saw * 0.22f + Mathf.Sin(s.Phase * 0.5f) * 0.25f + s.Lp * 0.6f * u) * env;
        });

        /// <summary>The jump: sub thump, a bright crack and a long falling tail (2 s).</summary>
        public static AudioClip Jump => _jump != null ? _jump : _jump = Make("su_drive_jump", 2f, (t, u, s) =>
        {
            s.Phase += 2f * Mathf.PI * Mathf.Lerp(95f, 28f, Mathf.Sqrt(u)) / Rate;
            s.Lp += (s.Noise() - s.Lp) * Mathf.Lerp(0.8f, 0.03f, Mathf.Sqrt(u));
            var thump = Mathf.Sin(s.Phase) * Mathf.Exp(-t * 3.2f);
            var crack = s.Lp * Mathf.Exp(-t * 4.5f);
            return (thump * 0.7f + crack * 0.8f) * Mathf.Clamp01(t * 400f);
        });

        /// <summary>Dropping out: a rising rush that lands on a thump (1.4 s).</summary>
        public static AudioClip DropOut => _dropOut != null ? _dropOut : _dropOut = Make("su_drive_dropout", 1.4f, (t, u, s) =>
        {
            s.Lp += (s.Noise() - s.Lp) * Mathf.Lerp(0.05f, 0.5f, u);
            var rush = s.Lp * u * u * Mathf.Clamp01((1f - u) * 14f);
            var hit = u > 0.72f ? Mathf.Exp(-(u - 0.72f) * 10f) : 0f;
            s.Phase += 2f * Mathf.PI * 60f / Rate;
            return rush * 0.7f + Mathf.Sin(s.Phase) * hit * 0.6f;
        });

        /// <summary>Bond PRL snap: a glassy chord collapsing to a click (1.2 s).</summary>
        public static AudioClip Bond => _bond != null ? _bond : _bond = Make("su_bond_snap", 1.2f, (t, u, s) =>
        {
            var f = Mathf.Lerp(1320f, 180f, Mathf.Sqrt(u));
            s.Phase += 2f * Mathf.PI * f / Rate;
            var env = Mathf.Clamp01(t * 200f) * Mathf.Exp(-t * 3.5f);
            return (Mathf.Sin(s.Phase) * 0.3f + Mathf.Sin(s.Phase * 1.5f) * 0.18f + Mathf.Sin(s.Phase * 2.01f) * 0.12f) * env;
        });

        /// <summary>Cruise hum (seamless 2 s loop): low drone with slow beating, filtered air.</summary>
        public static AudioClip Hum
        {
            get
            {
                if (_hum != null)
                    return _hum;
                const float seconds = 2f;
                var n = (int)(Rate * seconds);
                var data = new float[n];
                var rng = new System.Random(4242);
                var lp = 0f;
                for (var i = 0; i < n; i++)
                {
                    var t = i / (float)Rate;
                    // Whole cycles in 2 s (55, 82.5, 110 Hz; 0.5 Hz beat) keep the loop seamless.
                    var drone = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.14f +
                                Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.1f;
                    var beat = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                    lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.04f;
                    data[i] = (drone * beat + lp * 0.8f) * 0.5f;
                }

                // Crossfade the noise seam.
                const int fade = 2000;
                for (var i = 0; i < fade; i++)
                {
                    var k = i / (float)fade;
                    var a = data[i];
                    var b = data[n - fade + i];
                    data[n - fade + i] = Mathf.Lerp(b, a, k);
                }

                _hum = AudioClip.Create("su_drive_hum", n, 1, Rate, false);
                _hum.SetData(data, 0);
                return _hum;
            }
        }

        sealed class Synth
        {
            public float Phase;
            public float Lp;
            readonly System.Random _rng;
            public Synth(int seed) => _rng = new System.Random(seed);
            public float Noise() => (float)_rng.NextDouble() * 2f - 1f;
        }

        static AudioClip Make(string name, float seconds, System.Func<float, float, Synth, float> sample)
        {
            var n = Mathf.Max(64, (int)(Rate * seconds));
            var data = new float[n];
            var s = new Synth(name.GetHashCode());
            for (var i = 0; i < n; i++)
                data[i] = Mathf.Clamp(sample(i / (float)Rate, i / (float)n, s), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
