using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>配置・品質・スタッフ・成長・客・経済・不正操作の未検証の境界条件を保証する。</summary>
    public sealed class CoreEdgeCaseTests
    {
        /// <summary>日付と注文番号が固定された、練習済みのシミュレーションを作る。</summary>
        private static RestaurantSimulation CreateSimulation()
        {
            RestaurantSimulation sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            sim.State.day = 1;
            sim.State.nextOrderNumber = 1;
            return sim;
        }

        /// <summary>既知の在庫だけを使う注文を登録し、他の客の発生に依存しない検証を可能にする。</summary>
        private static CustomerOrder AddWaitingOrder(RestaurantSimulation sim, OrderStyle style)
        {
            CustomerOrder order = new CustomerOrder
            {
                id = "edge-order", customerName = "境界テストさん", style = style,
                stage = OrderStage.Waiting, patience = 999, takeout = true, soup = "mala", spice = 1
            };
            order.requested.AddRange(new[] { "bok_choy", "enoki" });
            sim.State.orders.Add(order);
            return order;
        }

        [Test]
        public void RotatedFurnitureOccupiesSwappedWidthAndDepthCells()
        {
            PlacedFurniture item = new PlacedFurniture { uid = 1, defId = "table_counter3", x = 2, z = 0 };
            CollectionAssert.AreEquivalent(new[] { (2, 0), (3, 0), (4, 0) }, DiningLayout.Cells(item).ToArray(),
                "回転前のカウンター席は横方向に3セルを占有する");
            item.rotation = 1;
            CollectionAssert.AreEquivalent(new[] { (2, 0), (2, 1), (2, 2) }, DiningLayout.Cells(item).ToArray(),
                "90度回転すると幅と奥行が交換され、縦方向に3セルを占有する");
            item.rotation = 2;
            CollectionAssert.AreEquivalent(new[] { (2, 0), (3, 0), (4, 0) }, DiningLayout.Cells(item).ToArray(),
                "偶数回の90度回転では元の占有サイズに戻る");
        }

        [Test]
        public void RotatedFurniturePlacementChecksTheRotatedBoundaryAndOverlap()
        {
            GameState state = new GameState();
            Assert.IsTrue(DiningLayout.CanPlace(state, "table_counter3", 11, 0, 1, -1, out string validReason),
                "回転して幅1セルになれば右端の列に収まる：" + validReason);
            Assert.IsFalse(DiningLayout.CanPlace(state, "table_counter3", 11, 0, 0, -1, out string boundaryReason),
                "未回転の幅3セルでは右端からはみ出す");
            StringAssert.Contains("はみ出", boundaryReason, "配置失敗の理由はフロア境界である");
            state.furniture.Add(new PlacedFurniture { uid = 1, defId = "plant", x = 2, z = 2 });
            Assert.IsFalse(DiningLayout.CanPlace(state, "table_counter3", 2, 0, 1, -1, out string overlapReason),
                "回転後の奥行3セル目も重なり判定に含める");
            StringAssert.Contains("重な", overlapReason, "回転した占有セルと既存家具との衝突を報告する");
        }

        [Test]
        public void WallDecorationsRejectDuplicateAndOutOfRangeSlotsButAllowTheirOwnSlot()
        {
            GameState state = new GameState();
            state.furniture.Add(new PlacedFurniture { uid = 7, defId = "poster_menu", x = 0 });
            Assert.IsFalse(DiningLayout.CanPlace(state, "calligraphy", 0, 0, 0, -1, out string duplicateReason),
                "種類が違っても同じ壁スロットに二重配置できない");
            StringAssert.Contains("すでに", duplicateReason, "壁スロットの重複が失敗理由になる");
            foreach (int slot in new[] { -1, DiningLayout.WallSlots })
            {
                Assert.IsFalse(DiningLayout.CanPlace(state, "calligraphy", slot, 0, 0, -1, out string reason),
                    "壁スロットは0から7までに限定される：" + slot);
                StringAssert.Contains("飾り場所", reason, "壁スロット範囲外の理由を返す");
            }
            Assert.IsTrue(DiningLayout.CanPlace(state, "poster_menu", 0, 0, 0, 7, out _),
                "移動対象自身のuidを除外すれば元の壁スロットに置ける");
            Assert.IsTrue(DiningLayout.CanPlace(state, "calligraphy", DiningLayout.WallSlots - 1, 0, 0, -1, out _),
                "最後の有効な壁スロット7は利用できる");
            Assert.IsEmpty(DiningLayout.Cells(state.furniture[0]).ToArray(), "壁飾りは床セルを占有しない");
        }

        [Test]
        public void ExpansionAddsTwoRowsPerLevelAndMakesTheNewLastRowPlaceable()
        {
            GameState state = new GameState();
            Assert.AreEqual(4, DiningLayout.Rows(state), "未拡張のフロアは4行ある");
            Assert.IsFalse(DiningLayout.CanPlace(state, "plant", 0, 4, 0, -1, out _), "未拡張では行4に置けない");
            state.SetEquipmentLevel("expansion", 2);
            Assert.AreEqual(6, DiningLayout.Rows(state), "拡張レベル2では2行増える");
            Assert.IsTrue(DiningLayout.CanPlace(state, "plant", 0, 4, 0, -1, out _), "拡張後は以前の範囲外の行4に置ける");
            Assert.IsTrue(DiningLayout.CanPlace(state, "plant", 0, 5, 0, -1, out _), "拡張レベル2の最終行5に置ける");
            Assert.IsFalse(DiningLayout.CanPlace(state, "plant", 0, 6, 0, -1, out _), "最終行の次の行6は依然として範囲外である");
            state.SetEquipmentLevel("expansion", 3);
            Assert.AreEqual(8, DiningLayout.Rows(state), "最大拡張では8行になる");
            Assert.IsTrue(DiningLayout.CanPlace(state, "plant", 0, 7, 0, -1, out _), "最大拡張の最終行7を利用できる");
            CollectionAssert.AreEquivalent(new[] { (5, 7), (6, 7) }, DiningLayout.EntranceCells(state).ToArray(),
                "拡張に合わせて入口も新しい最終行へ移る");
        }

        [Test]
        public void FreeSeatsExcludeEverySeatOfDirtyTablesAndOnlyCurrentlyOccupiedSeats()
        {
            GameState state = new GameState();
            state.furniture.Add(new PlacedFurniture { uid = 1, defId = "table_round2" });
            state.furniture.Add(new PlacedFurniture { uid = 2, defId = "table_round2", x = 8 });
            state.dirtyTables.Add(1);
            state.orders.Add(new CustomerOrder { id = "occupied", stage = OrderStage.Served, seat = 20, visualState = "eat" });
            state.orders.Add(new CustomerOrder { id = "departed", stage = OrderStage.Served, seat = 21, visualState = "leave" });
            state.orders.Add(new CustomerOrder { id = "cancelled", stage = OrderStage.Cancelled, seat = 21 });
            CollectionAssert.AreEqual(new[] { 21 }, DiningLayout.FreeSeats(state).Select(seat => seat.SeatId).ToArray(),
                "汚れた卓の全席と食事中の席を除き、退店済み・取消済みの席だけを空席に戻す");
            state.dirtyTables.Clear();
            CollectionAssert.AreEqual(new[] { 10, 11, 21 }, DiningLayout.FreeSeats(state).Select(seat => seat.SeatId).ToArray(),
                "卓を片付けると、その卓の2席がまとめて利用可能になる");
        }

        [Test]
        public void AmbienceLosesExactlyTwoPointsPerDirtyTableAndRecoversAfterCleaning()
        {
            RestaurantSimulation sim = CreateSimulation();
            int cleanAmbience = DiningLayout.Ambience(sim.State);
            int[] tables = sim.State.furniture.Where(item => Content.FurnitureById(item.defId).Kind == FurnitureKind.Table)
                .Take(2).Select(item => item.uid).ToArray();
            sim.State.dirtyTables.Add(tables[0]);
            Assert.AreEqual(cleanAmbience - 2, DiningLayout.Ambience(sim.State), "汚れた卓1つにつき雰囲気が2点下がる");
            sim.State.dirtyTables.Add(tables[1]);
            Assert.AreEqual(cleanAmbience - 4, DiningLayout.Ambience(sim.State), "汚れた卓2つでは4点下がる");
            Assert.IsTrue(sim.ClearTable(tables[0]), "汚れた卓を片付けられる");
            Assert.AreEqual(cleanAmbience - 2, DiningLayout.Ambience(sim.State), "片付けた卓の2点だけが回復する");
        }

        [Test]
        public void DonenessScoreIncludesBothPerfectBoundariesAndPenalizesJustOutsideThem()
        {
            Assert.AreEqual(100f, QualityEvaluator.DonenessScore(.85f), "理想下限0.85は満点に含まれる");
            Assert.AreEqual(100f, QualityEvaluator.DonenessScore(1.25f), "理想上限1.25は満点に含まれる");
            Assert.AreEqual(97.8f, QualityEvaluator.DonenessScore(.84f), .001f, "下限より0.01生なら220倍の係数で2.2点減点する");
            Assert.AreEqual(98.6f, QualityEvaluator.DonenessScore(1.26f), .001f, "上限より0.01煮すぎなら140倍の係数で1.4点減点する");
        }

        [Test]
        public void DonenessScoreNeverFallsBelowZeroForRawNegativeOrOvercookedRatios()
        {
            foreach (float ratio in new[] { -10f, 0f, 10f, float.MinValue, float.MaxValue })
                Assert.AreEqual(0f, QualityEvaluator.DonenessScore(ratio), "極端な茹で割合でも点数は0で下げ止まる：" + ratio);
        }

        [Test]
        public void DrainScoreCoversEveryReachableShakeCountAndClampsItsOuterBranches()
        {
            float[] expected = { 35f, 65f, 88f, 100f, 100f, 90f, 78f, 78f, 78f, 78f };
            for (int shakes = 0; shakes < expected.Length; shakes++)
                Assert.AreEqual(expected[shakes], QualityEvaluator.DrainScore(shakes), "湯切り0〜9回の各点数を保証する：" + shakes + "回");
            Assert.AreEqual(35f, QualityEvaluator.DrainScore(-1), "負の回数は湯切りなしと同じ35点になる");
            Assert.AreEqual(78f, QualityEvaluator.DrainScore(int.MaxValue), "9回を超える回数も最後の減点区分78点になる");
        }

        [Test]
        public void DrySoupScoresPerfectlyFromZeroToFortyMlAndPenalizesExcess()
        {
            CustomerOrder order = new CustomerOrder { soup = "dry", chosenSoup = "dry" };
            foreach (float ml in new[] { 0f, 20f, 40f })
            {
                order.soupMl = ml;
                Assert.AreEqual(100f, QualityEvaluator.SoupScore(order), "汁なしスープは0〜40mlの両端を含めて満点になる：" + ml);
            }
            order.soupMl = 41;
            Assert.AreEqual(99.65f, QualityEvaluator.SoupScore(order), .001f, "汁なしの上限を1ml超えると0.35点減点する");
            order.soupMl = 600;
            Assert.AreEqual(0f, QualityEvaluator.SoupScore(order), "汁なしに大量のスープを注いでも点数は負にならない");
        }

        [Test]
        public void UnrequestedSoupMultipliesTheAmountScoreByPointFortyFive()
        {
            CustomerOrder order = new CustomerOrder { soup = "baitang", chosenSoup = "baitang", soupMl = 340 };
            Assert.AreEqual(100f, QualityEvaluator.SoupScore(order), "注文どおりの理想量は満点になる");
            order.chosenSoup = "mala";
            Assert.AreEqual(45f, QualityEvaluator.SoupScore(order), "頼んでいないスープは理想量でも0.45倍になる");
            order.soupMl = 280;
            Assert.AreEqual(40.95f, QualityEvaluator.SoupScore(order), .001f, "量不足20mlの91点にも0.45倍を適用する");
            order.chosenSoup = "";
            Assert.AreEqual(0f, QualityEvaluator.SoupScore(order), "未選択のスープは0点になる");
        }

        [Test]
        public void StrictCustomersLowerTheSameImperfectBowlScoreWithoutChangingComponents()
        {
            IngredientCatalog catalog = IngredientCatalog.Load();
            CustomerOrder order = new CustomerOrder
            {
                soup = "mala", chosenSoup = "mala", soupMl = 340, spice = 1, chosenSpice = 1,
                shakes = 2, patience = 200, heatSeconds = catalog.Get("bok_choy").cookingSeconds
            };
            order.requested.Add("bok_choy"); order.selected.Add("bok_choy"); order.dropTimes.Add(0);
            QualityReport normal = QualityEvaluator.Evaluate(order, catalog, _ => 100f);
            QualityReport strict = QualityEvaluator.Evaluate(order, catalog, _ => 100f, Content.Archetype("critic").Strictness);
            Assert.AreEqual(99.04f, normal.total, .001f, "湯切り88点だけが減点され、通常客の総合点は99.04点になる");
            Assert.AreEqual(98.464f, strict.total, .001f, "評論家は満点との差を1.6倍にし、98.464点になる");
            Assert.Less(strict.total, normal.total, "同じ不完全な料理でも厳しい客の総合点は下がる");
            CollectionAssert.AreEqual(
                new[] { normal.accuracy, normal.doneness, normal.drain, normal.soup, normal.seasoning, normal.speed, normal.freshness },
                new[] { strict.accuracy, strict.doneness, strict.drain, strict.soup, strict.seasoning, strict.speed, strict.freshness },
                "厳しさは総合点だけに作用し、料理自体の各評価項目は変えない");
        }

        [Test]
        public void StaffTrainingShortensEveryRoleTaskByTwentyPercentOfBasePerLevel()
        {
            foreach (StaffRole role in Enum.GetValues(typeof(StaffRole)))
            {
                HiredStaff member = new HiredStaff { role = role };
                float baseline = Content.StaffFor(role).BaseTaskSeconds;
                member.trainingLevel = 1;
                Assert.AreEqual(baseline, StaffSystem.TaskSeconds(member), .001f, "研修レベル1は定義の基本時間になる：" + role);
                member.trainingLevel = 2;
                Assert.AreEqual(baseline * .8f, StaffSystem.TaskSeconds(member), .001f, "研修レベル2は基本時間の80%になる：" + role);
                member.trainingLevel = 3;
                Assert.AreEqual(baseline * .6f, StaffSystem.TaskSeconds(member), .001f, "研修レベル3は基本時間の60%になる：" + role);
            }
        }

        [Test]
        public void AutoDropInsertsLongestIngredientsFirstAndFinishesAllAtTheSameHeatTime()
        {
            RestaurantSimulation sim = CreateSimulation();
            CustomerOrder order = new CustomerOrder { id = "auto-drop", stage = OrderStage.Heating, handledByStaff = true };
            order.selected.AddRange(new[] { "bok_choy", "potato", "enoki", "mung_bean_noodle" });
            order.dropTimes.AddRange(new[] { -1f, -1f, -1f, -1f });
            float slowest = order.selected.Max(id => sim.Catalog.Get(id).cookingSeconds);
            float[] times = order.selected.Select(id => slowest - sim.Catalog.Get(id).cookingSeconds).Distinct().OrderBy(time => time).ToArray();
            sim.StaffWork.AutoDrop(order);
            for (int i = 0; i < order.selected.Count; i++)
                Assert.AreEqual(sim.Catalog.Get(order.selected[i]).cookingSeconds == slowest ? 0f : -1f, order.dropTimes[i],
                    "加熱開始時は最も長い具材だけを入れ、短い具材は待つ：" + order.selected[i]);
            foreach (float time in times.Where(time => time > 0))
            {
                order.heatSeconds = time - .001f;
                sim.StaffWork.AutoDrop(order);
                for (int i = 0; i < order.selected.Count; i++)
                    if (slowest - sim.Catalog.Get(order.selected[i]).cookingSeconds == time)
                        Assert.AreEqual(-1f, order.dropTimes[i], "予定時刻の直前にはまだ具材を投入しない：" + order.selected[i]);
                order.heatSeconds = time;
                sim.StaffWork.AutoDrop(order);
            }
            order.heatSeconds = slowest;
            for (int i = 0; i < order.selected.Count; i++)
            {
                Assert.AreEqual(slowest - sim.Catalog.Get(order.selected[i]).cookingSeconds, order.dropTimes[i], .001f,
                    "各具材は同時に仕上がるための差分時刻に投入する：" + order.selected[i]);
                Assert.AreEqual(1f, sim.Doneness(order, i), .001f, "最長時間の経過時には全具材がちょうど茹で上がる：" + order.selected[i]);
            }
        }

        [Test]
        public void AutoDropPreservesScheduledDropTimesAndSimultaneousDonenessWithACoarseTick()
        {
            RestaurantSimulation sim = CreateSimulation();
            CustomerOrder order = new CustomerOrder { id = "coarse-auto-drop", stage = OrderStage.Heating, handledByStaff = true };
            order.selected.AddRange(new[] { "bok_choy", "potato", "enoki", "mung_bean_noodle" });
            order.dropTimes.AddRange(new[] { -1f, -1f, -1f, -1f });
            float slowest = order.selected.Max(id => sim.Catalog.Get(id).cookingSeconds);
            sim.StaffWork.AutoDrop(order);
            order.heatSeconds = slowest;
            sim.StaffWork.AutoDrop(order);
            for (int i = 0; i < order.selected.Count; i++)
            {
                Assert.AreEqual(slowest - sim.Catalog.Get(order.selected[i]).cookingSeconds, order.dropTimes[i], .001f,
                    "途中の投入予定時刻を飛び越えても実行時刻ではなく予定時刻を記録する：" + order.selected[i]);
                Assert.AreEqual(1f, sim.Doneness(order, i), .001f,
                    "大きな時間刻みでも全具材は最長の茹で時間で同時に仕上がる：" + order.selected[i]);
            }
            float[] dropTimes = order.dropTimes.ToArray();
            order.heatSeconds = slowest + 1;
            sim.StaffWork.AutoDrop(order);
            CollectionAssert.AreEqual(dropTimes, order.dropTimes, "再呼び出しでも投入済み具材の予定時刻を書き換えない");
        }

        [Test]
        public void AutoDropLeavesPlayerOwnedAndNonHeatingOrdersUntouched()
        {
            RestaurantSimulation sim = CreateSimulation();
            CustomerOrder order = new CustomerOrder { stage = OrderStage.Heating };
            order.selected.Add("bok_choy"); order.dropTimes.Add(-1);
            sim.StaffWork.AutoDrop(order);
            Assert.AreEqual(-1f, order.dropTimes[0], "プレイヤーが調理する注文へは自動投入しない");
            order.handledByStaff = true; order.stage = OrderStage.Drained;
            sim.StaffWork.AutoDrop(order);
            Assert.AreEqual(-1f, order.dropTimes[0], "スタッフの注文でも茹で段階でなければ自動投入しない");
        }

        [Test]
        public void ServerClearsADirtyTableOnlyAfterItsTaskTimeHasElapsed()
        {
            RestaurantSimulation sim = CreateSimulation();
            int table = sim.State.furniture.First(item => Content.FurnitureById(item.defId).Kind == FurnitureKind.Table).uid;
            sim.State.dirtyTables.Add(table);
            HiredStaff member = new HiredStaff { role = StaffRole.Server, trainingLevel = 2 };
            sim.State.staff.Add(member);
            sim.StaffWork.Tick(.1f);
            Assert.AreEqual("clean", member.task, "ホール係は提供待ちの注文がなければ片付けを選ぶ");
            Assert.AreEqual(table.ToString(), member.taskTarget, "片付け対象は汚れたテーブルのuidになる");
            Assert.AreEqual(StaffSystem.TaskSeconds(member), member.busySeconds, .001f, "片付けには研修レベルに対応する作業時間を割り当てる");
            float taskSeconds = member.busySeconds;
            sim.StaffWork.Tick(taskSeconds / 2);
            CollectionAssert.Contains(sim.State.dirtyTables, table, "作業時間の途中では卓はまだ汚れている");
            sim.StaffWork.Tick(taskSeconds);
            Assert.IsEmpty(sim.State.dirtyTables, "作業完了時にホール係が汚れた卓を片付ける");
            Assert.IsTrue(DiningLayout.FreeSeats(sim.State).Any(seat => seat.Table.uid == table), "片付け後は卓の席を利用できる");
            Assert.AreEqual("", member.task, "片付け完了時に作業状態を解除する");
        }

        [Test]
        public void CashierAutomaticallyAcceptsAndWeighsSelfServeCustomersExactlyOnce()
        {
            RestaurantSimulation sim = CreateSimulation();
            Assert.IsTrue(sim.BeginDay(), "レジ係の検証を営業中に行う");
            CustomerOrder order = AddWaitingOrder(sim, OrderStyle.SelfServe);
            CustomerDirector.SelectAll(order);
            string[] selected = order.selected.ToArray();
            int[] stock = sim.State.stock.Select(item => item.quantity).ToArray();
            long money = sim.State.moneyFen;
            long expectedPrice = RestaurantSimulation.RoundToJiao(order.selected.Sum(id => sim.Catalog.Get(id).portionGrams) * sim.State.pricePer100gFen / 100d);
            HiredStaff member = new HiredStaff { role = StaffRole.Cashier };
            sim.State.staff.Add(member);
            sim.StaffWork.Tick(.1f);
            Assert.AreEqual("register", member.task, "レジ係は待機中のセルフ式注文の受付を選ぶ");
            Assert.AreEqual(order.id, member.taskTarget, "レジ係の対象は待機中の注文になる");
            sim.StaffWork.Tick(StaffSystem.TaskSeconds(member));
            Assert.AreEqual(OrderStage.Weighed, order.stage, "セルフ式の客はレジ係だけで受付から計量済みまで進む");
            Assert.AreEqual(expectedPrice, order.paidFen, "セルフ式の客にも具材の重量どおりの料金を請求する");
            Assert.AreEqual(money + expectedPrice, sim.State.moneyFen, "料金は現金へ一度だけ加算する");
            CollectionAssert.AreEqual(selected, order.selected, "セルフ式の客の選択済み具材を重複して追加しない");
            CollectionAssert.AreEqual(stock, sim.State.stock.Select(item => item.quantity).ToArray(), "計量時点では在庫を消費しない");
            sim.StaffWork.Tick(20);
            Assert.AreEqual(money + expectedPrice, sim.State.moneyFen, "計量済みの客を再受付して二重請求しない");
        }

        [Test]
        public void ExperienceThresholdsStrictlyIncreaseThroughTheMaximumLevel()
        {
            Assert.AreEqual(0, Content.ExperienceForLevel(1), "初期レベルに必要な経験値は0である");
            Assert.AreEqual(180, Content.ExperienceForLevel(2), "最初のレベルアップに必要な累計経験値は180である");
            Assert.AreEqual(13440, Content.ExperienceForLevel(Content.MaxShopLevel), "レベル15の必要累計経験値は13440である");
            for (int level = 2; level <= Content.MaxShopLevel; level++)
                Assert.Greater(Content.ExperienceForLevel(level), Content.ExperienceForLevel(level - 1),
                    "レベルが上がるたびに必要経験値が厳密に増える：" + level);
        }

        [Test]
        public void LevelProgressClampsBelowAndAboveThresholdsAndInterpolatesTheirMidpoint()
        {
            GameState state = new GameState();
            for (int level = 1; level < Content.MaxShopLevel; level++)
            {
                state.shopLevel = level;
                int from = Content.ExperienceForLevel(level);
                int to = Content.ExperienceForLevel(level + 1);
                int[] experience = { from - 1, from, (from + to) / 2, to, to + 1 };
                float[] expected = { 0f, 0f, .5f, 1f, 1f };
                for (int i = 0; i < experience.Length; i++)
                {
                    state.experience = experience[i];
                    Assert.AreEqual(expected[i], Progression.LevelProgress(state), .001f,
                        "進捗は下限未満で0、閾値の中間で0.5、次レベル以上で1になる：レベル" + level + " 経験値" + experience[i]);
                }
            }
            state.shopLevel = Content.MaxShopLevel; state.experience = 0;
            Assert.AreEqual(1f, Progression.LevelProgress(state), "最大レベルでは次レベルがないため進捗は常に1になる");
        }

        [Test]
        public void AddingExperienceStopsAtTheMaximumShopLevelAndIgnoresNegativeAwards()
        {
            RestaurantSimulation sim = CreateSimulation();
            Progression.AddExperience(sim, -1);
            Assert.AreEqual(0, sim.State.experience, "負の経験値付与では累計経験値を減らさない");
            Assert.AreEqual(0, sim.State.report.experience, "負の経験値付与は日報にも計上しない");
            int amount = Content.ExperienceForLevel(Content.MaxShopLevel) + 1000;
            Progression.AddExperience(sim, amount);
            Assert.AreEqual(Content.MaxShopLevel, sim.State.shopLevel, "大量の経験値を得ても最大レベル15で止まる");
            int highlights = sim.State.report.highlights.Count;
            Progression.AddExperience(sim, 1000);
            Assert.AreEqual(Content.MaxShopLevel, sim.State.shopLevel, "最大到達後もさらにレベルアップしない");
            Assert.AreEqual(amount + 1000, sim.State.experience, "最大レベルでも累計経験値は保持する");
            Assert.AreEqual(amount + 1000, sim.State.report.experience, "最大レベルでも付与した経験値は日報に残す");
            Assert.AreEqual(highlights, sim.State.report.highlights.Count, "最大到達後は新しいレベルアップ通知を追加しない");
        }

        [Test]
        public void UnlocksAtMatchesEverySoupCondimentFurnitureStaffAndEquipmentDefinition()
        {
            for (int level = 0; level <= Content.MaxShopLevel + 1; level++)
            {
                List<string> expected = new List<string>();
                foreach (SoupDefinition def in Content.Soups)
                    if (def.UnlockLevel == level) expected.Add(def.Name + "スープ");
                foreach (CondimentDefinition def in Content.Condiments)
                    if (def.UnlockLevel == level) expected.Add(def.Name);
                foreach (FurnitureDefinition def in Content.Furniture)
                    if (def.UnlockLevel == level) expected.Add(def.Name);
                foreach (StaffDefinition def in Content.Staff)
                    if (def.UnlockLevel == level) expected.Add(def.Title + "の採用");
                foreach (EquipmentDefinition def in Content.Equipment)
                    for (int targetLevel = 2; targetLevel <= def.MaxLevel; targetLevel++)
                        if (def.RequiredShopLevel[targetLevel - 1] == level) expected.Add(def.Name + "Lv" + targetLevel);
                CollectionAssert.AreEquivalent(expected, Progression.UnlocksAt(level),
                    "解禁一覧は各定義の解禁レベルと一致し、漏れや余分な内容がない：レベル" + level);
            }
        }

        [Test]
        public void AllAchievementIdsAreNonEmptyAndUnique()
        {
            Assert.IsNotEmpty(Content.Achievements, "実績定義が存在する");
            string[] ids = Content.Achievements.Select(def => def.Id).ToArray();
            Assert.IsTrue(ids.All(id => !string.IsNullOrWhiteSpace(id)), "全実績に空白でないIDを割り当てる");
            Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count(), "全実績IDは重複せず保存時に一意に識別できる");
        }

        [Test]
        public void FamilyCustomersAlwaysRequestZeroSpiceRegardlessOfWeather()
        {
            RestaurantSimulation sim = CreateSimulation();
            foreach (WeatherDefinition weather in Content.Weathers)
            {
                sim.State.weather = weather.Id;
                for (int number = 1; number <= 32; number++)
                {
                    sim.State.nextOrderNumber = number;
                    CustomerOrder order = sim.Customers.Create("family");
                    Assert.IsNotNull(order, "子ども連れの注文を生成できる");
                    Assert.AreEqual(0, order.spice, "子ども連れは寒波を含む全ての天気で辛さ0を要望する：" + weather.Id);
                    CollectionAssert.Contains(order.dislikedCondiments, Content.ChiliOil, "子ども連れはラー油を入れないように要望する");
                }
            }
        }

        [Test]
        public void ColdWeatherRaisesOfficeCustomerSpiceByOneWithTheSameDateAndOrderNumber()
        {
            RestaurantSimulation normal = CreateSimulation();
            RestaurantSimulation cold = CreateSimulation();
            normal.State.weather = "cloudy"; cold.State.weather = "cold";
            for (int number = 1; number <= 64; number++)
            {
                normal.State.nextOrderNumber = number; cold.State.nextOrderNumber = number;
                CustomerOrder normalOrder = normal.Customers.Create("office");
                CustomerOrder coldOrder = cold.Customers.Create("office");
                Assert.IsNotNull(normalOrder, "比較元の注文を生成できる");
                Assert.IsNotNull(coldOrder, "寒波側の注文を生成できる");
                Assert.AreEqual(normalOrder.id, coldOrder.id, "日付・店舗・注文番号を一致させて天気だけを比較する");
                Assert.AreEqual(normalOrder.spice + 1, coldOrder.spice, "同じ会社員客の辛さ要望は寒波の日に1段階上がる：" + number);
            }
        }

        /// <summary>初期在庫で必ず選べる好みを持つ常連を登録する。</summary>
        private static RegularCustomer AddRegular(RestaurantSimulation sim)
        {
            RegularCustomer regular = new RegularCustomer
            {
                id = "edge-regular", name = "境界常連", archetype = "office", appearanceSeed = 123,
                favoriteSoup = "tomato", favoriteSpice = 4
            };
            regular.favorites.AddRange(new[] { "bok_choy", "enoki" });
            regular.likedCondiments.Add("scallion"); regular.dislikedCondiments.Add("vinegar");
            sim.State.regulars.Add(regular);
            return regular;
        }

        [Test]
        public void RegularCustomersRetainTheirIdentitySpiceAndStockedFavoriteIngredients()
        {
            RestaurantSimulation sim = CreateSimulation();
            sim.State.weather = "cold";
            RegularCustomer regular = AddRegular(sim);
            int checkedRegulars = 0;
            for (int number = 1; number <= 256; number++)
            {
                sim.State.nextOrderNumber = number;
                CustomerOrder order = sim.Customers.Create();
                Assert.IsNotNull(order, "初期在庫から客を生成できる");
                if (order.regularId != regular.id) continue;
                checkedRegulars++;
                Assert.AreEqual(regular.name + "さん", order.customerName, "常連は登録済みの名前で来店する");
                Assert.AreEqual(regular.appearanceSeed, order.appearanceSeed, "常連の見た目の種は変わらない");
                Assert.AreEqual(regular.favoriteSpice, order.spice, "常連は寒波の日も覚えている好みの辛さを注文する");
                foreach (string ingredient in regular.favorites)
                    CollectionAssert.Contains(order.requested, ingredient, "在庫のある常連の好みの具材を優先して選ぶ：" + ingredient);
            }
            Assert.Greater(checkedRegulars, 0, "固定された256注文の列で実際に常連の注文を検証する");
        }

        [Test]
        public void RegularCustomerSoupAndCondimentPreferencesOverrideArchetypePreferences()
        {
            RestaurantSimulation sim = CreateSimulation();
            RegularCustomer regular = AddRegular(sim);
            int checkedRegulars = 0, favoriteSoups = 0, likedCondiments = 0;
            for (int number = 1; number <= 256; number++)
            {
                sim.State.nextOrderNumber = number;
                CustomerOrder order = sim.Customers.Create();
                Assert.IsNotNull(order, "好みを検証する注文を生成できる");
                if (order.regularId != regular.id) continue;
                checkedRegulars++;
                if (order.soup == regular.favoriteSoup) favoriteSoups++;
                if (order.requestedCondiments.Contains("scallion")) likedCondiments++;
                CollectionAssert.Contains(order.dislikedCondiments, "vinegar", "客タイプでは好物の黒酢も、常連が嫌いなら禁止として伝える");
                Assert.IsTrue(order.requestedCondiments.All(id => regular.likedCondiments.Contains(id)),
                    "欲しい薬味は客タイプではなく登録済みの常連の好みから選ぶ");
            }
            Assert.Greater(checkedRegulars, 0, "固定された256注文の列で常連が実際に選ばれる");
            Assert.Greater(favoriteSoups, checkedRegulars / 2, "固定注文列の常連は過半数で好みのトマトスープを選ぶ");
            Assert.Greater(likedCondiments, 0, "固定注文列に客タイプの好物でない常連の好みの青ねぎが反映される");
        }

        [Test]
        public void NormalCustomerDrawsNeverSpawnCriticsOrInfluencers()
        {
            RestaurantSimulation sim = CreateSimulation();
            Assert.AreEqual(0f, Content.Archetype("critic").SpawnWeight, "評論家の通常抽選の重みは0である");
            Assert.AreEqual(0f, Content.Archetype("influencer").SpawnWeight, "配信者の通常抽選の重みは0である");
            for (int number = 1; number <= 512; number++)
            {
                sim.State.nextOrderNumber = number;
                CustomerOrder order = sim.Customers.Create();
                Assert.IsNotNull(order, "通常抽選の客を生成できる");
                Assert.AreNotEqual("critic", order.archetype, "イベントを指定しない通常抽選では評論家を生成しない：" + number);
                Assert.AreNotEqual("influencer", order.archetype, "イベントを指定しない通常抽選では配信者を生成しない：" + number);
            }
            Assert.AreEqual("critic", sim.Customers.Create("critic").archetype, "評論家は明示的なイベント指定なら生成できる");
            Assert.AreEqual("influencer", sim.Customers.Create("influencer").archetype, "配信者は明示的なイベント指定なら生成できる");
        }

        [Test]
        public void RaisingPriceLowersDemandWhenNeitherBoundIsSaturated()
        {
            RestaurantSimulation sim = CreateSimulation();
            sim.State.weather = "cloudy";
            int original = sim.Demand;
            sim.ChangePrice(700);
            Assert.AreEqual(2500, sim.State.pricePer100gFen, "準備中に100g価格を1800分から2500分へ上げられる");
            Assert.Less(sim.Demand, original, "その他の条件を変えずに値上げすると来客見込みが下がる");
            Assert.AreEqual(original - 5, sim.Demand, "700分の値上げは来客見込みを5人下げる");
        }

        [Test]
        public void AddingSeatsRaisesDemandWithoutAnAmbienceChange()
        {
            RestaurantSimulation sim = CreateSimulation();
            sim.State.weather = "cloudy"; sim.State.shopLevel = 2; sim.State.moneyFen = 1000000;
            int demand = sim.Demand, seats = DiningLayout.SeatCount(sim.State), ambience = DiningLayout.Ambience(sim.State);
            Assert.IsTrue(sim.BuyFurniture("table_counter3", 4, 0, 0), "空き通路を保った配置に3席のカウンターを購入できる");
            Assert.AreEqual(seats + 3, DiningLayout.SeatCount(sim.State), "購入後は客席が3席増える");
            Assert.AreEqual(ambience, DiningLayout.Ambience(sim.State), "カウンター席自体に雰囲気の加点はない");
            Assert.Greater(sim.Demand, demand, "雰囲気・価格・レベルを変えず席だけを増やすと来客見込みが上がる");
        }

        [Test]
        public void DemandStaysBetweenFiveAndFortyEightAcrossValidEconomicExtremes()
        {
            RestaurantSimulation sim = CreateSimulation();
            foreach (WeatherDefinition weather in Content.Weathers)
            foreach (DailyEventDefinition dailyEvent in Content.DailyEvents)
            foreach (int level in new[] { 1, Content.MaxShopLevel })
            foreach (int price in new[] { 900, 1800, 3000 })
            foreach (float reputation in new[] { 0f, 50f, 100f })
            foreach (float popularity in new[] { 0f, 100f })
            {
                sim.State.weather = weather.Id; sim.State.dailyEvent = dailyEvent.Id;
                sim.State.shopLevel = level; sim.State.pricePer100gFen = price;
                sim.State.reputation = reputation; sim.State.popularity = popularity;
                Assert.That(sim.Demand, Is.InRange(5, 48), "天気・イベント・レベル・価格・評判・人気の端点の組合せでも需要は5〜48人に収まる");
            }
            sim.State.weather = "rain"; sim.State.dailyEvent = "none";
            sim.State.shopLevel = 1; sim.State.pricePer100gFen = 3000; sim.State.reputation = 0; sim.State.popularity = 0;
            Assert.AreEqual(5, sim.Demand, "低評判・高価格・雨でも最低5人の来客見込みを維持する");
            sim.State.weather = "cold"; sim.State.dailyEvent = "festival";
            sim.State.shopLevel = Content.MaxShopLevel; sim.State.pricePer100gFen = 900; sim.State.reputation = 100; sim.State.popularity = 100;
            Assert.AreEqual(48, sim.Demand, "最大評判・低価格・寒波・祭りでも来客見込みは48人で止まる");
        }

        [Test]
        public void FixedCostsIncreaseByOneThousandPerPotAndFiveThousandPerExpansionLevel()
        {
            RestaurantSimulation sim = CreateSimulation();
            Assert.AreEqual(18000L, sim.FixedCostFen, "初期の家賃と光熱費は合計18000分である");
            sim.State.SetEquipmentLevel("pot", 2);
            Assert.AreEqual(19000L, sim.FixedCostFen, "鍋を1個追加すると固定費が1000分増える");
            sim.State.SetEquipmentLevel("expansion", 2);
            Assert.AreEqual(24000L, sim.FixedCostFen, "1段階の客席拡張は固定費をさらに5000分増やす");
            sim.State.SetEquipmentLevel("pot", 4); sim.State.SetEquipmentLevel("expansion", 3);
            Assert.AreEqual(31000L, sim.FixedCostFen, "最大鍋数と最大拡張の固定費は18000＋3000＋10000分になる");
        }

        [Test]
        public void RequestSupportRejectsCashAtOrAboveTheEligibilityThresholdWithoutChangingAccounts()
        {
            RestaurantSimulation sim = CreateSimulation();
            long threshold = Math.Max(5000L, sim.MinimumPurchaseFen);
            foreach (DayPhase phase in new[] { DayPhase.Preparation, DayPhase.Report })
            foreach (long money in new[] { threshold, threshold + 1 })
            {
                sim.State.phase = phase; sim.State.moneyFen = money;
                Assert.IsFalse(sim.RequestSupport(), "支援基準額以上の現金がある店は営業前・閉店後とも対象外になる");
                Assert.AreEqual(money, sim.State.moneyFen, "対象外の支援申請では現金を変更しない");
                Assert.AreEqual(0L, sim.State.report.supportFen, "対象外の申請で支援額を計上しない");
                Assert.AreEqual(0L, sim.State.ingredients.settledRevenueFen, "対象外の申請で累計売上を変更しない");
            }
        }

        [Test]
        public void RequestSupportRejectsOpenAndClosingPhasesEvenWhenCashIsInsufficient()
        {
            RestaurantSimulation sim = CreateSimulation();
            foreach (DayPhase phase in new[] { DayPhase.Open, DayPhase.Closing })
            {
                sim.State.phase = phase; sim.State.moneyFen = -100;
                Assert.IsFalse(sim.RequestSupport(), "資金不足でも営業中・閉店処理中は支援申請できない：" + phase);
                Assert.AreEqual(-100L, sim.State.moneyFen, "支援申請できない時間帯では現金を変更しない");
                Assert.AreEqual(0L, sim.State.report.supportFen, "支援申請できない時間帯では支援額を計上しない");
            }
        }

        [Test]
        public void CancelledSelectingOrderRejectsCookingOperationsWithoutChangingCashStockOrStage()
        {
            RestaurantSimulation sim = CreateSimulation();
            Assert.IsTrue(sim.BeginDay(), "取消検証を営業中に行う");
            CustomerOrder order = AddWaitingOrder(sim, OrderStyle.Ordered);
            Assert.IsTrue(sim.Accept(order.id), "未会計の注文を受付できる");
            Assert.IsTrue(sim.AddIngredient(order.id, "bok_choy"), "取消前に具材を確保できる");
            Assert.IsTrue(sim.Cancel(order.id), "具材選択中の注文を取消できる");
            AssertCancelledOperationsLeaveStateUnchanged(sim, order);
        }

        [Test]
        public void CancelledFinishingOrderRejectsCookingOperationsWithoutReconsumingStockOrRefundingAgain()
        {
            RestaurantSimulation sim = CreateSimulation();
            Assert.IsTrue(sim.BeginDay(), "取消検証を営業中に行う");
            CustomerOrder order = AddWaitingOrder(sim, OrderStyle.Ordered);
            Assert.IsTrue(sim.Accept(order.id), "注文を受付できる");
            foreach (string id in order.requested) Assert.IsTrue(sim.AddIngredient(order.id, id), "注文の具材を追加できる：" + id);
            Assert.IsTrue(sim.Weigh(order.id), "会計できる");
            Assert.IsTrue(sim.StartHeating(order.id), "在庫を消費して茹で始められる");
            order.heatSeconds = order.selected.Max(id => sim.Catalog.Get(id).cookingSeconds);
            Assert.IsTrue(sim.Drain(order.id), "取消前に湯切り段階へ進める");
            Assert.IsTrue(sim.Shake(order.id), "取消前に網じゃくを振れる");
            Assert.IsTrue(sim.PourSoup(order.id, "mala", 340), "取消前にスープを注げる");
            Assert.IsTrue(sim.AddCondiment(order.id, Content.ChiliOil), "取消前に辛さを付けられる");
            Assert.IsTrue(sim.Cancel(order.id), "仕上げ中の会計済み注文を取消できる");
            AssertCancelledOperationsLeaveStateUnchanged(sim, order);
        }

        /// <summary>取消後に段階チェック付きの全調理操作を試し、各操作直後の状態の不変性を確認する。</summary>
        private static void AssertCancelledOperationsLeaveStateUnchanged(RestaurantSimulation sim, CustomerOrder order)
        {
            long money = sim.State.moneyFen, revenue = sim.State.report.revenueFen, refunds = sim.State.report.refundsFen;
            long foodCost = sim.State.report.foodCostFen, settled = sim.State.ingredients.settledRevenueFen;
            long paid = order.paidFen, consumed = order.consumedCostFen;
            int lost = sim.State.report.lost, cancelled = sim.State.stats.cancelled, served = sim.State.report.served;
            int spice = order.chosenSpice, shakes = order.shakes, experience = sim.State.experience;
            float ml = order.soupMl, heat = order.heatSeconds, quality = order.quality;
            bool stockConsumed = order.stockConsumed;
            string soup = order.chosenSoup;
            string[] stockIds = sim.State.stock.Select(item => item.ingredientId).ToArray();
            int[] quantities = sim.State.stock.Select(item => item.quantity).ToArray();
            string[] selected = order.selected.ToArray(), condiments = order.condiments.ToArray();
            float[] dropTimes = order.dropTimes.ToArray();
            List<(string name, Action action)> operations = new List<(string name, Action action)>
            {
                ("Accept", () => Assert.IsFalse(sim.Accept(order.id), "取消済み注文は受付できない")),
                ("AddIngredient", () => Assert.IsFalse(sim.AddIngredient(order.id, "bok_choy"), "取消済み注文に具材を足せない")),
                ("RemoveIngredient", () => sim.RemoveIngredient(order.id, "bok_choy")),
                ("Weigh", () => Assert.IsFalse(sim.Weigh(order.id), "取消済み注文は再会計できない")),
                ("StartHeating", () => Assert.IsFalse(sim.StartHeating(order.id), "取消済み注文は茹で始められない")),
                ("DropIngredient", () => Assert.IsFalse(sim.DropIngredient(order.id, 0), "取消済み注文には具材を投入できない")),
                ("DropRemaining", () => sim.DropRemaining(order.id)),
                ("Drain", () => Assert.IsFalse(sim.Drain(order.id), "取消済み注文は引き上げられない")),
                ("Shake", () => Assert.IsFalse(sim.Shake(order.id), "取消済み注文は湯切りできない")),
                ("PourSoup", () => Assert.IsFalse(sim.PourSoup(order.id, "baitang", 100), "取消済み注文にスープを注げない")),
                ("ChooseSoup", () => sim.ChooseSoup(order.id, "baitang")),
                ("ChooseSpice", () => sim.ChooseSpice(order.id, 5)),
                ("AddCondiment", () => Assert.IsFalse(sim.AddCondiment(order.id, Content.ChiliOil), "取消済み注文に調味料を足せない")),
                ("ResetSeasoning", () => sim.ResetSeasoning(order.id)),
                ("Finish", () => Assert.IsFalse(sim.Finish(order.id), "取消済み注文は完成させられない")),
                ("Plate", () => Assert.IsFalse(sim.Plate(order.id), "取消済み注文は互換APIでも盛り付けられない")),
                ("Serve", () => Assert.IsFalse(sim.Serve(order.id), "取消済み注文は提供できない")),
                ("RetryTutorial", () => sim.RetryTutorial(order.id)),
                ("RegisterSignature", () => Assert.IsFalse(sim.RegisterSignature(order.id, "取消済み"), "取消済み注文は名物にできない")),
                ("Cancel", () => Assert.IsFalse(sim.Cancel(order.id), "取消済み注文は再取消できない"))
            };
            foreach ((string name, Action action) operation in operations)
            {
                operation.action();
                string message = "取消済み注文への" + operation.name + "は状態を変えない";
                Assert.AreEqual(OrderStage.Cancelled, order.stage, message + "：注文段階");
                Assert.AreEqual(money, sim.State.moneyFen, message + "：現金");
                CollectionAssert.AreEqual(stockIds, sim.State.stock.Select(item => item.ingredientId).ToArray(), message + "：在庫ID");
                CollectionAssert.AreEqual(quantities, sim.State.stock.Select(item => item.quantity).ToArray(), message + "：在庫数");
                CollectionAssert.AreEqual(new[] { revenue, refunds, foodCost, settled, paid, consumed },
                    new[] { sim.State.report.revenueFen, sim.State.report.refundsFen, sim.State.report.foodCostFen,
                        sim.State.ingredients.settledRevenueFen, order.paidFen, order.consumedCostFen }, message + "：会計と原価");
                CollectionAssert.AreEqual(new[] { lost, cancelled, served, experience },
                    new[] { sim.State.report.lost, sim.State.stats.cancelled, sim.State.report.served, sim.State.experience }, message + "：営業統計と経験値");
                CollectionAssert.AreEqual(selected, order.selected, message + "：選択具材");
                CollectionAssert.AreEqual(dropTimes, order.dropTimes, message + "：投入時刻");
                CollectionAssert.AreEqual(condiments, order.condiments, message + "：薬味");
                Assert.AreEqual(spice, order.chosenSpice, message + "：辛さ");
                Assert.AreEqual(shakes, order.shakes, message + "：湯切り回数");
                Assert.AreEqual(soup, order.chosenSoup, message + "：スープ");
                Assert.AreEqual(ml, order.soupMl, message + "：スープ量");
                Assert.AreEqual(heat, order.heatSeconds, message + "：加熱時間");
                Assert.AreEqual(quality, order.quality, message + "：品質");
                Assert.AreEqual(stockConsumed, order.stockConsumed, message + "：在庫消費済み状態");
                Assert.AreEqual(-1, order.pot, message + "：鍋を再占有しない");
                Assert.IsEmpty(sim.State.signatures, message + "：名物を登録しない");
            }
        }

        [Test]
        public void ReportPhaseRejectsAcceptWithoutChangingWaitingOrderOrAccounts()
        {
            RestaurantSimulation sim = CreateSimulation();
            CustomerOrder order = AddWaitingOrder(sim, OrderStyle.SelfServe);
            sim.State.phase = DayPhase.Report;
            long money = sim.State.moneyFen;
            Assert.IsFalse(sim.Accept(order.id), "営業レポート中は待機中の注文でも受付できない");
            Assert.AreEqual(OrderStage.Waiting, order.stage, "拒否された注文を具材選択段階へ進めない");
            Assert.IsEmpty(order.selected, "受付拒否時にはセルフ式の具材選択も実行しない");
            Assert.IsEmpty(order.dropTimes, "受付拒否時には投入時刻のリストも作らない");
            Assert.AreEqual(money, sim.State.moneyFen, "受付拒否時には現金を変更しない");
            Assert.AreEqual(DayPhase.Report, sim.State.phase, "受付拒否時には営業段階を変更しない");
        }

        [Test]
        public void OpenAndClosingPhasesRejectPurchasesFurnitureAndHiringWithoutSpendingMoney()
        {
            RestaurantSimulation sim = CreateSimulation();
            sim.State.shopLevel = Content.MaxShopLevel; sim.State.moneyFen = 1000000;
            int[] quantities = sim.State.stock.Select(item => item.quantity).ToArray();
            int furnitureCount = sim.State.furniture.Count, nextUid = sim.State.nextFurnitureUid;
            foreach (DayPhase phase in new[] { DayPhase.Open, DayPhase.Closing })
            {
                sim.State.phase = phase;
                Assert.IsFalse(sim.Purchase("bok_choy", 5), "営業中・閉店処理中は仕入れできない：" + phase);
                Assert.IsFalse(sim.BuyFurniture("plant", 5, 0, 0), "資金・レベル・配置が有効でも営業中・閉店処理中は家具を買えない：" + phase);
                Assert.IsFalse(sim.HireStaff(StaffRole.Server), "採用条件が揃っていても営業中・閉店処理中は雇用できない：" + phase);
                Assert.AreEqual(1000000L, sim.State.moneyFen, "拒否された経営操作では現金を消費しない");
                Assert.AreEqual(0L, sim.State.report.purchasesFen, "拒否された仕入れを日報に計上しない");
                Assert.AreEqual(0L, sim.State.report.investmentFen, "拒否された家具・採用を投資額に計上しない");
                CollectionAssert.AreEqual(quantities, sim.State.stock.Select(item => item.quantity).ToArray(), "拒否された仕入れでは在庫を増やさない");
                Assert.AreEqual(furnitureCount, sim.State.furniture.Count, "拒否された家具購入では家具を増やさない");
                Assert.AreEqual(nextUid, sim.State.nextFurnitureUid, "拒否された家具購入ではuidを消費しない");
                Assert.IsEmpty(sim.State.staff, "拒否された採用ではスタッフを増やさない");
                Assert.AreEqual(phase, sim.State.phase, "拒否された経営操作で営業段階は変わらない");
            }
        }
    }
}
