using System;
using System.Linq;
namespace MalatangStory.Game
{
    public sealed partial class RestaurantSimulation
    {
        public bool CanOfferApology(CustomerOrder order) => order != null && order.stage == OrderStage.Ready && State.combo == 0
            && State.lastBrokenCombo >= 3 && !State.comboRecoveryUsed && State.daySeconds <= State.comboRecoveryUntil && string.IsNullOrEmpty(State.challengeMode);
        public bool OfferApologySide(string orderId)
        {
            var order = Find(orderId);
            if (!CanOfferApology(order)) return false;
            var stock = State.stock.FirstOrDefault(x => x.quantity > 0 && (Catalog.Get(x.ingredientId).category == "vegetable" || Catalog.Get(x.ingredientId).category == "soy"));
            if (stock == null) return Fail("おまけに添える野菜か豆腐がありません。");
            stock.quantity--; State.report.foodCostFen += stock.unitCostFen > 0 ? stock.unitCostFen : Cost(Catalog.Get(stock.ingredientId));
            State.combo = Math.Min(3, State.lastBrokenCombo / 2); State.comboRecoveryUsed = true; State.lastBrokenCombo = 0;
            order.serviceBonus += 3;
            Notify("謝罪のおまけを添えました。コンボを" + State.combo + "まで取り戻しました！"); Save(); return true;
        }
    }
}
