using System;
using System.Linq;
using MalatangStory.Ingredients;
namespace MalatangStory.Game
{
    public static class CollectionRewards
    {
        public static int Percent(GameState state, IngredientCatalog catalog)
        {
            int found = catalog.ingredients.Count(x => state.ingredients.unlockedIds.Contains(x.id) || state.ingredients.rareObtainedIds.Contains(x.id));
            return catalog.ingredients.Length == 0 ? 0 : found * 100 / catalog.ingredients.Length;
        }
        public static int UnlockedPercent(GameState state, IngredientCatalog catalog) => Math.Max(Percent(state, catalog), state.collectionRewardTier * 25);
        public static int Required(string id) => id == "apron" ? 25 : id == "bowl" ? 50 : id == "pennant" ? 75 : id == "title" ? 100 : 101;
        public static string Name(string id) => id == "apron" ? "金の刺繍エプロン" : id == "bowl" ? "翡翠の器" : id == "pennant" ? "収集家ののれん飾り" : id == "title" ? "麻辣の探究者" : "";
    }
    public sealed partial class RestaurantSimulation
    {
        public bool ChooseCollectionReward(string id, bool enabled)
        {
            if (!CanManage || enabled && CollectionRewards.UnlockedPercent(State, Catalog) < CollectionRewards.Required(id)) return false;
            State.collectionRewardTier = Math.Max(State.collectionRewardTier, CollectionRewards.Percent(State, Catalog) / 25);
            switch (id)
            {
                case "apron": State.chef.embroidery = enabled; break;
                case "bowl": State.collectionBowl = enabled; break;
                case "pennant": State.collectionPennant = enabled; break;
                case "title": State.collectionTitle = enabled; break;
                default: return false;
            }
            Notify(CollectionRewards.Name(id) + (enabled ? "を使います。" : "を外しました。")); Save(); return true;
        }
    }
}
