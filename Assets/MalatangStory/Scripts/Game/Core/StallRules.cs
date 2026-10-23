using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;
namespace MalatangStory.Game
{
    public static class StallRules
    {
        public static List<string> Menu(RestaurantSimulation simulation)
        {
            var pool = simulation.Unlocks.NormalAvailable.Where(x => simulation.StockFor(x.id).quantity > 0).OrderByDescending(x => x.popularity).ThenBy(x => x.id).ToList();
            var chosen = pool.GroupBy(x => FoodProfile.Of(x).Role).Select(g => g.First()).Take(5).ToList();
            chosen.AddRange(pool.Where(x => !chosen.Contains(x)).Take(5 - chosen.Count));
            return chosen.Select(x => x.id).ToList();
        }
    }
}
