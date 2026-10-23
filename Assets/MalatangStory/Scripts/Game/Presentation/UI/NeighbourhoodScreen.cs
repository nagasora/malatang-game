using UnityEngine;
using UnityEngine.UI;
namespace MalatangStory.Game
{
    public sealed class NeighbourhoodScreen : UIScreen
    {
        public override bool Modal => true;
        public override bool PausesGame => true;
        private bool confirmFranchise;
        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .4f);
            var body = UIKit.Card(root, "商店街と新しい営業", new Vector2(760, 580), IconKind.Heart, () => App.Open(GameScreen.Morning));
            var list = UIKit.ScrollList(body, 10, 0); UIKit.Fill(list.parent as RectTransform);
            var info = UIKit.Label(list, "自店の評判 " + S.reputation.ToString("0") + "／向かいの店 " + S.rivalReputation.ToString("0") + "\n評判60以上で、5日ごとに隣のお店からおすそ分けが届きます。", 19, UIKit.Ink, TextAnchor.UpperLeft); UIKit.Height(info, 70);
            void Choice(string label, System.Func<bool> action, bool enabled = true)
            {
                var button = UIKit.Button(list, label, () => { action(); App.Open(GameScreen.Neighbourhood); }, ButtonStyle.Secondary, 19, null, enabled); UIKit.Height(button, 52);
            }
            Choice("おすそ分けを受け取る（食材3個）", Sim.ReceiveNeighbourGift, S.phase == DayPhase.Preparation && S.reputation >= 60 && S.day - S.neighbourGiftDay >= 5);
            Choice(S.deliveryEnabled ? "出前の受付を止める" : "出前を始める（配達係が必要）", () => Sim.SetDeliveryEnabled(!S.deliveryEnabled), Sim.CanManage && !App.InChallenge);
            Choice(S.festivalStall ? "屋台を片付けて店で営業する" : "祭りの屋台を出す（限定5食材）", () => Sim.SetFestivalStall(!S.festivalStall), S.phase == DayPhase.Preparation && (S.festivalStall || S.dailyEvent == "festival") && !App.InChallenge);
            Choice("向かいの店に料理対決を申し込む", Sim.ScheduleRivalCookoff, S.phase == DayPhase.Preparation && S.day >= 7 && !S.festivalStall && S.rivalContestDay != S.day && !App.InChallenge);
            var hint = UIKit.Label(list, "のれん分けは物語の最終章後。前の店舗を別ファイルに保存し、衣装・内装・実績と最大2,500元の資金を引き継いで、別の街の1日目から始めます。", 17, UIKit.InkSoft, TextAnchor.UpperLeft); UIKit.Height(hint, 72);
            var franchise = UIKit.Button(list, "のれん分け・2号店へ", () =>
            {
                if (!confirmFranchise) { confirmFranchise = true; return; }
                App.StartFranchise();
            }, ButtonStyle.Gold, 20, IconKind.Star, Sim.CanManage && S.chapter >= Chapters.All.Length && !App.InChallenge);
            UIKit.Height(franchise, 54);
            franchise.onClick.AddListener(() => { if (confirmFranchise) UIKit.SetLabel(franchise, "旧店舗をアーカイブして新しい街へ（もう一度）"); });
        }
    }
}
