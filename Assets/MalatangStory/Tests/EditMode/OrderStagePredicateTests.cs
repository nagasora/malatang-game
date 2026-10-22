using System;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>注文ステージの組み合わせ判定（終了・レジ前・一杯待ち）を全ステージについて保証します。</summary>
    public sealed class OrderStagePredicateTests
    {
        private static OrderStage[] AllStages => Enum.GetValues(typeof(OrderStage)).Cast<OrderStage>().ToArray();

        /// <summary>提供済みと取消済みだけが終了した注文です。</summary>
        [Test]
        public void IsClosedOnlyForServedAndCancelled()
        {
            foreach (var stage in AllStages)
                Assert.AreEqual(stage == OrderStage.Served || stage == OrderStage.Cancelled,
                    new CustomerOrder { stage = stage }.IsClosed, stage.ToString());
        }

        /// <summary>列に並んでいる客と注文を話している客だけがレジ前の注文です。</summary>
        [Test]
        public void IsAtRegisterOnlyForWaitingAndOrdering()
        {
            foreach (var stage in AllStages)
                Assert.AreEqual(stage == OrderStage.Waiting || stage == OrderStage.Ordering,
                    new CustomerOrder { stage = stage }.IsAtRegister, stage.ToString());
        }

        /// <summary>会計後から完成までが一杯待ちで、具材選び中は支払い済みの場合だけ含みます。</summary>
        [Test]
        public void AwaitsBowlFromPaymentUntilReady()
        {
            var awaiting = new[] { OrderStage.Weighed, OrderStage.Heating, OrderStage.Drained, OrderStage.Finishing, OrderStage.Ready };
            foreach (var stage in AllStages.Where(x => x != OrderStage.Selecting))
                Assert.AreEqual(awaiting.Contains(stage), new CustomerOrder { stage = stage }.AwaitsBowl, stage.ToString());
            Assert.IsFalse(new CustomerOrder { stage = OrderStage.Selecting }.AwaitsBowl);
            Assert.IsTrue(new CustomerOrder { stage = OrderStage.Selecting, paidFen = 1 }.AwaitsBowl);
        }
    }
}
