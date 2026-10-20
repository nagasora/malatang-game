using System;
using System.Collections.Generic;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>厨房の表示用スナップショットを再利用し、変化のないフレームの割り当てを防ぎます。</summary>
    public sealed class KitchenDisplaySnapshot
    {
        private const int CoolerLimit = 12;
        private readonly IngredientDefinition[] coolerCandidates;
        private readonly List<IngredientDefinition> coolerItems = new List<IngredientDefinition>(CoolerLimit);
        private readonly List<IngredientDefinition> nextCoolerItems = new List<IngredientDefinition>(CoolerLimit);
        private readonly List<(string id, bool dropped)> bowlContents = new List<(string, bool)>(16);
        private CustomerOrder bowlOrder;
        private int bowlStage = -1;
        private bool bowlDirty;

        /// <summary>表示順を一度だけ確定し、毎フレームのソートを避けます。</summary>
        public KitchenDisplaySnapshot(IngredientCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            coolerCandidates = (IngredientDefinition[])catalog.ingredients.Clone();
            Array.Sort(coolerCandidates, CompareCoolerItems);
        }

        /// <summary>直近で同期した冷蔵ケースの表示食材です。</summary>
        public IReadOnlyList<IngredientDefinition> CoolerItems => coolerItems;

        /// <summary>人気順とID順で安定した陳列順を返します。</summary>
        private static int CompareCoolerItems(IngredientDefinition left, IngredientDefinition right)
        {
            int popularity = right.popularity.CompareTo(left.popularity);
            return popularity != 0 ? popularity : string.Compare(left.id, right.id, StringComparison.Ordinal);
        }

        /// <summary>工程・食材・投入フラグが変わった時だけ記録し直します。完成と仕上げは同じ器を使います。</summary>
        public bool SynchronizeBowl(CustomerOrder current)
        {
            int stage = DisplayStage(current);
            bool changed = bowlDirty || !ReferenceEquals(current, bowlOrder) || stage != bowlStage;
            if (!changed && current != null)
            {
                changed = current.selected.Count != bowlContents.Count;
                for (int index = 0; !changed && index < bowlContents.Count; index++)
                {
                    bool dropped = index < current.dropTimes.Count && current.dropTimes[index] >= 0f;
                    changed = bowlContents[index].id != current.selected[index] || bowlContents[index].dropped != dropped;
                }
            }
            if (!changed) return false;
            CaptureBowl(current);
            return true;
        }

        /// <summary>手動投入で表示済みの構成を保存し、次フレームの二重生成を防ぎます。</summary>
        public void CaptureBowl(CustomerOrder current)
        {
            bowlOrder = current;
            bowlStage = DisplayStage(current);
            bowlDirty = false;
            bowlContents.Clear();
            if (current == null) return;
            for (int index = 0; index < current.selected.Count; index++)
                bowlContents.Add((current.selected[index], index < current.dropTimes.Count && current.dropTimes[index] >= 0f));
        }

        /// <summary>明示的な表示再生成を次の同期時に要求します。</summary>
        public void InvalidateBowl() => bowlDirty = true;

        /// <summary>完成時の具材物理を作り直さないため、仕上げ工程と同一視します。</summary>
        private static int DisplayStage(CustomerOrder current) => current == null ? -1
            : (int)(current.stage == OrderStage.Ready ? OrderStage.Finishing : current.stage);

        /// <summary>解禁と在庫を直接参照し、上位12食材の構成が変わった時だけ表示更新を返します。</summary>
        public bool SynchronizeCooler(IngredientUnlocks unlocks, List<Stock> stock)
        {
            nextCoolerItems.Clear();
            for (int index = 0; index < coolerCandidates.Length && nextCoolerItems.Count < CoolerLimit; index++)
            {
                IngredientDefinition item = coolerCandidates[index];
                if (unlocks.IsUnlocked(item.id) && HasStock(stock, item.id)) nextCoolerItems.Add(item);
            }
            bool changed = coolerItems.Count != nextCoolerItems.Count;
            for (int index = 0; !changed && index < coolerItems.Count; index++)
                changed = coolerItems[index].id != nextCoolerItems[index].id;
            if (!changed) return false;
            coolerItems.Clear();
            coolerItems.AddRange(nextCoolerItems);
            return true;
        }

        /// <summary>表示の照会で空在庫を作らないよう、在庫リストだけを読み取ります。</summary>
        private static bool HasStock(List<Stock> stock, string id)
        {
            for (int index = 0; index < stock.Count; index++)
                if (stock[index].ingredientId == id) return stock[index].quantity > 0;
            return false;
        }
    }
}
