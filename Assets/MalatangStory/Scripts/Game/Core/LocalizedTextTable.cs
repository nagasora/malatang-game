using System.Collections.Generic;
using System.Globalization;
namespace MalatangStory.Game
{
    public sealed class LocalizedTextTable
    {
        private readonly Dictionary<string, Dictionary<string, string>> languages = new Dictionary<string, Dictionary<string, string>>();
        public string Locale = "ja";
        public void Register(string locale, string key, string text)
        {
            if (string.IsNullOrEmpty(locale) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return;
            if (!languages.TryGetValue(locale, out var entries)) languages[locale] = entries = new Dictionary<string, string>();
            entries[key] = text;
        }
        public string Text(string key)
        {
            if (key == null) return "";
            if (languages.TryGetValue(Locale ?? "ja", out var selected) && selected.TryGetValue(key, out string value)) return value;
            return languages.TryGetValue("ja", out var fallback) && fallback.TryGetValue(key, out value) ? value : key;
        }
        public string Format(string key, params object[] arguments)
        {
            string template = Text(key);
            try { return string.Format(CultureInfo.InvariantCulture, template, arguments); }
            catch (System.FormatException) { return template; }
        }
    }
}
