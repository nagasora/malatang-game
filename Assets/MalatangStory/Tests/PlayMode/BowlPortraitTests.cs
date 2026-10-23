using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MalatangStory.Game;
using MalatangStory.Ingredients;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MalatangStory.Tests
{
    /// <summary>一杯画像の共有・寿命・撮影時の状態復元と、図鑑の公開範囲を保証します。</summary>
    public sealed class BowlPortraitTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private IngredientCatalog catalog;
        private LightingSnapshot originalLighting;
        private RenderTexture originalActive;
        private Font originalFont;
        private Action originalClickSound;
        private int originalStartTab;

        [Test]
        public void PaintedGarnishPositionsArePartOfThePortraitCacheKey()
        {
            var order = new CustomerOrder { id = "painted-portrait", chosenSoup = "mala", chosenSpice = 1,
                selected = new List<string> { "enoki" }, condiments = new List<string> { "garlic" } };
            Texture2D initial = BowlPortrait.Get(order);
            order.garnishStrokes.Add(new GarnishStroke { id = "garlic", x = .4f, z = -.3f });
            Texture2D painted = BowlPortrait.Get(order);
            Assert.AreNotSame(initial, painted);
            Assert.AreSame(painted, BowlPortrait.Get(order));
        }

        /// <summary>各テストを空の画像キャッシュで開始し、変更する共有状態を退避します。</summary>
        [SetUp]
        public void SetUp()
        {
            originalLighting = new LightingSnapshot();
            originalActive = RenderTexture.active;
            originalFont = UIKit.Font;
            originalClickSound = UIKit.ClickSound;
            originalStartTab = CollectionScreen.StartTab;
            BowlPortrait.ReleaseAll();
            catalog = IngredientCatalog.Load();
        }

        /// <summary>失敗したテストでも共有状態を戻し、遅延破棄を次のテストへ持ち越しません。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderTexture.active = originalActive;
            originalLighting.Restore();
            UIKit.Font = originalFont;
            UIKit.ClickSound = originalClickSound;
            CollectionScreen.StartTab = originalStartTab;
            BowlPortrait.ReleaseAll();
            foreach (UnityEngine.Object value in owned)
                if (value != null) UnityEngine.Object.Destroy(value);
            owned.Clear();
            yield return null;
        }

        /// <summary>記録の人物・日付・品質によらず、同内容の一杯は同じ512角画像を共有します。</summary>
        [UnityTest]
        public IEnumerator EqualBowlContentsShareTextureAcrossDistinctAlbumEntries()
        {
            AlbumEntry first = Entry();
            AlbumEntry second = Entry();
            first.day = 1;
            first.customerName = "一人目";
            first.quality = 90;
            second.day = 20;
            second.customerName = "二人目";
            second.quality = 100;
            Texture2D image = BowlPortrait.Get(first);

            Assert.NotNull(image);
            Assert.AreEqual(512, image.width);
            Assert.AreEqual(512, image.height);
            Assert.AreEqual(TextureFormat.RGBA32, image.format);
            Assert.AreSame(image, BowlPortrait.Get(second));
            Assert.AreNotSame(first.ingredients, second.ingredients);
            yield return null;
        }

        /// <summary>白湯と麻辣の写真は器を大きく収め、縁を切らず、液面全体の色飽和を避けます。</summary>
        [UnityTest]
        public IEnumerator SoupPortraitFillsFrameWithoutClippingOrBroadHighlightSaturation()
        {
            foreach (string soupId in new[] { "baitang", "mala" })
            {
                AlbumEntry entry = Entry();
                entry.soup = soupId; entry.spice = 0;
                entry.ingredients.Clear(); entry.condiments.Clear();
                Texture2D image = BowlPortrait.Get(entry, false);
                Texture2D photo = BowlPortrait.Get(entry);
                Assert.AreNotSame(image, photo);
                Assert.Greater(ReadPortraitPixels(photo)[0].a, .99f, "料理写真には木目の卓上背景を含める");
                Color[] pixels = ReadPortraitPixels(image);
                int left = image.width, right = -1, opaque = 0, saturated = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color pixel = pixels[index];
                    if (pixel.a < .99f) continue;
                    opaque++;
                    if (Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) >= .995f) saturated++;
                    int x = index % image.width;
                    left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                }
                Assert.Greater(opaque, 0, soupId);
                Assert.Greater((right - left + 1f) / image.width, .76f, "器が枠内で小さくなり過ぎない");
                Assert.GreaterOrEqual(left, 4, "左縁を切らない");
                Assert.Less(right, image.width - 4, "右縁を切らない");
                Assert.Less(saturated / (float)opaque, .08f, soupId + "の広い液面を白飛び・色飽和させない");
            }
            yield return null;
        }

        /// <summary>GPU専用の写真を一時描画先から読み戻し、呼び出し元の描画先を復元します。</summary>
        private static Color[] ReadPortraitPixels(Texture2D image)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(image.width, image.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D readback = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(image, target);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0f, 0f, image.width, image.height), 0, 0, false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(readback);
            }
        }

        /// <summary>スープ未選択の注文は希望のスープを使い、同内容の記録と画像を共有して注文を変更しません。</summary>
        [UnityTest]
        public IEnumerator OrderWithoutChosenSoupSharesAlbumTextureWithoutMutatingOrder()
        {
            AlbumEntry entry = Entry();
            CustomerOrder order = Order(entry);
            order.chosenSoup = "";
            string before = JsonUtility.ToJson(order);

            Assert.AreSame(BowlPortrait.Get(entry), BowlPortrait.Get(order));
            Assert.AreEqual(before, JsonUtility.ToJson(order));
            yield return null;
        }

        /// <summary>希望と異なる選択済みスープは、希望ではなく選択内容の画像を返します。</summary>
        [UnityTest]
        public IEnumerator ChosenSoupOverridesRequestedSoupForPortraitCache()
        {
            AlbumEntry entry = Entry();
            CustomerOrder order = Order(entry);
            order.soup = Content.Soups.First(soup => soup.Id != entry.soup).Id;

            Assert.AreSame(BowlPortrait.Get(entry), BowlPortrait.Get(order));
            yield return null;
        }

        /// <summary>スープ・辛さ・具材・調味料の各変更は、それぞれ異なる画像として保持します。</summary>
        [UnityTest]
        public IEnumerator EveryBowlContentFieldChangeCreatesADistinctTexture()
        {
            AlbumEntry baseline = Entry();
            Texture2D original = BowlPortrait.Get(baseline);
            AlbumEntry soup = Entry();
            soup.soup = Content.Soups.First(value => value.Id != baseline.soup).Id;
            AlbumEntry spice = Entry();
            spice.spice++;
            AlbumEntry ingredient = Entry();
            ingredient.ingredients.Add(catalog.ingredients[1].id);
            AlbumEntry condiment = Entry();
            condiment.condiments.Add(Content.ChiliOil);
            List<Texture2D> images = new List<Texture2D> { original };
            foreach (AlbumEntry changed in new[] { soup, spice, ingredient, condiment })
            {
                Texture2D image = BowlPortrait.Get(changed);
                foreach (Texture2D previous in images) Assert.AreNotSame(previous, image);
                images.Add(image);
            }
            Assert.AreSame(original, BowlPortrait.Get(baseline));
            yield return null;
        }

        /// <summary>同じ注文オブジェクトでも選択具材が変われば再撮影され、元の内容に戻せば元画像を返します。</summary>
        [UnityTest]
        public IEnumerator MutatingOrderContentsDoesNotReuseStalePortrait()
        {
            CustomerOrder order = Order(Entry());
            Texture2D first = BowlPortrait.Get(order);
            order.selected.Add(catalog.ingredients[1].id);
            Texture2D changed = BowlPortrait.Get(order);

            Assert.AreNotSame(first, changed);
            order.selected.RemoveAt(order.selected.Count - 1);
            Assert.AreSame(first, BowlPortrait.Get(order));
            yield return null;
        }

        /// <summary>具材の並び順と重複は失われず、別の内容キーになります。</summary>
        [UnityTest]
        public IEnumerator IngredientOrderAndDuplicatesRemainDistinctCacheContents()
        {
            AlbumEntry forward = Entry();
            forward.ingredients.Add(catalog.ingredients[1].id);
            AlbumEntry reverse = Entry();
            reverse.ingredients.Insert(0, catalog.ingredients[1].id);
            AlbumEntry duplicate = Entry();
            duplicate.ingredients.Add(duplicate.ingredients[0]);

            Texture2D first = BowlPortrait.Get(forward);
            Texture2D second = BowlPortrait.Get(reverse);
            Texture2D third = BowlPortrait.Get(duplicate);
            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first, third);
            Assert.AreNotSame(second, third);
            yield return null;
        }

        /// <summary>nullの注文・記録と一覧の欠落は、画像取得時に明示的な例外になります。</summary>
        [Test]
        public void NullInputsAndMissingListsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => BowlPortrait.Get((AlbumEntry)null));
            Assert.Throws<ArgumentNullException>(() => BowlPortrait.Get((CustomerOrder)null));
            AlbumEntry entry = Entry();
            entry.ingredients = null;
            Assert.Throws<ArgumentException>(() => BowlPortrait.Get(entry));
            entry = Entry();
            entry.condiments = null;
            Assert.Throws<ArgumentException>(() => BowlPortrait.Get(entry));
            CustomerOrder order = Order(Entry());
            order.selected = null;
            Assert.Throws<ArgumentException>(() => BowlPortrait.Get(order));
            order = Order(Entry());
            order.condiments = null;
            Assert.Throws<ArgumentException>(() => BowlPortrait.Get(order));
        }

        /// <summary>ReleaseAllは所有画像と撮影台を破棄し、同フレームの再取得を旧撮影台の破棄から保護します。</summary>
        [UnityTest]
        public IEnumerator ReleaseAllDestroysOldResourcesWithoutDestroyingImmediateReplacement()
        {
            AlbumEntry entry = Entry();
            Texture2D first = BowlPortrait.Get(entry);
            GameObject firstStudio = GameObject.Find("Bowl Portrait Studio");
            Assert.NotNull(firstStudio);

            BowlPortrait.ReleaseAll();
            Assert.IsFalse(firstStudio.activeSelf);
            Texture2D replacement = BowlPortrait.Get(entry);
            Assert.AreNotSame(first, replacement);
            yield return null;

            Assert.IsTrue(first == null, "旧画像が遅延破棄されること");
            Assert.IsTrue(firstStudio == null, "旧撮影台が遅延破棄されること");
            Assert.IsTrue(replacement != null, "旧撮影台のOnDestroyが新画像を解放しないこと");
            Assert.AreSame(replacement, BowlPortrait.Get(entry));
            BowlPortrait.ReleaseAll();
            BowlPortrait.ReleaseAll();
            yield return null;
            Assert.IsTrue(replacement == null);
            Assert.IsNull(GameObject.Find("Bowl Portrait Studio"));
        }

        /// <summary>24枚の上限では表示中画像と最近取得した画像を保護し、最も古い未使用画像を解放します。</summary>
        [UnityTest]
        public IEnumerator CacheEvictsLeastRecentlyUsedUnprotectedImageAtCapacity24()
        {
            const int capacity = 24;
            Assert.GreaterOrEqual(catalog.ingredients.Length, capacity + 2, "上限を超える異なる有効具材を用意できること");
            List<AlbumEntry> entries = new List<AlbumEntry>();
            List<Texture2D> images = new List<Texture2D>();
            for (int index = 0; index < capacity + 2; index++)
            {
                AlbumEntry entry = Entry();
                entry.ingredients[0] = catalog.ingredients[index].id;
                entries.Add(entry);
                if (index < capacity) images.Add(BowlPortrait.Get(entry));
            }
            GameObject display = Own(new GameObject("Protected Portrait", typeof(RectTransform), typeof(RawImage)));
            RawImage rawImage = display.GetComponent<RawImage>();
            rawImage.texture = images[0];
            Assert.AreSame(images[1], BowlPortrait.Get(entries[1]));
            Texture2D newest = BowlPortrait.Get(entries[capacity]);
            yield return null;

            Assert.IsTrue(images[2] == null, "LRUの未使用画像が破棄されること");
            Assert.IsTrue(images[0] != null, "表示中の最古画像を保護すること");
            Assert.IsTrue(images[1] != null, "再取得した画像を保護すること");
            Assert.IsTrue(newest != null);
            for (int index = 3; index < capacity; index++) Assert.IsTrue(images[index] != null);

            rawImage.enabled = false;
            BowlPortrait.Get(entries[capacity + 1]);
            yield return null;
            Assert.IsTrue(images[0] == null, "無効なRawImageは画像を保護しないこと");
            Assert.IsTrue(images[1] != null);
        }

        /// <summary>撮影成功後は環境光・反射・霧・有効および無効の平行光源と描画先を復元します。</summary>
        [UnityTest]
        public IEnumerator SuccessfulCaptureRestoresLightingFogDirectionalLightsAndActiveRenderTexture()
        {
            ConfigureDistinctEnvironment();
            LightingSnapshot expected = new LightingSnapshot();
            Light enabled = CreateDirectionalLight(true);
            Light disabled = CreateDirectionalLight(false);
            RenderTexture sentinel = CreateActiveRenderTexture();

            Assert.NotNull(BowlPortrait.Get(Entry()));
            expected.AssertRestored();
            Assert.IsTrue(enabled.enabled);
            Assert.IsFalse(disabled.enabled);
            Assert.AreSame(sentinel, RenderTexture.active);
            AssertStudioIdle();
            yield return null;
        }

        /// <summary>無効具材で撮影が失敗しても状態と撮影台を復元し、続く正常な撮影を妨げません。</summary>
        [UnityTest]
        public IEnumerator InvalidIngredientFailureRestoresStateAndAllowsSubsequentCapture()
        {
            ConfigureDistinctEnvironment();
            LightingSnapshot expected = new LightingSnapshot();
            Light enabled = CreateDirectionalLight(true);
            Light disabled = CreateDirectionalLight(false);
            RenderTexture sentinel = CreateActiveRenderTexture();
            AlbumEntry invalid = Entry();
            invalid.ingredients.Add("__missing_portrait_test_ingredient__");

            Assert.Throws<InvalidOperationException>(() => BowlPortrait.Get(invalid));
            expected.AssertRestored();
            Assert.IsTrue(enabled.enabled);
            Assert.IsFalse(disabled.enabled);
            Assert.AreSame(sentinel, RenderTexture.active);
            AssertStudioIdle();
            Assert.Throws<InvalidOperationException>(() => BowlPortrait.Get(invalid), "失敗した画像をキャッシュしないこと");
            Assert.NotNull(BowlPortrait.Get(Entry()), "再入防止フラグを失敗後も解除すること");
            expected.AssertRestored();
            Assert.AreSame(sentinel, RenderTexture.active);
            yield return null;
        }

        /// <summary>板春雨の撮影後も共有透明材のアルファ用と色用のブレンド値を保持します。</summary>
        [UnityTest]
        public IEnumerator TransparentIngredientCaptureRestoresSharedMaterialBlendFactors()
        {
            GameObject parent = Own(new GameObject("Transparent Ingredient Fixture"));
            GameObject piece = IngredientVisuals.Build(catalog.Get("wide_glass_noodle"), parent.transform);
            List<Material> materials = piece.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && material.HasProperty("_Surface") && material.GetFloat("_Surface") > .5f)
                .Distinct().ToList();
            Assert.IsNotEmpty(materials, "透明な共有材を実際に検証すること");
            string[] properties = { "_SrcBlendAlpha", "_DstBlendAlpha", "_SrcBlend", "_DstBlend" };
            List<float[]> before = new List<float[]>();
            foreach (Material material in materials)
            {
                foreach (string property in properties) Assert.IsTrue(material.HasProperty(property));
                before.Add(properties.Select(material.GetFloat).ToArray());
            }
            AlbumEntry entry = Entry();
            entry.ingredients = new List<string> { "wide_glass_noodle" };

            Assert.NotNull(BowlPortrait.Get(entry));
            for (int index = 0; index < materials.Count; index++)
                for (int property = 0; property < properties.Length; property++)
                    Assert.AreEqual(before[index][property], materials[index].GetFloat(properties[property]), properties[property]);
            yield return null;
        }

        /// <summary>既存の六タブを並べ替えず、開始タブ6で実際の一杯アルバムと共有画像を表示します。</summary>
        [UnityTest]
        public IEnumerator CollectionTabSixAppendsAlbumAndDisplaysCachedPortrait()
        {
            GameApplication app = CreateCollectionApplication();
            AlbumEntry entry = Entry();
            entry.day = 3;
            entry.quality = 95;
            entry.customerName = "記録のお客さん";
            app.Simulation.State.album.Add(entry);
            CollectionScreen.StartTab = 6;
            CollectionScreen screen = CreateCollectionScreen(app);
            Transform tabs = screen.Root.GetComponentsInChildren<Transform>(true).Single(value => value.name == "Tabs");
            string[] labels = tabs.GetComponentsInChildren<Button>().Select(button => button.name).ToArray();

            CollectionAssert.AreEqual(new[] { "店の物語", "お客さん図鑑", "食材図鑑", "レシピ帳", "ミッション", "記録", "一杯アルバム", "希少食材", "収集報酬" }, labels);
            RawImage portrait = screen.Root.GetComponentsInChildren<RawImage>().Single(value => value.name == "Bowl Portrait");
            Assert.AreSame(BowlPortrait.Get(entry), portrait.texture);
            Assert.IsFalse(portrait.raycastTarget);
            Assert.IsTrue(screen.Root.GetComponentsInChildren<Text>().Any(text => text.text.Contains("一杯アルバム 1 /")));
            yield return null;
        }

        /// <summary>開始タブ7は希少食材のみを表示し、未入手の名前・実画像・固有説明は公開しません。</summary>
        [UnityTest]
        public IEnumerator RareCollectionTabSevenHidesAllUnownedFoodDetails()
        {
            GameApplication app = CreateCollectionApplication();
            CollectionScreen.StartTab = 7;
            CollectionScreen screen = CreateCollectionScreen(app);
            IngredientDefinition[] rare = catalog.ingredients.Where(item => item.isRare).ToArray();
            Assert.IsNotEmpty(rare);
            Transform[] cards = screen.Root.GetComponentsInChildren<Transform>(true)
                .Where(value => value.name.StartsWith("Food ", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(rare.Length, cards.Length);
            foreach (IngredientDefinition item in rare)
            {
                Assert.IsFalse(app.Simulation.Unlocks.IsUnlocked(item.id), item.id);
                Transform card = cards.Single(value => value.name == "Food " + item.id);
                List<string> labels = card.GetComponentsInChildren<Text>().Select(label => label.text).ToList();
                CollectionAssert.Contains(labels, "？？？");
                CollectionAssert.Contains(labels, "希少食材\n3日目以降の朝市で入手できます");
                Assert.IsFalse(labels.Any(text => text.Contains(item.displayName)), item.id);
                string note = FoodProfile.Of(item).Note;
                if (!string.IsNullOrEmpty(note)) Assert.IsFalse(labels.Any(text => text.Contains(note)), item.id);
                Image icon = card.Find("Icon").GetComponent<Image>();
                Assert.AreSame(TextureFactory.Icon(IconKind.Lock), icon.sprite, "未入手の実画像を描画しない");
                Assert.IsFalse(icon.raycastTarget);
            }
            Assert.IsEmpty(screen.Root.GetComponentsInChildren<RawImage>());
            yield return null;
        }

        /// <summary>開始タブ5は従来の記録のままで、アルバムボタンを押すと空のアルバムへ移ります。</summary>
        [UnityTest]
        public IEnumerator ExistingRecordsTabKeepsIndexFiveAndAlbumButtonOpensEmptyState()
        {
            GameApplication app = CreateCollectionApplication();
            CollectionScreen.StartTab = 5;
            CollectionScreen screen = CreateCollectionScreen(app);
            Assert.IsTrue(screen.Root.GetComponentsInChildren<Text>().Any(text => text.text == "累計提供"));
            Assert.IsFalse(screen.Root.GetComponentsInChildren<Text>().Any(text => text.text.StartsWith("一杯アルバム ")));

            screen.Root.GetComponentsInChildren<Button>().Single(button => button.name == "一杯アルバム").onClick.Invoke();
            yield return null;

            Assert.IsTrue(screen.Root.GetComponentsInChildren<Text>().Any(text => text.text.Contains("まだアルバムは空です")));
            Assert.IsEmpty(screen.Root.GetComponentsInChildren<RawImage>());
        }

        /// <summary>未発見の相方札は組名・相方名・説明を伏せ、発見済みの組だけを公開します。</summary>
        [UnityTest]
        public IEnumerator IngredientPartnerLabelsHideUndiscoveredDetailsAndRevealOnlyDiscoveredPair()
        {
            GameApplication app = CreateCollectionApplication();
            app.Simulation.State.pairings.Clear();
            PairingDefinition known = Pairings.All[0];
            LifetimeStats.Add(app.Simulation.State.pairings, known.Id);
            CollectionScreen.StartTab = 2;
            CollectionScreen screen = CreateCollectionScreen(app);

            IngredientDefinition[] normalIngredients = catalog.ingredients.Where(item => !item.isRare).ToArray();
            Transform[] cards = screen.Root.GetComponentsInChildren<Transform>(true)
                .Where(value => value.name.StartsWith("Food ", StringComparison.Ordinal)).ToArray();
            CollectionAssert.AreEquivalent(normalIngredients.Select(item => "Food " + item.id), cards.Select(value => value.name));
            foreach (IngredientDefinition ingredient in normalIngredients)
            {
                Transform card = cards.Single(value => value.name == "Food " + ingredient.id);
                List<string> text = card.GetComponentsInChildren<Text>().Select(label => label.text).ToList();
                List<PairingDefinition> pairs = Pairings.For(ingredient.id).ToList();
                int hidden = pairs.Count(pair => pair.Id != known.Id);
                Assert.AreEqual(hidden, text.Count(value => value == "？？？（未発見の相方）"), ingredient.id);
                foreach (PairingDefinition pair in pairs)
                {
                    if (pair.Id == known.Id)
                    {
                        string revealed = catalog.Get(pair.PartnerOf(ingredient.id)).displayName + "／" + pair.Name + "\n" + pair.Note;
                        CollectionAssert.Contains(text, revealed);
                    }
                    else
                    {
                        Assert.IsFalse(text.Any(value => value.Contains(pair.Name)), pair.Id);
                        Assert.IsFalse(text.Any(value => value.Contains(pair.Note)), pair.Id);
                        string partner = catalog.Get(pair.PartnerOf(ingredient.id)).displayName + "／";
                        Assert.IsFalse(text.Any(value => value.Contains(partner)), pair.Id);
                    }
                }
                if (pairs.Count == 0) CollectionAssert.Contains(text, "相性の記録なし");
            }
            yield return null;
        }

        /// <summary>有効なカタログ具材を持つ、互いに独立した一杯記録を作ります。</summary>
        private AlbumEntry Entry()
        {
            return new AlbumEntry
            {
                soup = Content.Soups[0].Id,
                spice = 1,
                ingredients = new List<string> { catalog.ingredients[0].id }
            };
        }

        /// <summary>同内容の記録と一覧を共有しない注文を作ります。</summary>
        private static CustomerOrder Order(AlbumEntry entry)
        {
            return new CustomerOrder
            {
                soup = entry.soup,
                chosenSoup = entry.soup,
                chosenSpice = entry.spice,
                selected = new List<string>(entry.ingredients),
                condiments = new List<string>(entry.condiments)
            };
        }

        /// <summary>テストが所有するUnityオブジェクトを終了時の破棄対象にします。</summary>
        private T Own<T>(T value) where T : UnityEngine.Object
        {
            owned.Add(value);
            return value;
        }

        /// <summary>有効と無効を区別できる外部の平行光源を作ります。</summary>
        private Light CreateDirectionalLight(bool enabled)
        {
            GameObject value = Own(new GameObject("Portrait Test Directional Light"));
            Light light = value.AddComponent<Light>();
            light.type = LightType.Directional;
            light.enabled = enabled;
            return light;
        }

        /// <summary>撮影用ではない描画先を明示し、activeの復元先にします。</summary>
        private RenderTexture CreateActiveRenderTexture()
        {
            RenderTexture texture = Own(new RenderTexture(16, 16, 0));
            texture.Create();
            RenderTexture.active = texture;
            return texture;
        }

        /// <summary>撮影スタジオとは異なる環境光と霧を設定します。</summary>
        private static void ConfigureDistinctEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientLight = new Color(.13f, .27f, .49f);
            RenderSettings.ambientIntensity = .47f;
            RenderSettings.reflectionIntensity = .63f;
            RenderSettings.fog = true;
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    probe[channel, coefficient] = (channel * 9 + coefficient + 1) * .013f;
            RenderSettings.ambientProbe = probe;
        }

        /// <summary>成功・失敗後とも撮影モデルと局所照明が外部描画に残らないことを確認します。</summary>
        private static void AssertStudioIdle()
        {
            GameObject studio = GameObject.Find("Bowl Portrait Studio");
            Assert.NotNull(studio);
            Camera camera = studio.GetComponentInChildren<Camera>(true);
            Assert.NotNull(camera);
            Assert.IsFalse(camera.enabled);
            Assert.IsNull(camera.targetTexture);
            Light[] lights = studio.GetComponentsInChildren<Light>(true);
            Assert.AreEqual(3, lights.Length);
            foreach (Light light in lights) Assert.IsFalse(light.enabled);
            foreach (Transform child in studio.transform)
                if (child.name == "Portrait Model") Assert.IsFalse(child.gameObject.activeSelf);
        }

        /// <summary>保存・音声・店内生成を起動せず、図鑑の公開APIに必要な依存だけを供給します。</summary>
        private GameApplication CreateCollectionApplication()
        {
            GameObject host = Own(new GameObject("Collection Application Fixture"));
            // Awakeによる保存先・店内・音声への副作用を避けるため、アプリを有効化しません。
            host.SetActive(false);
            GameApplication app = host.AddComponent<GameApplication>();
            SetApplicationProperty(app, nameof(GameApplication.Catalog), catalog);
            SetApplicationProperty(app, nameof(GameApplication.Simulation), new RestaurantSimulation(catalog));
            UIKit.Font = Resources.Load<Font>("Fonts/NotoSansJP") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            UIKit.ClickSound = null;
            return app;
        }

        /// <summary>実装の公開範囲を変更せず、軽量fixtureの読み取り専用依存を注入します。</summary>
        private static void SetApplicationProperty(GameApplication app, string name, object value)
        {
            PropertyInfo property = typeof(GameApplication).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, name);
            property.SetValue(app, value);
        }

        /// <summary>有効なUI親の下で実際のCollectionScreen.Createを呼び出します。</summary>
        private CollectionScreen CreateCollectionScreen(GameApplication app)
        {
            GameObject root = Own(new GameObject("Collection Screen Fixture", typeof(RectTransform), typeof(Canvas)));
            CollectionScreen screen = new CollectionScreen();
            screen.Create(app, root.GetComponent<RectTransform>());
            return screen;
        }

        /// <summary>撮影が変更する環境状態を保存し、係数を含めて比較・復元します。</summary>
        private sealed class LightingSnapshot
        {
            private readonly AmbientMode mode = RenderSettings.ambientMode;
            private readonly Color light = RenderSettings.ambientLight;
            private readonly float intensity = RenderSettings.ambientIntensity;
            private readonly SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            private readonly float reflection = RenderSettings.reflectionIntensity;
            private readonly bool fog = RenderSettings.fog;

            /// <summary>テスト開始前の環境へ戻します。</summary>
            internal void Restore()
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = light;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.ambientProbe = probe;
                RenderSettings.reflectionIntensity = reflection;
                RenderSettings.fog = fog;
            }

            /// <summary>撮影前の設定と環境光の全27係数が残っていることを保証します。</summary>
            internal void AssertRestored()
            {
                Assert.AreEqual(mode, RenderSettings.ambientMode);
                Assert.AreEqual(light, RenderSettings.ambientLight);
                Assert.AreEqual(intensity, RenderSettings.ambientIntensity);
                Assert.AreEqual(reflection, RenderSettings.reflectionIntensity);
                Assert.AreEqual(fog, RenderSettings.fog);
                SphericalHarmonicsL2 actual = RenderSettings.ambientProbe;
                for (int channel = 0; channel < 3; channel++)
                    for (int coefficient = 0; coefficient < 9; coefficient++)
                        Assert.AreEqual(probe[channel, coefficient], actual[channel, coefficient], .000001f);
            }
        }
    }
}
