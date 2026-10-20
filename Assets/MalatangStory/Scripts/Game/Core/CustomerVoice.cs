using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>
    /// お客さんのせりふ（レジでの注文・支払い・訂正・食後の一言）。
    /// 客タイプごとに口調を変える。同じ注文からは常に同じせりふになるよう、注文 ID から乱数を作る。
    /// </summary>
    public static class CustomerVoice
    {
        private static Random For(CustomerOrder o, int salt) => new Random(unchecked(CustomerDirector.StableHash(o.id) * 31 + salt));

        private static string Pick(Random random, params string[] lines) => lines[random.Next(lines.Length)];

        /// <summary>レジに来た時のあいさつ。</summary>
        public static string Greeting(CustomerOrder o)
        {
            var r = For(o, 1);
            if (o.usual) return Pick(r, "やあ、店長！", "こんにちは、また来ちゃいました。", "どうも、今日もよろしく！");
            switch (o.archetype)
            {
                case "student": return Pick(r, "すみませーん！", "お願いしまーす！", "おなかすいたー！");
                case "elder": return Pick(r, "こんにちは。ええと…", "どうもどうも。", "ひとつお願いできるかね。");
                case "rider": return Pick(r, "急ぎでお願いします！", "すみません、急ぎで！");
                case "foreigner": return Pick(r, "Hi! (こんにちは！)", "Hello! First time here! (はじめて来ました！)");
                case "drunk": return Pick(r, "う〜い、しめの一杯ちょうだい…", "店長〜、やってる〜？");
                case "critic": return Pick(r, "……ひとつ、頼もうか。", "……注文を。");
                case "celebrity": return Pick(r, "（小声で）…ひとつお願いします。", "（帽子を深くかぶって）…どうも。");
                case "couple": return Pick(r, "ふたりで来ました！", "ここ、気になってたんだ〜。");
                case "worker": return Pick(r, "おう、腹ぺこだ！", "昼休みが短いんでね！");
                case "judge": return "麻辣湯グランプリの審査に参りました。";
                case "influencer": return Pick(r, "今日もカメラ回してまーす！", "映える一杯、お願いします！");
                case "nurse": return Pick(r, "夜勤明けで…あたたかいのを。", "ふう、やっと帰れる…");
                case "athlete": return Pick(r, "トレーニング帰りです！", "たんぱく質、多めで！");
                case "bigeater": return Pick(r, "いっぱい食べるよ〜！", "今日も山盛りで！");
                case "family": return Pick(r, "この子の分です。辛くしないでね。", "子ども用でお願いします。");
                default: return Pick(r, "すみません。", "お願いします。", "こんにちは！");
            }
        }

        /// <summary>辛さの言い方。</summary>
        public static string SpicePhrase(int spice)
        {
            switch (Math.Max(0, Math.Min(5, spice)))
            {
                case 0: return "辛さはなしで";
                case 1: return "ピリ辛（辛さ1）で";
                case 2: return "辛さ2で";
                case 3: return "中辛、辛さ3で";
                case 4: return "辛め、辛さ4で";
                default: return "激辛の5で！";
            }
        }

        /// <summary>スープの言い方。</summary>
        public static string SoupPhrase(string soupId)
        {
            var soup = Content.Soup(soupId);
            return soup.Id == "dry" ? "汁なしの麻辣拌" : soup.Name + "スープ";
        }

        /// <summary>注文のせりふ（吹き出しで順に出す）。</summary>
        public static List<string> OrderLines(CustomerOrder o, IngredientCatalog catalog)
        {
            var lines = new List<string>();
            bool english = o.archetype == "foreigner";
            if (o.usual)
            {
                lines.Add("いつもの、お願い！");
                lines.Add(o.takeout ? "今日は持ち帰りで。" : "ここで食べていきます。");
                return lines;
            }
            var recipe = Recipes.Find(o.recipeRequest);
            if (o.omakase)
            {
                lines.Add(english ? "Chef's choice, please! (おまかせで！)" : "具材はおまかせで！予算は" + RestaurantSimulation.Money(o.budgetFen) + "くらいで。");
                if (o.likeTexture >= 0) lines.Add(FoodProfile.TextureName((FoodTexture)o.likeTexture) + "した食感が好きなんです。");
                if (o.wantVeg) lines.Add("野菜はたっぷりでお願いします。");
                if (!string.IsNullOrEmpty(o.avoidCategory)) lines.Add(Omakase.AvoidLabel(o.avoidCategory) + "は苦手なので入れないでください。");
            }
            else if (recipe != null)
            {
                lines.Add(english ? "\"" + recipe.Name + "\", please! I saw it online. (ネットで見ました)" : "噂の「" + recipe.Name + "」、できますか？");
                lines.Add("具材は" + string.Join("、", o.requested.Select(id => catalog.Get(id).displayName)) + "ですよね。");
            }
            else if (!string.IsNullOrEmpty(o.signatureId)) lines.Add("名物の「" + o.type.Replace("名物「", "").TrimEnd('」') + "」をください！");
            else if (o.style == OrderStyle.SelfServe) lines.Add(english ? "I picked these! (具材はこれで！)" : "具材はこれでお願いします。");
            else
            {
                var names = o.requested.Select(id => catalog.Get(id).displayName).ToList();
                lines.Add("具材は" + string.Join("、", names) + "で。");
            }
            string soup = english ? SoupPhrase(o.soup) + " please! (" + SoupPhrase(o.soup) + "で)" : SoupPhrase(o.soup) + "、" + SpicePhrase(o.spice) + "。";
            if (english) { lines.Add(soup); lines.Add("Spicy level " + o.spice + ". (" + SpicePhrase(o.spice) + ")"); }
            else lines.Add(soup);
            var extras = new List<string>();
            extras.AddRange(o.requestedCondiments.Distinct().Select(id => Content.Condiment(id).Name + "多め"));
            extras.AddRange(o.dislikedCondiments.Where(id => id != Content.ChiliOil).Select(id => Content.Condiment(id).Name + "抜き"));
            if (extras.Count > 0) lines.Add(string.Join("、", extras) + "でお願いします。");
            if (Content.Archetype(o.archetype).ForbiddenCategories.Length > 0) lines.Add("お肉・魚介・加工品は食べられないので、気をつけてください。");
            lines.Add(o.takeout ? (english ? "To go! (持ち帰りで)" : "持ち帰りで！") : (english ? "For here. (店内で)" : "店内で食べます。"));
            return lines;
        }

        /// <summary>支払いのせりふ。</summary>
        public static string PaymentLine(CustomerOrder o)
        {
            switch (o.payment)
            {
                case PaymentMethod.QrCode: return o.archetype == "student" ? "QRで払いまーす！" : "QRコードでお願いします。";
                case PaymentMethod.Card: return o.archetype == "foreigner" ? "Card, please. (カードで)" : "カードで。";
                default:
                    if (o.tenderedFen == o.billFen) return "ちょうどあります。はい、どうぞ。";
                    return "じゃあ、" + RestaurantSimulation.Money(o.tenderedFen) + "で。";
            }
        }

        /// <summary>注文票の聞き間違いを訂正するせりふ。</summary>
        public static string TicketCorrection(CustomerOrder o, bool soupWrong, bool spiceWrong, bool takeoutWrong)
        {
            var r = For(o, 7 + o.registerMistakes);
            string lead = o.archetype == "drunk" ? "ちがうちがう〜、" : o.archetype == "elder" ? "おや、" : o.archetype == "rider" ? "違います、急いでるのに…" : Pick(r, "あ、違います。", "えっと、", "いや、");
            if (soupWrong) return lead + SoupPhrase(o.soup) + "って言いました。";
            if (spiceWrong) return lead + SpicePhrase(o.spice) + "って言いました。";
            if (takeoutWrong) return lead + (o.takeout ? "持ち帰りです。" : "店内で食べます。");
            return lead + "もう一度確認してください。";
        }

        /// <summary>おつりが足りない時のせりふ。</summary>
        public static string ChangeShort(CustomerOrder o)
            => o.archetype == "elder" ? "おや、おつりが少し足りないようじゃが…" : o.archetype == "foreigner" ? "Hmm, the change is short. (おつりが足りません)" : "あの、おつりが足りませんよ。";

        /// <summary>おつりが多すぎた時のせりふ。</summary>
        public static string ChangeOver(CustomerOrder o)
            => o.archetype == "elder" ? "多すぎるけど…まあ、ありがたくもらっておくよ。" : "あれ、多くない？…ラッキー！";

        /// <summary>会計が終わった時のせりふ。</summary>
        public static string ThankYou(CustomerOrder o)
        {
            var r = For(o, 3);
            if (o.archetype == "foreigner") return "Thank you! (ありがとう！)";
            if (o.archetype == "rider") return "できるだけ早くお願いします！";
            return Pick(r, "よろしくお願いします！", "楽しみにしてます。", "席で待ってますね。");
        }

        /// <summary>客の口調で完食・完飲と残食の理由を伝えます。</summary>
        public static string MealFinished(CustomerOrder order, MalatangStory.Ingredients.IngredientCatalog catalog)
        {
            string meal = MealLeftovers.Describe(order.leftovers, catalog);
            if (order.leftovers.FinishedSoup) meal += " スープも完飲！";
            else if (!string.IsNullOrEmpty(order.leftovers.soupReason)) meal += " スープは" + MealLeftovers.ReasonName(order.leftovers.soupReason) + "で残した。";
            switch (order.archetype)
            {
                case "elder": return meal + " ごちそうさまじゃ。";
                case "student": case "influencer": return meal + " ごちそうさまっ！";
                case "spicy": case "meatlover": return meal + " ごちそうさん！";
                case "foreigner": return meal + " Thank you!";
                case "critic": return meal + " 参考にしていただきたい。";
                default: return meal + " ごちそうさまでした。";
            }
        }

        /// <summary>希少食材を使った一杯についての食材名入りの感想。</summary>
        public static string RareIngredient(string name) => "珍しい" + name + "！特別な旨味がうれしい。";

        /// <summary>食後（提供時）の満足度に応じた一言。tip と discount は確定額。</summary>
        public static string Reaction(CustomerOrder o, long tipFen, long discountFen)
        {
            var r = For(o, 11);
            if (discountFen > 0)
                return o.report.satisfaction < 40
                    ? Pick(r, "これじゃお金は払えないよ。半分返して。", "ひどいな…ちょっと返してもらえる？")
                    : Pick(r, "うーん…ちょっと値引きしてくれる？", "期待してたんだけどな…少しまけて。");
            if (tipFen > 0)
                return o.archetype == "foreigner" ? "Delicious! Here's a tip! (おいしい！チップをどうぞ)"
                    : o.archetype == "celebrity" ? "（サングラスを外して）最高でした。これ、気持ちです。"
                    : Pick(r, "おいしかった！これ、取っておいて。", "最高！気持ちだけど受け取って！", "また来ます！おつりはいらないよ。");
            return o.report.satisfaction >= 60 ? Pick(r, "ごちそうさま！", "おいしかったです。") : Pick(r, "まあまあかな…", "うーん…");
        }
    }
}
