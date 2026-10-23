using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class StaffCareTests
    {
        private RestaurantSimulation Sim()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load()); sim.State.tutorialComplete = true;
            sim.State.shopLevel = 8; sim.State.moneyFen = 1000000; return sim;
        }
        [Test] public void SameRoleHasDistinctIdsAndManagementTargetsOnlyOnePerson()
        {
            var sim = Sim();
            Assert.IsTrue(sim.HireStaff(StaffRole.Server)); Assert.IsTrue(sim.HireStaff(StaffRole.Server)); Assert.IsTrue(sim.HireStaff(StaffRole.Server));
            Assert.IsFalse(sim.HireStaff(StaffRole.Server));
            Assert.AreEqual(3, sim.State.staff.Select(x => x.id).Distinct().Count());
            string first = sim.State.staff[0].id, second = sim.State.staff[1].id;
            Assert.IsTrue(sim.TrainStaff(second)); Assert.AreEqual(1, sim.State.staff[0].trainingLevel); Assert.AreEqual(2, sim.State.staff[1].trainingLevel);
            Assert.IsTrue(sim.FireStaff(second)); Assert.AreEqual(2, sim.State.staff.Count); Assert.IsTrue(sim.State.staff.Any(x => x.id == first));
            SaveMigration.Migrate(sim.State); Assert.DoesNotThrow(() => SaveRepository.Validate(sim.State));
        }
        [Test] public void TwoServersReserveDifferentReadyOrdersAndNeverDoubleServe()
        {
            var sim = Sim(); sim.HireStaff(StaffRole.Server); sim.HireStaff(StaffRole.Server);
            foreach (string id in new[] { "a", "b" }) sim.State.orders.Add(new CustomerOrder { id = id, customerName = "テストさん", type = "会社員", takeout = true, stage = OrderStage.Ready });
            sim.StaffWork.Tick(.1f);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, sim.State.staff.Select(x => x.taskTarget));
            sim.StaffWork.Tick(100);
            Assert.AreEqual(2, sim.State.report.served);
            sim.StaffWork.Tick(100); Assert.AreEqual(2, sim.State.report.served);
        }
        [Test] public void RestAndMealRecoverFatigueAndPraiseIsLimitedToOncePerDay()
        {
            var sim = Sim(); sim.HireStaff(StaffRole.Server); var member = sim.State.staff.Single();
            member.fatigue = 80; member.motivation = 40;
            float tired = StaffSystem.TaskSeconds(member);
            Assert.IsTrue(sim.RestStaff(member.id, true)); sim.StaffWork.Tick(100);
            Assert.AreEqual(60, member.fatigue, .001f); Assert.Less(StaffSystem.TaskSeconds(member), tired);
            Assert.IsTrue(sim.PraiseStaff(member.id)); Assert.IsFalse(sim.PraiseStaff(member.id));
            var stock = sim.State.stock.First(x => x.quantity > 0); int quantity = stock.quantity;
            Assert.IsTrue(sim.StaffMeal(member.id, stock.ingredientId)); Assert.IsFalse(sim.StaffMeal(member.id, stock.ingredientId));
            Assert.AreEqual(quantity - 1, stock.quantity); Assert.AreEqual(65, member.motivation); Assert.AreEqual(45, member.fatigue);
        }
        [Test] public void OldStaffWithoutIdsGainReproducibleIdsAndKeepTheirJobs()
        {
            var sim = Sim(); sim.State.saveVersion = 9;
            sim.State.staff.Add(new HiredStaff { role = StaffRole.Cashier, name = "甲", trainingLevel = 2 });
            sim.State.staff.Add(new HiredStaff { role = StaffRole.Cook, name = "乙", trainingLevel = 3 });
            SaveMigration.Migrate(sim.State); string ids = string.Join(",", sim.State.staff.Select(x => x.id));
            SaveMigration.Migrate(sim.State); Assert.AreEqual(ids, string.Join(",", sim.State.staff.Select(x => x.id)));
            Assert.AreEqual(2, sim.State.staff[0].trainingLevel); Assert.AreEqual(70, sim.State.staff[0].motivation);
        }
    }
}
