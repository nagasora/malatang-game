using System;
namespace MalatangStory.Game
{
    public readonly struct PhotoTone
    {
        public readonly byte R, G, B;
        public PhotoTone(float red, float green, float blue)
        { R = Byte(red); G = Byte(green); B = Byte(blue); }
        private static byte Byte(float value) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value)));
    }
    public static class PhotoFilter
    {
        public static PhotoTone Apply(byte r, byte g, byte b, int filter)
        {
            if (filter == 1) return new PhotoTone(r * .393f + g * .769f + b * .189f, r * .349f + g * .686f + b * .168f, r * .272f + g * .534f + b * .131f);
            if (filter == 2) { float gray = r * .2126f + g * .7152f + b * .0722f; return new PhotoTone(gray, gray, gray); }
            return new PhotoTone(r, g, b);
        }
    }
}
