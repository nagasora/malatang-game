using MalatangStory.Game;
using NUnit.Framework;
namespace MalatangStory.Ingredients.Tests
{
    public sealed class LocalizedTextTableTests
    {
        [Test] public void EnglishCanOverrideAKeyAndMissingTranslationsFallBackToJapanese()
        {
            var table = new LocalizedTextTable(); table.Register("ja", "day", "{0}日目"); table.Register("en", "day", "Day {0}"); table.Register("ja", "greeting", "こんにちは");
            Assert.AreEqual("3日目", table.Format("day", 3)); table.Locale = "en";
            Assert.AreEqual("Day 3", table.Format("day", 3)); Assert.AreEqual("こんにちは", table.Text("greeting")); Assert.AreEqual("unknown", table.Text("unknown"));
            table.Locale = "missing"; Assert.AreEqual("3日目", table.Format("day", 3));
        }
        [Test] public void MalformedTemplatesRemainVisibleWithoutCrashingTheUI()
        {
            var table = new LocalizedTextTable(); table.Register("ja", "broken", "{0");
            Assert.AreEqual("{0", table.Format("broken", 3)); Assert.AreEqual("", table.Text(null));
        }
    }
}
