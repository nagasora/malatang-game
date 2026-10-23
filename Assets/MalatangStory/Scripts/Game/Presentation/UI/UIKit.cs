using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>ボタンの見た目の種類。</summary>
    public enum ButtonStyle { Primary, Secondary, Ghost, Danger, Gold, Tab, TabActive }

    /// <summary>
    /// 統一された見た目の uGUI 部品を作るファクトリー。
    /// 色・角丸・影・押下時の反応をここに集約し、各画面は配置と内容だけを書く。
    /// </summary>
    public static class UIKit
    {
        public static Font Font, HeadingFont;
        public static float TextScale = 1;
        public static bool ReduceMotion;
        /// <summary>ボタン押下時に鳴らす音（オーディオ側が設定する）。</summary>
        public static Action ClickSound;

        public static readonly Color Ink = new Color(.22f, .15f, .11f);
        public static readonly Color InkSoft = new Color(.42f, .33f, .27f);
        public static readonly Color Paper = new Color(.99f, .96f, .89f);
        public static readonly Color PaperDark = new Color(.94f, .88f, .76f);
        public static readonly Color Lacquer = new Color(.74f, .16f, .12f);
        public static readonly Color LacquerDark = new Color(.52f, .1f, .08f);
        public static readonly Color Gold = new Color(.93f, .69f, .25f);
        public static readonly Color Jade = new Color(.2f, .47f, .38f);
        public static readonly Color JadeDark = new Color(.13f, .32f, .26f);
        public static readonly Color Night = new Color(.1f, .09f, .1f, .78f);
        public static readonly Color Good = new Color(.25f, .62f, .32f);
        public static readonly Color Warn = new Color(.92f, .55f, .13f);
        public static readonly Color Bad = new Color(.82f, .2f, .16f);

        public static RectTransform Rect(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>親の中で四隅・中央などに固定して配置する。anchor は (0,0)=左下 〜 (1,1)=右上。</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }

        /// <summary>親いっぱいに広げる（余白つき）。</summary>
        public static RectTransform Fill(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>角丸パネル。shadow で柔らかい影を付ける。</summary>
        public static Image Panel(Transform parent, Color color, int radius = 18, bool shadow = true, string name = "Panel")
        {
            var rt = Rect(name, parent);
            if (shadow)
            {
                var shade = Rect("Shadow", rt);
                Fill(shade, -10, -10, -6, -14);
                var shadowImage = shade.gameObject.AddComponent<Image>();
                shadowImage.sprite = TextureFactory.SoftShadow(radius + 8);
                shadowImage.type = Image.Type.Sliced;
                shadowImage.color = new Color(0, 0, 0, .28f);
                shadowImage.raycastTarget = false;
            }
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = TextureFactory.RoundedRect(radius);
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        public static Text Label(Transform parent, string text, int size = 20, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false, string name = "Label")
        {
            var rt = Rect(name, parent);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = bold && HeadingFont != null ? HeadingFont : Font; label.text = UiText.TranslateSource(text); label.fontSize = Mathf.RoundToInt(size * TextScale); label.color = color ?? Ink;
            label.alignment = align; label.raycastTarget = false; label.supportRichText = false;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = Mathf.Max(10, size - 8); label.resizeTextMaxSize = Mathf.RoundToInt(size * TextScale);
            label.lineSpacing = 1.05f;
            return label;
        }

        /// <summary>文字に縁取りを付ける（3D画面の上でも読めるように）。</summary>
        public static Text Outlined(Text label, Color outline, float distance = 1.6f)
        {
            var o = label.gameObject.AddComponent<Outline>();
            o.effectColor = outline; o.effectDistance = new Vector2(distance, -distance);
            return label;
        }

        public static Image Icon(Transform parent, IconKind kind, Color color, string name = "Icon")
        {
            var rt = Rect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = TextureFactory.Icon(kind); image.color = color; image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private static Color themeAccent = Jade;
        public static void SetTheme(string id)
        {
            themeAccent = id == "neon" ? new Color(.62f, .18f, .43f) : id == "cafe" ? new Color(.43f, .3f, .23f)
                : id == "lantern" ? new Color(.66f, .24f, .12f) : id == "teahouse" ? new Color(.25f, .41f, .2f) : Jade;
        }
        public static void ReceiptPerforations(RectTransform card)
        {
            card.localRotation = Quaternion.Euler(0, 0, -.35f);
            for (int x = 16; x < card.rect.width - 8; x += 18)
            {
                var dot = Panel(card, PaperDark, 3, false, "Receipt Perforation");
                Place(dot.rectTransform, new Vector2(0, 1), new Vector2(x, -61), new Vector2(5, 5));
            }
        }
        public static void LedgerLines(RectTransform parent, int count, float width)
        {
            for (int i = 0; i < count; i++)
            {
                var line = Panel(parent, new Color(.4f, .3f, .2f, .12f), 1, false, "Ledger Rule");
                Place(line.rectTransform, new Vector2(0, 1), new Vector2(0, -24 - i * 22), new Vector2(width, 1));
            }
        }

        public static (Color fill, Color text) Colors(ButtonStyle style)
        {
            switch (style)
            {
                case ButtonStyle.Primary: return (Lacquer, Paper);
                case ButtonStyle.Danger: return (Bad, Paper);
                case ButtonStyle.Gold: return (Gold, Ink);
                case ButtonStyle.Ghost: return (new Color(1, 1, 1, .0f), Ink);
                case ButtonStyle.Tab: return (PaperDark, InkSoft);
                case ButtonStyle.TabActive: return (Lacquer, Paper);
                default: return (themeAccent, Paper);
            }
        }

        /// <summary>押すと少し沈む角丸ボタン。icon を指定すると左にアイコンを置く。</summary>
        public static Button Button(Transform parent, string label, Action onClick, ButtonStyle style = ButtonStyle.Secondary, int size = 20,
            IconKind? icon = null, bool interactable = true, string name = null)
        {
            var (fill, textColor) = Colors(style);
            var image = Panel(parent, fill, 14, style != ButtonStyle.Ghost && style != ButtonStyle.Tab, name ?? label);
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.06f, 1.06f, 1.06f);
            colors.pressedColor = new Color(.85f, .85f, .85f); colors.disabledColor = new Color(.62f, .6f, .58f, .7f);
            colors.fadeDuration = .06f;
            button.colors = colors;
            button.interactable = interactable;
            image.gameObject.AddComponent<PressFeedback>();
            float iconSpace = 0;
            if (icon.HasValue)
            {
                var iconImage = Icon(image.transform, icon.Value, textColor);
                var irt = iconImage.rectTransform;
                irt.anchorMin = new Vector2(0, .5f); irt.anchorMax = new Vector2(0, .5f); irt.pivot = new Vector2(0, .5f);
                irt.anchoredPosition = new Vector2(12, 0); irt.sizeDelta = new Vector2(size + 6, size + 6);
                iconSpace = size + 14;
            }
            var text = Label(image.transform, label, size, textColor, string.IsNullOrEmpty(label) ? TextAnchor.MiddleCenter : TextAnchor.MiddleCenter, true);
            Fill(text.rectTransform, 8 + iconSpace, 8, 2, 2);
            if (string.IsNullOrEmpty(label) && icon.HasValue)
            {
                var irt = image.transform.Find("Icon").GetComponent<RectTransform>();
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(.5f, .5f); irt.anchoredPosition = Vector2.zero;
            }
            button.onClick.AddListener(() => { ClickSound?.Invoke(); onClick?.Invoke(); });
            return button;
        }

        /// <summary>ボタンの文字を書き換える。</summary>
        public static void SetLabel(Button button, string text)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.text = UiText.TranslateSource(text);
        }

        /// <summary>横棒のゲージ。戻り値の Fill の anchorMax.x で割合を設定する。</summary>
        public static RectTransform Bar(Transform parent, Color back, Color fill, out Image fillImage, int radius = 8)
        {
            var bg = Panel(parent, back, radius, false, "Bar");
            var f = Rect("Fill", bg.transform);
            f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(.5f, 1); f.offsetMin = Vector2.zero; f.offsetMax = Vector2.zero;
            fillImage = f.gameObject.AddComponent<Image>();
            fillImage.sprite = TextureFactory.RoundedRect(radius); fillImage.type = Image.Type.Sliced; fillImage.color = fill;
            fillImage.raycastTarget = false;
            return bg.rectTransform;
        }

        /// <summary>ゲージの割合を設定する。</summary>
        public static void SetBar(Image fillImage, float ratio)
        {
            var rt = fillImage.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            fillImage.enabled = ratio > .001f;
        }

        /// <summary>縦スクロールのリスト。戻り値のコンテンツに子を追加すると自動で並ぶ。</summary>
        public static RectTransform ScrollList(Transform parent, float spacing = 10, int padding = 8, string name = "List")
        {
            var outer = Rect(name, parent);
            var mask = outer.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
            var hit = outer.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            var scroll = outer.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Elastic; scroll.scrollSensitivity = 30;
            var content = Rect("Content", outer);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing; layout.padding = new RectOffset(padding, padding + 6, padding, padding);
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content; scroll.viewport = outer;
            return content;
        }

        /// <summary>格子状に並べるスクロール領域。</summary>
        public static RectTransform ScrollGrid(Transform parent, Vector2 cell, Vector2 spacing, string name = "Grid")
        {
            var content = ScrollList(parent, 0, 6, name);
            UnityEngine.Object.DestroyImmediate(content.GetComponent<VerticalLayoutGroup>());
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell; grid.spacing = spacing; grid.padding = new RectOffset(6, 12, 6, 6);
            grid.startAxis = GridLayoutGroup.Axis.Horizontal; grid.childAlignment = TextAnchor.UpperLeft;
            return content;
        }

        /// <summary>レイアウトグループ内での高さを指定する。</summary>
        public static T Height<T>(T component, float height, float width = -1) where T : Component
        {
            var element = (component.TryGetComponent<LayoutElement>(out var existingLayoutElement) ? existingLayoutElement : component.gameObject.AddComponent<LayoutElement>());
            element.preferredHeight = height; element.minHeight = height;
            if (width > 0) { element.preferredWidth = width; element.minWidth = width; }
            return component;
        }

        /// <summary>横並びの行（レイアウト用）。</summary>
        public static RectTransform Row(Transform parent, float height, float spacing = 8, string name = "Row")
        {
            var rt = Rect(name, parent);
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = true; layout.childAlignment = TextAnchor.MiddleLeft;
            Height(rt, height);
            return rt;
        }

        /// <summary>全画面を覆う半透明の幕（背面の操作を防ぐ）。</summary>
        public static Image Dim(Transform parent, float alpha = .45f, Action onClick = null)
        {
            var rt = Fill(Rect("Dim", parent));
            var image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(.05f, .03f, .02f, alpha);
            if (onClick != null) rt.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
            return image;
        }

        /// <summary>見出しつきの大きなカード（モーダル画面の土台）。</summary>
        public static InputField NameInput(Transform parent, string value, int maximum = 16)
        {
            var bg = Panel(parent, Color.white, 10, false, "Name Input");
            var field = bg.gameObject.AddComponent<InputField>();
            var text = Label(bg.transform, "", 19, Ink, TextAnchor.MiddleLeft);
            Fill(text.rectTransform, 12, 12, 4, 4); text.raycastTarget = false;
            field.textComponent = text; field.characterLimit = 256; field.text = value;
            field.onEndEdit.AddListener(v => field.SetTextWithoutNotify(PlayerText.Clean(v, maximum)));
            return field;
        }

        public static RectTransform Card(Transform parent, string title, Vector2 size, IconKind? icon = null, Action onClose = null)
        {
            var panel = Panel(parent, Paper, 24, true, "Card " + title);
            var rt = Place(panel.rectTransform, new Vector2(.5f, .5f), Vector2.zero, size);
            var header = Panel(rt, Lacquer, 24, false, "Header");
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(.5f, 1);
            hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = new Vector2(0, 62);
            float x = 24;
            if (icon.HasValue)
            {
                var i = Icon(hrt, icon.Value, Gold);
                Place(i.rectTransform, new Vector2(0, .5f), new Vector2(20, 0), new Vector2(34, 34));
                x = 64;
            }
            var t = Label(hrt, title, 28, Paper, TextAnchor.MiddleLeft, true, "Title");
            Fill(t.rectTransform, x, 70, 0, 0);
            if (onClose != null)
            {
                var close = Button(hrt, "", onClose, ButtonStyle.Ghost, 22, IconKind.Cross, true, "Close");
                Place(close.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(-10, 0), new Vector2(50, 50));
                close.transform.Find("Icon").GetComponent<Image>().color = Paper;
            }
            var body = Rect("Body", rt);
            Fill(body, 22, 22, 76, 20);
            return body;
        }

        /// <summary>数値を「1,234.50 元」の形に整える。</summary>
        public static string Money(long fen) => RestaurantSimulation.Money(fen);

        /// <summary>品質の星表示（★5段階）。</summary>
        public static string Stars(float quality)
        {
            int stars = Mathf.Clamp(Mathf.RoundToInt(quality / 20f), 0, 5);
            return new string('★', stars) + new string('☆', 5 - stars);
        }
    }

    /// <summary>押下時に少し縮み、離すと弾んで戻る。</summary>
    public sealed class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private float target = 1, current = 1, velocity;
        public void OnPointerDown(PointerEventData e) { var b = GetComponent<Selectable>(); if (b == null || b.interactable) target = .93f; }
        public void OnPointerUp(PointerEventData e) => target = 1;
        public void OnPointerExit(PointerEventData e) => target = 1;
        private void Update()
        {
            if (Mathf.Abs(current - target) < .0005f && Mathf.Abs(velocity) < .0005f) return;
            current = Mathf.SmoothDamp(current, target, ref velocity, .05f, 100, Time.unscaledDeltaTime);
            transform.localScale = Vector3.one * (UIKit.ReduceMotion ? 1 : current);
        }
    }

    /// <summary>押している間だけ Held(true)、離すと Held(false) を通知する。</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action<bool> Held;
        public bool IsHeld { get; private set; }
        public void OnPointerDown(PointerEventData e) { IsHeld = true; Held?.Invoke(true); }
        public void OnPointerUp(PointerEventData e) { if (!IsHeld) return; IsHeld = false; Held?.Invoke(false); }
        public void OnPointerExit(PointerEventData e) { if (!IsHeld) return; IsHeld = false; Held?.Invoke(false); }
        private void OnDisable() { if (IsHeld) { IsHeld = false; Held?.Invoke(false); } }
    }

    /// <summary>出現時にふわっと拡大・フェードインする。</summary>
    public sealed class PopIn : MonoBehaviour
    {
        public float Delay;
        public float Duration = .28f;
        private float time;
        private CanvasGroup group;
        private RectTransform rect;
        private Vector2 origin;
        private bool positioned;
        private void Awake()
        {
            group = (TryGetComponent<CanvasGroup>(out var existingCanvasGroup) ? existingCanvasGroup : gameObject.AddComponent<CanvasGroup>());
            group.alpha = UIKit.ReduceMotion ? 1 : 0; transform.localScale = Vector3.one * (UIKit.ReduceMotion ? 1 : .85f);
        }
        private void Update()
        {
            if (!positioned) { rect = transform as RectTransform; if (rect != null) origin = rect.anchoredPosition; positioned = true; }
            time += Time.unscaledDeltaTime;
            float t = UIKit.ReduceMotion ? 1 : Mathf.Clamp01((time - Delay) / Duration);
            float eased = 1 - Mathf.Pow(1 - t, 3);
            float overshoot = 1 + Mathf.Sin(t * Mathf.PI) * .06f;
            group.alpha = eased;
            if (rect != null) rect.anchoredPosition = origin + Vector2.down * (1 - eased) * 24;
            transform.localScale = Vector3.one * Mathf.Lerp(.85f, 1f, eased) * overshoot;
            if (t >= 1) { transform.localScale = Vector3.one; group.alpha = 1; Destroy(this); }
        }
    }
}
