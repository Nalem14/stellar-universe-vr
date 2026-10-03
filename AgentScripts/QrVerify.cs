using System.IO;
using Core.Utils;
using UnityEngine;

/// <summary>Ephemeral: writes the sas plaque's QR codes to Screenshots/ for an external decoder check.</summary>
public static class QrVerify
{
    public static string Run()
    {
        var dir = Path.GetFullPath("Screenshots");
        Directory.CreateDirectory(dir);
        var report = "";
        foreach (var (name, url) in new[] { ("site", Core.UI.CommunityPlaque.WebsiteUrl), ("discord", Core.UI.CommunityPlaque.DiscordUrl) })
        {
            var qr = QrCode.Encode(url);
            var tex = qr.ToTexture(Color.black, Color.white, 10);
            var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readable.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, "qr-" + name + ".png"), readable.EncodeToPNG());
            report += name + " v" + qr.Version + " size " + qr.Size + "; ";
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(readable);
        }

        return report;
    }
}
