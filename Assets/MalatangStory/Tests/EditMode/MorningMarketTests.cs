using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>希少食材の通常解禁分離・朝市の日付制限・品質・保存移行を保証する。</summary>
    public sealed class MorningMarketTests
    {
        /// <summary>資金に余裕のある3日目の朝を作る。</summary>
        private static RestaurantSimulation Create(int day = 3)
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.day = day;
            sim.State.storeId = "market-test";
            sim.State.moneyFen = 1000000;
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            MorningMarket.Ensure(sim);
            return sim;
        }

        /// <summary>実際にFinishとServeへ渡せる、茹で上がりの注文を作る。</summary>
        private static CustomerOrder Finishing(RestaurantSimulation sim, params string[] ids)
        {
            var order = new CustomerOrder
            {
                id = "rare-" + sim.State.orders.Count, customerName = "テスト客", archetype = "office",
                stage = OrderStage.Finishing, takeout = true, chosenSoup = "mala", soup = "mala",
                soupMl = 300, spice = 0, chosenSpice = 0, heatSeconds = 30, shakes = 2, patience = 100, elapsed = 80
            };
            order.selected.AddRange(ids);
            order.requested.AddRange(ids);
            order.dropTimes.AddRange(ids.Select(id => 30 - sim.Catalog.Get(id).cookingSeconds));
            sim.State.orders.Add(order);
            return order;
        }

        /// <summary>売上解禁は通常69種類だけを対象とする。</summary>
        [Test]
        public void RareCatalogIsSeparateFromAllNormalRevenueUnlocks()
        {
            var sim = Create();
            Assert.AreEqual(6, sim.Catalog.ingredients.Count(x => x.isRare));
            CollectionAssert.AreEquivalent(MorningMarket.RareIds, sim.Catalog.ingredients.Where(x => x.isRare).Select(x => x.id));
            sim.Unlocks.SettleOrder("huge", 100000000);
            Assert.AreEqual(69, sim.State.ingredients.unlockedIds.Count);
            Assert.AreEqual(0, sim.State.ingredients.rareObtainedIds.Count);
            Assert.IsNull(sim.Unlocks.Next);
            Assert.IsFalse(sim.Unlocks.Available.Any(x => x.isRare));
        }

        /// <summary>3日目より前の朝市と購入を禁止する。</summary>
        [TestCase(-5)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MarketDoesNotOpenBeforeTheThirdDay(int day)
        {
            var sim = Create(day);
            Assert.AreEqual("", MorningMarket.Pick(sim.State.storeId, day));
            Assert.IsNull(MorningMarket.Current(sim));
            Assert.IsFalse(sim.PurchaseRare(day));
        }

        /// <summary>店舗と日付の選出が再現でき、6日間ですべての日替わり品が現れる。</summary>
        [Test]
        public void SelectionIsDeterministicAndShufflesEachSixDayBlockWithoutMissingAnyIngredient()
        {
            var sim = Create();
            var ids = Enumerable.Range(3, 6).Select(day => MorningMarket.Pick("market-test", day)).ToArray();
            CollectionAssert.AreEquivalent(MorningMarket.RareIds, ids);
            var nextBlock = Enumerable.Range(9, 6).Select(day => MorningMarket.Pick("market-test", day)).ToArray();
            CollectionAssert.AreEquivalent(MorningMarket.RareIds, nextBlock);
            Assert.IsFalse(ids.SequenceEqual(nextBlock), "次の6日ブロックは同じ順番の巡回ではなく再シャッフルされる");
            Assert.IsFalse(ids.SequenceEqual(Enumerable.Range(3, 6).Select(day => MorningMarket.Pick("another-store", day))), "店舗IDも選出シードに含む");
            foreach (int day in new[] { 3, 4, 500, int.MaxValue })
                Assert.AreEqual(MorningMarket.Pick("market-test", day), MorningMarket.Pick("market-test", day));
            string original = sim.State.marketIngredient;
            sim.State.moneyFen = 0;
            sim.State.ingredients.settledRevenueFen = 10000000;
            MorningMarket.Ensure(sim);
            Assert.AreEqual(original, sim.State.marketIngredient);
        }

        /// <summary>朝市は5個1回で、再読込しても購入済み状態と在庫を保持する。</summary>
        [Test]
        public void PurchaseIsExactlyFiveOncePerDayAndRetainsAcquiredStockAfterReload()
        {
            var sim = Create();
            var item = MorningMarket.Current(sim);
            long money = sim.State.moneyFen, cost = sim.PurchaseCost(item) * 5;
            Assert.IsTrue(sim.PurchaseRare(3));
            Assert.AreEqual(5, sim.StockFor(item.id).quantity);
            Assert.AreEqual(money - cost, sim.State.moneyFen);
            Assert.AreEqual(cost, sim.State.report.purchasesFen);
            Assert.AreEqual(sim.PurchaseCost(item), sim.StockFor(item.id).unitCostFen);
            Assert.IsTrue(sim.Unlocks.IsUnlocked(item.id));
            Assert.Contains(item.id, sim.State.ingredients.rareObtainedIds);
            Assert.IsFalse(sim.State.ingredients.unlockedIds.Contains(item.id));
            Assert.IsTrue(sim.State.achievements.Contains("rare_first"));
            Assert.IsFalse(sim.PurchaseRare(3));
            var loaded = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(sim.State));
            SaveMigration.Migrate(loaded);
            var restored = new RestaurantSimulation(sim.Catalog, loaded);
            Assert.IsFalse(restored.PurchaseRare(3));
            Assert.AreEqual(5, restored.StockFor(item.id).quantity);
        }

        /// <summary>通常購入と一括補充では希少在庫が増えず、セールでも値引きされない。</summary>
        [Test]
        public void NoNormalPurchaseOrRestockBypassesTheDailyCapOrSalePrice()
        {
            var sim = Create();
            var item = MorningMarket.Current(sim);
            Assert.IsTrue(sim.PurchaseRare(3));
            Assert.IsFalse(sim.Purchase(item.id, 5));
            sim.State.dailyEvent = "sale";
            Assert.AreEqual(RestaurantSimulation.Cost(item), sim.PurchaseCost(item));
            sim.RestockAll(20);
            Assert.AreEqual(5, sim.StockFor(item.id).quantity);
            Assert.AreEqual(RestaurantSimulation.Cost(item), sim.StockFor(item.id).unitCostFen);
        }

        /// <summary>容量不足や1分の資金不足では購入権を消費しない。</summary>
        [Test]
        public void CapacityAndMoneyFailuresDoNotConsumeTheOffer()
        {
            var sim = Create();
            var item = MorningMarket.Current(sim);
            sim.StockFor(item.id).quantity = sim.State.storageCapacity - 4;
            Assert.IsFalse(sim.PurchaseRare(3));
            Assert.IsFalse(sim.State.marketPurchased);
            sim.StockFor(item.id).quantity = 0;
            sim.State.moneyFen = sim.PurchaseCost(item) * 5 - 1;
            Assert.IsFalse(sim.PurchaseRare(3));
            Assert.IsFalse(sim.State.marketPurchased);
            sim.State.moneyFen++;
            Assert.IsTrue(sim.PurchaseRare(3));
            Assert.AreEqual(0, sim.State.moneyFen);
        }

        /// <summary>開店後と閉店後の購入を禁止する。</summary>
        [TestCase(DayPhase.Open)]
        [TestCase(DayPhase.Closing)]
        [TestCase(DayPhase.Report)]
        public void OfferCanOnlyBePurchasedDuringPreparation(DayPhase phase)
        {
            var sim = Create();
            sim.State.phase = phase;
            Assert.IsFalse(sim.PurchaseRare(3));
            Assert.IsFalse(sim.State.marketPurchased);
        }

        /// <summary>翌日は品が替わり、前日の画面からの購入を拒否する。</summary>
        [Test]
        public void AdvancingDayChangesTheOfferAndRejectsStalePurchaseCallbacks()
        {
            var sim = Create();
            string old = sim.State.marketIngredient;
            Assert.IsTrue(sim.PurchaseRare(3));
            sim.State.phase = DayPhase.Report;
            Assert.IsTrue(sim.NextDay());
            Assert.AreEqual(4, sim.State.marketDay);
            Assert.AreNotEqual(old, sim.State.marketIngredient);
            Assert.IsFalse(sim.State.marketPurchased);
            Assert.IsFalse(sim.PurchaseRare(3));
            Assert.IsTrue(sim.PurchaseRare(4));
            Assert.AreEqual(5, sim.StockFor(old).quantity);
            Assert.AreEqual(1, sim.StockFor(old).ageDays);
        }

        /// <summary>希少の種類数や重複数に関わらず一杯3点、品質100点を上限とする。</summary>
        [Test]
        public void RareBonusIsThreePerBowlRegardlessOfDuplicatesAndCapsAtOneHundred()
        {
            var sim = Create();
            foreach (string[] ids in new[] { new[] { "matsutake" }, new[] { "matsutake", "matsutake", "wagyu" } })
            {
                var order = Finishing(sim, ids);
                order.report.total = 80;
                MorningMarket.ApplyBonus(order, sim.Catalog);
                Assert.AreEqual(83, order.report.total);
                Assert.AreEqual(1, order.report.comments.Count);
                StringAssert.Contains("松茸", order.report.comments.Single());
                order.report.total = 99;
                MorningMarket.ApplyBonus(order, sim.Catalog);
                Assert.AreEqual(100, order.report.total);
            }
            var normal = Finishing(sim, "enoki");
            normal.report.total = 80;
            MorningMarket.ApplyBonus(normal, sim.Catalog);
            Assert.AreEqual(80, normal.report.total);
            Assert.IsEmpty(normal.report.comments);
        }

        /// <summary>実際の完成・提供経路で加点と口コミ、希少2種の実績が記録される。</summary>
        [Test]
        public void ActualFinishAndServeApplyTheBonusVoiceAndTwoRareIngredientAchievement()
        {
            var sim = Create();
            var order = Finishing(sim, "matsutake", "wagyu");
            float expected = Math.Min(100, QualityEvaluator.Evaluate(order, sim.Catalog, sim.FreshnessScore).total + 3);
            Assert.IsTrue(sim.Finish(order.id));
            Assert.AreEqual(expected, order.quality, .001f);
            Assert.IsTrue(order.report.comments.Any(line => line.Contains("松茸") && line.Contains("霜降り和牛")));
            Assert.IsFalse(sim.Finish(order.id));
            Assert.IsTrue(sim.Serve(order.id));
            StringAssert.Contains("特別な旨味", order.review);
            Assert.IsTrue(sim.State.achievements.Contains("rare_served"));
            Assert.AreEqual(1, sim.State.stats.rareCombinationBowls);
            Assert.IsFalse(sim.Serve(order.id));
            Assert.AreEqual(1, sim.State.stats.rareCombinationBowls);
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.stats.ingredientsServed, "matsutake"));
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.stats.ingredientsServed, "wagyu"));
        }

        /// <summary>希少食材の加点後も、量不足のおまかせは最終品質60点の上限を守る。</summary>
        [Test]
        public void RareBonusDoesNotExceedTheOmakaseCapAfterActualFinish()
        {
            RestaurantSimulation sim = Create();
            CustomerOrder order = Finishing(sim, "matsutake", "wagyu");
            order.omakase = true;
            order.targetGrams = 4000;
            Assert.AreEqual(60f, Omakase.QualityCap(order, sim.Catalog));
            Assert.AreEqual(60f, QualityEvaluator.Evaluate(order, sim.Catalog, sim.FreshnessScore).total,
                "加点前の品質が上限に達し、希少加点後の再制限を必要とする条件を保証する");
            Assert.IsTrue(sim.Finish(order.id));
            Assert.AreEqual(60f, order.report.total);
            Assert.AreEqual(60f, order.quality);
        }

        /// <summary>異なる希少食材を別々の一杯で提供しても、一杯単位の実績は解除しない。</summary>
        [Test]
        public void SeparateBowlsWithOneRareIngredientEachDoNotUnlockTheCombinationAchievement()
        {
            var sim = Create();
            foreach (string id in new[] { "matsutake", "wagyu" })
            {
                var order = Finishing(sim, id);
                Assert.IsTrue(sim.Finish(order.id));
                Assert.IsTrue(sim.Serve(order.id));
            }
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.stats.ingredientsServed, "matsutake"));
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.stats.ingredientsServed, "wagyu"));
            Assert.AreEqual(0, sim.State.stats.rareCombinationBowls);
            Assert.IsFalse(sim.State.achievements.Contains("rare_served"));
        }

        /// <summary>同じ希少食材を2個入れた一杯は異なる2種類として数えない。</summary>
        [Test]
        public void DuplicateRareIngredientInOneBowlDoesNotUnlockTheCombinationAchievement()
        {
            var sim = Create();
            var order = Finishing(sim, "matsutake", "matsutake");
            Assert.IsTrue(sim.Finish(order.id));
            Assert.IsTrue(sim.Serve(order.id));
            Assert.AreEqual(2, LifetimeStats.Get(sim.State.stats.ingredientsServed, "matsutake"));
            Assert.AreEqual(0, sim.State.stats.rareCombinationBowls);
            Assert.IsFalse(sim.State.achievements.Contains("rare_served"));
        }

        /// <summary>希少2種入りの提供統計は保存され、負数や総提供数超過を修復する。</summary>
        [Test]
        public void CombinationBowlStatisticPersistsAndCorruptedCountsAreClamped()
        {
            var sim = Create();
            var order = Finishing(sim, "matsutake", "wagyu");
            Assert.IsTrue(sim.Finish(order.id));
            Assert.IsTrue(sim.Serve(order.id));
            var loaded = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(sim.State));
            SaveMigration.Migrate(loaded);
            Assert.AreEqual(1, loaded.stats.rareCombinationBowls);
            loaded.stats.rareCombinationBowls = -5;
            SaveMigration.Migrate(loaded);
            Assert.AreEqual(0, loaded.stats.rareCombinationBowls);
            loaded.stats.rareCombinationBowls = int.MaxValue;
            SaveMigration.Migrate(loaded);
            Assert.AreEqual(loaded.stats.served, loaded.stats.rareCombinationBowls);
        }

        /// <summary>6種類すべてを購入すると収集実績を獲得する。</summary>
        [Test]
        public void PurchasingAllSixOffersUnlocksTheCollectionAchievement()
        {
            var sim = Create();
            for (int day = 3; day < 9; day++)
            {
                sim.State.day = day;
                MorningMarket.Ensure(sim);
                Assert.IsTrue(sim.PurchaseRare(day));
            }
            Assert.AreEqual(6, sim.State.ingredients.rareObtainedIds.Count);
            Assert.IsTrue(sim.State.achievements.Contains("rare_all"));
        }

        /// <summary>旧v7の記録は空の希少図鑑と当日オファーから最新形式へ移行する。</summary>
        [Test]
        public void VersionSevenMigratesWithAnEmptyRareCollectionAndAnUnpurchasedDailyOffer()
        {
            var sim = Create();
            sim.State.saveVersion = 7;
            sim.State.stats.served = 8;
            sim.State.stats.rareCombinationBowls = 5;
            sim.State.marketPurchased = true;
            sim.State.ingredients.rareObtainedIds.Add("matsutake");
            SaveMigration.Migrate(sim.State);
            Assert.AreEqual(GameState.CurrentVersion, sim.State.saveVersion);
            Assert.AreEqual(0, sim.State.stats.rareCombinationBowls);
            Assert.IsEmpty(sim.State.ingredients.rareObtainedIds);
            Assert.IsFalse(sim.State.marketPurchased);
            Assert.AreEqual(MorningMarket.Pick(sim.State.storeId, 3), sim.State.marketIngredient);
        }

        /// <summary>壊れた入手記録とオファーを修復し、当日の購入済み状態は保持する。</summary>
        [Test]
        public void CorruptedRareSaveFieldsAreNormalizedWithoutResettingTodaysPurchase()
        {
            var sim = Create();
            sim.State.ingredients.rareObtainedIds = new List<string> { null, "", "unknown", "enoki", "wagyu", "wagyu" };
            sim.State.ingredients.unlockedIds.Add("wagyu");
            sim.State.marketIngredient = "unknown";
            sim.State.marketPurchased = true;
            SaveMigration.Migrate(sim.State);
            CollectionAssert.AreEqual(new[] { "wagyu" }, sim.State.ingredients.rareObtainedIds);
            Assert.IsFalse(sim.State.ingredients.unlockedIds.Contains("wagyu"));
            Assert.AreEqual(MorningMarket.Pick(sim.State.storeId, 3), sim.State.marketIngredient);
            Assert.IsTrue(sim.State.marketPurchased);
            sim.State.ingredients.rareObtainedIds = null;
            sim.State.marketDay = -123;
            SaveMigration.Migrate(sim.State);
            Assert.IsEmpty(sim.State.ingredients.rareObtainedIds);
            Assert.IsFalse(sim.State.marketPurchased);
            Assert.AreEqual(3, sim.State.marketDay);
        }
    }
}
