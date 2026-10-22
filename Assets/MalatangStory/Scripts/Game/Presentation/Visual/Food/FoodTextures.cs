using System;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>食材の微細な構造を表すテクスチャ模様です。</summary>
    public enum FoodPattern { Mottled, Veins, Fibers, Pores, Marbling, Gradient, Toasted, Translucent, Florets }

    /// <summary>外部画像を使わず、128〜256pxのミップ付き食材色柄を生成します。</summary>
    public static class FoodTextures
    {
        /// <summary>多層ノイズに葉脈・繊維・孔・霜降りなどを重ねます。生成物は呼び出し元が所有します。</summary>
        public static Texture2D Create(string id, Color dark, Color light, FoodPattern pattern, int size = 128)
        {
            if (size != 128 && size != 256) throw new ArgumentOutOfRangeException(nameof(size));
            int seed = Seed(id); float offset = (seed & 65535) * .013f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
                { name = "Food Texture " + id, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            try
            {
                Color[] pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size; float v = (y + .5f) / size;
                    float n = Noise(u, v, offset); float value = .30f + n * .45f;
                    switch (pattern)
                    {
                        case FoodPattern.Veins:
                            float midrib = Mathf.Exp(-Mathf.Abs(u - .5f) * 95f);
                            float branchPhase = Mathf.Repeat(v * 7f - Mathf.Abs(u - .5f) * 3.4f, 1f);
                            float branches = Mathf.Exp(-Mathf.Min(branchPhase, 1f - branchPhase) * 65f) * (1f - Mathf.Abs(u - .5f));
                            value = Mathf.Max(value * (.7f + .3f * v), Mathf.Max(midrib, branches * .88f)); break;
                        case FoodPattern.Fibers:
                            value = .2f + n * .35f + .4f * Mathf.Pow(.5f + .5f * Mathf.Sin(u * 180f + n * 6f), 8f); break;
                        case FoodPattern.Pores:
                            float pore = Mathf.PerlinNoise(u * 48f + offset, v * 48f + offset);
                            value = pore > .66f ? .08f : .65f + n * .25f; break;
                        case FoodPattern.Marbling:
                            float fat = Mathf.Abs(Mathf.Sin(u * 18f + v * 9f + n * 14f));
                            value = fat < .23f ? .95f : n * .5f; break;
                        case FoodPattern.Gradient: value = Mathf.Clamp01(v * .65f + n * .3f); break;
                        case FoodPattern.Toasted: value = Mathf.Clamp01(n * 1.5f - .2f + Mathf.Sin(v * Mathf.PI) * .2f); break;
                        case FoodPattern.Translucent: value = .25f + n * .5f + v * .15f; break;
                        case FoodPattern.Florets:
                            float bumps = Mathf.Sin(u * 80f + n * 5f) * Mathf.Sin(v * 80f + n * 5f);
                            value = .15f + n * .45f + Mathf.Max(0f, bumps) * .4f; break;
                    }
                    pixels[y * size + x] = Color.Lerp(dark, light, Mathf.Clamp01(value));
                }
                texture.SetPixels(pixels); texture.Apply(true, true); return texture;
            }
            catch { OwnedObjects.Destroy(texture); throw; }
        }

        /// <summary>多層の連続ノイズを決定論的に合成します。</summary>
        private static float Noise(float u, float v, float offset)
        {
            return Mathf.PerlinNoise(u * 4f + offset, v * 4f + offset) * .55f
                + Mathf.PerlinNoise(u * 17f + offset, v * 17f + offset) * .3f
                + Mathf.PerlinNoise(u * 61f + offset, v * 61f + offset) * .15f;
        }

        /// <summary>プラットフォームに依存しないIDの乱数種を返します。</summary>
        public static int Seed(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            unchecked { int value = 17; foreach (char c in id) value = value * 31 + c; return value & int.MaxValue; }
        }

        /// <summary>24ビットRGBを不透明色に変換します。</summary>
        public static Color Rgb(uint color)
        {
            return new Color(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, 1f);
        }
    }
}
