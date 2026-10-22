using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>店舗レベル（経験値）と実績の進行。</summary>
    public static class Progression
    {
        /// <summary>経験値を加え、レベルが上がったら解禁内容を知らせる。</summary>
        public static void AddExperience(RestaurantSimulation sim, int amount)
        {
            var s = sim.State;
            s.experience += Math.Max(0, amount);
            s.report.experience += Math.Max(0, amount);
            while (s.shopLevel < Content.MaxShopLevel && s.experience >= Content.ExperienceForLevel(s.shopLevel + 1))
            {
                s.shopLevel++;
                var unlocks = UnlocksAt(s.shopLevel);
                string text = "店舗レベル" + s.shopLevel + "！" + (unlocks.Count > 0 ? "解禁：" + string.Join("・", unlocks) : "");
                s.report.highlights.Add(text);
                sim.Emit(SimEventKind.LevelUp, "", s.shopLevel, text);
            }
        }

        /// <summary>次のレベルまでの進み具合（0〜1）。</summary>
        public static float LevelProgress(GameState s)
        {
            if (s.shopLevel >= Content.MaxShopLevel) return 1;
            int from = Content.ExperienceForLevel(s.shopLevel), to = Content.ExperienceForLevel(s.shopLevel + 1);
            return Math.Max(0, Math.Min(1, (s.experience - from) / (float)Math.Max(1, to - from)));
        }

        /// <summary>そのレベルで新しく解禁される内容の名前。</summary>
        public static List<string> UnlocksAt(int level)
        {
            var list = new List<string>();
            list.AddRange(Content.Soups.Where(x => x.UnlockLevel == level).Select(x => x.Name + "スープ"));
            list.AddRange(Content.Condiments.Where(x => x.UnlockLevel == level).Select(x => x.Name));
            list.AddRange(Content.Furniture.Where(x => x.UnlockLevel == level).Select(x => x.Name));
            list.AddRange(Content.Staff.Where(x => x.UnlockLevel == level).Select(x => x.Title + "の採用"));
            foreach (var equipment in Content.Equipment)
                for (int i = 1; i < equipment.RequiredShopLevel.Length; i++)
                    if (equipment.RequiredShopLevel[i] == level) list.Add(equipment.Name + "Lv" + (i + 1));
            return list;
        }

        /// <summary>格付けの点数（評判・人気・店舗レベルから。おおむね 0〜110）。</summary>
        public static float RankPoints(GameState s) => s.reputation * .6f + s.popularity * .2f + Math.Min(30, s.shopLevel * 2);

        /// <summary>店の格付け（星1〜5）。</summary>
        public static int Stars(GameState s)
        {
            float points = RankPoints(s);
            return Content.Ranks.Where(r => points >= r.MinPoints).Select(r => r.Stars).DefaultIfEmpty(1).Max();
        }

        /// <summary>未達成の実績を判定し、新たに達成したものを知らせる。</summary>
        public static void CheckAchievements(RestaurantSimulation sim)
        {
            var s = sim.State;
            foreach (var def in Content.Achievements)
            {
                if (s.achievements.Contains(def.Id) || !def.Condition(s)) continue;
                s.achievements.Add(def.Id);
                s.report.highlights.Add("実績「" + def.Name + "」を達成！");
                sim.Emit(SimEventKind.Achievement, def.Id, 0, def.Name);
            }
        }
    }

    /// <summary>常連客との小さな物語（親密度に応じて解放される一言）。</summary>
    public static class RegularStories
    {
        private static readonly Dictionary<string, string[]> Lines = new Dictionary<string, string[]>
        {
            ["office"] = new[] { "「昼休みはここって決めてるんです」", "「実は部長もこの店のファンでして…」", "「昇進しました！お祝いに大盛りで」", "「転勤が決まっても、出張のたびに寄りますね」" },
            ["student"] = new[] { "「試験前はここで気合い入れてます」", "「サークルの後輩も連れてきますね！」", "「合格しました！店長のおかげです」", "「就職してもずっと通います」" },
            ["spicy"] = new[] { "「辛さ5、まだいけるな」", "「この花椒の痺れ、本場を思い出す」", "「辛党仲間に自慢しておいたよ」", "「あんたの麻辣が世界一だ」" },
            ["healthy"] = new[] { "「野菜たっぷりで体が軽いです」", "「ジム仲間にも勧めました」", "「健康診断の結果、良くなったんです」", "「ここは私の元気の源です」" },
            ["meatlover"] = new[] { "「肉は多めで頼む！」", "「牛肉の火の通し方、わかってるね」", "「肉の仕入れ、いいところ使ってるだろ」", "「この店の肉は裏切らない」" },
            ["family"] = new[] { "「子どもがここのトマトが好きで」", "「娘が店長さんの絵を描いたんです」", "「家族の記念日はここにしました」", "「この子が大きくなっても来ますね」" },
            ["elder"] = new[] { "「若い頃に食べた味に似とる」", "「孫にもこの店を教えたよ」", "「毎日の楽しみができたわい」", "「あんたの店は町の宝じゃ」" },
            ["tourist"] = new[] { "「旅のガイドに載ってなかったけど大当たり！」", "「また旅の途中で寄りました」", "「国の友人にも写真を送ったよ」", "「この町に住みたくなってきた」" },
            ["influencer"] = new[] { "「今日も映えてます」", "「フォロワーが行きたいって」", "「コラボ企画しませんか？」", "「ここは私の推し店です」" },
        };

        /// <summary>常連の現在の物語の一言。</summary>
        public static string Line(RegularCustomer regular)
        {
            if (!Lines.TryGetValue(regular.archetype ?? "", out var lines)) lines = Lines["office"];
            int index = Math.Max(0, Math.Min(lines.Length - 1, regular.storyIndex - 1));
            return regular.name + "さん" + lines[index];
        }
    }

    /// <summary>天気と日替わりイベントの抽選。</summary>
    public static class DailyEvents
    {
        /// <summary>翌日の天気とイベントを決める（日付から決定的）。</summary>
        public static void Roll(RestaurantSimulation sim)
        {
            var s = sim.State;
            var random = new Random(unchecked(s.day * 92821 + CustomerDirector.StableHash(s.storeId)));
            int season = Seasons.Index(s.day);
            s.weather = CustomerDirector.Weighted(random, Content.Weathers, w =>
                w.Id == "cold" ? (season == 3 ? 3f : season == 2 ? .8f : .2f)
                : w.Id == "hot" ? (season == 1 ? 3f : season == 0 ? .6f : .2f)
                : w.Id == "rain" ? 1.2f : w.Id == "sunny" ? 2.5f : 1.5f).Id;
            var events = Content.DailyEvents.Where(e => e.MinDay <= s.day).ToArray();
            s.dailyEvent = CustomerDirector.Weighted(random, events, e => e.Weight).Id;
            // グランプリは2週目から毎週（7の倍数の日）に必ず開かれる。
            if (s.day >= 14 && s.day % 7 == 0) s.dailyEvent = "contest";
            s.eventTriggered = false;
            s.eventSeconds = s.dailyEvent == "group" ? 80 : s.dailyEvent == "contest" ? 150
                : s.dailyEvent == "critic" || s.dailyEvent == "influencer" ? 120 + (float)random.NextDouble() * 420 : -1;
            Seasons.EnsureFeatured(sim);
            Missions.Generate(sim);
        }
    }
}

