using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MalatangStory.Ingredients.Tests
{
    public class IngredientTests
    {
        [Test]
        public void CatalogContainsAllSourceRowsAndEightCategories()
        {
            var catalog = IngredientCatalog.Load();
            Assert.AreEqual(75, catalog.ingredients.Length);
            Assert.AreEqual(69, catalog.ingredients.Count(x => !x.isRare));
            Assert.AreEqual(6, catalog.ingredients.Count(x => x.isRare));
            Assert.AreEqual(8, catalog.ingredients.Select(x => x.category).Distinct().Count());
            Assert.AreEqual(218, catalog.Get("yuba").kcalPer100g);
            Assert.AreEqual(109, catalog.Get("yuba").Calories(50));
        }

        [Test]
        public void RevenueUnlocksPersistWithoutDuplicateSalesOrRefundedRevenue()
        {
            var catalog = IngredientCatalog.Load();
            var unlocks = new IngredientUnlocks(catalog);
            Assert.AreEqual(10, unlocks.Available.Count());
            unlocks.SettleOrder("cancelled", 30000, 30000);
            Assert.IsFalse(unlocks.IsUnlocked("quail_egg"));
            unlocks.SettleOrder("first", 29999);
            Assert.IsFalse(unlocks.IsUnlocked("quail_egg"));
            Assert.AreEqual(1, unlocks.SettleOrder("second", 1).Count);
            Assert.IsTrue(unlocks.IsUnlocked("quail_egg"));
            unlocks.SettleOrder("second", 100000);
            Assert.AreEqual(30000, unlocks.Progress.settledRevenueFen);
            var loaded = new IngredientUnlocks(catalog,
                JsonUtility.FromJson<IngredientProgress>(JsonUtility.ToJson(unlocks.Progress)));
            Assert.IsTrue(loaded.IsUnlocked("quail_egg"));
            Assert.AreEqual(0, loaded.SettleOrder("first", 29999).Count);
            loaded.SettleOrder("large", 2400000);
            Assert.AreEqual(69, loaded.Available.Count());
            Assert.IsNull(loaded.Next);
        }
    }
}
