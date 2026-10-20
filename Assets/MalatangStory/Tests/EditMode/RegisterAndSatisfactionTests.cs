using System;
using System.IO;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>レジ接客（注文票・計量・おつり）と、満足度によるチップ・値引き・コンボ、ミッション、グランプリ、客の多様性を保証するテスト。</summary>
    public class RegisterAndSatisfactionTests
    {
        private static RestaurantSimulation Open()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            Assert.IsTrue(sim.BeginDay());
            return sim;
        }

        private static CustomerOrder Waiting(RestaurantSimulation sim, OrderStyle style, string archetype = "office", params string[] items)
        {
            var o = new CustomerOrder { id = "t-" + sim.State.orders.Count, customerName = "テストさん", archetype = archetype, style = style, stage = OrderStage.Waiting, soup = "mala", spice = 2, patience = 999 };
            o.requested.AddRange(items.Length > 0 ? items : new[] { "bok_choy", "enoki" });
            if (style == OrderStyle.SelfServe) CustomerDirector.SelectAll(o);
            sim.State.orders.Add(o);
            return o;
        }

        [Test]
        public void MisheardTicketIsCorrectedByTheCustomerAndLowersServiceUntilWrittenCorrectly()
        {
            var sim = Open();
            var o = Waiting(sim, OrderStyle.Ordered);
            Assert.IsTrue(sim.TakeOrder(o.id));
            Assert.AreEqual(o.id, sim.PlayerFocusOrderId, "接客中の客にスタッフは手を出さない");
            string said = "";
            sim.Event += e => { if (e.Kind == SimEventKind.CustomerSpeech) said = e.Text; };
            Assert.IsFalse(sim.ConfirmTicket(o.id, "tomato", 2, false), "スープを聞き間違えると確定できない");
            StringAssert.Contains("麻辣", said, "客が正しいスープを言い直す");
            Assert.AreEqual(1, o.registerMistakes);
            Assert.Less(o.serviceBonus, 0f);
            Assert.IsFalse(sim.Pay(o.id, 0), "注文票の確認前は会計できない");
            Assert.IsTrue(sim.ConfirmTicket(o.id, "mala", 2, false));
            Assert.IsTrue(o.ticketConfirmed);
        }

        [Test]
        public void CashChangeMustNotBeShortAndOverpaymentIsRecordedAsALoss()
        {
            var sim = Open();
            var o = Waiting(sim, OrderStyle.Ordered);
            Assert.IsTrue(sim.TakeOrder(o.id));
            o.payment = PaymentMethod.Cash; o.tenderedFen = o.billFen + 5000;
            Assert.IsTrue(sim.ConfirmTicket(o.id, o.soup, o.spice, o.takeout));
            long money = sim.State.moneyFen;
            Assert.IsFalse(sim.Pay(o.id, 4990), "おつりが1角足りなければ客に指摘される");
            Assert.AreEqual(OrderStage.Ordering, o.stage);
            Assert.AreEqual(money, sim.State.moneyFen, "失敗した会計では現金は動かない");
            Assert.IsTrue(sim.Pay(o.id, 5100), "多すぎるおつりでも会計は終わる");
            Assert.AreEqual(100, sim.State.report.changeLossFen, "渡しすぎた1元は損失");
            Assert.AreEqual(money + o.billFen - 100, sim.State.moneyFen);
            Assert.AreEqual(o.billFen, sim.State.report.revenueFen, "売上は代金どおり");
            Assert.AreEqual(OrderStage.Selecting, o.stage, "注文式は会計後に具材選びへ");
            Assert.AreEqual(0, sim.State.report.exactChange);
        }

        [Test]
        public void ExactCashChangeCountsAndDigitalPaymentsTakeNoChange()
        {
            var sim = Open();
            var cash = Waiting(sim, OrderStyle.Ordered);
            sim.TakeOrder(cash.id);
            cash.payment = PaymentMethod.Cash; cash.tenderedFen = 10000;
            sim.ConfirmTicket(cash.id, cash.soup, cash.spice, cash.takeout);
            Assert.IsTrue(sim.Pay(cash.id, 10000 - cash.billFen));
            Assert.AreEqual(1, sim.State.report.exactChange);
            Assert.AreEqual(1, sim.State.stats.exactChange);
            Assert.Greater(cash.serviceBonus, 0f, "ぴったりの会計は接客の評価を上げる");

            var qr = Waiting(sim, OrderStyle.Ordered);
            sim.TakeOrder(qr.id);
            qr.payment = PaymentMethod.QrCode; qr.tenderedFen = 0;
            sim.ConfirmTicket(qr.id, qr.soup, qr.spice, qr.takeout);
            Assert.IsFalse(sim.Pay(qr.id, 100), "QR払いにおつりは出せない");
            Assert.IsTrue(sim.Pay(qr.id, 0));
        }

        [Test]
        public void SelfServeBowlIsWeighedAtTheRegisterThenGoesStraightToTheKitchen()
        {
            var sim = Open();
            var o = Waiting(sim, OrderStyle.SelfServe);
            Assert.IsTrue(sim.TakeOrder(o.id));
            Assert.IsTrue(sim.ConfirmTicket(o.id, o.soup, o.spice, o.takeout));
            Assert.IsFalse(sim.Pay(o.id, RestaurantSimulation.ExpectedChange(o)), "量る前は会計できない");
            Assert.IsTrue(sim.WeighAtRegister(o.id));
            Assert.AreEqual(o.selected.Sum(x => sim.Catalog.Get(x).portionGrams), o.weighedGrams, .01f);
            Assert.IsTrue(sim.Pay(o.id, RestaurantSimulation.ExpectedChange(o)));
            Assert.AreEqual(OrderStage.Weighed, o.stage);
            Assert.IsNull(sim.PlayerFocusOrderId, "セルフ式は会計で接客が終わる");
        }

        [Test]
        public void TipGrowsWithSatisfactionGenerosityAndComboWhileComplaintsCutTheBill()
        {
            Assert.AreEqual(0, Satisfaction.Tip(5000, 79.9f, 1, 0), "満足度80未満はチップなし");
            Assert.AreEqual(250, Satisfaction.Tip(5000, 80, 1, 0), "80点で代金の5%");
            Assert.AreEqual(1250, Satisfaction.Tip(5000, 100, 1, 0));
            Assert.AreEqual(1880, Satisfaction.Tip(5000, 100, 1, 10), "10連続で1.5倍（1角単位）");
            Assert.AreEqual(0, Satisfaction.Tip(5000, 100, 0, 0), "評論家はチップを払わない");
            Assert.AreEqual(0, Satisfaction.Discount(5000, 60, 1), "60点以上は値引きなし");
            Assert.AreEqual(600, Satisfaction.Discount(5000, 50, 1), "50点で12%");
            Assert.AreEqual(2000, Satisfaction.Discount(5000, 30, 1), "30点で40%");
            Assert.AreEqual(3000, Satisfaction.Discount(5000, 0, 1), "最大6割");
            Assert.AreEqual(0, Satisfaction.Discount(5000, 0, 0), "審査員は値引きを求めない");
        }

        [Test]
        public void PoorBowlIsDiscountedFromRevenueAndBreaksTheCombo()
        {
            var sim = Open();
            var o = Waiting(sim, OrderStyle.Ordered);
            o.takeout = true;
            Assert.IsTrue(sim.Accept(o.id));
            foreach (string id in o.requested) sim.AddIngredient(o.id, id);
            sim.Weigh(o.id); sim.StartHeating(o.id);
            sim.State.combo = 4;
            sim.Tick(400);
            sim.Drain(o.id);
            sim.PourSoup(o.id, "tomato", 600);
            for (int i = 0; i < 5; i++) sim.AddCondiment(o.id, Content.ChiliOil);
            Assert.IsTrue(sim.Finish(o.id));
            o.serviceBonus = -10;
            long money = sim.State.moneyFen, revenue = sim.State.report.revenueFen;
            Assert.IsTrue(sim.Serve(o.id));
            Assert.Less(o.report.satisfaction, Satisfaction.ComplaintThreshold, "煮えすぎ・違うスープ・辛すぎは不満");
            Assert.Greater(o.discountFen, 0);
            Assert.AreEqual(money - o.discountFen, sim.State.moneyFen);
            Assert.AreEqual(revenue - o.discountFen, sim.State.report.revenueFen, "値引きは売上から差し引く");
            Assert.AreEqual(1, sim.State.report.complaints);
            Assert.AreEqual(0, sim.State.combo, "不満でコンボが途切れる");
            Assert.AreEqual(o.paidFen - o.discountFen, sim.State.ingredients.settledRevenueFen, "食材解禁の累計売上も値引き後");
            Assert.IsNotEmpty(o.reaction);
        }

        [Test]
        public void GreatBowlEarnsATipAndExtendsTheCombo()
        {
            var sim = Open();
            var o = Waiting(sim, OrderStyle.Ordered, "tourist");
            o.takeout = true;
            RestaurantTests.Cook(sim, o);
            Assert.GreaterOrEqual(o.report.satisfaction, Satisfaction.TipThreshold);
            Assert.Greater(o.tipFen, 0);
            Assert.AreEqual(o.tipFen, sim.State.report.tipsFen);
            Assert.AreEqual(1, sim.State.combo);
        }

        [Test]
        public void VegetariansNeverOrderMeatAndAreUpsetWhenServedIt()
        {
            var sim = Open();
            sim.State.shopLevel = 6;
            for (int i = 0; i < 40; i++)
            {
                var o = sim.Customers.Create("vegetarian");
                Assert.IsNotNull(o);
                foreach (string id in o.requested)
                    CollectionAssert.DoesNotContain(new[] { "meat", "seafood", "processed" }, sim.Catalog.Get(id).category, "ベジタリアンの注文に肉・魚介・加工品は入らない");
            }
            var veg = Content.Archetype("vegetarian");
            var meat = sim.Catalog.ingredients.First(x => x.category == "meat").id;
            var order = new CustomerOrder { id = "v", archetype = "vegetarian", quality = 90 };
            order.selected.Add(meat);
            Assert.IsTrue(Satisfaction.ServedForbidden(sim, order, veg));
            Assert.AreEqual(50, Satisfaction.Compute(sim, order, veg), 8, "肉を出すと満足度が大きく下がる");
        }

        [Test]
        public void ArchetypesFollowShopLevelAndTimeOfDay()
        {
            var sim = Open();
            sim.State.shopLevel = 15;
            sim.State.daySeconds = 0;
            for (int i = 0; i < 120; i++)
            {
                var o = sim.Customers.Create();
                Assert.AreNotEqual("drunk", o.archetype, "ほろ酔いの客は昼には来ない");
                Assert.AreNotEqual("judge", o.archetype, "審査員はグランプリの日だけ");
            }
            var low = Open();
            for (int i = 0; i < 120; i++)
                Assert.LessOrEqual(Content.Archetype(low.Customers.Create().archetype).MinShopLevel, 1, "レベル1では解禁前の客は来ない");
        }

        [Test]
        public void CouplesArriveTogetherAndShareATable()
        {
            var sim = Open();
            sim.State.shopLevel = 15;
            sim.State.daySeconds = RestaurantSimulation.DayDuration * .8f;
            for (int i = 0; i < 400 && !sim.State.orders.Any(o => o.archetype == "couple"); i++)
            {
                foreach (var waiting in sim.Queue.ToArray()) sim.Cancel(waiting.id);
                sim.State.spawnSeconds = 0; sim.State.spawnedToday = 0;
                sim.Tick(.01f);
            }
            var first = sim.State.orders.First(o => o.archetype == "couple");
            var pair = sim.State.orders.Where(o => o.groupId == first.groupId).ToList();
            Assert.AreEqual(2, pair.Count, "カップルはふたりで来る");
            Assert.IsTrue(pair.All(o => !o.takeout));
            foreach (var o in pair) Assert.IsTrue(sim.Accept(o.id));
            sim.Tick(.1f);
            Assert.IsTrue(pair.All(o => o.seat >= 0));
            Assert.AreEqual(DiningLayout.TableOfSeat(pair[0].seat), DiningLayout.TableOfSeat(pair[1].seat), "同じテーブルに座る");
        }

        [Test]
        public void CashTenderCoversTheBillAndPracticeAlwaysNeedsChange()
        {
            var random = new Random(3);
            for (int i = 0; i < 300; i++)
            {
                long bill = 10 * random.Next(150, 2500);
                long tender = CustomerDirector.Tender(bill, random);
                Assert.GreaterOrEqual(tender, bill);
                Assert.AreEqual(0, tender % 10, "1角単位");
                long practice = CustomerDirector.Tender(bill, random, true);
                Assert.Greater(practice, bill, "練習では必ずおつりが出る");
                Assert.AreEqual(0, practice % 5000, "練習は50元札");
            }
        }

        [Test]
        public void ThreeDeterministicMissionsArePaidOnceWhenCompleted()
        {
            var a = Open(); var b = Open();
            Missions.Generate(a); Missions.Generate(b);
            Assert.AreEqual(Missions.PerDay, a.State.missions.Count);
            CollectionAssert.AreEqual(a.State.missions.Select(m => m.kind + m.target + m.param), b.State.missions.Select(m => m.kind + m.target + m.param), "同じ日・同じ店なら同じミッション");
            Assert.AreEqual(a.State.missions.Count, a.State.missions.Select(m => m.kind).Distinct().Count(), "種類は重複しない");
            var mission = a.State.missions[0];
            mission.kind = "serve"; mission.target = 1;
            long money = a.State.moneyFen; int completed = a.State.stats.missionsCompleted;
            a.State.report.served = 1;
            Missions.Update(a);
            Missions.Update(a);
            Assert.IsTrue(mission.done);
            Assert.AreEqual(money + mission.rewardFen, a.State.moneyFen, "報酬は一度だけ");
            Assert.AreEqual(completed + 1, a.State.stats.missionsCompleted);
            Assert.AreEqual(mission.rewardFen, a.State.report.missionRewardsFen);
        }

        [Test]
        public void NoComplaintMissionIsOnlyDecidedAtClosing()
        {
            var sim = Open();
            sim.State.missions.Clear();
            sim.State.missions.Add(new Mission { id = "nc", kind = "noComplaint", target = 1, rewardFen = 100 });
            sim.State.report.served = 3;
            Missions.Update(sim);
            Assert.IsFalse(sim.State.missions[0].done, "営業中は確定しない");
            Missions.Update(sim, true);
            Assert.IsTrue(sim.State.missions[0].done);
        }

        [Test]
        public void GrandPrixIsHeldWeeklyFromDayFourteenAndAwardsByTheJudgesAverage()
        {
            var sim = Open();
            sim.State.day = 14;
            DailyEvents.Roll(sim);
            Assert.AreEqual("contest", sim.State.dailyEvent);
            sim.State.day = 15; DailyEvents.Roll(sim);
            Assert.AreNotEqual("contest", sim.State.dailyEvent);
            Assert.AreEqual(("金賞", 80000L, 6), Contest.Award(90));
            Assert.AreEqual("銀賞", Contest.Award(85).name);
            Assert.AreEqual("銅賞", Contest.Award(70).name);
            Assert.AreEqual(0, Contest.Award(50).prizeFen);

            sim.State.day = 14; sim.State.dailyEvent = "contest"; sim.State.eventSeconds = 1; sim.State.eventTriggered = false;
            sim.Tick(2);
            var judges = sim.State.orders.Where(o => o.archetype == "judge").ToList();
            Assert.AreEqual(3, judges.Count, "審査員は3人");
            long money = sim.State.moneyFen;
            foreach (var j in judges) { j.takeout = true; j.patience = 9999; }
            foreach (var j in judges) RestaurantTests.Cook(sim, j);
            Assert.IsTrue(sim.State.report.contestConcluded);
            Assert.Greater(sim.State.report.prizeFen, 0, "丁寧に作れば入賞する");
            Assert.AreEqual(0, judges.Sum(j => j.tipFen), "審査員はチップを払わない");
            long prize = sim.State.report.prizeFen;
            Contest.Conclude(sim);
            Assert.AreEqual(prize, sim.State.report.prizeFen, "結果は一度だけ");
            Assert.GreaterOrEqual(sim.State.stats.contestGold + sim.State.stats.contestSilver + sim.State.stats.contestBronze, 1);
        }

        [Test]
        public void StarsRiseWithReputationPopularityAndLevel()
        {
            var s = new GameState { reputation = 50, popularity = 30, shopLevel = 1 };
            Assert.AreEqual(1, Progression.Stars(s));
            s.reputation = 80; s.popularity = 70; s.shopLevel = 10;
            Assert.AreEqual(4, Progression.Stars(s));
            s.reputation = 100; s.popularity = 100; s.shopLevel = 15;
            Assert.AreEqual(5, Progression.Stars(s));
        }

        [Test]
        public void RegularsOrderingTheUsualKeepTheirFavouriteSoupAndSpice()
        {
            var sim = Open();
            sim.State.regulars.Add(new RegularCustomer { id = "r1", name = "常連", archetype = "office", visits = 10, favoriteSoup = "baitang", favoriteSpice = 3, affinity = 60 });
            CustomerOrder usual = null;
            for (int i = 0; i < 300 && usual == null; i++)
            {
                var o = sim.Customers.Create();
                if (o != null && o.usual) usual = o;
            }
            Assert.IsNotNull(usual, "通い慣れた常連は「いつもの」で頼む");
            Assert.AreEqual("baitang", usual.soup);
            Assert.AreEqual(3, usual.spice);
            Assert.AreEqual(OrderStyle.Ordered, usual.style);
            StringAssert.Contains("いつもの", CustomerVoice.OrderLines(usual, sim.Catalog)[0]);
        }

        [Test]
        public void VersionThreeSaveKeepsMissionsCountersAndRegisterState()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-v3-" + Guid.NewGuid());
            try
            {
                var repo = new SaveRepository(Path.Combine(folder, "save.json"));
                var sim = Open();
                Missions.Generate(sim);
                var o = Waiting(sim, OrderStyle.Ordered);
                sim.TakeOrder(o.id);
                LifetimeStats.Add(sim.State.stats.archetypesServed, "tourist", 2);
                sim.State.combo = 7;
                repo.Save(sim.State);
                var loaded = repo.Load();
                Assert.AreEqual(GameState.CurrentVersion, loaded.saveVersion);
                Assert.AreEqual(sim.State.missions.Count, loaded.missions.Count);
                Assert.AreEqual(2, LifetimeStats.Get(loaded.stats.archetypesServed, "tourist"));
                Assert.AreEqual(7, loaded.combo);
                var restored = loaded.orders.Single(x => x.id == o.id);
                Assert.AreEqual(OrderStage.Ordering, restored.stage);
                Assert.AreEqual(o.billFen, restored.billFen);
                Assert.AreEqual(o.tenderedFen, restored.tenderedFen);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void ClosingSendsHomeCustomersStillTalkingAtTheRegisterWithoutCharging()
        {
            var sim = Open();
            var o = Waiting(sim, OrderStyle.Ordered);
            sim.TakeOrder(o.id);
            sim.State.daySeconds = RestaurantSimulation.DayDuration - .5f;
            sim.Tick(1);
            Assert.AreEqual(OrderStage.Cancelled, o.stage);
            Assert.AreEqual(0, o.paidFen);
            Assert.AreEqual(0, sim.State.report.revenueFen, "会計前の客からは何も受け取らない");
        }

        [Test]
        public void ArchetypeOutfitsAreDeterministicAndDistinctive()
        {
            foreach (var a in Content.Archetypes)
            {
                var x = CharacterAppearance.ForArchetype(a.Id, 1234, a.Child);
                var y = CharacterAppearance.ForArchetype(a.Id, 1234, a.Child);
                Assert.AreEqual(JsonUtility(x), JsonUtility(y), "同じ種なら同じ見た目：" + a.Id);
            }
            Assert.AreEqual(3, CharacterAppearance.ForArchetype("worker", 5, false).outfit, "作業員は作業着");
            Assert.AreEqual(1, CharacterAppearance.ForArchetype("worker", 5, false).headwear, "作業員は工事ヘルメット");
            Assert.AreEqual(4, CharacterAppearance.ForArchetype("rider", 5, false).bag, "配達員は配達ボックス");
            Assert.IsTrue(CharacterAppearance.ForArchetype("elder", 5, false).elderly);
            Assert.AreEqual(.7f, CharacterAppearance.ForArchetype("family", 5, true).height, .001f, "子どもは小さい");
            Assert.AreEqual(JsonUtility(CharacterAppearance.FromSeed(77)), JsonUtility(CharacterAppearance.ForArchetype("unknown-type", 77, false)), "未知の客タイプは従来どおり");
        }

        [Test]
        public void RevenueMissionCountsOnlyServedBowlsSoRefundedPrepaymentsDoNotPay()
        {
            var sim = Open();
            sim.State.missions.Clear();
            var mission = new Mission { id = "rev", kind = "revenue", target = 1, rewardFen = 5000 };
            sim.State.missions.Add(mission);
            var o = Waiting(sim, OrderStyle.Ordered);
            Assert.IsTrue(sim.Accept(o.id));
            Assert.Greater(o.paidFen, 100);
            Assert.IsFalse(mission.done, "前受けの代金だけでは達成しない");
            Assert.IsTrue(sim.Cancel(o.id));
            Missions.Update(sim, true);
            Assert.IsFalse(mission.done, "返金した注文は売上ミッションに数えない");
        }

        [Test]
        public void LegacyUnpaidSelfServeBowlIsTrimmedToStockWhenWeighedInTheKitchen()
        {
            var sim = Open();
            foreach (var stock in sim.State.stock) stock.quantity = 0;
            sim.StockFor("bok_choy").quantity = 1;
            sim.StockFor("enoki").quantity = 2;
            var a = new CustomerOrder { id = "a", style = OrderStyle.SelfServe, stage = OrderStage.Selecting, patience = 999 };
            var b = new CustomerOrder { id = "b", style = OrderStyle.SelfServe, stage = OrderStage.Selecting, patience = 999 };
            foreach (var o in new[] { a, b }) { o.requested.AddRange(new[] { "bok_choy", "enoki" }); CustomerDirector.SelectAll(o); sim.State.orders.Add(o); }
            Assert.IsTrue(sim.Weigh("a"), "v2 の未会計セルフ式は不足分を外して量れる");
            Assert.IsTrue(sim.Weigh("b"));
            Assert.AreEqual(1, new[] { a, b }.Sum(o => o.selected.Count(x => x == "bok_choy")), "最後の1個は一方にだけ入る");
            Assert.Greater(a.paidFen, 0); Assert.Greater(b.paidFen, 0);
        }

        [Test]
        public void ACoupleWaitsForThePartnerAndSitsAtOneTable()
        {
            var sim = Open();
            var first = Waiting(sim, OrderStyle.Ordered, "couple");
            var second = Waiting(sim, OrderStyle.Ordered, "couple");
            first.groupId = second.groupId = "pair-x";
            Assert.IsTrue(sim.Accept(first.id));
            sim.Tick(.1f);
            Assert.Less(first.seat, 0, "相手の会計が済むまで座らずに待つ");
            Assert.IsTrue(sim.Accept(second.id));
            sim.Tick(.1f);
            Assert.GreaterOrEqual(first.seat, 0);
            Assert.AreEqual(DiningLayout.TableOfSeat(first.seat), DiningLayout.TableOfSeat(second.seat));
        }

        [Test]
        public void NightRegularsDoNotVisitAtLunch()
        {
            var sim = Open();
            sim.State.regulars.Add(new RegularCustomer { id = "night", name = "夜の常連", archetype = "drunk", visits = 5, favoriteSoup = "mala", favoriteSpice = 3 });
            sim.State.daySeconds = 0;
            for (int i = 0; i < 300; i++) Assert.AreNotEqual("drunk", sim.Customers.Create().archetype, "ほろ酔いの常連は昼に来ない");
        }

        private static string JsonUtility(CharacterAppearance a) => UnityEngine.JsonUtility.ToJson(a);
    }
}
