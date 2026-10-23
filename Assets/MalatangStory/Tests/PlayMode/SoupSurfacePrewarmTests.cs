using System.Collections;
using System.Collections.Generic;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>液面の細かな油膜とロード時事前生成の回帰を検証します。</summary>
namespace MalatangStory.Tests
{
    /// <summary>大きな二色斑点の再発と、注文操作での共有資源の追加生成を防ぎます。</summary>
    public sealed class SoupSurfacePrewarmTests
    {
        private GameObject host;

        /// <summary>共有キャッシュを空にして、事前生成を冷状態から検証します。</summary>
        [SetUp]
        public void SetUp()
        {
            MaterialLibrary.ReleaseAll();
            SoupVisuals.ReleaseAll();
        }

        /// <summary>利用者の破棄後に共有資源を解放し、遅延破棄を完了させます。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (host != null)
            {
                host.SetActive(false);
                Object.Destroy(host);
            }
            yield return null;
            MaterialLibrary.ReleaseAll();
            SoupVisuals.ReleaseAll();
            yield return null;
        }

        /// <summary>全36種を準備しても16MB以内に収まり、全設定の描画と再準備で資源数が増えません。</summary>
        [Test]
        public void PrewarmCoversEverySurfaceAndSeasoningWithinMemoryBudget()
        {
            SoupVisuals.PrewarmReport report = SoupVisuals.PrewarmAll();
            Assert.AreEqual(12582864L, report.TextureBytes, "256角RGBA32の全ミップ36枚");
            Assert.Greater(report.SeasoningMeshBytes, 0L);
            Assert.Less(report.TotalResourceBytes, 16000000L);
            Assert.GreaterOrEqual(report.ElapsedMilliseconds, 0d);
            HashSet<int> textures = ResourceIds<Texture2D>();
            HashSet<int> meshes = ResourceIds<Mesh>();
            Assert.AreEqual(36, textures.Count);
            Assert.AreEqual(24, meshes.Count);
            host = new GameObject("Prewarmed Soup Surfaces");
            foreach (SoupDefinition soup in Content.Soups)
                for (int spice = 0; spice <= 5; spice++)
                {
                    Texture2D texture = SoupVisuals.Texture(soup.Id, spice);
                    Assert.IsTrue(textures.Contains(texture.GetInstanceID()));
                    Assert.IsFalse(texture.isReadable, "CPU画素コピーを保持しない");
                    GameObject surface = SoupVisuals.CreateSurface(host.transform, soup.Id, spice, .44f, .2f, false, false);
                    foreach (MeshFilter filter in surface.GetComponentsInChildren<MeshFilter>())
                    {
                        if (filter.sharedMesh.name.StartsWith("MeshFactory.")) continue;
                        Assert.IsTrue(meshes.Contains(filter.sharedMesh.GetInstanceID()));
                        Assert.IsFalse(filter.sharedMesh.isReadable, "結合メッシュのCPUコピーを保持しない");
                    }
                }
            SoupVisuals.PrewarmReport repeat = SoupVisuals.PrewarmAll();
            Assert.AreEqual(report.TotalResourceBytes, repeat.TotalResourceBytes);
            CollectionAssert.AreEquivalent(textures, ResourceIds<Texture2D>());
            CollectionAssert.AreEquivalent(meshes, ResourceIds<Mesh>());
        }

        /// <summary>麻辣の局所平均色は大きく二色に分離せず、細かな油玉が十分に存在します。</summary>
        [TestCase(0)]
        [TestCase(3)]
        [TestCase(5)]
        public void MalaHasFineFilmInsteadOfLargeCamouflagePatches(int spice)
        {
            Color[] pixels = SoupVisuals.GenerateSurfacePixels("mala", spice);
            float minRed = 1f, maxRed = 0f, minGreen = 1f, maxGreen = 0f;
            for (int row = 72; row <= 192; row += 40)
                for (int column = 72; column <= 192; column += 40)
                {
                    Color average = PatchAverage(pixels, column, row);
                    minRed = Mathf.Min(minRed, average.r); maxRed = Mathf.Max(maxRed, average.r);
                    minGreen = Mathf.Min(minGreen, average.g); maxGreen = Mathf.Max(maxGreen, average.g);
                }
            Assert.Less(maxRed - minRed, .1f, "中心付近の大きな茶赤斑点を抑える");
            Assert.Less(maxGreen - minGreen, .065f, "膜の低周波コントラストを抑える");
            Assert.GreaterOrEqual(SoupAppearance.ForSoup("mala", spice).OilDrops, 100);
            CollectionAssert.AreEqual(pixels, SoupVisuals.GenerateSurfacePixels("mala", spice));
        }

