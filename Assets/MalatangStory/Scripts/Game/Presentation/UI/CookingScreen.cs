using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 調理画面。左側は厨房の寄り視点（3D で直接操作も可能）、右側のパネルは注文票と工程ごとの操作。
    /// 工程が変わった時だけパネルを作り直し、茹で加減・スープ量などは毎フレーム更新する。
    /// </summary>
    public sealed class CookingScreen : UIScreen
    {
        private static readonly string[] CategoryIds = { "all", "vegetable", "mushroom", "noodle", "soy", "meat", "seafood", "processed", "other" };
        private static readonly string[] CategoryLabels = { "全て", "野菜", "きのこ", "麺", "大豆", "肉", "魚介", "加工品", "他" };
        private static string category = "all";

        /// <summary>調理画面で表示する食材カテゴリを取得・設定する。</summary>
        internal static string Category
        {
            get => category;
            set => category = CategoryIds.Contains(value) ? value : "all";
        }

        private RectTransform panel, ticket, body, switcher;
        private string bodySignature = "", ticketSignature = "", switcherSignature = "";
        private string selectedSoup { get => App.World.Kitchen.SelectedSoup; set => App.World.Kitchen.SelectedSoup = value; }
        private readonly List<(int index, Image fill, RectTransform marker, Text hint, Button drop)> doneness = new List<(int, Image, RectTransform, Text, Button)>();
        private Text weighLabel, shakeLabel, soupLabel, spiceLabel, timerLabel, boilWarning;
        private Image soupFill;
        private RectTransform soupBand;
        private HoldButton pourButton;
        private Button shakeButton;
        private readonly List<(string id, Text count)> condimentCounts = new List<(string, Text)>();
        private RectTransform resultOverview;
        private RawImage resultPortrait;
        private Text resultStars, resultRecipe, resultPairings;
        private float resultWidth = -1f, resultReveal;
        private int shownResultScore = -1;
        private Text resultRare;

        public override bool ShowHud => true;

        private CustomerOrder Order => App.Selected;

        protected override void Build(RectTransform root)
        {
            var p = UIKit.Panel(root, new Color(.99f, .96f, .89f, .97f), 22, true, "Cooking Panel");
            panel = p.rectTransform;
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1); panel.pivot = new Vector2(1, .5f);
            panel.offsetMin = new Vector2(-484, 12); panel.offsetMax = new Vector2(-12, -80);
            var close = UIKit.Button(panel, "店内へ", () => App.BackToStore(), ButtonStyle.Secondary, 16, IconKind.ArrowLeft);
            UIKit.Place(close.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-12, -10), new Vector2(118, 40));
            ticket = UIKit.Rect("Ticket", panel);
            ticket.anchorMin = new Vector2(0, 1); ticket.anchorMax = new Vector2(1, 1); ticket.pivot = new Vector2(.5f, 1);
            ticket.offsetMin = new Vector2(14, -160); ticket.offsetMax = new Vector2(-14, -8);
            body = UIKit.Rect("Body", panel);
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = new Vector2(14, 12); body.offsetMax = new Vector2(-14, -166);
            switcher = UIKit.Rect("Switcher", root);
            switcher.anchorMin = new Vector2(0, 0); switcher.anchorMax = new Vector2(0, 0); switcher.pivot = new Vector2(0, 0);
            switcher.anchoredPosition = new Vector2(14, 14); switcher.sizeDelta = new Vector2(760, 66);
            var layout = switcher.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8; layout.childControlWidth = false; layout.childControlHeight = false; layout.childForceExpandWidth = false; layout.childAlignment = TextAnchor.LowerLeft;
            if (App.UI.Hud != null) App.UI.Hud.TicketsVisible = false;
        }

        public override void Close()
        {
            if (App.UI.Hud != null) App.UI.Hud.TicketsVisible = true;
            base.Close();
        }

        public override void Refresh()
        {
            var o = Order;
            App.World.Kitchen?.SetOrder(o);
            RefreshSwitcher();
            if (o == null || o.IsClosed)
            {
                if (bodySignature != "none") { bodySignature = "none"; ticketSignature = ""; ResetDynamicRefs(); Clear(ticket); Clear(body); BuildEmpty(); }
                return;
            }
            bool ready = o.stage == OrderStage.Ready;
            ticket.offsetMin = new Vector2(14f, ready ? -80f : -160f);
            body.offsetMax = new Vector2(-14f, ready ? -86f : -166f);
            string ts = o.id + ":" + (int)o.stage + ":" + o.selected.Count;
            if (ts != ticketSignature) { ticketSignature = ts; Clear(ticket); BuildTicket(o); }
            int stageToken = o.stage == OrderStage.Drained ? (int)OrderStage.Finishing : (int)o.stage;
            string bs = o.id + ":" + stageToken + ":" + (o.stage == OrderStage.Selecting ? string.Join(",", o.selected) + category : "")
                + (o.stage == OrderStage.Heating ? string.Join(",", o.dropTimes.Select(t => t >= 0 ? "1" : "0")) : "")
                + (o.stage == OrderStage.Weighed ? Sim.FreePot().ToString() : "") + (o.stage == OrderStage.Finishing || o.stage == OrderStage.Drained ? selectedSoup + Sim.State.shopLevel : "") + o.handledByStaff;
            if (bs != bodySignature) { bodySignature = bs; Clear(body); BuildBody(o); }
        }

        private void BuildEmpty()
        {
            var label = UIKit.Label(body, "この注文は完了しました。\n下の注文を選ぶか、店内へ戻りましょう。", 20, UIKit.InkSoft, TextAnchor.MiddleCenter);
            UIKit.Fill(label.rectTransform, 10, 10, 10, 100);
        }

        private void BuildTicket(CustomerOrder o)
        {
            var name = UIKit.Label(ticket, o.customerName + "　" + o.type + (o.takeout ? "・持ち帰り" : "") + "", 22, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(0, 0), new Vector2(320, 36));
            var chips = UIKit.Rect("Request", ticket);
            chips.gameObject.SetActive(o.stage != OrderStage.Ready);
            chips.anchorMin = new Vector2(0, 1); chips.anchorMax = new Vector2(1, 1); chips.pivot = new Vector2(0, 1);
            chips.offsetMin = new Vector2(0, -118); chips.offsetMax = new Vector2(0, -40);
            var grid = chips.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(84, 36); grid.spacing = new Vector2(4, 4);
            if (o.omakase)
            {
                var wish = UIKit.Label(chips, "おまかせ：" + string.Join("・", Omakase.Wishes(o)), 15, UIKit.Jade, TextAnchor.UpperLeft, true);
                wish.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                UIKit.Fill(wish.rectTransform);
            }
            var remaining = o.selected.ToList();
            foreach (string id in o.requested)
            {
                bool have = remaining.Remove(id);
                var chip = UIKit.Panel(chips, have ? new Color(.85f, .93f, .82f) : UIKit.PaperDark, 10, false, "Chip");
                var icon = IngredientIcon(chip.transform, id);
                UIKit.Place(icon.rectTransform, new Vector2(0, .5f), new Vector2(2, 0), new Vector2(32, 32));
                var label = UIKit.Label(chip.transform, Short(Sim.Catalog.Get(id).displayName), 12, UIKit.Ink, TextAnchor.MiddleLeft);
                UIKit.Fill(label.rectTransform, 34, 2, 0, 0);
                if (have) { var check = UIKit.Icon(chip.transform, IconKind.Check, UIKit.Good); UIKit.Place(check.rectTransform, new Vector2(1, 1), new Vector2(2, 2), new Vector2(16, 16)); }
            }
            var wants = new List<string> { Content.Soup(o.soup).Name + "スープ", "辛さ " + new string('辣', o.spice) + (o.spice == 0 ? "なし" : "") };
            wants.AddRange(o.requestedCondiments.Distinct().Select(x => Content.Condiment(x).Name + "多め"));
            wants.AddRange(o.dislikedCondiments.Where(x => x != Content.ChiliOil).Select(x => Content.Condiment(x).Name + "抜き"));
            var line = UIKit.Label(ticket, string.Join("　/　", wants), 16, UIKit.Ink, TextAnchor.MiddleLeft, true);
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0); line.rectTransform.pivot = new Vector2(0, 0);
            line.rectTransform.offsetMin = new Vector2(0, 0); line.rectTransform.offsetMax = new Vector2(0, 36);
        }

        private Image IngredientIcon(Transform parent, string id)
        {
            var image = UIKit.Rect("Ingredient Icon", parent).gameObject.AddComponent<Image>();
            image.preserveAspect = true; image.raycastTarget = false;
            var sprite = App.Icons != null ? App.Icons.Get(id) : null;
            if (sprite != null) image.sprite = sprite;
            else { image.sprite = TextureFactory.Icon(IconKind.Leaf); image.color = CategoryColor(Sim.Catalog.Get(id).category); }
            return image;
        }

        public static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "vegetable": return new Color(.35f, .62f, .25f);
                case "mushroom": return new Color(.6f, .45f, .3f);
                case "noodle": return new Color(.9f, .78f, .5f);
                case "soy": return new Color(.92f, .86f, .65f);
                case "meat": return new Color(.78f, .36f, .3f);
                case "seafood": return new Color(.95f, .55f, .4f);
                case "processed": return new Color(.85f, .6f, .4f);
                default: return new Color(.55f, .3f, .3f);
            }
        }

        private static string Short(string name)
        {
            int paren = name.IndexOf('（');
            string s = paren > 0 ? name.Substring(0, paren) : name;
            return s.Length > 5 ? s.Substring(0, 5) : s;
        }

        private void ResetDynamicRefs()
        {
            doneness.Clear(); condimentCounts.Clear();
            resultOverview = null; resultPortrait = null;
            resultStars = resultRecipe = resultPairings = null; resultWidth = -1f;
            weighLabel = shakeLabel = soupLabel = spiceLabel = timerLabel = boilWarning = null; soupFill = null; pourButton = null; shakeButton = null; soupBand = null;
        }

        private void BuildBody(CustomerOrder o)
        {
            ResetDynamicRefs();
            if (o.handledByStaff && o.stage != OrderStage.Ready)
            {
                var label = UIKit.Label(body, "スタッフがこの注文を調理しています。\n他の注文を担当するか、引き継いで自分で仕上げましょう。", 20, UIKit.InkSoft, TextAnchor.MiddleCenter);
                UIKit.Fill(label.rectTransform, 10, 10, 10, 120);
                string id = o.id;
                BigButton(body, "自分で引き継ぐ", () => { Sim.TakeOver(id); bodySignature = ""; Refresh(); }, ButtonStyle.Primary, IconKind.Person);
                return;
            }
            switch (o.stage)
            {
                case OrderStage.Waiting:
                case OrderStage.Ordering: BuildWaiting(o); break;
                case OrderStage.Selecting: if (o.style == OrderStyle.SelfServe && o.paidFen == 0) BuildSelfServe(o); else BuildPicking(o); break;
                case OrderStage.Weighed: BuildWeighed(o); break;
                case OrderStage.Heating: BuildHeating(o); break;
                case OrderStage.Drained:
                case OrderStage.Finishing: BuildFinishing(o); break;
                case OrderStage.Ready: BuildResult(o); break;
            }
        }

        private RectTransform Step(string title, string hint)
        {
            var header = UIKit.Label(body, title, 20, UIKit.Lacquer, TextAnchor.MiddleLeft, true, "Step");
            UIKit.Place(header.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(440, 30));
            var h = UIKit.Label(body, hint, 15, UIKit.InkSoft, TextAnchor.UpperLeft, false, "Hint");
            UIKit.Place(h.rectTransform, new Vector2(0, 1), new Vector2(0, -30), new Vector2(440, 40));
            var content = UIKit.Rect("Content", body);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one; content.offsetMin = Vector2.zero; content.offsetMax = new Vector2(0, -72);
            return content;
        }

        private Button BigButton(RectTransform parent, string label, System.Action action, ButtonStyle style = ButtonStyle.Primary, IconKind? icon = null, bool interactable = true)
        {
            var button = UIKit.Button(parent, label, action, style, 22, icon, interactable);
            var rt = button.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(.5f, 0);
            rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 58);
            return button;
        }

        private void BuildWaiting(CustomerOrder o)
        {
            var content = Step("レジで注文を聞きましょう", "レジで注文を聞いてお会計が済むと、調理を始められます。");
            string id = o.id;
            BigButton(content, "レジへ行く", () => App.SelectOrder(id), ButtonStyle.Primary, IconKind.Bell);
        }

        private void BuildSelfServe(CustomerOrder o)
        {
            var content = Step("① 量って会計", "セルフ式のお客さんが自分で具材を選んだボウルです。秤に載せて量りましょう。");
            BowlStrip(content, o, false, 0);
            weighLabel = UIKit.Label(content, "", 20, UIKit.Ink, TextAnchor.MiddleCenter, true, "Weight");
            UIKit.Place(weighLabel.rectTransform, new Vector2(.5f, 0), new Vector2(0, 70), new Vector2(440, 40));
            BigButton(content, "秤で量る", () => Sim.Weigh(o.id), ButtonStyle.Primary, IconKind.Scale);
        }

        private void BuildPicking(CustomerOrder o)
        {
            var content = Step("① 具材を取る", "冷蔵ケースの具材をタップしてボウルへ。ボウルの具材をタップすると戻せます。");
            var tabs = UIKit.Rect("Tabs", content);
            tabs.anchorMin = new Vector2(0, 1); tabs.anchorMax = new Vector2(1, 1); tabs.pivot = new Vector2(.5f, 1);
            tabs.offsetMin = new Vector2(0, -36); tabs.offsetMax = Vector2.zero;
            var tabLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 3; tabLayout.childControlWidth = true; tabLayout.childControlHeight = true; tabLayout.childForceExpandWidth = true;
            for (int i = 0; i < CategoryIds.Length; i++)
            {
                string id = CategoryIds[i];
                var b = UIKit.Button(tabs, CategoryLabels[i], () => { category = id; Refresh(); }, category == id ? ButtonStyle.TabActive : ButtonStyle.Tab, 14);
                b.GetComponentInChildren<Text>().resizeTextMinSize = 10;
            }
            bool showBalance = o.omakase;
            var gridArea = UIKit.Rect("Grid Area", content);
            gridArea.anchorMin = Vector2.zero; gridArea.anchorMax = Vector2.one; gridArea.offsetMin = new Vector2(0, showBalance ? 252 : 182); gridArea.offsetMax = new Vector2(0, -40);
            if (showBalance) BalancePanel(content, o, 180);
            var grid = UIKit.ScrollGrid(gridArea, new Vector2(102, 92), new Vector2(6, 6));
            UIKit.Fill(grid.parent.GetComponent<RectTransform>());
            var items = Sim.Unlocks.Available.Where(x => category == "all" || x.category == category)
                .OrderByDescending(x => o.requested.Contains(x.id)).ThenBy(x => x.category).ThenBy(x => x.displayName).ToArray();
            foreach (var item in items) IngredientCell(grid, o, item);
            BowlStrip(content, o, true, 68);
            weighLabel = UIKit.Label(content, "", 15, UIKit.InkSoft, TextAnchor.MiddleLeft, false, "Weight");
            UIKit.Place(weighLabel.rectTransform, new Vector2(0, 0), new Vector2(0, 62), new Vector2(440, 20));
            BigButton(content, "秤で量る", () => Sim.Weigh(o.id), ButtonStyle.Primary, IconKind.Scale, o.selected.Count > 0);
        }

        private void IngredientCell(RectTransform grid, CustomerOrder o, IngredientDefinition item)
        {
            int stock = Sim.StockFor(item.id).quantity - o.selected.Count(x => x == item.id);
            bool wanted = o.requested.Contains(item.id);
            var cell = UIKit.Panel(grid, wanted ? new Color(1, .95f, .8f) : Color.white, 12, true, item.displayName);
            var button = cell.gameObject.AddComponent<Button>();
            button.interactable = stock > 0;
            string id = item.id;
            button.onClick.AddListener(() => Sim.AddIngredient(o.id, id));
            cell.gameObject.AddComponent<PressFeedback>();
            var icon = IngredientIcon(cell.transform, item.id);
            UIKit.Place(icon.rectTransform, new Vector2(.5f, 1), new Vector2(0, -4), new Vector2(54, 50));
            var name = UIKit.Label(cell.transform, Short(item.displayName), 13, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Place(name.rectTransform, new Vector2(.5f, 0), new Vector2(0, 18), new Vector2(98, 20));
            var traits = FoodProfile.Of(item);
            var info = UIKit.Label(cell.transform, FoodProfile.TextureShort(traits.Texture) + "・" + item.cookingSeconds + "秒・残" + stock, 11, stock > 0 ? UIKit.InkSoft : UIKit.Bad, TextAnchor.MiddleCenter);
            UIKit.Place(info.rectTransform, new Vector2(.5f, 0), new Vector2(0, 2), new Vector2(98, 16));
            if (wanted) { var star = UIKit.Icon(cell.transform, IconKind.Bell, UIKit.Lacquer); UIKit.Place(star.rectTransform, new Vector2(1, 1), new Vector2(-3, -3), new Vector2(18, 18)); }
            if (Pairings.For(item.id).Any(pair => o.selected.Contains(pair.PartnerOf(item.id))))
            {
                Image heart = UIKit.Icon(cell.transform, IconKind.Heart, UIKit.Lacquer, "Pairing Partner");
                UIKit.Place(heart.rectTransform, new Vector2(0, 1), new Vector2(3, -3), new Vector2(20, 20));
            }
            if (stock <= 0) cell.color = new Color(.85f, .82f, .8f);
        }

        /// <summary>おまかせ用：いまのボウルの彩り・食感・栄養と、予算に対する重さを棒で見せる。</summary>
        private void BalancePanel(RectTransform content, CustomerOrder o, float bottom)
        {
            var panel = UIKit.Panel(content, new Color(.93f, .97f, .92f), 12, false, "Balance");
            var rt = panel.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(.5f, 0);
            rt.offsetMin = new Vector2(0, bottom); rt.offsetMax = new Vector2(0, bottom + 64);
            var balance = FoodProfile.Evaluate(o.selected, Sim.Catalog);
            float grams = o.selected.Sum(id => Sim.Catalog.Get(id).portionGrams);
            var rows = new (string label, float value, string detail)[]
            {
                ("彩り", balance.ColorScore, balance.Colors.Count + "色"),
                ("食感", balance.TextureScore, balance.Textures.Count + "種"),
                ("栄養", balance.NutritionScore, (balance.Vegetables > 0 ? "菜" : "") + (balance.Proteins > 0 ? "肉" : "") + (balance.Staples > 0 ? "麺" : "")),
                ("重さ", Omakase.WeightFit(grams, o.targetGrams), grams.ToString("0") + "/" + o.targetGrams.ToString("0") + "g"),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float x = 8 + (i % 2) * 222, y = -6 - (i / 2) * 20;
                var label = UIKit.Label(rt, rows[i].label, 13, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place(label.rectTransform, new Vector2(0, 1), new Vector2(x, y), new Vector2(36, 18));
                var bar = UIKit.Bar(rt, new Color(0, 0, 0, .1f), rows[i].value >= 80 ? UIKit.Good : rows[i].value >= 50 ? UIKit.Gold : UIKit.Bad, out var fill, 5);
                UIKit.Place(bar, new Vector2(0, 1), new Vector2(x + 38, y - 5), new Vector2(110, 9));
                UIKit.SetBar(fill, rows[i].value / 100f);
                var detail = UIKit.Label(rt, rows[i].detail, 12, UIKit.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(detail.rectTransform, new Vector2(0, 1), new Vector2(x + 152, y), new Vector2(68, 18));
            }
            string advice = o.selected.Count == 0 ? "好みに合わせて自由に選びましょう" : balance.Advice();
            var tip = UIKit.Label(rt, advice.Length == 0 ? "いいバランス！" : advice, 12, advice.Length == 0 ? UIKit.Good : UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(tip.rectTransform, new Vector2(0, 0), new Vector2(8, 2), new Vector2(430, 18));
        }

        /// <summary>成立した相性と、品質に加算される上限適用後のボーナスを表示する。</summary>
        private void PairingSummary(RectTransform content, CustomerOrder order, float bottom)
        {
            List<PairingDefinition> pairs = Pairings.In(order.selected);
            string text = pairs.Count == 0
                ? "相方の食材には心印。組み合わせて相性を見つけましょう。"
                : string.Join("・", pairs.Select(pair => pair.Name)) + "　品質 +" + Pairings.Bonus(pairs.Count).ToString("0.#");
            Text label = UIKit.Label(content, text, 14, pairs.Count == 0 ? UIKit.InkSoft : UIKit.Lacquer, TextAnchor.MiddleLeft, pairs.Count > 0, "Pairing Summary");
            UIKit.Place(label.rectTransform, new Vector2(0, 0), new Vector2(0, bottom), new Vector2(440, 40));
        }

        /// <summary>選択済みの具材と、その上に成立相性の要約を表示する。</summary>
        private void BowlStrip(RectTransform content, CustomerOrder o, bool removable, float bottom)
        {
            var strip = UIKit.Panel(content, UIKit.PaperDark, 12, false, "Bowl");
            var rt = strip.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(.5f, 0);
            rt.offsetMin = new Vector2(0, bottom + (removable ? 22 : 110)); rt.offsetMax = new Vector2(0, bottom + (removable ? 62 : 190));
            PairingSummary(content, o, rt.offsetMax.y + 4);
            var layout = strip.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(40, 38); layout.spacing = new Vector2(2, 2); layout.padding = new RectOffset(4, 4, 1, 1);
            for (int i = 0; i < o.selected.Count; i++)
            {
                string id = o.selected[i];
                var chip = UIKit.Rect("Item", strip.transform);
                var image = chip.gameObject.AddComponent<Image>(); image.color = new Color(1, 1, 1, .001f);
                var icon = IngredientIcon(chip, id); UIKit.Fill(icon.rectTransform, 2, 2, 2, 2);
                if (removable) { var b = chip.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => Sim.RemoveIngredient(o.id, id)); chip.gameObject.AddComponent<PressFeedback>(); }
            }
        }

        private void BuildWeighed(CustomerOrder o)
        {
            var content = Step("② 茹でる", "具材ごとに茹で時間が違います。「時間差」なら長いものから順に入れて、全部を同時にちょうど良く仕上げられます。");
            int pot = Sim.FreePot();
            var status = UIKit.Label(content, pot >= 0 ? "空いている鍋：" + (pot + 1) + "番" : "鍋がすべて使用中です。先の注文を引き上げましょう。", 18, pot >= 0 ? UIKit.Jade : UIKit.Bad, TextAnchor.MiddleLeft, true);
            UIKit.Place(status.rectTransform, new Vector2(0, 1), new Vector2(0, -6), new Vector2(440, 30));
            var list = UIKit.Label(content, string.Join("\n", o.selected.Distinct().OrderByDescending(x => Sim.Catalog.Get(x).cookingSeconds)
                .Select(x => "・" + Sim.Catalog.Get(x).displayName + "　" + Sim.Catalog.Get(x).cookingSeconds + "秒")), 16, UIKit.Ink, TextAnchor.UpperLeft);
            list.rectTransform.anchorMin = Vector2.zero; list.rectTransform.anchorMax = Vector2.one;
            list.rectTransform.offsetMin = new Vector2(0, 140); list.rectTransform.offsetMax = new Vector2(0, -44);
            var all = BigButton(content, "まとめて茹でる（かんたん）", () => Sim.StartHeating(o.id, true), ButtonStyle.Secondary, IconKind.Pot, pot >= 0);
            all.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 66);
            BigButton(content, "時間差で茹でる（おすすめ）", () => Sim.StartHeating(o.id, false), ButtonStyle.Primary, IconKind.Clock, pot >= 0);
        }

        private void BuildHeating(CustomerOrder o)
        {
            var content = Step("② 茹でる", "ゲージが緑になったら食べごろ。「今！」の具材を入れていきましょう。");
            timerLabel = UIKit.Label(content, "", 18, UIKit.Ink, TextAnchor.MiddleLeft, true, "Timer");
            UIKit.Place(timerLabel.rectTransform, new Vector2(0, 1), new Vector2(0, -2), new Vector2(300, 28));
            boilWarning = UIKit.Label(content, "吹きこぼれ！", 18, UIKit.Bad, TextAnchor.MiddleRight, true, "Boil");
            UIKit.Place(boilWarning.rectTransform, new Vector2(1, 1), new Vector2(0, -2), new Vector2(140, 28));
            var listArea = UIKit.Rect("Items", content);
            listArea.anchorMin = Vector2.zero; listArea.anchorMax = Vector2.one; listArea.offsetMin = new Vector2(0, 132); listArea.offsetMax = new Vector2(0, -34);
            var list = UIKit.ScrollList(listArea, 6, 2);
            UIKit.Fill(list.parent.GetComponent<RectTransform>());
            for (int i = 0; i < o.selected.Count; i++) DonenessRow(list, o, i);
            var dropAll = BigButton(content, "残りをすべて入れる", () => Sim.DropRemaining(o.id), ButtonStyle.Secondary, IconKind.Plus, o.dropTimes.Any(t => t < 0));
            dropAll.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 66);
            BigButton(content, "引き上げる", () => Sim.Drain(o.id), ButtonStyle.Primary, IconKind.Ladle, o.dropTimes.All(t => t >= 0));
        }

        private void DonenessRow(RectTransform list, CustomerOrder o, int index)
        {
            var row = UIKit.Rect("Row", list);
            UIKit.Height(row, 44);
            var icon = IngredientIcon(row, o.selected[index]);
            UIKit.Place(icon.rectTransform, new Vector2(0, .5f), Vector2.zero, new Vector2(40, 40));
            var name = UIKit.Label(row, Short(Sim.Catalog.Get(o.selected[index]).displayName), 14, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place(name.rectTransform, new Vector2(0, .5f), new Vector2(44, 9), new Vector2(110, 22));
            var hint = UIKit.Label(row, "", 13, UIKit.InkSoft, TextAnchor.MiddleLeft);
            UIKit.Place(hint.rectTransform, new Vector2(0, .5f), new Vector2(44, -11), new Vector2(110, 20));
            var bar = UIKit.Panel(row, new Color(.82f, .8f, .78f), 6, false, "Gauge");
            var brt = bar.rectTransform; brt.anchorMin = new Vector2(0, .5f); brt.anchorMax = new Vector2(1, .5f); brt.pivot = new Vector2(0, .5f);
            brt.offsetMin = new Vector2(158, -9); brt.offsetMax = new Vector2(-76, 9);
            const float range = 1.8f;
            var perfect = UIKit.Rect("Perfect", brt); var pimg = perfect.gameObject.AddComponent<Image>(); pimg.color = new Color(.45f, .78f, .4f, .9f); pimg.raycastTarget = false;
            perfect.anchorMin = new Vector2(QualityEvaluator.PerfectDonenessMin / range, 0); perfect.anchorMax = new Vector2(QualityEvaluator.PerfectDonenessMax / range, 1);
            perfect.offsetMin = perfect.offsetMax = Vector2.zero;
            var over = UIKit.Rect("Over", brt); var oimg = over.gameObject.AddComponent<Image>(); oimg.color = new Color(.92f, .55f, .3f, .7f); oimg.raycastTarget = false;
            over.anchorMin = new Vector2(QualityEvaluator.PerfectDonenessMax / range, 0); over.anchorMax = new Vector2(1, 1); over.offsetMin = over.offsetMax = Vector2.zero;
            var fillRt = UIKit.Rect("Fill", brt); var fill = fillRt.gameObject.AddComponent<Image>(); fill.color = new Color(.2f, .14f, .1f, .35f); fill.raycastTarget = false;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(0, 1); fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var marker = UIKit.Rect("Marker", brt); var mimg = marker.gameObject.AddComponent<Image>(); mimg.color = UIKit.Ink; mimg.raycastTarget = false;
            marker.anchorMin = new Vector2(0, -.2f); marker.anchorMax = new Vector2(0, 1.2f); marker.sizeDelta = new Vector2(4, 0);
            Button drop = null;
            if (o.dropTimes[index] < 0)
            {
                int i = index;
                drop = UIKit.Button(row, "投入", () => Sim.DropIngredient(o.id, i), ButtonStyle.Primary, 15);
                UIKit.Place(drop.GetComponent<RectTransform>(), new Vector2(1, .5f), Vector2.zero, new Vector2(70, 38));
            }
            doneness.Add((index, fill, marker, hint, drop));
        }

        private void BuildFinishing(CustomerOrder o)
        {
            var content = Step(o.stage == OrderStage.Drained ? "③ 湯切り → 仕上げ" : "④ 仕上げ", "振って湯切り（3〜4回）→ スープを長押しで注ぐ → ラー油と薬味 →「完成」。");
            var shakeRow = UIKit.Rect("Shake", content);
            shakeRow.anchorMin = new Vector2(0, 1); shakeRow.anchorMax = new Vector2(1, 1); shakeRow.pivot = new Vector2(.5f, 1);
            shakeRow.offsetMin = new Vector2(0, -44); shakeRow.offsetMax = Vector2.zero;
            var shake = UIKit.Button(shakeRow, "振る", () => Sim.Shake(o.id), ButtonStyle.Secondary, 18, IconKind.Drop, o.stage == OrderStage.Drained);
            shakeButton = shake;
            UIKit.Place(shake.GetComponent<RectTransform>(), new Vector2(0, .5f), Vector2.zero, new Vector2(140, 42));
            shakeLabel = UIKit.Label(shakeRow, "", 17, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place(shakeLabel.rectTransform, new Vector2(0, .5f), new Vector2(150, 0), new Vector2(290, 40));

            var soups = Sim.Customers.AvailableSoups.ToArray();
            if (string.IsNullOrEmpty(selectedSoup)) selectedSoup = string.IsNullOrEmpty(o.chosenSoup) ? "" : o.chosenSoup;
            var soupRow = UIKit.Rect("Soups", content);
            soupRow.anchorMin = new Vector2(0, 1); soupRow.anchorMax = new Vector2(1, 1); soupRow.pivot = new Vector2(.5f, 1);
            soupRow.offsetMin = new Vector2(0, -122); soupRow.offsetMax = new Vector2(0, -50);
            var grid = soupRow.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(144, 34); grid.spacing = new Vector2(4, 4);
            foreach (var soup in soups)
            {
                string id = soup.Id;
                var b = UIKit.Button(soupRow, soup.Name, () => { selectedSoup = id; Refresh(); }, selectedSoup == id ? ButtonStyle.TabActive : ButtonStyle.Tab, 15);
                var swatch = UIKit.Panel(b.transform, new Color(soup.R, soup.G, soup.B), 6, false, "Swatch");
                UIKit.Place(swatch.rectTransform, new Vector2(0, .5f), new Vector2(6, 0), new Vector2(14, 14));
            }
            var pourRow = UIKit.Rect("Pour", content);
            pourRow.anchorMin = new Vector2(0, 1); pourRow.anchorMax = new Vector2(1, 1); pourRow.pivot = new Vector2(.5f, 1);
            pourRow.offsetMin = new Vector2(0, -172); pourRow.offsetMax = new Vector2(0, -126);
            var pour = UIKit.Button(pourRow, "長押しで注ぐ", null, ButtonStyle.Primary, 17, IconKind.Ladle, !string.IsNullOrEmpty(selectedSoup));
            UIKit.Place(pour.GetComponent<RectTransform>(), new Vector2(0, .5f), Vector2.zero, new Vector2(176, 46));
            pourButton = pour.gameObject.AddComponent<HoldButton>();
            var gauge = UIKit.Bar(pourRow, new Color(.82f, .8f, .78f), new Color(.75f, .25f, .12f), out soupFill, 8);
            UIKit.Place(gauge, new Vector2(0, .5f), new Vector2(186, 6), new Vector2(250, 18));
            soupBand = UIKit.Rect("Ideal", gauge); var band = soupBand.gameObject.AddComponent<Image>(); band.color = new Color(.3f, .7f, .35f, .45f); band.raycastTarget = false;
            soupBand.offsetMin = soupBand.offsetMax = Vector2.zero;
            soupLabel = UIKit.Label(pourRow, "", 14, UIKit.InkSoft, TextAnchor.MiddleLeft);
            UIKit.Place(soupLabel.rectTransform, new Vector2(0, .5f), new Vector2(186, -16), new Vector2(250, 20));

            var spiceRow = UIKit.Rect("Spice", content);
            spiceRow.anchorMin = new Vector2(0, 1); spiceRow.anchorMax = new Vector2(1, 1); spiceRow.pivot = new Vector2(.5f, 1);
            spiceRow.offsetMin = new Vector2(0, -218); spiceRow.offsetMax = new Vector2(0, -176);
            var chili = UIKit.Button(spiceRow, "ラー油", () => Sim.AddCondiment(o.id, Content.ChiliOil), ButtonStyle.Danger, 17, IconKind.Chili);
            UIKit.Place(chili.GetComponent<RectTransform>(), new Vector2(0, .5f), Vector2.zero, new Vector2(140, 40));
            spiceLabel = UIKit.Label(spiceRow, "", 18, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(spiceLabel.rectTransform, new Vector2(0, .5f), new Vector2(150, 0), new Vector2(200, 40));
            var reset = UIKit.Button(spiceRow, "やり直し", () => Sim.ResetSeasoning(o.id), ButtonStyle.Tab, 14, IconKind.Rotate, o.stage == OrderStage.Finishing);
            UIKit.Place(reset.GetComponent<RectTransform>(), new Vector2(1, .5f), Vector2.zero, new Vector2(100, 38));

            var condRow = UIKit.Rect("Condiments", content);
            condRow.anchorMin = new Vector2(0, 0); condRow.anchorMax = new Vector2(1, 1); condRow.offsetMin = new Vector2(0, 64); condRow.offsetMax = new Vector2(0, -224);
            var condGrid = condRow.gameObject.AddComponent<GridLayoutGroup>();
            condGrid.cellSize = new Vector2(108, 34); condGrid.spacing = new Vector2(4, 4);
            foreach (var condiment in Sim.Customers.AvailableCondiments.Where(x => x.Id != Content.ChiliOil))
            {
                string id = condiment.Id;
                bool wanted = o.requestedCondiments.Contains(id), disliked = o.dislikedCondiments.Contains(id);
                var b = UIKit.Button(condRow, condiment.Name, () => Sim.AddCondiment(o.id, id), wanted ? ButtonStyle.Gold : ButtonStyle.Tab, 13);
                if (disliked) b.GetComponentInChildren<Text>().text = condiment.Name + "\n抜きで";
                var count = UIKit.Label(b.transform, "", 13, UIKit.Lacquer, TextAnchor.UpperRight, true);
                UIKit.Fill(count.rectTransform, 2, 5, 1, 2);
                condimentCounts.Add((id, count));
            }
            BigButton(content, "完成！", () => Sim.Finish(o.id), ButtonStyle.Primary, IconKind.Sparkle, true);
        }

        /// <summary>完成した一杯の画像・品質・レシピ・相性と評価内訳を表示する。</summary>
        /// <summary>拡大写真と品質内訳をスクロールで切り替え、提供操作は固定表示します。</summary>
        private void BuildResult(CustomerOrder o)
        {
            Text heading = UIKit.Label(body, "完成！ " + o.quality.ToString("0") + "点", 23, UIKit.Lacquer, TextAnchor.MiddleLeft, true, "Step");
            PlaceResultText(heading.rectTransform, 0f, 0f, 32f);
            heading.rectTransform.offsetMax = new Vector2(-166f, 0f);
            RectTransform content = UIKit.Rect("Content", body);
            UIKit.Fill(content, 0f, 0f, 36f, 0f);
            var r = o.report;
            RectTransform resultArea = UIKit.Rect("Result Area", content);
            UIKit.Fill(resultArea, 0, 0, 0, 122);
            RectTransform list = UIKit.ScrollList(resultArea, 8, 0, "Result Details");
            UIKit.Fill(list.parent as RectTransform);
            ScrollRect resultScroll = list.parent.GetComponent<ScrollRect>();
            RectTransform metrics = null;
            Button details = null;
            details = UIKit.Button(body, "品質内訳 ↓", () =>
            {
                Canvas.ForceUpdateCanvases();
                bool showDetails = resultScroll.verticalNormalizedPosition > .5f;
                resultScroll.StopMovement();
                float scrollRange = Mathf.Max(0f, list.rect.height - resultScroll.viewport.rect.height);
                float metricsTop = -list.InverseTransformPoint(metrics.TransformPoint(new Vector3(metrics.rect.xMin, metrics.rect.yMax, 0f))).y;
                resultScroll.verticalNormalizedPosition = showDetails && scrollRange > 0f ? 1f - Mathf.Clamp01(metricsTop / scrollRange) : 1f;
                details.GetComponentInChildren<Text>().text = showDetails ? "一杯の写真 ↑" : "品質内訳 ↓";
            }, ButtonStyle.Secondary, 15, null, true, "Quality Details Toggle");
            UIKit.Place(details.GetComponent<RectTransform>(), new Vector2(1f, 1f), Vector2.zero, new Vector2(154f, 32f));
            RectTransform overview = UIKit.Rect("Bowl Overview", list);
            UIKit.Height(overview, 184);
            resultOverview = overview;
            RawImage portrait = UIKit.Rect("Bowl Portrait", overview).gameObject.AddComponent<RawImage>();
            portrait.texture = BowlPortrait.Get(o);
            portrait.raycastTarget = false;
            resultPortrait = portrait;
            RecipeDefinition recipe = Recipes.Find(o.recipe);
            Text recipeLabel = UIKit.Label(overview, recipe == null ? "隠しレシピ：なし" : recipe.Name, 18, recipe == null ? UIKit.InkSoft : UIKit.Lacquer, TextAnchor.UpperLeft, true, "Recipe");
            resultRecipe = recipeLabel;
            List<PairingDefinition> pairs = o.pairings.Select(Pairings.Find).Where(pair => pair != null).ToList();
            string pairingText = pairs.Count == 0 ? "相性：なし" : "相性：" + string.Join("・", pairs.Select(pair => pair.Name)) + "\n品質 +" + Pairings.Bonus(pairs.Count).ToString("0.#");
            Text pairingLabel = UIKit.Label(overview, pairingText, 14, UIKit.InkSoft, TextAnchor.UpperLeft, false, "Pairings");
            resultPairings = pairingLabel;
            var stars = UIKit.Label(overview, UIKit.Stars(o.quality) + "  " + o.quality.ToString("0") + "点", 23, UIKit.Gold, TextAnchor.MiddleCenter, true);
            UIKit.Outlined(stars, new Color(.4f, .2f, .05f, .5f), 1.2f);
            resultStars = stars;
            resultReveal = 0; shownResultScore = -1;
            bool rare = o.selected.Any(MorningMarket.RareIds.Contains);
            resultRare = rare ? UIKit.Label(overview, "希少食材の特別な一杯", 16, UIKit.Lacquer, TextAnchor.MiddleLeft, true, "Rare Badge") : null;
            if (o.quality >= 80)
            {
                var stamp = UIKit.Label(overview, "お見事", 28, UIKit.Lacquer, TextAnchor.MiddleCenter, true, "Finish Stamp");
                UIKit.Place(stamp.rectTransform, new Vector2(1, 1), new Vector2(-8, -12), new Vector2(120, 54));
                stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, -13);
                stamp.gameObject.AddComponent<PopIn>().Delay = .7f;
            }
            LayoutResultOverview();
            metrics = UIKit.Rect("Quality Details", list);
            UIKit.Height(metrics, 186);
            var rows = new[] { ("具材", r.accuracy), ("茹で加減", r.doneness), ("湯切り", r.drain), ("スープ", r.soup), ("味付け", r.seasoning), ("速さ", r.speed), ("鮮度", r.freshness) };
            for (int i = 0; i < rows.Length; i++)
            {
                var label = UIKit.Label(metrics, rows[i].Item1, 15, UIKit.Ink, TextAnchor.MiddleLeft);
                UIKit.Place(label.rectTransform, new Vector2(0, 1), new Vector2(0, -i * 26), new Vector2(90, 24));
                var bar = UIKit.Bar(metrics, new Color(.85f, .82f, .78f), rows[i].Item2 >= 85 ? UIKit.Good : rows[i].Item2 >= 60 ? UIKit.Warn : UIKit.Bad, out var fill, 6);
                bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = Vector2.one;
                bar.offsetMin = new Vector2(92f, -18f - i * 26f);
                bar.offsetMax = new Vector2(-54f, -6f - i * 26f);
                UIKit.SetBar(fill, rows[i].Item2 / 100f);
                var value = UIKit.Label(metrics, rows[i].Item2.ToString("0"), 15, UIKit.InkSoft, TextAnchor.MiddleRight);
                UIKit.Place(value.rectTransform, new Vector2(1, 1), new Vector2(0, -i * 26), new Vector2(50, 24));
            }
            var comment = UIKit.Label(list, "「" + string.Join(" ", r.comments) + "」", 16, UIKit.InkSoft, TextAnchor.UpperLeft);
            UIKit.Height(comment, 84);
            var signature = UIKit.Button(content, "名物メニューに登録", () => Sim.RegisterSignature(o.id, ""), ButtonStyle.Gold, 17, IconKind.Star, o.quality >= 75 && S.signatures.Count < 3 && string.IsNullOrEmpty(o.signatureId));
            var srt = signature.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(.5f, 0); srt.offsetMin = new Vector2(0, 66); srt.offsetMax = new Vector2(0, 112);
            BigButton(content, o.takeout ? "レジで渡す" : "席へ運ぶ", () => App.SelectOrder(o.id), ButtonStyle.Primary, IconKind.Bowl);
        }

        /// <summary>完成写真をカード幅いっぱいに広げ、点数と説明は下へ積んでスクロールで確認できます。</summary>
        private void LayoutResultOverview()
        {
            if (resultOverview == null || resultPortrait == null) return;
            float width = resultOverview.rect.width;
            if (width <= 0f || Mathf.Approximately(width, resultWidth)) return;
            resultWidth = width;
            float size = width;
            UIKit.Place(resultPortrait.rectTransform, new Vector2(.5f, 1f), Vector2.zero, new Vector2(size, size));
            UIKit.Height(resultOverview, size + (resultRare != null ? 184f : 152f));
            PlaceResultText(resultStars.rectTransform, 0f, size, 38f);
            PlaceResultText(resultRecipe.rectTransform, 0f, size + 42f, 36f);
            PlaceResultText(resultPairings.rectTransform, 0f, size + 82f, 66f);
            if (resultRare != null) PlaceResultText(resultRare.rectTransform, 0, size + 150, 30);
        }

        /// <summary>完成概要の文字領域を、残りの横幅いっぱいに収めます。</summary>
        private static void PlaceResultText(RectTransform target, float left, float top, float height)
        {
            target.anchorMin = new Vector2(0f, 1f); target.anchorMax = Vector2.one;
            target.pivot = new Vector2(0f, 1f);
            target.offsetMin = new Vector2(left, -top - height);
            target.offsetMax = new Vector2(0f, -top);
        }

        private void RefreshSwitcher()
        {
            var orders = Sim.Active.Where(x => x.stage != OrderStage.Waiting && !x.handledByStaff).ToList();
            int queue = Sim.Queue.Count();
            string signature = string.Join("|", orders.Select(x => x.id + (int)x.stage)) + ":" + queue + ":" + App.SelectedOrderId;
            if (signature == switcherSignature) return;
            switcherSignature = signature;
            Clear(switcher);
            foreach (var order in orders.Take(6))
            {
                string id = order.id;
                bool active = id == App.SelectedOrderId;
                var b = UIKit.Button(switcher, order.customerName.Replace("さん", ""), () => { App.SelectedOrderId = id; Sim.PlayerFocusOrderId = id; selectedSoup = ""; Refresh(); },
                    active ? ButtonStyle.TabActive : ButtonStyle.Tab, 15, HudView.StageIcon(order));
                b.GetComponent<RectTransform>().sizeDelta = new Vector2(132, 54);
            }
            if (queue > 0)
            {
                var next = UIKit.Button(switcher, "次の客（" + queue + "）", () => App.SelectOrder(Sim.Queue.First().id), ButtonStyle.Gold, 15, IconKind.Bell);
                next.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 54);
            }
        }

        public override void Tick(float deltaTime)
        {
            var o = Order;
            if (o == null) return;
            if (o.stage == OrderStage.Ready)
            {
                LayoutResultOverview();
                resultReveal = S.reducedMotion ? 2 : resultReveal + deltaTime;
                int score = Mathf.RoundToInt(o.quality * Mathf.SmoothStep(0, 1, resultReveal / .8f));
                if (resultStars != null && score != shownResultScore) { shownResultScore = score; resultStars.text = UIKit.Stars(score) + "  " + score + "点"; }
                if (resultRecipe != null) resultRecipe.gameObject.SetActive(resultReveal >= .35f);
                if (resultPairings != null) resultPairings.gameObject.SetActive(resultReveal >= .7f);
                if (resultRare != null) resultRare.gameObject.SetActive(resultReveal >= 1.05f);
            }
            if (weighLabel != null) weighLabel.text = o.omakase
                ? o.selected.Count + "品　" + Sim.Grams(o).ToString("0") + "g ／ 目安" + o.targetGrams.ToString("0") + "g（予算 " + RestaurantSimulation.Money(o.budgetFen) + " 受領済み）"
                : o.selected.Count + "品　" + Sim.Grams(o).ToString("0") + "g　→　" + RestaurantSimulation.Money(Sim.PriceFor(o));
            if (timerLabel != null)
            {
                bool expert = S.tutorialComplete && S.gaugeMode == 2;
                float left = Sim.SecondsUntilDone(o);
                timerLabel.text = expert ? "色・泡・湯気を見て判断しましょう" : left > 0 ? "全部茹で上がるまで 約" + Mathf.CeilToInt(left) + "秒" : "茹で上がり！引き上げましょう";
                timerLabel.color = left > 0 ? UIKit.Ink : UIKit.Good;
                boilWarning.gameObject.SetActive(o.boiledOver);
            }
            foreach (var (index, fill, marker, hint, drop) in doneness)
            {
                if (fill == null || marker == null || hint == null || index >= o.selected.Count || index >= o.dropTimes.Count) continue;
                float ratio = Sim.Doneness(o, index);
                bool numbers = !S.tutorialComplete || S.gaugeMode == 0;
                fill.transform.parent.gameObject.SetActive(numbers);
                if (S.tutorialComplete && S.gaugeMode == 2)
                {
                    hint.text = o.dropTimes[index] < 0 ? "投入待ち" : "鍋を見て判断";
                    continue;
                }
                fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(ratio / 1.8f), 1);
                marker.anchorMin = new Vector2(Mathf.Clamp01(ratio / 1.8f), -.2f); marker.anchorMax = new Vector2(Mathf.Clamp01(ratio / 1.8f), 1.2f);
                if (o.dropTimes[index] < 0)
                {
                    float wait = RecommendedDrop(o, index) - o.heatSeconds;
                    hint.text = wait <= .5f ? "今！" : "あと" + Mathf.CeilToInt(wait / Sim.CookSpeedMultiplier) + "秒で投入";
                    if (drop != null) drop.transform.localScale = Vector3.one * (wait <= .5f ? 1 + Mathf.Sin(Time.unscaledTime * 10) * .06f : 1);
                }
                else hint.text = ratio < QualityEvaluator.PerfectDonenessMin ? "○ 茹で中" : ratio <= QualityEvaluator.PerfectDonenessMax ? "✓ 食べごろ" : "！ 煮えすぎ";
            }
            if (shakeButton != null) shakeButton.interactable = o.stage == OrderStage.Drained;
            if (shakeLabel != null)
                shakeLabel.text = "湯切り " + o.shakes + "回 " + (o.shakes >= 3 && o.shakes <= 4 ? "◎ばっちり" : o.shakes > 4 ? "振りすぎ" : "（3〜4回）");
            if (pourButton != null && pourButton.IsHeld && !string.IsNullOrEmpty(selectedSoup))
            {
                Sim.PourSoup(o.id, selectedSoup, 150f * deltaTime);
                App.World.Kitchen?.SetPouring(true);
            }
            else App.World.Kitchen?.SetPouring(false);
            if (soupFill != null && soupBand != null && soupLabel != null)
            {
                var soup = Content.Soup(string.IsNullOrEmpty(selectedSoup) ? o.soup : selectedSoup);
                float ml = o.chosenSoup == selectedSoup ? o.soupMl : 0;
                UIKit.SetBar(soupFill, ml / 500f);
                soupFill.color = new Color(soup.R, soup.G, soup.B);
                soupBand.anchorMin = new Vector2(soup.IdealMinMl / 500f, 0); soupBand.anchorMax = new Vector2(soup.IdealMaxMl / 500f, 1);
                soupLabel.text = string.IsNullOrEmpty(selectedSoup) ? "スープを選んでください" : ml.ToString("0") + " ml（目安 " + soup.IdealMinMl.ToString("0") + "〜" + soup.IdealMaxMl.ToString("0") + "）";
            }
            if (spiceLabel != null) spiceLabel.text = "辛さ " + o.chosenSpice + " / 希望 " + o.spice;
            foreach (var (id, count) in condimentCounts)
            {
                if (count == null) continue;
                int n = o.condiments.Count(x => x == id);
                count.text = n > 0 ? "×" + n : "";
            }
        }

        /// <summary>全具材が同時に仕上がる投入時刻（加熱秒）。</summary>
        private float RecommendedDrop(CustomerOrder o, int index)
        {
            float finish = 0;
            bool anyDropped = false;
            for (int i = 0; i < o.selected.Count; i++)
            {
                float cook = Sim.Catalog.Get(o.selected[i]).cookingSeconds;
                if (o.dropTimes[i] >= 0) { finish = Mathf.Max(finish, o.dropTimes[i] + cook); anyDropped = true; }
            }
            float slowestUndropped = Enumerable.Range(0, o.selected.Count).Where(i => o.dropTimes[i] < 0).Select(i => Sim.Catalog.Get(o.selected[i]).cookingSeconds).DefaultIfEmpty(0).Max();
            finish = Mathf.Max(anyDropped ? finish : 0, o.heatSeconds + slowestUndropped);
            return finish - Sim.Catalog.Get(o.selected[index]).cookingSeconds;
        }
    }
}
