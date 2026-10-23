using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>経営操作（仕入れ・設備・内装・スタッフ・価格・支援）。営業前か閉店後に行う。</summary>
    public sealed partial class RestaurantSimulation
    {
        /// <summary>経営操作ができる時間帯か。</summary>
        public bool CanManage => State.phase == DayPhase.Preparation || State.phase == DayPhase.Report;

        public bool Purchase(string id, int count)
        {
            if (!CanManage) return Fail("仕入れは営業前か閉店後に行えます。");
            if (count <= 0 || !Unlocks.IsUnlocked(id)) return false;
            if (!ChallengeRules.AllowsIngredient(State, Catalog.Get(id))) return Fail("今週のチャレンジでは野菜と麺だけを使えます。");
            if (Catalog.Get(id).isRare) return Fail("希少食材は当日の朝市でのみ仕入れられます。");
            var stock = StockFor(id);
            int free = State.storageCapacity - stock.quantity;
            if (free <= 0) return Fail("保管上限（" + State.storageCapacity + "個）に達しています。");
            count = Math.Min(count, free);
            var item = Catalog.Get(id);
            long cost = checked(PurchaseCost(item) * count);
            if (State.moneyFen < cost) return Fail("資金が足りません。");
            AddStock(stock, count, PurchaseCost(item));
            State.moneyFen -= cost;
            State.report.purchasesFen += cost;
            if (!State.tutorialComplete && State.tutorialStep == Tutorial.Purchase) { State.tutorialStep = TutorialFinished; State.tutorialComplete = true; Emit(SimEventKind.Tutorial, "", TutorialFinished); }
            Emit(SimEventKind.Purchased, id, count);
            Notify(item.displayName + "を" + count + "個仕入れました。");
            Save();
            return true;
        }

        /// <summary>当日の朝市から希少食材を5個だけ仕入れる。資金や容量不足では購入権を消費しない。</summary>
        public bool PurchaseRare(int offerDay)
        {
            if (offerDay != State.day) return Fail("この朝市の販売日は終了しました。");
            MorningMarket.Ensure(this);
            var item = MorningMarket.Current(this);
            if (State.phase != DayPhase.Preparation || item == null) return Fail("朝市は3日目以降の朝に開かれます。");
            if (State.marketPurchased) return Fail("本日の朝市は売り切れです。");
            if (!ChallengeRules.AllowsIngredient(State, item)) return false;
            var stock = StockFor(item.id);
            if (stock.quantity > State.storageCapacity - MorningMarket.BatchSize) return Fail("保管場所を5個分空けてください。");
            long cost = checked(PurchaseCost(item) * MorningMarket.BatchSize);
            if (State.moneyFen < cost) return Fail("朝市の仕入れ資金が足りません。");
            AddStock(stock, MorningMarket.BatchSize, PurchaseCost(item));
            State.moneyFen -= cost;
            State.report.purchasesFen += cost;
            State.marketPurchased = true;
            if (!State.ingredients.rareObtainedIds.Contains(item.id)) State.ingredients.rareObtainedIds.Add(item.id);
            Progression.CheckAchievements(this);
            Emit(SimEventKind.Purchased, item.id, MorningMarket.BatchSize);
            Notify("朝市で「" + item.displayName + "」を5個仕入れました！");
            Save();
            return true;
        }

        /// <summary>在庫が少ない解禁済み食材を、上限まで（資金の範囲で）まとめて仕入れる。</summary>
        public int RestockAll(int target)
        {
            if (!CanManage) { Fail("仕入れは営業前か閉店後に行えます。"); return 0; }
            int bought = 0;
            foreach (var item in Unlocks.Available.Where(x => !x.isRare && ChallengeRules.AllowsIngredient(State, x)).OrderByDescending(x => x.popularity))
            {
                var stock = StockFor(item.id);
                int want = Math.Min(target, State.storageCapacity) - stock.quantity;
                int batches = want / 5;
                for (int i = 0; i < batches; i++)
                {
                    long cost = PurchaseCost(item) * 5;
                    if (State.moneyFen < cost) break;
                    AddStock(stock, 5, PurchaseCost(item));
                    State.moneyFen -= cost; State.report.purchasesFen += cost; bought += 5;
                }
            }
            if (bought > 0)
            {
                if (!State.tutorialComplete && State.tutorialStep == Tutorial.Purchase) { State.tutorialStep = TutorialFinished; State.tutorialComplete = true; }
                Emit(SimEventKind.Purchased, "", bought);
                Notify("まとめて" + bought + "個仕入れました。");
                Save();
            }
            else Notify("仕入れる必要のある食材はありません（または資金不足）。");
            return bought;
        }

        /// <summary>
        /// 在庫を足し、平均取得原価を更新する。
        /// 古い在庫を残したまま新しい在庫を足しても、鮮度は古い方に合わせる（新品で若返らせない）。
        /// </summary>
        private void AddStock(Stock stock, int count, long unitPriceFen)
        {
            if (stock.quantity == 0) { stock.ageDays = 0; stock.unitCostFen = unitPriceFen; }
            else
            {
                long current = stock.unitCostFen > 0 ? stock.unitCostFen : Cost(Catalog.Get(stock.ingredientId));
                stock.unitCostFen = (long)Math.Round((current * (double)stock.quantity + unitPriceFen * (double)count) / (stock.quantity + count));
            }
            stock.quantity += count;
        }

        /// <summary>設備の次のレベルの価格。最大なら -1。</summary>
        public long UpgradePrice(string equipmentId)
        {
            var def = Content.EquipmentById(equipmentId);
            int level = State.EquipmentLevel(equipmentId);
            return level >= def.MaxLevel ? -1 : def.PriceFen[level];
        }

        /// <summary>設備の次のレベルに必要な店舗レベル。</summary>
        public int UpgradeRequiredLevel(string equipmentId)
        {
            var def = Content.EquipmentById(equipmentId);
            int level = State.EquipmentLevel(equipmentId);
            return level >= def.MaxLevel ? 0 : def.RequiredShopLevel[level];
        }

        public bool Upgrade(string equipmentId)
        {
            if (!State.tutorialComplete || !CanManage) return Fail("設備は営業前か閉店後に購入できます。");
            var def = Content.EquipmentById(equipmentId);
            int level = State.EquipmentLevel(equipmentId);
            if (level >= def.MaxLevel) return Fail(def.Name + "は最大レベルです。");
            if (State.shopLevel < def.RequiredShopLevel[level]) return Fail("店舗レベル" + def.RequiredShopLevel[level] + "で購入できます。");
            long cost = def.PriceFen[level];
            if (State.moneyFen < cost) return Fail("設備購入の資金が足りません。");
            State.moneyFen -= cost;
            State.report.investmentFen += cost;
            State.SetEquipmentLevel(equipmentId, level + 1);
            Emit(SimEventKind.Upgraded, equipmentId, level + 1, def.Name);
            Progression.CheckAchievements(this);
            Notify(def.Name + "をレベル" + (level + 1) + "にしました！");
            Save();
            return true;
        }

        /// <summary>v1 互換：鍋（true）または保管棚（false）を強化する。</summary>
        public bool Upgrade(bool pot) => Upgrade(pot ? "pot" : "storage");

        public bool BuyFurniture(string defId, int x, int z, int rotation)
        {
            if (!State.tutorialComplete || !CanManage) return Fail("内装は営業前か閉店後に変更できます。");
            var def = Content.FurnitureById(defId);
            if (State.shopLevel < def.UnlockLevel) return Fail("店舗レベル" + def.UnlockLevel + "で購入できます。");
            if (State.moneyFen < def.PriceFen) return Fail("資金が足りません。");
            if (!DiningLayout.CanPlace(State, defId, x, z, rotation, -1, out string reason)) return Fail(reason);
            State.moneyFen -= def.PriceFen;
            State.report.investmentFen += def.PriceFen;
            State.furniture.Add(new PlacedFurniture { uid = State.nextFurnitureUid++, defId = defId, x = x, z = z, rotation = rotation % 2 });
            State.stats.bestAmbience = Math.Max(State.stats.bestAmbience, DiningLayout.Ambience(State));
            Emit(SimEventKind.FurnitureChanged, defId, 1, def.Name);
            Progression.CheckAchievements(this);
            Notify(def.Name + "を置きました。");
            Save();
            return true;
        }

        public bool MoveFurniture(int uid, int x, int z, int rotation)
        {
            if (!CanManage) return Fail("内装は営業前か閉店後に変更できます。");
            var item = State.furniture.FirstOrDefault(f => f.uid == uid);
            if (item == null) return false;
            if (!DiningLayout.CanPlace(State, item.defId, x, z, rotation, uid, out string reason)) return Fail(reason);
            item.x = x; item.z = z; item.rotation = rotation % 2;
            Emit(SimEventKind.FurnitureChanged, item.defId, 0);
            Changed?.Invoke(); Save();
            return true;
        }

        /// <summary>家具を売却する（購入価格の半額が戻る）。席は最低2席残す。</summary>
        public bool SellFurniture(int uid)
        {
            if (!CanManage) return Fail("内装は営業前か閉店後に変更できます。");
            var item = State.furniture.FirstOrDefault(f => f.uid == uid);
            if (item == null) return false;
            var def = Content.FurnitureById(item.defId);
            if (def.Kind == FurnitureKind.Table && DiningLayout.SeatCount(State) - def.Seats < 2) return Fail("席を最低2席は残してください。");
            State.furniture.Remove(item);
            State.dirtyTables.Remove(uid);
            State.mealRemains.RemoveAll(x => DiningLayout.TableOfSeat(x.seat) == uid);
            State.moneyFen += def.PriceFen / 2;
            // 売却の戻り金は投資の取り崩しとして記録し、日報の収支と現金を一致させる。
            State.report.investmentFen -= def.PriceFen / 2;
            Emit(SimEventKind.FurnitureChanged, item.defId, -1, def.Name);
            Notify(def.Name + "を売却しました（" + Money(def.PriceFen / 2) + "）。");
            Save();
            return true;
        }

        /// <summary>内装テーマを買ってすぐに適用する。購入済みなら切り替えるだけ（無料）。</summary>
        public bool BuyTheme(string themeId)
        {
            if (!State.tutorialComplete || !CanManage) return Fail("内装は営業前か閉店後に変更できます。");
            var def = Content.Theme(themeId);
            if (def.Id != themeId) return false;
            if (!State.ownedThemes.Contains(def.Id))
            {
                if (State.chapter < def.ChapterRequired) return Fail("「店の物語」第" + def.ChapterRequired + "章を完了すると買えます。");
                if (State.moneyFen < def.PriceFen) return Fail("資金が足りません。");
                State.moneyFen -= def.PriceFen;
                State.report.investmentFen += def.PriceFen;
                State.ownedThemes.Add(def.Id);
                Emit(SimEventKind.Purchased, def.Id, def.PriceFen, def.Name);
            }
            if (State.theme == def.Id) return true;
            State.theme = def.Id;
            State.stats.bestAmbience = Math.Max(State.stats.bestAmbience, DiningLayout.Ambience(State));
            Emit(SimEventKind.ThemeChanged, def.Id, 0, def.Name);
            Progression.CheckAchievements(this);
            Notify("内装を「" + def.Name + "」に模様替えしました。");
            Save();
            return true;
        }

        public bool HireStaff(StaffRole role)
        {
            if (!State.tutorialComplete || !CanManage) return Fail("スタッフの採用は営業前か閉店後に行えます。");
            var def = Content.StaffFor(role);
            if (State.shopLevel < def.UnlockLevel) return Fail("店舗レベル" + def.UnlockLevel + "で採用できます。");
            if (State.staff.Count(x => x.role == role) >= 3 || State.staff.Count >= 9) return Fail("同じ役割は3人、合計9人まで採用できます。");
            if (State.moneyFen < def.HireFeeFen) return Fail("採用費が足りません。");
            State.moneyFen -= def.HireFeeFen;
            State.report.investmentFen += def.HireFeeFen;
            int seed = CustomerDirector.StableHash(role + ":" + State.day + ":" + State.nextStaffId);
            State.staff.Add(new HiredStaff { id = "staff-" + State.nextStaffId++, personality = (seed & int.MaxValue) % 3, role = role, name = Content.GivenNames[Math.Abs(seed) % Content.GivenNames.Length], appearanceSeed = seed });
            Emit(SimEventKind.StaffChanged, role.ToString(), 1, def.Title);
            Progression.CheckAchievements(this);
            Notify(def.Title + "を採用しました！営業中に自動で働きます。");
            Save();
            return true;
        }

        public bool FireStaff(StaffRole role) => FireStaff(State.staff.FirstOrDefault(x => x.role == role)?.id);
        public bool FireStaff(string id)
        {
            if (!CanManage) return Fail("営業中は解雇できません。");
            var member = State.staff.FirstOrDefault(x => x.id == id);
            if (member == null) return false;
            State.staff.Remove(member);
            Emit(SimEventKind.StaffChanged, id, -1);
            Notify(member.name + "との契約を終了しました。"); Save(); return true;
        }

        /// <summary>研修費（現在の研修レベル×200元）。</summary>
        public static long TrainingCost(HiredStaff member) => member.trainingLevel * 20000L;

        public bool TrainStaff(StaffRole role) => TrainStaff(State.staff.FirstOrDefault(x => x.role == role)?.id);
        public bool TrainStaff(string id)
        {
            if (!CanManage) return Fail("研修は営業前か閉店後に行えます。");
            var member = State.staff.FirstOrDefault(x => x.id == id);
            if (member == null) return false;
            if (member.trainingLevel >= 3) return Fail("研修は最大レベルです。");
            long cost = TrainingCost(member);
            if (State.moneyFen < cost) return Fail("研修費が足りません。");
            State.moneyFen -= cost;
            State.report.investmentFen += cost;
            member.trainingLevel++;
            Emit(SimEventKind.StaffChanged, id, 0);
            Notify(member.name + "の研修が完了し、レベル" + member.trainingLevel + "になりました。");
            Save();
            return true;
        }

        public bool RequestSupport()
        {
            if (!CanManage) return false;
            long target = Math.Max(5000, MinimumPurchaseFen);
            if (State.moneyFen >= target) return Fail("今は再建支援の対象ではありません。");
            long amount = target - State.moneyFen;
            State.moneyFen = target; State.report.supportFen += amount;
            Notify("商店街から再建支援を受け取りました。累計売上には含まれません。");
            Save();
            return true;
        }

        public void ChangePrice(int changeFen)
        {
            if (!CanManage) return;
            State.pricePer100gFen = Math.Max(900, Math.Min(3000, State.pricePer100gFen + changeFen));
            Changed?.Invoke(); Save();
        }

        /// <summary>店主の見た目を変更する。</summary>
        public void SetChefAppearance(CharacterAppearance appearance)
        {
            State.chef = appearance.Clone();
            Changed?.Invoke(); Save();
        }

        /// <summary>店名を変更する。</summary>
        public void RenameStore(string name)
        {
            string clean = PlayerText.Clean(name);
            if (clean.Length == 0) return;
            State.storeName = clean;
            Changed?.Invoke(); Save();
        }
    }
}
