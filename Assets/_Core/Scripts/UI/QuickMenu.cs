using Core.App;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// The captain's options, on the left controller's menu button (M in the Editor): a small holo panel that
    /// opens in front of the player — master volume, glow (bloom), smooth or snap turning, replay the guide,
    /// go on watch (when worthwhile), back to the chair. Settings are kept on the headset and applied at start.
    /// Press the button again (or Close) to put it away. On the Watch layer: it works on watch as well.
    /// </summary>
    public sealed class QuickMenu : MonoBehaviour
    {
        const string VolumeKey = "su.volume";
        const string SmoothKey = "su.turn.smooth";
        static readonly Vector2 Px = new(460f, 330f);

        InputAction _menu;
        GameObject _panel;
        TMP_Text _volume;
        TMP_Text _glow;
        TMP_Text _turn;
        Button _watch;
        int _layer;

        public static QuickMenu Build(Transform room, int layer)
        {
            var go = new GameObject("QuickMenu");
            go.transform.SetParent(room, false);
            var m = go.AddComponent<QuickMenu>();
            m._layer = layer;
            m.BuildPanel();
            ApplySaved();
            m._menu = new InputAction("QuickMenu", InputActionType.Button);
            m._menu.AddBinding("<XRController>{LeftHand}/menu");
            m._menu.AddBinding("<XRController>{LeftHand}/menuButton");
#if UNITY_EDITOR
            m._menu.AddBinding("<Keyboard>/m");
#endif
            m._menu.performed += _ => m.Toggle();
            m._menu.Enable();
            return m;
        }

        void OnDestroy()
        {
            _menu?.Disable();
            _menu?.Dispose();
        }

        /// <summary>Volume and turning as the player left them.</summary>
        static void ApplySaved()
        {
            AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
            SetSmoothTurn(PlayerPrefs.GetInt(SmoothKey, 0) == 1);
        }

        static void SetSmoothTurn(bool smooth)
        {
            foreach (var mgr in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager>(FindObjectsSortMode.None))
                mgr.smoothTurnEnabled = smooth;
        }

        void Toggle()
        {
            if (_panel.activeSelf)
            {
                _panel.SetActive(false);
                CicCue.Ok(_panel.transform.position);
                return;
            }

            var cam = Camera.main;
            if (cam == null)
                return;
            // In front of the eyes, a little low, facing them; level (no roll).
            var f = cam.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f)
                f = Vector3.forward;
            f.Normalize();
            var at = cam.transform.position + f * 0.6f + Vector3.down * 0.12f;
            _panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
            _panel.SetActive(true);
            Refresh();
            CicCue.Ok(at);
        }

        void Refresh()
        {
            _volume.text = Trans.Get("vr.menu.volume") + "  " + Mathf.RoundToInt(AudioListener.volume * 100f) + " %";
            _glow.text = Trans.Get("vr.fx.glow") + " · " + Trans.Get(CicEnvironment.GlowEnabled ? "vr.fx.on" : "vr.fx.off");
            var smooth = PlayerPrefs.GetInt(SmoothKey, 0) == 1;
            _turn.text = Trans.Get("vr.menu.turn") + " · " + Trans.Get(smooth ? "vr.menu.turnSmooth" : "vr.menu.turnSnap");
            _watch.interactable = WatchMode.Instance != null && WatchMode.Supported && WatchMode.Instance.Worthwhile && !WatchMode.Inside;
        }

        void Volume(float step)
        {
            var v = Mathf.Clamp01(Mathf.Round((AudioListener.volume + step) * 10f) / 10f);
            AudioListener.volume = v;
            PlayerPrefs.SetFloat(VolumeKey, v);
            Refresh();
        }

        void BuildPanel()
        {
            _panel = new GameObject("QuickMenuPanel");
            _panel.transform.SetParent(transform, false);
            var canvas = DiegeticUi.WorldCanvas(_panel.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, 0.001f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px, Trans.Get("vr.menu.title"));
            frame.GetComponent<Image>().raycastTarget = false;

            // Volume: − value +.
            DiegeticUi.HoloButton(frame, "−", new Vector2(-170f, 90f), new Vector2(64f, 52f), () => Volume(-0.1f), DiegeticUi.BtnStyle.Ghost);
            _volume = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(0f, 90f), new Vector2(260f, 44f), 24f, UiKit.TextBright);
            DiegeticUi.HoloButton(frame, "+", new Vector2(170f, 90f), new Vector2(64f, 52f), () => Volume(0.1f), DiegeticUi.BtnStyle.Ghost);

            var glow = DiegeticUi.HoloButton(frame, string.Empty, new Vector2(-110f, 26f), new Vector2(200f, 52f), () =>
            {
                CicEnvironment.ToggleGlow();
                Refresh();
            }, DiegeticUi.BtnStyle.Cyan);
            _glow = glow.GetComponentInChildren<TMP_Text>();
            var turn = DiegeticUi.HoloButton(frame, string.Empty, new Vector2(110f, 26f), new Vector2(200f, 52f), () =>
            {
                var smooth = PlayerPrefs.GetInt(SmoothKey, 0) != 1;
                PlayerPrefs.SetInt(SmoothKey, smooth ? 1 : 0);
                SetSmoothTurn(smooth);
                Refresh();
            }, DiegeticUi.BtnStyle.Cyan);
            _turn = turn.GetComponentInChildren<TMP_Text>();

            DiegeticUi.HoloButton(frame, Trans.Get("guide"), new Vector2(-110f, -38f), new Vector2(200f, 52f), () =>
            {
                _panel.SetActive(false);
                Core.Crew.TutorialGuide.Instance?.Restart();
            }, DiegeticUi.BtnStyle.Cyan);
            _watch = DiegeticUi.HoloButton(frame, Trans.Get("vr.watch.enter"), new Vector2(110f, -38f), new Vector2(200f, 52f), () =>
            {
                _panel.SetActive(false);
                WatchMode.Instance?.Enter();
            }, DiegeticUi.BtnStyle.Amber);

            DiegeticUi.HoloButton(frame, Trans.Get("vr.menu.recenter"), new Vector2(-110f, -102f), new Vector2(200f, 52f), () =>
            {
                _panel.SetActive(false);
                if (!Core.Stations.DiplomacyRoom.AnyRoomInside)
                    FindFirstObjectByType<BridgeViewRig>()?.PutPlayerOnDeck();
            }, DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(frame, Trans.Get("close"), new Vector2(110f, -102f), new Vector2(200f, 52f), () => _panel.SetActive(false),
                DiegeticUi.BtnStyle.Ghost);

            foreach (var t in _panel.GetComponentsInChildren<Transform>(true))
                if (_layer >= 0)
                    t.gameObject.layer = _layer;
            _panel.SetActive(false);
        }
    }
}
