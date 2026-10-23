using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;
namespace MalatangStory.Game
{
    public static class CustomerWishes
    {
        public static readonly string[] Kinds = { "", "noodles", "photogenic", "warm", "budget", "stronger", "friend", "mild" };
        public static string Description(CustomerOrder order)
        {
            switch (order.wish)
            {
                case "noodles": return "今日は麺を多めに、二種類くらい入れてほしいな。";
                case "photogenic": return "写真に撮りたいから、三色以上の彩りでお願い！";
                case "warm": return "体を温めたいので、温かいスープをしっかりお願いします。";
                case "budget": return "予算30元で、できるだけお腹いっぱいになるものを。";
                case "mild": return "今日は辛さをいつもより控えめに。あっさりした気分なんです。";
                case "stronger": return "今日はいつもより一段辛くしてみたい！";
                case "friend": return "友達へのお土産です。持ち帰りでお願いします。";
                default: return "";
            }
        }
        public static void Assign(GameState state, CustomerOrder order, RegularCustomer regular, Random random, IngredientCatalog catalog)
        {
            if (Content.Archetype(order.archetype).Child || !state.tutorialComplete || state.shopLevel < 3 || !string.IsNullOrEmpty(state.challengeMode)) return;
            if (regular != null && regular.lastVisitDay > 0 && state.day - regular.lastVisitDay >= 3)
                order.returnNote = regular.concerned ? "気にかけてくれてありがとう。しばらく" + (regular.archetype == "student" ? "試験勉強" : "出張") + "で来られなかったんです。" : "久しぶり！やっとまた食べに来られました。";
            if (regular != null && (state.day % 11 == 0 || Seasons.Of(state.day).Id == "summer" && state.day % 4 == 0))
            { order.wish = "mild"; order.usual = false; order.spice = Math.Max(0, regular.favoriteSpice - 1); return; }
            if (regular != null && state.day % 5 == 0)
            { order.wish = "stronger"; order.usual = false; order.spice = Math.Min(5, regular.favoriteSpice + 1); order.dislikedCondiments.Remove(Content.ChiliOil); return; }
            if (regular != null && state.day % 7 == 0)
            { order.wish = "friend"; order.takeout = true; order.usual = false; return; }
            if (!order.omakase || random.NextDouble() >= .6) return;
            order.wish = new[] { "noodles", "photogenic", "warm", "budget" }[random.Next(4)];
            if (order.wish == "noodles" && state.stock.Count(x => x.quantity > 0 && state.ingredients.unlockedIds.Contains(x.ingredientId) && catalog.Get(x.ingredientId).category == "noodle") < 2) order.wish = "warm";
            if (order.wish == "budget")
            { order.budgetFen = 3000; order.targetGrams = 3000f * 100 / state.pricePer100gFen; }
            if (order.wish == "warm" && order.soup == "dry") order.soup = "mala";
        }
        public static bool Fulfilled(CustomerOrder order, IngredientCatalog catalog)
        {
            switch (order.wish)
            {
                case "noodles": return order.selected.Where(x => catalog.Get(x).category == "noodle").Distinct().Count() >= 2;
                case "photogenic": return order.selected.Select(x => FoodProfile.Of(catalog.Get(x)).Color).Distinct().Count() >= 3;
                case "warm": return order.chosenSoup != "dry" && order.soupMl >= Content.Soup(order.chosenSoup).IdealMinMl;
                case "budget": return order.paidFen <= order.budgetFen && order.selected.Sum(x => catalog.Get(x).portionGrams) >= order.targetGrams * .9f;
                case "mild":
                case "stronger": return order.chosenSpice == order.spice;
                case "friend": return order.takeout;
                default: return true;
            }
        }
        public static void ApplyBonus(CustomerOrder order, IngredientCatalog catalog)
        {
            if (string.IsNullOrEmpty(order.wish)) return;
            bool fulfilled = Fulfilled(order, catalog);
            order.report.total = fulfilled ? Math.Min(100, order.report.total + 2) : Math.Min(85, order.report.total);
            order.report.comments.Add(fulfilled ? "今日の気分にぴったりの一杯！" : "今日お願いしたことも聞いてほしかったな。");
        }
    }
}
