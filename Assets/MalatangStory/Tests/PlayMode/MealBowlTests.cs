using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>実際の厨房と客席で、一杯の進行・食後・保存復元を検証します。</summary>
namespace MalatangStory.Tests
{
    /// <summary>生成済み部品だけで食事進行を表示することを保証します。</summary>
    public sealed class MealBowlTests
    {
        private GameObject host;
        private GameApplication app;
        private string directory, previousSavePath;
        private Font previousFont;
        private Action previousClick;

        /// <summary>通常保存を隔離して実際のアプリと厨房を起動します。</summary>
        [SetUp]
        public void SetUp()
        {
            previousSavePath = GameApplication.SavePathOverride; previousFont = UIKit.Font; previousClick = UIKit.ClickSound;
            directory = Path.Combine(Path.GetTempPath(), "meal-bowl-tests-" + Guid.NewGuid());
            GameApplication.SavePathOverride = Path.Combine(directory, "save.json");
            host = new GameObject("Meal Bowl Tests"); app = host.AddComponent<GameApplication>(); app.NewGame();
            app.DevelopmentSpeed = 0f;
        }

        /// <summary>アプリ所有の描画を停止してから破棄し、共有UI設定を戻します。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (host != null) { host.SetActive(false); UnityEngine.Object.Destroy(host); }
            yield return null; yield return null;
            GameApplication.SavePathOverride = previousSavePath; UIKit.Font = previousFont; UIKit.ClickSound = previousClick;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        /// <summary>えのきだけを生で残し、ほかの二具材は食べられる注文を作ります。</summary>
        private CustomerOrder Order()
        {
            CustomerOrder order = new CustomerOrder { id = "bowl-test", customerName = "試食客", quality = 80,
                soup = "tomato", chosenSoup = "tomato", soupMl = 340, heatSeconds = 100,
                selected = new List<string> { "enoki", "bok_choy", "beef_slice" },
                requested = new List<string> { "enoki", "bok_choy", "beef_slice" } };
            for (int i = 0; i < order.selected.Count; i++) order.dropTimes.Add(i == 0 ? 100 : 100 - app.Catalog.Get(order.selected[i]).cookingSeconds);
            return order;
        }

        /// <summary>食べる順に具材が消え、残す具材は最後まで同じモデルで表示されます。</summary>
        [Test]
        public void EatingProgressRemovesOnlyEdibleIngredientsAndLowersSoup()
        {
            GameObject bowl = app.World.Kitchen.CreateServedBowl(Order(), Vector3.zero);
            MealBowlView view = bowl.GetComponent<MealBowlView>();
            Transform enoki = bowl.transform.Find("Ingredient_enoki"); Transform soup = bowl.transform.Find("Soup");
            float initialHeight = soup.localPosition.y; float initialFoodHeight = enoki.localPosition.y;
            Assert.AreEqual(3, view.VisibleIngredientCount);
            view.ApplyProgress(.5f, 14f, true); Assert.AreEqual(2, view.VisibleIngredientCount); Assert.IsTrue(enoki.gameObject.activeSelf);
            Assert.Less(soup.localPosition.y, initialHeight);
            view.ApplyProgress(1f, 28f, false); Assert.AreEqual(1, view.VisibleIngredientCount); Assert.IsTrue(enoki.gameObject.activeSelf);
            Assert.AreEqual(MealLeftovers.SmallSoupRemaining, view.SoupRemaining, .00001f);
            Assert.Less(enoki.localPosition.y, initialFoodHeight, "残った具材も液面とともに器の底へ下がること");
            Transform garnish = bowl.transform.Cast<Transform>().Single(x => x.name == "Garnishes");
            Assert.Less(garnish.localScale.x, 1f, "薬味も小さくなった液面内に収まること");
        }

