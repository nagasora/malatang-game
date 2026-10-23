using System;
using System.Collections.Generic;
using UnityEngine;
namespace MalatangStory.Game
{
    public static class UiText
    {
        [Serializable] private sealed class Entry { public string key, ja, en; }
        [Serializable] private sealed class Document { public Entry[] entries; }
        private static LocalizedTextTable table = new LocalizedTextTable();
        private static readonly Dictionary<string, string> sourceKeys = new Dictionary<string, string>();
        public static void Initialize(string locale = "ja")
        {
            table = new LocalizedTextTable { Locale = locale }; sourceKeys.Clear();
            var asset = Resources.Load<TextAsset>("Localization/Strings");
            if (asset == null) return;
            var document = JsonUtility.FromJson<Document>(asset.text);
            foreach (var entry in document?.entries ?? Array.Empty<Entry>())
            {
                table.Register("ja", entry.key, entry.ja); table.Register("en", entry.key, entry.en);
                if (!string.IsNullOrEmpty(entry.ja)) sourceKeys[entry.ja] = entry.key;
            }
        }
        public static string TranslateSource(string text) => text != null && sourceKeys.TryGetValue(text, out string key) ? table.Text(key) : text ?? "";
        public static string Format(string key, params object[] values)
        {
            for (int i = 0; i < values.Length; i++) if (values[i] is string text) values[i] = TranslateSource(text);
            return table.Format(key, values);
        }
    }
}
