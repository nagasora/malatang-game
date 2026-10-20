using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>章の目標ひとつ。進み具合は現在値と目標値で表す。</summary>
    public sealed class ChapterGoal
    {
        public string Label;
        public int Target;
        public Func<GameState, int> Current;

        /// <summary>達成済みか。</summary>
        public bool Done(GameState s) => Current(s) >= Target;
    }

    /// <summary>「店の物語」の章。すべての目標を満たすと完了し、報酬と次の章が開く。</summary>
    public sealed class ChapterDefinition
    {
        public string Title, Story, Ending;
        public ChapterGoal[] Goals;
        public long RewardFen;
        /// <summary>完了で買えるようになる内装テーマ（無ければ空）。</summary>
        public string UnlocksTheme = "";
    }

    /// <summary>
    /// 長期目標「店の物語」。章は順番に進み、目標は累計の記録から判定する（日をまたいで進む）。
    /// </summary>
    public static class Chapters
    {
        public static readonly ChapterDefinition[] All =
        {
            new ChapterDefinition
            {
                Title = "第1章　鍋ひとつからの出発",
                Story = "商店街の角に、小さな麻辣湯の店を開いた。まずは近所の人に味を覚えてもらおう。",
                Ending = "「また来るね」の声が増えてきた。屋台の頃の師匠が、赤提灯を譲ってくれるという。",
                Goals = new[]
                {
                    Goal("麻辣湯を提供する", 20, s => s.stats.served),
                    Goal("常連さんをつくる", 2, s => s.regulars.Count),
                    Goal("店舗レベルを上げる", 3, s => s.shopLevel),
                },
                RewardFen = 30000, UnlocksTheme = "lantern",
            },
            new ChapterDefinition
            {
                Title = "第2章　商店街の顔",
                Story = "商店街の会長から「うちの通りの顔になってくれ」と頼まれた。評判を上げて、新しい味も探そう。",
                Ending = "通りの人たちが店の話をしてくれるようになった。若いお客さんからは「おしゃれにしたら？」との声も。",
                Goals = new[]
                {
                    Goal("評判を上げる", 65, s => (int)s.reputation),
                    Goal("麻辣湯を提供する", 80, s => s.stats.served),
                    Goal("隠しレシピを見つける", 3, s => s.recipes.Count),
                    Goal("ミッションを達成する", 5, s => s.stats.missionsCompleted),
                },
                RewardFen = 80000, UnlocksTheme = "cafe",
            },
            new ChapterDefinition
            {
                Title = "第3章　評判の店",
                Story = "グルメ雑誌に小さく載った。評論家も来るかもしれない。どんなお客さんにも最高の一杯を。",
                Ending = "評論家の記事が出てから、遠くからのお客さんも増えた。老舗の茶館の主人が、古い調度品を譲ってくれた。",
                Goals = new[]
                {
                    Goal("評論家を満足させる", 1, s => s.stats.criticsSatisfied),
                    Goal("店の格付けを星3つにする", 3, Progression.Stars),
                    Goal("会心の一杯（品質95点以上）を出す", 20, s => s.stats.perfectBowls),
                    Goal("いろいろなタイプのお客さんをもてなす", 12, s => s.stats.archetypesServed.Count(x => x.count > 0)),
                },
                RewardFen = 150000, UnlocksTheme = "teahouse",
            },
            new ChapterDefinition
            {
                Title = "第4章　グランプリへの道",
                Story = "麻辣湯グランプリの招待状が届いた。腕を磨き、審査員をうならせよう。",
                Ending = "グランプリの表彰台に立った。夜の街でいちばん目立つ店にしよう、と仲間が言った。",
                Goals = new[]
                {
                    Goal("グランプリで入賞する", 1, s => s.stats.contestGold + s.stats.contestSilver + s.stats.contestBronze),
                    Goal("店舗レベルを上げる", 9, s => s.shopLevel),
                    Goal("隠しレシピを見つける", 8, s => s.recipes.Count),
                    Goal("大満足を連続で出す", 15, s => s.stats.bestCombo),
                },
                RewardFen = 300000, UnlocksTheme = "neon",
            },
            new ChapterDefinition
            {
                Title = "最終章　伝説の一杯",
                Story = "街の誰もが知る店になった。最後の目標は、誰も食べたことのない伝説の一杯。",
                Ending = "「この街の麻辣湯といえば、あの店」。鍋ひとつから始まった物語は、ここで一区切り。でも、お客さんはまだまだやって来る。",
                Goals = new[]
                {
                    Goal("店の格付けを星5つにする", 5, Progression.Stars),
                    Goal("グランプリで金賞を取る", 1, s => s.stats.contestGold),
                    Goal("隠しレシピをすべて見つける", Recipes.All.Length, s => s.recipes.Count),
                    Goal("麻辣湯を提供する", 1000, s => s.stats.served),
                },
                RewardFen = 1000000,
            },
        };

        private static ChapterGoal Goal(string label, int target, Func<GameState, int> current)
            => new ChapterGoal { Label = label, Target = target, Current = current };

        /// <summary>進めている章（すべて完了していれば null）。</summary>
        public static ChapterDefinition Current(GameState s) => s.chapter < All.Length ? All[s.chapter] : null;

        /// <summary>
        /// 進めている章の目標がそろっていれば完了にして報酬を渡す。
        /// 1回の呼び出しで完了するのは1章だけ（次の章の物語を読む間を作るため、続けて完了させない）。
        /// </summary>
        public static bool Check(RestaurantSimulation sim)
        {
            var s = sim.State;
            var chapter = Current(s);
            if (chapter == null || !s.tutorialComplete || !chapter.Goals.All(g => g.Done(s))) return false;
            s.chapter++;
            s.moneyFen += chapter.RewardFen;
            s.report.missionRewardsFen += chapter.RewardFen;
            s.report.highlights.Add(chapter.Title + " 完了！");
            string unlock = string.IsNullOrEmpty(chapter.UnlocksTheme) ? "" : "\n内装テーマ「" + Content.Theme(chapter.UnlocksTheme).Name + "」が買えるようになりました。";
            sim.Emit(SimEventKind.ChapterComplete, chapter.UnlocksTheme, s.chapter, chapter.Title + " 完了！\n" + chapter.Ending + unlock);
            Progression.CheckAchievements(sim);
            return true;
        }
    }
}
