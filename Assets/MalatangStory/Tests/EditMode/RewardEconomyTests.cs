using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class RewardEconomyTests
    {
        [Test] public void PhotoFiltersPreserveNeutralImagesAndClampWarmHighlights()
        {
            var original = PhotoFilter.Apply(12, 78, 200, 0); Assert.AreEqual(12, original.R); Assert.AreEqual(78, original.G); Assert.AreEqual(200, original.B);
            var monochrome = PhotoFilter.Apply(12, 78, 200, 2); Assert.AreEqual(monochrome.R, monochrome.G); Assert.AreEqual(monochrome.G, monochrome.B);
            var warm = PhotoFilter.Apply(255, 255, 255, 1); Assert.AreEqual(255, warm.R); Assert.GreaterOrEqual(warm.R, warm.G); Assert.Greater(warm.G, warm.B);
        }
        [Test] public void WholesalePricesAreDailyDeterministicAndWeatherSensitive()
        {
            Assert.AreEqual(1, WholesaleMarket.Multiplier(1, "sunny", "spring"));
            Assert.AreNotEqual(WholesaleMarket.Multiplier(2, "sunny", "spring"), WholesaleMarket.Multiplier(3, "sunny", "spring"));
            for (int day = 1; day <= 90; day++)
            {
                float dry = WholesaleMarket.Multiplier(day, "sunny", "winter"), wet = WholesaleMarket.Multiplier(day, "rain", "winter");
                Assert.That(dry, Is.InRange(.8f, 1.35f)); Assert.Greater(wet, dry);
                Assert.AreEqual(wet, WholesaleMarket.Multiplier(day, "rain", "winter"));
            }
        }
        [Test] public void CollectionMilestonesUnlockAppearanceAndSurviveFranchising()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            Assert.IsFalse(sim.ChooseCollectionReward("bowl", true));
            sim.State.ingredients.unlockedIds = sim.Catalog.ingredients.Where(x => !x.isRare).Select(x => x.id).ToList();
            sim.State.ingredients.rareObtainedIds = sim.Catalog.ingredients.Where(x => x.isRare).Select(x => x.id).ToList();
            Assert.AreEqual(100, CollectionRewards.Percent(sim.State, sim.Catalog));
            foreach (string id in new[] { "apron", "bowl", "pennant", "title" }) Assert.IsTrue(sim.ChooseCollectionReward(id, true));
            Assert.IsTrue(sim.State.chef.embroidery); Assert.AreEqual(4, sim.State.collectionRewardTier);
            sim.State.chapter = Chapters.All.Length;
            var next = FranchiseRules.Create(sim.Catalog, sim.State, "reward-city"); SaveMigration.Migrate(next);
            Assert.Less(CollectionRewards.Percent(next, sim.Catalog), 25); Assert.AreEqual(100, CollectionRewards.UnlockedPercent(next, sim.Catalog));
            Assert.IsTrue(next.collectionBowl); Assert.IsTrue(next.collectionPennant); Assert.IsTrue(next.collectionTitle); Assert.IsTrue(next.chef.embroidery);
        }
        [Test] public void ComboMilestoneBonusIsPaidOnceAndApologySpendsOneIngredient()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load()); sim.State.tutorialComplete = true; sim.State.combo = 9;
            foreach (string id in new[] { "first", "repeat" })
            {
                var order = new CustomerOrder { id = id, customerName = "常連さん", type = "会社員", stage = OrderStage.Ready, takeout = true, chosenSoup = "mala", quality = 100, paidFen = 3000 };
                sim.State.orders.Add(order); sim.State.combo = 9; Assert.IsTrue(sim.Serve(id));
            }
            Assert.AreEqual(100, sim.State.report.missionRewardsFen); CollectionAssert.AreEqual(new[] { 10 }, sim.State.comboMilestones);
            sim.State.daySeconds = 60; Satisfaction.BreakCombo(sim);
            var apology = new CustomerOrder { id = "apology", stage = OrderStage.Ready, quality = 90 }; sim.State.orders.Add(apology);
            int stock = sim.State.stock.Sum(x => x.quantity);
            Assert.IsTrue(sim.OfferApologySide(apology.id)); Assert.AreEqual(3, sim.State.combo); Assert.AreEqual(stock - 1, sim.State.stock.Sum(x => x.quantity));
            Assert.IsFalse(sim.OfferApologySide(apology.id)); Assert.AreEqual(stock - 1, sim.State.stock.Sum(x => x.quantity));
        }
    }
}
