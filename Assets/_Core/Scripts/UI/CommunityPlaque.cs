using Core.Stations;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI
{
    /// <summary>
    /// The sas's community plaque, right of the terminal (mirror of the transmissions board): the game's site and
    /// its Discord, each with its QR code to scan from a phone and a key that opens it in the headset's browser.
    /// Community links only — no feature of the game is ever sent to the web (AGENTS.md). The codes are encoded
    /// here (<see cref="QrCode"/>), drawn dark on a pale holo card; labels through <see cref="Trans"/>.
    /// </summary>
    public sealed class CommunityPlaque : MonoBehaviour
    {
        public const string WebsiteUrl = "https://www.stellar-universe.com";
        public const string DiscordUrl = "https://discord.gg/KFrJKQMEGH";
        static readonly Vector2 Size = new(1.1f, 0.66f);
        static readonly Color Discord = new(0.55f, 0.6f, 1f, 1f);

        HoloScreen _screen;
        readonly System.Collections.Generic.List<Texture2D> _codes = new();

        public static CommunityPlaque Build(Transform room, CicArtKit art)
        {
            var mount = SasTerminal.Lectern(room, art, new Vector3(1.3f, 0f, 0.72f), 0f, 1.42f);
            var plaque = mount.gameObject.AddComponent<CommunityPlaque>();
            plaque._screen = HoloScreen.Create(mount, "CommunityPlaque", Size, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.menu.community"));
            plaque._screen.SetAccent(UiKit.Cyan, 0.45f);
            var eye = room.TransformPoint(new Vector3(0f, WorldScale.EyeStanding, 0f));
            ScreenMount.FaceViewer(mount.parent, new Vector3(eye.x, mount.parent.position.y, eye.z), 0f);
            ScreenMount.FaceViewer(plaque._screen.transform, eye, 1f, 10f);
            var body = ScreenKit.Body(plaque._screen.Content);
            plaque.Entry(body, -270f, Trans.Get("vr.menu.website"), "stellar-universe.com", WebsiteUrl, UiKit.Cyan);
            plaque.Entry(body, 270f, Trans.Get("vr.menu.discord"), "discord.gg/KFrJKQMEGH", DiscordUrl, Discord);
            return plaque;
        }

        void Entry(RectTransform body, float x, string title, string address, string url, Color accent)
        {
            ScreenKit.Line(body, "<b>" + title + "</b>", x, 205f, 26f, accent, 480f, TextAlignmentOptions.Center);

            // The code on a pale card (a scanner wants dark on light), framed in the accent.
            var frame = new GameObject("QrFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(body, false);
            var frt = frame.GetComponent<RectTransform>();
            frt.sizeDelta = new Vector2(292f, 292f);
            frt.anchoredPosition = new Vector2(x, 20f);
            var fimg = frame.GetComponent<Image>();
            fimg.color = accent * 0.85f;
            fimg.raycastTarget = false;

            var code = QrCode.Encode(url).ToTexture(new Color(0.03f, 0.05f, 0.08f), new Color(0.93f, 0.97f, 1f), 6);
            _codes.Add(code);
            var qr = new GameObject("Qr", typeof(RectTransform), typeof(RawImage));
            qr.transform.SetParent(frame.transform, false);
            var qrt = qr.GetComponent<RectTransform>();
            qrt.sizeDelta = new Vector2(276f, 276f);
            var raw = qr.GetComponent<RawImage>();
            raw.texture = code;
            raw.raycastTarget = false;

            ScreenKit.Line(body, address, x, -150f, 17f, UiKit.TextDim, 480f, TextAlignmentOptions.Center);
            ScreenKit.Btn(body, Trans.Get("vr.menu.open"), x, -210f, 300f, 54f, () => Open(url),
                DiegeticUi.BtnStyle.Cyan);
        }

        void Open(string url)
        {
            CicCue.Ok(transform.position);
            // The headset's own browser (Quest) takes it; the QR beside it is for a phone.
            Application.OpenURL(url);
        }

        void OnDestroy()
        {
            foreach (var t in _codes)
                if (t != null)
                    Destroy(t);
        }
    }
}
