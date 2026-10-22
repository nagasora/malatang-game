using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

/// <summary>食後の純粋判定・営業への接続・保存互換を検証します。</summary>
namespace MalatangStory.Ingredients.Tests
{
    /// <summary>経済計算を変えずに食べ残しを記録できることを保証します。</summary>
    public sealed class MealLeftoversTests
    {
        private IngredientCatalog catalog;

        /// <summary>判定に使うカタログを読み込みます。</summary>
        [SetUp]
        public void SetUp() => catalog = IngredientCatalog.Load();

        /// <summary>具材一個が適正に茹でられた注文を作ります。</summary>
        private CustomerOrder Order(string id = "enoki", float ratio = 1f)
        {
            return new CustomerOrder { id = "meal-test", customerName = "試食客", soup = "mala", chosenSoup = "mala", soupMl = 340,
                quality = 95, heatSeconds = catalog.Get(id).cookingSeconds * ratio, selected = new List<string> { id }, requested = new List<string> { id },
                dropTimes = new List<float> { 0f } };
        }

        /// <summary>境界を含めて生・適正・過煮を判定します。</summary>
        [TestCase(0f, "raw")]
        [TestCase(.59f, "raw")]
        [TestCase(.6f, "")]
        [TestCase(1f, "")]
        [TestCase(2f, "")]
        [TestCase(2.01f, "overcooked")]
        public void DonenessDecidesLeftovers(float ratio, string reason)
        {
            MealLeftoverResult result = MealLeftovers.Evaluate(Order("enoki", ratio), catalog);
            Assert.AreEqual(reason == "", result.FinishedFood);
            if (reason != "") Assert.AreEqual(reason, result.ingredients.Single().reason);
        }

        /// <summary>おまかせの苦手カテゴリを残します。</summary>
        [Test]
        public void OmakaseAvoidedCategoryRemains()
        {
            CustomerOrder order = Order(); order.omakase = true; order.avoidCategory = catalog.Get("enoki").category;
            Assert.AreEqual("avoid", MealLeftovers.Evaluate(order, catalog).ingredients.Single().reason);
        }

        /// <summary>通常注文にはおまかせ専用のカテゴリ指定を適用しません。</summary>
        [Test]
        public void OrdinaryOrderIgnoresOmakaseCategory()
        {
            CustomerOrder order = Order(); order.avoidCategory = catalog.Get("enoki").category;
            Assert.IsTrue(MealLeftovers.Evaluate(order, catalog).FinishedFood);
        }

        /// <summary>客タイプの食事制限は良い茹で加減でも残します。</summary>
        [Test]
        public void ForbiddenCategoryRemains()
        {
            CustomerArchetype vegetarian = Content.Archetypes.First(x => x.ForbiddenCategories.Contains("meat"));
            CustomerOrder order = Order("beef_slice"); order.archetype = vegetarian.Id;
            Assert.AreEqual("forbidden", MealLeftovers.Evaluate(order, catalog).ingredients.Single().reason);
        }

        /// <summary>低品質のときだけ、注文個数を超えた具材を残します。</summary>
        [TestCase(59f, false, 1)]
        [TestCase(60f, false, 0)]
        [TestCase(20f, true, 0)]
        public void UnorderedExtraDependsOnQualityAndStyle(float quality, bool omakase, int count)
        {
            CustomerOrder order = Order(); order.quality = quality; order.omakase = omakase;
            order.selected.Add("enoki"); order.dropTimes.Add(order.dropTimes[0]);
            MealLeftoverResult result = MealLeftovers.Evaluate(order, catalog);
            Assert.AreEqual(count, result.ingredients.Count);
            if (count > 0) { Assert.AreEqual(1, result.ingredients[0].index); Assert.AreEqual("unordered", result.ingredients[0].reason); }
        }

