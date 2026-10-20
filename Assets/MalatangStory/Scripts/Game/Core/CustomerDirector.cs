using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>
    /// 客の生成。タイプ（性格）・常連・名物メニュー・天気から、具材・スープ・辛さ・調味料の要望と忍耐を決める。
    /// 乱数は日付と注文番号から作るため、同じ状態からは同じ客が来る（テストと再現性のため）。
    /// </summary>
    public sealed class CustomerDirector
    {
        private readonly RestaurantSimulation sim;
        public CustomerDirector(RestaurantSimulation simulation) { sim = simulation; }

        private GameState S => sim.State;

        /// <summary>注文番号と日付から決定的な乱数を作る。</summary>
        public Random RandomFor(int salt) => new Random(unchecked(S.day * 7919 + S.nextOrderNumber * 104729 + salt * 31 + StableHash(S.storeId)));

        /// <summary>実行環境に依存しない文字列ハッシュ（string.GetHashCode は実行ごとに変わり得るため使わない）。</summary>
        public static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text ?? "") hash = hash * 31 + c;
                return hash;
            }
        }

        /// <summary>現在の店舗レベルで使えるスープ。</summary>
        public IEnumerable<SoupDefinition> AvailableSoups => Content.Soups.Where(x => x.UnlockLevel <= S.shopLevel);

        /// <summary>現在の店舗レベルで使える調味料。</summary>
        public IEnumerable<CondimentDefinition> AvailableCondiments => Content.Condiments.Where(x => x.UnlockLevel <= S.shopLevel);

        /// <summary>客と注文を1件作る。通常食材の在庫がない場合は null。</summary>
        public CustomerOrder Create(string forcedArchetype = null, string groupId = "")
        {
            var stocked = sim.Unlocks.NormalAvailable.Where(x => sim.Available(x.id) > 0).OrderBy(x => x.id, StringComparer.Ordinal).ToArray();
            if (stocked.Length == 0) return null;
            var random = RandomFor(forcedArchetype == null ? 0 : forcedArchetype.Length);
            var weather = Content.Weather(S.weather);

            RegularCustomer regular = null;
            float fraction = S.daySeconds / RestaurantSimulation.DayDuration;
            if (forcedArchetype == null && S.regulars.Count > 0 && random.NextDouble() < Math.Min(.35, S.regulars.Count * .06))
                regular = S.regulars[random.Next(S.regulars.Count)];
            // 常連も時間帯の習慣は変わらない（夜の常連は昼には来ない）。
            if (regular != null && Content.Archetype(regular.archetype).HourWeight(fraction) <= 0) regular = null;
            var archetype = Content.Archetype(forcedArchetype ?? regular?.archetype ?? PickArchetype(random));

            var order = new CustomerOrder
            {
                id = S.storeId + ":" + S.day + ":" + S.nextOrderNumber,
                archetype = archetype.Id,
                regularId = regular?.id ?? "",
                groupId = groupId,
                appearanceSeed = regular?.appearanceSeed ?? random.Next(),
                stage = OrderStage.Waiting,
            };
            string given = archetype.Id == "foreigner" ? Content.ForeignNames[random.Next(Content.ForeignNames.Length)] : PickName(random);
            order.customerName = regular != null ? regular.name + "さん"
                : archetype.Id == "critic" ? "評論家 " + given + "さん"
                : archetype.Id == "influencer" ? "配信者 " + given + "さん"
                : archetype.Id == "celebrity" ? "帽子の" + given + "さん"
                : given + "さん";
            order.type = regular != null ? "常連・" + archetype.Label : archetype.Label;
            order.style = random.NextDouble() < archetype.SelfServeChance ? OrderStyle.SelfServe : OrderStyle.Ordered;
            order.takeout = string.IsNullOrEmpty(groupId) && random.NextDouble() < archetype.TakeoutChance + weather.TakeoutBonus;

            // 子ども連れは辛さ0が前提のため、辛さが決まっている名物メニューは頼まない。
            var signature = forcedArchetype == null && regular == null && !archetype.Child && archetype.ForbiddenCategories.Length == 0 ? PickSignature(random) : null;
            if (signature != null)
            {
                order.signatureId = signature.id;
                order.requested.AddRange(signature.ingredients);
                order.soup = signature.soup;
                order.spice = signature.spice;
                order.requestedCondiments.AddRange(signature.condiments.Distinct());
                order.style = OrderStyle.Ordered;
                order.type = "名物「" + signature.name + "」";
            }
            else if (TryRecipeRequest(random, archetype, regular, order)) { }
            else
            {
                order.soup = regular != null && random.NextDouble() < .8 && AvailableSoups.Any(x => x.Id == regular.favoriteSoup)
                    ? regular.favoriteSoup : PickSoup(random, archetype, weather);
                int spice = regular != null ? regular.favoriteSpice : random.Next(archetype.MinSpice, archetype.MaxSpice + 1) + weather.SpiceShift;
                order.spice = archetype.Child ? 0 : Math.Max(0, Math.Min(5, spice));
                var edible = stocked.Where(x => !archetype.ForbiddenCategories.Contains(x.category)).ToArray();
                if (edible.Length == 0) return null;
                PickIngredients(random, archetype, edible, regular, order.requested);
                var condiments = AvailableCondiments.Where(x => x.Id != Content.ChiliOil).Select(x => x.Id).ToList();
                var liked = (regular?.likedCondiments ?? archetype.LikedCondiments.ToList()).Where(condiments.Contains).ToList();
                foreach (string id in liked) if (random.NextDouble() < .55) order.requestedCondiments.Add(id);
                // スープ・辛さ・調味料は通常の客と同じに決めてから、具材の指定だけをおまかせの希望に置き換える。
                if (regular == null && forcedArchetype == null && Omakase.Wants(S, archetype, random))
                    Omakase.Fill(order, archetype, edible, S.pricePer100gFen, random);
            }
            var available = AvailableCondiments.Select(x => x.Id).ToList();
            var disliked = (regular?.dislikedCondiments ?? archetype.DislikedCondiments.ToList()).Where(available.Contains).ToList();
            if (available.Contains("coriander") && !disliked.Contains("coriander") && !order.requestedCondiments.Contains("coriander") && random.NextDouble() < .18) disliked.Add("coriander");
            order.dislikedCondiments.AddRange(disliked.Where(x => !order.requestedCondiments.Contains(x)).Distinct());
            if (order.spice == 0 && !order.dislikedCondiments.Contains(Content.ChiliOil)) order.dislikedCondiments.Add(Content.ChiliOil);

            // 何度も来ている常連は「いつもの」で頼む。スープと辛さは好みどおりで、注文票は店主の記憶頼みになる。
            if (regular != null && regular.visits >= 3 && string.IsNullOrEmpty(order.signatureId) && random.NextDouble() < .45
                && AvailableSoups.Any(x => x.Id == regular.favoriteSoup))
            {
                order.usual = true;
                order.style = OrderStyle.Ordered;
                order.soup = regular.favoriteSoup;
                order.spice = archetype.Child ? 0 : Math.Max(0, Math.Min(5, regular.favoriteSpice));
            }
            if (archetype.Id == "rider") order.takeout = true;

            float ambienceBonus = Math.Min(.3f, Math.Max(0, DiningLayout.Ambience(S)) * .01f);
            order.patience = archetype.Patience * (1 + ambienceBonus) * (regular != null ? 1.2f : 1f) * Skills.Patience(S) * (S.tutorialComplete ? 1f : 10f);
            if (order.style == OrderStyle.SelfServe) SelectAll(order);
            S.nextOrderNumber++;
            return order;
        }

        /// <summary>
        /// 見つけた隠しレシピを名前で頼む（一見客の約12%）。レシピが未発見なら乱数を消費しないため、
        /// レシピを見つける前の客の並びは従来と同じになる。
        /// </summary>
        private bool TryRecipeRequest(Random random, CustomerArchetype archetype, RegularCustomer regular, CustomerOrder order)
        {
            if (S.recipes.Count == 0 || regular != null || !S.tutorialComplete) return false;
            if (random.NextDouble() >= .12) return false;
            var options = Recipes.Orderable(sim, archetype);
            if (options.Count == 0) return false;
            var recipe = options[random.Next(options.Count)];
            order.recipeRequest = recipe.Id;
            order.style = OrderStyle.Ordered;
            order.soup = string.IsNullOrEmpty(recipe.Soup) ? PickSoup(random, archetype, Content.Weather(S.weather)) : recipe.Soup;
            order.spice = Recipes.SpiceFor(recipe, archetype);
            order.requested.AddRange(recipe.Ingredients);
            order.requestedCondiments.AddRange(recipe.Condiments);
            order.type = "レシピ「" + recipe.Name + "」";
            return true;
        }

        /// <summary>常連客と同じ名前にならないよう、名前を選ぶ。</summary>
        private string PickName(Random random)
        {
            var taken = new HashSet<string>(S.regulars.Select(r => r.name));
            var pool = Content.GivenNames.Where(n => !taken.Contains(n)).ToArray();
            if (pool.Length == 0) pool = Content.GivenNames;
            return pool[random.Next(pool.Length)];
        }

        /// <summary>チュートリアル用の固定の客。</summary>
        public CustomerOrder CreateTutorial()
        {
            var order = new CustomerOrder
            {
                id = S.storeId + ":" + S.day + ":" + S.nextOrderNumber++,
                customerName = "リンさん", type = "はじめてのお客さん", archetype = "office", style = OrderStyle.Ordered,
                soup = "mala", spice = 1, appearanceSeed = 7, patience = 99999, stage = OrderStage.Waiting
            };
            order.requested.AddRange(new[] { "mung_bean_noodle", "bok_choy", "enoki", "yuba" });
            return order;
        }

        /// <summary>
        /// 支払い方法と、現金なら客が出すお金を決める。
        /// 初日の練習は「おつりを渡す」練習のため、必ず50元札などの現金で払う。
        /// </summary>
        public void PreparePayment(CustomerOrder o)
        {
            var random = new Random(unchecked(StableHash(o.id) * 397 + 11));
            var archetype = Content.Archetype(o.archetype);
            if (!S.tutorialComplete) o.payment = PaymentMethod.Cash;
            else
            {
                var methods = new[] { (PaymentMethod.Cash, archetype.CashWeight), (PaymentMethod.QrCode, archetype.QrWeight), (PaymentMethod.Card, archetype.CardWeight) };
                o.payment = methods.Sum(m => m.Item2) <= 0 ? PaymentMethod.Cash : Weighted(random, methods, m => m.Item2).Item1;
            }
            o.tenderedFen = o.payment == PaymentMethod.Cash ? Tender(o.billFen, random, !S.tutorialComplete) : 0;
        }

        /// <summary>現金で出すお金（分）。ちょうど・10元単位・20元単位・50元札・100元札などから自然なものを選ぶ。</summary>
        public static long Tender(long billFen, Random random, bool practice = false)
        {
            long Ceil(long unit) => (billFen + unit - 1) / unit * unit;
            if (practice) return Ceil(5000) == billFen ? billFen + 5000 : Ceil(5000);
            var options = new List<(long amount, float weight)>
            {
                (billFen, .12f), (Ceil(100), .12f), (Ceil(1000), .22f), (Ceil(2000), .14f), (Ceil(5000), .22f), (Ceil(10000), .18f),
                (Ceil(500), .08f),
            };
            var valid = options.Where(x => x.amount >= billFen).GroupBy(x => x.amount).Select(g => (g.Key, g.Sum(x => x.weight))).ToList();
            return Weighted(random, valid, x => x.Item2).Item1;
        }

        /// <summary>セルフ式の客が自分で取った具材をボウルに入れる。</summary>
        public static void SelectAll(CustomerOrder order)
        {
            order.selected.Clear(); order.dropTimes.Clear();
            foreach (string id in order.requested) { order.selected.Add(id); order.dropTimes.Add(-1); }
        }

        /// <summary>店舗レベルと時間帯（昼・放課後・夜など）に合わせて客タイプを選ぶ。</summary>
        private string PickArchetype(Random random)
        {
            float fraction = S.daySeconds / RestaurantSimulation.DayDuration;
            bool nightMarket = S.dailyEvent == "night_market" && fraction > .55f;
            var pool = Content.Archetypes.Where(x => x.SpawnWeight > 0 && x.MinShopLevel <= S.shopLevel).ToArray();
            return Weighted(random, pool, x => x.SpawnWeight * x.HourWeight(fraction) * (nightMarket && (x.Id == "couple" || x.Id == "drunk" || x.Id == "tourist") ? 2f : 1f)).Id;
        }

        private string PickSoup(Random random, CustomerArchetype archetype, WeatherDefinition weather)
        {
            var soups = AvailableSoups.ToArray();
            return Weighted(random, soups, x =>
            {
                float w = archetype.SoupWeights.TryGetValue(x.Id, out var value) ? value : .3f;
                if (x.Id == weather.FavoredSoup) w *= 2;
                return x.Id == Seasons.Of(S.day).FavoredSoup ? w * 1.4f : w;
            }).Id;
        }

        /// <summary>名物が非空で、必要数の通常食材を確保できるかを注文生成とミッションで共通判定する。</summary>
        public bool CanOrderSignature(SignatureDish dish) => dish != null && dish.ingredients.Count > 0
            && dish.ingredients.GroupBy(x => x).All(g => sim.Unlocks.IsNormalUnlocked(g.Key) && sim.Available(g.Key) >= g.Count());

        /// <summary>空や希少入りの名物を除き、通常食材の在庫で注文を満たせる名物だけを選ぶ。</summary>
        private SignatureDish PickSignature(Random random)
        {
            foreach (SignatureDish dish in S.signatures)
            {
                if (!CanOrderSignature(dish)) continue;
                float average = dish.timesSold == 0 ? 70 : dish.qualitySum / dish.timesSold;
                double chance = Math.Min(.25, .04 + dish.timesSold * .004) * (average / 80f);
                if (random.NextDouble() < chance) return dish;
            }
            return null;
        }

        private void PickIngredients(Random random, CustomerArchetype archetype, IngredientDefinition[] stocked, RegularCustomer regular, List<string> into)
        {
            int count = random.Next(archetype.MinItems, archetype.MaxItems + 1);
            var pool = stocked.ToList();
            if (regular != null)
                foreach (string id in regular.favorites.Where(f => pool.Any(p => p.id == f)).Take(count - 1))
                { into.Add(id); pool.RemoveAll(p => p.id == id); }
            if (archetype.CategoryWeights.ContainsKey("noodle") && into.All(id => sim.Catalog.Get(id).category != "noodle"))
            {
                var noodles = pool.Where(x => x.category == "noodle").ToArray();
                if (noodles.Length > 0 && random.NextDouble() < .8)
                {
                    var noodle = Weighted(random, noodles, x => x.popularity);
                    into.Add(noodle.id); pool.Remove(noodle);
                }
            }
            while (into.Count < count && pool.Count > 0)
            {
                var item = Weighted(random, pool.ToArray(), x =>
                    (archetype.CategoryWeights.TryGetValue(x.category, out var w) ? w : .15f) * Math.Max(5, x.popularity) / 50f
                    * (Seasons.IsFeatured(S, x.id) ? 3f : 1f));
                into.Add(item.id); pool.Remove(item);
            }
        }

        /// <summary>重み付きで1つ選ぶ。</summary>
        public static T Weighted<T>(Random random, IList<T> items, Func<T, float> weight)
        {
            float total = items.Sum(x => Math.Max(0, weight(x)));
            if (total <= 0) return items[random.Next(items.Count)];
            double roll = random.NextDouble() * total;
            foreach (var item in items)
            {
                roll -= Math.Max(0, weight(item));
                if (roll <= 0) return item;
            }
            return items[items.Count - 1];
        }
    }
}
