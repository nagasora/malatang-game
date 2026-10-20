using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 図鑑と記録の画面。店の物語（長期目標）、お客さん図鑑（もてなした客タイプと好み）、
    /// 隠しレシピ帳、今日のミッション、店の格付け・グランプリ・レジの腕前などの累計記録を見られる。
    /// </summary>
    public sealed class CollectionScreen : UIScreen
    {
        private static readonly string[] TabLabels = { "店の物語", "お客さん図鑑", "食材図鑑", "レシピ帳", "ミッション", "記録", "一杯アルバム", "希少食材" };
        /// <summary>開いた時に表示するタブ（0:店の物語 1:お客さん図鑑 2:食材図鑑 3:レシピ帳 4:ミッション 5:記録 6:一杯アルバム 7:希少食材）。</summary>
        public static int StartTab;
        private int tab;
        private bool portrait;
        private Rect layoutSafeArea;
        private RectTransform tabs, content;

        public override bool PausesGame => true;

        protected override void Build(RectTransform root)
        {
            tab = Mathf.Clamp(StartTab, 0, TabLabels.Length - 1);
            BuildLayout(root);
        }

        /// <summary>セーフエリア幅1280の論理座標で、縦画面に十分な実ピクセル文字サイズと高さを確保する。</summary>
        private void BuildLayout(RectTransform root)
        {
            portrait = Screen.height > Screen.width;
            layoutSafeArea = Screen.safeArea;
            float safeHeight = layoutSafeArea.height * 1280f / Mathf.Max(1f, layoutSafeArea.width);
            float height = portrait ? Mathf.Min(1860f, Mathf.Max(900f, safeHeight - 100f)) : 620f;
            UIKit.Dim(root, .5f);
            var body = UIKit.Card(root, "図鑑と記録", new Vector2(1120, height), IconKind.Book, () => App.BackToStore());
            tabs = UIKit.Rect("Tabs", body);
            tabs.anchorMin = new Vector2(0, 1); tabs.anchorMax = new Vector2(1, 1); tabs.pivot = new Vector2(.5f, 1);
            tabs.sizeDelta = new Vector2(0, portrait ? 154 : 46); tabs.anchoredPosition = Vector2.zero;
            if (portrait)
            {
                var layout = tabs.gameObject.AddComponent<GridLayoutGroup>();
                layout.cellSize = new Vector2(260, 72); layout.spacing = new Vector2(10, 10);
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 4;
            }
            else
            {
                var layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = false;
            }
            content = UIKit.Rect("Content", body);
            UIKit.Fill(content, 0, 0, portrait ? 170 : 56, 0);
            body.parent.gameObject.AddComponent<PopIn>();
            Rebuild();
        }

        /// <summary>回転や同方向のセーフエリア変更でも、選択タブを保持してレイアウトを切り替える。</summary>
        public override void Tick(float deltaTime)
        {
            if (portrait == (Screen.height > Screen.width) && layoutSafeArea == Screen.safeArea) return;
            Clear(Root);
            BuildLayout(Root);
        }

        private void Rebuild()
        {
            Clear(tabs);
            for (int i = 0; i < TabLabels.Length; i++)
            {
                int index = i;
                UIKit.Height(UIKit.Button(tabs, TabLabels[i], () => { tab = index; Rebuild(); }, tab == i ? ButtonStyle.TabActive : ButtonStyle.Tab, portrait ? 28 : 16), portrait ? 72 : 46, portrait ? 260 : 126);
            }
            Clear(content);
            if (tab == 0) BuildStory();
            else if (tab == 1) BuildCustomers();
            else if (tab == 2) BuildIngredients();
            else if (tab == 3) BuildRecipes();
            else if (tab == 4) BuildMissions();
            else if (tab == 5) BuildRecords();
            else if (tab == 6) BuildAlbum();
            else BuildIngredients(true);
        }

        private void BuildCustomers()
        {
            var types = Content.Archetypes.Where(a => a.Id != "judge").ToList();
            int met = types.Count(a => LifetimeStats.Get(S.stats.archetypesServed, a.Id) > 0);
            var head = UIKit.Label(content, "もてなしたお客さん " + met + " / " + types.Count + " タイプ", 18, UIKit.InkSoft, TextAnchor.MiddleLeft, true);
            head.rectTransform.anchorMin = new Vector2(0, 1); head.rectTransform.anchorMax = new Vector2(1, 1); head.rectTransform.pivot = new Vector2(.5f, 1);
            head.rectTransform.sizeDelta = new Vector2(0, 30); head.rectTransform.anchoredPosition = Vector2.zero;
            var area = UIKit.Rect("Area", content);
            UIKit.Fill(area, 0, 0, 36, 0);
            var grid = UIKit.ScrollGrid(area, new Vector2(345, 150), new Vector2(10, 10));
            UIKit.Fill(grid.parent as RectTransform);
            foreach (var a in types)
            {
                int count = LifetimeStats.Get(S.stats.archetypesServed, a.Id);
                bool known = count > 0;
                var panel = UIKit.Panel(grid, known ? UIKit.PaperDark : new Color(.85f, .82f, .76f), 14, false, "Type " + a.Id);
                var name = UIKit.Label(panel.rectTransform, known ? a.Label : "？？？", 19, known ? UIKit.Ink : UIKit.InkSoft, TextAnchor.UpperLeft, true);
                UIKit.Fill(name.rectTransform, 12, 12, 8, 116);
                var countLabel = UIKit.Label(panel.rectTransform, known ? count + "回" : (a.MinShopLevel > S.shopLevel ? "Lv" + a.MinShopLevel + "から来店" : "未来店"), 15, UIKit.Lacquer, TextAnchor.UpperRight, true);
                UIKit.Fill(countLabel.rectTransform, 12, 12, 10, 116);
                string detail = known
                    ? a.Description + "\n" + Traits(a)
                    : "まだ会ったことがありません。" + (a.SpawnWeight <= 0 ? "\n特別な日にだけ現れるようです。" : a.HourWeights != null ? "\n" + Hours(a) : "");
                var text = UIKit.Label(panel.rectTransform, detail, 14, UIKit.Ink, TextAnchor.UpperLeft);
                UIKit.Fill(text.rectTransform, 12, 12, 38, 8);
            }
        }

        private static string Traits(CustomerArchetype a)
        {
            string spice = a.MinSpice == a.MaxSpice ? "辛さ" + a.MinSpice : "辛さ" + a.MinSpice + "〜" + a.MaxSpice;
            string tip = a.Generosity <= 0 ? "チップなし" : a.Generosity >= 2 ? "チップ：とても気前がいい" : a.Generosity >= 1.2f ? "チップ：気前がいい" : a.Generosity < .8f ? "チップ：控えめ" : "チップ：ふつう";
            string pay = a.CardWeight >= a.CashWeight && a.CardWeight >= a.QrWeight ? "カード派" : a.QrWeight >= a.CashWeight ? "QR派" : "現金派";
            return spice + "・" + pay + "・" + tip + (a.Complaint >= 1.3f ? "・不満に厳しい" : "");
        }

        private static string Hours(CustomerArchetype a)
        {
            int best = 0;
            for (int i = 1; i < a.HourWeights.Length; i++) if (a.HourWeights[i] > a.HourWeights[best]) best = i;
            return (11 + best) + "時ごろによく来るらしい。";
        }

        private void BuildStory()
        {
            var list = UIKit.ScrollList(content, 10);
            UIKit.Fill(list.parent as RectTransform);
            for (int i = 0; i < Chapters.All.Length; i++)
            {
                var chapter = Chapters.All[i];
                bool done = i < S.chapter, current = i == S.chapter;
                if (!done && !current)
                {
                    UIKit.Height(UIKit.Label(list, "？？？　（前の章を完了すると始まります）", 18, UIKit.InkSoft, TextAnchor.MiddleLeft, true), 40);
                    continue;
                }
                int goalRows = current ? chapter.Goals.Length : 0;
                var panel = UIKit.Panel(list, done ? new Color(.88f, .94f, .86f) : UIKit.PaperDark, 16, false, "Chapter " + i);
                UIKit.Height(panel, 100 + goalRows * 34 + (current ? 36 : 0));
                var title = UIKit.Label(panel.rectTransform, chapter.Title + (done ? "　完了" : ""), 21, done ? UIKit.Good : UIKit.Lacquer, TextAnchor.UpperLeft, true);
                UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(16, -10), new Vector2(1010, 30));
                var story = UIKit.Label(panel.rectTransform, done ? chapter.Ending : chapter.Story, 16, UIKit.Ink, TextAnchor.UpperLeft);
                UIKit.Place(story.rectTransform, new Vector2(0, 1), new Vector2(16, -42), new Vector2(1010, 50));
                for (int g = 0; g < goalRows; g++)
                {
                    var goal = chapter.Goals[g];
                    int value = System.Math.Min(goal.Target, goal.Current(S));
                    bool ok = goal.Done(S);
                    var icon = UIKit.Icon(panel.rectTransform, ok ? IconKind.Check : IconKind.Star, ok ? UIKit.Good : UIKit.Gold);
                    UIKit.Place(icon.rectTransform, new Vector2(0, 1), new Vector2(20, -98 - g * 34), new Vector2(24, 24));
                    var label = UIKit.Label(panel.rectTransform, goal.Label + "　" + value + " / " + goal.Target, 17, ok ? UIKit.Good : UIKit.Ink, TextAnchor.MiddleLeft, true);
                    UIKit.Place(label.rectTransform, new Vector2(0, 1), new Vector2(52, -94 - g * 34), new Vector2(560, 32));
                    var bar = UIKit.Bar(panel.rectTransform, new Color(0, 0, 0, .1f), ok ? UIKit.Good : UIKit.Gold, out var fill, 6);
                    UIKit.Place(bar, new Vector2(0, 1), new Vector2(620, -104 - g * 34), new Vector2(240, 12));
                    UIKit.SetBar(fill, goal.Target <= 0 ? 1 : value / (float)goal.Target);
                }
                if (current)
                {
                    string reward = "報酬 " + UIKit.Money(chapter.RewardFen) + (string.IsNullOrEmpty(chapter.UnlocksTheme) ? "" : "・内装テーマ「" + Content.Theme(chapter.UnlocksTheme).Name + "」");
                    var r = UIKit.Label(panel.rectTransform, reward, 15, UIKit.InkSoft, TextAnchor.LowerRight);
                    UIKit.Fill(r.rectTransform, 16, 16, 8, 8);
                }
            }
            if (S.chapter >= Chapters.All.Length)
                UIKit.Height(UIKit.Label(list, "物語はひと区切り。これからも、あなたの店の毎日は続きます。", 18, UIKit.Lacquer, TextAnchor.MiddleLeft, true), 40);
        }

        /// <summary>食材図鑑：解禁した具材の食感・色・ひとこと・茹で時間・栄養と、使った回数。</summary>
        private void BuildIngredients(bool rareOnly = false)
        {
            var all = App.Catalog.ingredients.Where(x => x.isRare == rareOnly).ToArray();
            int unlocked = all.Count(x => Sim.Unlocks.IsUnlocked(x.id));
            bool readablePortrait = rareOnly && portrait;
            string heading = rareOnly ? "入手した希少食材 " + unlocked + " / " + all.Length + "\n朝市限定・一杯の品質+3（重複加算なし）"
                : "解禁した具材 " + unlocked + " / " + all.Length + "　（食感と色の違う具材を組み合わせると、彩り・食感のバランスが上がります）";
            var head = UIKit.Label(content, heading, readablePortrait ? 30 : 16, UIKit.InkSoft, TextAnchor.MiddleLeft, true);
            if (readablePortrait) head.resizeTextForBestFit = false;
            head.rectTransform.anchorMin = new Vector2(0, 1); head.rectTransform.anchorMax = new Vector2(1, 1); head.rectTransform.pivot = new Vector2(.5f, 1);
            head.rectTransform.sizeDelta = new Vector2(0, readablePortrait ? 96 : rareOnly ? 50 : 30); head.rectTransform.anchoredPosition = Vector2.zero;
            var area = UIKit.Rect("Area", content);
            UIKit.Fill(area, 0, 0, readablePortrait ? 104 : rareOnly ? 56 : 36, 0);
            int partnerRows = all.Max(item => Pairings.For(item.id).Count());
            var grid = UIKit.ScrollGrid(area, readablePortrait ? new Vector2(520, 420) : new Vector2(258, 126 + Mathf.Max(1, partnerRows) * 42), readablePortrait ? new Vector2(16, 16) : new Vector2(8, 8));
            if (readablePortrait)
            {
                var layout = grid.GetComponent<GridLayoutGroup>();
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 2;
            }
            UIKit.Fill(grid.parent as RectTransform);
            foreach (var item in all.OrderBy(x => !Sim.Unlocks.IsUnlocked(x.id)).ThenBy(x => x.unlockRevenueYuan))
            {
                bool known = Sim.Unlocks.IsUnlocked(item.id);
                var panel = UIKit.Panel(grid, known ? UIKit.PaperDark : new Color(.85f, .82f, .76f), 12, false, "Food " + item.id);
                var iconImage = UIKit.Rect("Icon", panel.rectTransform).gameObject.AddComponent<Image>();
                iconImage.preserveAspect = true; iconImage.raycastTarget = false;
                var sprite = known && App.Icons != null ? App.Icons.Get(item.id) : null;
                if (sprite != null) iconImage.sprite = sprite; else { iconImage.sprite = TextureFactory.Icon(IconKind.Lock); iconImage.color = UIKit.InkSoft; }
                UIKit.Place(iconImage.rectTransform, new Vector2(0, 1), new Vector2(readablePortrait ? 16 : 6, readablePortrait ? -14 : -8), new Vector2(readablePortrait ? 132 : 64, readablePortrait ? 132 : 64));
                var name = UIKit.Label(panel.rectTransform, known ? item.displayName : "？？？", readablePortrait ? 36 : 16, UIKit.Ink, TextAnchor.UpperLeft, true);
                if (readablePortrait) name.resizeTextForBestFit = false;
                UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(readablePortrait ? 162 : 74, readablePortrait ? -22 : -6), new Vector2(readablePortrait ? 342 : 178, readablePortrait ? 98 : 22));
                string detail;
                if (known)
                {
                    var traits = FoodProfile.Of(item);
                    int used = LifetimeStats.Get(S.stats.ingredientsServed, item.id);
                    detail = FoodProfile.TextureName(traits.Texture) + "・" + FoodProfile.ColorName(traits.Color) + "・茹で" + item.cookingSeconds + "秒\n" + traits.Note
                        + "\n" + item.portionGrams.ToString("0") + "g " + item.Calories(item.portionGrams).ToString("0") + "kcal　使った " + used + "回"
                        + (item.isRare ? "　希少・品質+3" : "")
                        + (Seasons.IsFeatured(S, item.id) ? "　<color=#2e7d5b>今週の旬</color>" : "");
                }
                else detail = item.isRare ? "希少食材\n3日目以降の朝市で入手できます" : item.categoryLabel + "\n累計売上 " + item.unlockRevenueYuan.ToString("#,0") + "元で解禁";
                if (readablePortrait && known)
                {
                    var traits = FoodProfile.Of(item);
                    detail = FoodProfile.TextureName(traits.Texture) + "・" + FoodProfile.ColorName(traits.Color) + "・茹で" + item.cookingSeconds + "秒\n" + traits.Note
                        + "\n" + item.portionGrams.ToString("0") + "g / " + item.Calories(item.portionGrams).ToString("0") + "kcal"
                        + "\n使用 " + LifetimeStats.Get(S.stats.ingredientsServed, item.id) + "回・品質+3";
                }
                var text = UIKit.Label(panel.rectTransform, detail, readablePortrait ? 30 : 12, UIKit.InkSoft, TextAnchor.UpperLeft);
                if (readablePortrait) text.resizeTextForBestFit = false;
                UIKit.Place(text.rectTransform, new Vector2(0, 1), new Vector2(readablePortrait ? 20 : 74, readablePortrait ? -158 : -30), new Vector2(readablePortrait ? 480 : 180, readablePortrait ? 242 : 84));
                if (!item.isRare) BuildIngredientPartners(panel.rectTransform, item.id);
            }
        }

        /// <summary>食材の全相方を表示し、未発見の組は名前やひとことを伏せる。</summary>
        private void BuildIngredientPartners(RectTransform parent, string ingredientId)
        {
            int index = 0;
            foreach (PairingDefinition pair in Pairings.For(ingredientId))
            {
                bool discovered = Pairings.Discovered(S, pair.Id);
                string text = discovered
                    ? App.Catalog.Get(pair.PartnerOf(ingredientId)).displayName + "／" + pair.Name + "\n" + pair.Note
                    : "？？？（未発見の相方）";
                Image heart = UIKit.Icon(parent, IconKind.Heart, discovered ? UIKit.Lacquer : UIKit.InkSoft);
                UIKit.Place(heart.rectTransform, new Vector2(0, 1), new Vector2(8, -126 - index * 42), new Vector2(16, 16));
                Text label = UIKit.Label(parent, text, 12, discovered ? UIKit.Ink : UIKit.InkSoft, TextAnchor.UpperLeft);
                UIKit.Place(label.rectTransform, new Vector2(0, 1), new Vector2(30, -122 - index * 42), new Vector2(220, 40));
                index++;
            }
            if (index == 0)
            {
                Text label = UIKit.Label(parent, "相性の記録なし", 12, UIKit.InkSoft, TextAnchor.UpperLeft);
                UIKit.Place(label.rectTransform, new Vector2(0, 1), new Vector2(8, -122), new Vector2(242, 36));
            }
        }

        private void BuildRecipes()
        {
            int found = S.recipes.Count;
            var head = UIKit.Label(content, "見つけた隠しレシピ " + found + " / " + Recipes.All.Length + "　（組み合わせが当てはまると品質が上がり、見つけたレシピは客が名前で頼むようになります）", 16, UIKit.InkSoft, TextAnchor.MiddleLeft, true);
            head.rectTransform.anchorMin = new Vector2(0, 1); head.rectTransform.anchorMax = new Vector2(1, 1); head.rectTransform.pivot = new Vector2(.5f, 1);
            head.rectTransform.sizeDelta = new Vector2(0, 30); head.rectTransform.anchoredPosition = Vector2.zero;
            var area = UIKit.Rect("Area", content);
            UIKit.Fill(area, 0, 0, 36, 0);
            var grid = UIKit.ScrollGrid(area, new Vector2(345, 150), new Vector2(10, 10));
            UIKit.Fill(grid.parent as RectTransform);
            foreach (var recipe in Recipes.All)
            {
                int count = LifetimeStats.Get(S.recipes, recipe.Id);
                bool known = count > 0;
                Color back = known ? (recipe.Legendary ? new Color(1, .9f, .62f) : UIKit.PaperDark) : new Color(.85f, .82f, .76f);
                var panel = UIKit.Panel(grid, back, 14, false, "Recipe " + recipe.Id);
                var name = UIKit.Label(panel.rectTransform, known ? recipe.Name : "？？？", 19, known ? UIKit.Ink : UIKit.InkSoft, TextAnchor.UpperLeft, true);
                UIKit.Fill(name.rectTransform, 12, 70, 8, 116);
                var countLabel = UIKit.Label(panel.rectTransform, known ? count + "杯" : recipe.Legendary ? "伝説" : "未発見", 15, recipe.Legendary ? UIKit.Gold : UIKit.Lacquer, TextAnchor.UpperRight, true);
                UIKit.Fill(countLabel.rectTransform, 12, 12, 10, 116);
                string detail = known ? recipe.Description + "\n" + RecipeLine(recipe) + "　品質+" + recipe.Bonus.ToString("0") : "ヒント：" + recipe.Hint;
                var text = UIKit.Label(panel.rectTransform, detail, 14, UIKit.Ink, TextAnchor.UpperLeft);
                UIKit.Fill(text.rectTransform, 12, 12, 38, 8);
            }
        }

        private string RecipeLine(RecipeDefinition r)
        {
            var parts = r.Ingredients.Select(id => App.Catalog.Get(id).displayName).ToList();
            parts.AddRange(r.Condiments.Select(id => Content.Condiment(id).Name));
            string soup = string.IsNullOrEmpty(r.Soup) ? "" : Content.Soup(r.Soup).Name + "・";
            string spice = r.MinSpice > 0 ? "・辛さ" + r.MinSpice + "以上" : r.MaxSpice < 5 ? "・辛さ" + r.MaxSpice + "まで" : "";
            return soup + string.Join("＋", parts) + spice;
        }

        /// <summary>品質の高い一杯を最大十二杯まで、画像と提供時の記録で振り返る。</summary>
        private void BuildAlbum()
        {
            Text head = UIKit.Label(content, "一杯アルバム " + S.album.Count + " / " + BowlAlbum.Limit + "杯　品質" + BowlAlbum.MinQuality.ToString("0") + "点以上の一杯を、品質の高い順に記録します。", 16, UIKit.InkSoft, TextAnchor.MiddleLeft, true);
            head.rectTransform.anchorMin = new Vector2(0, 1); head.rectTransform.anchorMax = new Vector2(1, 1); head.rectTransform.pivot = new Vector2(.5f, 1);
            head.rectTransform.sizeDelta = new Vector2(0, 32); head.rectTransform.anchoredPosition = Vector2.zero;
            RectTransform area = UIKit.Rect("Album Area", content);
            UIKit.Fill(area, 0, 0, 38, 0);
            if (S.album.Count == 0)
            {
                Text empty = UIKit.Label(area, "まだアルバムは空です。\n品質" + BowlAlbum.MinQuality.ToString("0") + "点以上の一杯をお客さんに提供して、思い出を残しましょう。", 21, UIKit.InkSoft, TextAnchor.MiddleCenter, true);
                UIKit.Fill(empty.rectTransform, 24, 24, 24, 24);
                return;
            }
            RectTransform grid = UIKit.ScrollGrid(area, new Vector2(345, 266), new Vector2(10, 10), "Album Grid");
            UIKit.Fill(grid.parent as RectTransform);
            foreach (AlbumEntry entry in S.album.Take(BowlAlbum.Limit)) BuildAlbumCard(grid, entry);
        }

        /// <summary>一杯の画像・題名・品質・提供日・お客さん・使用食材を一枚のカードにまとめる。</summary>
        private void BuildAlbumCard(RectTransform grid, AlbumEntry entry)
        {
            Image panel = UIKit.Panel(grid, UIKit.PaperDark, 14, false, "Album Bowl");
            Text title = UIKit.Label(panel.rectTransform, BowlAlbum.Title(entry, id => App.Catalog.Get(id).displayName), 18, UIKit.Lacquer, TextAnchor.UpperLeft, true, "Bowl Title");
            UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(10, -8), new Vector2(325, 28));
            RawImage portrait = UIKit.Rect("Bowl Portrait", panel.rectTransform).gameObject.AddComponent<RawImage>();
            portrait.texture = BowlPortrait.Get(entry);
            portrait.raycastTarget = false;
            UIKit.Place(portrait.rectTransform, new Vector2(0, 1), new Vector2(10, -40), new Vector2(120, 120));
            Text quality = UIKit.Label(panel.rectTransform, entry.quality + "点 " + UIKit.Stars(entry.quality), 20, UIKit.Lacquer, TextAnchor.MiddleLeft, true, "Quality");
            UIKit.Place(quality.rectTransform, new Vector2(0, 1), new Vector2(138, -42), new Vector2(197, 32));
            Text served = UIKit.Label(panel.rectTransform, entry.day + "日目\n" + entry.customerName + (entry.omakase ? "・おまかせ" : ""), 15, UIKit.Ink, TextAnchor.UpperLeft, false, "Served");
            UIKit.Place(served.rectTransform, new Vector2(0, 1), new Vector2(138, -78), new Vector2(197, 48));
            Text soup = UIKit.Label(panel.rectTransform, Content.Soup(entry.soup).Name + "・辛さ" + entry.spice, 14, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(soup.rectTransform, new Vector2(0, 1), new Vector2(138, -132), new Vector2(197, 28));
            Text ingredients = UIKit.Label(panel.rectTransform, "この一杯の食材", 13, UIKit.InkSoft, TextAnchor.MiddleLeft, true);
            UIKit.Place(ingredients.rectTransform, new Vector2(0, 1), new Vector2(10, -162), new Vector2(325, 20));
            RectTransform icons = UIKit.Rect("Ingredients", panel.rectTransform);
            UIKit.Place(icons, new Vector2(0, 1), new Vector2(10, -184), new Vector2(325, 72));
            GridLayoutGroup layout = icons.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(36, 34); layout.spacing = new Vector2(4, 4);
            foreach (string ingredientId in entry.ingredients)
            {
                Image icon = UIKit.Rect("Ingredient " + ingredientId, icons).gameObject.AddComponent<Image>();
                icon.preserveAspect = true; icon.raycastTarget = false;
                Sprite sprite = App.Icons == null ? null : App.Icons.Get(ingredientId);
                icon.sprite = sprite != null ? sprite : TextureFactory.Icon(IconKind.Bowl);
            }
        }

        private void BuildMissions()
        {
            var list = UIKit.ScrollList(content, 10);
            UIKit.Fill(list.parent as RectTransform);
            if (S.missions.Count == 0)
            {
                UIKit.Height(UIKit.Label(list, "ミッションは2日目から始まります。", 19, UIKit.InkSoft, TextAnchor.MiddleLeft), 40);
                return;
            }
            UIKit.Height(UIKit.Label(list, S.day + "日目のミッション（達成するとすぐに報酬）", 19, UIKit.Lacquer, TextAnchor.MiddleLeft, true), 34);
            foreach (var m in S.missions)
            {
                var panel = UIKit.Panel(list, m.done ? new Color(.86f, .95f, .86f) : UIKit.PaperDark, 14, false, "Mission");
                UIKit.Height(panel, 78);
                var icon = UIKit.Icon(panel.rectTransform, m.done ? IconKind.Check : IconKind.Star, m.done ? UIKit.Good : UIKit.Gold);
                UIKit.Place(icon.rectTransform, new Vector2(0, .5f), new Vector2(16, 0), new Vector2(36, 36));
                var label = UIKit.Label(panel.rectTransform, Missions.Describe(m), 19, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Fill(label.rectTransform, 64, 300, 8, 34);
                var bar = UIKit.Bar(panel.rectTransform, new Color(0, 0, 0, .1f), m.done ? UIKit.Good : UIKit.Gold, out var fill, 6);
                UIKit.Place(bar, new Vector2(0, 0), new Vector2(64, 14), new Vector2(560, 12));
                UIKit.SetBar(fill, m.target <= 0 ? 0 : m.progress / (float)m.target);
                var reward = UIKit.Label(panel.rectTransform, m.progress + " / " + m.target + "　報酬 " + UIKit.Money(m.rewardFen) + "・" + m.rewardXp + "XP", 16, UIKit.InkSoft, TextAnchor.MiddleRight);
                UIKit.Fill(reward.rectTransform, 640, 18, 8, 8);
            }
            UIKit.Height(UIKit.Label(list, "これまでに達成したミッション：" + S.stats.missionsCompleted + "回", 17, UIKit.InkSoft, TextAnchor.MiddleLeft), 34);
        }

        private void BuildRecords()
        {
            var list = UIKit.ScrollList(content, 8);
            UIKit.Fill(list.parent as RectTransform);
            int stars = Progression.Stars(S);
            var rank = Content.Rank(stars);
            Row(list, "店の格付け", new string('★', stars) + new string('☆', 5 - stars) + "　" + rank.Title, UIKit.Lacquer);
            Row(list, "", rank.Description + "（格付けの点数 " + Progression.RankPoints(S).ToString("0") + "）", UIKit.InkSoft);
            if (stars < 5) Row(list, "", "次の星まで：評判・人気・店舗レベルを上げると " + Content.Rank(stars + 1).MinPoints.ToString("0") + " 点で昇格", UIKit.InkSoft);
            Row(list, "麻辣湯グランプリ", "金 " + S.stats.contestGold + "　銀 " + S.stats.contestSilver + "　銅 " + S.stats.contestBronze + "（14日目から7日ごと）", UIKit.Ink);
            Row(list, "累計完食・完飲", "完食 " + S.stats.finishedFood + "杯・完飲 " + S.stats.finishedSoup + "杯", UIKit.Ink);
            Row(list, "累計提供", S.stats.served + "杯（会心の一杯 " + S.stats.perfectBowls + "杯）", UIKit.Ink);
            Row(list, "最高コンボ", S.stats.bestCombo + "連続（現在 " + S.combo + "）", UIKit.Ink);
            Row(list, "1日の最高提供数", S.stats.bestDayServed + "杯", UIKit.Ink);
            Row(list, "おつりぴったり", S.stats.exactChange + "回", UIKit.Ink);
            Row(list, "チップ累計", UIKit.Money(S.stats.tipsFen), UIKit.Ink);
            Row(list, "値引き", S.stats.complaints + "回・" + UIKit.Money(S.stats.discountsFen), UIKit.Ink);
            Row(list, "評論家を満足", S.stats.criticsSatisfied + "回", UIKit.Ink);
            Row(list, "芸能人をもてなした", S.stats.celebrities + "回", UIKit.Ink);
            Row(list, "実績", S.achievements.Count + " / " + Content.Achievements.Length, UIKit.Ink);
            Row(list, "隠しレシピ", S.recipes.Count + " / " + Recipes.All.Length + "（レシピの一杯 " + S.stats.recipeBowls + "杯）", UIKit.Ink);
            Row(list, "内装テーマ", S.ownedThemes.Count + " / " + Content.Themes.Length + "（いまは「" + Content.Theme(S.theme).Name + "」）", UIKit.Ink);
            foreach (var soup in Content.Soups)
            {
                int best = LifetimeStats.Get(S.stats.bestQualityBySoup, soup.Id);
                Row(list, soup.Name + "スープ", best > 0 ? "最高 " + best + "点 " + UIKit.Stars(best) : soup.UnlockLevel > S.shopLevel ? "Lv" + soup.UnlockLevel + "で解禁" : "まだ作っていません", UIKit.InkSoft);
            }
        }

        private static void Row(Transform parent, string key, string value, Color color)
        {
            var row = UIKit.Row(parent, 34, 12);
            var k = UIKit.Label(row, key, 18, UIKit.InkSoft, TextAnchor.MiddleLeft, true);
            UIKit.Height(k, 34, 240);
            k.GetComponent<LayoutElement>().flexibleWidth = 0;
            var v = UIKit.Label(row, value, 18, color, TextAnchor.MiddleLeft, true);
            v.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        }
    }
}
