using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class PreparationTests
    {
        private RestaurantSimulation Sim()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.tutorialComplete = true; return sim;
        }
        [Test] public void PreparationChargesOnceRewardsAccurateCutsAndSurvivesJsonRoundtrip()
        {
            var sim = Sim(); long money = sim.State.moneyFen;
            Assert.IsFalse(sim.BeginPreparation("mala", 0, 0, 0));
            Assert.IsTrue(sim.BeginPreparation("mala", 3, 2, 1));
            Assert.AreEqual(money - 240, sim.State.moneyFen);
            Assert.IsFalse(sim.BeginPreparation("mala", 3, 2, 1));
            sim.TickPreparation(.5f); Assert.IsTrue(sim.ChopPreparation()); Assert.IsFalse(sim.ChopPreparation());
            for (int i = 0; i < 9; i++) { sim.TickPreparation(1); Assert.IsTrue(sim.ChopPreparation()); }
            GameState loaded = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(sim.State));
            SaveMigration.Migrate(loaded); SaveRepository.Validate(loaded);
            var resumed = new RestaurantSimulation(IngredientCatalog.Load(), loaded);
            Assert.IsTrue(resumed.FinishPreparation()); Assert.AreEqual(100, loaded.preparation.quality, .001f);
            Assert.IsFalse(resumed.FinishPreparation()); Assert.IsFalse(resumed.FinishPreparation(true));
            Assert.IsTrue(resumed.RecordHouseSoup("<b>家の味</b>"));
            Assert.AreEqual(1, loaded.houseSoups.Count); Assert.IsFalse(loaded.houseSoups[0].name.Contains("<"));
            Assert.Greater(PreparationRules.Bonus(loaded, "mala"), PreparationRules.Bonus(loaded, "tomato"));
        }
        [Test] public void SkippingPreparationHasNoQualityBonusAndNeverErasesStartedMaterials()
        {
            var sim = Sim(); Assert.IsTrue(sim.FinishPreparation(true));
            Assert.AreEqual(50, sim.State.preparation.quality); Assert.AreEqual(0, PreparationRules.Bonus(sim.State, "mala"));
            Assert.IsFalse(sim.RecordHouseSoup("無料スープ"));
            Assert.IsTrue(sim.BeginDay()); Assert.AreEqual(50, sim.State.preparation.quality);
        }
        [TestCase(.34f, false)] [TestCase(.35f, true)] [TestCase(.65f, true)] [TestCase(.66f, false)]
        public void CuttingUsesTheMiddleOfEachBeat(float time, bool accurate) => Assert.AreEqual(accurate, PreparationRules.AccurateCut(time));
        [Test] public void InvalidPreparationAndHouseSoupFieldsAreBounded()
        {
            var sim = Sim(); sim.State.preparation.seconds = float.PositiveInfinity;
            sim.State.preparation.accurateCuts = 99; sim.State.preparation.cuts = 2;
            sim.State.houseSoups.Add(new HouseSoup { id = "x", name = "味", soup = "unknown" });
            SaveMigration.Migrate(sim.State);
            Assert.AreEqual(0, sim.State.preparation.seconds); Assert.AreEqual(2, sim.State.preparation.accurateCuts);
            Assert.IsEmpty(sim.State.houseSoups);
        }
    }
}
