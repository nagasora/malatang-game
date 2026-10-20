using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// レジの接客画面。お客さんの注文を聞いて注文票を書き、セルフ式はボウルを量り、
    /// 支払い方法に応じて代金を受け取る（現金はおつりを紙幣・硬貨で組み立てて渡す）。
    /// カメラはレジの内側からカウンター越しにお客さんを映す。
    /// </summary>
    public sealed class RegisterScreen : UIScreen
    {
        private enum Step { Listen, Weigh, Pay, Done }

        private static readonly string[] DenominationLabels = { "100元", "50元", "20元", "10元", "5元", "1元", "5角", "1角" };

        private string orderId;
        private Step step;
        private RectTransform card, body;
        private Text title, dialogText, dialogName, registerHint, changeText, trayText;
        private readonly List<string> lines = new List<string>();
        private int shownLines;
        private float lineTimer;
        private string ticketSoup = "";
        private int ticketSpice = -1, ticketTakeout = -1;
        private bool hintUsed;
        private readonly List<long> tray = new List<long>();
        private Button confirmButton;
        private readonly List<(Button button, string soup)> soupButtons = new List<(Button, string)>();
        private readonly List<(Button button, int spice)> spiceButtons = new List<(Button, int)>();
        private readonly List<(Button button, int takeout)> takeoutButtons = new List<(Button, int)>();
        private float doneTimer;

        /// <summary>接客中の注文。</summary>
        private CustomerOrder O => Sim.Find(orderId);

        protected override void Build(RectTransform root)
        {
            orderId = App.SelectedOrderId;
            if (App.UI.Hud != null) App.UI.Hud.TicketsVisible = false;

            var dialog = UIKit.Panel(root, new Color(.99f, .97f, .9f, .97f), 22, true, "Dialog").rectTransform;
            UIKit.Place(dialog, new Vector2(0, 0), new Vector2(18, 18), new Vector2(600, 230));
            var namePlate = UIKit.Panel(dialog, UIKit.Lacquer, 12, false, "Name Plate");
            UIKit.Place(namePlate.rectTransform, new Vector2(0, 1), new Vector2(16, 18), new Vector2(330, 38));
            dialogName = UIKit.Label(namePlate.rectTransform, "", 18, UIKit.Paper, TextAnchor.MiddleLeft, true);
            UIKit.Fill(dialogName.rectTransform, 14, 10, 0, 0);
            dialogText = UIKit.Label(dialog, "", 20, UIKit.Ink, TextAnchor.LowerLeft, false, "Lines");
            dialogText.verticalOverflow = VerticalWrapMode.Truncate;
            UIKit.Fill(dialogText.rectTransform, 22, 22, 32, 16);
            var skip = dialog.gameObject.AddComponent<Button>();
            skip.onClick.AddListener(() => { shownLines = lines.Count; RenderDialog(); });

            var back = UIKit.Button(root, "あとで", () => App.BackToStore(), ButtonStyle.Tab, 17, IconKind.ArrowLeft, true, "Later");
            UIKit.Place(back.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(18, 276), new Vector2(150, 46));

            card = UIKit.Panel(root, UIKit.Paper, 22, true, "Register Card").rectTransform;
            UIKit.Place(card, new Vector2(1, .5f), new Vector2(-14, -38), new Vector2(560, 600));
            var header = UIKit.Panel(card, UIKit.Lacquer, 22, false, "Header").rectTransform;
            header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1); header.pivot = new Vector2(.5f, 1);
            header.sizeDelta = new Vector2(0, 56); header.anchoredPosition = Vector2.zero;
            var icon = UIKit.Icon(header, IconKind.Bell, UIKit.Gold);
            UIKit.Place(icon.rectTransform, new Vector2(0, .5f), new Vector2(18, 0), new Vector2(30, 30));
            title = UIKit.Label(header, "", 24, UIKit.Paper, TextAnchor.MiddleLeft, true, "Title");
            UIKit.Fill(title.rectTransform, 60, 16, 0, 0);
            body = UIKit.Rect("Body", card);
            UIKit.Fill(body, 20, 20, 68, 18);
            card.gameObject.AddComponent<PopIn>();

            var o = O;
            if (o == null) { step = Step.Done; BuildDone(); return; }
            lines.Add(CustomerVoice.Greeting(o));
            lines.AddRange(CustomerVoice.OrderLines(o, Sim.Catalog));
            if (o.ticketConfirmed) { lines.Add(CustomerVoice.PaymentLine(o)); shownLines = lines.Count; }
            else shownLines = 1;
            App.Audio.Play(Sfx.Voice, .7f);
            App.World.Speak(o.id, lines[0]);
            step = !o.ticketConfirmed ? Step.Listen : o.style == OrderStyle.SelfServe && o.weighedGrams <= 0 ? Step.Weigh : Step.Pay;
            BuildStep();
        }

        /// <summary>接客中のお客さんのせりふを会話欄に足す（訂正・支払い・お礼など）。他の客のせりふは無視する。</summary>
        public void Say(string speakerOrderId, string text)
        {
            if (string.IsNullOrEmpty(text) || speakerOrderId != orderId) return;
            lines.Add(text);
            shownLines = lines.Count;
            RenderDialog();
        }

        private void RenderDialog()
        {
            var o = O;
            dialogName.text = o == null ? "" : o.customerName + "（" + Content.Archetype(o.archetype).Label + "）";
            dialogText.text = string.Join("\n", lines.Take(shownLines).Skip(Mathf.Max(0, shownLines - 5)));
        }

        private void BuildStep()
        {
            Clear(body);
            soupButtons.Clear(); spiceButtons.Clear(); takeoutButtons.Clear();
            confirmButton = null; registerHint = null; changeText = null; trayText = null;
            var o = O;
            title.text = o == null ? "レジ" : "レジ　" + o.customerName;
            switch (step)
            {
                case Step.Listen: BuildListen(o); break;
                case Step.Weigh: BuildWeigh(o); break;
                case Step.Pay: BuildPay(o); break;
                default: BuildDone(); break;
            }
            RenderDialog();
        }

        private RectTransform Column()
        {
            // Unity の偽 null は ?? で判定できないため TryGetComponent を使う。
            if (!body.TryGetComponent<VerticalLayoutGroup>(out var layout)) layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            return body;
        }

        private void Caption(Transform parent, string text, int size = 18, Color? color = null, float height = 28)
            => UIKit.Height(UIKit.Label(parent, text, size, color ?? UIKit.InkSoft, TextAnchor.MiddleLeft, true), height);

        // ───────── 注文を聞く ─────────

        private void BuildListen(CustomerOrder o)
        {
            var col = Column();
            Caption(col, "① 注文票を書く（お客さんの話をよく聞こう）", 18, UIKit.Lacquer);
            Caption(col, "スープ");
            var soups = Sim.Customers.AvailableSoups.ToList();
            for (int rowIndex = 0; rowIndex * 3 < soups.Count; rowIndex++)
            {
                var row = UIKit.Row(col, 46, 6);
                foreach (var soup in soups.Skip(rowIndex * 3).Take(3))
                {
                    string id = soup.Id;
                    var b = UIKit.Button(row, soup.Id == "dry" ? "汁なし" : soup.Name, () => { ticketSoup = id; RefreshTicket(); }, ButtonStyle.Tab, 17);
                    soupButtons.Add((b, id));
                }
            }
            Caption(col, "辛さ");
            var spiceRow = UIKit.Row(col, 46, 6);
            for (int i = 0; i <= 5; i++)
            {
                int level = i;
                var b = UIKit.Button(spiceRow, i.ToString(), () => { ticketSpice = level; RefreshTicket(); }, ButtonStyle.Tab, 19, i >= 4 ? IconKind.Chili : (IconKind?)null);
                spiceButtons.Add((b, level));
            }
            var placeRow = UIKit.Row(col, 46, 6);
            takeoutButtons.Add((UIKit.Button(placeRow, "店内", () => { ticketTakeout = 0; RefreshTicket(); }, ButtonStyle.Tab, 18, IconKind.Home), 0));
            takeoutButtons.Add((UIKit.Button(placeRow, "持ち帰り", () => { ticketTakeout = 1; RefreshTicket(); }, ButtonStyle.Tab, 18, IconKind.Cart), 1));
            string items = o.style == OrderStyle.SelfServe ? "具材：お客さんが選んだボウル（" + o.selected.Count + "品）"
                : o.omakase ? "具材：おまかせ（" + string.Join("・", Omakase.Wishes(o)) + "）"
                : "具材：" + string.Join("・", o.requested.Select(id => Sim.Catalog.Get(id).displayName));
            var note = UIKit.Label(col, items, 15, UIKit.Ink, TextAnchor.UpperLeft);
            UIKit.Height(note, 44);
            if (o.usual)
            {
                var memo = UIKit.Button(col, "常連ノートを見る（ボーナスなし）", () =>
                {
                    hintUsed = true;
                    var regular = S.regulars.FirstOrDefault(r => r.id == o.regularId);
                    if (regular != null) Say(o.id, "（ノート）" + regular.name + "さん：" + CustomerVoice.SoupPhrase(regular.favoriteSoup) + "・" + CustomerVoice.SpicePhrase(regular.favoriteSpice));
                }, ButtonStyle.Gold, 16, IconKind.Book);
                UIKit.Height(memo, 40);
            }
            confirmButton = UIKit.Button(col, "注文票を確定", Confirm, ButtonStyle.Primary, 21, IconKind.Check);
            UIKit.Height(confirmButton, 54);
            RefreshTicket();
        }

        private void RefreshTicket()
        {
            foreach (var (b, id) in soupButtons) Paint(b, id == ticketSoup);
            foreach (var (b, level) in spiceButtons) Paint(b, level == ticketSpice);
            foreach (var (b, value) in takeoutButtons) Paint(b, value == ticketTakeout);
            if (confirmButton != null) confirmButton.interactable = ticketSoup != "" && ticketSpice >= 0 && ticketTakeout >= 0;
        }

        private static void Paint(Button button, bool active)
        {
            var (fill, text) = UIKit.Colors(active ? ButtonStyle.TabActive : ButtonStyle.Tab);
            button.GetComponent<Image>().color = fill;
            foreach (var label in button.GetComponentsInChildren<Text>()) label.color = text;
            foreach (var image in button.GetComponentsInChildren<Image>()) if (image.gameObject != button.gameObject) image.color = text;
        }

        private void Confirm()
        {
            var o = O;
            if (o == null) return;
            if (!Sim.ConfirmTicket(o.id, ticketSoup, ticketSpice, ticketTakeout == 1, hintUsed)) return;
            step = o.style == OrderStyle.SelfServe ? Step.Weigh : Step.Pay;
            BuildStep();
        }

        // ───────── 量る（セルフ式） ─────────

        private void BuildWeigh(CustomerOrder o)
        {
            var col = Column();
            Caption(col, "② お客さんのボウルを量る", 18, UIKit.Lacquer);
            var info = UIKit.Label(col, "セルフ式のお客さんは、自分で選んだ具材のボウルを持ってきます。\nレジ横の秤にのせて重さを量りましょう。", 17, UIKit.Ink, TextAnchor.UpperLeft);
            UIKit.Height(info, 70);
            registerHint = UIKit.Label(col, "— g", 44, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Height(registerHint, 90);
            var weigh = UIKit.Button(col, "秤にのせる", () =>
            {
                if (!Sim.WeighAtRegister(o.id)) return;
                step = Step.Pay;
                BuildStep();
            }, ButtonStyle.Primary, 22, IconKind.Scale);
            UIKit.Height(weigh, 60);
        }

        // ───────── 会計 ─────────

        private void BuildPay(CustomerOrder o)
        {
            var col = Column();
            Caption(col, o.style == OrderStyle.SelfServe ? "③ お会計" : "② お会計", 18, UIKit.Lacquer);
            string detail = o.style == OrderStyle.SelfServe ? o.weighedGrams.ToString("0") + "g"
                : o.omakase ? "おまかせの予算（約" + o.targetGrams.ToString("0") + "g）"
                : o.requested.Sum(id => Sim.Catalog.Get(id).portionGrams).ToString("0") + "g（注文の具材）";
            var price = UIKit.Label(col, "お会計　" + UIKit.Money(o.billFen), 30, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Height(price, 44);
            var sub = UIKit.Label(col, detail + "　100gあたり " + UIKit.Money(S.pricePer100gFen) + (string.IsNullOrEmpty(o.signatureId) ? "" : "（名物 15%増し）"), 15, UIKit.InkSoft, TextAnchor.MiddleLeft);
            UIKit.Height(sub, 24);
            switch (o.payment)
            {
                case PaymentMethod.QrCode:
                    Method(col, IconKind.Info, "QRコード払い", "お客さんのスマホのQRコードを読み取ります。");
                    var scan = UIKit.Button(col, "QRを読み取る", () => { App.Audio.Play(Sfx.QrBeep); PayNow(0); }, ButtonStyle.Primary, 22, IconKind.Check);
                    UIKit.Height(scan, 64);
                    break;
                case PaymentMethod.Card:
                    Method(col, IconKind.Info, "カード払い", "カードを端末に通します。おつりはありません。");
                    var swipe = UIKit.Button(col, "カードを通す", () => { App.Audio.Play(Sfx.CardSwipe); PayNow(0); }, ButtonStyle.Primary, 22, IconKind.Check);
                    UIKit.Height(swipe, 64);
                    break;
                default:
                    BuildCash(col, o);
                    break;
            }
        }

        private void Method(Transform parent, IconKind icon, string name, string description)
        {
            var label = UIKit.Label(parent, name + "　—　" + description, 17, UIKit.Jade, TextAnchor.MiddleLeft, true);
            UIKit.Height(label, 60);
        }

        private void BuildCash(Transform col, CustomerOrder o)
        {
            var tendered = UIKit.Label(col, "お預かり　" + UIKit.Money(o.tenderedFen) + "（現金）", 20, UIKit.Jade, TextAnchor.MiddleLeft, true);
            UIKit.Height(tendered, 30);
            changeText = UIKit.Label(col, "", 20, UIKit.Lacquer, TextAnchor.MiddleLeft, true);
            UIKit.Height(changeText, 30);
            for (int r = 0; r < 2; r++)
            {
                var row = UIKit.Row(col, 58, 6);
                for (int c = 0; c < 4; c++)
                {
                    int index = r * 4 + c;
                    long value = RestaurantSimulation.Denominations[index];
                    var b = UIKit.Button(row, DenominationLabels[index], () =>
                    {
                        tray.Add(value);
                        App.Audio.Play(value >= 500 ? Sfx.BillRustle : Sfx.CoinDrop, .8f);
                        RefreshTray();
                    }, value >= 500 ? ButtonStyle.Secondary : ButtonStyle.Gold, 19);
                    if (value >= 500) b.GetComponent<Image>().color = value >= 5000 ? new Color(.75f, .25f, .3f) : value >= 1000 ? new Color(.32f, .5f, .62f) : new Color(.45f, .35f, .55f);
                }
            }
            trayText = UIKit.Label(col, "", 18, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Height(trayText, 52);
            var actions = UIKit.Row(col, 56, 8);
            UIKit.Button(actions, "1つ戻す", () => { if (tray.Count > 0) tray.RemoveAt(tray.Count - 1); RefreshTray(); }, ButtonStyle.Tab, 17, IconKind.ArrowLeft);
            UIKit.Button(actions, "クリア", () => { tray.Clear(); RefreshTray(); }, ButtonStyle.Tab, 17, IconKind.Cross);
            var give = UIKit.Button(actions, "おつりを渡す", () => PayNow(tray.Sum()), ButtonStyle.Primary, 20, IconKind.Coin);
            UIKit.Height(give, 56, 230);
            RefreshTray();
        }

        private void RefreshTray()
        {
            var o = O;
            if (o == null || trayText == null) return;
            long due = RestaurantSimulation.ExpectedChange(o);
            changeText.text = "おつり　" + UIKit.Money(due) + "（レジの表示）";
            long sum = tray.Sum();
            string pieces = string.Join(" ", tray.GroupBy(x => x).OrderByDescending(g => g.Key)
                .Select(g => DenominationLabels[System.Array.IndexOf(RestaurantSimulation.Denominations, g.Key)] + (g.Count() > 1 ? "×" + g.Count() : "")));
            trayText.text = "トレー：" + UIKit.Money(sum) + (pieces.Length > 0 ? "\n" + pieces : "");
            trayText.color = sum == due ? UIKit.Good : sum > due ? UIKit.Warn : UIKit.Ink;
        }

        private void PayNow(long change)
        {
            var o = O;
            if (o == null) return;
            if (!Sim.Pay(o.id, change)) { tray.Clear(); RefreshTray(); return; }
            step = Step.Done;
            doneTimer = 0;
            BuildStep();
        }

        // ───────── 完了 ─────────

        private void BuildDone()
        {
            var col = Column();
            var o = O;
            Caption(col, "お会計が終わりました", 22, UIKit.Good, 40);
            if (o != null)
            {
                var info = UIKit.Label(col, o.style == OrderStyle.SelfServe ? "ボウルを厨房へ。茹でて仕上げましょう。" : "注文票は厨房へ。冷蔵ケースから具材を取りましょう。", 18, UIKit.Ink, TextAnchor.UpperLeft);
                UIKit.Height(info, 56);
                string id = o.id;
                var cook = UIKit.Button(col, "厨房で作る", () => App.World.Visit(Station.Kitchen, () => App.OpenCooking(id)), ButtonStyle.Primary, 22, IconKind.Pot);
                UIKit.Height(cook, 60);
            }
            var next = Sim.Queue.FirstOrDefault();
            if (next != null)
            {
                string nextId = next.id;
                var b = UIKit.Button(col, "次のお客さん（" + Sim.Queue.Count() + "人待ち）", () => App.OpenRegister(nextId), ButtonStyle.Secondary, 20, IconKind.Bell);
                UIKit.Height(b, 56);
            }
            var store = UIKit.Button(col, "店内に戻る", () => App.BackToStore(), ButtonStyle.Tab, 18, IconKind.Home);
            UIKit.Height(store, 50);
        }

        public override void Refresh()
        {
            var o = O;
            if (o == null || o.stage == OrderStage.Cancelled || (o.stage != OrderStage.Ordering && step != Step.Done) || S.phase == DayPhase.Report) { App.BackToStore(); return; }
            RenderDialog();
        }

        public override void Tick(float deltaTime)
        {
            var o = O;
            if (shownLines < lines.Count)
            {
                lineTimer += deltaTime;
                if (lineTimer > 1.1f)
                {
                    lineTimer = 0;
                    shownLines++;
                    App.Audio.Play(Sfx.Voice, .55f);
                    if (o != null) App.World.Speak(o.id, lines[shownLines - 1], false, 2.6f);
                    RenderDialog();
                }
            }
            if (step == Step.Done) doneTimer += deltaTime;
            var store = App.World.Store;
            if (store != null && o != null)
            {
                if (store.RegisterDisplay != null)
                    store.RegisterDisplay.text = o.billFen <= 0 || (o.style == OrderStyle.SelfServe && o.weighedGrams <= 0) ? "0.00"
                        : step == Step.Pay && o.payment == PaymentMethod.Cash ? (o.billFen / 100m).ToString("0.00") + "\n/" + (RestaurantSimulation.ExpectedChange(o) / 100m).ToString("0.00")
                        : (o.billFen / 100m).ToString("0.00");
                if (store.RegisterScaleDisplay != null) store.RegisterScaleDisplay.text = o.weighedGrams > 0 ? o.weighedGrams.ToString("0") + "g" : "0g";
            }
            if (step == Step.Weigh && registerHint != null && o != null) registerHint.text = o.weighedGrams > 0 ? o.weighedGrams.ToString("0") + " g" : "— g";
        }

        public override void Close()
        {
            if (App.UI.Hud != null) App.UI.Hud.TicketsVisible = true;
            var store = App.World != null ? App.World.Store : null;
            if (store != null && store.RegisterDisplay != null) store.RegisterDisplay.text = "0.00";
            base.Close();
        }
    }
}
