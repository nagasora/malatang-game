using System.Linq;
using UnityEngine;
namespace MalatangStory.Game
{
    /// <summary>屋台の鍋と湯気を営業中の一杯に同期する。</summary>
    public sealed class FestivalStallView : MonoBehaviour
    {
        private GameApplication app;
        private ParticleSystem steam;
        private GameObject bowl;
        private string shownOrder;
        public void Initialize(GameApplication application)
        {
            app = application;
            Material wood = MaterialLibrary.Lit(StoreBuilder.Wood, .3f), red = MaterialLibrary.Lit(StoreBuilder.Lacquer, .4f);
            Material steel = MaterialLibrary.Lit(StoreBuilder.Steel, .6f, .35f);
            using (var g = new CityGeometry())
            {
                g.Box(new Vector3(0, .85f, 0), new Vector3(2.7f, .15f, 1.3f), wood);
                g.Box(new Vector3(0, .5f, .2f), new Vector3(2.6f, .65f, .8f), red);
                foreach (int side in new[] { -1, 1 })
                {
                    g.Cylinder(new Vector3(side * 1.15f, 1.5f, .38f), .05f, 2.8f, wood);
                    g.Box(new Vector3(side * .64f, 2.8f, .2f), new Vector3(1.5f, .1f, 1.7f), red, Quaternion.Euler(0, 0, -side * 10));
                }
                g.Add(MeshFactory.Pot(), new Vector3(-.7f, .94f, 0), Vector3.one * .6f, Quaternion.identity, steel);
                g.Flush(transform, "Festival Cart", false);
            }
            steam = EffectsFactory.Steam(transform, 0); steam.transform.localPosition = new Vector3(-.7f, 1.3f, 0);
            var blocker = gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            blocker.shape = UnityEngine.AI.NavMeshObstacleShape.Box; blocker.center = new Vector3(0, .7f, .1f); blocker.size = new Vector3(2.8f, 1.5f, 1.4f); blocker.carving = true;
        }
        private void Update()
        {
            if (app == null) return;
            CustomerOrder heating = app.Simulation.Active.FirstOrDefault(x => x.stage == OrderStage.Heating);
            float intensity = heating == null ? .08f : CookingFeel.Cues(heating.heatSeconds / Mathf.Max(1, app.Simulation.TargetHeat(heating))).Steam;
            EffectsFactory.SetIntensity(steam, intensity);
            CustomerOrder ready = app.Simulation.Active.FirstOrDefault(x => x.stage == OrderStage.Ready);
            string id = ready?.id;
            if (id == shownOrder) return;
            shownOrder = id; if (bowl != null) Destroy(bowl);
            if (ready != null) bowl = app.World.Kitchen.CreateServedBowl(ready, transform.position + new Vector3(.65f, .96f, 0));
        }
        private void OnDestroy() { if (bowl != null) Destroy(bowl); }
    }
}
