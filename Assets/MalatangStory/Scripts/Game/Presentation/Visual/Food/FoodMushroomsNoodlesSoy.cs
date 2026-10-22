using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>きのこ・戻した麺・大豆製品23種を低ポリゴンの共有モデルとして生成します。</summary>
    public sealed class FoodMushroomsNoodlesSoy : IFoodBuilder
    {
        private static readonly string[] Ids =
        {
            "enoki", "king_oyster", "shimeji", "maitake", "black_wood_ear", "brown_enoki", "white_wood_ear",
            "sweet_potato_noodle", "shirataki", "corn_noodle", "mung_bean_noodle", "chinese_noodle",
            "knife_cut_noodle", "wide_glass_noodle", "rice_noodle", "dried_tofu_strips", "silken_tofu",
            "atsuage", "yuba", "soy_sheet", "fuzhu", "firm_tofu", "aburaage"
        };

        /// <summary>反射を使わずプレイ開始前に担当IDを登録します。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
            FoodRegistry.Register(new FoodMushroomsNoodlesSoy(), Ids);
        }

        /// <summary>対応IDを組み立て、未対応IDにはnullを返します。</summary>
        public FoodModel Build(string id)
        {
            if (Array.IndexOf(Ids, id) < 0) return null;
            using (FoodModelBuilder model = new FoodModelBuilder())
            {
                switch (id)
                {
                    case "enoki": case "brown_enoki": case "shimeji": Cluster(model, id); break;
                    case "king_oyster": KingOyster(model, id); break;
                    case "maitake": case "black_wood_ear": case "white_wood_ear": Frills(model, id); break;
                    case "sweet_potato_noodle": case "shirataki": case "corn_noodle":
                    case "mung_bean_noodle": case "chinese_noodle": case "rice_noodle": Noodles(model, id); break;
                    case "knife_cut_noodle": case "wide_glass_noodle": RibbonNoodles(model, id); break;
                    case "silken_tofu": case "firm_tofu": case "atsuage": case "aburaage": Tofu(model, id); break;
                    case "dried_tofu_strips": TofuStrips(model, id); break;
                    case "yuba": Yuba(model, id); break;
                    case "soy_sheet": case "fuzhu": RolledSoy(model, id); break;
                }
                return model.Finish(id);
            }
        }

        /// <summary>原色をテクスチャに保存し、加熱による暗色化・湿潤・透過率を設定します。</summary>
        private static void Surface(FoodModelBuilder model, string id, int group, uint dark, uint light,
            FoodPattern pattern, bool mushroom = false, bool transparent = false, float rawAlpha = .84f,
            float cookedAlpha = .72f, float gloss = .35f)
        {
            Color cooked = mushroom ? new Color(.78f, .72f, .61f, 1f) : new Color(.97f, .94f, .83f, 1f);
            Color over = mushroom ? new Color(.53f, .46f, .34f, 1f) : new Color(.77f, .69f, .50f, 1f);
            if (transparent) { cooked.a = cookedAlpha; over.a = Mathf.Min(.94f, cookedAlpha + .10f); }
            model.SetSurface(group, FoodTextures.Create(id + "/" + group, FoodTextures.Rgb(dark), FoodTextures.Rgb(light), pattern),
                new FoodSurface(cooked, over, gloss, .78f, transparent ? rawAlpha : 1f, .62f), transparent);
        }

        /// <summary>株元を共有する曲がった軸と、丸みのある小傘を密集させます。</summary>
        private static void Cluster(FoodModelBuilder model, string id)
        {
            bool shimeji = id == "shimeji"; bool brown = id == "brown_enoki";
            Surface(model, id, 0, brown ? 0xBDAB89u : 0xD8D6BBu, 0xFFFFEF, FoodPattern.Fibers, true);
            Surface(model, id, 1, shimeji ? 0x706354u : brown ? 0x8B572Cu : 0xDFDCC0u,
                shimeji ? 0xBCAB8Du : brown ? 0xCBA46Au : 0xFFFFEEu, FoodPattern.Mottled, true, gloss: .42f);
            int count = shimeji ? 10 : 18;
            model.Add(0, FoodKit.Ellipsoid(21, .055f, 8, 4), new Vector3(0f, -.065f, 0f),
                new Vector3(shimeji ? .074f : .059f, .032f, .056f));
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.39996f; float radius = .012f + .022f * Mathf.Sqrt((i + .5f) / count);
                Vector3 tip = new Vector3(Mathf.Cos(angle) * radius, (shimeji ? .039f : .072f) + .012f * Mathf.Sin(i * 1.9f), Mathf.Sin(angle) * radius);
                Vector3 start = new Vector3(tip.x * .45f, -.063f, tip.z * .45f);
                Vector3 middle = Vector3.Lerp(start, tip, .52f) + new Vector3(.005f * Mathf.Sin(i), 0f, .003f);
                model.Add(0, FoodKit.Tube(new Vector3[] { start, middle, tip }, shimeji ? .0055f : .0028f,
                    shimeji ? .004f : .0021f, shimeji ? 5 : 4), Vector3.zero, Vector3.one);
                float cap = shimeji ? .017f + .002f * (i % 3) : .006f + .001f * (i % 3);
                model.Add(1, FoodKit.Lathe(new Vector2[] { new Vector2(0f, -.002f), new Vector2(cap, 0f),
                    new Vector2(cap * .77f, cap * .65f), new Vector2(0f, cap * .86f) }, shimeji ? 8 : 6), tip, Vector3.one,
                    new Vector3(8f * Mathf.Sin(i), 0f, 8f * Mathf.Cos(i)));
            }
        }

        /// <summary>エリンギの太い繊維質の軸と平たい褐色の傘・淡い傘裏を分離します。</summary>
        private static void KingOyster(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xCFCAAF, 0xFFF7DE, FoodPattern.Fibers, true);
            Surface(model, id, 1, 0x715039, 0xB39367, FoodPattern.Mottled, true);
            Surface(model, id, 2, 0xC3B599, 0xF0DFC1, FoodPattern.Fibers, true);
            for (int i = 0; i < 2; i++)
            {
                Vector3 origin = new Vector3(i == 0 ? -.025f : .028f, i == 0 ? -.072f : -.050f, i * .020f - .01f);
                model.Add(0, FoodKit.Lathe(new Vector2[] { new Vector2(0f, 0f), new Vector2(.021f, .003f),
                    new Vector2(.025f, .032f), new Vector2(.018f, .102f), new Vector2(.013f, .125f), new Vector2(0f, .126f) }, 10),
                    origin, Vector3.one, new Vector3(0f, 0f, i == 0 ? 9f : -12f));
                Vector3 top = origin + new Vector3(i == 0 ? -.018f : .025f, .122f, 0f);
                model.Add(1, FoodKit.Lathe(new Vector2[] { new Vector2(.037f, 0f), new Vector2(.034f, .009f),
                    new Vector2(.023f, .014f), new Vector2(0f, .017f) }, 12), top, Vector3.one);
                model.Add(2, FoodKit.Lathe(new Vector2[] { new Vector2(0f, -.005f), new Vector2(.023f, -.003f),
                    new Vector2(.037f, 0f) }, 12), top, Vector3.one);
            }
        }

        /// <summary>まいたけの扇形傘と、黒白のキクラゲの耳状・花状薄片を重ねます。</summary>
        private static void Frills(FoodModelBuilder model, string id)
        {
            bool maitake = id == "maitake"; bool white = id == "white_wood_ear";
            Surface(model, id, 0, maitake ? 0x73634Cu : white ? 0xDDD7AAu : 0x23150Fu,
                maitake ? 0xC4B18Fu : white ? 0xFFF6DCu : 0x685041u, FoodPattern.Fibers,
                true, !maitake, white ? .76f : .91f, white ? .65f : .84f, .44f);
            if (maitake) Surface(model, id, 1, 0xC0B59A, 0xEADCC0, FoodPattern.Fibers, true);
            int count = maitake ? 10 : white ? 9 : 6;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 137.5f;
                Vector3 origin = new Vector3(.018f * Mathf.Sin(i * 2.4f), -.019f + (i / 3) * .016f, .017f * Mathf.Cos(i * 2.4f));
                model.Add(0, Fan(maitake ? .058f : .064f, maitake ? 2.8f : 4.8f,
                    maitake ? .009f : .020f, i), origin, Vector3.one, new Vector3(maitake ? -12f : -28f + i * 8f, angle, 0f));
                if (maitake)
                    model.Add(1, FoodKit.Tube(new Vector3[] { new Vector3(0f, -.045f, 0f), origin + new Vector3(0f, 0f, .012f) },
                        .004f, .002f, 4), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>根元がすぼまり外周が波打つ扇形の薄膜を生成します。</summary>
        private static Mesh Fan(float radius, float spread, float curl, int phase)
        {
            const int rows = 4; const int columns = 12;
            List<Vector3> vertices = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> triangles = new List<int>();
            for (int r = 0; r <= rows; r++) for (int c = 0; c <= columns; c++)
            {
                float s = (float)r / rows; float u = (float)c / columns; float angle = (u - .5f) * spread;
                float radial = radius * (.10f + .90f * s) * (1f + .10f * s * Mathf.Sin(u * 31f + phase));
                float lift = curl * s * s * (.6f + Mathf.Sin(u * 29f + phase)) + .008f * s * Mathf.Sin(angle);
                vertices.Add(new Vector3(Mathf.Sin(angle) * radial, lift, Mathf.Cos(angle) * radial)); uv.Add(new Vector2(u, s));
                if (r < rows && c < columns) Grid(triangles, r * (columns + 1) + c, columns + 1);
            }
            return MeshFrom("Food Frilled Fan", vertices, triangles, uv);
        }

        /// <summary>戻した麺を、画面で消えにくい直径5〜9mmの湾曲した束にします。</summary>
        private static void Noodles(FoodModelBuilder model, string id)
        {
            bool glass = id == "sweet_potato_noodle" || id == "mung_bean_noodle" || id == "shirataki";
            bool yellow = id == "corn_noodle" || id == "chinese_noodle";
            bool curly = id == "chinese_noodle";
            uint dark = id == "sweet_potato_noodle" ? 0x958678u : yellow ? 0xD4AB42u : 0xD8DFCCu;
            uint light = id == "sweet_potato_noodle" ? 0xD3C9B4u : yellow ? 0xFFE69Au : 0xFFFEEE;
            Surface(model, id, 0, dark, light, glass ? FoodPattern.Translucent : FoodPattern.Fibers,
                transparent: glass, rawAlpha: id == "shirataki" ? .87f : .82f, cookedAlpha: .64f, gloss: .56f);
            int count = curly ? 8 : 9; int samples = curly ? 16 : 12;
            float radius = id == "rice_noodle" || id == "sweet_potato_noodle" ? .0045f : .0031f;
            for (int i = 0; i < count; i++)
                model.Add(0, FoodKit.Tube(NoodlePath(i, count, samples, curly), radius, radius * .91f, curly ? 5 : 6), Vector3.zero, Vector3.one);
        }

        /// <summary>互いに隙間があり、両端が少しほぐれた麺の中心線を決定論的に生成します。</summary>
        private static Vector3[] NoodlePath(int index, int count, int samples, bool curly)
        {
            Vector3[] path = new Vector3[samples];
            float offset = (index - (count - 1) * .5f) * .010f;
            for (int j = 0; j < samples; j++)
            {
                float s = (float)j / (samples - 1); float phase = s * Mathf.PI * (curly ? 6f : 2f) + index * .49f;
                float x = offset + (curly ? .007f : .012f) * Mathf.Sin(phase) + .010f * (s - .5f) * Mathf.Sin(index * 1.7f);
                float y = .010f * (index % 3) + .009f * Mathf.Sin(s * Mathf.PI * 2f + index * .71f);
                path[j] = new Vector3(x, y, (s - .5f) * .174f);
            }
            return path;
        }

        /// <summary>板春雨の透ける幅広帯と、刀削麺の薄い縁を持つ不揃いな帯を表現します。</summary>
        private static void RibbonNoodles(FoodModelBuilder model, string id)
        {
            bool glass = id == "wide_glass_noodle";
            Surface(model, id, 0, glass ? 0xC1CCBCu : 0xDDD3B9u, glass ? 0xF2F7E6u : 0xFFF4DDu,
                glass ? FoodPattern.Translucent : FoodPattern.Fibers, transparent: glass, rawAlpha: .79f, cookedAlpha: .60f, gloss: .58f);
            for (int i = 0; i < 4; i++)
                model.Add(0, Ribbon(.021f, .168f, glass ? .0017f : .0036f, i, !glass),
                    new Vector3((i - 1.5f) * .025f, .007f * (i % 2), 0f), Vector3.one, new Vector3(0f, (i - 1.5f) * 5f, 0f));
        }

        /// <summary>薄い側縁を閉じた、幅と厚みがうねる麺または干豆腐の帯を生成します。</summary>
        private static Mesh Ribbon(float width, float length, float thickness, int phase, bool shaved)
        {
            const int rows = 14; const int columns = 4; const int stride = columns + 1;
            List<Vector3> vertices = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> triangles = new List<int>();
            int layerSize = (rows + 1) * stride;
            for (int layer = 0; layer < 2; layer++) for (int r = 0; r <= rows; r++) for (int c = 0; c <= columns; c++)
            {
                float s = (float)r / rows; float u = (float)c / columns; float across = 2f * u - 1f;
                float taper = shaved ? .65f + .35f * Mathf.Sin(s * Mathf.PI) : 1f;
                float edgeThickness = shaved ? 1f - .78f * Mathf.Abs(across) : 1f;
                float x = across * width * .5f * taper * (1f + .08f * Mathf.Sin(s * 25f + phase)) + .009f * Mathf.Sin(s * 7f + phase);
                float y = .008f * Mathf.Sin(s * 9f + phase) + .003f * across * Mathf.Sin(s * 11f) + (layer == 0 ? 1f : -1f) * thickness * .5f * edgeThickness;
                vertices.Add(new Vector3(x, y, (s - .5f) * length)); uv.Add(new Vector2(u, s));
                if (r < rows && c < columns)
                {
                    int a = layer * layerSize + r * stride + c;
                    if (layer == 0) Grid(triangles, a, stride);
                    else { FoodKit.Triangle(triangles, a, a + 1, a + stride); FoodKit.Triangle(triangles, a + 1, a + stride + 1, a + stride); }
                }
            }
            for (int r = 0; r < rows; r++)
            {
                Bridge(triangles, r * stride, (r + 1) * stride, layerSize);
                Bridge(triangles, (r + 1) * stride + columns, r * stride + columns, layerSize);
            }
            for (int c = 0; c < columns; c++)
            {
                Bridge(triangles, c + 1, c, layerSize);
                Bridge(triangles, rows * stride + c, rows * stride + c + 1, layerSize);
            }
            return MeshFrom("Food Soft Ribbon", vertices, triangles, uv);
        }

        /// <summary>なめらかな絹、布目の木綿、揚げた厚揚げと薄い油揚げを丸角立体にします。</summary>
        private static void Tofu(FoodModelBuilder model, string id)
        {
            bool fried = id == "atsuage" || id == "aburaage"; bool thin = id == "aburaage"; bool firm = id == "firm_tofu";
            model.SetSurface(0, SoyTexture(id, fried, firm), new FoodSurface(new Color(.96f, .91f, .79f, 1f),
                new Color(.79f, .68f, .49f, 1f), fried ? .27f : firm ? .29f : .52f, .72f));
            if (id == "atsuage") Surface(model, id, 1, 0xDDD9C1, 0xFFFFEC, FoodPattern.Pores);
            Vector3 size = new Vector3(.132f, thin ? .014f : .065f, thin ? .106f : .095f);
            float corner = thin ? .005f : firm ? .0045f : .008f;
            model.Add(0, RoundedBlock(size, corner, fried ? .0018f : firm ? .0007f : .00015f, false, id == "atsuage"), Vector3.zero, Vector3.one);
            if (id == "atsuage") model.Add(1, RoundedBlock(size, corner, .0005f, true, true), Vector3.zero, Vector3.one);
        }

        /// <summary>布の交差繊維や揚げ面の斑点・気泡を128pxミップ付きテクスチャに焼き込みます。</summary>
        private static Texture2D SoyTexture(string id, bool fried, bool woven)
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
                { name = "Food Texture " + id, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            try
            {
                Color[] pixels = new Color[size * size]; float seed = (FoodTextures.Seed(id) % 4096) * .07f;
                Color dark = FoodTextures.Rgb(fried ? 0xAA6428u : woven ? 0xCFCCB6u : 0xE5E1CBu);
                Color light = FoodTextures.Rgb(fried ? 0xEBC078u : 0xFFFEEDu);
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size; float v = (y + .5f) / size;
                    float noise = Mathf.PerlinNoise(u * 24f + seed, v * 24f + seed);
                    float value = .70f + .19f * noise;
                    if (woven)
                    {
                        float weave = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 32f)) * Mathf.Abs(Mathf.Sin(v * Mathf.PI * 32f));
                        value = .38f + .42f * weave + .15f * noise;
                    }
                    if (fried)
                    {
                        float bubble = Mathf.PerlinNoise(u * 53f + seed, v * 53f + seed);
                        float wrinkles = Mathf.Sin(u * 90f + Mathf.Sin(v * 32f) * 3f);
                        value = Mathf.Clamp01(.17f + noise * .75f + bubble * .2f + wrinkles * .07f);
                    }
                    pixels[y * size + x] = Color.Lerp(dark, light, value);
                }
                texture.SetPixels(pixels); texture.Apply(true, true); return texture;
            }
            catch { OwnedObjects.Destroy(texture); throw; }
        }

        /// <summary>六面を別法線・別UVで生成し、厚揚げでは一面だけを白い切断面に分離します。</summary>
        private static Mesh RoundedBlock(Vector3 size, float radius, float bumps, bool cutOnly, bool separateCut)
        {
            const int divisions = 4;
            List<Vector3> vertices = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> triangles = new List<int>();
            Vector3 half = size * .5f; Vector3 core = half - Vector3.one * radius;
            for (int face = 0; face < 6; face++)
            {
                if (separateCut && (cutOnly ? face != 4 : face == 4)) continue;
                Vector3 normal = face == 0 ? Vector3.up : face == 1 ? Vector3.down : face == 2 ? Vector3.right : face == 3 ? Vector3.left : face == 4 ? Vector3.forward : Vector3.back;
                Vector3 axis = Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.up;
                Vector3 across = Vector3.Cross(axis, normal); int start = vertices.Count;
                for (int r = 0; r <= divisions; r++) for (int c = 0; c <= divisions; c++)
                {
                    float u = (float)c / divisions; float v = (float)r / divisions;
                    Vector3 cube = normal + axis * (u * 2f - 1f) + across * (v * 2f - 1f);
                    Vector3 outer = Vector3.Scale(cube, half);
                    Vector3 closest = new Vector3(Mathf.Clamp(outer.x, -core.x, core.x), Mathf.Clamp(outer.y, -core.y, core.y), Mathf.Clamp(outer.z, -core.z, core.z));
                    Vector3 point = closest + (outer - closest).normalized * radius;
                    float envelope = c == 0 || c == divisions || r == 0 || r == divisions ? 0f : Mathf.Sin(u * Mathf.PI) * Mathf.Sin(v * Mathf.PI);
                    point += normal * (bumps * envelope * Mathf.Sin(u * 31f + face) * Mathf.Sin(v * 29f + face));
                    vertices.Add(point); uv.Add(new Vector2(u, v));
                    if (r < divisions && c < divisions) Grid(triangles, start + r * (divisions + 1) + c, divisions + 1);
                }
            }
            return MeshFrom("Food Rounded Tofu", vertices, triangles, uv);
        }

        /// <summary>干豆腐を繊維質の薄い細切り八本にして、緩く重なった束にします。</summary>
        private static void TofuStrips(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xC7B476, 0xF2DDA3, FoodPattern.Fibers);
            for (int i = 0; i < 8; i++)
                model.Add(0, FoodKit.Sheet(.009f, .166f, 1.4f, .005f, 12, 3),
                    new Vector3((i - 3.5f) * .011f, .006f * (i % 3), 0f), Vector3.one, new Vector3(0f, (i % 3 - 1) * 6f, 0f));
        }

        /// <summary>湯葉の薄膜を四層に折り重ね、不揃いなしわと縁を見せます。</summary>
        private static void Yuba(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xC9AD60, 0xF4DEA2, FoodPattern.Fibers, transparent: true, rawAlpha: .91f, cookedAlpha: .83f, gloss: .44f);
            for (int i = 0; i < 4; i++)
                model.Add(0, FoodKit.Sheet(.116f - i * .006f, .142f, 3.5f, .006f, 10, 8),
                    new Vector3(i * .006f - .009f, i * .008f, (i % 2) * .010f), Vector3.one, new Vector3(0f, i * 13f - 18f, i * 3f));
        }

        /// <summary>豆皮を幅広の巻きシート、腐竹をしわのある細い巻き棒として生成します。</summary>
        private static void RolledSoy(FoodModelBuilder model, string id)
        {
            bool fuzhu = id == "fuzhu";
            Surface(model, id, 0, fuzhu ? 0xB89C51u : 0xC8B273u, fuzhu ? 0xEDD491u : 0xF4DFADu, FoodPattern.Fibers);
            int count = fuzhu ? 4 : 2;
            for (int i = 0; i < count; i++)
                model.Add(0, Scroll(fuzhu ? .009f : .020f, fuzhu ? .160f : .118f, i),
                    new Vector3((i - (count - 1) * .5f) * (fuzhu ? .024f : .045f), .005f * (i % 2), 0f),
                    Vector3.one, new Vector3(0f, (i - 1) * 5f, fuzhu ? 0f : 12f));
        }

        /// <summary>端面に巻き込みが見える開いた渦巻き膜を長手方向にしわを付けて生成します。</summary>
        private static Mesh Scroll(float radius, float length, int phase)
        {
            const int rows = 8; const int columns = 20;
            List<Vector3> vertices = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> triangles = new List<int>();
            for (int r = 0; r <= rows; r++) for (int c = 0; c <= columns; c++)
            {
                float s = (float)r / rows; float u = (float)c / columns; float angle = -u * Mathf.PI * 3.3f;
                float radial = radius * (.38f + .62f * u) * (1f + .12f * Mathf.Sin(s * 35f + angle * 3f + phase));
                vertices.Add(new Vector3(Mathf.Cos(angle) * radial + .004f * Mathf.Sin(s * 7f + phase),
                    Mathf.Sin(angle) * radial, (s - .5f) * length)); uv.Add(new Vector2(u, s));
                if (r < rows && c < columns) Grid(triangles, r * (columns + 1) + c, columns + 1);
            }
            return MeshFrom("Food Rolled Soy Sheet", vertices, triangles, uv);
        }

        /// <summary>行方向と列方向の格子を二枚の三角形で張ります。</summary>
        private static void Grid(List<int> triangles, int a, int stride)
        {
            FoodKit.Triangle(triangles, a, a + stride, a + 1); FoodKit.Triangle(triangles, a + 1, a + stride, a + stride + 1);
        }

        /// <summary>帯の上下二層の外縁を閉じます。</summary>
        private static void Bridge(List<int> triangles, int a, int b, int offset)
        {
            FoodKit.Triangle(triangles, a, b, a + offset); FoodKit.Triangle(triangles, b, b + offset, a + offset);
        }

        /// <summary>有限な頂点を検査し、失敗時の所有物を解放して法線・接線付きメッシュを返します。</summary>
        private static Mesh MeshFrom(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uv)
        {
            foreach (Vector3 vertex in vertices)
                if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
                    throw new ArgumentException("食材頂点には有限値が必要です。");
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            try
            {
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
            }
            catch { OwnedObjects.Destroy(mesh); throw; }
        }
    }
}
