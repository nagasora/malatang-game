using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>季節の定義。</summary>
    public sealed class SeasonDefinition
    {
        public string Id, Name, Description;
        /// <summary>この季節に旬になり得る具材（解禁済みのものから週ごとに1つ選ぶ）。</summary>
        public string[] Produce;
        /// <summary>この季節に好まれるスープ（客のスープ選びの重みを上げる）。</summary>
        public string FavoredSoup;
    }

    /// <summary>
    /// 季節（春夏秋冬を14日ずつ）と「旬の食材」。旬は7日ごとに替わり、仕入れが25%安く、
    /// 客が頼みやすくなり、旬の具材が入った一杯は満足度が少し上がる。
    /// </summary>
    public static class Seasons
    {
        /// <summary>1つの季節の日数。</summary>
        public const int Length = 14;
        /// <summary>旬の食材の仕入れ値の倍率。</summary>
        public const float FeaturedPurchase = .75f;
        /// <summary>旬の具材が入った一杯の満足度の加算。</summary>
        public const float FeaturedBonus = 3f;

        public static readonly SeasonDefinition[] All =
        {
            new SeasonDefinition { Id = "spring", Name = "春", Description = "桜の季節。若い青菜と貝がおいしい。", FavoredSoup = "baitang",
                Produce = new[] { "pea_shoot", "komatsuna", "mizuna", "clam", "wakame", "spinach" } },
            new SeasonDefinition { Id = "summer", Name = "夏", Description = "暑さに負けない酸っぱいスープが人気。夏野菜が旬。", FavoredSoup = "tomato",
                Produce = new[] { "okra", "baby_corn", "cherry_tomato", "stem_lettuce", "squid", "bean_sprout" } },
            new SeasonDefinition { Id = "autumn", Name = "秋", Description = "実りの秋。きのこと根菜が香る。", FavoredSoup = "mushroom",
                Produce = new[] { "maitake", "king_oyster", "shimeji", "lotus_root", "burdock", "brown_enoki" } },
            new SeasonDefinition { Id = "winter", Name = "冬", Description = "寒い日は辛い麻辣で温まる。白菜と羊肉が旬。", FavoredSoup = "mala",
                Produce = new[] { "napa_cabbage", "crown_daisy", "potato", "lamb", "scallop", "kombu" } },
        };

        /// <summary>その日の季節の番号（0:春 1:夏 2:秋 3:冬）。1日目から14日ずつ。</summary>
        public static int Index(int day) => Math.Max(0, day - 1) / Length % 4;

        /// <summary>その日の季節。</summary>
        public static SeasonDefinition Of(int day) => All[Index(day)];

        /// <summary>季節の何日目か（1始まり）。</summary>
        public static int DayOfSeason(int day) => Math.Max(0, day - 1) % Length + 1;

        /// <summary>
        /// 旬の食材を決める（7日ごとに替わる）。解禁済みの通常候補が無ければ空。希少食材は代替候補にも含めない。
        /// 日付と店舗IDから決定的に選び、同じ週の中では同じものになる。
        /// </summary>
        public static string PickFeatured(RestaurantSimulation sim)
        {
            var s = sim.State;
            var options = Of(s.day).Produce.Where(sim.Unlocks.IsNormalUnlocked).ToArray();
            // 序盤は季節の候補が未解禁のことが多いため、解禁済みの野菜・きのこから選んで旬を毎週用意する。
            if (options.Length == 0)
                options = sim.Unlocks.NormalAvailable.Where(x => x.category == "vegetable" || x.category == "mushroom").Select(x => x.id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (options.Length == 0) return "";
            var random = new Random(unchecked(Week(s.day) * 48271 + CustomerDirector.StableHash(s.storeId)));
            return options[random.Next(options.Length)];
        }

        /// <summary>その日が属する週の番号（0始まり。旬はこの単位で替わる）。</summary>
        public static int Week(int day) => Math.Max(0, day - 1) / 7;

        /// <summary>
        /// 週が替わっていれば旬を選び直す。同じ週の中では、途中で食材が解禁されても選び直さない
        /// （週の途中で仕入れ値や注文の傾向が変わらないようにするため）。
        /// ただし保存済みの旬が希少食材などの通常候補外なら修復する。
        /// </summary>
        public static void EnsureFeatured(RestaurantSimulation sim)
        {
            var s = sim.State;
            int week = Week(s.day);
            if (s.featuredWeek == week && (string.IsNullOrEmpty(s.featuredIngredient) || sim.Unlocks.IsNormalUnlocked(s.featuredIngredient))) return;
            s.featuredIngredient = PickFeatured(sim);
            s.featuredWeek = week;
        }

        /// <summary>旬の食材か。</summary>
        public static bool IsFeatured(GameState s, string ingredientId) => !string.IsNullOrEmpty(s.featuredIngredient) && s.featuredIngredient == ingredientId;
    }
}
