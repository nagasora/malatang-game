namespace MalatangStory.Game
{
    public static class WholesaleMarket
    {
        public static float Multiplier(int day, string weather, string season)
        {
            float daily = day <= 1 ? 1 : .88f + (day * 17L % 9) * .03f;
            float transport = weather == "rain" ? 1.08f : weather == "cold" ? 1.1f : 1;
            return daily * transport * (season == "winter" ? 1.04f : season == "summer" ? .98f : 1);
        }
    }
}
