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
    /// <summary>レジの境界条件と、現金・在庫・接客・精算・保存の不変条件を保証する。</summary>
    public sealed class RegisterEdgeCaseTests
    {
        /// <summary>自然来店と予定イベントを止めた、練習完了済みの営業状態を作る。</summary>
        private static RestaurantSimulation Open()
        {
            var sim = Prepared();
            Assert.IsTrue(sim.BeginDay(), "在庫のある店は開店できる");
            sim.State.spawnSeconds = float.MaxValue;
            return sim;
        }

        /// <summary>副作用を持たない初期店舗を用意する。</summary>
        private static RestaurantSimulation Prepared()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            sim.State.dailyEvent = "none";
            sim.State.eventSeconds = -1;
            return sim;
        }

        /// <summary>席の待ち時間に左右されない注文を追加する。</summary>
        private static CustomerOrder Waiting(RestaurantSimulation sim, OrderStyle style = OrderStyle.Ordered,
            string archetype = "office", params string[] ingredients)
        {
            var order = new CustomerOrder
            {
                id = "edge-" + sim.State.orders.Count, customerName = "境界テストさん", archetype = archetype,
                style = style, stage = OrderStage.Waiting, soup = "mala", spice = 1,
                takeout = true, patience = 9999
            };
            order.requested.AddRange(ingredients.Length == 0 ? new[] { "bok_choy", "enoki" } : ingredients);
            if (style == OrderStyle.SelfServe) CustomerDirector.SelectAll(order);
            sim.State.orders.Add(order);
            return order;
        }

        /// <summary>現金払いを指定し、注文票と必要な計量を確定する。</summary>
        private static void PrepareCash(RestaurantSimulation sim, CustomerOrder order, bool byPlayer = true)
        {
            Assert.IsTrue(sim.TakeOrder(order.id, byPlayer), "在庫のある注文を聞ける");
            order.payment = PaymentMethod.Cash;
            order.tenderedFen = order.billFen + 1000;
            Assert.IsTrue(sim.ConfirmTicket(order.id, order.soup, order.spice, order.takeout), "正しい注文票を確定できる");
            if (order.style == OrderStyle.SelfServe)
                Assert.IsTrue(sim.WeighAtRegister(order.id), "セルフ式はレジで量れる");
        }

        /// <summary>会計済みの注文を、公開調理APIだけで提供する。</summary>
        private static void ServePaid(RestaurantSimulation sim, CustomerOrder order, bool dissatisfied)
        {
            if (order.stage == OrderStage.Selecting)
            {
                foreach (string id in order.requested)
                    Assert.IsTrue(sim.AddIngredient(order.id, id), "確保した注文具材を取り出せる");
                Assert.IsTrue(sim.Weigh(order.id), "会計済み注文を厨房で量れる");
            }
            Assert.IsTrue(sim.StartHeating(order.id, false), "空いている鍋で調理を開始できる");
            float slowest = order.selected.Max(id => sim.Catalog.Get(id).cookingSeconds);
            while (order.heatSeconds < slowest)
            {
                for (int i = 0; i < order.selected.Count; i++)
                    if (order.dropTimes[i] < 0 && order.heatSeconds >= slowest - sim.Catalog.Get(order.selected[i]).cookingSeconds)
                        Assert.IsTrue(sim.DropIngredient(order.id, i), "具材を予定時刻に投入できる");
                sim.Tick(.5f);
            }
            Assert.IsTrue(sim.Drain(order.id), "茹で終わった注文を湯切りできる");
            for (int i = 0; i < 3; i++) sim.Shake(order.id);
            var soup = Content.Soup(order.soup);
            Assert.IsTrue(sim.PourSoup(order.id, order.soup, (soup.IdealMinMl + soup.IdealMaxMl) / 2), "注文どおりのスープを注げる");
            for (int i = 0; i < order.spice; i++) sim.AddCondiment(order.id, Content.ChiliOil);
            Assert.IsTrue(sim.Finish(order.id), "調理した注文を仕上げられる");
            if (dissatisfied) order.serviceBonus = -100;
            Assert.IsTrue(sim.Serve(order.id), "会計済みの持ち帰り注文を提供できる");
        }

        /// <summary>前受け返金や値引きを二重控除せず、全現金移動が台帳に一致する。</summary>
        private static void AssertCashLedger(RestaurantSimulation sim, long openingMoney, string step)
        {
            var r = sim.State.report;
            long expected = openingMoney + r.revenueFen + r.tipsFen + r.missionRewardsFen + r.prizeFen
                - r.purchasesFen - r.fixedCostFen - r.wagesFen - r.investmentFen - r.changeLossFen + r.supportFen;
            Assert.AreEqual(expected, sim.State.moneyFen, "現金増減は値引き・返金後売上と全収支に一致する：" + step);
        }

        /// <summary>固定乱数で過不足のおつり・取消・提供・離脱を混ぜても、各操作後と閉店後に現金台帳が一致する。</summary>
        [TestCase(173)]
        [TestCase(941)]
        public void MixedRegisterFlowsPreserveCashLedgerAfterEveryOperationAndClosing(int seed)
        {
            var sim = Prepared();
            sim.State.shopLevel = 4;
            sim.State.moneyFen = 27000;
            long openingMoney = sim.State.moneyFen;
            Assert.IsTrue(sim.HireStaff(StaffRole.Cashier), "レジ係の採用費を投資として計上できる");
            sim.State.staff[0].busySeconds = float.MaxValue;
            Assert.IsTrue(sim.Purchase("bok_choy", 1), "仕入れ代金を計上できる");
            AssertCashLedger(sim, openingMoney, "営業準備");
            Assert.IsTrue(sim.BeginDay(), "準備後に開店できる");
            sim.State.spawnSeconds = float.MaxValue;
            sim.State.missions.Add(new Mission { id = "ledger", kind = "serve", target = 2, rewardFen = 100, rewardXp = 1 });
            var random = new System.Random(seed);
            for (int i = 0; i < 24; i++)
            {
                var order = Waiting(sim, random.Next(2) == 0 ? OrderStyle.Ordered : OrderStyle.SelfServe);
                PrepareCash(sim, order);
                long change = RestaurantSimulation.ExpectedChange(order);
                Assert.IsFalse(sim.Pay(order.id, change - 10), "1角不足の会計では金銭を動かさない");
                AssertCashLedger(sim, openingMoney, i + "：おつり不足");
                if (i % 4 == 0)
                {
                    Assert.IsTrue(sim.Cancel(order.id), "会計前の注文を取り消せる");
                }
                else
                {
                    Assert.IsTrue(sim.Pay(order.id, change + random.Next(1, 6) * 10), "渡しすぎ分を損失に記録して会計できる");
                    AssertCashLedger(sim, openingMoney, i + "：会計");
                    if (i % 4 == 1) Assert.IsTrue(sim.Cancel(order.id), "会計済み注文を返金して取り消せる");
                    else if (i % 4 == 2)
                    {
                        order.patience = 1;
                        sim.Tick(2.1f);
                        Assert.AreEqual(OrderStage.Cancelled, order.stage, "調理前に忍耐の2倍を超えると返金して帰る");
                    }
                    else ServePaid(sim, order, i % 8 == 7);
                }
                AssertCashLedger(sim, openingMoney, i + "：最終状態");
            }
            Assert.Greater(sim.State.report.tipsFen, 0, "良い提供ではチップ収入を検証する");
            Assert.Greater(sim.State.report.discountsFen, 0, "悪い接客では売上からの値引きを検証する");
            Assert.Greater(sim.State.report.refundsFen, 0, "取消と離脱では前受け返金を検証する");
            Assert.Greater(sim.State.report.missionRewardsFen, 0, "提供達成による報酬を検証する");
            sim.State.daySeconds = RestaurantSimulation.DayDuration - .5f;
            sim.Tick(1);
            Assert.AreEqual(DayPhase.Report, sim.State.phase, "全注文が終了すれば閉店精算する");
            Assert.Greater(sim.State.report.wagesFen, 0, "雇ったスタッフの人件費を計上する");
            AssertCashLedger(sim, openingMoney, "閉店固定費と人件費");
            Assert.IsTrue(sim.RequestSupport(), "赤字営業後に再建支援を受けられる");
            AssertCashLedger(sim, openingMoney, "再建支援");
            sim.State.dailyEvent = "contest";
            sim.State.eventTriggered = true;
            for (int i = 0; i < 3; i++)
            {
                var judge = Waiting(sim, archetype: "judge");
                judge.stage = OrderStage.Served;
                judge.quality = 90;
            }
            Contest.Conclude(sim);
            Assert.AreEqual(80000, sim.State.report.prizeFen, "審査員の平均90点で金賞賞金を計上する");
            AssertCashLedger(sim, openingMoney, "賞金確定");
        }

        /// <summary>接客中は通常待機の0.35倍だけ経過し、忍耐ちょうどでは残り、超過したときだけ無料で帰る。</summary>
        [Test]
        public void OrderingPatienceHasReducedRateAndStrictUnpaidTimeoutBoundary()
        {
            var sim = Open();
            var ordering = Waiting(sim);
            var waiting = Waiting(sim);
            ordering.patience = 3.5f;
            Assert.IsTrue(sim.TakeOrder(ordering.id), "接客を開始できる");
            long money = sim.State.moneyFen;
            sim.Tick(10);
            Assert.AreEqual(3.5f, ordering.elapsed, .00001f, "接客中は経過時間が0.35倍になる");
            Assert.AreEqual(10f, waiting.elapsed, .00001f, "未接客の待機は実時間で経過する");
            Assert.AreEqual(OrderStage.Ordering, ordering.stage, "忍耐と等しい時点では帰らない");
            sim.Tick(.1f);
            Assert.AreEqual(OrderStage.Cancelled, ordering.stage, "忍耐を超えた時点で帰る");
            Assert.AreEqual(0, ordering.paidFen, "接客中の離脱では代金を受け取っていない");
            Assert.AreEqual(0, sim.State.report.refundsFen, "未会計の離脱では返金を計上しない");
            Assert.AreEqual(money, sim.State.moneyFen, "未会計の離脱で所持金を動かさない");
            Assert.IsNull(sim.PlayerFocusOrderId, "帰った客へのフォーカスを解除する");
        }

        /// <summary>接客途中でも未フォーカスならスタッフが会計を引き継ぎ、店主のおつり実績に加算しない。</summary>
        [Test]
        public void CashierTakesOverUnfocusedOrderingWithoutPlayerExactChangeCredit()
        {
            var sim = Open();
            var order = Waiting(sim);
            order.billFen = 1000;
            for (int i = 0; i < 128; i++)
            {
                order.id = "staff-cash-" + i;
                sim.Customers.PreparePayment(order);
                if (order.payment == PaymentMethod.Cash) break;
            }
            Assert.AreEqual(PaymentMethod.Cash, order.payment, "決定的に現金払いを選ぶ注文IDを用意する");
            PrepareCash(sim, order, false);
            sim.State.staff.Add(new HiredStaff { role = StaffRole.Cashier, trainingLevel = 1 });
            sim.StaffWork.Tick(.01f);
            Assert.AreEqual(order.id, sim.State.staff[0].taskTarget, "接客途中の未フォーカス客を作業対象にする");
            sim.StaffWork.Tick(StaffSystem.TaskSeconds(sim.State.staff[0]));
            Assert.AreEqual(OrderStage.Weighed, order.stage, "レジ係は接客を引き継いで具材と計量まで終える");
            Assert.Greater(order.paidFen, 0, "レジ係が代金を受け取る");
            Assert.AreEqual(PaymentMethod.Cash, order.payment, "スタッフが実際に現金払いを精算した条件で実績を検証する");
            Assert.AreEqual(0, sim.State.report.exactChange, "スタッフ会計を日報の店主実績に数えない");
            Assert.AreEqual(0, sim.State.stats.exactChange, "スタッフ会計を累計の店主実績に数えない");
        }

        /// <summary>スタッフが作業予約した接客をプレイヤーが開くと、予約を解除し会計を横取りしない。</summary>
        [Test]
        public void PlayerFocusReleasesPendingCashierTaskAndBlocksReassignment()
        {
            var sim = Open();
            var order = Waiting(sim);
            Assert.IsTrue(sim.TakeOrder(order.id, false), "スタッフに任せる接客を開始できる");
            var member = new HiredStaff { role = StaffRole.Cashier, trainingLevel = 1 };
            sim.State.staff.Add(member);
            sim.StaffWork.Tick(.01f);
            Assert.AreEqual(order.id, member.taskTarget, "レジ係が客を予約する");
            Assert.IsTrue(sim.TakeOrder(order.id), "プレイヤーが接客途中から引き継げる");
            Assert.AreEqual("", member.taskTarget, "引継ぎ時にスタッフ予約を解除する");
            sim.StaffWork.Tick(100);
            sim.StaffWork.Tick(100);
            Assert.AreEqual(OrderStage.Ordering, order.stage, "フォーカス中の客にはスタッフが手を出さない");
            Assert.AreEqual(0, order.paidFen, "プレイヤーが会計する前に代金を受け取らない");
        }

        /// <summary>未選択の注文式でも接客開始時に要求個数を予約し、本人には確保在庫を使用可能として返す。</summary>
        [Test]
        public void OrderingReservesDuplicateRequestsAndTrimsCompetingOrdersWithoutDoubleSelling()
        {
            var sim = Open();
            sim.StockFor("bok_choy").quantity = 2;
            sim.StockFor("enoki").quantity = 1;
            var first = Waiting(sim, OrderStyle.Ordered, "office", "bok_choy", "bok_choy");
            var second = Waiting(sim, OrderStyle.Ordered, "office", "bok_choy", "enoki");
            Assert.IsTrue(sim.TakeOrder(first.id), "最初の客の注文を聞ける");
            Assert.IsEmpty(first.selected, "注文式は接客中にまだ具材を取り出していない");
            Assert.AreEqual(2, sim.Reserved("bok_choy"), "重複要求を個数どおり予約する");
            Assert.AreEqual(0, sim.Available("bok_choy"), "他の客には予約済み在庫を売らない");
            Assert.AreEqual(2, sim.Available("bok_choy", first), "本人の在庫照会では自分の予約を除外する");
            Assert.IsTrue(sim.TakeOrder(second.id), "残る具材だけなら二番目の注文も受けられる");
            CollectionAssert.AreEqual(new[] { "enoki" }, second.requested, "他の客が予約した具材を要求から外す");
            Assert.AreEqual(-3, second.serviceBonus, "在庫不足の接客減点は1回だけ");
            Assert.IsTrue(sim.Cancel(first.id), "最初の注文を取り消せる");
            Assert.AreEqual(2, sim.Available("bok_choy"), "未消費の取消で予約在庫を全て解放する");
        }

        /// <summary>予約競合で全具材がなくなると受付を拒否し、請求も選択リストの残骸も残さない。</summary>
        [TestCase(OrderStyle.Ordered)]
        [TestCase(OrderStyle.SelfServe)]
        public void CompletelyUnavailableOrderIsCancelledBeforeAnyPayment(OrderStyle style)
        {
            var sim = Open();
            sim.StockFor("bok_choy").quantity = 1;
            var owner = Waiting(sim, OrderStyle.Ordered, "office", "bok_choy");
            Assert.IsTrue(sim.TakeOrder(owner.id), "最後の1個を先客が確保できる");
            var rejected = Waiting(sim, style, "office", "bok_choy");
            long money = sim.State.moneyFen;
            Assert.IsFalse(sim.TakeOrder(rejected.id), "全具材不足の注文を受付しない");
            Assert.AreEqual(OrderStage.Cancelled, rejected.stage, "全具材不足の客を取消状態にする");
            Assert.IsEmpty(rejected.requested, "不足した要求具材を全て外す");
            Assert.IsEmpty(rejected.selected, "セルフ式の不足具材も全て外す");
            Assert.IsEmpty(rejected.dropTimes, "選択具材と投入時刻の対応を崩さない");
            Assert.AreEqual(0, rejected.paidFen, "取消前に請求しない");
            Assert.AreEqual(money, sim.State.moneyFen, "受付拒否では所持金を動かさない");
        }

        /// <summary>ゼロ・硬貨紙幣の境界・高額を含む1角単位の請求で、同一乱数列なら現金提示額も一致する。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TenderIsDeterministicAtDenominationBoundariesAndForEveryJiaoUpToOneHundredYuan(bool practice)
        {
            var a = new System.Random(523);
            var b = new System.Random(523);
            var bills = Enumerable.Range(0, 1001).Select(i => i * 10L)
                .Concat(new[] { 10010L, 19990L, 20000L, 20010L, 999990L, 1000000L });
            foreach (long bill in bills)
            {
                long tender = CustomerDirector.Tender(bill, a, practice);
                Assert.AreEqual(tender, CustomerDirector.Tender(bill, b, practice), "同一seedと請求列で提示額が再現する：" + bill);
                Assert.GreaterOrEqual(tender, bill, "提示額は請求を下回らない：" + bill);
                Assert.AreEqual(0, tender % 10, "提示額は1角単位になる：" + bill);
                if (practice) Assert.Greater(tender, bill, "練習は紙幣境界を含め必ずおつりを必要とする：" + bill);
            }
        }

        /// <summary>初日の客タイプにかかわらず現金払いを用意し、同じ注文IDなら支払い方法と金額が再現する。</summary>
        [Test]
        public void PreparePaymentIsDeterministicForEveryArchetypeAndPracticeForcesCash()
        {
            var sim = Open();
            foreach (var archetype in Content.Archetypes)
            {
                var order = new CustomerOrder { id = "payment-" + archetype.Id, archetype = archetype.Id, billFen = 5000 };
                sim.Customers.PreparePayment(order);
                var method = order.payment;
                long tender = order.tenderedFen;
                sim.Customers.PreparePayment(order);
                Assert.AreEqual(method, order.payment, "同一IDなら支払い方法が再現する：" + archetype.Id);
                Assert.AreEqual(tender, order.tenderedFen, "同一IDなら提示金額が再現する：" + archetype.Id);
                sim.State.tutorialComplete = false;
                sim.Customers.PreparePayment(order);
                Assert.AreEqual(PaymentMethod.Cash, order.payment, "練習は全客タイプで現金払いになる：" + archetype.Id);
                Assert.AreEqual(10000, order.tenderedFen, "50元ちょうどの請求でも練習用に100元を提示する：" + archetype.Id);
                sim.State.tutorialComplete = true;
            }
        }

        /// <summary>聞き間違いは訂正回数ごとに3点下がり、訂正後の確定や再確定でボーナスを水増ししない。</summary>
        [TestCase(1)]
        [TestCase(3)]
        public void RepeatedTicketMistakesDeductExactlyThreeEachAndCorrectConfirmationIsIdempotent(int mistakes)
        {
            var sim = Open();
            var order = Waiting(sim);
            order.usual = true;
            Assert.IsTrue(sim.TakeOrder(order.id), "常連の接客を開始できる");
            for (int i = 0; i < mistakes; i++)
                Assert.IsFalse(sim.ConfirmTicket(order.id, "tomato", 5, !order.takeout), "複数項目の誤りでも1訂正として記録する");
            Assert.AreEqual(mistakes, order.registerMistakes, "訂正回数を重複項目数ではなく呼出回数で数える");
            Assert.AreEqual(-3 * mistakes, order.serviceBonus, "訂正1回につき3点だけ減点する");
            Assert.IsTrue(sim.ConfirmTicket(order.id, order.soup, order.spice, order.takeout), "訂正後は確定できる");
            Assert.AreEqual(-3 * mistakes, order.serviceBonus, "誤り履歴があれば常連の正答ボーナスを加えない");
            Assert.IsTrue(sim.ConfirmTicket(order.id, order.soup, order.spice, order.takeout), "確定済み注文票の再確認は成功する");
            Assert.AreEqual(-3 * mistakes, order.serviceBonus, "再確定しても減点も加点も増やさない");
        }

        /// <summary>常連の記憶だけの正答は5点、ノート参照や普通の注文は2点を一度だけ加点する。</summary>
        [TestCase(true, false, 5)]
        [TestCase(true, true, 2)]
        [TestCase(false, false, 2)]
        public void UsualTicketBonusRequiresHintFreeFirstTryAndIsAwardedOnlyOnce(bool usual, bool hint, int bonus)
        {
            var sim = Open();
            var order = Waiting(sim);
            order.usual = usual;
            Assert.IsTrue(sim.TakeOrder(order.id), "接客を開始できる");
            Assert.IsTrue(sim.ConfirmTicket(order.id, order.soup, order.spice, order.takeout, hint), "初回正答を確定できる");
            Assert.AreEqual(bonus, order.serviceBonus, "常連とヒント利用に応じた加点になる");
            Assert.AreEqual(hint, order.usedHint, "確定時のヒント利用を記録する");
            sim.ConfirmTicket(order.id, order.soup, order.spice, order.takeout, !hint);
            Assert.AreEqual(bonus, order.serviceBonus, "再確定で加点しない");
            Assert.AreEqual(hint, order.usedHint, "再確定でヒント履歴を書き換えない");
        }

        /// <summary>評論家の高満足度はチップを生まず、審査員の低満足度は値引きを生まない。</summary>
        [TestCase("critic", 100)]
        [TestCase("judge", 0)]
        public void SpecialCustomerSettlementRespectsNoTipAndNoComplaintPolicies(string archetype, int quality)
        {
            var sim = Open();
            var order = Waiting(sim, archetype: archetype);
            order.quality = quality;
            order.paidFen = 5000;
            long money = sim.State.moneyFen;
            Satisfaction.Settle(sim, order, Content.Archetype(archetype));
            Assert.AreEqual(0, order.tipFen, "評論家と審査員のチップなし設定を精算まで守る");
            Assert.AreEqual(0, order.discountFen, "今回の特別客の条件では値引きしない");
            Assert.AreEqual(money, sim.State.moneyFen, "チップも値引きもない精算では現金を動かさない");
            Assert.AreEqual(0, sim.State.report.complaints, "審査員の低満足度を値引き苦情に数えない");
        }

        /// <summary>大満足を12回続けるとコンボ通知は5回目と10回目だけで、最大記録を更新する。</summary>
        [Test]
        public void ComboEventsOccurOnlyAtEveryFifthSatisfiedCustomer()
        {
            var sim = Open();
            var milestones = new List<long>();
            sim.Event += e => { if (e.Kind == SimEventKind.Combo) milestones.Add(e.Value); };
            for (int i = 0; i < 12; i++)
            {
                var order = Waiting(sim);
                order.quality = 100;
                Satisfaction.Settle(sim, order, Content.Archetype(order.archetype));
            }
            CollectionAssert.AreEqual(new long[] { 5, 10 }, milestones, "コンボイベントは5の倍数だけ通知する");
            Assert.AreEqual(12, sim.State.combo, "大満足の連続回数を全て記録する");
            Assert.AreEqual(12, sim.State.report.bestCombo, "日報の最長コンボを更新する");
            Assert.AreEqual(12, sim.State.stats.bestCombo, "累計の最長コンボを更新する");
        }

        /// <summary>満足度70ちょうどならコンボを維持し、それを下回ると切れ、85ちょうどで増える。</summary>
        [TestCase(69, 0)]
        [TestCase(70, 4)]
        [TestCase(84, 4)]
        [TestCase(85, 5)]
        public void SettlementComboThresholdsIncludeOnlyTheirSpecifiedBoundary(int quality, int expectedCombo)
        {
            var sim = Open();
            sim.State.furniture.Clear();
            sim.State.combo = 4;
            var order = Waiting(sim);
            order.quality = quality;
            Satisfaction.Settle(sim, order, Content.Archetype(order.archetype));
            Assert.AreEqual(quality, order.report.satisfaction, .001f, "雰囲気も接客補正もない状態は品質と満足度が一致する");
            Assert.AreEqual(expectedCombo, sim.State.combo, "70と85の境界に従ってコンボを更新する");
        }

        /// <summary>練習日の再生成は既存ミッションも消し、名物がなければ何日生成しても名物販売ミッションを出さない。</summary>
        [Test]
        public void MissionGenerationClearsPracticeDayAndExcludesSignaturesWithoutDishes()
        {
            var sim = Open();
            sim.State.missions.Add(new Mission { kind = "signature" });
            sim.State.tutorialComplete = false;
            Missions.Generate(sim);
            Assert.IsEmpty(sim.State.missions, "練習日には既存分も含めミッションを残さない");
            sim.State.tutorialComplete = true;
            sim.State.signatures.Clear();
            for (int day = 1; day <= 120; day++)
            {
                sim.State.day = day;
                Missions.Generate(sim);
                Assert.AreEqual(3, sim.State.missions.Count, "通常日には3件生成する：" + day);
                Assert.IsFalse(sim.State.missions.Any(m => m.kind == "signature"), "名物未登録時は名物販売を要求しない：" + day);
                Assert.IsFalse(sim.State.missions.Any(m => m.kind == "spicy"), "レベル1では高辛度ミッションを出さない：" + day);
            }
        }

        /// <summary>全ての既知ミッション種類に表示用の説明がある。</summary>
        [TestCase("serve")]
        [TestCase("excellent")]
        [TestCase("tips")]
        [TestCase("exact")]
        [TestCase("combo")]
        [TestCase("archetype")]
        [TestCase("signature")]
        [TestCase("spicy")]
        [TestCase("revenue")]
        [TestCase("noComplaint")]
        public void EveryMissionKindHasNonEmptyHumanReadableDescription(string kind)
        {
            string description = Missions.Describe(new Mission { kind = kind, target = 2, param = "office" });
            Assert.IsFalse(string.IsNullOrWhiteSpace(description), "全種類に空でない説明を用意する：" + kind);
            Assert.AreNotEqual(kind, description, "既知種類を内部識別子のまま表示しない：" + kind);
        }

        /// <summary>閉店で待機中の審査員を取消し、帰った審査員は残存品質にかかわらず0点として結果を一度だけ確定する。</summary>
        [Test]
        public void ClosingConcludesContestWithUnservedJudgesScoredZeroAndEmitsResultOnce()
        {
            var sim = Open();
            sim.State.dailyEvent = "contest";
            sim.State.eventTriggered = true;
            var first = Waiting(sim, archetype: "judge");
            first.stage = OrderStage.Served;
            first.quality = 100;
            first.visualState = "leave";
            var second = Waiting(sim, archetype: "judge");
            second.quality = 100;
            Assert.IsTrue(sim.TakeOrder(second.id), "二人目の審査員は会計前まで接客する");
            var third = Waiting(sim, archetype: "judge");
            third.quality = 100;
            var results = new List<SimEvent>();
            sim.Event += e => { if (e.Kind == SimEventKind.ContestResult) results.Add(e); };
            Contest.Conclude(sim);
            Assert.IsFalse(sim.State.report.contestConcluded, "未確定の審査員がいる営業中は結果を出さない");
            sim.State.daySeconds = RestaurantSimulation.DayDuration - .5f;
            sim.Tick(1);
            Assert.AreEqual(DayPhase.Report, sim.State.phase, "会計前の審査員を帰して閉店できる");
            Assert.AreEqual(OrderStage.Cancelled, second.stage, "接客中だった審査員を取消す");
            Assert.AreEqual(OrderStage.Cancelled, third.stage, "未接客の審査員を取消す");
            Assert.IsTrue(sim.State.report.contestConcluded, "帰った審査員がいても結果を確定する");
            Assert.AreEqual(1, results.Count, "結果イベントを1回だけ発行する");
            Assert.AreEqual("参加賞", results[0].Id, "平均100点ではなく100/3点として参加賞にする");
            Assert.AreEqual(0, sim.State.report.prizeFen, "帰った審査員の仮品質で賞金を出さない");
            long money = sim.State.moneyFen;
            int highlights = sim.State.report.highlights.Count;
            Contest.Conclude(sim);
            sim.Tick(1);
            Assert.AreEqual(1, results.Count, "結果確定後の再判定でもイベントを増やさない");
            Assert.AreEqual(money, sim.State.moneyFen, "再判定や閉店後Tickで二重精算しない");
            Assert.AreEqual(highlights, sim.State.report.highlights.Count, "再判定で結果記録を増やさない");
        }

        /// <summary>v2の実JSONから新フィールド欠損を補完し、注文と資産を失わずv3として検証可能にする。</summary>
        [Test]
        public void VersionTwoEnvelopeWithoutNewFieldsMigratesAndValidatesWithoutLosingState()
        {
            const string payload = "{\"saveVersion\":2,\"storeId\":\"legacy-v2\",\"day\":9,\"phase\":0,\"moneyFen\":123450,"
                + "\"pricePer100gFen\":1800,\"pots\":1,\"storageCapacity\":30,\"tutorialComplete\":true,\"tutorialStep\":13,"
                + "\"ingredients\":{\"settledRevenueFen\":78900,\"unlockedIds\":[\"enoki\"],\"settledOrderIds\":[]},"
                + "\"stock\":[{\"ingredientId\":\"enoki\",\"quantity\":4,\"ageDays\":1}],"
                + "\"orders\":[{\"id\":\"old-order\",\"stage\":1,\"soup\":\"mala\",\"selected\":[\"enoki\"],\"paidFen\":1000}],"
                + "\"report\":{\"revenueFen\":1000},\"stats\":{\"tipsFen\":500}}";
            string folder = Path.Combine(Path.GetTempPath(), "malatang-register-v2-" + Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "save.json");
                string checksum;
                using (var sha = SHA256.Create()) checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
                File.WriteAllText(file, JsonUtility.ToJson(new LegacyEnvelope { version = 2, payload = payload, checksum = checksum }));
                var repository = new SaveRepository(file);
                var loaded = repository.Load();
                Assert.IsNotNull(loaded, "チェックサム付きv2データを読める");
                Assert.IsTrue(string.IsNullOrEmpty(repository.LastError), "読込失敗時のフォールバックではなくv2本体を移行する");
                Assert.AreEqual(GameState.CurrentVersion, loaded.saveVersion, "最新形式に更新する");
                Assert.AreEqual("legacy-v2", loaded.storeId, "店舗IDを保持する");
                Assert.AreEqual(9, loaded.day, "営業日を保持する");
                Assert.AreEqual(123450, loaded.moneyFen, "v2移行で資産を変えない");
                Assert.AreEqual(78900, loaded.ingredients.settledRevenueFen, "解禁済み売上を保持する");
                Assert.IsNotNull(loaded.missions, "欠損ミッションリストを補完する");
                Assert.IsEmpty(loaded.missions, "移行で架空の達成済みミッションを作らない");
                Assert.IsNotNull(loaded.report.servedArchetypes, "日報の新しい客タイプ記録を補完する");
                Assert.IsNotNull(loaded.stats.archetypesServed, "累計の新しい客タイプ記録を補完する");
                var order = loaded.orders.Single();
                Assert.AreEqual("old-order", order.id, "旧注文を保持する");
                Assert.AreEqual(1000, order.paidFen, "旧前受け金額を保持する");
                CollectionAssert.AreEqual(new[] { -1f }, order.dropTimes, "欠損投入時刻を選択具材数まで補完する");
                Assert.AreEqual(0, order.registerMistakes, "新しい誤り回数は0で開始する");
                Assert.IsFalse(order.ticketConfirmed, "旧データで注文票を勝手に確定しない");
                Assert.DoesNotThrow(() => SaveRepository.Validate(loaded), "補完したv3データが正式な検証を通る");
                repository.Save(loaded);
                var reloaded = repository.Load();
                Assert.AreEqual(123450, reloaded.moneyFen, "v3として再保存しても資産を保持する");
                Assert.AreEqual("old-order", reloaded.orders.Single().id, "v3再読込で注文を保持する");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        /// <summary>旧保存形式の検証対象を、製品実装と同じEnvelope構造で表す。</summary>
        [Serializable]
        private sealed class LegacyEnvelope
        {
            public int version;
            public string payload;
            public string checksum;
        }

        /// <summary>半角単位の境界を、符号にかかわらずゼロから遠ざかる方向へ丸める。</summary>
        [TestCase(-15d, -20L)]
        [TestCase(-5d, -10L)]
        [TestCase(-4.999d, 0L)]
        [TestCase(0d, 0L)]
        [TestCase(4.999d, 0L)]
        [TestCase(5d, 10L)]
        [TestCase(15d, 20L)]
        public void RoundToJiaoUsesAwayFromZeroAtExactHalfJiao(double fen, long expected)
        {
            Assert.AreEqual(expected, RestaurantSimulation.RoundToJiao(fen), "半角ちょうどを銀行丸めせず、符号に従って四捨五入する");
        }

        /// <summary>おつりは現金の差額だけを返し、請求未満の提示や電子払いでは負額を返さない。</summary>
        [TestCase(PaymentMethod.Cash, 990L, 0L)]
        [TestCase(PaymentMethod.Cash, 1000L, 0L)]
        [TestCase(PaymentMethod.Cash, 1010L, 10L)]
        [TestCase(PaymentMethod.Card, 2000L, 0L)]
        [TestCase(PaymentMethod.QrCode, 2000L, 0L)]
        public void ExpectedChangeNeverReturnsNegativeAndIgnoresDigitalTender(PaymentMethod payment, long tender, long expected)
        {
            var order = new CustomerOrder { payment = payment, tenderedFen = tender, billFen = 1000 };
            Assert.AreEqual(expected, RestaurantSimulation.ExpectedChange(order), "現金のみ差額を返し、負のおつりを作らない");
        }

        /// <summary>不正な負のおつりや注文式へのレジ計量要求は、会計状態と評価を変えず拒否する。</summary>
        [Test]
        public void NegativeChangeAndOrderedRegisterWeighingAreRejectedWithoutSideEffects()
        {
            var sim = Open();
            var order = Waiting(sim);
            PrepareCash(sim, order);
            long money = sim.State.moneyFen;
            float bonus = order.serviceBonus;
            Assert.IsFalse(sim.Pay(order.id, -10), "負のおつりを受け付けない");
            Assert.IsFalse(sim.WeighAtRegister(order.id), "注文式のボウルをレジで量れない");
            Assert.AreEqual(money, sim.State.moneyFen, "不正操作で所持金を変えない");
            Assert.AreEqual(0, order.paidFen, "不正操作で代金を受け取らない");
            Assert.AreEqual(bonus, order.serviceBonus, "不正入力を客の訂正として減点しない");
            Assert.AreEqual(0, order.registerMistakes, "不正入力で訂正回数を増やさない");
            Assert.AreEqual(OrderStage.Ordering, order.stage, "不正操作で接客段階を進めない");
        }

        /// <summary>毎卓1席ずつ空いていても、カップルを別卓へ分割せず、同じ卓が2席空いてから同時に着席する。</summary>
        [Test]
        public void PairNeverSplitsAcrossTablesWhenOnlyOneSeatPerTableIsFree()
        {
            var sim = Open();
            foreach (var seat in DiningLayout.Seats(sim.State).Where(s => s.Index != 0).ToArray())
                sim.State.orders.Add(new CustomerOrder
                {
                    id = "block-seat-" + seat.SeatId, stage = OrderStage.Ready,
                    seat = seat.SeatId, takeout = false, patience = 9999
                });
            var first = Waiting(sim, archetype: "couple");
            var second = Waiting(sim, archetype: "couple");
            first.takeout = second.takeout = false;
            first.groupId = second.groupId = "pair-edge";
            Assert.IsTrue(sim.Accept(first.id), "一人目を会計できる");
            Assert.IsTrue(sim.Accept(second.id), "二人目を会計できる");
            sim.Tick(.1f);
            Assert.Less(first.seat, 0, "卓ごとの空きが1席なら一人目を別卓へ座らせない");
            Assert.Less(second.seat, 0, "卓ごとの空きが1席なら二人目も待機させる");
            var blocker = sim.State.orders.First(o => o.id.StartsWith("block-seat-", StringComparison.Ordinal));
            int table = DiningLayout.TableOfSeat(blocker.seat);
            Assert.IsTrue(sim.Cancel(blocker.id), "同じ卓の残り1席を解放できる");
            sim.Tick(.1f);
            Assert.GreaterOrEqual(first.seat, 0, "2席空けば一人目を着席させる");
            Assert.GreaterOrEqual(second.seat, 0, "2席空けば二人目を同時に着席させる");
            Assert.AreNotEqual(first.seat, second.seat, "同じ席を二人に割り当てない");
            Assert.AreEqual(table, DiningLayout.TableOfSeat(first.seat), "解放された卓に一人目が座る");
            Assert.AreEqual(table, DiningLayout.TableOfSeat(second.seat), "解放された同じ卓に二人目が座る");
        }

        /// <summary>食べられないカテゴリだけが在庫にあると、代替食材のない客を生成しない。</summary>
        [Test]
        public void ForbiddenOnlyStockCannotGenerateAnInedibleCustomerOrder()
        {
            var sim = Open();
            foreach (var stock in sim.State.stock) stock.quantity = 0;
            foreach (var archetype in Content.Archetypes.Where(a => a.ForbiddenCategories.Length > 0))
            {
                var ingredient = sim.Catalog.ingredients.First(i => archetype.ForbiddenCategories.Contains(i.category));
                if (!sim.State.ingredients.unlockedIds.Contains(ingredient.id))
                    sim.State.ingredients.unlockedIds.Add(ingredient.id);
                sim.StockFor(ingredient.id).quantity = 1;
                Assert.IsNull(sim.Customers.Create(archetype.Id), "禁止カテゴリだけの在庫なら注文を生成しない：" + archetype.Id);
                sim.StockFor(ingredient.id).quantity = 0;
            }
        }

        /// <summary>二週目以降の週次大会と日替わり抽選は、同じ店舗・日付で天気や発生秒まで再現する。</summary>
        [Test]
        public void DailyRollIsDeterministicAcrossSeasonsAndForcesEveryWeeklyContestFromDayFourteen()
        {
            var a = Open();
            var b = Open();
            for (int day = 1; day <= 112; day++)
            {
                a.State.day = b.State.day = day;
                a.State.eventTriggered = b.State.eventTriggered = true;
                DailyEvents.Roll(a);
                DailyEvents.Roll(b);
                Assert.AreEqual(a.State.weather, b.State.weather, "同じ日付で天気が再現する：" + day);
                Assert.AreEqual(a.State.dailyEvent, b.State.dailyEvent, "同じ日付でイベントが再現する：" + day);
                Assert.AreEqual(a.State.eventSeconds, b.State.eventSeconds, "同じ日付でイベント時刻が再現する：" + day);
                Assert.IsFalse(a.State.eventTriggered, "抽選時にイベント未発生へ戻す：" + day);
                if (day >= 14 && day % 7 == 0)
                    Assert.AreEqual("contest", a.State.dailyEvent, "二週目以降は7日おきに必ず大会を開く：" + day);
            }
        }

        /// <summary>星の閾値ちょうどでは昇格し、閾値直前では一つ下の格付けに留まる。</summary>
        [Test]
        public void StarsPromoteAtEveryExactRankThresholdButNotImmediatelyBelow()
        {
            foreach (var rank in Content.Ranks.Where(r => r.Stars > 1))
            {
                var state = new GameState { shopLevel = 15, popularity = 0, reputation = (rank.MinPoints - 30) / .6f };
                Assert.AreEqual(rank.Stars, Progression.Stars(state), "格付け閾値ちょうどで昇格する：" + rank.Stars);
                state.reputation -= .01f;
                Assert.AreEqual(rank.Stars - 1, Progression.Stars(state), "格付け閾値の直前では昇格しない：" + rank.Stars);
            }
        }

        /// <summary>家具の購入と売却も純投資として台帳へ反映され、所持金増減の不変条件から漏れない。</summary>
        [Test]
        public void FurnitureResaleProceedsAreIncludedInTheCashLedger()
        {
            var sim = Prepared();
            long openingMoney = sim.State.moneyFen;
            Assert.IsTrue(sim.BuyFurniture("poster_menu", 7, 0, 0), "壁の空きスロットへ家具を買える");
            AssertCashLedger(sim, openingMoney, "家具購入");
            var poster = sim.State.furniture.Single(f => f.defId == "poster_menu" && f.x == 7);
            Assert.IsTrue(sim.SellFurniture(poster.uid), "購入した壁家具を売却できる");
            Assert.AreEqual(openingMoney - 3000, sim.State.moneyFen, "6000分の家具を半額売却すると純支出は3000分になる");
            AssertCashLedger(sim, openingMoney, "家具売却の戻り金");
        }

        /// <summary>全客タイプ・全支払い方法・全反応分岐で台詞が空にならず、決定的に生成できる。</summary>
        [Test]
        public void EveryArchetypeHasNonEmptyDeterministicVoiceAcrossPaymentAndReactionBranches()
        {
            var sim = Open();
            foreach (var archetype in Content.Archetypes)
            {
                var order = Waiting(sim, archetype: archetype.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(CustomerVoice.Greeting(order)), "挨拶を生成できる：" + archetype.Id);
                Assert.AreEqual(CustomerVoice.Greeting(order), CustomerVoice.Greeting(order), "同じ客の挨拶が再現する：" + archetype.Id);
                foreach (bool usual in new[] { false, true })
                {
                    order.usual = usual;
                    var lines = CustomerVoice.OrderLines(order, sim.Catalog);
                    Assert.IsNotEmpty(lines, "通常・いつもの両方に注文台詞がある：" + archetype.Id);
                    Assert.IsTrue(lines.All(line => !string.IsNullOrWhiteSpace(line)), "注文台詞の各行が空でない：" + archetype.Id);
                }
                foreach (PaymentMethod method in Enum.GetValues(typeof(PaymentMethod)))
                {
                    order.payment = method;
                    order.billFen = 1200;
                    order.tenderedFen = method == PaymentMethod.Cash ? 2000 : 0;
                    Assert.IsFalse(string.IsNullOrWhiteSpace(CustomerVoice.PaymentLine(order)), "各支払い方法に台詞がある：" + archetype.Id + "/" + method);
                }
                foreach (var branch in new[] { (100f, 100L, 0L), (80f, 0L, 0L), (50f, 0L, 100L), (30f, 0L, 100L), (30f, 0L, 0L) })
                {
                    order.report.satisfaction = branch.Item1;
                    string reaction = CustomerVoice.Reaction(order, branch.Item2, branch.Item3);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(reaction), "チップ・値引き・通常反応が空でない：" + archetype.Id);
                    Assert.AreEqual(reaction, CustomerVoice.Reaction(order, branch.Item2, branch.Item3), "同じ精算条件の反応が再現する：" + archetype.Id);
                }
            }
        }
    }
}
