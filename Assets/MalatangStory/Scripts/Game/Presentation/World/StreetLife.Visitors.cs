using System;
using UnityEngine;
namespace MalatangStory.Game
{
    public sealed partial class StreetLife
    {
        public Func<int, string, bool> VisitorRequested;
        public Action BicycleBell;
        private float interest = 50, hourFraction;
        private bool allowVisits;
        private int shopQueue;
        private static string VisitorKind(int index) => new[] { "office", "student", "tourist", "rider", "elder", "couple", "family", "worker" }[index % 8];
        public void SetShopInterest(float reputation, float dayFraction, int waiting, bool allow)
        { interest = Mathf.Clamp(reputation, 0, 100); hourFraction = dayFraction; shopQueue = waiting; allowVisits = allow; }
        private void ConfigureVisitor(Pedestrian person, int index, int seed)
        {
            person.Seed = seed + index * 137; person.Kind = VisitorKind(index); person.NextLookTime = Time.time + index * 3;
            if (index != 4) return;
            var dog = new GameObject("Walking Dog").transform; dog.SetParent(person.Root, false); dog.localPosition = new Vector3(.55f, 0, .1f);
            var fur = MaterialLibrary.Lit(new Color(.57f, .34f, .18f), .3f); var dark = MaterialLibrary.Lit(StoreBuilder.Charcoal, .3f);
            using (var g = new CityGeometry())
            {
                g.Sphere(new Vector3(0, .25f, 0), new Vector3(.45f, .22f, .25f), fur);
                g.Sphere(new Vector3(0, .36f, .24f), new Vector3(.22f, .22f, .24f), fur);
                g.Sphere(new Vector3(0, .34f, .37f), new Vector3(.12f, .08f, .1f), dark);
                for (int side = -1; side <= 1; side += 2) for (int end = -1; end <= 1; end += 2)
                    g.Cylinder(new Vector3(side * .12f, .12f, end * .14f), .025f, .2f, fur);
                g.Line(new Vector3(0, .28f, -.2f), new Vector3(0, .48f, -.34f), .025f, fur);
                g.Line(new Vector3(0, .38f, .14f), new Vector3(-.35f, .8f, 0), .007f, dark);
                g.Flush(dog, "Dog", false);
            }
            person.Dog = dog;
        }
        private bool UpdateVisitor(Pedestrian person, int index, float delta)
        {
            float now = Time.time;
            if (person.HiddenUntil > 0 && now >= person.HiddenUntil)
            { person.HiddenUntil = 0; person.Root.localPosition = new Vector3(-HalfStreet, 0, person.LaneZ); }
            int count = hourFraction < .2f ? 6 : hourFraction < .7f ? 8 : 5;
            bool visible = index < count && (!rainy || index < 5) && now >= person.HiddenUntil;
            if (person.Root.gameObject.activeSelf != visible) person.Root.gameObject.SetActive(visible);
            if (!visible) return true;
            if (person.Dog != null) person.Dog.localPosition = new Vector3(.55f, Mathf.Abs(Mathf.Sin(person.Phase)) * .015f, .1f);
            if (rainy && index == 4)
            {
                person.Root.localPosition = new Vector3(11.1f, 0, streetFront - .55f); person.Rig.SetMoveSpeed(0); person.Rig.SetPose(CharacterPose.Think); return true;
            }
            if (person.LookSeconds > 0)
            {
                person.LookSeconds -= delta; person.Rig.SetMoveSpeed(0); person.Rig.SetPose(CharacterPose.Think); person.Root.localRotation = Quaternion.identity;
                if (person.LookSeconds <= 0 && allowVisits && shopQueue < 5)
                {
                    int chance = (CustomerDirector.StableHash(person.Kind + person.Seed + ":" + (int)(now / 23)) & int.MaxValue) % 100;
                    if (chance < interest && VisitorRequested?.Invoke(person.Seed, person.Kind) == true)
                    { person.HiddenUntil = now + 30; person.Root.gameObject.SetActive(false); }
                }
                return true;
            }
            if (!rainy && Mathf.Abs(person.Root.localPosition.x) < 4 && now >= person.NextLookTime)
            {
                person.LookSeconds = 2; person.NextLookTime = now + Mathf.Lerp(45, 15, interest / 100); return true;
            }
            return false;
        }
    }
}
