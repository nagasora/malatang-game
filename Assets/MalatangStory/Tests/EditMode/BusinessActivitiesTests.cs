using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class BusinessActivitiesTests
    {
        private RestaurantSimulation Sim()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load()); sim.State.tutorialComplete = true;
            sim.State.shopLevel = 6; sim.State.moneyFen = 1000000; sim.State.day = 10;
            return sim;
        }
        [Test] public void WalkInsRespectTheDailyAndQueueLimitsAndNeverEnterChallenges()
        {
            var sim = Sim(); Assert.IsFalse(sim.TryWalkInVisitor(123, "office")); sim.State.phase = DayPhase.Open;
            for (int i = 0; i < 3; i++) Assert.IsTrue(sim.TryWalkInVisitor(123 + i, "office"));
            Assert.IsFalse(sim.TryWalkInVisitor(126, "office")); Assert.AreEqual(3, sim.State.walkInVisitors); Assert.AreEqual(3, sim.State.spawnedToday);
            var challenge = new RestaurantSimulation(sim.Catalog, ChallengeRules.Create(sim.Catalog, ChallengeRules.Rush, 123)); challenge.State.phase = DayPhase.Open;
            Assert.IsFalse(challenge.TryWalkInVisitor(123, "office"));
        }
        [Test] public void SmallSpillsCanBeWipedWithoutDuplicatingIngredientsOrRewards()
        {
            var sim = Sim(); var order = new CustomerOrder { id = "spill", stage = OrderStage.Drained }; sim.State.orders.Add(order);
            Assert.IsFalse(sim.PourSoup(order.id, "mala", float.NaN)); Assert.IsTrue(sim.PourSoup(order.id, "mala", 500)); Assert.AreEqual(50, order.counterSpillMl);
            long money = sim.State.moneyFen; Assert.IsTrue(sim.WipeCounter(order.id)); Assert.AreEqual(0, order.counterSpillMl);
            Assert.IsFalse(sim.WipeCounter(order.id)); Assert.AreEqual(500, order.soupMl); Assert.AreEqual(money, sim.State.moneyFen);
        }
        [Test] public void FranchiseStartsANewCityAndCopiesOnlyTheApprovedBonuses()
        {
            var sim = Sim(); sim.State.chapter = Chapters.All.Length; sim.State.stats.served = 321;
            var next = FranchiseRules.Create(sim.Catalog, sim.State, "new-city");
            Assert.AreEqual(1, next.day); Assert.AreEqual(0, next.stats.served); Assert.AreEqual(150000, next.moneyFen);
            Assert.AreEqual("new-city", next.storeId); Assert.AreEqual(1, next.franchiseNumber);
            Assert.AreEqual(321, sim.State.stats.served); Assert.AreEqual(1000000, sim.State.moneyFen);
            Assert.AreNotSame(sim.State.chef, next.chef); Assert.AreNotSame(sim.State.ownedThemes, next.ownedThemes);
            SaveMigration.Migrate(next); Assert.DoesNotThrow(() => SaveRepository.Validate(next));
            Assert.Throws<System.InvalidOperationException>(() => FranchiseRules.Create(sim.Catalog, next, "another-city"));
        }
        [Test] public void StallUsesAtMostFiveStockedIngredientsAndOnlyTakeoutOrders()
        {
            var sim = Sim(); sim.State.dailyEvent = "festival";
            Assert.IsTrue(sim.SetFestivalStall(true)); Assert.That(sim.State.stallMenu.Count, Is.InRange(3, 5));
            for (int i = 0; i < 15; i++)
            {
                var order = sim.Customers.Create("office"); Assert.NotNull(order);
                Assert.IsTrue(order.takeout); Assert.IsFalse(order.omakase); Assert.AreEqual(OrderStyle.SelfServe, order.style);
                Assert.IsTrue(order.requested.All(sim.State.stallMenu.Contains)); Assert.LessOrEqual(order.requested.Count, 3);
            }
        }
        [Test] public void RivalCookoffConcludesOnceAndChangesBothReputations()
        {
            var sim = Sim(); Assert.IsTrue(sim.ScheduleRivalCookoff());
            foreach (string id in new[] { "a", "b", "c" }) sim.State.orders.Add(new CustomerOrder { id = id, customerName = "審査員", archetype = "judge", takeout = true, stage = OrderStage.Served, quality = 100 });
            sim.State.phase = DayPhase.Closing; sim.Tick(1);
            Assert.AreEqual(DayPhase.Report, sim.State.phase); Assert.AreEqual(48, sim.State.rivalReputation); Assert.AreEqual(sim.State.day, sim.State.rivalResolvedDay);
            float reputation = sim.State.reputation; sim.Tick(10); Assert.AreEqual(reputation, sim.State.reputation); Assert.AreEqual(48, sim.State.rivalReputation);
        }
        [Test] public void DeliveryNeedsACourierArrivesLaterAndSettlesOnlyOnce()
        {
            var sim = Sim(); Assert.IsFalse(sim.SetDeliveryEnabled(true)); Assert.IsTrue(sim.HireStaff(StaffRole.Courier)); Assert.IsTrue(sim.SetDeliveryEnabled(true));
            var order = new CustomerOrder { id = "delivery", customerName = "出前さん", type = "会社員", delivery = true, takeout = true, stage = OrderStage.Ready, chosenSoup = "mala", soupMl = 340, quality = 90, paidFen = 3000 };
            order.report.total = 90; sim.State.orders.Add(order); sim.State.phase = DayPhase.Open; sim.State.spawnSeconds = 1000;
            Assert.IsTrue(sim.Serve(order.id)); Assert.AreEqual(OrderStage.Delivering, order.stage); Assert.AreEqual(0, sim.State.report.served);
            Assert.IsFalse(sim.Serve(order.id)); Assert.IsFalse(sim.RestStaff(sim.State.staff.Single().id, true));
            order.deliveryDuration = 50; sim.Tick(40); Assert.AreEqual(OrderStage.Delivering, order.stage);
            sim.Tick(10); Assert.AreEqual(OrderStage.Served, order.stage); Assert.IsTrue(order.delivered);
            Assert.AreEqual(88.5f, order.quality, .001f); Assert.AreEqual(1, sim.State.report.served);
            sim.Tick(10); Assert.AreEqual(1, sim.State.report.served);
        }
        [Test] public void NeighbourGiftIsLimitedAndNeedsStorageRoom()
        {
            var sim = Sim(); sim.State.reputation = 60;
            int before = sim.State.stock.Sum(x => x.quantity); Assert.IsTrue(sim.ReceiveNeighbourGift());
            Assert.AreEqual(before + 3, sim.State.stock.Sum(x => x.quantity)); Assert.IsFalse(sim.ReceiveNeighbourGift());
            sim.State.day += 5; foreach (var stock in sim.State.stock) stock.quantity = sim.State.storageCapacity;
            Assert.IsFalse(sim.ReceiveNeighbourGift());
        }
        [Test] public void FestivalAndCookoffAreExplicitPreparationChoices()
        {
            var sim = Sim(); Assert.IsFalse(sim.SetFestivalStall(true)); sim.State.dailyEvent = "festival";
            Assert.IsTrue(sim.SetFestivalStall(true)); Assert.IsTrue(sim.State.festivalStall);
            Assert.IsFalse(sim.ScheduleRivalCookoff()); Assert.IsTrue(sim.SetFestivalStall(false));
            Assert.IsTrue(sim.ScheduleRivalCookoff()); Assert.IsFalse(sim.ScheduleRivalCookoff());
            Assert.AreEqual("contest", sim.State.dailyEvent); Assert.AreEqual(180, sim.State.eventSeconds);
            sim.State.phase = DayPhase.Open; Assert.IsFalse(sim.SetFestivalStall(false));
        }
    }
}
