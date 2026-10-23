using System;
namespace MalatangStory.Game
{
    public static class BusinessRhythm
    {
        public static bool IsRush(float seconds) => seconds >= 80 && seconds < 160 || seconds >= 560 && seconds < 680;
        public static float ArrivalRate(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0 || seconds >= RestaurantSimulation.DayDuration) return 0;
            if (IsRush(seconds)) return seconds < 160 ? 1.8f : 1.6f;
            if (seconds >= 160 && seconds < 184 || seconds >= 680 && seconds < 704) return .3f;
            return .75f;
        }
        public static float RewardMultiplier(float seconds) => IsRush(seconds) ? 1.25f : 1;
        public static int WarningMask(float seconds) => seconds >= 546.6667f ? 3 : seconds >= 66.6667f ? 1 : 0;
    }
}
