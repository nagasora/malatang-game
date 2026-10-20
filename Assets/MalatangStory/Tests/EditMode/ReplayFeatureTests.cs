using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>v1.2のレシピ・章・テーマ・スキル・行動案内・保存互換と外見の動作を保証する。</summary>
    public class ReplayFeatureTests
    {
        /// <summary>チュートリアルを完了した営業中の店舗を作る。</summary>
        private static RestaurantSimulation Open()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            Assert.IsTrue(sim.BeginDay(), "検証用の店を開店できる");
            return sim;
        }

        /// <summary>既存テストと同じ形式で待機注文を追加する。</summary>
        private static CustomerOrder Waiting(RestaurantSimulation sim, OrderStyle style, string archetype = "office", params string[] items)
        {
            var o = new CustomerOrder { id = "replay-" + sim.State.orders.Count, customerName = "テストさん", archetype = archetype,
                style = style, stage = OrderStage.Waiting, soup = "mala", spice = 2, patience = 999 };
            o.requested.AddRange(items.Length > 0 ? items : new[] { "bok_choy", "enoki" });
            if (style == OrderStyle.SelfServe) CustomerDirector.SelectAll(o);
            sim.State.orders.Add(o);
            return o;
        }

        /// <summary>全具材を解禁して各10個を用意する。</summary>
        private static void StockAll(RestaurantSimulation sim)
        {
            sim.State.shopLevel = Content.MaxShopLevel;
            foreach (var item in sim.Catalog.ingredients)
            {
                if (!sim.State.ingredients.unlockedIds.Contains(item.id)) sim.State.ingredients.unlockedIds.Add(item.id);
                sim.StockFor(item.id).quantity = 10;
            }
        }

        /// <summary>レシピ条件に一致する完成内容を作る。</summary>
        private static CustomerOrder Bowl(string id)
        {
            var recipe = Recipes.Find(id);
            var o = new CustomerOrder { chosenSoup = string.IsNullOrEmpty(recipe.Soup) ? "mala" : recipe.Soup,
                chosenSpice = recipe.MinSpice };
            o.selected.AddRange(recipe.Ingredients);
            o.condiments.AddRange(recipe.Condiments);
            return o;
        }

        /// <summary>先頭二章の全目標を満たす累計状態を用意する。</summary>
        private static void CompleteFirstTwoGoals(GameState s)
        {
            s.stats.served = 80;
            s.shopLevel = 3;
            s.reputation = 65;
            s.stats.missionsCompleted = 5;
            s.regulars.Add(new RegularCustomer { id = "r1" });
            s.regulars.Add(new RegularCustomer { id = "r2" });
            foreach (var r in Recipes.All.Take(3)) LifetimeStats.Add(s.recipes, r.Id);
        }

        /// <summary>案内優先順位の検証用に任意段階の注文を追加する。</summary>
        private static CustomerOrder AtStage(RestaurantSimulation sim, OrderStage stage)
        {
            var o = Waiting(sim, OrderStyle.Ordered);
            o.stage = stage;
            o.takeout = true;
            return o;
        }

        /// <summary>製品と同じチェックサム付き旧保存包絡を表す。</summary>
        [Serializable]
        private sealed class LegacyEnvelope
        {
            public int version;
            public string payload;
            public string checksum;
        }

        /// <summary>一時保存フォルダーを用意し、失敗時も削除する。</summary>
        private static void WithRepository(Action<SaveRepository, string> verify)
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-replay-" + Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "save.json");
                verify(new SaveRepository(path), path);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        /// <summary>全レシピの具材・調味料・スープ・辛さ両端が条件に一致する。</summary>
        [TestCase("tomato_egg")]
        [TestCase("beef_mala")]
        [TestCase("mushroom_forest")]
        [TestCase("seafood")]
        [TestCase("tofu_trio")]
        [TestCase("green_garden")]
        [TestCase("oden")]
        [TestCase("sesame_noodle")]
        [TestCase("chongqing")]
        [TestCase("sour_fish")]
        [TestCase("lamb_baitang")]
        [TestCase("kids_plate")]
        [TestCase("dumpling_party")]
        [TestCase("stamina")]
        [TestCase("sea_greens")]
        [TestCase("golden")]
        public void RecipeMatchesRequiredContentsAndBothSpiceBoundaries(string id)
        {
            var r = Recipes.Find(id);
            var o = Bowl(id);
            Assert.IsTrue(Recipes.Matches(r, o), "辛さ下限を含む正しい組合せに一致する");
            Assert.AreSame(r, Recipes.Match(o), "単独レシピの内容から正しいレシピを選ぶ");
            o.chosenSpice = r.MaxSpice;
            Assert.IsTrue(Recipes.Matches(r, o), "辛さ上限を含む");
            o.chosenSpice = r.MinSpice - 1;
            Assert.IsFalse(Recipes.Matches(r, o), "下限未満は一致しない");
            o.chosenSpice = r.MaxSpice + 1;
            Assert.IsFalse(Recipes.Matches(r, o), "上限超過は一致しない");
            o.chosenSpice = r.MinSpice;
            foreach (string item in r.Ingredients)
            {
                o.selected.Remove(item);
                Assert.IsFalse(Recipes.Matches(r, o), "必須具材「" + item + "」の欠落を拒否する");
                o.selected.Add(item);
            }
            foreach (string condiment in r.Condiments)
            {
                o.condiments.Remove(condiment);
                Assert.IsFalse(Recipes.Matches(r, o), "必須調味料「" + condiment + "」の欠落を拒否する");
                o.condiments.Add(condiment);
            }
            o.chosenSoup = "baitang" == r.Soup ? "mala" : "baitang";
            Assert.AreEqual(string.IsNullOrEmpty(r.Soup), Recipes.Matches(r, o), "スープ指定がある場合だけ違うスープを拒否する");
        }

        /// <summary>具材の追加は許容し、複数一致では条件数が多いレシピを選ぶ。</summary>
        [Test]
        public void MatchChoosesMostSpecificRecipeAndAllowsExtraIngredients()
        {
            var o = Bowl("golden");
            o.selected.AddRange(Recipes.Find("seafood").Ingredients);
            Assert.IsTrue(Recipes.Matches(Recipes.Find("seafood"), o), "具材余分でも条件は成立する");
            Assert.AreEqual("golden", Recipes.Match(o).Id, "具材3条件よりスープ込み4条件を優先する");
            o.selected.Clear();
            Assert.IsNull(Recipes.Match(o), "どれにも一致しなければnullを返す");
        }

        /// <summary>調味料の条件も具体性へ数え、同じスープで複数一致したレシピを選ぶ。</summary>
        [Test]
        public void MatchCountsCondimentRequirementsWhenChoosingSpecificRecipe()
        {
            var o = Bowl("chongqing");
            o.selected.Add("beef_slice");
            Assert.IsTrue(Recipes.Matches(Recipes.Find("beef_mala"), o), "牛づくしも同時に成立する");
            Assert.AreEqual("chongqing", Recipes.Match(o).Id, "具材とスープに加え調味料条件のある重慶を優先する");
        }

        /// <summary>レシピ加点は上限100とコメント先頭の発見文を保証する。</summary>
        [TestCase(80f, 89f)]
        [TestCase(98f, 100f)]
        [TestCase(100f, 100f)]
        public void ApplyBonusAddsQualityWithCapAndPrependsComment(float initial, float expected)
        {
            var o = Bowl("golden");
            o.report.total = initial;
            o.report.comments.Add("元のコメント");
            Recipes.ApplyBonus(o);
            Assert.AreEqual(expected, o.report.total, "伝説レシピの9点加算は100を超えない");
            Assert.AreEqual("golden", o.recipe, "一致したレシピIDを保存する");
            StringAssert.Contains(Recipes.Find("golden").Name, o.report.comments[0], "レシピのコメントが先頭に入る");
            Assert.AreEqual("元のコメント", o.report.comments[1], "既存コメントを失わない");
        }

        /// <summary>不一致時はレシピIDを消し、品質とコメントを変えない。</summary>
        [Test]
        public void ApplyBonusWithoutMatchClearsRecipeWithoutChangingQuality()
        {
            var o = new CustomerOrder { recipe = "golden" };
            o.report.total = 80;
            o.report.comments.Add("元のコメント");
            Recipes.ApplyBonus(o);
            Assert.IsEmpty(o.recipe, "一致しない一杯に古いレシピを残さない");
            Assert.AreEqual(80, o.report.total, "不一致なら加点しない");
            CollectionAssert.AreEqual(new[] { "元のコメント" }, o.report.comments, "不一致ならコメントを追加しない");
        }

        /// <summary>発見イベントと日報は初回のみで、二杯目も回数と累計杯数を増やす。</summary>
        [Test]
        public void RecordServedDiscoversOnceAndCountsEveryRecipeBowl()
        {
            var sim = Open();
            var events = new List<SimEvent>();
            sim.Event += events.Add;
            var o = new CustomerOrder { recipe = "golden" };
            Recipes.RecordServed(sim, o);
            Assert.AreEqual(1, events.Count(e => e.Kind == SimEventKind.RecipeDiscovered), "初回だけ発見通知を送る");
            Assert.AreEqual("golden", events.Single(e => e.Kind == SimEventKind.RecipeDiscovered).Id, "発見したIDを通知する");
            Assert.AreEqual(1, sim.State.report.highlights.Count(h => h.Contains("隠しレシピ発見")), "初回の発見を日報へ載せる");
            Recipes.RecordServed(sim, new CustomerOrder { recipe = "golden" });
            Assert.AreEqual(1, events.Count(e => e.Kind == SimEventKind.RecipeDiscovered), "再提供は再発見しない");
            Assert.AreEqual(1, sim.State.report.highlights.Count(h => h.Contains("隠しレシピ発見")), "再提供は発見日報を増やさない");
            Assert.AreEqual(2, LifetimeStats.Get(sim.State.recipes, "golden"), "レシピ提供回数は二杯分記録する");
            Assert.AreEqual(2, sim.State.stats.recipeBowls, "累計レシピ杯数も二杯分記録する");
            Recipes.RecordServed(sim, new CustomerOrder { recipe = "unknown" });
            Assert.AreEqual(2, sim.State.stats.recipeBowls, "未知のレシピを杯数に算入しない");
        }

        /// <summary>名指ししたレシピを成立させたときだけ接客に5点加える。</summary>
        [TestCase("golden", "golden", 5f)]
        [TestCase("seafood", "golden", 0f)]
        [TestCase("", "golden", 0f)]
        [TestCase("", "", 0f)]
        public void NamedRecipeServiceBonusRequiresFulfilledRequest(string request, string made, float bonus)
        {
            var sim = Open();
            var o = new CustomerOrder { recipeRequest = request, recipe = made, serviceBonus = 2 };
            Recipes.RecordServed(sim, o);
            Assert.AreEqual(2 + bonus, o.serviceBonus, "名指しと実際のレシピが一致した場合だけ+5する");
        }

        /// <summary>発見・具材解禁・在庫・スープ解禁・調味料解禁を全て満たす場合だけ注文できる。</summary>
        [Test]
        public void OrderableRequiresDiscoveryUnlocksStockSoupAndCondiments()
        {
            var sim = Open();
            StockAll(sim);
            var archetype = Content.Archetype("office");
            Assert.IsEmpty(Recipes.Orderable(sim, archetype), "未発見レシピは注文候補にしない");
            LifetimeStats.Add(sim.State.recipes, "sesame_noodle");
            Assert.IsTrue(Recipes.Orderable(sim, archetype).Any(r => r.Id == "sesame_noodle"), "全条件を満たす発見済みレシピは注文可能");
            sim.StockFor("chinese_noodle").quantity = 0;
            Assert.IsEmpty(Recipes.Orderable(sim, archetype), "必須具材の在庫なしは注文不可");
            sim.StockFor("chinese_noodle").quantity = 10;
            sim.State.ingredients.unlockedIds.Remove("chinese_noodle");
            Assert.IsEmpty(Recipes.Orderable(sim, archetype), "在庫があっても未解禁具材は注文不可");
            sim.State.ingredients.unlockedIds.Add("chinese_noodle");
            sim.State.shopLevel = 1;
            Assert.IsEmpty(Recipes.Orderable(sim, archetype), "汁なしスープ未解禁は注文不可");
            sim.State.shopLevel = Content.MaxShopLevel;
            var condimentLocked = new CustomerArchetype { MinSpice = 0, MaxSpice = 5 };
            var recipe = Recipes.Find("stamina");
            LifetimeStats.Add(sim.State.recipes, recipe.Id);
            int garlicLevel = Content.Condiment("garlic").UnlockLevel;
            sim.State.shopLevel = garlicLevel - 1;
            Assert.IsFalse(Recipes.Orderable(sim, condimentLocked).Any(r => r.Id == recipe.Id), "スープ指定なしでも必須調味料未解禁は注文不可");
        }

        /// <summary>ベジタリアン・子ども・辛党の食材制限と辛さがレシピと両立する。</summary>
        [TestCase("vegetarian")]
        [TestCase("family")]
        [TestCase("spicy")]
        public void OrderableRespectsCustomerDietDislikesAndSpice(string archetypeId)
        {
            var sim = Open();
            StockAll(sim);
            foreach (var r in Recipes.All) LifetimeStats.Add(sim.State.recipes, r.Id);
            var a = Content.Archetype(archetypeId);
            var choices = Recipes.Orderable(sim, a);
            Assert.IsNotEmpty(choices, "制限のある客にも注文可能なレシピが残る");
            foreach (var r in choices)
            {
                Assert.IsFalse(r.Ingredients.Any(id => a.ForbiddenCategories.Contains(sim.Catalog.Get(id).category)), "禁止カテゴリーを候補にしない");
                Assert.IsFalse(r.Condiments.Any(a.DislikedCondiments.Contains), "嫌いな調味料を候補にしない");
                int spice = Recipes.SpiceFor(r, a);
                Assert.That(spice, Is.InRange(r.MinSpice, r.MaxSpice), "レシピの辛さ範囲を守る");
                Assert.That(spice, Is.InRange(a.Child ? 0 : a.MinSpice, a.Child ? 0 : a.MaxSpice), "客の辛さ範囲を守る");
            }
            if (archetypeId == "vegetarian") Assert.IsFalse(choices.Any(r => r.Id == "beef_mala"), "牛肉レシピを除外する");
            if (a.Child)
            {
                Assert.IsTrue(choices.Any(r => r.Id == "kids_plate"), "お子さまセットを注文できる");
                Assert.IsFalse(choices.Any(r => r.Id == "lamb_baitang"), "香菜入りは子どもの候補にしない");
            }
            if (archetypeId == "spicy") Assert.IsFalse(choices.Any(r => r.Id == "kids_plate"), "辛さ0限定は辛党の候補にしない");
        }

        /// <summary>仕上げだけでは発見せず、実際の提供で初めて発見する。</summary>
        [Test]
        public void FinishThenServeDiscoversRecipeOnlyAfterServing()
        {
            var sim = Open();
            StockAll(sim);
            var o = Waiting(sim, OrderStyle.Ordered, "office", Recipes.Find("tomato_egg").Ingredients);
            o.stage = OrderStage.Finishing;
            o.takeout = true;
            o.soup = o.chosenSoup = "tomato";
            o.spice = o.chosenSpice = 0;
            o.soupMl = 300;
            CustomerDirector.SelectAll(o);
            Assert.IsTrue(sim.Finish(o.id), "条件の揃ったレシピを仕上げられる");
            Assert.AreEqual("tomato_egg", o.recipe, "仕上げでレシピを判定する");
            Assert.AreEqual(o.report.total, o.quality, "加点済み品質を完成品質へ反映する");
            Assert.IsFalse(Recipes.Discovered(sim.State, o.recipe), "未提供の完成品では発見扱いにしない");
            Assert.IsTrue(sim.Serve(o.id), "持ち帰りの完成品を提供できる");
            Assert.IsTrue(Recipes.Discovered(sim.State, "tomato_egg"), "提供で図鑑へ登録する");
            Assert.AreEqual(1, sim.State.stats.recipeBowls, "通し操作でもレシピ杯数を記録する");
            Assert.IsFalse(sim.Serve(o.id), "同じ一杯を二重提供できない");
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.recipes, "tomato_egg"), "二重操作で回数を増やさない");
        }

        /// <summary>未発見状態の同一セーブからは全フィールドが同一の客列を生成する。</summary>
        [Test]
        public void UndiscoveredRecipesPreserveDeterministicCustomerSequence()
        {
            var source = Open();
            StockAll(source);
            string json = JsonUtility.ToJson(source.State);
            var left = new RestaurantSimulation(source.Catalog, JsonUtility.FromJson<GameState>(json));
            var right = new RestaurantSimulation(source.Catalog, JsonUtility.FromJson<GameState>(json));
            for (int i = 0; i < 80; i++)
            {
                var a = left.Customers.Create();
                var b = right.Customers.Create();
                Assert.IsNotNull(a, "在庫がある状態では客を生成する");
                Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b), "同じ状態と注文番号から同じ注文・外見seedを生成する");
                Assert.IsEmpty(a.recipeRequest, "未発見なら名指し注文を生成しない");
            }
        }

        /// <summary>固定状態の十分な客列に名指し注文が出て、必須条件と客の辛さを満たす。</summary>
        [TestCase("office")]
        [TestCase("family")]
        [TestCase("spicy")]
        [TestCase("vegetarian")]
        public void DiscoveredRecipesGenerateNamedOrdersWithValidContents(string archetypeId)
        {
            var sim = Open();
            StockAll(sim);
            foreach (var r in Recipes.All) LifetimeStats.Add(sim.State.recipes, r.Id);
            int named = 0;
            var a = Content.Archetype(archetypeId);
            for (int i = 0; i < 300; i++)
            {
                var o = sim.Customers.Create(archetypeId);
                Assert.IsNotNull(o, "十分な在庫なら指定タイプの客を作れる");
                if (string.IsNullOrEmpty(o.recipeRequest)) continue;
                named++;
                var r = Recipes.Find(o.recipeRequest);
                Assert.IsTrue(r.Ingredients.All(o.requested.Contains), "名指し注文は必須具材を全て含む");
                Assert.IsTrue(r.Condiments.All(o.requestedCondiments.Contains), "名指し注文は必須調味料を全て含む");
                Assert.AreEqual(OrderStyle.Ordered, o.style, "名指しは注文式になる");
                if (!string.IsNullOrEmpty(r.Soup)) Assert.AreEqual(r.Soup, o.soup, "指定スープを注文する");
                Assert.That(o.spice, Is.InRange(r.MinSpice, r.MaxSpice), "レシピの辛さ条件を満たす");
                Assert.That(o.spice, Is.InRange(a.Child ? 0 : a.MinSpice, a.Child ? 0 : a.MaxSpice), "客の辛さ条件も満たす");
                Assert.IsFalse(o.requestedCondiments.Intersect(o.dislikedCondiments).Any(), "要求と嫌いな調味料が矛盾しない");
            }
            Assert.Greater(named, 0, "固定seedの300人以内に発見済みレシピの名指し注文が出る");
        }

        /// <summary>受付で必須食材が在庫切れなら名指し注文を通常注文へ戻す。</summary>
        [Test]
        public void AdaptToStockClearsNamedRecipeWhenRequiredIngredientIsRemoved()
        {
            var sim = Open();
            StockAll(sim);
            var o = Waiting(sim, OrderStyle.Ordered, "office", "egg", "cherry_tomato");
            o.recipeRequest = "tomato_egg";
            o.type = "レシピ注文";
            sim.StockFor("egg").quantity = 0;
            Assert.IsTrue(sim.TakeOrder(o.id), "残る具材があれば通常注文として受付できる");
            CollectionAssert.AreEqual(new[] { "cherry_tomato" }, o.requested, "在庫切れの必須具材を外す");
            Assert.IsEmpty(o.recipeRequest, "成立しない名指しレシピを消す");
            Assert.AreEqual(Content.Archetype("office").Label, o.type, "表示も通常の客タイプへ戻す");
        }

        /// <summary>在庫切れで外したのが余分な具材だけなら名指しを維持する。</summary>
        [Test]
        public void AdaptToStockKeepsNamedRecipeWhenOnlyExtraIngredientIsRemoved()
        {
            var sim = Open();
            StockAll(sim);
            var o = Waiting(sim, OrderStyle.Ordered, "office", "egg", "cherry_tomato", "enoki");
            o.recipeRequest = "tomato_egg";
            sim.StockFor("enoki").quantity = 0;
            Assert.IsTrue(sim.TakeOrder(o.id), "必須具材の在庫があれば受付できる");
            Assert.AreEqual("tomato_egg", o.recipeRequest, "必須具材が残ったレシピの名指しを維持する");
        }

        /// <summary>先頭章の目標を一つでも未達にすると進まない。</summary>
        [TestCase("served")]
        [TestCase("regulars")]
        [TestCase("level")]
        public void ChapterDoesNotAdvanceWhenAnyGoalIsBelowTarget(string missing)
        {
            var sim = Open();
            CompleteFirstTwoGoals(sim.State);
            if (missing == "served") sim.State.stats.served = 19;
            if (missing == "regulars") sim.State.regulars.RemoveAt(0);
            if (missing == "level") sim.State.shopLevel = 2;
            long money = sim.State.moneyFen;
            Assert.IsFalse(Chapters.Check(sim), "目標「" + missing + "」が未達なら進まない");
            Assert.AreEqual(0, sim.State.chapter, "未達では章番号を変えない");
            Assert.AreEqual(money, sim.State.moneyFen, "未達では報酬を支払わない");
        }

        /// <summary>達成報酬は現金と利益へ同額入り、一回の判定で一章のみ進む。</summary>
        [Test]
        public void ChapterRewardKeepsCashAndProfitConsistentAndAdvancesOnlyOneChapter()
        {
            var sim = Open();
            CompleteFirstTwoGoals(sim.State);
            var events = new List<SimEvent>();
            sim.Event += events.Add;
            long cash = sim.State.moneyFen, profit = sim.State.report.ProfitFen;
            Assert.IsTrue(Chapters.Check(sim), "全目標を満たす章は完了する");
            Assert.AreEqual(1, sim.State.chapter, "次章も達成済みでも一回で一章だけ進む");
            Assert.AreEqual(cash + Chapters.All[0].RewardFen, sim.State.moneyFen, "報酬を現金に入れる");
            Assert.AreEqual(Chapters.All[0].RewardFen, sim.State.report.missionRewardsFen, "日報の報酬へ計上する");
            Assert.AreEqual(profit + Chapters.All[0].RewardFen, sim.State.report.ProfitFen, "現金増加と利益増加が一致する");
            Assert.AreEqual(1, events.Count(e => e.Kind == SimEventKind.ChapterComplete), "一章だけ完了通知する");
            Assert.AreEqual(1, events.Single(e => e.Kind == SimEventKind.ChapterComplete).Value, "通知は完了章番号1を示す");
            Assert.IsTrue(Chapters.Check(sim), "二回目の判定で二章目を完了できる");
            Assert.AreEqual(2, sim.State.chapter, "独立した判定で次章へ進む");
        }

        /// <summary>すべての章でその章の報酬を受け取り、最終章から完了状態へ進める。</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void EveryChapterPaysItsOwnRewardAndAdvancesOnce(int chapter)
        {
            var sim = Open();
            CompleteFirstTwoGoals(sim.State);
            sim.State.chapter = chapter;
            sim.State.shopLevel = Content.MaxShopLevel;
            sim.State.reputation = sim.State.popularity = 100;
            sim.State.stats.served = 1000;
            sim.State.stats.criticsSatisfied = 1;
            sim.State.stats.perfectBowls = 20;
            sim.State.stats.contestGold = 1;
            sim.State.stats.bestCombo = 15;
            sim.State.recipes.Clear();
            foreach (var r in Recipes.All) LifetimeStats.Add(sim.State.recipes, r.Id);
            foreach (var a in Content.Archetypes.Take(12)) LifetimeStats.Add(sim.State.stats.archetypesServed, a.Id);
            Assert.IsTrue(Chapters.All[chapter].Goals.All(g => g.Done(sim.State)), "検証状態が対象章の全目標を満たす");
            long cash = sim.State.moneyFen;
            Assert.IsTrue(Chapters.Check(sim), "各章の達成を判定できる");
            Assert.AreEqual(chapter + 1, sim.State.chapter, "対象章から一章だけ進む");
            Assert.AreEqual(cash + Chapters.All[chapter].RewardFen, sim.State.moneyFen, "対象章に定義された報酬を現金へ入れる");
            Assert.AreEqual(Chapters.All[chapter].RewardFen, sim.State.report.missionRewardsFen, "対象章の報酬を日報へ入れる");
            if (chapter == Chapters.All.Length - 1) Assert.IsNull(Chapters.Current(sim.State), "最終章完了後は現在章がない");
        }

        /// <summary>チュートリアル中と全章完了後は章判定をしても副作用がない。</summary>
        [TestCase(false, 0)]
        [TestCase(true, 5)]
        public void ChapterCheckIsNoOpDuringTutorialOrAfterFinalChapter(bool tutorialComplete, int chapter)
        {
            var sim = Open();
            CompleteFirstTwoGoals(sim.State);
            sim.State.tutorialComplete = tutorialComplete;
            sim.State.chapter = chapter;
            string before = JsonUtility.ToJson(sim.State);
            Assert.IsFalse(Chapters.Check(sim), "チュートリアル中または全章完了後は進まない");
            Assert.AreEqual(before, JsonUtility.ToJson(sim.State), "現金・日報を含む状態を一切変えない");
        }

        /// <summary>閉店の公開経路からも章達成を判定し固定費と報酬を正しく精算する。</summary>
        [Test]
        public void EndDayChecksChapterAndSettlesFixedCostAndReward()
        {
            var sim = Open();
            CompleteFirstTwoGoals(sim.State);
            sim.State.orders.Clear();
            sim.State.missions.Clear();
            sim.State.phase = DayPhase.Closing;
            long before = sim.State.moneyFen;
            sim.Tick(1);
            Assert.AreEqual(DayPhase.Report, sim.State.phase, "客がいない閉店中のTickで日報へ移る");
            Assert.AreEqual(1, sim.State.chapter, "閉店でも章達成を判定する");
            Assert.AreEqual(before - sim.FixedCostFen + Chapters.All[0].RewardFen, sim.State.moneyFen, "固定費を控除して章報酬を受け取る");
            Assert.AreEqual(sim.State.moneyFen - before, sim.State.report.ProfitFen, "追加売上がない日の現金増減と利益が一致する");
        }

        /// <summary>各テーマの章と資金条件を満たして購入すると資産・所有権・雰囲気・通知が更新される。</summary>
        [TestCase("lantern")]
        [TestCase("cafe")]
        [TestCase("teahouse")]
        [TestCase("neon")]
        public void BuyThemeChecksChapterAndFundsThenUpdatesOwnershipInvestmentAndAmbience(string id)
        {
            var sim = Open();
            sim.State.phase = DayPhase.Preparation;
            var theme = Content.Theme(id);
            sim.State.moneyFen = theme.PriceFen;
            sim.State.chapter = theme.ChapterRequired - 1;
            Assert.IsFalse(sim.BuyTheme(id), "必要章の直前では購入できない");
            Assert.AreEqual(theme.PriceFen, sim.State.moneyFen, "章不足で資金を引かない");
            sim.State.chapter++;
            sim.State.moneyFen = theme.PriceFen - 1;
            Assert.IsFalse(sim.BuyTheme(id), "価格より1分少なければ購入できない");
            Assert.IsFalse(sim.State.ownedThemes.Contains(id), "購入失敗で所有権を付けない");
            sim.State.moneyFen = theme.PriceFen;
            int ambience = DiningLayout.Ambience(sim.State);
            var events = new List<SimEvent>();
            sim.Event += events.Add;
            Assert.IsTrue(sim.BuyTheme(id), "章と価格ちょうどの資金があれば購入できる");
            Assert.AreEqual(0, sim.State.moneyFen, "購入価格を現金から引く");
            Assert.AreEqual(theme.PriceFen, sim.State.report.investmentFen, "価格を投資として計上する");
            Assert.IsTrue(sim.State.ownedThemes.Contains(id), "購入済み一覧へ追加する");
            Assert.AreEqual(id, sim.State.theme, "購入したテーマへ切り替える");
            Assert.AreEqual(ambience + theme.Ambience, DiningLayout.Ambience(sim.State), "家具の雰囲気へテーマ分を加算する");
            Assert.AreEqual(id, events.Single(e => e.Kind == SimEventKind.ThemeChanged).Id, "変更テーマIDを通知する");
            Assert.IsTrue(sim.BuyTheme(Content.DefaultTheme), "既定テーマへ無料で戻せる");
            Assert.IsTrue(sim.BuyTheme(id), "資金ゼロでも所有済みテーマへ無料切替できる");
            Assert.AreEqual(0, sim.State.moneyFen, "所有済みの切替は二重課金しない");
            Assert.AreEqual(theme.PriceFen, sim.State.report.investmentFen, "所有済み切替は投資を増やさない");
            Assert.AreEqual(1, sim.State.ownedThemes.Count(t => t == id), "所有テーマを重複登録しない");
        }

        /// <summary>営業中・閉店処理中は所有済みでもテーマを変更できない。</summary>
        [TestCase(DayPhase.Open)]
        [TestCase(DayPhase.Closing)]
        public void ThemeChangeRequiresManagementPhase(DayPhase phase)
        {
            var sim = Open();
            sim.State.phase = phase;
            sim.State.ownedThemes.Add("lantern");
            Assert.IsFalse(sim.CanManage, "営業中と閉店処理中は管理不可");
            Assert.IsFalse(sim.BuyTheme("lantern"), "所有済みでも営業中は変更不可");
            Assert.AreEqual(Content.DefaultTheme, sim.State.theme, "失敗時にテーマを変えない");
        }

        /// <summary>チュートリアル中と未知IDのテーマは購入できない。</summary>
        [Test]
        public void ThemePurchaseRejectsTutorialAndUnknownId()
        {
            var sim = Open();
            sim.State.phase = DayPhase.Report;
            sim.State.tutorialComplete = false;
            Assert.IsFalse(sim.BuyTheme("lantern"), "チュートリアル中は購入不可");
            sim.State.tutorialComplete = true;
            Assert.IsFalse(sim.BuyTheme("unknown"), "未知のテーマを購入しない");
            Assert.AreEqual(Content.DefaultTheme, sim.State.theme, "未知IDを既定テーマとして購入扱いにしない");
        }

        /// <summary>獲得ポイントはレベル差と金銀銅の所定点数の合計になる。</summary>
        [TestCase(1, 0, 0, 0, 0)]
        [TestCase(0, 0, 0, 0, 0)]
        [TestCase(5, 2, 3, 4, 15)]
        public void SkillPointsAreEarnedFromLevelsAndContestPlacements(int level, int gold, int silver, int bronze, int expected)
        {
            var s = new GameState { shopLevel = level };
            s.stats.contestGold = gold; s.stats.contestSilver = silver; s.stats.contestBronze = bronze;
            Assert.AreEqual(expected, Skills.Earned(s), "レベル-1と金2・銀1・銅1の合計を獲得する");
            Assert.AreEqual(expected, Skills.Available(s), "未使用なら全獲得ポイントが使える");
        }

        /// <summary>各スキルは段階ごとの1・2・3点を消費し最大3段階を超えない。</summary>
        [TestCase(Skills.Speed)]
        [TestCase(Skills.Hospitality)]
        [TestCase(Skills.Bargain)]
        [TestCase(Skills.Charm)]
        [TestCase(Skills.Fame)]
        public void LearnUsesProgressiveCostsAndStopsAtMaximumRank(string id)
        {
            var sim = Open();
            sim.State.shopLevel = Content.MaxShopLevel;
            int earned = Skills.Earned(sim.State), spent = 0;
            for (int rank = 1; rank <= 3; rank++)
            {
                Assert.AreEqual(rank, Skills.NextCost(sim.State, id), "次段階に段階数と同じポイントが必要");
                Assert.IsTrue(Skills.Learn(sim, id), "十分なポイントなら習得できる");
                spent += rank;
                Assert.AreEqual(rank, Skills.Rank(sim.State, id), "習得で一段階だけ増える");
                Assert.AreEqual(spent, Skills.Spent(sim.State), "消費ポイントを段階ごとに累積する");
                Assert.AreEqual(earned - spent, Skills.Available(sim.State), "使用可能ポイントから正しいコストを引く");
            }
            Assert.AreEqual(-1, Skills.NextCost(sim.State, id), "最大段階の次コストは-1");
            Assert.IsFalse(Skills.Learn(sim, id), "最大段階より上を習得できない");
            Assert.AreEqual(3, Skills.Rank(sim.State, id), "失敗時に段階を変えない");
        }

        /// <summary>ポイント不足・未知スキルは状態を増やさず失敗する。</summary>
        [Test]
        public void LearnRejectsInsufficientPointsAndUnknownSkill()
        {
            var sim = Open();
            Assert.IsFalse(Skills.Learn(sim, Skills.Speed), "レベル1・未入賞ではポイント不足");
            Assert.IsEmpty(sim.State.skills, "不足時にスキルを作らない");
            sim.State.shopLevel = 3;
            Assert.IsTrue(Skills.Learn(sim, Skills.Speed), "1段階目の1点は払える");
            Assert.IsFalse(Skills.Learn(sim, Skills.Speed), "残り1点で2点の段階を習得できない");
            Assert.AreEqual(1, Skills.Rank(sim.State, Skills.Speed), "不足時に既存段階を上げない");
            Assert.IsFalse(Skills.Learn(sim, "unknown"), "未知のスキルを習得しない");
            Assert.AreEqual(0, Skills.Rank(sim.State, "unknown"), "未知IDの段階は0");
        }

        /// <summary>全段階でスキル倍率が定義通りで、設備・セール・来客見込みにも反映される。</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SkillMultipliersAffectCookingPurchaseAndDemand(int rank)
        {
            var sim = Open();
            foreach (var def in Skills.All) LifetimeStats.Add(sim.State.skills, def.Id, rank);
            Assert.AreEqual(1f / (1f - .06f * rank), Skills.CookSpeed(sim.State), .00001f, "手際で茹で速度が上がる");
            Assert.AreEqual(1f + .08f * rank, Skills.Patience(sim.State), .00001f, "接客で忍耐倍率が上がる");
            Assert.AreEqual(1f - .05f * rank, Skills.Purchase(sim.State), .00001f, "目利きで仕入れ倍率が下がる");
            Assert.AreEqual(1f + .1f * rank, Skills.Tip(sim.State), .00001f, "愛想でチップ倍率が上がる");
            Assert.AreEqual(1f + .05f * rank, Skills.Demand(sim.State), .00001f, "名声で需要倍率が上がる");
            sim.State.SetEquipmentLevel("stove", 2);
            Assert.AreEqual(1.25f / (1f - .06f * rank), sim.CookSpeedMultiplier, .00001f, "コンロ倍率と手際倍率が掛け合わされる");
            sim.State.dailyEvent = "sale";
            Assert.AreEqual(.7f * (1f - .05f * rank), sim.PurchaseMultiplier, .00001f, "セールと目利き割引が掛け合わされる");
            var item = sim.Catalog.Get("enoki");
            long expected = (long)Math.Round(RestaurantSimulation.Cost(item) * sim.PurchaseMultiplier, MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, sim.PurchaseCost(item), "仕入れ価格へ割引と指定の端数処理を適用する");
            float demand = 9 + (sim.State.reputation - 50) / 5f + sim.State.popularity / 10f
                + DiningLayout.SeatCount(sim.State) * .7f + sim.State.shopLevel * .7f
                + Math.Max(0, DiningLayout.Ambience(sim.State)) * .12f - (sim.State.pricePer100gFen - 1800) / 140f;
            int expectedDemand = (int)Math.Max(5, Math.Min(48, Math.Round(demand * Content.Weather(sim.State.weather).DemandMultiplier
                * Content.DailyEvent(sim.State.dailyEvent).DemandMultiplier * (1f + .05f * rank))));
            Assert.AreEqual(expectedDemand, sim.Demand, "需要へ名声倍率を反映して丸めと上限下限を適用する");
        }

        /// <summary>加熱の実時間更新に手際スキルとコンロ倍率を適用する。</summary>
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(3, 2)]
        public void SpeedSkillChangesHeatingProgressDuringTick(int rank, int stoveLevel)
        {
            var sim = Open();
            sim.State.orders.Clear();
            sim.State.phase = DayPhase.Closing;
            sim.State.SetEquipmentLevel("stove", stoveLevel);
            LifetimeStats.Add(sim.State.skills, Skills.Speed, rank);
            var o = AtStage(sim, OrderStage.Heating);
            o.pot = 0;
            o.selected.Add("enoki");
            o.dropTimes.Add(0);
            sim.Tick(2);
            float expected = 2f * (stoveLevel >= 2 ? 1.25f : 1f) / (1f - .06f * rank);
            Assert.AreEqual(expected, o.heatSeconds, .00001f, "Tickの加熱秒へ手際と設備の速度倍率を反映する");
            Assert.AreEqual(2f, o.elapsed, "加熱速度が変わっても客の待ち時間は実時間で進む");
        }

        /// <summary>目利きによる割引は仕入れ操作の現金・日報・取得原価にも反映する。</summary>
        [Test]
        public void BargainSkillChangesActualPurchaseCashReportAndAcquisitionCost()
        {
            var sim = Open();
            sim.State.phase = DayPhase.Preparation;
            sim.State.dailyEvent = "sale";
            LifetimeStats.Add(sim.State.skills, Skills.Bargain, 3);
            var stock = sim.StockFor("enoki");
            stock.quantity = 0;
            long unit = sim.PurchaseCost(sim.Catalog.Get("enoki"));
            long cash = sim.State.moneyFen, purchases = sim.State.report.purchasesFen;
            Assert.IsTrue(sim.Purchase("enoki", 2), "割引価格で二個を仕入れられる");
            Assert.AreEqual(cash - unit * 2, sim.State.moneyFen, "割引済み単価二個分を現金から引く");
            Assert.AreEqual(purchases + unit * 2, sim.State.report.purchasesFen, "仕入れ日報も割引済み価格で記録する");
            Assert.AreEqual(unit, stock.unitCostFen, "食材の取得原価に割引済み単価を記録する");
            Assert.AreEqual(2, stock.quantity, "仕入れた数量だけ在庫が増える");
        }

        /// <summary>接客スキルだけを変えた同一客の忍耐が所定倍率で増える。</summary>
        [Test]
        public void HospitalitySkillIncreasesGeneratedCustomerPatience()
        {
            var sim = Open();
            string json = JsonUtility.ToJson(sim.State);
            var skilled = new RestaurantSimulation(sim.Catalog, JsonUtility.FromJson<GameState>(json));
            LifetimeStats.Add(skilled.State.skills, Skills.Hospitality, 3);
            var basic = sim.Customers.Create("office");
            var trained = skilled.Customers.Create("office");
            Assert.AreEqual(basic.patience * 1.24f, trained.patience, .001f, "生成した客の忍耐へ接客3段階を適用する");
            trained.patience = basic.patience;
            Assert.AreEqual(JsonUtility.ToJson(basic), JsonUtility.ToJson(trained), "接客スキルは忍耐以外の客内容を変えない");
        }

        /// <summary>愛想スキルは満足度を変えずチップと現金・日報へ反映される。</summary>
        [Test]
        public void CharmSkillIncreasesSettledTipsAndCashWithoutChangingSatisfaction()
        {
            var basic = Open();
            var skilled = Open();
            LifetimeStats.Add(skilled.State.skills, Skills.Charm, 3);
            var a = new CustomerOrder { quality = 100, paidFen = 10000 };
            var b = new CustomerOrder { quality = 100, paidFen = 10000 };
            var archetype = Content.Archetype("office");
            long cash = skilled.State.moneyFen;
            Satisfaction.Settle(basic, a, archetype);
            Satisfaction.Settle(skilled, b, archetype);
            Assert.AreEqual(a.report.satisfaction, b.report.satisfaction, "愛想は品質や満足度を直接変えない");
            long expected = Satisfaction.Tip(b.paidFen, b.report.satisfaction, archetype.Generosity * 1.3f, 0);
            Assert.Greater(b.tipFen, a.tipFen, "同じ満足度でも愛想でチップが増える");
            Assert.AreEqual(expected, b.tipFen, "チップ計算へ愛想倍率を入れる");
            Assert.AreEqual(cash + expected, skilled.State.moneyFen, "増えたチップを現金に入れる");
            Assert.AreEqual(expected, skilled.State.report.tipsFen, "増えたチップを日報に入れる");
        }

        /// <summary>提供、進行中会計、列、調理、片付けの順に候補を選ぶ。</summary>
        [Test]
        public void NextActionPrioritizesReadyThenOrderingQueueCookingAndCleaning()
        {
            var sim = Open();
            sim.State.orders.Clear();
            var cooking = AtStage(sim, OrderStage.Drained);
            var waiting = AtStage(sim, OrderStage.Waiting);
            var ordering = AtStage(sim, OrderStage.Ordering);
            var ready = AtStage(sim, OrderStage.Ready);
            sim.State.dirtyTables.Add(sim.State.furniture.First(f => f.defId == "table_round2").uid);
            Assert.AreEqual(ready.id, NextAction.Suggest(sim).OrderId, "完成品を最優先で提供する");
            Assert.AreEqual(NextActionKind.Serve, NextAction.Suggest(sim).Kind, "完成品には提供を提案する");
            ready.stage = OrderStage.Served;
            Assert.AreEqual(ordering.id, NextAction.Suggest(sim).OrderId, "完成品がなければ会計中を優先する");
            ordering.stage = OrderStage.Cancelled;
            Assert.AreEqual(waiting.id, NextAction.Suggest(sim).OrderId, "空き鍋があれば調理より列を優先する");
            waiting.stage = OrderStage.Cancelled;
            Assert.AreEqual(cooking.id, NextAction.Suggest(sim).OrderId, "会計待ちがなければ調理を提案する");
            Assert.AreEqual(NextActionKind.Cook, NextAction.Suggest(sim).Kind, "調理対象に調理を提案する");
            cooking.stage = OrderStage.Cancelled;
            var clean = NextAction.Suggest(sim);
            Assert.AreEqual(NextActionKind.Clean, clean.Kind, "注文処理がなければ片付けを提案する");
            Assert.AreEqual(sim.State.dirtyTables[0], clean.Table, "最初の汚れたテーブルを示す");
        }

        /// <summary>席のない完成品は飛ばし、スタッフ仕上げも含め持ち帰り完成品を提案する。</summary>
        [Test]
        public void NextActionSkipsUnseatedReadyAndCanServeStaffFinishedTakeout()
        {
            var sim = Open();
            sim.State.orders.Clear();
            var unseated = AtStage(sim, OrderStage.Ready);
            unseated.takeout = false; unseated.elapsed = 100;
            var takeout = AtStage(sim, OrderStage.Ready);
            takeout.handledByStaff = true;
            Assert.AreEqual(takeout.id, NextAction.Suggest(sim).OrderId, "席なし店内Readyを飛ばしてスタッフ仕上げの持ち帰りを提案する");
            Assert.AreEqual(NextActionKind.Serve, NextAction.Suggest(sim).Kind, "スタッフの仕上げ品も店主が届けられる");
        }

        /// <summary>Readyが複数なら完成後に長く待っている順に届ける。</summary>
        [Test]
        public void NextActionServesReadyOrderWithLongestElapsedTime()
        {
            var sim = Open();
            sim.State.orders.Clear();
            var newer = AtStage(sim, OrderStage.Ready); newer.elapsed = 5;
            var older = AtStage(sim, OrderStage.Ready); older.elapsed = 20;
            Assert.AreEqual(older.id, NextAction.Suggest(sim).OrderId, "冷めるリスクの高い古い完成品を先にする");
        }

        /// <summary>スタッフが作業対象として確保した注文は案内から除外する。</summary>
        [TestCase(OrderStage.Ready)]
        [TestCase(OrderStage.Ordering)]
        [TestCase(OrderStage.Waiting)]
        [TestCase(OrderStage.Drained)]
        public void NextActionExcludesOrdersWithActiveStaffTaskTarget(OrderStage stage)
        {
            var sim = Open();
            sim.State.orders.Clear();
            var busy = AtStage(sim, stage);
            sim.State.staff.Add(new HiredStaff { task = "work", taskTarget = busy.id });
            Assert.AreEqual(NextActionKind.None, NextAction.Suggest(sim).Kind, "スタッフ作業中の対象に重ねて案内しない");
            sim.State.staff[0].task = "";
            Assert.AreEqual(busy.id, NextAction.Suggest(sim).OrderId, "taskが空なら古いtaskTargetだけで除外しない");
        }

        /// <summary>スタッフ担当の非Ready注文を店主の調理・会計に案内しない。</summary>
        [TestCase(OrderStage.Ordering)]
        [TestCase(OrderStage.Drained)]
        public void NextActionExcludesNonReadyOrdersHandledByStaff(OrderStage stage)
        {
            var sim = Open();
            sim.State.orders.Clear();
            var o = AtStage(sim, stage);
            o.handledByStaff = true;
            Assert.AreEqual(NextActionKind.None, NextAction.Suggest(sim).Kind, "スタッフ担当の非完成注文は店主の作業にしない");
        }

        /// <summary>鍋が埋まっていると受付より調理を優先し、計量済みを加熱候補にしない。</summary>
        [Test]
        public void NextActionPrioritizesCookingOverQueueWhenNoPotIsFree()
        {
            var sim = Open();
            sim.State.orders.Clear();
            var heating = AtStage(sim, OrderStage.Heating); heating.pot = 0;
            AtStage(sim, OrderStage.Weighed);
            AtStage(sim, OrderStage.Waiting);
            Assert.AreEqual(-1, sim.FreePot(), "検証中は鍋を全て使用している");
            Assert.AreEqual(heating.id, NextAction.Suggest(sim).OrderId, "空き鍋なしでは受付より鍋の調理を進める");
            heating.handledByStaff = true;
            sim.State.orders.Last().stage = OrderStage.Cancelled;
            Assert.AreEqual(NextActionKind.None, NextAction.Suggest(sim).Kind, "空き鍋なしではWeighedを提案しない");
        }

        /// <summary>営業外は完成品があっても案内しない。</summary>
        [TestCase(DayPhase.Preparation)]
        [TestCase(DayPhase.Report)]
        public void NextActionReturnsNoneOutsideServiceHours(DayPhase phase)
        {
            var sim = Open();
            AtStage(sim, OrderStage.Ready);
            sim.State.phase = phase;
            Assert.AreEqual(NextActionKind.None, NextAction.Suggest(sim).Kind, "営業外の案内はNone");
        }

        /// <summary>レシピ未発見時はミッション候補に出ず、発見後は固定日列に出現する。</summary>
        [Test]
        public void RecipeMissionAppearsOnlyAfterDiscovery()
        {
            var sim = Open();
            for (int day = 1; day <= 100; day++)
            {
                sim.State.day = day;
                Missions.Generate(sim);
                Assert.IsFalse(sim.State.missions.Any(m => m.kind == "recipe"), "未発見ではレシピ種別をプールへ入れない");
            }
            LifetimeStats.Add(sim.State.recipes, "tomato_egg");
            int found = 0;
            for (int day = 1; day <= 100; day++)
            {
                sim.State.day = day;
                Missions.Generate(sim);
                found += sim.State.missions.Count(m => m.kind == "recipe");
            }
            Assert.Greater(found, 0, "発見後は100日以内の固定日列にレシピミッションが出る");
        }

        /// <summary>レシピミッション進捗は当日の提供済みレシピ注文だけを数える。</summary>
        [Test]
        public void RecipeMissionCountsServedRecipeOrdersNotReadyCancelledOrLifetimeTotals()
        {
            var sim = Open();
            sim.State.orders.Clear();
            sim.State.stats.recipeBowls = 100;
            AtStage(sim, OrderStage.Served).recipe = "tomato_egg";
            AtStage(sim, OrderStage.Served).recipe = "golden";
            AtStage(sim, OrderStage.Served);
            AtStage(sim, OrderStage.Ready).recipe = "tomato_egg";
            AtStage(sim, OrderStage.Cancelled).recipe = "golden";
            Assert.AreEqual(2, Missions.Progress(sim.State, new Mission { kind = "recipe" }), "未提供・取消・累計分を除き提供済み二杯だけ数える");
            sim.State.orders.Clear();
            Assert.AreEqual(0, Missions.Progress(sim.State, new Mission { kind = "recipe" }), "翌日相当の注文リスト消去で進捗も0に戻る");
        }

        /// <summary>新フィールドを含まないv3実JSONは既定値を補完しv4として読み込める。</summary>
        [Test]
        public void VersionThreeJsonWithoutReplayFieldsMigratesToVersionFourDefaults()
        {
            const string payload = "{\"saveVersion\":3,\"storeId\":\"legacy-v3\",\"day\":7,\"phase\":0,\"moneyFen\":123450,"
                + "\"pricePer100gFen\":1800,\"pots\":1,\"storageCapacity\":30,\"shopLevel\":2,"
                + "\"tutorialComplete\":true,\"tutorialStep\":13,\"ingredients\":{\"settledRevenueFen\":0,\"unlockedIds\":[],\"settledOrderIds\":[]}}";
            WithRepository((repo, path) =>
            {
                string checksum;
                using (var sha = SHA256.Create()) checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
                File.WriteAllText(path, JsonUtility.ToJson(new LegacyEnvelope { version = 3, payload = payload, checksum = checksum }));
                var s = repo.Load();
                Assert.IsNotNull(s, "チェックサム付きv3を読み込める");
                Assert.IsTrue(string.IsNullOrEmpty(repo.LastError), "復旧フォールバックではなく旧本体を移行する");
                Assert.AreEqual(GameState.CurrentVersion, s.saveVersion, "v3を最新の保存形式へ移行する");
                Assert.IsTrue(s.navigation, "旧保存でも案内は既定の有効になる");
                Assert.AreEqual(1f, s.lookSensitivity, "旧保存でも感度は1になる");
                Assert.AreEqual("shitamachi", s.theme, "欠落テーマは下町に戻る");
                CollectionAssert.Contains(s.ownedThemes, "shitamachi", "既定テーマを所有済みにする");
                Assert.IsEmpty(s.recipes, "架空のレシピ発見を作らない");
                Assert.IsEmpty(s.skills, "架空のスキルを作らない");
                Assert.AreEqual(0, s.chapter, "欠落章番号は0になる");
                Assert.AreEqual(123450, s.moneyFen, "旧資金を失わない");
                Assert.AreEqual(7, s.day, "旧営業日を失わない");
                Assert.DoesNotThrow(() => SaveRepository.Validate(s), "補完した状態が正式検証を通る");
            });
        }

        /// <summary>正規化は未知IDを削除し、未所有テーマを既定へ戻す。</summary>
        [Test]
        public void NormalizeRemovesUnknownIdsAndResetsUnownedTheme()
        {
            var s = new GameState { theme = "lantern" };
            s.ownedThemes = new List<string> { "unknown" };
            s.recipes = new List<NamedCount> { null, new NamedCount { id = "unknown" }, new NamedCount { id = "golden", count = 2 } };
            s.skills = new List<NamedCount> { null, new NamedCount { id = "unknown" }, new NamedCount { id = Skills.Speed, count = 2 } };
            SaveMigration.Migrate(s);
            CollectionAssert.AreEqual(new[] { "shitamachi" }, s.ownedThemes, "未知テーマを除去し既定所有を補完する");
            Assert.AreEqual("shitamachi", s.theme, "未所有の適用テーマを既定へ戻す");
            Assert.AreEqual("golden", s.recipes.Single().id, "未知・nullレシピを除去する");
            Assert.AreEqual(2, s.recipes.Single().count, "正しいレシピの回数を保持する");
            Assert.AreEqual(Skills.Speed, s.skills.Single().id, "未知・nullスキルを除去する");
            Assert.AreEqual(2, s.skills.Single().count, "正しいスキル段階を保持する");
            s.theme = "unknown";
            SaveMigration.Migrate(s);
            Assert.AreEqual(Content.DefaultTheme, s.theme, "未知の適用テーマも既定へ戻す");
        }

        /// <summary>v4保存と再読込でレシピ・スキル・章・テーマ・視点設定を保持する。</summary>
        [Test]
        public void VersionFourRoundTripPreservesReplayProgressAndSettings()
        {
            var sim = Open();
            sim.State.chapter = 2;
            sim.State.theme = "cafe";
            sim.State.ownedThemes.Add("cafe");
            sim.State.navigation = false;
            sim.State.lookSensitivity = 1.7f;
            sim.State.invertLook = true;
            LifetimeStats.Add(sim.State.recipes, "golden", 7);
            LifetimeStats.Add(sim.State.skills, Skills.Speed, 2);
            LifetimeStats.Add(sim.State.skills, Skills.Charm, 1);
            WithRepository((repo, path) =>
            {
                repo.Save(sim.State);
                var envelope = JsonUtility.FromJson<LegacyEnvelope>(File.ReadAllText(path));
                Assert.AreEqual(GameState.CurrentVersion, envelope.version, "保存包絡のバージョンは最新");
                var s = repo.Load();
                Assert.IsTrue(string.IsNullOrEmpty(repo.LastError), "v4をエラーなしで読める");
                Assert.AreEqual(GameState.CurrentVersion, s.saveVersion, "保存状態も最新");
                Assert.AreEqual(7, LifetimeStats.Get(s.recipes, "golden"), "発見レシピと回数を保持する");
                Assert.AreEqual(2, Skills.Rank(s, Skills.Speed), "手際段階を保持する");
                Assert.AreEqual(1, Skills.Rank(s, Skills.Charm), "愛想段階を保持する");
                Assert.AreEqual(2, s.chapter, "章進行を保持する");
                Assert.AreEqual("cafe", s.theme, "適用テーマを保持する");
                CollectionAssert.AreEqual(sim.State.ownedThemes, s.ownedThemes, "所有テーマを保持する");
                Assert.IsFalse(s.navigation, "v4の案内無効は移行で上書きしない");
                Assert.AreEqual(1.7f, s.lookSensitivity, "v4の感度設定を保持する");
                Assert.IsTrue(s.invertLook, "v4の視点反転を保持する");
            });
        }

        /// <summary>全客タイプは同じseedから新フィールドを含む同じ外見を作る。</summary>
        [TestCase(0)]
        [TestCase(7)]
        [TestCase(12345)]
        [TestCase(int.MaxValue)]
        public void AppearanceIsDeterministicForEveryArchetypeAndAllFields(int seed)
        {
            foreach (bool child in new[] { false, true })
            {
                Assert.AreEqual(JsonUtility.ToJson(CharacterAppearance.FromSeed(seed, child)),
                    JsonUtility.ToJson(CharacterAppearance.FromSeed(seed, child)), "基本外見の全フィールドが同じseedで一致する");
                foreach (var a in Content.Archetypes)
                {
                    var first = CharacterAppearance.ForArchetype(a.Id, seed, child);
                    var second = CharacterAppearance.ForArchetype(a.Id, seed, child);
                    Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(second), "客タイプ「" + a.Id + "」の目・眉・ひげ・靴・そばかす・ネクタイを含め再現する");
                    if (child)
                    {
                        Assert.AreEqual(0, first.facialHair, "子どもにはどの客タイプでもひげが付かない");
                        Assert.AreEqual(.7f, first.height, "子どもにはどの客タイプでも身長0.7を保つ");
                    }
                }
                Assert.AreEqual(JsonUtility.ToJson(CharacterAppearance.FromSeed(seed, child)),
                    JsonUtility.ToJson(CharacterAppearance.ForArchetype("unknown", seed, child)), "未知タイプは基本外見と全フィールド同一");
            }
        }

        /// <summary>固定seed列の新外見フィールドは描画側パレットの定義範囲に収まる。</summary>
        [Test]
        public void AppearanceFieldsStayWithinDefinedRangesAcrossFixedSeeds()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var a = CharacterAppearance.FromSeed(seed);
                Assert.That(a.eyeStyle, Is.InRange(0, 4), "目は0〜4");
                Assert.That(a.browStyle, Is.InRange(0, 2), "眉は0〜2");
                Assert.That(a.facialHair, Is.InRange(0, 3), "ひげは0〜3");
                Assert.That(a.shoeColor, Is.InRange(0, 5), "靴色は0〜5");
                Assert.That(a.height, Is.InRange(.88f, 1.08f), "大人の基本身長は0.88〜1.08");
                var drunk = CharacterAppearance.ForArchetype("drunk", seed, false);
                Assert.IsTrue(drunk.looseTie, "酔客は緩めたネクタイになる");
                Assert.AreEqual(3, drunk.eyeStyle, "酔客は細目になる");
                var family = CharacterAppearance.ForArchetype("family", seed, true);
                Assert.AreEqual(0, family.accessory, "親子連れの子どもに大人用小物を付けない");
                Assert.AreEqual(0, family.outfit, "親子連れの子どもへ学生服を適用しない");
                Assert.AreEqual(3, CharacterAppearance.ForArchetype("elder", seed, false).eyeStyle, "年配の客は細目になる");
                Assert.AreEqual(1, CharacterAppearance.ForArchetype("worker", seed, false).browStyle, "作業員は太い直線の眉になる");
            }
        }
    }
}
