using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>
    /// 「おまかせ」注文。客は具材を指定せず、予算（＝重さの目安）・好きな食感・野菜多め・苦手なものだけを伝え、
    /// 店主が冷蔵ケースから自由に選ぶ。評価は重さの合い方・好みの満たし方・一杯のバランスで決まる。
    /// </summary>
    public static class Omakase
    {
        /// <summary>おまかせで注文しがちな客タイプと、その確率。</summary>
        private static readonly Dictionary<string, float> Chance = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["office"] = .08f, ["couple"] = .2f, ["tourist"] = .2f, ["foreigner"] = .3f, ["healthy"] = .22f, ["student"] = .1f,
            ["meatlover"] = .12f, ["worker"] = .1f, ["nurse"] = .15f, ["vegetarian"] = .25f, ["athlete"] = .15f,
            ["bigeater"] = .18f, ["celebrity"] = .2f, ["drunk"] = .15f,
        };

        /// <summary>苦手として伝えられるカテゴリと、そのせりふ。</summary>
        private static readonly (string category, string label)[] Avoidable =
        {
            ("seafood", "魚介"), ("other", "内臓や血"), ("processed", "練り物"), ("meat", "お肉"),
        };

        /// <summary>おまかせを頼むか（解禁は店舗レベル2から。常連・名物・レシピの注文は対象外）。</summary>
        public static bool Wants(GameState s, CustomerArchetype archetype, Random random)
        {
            if (!s.tutorialComplete || s.shopLevel < 2 || archetype.Child) return false;
            return Chance.TryGetValue(archetype.Id, out float chance) && random.NextDouble() < chance;
        }

        /// <summary>おまかせの好みを決めて注文に書き込む（具材の指定は空のまま）。</summary>
        public static void Fill(CustomerOrder o, CustomerArchetype archetype, IEnumerable<IngredientDefinition> stocked, int pricePer100gFen, Random random)
        {
            o.omakase = true;
            o.style = OrderStyle.Ordered;
            o.requested.Clear();
            float grams = archetype.Id == "bigeater" ? 520 + random.Next(0, 160) : archetype.Id == "athlete" ? 400 + random.Next(0, 100) : 260 + random.Next(0, 180);
            o.targetGrams = (float)Math.Round(grams / 10) * 10;
            o.budgetFen = RestaurantSimulation.RoundToJiao(o.targetGrams / 100d * pricePer100gFen);
            var avoid = Avoidable.Where(a => !archetype.ForbiddenCategories.Contains(a.category) && !(archetype.Id == "meatlover" && a.category == "meat")).ToList();
            o.avoidCategory = avoid.Count > 0 && random.NextDouble() < .35 ? avoid[random.Next(avoid.Count)].category : "";
            // 好きな食感は、禁止・苦手を除いた在庫にあるものから選ぶ（両立しない希望を出さないため）。
            var textures = stocked.Where(x => !archetype.ForbiddenCategories.Contains(x.category) && x.category != o.avoidCategory)
                .Select(x => FoodProfile.Of(x).Texture).Distinct().OrderBy(t => t).ToList();
            o.likeTexture = textures.Count == 0 ? -1 : (int)textures[random.Next(textures.Count)];
            o.wantVeg = archetype.Id == "healthy" || archetype.Id == "vegetarian" || random.NextDouble() < .3;
            o.type = "おまかせ";
        }

        /// <summary>苦手なカテゴリの呼び名。</summary>
        public static string AvoidLabel(string category) => Avoidable.FirstOrDefault(a => a.category == category).label ?? "";

        /// <summary>客の好みを短い一覧にする（注文票・調理画面用）。</summary>
        public static List<string> Wishes(CustomerOrder o)
        {
            var list = new List<string> { "予算 " + RestaurantSimulation.Money(o.budgetFen) + "（約" + o.targetGrams.ToString("0") + "g）" };
            if (o.likeTexture >= 0) list.Add(FoodProfile.TextureName((FoodTexture)o.likeTexture) + "が好き");
            if (o.wantVeg) list.Add("野菜たっぷり");
            if (!string.IsNullOrEmpty(o.avoidCategory)) list.Add(AvoidLabel(o.avoidCategory) + "は苦手");
            if (Content.Archetype(o.archetype).ForbiddenCategories.Length > 0) list.Add("肉・魚介・加工品は不可");
            return list;
        }

        /// <summary>重さの合い方（0〜100）。目安の±10%なら満点。</summary>
        public static float WeightFit(float grams, float target)
        {
            if (target <= 0) return 100;
            float off = Math.Abs(grams / target - 1);
            return off <= .1f ? 100 : off <= .25f ? 75 : off <= .4f ? 45 : 15;
        }

        /// <summary>好みの満たし方（0〜100）。</summary>
        public static float PreferenceFit(CustomerOrder o, IngredientCatalog catalog)
        {
            var items = o.selected.Select(catalog.Get).ToList();
            float score = 100;
            if (o.likeTexture >= 0)
            {
                int liked = items.Count(x => (int)FoodProfile.Of(x).Texture == o.likeTexture);
                score -= liked == 0 ? 45 : liked == 1 ? 15 : 0;
            }
            if (o.wantVeg) score -= Math.Max(0, 3 - items.Count(x => FoodProfile.Of(x).Role == FoodRole.Vegetable)) * 15;
            if (!string.IsNullOrEmpty(o.avoidCategory)) score -= Math.Min(80, items.Count(x => x.category == o.avoidCategory) * 40);
            return Math.Max(0, score);
        }

        /// <summary>おまかせの「具材」点（重さ35%・好み35%・バランス30%）。</summary>
        public static float Score(CustomerOrder o, IngredientCatalog catalog)
        {
            float grams = o.selected.Sum(x => catalog.Get(x).portionGrams);
            return .35f * WeightFit(grams, o.targetGrams) + .35f * PreferenceFit(o, catalog) + .30f * FoodProfile.Evaluate(o.selected, catalog).Total;
        }

        /// <summary>
        /// おまかせの品質の上限。重さが大きくずれた一杯や、苦手なものを入れた一杯は、他が完璧でも高評価にしない
        /// （予算を受け取ったまま量を減らす・希望を無視することが得にならないように）。
        /// </summary>
        public static float QualityCap(CustomerOrder o, IngredientCatalog catalog)
        {
            var items = o.selected.Select(catalog.Get).ToList();
            if (!string.IsNullOrEmpty(o.avoidCategory) && items.Any(x => x.category == o.avoidCategory)) return 60;
            float fit = WeightFit(items.Sum(x => x.portionGrams), o.targetGrams);
            return fit <= 15 ? 60 : fit <= 45 ? 72 : 100;
        }

        /// <summary>おまかせへの感想（良い点か、いちばん気になった点）。</summary>
        public static string Comment(CustomerOrder o, IngredientCatalog catalog)
        {
            var items = o.selected.Select(catalog.Get).ToList();
            float grams = items.Sum(x => x.portionGrams);
            if (!string.IsNullOrEmpty(o.avoidCategory) && items.Any(x => x.category == o.avoidCategory)) return AvoidLabel(o.avoidCategory) + "は苦手って言ったのに…";
            if (o.targetGrams > 0 && grams > o.targetGrams * 1.25f) return "予算のわりに量が多すぎるかな。";
            if (o.targetGrams > 0 && grams < o.targetGrams * .75f) return "おまかせにしては、ちょっと少ないな。";
            if (o.likeTexture >= 0 && !items.Any(x => (int)FoodProfile.Of(x).Texture == o.likeTexture)) return FoodProfile.TextureName((FoodTexture)o.likeTexture) + "の具材がほしかったな。";
            var balance = FoodProfile.Evaluate(o.selected, catalog);
            if (balance.Total >= 85) return "おまかせにして正解！彩りも食感も最高！";
            if (balance.Total >= 65) return "いい組み合わせ。センスあるね。";
            return "悪くないけど、ちょっと単調かな。";
        }

        /// <summary>
        /// スタッフ用の自動選択：希少食材を除き、好みと野菜・たんぱく質・麺の役割を順に満たしながら、目安の重さまで具材を足す。
        /// </summary>
        public static List<string> AutoPick(CustomerOrder o, IEnumerable<IngredientDefinition> candidates, Func<string, int> available)
        {
            var forbidden = Content.Archetype(o.archetype).ForbiddenCategories;
            var pool = candidates.Where(x => !x.isRare && available(x.id) > 0 && x.category != o.avoidCategory && !forbidden.Contains(x.category))
                .OrderByDescending(x => x.popularity).ThenBy(x => x.id, StringComparer.Ordinal).ToList();
            var pick = new List<string>();
            float grams = 0;
            void Take(IngredientDefinition item)
            {
                if (item == null || pick.Contains(item.id) || pick.Count >= RestaurantSimulation.MaxIngredientsPerBowl) return;
                pick.Add(item.id); grams += item.portionGrams;
            }
            if (o.wish == "noodles") foreach (var noodle in pool.Where(x => x.category == "noodle").Take(2)) Take(noodle);
            if (o.wish == "photogenic") foreach (var colour in pool.GroupBy(x => FoodProfile.Of(x).Color).Take(3)) Take(colour.First());
            if (o.likeTexture >= 0) Take(pool.FirstOrDefault(x => (int)FoodProfile.Of(x).Texture == o.likeTexture));
            foreach (var role in new[] { FoodRole.Vegetable, FoodRole.Protein, FoodRole.Staple })
                Take(pool.FirstOrDefault(x => FoodProfile.Of(x).Role == role && !pick.Contains(x.id)));
            if (o.wantVeg) foreach (var veg in pool.Where(x => FoodProfile.Of(x).Role == FoodRole.Vegetable && !pick.Contains(x.id)).Take(2).ToList()) Take(veg);
            foreach (var item in pool.Where(x => !pick.Contains(x.id)).ToList())
            {
                if (grams >= o.targetGrams * .95f) break;
                if (pick.Select(id => FoodProfile.Of(pool.First(p => p.id == id)).Color).Contains(FoodProfile.Of(item).Color) && pick.Count < 6) continue;
                Take(item);
            }
            foreach (var item in pool.Where(x => !pick.Contains(x.id)).ToList())
            {
                if (grams >= o.targetGrams * .9f) break;
                Take(item);
            }
            return pick;
        }
    }
}
