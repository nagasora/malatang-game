using System.Collections.Generic;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>購入済みの希少食材を調理には使え、通常コンテンツの候補には混ぜないことを保証する。</summary>
    public sealed class RareContentCandidateTests
    {
        /// <summary>実際の朝市購入を通して希少6種を入手した店舗を作る。</summary>
        private static RestaurantSimulation Purchased()
        {
            RestaurantSimulation sim = new RestaurantSimulation(IngredientCatalog.Load());
            sim.State.storeId = "rare-candidate-fixed";
            sim.State.tutorialComplete = true;
            sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            sim.State.moneyFen = 10000000;
            sim.State.shopLevel = Content.MaxShopLevel;
            for (int day = 3; day <= 8; day++)
            {
                sim.State.day = day;
                MorningMarket.Ensure(sim);
                Assert.IsTrue(sim.PurchaseRare(day));
            }
            Assert.AreEqual(6, sim.State.ingredients.rareObtainedIds.Count);
            return sim;
        }

        /// <summary>通常食材を全解禁し、購入した希少在庫を残して通常在庫だけを補う。</summary>
        private static void StockNormal(RestaurantSimulation sim)
        {
            sim.Unlocks.SettleOrder("all-normal", 100000000);
            foreach (IngredientDefinition item in sim.Unlocks.NormalAvailable) sim.StockFor(item.id).quantity = 20;
        }

        /// <summary>購入した希少在庫を残して通常在庫だけをなくす。</summary>
        private static void ClearNormalStock(RestaurantSimulation sim)
        {
            foreach (IngredientDefinition item in sim.Catalog.ingredients.Where(x => !x.isRare)) sim.StockFor(item.id).quantity = 0;
        }

        /// <summary>購入済みの全食材と通常候補を明確に分け、希少の手動投入を許す。</summary>
        [Test]
        public void PurchasedRareRemainsAvailableForManualCookingButNotNormalCandidates()
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            Assert.AreEqual(75, sim.Unlocks.Available.Count());
            Assert.AreEqual(69, sim.Unlocks.NormalAvailable.Count());
            CustomerOrder order = new CustomerOrder { id = "manual-rare", stage = OrderStage.Selecting };
            sim.State.orders.Add(order);
            foreach (string id in sim.State.ingredients.rareObtainedIds)
            {
                Assert.IsTrue(sim.Unlocks.IsUnlocked(id));
                Assert.IsFalse(sim.Unlocks.IsNormalUnlocked(id));
                Assert.IsTrue(sim.AddIngredient(order.id, id));
            }
            Assert.IsFalse(sim.Unlocks.IsNormalUnlocked("unknown-ingredient"));
            Assert.AreEqual(6, order.selected.Count);
        }

        /// <summary>すべての客タイプは購入後も通常食材だけを指定する。</summary>
        [Test]
        public void EveryCustomerArchetypeRequestsOnlyNormalIngredientsAfterPurchase()
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            foreach (CustomerArchetype archetype in Content.Archetypes)
                for (int index = 0; index < 24; index++)
                {
                    CustomerOrder order = sim.Customers.Create(archetype.Id);
                    Assert.IsNotNull(order, archetype.Id);
                    Assert.IsNotEmpty(order.requested, archetype.Id);
                    Assert.IsTrue(order.requested.All(sim.Unlocks.IsNormalUnlocked), archetype.Id);
                    Assert.IsTrue(order.selected.All(sim.Unlocks.IsNormalUnlocked), archetype.Id);
                }
        }

        /// <summary>希少が好物の常連も、注文できる通常食材の範囲だけで好物を選ぶ。</summary>
        [Test]
        public void RegularFavoritesDoNotReintroducePurchasedRareIngredients()
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            RegularCustomer regular = new RegularCustomer
            {
                id = "rare-regular", name = "常連", archetype = "office", visits = 5, favoriteSoup = "mala", favoriteSpice = 1
            };
            regular.favorites.AddRange(sim.State.ingredients.rareObtainedIds);
            regular.favorites.Add("bok_choy");
            sim.State.regulars.Add(regular);
            bool sawRegular = false;
            for (int index = 0; index < 200; index++)
            {
                CustomerOrder order = sim.Customers.Create();
                Assert.IsNotNull(order);
                Assert.IsTrue(order.requested.All(sim.Unlocks.IsNormalUnlocked));
                if (order.regularId != regular.id) continue;
                sawRegular = true;
                CollectionAssert.Contains(order.requested, "bok_choy");
            }
            Assert.IsTrue(sawRegular);
        }

        /// <summary>レシピの名指し注文も購入後に生成され、通常食材だけを指定する。</summary>
        [Test]
        public void DiscoveredRecipeRequestsRemainNormalAfterRarePurchase()
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            sim.State.recipes.AddRange(Recipes.All.Select(x => new NamedCount { id = x.Id, count = 1 }));
            bool sawRecipe = false;
            for (int index = 0; index < 200; index++)
            {
                CustomerOrder order = sim.Customers.Create();
                Assert.IsNotNull(order);
                Assert.IsTrue(order.requested.All(sim.Unlocks.IsNormalUnlocked));
                sawRecipe |= !string.IsNullOrEmpty(order.recipeRequest);
            }
            Assert.IsTrue(sawRecipe);
        }

        /// <summary>通常在庫がなければ希少を代替せず、空の客注文も作らない。</summary>
        [Test]
        public void RareOnlyStockDoesNotCreateAnEmptyOrRareSubstituteOrder()
        {
            RestaurantSimulation sim = Purchased();
            ClearNormalStock(sim);
            int nextNumber = sim.State.nextOrderNumber;
            Assert.IsNull(sim.Customers.Create());
            Assert.IsNull(sim.Customers.Create("office"));
            Assert.AreEqual(nextNumber, sim.State.nextOrderNumber);
            Assert.IsTrue(sim.Unlocks.Available.Any(x => x.isRare && sim.Available(x.id) > 0));
        }

        /// <summary>購入した希少だけでは開店せず、通常仕入れの案内後に通常食材を仕入れると開店できる。</summary>
        [Test]
        public void BeginDayRejectsRareOnlyStockAndAllowsOpeningAfterNormalPurchase()
        {
            RestaurantSimulation sim = Purchased();
            ClearNormalStock(sim);
            List<SimEvent> events = new List<SimEvent>();
            sim.Event += events.Add;
            int nextOrderNumber = sim.State.nextOrderNumber;
            Assert.IsFalse(sim.BeginDay());
            Assert.AreEqual(DayPhase.Preparation, sim.State.phase);
            Assert.AreEqual(nextOrderNumber, sim.State.nextOrderNumber);
            Assert.IsEmpty(sim.State.orders);
            Assert.IsFalse(events.Any(x => x.Kind == SimEventKind.DayOpened));
            SimEvent failure = events.Single(x => x.Kind == SimEventKind.Failed);
            StringAssert.Contains("通常食材の仕入れ", failure.Text);
            StringAssert.Contains("再建支援", failure.Text);
            Assert.AreEqual(failure.Text, sim.Notice);
            foreach (string id in sim.State.ingredients.rareObtainedIds) Assert.AreEqual(5, sim.StockFor(id).quantity);
            Assert.IsTrue(sim.Purchase("bok_choy", 5));
            Assert.IsTrue(sim.BeginDay());
            Assert.AreEqual(DayPhase.Open, sim.State.phase);
            Assert.IsNotNull(sim.Customers.Create("office"));
        }

        /// <summary>全食材を自動選択へ渡しても希少を混ぜず、通常候補だけの場合と一致する。</summary>
        [Test]
        public void AutoPickDefensivelyExcludesPurchasedRareCandidates()
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            CustomerOrder order = new CustomerOrder { archetype = "office", omakase = true, targetGrams = 10000 };
            List<string> expected = Omakase.AutoPick(order, sim.Unlocks.NormalAvailable, id => sim.Available(id));
            List<string> actual = Omakase.AutoPick(order, sim.Unlocks.Available, id => sim.Available(id));
            Assert.IsNotEmpty(actual);
            CollectionAssert.AreEqual(expected, actual);
            Assert.IsTrue(actual.All(sim.Unlocks.IsNormalUnlocked));
            ClearNormalStock(sim);
            Assert.IsEmpty(Omakase.AutoPick(order, sim.Unlocks.Available, id => sim.Available(id)));
        }

        /// <summary>レジスタッフのおまかせ選択も希少を使わず、通常在庫がなければ空の一杯を会計完了にしない。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CashierAutoSelectionNeverSubstitutesRare(bool rareOnly)
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            if (rareOnly) ClearNormalStock(sim);
            sim.State.phase = DayPhase.Open;
            CustomerOrder order = new CustomerOrder
            {
                id = "staff-rare", archetype = "office", stage = OrderStage.Waiting, style = OrderStyle.Ordered,
                soup = "mala", omakase = true, targetGrams = 10000, budgetFen = 6000, takeout = true
            };
            sim.State.orders.Add(order);
            sim.State.staff.Add(new HiredStaff { role = StaffRole.Cashier, trainingLevel = 1 });
            sim.StaffWork.Tick(0);
            sim.StaffWork.Tick(100);
            Assert.IsTrue(order.selected.All(sim.Unlocks.IsNormalUnlocked));
            Assert.AreEqual(rareOnly ? OrderStage.Cancelled : OrderStage.Weighed, order.stage);
            foreach (string id in sim.State.ingredients.rareObtainedIds) Assert.AreEqual(5, sim.StockFor(id).quantity);
        }

        /// <summary>購入済みの希少きのこも旬の代替に使わず、通常の旬候補がなければ空にする。</summary>
        [TestCase(1)]
        [TestCase(15)]
        [TestCase(29)]
        [TestCase(43)]
        public void FeaturedNeverFallsBackToPurchasedRare(int day)
        {
            RestaurantSimulation sim = Purchased();
            sim.State.day = day;
            StockNormal(sim);
            Assert.IsTrue(sim.Unlocks.IsNormalUnlocked(Seasons.PickFeatured(sim)));
            sim.State.ingredients.unlockedIds.Clear();
            Assert.AreEqual("", Seasons.PickFeatured(sim));
            sim.State.featuredWeek = -1;
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("", sim.State.featuredIngredient);
            sim.State.ingredients.unlockedIds.Add("bok_choy");
            Seasons.EnsureFeatured(sim);
            Assert.AreEqual("", sim.State.featuredIngredient, "同週に通常候補が増えても空の旬は再抽選しない");
        }

        /// <summary>同週に保存された希少の旬は通常候補へ修復する。</summary>
        [Test]
        public void EnsureFeaturedRepairsPurchasedRareFromTheCurrentWeek()
        {
            RestaurantSimulation sim = Purchased();
            sim.State.featuredIngredient = sim.State.ingredients.rareObtainedIds[0];
            sim.State.featuredWeek = Seasons.Week(sim.State.day);
            Seasons.EnsureFeatured(sim);
            Assert.IsTrue(sim.Unlocks.IsNormalUnlocked(sim.State.featuredIngredient));
        }

        /// <summary>旬ミッションは購入済み希少を拒否し、通常の旬では引き続き生成される。</summary>
        [Test]
        public void SeasonalMissionsRejectRareFeaturedEvenBeforeRepair()
        {
            RestaurantSimulation sim = Purchased();
            bool foundNormalSeasonal = false;
            for (int day = 9; day <= 108; day++)
            {
                sim.State.day = day;
                sim.State.featuredIngredient = sim.State.ingredients.rareObtainedIds[day % 6];
                Missions.Generate(sim);
                Assert.IsFalse(sim.State.missions.Any(x => x.kind == "seasonal"));
                sim.State.featuredIngredient = "bok_choy";
                Missions.Generate(sim);
                foundNormalSeasonal |= sim.State.missions.Any(x => x.kind == "seasonal" && x.param == "bok_choy");
            }
            Assert.IsTrue(foundNormalSeasonal);
        }

        /// <summary>名物ミッションと客の名指し注文は、非空・通常解禁・必要数の在庫という同じ条件で候補を選ぶ。</summary>
        [TestCase("rare", false)]
        [TestCase("empty", false)]
        [TestCase("locked", false)]
        [TestCase("shortage", false)]
        [TestCase("normal", true)]
        public void SignatureMissionsAndOrdersShareEligibility(string kind, bool eligible)
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            SignatureDish dish = new SignatureDish
            {
                id = "mission-signature", name = "候補検証", soup = "mala", timesSold = 1000, qualitySum = 100000
            };
            switch (kind)
            {
                case "rare":
                    dish.ingredients.Add("bok_choy");
                    dish.ingredients.Add(sim.State.ingredients.rareObtainedIds[0]);
                    break;
                case "locked":
                    dish.ingredients.Add("scallop");
                    sim.State.ingredients.unlockedIds.Remove("scallop");
                    break;
                case "shortage":
                    dish.ingredients.AddRange(new[] { "bok_choy", "bok_choy" });
                    sim.StockFor("bok_choy").quantity = 1;
                    break;
                case "normal":
                    dish.ingredients.AddRange(new[] { "bok_choy", "bok_choy" });
                    break;
            }
            sim.State.signatures.Add(dish);
            Assert.IsFalse(sim.Customers.CanOrderSignature(null));
            Assert.AreEqual(eligible, sim.Customers.CanOrderSignature(dish));
            bool sawSignatureMission = false;
            bool sawSignatureOrder = false;
            for (int day = 9; day <= 208; day++)
            {
                sim.State.day = day;
                Missions.Generate(sim);
                bool hasSignatureMission = sim.State.missions.Any(x => x.kind == "signature");
                if (!eligible) Assert.IsFalse(hasSignatureMission, kind);
                sawSignatureMission |= hasSignatureMission;
                CustomerOrder order = sim.Customers.Create();
                Assert.IsNotNull(order);
                if (!eligible) Assert.IsEmpty(order.signatureId, kind);
                sawSignatureOrder |= order.signatureId == dish.id;
            }
            Assert.AreEqual(eligible, sawSignatureMission, "注文可能な通常名物がある場合だけ名物ミッションを生成する");
            Assert.AreEqual(eligible, sawSignatureOrder, "客の名指し注文にも同じ候補条件を使う");
        }

        /// <summary>希少入り名物は登録できるが、空の名物とともに客からは注文されない。</summary>
        [Test]
        public void RareSignatureCanBeRegisteredWithoutBecomingACustomerRequest()
        {
            RestaurantSimulation sim = Purchased();
            StockNormal(sim);
            CustomerOrder source = new CustomerOrder
            {
                id = "signature-source", stage = OrderStage.Ready, quality = 100, chosenSoup = "mala", chosenSpice = 1
            };
            source.selected.Add("bok_choy");
            source.selected.Add(sim.State.ingredients.rareObtainedIds[0]);
            sim.State.orders.Add(source);
            Assert.IsTrue(sim.RegisterSignature(source.id, "希少入り名物"));
            SignatureDish rare = sim.State.signatures.Single();
            CollectionAssert.AreEqual(source.selected, rare.ingredients);
            rare.timesSold = 1000;
            rare.qualitySum = 100000;
            sim.State.signatures.Add(new SignatureDish { id = "empty-signature", name = "空の名物", soup = "mala", timesSold = 1000, qualitySum = 100000 });
            bool sawOmakase = false;
            for (int index = 0; index < 400; index++)
            {
                CustomerOrder order = sim.Customers.Create();
                Assert.IsNotNull(order);
                Assert.IsEmpty(order.signatureId);
                Assert.IsTrue(order.omakase || order.requested.Count > 0);
                Assert.IsTrue(order.requested.All(sim.Unlocks.IsNormalUnlocked));
                sawOmakase |= order.omakase;
            }
            Assert.IsTrue(sawOmakase, "通常のおまかせ生成も維持する");
            SignatureDish normal = new SignatureDish { id = "normal-signature", name = "通常名物", soup = "mala", timesSold = 1000, qualitySum = 100000 };
            normal.ingredients.Add("bok_choy");
            sim.State.signatures.Add(normal);
            bool sawNormalSignature = false;
            for (int index = 0; index < 200; index++)
            {
                CustomerOrder order = sim.Customers.Create();
                Assert.IsTrue(order.omakase || order.requested.Count > 0);
                Assert.IsTrue(order.requested.All(sim.Unlocks.IsNormalUnlocked));
                sawNormalSignature |= order.signatureId == normal.id;
            }
            Assert.IsTrue(sawNormalSignature, "通常食材だけの名物は注文される");
            Assert.IsTrue(rare.ingredients.Any(id => sim.Catalog.Get(id).isRare), "登録済みの希少入り名物自体は変更しない");
        }
    }
}
