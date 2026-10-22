using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

/// <summary>食後処理の経済的独立性、時間差集計、保存境界を独立に検証します。</summary>
namespace MalatangStory.Ingredients.Tests
{
    /// <summary>実際の進行とファイル読み込みを通じた食後処理の回帰テストです。</summary>
    public sealed class MealLeftoversIndependentTests
    {
        private IngredientCatalog catalog;
        private string directory;

        /// <summary>各ケース専用の保存先とカタログを準備します。</summary>
        [SetUp]
        public void SetUp()
        {
            catalog = IngredientCatalog.Load();
            directory = Path.Combine(Path.GetTempPath(), "meal-independent-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
        }

        /// <summary>テスト専用の一時保存だけを削除します。</summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        /// <summary>生成イベントを抑止した決定的な営業状態を作ります。</summary>
        private RestaurantSimulation Simulation(bool tutorial = false)
        {
            RestaurantSimulation sim = new RestaurantSimulation(catalog);
            sim.State.storeId = "independent-meal-regression";
            sim.State.tutorialComplete = !tutorial;
            sim.State.tutorialStep = tutorial ? RestaurantSimulation.Tutorial.Serve : RestaurantSimulation.TutorialFinished;
            sim.State.phase = DayPhase.Open;
            sim.State.spawnSeconds = 99999f;
            sim.State.eventTriggered = true;
            return sim;
        }

        /// <summary>品質と茹で加減を独立に指定した注文を作ります。</summary>
        private CustomerOrder Order(RestaurantSimulation sim, string id, string ingredient = "enoki", float ratio = 1f, float quality = 95f)
        {
            CustomerOrder order = new CustomerOrder
            {
                id = id, customerName = "検証客" + id, archetype = "office",
                soup = "mala", chosenSoup = "mala", soupMl = 340f, quality = quality,
                heatSeconds = catalog.Get(ingredient).cookingSeconds * ratio,
                selected = new List<string> { ingredient }, requested = new List<string> { ingredient },
                dropTimes = new List<float> { 0f }, seat = DiningLayout.Seats(sim.State).First().SeatId,
                stage = OrderStage.Ready
            };
            sim.State.orders.Add(order);
            return order;
        }

        /// <summary>実ファイルの包絡とチェックサムを構成します。</summary>
        [Serializable]
        private sealed class SaveEnvelope
        {
            public int version = GameState.CurrentVersion;
            public string payload;
            public string checksum;
        }

        /// <summary>保存時検証を迂回し、移行または破損値を含む有効チェックサムのファイルを書きます。</summary>
        private SaveRepository WritePayload(string payload, int version = GameState.CurrentVersion)
        {
            string checksum;
            using (SHA256 sha = SHA256.Create())
                checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            string path = Path.Combine(directory, "save.json");
            File.WriteAllText(path, JsonUtility.ToJson(new SaveEnvelope { version = version, payload = payload, checksum = checksum }));
            return new SaveRepository(path);
        }

        /// <summary>日報と累計に同じ食事件数が記録されていることを保証します。</summary>
        private static void AssertCounts(GameState state, int meals, int food, int soup)
        {
            {
                Assert.AreEqual(meals, state.report.mealsFinished, "日報の食事終了数");
                Assert.AreEqual(food, state.report.finishedFood, "日報の完食数");
                Assert.AreEqual(soup, state.report.finishedSoup, "日報の完飲数");
                Assert.AreEqual(meals, state.stats.mealsFinished, "累計の食事終了数");
                Assert.AreEqual(food, state.stats.finishedFood, "累計の完食数");
                Assert.AreEqual(soup, state.stats.finishedSoup, "累計の完飲数");
            }
        }

        /// <summary>同一の会計済み注文で食後判定の有無が売上・チップ・値引き・満足度を変えません。</summary>
        [TestCase(95f, 1f)]
        [TestCase(95f, 0f)]
        [TestCase(95f, 3f)]
        [TestCase(50f, 1f)]
        [TestCase(50f, 0f)]
        [TestCase(50f, 3f)]
        public void SamePaidOrderHasIdenticalEconomicsWithAndWithoutMealEvaluation(float quality, float ratio)
        {
            RestaurantSimulation seed = Simulation();
            CustomerOrder source = Order(seed, "economic", ratio: ratio, quality: quality);
            source.stage = OrderStage.Selecting;
            Assert.IsTrue(seed.Weigh(source.id), "通常の会計経路が成立すること");
            Assert.Greater(source.paidFen, 0);
            source.stage = OrderStage.Ready;
            string snapshot = JsonUtility.ToJson(seed.State);
            RestaurantSimulation evaluated = new RestaurantSimulation(catalog, JsonUtility.FromJson<GameState>(snapshot));
            RestaurantSimulation bypassed = new RestaurantSimulation(catalog, JsonUtility.FromJson<GameState>(snapshot));
            CustomerOrder actual = evaluated.State.orders.Single();
            CustomerOrder control = bypassed.State.orders.Single();
            MealLeftovers.Evaluate(actual, catalog);
            Assert.IsTrue(evaluated.Serve(actual.id));
            Assert.IsTrue(bypassed.Serve(control.id));
            // 旧版の食後判定なし経路を再現するため、対照群だけ食事終了の表示へ直接遷移します。
            control.visualState = "leave";
            evaluated.Tick(28f);
            bypassed.Tick(28f);
            Assert.IsTrue(actual.mealFinished);
            Assert.IsFalse(control.mealFinished);
            {
                Assert.AreEqual(bypassed.State.moneyFen, evaluated.State.moneyFen);
                Assert.AreEqual(bypassed.State.report.revenueFen, evaluated.State.report.revenueFen);
                Assert.AreEqual(bypassed.State.report.tipsFen, evaluated.State.report.tipsFen);
                Assert.AreEqual(bypassed.State.stats.tipsFen, evaluated.State.stats.tipsFen);
                Assert.AreEqual(control.tipFen, actual.tipFen);
                Assert.AreEqual(control.discountFen, actual.discountFen);
                Assert.AreEqual(bypassed.State.report.discountsFen, evaluated.State.report.discountsFen);
                Assert.AreEqual(control.report.satisfaction, actual.report.satisfaction);
                Assert.AreEqual(bypassed.State.report.qualitySum, evaluated.State.report.qualitySum);
            }
            if (quality >= 90) Assert.Greater(actual.tipFen, 0, "ゼロのチップ同士だけを比較しないこと");
            else Assert.Greater(actual.discountFen, 0, "値引きが発生する経路も比較すること");
        }

        /// <summary>ふたり連れが時間差で終えても各皿を一度だけ記録し片付け通知を皿単位で保持します。</summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void PairFinishingAtDifferentTimesPreservesCountsAndClearNotices(bool clearBetween, bool reverseFinish)
        {
            RestaurantSimulation sim = Simulation();
            CustomerOrder raw = Order(sim, "pair-raw", ratio: 0f);
            CustomerOrder good = Order(sim, "pair-good");
            SeatInfo[] seats = DiningLayout.Seats(sim.State).Where(x => x.Table.uid == DiningLayout.TableOfSeat(raw.seat)).ToArray();
            good.seat = seats[1].SeatId;
            raw.groupId = good.groupId = "pair-regression";
            good.chosenSoup = "tomato";
            Assert.IsTrue(sim.Serve(raw.id));
            Assert.IsTrue(sim.Serve(good.id));
            CustomerOrder first = reverseFinish ? good : raw;
            CustomerOrder second = reverseFinish ? raw : good;
            first.eatSeconds = 27f;
            List<SimEvent> events = new List<SimEvent>();
            sim.Event += events.Add;
            int table = DiningLayout.TableOfSeat(raw.seat);
            sim.Tick(1f);
            AssertCounts(sim.State, 1, reverseFinish ? 1 : 0, reverseFinish ? 0 : 1);
            Assert.AreEqual(first.id, sim.State.mealRemains.Single().orderId);
            Assert.IsTrue(sim.State.dirtyTables.Contains(table));
            Assert.AreEqual(1, events.Count(x => x.Kind == SimEventKind.TableDirty));
            if (clearBetween)
            {
                Assert.IsTrue(sim.ClearTable(table));
                StringAssert.Contains(reverseFinish ? "完食" : "えのき（固かった）", sim.Notice);
                Assert.IsEmpty(sim.State.mealRemains);
                Assert.IsFalse(sim.ClearTable(table));
            }
            sim.Tick(26f);
            Assert.IsFalse(second.mealFinished);
            sim.Tick(1f);
            Assert.IsTrue(second.mealFinished);
            AssertCounts(sim.State, 2, 1, 1);
            Assert.AreEqual(1, LifetimeStats.Get(sim.State.report.leftovers, "enoki|raw"));
            Assert.AreEqual(2, events.Count(x => x.Kind == SimEventKind.MealFinished));
            Assert.AreEqual(2, events.Count(x => x.Kind == SimEventKind.TableDirty));
            Assert.AreEqual(1, sim.State.dirtyTables.Count(x => x == table));
            Assert.IsTrue(sim.ClearTable(table));
            if (!clearBetween || reverseFinish) StringAssert.Contains("えのき（固かった）", sim.Notice);
            if (!clearBetween || !reverseFinish) StringAssert.Contains("完食", sim.Notice);
            Assert.AreEqual(clearBetween ? 2 : 1, events.Count(x => x.Kind == SimEventKind.TableCleared));
            Assert.IsEmpty(sim.State.mealRemains);
            Assert.IsFalse(sim.ClearTable(table));
            sim.State.phase = DayPhase.Closing;
            sim.Tick(.1f);
            Assert.AreEqual(DayPhase.Report, sim.State.phase);
            AssertCounts(sim.State, 2, 1, 1);
        }

        /// <summary>練習営業でも6秒の境界で完食・完飲を独立集計し日報と再読込に保持します。</summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void TutorialSixSecondMealPersistsIndependentFoodAndSoupCounts(bool food, bool soup)
        {
            RestaurantSimulation sim = Simulation(true);
            CustomerOrder order = Order(sim, "tutorial", ratio: food ? 1f : 0f);
            if (!soup) order.chosenSoup = "tomato";
            Assert.IsTrue(sim.Serve(order.id));
            sim.Tick(5.5f);
            AssertCounts(sim.State, 0, 0, 0);
            sim.Tick(.5f);
            AssertCounts(sim.State, 1, food ? 1 : 0, soup ? 1 : 0);
            sim.Tick(6f);
            Assert.IsTrue(sim.ClearTable(DiningLayout.TableOfSeat(order.seat)));
            Assert.AreEqual(RestaurantSimulation.Tutorial.Close, sim.State.tutorialStep);
            sim.FinishTutorial();
            Assert.AreEqual(DayPhase.Report, sim.State.phase);
            SaveRepository repository = new SaveRepository(Path.Combine(directory, "tutorial.json"));
            repository.Save(sim.State);
            GameState loaded = repository.Load();
            Assert.NotNull(loaded, repository.LastError);
            AssertCounts(loaded, 1, food ? 1 : 0, soup ? 1 : 0);
        }

        /// <summary>実際の50杯目で実績を一度だけ発行し、再読込と51杯目でも再発行しません。</summary>
        [Test]
        public void FiftiethFinishedFoodAwardsExactlyOnceAcrossSaveReloadAndNextMeal()
        {
            RestaurantSimulation sim = Simulation();
            sim.State.stats.served = sim.State.stats.mealsFinished = sim.State.stats.finishedFood = 49;
            CustomerOrder order = Order(sim, "fiftieth");
            List<SimEvent> events = new List<SimEvent>();
            sim.Event += events.Add;
            Progression.CheckAchievements(sim);
            Assert.IsFalse(sim.State.achievements.Contains("finished_food50"));
            Assert.IsTrue(sim.Serve(order.id));
            sim.Tick(27.5f);
            Assert.AreEqual(49, sim.State.stats.finishedFood);
            sim.Tick(.5f);
            Assert.AreEqual(50, sim.State.stats.finishedFood);
            Assert.AreEqual(1, events.Count(x => x.Kind == SimEventKind.Achievement && x.Id == "finished_food50"));
            string highlight = sim.State.report.highlights.Single(x => x.Contains(Content.Achievements.Single(a => a.Id == "finished_food50").Name));
            SaveRepository repository = new SaveRepository(Path.Combine(directory, "achievement.json"));
            for (int i = 0; i < 2; i++)
            {
                repository.Save(sim.State);
                GameState loaded = repository.Load();
                Assert.NotNull(loaded, repository.LastError);
                sim = new RestaurantSimulation(catalog, loaded);
                sim.Event += events.Add;
                sim.Tick(28f);
                Progression.CheckAchievements(sim);
                Assert.AreEqual(50, sim.State.stats.finishedFood);
                Assert.AreEqual(1, loaded.achievements.Count(x => x == "finished_food50"));
                Assert.AreEqual(1, loaded.report.highlights.Count(x => x == highlight));
            }
            Assert.IsTrue(sim.ClearTable(DiningLayout.TableOfSeat(order.seat)));
            CustomerOrder next = Order(sim, "fifty-first");
            Assert.IsTrue(sim.Serve(next.id));
            sim.Tick(28f);
            Assert.AreEqual(51, sim.State.stats.finishedFood);
            Assert.AreEqual(1, events.Count(x => x.Kind == SimEventKind.Achievement && x.Id == "finished_food50"));
        }

        /// <summary>9杯全員完食では実績が付かず10杯全員完食の閉店時だけ一度付きます。</summary>
        [TestCase(9, false)]
        [TestCase(10, true)]
        [TestCase(11, true)]
        public void WholeDayAchievementUsesActualMealCountAndTenBowlMinimum(int bowls, bool expected)
        {
            RestaurantSimulation sim = Simulation();
            List<SimEvent> events = new List<SimEvent>();
            sim.Event += events.Add;
            for (int i = 0; i < bowls; i++)
            {
                CustomerOrder order = Order(sim, "day-" + i);
                Assert.IsTrue(sim.Serve(order.id));
                sim.Tick(28f);
                Assert.IsTrue(sim.ClearTable(DiningLayout.TableOfSeat(order.seat)));
            }
            AssertCounts(sim.State, bowls, bowls, bowls);
            Assert.IsFalse(sim.State.achievements.Contains("all_finished_day"));
            sim.State.phase = DayPhase.Closing;
            sim.Tick(.1f);
            sim.Tick(28f);
            Assert.AreEqual(DayPhase.Report, sim.State.phase);
            Assert.AreEqual(expected ? 1 : 0, sim.State.stats.allFinishedDays);
            Assert.AreEqual(expected ? 1 : 0, events.Count(x => x.Kind == SimEventKind.Achievement && x.Id == "all_finished_day"));
        }

        /// <summary>読み込み時に注文と残り器の負値・非数・過大スープ量を安全な値へ補正します。</summary>
        [TestCase(-.5f, 0f)]
        [TestCase(float.NaN, MealLeftovers.SmallSoupRemaining)]
        [TestCase(1.5f, 1f)]
        public void LoadingRepairsSoupRemainingInBothOrderAndBowl(float invalid, float expected)
        {
            RestaurantSimulation sim = Simulation();
            CustomerOrder order = Order(sim, "invalid-soup");
            Assert.IsTrue(sim.Serve(order.id));
            sim.Tick(28f);
            order.leftovers.soupRemaining = invalid;
            sim.State.mealRemains.Single().leftovers.soupRemaining = invalid;
            string payload = JsonUtility.ToJson(sim.State);
            if (float.IsNaN(invalid)) StringAssert.Contains("NaN", payload, "非数を含むファイルを実際に読み込むこと");
            SaveRepository repository = WritePayload(payload);
            GameState loaded = repository.Load();
            Assert.NotNull(loaded, repository.LastError);
            Assert.IsNull(repository.LastError, "バックアップに逃げず主保存を補正すること");
            Assert.AreEqual(expected, loaded.orders.Single().leftovers.soupRemaining);
            Assert.AreEqual(expected, loaded.mealRemains.Single().leftovers.soupRemaining);
            AssertCounts(loaded, 1, 1, 1);
        }

        /// <summary>未知の残食IDは除去し、器の具材構成自体が未知ならその器だけを除去します。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LoadingRemovesUnknownLeftoverIngredientWithoutLosingValidRecords(bool unknownSelected)
        {
            RestaurantSimulation sim = Simulation();
            CustomerOrder order = Order(sim, "unknown-id", ratio: 0f);
            Assert.IsTrue(sim.Serve(order.id));
            sim.Tick(28f);
            order.leftovers.ingredients.Add(new LeftoverIngredient { index = 1, ingredientId = "does-not-exist", reason = "raw" });
            if (unknownSelected) sim.State.mealRemains.Single().selected.Add("does-not-exist");
            SaveRepository repository = WritePayload(JsonUtility.ToJson(sim.State));
            GameState loaded = repository.Load();
            Assert.NotNull(loaded, repository.LastError);
            Assert.IsNull(repository.LastError);
            Assert.AreEqual("enoki", loaded.orders.Single().leftovers.ingredients.Single().ingredientId);
            Assert.AreEqual(unknownSelected ? 0 : 1, loaded.mealRemains.Count);
            if (!unknownSelected) Assert.AreEqual("enoki", loaded.mealRemains.Single().leftovers.ingredients.Single().ingredientId);
            AssertCounts(loaded, 1, 0, 1);
        }

        /// <summary>実在しない席番号の残り器と注文割当を読み込み時に除去します。</summary>
        [TestCase(-99)]
        [TestCase(999999)]
        public void LoadingRemovesNonexistentSeatRemainsAndRepairsOrderAssignment(int invalidSeat)
        {
            RestaurantSimulation sim = Simulation();
            CustomerOrder order = Order(sim, "invalid-seat");
            Assert.IsTrue(sim.Serve(order.id));
            sim.Tick(28f);
            sim.State.mealRemains.Single().seat = invalidSeat;
            order.seat = invalidSeat;
            SaveRepository repository = WritePayload(JsonUtility.ToJson(sim.State));
            GameState loaded = repository.Load();
            Assert.NotNull(loaded, repository.LastError);
            Assert.IsNull(repository.LastError);
            Assert.IsEmpty(loaded.mealRemains);
            Assert.Less(loaded.orders.Single().seat, 0);
            AssertCounts(loaded, 1, 1, 1);
        }

        /// <summary>v8の欠落フィールドを含む食事中保存を移行し、残り時間後に一度だけ集計します。</summary>
        [TestCase(0f)]
        [TestCase(12f)]
        [TestCase(27.5f)]
        public void VersionEightEatingSaveFinishesExactlyOnceAfterMigrationAndRepeatedReload(float elapsed)
        {
            RestaurantSimulation sim = Simulation();
            CustomerOrder order = Order(sim, "version-eight", ratio: 0f);
            Assert.IsTrue(sim.Serve(order.id));
            order.eatSeconds = elapsed;
            sim.State.saveVersion = 8;
            string payload = JsonUtility.ToJson(sim.State);
            payload = Regex.Replace(payload, "\"mealFinished\":false,", "");
            payload = Regex.Replace(payload, "\"leftovers\":\\{[^{}]*\\},", "");
            payload = Regex.Replace(payload, "\"(?:leftovers|mealRemains)\":\\[\\],", "");
            payload = Regex.Replace(payload, "\"(?:mealsFinished|finishedFood|finishedSoup|allFinishedDays)\":0,", "");
            foreach (string field in new[] { "mealFinished", "leftovers", "mealRemains", "mealsFinished", "finishedFood", "finishedSoup", "allFinishedDays" })
                StringAssert.DoesNotContain("\"" + field + "\":", payload, "v9追加フィールドがないv8保存を使うこと");
            SaveRepository repository = WritePayload(payload, 8);
            GameState loaded = repository.Load();
            Assert.NotNull(loaded, repository.LastError);
            Assert.AreEqual(GameState.CurrentVersion, loaded.saveVersion);
            Assert.AreEqual(elapsed, loaded.orders.Single().eatSeconds);
            AssertCounts(loaded, 0, 0, 0);
            sim = new RestaurantSimulation(catalog, loaded);
            List<SimEvent> events = new List<SimEvent>();
            sim.Event += events.Add;
            Assert.DoesNotThrow(() => sim.Tick(27.75f - elapsed));
            AssertCounts(sim.State, 0, 0, 0);
            Assert.DoesNotThrow(() => sim.Tick(.25f));
            AssertCounts(sim.State, 1, 0, 1);
            for (int i = 0; i < 2; i++)
            {
                repository.Save(sim.State);
                loaded = repository.Load();
                Assert.NotNull(loaded, repository.LastError);
                sim = new RestaurantSimulation(catalog, loaded);
                sim.Event += events.Add;
                Assert.DoesNotThrow(() => sim.Tick(28f));
                AssertCounts(loaded, 1, 0, 1);
                Assert.AreEqual(1, loaded.mealRemains.Count);
                Assert.AreEqual(1, LifetimeStats.Get(loaded.report.leftovers, "enoki|raw"));
            }
            Assert.AreEqual(1, events.Count(x => x.Kind == SimEventKind.MealFinished));
        }
    }
}
