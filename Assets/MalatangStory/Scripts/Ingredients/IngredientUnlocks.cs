using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Ingredients
{
    /// <summary>通常解禁と朝市での希少食材入手を別々に保持する。</summary>
    [Serializable]
    public sealed class IngredientProgress
    {
        public long settledRevenueFen;
        public List<string> unlockedIds = new List<string>();
        /// <summary>朝市で入手した希少食材。通常解禁とは分離して永続化する。</summary>
        public List<string> rareObtainedIds = new List<string>();
        public List<string> settledOrderIds = new List<string>();
    }

    /// <summary>調理に利用できる全食材と、通常コンテンツに使える食材を管理する。</summary>
    public sealed class IngredientUnlocks
    {
        private readonly IngredientCatalog catalog;
        public IngredientProgress Progress { get; }

        /// <summary>カタログと進行状態から食材の利用可否を復元する。</summary>
        public IngredientUnlocks(IngredientCatalog catalog, IngredientProgress progress = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Progress = progress ?? new IngredientProgress();
            if (Progress.settledRevenueFen < 0) throw new ArgumentException("Revenue cannot be negative.");
            Progress.unlockedIds = Progress.unlockedIds ?? new List<string>();
            Progress.settledOrderIds = Progress.settledOrderIds ?? new List<string>();
            Progress.rareObtainedIds ??= new List<string>();
            Progress.rareObtainedIds = Progress.rareObtainedIds.Where(id => catalog.ingredients.Any(x => x.isRare && x.id == id)).Distinct().ToList();
            Progress.unlockedIds = Progress.unlockedIds.Where(id => catalog.ingredients.Any(x => !x.isRare && x.id == id)).Distinct().ToList();
            UnlockEligible();
        }

        /// <summary>提供または取消後の確定した純売上を計上し、通常食材を解禁する。</summary>
        public IReadOnlyList<IngredientDefinition> SettleOrder(string orderId, long paidFen, long refundedFen = 0)
        {
            if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("Order ID is required.");
            if (paidFen < 0 || refundedFen < 0 || refundedFen > paidFen)
                throw new ArgumentOutOfRangeException(nameof(paidFen));
            if (Progress.settledOrderIds.Contains(orderId)) return Array.Empty<IngredientDefinition>();
            var total = checked(Progress.settledRevenueFen + paidFen - refundedFen);
            Progress.settledOrderIds.Add(orderId);
            Progress.settledRevenueFen = total;
            return UnlockEligible();
        }

        /// <summary>調理・図鑑で利用できるか。購入済みの希少食材も含む。</summary>
        public bool IsUnlocked(string id) => Progress.unlockedIds.Contains(id) || Progress.rareObtainedIds.Contains(id);
        /// <summary>調理・図鑑で利用できる全食材。購入済みの希少食材も含む。</summary>
        public IEnumerable<IngredientDefinition> Available => catalog.ingredients.Where(x => IsUnlocked(x.id));
        /// <summary>注文・自動選択・旬などの通常コンテンツで利用できる食材か。</summary>
        public bool IsNormalUnlocked(string id) => catalog.ingredients.Any(x => x.id == id && !x.isRare) && IsUnlocked(id);

        /// <summary>解禁済みの通常食材。購入済みでも希少食材は含めない。</summary>
        public IEnumerable<IngredientDefinition> NormalAvailable => Available.Where(x => !x.isRare);

        public IngredientDefinition Next => catalog.ingredients.Where(x => !x.isRare && !IsUnlocked(x.id))
            .OrderBy(x => x.unlockRevenueYuan).ThenBy(x => x.id, StringComparer.Ordinal).FirstOrDefault();
        public long RemainingFen => Next == null ? 0 : Math.Max(0, Next.unlockRevenueYuan * 100L - Progress.settledRevenueFen);

        /// <summary>累計売上の条件を満たす通常食材だけを解禁する。</summary>
        private IReadOnlyList<IngredientDefinition> UnlockEligible()
        {
            var added = catalog.ingredients.Where(x => !x.isRare && !IsUnlocked(x.id)
                && Progress.settledRevenueFen >= x.unlockRevenueYuan * 100L)
                .OrderBy(x => x.unlockRevenueYuan).ThenBy(x => x.id, StringComparer.Ordinal).ToArray();
            foreach (var item in added) Progress.unlockedIds.Add(item.id);
            return added;
        }
    }
}
