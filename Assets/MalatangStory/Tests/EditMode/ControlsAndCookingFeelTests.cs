using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

namespace MalatangStory.Ingredients.Tests
{
    public sealed class ControlsAndCookingFeelTests
    {
        [Test] public void MovementReachesFullSpeedInTwelveFramesAndStopsInEight()
        {
            float speed = 0;
            for (int i = 0; i < 12; i++) speed = MovementFeel.Step(speed, 3.8f, .01f, 3.8f);
            Assert.AreEqual(3.8f, speed, .0001f);
            for (int i = 0; i < 8; i++) speed = MovementFeel.Step(speed, 0, .01f, 3.8f);
            Assert.AreEqual(0, speed, .0001f);
        }
        [Test] public void MovementDoesNotOvershootAndRecoversFromInvalidVelocity()
        {
            Assert.AreEqual(.2f, MovementFeel.Step(0, .2f, .1f, 3.8f));
            Assert.AreEqual(0, MovementFeel.Step(float.NaN, 0, .01f, 3.8f));
            Assert.Less(MovementFeel.Step(3.8f, -3.8f, .01f, 3.8f), 3.8f);
        }
        [Test] public void ForwardStationWinsWhenStandingBetweenTwoStations()
        {
            Assert.Less(MovementFeel.TargetScore(1.4f, 1), MovementFeel.TargetScore(1.1f, -1));
            Assert.Less(MovementFeel.TargetScore(.2f, -1), MovementFeel.TargetScore(1.7f, 1));
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
        public void OldSavesGainDefaultControlsWithoutLosingProgress(int version)
        {
            var state = new RestaurantSimulation(IngredientCatalog.Load()).State;
            state.saveVersion = version; state.moneyFen = 54321;
            state.leftHanded = true; state.reducedMotion = true; state.gaugeMode = 2;
            SaveMigration.Migrate(state);
            Assert.AreEqual(GameState.CurrentVersion, state.saveVersion);
            Assert.AreEqual(54321, state.moneyFen);
            Assert.IsFalse(state.leftHanded); Assert.IsFalse(state.reducedMotion); Assert.AreEqual(0, state.gaugeMode);
            Assert.AreEqual("E", state.interactKey); Assert.AreEqual("T", state.buildRotateKey);
            Assert.DoesNotThrow(() => SaveRepository.Validate(state));
        }
        [TestCase(0f, false)] [TestCase(.85f, true)] [TestCase(1.25f, true)] [TestCase(1.5f, false)]
        public void CookingCuesFollowTheDonenessBoundaries(float value, bool ready)
        {
            CookingCue cue = CookingFeel.Cues(value);
            Assert.AreEqual(ready, cue.Ready);
            Assert.That(cue.ColourBlend, Is.InRange(0f, 1f)); Assert.That(cue.Scale, Is.InRange(.88f, 1f));
            Assert.GreaterOrEqual(cue.Bubbles, CookingFeel.Cues(0).Bubbles);
            Assert.GreaterOrEqual(cue.Steam, CookingFeel.Cues(0).Steam);
        }
        [TestCase(.249f, false)] [TestCase(.25f, true)] [TestCase(.5f, true)] [TestCase(.501f, false)]
        public void RhythmIncludesOnlyTheIntendedTimingWindow(float interval, bool success)
            => Assert.AreEqual(success, CookingFeel.OnRhythm(interval));
        [Test] public void HandRhythmHasABonusAndTheButtonKeepsItsOriginalQuality()
        {
            var button = BowlFixture(); var hand = BowlFixture();
            button.Shake("feel"); button.Shake("feel"); button.Shake("feel");
            hand.ShakeByHand("feel", 1); hand.ShakeByHand("feel", 1.35f); hand.ShakeByHand("feel", 1.7f);
            Assert.AreEqual(0, button.Find("feel").handRhythm); Assert.AreEqual(2, hand.Find("feel").handRhythm);
            Assert.IsTrue(button.Finish("feel")); Assert.IsTrue(hand.Finish("feel"));
            Assert.AreEqual(button.Find("feel").quality + 1, hand.Find("feel").quality, .001f);
        }
        [Test] public void ExpertBowlEarnsExtraExperienceOnlyOnce()
        {
            var normal = BowlFixture(); var expert = BowlFixture();
            normal.Shake("feel"); normal.Shake("feel"); normal.Shake("feel");
            expert.Shake("feel"); expert.Shake("feel"); expert.Shake("feel");
            expert.State.gaugeMode = 2; expert.Find("feel").usedVisibleGauge = false;
            normal.Finish("feel"); expert.Finish("feel");
            Assert.IsTrue(expert.Find("feel").expertBowl);
            normal.Serve("feel"); expert.Serve("feel");
            Assert.AreEqual(normal.State.experience + 3, expert.State.experience);
            int experience = expert.State.experience;
            Assert.IsFalse(expert.Serve("feel")); Assert.AreEqual(experience, expert.State.experience);
        }
        private static RestaurantSimulation BowlFixture()
        {
            var simulation = new RestaurantSimulation(IngredientCatalog.Load()); simulation.State.tutorialComplete = true;
            simulation.State.orders.Add(new CustomerOrder { id = "feel", customerName = "テストさん", type = "会社員", stage = OrderStage.Drained, takeout = true,
                requested = new System.Collections.Generic.List<string> { "enoki" }, selected = new System.Collections.Generic.List<string> { "enoki" },
                dropTimes = new System.Collections.Generic.List<float> { 0 }, heatSeconds = simulation.Catalog.Get("enoki").cookingSeconds,
                chosenSoup = "mala", soup = "mala", soupMl = 340, spice = 1, chosenSpice = 1, elapsed = 70, patience = 100 });
            return simulation;
        }
        [TestCase(.2f, EatingStage.Pick)] [TestCase(1f, EatingStage.Lift)] [TestCase(2f, EatingStage.Chew)] [TestCase(3f, EatingStage.Sip)]
        public void EatingHasDistinctRepeatableStages(float seconds, EatingStage expected)
            => Assert.AreEqual(expected, CookingFeel.MealStage(seconds));

        [Test] public void PaintingChangesPlacementWithoutAddingMoreCondiments()
        {
            var simulation = BowlFixture(); var order = simulation.Find("feel"); order.stage = OrderStage.Finishing;
            order.condiments.Add("garlic");
            Assert.IsTrue(simulation.PaintCondiment(order.id, "garlic", 2, 2));
            Assert.AreEqual(1, order.condiments.Count);
            Assert.LessOrEqual(order.garnishStrokes[0].x * order.garnishStrokes[0].x + order.garnishStrokes[0].z * order.garnishStrokes[0].z, 1.00001f);
            Assert.IsFalse(simulation.PaintCondiment(order.id, "unknown", 0, 0));
            Assert.IsFalse(simulation.PaintCondiment(order.id, "garlic", float.NaN, 0));
            for (int i = 0; i < 100; i++) simulation.PaintCondiment(order.id, "garlic", .5f, .5f);
            Assert.AreEqual(GarnishStroke.Limit, order.garnishStrokes.Count);
            Assert.AreEqual(1, order.condiments.Count);
            var saved = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(simulation.State)); SaveMigration.Migrate(saved);
            Assert.AreEqual(GarnishStroke.Limit, saved.orders[0].garnishStrokes.Count);
        }
        [Test] public void DamagedGarnishCoordinatesAreRemovedOrClamped()
        {
            var values = new System.Collections.Generic.List<GarnishStroke>
            {
                null, new GarnishStroke { id = "garlic", x = float.PositiveInfinity },
                new GarnishStroke { id = "missing" }, new GarnishStroke { id = "garlic", x = 100, z = 100 }
            };
            GarnishStroke.Normalize(values); Assert.AreEqual(1, values.Count);
            Assert.That(values[0].x, Is.InRange(0f, 1f)); Assert.That(values[0].z, Is.InRange(0f, 1f));
        }
        [Test] public void CurrentSettingsSurviveSerializationAndNormalization()
        {
            var state = new RestaurantSimulation(IngredientCatalog.Load()).State;
            state.leftHanded = true; state.reducedMotion = true; state.snapCamera = true;
            state.gaugeMode = 2; state.textSize = 24; state.interactKey = "F"; state.buildRotateKey = "Y";
            var loaded = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(state)); SaveMigration.Migrate(loaded);
            Assert.IsTrue(loaded.leftHanded && loaded.reducedMotion && loaded.snapCamera);
            Assert.AreEqual(2, loaded.gaugeMode); Assert.AreEqual(24, loaded.textSize);
            Assert.AreEqual("F", loaded.interactKey); Assert.AreEqual("Y", loaded.buildRotateKey);
        }
    }
}
