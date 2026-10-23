using System;
using System.Collections.Generic;
using MalatangStory.Ingredients;
namespace MalatangStory.Game
{
    public static class FranchiseRules
    {
        public static GameState Create(IngredientCatalog catalog, GameState previous, string storeId)
        {
            if (previous.chapter < Chapters.All.Length || string.IsNullOrEmpty(storeId) || storeId == previous.storeId) throw new InvalidOperationException("物語の完了後に別の街で始められます。");
            var state = new RestaurantSimulation(catalog).State;
            state.storeId = PlayerText.Clean(storeId, 80); state.franchiseNumber = previous.franchiseNumber + 1;
            state.storeName = PlayerText.Clean(previous.storeName + "・のれん分け");
            state.moneyFen += Math.Max(0, Math.Min(250000, previous.moneyFen / 10));
            state.tutorialComplete = true; state.tutorialStep = RestaurantSimulation.TutorialFinished;
            state.chef = previous.chef.Clone(); state.ownedThemes = new List<string>(previous.ownedThemes); state.theme = previous.theme;
            state.collectionRewardTier = Math.Max(previous.collectionRewardTier, CollectionRewards.Percent(previous, catalog) / 25);
            state.collectionBowl = previous.collectionBowl; state.collectionPennant = previous.collectionPennant; state.collectionTitle = previous.collectionTitle;
            state.achievements = new List<string>(previous.achievements);
            return state;
        }
    }
}
