using System.Collections.Generic;
using Core.App;
using Core.UI;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Galaxy territories, Stellaris-style: every owned system radiates an influence disc; the closest owner
    /// takes the space, filled translucent in the empire's flag colour with a bright border where owners
    /// meet. Drawn once per galaxy open into one small texture on one quad (SU/HoloTerritory, clipped to the
    /// table), so pan / zoom only move the quad. Each empire's flag + name floats over its territory.
    /// Colours: flag background (GetEmpires.flag, as web galaxy.js applyEmpireFlagColors), else a stable hue.
    /// </summary>
    public partial class HoloZoneMap
    {
        const int TerritoryRes = 512;
        const int MaxEmblems = 10;

        Texture2D _terrTex;
        Color32[] _terrPixels;
        GameObject _terrQuad;
        Material _terrMat;
        Material _curtainMat;
        readonly GameObject[] _curtains = new GameObject[3];
        GameObject _core;
        Vector2 _galaxyCentre;
        Rect _terrRect;
        float _terrInfluence;
        GameObject _here;
        readonly List<(Transform Root, Vector2 Map, float Radius)> _emblems = new();
        static readonly Dictionary<int, Texture2D> FlagTextures = new();
        static readonly Dictionary<int, Color> OwnerColors = new();

        public static Color OwnerColor(int userId)
        {
            if (OwnerColors.TryGetValue(userId, out var c))
                return c;
            if (DiplomacyIndex.TryIdentity(userId, out _, out var flag) && !string.IsNullOrEmpty(flag))
                c = UI.FlagPainter.Hex(UI.FlagSpec.FromJson(flag).Bg, Color.gray);
            else
                c = Color.HSVToRGB((userId * 0.618034f) % 1f, 0.65f, 0.95f);
            // Too dark a flag would vanish on the table: keep a readable floor.
            Color.RGBToHSV(c, out var h, out var sat, out var v);
            c = Color.HSVToRGB(h, Mathf.Max(0.35f, sat), Mathf.Max(0.6f, v));
            OwnerColors[userId] = c;
            return c;
        }

        void BuildTerritories()
        {
            OwnerColors.Clear();
            var stars = GalaxyStars;
            if (stars.Count == 0)
                return;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var s in stars)
            {
                minX = Mathf.Min(minX, s.MapX);
                maxX = Mathf.Max(maxX, s.MapX);
                minY = Mathf.Min(minY, s.MapY);
                maxY = Mathf.Max(maxY, s.MapY);
            }

            _terrInfluence = InfluenceRadius(stars);
            var pad = _terrInfluence * 1.2f;
            var size = Mathf.Max(maxX - minX, maxY - minY) + pad * 2f;
            _terrRect = new Rect((minX + maxX) * 0.5f - size * 0.5f, (minY + maxY) * 0.5f - size * 0.5f, size, size);

            var n = TerritoryRes * TerritoryRes;
            var owner = new int[n];
            var dist = new float[n];
            var dist2 = new float[n];
            for (var i = 0; i < n; i++)
                dist[i] = dist2[i] = float.MaxValue;
            var px = size / TerritoryRes;
            var rpx = _terrInfluence / px;
            var gaps = new float[n];
            // Just past the edges: distance to the nearest claim and whose it is (smooth outer border, no stairs).
            var near = new float[n];
            var nearOwner = new int[n];
            for (var i = 0; i < n; i++)
                near[i] = float.MaxValue;

            // Each owned system claims a disc; neighbouring systems of one empire are joined by a bridge, so an
            // empire reads as one region (Stellaris) rather than beads on a string. Nearest claim wins; the second
            // nearest (another owner) sets where two empires meet.
            var byOwner = new Dictionary<int, List<Vector2>>();
            foreach (var s in stars)
            {
                if (s.OwnerId <= 0)
                    continue;
                var c = new Vector2((s.MapX - _terrRect.x) / px, (s.MapY - _terrRect.y) / px);
                if (!byOwner.TryGetValue(s.OwnerId, out var list))
                    byOwner[s.OwnerId] = list = new List<Vector2>();
                list.Add(c);
                Claim(s.OwnerId, c, c, rpx, rpx, owner, dist, dist2, near, nearOwner);
            }

            var link = rpx * 2.6f;
            foreach (var kv in byOwner)
            {
                var pts = kv.Value;
                for (var a = 0; a < pts.Count; a++)
                for (var b = a + 1; b < pts.Count; b++)
                    if ((pts[a] - pts[b]).sqrMagnitude <= link * link)
                        Claim(kv.Key, pts[a], pts[b], rpx * 0.72f, rpx, owner, dist, dist2, near, nearOwner);
            }

            // Colour + distance to the border (alpha: 0.5 = border, 1 = rpx inside) for SU/HoloTerritory's SDF mode.
            // Web y grows down: texture v flips (map −y = table +z).
            _terrPixels ??= new Color32[n];
            var outside = (byte)Mathf.RoundToInt(255f * (0.5f - 0.5f * (1.5f / Mathf.Max(1f, rpx))));
            for (var y = 0; y < TerritoryRes; y++)
            for (var x = 0; x < TerritoryRes; x++)
            {
                var k = y * TerritoryRes + x;
                var o = owner[k];
                var dst = (TerritoryRes - 1 - y) * TerritoryRes + x;
                if (o <= 0)
                {
                    // Free space just past an edge: the true (negative) distance, in the neighbour's colour.
                    if (nearOwner[k] <= 0)
                    {
                        _terrPixels[dst] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    var nc = (Color32)OwnerColor(nearOwner[k]);
                    var outGap = rpx - Mathf.Sqrt(near[k]);
                    nc.a = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.5f + 0.5f * outGap / rpx));
                    _terrPixels[dst] = nc;
                    continue;
                }

                var d1 = Mathf.Sqrt(dist[k]);
                var gap = rpx - d1;
                if (dist2[k] < float.MaxValue)
                    gap = Mathf.Min(gap, (Mathf.Sqrt(dist2[k]) - d1) * 0.5f);
                gaps[k] = gap;
                var c = (Color32)OwnerColor(o);
                c.a = (byte)Mathf.RoundToInt(255f * (0.5f + 0.5f * Mathf.Clamp01(gap / rpx)));
                _terrPixels[dst] = c;
            }

            if (_terrTex == null)
            {
                _terrTex = new Texture2D(TerritoryRes, TerritoryRes, TextureFormat.RGBA32, false)
                {
                    name = "GalaxyTerritories",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            _terrTex.SetPixels32(_terrPixels);
            _terrTex.Apply(false);

            _terrQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _terrQuad.name = "Territories";
            DropCollider(_terrQuad);
            _terrQuad.transform.SetParent(_root, false);
            _terrQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _terrMat ??= MakeTerritoryMat();
            _terrMat.mainTexture = _terrTex;
            _terrMat.SetFloat("_Sdf", 1f);
            _terrMat.SetFloat("_GapScale", rpx);
            // The inner glow reaches about a third of a system's claim.
            _terrMat.SetFloat("_GlowTexels", Mathf.Max(3f, rpx * 0.35f));
            _terrMat.SetFloat("_FillAlpha", 0.1f);
            _terrMat.SetFloat("_GlowAlpha", 0.5f);
            _terrMat.SetFloat("_LinePx", 2.2f);
            var mr = _terrQuad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _terrMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tokenRoots.Add(_terrQuad);

            // (No stacked border curtains any more: they read as stair-stepped walls hiding the stars.)

            // Galactic core: a soft glow where the stars gather.
            var sum = Vector2.zero;
            foreach (var st in stars)
                sum += new Vector2(st.MapX, st.MapY);
            _galaxyCentre = sum / stars.Count;
            _core = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _core.name = "GalacticCore";
            DropCollider(_core);
            _core.transform.SetParent(_root, false);
            _core.AddComponent<BillboardFace>();
            var cr = _core.GetComponent<MeshRenderer>();
            cr.sharedMaterial = CoreGlowMat();
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tokenRoots.Add(_core);

            BuildEmblems(EmblemAnchors(owner, gaps, px));
            BuildNebula(stars);
        }

        const int NebulaRes = 128;
        Texture2D _nebTex;
        GameObject _nebQuad;
        Material _nebMat;

        /// <summary>
        /// The galaxy's own shape under the stars: every star splats a soft blob into a small density map,
        /// coloured deep blue-violet in the arms and warm toward the dense core — a nebula that is really where
        /// the stars are. One 128² texture on one quad, drawn once per galaxy open (clipped to the table).
        /// </summary>
        void BuildNebula(IReadOnlyList<GalaxyCatalog.Star> stars)
        {
            var n = NebulaRes * NebulaRes;
            var dens = new float[n];
            var px = _terrRect.width / NebulaRes;
            var r = Mathf.Max(1.5f, _terrInfluence * 1.6f / px);
            var r2 = r * r;
            foreach (var st in stars)
            {
                var cx = (st.MapX - _terrRect.x) / px;
                var cy = (st.MapY - _terrRect.y) / px;
                var x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r));
                var x1 = Mathf.Min(NebulaRes - 1, Mathf.CeilToInt(cx + r));
                var y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r));
                var y1 = Mathf.Min(NebulaRes - 1, Mathf.CeilToInt(cy + r));
                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var dx = x + 0.5f - cx;
                    var dy = y + 0.5f - cy;
                    var d = (dx * dx + dy * dy) / r2;
                    if (d < 1f)
                        dens[y * NebulaRes + x] += (1f - d) * (1f - d);
                }
            }

            var max = 0f;
            for (var i = 0; i < n; i++)
                max = Mathf.Max(max, dens[i]);
            if (max <= 0f)
                return;
            var pixels = new Color32[n];
            var arm = new Color(0.22f, 0.3f, 0.9f);
            var core = new Color(1f, 0.74f, 0.5f);
            for (var y = 0; y < NebulaRes; y++)
            for (var x = 0; x < NebulaRes; x++)
            {
                var v = Mathf.Pow(dens[y * NebulaRes + x] / max, 0.6f);
                var c = Color.Lerp(arm, core, Mathf.SmoothStep(0.86f, 1f, v) * 0.8f);
                c.a = Mathf.SmoothStep(0.02f, 0.9f, v) * 0.3f;
                pixels[(NebulaRes - 1 - y) * NebulaRes + x] = c;
            }

            if (_nebTex == null)
                _nebTex = new Texture2D(NebulaRes, NebulaRes, TextureFormat.RGBA32, false)
                {
                    name = "GalaxyNebula",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            _nebTex.SetPixels32(pixels);
            _nebTex.Apply(false);
            _nebQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _nebQuad.name = "GalaxyNebula";
            DropCollider(_nebQuad);
            _nebQuad.transform.SetParent(_root, false);
            _nebQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _nebMat ??= MakeTerritoryMat();
            _nebMat.mainTexture = _nebTex;
            var mr = _nebQuad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _nebMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tokenRoots.Add(_nebQuad);
        }

        /// <summary>
        /// One claim on the territory grid: a disc (<paramref name="a"/> = <paramref name="b"/>) or a capsule
        /// between two systems, <paramref name="radius"/> texels wide. Distances are scaled so every claim's edge
        /// sits at <paramref name="rpx"/> (the border pass reads them alike).
        /// </summary>
        static void Claim(int o, Vector2 a, Vector2 b, float radius, float rpx, int[] owner, float[] dist, float[] dist2,
            float[] near, int[] nearOwner)
        {
            // A few texels past the claim too: the outer border's distance field continues outside.
            const float Margin = 4f;
            var scale = rpx / Mathf.Max(0.01f, radius);
            var reach = radius + Margin / scale;
            var x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - reach));
            var x1 = Mathf.Min(TerritoryRes - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + reach));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - reach));
            var y1 = Mathf.Min(TerritoryRes - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + reach));
            var outer = (rpx + Margin) * (rpx + Margin);
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                var q = (p - (a + ab * t)) * scale;
                var d = q.sqrMagnitude;
                if (d > outer)
                    continue;
                var k = y * TerritoryRes + x;
                if (d < near[k])
                {
                    near[k] = d;
                    nearOwner[k] = o;
                }

                if (d > rpx * rpx)
                    continue;
                // Nearest owner, and the nearest *other* owner (for a smooth border between the two).
                if (owner[k] == o)
                {
                    if (d < dist[k])
                        dist[k] = d;
                }
                else if (d < dist[k])
                {
                    if (owner[k] > 0)
                        dist2[k] = Mathf.Min(dist2[k], dist[k]);
                    dist[k] = d;
                    owner[k] = o;
                }
                else if (d < dist2[k])
                {
                    dist2[k] = d;
                }
            }
        }

        Material _coreMat;

        /// <summary>Additive soft glow (the lab's particle material): no square edges.</summary>
        Material CoreGlowMat()
        {
            if (_coreMat != null)
                return _coreMat;
            var shader = Shader.Find("SU/ParticleGlow");
            _coreMat = shader != null ? new Material(shader) : _art.Holo(Texture2D.whiteTexture, new Color(1f, 0.85f, 0.6f, 0.2f));
            if (_coreMat.HasProperty("_MainTex") && _art.ProjectorGlow != null)
                _coreMat.mainTexture = _art.ProjectorGlow;
            if (_coreMat.HasProperty("_Color"))
                _coreMat.SetColor("_Color", new Color(1f, 0.82f, 0.55f, 1f));
            if (_coreMat.HasProperty("_EmissionMul"))
                _coreMat.SetFloat("_EmissionMul", 0.7f);
            return _coreMat;
        }

        Material MakeTerritoryMat()
        {
            var shader = Shader.Find("SU/HoloTerritory");
            return shader != null ? new Material(shader) : _art.Holo(Texture2D.whiteTexture, new Color(1f, 1f, 1f, 0.3f));
        }

        /// <summary>Influence disc: most of the mean nearest-neighbour gap, so neighbours of one empire merge.</summary>
        static float InfluenceRadius(IReadOnlyList<GalaxyCatalog.Star> stars)
        {
            if (stars.Count < 2)
                return 50f;
            var step = Mathf.Max(1, stars.Count / 250);
            var total = 0f;
            var counted = 0;
            for (var i = 0; i < stars.Count; i += step)
            {
                var best = float.MaxValue;
                for (var j = 0; j < stars.Count; j++)
                {
                    if (i == j)
                        continue;
                    var dx = stars[i].MapX - stars[j].MapX;
                    var dy = stars[i].MapY - stars[j].MapY;
                    best = Mathf.Min(best, dx * dx + dy * dy);
                }

                if (best < float.MaxValue)
                {
                    total += Mathf.Sqrt(best);
                    counted++;
                }
            }

            return counted == 0 ? 50f : Mathf.Max(10f, total / counted * 2.4f);
        }

        /// <summary>
        /// Where each empire's name goes: the heart of its largest territory — the point of that region farthest
        /// from its border (the fill pass already knows each texel's distance to it). The mean of an empire's
        /// systems fell between its far-flung colonies, in space it does not hold.
        /// </summary>
        static Dictionary<int, (Vector2 Map, float Radius)> EmblemAnchors(int[] owner, float[] gaps, float px)
        {
            var anchors = new Dictionary<int, (Vector2 Map, float Radius)>();
            var best = new Dictionary<int, int>();
            var label = new int[owner.Length];
            var queue = new Queue<int>();
            var comp = 0;
            for (var start = 0; start < owner.Length; start++)
            {
                var o = owner[start];
                if (o <= 0 || label[start] != 0)
                    continue;
                // One connected region of this owner (4-neighbour flood).
                comp++;
                label[start] = comp;
                queue.Enqueue(start);
                var area = 0;
                var core = start;
                while (queue.Count > 0)
                {
                    var k = queue.Dequeue();
                    area++;
                    if (gaps[k] > gaps[core])
                        core = k;
                    var x = k % TerritoryRes;
                    var y = k / TerritoryRes;
                    if (x > 0) Visit(k - 1);
                    if (x < TerritoryRes - 1) Visit(k + 1);
                    if (y > 0) Visit(k - TerritoryRes);
                    if (y < TerritoryRes - 1) Visit(k + TerritoryRes);
                }

                if (!best.TryGetValue(o, out var bestArea) || area > bestArea)
                {
                    best[o] = area;
                    var cx = core % TerritoryRes;
                    var cy = core / TerritoryRes;
                    anchors[o] = (new Vector2((cx + 0.5f) * px, (cy + 0.5f) * px), Mathf.Sqrt(area / Mathf.PI) * px);
                }

                continue;

                void Visit(int j)
                {
                    if (label[j] != 0 || owner[j] != o)
                        return;
                    label[j] = comp;
                    queue.Enqueue(j);
                }
            }

            return anchors;
        }

        void BuildEmblems(Dictionary<int, (Vector2 Map, float Radius)> anchors)
        {
            _emblems.Clear();
            foreach (var kv in anchors)
            {
                var uid = kv.Key;
                var centre = new Vector2(_terrRect.x, _terrRect.y) + kv.Value.Map;
                var root = new GameObject("Emblem_" + uid).transform;
                root.SetParent(_root, false);
                root.gameObject.AddComponent<BillboardFace>();
                _tokenRoots.Add(root.gameObject);

                DiplomacyIndex.TryIdentity(uid, out var name, out var flagJson);
                var flag = GameObject.CreatePrimitive(PrimitiveType.Quad);
                flag.name = "Flag";
                DropCollider(flag);
                flag.transform.SetParent(root, false);
                flag.transform.localPosition = new Vector3(0f, 0.026f, 0f);
                flag.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                flag.transform.localScale = new Vector3(0.06f, 0.04f, 1f);
                var mr = flag.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _art.Lit(FlagTexture(uid, flagJson), Color.white, 0.6f);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var label = UiKit.Label(root, "Name", string.IsNullOrEmpty(name) ? "?" : name, new Vector3(0f, 0f, 0f),
                    0.34f, 0.018f, OwnerColor(uid));
                label.fontStyle = FontStyles.Bold;
                label.outlineWidth = 0.2f;
                label.outlineColor = new Color32(2, 10, 16, 230);

                // How big the region reads: the radius of a disc of its area.
                _emblems.Add((root, centre, kv.Value.Radius));
            }
        }

        static Texture2D FlagTexture(int uid, string flagJson)
        {
            if (FlagTextures.TryGetValue(uid, out var tex) && tex != null)
                return tex;
            tex = UI.FlagPainter.Paint(UI.FlagSpec.FromJson(flagJson));
            FlagTextures[uid] = tex;
            return tex;
        }

        /// <summary>Pan / zoom: move the territory quad and emblems; clip them to the table.</summary>
        void UpdateTerritoryView(float lift)
        {
            if (_terrQuad != null)
            {
                var c = _terrRect.center;
                _terrQuad.transform.localPosition = MapToLocal(c.x, c.y, lift - 0.002f);
                _terrQuad.transform.localScale = new Vector3(_terrRect.width * _gScale, _terrRect.height * _gScale, 1f);
                var clipC = _root.TransformPoint(new Vector3(0f, lift, 0f));
                var clipR = WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor * _root.lossyScale.x;
                if (_nebQuad != null)
                {
                    _nebQuad.transform.localPosition = MapToLocal(c.x, c.y, lift - 0.006f);
                    _nebQuad.transform.localScale = _terrQuad.transform.localScale;
                }

                foreach (var m in new[] { _terrMat, _curtainMat, _nebMat })
                {
                    if (m == null)
                        continue;
                    m.SetVector("_ClipCenter", clipC);
                    m.SetFloat("_ClipRadius", clipR);
                }

                for (var i = 0; i < _curtains.Length; i++)
                {
                    if (_curtains[i] == null)
                        continue;
                    _curtains[i].transform.localPosition = MapToLocal(c.x, c.y, lift + 0.006f + i * 0.007f);
                    _curtains[i].transform.localScale = _terrQuad.transform.localScale;
                }
            }

            if (_core != null)
            {
                var cp = MapToLocal(_galaxyCentre.x, _galaxyCentre.y, lift + 0.03f);
                var inside = new Vector2(cp.x, cp.z).magnitude < WorldScale.HoloDiscRadius * 0.8f;
                _core.SetActive(inside);
                _core.transform.localPosition = cp;
                _core.transform.localScale = Vector3.one * Mathf.Clamp(_terrRect.width * _gScale * 0.18f, 0.08f, 0.4f);
            }

            // Only the empires big enough to read at this zoom, nearest the view centre first.
            var limit = WorldScale.HoloDiscRadius * 0.85f;
            var shown = 0;
            _emblems.Sort((a, b) => (a.Map - _gCentre).sqrMagnitude.CompareTo((b.Map - _gCentre).sqrMagnitude));
            foreach (var e in _emblems)
            {
                if (e.Root == null)
                    continue;
                // Low over the plate: a name floating high drifts off its territory as soon as one looks at the
                // table from the side.
                var p = MapToLocal(e.Map.x, e.Map.y, lift + 0.012f);
                var visible = shown < MaxEmblems && new Vector2(p.x, p.z).magnitude < limit && e.Radius * _gScale > 0.035f;
                if (e.Root.gameObject.activeSelf != visible)
                    e.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                shown++;
                e.Root.localPosition = p;
            }

            // "You are here": a beam on the inhabited system.
            var focus = _focus ?? FocusContext.Current;
            if (focus != null && GalaxyCatalog.TryGet(focus.SystemId, out var here))
            {
                if (_here == null)
                {
                    _here = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    _here.name = "HereBeam";
                    DropCollider(_here);
                    _here.transform.SetParent(_root, false);
                    _here.transform.localScale = new Vector3(0.005f, 0.06f, 0.005f);
                    _here.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(Texture2D.whiteTexture,
                        new Color(1f, 0.78f, 0.3f, 0.7f));
                    _tokenRoots.Add(_here);
                }

                var hp = MapToLocal(here.MapX, here.MapY, lift + 0.06f);
                _here.transform.localPosition = hp;
                _here.SetActive(new Vector2(hp.x, hp.z).magnitude < WorldScale.HoloDiscRadius * GalaxyViewRadiusFactor);
            }
        }
    }
}
