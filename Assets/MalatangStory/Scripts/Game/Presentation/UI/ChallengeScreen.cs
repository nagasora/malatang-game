using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace MalatangStory.Game
{
    public sealed class ChallengeScreen : UIScreen
    {
        public override bool Modal => true;
        public override bool ShowHud => false;
        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .4f);
            var body = UIKit.Card(root, "チャレンジ・自己ベスト", new Vector2(760, 580), IconKind.Star, () => App.Open(GameScreen.Title));
            var hint = UIKit.Label(body, "通常の店舗と別のセーブで遊びます。UTC日付で毎日・毎週同じ条件です。\n提供100点／大満足+30点／最高コンボ×10点／取消−50点。", 18, UIKit.Ink, TextAnchor.UpperLeft);
            UIKit.Place(hint.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(716, 62));
            var list = UIKit.ScrollList(body, 10, 0); UIKit.Fill(list.parent as RectTransform, 0, 0, 74, 0);
            var records = App.ChallengeRecords;
            foreach (string mode in new[] { ChallengeRules.Rush, ChallengeRules.Weekly })
            {
                string selected = mode;
                DateTime utc = DateTime.UtcNow;
                int seed = mode == ChallengeRules.Rush ? ChallengeRules.DailySeed(utc) : ChallengeRules.WeeklySeed(utc);
                string id = mode + "-" + seed;
                var best = records.FirstOrDefault(x => x.id == id);
                var info = UIKit.Label(list, (mode == ChallengeRules.Rush ? "日替わりラッシュ" : "週替わりチャレンジ") + "・" + ChallengeRules.Description(ChallengeRules.Rule(mode, seed)) + "\n自己ベスト：" + (best == null ? "未挑戦" : best.score + "点・" + best.served + "杯"), 18, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Height(info, 70);
                var start = UIKit.Button(list, "この条件で新しい挑戦を始める", () => App.StartChallenge(selected, utc), ButtonStyle.Primary, 20, IconKind.Play);
                UIKit.Height(start, 52);
            }
            var resume = UIKit.Button(list, "前回の挑戦を再開", App.ResumeChallenge, ButtonStyle.Gold, 20, IconKind.Play, App.HasChallenge); UIKit.Height(resume, 52);
            var note = UIKit.Label(list, "自己ベストは営業レポートから「通常の店舗へ」で戻ると記録します。\n再挑戦しても過去の最高記録と通常営業の進行は保持します。", 16, UIKit.InkSoft, TextAnchor.UpperLeft); UIKit.Height(note, 70);
        }
    }
}
