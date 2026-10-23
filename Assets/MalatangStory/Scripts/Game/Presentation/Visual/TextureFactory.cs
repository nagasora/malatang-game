using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>白色のマスクとして描画する画面操作・店舗・天気アイコンの種類。</summary>
    public enum IconKind
    {
        /// <summary>硬貨。</summary>
        Coin,
        /// <summary>星。</summary>
        Star,
        /// <summary>ハート。</summary>
        Heart,
        /// <summary>時計。</summary>
        Clock,
        /// <summary>調理鍋。</summary>
        Pot,
        /// <summary>盛り付けボウル。</summary>
        Bowl,
        /// <summary>唐辛子。</summary>
        Chili,
        /// <summary>葉。</summary>
        Leaf,
        /// <summary>人物。</summary>
        Person,
        /// <summary>歯車。</summary>
        Gear,
        /// <summary>買い物カート。</summary>
        Cart,
        /// <summary>棒グラフ。</summary>
        Chart,
        /// <summary>金槌。</summary>
        Hammer,
        /// <summary>優勝杯。</summary>
        Trophy,
        /// <summary>通知ベル。</summary>
        Bell,
        /// <summary>南京錠。</summary>
        Lock,
        /// <summary>確認。</summary>
        Check,
        /// <summary>取消。</summary>
        Cross,
        /// <summary>再生。</summary>
        Play,
        /// <summary>一時停止。</summary>
        Pause,
        /// <summary>左矢印。</summary>
        ArrowLeft,
        /// <summary>右矢印。</summary>
        ArrowRight,
        /// <summary>回転。</summary>
        Rotate,
        /// <summary>保存。</summary>
        Save,
        /// <summary>家。</summary>
        Home,
        /// <summary>本。</summary>
        Book,
        /// <summary>太陽。</summary>
        Sun,
        /// <summary>雲。</summary>
        Cloud,
        /// <summary>雨。</summary>
        Rain,
        /// <summary>雪。</summary>
        Snow,
        /// <summary>炎。</summary>
        Fire,
        /// <summary>箒。</summary>
        Broom,
        /// <summary>店舗スタッフ。</summary>
        Staff,
        /// <summary>重量計。</summary>
        Scale,
        /// <summary>お玉。</summary>
        Ladle,
        /// <summary>追加。</summary>
        Plus,
        /// <summary>減少。</summary>
        Minus,
        /// <summary>メニュー。</summary>
        Menu,
        /// <summary>情報。</summary>
        Info,
        /// <summary>警告。</summary>
        Warning,
        /// <summary>輝き。</summary>
        Sparkle,
        /// <summary>店舗。</summary>
        Shop,
        /// <summary>カレンダー。</summary>
        Calendar,
        /// <summary>温度計。</summary>
        Thermometer,
        /// <summary>滴。</summary>
        Drop,
        Camera
    }

    /// <summary>店舗用テクスチャとUIスプライトを外部素材なしで生成する。</summary>
    /// <remarks>
    /// Unityのメインスレッドから使用する。全引数を含むキーで生成物を共有するため、
    /// 呼出側は返却されたオブジェクトの画素や設定を変更・破棄しないこと。
    /// 破棄されたキャッシュは次の取得時に再生成する。色成分は有限値を許容し、RGBA32への書込時に飽和する。
    /// </remarks>
    public static class TextureFactory
    {
        private const int MaximumSize = 4096;
        private const int IconSize = 96;
        private static readonly Dictionary<Tuple<string, int, int, int, float, Color, Color>, Texture2D> Textures =
            new Dictionary<Tuple<string, int, int, int, float, Color, Color>, Texture2D>();
        private static readonly Dictionary<Tuple<string, int, int, int, float, Color, Color>, Sprite> Sprites =
            new Dictionary<Tuple<string, int, int, int, float, Color, Color>, Sprite>();

        /// <summary>指定した二色とシードから、継ぎ目と木目のある板材を生成する。</summary>
        public static Texture2D SteamNoise(int size)
        {
            ValidateSize(size);
            return Texture(Key(nameof(SteamNoise), size), size, size, false, (x, y) =>
            {
                float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                float noise = ValueNoise(x * .17f, y * .11f, 711);
                float alpha = Mathf.Exp(-(u * u * 1.4f + v * v * .7f) * 4) * (.3f + noise * .7f);
                return new Color(1, 1, 1, Mathf.Clamp01(alpha));
            });
        }

        public static Texture2D WoodPlanks(int size, Color light, Color dark, int seed)
        {
            ValidateSize(size);
            OwnedObjects.ValidateColor(light, nameof(light));
            OwnedObjects.ValidateColor(dark, nameof(dark));
            return Texture(Key(nameof(WoodPlanks), size, seed, 0, 0f, light, dark), size, size, true,
                (int x, int y) =>
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    int plank = Mathf.FloorToInt(v * 6f);
                    float seam = Mathf.Min(Fraction(v * 6f), 1f - Fraction(v * 6f));
                    float phase = Hash(plank, 0, seed) * Mathf.PI * 2f;
                    float grain = Mathf.Sin(u * Mathf.PI * 64f + Mathf.Sin(v * Mathf.PI * 4f + phase) * .35f + phase);
                    float shade = 0.5f + grain * .035f + (Hash(x, y, seed) - 0.5f) * .025f;
                    float joint = Fraction(u * 3f + (plank % 2) * 0.5f);
                    if (seam < 0.025f || joint < 0.009f) shade = 0.05f;
                    return Color.Lerp(dark, light, Mathf.Clamp01(shade));
                });
        }

        /// <summary>一辺をcount枚に分けた正方形タイルと目地を生成する。</summary>
        public static Texture2D Tiles(int size, Color tile, Color grout, int count)
        {
            ValidateGrid(size, count);
            OwnedObjects.ValidateColor(tile, nameof(tile));
            OwnedObjects.ValidateColor(grout, nameof(grout));
            return Texture(Key(nameof(Tiles), size, count, 0, 0f, tile, grout), size, size, true,
                (int x, int y) =>
                {
                    float u = (x + 0.5f) * count / size;
                    float v = (y + 0.5f) * count / size;
                    float edge = Mathf.Min(Mathf.Min(Fraction(u), 1f - Fraction(u)), Mathf.Min(Fraction(v), 1f - Fraction(v)));
                    float mask = Mathf.Clamp01((edge - 0.035f) * size / count + 0.5f);
                    Color face = Tint(tile, 0.97f + Hash(Mathf.FloorToInt(u), Mathf.FloorToInt(v), 71) * 0.06f);
                    return Color.Lerp(grout, face, mask);
                });
        }

        /// <summary>局所乱数による滑らかなグレースケールノイズを生成する。scaleは一辺の格子周波数。</summary>
        public static Texture2D Noise(int size, float scale, int seed)
        {
            ValidateSize(size);
            OwnedObjects.ValidateFinite(scale, nameof(scale));
            if (scale <= 0f || scale > MaximumSize) throw new ArgumentOutOfRangeException(nameof(scale));
            return Texture(Key(nameof(Noise), size, seed, 0, scale, default(Color), default(Color)), size, size, true,
                (int x, int y) =>
                {
                    float shade = ValueNoise((x + 0.5f) * scale / size, (y + 0.5f) * scale / size, seed);
                    return new Color(shade, shade, shade, 1f);
                });
        }

        /// <summary>指定色に縦糸・横糸の織り目を重ねた布を生成する。</summary>
        public static Texture2D Fabric(int size, Color color)
        {
            ValidateSize(size);
            OwnedObjects.ValidateColor(color, nameof(color));
            return Texture(Key(nameof(Fabric), size, 0, 0, 0f, color, default(Color)), size, size, true,
                (int x, int y) =>
                {
                    float warp = Mathf.Cos((x + 0.5f) / size * Mathf.PI * 64f);
                    float weft = Mathf.Cos((y + 0.5f) / size * Mathf.PI * 64f);
                    float weave = warp * weft;
                    return Tint(color, 0.93f + weave * 0.055f + (Hash(x, y, 193) - 0.5f) * 0.055f);
                });
        }

        /// <summary>中華回紋の帯と渦巻く雲紋を配した壁紙を生成する。</summary>
        public static Texture2D Wallpaper(int size, Color background, Color pattern)
        {
            ValidateSize(size);
            OwnedObjects.ValidateColor(background, nameof(background));
            OwnedObjects.ValidateColor(pattern, nameof(pattern));
            Vector2[] meander = { V(0, 4), V(3, 4), V(3, 1), V(9, 1), V(9, 7), V(5, 7), V(5, 4), V(7, 4) };
            Func<Vector2, float> fret = Polyline(0.42f, meander);
            Func<Vector2, float> curl = Arc(V(0, 0), 1.35f, -0.5f, Mathf.PI * 1.6f, 0.24f);
            return Texture(Key(nameof(Wallpaper), size, 0, 0, 0f, background, pattern), size, size, true,
                (int x, int y) =>
                {
                    float u = (x + 0.5f) / size * 4f;
                    float v = (y + 0.5f) / size * 4f;
                    float bandY = Fraction(v);
                    float distance;
                    if (bandY < 0.25f)
                    {
                        Vector2 p = V(Fraction(u) * 12f, bandY * 40f);
                        distance = Mathf.Min(fret(p), PolylineDistance(p, meander, V(-12, 0), 0.42f)) / 12f;
                        distance = Mathf.Min(distance, Mathf.Abs(bandY - 0.225f) - 0.01f);
                    }
                    else
                    {
                        Vector2 p = V((Fraction(u + (Mathf.FloorToInt(v) % 2) * 0.5f) - 0.5f) * 10f, (bandY - 0.62f) * 10f);
                        distance = Mathf.Min(curl(p - V(-0.9f, 0)), curl(V(-p.x, p.y) - V(-0.9f, 0)));
                        distance = Mathf.Min(distance, SegmentDistance(p, V(-2.5f, -1.3f), V(2.5f, -1.3f)) - 0.24f) / 10f;
                    }
                    float alpha = Mathf.Clamp01(0.5f - distance * size / 4f);
                    return Color.Lerp(background, pattern, alpha);
                });
        }

        /// <summary>一辺をcount段に分け、交互に半枚ずらした煉瓦とモルタルを生成する。</summary>
        public static Texture2D Brick(int size, Color brick, Color mortar, int count = 8)
        {
            ValidateGrid(size, count);
            OwnedObjects.ValidateColor(brick, nameof(brick));
            OwnedObjects.ValidateColor(mortar, nameof(mortar));
            return Texture(Key(nameof(Brick), size, count, 0, 0f, brick, mortar), size, size, true,
                (int x, int y) =>
                {
                    float v = (y + 0.5f) * count / size;
                    int row = Mathf.FloorToInt(v);
                    float u = (x + 0.5f) * count / size + (row % 2) * 0.5f;
                    float edge = Mathf.Min(Mathf.Min(Fraction(v), 1f - Fraction(v)), Mathf.Min(Fraction(u), 1f - Fraction(u)) * 2f);
                    float mask = Mathf.Clamp01((edge - 0.06f) * size / count + 0.5f);
                    float shade = 0.88f + Hash(Mathf.FloorToInt(u), row, 29) * 0.16f + (Hash(x, y, 53) - 0.5f) * 0.06f;
                    return Color.Lerp(mortar, Tint(brick, shade), mask);
                });
        }

        /// <summary>微細な繊維と濃淡を備えた紙を指定色で生成する。</summary>
        public static Texture2D Paper(int size, Color color)
        {
            ValidateSize(size);
            OwnedObjects.ValidateColor(color, nameof(color));
            return Texture(Key(nameof(Paper), size, 0, 0, 0f, color, default(Color)), size, size, true,
                (int x, int y) => Tint(color, 0.94f + Hash(x, y, 811) * 0.07f +
                    (ValueNoise(x * 0.12f, y * 0.6f, 307) - 0.5f) * 0.04f));
        }

        /// <summary>中心から周縁まで透明度が滑らかに減衰する白い円を生成する。</summary>
        public static Texture2D SoftCircle(int size)
        {
            ValidateSize(size);
            return Texture(Key(nameof(SoftCircle), size), size, size, false,
                (int x, int y) =>
                {
                    float distance = V((x + 0.5f) * 2f / size - 1f, (y + 0.5f) * 2f / size - 1f).magnitude;
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance));
                    return new Color(1f, 1f, 1f, alpha);
                });
        }

        /// <summary>沸騰の泡に使う、輪郭が柔らかな中空の白い円マスクを共有キャッシュに生成します。</summary>
        public static Texture2D BubbleRing(int size)
        {
            ValidateSize(size);
            return Texture(Key(nameof(BubbleRing), size), size, size, false,
                (int x, int y) =>
                {
                    float radius = new Vector2((x + .5f) * 2f / size - 1f, (y + .5f) * 2f / size - 1f).magnitude;
                    float alpha = 1f - Mathf.SmoothStep(.07f, .24f, Mathf.Abs(radius - .67f));
                    return new Color(1, 1, 1, alpha);
                });
        }

        /// <summary>花びらまたは落ち葉の先細りマスクを、季節別の二枚だけ生成します。</summary>
        public static Texture2D FallingLeaf(int size, bool petal)
        {
            ValidateSize(size);
            return Texture(Key(nameof(FallingLeaf), size, petal ? 1 : 0), size, size, false,
                (int x, int y) =>
                {
                    float u = (x + .5f) * 2f / size - 1f;
                    float v = (y + .5f) * 2f / size - 1f;
                    float width = petal ? .65f * Mathf.Sqrt(Mathf.Max(0, 1f - v * v)) : .7f * (1f - Mathf.Abs(v));
                    float alpha = Mathf.Clamp01((width - Mathf.Abs(u)) * size * .35f);
                    if (petal && v > .5f) alpha *= Mathf.Clamp01((Mathf.Abs(u) + .8f - v) * 8);
                    return new Color(1, 1, 1, alpha);
                });
        }

        /// <summary>白く塗った角丸矩形を9スライス用の境界付きスプライトとして生成する。</summary>
        /// <param name="radius">角の画素半径。1以上で生成寸法が4096画素以下となる値。</param>
        /// <param name="border">各辺のスライス境界の画素数。0はradius+2の自動指定、正値は指定値を使用する。</param>
        /// <remarks>生成寸法はradius×2+6画素。borderの許容範囲は0以上、生成寸法の半分以下。</remarks>
        public static Sprite RoundedRect(int radius, int border = 0)
        {
            ValidateRadius(radius, 2, 6);
            int size = radius * 2 + 6;
            if (border < 0 || border > size / 2) throw new ArgumentOutOfRangeException(nameof(border));
            int inset = border == 0 ? radius + 2 : border;
            float half = size * 0.5f;
            Func<Vector2, float> shape = Box(V(half, half), V(half - 1f, half - 1f), radius);
            Tuple<string, int, int, int, float, Color, Color> key = Key(nameof(RoundedRect), radius, border);
            return SpriteMask(key, size, size, (Vector2 p) => Coverage(shape(p)),
                new Vector4(inset, inset, inset, inset));
        }

        /// <summary>指定した画素寸法でアンチエイリアス付きの白い円スプライトを生成する。</summary>
        public static Sprite Circle(int size)
        {
            ValidateSize(size);
            Func<Vector2, float> shape = Disk(V(size * 0.5f, size * 0.5f), Mathf.Max(0.25f, size * 0.5f - 0.75f));
            return SpriteMask(Key(nameof(Circle), size), size, size, (Vector2 p) => Coverage(shape(p)), Vector4.zero);
        }

        /// <summary>指定半径を減衰尺度とする黒い円形の柔らかな影を生成する。</summary>
        public static Sprite SoftShadow(int radius)
        {
            ValidateRadius(radius, 4, 4);
            int size = radius * 4 + 4;
            Tuple<string, int, int, int, float, Color, Color> key = Key(nameof(SoftShadow), radius);
            return CachedSprite(key, () => Texture(key, size, size, false, (int x, int y) =>
            {
                float distance = V(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f).magnitude;
                float fade = 1f - Mathf.SmoothStep(radius, radius * 2f, distance);
                float alpha = Mathf.Exp(-distance * distance / (2f * radius * radius)) * fade * 0.45f;
                return new Color(0f, 0f, 0f, alpha);
            }), Vector4.zero);
        }

        /// <summary>上端top・下端bottomの縦グラデーションを2×96画素で生成する。</summary>
        public static Sprite VerticalGradient(Color top, Color bottom)
        {
            OwnedObjects.ValidateColor(top, nameof(top));
            OwnedObjects.ValidateColor(bottom, nameof(bottom));
            Tuple<string, int, int, int, float, Color, Color> key = Key(nameof(VerticalGradient), 96, 0, 0, 0f, top, bottom);
            return CachedSprite(key, () => Texture(key, 2, 96, false,
                (int x, int y) => Color.Lerp(bottom, top, y / 95f)), Vector4.zero);
        }

        /// <summary>96×96画素の白色アイコンを符号付き距離と透明度によるアンチエイリアスで生成する。</summary>
        /// <exception cref="ArgumentOutOfRangeException">kindが定義済みの値でない場合。</exception>
        public static Sprite Icon(IconKind kind)
        {
            if (!Enum.IsDefined(typeof(IconKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Tuple<string, int, int, int, float, Color, Color> key = Key(nameof(Icon), IconSize, (int)kind);
            return CachedSprite(key, () =>
            {
                Glyph glyph = CreateGlyph(kind);
                return Texture(key, IconSize, IconSize, false,
                    (int x, int y) => new Color(1f, 1f, 1f, Coverage(glyph.Distance(V(x + 0.5f, y + 0.5f)))));
            }, Vector4.zero);
        }

        /// <summary>テクスチャとスプライトに共通する全引数キーを組み立てる。</summary>
        private static Tuple<string, int, int, int, float, Color, Color> Key(string name, int size, int first = 0,
            int second = 0, float scale = 0f, Color primary = default(Color), Color secondary = default(Color))
        {
            return Tuple.Create(name, size, first, second, scale, primary, secondary);
        }

        /// <summary>画素配列を一括転送して共有テクスチャを構築する。</summary>
        private static Texture2D Texture(Tuple<string, int, int, int, float, Color, Color> key, int width, int height,
            bool repeat, Func<int, int, Color> sample)
        {
            Texture2D cached;
            if (Textures.TryGetValue(key, out cached) && cached != null) return cached;
            Color[] pixels = new Color[checked(width * height)];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) pixels[y * width + x] = sample(x, y);
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.name = "Procedural_" + key.Item1;
                texture.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                Textures[key] = texture;
                return texture;
            }
            catch
            {
                OwnedObjects.Destroy(texture);
                throw;
            }
        }

        /// <summary>白色の距離場マスクからスプライトを取得する。</summary>
        private static Sprite SpriteMask(Tuple<string, int, int, int, float, Color, Color> key, int width, int height,
            Func<Vector2, float> alpha, Vector4 border)
        {
            return CachedSprite(key, () => Texture(key, width, height, false,
                (int x, int y) => new Color(1f, 1f, 1f, alpha(V(x + 0.5f, y + 0.5f)))), border);
        }

        /// <summary>矩形メッシュでスプライトを生成し、破棄済みの参照は置き換える。</summary>
        private static Sprite CachedSprite(Tuple<string, int, int, int, float, Color, Color> key,
            Func<Texture2D> createTexture, Vector4 border)
        {
            Sprite cached;
            if (Sprites.TryGetValue(key, out cached) && cached != null && cached.texture != null) return cached;
            if (cached != null) OwnedObjects.Destroy(cached);
            Texture2D texture = createTexture();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = "Procedural_" + key.Item1;
            Sprites[key] = sprite;
            return sprite;
        }

        /// <summary>配列サイズと生成可能な画素寸法を検証する。</summary>
        private static void ValidateSize(int size)
        {
            if (size < 1 || size > MaximumSize) throw new ArgumentOutOfRangeException(nameof(size), "寸法は1〜4096画素です。");
        }

        /// <summary>半径からの寸法計算が上限を超えないことを検証する。</summary>
        private static void ValidateRadius(int radius, int multiplier, int padding)
        {
            if (radius < 1 || radius > (MaximumSize - padding) / multiplier)
                throw new ArgumentOutOfRangeException(nameof(radius));
        }

        /// <summary>画素より細かいタイル分割を拒否する。</summary>
        private static void ValidateGrid(int size, int count)
        {
            ValidateSize(size);
            if (count < 1 || count > size) throw new ArgumentOutOfRangeException(nameof(count));
        }

        /// <summary>透明度を変えずにRGBの明度だけを調整する。</summary>
        private static Color Tint(Color color, float amount)
        {
            return new Color(color.r * amount, color.g * amount, color.b * amount, color.a);
        }

        /// <summary>負座標にも対応した小数部を求める。</summary>
        private static float Fraction(float value) { return value - Mathf.Floor(value); }

        /// <summary>Unityの大域乱数を変更せず座標とシードを乱数に写像する。</summary>
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint value = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                value = (value ^ (value >> 13)) * 1274126177u;
                value ^= value >> 16;
                return (value & 0x00ffffffu) / 16777215f;
            }
        }

        /// <summary>隣接格子の値を滑らかに補間する。</summary>
        private static float ValueNoise(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float u = Fraction(x);
            float v = Fraction(y);
            u = u * u * (3f - 2f * u);
            v = v * v * (3f - 2f * v);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), u),
                Mathf.Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), u), v);
        }

        /// <summary>画素単位の符号付き距離を境界幅1画素の被覆率に変換する。</summary>
        private static float Coverage(float distance) { return Mathf.Clamp01(0.5f - distance); }

        /// <summary>二次元座標を構築する。</summary>
        private static Vector2 V(float x, float y) { return new Vector2(x, y); }

        /// <summary>円の正確な符号付き距離を生成する。</summary>
        private static Func<Vector2, float> Disk(Vector2 center, float radius)
        {
            return (Vector2 p) => (p - center).magnitude - radius;
        }

        /// <summary>円環の距離を生成する。</summary>
        private static Func<Vector2, float> Ring(Vector2 center, float radius, float width)
        {
            return (Vector2 p) => Mathf.Abs((p - center).magnitude - radius) - width * 0.5f;
        }

        /// <summary>角丸矩形の正確な符号付き距離を生成する。</summary>
        private static Func<Vector2, float> Box(Vector2 center, Vector2 half, float radius = 0f)
        {
            return (Vector2 p) =>
            {
                Vector2 q = V(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + V(radius, radius);
                return V(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            };
        }

        /// <summary>二つの形状の差集合を生成する。</summary>
        private static Func<Vector2, float> Cut(Func<Vector2, float> outer, Func<Vector2, float> inner)
        {
            return (Vector2 p) => Mathf.Max(outer(p), -inner(p));
        }

        /// <summary>形状同士の和集合を生成する。</summary>
        private static Func<Vector2, float> Union(params Func<Vector2, float>[] shapes)
        {
            return (Vector2 p) =>
            {
                float distance = float.PositiveInfinity;
                for (int i = 0; i < shapes.Length; i++) distance = Mathf.Min(distance, shapes[i](p));
                return distance;
            };
        }

        /// <summary>有限線分までの距離を求める。</summary>
        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float denominator = Vector2.Dot(edge, edge);
            float t = denominator > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, edge) / denominator) : 0f;
            return (p - a - edge * t).magnitude;
        }

        /// <summary>丸端の折れ線の距離を生成する。</summary>
        private static Func<Vector2, float> Polyline(float radius, params Vector2[] points)
        {
            return (Vector2 p) => PolylineDistance(p, points, Vector2.zero, radius);
        }

        /// <summary>平行移動した折れ線までの距離を求める。</summary>
        private static float PolylineDistance(Vector2 p, Vector2[] points, Vector2 offset, float radius)
        {
            float distance = float.PositiveInfinity;
            for (int i = 1; i < points.Length; i++)
                distance = Mathf.Min(distance, SegmentDistance(p - offset, points[i - 1], points[i]));
            return distance - radius;
        }

        /// <summary>閉じた多角形の正確な境界距離と内外判定を生成する。</summary>
        private static Func<Vector2, float> Polygon(params Vector2[] points)
        {
            return (Vector2 p) =>
            {
                float distance = float.PositiveInfinity;
                bool inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                {
                    Vector2 a = points[j];
                    Vector2 b = points[i];
                    distance = Mathf.Min(distance, SegmentDistance(p, a, b));
                    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                return inside ? -distance : distance;
            };
        }

        /// <summary>円弧を十分細かな線分に分けて距離場に変換する。</summary>
        private static Func<Vector2, float> Arc(Vector2 center, float radius, float start, float sweep, float stroke)
        {
            int count = Mathf.Max(12, Mathf.CeilToInt(Mathf.Abs(sweep) * radius));
            Vector2[] points = new Vector2[count + 1];
            for (int i = 0; i <= count; i++)
            {
                float angle = start + sweep * i / count;
                points[i] = center + V(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return Polyline(stroke, points);
        }

        /// <summary>任意の頂点数で星形を生成する。</summary>
        private static Func<Vector2, float> StarShape(Vector2 center, float outer, float inner, int tips)
        {
            Vector2[] points = new Vector2[tips * 2];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = Mathf.PI * 0.5f + Mathf.PI * i / tips;
                points[i] = center + V(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? outer : inner);
            }
            return Polygon(points);
        }

        /// <summary>雲の丸い輪郭を構成する。</summary>
        private static Func<Vector2, float> CloudShape(float offsetY)
        {
            return Union(Disk(V(31, 44 + offsetY), 15), Disk(V(47, 55 + offsetY), 19),
                Disk(V(66, 45 + offsetY), 14), Box(V(49, 39 + offsetY), V(26, 8), 6));
        }

        /// <summary>滴または炎の外周を曲線状の多角形で構成する。</summary>
        private static Func<Vector2, float> DropShape()
        {
            return Polygon(V(48, 84), V(42, 71), V(29, 53), V(24, 40), V(26, 27), V(34, 18),
                V(48, 14), V(62, 18), V(70, 27), V(72, 40), V(67, 53), V(56, 69));
        }

        /// <summary>人物の頭と肩を構成する。</summary>
        private static Func<Vector2, float> PersonShape()
        {
            return Union(Disk(V(48, 67), 13), Box(V(48, 30), V(26, 18), 15));
        }

        /// <summary>図形の和集合として一つのグリフを保持する。</summary>
        private sealed class Glyph
        {
            private readonly List<Func<Vector2, float>> shapes = new List<Func<Vector2, float>>();

            /// <summary>グリフに輪郭を追加する。</summary>
            public void Add(params Func<Vector2, float>[] values) { shapes.AddRange(values); }

            /// <summary>グリフ全体の符号付き距離を求める。</summary>
            public float Distance(Vector2 p)
            {
                float distance = float.PositiveInfinity;
                for (int i = 0; i < shapes.Count; i++) distance = Mathf.Min(distance, shapes[i](p));
                return distance;
            }
        }

        /// <summary>全アイコンの意味を識別できる専用の輪郭を構成する。</summary>
        private static Glyph CreateGlyph(IconKind kind)
        {
            Glyph g = new Glyph();
            switch (kind)
            {
                case IconKind.Coin:
                    g.Add(Ring(V(48, 48), 33, 6), Ring(V(48, 48), 25, 2),
                        Polyline(2.5f, V(58, 64), V(39, 64), V(36, 52), V(58, 44), V(56, 32), V(37, 32)),
                        Polyline(2, V(47, 70), V(47, 26)));
                    break;
                case IconKind.Star:
                    g.Add(StarShape(V(48, 47), 36, 16, 5));
                    break;
                case IconKind.Heart:
                    g.Add(Union(Disk(V(33, 61), 18), Disk(V(63, 61), 18),
                        Polygon(V(16, 54), V(48, 15), V(80, 54), V(48, 69))));
                    break;
                case IconKind.Clock:
                    g.Add(Ring(V(48, 48), 33, 6), Polyline(3, V(48, 71), V(48, 48), V(65, 38)), Disk(V(48, 48), 4));
                    break;
                case IconKind.Pot:
                    g.Add(Box(V(48, 37), V(27, 20), 7), Polyline(3, V(17, 60), V(79, 60)),
                        Arc(V(48, 60), 22, 0f, Mathf.PI, 2.5f), Box(V(48, 83), V(6, 3), 2),
                        Polyline(3, V(22, 48), V(12, 48), V(12, 36), V(22, 36)),
                        Polyline(3, V(74, 48), V(84, 48), V(84, 36), V(74, 36)));
                    break;
                case IconKind.Bowl:
                    g.Add(Polygon(V(15, 53), V(22, 34), V(34, 20), V(62, 20), V(74, 34), V(81, 53)),
                        Box(V(48, 16), V(19, 3), 2), Polyline(3, V(13, 58), V(83, 58)),
                        Polyline(2, V(35, 68), V(31, 74), V(35, 81)), Polyline(2, V(57, 68), V(53, 75), V(57, 84)));
                    break;
                case IconKind.Chili:
                    g.Add(Polygon(V(59, 70), V(73, 64), V(75, 51), V(66, 35), V(52, 25), V(31, 19),
                        V(17, 22), V(35, 30), V(47, 43), V(50, 60)),
                        Polyline(3, V(62, 68), V(59, 78), V(49, 84)), Polyline(2, V(55, 65), V(66, 61), V(72, 65)));
                    break;
                case IconKind.Leaf:
                    g.Add(Cut(Polygon(V(17, 22), V(18, 48), V(30, 67), V(53, 75), V(79, 80),
                            V(75, 51), V(65, 31), V(44, 21)),
                        Union(Polyline(2, V(24, 28), V(67, 66)), Polyline(1.5f, V(43, 45), V(36, 61)),
                            Polyline(1.5f, V(47, 49), V(63, 44)))));
                    g.Add(Polyline(2.5f, V(13, 16), V(28, 31)));
                    break;
                case IconKind.Person:
                    g.Add(PersonShape());
                    break;
                case IconKind.Gear:
                    g.Add(Cut(StarShape(V(48, 48), 36, 28, 12), Disk(V(48, 48), 13)));
                    break;
                case IconKind.Cart:
                    g.Add(Polyline(3, V(20, 57), V(28, 24), V(73, 24), V(81, 57), V(20, 57)),
                        Polyline(3, V(32, 57), V(42, 78), V(56, 78), V(69, 57)),
                        Polyline(2, V(39, 28), V(36, 53)), Polyline(2, V(55, 27), V(55, 53)), Polyline(2, V(70, 29), V(73, 53)),
                        Polyline(2, V(27, 41), V(76, 41)));
                    break;
                case IconKind.Chart:
                    g.Add(Polyline(3, V(23, 16), V(23, 80), V(79, 80), V(79, 16), V(23, 16)),
                        Polyline(2, V(38, 18), V(38, 77)), Polyline(2, V(44, 64), V(70, 64)),
                        Polyline(2, V(44, 48), V(70, 48)), Polyline(2, V(44, 32), V(70, 32)));
                    break;
                case IconKind.Camera:
                    g.Add(Polyline(3, V(15, 23), V(15, 68), V(30, 68), V(36, 78), V(61, 78), V(67, 68), V(82, 68), V(82, 23), V(15, 23)),
                        Cut(Disk(V(49, 45), 18), Disk(V(49, 45), 12)), Disk(V(73, 60), 3));
                    break;
                case IconKind.Hammer:
                    g.Add(Polyline(6, V(27, 18), V(61, 61)),
                        Polygon(V(39, 71), V(54, 84), V(82, 59), V(68, 44)));
                    break;
                case IconKind.Trophy:
                    g.Add(Polygon(V(28, 80), V(68, 80), V(65, 52), V(56, 39), V(40, 39), V(31, 52)),
                        Polyline(4, V(48, 39), V(48, 20)), Box(V(48, 15), V(22, 4), 2),
                        Arc(V(28, 64), 14, Mathf.PI * 0.5f, Mathf.PI, 3),
                        Arc(V(68, 64), 14, -Mathf.PI * 0.5f, Mathf.PI, 3));
                    break;
                case IconKind.Bell:
                    g.Add(Union(Disk(V(48, 56), 23), Polygon(V(25, 58), V(71, 58), V(74, 32),
                        V(81, 24), V(15, 24), V(22, 32))), Disk(V(48, 15), 7), Disk(V(48, 82), 4));
                    break;
                case IconKind.Lock:
                    g.Add(Cut(Box(V(48, 34), V(27, 22), 5),
                            Union(Disk(V(48, 39), 5), Box(V(48, 31), V(2, 6)))),
                        Arc(V(48, 60), 17, 0f, Mathf.PI, 4),
                        Polyline(4, V(31, 60), V(31, 54)), Polyline(4, V(65, 60), V(65, 54)));
                    break;
                case IconKind.Check:
                    g.Add(Polyline(5, V(18, 47), V(38, 26), V(79, 71)));
                    break;
                case IconKind.Cross:
                    g.Add(Polyline(5, V(23, 23), V(73, 73)), Polyline(5, V(23, 73), V(73, 23)));
                    break;
                case IconKind.Play:
                    g.Add(Polygon(V(29, 16), V(78, 48), V(29, 80)));
                    break;
                case IconKind.Pause:
                    g.Add(Box(V(32, 48), V(8, 31), 2), Box(V(64, 48), V(8, 31), 2));
                    break;
                case IconKind.ArrowLeft:
                    g.Add(Polyline(4, V(75, 48), V(20, 48)), Polyline(4, V(43, 72), V(19, 48), V(43, 24)));
                    break;
                case IconKind.ArrowRight:
                    g.Add(Polyline(4, V(21, 48), V(76, 48)), Polyline(4, V(53, 72), V(77, 48), V(53, 24)));
                    break;
                case IconKind.Rotate:
                    g.Add(Arc(V(48, 48), 28, Mathf.PI * 0.25f, Mathf.PI * 1.65f, 3.5f),
                        Polygon(V(78, 76), V(56, 72), V(74, 54)));
                    break;
                case IconKind.Save:
                    g.Add(Cut(Polygon(V(17, 17), V(79, 17), V(79, 68), V(66, 81), V(17, 81)),
                        Union(Box(V(48, 69), V(16, 10)), Box(V(48, 35), V(20, 13)))));
                    g.Add(Box(V(55, 69), V(3, 7)), Polyline(2, V(35, 36), V(61, 36)));
                    break;
                case IconKind.Home:
                    g.Add(Cut(Box(V(48, 35), V(25, 22)), Box(V(48, 26), V(8, 13))),
                        Polyline(4, V(13, 51), V(48, 81), V(83, 51)));
                    break;
                case IconKind.Book:
                    g.Add(Polyline(3, V(48, 18), V(48, 73)),
                        Polyline(3, V(48, 73), V(33, 80), V(15, 80), V(15, 24), V(33, 24), V(48, 18),
                            V(63, 24), V(81, 24), V(81, 80), V(63, 80), V(48, 73)),
                        Polyline(2, V(25, 61), V(36, 58)), Polyline(2, V(60, 58), V(71, 61)));
                    break;
                case IconKind.Sun:
                    g.Add(Disk(V(48, 48), 19));
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4f;
                        Vector2 direction = V(Mathf.Cos(angle), Mathf.Sin(angle));
                        g.Add(Polyline(3, V(48, 48) + direction * 27f, V(48, 48) + direction * 37f));
                    }
                    break;
                case IconKind.Cloud:
                    g.Add(CloudShape(0));
                    break;
                case IconKind.Rain:
                    g.Add(CloudShape(12));
                    for (int i = 0; i < 3; i++) g.Add(Polyline(3, V(29 + i * 20, 33), V(24 + i * 20, 18)));
                    break;
                case IconKind.Snow:
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = i * Mathf.PI / 3f;
                        Vector2 d = V(Mathf.Cos(angle), Mathf.Sin(angle));
                        Vector2 n = V(-d.y, d.x);
                        g.Add(Polyline(2.5f, V(48, 48) - d * 34f, V(48, 48) + d * 34f));
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            Vector2 tip = V(48, 48) + d * (23 * sign);
                            g.Add(Polyline(2, tip - d * (9 * sign) + n * 8, tip, tip - d * (9 * sign) - n * 8));
                        }
                    }
                    break;
                case IconKind.Fire:
                    g.Add(Cut(Polygon(V(47, 85), V(62, 68), V(59, 53), V(73, 64), V(79, 40),
                            V(74, 24), V(61, 14), V(37, 14), V(23, 26), V(19, 41), V(30, 63), V(33, 47), V(45, 65)),
                        Polygon(V(48, 53), V(62, 33), V(58, 21), V(47, 17), V(37, 25), V(37, 34))));
                    break;
                case IconKind.Broom:
                    g.Add(Polyline(4, V(37, 43), V(70, 82)),
                        Cut(Polygon(V(34, 48), V(52, 33), V(44, 13), V(12, 29)),
                            Union(Polyline(1.5f, V(22, 25), V(35, 41)), Polyline(1.5f, V(32, 20), V(43, 35)))));
                    break;
                case IconKind.Staff:
                    g.Add(Cut(PersonShape(), Union(Polygon(V(38, 47), V(48, 35), V(58, 47)),
                        Box(V(48, 25), V(12, 7), 2))),
                        Union(Disk(V(36, 81), 7), Disk(V(48, 84), 8), Disk(V(60, 81), 7)), Box(V(48, 76), V(17, 3)));
                    break;
                case IconKind.Scale:
                    g.Add(Cut(Box(V(48, 35), V(30, 22), 6), Box(V(48, 38), V(14, 10), 3)),
                        Polyline(3, V(48, 56), V(48, 66)), Polyline(4, V(19, 71), V(77, 71)),
                        Polyline(2, V(48, 32), V(54, 43)));
                    break;
                case IconKind.Ladle:
                    g.Add(Polyline(4, V(50, 44), V(66, 80), V(76, 80)),
                        Cut(Disk(V(36, 32), 21), Box(V(36, 49), V(24, 9))));
                    break;
                case IconKind.Plus:
                    g.Add(Polyline(5, V(18, 48), V(78, 48)), Polyline(5, V(48, 18), V(48, 78)));
                    break;
                case IconKind.Minus:
                    g.Add(Polyline(5, V(18, 48), V(78, 48)));
                    break;
                case IconKind.Menu:
                    for (int i = 0; i < 3; i++) g.Add(Polyline(4, V(18, 26 + i * 22), V(78, 26 + i * 22)));
                    break;
                case IconKind.Info:
                    g.Add(Ring(V(48, 48), 34, 5), Disk(V(48, 67), 4),
                        Polyline(3, V(43, 52), V(49, 52), V(49, 28)), Polyline(3, V(41, 28), V(57, 28)));
                    break;
                case IconKind.Warning:
                    g.Add(Cut(Polygon(V(48, 83), V(10, 17), V(86, 17)), Polygon(V(48, 71), V(21, 23), V(75, 23))),
                        Polyline(3, V(48, 57), V(48, 40)), Disk(V(48, 30), 3.5f));
                    break;
                case IconKind.Sparkle:
                    g.Add(StarShape(V(43, 44), 32, 7, 4), StarShape(V(75, 73), 12, 3, 4),
                        StarShape(V(19, 77), 7, 2, 4));
                    break;
                case IconKind.Shop:
                    g.Add(Cut(Box(V(48, 32), V(29, 19)), Union(Box(V(59, 26), V(8, 13)), Box(V(34, 34), V(8, 8)))),
                        Polygon(V(15, 58), V(23, 80), V(73, 80), V(81, 58)), Polyline(3, V(14, 56), V(82, 56)));
                    for (int i = 0; i < 4; i++) g.Add(Disk(V(23 + i * 17, 54), 7));
                    break;
                case IconKind.Calendar:
                    g.Add(Cut(Box(V(48, 44), V(31, 29), 4), Box(V(48, 39), V(25, 17), 1)),
                        Polyline(3, V(33, 68), V(33, 82)), Polyline(3, V(63, 68), V(63, 82)));
                    for (int y = 0; y < 2; y++)
                        for (int x = 0; x < 3; x++) g.Add(Box(V(32 + x * 16, 31 + y * 15), V(3, 3)));
                    break;
                case IconKind.Thermometer:
                    g.Add(Cut(Union(Box(V(43, 56), V(10, 26), 9), Disk(V(43, 26), 16)),
                            Union(Box(V(43, 54), V(5, 22), 4), Disk(V(43, 26), 10))),
                        Disk(V(43, 26), 6), Polyline(2.5f, V(43, 28), V(43, 58)));
                    for (int i = 0; i < 3; i++) g.Add(Polyline(2, V(61, 44 + i * 14), V(70, 44 + i * 14)));
                    break;
                case IconKind.Drop:
                    g.Add(Cut(DropShape(), Arc(V(48, 38), 15, Mathf.PI, Mathf.PI * 0.5f, 2)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return g;
        }
    }
}
