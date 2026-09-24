using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Crew
{
    /// <summary>
    /// Crew intercom strip: a slim holo bar over the far rim of the holo table, facing the captain
    /// (~2.3 m away, just above the map volume, under eye line — between the captain and the front
    /// officers, never across the viewscreen). Shows who speaks (station colour chip +
    /// station name) and the line with a typewriter reveal, then fades. World-fixed, never head-locked.
    /// </summary>
    public sealed class CrewSubtitle : MonoBehaviour
    {
        const float CharsPerSecond = 48f;
        const float Fade = 0.25f;

        GameObject _screen;
        CanvasGroup _group;
        Image _chip;
        TMP_Text _speaker;
        TMP_Text _line;
        string _full = string.Empty;
        float _start;
        float _hold;
        bool _showing;

        public bool IsShowing => _showing;

        public static CrewSubtitle Build(Transform room)
        {
            // Driver lives beside the screen so the whole strip (housing included) can switch off when silent.
            var rig = new GameObject("CrewIntercomRig").transform;
            rig.SetParent(room, false);
            var screen = HoloScreen.Create(rig, "CrewIntercom", new Vector2(1.15f, 0.17f),
                new Vector3(0f, 1.4f, WorldScale.CicTableCenterZ + 0.9f), Quaternion.identity);
            ScreenMount.FaceViewer(screen.transform,
                room.TransformPoint(WorldScale.CicCaptainStand + Vector3.up * WorldScale.EyeStanding), 1f);
            screen.SetAccent(UiKit.Cyan, 0.4f);

            var view = rig.gameObject.AddComponent<CrewSubtitle>();
            view._screen = screen.gameObject;
            view._group = screen.Canvas.gameObject.AddComponent<CanvasGroup>();
            view._group.alpha = 0f;
            view._group.interactable = false;
            view._group.blocksRaycasts = false;

            var px = screen.PixelSize;
            var chipGo = new GameObject("SpeakerChip", typeof(RectTransform), typeof(Image));
            chipGo.transform.SetParent(screen.Content, false);
            var chipRt = chipGo.GetComponent<RectTransform>();
            chipRt.sizeDelta = new Vector2(18f, px.y * 0.62f);
            chipRt.anchoredPosition = new Vector2(-px.x * 0.5f + 34f, 0f);
            view._chip = chipGo.GetComponent<Image>();
            view._chip.sprite = DiegeticUi.SprBtn;
            view._chip.type = Image.Type.Sliced;
            view._chip.raycastTarget = false;

            view._speaker = DiegeticUi.HoloLabel(screen.Content, string.Empty,
                new Vector2(-px.x * 0.5f + 150f, 0f), new Vector2(200f, px.y * 0.7f), 30f, UiKit.Cyan,
                TextAlignmentOptions.MidlineLeft);
            view._speaker.textWrappingMode = TextWrappingModes.NoWrap;
            view._speaker.enableAutoSizing = true;
            view._speaker.fontSizeMin = 18f;
            view._speaker.fontSizeMax = 30f;
            view._line = DiegeticUi.HoloLabel(screen.Content, string.Empty,
                new Vector2(110f, 0f), new Vector2(px.x - 300f, px.y * 0.8f), 44f, UiKit.TextBright,
                TextAlignmentOptions.MidlineLeft);
            view._line.textWrappingMode = TextWrappingModes.Normal;
            view._line.enableAutoSizing = true;
            view._line.fontSizeMin = 26f;
            view._line.fontSizeMax = 44f;
            view.enabled = false;
            screen.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Show a line; returns how long it stays on screen (reveal + read time).</summary>
        public float Show(string speaker, Color accent, string line)
        {
            _full = line ?? string.Empty;
            _speaker.text = speaker ?? string.Empty;
            _speaker.color = accent;
            _chip.color = accent;
            _line.text = _full;
            _line.maxVisibleCharacters = 0;
            _start = Time.unscaledTime;
            _hold = _full.Length / CharsPerSecond + 1.6f + _full.Length * 0.035f;
            _showing = true;
            _screen.SetActive(true);
            enabled = true;
            return _hold + Fade;
        }

        void Update()
        {
            var t = Time.unscaledTime - _start;
            _line.maxVisibleCharacters = Mathf.Min(_full.Length, Mathf.FloorToInt(t * CharsPerSecond));
            float alpha;
            if (t < Fade)
                alpha = t / Fade;
            else if (t < _hold)
                alpha = 1f;
            else
                alpha = 1f - (t - _hold) / Fade;
            _group.alpha = Mathf.Clamp01(alpha);
            if (t >= _hold + Fade)
            {
                _group.alpha = 0f;
                _showing = false;
                _screen.SetActive(false);
                enabled = false;
            }
        }
    }
}
