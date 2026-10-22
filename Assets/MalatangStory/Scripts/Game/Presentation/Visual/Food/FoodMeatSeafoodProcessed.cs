using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>肉・魚介・加工品・その他の26食材を、実行時生成の形状と色柄で描き分けます。</summary>
    public sealed class FoodMeatSeafoodProcessed : IFoodBuilder
    {
        private static readonly string[] Ids =
        {
            "quail_egg", "lamb", "beef_offal", "beef_slice", "beef_tripe", "chicken_breast",
            "chicken_thigh", "pork_belly", "egg", "clam", "squid", "shrimp", "scallop",
            "shrimp_ball", "crab_stick", "crab_ball", "chikuwa", "fish_ball", "tteokbokki",
            "hanpen", "wonton", "dumpling", "meat_ball", "youtiao", "duck_blood", "chicken_feet"
        };

        /// <summary>食材固有の斑点や層、焼き色を選択します。</summary>
        private enum Detail { Speckles, Quail, PorkLayers, Chikuwa, Shell, Blood, Bread }

        /// <summary>反射を使わず、プレイ開始前に担当IDを登録します。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
            FoodRegistry.Register(new FoodMeatSeafoodProcessed(), Ids);
        }

        /// <summary>対応する食材の共有モデルを生成し、対応外のIDにはnullを返します。</summary>
        public FoodModel Build(string id)
        {
            if (Array.IndexOf(Ids, id) < 0) return null;
            using (FoodModelBuilder model = new FoodModelBuilder())
            {
                switch (id)
                {
                    case "lamb": case "beef_slice": case "pork_belly": SlicedMeat(model, id); break;
                    case "chicken_breast": case "chicken_thigh": Chicken(model, id); break;
                    case "beef_offal": Offal(model, id); break;
                    case "beef_tripe": Tripe(model, id); break;
                    case "egg": case "quail_egg": Eggs(model, id); break;
                    case "clam": Clam(model, id); break;
                    case "squid": Squid(model, id); break;
                    case "shrimp": Shrimp(model, id); break;
                    case "scallop": Scallop(model, id); break;
                    case "shrimp_ball": case "crab_ball": case "fish_ball": case "meat_ball": Balls(model, id); break;
                    case "crab_stick": CrabStick(model, id); break;
                    case "chikuwa": Chikuwa(model, id); break;
                    case "tteokbokki": RiceCakes(model, id); break;
                    case "hanpen": Hanpen(model, id); break;
                    case "wonton": Wonton(model, id); break;
                    case "dumpling": Dumpling(model, id); break;
                    case "youtiao": Youtiao(model, id); break;
                    case "duck_blood": Blood(model, id); break;
                    case "chicken_feet": ChickenFeet(model, id); break;
                }
                return model.Finish(id);
            }
        }

        /// <summary>色柄の所有権をビルダーに移し、加熱時の色・艶と透けを設定します。</summary>
        private static void Surface(FoodModelBuilder model, string id, int group, uint dark, uint light,
            FoodPattern pattern, Color cooked, float rawGloss = .34f, float cookedGloss = .70f,
            float alpha = 1f)
        {
            Texture2D texture = FoodTextures.Create(id + "/" + group, FoodTextures.Rgb(dark), FoodTextures.Rgb(light), pattern);
            SetTexture(model, group, texture, cooked, rawGloss, cookedGloss, alpha);
        }

        /// <summary>専用色柄にも共通の加熱・過加熱特性を適用します。</summary>
        private static void SetTexture(FoodModelBuilder model, int group, Texture2D texture, Color cooked,
            float rawGloss = .34f, float cookedGloss = .70f, float alpha = 1f)
        {
            Color over = new Color(cooked.r * .77f, cooked.g * .74f, cooked.b * .69f, cooked.a);
            model.SetSurface(group, texture, new FoodSurface(cooked, over, rawGloss, cookedGloss, alpha, .52f), alpha < 1f);
        }

        /// <summary>淡色食材を茹でた時の、わずかな黄味と濡れた艶に用いる倍率です。</summary>
        private static Color PaleCooked(float alpha = 1f)
        {
            return new Color(.98f, .96f, .89f, alpha);
        }

        /// <summary>赤身を灰褐色寄りに抑える倍率です。脂身とは別の表面に適用します。</summary>
        private static Color MeatCooked()
        {
            return new Color(.76f, .82f, .74f, 1f);
        }

        /// <summary>薄い不規則な切り身を三枚重ね、霜降りと豚バラの脂層を表現します。</summary>
        private static void SlicedMeat(FoodModelBuilder model, string id)
        {
            bool pork = id == "pork_belly";
            if (pork)
                SetTexture(model, 0, Texture(id, Detail.PorkLayers, 0xAD6260, 0xF8E7D2), MeatCooked(), .40f, .62f);
            else
                Surface(model, id, 0, id == "lamb" ? 0xA66261u : 0x995454u,
                    0xF6DDCE, FoodPattern.Marbling, MeatCooked(), .40f, .62f);
            Surface(model, id, 1, 0xE0C4A8, 0xFFF1DA, FoodPattern.Fibers, PaleCooked(), .48f);
            for (int i = 0; i < 3; i++)
            {
                Vector3 position = new Vector3((i - 1) * .021f, i * .014f, (i - 1) * .013f);
                Vector3 rotation = new Vector3(i * 4f, (i - 1) * 22f, (i - 1) * 6f);
                model.Add(0, Slice(.102f, .146f, .0028f, .010f, i * .7f), position, Vector3.one, rotation);
                model.Add(1, FatRibbon(.102f, .146f, i * .7f), position + Vector3.up * .0017f, Vector3.one, rotation);
            }
        }

        /// <summary>そぎ切りの鶏肉を三片重ね、もも肉だけ薄い皮を上面に付けます。</summary>
        private static void Chicken(FoodModelBuilder model, string id)
        {
            bool thigh = id == "chicken_thigh";
            Surface(model, id, 0, thigh ? 0xB78070u : 0xD5AFA1u, thigh ? 0xEFC2AAu : 0xFFE4CCu,
                FoodPattern.Fibers, new Color(.88f, 1.02f, .98f, 1f), .43f);
            if (thigh) Surface(model, id, 1, 0xDEBD86, 0xFFF0BD, FoodPattern.Pores, PaleCooked(), .58f, .82f);
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = new Vector3((i - 1) * .036f, i * .009f, (i - 1) * .026f);
                Vector3 r = new Vector3(8f, i * 33f, i * 5f);
                model.Add(0, FoodKit.Ellipsoid(i + 40, .10f, 12, 6), p, new Vector3(.078f, .035f, .105f), r);
                if (thigh) model.Add(1, Slice(.061f, .077f, .002f, .004f, i), p + Vector3.up * .017f,
                    Vector3.one, r);
            }
        }

        /// <summary>不規則なもつの塊、脂の結節と巻いた膜を一緒にします。</summary>
        private static void Offal(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x9A7261, 0xDFBA9A, FoodPattern.Pores, MeatCooked(), .55f, .77f);
            Surface(model, id, 1, 0xD5BC91, 0xF9E6BE, FoodPattern.Mottled, PaleCooked(), .55f);
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = new Vector3((i - 1) * .036f, i * .009f, (i % 2) * .033f);
                model.Add(0, FoodKit.Ellipsoid(i + 13, .32f, 10, 5), p, new Vector3(.065f, .053f, .075f));
                model.Add(1, FoodKit.Ellipsoid(i + 83, .22f, 8, 4), p + new Vector3(.015f, .019f, .008f), new Vector3(.038f, .026f, .046f));
                model.Add(0, FoodKit.Tube(new Vector3[] { p + new Vector3(-.023f, .015f, -.025f),
                    p + new Vector3(-.032f, .038f, 0f), p + new Vector3(.008f, .042f, .032f) }, .008f, .005f, 6), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>厚いハチノスの膜に、底を残した十九個の六角形の凹みを作ります。</summary>
        private static void Tripe(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0x8E8062, 0xC7BB94, FoodPattern.Pores, new Color(.91f, .86f, .70f, 1f), .42f);
            Surface(model, id, 1, 0xB4A480, 0xE4D7AC, FoodPattern.Fibers, PaleCooked(), .44f);
            model.Add(0, Slice(.125f, .145f, .012f, .004f, 0f), Vector3.zero, Vector3.one);
            for (int row = -2; row <= 2; row++)
            {
                int count = 5 - Mathf.Abs(row);
                for (int col = 0; col < count; col++)
                {
                    float x = (col - (count - 1) * .5f) * .023f;
                    float z = row * .020f;
                    float y = Wave((x / .125f) + .5f, (z / .145f) + .5f, .004f, 0f) + .006f;
                    model.Add(1, FoodKit.Lathe(new Vector2[] { new Vector2(.012f, 0f),
                        new Vector2(.0118f, .010f), new Vector2(.0083f, .009f), new Vector2(.006f, .001f) }, 6),
                        new Vector3(x, y, z), Vector3.one, new Vector3(0f, 30f, 0f));
                }
            }
        }

        /// <summary>白い殻付き卵と、褐色の斑点がある小ぶりなうずら卵を描き分けます。</summary>
        private static void Eggs(FoodModelBuilder model, string id)
        {
            bool quail = id == "quail_egg";
            if (quail) SetTexture(model, 0, Texture(id, Detail.Quail, 0x513B29, 0xE7D7AB), PaleCooked(), .22f, .65f);
            else Surface(model, id, 0, 0xE1DDCB, 0xFFF8E9, FoodPattern.Pores, PaleCooked(), .23f, .67f);
            int count = quail ? 3 : 1;
            for (int i = 0; i < count; i++)
                model.Add(0, EggMesh(), new Vector3((i - (count - 1) * .5f) * .039f, i * .006f, (i % 2) * .027f),
                    quail ? new Vector3(.049f, .066f, .049f) : new Vector3(.083f, .118f, .083f), new Vector3(65f, i * 43f, 8f));
        }

        /// <summary>開いた二枚の殻を放射状の筋と同心円模様で作り、内部の貝肉を添えます。</summary>
        private static void Clam(FoodModelBuilder model, string id)
        {
            SetTexture(model, 0, Texture(id, Detail.Shell, 0x52483B, 0xC9B995), new Color(.88f, .84f, .77f, 1f), .38f, .75f);
            Surface(model, id, 1, 0xD6CDB6, 0xFFF4D7, FoodPattern.Translucent, PaleCooked(), .65f, .84f);
            Surface(model, id, 2, 0xB2936E, 0xF2D3A3, FoodPattern.Fibers, PaleCooked(), .62f, .83f);
            model.Add(0, Shell(false), Vector3.zero, Vector3.one);
            model.Add(1, Shell(true), Vector3.up * .001f, Vector3.one);
            model.Add(0, Shell(false), new Vector3(0f, .023f, -.004f), Vector3.one, new Vector3(-37f, 0f, 180f));
            model.Add(1, Shell(true), new Vector3(0f, .022f, -.004f), Vector3.one, new Vector3(-37f, 0f, 180f));
            model.Add(2, FoodKit.Ellipsoid(71, .08f, 10, 5), new Vector3(0f, .009f, .036f), new Vector3(.070f, .022f, .049f));
        }

        /// <summary>厚いイカの輪切り二枚と、格子切り込み付きの身・短いゲソを作ります。</summary>
        private static void Squid(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xDAD5CC, 0xFFF7E6, FoodPattern.Translucent, PaleCooked(.97f), .52f, .84f, .91f);
            Surface(model, id, 1, 0xAC9E92, 0xD7CBBA, FoodPattern.Fibers, PaleCooked(), .48f);
            for (int i = 0; i < 2; i++)
                model.Add(0, FoodKit.Lathe(new Vector2[] { new Vector2(.037f, -.009f), new Vector2(.041f, -.007f),
                    new Vector2(.041f, .007f), new Vector2(.037f, .009f), new Vector2(.026f, .008f),
                    new Vector2(.026f, -.008f), new Vector2(.037f, -.009f) }, 16),
                    new Vector3((i - .5f) * .054f, i * .013f, -.028f), new Vector3(1f, 1f, .85f), new Vector3(7f, i * 20f, 4f));
            model.Add(0, Slice(.062f, .065f, .009f, .004f, 0f), new Vector3(.027f, .011f, .049f), Vector3.one);
            for (int i = 0; i < 4; i++)
            {
                float p = -.021f + i * .014f;
                model.Add(1, FoodKit.Tube(new Vector3[] { new Vector3(.027f + p, .020f, .020f), new Vector3(.027f + p, .020f, .078f) }, .0007f, .0007f, 3), Vector3.zero, Vector3.one);
                model.Add(1, FoodKit.Tube(new Vector3[] { new Vector3(-.002f, .020f, .049f + p), new Vector3(.056f, .020f, .049f + p) }, .0007f, .0007f, 3), Vector3.zero, Vector3.one);
            }
            for (int i = 0; i < 3; i++)
                model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(-.049f + i * .008f, .005f, .009f),
                    new Vector3(-.061f + i * .009f, .015f, .047f), new Vector3(-.040f + i * .012f, .021f, .075f) }, .0038f, .0012f, 5), Vector3.zero, Vector3.one);
        }

        /// <summary>七節の湾曲胴体、頭・尾扇・脚・ひげで殻付きエビを作ります。</summary>
        private static void Shrimp(FoodModelBuilder model, string id)
        {
            Color cooked = new Color(1.55f, .66f, .35f, .98f);
            Surface(model, id, 0, 0x99958C, 0xDCD0BB, FoodPattern.Translucent, cooked, .52f, .85f, .88f);
            Surface(model, id, 1, 0x8A7466, 0xC0A68F, FoodPattern.Fibers, new Color(1.70f, .64f, .32f, 1f), .40f, .76f);
            Surface(model, id, 2, 0x22231F, 0x4A4037, FoodPattern.Mottled, Color.white, .76f, .90f);
            Vector3[] center = new Vector3[8];
            for (int i = 0; i < center.Length; i++)
            {
                float a = (-130f + i * 29f) * Mathf.Deg2Rad;
                center[i] = new Vector3(Mathf.Cos(a) * .050f, .006f + .003f * Mathf.Sin(i), Mathf.Sin(a) * .050f);
            }
            for (int i = 0; i < 7; i++)
            {
                float radius = Mathf.Lerp(.018f, .006f, i / 6f);
                model.Add(0, FoodKit.Tube(new Vector3[] { center[i], Vector3.Lerp(center[i], center[i + 1], .94f) }, radius, radius * .84f, 10), Vector3.zero, Vector3.one);
                model.Add(1, FoodKit.Tube(new Vector3[] { Vector3.Lerp(center[i], center[i + 1], .80f), center[i + 1] }, radius * .92f, radius * .87f, 8), Vector3.zero, Vector3.one);
            }
            Vector3 head = center[0] + new Vector3(.005f, .007f, -.025f);
            model.Add(0, FoodKit.Ellipsoid(12, .02f, 12, 6), head, new Vector3(.038f, .035f, .063f), new Vector3(0f, -15f, 0f));
            model.Add(1, FoodKit.Tube(new Vector3[] { head, head + new Vector3(.009f, .004f, -.045f) }, .008f, .001f, 5), Vector3.zero, Vector3.one);
            for (int side = -1; side <= 1; side += 2)
            {
                model.Add(2, FoodKit.Ellipsoid(side + 10, 0f, 6, 3), head + new Vector3(side * .015f, .013f, -.018f), Vector3.one * .005f);
                model.Add(1, FoodKit.Tube(new Vector3[] { head + new Vector3(side * .008f, .006f, -.020f),
                    head + new Vector3(side * .029f, .015f, -.045f), head + new Vector3(side * .043f, .019f, -.055f) }, .0009f, .0004f, 4), Vector3.zero, Vector3.one);
                for (int leg = 0; leg < 3; leg++)
                    model.Add(1, FoodKit.Tube(new Vector3[] { center[leg + 1],
                        center[leg + 1] + new Vector3(side * .023f, -.012f, -.005f),
                        center[leg + 1] + new Vector3(side * .030f, -.015f, .010f) }, .0018f, .0007f, 4), Vector3.zero, Vector3.one);
            }
            for (int i = 0; i < 3; i++)
                model.Add(1, FoodKit.Leaf(.018f, .029f, .08f, .03f, 4, 2), center[7], Vector3.one, new Vector3(0f, 115f + i * 33f, 0f));
        }

        /// <summary>平らな切断面の貝柱を二つ並べ、一方に波打つひもを添えます。</summary>
        private static void Scallop(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xDCD5BD, 0xFFF5DF, FoodPattern.Fibers, PaleCooked(), .48f, .80f);
            Surface(model, id, 1, 0xB7956E, 0xEDD5A5, FoodPattern.Mottled, PaleCooked(), .60f, .82f);
            for (int i = 0; i < 2; i++)
                model.Add(0, FoodKit.Tube(new Vector3[] { Vector3.down * .019f, Vector3.up * .019f }, .035f, .033f, 18),
                    new Vector3((i - .5f) * .060f, i * .006f, (i - .5f) * .025f), Vector3.one);
            Vector3[] fringe = new Vector3[19];
            for (int i = 0; i < fringe.Length; i++)
            {
                float a = i * Mathf.PI * 2f / (fringe.Length - 1);
                float r = .039f + .004f * Mathf.Sin(i * 2.7f);
                fringe[i] = new Vector3(-.030f + Mathf.Cos(a) * r, -.010f + .004f * Mathf.Cos(i * 2f), -.0125f + Mathf.Sin(a) * r);
            }
            model.Add(1, FoodKit.Tube(fringe, .004f, .004f, 4), Vector3.zero, Vector3.one);
        }

        /// <summary>団子の灰色魚肉・桃色エビ片・赤いカニ片・肉の焼き色を描き分けます。</summary>
        private static void Balls(FoodModelBuilder model, string id)
        {
            bool meat = id == "meat_ball";
            uint dark = meat ? 0x75442Au : id == "fish_ball" ? 0x8B8F86u : id == "crab_ball" ? 0xCF7965u : 0xCB897Eu;
            uint light = meat ? 0xBA8C5Au : id == "fish_ball" ? 0xD5D4BDu : 0xF7DEC2u;
            SetTexture(model, 0, Texture(id, Detail.Speckles, dark, light),
                meat ? new Color(.87f, .80f, .67f, 1f) : PaleCooked(), meat ? .26f : .39f, .76f);
            for (int i = 0; i < 3; i++)
                model.Add(0, FoodKit.Ellipsoid(FoodTextures.Seed(id) % 100 + i, meat ? .16f : .065f, 12, 7),
                    new Vector3((i - 1) * .038f, (i % 2) * .027f, (i % 2) * .027f), new Vector3(.062f, .059f, .062f));
        }

        /// <summary>かにかまを白い繊維質の棒と、三面を覆う薄い赤い外皮で作ります。</summary>
        private static void CrabStick(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xD5D2BE, 0xFFF4DE, FoodPattern.Fibers, PaleCooked(), .35f, .74f);
            Surface(model, id, 1, 0xC74736, 0xF47850, FoodPattern.Fibers, new Color(.94f, .85f, .73f, 1f), .41f, .78f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = new Vector3((i - .5f) * .041f, i * .013f, 0f);
                Vector3 r = new Vector3(0f, (i - .5f) * 16f, 0f);
                model.Add(0, RoundedBlock(new Vector3(.032f, .025f, .155f), .005f), p, Vector3.one, r);
                model.Add(1, Slice(.029f, .151f, .001f, .001f, i), p + Vector3.up * .013f, Vector3.one, r);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 offset = Quaternion.Euler(r) * new Vector3(side * .014f, .004f, 0f);
                    model.Add(1, FoodKit.Sheet(.013f, .150f, 0f, .0004f, 6, 2), p + offset,
                        Vector3.one, new Vector3(0f, r.y, side * 80f));
                }
                for (int fiber = 0; fiber < 5; fiber++)
                    model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3((fiber - 2) * .005f, .014f, -.079f),
                        new Vector3((fiber - 2) * .005f, .014f, -.069f) }, .0011f, .0011f, 4), p, Vector3.one, r);
            }
        }

        /// <summary>ちくわを実際に貫通した筒にし、白い端と中央の焼き色を付けます。</summary>
        private static void Chikuwa(FoodModelBuilder model, string id)
        {
            SetTexture(model, 0, Texture(id, Detail.Chikuwa, 0x8A431D, 0xF5E7C8), PaleCooked(), .35f, .71f);
            Surface(model, id, 1, 0xD8CDB0, 0xFFF2D7, FoodPattern.Fibers, PaleCooked(), .40f, .73f);
            model.Add(0, FoodKit.Lathe(new Vector2[] { new Vector2(.021f, -.076f), new Vector2(.025f, -.066f),
                new Vector2(.026f, -.045f), new Vector2(.025f, .043f), new Vector2(.024f, .066f), new Vector2(.020f, .076f) }, 16),
                Vector3.zero, Vector3.one, new Vector3(76f, 0f, -9f));
            model.Add(1, FoodKit.Lathe(new Vector2[] { new Vector2(.020f, -.076f), new Vector2(.011f, -.076f),
                new Vector2(.011f, .076f), new Vector2(.020f, .076f) }, 16), Vector3.zero, Vector3.one, new Vector3(76f, 0f, -9f));
        }

        /// <summary>丸い縁と平らな切り口を持つ白い棒餅を三本並べます。</summary>
        private static void RiceCakes(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xDDD9C9, 0xFFF8E8, FoodPattern.Translucent, PaleCooked(), .30f, .85f);
            for (int i = 0; i < 3; i++)
                model.Add(0, FoodKit.Lathe(new Vector2[] { new Vector2(0f, -.060f), new Vector2(.012f, -.060f),
                    new Vector2(.017f, -.055f), new Vector2(.018f, -.042f), new Vector2(.018f, .042f),
                    new Vector2(.017f, .055f), new Vector2(.012f, .060f), new Vector2(0f, .060f) }, 12),
                    new Vector3((i - 1) * .028f, i * .005f, 0f), Vector3.one, new Vector3(83f, (i - 1) * 12f, 0f));
        }

        /// <summary>はんぺんの角を丸め、微細な孔のある厚い白い四角を二枚重ねます。</summary>
        private static void Hanpen(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xDBD9C7, 0xFFF8E9, FoodPattern.Pores, PaleCooked(), .18f, .65f);
            for (int i = 0; i < 2; i++)
                model.Add(0, RoundedBlock(new Vector3(.107f, .033f, .095f), .010f),
                    new Vector3((i - .5f) * .028f, i * .027f, (i - .5f) * .018f), Vector3.one, new Vector3(0f, i * 19f, 0f));
        }

        /// <summary>ワンタンの透ける薄皮、中央の餡と、垂れた四隅のひだを作ります。</summary>
        private static void Wonton(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xDECCAA, 0xFFF1D1, FoodPattern.Translucent, PaleCooked(.96f), .29f, .81f, .88f);
            Surface(model, id, 1, 0xA68B66, 0xD7B28A, FoodPattern.Mottled, new Color(.84f, .86f, .74f, 1f), .39f);
            model.Add(1, FoodKit.Ellipsoid(34, .06f, 10, 5), new Vector3(0f, .006f, 0f), new Vector3(.051f, .035f, .045f));
            model.Add(0, Pouch(false), Vector3.zero, Vector3.one);
            for (int i = 0; i < 4; i++)
                model.Add(0, FoodKit.Leaf(.055f, .076f, .25f, .10f, 6, 4), new Vector3(0f, .009f, 0f),
                    Vector3.one, new Vector3(-14f, 45f + i * 90f, i % 2 == 0 ? 12f : -12f));
        }

        /// <summary>水餃子を三日月の餡の膨らみと、波打つ閉じ目・八つのひだで作ります。</summary>
        private static void Dumpling(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xDED1B4, 0xFFF4D8, FoodPattern.Translucent, PaleCooked(.96f), .33f, .83f, .90f);
            Surface(model, id, 1, 0xD1C19D, 0xF4E6C2, FoodPattern.Fibers, PaleCooked(), .34f, .79f);
            model.Add(0, Pouch(true), Vector3.zero, Vector3.one);
            Vector3[] seam = new Vector3[17];
            for (int i = 0; i < seam.Length; i++)
            {
                float a = i * Mathf.PI / (seam.Length - 1);
                seam[i] = new Vector3(-.067f * Mathf.Cos(a), .014f + .030f * Mathf.Sin(a), .027f * Mathf.Sin(a));
            }
            model.Add(1, FoodKit.Tube(seam, .0026f, .0026f, 5), Vector3.zero, Vector3.one);
            for (int i = 1; i <= 8; i++)
            {
                float a = i * Mathf.PI / 9f;
                Vector3 p = new Vector3(-.065f * Mathf.Cos(a), .014f + .030f * Mathf.Sin(a), .027f * Mathf.Sin(a));
                model.Add(1, FoodKit.Tube(new Vector3[] { p + new Vector3(-.004f, -.012f, -.016f),
                    p + new Vector3(.002f, -.002f, -.003f), p + new Vector3(.003f, .002f, .002f) }, .0019f, .0012f, 4), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>油条の二本が接した揚げパン、縦の割れ目と多孔質の切り口を作ります。</summary>
        private static void Youtiao(FoodModelBuilder model, string id)
        {
            SetTexture(model, 0, Texture(id, Detail.Bread, 0x9A4D1C, 0xF4C06A), new Color(.92f, .86f, .71f, 1f), .23f, .61f);
            Surface(model, id, 1, 0xC89B50, 0xF9DA95, FoodPattern.Pores, PaleCooked(), .14f, .55f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = new Vector3((i - .5f) * .034f, 0f, 0f);
                model.Add(0, FoodKit.Lathe(new Vector2[] { new Vector2(.018f, -.085f), new Vector2(.023f, -.066f),
                    new Vector2(.022f, -.029f), new Vector2(.025f, .002f), new Vector2(.022f, .042f), new Vector2(.020f, .085f) }, 12),
                    p, new Vector3(1f, 1f, .88f), new Vector3(90f, 0f, 0f));
                for (int end = -1; end <= 1; end += 2)
                    model.Add(1, FoodKit.Tube(new Vector3[] { new Vector3(0f, 0f, end * .084f), new Vector3(0f, 0f, end * .086f) }, .017f, .017f, 12),
                        p, new Vector3(1f, .88f, 1f));
            }
        }

        /// <summary>鴨血のなめらかな暗赤色の角切りに、小さな気泡の色柄を付けます。</summary>
        private static void Blood(FoodModelBuilder model, string id)
        {
            SetTexture(model, 0, Texture(id, Detail.Blood, 0x492222, 0x804339), new Color(.74f, .76f, .72f, 1f), .69f, .86f);
            for (int i = 0; i < 2; i++)
                model.Add(0, RoundedBlock(new Vector3(.075f, .041f, .083f), .003f),
                    new Vector3((i - .5f) * .044f, i * .020f, (i - .5f) * .035f), Vector3.one, new Vector3(0f, i * 15f, 0f));
        }

        /// <summary>鶏の足を足首・掌・三本の前指・後指と短い爪で構成します。</summary>
        private static void ChickenFeet(FoodModelBuilder model, string id)
        {
            Surface(model, id, 0, 0xBE964C, 0xF2D997, FoodPattern.Pores, new Color(.93f, .88f, .71f, 1f), .47f, .87f);
            Surface(model, id, 1, 0xB3A078, 0xEAD9B0, FoodPattern.Fibers, PaleCooked(), .40f, .77f);
            model.Add(0, FoodKit.Tube(new Vector3[] { new Vector3(0f, .023f, -.076f), new Vector3(.004f, .016f, -.045f),
                new Vector3(0f, .001f, -.007f) }, .010f, .014f, 8), Vector3.zero, Vector3.one);
            model.Add(0, FoodKit.Ellipsoid(43, .08f, 10, 5), Vector3.zero, new Vector3(.050f, .027f, .059f));
            for (int i = 0; i < 4; i++)
            {
                bool back = i == 3;
                float x = back ? -.026f : (i - 1) * .033f;
                float z = back ? -.031f : i == 1 ? .083f : .065f;
                Vector3 start = new Vector3(back ? -.012f : (i - 1) * .012f, 0f, .011f);
                Vector3 middle = new Vector3(x * .69f, .004f, z * .55f);
                Vector3 end = new Vector3(x, -.002f, z);
                model.Add(0, FoodKit.Tube(new Vector3[] { start, middle, end }, .0068f, .0035f, 7), Vector3.zero, Vector3.one);
                model.Add(0, FoodKit.Ellipsoid(i, .03f, 6, 3), middle, new Vector3(.014f, .012f, .015f));
                Vector3 nail = end + new Vector3(back ? -.005f : x * .10f, -.004f, back ? -.010f : .011f);
                model.Add(1, FoodKit.Tube(new Vector3[] { end, nail }, .0034f, .0006f, 5), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>波打つ面の高さを返し、脂の縁とハチノスを同じ面に沿わせます。</summary>
        private static float Wave(float u, float v, float amplitude, float phase)
        {
            return amplitude * (Mathf.Sin(u * Mathf.PI * 4f + v * 3f + phase) + .35f * Mathf.Sin(v * 17f + phase));
        }

        /// <summary>丸めた不規則な輪郭の薄い切り身を、上下面と縁を閉じて生成します。</summary>
        private static Mesh Slice(float width, float length, float thickness, float amplitude, float phase)
        {
            const int rows = 8;
            const int columns = 6;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            int layer = (rows + 1) * (columns + 1);
            for (int side = 0; side < 2; side++)
                for (int row = 0; row <= rows; row++)
                    for (int col = 0; col <= columns; col++)
                    {
                        float u = (float)col / columns;
                        float v = (float)row / rows;
                        float outline = .68f + .32f * Mathf.Sin(v * Mathf.PI);
                        outline *= 1f + .035f * Mathf.Sin(v * 23f + phase);
                        vertices.Add(new Vector3((u - .5f) * width * outline,
                            Wave(u, v, amplitude, phase) + (side == 0 ? .5f : -.5f) * thickness, (v - .5f) * length));
                        uv.Add(new Vector2(u, v));
                        if (col == columns || row == rows) continue;
                        int a = side * layer + row * (columns + 1) + col;
                        if (side == 0) Quad(triangles, a, a + columns + 1, a + columns + 2, a + 1);
                        else Quad(triangles, a, a + 1, a + columns + 2, a + columns + 1);
                    }
            for (int row = 0; row < rows; row++)
            {
                int left = row * (columns + 1);
                int right = left + columns;
                Quad(triangles, left, left + layer, left + columns + 1 + layer, left + columns + 1);
                Quad(triangles, right, right + columns + 1, right + columns + 1 + layer, right + layer);
            }
            for (int col = 0; col < columns; col++)
            {
                int front = rows * (columns + 1) + col;
                Quad(triangles, col, col + 1, col + 1 + layer, col + layer);
                Quad(triangles, front, front + layer, front + 1 + layer, front + 1);
            }
            return MeshFrom("Food Thick Slice", vertices, triangles, uv);
        }

        /// <summary>切り身の片側の縁に沿って、幅に揺らぎのある白い脂の帯を生成します。</summary>
        private static Mesh FatRibbon(float width, float length, float phase)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            for (int row = 0; row <= 8; row++)
            {
                float v = row / 8f;
                float outline = (.68f + .32f * Mathf.Sin(v * Mathf.PI)) * (1f + .035f * Mathf.Sin(v * 23f + phase));
                for (int col = 0; col < 2; col++)
                {
                    float u = col == 0 ? .025f : .10f + .035f * Mathf.Sin(v * 19f + phase);
                    vertices.Add(new Vector3((u - .5f) * width * outline, Wave(u, v, .010f, phase), (v - .5f) * length));
                    uv.Add(new Vector2(col, v));
                }
                if (row < 8) Quad(triangles, row * 2, row * 2 + 2, row * 2 + 3, row * 2 + 1);
            }
            return MeshFrom("Food Fat Edge", vertices, triangles, uv);
        }

        /// <summary>球の上端を絞り、卵特有の左右非対称な卵形を生成します。</summary>
        private static Mesh EggMesh()
        {
            Mesh mesh = FoodKit.Ellipsoid(11, 0f, 16, 10);
            try
            {
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    float taper = 1f - .22f * (vertices[i].y + .5f);
                    vertices[i].x *= taper;
                    vertices[i].z *= taper;
                }
                mesh.vertices = vertices;
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
            catch { OwnedObjects.Destroy(mesh); throw; }
        }

        /// <summary>あさりのヒンジから広がる扇形の殻に、放射方向の低い稜線を付けます。</summary>
        private static Mesh Shell(bool inside)
        {
            const int rows = 5;
            const int columns = 24;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            for (int row = 0; row <= rows; row++)
                for (int col = 0; col <= columns; col++)
                {
                    float r = (float)row / rows;
                    float a = (float)col / columns * Mathf.PI;
                    float ridge = inside ? 0f : .0014f * Mathf.Cos(a * 24f) * r;
                    float y = -.019f * Mathf.Sin(r * Mathf.PI) - .002f * r + ridge + (inside ? .0015f : 0f);
                    vertices.Add(new Vector3(Mathf.Cos(a) * .066f * r, y, Mathf.Sin(a) * .086f * r));
                    uv.Add(new Vector2((float)col / columns, r));
                    if (row == rows || col == columns) continue;
                    int i = row * (columns + 1) + col;
                    if (row == 0)
                    {
                        if (inside) FoodKit.Triangle(triangles, i, i + columns + 2, i + columns + 1);
                        else FoodKit.Triangle(triangles, i, i + columns + 1, i + columns + 2);
                    }
                    else if (inside) Quad(triangles, i, i + 1, i + columns + 2, i + columns + 1);
                    else Quad(triangles, i, i + columns + 1, i + columns + 2, i + 1);
                }
            return MeshFrom("Food Clam Shell", vertices, triangles, uv);
        }

        /// <summary>角を小さな丸みにした直方体を六つの独立面から生成し、平面UVを付けます。</summary>
        private static Mesh RoundedBlock(Vector3 size, float bevel)
        {
            const int divisions = 4;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            List<Vector3> normals = new List<Vector3>();
            Vector3 half = size * .5f;
            Vector3 inner = half - Vector3.one * bevel;
            for (int face = 0; face < 6; face++)
            {
                Vector3 normal = face == 0 ? Vector3.right : face == 1 ? Vector3.left : face == 2 ? Vector3.up :
                    face == 3 ? Vector3.down : face == 4 ? Vector3.forward : Vector3.back;
                Vector3 horizontal = Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.Cross(Vector3.up, normal);
                Vector3 vertical = Vector3.Cross(normal, horizontal);
                int start = vertices.Count;
                for (int row = 0; row <= divisions; row++)
                    for (int col = 0; col <= divisions; col++)
                    {
                        float u = (float)col / divisions;
                        float v = (float)row / divisions;
                        Vector3 unit = normal + horizontal * (u * 2f - 1f) + vertical * (v * 2f - 1f);
                        Vector3 p = Vector3.Scale(unit, half);
                        Vector3 nearest = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 roundedNormal = (p - nearest).normalized;
                        vertices.Add(nearest + roundedNormal * bevel);
                        normals.Add(roundedNormal);
                        uv.Add(new Vector2(u, v));
                        if (row == divisions || col == divisions) continue;
                        int i = start + row * (divisions + 1) + col;
                        Quad(triangles, i, i + 1, i + divisions + 2, i + divisions + 1);
                    }
            }
            return MeshFrom("Food Rounded Block", vertices, triangles, uv, normals);
        }

        /// <summary>餡の膨らみを包む閉じた皮を生成し、餃子では三日月の輪郭にします。</summary>
        private static Mesh Pouch(bool crescent)
        {
            const int segments = 20;
            const int rings = 8;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            for (int row = 0; row <= rings; row++)
                for (int col = 0; col <= segments; col++)
                {
                    float v = (float)row / rings;
                    float u = (float)col / segments;
                    float latitude = v * Mathf.PI;
                    float a = u * Mathf.PI * 2f;
                    float ring = row == 0 || row == rings ? 0f : Mathf.Sin(latitude);
                    float x = Mathf.Cos(a) * ring;
                    float z = Mathf.Sin(a) * ring;
                    float pleat = .0018f * Mathf.Cos(a * 10f) * ring * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(latitude)), 2f);
                    Vector3 point;
                    if (crescent)
                    {
                        float curve = .022f * (1f - x * x);
                        point = new Vector3(x * .068f, .010f + Mathf.Cos(latitude) * .031f + pleat,
                            z * .029f + curve);
                    }
                    else point = new Vector3(x * .035f, .012f + Mathf.Cos(latitude) * .024f + pleat, z * .033f);
                    vertices.Add(point); uv.Add(new Vector2(u, v));
                    if (row == rings || col == segments) continue;
                    int i = row * (segments + 1) + col;
                    if (row != 0) FoodKit.Triangle(triangles, i, i + 1, i + segments + 1);
                    if (row != rings - 1) FoodKit.Triangle(triangles, i + 1, i + segments + 2, i + segments + 1);
                }
            return MeshFrom("Food Wrapped Filling", vertices, triangles, uv);
        }

        /// <summary>食材固有の斑点・層・殻模様を128pxのミップ付きテクスチャとして生成します。</summary>
        private static Texture2D Texture(string id, Detail detail, uint darkRgb, uint lightRgb)
        {
            const int size = 128;
            float offset = (FoodTextures.Seed(id) & 65535) * .013f;
            Color dark = FoodTextures.Rgb(darkRgb);
            Color light = FoodTextures.Rgb(lightRgb);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Food Detail " + id, filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            try
            {
                Color[] pixels = new Color[size * size];
                for (int row = 0; row < size; row++)
                    for (int col = 0; col < size; col++)
                    {
                        float u = (col + .5f) / size;
                        float v = (row + .5f) / size;
                        float n = Mathf.PerlinNoise(u * 9f + offset, v * 11f + offset);
                        float fine = Mathf.PerlinNoise(u * 65f + offset, v * 65f + offset);
                        float value;
                        switch (detail)
                        {
                            case Detail.Quail:
                                float spots = Mathf.PerlinNoise(u * 22f + offset, v * 28f + offset);
                                value = spots > .61f ? .06f + fine * .17f : .87f + n * .12f;
                                break;
                            case Detail.PorkLayers:
                                float stripe = Mathf.Repeat(u * 4.2f + .10f * Mathf.Sin(v * 11f) + n * .13f, 1f);
                                value = stripe < .37f ? .88f + fine * .12f : .08f + n * .25f;
                                break;
                            case Detail.Chikuwa:
                                float band = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - .12f) / .16f))
                                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - .72f) / .16f)));
                                value = 1f - band * (.36f + n * .56f);
                                value -= .04f * Mathf.Pow(.5f + .5f * Mathf.Sin(u * 130f + n * 4f), 8f);
                                break;
                            case Detail.Shell:
                                float radial = .5f + .5f * Mathf.Sin(u * 120f + n * 3f);
                                float growth = .5f + .5f * Mathf.Sin(v * 85f + n * 4f);
                                value = .22f + n * .38f + radial * .16f + growth * .14f;
                                break;
                            case Detail.Blood:
                                value = fine > .72f ? .09f : .48f + n * .16f;
                                break;
                            case Detail.Bread:
                                float split = Mathf.Pow(.5f + .5f * Mathf.Sin(u * 32f + n * 3f), 10f);
                                value = .22f + n * .55f + split * .18f;
                                if (fine > .70f) value *= .48f;
                                break;
                            default:
                                value = fine > .64f ? .13f + n * .14f : .77f + n * .20f;
                                if (id == "meat_ball") value = .15f + n * .70f + fine * .10f;
                                break;
                        }
                        pixels[row * size + col] = Color.Lerp(dark, light, Mathf.Clamp01(value));
                    }
                texture.SetPixels(pixels); texture.Apply(true, true);
                return texture;
            }
            catch { OwnedObjects.Destroy(texture); throw; }
        }

        /// <summary>四角形を二つの三角形で登録します。</summary>
        private static void Quad(List<int> triangles, int a, int b, int c, int d)
        {
            FoodKit.Triangle(triangles, a, b, c); FoodKit.Triangle(triangles, a, c, d);
        }

        /// <summary>有限な頂点から専用メッシュを確定し、失敗した場合はUnityリソースを解放します。</summary>
        private static Mesh MeshFrom(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uv,
            List<Vector3> normals = null)
        {
            foreach (Vector3 p in vertices)
                if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                    throw new ArgumentException("食材メッシュには有限の頂点が必要です。");
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            try
            {
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
                if (normals == null) mesh.RecalculateNormals(); else mesh.SetNormals(normals);
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
            catch { OwnedObjects.Destroy(mesh); throw; }
        }
    }
}