        /// <summary>辛さの両端で、複数の細粒油玉が実画素上の暗い中心と明るい輪を持ちます。</summary>
        [TestCase(0)]
        [TestCase(5)]
        public void MalaPixelsContainNumerousFineDropsWithDarkCentersAndBrightRims(int spice)
        {
            Color[] pixels = SoupVisuals.GenerateSurfacePixels("mala", spice);
            List<OilDroplet> drops = MalaDropLayout(spice);
            int visibleFineDrops = 0;
            for (int index = 0; index < drops.Count; index++)
            {
                OilDroplet drop = drops[index];
                if (drop.Radius < .025f || drop.Radius > .065f || !IsIsolatedDrop(drops, index)) continue;
                if (drop.X * drop.X + drop.Z * drop.Z > .93f * .93f) continue;
                Color center = DropRegionAverage(pixels, drop, 0f, .18f);
                Color rim = DropRegionAverage(pixels, drop, .55f, .88f);
                float centerValue = Luminance(center), rimValue = Luminance(rim);
                if (center.r - center.g > .2f && rimValue - centerValue > .1f && rim.g - center.g > .1f)
                    visibleFineDrops++;
            }
            Assert.GreaterOrEqual(visibleFineDrops, 5,
                "大小が混在する独立した楕円の油玉を5粒以上、中心より輝度・緑成分が0.1超明るい輪で確認する");
        }

        /// <summary>ゲーム乱数を変更せず、実画素を調べる対象油玉の中心と半径を再現します。</summary>
        private static List<OilDroplet> MalaDropLayout(int spice)
        {
            System.Random random = new System.Random(CustomerDirector.StableHash("mala"));
            List<OilDroplet> drops = new List<OilDroplet>();
            int count = SoupAppearance.ForSoup("mala", spice).OilDrops;
            OilDroplet previous = default;
            for (int index = 0; index < count; index++)
            {
                previous = OilDropletRules.Next(random, true, spice, previous, index > 0);
                drops.Add(previous);
            }
            return drops;
        }

        /// <summary>後から描かれる粒による上書きを含め、油玉同士の輪が重ならない対象だけを選びます。</summary>
        private static bool IsIsolatedDrop(List<OilDroplet> drops, int index)
        {
            OilDroplet target = drops[index];
            for (int other = 0; other < drops.Count; other++)
            {
                if (other == index) continue;
                float dx = target.X - drops[other].X, dz = target.Z - drops[other].Z;
                float separation = (target.Radius * Mathf.Max(target.Aspect, 1 / target.Aspect) + drops[other].Radius * Mathf.Max(drops[other].Aspect, 1 / drops[other].Aspect)) * 1.05f;
                if (dx * dx + dz * dz < separation * separation) return false;
            }
            return true;
        }

        /// <summary>実画素の中心領域または環状領域を平均し、画素丸めに依存しない濃淡を取得します。</summary>
        private static Color DropRegionAverage(Color[] pixels, OilDroplet drop, float lowerSquaredRadius, float upperSquaredRadius)
        {
            int left = Mathf.Max(0, Mathf.FloorToInt((drop.X - drop.Radius * 1.6f + 1f) * .5f * 255f));
            int right = Mathf.Min(255, Mathf.CeilToInt((drop.X + drop.Radius * 1.6f + 1f) * .5f * 255f));
            int bottom = Mathf.Max(0, Mathf.FloorToInt((drop.Z - drop.Radius * 1.6f + 1f) * .5f * 255f));
            int top = Mathf.Min(255, Mathf.CeilToInt((drop.Z + drop.Radius * 1.6f + 1f) * .5f * 255f));
            Color sum = Color.clear;
            int count = 0;
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    float dx = (x / 255f * 2f - 1f - drop.X) / drop.Radius;
                    float dz = (y / 255f * 2f - 1f - drop.Z) / drop.Radius;
                    float ex = (dx * Mathf.Cos(drop.Angle) + dz * Mathf.Sin(drop.Angle)) / drop.Aspect;
                    float ez = (-dx * Mathf.Sin(drop.Angle) + dz * Mathf.Cos(drop.Angle)) * drop.Aspect;
                    float squaredRadius = ex * ex + ez * ez;
                    if (squaredRadius < lowerSquaredRadius || squaredRadius > upperSquaredRadius) continue;
                    sum += pixels[y * 256 + x];
                    count++;
                }
            Assert.Greater(count, 0, "中心と輪はそれぞれ実画素を含む寸法に限定する");
            return sum / count;
        }

        /// <summary>画素の相対輝度を返し、色相だけの違いを明るい輪と誤認しないようにします。</summary>
        private static float Luminance(Color color) => color.r * .2126f + color.g * .7152f + color.b * .0722f;

        /// <summary>スープキャッシュが命名する資源だけのインスタンス集合を返します。</summary>
        private static HashSet<int> ResourceIds<T>() where T : Object
        {
            HashSet<int> ids = new HashSet<int>();
            foreach (T resource in Resources.FindObjectsOfTypeAll<T>())
                if (resource.name.StartsWith("Soup ") || resource.name == "mala Sesame" || resource.name == "dry Sesame"
                    || resource.name == "mala Chili Flakes" || resource.name == "dry Chili Flakes")
                    ids.Add(resource.GetInstanceID());
            return ids;
        }

        /// <summary>油玉と細い反射の画素差を平均し、低周波の色差を抽出します。</summary>
        private static Color PatchAverage(Color[] pixels, int column, int row)
        {
            Color sum = Color.clear;
            for (int y = row - 10; y < row + 10; y++)
                for (int x = column - 10; x < column + 10; x++)
                    sum += pixels[y * 256 + x];
            return sum / 400f;
        }
    }
}
