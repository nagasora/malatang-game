using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>保存復元時の値とコレクションを、実際に扱える範囲へ正規化する。</summary>
    public static class SaveLimits
    {
        public const long MinimumMoney = -1000000000L, MaximumMoney = 1000000000000L;
        public const int MaximumOrders = 200, MaximumItems = 100, MaximumExperience = 100000000;

        public static void Normalize(GameState s)
        {
            s.moneyFen = Clamp(s.moneyFen, MinimumMoney, MaximumMoney);
            s.experience = (int)Clamp(s.experience, 0, MaximumExperience);
            s.day = (int)Clamp(s.day, 1, 1000000);
            s.storeId = PlayerText.Clean(s.storeId, 80);
            if (s.storeId.Length == 0) s.storeId = "store-001";
            s.storeName = PlayerText.Clean(s.storeName);
            if (s.storeName.Length == 0) s.storeName = "麻辣湯の小さなお店";
            s.reputation = Finite(s.reputation, 0, 100); s.popularity = Finite(s.popularity, 0, 100);
            s.daySeconds = Finite(s.daySeconds, 0, RestaurantSimulation.DayDuration);
            s.spawnSeconds = Finite(s.spawnSeconds, 0, RestaurantSimulation.DayDuration);
            s.playerX = Finite(s.playerX, -30, 30); s.playerZ = Finite(s.playerZ, -50, 20);
            s.cameraYaw = Finite(s.cameraYaw, -360, 360);
            s.musicVolume = Finite(s.musicVolume, 0, 1); s.effectsVolume = Finite(s.effectsVolume, 0, 1);
            s.textSize = (int)Clamp(s.textSize, 16, 28);
            s.shopLevel = (int)Clamp(s.shopLevel, 1, Content.MaxShopLevel);
            s.nextOrderNumber = (int)Clamp(s.nextOrderNumber, 1, 100000000);
            s.nextFurnitureUid = (int)Clamp(s.nextFurnitureUid, 1, 10000000);
            s.nextSignatureNumber = (int)Clamp(s.nextSignatureNumber, 1, 100000000);
            var ids = new HashSet<string>(IngredientCatalog.Load().ingredients.Select(x => x.id));
            s.stock = Bound(s.stock, ids.Count);
            var stocks = new HashSet<string>();
            s.stock.RemoveAll(x => x == null || !ids.Contains(x.ingredientId) || !stocks.Add(x.ingredientId));
            foreach (var x in s.stock) { x.quantity = (int)Clamp(x.quantity, 0, s.storageCapacity); x.ageDays = (int)Clamp(x.ageDays, 0, 365); x.unitCostFen = Clamp(x.unitCostFen, 0, 1000000); }
            s.orders = Bound(s.orders, MaximumOrders);
            var orders = new HashSet<string>();
            s.orders.RemoveAll(o => o == null || string.IsNullOrEmpty(o.id) || !orders.Add(o.id));
            foreach (var o in s.orders)
            {
                o.customerName = PlayerText.Clean(o.customerName, 32); o.review = PlayerText.Clean(o.review, 300);
                o.reaction = PlayerText.Clean(o.reaction, 300);
                o.requested = Bound(o.requested, MaximumItems); o.requested.RemoveAll(x => !ids.Contains(x));
                o.selected = Bound(o.selected, MaximumItems); o.dropTimes = Bound(o.dropTimes, MaximumItems);
                for (int i = o.selected.Count - 1; i >= 0; i--)
                    if (!ids.Contains(o.selected[i])) { o.selected.RemoveAt(i); if (i < o.dropTimes.Count) o.dropTimes.RemoveAt(i); }
                if (o.dropTimes.Count > o.selected.Count) o.dropTimes.RemoveRange(o.selected.Count, o.dropTimes.Count - o.selected.Count);
                for (int i = 0; i < o.dropTimes.Count; i++) o.dropTimes[i] = Finite(o.dropTimes[i], -1, 10000);
                o.condiments = Bound(o.condiments, MaximumItems); o.requestedCondiments = Bound(o.requestedCondiments, MaximumItems);
                o.dislikedCondiments = Bound(o.dislikedCondiments, MaximumItems); o.pairings = Bound(o.pairings, MaximumItems);
                o.heatSeconds = Finite(o.heatSeconds, 0, 10000); o.elapsed = Finite(o.elapsed, 0, 10000);
                o.patience = Finite(o.patience, 1, 10000); o.visualSeconds = Finite(o.visualSeconds, 0, 10000);
                o.quality = Finite(o.quality, 0, 100); o.serviceBonus = Finite(o.serviceBonus, -100, 100);
                o.weighedGrams = Finite(o.weighedGrams, 0, 10000); o.soupMl = Finite(o.soupMl, 0, 10000);
                o.targetGrams = Finite(o.targetGrams, 0, 10000);
                o.spice = (int)Clamp(o.spice, 0, 5); o.chosenSpice = (int)Clamp(o.chosenSpice, 0, 5);
                o.paidFen = Clamp(o.paidFen, 0, 100000000); o.billFen = Clamp(o.billFen, 0, 100000000);
                o.tenderedFen = Clamp(o.tenderedFen, 0, 100000000); o.consumedCostFen = Clamp(o.consumedCostFen, 0, 100000000);
                o.tipFen = Clamp(o.tipFen, 0, 100000000); o.discountFen = Clamp(o.discountFen, 0, o.paidFen);
                o.budgetFen = Clamp(o.budgetFen, 0, 100000000);
                if (s.saveVersion > 1 && !Content.Soups.Any(x => x.Id == o.soup)) o.soup = "mala";
                if (s.saveVersion > 1 && o.chosenSoup != "" && !Content.Soups.Any(x => x.Id == o.chosenSoup)) o.chosenSoup = "";
                o.report ??= new QualityReport(); o.report.comments = Texts(o.report.comments, 16, 300);
                o.report.total = Finite(o.report.total, 0, 100); o.report.satisfaction = Finite(o.report.satisfaction, 0, 100);
            }
            s.equipment = Bound(s.equipment, Content.Equipment.Length);
            var equipment = new HashSet<string>();
            s.equipment.RemoveAll(e => e == null || !Content.Equipment.Any(x => x.Id == e.id) || !equipment.Add(e.id));
            foreach (var e in s.equipment) e.level = (int)Clamp(e.level, 0, Content.EquipmentById(e.id).MaxLevel);
            s.furniture = Bound(s.furniture, 104);
            var furniture = new HashSet<int>();
            s.furniture.RemoveAll(f => f == null || f.uid < 1 || f.uid > 10000000 || !furniture.Add(f.uid)
                || !Content.Furniture.Any(d => d.Id == f.defId) || f.rotation < 0 || f.rotation > 1
                || (Content.FurnitureById(f.defId).Kind == FurnitureKind.Wall ? f.x < 0 || f.x >= DiningLayout.WallSlots
                    : DiningLayout.Cells(f).Any(c => c.column < 0 || c.column >= DiningLayout.Columns || c.row < 0 || c.row >= DiningLayout.Rows(s))));
            s.nextFurnitureUid = Math.Max(s.nextFurnitureUid, s.furniture.Count == 0 ? 1 : s.furniture.Max(f => f.uid) + 1);
            s.regulars = Bound(s.regulars, 100); s.regulars.RemoveAll(r => r == null || string.IsNullOrEmpty(r.id));
            foreach (var r in s.regulars) { r.name = PlayerText.Clean(r.name, 32); r.favorites = Bound(r.favorites, MaximumItems); r.favorites.RemoveAll(x => !ids.Contains(x)); r.affinity = Finite(r.affinity, 0, 100); }
            s.signatures = Bound(s.signatures, 3); s.signatures.RemoveAll(d => d == null || string.IsNullOrEmpty(d.id));
            foreach (var d in s.signatures) { d.name = PlayerText.Clean(d.name, 32); d.ingredients = Bound(d.ingredients, MaximumItems); d.ingredients.RemoveAll(x => !ids.Contains(x)); }
            s.staff = Bound(s.staff, 3); s.staff.RemoveAll(m => m == null);
            foreach (var m in s.staff) { m.name = PlayerText.Clean(m.name, 32); m.busySeconds = Finite(m.busySeconds, 0, 10000); }
            s.history = Bound(s.history, DayGrade.HistoryLimit, true);
            s.recentReviews = Texts(s.recentReviews, 20, 300);
            s.report ??= new DailyReport(); s.report.reviews = Texts(s.report.reviews, 50, 300); s.report.highlights = Texts(s.report.highlights, 50, 300);
            s.report.qualitySum = Finite(s.report.qualitySum, 0, 1000000);
            s.ingredients ??= new IngredientProgress();
            s.ingredients.settledRevenueFen = Clamp(s.ingredients.settledRevenueFen, 0, MaximumMoney);
            s.ingredients.settledOrderIds = Texts(s.ingredients.settledOrderIds, 10000, 80);
            s.ingredients.unlockedIds = Bound(s.ingredients.unlockedIds, ids.Count);
            s.ingredients.rareObtainedIds = Bound(s.ingredients.rareObtainedIds, ids.Count);
            s.missions = Bound(s.missions, 3); s.achievements = Texts(s.achievements, 100, 80);
            s.recipes = Bound(s.recipes, 100); s.skills = Bound(s.skills, 20); s.pairings = Bound(s.pairings, 100);
            s.stats ??= new LifetimeStats();
            s.stats.ingredientsServed = Counts(s.stats.ingredientsServed, 100);
            s.stats.archetypesServed = Counts(s.stats.archetypesServed, 30);
            s.stats.bestQualityBySoup = Counts(s.stats.bestQualityBySoup, 10);
            s.recipes = Counts(s.recipes, 100); s.skills = Counts(s.skills, 20); s.pairings = Counts(s.pairings, 100);
            foreach (var r in s.regulars) { r.likedCondiments = Bound(r.likedCondiments, MaximumItems); r.dislikedCondiments = Bound(r.dislikedCondiments, MaximumItems); }
            foreach (var d in s.signatures) d.condiments = Bound(d.condiments, MaximumItems);
            foreach (var entry in s.album ?? new List<AlbumEntry>())
            {
                if (entry == null) continue;
                entry.customerName = PlayerText.Clean(entry.customerName, 32);
                entry.ingredients = Bound(entry.ingredients, MaximumItems); entry.condiments = Bound(entry.condiments, MaximumItems);
            }
            s.dirtyTables = Bound(s.dirtyTables, 104); s.mealRemains = Bound(s.mealRemains, MaximumOrders);
        }

        private static List<NamedCount> Counts(List<NamedCount> entries, int maximum)
        {
            entries = Bound(entries, maximum);
            entries.RemoveAll(x => x == null || string.IsNullOrEmpty(x.id));
            foreach (var entry in entries) { entry.id = PlayerText.Clean(entry.id, 80); entry.count = (int)Clamp(entry.count, 0, 100000000); }
            return entries;
        }

        private static long Clamp(long value, long minimum, long maximum) => Math.Max(minimum, Math.Min(maximum, value));
        private static float Finite(float value, float minimum, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? minimum : Math.Max(minimum, Math.Min(maximum, value));
        private static List<T> Bound<T>(List<T> values, int maximum, bool recent = false)
        {
            values ??= new List<T>();
            if (values.Count > maximum) values.RemoveRange(recent ? 0 : maximum, values.Count - maximum);
            return values;
        }
        private static List<string> Texts(List<string> values, int maximum, int length)
        {
            values = Bound(values, maximum, true);
            for (int i = 0; i < values.Count; i++) values[i] = PlayerText.Clean(values[i], length);
            values.RemoveAll(string.IsNullOrEmpty);
            return values;
        }
    }
}
