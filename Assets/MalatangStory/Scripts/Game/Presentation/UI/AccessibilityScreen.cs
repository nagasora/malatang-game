using System;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>操作補助と文字・演出の設定を、入力機器に関係なく変更する。</summary>
    public sealed class AccessibilityScreen : UIScreen
    {
        public override bool PausesGame => true;
        public override bool ShowHud => App.Started;
        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .5f);
            var body = UIKit.Card(root, "操作・表示", new Vector2(700, 660), IconKind.Gear, () => App.Open(GameScreen.Settings));
            var list = UIKit.ScrollList(body, 8, 4);
            UIKit.Fill((RectTransform)list.parent, 0, 0, 0, 64);
            Option(list, "文字サイズ：" + (S.textSize < 20 ? "小" : S.textSize > 20 ? "大" : "標準"), () => S.textSize = S.textSize < 20 ? 20 : S.textSize == 20 ? 24 : 16);
            Option(list, "画面の揺れ・演出：" + (S.reducedMotion ? "抑える" : "通常"), () => S.reducedMotion = !S.reducedMotion);
            Option(list, "移動スティック：" + (S.leftHanded ? "右下（左利き）" : "左下"), () => S.leftHanded = !S.leftHanded);
            Option(list, "見下ろしカメラの45度吸着：" + (S.snapCamera ? "あり" : "なし"), () => S.snapCamera = !S.snapCamera);
            Option(list, "調理ゲージ：" + new[] { "常に表示", "簡易（記号だけ）", "なし（上級）" }[S.gaugeMode], () => S.gaugeMode = (S.gaugeMode + 1) % 3);
            Option(list, "設備を操作するキー：" + S.interactKey, () => S.interactKey = S.interactKey == "E" ? "F" : S.interactKey == "F" ? "G" : "E");
            Option(list, "内装を回転するキー：" + S.buildRotateKey, () => S.buildRotateKey = S.buildRotateKey == "T" ? "Y" : S.buildRotateKey == "Y" ? "R" : "T");
            UIKit.Height(UIKit.Label(list, "移動 WASD / 矢印　小走り Shift\nカメラ Q / R　視点 V　注文票 1〜9\n次の作業 N / Tab　図鑑 C　経営 M　仕入れ I　内装 B\nゲームパッド：左スティック移動・右スティック視点・Aで操作", 18, UIKit.Ink, TextAnchor.UpperLeft), 110);
            var back = UIKit.Button(body, "戻る", () => App.Open(GameScreen.Settings), ButtonStyle.Primary, 20, IconKind.ArrowLeft);
            UIKit.Place(back.GetComponent<RectTransform>(), new Vector2(.5f, 0), Vector2.zero, new Vector2(620, 52));
        }
        private void Option(Transform parent, string title, Action change)
        {
            var button = UIKit.Button(parent, title, () => { change(); App.ApplySettings(); App.Save(); App.Open(GameScreen.Accessibility); }, ButtonStyle.Secondary, 18);
            UIKit.Height(button, 50);
        }
    }
}
