using Core.App;
using Core.UI;
using UnityEngine;

namespace Core.Crew
{
    /// <summary>
    /// Our crew looks like our people: the empire's species (GetMeEmpire specy.type_id) picks a family of
    /// headgear built on the helmet from the same rounded kit — a synthetic faceplate and antenna, aquatic fins,
    /// a reptilian crest, swept-back avian plumes, insect antennae and lenses, a fungal cap, lithoid facets.
    /// Humanoid (and an empire with no species row) keep the plain helmet. The same families drive the
    /// correspondent's hologram (SU/HoloPortrait _Variant).
    /// </summary>
    public static class CrewSpecies
    {
        public enum Family
        {
            Humanoid,
            Synthetic,
            Aquatic,
            Reptilian,
            Avian,
            Insectoid,
            Sylvan,
            Lithoid
        }

        /// <summary>species_types.id → family.</summary>
        public static Family FromType(int typeId) => typeId switch
        {
            2 => Family.Synthetic,
            3 or 8 => Family.Aquatic,
            5 or 13 => Family.Reptilian,
            6 => Family.Avian,
            7 or 14 => Family.Insectoid,
            9 or 10 => Family.Sylvan,
            11 => Family.Lithoid,
            _ => Family.Humanoid
        };

        /// <summary>Our own people (Humanoid until the empire row is read).</summary>
        public static Family Ours
        {
            get
            {
                var empire = AuthManager.Ensure().Empire;
                return empire?["specy"] is Newtonsoft.Json.Linq.JObject sp ? FromType(FocusContext.AsInt(sp["type_id"])) : Family.Humanoid;
            }
        }

        /// <summary>Adds one rounded piece to the head (size, corner radius, local position, euler, glow on the accent).</summary>
        public delegate void Piece(string name, Vector3 size, float radius, Vector3 pos, Vector3 euler, float glow);

