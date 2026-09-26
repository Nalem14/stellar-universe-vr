using Core.Stations;
using Core.UI;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Airlock furniture on turned and rounded forms (matches <see cref="SasShell"/>): the login terminal — a
    /// stepped round pedestal, a waisted column, a rounded housing with a lit lip — and the lectern that holds
    /// the transmissions board. Shared meshes (built once), shared materials.
    /// </summary>
    public static class SasTerminal
    {
        static Mesh _pedestal;
        static Mesh _column;
        static Mesh _dais;

        /// <summary>The login terminal at <paramref name="foot"/> (floor point under its screen).</summary>
        public static void Build(Transform room, CicArtKit art, Vector3 foot)
        {
            var metal = art.MetalPanel(0.2f);
            var dark = art.DarkPanel(0.1f);
            var root = new GameObject("SasTerminal").transform;
            root.SetParent(room, false);
            root.localPosition = foot;

            // The walk dais under the captain: a low turned disc with a lit edge, centred on the hall.
            var dais = Part(root, "Dais", Dais, art.DeckMat(0.4f));
            dais.transform.localPosition = SasShell.Centre - foot + new Vector3(0f, 0f, -0.6f);
            dais.gameObject.AddComponent<MeshCollider>().sharedMesh = Dais;

            Part(root, "Pedestal", Pedestal, metal).transform.localPosition = new Vector3(0f, 0f, 0.08f);
            Part(root, "Column", Column, dark).transform.localPosition = new Vector3(0f, 0.16f, 0.1f);
            GateRoomDecor.Rounded(root, "Housing", new Vector3(1.55f, 0.95f, 0.12f), 0.09f, new Vector3(0f, 1.35f, 0f),
                UiKit.Chassis, CicArtKit.Cyan, 0.6f);
            GateRoomDecor.Rounded(root, "Lip", new Vector3(1.42f, 0.05f, 0.1f), 0.024f, new Vector3(0f, 0.86f, -0.07f),
                UiKit.Chassis, CicArtKit.Cyan, 1.6f);
        }

        /// <summary>A lectern post for a screen; returns the mount (screen centre, facing −z of the mount).</summary>
        public static Transform Lectern(Transform room, CicArtKit art, Vector3 foot, float yaw, float screenCentreY)
        {
            var root = new GameObject("SasLectern").transform;
            root.SetParent(room, false);
            root.localPosition = foot;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Part(root, "Pedestal", Pedestal, art.MetalPanel(0.2f)).transform.localScale = new Vector3(0.6f, 1f, 0.6f);
            var col = Part(root, "Column", Column, art.DarkPanel(0.1f));
            col.transform.localPosition = new Vector3(0f, 0.16f, 0.06f);
            col.transform.localScale = new Vector3(0.75f, 0.85f, 0.75f);
            var mount = new GameObject("ScreenMount").transform;
            mount.SetParent(root, false);
            mount.localPosition = new Vector3(0f, screenCentreY, 0f);
            return mount;
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        static Mesh Pedestal => _pedestal != null ? _pedestal : _pedestal = BuildPedestal();
        static Mesh Column => _column != null ? _column : _column = BuildColumn();
        static Mesh Dais => _dais != null ? _dais : _dais = BuildDais();

        static Mesh BuildPedestal()
        {
            var b = new DefensePlatformKit.Builder();
            b.Lathe(new[]
            {
                new Vector2(0.66f, 0f), new Vector2(0.62f, 0.05f), new Vector2(0.44f, 0.06f), new Vector2(0.4f, 0.13f),
                new Vector2(0.34f, 0.16f), new Vector2(0f, 0.16f)
            }, 32, false, Matrix4x4.identity);
            return b.ToMesh("SU_SasPedestal");
        }

        static Mesh BuildColumn()
        {
            var b = new DefensePlatformKit.Builder();
            b.Lathe(new[]
            {
                new Vector2(0.22f, 0f), new Vector2(0.2f, 0.04f), new Vector2(0.13f, 0.2f), new Vector2(0.11f, 0.62f),
                new Vector2(0.15f, 0.9f), new Vector2(0.2f, 1.0f), new Vector2(0f, 1.0f)
            }, 24, false, Matrix4x4.identity);
            return b.ToMesh("SU_SasColumn");
        }

        static Mesh BuildDais()
        {
            var b = new DefensePlatformKit.Builder();
            b.Lathe(new[]
            {
                new Vector2(1.84f, 0f), new Vector2(1.8f, 0.04f), new Vector2(1.72f, 0.06f), new Vector2(0f, 0.06f)
            }, 64, false, Matrix4x4.identity);
            return b.ToMesh("SU_SasDais");
        }
    }
}
