using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>
    /// 日替わりミッション（1日3つ）。営業の成績（日報）から進み具合を求め、達成するとすぐに賞金と経験値がもらえる。
    /// 内容は日付と店舗レベルから決定的に決まる。
    /// </summary>
    public static class Missions
    {
        /// <summary>1日のミッション数。</summary>
        public const int PerDay = 3;

        private static readonly (string kind, float weight)[] Kinds =
        {
            ("serve", 1.4f), ("excellent", 1.2f), ("tips", 1f), ("exact", 1f), ("combo", 1f),
            ("archetype", 1.2f), ("signature", .8f), ("spicy", .8f), ("revenue", 1f), ("noComplaint", .9f), ("recipe", .9f), ("seasonal", 1f), ("omakase", 1f),
        };

        /// <summary>その日のミッションを作り直す（練習中の初日は作らない）。</summary>
        public static void Generate(RestaurantSimulation sim)
        {
            var s = sim.State;
            s.missions.Clear();
            if (!s.tutorialComplete) return;
            var random = new Random(unchecked(s.day * 7907 + CustomerDirector.StableHash(s.storeId) + 17));
            int level = s.shopLevel;
            var pool = Kinds.Where(k => (k.kind != "signature" || s.signatures.Any(sim.Customers.CanOrderSignature)) && (k.kind != "spicy" || level >= 2) && (k.kind != "recipe" || s.recipes.Count > 0)
                && (k.kind != "seasonal" || sim.Unlocks.IsNormalUnlocked(s.featuredIngredient))
                && (k.kind != "omakase" || level >= 3)).ToList();
            for (int i = 0; i < PerDay && pool.Count > 0; i++)
            {
                var pick = CustomerDirector.Weighted(random, pool, k => k.weight);
                pool.Remove(pick);
                var mission = Create(pick.kind, level, random, s);
                mission.id = "m-" + s.day + "-" + i;
                s.missions.Add(mission);
            }
        }

        private static Mission Create(string kind, int level, Random random, GameState s)
        {
            var m = new Mission { kind = kind };
            switch (kind)
            {
                case "serve": m.target = 6 + level + random.Next(0, 4); break;
                case "excellent": m.target = 2 + level / 3 + random.Next(0, 2); break;
                case "tips": m.target = 10 + level * 4 + random.Next(0, 3) * 5; break;
                case "exact": m.target = 3 + level / 3; break;
                case "combo": m.target = 3 + level / 3 + random.Next(0, 2); break;
                case "archetype":
                    var candidates = Content.Archetypes.Where(a => a.SpawnWeight > 0 && a.MinShopLevel <= level && !a.Pair).ToArray();
                    m.param = candidates[random.Next(candidates.Length)].Id;
                    m.target = 1 + random.Next(0, 2);
                    break;
                case "signature": m.target = 2 + random.Next(0, 2); break;
                case "spicy": m.target = 2 + level / 5; break;
                case "recipe": m.target = 1 + Math.Min(2, s.recipes.Count / 4); break;
                case "seasonal": m.param = s.featuredIngredient; m.target = 3 + level / 3; break;
                case "omakase": m.target = 1 + level / 5; break;
                case "revenue": m.target = 200 + level * 60 + random.Next(0, 4) * 20; break;
                default: m.target = 8 + level / 2; break;
            }
            m.rewardFen = (30 + level * 8 + random.Next(0, 3) * 10) * 100L;
            m.rewardXp = 10 + level;
            return m;
        }

        /// <summary>本日の成績からの進み具合。</summary>
        public static int Progress(GameState s, Mission m)
        {
            var r = s.report;
            switch (m.kind)
            {
                case "serve": return r.served;
                case "excellent": return r.excellent;
                case "tips": return (int)(r.tipsFen / 100);
                case "exact": return r.exactChange;
                case "combo": return r.bestCombo;
                case "archetype": return r.servedArchetypes.Count(x => x == m.param);
                case "signature": return r.signatureSold;
                case "spicy": return r.spicyServed;
                // 前受けの代金は取消で返金され得るため、提供済み（値引き後）の売上だけで判定する。
                case "revenue": return (int)(s.orders.Where(o => o.stage == OrderStage.Served).Sum(o => Math.Max(0, o.paidFen - o.discountFen)) / 100);
                case "noComplaint": return r.complaints == 0 ? r.served : 0;
                case "recipe": return s.orders.Count(o => o.stage == OrderStage.Served && !string.IsNullOrEmpty(o.recipe));
                case "seasonal": return s.orders.Count(o => o.stage == OrderStage.Served && o.selected.Contains(m.param));
                case "omakase": return s.orders.Count(o => o.stage == OrderStage.Served && o.omakase && o.quality >= 85);
                default: return 0;
            }
        }

        /// <summary>
        /// 進み具合を更新し、達成したミッションの報酬を渡す。
        /// 「値引きなし」は閉店まで結果が決まらないため、endOfDay のときだけ達成にする。
        /// </summary>
        public static void Update(RestaurantSimulation sim, bool endOfDay = false)
        {
            var s = sim.State;
            foreach (var m in s.missions.Where(m => !m.done))
            {
                m.progress = Math.Min(m.target, Progress(s, m));
                if (m.progress < m.target || (m.kind == "noComplaint" && !endOfDay)) continue;
                m.done = true;
                s.moneyFen += m.rewardFen;
                s.report.missionRewardsFen += m.rewardFen;
                s.stats.missionsCompleted++;
                s.report.highlights.Add("ミッション達成：" + Describe(m));
                sim.Emit(SimEventKind.MissionComplete, m.id, m.rewardFen, Describe(m));
                Progression.AddExperience(sim, m.rewardXp);
            }
        }

        /// <summary>具材の表示名（Missions は表示層から呼ばれるため、カタログを都度読まずに済むよう一度だけ読む）。</summary>
        private static string IngredientName(string id)
        {
            catalog ??= MalatangStory.Ingredients.IngredientCatalog.Load();
            return catalog.ingredients.FirstOrDefault(x => x.id == id)?.displayName ?? id;
        }
        private static MalatangStory.Ingredients.IngredientCatalog catalog;

        /// <summary>ミッションの説明文。</summary>
        public static string Describe(Mission m)
        {
            switch (m.kind)
            {
                case "serve": return "麻辣湯を" + m.target + "杯提供する";
                case "excellent": return "品質90点以上を" + m.target + "杯作る";
                case "tips": return "チップを" + m.target + "元受け取る";
                case "exact": return "おつりをぴったり" + m.target + "回渡す";
                case "combo": return "大満足を" + m.target + "連続で出す";
                case "archetype": return Content.Archetype(m.param).Label + "を" + m.target + "人もてなす";
                case "signature": return "名物メニューを" + m.target + "杯売る";
                case "spicy": return "辛さ4以上の注文を" + m.target + "杯出す";
                case "revenue": return "提供した麻辣湯の売上" + m.target + "元を達成する";
                case "recipe": return "隠しレシピの一杯を" + m.target + "杯出す";
                case "seasonal": return "旬の" + IngredientName(m.param) + "入りを" + m.target + "杯出す";
                case "omakase": return "おまかせ注文で品質85点以上を" + m.target + "杯出す";
                case "noComplaint": return "値引きなしで" + m.target + "杯以上提供する（閉店時に判定）";
                default: return m.kind;
            }
        }
    }
}
