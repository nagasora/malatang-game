using System;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>具材の特徴・一杯のバランス評価と、おまかせ注文（希望・評価の上限・自動選択・客の生成）を保証する。</summary>
    public class OmakaseAndBalanceTests
    {
        private static RestaurantSimulation Open()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            sim.State.shopLevel = 5;
            Assert.IsTrue(sim.BeginDay());
            return sim;
        }

        private static void StockAll(RestaurantSimulation sim)
        {
            foreach (var item in sim.Catalog.ingredients)
            {
                if (!sim.Unlocks.IsUnlocked(item.id)) sim.State.ingredients.unlockedIds.Add(item.id);
                sim.StockFor(item.id).quantity = 20;
            }
        }

        private static CustomerOrder Omakase(RestaurantSimulation sim, string archetype = "office", float grams = 300, int texture = -1, bool veg = false, string avoid = "")
        {
            var o = new CustomerOrder
            {
                id = "omakase-" + sim.State.orders.Count, customerName = "おまかせさん", archetype = archetype, style = OrderStyle.Ordered,
                stage = OrderStage.Selecting, soup = "mala", spice = 1, patience = 999, omakase = true, targetGrams = grams,
                budgetFen = 5400, likeTexture = texture, wantVeg = veg, avoidCategory = avoid,
            };
            sim.State.orders.Add(o);
            return o;
        }

        [Test]
        public void EveryCatalogIngredientHasHandWrittenTraitsWithANote()
        {
            var catalog = IngredientCatalog.Load();
            foreach (var item in catalog.ingredients)
                Assert.IsFalse(string.IsNullOrEmpty(FoodProfile.Of(item).Note), item.id + " に選ぶときのひとことがある");
        }

        [Test]
        public void BalanceRewardsColourTextureAndAllThreeRoles()
        {
            var catalog = IngredientCatalog.Load();
            var plain = FoodProfile.Evaluate(new[] { "bok_choy", "spinach", "komatsuna" }, catalog);
            var rich = FoodProfile.Evaluate(new[] { "bok_choy", "cherry_tomato", "shrimp", "chinese_noodle", "black_wood_ear", "lotus_root" }, catalog);
            Assert.AreEqual(1, plain.Colors.Count, "緑の葉物だけでは1色");
            Assert.AreEqual(35, plain.NutritionScore, "野菜だけでは栄養の役割が1つ");
            Assert.AreEqual(100, rich.NutritionScore, "野菜・たんぱく質・主食がそろうと栄養満点");
            Assert.GreaterOrEqual(rich.Colors.Count, 5);
            Assert.Greater(rich.Total, plain.Total + 40, "彩りと食感の多い一杯ほど高評価");
            Assert.AreEqual("", rich.Advice(), "全部そろっていれば助言は無い");
            StringAssert.Contains("たんぱく質", plain.Advice());
            Assert.AreEqual(0, FoodProfile.Evaluate(new string[0], catalog).Total, "空のボウルは0点");
        }

        [Test]
        public void WeightFitIsFullWithinTenPercentAndDropsWithDistance()
        {
            Assert.AreEqual(100, Game.Omakase.WeightFit(330, 300));
            Assert.AreEqual(75, Game.Omakase.WeightFit(360, 300));
            Assert.AreEqual(45, Game.Omakase.WeightFit(190, 300));
            Assert.AreEqual(15, Game.Omakase.WeightFit(100, 300));
        }

        [Test]
        public void PreferenceFitPenalisesMissingTextureFewVegetablesAndAvoidedFood()
        {
            var sim = Open(); StockAll(sim);
            var o = Omakase(sim, texture: (int)FoodTexture.Bouncy, veg: true, avoid: "seafood");
            o.selected.AddRange(new[] { "bok_choy", "spinach", "king_oyster", "fish_ball" });
            Assert.AreEqual(100, Game.Omakase.PreferenceFit(o, sim.Catalog), "好きな食感が2つ以上・野菜3つ・苦手なし");
            o.selected.Add("shrimp");
            Assert.AreEqual(60, Game.Omakase.PreferenceFit(o, sim.Catalog), "苦手な魚介を1つ入れると40点減");
            o.selected.Clear(); o.selected.Add("chinese_noodle");
            Assert.Less(Game.Omakase.PreferenceFit(o, sim.Catalog), 20, "好きな食感も野菜も無い");
        }

        [Test]
        public void QualityIsCappedWhenTheBowlIsFarTooSmallOrContainsAvoidedFood()
        {
            var sim = Open(); StockAll(sim);
            var o = Omakase(sim, grams: 400);
            o.selected.AddRange(new[] { "bok_choy", "yuba", "mung_bean_noodle" });
            Assert.AreEqual(60, Game.Omakase.QualityCap(o, sim.Catalog), "目安の約3分の1しか入っていない一杯は60点まで");
            o.selected.Clear(); o.selected.AddRange(new[] { "bok_choy", "spinach", "shrimp", "chinese_noodle", "lotus_root", "egg", "potato", "beef_slice", "enoki" });
            Assert.AreEqual(100, Game.Omakase.QualityCap(o, sim.Catalog), "目安どおりなら上限なし");
            o.avoidCategory = "seafood";
            Assert.AreEqual(60, Game.Omakase.QualityCap(o, sim.Catalog), "苦手と言われたものを入れたら60点まで");
        }

        [Test]
        public void FinishAppliesTheOmakaseCapToTheFinalQuality()
        {
            var sim = Open(); StockAll(sim);
            var o = Omakase(sim, grams: 400);
            o.paidFen = o.budgetFen;
            foreach (string id in new[] { "bok_choy", "yuba", "mung_bean_noodle" }) Assert.IsTrue(sim.AddIngredient(o.id, id));
            Assert.IsTrue(sim.Weigh(o.id));
            Assert.IsTrue(sim.StartHeating(o.id, false));
            for (int i = 0; i < o.dropTimes.Count; i++) if (o.dropTimes[i] < 0) sim.DropIngredient(o.id, i);
            for (int guard = 0; guard < 200 && Enumerable.Range(0, o.selected.Count).Any(i => sim.Doneness(o, i) < 1f); guard++) sim.Tick(.5f);
            Assert.IsTrue(sim.Drain(o.id)); for (int i = 0; i < 3; i++) sim.Shake(o.id);
            sim.PourSoup(o.id, "mala", 340); sim.AddCondiment(o.id, Content.ChiliOil);
            Assert.IsTrue(sim.Finish(o.id));
            Assert.LessOrEqual(o.quality, 60f, "量の足りないおまかせは他が完璧でも高品質にならない");
        }

        [Test]
        public void AutoPickRespectsForbiddenAndAvoidedCategoriesTheBowlCapAndTheTargetWeight()
        {
            var sim = Open(); StockAll(sim);
            var veg = Omakase(sim, "vegetarian", 500, (int)FoodTexture.Bouncy, true);
            var pick = Game.Omakase.AutoPick(veg, sim.Unlocks.Available, id => sim.Available(id, veg));
            var forbidden = Content.Archetype("vegetarian").ForbiddenCategories;
            Assert.IsFalse(pick.Any(id => forbidden.Contains(sim.Catalog.Get(id).category)), "ベジタリアンに肉・魚介・加工品を選ばない");
            Assert.LessOrEqual(pick.Count, RestaurantSimulation.MaxIngredientsPerBowl);
            var big = Omakase(sim, "bigeater", 680, (int)FoodTexture.Crisp, false, "seafood");
            pick = Game.Omakase.AutoPick(big, sim.Unlocks.Available, id => sim.Available(id, big));
            Assert.LessOrEqual(pick.Count, RestaurantSimulation.MaxIngredientsPerBowl, "1杯10個までに収める");
            Assert.IsFalse(pick.Any(id => sim.Catalog.Get(id).category == "seafood"), "苦手な魚介は選ばない");
            float grams = pick.Sum(id => sim.Catalog.Get(id).portionGrams);
            Assert.Greater(grams, 680 * .6f, "上限内で目安に近づける");
            foreach (string id in pick) Assert.IsTrue(sim.AddIngredient(big.id, id), "選んだ具材はすべてボウルに入る");
        }

        [Test]
        public void OmakaseCustomersStillGetSoupSpiceFromTheirTypeAndPayTheirBudgetAtTheRegister()
        {
            var sim = Open(); StockAll(sim);
            CustomerOrder found = null;
            for (int i = 0; i < 400 && found == null; i++)
            {
                var o = sim.Customers.Create("bigeater");
                if (o == null) continue;
                sim.State.orders.Add(o);
                if (o.omakase) found = o;
            }
            if (found == null)
            {
                // 指名の客タイプはおまかせを選ばないため、通常の来客から探す。
                for (int i = 0; i < 3000 && found == null; i++)
                {
                    var o = sim.Customers.Create();
                    if (o == null) continue;
                    if (o.omakase) { sim.State.orders.Add(o); found = o; }
                }
            }
            Assert.IsNotNull(found, "店舗レベル5ではおまかせ客が来る");
            var type = Content.Archetype(found.archetype);
            Assert.AreEqual(0, found.requested.Count, "具材の指定は無い");
            Assert.Greater(found.targetGrams, 0); Assert.Greater(found.budgetFen, 0);
            Assert.IsTrue(found.spice >= Math.Max(0, type.MinSpice - 1) && found.spice <= Math.Min(5, type.MaxSpice + 1), "辛さは客タイプの好み（天気の補正込み）から決まる");
            if (found.likeTexture >= 0 && !string.IsNullOrEmpty(found.avoidCategory))
                Assert.IsTrue(sim.Catalog.ingredients.Any(x => x.category != found.avoidCategory && (int)FoodProfile.Of(x).Texture == found.likeTexture), "好きな食感と苦手が両立する");
            Assert.IsTrue(sim.TakeOrder(found.id));
            Assert.AreEqual(found.budgetFen, found.billFen, "レジでは予算どおりに請求する");
        }

        [Test]
        public void ServingAGreatOmakaseCountsForStatsAndRegularsRememberWhatWasServed()
        {
            var sim = Open(); StockAll(sim);
            var o = Omakase(sim, grams: 300);
            o.stage = OrderStage.Ready; o.quality = 92; o.takeout = true; o.paidFen = o.budgetFen;
            o.selected.AddRange(new[] { "bok_choy", "cherry_tomato", "shrimp", "chinese_noodle", "black_wood_ear" });
            o.report = new QualityReport { total = 92 }; o.report.comments.Add("ok");
            Assert.IsTrue(sim.Serve(o.id));
            Assert.AreEqual(1, sim.State.stats.omakaseGreat);
            Assert.AreEqual(1, sim.State.stats.rainbowBowls, "5色の一杯");
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.stats.ingredientsServed, "shrimp"));
            var regular = sim.State.regulars.FirstOrDefault(r => r.name == "おまかせ");
            if (regular != null) CollectionAssert.IsSubsetOf(regular.favorites, o.selected, "おまかせの常連は実際に出した具材を覚える");
        }
    }
}
