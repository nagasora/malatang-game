using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>表示層へ知らせるゲーム内の出来事の種類。</summary>
    public enum SimEventKind
    {
        OrderArrived, Accepted, IngredientAdded, IngredientRemoved, Weighed, HeatingStarted, Dropped, Drained, Shaken,
        SoupPoured, Condiment, Finished, Served, Cancelled, CustomerLeft, TableDirty, TableCleared, LevelUp, Unlock,
        Achievement, BoilOver, Purchased, Upgraded, FurnitureChanged, StaffChanged, StaffAction, Review, SpecialEvent,
        DayOpened, Closing, DayEnded, NewDay, Tip, Regular, Signature, Failed, Tutorial,
        /// <summary>客のせりふ（Value 1 は好意的、0 は訂正・不満）。</summary>
        CustomerSpeech, Paid, Discount, Combo, MissionComplete, ContestResult,
        /// <summary>隠しレシピを初めて作った（Id はレシピ ID）。</summary>
        RecipeDiscovered,
        /// <summary>相性の組み合わせを初めて出した（Id は組み合わせ ID）。</summary>
        PairingDiscovered,
        /// <summary>提供した一杯が一杯アルバムに載った（Id は注文 ID、Value は品質）。</summary>
        AlbumAdded,
        /// <summary>「店の物語」の章を完了した（Value は完了した章の番号、1 始まり）。</summary>
        ChapterComplete,
        /// <summary>内装テーマを変えた（Id はテーマ ID）。</summary>
        ThemeChanged,
        /// <summary>食べ終わった器と理由が確定しました。</summary>
        MealFinished
    }

    /// <summary>表示層へ知らせるゲーム内の出来事。</summary>
    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;
        public readonly string Id;
        public readonly long Value;
        public readonly string Text;
        public SimEvent(SimEventKind kind, string id = "", long value = 0, string text = "") { Kind = kind; Id = id; Value = value; Text = text; }
    }

    /// <summary>
    /// 麻辣湯店の営業シミュレーション（UnityEngine 非依存）。
    /// 一日の進行・客の来店・調理・会計・在庫・経営・成長を扱う。調理と経営の操作は partial の別ファイルにある。
    /// </summary>
    public sealed partial class RestaurantSimulation
    {
        public const float DayDuration = 720;
        public const float EatDuration = 28;
        public const int TutorialFinished = 13;
        public const int MaxIngredientsPerBowl = 10;
        public const float BowlTareGrams = 0;

        public readonly IngredientCatalog Catalog;
        public readonly GameState State;
        public readonly IngredientUnlocks Unlocks;
        public readonly CustomerDirector Customers;
        public readonly StaffSystem StaffWork;

        /// <summary>プレイヤーが調理画面で扱っている注文（スタッフはこれに手を出さない）。保存しない。</summary>
        public string PlayerFocusOrderId;

        public string Notice { get; private set; } = "ようこそ！仕入れを確認して営業を始めましょう。";
        public event Action Changed;
        public event Action Checkpoint;
        public event Action<SimEvent> Event;

        public RestaurantSimulation(IngredientCatalog catalog, GameState state = null)
        {
            Catalog = catalog;
            State = state ?? new GameState();
            Unlocks = new IngredientUnlocks(catalog, State.ingredients);
            Customers = new CustomerDirector(this);
            StaffWork = new StaffSystem(this);
            if (state == null)
            {
                foreach (var item in Unlocks.Available) State.stock.Add(new Stock { ingredientId = item.id, quantity = 8 });
                int uid = State.nextFurnitureUid;
                State.furniture.AddRange(DiningLayout.DefaultLayout(ref uid));
                State.nextFurnitureUid = uid;
            }
            else if (State.missions.Count == 0 && State.tutorialComplete && State.phase != DayPhase.Report) Missions.Generate(this);
            // 新規でも保存からの再開でも同じ旬になるよう、どちらの経路でもここで補完する（v1.2 以前の保存にも旬が付く）。
            Seasons.EnsureFeatured(this);
            MorningMarket.Ensure(this);
        }

        public Stock StockFor(string id)
        {
            var entry = State.stock.FirstOrDefault(x => x.ingredientId == id);
            if (entry == null) { entry = new Stock { ingredientId = id }; State.stock.Add(entry); }
            return entry;
        }

        /// <summary>まだ提供・取消されていない注文。</summary>
        public IEnumerable<CustomerOrder> Active => State.orders.Where(x => !x.IsClosed);

        /// <summary>レジに並んでいる客（受付前）。</summary>
        public IEnumerable<CustomerOrder> Queue => State.orders.Where(x => x.stage == OrderStage.Waiting);

        /// <summary>席にいる客（食事中を含む）。</summary>
        public IEnumerable<CustomerOrder> Seated => State.orders.Where(DiningLayout.OccupiesSeat);

        /// <summary>本日の来客見込み（人）。</summary>
        public int Demand
        {
            get
            {
                float seats = DiningLayout.SeatCount(State);
                float baseDemand = 9 + (State.reputation - 50) / 5f + State.popularity / 10f + seats * .7f + State.shopLevel * .7f
                    + Math.Max(0, DiningLayout.Ambience(State)) * .12f - (State.pricePer100gFen - 1800) / 140f;
                float multiplier = Content.Weather(State.weather).DemandMultiplier * Content.DailyEvent(State.dailyEvent).DemandMultiplier;
                return (int)Math.Max(5, Math.Min(48, Math.Round(baseDemand * multiplier * Skills.Demand(State))));
            }
        }

        /// <summary>仕入れ価格の倍率（セール日は割引）。</summary>
        public float PurchaseMultiplier => Content.DailyEvent(State.dailyEvent).PurchaseMultiplier * Skills.Purchase(State);

        public long MinimumPurchaseFen => Unlocks.Available.OrderBy(x => x.purchasePricePerPortion).Take(4).Sum(x => PurchaseCost(x) * 5);
        public static long Cost(IngredientDefinition item) => (long)Math.Round(item.purchasePricePerPortion * 100, MidpointRounding.AwayFromZero);
        public long PurchaseCost(IngredientDefinition item)
            => (long)Math.Round(Cost(item) * (item.isRare ? 1f : PurchaseMultiplier * (Seasons.IsFeatured(State, item.id) ? Seasons.FeaturedPurchase : 1f)), MidpointRounding.AwayFromZero);
        public static string Money(long fen) => (fen / 100m).ToString("#,0.00") + " 元";

        /// <summary>1日の固定費（家賃・光熱費・鍋の維持費・拡張分の家賃）。</summary>
        public long FixedCostFen => 15000 + 3000 + (State.pots - 1) * 1000L + (State.EquipmentLevel("expansion") - 1) * 5000L;

        /// <summary>鍋の数の上限（設備レベル）。</summary>
        public float CookSpeedMultiplier => (State.EquipmentLevel("stove") >= 2 ? 1.25f : 1f) * Skills.CookSpeed(State);

        /// <summary>鮮度の日数への加算（冷蔵庫）。</summary>
        public int FreshnessBonusDays => State.EquipmentLevel("fridge") >= 2 ? 1 : 0;

        internal bool Fail(string message) { Notice = message; Emit(SimEventKind.Failed, "", 0, message); Changed?.Invoke(); return false; }
        internal void Notify(string message) { Notice = message; Changed?.Invoke(); }
        internal void Save() => Checkpoint?.Invoke();
        internal void Emit(SimEventKind kind, string id = "", long value = 0, string text = "") => Event?.Invoke(new SimEvent(kind, id, value, text));
        internal void RaiseChanged() => Changed?.Invoke();

        public CustomerOrder Find(string id) => State.orders.FirstOrDefault(x => x.id == id);

        /// <summary>
        /// まだ鍋に入れていない（在庫を消費していない）注文が確保している数。
        /// 注文式はレジで注文を聞いた時点から、具材を取り終えるまで注文の具材を確保する。
        /// </summary>
        public int Reserved(string ingredientId, CustomerOrder except = null)
            => State.orders.Where(o => o != except && !o.stockConsumed && (o.stage == OrderStage.Selecting || o.stage == OrderStage.Weighed || o.stage == OrderStage.Ordering))
                .Sum(o => ReservedBy(o, ingredientId));

        private static int ReservedBy(CustomerOrder o, string ingredientId)
        {
            int selected = o.selected.Count(x => x == ingredientId);
            bool holdsRequest = o.style == OrderStyle.Ordered && (o.stage == OrderStage.Ordering || (o.stage == OrderStage.Selecting && o.paidFen > 0));
            return holdsRequest ? Math.Max(selected, o.requested.Count(x => x == ingredientId)) : selected;
        }

        /// <summary>他の注文に確保されていない在庫数。</summary>
        public int Available(string ingredientId, CustomerOrder except = null) => StockFor(ingredientId).quantity - Reserved(ingredientId, except);

        /// <summary>在庫1個分の原価（平均取得原価。旧データは標準原価）。</summary>
        public long UnitCost(string ingredientId)
        {
            var stock = StockFor(ingredientId);
            return stock.unitCostFen > 0 ? stock.unitCostFen : Cost(Catalog.Get(ingredientId));
        }

        /// <summary>
        /// プレイヤーが調理画面で扱う注文を設定する。スタッフが調理中の注文は閲覧のみで予約しない。
        /// 予約した注文を対象にしていたスタッフの作業は取り消す。
        /// </summary>
        public bool SetPlayerFocus(string orderId)
        {
            var o = Find(orderId);
            if (o == null || o.handledByStaff) { PlayerFocusOrderId = null; return false; }
            PlayerFocusOrderId = orderId;
            StaffWork.Release(orderId);
            return true;
        }

        /// <summary>スタッフが調理中の注文をプレイヤーが引き継ぐ。</summary>
        public bool TakeOver(string orderId)
        {
            var o = Find(orderId);
            if (o == null || !o.handledByStaff) return false;
            o.handledByStaff = false;
            StaffWork.Release(orderId);
            PlayerFocusOrderId = orderId;
            Notify("スタッフから" + o.customerName + "の調理を引き継ぎました。");
            return true;
        }

        /// <summary>注文に使える通常食材の在庫がある場合だけ開店する。</summary>
        public bool BeginDay()
        {
            if (State.phase != DayPhase.Preparation) return false;
            if (!Unlocks.NormalAvailable.Any(x => StockFor(x.id).quantity > 0)) return Fail("通常食材の在庫がありません。通常食材の仕入れ、または再建支援を利用してください。");
            State.phase = DayPhase.Open;
            State.spawnSeconds = State.tutorialComplete ? 4 : 0;
            State.eventTriggered = false;
            if (!State.tutorialComplete) AddOrder(Customers.CreateTutorial());
            Emit(SimEventKind.DayOpened);
            Notify(State.tutorialComplete ? "開店しました！今日の天気は" + Content.Weather(State.weather).Name + "。" : "初日は練習営業です。画面の案内に従いましょう。");
            Save();
            return true;
        }

        public void Tick(float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            if (State.phase != DayPhase.Open && State.phase != DayPhase.Closing) return;
            bool tutorial = !State.tutorialComplete;
            if (!tutorial)
            {
                State.daySeconds = Math.Min(DayDuration, State.daySeconds + seconds);
                if (State.daySeconds >= DayDuration && State.phase == DayPhase.Open) BeginClosing();
                if (State.phase == DayPhase.Open) SpawnCustomers(seconds);
                TriggerScheduledEvent();
            }
            foreach (var order in State.orders.ToArray()) TickOrder(order, seconds, tutorial);
            AssignSeats();
            if (!tutorial) StaffWork.Tick(seconds);
            if (State.phase == DayPhase.Closing && !Active.Any() && !State.orders.Any(o => o.stage == OrderStage.Served && DiningLayout.OccupiesSeat(o))) EndDay();
        }

        private void BeginClosing()
        {
            State.phase = DayPhase.Closing;
            // 会計前の客は受付終了で帰ってもらう（レジで話している途中の客も、まだ支払っていない）。
            foreach (var waiting in Active.Where(x => x.IsAtRegister).ToArray()) Cancel(waiting.id, false);
            Emit(SimEventKind.Closing);
            Notify("20:00 受付終了。残りの注文を仕上げましょう。");
        }

        private void SpawnCustomers(float seconds)
        {
            State.spawnSeconds -= seconds;
            if (State.spawnSeconds > 0) return;
            int waiting = Queue.Count();
            if (State.spawnedToday < Demand && waiting < 6)
            {
                var order = Customers.Create();
                if (order != null)
                {
                    AddOrder(order);
                    if (Content.Archetype(order.archetype).Pair && string.IsNullOrEmpty(order.groupId)) AddPartner(order);
                }
            }
            float fraction = State.daySeconds / DayDuration;
            float rush = fraction < .11f ? .6f : fraction < .28f ? 1.8f : fraction < .72f ? .6f : fraction < .94f ? 1.6f : .5f;
            State.spawnSeconds = DayDuration / Math.Max(1, Demand) / rush;
        }

        /// <summary>ふたり連れの相手を作り、同じグループにする（同じテーブルに座る）。</summary>
        private void AddPartner(CustomerOrder first)
        {
            first.groupId = "pair-" + first.id;
            var partner = Customers.Create(first.archetype, first.groupId);
            if (partner == null) return;
            partner.takeout = first.takeout = false;
            AddOrder(partner);
        }

        internal void AddOrder(CustomerOrder order)
        {
            State.orders.Add(order);
            State.spawnedToday++;
            Emit(SimEventKind.OrderArrived, order.id, 0, order.customerName);
            if (!string.IsNullOrEmpty(order.regularId)) Emit(SimEventKind.Regular, order.id, 0, order.customerName + "が来店しました。");
            Changed?.Invoke();
        }

        private void TickOrder(CustomerOrder order, float seconds, bool tutorial)
        {
            if (order.IsClosed)
            {
                order.visualSeconds += seconds;
                if (order.stage == OrderStage.Served && order.visualState == "eat")
                {
                    order.eatSeconds += seconds;
                    if (order.eatSeconds >= (tutorial ? 6 : EatDuration)) FinishMeal(order);
                }
                return;
            }
            if (order.stage == OrderStage.Heating)
            {
                order.heatSeconds += seconds * CookSpeedMultiplier;
                StaffWork.AutoDrop(order);
                float slowest = order.selected.Count == 0 ? 0 : order.selected.Max(x => Catalog.Get(x).cookingSeconds);
                if (!order.boiledOver && !tutorial && order.heatSeconds > slowest * 2.2f + 15)
                {
                    order.boiledOver = true;
                    Emit(SimEventKind.BoilOver, order.id, order.pot);
                    Notify("鍋が吹きこぼれています！早く湯切りしましょう。");
                }
            }
            if (tutorial) return;
            // レジで話している間は待たされている感覚が薄いため、忍耐の減りを遅くする。
            order.elapsed += order.stage == OrderStage.Ordering ? seconds * .35f : seconds;
            bool unpaid = order.paidFen == 0 && (order.stage == OrderStage.Waiting || order.stage == OrderStage.Ordering || order.stage == OrderStage.Selecting);
            if (unpaid && order.elapsed > order.patience) { CustomerGivesUp(order); return; }
            // 会計後の客は待ち続けるが、調理に入らないまま長く待たされた場合だけ返金して帰る。
            bool paidWaiting = order.paidFen > 0 && (order.stage == OrderStage.Weighed || order.stage == OrderStage.Selecting);
            if (paidWaiting && order.elapsed > order.patience * 2) CustomerGivesUp(order);
        }

        private void CustomerGivesUp(CustomerOrder order)
        {
            if (PlayerFocusOrderId == order.id) PlayerFocusOrderId = null;
            Satisfaction.BreakCombo(this);
            Cancel(order.id, false);
            Emit(SimEventKind.CustomerLeft, order.id, 1, order.customerName + "が待ちきれずに帰ってしまいました。");
            Notify(order.customerName + "が待ちきれずに帰ってしまいました…");
        }

        private void FinishMeal(CustomerOrder order)
        {
            if (order.mealFinished) return;
            order.leftovers = MealLeftovers.Evaluate(order, Catalog);
            order.mealFinished = true;
            State.report.mealsFinished++; State.stats.mealsFinished++;
            if (order.leftovers.FinishedFood) { State.report.finishedFood++; State.stats.finishedFood++; }
            if (order.leftovers.FinishedSoup) { State.report.finishedSoup++; State.stats.finishedSoup++; }
            foreach (LeftoverIngredient item in order.leftovers.ingredients)
                LifetimeStats.Add(State.report.leftovers, item.ingredientId + "|" + item.reason);
            if (order.seat >= 0)
            {
                State.mealRemains.RemoveAll(x => x.seat == order.seat);
                State.mealRemains.Add(new MealRemains { orderId = order.id, seat = order.seat,
                    soup = string.IsNullOrEmpty(order.chosenSoup) ? order.soup : order.chosenSoup, spice = order.chosenSpice,
                    selected = new List<string>(order.selected), condiments = new List<string>(order.condiments), leftovers = order.leftovers });
            }
            order.visualState = "leave";
            order.visualSeconds = 0;
            Emit(SimEventKind.MealFinished, order.id, 0, CustomerVoice.MealFinished(order, Catalog));
            Progression.CheckAchievements(this);
            if (order.seat >= 0)
            {
                int table = DiningLayout.TableOfSeat(order.seat);
                if (!State.dirtyTables.Contains(table)) State.dirtyTables.Add(table);
                Emit(SimEventKind.TableDirty, table.ToString(), table);
            }
            if (!string.IsNullOrEmpty(order.review)) Emit(SimEventKind.Review, order.id, (long)order.quality, order.review);
            Changed?.Invoke();
        }

        private void AssignSeats()
        {
            foreach (var order in State.orders.Where(o => o.seat < 0 && !o.takeout && o.AwaitsBowl).ToArray())
            {
                if (order.seat >= 0) continue;
                if (order.groupId.StartsWith("pair-", StringComparison.Ordinal)) { AssignPair(order); continue; }
                var free = PickSeat(order);
                if (free == null) break;
                order.seat = free.Value.SeatId;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// ふたり連れは同じテーブルに並んで座る。相手の会計が済むまで待ち、2席空いたテーブルにそろって着席する。
        /// </summary>
        private void AssignPair(CustomerOrder order)
        {
            var mates = State.orders.Where(o => o.groupId == order.groupId && o.stage != OrderStage.Cancelled && o.visualState != "leave").ToList();
            if (mates.Any(o => o.seat < 0 && !o.AwaitsBowl)) return;
            var unseated = mates.Where(o => o.seat < 0).ToList();
            var seated = mates.Where(o => o.seat >= 0).Select(o => DiningLayout.TableOfSeat(o.seat)).ToList();
            var free = DiningLayout.FreeSeats(State).GroupBy(x => x.Table.uid)
                .Where(g => seated.Count == 0 ? g.Count() >= unseated.Count : seated.Contains(g.Key))
                .OrderBy(g => g.Count()).FirstOrDefault();
            if (free == null) return;
            var seats = free.ToList();
            for (int i = 0; i < unseated.Count && i < seats.Count; i++) unseated[i].seat = seats[i].SeatId;
            Changed?.Invoke();
        }

        private SeatInfo? PickSeat(CustomerOrder order)
        {
            var free = DiningLayout.FreeSeats(State).ToList();
            if (free.Count == 0) return null;
            if (!string.IsNullOrEmpty(order.groupId))
            {
                var mates = State.orders.Where(o => o.groupId == order.groupId && o.seat >= 0).Select(o => DiningLayout.TableOfSeat(o.seat)).ToList();
                var sameTable = free.FirstOrDefault(x => mates.Contains(x.Table.uid));
                if (mates.Count > 0 && sameTable.Table != null) return sameTable;
                var roomy = free.GroupBy(x => x.Table.uid).OrderByDescending(g => g.Count()).First().First();
                return roomy;
            }
            // 一人客は相席を避け、空いているテーブルから座る。
            var used = new HashSet<int>(Seated.Select(o => DiningLayout.TableOfSeat(o.seat)));
            var empty = free.FirstOrDefault(x => !used.Contains(x.Table.uid));
            return empty.Table != null ? empty : free[0];
        }

        private void TriggerScheduledEvent()
        {
            if (State.eventTriggered || State.eventSeconds < 0 || State.daySeconds < State.eventSeconds || State.phase != DayPhase.Open) return;
            State.eventTriggered = true;
            switch (State.dailyEvent)
            {
                case "critic":
                    var critic = Customers.Create("critic");
                    if (critic != null) { AddOrder(critic); Emit(SimEventKind.SpecialEvent, critic.id, 0, "グルメ評論家が来店しました！"); Notify("グルメ評論家が来店！最高の一杯でもてなしましょう。"); }
                    break;
                case "influencer":
                    var influencer = Customers.Create("influencer");
                    if (influencer != null) { AddOrder(influencer); Emit(SimEventKind.SpecialEvent, influencer.id, 0, "人気配信者が来店しました！"); Notify("人気配信者が来店！映える一杯を作りましょう。"); }
                    break;
                case "group":
                    string group = "group-" + State.day;
                    for (int i = 0; i < 4; i++)
                    {
                        var member = Customers.Create(i == 0 ? "office" : null, group);
                        if (member == null) break;
                        member.type = "団体客・" + member.type;
                        AddOrder(member);
                    }
                    Emit(SimEventKind.SpecialEvent, group, 4, "団体客が来店しました！");
                    Notify("4人組の団体客が来店しました！");
                    break;
                case "contest":
                    string panel = "contest-" + State.day;
                    for (int i = 0; i < 3; i++)
                    {
                        var judge = Customers.Create("judge", panel);
                        if (judge == null) break;
                        judge.customerName = "審査員 " + judge.customerName;
                        AddOrder(judge);
                    }
                    Emit(SimEventKind.SpecialEvent, panel, 3, "麻辣湯グランプリの審査員が来店しました！");
                    Notify("グランプリの審査員3人が来店！3杯の平均点で賞が決まります。");
                    break;
            }
        }

        private void EndDay()
        {
            if (State.phase == DayPhase.Report) return;
            var r = State.report;
            State.phase = DayPhase.Report;
            r.fixedCostFen = State.tutorialComplete ? FixedCostFen : 0;
            r.wagesFen = State.tutorialComplete ? State.staff.Sum(x => Content.StaffFor(x.role).DailyWageFen) : 0;
            State.moneyFen -= r.fixedCostFen + r.wagesFen;
            float before = State.reputation;
            float satisfaction = r.served == 0 ? 0 : (float)State.orders.Where(o => o.stage == OrderStage.Served).Average(o => o.report.satisfaction);
            if (r.served > 0) State.reputation = State.reputation * .85f + satisfaction * .15f - r.lost * .8f;
            else State.reputation -= r.lost * .8f;
            State.reputation = Math.Max(0, Math.Min(100, State.reputation + r.reputationDelta));
            r.reputationDelta = (int)Math.Round(State.reputation - before);
            float ambience = Math.Max(0, DiningLayout.Ambience(State));
            State.popularity = Math.Min(100, Math.Max(0, State.popularity * .7f + (State.reputation * .5f + State.regulars.Count * 1.5f + ambience * .3f) * .3f + r.popularityDelta));
            if (r.served >= 10 && r.lost == 0) { State.stats.flawlessDays++; r.highlights.Add("取りこぼしゼロの営業！"); }
            Missions.Update(this, true);
            Contest.Conclude(this);
            Chapters.Check(this);
            DayGrade.Record(State);
            if (r.served >= 10 && r.mealsFinished == r.served && r.finishedFood == r.mealsFinished)
                State.stats.allFinishedDays++;
            State.stats.bestAmbience = Math.Max(State.stats.bestAmbience, (int)ambience);
            Progression.CheckAchievements(this);
            Emit(SimEventKind.DayEnded);
            Notify("営業終了！レポートを確認して、翌日の準備をしましょう。");
            Save();
        }

        public bool NextDay()
        {
            if (State.phase != DayPhase.Report) return false;
            // 開店前に保管上限まで仕入れていると最後の仕入れ練習ができないため、その場合は練習済みとして扱う。
            if (!State.tutorialComplete && State.tutorialStep == Tutorial.Purchase && Unlocks.Available.All(x => StockFor(x.id).quantity >= State.storageCapacity))
            { State.tutorialStep = TutorialFinished; State.tutorialComplete = true; }
            if (!State.tutorialComplete) return Fail("初日の最後に、仕入れを1回行いましょう。");
            State.day = checked(State.day + 1);
            State.phase = DayPhase.Preparation;
            State.daySeconds = 0; State.spawnedToday = 0; State.spawnSeconds = 0;
            State.orders.Clear(); State.dirtyTables.Clear(); State.mealRemains.Clear();
            var reviews = State.report.reviews;
            State.recentReviews.InsertRange(0, reviews.Take(6));
            if (State.recentReviews.Count > 20) State.recentReviews.RemoveRange(20, State.recentReviews.Count - 20);
            State.report = new DailyReport();
            int spoiled = 0;
            foreach (var stock in State.stock)
            {
                if (stock.quantity == 0) { stock.ageDays = 0; continue; }
                stock.ageDays++;
                if (stock.ageDays >= Catalog.Get(stock.ingredientId).freshnessDays + FreshnessBonusDays) { spoiled += stock.quantity; stock.quantity = 0; stock.ageDays = 0; }
            }
            foreach (var member in State.staff) { member.busySeconds = 0; member.task = ""; member.taskTarget = ""; }
            // 注文IDには日付が含まれるため、精算済みIDは当日分だけ保持すれば重複精算を防げる。
            State.ingredients.settledOrderIds.Clear();
            DailyEvents.Roll(this);
            MorningMarket.Ensure(this);
            Emit(SimEventKind.NewDay, "", State.day);
            Notify(State.day + "日目。天気は" + Content.Weather(State.weather).Name + "、" + Content.DailyEvent(State.dailyEvent).Name + "。" + (spoiled > 0 ? "鮮度切れで" + spoiled + "個を廃棄しました。" : ""));
            Save();
            return true;
        }

        /// <summary>鮮度の点数（0〜100）。</summary>
        public float FreshnessScore(string ingredientId) => Math.Max(0f, 100f - StockFor(ingredientId).ageDays * 18f);

        /// <summary>鍋が空いているか。</summary>
        public int FreePot() => Enumerable.Range(0, State.pots)
            .Where(p => !Active.Any(x => x.pot == p && x.stage == OrderStage.Heating)).DefaultIfEmpty(-1).First();
    }
}
