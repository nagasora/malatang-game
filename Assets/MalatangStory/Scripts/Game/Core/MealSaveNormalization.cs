using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

/// <summary>食後の記録を旧保存と破損値から安全に復元します。</summary>
namespace MalatangStory.Game
{
    /// <summary>描画が参照する具材位置と保存済み統計を正規化します。</summary>
    public static class MealSaveNormalization
    {
        /// <summary>非有限な進行値と未知IDを補正し、片付け済みの記録を除きます。</summary>
        public static void Normalize(GameState state, IngredientCatalog catalog)
        {
            if (state.furniture == null || state.furniture.Any(x => x == null || !Content.Furniture.Any(d => d.Id == x.defId)))
                throw new InvalidOperationException("保存データの家具が不正です。");
            HashSet<string> ids = new HashSet<string>(catalog.ingredients.Select(x => x.id));
            state.mealRemains ??= new List<MealRemains>();
            foreach (CustomerOrder order in state.orders.Where(x => x != null))
            {
                order.eatSeconds = FiniteRange(order.eatSeconds, RestaurantSimulation.EatDuration);
                order.leftovers ??= new MealLeftoverResult();
                NormalizeResult(order.leftovers, order.selected, catalog);
                if (order.mealFinished)
                {
                    order.stage = OrderStage.Served;
                    order.visualState = "leave";
                    order.pot = -1;
                }
                if (!order.mealFinished) order.leftovers = new MealLeftoverResult();
            }
            HashSet<int> seats = new HashSet<int>(DiningLayout.Seats(state).Select(x => x.SeatId));
            HashSet<int> seen = new HashSet<int>();
            state.mealRemains.RemoveAll(x => x == null || string.IsNullOrEmpty(x.orderId) || !seats.Contains(x.seat)
                || !state.dirtyTables.Contains(DiningLayout.TableOfSeat(x.seat)) || x.selected == null
                || x.selected.Count == 0 || x.selected.Any(id => !ids.Contains(id)) || !seen.Add(x.seat));
            foreach (MealRemains remains in state.mealRemains)
            {
                if (remains.bowlStyle != "jade" && remains.bowlStyle != "paper" && !Content.Themes.Any(x => x.Id == remains.bowlStyle)) remains.bowlStyle = "";
                remains.tipFen = Math.Max(0, Math.Min(1000000, remains.tipFen));
                remains.garnishStrokes = GarnishStroke.Normalize(remains.garnishStrokes);
                if (!Content.Soups.Any(x => x.Id == remains.soup)) remains.soup = "mala";
                remains.spice = Math.Max(0, Math.Min(5, remains.spice));
                remains.condiments ??= new List<string>();
                remains.condiments.RemoveAll(x => !Content.Condiments.Any(c => c.Id == x));
                remains.leftovers ??= new MealLeftoverResult();
                NormalizeResult(remains.leftovers, remains.selected, catalog);
            }
            DailyReport report = state.report;
            report.mealsFinished = Count(report.mealsFinished, report.served);
            report.finishedFood = Count(report.finishedFood, report.mealsFinished);
            report.finishedSoup = Count(report.finishedSoup, report.mealsFinished);
            report.leftovers ??= new List<NamedCount>();
            report.leftovers.RemoveAll(x => x == null || x.count <= 0 || !ValidKey(x.id, ids));
            report.leftovers = report.leftovers.GroupBy(x => x.id).Select(g => new NamedCount
                { id = g.Key, count = (int)Math.Min(int.MaxValue, g.Sum(x => (long)x.count)) }).ToList();
            LifetimeStats stats = state.stats;
            stats.mealsFinished = Count(stats.mealsFinished, stats.served);
            stats.finishedFood = Count(stats.finishedFood, stats.mealsFinished);
            stats.finishedSoup = Count(stats.finishedSoup, stats.mealsFinished);
            stats.allFinishedDays = Count(stats.allFinishedDays, Math.Max(0, state.day));
        }

        /// <summary>保存された具材位置を実際の注文の並びと照合します。</summary>
        private static void NormalizeResult(MealLeftoverResult result, List<string> selected, IngredientCatalog catalog)
        {
            MealLeftovers.Normalize(result, catalog);
            result.ingredients.RemoveAll(x => x.index >= selected.Count || selected[x.index] != x.ingredientId);
            if (result.soupReason != "soup_mismatch" && result.soupReason != "spice" && result.soupReason != "condiment") result.soupReason = "";
        }

        /// <summary>具材と理由を含む統計キーの妥当性を返します。</summary>
        private static bool ValidKey(string key, HashSet<string> ids)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string[] parts = key.Split('|');
            return parts.Length == 2 && ids.Contains(parts[0]) && MealLeftovers.IsFoodReason(parts[1]);
        }

        /// <summary>整数カウンターを上限までに補正します。</summary>
        private static int Count(int value, int maximum) => Math.Max(0, Math.Min(Math.Max(0, maximum), value));

        /// <summary>食事秒を非負かつ有限に補正します。</summary>
        private static float FiniteRange(float value, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(maximum, value));
    }
}
