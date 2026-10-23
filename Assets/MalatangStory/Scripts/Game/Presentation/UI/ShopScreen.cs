using System.Linq;
using MalatangStory.Ingredients;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>仕入れ画面。カテゴリー別の食材カード、在庫・鮮度、5個／10個の購入、おまかせ補充、次の解禁を表示する。</summary>
    public sealed class ShopScreen : UIScreen
    {
        public override bool Modal => true;
        private static readonly string[] CategoryIds = { "market", "all", "vegetable", "mushroom", "noodle", "soy", "meat", "seafood", "processed", "other" };
        private static readonly string[] CategoryLabels = { "朝市", "全て", "野菜", "きのこ", "麺類", "大豆", "肉類", "魚介", "加工品", "その他" };
        private static string category = "all";
        /// <summary>次回表示時に朝市区分を開くための指定。</summary>
        public static bool StartAtMarket;
        private RectTransform grid, tabs;
        private Text header, unlockText;
        private Image unlockFill;
        private string signature = "";

        protected override void Build(RectTransform root)
        {
            if (StartAtMarket) { category = "market"; StartAtMarket = false; }
            UIKit.Dim(root, .35f);
            var body = UIKit.Card(root, "食材の仕入れ", new Vector2(1180, 600), IconKind.Cart, () => App.BackToStore());
            header = UIKit.Label(body, "", 17, UIKit.InkSoft, TextAnchor.MiddleLeft);
            UIKit.Place(header.rectTransform, new Vector2(0, 1), new Vector2(150, 0), new Vector2(620, 40));
            var restock = UIKit.Button(body, "おまかせ補充（各15個まで）", () => Sim.RestockAll(15), ButtonStyle.Gold, 17, IconKind.Sparkle, Sim.CanManage);
            UIKit.Place(restock.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(0, 0), new Vector2(300, 42));
            tabs = UIKit.Rect("Tabs", body);
            tabs.anchorMin = new Vector2(0, 0); tabs.anchorMax = new Vector2(0, 1); tabs.pivot = new Vector2(0, 1);
            tabs.offsetMin = new Vector2(0, 60); tabs.offsetMax = new Vector2(136, -50);
            var layout = tabs.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = true;
            var area = UIKit.Rect("Area", body);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = new Vector2(150, 60); area.offsetMax = new Vector2(0, -50);
            grid = UIKit.ScrollGrid(area, new Vector2(234, 166), new Vector2(8, 8));
            UIKit.Fill(grid.parent.GetComponent<RectTransform>());
            var unlockBar = UIKit.Bar(body, new Color(.85f, .82f, .78f), UIKit.Gold, out unlockFill, 7);
            UIKit.Place(unlockBar, new Vector2(0, 0), new Vector2(0, 8), new Vector2(360, 14));
            unlockText = UIKit.Label(body, "", 16, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place(unlockText.rectTransform, new Vector2(0, 0), new Vector2(370, 0), new Vector2(560, 34));
            var support = UIKit.Button(body, "再建支援", () => Sim.RequestSupport(), ButtonStyle.Tab, 15, IconKind.Heart, Sim.CanManage);
            UIKit.Place(support.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(0, 0), new Vector2(150, 40));
            BuildTabs();
        }

        private void BuildTabs()
        {
            Clear(tabs);
            for (int i = 0; i < CategoryIds.Length; i++)
            {
                string id = CategoryIds[i];
                UIKit.Button(tabs, CategoryLabels[i], () => { category = id; BuildTabs(); signature = ""; Refresh(); }, category == id ? ButtonStyle.TabActive : ButtonStyle.Tab, 17);
            }
        }

        public override void Refresh()
        {
            string sig = category + ":" + S.day + ":" + S.phase + ":" + S.marketPurchased + ":" + S.moneyFen + ":" + string.Join(",", S.stock.Select(x => x.quantity)) + ":" + S.ingredients.unlockedIds.Count + ":" + S.storageCapacity;
            bool sale = Sim.PurchaseMultiplier < 1;
            header.text = (Sim.CanManage ? "食材は5個単位。鮮度は3日" + (Sim.FreshnessBonusDays > 0 ? "＋冷蔵庫1日" : "") + "。保管上限 " + S.storageCapacity + "個／食材" : "営業中は在庫の確認のみできます。")
                + (sale ? "　本日セール 3割引！" : "");
            var next = Sim.Unlocks.Next;
            if (next == null) { unlockText.text = "通常69種類の食材が解禁済み！"; UIKit.SetBar(unlockFill, 1); }
            else
            {
                long total = next.unlockRevenueYuan * 100L;
                unlockText.text = "次の解禁：" + next.displayName + "（累計売上あと " + RestaurantSimulation.Money(Sim.Unlocks.RemainingFen) + "）";
                UIKit.SetBar(unlockFill, total <= 0 ? 1 : (float)S.ingredients.settledRevenueFen / total);
            }
            if (sig == signature) return;
            signature = sig;
            Clear(grid);
            if (category == "market") { BuildMarket(); return; }
            var items = Sim.Catalog.ingredients.Where(x => !x.isRare && (category == "all" || x.category == category))
                .OrderByDescending(x => Sim.Unlocks.IsUnlocked(x.id)).ThenBy(x => x.unlockRevenueYuan).ThenBy(x => x.category).ToArray();
            foreach (var item in items) Card(item);
        }

        /// <summary>当日の1品と限定数・購入可否を通常仕入れから独立して表示する。</summary>
        private void BuildMarket()
        {
            var item = MorningMarket.Current(Sim);
            var card = UIKit.Panel(grid, UIKit.PaperDark, 14, false, "MorningMarket");
            string title = item == null ? "朝市は3日目から" : "本日限定：" + item.displayName;
            var name = UIKit.Label(card.rectTransform, title, 18, UIKit.Lacquer, TextAnchor.UpperLeft, true);
            UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(10, -8), new Vector2(216, 40));
            if (item == null) return;
            var image = UIKit.Rect("RareIcon", card.rectTransform).gameObject.AddComponent<Image>();
            image.sprite = App.Icons.Get(item.id); image.preserveAspect = true;
            UIKit.Place(image.rectTransform, new Vector2(0, 1), new Vector2(8, -46), new Vector2(62, 62));
            var info = UIKit.Label(card.rectTransform, "希少・品質 +3\n5個限定／朝のみ\n在庫 " + Sim.StockFor(item.id).quantity + "/" + S.storageCapacity + "・割引なし", 13, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(info.rectTransform, new Vector2(0, 1), new Vector2(78, -46), new Vector2(148, 64));
            long cost = Sim.PurchaseCost(item) * MorningMarket.BatchSize;
            bool canBuy = S.phase == DayPhase.Preparation && !S.marketPurchased && S.moneyFen >= cost
                && Sim.StockFor(item.id).quantity + MorningMarket.BatchSize <= S.storageCapacity;
            string label = S.marketPurchased ? "本日売切れ" : "5個 " + RestaurantSimulation.Money(cost);
            int offerDay = S.day;
            var buy = UIKit.Button(card.rectTransform, label, () => Sim.PurchaseRare(offerDay), ButtonStyle.Gold, 15, IconKind.Cart, canBuy);
            UIKit.Place(buy.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(8, 8), new Vector2(218, 38));
        }

        private void Card(IngredientDefinition item)
        {
            bool unlocked = Sim.Unlocks.IsUnlocked(item.id);
            var stock = Sim.StockFor(item.id);
            var card = UIKit.Panel(grid, unlocked ? Color.white : new Color(.9f, .87f, .82f), 14, true, item.displayName);
            var icon = card.transform;
            var image = UIKit.Rect("Icon", icon).gameObject.AddComponent<Image>();
            image.preserveAspect = true; image.raycastTarget = false;
            var sprite = App.Icons.Get(item.id);
            if (sprite != null) image.sprite = sprite; else { image.sprite = TextureFactory.Icon(IconKind.Leaf); image.color = CookingScreen.CategoryColor(item.category); }
            if (!unlocked) image.color = new Color(.3f, .3f, .3f, .55f);
            UIKit.Place(image.rectTransform, new Vector2(0, 1), new Vector2(8, -8), new Vector2(70, 70));
            var name = UIKit.Label(icon, item.displayName, 17, unlocked ? UIKit.Ink : UIKit.InkSoft, TextAnchor.UpperLeft, true);
            UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(82, -8), new Vector2(146, 40));
            var info = UIKit.Label(icon, FoodProfile.TextureName(FoodProfile.Of(item).Texture) + "　茹で" + item.cookingSeconds + "秒\n人気 " + Popularity(item.popularity) + "　" + item.portionGrams + "g", 13, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(info.rectTransform, new Vector2(0, 1), new Vector2(82, -48), new Vector2(146, 38));
            if (Seasons.IsFeatured(S, item.id))
            {
                var tag = UIKit.Panel(icon, UIKit.Jade, 8, false, "Featured");
                UIKit.Place(tag.rectTransform, new Vector2(0, 1), new Vector2(4, -4), new Vector2(64, 22));
                var tagText = UIKit.Label(tag.rectTransform, "旬 -25%", 12, UIKit.Paper, TextAnchor.MiddleCenter, true);
                UIKit.Fill(tagText.rectTransform);
            }
            if (!unlocked)
            {
                var lockIcon = UIKit.Icon(icon, IconKind.Lock, UIKit.InkSoft);
                UIKit.Place(lockIcon.rectTransform, new Vector2(0, 0), new Vector2(10, 12), new Vector2(26, 26));
                var need = UIKit.Label(icon, "累計売上 " + item.unlockRevenueYuan.ToString("#,0") + "元で解禁", 14, UIKit.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(need.rectTransform, new Vector2(0, 0), new Vector2(42, 10), new Vector2(190, 30));
                return;
            }
            int days = Sim.Catalog.Get(item.id).freshnessDays + Sim.FreshnessBonusDays - stock.ageDays;
            var stockText = UIKit.Label(icon, "在庫 " + stock.quantity + "/" + S.storageCapacity + (stock.quantity > 0 ? "　鮮度 あと" + days + "日" : ""), 14,
                stock.quantity == 0 ? UIKit.Bad : stock.quantity < 5 ? UIKit.Warn : UIKit.Jade, TextAnchor.MiddleLeft, true);
            UIKit.Place(stockText.rectTransform, new Vector2(0, 0), new Vector2(10, 50), new Vector2(216, 24));
            long price = Sim.PurchaseCost(item);
            string id = item.id;
            var buy5 = UIKit.Button(icon, "5個 " + RestaurantSimulation.Money(price * 5), () => Sim.Purchase(id, 5), ButtonStyle.Secondary, 14, null,
                Sim.CanManage && stock.quantity + 5 <= S.storageCapacity && S.moneyFen >= price * 5);
            UIKit.Place(buy5.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(8, 8), new Vector2(106, 38));
            var buy10 = UIKit.Button(icon, "10個 " + RestaurantSimulation.Money(price * 10), () => Sim.Purchase(id, 10), ButtonStyle.Primary, 14, null,
                Sim.CanManage && stock.quantity + 10 <= S.storageCapacity && S.moneyFen >= price * 10);
            UIKit.Place(buy10.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(-8, 8), new Vector2(108, 38));
        }

        private static string Popularity(int value) => value >= 75 ? "★★★" : value >= 55 ? "★★" : "★";
    }
}
