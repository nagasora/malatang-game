using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>配合、刻むタイミング、煮込み時間を同じ朝の状態で扱う。</summary>
    public sealed class PreparationScreen : UIScreen
    {
        public override bool Modal => true;
        private int bones = 2, spice = 2, oil = 1;
        private string soup = "mala";
        private Text status;
        private InputField name;
        private RectTransform controls;
        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .4f);
            var body = UIKit.Card(root, "朝の仕込み・オリジナルスープ", new Vector2(760, 580), IconKind.Pot, () => App.Open(GameScreen.Morning));
            var hint = UIKit.Label(body, "骨・香辛料・油は各0〜5（合計3以上）。材料1つ0.4元。\n8〜12秒煮込み、毎秒の中央で刻むと品質が上がります。省略は標準品質です。", 18, UIKit.Ink, TextAnchor.UpperLeft);
            UIKit.Place(hint.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(716, 68));
            controls = UIKit.ScrollList(body, 6, 0, "Preparation Controls"); UIKit.Fill(controls.parent as RectTransform, 0, 0, 80, 0);
            BuildControls();
        }
        private void BuildControls()
        {
            Clear(controls);
            var p = S.preparation;
            void Button(string label, System.Action click, bool enabled = true)
            {
                var b = UIKit.Button(controls, label, click, ButtonStyle.Secondary, 19, null, enabled);
                UIKit.Height(b, 45);
            }
            if (!p.started && !p.completed)
            {
                Button("スープ：" + Content.Soup(soup).Name, () => { var soups = Sim.Customers.AvailableSoups.ToArray(); int i = System.Array.FindIndex(soups, x => x.Id == soup); soup = soups[(i + 1) % soups.Length].Id; BuildControls(); });
                Button("骨 " + bones + "　＋", () => { bones = (bones + 1) % 6; BuildControls(); });
                Button("香辛料 " + spice + "　＋", () => { spice = (spice + 1) % 6; BuildControls(); });
                Button("油 " + oil + "　＋", () => { oil = (oil + 1) % 6; BuildControls(); });
                Button("材料を入れて仕込み開始", () => { if (Sim.BeginPreparation(soup, bones, spice, oil)) BuildControls(); }, bones + spice + oil >= 3);
                Button("スタッフに任せる（品質50・材料費なし）", () => { Sim.FinishPreparation(true); BuildControls(); });
            }
            else if (!p.completed)
            {
                status = UIKit.Label(controls, "", 23, UIKit.Lacquer, TextAnchor.MiddleCenter, true);
                UIKit.Height(status, 96);
                Button("トントン・具材を刻む", () => { if (Sim.ChopPreparation()) App.Audio.Play(Sfx.Click); });
                Button("火を止めて仕込み完了", () => { if (Sim.FinishPreparation()) BuildControls(); });
            }
            else
            {
                Button("本日の仕込み品質：" + p.quality.ToString("0") + "　営業へ", () => App.Open(GameScreen.Morning));
                if (p.started)
                {
                    name = UIKit.NameInput(controls, "本日の自家製スープ", 16);
                    UIKit.Height(name, 46);
                    Button("この配合をオリジナルスープに登録", () => { Sim.RecordHouseSoup(name.text); BuildControls(); });
                }
            }
            foreach (var recipe in S.houseSoups)
            {
                var selected = recipe;
                Button((S.activeHouseSoup == recipe.id ? "✓ " : "") + recipe.name + "　骨" + recipe.bones + "・香" + recipe.spice + "・油" + recipe.oil,
                    () => { Sim.ChooseHouseSoup(selected.id); BuildControls(); });
            }
        }
        public override void Tick(float deltaTime)
        {
            Sim.TickPreparation(deltaTime);
            var p = S.preparation;
            if (status != null) status.text = "煮込み " + p.seconds.ToString("0.0") + "秒　刻み " + p.accurateCuts + "/" + p.cuts + "\n" + (PreparationRules.AccurateCut(p.seconds) ? "● いま刻む！" : "○ リズムを待つ");
        }
    }
}
