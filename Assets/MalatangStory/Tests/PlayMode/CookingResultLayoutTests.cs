using System.Reflection;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>完成カードの写真拡大と概要の縦積みを検証します。</summary>
namespace MalatangStory.Tests
{
    /// <summary>表示幅によらず写真がカードの利用可能幅を使うことを保証します。</summary>
    public sealed class CookingResultLayoutTests
    {
        /// <summary>写真は正方形のまま横幅を使い、説明文が写真に重なりません。</summary>
        [TestCase(300f)]
        [TestCase(438f)]
        [TestCase(600f)]
        public void PortraitUsesAvailableWidthAndPlacesSummaryBelow(float width)
        {
            GameObject host = new GameObject("Result Layout Fixture", typeof(RectTransform));
            try
            {
                RectTransform overview = host.GetComponent<RectTransform>();
                overview.sizeDelta = new Vector2(width, 200f);
                RawImage portrait = UIKit.Rect("Bowl Portrait", overview).gameObject.AddComponent<RawImage>();
                Text stars = UIKit.Rect("Stars", overview).gameObject.AddComponent<Text>();
                Text recipe = UIKit.Rect("Recipe", overview).gameObject.AddComponent<Text>();
                Text pairings = UIKit.Rect("Pairings", overview).gameObject.AddComponent<Text>();
                CookingScreen screen = new CookingScreen();
                SetField(screen, "resultOverview", overview);
                SetField(screen, "resultPortrait", portrait);
                SetField(screen, "resultStars", stars);
                SetField(screen, "resultRecipe", recipe);
                SetField(screen, "resultPairings", pairings);
                MethodInfo layout = typeof(CookingScreen).GetMethod("LayoutResultOverview", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(layout);
                layout.Invoke(screen, null);

                Assert.AreEqual(width, portrait.rectTransform.rect.width, .01f);
                Assert.AreEqual(width, portrait.rectTransform.rect.height, .01f);
                Assert.AreEqual(width + 152f, overview.GetComponent<LayoutElement>().preferredHeight, .01f);
                Assert.LessOrEqual(stars.rectTransform.offsetMax.y, -width);
                Assert.Less(recipe.rectTransform.offsetMax.y, stars.rectTransform.offsetMin.y);
                Assert.Less(pairings.rectTransform.offsetMax.y, recipe.rectTransform.offsetMin.y);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>製品コードの公開範囲を広げずに表示用参照のみ注入します。</summary>
        private static void SetField(CookingScreen screen, string name, object value)
        {
            FieldInfo field = typeof(CookingScreen).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, name);
            field.SetValue(screen, value);
        }
    }
}
