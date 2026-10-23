using System;
using System.Linq;
namespace MalatangStory.Game
{
    public sealed partial class RestaurantSimulation
    {
        public bool SetDeliveryEnabled(bool enabled)
        {
            if (!CanManage || !State.tutorialComplete || !string.IsNullOrEmpty(State.challengeMode)) return false;
            if (enabled && !State.staff.Any(x => x.role == StaffRole.Courier)) return Fail("出前を始めるには配達係を採用しましょう。");
            State.deliveryEnabled = enabled; Save(); Changed?.Invoke(); return true;
        }
        public bool SetFestivalStall(bool enabled)
        {
            if (State.phase != DayPhase.Preparation || !State.tutorialComplete || !string.IsNullOrEmpty(State.challengeMode)) return false;
            if (enabled && (State.dailyEvent != "festival" || State.shopLevel < 3)) return Fail("屋台はお祭りの日、店舗Lv.3から出せます。");
            if (enabled)
            {
                State.stallMenu = StallRules.Menu(this);
                if (State.stallMenu.Count < 3) return Fail("屋台用の食材を3種類以上仕入れましょう。");
            }
            State.festivalStall = enabled; Save(); Changed?.Invoke(); return true;
        }
        public bool ReceiveNeighbourGift()
        {
            if (State.phase != DayPhase.Preparation || State.reputation < 60 || State.day - State.neighbourGiftDay < 5) return false;
            var item = Unlocks.NormalAvailable.FirstOrDefault(x => StockFor(x.id).quantity <= State.storageCapacity - 3);
            if (item == null) return Fail("贈り物を受け取れる保管場所がありません。");
            AddStock(StockFor(item.id), 3, 0); State.neighbourGiftDay = State.day;
            Notify("隣のお店から「いつもありがとう」。" + item.displayName + "を3個もらいました！"); Save(); return true;
        }
        public bool ScheduleRivalCookoff()
        {
            if (State.phase != DayPhase.Preparation || !State.tutorialComplete || State.day < 7 || !string.IsNullOrEmpty(State.challengeMode)) return false;
            if (State.rivalContestDay == State.day || State.festivalStall) return false;
            State.rivalContestDay = State.day; State.dailyEvent = "contest"; State.eventSeconds = 180; State.eventTriggered = false;
            Notify("向かいのお店と料理対決！13:15ごろ審査員が来店します。"); Save(); return true;
        }
        private void ResolveRivalCookoff()
        {
            if (State.rivalContestDay != State.day || State.rivalResolvedDay == State.day) return;
            State.rivalResolvedDay = State.day;
            int rivalScore = 65 + (CustomerDirector.StableHash(State.storeId + ":rival:" + State.day) & int.MaxValue) % 26;
            var judges = State.orders.Where(x => x.archetype == "judge" && x.stage == OrderStage.Served).ToList();
            float score = judges.Count == 0 ? 0 : judges.Average(x => x.quality);
            bool won = judges.Count >= 3 && score >= rivalScore;
            State.reputation = Math.Max(0, Math.Min(100, State.reputation + (won ? 3 : -2)));
            State.rivalReputation = Math.Max(0, Math.Min(100, State.rivalReputation + (won ? -2 : 3)));
            State.report.highlights.Add("料理対決：自店 " + score.ToString("0") + "／向かい " + rivalScore + "点。" + (won ? "勝利！評判+3" : "次こそ勝とう。評判-2"));
        }
        private bool DispatchDelivery(CustomerOrder order)
        {
            int couriers = State.staff.Count(x => x.role == StaffRole.Courier && !x.resting);
            if (State.orders.Count(x => x.stage == OrderStage.Delivering) >= couriers) return Fail("空いている配達係を待ちましょう。");
            order.stage = OrderStage.Delivering; order.deliverySeconds = 0;
            order.deliveryDuration = 30 + (CustomerDirector.StableHash(order.id) & int.MaxValue) % 26;
            Notify("配達スクーターが出発しました。45秒以内なら温かいまま届きます。");
            Emit(SimEventKind.StaffAction, order.id, 0, "delivery"); Save(); return true;
        }
        private void TickDelivery(CustomerOrder order, float seconds)
        {
            order.deliverySeconds += seconds; order.elapsed += seconds;
            if (order.deliverySeconds < order.deliveryDuration) return;
            float penalty = Math.Max(0, order.deliverySeconds - 45) * .3f;
            order.quality = Math.Max(0, order.quality - penalty); order.report.total = order.quality;
            order.delivered = true; order.stage = OrderStage.Ready;
            Serve(order.id, true);
            Notify(order.customerName + "へ到着！" + (penalty > 0 ? "少し冷めて品質が下がりました。" : "温かいうちに届けられました。"));
        }
    }
}
