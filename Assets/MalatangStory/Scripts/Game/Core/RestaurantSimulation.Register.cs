using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>
    /// レジでの接客（注文を聞く→注文票を確認→セルフ式は計量→支払いとおつり）。
    /// 注文式はレジで注文内容から代金を受け取り、セルフ式は客が選んだボウルをレジで量って会計する。
    /// </summary>
    public sealed partial class RestaurantSimulation
    {
        /// <summary>おつりに使う紙幣・硬貨（分）。100元・50元・20元・10元・5元・1元・5角・1角。</summary>
        public static readonly long[] Denominations = { 10000, 5000, 2000, 1000, 500, 100, 50, 10 };

        /// <summary>金額を1角（10分）単位に丸める（硬貨で払える最小単位）。</summary>
        public static long RoundToJiao(double fen) => (long)Math.Round(fen / 10d, MidpointRounding.AwayFromZero) * 10;

        /// <summary>レジで接客を始める（Waiting → Ordering）。byPlayer ならスタッフはこの客に手を出さない。</summary>
        public bool TakeOrder(string id, bool byPlayer = true)
        {
            var o = Find(id);
            if ((State.phase != DayPhase.Open && State.phase != DayPhase.Closing) || o == null) return false;
            if (o.stage == OrderStage.Ordering) { if (byPlayer) FocusRegister(o); return true; }
            if (o.stage != OrderStage.Waiting) return false;
            if (TutorialBlocks(Tutorial.Accept, "まずは移動の練習をしましょう。")) return false;
            if (o.style == OrderStyle.SelfServe && o.selected.Count == 0) CustomerDirector.SelectAll(o);
            // おまかせは具材の指定が無く、在庫に合わせて店主が選ぶため、在庫切れによる調整は要らない。
            if (!o.omakase && !AdaptToStock(o)) return false;
            o.stage = OrderStage.Ordering;
            o.visualState = "order";
            o.billFen = o.omakase ? o.budgetFen : PriceFor(o.style == OrderStyle.SelfServe ? o.selected : o.requested, o.signatureId);
            Customers.PreparePayment(o);
            if (byPlayer) FocusRegister(o);
            Emit(SimEventKind.Accepted, o.id);
            Notify(o.customerName + "の注文を聞きましょう。");
            Changed?.Invoke();
            return true;
        }

        private void FocusRegister(CustomerOrder o)
        {
            PlayerFocusOrderId = o.id;
            StaffWork.Release(o.id);
        }

        /// <summary>在庫切れの具材を客が諦めて外す。何も残らなければ断って false。</summary>
        private bool AdaptToStock(CustomerOrder o)
        {
            var list = o.style == OrderStyle.SelfServe ? o.selected : o.requested;
            var missing = list.GroupBy(x => x).Where(g => g.Count() > Math.Max(0, Available(g.Key, o))).ToList();
            if (missing.Count == 0) return true;
            foreach (var g in missing)
                for (int extra = g.Count() - Math.Max(0, Available(g.Key, o)); extra > 0; extra--)
                {
                    if (o.style == OrderStyle.SelfServe)
                    {
                        int index = o.selected.LastIndexOf(g.Key);
                        o.selected.RemoveAt(index); if (index < o.dropTimes.Count) o.dropTimes.RemoveAt(index);
                    }
                    o.requested.Remove(g.Key);
                }
            o.serviceBonus -= 3;
            if (list.Count == 0)
            {
                if (State.tutorialComplete) Cancel(o.id, false);
                return Fail("在庫が足りず、" + o.customerName + "の注文を受けられませんでした。");
            }
            var recipe = Recipes.Find(o.recipeRequest);
            if (recipe != null && !recipe.Ingredients.All(o.requested.Contains))
            {
                // 必須の具材が欠けたらレシピは成り立たないため、ふつうの注文に切り替える（名指しのまま残すと提供時の判定と食い違う）。
                o.recipeRequest = "";
                o.type = Content.Archetype(o.archetype).Label;
            }
            Notify("在庫切れの" + string.Join("・", missing.Select(g => Catalog.Get(g.Key).displayName)) + "は外してもらいました。");
            return true;
        }

        /// <summary>
        /// 聞き取った注文（スープ・辛さ・持ち帰り）を注文票に書いて確認する。
        /// 違っていれば客に訂正され（接客の評価が下がる）、false を返す。usedHint は常連ノートを見たか。
        /// </summary>
        public bool ConfirmTicket(string id, string soupId, int spice, bool takeout, bool usedHint = false)
        {
            if (Find(id)?.delivery == true && !takeout) return Fail("出前の注文は持ち帰りで受け付けます。");
            var o = Find(id);
            if (o == null || o.stage != OrderStage.Ordering) return false;
            if (o.ticketConfirmed) return true;
            bool soupWrong = soupId != o.soup, spiceWrong = spice != o.spice, takeoutWrong = takeout != o.takeout;
            if (soupWrong || spiceWrong || takeoutWrong)
            {
                o.registerMistakes++;
                o.serviceBonus -= 3;
                string line = CustomerVoice.TicketCorrection(o, soupWrong, spiceWrong, takeoutWrong);
                Emit(SimEventKind.CustomerSpeech, o.id, 0, line);
                Notify(o.customerName + "「" + line + "」");
                return false;
            }
            o.ticketConfirmed = true;
            o.usedHint = usedHint;
            if (o.registerMistakes == 0) o.serviceBonus += o.usual && !usedHint ? 5 : 2;
            Emit(SimEventKind.CustomerSpeech, o.id, 1, CustomerVoice.PaymentLine(o));
            Changed?.Invoke();
            return true;
        }

        /// <summary>セルフ式のボウルをレジの秤で量る。</summary>
        public bool WeighAtRegister(string id)
        {
            var o = Find(id);
            if (o == null || o.stage != OrderStage.Ordering || o.style != OrderStyle.SelfServe) return false;
            if (o.selected.Count == 0) return Fail("ボウルに具材が入っていません。");
            o.weighedGrams = Grams(o);
            o.billFen = PriceFor(o.selected, o.signatureId);
            Emit(SimEventKind.Weighed, o.id, 0, o.weighedGrams.ToString("0") + "g");
            Changed?.Invoke();
            return true;
        }

        /// <summary>レジで支払いが可能な状態か（注文票の確認と、セルフ式なら計量が済んでいる）。</summary>
        public bool ReadyToPay(CustomerOrder o)
            => o != null && o.stage == OrderStage.Ordering && o.ticketConfirmed && (o.style != OrderStyle.SelfServe || o.weighedGrams > 0);

        /// <summary>現金払いで渡すべきおつり（分）。</summary>
        public static long ExpectedChange(CustomerOrder o) => o.payment == PaymentMethod.Cash ? Math.Max(0, o.tenderedFen - o.billFen) : 0;

        /// <summary>
        /// 代金を受け取り、おつりを渡す。足りなければ客に指摘されて false。多すぎれば差額は店の損失になる。
        /// 注文式は具材選びへ（Selecting）、セルフ式は茹で待ち（Weighed）へ進む。
        /// </summary>
        public bool Pay(string id, long changeFen)
        {
            var o = Find(id);
            if (o == null || o.stage != OrderStage.Ordering) return false;
            if (!o.ticketConfirmed) return Fail("先に注文票を確認しましょう。");
            if (o.style == OrderStyle.SelfServe && o.weighedGrams <= 0) return Fail("先に秤でボウルを量りましょう。");
            if (changeFen < 0) return false;
            long expected = ExpectedChange(o);
            if (o.payment != PaymentMethod.Cash && changeFen != 0) return Fail(o.payment == PaymentMethod.Card ? "カード払いにおつりはありません。" : "QR払いにおつりはありません。");
            if (changeFen < expected)
            {
                o.registerMistakes++;
                o.serviceBonus -= 4;
                string line = CustomerVoice.ChangeShort(o);
                Emit(SimEventKind.CustomerSpeech, o.id, 0, line);
                Notify(o.customerName + "「" + line + "」");
                return false;
            }
            long loss = changeFen - expected;
            string thanks;
            if (loss > 0)
            {
                State.moneyFen -= loss;
                State.report.changeLossFen += loss;
                thanks = CustomerVoice.ChangeOver(o);
            }
            else
            {
                // スタッフの会計は店主の腕前に数えない（実績・ミッションはプレイヤーのレジ操作だけ）。
                if (o.payment == PaymentMethod.Cash && PlayerFocusOrderId == o.id) { State.report.exactChange++; State.stats.exactChange++; }
                o.serviceBonus += o.registerMistakes == 0 ? 3 : 1;
                thanks = CustomerVoice.ThankYou(o);
            }
            o.paidFen = o.billFen;
            State.moneyFen += o.paidFen;
            State.report.revenueFen += o.paidFen;
            if (o.style == OrderStyle.SelfServe) o.stage = OrderStage.Weighed;
            else o.stage = OrderStage.Selecting;
            o.visualState = "wait";
            if (PlayerFocusOrderId == o.id && o.style == OrderStyle.SelfServe) PlayerFocusOrderId = null;
            Emit(SimEventKind.Paid, o.id, o.paidFen, ((int)o.payment).ToString());
            Emit(SimEventKind.CustomerSpeech, o.id, 1, thanks);
            TutorialAction(Tutorial.Accept);
            Missions.Update(this);
            Notify(RestaurantSimulation.Money(o.paidFen) + "を受け取りました。" + (o.style == OrderStyle.SelfServe ? "ボウルを厨房へ運んで茹でましょう。" : "冷蔵ケースから具材を取りましょう。"));
            Save();
            return true;
        }

        /// <summary>
        /// レジの接客を最後まで正確に行う（スタッフ・自動進行・テスト用）。
        /// 注文票は聞いたとおり、セルフ式は計量し、おつりはぴったり渡す。
        /// </summary>
        public bool Accept(string id) => Accept(id, true);

        internal bool Accept(string id, bool byPlayer)
        {
            var o = Find(id);
            if (o == null || (o.stage != OrderStage.Waiting && o.stage != OrderStage.Ordering)) return false;
            if (!TakeOrder(id, byPlayer)) return false;
            o = Find(id);
            if (o == null || o.stage != OrderStage.Ordering) return false;
            if (!ConfirmTicket(id, o.soup, o.spice, o.takeout)) return false;
            if (o.style == OrderStyle.SelfServe && !WeighAtRegister(id)) return false;
            return Pay(id, ExpectedChange(o));
        }
    }
}