        /// <summary>スープの違い・辛さ差・苦手薬味は多めに残します。</summary>
        [TestCase("soup_mismatch")]
        [TestCase("spice")]
        [TestCase("condiment")]
        public void SoupProblemsRemainEvenAtHighQuality(string reason)
        {
            CustomerOrder order = Order();
            if (reason == "soup_mismatch") order.chosenSoup = "tomato";
            if (reason == "spice") order.chosenSpice = 2;
            if (reason == "condiment") { order.condiments.Add(Content.ChiliOil); order.dislikedCondiments.Add(Content.ChiliOil); }
            MealLeftoverResult result = MealLeftovers.Evaluate(order, catalog);
            Assert.AreEqual(reason, result.soupReason); Assert.AreEqual(MealLeftovers.LargeSoupRemaining, result.soupRemaining);
        }

        /// <summary>品質とスープ点が両方90以上のときだけ完飲します。</summary>
        [TestCase(89f, 340f, false)]
        [TestCase(90f, 340f, true)]
        [TestCase(100f, 1f, false)]
        public void EmptySoupNeedsBothScores(float quality, float ml, bool empty)
        {
            CustomerOrder order = Order(); order.quality = quality; order.soupMl = ml;
            MealLeftoverResult result = MealLeftovers.Evaluate(order, catalog);
            Assert.AreEqual(empty, result.FinishedSoup);
            if (!empty) Assert.AreEqual(MealLeftovers.SmallSoupRemaining, result.soupRemaining);
        }

        /// <summary>完食と完飲は独立し、判定は入力を変更せず決定的です。</summary>
        [Test]
        public void EvaluationIsDeterministicPureAndFoodIsIndependent()
        {
            CustomerOrder order = Order("enoki", 0f);
            string before = JsonUtility.ToJson(order);
            string first = JsonUtility.ToJson(MealLeftovers.Evaluate(order, catalog));
            Assert.AreEqual(first, JsonUtility.ToJson(MealLeftovers.Evaluate(order, catalog)));
            Assert.AreEqual(before, JsonUtility.ToJson(order));
            Assert.IsFalse(MealLeftovers.Evaluate(order, catalog).FinishedFood);
            Assert.IsTrue(MealLeftovers.Evaluate(order, catalog).FinishedSoup);
        }

        /// <summary>営業中に食事終了へ進める状態を作ります。</summary>
        private RestaurantSimulation Eating(CustomerOrder order, bool tutorial = false)
        {
            RestaurantSimulation sim = new RestaurantSimulation(catalog);
            sim.State.tutorialComplete = !tutorial; sim.State.tutorialStep = tutorial ? 0 : RestaurantSimulation.TutorialFinished;
            sim.State.phase = DayPhase.Open; sim.State.spawnSeconds = 9999;
            order.stage = OrderStage.Served; order.visualState = "eat"; order.seat = DiningLayout.Seats(sim.State).First().SeatId;
            sim.State.orders.Add(order); sim.State.report.served = 1; sim.State.stats.served = 1;
            return sim;
        }

        /// <summary>食事時間の境界で一度だけ記録し、売上・チップ・満足度を変えません。</summary>
        [TestCase(false, 28f)]
        [TestCase(true, 6f)]
        public void FinishMealRecordsOnceWithoutEconomicChanges(bool tutorial, float duration)
        {
            CustomerOrder order = Order(); RestaurantSimulation sim = Eating(order, tutorial);
            long money = sim.State.moneyFen; long revenue = sim.State.report.revenueFen; long tips = sim.State.stats.tipsFen;
            float satisfaction = order.report.satisfaction;
            sim.Tick(duration - .1f); Assert.IsFalse(order.mealFinished);
            sim.Tick(.2f); Assert.IsTrue(order.mealFinished);
            sim.Tick(duration);
            Assert.AreEqual(1, sim.State.report.finishedFood); Assert.AreEqual(1, sim.State.stats.finishedSoup);
            Assert.AreEqual(1, sim.State.mealRemains.Count); Assert.Contains("first_empty_soup", sim.State.achievements);
            Assert.AreEqual(money, sim.State.moneyFen); Assert.AreEqual(revenue, sim.State.report.revenueFen);
            Assert.AreEqual(tips, sim.State.stats.tipsFen); Assert.AreEqual(satisfaction, order.report.satisfaction);
        }

