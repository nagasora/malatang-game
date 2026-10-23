using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

/// <summary>食事後の残りを、会計や品質を変更せずに判定します。</summary>
namespace MalatangStory.Game
{
    /// <summary>具材一個の残った位置と理由です。同じ具材の複数個を区別します。</summary>
    [Serializable] public sealed class LeftoverIngredient
    {
        public int index;
        public string ingredientId = "";
        public string reason = "";
    }

    /// <summary>食事後の具材とスープの残量です。</summary>
    [Serializable] public sealed class MealLeftoverResult
    {
        public List<LeftoverIngredient> ingredients = new List<LeftoverIngredient>();
        public float soupRemaining;
        public string soupReason = "";
        public bool FinishedFood => ingredients.Count == 0;
        public bool FinishedSoup => soupRemaining <= 0f;
    }

    /// <summary>片付けるまで保持する、実際の席と食後の器の記録です。</summary>
    [Serializable] public sealed class MealRemains
    {
        public string orderId = "";
        public int seat = -1;
        public string soup = "mala";
        public int spice;
        public List<string> selected = new List<string>();
        public List<string> condiments = new List<string>();
        public MealLeftoverResult leftovers = new MealLeftoverResult();
        public List<GarnishStroke> garnishStrokes = new List<GarnishStroke>();
        public long tipFen;
        public string bowlStyle = "";
    }

    /// <summary>料理の状態と客の好みから残食を決定する純関数群です。</summary>
    public static class MealLeftovers
    {
        public const float SmallSoupRemaining = .12f;
        public const float LargeSoupRemaining = .65f;

        /// <summary>生・過煮・苦手・食事制限・低品質時の未注文具材を個別に判定します。</summary>
        public static MealLeftoverResult Evaluate(CustomerOrder order, IngredientCatalog catalog)
        {
            MealLeftoverResult result = new MealLeftoverResult();
            CustomerArchetype archetype = Content.Archetype(order.archetype);
            Dictionary<string, int> requested = new Dictionary<string, int>();
            foreach (string id in order.requested)
            {
                requested.TryGetValue(id, out int count);
                requested[id] = count + 1;
            }
            for (int index = 0; index < order.selected.Count; index++)
            {
                string id = order.selected[index];
                string category = catalog.Get(id).category;
                float doneness = QualityEvaluator.Doneness(order, index, catalog);
                requested.TryGetValue(id, out int count);
                bool extra = count <= 0;
                if (!extra) requested[id] = count - 1;
                string reason = doneness < .6f ? "raw" : doneness > 2f ? "overcooked"
                    : order.omakase && !string.IsNullOrEmpty(order.avoidCategory) && category == order.avoidCategory ? "avoid"
                    : Array.IndexOf(archetype.ForbiddenCategories, category) >= 0 ? "forbidden"
                    : order.quality < 60f && !order.omakase && extra ? "unordered" : "";
                if (reason.Length > 0)
                    result.ingredients.Add(new LeftoverIngredient { index = index, ingredientId = id, reason = reason });
            }
            result.soupReason = order.chosenSoup != order.soup ? "soup_mismatch"
                : Math.Abs(order.chosenSpice - order.spice) >= 2 ? "spice"
                : order.dislikedCondiments.Any(id => order.condiments.Contains(id)
                    || (id == Content.ChiliOil && order.chosenSpice > 0 && order.spice == 0)) ? "condiment" : "";
            result.soupRemaining = result.soupReason.Length > 0 ? LargeSoupRemaining
                : order.quality >= 90f && QualityEvaluator.SoupScore(order) >= 90f ? 0f : SmallSoupRemaining;
            return result;
        }

        /// <summary>保存と統計で共通の理由IDを日本語に変換します。</summary>
        public static string ReasonName(string reason)
        {
            switch (reason)
            {
                case "raw": return "固かった";
                case "overcooked": return "煮えすぎ";
                case "avoid": return "苦手";
                case "forbidden": return "食べられない";
                case "unordered": return "頼んでいない";
                case "soup_mismatch": return "スープ違い";
                case "spice": return "辛さ違い";
                case "condiment": return "苦手な薬味";
                default: return "なし";
            }
        }

        /// <summary>同数なら理由ID順として、最多理由を安定して表示します。</summary>
        public static string MostCommonReason(List<NamedCount> reasons)
        {
            NamedCount most = reasons.OrderByDescending(entry => entry.count).ThenBy(entry => entry.id, StringComparer.Ordinal).FirstOrDefault();
            return most == null ? "なし" : ReasonName(most.id) + "（" + most.count + "）";
        }

        /// <summary>具材の理由IDが既知かを返します。</summary>
        public static bool IsFoodReason(string reason) => reason == "raw" || reason == "overcooked" || reason == "avoid" || reason == "forbidden" || reason == "unordered";

        /// <summary>残った具材と理由を、表示用の短い日本語にします。</summary>
        public static string Describe(MealLeftoverResult result, IngredientCatalog catalog)
        {
            return result.FinishedFood ? "完食！" : "残し: " + string.Join("、", result.ingredients.Select(x => catalog.Get(x.ingredientId).displayName + "（" + ReasonName(x.reason) + "）"));
        }

        /// <summary>最多の具材と理由を同数時はID順で返します。</summary>
        public static string DailyMostLeft(DailyReport report, IngredientCatalog catalog)
        {
            var ingredient = report.leftovers.GroupBy(x => x.id.Split('|')[0])
                .OrderByDescending(g => g.Sum(x => (long)x.count)).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault();
            if (ingredient == null) return "食べ残しなし";
            NamedCount reason = ingredient.OrderByDescending(x => x.count).ThenBy(x => x.id, StringComparer.Ordinal).First();
            return catalog.Get(ingredient.Key).displayName + "（" + ReasonName(reason.id.Split('|')[1]) + "） " + ingredient.Sum(x => (long)x.count) + "個";
        }

        /// <summary>欠損・非有限値・重複位置を正規化し、残った具材の実数を保持します。</summary>
        public static void Normalize(MealLeftoverResult result, IngredientCatalog catalog)
        {
            result.ingredients ??= new List<LeftoverIngredient>();
            HashSet<int> indices = new HashSet<int>();
            result.ingredients.RemoveAll(item => item == null || item.index < 0 || !catalog.ingredients.Any(x => x.id == item.ingredientId) || !IsFoodReason(item.reason) || !indices.Add(item.index));
            foreach (LeftoverIngredient item in result.ingredients) item.reason ??= "";
            result.soupReason ??= "";
            result.soupRemaining = float.IsNaN(result.soupRemaining) || float.IsInfinity(result.soupRemaining)
                ? SmallSoupRemaining : Math.Max(0f, Math.Min(1f, result.soupRemaining));
        }
    }
}
