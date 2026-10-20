using System;
using System.IO;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>営業シミュレーションの中核ルールを保証するテスト。</summary>
    public class RestaurantTests
    {
        private RestaurantSimulation Create(bool tutorialComplete = false)
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.tutorialComplete = tutorialComplete;
            if (tutorialComplete) sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            return sim;
        }

        /// <summary>理想的な手順で1杯を作って提供する（茹で時間の長い具材から投入）。</summary>
        internal static void Cook(RestaurantSimulation sim, CustomerOrder order)
        {
            Assert.IsTrue(sim.Accept(order.id), "レジで受付・会計できること");
            if (order.stage == OrderStage.Selecting)
            {
                var picks = order.omakase ? Omakase.AutoPick(order, sim.Unlocks.Available, x => sim.Available(x, order)) : order.requested;
                foreach (string id in picks) Assert.IsTrue(sim.AddIngredient(order.id, id));
                Assert.IsTrue(sim.Weigh(order.id), "厨房の秤で量れること");
            }
            Assert.AreEqual(OrderStage.Weighed, order.stage);
            int guard = 0;
            while (!sim.StartHeating(order.id, false))
            {
                Assert.Less(guard++, 400, "鍋が空くこと");
                CookOthersQuickly(sim, order);
                sim.Tick(1);
            }
            float slowest = order.selected.Max(x => sim.Catalog.Get(x).cookingSeconds);
            DropDue(sim, order, slowest);
            while (Enumerable.Range(0, order.selected.Count).Any(i => sim.Doneness(order, i) < 1f))
            {
                Assert.Less(guard++, 800, "茹で上がること");
                sim.Tick(.5f);
                DropDue(sim, order, slowest);
            }
            Assert.IsTrue(sim.Drain(order.id));
            for (int i = 0; i < 3; i++) sim.Shake(order.id);
            var soup = Content.Soup(order.soup);
            Assert.IsTrue(sim.PourSoup(order.id, order.soup, (soup.IdealMinMl + soup.IdealMaxMl) / 2));
            for (int i = 0; i < order.spice; i++) sim.AddCondiment(order.id, Content.ChiliOil);
            foreach (string id in order.requestedCondiments.Distinct()) sim.AddCondiment(order.id, id);
            Assert.IsTrue(sim.Finish(order.id));
            while (!order.takeout && order.seat < 0)
            {
                Assert.Less(guard++, 1500, "席が空くこと");
                foreach (int table in sim.State.dirtyTables.ToArray()) sim.ClearTable(table);
                sim.Tick(1);
            }
            Assert.IsTrue(sim.Serve(order.id));
        }

        private static void DropDue(RestaurantSimulation sim, CustomerOrder order, float slowest)
        {
            for (int i = 0; i < order.selected.Count; i++)
                if (order.dropTimes[i] < 0 && order.heatSeconds >= (slowest - sim.Catalog.Get(order.selected[i]).cookingSeconds) * sim.CookSpeedMultiplier / sim.CookSpeedMultiplier)
                    sim.DropIngredient(order.id, i);
        }

        private static void CookOthersQuickly(RestaurantSimulation sim, CustomerOrder except)
        {
            foreach (var other in sim.Active.Where(o => o != except && o.stage == OrderStage.Heating).ToArray())
            {
                sim.DropRemaining(other.id);
                if (Enumerable.Range(0, other.selected.Count).All(i => sim.Doneness(other, i) >= .9f)) sim.Drain(other.id);
            }
        }

        [Test]
        public void FirstDayTutorialTeachesTheFullCookingFlowThenPurchaseAndContinuesIntoDayTwo()
        {
            var sim = Create();
            Assert.IsTrue(sim.BeginDay());
            sim.Tick(60);
            Assert.AreEqual(0, sim.State.daySeconds, "練習中は営業時間が進まない");
            var order = sim.Active.Single();
            Assert.IsFalse(sim.Accept(order.id), "移動の練習前は受付できない");
            sim.TutorialAction(RestaurantSimulation.Tutorial.Move);
            Assert.IsTrue(sim.Accept(order.id));
            foreach (string id in order.requested) Assert.IsTrue(sim.AddIngredient(order.id, id));
            Assert.AreEqual(RestaurantSimulation.Tutorial.Weigh, sim.State.tutorialStep);
            Assert.IsTrue(sim.Weigh(order.id));
            Assert.IsTrue(sim.StartHeating(order.id));
            Assert.IsFalse(sim.Drain(order.id), "生のままでは引き上げられない");
            sim.Tick(order.selected.Max(x => sim.Catalog.Get(x).cookingSeconds));
            Assert.IsTrue(sim.Drain(order.id));
            Assert.IsFalse(sim.PourSoup(order.id, "mala", 340), "湯切りの前にスープは注げない");
            sim.Shake(order.id); sim.Shake(order.id);
            Assert.IsTrue(sim.PourSoup(order.id, "mala", 340));
            Assert.IsFalse(sim.Finish(order.id), "辛さを合わせるまで完成できない");
            sim.AddCondiment(order.id, Content.ChiliOil);
            Assert.IsTrue(sim.Finish(order.id));
            for (int i = 0; i < 3 && order.seat < 0; i++) sim.Tick(1);
            Assert.IsTrue(sim.Serve(order.id));
            sim.Tick(7);
            Assert.AreEqual(1, sim.State.dirtyTables.Count, "食べ終えた席は片付けが必要");
            Assert.IsTrue(sim.ClearTable(sim.State.dirtyTables[0]));
            Assert.AreEqual(RestaurantSimulation.Tutorial.Close, sim.State.tutorialStep);
            sim.FinishTutorial();
            Assert.AreEqual(DayPhase.Report, sim.State.phase);
            Assert.IsFalse(sim.NextDay());
            Assert.IsTrue(sim.Purchase("bok_choy", 5));
            Assert.IsTrue(sim.State.tutorialComplete);
            Assert.IsTrue(sim.NextDay());
            Assert.AreEqual(2, sim.State.day);
            Assert.IsTrue(sim.BeginDay());
            sim.Tick(5);
            Assert.IsTrue(sim.Active.Any(), "2日目は客が来る");
        }

        [Test]
        public void RegisterChargesByWeightOnceAndCancellationRefundsExactlyOnce()
        {
            var sim = Create(true); sim.BeginDay(); sim.Tick(5);
            var o = sim.Active.First();
            long before = sim.State.moneyFen;
            Assert.IsTrue(sim.TakeOrder(o.id));
            Assert.AreEqual(OrderStage.Ordering, o.stage);
            Assert.AreEqual(before, sim.State.moneyFen, "注文を聞くだけでは会計しない");
            var items = o.style == OrderStyle.SelfServe ? o.selected : o.requested;
            long expected = RestaurantSimulation.RoundToJiao(items.Sum(x => sim.Catalog.Get(x).portionGrams) * sim.State.pricePer100gFen / 100d);
            Assert.AreEqual(expected, o.billFen, "請求額は重さ×100g単価を1角単位に丸めたもの");
            Assert.IsTrue(sim.Accept(o.id), "注文票・計量・おつりを正しく済ませて会計できる");
            Assert.AreEqual(expected, o.paidFen);
            Assert.IsFalse(sim.Accept(o.id), "二重会計しない");
            Assert.AreEqual(before + expected, sim.State.moneyFen);
            if (o.stage == OrderStage.Selecting)
            {
                foreach (string id in o.requested) sim.AddIngredient(o.id, id);
                Assert.IsTrue(sim.Weigh(o.id));
                Assert.AreEqual(before + expected, sim.State.moneyFen, "支払い済みの注文は厨房で量っても再請求しない");
            }
            Assert.IsTrue(sim.StartHeating(o.id));
            long cost = sim.State.report.foodCostFen;
            Assert.IsFalse(sim.StartHeating(o.id));
            Assert.AreEqual(cost, sim.State.report.foodCostFen, "原価は一度だけ計上");
            Assert.IsTrue(sim.Cancel(o.id)); Assert.IsFalse(sim.Cancel(o.id));
            Assert.AreEqual(before, sim.State.moneyFen);
            Assert.AreEqual(0, sim.State.report.revenueFen);
            Assert.AreEqual(0, sim.State.ingredients.settledRevenueFen);
        }

        [Test]
        public void StaggeredDroppingByCookingTimeScoresHigherThanDroppingEverythingAtOnce()
        {
            var catalog = IngredientCatalog.Load();
            CustomerOrder Make(bool staggered)
            {
                var o = new CustomerOrder { soup = "mala", spice = 1, chosenSoup = "mala", soupMl = 340, chosenSpice = 1, shakes = 3, patience = 200 };
                o.requested.AddRange(new[] { "potato", "bok_choy", "beef_slice", "mung_bean_noodle" });
                o.selected.AddRange(o.requested);
                float slowest = o.selected.Max(x => catalog.Get(x).cookingSeconds);
                foreach (string id in o.selected) o.dropTimes.Add(staggered ? slowest - catalog.Get(id).cookingSeconds : 0);
                o.heatSeconds = slowest;
                return o;
            }
            var good = QualityEvaluator.Evaluate(Make(true), catalog, _ => 100);
            var bad = QualityEvaluator.Evaluate(Make(false), catalog, _ => 100);
            Assert.AreEqual(100, good.doneness, 0.01f);
            Assert.Less(bad.doneness, 70, "葉物や薄切り肉を最初から入れると煮えすぎる");
            Assert.Greater(good.total, bad.total + 5);
            Assert.IsTrue(bad.comments.Any(c => c.Contains("煮えすぎ") || c.Contains("のびてる")), "弱点がコメントに出る");
        }

        [Test]
        public void DislikedCondimentsAndWrongSpiceLowerSeasoningWithSpecificComplaints()
        {
            var catalog = IngredientCatalog.Load();
            var o = new CustomerOrder { soup = "baitang", spice = 0, chosenSoup = "baitang", soupMl = 340, chosenSpice = 2, shakes = 3, patience = 200 };
            o.requested.Add("bok_choy"); o.selected.Add("bok_choy"); o.dropTimes.Add(0); o.heatSeconds = catalog.Get("bok_choy").cookingSeconds;
            o.dislikedCondiments.Add("coriander"); o.condiments.Add("coriander");
            var report = QualityEvaluator.Evaluate(o, catalog, _ => 100);
            Assert.Less(report.seasoning, 30);
            Assert.IsTrue(report.comments.Any(c => c.Contains("抜いてほしかった")));
        }

        [Test]
        public void ClosingStopsNewCustomersButSeatedGuestsFinishBeforeTheReport()
        {
            var sim = Create(true); sim.BeginDay(); sim.Tick(5);
            var o = sim.Active.First();
            sim.State.daySeconds = 719; sim.Tick(2);
            Assert.AreNotEqual(DayPhase.Open, sim.State.phase);
            Assert.AreEqual(OrderStage.Cancelled, o.stage, "受付前の客は閉店で帰る");
            int spawned = sim.State.spawnedToday; sim.Tick(30);
            Assert.AreEqual(spawned, sim.State.spawnedToday, "閉店後は新しい客が来ない");
            Assert.AreEqual(DayPhase.Report, sim.State.phase, "残りの客がいなければ営業終了");

            sim = Create(true); sim.BeginDay(); sim.Tick(5);
            o = sim.Active.First();
            o.takeout = false;
            Cook(sim, o);
            sim.State.daySeconds = 719; sim.Tick(2);
            Assert.AreEqual(DayPhase.Closing, sim.State.phase, "食事中の客がいる間は営業終了しない");
            sim.Tick(RestaurantSimulation.EatDuration + 1);
            Assert.AreEqual(DayPhase.Report, sim.State.phase);
            Assert.AreEqual(1, sim.State.report.served);
        }

        [Test]
        public void HundredBusinessDaysKeepCashInventoryProgressionAndSavesConsistent()
        {
            var sim = Create(true);
            int totalServed = 0;
            for (int day = 1; day <= 100; day++)
            {
                Assert.AreEqual(day, sim.State.day);
                long startingCash = sim.State.moneyFen;
                sim.RequestSupport();
                sim.RestockAll(15);
                if (day == 20) { sim.State.moneyFen += 200000; startingCash += 200000; sim.HireStaff(StaffRole.Server); }
                Assert.IsTrue(sim.BeginDay());
                int guard = 0;
                while (sim.State.phase != DayPhase.Report && guard++ < 6000)
                {
                    sim.Tick(1);
                    foreach (int table in sim.State.dirtyTables.ToArray()) sim.ClearTable(table);
                    var next = sim.Queue.FirstOrDefault();
                    if (next == null) continue;
                    if (next.requested.Any(id => sim.StockFor(id).quantity < 1)) { sim.Cancel(next.id); continue; }
                    Cook(sim, next); totalServed++;
                }
                Assert.Less(guard, 6000, "一日が終わること");
                Assert.IsFalse(sim.Active.Any());
                Assert.IsTrue(sim.State.stock.All(x => x.quantity >= 0 && x.quantity <= sim.State.storageCapacity));
                var r = sim.State.report;
                Assert.AreEqual(startingCash + r.supportFen + r.revenueFen + r.tipsFen + r.missionRewardsFen + r.prizeFen
                    - r.purchasesFen - r.fixedCostFen - r.wagesFen - r.investmentFen - r.changeLossFen,
                    sim.State.moneyFen, "現金は売上（値引き後）・チップ・ミッション・賞金・支援・仕入れ・固定費・人件費・投資・おつりの損失で説明できる");
                SaveRepository.Validate(sim.State);
                Assert.IsTrue(sim.NextDay());
            }
            Assert.Greater(totalServed, 500);
            Assert.Greater(sim.State.shopLevel, 5, "営業を続けると店舗レベルが上がる");
            Assert.Greater(sim.Unlocks.Available.Count(), 10);
            Assert.IsTrue(sim.State.achievements.Contains("first_bowl"));
            Assert.AreEqual(101, sim.State.day);
        }

        [Test]
        public void HiredStaffCanRunTheWholeServiceWithoutThePlayer()
        {
            var sim = Create(true);
            sim.State.shopLevel = 6; sim.State.moneyFen = 1000000;
            foreach (StaffRole role in Enum.GetValues(typeof(StaffRole))) Assert.IsTrue(sim.HireStaff(role));
            sim.RestockAll(30);
            Assert.IsTrue(sim.BeginDay());
            int guard = 0;
            while (sim.State.phase != DayPhase.Report && guard++ < 3000) sim.Tick(1);
            Assert.AreEqual(DayPhase.Report, sim.State.phase);
            Assert.Greater(sim.State.report.served, 5, "スタッフだけで提供できる");
            Assert.Greater(sim.State.report.AverageQuality, 60, "スタッフの調理は一定の品質を保つ");
            Assert.Greater(sim.State.report.wagesFen, 0);
        }

        [Test]
        public void LayoutRejectsOverlapsBlockedEntranceAndUnreachableTables()
        {
            var sim = Create(true);
            var s = sim.State;
            Assert.IsTrue(DiningLayout.AllTablesReachable(s, s.furniture));
            Assert.IsFalse(DiningLayout.CanPlace(s, "plant", 5, DiningLayout.Rows(s) - 1, 0, -1, out _), "入口はふさげない");
            Assert.IsFalse(DiningLayout.CanPlace(s, "plant", 1, 0, 0, -1, out _), "重ねて置けない");
            Assert.IsFalse(DiningLayout.CanPlace(s, "plant", 12, 0, 0, -1, out _), "フロア外には置けない");
            Assert.IsTrue(DiningLayout.CanPlace(s, "plant", 5, 0, 0, -1, out _));
            // 入口の周囲を家具で囲むと、どの席にも行けなくなる。
            s.furniture.Clear();
            s.furniture.Add(new PlacedFurniture { uid = 1, defId = "table_round2", x = 0, z = 0 });
            s.furniture.Add(new PlacedFurniture { uid = 2, defId = "folding_screen", x = 3, z = 3 });
            s.furniture.Add(new PlacedFurniture { uid = 3, defId = "folding_screen", x = 7, z = 3 });
            Assert.IsFalse(DiningLayout.CanPlace(s, "folding_screen", 5, 2, 0, -1, out string reason));
            StringAssert.Contains("通路", reason);
            s.shopLevel = 6; s.moneyFen = 1000000;
            Assert.IsTrue(sim.BuyFurniture("table_square4", 8, 0, 0));
            Assert.AreEqual(6, DiningLayout.SeatCount(s));
        }

        [Test]
        public void ExperienceRaisesShopLevelUnlockingSoupsAndRecordsAchievements()
        {
            var sim = Create(true);
            Assert.AreEqual(3, sim.Customers.AvailableSoups.Count());
            Progression.AddExperience(sim, Content.ExperienceForLevel(3));
            Assert.AreEqual(3, sim.State.shopLevel);
            Assert.IsTrue(sim.Customers.AvailableSoups.Any(x => x.Id == "sour"));
            Assert.IsTrue(Progression.UnlocksAt(3).Any(x => x.Contains("酸菜")));
            sim.State.stats.served = 1;
            Progression.CheckAchievements(sim);
            Assert.IsTrue(sim.State.achievements.Contains("first_bowl"));
            int count = sim.State.achievements.Count;
            Progression.CheckAchievements(sim);
            Assert.AreEqual(count, sim.State.achievements.Count, "実績は重複しない");
        }

        [Test]
        public void CustomersAreDeterministicAndOnlyRequestStockedIngredients()
        {
            var a = Create(true); var b = Create(true);
            foreach (var stock in a.State.stock.Where(x => x.ingredientId != "bok_choy" && x.ingredientId != "enoki")) stock.quantity = 0;
            foreach (var stock in b.State.stock.Where(x => x.ingredientId != "bok_choy" && x.ingredientId != "enoki")) stock.quantity = 0;
            for (int i = 0; i < 20; i++)
            {
                var x = a.Customers.Create(); var y = b.Customers.Create();
                CollectionAssert.AreEqual(x.requested, y.requested);
                Assert.AreEqual(x.soup, y.soup);
                Assert.IsTrue(x.requested.All(id => id == "bok_choy" || id == "enoki"));
                Assert.IsTrue(x.spice >= 0 && x.spice <= 5);
                Assert.Greater(x.patience, 0);
            }
        }

        [Test]
        public void CriticEventArrivesAndASatisfiedCriticRaisesReputation()
        {
            var sim = Create(true);
            sim.State.dailyEvent = "critic"; sim.State.eventSeconds = 10;
            sim.BeginDay();
            sim.Tick(11);
            var critic = sim.Active.FirstOrDefault(o => o.archetype == "critic");
            Assert.NotNull(critic, "評論家が来店する");
            Cook(sim, critic);
            Assert.GreaterOrEqual(critic.quality, 80, "理想的な手順なら評論家も満足する");
            Assert.AreEqual(1, sim.State.stats.criticsSatisfied);
            Assert.AreEqual(8, sim.State.report.reputationDelta);
        }

        [Test]
        public void SignatureDishesAreRegisteredFromGoodBowlsAndSoldAtAPremium()
        {
            var sim = Create(true); sim.BeginDay(); sim.Tick(5);
            var o = sim.Active.First();
            Cook(sim, o);
            Assert.IsTrue(sim.RegisterSignature(o.id, "店長の特製"));
            var dish = sim.State.signatures.Single();
            var order = new CustomerOrder { signatureId = dish.id };
            order.selected.AddRange(dish.ingredients);
            Assert.AreEqual(RestaurantSimulation.RoundToJiao(sim.Grams(order) * sim.State.pricePer100gFen / 100d * 1.15), sim.PriceFor(order));
        }

        [Test]
        public void SaveRestoresHeatingAndRecoversPreviousGenerationAfterCorruption()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-test-" + Guid.NewGuid());
            try
            {
                var repo = new SaveRepository(Path.Combine(folder, "save.json"));
                var sim = Create(true); sim.BeginDay(); sim.Tick(5);
                var o = sim.Active.First(); sim.Accept(o.id);
                if (o.selected.Count == 0) foreach (string id in o.requested) sim.AddIngredient(o.id, id);
                sim.Weigh(o.id); sim.StartHeating(o.id); sim.Tick(5);
                repo.Save(sim.State); var saved = repo.Load();
                var restored = saved.orders.Single(x => x.id == o.id);
                Assert.AreEqual(OrderStage.Heating, restored.stage);
                Assert.AreEqual(o.heatSeconds, restored.heatSeconds, .001f);
                CollectionAssert.AreEqual(o.dropTimes, restored.dropTimes);
                Assert.AreEqual(sim.State.furniture.Count, saved.furniture.Count);
                sim.Tick(2); repo.Save(sim.State);
                File.WriteAllText(Path.Combine(folder, "save.json"), "corrupt");
                var recovered = repo.Load();
                Assert.NotNull(recovered); Assert.IsNotEmpty(repo.LastError);
                repo.Save(recovered); Assert.NotNull(repo.Load());
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void VersionOneSavesMigrateWithoutLosingProgress()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-test-" + Guid.NewGuid());
            try
            {
                const string payload = "{\"saveVersion\":1,\"storeId\":\"store-001\",\"day\":7,\"phase\":1,\"daySeconds\":100,\"moneyFen\":90000,"
                    + "\"pricePer100gFen\":2000,\"pots\":2,\"storageCapacity\":60,\"reputation\":62,\"tutorialComplete\":true,\"tutorialStep\":10,"
                    + "\"ingredients\":{\"settledRevenueFen\":50000,\"unlockedIds\":[\"enoki\",\"bok_choy\"],\"settledOrderIds\":[]},"
                    + "\"stock\":[{\"ingredientId\":\"enoki\",\"quantity\":10,\"ageDays\":1}],"
                    + "\"orders\":[{\"id\":\"store-001:7:1\",\"customerName\":\"リンさん\",\"type\":\"セルフ式\",\"soup\":\"白湯\",\"spice\":1,\"requested\":[\"enoki\"],\"selected\":[\"enoki\"],\"chosenSoup\":\"麻辣\",\"stage\":1,\"paidFen\":1000}],"
                    + "\"report\":{\"revenueFen\":1000}}";
                string checksum;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    checksum = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload)));
                Directory.CreateDirectory(folder);
                string envelope = JsonUtility.ToJson(new V1Envelope { version = 1, payload = payload, checksum = checksum });
                File.WriteAllText(Path.Combine(folder, "save.json"), envelope);
                var state = new SaveRepository(Path.Combine(folder, "save.json")).Load();
                Assert.NotNull(state);
                Assert.AreEqual(GameState.CurrentVersion, state.saveVersion);
                Assert.AreEqual(7, state.day); Assert.AreEqual(2, state.pots); Assert.AreEqual(60, state.storageCapacity);
                Assert.AreEqual(DayPhase.Report, state.phase, "営業中の旧データは閉店扱いになる");
                Assert.AreEqual(89000 - 1500, state.moneyFen, "受付済みの旧注文は返金され、その日の固定費を精算する");
                Assert.AreEqual("baitang", state.orders[0].soup);
                Assert.Greater(state.furniture.Count, 0, "既定の客席が用意される");
                Assert.AreEqual(50000, state.ingredients.settledRevenueFen);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Serializable] private sealed class V1Envelope { public int version; public string payload, checksum; }

        private static void WriteEnvelope(string file, int version, string payload)
        {
            string checksum;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                checksum = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload)));
            File.WriteAllText(file, JsonUtility.ToJson(new V1Envelope { version = version, payload = payload, checksum = checksum }));
        }

        [Test]
        public void FutureVersionSavesAreNeitherReplacedByTheBackupNorOverwritten()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-test-" + Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "save.json");
                var repo = new SaveRepository(file);
                repo.Save(Create(true).State);
                File.Copy(file, file + ".bak", true);
                WriteEnvelope(file, GameState.CurrentVersion + 1, "{\"saveVersion\":" + (GameState.CurrentVersion + 1) + "}");
                string future = File.ReadAllText(file);
                Assert.IsNull(repo.Load(), "古いバックアップへ黙って戻さない");
                Assert.IsTrue(repo.BlockedByFutureVersion);
                Assert.Throws<FutureSaveVersionException>(() => repo.Save(Create(true).State));
                Assert.AreEqual(future, File.ReadAllText(file), "新しい形式の主ファイルは上書きされない");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void IngredientsAreReservedSoTheLastPieceCannotBeSoldTwice()
        {
            var sim = Create(true); sim.BeginDay();
            foreach (var stock in sim.State.stock) stock.quantity = 0;
            sim.StockFor("bok_choy").quantity = 1;
            var a = new CustomerOrder { id = "a", style = OrderStyle.Ordered, stage = OrderStage.Waiting, patience = 999 };
            var b = new CustomerOrder { id = "b", style = OrderStyle.Ordered, stage = OrderStage.Waiting, patience = 999 };
            a.requested.Add("bok_choy"); b.requested.Add("bok_choy");
            sim.State.orders.Add(a); sim.State.orders.Add(b);
            Assert.IsTrue(sim.Accept("a")); Assert.IsTrue(sim.AddIngredient("a", "bok_choy"));
            Assert.IsFalse(sim.Accept("b"), "他の注文が確保した最後の1個しか頼んでいない客は受けられない");
            Assert.AreEqual(OrderStage.Cancelled, b.stage);
            Assert.AreEqual(0, b.paidFen, "受けられなかった客からはお金を受け取らない");
            Assert.AreEqual(0, sim.Available("bok_choy"));
            Assert.IsNull(sim.Customers.Create(), "確保済みの在庫しかない時は注文を作らない");
            Assert.IsTrue(sim.Cancel("a"));
            Assert.AreEqual(1, sim.Available("bok_choy"), "取消で確保が外れる");
        }

        [Test]
        public void TheFirstDayCanFinishEvenIfStorageWasFilledBeforeTheLastPurchaseLesson()
        {
            var sim = Create();
            sim.State.moneyFen = 10000000;
            foreach (var item in sim.Unlocks.Available.ToArray()) sim.StockFor(item.id).quantity = sim.State.storageCapacity;
            sim.State.phase = DayPhase.Report;
            sim.State.tutorialStep = RestaurantSimulation.Tutorial.Purchase;
            Assert.IsFalse(sim.Purchase("bok_choy", 5), "満杯なら買えない");
            Assert.IsTrue(sim.NextDay(), "保管上限まで仕入れ済みなら練習済みとして翌日へ進める");
            Assert.IsTrue(sim.State.tutorialComplete);
            sim.StockFor("bok_choy").quantity = sim.State.storageCapacity - 2;
            sim.State.phase = DayPhase.Preparation;
            Assert.IsTrue(sim.Purchase("bok_choy", 5), "空きが5個未満でも空き分だけ買える");
            Assert.AreEqual(sim.State.storageCapacity, sim.StockFor("bok_choy").quantity);
        }

        [Test]
        public void SignatureDishesGetUniqueIdsAndTheSameBowlCannotBeRegisteredTwice()
        {
            var sim = Create(true); sim.BeginDay(); sim.Tick(5);
            var first = sim.Active.First(); first.takeout = true; Cook(sim, first);
            Assert.IsTrue(sim.RegisterSignature(first.id, "一号"));
            Assert.IsFalse(sim.RegisterSignature(first.id, "二重"));
            sim.Tick(30);
            var second = sim.Active.FirstOrDefault(o => o.stage == OrderStage.Waiting) ?? sim.Customers.Create();
            if (!sim.State.orders.Contains(second)) sim.State.orders.Add(second);
            second.takeout = true; Cook(sim, second);
            Assert.IsTrue(sim.RegisterSignature(second.id, "二号"));
            Assert.AreEqual(2, sim.State.signatures.Select(d => d.id).Distinct().Count());
            sim.RemoveSignature(sim.State.signatures[0].id);
            Assert.AreEqual(1, sim.State.signatures.Count, "取り下げは1品だけに効く");
        }

        [Test]
        public void InfluencerPopularityEffectSurvivesASaveBeforeClosing()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-test-" + Guid.NewGuid());
            try
            {
                var sim = Create(true);
                sim.State.report.popularityDelta = 12;
                var repo = new SaveRepository(Path.Combine(folder, "save.json"));
                repo.Save(sim.State);
                Assert.AreEqual(12, repo.Load().report.popularityDelta, .001f);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void StaffLeaveTheOrderThePlayerIsWorkingOnAndThePlayerCanTakeOverStaffCooking()
        {
            var sim = Create(true);
            sim.State.shopLevel = 6; sim.State.moneyFen = 1000000;
            Assert.IsTrue(sim.HireStaff(StaffRole.Cook));
            sim.BeginDay(); sim.Tick(5);
            var o = sim.Active.First();
            sim.Accept(o.id);
            if (o.selected.Count == 0) foreach (string id in o.requested) sim.AddIngredient(o.id, id);
            sim.Weigh(o.id);
            Assert.IsTrue(sim.SetPlayerFocus(o.id));
            for (int i = 0; i < 20; i++) sim.Tick(1);
            Assert.AreEqual(OrderStage.Weighed, o.stage, "プレイヤーが扱っている注文にスタッフは手を出さない");
            sim.PlayerFocusOrderId = null;
            for (int i = 0; i < 20 && o.stage == OrderStage.Weighed; i++) sim.Tick(1);
            Assert.AreEqual(OrderStage.Heating, o.stage);
            Assert.IsTrue(o.handledByStaff);
            Assert.IsFalse(sim.SetPlayerFocus(o.id), "スタッフ調理中は閲覧のみ");
            Assert.IsTrue(sim.TakeOver(o.id));
            Assert.IsFalse(o.handledByStaff);
            Assert.AreEqual(o.id, sim.PlayerFocusOrderId);
        }

        [Test]
        public void SaleStockIsCostedAtItsAverageAcquisitionPriceAndSeasoningCostsAreCounted()
        {
            var sim = Create(true);
            // 旬の割引と混ざらないよう、旬を対象外の具材にしておく（この試験はセールの割引だけを見る）。
            sim.State.featuredIngredient = "enoki";
            foreach (var stock in sim.State.stock) stock.quantity = 0;
            sim.State.dailyEvent = "sale";
            Assert.IsTrue(sim.Purchase("bok_choy", 10));
            long salePrice = sim.PurchaseCost(sim.Catalog.Get("bok_choy"));
            Assert.AreEqual(salePrice, sim.UnitCost("bok_choy"));
            sim.State.dailyEvent = "none";
            Assert.IsTrue(sim.Purchase("bok_choy", 10));
            long normal = RestaurantSimulation.Cost(sim.Catalog.Get("bok_choy"));
            Assert.AreEqual((long)Math.Round((salePrice * 10 + normal * 10) / 20.0), sim.UnitCost("bok_choy"), "平均取得原価");
            sim.BeginDay();
            var o = new CustomerOrder { id = "x", style = OrderStyle.Ordered, stage = OrderStage.Waiting, soup = "mala", spice = 1, patience = 999, takeout = true };
            o.requested.Add("bok_choy"); sim.State.orders.Add(o);
            sim.Accept("x"); sim.AddIngredient("x", "bok_choy"); sim.Weigh("x");
            long before = sim.State.report.foodCostFen;
            sim.StartHeating("x"); sim.Tick(20); sim.Drain("x"); sim.Shake("x");
            sim.PourSoup("x", "mala", 340); sim.AddCondiment("x", Content.ChiliOil); sim.Finish("x");
            long expected = sim.UnitCost("bok_choy") + Content.Soup("mala").CostFen + Content.Condiment(Content.ChiliOil).CostFen;
            Assert.AreEqual(expected, sim.State.report.foodCostFen - before, "具材の平均原価＋スープ＋ラー油");
        }

        [Test]
        public void LoadingRepairsMissingMealStateInsteadOfBlockingTheDayForever()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-test-" + Guid.NewGuid());
            try
            {
                var sim = Create(true); sim.BeginDay(); sim.Tick(5);
                var o = sim.Active.First(); o.takeout = false; Cook(sim, o);
                o.visualState = "";
                var repo = new SaveRepository(Path.Combine(folder, "save.json"));
                repo.Save(sim.State);
                var loaded = repo.Load();
                Assert.AreEqual("eat", loaded.orders.Single(x => x.id == o.id).visualState, "食事中の状態に戻す");
                var resumed = new RestaurantSimulation(IngredientCatalog.Load(), loaded);
                resumed.State.daySeconds = 719; resumed.Tick(2); resumed.Tick(RestaurantSimulation.EatDuration + 1);
                Assert.AreEqual(DayPhase.Report, resumed.State.phase, "閉店まで進める");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void ShortageSupportDoesNotCountAsRevenueAndLockedFoodCannotBeBought()
        {
            var sim = Create(true); sim.State.moneyFen = -1200;
            Assert.IsTrue(sim.RequestSupport()); Assert.GreaterOrEqual(sim.State.moneyFen, 5000);
            Assert.AreEqual(0, sim.State.ingredients.settledRevenueFen);
            Assert.IsFalse(sim.Purchase("shrimp", 5));
            sim.State.moneyFen = 50000; sim.Purchase("bok_choy", 5);
            Assert.AreEqual(0, sim.State.report.foodCostFen); Assert.Greater(sim.State.report.purchasesFen, 0);
        }

        [Test]
        public void SaleEventDiscountsPurchasesAndEquipmentRequiresShopLevel()
        {
            var sim = Create(true);
            // 旬の割引と混ざらないよう、旬を対象外の具材にしておく（この試験はセールの割引だけを見る）。
            sim.State.featuredIngredient = "enoki";
            sim.State.dailyEvent = "sale";
            var item = sim.Catalog.Get("bok_choy");
            Assert.AreEqual((long)Math.Round(RestaurantSimulation.Cost(item) * .7, MidpointRounding.AwayFromZero), sim.PurchaseCost(item));
            sim.State.moneyFen = 10000000;
            Assert.IsFalse(sim.Upgrade("stove"), "レベル不足では買えない");
            sim.State.shopLevel = 5;
            Assert.IsTrue(sim.Upgrade("stove"));
            Assert.AreEqual(1.25f, sim.CookSpeedMultiplier);
        }
    }
}
