using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>店主スキルの定義。</summary>
    public sealed class SkillDefinition
    {
        public string Id, Name, Description;
        /// <summary>1段階あたりの効果の説明（例：「茹で時間 -6%」）。</summary>
        public string PerRank;
        public int MaxRank = 3;
        /// <summary>段階ごとに必要なポイント（1段目、2段目、3段目）。</summary>
        public int[] Cost = { 1, 2, 3 };
    }

    /// <summary>
    /// 店主スキル。店舗レベルが上がるたびに1ポイントもらえ、グランプリの入賞でも増える。
    /// 効果はシミュレーションの各所（茹で・客の忍耐・仕入れ・チップ・来客数）から倍率として参照する。
    /// </summary>
    public static class Skills
    {
        public const string Speed = "speed", Hospitality = "hospitality", Bargain = "bargain", Charm = "charm", Fame = "fame";

        public static readonly SkillDefinition[] All =
        {
            new SkillDefinition { Id = Speed, Name = "手際", Description = "火加減の勘がよくなり、具材が早く茹で上がる。", PerRank = "茹で時間 -6%" },
            new SkillDefinition { Id = Hospitality, Name = "接客", Description = "笑顔と声かけで、お客さんが待ってくれる。", PerRank = "お客さんの忍耐 +8%" },
            new SkillDefinition { Id = Bargain, Name = "目利き", Description = "市場の顔なじみが増えて、仕入れが安くなる。", PerRank = "仕入れ値 -5%" },
            new SkillDefinition { Id = Charm, Name = "愛想", Description = "また来たくなる店主。チップが増える。", PerRank = "チップ +10%" },
            new SkillDefinition { Id = Fame, Name = "名声", Description = "店主の顔が知られて、来店するお客さんが増える。", PerRank = "来客数 +5%" },
        };

        /// <summary>ID からスキルを探す（無ければ null）。</summary>
        public static SkillDefinition Find(string id) => All.FirstOrDefault(x => x.Id == id);

        /// <summary>スキルの段階（0〜MaxRank）。</summary>
        public static int Rank(GameState s, string id) => Math.Max(0, Math.Min(Find(id)?.MaxRank ?? 0, LifetimeStats.Get(s.skills, id)));

        /// <summary>これまでにもらったポイント（店舗レベル−1、グランプリの金・銀・銅でそれぞれ2・1・1）。</summary>
        public static int Earned(GameState s) => Math.Max(0, s.shopLevel - 1) + s.stats.contestGold * 2 + s.stats.contestSilver + s.stats.contestBronze;

        /// <summary>使ったポイント。</summary>
        public static int Spent(GameState s) => All.Sum(def => Enumerable.Range(0, Rank(s, def.Id)).Sum(i => def.Cost[i]));

        /// <summary>使えるポイント。</summary>
        public static int Available(GameState s) => Math.Max(0, Earned(s) - Spent(s));

        /// <summary>次の段階に必要なポイント（最大なら -1）。</summary>
        public static int NextCost(GameState s, string id)
        {
            var def = Find(id);
            int rank = Rank(s, id);
            return def == null || rank >= def.MaxRank ? -1 : def.Cost[rank];
        }

        /// <summary>スキルを1段階上げる。</summary>
        public static bool Learn(RestaurantSimulation sim, string id)
        {
            var s = sim.State;
            int cost = NextCost(s, id);
            if (cost < 0) return false;
            if (Available(s) < cost) return sim.Fail("スキルポイントが足りません（あと" + (cost - Available(s)) + "）。");
            LifetimeStats.Add(s.skills, id);
            var def = Find(id);
            sim.Notify("スキル「" + def.Name + "」が" + Rank(s, id) + "段階になりました（" + def.PerRank + "）。");
            Progression.CheckAchievements(sim);
            sim.Save();
            return true;
        }

        /// <summary>茹での速さの倍率。</summary>
        public static float CookSpeed(GameState s) => 1f / (1f - .06f * Rank(s, Speed));

        /// <summary>客の忍耐の倍率。</summary>
        public static float Patience(GameState s) => 1f + .08f * Rank(s, Hospitality);

        /// <summary>仕入れ値の倍率。</summary>
        public static float Purchase(GameState s) => 1f - .05f * Rank(s, Bargain);

        /// <summary>チップの倍率。</summary>
        public static float Tip(GameState s) => 1f + .1f * Rank(s, Charm);

        /// <summary>来客数の倍率。</summary>
        public static float Demand(GameState s) => 1f + .05f * Rank(s, Fame);
    }
}
