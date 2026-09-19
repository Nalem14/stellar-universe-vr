using System.IO;
using UnityEditor;
using UnityEngine;

namespace Core.Editor
{
    /// <summary>
    /// Imports CIC/Ui holographic sprites as 9-slice ready Sprite assets.
    /// Menu: StellarUniverse/UI/Import Holo UI Sprites
    /// </summary>
    public static class HoloUiSpriteImporter
    {
        const string Root = "Assets/_Core/Resources/CIC/Ui";

        [MenuItem("StellarUniverse/UI/Import Holo UI Sprites")]
        public static void ImportAll()
        {
            if (!Directory.Exists(Root))
            {
                Debug.LogWarning("[SU] Missing " + Root);
                return;
            }

            // Panel / dark: large border for 9-slice
            Configure(Root + "/Panel.png", 48);
            Configure(Root + "/PanelDark.png", 48);
            Configure(Root + "/Header.png", 16, 12, 16, 12);
            Configure(Root + "/BtnCyan.png", 24, 20, 24, 20);
            Configure(Root + "/BtnCyanHover.png", 24, 20, 24, 20);
            Configure(Root + "/BtnCyanPressed.png", 24, 20, 24, 20);
            Configure(Root + "/BtnCyanDisabled.png", 24, 20, 24, 20);
            Configure(Root + "/BtnAmber.png", 24, 20, 24, 20);
            Configure(Root + "/BtnAmberHover.png", 24, 20, 24, 20);
            Configure(Root + "/BtnAmberPressed.png", 24, 20, 24, 20);
            Configure(Root + "/BtnDanger.png", 24, 20, 24, 20);
            Configure(Root + "/BtnDangerHover.png", 24, 20, 24, 20);
            Configure(Root + "/BtnGhost.png", 24, 20, 24, 20);
            Configure(Root + "/BtnGhostHover.png", 24, 20, 24, 20);
            Configure(Root + "/TabIdle.png", 16, 12, 16, 12);
            Configure(Root + "/TabActive.png", 16, 12, 16, 12);
            Configure(Root + "/Field.png", 16, 12, 16, 12);
            Configure(Root + "/Readout.png", 12, 10, 12, 10);
            Configure(Root + "/Divider.png", 2, 2, 2, 2);
            Configure(Root + "/Dot.png", 0);
            Configure(Root + "/DotEmpty.png", 0);
            Configure(Root + "/RingButton.png", 0);
            Configure(Root + "/GlowSoft.png", 0);
            Configure(Root + "/CheckOff.png", 8);
            Configure(Root + "/CheckOn.png", 8);

            // Legacy CIC root mirrors
            Configure("Assets/_Core/Resources/CIC/HoloPanel.png", 48);
            Configure("Assets/_Core/Resources/CIC/HoloBtn.png", 24, 20, 24, 20);
            Configure("Assets/_Core/Resources/CIC/HoloBtnHover.png", 24, 20, 24, 20);
            Configure("Assets/_Core/Resources/CIC/HoloBtnAmber.png", 24, 20, 24, 20);
            Configure("Assets/_Core/Resources/CIC/HoloHeader.png", 16, 12, 16, 12);

            AssetDatabase.Refresh();
            Debug.Log("[SU] Holo UI sprites imported (9-slice).");
        }

        static void Configure(string path, int border)
        {
            Configure(path, border, border, border, border);
        }

        static void Configure(string path, int left, int bottom, int right, int top)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("[SU] Missing " + path);
                return;
            }

            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(left, bottom, right, top);
            var android = new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 512,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 50
            };
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }
    }
}
