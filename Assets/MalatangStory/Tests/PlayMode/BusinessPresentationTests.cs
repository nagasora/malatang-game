using System;
using System.Collections;
using System.IO;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace MalatangStory.Tests
{
    public sealed class BusinessPresentationTests
    {
        private static void Click(string caption)
        {
            var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.gameObject.activeInHierarchy && b.IsInteractable() && b.GetComponentInChildren<Text>()?.text == caption);
            Assert.NotNull(button, caption); Assert.IsTrue(button.interactable, caption); button.onClick.Invoke();
        }
        [UnityTest] public IEnumerator PhotoModePausesTheGameAndWritesAnActualFilteredPng()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-photo-ui-" + Guid.NewGuid());
            GameApplication.SavePathOverride = Path.Combine(folder, "save.json"); PhotoExportService.OutputDirectoryOverride = Path.Combine(folder, "photos");
            var root = new GameObject("Photo UI Test"); var app = root.AddComponent<GameApplication>(); yield return null;
            try
            {
                app.NewGame(); app.BeginPhoto(false); yield return new WaitForSeconds(.4f);
                Assert.AreEqual(WorldMode.Photo, app.World.Mode); Assert.IsTrue(app.UI.Current.PausesGame); Assert.AreEqual(0, app.Simulation.State.daySeconds);
                Click("フィルター：なし"); Click("フィルター：あたたかい（保存時）"); Click("PNGに保存");
                int guard = 0;
                while ((!Directory.Exists(PhotoExportService.OutputDirectoryOverride) || Directory.GetFiles(PhotoExportService.OutputDirectoryOverride, "*.png").Length == 0) && guard++ < 120) yield return null;
                string path = Directory.GetFiles(PhotoExportService.OutputDirectoryOverride, "*.png").Single();
                byte[] data = File.ReadAllBytes(path); Assert.Greater(data.Length, 1000); Assert.AreEqual(137, data[0]); Assert.AreEqual(80, data[1]);
                var image = new Texture2D(2, 2);
                try
                {
                    Assert.IsTrue(image.LoadImage(data)); Assert.Greater(image.width, 100);
                    var pixels = image.GetPixels32(); Assert.IsTrue(pixels.Any(p => p.r > 30));
                    foreach (var pixel in pixels.Where((p, index) => index % 97 == 0)) { Assert.LessOrEqual(Math.Abs(pixel.r - pixel.g), 1); Assert.LessOrEqual(Math.Abs(pixel.g - pixel.b), 1); }
                }
                finally { UnityEngine.Object.Destroy(image); }
                app.EndPhoto(); Assert.AreEqual(GameScreen.Morning, app.Screen);
            }
            finally { UnityEngine.Object.Destroy(root); GameApplication.SavePathOverride = null; PhotoExportService.OutputDirectoryOverride = null; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            yield return null;
        }
        [UnityTest] public IEnumerator FestivalStallCanBePlayedThroughItsRealButtons()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-stall-ui-" + Guid.NewGuid());
            GameApplication.SavePathOverride = Path.Combine(folder, "save.json");
            var root = new GameObject("Stall UI Test"); var app = root.AddComponent<GameApplication>(); yield return null;
            try
            {
                app.NewGame(); var sim = app.Simulation; sim.State.tutorialComplete = true; sim.State.day = 10; sim.State.shopLevel = 8; sim.State.moneyFen = 2000000; sim.State.dailyEvent = "festival";
                app.Open(GameScreen.Neighbourhood); Click("祭りの屋台を出す（限定5食材）"); Assert.IsTrue(sim.State.festivalStall);
                Assert.IsTrue(sim.BeginDay()); app.Open(GameScreen.Stall); yield return null;
                Assert.AreEqual(WorldMode.Stall, app.World.Mode); Assert.NotNull(GameObject.Find("Festival Stall"));
                var order = sim.Customers.Create("office"); sim.State.orders.Add(order); app.UI.Refresh();
                Click("受ける・量って会計"); app.UI.Refresh(); Assert.AreEqual(OrderStage.Weighed, order.stage);
                Click("ザルを鍋に入れる"); sim.Tick(sim.TargetHeat(order) / sim.CookSpeedMultiplier + .5f); app.UI.Refresh();
                Click("引き上げて湯切りへ"); app.UI.Refresh();
                for (int i = 0; i < 3; i++) { Click("手首でシャッ！ " + i + "/3"); app.UI.Refresh(); }
                Click("スープと薬味を添える"); app.UI.Refresh(); Click("仕上げる"); app.UI.Refresh();
                Click("屋台の窓口で渡す"); Assert.AreEqual(OrderStage.Served, order.stage); Assert.AreEqual(1, sim.State.report.served);
            }
            finally { UnityEngine.Object.Destroy(root); GameApplication.SavePathOverride = null; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            yield return null;
        }
        [UnityTest] public IEnumerator FranchiseArchivesAndVerifiesThePreviousStoreBeforeReplacingCampaign()
        {
            string folder = Path.Combine(Path.GetTempPath(), "malatang-franchise-ui-" + Guid.NewGuid());
            GameApplication.SavePathOverride = Path.Combine(folder, "save.json");
            var root = new GameObject("Franchise Test"); var app = root.AddComponent<GameApplication>(); yield return null;
            try
            {
                app.NewGame(); var previous = app.Simulation.State;
                previous.chapter = Chapters.All.Length; previous.stats.served = 321; previous.moneyFen = 1000000; app.Save();
                Assert.IsTrue(app.StartFranchise()); yield return null;
                Assert.AreEqual(1, app.Simulation.State.day); Assert.AreEqual(0, app.Simulation.State.stats.served); Assert.AreNotEqual(previous.storeId, app.Simulation.State.storeId);
                string archive = Directory.GetFiles(folder, "save.json.archive-*.json").Single();
                var restored = new SaveRepository(archive).Load(); Assert.NotNull(restored); Assert.AreEqual(321, restored.stats.served); Assert.AreEqual(1000000, restored.moneyFen);
                Assert.AreEqual(app.Simulation.State.storeId, app.Saves.Load().storeId);
            }
            finally { UnityEngine.Object.Destroy(root); GameApplication.SavePathOverride = null; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            yield return null;
        }
    }
}
