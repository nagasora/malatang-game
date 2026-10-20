using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>具材の食感。</summary>
    public enum FoodTexture { Crisp, Chewy, Bouncy, Fluffy, Silky, Crunchy, Starchy, Tender }

    /// <summary>具材の色（彩りの判定に使う色の系統）。</summary>
    public enum FoodColor { Green, Red, White, Yellow, Brown, Black, Orange }

    /// <summary>具材の栄養の役割。</summary>
    public enum FoodRole { Vegetable, Protein, Staple }

    /// <summary>具材ひとつの特徴（食感・色・栄養の役割・ひとこと）。</summary>
    public readonly struct FoodTraits
    {
        public readonly FoodTexture Texture;
        public readonly FoodColor Color;
        public readonly FoodRole Role;
        /// <summary>選ぶときに見せる短い説明（味と食べごたえ）。</summary>
        public readonly string Note;

        public FoodTraits(FoodTexture texture, FoodColor color, FoodRole role, string note)
        {
            Texture = texture; Color = color; Role = role; Note = note;
        }
    }

    /// <summary>
    /// 具材の特徴の一覧と、一杯の「バランス」（彩り・食感・栄養）の評価。
    /// 麻辣湯は好きな具材を自由に組み合わせる料理なので、組み合わせの良し悪しを数値と言葉で見せる。
    /// </summary>
    public static class FoodProfile
    {
        private static readonly Dictionary<string, FoodTraits> Traits = new Dictionary<string, FoodTraits>(StringComparer.Ordinal)
        {
            ["matsutake"] = T(FoodTexture.Tender, FoodColor.Brown, FoodRole.Vegetable, "香り豊かな希少きのこ。朝市の特別な一品"),
            ["black_truffle"] = T(FoodTexture.Crunchy, FoodColor.Black, FoodRole.Vegetable, "黒い表皮の奥に広がる芳醇な香り"),
            ["jinhua_ham"] = T(FoodTexture.Tender, FoodColor.Red, FoodRole.Protein, "熟成した肉と脂の豊かな旨味"),
            ["wagyu"] = T(FoodTexture.Tender, FoodColor.Red, FoodRole.Protein, "細かな霜降りがとろける贅沢な牛肉"),
            ["kuruma_shrimp"] = T(FoodTexture.Bouncy, FoodColor.Orange, FoodRole.Protein, "大ぶりの身に詰まった甘い旨味"),
            ["abalone"] = T(FoodTexture.Crunchy, FoodColor.Brown, FoodRole.Protein, "肉厚で磯の香りが広がる"),
            ["enoki"] = T(FoodTexture.Crisp, FoodColor.White, FoodRole.Vegetable, "シャキシャキ。スープをよく吸う"),
            ["king_oyster"] = T(FoodTexture.Bouncy, FoodColor.White, FoodRole.Vegetable, "コリッと肉厚。食べごたえあり"),
            ["shimeji"] = T(FoodTexture.Crisp, FoodColor.Brown, FoodRole.Vegetable, "プリッとして香りがいい"),
            ["maitake"] = T(FoodTexture.Crisp, FoodColor.Brown, FoodRole.Vegetable, "香り高く、旨味がスープに出る"),
            ["black_wood_ear"] = T(FoodTexture.Crunchy, FoodColor.Black, FoodRole.Vegetable, "コリコリ。黒が彩りを締める"),
            ["brown_enoki"] = T(FoodTexture.Crisp, FoodColor.Brown, FoodRole.Vegetable, "香ばしい茶色のえのき"),
            ["white_wood_ear"] = T(FoodTexture.Silky, FoodColor.White, FoodRole.Vegetable, "ぷるぷるで、ほんのり甘い"),
            ["sweet_potato_noodle"] = T(FoodTexture.Chewy, FoodColor.Brown, FoodRole.Staple, "太くてもっちり。麻辣の定番"),
            ["shirataki"] = T(FoodTexture.Silky, FoodColor.White, FoodRole.Staple, "つるつる低カロリー"),
            ["corn_noodle"] = T(FoodTexture.Chewy, FoodColor.Yellow, FoodRole.Staple, "黄色くてもちもち"),
            ["mung_bean_noodle"] = T(FoodTexture.Silky, FoodColor.White, FoodRole.Staple, "つるっと細い春雨"),
            ["chinese_noodle"] = T(FoodTexture.Chewy, FoodColor.Yellow, FoodRole.Staple, "しっかりお腹にたまる"),
            ["knife_cut_noodle"] = T(FoodTexture.Chewy, FoodColor.White, FoodRole.Staple, "幅広で歯ごたえ抜群"),
            ["wide_glass_noodle"] = T(FoodTexture.Silky, FoodColor.White, FoodRole.Staple, "平たくてのどごしがいい"),
            ["rice_noodle"] = T(FoodTexture.Silky, FoodColor.White, FoodRole.Staple, "やさしい米の麺"),
            ["duck_blood"] = T(FoodTexture.Silky, FoodColor.Red, FoodRole.Protein, "ぷるんとなめらか。本場の味"),
            ["chicken_feet"] = T(FoodTexture.Chewy, FoodColor.Yellow, FoodRole.Protein, "ぷるぷるのコラーゲン"),
            ["shrimp_ball"] = T(FoodTexture.Bouncy, FoodColor.Orange, FoodRole.Protein, "ぷりぷりのエビ団子"),
            ["crab_stick"] = T(FoodTexture.Fluffy, FoodColor.Red, FoodRole.Protein, "赤白がかわいい"),
            ["crab_ball"] = T(FoodTexture.Bouncy, FoodColor.Orange, FoodRole.Protein, "カニの香りの団子"),
            ["chikuwa"] = T(FoodTexture.Bouncy, FoodColor.Brown, FoodRole.Protein, "焼き目がおいしい"),
            ["fish_ball"] = T(FoodTexture.Bouncy, FoodColor.White, FoodRole.Protein, "弾力のある魚のつみれ"),
            ["tteokbokki"] = T(FoodTexture.Chewy, FoodColor.White, FoodRole.Staple, "もっちもちの餅"),
            ["hanpen"] = T(FoodTexture.Fluffy, FoodColor.White, FoodRole.Protein, "ふわふわでスープを吸う"),
            ["wonton"] = T(FoodTexture.Silky, FoodColor.Yellow, FoodRole.Protein, "つるんとした皮に肉あん"),
            ["dumpling"] = T(FoodTexture.Chewy, FoodColor.White, FoodRole.Protein, "肉汁たっぷりの水餃子"),
            ["meat_ball"] = T(FoodTexture.Bouncy, FoodColor.Brown, FoodRole.Protein, "ジューシーな肉団子"),
            ["youtiao"] = T(FoodTexture.Fluffy, FoodColor.Yellow, FoodRole.Staple, "スープに浸すとじゅわっ"),
            ["quail_egg"] = T(FoodTexture.Tender, FoodColor.White, FoodRole.Protein, "ころんと一口"),
            ["lamb"] = T(FoodTexture.Tender, FoodColor.Red, FoodRole.Protein, "香りの強いラム肉"),
            ["beef_offal"] = T(FoodTexture.Chewy, FoodColor.Brown, FoodRole.Protein, "旨味たっぷりのもつ"),
            ["beef_slice"] = T(FoodTexture.Tender, FoodColor.Red, FoodRole.Protein, "さっと茹でてやわらか"),
            ["beef_tripe"] = T(FoodTexture.Crunchy, FoodColor.White, FoodRole.Protein, "シャキッとした歯ざわり"),
            ["chicken_breast"] = T(FoodTexture.Tender, FoodColor.White, FoodRole.Protein, "あっさり高たんぱく"),
            ["chicken_thigh"] = T(FoodTexture.Tender, FoodColor.Brown, FoodRole.Protein, "ジューシーなもも肉"),
            ["pork_belly"] = T(FoodTexture.Tender, FoodColor.Red, FoodRole.Protein, "脂の甘みがとろける"),
            ["egg"] = T(FoodTexture.Tender, FoodColor.Yellow, FoodRole.Protein, "半熟にすると最高"),
            ["clam"] = T(FoodTexture.Bouncy, FoodColor.Brown, FoodRole.Protein, "出汁が出る貝"),
            ["squid"] = T(FoodTexture.Bouncy, FoodColor.White, FoodRole.Protein, "むっちり噛みごたえ"),
            ["shrimp"] = T(FoodTexture.Bouncy, FoodColor.Orange, FoodRole.Protein, "茹でると赤く色づく"),
            ["scallop"] = T(FoodTexture.Tender, FoodColor.White, FoodRole.Protein, "甘みの強い高級貝"),
            ["dried_tofu_strips"] = T(FoodTexture.Chewy, FoodColor.Yellow, FoodRole.Protein, "細切りで麺のよう"),
            ["silken_tofu"] = T(FoodTexture.Silky, FoodColor.White, FoodRole.Protein, "とろけるなめらかさ"),
            ["atsuage"] = T(FoodTexture.Fluffy, FoodColor.Brown, FoodRole.Protein, "外はこんがり中はふわり"),
            ["yuba"] = T(FoodTexture.Silky, FoodColor.Yellow, FoodRole.Protein, "やさしい大豆の風味"),
            ["soy_sheet"] = T(FoodTexture.Chewy, FoodColor.Yellow, FoodRole.Protein, "薄くて歯ごたえがある"),
            ["fuzhu"] = T(FoodTexture.Chewy, FoodColor.Yellow, FoodRole.Protein, "スープを吸ってじゅわっ"),
            ["firm_tofu"] = T(FoodTexture.Tender, FoodColor.White, FoodRole.Protein, "しっかりした豆腐"),
            ["aburaage"] = T(FoodTexture.Fluffy, FoodColor.Brown, FoodRole.Protein, "スープをたっぷり含む"),
            ["okra"] = T(FoodTexture.Silky, FoodColor.Green, FoodRole.Vegetable, "ねばねばで星形の断面"),
            ["burdock"] = T(FoodTexture.Crunchy, FoodColor.Brown, FoodRole.Vegetable, "香りと歯ごたえ"),
            ["potato"] = T(FoodTexture.Starchy, FoodColor.Yellow, FoodRole.Staple, "ほくほくで食べごたえ"),
            ["bok_choy"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "シャキッとみずみずしい"),
            ["cilantro"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "好き嫌いの分かれる香り"),
            ["broccoli"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "つぼみがスープをからめる"),
            ["spinach"] = T(FoodTexture.Silky, FoodColor.Green, FoodRole.Vegetable, "くたっと甘い"),
            ["cherry_tomato"] = T(FoodTexture.Silky, FoodColor.Red, FoodRole.Vegetable, "甘酸っぱい赤"),
            ["bean_sprout"] = T(FoodTexture.Crisp, FoodColor.White, FoodRole.Vegetable, "安くてシャキシャキ"),
            ["baby_corn"] = T(FoodTexture.Crunchy, FoodColor.Yellow, FoodRole.Vegetable, "コリッと甘い"),
            ["lettuce"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "さっと茹でるのがコツ"),
            ["lotus_root"] = T(FoodTexture.Crunchy, FoodColor.White, FoodRole.Vegetable, "シャクシャク。穴が楽しい"),
            ["wakame"] = T(FoodTexture.Silky, FoodColor.Green, FoodRole.Vegetable, "つるんと磯の香り"),
            ["kombu"] = T(FoodTexture.Chewy, FoodColor.Black, FoodRole.Vegetable, "旨味の出る昆布"),
            ["stem_lettuce"] = T(FoodTexture.Crunchy, FoodColor.Green, FoodRole.Vegetable, "コリコリの山くらげ"),
            ["crown_daisy"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "ほろ苦い大人の香り"),
            ["komatsuna"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "クセがなく食べやすい"),
            ["mizuna"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "細い茎がシャキシャキ"),
            ["pea_shoot"] = T(FoodTexture.Crisp, FoodColor.Green, FoodRole.Vegetable, "豆の香りの若芽"),
            ["napa_cabbage"] = T(FoodTexture.Silky, FoodColor.White, FoodRole.Vegetable, "煮るほど甘くとろける"),
        };

        private static FoodTraits T(FoodTexture texture, FoodColor color, FoodRole role, string note) => new FoodTraits(texture, color, role, note);

        /// <summary>具材の特徴（未登録はカテゴリから推定）。</summary>
        public static FoodTraits Of(IngredientDefinition item)
        {
            if (item != null && Traits.TryGetValue(item.id, out var traits)) return traits;
            string category = item?.category ?? "";
            var role = category == "noodle" ? FoodRole.Staple : category == "vegetable" || category == "mushroom" ? FoodRole.Vegetable : FoodRole.Protein;
            var color = category == "vegetable" ? FoodColor.Green : category == "meat" ? FoodColor.Red : FoodColor.White;
            return new FoodTraits(category == "noodle" ? FoodTexture.Chewy : FoodTexture.Tender, color, role, item?.categoryLabel ?? "");
        }

        /// <summary>食感の名前。</summary>
        public static string TextureName(FoodTexture texture)
        {
            switch (texture)
            {
                case FoodTexture.Crisp: return "シャキシャキ";
                case FoodTexture.Chewy: return "もちもち";
                case FoodTexture.Bouncy: return "ぷりぷり";
                case FoodTexture.Fluffy: return "ふわふわ";
                case FoodTexture.Silky: return "つるとろ";
                case FoodTexture.Crunchy: return "コリコリ";
                case FoodTexture.Starchy: return "ほくほく";
                default: return "やわらか";
            }
        }

        /// <summary>食感の短い名前（具材の札に収まる2文字）。</summary>
        public static string TextureShort(FoodTexture texture)
        {
            switch (texture)
            {
                case FoodTexture.Crisp: return "シャキ";
                case FoodTexture.Chewy: return "もち";
                case FoodTexture.Bouncy: return "ぷり";
                case FoodTexture.Fluffy: return "ふわ";
                case FoodTexture.Silky: return "つる";
                case FoodTexture.Crunchy: return "コリ";
                case FoodTexture.Starchy: return "ほく";
                default: return "やわ";
            }
        }

        /// <summary>色の名前。</summary>
        public static string ColorName(FoodColor color)
        {
            switch (color)
            {
                case FoodColor.Green: return "緑";
                case FoodColor.Red: return "赤";
                case FoodColor.White: return "白";
                case FoodColor.Yellow: return "黄";
                case FoodColor.Brown: return "茶";
                case FoodColor.Black: return "黒";
                default: return "橙";
            }
        }

        /// <summary>種類の数から点数（1種類20点 … 5種類以上100点）。</summary>
        private static float VarietyScore(int kinds) => kinds <= 0 ? 0 : kinds == 1 ? 20 : kinds == 2 ? 45 : kinds == 3 ? 70 : kinds == 4 ? 88 : 100;

        /// <summary>一杯のバランスを評価する。</summary>
        public static BowlBalance Evaluate(IEnumerable<string> ids, IngredientCatalog catalog)
        {
            var traits = ids.Select(id => Of(catalog.Get(id))).ToList();
            var balance = new BowlBalance
            {
                Colors = traits.Select(t => t.Color).Distinct().OrderBy(c => c).ToList(),
                Textures = traits.Select(t => t.Texture).Distinct().OrderBy(t => t).ToList(),
                Vegetables = traits.Count(t => t.Role == FoodRole.Vegetable),
                Proteins = traits.Count(t => t.Role == FoodRole.Protein),
                Staples = traits.Count(t => t.Role == FoodRole.Staple),
            };
            balance.ColorScore = VarietyScore(balance.Colors.Count);
            balance.TextureScore = VarietyScore(balance.Textures.Count);
            int roles = (balance.Vegetables > 0 ? 1 : 0) + (balance.Proteins > 0 ? 1 : 0) + (balance.Staples > 0 ? 1 : 0);
            balance.NutritionScore = traits.Count == 0 ? 0 : roles == 3 ? 100 : roles == 2 ? 70 : 35;
            balance.Total = traits.Count == 0 ? 0 : (balance.ColorScore + balance.TextureScore + balance.NutritionScore) / 3f;
            return balance;
        }
    }

    /// <summary>一杯のバランスの評価結果。</summary>
    public sealed class BowlBalance
    {
        public List<FoodColor> Colors = new List<FoodColor>();
        public List<FoodTexture> Textures = new List<FoodTexture>();
        public int Vegetables, Proteins, Staples;
        public float ColorScore, TextureScore, NutritionScore, Total;

        /// <summary>足りないものをひとことで（全部そろっていれば空）。</summary>
        public string Advice()
        {
            if (Vegetables == 0) return "野菜やきのこを入れると栄養のバランスが良くなります";
            if (Proteins == 0) return "肉・魚介・豆腐などのたんぱく質を足しましょう";
            if (Staples == 0) return "麺やじゃがいもを入れるとお腹にたまります";
            if (Colors.Count < 3) return "色の違う具材を足すと見た目が華やかに";
            if (Textures.Count < 3) return "食感の違う具材を足すと食べていて楽しい";
            return "";
        }
    }
}
