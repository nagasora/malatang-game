using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>
    /// 「一杯アルバム」。品質の高い一杯を、中身（具材・スープ・味付け）ごと記録して図鑑で見返せるようにする。
    /// 品質の高い順に Limit 杯だけ残す。
    /// </summary>
    public static class BowlAlbum
    {
        public const int Limit = 12;
        /// <summary>アルバムに載る品質の下限。</summary>
        public const float MinQuality = 85;

        /// <summary>提供した一杯をアルバムに載せる（下限未満や、上位に入らなければ何もしない）。載ったら true。</summary>
        public static bool Record(GameState s, CustomerOrder o)
        {
            if (o.quality < MinQuality || o.selected.Count == 0 || string.IsNullOrEmpty(o.chosenSoup)) return false;
            var entry = new AlbumEntry
            {
                day = s.day, customerName = o.customerName ?? "", archetype = o.archetype ?? "", soup = o.chosenSoup, spice = o.chosenSpice,
                quality = (int)Math.Round(o.quality), score = o.quality, seq = ++s.albumSeq, recipe = o.recipe ?? "", omakase = o.omakase,
                ingredients = o.selected.ToList(), condiments = o.condiments.ToList(), pairings = o.pairings.ToList(), garnishStrokes = GarnishStroke.Copy(o.garnishStrokes),
            };
            s.album.Add(entry);
            Sort(s);
            return s.album.Contains(entry);
        }

        /// <summary>
        /// 品質（丸める前の値）の高い順に並べ、上位 Limit 杯だけ残す。
        /// 同点なら新しい一杯を上にする（同じ点の古い記録を残すより、最近の腕前を見せたいため）。
        /// </summary>
        public static void Sort(GameState s)
        {
            var ordered = s.album.OrderByDescending(e => e.score).ThenByDescending(e => e.day).ThenByDescending(e => e.seq).Take(Limit).ToList();
            s.album.Clear();
            s.album.AddRange(ordered);
        }

        /// <summary>保存データの記録として成り立っているか（品質の下限・具材・既知のスープ）。</summary>
        public static bool IsValid(AlbumEntry e)
            => e != null && e.day > 0 && e.score >= MinQuality && e.score <= 100 && e.ingredients != null && e.ingredients.Count > 0
                && Content.Soups.Any(x => x.Id == e.soup);

        /// <summary>アルバムの一杯の見出し（隠しレシピ名か、スープ名と主な具材）。</summary>
        public static string Title(AlbumEntry e, Func<string, string> ingredientName)
        {
            var recipe = Recipes.Find(e.recipe);
            if (recipe != null) return recipe.Name;
            string main = e.ingredients.Count == 0 ? "" : ingredientName(e.ingredients[0]);
            return Content.Soup(e.soup).Name + (main.Length > 0 ? "・" + main + "入り" : "");
        }
    }

    /// <summary>アルバムに載せた一杯の記録。</summary>
    [Serializable] public sealed class AlbumEntry
    {
        public int day;
        public string customerName = "", archetype = "", soup = "", recipe = "";
        public int spice;
        /// <summary>表示用の品質（整数に丸めた値）。</summary>
        public int quality;
        /// <summary>順位づけに使う丸める前の品質。</summary>
        public float score;
        /// <summary>記録した順の通し番号（同じ日・同点の新旧を決める）。</summary>
        public long seq;
        public bool omakase;
        public List<string> ingredients = new List<string>();
        public List<string> condiments = new List<string>();
        /// <summary>入っていた相性の組み合わせ ID。</summary>
        public List<string> pairings = new List<string>();
        public List<GarnishStroke> garnishStrokes = new List<GarnishStroke>();
    }
}
