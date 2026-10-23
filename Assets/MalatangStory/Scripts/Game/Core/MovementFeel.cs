using System;

namespace MalatangStory.Game
{
    /// <summary>入力機器に依存しない加減速と設備の前方優先判定。</summary>
    public static class MovementFeel
    {
        public const float AccelerationSeconds = .12f, DecelerationSeconds = .08f;
        public static float Step(float velocity, float desired, float seconds, float fullSpeed)
        {
            if (float.IsNaN(velocity) || float.IsInfinity(velocity)) velocity = 0;
            if (float.IsNaN(desired) || float.IsInfinity(desired)) desired = 0;
            if (!(seconds > 0) || float.IsInfinity(seconds) || !(fullSpeed > 0) || float.IsInfinity(fullSpeed)) return velocity;
            float duration = Math.Abs(desired) < Math.Abs(velocity) || velocity * desired < 0 ? DecelerationSeconds : AccelerationSeconds;
            float change = fullSpeed / duration * Math.Min(seconds, .1f);
            return velocity + Math.Max(-change, Math.Min(change, desired - velocity));
        }
        public static float TargetScore(float distance, float forwardDot) => distance + (forwardDot >= .5f ? 0 : .85f);
    }
}
