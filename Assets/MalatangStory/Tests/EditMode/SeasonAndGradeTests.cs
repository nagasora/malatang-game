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
    /// <summary>v1.3の季節・旬・営業評価・保存互換・実績の動作を保証する。</summary>
    public class SeasonAndGradeTests
    {
        /// <summary>乱数系列を固定した検証用店舗を作る。</summary>
        private static RestaurantSimulation Create()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.storeId = "season-grade-fixed";
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            sim.State.weather = "sunny";
            sim.State.dailyEvent = "none";
            return sim;
        }

        /// <summary>全食材を解禁し、客生成で在庫不足にならないようにする。</summary>
        private static void StockAll(RestaurantSimulation sim)
        {
            sim.State.shopLevel = Content.MaxShopLevel;
            foreach (var item in sim.Catalog.ingredients)
            {
                if (!sim.Unlocks.IsUnlocked(item.id)) sim.State.ingredients.unlockedIds.Add(item.id);
                sim.StockFor(item.id).quantity = 10;
            }
        }

        /// <summary>評価に必要な提供済み注文と一致する日報を作る。</summary>
        private static GameState GradedState(int served = 12, float satisfaction = 95)
        {
            var s = new GameState { tutorialComplete = true, shopLevel = 2 };
            s.report.served = served;
            for (int i = 0; i < served; i++)
                s.orders.Add(new CustomerOrder { id = "grade-" + i, stage = OrderStage.Served, takeout = true,
                    report = new QualityReport { satisfaction = satisfaction } });
            return s;
        }

        /// <summary>既存テストと同じ形式で、提供直前の持ち帰り注文を追加する。</summary>
        private static CustomerOrder Ready(RestaurantSimulation sim, params string[] items)
        {
            var o = new CustomerOrder { id = "season-" + sim.State.orders.Count, customerName = "テストさん",
                archetype = "office", stage = OrderStage.Ready, style = OrderStyle.Ordered, takeout = true,
                soup = "mala", chosenSoup = "mala", spice = 2, chosenSpice = 2, quality = 80, patience = 999,
                report = new QualityReport { total = 80 } };
            o.requested.AddRange(items);
            CustomerDirector.SelectAll(o);
            sim.State.orders.Add(o);
            return o;
        }

        /// <summary>製品のチェックサム付き保存包絡を表す。</summary>
        [Serializable]
        private sealed class LegacyEnvelope
        {
            public int version;
            public string payload;
            public string checksum;
        }

        /// <summary>一時保存先を用意し、テストの成否によらず片付ける。</summary>
        private static void WithRepository(Action<SaveRepository, string> verify)
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-season-" + Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "save.json");
                verify(new SaveRepository(path), path);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        /// <summary>過去の形式の実JSONを正しいチェックサム付きで保存する。</summary>
        private static void WriteLegacy(string path, string payload, int version)
        {
            string checksum;
            using (var sha = SHA256.Create())
                checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            File.WriteAllText(path, JsonUtility.ToJson(new LegacyEnvelope { version = version, payload = payload, checksum = checksum }));
        }

        /// <summary>v1.3追加フィールドの存在しないv4保存を表す。</summary>
        private const string VersionFourPayload = "{\"saveVersion\":4,\"storeId\":\"legacy-season-v4\",\"day\":15,\"phase\":0,"
            + "\"moneyFen\":50000,\"pricePer100gFen\":1800,\"pots\":1,\"storageCapacity\":30,\"shopLevel\":2,"
            + "\"tutorialComplete\":true,\"tutorialStep\":13,\"ingredients\":{\"settledRevenueFen\":0,"
            + "\"unlockedIds\":[\"bok_choy\",\"enoki\"],\"settledOrderIds\":[]}}";

        /// <summary>季節・季節内日付・週の境界と二年目の循環が一致する。</summary>
        [TestCase(-1, 0, "spring", 1, 0)]
        [TestCase(0, 0, "spring", 1, 0)]
        [TestCase(1, 0, "spring", 1, 0)]
        [TestCase(7, 0, "spring", 7, 0)]
        [TestCase(8, 0, "spring", 8, 1)]
        [TestCase(14, 0, "spring", 14, 1)]
        [TestCase(15, 1, "summer", 1, 2)]
        [TestCase(28, 1, "summer", 14, 3)]
        [TestCase(29, 2, "autumn", 1, 4)]
        [TestCase(42, 2, "autumn", 14, 5)]
        [TestCase(43, 3, "winter", 1, 6)]
        [TestCase(56, 3, "winter", 14, 7)]
        [TestCase(57, 0, "spring", 1, 8)]
        [TestCase(112, 3, "winter", 14, 15)]
        [TestCase(113, 0, "spring", 1, 16)]
        public void SeasonAndWeekBoundariesMatch(int day, int index, string id, int withinSeason, int week)
        {
            Assert.AreEqual(index, Seasons.Index(day), "季節番号は14日単位で循環する");
            Assert.AreEqual(id, Seasons.Of(day).Id, "季節定義は季節番号と一致する");
            Assert.AreEqual(withinSeason, Seasons.DayOfSeason(day), "季節内の日は1から14になる");
            Assert.AreEqual(week, Seasons.Week(day), "週は7日単位の0始まりになる");
        }

        /// <summary>各季節では解禁済みの季節候補を優先し、同じ入力で同じ旬を選ぶ。</summary>
        [TestCase(1)]
        [TestCase(15)]
        [TestCase(29)]
        [TestCase(43)]
        public void PickFeaturedPrefersUnlockedSeasonalCandidates(int day)
        {
            var sim = Create();
            sim.State.day = day;
            sim.State.ingredients.unlockedIds.Clear();
            var candidates = Seasons.Of(day).Produce.Take(2).ToArray();
            sim.State.ingredients.unlockedIds.AddRange(candidates);
            sim.State.ingredients.unlockedIds.Add("bok_choy");
            string chosen = Seasons.PickFeatured(sim);
            CollectionAssert.Contains(candidates, chosen, "解禁済みの季節候補をフォールバックより優先する");
            Assert.AreEqual(chosen, Seasons.PickFeatured(sim), "固定店舗・固定週では抽選が再現する");
            sim.State.day = day + 6;
            Assert.AreEqual(chosen, Seasons.PickFeatured(sim), "候補が同じなら同じ週のどの日でも同じ旬になる");
        }

        /// <summary>季節候補が未解禁なら野菜またはきのこだけにフォールバックする。</summary>
        [TestCase("bok_choy")]
        [TestCase("enoki")]
        public void PickFeaturedFallsBackToUnlockedProduce(string fallback)
        {
            var sim = Create();
            sim.State.ingredients.unlockedIds.Clear();
            sim.State.ingredients.unlockedIds.AddRange(new[] { fallback, "beef", "mung_bean_noodle" });
            Assert.AreEqual(fallback, Seasons.PickFeatured(sim), "季節候補が無い時は解禁済み野菜・きのこを選ぶ");
        }

        /// <summary>候補も野菜・きのこも無ければ旬は空になる。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PickFeaturedReturnsEmptyWithoutEligibleProduce(bool meatOnly)
        {
            var sim = Create();
            sim.State.ingredients.unlockedIds.Clear();
            if (meatOnly) sim.State.ingredients.unlockedIds.Add("beef");
            Assert.AreEqual("", Seasons.PickFeatured(sim), "解禁済み食材が無いか対象外なら旬を作らない");
            sim.State.featuredIngredient = "";
            sim.State.featuredWeek = -1;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("", sim.State.featuredIngredient, "補完でも未解禁食材を選ばない");
            Assert.AreEqual(0, sim.State.featuredWeek, "旬が無い場合も判定した週を記録する");
        }

        /// <summary>週の途中の解禁では現在の旬を保持し、翌週には再抽選と週番号更新を行う。</summary>
        [Test]
        public void EnsureFeaturedPreservesCurrentWeekAndRepicksNextWeek()
        {
            var sim = Create();
            sim.State.ingredients.unlockedIds.Clear();
            sim.State.ingredients.unlockedIds.Add("bok_choy");
            sim.State.featuredIngredient = "";
            sim.State.featuredWeek = -1;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("bok_choy", sim.State.featuredIngredient, "初週はフォールバック食材になる");
            sim.State.ingredients.unlockedIds.Add("pea_shoot");
            sim.State.day = 7;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("bok_choy", sim.State.featuredIngredient, "同週の解禁で仕入れ価格や注文傾向を変えない");
            Assert.AreEqual(0, sim.State.featuredWeek, "週末でも週番号を保持する");
            sim.State.day = 8;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("pea_shoot", sim.State.featuredIngredient, "翌週には新たに解禁された季節候補を選ぶ");
            Assert.AreEqual(1, sim.State.featuredWeek, "再抽選した週番号を保存する");
        }

        /// <summary>旬が無いと判定した週は、週の途中で食材が解禁されても旬を作らない（翌週から選ぶ）。</summary>
        [Test]
        public void EmptyFeaturedWeekIsNotRepickedWhenProduceUnlocksMidWeek()
        {
            var sim = Create();
            sim.State.ingredients.unlockedIds.Clear();
            sim.State.day = 1; sim.State.featuredIngredient = ""; sim.State.featuredWeek = -1;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("", sim.State.featuredIngredient);
            sim.State.ingredients.unlockedIds.Add("pea_shoot");
            sim.State.day = 2;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("", sim.State.featuredIngredient, "同じ週の途中では旬を作らない");
            sim.State.day = 8;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("pea_shoot", sim.State.featuredIngredient, "翌週には解禁済みの季節の具材を旬にする");
        }

        /// <summary>保存 JSON の履歴に null があっても、読み込み後は有効な記録だけが残る。</summary>
        [Test]
        public void NullHistoryEntriesInSavedJsonAreDropped()
        {
            WithRepository((repo, path) =>
            {
                string payload = VersionFourPayload.Substring(0, VersionFourPayload.Length - 1)
                    + ",\"history\":[null,{\"day\":14,\"served\":6,\"satisfaction\":82,\"grade\":\"B\"},null]}";
                WriteLegacy(path, payload, 4);
                var loaded = repo.Load();
                Assert.IsNotNull(loaded);
                Assert.AreEqual(1, loaded.history.Count, "日付の無い壊れた記録は除く");
                Assert.AreEqual(14, loaded.history[0].day);
            });
        }

        /// <summary>旬の一致判定では空文字やnullを旬として扱わない。</summary>
        [TestCase("bok_choy", "bok_choy", true)]
        [TestCase("bok_choy", "enoki", false)]
        [TestCase("", "", false)]
        [TestCase(null, null, false)]
        public void IsFeaturedRequiresNonemptyExactMatch(string featured, string id, bool expected)
        {
            Assert.AreEqual(expected, Seasons.IsFeatured(new GameState { featuredIngredient = featured }, id), "有効な旬IDとの完全一致だけを認める");
        }

        /// <summary>旬の割引はセール・目利きと積算し、非旬に適用せず分単位で丸める。</summary>
        [TestCase("none", 0, true)]
        [TestCase("sale", 0, true)]
        [TestCase("none", 3, true)]
        [TestCase("sale", 3, true)]
        [TestCase("sale", 3, false)]
        [TestCase("none", 0, false)]
        public void PurchaseCostCombinesOnlyApplicableMultipliers(string dailyEvent, int bargainRank, bool featured)
        {
            var sim = Create();
            sim.State.dailyEvent = dailyEvent;
            sim.State.skills.Add(new NamedCount { id = Skills.Bargain, count = bargainRank });
            sim.State.featuredIngredient = featured ? "rounding-item" : "another-item";
            var item = new IngredientDefinition { id = "rounding-item", purchasePricePerPortion = 1.02f };
            float eventFactor = dailyEvent == "sale" ? .7f : 1f;
            float skillFactor = 1f - .05f * bargainRank;
            long expected = (long)Math.Round(102 * (eventFactor * skillFactor) * (featured ? .75f : 1f), MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, sim.PurchaseCost(item), "標準価格・セール・スキル・旬の積を最後に四捨五入する");
            if (dailyEvent == "none" && bargainRank == 0 && featured)
                Assert.AreEqual(77, sim.PurchaseCost(item), "76.5分は偶数丸めではなく77分になる");
        }

        /// <summary>旬の仕入れ支出・在庫数・平均取得原価が同じ取引価格に基づく。</summary>
        [TestCase(0, 0L)]
        [TestCase(4, 0L)]
        [TestCase(4, 211L)]
        public void FeaturedPurchaseUpdatesCashReportAndWeightedUnitCost(int oldQuantity, long oldUnitCost)
        {
            var sim = Create();
            sim.State.featuredIngredient = "bok_choy";
            sim.State.dailyEvent = "sale";
            sim.State.skills.Add(new NamedCount { id = Skills.Bargain, count = 2 });
            var stock = sim.StockFor("bok_choy");
            stock.quantity = oldQuantity;
            stock.unitCostFen = oldUnitCost;
            stock.ageDays = 2;
            long unit = sim.PurchaseCost(sim.Catalog.Get("bok_choy"));
            long prior = oldUnitCost > 0 ? oldUnitCost : RestaurantSimulation.Cost(sim.Catalog.Get("bok_choy"));
            long money = sim.State.moneyFen;
            const int count = 5;
            Assert.IsTrue(sim.Purchase("bok_choy", count), "旬を営業前に仕入れられる");
            Assert.AreEqual(money - unit * count, sim.State.moneyFen, "実際の支払額は割引後単価と個数の積になる");
            Assert.AreEqual(unit * count, sim.State.report.purchasesFen, "日報支出は財布の減少額と一致する");
            Assert.AreEqual(oldQuantity + count, stock.quantity, "仕入れ分が在庫へ増える");
            long expected = oldQuantity == 0 ? unit : (long)Math.Round((prior * (double)oldQuantity + unit * (double)count) / (oldQuantity + count));
            Assert.AreEqual(expected, stock.unitCostFen, "旧在庫と旬仕入れの加重平均原価を保存する");
            Assert.AreEqual(expected, sim.UnitCost("bok_choy"), "調理原価の参照値も平均取得原価と一致する");
            Assert.AreEqual(oldQuantity == 0 ? 0 : 2, stock.ageDays, "古い在庫がある場合は仕入れで若返らせない");
        }

        /// <summary>資金不足・不正数量・営業中の失敗で旬在庫や資金を変更しない。</summary>
        [TestCase("funds")]
        [TestCase("quantity")]
        [TestCase("open")]
        public void RejectedFeaturedPurchaseDoesNotMutateTransaction(string reason)
        {
            var sim = Create();
            sim.State.featuredIngredient = "bok_choy";
            var stock = sim.StockFor("bok_choy");
            stock.unitCostFen = 199;
            if (reason == "funds") sim.State.moneyFen = 0;
            if (reason == "open") sim.State.phase = DayPhase.Open;
            long money = sim.State.moneyFen;
            int quantity = stock.quantity;
            Assert.IsFalse(sim.Purchase("bok_choy", reason == "quantity" ? 0 : 5), "無効な仕入れ要求は拒否する");
            Assert.AreEqual(money, sim.State.moneyFen, "拒否時は支出しない");
            Assert.AreEqual(quantity, stock.quantity, "拒否時は在庫を増やさない");
            Assert.AreEqual(199, stock.unitCostFen, "拒否時は平均原価を変えない");
            Assert.AreEqual(0, sim.State.report.purchasesFen, "拒否時は日報支出を増やさない");
        }

        /// <summary>同じ日・店舗・注文番号系列で、旬ありの食材は旬なしより頼まれやすい。</summary>
        [Test]
        public void FeaturedIngredientHasHigherRequestRateWithFixedSeeds()
        {
            var plain = Create();
            StockAll(plain);
            plain.State.day = 15;
            plain.State.featuredIngredient = "";
            plain.State.nextOrderNumber = 1;
            var featured = new RestaurantSimulation(plain.Catalog, JsonUtility.FromJson<GameState>(JsonUtility.ToJson(plain.State)));
            featured.State.featuredIngredient = "bok_choy";
            int without = 0, with = 0;
            const int samples = 3000;
            for (int i = 0; i < samples; i++)
            {
                var a = plain.Customers.Create("office");
                var b = featured.Customers.Create("office");
                Assert.IsNotNull(a, "全在庫ありで対照群の客を生成できる");
                Assert.IsNotNull(b, "全在庫ありで旬群の客を生成できる");
                if (a.requested.Contains("bok_choy")) without++;
                if (b.requested.Contains("bok_choy")) with++;
            }
            Assert.Greater(with, without * 1.35f, "固定3000件で旬の注文出現数が十分に増える（旬=" + with + "、対照=" + without + "）");
        }

        /// <summary>冬の麻辣・夏のトマト・春の白湯・秋のきのこは他季節より多く選ばれる。</summary>
        [TestCase(43, 15, "mala")]
        [TestCase(15, 43, "tomato")]
        [TestCase(1, 29, "baitang")]
        [TestCase(29, 1, "mushroom")]
        public void SeasonalSoupPreferenceRaisesSelectionRate(int favoredDay, int controlDay, string soup)
        {
            var favored = Create();
            StockAll(favored);
            favored.State.day = favoredDay;
            favored.State.featuredIngredient = "";
            var control = Create();
            StockAll(control);
            control.State.day = controlDay;
            control.State.featuredIngredient = "";
            int favoredCount = 0, controlCount = 0;
            const int samples = 4000;
            for (int i = 0; i < samples; i++)
            {
                if (favored.Customers.Create("office").soup == soup) favoredCount++;
                if (control.Customers.Create("office").soup == soup) controlCount++;
            }
            Assert.Greater(favoredCount, controlCount * 1.08f, "天気・客タイプ・在庫を固定した4000件で季節のスープが増える（季節=" + favoredCount + "、対照=" + controlCount + "）");
        }

        /// <summary>旬入りの提供でのみ接客加点と累計旬杯数を増やし、二重提供を拒否する。</summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ServeAwardsFeaturedBonusOnceOnlyWhenIncluded(bool includesFeatured)
        {
            var sim = Create();
            Assert.IsTrue(sim.BeginDay(), "検証用店舗を開店できる");
            sim.State.featuredIngredient = "bok_choy";
            var o = Ready(sim, includesFeatured ? "bok_choy" : "enoki");
            o.serviceBonus = 2;
            Assert.IsTrue(sim.Serve(o.id), "完成済み持ち帰り注文を提供できる");
            Assert.AreEqual(includesFeatured ? 5 : 2, o.serviceBonus, "旬入りだけ既存の接客加点に3点加える");
            Assert.AreEqual(includesFeatured ? 1 : 0, sim.State.stats.seasonalServed, "旬入りだけ累計に一杯加える");
            Assert.IsFalse(sim.Serve(o.id), "同じ注文の二重提供を拒否する");
            Assert.AreEqual(includesFeatured ? 5 : 2, o.serviceBonus, "二重提供で加点しない");
            Assert.AreEqual(includesFeatured ? 1 : 0, sim.State.stats.seasonalServed, "二重提供で旬杯数を増やさない");
        }

        /// <summary>旬なしのミッション候補から旬を除き、旬ありでは具材名付きのミッションが生成される。</summary>
        [Test]
        public void SeasonalMissionPoolRequiresFeaturedIngredientAndDescribesItsName()
        {
            var sim = Create();
            bool found = false;
            for (int day = 1; day <= 150; day++)
            {
                sim.State.day = day;
                sim.State.featuredIngredient = "";
                Missions.Generate(sim);
                Assert.IsFalse(sim.State.missions.Any(m => m.kind == "seasonal"), "旬が無ければ旬ミッションは出ない");
                sim.State.featuredIngredient = "bok_choy";
                Missions.Generate(sim);
                var mission = sim.State.missions.FirstOrDefault(m => m.kind == "seasonal");
                if (mission == null) continue;
                found = true;
                Assert.AreEqual("bok_choy", mission.param, "ミッションは現在の旬を対象とする");
                Assert.AreEqual(3, mission.target, "レベル1の旬ミッションは3杯を要求する");
                StringAssert.Contains(sim.Catalog.Get("bok_choy").displayName, Missions.Describe(mission), "説明には旬の具材の表示名を使う");
            }
            Assert.IsTrue(found, "固定150日中に旬ミッションを実際に生成できる");
        }

        /// <summary>旬ミッションは対象入りの提供済み注文数だけを数え、重複具材や未提供を数えない。</summary>
        [Test]
        public void SeasonalMissionProgressCountsServedOrdersNotIngredientsOrLifetimeStats()
        {
            var sim = Create();
            sim.State.stats.seasonalServed = 99;
            Ready(sim, "bok_choy", "bok_choy").stage = OrderStage.Served;
            Ready(sim, "bok_choy").stage = OrderStage.Served;
            Ready(sim, "enoki").stage = OrderStage.Served;
            Ready(sim, "bok_choy");
            Ready(sim, "bok_choy").stage = OrderStage.Cancelled;
            var m = new Mission { kind = "seasonal", param = "bok_choy" };
            Assert.AreEqual(2, Missions.Progress(sim.State, m), "旬入り提供済み注文二件だけ数える");
            sim.State.orders.Clear();
            Assert.AreEqual(0, Missions.Progress(sim.State, m), "翌日相当の注文消去後は累計から進捗を作らない");
        }

        /// <summary>日替わり天気の季節重みはSeasonsと同じ境界を使い、旬と週番号を補完する。</summary>
        [TestCase(1)]
        [TestCase(14)]
        [TestCase(15)]
        [TestCase(28)]
        [TestCase(29)]
        [TestCase(42)]
        [TestCase(43)]
        [TestCase(56)]
        [TestCase(57)]
        public void DailyEventsUsesSeasonBoundariesAndEnsuresFeatured(int day)
        {
            var sim = Create();
            StockAll(sim);
            sim.State.day = day;
            sim.State.featuredIngredient = "";
            sim.State.featuredWeek = -1;
            int season = Seasons.Index(day);
            float[] weights = Content.Weathers.Select(w => w.Id == "cold" ? (season == 3 ? 3f : season == 2 ? .8f : .2f)
                : w.Id == "hot" ? (season == 1 ? 3f : season == 0 ? .6f : .2f)
                : w.Id == "rain" ? 1.2f : w.Id == "sunny" ? 2.5f : 1.5f).ToArray();
            var random = new System.Random(unchecked(day * 92821 + CustomerDirector.StableHash(sim.State.storeId)));
            double roll = random.NextDouble() * weights.Sum();
            int chosen = weights.Length - 1;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0) { chosen = i; break; }
            }
            string expectedFeatured = Seasons.PickFeatured(sim);
            DailyEvents.Roll(sim);
            Assert.AreEqual(Content.Weathers[chosen].Id, sim.State.weather, "日替わり天気が季節境界に一致する重みで抽選される");
            Assert.AreEqual(expectedFeatured, sim.State.featuredIngredient, "日替わり処理で旬を決定的に補完する");
            Assert.AreEqual(Seasons.Week(day), sim.State.featuredWeek, "日替わり処理で旬の週番号も更新する");
            string weather = sim.State.weather, dailyEvent = sim.State.dailyEvent;
            DailyEvents.Roll(sim);
            Assert.AreEqual(weather, sim.State.weather, "同じ日・店舗で天気抽選を再現する");
            Assert.AreEqual(dailyEvent, sim.State.dailyEvent, "同じ日・店舗でイベント抽選を再現する");
        }

        /// <summary>日替わり処理も週途中の解禁で旬を変えない。</summary>
        [Test]
        public void DailyEventsPreservesFeaturedWithinExistingWeek()
        {
            var sim = Create();
            sim.State.featuredIngredient = "bok_choy";
            sim.State.featuredWeek = 0;
            sim.State.day = 7;
            sim.State.ingredients.unlockedIds.Add("pea_shoot");
            DailyEvents.Roll(sim);
            Assert.AreEqual("bok_choy", sim.State.featuredIngredient, "日替わり処理でも同週の旬を保持する");
            Assert.AreEqual(0, sim.State.featuredWeek, "日替わり処理でも同週番号を保持する");
        }

        /// <summary>新規店舗とJSONから再構築した店舗は旬も客の全シリアライズ情報も一致する。</summary>
        [Test]
        public void NewAndJsonReconstructedSimulationProduceIdenticalFeaturedAndCustomers()
        {
            var original = new RestaurantSimulation(IngredientCatalog.Load());
            var restored = new RestaurantSimulation(original.Catalog, JsonUtility.FromJson<GameState>(JsonUtility.ToJson(original.State)));
            Assert.IsNotEmpty(original.State.featuredIngredient, "新規店舗にも旬を用意する");
            Assert.AreEqual(original.State.featuredIngredient, restored.State.featuredIngredient, "再構築で旬を変えない");
            Assert.AreEqual(original.State.featuredWeek, restored.State.featuredWeek, "再構築で週番号を変えない");
            for (int i = 0; i < 40; i++)
            {
                var a = original.Customers.Create();
                var b = restored.Customers.Create();
                Assert.IsNotNull(a, "初期在庫から客を生成できる");
                Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b), "客の名前・具材・スープ・外見を含む全保存値が再現する");
            }
        }

        /// <summary>提供ゼロなら取りこぼしや満足度に関係なく評価はゼロになる。</summary>
        [Test]
        public void DayScoreIsZeroWithoutServedOrders()
        {
            var s = GradedState(0);
            s.report.lost = 4;
            Assert.AreEqual(0, DayGrade.Score(s), "提供が無い営業は0点になる");
        }

        /// <summary>満足度・取りこぼし・値引き件数・提供数の増減が評価へ期待する方向に影響する。</summary>
        [TestCase("satisfaction")]
        [TestCase("lost")]
        [TestCase("complaints")]
        [TestCase("volume")]
        public void DayScoreRespondsToQualityLossComplaintsAndVolume(string factor)
        {
            var baseline = GradedState();
            var changed = GradedState();
            if (factor == "satisfaction") changed.orders.ForEach(o => o.report.satisfaction = 85);
            if (factor == "lost") changed.report.lost = 3;
            if (factor == "complaints") { changed.report.complaints = 3; changed.report.discountsFen = 300; }
            if (factor == "volume") changed = GradedState(6);
            Assert.Less(DayGrade.Score(changed), DayGrade.Score(baseline), "満足度低下・取りこぼし・値引き件数増・提供不足はいずれも評価を下げる");
        }

        /// <summary>既知の日報で評価の計算結果を検証し、提供済み以外の満足度を除外する。</summary>
        [Test]
        public void DayScoreMatchesKnownFormulaAndIgnoresUnservedSatisfaction()
        {
            var s = GradedState(6, 80);
            s.report.lost = 2;
            s.report.complaints = 1;
            s.orders.Add(new CustomerOrder { stage = OrderStage.Ready, report = new QualityReport { satisfaction = 100 } });
            s.orders.Add(new CustomerOrder { stage = OrderStage.Cancelled, report = new QualityReport { satisfaction = 0 } });
            Assert.AreEqual(52.5f, DayGrade.Score(s), .0001f, "80点から取りこぼし15点・値引き5点・提供不足7.5点を引く");
        }

        /// <summary>評価点は上下限で0〜100に収まる。</summary>
        [TestCase(-100f, 0f)]
        [TestCase(150f, 100f)]
        public void DayScoreClampsToZeroThroughHundred(float satisfaction, float expected)
        {
            Assert.AreEqual(expected, DayGrade.Score(GradedState(12, satisfaction)), "極端な満足度でも点数の上下限を守る");
        }

        /// <summary>評価文字の各しきい値では等号を上位評価へ含める。</summary>
        [TestCase(100f, "S")]
        [TestCase(90f, "S")]
        [TestCase(89.999f, "A")]
        [TestCase(80f, "A")]
        [TestCase(79.999f, "B")]
        [TestCase(68f, "B")]
        [TestCase(67.999f, "C")]
        [TestCase(55f, "C")]
        [TestCase(54.999f, "D")]
        [TestCase(0f, "D")]
        public void DayGradeLetterIncludesExactThreshold(float score, string expected)
        {
            Assert.AreEqual(expected, DayGrade.Letter(score), "90・80・68・55点の境界を正しく分類する");
        }

        /// <summary>評価記録は日報を要約し、評価を先頭に表示してS累計を一度だけ増やす。</summary>
        [TestCase(95f, "S", 1)]
        [TestCase(85f, "A", 0)]
        public void RecordSummarizesDayAndIsIdempotent(float satisfaction, string grade, int gradeS)
        {
            var s = GradedState(12, satisfaction);
            s.day = 8;
            s.report.revenueFen = 25000;
            s.report.tipsFen = 800;
            s.report.foodCostFen = 7000;
            s.report.fixedCostFen = 1200;
            s.report.wagesFen = 500;
            s.report.changeLossFen = 100;
            s.report.missionRewardsFen = 200;
            s.report.highlights.Add("既存の出来事");
            var summary = DayGrade.Record(s);
            Assert.AreEqual(1, s.history.Count, "一日の記録を一件追加する");
            Assert.AreEqual(8, summary.day, "当日の日付を記録する");
            Assert.AreEqual(25800, summary.revenueFen, "売上とチップの合計を記録する");
            Assert.AreEqual(17200, summary.profitFen, "仕入原価・固定費・給与・釣銭損失・報酬を含む利益を記録する");
            Assert.AreEqual(12, summary.served, "提供杯数を記録する");
            Assert.AreEqual(0, summary.lost, "取りこぼし件数を記録する");
            Assert.AreEqual(satisfaction, summary.satisfaction, "提供済み客の平均満足度を記録する");
            Assert.AreEqual(grade, summary.grade, "営業評価文字を記録する");
            Assert.AreEqual(gradeS, s.stats.gradeS, "Sの場合だけ累計を増やす");
            Assert.AreEqual("本日の評価：" + grade, s.report.highlights[0], "評価をハイライト先頭へ置く");
            Assert.AreSame(summary, DayGrade.Record(s), "同日の二度目は既存記録を返す");
            Assert.AreEqual(1, s.history.Count, "二重記録で履歴を増やさない");
            Assert.AreEqual(gradeS, s.stats.gradeS, "二重記録でS累計を増やさない");
            Assert.AreEqual(2, s.report.highlights.Count, "二重記録で評価ハイライトを増やさない");
        }

        /// <summary>履歴は28日分だけを保持し、上限を超えた最古の記録から削除する。</summary>
        [Test]
        public void RecordRetainsMostRecentTwentyEightDays()
        {
            Assert.AreEqual(28, DayGrade.HistoryLimit, "履歴上限は28日である");
            var s = GradedState();
            for (int day = 1; day <= 30; day++)
            {
                s.day = day;
                DayGrade.Record(s);
                Assert.AreEqual(Math.Min(day, 28), s.history.Count, "履歴数は上限まで増え、その後は一定になる");
            }
            CollectionAssert.AreEqual(Enumerable.Range(3, 28).ToArray(), s.history.Select(h => h.day).ToArray(), "1・2日目を捨てて3〜30日目を順序通り残す");
            Assert.AreEqual(30, s.stats.gradeS, "履歴から消えても過去のS累計は保持する");
        }

        /// <summary>公開Tickからの閉店経路で当日の履歴・評価・S実績を一度だけ記録する。</summary>
        [Test]
        public void EndDayThroughTickRecordsHistoryAndUnlocksGradeAchievement()
        {
            var sim = Create();
            sim.State.orders = GradedState().orders;
            sim.State.report.served = 12;
            sim.State.phase = DayPhase.Closing;
            sim.Tick(1);
            Assert.AreEqual(DayPhase.Report, sim.State.phase, "客の処理完了後に閉店する");
            Assert.AreEqual(1, sim.State.history.Count, "EndDay経由で当日履歴を一件記録する");
            Assert.AreEqual(sim.State.day, sim.State.history[0].day, "履歴の日付は閉店した日になる");
            Assert.AreEqual("S", sim.State.history[0].grade, "満足度95・取りこぼしなし・十分な杯数でSになる");
            Assert.AreEqual(1, sim.State.stats.gradeS, "閉店でS累計を増やす");
            Assert.AreEqual("本日の評価：S", sim.State.report.highlights[0], "閉店の他ハイライトより評価を先に表示する");
            CollectionAssert.Contains(sim.State.achievements, "grade_s", "S記録後の実績判定で最高の一日を解禁する");
            sim.Tick(1);
            Assert.AreEqual(1, sim.State.history.Count, "レポート中の再Tickでは記録を増やさない");
            Assert.AreEqual(1, sim.State.stats.gradeS, "レポート中の再TickではS累計を増やさない");
        }

        /// <summary>v4実JSONの欠落フィールドを補完し、v5へ移行後にコンストラクタが旬を用意する。</summary>
        [Test]
        public void VersionFourMissingSeasonAndHistoryMigratesAndConstructorSuppliesFeatured()
        {
            Assert.GreaterOrEqual(GameState.CurrentVersion, 5, "保存形式はv5以降である");
            WithRepository((repo, path) =>
            {
                WriteLegacy(path, VersionFourPayload, 4);
                var s = repo.Load();
                Assert.IsNotNull(s, "v4実JSONを読み込める");
                Assert.IsTrue(string.IsNullOrEmpty(repo.LastError), "バックアップ復元ではなく本体を移行する");
                Assert.AreEqual(GameState.CurrentVersion, s.saveVersion, "読み込みで最新形式へ移行する");
                Assert.AreEqual("", s.featuredIngredient, "欠落した旬IDは空で補完する");
                Assert.AreEqual(-1, s.featuredWeek, "欠落した旬の週番号は未決定の-1で補完する");
                Assert.IsNotNull(s.history, "欠落した履歴リストを補完する");
                Assert.IsEmpty(s.history, "旧保存に架空の営業履歴を追加しない");
                var sim = new RestaurantSimulation(IngredientCatalog.Load(), s);
                Assert.AreEqual(Seasons.PickFeatured(sim), s.featuredIngredient, "旧保存の再構築で決定的な旬を補完する");
                Assert.IsNotEmpty(s.featuredIngredient, "旧保存の解禁済み野菜・きのこから旬を用意する");
                Assert.AreEqual(2, s.featuredWeek, "15日目に対応する旬の週へ補完する");
            });
        }

        /// <summary>v5の保存・読込で旬ID・週番号・全履歴フィールドを保持する。</summary>
        [Test]
        public void VersionFiveRoundTripPreservesFeaturedAndHistory()
        {
            var sim = Create();
            sim.State.day = 22;
            sim.State.featuredIngredient = "bok_choy";
            sim.State.featuredWeek = 3;
            sim.State.history.Add(new DaySummary { day = 20, revenueFen = 12345, profitFen = -500,
                served = 4, lost = 2, satisfaction = 67.5f, grade = "C" });
            sim.State.history.Add(new DaySummary { day = 21, revenueFen = 50000, profitFen = 37000,
                served = 14, lost = 0, satisfaction = 96.25f, grade = "S" });
            sim.State.stats.gradeS = 1;
            sim.State.stats.seasonalServed = 30;
            WithRepository((repo, path) =>
            {
                repo.Save(sim.State);
                var envelope = JsonUtility.FromJson<LegacyEnvelope>(File.ReadAllText(path));
                Assert.AreEqual(GameState.CurrentVersion, envelope.version, "包絡形式も最新で保存する");
                var loaded = repo.Load();
                Assert.IsNotNull(loaded, "v5保存を読み込める");
                Assert.IsTrue(string.IsNullOrEmpty(repo.LastError), "保存直後の読込でエラーは無い");
                Assert.AreEqual(GameState.CurrentVersion, loaded.saveVersion, "保存内状態も最新形式");
                Assert.AreEqual("bok_choy", loaded.featuredIngredient, "旬IDを保持する");
                Assert.AreEqual(3, loaded.featuredWeek, "旬の週番号を保持する");
                Assert.AreEqual(2, loaded.history.Count, "履歴の件数を保持する");
                for (int i = 0; i < sim.State.history.Count; i++)
                    Assert.AreEqual(JsonUtility.ToJson(sim.State.history[i]), JsonUtility.ToJson(loaded.history[i]), "全履歴フィールドと順序を保持する");
                Assert.AreEqual(1, loaded.stats.gradeS, "S累計を保持する");
                Assert.AreEqual(30, loaded.stats.seasonalServed, "旬杯数累計を保持する");
                new RestaurantSimulation(sim.Catalog, loaded);
                Assert.AreEqual("bok_choy", loaded.featuredIngredient, "同週の再構築でも保存された旬を保持する");
            });
        }

        /// <summary>移行時は履歴のnull要素を除去し、有効な要素の順序を保持する。</summary>
        [Test]
        public void SaveMigrationRemovesNullHistoryEntries()
        {
            var s = new GameState();
            var first = new DaySummary { day = 1, grade = "C" };
            var last = new DaySummary { day = 2, grade = "S" };
            s.history = new List<DaySummary> { null, first, null, last, null };
            SaveMigration.Migrate(s);
            CollectionAssert.AreEqual(new[] { first, last }, s.history, "null履歴だけ除去して有効な履歴は順序・同一性を保持する");
        }

        /// <summary>履歴がnullの保存互換状態も空のリストへ正規化する。</summary>
        [Test]
        public void SaveMigrationNormalizesNullHistoryAndFeatured()
        {
            var s = new GameState { saveVersion = 4, history = null, featuredIngredient = null };
            SaveMigration.Migrate(s);
            Assert.IsNotNull(s.history, "null履歴を使用可能なリストへ戻す");
            Assert.IsEmpty(s.history, "欠損履歴を架空の記録で埋めない");
            Assert.AreEqual("", s.featuredIngredient, "null旬IDを空へ戻す");
            Assert.AreEqual(GameState.CurrentVersion, s.saveVersion, "正規化後は最新形式へ移行する");
        }

        /// <summary>新しい実績の条件は必要数の直前で未達、等号と超過で達成する。</summary>
        [TestCase("grade_s", 0, false)]
        [TestCase("grade_s", 1, true)]
        [TestCase("grade_s", 2, true)]
        [TestCase("grade_s10", 9, false)]
        [TestCase("grade_s10", 10, true)]
        [TestCase("grade_s10", 11, true)]
        [TestCase("seasonal_30", 29, false)]
        [TestCase("seasonal_30", 30, true)]
        [TestCase("seasonal_30", 31, true)]
        [TestCase("four_seasons", 56, false)]
        [TestCase("four_seasons", 57, true)]
        [TestCase("four_seasons", 58, true)]
        public void NewAchievementConditionsRespectExactThresholds(string id, int count, bool expected)
        {
            var s = new GameState();
            if (id == "grade_s" || id == "grade_s10") s.stats.gradeS = count;
            if (id == "seasonal_30") s.stats.seasonalServed = count;
            if (id == "four_seasons") s.day = count;
            var definition = Content.Achievements.Single(a => a.Id == id);
            Assert.AreEqual(expected, definition.Condition(s), "実績のしきい値の直前・等号・超過を判定する");
        }
    }
}
