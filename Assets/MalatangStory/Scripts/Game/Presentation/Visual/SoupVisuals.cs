using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>外部アセットを使わず、六種のスープ液面と薬味を生成します。</summary>
namespace MalatangStory.Game
{
    /// <summary>有限個の共有テクスチャと、器ごとに所有する結合薬味メッシュを提供します。</summary>
    public static class SoupVisuals
    {
        private const int TextureSize = 256;
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SeasoningMeshes> Seasonings = new Dictionary<string, SeasoningMeshes>(StringComparer.Ordinal);

        /// <summary>薬味注文とは独立した、スープ標準トッピングの共有結合メッシュです。</summary>
        private readonly struct SeasoningMeshes
        {
            internal readonly Mesh Sesame, Chili;
            /// <summary>キャッシュが所有する二つの描画バッチを記録します。</summary>
            internal SeasoningMeshes(Mesh sesame, Mesh chili) { Sesame = sesame; Chili = chili; }
        }

        /// <summary>半径に合わせた液面、縁のメニスカス、油膜と必要な湯気を生成します。</summary>
        public static GameObject CreateSurface(Transform parent, string soupId, int spice, float radius, float height, bool animated = true, bool steam = true)
        {
            SoupAppearanceParameters appearance = SoupAppearance.ForSoup(soupId, spice);
            GameObject surface = StoreBuilder.Part("Soup", MeshFactory.Disc(.5f, 64), SurfaceMaterial(appearance),
                new Vector3(0f, height, 0f), new Vector3(radius * 2f, 1f, radius * 2f), parent, null, false);
            StoreBuilder.Part("Meniscus", MeshFactory.Torus(.495f, .0035f, 64, 6), MeniscusMaterial(appearance),
                Vector3.up * .001f, Vector3.one, surface.transform, null, false);
            SoupSurfaceMotion motion = surface.AddComponent<SoupSurfaceMotion>();
            motion.Initialize(appearance, animated);
            if (steam)
            {
                ParticleSystem vapor = EffectsFactory.Steam(surface.transform, appearance.SteamIntensity);
                motion.SetSteam(vapor);
                vapor.transform.localPosition = Vector3.up * .025f;
                ParticleSystem.ShapeModule shape = vapor.shape; shape.radius = .22f;
            }
            return surface;
        }

        /// <summary>既存液面の種類と辛さを変更時だけ更新します。毎フレームの素材複製を行いません。</summary>
        public static void ConfigureSurface(Transform surface, string soupId, int spice)
        {
            if (surface == null) return;
            SoupSurfaceMotion motion = surface.GetComponent<SoupSurfaceMotion>();
            if (motion != null) motion.Configure(soupId, spice);
        }

        /// <summary>濁り・放射勾配・油膜と油玉を焼き込んだ、最大36枚の共有テクスチャを返します。</summary>
        public static Texture2D Texture(string soupId, int spice)
        {
            SoupAppearanceParameters appearance = SoupAppearance.ForSoup(soupId, spice);
            string key = appearance.SoupId + ":" + appearance.Spice;
            if (Textures.TryGetValue(key, out Texture2D cached) && cached != null) return cached;
            Color[] pixels = GenerateSurfacePixels(appearance.SoupId, appearance.Spice);
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)
            {
                name = "Soup " + key, hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 2
            };
            texture.SetPixels(pixels); texture.Apply(true, true); Textures[key] = texture;
            return texture;
        }

        /// <summary>事前生成に要した時間と、共有テクスチャ・結合メッシュの保持サイズです。</summary>
        public readonly struct PrewarmReport
        {
            public readonly double ElapsedMilliseconds;
            public readonly long TextureBytes, SeasoningMeshBytes, NativeMemoryBytes;
            public long TotalResourceBytes => TextureBytes + SeasoningMeshBytes;

            /// <summary>GPUバッファの計算値とUnityのネイティブメモリ計測値を分けて記録します。</summary>
            internal PrewarmReport(double elapsedMilliseconds, long textureBytes, long seasoningMeshBytes, long nativeMemoryBytes)
            {
                ElapsedMilliseconds = elapsedMilliseconds; TextureBytes = textureBytes;
                SeasoningMeshBytes = seasoningMeshBytes; NativeMemoryBytes = nativeMemoryBytes;
            }
        }

