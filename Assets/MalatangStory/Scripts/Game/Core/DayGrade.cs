using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>
    /// 1日の営業の評価（S〜D）と、日ごとの記録。評価は満足度の平均・取りこぼし・値引き・提供数から決める。
    /// </summary>
    public static class DayGrade
    {
        /// <summary>保存しておく日数。</summary>
        public const int HistoryLimit = 28;

        /// <summary>評価の点数（0〜100）。提供が無い日は 0。</summary>
        public static float Score(GameState s)
        {
            var r = s.report;
            if (r.served == 0) return 0;
            var served = s.orders.Where(o => o.stage == OrderStage.Served).ToList();
            float satisfaction = served.Count == 0 ? 0 : served.Average(o => o.report.satisfaction);
            float lostRate = r.lost / (float)Math.Max(1, r.served + r.lost);
            float complaintRate = r.complaints / (float)r.served;
            float volume = Math.Min(1f, r.served / (float)Math.Max(6, s.tutorialComplete ? 10 + s.shopLevel : 1));
            return Math.Max(0, Math.Min(100, satisfaction - lostRate * 60 - complaintRate * 30 - (1 - volume) * 15));
        }

        /// <summary>点数から評価の文字（S・A・B・C・D）。</summary>
        public static string Letter(float score) => score >= 90 ? "S" : score >= 80 ? "A" : score >= 68 ? "B" : score >= 55 ? "C" : "D";

        /// <summary>閉店時に今日の要約を記録する（同じ日を二重に記録しない）。</summary>
        public static DaySummary Record(GameState s)
        {
            var existing = s.history.FirstOrDefault(x => x.day == s.day);
            if (existing != null) return existing;
            var r = s.report;
            var served = s.orders.Where(o => o.stage == OrderStage.Served).ToList();
            var summary = new DaySummary
            {
                day = s.day,
                revenueFen = r.revenueFen + r.tipsFen,
                profitFen = r.ProfitFen,
                served = r.served,
                lost = r.lost,
                satisfaction = served.Count == 0 ? 0 : served.Average(o => o.report.satisfaction),
                grade = Letter(Score(s)),
            };
            s.history.Add(summary);
            if (s.history.Count > HistoryLimit) s.history.RemoveRange(0, s.history.Count - HistoryLimit);
            if (summary.grade == "S") s.stats.gradeS++;
            r.highlights.Insert(0, "本日の評価：" + summary.grade);
            return summary;
        }
    }
}
