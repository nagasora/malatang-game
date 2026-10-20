using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>スープ描画の更新と、アプリ終了時の所有権終了を実環境で検証します。</summary>
namespace MalatangStory.Tests
{
    /// <summary>スープ変更時の湯気追従と、共有リソースの安全な解放を保証します。</summary>
    public sealed class SoupVisualsTests
    {
        private readonly List<GameObject> owned = new List<GameObject>();
        private string originalSavePath;
        private string temporaryDirectory;
        private Font originalFont;
        private Action originalClickSound;

        /// <summary>保存先とUI共有状態を退避し、通常の保存データを触らないようにします。</summary>
        [SetUp]
        public void SetUp()
        {
            originalSavePath = GameApplication.SavePathOverride;
            originalFont = UIKit.Font; originalClickSound = UIKit.ClickSound;
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "malatang-soup-tests-" + Guid.NewGuid());
            GameApplication.SavePathOverride = Path.Combine(temporaryDirectory, "save.json");
            BowlPortrait.ReleaseAll();
        }

        /// <summary>描画利用者の停止と遅延破棄を待ち、キャッシュと共有状態を復元します。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject value in owned)
            {
                if (value == null) continue;
                value.SetActive(false); UnityEngine.Object.Destroy(value);
            }
            owned.Clear();
            yield return null;
            yield return null;
            BowlPortrait.ReleaseAll();
            MaterialLibrary.ReleaseAll(); SoupVisuals.ReleaseAll();
            GameApplication.SavePathOverride = originalSavePath;
            UIKit.Font = originalFont; UIKit.ClickSound = originalClickSound;
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }

        /// <summary>六種すべての生成画素に、暗い外周と視認できる濃淡があり、値は有限です。</summary>
        [TestCase("mala")]
        [TestCase("baitang")]
        [TestCase("tomato")]
        [TestCase("sour")]
        [TestCase("mushroom")]
        [TestCase("dry")]
        public void GeneratedPixelsHaveVisibleRadialDepthAndVariation(string soupId)
        {
            Color[] pixels = SoupVisuals.GenerateSurfacePixels(soupId, 0);
            Assert.AreEqual(256 * 256, pixels.Length);
            float innerSum = 0f, outerSum = 0f, darkest = 1f, lightest = 0f;
            int innerCount = 0, outerCount = 0;
            bool valid = true;
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    Color pixel = pixels[y * 256 + x];
                    valid &= pixel.r >= 0f && pixel.r <= 1f && pixel.g >= 0f && pixel.g <= 1f
                        && pixel.b >= 0f && pixel.b <= 1f && pixel.a == 1f;
                    float px = x / 255f * 2f - 1f, pz = y / 255f * 2f - 1f;
                    float radius = Mathf.Sqrt(px * px + pz * pz);
                    float value = Luminance(pixel);
                    if (radius < .4f) { innerSum += value; innerCount++; }
                    if (radius > .82f && radius < .93f) { outerSum += value; outerCount++; }
                    if (radius < .85f) { darkest = Mathf.Min(darkest, value); lightest = Mathf.Max(lightest, value); }
                }
            Assert.IsTrue(valid);
            Assert.Greater(innerSum / innerCount - outerSum / outerCount, .018f, "液面外周に深さを示す暗部がある");
            Assert.Greater(lightest - darkest, .08f, "単色の液面に戻らない");
        }

        /// <summary>白湯の油玉は中心が十分に色付き、外周に中心より明るい輪郭を持ちます。</summary>
        [Test]
        public void OilDropsHaveVisibleBodiesAndBrighterRims()
        {
            Color[] pixels = SoupVisuals.GenerateSurfacePixels("baitang", 0);
            System.Random random = new System.Random(CustomerDirector.StableHash("baitang"));
            int count = SoupAppearance.ForSoup("baitang", 0).OilDrops;
            Vector3[] drops = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                GarnishPlacement placement = SoupAppearance.Scatter(random, .84f);
                float variation = (float)random.NextDouble();
                float radius = index % 5 == 0 ? .072f + variation * .022f : .014f + variation * .03f;
                drops[index] = new Vector3(placement.X, placement.Z, radius);
            }
            bool visible = false;
            foreach (Vector3 drop in drops)
            {
                Color center = PixelAt(pixels, drop.x, drop.y);
                Color rim = PixelAt(pixels, drop.x + drop.z * .83f, drop.y);
                visible |= center.r - center.g > .2f && Luminance(rim) - Luminance(center) > .1f;
            }
            Assert.IsTrue(visible, "SmoothStepのfrom/toを閾値と誤用して油玉の不透明度を潰さない");
        }

        /// <summary>辛さの増加で赤い油膜が増え、同じ設定の画素列は再現可能です。</summary>
        [Test]
        public void HotOilCoverageIncreasesAndPixelsAreDeterministic()
        {
            Color[] mild = SoupVisuals.GenerateSurfacePixels("baitang", 0);
            Color[] hot = SoupVisuals.GenerateSurfacePixels("baitang", 5);
            Color[] repeat = SoupVisuals.GenerateSurfacePixels("baitang", 5);
            CollectionAssert.AreEqual(hot, repeat);
            int mildOil = 0, hotOil = 0;
            for (int index = 0; index < mild.Length; index++)
            {
                if (mild[index].r > mild[index].g + .18f && mild[index].g < .55f) mildOil++;
                if (hot[index].r > hot[index].g + .18f && hot[index].g < .55f) hotOil++;
            }
            Assert.Greater(hotOil, mildOil);
        }

        /// <summary>色差比較に使う相対輝度を返します。</summary>
        private static float Luminance(Color color) => color.r * .2126f + color.g * .7152f + color.b * .0722f;

        /// <summary>正規化した液面座標の最寄り画素を取得します。</summary>
        private static Color PixelAt(Color[] pixels, float x, float z)
        {
            int column = Mathf.Clamp(Mathf.RoundToInt((x + 1f) * .5f * 255f), 0, 255);
            int row = Mathf.Clamp(Mathf.RoundToInt((z + 1f) * .5f * 255f), 0, 255);
            return pixels[row * 256 + column];
        }

        /// <summary>標準の白ごまと唐辛子片は共有結合メッシュで描画し、注文薬味を変更せず種類と辛さへ追従します。</summary>
        [UnityTest]
        public IEnumerator StandardSeasoningSharesMeshesAndFollowsSoupWithoutChangingCondiments()
        {
            GameObject host = new GameObject("Seasoning Test Host"); owned.Add(host);
            GameObject first = SoupVisuals.CreateSurface(host.transform, "mala", 0, .44f, .2f, false, false);
            GameObject second = SoupVisuals.CreateSurface(host.transform, "mala", 0, .32f, .2f, false, false);
            Transform original = first.transform.Find("Soup Seasoning");
            Assert.NotNull(original);
            Assert.AreEqual(2, original.GetComponentsInChildren<MeshRenderer>().Length);
            Mesh sesame = original.Find("Sesame").GetComponent<MeshFilter>().sharedMesh;
            Mesh chili = original.Find("Chili Flakes").GetComponent<MeshFilter>().sharedMesh;
            Assert.AreSame(sesame, second.transform.Find("Soup Seasoning/Sesame").GetComponent<MeshFilter>().sharedMesh);
            Assert.AreSame(chili, second.transform.Find("Soup Seasoning/Chili Flakes").GetComponent<MeshFilter>().sharedMesh);
            SoupVisuals.ConfigureSurface(first.transform, "mala", 0);
            Assert.AreSame(original, first.transform.Find("Soup Seasoning"), "同一状態では作り直さない");
            SoupVisuals.ConfigureSurface(first.transform, "baitang", 0);
            Assert.IsFalse(original.gameObject.activeSelf, "遅延破棄を待たず標準トッピングを取り除く");
            Assert.IsNull(first.transform.Find("Soup Seasoning"));
            SoupVisuals.ConfigureSurface(first.transform, "mala", 5);
            Mesh hotChili = first.transform.Find("Soup Seasoning/Chili Flakes").GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(hotChili.vertexCount, chili.vertexCount, "辛いスープでは唐辛子片が増える");
            SoupVisuals.ConfigureSurface(first.transform, "mala", 0);
            Assert.AreSame(sesame, first.transform.Find("Soup Seasoning/Sesame").GetComponent<MeshFilter>().sharedMesh);
            Assert.AreSame(chili, first.transform.Find("Soup Seasoning/Chili Flakes").GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.Destroy(first);
            yield return null;
            Assert.IsTrue(sesame != null && chili != null, "個々の器は共有メッシュを破棄しない");
            SoupVisuals.ConfigureSurface(second.transform, "dry", 0);
            Assert.NotNull(second.transform.Find("Soup Seasoning/Sesame"));
        }

        /// <summary>利用者を停止した後の一括解放で、標準トッピングの共有メッシュも解放します。</summary>
        [UnityTest]
        public IEnumerator ReleasingSoupCacheDestroysStandardSeasoningMeshes()
        {
            GameObject host = new GameObject("Seasoning Lifetime Host"); owned.Add(host);
            GameObject surface = SoupVisuals.CreateSurface(host.transform, "mala", 0, .44f, .2f, false, false);
            Mesh sesame = surface.transform.Find("Soup Seasoning/Sesame").GetComponent<MeshFilter>().sharedMesh;
            Mesh chili = surface.transform.Find("Soup Seasoning/Chili Flakes").GetComponent<MeshFilter>().sharedMesh;
            host.SetActive(false);
            UnityEngine.Object.Destroy(host);
            SoupVisuals.ReleaseAll();
            yield return null;
            Assert.IsTrue(sesame == null && chili == null);
        }

        /// <summary>液面を麻辣から汁なしへ変更すると、同じ粒子系の湯気が減り、戻すと元の量へ戻ります。</summary>
        [UnityTest]
        public IEnumerator ChangingSoupUpdatesExistingSteamIntensity()
        {
            GameObject host = new GameObject("Soup Test Host"); owned.Add(host);
            GameObject surface = SoupVisuals.CreateSurface(host.transform, "mala", 0, .32f, .2f);
            ParticleSystem steam = surface.GetComponentInChildren<ParticleSystem>();
            Assert.NotNull(steam);
            float original = steam.emission.rateOverTime.constant;
            SoupVisuals.ConfigureSurface(surface.transform, "dry", 0);
            Assert.AreSame(steam, surface.GetComponentInChildren<ParticleSystem>());
            Assert.AreEqual(12f * SoupAppearance.ForSoup("dry", 0).SteamIntensity, steam.emission.rateOverTime.constant, .0001f);
            Assert.Less(steam.emission.rateOverTime.constant, original);
            SoupVisuals.ConfigureSurface(surface.transform, "mala", 0);
            Assert.AreEqual(original, steam.emission.rateOverTime.constant, .0001f);
            yield return null;
        }

        /// <summary>アプリコンポーネントだけの破棄でも、残るホストを停止し、独立ワールド・撮影台・写真・スープを解放します。</summary>
        [UnityTest]
        public IEnumerator DestroyingApplicationComponentEndsAllPresentationOwners()
        {
            GameObject host = new GameObject("Application Lifetime Test"); owned.Add(host);
            GameApplication app = host.AddComponent<GameApplication>();
            RestaurantWorld world = app.World;
            GameObject ui = app.UI.Safe.GetComponentInParent<Canvas>().gameObject;
            IngredientIconRenderer icons = app.Icons;
            Texture2D soup = SoupVisuals.Texture("mala", 1);
            AlbumEntry entry = new AlbumEntry
            {
                soup = "mala", spice = 1,
                ingredients = new List<string> { app.Catalog.ingredients[0].id }
            };
            Texture2D portrait = BowlPortrait.Get(entry);
            GameObject portraitStudio = GameObject.Find("Bowl Portrait Studio");
            Assert.NotNull(world); Assert.NotNull(portraitStudio);
            UnityEngine.Object.Destroy(app);
            yield return null;
            Assert.IsFalse(host.activeSelf, "コンポーネントだけを破棄しても残存UIを動かさない");
            Assert.IsTrue(world == null || !world.gameObject.activeInHierarchy);
            Assert.IsTrue(ui == null || !ui.activeInHierarchy);
            Assert.IsTrue(portraitStudio == null || !portraitStudio.activeInHierarchy);
            yield return null;
            Assert.IsTrue(world == null, "独立ワールドを解放する");
            Assert.IsTrue(icons == null, "アイコン生成の所有者を解放する");
            Assert.IsTrue(portraitStudio == null, "DontDestroyOnLoadの撮影台も解放する");
            Assert.IsTrue(portrait == null, "表示終了後に写真を解放する");
            Assert.IsTrue(soup == null, "利用者を停止してから共有スープを解放する");
        }
    }
}
