using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 店内で常に表示する情報（日付・時刻・天気・所持金・評判・店舗レベル）と注文票の列。
    /// 注文票は注文の増減時だけ作り直し、待ち時間ゲージや段階表示は毎フレーム更新する。
    /// </summary>
    public sealed class HudView : UIScreen
    {
        private Text dayText, clockText, moneyText, reputationText, levelText, phaseText, starsText, comboText;
        private RectTransform comboBadge;
        private Button viewButton;
        private bool shownFirstPerson;
        private Image clockFill, levelFill, weatherIcon;
        private RectTransform ticketRow;
        private string ticketSignature = "";
        private readonly List<(string id, Image patience, Text stage, Image badge)> tickets = new List<(string, Image, Text, Image)>();
        private long shownMoney;

        protected override void Build(RectTransform root)
        {
            var bar = UIKit.Panel(root, new Color(.16f, .1f, .08f, .9f), 20, true, "Top Bar");
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(.5f, 1);
            rt.offsetMin = new Vector2(12, -70); rt.offsetMax = new Vector2(-12, -8);

            var emblem = UIKit.Panel(rt, UIKit.Lacquer, 16, false, "Emblem");
            UIKit.Place(emblem.rectTransform, new Vector2(0, .5f), new Vector2(8, 0), new Vector2(46, 46));
            var bowl = UIKit.Icon(emblem.rectTransform, IconKind.Bowl, UIKit.Gold);
            UIKit.Fill(bowl.rectTransform, 7, 7, 7, 7);
            dayText = UIKit.Label(rt, "", 20, UIKit.Paper, TextAnchor.MiddleLeft, true, "Day");
            UIKit.Place(dayText.rectTransform, new Vector2(0, .5f), new Vector2(62, 10), new Vector2(200, 28));
            phaseText = UIKit.Label(rt, "", 15, UIKit.PaperDark, TextAnchor.MiddleLeft, false, "Phase");
            UIKit.Place(phaseText.rectTransform, new Vector2(0, .5f), new Vector2(62, -13), new Vector2(200, 22));

            weatherIcon = UIKit.Icon(rt, IconKind.Sun, UIKit.Gold, "Weather");
            UIKit.Place(weatherIcon.rectTransform, new Vector2(0, .5f), new Vector2(262, 0), new Vector2(34, 34));
            var clockBack = UIKit.Bar(rt, new Color(1, 1, 1, .12f), UIKit.Gold, out clockFill, 6);
            UIKit.Place(clockBack, new Vector2(0, .5f), new Vector2(304, -14), new Vector2(110, 8));
            clockText = UIKit.Label(rt, "", 22, UIKit.Paper, TextAnchor.MiddleLeft, true, "Clock");
            UIKit.Place(clockText.rectTransform, new Vector2(0, .5f), new Vector2(304, 7), new Vector2(120, 30));

            var coin = UIKit.Icon(rt, IconKind.Coin, UIKit.Gold, "Coin");
            UIKit.Place(coin.rectTransform, new Vector2(0, .5f), new Vector2(436, 0), new Vector2(30, 30));
            moneyText = UIKit.Label(rt, "", 22, UIKit.Paper, TextAnchor.MiddleLeft, true, "Money");
            UIKit.Place(moneyText.rectTransform, new Vector2(0, .5f), new Vector2(472, 0), new Vector2(170, 34));

            var star = UIKit.Icon(rt, IconKind.Star, UIKit.Gold, "Star");
            UIKit.Place(star.rectTransform, new Vector2(0, .5f), new Vector2(646, 0), new Vector2(28, 28));
            reputationText = UIKit.Label(rt, "", 20, UIKit.Paper, TextAnchor.MiddleLeft, true, "Reputation");
            UIKit.Place(reputationText.rectTransform, new Vector2(0, .5f), new Vector2(678, 0), new Vector2(70, 34));

            var badge = UIKit.Panel(rt, UIKit.Gold, 14, false, "Level Badge");
            UIKit.Place(badge.rectTransform, new Vector2(0, .5f), new Vector2(752, 0), new Vector2(76, 40));
            levelText = UIKit.Label(badge.rectTransform, "", 17, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Fill(levelText.rectTransform, 2, 2, 2, 10);
            var levelBack = UIKit.Bar(badge.rectTransform, new Color(0, 0, 0, .2f), UIKit.Lacquer, out levelFill, 4);
            levelBack.anchorMin = new Vector2(0, 0); levelBack.anchorMax = new Vector2(1, 0); levelBack.pivot = new Vector2(.5f, 0);
            levelBack.offsetMin = new Vector2(8, 5); levelBack.offsetMax = new Vector2(-8, 11);

            starsText = UIKit.Label(rt, "", 19, UIKit.Gold, TextAnchor.MiddleLeft, true, "Stars");
            UIKit.Place(starsText.rectTransform, new Vector2(0, .5f), new Vector2(840, 0), new Vector2(104, 34));
            comboBadge = UIKit.Panel(rt, UIKit.LacquerDark, 12, false, "Combo").rectTransform;
            UIKit.Place(comboBadge, new Vector2(0, .5f), new Vector2(946, 0), new Vector2(104, 38));
            var fire = UIKit.Icon(comboBadge, IconKind.Fire, UIKit.Gold);
            UIKit.Place(fire.rectTransform, new Vector2(0, .5f), new Vector2(6, 0), new Vector2(24, 24));
            comboText = UIKit.Label(comboBadge, "", 16, UIKit.Paper, TextAnchor.MiddleLeft, true);
            UIKit.Fill(comboText.rectTransform, 32, 4, 0, 0);

            viewButton = UIKit.Button(rt, "一人称", () => App.World.SetFirstPerson(!App.World.FirstPerson), ButtonStyle.Secondary, 16, IconKind.Person, true, "View");
            UIKit.Place(viewButton.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(-66, 0), new Vector2(122, 44));

            var menu = UIKit.Button(rt, "", () => { App.Paused = true; App.Open(GameScreen.Pause); }, ButtonStyle.Ghost, 24, IconKind.Menu, true, "Menu");
            UIKit.Place(menu.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(-6, 0), new Vector2(54, 50));
            menu.transform.Find("Icon").GetComponent<Image>().color = UIKit.Paper;

            ticketRow = UIKit.Rect("Tickets", root);
            ticketRow.anchorMin = new Vector2(0, 1); ticketRow.anchorMax = new Vector2(1, 1); ticketRow.pivot = new Vector2(0, 1);
            ticketRow.offsetMin = new Vector2(14, -192); ticketRow.offsetMax = new Vector2(-14, -80);
            var layout = ticketRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10; layout.childControlWidth = false; layout.childControlHeight = false; layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            shownMoney = S.moneyFen;
        }

        /// <summary>表示中の注文票の注文 ID（左から順。キーボードの数字キーで選ぶ）。</summary>
        public IEnumerable<string> TicketIds => tickets.Select(t => t.id);

        /// <summary>注文票を表示するか（調理画面などでは隠す）。</summary>
        public bool TicketsVisible { set { if (ticketRow != null) ticketRow.gameObject.SetActive(value); } }

        public override void Refresh()
        {
            var visible = VisibleOrders().ToList();
            string signature = string.Join("|", visible.Select(o => o.id + ":" + (int)o.stage + ":" + o.seat + ":" + o.handledByStaff));
            if (signature == ticketSignature) return;
            ticketSignature = signature;
            Clear(ticketRow);
            tickets.Clear();
            foreach (var order in visible.Take(8)) BuildTicket(order);
        }

        private IEnumerable<CustomerOrder> VisibleOrders()
            => Sim.Active.OrderBy(o => o.stage == OrderStage.Ready ? 0 : o.IsAtRegister ? 2 : 1).ThenBy(o => o.elapsed > 0 ? -o.elapsed : 0);

        private void BuildTicket(CustomerOrder order)
        {
            bool special = order.archetype == "critic" || order.archetype == "influencer" || order.archetype == "judge" || order.archetype == "celebrity";
            var paper = UIKit.Panel(ticketRow, special ? new Color(1, .93f, .74f) : UIKit.Paper, 12, true, "Ticket " + order.id);
            var rt = paper.rectTransform; rt.sizeDelta = new Vector2(156, 104);
            var button = paper.gameObject.AddComponent<Button>();
            string id = order.id;
            button.onClick.AddListener(() => { UIKit.ClickSound?.Invoke(); App.SelectOrder(id); });
            paper.gameObject.AddComponent<PressFeedback>();
            var stripe = UIKit.Panel(rt, StyleColor(order), 12, false, "Stripe");
            var srt = stripe.rectTransform; srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1); srt.pivot = new Vector2(.5f, 1);
            srt.sizeDelta = new Vector2(0, 26); srt.anchoredPosition = Vector2.zero;
            var tag = UIKit.Label(srt, StyleTag(order), 14, UIKit.Paper, TextAnchor.MiddleCenter, true);
            UIKit.Fill(tag.rectTransform, 4, 4, 0, 0);
            if (UnityEngine.InputSystem.Keyboard.current != null && !Application.isMobilePlatform)
            {
                var key = UIKit.Label(srt, (tickets.Count + 1).ToString(), 12, new Color(1, 1, 1, .7f), TextAnchor.MiddleLeft, true, "Key");
                UIKit.Fill(key.rectTransform, 8, 4, 0, 0);
            }
            var name = UIKit.Label(rt, order.customerName, 16, UIKit.Ink, TextAnchor.MiddleLeft, true, "Name");
            UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(10, -28), new Vector2(140, 24));
            var badge = UIKit.Icon(rt, StageIcon(order), UIKit.Lacquer, "Stage Icon");
            UIKit.Place(badge.rectTransform, new Vector2(0, 1), new Vector2(10, -54), new Vector2(22, 22));
            var stage = UIKit.Label(rt, "", 14, UIKit.InkSoft, TextAnchor.MiddleLeft, false, "Stage");
            UIKit.Place(stage.rectTransform, new Vector2(0, 1), new Vector2(36, -53), new Vector2(116, 24));
            var back = UIKit.Bar(rt, new Color(0, 0, 0, .1f), UIKit.Good, out var fill, 5);
            UIKit.Place(back, new Vector2(0, 0), new Vector2(10, 10), new Vector2(136, 9));
            paper.gameObject.AddComponent<PopIn>();
            tickets.Add((order.id, fill, stage, badge));
        }

        private static Color StyleColor(CustomerOrder o)
            => o.archetype == "critic" || o.archetype == "judge" ? UIKit.Ink : o.archetype == "vegetarian" ? new Color(.35f, .6f, .25f) : !string.IsNullOrEmpty(o.signatureId) ? UIKit.Gold : !string.IsNullOrEmpty(o.recipeRequest) ? new Color(.55f, .3f, .6f) : o.takeout ? UIKit.Warn : o.style == OrderStyle.SelfServe ? UIKit.Jade : UIKit.Lacquer;

        private static string StyleTag(CustomerOrder o)
            => o.archetype == "critic" ? "評論家" : o.archetype == "influencer" ? "配信者" : o.archetype == "judge" ? "審査員" : o.archetype == "vegetarian" ? "ベジ" : o.usual ? "いつもの"
            : !string.IsNullOrEmpty(o.signatureId) ? "名物" : !string.IsNullOrEmpty(o.recipeRequest) ? "レシピ" : !string.IsNullOrEmpty(o.regularId) ? "常連" : o.takeout ? "持ち帰り" : o.style == OrderStyle.SelfServe ? "セルフ" : "注文";

        public static IconKind StageIcon(CustomerOrder o)
        {
            switch (o.stage)
            {
                case OrderStage.Waiting: return IconKind.Bell;
                case OrderStage.Ordering: return IconKind.Coin;
                case OrderStage.Selecting: return IconKind.Leaf;
                case OrderStage.Weighed: return IconKind.Scale;
                case OrderStage.Heating: return IconKind.Pot;
                case OrderStage.Drained: return IconKind.Drop;
                case OrderStage.Finishing: return IconKind.Ladle;
                case OrderStage.Ready: return IconKind.Bowl;
                default: return IconKind.Check;
            }
        }

        public static string StageText(RestaurantSimulation sim, CustomerOrder o)
        {
            if (o.handledByStaff && o.stage != OrderStage.Ready) return "スタッフ調理中";
            switch (o.stage)
            {
                case OrderStage.Waiting: return "レジに並んでいます";
                case OrderStage.Ordering: return o.ticketConfirmed ? "お会計中" : "注文を聞いています";
                case OrderStage.Selecting: return o.paidFen > 0 ? "具材を取る" : o.style == OrderStyle.SelfServe ? "計量待ち" : "具材を取る";
                case OrderStage.Weighed: return "茹で待ち";
                case OrderStage.Heating: return "茹で中 " + Mathf.CeilToInt(sim.SecondsUntilDone(o)) + "秒";
                case OrderStage.Drained: return "湯切り";
                case OrderStage.Finishing: return "仕上げ中";
                case OrderStage.Ready: return o.takeout ? "レジで渡す" : o.seat < 0 ? "席の空き待ち" : "席へ運ぶ";
                default: return "";
            }
        }

        public override void Tick(float deltaTime)
        {
            var s = S;
            float minutes = 660 + s.daySeconds / RestaurantSimulation.DayDuration * 540;
            dayText.text = Seasons.Of(s.day).Name + "・" + s.day + "日目　" + Content.Weather(s.weather).Name;
            phaseText.text = App.Paused ? "一時停止中" : Phase(s);
            clockText.text = ((int)minutes / 60).ToString("00") + ":" + ((int)minutes % 60).ToString("00");
            UIKit.SetBar(clockFill, s.daySeconds / RestaurantSimulation.DayDuration);
            weatherIcon.sprite = TextureFactory.Icon(WeatherIcon(s.weather));
            shownMoney = (long)Mathf.Lerp(shownMoney, s.moneyFen, Mathf.Clamp01(deltaTime * 8));
            if (System.Math.Abs(shownMoney - s.moneyFen) < 50) shownMoney = s.moneyFen;
            moneyText.text = RestaurantSimulation.Money(shownMoney);
            reputationText.text = s.reputation.ToString("0");
            levelText.text = "Lv." + s.shopLevel;
            int stars = Progression.Stars(s);
            starsText.text = new string('★', stars) + new string('☆', 5 - stars);
            comboBadge.gameObject.SetActive(s.combo >= 2);
            comboText.text = s.combo + "連続";
            if (s.combo >= 5) comboBadge.localScale = Vector3.one * (1 + Mathf.Sin(Time.unscaledTime * 6) * .04f);
            bool fp = App.World.FirstPerson;
            if (fp != shownFirstPerson)
            {
                shownFirstPerson = fp;
                UIKit.SetLabel(viewButton, fp ? "見下ろし" : "一人称");
                viewButton.transform.Find("Icon").GetComponent<Image>().sprite = TextureFactory.Icon(fp ? IconKind.Home : IconKind.Person);
            }
            UIKit.SetBar(levelFill, Progression.LevelProgress(s));
            foreach (var (id, patience, stage, badge) in tickets)
            {
                var o = Sim.Find(id);
                if (o == null || patience == null) continue;
                float ratio = o.paidFen == 0 ? 1 - o.elapsed / Mathf.Max(1, o.patience) : 1 - o.elapsed / Mathf.Max(1, o.patience * 2);
                ratio = Mathf.Clamp01(ratio);
                UIKit.SetBar(patience, ratio);
                patience.color = ratio > .5f ? UIKit.Good : ratio > .25f ? UIKit.Warn : UIKit.Bad;
                stage.text = StageText(Sim, o);
                if (o.stage == OrderStage.Ready || (o.stage == OrderStage.Heating && Sim.SecondsUntilDone(o) <= 0))
                    badge.transform.localScale = Vector3.one * (1 + Mathf.Sin(Time.unscaledTime * 8) * .15f);
            }
        }

        public static IconKind WeatherIcon(string weather)
            => weather == "rain" ? IconKind.Rain : weather == "cloudy" ? IconKind.Cloud : weather == "cold" ? IconKind.Snow : weather == "hot" ? IconKind.Thermometer : IconKind.Sun;

        private static string Phase(GameState s)
            => s.phase == DayPhase.Preparation ? "開店準備" : s.phase == DayPhase.Open ? (s.tutorialComplete ? "営業中" : "練習営業") : s.phase == DayPhase.Closing ? "閉店処理" : "営業終了";
    }
}