        /// <summary>新しい食事表示の更新は部品数を変えず、管理ヒープ割り当てが0です。</summary>
        [Test]
        public void EatingUpdatesAllocateZeroManagedBytes()
        {
            MealBowlView view = app.World.Kitchen.CreateServedBowl(Order(), Vector3.zero).GetComponent<MealBowlView>();
            for (int i = 0; i < 1000; i++) view.ApplyProgress((i % 101) / 100f, i * .05f, true);
            int children = view.transform.childCount;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) view.ApplyProgress((i % 101) / 100f, i * .05f, true);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated); Assert.AreEqual(children, view.transform.childCount);
        }

        /// <summary>完食・完飲の器には具材も液面も残りません。</summary>
        [Test]
        public void FinishedFoodAndSoupHideAllContents()
        {
            CustomerOrder order = Order(); order.dropTimes[0] = 100 - app.Catalog.Get("enoki").cookingSeconds; order.quality = 100;
            MealBowlView view = app.World.Kitchen.CreateServedBowl(order, Vector3.zero).GetComponent<MealBowlView>();
            view.ApplyProgress(1f, 28f, false);
            Assert.AreEqual(0, view.VisibleIngredientCount); Assert.AreEqual(0f, view.SoupRemaining);
            Assert.IsFalse(view.transform.Find("Soup").gameObject.activeSelf);
            Assert.IsFalse(view.transform.Find("Meal Chopsticks").gameObject.activeSelf);
        }

        /// <summary>実ファイルから復元した客席の器に、元の色の液面と残った具材だけが表示されます。</summary>
        [Test]
        public void DirtyBowlRestoresActualContentsAfterSaveLoad()
        {
            RestaurantSimulation sim = app.Simulation; CustomerOrder order = Order();
            order.stage = OrderStage.Served; order.visualState = "eat"; order.seat = DiningLayout.Seats(sim.State).First().SeatId;
            sim.State.orders.Add(order); sim.State.phase = DayPhase.Open; sim.State.spawnSeconds = 9999;
            sim.State.report.served = 1; sim.State.stats.served = 1;
            sim.Tick(6f);
            SaveRepository repository = new SaveRepository(Path.Combine(directory, "leftovers.json")); repository.Save(sim.State);
            GameState loaded = repository.Load(); Assert.NotNull(loaded);
            app.World.Furniture.Rebuild(loaded, null);
            MealBowlView restored = UnityEngine.Object.FindObjectsByType<MealBowlView>(FindObjectsSortMode.None)
                .Last(x => x.name == "Leftover Bowl " + order.id);
            Assert.AreEqual(1, restored.VisibleIngredientCount);
            Assert.IsTrue(restored.transform.Find("Ingredient_enoki").gameObject.activeSelf);
            Assert.IsFalse(restored.transform.Find("Ingredient_bok_choy").gameObject.activeSelf);
            Assert.AreSame(SoupVisuals.Texture("tomato", 0), restored.transform.Find("Soup").GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap"));
            Assert.AreEqual(MealLeftovers.SmallSoupRemaining, restored.SoupRemaining, .00001f);
        }

        /// <summary>食後情報のない旧保存の汚れた席は従来の器で安全に表示されます。</summary>
        [Test]
        public void LegacyDirtyTableUsesFallbackBowl()
        {
            GameState state = app.Simulation.State; int table = state.furniture.First(x => Content.FurnitureById(x.defId).Kind == FurnitureKind.Table).uid;
            state.dirtyTables.Add(table); state.mealRemains.Clear(); app.World.Furniture.Rebuild(state, null);
            Assert.IsTrue(app.World.GetComponentsInChildren<Transform>().Any(x => x.name == "Dirty Bowl"));
        }

        /// <summary>実フレームで客が着席した後、シミュレーションの食事秒が器へ反映されます。</summary>
        [UnityTest]
        public IEnumerator ActualDiningActorUpdatesBowlWithMealTime()
        {
            RestaurantSimulation sim = app.Simulation; CustomerOrder order = Order();
            sim.State.tutorialComplete = true; sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            sim.State.phase = DayPhase.Open; sim.State.spawnSeconds = 9999;
            order.stage = OrderStage.Served; order.visualState = "eat"; order.seat = DiningLayout.Seats(sim.State).First().SeatId;
            sim.State.orders.Add(order); sim.State.report.served = 1; sim.State.stats.served = 1;
            app.BackToStore(); app.DevelopmentSpeed = 0f;
            MealBowlView view = null; float deadline = Time.realtimeSinceStartup + 20f;
            while (view == null && Time.realtimeSinceStartup < deadline)
            {
                view = UnityEngine.Object.FindObjectsByType<MealBowlView>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "Served Bowl " + order.id);
                yield return null;
            }
            Assert.NotNull(view, "客が実際に着席して器が表示されること");
            Assert.AreEqual(3, view.VisibleIngredientCount);
            sim.Tick(14f); yield return null;
            Assert.AreEqual(2, view.VisibleIngredientCount);
            sim.Tick(14f); yield return null;
            MealBowlView leftover = UnityEngine.Object.FindObjectsByType<MealBowlView>(FindObjectsSortMode.None).Single(x => x.name == "Leftover Bowl " + order.id);
            Assert.AreEqual(1, leftover.VisibleIngredientCount);
            Assert.IsTrue(view == null || !view.gameObject.activeInHierarchy);
        }
        /// <summary>残具材は器の内底に接し、薬味の親は低下した液面を追従します。</summary>
        [Test]
        public void LeftoversTouchBowlAndGarnishesFollowSurface()
        {
            CustomerOrder order = Order(); order.selected[0] = "yuba"; order.condiments.Add(Content.ChiliOil);
            MealBowlView view = app.World.Kitchen.CreateServedBowl(order, Vector3.zero).GetComponent<MealBowlView>();
            view.ApplyProgress(1f, 28f, false);
            Transform piece = view.transform.Find("Ingredient_yuba"); BoxCollider box = piece.GetComponent<BoxCollider>();
            float bottom = float.PositiveInfinity;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = box.center + Vector3.Scale(box.size * .5f, new Vector3(x, y, z));
                bottom = Mathf.Min(bottom, view.transform.InverseTransformPoint(piece.TransformPoint(corner)).y);
            }
            Assert.AreEqual(.075f, bottom, .0001f);
            Assert.AreEqual(view.transform.Find("Soup").localPosition.y - .003f,
                view.transform.Find("Garnishes").localPosition.y, .0001f);
        }

        /// <summary>細長いえのきは寝かされ、器の縁から大きく突出しません。</summary>
        [Test]
        public void EnokiDiningPoseStaysBelowRim()
        {
            MealBowlView view = app.World.Kitchen.CreateServedBowl(Order(), Vector3.zero).GetComponent<MealBowlView>();
            Transform piece = view.transform.Find("Ingredient_enoki");
            Assert.Less(Mathf.Abs(Vector3.Dot(piece.up, view.transform.up)), .4f);
            BoxCollider box = piece.GetComponent<BoxCollider>();
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = box.center + Vector3.Scale(box.size * .5f, new Vector3(x, y, z));
                Assert.Less(view.transform.InverseTransformPoint(piece.TransformPoint(corner)).y, .41f);
            }
        }

        /// <summary>接写時刻では箸と一口が持ち上がったまま表示されます。</summary>
        [Test]
        public void CloseupTimeShowsRaisedChopsticksAndMouthful()
        {
            MealBowlView view = app.World.Kitchen.CreateServedBowl(Order(), Vector3.zero).GetComponent<MealBowlView>();
            view.ApplyProgress(4.15f / 6f, 4.15f, true);
            Transform sticks = view.transform.Find("Meal Chopsticks");
            Assert.IsTrue(sticks.gameObject.activeSelf); Assert.IsTrue(sticks.Find("Mouthful").gameObject.activeSelf);
            Assert.Greater(sticks.localPosition.y, 1f);
        }

        /// <summary>接写カメラでは器・箸・一口の描画範囲が画像端で切れません。</summary>
        [Test]
        public void CloseupCameraKeepsRaisedBiteInsideFrame()
        {
            MealBowlView view = app.World.Kitchen.CreateServedBowl(Order(), Vector3.zero).GetComponent<MealBowlView>();
            view.ApplyProgress(4.15f / 6f, 4.15f, true);
            GameObject cameraObject = new GameObject("Framing Test Camera"); cameraObject.transform.SetParent(view.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>(); camera.fieldOfView = 32f; camera.aspect = 1280f / 720f;
            SmokePlaythrough.FrameMealCloseup(camera, view);
            foreach (MeshRenderer renderer in view.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    Vector3 viewport = camera.WorldToViewportPoint(corner);
                    Assert.That(viewport.x, Is.InRange(.02f, .98f)); Assert.That(viewport.y, Is.InRange(.02f, .98f));
                    Assert.Greater(viewport.z, camera.nearClipPlane);
                }
            }
        }

        /// <summary>詳細情報の席と旧情報の席を同一テーブルで共存表示します。</summary>
        [Test]
        public void MixedLegacyAndDetailedSeatsBothDisplayBowls()
        {
            GameState state = app.Simulation.State; var seats = DiningLayout.Seats(state).ToList();
            int table = seats.First().Table.uid; int seat = seats.First().SeatId;
            state.dirtyTables.Add(table); state.mealRemains.Add(new MealRemains { orderId = "mixed", seat = seat,
                selected = new List<string> { "enoki" }, leftovers = MealLeftovers.Evaluate(Order(), app.Catalog) });
            app.World.Furniture.Rebuild(state, null);
            Transform[] children = app.World.GetComponentsInChildren<Transform>();
            Assert.IsTrue(children.Any(x => x.name == "Leftover Bowl mixed"));
            Assert.AreEqual(seats.Count(x => x.Table.uid == table) - 1, children.Count(x => x.name == "Dirty Bowl"));
        }

        /// <summary>未片付けで閉店しても翌日に古い器は残らず同じ席へ提供できます。</summary>
        [UnityTest]
        public IEnumerator NextDayClearsOldBowlsBeforeServingSameSeat()
        {
            RestaurantSimulation sim = app.Simulation; CustomerOrder old = Order();
            old.stage = OrderStage.Served; old.visualState = "eat"; old.seat = DiningLayout.Seats(sim.State).First().SeatId;
            sim.State.orders.Add(old); sim.State.phase = DayPhase.Open; sim.State.spawnSeconds = 9999;
            sim.State.tutorialComplete = true; sim.State.tutorialStep = RestaurantSimulation.TutorialFinished;
            sim.State.report.served = 1; sim.State.stats.served = 1; sim.Tick(28f);
            Assert.IsTrue(app.World.GetComponentsInChildren<MealBowlView>().Any());
            sim.State.phase = DayPhase.Closing; sim.Tick(20f);
            Assert.AreEqual(DayPhase.Report, sim.State.phase); Assert.IsTrue(sim.NextDay());
            Assert.IsFalse(app.World.GetComponentsInChildren<Transform>().Any(x => x.name.StartsWith("Leftover Bowl ") || x.name == "Dirty Bowl"));
            yield return null;
            CustomerOrder fresh = Order(); fresh.id = "next-day-bowl"; fresh.stage = OrderStage.Ready; fresh.seat = old.seat;
            sim.State.orders.Add(fresh); sim.State.phase = DayPhase.Open; sim.State.spawnSeconds = 9999;
            Assert.IsTrue(sim.Serve(fresh.id)); app.BackToStore();
            float deadline = Time.realtimeSinceStartup + 20f; MealBowlView served = null;
            while (served == null && Time.realtimeSinceStartup < deadline)
            {
                served = app.World.GetComponentsInChildren<MealBowlView>().FirstOrDefault(x => x.name == "Served Bowl " + fresh.id);
                yield return null;
            }
            Assert.NotNull(served);
            Assert.IsFalse(app.World.GetComponentsInChildren<Transform>().Any(x => x.name.StartsWith("Leftover Bowl ") || x.name == "Dirty Bowl"));
        }
    }
}
