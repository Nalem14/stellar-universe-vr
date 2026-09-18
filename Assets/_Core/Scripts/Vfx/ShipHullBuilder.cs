using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Procedural fleet hull from the 9×9 module grid. One smoothed envelope
    /// mesh for connected cells; modules are distinct silhouettes on deck.
    /// Scale: docs/SCALE.md.
    /// </summary>
    public static class ShipHullBuilder
    {
        static readonly Color Cyan = new(0.25f, 0.92f, 1f, 1f);
        static readonly Color Amber = new(1f, 0.62f, 0.22f, 1f);
        static Palette _ownPal;
        static Palette _foePal;

        enum Kind
        {
            Core, Armor, Cargo, Colony, Troop, Weapon, Engine, Shield, Sensor, Science, Stealth, Repair, Special, Utility
        }

        public static string Signature(IReadOnlyList<FocusShipModule> modules)
        {
            if (modules == null || modules.Count == 0)
                return "empty";
            var sb = new System.Text.StringBuilder(modules.Count * 12);
            for (var i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                sb.Append(m.Id).Append(':').Append(m.Type).Append(':').Append(m.GridX).Append(',').Append(m.GridY)
                    .Append(';');
            }

            return sb.ToString();
        }

        public static List<FocusShipModule> ResolveLayout(IReadOnlyList<FocusShipModule> modules, int seed)
        {
            var list = new List<FocusShipModule>();
            if (modules != null)
            {
                for (var i = 0; i < modules.Count; i++)
                {
                    var m = modules[i];
                    list.Add(new FocusShipModule
                    {
                        Id = m.Id,
                        Type = m.Type,
                        GridX = m.GridX,
                        GridY = m.GridY
                    });
                }
            }

            if (list.Count == 0)
                return Synthetic(seed);
            if (!AnyOnGrid(list))
                AutoPlace(list);
            else
                FillMissing(list);
            return list;
        }

        public static void Build(Transform root, IReadOnlyList<FocusShipModule> modules, bool owned, int seed)
        {
            if (root == null)
                return;
            ClearChildren(root);
            var laid = ResolveLayout(modules, seed);
            var zSign = NoseSign(laid);
            var occupied = new bool[WorldScale.ShipGrid, WorldScale.ShipGrid];
            var engine = new bool[WorldScale.ShipGrid, WorldScale.ShipGrid];
            for (var i = 0; i < laid.Count; i++)
            {
                if (!laid[i].OnGrid)
                    continue;
                occupied[laid[i].GridX, laid[i].GridY] = true;
                if (Classify(laid[i].Type) == Kind.Engine)
                    engine[laid[i].GridX, laid[i].GridY] = true;
            }

            var kit = new Kit(root, zSign, CachePalette(owned));
            BuildChassis(kit, occupied, engine);
            for (var i = 0; i < laid.Count; i++)
                BuildModule(kit, laid[i]);
            AddNavLights(kit, occupied);
        }

        static Palette CachePalette(bool owned)
        {
            if (owned)
                return _ownPal ??= new Palette(true, Cyan);
            return _foePal ??= new Palette(false, new Color(1f, 0.32f, 0.28f, 1f));
        }

        static void BuildChassis(Kit k, bool[,] occupied, bool[,] engine)
        {
            var hull = new bool[WorldScale.ShipGrid, WorldScale.ShipGrid];
            var any = false;
            for (var x = 0; x < WorldScale.ShipGrid; x++)
            for (var y = 0; y < WorldScale.ShipGrid; y++)
            {
                if (!occupied[x, y] || engine[x, y])
                    continue;
                hull[x, y] = true;
                any = true;
            }

            if (!any)
            {
                for (var x = 0; x < WorldScale.ShipGrid; x++)
                for (var y = 0; y < WorldScale.ShipGrid; y++)
                    hull[x, y] = occupied[x, y];
            }

            CloseRooms(hull);
            StitchLinks(hull);
            var mesh = BuildHullMesh(hull, k.Z);
            if (mesh == null)
                return;
            var go = new GameObject("Hull");
            go.transform.SetParent(k.Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = k.Pal.Hull;
        }

        static void StitchLinks(bool[,] occ)
        {
            var g = WorldScale.ShipGrid;
            for (var y = 0; y < g; y++)
            for (var x = 0; x < g - 2; x++)
            {
                if (occ[x, y] && occ[x + 2, y] && !occ[x + 1, y])
                    occ[x + 1, y] = true;
            }

            for (var x = 0; x < g; x++)
            for (var y = 0; y < g - 2; y++)
            {
                if (occ[x, y] && occ[x, y + 2] && !occ[x, y + 1])
                    occ[x, y + 1] = true;
            }

            for (var x = 0; x < g - 1; x++)
            for (var y = 0; y < g - 1; y++)
            {
                var a = occ[x, y];
                var b = occ[x + 1, y];
                var c = occ[x, y + 1];
                var d = occ[x + 1, y + 1];
                if (a && d && !b && !c)
                    occ[x + 1, y] = occ[x, y + 1] = true;
                else if (b && c && !a && !d)
                    occ[x, y] = occ[x + 1, y + 1] = true;
            }
        }

        static void CloseRooms(bool[,] occ)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                for (var x = 0; x < WorldScale.ShipGrid; x++)
                for (var y = 0; y < WorldScale.ShipGrid; y++)
                {
                    if (occ[x, y])
                        continue;
                    var n = 0;
                    if (Has(occ, x + 1, y)) n++;
                    if (Has(occ, x - 1, y)) n++;
                    if (Has(occ, x, y + 1)) n++;
                    if (Has(occ, x, y - 1)) n++;
                    if (n >= 4)
                        occ[x, y] = true;
                }
            }
        }

        static bool Has(bool[,] occ, int x, int y)
        {
            return x >= 0 && x < WorldScale.ShipGrid && y >= 0 && y < WorldScale.ShipGrid && occ[x, y];
        }

        static Mesh BuildHullMesh(bool[,] occ, float zSign)
        {
            var loops = TraceLoops(occ, zSign);
            if (loops.Count == 0)
                return null;

            var verts = new List<Vector3>(256);
            var tris = new List<int>(512);
            float[] ys = { 0.08f, 0.7f, 1.22f, 1.62f };
            float[] sc = { 0.9f, 1.18f, 1.05f, 0.72f };
            for (var l = 0; l < loops.Count; l++)
            {
                var ring = Chaikin(loops[l], 2);
                Outset(ring, WorldScale.ShipCell * 0.08f);
                if (ring.Count < 3)
                    continue;
                var c = Vector3.zero;
                for (var i = 0; i < ring.Count; i++)
                    c += ring[i];
                c /= ring.Count;
                c.y = 0f;
                var rings = new List<Vector3>[ys.Length];
                for (var r = 0; r < ys.Length; r++)
                {
                    rings[r] = new List<Vector3>(ring.Count);
                    for (var i = 0; i < ring.Count; i++)
                    {
                        var d = ring[i] - c;
                        d.y = 0f;
                        rings[r].Add(c + d * sc[r] + Vector3.up * ys[r]);
                    }
                }

                var baseIndex = verts.Count;
                var n = ring.Count;
                for (var r = 0; r < rings.Length; r++)
                for (var i = 0; i < n; i++)
                    verts.Add(rings[r][i]);

                for (var r = 0; r < rings.Length - 1; r++)
                {
                    for (var i = 0; i < n; i++)
                    {
                        var i0 = baseIndex + r * n + i;
                        var i1 = baseIndex + r * n + (i + 1) % n;
                        var j0 = baseIndex + (r + 1) * n + i;
                        var j1 = baseIndex + (r + 1) * n + (i + 1) % n;
                        tris.Add(i0);
                        tris.Add(j0);
                        tris.Add(j1);
                        tris.Add(i0);
                        tris.Add(j1);
                        tris.Add(i1);
                    }
                }

                var top = baseIndex + (rings.Length - 1) * n;
                var bot = baseIndex;
                var botC = Vector3.zero;
                var topC = Vector3.zero;
                for (var i = 0; i < n; i++)
                {
                    botC += rings[0][i];
                    topC += rings[rings.Length - 1][i];
                }

                botC /= n;
                topC /= n;
                var botIdx = verts.Count;
                verts.Add(botC);
                var topIdx = verts.Count;
                verts.Add(topC);
                for (var i = 0; i < n; i++)
                {
                    var i1 = (i + 1) % n;
                    tris.Add(botIdx);
                    tris.Add(bot + i);
                    tris.Add(bot + i1);
                    tris.Add(topIdx);
                    tris.Add(top + i1);
                    tris.Add(top + i);
                }
            }

            if (tris.Count < 3)
                return null;
            var mesh = new Mesh { name = "SUHull" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var norms = mesh.normals;
            var flip = 0f;
            for (var i = 0; i < norms.Length; i++)
                flip += norms[i].y;
            if (flip < 0f)
            {
                for (var i = 0; i < tris.Count; i += 3)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        static List<List<Vector3>> TraceLoops(bool[,] occ, float zSign)
        {
            var edges = new List<(int a, int b)>(64);
            for (var x = 0; x < WorldScale.ShipGrid; x++)
            for (var y = 0; y < WorldScale.ShipGrid; y++)
            {
                if (!occ[x, y])
                    continue;
                if (!Has(occ, x, y - 1))
                    edges.Add((Pack(x, y), Pack(x + 1, y)));
                if (!Has(occ, x + 1, y))
                    edges.Add((Pack(x + 1, y), Pack(x + 1, y + 1)));
                if (!Has(occ, x, y + 1))
                    edges.Add((Pack(x + 1, y + 1), Pack(x, y + 1)));
                if (!Has(occ, x - 1, y))
                    edges.Add((Pack(x, y + 1), Pack(x, y)));
            }

            var used = new bool[edges.Count];
            var loops = new List<List<Vector3>>();
            for (var s = 0; s < edges.Count; s++)
            {
                if (used[s])
                    continue;
                var loop = new List<Vector3>(16);
                var cur = s;
                var start = edges[s].a;
                var guard = 0;
                while (cur >= 0 && guard++ < 128)
                {
                    used[cur] = true;
                    var a = edges[cur].a;
                    loop.Add(Corner(a % 16, a / 16, zSign));
                    var b = edges[cur].b;
                    if (b == start)
                        break;
                    cur = -1;
                    for (var i = 0; i < edges.Count; i++)
                    {
                        if (!used[i] && edges[i].a == b)
                        {
                            cur = i;
                            break;
                        }
                    }
                }

                if (loop.Count >= 3)
                    loops.Add(loop);
            }

            return loops;
        }

        static int Pack(int x, int y) => x + y * 16;

        static Vector3 Corner(int ix, int iy, float zSign)
        {
            var c = WorldScale.ShipCell;
            return new Vector3((ix - 4.5f) * c, 0f, (iy - 4.5f) * c * zSign);
        }

        static List<Vector3> Chaikin(List<Vector3> pts, int times)
        {
            var p = pts;
            for (var t = 0; t < times; t++)
            {
                var n = p.Count;
                var next = new List<Vector3>(n * 2);
                for (var i = 0; i < n; i++)
                {
                    var a = p[i];
                    var b = p[(i + 1) % n];
                    next.Add(a * 0.75f + b * 0.25f);
                    next.Add(a * 0.25f + b * 0.75f);
                }

                p = next;
            }

            return p;
        }

        static void Outset(List<Vector3> p, float dist)
        {
            var n = p.Count;
            if (n < 3)
                return;
            var o = new Vector3[n];
            for (var i = 0; i < n; i++)
            {
                var prev = p[(i - 1 + n) % n];
                var nxt = p[(i + 1) % n];
                var tan = nxt - prev;
                tan.y = 0f;
                if (tan.sqrMagnitude < 1e-8f)
                {
                    o[i] = p[i];
                    continue;
                }

                var nor = Vector3.Cross(Vector3.up, tan).normalized;
                o[i] = p[i] + nor * dist;
            }

            for (var i = 0; i < n; i++)
                p[i] = o[i];
        }

        static void BuildModule(Kit k, FocusShipModule module)
        {
            if (!module.OnGrid)
                return;
            var p = CellPos(module.GridX, module.GridY, k.Z);
            var deck = p + Vector3.up * 1.18f;
            var kind = Classify(module.Type);
            switch (kind)
            {
                case Kind.Core: BuildCore(k, deck); break;
                case Kind.Armor: BuildArmor(k, deck); break;
                case Kind.Cargo: BuildCargo(k, deck); break;
                case Kind.Colony: BuildColony(k, deck); break;
                case Kind.Troop: BuildTroop(k, deck); break;
                case Kind.Weapon: BuildWeapon(k, deck, module.Type); break;
                case Kind.Engine: BuildEngine(k, p, module.Type); break;
                case Kind.Shield: BuildShield(k, deck); break;
                case Kind.Sensor: BuildSensor(k, deck); break;
                case Kind.Science: BuildScience(k, deck); break;
                case Kind.Stealth: BuildStealth(k, deck); break;
                case Kind.Repair: BuildRepair(k, deck); break;
                case Kind.Special: BuildSpecial(k, deck, module.Type); break;
                default: BuildUtility(k, deck); break;
            }
        }

        static void BuildCore(Kit k, Vector3 p)
        {
            var c = WorldScale.ShipCell;
            var z = k.Z;
            k.Cyl("Tower", p + new Vector3(0f, 1.35f, -0.08f * z), new Vector3(c * 0.42f, 0.55f, c * 0.42f),
                Vector3.zero, k.Pal.Dark);
            k.Sph("Cupola", p + new Vector3(0f, 1.95f, 0.12f * z), c * 0.22f, k.Pal.Hull);
            k.Blob("Bridge", p + new Vector3(0f, 1.72f, 0.28f * z), new Vector3(c * 0.48f, 0.2f, c * 0.32f),
                k.Pal.Hull);
            k.Cyl("Window", p + new Vector3(0f, 1.74f, 0.42f * z), new Vector3(0.28f, 0.05f, 0.28f),
                new Vector3(90f, 0f, 0f), k.Pal.Glass);
            k.Cyl("Mast", p + new Vector3(0.38f, 2.22f, -0.1f * z), new Vector3(0.05f, 0.38f, 0.05f), Vector3.zero,
                k.Pal.Dark);
            var beacon = k.Sph("Beacon", p + new Vector3(0.38f, 2.62f, -0.1f * z), 0.08f, k.Pal.Glow);
            Pulse(beacon, 3.2f, 7f, 5.5f);
            k.Link("RadL", p + new Vector3(-c * 0.42f, 0.55f, -0.28f), p + new Vector3(-c * 0.42f, 0.55f, 0.28f),
                0.07f, k.Pal.Dark);
            k.Link("RadR", p + new Vector3(c * 0.42f, 0.55f, -0.28f), p + new Vector3(c * 0.42f, 0.55f, 0.28f),
                0.07f, k.Pal.Dark);
            k.Cyl("Stripe", p + new Vector3(0f, 1.12f, 0f), new Vector3(c * 0.38f, 0.035f, c * 0.38f), Vector3.zero,
                k.Pal.Glow);
        }

        static void BuildArmor(Kit k, Vector3 p)
        {
            var c = WorldScale.ShipCell;
            k.Cyl("Plate", p + new Vector3(0f, 0.95f, 0f), new Vector3(c * 0.95f, 0.08f, c * 0.95f), Vector3.zero,
                k.Pal.Armor);
            k.Sph("BossA", p + new Vector3(-c * 0.32f, 0.52f, c * 0.28f), 0.16f, k.Pal.Brass);
            k.Sph("BossB", p + new Vector3(c * 0.32f, 0.52f, c * 0.28f), 0.16f, k.Pal.Brass);
        }

        static void BuildCargo(Kit k, Vector3 p)
        {
            var c = WorldScale.ShipCell;
            k.Cyl("Hold", p + new Vector3(0f, 0.72f, 0f), new Vector3(c * 0.78f, 0.7f, c * 0.78f), Vector3.zero,
                k.Pal.Cargo);
            k.Sph("CapTop", p + new Vector3(0f, 1.42f, 0f), c * 0.39f, k.Pal.Cargo);
            k.Sph("CapBot", p + new Vector3(0f, 0.12f, 0f), c * 0.36f, k.Pal.Dark);
            k.Cyl("Band", p + new Vector3(0f, 0.72f, 0f), new Vector3(c * 0.86f, 0.06f, c * 0.86f), Vector3.zero,
                k.Pal.AmberDim);
            k.Cyl("ClampL", p + new Vector3(-c * 0.42f, 0.28f, 0f), new Vector3(0.12f, 0.16f, 0.12f), Vector3.zero,
                k.Pal.Brass);
            k.Cyl("ClampR", p + new Vector3(c * 0.42f, 0.28f, 0f), new Vector3(0.12f, 0.16f, 0.12f), Vector3.zero,
                k.Pal.Brass);
        }

        static void BuildColony(Kit k, Vector3 p)
        {
            var c = WorldScale.ShipCell;
            k.Cyl("Hab", p + new Vector3(0f, 0.7f, 0f), new Vector3(c * 0.74f, 0.62f, c * 0.74f), Vector3.zero,
                k.Pal.Hull);
            k.Cyl("WindowRing", p + new Vector3(0f, 0.78f, 0f), new Vector3(c * 0.78f, 0.08f, c * 0.78f), Vector3.zero,
                k.Pal.Glass);
            var dome = k.Sph("Dome", p + new Vector3(0f, 1.42f, 0f), c * 0.36f, k.Pal.Glass);
            var spin = dome.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 6f;
            spin.BobMeters = 0f;
            k.Cyl("Collar", p + new Vector3(0f, 0.18f, 0f), new Vector3(c * 0.5f, 0.12f, c * 0.5f), Vector3.zero,
                k.Pal.Dark);
            k.Sph("Dock", p + new Vector3(0f, 0.4f, 0.48f * k.Z), 0.18f, k.Pal.Dark);
        }

        static void BuildTroop(Kit k, Vector3 p)
        {
            var c = WorldScale.ShipCell;
            k.Cyl("Bay", p + new Vector3(0f, 0.62f, 0f), new Vector3(c * 0.88f, 0.7f, c * 0.88f), Vector3.zero,
                k.Pal.Hull);
            k.Sph("Crown", p + new Vector3(0f, 1.22f, 0f), c * 0.38f, k.Pal.Dark);
            k.Blob("Well", p + new Vector3(0f, 0.52f, 0.42f * k.Z), new Vector3(c * 0.55f, 0.7f, 0.28f), k.Pal.Dark);
            k.Cyl("HangarLight", p + new Vector3(0f, 0.95f, 0.48f * k.Z), new Vector3(0.42f, 0.05f, 0.42f),
                new Vector3(90f, 0f, 0f), k.Pal.AmberDim);
            k.Cyl("Winch", p + new Vector3(0f, 1.18f, 0f), new Vector3(0.16f, 0.1f, 0.16f), Vector3.zero, k.Pal.Brass);
        }

        static void BuildWeapon(Kit k, Vector3 p, string type)
        {
            var t = type != null ? type.ToLowerInvariant() : string.Empty;
            k.Cyl("Turret", p + new Vector3(0f, 0.88f, 0f), new Vector3(0.78f, 0.28f, 0.78f), Vector3.zero, k.Pal.Weapon);
            k.Cyl("Collar", p + new Vector3(0f, 1.18f, 0f), new Vector3(0.52f, 0.1f, 0.52f), Vector3.zero, k.Pal.Weapon);
            var z = 1f;
            var glow = WeaponMat(k.Pal, t);
            if (t.Contains("missile"))
            {
                for (var x = -1; x <= 1; x += 2)
                for (var y = 0; y <= 1; y++)
                {
                    var a = p + new Vector3(x * 0.26f, 0.62f + y * 0.26f, 0.05f * z);
                    var b = a + new Vector3(0f, 0f, 0.85f * z);
                    k.Link("Tube", a, b, 0.09f, k.Pal.Weapon);
                    k.Sph("Cap", b, 0.09f, glow);
                }
            }
            else if (t.Contains("ion"))
            {
                k.Link("RailL", p + new Vector3(-0.2f, 0.82f, -0.05f * z), p + new Vector3(-0.2f, 0.82f, 1.45f * z),
                    0.07f, k.Pal.Weapon);
                k.Link("RailR", p + new Vector3(0.2f, 0.82f, -0.05f * z), p + new Vector3(0.2f, 0.82f, 1.45f * z),
                    0.07f, k.Pal.Weapon);
                var arc = k.Sph("Arc", p + new Vector3(0f, 0.82f, 1.52f * z), 0.14f, glow);
                Pulse(arc, 2.5f, 7.5f, 8f);
                k.Cyl("Coil", p + new Vector3(0f, 0.7f, 0.12f * z), new Vector3(0.38f, 0.16f, 0.38f), Vector3.zero,
                    k.Pal.Brass);
            }
            else if (t.Contains("plasma"))
            {
                k.Link("Barrel", p + new Vector3(0f, 0.7f, 0.05f * z), p + new Vector3(0f, 0.7f, 0.95f * z), 0.2f,
                    k.Pal.Weapon);
                k.Cyl("Ring", p + new Vector3(0f, 0.7f, 0.85f * z), new Vector3(0.48f, 0.07f, 0.48f),
                    new Vector3(90f, 0f, 0f), k.Pal.Brass);
                var orb = k.Sph("Orb", p + new Vector3(0f, 0.7f, 1.15f * z), 0.22f, glow);
                Pulse(orb, 3f, 8f, 4.2f);
            }
            else if (t.Contains("iem") || t.Contains("pulse"))
            {
                k.Sph("Dish", p + new Vector3(0f, 0.78f, 0.28f * z), 0.52f, k.Pal.Dark);
                k.Sph("DishFace", p + new Vector3(0f, 0.78f, 0.38f * z), 0.38f, glow);
                k.Link("Feed", p + new Vector3(0f, 0.78f, 0.45f * z), p + new Vector3(0f, 0.78f, 0.85f * z), 0.06f,
                    k.Pal.Hull);
            }
            else
            {
                k.Link("Barrel", p + new Vector3(0f, 0.92f, 0.1f * z), p + new Vector3(0f, 0.92f, 1.7f * z), 0.13f,
                    k.Pal.Weapon);
                k.Cyl("RingA", p + new Vector3(0f, 0.92f, 0.7f * z), new Vector3(0.32f, 0.05f, 0.32f),
                    new Vector3(90f, 0f, 0f), k.Pal.Brass);
                k.Cyl("RingB", p + new Vector3(0f, 0.92f, 1.32f * z), new Vector3(0.28f, 0.05f, 0.28f),
                    new Vector3(90f, 0f, 0f), k.Pal.Brass);
                var tip = k.Sph("Crystal", p + new Vector3(0f, 0.92f, 1.88f * z), 0.14f, glow);
                Pulse(tip, 3.5f, 7.2f, 6.5f);
            }
        }

        static void BuildEngine(Kit k, Vector3 p, string type)
        {
            var c = WorldScale.ShipCell;
            var hyper = type != null && type.ToLowerInvariant().Contains("hyper");
            var y = 0.55f;
            k.Link("Nacelle", p + new Vector3(0f, y, 1.15f), p + new Vector3(0f, y, -0.85f), c * 0.32f, k.Pal.Hull);
            k.Cyl("Fairing", p + new Vector3(0f, y, 0.55f), new Vector3(c * 0.48f, 0.14f, c * 0.48f),
                new Vector3(90f, 0f, 0f), k.Pal.Hull);
            var s = hyper ? 1.25f : 1.05f;
            k.Cyl("BellA", p + new Vector3(0f, y, -0.95f), new Vector3(1.15f * s, 0.22f, 1.15f * s),
                new Vector3(90f, 0f, 0f), k.Pal.EngineBody);
            k.Cyl("BellB", p + new Vector3(0f, y, -1.22f), new Vector3(0.88f * s, 0.14f, 0.88f * s),
                new Vector3(90f, 0f, 0f), k.Pal.Brass);
            k.Cyl("BellC", p + new Vector3(0f, y, -1.42f), new Vector3(0.58f * s, 0.1f, 0.58f * s),
                new Vector3(90f, 0f, 0f), k.Pal.Dark);
            var core = k.Sph("Plasma", p + new Vector3(0f, y, -1.52f), 0.22f * s,
                hyper ? k.Pal.EngineCoreHot : k.Pal.EngineCore);
            var flame = k.Sph("Flame", p + new Vector3(0f, y, -1.92f), 0.13f * s, k.Pal.EngineFlame);
            flame.transform.localScale = Vector3.Scale(flame.transform.localScale, new Vector3(0.78f, 0.78f, 1.45f));
            var wash = k.Sph("Wash", p + new Vector3(0f, y, -2.28f), 0.08f * s, k.Pal.EngineFlame);
            wash.transform.localScale = Vector3.Scale(wash.transform.localScale, new Vector3(0.62f, 0.62f, 1.7f));
            var exhaust = AddEngineExhaust(k, p + new Vector3(0f, y, -1.62f), s, hyper);
            var burn = exhaust.AddComponent<EngineBurn>();
            burn.Plasma = core.transform;
            burn.Flame = flame.transform;
            burn.Wash = wash.transform;
            burn.Plume = exhaust.GetComponent<ParticleSystem>();
            burn.Phase = core.GetInstanceID() * 0.17f;
            burn.Breath = hyper ? 1.7f : 2.2f;
            burn.Flicker = hyper ? 15f : 12.4f;
        }

        static GameObject AddEngineExhaust(Kit k, Vector3 aft, float scale, bool hyper)
        {
            var go = new GameObject("Exhaust");
            go.transform.SetParent(k.Root, false);
            go.transform.localPosition = aft;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.back);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = hyper ? 0.48f : 0.34f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(hyper ? 4.4f : 2.8f, hyper ? 6.2f : 4.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f * scale, 0.16f * scale);
            var hot = Color.Lerp(k.Pal.EngineColor, Color.white, hyper ? 0.45f : 0.18f);
            main.startColor = hot;
            main.maxParticles = hyper ? 32 : 22;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.loop = true;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = hyper ? 28f : 18f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = hyper ? 11f : 7f;
            shape.radius = 0.04f * scale;
            shape.rotation = Vector3.zero;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var c = k.Pal.EngineColor;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(hot, 0f),
                    new GradientColorKey(c, 0.35f),
                    new GradientColorKey(c * 0.25f, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.7f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(grad);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0.12f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = hyper ? 0.9f : 0.55f;
            vel.radial = new ParticleSystem.MinMaxCurve(-0.08f, 0.05f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = k.Pal.Exhaust;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0f;
            renderer.cameraVelocityScale = 0f;
            ps.Play();
            return go;
        }

        static void BuildShield(Kit k, Vector3 p)
        {
            k.Cyl("Pedestal", p + new Vector3(0f, 0.32f, 0f), new Vector3(0.55f, 0.18f, 0.55f), Vector3.zero, k.Pal.Dark);
            k.Cyl("Emitter", p + new Vector3(0f, 0.58f, 0f), new Vector3(0.38f, 0.14f, 0.38f), Vector3.zero, k.Pal.Hull);
            var ring = k.Cyl("Halo", p + new Vector3(0f, 0.92f, 0f), new Vector3(0.72f, 0.018f, 0.72f), Vector3.zero,
                k.Pal.Dark);
            var spin = ring.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 22f;
            spin.BobMeters = 0.02f;
            Pulse(k.Sph("HaloGlow", p + new Vector3(0.38f, 0.92f, 0f), 0.05f, k.Pal.Glow), 2.4f, 5.5f, 3.2f);
            k.Blob("FinA", p + new Vector3(0.42f, 0.62f, 0f), new Vector3(0.1f, 0.55f, 0.22f), k.Pal.Hull);
            k.Blob("FinB", p + new Vector3(-0.42f, 0.62f, 0f), new Vector3(0.1f, 0.55f, 0.22f), k.Pal.Hull);
        }

        static void BuildSensor(Kit k, Vector3 p)
        {
            var z = k.Z;
            k.Sph("Base", p + new Vector3(0f, 0.28f, 0f), 0.32f, k.Pal.Dark);
            k.Cyl("Mast", p + new Vector3(0f, 1.05f, 0f), new Vector3(0.09f, 0.78f, 0.09f), Vector3.zero, k.Pal.Hull);
            k.Link("ArmL", p + new Vector3(-0.55f, 1.35f, 0.05f * z), p + new Vector3(0.55f, 1.35f, 0.05f * z), 0.04f,
                k.Pal.Dark);
            var dish = k.Sph("Dish", p + new Vector3(0f, 1.72f, 0.12f * z), 0.58f, k.Pal.Hull);
            dish.transform.localScale = new Vector3(1.25f, 0.16f, 1.25f);
            dish.transform.localRotation = Quaternion.Euler(18f * z, 0f, 0f);
            var spin = dish.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 12f;
            spin.BobMeters = 0f;
            k.Sph("Feed", p + new Vector3(0f, 1.62f, 0.32f * z), 0.12f, k.Pal.Glow);
            var blink = k.Sph("Blink", p + new Vector3(0.12f, 2.05f, 0f), 0.07f, k.Pal.Glow);
            Pulse(blink, 0.4f, 6f, 1.6f);
        }

        static void BuildScience(Kit k, Vector3 p)
        {
            var c = WorldScale.ShipCell;
            k.Cyl("Lab", p + new Vector3(0f, 0.78f, 0f), new Vector3(c * 0.52f, 0.7f, c * 0.52f), Vector3.zero,
                k.Pal.Glass);
            k.Cyl("Core", p + new Vector3(0f, 0.78f, 0f), new Vector3(c * 0.18f, 0.55f, c * 0.18f), Vector3.zero,
                k.Pal.Glow);
            k.Cyl("Eq", p + new Vector3(0f, 0.78f, 0f), new Vector3(c * 0.62f, 0.06f, c * 0.62f), Vector3.zero,
                k.Pal.Brass);
            k.Link("Arm", p + new Vector3(c * 0.12f, 0.9f, 0f), p + new Vector3(c * 0.55f, 0.9f, 0f), 0.045f, k.Pal.Dark);
            var sample = k.Sph("Sample", p + new Vector3(c * 0.62f, 0.9f, 0f), 0.12f, k.Pal.Glow);
            Pulse(sample, 1.5f, 5f, 2.2f);
        }

        static void BuildStealth(Kit k, Vector3 p)
        {
            var c = WorldScale.ShipCell;
            k.Box("FacetA", p + new Vector3(0f, 0.55f, 0f), new Vector3(c * 1.12f, 0.55f, c * 1.18f), k.Pal.Dark);
            k.Box("FacetB", p + new Vector3(0f, 0.88f, -0.1f * k.Z), new Vector3(c * 0.85f, 0.18f, c * 0.9f),
                k.Pal.Dark);
        }

        static void BuildRepair(Kit k, Vector3 p)
        {
            k.Cyl("Shoulder", p + new Vector3(0f, 1.05f, 0f), new Vector3(0.55f, 0.18f, 0.55f), Vector3.zero, k.Pal.Hull);
            k.Link("Boom", p + new Vector3(-0.55f, 0.85f, 0f), p + new Vector3(0.55f, 0.85f, 0f), 0.05f, k.Pal.Dark);
            k.Sph("DroneA", p + new Vector3(0.55f, 0.85f, 0.15f), 0.2f, k.Pal.Hull);
            k.Sph("DroneB", p + new Vector3(-0.55f, 0.85f, -0.1f), 0.2f, k.Pal.Hull);
            var tool = k.Cyl("Torch", p + new Vector3(0.55f, 0.85f, 0.38f), new Vector3(0.06f, 0.14f, 0.06f),
                new Vector3(90f, 0f, 0f), k.Pal.AmberDim);
            Pulse(tool, 1.2f, 5.5f, 7f);
        }

        static void BuildSpecial(Kit k, Vector3 p, string type)
        {
            var t = type != null ? type.ToLowerInvariant() : string.Empty;
            k.Sph("Rack", p + new Vector3(0f, 0.52f, 0f), 0.42f, k.Pal.Dark);
            if (t.Contains("teleport"))
            {
                var ring = k.Cyl("Gate", p + new Vector3(0f, 0.95f, 0f), new Vector3(0.7f, 0.06f, 0.7f), Vector3.zero,
                    k.Pal.Glow);
                var spin = ring.AddComponent<HoloSpin>();
                spin.DegreesPerSecond = 40f;
                spin.BobMeters = 0.02f;
            }
            else
            {
                k.Blob("Board", p + new Vector3(0f, 0.72f, 0.22f), new Vector3(0.55f, 0.32f, 0.12f), k.Pal.Glow);
                k.Link("Cable", p + new Vector3(0.05f, 0.35f, 0f), p + new Vector3(0.45f, 0.35f, 0.15f), 0.035f,
                    k.Pal.Brass);
            }
        }

        static void BuildUtility(Kit k, Vector3 p)
        {
            k.Box("Mod", p + new Vector3(0f, 1.05f, 0f), new Vector3(0.7f, 0.35f, 0.7f), k.Pal.Hull);
            k.Cyl("Cap", p + new Vector3(0f, 0.98f, 0f), new Vector3(0.28f, 0.06f, 0.28f), Vector3.zero, k.Pal.Glow);
            k.Cyl("Vent", p + new Vector3(0.22f, 0.7f, 0.22f), new Vector3(0.1f, 0.16f, 0.1f), Vector3.zero, k.Pal.Dark);
        }

        static void AddNavLights(Kit k, bool[,] occupied)
        {
            var minX = 8;
            var maxX = 0;
            var noseY = 4;
            var any = false;
            for (var x = 0; x < 9; x++)
            for (var y = 0; y < 9; y++)
            {
                if (!occupied[x, y])
                    continue;
                any = true;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                noseY = k.Z < 0 ? Mathf.Min(noseY, y) : Mathf.Max(noseY, y);
            }

            if (!any)
                return;
            var port = CellPos(minX, 4, k.Z) + new Vector3(-WorldScale.ShipCell * 0.42f, 0.55f, 0f);
            var star = CellPos(maxX, 4, k.Z) + new Vector3(WorldScale.ShipCell * 0.42f, 0.55f, 0f);
            Pulse(k.Sph("NavPort", port, 0.08f, k.Pal.NavRed), 3f, 6.5f, 2.4f);
            Pulse(k.Sph("NavStar", star, 0.08f, k.Pal.Glow), 3f, 6.5f, 2.4f);
            var nose = CellPos(4, noseY, k.Z) + new Vector3(0f, 0.38f, 0.95f);
            Pulse(k.Sph("NavNose", nose, 0.07f, k.Pal.Glow), 1.5f, 7f, 9f);
        }

        static void Pulse(GameObject go, float min, float max, float speed)
        {
            if (go == null)
                return;
            var pulse = go.AddComponent<EmissionPulse>();
            pulse.Min = min;
            pulse.Max = max;
            pulse.Speed = speed;
            pulse.Phase = go.GetInstanceID() * 0.13f;
        }

        static Vector3 CellPos(int gx, int gy, float zSign)
        {
            var c = WorldScale.ShipCell;
            return new Vector3((gx - WorldScale.ShipCoreCell) * c, 0f, (gy - WorldScale.ShipCoreCell) * c * zSign);
        }

        static float NoseSign(List<FocusShipModule> laid)
        {
            var engLow = 0;
            var engHigh = 0;
            var weaponsLow = 0;
            var weaponsHigh = 0;
            for (var i = 0; i < laid.Count; i++)
            {
                var m = laid[i];
                if (!m.OnGrid)
                    continue;
                var kind = Classify(m.Type);
                if (kind == Kind.Engine)
                {
                    if (m.GridY < WorldScale.ShipCoreCell) engLow++;
                    else if (m.GridY > WorldScale.ShipCoreCell) engHigh++;
                }
                else if (kind == Kind.Weapon)
                {
                    if (m.GridY < WorldScale.ShipCoreCell) weaponsLow++;
                    else if (m.GridY > WorldScale.ShipCoreCell) weaponsHigh++;
                }
            }

            if (engHigh != engLow)
                return engHigh > engLow ? -1f : 1f;
            if (weaponsHigh != weaponsLow)
                return weaponsHigh > weaponsLow ? 1f : -1f;
            return -1f;
        }

        static Kind Classify(string type)
        {
            if (string.IsNullOrEmpty(type))
                return Kind.Utility;
            var t = type.ToLowerInvariant();
            if (t.Contains("core")) return Kind.Core;
            if (t.Contains("armor")) return Kind.Armor;
            if (t.Contains("cargo")) return Kind.Cargo;
            if (t.Contains("colony")) return Kind.Colony;
            if (t.Contains("troop")) return Kind.Troop;
            if (t.Contains("laser") || t.Contains("ion") || t.Contains("plasma") || t.Contains("missile")
                || t.Contains("cannon") || t.Contains("iem") || t.Contains("pulse"))
                return Kind.Weapon;
            if (t.Contains("hyper") || t.Contains("speed") || t.Contains("booster") || t.Contains("drive")
                || t.Contains("engine"))
                return Kind.Engine;
            if (t.Contains("shield")) return Kind.Shield;
            if (t.Contains("sensor")) return Kind.Sensor;
            if (t.Contains("science")) return Kind.Science;
            if (t.Contains("stealth")) return Kind.Stealth;
            if (t.Contains("repair") || t.Contains("drone")) return Kind.Repair;
            if (t.Contains("hack") || t.Contains("teleport")) return Kind.Special;
            return Kind.Utility;
        }

        static List<FocusShipModule> Synthetic(int seed)
        {
            var list = new List<FocusShipModule>
            {
                Mod(1, "ShipCore", 4, 4),
                Mod(2, "ArmorPlating", 3, 4),
                Mod(3, "ArmorPlating", 5, 4),
                Mod(4, "LaserCannon", 4, 3),
                Mod(5, "HyperspaceDrive", 4, 5)
            };
            var extra = Mathf.Abs(seed) % 5;
            if (extra >= 1) list.Add(Mod(6, "IonCannon", 3, 3));
            if (extra >= 2) list.Add(Mod(7, "ShieldGenerator", 5, 5));
            if (extra >= 3) list.Add(Mod(8, "CargoHold", 3, 5));
            if (extra >= 4) list.Add(Mod(9, "SensorArray", 5, 3));
            return list;
        }

        static FocusShipModule Mod(int id, string type, int x, int y)
        {
            return new FocusShipModule { Id = id, Type = type, GridX = x, GridY = y };
        }

        static bool AnyOnGrid(List<FocusShipModule> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].OnGrid) return true;
            }

            return false;
        }

        static void AutoPlace(List<FocusShipModule> list)
        {
            var used = new bool[9, 9];
            var hasCore = false;
            for (var i = 0; i < list.Count; i++)
            {
                if (Classify(list[i].Type) != Kind.Core) continue;
                list[i].GridX = 4;
                list[i].GridY = 4;
                used[4, 4] = true;
                hasCore = true;
                break;
            }

            if (!hasCore)
            {
                list.Insert(0, Mod(0, "ShipCore", 4, 4));
                used[4, 4] = true;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].OnGrid)
                {
                    used[list[i].GridX, list[i].GridY] = true;
                    continue;
                }

                TryPlace(list[i], Classify(list[i].Type), used);
            }
        }

        static void FillMissing(List<FocusShipModule> list)
        {
            var used = new bool[9, 9];
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].OnGrid) used[list[i].GridX, list[i].GridY] = true;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (!list[i].OnGrid) TryPlace(list[i], Classify(list[i].Type), used);
            }
        }

        static void TryPlace(FocusShipModule module, Kind kind, bool[,] used)
        {
            Vector2Int[] pref = kind switch
            {
                Kind.Weapon => new[]
                {
                    new Vector2Int(4, 3), new Vector2Int(4, 2), new Vector2Int(3, 3), new Vector2Int(5, 3),
                    new Vector2Int(4, 1), new Vector2Int(3, 2), new Vector2Int(5, 2)
                },
                Kind.Engine => new[]
                {
                    new Vector2Int(4, 5), new Vector2Int(4, 6), new Vector2Int(3, 5), new Vector2Int(5, 5),
                    new Vector2Int(4, 7), new Vector2Int(3, 6), new Vector2Int(5, 6)
                },
                Kind.Armor => new[]
                {
                    new Vector2Int(3, 4), new Vector2Int(5, 4), new Vector2Int(2, 4), new Vector2Int(6, 4),
                    new Vector2Int(3, 3), new Vector2Int(5, 5)
                },
                _ => new[]
                {
                    new Vector2Int(3, 5), new Vector2Int(5, 5), new Vector2Int(3, 3), new Vector2Int(5, 3),
                    new Vector2Int(4, 6), new Vector2Int(2, 4), new Vector2Int(6, 4), new Vector2Int(4, 2)
                }
            };
            for (var i = 0; i < pref.Length; i++)
            {
                var c = pref[i];
                if (c.x < 0 || c.x > 8 || c.y < 0 || c.y > 8 || used[c.x, c.y]) continue;
                module.GridX = c.x;
                module.GridY = c.y;
                used[c.x, c.y] = true;
                return;
            }

            for (var y = 0; y < 9; y++)
            for (var x = 0; x < 9; x++)
            {
                if (used[x, y]) continue;
                module.GridX = x;
                module.GridY = y;
                used[x, y] = true;
                return;
            }
        }

        static Texture2D LoadTex(string name)
        {
            return Resources.Load<Texture2D>("Ships/" + name);
        }

        static Material HullMat(Color albedo, Color rim, Texture2D tex, float tiling = 0.85f)
        {
            var shader = Shader.Find("SU/HullMetal") ?? Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_MainTex"))
            {
                mat.mainTexture = tex;
                mat.SetTextureScale("_MainTex", Vector2.one * tiling);
            }

            if (mat.HasProperty("_Color")) mat.SetColor("_Color", albedo);
            if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", rim * 0.55f);
            if (mat.HasProperty("_EmissionMul")) mat.SetFloat("_EmissionMul", 0f);
            if (mat.HasProperty("_PanelScale")) mat.SetFloat("_PanelScale", 0.55f);
            if (mat.HasProperty("_Groove")) mat.SetFloat("_Groove", 0.06f);
            if (mat.HasProperty("_Wear")) mat.SetFloat("_Wear", 0.1f);
            return mat;
        }

        static Material ParticleMat(Color tint, float mul, Texture2D tex)
        {
            var shader = Shader.Find("SU/ParticleGlow") ?? Shader.Find("Sprites/Default") ?? Shader.Find("SU/UnlitEmissive");
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = tex;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Emission")) mat.SetColor("_Emission", tint);
            if (mat.HasProperty("_EmissionMul")) mat.SetFloat("_EmissionMul", mul);
            return mat;
        }

        static Material Emissive(Color tint, float mul, Texture2D tex = null)
        {
            var shader = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_MainTex"))
            {
                mat.mainTexture = tex;
                mat.SetTextureScale("_MainTex", Vector2.one * 1.4f);
            }

            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Emission")) mat.SetColor("_Emission", tint);
            if (mat.HasProperty("_EmissionMul")) mat.SetFloat("_EmissionMul", mul);
            return mat;
        }

        static Material WeaponMat(Palette pal, string t)
        {
            if (t.Contains("ion")) return pal.WepIon;
            if (t.Contains("plasma")) return pal.WepPlasma;
            if (t.Contains("missile")) return pal.WepMissile;
            if (t.Contains("iem") || t.Contains("pulse")) return pal.WepIem;
            return pal.WepLaser;
        }

        static void Drop(Object o)
        {
            if (o == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(o);
            else
                Object.DestroyImmediate(o);
        }

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var t = parent.GetChild(i);
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "SUHull")
                    Drop(mf.sharedMesh);
                Drop(t.gameObject);
            }
        }

        sealed class Palette
        {
            public readonly Material Hull;
            public readonly Material Dark;
            public readonly Material Armor;
            public readonly Material Brass;
            public readonly Material Cargo;
            public readonly Material Weapon;
            public readonly Material EngineBody;
            public readonly Material Glow;
            public readonly Material Glass;
            public readonly Material Engine;
            public readonly Material Exhaust;
            public readonly Material AmberDim;
            public readonly Material EngineCore;
            public readonly Material EngineCoreHot;
            public readonly Material EngineFlame;
            public readonly Material WepLaser;
            public readonly Material WepIon;
            public readonly Material WepPlasma;
            public readonly Material WepMissile;
            public readonly Material WepIem;
            public readonly Material NavRed;
            public readonly Color EngineColor;

            public Palette(bool owned, Color accent)
            {
                Hull = HullMat(Color.white, accent, LoadTex("Hull"), 0.42f);
                Dark = HullMat(new Color(0.62f, 0.66f, 0.72f), accent * 0.4f, LoadTex("Hull"), 0.55f);
                Armor = HullMat(Color.white, accent * 0.3f, LoadTex("Armor"), 0.65f);
                Brass = HullMat(Color.white, Amber, LoadTex("Trim"), 1.1f);
                Cargo = HullMat(Color.white, Amber, LoadTex("Cargo"), 0.7f);
                Weapon = HullMat(Color.white, accent, LoadTex("Weapon"), 0.9f);
                EngineBody = HullMat(Color.white, Amber, LoadTex("Engine"), 0.75f);
                Glow = Emissive(accent, owned ? 3.6f : 2.4f, LoadTex("GlowCyan"));
                Glass = Emissive(owned ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.5f, 0.4f), 2.4f,
                    LoadTex("Glass"));
                EngineColor = owned ? Amber : new Color(1f, 0.4f, 0.22f);
                Engine = Emissive(EngineColor, 5.4f, LoadTex("GlowAmber"));
                Exhaust = ParticleMat(EngineColor, 1.55f, LoadTex("Exhaust"));
                AmberDim = Emissive(Amber, 1.8f, LoadTex("GlowAmber"));
                EngineCore = ParticleMat(Color.Lerp(EngineColor, Color.white, 0.38f), 3.6f, LoadTex("GlowAmber"));
                EngineCoreHot = ParticleMat(Cyan, 4.2f, LoadTex("GlowCyan"));
                EngineFlame = ParticleMat(EngineColor, 2.15f, LoadTex("GlowAmber"));
                WepLaser = Emissive(Cyan, 5.2f);
                WepIon = Emissive(new Color(0.35f, 0.65f, 1f), 5.2f);
                WepPlasma = Emissive(new Color(0.9f, 0.3f, 1f), 5.4f);
                WepMissile = Emissive(Amber, 3.8f);
                WepIem = Emissive(new Color(0.72f, 0.45f, 1f), 5f);
                NavRed = Emissive(new Color(1f, 0.18f, 0.12f), 5.5f);
            }
        }

        sealed class Kit
        {
            public readonly Transform Root;
            public readonly float Z;
            public readonly Palette Pal;

            public Kit(Transform root, float z, Palette pal)
            {
                Root = root;
                Z = z;
                Pal = pal;
            }

            public GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat)
            {
                return Part(name, PrimitiveType.Cube, pos, scale, Vector3.zero, mat);
            }

            public GameObject Cyl(string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
            {
                return Part(name, PrimitiveType.Cylinder, pos, scale, euler, mat);
            }

            public GameObject Sph(string name, Vector3 pos, float radius, Material mat)
            {
                return Part(name, PrimitiveType.Sphere, pos, Vector3.one * (radius * 2f), Vector3.zero, mat);
            }

            public GameObject Blob(string name, Vector3 pos, Vector3 diameters, Material mat)
            {
                return Part(name, PrimitiveType.Sphere, pos, diameters, Vector3.zero, mat);
            }

            public GameObject Link(string name, Vector3 a, Vector3 b, float radius, Material mat)
            {
                var d = b - a;
                var len = d.magnitude;
                if (len < 0.02f)
                    return Sph(name, a, radius, mat);
                var go = Part(name, PrimitiveType.Capsule, (a + b) * 0.5f,
                    new Vector3(radius * 2f, len * 0.5f, radius * 2f), Vector3.zero, mat);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
                return go;
            }

            GameObject Part(string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Vector3 euler,
                Material mat)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(Root, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.transform.localScale = scale;
                var col = go.GetComponent<Collider>();
                if (col != null)
                    Drop(col);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return go;
            }
        }
    }
}
