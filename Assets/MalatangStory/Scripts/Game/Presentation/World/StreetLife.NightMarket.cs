using UnityEngine;
namespace MalatangStory.Game
{
    public sealed partial class StreetLife
    {
        private void BuildNightMarket(int seed)
        {
            nightMarket = new GameObject("Evening Street Food"); nightMarket.transform.SetParent(transform, false);
            nightMarket.transform.localPosition = new Vector3(-14, 0, streetFront - 13.7f);
            using (var geometry = new CityGeometry())
            {
                var wood = MaterialLibrary.Lit(StoreBuilder.Wood, .3f);
                geometry.Box(new Vector3(0, .8f, 0), new Vector3(2, .16f, 1), wood);
                geometry.Box(new Vector3(0, .4f, 0), new Vector3(1.9f, .7f, .8f), MaterialLibrary.Lit(StoreBuilder.Lacquer, .3f));
                geometry.Add(MeshFactory.Pot(), new Vector3(-.4f, .9f, 0), Vector3.one * .5f, Quaternion.identity, MaterialLibrary.Lit(StoreBuilder.Steel, .6f));
                geometry.Flush(nightMarket.transform, "Evening Cart", false);
            }
            var steam = EffectsFactory.Steam(nightMarket.transform, .35f); steam.transform.localPosition = new Vector3(-.4f, 1.2f, 0);
            for (int i = 0; i < 3; i++)
            {
                var appearance = CharacterAppearance.ForArchetype(i == 0 ? "elder" : "office", seed + 500 + i * 137, false);
                var person = CharacterFactory.Create(appearance, "Evening Customer " + i, nightMarket.transform);
                person.transform.localPosition = new Vector3(-1.5f + i * 1.1f, 0, -1);
                person.SetPose(i % 2 == 0 ? CharacterPose.Think : CharacterPose.Wave);
            }
        }
    }
}
