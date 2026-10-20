using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MalatangStory.Ingredients
{
    [Serializable]
    public sealed class IngredientDefinition
    {
        public string id, displayName, category, categoryLabel, visualKey, modelResourcePath;
        /// <summary>通常の売上解禁ではなく朝市でのみ入手する食材か。</summary>
        public bool isRare;
        public float kcalPer100g, proteinPer100g, fatPer100g, carbohydratePer100g;
        public float portionGrams, purchasePricePerPortion, cookingSeconds;
        public int freshnessDays, popularity, unlockRevenueYuan;
        public float Calories(float grams) => kcalPer100g * grams / 100f;
    }

    [Serializable]
    public sealed class IngredientCatalog
    {
        public int schemaVersion;
        public string source;
        public IngredientDefinition[] ingredients;

        public static IngredientCatalog Load()
        {
            var asset = Resources.Load<TextAsset>("IngredientCatalog");
            if (asset == null) throw new InvalidOperationException("IngredientCatalog resource is missing.");
            return FromJson(asset.text);
        }

        public static IngredientCatalog FromJson(string json)
        {
            var catalog = JsonUtility.FromJson<IngredientCatalog>(json);
            if (catalog == null || catalog.schemaVersion != 1 || catalog.ingredients == null)
                throw new InvalidOperationException("Unsupported ingredient catalog.");
            var ids = new HashSet<string>();
            foreach (var item in catalog.ingredients)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id)
                    || string.IsNullOrWhiteSpace(item.displayName) || string.IsNullOrWhiteSpace(item.category)
                    || !Positive(item.portionGrams) || !Positive(item.cookingSeconds)
                    || !Nonnegative(item.purchasePricePerPortion) || item.unlockRevenueYuan < 0
                    || !Nonnegative(item.kcalPer100g) || !Nonnegative(item.proteinPer100g)
                    || !Nonnegative(item.fatPer100g) || !Nonnegative(item.carbohydratePer100g))
                    throw new InvalidOperationException("Invalid ingredient definition.");
            }
            return catalog;
        }

        private static bool Nonnegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
        private static bool Positive(float value) => Nonnegative(value) && value > 0;
        public IngredientDefinition Get(string id) => ingredients.First(x => x.id == id);
        public IEnumerable<IngredientDefinition> ByCategory(string category) => ingredients.Where(x => x.category == category);
    }
}
