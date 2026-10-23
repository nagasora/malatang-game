using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>朝の準備画面。天気・本日のイベント・来客見込み・在庫の注意を確認して開店する。</summary>
    public sealed class MorningScreen : UIScreen
    {
        private Text warnings, marketNote;

        /// <summary>進めている「店の物語」の章と、まだ達成していない目標をひとつ。</summary>
        private string ChapterNote()
        {
            var chapter = Chapters.Current(S);
            var goal = chapter?.Goals.FirstOrDefault(g => !g.Done(S));
            return goal == null ? "" : "　｜　" + chapter.Title.Split('　')[0] + "：" + goal.Label + " " + System.Math.Min(goal.Target, goal.Current(S)) + "/" + goal.Target;
        }

        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .3f);
            bool tutorial = !S.tutorialComplete;
            var body = UIKit.Card(root, Seasons.Of(S.day).Name + "・" + S.day + "日目の朝", new Vector2(760, 600), IconKind.Calendar, () => App.Open(GameScreen.Store));
            body.parent.gameObject.AddComponent<PopIn>();
            body.parent.GetComponent<Image>().color = new Color(.13f, .23f, .2f);
            var weather = Content.Weather(S.weather);
            var dailyEvent = Content.DailyEvent(S.dailyEvent);
            var wIcon = UIKit.Icon(body, HudView.WeatherIcon(S.weather), UIKit.Gold);
            UIKit.Place(wIcon.rectTransform, new Vector2(0, 1), new Vector2(0, 0), new Vector2(64, 64));
            var season = Seasons.Of(S.day);
            string featured = string.IsNullOrEmpty(S.featuredIngredient) ? "" : "　旬：" + App.Catalog.Get(S.featuredIngredient).displayName + "（仕入れ25%引き・よく頼まれる）";
            var wText = UIKit.Label(body, "天気：" + weather.Name + "　" + WeatherNote(weather) + "\n" + season.Name + "（" + Seasons.DayOfSeason(S.day) + "日目）" + featured + "", 19, UIKit.Paper, TextAnchor.MiddleLeft, true);
            UIKit.Place(wText.rectTransform, new Vector2(0, 1), new Vector2(76, -4), new Vector2(630, 56));
            var eventPanel = UIKit.Panel(body, new Color(.19f, .31f, .26f), 14, false, "Event");
            UIKit.Place(eventPanel.rectTransform, new Vector2(0, 1), new Vector2(0, -76), new Vector2(716, 92));
            var eTitle = UIKit.Label(eventPanel.transform, "本日：" + dailyEvent.Name, 21, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Place(eTitle.rectTransform, new Vector2(0, 1), new Vector2(16, -6), new Vector2(680, 34));
            var eText = UIKit.Label(eventPanel.transform, tutorial ? "今日は練習営業です。お客さんは1人だけ。案内に従って麻辣湯を作ってみましょう。" : dailyEvent.Description, 16, UIKit.PaperDark, TextAnchor.UpperLeft);
            UIKit.Place(eText.rectTransform, new Vector2(0, 1), new Vector2(16, -42), new Vector2(680, 46));
            var market = MorningMarket.Current(Sim);
            marketNote = UIKit.Label(body, market == null ? "朝市：3日目から希少食材が日替わりで届きます" : "朝市：" + market.displayName + "　限定5個・品質+3（朝のみ）", 16, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Place(marketNote.rectTransform, new Vector2(0, 1), new Vector2(0, -172), new Vector2(716, 30));
            var stats = UIKit.Label(body, tutorial ? "" : "来客見込み " + Sim.Demand + "人　客席 " + DiningLayout.SeatCount(S) + "席　スタッフ " + S.staff.Count + "人\n固定費・人件費 " + RestaurantSimulation.Money(Sim.FixedCostFen + S.staff.Sum(x => Content.StaffFor(x.role).DailyWageFen)) + "／日", 18, UIKit.Paper, TextAnchor.UpperLeft);
            UIKit.Place(stats.rectTransform, new Vector2(0, 1), new Vector2(0, -204), new Vector2(716, 50));
            int stars = Progression.Stars(S);
            string missions = S.missions.Count == 0 ? "" : "今日のミッション：" + string.Join("／", S.missions.Select(Missions.Describe));
            var goals = UIKit.Label(body, tutorial ? "" : "格付け " + new string('★', stars) + new string('☆', 5 - stars) + " " + Content.Rank(stars).Title + ChapterNote() + "\n" + missions, 16, UIKit.PaperDark, TextAnchor.UpperLeft);
            goals.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIKit.Place(goals.rectTransform, new Vector2(0, 1), new Vector2(0, -256), new Vector2(716, 62));
            warnings = UIKit.Label(body, "", 16, new Color(1, .62f, .4f), TextAnchor.UpperLeft);
            UIKit.Place(warnings.rectTransform, new Vector2(0, 1), new Vector2(0, -320), new Vector2(716, 52));

            var row = UIKit.Rect("Buttons", body);
            row.anchorMin = new Vector2(0, 0); row.anchorMax = new Vector2(1, 0); row.pivot = new Vector2(.5f, 0);
            row.offsetMin = new Vector2(0, 72); row.offsetMax = new Vector2(0, 122);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true;
            UIKit.Button(row, "朝市", () => { ShopScreen.StartAtMarket = true; App.Open(GameScreen.Shop); }, ButtonStyle.Gold, 18, IconKind.Cart, market != null);
            UIKit.Button(row, "仕入れ", () => App.Open(GameScreen.Shop), ButtonStyle.Secondary, 18, IconKind.Cart);
            UIKit.Button(row, "商店街", () => App.Open(GameScreen.Neighbourhood), ButtonStyle.Secondary, 18, IconKind.Heart, !tutorial);
            UIKit.Button(row, "仕込み", () => App.Open(GameScreen.Preparation), ButtonStyle.Gold, 18, IconKind.Pot, !tutorial);
            UIKit.Button(row, "経営", () => App.Open(GameScreen.Management), ButtonStyle.Secondary, 18, IconKind.Chart, !tutorial);
            UIKit.Button(row, "内装", () => App.Open(GameScreen.Build), ButtonStyle.Secondary, 18, IconKind.Hammer, !tutorial);
            UIKit.Button(row, "着せ替え", () => App.Open(GameScreen.Wardrobe), ButtonStyle.Secondary, 18, IconKind.Person);
            var open = UIKit.Button(body, tutorial ? "練習営業を始める" : "営業開始！", () => { if (Sim.BeginDay()) App.Open(S.festivalStall ? GameScreen.Stall : GameScreen.Store); }, ButtonStyle.Primary, 24, IconKind.Play);
            var ort = open.GetComponent<RectTransform>();
            ort.anchorMin = new Vector2(0, 0); ort.anchorMax = new Vector2(1, 0); ort.pivot = new Vector2(.5f, 0); ort.offsetMin = Vector2.zero; ort.offsetMax = new Vector2(0, 62);
        }

        private static string WeatherNote(WeatherDefinition w)
        {
            var notes = new List<string>();
            if (w.DemandMultiplier > 1.05f) notes.Add("客足↑");
            if (w.DemandMultiplier < .95f) notes.Add("客足↓");
            if (w.SpiceShift > 0) notes.Add("辛いもの人気");
            if (w.SpiceShift < 0) notes.Add("あっさり人気");
            if (!string.IsNullOrEmpty(w.FavoredSoup)) notes.Add(Content.Soup(w.FavoredSoup).Name + "スープが人気");
            if (w.TakeoutBonus > 0) notes.Add("持ち帰り増");
            return string.Join("・", notes);
        }

        public override void Refresh()
        {
            var market = MorningMarket.Current(Sim);
            if (marketNote != null && market != null) marketNote.text = "朝市：" + market.displayName + (S.marketPurchased ? "　本日売切れ" : "　限定5個・品質+3（朝のみ）");
            var lines = new List<string>();
            var empty = Sim.Unlocks.Available.Where(x => Sim.StockFor(x.id).quantity == 0).Select(x => x.displayName).ToList();
            if (empty.Count > 0) lines.Add("在庫切れ：" + string.Join("・", empty.Take(8)) + (empty.Count > 8 ? " ほか" + (empty.Count - 8) + "種" : ""));
            var spoiling = Sim.Unlocks.Available.Where(x => Sim.StockFor(x.id).quantity > 0 && Sim.StockFor(x.id).ageDays + 1 >= x.freshnessDays + Sim.FreshnessBonusDays).Select(x => x.displayName).ToList();
            if (spoiling.Count > 0) lines.Add("今日で鮮度切れ：" + string.Join("・", spoiling.Take(8)));
            if (S.moneyFen < Sim.MinimumPurchaseFen) lines.Add("資金が少なくなっています。仕入れ画面の「再建支援」を利用できます。");
            warnings.text = string.Join("\n", lines);
        }
    }

    /// <summary>営業レポート。売上・費用・利益を数え上げ、ハイライトと口コミ、経験値を表示する。</summary>
    public sealed class ReportScreen : UIScreen
    {
        public override bool Modal => true;
        private Text numbers;
        private float reveal;
        private string[] lines;
        private int shownLineCount = -1;
        private long shownRevenue = long.MinValue, shownProfit = long.MinValue;
        private string revenueSuffix;

        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .4f);
            var r = S.report;
            var body = UIKit.Card(root, S.day + "日目の営業レポート", new Vector2(1060, 600), IconKind.Chart, null);
            body.parent.gameObject.AddComponent<PopIn>();
            lines = new[]
            {
                "売上　　　　" + RestaurantSimulation.MoneyOrDash(r.revenueFen) + (r.discountsFen > 0 ? "（値引き -" + RestaurantSimulation.Money(r.discountsFen) + "）" : ""),
                "チップ　　　" + RestaurantSimulation.MoneyOrDash(r.tipsFen),
                "報酬・賞金　" + RestaurantSimulation.MoneyOrDash(r.missionRewardsFen + r.prizeFen),
                "食材原価　　" + RestaurantSimulation.MoneyOrDash(-r.foodCostFen),
                "固定費　　　" + RestaurantSimulation.MoneyOrDash(-r.fixedCostFen),
                "人件費　　　" + RestaurantSimulation.MoneyOrDash(-r.wagesFen),
                "おつり過払い " + RestaurantSimulation.MoneyOrDash(-r.changeLossFen),
                "営業利益　　" + RestaurantSimulation.Money(r.ProfitFen) + "",
                "",
                "仕入れ支出　" + RestaurantSimulation.Money(r.purchasesFen) + "　設備・内装 " + RestaurantSimulation.Money(r.investmentFen),
                "提供 " + r.served + "杯　取消 " + r.lost + "件　平均品質 " + r.AverageQuality.ToString("0") + "点　会心 " + r.perfect + "杯",
                "完食 " + r.finishedFood + "/" + r.mealsFinished + " 杯・完飲 " + r.finishedSoup + " 杯",
                "最多の残し: " + MealLeftovers.DailyMostLeft(r, App.Simulation.Catalog),
                "最高コンボ " + r.bestCombo + "　おつりぴったり " + r.exactChange + "回　不満 " + r.complaints + "件　ミッション " + S.missions.Count(m => m.done) + "/" + S.missions.Count,
                "評判 " + (r.reputationDelta >= 0 ? "+" : "") + r.reputationDelta + "　経験値 +" + r.experience + "　所持金 " + RestaurantSimulation.Money(S.moneyFen),
            };
            revenueSuffix = r.discountsFen > 0 ? "（値引き -" + RestaurantSimulation.Money(r.discountsFen) + "）" : "";
            UIKit.LedgerLines(body, 19, 454);
            numbers = UIKit.Label(body, "", 19, UIKit.Ink, TextAnchor.UpperLeft);
            numbers.rectTransform.anchorMin = new Vector2(0, 0); numbers.rectTransform.anchorMax = new Vector2(.47f, 1);
            numbers.rectTransform.offsetMin = new Vector2(0, 80); numbers.rectTransform.offsetMax = new Vector2(-10, 0);
            numbers.resizeTextForBestFit = false; numbers.lineSpacing = .88f;

            var right = UIKit.Rect("Right", body);
            right.anchorMin = new Vector2(.47f, 0); right.anchorMax = Vector2.one; right.offsetMin = new Vector2(10, 76); right.offsetMax = Vector2.zero;
            BuildGrade(right);
            BuildChart(right);
            var hTitle = UIKit.Label(right, "今日のできごと", 18, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(hTitle.rectTransform, new Vector2(0, 1), new Vector2(0, -226), new Vector2(530, 26));
            var highlights = r.highlights.Count > 0 ? string.Join("\n", r.highlights.Take(4)) : "穏やかな一日でした。";
            var hText = UIKit.Label(right, highlights, 15, UIKit.Ink, TextAnchor.UpperLeft);
            UIKit.Place(hText.rectTransform, new Vector2(0, 1), new Vector2(0, -252), new Vector2(r.bestBowl == null ? 530 : 350, 84));
            if (r.bestBowl != null && r.bestBowl.ingredients.Count > 0)
            {
                var photo = UIKit.Rect("Best Bowl Photo", right).gameObject.AddComponent<RawImage>();
                photo.texture = BowlPortrait.Get(r.bestBowl);
                UIKit.Place(photo.rectTransform, new Vector2(1, 0), new Vector2(0, 0), new Vector2(156, 156));
                var caption = UIKit.Label(photo.transform, "今日の一杯・" + r.bestBowl.quality + "点", 15, UIKit.Lacquer, TextAnchor.LowerCenter, true);
                UIKit.Fill(caption.rectTransform);
            }
            var reviews = r.reviews.Count > 0 ? r.reviews[r.reviews.Count - 1] : "—";
            var rText = UIKit.Label(right, "お客さんの声　" + reviews, 14, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(rText.rectTransform, new Vector2(0, 1), new Vector2(0, -340), new Vector2(r.bestBowl == null ? 530 : 350, 82));

            var row = UIKit.Rect("Buttons", body);
            row.anchorMin = new Vector2(0, 0); row.anchorMax = new Vector2(1, 0); row.pivot = new Vector2(.5f, 0);
            row.offsetMin = Vector2.zero; row.offsetMax = new Vector2(0, 62);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true;
            bool tutorial = !S.tutorialComplete;
            UIKit.Button(row, "仕入れ", () => App.Open(GameScreen.Shop), tutorial ? ButtonStyle.Primary : ButtonStyle.Secondary, 20, IconKind.Cart);
            UIKit.Button(row, "経営", () => App.Open(GameScreen.Management), ButtonStyle.Secondary, 20, IconKind.Chart, !tutorial);
            UIKit.Button(row, "内装", () => App.Open(GameScreen.Build), ButtonStyle.Secondary, 20, IconKind.Hammer, !tutorial);
            var next = UIKit.Button(row, App.InChallenge ? "通常の店舗へ・記録する" : tutorial ? "仕入れをしてから翌日へ" : "翌日へ", () => { if (App.InChallenge) App.LeaveChallenge(); else if (Sim.NextDay()) App.Open(GameScreen.Morning); }, tutorial ? ButtonStyle.Secondary : ButtonStyle.Primary, 20, IconKind.ArrowRight, !tutorial);
            App.Audio.Play(Sfx.CoinJingle, .7f);
        }

        /// <summary>今日の評価（S〜D）の大きな札と、満足度・取りこぼしの要約。</summary>
        private void BuildGrade(RectTransform right)
        {
            var today = S.history.LastOrDefault(h => h.day == S.day);
            string grade = today?.grade ?? "-";
            Color color = grade == "S" ? UIKit.Gold : grade == "A" ? UIKit.Lacquer : grade == "B" ? UIKit.Jade : UIKit.InkSoft;
            var badge = UIKit.Panel(right, color, 18, true, "Grade");
            UIKit.Place(badge.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(84, 84));
            var letter = UIKit.Label(badge.rectTransform, grade, 54, UIKit.Paper, TextAnchor.MiddleCenter, true);
            UIKit.Fill(letter.rectTransform);
            badge.rectTransform.localRotation = Quaternion.Euler(0, 0, -7);
            badge.gameObject.AddComponent<PopIn>();
            string comment = grade == "S" ? "最高の一日！お客さんみんなが笑顔で帰りました。"
                : grade == "A" ? "とてもいい営業でした。" : grade == "B" ? "まずまずの一日。もうひと工夫できそう。"
                : grade == "C" ? "待たせたり、不満が出たりした一日でした。" : grade == "D" ? "明日は立て直しましょう。" : "この日は評価の記録がありません。";
            var text = UIKit.Label(right, "本日の評価\n" + comment + (today != null ? "\n満足度 平均" + today.satisfaction.ToString("0") + "点　取りこぼし " + today.lost + "人" : ""), 16, UIKit.Ink, TextAnchor.UpperLeft);
            UIKit.Place(text.rectTransform, new Vector2(0, 1), new Vector2(98, 0), new Vector2(440, 84));
        }

        /// <summary>直近7日の利益の棒グラフ（今日を強調、赤字は下向き）。</summary>
        private void BuildChart(RectTransform right)
        {
            var days = S.history.Skip(System.Math.Max(0, S.history.Count - 7)).ToList();
            var frame = UIKit.Panel(right, UIKit.PaperDark, 14, false, "Chart");
            var rt = UIKit.Place(frame.rectTransform, new Vector2(0, 1), new Vector2(0, -94), new Vector2(530, 124));
            var title = UIKit.Label(rt, "直近7日の営業利益" + (days.Count < 7 ? "（あと" + (7 - days.Count) + "日で1週間）" : ""), 14, UIKit.InkSoft, TextAnchor.MiddleLeft, true);
            UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(10, -2), new Vector2(300, 22));
            for (int i = days.Count; i < 7; i++)
            {
                var ghost = UIKit.Panel(rt, new Color(.35f, .3f, .25f, .1f), 4, false, "Future Day " + i);
                UIKit.Place(ghost.rectTransform, new Vector2(0, 0), new Vector2(14 + i * (510f / 7) + 14, 22), new Vector2(36, 64));
            }
            if (days.Count == 0) return;
            long max = System.Math.Max(1, days.Max(d => System.Math.Abs(d.profitFen)));
            bool anyLoss = days.Any(d => d.profitFen < 0);
            float baseY = anyLoss ? 52 : 22, height = anyLoss ? 34 : 64, slot = 510f / 7;
            for (int i = 0; i < days.Count; i++)
            {
                var d = days[i];
                float h = System.Math.Max(2, height * System.Math.Abs(d.profitFen) / (float)max);
                bool today = d.day == S.day;
                var bar = UIKit.Panel(rt, d.profitFen < 0 ? UIKit.Bad : today ? UIKit.Lacquer : UIKit.Jade, 4, false, "Bar " + d.day);
                float y = d.profitFen < 0 ? baseY - h : baseY;
                UIKit.Place(bar.rectTransform, new Vector2(0, 0), new Vector2(14 + i * slot + slot * .2f, y), new Vector2(slot * .5f, h));
                var label = UIKit.Label(rt, d.day + "日 " + d.grade, 11, today ? UIKit.Lacquer : UIKit.InkSoft, TextAnchor.MiddleCenter, today);
                UIKit.Place(label.rectTransform, new Vector2(0, 0), new Vector2(4 + i * slot, 2), new Vector2(slot + 10, 16));
            }
        }

        public override void Tick(float deltaTime)
        {
            reveal += deltaTime;
            float progress = S.reducedMotion ? 1 : Mathf.SmoothStep(0, 1, reveal / 1.1f);
            int count = S.reducedMotion ? lines.Length : Mathf.Min(lines.Length, Mathf.FloorToInt(reveal * 18));
            long revenue = progress >= 1 ? S.report.revenueFen : (long)(S.report.revenueFen * (double)progress);
            long profit = progress >= 1 ? S.report.ProfitFen : (long)(S.report.ProfitFen * (double)progress);
            if (revenue == shownRevenue && profit == shownProfit && count == shownLineCount) return;
            shownRevenue = revenue; shownProfit = profit; shownLineCount = count;
            lines[0] = "売上　　　　" + RestaurantSimulation.MoneyOrDash(revenue) + revenueSuffix;
            lines[7] = "営業利益　　" + RestaurantSimulation.MoneyOrDash(profit);
            numbers.text = string.Join("\n", lines, 0, count);
        }
    }

    /// <summary>店主の着せ替え。固定の決定ボタンと縦スクロールで横画面に項目を収める。</summary>
    public sealed class WardrobeScreen : UIScreen
    {
        private CharacterAppearance draft;
        private readonly List<Text> values = new List<Text>();
        private static readonly (string label, int count)[] Options =
        {
            ("髪型", 11), ("髪の色", 6), ("肌の色", 5), ("服装", 13), ("服の色", 10),
            ("エプロン", 6), ("帽子", 4), ("小物", 5), ("眼鏡", 3), ("体型", 4),
            ("目", 5), ("眉", 3), ("ひげ", 4), ("靴の色", 6), ("そばかす", 2), ("ボトムの色", 6)
        };
        private static readonly string[] HairNames = { "短髪", "お団子", "ボブ", "ポニーテール", "坊主", "長髪", "ツインテール", "ツンツン", "パーマ", "七三分け", "ロングウェーブ" };
        private static readonly string[] HatNames = { "なし", "コック帽", "バンダナ", "キャップ" };
        private static readonly string[] AccessoryNames = { "なし", "眼鏡", "帽子", "ヘッドホン", "マフラー" };
        private static readonly string[] OutfitNames = { "普段着", "スーツ", "パーカー", "作業着", "スクラブ", "ジャージ", "ワンピース", "学生服", "配達ジャケット", "アロハ", "カーディガン", "ジャケット", "フォーマル" };
        private static readonly string[] EyewearNames = { "なし", "眼鏡", "サングラス" };
        private static readonly string[] BuildNames = { "標準", "がっしり", "細身", "ふくよか" };
        private static readonly string[] EyeNames = { "丸目", "たれ目", "つり目", "細目", "まつげ" };
        private static readonly string[] BrowNames = { "やわらか", "太い眉", "きりっと" };
        private static readonly string[] BeardNames = { "なし", "口ひげ", "あごひげ", "無精ひげ" };
        private static readonly string[] ShoeNames = { "墨色", "茶", "赤", "青", "クリーム", "緑" };

        /// <summary>右側のパネルにスクロール可能な変更行と常時見える操作ボタンを作る。</summary>
        protected override void Build(RectTransform root)
        {
            draft = S.chef.Clone();
            values.Clear();
            App.World.FocusPlayer(true);
            RectTransform panel = UIKit.Panel(root, UIKit.Paper, 22, true, "Wardrobe").rectTransform;
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1); panel.pivot = new Vector2(1, .5f);
            panel.offsetMin = new Vector2(-470, 12); panel.offsetMax = new Vector2(-12, -80);
            Text title = UIKit.Label(panel, "店主の着せ替え", 26, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(20, -10), new Vector2(420, 40));
            RectTransform content = UIKit.ScrollList(panel, 5, 4, "WardrobeOptions");
            UIKit.Fill((RectTransform)content.parent, 12, 12, 58, 84);
            for (int i = 0; i < Options.Length; i++)
                BuildOptionRow(content, i);
            Button done = UIKit.Button(panel, "決定", () => { Sim.SetChefAppearance(draft); Back(); }, ButtonStyle.Primary, 22, IconKind.Check);
            UIKit.Place(done.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(110, 16), new Vector2(200, 56));
            Button cancel = UIKit.Button(panel, "やめる", () => { App.World.PreviewChef(S.chef); Back(); }, ButtonStyle.Tab, 20);
            UIKit.Place(cancel.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(-110, 16), new Vector2(200, 56));
        }

        /// <summary>既存と同じ左右矢印の変更行をスクロール内容に追加する。</summary>
        private void BuildOptionRow(RectTransform content, int index)
        {
            RectTransform row = UIKit.Rect("Option", content);
            LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 44f;
            layout.preferredHeight = 44f;
            Text label = UIKit.Label(row, Options[index].label, 18, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place(label.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(110, 44));
            Button prev = UIKit.Button(row, "", () => Change(index, -1), ButtonStyle.Secondary, 18, IconKind.ArrowLeft);
            UIKit.Place(prev.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(116, 0), new Vector2(44, 44));
            Text value = UIKit.Label(row, "", 16, UIKit.Ink, TextAnchor.MiddleCenter);
            value.rectTransform.anchorMin = new Vector2(0, 0);
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = new Vector2(164, 0);
            value.rectTransform.offsetMax = new Vector2(-48, 0);
            values.Add(value);
            Button next = UIKit.Button(row, "", () => Change(index, 1), ButtonStyle.Secondary, 18, IconKind.ArrowRight);
            UIKit.Place(next.GetComponent<RectTransform>(), new Vector2(1, 1), Vector2.zero, new Vector2(44, 44));
        }

        /// <summary>店主へのカメラ注視を戻して元の画面に移る。</summary>
        private void Back()
        {
            App.World.FocusPlayer(false);
            if (S.phase == DayPhase.Preparation) App.Open(GameScreen.Morning); else App.BackToStore();
        }

        /// <summary>保存データの負値も有効な番号へ戻す。</summary>
        private static int Normalize(int value, int count)
        {
            int remainder = value % count;
            return remainder < 0 ? remainder + count : remainder;
        }

        /// <summary>選択項目を変更して、保存せずにモデルをプレビューする。</summary>
        private void Change(int option, int delta)
        {
            /// <summary>番号を正規化してから小さな変更量を加える。</summary>
            int Wrap(int value, int count) => Normalize(Normalize(value, count) + delta, count);
            switch (option)
            {
                case 0: draft.hairStyle = Wrap(draft.hairStyle, 11); break;
                case 1: draft.hairColor = Wrap(draft.hairColor, 6); break;
                case 2: draft.skinTone = Wrap(draft.skinTone, 5); break;
                case 3: draft.outfit = Wrap(draft.outfit, 13); break;
                case 4: draft.topColor = Wrap(draft.topColor, 10); break;
                case 5: draft.apronColor = Wrap(draft.apronColor, 6); break;
                case 6: draft.hatStyle = Wrap(draft.hatStyle, 4); break;
                case 7: draft.accessory = Wrap(draft.accessory, 5); break;
                case 8: draft.eyewear = Wrap(draft.eyewear, 3); break;
                case 9: draft.build = Wrap(draft.build, 4); break;
                case 10: draft.eyeStyle = Wrap(draft.eyeStyle, 5); break;
                case 11: draft.browStyle = Wrap(draft.browStyle, 3); break;
                case 12: draft.facialHair = Wrap(draft.facialHair, 4); break;
                case 13: draft.shoeColor = Wrap(draft.shoeColor, 6); break;
                case 14: draft.freckles = !draft.freckles; break;
                case 15: draft.bottomColor = Wrap(draft.bottomColor, 6); break;
            }
            draft.apron = true;
            App.World.PreviewChef(draft);
            Refresh();
        }

        /// <summary>選択状態を正規化して各行の名前とパレット番号を表示する。</summary>
        public override void Refresh()
        {
            values[0].text = HairNames[Normalize(draft.hairStyle, 11)];
            values[1].text = (Normalize(draft.hairColor, 6) + 1) + " / 6";
            values[2].text = (Normalize(draft.skinTone, 5) + 1) + " / 5";
            values[3].text = OutfitNames[Normalize(draft.outfit, 13)];
            values[4].text = (Normalize(draft.topColor, 10) + 1) + " / 10";
            values[5].text = (Normalize(draft.apronColor, 6) + 1) + " / 6";
            values[6].text = HatNames[Normalize(draft.hatStyle, 4)];
            values[7].text = AccessoryNames[Normalize(draft.accessory, 5)];
            values[8].text = EyewearNames[Normalize(draft.eyewear, 3)];
            values[9].text = BuildNames[Normalize(draft.build, 4)];
            values[10].text = EyeNames[Normalize(draft.eyeStyle, 5)];
            values[11].text = BrowNames[Normalize(draft.browStyle, 3)];
            values[12].text = BeardNames[Normalize(draft.facialHair, 4)];
            values[13].text = ShoeNames[Normalize(draft.shoeColor, 6)];
            values[14].text = draft.freckles ? "あり" : "なし";
            values[15].text = (Normalize(draft.bottomColor, 6) + 1) + " / 6";
        }
    }
}
