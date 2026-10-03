using Core.App;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// The commander's condition selector: a small pedestal by the captain's chair (at a station, beside the
    /// podium — <see cref="StationCommandLayout"/>) with four keys — Auto (the ship judges for itself), stand
    /// down, yellow alert, red alert. The selected key stays lit in its colour; a lamp and a plate show the
    /// condition standing now. Local −Z faces the commander, like every kit piece.
    /// </summary>
    public sealed class AlertConditionPanel : MonoBehaviour
    {
        const float HeadHeight = 0.9f;
        const float Tilt = 50f;
        static readonly Vector2 Key = new(0.105f, 0.045f);
        static readonly Color Dim = new(0.32f, 0.42f, 0.5f, 1f);
        static readonly Color Green = new(0.35f, 1f, 0.6f, 1f);

        readonly PokeButton[] _keys = new PokeButton[4];
        readonly Color[] _colors = new Color[4];
        TextMeshPro _plate;
        MeshRenderer _lamp;
        MaterialPropertyBlock _block;
        AlertDirector.Mode _shownMode = (AlertDirector.Mode)(-1);
        AlertLevel _shownLevel = (AlertLevel)(-1);

        /// <summary>Build the selector at <paramref name="localPos"/> under <paramref name="room"/>, turned to face <paramref name="eye"/> (room space).</summary>
        public static AlertConditionPanel Build(Transform room, Vector3 localPos, Vector3 eye, CicArtKit art)
        {
            var root = new GameObject("ConditionPanel");
            root.transform.SetParent(room, false);
            var p = root.AddComponent<AlertConditionPanel>();
            p.Make(art);
            p.Place(localPos, eye);
            return p;
        }

        /// <summary>Stand at <paramref name="localPos"/> (room space, on the floor), keys toward <paramref name="eye"/>.</summary>
        public void Place(Vector3 localPos, Vector3 eye)
        {
            var away = localPos - eye;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
                away = Vector3.forward;
            transform.SetLocalPositionAndRotation(localPos, Quaternion.LookRotation(away.normalized, Vector3.up));
        }

        void Make(CicArtKit art)
        {
            var chassis = UiKit.Chassis;
            Piece(transform, "Foot", new Vector3(0.24f, 0.03f, 0.2f), 0.012f, new Vector3(0f, 0.015f, 0f), CicArtKit.Amber, 0.35f);
            Piece(transform, "Column", new Vector3(0.08f, HeadHeight - 0.06f, 0.08f), 0.03f, new Vector3(0f, HeadHeight * 0.5f, 0.02f),
                CicArtKit.Amber, 0.12f);

            var head = new GameObject("Head").transform;
            head.SetParent(transform, false);
            head.localPosition = new Vector3(0f, HeadHeight, 0f);
            head.localRotation = Quaternion.Euler(Tilt, 0f, 0f);
            Piece(head, "Housing", new Vector3(0.27f, 0.2f, 0.035f), 0.012f, new Vector3(0f, 0f, 0.018f), CicArtKit.Amber, 0.5f);
            // A lit hazard lip along the near edge: this is the ship's alarm, not a screen setting.
            var lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lip.name = "Lip";
            Destroy(lip.GetComponent<Collider>());
            lip.transform.SetParent(head, false);
            lip.transform.localPosition = new Vector3(0f, -0.1f, -0.002f);
            lip.transform.localScale = new Vector3(0.25f, 0.006f, 0.006f);
            lip.GetComponent<MeshRenderer>().sharedMaterial = art.AmberEmit(2f);

            // Plate: the condition now, with its lamp.
            _plate = UiKit.Label(head, "Plate", string.Empty, new Vector3(0.015f, 0.072f, -0.002f), 0.2f, 0.022f, UiKit.TextBright);
            _plate.alignment = TextAlignmentOptions.MidlineLeft;
            _plate.richText = true;
            var lamp = UiKit.MeshPiece(head, "Lamp", UiMeshes.RoundedBox(new Vector3(0.018f, 0.018f, 0.01f), 0.008f), chassis,
                new Vector3(-0.112f, 0.072f, -0.003f));
            _lamp = lamp.GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();

            var modes = new[] { AlertDirector.Mode.Auto, AlertDirector.Mode.StandDown, AlertDirector.Mode.Yellow, AlertDirector.Mode.Red };
            var labels = new[] { "vr.alert.auto", "vr.alert.standDown", "vr.console.amberAlert", "vr.screen.redAlert" };
            _colors[0] = CicArtKit.Cyan;
            _colors[1] = Green;
            _colors[2] = AlertDirector.AmberTint;
            _colors[3] = AlertDirector.RedTint;
            for (var i = 0; i < 4; i++)
            {
                var mode = modes[i];
                var pos = new Vector3((i % 2 == 0 ? -1f : 1f) * 0.063f, i < 2 ? 0.012f : -0.048f, -0.004f);
                _keys[i] = PokeButton.Create(head, "Condition" + mode, Trans.Get(labels[i]), pos, Quaternion.identity, Key, Dim,
                    () => Select(mode));
            }
        }

        static void Piece(Transform parent, string name, Vector3 size, float radius, Vector3 pos, Color accent, float mul)
        {
            var go = UiKit.MeshPiece(parent, name, UiMeshes.RoundedBox(size, radius), UiKit.Chassis, pos);
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, mul);
            var mr = go.GetComponent<MeshRenderer>();
            mr.SetPropertyBlock(block);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Select(AlertDirector.Mode mode)
        {
            AlertDirector.Condition = mode;
        }

        void Update()
        {
            var mode = AlertDirector.Condition;
            var level = AlertState.Level;
            if (mode == _shownMode && level == _shownLevel)
                return;
            _shownMode = mode;
            _shownLevel = level;
            for (var i = 0; i < 4; i++)
                _keys[i].SetAccent((int)mode == i ? _colors[i] : Dim);

            var levelKey = level == AlertLevel.Red ? "vr.screen.redAlert" : level == AlertLevel.Amber ? "vr.console.amberAlert" : "vr.alert.normal";
            _plate.text = Trans.Get(levelKey) + "  <size=70%><color=#9fb4c4>" +
                          Trans.Get(mode == AlertDirector.Mode.Auto ? "vr.alert.auto" : "vr.alert.manual") + "</color></size>";
            var lampColor = level == AlertLevel.Red ? AlertDirector.RedTint : level == AlertLevel.Amber ? AlertDirector.AmberTint : Green;
            _block.SetColor(UiKit.AccentId, lampColor);
            _block.SetFloat(UiKit.AccentMulId, 2.6f);
            _lamp.SetPropertyBlock(_block);
        }
    }
}
