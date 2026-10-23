using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>提供後の経過と季節で湯気を弱め、食後の器では止める。</summary>
    public sealed class BowlSteam : MonoBehaviour
    {
        private ParticleSystem particles;
        private CustomerOrder order;
        private GameState state;
        private float initialAge;
        public void Initialize(ParticleSystem value, CustomerOrder customer, GameState game)
        { particles = value; order = customer; state = game; initialAge = Time.time; }
        private void Update()
        {
            if (particles == null || order == null) return;
            string season = Seasons.Of(state.day).Id;
            float seasonal = season == "winter" ? 1.5f : season == "summer" ? .6f : 1;
            float age = order.stage == OrderStage.Served ? order.eatSeconds : Time.time - initialAge;
            float intensity = order.mealFinished || order.takeout && order.stage == OrderStage.Served ? 0 : seasonal * Mathf.Clamp01(1 - age / RestaurantSimulation.EatDuration);
            EffectsFactory.SetIntensity(particles, intensity * .7f * (state.highQuality ? 1 : .4f));
        }
    }
}
