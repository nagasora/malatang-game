using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>調理工程（受付→具材→計量→茹で→湯切り→仕上げ→提供）とチュートリアル。</summary>
    public sealed partial class RestaurantSimulation
    {
        /// <summary>チュートリアルの段階。</summary>
        public static class Tutorial
        {
            public const int Move = 0, Accept = 1, Pick = 2, Weigh = 3, Heat = 4, Drain = 5, Shake = 6, Soup = 7, Spice = 8, Serve = 9, Clean = 10, Close = 11, Purchase = 12;
        }

        /// <summary>チュートリアルを次へ進める（指定の段階のときだけ）。</summary>
        public void TutorialAction(int step)
        {
            if (State.tutorialComplete || State.tutorialStep != step) return;
            if (step != Tutorial.Purchase && State.phase != DayPhase.Open) return;
            State.tutorialStep++;
            Emit(SimEventKind.Tutorial, "", State.tutorialStep);
            Changed?.Invoke();
            Save();
        }

        private bool TutorialBlocks(int requiredStep, string message)
        {
            if (State.tutorialComplete || State.tutorialStep >= requiredStep) return false;
            Fail(message);
            return true;
        }

        private bool StaffOwns(CustomerOrder o) => o.handledByStaff;

        public bool AddIngredient(string orderId, string ingredientId)
        {
            var o = Find(orderId);
            if (o == null || o.stage != OrderStage.Selecting || !Unlocks.IsUnlocked(ingredientId)) return false;
            if (!ChallengeRules.AllowsIngredient(State, Catalog.Get(ingredientId))) return Fail("チャレンジで使えない具材です。");
            if (o.selected.Count >= MaxIngredientsPerBowl) return Fail("1杯の具材は" + MaxIngredientsPerBowl + "個までです。");
            if (o.selected.Count(x => x == ingredientId) >= Available(ingredientId, o)) return Fail(Catalog.Get(ingredientId).displayName + "は在庫切れです（他の注文で使用中を含む）。");
            o.selected.Add(ingredientId);
            o.dropTimes.Add(-1);
            Emit(SimEventKind.IngredientAdded, o.id, 0, ingredientId);
            if (State.tutorialStep == Tutorial.Pick && SameIngredients(o)) TutorialAction(Tutorial.Pick);
            Changed?.Invoke();
            return true;
        }

        public void RemoveIngredient(string orderId, string ingredientId)
        {
            var o = Find(orderId);
            if (o == null || o.stage != OrderStage.Selecting) return;
            int index = o.selected.LastIndexOf(ingredientId);
            if (index < 0) return;
            o.selected.RemoveAt(index);
            if (index < o.dropTimes.Count) o.dropTimes.RemoveAt(index);
            Emit(SimEventKind.IngredientRemoved, o.id, 0, ingredientId);
            Changed?.Invoke();
        }

        /// <summary>具材の合計重量（g）。</summary>
        public float Grams(CustomerOrder o) => BowlTareGrams + o.selected.Sum(x => Catalog.Get(x).portionGrams);

        /// <summary>重量から売価（分）を求める。名物メニューは15%増し。1角単位に丸める。</summary>
        public long PriceFor(CustomerOrder o) => PriceFor(o.selected, o.signatureId);

        /// <summary>具材の一覧から売価（分）を求める。</summary>
        public long PriceFor(System.Collections.Generic.IEnumerable<string> ingredients, string signatureId)
        {
            double price = (BowlTareGrams + ingredients.Sum(x => Catalog.Get(x).portionGrams)) * State.pricePer100gFen / 100d;
            if (!string.IsNullOrEmpty(signatureId)) price *= 1.15;
            return RoundToJiao(price);
        }

        /// <summary>
        /// 厨房の秤で量る。レジで支払い済みの注文は重さの確認だけ、旧データの未会計の注文はここで会計する。
        /// </summary>
        public bool Weigh(string orderId)
        {
            var o = Find(orderId);
            if (o == null || o.stage != OrderStage.Selecting) return false;
            if (o.selected.Count == 0) return Fail("ボウルに具材を入れてから量りましょう。");
            if (TutorialBlocks(Tutorial.Weigh, "注文の具材（春雨・チンゲン菜・えのき・湯葉）をボウルに入れましょう。")) return false;
            bool prepaid = o.paidFen > 0;
            var shortages = o.selected.GroupBy(x => x).Where(g => g.Count() > Available(g.Key, o)).ToList();
            if (shortages.Count > 0)
            {
                if (o.style != OrderStyle.SelfServe || prepaid) return Fail(Catalog.Get(shortages[0].Key).displayName + "が足りません。ボウルから外してください。");
                // 旧データ（v2）の未会計セルフ式は、客が選んだ具材を外す画面がないため、不足分を外して量る。
                if (!AdaptToStock(o)) return false;
            }
            o.weighedGrams = Grams(o);
            if (!prepaid)
            {
                o.paidFen = PriceFor(o);
                o.billFen = o.paidFen;
                State.moneyFen += o.paidFen;
                State.report.revenueFen += o.paidFen;
            }
            o.stage = OrderStage.Weighed;
            Emit(SimEventKind.Weighed, o.id, prepaid ? 0 : o.paidFen, o.weighedGrams.ToString("0") + "g");
            TutorialAction(Tutorial.Weigh);
            float ordered = o.omakase ? o.targetGrams : o.requested.Sum(x => Catalog.Get(x).portionGrams);
            Notify(o.weighedGrams.ToString("0") + "g" + (prepaid ? (Math.Abs(o.weighedGrams - ordered) < 1 ? "。注文どおりです！" : "（注文は" + ordered.ToString("0") + "g）。") : "で " + Money(o.paidFen) + "。会計しました！") + "網じゃくに入れて茹でましょう。");
            Save();
            return true;
        }

        /// <summary>網じゃくを鍋に入れて茹で始める。dropAll が false なら具材は後から1つずつ入れる。</summary>
        public bool StartHeating(string orderId, bool dropAll = true)
        {
            var o = Find(orderId);
            if (o == null || o.stage != OrderStage.Weighed) return o != null && o.stage == OrderStage.Selecting ? Fail("先に秤で量りましょう。") : false;
            int pot = FreePot();
            if (pot < 0) return Fail("鍋がすべて使用中です。先の料理を湯切りしましょう。");
            foreach (var group in o.selected.GroupBy(x => x))
                if (StockFor(group.Key).quantity < group.Count()) return Fail(Catalog.Get(group.Key).displayName + "の在庫が足りません。");
            foreach (var ingredient in o.selected)
            {
                long cost = UnitCost(ingredient);
                StockFor(ingredient).quantity--;
                o.consumedCostFen += cost;
                State.report.foodCostFen += cost;
            }
            o.stockConsumed = true;
            o.pot = pot;
            o.heatSeconds = 0;
            while (o.dropTimes.Count < o.selected.Count) o.dropTimes.Add(-1);
            for (int i = 0; i < o.dropTimes.Count; i++) o.dropTimes[i] = dropAll ? 0 : -1;
            o.usedVisibleGauge = State.gaugeMode != 2 || !State.tutorialComplete || o.handledByStaff;
            o.stage = OrderStage.Heating;
            Emit(SimEventKind.HeatingStarted, o.id, pot);
            TutorialAction(Tutorial.Heat);
            Notify("茹で始めました。茹で時間の長い具材から入れると、すべてがちょうど良く仕上がります。");
            Save();
            return true;
        }

        /// <summary>茹で中の網じゃくに具材を1つ入れる。</summary>
        public bool DropIngredient(string orderId, int index)
        {
            var o = Find(orderId);
            if (o == null || o.stage != OrderStage.Heating || index < 0 || index >= o.dropTimes.Count || o.dropTimes[index] >= 0) return false;
            o.dropTimes[index] = o.heatSeconds;
            Emit(SimEventKind.Dropped, o.id, index, o.selected[index]);
            Changed?.Invoke();
            return true;
        }

        /// <summary>まだ入れていない具材をすべて鍋に入れる。</summary>
        public void DropRemaining(string orderId)
        {
            var o = Find(orderId);
            if (o == null || o.stage != OrderStage.Heating) return;
            for (int i = 0; i < o.dropTimes.Count; i++) if (o.dropTimes[i] < 0) DropIngredient(orderId, i);
        }

        /// <summary>具材ごとの茹で加減（1 でちょうど良い）。</summary>
        public float Doneness(CustomerOrder o, int index) => QualityEvaluator.Doneness(o, index, Catalog);

        /// <summary>全具材が茹で上がるまでの目安（秒）。投入済みの具材で最も遅いもの。</summary>
        public float SecondsUntilDone(CustomerOrder o)
        {
            float worst = 0;
            for (int i = 0; i < o.selected.Count; i++)
            {
                float cook = Catalog.Get(o.selected[i]).cookingSeconds;
                float remaining = o.dropTimes.Count > i && o.dropTimes[i] >= 0 ? cook - (o.heatSeconds - o.dropTimes[i]) : cook;
                worst = Math.Max(worst, remaining / CookSpeedMultiplier);
            }
            return worst;
        }

        /// <summary>v1 互換：全具材の平均茹で時間。</summary>
        public float TargetHeat(CustomerOrder o) => o.selected.Count == 0 ? 0 : o.selected.Average(x => Catalog.Get(x).cookingSeconds);

        /// <summary>網じゃくを引き上げる（湯切り工程へ）。</summary>
        public bool Drain(string id)
        {
            var o = Find(id);
            if (o == null || o.stage != OrderStage.Heating) return false;
            if (o.dropTimes.Any(t => t < 0)) return Fail("まだ鍋に入れていない具材があります。");
            if (!State.tutorialComplete && Enumerable.Range(0, o.selected.Count).Any(i => Doneness(o, i) < .8f))
                return Fail("まだ生の具材があります。具材の色が変わるまで待ちましょう。");
            o.stage = OrderStage.Drained;
            o.pot = -1;
            o.shakes = 0;
            Emit(SimEventKind.Drained, o.id);
            TutorialAction(Tutorial.Drain);
            Notify("引き上げました！上下に振って湯切りしましょう（3〜4回がベスト）。");
            Save();
            return true;
        }

        /// <summary>網じゃくを振って水気を切る。</summary>
        public bool Shake(string id)
        {
            var o = Find(id);
            if (o == null || o.stage != OrderStage.Drained) return false;
            o.shakes = Math.Min(9, o.shakes + 1);
            Emit(SimEventKind.Shaken, o.id, o.shakes);
            if (o.shakes >= 2) TutorialAction(Tutorial.Shake);
            Changed?.Invoke();
            return true;
        }

        public bool ShakeByHand(string id, float seconds)
        {
            var order = Find(id);
            if (order == null || order.stage != OrderStage.Drained || !(seconds >= 0) || float.IsInfinity(seconds)) return false;
            order.handRhythm = order.lastHandShake >= 0 && CookingFeel.OnRhythm(seconds - order.lastHandShake) ? Math.Min(3, order.handRhythm + 1) : 0;
            order.lastHandShake = seconds;
            return Shake(id);
        }

        /// <summary>スープを注ぐ。種類を変えると量はやり直しになる。</summary>
        public bool PourSoup(string orderId, string soupId, float ml)
        {
            var o = Find(orderId);
            if (o == null || (o.stage != OrderStage.Drained && o.stage != OrderStage.Finishing)) return false;
            if (!float.IsFinite(ml)) return false;
            if (!Customers.AvailableSoups.Any(x => x.Id == soupId)) return Fail("このスープはまだ作れません。");
            if (TutorialBlocks(Tutorial.Soup, "先に網じゃくを2回以上振って湯切りしましょう。")) return false;
            if (o.chosenSoup != soupId) { o.chosenSoup = soupId; o.soupMl = 0; }
            float overflow = Math.Max(0, o.soupMl + Math.Max(0, ml) - 450) - Math.Max(0, o.soupMl - 450);
            if (o.counterSpillMl == 0 && overflow > 0) Notify("あっ、少しこぼれちゃった。台のしみをタップして、ふきふきしよう。");
            o.counterSpillMl = Math.Min(100, o.counterSpillMl + overflow);
            o.soupMl = Math.Max(0, Math.Min(600, o.soupMl + ml));
            o.stage = OrderStage.Finishing;
            Emit(SimEventKind.SoupPoured, o.id, (long)o.soupMl, soupId);
            var soup = Content.Soup(soupId);
            if (State.tutorialStep == Tutorial.Soup && soupId == o.soup && o.soupMl >= soup.IdealMinMl && o.soupMl <= soup.IdealMaxMl) TutorialAction(Tutorial.Soup);
            Changed?.Invoke();
            return true;
        }

        public bool WipeCounter(string orderId)
        {
            var order = Find(orderId);
            if (order == null || order.counterSpillMl <= 0) return false;
            order.counterSpillMl = 0; Notify("ふきふき。台がきれいになりました！"); Save(); return true;
        }

        /// <summary>v1 互換：スープを選ぶ（理想量の中央まで注ぐ）。</summary>
        public void ChooseSoup(string orderId, string soupId)
        {
            var o = Find(orderId);
            if (o == null) return;
            var soup = Content.Soup(soupId);
            float target = (soup.IdealMinMl + soup.IdealMaxMl) / 2;
            float current = o.chosenSoup == soupId ? o.soupMl : 0;
            PourSoup(orderId, soupId, target - current);
        }

        /// <summary>調味料・薬味をかける。ラー油は辛さを1段階上げる。</summary>
        public bool AddCondiment(string orderId, string condimentId)
        {
            var o = Find(orderId);
            if (o == null || (o.stage != OrderStage.Drained && o.stage != OrderStage.Finishing)) return false;
            if (!Customers.AvailableCondiments.Any(x => x.Id == condimentId)) return false;
            if (string.IsNullOrEmpty(o.chosenSoup)) return Fail("先にスープを注ぎましょう。");
            if (condimentId == Content.ChiliOil)
            {
                if (o.chosenSpice >= 5) return Fail("辛さは5が上限です。");
                o.chosenSpice++;
            }
            else
            {
                if (o.condiments.Count(x => x == condimentId) >= 2) return Fail("かけすぎです。");
                o.condiments.Add(condimentId);
            }
            o.stage = OrderStage.Finishing;
            Emit(SimEventKind.Condiment, o.id, o.chosenSpice, condimentId);
            Changed?.Invoke();
            return true;
        }

        /// <summary>v1 互換：辛さを直接指定する。</summary>
        public void ChooseSpice(string orderId, int spice)
        {
            var o = Find(orderId);
            if (o == null || string.IsNullOrEmpty(o.chosenSoup) || (o.stage != OrderStage.Drained && o.stage != OrderStage.Finishing)) return;
            o.chosenSpice = Math.Max(0, Math.Min(5, spice));
            Changed?.Invoke();
        }

        /// <summary>仕上げをやり直す（調味料と辛さを戻す）。</summary>
        public void ResetSeasoning(string orderId)
        {
            var o = Find(orderId);
            if (o == null || o.stage != OrderStage.Finishing) return;
            o.condiments.Clear(); o.chosenSpice = 0;
            Changed?.Invoke();
        }

        /// <summary>仕上げて完成させる（品質を確定）。</summary>
        public bool Finish(string id)
        {
            var o = Find(id);
            if (o == null || (o.stage != OrderStage.Finishing && o.stage != OrderStage.Drained)) return false;
            if (string.IsNullOrEmpty(o.chosenSoup)) return Fail("スープを注いでから完成させましょう。");
            if (!State.tutorialComplete && State.tutorialStep == Tutorial.Spice && o.chosenSpice != o.spice) return Fail("ラー油で辛さを" + o.spice + "にしましょう。");
            if (TutorialBlocks(Tutorial.Spice, "スープを注文どおりの量まで注ぎましょう。")) return false;
            var archetype = Content.Archetype(o.archetype);
            long seasoningCost = Content.Soup(o.chosenSoup).CostFen + o.condiments.Sum(x => Content.Condiment(x).CostFen) + o.chosenSpice * Content.Condiment(Content.ChiliOil).CostFen;
            o.consumedCostFen += seasoningCost;
            State.report.foodCostFen += seasoningCost;
            o.report = QualityEvaluator.Evaluate(o, Catalog, FreshnessScore, archetype.Strictness);
            if (o.handRhythm >= 2)
            {
                o.report.total = Math.Min(100, o.report.total + CookingFeel.HandRhythmBonus);
                o.report.comments.Add("リズムよく湯切りされて、口当たりがいい！");
            }
            CustomerWishes.ApplyBonus(o, Catalog);
            Recipes.ApplyBonus(o);
            Pairings.ApplyBonus(o);
            MorningMarket.ApplyBonus(o, Catalog);
            if (o.omakase) o.report.total = Math.Min(o.report.total, Omakase.QualityCap(o, Catalog));
            o.report.total = Math.Max(0, Math.Min(100, o.report.total + PreparationRules.Bonus(State, o.chosenSoup)));
            o.bowlStyle = o.takeout ? "paper" : State.collectionBowl ? "jade" : State.theme;
            o.quality = o.report.total;
            o.expertBowl = !o.usedVisibleGauge && State.gaugeMode == 2 && !o.handledByStaff && o.quality >= 85;
            o.stage = OrderStage.Ready;
            o.pot = -1;
            Emit(SimEventKind.Finished, o.id, (long)o.quality);
            TutorialAction(Tutorial.Spice);
            Notify("麻辣湯が完成！品質 " + o.quality.ToString("0") + "点。" + (o.takeout ? "持ち帰りの袋に入れて渡しましょう。" : "お客さんの席へ運びましょう。"));
            Save();
            return true;
        }

        /// <summary>v1 互換：盛り付け（＝仕上げて完成）。</summary>
        public bool Plate(string id) => Finish(id);

        /// <summary>完成した麻辣湯を提供する。</summary>
        public bool Serve(string id, bool byStaff = false)
        {
            var o = Find(id);
            if (o == null || o.stage != OrderStage.Ready) return false;
            if (!o.takeout && o.seat < 0) return Fail(o.customerName + "の席が空いていません。テーブルを片付けましょう。");
            if (o.delivery && !o.delivered) return DispatchDelivery(o);
            o.stage = OrderStage.Served;
            o.visualState = o.takeout ? "leave" : "eat";
            o.visualSeconds = 0;
            var archetype = Content.Archetype(o.archetype);
            var r = State.report;
            r.served++; r.qualitySum += o.quality;
            State.stats.served++;
            if (o.selected.Where(MorningMarket.RareIds.Contains).Distinct().Count() >= 2)
                State.stats.rareCombinationBowls++;
            State.regularVisits++;
            if (o.quality >= 95) { r.perfect++; State.stats.perfectBowls++; }
            if (o.quality >= 90) r.excellent++;
            if (o.quality >= 95 && o.spice == 5 && o.chosenSpice == 5) State.stats.spicyPerfect++;
            if (o.spice >= 4 && o.chosenSpice >= 4) r.spicyServed++;
            if (!string.IsNullOrEmpty(o.signatureId)) r.signatureSold++;
            r.servedArchetypes.Add(o.archetype);
            LifetimeStats.Add(State.stats.archetypesServed, o.archetype);
            if (!string.IsNullOrEmpty(o.chosenSoup)) LifetimeStats.Max(State.stats.bestQualityBySoup, o.chosenSoup, (int)o.quality);
            State.stats.bestDayServed = Math.Max(State.stats.bestDayServed, r.served);
            Recipes.RecordServed(this, o);
            Pairings.RecordServed(this, o);
            var houseSoup = State.houseSoups.FirstOrDefault(x => x.id == State.activeHouseSoup && x.soup == o.chosenSoup);
            if (houseSoup != null) houseSoup.served++;
            if (State.report.bestBowl == null || o.quality >= State.report.bestBowl.score)
                State.report.bestBowl = BowlAlbum.Snapshot(State, o);
            if (BowlAlbum.Record(State, o)) Emit(SimEventKind.AlbumAdded, o.id, (long)o.quality, "一杯アルバムに載りました（" + o.quality.ToString("0") + "点）");
            if (o.omakase && o.quality >= 85) State.stats.omakaseGreat++;
            foreach (string item in o.selected) LifetimeStats.Add(State.stats.ingredientsServed, item);
            if (FoodProfile.Evaluate(o.selected, Catalog).Colors.Count >= 5) State.stats.rainbowBowls++;
            if (!string.IsNullOrEmpty(State.featuredIngredient) && o.selected.Contains(State.featuredIngredient))
            {
                o.serviceBonus += Seasons.FeaturedBonus;
                State.stats.seasonalServed++;
            }
            Satisfaction.Settle(this, o, archetype);
            o.review = o.customerName + "「" + string.Join(" ", o.report.comments) + "」";
            o.reaction = CustomerVoice.Reaction(o, o.tipFen, o.discountFen);
            r.reviews.Add(o.review);
            ApplySpecialCustomer(o, archetype);
            UpdateRegular(o, archetype);
            UpdateSignature(o);
            var added = Unlocks.SettleOrder(o.id, o.paidFen, Math.Min(o.paidFen, o.discountFen));
            int xp = 6 + (int)(o.quality / 20) + (o.quality >= 95 ? 2 : 0) + (o.expertBowl ? CookingFeel.ExpertExperienceBonus : 0);
            Progression.AddExperience(this, xp);
            Emit(SimEventKind.Served, o.id, (long)o.quality, o.review);
            foreach (var item in added) Emit(SimEventKind.Unlock, item.id, 0, "新しい食材「" + item.displayName + "」を仕入れられるようになりました！");
            TutorialAction(Tutorial.Serve);
            Missions.Update(this);
            Progression.CheckAchievements(this);
            Chapters.Check(this);
            Notify(added.Count > 0 ? "食材解禁：" + string.Join("・", added.Select(x => x.displayName))
                : (byStaff ? "スタッフが提供しました。" : "提供しました！") + "品質 " + o.quality.ToString("0") + "点" + (o.tipFen > 0 ? "　チップ " + Money(o.tipFen) : "") + (o.discountFen > 0 ? "　値引き " + Money(o.discountFen) : ""));
            Save();
            return true;
        }

        private void ApplySpecialCustomer(CustomerOrder o, CustomerArchetype archetype)
        {
            if (archetype.Id == "critic")
            {
                bool satisfied = o.quality >= 80;
                State.report.reputationDelta += satisfied ? 8 : -6;
                if (satisfied) State.stats.criticsSatisfied++;
                string article = satisfied ? "評論家の記事「麻辣湯の新星。茹で加減が見事だ」" : "評論家の記事「惜しい。まだ伸びしろがある店」";
                State.report.highlights.Add(article);
                Emit(SimEventKind.SpecialEvent, o.id, satisfied ? 1 : 0, article);
            }
            else if (archetype.Id == "influencer")
            {
                bool great = o.quality >= 85;
                State.report.popularityDelta += great ? 12 : -3;
                string post = great ? "人気配信者が「ここの麻辣湯、神！」と投稿。人気が急上昇！" : "人気配信者の投稿は「ふつうだった」…";
                State.report.highlights.Add(post);
                Emit(SimEventKind.SpecialEvent, o.id, great ? 1 : 0, post);
            }
            if (archetype.Id == "judge") Contest.Conclude(this);
            else if (!string.IsNullOrEmpty(o.groupId) && !o.groupId.StartsWith("pair-") && State.orders.Where(x => x.groupId == o.groupId).All(x => x.stage == OrderStage.Served))
            {
                State.stats.groupsServed++;
                State.report.highlights.Add("団体客を全員もてなしました！");
            }
        }

        private void UpdateRegular(CustomerOrder o, CustomerArchetype archetype)
        {
            if (archetype.Id == "critic" || archetype.Id == "influencer" || archetype.Id == "judge") return;
            var regular = State.regulars.FirstOrDefault(x => x.id == o.regularId);
            if (regular == null)
            {
                if (o.report.satisfaction < 80 || State.regulars.Count >= 24 || !string.IsNullOrEmpty(o.groupId)) return;
                regular = new RegularCustomer
                {
                    id = "regular-" + State.day + "-" + State.nextOrderNumber + "-" + State.regulars.Count,
                    name = o.customerName.Replace("さん", ""), archetype = o.archetype, appearanceSeed = o.appearanceSeed,
                    favoriteSoup = o.soup, favoriteSpice = o.spice, affinity = 10
                };
                regular.favorites.AddRange((o.omakase ? o.selected : o.requested).Distinct().Take(4));
                regular.likedCondiments.AddRange(o.requestedCondiments);
                regular.dislikedCondiments.AddRange(o.dislikedCondiments.Where(x => x != Content.ChiliOil));
                State.regulars.Add(regular);
                Emit(SimEventKind.Regular, regular.id, 1, o.customerName + "が常連になりました！");
            }
            regular.lastVisitDay = State.day; regular.concerned = false;
            regular.visits++;
            regular.affinity = Math.Max(0, Math.Min(100, regular.affinity + (o.report.satisfaction - 60) / 4f));
            int story = (int)(regular.affinity / 25);
            if (story > regular.storyIndex && story <= 4)
            {
                regular.storyIndex = story;
                Emit(SimEventKind.Regular, regular.id, 2, RegularStories.Line(regular));
            }
        }

        private void UpdateSignature(CustomerOrder o)
        {
            var dish = State.signatures.FirstOrDefault(x => x.id == o.signatureId);
            if (dish == null) return;
            dish.timesSold++;
            dish.qualitySum += o.quality;
            State.report.reputationDelta += o.quality >= 85 ? 1 : 0;
        }

        /// <summary>食べ終えた客の席を片付ける。</summary>
        public bool ClearTable(int tableUid)
        {
            if (!State.dirtyTables.Remove(tableUid)) return false;
            List<MealRemains> remains = State.mealRemains.Where(x => DiningLayout.TableOfSeat(x.seat) == tableUid).ToList();
            string summary = remains.Count == 0 ? "" : " " + string.Join(" / ", remains.Select(x => MealLeftovers.Describe(x.leftovers, Catalog)));
            State.mealRemains.RemoveAll(x => DiningLayout.TableOfSeat(x.seat) == tableUid);
            Emit(SimEventKind.TableCleared, tableUid.ToString(), tableUid);
            TutorialAction(Tutorial.Clean);
            Notify("テーブルを片付けました。" + summary);
            return true;
        }

        /// <summary>v1 互換：初日の練習をやり直す。</summary>
        public void RetryTutorial(string id)
        {
            if (State.tutorialComplete) return;
            var o = Find(id);
            if (o == null || o.IsClosed) return;
            if (o.stockConsumed)
            {
                foreach (string ingredient in o.selected) StockFor(ingredient).quantity++;
                State.report.foodCostFen -= o.consumedCostFen;
            }
            State.moneyFen -= o.paidFen; State.report.revenueFen -= o.paidFen;
            o.paidFen = 0; o.stockConsumed = false; o.consumedCostFen = 0; o.heatSeconds = 0; o.pot = -1; o.shakes = 0;
            o.chosenSoup = ""; o.soupMl = 0; o.chosenSpice = 0; o.condiments.Clear(); o.boiledOver = false;
            o.stage = OrderStage.Selecting; o.selected.Clear(); o.dropTimes.Clear(); State.tutorialStep = Tutorial.Pick;
            Notify("練習の具材を戻しました。もう一度作りましょう。");
            Save();
        }

        /// <summary>注文を取り消す。会計済みなら返金する。</summary>
        public bool Cancel(string id) => Cancel(id, true);

        private bool Cancel(string id, bool byPlayer)
        {
            if (!State.tutorialComplete) return Fail("初日の注文は取り消せません。");
            var o = Find(id);
            if (o == null || o.IsClosed) return false;
            State.moneyFen -= o.paidFen;
            State.report.revenueFen -= o.paidFen;
            State.report.refundsFen += o.paidFen;
            o.stage = OrderStage.Cancelled; o.pot = -1; o.visualState = "leave"; o.visualSeconds = 0;
            State.report.lost++;
            State.stats.cancelled++;
            Unlocks.SettleOrder(o.id, o.paidFen, o.paidFen);
            Emit(SimEventKind.Cancelled, o.id, o.paidFen);
            if (byPlayer) Notify("注文をお断りしました。" + (o.paidFen > 0 ? "代金は返金しました。" : ""));
            Save();
            return true;
        }

        /// <summary>完成した一杯を名物メニューとして登録する。</summary>
        public bool RegisterSignature(string orderId, string name)
        {
            var o = Find(orderId);
            if (o == null || (o.stage != OrderStage.Ready && o.stage != OrderStage.Served)) return false;
            if (o.quality < 75) return Fail("名物にできるのは品質75点以上の一杯です。");
            if (State.signatures.Count >= 3) return Fail("名物メニューは3つまでです。経営画面で入れ替えましょう。");
            if (State.signatures.Any(d => d.sourceOrderId == o.id)) return Fail("この一杯はすでに名物に登録済みです。");
            string title = string.IsNullOrWhiteSpace(name) ? "店主のおすすめ" + State.nextSignatureNumber : name.Trim();
            var dish = new SignatureDish { id = "sig-" + State.nextSignatureNumber++, sourceOrderId = o.id, name = title, soup = o.chosenSoup, spice = o.chosenSpice };
            dish.ingredients.AddRange(o.selected);
            dish.condiments.AddRange(o.condiments.Distinct());
            State.signatures.Add(dish);
            Emit(SimEventKind.Signature, dish.id, 0, title);
            Notify("名物メニュー「" + title + "」を登録しました！注文されると15%増しで売れます。");
            Save();
            return true;
        }

        /// <summary>名物メニューを削除する。</summary>
        public void RemoveSignature(string id)
        {
            State.signatures.RemoveAll(x => x.id == id);
            Changed?.Invoke(); Save();
        }

        public void FinishTutorial()
        {
            if (State.tutorialComplete || State.tutorialStep != Tutorial.Close) return;
            State.tutorialStep = Tutorial.Purchase;
            EndDay();
        }

        private static bool SameIngredients(CustomerOrder o) => o.requested.OrderBy(x => x).SequenceEqual(o.selected.OrderBy(x => x));
    }
}
