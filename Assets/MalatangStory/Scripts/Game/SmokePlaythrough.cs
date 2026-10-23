#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MalatangStory.Ingredients;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 開発ビルド用の自動プレイ。主要画面を順に撮影し、数日分の営業を自動で進めて保存を検証する。
    /// 通常のセーブとは別のファイルを使う。
    /// </summary>
    public sealed class SmokePlaythrough : MonoBehaviour
    {
        public GameApplication App;
        public string Output;

        /// <summary>主要工程と追加表示を撮影し、営業中の性能と保存を検証する。</summary>
        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            Directory.CreateDirectory(Output);
            yield return new WaitForSeconds(3);
            yield return FoodGallery();
            yield return Capture("01-title");
            App.NewGame(); yield return new WaitForSeconds(1.5f);
            yield return Capture("02-morning");
            var sim = App.Simulation;
            sim.BeginDay(); App.Open(GameScreen.Store);
            sim.TutorialAction(RestaurantSimulation.Tutorial.Move);
            yield return new WaitForSeconds(2);
            yield return Capture("03-store");
            App.Paused = true;
            string originalName = sim.State.storeName;
            sim.RenameStore("<size=300>麻辣のお店");
            App.Open(GameScreen.Title);
            yield return new WaitForSeconds(1);
            yield return Capture("03c-safe-store-name");
            sim.RenameStore(originalName);
            App.Open(GameScreen.Store); App.Paused = true;
            App.UI.Toast("ご注文の準備ができました。", seconds: 25);
            App.UI.Toast("通路を空けてお待ちください。", seconds: 25);
            App.UI.Toast("この通知は前の通知の後に表示します。", seconds: 25);
            App.UI.Speech("overlay-regression", () => App.World.Player.position + Vector3.up * 1.8f, "おいしい一杯を楽しみにしています。", 25);
            yield return CaptureLayouts("03d-overlay-regression");
            App.Paused = false;
            yield return CaptureMovement();
            var order = sim.Active.Single();
            App.OpenRegister(order.id);
            yield return new WaitForSeconds(6);
            yield return Capture("03b-register-order");
            if (!sim.ConfirmTicket(order.id, order.soup, order.spice, order.takeout)) throw new Exception("Smoke: ticket rejected.");
            App.Open(GameScreen.Register);
            yield return new WaitForSeconds(1.5f);
            yield return Capture("03c-register-pay");
            if (!sim.Pay(order.id, RestaurantSimulation.ExpectedChange(order))) throw new Exception("Smoke: payment failed.");
            App.OpenCooking(order.id);
            foreach (string id in order.requested) { sim.AddIngredient(order.id, id); yield return new WaitForSeconds(.25f); }
            yield return new WaitForSeconds(2.5f);
            Debug.Log("MALATANG_DIAG pieces_in_bowl=" + App.World.Kitchen.PiecesInBowl + "/" + order.selected.Count);
            if (App.World.Kitchen.PiecesInBowl < order.selected.Count) Debug.LogWarning("MALATANG_DIAG 具材が器からこぼれています");
            yield return Capture("04-cooking-pick");
            if (!sim.Unlocks.IsUnlocked("beef_slice")) sim.Unlocks.Progress.unlockedIds.Add("beef_slice");
            sim.StockFor("beef_slice").quantity = Math.Max(8, sim.StockFor("beef_slice").quantity);
            if (!sim.AddIngredient(order.id, "beef_slice")) throw new Exception("Smoke: pairing ingredient rejected.");
            CookingScreen.Category = "mushroom";
            App.Open(GameScreen.Cooking); yield return new WaitForSeconds(1);
            yield return Capture("04b-cooking-pairings");
            yield return CaptureLayouts("04b-cooking-pairings");
            bool omakase = order.omakase;
            try
            {
                order.omakase = true;
                App.Open(GameScreen.Cooking); yield return new WaitForSeconds(1);
                yield return Capture("04c-omakase-pairings");
                yield return CaptureLayouts("04c-omakase-pairings");
            }
            finally { order.omakase = omakase; }
            sim.RemoveIngredient(order.id, "beef_slice");
            CookingScreen.Category = "all";
            App.Open(GameScreen.Cooking);
            sim.Weigh(order.id); sim.StartHeating(order.id, false);
            float slowest = order.selected.Max(x => sim.Catalog.Get(x).cookingSeconds);
            // 生の具材は練習の湯切りガードに拒否されるため、早入れによる煮えすぎで残食を作る。
            sim.DropIngredient(order.id, order.selected.IndexOf("bok_choy"));
            sim.DropIngredient(order.id, order.selected.IndexOf("yuba"));
            for (int guard = 0; guard < 400 && Enumerable.Range(0, order.selected.Count).Any(i => sim.Doneness(order, i) < 1f); guard++)
            {
                for (int i = 0; i < order.selected.Count; i++)
                    if (order.dropTimes[i] < 0 && order.heatSeconds >= slowest - sim.Catalog.Get(order.selected[i]).cookingSeconds) sim.DropIngredient(order.id, i);
                if (guard == 30)
                {
                    yield return Capture("05-cooking-boil");
                    App.Paused = true; bool tutorialComplete = sim.State.tutorialComplete;
                    try
                    {
                        sim.State.tutorialComplete = true;
                        for (int gauge = 1; gauge <= 2; gauge++)
                        {
                            sim.State.gaugeMode = gauge; App.UI.Refresh(); yield return new WaitForSeconds(.2f);
                            yield return Capture("05b-cooking-gauge-" + gauge);
                        }
                    }
                    finally { sim.State.tutorialComplete = tutorialComplete; sim.State.gaugeMode = 0; App.Paused = false; App.UI.Refresh(); }
                }
                sim.Tick(.25f);
                if (guard % 8 == 0) yield return null;
            }
            if (!sim.Drain(order.id)) throw new Exception("Smoke: tutorial bowl could not be drained.");
            sim.Shake(order.id); sim.Shake(order.id); sim.Shake(order.id);
            sim.PourSoup(order.id, order.soup, 340); sim.AddCondiment(order.id, Content.ChiliOil);
            yield return new WaitForSeconds(1.5f);
            yield return Capture("06-cooking-finish");
            if (!sim.Finish(order.id)) throw new Exception("Smoke: tutorial bowl could not be finished.");
            yield return new WaitForSeconds(1);
            yield return Capture("07-cooking-result");
            yield return Capture("07b-bowl-portrait");
            yield return CaptureLayouts("07b-bowl-portrait");
            yield return CaptureResultDetails();
            CaptureSoupPortraits(order);
            App.BackToStore();
            for (int i = 0; i < 5 && order.seat < 0; i++) { sim.Tick(1); yield return null; }
            sim.Serve(order.id);
            float mealSpeed = App.DevelopmentSpeed;
            App.DevelopmentSpeed = 0f;
            try
            {
                for (int i = 0; i < 150 && !FindObjectsByType<MealBowlView>(FindObjectsSortMode.None).Any(x => x.name == "Served Bowl " + order.id); i++)
                    yield return new WaitForSeconds(.1f);
                yield return new WaitForSeconds(2);
                yield return Capture("08-store-eating");
                sim.Tick(4.15f);
                yield return null;
                yield return CaptureMealCloseup("08b-eating-closeup", order);
                sim.Tick(7);
                yield return null;
                if (!order.mealFinished || order.leftovers.ingredients.Count == 0) throw new Exception("Smoke: leftover meal was not recorded.");
                yield return CaptureMealCloseup("08c-leftover-closeup", order);
            }
            finally { App.DevelopmentSpeed = mealSpeed; }
            foreach (int table in sim.State.dirtyTables.ToArray()) sim.ClearTable(table);
            sim.FinishTutorial();
            yield return new WaitForSeconds(1.5f);
            yield return Capture("09-report");
            App.Open(GameScreen.Shop); yield return new WaitForSeconds(1.5f);
            yield return Capture("10-shop");
            sim.Purchase("bok_choy", 5);
            sim.NextDay();
            sim.State.moneyFen += 500000; sim.State.shopLevel = 6;
            App.Open(GameScreen.Management); yield return new WaitForSeconds(1);
            yield return Capture("11-management");
            sim.HireStaff(StaffRole.Server);
            App.Open(GameScreen.Build); yield return new WaitForSeconds(1.5f);
            sim.BuyFurniture("maneki", 5, 0, 0);
            yield return new WaitForSeconds(1);
            yield return Capture("12-build");
            App.Open(GameScreen.Wardrobe); yield return new WaitForSeconds(2);
            yield return Capture("14-wardrobe");
            App.Open(GameScreen.Help); yield return new WaitForSeconds(1);
            yield return Capture("15-help");
            App.Open(GameScreen.Collection); yield return new WaitForSeconds(1);
            yield return Capture("19-collection");
            foreach (string id in new[] { "tomato_egg", "seafood", "green_garden", "golden" }) LifetimeStats.Add(sim.State.recipes, id);
            CollectionScreen.StartTab = 3;
            App.Open(GameScreen.Collection); yield return new WaitForSeconds(1);
            yield return Capture("19b-recipes");
            CollectionScreen.StartTab = 2;
            App.Open(GameScreen.Collection); yield return new WaitForSeconds(1);
            yield return Capture("19c-ingredients");
            EnsureAlbum(sim);
            CollectionScreen.StartTab = 6;
            App.Open(GameScreen.Collection); yield return new WaitForSeconds(1);
            yield return Capture("19d-album");
            yield return CaptureLayouts("19d-album");
            CollectionScreen.StartTab = 0;
            ManagementScreen.tab = 2;
            App.Open(GameScreen.Management); yield return new WaitForSeconds(1);
            yield return Capture("11b-skills");
            ManagementScreen.tab = 0;
            sim.State.chapter = 4; sim.State.moneyFen += 2000000;
            string[] themes = { "lantern", "cafe", "teahouse", "neon" };
            for (int i = 0; i < themes.Length; i++)
            {
                if (!sim.BuyTheme(themes[i])) throw new Exception("Smoke: could not buy theme " + themes[i]);
                App.Open(GameScreen.Store); yield return new WaitForSeconds(1.5f);
                yield return Capture("2" + i + "-theme-" + themes[i]);
            }
            sim.BuyTheme("lantern");
            App.Open(GameScreen.Morning); yield return new WaitForSeconds(1);
            for (int day = 2; day <= 4; day++)
            {
                if (day == 3) sim.BuyTheme("neon");
                if (day == 4) sim.BuyTheme("cafe");
                if (day == 3) yield return CaptureMorningMarket(sim);
                sim.RequestSupport();
                sim.RestockAll(15);
                if (!sim.BeginDay()) throw new Exception("Smoke: could not start day.");
                App.Open(GameScreen.Store);
                int guard = 0;
                bool realtimeShown = false, eveningShown = false, firstPersonShown = false;
                while (sim.State.phase != DayPhase.Report && guard++ < 4000)
                {
                    bool realtime = (day == 2 && sim.State.daySeconds > 90 && !realtimeShown) || (day == 3 && sim.State.daySeconds > 600 && !eveningShown)
                        || (day == 4 && sim.State.daySeconds > 300 && !firstPersonShown);
                    if (realtime)
                    {
                        // 実時間に近い速度で営業させ、客の移動・着席・食事の見た目を確認する。
                        bool fp = day == 4;
                        if (fp) App.World.SetFirstPerson(true);
                        sim.State.highQuality = true; App.ApplySettings();
                        App.Paused = false; App.DevelopmentSpeed = 2;
                        if (day == 2)
                        {
                            yield return MeasurePerformance(sim);
                            sim.State.highQuality = false; App.ApplySettings();
                            yield return MeasurePerformance(sim);
                            sim.State.highQuality = true; App.ApplySettings();
                        }
                        float until = Time.realtimeSinceStartup + (day == 2 ? 12 : 22);
                        while (Time.realtimeSinceStartup < until && sim.State.phase != DayPhase.Report)
                        {
                            foreach (int table in sim.State.dirtyTables.ToArray()) sim.ClearTable(table);
                            foreach (var o in sim.Active.ToArray()) AutoCook(sim, o);
                            yield return new WaitForSeconds(.4f);
                        }
                        App.Paused = true; App.DevelopmentSpeed = 1;
                        yield return Capture(day == 2 ? "13-business" : day == 3 ? "16-evening" : "17-first-person");
                        if (fp)
                        {
                            App.Paused = false;
                            var door = StoreLayout.Door(DiningLayout.Rows(sim.State));
                            App.World.WalkTo(door + Vector3.forward * 1.2f);
                            yield return new WaitForSeconds(4);
                            App.World.FaceTowards(door + Vector3.back * 8);
                            yield return new WaitForSeconds(.6f);
                            App.Paused = true;
                            yield return Capture("18-first-person-door");
                            App.World.SetFirstPerson(false);
                        }
                        if (day == 2) realtimeShown = true; else if (day == 3) eveningShown = true; else firstPersonShown = true;
                        continue;
                    }
                    sim.Tick(1);
                    foreach (int table in sim.State.dirtyTables.ToArray()) sim.ClearTable(table);
                    foreach (var o in sim.Active.ToArray()) AutoCook(sim, o);
                    if (guard % 20 == 0) yield return null;
                }
                if (guard >= 4000) throw new Exception("Smoke: day did not finish.");
                if (day == 4)
                {
                    App.Open(GameScreen.Report); yield return new WaitForSeconds(3);
                    yield return Capture("09b-report-day4");
                }
                sim.NextDay();
            }
            int realDay = sim.State.day; string realWeather = sim.State.weather;
            // 撮影のために日付を一時的に変えるので、その間の自動保存で不整合な保存が残らないよう保存を止める。
            App.SuspendSaving = true;
            try
            {
            foreach (var (seasonDay, weather, name) in new[] { (20, "hot", "summer"), (34, "sunny", "autumn"), (48, "cold", "winter") })
            {
                sim.State.day = seasonDay; sim.State.weather = weather;
                App.World.ApplyWeather();
                App.Open(GameScreen.Store); yield return new WaitForSeconds(2.5f);
                yield return Capture("24-season-" + name);
            }
            App.Paused = false;
            App.World.SetFirstPerson(true);
            var street = StoreLayout.Door(DiningLayout.Rows(sim.State));
            App.World.WalkTo(street + Vector3.forward * 1.2f);
            yield return new WaitForSeconds(4);
            App.World.FaceTowards(street + Vector3.back * 8);
            yield return new WaitForSeconds(1.5f);
            yield return Capture("25-winter-street");
            App.World.SetFirstPerson(false);
            }
            finally
            {
                sim.State.day = realDay; sim.State.weather = realWeather;
                App.SuspendSaving = false;
                App.World.ApplyWeather();
            }
            App.Save();
            var saved = App.Saves.Load();
            if (saved == null || saved.day != 5) throw new Exception("Smoke: save failed.");
            yield return CaptureNewFeatures(sim);
            File.WriteAllText(Path.Combine(Output, "smoke-result.json"), "{\"result\":\"passed\",\"reachedDay\":5,\"shopLevel\":" + sim.State.shopLevel + ",\"served\":" + sim.State.stats.served + ",\"unlockedIngredients\":" + sim.Unlocks.Available.Count() + "}");
            Debug.Log("MALATANG_PLAYER_SMOKE_SUCCESS");
            Application.Quit(0);
        }

        private IEnumerator CaptureNewFeatures(RestaurantSimulation campaign)
        {
            campaign.State.shopLevel = Math.Max(8, campaign.State.shopLevel); campaign.State.moneyFen += 2000000;
            if (!campaign.BeginPreparation("mala", 3, 2, 1)) throw new Exception("Smoke: preparation start failed");
            campaign.TickPreparation(.5f); campaign.ChopPreparation();
            for (int i = 0; i < 8; i++) { campaign.TickPreparation(1); campaign.ChopPreparation(); }
            App.Open(GameScreen.Preparation); yield return Capture("30-preparation");
            if (!campaign.FinishPreparation() || !campaign.RecordHouseSoup("朝の香りスープ")) throw new Exception("Smoke: house soup failed");
            App.Open(GameScreen.Preparation); yield return Capture("30b-house-soup"); yield return CaptureLayouts("30b-house-soup");
            if (!campaign.HireStaff(StaffRole.Server) || !campaign.HireStaff(StaffRole.Server)) throw new Exception("Smoke: multiple staff failed");
            var first = campaign.State.staff.First(x => x.role == StaffRole.Server);
            var second = campaign.State.staff.Last(x => x.role == StaffRole.Server);
            campaign.TrainStaff(first.id); campaign.PraiseStaff(first.id); campaign.RestStaff(second.id, true);
            ManagementScreen.tab = 3; App.Open(GameScreen.Management);
            yield return Capture("31-staff"); yield return CaptureLayouts("31-staff"); ManagementScreen.tab = 0;
            App.Save();
            var before = App.Saves.Load();
            App.Open(GameScreen.Challenges); yield return Capture("32-challenges"); yield return CaptureLayouts("32-challenges");
            App.StartChallenge(ChallengeRules.Rush, new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc));
            if (!App.InChallenge || !App.Simulation.BeginDay()) throw new Exception("Smoke: challenge start failed");
            App.Open(GameScreen.Store); yield return Capture("32b-rush");
            var challenge = App.Simulation;
            int guard = 0;
            while (challenge.State.phase != DayPhase.Report && guard++ < 2000)
            {
                challenge.Tick(1);
                foreach (int table in challenge.State.dirtyTables.ToArray()) challenge.ClearTable(table);
                foreach (var order in challenge.Active.ToArray()) AutoCook(challenge, order);
                if (guard % 30 == 0) yield return null;
            }
            if (challenge.State.phase != DayPhase.Report) throw new Exception("Smoke: challenge did not finish");
            App.Open(GameScreen.Report); yield return new WaitForSeconds(1.5f); yield return Capture("32c-rush-result");
            App.LeaveChallenge();
            var after = App.Saves.Load();
            if (after == null || after.day != before.day || after.moneyFen != before.moneyFen || after.stats.served != before.stats.served || after.challengeRecords.Count != before.challengeRecords.Count + 1)
                throw new Exception("Smoke: challenge changed campaign progress or failed to record");
            App.Open(GameScreen.Challenges); yield return Capture("32d-personal-best");
            yield return CaptureBusinessAndPhoto();
        }

        private IEnumerator CaptureBusinessAndPhoto()
        {
            App.Continue();
            App.SuspendSaving = true;
            try
            {
                var sim = App.Simulation; var state = sim.State;
                state.day = 10; state.dailyEvent = "festival"; state.phase = DayPhase.Preparation;
                state.moneyFen = 4000000; state.shopLevel = 8;
                App.Open(GameScreen.Neighbourhood); yield return Capture("33-neighbourhood");
                if (!sim.SetFestivalStall(true) || !sim.BeginDay()) throw new Exception("Smoke: festival stall failed");
                App.Open(GameScreen.Stall); yield return Capture("33b-festival-stall"); yield return CaptureLayouts("33b-festival-stall");
                for (int i = 0; i < 65; i++)
                {
                    sim.Tick(1); foreach (var order in sim.Active.ToArray()) AutoCook(sim, order);
                    if (i % 10 == 0) yield return null;
                }
                yield return Capture("33c-stall-service");
                state.phase = DayPhase.Preparation; state.orders.Clear(); state.festivalStall = false;
                if (!sim.HireStaff(StaffRole.Courier) || !sim.SetDeliveryEnabled(true)) throw new Exception("Smoke: courier failed");
                var delivery = new CustomerOrder { id = "smoke-delivery", customerName = "出前・試食さん", type = "会社員", delivery = true, takeout = true, stage = OrderStage.Ready, chosenSoup = "mala", soupMl = 340, quality = 94, paidFen = 3000, bowlStyle = "paper",
                    selected = new System.Collections.Generic.List<string> { "enoki", "bok_choy", "beef_slice" } };
                delivery.report.total = 94; state.orders.Add(delivery); state.phase = DayPhase.Open;
                if (!sim.Serve(delivery.id)) throw new Exception("Smoke: dispatch failed");
                sim.Tick(8); App.Open(GameScreen.Store); yield return Capture("34-delivery");
                var scooter = GameObject.Find("Delivery Scooter");
                if (scooter == null) throw new Exception("Smoke: missing visible delivery scooter");
                yield return CaptureObject("34b-delivery-scooter", scooter.transform, new Vector3(2, 1.4f, -3));
                state.phase = DayPhase.Preparation; state.orders.Clear();
                if (!sim.ScheduleRivalCookoff() || !sim.BeginDay()) throw new Exception("Smoke: rival cookoff failed");
                for (int guard = 0; state.phase != DayPhase.Report && guard < 1800; guard++)
                {
                    sim.Tick(1); foreach (int table in state.dirtyTables.ToArray()) sim.ClearTable(table);
                    foreach (var order in sim.Active.ToArray()) AutoCook(sim, order);
                    if (guard % 30 == 0) yield return null;
                }
                if (state.phase != DayPhase.Report || state.rivalResolvedDay != state.day) throw new Exception("Smoke: rival result failed");
                App.Open(GameScreen.Report); yield return new WaitForSeconds(1.5f); yield return Capture("35-rival-result");
                state.ingredients.unlockedIds = App.Catalog.ingredients.Where(x => !x.isRare).Select(x => x.id).ToList();
                state.ingredients.rareObtainedIds = App.Catalog.ingredients.Where(x => x.isRare).Select(x => x.id).ToList();
                foreach (string reward in new[] { "apron", "bowl", "pennant", "title" }) sim.ChooseCollectionReward(reward, true);
                App.World.PreviewChef(state.chef); App.World.SetCollectorPennant(true);
                CollectionScreen.StartTab = 8; App.Open(GameScreen.Collection); yield return Capture("36-collection-rewards"); yield return CaptureLayouts("36-collection-rewards");
                CollectionScreen.StartTab = 0;
                var photoOrder = new CustomerOrder { id = "smoke-photo", customerName = "撮影用の一杯", type = "会社員", stage = OrderStage.Ready, chosenSoup = "mala", soupMl = 340, quality = 96, bowlStyle = "jade",
                    selected = new System.Collections.Generic.List<string> { "enoki", "bok_choy", "beef_slice", "rice_noodle" } };
                photoOrder.report.total = 96; state.orders.Add(photoOrder); App.SelectedOrderId = photoOrder.id;
                App.Open(GameScreen.Cooking); yield return Capture("36b-reward-bowl");
                App.BeginPhoto(true); yield return new WaitForSeconds(.6f); yield return Capture("37-photo-mode");
                PhotoExportService.OutputDirectoryOverride = Path.Combine(Output, "Photos");
                FindObjectsByType<Button>(FindObjectsSortMode.None).First(x => x.gameObject.activeInHierarchy && x.GetComponentInChildren<Text>()?.text == "スタンプ切替").onClick.Invoke();
                FindObjectsByType<Button>(FindObjectsSortMode.None).First(x => x.gameObject.activeInHierarchy && x.GetComponentInChildren<Text>()?.text == "フィルター：なし").onClick.Invoke();
                FindObjectsByType<Button>(FindObjectsSortMode.None).First(x => x.gameObject.activeInHierarchy && x.GetComponentInChildren<Text>()?.text == "PNGに保存").onClick.Invoke();
                for (int guard = 0; (!Directory.Exists(PhotoExportService.OutputDirectoryOverride) || Directory.GetFiles(PhotoExportService.OutputDirectoryOverride, "*.png").Length == 0) && guard < 120; guard++) yield return null;
                if (!Directory.Exists(PhotoExportService.OutputDirectoryOverride) || Directory.GetFiles(PhotoExportService.OutputDirectoryOverride, "*.png").Length == 0) throw new Exception("Smoke: photo export failed");
                yield return Capture("37b-photo-saved");
                File.WriteAllText(Path.Combine(Output, "business-proof.json"), "{\"festival\":true,\"delivery\":true,\"rivalResolved\":true,\"photoPng\":true,\"iosNativeVerified\":false}");
            }
            finally
            {
                PhotoExportService.OutputDirectoryOverride = null; App.SuspendSaving = false; App.Continue();
            }
        }
        private IEnumerator CaptureObject(string name, Transform target, Vector3 offset)
        {
            var cameraObject = new GameObject("Verification Camera"); var camera = cameraObject.AddComponent<Camera>();
            var canvas = App.UI.GetComponentInChildren<Canvas>(); bool visible = canvas.enabled;
            try
            {
                camera.depth = 100; camera.fieldOfView = 34; camera.nearClipPlane = .03f;
                camera.transform.position = target.position + offset; camera.transform.LookAt(target.position + Vector3.up * .7f);
                canvas.enabled = false; yield return null; yield return Capture(name);
            }
            finally { canvas.enabled = visible; Destroy(cameraObject); }
        }

        /// <summary>実際の客席に専用カメラを向け、食事中または片付け前の器を撮影します。</summary>
        private IEnumerator CaptureMealCloseup(string name, CustomerOrder order)
        {
            string bowlName = name.StartsWith("08c", StringComparison.Ordinal) ? "Leftover Bowl " + order.id : "Served Bowl " + order.id;
            MealBowlView bowl = FindObjectsByType<MealBowlView>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == bowlName && x.gameObject.activeInHierarchy);
            if (bowl == null) throw new Exception("Smoke: missing " + bowlName);
            GameObject cameraObject = new GameObject("Meal Closeup Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            Canvas canvas = App.UI.GetComponentInChildren<Canvas>();
            bool wasVisible = canvas.enabled;
            try
            {
                camera.depth = 100; camera.fieldOfView = 32f; camera.nearClipPlane = .03f;
                FrameMealCloseup(camera, bowl);
                canvas.enabled = false;
                yield return null;
                yield return Capture(name);
            }
            finally
            {
                canvas.enabled = wasVisible;
                camera.enabled = false;
                Destroy(cameraObject);
            }
        }

        /// <summary>器と持ち上げた一口の描画範囲を、余白付きで接写カメラへ収めます。</summary>
        public static void FrameMealCloseup(Camera camera, MealBowlView bowl)
        {
            Bounds bounds = new Bounds(bowl.transform.position, Vector3.zero);
            foreach (MeshRenderer renderer in bowl.GetComponentsInChildren<MeshRenderer>())
                if (renderer.enabled && renderer.gameObject.activeInHierarchy) bounds.Encapsulate(renderer.bounds);
            float halfAngle = Mathf.Atan(Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * Mathf.Min(1f, camera.aspect));
            float distance = Mathf.Max(.1f, bounds.extents.magnitude) / Mathf.Sin(halfAngle) * 1.1f;
            camera.transform.position = bounds.center + new Vector3(.42f, .58f, -.52f).normalized * distance;
            camera.transform.LookAt(bounds.center);
        }

        /// <summary>完成カードの実ボタンで品質内訳へ移動し、全七行が操作ボタンの上に収まることを確認します。</summary>
        private IEnumerator CaptureResultDetails()
        {
            int width = Screen.width, height = Screen.height;
            try
            {
                foreach (var (w, h, suffix) in new[] { (width, height, ""), (1024, 768, "-ipad"), (590, 1280, "-phone-portrait"), (1280, 590, "-phone-landscape") })
                {
                    Screen.SetResolution(w, h, FullScreenMode.Windowed);
                    yield return new WaitForSeconds(1.5f);
                    ScrollRect scroll = FindObjectsByType<ScrollRect>(FindObjectsSortMode.None).Single(item => item.name == "Result Details");
                    Button toggle = FindObjectsByType<Button>(FindObjectsSortMode.None).Single(item => item.name == "Quality Details Toggle");
                    Canvas.ForceUpdateCanvases();
                    RectTransform overview = scroll.content.Find("Bowl Overview") as RectTransform;
                    RectTransform photo = overview.Find("Bowl Portrait") as RectTransform;
                    if (photo.rect.width < overview.rect.width * .95f)
                        throw new Exception("Smoke: completed bowl portrait does not fill card width.");
                    scroll.verticalNormalizedPosition = 1f;
                    toggle.onClick.Invoke();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    RectTransform metrics = scroll.content.Find("Quality Details") as RectTransform;
                    Vector3[] corners = new Vector3[4];
                    metrics.GetWorldCorners(corners);
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 local = scroll.viewport.InverseTransformPoint(corner);
                        if (local.y < scroll.viewport.rect.yMin - 1f || local.y > scroll.viewport.rect.yMax + 1f)
                            throw new Exception("Smoke: quality details clipped in " + w + "x" + h);
                    }
                    yield return Capture("07b-quality-details" + suffix);
                    scroll.verticalNormalizedPosition = 1f;
                }
            }
            finally { Screen.SetResolution(width, height, FullScreenMode.Windowed); }
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>3日目の朝市で限定品を購入し、朝・仕入れ・入手後の希少図鑑を撮影する。</summary>
        private IEnumerator CaptureMorningMarket(RestaurantSimulation sim)
        {
            IngredientDefinition item = MorningMarket.Current(sim);
            if (item == null || !item.isRare) throw new Exception("Smoke: morning market offer missing.");
            App.Open(GameScreen.Morning); yield return new WaitForSecondsRealtime(7f);
            yield return Capture("26-market-morning");
            yield return CaptureLayouts("26-market-morning");
            ShopScreen.StartAtMarket = true;
            App.Open(GameScreen.Shop); yield return new WaitForSeconds(1);
            yield return Capture("27-market-shop");
            yield return CaptureLayouts("27-market-shop");
            int before = sim.StockFor(item.id).quantity;
            if (!sim.PurchaseRare(sim.State.day)) throw new Exception("Smoke: morning market purchase rejected.");
            if (sim.StockFor(item.id).quantity != before + MorningMarket.BatchSize || sim.PurchaseRare(sim.State.day))
                throw new Exception("Smoke: morning market quantity limit failed.");
            CollectionScreen.StartTab = 7;
            App.Open(GameScreen.Collection); yield return new WaitForSeconds(3);
            yield return Capture("28-rare-collection");
            yield return CaptureLayouts("28-rare-collection");
            yield return CaptureRareIcons();
            CollectionScreen.StartTab = 0;
            App.Open(GameScreen.Morning); yield return new WaitForSeconds(.5f);
        }

        /// <summary>希少六種の実際のUI用アイコンを保存し、小さい表示でも外観を判別できるか確認可能にします。</summary>
        private IEnumerator CaptureRareIcons()
        {
            foreach (IngredientDefinition item in App.Catalog.ingredients.Where(ingredient => ingredient.isRare))
            {
                Sprite icon = App.Icons.Get(item.id);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (icon == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    icon = App.Icons.Get(item.id);
                }
                if (icon == null) throw new Exception("Smoke: rare ingredient icon missing: " + item.id);
                SavePortrait(icon.texture, "29-rare-icon-" + item.id);
            }
        }

        /// <summary>完成した一杯と全スープの寄り写真を、実際の完成カードと同じ撮影経路で保存する。</summary>
        private void CaptureSoupPortraits(CustomerOrder order)
        {
            SavePortrait(BowlPortrait.Get(order), "07c-soup-closeup");
            foreach (SoupDefinition soup in Content.Soups)
            {
                var entry = new AlbumEntry
                {
                    soup = soup.Id, spice = soup.Id == "mala" ? 3 : 0,
                    ingredients = new System.Collections.Generic.List<string> { "beef_slice", "enoki", "bok_choy", "rice_noodle" },
                    condiments = new System.Collections.Generic.List<string> { "scallion", "garlic", "coriander", "sichuan_pepper", "peanut", "sesame_paste", "vinegar" },
                };
                if (entry.spice > 0) entry.condiments.Add(Content.ChiliOil);
                SavePortrait(BowlPortrait.Get(entry), "07d-soup-" + soup.Id);
                if (soup.Id == "mala")
                {
                    foreach (int spice in new[] { 0, 5 })
                    {
                        entry.spice = spice;
                        SavePortrait(BowlPortrait.Get(entry), "07e-soup-mala-spice-" + spice);
                    }
                }
            }
        }

        /// <summary>CPU側の画素を保持しない共有写真をGPUから読み戻して検証用PNGへ保存する。</summary>
        private void SavePortrait(Texture2D portrait, string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(portrait.width, portrait.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                // 通常写真を読み取り可能にすると、検証のためだけに製品版のCPUメモリ予算が増えてしまう。
                Graphics.Blit(portrait, target);
                RenderTexture.active = target;
                readable = new Texture2D(portrait.width, portrait.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, portrait.width, portrait.height), 0, 0);
                readable.Apply(false, false);
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (readable != null) Destroy(readable);
            }
        }

        /// <summary>実提供の記録がない場合だけ、撮影用の高品質な一杯を記録する。</summary>
        private static void EnsureAlbum(RestaurantSimulation sim)
        {
            if (sim.State.album.Count > 0) return;
            foreach (var (soup, ingredients, quality) in new[]
            {
                ("mala", new[] { "beef_slice", "enoki", "bok_choy", "rice_noodle" }, 96),
                ("tomato", new[] { "cherry_tomato", "shrimp", "egg", "spinach" }, 93),
                ("mala", new[] { "pork_belly", "potato", "napa_cabbage", "meat_ball" }, 90),
            })
            {
                sim.State.album.Add(new AlbumEntry
                {
                    day = sim.State.day, customerName = "撮影用の一杯", soup = soup, spice = soup == "mala" ? 2 : 0,
                    quality = quality, score = quality, seq = ++sim.State.albumSeq, ingredients = ingredients.ToList(),
                    pairings = Pairings.In(ingredients).Select(p => p.Id).ToList(),
                });
            }
        }

        /// <summary>営業中の実フレーム間隔と、有効な描画メッシュの規模を記録する。</summary>
        private IEnumerator MeasurePerformance(RestaurantSimulation sim)
        {
            yield return null;
            double start = Time.realtimeSinceStartupAsDouble, previous = start, nextCook = start;
            double sum = 0, maximum = 0;
            int frames = 0, timingSamples = 0;
            double cpuFrame = 0, gpuFrame = 0;
            var timing = new FrameTiming[1];
            var process = System.Diagnostics.Process.GetCurrentProcess();
            double cpuStart = process.TotalProcessorTime.TotalMilliseconds;
            long probeBefore = GC.GetAllocatedBytesForCurrentThread();
            var allocationProbe = new byte[128]; GC.KeepAlive(allocationProbe);
            bool allocationCounterSupported = GC.GetAllocatedBytesForCurrentThread() > probeBefore;
            long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
            var intervals = new double[10000]; int intervalCount = 0, hitches = 0;
            while (previous - start < 10)
            {
                if (previous >= nextCook)
                {
                    foreach (int table in sim.State.dirtyTables.ToArray()) sim.ClearTable(table);
                    foreach (CustomerOrder order in sim.Active.ToArray()) AutoCook(sim, order);
                    nextCook = previous + .4;
                }
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                {
                    cpuFrame += timing[0].cpuFrameTime; gpuFrame += timing[0].gpuFrameTime; timingSamples++;
                }
                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = (now - previous) * 1000;
                sum += elapsed; maximum = Math.Max(maximum, elapsed); frames++;
                if (intervalCount < intervals.Length) intervals[intervalCount++] = elapsed;
                if (elapsed > 50) hitches++;
                previous = now;
            }
            int renderers = 0;
            long triangles = 0;
            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                renderers++;
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    if (mesh.GetTopology(submesh) == MeshTopology.Triangles) triangles += mesh.GetIndexCount(submesh) / 3;
            }
            Array.Sort(intervals, 0, intervalCount);
            var result = new PerformanceResult
            {
                highQuality = sim.State.highQuality, durationSeconds = previous - start, frames = frames,
                averageFrameMs = frames > 0 ? sum / frames : 0, maximumFrameMs = maximum,
                meshRenderers = renderers, totalTriangles = triangles,
                timingSamples = timingSamples, cpuFrameMs = timingSamples > 0 ? cpuFrame / timingSamples : 0,
                gpuFrameMs = timingSamples > 0 ? gpuFrame / timingSamples : 0,
                processCpuMsPerFrame = frames > 0 ? (process.TotalProcessorTime.TotalMilliseconds - cpuStart) / frames : 0,
                allocationCounterSupported = allocationCounterSupported,
                percentile95Ms = intervalCount > 0 ? intervals[(int)((intervalCount - 1) * .95)] : 0,
                percentile99Ms = intervalCount > 0 ? intervals[(int)((intervalCount - 1) * .99)] : 0, hitchesOver50Ms = hitches,
                mainThreadBytesPerFrame = allocationCounterSupported && frames > 0 ? (GC.GetAllocatedBytesForCurrentThread() - allocatedStart) / frames : -1,
            };
            File.WriteAllText(Path.Combine(Output, sim.State.highQuality ? "perf.json" : "perf-low.json"), JsonUtility.ToJson(result, true));
            Debug.Log("MALATANG_PERF " + JsonUtility.ToJson(result));
        }

        /// <summary>壁時計のフレーム間隔と、計測終了時の有効なシーンメッシュの集計。</summary>
        [Serializable]
        private sealed class PerformanceResult
        {
            public bool highQuality;
            public double durationSeconds, averageFrameMs, maximumFrameMs;
            public int frames, meshRenderers;
            public long totalTriangles;
            public int timingSamples, hitchesOver50Ms;
            public bool allocationCounterSupported;
            public double percentile95Ms, percentile99Ms;
            public double cpuFrameMs, gpuFrameMs, processCpuMsPerFrame, mainThreadBytesPerFrame;
        }

        /// <summary>注文の具材（おまかせはスタッフと同じ自動選択）。</summary>
        private static System.Collections.Generic.List<string> Picks(RestaurantSimulation sim, CustomerOrder o)
            => o.omakase ? Omakase.AutoPick(o, sim.Unlocks.Available, x => sim.Available(x, o)) : o.requested.ToList();

        /// <summary>1段階ずつ進める簡易調理（1ティックで終わらない工程は次のティックで続ける）。</summary>
        private static void AutoCook(RestaurantSimulation sim, CustomerOrder o)
        {
            switch (o.stage)
            {
                case OrderStage.Waiting:
                case OrderStage.Ordering:
                    if (!sim.Accept(o.id)) return;
                    if (o.stage == OrderStage.Selecting) { foreach (string id in Picks(sim, o)) sim.AddIngredient(o.id, id); sim.Weigh(o.id); }
                    break;
                case OrderStage.Selecting:
                    if (o.omakase && o.selected.Count == 0) foreach (string id in Picks(sim, o)) sim.AddIngredient(o.id, id);
                    foreach (string id in o.requested.Where(id => o.selected.Count(x => x == id) < o.requested.Count(x => x == id))) sim.AddIngredient(o.id, id);
                    if (!sim.Weigh(o.id)) sim.Cancel(o.id);
                    break;
                case OrderStage.Weighed: sim.StartHeating(o.id, true); break;
                case OrderStage.Heating:
                    if (Enumerable.Range(0, o.selected.Count).All(i => sim.Doneness(o, i) >= .9f)) { sim.Drain(o.id); sim.Shake(o.id); sim.Shake(o.id); sim.Shake(o.id); }
                    break;
                case OrderStage.Drained:
                    var soup = Content.Soup(o.soup);
                    sim.PourSoup(o.id, soup.Id, (soup.IdealMinMl + soup.IdealMaxMl) / 2);
                    for (int i = 0; i < o.spice; i++) sim.AddCondiment(o.id, Content.ChiliOil);
                    foreach (string id in o.requestedCondiments.Distinct()) sim.AddCondiment(o.id, id);
                    sim.Finish(o.id);
                    break;
                case OrderStage.Ready: if (o.takeout || o.seat >= 0) sim.Serve(o.id); break;
            }
        }

        /// <summary>
        /// 全具材を生・茹で上がりの2段で並べ、近くから撮る（食材の見た目の確認用）。
        /// 店から離れた場所に専用の台とカメラを置き、撮り終えたら片付ける。
        /// </summary>
        private IEnumerator FoodGallery()
        {
            var root = new GameObject("Food Gallery");
            root.transform.position = new Vector3(400, 0, 400);
            var table = GameObject.CreatePrimitive(PrimitiveType.Plane);
            table.transform.SetParent(root.transform, false);
            table.transform.localScale = new Vector3(1, 1, 1);
            table.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Lit(new Color(.45f, .42f, .4f));
            var light = new GameObject("Gallery Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = .35f; light.shadows = LightShadows.Soft;
            light.transform.SetParent(root.transform, false); light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var cam = new GameObject("Gallery Camera").AddComponent<Camera>();
            cam.transform.SetParent(root.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.3f, .28f, .27f);
            cam.depth = 100; cam.fieldOfView = 30; cam.nearClipPlane = .05f;
            var items = App.Catalog.ingredients;
            // UI はこのスクリプトと同じオブジェクトにあり、無効化するとコルーチンも止まるため、Canvas だけを隠す。
            var canvas = App.UI.GetComponentInChildren<Canvas>();
            canvas.enabled = false;
            const int perPage = 12, columns = 6;
            for (int page = 0; page * perPage < items.Length; page++)
            {
                var holder = new GameObject("Page").transform; holder.SetParent(root.transform, false);
                var slice = items.Skip(page * perPage).Take(perPage).ToArray();
                int visibleColumns = Math.Min(columns, slice.Length);
                cam.fieldOfView = visibleColumns <= 3 ? 32 : 52;
                for (int i = 0; i < slice.Length; i++)
                {
                    int row = i / columns, col = i % columns;
                    for (int cooked = 0; cooked < 2; cooked++)
                    {
                        var go = IngredientVisuals.Build(slice[i], holder, 1f);
                        go.transform.localPosition = new Vector3((col - (visibleColumns - 1) / 2f) * .42f + (cooked == 1 ? .19f : -.04f), .1f, row == 0 ? .3f : -.15f);
                        go.transform.localRotation = Quaternion.Euler(0, 25 + i * 37, 0);
                        go.GetComponent<IngredientView>()?.SetDoneness(cooked == 1 ? 1f : 0f);
                    }
                }
                cam.transform.localPosition = new Vector3(.075f, 1.45f, -1.25f);
                cam.transform.LookAt(root.transform.position + new Vector3(.075f, .05f, .07f));
                yield return new WaitForSeconds(.5f);
                yield return Capture("00-food-" + (page + 1));
                Destroy(holder.gameObject);
            }
            canvas.enabled = true;
            Destroy(root);
        }

        private IEnumerator CaptureMovement()
        {
            Vector3 original = App.World.Player.position;
            var agent = App.World.Player.GetComponent<UnityEngine.AI.NavMeshAgent>();
            try
            {
                yield return Capture("03e-movement-0");
                App.World.TouchMovement = new Vector2(0, 1);
                yield return new WaitForSeconds(.06f); yield return Capture("03e-movement-1");
                yield return new WaitForSeconds(.06f); yield return Capture("03e-movement-2");
                App.World.TouchMovement = Vector2.zero;
                yield return new WaitForSeconds(.1f); yield return Capture("03e-movement-3");
            }
            finally { App.World.TouchMovement = Vector2.zero; agent.Warp(original); }
        }

        /// <summary>タブレットと携帯の縦横比で撮影し、元のウィンドウ寸法へ戻す。</summary>
        private IEnumerator CaptureLayouts(string name)
        {
            int width = Screen.width, height = Screen.height;
            try
            {
                foreach (var (w, h, suffix) in new[] { (1024, 768, "ipad"), (590, 1280, "phone-portrait"), (1280, 590, "phone-landscape") })
                {
                    Screen.SetResolution(w, h, FullScreenMode.Windowed);
                    yield return new WaitForSeconds(1.5f);
                    yield return Capture(name + "-" + suffix);
                }
            }
            finally { Screen.SetResolution(width, height, FullScreenMode.Windowed); }
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>描画が完了した画面をPNGファイルに保存する。</summary>
        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            // モバイルでは CaptureScreenshot のパスが保存領域からの相対になるため、テクスチャ経由で書き出す。
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);
            yield return new WaitForSeconds(.5f);
        }

        private void OnLog(string condition, string trace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            File.WriteAllText(Path.Combine(Output, "smoke-failure.txt"), condition + "\n" + trace);
            Application.Quit(1);
        }

        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
    }
}
#endif
