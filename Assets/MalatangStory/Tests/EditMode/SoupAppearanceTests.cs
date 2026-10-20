using System;
using System.Collections.Generic;
using MalatangStory.Game;
using NUnit.Framework;

/// <summary>Unity描画を必要としないスープと薬味の外観計算を検証します。</summary>
namespace MalatangStory.Ingredients.Tests
{
    /// <summary>六種の識別、辛さの上限、薬味の定義と配置の再現性を保証します。</summary>
    public sealed class SoupAppearanceTests
    {
        /// <summary>全スープに固有の濁りと安全な描画パラメータが存在します。</summary>
        [Test]
        public void EverySoupHasDistinctBoundedAppearance()
        {
            HashSet<float> cloudiness = new HashSet<float>();
            Assert.AreEqual(6, Content.Soups.Length);
            foreach (SoupDefinition soup in Content.Soups)
            {
                SoupAppearanceParameters value = SoupAppearance.ForSoup(soup.Id, 2);
                Assert.AreEqual(soup.Id, value.SoupId);
                Assert.That(value.Cloudiness, Is.InRange(0f, 1f));
                Assert.That(value.OilCoverage, Is.InRange(0f, 1f));
                Assert.That(value.Smoothness, Is.InRange(0f, .65f));
                Assert.That(value.SteamIntensity, Is.InRange(0f, 1f));
                Assert.IsTrue(cloudiness.Add(value.Cloudiness), soup.Id);
            }
            Assert.Greater(SoupAppearance.ForSoup("baitang", 0).Cloudiness, SoupAppearance.ForSoup("mushroom", 0).Cloudiness);
            Assert.Less(SoupAppearance.ForSoup("dry", 0).SteamIntensity, SoupAppearance.ForSoup("mala", 0).SteamIntensity);
        }

        /// <summary>辛さは0〜5に正規化され、増加すると油膜と油玉が増えます。</summary>
        [TestCase("mala")]
        [TestCase("baitang")]
        [TestCase("tomato")]
        [TestCase("sour")]
        [TestCase("mushroom")]
        [TestCase("dry")]
        public void SpiceMonotonicallyIncreasesOilAndClampsAtLimits(string id)
        {
            SoupAppearanceParameters mild = SoupAppearance.ForSoup(id, int.MinValue);
            SoupAppearanceParameters hot = SoupAppearance.ForSoup(id, int.MaxValue);
            Assert.AreEqual(0, mild.Spice);
            Assert.AreEqual(5, hot.Spice);
            Assert.AreEqual(SoupAppearance.ForSoup(id, 5).OilCoverage, hot.OilCoverage);
            float previousCoverage = mild.OilCoverage;
            int previousDrops = mild.OilDrops;
            for (int spice = 1; spice <= 5; spice++)
            {
                SoupAppearanceParameters value = SoupAppearance.ForSoup(id, spice);
                Assert.Greater(value.OilCoverage, previousCoverage);
                Assert.Greater(value.OilDrops, previousDrops);
                Assert.LessOrEqual(value.OilCoverage, 1f);
                previousCoverage = value.OilCoverage; previousDrops = value.OilDrops;
            }
        }

        /// <summary>全薬味に固有の形状があり、薬味なしを勝手に補いません。</summary>
        [Test]
        public void EveryDefinedCondimentHasExplicitGeometry()
        {
            HashSet<GarnishShape> shapes = new HashSet<GarnishShape>();
            foreach (CondimentDefinition condiment in Content.Condiments)
            {
                GarnishAppearanceParameters value = SoupAppearance.ForGarnish(condiment.Id);
                Assert.IsTrue(shapes.Add(value.Shape), condiment.Id);
                Assert.That(value.Count, Is.InRange(1, 24));
                Assert.That(value.Size, Is.InRange(.001f, .1f));
            }
            Assert.Throws<ArgumentException>(() => SoupAppearance.ForGarnish("unknown"));
            Assert.Throws<ArgumentException>(() => SoupAppearance.ForGarnish(null));
        }

        /// <summary>同一シードから同一配置が得られ、全粒は指定半径内に収まります。</summary>
        [Test]
        public void GarnishScatterIsDeterministicAndStaysInsideDisc()
        {
            Random first = new Random(1234), second = new Random(1234), other = new Random(5678);
            bool differs = false;
            for (int index = 0; index < 128; index++)
            {
                GarnishPlacement a = SoupAppearance.Scatter(first, .38f);
                GarnishPlacement b = SoupAppearance.Scatter(second, .38f);
                GarnishPlacement c = SoupAppearance.Scatter(other, .38f);
                Assert.AreEqual(a.X, b.X); Assert.AreEqual(a.Z, b.Z);
                Assert.AreEqual(a.Angle, b.Angle); Assert.AreEqual(a.Scale, b.Scale);
                Assert.LessOrEqual(a.X * a.X + a.Z * a.Z, .38f * .38f + .000001f);
                Assert.That(a.Scale, Is.InRange(.72f, 1.27f));
                Assert.That(a.Angle, Is.InRange(0f, 360f));
                differs |= a.X != c.X || a.Z != c.Z;
            }
            Assert.IsTrue(differs);
        }

        /// <summary>汁なしと注ぎ途中の狭い液面でも、薬味の外形全体が指定半径に収まります。</summary>
        [TestCase(.44f)]
        [TestCase(.32f)]
        [TestCase(.08f)]
        [TestCase(.005f)]
        public void GarnishFootprintsFitInsideEverySurfaceRadius(float radius)
        {
            Random first = new Random(19), second = new Random(19);
            foreach (CondimentDefinition condiment in Content.Condiments)
            {
                GarnishAppearanceParameters appearance = SoupAppearance.ForGarnish(condiment.Id);
                for (int index = 0; index < 64; index++)
                {
                    GarnishPlacement a = SoupAppearance.ScatterGarnish(first, radius, appearance);
                    GarnishPlacement b = SoupAppearance.ScatterGarnish(second, radius, appearance);
                    double center = Math.Sqrt((double)a.X * a.X + (double)a.Z * a.Z);
                    Assert.LessOrEqual(center + SoupAppearance.GarnishFootprint(appearance, a.Scale), radius + .000001f, condiment.Id);
                    Assert.AreEqual(a.X, b.X); Assert.AreEqual(a.Z, b.Z); Assert.AreEqual(a.Scale, b.Scale);
                    Assert.Greater(a.Scale, 0f);
                }
            }
        }

        /// <summary>不正半径は拒否し、半径ゼロは有限な原点配置になります。</summary>
        [Test]
        public void ScatterRejectsInvalidInputsAndAllowsZeroRadius()
        {
            Random random = new Random(1);
            Assert.Throws<ArgumentNullException>(() => SoupAppearance.Scatter(null, .4f));
            Assert.Throws<ArgumentOutOfRangeException>(() => SoupAppearance.Scatter(random, -.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => SoupAppearance.Scatter(random, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => SoupAppearance.Scatter(random, float.PositiveInfinity));
            GarnishPlacement origin = SoupAppearance.Scatter(random, 0f);
            Assert.AreEqual(0f, origin.X); Assert.AreEqual(0f, origin.Z);
        }
    }
}
