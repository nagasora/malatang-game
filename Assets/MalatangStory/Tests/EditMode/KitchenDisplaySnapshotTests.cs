using System;
using System.Collections.Generic;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>厨房の表示変更検出と、定常状態での割り当てがないことを保証します。</summary>
    public sealed class KitchenDisplaySnapshotTests
    {
        /// <summary>器の構成だけを持つ注文を作ります。</summary>
        private static CustomerOrder CreateOrder() => new CustomerOrder
        {
            id = "snapshot", stage = OrderStage.Heating,
            selected = new List<string> { "enoki", "bok_choy" },
            dropTimes = new List<float> { -1f, -1f }
        };

        /// <summary>工程・食材ID・投入フラグ・明示再生成を検出し、加熱時間や仕上げから完成では作り直しません。</summary>
        [Test]
        public void BowlSnapshotDetectsVisualChangesWithoutRebuildingForContinuousValues()
        {
            KitchenDisplaySnapshot snapshot = new KitchenDisplaySnapshot(IngredientCatalog.Load());
            CustomerOrder order = CreateOrder();
            Assert.IsTrue(snapshot.SynchronizeBowl(order));
            Assert.IsFalse(snapshot.SynchronizeBowl(order));
            order.heatSeconds = 50f;
            Assert.IsFalse(snapshot.SynchronizeBowl(order));
            order.dropTimes[0] = 0f;
            Assert.IsTrue(snapshot.SynchronizeBowl(order));
            order.dropTimes[0] = 3f;
            Assert.IsFalse(snapshot.SynchronizeBowl(order));
            order.selected[0] = "beef_slice";
            Assert.IsTrue(snapshot.SynchronizeBowl(order));
            order.stage = OrderStage.Finishing;
            Assert.IsTrue(snapshot.SynchronizeBowl(order));
            order.stage = OrderStage.Ready;
            Assert.IsFalse(snapshot.SynchronizeBowl(order));
            snapshot.InvalidateBowl();
            Assert.IsTrue(snapshot.SynchronizeBowl(order));
            order.selected.Add("rice_noodle");
            snapshot.CaptureBowl(order);
            Assert.IsFalse(snapshot.SynchronizeBowl(order));
            Assert.IsTrue(snapshot.SynchronizeBowl(null));
            Assert.IsFalse(snapshot.SynchronizeBowl(null));
        }

        /// <summary>数量の増減だけでは作り直さず、品切れと解禁による構成変更で更新します。</summary>
        [Test]
        public void CoolerSnapshotTracksAvailabilityAndStockWithoutCreatingEmptyStock()
        {
            RestaurantSimulation simulation = new RestaurantSimulation(IngredientCatalog.Load());
            KitchenDisplaySnapshot snapshot = new KitchenDisplaySnapshot(simulation.Catalog);
            simulation.State.stock.Clear();
            simulation.State.stock.Add(new Stock { ingredientId = "enoki", quantity = 3 });
            Assert.IsTrue(snapshot.SynchronizeCooler(simulation.Unlocks, simulation.State.stock));
            Assert.AreEqual(1, snapshot.CoolerItems.Count);
            Assert.AreEqual("enoki", snapshot.CoolerItems[0].id);
            simulation.State.stock[0].quantity = 2;
            Assert.IsFalse(snapshot.SynchronizeCooler(simulation.Unlocks, simulation.State.stock));
            simulation.State.stock[0].quantity = 0;
            Assert.IsTrue(snapshot.SynchronizeCooler(simulation.Unlocks, simulation.State.stock));
            Assert.IsEmpty(snapshot.CoolerItems);
            simulation.State.stock.Add(new Stock { ingredientId = "jinhua_ham", quantity = 5 });
            Assert.IsFalse(snapshot.SynchronizeCooler(simulation.Unlocks, simulation.State.stock));
            simulation.State.ingredients.rareObtainedIds.Add("jinhua_ham");
            Assert.IsTrue(snapshot.SynchronizeCooler(simulation.Unlocks, simulation.State.stock));
            Assert.AreEqual("jinhua_ham", snapshot.CoolerItems[0].id);
            Assert.AreEqual(2, simulation.State.stock.Count);
        }

        /// <summary>ウォームアップ後の器・冷蔵ケース同期5000回で、管理ヒープを割り当てません。</summary>
        [Test]
        public void StableSnapshotSynchronizationAllocatesNoManagedMemory()
        {
            RestaurantSimulation simulation = new RestaurantSimulation(IngredientCatalog.Load());
            KitchenDisplaySnapshot snapshot = new KitchenDisplaySnapshot(simulation.Catalog);
            CustomerOrder order = CreateOrder();
            for (int index = 0; index < 100; index++)
            {
                snapshot.SynchronizeBowl(order);
                snapshot.SynchronizeCooler(simulation.Unlocks, simulation.State.stock);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 5000; index++)
            {
                snapshot.SynchronizeBowl(order);
                snapshot.SynchronizeCooler(simulation.Unlocks, simulation.State.stock);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }
    }
}
