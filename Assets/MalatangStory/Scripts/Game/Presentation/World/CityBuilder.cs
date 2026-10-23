using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>店舗を囲む商店街を、共有マテリアルごとの結合メッシュで生成します。</summary>
    public sealed class CityBuilder
    {
        private readonly StoreBuilder store;
        private readonly Transform root;
        private readonly float front;
        private readonly System.Random random = new System.Random(4107);
        private readonly List<(Material material, Color color)> emissionMaterials = new List<(Material, Color)>();
        private readonly Material concrete = MaterialLibrary.Lit(new Color(.57f, .55f, .51f), .2f);
        private readonly Material dark = MaterialLibrary.Lit(new Color(.15f, .18f, .21f), .3f);
        private readonly Material metal = MaterialLibrary.Lit(new Color(.54f, .57f, .59f), .5f, .35f);
        private readonly Material white = MaterialLibrary.Lit(new Color(.9f, .88f, .78f), .25f);
        private readonly Material red = MaterialLibrary.Lit(new Color(.65f, .12f, .1f), .3f);
        private readonly Material green = MaterialLibrary.Lit(new Color(.24f, .39f, .23f), .2f);
        private readonly Material[] facades =
        {
            MaterialLibrary.Lit(new Color(.66f, .6f, .51f), .2f),
            MaterialLibrary.Lit(new Color(.53f, .58f, .6f), .2f),
            MaterialLibrary.Lit(new Color(.72f, .66f, .56f), .2f),
            MaterialLibrary.Lit(new Color(.56f, .48f, .43f), .2f)
        };
        private Transform seasonalRoot;
        private string seasonalId;
        private bool seasonalSnow, seasonalQuality;
        private Material windows, pinkNeon, cyanNeon, warmLight, redNeon;
        private static readonly string[] ShopNames = { "便利商店", "理髪 サクラ", "くすり", "香港茶餐廳", "手機修理", "青果 山田", "酒処 路地", "餃子・拉麵" };

        /// <summary>店の前壁に合わせて街の原点を設定します。</summary>
        public CityBuilder(StoreBuilder store, float front)
        {
            this.store = store;
            this.front = front;
            root = new GameObject("Shopping District").transform;
            root.SetParent(store.Root, false);
        }

        /// <summary>道路、建物、小道具と街の動きを生成します。建物はナビ源に登録しません。</summary>
        public StreetLife Build()
        {
            windows = Glow(new Color(1f, .72f, .34f));
            pinkNeon = Glow(new Color(1f, .15f, .45f));
            cyanNeon = Glow(new Color(.16f, .8f, 1f));
            warmLight = Glow(new Color(1f, .9f, .65f));
            redNeon = Glow(new Color(1f, .23f, .09f));
            using (CityGeometry geometry = new CityGeometry())
            {
                BuildGround(geometry);
                BuildBlocks(geometry);
                BuildStreetFurniture(geometry);
                BuildStorefront(geometry);
                Dictionary<Material, Renderer> renderers = geometry.Flush(root, "City", false);
                foreach ((Material material, Color color) entry in emissionMaterials)
                    store.NightEmitters.Add((renderers[entry.material], entry.color));
            }
            StreetLife life = root.gameObject.AddComponent<StreetLife>();
            life.Initialize(front, 4107);
            return life;
        }

        /// <summary>街路樹・植栽・歩道を季節別に結合し、変更時だけ旧メッシュを破棄します。</summary>
        public void ApplySeason(string seasonId, string weatherId, bool highQuality)
        {
            bool snow = seasonId == "winter" && weatherId == "cold";
            if (seasonalRoot != null && seasonalId == seasonId && seasonalSnow == snow && seasonalQuality == highQuality) return;
            if (seasonalRoot != null)
            {
                seasonalRoot.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(seasonalRoot.gameObject);
            }
            seasonalId = seasonId; seasonalSnow = snow; seasonalQuality = highQuality;
            seasonalRoot = new GameObject("Seasonal Street").transform;
            seasonalRoot.SetParent(root, false);
            Material bark = MaterialLibrary.Lit(new Color(.34f, .23f, .17f), .25f);
            Color leafColor = seasonId == "spring" ? new Color(.98f, .58f, .72f)
                : seasonId == "summer" ? new Color(.12f, .34f, .13f) : new Color(.88f, .35f, .07f);
            Material leaves = MaterialLibrary.Lit(leafColor, .25f);
            Material alternate = MaterialLibrary.Lit(seasonId == "autumn" ? new Color(.66f, .12f, .06f) : leafColor * .88f, .25f);
            using (CityGeometry g = new CityGeometry())
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int shrub = 0; shrub < 4; shrub++)
                    {
                        Vector3 p = new Vector3(side * 15 - .9f + shrub * .6f, .5f, front - 3.1f);
                        if (seasonId != "winter") g.Sphere(p, new Vector3(.75f, .58f, .6f), leaves);
                        else g.Line(p - Vector3.up * .25f, p + Vector3.up * .18f, .025f, bark);
                    }
                    for (int tree = 0; tree < 3; tree++)
                    {
                        Vector3 p = new Vector3(side * (5.8f + tree * 10), 0, front - 2.85f);
                        g.Cylinder(p + Vector3.up * 1.25f, .1f, 2.5f, bark);
                        for (int branch = 0; branch < 5; branch++)
                        {
                            float angle = branch * 2.4f;
                            Vector3 tip = p + new Vector3(Mathf.Cos(angle) * .8f, 2.9f + branch % 2 * .35f, Mathf.Sin(angle) * .65f);
                            g.Line(p + Vector3.up * (1.8f + branch * .1f), tip, seasonId == "winter" ? .065f : .035f, bark);
                            if (seasonId == "winter")
                            {
                                g.Line(tip, tip + new Vector3(.34f, .24f, .2f), .032f, bark);
                                g.Line(tip, tip + new Vector3(-.28f, .3f, -.22f), .032f, bark);
                            }
                            if (seasonId != "winter")
                                g.Sphere(tip, new Vector3(1.25f, 1.05f, 1.15f), branch % 2 == 0 ? leaves : alternate);
                        }
                    }
                }
                if (seasonId == "autumn")
                {
                    int count = highQuality ? 64 : 28;
                    Mesh leaf = MeshFactory.Leaf(.14f, .24f, .008f);
                    for (int i = 0; i < count; i++)
                    {
                        float x = Mathf.Sin(i * 7.13f) * (i < count / 2 ? 7f : 28f);
                        float z = front - .5f - Mathf.Repeat(i * .73f, 2.7f);
                        g.Add(leaf, new Vector3(x, .012f, z), Vector3.one, Quaternion.Euler(0, i * 137, 0), i % 2 == 0 ? leaves : alternate);
                    }
                }
                if (snow)
                {
                    Material frost = MaterialLibrary.Lit(new Color(.98f, 1f, 1f), .2f);
                    for (int i = -10; i <= 10; i++)
                    {
                        float x = i * 3.2f;
                        g.Box(new Vector3(x, .028f, front - .65f), new Vector3(2.7f, .045f, .7f), frost);
                        g.Box(new Vector3(x + .35f, .028f, front - 3.25f), new Vector3(2.45f, .045f, .8f), frost);
                        g.Box(new Vector3(x, -.061f, front - 4.25f), new Vector3(2.65f, .032f, .55f), frost);
                        g.Box(new Vector3(x + .2f, -.061f, front - 10.95f), new Vector3(2.65f, .032f, .55f), frost);
                    }
                    Material slush = MaterialLibrary.Lit(new Color(.48f, .46f, .43f), .35f);
                    for (int lane = 0; lane < 2; lane++)
                        for (int track = -1; track <= 1; track += 2)
                            g.Box(new Vector3(0, -.035f, front - 5.5f - lane * 4 + track * .55f), new Vector3(70, .015f, .22f), slush);
                    for (int step = 0; step < 48; step++)
                        g.Box(new Vector3(-12 + step * .5f, .055f, front - 1.4f + (step % 2) * .18f), new Vector3(.14f, .015f, .27f), slush, Quaternion.Euler(0, 80, 0));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        g.Box(new Vector3(side * 15, .44f, front - 3.1f), new Vector3(2.5f, .075f, .62f), frost);
                        for (int shrub = 0; shrub < 4; shrub++)
                            g.Sphere(new Vector3(side * 15 - .9f + shrub * .6f, .57f, front - 3.1f), new Vector3(.48f, .13f, .48f), frost);
                    }
                    g.Box(new Vector3(-23, 2.545f, front - 13.2f), new Vector3(4.5f, .065f, 1.5f), frost);
                }
                g.Flush(seasonalRoot, "Seasonal Street", false);
            }
        }

        /// <summary>夜間に色を保ったまま点灯する共有マテリアルを用意します。</summary>
        private Material Glow(Color color)
        {
            Material material = MaterialLibrary.Emissive(color * .3f, color, 0f);
            emissionMaterials.Add((material, color));
            return material;
        }

        /// <summary>芝生を使わず、フォグ終端より広い市街地の基盤と歩道を敷きます。</summary>
        private void BuildGround(CityGeometry g)
        {
            Material asphalt = MaterialLibrary.Lit(new Color(.23f, .25f, .27f), .15f);
            Material paving = MaterialLibrary.Textured(TextureFactory.Tiles(128, new Color(.63f, .6f, .55f), new Color(.46f, .44f, .41f), 8), Color.white, .2f, new Vector2(28, 2));
            g.Box(new Vector3(0, -.3f, 0), new Vector3(250, .2f, 250), concrete);
            g.Box(new Vector3(0, -.16f, front - 7.5f), new Vector3(220, .16f, 8), asphalt);
            g.Box(new Vector3(0, -.1f, front - 1.9f), new Vector3(220, .2f, 3.8f), paving);
            g.Box(new Vector3(0, -.1f, front - 13.3f), new Vector3(220, .2f, 3.6f), paving);
            store.AddWalkable(new Vector3(0, -.1f, front - 1.9f), new Vector3(19f, .2f, 3.8f));
            for (int side = 0; side < 2; side++)
                g.Box(new Vector3(0, -.03f, front - (side == 0 ? 3.8f : 11.5f)), new Vector3(220, .16f, .22f), white);
            for (int i = -22; i <= 22; i++)
                g.Box(new Vector3(i * 4.8f, -.07f, front - 7.65f), new Vector3(2.5f, .015f, .1f), white);
            for (int i = 0; i < 9; i++)
                g.Box(new Vector3(22, -.065f, front - 4.3f - i * .79f), new Vector3(3f, .02f, .43f), white);
            for (int i = -2; i <= 2; i++)
            {
                g.Cylinder(new Vector3(i * 18 + 5, -.065f, front - 6), .42f, .025f, dark);
                g.Box(new Vector3(i * 18 + 5, -.045f, front - 6), new Vector3(.5f, .012f, .04f), metal);
            }
        }

        /// <summary>隣接・向かい・背後と遠景の街区を決定的に配置します。</summary>
        private void BuildBlocks(CityGeometry g)
        {
            for (int i = 0; i < 4; i++)
            {
                BuildBuilding(g, -11.2f - i * 7.2f, front + .2f, 6.7f, 8.5f, i, false, true);
                BuildBuilding(g, 11.2f + i * 7.2f, front + .2f, 6.7f, 8.5f, i + 4, false, true);
            }
            for (int i = -4; i <= 4; i++)
                BuildBuilding(g, i * 7.2f, front - 15.2f, 6.8f, 8.8f, i + 12, true, true);
            for (int i = -4; i <= 4; i++)
                BuildBuilding(g, i * 7.8f, 10f, 7.3f, 9, i + 22, false, false);
            for (int row = 0; row < 3; row++)
            {
                float distance = 43 + row * 23;
                for (int col = -4; col <= 4; col++)
                {
                    float span = col * 22;
                    BuildBackdrop(g, new Vector3(span, 0, distance), row + col + 8);
                    BuildBackdrop(g, new Vector3(span, 0, -distance + front), row + col + 12);
                    BuildBackdrop(g, new Vector3(distance, 0, span), row + col + 15);
                    BuildBackdrop(g, new Vector3(-distance, 0, span), row + col + 18);
                }
            }
        }

        /// <summary>一階店舗と階ごとの窓、ベランダ、室外機、雨どいを作ります。</summary>
        private void BuildBuilding(CityGeometry g, float x, float edge, float width, float depth, int style, bool opposite, bool labeled)
        {
            var lot = StreetVisualRules.Lot(4107, style);
            width *= lot.Width;
            int floors = lot.Floors;
            float height = floors * 2.6f + .4f;
            float direction = opposite ? -1 : 1;
            float centerZ = edge + direction * depth * .5f;
            Material facade = facades[style % facades.Length];
            g.Box(new Vector3(x, height * .5f, centerZ), new Vector3(width, height, depth), facade);
            g.Box(new Vector3(x, height + .1f, centerZ), new Vector3(width + .18f, .25f, depth + .2f), concrete);
            if (lot.Roof == 1)
            {
                for (int side = -1; side <= 1; side += 2)
                    g.Box(new Vector3(x + side * width * .245f, height + .55f, centerZ), new Vector3(width * .55f, .16f, depth + .5f), red, Quaternion.Euler(0, 0, -side * 16));
            }
            else if (lot.Roof == 2)
                g.Cylinder(new Vector3(x - width * .22f, height + .65f, centerZ), .72f, 1.1f, metal);
            if (labeled)
            {
                g.Box(new Vector3(x + width * .38f, .25f, edge - direction * .65f), new Vector3(.6f, .5f, .5f), style % 2 == 0 ? green : white);
                g.Sphere(new Vector3(x + width * .38f, .72f, edge - direction * .65f), new Vector3(.7f, .6f, .6f), green);
                for (int crate = 0; crate < style % 3; crate++)
                    g.Box(new Vector3(x - width * .35f, .2f + crate * .38f, edge - direction * .65f), new Vector3(.75f, .35f, .55f), concrete);
                if (style % 3 == 1)
                    for (int panel = -2; panel <= 2; panel++)
                        g.Box(new Vector3(x + panel * .35f, 1.9f, edge - direction * .42f), new Vector3(.32f, .7f, .04f), green);
            }
            g.Box(new Vector3(x, 1.2f, edge - direction * .04f), new Vector3(width - .6f, 2.35f, .12f), dark);
            g.Box(new Vector3(x, 1.2f, edge - direction * .12f), new Vector3(width - 1.2f, 1.75f, .04f), windows);
            for (int col = -1; col <= 1; col++)
                g.Box(new Vector3(x + col * (width - .8f) / 3, 1.2f, edge - direction * .17f), new Vector3(.08f, 2.3f, .06f), metal);
            g.Box(new Vector3(x, 2.65f, edge - direction * .18f), new Vector3(width - .25f, .62f, .28f), style % 2 == 0 ? red : green);
            g.Box(new Vector3(x, 3.06f, edge - direction * .36f), new Vector3(width, .12f, .8f), white);
            if (labeled)
            {
                store.Label(ShopNames[style % ShopNames.Length], new Vector3(x, 2.65f, edge - direction * .35f), Quaternion.Euler(0, opposite ? 180 : 0, 0), .04f, Color.white, root);
                if (style % 2 == 0)
                {
                    float sx = x + width * .43f;
                    g.Box(new Vector3(sx, 4.6f, edge - direction * .55f), new Vector3(.85f, 2.1f, .28f), style % 4 == 0 ? pinkNeon : cyanNeon);
                    store.Label(style % 4 == 0 ? "営\n業\n中" : "茶\n餐\n廳", new Vector3(sx, 4.6f, edge - direction * .71f), Quaternion.Euler(0, opposite ? 180 : 0, 0), .032f, Color.white, root);
                }
            }
            for (int floor = 1; floor < floors; floor++)
            {
                float y = 1.4f + floor * 2.6f;
                for (int col = -1; col <= 1; col++)
                {
                    float wx = x + col * width * .28f;
                    g.Box(new Vector3(wx, y, edge - direction * .07f), new Vector3(lot.Windows == 1 ? 1.1f : 1.55f, 1.55f, .12f), dark);
                    g.Box(new Vector3(wx, y, edge - direction * .15f), new Vector3(lot.Windows == 1 ? .94f : 1.37f, 1.33f, .04f), (floor + col + style) % 4 == 0 ? dark : windows);
                    g.Box(new Vector3(wx, y, edge - direction * .19f), new Vector3(.045f, 1.4f, .04f), metal);
                }
                g.Box(new Vector3(x, y - .85f, edge - direction * .32f), new Vector3(width - .6f, .12f, .78f), concrete);
                g.Box(new Vector3(x, y - .35f, edge - direction * .68f), new Vector3(width - .6f, .04f, .05f), metal);
                for (int bar = -3; bar <= 3; bar++)
                    g.Box(new Vector3(x + bar * .83f, y - .59f, edge - direction * .68f), new Vector3(.035f, .5f, .035f), metal);
                g.Box(new Vector3(x + width * .35f, y - .58f, edge - direction * .4f), new Vector3(.78f, .43f, .45f), white);
                g.Box(new Vector3(x + width * .35f, y - .58f, edge - direction * .64f), new Vector3(.55f, .26f, .025f), dark);
                if ((style + floor) % 3 == 0)
                    for (int garment = 0; garment < 3; garment++)
                        g.Box(new Vector3(x - .6f + garment * .45f, y - .15f, edge - direction * .75f), new Vector3(.3f, .45f, .035f), garment % 2 == 0 ? white : red);
            }
            g.Cylinder(new Vector3(x - width * .46f, height * .5f, edge - direction * .16f), .055f, height, metal);
            g.Box(new Vector3(x + .5f, height + .42f, centerZ), new Vector3(1.1f, .65f, .8f), metal);
        }

        /// <summary>フォグの奥まで四方向に続く低コストな建物の輪郭を作ります。</summary>
        private void BuildBackdrop(CityGeometry g, Vector3 p, int style)
        {
            float height = 9 + random.Next(12);
            g.Box(p + Vector3.up * height * .5f, new Vector3(17, height, 17), facades[style % facades.Length]);
            for (int level = 0; level < 3; level++)
            {
                float y = 3 + level * 3;
                g.Box(p + new Vector3(0, y, -8.55f), new Vector3(13, .8f, .04f), windows);
                g.Box(p + new Vector3(0, y, 8.55f), new Vector3(13, .8f, .04f), windows);
                g.Box(p + new Vector3(-8.55f, y, 0), new Vector3(.04f, .8f, 13), windows);
                g.Box(p + new Vector3(8.55f, y, 0), new Vector3(.04f, .8f, 13), windows);
            }
        }

        /// <summary>街灯、電線、信号、バス停と路肩の生活用品を配置します。</summary>
        private void BuildStreetFurniture(CityGeometry g)
        {
            for (int i = -3; i <= 3; i++)
            {
                float x = i * 12 + 5;
                float z = front - 3.55f;
                g.Cylinder(new Vector3(x, 2.3f, z), .065f, 4.6f, metal);
                g.Box(new Vector3(x, 4.45f, z - .3f), new Vector3(.12f, .12f, .7f), metal);
                g.Box(new Vector3(x, 4.38f, z - .65f), new Vector3(.55f, .09f, .32f), warmLight);
                g.Cylinder(new Vector3(x - 2, 3.8f, front - 14.7f), .12f, 7.6f, concrete);
                g.Box(new Vector3(x - 2, 6.6f, front - 14.7f), new Vector3(1.6f, .12f, .15f), dark);
                if (i < 3)
                {
                    for (int wire = 0; wire < 3; wire++)
                    {
                        Vector3 a = new Vector3(x - 2, 6.8f + wire * .2f, front - 14.7f + wire * .2f);
                        Vector3 middle = a + new Vector3(6, -.5f, 0);
                        g.Line(a, middle, .012f, dark);
                        g.Line(middle, a + Vector3.right * 12, .012f, dark);
                    }
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 9;
                float z = front - .8f;
                g.Box(new Vector3(x, .95f, z), new Vector3(.85f, 1.9f, .72f), red);
                g.Box(new Vector3(x, 1.16f, z - .38f), new Vector3(.64f, .91f, .04f), cyanNeon);
                for (int drink = 0; drink < 6; drink++)
                    g.Box(new Vector3(x - .2f + drink % 3 * .2f, .95f + drink / 3 * .37f, z - .42f), new Vector3(.12f, .24f, .04f), white);
                g.Box(new Vector3(x, .37f, z - .38f), new Vector3(.48f, .16f, .03f), dark);
                float binX = side * 10.1f;
                g.Box(new Vector3(binX, .48f, z), new Vector3(.5f, .95f, .5f), metal);
                g.Box(new Vector3(binX, .86f, z - .26f), new Vector3(.28f, .12f, .02f), dark);
                g.Box(new Vector3(side * 15, .2f, front - 3.1f), new Vector3(2.6f, .4f, .6f), concrete);
                for (int bicycle = 0; bicycle < 3; bicycle++)
                    AddBicycle(g, new Vector3(side * 18 + bicycle * .75f, 0, front - 2.6f), Quaternion.Euler(0, 25, 0), red, metal, dark);
                AddScooter(g, new Vector3(side * 21, 0, front - 2.6f));
            }
            float busX = -23;
            g.Box(new Vector3(busX, 2.45f, front - 13.2f), new Vector3(4.6f, .12f, 1.6f), green);
            for (int i = -1; i <= 1; i += 2)
                g.Cylinder(new Vector3(busX + i * 2, 1.2f, front - 13.9f), .06f, 2.4f, metal);
            g.Box(new Vector3(busX, .55f, front - 13.5f), new Vector3(3.4f, .12f, .45f), StoreBuilderMaterialWood());
            g.Box(new Vector3(busX + 3, 1.95f, front - 12.4f), new Vector3(.65f, .9f, .1f), white);
            g.Cylinder(new Vector3(busX + 3, .85f, front - 12.4f), .045f, 1.7f, metal);
            store.Label("商店街前\nBUS", new Vector3(busX + 3, 1.95f, front - 12.32f), Quaternion.identity, .021f, StoreBuilder.Charcoal, root);
            for (int side = 0; side < 2; side++)
            {
                Vector3 p = new Vector3(24, 0, front - (side == 0 ? 3.5f : 11.9f));
                g.Cylinder(p + Vector3.up * 2.3f, .075f, 4.6f, metal);
                g.Box(p + new Vector3(-.65f, 4.2f, 0), new Vector3(1.3f, .5f, .35f), dark);
                g.Sphere(p + new Vector3(-1, 4.2f, side == 0 ? -.2f : .2f), new Vector3(.22f, .22f, .06f), redNeon);
                g.Sphere(p + new Vector3(-.35f, 4.2f, side == 0 ? -.2f : .2f), new Vector3(.22f, .22f, .06f), green);
            }
        }

        /// <summary>店頭の提灯、縦看板、メニューと行列ロープを入口の左右に置きます。</summary>
        private void BuildStorefront(CityGeometry g)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 6.55f;
                g.Box(new Vector3(x, 2.05f, front - .28f), new Vector3(.48f, 2.05f, .22f), red);
                store.Label("麻\n辣\n湯", new Vector3(x, 2.05f, front - .41f), Quaternion.identity, .048f, StoreBuilder.Gold, root);
                g.Box(new Vector3(side * 4.8f, .82f, front - .62f), new Vector3(.78f, 1.2f, .12f), dark);
                g.Line(new Vector3(side * 4.8f, .08f, front - .4f), new Vector3(side * 4.8f, 1.4f, front - .65f), .045f, StoreBuilderMaterialWood());
                store.Label("麻辣湯\n量り売り\n新鮮野菜\n歓迎光臨", new Vector3(side * 4.8f, .85f, front - .7f), Quaternion.identity, .016f, StoreBuilder.Cream, root);
            }
            // 見下ろし視点では前壁と庇を切り欠くため、軒先の提灯も庇と一緒に隠す（残すと客席の上に浮いて見える）。
            Transform eaves = new GameObject("Awning Lanterns").transform;
            eaves.SetParent(store.Root, false);
            eaves.localPosition = new Vector3(0, 2.75f, front - .8f);
            Mesh lanternMesh = MeshFactory.Lantern();
            Material paper = MaterialLibrary.Emissive(new Color(.78f, .13f, .08f), new Color(1f, .35f, .12f), .2f);
            using (CityGeometry lanterns = new CityGeometry())
            {
                for (int i = 0; i < 8; i++)
                {
                    float x = -5.7f + i * 1.62f;
                    lanterns.Line(new Vector3(x, .25f, 0), new Vector3(x, 0, 0), .008f, dark);
                    lanterns.Add(lanternMesh, new Vector3(x, -.22f, 0), Vector3.one * StoreBuilder.Fit(lanternMesh, .43f), Quaternion.identity, paper);
                }
                var flushed = lanterns.Flush(eaves, "Eave Lanterns", false);
                if (flushed.TryGetValue(paper, out Renderer glow)) store.Lanterns.Add(glow);
            }
            store.CutawayWalls.Add((eaves, Vector3.forward, 1));
            for (int i = 0; i < 3; i++)
            {
                float x = 1.7f + i * 1.15f;
                g.Cylinder(new Vector3(x, .43f, front - 1.12f), .035f, .86f, metal);
                g.Cylinder(new Vector3(x, .045f, front - 1.12f), .16f, .08f, dark);
                if (i < 2) g.Line(new Vector3(x, .78f, front - 1.12f), new Vector3(x + 1.15f, .78f, front - 1.12f), .025f, red);
            }
        }

        /// <summary>自転車の車輪、フレーム、サドル、ハンドルを結合対象に追加します。</summary>
        internal static void AddBicycle(CityGeometry g, Vector3 p, Quaternion rotation, Material body, Material metal, Material tire)
        {
            Vector3 a = p + rotation * new Vector3(0, .34f, -.52f);
            Vector3 b = p + rotation * new Vector3(0, .34f, .52f);
            Vector3 crank = p + rotation * new Vector3(0, .34f, -.06f);
            Vector3 seat = p + rotation * new Vector3(0, .85f, -.18f);
            Vector3 handle = p + rotation * new Vector3(0, .9f, .37f);
            g.Wheel(a, rotation, tire); g.Wheel(b, rotation, tire);
            g.Line(a, seat, .025f, body); g.Line(seat, crank, .025f, body);
            g.Line(crank, a, .025f, body); g.Line(seat, handle, .025f, body);
            g.Line(crank, handle, .025f, body); g.Line(handle, b, .025f, metal);
            g.Box(seat + Vector3.up * .025f, new Vector3(.22f, .06f, .24f), tire, rotation);
            g.Line(handle + rotation * new Vector3(-.26f, .08f, 0), handle + rotation * new Vector3(.26f, .08f, 0), .025f, metal);
        }

        /// <summary>駐輪スクーターを少数の車体と車輪で表現します。</summary>
        private void AddScooter(CityGeometry g, Vector3 p)
        {
            g.Wheel(p + new Vector3(0, .34f, -.5f), Quaternion.identity, dark);
            g.Wheel(p + new Vector3(0, .34f, .5f), Quaternion.identity, dark);
            g.Box(p + new Vector3(0, .55f, -.25f), new Vector3(.48f, .6f, .8f), white);
            g.Box(p + new Vector3(0, .9f, -.22f), new Vector3(.45f, .12f, .7f), dark);
            g.Box(p + new Vector3(0, .7f, .4f), new Vector3(.48f, .9f, .18f), white);
            g.Line(p + new Vector3(-.32f, 1.1f, .4f), p + new Vector3(.32f, 1.1f, .4f), .025f, metal);
        }

        /// <summary>木製の共通部品用にキャッシュされたマテリアルを取得します。</summary>
        private static Material StoreBuilderMaterialWood() => MaterialLibrary.Lit(StoreBuilder.Wood, .3f);
    }

    /// <summary>小部品をマテリアル別に蓄積し、Rendererを増やさずにメッシュへ結合します。</summary>
    internal sealed class CityGeometry : IDisposable
    {
        private readonly Dictionary<Material, List<CombineInstance>> groups = new Dictionary<Material, List<CombineInstance>>();
        private readonly Mesh box, cylinder, sphere, wheel;

        /// <summary>共有キャッシュから低頂点数の基本形状を取得します。</summary>
        internal CityGeometry()
        {
            box = StoreBuilder.RoundedBox(Vector3.one, .01f, 1);
            cylinder = MeshFactory.Cylinder(1, 1, 8);
            sphere = MeshFactory.Sphere(6, 8);
            wheel = MeshFactory.Torus(.3f, .035f, 12, 5);
        }

        /// <summary>中心と寸法で箱を追加します。</summary>
        internal void Box(Vector3 p, Vector3 size, Material material, Quaternion? rotation = null) => Add(box, p, size, rotation ?? Quaternion.identity, material);
        /// <summary>縦向きの円柱を追加します。</summary>
        internal void Cylinder(Vector3 p, float radius, float height, Material material) => Add(cylinder, p, new Vector3(radius, height, radius), Quaternion.identity, material);
        /// <summary>楕円形の小物を追加します。</summary>
        internal void Sphere(Vector3 p, Vector3 size, Material material) => Add(sphere, p, size, Quaternion.identity, material);
        /// <summary>自転車の縦向き車輪を追加します。</summary>
        internal void Wheel(Vector3 p, Quaternion rotation, Material material) => Add(wheel, p, Vector3.one, rotation * Quaternion.Euler(0, 0, 90), material);
        /// <summary>車の横向きタイヤを追加します。</summary>
        internal void AddWheelCar(Vector3 p, Material material) => Add(cylinder, p, new Vector3(.34f, .2f, .34f), Quaternion.Euler(0, 0, 90), material);
        /// <summary>二点間の棒・電線を追加します。</summary>
        internal void Line(Vector3 a, Vector3 b, float radius, Material material)
        {
            Vector3 direction = b - a;
            Add(cylinder, (a + b) * .5f, new Vector3(radius, direction.magnitude, radius), Quaternion.FromToRotation(Vector3.up, direction), material);
        }

        /// <summary>任意のメッシュを共有マテリアルの結合グループに登録します。</summary>
        internal void Add(Mesh mesh, Vector3 p, Vector3 size, Quaternion rotation, Material material)
        {
            if (!groups.TryGetValue(material, out List<CombineInstance> parts))
            {
                parts = new List<CombineInstance>(); groups.Add(material, parts);
            }
            parts.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(p, rotation, size) });
        }

        /// <summary>各マテリアルを一個のRendererへまとめ、寿命を生成先に連動させます。</summary>
        internal Dictionary<Material, Renderer> Flush(Transform parent, string name, bool shadows)
        {
            Dictionary<Material, Renderer> result = new Dictionary<Material, Renderer>();
            CityMeshOwner owner = parent.gameObject.GetComponent<CityMeshOwner>();
            if (owner == null) owner = parent.gameObject.AddComponent<CityMeshOwner>();
            foreach (KeyValuePair<Material, List<CombineInstance>> group in groups)
            {
                Mesh combined = new Mesh { name = name + " Combined", indexFormat = IndexFormat.UInt32 };
                combined.CombineMeshes(group.Value.ToArray(), true, true);
                combined.UploadMeshData(true);
                GameObject part = StoreBuilder.Part(name + " Batch", combined, group.Key, Vector3.zero, Vector3.one, parent, null, shadows);
                owner.Meshes.Add(combined);
                result.Add(group.Key, part.GetComponent<Renderer>());
            }
            groups.Clear();
            return result;
        }

        /// <summary>蓄積状態を破棄します。基本形状はMeshFactoryの共有キャッシュが所有します。</summary>
        public void Dispose() => groups.Clear();
    }

    /// <summary>行数変更による街の再生成時に、所有する生成メッシュを解放します。</summary>
    internal sealed class CityMeshOwner : MonoBehaviour
    {
        internal readonly List<Mesh> Meshes = new List<Mesh>();
        /// <summary>共有マテリアルには触れず、この街固有の結合メッシュだけを解放します。</summary>
        private void OnDestroy()
        {
            foreach (Mesh mesh in Meshes) if (mesh != null) Destroy(mesh);
            Meshes.Clear();
        }
    }
}
