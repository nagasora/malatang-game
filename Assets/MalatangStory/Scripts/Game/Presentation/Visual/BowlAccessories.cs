using UnityEngine;
namespace MalatangStory.Game
{
    public static class BowlAccessories
    {
        public static void Add(Transform bowl, string style, bool closed = false)
        {
            if (style == "paper")
            {
                var ink = MaterialLibrary.Lit(new Color(.65f, .18f, .1f), .15f);
                StoreBuilder.Part("Paper Bowl Band", MeshFactory.Torus(.405f, .012f, 24, 6), ink, Vector3.up * .23f, Vector3.one, bowl, null, false);
                if (closed)
                {
                    var bag = MaterialLibrary.Lit(new Color(.72f, .58f, .39f), .1f);
                    StoreBuilder.Part("Takeaway Paper Bag", StoreBuilder.RoundedBox(new Vector3(.36f, .48f, .24f), .02f), bag, new Vector3(.7f, .24f, .15f), Vector3.one, bowl, null, false);
                    StoreBuilder.Part("Paper Bag Handle", MeshFactory.Torus(.1f, .012f, 16, 4), bag, new Vector3(.7f, .51f, .15f), Vector3.one, bowl, Quaternion.Euler(90, 0, 0), false);
                    StoreBuilder.Part("Paper Lid", MeshFactory.Cylinder(.505f, .035f), MaterialLibrary.Lit(new Color(.9f, .82f, .66f), .12f), Vector3.up * .425f, Vector3.one, bowl, null, false);
                }
            }
            if (style == "paper") return;
            StoreBuilder.Part("Wooden Serving Tray", StoreBuilder.RoundedBox(new Vector3(1.55f, .05f, 1.2f), .05f), MaterialLibrary.Lit(StoreBuilder.Wood, .3f), Vector3.down * .04f, Vector3.one, bowl, null, false);
            StoreBuilder.Part("Tray Side Dish", MeshFactory.Bowl(), MaterialLibrary.Lit(new Color(.9f, .91f, .84f), .7f), new Vector3(-.62f, .015f, .2f), Vector3.one * .2f, bowl, null, false);
            StoreBuilder.Part("Tray Spoon", MeshFactory.Sphere(), MaterialLibrary.Lit(new Color(.88f, .9f, .84f), .7f), new Vector3(-.62f, .035f, -.24f), new Vector3(.085f, .025f, .2f), bowl, null, false);
            var wood = MaterialLibrary.Lit(StoreBuilder.WoodLight, .35f);
            for (int side = -1; side <= 1; side += 2)
                StoreBuilder.Part("Tray Chopstick", MeshFactory.Cylinder(.009f, .72f), wood, new Vector3(.62f + side * .018f, -.002f, 0), Vector3.one, bowl, Quaternion.Euler(90, 0, 0), false);
        }
    }
}
