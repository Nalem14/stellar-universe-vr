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
            var stars = GalaxyCatalog.All;
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
            var sums = new Dictionary<int, (Vector2 Sum, int Count)>();
            foreach (var s in stars)
            {
                if (s.OwnerId <= 0)
                    continue;
                var cx = (s.MapX - _terrRect.x) / px;
                var cy = (s.MapY - _terrRect.y) / px;
                var x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rpx));
                var x1 = Mathf.Min(TerritoryRes - 1, Mathf.CeilToInt(cx + rpx));
                var y0 = Mathf.Max(0, Mathf.FloorToInt(cy - rpx));
                var y1 = Mathf.Min(TerritoryRes - 1, Mathf.CeilToInt(cy + rpx));
                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var dx = x + 0.5f - cx;
                    var dy = y + 0.5f - cy;
                    var d = dx * dx + dy * dy;
                    var k = y * TerritoryRes + x;
                    if (d > rpx * rpx)
                        continue;
                    // Nearest owner, and the nearest *other* owner (for a smooth border between the two).
                    if (owner[k] == s.OwnerId)
                    {
                        if (d < dist[k])
                            dist[k] = d;
                    }
                    else if (d < dist[k])
                    {
                        if (owner[k] > 0)
                            dist2[k] = Mathf.Min(dist2[k], dist[k]);
                        dist[k] = d;
                        owner[k] = s.OwnerId;
                    }
                    else if (d < dist2[k])
                    {
                        dist2[k] = d;
                    }
                }

                sums.TryGetValue(s.OwnerId, out var acc);
                sums[s.OwnerId] = (acc.Sum + new Vector2(s.MapX, s.MapY), acc.Count + 1);
            }

            // Fill + border (4-neighbour owner change). Web y grows down: texture v flips (map −y = table +z).
            _terrPixels ??= new Color32[n];
            var me = FocusContext.OwnedUserId();
            for (var y = 0; y < TerritoryRes; y++)
            for (var x = 0; x < TerritoryRes; x++)
            {
                var k = y * TerritoryRes + x;
                var o = owner[k];
                var dst = (TerritoryRes - 1 - y) * TerritoryRes + x;
                if (o <= 0)
                {
                    _terrPixels[dst] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // Border strength from distances, not from pixel neighbours: a soft, even line where the
                // influence ends (against free space) or where two empires meet — no stair steps.
                var d1 = Mathf.Sqrt(dist[k]);
                var outer = rpx - d1;
                var gap = outer;
                if (dist2[k] < float.MaxValue)
                    gap = Mathf.Min(gap, (Mathf.Sqrt(dist2[k]) - d1) * 0.5f);
                // The line sits just inside the edge; the very edge fades to nothing (no texel stair).
                var line = 1f - Mathf.SmoothStep(1.2f, 3.6f, gap);
                var fade = Mathf.SmoothStep(0f, 1.4f, outer);
                var c = OwnerColor(o);
                // A light veil inside, a bright thin border: the stars stay the subject.
                var fill = o == me ? 0.2f : 0.13f;
                var bright = new Color(Mathf.Min(1f, c.r * 1.15f), Mathf.Min(1f, c.g * 1.15f), Mathf.Min(1f, c.b * 1.15f), 0.95f);
                var px32 = Color.Lerp(new Color(c.r, c.g, c.b, fill), bright, line);
                px32.a *= fade;
                _terrPixels[dst] = px32;
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

            BuildEmblems(sums);
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

        void BuildEmblems(Dictionary<int, (Vector2 Sum, int Count)> sums)
        {
            _emblems.Clear();
            foreach (var kv in sums)
            {
                var uid = kv.Key;
                var centre = kv.Value.Sum / kv.Value.Count;
                var root = new GameObject("Emblem_" + uid).transform;
                root.SetParent(_root, false);
                root.gameObject.AddComponent<BillboardFace>();
                _tokenRoots.Add(root.gameObject);

                DiplomacyIndex.TryIdentity(uid, out var name, out var flagJson);
                var flag = GameObject.CreatePrimitive(PrimitiveType.Quad);
                flag.name = "Flag";
                DropCollider(flag);
                flag.transform.SetParent(root, false);
                flag.transform.localPosition = new Vector3(0f, 0.022f, 0f);
                flag.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                flag.transform.localScale = new Vector3(0.06f, 0.04f, 1f);
                var mr = flag.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _art.Lit(FlagTexture(uid, flagJson), Color.white, 0.6f);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var label = UiKit.Label(root, "Name", string.IsNullOrEmpty(name) ? "?" : name, new Vector3(0f, -0.008f, 0f),
                    0.3f, 0.014f, OwnerColor(uid));
                label.fontStyle = FontStyles.Bold;
                label.outlineWidth = 0.2f;
                label.outlineColor = new Color32(2, 10, 16, 230);

                // How big the territory reads: a few influence radii per √(systems).
                _emblems.Add((root, centre, _terrInfluence * Mathf.Sqrt(kv.Value.Count)));
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
                var p = MapToLocal(e.Map.x, e.Map.y, lift + 0.035f);
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
