using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>
    /// 提供時の満足度と、それによるお金のやり取り（チップ・値引き）と連続記録（コンボ）。
    /// 満足度 = 品質 + 雰囲気 − 汚れた席 + 接客（レジ対応など）。80 以上でチップ、60 未満で値引きを求められる。
    /// </summary>
    public static class Satisfaction
    {
        public const float TipThreshold = 80, ComplaintThreshold = 60, ComboThreshold = 85, ComboBreakThreshold = 70;

        /// <summary>食べられない具材（ベジタリアンの肉など）が入っているか。</summary>
        public static bool ServedForbidden(RestaurantSimulation sim, CustomerOrder o, CustomerArchetype archetype)
            => archetype.ForbiddenCategories.Length > 0 && o.selected.Any(id => archetype.ForbiddenCategories.Contains(sim.Catalog.Get(id).category));

        /// <summary>満足度（0〜100）を求める。</summary>
        public static float Compute(RestaurantSimulation sim, CustomerOrder o, CustomerArchetype archetype)
        {
            var s = sim.State;
            float ambience = Math.Max(0, DiningLayout.Ambience(s));
            float value = o.quality + Math.Min(8, ambience * .25f) - s.dirtyTables.Count * 2 + o.serviceBonus;
            if (ServedForbidden(sim, o, archetype)) value -= 40;
            if (archetype.Id == "couple" && ambience >= 15) value += 3;
            if (archetype.Id == "rider" && o.elapsed < o.patience * .6f) value += 5;
            return Math.Max(0, Math.Min(100, value));
        }

        /// <summary>コンボ数によるチップの倍率（10連続で1.5倍まで）。</summary>
        public static float ComboMultiplier(int combo) => 1f + Math.Min(Math.Max(0, combo), 10) * .05f;

        /// <summary>チップの額（分）。満足度80未満、または払わない客は 0。</summary>
        public static long Tip(long paidFen, float satisfaction, float generosity, int combo)
        {
            if (satisfaction < TipThreshold || generosity <= 0 || paidFen <= 0) return 0;
            return RestaurantSimulation.RoundToJiao(paidFen * (satisfaction - 75) / 100d * generosity * ComboMultiplier(combo));
        }

        /// <summary>値引きの額（分）。満足度60以上、または値引きを求めない客は 0。最大で代金の6割。</summary>
        public static long Discount(long paidFen, float satisfaction, float complaint)
        {
            if (satisfaction >= ComplaintThreshold || complaint <= 0 || paidFen <= 0) return 0;
            double rate = satisfaction >= 40 ? (ComplaintThreshold - satisfaction) / 100d * 1.2 : .3 + (40 - satisfaction) / 100d;
            rate = Math.Min(.6, rate * complaint);
            return Math.Min(paidFen, RestaurantSimulation.RoundToJiao(paidFen * rate));
        }

        /// <summary>満足度を確定し、チップ・値引き・コンボ・特別な客の記録を反映する。</summary>
        public static void Settle(RestaurantSimulation sim, CustomerOrder o, CustomerArchetype archetype)
        {
            var s = sim.State;
            var r = s.report;
            float satisfaction = Compute(sim, o, archetype);
            o.report.satisfaction = satisfaction;
            if (ServedForbidden(sim, o, archetype)) o.report.comments.Insert(0, "お肉や魚は食べられないって言ったのに…");
            if (o.serviceBonus >= 5) o.report.comments.Add("レジの対応も気持ちよかった。");
            else if (o.serviceBonus <= -5) o.report.comments.Add("レジでのやり取りがちょっと…");

            o.tipFen = Tip(o.paidFen, satisfaction, archetype.Generosity * Skills.Tip(s) * BusinessRhythm.RewardMultiplier(s.daySeconds), s.combo);
            if (o.tipFen > 0)
            {
                s.moneyFen += o.tipFen; r.tipsFen += o.tipFen; s.stats.tipsFen += o.tipFen;
                sim.Emit(SimEventKind.Tip, o.id, o.tipFen);
            }
            o.discountFen = Discount(o.paidFen, satisfaction, archetype.Complaint);
            if (o.discountFen > 0)
            {
                s.moneyFen -= o.discountFen; r.revenueFen -= o.discountFen; r.discountsFen += o.discountFen; r.complaints++;
                s.stats.complaints++; s.stats.discountsFen += o.discountFen;
                sim.Emit(SimEventKind.Discount, o.id, o.discountFen);
            }

            if (satisfaction >= ComboThreshold)
            {
                s.combo++;
                r.bestCombo = Math.Max(r.bestCombo, s.combo);
                s.stats.bestCombo = Math.Max(s.stats.bestCombo, s.combo);
                if (s.combo % 5 == 0) sim.Emit(SimEventKind.Combo, o.id, s.combo, s.combo + "連続で大満足！チップ×" + ComboMultiplier(s.combo).ToString("0.00"));
            }
            else if (satisfaction < ComboBreakThreshold) BreakCombo(sim);

            if (archetype.Id == "vegetarian" && satisfaction >= TipThreshold) s.stats.vegetarianPleased++;
            if (archetype.Id == "celebrity" && satisfaction >= TipThreshold)
            {
                s.stats.celebrities++;
                r.popularityDelta += 6;
                r.highlights.Add("お忍びの芸能人がSNSで「隠れた名店」と紹介！");
                sim.Emit(SimEventKind.SpecialEvent, o.id, 1, "お忍びの芸能人が大満足！SNSで紹介されて人気が上がります。");
            }
        }

        /// <summary>コンボを途切れさせる（3以上続いていた場合は知らせる）。</summary>
        public static void BreakCombo(RestaurantSimulation sim)
        {
            if (sim.State.combo >= 3) sim.Emit(SimEventKind.Combo, "", 0, sim.State.combo + "連続の記録が途切れました。");
            sim.State.combo = 0;
        }
    }
}
