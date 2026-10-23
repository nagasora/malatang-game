using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 店内（見下ろし視点）の画面。移動スティック・設備へのショートカット・経営メニュー・チュートリアル案内・
    /// 最寄り設備の操作ボタンを置く。
    /// </summary>
    public sealed class StoreScreen : UIScreen
    {
        public static readonly string[] TutorialSteps =
        {
            "スティックかWASDで歩いてみましょう。床をタップしてもその場所へ歩きます。",
            "お客さんがレジに来ました！レジへ行って注文を聞き、注文票を書いて、おつりを渡しましょう。",
            "冷蔵ケースから注文の具材（春雨・チンゲン菜・えのき・湯葉）をボウルに入れましょう。",
            "ボウルを秤に載せて、注文どおりの重さか確かめましょう。",
            "空いている鍋をタップして、網じゃくごと茹で始めましょう。",
            "具材の色が変わり、ゲージが緑になったら「引き上げる」。",
            "網じゃくを上下に振って湯切り！（2回以上）",
            "麻辣スープをおたまで注ぎましょう。ゲージの緑の範囲まで長押し。",
            "ラー油で辛さを1にして「完成」！",
            "注文票の「席へ運ぶ」をタップして、お客さんに届けましょう。",
            "お客さんが食べ終えました。テーブルをタップして片付けましょう。",
            "練習営業はここまで！「初日を終える」を押しましょう。",
            "仕入れ画面で食材を仕入れましょう（5個から購入できます）。",
            "初日の練習は完了です！"
        };

        private Text guideText, guideTitle, summary;
        private RectTransform guide, prepPanel;
        private Button finishTutorial, contextButton, buildButton;
        private Text contextLabel, crosshairLabel;
        private RectTransform missionPanel, crosshair, navPanel;
        private Text[] missionLines;
        private Text navText;
        private Image navIcon;
        private NextActionHint nextAction;
        private float navRefresh;

        protected override void Build(RectTransform root)
        {
            var dock = UIKit.Panel(root, new Color(.16f, .1f, .08f, .86f), 22, true, "Dock");
            UIKit.Place(dock.rectTransform, new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(690, 74));
            var row = UIKit.Fill(UIKit.Rect("Row", dock.rectTransform), 10, 10, 10, 10);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true;
            UIKit.Button(row, "仕入れ", () => App.Open(GameScreen.Shop), ButtonStyle.Secondary, 18, IconKind.Cart);
            UIKit.Button(row, "経営", () => App.Open(GameScreen.Management), ButtonStyle.Secondary, 18, IconKind.Chart);
            buildButton = UIKit.Button(row, "内装", () => App.Open(GameScreen.Build), ButtonStyle.Secondary, 18, IconKind.Hammer);
            UIKit.Button(row, "図鑑", () => App.Open(GameScreen.Collection), ButtonStyle.Secondary, 18, IconKind.Book);
            UIKit.Button(row, "写真", () => App.BeginPhoto(false), ButtonStyle.Secondary, 18, IconKind.Camera);
            UIKit.Button(row, "厨房", () => App.World.Visit(Station.Kitchen, () => App.Interact(Station.Kitchen)), ButtonStyle.Primary, 18, IconKind.Pot);

            crosshair = UIKit.Rect("Crosshair", root);
            UIKit.Place(crosshair, new Vector2(.5f, .5f), Vector2.zero, new Vector2(12, 12));
            var dot = crosshair.gameObject.AddComponent<Image>();
            dot.sprite = TextureFactory.Circle(32); dot.color = new Color(1, 1, 1, .85f); dot.raycastTarget = false;
            crosshairLabel = UIKit.Outlined(UIKit.Label(root, "", 20, UIKit.Paper, TextAnchor.MiddleCenter, true, "Look Label"), new Color(0, 0, 0, .7f), 2);
            crosshairLabel.raycastTarget = false;
            UIKit.Place(crosshairLabel.rectTransform, new Vector2(.5f, .5f), new Vector2(0, -44), new Vector2(420, 34));

            missionPanel = UIKit.Panel(root, new Color(.16f, .1f, .08f, .78f), 16, false, "Missions").rectTransform;
            UIKit.Place(missionPanel, new Vector2(1, 1), new Vector2(-14, -200), new Vector2(330, 112));
            missionLines = new Text[Missions.PerDay];
            for (int i = 0; i < missionLines.Length; i++)
            {
                missionLines[i] = UIKit.Label(missionPanel, "", 15, UIKit.Paper, TextAnchor.MiddleLeft, false, "Mission " + i);
                UIKit.Place(missionLines[i].rectTransform, new Vector2(0, 1), new Vector2(12, -8 - i * 33), new Vector2(310, 30));
            }
            var missionButton = missionPanel.gameObject.AddComponent<Button>();
            missionButton.onClick.AddListener(() => App.Open(GameScreen.Collection));

            var navImage = UIKit.Panel(root, new Color(.13f, .36f, .3f, .94f), 18, true, "Next Action");
            navPanel = UIKit.Place(navImage.rectTransform, new Vector2(.5f, 0), new Vector2(-100, 96), new Vector2(480, 44));
            navImage.gameObject.AddComponent<Button>().onClick.AddListener(() => { UIKit.ClickSound?.Invoke(); DoNextAction(); });
            navImage.gameObject.AddComponent<PressFeedback>();
            navIcon = UIKit.Icon(navPanel, IconKind.Info, UIKit.Gold);
            UIKit.Place(navIcon.rectTransform, new Vector2(0, .5f), new Vector2(12, 0), new Vector2(26, 26));
            navText = UIKit.Label(navPanel, "", 17, UIKit.Paper, TextAnchor.MiddleLeft, true, "Label");
            navText.resizeTextForBestFit = true; navText.resizeTextMinSize = 12; navText.resizeTextMaxSize = 17;
            UIKit.Fill(navText.rectTransform, 46, 12, 2, 2);

            var stick = UIKit.Panel(root, new Color(.1f, .07f, .05f, .35f), 70, false, "Virtual Joystick");
            stick.sprite = TextureFactory.Circle(128); stick.type = Image.Type.Simple;
            var srt = UIKit.Place(stick.rectTransform, new Vector2(S.leftHanded ? 1 : 0, 0), new Vector2(S.leftHanded ? -34 : 34, 30), new Vector2(150, 150));
            var thumb = UIKit.Panel(srt, new Color(1, .95f, .85f, .9f), 30, false, "Thumb");
            thumb.sprite = TextureFactory.Circle(128); thumb.type = Image.Type.Simple; thumb.raycastTarget = false;
            var trt = thumb.rectTransform; trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(.5f, .5f); trt.sizeDelta = new Vector2(62, 62);
            var joystick = stick.gameObject.AddComponent<VirtualJoystick>(); joystick.World = App.World; joystick.Thumb = trt;

            contextButton = UIKit.Button(root, "", () => { var near = App.World.NearestStation(out int target); if (near.HasValue) App.Interact(near.Value, target); }, ButtonStyle.Primary, 22, IconKind.Info, true, "Context");
            var crt = UIKit.Place(contextButton.GetComponent<RectTransform>(), new Vector2(S.leftHanded ? 0 : 1, 0), new Vector2(S.leftHanded ? 30 : -30, 30), new Vector2(120, 120));
            contextButton.GetComponent<Image>().sprite = TextureFactory.Circle(128);
            contextButton.GetComponent<Image>().type = Image.Type.Simple;
            var icon = contextButton.transform.Find("Icon").GetComponent<RectTransform>();
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(.5f, .5f); icon.anchoredPosition = new Vector2(0, 12); icon.sizeDelta = new Vector2(46, 46);
            contextLabel = contextButton.GetComponentInChildren<Text>();
            var lrt = contextLabel.rectTransform; lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0); lrt.pivot = new Vector2(.5f, 0);
            lrt.offsetMin = new Vector2(6, 14); lrt.offsetMax = new Vector2(-6, 40); contextLabel.fontSize = 17;

            summary = UIKit.Outlined(UIKit.Label(root, "", 18, UIKit.Paper, TextAnchor.MiddleRight, true, "Summary"), new Color(0, 0, 0, .6f));
            UIKit.Place(summary.rectTransform, new Vector2(S.leftHanded ? 0 : 1, 0), new Vector2(S.leftHanded ? 170 : -170, 100), new Vector2(330, 60));

            guide = UIKit.Panel(root, new Color(.99f, .96f, .88f, .97f), 18, true, "Tutorial").rectTransform;
            UIKit.Place(guide, new Vector2(0, 1), new Vector2(14, -200), new Vector2(460, 150));
            var badge = UIKit.Panel(guide, UIKit.Lacquer, 12, false, "Badge");
            UIKit.Place(badge.rectTransform, new Vector2(0, 1), new Vector2(12, -12), new Vector2(150, 30));
            guideTitle = UIKit.Label(badge.rectTransform, "", 16, UIKit.Paper, TextAnchor.MiddleCenter, true);
            UIKit.Fill(guideTitle.rectTransform);
            guideText = UIKit.Label(guide, "", 19, UIKit.Ink, TextAnchor.UpperLeft);
            guideText.resizeTextMinSize = 16;
            UIKit.Fill(guideText.rectTransform, 14, 14, 48, 8);
            guide.gameObject.AddComponent<PopIn>();
            finishTutorial = UIKit.Button(root, "初日を終える", () => Sim.FinishTutorial(), ButtonStyle.Primary, 22, IconKind.Check);
            UIKit.Place(finishTutorial.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(14, -362), new Vector2(260, 56));

            prepPanel = UIKit.Panel(root, UIKit.Paper, 22, true, "Prep").rectTransform;
            UIKit.Place(prepPanel, new Vector2(1, 1), new Vector2(-16, -90), new Vector2(330, 170));
            var prepTitle = UIKit.Label(prepPanel, "開店準備中", 24, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(prepTitle.rectTransform, new Vector2(0, 1), new Vector2(18, -10), new Vector2(300, 36));
            var prepText = UIKit.Label(prepPanel, "仕入れ・内装・スタッフを整えたら\n店を開けましょう。", 17, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(prepText.rectTransform, new Vector2(0, 1), new Vector2(18, -48), new Vector2(300, 50));
            var open = UIKit.Button(prepPanel, "店を開ける", () => App.Open(GameScreen.Morning), ButtonStyle.Primary, 22, IconKind.Play);
            UIKit.Place(open.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(294, 56));
        }

        public override void Refresh()
        {
            bool tutorial = !S.tutorialComplete && S.phase == DayPhase.Open;
            guide.gameObject.SetActive(tutorial);
            if (tutorial)
            {
                int step = Mathf.Clamp(S.tutorialStep, 0, TutorialSteps.Length - 1);
                guideTitle.text = "はじめての営業 " + (step + 1) + " / 12";
                guideText.text = TutorialSteps[step];
            }
            finishTutorial.gameObject.SetActive(tutorial && S.tutorialStep == RestaurantSimulation.Tutorial.Close);
            prepPanel.gameObject.SetActive(S.phase == DayPhase.Preparation);
            buildButton.interactable = Sim.CanManage && S.tutorialComplete;
            App.World.TutorialTarget = tutorial ? TutorialStation(S.tutorialStep) : null;
        }

        /// <summary>「次にやること」を実行する（その場所まで歩いて操作する）。</summary>
        private void DoNextAction()
        {
            var hint = NextAction.Suggest(Sim);
            switch (hint.Kind)
            {
                case NextActionKind.Clean: App.World.Visit(Station.Table, null, hint.Table); break;
                case NextActionKind.None: break;
                default: App.SelectOrder(hint.OrderId); break;
            }
        }

        private static Station NavStation(NextActionHint hint)
        {
            switch (hint.Kind)
            {
                case NextActionKind.Serve: return Station.Pass;
                case NextActionKind.Register: return Station.Register;
                case NextActionKind.Clean: return Station.Table;
                default: return Station.Kitchen;
            }
        }

        /// <summary>キーボードの近道（数字キーで注文票、N で次にやること、B／C／M／I で各画面）。</summary>
        private void HandleShortcuts()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.nKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame) { DoNextAction(); return; }
            for (int i = 0; i < 9; i++)
            {
                if (!keyboard[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame) continue;
                string id = App.UI.Hud?.TicketIds.ElementAtOrDefault(i);
                if (id != null) App.SelectOrder(id);
                return;
            }
            if (keyboard.cKey.wasPressedThisFrame) App.Open(GameScreen.Collection);
            else if (keyboard.mKey.wasPressedThisFrame) App.Open(GameScreen.Management);
            else if (keyboard.iKey.wasPressedThisFrame) App.Open(GameScreen.Shop);
            else if (keyboard.bKey.wasPressedThisFrame && buildButton.interactable) App.Open(GameScreen.Build);
        }

        private Station? TutorialStation(int step)
        {
            switch (step)
            {
                case RestaurantSimulation.Tutorial.Accept: return Station.Register;
                case RestaurantSimulation.Tutorial.Pick:
                case RestaurantSimulation.Tutorial.Weigh:
                case RestaurantSimulation.Tutorial.Heat:
                case RestaurantSimulation.Tutorial.Drain:
                case RestaurantSimulation.Tutorial.Shake:
                case RestaurantSimulation.Tutorial.Soup:
                case RestaurantSimulation.Tutorial.Spice: return Station.Kitchen;
                case RestaurantSimulation.Tutorial.Serve: return Station.Pass;
                case RestaurantSimulation.Tutorial.Clean: return Station.Table;
                default: return null;
            }
        }

        public override void Tick(float deltaTime)
        {
            var near = App.World.NearestStation(out int target);
            contextButton.gameObject.SetActive(near.HasValue && S.phase != DayPhase.Report);
            if (near.HasValue)
            {
                contextLabel.text = (Keyboard() ? S.interactKey + " " : "") + StationLabel(near.Value);
                contextButton.transform.Find("Icon").GetComponent<Image>().sprite = TextureFactory.Icon(StationIcon(near.Value));
            }
            bool fp = App.World.FirstPersonActive;
            crosshair.gameObject.SetActive(fp);
            crosshair.sizeDelta = Vector2.one * (App.World.LookTarget != null ? 18 : 12);
            var look = App.World.LookTarget;
            crosshairLabel.gameObject.SetActive(fp && look != null);
            if (fp && look != null) crosshairLabel.text = (Keyboard() ? S.interactKey + "：" : "タップ：") + StationLabel(look.Station);
            HandleShortcuts();
            bool showMissions = S.missions.Count > 0 && (S.phase == DayPhase.Open || S.phase == DayPhase.Closing);
            missionPanel.gameObject.SetActive(showMissions);
            if (showMissions)
            {
                // 一人称では視界を広く取るため、達成数だけの1行に縮める。
                missionPanel.sizeDelta = new Vector2(330, fp ? 40 : 112);
                for (int i = 0; i < missionLines.Length; i++)
                {
                    var m = i < S.missions.Count ? S.missions[i] : null;
                    missionLines[i].gameObject.SetActive(!fp || i == 0);
                    missionLines[i].text = fp && i == 0 ? "ミッション " + S.missions.Count(x => x.done) + " / " + S.missions.Count + " 達成"
                        : m == null ? "" : (m.done ? "■ " : "□ ") + Missions.Describe(m) + "  " + m.progress + "/" + m.target;
                    missionLines[i].color = !fp && m != null && m.done ? new Color(.6f, .95f, .6f) : UIKit.Paper;
                }
            }
            UpdateNavigation(deltaTime);
            var r = S.report;
            summary.text = S.phase == DayPhase.Preparation ? "" : "本日 " + r.served + "杯　" + RestaurantSimulation.Money(r.revenueFen + r.tipsFen)
                + (S.phase == DayPhase.Open && S.tutorialComplete ? "\n来客見込み " + Sim.Demand + "人（" + S.spawnedToday + "人来店）" : "");
        }

        private void UpdateNavigation(float deltaTime)
        {
            bool tutorial = !S.tutorialComplete;
            navRefresh -= deltaTime;
            if (navRefresh <= 0) { navRefresh = .25f; nextAction = tutorial ? default : NextAction.Suggest(Sim); }
            bool show = S.navigation && nextAction.Kind != NextActionKind.None;
            navPanel.gameObject.SetActive(show);
            if (!tutorial) App.World.TutorialTarget = show ? NavStation(nextAction) : (Station?)null;
            if (!show) return;
            navText.text = "次：" + nextAction.Label + (Keyboard() ? "　[N]" : "");
            if (navIcon != null) navIcon.sprite = TextureFactory.Icon(StationIcon(NavStation(nextAction)));
        }

        private static bool Keyboard() => UnityEngine.InputSystem.Keyboard.current != null && !Application.isMobilePlatform;

        public static string StationLabel(Station station)
        {
            switch (station)
            {
                case Station.Register: return "レジ";
                case Station.Fridge: return "冷蔵ケース";
                case Station.Kitchen: return "厨房";
                case Station.Pass: return "提供台";
                case Station.Table: return "片付け";
                default: return "入口";
            }
        }

        public static IconKind StationIcon(Station station)
        {
            switch (station)
            {
                case Station.Register: return IconKind.Bell;
                case Station.Fridge: return IconKind.Leaf;
                case Station.Kitchen: return IconKind.Pot;
                case Station.Pass: return IconKind.Bowl;
                case Station.Table: return IconKind.Broom;
                default: return IconKind.Home;
            }
        }
    }
}
