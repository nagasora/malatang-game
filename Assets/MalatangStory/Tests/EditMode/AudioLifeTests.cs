using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class AudioLifeTests
    {
        [TestCase(AmbientLoop.Birds)] [TestCase(AmbientLoop.Insects)]
        public void OutdoorAmbienceHasDeterministicFiniteAudibleSamples(AmbientLoop kind)
        {
            float[] samples = ProceduralAudioSynth.Ambient(kind, 8000);
            Assert.IsTrue(samples.Any(x => System.Math.Abs(x) > .01f)); Assert.IsTrue(samples.All(x => float.IsFinite(x) && System.Math.Abs(x) <= 1));
            CollectionAssert.AreEqual(samples, ProceduralAudioSynth.Ambient(kind, 8000));
        }
        [TestCase(Sfx.Slurp)] [TestCase(Sfx.BicycleBell)]
        public void NewActionsProduceFiniteShortEffects(Sfx kind)
        {
            float[] samples = ProceduralAudioSynth.Effect(kind, 8000);
            Assert.IsTrue(samples.Any(x => System.Math.Abs(x) > .01f)); Assert.IsTrue(samples.All(x => float.IsFinite(x) && System.Math.Abs(x) <= 1));
        }
        [TestCase(0f, EatingStage.Pick)] [TestCase(.8f, EatingStage.Lift)] [TestCase(1.5f, EatingStage.Chew)] [TestCase(2.6f, EatingStage.Sip)] [TestCase(3.5f, EatingStage.Pick)]
        public void MealStagesUseTheSameClockAsTheBowl(float seconds, EatingStage stage) => Assert.AreEqual(stage, CookingFeel.MealStage(seconds));
    }
}
