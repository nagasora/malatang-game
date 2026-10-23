using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace MalatangStory.Game
{
    public sealed class StallScreen : UIScreen
    {
        private RectTransform list;
        private string signature;
        private readonly List<(Text text, CustomerOrder order)> timers = new List<(Text, CustomerOrder)>();
        protected override void Build(RectTransform root)
        {
            var panel = UIKit.Panel(root, UIKit.Paper, 18, true, "Festival Stall Controls");
            var rt = panel.rectTransform; rt.anchorMin = new Vector2(1, 0); rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(1, .5f); rt.offsetMin = new Vector2(-480, 16); rt.offsetMax = new Vector2(-14, -84);
            list = UIKit.ScrollList(rt, 8, 12); UIKit.Fill(list.parent as RectTransform);
        }
        public override void Refresh()
        {
            string next = string.Join(",", Sim.Active.Select(x => x.id + ":" + x.stage + ":" + x.shakes + ":" + x.soupMl));
            if (signature == next) return; signature = next;
            Clear(list); timers.Clear();
            var title = UIKit.Label(list, "お祭りの屋台・回転勝負！", 25, UIKit.Lacquer, TextAnchor.MiddleLeft, true); UIKit.Height(title, 40);
            var menu = UIKit.Label(list, "限定食材：" + string.Join("・", S.stallMenu.Select(id => App.Catalog.Get(id).displayName)), 16, UIKit.InkSoft, TextAnchor.UpperLeft); UIKit.Height(menu, 52);
            foreach (var order in Sim.Active.Take(8).ToArray())
            {
                var current = order;
                var label = UIKit.Label(list, order.customerName + "・" + Content.Soup(order.soup).Name + "・辛さ" + order.spice, 18, UIKit.Ink, TextAnchor.MiddleLeft, true); UIKit.Height(label, 32);
                void Action(string caption, System.Action action)
                {
                    var button = UIKit.Button(list, caption, () => { Sim.PlayerFocusOrderId = current.id; action(); }, ButtonStyle.Primary, 19);
                    UIKit.Height(button, 48);
                }
                switch (order.stage)
                {
                    case OrderStage.Waiting:
                    case OrderStage.Ordering:
                        Action("受ける・量って会計", () => Sim.Accept(current.id)); break;
                    case OrderStage.Selecting:
                        Action("具材を入れて量る", () => { foreach (string id in current.requested) Sim.AddIngredient(current.id, id); Sim.Weigh(current.id); }); break;
                    case OrderStage.Weighed:
                        Action("ザルを鍋に入れる", () => Sim.StartHeating(current.id, true)); break;
                    case OrderStage.Heating:
                        var timer = UIKit.Label(list, "", 18, UIKit.Jade, TextAnchor.MiddleLeft); UIKit.Height(timer, 30); timers.Add((timer, order));
                        Action("引き上げて湯切りへ", () => Sim.Drain(current.id)); break;
                    case OrderStage.Drained:
                    case OrderStage.Finishing:
                        if (order.shakes < 3) Action("手首でシャッ！ " + order.shakes + "/3", () => Sim.ShakeByHand(current.id, Time.unscaledTime));
                        else if (string.IsNullOrEmpty(order.chosenSoup)) Action("スープと薬味を添える", () => {
                            var soup = Content.Soup(current.soup); Sim.PourSoup(current.id, soup.Id, (soup.IdealMinMl + soup.IdealMaxMl) / 2);
                            for (int i = 0; i < current.spice; i++) Sim.AddCondiment(current.id, Content.ChiliOil);
                            foreach (string id in current.requestedCondiments.Distinct()) Sim.AddCondiment(current.id, id);
                        });
                        else Action("仕上げる", () => Sim.Finish(current.id)); break;
                    case OrderStage.Ready:
                        if (Sim.CanOfferApology(order)) Action("謝罪のおまけを添える", () => Sim.OfferApologySide(current.id));
                        Action("屋台の窓口で渡す", () => Sim.Serve(current.id)); break;
                }
            }
            if (!Sim.Active.Any()) { var wait = UIKit.Label(list, "お祭りのお客さんを待ちましょう。", 20, UIKit.InkSoft); UIKit.Height(wait, 60); }
            var back = UIKit.Button(list, "店内を確認", () => App.Open(GameScreen.Store), ButtonStyle.Secondary, 18, IconKind.ArrowRight); UIKit.Height(back, 44);
        }
        public override void Tick(float deltaTime)
        {
            foreach (var timer in timers)
            {
                float remaining = Sim.SecondsUntilDone(timer.order);
                timer.text.text = remaining <= 0 ? "✓ 食材の色と泡を見て、いま引き上げ！" : "煮え具合を確認…あと " + remaining.ToString("0.0") + "秒";
            }
        }
    }
}
