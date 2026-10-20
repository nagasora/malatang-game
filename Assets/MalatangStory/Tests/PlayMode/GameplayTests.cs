using System;
using System.Collections;
using System.IO;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MalatangStory.Tests
{
    /// <summary>実際の画面のボタン操作と店内の移動で、初日の営業から翌日までを通しで遊べることを保証する。</summary>
    public class GameplayTests
    {
        private static Button FindButton(string name)
            => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.gameObject.activeInHierarchy && x.name == name);

        private static IEnumerator Click(string name)
        {
            Button button = null;
            float deadline = Time.realtimeSinceStartup + 3;
            while ((button = FindButton(name)) == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.NotNull(button, "ボタン「" + name + "」が表示されていること");
            Assert.IsTrue(button.interactable, "ボタン「" + name + "」が押せること");
            button.onClick.Invoke();
            yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), message);
        }

        [UnityTest]
        public IEnumerator FirstDayCanBePlayedThroughTheRealUIAndReachesDayTwo()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-playtest-" + Guid.NewGuid());
            GameApplication.SavePathOverride = Path.Combine(folder, "save.json");
            var go = new GameObject("Play Test");
            var app = go.AddComponent<GameApplication>();
            yield return null;
            try
            {
                Assert.NotNull(app.World.View);
                Assert.IsTrue(app.World.Player.gameObject.activeInHierarchy);
                Assert.AreEqual(GameScreen.Title, app.Screen);
                yield return Click("新しく始める");
                Assert.AreEqual(GameScreen.Morning, app.Screen);
                yield return Click("練習営業を始める");
                Assert.AreEqual(DayPhase.Open, app.Simulation.State.phase);
                Assert.AreEqual(GameScreen.Store, app.Screen);
                var sim = app.Simulation;

                Assert.IsFalse(app.World.WalkTo(new Vector3(100, 0, 100)), "店の外の遠くへは歩けない");
                Assert.IsTrue(app.World.WalkTo(app.World.Player.position + new Vector3(1.2f, 0, -1.2f)));
                yield return WaitUntil(() => sim.State.tutorialStep >= RestaurantSimulation.Tutorial.Accept, 6, "歩くと移動の練習が終わる");

                var order = sim.Active.Single();
                app.SelectOrder(order.id);
                yield return WaitUntil(() => app.Screen == GameScreen.Register, 10, "レジまで歩いて接客画面が開く");
                Assert.AreEqual(OrderStage.Ordering, order.stage);
                yield return Click("トマト");
                yield return Click(order.spice.ToString());
                yield return Click("店内");
                yield return Click("注文票を確定");
                Assert.IsFalse(order.ticketConfirmed, "聞き間違えたスープでは注文票を確定できない");
                Assert.AreEqual(1, order.registerMistakes);
                yield return Click("麻辣");
                yield return Click("注文票を確定");
                Assert.IsTrue(order.ticketConfirmed);
                Assert.AreEqual(PaymentMethod.Cash, order.payment, "練習のお客さんは現金で払う");
                long change = RestaurantSimulation.ExpectedChange(order);
                Assert.Greater(change, 0);
                string[] labels = { "100元", "50元", "20元", "10元", "5元", "1元", "5角", "1角" };
                long rest = change;
                for (int i = 0; i < RestaurantSimulation.Denominations.Length; i++)
                    while (rest >= RestaurantSimulation.Denominations[i]) { yield return Click(labels[i]); rest -= RestaurantSimulation.Denominations[i]; }
                long money = sim.State.moneyFen;
                yield return Click("おつりを渡す");
                Assert.AreEqual(OrderStage.Selecting, order.stage, "会計が済むと具材選びへ進む");
                Assert.AreEqual(money + order.billFen, sim.State.moneyFen, "代金だけが増える（おつりはぴったり）");
                Assert.AreEqual(1, sim.State.report.exactChange);
                yield return Click("厨房で作る");
                yield return WaitUntil(() => app.Screen == GameScreen.Cooking, 10, "厨房まで歩いて調理画面が開く");
                foreach (string id in order.requested) yield return Click(sim.Catalog.Get(id).displayName);
                Assert.AreEqual(RestaurantSimulation.Tutorial.Weigh, sim.State.tutorialStep);
                yield return new WaitForSeconds(2.5f);
                Assert.AreEqual(order.selected.Count, app.World.Kitchen.PiecesInBowl, "取った具材は物理演算で器の中に収まる " + app.World.Kitchen.DescribeBowl());
                yield return Click("秤で量る");
                Assert.AreEqual(OrderStage.Weighed, order.stage);
                Assert.Greater(order.paidFen, 0);
                yield return Click("時間差で茹でる（おすすめ）");
                Assert.AreEqual(OrderStage.Heating, order.stage);

                app.DevelopmentSpeed = 4;
                float slowest = order.selected.Max(x => sim.Catalog.Get(x).cookingSeconds);
                float deadline = Time.realtimeSinceStartup + 40;
                while (Enumerable.Range(0, order.selected.Count).Any(i => sim.Doneness(order, i) < 1f) && Time.realtimeSinceStartup < deadline)
                {
                    for (int i = 0; i < order.selected.Count; i++)
                    {
                        if (order.dropTimes[i] >= 0 || order.heatSeconds < slowest - sim.Catalog.Get(order.selected[i]).cookingSeconds) continue;
                        var row = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                            .Where(b => b.gameObject.activeInHierarchy && b.name == "投入").Select(b => b.transform.parent)
                            .FirstOrDefault(r => r.GetSiblingIndex() == i);
                        Assert.NotNull(row, "投入ボタンが表示されていること（" + i + "）");
                        row.Find("投入").GetComponent<Button>().onClick.Invoke();
                    }
                    yield return null;
                }
                app.DevelopmentSpeed = 1;
                Assert.IsTrue(order.dropTimes.All(t => t >= 0), "すべて投入ボタンで入れた");
                yield return Click("引き上げる");
                Assert.AreEqual(OrderStage.Drained, order.stage);
                yield return Click("振る"); yield return Click("振る"); yield return Click("振る");
                Assert.AreEqual(3, order.shakes);
                yield return Click("麻辣");
                var pour = UnityEngine.Object.FindObjectsByType<HoldButton>(FindObjectsSortMode.None).First(h => h.gameObject.activeInHierarchy);
                var pointer = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(pour.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                yield return WaitUntil(() => order.soupMl >= 330 || !pour.IsHeld, 8, "長押しでスープを注ぐ");
                pour = UnityEngine.Object.FindObjectsByType<HoldButton>(FindObjectsSortMode.None).FirstOrDefault(h => h.gameObject.activeInHierarchy);
                if (pour != null) ExecuteEvents.Execute(pour.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                Assert.That(order.soupMl, Is.InRange(300f, 380f), "初回の長押しが途中で切れず、理想量まで注げる");
                yield return Click("ラー油");
                yield return Click("完成！");
                Assert.AreEqual(OrderStage.Ready, order.stage);
                Assert.Greater(order.quality, 85, "理想的に作れば高品質");

                yield return Click("席へ運ぶ");
                Assert.AreEqual(GameScreen.Store, app.Screen);
                yield return WaitUntil(() => order.stage == OrderStage.Served, 15, "提供台から席まで運んで提供する");
                yield return WaitUntil(() => sim.State.dirtyTables.Count == 1, 10, "食べ終えると片付けが必要になる");
                app.Interact(Station.Table, sim.State.dirtyTables[0]);
                Assert.AreEqual(RestaurantSimulation.Tutorial.Close, sim.State.tutorialStep);
                yield return Click("初日を終える");
                Assert.AreEqual(DayPhase.Report, sim.State.phase);
                yield return WaitUntil(() => app.Screen == GameScreen.Report, 3, "営業レポートが開く");
                yield return Click("仕入れ");
                Assert.AreEqual(GameScreen.Shop, app.Screen);
                var buy = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                    .First(b => b.gameObject.activeInHierarchy && b.interactable && b.name.StartsWith("5個"));
                buy.onClick.Invoke();
                Assert.IsTrue(sim.State.tutorialComplete, "仕入れ画面のボタンで練習が完了する");
                app.BackToStore();
                yield return Click("翌日へ");
                Assert.AreEqual(2, sim.State.day);
                Assert.AreEqual(GameScreen.Morning, app.Screen);
                yield return null; yield return null;
                Assert.AreEqual(1, app.World.GetComponentsInChildren<NavMeshAgent>().Length, "前日の客は店から消えている");
                app.Save();
                Assert.NotNull(app.Saves.Load());
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
                GameApplication.SavePathOverride = null;
            }
            yield return null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
