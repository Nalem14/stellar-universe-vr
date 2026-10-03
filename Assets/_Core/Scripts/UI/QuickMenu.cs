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
    /// opens in front of the player — master volume; comfort (smooth or teleport moving, smooth or snap turning,
    /// the tunnelling vignette, seated play — see <see cref="ComfortSettings"/>); glow (bloom); replay the guide;
    /// go on watch (when worthwhile); back to the chair. Settings are kept on the headset and applied at start.
    /// Press the button again (or Close) to put it away. On the Watch layer: it works on watch as well.
    /// </summary>
    public sealed class QuickMenu : MonoBehaviour
    {
        const string VolumeKey = "su.volume";
        static readonly Vector2 Px = new(500f, 470f);
        static readonly Vector2 Cell = new(226f, 54f);
        const float ColX = 118f;

        InputAction _menu;
        GameObject _panel;
        TMP_Text _volume;
        TMP_Text _move;
        TMP_Text _turn;
        TMP_Text _vignette;
        TMP_Text _seated;
        TMP_Text _glow;
        Button _watch;
        int _layer;

        public static QuickMenu Build(Transform room, int layer)
        {
            var go = new GameObject("QuickMenu");
            go.transform.SetParent(room, false);
            var m = go.AddComponent<QuickMenu>();
            m._layer = layer;
            m.BuildPanel();
            AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
            m._menu = new InputAction("QuickMenu", InputActionType.Button);
            m._menu.AddBinding("<XRController>{LeftHand}/menu");
            m._menu.AddBinding("<XRController>{LeftHand}/menuButton");
            m._menu.AddBinding("<Keyboard>/escape");
            m._menu.AddBinding("<Keyboard>/m");
            // Esc / M are keys a typed text may contain: never while a field has the keyboard (PC, phone).
            m._menu.performed += _ =>
            {
                if (PcPlatformBoot.IsFlatScreen && (PcPlatformBoot.IsTyping || FlatGrab.Holding))
                    return;
                // Seated at the command post on PC, Esc stands up and M flips the table (CaptainCommandMode).
                if (PcPlatformBoot.IsPcDesktop && CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode)
                    return;
                m.Toggle();
            };
            m._menu.Enable();
            return m;
        }

        void OnDestroy()
        {
            _menu?.Disable();
            _menu?.Dispose();
        }

        void Toggle()
        {
            if (_panel.activeSelf)
            {
                _panel.SetActive(false);
                CicCue.Ok(_panel.transform.position);
                if (PcPlatformBoot.IsPcDesktop && !(CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode))
                    PcDesktopController.Instance?.SetCursorLock(true);
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
            var at = cam.transform.position + f * 0.62f + Vector3.down * 0.1f;
            _panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
            _panel.SetActive(true);
            if (PcPlatformBoot.IsPcDesktop)
                PcDesktopController.Instance?.SetCursorLock(false);
            Refresh();
            CicCue.Ok(at);
        }

        static string OnOff(bool on) => Trans.Get(on ? "vr.fx.on" : "vr.fx.off");

        void Refresh()
        {
            _volume.text = Trans.Get("vr.menu.volume") + "  " + Mathf.RoundToInt(AudioListener.volume * 100f) + " %";
            if (_move != null)
                _move.text = Trans.Get("vr.menu.move") + " · " + Trans.Get(ComfortSettings.Teleport ? "vr.menu.moveTeleport" : "vr.menu.moveSmooth");
            if (_turn != null)
                _turn.text = Trans.Get("vr.menu.turn") + " · " + Trans.Get(ComfortSettings.SmoothTurn ? "vr.menu.turnSmooth" : "vr.menu.turnSnap");
            if (_vignette != null)
                _vignette.text = Trans.Get("vr.menu.vignette") + " · " + OnOff(ComfortSettings.Vignette);
            if (_seated != null)
                _seated.text = Trans.Get("vr.menu.seated") + " · " + OnOff(ComfortSettings.Seated);
            var pc = PcDesktopController.Instance;
            if (_sens != null)
                _sens.text = Trans.Get("vr.menu.mouseSens") + "  " + (pc != null ? pc.MouseSensitivity : 2f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            if (_invert != null)
                _invert.text = Trans.Get("vr.menu.invertY") + " · " + OnOff(pc != null && pc.InvertY);
            _glow.text = Trans.Get("vr.fx.glow") + " · " + OnOff(CicEnvironment.GlowEnabled);
            _watch.interactable = WatchMode.Instance != null && WatchMode.Supported && WatchMode.Instance.Worthwhile && !WatchMode.Inside;
        }

        void Volume(float step)
        {
            var v = Mathf.Clamp01(Mathf.Round((AudioListener.volume + step) * 10f) / 10f);
            AudioListener.volume = v;
            PlayerPrefs.SetFloat(VolumeKey, v);
            Refresh();
        }

        TMP_Text Switch(Transform frame, float x, float y, System.Action flip)
        {
            var b = DiegeticUi.HoloButton(frame, string.Empty, new Vector2(x, y), Cell, () =>
            {
                flip();
                Refresh();
            }, DiegeticUi.BtnStyle.Cyan);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.enableAutoSizing = true;
            t.fontSizeMin = 13f;
            t.fontSizeMax = 19f;
            return t;
        }

        TMP_Text _sens;
        TMP_Text _invert;

        void Sensitivity(float step)
        {
            var pc = PcDesktopController.Instance;
            if (pc != null)
                pc.SaveSettings(Mathf.Clamp(pc.MouseSensitivity + step, 0.25f, 6f), pc.InvertY);
            Refresh();
        }

        /// <summary>Close the panel (on PC the crosshair comes back, unless seated where the cursor stays free).</summary>
        void Hide()
        {
            _panel.SetActive(false);
            if (PcPlatformBoot.IsPcDesktop && !(CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode))
                PcDesktopController.Instance?.SetCursorLock(true);
        }

        void BuildPanel()
        {
            _panel = new GameObject("QuickMenuPanel");
            _panel.transform.SetParent(transform, false);
            var canvas = DiegeticUi.WorldCanvas(_panel.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, 0.001f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px, Trans.Get("vr.menu.title"));
            frame.GetComponent<Image>().raycastTarget = false;

            // Volume: − value +.
            DiegeticUi.HoloButton(frame, "−", new Vector2(-180f, 140f), new Vector2(64f, 50f), () => Volume(-0.1f), DiegeticUi.BtnStyle.Ghost);
            _volume = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(0f, 140f), new Vector2(280f, 44f), 24f, UiKit.TextBright);
            DiegeticUi.HoloButton(frame, "+", new Vector2(180f, 140f), new Vector2(64f, 50f), () => Volume(0.1f), DiegeticUi.BtnStyle.Ghost);

            if (PcPlatformBoot.IsVr)
            {
                // Comfort: moving, turning, the vignette, seated play (a headset's).
                _move = Switch(frame, -ColX, 78f, () => ComfortSettings.Teleport = !ComfortSettings.Teleport);
                _turn = Switch(frame, ColX, 78f, () => ComfortSettings.SmoothTurn = !ComfortSettings.SmoothTurn);
                _vignette = Switch(frame, -ColX, 16f, () => ComfortSettings.Vignette = !ComfortSettings.Vignette);
                _seated = Switch(frame, ColX, 16f, () => ComfortSettings.Seated = !ComfortSettings.Seated);
            }
            else if (PcPlatformBoot.IsDesktop)
            {
                // PC: the mouse's feel.
                DiegeticUi.HoloButton(frame, "−", new Vector2(-ColX - 150f, 78f), new Vector2(54f, 50f), () => Sensitivity(-0.25f), DiegeticUi.BtnStyle.Ghost);
                _sens = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(-ColX, 78f), new Vector2(230f, 44f), 18f, UiKit.TextBright);
                DiegeticUi.HoloButton(frame, "+", new Vector2(-ColX + 150f, 78f), new Vector2(54f, 50f), () => Sensitivity(0.25f), DiegeticUi.BtnStyle.Ghost);
                _invert = Switch(frame, ColX, 78f, () =>
                {
                    var pc = PcDesktopController.Instance;
                    if (pc != null)
                        pc.SaveSettings(pc.MouseSensitivity, !pc.InvertY);
                });
            }

            _glow = Switch(frame, -ColX, -46f, () => CicEnvironment.ToggleGlow());
            DiegeticUi.HoloButton(frame, Trans.Get("guide"), new Vector2(ColX, -46f), Cell, () =>
            {
                Hide();
                Core.Crew.TutorialGuide.Instance?.Restart();
            }, DiegeticUi.BtnStyle.Cyan);

            _watch = DiegeticUi.HoloButton(frame, Trans.Get("vr.watch.enter"), new Vector2(-ColX, -108f), Cell, () =>
            {
                Hide();
                WatchMode.Instance?.Enter();
            }, DiegeticUi.BtnStyle.Amber);
            // The watch (passthrough) is a headset's.
            _watch.gameObject.SetActive(PcPlatformBoot.IsVr);
            DiegeticUi.HoloButton(frame, Trans.Get("vr.menu.recenter"), new Vector2(ColX, -108f), Cell, () =>
            {
                Hide();
                if (!Core.Stations.DiplomacyRoom.AnyRoomInside)
                    FindFirstObjectByType<BridgeViewRig>()?.PutPlayerOnDeck();
            }, DiegeticUi.BtnStyle.Ghost);

            DiegeticUi.HoloButton(frame, Trans.Get("close"), new Vector2(0f, -176f), new Vector2(200f, 50f), Hide,
                DiegeticUi.BtnStyle.Ghost);

            foreach (var t in _panel.GetComponentsInChildren<Transform>(true))
                if (_layer >= 0)
                    t.gameObject.layer = _layer;
            _panel.SetActive(false);
        }
    }
}