        /// <summary>片付け時に実際の具材名と理由を通知し、残り器の保存記録も消します。</summary>
        [Test]
        public void ClearingShowsReasonAndRemovesRemains()
        {
            CustomerOrder order = Order("enoki", 0f); RestaurantSimulation sim = Eating(order);
            sim.Tick(28f); int table = DiningLayout.TableOfSeat(order.seat);
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.report.leftovers, "enoki|raw"));
            Assert.IsTrue(sim.ClearTable(table)); Assert.AreEqual(0, sim.State.mealRemains.Count);
            StringAssert.Contains("えのき", sim.Notice); StringAssert.Contains("固かった", sim.Notice);
            Assert.IsFalse(sim.ClearTable(table));
        }

        /// <summary>日報は理由が分かれた場合も具材の合計で最多を決めます。</summary>
        [Test]
        public void DailyReportAggregatesIngredientBeforeReason()
        {
            DailyReport report = new DailyReport();
            LifetimeStats.Add(report.leftovers, "enoki|raw", 3); LifetimeStats.Add(report.leftovers, "enoki|overcooked", 3);
            LifetimeStats.Add(report.leftovers, "beef_slice|raw", 5);
            string text = MealLeftovers.DailyMostLeft(report, catalog);
            StringAssert.Contains(catalog.Get("enoki").displayName, text); StringAssert.Contains("6個", text);
        }

        /// <summary>追加実績を含め63件となり、50杯の境界を判定します。</summary>
        [Test]
        public void AchievementsHaveCorrectCountAndThreshold()
        {
            RestaurantSimulation sim = new RestaurantSimulation(catalog);
            Assert.AreEqual(63, Content.Achievements.Length);
            sim.State.stats.finishedFood = 49; Progression.CheckAchievements(sim);
            Assert.IsFalse(sim.State.achievements.Contains("finished_food50"));
            sim.State.stats.finishedFood = 50; Progression.CheckAchievements(sim);
            Assert.Contains("finished_food50", sim.State.achievements);
        }

        /// <summary>閉店確定時にのみ10杯以上の全員完食実績を記録します。</summary>
        [TestCase(9, 9, false)]
        [TestCase(10, 9, false)]
        [TestCase(10, 10, true)]
        public void EntireDayAchievementRequiresEveryServedBowl(int served, int finished, bool achieved)
        {
            RestaurantSimulation sim = new RestaurantSimulation(catalog);
            sim.State.tutorialComplete = true; sim.State.report.served = served; sim.State.report.mealsFinished = served;
            sim.State.report.finishedFood = finished;
            sim.State.orders.Add(new CustomerOrder { id = "summary", stage = OrderStage.Served });
            typeof(RestaurantSimulation).GetMethod("EndDay", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sim, null);
            Assert.AreEqual(achieved, sim.State.achievements.Contains("all_finished_day"));
        }

        /// <summary>v8の食事中と食後の注文を壊さず、食後の未記録はフォールバックします。</summary>
        [TestCase("eat")]
        [TestCase("leave")]
        public void VersionEightMigratesWithoutInventingFinishedMeals(string visual)
        {
            CustomerOrder order = Order(); RestaurantSimulation sim = Eating(order);
            sim.State.saveVersion = 8; order.visualState = visual; order.eatSeconds = 12;
            sim.State.dirtyTables.Add(DiningLayout.TableOfSeat(order.seat));
            SaveMigration.Migrate(sim.State);
            Assert.AreEqual(9, sim.State.saveVersion); Assert.AreEqual(12f, order.eatSeconds);
            Assert.AreEqual(visual, order.visualState); Assert.IsFalse(order.mealFinished); Assert.IsEmpty(sim.State.mealRemains);
        }

        /// <summary>食事中・食後・片付け後の実ファイル保存を往復できます。</summary>
        [Test]
        public void SaveRepositoryRestoresEatingAndFinishedAndClearedState()
        {
            string directory = Path.Combine(Path.GetTempPath(), "meal-save-" + Guid.NewGuid());
            try
            {
                SaveRepository repository = new SaveRepository(Path.Combine(directory, "save.json"));
                CustomerOrder order = Order("enoki", 0f); RestaurantSimulation sim = Eating(order);
                sim.Tick(10); repository.Save(sim.State);
                GameState eating = repository.Load(); Assert.NotNull(eating); Assert.AreEqual(10, eating.orders[0].eatSeconds);
                RestaurantSimulation resumed = new RestaurantSimulation(catalog, eating); resumed.Tick(18);
                repository.Save(resumed.State); GameState finished = repository.Load();
                Assert.NotNull(finished); Assert.IsTrue(finished.orders[0].mealFinished);
                Assert.AreEqual("enoki", finished.mealRemains.Single().leftovers.ingredients.Single().ingredientId);
                Assert.AreEqual(1, finished.stats.mealsFinished);
                RestaurantSimulation cleared = new RestaurantSimulation(catalog, finished);
                cleared.ClearTable(DiningLayout.TableOfSeat(order.seat)); repository.Save(cleared.State);
                Assert.IsEmpty(repository.Load().mealRemains);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        /// <summary>不正な残量・重複位置・未知ID・範囲外の統計値を読み込み時に正規化します。</summary>
        [Test]
        public void NormalizationRemovesInvalidValuesAndDuplicatePositions()
        {
            CustomerOrder order = Order("enoki", 0f); RestaurantSimulation sim = Eating(order); sim.Tick(28);
            MealRemains remains = sim.State.mealRemains.Single(); remains.spice = 999; remains.soup = "unknown";
            remains.leftovers.soupRemaining = float.NaN;
            remains.leftovers.ingredients.Add(new LeftoverIngredient { index = 0, ingredientId = "enoki", reason = "raw" });
            remains.leftovers.ingredients.Add(new LeftoverIngredient { index = 10, ingredientId = "missing", reason = "unknown" });
            sim.State.stats.finishedFood = -1; sim.State.report.finishedSoup = 999;
            order.eatSeconds = float.PositiveInfinity;
            SaveMigration.Migrate(sim.State);
            Assert.AreEqual(1, remains.leftovers.ingredients.Count); Assert.AreEqual(5, remains.spice); Assert.AreEqual("mala", remains.soup);
            Assert.AreEqual(MealLeftovers.SmallSoupRemaining, remains.leftovers.soupRemaining);
            Assert.AreEqual(0, sim.State.stats.finishedFood); Assert.AreEqual(1, sim.State.report.finishedSoup); Assert.AreEqual(0, order.eatSeconds);
        }

        /// <summary>食事終了のせりふに客タイプの口調と具体的な理由が含まれます。</summary>
        [Test]
        public void CustomerVoiceIncludesReasonAndArchetypeTone()
        {
            CustomerOrder order = Order("enoki", 0); order.archetype = "elder"; order.leftovers = MealLeftovers.Evaluate(order, catalog);
            string line = CustomerVoice.MealFinished(order, catalog);
            StringAssert.Contains("固かった", line); StringAssert.Contains("じゃ", line); StringAssert.Contains("完飲", line);
        }
        /// <summary>表示状態が壊れても集計済みの食事を再集計しません。</summary>
        [TestCase("eat")]
        [TestCase("")]
        [TestCase("invalid")]
        public void FinishedMealSurvivesInconsistentVisualState(string visual)
        {
            string directory = Path.Combine(Path.GetTempPath(), "meal-count-" + Guid.NewGuid());
            try
            {
                RestaurantSimulation sim = Eating(Order("enoki", 0f)); sim.Tick(28f);
                sim.State.orders[0].visualState = visual;
                SaveRepository repository = new SaveRepository(Path.Combine(directory, "save.json")); repository.Save(sim.State);
                for (int i = 0; i < 2; i++)
                {
                    GameState loaded = repository.Load(); Assert.NotNull(loaded);
                    Assert.IsTrue(loaded.orders[0].mealFinished); Assert.AreEqual("leave", loaded.orders[0].visualState);
                    RestaurantSimulation resumed = new RestaurantSimulation(catalog, loaded); resumed.Tick(.1f);
                    Assert.AreEqual(1, loaded.stats.mealsFinished); Assert.AreEqual(0, loaded.stats.finishedFood);
                    Assert.AreEqual(1, loaded.stats.finishedSoup); Assert.AreEqual(1, loaded.report.mealsFinished);
                    Assert.AreEqual(0, loaded.report.finishedFood); Assert.AreEqual(1, loaded.report.finishedSoup);
                    Assert.AreEqual(1, LifetimeStats.Get(loaded.report.leftovers, "enoki|raw"));
                    repository.Save(loaded);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        /// <summary>チェックサムが正しい主保存の不正家具を除去し、残りの進行を保持します。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void InvalidFurnitureIsRemovedWithoutDiscardingValidProgress(bool nullEntry)
        {
            string directory = Path.Combine(Path.GetTempPath(), "meal-backup-" + Guid.NewGuid());
            try
            {
                string file = Path.Combine(directory, "save.json"); SaveRepository repository = new SaveRepository(file);
                GameState state = new RestaurantSimulation(catalog).State; repository.Save(state); File.Copy(file, file + ".bak");
                if (nullEntry) state.furniture.Add(null); else state.furniture[0].defId = "unknown";
                string payload = JsonUtility.ToJson(state);
                if (nullEntry)
                {
                    int start = payload.IndexOf("\"furniture\":[", StringComparison.Ordinal) + "\"furniture\":[".Length;
                    payload = payload.Insert(start, "null,");
                }
                string checksum;
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                    checksum = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload)));
                File.WriteAllText(file, JsonUtility.ToJson(new RegressionEnvelope { payload = payload, checksum = checksum }));
                GameState loaded = repository.Load(); Assert.NotNull(loaded); Assert.AreEqual(state.storeId, loaded.storeId);
                Assert.IsNull(repository.LastError);
                Assert.DoesNotThrow(() => SaveRepository.Validate(loaded));
                Assert.IsTrue(loaded.furniture.All(x => x != null && Content.Furniture.Any(d => d.Id == x.defId)));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        /// <summary>検証対象の包絡に正しいチェックサムを付けます。</summary>
        [Serializable]
        private sealed class RegressionEnvelope
        {
            public int version = GameState.CurrentVersion;
            public string payload, checksum;
        }

        /// <summary>売却したテーブルの記録だけを消し、別テーブルの残食は保持します。</summary>
        [Test]
        public void SellingTableRemovesOnlyItsMealRemains()
        {
            RestaurantSimulation sim = new RestaurantSimulation(catalog);
            var tables = sim.State.furniture.Where(x => Content.FurnitureById(x.defId).Kind == FurnitureKind.Table).ToList();
            var seats = DiningLayout.Seats(sim.State).ToList();
            int table = tables[0].uid; int other = tables[1].uid;
            sim.State.mealRemains.Add(new MealRemains { seat = seats.First(x => x.Table.uid == table).SeatId });
            sim.State.mealRemains.Add(new MealRemains { seat = seats.First(x => x.Table.uid == other).SeatId });
            sim.State.dirtyTables.Add(table); sim.State.dirtyTables.Add(other);
            Assert.IsTrue(sim.SellFurniture(table)); Assert.AreEqual(1, sim.State.mealRemains.Count);
            Assert.AreEqual(other, DiningLayout.TableOfSeat(sim.State.mealRemains[0].seat));
        }
    }
}