        /// <summary>ロード中に全36種の液面素材と標準トッピングを準備し、処理時間と保持サイズを返します。</summary>
        public static PrewarmReport PrewarmAll()
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            MeshFactory.Disc(.5f, 64);
            MeshFactory.Torus(.495f, .0035f, 64, 6);
            foreach (SoupDefinition soup in Content.Soups)
                for (int spice = 0; spice <= 5; spice++)
                {
                    SoupAppearanceParameters appearance = SoupAppearance.ForSoup(soup.Id, spice);
                    SurfaceMaterial(appearance);
                    MeniscusMaterial(appearance);
                    if (soup.Id == "mala" || soup.Id == "dry") GetSeasoningMeshes(appearance);
                }
            MaterialLibrary.Lit(new Color(.96f, .91f, .74f), .3f);
            MaterialLibrary.Lit(new Color(.38f, .045f, .012f), .28f);
            long textureBytes = 0L, meshBytes = 0L, nativeBytes = 0L;
            foreach (Texture2D texture in Textures.Values)
            {
                int width = texture.width, height = texture.height;
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    textureBytes += (long)width * height * 4L;
                    width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
                }
                nativeBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
            }
            foreach (SeasoningMeshes meshes in Seasonings.Values)
            {
                meshBytes += MeshBufferBytes(meshes.Sesame) + MeshBufferBytes(meshes.Chili);
                nativeBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(meshes.Sesame)
                    + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(meshes.Chili);
            }
            stopwatch.Stop();
            return new PrewarmReport(stopwatch.Elapsed.TotalMilliseconds, textureBytes, meshBytes, nativeBytes);
        }

        /// <summary>CPU読み取りを再有効化せず、メッシュの頂点・インデックスバッファ長を集計します。</summary>
        private static long MeshBufferBytes(Mesh mesh)
        {
            long bytes = 0L;
            for (int stream = 0; stream < mesh.vertexBufferCount; stream++)
                bytes += (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream);
            int indexBytes = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                bytes += (long)mesh.GetIndexCount(submesh) * indexBytes;
            return bytes;
        }

        /// <summary>256角の液面を基底、油玉、反射の順に生成します。配列は呼び出し元が所有します。</summary>
        public static Color[] GenerateSurfacePixels(string soupId, int spice)
        {
            SoupAppearanceParameters appearance = SoupAppearance.ForSoup(soupId, spice);
            SoupDefinition soup = Content.Soup(appearance.SoupId);
            bool mala = appearance.SoupId == "mala";
            Color broth = mala ? new Color(.5f, .29f, .11f) : new Color(soup.R, soup.G, soup.B);
            Color cream = new Color(.96f, .89f, .7f);
            Color filmTint = mala
                ? Color.Lerp(new Color(.83f, .32f, .075f), new Color(.79f, .22f, .04f), appearance.Spice / 5f)
                : Color.Lerp(new Color(.82f, .48f, .08f), new Color(.7f, .16f, .025f), appearance.Spice / 5f);
            Color[] pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = x / (float)(TextureSize - 1), v = y / (float)(TextureSize - 1);
                    float px = u * 2f - 1f, pz = v * 2f - 1f;
                    float distance = Mathf.Sqrt(px * px + pz * pz);
                    float fine = Mathf.PerlinNoise(u * 48f + 4f, v * 48f + 21f);
                    float detail = Mathf.PerlinNoise(u * 103f + 19f, v * 103f + 7f);
                    float cloud = (fine - .5f) * .045f + (detail - .5f) * .025f;
                    Color color = broth * (1.02f + cloud);
                    color = Color.Lerp(color, cream, appearance.Cloudiness * (.055f + fine * .035f));
                    float filmStrength = mala ? .68f + appearance.OilCoverage * .24f : appearance.OilCoverage * .7f;
                    // 低周波を閾値で二色に分けると迷彩状になるため、油膜は細かな連続濃淡に留める。
                    float clearPocket = mala ? SmoothThreshold(.58f, .84f, fine) * .28f : 0f;
                    float film = Mathf.Clamp01(filmStrength + (detail - .5f) * .055f - clearPocket);
                    color = Color.Lerp(color, filmTint, film);
                    // 中心から広く暗くすると油膜が大きな茶色領域に見えるため、深さの陰影は外周へ寄せる。
                    color *= 1f - .24f * SmoothThreshold(.64f, 1f, distance);
                    color.a = 1f;
                    pixels[y * TextureSize + x] = color;
                }
            PaintOilDrops(pixels, appearance);
            PaintReflections(pixels, broth, cream);
            return pixels;
        }

        /// <summary>各油玉の外接矩形だけを走査し、全画素と全粒の積に比例する処理を避けます。</summary>
        private static void PaintOilDrops(Color[] pixels, SoupAppearanceParameters appearance)
        {
            System.Random random = new System.Random(CustomerDirector.StableHash(appearance.SoupId));
            Color oilBody = Color.Lerp(new Color(.64f, .31f, .025f), new Color(.52f, .055f, .008f), appearance.Spice / 5f);
            Color oilRim = new Color(.98f, .68f, .18f);
            bool mala = appearance.SoupId == "mala";
            for (int index = 0; index < appearance.OilDrops; index++)
            {
                GarnishPlacement placement = SoupAppearance.Scatter(random, .84f);
                float variation = (float)random.NextDouble();
                float radius = mala
                    ? (index % 9 == 0 ? .035f + variation * .014f : .009f + variation * .017f)
                    : (index % 5 == 0 ? .072f + variation * .022f : .014f + variation * .03f);
                radius *= 1f + appearance.Spice * .025f;
                PaintOilDrop(pixels, placement.X, placement.Z, radius, oilBody, oilRim);
            }
        }

        /// <summary>濃い中心、明るい輪、偏った小反射を持つ一粒を合成します。</summary>
        private static void PaintOilDrop(Color[] pixels, float centerX, float centerZ, float radius, Color body, Color rimColor)
        {
            float extent = radius * 1.03f;
            int left = PixelCoordinate(centerX - extent), right = PixelCoordinate(centerX + extent);
            int bottom = PixelCoordinate(centerZ - extent), top = PixelCoordinate(centerZ + extent);
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    float dx = (x / (float)(TextureSize - 1) * 2f - 1f - centerX) / radius;
                    float dz = (y / (float)(TextureSize - 1) * 2f - 1f - centerZ) / radius;
                    float q = dx * dx + dz * dz;
                    if (q > 1.06f) continue;
                    float opacity = 1f - SmoothThreshold(.84f, 1.06f, q);
                    float rim = SmoothThreshold(.42f, .72f, q) * (1f - SmoothThreshold(.88f, 1.05f, q));
                    Color oil = Color.Lerp(body, rimColor, rim * .82f);
                    float gx = dx + .28f, gz = dz + .32f;
                    float glint = Mathf.Exp(-(gx * gx + gz * gz) / .045f);
                    oil = Color.Lerp(oil, new Color(1f, .84f, .44f), glint * .48f);
                    int offset = y * TextureSize + x;
                    pixels[offset] = Color.Lerp(pixels[offset], oil, opacity * .92f);
                }
        }

        /// <summary>正規化した液面座標を安全な画素位置へ丸めます。</summary>
        private static int PixelCoordinate(float coordinate)
            => Mathf.Clamp(Mathf.RoundToInt((coordinate + 1f) * .5f * (TextureSize - 1)), 0, TextureSize - 1);

        /// <summary>油玉の上に細い反射光筋と内外のメニスカスを重ねます。</summary>
        private static void PaintReflections(Color[] pixels, Color broth, Color cream)
        {
            Color reflection = new Color(1f, .9f, .63f);
            Color innerColor = Color.Lerp(broth, cream, .35f);
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    float px = x / (float)(TextureSize - 1) * 2f - 1f, pz = y / (float)(TextureSize - 1) * 2f - 1f;
                    float distance = Mathf.Sqrt(px * px + pz * pz);
                    float line = (px + pz * .48f + .26f) / .025f, length = (pz - .12f) / .48f;
                    float lengthSquared = length * length;
                    float streak = Mathf.Exp(-line * line - lengthSquared * lengthSquared)
                        * (1f - SmoothThreshold(.72f, .94f, distance));
                    int offset = y * TextureSize + x;
                    Color color = Color.Lerp(pixels[offset], reflection, streak * .32f);
                    float inner = (distance - .943f) / .012f, outer = (distance - .976f) / .012f;
                    color = Color.Lerp(color, innerColor, Mathf.Exp(-inner * inner) * .18f);
                    color = Color.Lerp(color, broth * .42f, Mathf.Exp(-outer * outer) * .8f);
                    color.r = Mathf.Clamp01(color.r); color.g = Mathf.Clamp01(color.g); color.b = Mathf.Clamp01(color.b);
                    color.a = 1f;
                    pixels[offset] = color;
                }
        }

        /// <summary>境界区間を0〜1に正規化してから三次の滑らかな閾値を評価します。</summary>
        private static float SmoothThreshold(float lower, float upper, float value)
            => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lower, upper, value));

        /// <summary>白飛びしにくい艶に制限した共有液面素材を返します。</summary>
        internal static Material SurfaceMaterial(SoupAppearanceParameters appearance)
            => MaterialLibrary.Textured(Texture(appearance.SoupId, appearance.Spice), Color.white, appearance.Smoothness);

        /// <summary>液面の外縁を暗く締める共有メニスカス素材を返します。</summary>
        internal static Material MeniscusMaterial(SoupAppearanceParameters appearance)
        {
            SoupDefinition soup = Content.Soup(appearance.SoupId);
            Color baseColor = appearance.SoupId == "mala" ? new Color(.5f, .29f, .11f) : new Color(soup.R, soup.G, soup.B);
            return MaterialLibrary.Lit(new Color(baseColor.r * .42f, baseColor.g * .42f, baseColor.b * .42f), .46f);
        }

        /// <summary>薬味指定とは別の標準トッピングを、スープ変更時だけ取り替えます。</summary>
        internal static void ConfigureSeasoning(Transform surface, SoupAppearanceParameters appearance)
        {
            Transform previous = surface.Find("Soup Seasoning");
            if (previous != null)
            {
                previous.name = "Retired Soup Seasoning";
                previous.gameObject.SetActive(false);
                DestroyCached(previous.gameObject);
            }
            if (appearance.SoupId != "mala" && appearance.SoupId != "dry") return;
            SeasoningMeshes meshes = GetSeasoningMeshes(appearance);
            Transform root = new GameObject("Soup Seasoning").transform;
            root.SetParent(surface, false);
            StoreBuilder.Part("Sesame", meshes.Sesame, MaterialLibrary.Lit(new Color(.96f, .91f, .74f), .3f),
                Vector3.zero, Vector3.one, root, null, false);
            StoreBuilder.Part("Chili Flakes", meshes.Chili, MaterialLibrary.Lit(new Color(.38f, .045f, .012f), .28f),
                Vector3.zero, Vector3.one, root, null, false);
        }

        /// <summary>ロードと実描画で共通のキーを使い、標準トッピングを一度だけ結合します。</summary>
        private static SeasoningMeshes GetSeasoningMeshes(SoupAppearanceParameters appearance)
        {
            string key = appearance.SoupId + ":" + appearance.Spice;
            if (Seasonings.TryGetValue(key, out SeasoningMeshes meshes)) return meshes;
            meshes = new SeasoningMeshes(BuildSeasoningMesh(appearance, true), BuildSeasoningMesh(appearance, false));
            Seasonings[key] = meshes;
            return meshes;
        }

        /// <summary>白ごまか唐辛子片を固定シードで散らし、一つの共有メッシュに結合します。</summary>
        private static Mesh BuildSeasoningMesh(SoupAppearanceParameters appearance, bool sesame)
        {
            int count = sesame ? (appearance.SoupId == "mala" ? 32 : 24) : 8 + appearance.Spice * 4;
            System.Random random = new System.Random(CustomerDirector.StableHash(appearance.SoupId + (sesame ? ":sesame" : ":flakes")));
            Mesh piece = sesame ? MeshFactory.Sphere(4, 6) : MeshFactory.RoundedBox(Vector3.one, .08f, 1);
            Vector3 dimensions = sesame ? new Vector3(.011f, .0028f, .0048f) : new Vector3(.014f, .0018f, .008f);
            CombineInstance[] pieces = new CombineInstance[count];
            for (int index = 0; index < count; index++)
            {
                GarnishPlacement placement = SoupAppearance.Scatter(random, .45f);
                Vector3 position = new Vector3(placement.X, .005f + index * .00001f, placement.Z);
                Quaternion rotation = Quaternion.Euler(sesame ? 0f : 8f, placement.Angle, sesame ? 0f : 12f);
                pieces[index] = new CombineInstance
                {
                    mesh = piece,
                    transform = Matrix4x4.TRS(position, rotation, dimensions * placement.Scale)
                };
            }
            Mesh combined = new Mesh { name = appearance.SoupId + (sesame ? " Sesame" : " Chili Flakes"), hideFlags = HideFlags.DontSave };
            combined.CombineMeshes(pieces, true, true);
            combined.UploadMeshData(true);
            return combined;
        }

        /// <summary>定義色と形状に基づき、薬味一種類につき一つの描画バッチへ結合します。</summary>
        public static Transform BuildGarnishes(Transform parent, List<string> condiments, int spice, int seed, float radius = .38f)
        {
            if (condiments == null) throw new ArgumentNullException(nameof(condiments));
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius));
            Transform root = new GameObject("Garnishes").transform;
            root.SetParent(parent, false);
            System.Random random = new System.Random(seed);
            using (CityGeometry geometry = new CityGeometry())
            {
                foreach (string id in condiments)
                {
                    // ラー油は辛さへ既に加算されるため、薬味リストから重ねて二重描画しない。
                    if (id == Content.ChiliOil) continue;
                    AddGarnish(geometry, id, random, radius);
                }
                int oilCount = Math.Max(0, Math.Min(5, spice)) * 5;
                Material oil = MaterialLibrary.Lit(new Color(.72f, .16f, .025f), .65f);
                for (int index = 0; index < oilCount; index++)
                {
                    GarnishPlacement placement = SoupAppearance.ScatterGarnish(random, radius, SoupAppearance.ForGarnish(Content.ChiliOil));
                    geometry.Add(MeshFactory.Sphere(5, 8), new Vector3(placement.X, .003f, placement.Z),
                        new Vector3(.024f, .003f, .019f) * placement.Scale, Quaternion.Euler(0f, placement.Angle, 0f), oil);
                }
                geometry.Flush(root, "Floating Garnish", false);
            }
            return root;
        }

        /// <summary>ねぎの輪、香菜の葉、砕いた粒、液体の帯をそれぞれ専用形状で追加します。</summary>
        private static void AddGarnish(CityGeometry geometry, string id, System.Random random, float radius)
        {
            if (!Content.TryCondiment(id, out CondimentDefinition definition)) return;
            GarnishAppearanceParameters appearance = SoupAppearance.ForGarnish(id);
            Color tint = new Color(definition.R, definition.G, definition.B);
            bool liquid = appearance.Shape == GarnishShape.Sauce || appearance.Shape == GarnishShape.Vinegar;
            Material material = liquid && appearance.Shape == GarnishShape.Vinegar
                ? MaterialLibrary.Transparent(new Color(tint.r, tint.g, tint.b, .5f), .58f)
                : MaterialLibrary.Lit(tint, liquid ? .5f : .28f);
            for (int index = 0; index < appearance.Count; index++)
            {
                GarnishPlacement p = SoupAppearance.ScatterGarnish(random, radius * (liquid ? .9f : 1f), appearance);
                Vector3 position = new Vector3(p.X, .006f + index * .00004f, p.Z);
                Quaternion rotation = Quaternion.Euler(0f, p.Angle, 0f);
                float size = appearance.Size * p.Scale;
                switch (appearance.Shape)
                {
                    case GarnishShape.Scallion:
                        geometry.Add(MeshFactory.Torus(.65f, .23f, 9, 4), position, new Vector3(size, size * .65f, size), rotation, material);
                        break;
                    case GarnishShape.Coriander:
                        for (int leaf = 0; leaf < 3; leaf++)
                            geometry.Add(MeshFactory.Leaf(.75f, 1.25f, .07f), position, Vector3.one * size,
                                rotation * Quaternion.Euler(0f, leaf * 110f, 0f), material);
                        break;
                    case GarnishShape.Sauce:
                    case GarnishShape.Vinegar:
                        geometry.Add(MeshFactory.Disc(1f, 20), position, new Vector3(size * 1.8f, 1f, size * .45f), rotation, material);
                        break;
                    case GarnishShape.Garlic:
                    case GarnishShape.Peanut:
                        geometry.Add(MeshFactory.RoundedBox(Vector3.one, .12f, 1), position,
                            new Vector3(size, size * .5f, size * .7f), rotation * Quaternion.Euler(8f, 0f, 12f), material);
                        break;
                    default:
                        geometry.Add(MeshFactory.Sphere(5, 7), position, Vector3.one * size, rotation, material);
                        break;
                }
            }
        }

        /// <summary>実行モードに応じて所有オブジェクトの破棄方法を切り替えます。</summary>
        private static void DestroyCached(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        /// <summary>液面テクスチャと標準トッピングの共有メッシュを、描画利用終了後に解放します。</summary>
        public static void ReleaseAll()
        {
            foreach (Texture2D texture in Textures.Values) DestroyCached(texture);
            Textures.Clear();
            foreach (SeasoningMeshes meshes in Seasonings.Values)
            {
                DestroyCached(meshes.Sesame);
                DestroyCached(meshes.Chili);
            }
            Seasonings.Clear();
        }
    }
}
