using System;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>野菜20種を、切り売り・小束・小房の状態で識別できる形に組み立てます。</summary>
    public sealed class FoodVegetables : IFoodBuilder
    {
        private static readonly string[] Ids = { "okra", "burdock", "potato", "bok_choy", "cilantro", "broccoli", "spinach", "cherry_tomato", "bean_sprout", "baby_corn", "lettuce", "lotus_root", "wakame", "kombu", "stem_lettuce", "crown_daisy", "komatsuna", "mizuna", "pea_shoot", "napa_cabbage" };

        /// <summary>標準の野菜カテゴリを登録します。</summary>
        public static void Register()
        {
            FoodRegistry.Register(new FoodVegetables(), Ids);
        }

        /// <summary>指定野菜の低ポリゴン・テクスチャ付き共有モデルを生成します。</summary>
        public FoodModel Build(string id)
        {
            if (Array.IndexOf(Ids, id) < 0) return null;
            using (FoodModelBuilder model = new FoodModelBuilder())
            {
                switch (id)
                {
                    case "cherry_tomato": Tomato(model, id); break;
                    case "broccoli": Broccoli(model, id); break;
                    case "bean_sprout": Sprouts(model, id); break;
                    case "baby_corn": Corn(model, id); break;
                    case "lotus_root": Lotus(model, id); break;
                    case "okra": Okra(model, id); break;
                    case "burdock": Roots(model, id, true); break;
                    case "stem_lettuce": Roots(model, id, false); break;
                    case "potato": Potato(model, id); break;
                    case "wakame": Seaweed(model, id, false); break;
                    case "kombu": Seaweed(model, id, true); break;
                    case "napa_cabbage": NapaCabbage(model, id); break;
                    default: Greens(model, id); break;
                }
                return model.Finish(id);
            }
        }

        /// <summary>色柄と野菜の加熱による暗色化・湿潤を設定します。</summary>
        private static void Surface(FoodModelBuilder model, string id, int slot, uint dark, uint light,
            FoodPattern pattern, bool leafy = false, float gloss = .3f, bool transparent = false)
        {
            Color cooked = leafy ? new Color(.60f, .78f, .46f, transparent ? .88f : 1f) : new Color(.94f, .90f, .74f, 1f);
            Color over = leafy ? new Color(.43f, .52f, .22f, transparent ? .93f : 1f) : new Color(.69f, .61f, .42f, 1f);
            model.SetSurface(slot, FoodTextures.Create(id + "/" + slot, FoodTextures.Rgb(dark), FoodTextures.Rgb(light), pattern),
                new FoodSurface(cooked, over, gloss, leafy ? .74f : .62f, transparent ? .82f : 1f), transparent);
        }

        /// <summary>丸く張った果皮と五枚の星形のヘタを表現します。</summary>
        private static void Tomato(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xAD2117, 0xFF5530, FoodPattern.Mottled, false, .82f);
            Surface(model, id, 1, 0x244B15, 0x598B25, FoodPattern.Veins, true);
            model.Add(0, FoodKit.Ellipsoid(11, .012f, 14, 8), Vector3.zero, new Vector3(.115f, .11f, .115f));
            for (int i = 0; i < 5; i++) model.Add(1, FoodKit.Leaf(.013f, .039f, -.18f, 0f, 4, 2),
                new Vector3(0f, .052f, 0f), Vector3.one, new Vector3(0f, i * 72f, 0f));
            model.Add(1, FoodKit.Tube(new Vector3[] { new Vector3(0f, .048f, 0f), new Vector3(.008f, .068f, 0f) }, .003f, .002f), Vector3.zero, Vector3.one);
        }

        /// <summary>太い切断軸から枝分かれする小房と、粒状の花蕾を表現します。</summary>
        private static void Broccoli(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x659B43, 0xBDD78C, FoodPattern.Fibers);
            Surface(model, id, 1, 0x164322, 0x61913B, FoodPattern.Florets, true);
            model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(0f, -.07f, 0f), Vector3.zero, new Vector3(0f, .025f, 0f) }, .021f, .014f, 8), Vector3.zero, Vector3.one);
            for (int i = 0; i < 7; i++)
            {
                float angle = i * Mathf.PI * 2f / 7f;
                Vector3 top = new Vector3(Mathf.Cos(angle) * .046f, .035f + .006f * (i % 2), Mathf.Sin(angle) * .046f);
                model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(0f, -.015f, 0f), top }, .008f, .011f, 5), Vector3.zero, Vector3.one);
                model.Add(1, FoodKit.Ellipsoid(i, .10f, 8, 5), top, new Vector3(.067f, .061f, .063f));
            }
            model.Add(1, FoodKit.Ellipsoid(31, .10f, 8, 5), new Vector3(0f, .05f, 0f), new Vector3(.07f, .067f, .07f));
        }

        /// <summary>白く湾曲した茎、黄色い豆、細い根を小束にします。</summary>
        private static void Sprouts(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xBFC9AA, 0xF2EBD1, FoodPattern.Fibers, false, .32f);
            Surface(model, id, 1, 0xADBD42, 0xF1DF72, FoodPattern.Mottled);
            Surface(model, id, 2, 0xAAB38B, 0xD5D5AC, FoodPattern.Fibers);
            for (int i = 0; i < 12; i++)
            {
                float x = (i % 4 - 1.5f) * .014f; float z = (i / 4 - 1f) * .020f;
                Vector3 tip = new Vector3(x + .012f * Mathf.Sin(i), .019f, z + .072f);
                model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(x, 0f, z - .075f), new Vector3(x - .011f, .014f, z), tip }, .0049f, .0063f, 5), Vector3.zero, Vector3.one);
                model.Add(1, FoodKit.Ellipsoid(i, .02f, 6, 3), tip, new Vector3(.014f, .009f, .019f), new Vector3(0f, i * 17f, 0f));
                model.Add(2, FoodKit.Tube(new Vector3[] { new Vector3(x, 0f, z - .075f), new Vector3(x + .01f, .001f, z - .098f) }, .0011f, .0004f, 4), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>細長い芯の周りに規則的な粒列を並べ、若いトウモロコシにします。</summary>
        private static void Corn(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xD5B54D, 0xFFF1A2, FoodPattern.Fibers);
            Surface(model, id, 1, 0xD6B346, 0xFFF0A0, FoodPattern.Mottled, false, .42f);
            model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(0f, -.083f, 0f), new Vector3(0f, .074f, 0f) }, .014f, .008f, 8), Vector3.zero, Vector3.one);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 6; x++)
            {
                float angle = x * Mathf.PI / 3f; float radius = Mathf.Lerp(.014f, .008f, y / 8f);
                model.Add(1, FoodKit.Ellipsoid(y * 6 + x, .02f, 6, 3), new Vector3(Mathf.Cos(angle) * radius, -.068f + y * .019f, Mathf.Sin(angle) * radius),
                    new Vector3(.013f, .016f, .012f));
            }
        }

        /// <summary>れんこんの七つの貫通孔、中央孔と、皮に近い淡褐色の縁を表現します。</summary>
        private static void Lotus(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xD5C7A6, 0xF6EDD3, FoodPattern.Fibers, false, .28f);
            Surface(model, id, 1, 0x9F805A, 0xC7AD81, FoodPattern.Mottled);
            model.Add(0, FoodKit.RadialPerforatedSlice(.071f, .020f, 8, 14), Vector3.zero, Vector3.one);
            model.Add(1, FoodKit.Lathe(new Vector2[] { new Vector2(.071f, -.010f), new Vector2(.072f, -.009f), new Vector2(.072f, .009f), new Vector2(.071f, .010f) }, 32), Vector3.zero, Vector3.one);
        }

        /// <summary>五角形の莢、細い先端、淡い切断面と断面の種を表現します。</summary>
        private static void Okra(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x386B25, 0x7DA849, FoodPattern.Fibers, true);
            Surface(model, id, 1, 0x97B964, 0xE0DFA0, FoodPattern.Mottled);
            Surface(model, id, 2, 0xD7D5A0, 0xFFFFE0, FoodPattern.Mottled);
            model.Add(0, FoodKit.Lathe(new Vector2[] { new Vector2(0f, -.086f), new Vector2(.009f, -.071f), new Vector2(.018f, .025f), new Vector2(.019f, .071f) }, 5), Vector3.zero, Vector3.one, new Vector3(73f, 0f, 12f));
            model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(.039f, 0f, -.035f), new Vector3(.039f, .019f, -.035f) }, .022f, .022f, 5), Vector3.zero, Vector3.one);
            model.Add(1, FoodKit.Ellipsoid(1, 0f, 10, 3), new Vector3(.039f, .0195f, -.035f), new Vector3(.036f, .002f, .036f));
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                model.Add(2, FoodKit.Ellipsoid(i, 0f, 6, 3), new Vector3(.039f + .009f * Mathf.Cos(a), .021f, -.035f + .009f * Mathf.Sin(a)), new Vector3(.004f, .003f, .005f));
            }
        }

        /// <summary>ごぼうの皮付き短冊と、茎レタスの淡緑の皮むき切片を作ります。</summary>
        private static void Roots(FoodModelBuilder model, string id, bool burdock)
        {
            Surface(model, id, 0, burdock ? 0x72513Au : 0x659D4Fu, burdock ? 0xB29266u : 0xB8D68Au, FoodPattern.Fibers);
            Surface(model, id, 1, burdock ? 0xCEBF96u : 0xB0CC8Eu, burdock ? 0xF2E4BFu : 0xE4EFC0u, FoodPattern.Fibers);
            for (int i = 0; i < 3; i++)
            {
                Vector3 position = new Vector3((i - 1) * .026f, i * .007f, 0f);
                model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(0f, 0f, -.088f), new Vector3(.003f, .002f, 0f), new Vector3(0f, 0f, .088f) }, burdock ? .008f : .011f, burdock ? .006f : .011f, 7), position, Vector3.one, new Vector3(0f, (i - 1) * 7f, 0f));
                model.Add(1, FoodKit.Sheet(burdock ? .010f : .015f, .176f, 1f, .001f, 5, 2), position + new Vector3(0f, burdock ? .006f : .009f, 0f), Vector3.one, new Vector3(0f, (i - 1) * 7f, 0f));
            }
        }

        /// <summary>皮の縁を残した厚いジャガイモの輪切りを二枚重ねます。</summary>
        private static void Potato(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xC5A777, 0xE5CD98, FoodPattern.Pores);
            Surface(model, id, 1, 0xE4CB87, 0xFFF0BB, FoodPattern.Mottled);
            for (int i = 0; i < 2; i++)
            {
                Vector3 position = new Vector3(i * .033f - .017f, i * .021f, i * .021f);
                model.Add(0, FoodKit.Tube(new Vector3[] { Vector3.down * .01f, Vector3.up * .01f }, .055f, .055f, 16), position, new Vector3(1f, 1f, .8f));
                model.Add(1, FoodKit.Lathe(new Vector2[] { Vector2.zero, new Vector2(.052f, 0f), new Vector2(.052f, .001f), new Vector2(0f, .001f) }, 16), position + Vector3.up * .0102f, new Vector3(1f, 1f, .8f));
            }
        }

        /// <summary>わかめの薄い縮れ葉と、昆布の幅広い帯状の切片を表現します。</summary>
        private static void Seaweed(FoodModelBuilder model, string id, bool kombu)
        {
            Surface(model, id, 0, kombu ? 0x182D16u : 0x123E27u, kombu ? 0x52643Bu : 0x39734Au,
                FoodPattern.Translucent, true, .78f, !kombu);
            for (int i = 0; i < (kombu ? 2 : 3); i++)
                model.Add(0, FoodKit.Sheet(kombu ? .063f : .082f, kombu ? .19f : .12f, kombu ? 1.5f : 3f, kombu ? .007f : .016f),
                    new Vector3((i - 1) * .026f, i * .014f, 0f), Vector3.one, new Vector3(i * 8f, i * 27f, i * 9f));
        }

        /// <summary>太い白い葉軸と縦筋、先端の淡黄緑の波打つ葉を、ざく切り三枚の重なりにします。</summary>
        private static void NapaCabbage(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x629439, 0xBCCF73, FoodPattern.Veins, true, .25f);
            Surface(model, id, 1, 0xC5C4A6, 0xEEE8CF, FoodPattern.Fibers, false, .28f);
            for (int i = 0; i < 3; i++)
            {
                Vector3 position = new Vector3((i - 1) * .022f, i * .014f, -.077f + i * .009f);
                Vector3 rotation = new Vector3(-4f + i * 5f, (i - 1) * 13f, (i - 1) * 7f);
                model.Add(1, FoodKit.Tube(new Vector3[] { Vector3.zero, new Vector3(0f, .007f, .046f), new Vector3(0f, .011f, .092f) }, .015f, .010f, 8),
                    position, new Vector3(1.3f, .55f, 1f), rotation);
                model.Add(0, FoodKit.VegetableLeaf(.089f, .107f, FoodLeafShape.Cut, .32f, .16f, 8, 6),
                    position + Quaternion.Euler(rotation) * new Vector3(0f, .009f, .044f), Vector3.one, rotation);
                model.Add(1, FoodKit.Tube(new Vector3[] { new Vector3(0f, .016f, .060f), new Vector3(0f, .023f, .098f), new Vector3(0f, .031f, .132f) }, .005f, .0016f, 6),
                    position, Vector3.one, rotation);
                for (int branch = 0; branch < 2; branch++) for (int side = -1; side <= 1; side += 2)
                {
                    float z = .074f + branch * .021f;
                    model.Add(1, FoodKit.Tube(new Vector3[] { new Vector3(0f, .023f + branch * .006f, z), new Vector3(side * .034f, .028f + branch * .007f, z + .014f) }, .0018f, .00065f, 4),
                        position, Vector3.one, rotation);
                }
            }
        }

        /// <summary>葉形、葉脈色、葉柄の太さと切り売り構成を野菜ごとに変えます。</summary>
        private static void Greens(FoodModelBuilder model, string id)
        {
            bool bok = id == "bok_choy";
            bool pale = bok; bool lettuce = id == "lettuce";
            bool narrow = id == "mizuna" || id == "pea_shoot";
            bool lobed = id == "cilantro" || id == "crown_daisy" || id == "mizuna";
            uint dark = lettuce ? 0x6FAD40u : id == "spinach" ? 0x143C24u : id == "komatsuna" ? 0x0E5A31u : id == "crown_daisy" ? 0x476428u : 0x235D2Au;
            uint light = bok ? 0x77A84Cu : lettuce ? 0xC4DC7Cu : id == "spinach" ? 0x597C2Cu : id == "komatsuna" ? 0x3F9B56u : id == "crown_daisy" ? 0x9AB446u : 0x86AE59u;
            Surface(model, id, 0, dark, light, FoodPattern.Veins, true, .28f);
            Surface(model, id, 1, pale ? 0xBDD29Eu : 0x568344u, pale ? 0xFFFFE1u : 0xBBD48Au, FoodPattern.Fibers);
            if (id == "spinach") Surface(model, id, 2, 0x8B293B, 0xD37A74, FoodPattern.Fibers);
            int count = lettuce ? 4 : narrow ? 6 : 5;
            for (int i = 0; i < count; i++)
            {
                float spread = (i - (count - 1) * .5f) * (bok ? 14f : 13f);
                float width = lettuce ? .09f : bok ? .058f : narrow ? .022f : id == "cilantro" ? .038f : .046f;
                float length = lettuce ? .135f : bok ? .105f : id == "pea_shoot" ? .046f : narrow ? .09f : .105f;
                float stemLength = lettuce ? .022f : bok ? .066f : .079f;
                Vector3 stem = new Vector3((i - (count - 1) * .5f) * .006f, i * .002f, -.10f);
                Vector3 tip = stem + Quaternion.Euler(0f, spread, 0f) * new Vector3(0f, .014f, stemLength);
                float stemRadius = bok ? .011f : .003f;
                model.Add(1, FoodKit.Tube(new Vector3[] { stem, Vector3.Lerp(stem, tip, .5f) + Vector3.up * .004f, tip }, stemRadius, stemRadius * .55f, 6), Vector3.zero, Vector3.one);
                Vector3 leafRotation = new Vector3(-i * 3f, spread, (i % 2 == 0 ? 1f : -1f) * 8f);
                if (id == "spinach")
                {
                    model.Add(0, FoodKit.VegetableLeaf(.050f, .120f, FoodLeafShape.Arrow, .16f, .02f), tip, Vector3.one, leafRotation);
                    model.Add(2, FoodKit.Tube(new Vector3[] { stem, Vector3.Lerp(stem, tip, .26f) }, .0042f, .0037f, 5), Vector3.zero, Vector3.one);
                }
                else if (id == "komatsuna")
                    model.Add(0, FoodKit.VegetableLeaf(.069f, .096f, FoodLeafShape.Rounded, .12f, .025f), tip, Vector3.one, leafRotation);
                else if (id == "crown_daisy")
                    model.Add(0, FoodKit.VegetableLeaf(.043f, .115f, FoodLeafShape.Lobed, .12f, .58f, 14, 4), tip, Vector3.one, leafRotation);
                else
                    model.Add(0, FoodKit.Leaf(width, length, lettuce ? .36f : .18f, lobed ? .32f : lettuce ? .19f : .04f, 8, 4), tip, Vector3.one, leafRotation);
                if (bok)
                    model.Add(1, FoodKit.Tube(new Vector3[] { tip, tip + Quaternion.Euler(0f, spread, 0f) * new Vector3(0f, .006f, length * .42f), tip + Quaternion.Euler(0f, spread, 0f) * new Vector3(0f, .018f, length * .85f) }, stemRadius * .50f, .0015f, 5), Vector3.zero, Vector3.one);
                if (id == "pea_shoot")
                    model.Add(0, FoodKit.Leaf(.025f, .042f, .16f, 0f, 4, 2), tip + new Vector3(.005f, .004f, -.016f), Vector3.one, new Vector3(0f, spread + 100f, 0f));
                if (id == "cilantro")
                    for (int lobe = 0; lobe < 2; lobe++)
                        model.Add(0, FoodKit.Leaf(.025f, .035f, .1f, .3f, 4, 2), tip + new Vector3(0f, .008f, .032f + lobe * .025f), Vector3.one, new Vector3(0f, spread + (lobe == 0 ? 65f : -65f), 0f));
            }
        }
    }
}
