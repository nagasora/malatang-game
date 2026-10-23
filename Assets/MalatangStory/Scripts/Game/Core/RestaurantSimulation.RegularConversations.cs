using System;
using System.Linq;
namespace MalatangStory.Game
{
    public sealed partial class RestaurantSimulation
    {
        public bool ReplyToRegular(string id, bool encouraging)
        {
            var regular = State.regulars.FirstOrDefault(x => x.id == id);
            if (regular == null || regular.storyIndex == 0 || regular.answeredStory >= regular.storyIndex) return false;
            regular.answeredStory = regular.storyIndex;
            regular.affinity = Math.Min(100, regular.affinity + (encouraging ? 5 : 2));
            string response = encouraging ? "「聞いてくれてうれしいです。また来ますね」" : "「忙しいところありがとう。今度ゆっくり話しますね」";
            if (encouraging && regular.storyIndex >= 2 && !regular.introducedFriend)
            {
                var friend = Content.Archetypes.Where(x => x.SpawnWeight > 0 && !x.Child && x.MinShopLevel > State.shopLevel && !State.introducedArchetypes.Contains(x.Id)).OrderBy(x => x.MinShopLevel).FirstOrDefault();
                if (friend != null)
                {
                    State.introducedArchetypes.Add(friend.Id); regular.introducedFriend = true;
                    response += "　「次は" + friend.Label + "の友達を連れてきます！」";
                }
            }
            Notify(regular.name + "さん" + response); Save(); return true;
        }
        public bool AskAfterRegular(string id)
        {
            var regular = State.regulars.FirstOrDefault(x => x.id == id);
            if (regular == null || regular.lastVisitDay == 0 || State.day - regular.lastVisitDay < 3 || regular.concerned) return false;
            regular.concerned = true; regular.affinity = Math.Min(100, regular.affinity + 1);
            Notify(regular.name + "さんは" + (regular.archetype == "student" ? "試験の勉強中" : "遠くへ出かけているようです") + "。また会える日を待ちましょう。"); Save(); return true;
        }
    }
}
