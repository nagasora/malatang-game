using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>相性のよい具材の組み合わせ（2つの具材）。</summary>
    public sealed class PairingDefinition
    {
        public string Id, A, B, Name, Note;

        /// <summary>この組み合わせに具材 id が含まれるか。</summary>
        public bool Involves(string id) => A == id || B == id;

        /// <summary>id の相方（含まれなければ null）。</summary>
        public string PartnerOf(string id) => A == id ? B : B == id ? A : null;
    }

    /// <summary>
    /// 具材どうしの相性。隠しレシピより小さな単位の「合う組み合わせ」で、一杯に入っていると少し品質が上がる。
    /// 出したことのある組み合わせは図鑑に載り、選ぶ画面で相方の具材に印が付く。
    /// </summary>
    public static class Pairings
    {
        /// <summary>1組あたりの品質の加点。</summary>
        public const float BonusPerPair = 1.5f;
        /// <summary>加点の対象にする組の上限（具材を詰め込むほど得にならないように）。</summary>
        public const int MaxCounted = 3;

        public static readonly PairingDefinition[] All =
        {
            P("enoki", "beef_slice", "肥牛えのき", "牛の脂をえのきがたっぷり吸う"),
            P("potato", "pork_belly", "豚じゃが", "豚の脂でじゃがいもがほくほくに"),
            P("black_wood_ear", "egg", "木須の相棒", "キクラゲのコリコリと卵のふわふわ"),
            P("bean_sprout", "chicken_breast", "さっぱりコンビ", "淡白な鶏むねにシャキシャキを添えて"),
            P("spinach", "egg", "ほうれん草たま", "青菜に卵がやさしく絡む"),
            P("silken_tofu", "duck_blood", "紅白豆腐", "白い豆腐と赤い鴨血、本場の組み合わせ"),
            P("tteokbokki", "fish_ball", "屋台の練りもの", "もちもちとふわふわ、屋台の味"),
            P("youtiao", "fuzhu", "スープ吸い名人", "どちらもスープをたっぷり含む"),
            P("cherry_tomato", "shrimp", "トマトとエビ", "トマトの酸味がエビの甘さを引き立てる"),
            P("crown_daisy", "lamb", "春菊ラム", "春菊の香りがラムのくせを和らげる"),
            P("napa_cabbage", "meat_ball", "獅子頭風", "白菜が肉団子の旨味を吸う"),
            P("bok_choy", "shrimp_ball", "翡翠とエビ団子", "緑と橙で見た目も華やか"),
            P("okra", "shirataki", "ねばつる", "ねばねばとつるつるで喉ごし抜群"),
            P("lettuce", "chicken_thigh", "レタス鶏", "鶏ももの脂をレタスで軽やかに"),
            P("maitake", "chicken_thigh", "鶏まいたけ", "まいたけの香りが鶏に移る"),
            P("burdock", "aburaage", "ごぼう揚げ", "土の香りと油揚げのコク"),
            P("atsuage", "mizuna", "水菜厚揚げ", "香ばしい厚揚げにシャキッと水菜"),
            P("pea_shoot", "scallop", "豆苗ホタテ", "ホタテの甘みと豆苗の青さ"),
            P("stem_lettuce", "beef_offal", "コリコリ牛もつ", "山くらげの歯ごたえが牛もつに合う"),
            P("wonton", "komatsuna", "青菜ワンタン", "つるんとしたワンタンに青菜を添えて"),
            P("dumpling", "cilantro", "パクチー餃子", "パクチーの香りで餃子がさわやかに"),
            P("clam", "wakame", "あさりわかめ", "あさりの出汁をわかめが吸う"),
            P("squid", "broccoli", "イカブロッコリー", "白と緑、プリッとした歯ごたえ同士"),
            P("rice_noodle", "quail_egg", "過橋米線風", "雲南の米線にはうずら卵がつきもの"),
        };

        private static PairingDefinition P(string a, string b, string name, string note)
            => new PairingDefinition { Id = a + "+" + b, A = a, B = b, Name = name, Note = note };

        /// <summary>ID から組み合わせを探す（無ければ null）。</summary>
        public static PairingDefinition Find(string id) => All.FirstOrDefault(p => p.Id == id);

        /// <summary>具材の並びに入っている組み合わせ（定義順）。</summary>
        public static List<PairingDefinition> In(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids, StringComparer.Ordinal);
            return All.Where(p => set.Contains(p.A) && set.Contains(p.B)).ToList();
        }

        /// <summary>具材 id と相性のよい組み合わせ。</summary>
        public static IEnumerable<PairingDefinition> For(string id) => All.Where(p => p.Involves(id));

        /// <summary>出したことのある組み合わせか。</summary>
        public static bool Discovered(GameState s, string id) => s.pairings.Any(p => p.id == id);

        /// <summary>品質への加点（上限 MaxCounted 組）。</summary>
        public static float Bonus(int pairs) => Math.Min(MaxCounted, Math.Max(0, pairs)) * BonusPerPair;

        /// <summary>
        /// 完成した一杯に相性の加点を付けて記録する。品質は 100 を超えない。
        /// 隠しレシピのコメントがあれば、その次に相性のひとことを入れる。
        /// </summary>
        public static void ApplyBonus(CustomerOrder o)
        {
            var pairs = In(o.selected);
            o.pairings = pairs.Select(p => p.Id).ToList();
            if (pairs.Count == 0) return;
            o.report.total = Math.Min(100, o.report.total + Bonus(pairs.Count));
            int at = string.IsNullOrEmpty(o.recipe) ? 0 : Math.Min(1, o.report.comments.Count);
            o.report.comments.Insert(at, pairs.Count >= 2 ? "相性のいい具材ばかり。わかってるね！" : "「" + pairs[0].Name + "」、よく合うね！");
        }

        /// <summary>提供時の記録。初めて出した組み合わせは発見として知らせる。</summary>
        public static void RecordServed(RestaurantSimulation sim, CustomerOrder o)
        {
            var s = sim.State;
            foreach (var pair in o.pairings.Select(Find).Where(p => p != null))
            {
                bool first = !Discovered(s, pair.Id);
                LifetimeStats.Add(s.pairings, pair.Id);
                if (!first) continue;
                s.report.highlights.Add("相性発見：「" + pair.Name + "」");
                sim.Emit(SimEventKind.PairingDiscovered, pair.Id, 0, "相性発見！「" + pair.Name + "」" + pair.Note);
            }
        }
    }
}
