using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>経営画面。概要・設備・店主スキル・スタッフ・名物・常連・口コミ・実績のタブで店の成長を管理する。</summary>
    public sealed class ManagementScreen : UIScreen
    {
        public override bool Modal => true;
        private static readonly string[] TabLabels = { "概要", "設備", "スキル", "スタッフ", "名物", "常連", "口コミ", "実績" };
        /// <summary>表示中のタブ（画面を開き直しても前回のタブを保つ）。</summary>
        public static int tab;
        private RectTransform tabs, content;
        private string signature = "";

        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .35f);
            var body = UIKit.Card(root, "店舗経営", new Vector2(1100, 600), IconKind.Chart, () => App.BackToStore());
            tabs = UIKit.Rect("Tabs", body);
            tabs.anchorMin = new Vector2(0, 1); tabs.anchorMax = new Vector2(1, 1); tabs.pivot = new Vector2(.5f, 1);
            tabs.offsetMin = new Vector2(0, -46); tabs.offsetMax = Vector2.zero;
            var layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true;
            content = UIKit.Rect("Content", body);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one; content.offsetMin = Vector2.zero; content.offsetMax = new Vector2(0, -56);
            BuildTabs();
        }

        private void BuildTabs()
        {
            Clear(tabs);
            for (int i = 0; i < TabLabels.Length; i++)
            {
                int index = i;
                UIKit.Button(tabs, TabLabels[i], () => { tab = index; BuildTabs(); signature = ""; Refresh(); }, tab == i ? ButtonStyle.TabActive : ButtonStyle.Tab, 17);
            }
        }

        public override void Refresh()
        {
            string sig = string.Join(",", S.staff.Select(x => x.id + ":" + x.resting + ":" + x.praisedDay + ":" + x.mealDay + ":" + x.motivation + ":" + (int)x.fatigue))
                + ":" + string.Join(",", S.regulars.Select(x => x.id + ":" + x.affinity + ":" + x.answeredStory + ":" + x.concerned)) + ":" + tab + ":" + S.moneyFen + ":" + S.pricePer100gFen + ":" + S.staff.Count + ":" + string.Join(",", S.staff.Select(x => x.trainingLevel))
                + ":" + S.signatures.Count + ":" + S.pots + ":" + S.storageCapacity + ":" + string.Join(",", S.equipment.Select(e => e.level)) + ":" + S.storeName + ":" + S.achievements.Count + ":" + string.Join(",", S.skills.Select(x => x.count));
            if (sig == signature) return;
            signature = sig;
            Clear(content);
            switch (tab)
            {
                case 0: Overview(); break;
                case 1: Equipment(); break;
                case 2: SkillTree(); break;
                case 3: Staff(); break;
                case 4: Signatures(); break;
                case 5: Regulars(); break;
                case 6: Reviews(); break;
                default: Achievements(); break;
            }
        }

        private RectTransform Column(float x, float width)
        {
            var rt = UIKit.Rect("Column", content);
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(width, 0);
            return rt;
        }

        private void Stat(RectTransform parent, float y, string label, string value, float ratio = -1, Color? color = null)
        {
            var l = UIKit.Label(parent, label, 18, UIKit.InkSoft, TextAnchor.MiddleLeft);
            UIKit.Place(l.rectTransform, new Vector2(0, 1), new Vector2(0, -y), new Vector2(170, 34));
            var v = UIKit.Label(parent, value, 20, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place(v.rectTransform, new Vector2(0, 1), new Vector2(150, -y), new Vector2(ratio >= 0 ? 190 : 370, 34));
            if (ratio >= 0)
            {
                var bar = UIKit.Bar(parent, new Color(.85f, .82f, .78f), color ?? UIKit.Jade, out var fill, 6);
                UIKit.Place(bar, new Vector2(0, 1), new Vector2(350, -y - 12), new Vector2(160, 12));
                UIKit.SetBar(fill, ratio);
            }
        }

        private void Overview()
        {
            var left = Column(0, 520);
            int next = Content.ExperienceForLevel(Mathf.Min(Content.MaxShopLevel, S.shopLevel + 1));
            Stat(left, 0, "店舗レベル", "Lv." + S.shopLevel + (S.shopLevel >= Content.MaxShopLevel ? "（最大）" : "（あと" + (next - S.experience) + "）"), Progression.LevelProgress(S), UIKit.Gold);
            Stat(left, 42, "評判", S.reputation.ToString("0") + " / 100", S.reputation / 100f, UIKit.Lacquer);
            Stat(left, 84, "人気", S.popularity.ToString("0") + " / 100", S.popularity / 100f, UIKit.Jade);
            Stat(left, 126, "雰囲気", DiningLayout.Ambience(S) + " pt", Mathf.Clamp01(DiningLayout.Ambience(S) / 40f), UIKit.Gold);
            Stat(left, 168, "客席", DiningLayout.SeatCount(S) + " 席");
            Stat(left, 210, "来客見込み", Sim.Demand + " 人／日");
            Stat(left, 252, "固定費", RestaurantSimulation.Money(Sim.FixedCostFen + S.staff.Sum(x => Content.StaffFor(x.role).DailyWageFen)) + "／日");
            Stat(left, 294, "累計売上", RestaurantSimulation.Money(S.ingredients.settledRevenueFen));
            Stat(left, 336, "累計提供", S.stats.served + " 杯（会心 " + S.stats.perfectBowls + "）");

            var right = Column(560, 480);
            var priceTitle = UIKit.Label(right, "販売価格（100gあたり）", 20, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(priceTitle.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(480, 34));
            var price = UIKit.Label(right, RestaurantSimulation.Money(S.pricePer100gFen), 30, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Place(price.rectTransform, new Vector2(0, 1), new Vector2(110, -40), new Vector2(260, 50));
            var minus = UIKit.Button(right, "-1元", () => Sim.ChangePrice(-100), ButtonStyle.Secondary, 18, null, Sim.CanManage);
            UIKit.Place(minus.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, -42), new Vector2(100, 46));
            var plus = UIKit.Button(right, "+1元", () => Sim.ChangePrice(100), ButtonStyle.Secondary, 18, null, Sim.CanManage);
            UIKit.Place(plus.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(0, -42), new Vector2(100, 46));
            var note = UIKit.Label(right, "1杯（約250g）でおよそ " + RestaurantSimulation.Money(S.pricePer100gFen * 25 / 10) + "。\n値上げすると来客が減り、値下げすると増えます。" + (Sim.CanManage ? "" : "\n価格は営業前か閉店後に変更できます。"), 15, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(note.rectTransform, new Vector2(0, 1), new Vector2(0, -96), new Vector2(480, 70));

            var nameTitle = UIKit.Label(right, "店名", 20, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(nameTitle.rectTransform, new Vector2(0, 1), new Vector2(0, -176), new Vector2(480, 34));
            var input = InputField(right, S.storeName);
            UIKit.Place(input.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, -212), new Vector2(330, 46));
            var rename = UIKit.Button(right, "変更", () => Sim.RenameStore(input.text), ButtonStyle.Secondary, 17);
            UIKit.Place(rename.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(0, -212), new Vector2(130, 46));

            var wardrobe = UIKit.Button(right, "店主の着せ替え", () => App.Open(GameScreen.Wardrobe), ButtonStyle.Gold, 19, IconKind.Person);
            UIKit.Place(wardrobe.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, -282), new Vector2(480, 52));
            var unlocks = UIKit.Label(right, "次のレベルで解禁：" + (S.shopLevel >= Content.MaxShopLevel ? "なし" : string.Join("・", Progression.UnlocksAt(S.shopLevel + 1).DefaultIfEmpty("（評判アップ）"))), 15, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(unlocks.rectTransform, new Vector2(0, 1), new Vector2(0, -346), new Vector2(480, 60));
        }

        private InputField InputField(RectTransform parent, string value)
        {
            var bg = UIKit.Panel(parent, Color.white, 10, false, "Input");
            var field = bg.gameObject.AddComponent<InputField>();
            var text = UIKit.Label(bg.transform, "", 19, UIKit.Ink, TextAnchor.MiddleLeft);
            UIKit.Fill(text.rectTransform, 12, 12, 4, 4);
            text.supportRichText = false; text.resizeTextForBestFit = false; text.raycastTarget = false;
            field.textComponent = text; field.characterLimit = 256; field.text = value;
            field.onEndEdit.AddListener(v => field.SetTextWithoutNotify(PlayerText.Clean(v)));
            return field;
        }

        private RectTransform List()
        {
            var area = UIKit.Rect("List Area", content);
            UIKit.Fill(area);
            var list = UIKit.ScrollList(area, 10, 4);
            UIKit.Fill(list.parent.GetComponent<RectTransform>());
            return list;
        }

        private RectTransform Row(RectTransform list, float height, Color? color = null)
        {
            var row = UIKit.Panel(list, color ?? Color.white, 14, true, "Row");
            UIKit.Height(row, height);
            return row.rectTransform;
        }

        private void Equipment()
        {
            var list = List();
            foreach (var def in Content.Equipment)
            {
                var row = Row(list, 84);
                int level = S.EquipmentLevel(def.Id);
                var title = UIKit.Label(row, def.Name + "　Lv." + level + " / " + def.MaxLevel + "", 21, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(18, -8), new Vector2(560, 34));
                var desc = UIKit.Label(row, def.Description, 15, UIKit.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(desc.rectTransform, new Vector2(0, 0), new Vector2(18, 8), new Vector2(620, 30));
                long price = Sim.UpgradePrice(def.Id);
                int need = Sim.UpgradeRequiredLevel(def.Id);
                string id = def.Id;
                string label = price < 0 ? "最大レベル" : S.shopLevel < need ? "店舗Lv." + need + "で解禁" : "強化 " + RestaurantSimulation.Money(price);
                var buy = UIKit.Button(row, label, () => Sim.Upgrade(id), ButtonStyle.Primary, 17, IconKind.Hammer,
                    price >= 0 && S.shopLevel >= need && S.moneyFen >= price && Sim.CanManage && S.tutorialComplete);
                UIKit.Place(buy.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(-14, 0), new Vector2(260, 52));
            }
        }

        /// <summary>店主スキル。店舗レベルとグランプリの入賞でもらえるポイントで、いつでも覚えられる。</summary>
        private void SkillTree()
        {
            var list = List();
            var head = Row(list, 50, new Color(.99f, .94f, .8f));
            var points = UIKit.Label(head, "スキルポイント　残り " + Skills.Available(S) + "（店舗レベルが上がるたび +1、グランプリ入賞でも増える）", 18, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Fill(points.rectTransform, 18, 18, 0, 0);
            foreach (var def in Skills.All)
            {
                var row = Row(list, 84);
                int rank = Skills.Rank(S, def.Id);
                string pips = new string('●', rank) + new string('○', def.MaxRank - rank);
                var title = UIKit.Label(row, def.Name + "　" + pips + "　" + def.PerRank + "／段階", 21, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(18, -8), new Vector2(620, 34));
                var desc = UIKit.Label(row, def.Description, 15, UIKit.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(desc.rectTransform, new Vector2(0, 0), new Vector2(18, 8), new Vector2(620, 30));
                int cost = Skills.NextCost(S, def.Id);
                string id = def.Id;
                string label = cost < 0 ? "最大" : "覚える（" + cost + "pt）";
                var learn = UIKit.Button(row, label, () => { if (Skills.Learn(Sim, id)) App.Audio.Play(Sfx.LevelUp, .6f); }, ButtonStyle.Primary, 17, IconKind.Star,
                    cost >= 0 && Skills.Available(S) >= cost && S.tutorialComplete);
                UIKit.Place(learn.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(-14, 0), new Vector2(230, 52));
            }
        }

        private void Staff()
        {
            var list = List();
            foreach (var def in Content.Staff)
            {
                var role = def.Role;
                int count = S.staff.Count(x => x.role == role);
                var row = Row(list, 104);
                var title = UIKit.Label(row, def.Title + "　" + count + "/3人　日給 " + RestaurantSimulation.Money(def.DailyWageFen), 20, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(16, -6), new Vector2(600, 34));
                var desc = UIKit.Label(row, def.Description, 16, UIKit.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(desc.rectTransform, new Vector2(0, 1), new Vector2(16, -42), new Vector2(540, 48));
                var hire = UIKit.Button(row, "採用 " + RestaurantSimulation.Money(def.HireFeeFen), () => Sim.HireStaff(role), ButtonStyle.Primary, 17, IconKind.Staff,
                    Sim.CanManage && count < 3 && S.shopLevel >= def.UnlockLevel && S.moneyFen >= def.HireFeeFen);
                UIKit.Place(hire.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(-14, -10), new Vector2(230, 48));
                foreach (var member in S.staff.Where(x => x.role == role))
                {
                    var person = member;
                    var card = Row(list, 190, new Color(.93f, .98f, .92f));
                    string personality = member.personality == 0 ? "せっかち・作業が速い" : member.personality == 1 ? "丁寧・仕上げが得意" : "おしゃべり・場を和ませる";
                    var name = UIKit.Label(card, member.name + "　研修Lv." + member.trainingLevel + "　" + personality + "\n疲れ " + member.fatigue.ToString("0") + "　やる気 " + member.motivation.ToString("0") + (member.resting ? "　休憩中" : "　勤務中"), 18, UIKit.Ink, TextAnchor.MiddleLeft, true);
                    UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(16, -6), new Vector2(790, 64));
                    void Action(string label, int index, int line, System.Action action, bool enabled)
                    {
                        var button = UIKit.Button(card, label, action, ButtonStyle.Secondary, 16, null, enabled);
                        UIKit.Place(button.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(16 + index * 260, -78 - line * 50), new Vector2(250, 44));
                    }
                    Action(member.trainingLevel == 3 ? "研修完了" : "研修 " + RestaurantSimulation.Money(RestaurantSimulation.TrainingCost(member)), 0, 0, () => Sim.TrainStaff(person.id), Sim.CanManage && member.trainingLevel < 3 && S.moneyFen >= RestaurantSimulation.TrainingCost(member));
                    Action(member.resting ? "仕事に戻る" : "休ませる", 1, 0, () => Sim.RestStaff(person.id, !person.resting), true);
                    Action("褒める", 2, 0, () => Sim.PraiseStaff(person.id), member.praisedDay != S.day);
                    Action("まかない（古い在庫から1個）", 0, 1, () => {
                        var stock = S.stock.Where(x => x.quantity > 0).OrderByDescending(x => x.ageDays).FirstOrDefault();
                        if (stock != null) Sim.StaffMeal(person.id, stock.ingredientId);
                    }, Sim.CanManage && member.mealDay != S.day && S.stock.Any(x => x.quantity > 0));
                    Action("契約終了", 2, 1, () => Sim.FireStaff(person.id), Sim.CanManage);
                }
            }
        }

        private void Signatures()
        {
            var list = List();
            var hint = UIKit.Label(list, "品質75点以上で完成した一杯は、完成画面の「名物メニューに登録」で看板メニューにできます（最大3品）。名物は15%増しで売れ、よく売れるほど注文が増えます。", 16, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Height(hint, 56);
            if (S.signatures.Count == 0) { var none = UIKit.Label(list, "まだ名物メニューはありません。", 20, UIKit.Ink, TextAnchor.MiddleCenter); UIKit.Height(none, 80); }
            foreach (var dish in S.signatures)
            {
                var row = Row(list, 104);
                var title = UIKit.Label(row, "「" + dish.name + "」", 22, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
                UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(18, -8), new Vector2(600, 34));
                string detail = Content.Soup(dish.soup).Name + "・辛さ" + dish.spice + "　" + string.Join("・", dish.ingredients.Select(x => Sim.Catalog.Get(x).displayName));
                var desc = UIKit.Label(row, detail + "\n販売 " + dish.timesSold + "杯" + (dish.timesSold > 0 ? "　平均品質 " + (dish.qualitySum / dish.timesSold).ToString("0") + "点" : ""), 15, UIKit.InkSoft, TextAnchor.UpperLeft);
                UIKit.Place(desc.rectTransform, new Vector2(0, 1), new Vector2(18, -44), new Vector2(780, 56));
                string id = dish.id;
                var remove = UIKit.Button(row, "取り下げ", () => { Sim.RemoveSignature(id); signature = ""; Refresh(); }, ButtonStyle.Tab, 15);
                UIKit.Place(remove.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(-14, 0), new Vector2(130, 46));
            }
        }

        private void Regulars()
        {
            var list = List();
            if (S.regulars.Count == 0) { var none = UIKit.Label(list, "満足したお客さんは常連になってくれます。\n常連は好みを覚えていて、親密度が上がると色々な話をしてくれます。", 18, UIKit.InkSoft, TextAnchor.MiddleCenter); UIKit.Height(none, 120); }
            foreach (var regular in S.regulars.OrderByDescending(x => x.affinity))
            {
                var row = Row(list, 158);
                var title = UIKit.Label(row, regular.name + "さん　" + Content.Archetype(regular.archetype).Label + "・来店" + regular.visits + "回", 20, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(18, -6), new Vector2(520, 32));
                var heart = UIKit.Icon(row, IconKind.Heart, UIKit.Lacquer);
                UIKit.Place(heart.rectTransform, new Vector2(1, 1), new Vector2(-250, -10), new Vector2(24, 24));
                var bar = UIKit.Bar(row, new Color(.88f, .85f, .8f), UIKit.Lacquer, out var fill, 6);
                UIKit.Place(bar, new Vector2(1, 1), new Vector2(-20, -16), new Vector2(220, 12));
                UIKit.SetBar(fill, regular.affinity / 100f);
                string favorite = "好み：" + Content.Soup(regular.favoriteSoup).Name + "・辛さ" + regular.favoriteSpice + "・" + string.Join("・", regular.favorites.Take(3).Select(x => Sim.Catalog.Get(x).displayName));
                var story = regular.storyIndex > 0 ? "\n" + RegularStories.Line(regular) : "";
                var desc = UIKit.Label(row, favorite + story, 15, UIKit.InkSoft, TextAnchor.UpperLeft);
                UIKit.Place(desc.rectTransform, new Vector2(0, 1), new Vector2(18, -40), new Vector2(1000, 52));
                if (regular.storyIndex > regular.answeredStory)
                {
                    var person = regular;
                    var listen = UIKit.Button(row, "話を聞いて応援する", () => Sim.ReplyToRegular(person.id, true), ButtonStyle.Gold, 16, IconKind.Heart);
                    UIKit.Place(listen.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(18, 8), new Vector2(270, 44));
                    var later = UIKit.Button(row, "またゆっくり話そう", () => Sim.ReplyToRegular(person.id, false), ButtonStyle.Secondary, 16);
                    UIKit.Place(later.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(300, 8), new Vector2(270, 44));
                }
                else if (regular.lastVisitDay > 0 && S.day - regular.lastVisitDay >= 3)
                {
                    var person = regular;
                    var ask = UIKit.Button(row, regular.concerned ? "また会えるのを待っています" : "最近見ないけれど元気かな", () => Sim.AskAfterRegular(person.id), ButtonStyle.Secondary, 16, IconKind.Heart, !regular.concerned);
                    UIKit.Place(ask.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(18, 8), new Vector2(400, 44));
                }
            }
        }

        private void Reviews()
        {
            var list = List();
            var all = S.report.reviews.AsEnumerable().Reverse().Concat(S.recentReviews).Take(30).ToList();
            if (all.Count == 0) { var none = UIKit.Label(list, "まだ口コミはありません。", 20, UIKit.InkSoft, TextAnchor.MiddleCenter); UIKit.Height(none, 80); }
            foreach (string review in all)
            {
                var row = Row(list, 62);
                var text = UIKit.Label(row, review, 16, UIKit.Ink, TextAnchor.MiddleLeft);
                UIKit.Fill(text.rectTransform, 18, 18, 6, 6);
            }
        }

        private void Achievements()
        {
            var area = UIKit.Rect("Grid Area", content);
            UIKit.Fill(area);
            var grid = UIKit.ScrollGrid(area, new Vector2(340, 84), new Vector2(10, 10));
            UIKit.Fill(grid.parent.GetComponent<RectTransform>());
            foreach (var def in Content.Achievements)
            {
                bool done = S.achievements.Contains(def.Id);
                var card = UIKit.Panel(grid, done ? new Color(1, .95f, .78f) : new Color(.92f, .9f, .87f), 14, true, def.Name);
                var icon = UIKit.Icon(card.transform, done ? IconKind.Trophy : IconKind.Lock, done ? UIKit.Gold : UIKit.InkSoft);
                UIKit.Place(icon.rectTransform, new Vector2(0, .5f), new Vector2(12, 0), new Vector2(44, 44));
                var name = UIKit.Label(card.transform, def.Name, 18, done ? UIKit.Ink : UIKit.InkSoft, TextAnchor.MiddleLeft, true);
                UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(66, -8), new Vector2(264, 30));
                var desc = UIKit.Label(card.transform, def.Description, 14, UIKit.InkSoft, TextAnchor.UpperLeft);
                UIKit.Place(desc.rectTransform, new Vector2(0, 1), new Vector2(66, -40), new Vector2(264, 40));
            }
            var count = UIKit.Label(content, S.achievements.Count + " / " + Content.Achievements.Length, 16, UIKit.InkSoft, TextAnchor.MiddleRight);
            UIKit.Place(count.rectTransform, new Vector2(1, 0), new Vector2(0, -26), new Vector2(200, 24));
        }
    }
}
