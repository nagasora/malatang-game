using System;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class ChallengeTests
    {
        [Test] public void SeedsChangeAtUtcDayAndMondayBoundaries()
        {
            var sunday = new DateTime(2026, 10, 25, 23, 59, 0, DateTimeKind.Utc);
            Assert.AreEqual(ChallengeRules.DailySeed(sunday), ChallengeRules.DailySeed(sunday.Date));
            Assert.AreEqual(ChallengeRules.DailySeed(sunday) + 1, ChallengeRules.DailySeed(sunday.AddMinutes(1)));
            Assert.AreEqual(ChallengeRules.WeeklySeed(sunday) + 1, ChallengeRules.WeeklySeed(sunday.AddDays(1)));
        }
        [Test] public void SameSeedProducesTheSameOrdersAndCampaignRemainsSeparate()
        {
            var catalog = IngredientCatalog.Load(); var campaign = new RestaurantSimulation(catalog).State;
            string original = JsonUtility.ToJson(campaign);
            var a = new RestaurantSimulation(catalog, ChallengeRules.Create(catalog, ChallengeRules.Rush, 123));
            var b = new RestaurantSimulation(catalog, ChallengeRules.Create(catalog, ChallengeRules.Rush, 123));
            for (int i = 0; i < 10; i++) Assert.AreEqual(JsonUtility.ToJson(a.Customers.Create()), JsonUtility.ToJson(b.Customers.Create()));
            Assert.AreEqual(original, JsonUtility.ToJson(campaign));
            SaveMigration.Migrate(a.State); Assert.DoesNotThrow(() => SaveRepository.Validate(a.State));
        }
        [TestCase(0, "spice5")] [TestCase(1, "vegetable")] [TestCase(2, "cash")]
        public void WeeklyRulesApplyToBothOrdersAndPurchases(int seed, string rule)
        {
            var catalog = IngredientCatalog.Load(); var state = ChallengeRules.Create(catalog, ChallengeRules.Weekly, seed);
            var sim = new RestaurantSimulation(catalog, state);
            Assert.AreEqual(rule, state.challengeRule);
            for (int i = 0; i < 12; i++)
            {
                var order = sim.Customers.Create(); Assert.NotNull(order);
                if (rule == "spice5") Assert.AreEqual(5, order.spice);
                if (rule == "vegetable") Assert.IsTrue(order.requested.All(x => ChallengeRules.AllowsIngredient(state, catalog.Get(x))));
                if (rule == "cash") { order.billFen = 2400; sim.Customers.PreparePayment(order); Assert.AreEqual(PaymentMethod.Cash, order.payment); Assert.Greater(order.tenderedFen, 0); }
            }
            if (rule == "vegetable")
            {
                Assert.IsFalse(sim.Purchase("beef_slice", 1)); sim.RestockAll(90); Assert.AreEqual(0, sim.StockFor("beef_slice").quantity);
            }
        }
        [Test] public void RecordingAChallengeIsIdempotentAndNeverReplacesABetterScore()
        {
            var catalog = IngredientCatalog.Load(); var campaign = new RestaurantSimulation(catalog).State;
            long money = campaign.moneyFen;
            var challenge = ChallengeRules.Create(catalog, ChallengeRules.Rush, 123);
            challenge.report.served = 10; challenge.report.excellent = 4; challenge.report.bestCombo = 3; challenge.report.lost = 1;
            ChallengeRules.Record(campaign, challenge); Assert.IsEmpty(campaign.challengeRecords);
            challenge.phase = DayPhase.Report; ChallengeRules.Record(campaign, challenge); ChallengeRules.Record(campaign, challenge);
            Assert.AreEqual(1, campaign.challengeRecords.Count); Assert.AreEqual(1100, campaign.challengeRecords[0].score);
            challenge.report.served = 1; ChallengeRules.Record(campaign, challenge); Assert.AreEqual(1100, campaign.challengeRecords[0].score);
            Assert.AreEqual(money, campaign.moneyFen); Assert.AreEqual(1, campaign.day); Assert.IsFalse(campaign.tutorialComplete);
        }
    }
}
