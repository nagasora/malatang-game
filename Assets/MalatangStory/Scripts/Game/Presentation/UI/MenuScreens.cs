using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>タイトル画面。背景は夕暮れの店をゆっくり周回するカメラ。</summary>
    public sealed class TitleScreen : UIScreen
    {
        private bool confirmNew;
        private Button newButton;
        private Text message;
        public override bool ShowHud => false;

        protected override void Build(RectTransform root)
        {
            var shade = UIKit.Rect("Gradient", root);
            UIKit.Fill(shade);
            var g = shade.gameObject.AddComponent<Image>();
            g.sprite = TextureFactory.VerticalGradient(new Color(0, 0, 0, 0), new Color(.08f, .03f, .02f, .65f));
            g.raycastTarget = false;

            var logo = UIKit.Rect("Logo", root);
            UIKit.Place(logo, new Vector2(0, .5f), new Vector2(80, 90), new Vector2(620, 300));
            var seal = UIKit.Panel(logo, UIKit.Lacquer, 20, true, "Seal");
            UIKit.Place(seal.rectTransform, new Vector2(0, 1), new Vector2(0, 0), new Vector2(96, 96));
            var sealIcon = UIKit.Icon(seal.rectTransform, IconKind.Bowl, UIKit.Gold);
            UIKit.Fill(sealIcon.rectTransform, 14, 14, 14, 14);
            var title = UIKit.Outlined(UIKit.Label(logo, "麻辣湯ストーリー", 64, UIKit.Paper, TextAnchor.MiddleLeft, true), new Color(.35f, .05f, .02f, .9f), 3);
            UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, -104), new Vector2(620, 84));
            var sub = UIKit.Outlined(UIKit.Label(logo, "MALATANG STORY", 30, UIKit.Gold, TextAnchor.MiddleLeft, true), new Color(.2f, .05f, .02f, .8f), 2);
            UIKit.Place(sub.rectTransform, new Vector2(0, 1), new Vector2(4, -184), new Vector2(620, 40));
            var tagline = UIKit.Outlined(UIKit.Label(logo, "鍋ひとつから、町いちばんの麻辣湯店へ。", 22, UIKit.Paper, TextAnchor.MiddleLeft), new Color(0, 0, 0, .6f));
            UIKit.Place(tagline.rectTransform, new Vector2(0, 1), new Vector2(4, -230), new Vector2(620, 34));

            var menu = UIKit.Rect("Menu", root);
            UIKit.Place(menu, new Vector2(0, .5f), new Vector2(84, -150), new Vector2(360, 260));
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12; layout.childControlHeight = false; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            var cont = UIKit.Button(menu, "続きから", () => App.Continue(), ButtonStyle.Primary, 24, IconKind.Play, App.Saves.Exists);
            cont.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 60);
            newButton = UIKit.Button(menu, "新しく始める", () =>
            {
                if (App.Saves.Exists && !confirmNew) { confirmNew = true; UIKit.SetLabel(newButton, "本当に最初から？（上書き）"); return; }
                confirmNew = false; App.NewGame();
            }, App.Saves.Exists ? ButtonStyle.Secondary : ButtonStyle.Primary, 22, IconKind.Plus);
            newButton.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 56);
            var challenge = UIKit.Button(root, "チャレンジ", () => App.Open(GameScreen.Challenges), ButtonStyle.Gold, 20, IconKind.Star);
            UIKit.Place(challenge.GetComponent<RectTransform>(), new Vector2(0, .5f), new Vector2(470, -210), new Vector2(250, 52));
            var help = UIKit.Button(menu, "遊び方", () => App.Open(GameScreen.Help), ButtonStyle.Secondary, 20, IconKind.Book);
            help.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 50);
            var settings = UIKit.Button(menu, "設定", () => App.Open(GameScreen.Settings), ButtonStyle.Secondary, 20, IconKind.Gear);
            settings.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 50);
            menu.gameObject.AddComponent<PopIn>().Delay = .2f;

            message = UIKit.Outlined(UIKit.Label(root, "", 16, UIKit.Paper, TextAnchor.LowerLeft), new Color(0, 0, 0, .6f));
            UIKit.Place(message.rectTransform, new Vector2(0, 0), new Vector2(24, 16), new Vector2(900, 50));
            var credit = UIKit.Outlined(UIKit.Label(root, "v" + Application.version + "　フォント：Noto Sans JP / Zen Maru Gothic（SIL OFL 1.1）　音・3Dはすべて手続き生成", 13, UIKit.PaperDark, TextAnchor.LowerRight), new Color(0, 0, 0, .6f));
            UIKit.Place(credit.rectTransform, new Vector2(1, 0), new Vector2(-20, 14), new Vector2(700, 24));
        }

        public override void Refresh()
        {
            message.text = (string.IsNullOrEmpty(App.SaveMessage) ? "" : App.SaveMessage + "\n") + "Mac：クリック / WASD　iPhone：タップ / スティック";
        }
    }

    /// <summary>一時停止メニュー。</summary>
    public sealed class PauseScreen : UIScreen
    {
        public override bool PausesGame => true;
        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .5f);
            var body = UIKit.Card(root, "一時停止", new Vector2(460, 490), IconKind.Pause, Resume);
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12; layout.childControlHeight = false; layout.childForceExpandHeight = false; layout.childControlWidth = true;
            Add(body, "ゲームへ戻る", Resume, ButtonStyle.Primary, IconKind.Play);
            Add(body, "図鑑と記録", () => App.Open(GameScreen.Collection), ButtonStyle.Secondary, IconKind.Trophy);
            Add(body, "遊び方", () => App.Open(GameScreen.Help), ButtonStyle.Secondary, IconKind.Book);
            Add(body, "設定", () => App.Open(GameScreen.Settings), ButtonStyle.Secondary, IconKind.Gear);
            Add(body, "保存してタイトルへ", () => App.SuspendToTitle(), ButtonStyle.Danger, IconKind.Home);
            var save = UIKit.Label(body, App.SaveMessage, 15, UIKit.InkSoft, TextAnchor.MiddleCenter);
            save.rectTransform.sizeDelta = new Vector2(0, 30);
            body.parent.gameObject.AddComponent<PopIn>();
        }
        private void Resume() { App.Paused = false; App.BackToStore(); }
        private static void Add(RectTransform parent, string label, System.Action action, ButtonStyle style, IconKind icon)
            => UIKit.Button(parent, label, action, style, 21, icon).GetComponent<RectTransform>().sizeDelta = new Vector2(0, 58);
    }

    /// <summary>設定画面（音量・画質・開発用速度）。</summary>
    public sealed class SettingsScreen : UIScreen
    {
        private Text music, effects, look, quality, navigation, invert, speed;
        public override bool PausesGame => true;
        public override bool ShowHud => App.Started;

        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .5f);
            var body = UIKit.Card(root, "設定", new Vector2(600, 640), IconKind.Gear, Back);
            music = VolumeRow(body, 0, "BGM", v => { S.musicVolume = v; App.ApplySettings(); }, () => S.musicVolume);
            effects = VolumeRow(body, 1, "効果音", v => { S.effectsVolume = v; App.ApplySettings(); App.Audio.Play(Sfx.Pop); }, () => S.effectsVolume);
            look = VolumeRow(body, 2, "視点の感度", v => S.lookSensitivity = v, () => S.lookSensitivity, .4f, 2f, .2f);
            quality = ToggleRow(body, 0, IconKind.Sparkle, () => { S.highQuality = !S.highQuality; App.ApplySettings(); });
            navigation = ToggleRow(body, 1, IconKind.Info, () => S.navigation = !S.navigation);
            invert = ToggleRow(body, 2, IconKind.Rotate, () => S.invertLook = !S.invertLook);
            if (Debug.isDebugBuild || Application.isEditor)
            {
                var d = UIKit.Button(body, "", () => { App.DevelopmentSpeed = App.DevelopmentSpeed == 1 ? 8 : 1; Refresh(); }, ButtonStyle.Tab, 17, IconKind.Clock);
                UIKit.Place(d.GetComponent<RectTransform>(), new Vector2(.5f, 1), new Vector2(0, -446), new Vector2(540, 36));
                speed = d.GetComponentInChildren<Text>();
            }
            var access = UIKit.Button(body, "操作・表示の設定", () => App.Open(GameScreen.Accessibility), ButtonStyle.Secondary, 18, IconKind.Gear);
            UIKit.Place(access.GetComponent<RectTransform>(), new Vector2(.5f, 1), new Vector2(0, -384), new Vector2(540, 46));
            var info = UIKit.Label(body, "自動保存：30秒ごと・会計や調理の区切り・閉店時", 15, UIKit.InkSoft, TextAnchor.MiddleCenter);
            UIKit.Place(info.rectTransform, new Vector2(.5f, 0), new Vector2(0, 64), new Vector2(540, 28));
            var back = UIKit.Button(body, "戻る", Back, ButtonStyle.Primary, 21, IconKind.ArrowLeft);
            UIKit.Place(back.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(0, 0), new Vector2(540, 56));
        }

        private Text ToggleRow(RectTransform body, int row, IconKind icon, System.Action toggle)
        {
            var button = UIKit.Button(body, "", () => { toggle(); App.Save(); Refresh(); }, ButtonStyle.Secondary, 18, icon);
            UIKit.Place(button.GetComponent<RectTransform>(), new Vector2(.5f, 1), new Vector2(0, -206 - row * 58), new Vector2(540, 50));
            return button.GetComponentInChildren<Text>();
        }

        private Text VolumeRow(RectTransform body, int row, string label, System.Action<float> set, System.Func<float> get, float min = 0, float max = 1, float step = .1f)
        {
            float y = -6 - row * 64;
            var text = UIKit.Label(body, "", 20, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place(text.rectTransform, new Vector2(0, 1), new Vector2(10, y), new Vector2(300, 50));
            float Snap(float v) => Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
            var minus = UIKit.Button(body, "", () => { set(Snap(get() - step)); App.Save(); Refresh(); }, ButtonStyle.Secondary, 20, IconKind.Minus);
            UIKit.Place(minus.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-90, y), new Vector2(70, 50));
            var plus = UIKit.Button(body, "", () => { set(Snap(get() + step)); App.Save(); Refresh(); }, ButtonStyle.Secondary, 20, IconKind.Plus);
            UIKit.Place(plus.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-10, y), new Vector2(70, 50));
            text.name = label;
            return text;
        }

        public override void Refresh()
        {
            music.text = "BGM　" + Mathf.RoundToInt(S.musicVolume * 100) + "%";
            effects.text = "効果音　" + Mathf.RoundToInt(S.effectsVolume * 100) + "%";
            look.text = "視点の感度　×" + S.lookSensitivity.ToString("0.0");
            quality.text = "画質：" + (S.highQuality ? "高（影・光の演出あり）" : "軽量（省電力）");
            navigation.text = "次にやることの案内：" + (S.navigation ? "表示する" : "表示しない");
            invert.text = "一人称の上下：" + (S.invertLook ? "反転" : "通常");
            if (speed != null) speed.text = "開発用：営業時間 " + App.DevelopmentSpeed + "倍速";
        }

        private void Back()
        {
            App.Save();
            if (!App.Started) App.Open(GameScreen.Title);
            else App.Open(GameScreen.Pause);
        }
    }

    /// <summary>遊び方の説明。</summary>
    public sealed class HelpScreen : UIScreen
    {
        private static readonly (string title, string text)[] Pages =
        {
            ("麻辣湯のお店へようこそ", "麻辣湯（マーラータン）は、好きな具材を選んで重さで値段が決まる中国の人気料理。\nお客さんの注文どおりに、手早くおいしく作って人気店を目指しましょう。\n\n移動：スティック / WASD / 床をタップ\n設備：近くでタップ、または右下のボタン（E キー）\nカメラ：ドラッグで回転、ホイール / ピンチで拡大"),
            ("視点の切り替え", "上のバーの「一人称」ボタン（V キー）で、店主の目線で店内を歩けます。\n\n・ドラッグ（スワイプ）で見回す、スティック / WASD で歩く\n・画面中央の照準を設備に合わせて E キー、またはタップで操作\n・窓や入口の向こうには街の通りが広がっています\n\nもう一度押すと見下ろし視点に戻ります。"),
            ("① レジで接客", "お客さんがレジに並んだら、注文票かレジをタップして接客します。\n\n・お客さんの話をよく聞いて、スープ・辛さ・店内／持ち帰りを注文票に書く（間違えると訂正され、印象が下がる）\n・セルフ式はお客さんのボウルをレジの秤で量る\n・現金ならおつりを紙幣と硬貨で組み立てて渡す。QR・カードはそのまま決済\n・常連の「いつもの」は好みを覚えておこう（常連ノートも見られます）"),
            ("② 具材・③ 計量", "オーダー式は、注文の具材を冷蔵ケースから取ってボウルへ入れ、厨房の秤で注文どおりか確かめます。\nセルフ式のボウルはレジで量り済みなので、すぐ茹でられます。\n\n注文と違う具材は減点。ベジタリアンのお客さんに肉や魚介を出すと大変なことに…"),
            ("④ 茹でる（腕の見せどころ）", "具材ごとに茹で時間が違います（葉物は短く、じゃがいもや団子は長い）。\n\n「時間差で茹でる」を選び、「今！」と出た具材から順に投入すると、全部が同時に食べごろ（緑のゲージ）になります。\n放っておくと煮えすぎ、さらに吹きこぼれます。"),
            ("⑤ 湯切り・⑥ 仕上げ", "引き上げたら上下に振って湯切り（3〜4回がベスト。振りすぎも冷めて減点）。\n\nスープを選び、長押しで緑の範囲まで注ぎます。\nラー油1回で辛さ+1。お客さんの「多め」「抜き」の希望も忘れずに。\n最後に「完成！」"),
            ("⑦ 提供・満足度とお金", "完成した麻辣湯は席へ運びます（持ち帰りはレジで）。\n\n満足度＝品質＋雰囲気＋レジ対応−汚れた席。\n・80点以上：チップ（気前のいいお客さんほど多い）\n・60点未満：値引きを求められ、売上が減る\n・85点以上が続くとコンボ！チップが最大1.5倍"),
            ("やり込み", "・店の物語：5つの章の目標を達成して、鍋ひとつの店を伝説の店へ（報酬で内装テーマが解禁）\n・隠しレシピ：相性のいい具材とスープの組み合わせ16種。見つけると品質アップ、お客さんが名前で頼むように\n・日替わりミッション：1日3つ。達成するとすぐに賞金と経験値\n・麻辣湯グランプリ：14日目から7日ごと。金・銀・銅賞\n・店の格付け（星1〜5）・お客さん図鑑（20タイプ）・実績47種類"),
            ("店主スキルと内装テーマ", "・店主スキル（経営画面）：レベルが上がるたびにポイント。手際（茹で時間）・接客（忍耐）・目利き（仕入れ値）・愛想（チップ）・名声（来客数）を伸ばせます\n・内装テーマ（内装画面）：下町食堂・赤提灯の夜市・モダンカフェ・老舗の茶館・ネオン屋台。雰囲気が上がり、見た目も一新\n\n便利な操作（Mac）：N か Tab で「次にやること」へ、数字キーで注文票、C 図鑑・M 経営・I 仕入れ・B 内装"),
            ("経営と成長", "・仕入れ：食材は5個単位。3日で鮮度切れ。\n・店舗レベル：提供するほど上がり、スープ・薬味・家具・スタッフ・設備・新しいお客さんが解禁。\n・内装：家具で席と雰囲気を増やす。入口と通路はふさげません。\n・スタッフ：ホール係・レジ係・調理補助が自動で働きます。\n・名物メニュー：高品質な一杯を登録すると15%増しで売れます。"),
        };
        private int page;
        private Text title, text, counter;
        public override bool PausesGame => true;
        public override bool ShowHud => App.Started;

        protected override void Build(RectTransform root)
        {
            UIKit.Dim(root, .5f);
            var body = UIKit.Card(root, "遊び方", new Vector2(760, 520), IconKind.Book, Back);
            title = UIKit.Label(body, "", 26, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(6, 0), new Vector2(700, 40));
            text = UIKit.Label(body, "", 19, UIKit.Ink, TextAnchor.UpperLeft);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(6, 70); text.rectTransform.offsetMax = new Vector2(-6, -50);
            var prev = UIKit.Button(body, "前へ", () => { page = (page + Pages.Length - 1) % Pages.Length; Refresh(); }, ButtonStyle.Secondary, 19, IconKind.ArrowLeft);
            UIKit.Place(prev.GetComponent<RectTransform>(), new Vector2(0, 0), Vector2.zero, new Vector2(160, 52));
            var next = UIKit.Button(body, "次へ", () => { page = (page + 1) % Pages.Length; Refresh(); }, ButtonStyle.Primary, 19, IconKind.ArrowRight);
            UIKit.Place(next.GetComponent<RectTransform>(), new Vector2(1, 0), Vector2.zero, new Vector2(160, 52));
            counter = UIKit.Label(body, "", 17, UIKit.InkSoft, TextAnchor.MiddleCenter);
            UIKit.Place(counter.rectTransform, new Vector2(.5f, 0), new Vector2(0, 10), new Vector2(200, 36));
        }

        public override void Refresh()
        {
            title.text = Pages[page].title; text.text = Pages[page].text; counter.text = (page + 1) + " / " + Pages.Length;
        }

        private void Back() { if (!App.Started) App.Open(GameScreen.Title); else App.Open(GameScreen.Pause); }
    }
}
