using System;

namespace MalatangStory.Game
{
    /// <summary>数値を使わない辛さの言い方。対応を学んで聞き取れるよう、意味は毎回変えない。</summary>
    public static class OrderLanguage
    {
        private static readonly string[,] Spices =
        {
            { "辛みは抜きで", "辛いのは苦手なので" },
            { "ほんの少しだけ辛く", "辛さは香りづけくらいで" },
            { "ほどよくピリッと", "ちょっと刺激がほしいな" },
            { "汗ばむくらい辛く", "いつもの中辛くらいで" },
            { "しっかりしびれる辛さで", "遠慮せず辛くして" },
            { "火が出るくらい辛く", "お店で一番辛いので" }
        };
        public static string SpicePhrase(int spice, int variant = 0) => Spices[Math.Max(0, Math.Min(5, spice)), variant & 1];
        public static bool TrySpice(string phrase, out int spice)
        {
            for (int level = 0; level < 6; level++) for (int variant = 0; variant < 2; variant++)
                if (phrase == Spices[level, variant]) { spice = level; return true; }
            spice = -1; return false;
        }
    }

    public sealed partial class RestaurantSimulation
    {
        public bool AskOrderAgain(string id)
        {
            var order = Find(id);
            if (order == null || order.stage != OrderStage.Ordering || order.ticketConfirmed) return false;
            if (State.tutorialComplete) order.elapsed += 5;
            Changed?.Invoke(); return true;
        }
    }
}
