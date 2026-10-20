using System;

/// <summary>描画エンジンに依存しないスープと薬味の外観パラメータを定義します。</summary>
namespace MalatangStory.Game
{
    /// <summary>スープ固有の濁り、油膜、艶と辛さによる変化を保持します。</summary>
    public readonly struct SoupAppearanceParameters
    {
        public readonly string SoupId;
        public readonly int Spice;
        public readonly float Cloudiness, OilCoverage, Smoothness, SteamIntensity;
        public readonly int OilDrops;

        /// <summary>正規化済みの外観値を記録します。</summary>
        internal SoupAppearanceParameters(string soupId, int spice, float cloudiness, float oilCoverage, float smoothness, float steamIntensity, int oilDrops)
        {
            SoupId = soupId; Spice = spice; Cloudiness = cloudiness; OilCoverage = oilCoverage;
            Smoothness = smoothness; SteamIntensity = steamIntensity; OilDrops = oilDrops;
        }
    }

    /// <summary>薬味の形状を表します。液体は薄い膜、固体は小さな立体として描画します。</summary>
    public enum GarnishShape { Oil, Sauce, Vinegar, Scallion, Garlic, Coriander, Peanut, Pepper }

    /// <summary>薬味一回分の形状、個数と相対寸法です。</summary>
    public readonly struct GarnishAppearanceParameters
    {
        public readonly GarnishShape Shape;
        public readonly int Count;
        public readonly float Size;

        /// <summary>薬味の固定寸法を記録します。</summary>
        internal GarnishAppearanceParameters(GarnishShape shape, int count, float size)
        { Shape = shape; Count = count; Size = size; }
    }

    /// <summary>円盤内の薬味一粒の位置・回転・大きさです。</summary>
    public readonly struct GarnishPlacement
    {
        public readonly float X, Z, Angle, Scale;
        /// <summary>配置値を記録します。</summary>
        internal GarnishPlacement(float x, float z, float angle, float scale)
        { X = x; Z = z; Angle = angle; Scale = scale; }
    }

    /// <summary>注文の外観を、ゲーム状態を変更せず決定論的に計算します。</summary>
    public static class SoupAppearance
    {
        /// <summary>辛さを0〜5に制限し、六種のスープの描き分け値を返します。</summary>
        public static SoupAppearanceParameters ForSoup(string soupId, int spice)
        {
            SoupDefinition soup = Content.Soup(soupId);
            int level = Math.Max(0, Math.Min(5, spice));
            float cloudiness = soup.Id == "baitang" ? .94f : soup.Id == "tomato" ? .72f
                : soup.Id == "dry" ? .84f : soup.Id == "mala" ? .48f : soup.Id == "sour" ? .28f : .2f;
            float baseOil = soup.Id == "mala" ? .18f : soup.Id == "dry" ? .12f : .035f;
            return new SoupAppearanceParameters(soup.Id, level, cloudiness, baseOil + level * .065f,
                soup.Id == "dry" ? .42f : .58f, soup.Id == "dry" ? .12f : .38f, soup.Id == "mala" ? 100 + level * 12 : 8 + level * 7);
        }

        /// <summary>定義済みの薬味IDに対応する形状と粒数を返します。未知のIDは受け付けません。</summary>
        public static GarnishAppearanceParameters ForGarnish(string id)
        {
            switch (id)
            {
                case "chili_oil": return new GarnishAppearanceParameters(GarnishShape.Oil, 12, .023f);
                case "sesame_paste": return new GarnishAppearanceParameters(GarnishShape.Sauce, 5, .055f);
                case "vinegar": return new GarnishAppearanceParameters(GarnishShape.Vinegar, 4, .06f);
                case "scallion": return new GarnishAppearanceParameters(GarnishShape.Scallion, 18, .014f);
                case "garlic": return new GarnishAppearanceParameters(GarnishShape.Garlic, 16, .014f);
                case "coriander": return new GarnishAppearanceParameters(GarnishShape.Coriander, 9, .025f);
                case "peanut": return new GarnishAppearanceParameters(GarnishShape.Peanut, 14, .02f);
                case "sichuan_pepper": return new GarnishAppearanceParameters(GarnishShape.Pepper, 16, .01f);
                default: throw new ArgumentException("未定義の薬味です。", nameof(id));
            }
        }

        /// <summary>形状の外接半径を見込み、粒全体が液面内に収まる位置と倍率を返します。</summary>
        public static GarnishPlacement ScatterGarnish(Random random, float radius, GarnishAppearanceParameters appearance)
        {
            GarnishPlacement candidate = Scatter(random, radius);
            float unitFootprint = GarnishFootprint(appearance, 1f);
            float scale = Math.Min(candidate.Scale, radius / unitFootprint);
            float centerRadius = Math.Max(0f, radius - GarnishFootprint(appearance, scale));
            float ratio = radius > 0f ? centerRadius / radius : 0f;
            return new GarnishPlacement(candidate.X * ratio, candidate.Z * ratio, candidate.Angle, scale);
        }

        /// <summary>任意のY軸回転でも形状が収まるXZ外接半径を返します。葉の根元原点と液体の長軸も含みます。</summary>
        public static float GarnishFootprint(GarnishAppearanceParameters appearance, float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0f) throw new ArgumentOutOfRangeException(nameof(scale));
            if (appearance.Size <= 0f) throw new ArgumentOutOfRangeException(nameof(appearance));
            float factor = appearance.Shape == GarnishShape.Sauce || appearance.Shape == GarnishShape.Vinegar ? 1.8f
                : appearance.Shape == GarnishShape.Coriander ? 1.4f : 1f;
            return appearance.Size * scale * factor;
        }

        /// <summary>半径の二乗に一様な面積分布で配置します。専用乱数を使うためゲームの乱数列を変更しません。</summary>
        public static GarnishPlacement Scatter(Random random, float radius)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f) throw new ArgumentOutOfRangeException(nameof(radius));
            double angle = random.NextDouble() * Math.PI * 2d;
            double distance = Math.Sqrt(random.NextDouble()) * radius;
            return new GarnishPlacement((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance),
                (float)(random.NextDouble() * 360d), .72f + (float)random.NextDouble() * .55f);
        }
    }
}
