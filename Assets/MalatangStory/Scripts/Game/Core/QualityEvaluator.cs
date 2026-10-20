using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>
    /// 1杯の品質を内訳つきで評価し、客のコメントを作る。
    /// 茹で加減は具材ごとに「鍋に入れてからの時間 ÷ その具材の茹で時間」で判定する。
    /// </summary>
    public static class QualityEvaluator
    {
        public const float PerfectDonenessMin = .85f, PerfectDonenessMax = 1.25f;

        /// <summary>具材1つの茹で加減の割合（1 でちょうど良い）。未投入は 0。</summary>
        public static float Doneness(CustomerOrder o, int index, IngredientCatalog catalog)
        {
            if (index < 0 || index >= o.selected.Count || index >= o.dropTimes.Count || o.dropTimes[index] < 0) return 0;
            return Math.Max(0, o.heatSeconds - o.dropTimes[index]) / catalog.Get(o.selected[index]).cookingSeconds;
        }

        /// <summary>茹で加減の割合を 0〜100 点に変換する。</summary>
        public static float DonenessScore(float ratio)
        {
            if (ratio < PerfectDonenessMin) return Clamp(100 - (PerfectDonenessMin - ratio) * 220);
            if (ratio > PerfectDonenessMax) return Clamp(100 - (ratio - PerfectDonenessMax) * 140);
            return 100;
        }

        /// <summary>湯切りの回数を点数にする（3〜4回が最適）。</summary>
        public static float DrainScore(int shakes) => shakes <= 0 ? 35 : shakes == 1 ? 65 : shakes == 2 ? 88 : shakes <= 4 ? 100 : shakes == 5 ? 90 : 78;

        /// <summary>スープの種類と量を点数にする。</summary>
        public static float SoupScore(CustomerOrder o)
        {
            if (string.IsNullOrEmpty(o.chosenSoup)) return 0;
            var soup = Content.Soup(o.chosenSoup);
            float amount = o.soupMl < soup.IdealMinMl ? 100 - (soup.IdealMinMl - o.soupMl) * .45f
                : o.soupMl > soup.IdealMaxMl ? 100 - (o.soupMl - soup.IdealMaxMl) * .35f : 100;
            return Clamp(amount) * (o.chosenSoup == o.soup ? 1f : .45f);
        }

        /// <summary>辛さと調味料の好みを点数にする。</summary>
        public static float SeasoningScore(CustomerOrder o)
        {
            float score = 100 - Math.Abs(o.chosenSpice - o.spice) * 22;
            foreach (string wanted in o.requestedCondiments.Distinct())
                if (!o.condiments.Contains(wanted)) score -= 15;
            foreach (string disliked in o.dislikedCondiments.Distinct())
                if (o.condiments.Contains(disliked) || (disliked == Content.ChiliOil && o.chosenSpice > 0 && o.spice == 0)) score -= 35;
            int extras = o.condiments.Distinct().Count(x => !o.requestedCondiments.Contains(x) && !o.dislikedCondiments.Contains(x));
            score -= Math.Max(0, extras - 1) * 5;
            return Clamp(score);
        }

        /// <summary>注文内容と選んだ具材の一致度。</summary>
        public static float AccuracyScore(CustomerOrder o)
        {
            int overlap = o.requested.GroupBy(x => x).Sum(g => Math.Min(g.Count(), o.selected.Count(x => x == g.Key)));
            return 100f * overlap / Math.Max(1, Math.Max(o.requested.Count, o.selected.Count));
        }

        /// <summary>品質の内訳を計算する。freshnessOf は具材ID→鮮度点（0〜100）。</summary>
        public static QualityReport Evaluate(CustomerOrder o, IngredientCatalog catalog, Func<string, float> freshnessOf, float strictness = 1f)
        {
            var report = new QualityReport
            {
                accuracy = o.omakase ? Omakase.Score(o, catalog) : AccuracyScore(o),
                doneness = o.selected.Count == 0 ? 0 : (float)Enumerable.Range(0, o.selected.Count).Average(i => DonenessScore(Doneness(o, i, catalog))),
                drain = DrainScore(o.shakes),
                soup = SoupScore(o),
                seasoning = SeasoningScore(o),
                speed = Clamp(100 - Math.Max(0, o.elapsed - o.patience * .55f) * (100f / Math.Max(30, o.patience))),
                freshness = o.selected.Count == 0 ? 0 : o.selected.Average(x => freshnessOf(x))
            };
            if (o.boiledOver) report.doneness *= .6f;
            float total = .24f * report.accuracy + .26f * report.doneness + .08f * report.drain + .14f * report.soup
                + .14f * report.seasoning + .08f * report.speed + .06f * report.freshness;
            if (strictness > 1) total = 100 - (100 - total) * strictness;
            report.total = Clamp(total);
            if (o.omakase) report.total = Math.Min(report.total, Omakase.QualityCap(o, catalog));
            report.comments = Comments(o, report, catalog);
            return report;
        }

        private static List<string> Comments(CustomerOrder o, QualityReport r, IngredientCatalog catalog)
        {
            var lines = new List<string>();
            var aspects = new List<(float score, string good, string bad)>
            {
                o.omakase ? (r.accuracy, Omakase.Comment(o, catalog), Omakase.Comment(o, catalog))
                    : (r.accuracy, "頼んだ具材がぜんぶ入ってる！", "あれ、頼んだ具材と違う…"),
                (r.doneness, "火の通りが完璧！", DonenessComplaint(o, catalog)),
                (r.drain, "水っぽさがなくて味が濃い。", "湯切りが甘くてスープが薄い…"),
                (r.soup, o.chosenSoup == o.soup ? "スープがたっぷりで最高。" : "このスープも悪くないね。", o.chosenSoup != o.soup ? "頼んだスープと違う…" : o.soupMl < Content.Soup(o.chosenSoup).IdealMinMl ? "スープが少ないな。" : "スープが多すぎてあふれそう。"),
                (r.seasoning, o.spice >= 4 ? "この辛さ、たまらない！" : "味付けがちょうどいい！", SeasoningComplaint(o)),
                (r.speed, "すぐに出てきて助かる！", "ずいぶん待たされたな…"),
            };
            var best = aspects.OrderByDescending(x => x.score).First();
            if (best.score >= 90) lines.Add(best.good);
            foreach (var weak in aspects.Where(x => x.score < 70).OrderBy(x => x.score).Take(2)) lines.Add(weak.bad);
            if (lines.Count == 0) lines.Add(r.total >= 75 ? "おいしかった！" : "まあまあかな。");
            return lines;
        }

        private static string DonenessComplaint(CustomerOrder o, IngredientCatalog catalog)
        {
            int worst = -1; float worstScore = 101; float worstRatio = 1;
            for (int i = 0; i < o.selected.Count; i++)
            {
                float ratio = Doneness(o, i, catalog), score = DonenessScore(ratio);
                if (score < worstScore) { worstScore = score; worst = i; worstRatio = ratio; }
            }
            if (worst < 0) return "茹で加減がいまひとつ。";
            var item = catalog.Get(o.selected[worst]);
            if (worstRatio < PerfectDonenessMin) return item.displayName + "が生っぽい…";
            return item.category == "noodle" ? item.displayName + "がのびてる…" : item.displayName + "が煮えすぎ…";
        }

        private static string SeasoningComplaint(CustomerOrder o)
        {
            var disliked = o.dislikedCondiments.FirstOrDefault(x => o.condiments.Contains(x));
            if (disliked != null) return Content.Condiment(disliked).Name + "は抜いてほしかった…";
            if (o.chosenSpice > o.spice) return "辛すぎる！";
            if (o.chosenSpice < o.spice) return "もっと辛くてもいいのに。";
            var missing = o.requestedCondiments.FirstOrDefault(x => !o.condiments.Contains(x));
            return missing != null ? Content.Condiment(missing).Name + "を入れてほしかった。" : "味付けがいまひとつ。";
        }

        private static float Clamp(float value) => Math.Max(0, Math.Min(100, value));
    }
}
