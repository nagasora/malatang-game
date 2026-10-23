namespace MalatangStory.Game
{
    public sealed partial class RestaurantSimulation
    {
        public bool TryWalkInVisitor(int seed, string archetype)
        {
            if (State.phase != DayPhase.Open || State.festivalStall || !State.tutorialComplete || !string.IsNullOrEmpty(State.challengeMode)
                || State.walkInVisitors >= 3 || State.spawnedToday >= Demand || System.Linq.Enumerable.Count(Queue) >= 5
                || Content.Archetype(archetype).MinShopLevel > State.shopLevel) return false;
            var order = Customers.Create(archetype);
            if (order == null) return false;
            order.appearanceSeed = seed; State.walkInVisitors++; AddOrder(order);
            Notify("通りがかりのお客さんが、店を覗いて入ってきました！"); return true;
        }
    }
}