        /// <summary>
        /// Dress a helmet (0.21 × 0.25 × 0.25 m, centred 0.12 m over the head pivot, visor on +z) for
        /// <paramref name="family"/>. Pieces glowing above 1 read as the species' lit features.
        /// </summary>
        public static void Dress(Family family, Piece add)
        {
            switch (family)
            {
                case Family.Synthetic:
                    add("Faceplate", new Vector3(0.2f, 0.17f, 0.03f), 0.02f, new Vector3(0f, 0.1f, 0.125f), Vector3.zero, 0.3f);
                    add("Slit", new Vector3(0.16f, 0.018f, 0.012f), 0.006f, new Vector3(0f, 0.14f, 0.142f), Vector3.zero, 3f);
                    add("Antenna", new Vector3(0.012f, 0.16f, 0.012f), 0.005f, new Vector3(0.08f, 0.3f, -0.03f), new Vector3(0f, 0f, -12f), 0.4f);
                    add("AntennaTip", new Vector3(0.03f, 0.03f, 0.03f), 0.014f, new Vector3(0.1f, 0.38f, -0.03f), Vector3.zero, 3.4f);
                    break;
                case Family.Aquatic:
                    for (var s = -1; s <= 1; s += 2)
                    {
                        add("Fin", new Vector3(0.018f, 0.16f, 0.18f), 0.008f, new Vector3(s * 0.115f, 0.15f, -0.03f), new Vector3(20f, 0f, s * -16f), 0.6f);
                        add("FinEdge", new Vector3(0.01f, 0.1f, 0.012f), 0.004f, new Vector3(s * 0.13f, 0.2f, -0.1f), new Vector3(20f, 0f, s * -16f), 2.6f);
                    }

                    add("Gills", new Vector3(0.2f, 0.02f, 0.03f), 0.008f, new Vector3(0f, 0.02f, 0.1f), Vector3.zero, 1.6f);
                    break;
                case Family.Reptilian:
                    for (var i = 0; i < 4; i++)
                        add("Crest" + i, new Vector3(0.03f, 0.1f - i * 0.018f, 0.05f), 0.012f,
                            new Vector3(0f, 0.27f - i * 0.03f, 0.04f - i * 0.055f), new Vector3(-25f, 0f, 0f), i == 0 ? 1.4f : 0.5f);
                    add("Brow", new Vector3(0.2f, 0.03f, 0.04f), 0.012f, new Vector3(0f, 0.19f, 0.1f), new Vector3(12f, 0f, 0f), 0.4f);
                    break;
                case Family.Avian:
                    for (var i = -1; i <= 1; i++)
                        add("Plume" + (i + 1), new Vector3(0.022f, 0.022f, 0.22f), 0.01f,
                            new Vector3(i * 0.04f, 0.25f - Mathf.Abs(i) * 0.02f, -0.1f), new Vector3(-38f + Mathf.Abs(i) * 8f, i * 14f, 0f), i == 0 ? 1.8f : 0.8f);
                    add("Beak", new Vector3(0.05f, 0.04f, 0.07f), 0.012f, new Vector3(0f, 0.07f, 0.14f), new Vector3(20f, 0f, 45f), 0.5f);
                    break;
                case Family.Insectoid:
                    for (var s = -1; s <= 1; s += 2)
                    {
                        add("Antenna", new Vector3(0.01f, 0.22f, 0.01f), 0.004f, new Vector3(s * 0.05f, 0.33f, 0.03f), new Vector3(-15f, 0f, s * -22f), 0.4f);
                        add("AntennaTip", new Vector3(0.026f, 0.026f, 0.026f), 0.012f, new Vector3(s * 0.1f, 0.43f, 0.0f), Vector3.zero, 3f);
                        add("Lens", new Vector3(0.075f, 0.06f, 0.03f), 0.025f, new Vector3(s * 0.055f, 0.14f, 0.125f), Vector3.zero, 2.6f);
                    }

                    add("Mandible", new Vector3(0.12f, 0.03f, 0.04f), 0.012f, new Vector3(0f, 0.02f, 0.11f), Vector3.zero, 0.3f);
                    break;
                case Family.Sylvan:
                    add("Cap", new Vector3(0.36f, 0.06f, 0.34f), 0.03f, new Vector3(0f, 0.27f, 0f), Vector3.zero, 0.6f);
                    add("CapRim", new Vector3(0.3f, 0.015f, 0.28f), 0.007f, new Vector3(0f, 0.237f, 0f), Vector3.zero, 2f);
                    for (var i = 0; i < 3; i++)
                        add("Spot" + i, new Vector3(0.04f, 0.015f, 0.04f), 0.007f,
                            new Vector3(-0.08f + i * 0.08f, 0.305f, -0.05f + (i % 2) * 0.09f), Vector3.zero, 2.4f);
                    break;
                case Family.Lithoid:
                    add("FacetL", new Vector3(0.1f, 0.12f, 0.2f), 0.004f, new Vector3(-0.07f, 0.24f, -0.01f), new Vector3(0f, 12f, 28f), 0.3f);
                    add("FacetR", new Vector3(0.1f, 0.14f, 0.18f), 0.004f, new Vector3(0.07f, 0.25f, 0.0f), new Vector3(8f, -10f, -32f), 0.3f);
                    add("Seam", new Vector3(0.02f, 0.12f, 0.2f), 0.004f, new Vector3(0f, 0.28f, 0f), Vector3.zero, 2.2f);
                    break;
            }
        }

        /// <summary>
        /// Dress a live head (seated officers): each piece is its own rounded kit renderer under
        /// <paramref name="head"/>, the accent on its property block.
        /// </summary>
        public static void DressHead(Transform head, Family family, Color accent)
        {
            if (head == null || family == Family.Humanoid)
                return;
            Dress(family, (name, size, radius, pos, euler, glow) =>
            {
                var go = UiKit.MeshPiece(head, name, UiMeshes.RoundedBox(size, radius), UiKit.Chassis, pos);
                go.transform.localRotation = Quaternion.Euler(euler);
                var block = new MaterialPropertyBlock();
                block.SetColor(UiKit.AccentId, accent);
                block.SetFloat(UiKit.AccentMulId, glow);
                go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            });
        }
    }
}
