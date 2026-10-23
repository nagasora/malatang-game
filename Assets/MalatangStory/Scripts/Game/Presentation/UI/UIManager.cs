using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>画面の種類。</summary>
    public enum GameScreen { Title, Store, Cooking, Shop, Management, Build, Report, Morning, Pause, Settings, Wardrobe, Help, Register, Collection, Accessibility, Preparation, Challenges }

    /// <summary>
    /// 画面の基底クラス。Build で一度だけ部品を作り、以後は Refresh（状態変化時）と Tick（毎フレーム）で中身だけ更新する。
    /// </summary>
    public abstract class UIScreen
    {
        protected GameApplication App;
        protected RestaurantSimulation Sim => App.Simulation;
        protected GameState S => App.Simulation.State;
        public RectTransform Root { get; private set; }

        /// <summary>時間を止める画面か（一時停止・設定など）。</summary>
        public virtual bool PausesGame => false;
        /// <summary>画面中央に大きなカードを出す画面か（通知はカードに重ならないよう画面下に出す）。</summary>
        public virtual bool Modal => PausesGame;
        /// <summary>店内の HUD（上部の情報と注文票）を表示するか。</summary>
        public virtual bool ShowHud => true;

        public void Create(GameApplication app, RectTransform parent)
        {
            App = app;
            Root = UIKit.Fill(UIKit.Rect(GetType().Name, parent));
            Build(Root);
            Refresh();
        }

        protected abstract void Build(RectTransform root);
        public virtual void Refresh() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void Close() { if (Root != null) UnityEngine.Object.Destroy(Root.gameObject); }

        /// <summary>子を全て削除する（部分的な作り直し用）。</summary>
        protected static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }

    /// <summary>
    /// UI 全体の管理。キャンバス・セーフエリア・画面の切り替え・HUD・通知（トースト）・浮かぶ文字を扱う。
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        private GameApplication app;
        private RectTransform safe, screenLayer, hudLayer, toastLayer, popupLayer;
        private Canvas canvas;
        private CanvasScaler scaler;
        private UIScreen current;
        private HudView hud;
        private sealed class ToastItem
        {
            public RectTransform Rect;
            public float Age, Life;
            public string Message;
            public IconKind Icon;
            public Color? Accent;
        }
        private readonly List<ToastItem> toasts = new List<ToastItem>();
        private int ToastCapacity => ToastAtBottom || announcement != null || safe.rect.height < 650 ? 1 : 2;
        private readonly List<(RectTransform rt, Vector3 world, float age, float life)> popups = new List<(RectTransform, Vector3, float, float)>();
        private readonly Queue<(string message, IconKind icon, Color? accent, float seconds)> pendingToasts = new Queue<(string, IconKind, Color?, float)>();
        private readonly Vector3[] overlayCorners = new Vector3[4];
        private readonly List<Rect> overlayObstacles = new List<Rect>(16);
        private readonly List<SpeechBubble> speeches = new List<SpeechBubble>();

        /// <summary>3D の人物の頭上に追従する吹き出し。</summary>
        private sealed class SpeechBubble
        {
            public string Key;
            public RectTransform Rect;
            public Func<Vector3?> Anchor;
            public float Age, Life;
        }
        private Rect lastSafeArea;
        public UIScreen Current => current;
        public HudView Hud => hud;
        public RectTransform Safe => safe;

        public void Initialize(GameApplication application, Font font)
        {
            app = application;
            UIKit.Font = font;
            var canvasObject = new GameObject("Game UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 1f;
            safe = UIKit.Rect("Safe Area", canvasObject.transform);
            hudLayer = UIKit.Fill(UIKit.Rect("HUD", safe));
            screenLayer = UIKit.Fill(UIKit.Rect("Screens", safe));
            popupLayer = UIKit.Fill(UIKit.Rect("Popups", safe));
            toastLayer = UIKit.Fill(UIKit.Rect("Toasts", safe));
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem.transform.SetParent(transform);
            }
            UpdateSafeArea();
        }

        private void UpdateSafeArea()
        {
            Rect area = Screen.safeArea;
            if (area == lastSafeArea || Screen.width <= 0 || Screen.height <= 0) return;
            lastSafeArea = area;
            // 画面はセーフエリアの横1280を基準に組んでいるため、高さ合わせで横幅が足りない画面（iPad の 4:3 など）では
            // 幅合わせに切り替え、セーフエリアの左右の余白の分だけ基準幅を広げて、安全領域が1280になるようにする。
            float safeWidthByHeight = area.width * 720f / Screen.height;
            bool byHeight = safeWidthByHeight >= 1279f;
            scaler.matchWidthOrHeight = byHeight ? 1f : 0f;
            scaler.referenceResolution = new Vector2(byHeight ? 1280f : 1280f * Screen.width / area.width, 720);
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
        }

        /// <summary>画面を切り替える。</summary>
        public void Show(GameScreen screen)
        {
            current?.Close();
            current = Create(screen);
            current?.Create(app, screenLayer);
            bool showHud = current != null && current.ShowHud && app.Started;
            if (showHud && hud == null) { hud = new HudView(); hud.Create(app, hudLayer); }
            if (!showHud && hud != null) { hud.Close(); hud = null; }
            if (screen == GameScreen.Title) { ClearToasts(); ClearSpeech(); }
        }

        private static UIScreen Create(GameScreen screen)
        {
            switch (screen)
            {
                case GameScreen.Title: return new TitleScreen();
                case GameScreen.Store: return new StoreScreen();
                case GameScreen.Cooking: return new CookingScreen();
                case GameScreen.Shop: return new ShopScreen();
                case GameScreen.Management: return new ManagementScreen();
                case GameScreen.Build: return new BuildScreen();
                case GameScreen.Report: return new ReportScreen();
                case GameScreen.Morning: return new MorningScreen();
                case GameScreen.Pause: return new PauseScreen();
                case GameScreen.Settings: return new SettingsScreen();
                case GameScreen.Accessibility: return new AccessibilityScreen();
                case GameScreen.Preparation: return new PreparationScreen();
                case GameScreen.Challenges: return new ChallengeScreen();
                case GameScreen.Wardrobe: return new WardrobeScreen();
                case GameScreen.Help: return new HelpScreen();
                case GameScreen.Register: return new RegisterScreen();
                case GameScreen.Collection: return new CollectionScreen();
                default: return null;
            }
        }

        /// <summary>状態が変わった時に呼ぶ。</summary>
        public void Refresh()
        {
            hud?.Refresh();
            current?.Refresh();
        }

        /// <summary>画面上部に通知を出す。</summary>
        public void Toast(string message, IconKind icon = IconKind.Info, Color? accent = null, float seconds = 3.2f)
        {
            if (string.IsNullOrEmpty(message)) return;
            for (int i = 0; i < toasts.Count; i++)
            {
                if (toasts[i].Message == message) { toasts[i].Age = 0; toasts[i].Life = Mathf.Max(toasts[i].Life, seconds); return; }
            }
            foreach (var pending in pendingToasts) if (pending.message == message) return;
            if (toasts.Count >= ToastCapacity)
            {
                pendingToasts.Enqueue((message, icon, accent, seconds));
                return;
            }
            var panel = UIKit.Panel(toastLayer, new Color(.16f, .11f, .09f, .92f), 18, true, "Toast");
            Rect bounds = OverlayLayout.ToastRect(safe.rect, app.Screen, 0, ToastAtBottom);
            var rt = UIKit.Place(panel.rectTransform, new Vector2(.5f, 1), new Vector2(bounds.center.x - safe.rect.center.x, bounds.center.y - safe.rect.yMax), bounds.size);
            var iconImage = UIKit.Icon(rt, icon, accent ?? UIKit.Gold);
            UIKit.Place(iconImage.rectTransform, new Vector2(0, .5f), new Vector2(14, 0), new Vector2(30, 30));
            var text = UIKit.Label(rt, message, 19, UIKit.Paper, TextAnchor.MiddleLeft);
            UIKit.Fill(text.rectTransform, 54, 14, 4, 4);
            panel.gameObject.AddComponent<PopIn>().Duration = .22f;
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
            toasts.Add(new ToastItem { Rect = rt, Life = seconds, Message = message, Icon = icon, Accent = accent });
        }

        private RectTransform announcement;

        /// <summary>
        /// 画面中央に物語や発見を知らせるカードを出す（画面は切り替えず、タップか時間経過で閉じる）。
        /// 新しい知らせが来たら前のものは閉じる。
        /// </summary>
        public void Announce(string title, string body, IconKind icon = IconKind.Book, float seconds = 9f)
        {
            if (announcement != null) Destroy(announcement.gameObject);
            var panel = UIKit.Panel(toastLayer, UIKit.Paper, 24, true, "Announcement");
            announcement = UIKit.Place(panel.rectTransform, new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(660, 250));
            var header = UIKit.Panel(announcement, UIKit.Lacquer, 24, false, "Header").rectTransform;
            header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1); header.pivot = new Vector2(.5f, 1);
            header.anchoredPosition = Vector2.zero; header.sizeDelta = new Vector2(0, 54);
            var iconImage = UIKit.Icon(header, icon, UIKit.Gold);
            UIKit.Place(iconImage.rectTransform, new Vector2(0, .5f), new Vector2(18, 0), new Vector2(30, 30));
            var titleText = UIKit.Label(header, title, 23, UIKit.Paper, TextAnchor.MiddleLeft, true);
            UIKit.Fill(titleText.rectTransform, 60, 16, 0, 0);
            var bodyText = UIKit.Label(announcement, body, 18, UIKit.Ink, TextAnchor.UpperLeft);
            bodyText.resizeTextMinSize = 14;
            UIKit.Fill(bodyText.rectTransform, 24, 24, 66, 60);
            var close = UIKit.Button(announcement, "閉じる", () => { if (announcement != null) Destroy(announcement.gameObject); }, ButtonStyle.Primary, 18, IconKind.Check);
            UIKit.Place(close.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(200, 44));
            panel.gameObject.AddComponent<PopIn>();
            Destroy(panel.gameObject, seconds);
        }

        /// <summary>大きなカードの画面では、通知をカードの下端（ボタン列の下の余白）に寄せる。</summary>
        private bool ToastAtBottom => current != null && current.Modal;

        private void ClearSpeech()
        {
            foreach (var b in speeches) if (b.Rect != null) Destroy(b.Rect.gameObject);
            speeches.Clear();
        }

        private void ClearToasts()
        {
            foreach (var t in toasts) if (t.Rect != null) Destroy(t.Rect.gameObject);
            toasts.Clear();
            pendingToasts.Clear();
            if (announcement != null) Destroy(announcement.gameObject);
        }

        /// <summary>3D 空間の位置に、浮かび上がって消える文字を出す（売上・品質など）。</summary>
        public void FloatText(Vector3 worldPosition, string text, Color color, int size = 26)
        {
            var label = UIKit.Outlined(UIKit.Label(popupLayer, text, size, color, TextAnchor.MiddleCenter, true, "Float"), new Color(0, 0, 0, .75f), 2);
            label.rectTransform.sizeDelta = new Vector2(320, 50);
            label.resizeTextForBestFit = false;
            popups.Add((label.rectTransform, worldPosition, 0, 1.6f));
            PositionPopup(label.rectTransform, worldPosition, 0);
        }

        /// <summary>
        /// 人物の頭上に吹き出しを出す。key が同じ吹き出しは置き換える（同じ客の続きのせりふ）。
        /// anchor が null を返すと（客が帰ったなど）消える。
        /// </summary>
        public void Speech(string key, Func<Vector3?> anchor, string text, float seconds = 3.2f, bool unhappy = false)
        {
            if (string.IsNullOrEmpty(text) || anchor == null) return;
            for (int i = speeches.Count - 1; i >= 0; i--)
                if (speeches[i].Key == key) { if (speeches[i].Rect != null) Destroy(speeches[i].Rect.gameObject); speeches.RemoveAt(i); }
            while (speeches.Count >= 4) { if (speeches[0].Rect != null) Destroy(speeches[0].Rect.gameObject); speeches.RemoveAt(0); }
            var panel = UIKit.Panel(popupLayer, unhappy ? new Color(1f, .9f, .86f, .97f) : new Color(1, .99f, .95f, .97f), 16, true, "Speech");
            panel.raycastTarget = false;
            var rt = panel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.pivot = new Vector2(.5f, 0);
            var label = UIKit.Label(rt, text, 17, unhappy ? UIKit.LacquerDark : UIKit.Ink, TextAnchor.MiddleCenter, false, "Text");
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            float width = Mathf.Clamp(text.Length * 17 + 30, 120, 340);
            int lines = Mathf.CeilToInt(text.Length * 17f / (width - 30));
            rt.sizeDelta = new Vector2(width, 22 + lines * 24);
            UIKit.Fill(label.rectTransform, 12, 12, 6, 6);
            var tail = UIKit.Panel(rt, panel.color, 4, false, "Tail");
            tail.raycastTarget = false;
            var trt = tail.rectTransform; trt.anchorMin = trt.anchorMax = new Vector2(.5f, 0); trt.sizeDelta = new Vector2(14, 14);
            trt.anchoredPosition = new Vector2(0, -2); trt.localRotation = Quaternion.Euler(0, 0, 45);
            trt.SetAsFirstSibling();
            panel.gameObject.AddComponent<PopIn>().Duration = .18f;
            speeches.Add(new SpeechBubble { Key = key, Rect = rt, Anchor = anchor, Life = Mathf.Max(1.5f, seconds) });
        }

        private void PositionPopup(RectTransform rt, Vector3 world, float age)
        {
            var cam = app.World != null ? app.World.View : null;
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * age * .6f);
            rt.gameObject.SetActive(screen.z > 0);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(popupLayer, screen, null, out var local);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.anchoredPosition = OverlayLayout.Clamp(popupLayer.rect, rt.sizeDelta, rt.pivot, local);
        }

        private bool ViewVisible(Vector3 point) => app.World.View.WorldToViewportPoint(point).z > 0;

        private void AddObstacle(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return;
            rt.GetWorldCorners(overlayCorners);
            Vector2 low = popupLayer.InverseTransformPoint(overlayCorners[0]);
            Vector2 high = popupLayer.InverseTransformPoint(overlayCorners[2]);
            overlayObstacles.Add(Rect.MinMaxRect(low.x - 8, low.y - 8, high.x + 8, high.y + 8));
        }

        private void Update()
        {
            UpdateSafeArea();
            float dt = Time.unscaledDeltaTime;
            // 大きなカードの画面では、店内の吹き出し・浮かぶ文字がカードの上に重なって読みにくいため隠す。
            bool modal = ToastAtBottom;
            if (popupLayer.gameObject.activeSelf == modal) popupLayer.gameObject.SetActive(!modal);

            while (toasts.Count > ToastCapacity)
            {
                ToastItem item = toasts[toasts.Count - 1];
                int waiting = pendingToasts.Count;
                pendingToasts.Enqueue((item.Message, item.Icon, item.Accent, Mathf.Max(.5f, item.Life - item.Age)));
                for (int i = 0; i < waiting; i++) pendingToasts.Enqueue(pendingToasts.Dequeue());
                item.Rect.gameObject.SetActive(false); Destroy(item.Rect.gameObject); toasts.RemoveAt(toasts.Count - 1);
            }
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                t.Age += dt;
                if (t.Rect == null || t.Age > t.Life) { if (t.Rect != null) Destroy(t.Rect.gameObject); toasts.RemoveAt(i); continue; }
                toasts[i] = t;
            }
            if (pendingToasts.Count > 0 && toasts.Count < ToastCapacity)
            {
                var pending = pendingToasts.Dequeue();
                Toast(pending.message, pending.icon, pending.accent, pending.seconds);
            }
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                Rect bounds = OverlayLayout.ToastRect(safe.rect, app.Screen, i, ToastAtBottom);
                t.Rect.sizeDelta = bounds.size;
                t.Rect.anchoredPosition = new Vector2(bounds.center.x - safe.rect.center.x, bounds.center.y - safe.rect.yMax);
                float fade = Mathf.Clamp01((t.Life - t.Age) / .35f);
                var group = t.Rect.GetComponent<CanvasGroup>();
                if (group != null && t.Rect.GetComponent<PopIn>() == null) group.alpha = fade;
            }
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                p.age += dt;
                if (p.rt == null || p.age > p.life) { if (p.rt != null) Destroy(p.rt.gameObject); popups.RemoveAt(i); continue; }
                PositionPopup(p.rt, p.world, p.age);
                var text = p.rt.GetComponent<Text>();
                var c = text.color; c.a = Mathf.Clamp01((p.life - p.age) / .5f); text.color = c;
                p.rt.localScale = Vector3.one * (p.age < .15f ? Mathf.Lerp(.6f, 1.15f, p.age / .15f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((p.age - .15f) / .2f)));
                popups[i] = p;
            }
            overlayObstacles.Clear();
            AddObstacle(announcement);
            AddObstacle(current?.Root?.Find("Dialog") as RectTransform);
            AddObstacle(current?.Root?.Find("Register Card") as RectTransform);
            AddObstacle(current?.Root?.Find("Tutorial") as RectTransform);
            AddObstacle(current?.Root?.Find("Missions") as RectTransform);
            AddObstacle(current?.Root?.Find("Next Action") as RectTransform);
            foreach (var t in toasts) AddObstacle(t.Rect);
            for (int i = speeches.Count - 1; i >= 0; i--)
            {
                var b = speeches[i];
                b.Age += dt;
                Vector3? anchor = b.Rect != null ? b.Anchor() : null;
                if (b.Rect == null || b.Age > b.Life || !anchor.HasValue) { if (b.Rect != null) Destroy(b.Rect.gameObject); speeches.RemoveAt(i); continue; }
                PositionPopup(b.Rect, anchor.Value, 0);
                Vector2 placed;
                bool fits = OverlayLayout.TryPlace(popupLayer.rect, b.Rect.sizeDelta, b.Rect.pivot, b.Rect.anchoredPosition, overlayObstacles, out placed);
                b.Rect.gameObject.SetActive(fits && ViewVisible(anchor.Value));
                b.Rect.anchoredPosition = placed;
                if (fits) AddObstacle(b.Rect);
            }
            if (app == null || app.Simulation == null) return;
            hud?.Tick(dt);
            current?.Tick(dt);
        }
    }
}
