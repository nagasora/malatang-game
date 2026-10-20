using System.Linq;
using System.Reflection;
using MalatangStory.Game;
using MalatangStory.Ingredients;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MalatangStory.Tests
{
    /// <summary>希少食材の形状・模様・加熱状態と、金華ハムの厚い三層断面を保証する。</summary>
    public sealed class RarePresentationTests
    {
        /// <summary>希少6種は模様付きで加熱色が変わり、共有モデルの実寸・描画予算を満たす。</summary>
        [TestCase("matsutake")]
        [TestCase("black_truffle")]
        [TestCase("jinhua_ham")]
        [TestCase("wagyu")]
        [TestCase("kuruma_shrimp")]
        [TestCase("abalone")]
        public void RareModelsHaveTexturesCookingStatesAndBoundedGeometry(string id)
        {
            using (FoodModel model = new FoodRare().Build(id))
            {
                Assert.IsNotNull(model);
                Assert.IsTrue(model.IsAlive());
                Assert.That(model.TriangleCount, Is.InRange(1, 1500));
                float size = Mathf.Max(model.Bounds.size.x, Mathf.Max(model.Bounds.size.y, model.Bounds.size.z));
                Assert.That(size, Is.InRange(.1f, .25f));
                Assert.AreEqual(model.Materials.Length, model.Surfaces.Length);
                for (int i = 0; i < model.Materials.Length; i++)
                {
                    Assert.IsNotNull(model.Materials[i].GetTexture("_BaseMap"));
                    Assert.AreNotEqual(model.Surfaces[i].RawTint, model.Surfaces[i].CookedTint);
                    Assert.Greater(model.Surfaces[i].CookedSmoothness, model.Surfaces[i].RawSmoothness);
                }
            }
        }

        /// <summary>縦の希少図鑑は2列で、590px幅でも本文が約14実ピクセルになる文字サイズを維持する。</summary>
        [Test]
        public void PortraitRareCollectionUsesTwoColumnsAndReadableUnshrunkText()
        {
            Font originalFont = UIKit.Font;
            var host = new GameObject("Rare Collection Application");
            host.SetActive(false);
            var root = new GameObject("Rare Collection Layout", typeof(RectTransform));
            try
            {
                UIKit.Font = Resources.Load<Font>("Fonts/NotoSansJP") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var catalog = IngredientCatalog.Load();
                var sim = new RestaurantSimulation(catalog);
                sim.State.ingredients.rareObtainedIds.Add("jinhua_ham");
                var app = host.AddComponent<GameApplication>();
                typeof(GameApplication).GetProperty(nameof(GameApplication.Catalog)).SetValue(app, catalog);
                typeof(GameApplication).GetProperty(nameof(GameApplication.Simulation)).SetValue(app, sim);
                var screen = new CollectionScreen();
                typeof(UIScreen).GetField("App", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(screen, app);
                var content = root.GetComponent<RectTransform>();
                content.sizeDelta = new Vector2(1076, 1500);
                typeof(CollectionScreen).GetField("content", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(screen, content);
                typeof(CollectionScreen).GetField("portrait", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(screen, true);
                typeof(CollectionScreen).GetMethod("BuildIngredients", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screen, new object[] { true });
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);

                var grid = root.GetComponentInChildren<GridLayoutGroup>();
                Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
                Assert.AreEqual(2, grid.constraintCount);
                Assert.AreEqual(520, grid.cellSize.x);
                Assert.AreEqual(420, grid.cellSize.y);
                Assert.AreEqual(6, grid.transform.childCount);
                foreach (Text label in root.GetComponentsInChildren<Text>())
                {
                    Assert.GreaterOrEqual(label.fontSize * 590f / 1280f, 13.5f, label.text);
                    Assert.IsFalse(label.resizeTextForBestFit, label.text);
                }
                var cards = grid.transform.Cast<Transform>().ToArray();
                Assert.IsTrue(cards.Single(card => card.name == "Food jinhua_ham").GetComponentsInChildren<Text>().Any(label => label.text == "金華ハム"));
                Assert.IsTrue(cards.Single(card => card.name == "Food matsutake").GetComponentsInChildren<Text>().Any(label => label.text == "？？？"));
            }
            finally
            {
                UIKit.Font = originalFont;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>向きを変えないセーフエリアの寸法変更でも再構築し、選択中の希少タブを保持する。</summary>
        [Test]
        public void SameOrientationSafeAreaResizeRebuildsCollectionAndPreservesSelectedTab()
        {
            Font originalFont = UIKit.Font;
            int originalTab = CollectionScreen.StartTab;
            var host = new GameObject("Resizable Rare Collection Application");
            host.SetActive(false);
            var root = new GameObject("Resizable Rare Collection", typeof(RectTransform));
            try
            {
                UIKit.Font = Resources.Load<Font>("Fonts/NotoSansJP") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var catalog = IngredientCatalog.Load();
                var app = host.AddComponent<GameApplication>();
                typeof(GameApplication).GetProperty(nameof(GameApplication.Catalog)).SetValue(app, catalog);
                typeof(GameApplication).GetProperty(nameof(GameApplication.Simulation)).SetValue(app, new RestaurantSimulation(catalog));
                CollectionScreen.StartTab = 7;
                var screen = new CollectionScreen();
                screen.Create(app, root.GetComponent<RectTransform>());
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo area = typeof(CollectionScreen).GetField("layoutSafeArea", flags);
                FieldInfo content = typeof(CollectionScreen).GetField("content", flags);
                object oldContent = content.GetValue(screen);
                Rect previousArea = Screen.safeArea;
                previousArea.height += 100f;
                area.SetValue(screen, previousArea);
                bool orientation = (bool)typeof(CollectionScreen).GetField("portrait", flags).GetValue(screen);

                screen.Tick(0);

                Assert.AreEqual(orientation, typeof(CollectionScreen).GetField("portrait", flags).GetValue(screen));
                Assert.AreEqual(Screen.safeArea, area.GetValue(screen));
                Assert.AreNotSame(oldContent, content.GetValue(screen));
                Assert.AreEqual(7, typeof(CollectionScreen).GetField("tab", flags).GetValue(screen));
                Assert.AreEqual(6, ((RectTransform)content.GetValue(screen)).GetComponentsInChildren<Text>().Count(label => label.text == "？？？"));
                object rebuiltContent = content.GetValue(screen);
                screen.Tick(0);
                Assert.AreSame(rebuiltContent, content.GetValue(screen), "寸法が変わらないフレームでは再構築しない");
            }
            finally
            {
                CollectionScreen.StartTab = originalTab;
                UIKit.Font = originalFont;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>金華ハムは薄い円盤ではなく、皮・脂・赤身の三表面を持つ厚い塊にする。</summary>
        [Test]
        public void CuredHamHasAThickBlockAndThreeSeparateSkinFatAndMeatSurfaces()
        {
            using (FoodModel model = new FoodRare().Build("jinhua_ham"))
            {
                Assert.AreEqual(3, model.Materials.Length);
                Assert.GreaterOrEqual(model.Bounds.size.y, .09f);
                Assert.Greater(model.Bounds.size.z, model.Bounds.size.y);
                Assert.AreNotSame(model.Materials[0].GetTexture("_BaseMap"), model.Materials[1].GetTexture("_BaseMap"));
                Assert.AreNotSame(model.Materials[1].GetTexture("_BaseMap"), model.Materials[2].GetTexture("_BaseMap"));
            }
        }
    }
}
