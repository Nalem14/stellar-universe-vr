using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Core.Vfx
{
    /// <summary>
    /// Bridge Crew–style diegetic World Space UI: Canvas + textured holo glass buttons.
    /// Mesh Plate/Button remain for pokeables that are not canvas-backed.
    /// </summary>
    public static class DiegeticUi
    {
        public static readonly Color Cyan = new(0.45f, 0.95f, 1f, 1f);
        public static readonly Color CyanDim = new(0.25f, 0.7f, 0.85f, 1f);
        public static readonly Color Amber = new(1f, 0.72f, 0.35f, 1f);

        static Sprite s_Panel;
        static Sprite s_PanelDark;
        static Sprite s_Btn;
        static Sprite s_BtnHover;
        static Sprite s_BtnPressed;
        static Sprite s_BtnDisabled;
        static Sprite s_BtnAmber;
        static Sprite s_BtnAmberHover;
        static Sprite s_BtnAmberPressed;
        static Sprite s_BtnDanger;
        static Sprite s_BtnDangerHover;
        static Sprite s_BtnGhost;
        static Sprite s_BtnGhostHover;
        static Sprite s_Header;
        static Sprite s_TabIdle;
        static Sprite s_TabActive;
        static Sprite s_Field;
        static Sprite s_Readout;
        static Sprite s_Divider;
        static Sprite s_Dot;
        static Sprite s_Ring;
        static bool s_SpritesReady;

        public static Sprite SprPanel { get { EnsureSprites(); return s_Panel; } }
        public static Sprite SprBtn { get { EnsureSprites(); return s_Btn; } }
        public static Sprite SprBtnHover { get { EnsureSprites(); return s_BtnHover; } }
        public static Sprite SprBtnAmber { get { EnsureSprites(); return s_BtnAmber; } }
        public static Sprite SprHeader { get { EnsureSprites(); return s_Header; } }
        public static Sprite SprField { get { EnsureSprites(); return s_Field; } }
        public static Sprite SprReadout { get { EnsureSprites(); return s_Readout; } }
        public static Sprite SprTabIdle { get { EnsureSprites(); return s_TabIdle; } }
        public static Sprite SprTabActive { get { EnsureSprites(); return s_TabActive; } }
        public static Sprite SprDanger { get { EnsureSprites(); return s_BtnDanger; } }
        public static Sprite SprGhost { get { EnsureSprites(); return s_BtnGhost; } }

        static void EnsureSprites()
        {
            if (s_SpritesReady)
                return;
            s_Panel = LoadSprite("CIC/Ui/Panel") ?? LoadSprite("CIC/HoloPanel");
            s_PanelDark = LoadSprite("CIC/Ui/PanelDark") ?? s_Panel;
            s_Btn = LoadSprite("CIC/Ui/BtnCyan") ?? LoadSprite("CIC/HoloBtn");
            s_BtnHover = LoadSprite("CIC/Ui/BtnCyanHover") ?? LoadSprite("CIC/HoloBtnHover");
            s_BtnPressed = LoadSprite("CIC/Ui/BtnCyanPressed") ?? s_BtnHover;
            s_BtnDisabled = LoadSprite("CIC/Ui/BtnCyanDisabled") ?? s_Btn;
            s_BtnAmber = LoadSprite("CIC/Ui/BtnAmber") ?? LoadSprite("CIC/HoloBtnAmber");
            s_BtnAmberHover = LoadSprite("CIC/Ui/BtnAmberHover") ?? s_BtnAmber;
            s_BtnAmberPressed = LoadSprite("CIC/Ui/BtnAmberPressed") ?? s_BtnAmber;
            s_BtnDanger = LoadSprite("CIC/Ui/BtnDanger") ?? s_Btn;
            s_BtnDangerHover = LoadSprite("CIC/Ui/BtnDangerHover") ?? s_BtnDanger;
            s_BtnGhost = LoadSprite("CIC/Ui/BtnGhost") ?? s_Btn;
            s_BtnGhostHover = LoadSprite("CIC/Ui/BtnGhostHover") ?? s_BtnGhost;
            s_Header = LoadSprite("CIC/Ui/Header") ?? LoadSprite("CIC/HoloHeader");
            s_TabIdle = LoadSprite("CIC/Ui/TabIdle") ?? s_Btn;
            s_TabActive = LoadSprite("CIC/Ui/TabActive") ?? s_BtnHover;
            s_Field = LoadSprite("CIC/Ui/Field") ?? s_PanelDark;
            s_Readout = LoadSprite("CIC/Ui/Readout") ?? s_Header;
            s_Divider = LoadSprite("CIC/Ui/Divider");
            s_Dot = LoadSprite("CIC/Ui/Dot");
            s_Ring = LoadSprite("CIC/Ui/RingButton");
            s_SpritesReady = true;
        }

        static Sprite LoadSprite(string resourcesPath)
        {
            // Prefer imported Sprite (9-slice borders from TextureImporter).
            var sprite = Resources.Load<Sprite>(resourcesPath);
            if (sprite != null)
                return sprite;
            var tex = Resources.Load<Texture2D>(resourcesPath);
            if (tex == null)
                return null;
            // Runtime fallback without borders.
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(24f, 20f, 24f, 20f));
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            Object.DontDestroyOnLoad(go);
        }

        /// <summary>
        /// World Space canvas in meters. scale = metersPerPixel (default 0.001 → 1000px = 1m).
        /// </summary>
        public static Canvas WorldCanvas(Transform parent, string name, Vector2 pixelSize,
            Vector3 localPos, Quaternion localRot, float metersPerPixel = 0.001f)
        {
            EnsureEventSystem();
            EnsureSprites();
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one * metersPerPixel;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = pixelSize;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2.5f;
            return canvas;
        }

        public static RectTransform HoloFrame(Transform canvasRoot, Vector2 size, string header = null)
        {
            EnsureSprites();
            var go = new GameObject("HoloFrame", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvasRoot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = s_Panel;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.55f, 0.95f, 1f, 0.92f);
            img.raycastTarget = false;

            if (!string.IsNullOrEmpty(header))
            {
                var bar = new GameObject("Header", typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(go.transform, false);
                var brt = bar.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0.04f, 0.88f);
                brt.anchorMax = new Vector2(0.96f, 0.98f);
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;
                var bimg = bar.GetComponent<Image>();
                bimg.sprite = s_Header != null ? s_Header : s_Btn;
                bimg.type = Image.Type.Sliced;
                bimg.color = Color.white;
                bimg.raycastTarget = false;

                var label = new GameObject("HeaderLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(bar.transform, false);
                Stretch(label.GetComponent<RectTransform>(), 8f);
                var tmp = label.GetComponent<TextMeshProUGUI>();
                tmp.text = header;
                tmp.fontSize = 22f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
            }

            return rt;
        }

        public enum BtnStyle
        {
            Cyan,
            Amber,
            Danger,
            Ghost
        }

        public static Button HoloButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size,
            UnityEngine.Events.UnityAction onClick, bool amber = false) =>
            HoloButton(parent, label, anchoredPos, size, onClick, amber ? BtnStyle.Amber : BtnStyle.Cyan);

        public static Button HoloButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size,
            UnityEngine.Events.UnityAction onClick, BtnStyle style)
        {
            EnsureSprites();
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            Sprite idle = s_Btn, hover = s_BtnHover, pressed = s_BtnPressed;
            switch (style)
            {
                case BtnStyle.Amber:
                    idle = s_BtnAmber ?? s_Btn;
                    hover = s_BtnAmberHover ?? idle;
                    pressed = s_BtnAmberPressed ?? hover;
                    break;
                case BtnStyle.Danger:
                    idle = s_BtnDanger ?? s_Btn;
                    hover = s_BtnDangerHover ?? idle;
                    pressed = hover;
                    break;
                case BtnStyle.Ghost:
                    idle = s_BtnGhost ?? s_Btn;
                    hover = s_BtnGhostHover ?? idle;
                    pressed = hover;
                    break;
            }

            var img = go.GetComponent<Image>();
            img.sprite = idle;
            img.type = Image.Type.Sliced;
            img.color = Color.white;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.SpriteSwap;
            var spriteState = btn.spriteState;
            spriteState.highlightedSprite = hover;
            spriteState.pressedSprite = pressed;
            spriteState.disabledSprite = s_BtnDisabled ?? idle;
            btn.spriteState = spriteState;
            var colors = btn.colors;
            colors.fadeDuration = 0.06f;
            btn.colors = colors;

            btn.onClick.AddListener(() =>
            {
                CicCue.Ok(go.transform.position);
                onClick?.Invoke();
            });

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>(), 10f);
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label ?? string.Empty;
            tmp.fontSize = Mathf.Clamp(size.y * 0.38f, 16f, 28f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return btn;
        }

        /// <summary>
        /// Select / dropdown header — field chrome, left title, count chip, chevron (▼/▲).
        /// Reads as a picker, not a plain action button.
        /// </summary>
        public static Button HoloSelect(Transform parent, string title, int count, bool open,
            Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            EnsureSprites();
            var go = new GameObject("Select_" + (title ?? "drop"), typeof(RectTransform), typeof(Image),
                typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var img = go.GetComponent<Image>();
            img.sprite = open ? (s_BtnAmber ?? s_Field ?? s_Btn) : (s_Field ?? s_PanelDark ?? s_Btn);
            img.type = Image.Type.Sliced;
            img.color = open
                ? new Color(1f, 0.92f, 0.75f, 1f)
                : new Color(0.75f, 0.9f, 1f, 0.95f);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
            colors.pressedColor = new Color(0.85f, 0.9f, 0.95f, 1f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            btn.onClick.AddListener(() =>
            {
                CicCue.Ok(go.transform.position);
                onClick?.Invoke();
            });

            // Left accent bar
            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(go.transform, false);
            var art = accent.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0f, 0.12f);
            art.anchorMax = new Vector2(0f, 0.88f);
            art.pivot = new Vector2(0f, 0.5f);
            art.sizeDelta = new Vector2(8f, 0f);
            art.anchoredPosition = new Vector2(6f, 0f);
            var aimg = accent.GetComponent<Image>();
            aimg.color = open ? Amber : Cyan;
            aimg.raycastTarget = false;

            // Title
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(go.transform, false);
            var trt = titleGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(22f, 4f);
            trt.offsetMax = new Vector2(-110f, -4f);
            var ttmp = titleGo.GetComponent<TextMeshProUGUI>();
            ttmp.text = string.IsNullOrEmpty(title) ? "—" : title;
            ttmp.fontSize = Mathf.Clamp(size.y * 0.36f, 15f, 24f);
            ttmp.fontStyle = FontStyles.Bold;
            ttmp.alignment = TextAlignmentOptions.MidlineLeft;
            ttmp.color = Color.white;
            ttmp.raycastTarget = false;
            ttmp.overflowMode = TextOverflowModes.Ellipsis;

            // Count chip
            var chip = new GameObject("Count", typeof(RectTransform), typeof(Image));
            chip.transform.SetParent(go.transform, false);
            var crt = chip.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 0.5f);
            crt.anchorMax = new Vector2(1f, 0.5f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.sizeDelta = new Vector2(52f, size.y * 0.62f);
            crt.anchoredPosition = new Vector2(-48f, 0f);
            var cimg = chip.GetComponent<Image>();
            cimg.sprite = s_BtnGhost ?? s_Btn;
            cimg.type = Image.Type.Sliced;
            cimg.color = open
                ? new Color(0.2f, 0.12f, 0.05f, 0.85f)
                : new Color(0.05f, 0.2f, 0.28f, 0.9f);
            cimg.raycastTarget = false;
            var countGo = new GameObject("N", typeof(RectTransform), typeof(TextMeshProUGUI));
            countGo.transform.SetParent(chip.transform, false);
            Stretch(countGo.GetComponent<RectTransform>(), 2f);
            var ctmp = countGo.GetComponent<TextMeshProUGUI>();
            ctmp.text = count.ToString();
            ctmp.fontSize = 16f;
            ctmp.fontStyle = FontStyles.Bold;
            ctmp.alignment = TextAlignmentOptions.Center;
            ctmp.color = open ? Amber : Cyan;
            ctmp.raycastTarget = false;

            // Chevron
            var chev = new GameObject("Chevron", typeof(RectTransform), typeof(TextMeshProUGUI));
            chev.transform.SetParent(go.transform, false);
            var chrt = chev.GetComponent<RectTransform>();
            chrt.anchorMin = new Vector2(1f, 0f);
            chrt.anchorMax = new Vector2(1f, 1f);
            chrt.pivot = new Vector2(1f, 0.5f);
            chrt.sizeDelta = new Vector2(40f, 0f);
            chrt.anchoredPosition = new Vector2(-6f, 0f);
            var chtmp = chev.GetComponent<TextMeshProUGUI>();
            chtmp.text = open ? "▲" : "▼";
            chtmp.fontSize = 18f;
            chtmp.alignment = TextAlignmentOptions.Center;
            chtmp.color = open ? Amber : Cyan;
            chtmp.raycastTarget = false;

            return btn;
        }

        /// <summary>Indented option under an open <see cref="HoloSelect"/> tray.</summary>
        public static Button HoloSelectOption(Transform parent, string label, Vector2 anchoredPos,
            Vector2 size, UnityEngine.Events.UnityAction onClick, BtnStyle style = BtnStyle.Ghost)
        {
            var btn = HoloButton(parent, label ?? string.Empty, anchoredPos, size, onClick, style);
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                var trt = tmp.rectTransform;
                trt.offsetMin = new Vector2(28f, 4f);
                trt.offsetMax = new Vector2(-12f, -4f);
            }

            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image));
            rail.transform.SetParent(btn.transform, false);
            var rrt = rail.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 0.18f);
            rrt.anchorMax = new Vector2(0f, 0.82f);
            rrt.pivot = new Vector2(0f, 0.5f);
            rrt.sizeDelta = new Vector2(5f, 0f);
            rrt.anchoredPosition = new Vector2(10f, 0f);
            var rimg = rail.GetComponent<Image>();
            rimg.color = style == BtnStyle.Amber ? Amber : CyanDim;
            rimg.raycastTarget = false;
            return btn;
        }

        /// <summary>Dark inset tray behind expanded select options.</summary>
        public static RectTransform HoloSelectTray(Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            EnsureSprites();
            var go = new GameObject("SelectTray", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.GetComponent<Image>();
            img.sprite = s_PanelDark ?? s_Panel ?? s_Field;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.08f, 0.18f, 0.24f, 0.92f);
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>World-space text field (Quest system keyboard on select).</summary>
        public static TMP_InputField HoloField(Transform parent, string name, string placeholder, Vector2 pos,
            Vector2 size, TouchScreenKeyboardType keyboard, bool hidden = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var bg = go.GetComponent<Image>();
            bg.sprite = SprField;
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(go.transform, false);
            var art = accent.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0f, 0f);
            art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(0f, 0.5f);
            art.sizeDelta = new Vector2(4f, 0f);
            art.anchoredPosition = Vector2.zero;
            accent.GetComponent<Image>().color = Cyan;
            accent.GetComponent<Image>().raycastTarget = false;
            // Field sprite already has left accent — hide duplicate when sprite present.
            if (SprField != null)
                accent.SetActive(false);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>(), 20f);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.fontSize = Mathf.Clamp(size.y * 0.42f, 16f, 24f);
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            phGo.transform.SetParent(go.transform, false);
            Stretch(phGo.GetComponent<RectTransform>(), 20f);
            var ph = phGo.GetComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.fontSize = Mathf.Clamp(size.y * 0.42f, 16f, 24f);
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(0.45f, 0.7f, 0.78f, 0.65f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;

            var field = go.GetComponent<TMP_InputField>();
            field.textViewport = rt;
            field.textComponent = text;
            field.placeholder = ph;
            field.keyboardType = keyboard;
            field.contentType = hidden
                ? TMP_InputField.ContentType.Password
                : TMP_InputField.ContentType.Standard;
            field.shouldHideMobileInput = false;
            field.caretColor = Cyan;
            field.selectionColor = new Color(0.2f, 0.7f, 0.85f, 0.35f);
            return field;
        }

        public static TMP_Text HoloLabel(Transform parent, string text, Vector2 anchoredPos, Vector2 size,
            float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text ?? string.Empty;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, 6f);
            rt.offsetMax = new Vector2(-pad, -6f);
        }

        // --- Mesh fallbacks (alcoves / hex) — textured when possible ---

        public static TMP_Text Label(Transform parent, string name, string text, Vector3 localPos,
            float worldScale, float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * worldScale;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = align;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.text = text ?? string.Empty;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.raycastTarget = false;
            tmp.rectTransform.sizeDelta = new Vector2(Mathf.Max(12f, fontSize * 4f), Mathf.Max(4f, fontSize * 1.2f));
            return tmp;
        }

        public static Transform Panel(Transform parent, string name, Vector3 localPos, Vector3 size,
            CicArtKit art, out MeshRenderer glassRend)
        {
            EnsureSprites();
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;

            var chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "Chassis";
            chassis.transform.SetParent(root, false);
            chassis.transform.localPosition = Vector3.zero;
            chassis.transform.localScale = size;
            CicEnvironment.DropColliderStatic(chassis);
            if (art != null)
                chassis.GetComponent<MeshRenderer>().sharedMaterial = art.MetalPanel(0.12f);

            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glass.name = "Glass";
            glass.transform.SetParent(root, false);
            glass.transform.localPosition = new Vector3(0f, 0f, -size.z * 0.52f);
            glass.transform.localScale = new Vector3(size.x * 0.94f, size.y * 0.92f, 1f);
            CicEnvironment.DropColliderStatic(glass);
            glassRend = glass.GetComponent<MeshRenderer>();
            if (art != null)
            {
                var tex = Resources.Load<Texture2D>("CIC/HoloPanel") ?? art.ScreenIdle as Texture2D;
                glassRend.sharedMaterial = art.Holo(tex != null ? tex : Texture2D.whiteTexture,
                    new Color(0.25f, 0.9f, 1f, 0.72f));
            }

            return root;
        }

        /// <summary>
        /// Physical console button (<see cref="Core.UI.PokeButton"/>): poke or ray. <paramref name="localPos"/>
        /// is in <paramref name="parent"/>'s local units (legacy callers); the button itself is built on an
        /// unscaled socket at that point, so a stretched parent no longer squashes cap or label.
        /// <paramref name="size"/>.x/.y = cap face in metres; front faces the parent's -Z.
        /// </summary>
        public static XRSimpleInteractable Button(Transform parent, string name, string label,
            Vector3 localPos, Vector3 size, CicArtKit art, Color accent, System.Action onSelect,
            bool interact = true)
        {
            var offsetMeters = Vector3.Scale(localPos, parent.lossyScale);
            var socket = Core.UI.ScreenMount.Socket(parent, name + "_Mount", offsetMeters, Quaternion.identity);
            var button = Core.UI.PokeButton.Create(socket, name, label, Vector3.zero, Quaternion.identity,
                new Vector2(Mathf.Max(0.05f, size.x), Mathf.Max(0.035f, size.y)), accent,
                interact ? onSelect : null);
            if (!interact)
                button.Interactive = false;
            return button.GetComponent<XRSimpleInteractable>();
        }

        public static XRSimpleInteractable Tab(Transform parent, string name, string label,
            Vector3 localPos, Vector3 size, CicArtKit art, Color accent, System.Action onSelect) =>
            Button(parent, name, label, localPos, size, art, accent, onSelect);

        public static XRSimpleInteractable Row(Transform parent, string name, string primary,
            string secondary, Vector3 localPos, Vector3 size, CicArtKit art, System.Action onSelect)
        {
            var xi = Button(parent, name, primary, localPos, size, art, Cyan, onSelect);
            if (!string.IsNullOrEmpty(secondary))
            {
                Core.UI.UiKit.Label(xi.transform, name + "_S", secondary,
                    new Vector3(0f, -size.y * 0.5f - 0.012f, -0.004f), size.x, 0.011f, CyanDim);
            }

            return xi;
        }

        public static TMP_Text Readout(Transform parent, string name, string text, Vector3 localPos,
            float worldScale = 0.025f)
        {
            var tmp = Label(parent, name, text ?? string.Empty, localPos, worldScale, 5f, Amber);
            tmp.rectTransform.sizeDelta = new Vector2(48f, 6f);
            return tmp;
        }
    }
}
