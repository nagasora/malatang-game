using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>隠しレシピ（相性のよい具材・スープ・味付けの組み合わせ）。</summary>
    public sealed class RecipeDefinition
    {
        public string Id, Name, Description, Hint;
        /// <summary>必要なスープ（空ならどのスープでもよい）。</summary>
        public string Soup = "";
        /// <summary>すべて入っている必要がある具材。</summary>
        public string[] Ingredients = Array.Empty<string>();
        /// <summary>入っている必要がある調味料。</summary>
        public string[] Condiments = Array.Empty<string>();
        public int MinSpice, MaxSpice = 5;
        /// <summary>品質への加点。</summary>
        public float Bonus = 5;
        /// <summary>伝説のレシピか（図鑑で金色に表示）。</summary>
        public bool Legendary;
    }

    /// <summary>
    /// 隠しレシピの判定と発見。完成した一杯が組み合わせに当てはまると品質が上がり、
    /// 初めて作ったときにレシピ帳へ載る。見つけたレシピは客が名前で注文するようになる。
    /// </summary>
    public static class Recipes
    {
        public static readonly RecipeDefinition[] All =
        {
            new RecipeDefinition { Id = "tomato_egg", Name = "トマたま麻辣湯", Soup = "tomato", Ingredients = new[] { "egg", "cherry_tomato" }, MaxSpice = 2,
                Description = "甘酸っぱいトマトに卵がとろける、家庭の味。", Hint = "トマトのスープに、卵と小さなトマトを重ねると…（辛さ控えめ）" },
            new RecipeDefinition { Id = "beef_mala", Name = "牛づくし麻辣", Soup = "mala", Ingredients = new[] { "beef_slice", "beef_tripe" }, MinSpice = 3,
                Description = "牛肉とセンマイを麻辣でしっかり辛く。", Hint = "麻辣スープで、牛の肉と胃袋を辛さ3以上で。" },
            new RecipeDefinition { Id = "mushroom_forest", Name = "きのこの森", Soup = "mushroom", Ingredients = new[] { "enoki", "shimeji", "king_oyster" },
                Description = "三種のきのこで旨味が重なる一杯。", Hint = "きのこのスープに、きのこを三種類。" },
            new RecipeDefinition { Id = "seafood", Name = "海鮮三昧", Ingredients = new[] { "shrimp", "squid", "clam" },
                Description = "エビ・イカ・アサリの出汁がスープに溶け出す。", Hint = "海のものを三つ。エビと、イカと、貝。" },
            new RecipeDefinition { Id = "tofu_trio", Name = "豆腐づくし", Ingredients = new[] { "silken_tofu", "firm_tofu", "yuba" }, MaxSpice = 3,
                Description = "やわらかい・かたい・ゆばの三重奏。ベジタリアンにも。", Hint = "豆腐を固さ違いで二つと、湯葉。" },
            new RecipeDefinition { Id = "green_garden", Name = "緑の菜園", Soup = "baitang", Ingredients = new[] { "bok_choy", "spinach", "broccoli" },
                Description = "白湯に青菜がたっぷり。体が喜ぶ。", Hint = "白湯に、青い野菜を三つ。チンゲン菜、ほうれん草、ブロッコリー。" },
            new RecipeDefinition { Id = "oden", Name = "おでん風", Soup = "baitang", Ingredients = new[] { "chikuwa", "hanpen", "kombu" }, MaxSpice = 1,
                Description = "練り物と昆布で、どこか懐かしい。", Hint = "白湯に、ちくわとはんぺん、それに昆布。辛くしないで。" },
            new RecipeDefinition { Id = "sesame_noodle", Name = "ごまだれ拌麺", Soup = "dry", Ingredients = new[] { "chinese_noodle" }, Condiments = new[] { "sesame_paste", "peanut" },
                Description = "汁なしの麺にごまだれとピーナッツを絡める。", Hint = "汁なしで中華麺を。ごまだれと砕きピーナッツを忘れずに。" },
            new RecipeDefinition { Id = "chongqing", Name = "重慶の夜", Soup = "mala", Ingredients = new[] { "duck_blood", "beef_tripe" }, Condiments = new[] { "sichuan_pepper" }, MinSpice = 5, Bonus = 7,
                Description = "鴨血とセンマイ、花椒、辛さ5。本場の夜の味。", Hint = "いちばん辛い麻辣に、鴨の血とセンマイ、花椒。" },
            new RecipeDefinition { Id = "sour_fish", Name = "酸菜つみれ", Soup = "sour", Ingredients = new[] { "fish_ball", "napa_cabbage" },
                Description = "酸っぱいスープに魚のつみれがよく合う。", Hint = "酸菜のスープで、魚のつみれと白菜を。" },
            new RecipeDefinition { Id = "lamb_baitang", Name = "羊肉の白湯", Soup = "baitang", Ingredients = new[] { "lamb", "napa_cabbage" }, Condiments = new[] { "coriander" },
                Description = "羊肉と白菜を白湯で。香菜が決め手。", Hint = "白湯に羊肉と白菜。香りの強い葉をのせて。" },
            new RecipeDefinition { Id = "kids_plate", Name = "お子さまセット", Soup = "tomato", Ingredients = new[] { "quail_egg", "meat_ball", "baby_corn" }, MaxSpice = 0,
                Description = "うずら卵と肉団子とヤングコーン。子どもが大喜び。", Hint = "辛くないトマトに、小さな卵と肉団子と小さなとうもろこし。" },
            new RecipeDefinition { Id = "dumpling_party", Name = "包みもの祭り", Ingredients = new[] { "dumpling", "wonton" },
                Description = "水餃子とワンタン、包みもの好きにはたまらない。", Hint = "包んだものを二種類。" },
            new RecipeDefinition { Id = "stamina", Name = "スタミナ麺", Ingredients = new[] { "pork_belly", "egg", "chinese_noodle" }, Condiments = new[] { "garlic" },
                Description = "豚バラと卵と麺、にんにく。明日もがんばれる。", Hint = "豚バラ、卵、中華麺。にんにくで元気に。" },
            new RecipeDefinition { Id = "sea_greens", Name = "海藻の滋味", Ingredients = new[] { "wakame", "kombu" }, MaxSpice = 2,
                Description = "わかめと昆布の、やさしい磯の香り。", Hint = "海の草を二つ。辛さは控えめに。" },
            new RecipeDefinition { Id = "golden", Name = "黄金の麻辣湯", Soup = "mala", Ingredients = new[] { "scallop", "shrimp_ball", "crab_ball" }, MinSpice = 2, Bonus = 9, Legendary = true,
                Description = "ホタテとエビ団子とカニ団子。伝説と呼ばれる一杯。", Hint = "麻辣に、海の宝を三つ丸めたもの…ホタテと、エビとカニの団子。" },
        };

        /// <summary>ID からレシピを探す（無ければ null）。</summary>
        public static RecipeDefinition Find(string id) => All.FirstOrDefault(r => r.Id == id);

        /// <summary>一杯の中身がレシピに当てはまるか。</summary>
        public static bool Matches(RecipeDefinition r, CustomerOrder o)
        {
            if (!string.IsNullOrEmpty(r.Soup) && o.chosenSoup != r.Soup) return false;
            if (o.chosenSpice < r.MinSpice || o.chosenSpice > r.MaxSpice) return false;
            return r.Ingredients.All(o.selected.Contains) && r.Condiments.All(o.condiments.Contains);
        }

        /// <summary>当てはまるレシピのうち、もっとも条件の多いもの（無ければ null）。</summary>
        public static RecipeDefinition Match(CustomerOrder o)
            => All.Where(r => Matches(r, o)).OrderByDescending(r => r.Ingredients.Length + r.Condiments.Length + (string.IsNullOrEmpty(r.Soup) ? 0 : 1)).FirstOrDefault();

        /// <summary>見つけたレシピか。</summary>
        public static bool Discovered(GameState s, string id) => s.recipes.Any(r => r.id == id);

        /// <summary>
        /// 完成した一杯にレシピの加点を付ける。品質は 100 を超えない。
        /// 頼まれた具材と違う組み合わせでも、偶然の発見として加点する。
        /// </summary>
        public static void ApplyBonus(CustomerOrder o)
        {
            var recipe = Match(o);
            o.recipe = recipe?.Id ?? "";
            if (recipe == null) return;
            o.report.total = Math.Min(100, o.report.total + recipe.Bonus);
            o.report.comments.Insert(0, "これは…「" + recipe.Name + "」の味！");
        }

        /// <summary>提供時の記録。初めてなら発見として知らせる。</summary>
        public static void RecordServed(RestaurantSimulation sim, CustomerOrder o)
        {
            var recipe = Find(o.recipe);
            if (!string.IsNullOrEmpty(o.recipeRequest) && o.recipe == o.recipeRequest) o.serviceBonus += 5;
            if (recipe == null) return;
            var s = sim.State;
            bool first = !Discovered(s, recipe.Id);
            LifetimeStats.Add(s.recipes, recipe.Id);
            s.stats.recipeBowls++;
            if (!first) return;
            s.report.highlights.Add("隠しレシピ発見：「" + recipe.Name + "」");
            sim.Emit(SimEventKind.RecipeDiscovered, recipe.Id, 0, "隠しレシピ発見！「" + recipe.Name + "」");
        }

        /// <summary>
        /// 客が名前で頼めるレシピ（発見済みで、具材が解禁・在庫ありで、スープが使え、客の好みに合うもの）。
        /// </summary>
        public static List<RecipeDefinition> Orderable(RestaurantSimulation sim, CustomerArchetype archetype)
        {
            var s = sim.State;
            var soups = Content.Soups.Where(x => x.UnlockLevel <= s.shopLevel).Select(x => x.Id).ToList();
            var condiments = Content.Condiments.Where(x => x.UnlockLevel <= s.shopLevel).Select(x => x.Id).ToList();
            return All.Where(r => Discovered(s, r.Id)
                    && (string.IsNullOrEmpty(r.Soup) || soups.Contains(r.Soup))
                    && r.Condiments.All(condiments.Contains)
                    && !r.Condiments.Any(archetype.DislikedCondiments.Contains)
                    && r.Ingredients.All(id => sim.Unlocks.IsNormalUnlocked(id) && sim.Available(id) > 0
                        && !archetype.ForbiddenCategories.Contains(sim.Catalog.Get(id).category))
                    && SpiceFor(r, archetype) >= 0)
                .ToList();
        }

        /// <summary>レシピと客タイプの両方を満たす辛さ（両立しなければ -1）。</summary>
        public static int SpiceFor(RecipeDefinition r, CustomerArchetype archetype)
        {
            int min = Math.Max(r.MinSpice, archetype.Child ? 0 : archetype.MinSpice);
            int max = Math.Min(r.MaxSpice, archetype.Child ? 0 : archetype.MaxSpice);
            return min <= max ? min : -1;
        }
    }
}
