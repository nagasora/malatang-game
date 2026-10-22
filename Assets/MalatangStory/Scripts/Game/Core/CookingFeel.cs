using System;

namespace MalatangStory.Game
{
    public readonly struct CookingCue
    {
        public readonly float ColourBlend, OverBlend, Bubbles, Steam, Scale;
        public readonly bool Ready;
        public CookingCue(float colour, float over, float bubbles, float steam, float scale, bool ready)
        { ColourBlend = colour; OverBlend = over; Bubbles = bubbles; Steam = steam; Scale = scale; Ready = ready; }
    }

    public enum EatingStage { Pick, Lift, Chew, Sip }

    /// <summary>調理・食事の視覚的な手がかりを入力や描画から独立して計算する。</summary>
    public static class CookingFeel
    {
        public const float HandRhythmBonus = 1;
        public const int ExpertExperienceBonus = 3;
        public static CookingCue Cues(float doneness)
        {
            if (float.IsNaN(doneness) || float.IsInfinity(doneness)) doneness = 0;
            float colour = Math.Max(0, Math.Min(1, doneness));
            float over = Math.Max(0, Math.Min(1, (doneness - 1) * 2));
            return new CookingCue(colour, over, .5f + colour * .8f + over * .8f, .3f + colour * .9f + over * .5f,
                1 - over * .12f, doneness >= QualityEvaluator.PerfectDonenessMin && doneness <= QualityEvaluator.PerfectDonenessMax);
        }
        public static bool OnRhythm(float interval) => interval >= .25f && interval <= .5f;
        public static EatingStage MealStage(float seconds)
        {
            if (!(seconds >= 0) || float.IsInfinity(seconds)) return EatingStage.Pick;
            float phase = seconds % 3.5f;
            return phase < .8f ? EatingStage.Pick : phase < 1.5f ? EatingStage.Lift : phase < 2.6f ? EatingStage.Chew : EatingStage.Sip;
        }
    }
}
