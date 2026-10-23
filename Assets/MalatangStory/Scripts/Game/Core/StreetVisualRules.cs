using System;

namespace MalatangStory.Game
{
    public readonly struct StreetLot
    {
        public readonly int Floors, Roof, Windows;
        public readonly float Width;
        public StreetLot(int floors, int roof, int windows, float width) { Floors = floors; Roof = roof; Windows = windows; Width = width; }
    }
    public static class StreetVisualRules
    {
        public const float CrosswalkX = 22;
        public static StreetLot Lot(int seed, int index)
        {
            var random = new Random(unchecked(seed * 31 + index * 7919));
            int roof = ((index % 3 + seed % 3) % 3 + 3) % 3;
            return new StreetLot(2 + random.Next(4), roof, 1 + random.Next(2), .82f + (float)random.NextDouble() * .16f);
        }
        public static bool YieldForCrossing(float vehicleX, int direction, float pedestrianZ, float front)
        {
            if (!float.IsFinite(vehicleX) || !float.IsFinite(pedestrianZ) || direction == 0) return false;
            float distance = (CrosswalkX - vehicleX) * Math.Sign(direction);
            return distance >= 0 && distance <= 6 && pedestrianZ < front - 3.8f && pedestrianZ > front - 11.5f;
        }
        public static bool PauseToLook(float time, float phase, bool rainy) => !rainy && (time + phase) % 23 < 2.5f;
    }

    public readonly struct OilDroplet
    {
        public readonly float X, Z, Radius, Aspect, Angle;
        public OilDroplet(float x, float z, float radius, float aspect, float angle) { X = x; Z = z; Radius = radius; Aspect = aspect; Angle = angle; }
    }
    public static class OilDropletRules
    {
        public static OilDroplet Next(Random random, bool mala, int spice, OilDroplet previous, bool mergeAllowed)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            float radius = .005f + (float)Math.Pow(random.NextDouble(), 3.5) * (mala ? .052f : .072f);
            radius *= 1 + Math.Max(0, Math.Min(5, spice)) * .025f;
            float aspect = .75f + (float)random.NextDouble() * .8f;
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            float radial = random.NextDouble() < .35 ? .76f + (float)random.NextDouble() * .13f : .82f * (float)Math.Sqrt(random.NextDouble());
            float x = (float)Math.Cos(angle) * radial, z = (float)Math.Sin(angle) * radial;
            if (mergeAllowed && random.NextDouble() < .32)
            { x = previous.X + (float)Math.Cos(angle) * (previous.Radius + radius) * .6f; z = previous.Z + (float)Math.Sin(angle) * (previous.Radius + radius) * .6f; }
            float limit = .96f - radius * Math.Max(aspect, 1 / aspect);
            float distance = (float)Math.Sqrt(x * x + z * z);
            if (distance > limit) { x *= limit / distance; z *= limit / distance; }
            return new OilDroplet(x, z, radius, aspect, angle);
        }
    }
}
