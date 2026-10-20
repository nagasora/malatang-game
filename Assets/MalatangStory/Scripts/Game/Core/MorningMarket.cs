using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>通常解禁と独立した希少食材の朝市。店舗IDと日付だけで当日の1品を決める。</summary>
    public static class MorningMarket
    {
        public const int FirstDay = 3;
        public const int BatchSize = 5;
        public const float QualityBonus = 3f;
        public static readonly IReadOnlyList<string> RareIds = Array.AsReadOnly(new[]
            { "matsutake", "black_truffle", "jinhua_ham", "wagyu", "kuruma_shrimp", "abalone" });

        /// <summary>店舗IDと6日ブロックをシードに並びをシャッフルする。各ブロック内で全6種が一度ずつ現れる。</summary>
        public static string Pick(string storeId, int day)
        {
            if (day < FirstDay) return "";
            int offset = day - FirstDay;
            uint seed = unchecked((uint)CustomerDirector.StableHash(storeId ?? "") ^ ((uint)(offset / RareIds.Count) * 0x9E3779B9u));
            if (seed == 0) seed = 0x6D2B79F5u;
            string[] shuffled = RareIds.ToArray();
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                int other = (int)(seed % (uint)(i + 1));
                string value = shuffled[i]; shuffled[i] = shuffled[other]; shuffled[other] = value;
            }
            return shuffled[offset % RareIds.Count];
        }

        /// <summary>当日オファーを補完する。同日中の再開では購入済みフラグを保持する。</summary>
        public static void Ensure(RestaurantSimulation sim) => Normalize(sim.State);

        /// <summary>保存された朝市と入手記録を正規化する。希少IDを通常解禁へ混ぜない。</summary>
        public static void Normalize(GameState state)
        {
            state.ingredients ??= new IngredientProgress();
            state.ingredients.rareObtainedIds ??= new List<string>();
            state.ingredients.rareObtainedIds = state.ingredients.rareObtainedIds.Where(RareIds.Contains).Distinct().ToList();
            state.ingredients.unlockedIds ??= new List<string>();
            state.ingredients.unlockedIds.RemoveAll(RareIds.Contains);
            if (state.marketDay != state.day || state.day < FirstDay) state.marketPurchased = false;
            state.marketDay = state.day;
            state.marketIngredient = Pick(state.storeId, state.day);
        }

        /// <summary>現在の朝市の食材を返す。選出日が当日でない場合や出店前はnull。</summary>
        public static IngredientDefinition Current(RestaurantSimulation sim)
        {
            if (sim.State.day < FirstDay || sim.State.marketDay != sim.State.day) return null;
            string id = Pick(sim.State.storeId, sim.State.day);
            return sim.Catalog.ingredients.FirstOrDefault(x => x.isRare && x.id == id);
        }

        /// <summary>希少食材を含む一杯に一律3点だけ加点する。同種複数や多種類で重複加算しない。</summary>
        public static void ApplyBonus(CustomerOrder order, IngredientCatalog catalog)
        {
            var rare = catalog.ingredients.Where(x => x.isRare && order.selected.Contains(x.id)).ToList();
            if (rare.Count == 0) return;
            order.report.total = Math.Min(100f, order.report.total + QualityBonus);
            order.report.comments.Add(CustomerVoice.RareIngredient(string.Join("・", rare.Select(x => x.displayName))));
        }
    }
}
