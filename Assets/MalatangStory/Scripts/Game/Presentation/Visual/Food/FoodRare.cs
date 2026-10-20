using System.Linq;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>朝市の希少6種を、専用の形状・模様・加熱色で生成する。</summary>
    public sealed class FoodRare : IFoodBuilder
    {
        /// <summary>希少モデルを共通キャッシュへ登録し、調理とアイコンで同じ形状を使う。</summary>
        public static void Register() => FoodRegistry.Register(new FoodRare(), MorningMarket.RareIds.ToArray());

        /// <summary>食材ID別の特徴を組み立て、未対応IDならnullを返す。</summary>
        public FoodModel Build(string id)
        {
            if (!System.Linq.Enumerable.Contains(MorningMarket.RareIds, id)) return null;
            using (var model = new FoodModelBuilder())
            {
                switch (id)
                {
                    case "matsutake": Matsutake(model, id); break;
                    case "black_truffle": Truffle(model, id); break;
                    case "jinhua_ham": Ham(model, id); break;
                    case "wagyu": Wagyu(model, id); break;
                    case "kuruma_shrimp": Shrimp(model, id); break;
                    case "abalone": Abalone(model, id); break;
                }
                return model.Finish(id);
            }
        }

        /// <summary>模様を持つ原色と、茹でたときの色・湿った艶を表面に設定する。</summary>
        private static void Surface(FoodModelBuilder model, string id, int group, uint dark, uint light,
            FoodPattern pattern, Color cooked)
        {
            model.SetSurface(group, FoodTextures.Create(id + "/" + group, FoodTextures.Rgb(dark), FoodTextures.Rgb(light), pattern),
                new FoodSurface(cooked, new Color(.54f, .43f, .30f), .24f, .76f));
        }

        /// <summary>厚い茶色の傘と太い白い軸、傘裏のひだで松茸を表す。</summary>
        private static void Matsutake(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x573A22, 0xC89C62, FoodPattern.Fibers, new Color(.76f, .70f, .58f));
            Surface(model, id, 1, 0xD7C9AE, 0xFFF4DC, FoodPattern.Fibers, new Color(.84f, .79f, .67f));
            model.Add(1, FoodKit.Ellipsoid(8), new Vector3(0, .005f, 0), new Vector3(.045f, .145f, .05f));
            model.Add(0, FoodKit.Ellipsoid(9), new Vector3(0, .065f, 0), new Vector3(.142f, .063f, .125f));
            model.Add(1, FoodKit.Ellipsoid(10), new Vector3(0, .048f, 0), new Vector3(.128f, .012f, .113f));
            for (int i = 0; i < 4; i++)
                model.Add(0, FoodKit.Ellipsoid(i, .04f, 6, 3), new Vector3((i % 2 == 0 ? -1 : 1) * .014f, -.036f + i * .018f, -.021f),
                    new Vector3(.009f, .023f, .004f), new Vector3(0, 0, i * 18));
        }

        /// <summary>不規則な黒い塊に粒状の突起を重ね、トリュフの凹凸を表す。</summary>
        private static void Truffle(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x100F0B, 0x61533A, FoodPattern.Pores, new Color(.66f, .62f, .53f));
            model.Add(0, FoodKit.Ellipsoid(51, .32f), Vector3.zero, new Vector3(.145f, .105f, .12f));
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 2.39996f;
                float elevation = (i + .5f) / 12f;
                float radius = Mathf.Sqrt(1f - elevation * elevation);
                model.Add(0, FoodKit.Ellipsoid(i, .25f, 6, 3), new Vector3(Mathf.Cos(angle) * radius * .061f, elevation * .048f, Mathf.Sin(angle) * radius * .05f), new Vector3(.026f, .025f, .024f));
            }
        }

        /// <summary>飴色の厚い外皮、白い脂の層、赤褐色の平らな切断面で金華ハムの塊を表す。</summary>
        private static void Ham(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x613416, 0xB77A38, FoodPattern.Toasted, new Color(.82f, .72f, .54f));
            Surface(model, id, 1, 0xE3C99E, 0xFFF4DA, FoodPattern.Gradient, new Color(.98f, .93f, .81f));
            Surface(model, id, 2, 0x72242A, 0xBF6553, FoodPattern.Fibers, new Color(.81f, .72f, .62f));
            model.Add(0, HamBlock(.145f, .095f, .17f, .78f), Vector3.zero, Vector3.one);
            model.Add(1, HamBlock(.132f, .081f, .003f, 1f), new Vector3(0, 0, -.086f), Vector3.one);
            model.Add(2, HamBlock(.116f, .052f, .003f, 1f), new Vector3(0, -.009f, -.089f), Vector3.one);
            for (int i = 0; i < 2; i++)
                model.Add(1, FoodKit.Tube(new[] { new Vector3(-.052f, -.018f + i * .019f, -.091f),
                    new Vector3(0, -.013f + i * .019f, -.092f), new Vector3(.052f, -.022f + i * .019f, -.091f) }, .0016f, .001f, 4), Vector3.zero, Vector3.one);
        }

        /// <summary>先細りの塊を平面ごとに分離し、皮と切り口に硬い稜線と安定したUVを与える。</summary>
        private static Mesh HamBlock(float width, float height, float depth, float taper)
        {
            float x = width * .5f, y = height * .5f, z = depth * .5f;
            Vector3[] corners = { new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(-x, y, -z),
                new Vector3(-x * taper, -y, z), new Vector3(x * taper, -y, z), new Vector3(x * taper, y * taper, z), new Vector3(-x * taper, y * taper, z) };
            int[] faces = { 0, 3, 2, 1, 4, 5, 6, 7, 0, 4, 7, 3, 1, 2, 6, 5, 3, 7, 6, 2, 0, 1, 5, 4 };
            var vertices = new Vector3[24]; var uv = new Vector2[24]; var triangles = new int[36];
            Vector2[] faceUv = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            for (int face = 0; face < 6; face++)
            {
                int v = face * 4, triangle = face * 6;
                for (int i = 0; i < 4; i++) { vertices[v + i] = corners[faces[v + i]]; uv[v + i] = faceUv[i]; }
                triangles[triangle] = v; triangles[triangle + 1] = v + 1; triangles[triangle + 2] = v + 2;
                triangles[triangle + 3] = v; triangles[triangle + 4] = v + 2; triangles[triangle + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Cured Ham Cut", hideFlags = HideFlags.DontSave, vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>薄く折れた肉片に白い霜降りの網目を描き、茹でると淡い肉色へ変える。</summary>
        private static void Wagyu(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x9E233C, 0xFFF0D9, FoodPattern.Marbling, new Color(.65f, .58f, .45f));
            for (int i = 0; i < 3; i++)
                model.Add(0, FoodKit.Sheet(.105f, .145f, 1.4f, .007f, 8, 6), new Vector3((i - 1) * .032f, i * .013f, 0),
                    Vector3.one, new Vector3(9 * i, i * 22, i * 8));
        }

        /// <summary>曲がった節と縞、尾扇と触角で車海老を表す。茹でると朱色が強くなる。</summary>
        private static void Shrimp(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x76674E, 0xE4C9AC, FoodPattern.Fibers, new Color(1f, .53f, .32f));
            Surface(model, id, 1, 0x5F392A, 0xD97A45, FoodPattern.Gradient, new Color(1f, .54f, .28f));
            Surface(model, id, 2, 0x171510, 0x41352B, FoodPattern.Mottled, new Color(.66f, .51f, .36f));
            model.Add(0, FoodKit.Ellipsoid(31, .04f, 8, 4), new Vector3(-.03f, 0, .056f), new Vector3(.044f, .043f, .065f));
            for (int side = -1; side <= 1; side += 2)
                model.Add(2, FoodKit.Ellipsoid(side + 2, .01f, 6, 3), new Vector3(-.03f + side * .018f, .016f, .075f), Vector3.one * .007f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * .38f;
                Vector3 point = new Vector3(Mathf.Sin(a) * .066f - .03f, 0, Mathf.Cos(a) * .066f - .025f);
                model.Add(0, FoodKit.Ellipsoid(i, .04f, 8, 4), point, new Vector3(.043f - i * .003f, .036f, .041f), new Vector3(0, i * 22, 0));
                model.Add(1, FoodKit.Ellipsoid(i, .01f, 8, 3), point + new Vector3(0, .016f, .006f), new Vector3(.033f - i * .002f, .003f, .006f), new Vector3(0, i * 22, 0));
            }
            for (int i = -1; i <= 1; i++)
                model.Add(1, FoodKit.Ellipsoid(i + 21, .04f, 8, 4), new Vector3(.035f + i * .017f, -.002f, -.065f), new Vector3(.028f, .007f, .047f), new Vector3(0, i * 32, 0));
            model.Add(1, FoodKit.Tube(new[] { new Vector3(-.03f, .01f, .055f), new Vector3(-.055f, .013f, .09f), new Vector3(-.09f, .006f, .097f) }, .0015f, .0008f, 4), Vector3.zero, Vector3.one);
        }

        /// <summary>楕円の殻と肉厚の身、殻の片側に並ぶ呼吸孔で鮑を表す。</summary>
        private static void Abalone(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x384E46, 0x9C9972, FoodPattern.Mottled, new Color(.75f, .70f, .52f));
            Surface(model, id, 1, 0xAC8053, 0xF3D5A0, FoodPattern.Fibers, new Color(.86f, .78f, .63f));
            Surface(model, id, 2, 0x182F29, 0x3C4B3C, FoodPattern.Pores, new Color(.73f, .68f, .54f));
            model.Add(0, FoodKit.Ellipsoid(40), Vector3.zero, new Vector3(.14f, .035f, .19f));
            model.Add(1, FoodKit.Ellipsoid(41), new Vector3(.006f, .019f, 0), new Vector3(.102f, .037f, .156f));
            for (int i = 0; i < 5; i++)
                model.Add(2, FoodKit.Ellipsoid(i, .02f, 6, 3), new Vector3(-.057f, .014f, (i - 2) * .021f), new Vector3(.009f, .004f, .009f));
        }
    }
}