namespace MalatangStory.Game
{
    /// <summary>麻辣湯グランプリ（審査員3人の平均点で賞を決める）。</summary>
    public static class Contest
    {
        /// <summary>賞の名前と賞金（分）・評判の加算。平均点が足りなければ参加賞。</summary>
        public static (string name, long prizeFen, int reputation) Award(float average)
            => average >= 90 ? ("金賞", 80000L, 6) : average >= 80 ? ("銀賞", 40000L, 4) : average >= 70 ? ("銅賞", 20000L, 2) : ("参加賞", 0L, 0);

        /// <summary>審査員が全員食べ終えた（または閉店した）ら結果を確定する。</summary>
        public static void Conclude(RestaurantSimulation sim)
        {
            var s = sim.State;
            if (s.report.contestConcluded || s.dailyEvent != "contest" || !s.eventTriggered) return;
            var judges = s.orders.Where(o => o.archetype == "judge").ToList();
            if (judges.Count == 0) return;
            bool closing = s.phase == DayPhase.Report;
            if (!closing && judges.Any(o => !o.IsClosed)) return;
            s.report.contestConcluded = true;
            // 食べずに帰った審査員は0点として平均に含める（待たせたことも審査のうち）。
            float average = judges.Sum(o => o.stage == OrderStage.Served ? o.quality : 0f) / judges.Count;
            var (name, prize, reputation) = Award(average);
            if (name == "金賞") s.stats.contestGold++;
            else if (name == "銀賞") s.stats.contestSilver++;
            else if (name == "銅賞") s.stats.contestBronze++;
            s.moneyFen += prize;
            s.report.prizeFen += prize;
            s.report.reputationDelta += reputation;
            string text = "麻辣湯グランプリ " + name + "（平均" + average.ToString("0") + "点）" + (prize > 0 ? "　賞金 " + RestaurantSimulation.Money(prize) : "");
            s.report.highlights.Add(text);
            sim.Emit(SimEventKind.ContestResult, name, prize, text);
            Progression.CheckAchievements(sim);
        }
    }
}
