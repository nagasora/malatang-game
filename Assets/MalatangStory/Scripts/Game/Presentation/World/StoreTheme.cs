using System.Collections.Generic;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>店舗内装の素材と装飾を所有し、固定数の素材で五つのテーマを切り替えます。</summary>
    public sealed class StoreTheme : MonoBehaviour
    {
        /// <summary>建築と結合装飾で共有する、店舗専用素材の用途です。</summary>
        public enum Surface
        {
            Wall, Wood, Floor, KitchenFloor, KitchenWall, Counter, CounterTop,
            Ceiling, Accent, Fabric, Glow, SecondaryGlow, Paper, Ink, Leaf
        }

        /// <summary>テーマの配色を、生成処理から独立して保持します。</summary>
        private sealed class Palette
        {
            internal readonly Color Wall, Pattern, Wood, LightWood, Floor, Tile, Grout;
            internal readonly Color Accent, Fabric, Ceiling, Light, Secondary, Ink;

            /// <summary>固定のRGB配色から各用途の色を構成します。</summary>
            internal Palette(int wall, int pattern, int wood, int lightWood, int floor, int tile,
                int grout, int accent, int fabric, int ceiling, int light, int secondary, int ink)
            {
                Wall = Rgb(wall); Pattern = Rgb(pattern); Wood = Rgb(wood); LightWood = Rgb(lightWood);
                Floor = Rgb(floor); Tile = Rgb(tile); Grout = Rgb(grout); Accent = Rgb(accent);
                Fabric = Rgb(fabric); Ceiling = Rgb(ceiling); Light = Rgb(light); Secondary = Rgb(secondary); Ink = Rgb(ink);
            }
        }

        private readonly Dictionary<Surface, Material> materials = new Dictionary<Surface, Material>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly List<GameObject> decorations = new List<GameObject>();
        private readonly List<Renderer> themeLanterns = new List<Renderer>();
        private readonly List<Mesh> decorationMeshes = new List<Mesh>();
        private StoreBuilder store;
        private int rows;
        private string currentId;
        /// <summary>現在の正規化済みテーマIDです。</summary>
        public string CurrentId => currentId ?? "shitamachi";

        /// <summary>未指定・未知のIDを既定の下町食堂に正規化します。</summary>
        public static string NormalizeId(string themeId)
        {
            switch (themeId)
            {
                case "lantern": case "cafe": case "teahouse": case "neon": return themeId;
                default: return "shitamachi";
            }
        }

        /// <summary>既存の室内灯二灯と提灯に使用する色を返します。</summary>
        public static Color LightColor(string themeId) => Colors(NormalizeId(themeId)).Light;

        /// <summary>Unityオブジェクトをフィールド初期化せず、生成時に素材を一度だけ確保します。</summary>
        public void Initialize(StoreBuilder builder, int diningRows)
        {
            store = builder;
            rows = diningRows;
            for (int i = 0; i <= (int)Surface.Leaf; i++)
            {
                Surface surface = (Surface)i;
                Material template = surface == Surface.Glow || surface == Surface.SecondaryGlow
                    ? MaterialLibrary.Emissive(Color.white, Color.white) : MaterialLibrary.Lit(Color.white);
                Material material = new Material(template) { name = "Store Theme " + surface, hideFlags = HideFlags.DontSave };
                materials.Add(surface, material);
            }
            UpdateMaterials("shitamachi");
        }

        /// <summary>テーマ変更後も参照が安定した、店舗専用の素材を返します。</summary>
        public Material Get(Surface surface) => materials[surface];

        /// <summary>建築は再生成せず、素材と壁面・天井装飾だけを交換します。同一IDは何もしません。</summary>
        public void Apply(string themeId)
        {
            string normalized = NormalizeId(themeId);
            if (currentId == normalized) return;
            ClearDecorations();
            if (currentId != null) UpdateMaterials(normalized);
            else if (normalized != "shitamachi") UpdateMaterials(normalized);
            currentId = normalized;
            UpdateSignColors(Colors(normalized));
            Transform eaves = store.Root.Find("Awning Lanterns");
            if (eaves != null)
                foreach (Renderer renderer in eaves.GetComponentsInChildren<Renderer>(true))
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"))
                        renderer.sharedMaterial = Get(Surface.Glow);
            BuildDecorations(normalized);
        }

        /// <summary>共有キャッシュへ登録せず、所有する最大五枚の生成テクスチャを差し替えます。</summary>
        private void UpdateMaterials(string id)
        {
            foreach (Material material in materials.Values) material.SetTexture("_BaseMap", null);
            foreach (Texture2D texture in textures) OwnedObjects.Destroy(texture);
            textures.Clear();
            Palette p = Colors(id);
            Texture2D wall = Own(PatternTexture(Surface.Wall, p.Wall, p.Pattern));
            Texture2D wood = Own(UnityEngine.Object.Instantiate(TextureFactory.WoodPlanks(256, p.LightWood, p.Wood, 531)));
            Texture2D tile = Own(PatternTexture(Surface.KitchenFloor, p.Tile, p.Grout));
            Texture2D floor = id == "cafe" ? Own(Concrete(p.Floor))
                : id == "neon" ? Own(PatternTexture(Surface.KitchenFloor, p.Floor, p.Grout))
                : id == "lantern" ? Own(PatternTexture(Surface.KitchenFloor, Rgb(0x6b4134), Rgb(0x2b1d18))) : wood;
            Texture2D fabric = Own(PatternTexture(Surface.Fabric, p.Fabric, p.Fabric * .8f));
            Set(Surface.Wall, Color.white, wall, new Vector2(6, 1.5f));
            Set(Surface.Wood, Color.white, wood, new Vector2(3, 1));
            Set(Surface.Floor, Color.white, floor, new Vector2(5, 4));
            Set(Surface.KitchenFloor, Color.white, tile, new Vector2(8, 3));
            Set(Surface.KitchenWall, Color.white, tile, new Vector2(8, 2));
            Set(Surface.Counter, id == "shitamachi" || id == "neon" ? p.Accent : Color.white,
                id == "shitamachi" || id == "neon" ? null : wood, new Vector2(4, 1));
            Set(Surface.CounterTop, id == "cafe" || id == "lantern" ? p.LightWood : p.Wall);
            Set(Surface.Ceiling, p.Ceiling);
            Set(Surface.Accent, p.Accent);
            Set(Surface.Fabric, Color.white, fabric, new Vector2(1, 1));
            Set(Surface.Glow, id == "lantern" ? p.Fabric : id == "shitamachi" ? p.Accent : p.Light * .5f);
            Set(Surface.SecondaryGlow, p.Secondary * .5f);
            Get(Surface.Glow).SetColor("_EmissionColor", p.Light * 1.8f);
            Get(Surface.SecondaryGlow).SetColor("_EmissionColor", p.Secondary * 1.8f);
            Set(Surface.Paper, id == "neon" ? Rgb(0xe5e4ed) : Rgb(0xf7efda));
            Set(Surface.Ink, p.Ink);
            Set(Surface.Leaf, id == "cafe" ? Rgb(0x448967) : Rgb(0x466141));
        }

        /// <summary>既存の店名文字を保持したまま、看板文字の色だけを変更します。</summary>
        private void UpdateSignColors(Palette p)
        {
            if (store.ShopSign != null)
                foreach (TextMesh label in store.ShopSign.GetComponentsInChildren<TextMesh>(true)) label.color = p.Light;
            Transform sign = store.Root.Find("Kitchen Sign");
            if (sign != null)
                foreach (TextMesh label in sign.GetComponentsInChildren<TextMesh>(true)) label.color = p.Light;
        }

        /// <summary>素材の色・表面特性・繰り返し倍率を更新します。</summary>
        private void Set(Surface surface, Color color, Texture2D texture = null, Vector2? tiling = null)
        {
            Material material = Get(surface);
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            material.SetFloat("_Smoothness", surface == Surface.CounterTop || surface == Surface.KitchenFloor ? .45f : .2f);
        }

        /// <summary>生成テクスチャの解放責任をこの店舗へ限定します。</summary>
        private Texture2D Own(Texture2D texture)
        {
            texture.hideFlags = HideFlags.DontSave;
            textures.Add(texture);
            return texture;
        }

        /// <summary>木目・織り目・回紋・目地を、共有キャッシュに依存しない所有テクスチャとして生成します。</summary>
        private static Texture2D PatternTexture(Surface surface, Color light, Color dark)
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Store " + surface, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size;
                    float v = (y + .5f) / size;
                    float noise = ((x * 73 + y * 151 + x * y * 7) % 101) / 100f;
                    float blend;
                    switch (surface)
                    {
                        case Surface.Wood:
                            float plank = Mathf.Repeat(v * 6, 1);
                            float joint = Mathf.Repeat(u * 3 + Mathf.Floor(v * 6) * .5f, 1);
                            float grain = Mathf.Sin(u * 190 + Mathf.Sin(v * 42) * 2.5f);
                            blend = plank < .025f || joint < .018f ? .05f : .55f + grain * .14f + noise * .1f;
                            break;
                        case Surface.KitchenFloor:
                            float tileU = Mathf.Repeat(u * 6, 1);
                            float tileV = Mathf.Repeat(v * 6, 1);
                            float edge = Mathf.Min(Mathf.Min(tileU, 1 - tileU), Mathf.Min(tileV, 1 - tileV));
                            blend = Mathf.Clamp01((edge - .035f) * 20);
                            break;
                        case Surface.Fabric:
                            blend = .65f + Mathf.Cos(u * 200) * Mathf.Cos(v * 200) * .2f + noise * .1f;
                            break;
                        default:
                            float band = Mathf.Repeat(v * 4, 1);
                            float cellX = Mathf.Repeat(u * 12, 1);
                            bool border = band < .055f || (band < .21f && (cellX < .12f || band > .17f));
                            blend = border ? .15f : .96f + noise * .04f;
                            break;
                    }
                    Color color = Color.Lerp(dark, light, blend);
                    color.a = 1;
                    pixels[y * size + x] = color;
                }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>乱数状態を変えず、コンクリートの微細な濃淡を生成します。</summary>
        private static Texture2D Concrete(Color color)
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Store Concrete", wrapMode = TextureWrapMode.Repeat };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float noise = ((x * 73 + y * 151 + x * y * 7) % 101) / 100f;
                    pixels[y * size + x] = color * (.94f + noise * .12f);
                }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>壁装飾を既存壁の子にしてcutawayの縮小・非表示に追従させます。</summary>
        private void BuildDecorations(string id)
        {
            float front = StoreLayout.FrontZ(rows);
            float back = StoreLayout.BackWallZ;
            Material wood = Get(Surface.Wood), paper = Get(Surface.Paper), ink = Get(Surface.Ink);
            Transform backWall = DecorationRoot(store.Root.Find("Back Wall"));
            using (CityGeometry g = new CityGeometry())
            {
                for (int i = 0; i < 5; i++)
                {
                    float x = 2.8f + i * .55f;
                    g.Box(new Vector3(x, 2.55f, -.19f), new Vector3(.45f, .91f, .04f), wood);
                    g.Line(new Vector3(x - .11f, 2.96f, -.24f), new Vector3(x + .11f, 2.96f, -.24f), .008f, ink);
                    g.Box(new Vector3(x + .1f, 2.24f, -.23f), new Vector3(.09f, .09f, .014f), Get(Surface.Accent));
                    for (int j = 0; j < 3; j++)
                        g.Box(new Vector3(x - .13f + j * .13f, 2.18f, -.23f), new Vector3(.05f, .016f, .012f), ink);
                }
                FlushDecoration(g, backWall, "Menu Frames");
            }
            foreach (string wallName in new[] { "Left Wall", "Right Wall", "Front Wall L", "Front Wall R" })
            {
                Transform wall = store.Root.Find(wallName);
                Transform root = DecorationRoot(wall);
                bool side = wallName == "Left Wall" || wallName == "Right Wall";
                bool left = wallName == "Left Wall";
                float depth = back - front;
                using (CityGeometry g = new CityGeometry())
                {
                    g.Box(new Vector3(0, .1f, 0), side ? new Vector3(.31f, .18f, depth) : new Vector3((StoreLayout.SideWallX * 2 - 2.2f) / 2, .18f, .31f), wood);
                    if (side) BuildWallFeature(g, id, left ? .17f : -.17f, depth, left);
                    FlushDecoration(g, root, "Wall Theme");
                }
            }
            Transform counter = DecorationRoot(store.Root);
            using (CityGeometry g = new CityGeometry())
            {
                BuildCounterDetails(g, id);
                FlushDecoration(g, counter, "Counter Details");
            }
            Transform entry = DecorationRoot(store.Root.Find("Door Frame"));
            using (CityGeometry g = new CityGeometry())
            {
                g.Line(new Vector3(-1.12f, -.23f, -.14f), new Vector3(1.12f, -.23f, -.14f), .025f, wood);
                for (int i = 0; i < 3; i++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = -.72f + i * .72f;
                        g.Box(new Vector3(x, -.9f, -.12f + side * .03f), new Vector3(.62f, .025f, .008f), paper);
                        for (int stitch = 0; stitch < 7; stitch++)
                            g.Box(new Vector3(x - .27f + stitch * .09f, -.28f, -.12f + side * .03f), new Vector3(.035f, .018f, .008f), paper);
                    }
                FlushDecoration(g, entry, "Noren Seams");
            }
            Transform ceiling = DecorationRoot(store.Ceiling);
            using (CityGeometry g = new CityGeometry())
            {
                BuildCeilingFeature(g, id, front);
                Dictionary<Material, Renderer> batches = FlushDecoration(g, ceiling, "Ceiling Theme");
                if (batches.TryGetValue(Get(Surface.Glow), out Renderer lantern))
                {
                    store.Lanterns.Add(lantern);
                    themeLanterns.Add(lantern);
                }
            }
        }

        /// <summary>既存の壁飾りスロットを避け、壁上部へ各テーマの意匠を生成します。</summary>
        private void BuildWallFeature(CityGeometry g, string id, float x, float depth, bool left)
        {
            Material wood = Get(Surface.Wood), ink = Get(Surface.Ink), paper = Get(Surface.Paper);
            float inner = left ? .06f : -.06f;
            float z = -.15f;
            if (id == "teahouse")
            {
                g.Box(new Vector3(x, 2.55f, z), new Vector3(.045f, .65f, 2.8f), paper);
                for (int i = 0; i < 9; i++)
                    g.Box(new Vector3(x + inner, 2.55f, z - 1.4f + i * .35f), new Vector3(.035f, .72f, .035f), wood);
                for (int i = 0; i < 3; i++)
                    g.Box(new Vector3(x + inner, 2.25f + i * .3f, z), new Vector3(.035f, .035f, 2.9f), wood);
                for (int i = 0; i < 8; i++)
                {
                    float offset = Mathf.Lerp(-depth * .45f, depth * .45f, i / 7f);
                    g.Box(new Vector3(x + inner, 2.87f, offset), new Vector3(.04f, .12f, .45f), wood, Quaternion.Euler(45, 0, 0));
                }
                float scrollZ = depth * .32f;
                g.Box(new Vector3(x, 2.47f, scrollZ), new Vector3(.025f, .65f, .4f), paper);
                for (int i = -1; i <= 1; i += 2)
                    g.Line(new Vector3(x + inner, 2.47f + i * .34f, scrollZ - .25f), new Vector3(x + inner, 2.47f + i * .34f, scrollZ + .25f), .025f, wood);
                for (int i = 0; i < 4; i++)
                    g.Line(new Vector3(x + inner, 2.7f - i * .12f, scrollZ - .09f), new Vector3(x + inner, 2.66f - i * .12f, scrollZ + .09f), .014f, ink);
            }
            else if (id == "cafe")
            {
                g.Box(new Vector3(x, 2.38f, z), new Vector3(.18f, .045f, 2.7f), wood);
                for (int i = 0; i < 5; i++)
                {
                    Vector3 pot = new Vector3(x + inner, 2.49f, z - 1.04f + i * .52f);
                    g.Cylinder(pot, .09f, .18f, paper);
                    for (int leaf = 0; leaf < 3; leaf++)
                        g.Sphere(pot + new Vector3(inner * leaf, .18f + leaf * .04f, (leaf - 1) * .06f), new Vector3(.12f, .26f, .18f), Get(Surface.Leaf));
                }
            }
            else if (id == "neon")
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = 2.35f + i * .18f;
                    Material glow = i % 2 == 0 ? Get(Surface.Glow) : Get(Surface.SecondaryGlow);
                    g.Line(new Vector3(x, y, -depth * .35f), new Vector3(x, y, depth * .35f), .018f, glow);
                    g.Line(new Vector3(x, y, depth * .35f), new Vector3(x, y + .1f, depth * .35f + .18f), .018f, glow);
                }
            }
            else
            {
                for (int i = 0; i < 7; i++)
                {
                    float offset = -1.5f + i * .5f;
                    g.Box(new Vector3(x, 2.55f, offset), new Vector3(.04f, .56f, .37f), wood);
                    g.Box(new Vector3(x + inner, 2.55f, offset), new Vector3(.012f, .49f, .3f), paper);
                    for (int mark = 0; mark < 3; mark++)
                        g.Box(new Vector3(x + inner * 1.2f, 2.69f - mark * .11f, offset), new Vector3(.012f, .025f, .13f + mark * .02f), ink);
                }
            }
        }

        /// <summary>操作対象と干渉しないカウンター端に調味料、湯飲みと木の細工を追加します。</summary>
        private void BuildCounterDetails(CityGeometry g, string id)
        {
            Material wood = Get(Surface.Wood), paper = Get(Surface.Paper), ink = Get(Surface.Ink);
            for (int i = 0; i < 3; i++)
            {
                float x = 2.45f + i * .23f;
                g.Cylinder(new Vector3(x, 1.1f, 2.08f), .055f, .18f, paper);
                g.Cylinder(new Vector3(x, 1.2f, 2.08f), .06f, .025f, ink);
                g.Box(new Vector3(x, 1.1f, 2.015f), new Vector3(.05f, .07f, .008f), Get(Surface.Accent));
            }
            g.Box(new Vector3(2.65f, 1.018f, 2.08f), new Vector3(.85f, .025f, .34f), wood);
            for (int i = 0; i < 3; i++)
            {
                g.Cylinder(new Vector3(-3.3f, 1.04f + i * .035f, 1.82f), .085f, .045f, paper);
                g.Cylinder(new Vector3(-3.3f, 1.066f + i * .035f, 1.82f), .06f, .006f, ink);
            }
            if (id == "neon")
            {
                g.Line(new Vector3(-3.65f, .94f, 1.41f), new Vector3(3.25f, .94f, 1.41f), .014f, Get(Surface.SecondaryGlow));
                g.Line(new Vector3(-3.65f, .84f, 1.46f), new Vector3(3.25f, .84f, 1.46f), .012f, Get(Surface.Glow));
            }
            else if (id == "lantern" || id == "teahouse")
            {
                for (int i = 0; i < 25; i++)
                    g.Box(new Vector3(-3.45f + i * .27f, .47f, 1.46f), new Vector3(.025f, .78f, .025f), wood);
                for (int i = -1; i <= 1; i += 2)
                    g.Box(new Vector3(-.2f, .47f + i * .37f, 1.45f), new Vector3(6.65f, .04f, .03f), wood);
            }
        }

        /// <summary>点光源を追加せず、発光メッシュの提灯列・ペンダント・欄間を天井配下に作ります。</summary>
        private void BuildCeilingFeature(CityGeometry g, string id, float front)
        {
            Material wood = Get(Surface.Wood), glow = Get(Surface.Glow), ink = Get(Surface.Ink);
            if (id == "lantern" || id == "shitamachi")
            {
                int rowCount = id == "lantern" ? 2 : 1;
                for (int row = 0; row < rowCount; row++)
                {
                    float z = row == 0 ? .2f : (front + .8f) * .5f;
                    g.Line(new Vector3(-5.6f, 3.64f, z), new Vector3(5.6f, 3.64f, z), .012f, ink);
                    for (int i = 0; i < 7; i++)
                    {
                        float x = -5.1f + i * 1.7f;
                        g.Line(new Vector3(x, 3.64f, z), new Vector3(x, 3.22f, z), .008f, ink);
                        g.Sphere(new Vector3(x, 2.99f, z), new Vector3(.45f, .55f, .45f), glow);
                        for (int band = -1; band <= 1; band++)
                            g.Cylinder(new Vector3(x, 2.99f + band * .18f, z), band == 0 ? .23f : .18f, .018f, wood);
                        g.Cylinder(new Vector3(x, 2.69f, z), .025f, .13f, Get(Surface.Fabric));
                    }
                }
            }
            else if (id == "cafe")
            {
                for (int i = 0; i < 5; i++)
                {
                    Vector3 p = new Vector3(-4.6f + i * 2.3f, 2.91f, (front + 1) * .5f);
                    g.Line(p + Vector3.up * .9f, p, .008f, ink);
                    g.Sphere(p, new Vector3(.34f, .28f, .34f), Get(Surface.Fabric));
                    g.Cylinder(p - Vector3.up * .11f, .14f, .035f, glow);
                }
            }
            else if (id == "teahouse")
            {
                for (int i = 0; i < 20; i++)
                {
                    float x = -6.3f + i * .66f;
                    g.Box(new Vector3(x, 3.32f, .5f), new Vector3(.06f, .48f, .06f), wood, Quaternion.Euler(0, 0, 45));
                    g.Box(new Vector3(x, 3.32f, .5f), new Vector3(.06f, .48f, .06f), wood, Quaternion.Euler(0, 0, -45));
                }
                for (int i = -1; i <= 1; i += 2)
                    g.Box(new Vector3(0, 3.32f + i * .22f, .5f), new Vector3(13.4f, .07f, .08f), wood);
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    float z = Mathf.Lerp(front + .7f, .3f, i / 3f);
                    g.Line(new Vector3(-5.8f, 3.5f, z), new Vector3(5.8f, 3.5f, z), .02f,
                        i % 2 == 0 ? glow : Get(Surface.SecondaryGlow));
                }
            }
        }

        /// <summary>交換対象を親のローカル原点に作成し、破棄対象として記録します。</summary>
        private Transform DecorationRoot(Transform parent)
        {
            GameObject root = new GameObject("Theme Decoration");
            root.transform.SetParent(parent, false);
            decorations.Add(root);
            return root.transform;
        }

        /// <summary>非アクティブな子のOnDestroyに依存せず、装飾の結合メッシュを店舗自身で所有します。</summary>
        private Dictionary<Material, Renderer> FlushDecoration(CityGeometry geometry, Transform parent, string name)
        {
            Dictionary<Material, Renderer> batches = geometry.Flush(parent, name, false);
            CityMeshOwner owner = parent.GetComponent<CityMeshOwner>();
            decorationMeshes.AddRange(owner.Meshes);
            owner.Meshes.Clear();
            return batches;
        }

        /// <summary>一度も表示しなかった装飾も含め、店舗が所有する結合メッシュだけを解放します。</summary>
        private void ReleaseDecorationMeshes()
        {
            foreach (Mesh mesh in decorationMeshes) OwnedObjects.Destroy(mesh);
            decorationMeshes.Clear();
        }

        /// <summary>遅延破棄のフレームも旧装飾を描画せず、共有メッシュには触れません。</summary>
        private void ClearDecorations()
        {
            foreach (Renderer lantern in themeLanterns) store.Lanterns.Remove(lantern);
            themeLanterns.Clear();
            foreach (GameObject decoration in decorations)
            {
                if (decoration == null) continue;
                decoration.SetActive(false);
                OwnedObjects.Destroy(decoration);
            }
            decorations.Clear();
            ReleaseDecorationMeshes();
        }

        /// <summary>店舗破棄時に専用の結合メッシュ・素材・テクスチャを解放します。</summary>
        private void OnDestroy()
        {
            ReleaseDecorationMeshes();
            foreach (Material material in materials.Values) OwnedObjects.Destroy(material);
            materials.Clear();
            foreach (Texture2D texture in textures) OwnedObjects.Destroy(texture);
            textures.Clear();
        }

        /// <summary>整数のRGBから不透明色へ変換します。</summary>
        private static Color Rgb(int value) => new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);

        /// <summary>固定IDに対応する五種類の配色を返します。</summary>
        private static Palette Colors(string id)
        {
            switch (id)
            {
                case "lantern": return new Palette(0x302421, 0x47302a, 0x39251b, 0x855b38, 0xb2a08b, 0x847c71, 0x403b36, 0xa82119, 0xc23121, 0x302522, 0xffa34f, 0xffd488, 0x241a16);
                case "cafe": return new Palette(0xe7f1e9, 0xc8e1d4, 0xae8c60, 0xe7d4ad, 0xa5aaa5, 0xe5ece8, 0xa8b5ad, 0x74b5a0, 0x83bdab, 0xf0eee6, 0xffefd3, 0xa9e8d6, 0x355c52);
                case "teahouse": return new Palette(0xc2b18d, 0xaa9772, 0x2f2119, 0x62432b, 0xc0ac88, 0x8a8570, 0x4d4839, 0x5b3026, 0x62553a, 0x51412f, 0xecc48a, 0xd4ba7e, 0x342c20);
                case "neon": return new Palette(0x222536, 0x303447, 0x252631, 0x494755, 0x414355, 0x747a88, 0x282d3d, 0x423348, 0xb93280, 0x242532, 0xff46a4, 0x30e1ed, 0x34374c);
                default: return new Palette(0xf7e8cc, 0xdbb19b, 0x79472b, 0xc28e59, 0xd5bea1, 0xece4d5, 0xaaa08f, 0xa72b22, 0x34436c, 0xf1e4c9, 0xffc078, 0xf5d699, 0x50362b);
            }
        }
    }
}
