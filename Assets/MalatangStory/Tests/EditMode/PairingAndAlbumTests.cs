using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>具材の相性（判定・加点・発見の記録）と、一杯アルバム（載る条件・上位12杯・保存の正規化）を保証する。</summary>
    public class PairingAndAlbumTests
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

        private static CustomerOrder Ready(RestaurantSimulation sim, float quality, params string[] ids)
        {
            var o = new CustomerOrder
            {
                id = "bowl-" + sim.State.orders.Count, customerName = "テスト", archetype = "office", stage = OrderStage.Ready,
                soup = "mala", chosenSoup = "mala", quality = quality, takeout = true, patience = 999,
            };
            o.selected.AddRange(ids);
            o.report = new QualityReport { total = quality }; o.report.comments.Add("ok");
            sim.State.orders.Add(o);
            return o;
        }

        [Test]
        public void EveryPairingUsesTwoDistinctCatalogIngredientsAndIsNotHiddenInsideARecipe()
        {
            var catalog = IngredientCatalog.Load();
            Assert.AreEqual(Pairings.All.Length, Pairings.All.Select(p => p.Id).Distinct().Count(), "ID が重複しない");
            foreach (var p in Pairings.All)
            {
                Assert.AreNotEqual(p.A, p.B, p.Id);
                Assert.IsNotNull(catalog.Get(p.A), p.A); Assert.IsNotNull(catalog.Get(p.B), p.B);
                Assert.IsFalse(string.IsNullOrEmpty(p.Name) || string.IsNullOrEmpty(p.Note), p.Id + " に名前とひとことがある");
                Assert.IsFalse(Recipes.All.Any(r => r.Ingredients.Contains(p.A) && r.Ingredients.Contains(p.B)), p.Id + " は隠しレシピの一部と重ならない（加点が二重にならない）");
            }
        }

        [Test]
        public void PairsAreFoundRegardlessOfOrderAndTheBonusIsCappedAtThreePairs()
        {
            var found = Pairings.In(new[] { "beef_slice", "rice_noodle", "enoki" });
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("肥牛えのき", found[0].Name);
            Assert.AreEqual("beef_slice", found[0].PartnerOf("enoki"));
            Assert.AreEqual(0, Pairings.In(new[] { "enoki" }).Count, "片方だけでは成立しない");
            Assert.AreEqual(1.5f, Pairings.Bonus(1));
            Assert.AreEqual(Pairings.Bonus(3), Pairings.Bonus(5), "4組目以降は加点しない");
        }

        [Test]
        public void ApplyBonusRaisesQualityWithoutExceedingOneHundredAndExplainsWhy()
        {
            var o = new CustomerOrder { report = new QualityReport { total = 80 } };
            o.report.comments.Add("火の通りが完璧！");
            o.selected.AddRange(new[] { "enoki", "beef_slice", "potato", "pork_belly" });
            Pairings.ApplyBonus(o);
            Assert.AreEqual(83f, o.report.total, 1e-4f);
            CollectionAssert.AreEquivalent(new[] { "enoki+beef_slice", "potato+pork_belly" }, o.pairings);
            StringAssert.Contains("相性", o.report.comments[0]);
            o.report.total = 99.5f;
            Pairings.ApplyBonus(o);
            Assert.AreEqual(100f, o.report.total, "100点を超えない");
            var none = new CustomerOrder { report = new QualityReport { total = 70 } };
            none.selected.Add("enoki");
            Pairings.ApplyBonus(none);
            Assert.AreEqual(70f, none.report.total); Assert.AreEqual(0, none.pairings.Count);
        }

        [Test]
        public void ServingRecordsDiscoveryOnceAndCountsRepeats()
        {
            var sim = Open();
            int events = 0;
            sim.Event += e => { if (e.Kind == SimEventKind.PairingDiscovered) events++; };
            var first = Ready(sim, 88, "enoki", "beef_slice"); first.pairings.Add("enoki+beef_slice");
            Assert.IsTrue(sim.Serve(first.id));
            var again = Ready(sim, 88, "enoki", "beef_slice"); again.pairings.Add("enoki+beef_slice");
            Assert.IsTrue(sim.Serve(again.id));
            Assert.AreEqual(1, events, "初めての組み合わせだけ発見として知らせる");
            Assert.IsTrue(Pairings.Discovered(sim.State, "enoki+beef_slice"));
            Assert.AreEqual(2, LifetimeStats.Get(sim.State.pairings, "enoki+beef_slice"));
            Assert.AreEqual(1, sim.State.report.highlights.Count(h => h.Contains("肥牛えのき")));
        }

        [Test]
        public void FinishedBowlWithAPairingGetsTheBonusThroughTheRealCookingFlow()
        {
            var sim = Open();
            foreach (string id in new[] { "enoki", "beef_slice" })
            {
                if (!sim.Unlocks.IsUnlocked(id)) sim.State.ingredients.unlockedIds.Add(id);
                sim.StockFor(id).quantity = 10;
            }
            var o = new CustomerOrder
            {
                id = "pair-flow", customerName = "相性さん", archetype = "office", style = OrderStyle.Ordered, stage = OrderStage.Waiting,
                soup = "mala", spice = 1, patience = 999, takeout = true, payment = PaymentMethod.QrCode,
            };
            o.requested.AddRange(new[] { "enoki", "beef_slice" });
            sim.State.orders.Add(o);
            RestaurantTests.Cook(sim, o);
            CollectionAssert.Contains(o.pairings, "enoki+beef_slice");
            Assert.IsTrue(o.report.comments.Any(c => c.Contains("肥牛えのき")), "完成時に相性の加点とひとことが付く");
            Assert.IsTrue(Pairings.Discovered(sim.State, "enoki+beef_slice"), "提供すると図鑑に載る");
        }

        [Test]
        public void AlbumKeepsOnlyTheTwelveBestBowlsAboveTheThreshold()
        {
            var s = new GameState { day = 3 };
            var low = new CustomerOrder { quality = 84, chosenSoup = "mala" }; low.selected.Add("enoki");
            Assert.IsFalse(BowlAlbum.Record(s, low), "85点未満は載らない");
            var empty = new CustomerOrder { quality = 99, chosenSoup = "mala" };
            Assert.IsFalse(BowlAlbum.Record(s, empty), "具材の無い一杯は載らない");
            for (int i = 0; i < BowlAlbum.Limit; i++)
            {
                var o = new CustomerOrder { quality = 86 + i % 5, chosenSoup = "mala" }; o.selected.Add("enoki");
                Assert.IsTrue(BowlAlbum.Record(s, o));
            }
            Assert.AreEqual(BowlAlbum.Limit, s.album.Count);
            var weak = new CustomerOrder { quality = 85, chosenSoup = "mala" }; weak.selected.Add("enoki");
            Assert.IsFalse(BowlAlbum.Record(s, weak), "満杯なら下位の一杯は載らない");
            var best = new CustomerOrder { quality = 100, chosenSoup = "tomato", recipe = "tomato_egg" }; best.selected.AddRange(new[] { "egg", "cherry_tomato" });
            Assert.IsTrue(BowlAlbum.Record(s, best));
            Assert.AreEqual(BowlAlbum.Limit, s.album.Count);
            Assert.AreEqual(100, s.album[0].quality, "品質の高い順に並ぶ");
            Assert.IsTrue(s.album.Zip(s.album.Skip(1), (a, b) => a.quality >= b.quality).All(x => x));
            Assert.AreEqual("トマたま麻辣湯", BowlAlbum.Title(s.album[0], id => id), "レシピの一杯はレシピ名で載る");
            best.selected.Add("spinach");
            CollectionAssert.AreEqual(new[] { "egg", "cherry_tomato" }, s.album[0].ingredients, "記録は注文と別の一覧を持つ");
        }

        [Test]
        public void ServingAnExcellentBowlAddsItToTheAlbumAndAnnouncesIt()
        {
            var sim = Open();
            int events = 0;
            sim.Event += e => { if (e.Kind == SimEventKind.AlbumAdded) events++; };
            var o = Ready(sim, 93, "bok_choy", "shrimp_ball");
            o.condiments.Add(Content.ChiliOil);
            Assert.IsTrue(sim.Serve(o.id));
            Assert.AreEqual(1, sim.State.album.Count);
            Assert.AreEqual(1, events);
            Assert.AreEqual(sim.State.day, sim.State.album[0].day);
            CollectionAssert.AreEqual(new[] { "bok_choy", "shrimp_ball" }, sim.State.album[0].ingredients);
        }

        [Test]
        public void TiesKeepTheNewestBowlsAndRankingUsesTheUnroundedQuality()
        {
            var s = new GameState { day = 4 };
            CustomerOrder Bowl(float q) { var o = new CustomerOrder { quality = q, chosenSoup = "mala", customerName = "客" + q }; o.selected.Add("enoki"); return o; }
            for (int i = 0; i < BowlAlbum.Limit; i++) { var o = Bowl(90); o.customerName = "客" + i; BowlAlbum.Record(s, o); }
            var newest = Bowl(90); newest.customerName = "最新";
            Assert.IsTrue(BowlAlbum.Record(s, newest), "同点なら新しい一杯が載る");
            Assert.IsFalse(s.album.Any(e => e.customerName == "客0"), "押し出されるのは最も古い一杯");
            Assert.IsTrue(s.album.Any(e => e.customerName == "客11"), "直前に載せた一杯は残る");
            var t2 = new GameState { day = 4 };
            for (int i = 0; i < BowlAlbum.Limit; i++) BowlAlbum.Record(t2, Bowl(90.4f));
            Assert.IsFalse(BowlAlbum.Record(t2, Bowl(89.6f)), "丸めると同じ90点でも、低い一杯は高い一杯を押し出さない");
        }

        [Test]
        public void SaveNormalizationDropsUnknownPairingsAndBrokenAlbumEntriesAndRestoresTheLimit()
        {
            var s = new GameState();
            s.pairings.Add(new NamedCount { id = "enoki+beef_slice", count = 2 });
            s.pairings.Add(new NamedCount { id = "unknown+pair", count = 1 });
            AlbumEntry Entry(int day, float score, string soup = "mala", bool withItems = true)
            {
                var e = new AlbumEntry { day = day, soup = soup, score = score, quality = (int)score, seq = day, condiments = null, pairings = null };
                if (withItems) e.ingredients.Add("enoki"); else e.ingredients = null;
                return e;
            }
            for (int i = 1; i <= 14; i++) s.album.Add(Entry(i, 86 + i * .5f));
            s.album.Add(Entry(0, 0, ""));
            s.album.Add(Entry(20, 70));
            s.album.Add(Entry(21, 99, withItems: false));
            s.album.Add(Entry(22, 99, "unknown_soup"));
            s.saveVersion = 6;
            SaveMigration.Migrate(s);
            Assert.AreEqual(GameState.CurrentVersion, s.saveVersion);
            Assert.AreEqual(1, s.pairings.Count);
            Assert.AreEqual(BowlAlbum.Limit, s.album.Count, "壊れた記録・下限未満を除き、上位12杯に戻す");
            Assert.IsTrue(s.album.All(e => e.score >= BowlAlbum.MinQuality && e.ingredients.Count > 0 && e.pairings != null && e.condiments != null));
            Assert.AreEqual(93f, s.album[0].score, 1e-4f);
            Assert.GreaterOrEqual(s.albumSeq, 14, "通し番号は既存の記録より先から振る");
        }

        [Test]
        public void ReadyBowlsFromAnOlderSaveRecoverTheirPairingsWithoutAnExtraBonus()
        {
            var s = new GameState { saveVersion = 6 };
            var o = new CustomerOrder { id = "old", stage = OrderStage.Ready, quality = 80, pairings = null };
            o.selected.AddRange(new[] { "enoki", "beef_slice" });
            s.orders.Add(o);
            SaveMigration.Migrate(s);
            CollectionAssert.AreEqual(new[] { "enoki+beef_slice" }, o.pairings);
            Assert.AreEqual(80f, o.quality, "確定済みの品質は変えない");
        }
    }
}
