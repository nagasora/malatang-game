using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;
namespace MalatangStory.Game
{
    [Serializable] public sealed class ChallengeRecord
    {
        public string id;
        public int score, served, lost;
    }
    public static class ChallengeRules
    {
        public const string Rush = "rush", Weekly = "weekly";
        public static int DailySeed(DateTime utc) => (int)(utc.Date - new DateTime(2020, 1, 1)).TotalDays + 1;
        public static int WeeklySeed(DateTime utc) => (DailySeed(utc) + 1) / 7;
        public static string Rule(string mode, int seed) => mode == Weekly ? new[] { "spice5", "vegetable", "cash" }[(seed % 3 + 3) % 3] : "rush";
        public static bool AllowsIngredient(GameState state, IngredientDefinition item)
            => state.challengeRule != "vegetable" || item.category == "vegetable" || item.category == "noodle";
        public static string Description(string rule) => rule == "spice5" ? "全員が辛さ5を注文" : rule == "vegetable" ? "野菜と麺だけで営業" : rule == "cash" ? "すべて現金払い" : "昼と夕方をまとめた3分ラッシュ";
        public static int Score(DailyReport report) => Math.Max(0, report.served * 100 + report.excellent * 30 + report.bestCombo * 10 - report.lost * 50);
        public static GameState Create(IngredientCatalog catalog, string mode, int seed)
        {
            if (mode != Rush && mode != Weekly) throw new ArgumentException(nameof(mode));
            var sim = new RestaurantSimulation(catalog);
            var s = sim.State;
            s.storeId = "challenge-" + mode + "-" + seed; s.storeName = mode == Rush ? "ラッシュ道場" : "週替わり食堂";
            s.challengeMode = mode; s.challengeSeed = seed; s.challengeRule = Rule(mode, seed);
            s.day = Math.Max(1, seed); s.shopLevel = 8; s.tutorialComplete = true; s.tutorialStep = RestaurantSimulation.TutorialFinished;
            s.pots = 3; s.moneyFen = 300000; s.storageCapacity = 120; s.dailyEvent = "none"; s.weather = "sunny";
            foreach (var item in catalog.ingredients.Where(x => !x.isRare).Take(20))
            {
                if (!s.ingredients.unlockedIds.Contains(item.id)) s.ingredients.unlockedIds.Add(item.id);
                var stock = sim.StockFor(item.id); stock.quantity = s.challengeRule == "vegetable" && item.category != "vegetable" && item.category != "noodle" ? 0 : 80;
            }
            if (s.challengeRule == "vegetable")
                foreach (var stock in s.stock)
                    if (catalog.Get(stock.ingredientId).category != "vegetable" && catalog.Get(stock.ingredientId).category != "noodle") stock.quantity = 0;
            s.preparation = new PreparationState { completed = true, quality = 50 };
            return s;
        }
        public static void ApplyOrder(GameState state, CustomerOrder order, IngredientCatalog catalog)
        {
            if (state.challengeRule == "spice5") { order.spice = 5; order.dislikedCondiments.Remove(Content.ChiliOil); }
            if (state.challengeRule == "cash") order.payment = PaymentMethod.Cash;
            if (state.challengeRule == "vegetable")
            {
                order.requested.RemoveAll(x => { string category = catalog.Get(x).category; return category != "vegetable" && category != "noodle"; });
                if (order.requested.Count == 0) order.requested.Add("bok_choy");
                order.selected.RemoveAll(x => !order.requested.Contains(x));
            }
        }
        public static void Record(GameState campaign, GameState challenge)
        {
            if (challenge.phase != DayPhase.Report || string.IsNullOrEmpty(challenge.challengeMode)) return;
            string id = challenge.challengeMode + "-" + challenge.challengeSeed;
            int score = Score(challenge.report);
            var record = campaign.challengeRecords.FirstOrDefault(x => x.id == id);
            if (record != null && record.score >= score) return;
            if (record == null) { record = new ChallengeRecord { id = id }; campaign.challengeRecords.Add(record); }
            record.score = score; record.served = challenge.report.served; record.lost = challenge.report.lost;
            while (campaign.challengeRecords.Count > 32) campaign.challengeRecords.RemoveAt(0);
        }
    }
}
