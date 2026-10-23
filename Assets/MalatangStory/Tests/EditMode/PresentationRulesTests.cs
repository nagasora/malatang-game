using System;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    public sealed class PresentationRulesTests
    {
        [TestCase(79.99f, .75f)] [TestCase(80f, 1.8f)] [TestCase(159.99f, 1.8f)]
        [TestCase(160f, .3f)] [TestCase(184f, .75f)] [TestCase(560f, 1.6f)] [TestCase(680f, .3f)] [TestCase(720f, 0f)]
        public void RushAndBreakArrivalRatesFollowTheBusinessClock(float seconds, float rate)
            => Assert.AreEqual(rate, BusinessRhythm.ArrivalRate(seconds));
        [TestCase(0L, "0元")] [TestCase(-2880L, "-28.8元")] [TestCase(2881L, "28.81元")]
        [TestCase(1090000L, "1.09万元")] [TestCase(100L, "1元")]
        public void CurrencyOmitsTrailingZerosAndAbbreviatesLargeAmounts(long value, string expected)
            => Assert.AreEqual(expected, RestaurantSimulation.Money(value));
        [Test] public void ZeroCostDisplaysADash() => Assert.AreEqual("—", RestaurantSimulation.MoneyOrDash(0));
        [Test] public void StreetLotsAreReproducibleAndNeighbouringRoofsDiffer()
        {
            for (int i = 0; i < 40; i++)
            {
                StreetLot a = StreetVisualRules.Lot(123, i), b = StreetVisualRules.Lot(123, i);
                Assert.AreEqual(a.Floors, b.Floors); Assert.AreEqual(a.Width, b.Width);
                Assert.That(a.Floors, Is.InRange(2, 5)); Assert.AreNotEqual(a.Roof, StreetVisualRules.Lot(123, i + 1).Roof);
            }
        }
        [Test] public void OilDropletsHaveManySmallParticlesAndFewLargeClusters()
        {
            var random = new Random(123); OilDroplet previous = default;
            int small = 0, large = 0, edge = 0;
            for (int i = 0; i < 1000; i++)
            {
                previous = OilDropletRules.Next(random, true, 3, previous, i > 0);
                if (previous.Radius < .02f) small++; if (previous.Radius > .045f) large++;
                float distance = (float)Math.Sqrt(previous.X * previous.X + previous.Z * previous.Z);
                if (distance > .72f) edge++;
                Assert.LessOrEqual(distance + previous.Radius * Math.Max(previous.Aspect, 1 / previous.Aspect), .961f);
            }
            Assert.Greater(small, 600); Assert.That(large, Is.InRange(1, 180)); Assert.Greater(edge, 250);
        }
        [TestCase(19, 1, -7, true)] [TestCase(30, -1, -7, false)] [TestCase(24, -1, -7, true)]
        [TestCase(19, 1, 0, false)]
        public void VehiclesStopOnlyWhenApproachingAnOccupiedCrosswalk(float x, int direction, float z, bool expected)
            => Assert.AreEqual(expected, StreetVisualRules.YieldForCrossing(x, direction, z, 0));
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void NaturalSpicePhrasesHaveAnUnambiguousMeaningWithoutDigits(int spice)
        {
            for (int variant = 0; variant < 2; variant++)
            {
                string phrase = OrderLanguage.SpicePhrase(spice, variant);
                Assert.IsFalse(phrase.Any(char.IsDigit)); Assert.IsTrue(OrderLanguage.TrySpice(phrase, out int parsed)); Assert.AreEqual(spice, parsed);
            }
        }
    }
}
