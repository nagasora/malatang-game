using System;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class CustomerWishesTests
    {
        [Test] public void NoodleAndColourRequestsAreEvaluatedAsFoodConstraints()
        {
            var catalog = IngredientCatalog.Load();
            var noodles = catalog.ingredients.Where(x => x.category == "noodle" && !x.isRare).Take(2).Select(x => x.id).ToList();
            var order = new CustomerOrder { wish = "noodles", selected = noodles };
            Assert.IsTrue(CustomerWishes.Fulfilled(order, catalog));
            order.selected = new System.Collections.Generic.List<string> { noodles[0], noodles[0] };
            Assert.IsFalse(CustomerWishes.Fulfilled(order, catalog));
            order.wish = "photogenic"; order.selected = new System.Collections.Generic.List<string> { "bok_choy", "cherry_tomato", "enoki" };
            Assert.IsTrue(CustomerWishes.Fulfilled(order, catalog)); order.selected.RemoveAt(0); Assert.IsFalse(CustomerWishes.Fulfilled(order, catalog));
        }
        [Test] public void AutoPickCanSatisfyTwoNoodlesAndPhotogenicRequests()
        {
            var catalog = IngredientCatalog.Load();
            foreach (string wish in new[] { "noodles", "photogenic" })
            {
                var order = new CustomerOrder { wish = wish, omakase = true, targetGrams = 420, likeTexture = -1 };
                order.selected = Omakase.AutoPick(order, catalog.ingredients, id => 10);
                Assert.IsTrue(CustomerWishes.Fulfilled(order, catalog), wish);
            }
        }
        [Test] public void RegularMoodChangesTheOrderWithoutRewritingTheirFavourite()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load()); sim.State.shopLevel = 4; sim.State.tutorialComplete = true; sim.State.day = 5;
            var regular = new RegularCustomer { favoriteSpice = 2, lastVisitDay = 1, concerned = true };
            var order = new CustomerOrder();
            CustomerWishes.Assign(sim.State, order, regular, new Random(123), sim.Catalog);
            Assert.AreEqual(3, order.spice); Assert.AreEqual(2, regular.favoriteSpice); Assert.AreEqual("stronger", order.wish);
            Assert.IsNotEmpty(order.returnNote);
            sim.State.day = 11; CustomerWishes.Assign(sim.State, order, regular, new Random(123), sim.Catalog);
            Assert.AreEqual(1, order.spice); Assert.AreEqual(2, regular.favoriteSpice); Assert.AreEqual("mild", order.wish);
        }
        [Test] public void RegularConversationIsOncePerChapterAndIntroductionIsPersistent()
        {
            var sim = new RestaurantSimulation(IngredientCatalog.Load()); sim.State.day = 10;
            var regular = new RegularCustomer { id = "r", name = "甲", storyIndex = 2, affinity = 50, lastVisitDay = 1 };
            sim.State.regulars.Add(regular);
            Assert.IsTrue(sim.ReplyToRegular("r", true)); Assert.AreEqual(55, regular.affinity);
            Assert.IsFalse(sim.ReplyToRegular("r", true)); Assert.AreEqual(55, regular.affinity);
            Assert.IsTrue(regular.introducedFriend); Assert.AreEqual(1, sim.State.introducedArchetypes.Count);
            Assert.IsTrue(sim.AskAfterRegular("r")); Assert.IsFalse(sim.AskAfterRegular("r")); Assert.AreEqual(56, regular.affinity);
            sim.State.day++; Assert.IsFalse(sim.AskAfterRegular("r"));
        }
    }
}
