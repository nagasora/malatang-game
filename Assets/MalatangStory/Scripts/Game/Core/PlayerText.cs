using System.Globalization;
using System.Text;

namespace MalatangStory.Game
{
    /// <summary>入力と保存復元で共用する、書記素単位の表示文字列の正規化。</summary>
    public static class PlayerText
    {
        public static string Clean(string value, int maximum = 16)
        {
            if (string.IsNullOrEmpty(value) || maximum <= 0) return "";
            var clean = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { clean.Append(c); clean.Append(value[++i]); }
                    continue;
                }
                if (char.IsLowSurrogate(c) || c == '<' || c == '>') continue;
                UnicodeCategory category = char.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format
                    || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator) continue;
                clean.Append(c);
            }
            string text = clean.ToString().Trim();
            int[] elements = StringInfo.ParseCombiningCharacters(text);
            return elements.Length <= maximum ? text : text.Substring(0, elements[maximum]);
        }
    }
}
