/// <summary>店舗の固定配置と手続き生成を提供します。</summary>
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MalatangStory.Game
{
    /// <summary>店舗の固定座標（厨房・カウンター・行列など）。</summary>
    public static class StoreLayout
    {
        public const float CounterZ = 1.9f, CounterHeight = 1.0f, BackCounterZ = 4.7f, BackCounterHeight = .95f, BackWallZ = 5.45f, SideWallX = 6.9f;
        public static readonly Vector3 RegisterTop = new Vector3(-2.3f, CounterHeight, CounterZ);
        public static readonly Vector3 RegisterCustomer = new Vector3(-2.3f, 0, .95f);
        public static readonly Vector3 RegisterStaff = new Vector3(-2.3f, 0, 2.75f);
        public static readonly Vector3 PassTop = new Vector3(1.4f, CounterHeight, CounterZ);
        public static readonly Vector3 PassStaff = new Vector3(1.4f, 0, 2.75f);
        public static readonly Vector3 KitchenSpot = new Vector3(-.6f, 0, 3.75f);
        public static readonly Vector3 CookStaff = new Vector3(1.6f, 0, 3.75f);
        public static readonly Vector3 FridgeFront = new Vector3(-5.1f, 0, .95f);
        public static readonly Vector3 PrepBowl = new Vector3(-3.25f, BackCounterHeight, 4.55f);
        public static readonly Vector3 ScaleTop = new Vector3(-2.35f, BackCounterHeight, 4.55f);
        public static readonly Vector3 FinishBowl = new Vector3(3.15f, BackCounterHeight, 4.45f);
        public static readonly float[] PotX = { -1.35f, -.45f, .45f, 1.35f };

        /// <summary>前壁（入口側）の z。客席の行数で変わる。</summary>
        public static float FrontZ(int rows) => -1f - rows - .2f;
        public static Vector3 Door(int rows) => new Vector3(0, 0, FrontZ(rows) - 1.6f);
        public static Vector3 PotTop(int index) => new Vector3(PotX[Mathf.Clamp(index, 0, 3)], BackCounterHeight + .02f, BackCounterZ);
        public static Vector3 KettleTop(int index) => new Vector3(2.15f + index * .42f, BackCounterHeight + .02f, 4.95f);
        public static Vector3 JarTop(int index) => new Vector3(4.75f + (index % 4) * .38f, BackCounterHeight + (index / 4) * .42f + .02f, 4.85f);
        public static Vector3 QueueSpot(int index)
            => index < 4 ? new Vector3(-3.1f - index * .75f, 0, .95f) : new Vector3(-5.9f, 0, .3f - (index - 4) * .75f);
        public static Vector3 WaitingSpot(int index) => new Vector3(.2f + (index % 5) * .7f, 0, .45f - (index / 5) * .55f);

        /// <summary>壁飾りスロットの位置と向き（壁の法線方向）。</summary>
        public static (Vector3 position, Quaternion rotation) WallSlot(int slot, int rows)
        {
            float front = FrontZ(rows);
            switch (slot)
            {
                case 0: return (new Vector3(-SideWallX + .14f, 1.7f, .4f), Quaternion.Euler(0, 90, 0));
                case 1: return (new Vector3(-SideWallX + .14f, 1.7f, -1.8f), Quaternion.Euler(0, 90, 0));
                case 2: return (new Vector3(-SideWallX + .14f, 1.7f, -3.8f), Quaternion.Euler(0, 90, 0));
                case 3: return (new Vector3(SideWallX - .14f, 1.7f, .4f), Quaternion.Euler(0, -90, 0));
                case 4: return (new Vector3(SideWallX - .14f, 1.7f, -1.8f), Quaternion.Euler(0, -90, 0));
                case 5: return (new Vector3(SideWallX - .14f, 1.7f, -3.8f), Quaternion.Euler(0, -90, 0));
                case 6: return (new Vector3(-4f, 1.7f, front + .14f), Quaternion.Euler(0, 0, 0));
                default: return (new Vector3(4f, 1.7f, front + .14f), Quaternion.Euler(0, 0, 0));
            }
        }
    }

    /// <summary>
    /// 店舗の建物（床・壁・厨房・カウンター・看板・外の通り）を手続き生成する。
    /// ナビメッシュ用の障害物も同時に登録する。
    /// </summary>
    public sealed class StoreBuilder
    {
        public readonly List<NavMeshBuildSource> Sources = new List<NavMeshBuildSource>();
        public readonly List<(Transform wall, Vector3 normal, float height)> CutawayWalls = new List<(Transform, Vector3, float)>();
        public readonly List<Light> NightLights = new List<Light>();
        public readonly List<Renderer> Lanterns = new List<Renderer>();
        public readonly List<Transform> PotRoots = new List<Transform>();
        public readonly List<Transform> Kettles = new List<Transform>();
        public readonly List<Transform> Jars = new List<Transform>();
        public Transform Root, ScaleRoot, Ladle, ShopSign;
        /// <summary>ネオン、窓、街灯それぞれの夜間発光色です。</summary>
        public List<(Renderer renderer, Color color)> NightEmitters { get; } = new List<(Renderer, Color)>();
        /// <summary>雨天・夜間状態を配線する街の動きです。</summary>
        public StreetLife Street { get; private set; }
        /// <summary>一人称視点だけで表示する、既定では非表示の天井です。</summary>
        public Transform Ceiling { get; private set; }
        /// <summary>レジ横の客向け量りです。厨房のScaleRootとは独立しています。</summary>
        public Transform RegisterScale { get; private set; }
        /// <summary>客向け量りのデジタル表示です。</summary>
        public TextMesh RegisterScaleDisplay { get; private set; }
        /// <summary>レジ上の金額表示です。</summary>
        public TextMesh RegisterDisplay { get; private set; }
        /// <summary>レジ横のキャッシュトレーです。</summary>
        public Transform CashTray { get; private set; }
        private TextMesh storeNameLabel;
        public void SetStoreName(string value) { if (storeNameLabel != null && storeNameLabel.text != value) storeNameLabel.text = value; }
        private CityBuilder city;
        private Transform seasonalEntrance;
        private Transform seasonalGroundEntrance;
        private string entranceSeason;
        private bool entranceSnow;
        private readonly StoreTheme theme;
        private readonly Font font;
        private readonly int rows;

        public static readonly Color Wood = new Color(.47f, .27f, .16f), WoodLight = new Color(.66f, .45f, .28f), Cream = new Color(.96f, .9f, .78f),
            Lacquer = new Color(.66f, .13f, .1f), Gold = new Color(.9f, .68f, .3f), Steel = new Color(.72f, .74f, .76f), Charcoal = new Color(.2f, .2f, .22f);

        /// <summary>店舗の親と寸法を受け取り、内装素材の所有者を生成します。</summary>
        public StoreBuilder(Transform parent, Font font, int rows)
        {
            this.font = font; this.rows = rows;
            Root = new GameObject("Store").transform;
            Root.SetParent(parent, false);
            theme = Root.gameObject.AddComponent<StoreTheme>();
            theme.Initialize(this, rows);
        }

        /// <summary>建築やナビメッシュを再生成せず、既定IDへ正規化した内装を反映します。</summary>
        public void ApplyTheme(string themeId) => theme.Apply(themeId);

        /// <summary>メッシュを置く。scale はローカルスケール。</summary>
        public static GameObject Part(string name, Mesh mesh, Material material, Vector3 position, Vector3 scale, Transform parent, Quaternion? rotation = null, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale; go.transform.localRotation = rotation ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>角丸の箱メッシュ。角丸半径は最短辺の半分を超えないよう自動で抑える。</summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int segments = 3)
            => MeshFactory.RoundedBox(size, Mathf.Min(radius, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .49f), segments);

        /// <summary>メッシュの最大寸法が size になるよう拡大率を求める。</summary>
        public static float Fit(Mesh mesh, float size) => size / Mathf.Max(.0001f, Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z)));

        /// <summary>角丸の箱を中心座標・寸法で置く。</summary>
        public GameObject Box(string name, Vector3 center, Vector3 size, Material material, Transform parent = null, float radius = .04f, bool obstacle = false)
        {
            var go = Part(name, RoundedBox(size, radius), material, center, Vector3.one, parent ?? Root);
            if (obstacle) AddObstacle(center, size);
            return go;
        }

        public void AddObstacle(Vector3 center, Vector3 size, Quaternion? rotation = null)
            => Sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = Matrix4x4.TRS(center, rotation ?? Quaternion.identity, Vector3.one), area = 1 });

        public void AddWalkable(Vector3 center, Vector3 size)
            => Sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one), area = 0 });

        public TextMesh Label(string text, Vector3 position, Quaternion rotation, float size, Color color, Transform parent = null)
        {
            var go = new GameObject("Text " + text);
            go.transform.SetParent(parent ?? Root, false);
            go.transform.localPosition = position; go.transform.localRotation = rotation;
            var mesh = go.AddComponent<TextMesh>();
            mesh.richText = false; mesh.text = text; mesh.font = font; mesh.fontSize = 128; mesh.characterSize = size * .5f; mesh.anchor = TextAnchor.MiddleCenter; mesh.color = color;
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return mesh;
        }

        /// <summary>建物全体を作る。</summary>
        public void Build(string storeName)
        {
            float front = StoreLayout.FrontZ(rows);
            float back = StoreLayout.BackWallZ, side = StoreLayout.SideWallX;
            BuildFloors(front, back, side);
            BuildWalls(front, back, side);
            BuildFrontCounter();
            BuildCustomerFridge();
            BuildKitchen();
            BuildDecor(front, back, side);
            BuildRegisterProps();
            BuildInteriorProps();
            BuildCeiling(front, back, side);
            BuildStreet(front, side, storeName);
            ApplyTheme("shitamachi");
        }

        private void BuildFloors(float front, float back, float side)
        {
            float diningDepth = 1.5f - front;
            var dining = Part("Dining Floor", StoreBuilder.RoundedBox(new Vector3(side * 2, .2f, diningDepth), .02f),
                theme.Get(StoreTheme.Surface.Floor), new Vector3(0, -.1f, (1.5f + front) / 2), Vector3.one, Root, null, false);
            dining.AddComponent<BoxCollider>();
            var kitchen = Part("Kitchen Floor", StoreBuilder.RoundedBox(new Vector3(side * 2, .2f, back - 1.5f), .02f),
                theme.Get(StoreTheme.Surface.KitchenFloor), new Vector3(0, -.1f, (back + 1.5f) / 2), Vector3.one, Root, null, false);
            kitchen.AddComponent<BoxCollider>();
            AddWalkable(new Vector3(0, -.1f, (back + front) / 2), new Vector3(side * 2, .2f, back - front));
            AddWalkable(new Vector3(0, -.1f, front - 1.9f), new Vector3(4f, .2f, 4f));
        }

        private void BuildWalls(float front, float back, float side)
        {
            Material paper = theme.Get(StoreTheme.Surface.Wall);
            Material wainscot = theme.Get(StoreTheme.Surface.Wood);
            Material tiles = theme.Get(StoreTheme.Surface.KitchenWall);
            Wall("Back Wall", new Vector3(0, 1.7f, back), new Vector3(side * 2 + .3f, 3.4f, .25f), tiles, wainscot, Vector3.back, false);
            float depth = back - front;
            Wall("Left Wall", new Vector3(-side, 1.5f, (back + front) / 2), new Vector3(.25f, 3f, depth), paper, wainscot, Vector3.right, true);
            Wall("Right Wall", new Vector3(side, 1.5f, (back + front) / 2), new Vector3(.25f, 3f, depth), paper, wainscot, Vector3.left, true);
            float segment = (side * 2 - 2.2f) / 2;
            Wall("Front Wall L", new Vector3(-side + segment / 2, 1.5f, front), new Vector3(segment, 3f, .25f), paper, wainscot, Vector3.forward, true);
            Wall("Front Wall R", new Vector3(side - segment / 2, 1.5f, front), new Vector3(segment, 3f, .25f), paper, wainscot, Vector3.forward, true);
            var frame = Box("Door Frame", new Vector3(0, 2.75f, front), new Vector3(2.4f, .5f, .3f), theme.Get(StoreTheme.Surface.Accent));
            CutawayWalls.Add((frame.transform, Vector3.forward, 1));
            var noren = new GameObject("Noren").transform; noren.SetParent(frame.transform, false);
            for (int i = 0; i < 3; i++)
            {
                var cloth = Part("Noren Cloth", StoreBuilder.RoundedBox(new Vector3(.68f, .7f, .03f), .01f), theme.Get(StoreTheme.Surface.Fabric),
                    new Vector3(-.72f + i * .72f, -.6f, -.12f), Vector3.one, noren);
                cloth.transform.localRotation = Quaternion.Euler(4, 0, 0);
            }
            Label("麻辣湯", new Vector3(0, -.55f, -.16f), Quaternion.Euler(0, 180, 0), .045f, Color.white, noren);
            Label("麻辣湯", new Vector3(0, -.55f, .16f), Quaternion.identity, .045f, Color.white, noren);
        }

        private void Wall(string name, Vector3 center, Vector3 size, Material face, Material trim, Vector3 normal, bool cutaway)
        {
            var root = new GameObject(name).transform; root.SetParent(Root, false); root.localPosition = new Vector3(center.x, 0, center.z);
            Part("Paper", StoreBuilder.RoundedBox(size, .02f), face, new Vector3(0, center.y, 0), Vector3.one, root);
            var trimSize = new Vector3(size.x + (Mathf.Abs(normal.x) > .5f ? .06f : 0), .95f, size.z + (Mathf.Abs(normal.z) > .5f ? .06f : 0));
            Part("Wainscot", StoreBuilder.RoundedBox(trimSize, .02f), trim, new Vector3(0, .475f, 0), Vector3.one, root);
            Part("Rail", StoreBuilder.RoundedBox(new Vector3(trimSize.x + .02f, .07f, trimSize.z + .02f), .02f), theme.Get(StoreTheme.Surface.Accent), new Vector3(0, .97f, 0), Vector3.one, root);
            AddObstacle(new Vector3(center.x, 1.5f, center.z), size);
            if (cutaway) CutawayWalls.Add((root, normal, size.y));
        }

        private void BuildFrontCounter()
        {
            Material body = theme.Get(StoreTheme.Surface.Counter);
            Material top = theme.Get(StoreTheme.Surface.CounterTop);
            Box("Front Counter", new Vector3(-.2f, .47f, StoreLayout.CounterZ), new Vector3(6.8f, .94f, .8f), body, null, .05f, true);
            Box("Counter Top", new Vector3(-.2f, .97f, StoreLayout.CounterZ), new Vector3(7f, .07f, .95f), top, null, .03f);
            for (int i = 0; i < 9; i++)
                Box("Counter Panel", new Vector3(-3.35f + i * .78f, .47f, StoreLayout.CounterZ - .41f), new Vector3(.62f, .6f, .03f), theme.Get(StoreTheme.Surface.Counter), null, .02f);
            var register = new GameObject("Register").transform; register.SetParent(Root, false); register.localPosition = StoreLayout.RegisterTop;
            Part("Register Body", StoreBuilder.RoundedBox(new Vector3(.5f, .26f, .42f), .05f), MaterialLibrary.Lit(new Color(.18f, .22f, .24f), .5f, .2f), new Vector3(0, .13f, 0), Vector3.one, register);
            Part("Register Screen", StoreBuilder.RoundedBox(new Vector3(.36f, .22f, .04f), .02f), MaterialLibrary.Emissive(new Color(.1f, .2f, .15f), new Color(.3f, 1f, .6f), .6f), new Vector3(0, .38f, .06f), Vector3.one, register, Quaternion.Euler(-15, 0, 0));
            Part("Bell", MeshFactory.Lathe(new[] { new Vector2(0, 0), new Vector2(.07f, 0), new Vector2(.06f, .03f), new Vector2(.03f, .07f), new Vector2(0, .08f) }, 16), MaterialLibrary.Lit(Gold, .8f, .8f), new Vector3(-.35f, 0, .1f), Vector3.one, register);
            var pass = new GameObject("Pass").transform; pass.SetParent(Root, false); pass.localPosition = StoreLayout.PassTop;
            Part("Pass Shelf", StoreBuilder.RoundedBox(new Vector3(1.6f, .05f, .5f), .02f), MaterialLibrary.Lit(Steel, .8f, .8f), new Vector3(0, .32f, 0), Vector3.one, pass);
            for (int i = -1; i <= 1; i += 2) Part("Pass Leg", MeshFactory.Cylinder(.025f, .32f, 8), MaterialLibrary.Lit(Steel, .8f, .8f), new Vector3(i * .75f, .16f, 0), Vector3.one, pass);
            var heat = new GameObject("Heat Lamp"); heat.transform.SetParent(pass, false); heat.transform.localPosition = new Vector3(0, .9f, 0);
            Part("Lamp", StoreBuilder.RoundedBox(new Vector3(1.5f, .08f, .3f), .03f), MaterialLibrary.Emissive(new Color(.3f, .25f, .2f), new Color(1f, .55f, .2f), .8f), Vector3.zero, Vector3.one, heat.transform);
            var menu = new GameObject("Menu Board").transform; menu.SetParent(Root, false); menu.localPosition = new Vector3(-.4f, 2.55f, StoreLayout.CounterZ + .2f);
            Part("Board", StoreBuilder.RoundedBox(new Vector3(3.4f, .7f, .06f), .04f), MaterialLibrary.Lit(new Color(.16f, .12f, .1f), .3f), Vector3.zero, Vector3.one, menu);
            Label("麻辣湯　100gあたり量り売り", new Vector3(0, .12f, -.04f), Quaternion.identity, .03f, new Color(1, .9f, .6f), menu);
            Label("好きな具材を選んでね！", new Vector3(0, -.16f, -.04f), Quaternion.identity, .024f, Color.white, menu);
            for (int i = -1; i <= 1; i += 2) Part("Chain", MeshFactory.Cylinder(.01f, .5f, 6), MaterialLibrary.Lit(Steel, .7f, .6f), new Vector3(i * 1.5f, .58f, 0), Vector3.one, menu);
        }

        private void BuildCustomerFridge()
        {
            var steel = MaterialLibrary.Lit(Steel, .8f, .7f);
            var fridge = new GameObject("Self-serve Fridge").transform; fridge.SetParent(Root, false); fridge.localPosition = new Vector3(-5.2f, 0, StoreLayout.CounterZ);
            Part("Body", StoreBuilder.RoundedBox(new Vector3(2.9f, .85f, .85f), .05f), steel, new Vector3(0, .425f, 0), Vector3.one, fridge);
            Part("Glass", StoreBuilder.RoundedBox(new Vector3(2.8f, .55f, .7f), .03f), MaterialLibrary.Transparent(new Color(.75f, .9f, 1f, .22f), .95f), new Vector3(0, 1.15f, 0), Vector3.one, fridge, null, false);
            Part("Top", StoreBuilder.RoundedBox(new Vector3(2.9f, .06f, .85f), .02f), steel, new Vector3(0, 1.45f, 0), Vector3.one, fridge);
            Part("Light", StoreBuilder.RoundedBox(new Vector3(2.7f, .03f, .1f), .01f), MaterialLibrary.Emissive(Color.white, new Color(.85f, .95f, 1f), 1.2f), new Vector3(0, 1.41f, 0), Vector3.one, fridge, null, false);
            var colors = new[] { new Color(.35f, .65f, .25f), new Color(.92f, .85f, .62f), new Color(.85f, .45f, .35f), new Color(.6f, .45f, .3f), new Color(.95f, .9f, .75f), new Color(.98f, .6f, .4f) };
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 6; col++)
                {
                    var tray = Part("Tray", StoreBuilder.RoundedBox(new Vector3(.42f, .05f, .3f), .02f), MaterialLibrary.Lit(new Color(.9f, .9f, .88f), .6f), new Vector3(-1.15f + col * .46f, .88f + row * .02f, -.15f + row * .3f), Vector3.one, fridge);
                    for (int k = 0; k < 4; k++)
                        Part("Item", MeshFactory.Sphere(), MaterialLibrary.Lit(colors[(col + row * 2) % colors.Length], .4f),
                            new Vector3(-.12f + k * .08f, .05f, (k % 2) * .06f - .03f), new Vector3(.09f, .05f, .08f), tray.transform);
                }
            AddObstacle(new Vector3(-5.2f, .7f, StoreLayout.CounterZ), new Vector3(2.9f, 1.4f, .85f));
            Label("セルフ具材コーナー", new Vector3(0, 1.62f, -.35f), Quaternion.identity, .022f, StoreBuilder.Lacquer, fridge);
        }

        private void BuildKitchen()
        {
            var steel = MaterialLibrary.Lit(Steel, .85f, .8f);
            var darkSteel = MaterialLibrary.Lit(new Color(.4f, .42f, .44f), .7f, .7f);
            Box("Back Counter", new Vector3(0, StoreLayout.BackCounterHeight / 2, StoreLayout.BackCounterZ), new Vector3(13.4f, StoreLayout.BackCounterHeight, .95f), steel, null, .04f, true);
            Box("Side Counter", new Vector3(5.6f, .47f, StoreLayout.CounterZ), new Vector3(2.4f, .94f, .8f), steel, null, .04f, true);
            Box("Dish Rack", new Vector3(5.6f, 1.25f, StoreLayout.CounterZ), new Vector3(2f, .5f, .5f), darkSteel, null, .03f);
            for (int i = 0; i < 6; i++)
                Part("Stacked Bowl", MeshFactory.Bowl(), MaterialLibrary.Lit(new Color(.97f, .95f, .9f), .7f), new Vector3(4.9f + i * .28f, 1.0f, StoreLayout.CounterZ), Vector3.one * .26f, Root);
            Box("Stove", new Vector3(0, StoreLayout.BackCounterHeight - .02f, StoreLayout.BackCounterZ), new Vector3(3.8f, .06f, .9f), MaterialLibrary.Lit(Charcoal, .4f, .5f));
            Box("Hood", new Vector3(0, 2.75f, StoreLayout.BackCounterZ + .1f), new Vector3(4.2f, .6f, 1.1f), steel, null, .06f);
            for (int i = 0; i < 4; i++)
            {
                var root = new GameObject("Pot " + i).transform; root.SetParent(Root, false); root.localPosition = StoreLayout.PotTop(i);
                Part("Burner", MeshFactory.Torus(.22f, .03f), MaterialLibrary.Lit(new Color(.15f, .15f, .15f), .3f, .6f), new Vector3(0, .01f, 0), Vector3.one, root);
                var potMesh = MeshFactory.Pot();
                Part("Pot", potMesh, MaterialLibrary.Lit(new Color(.78f, .8f, .82f), .85f, .85f), new Vector3(0, .02f, 0), Vector3.one * Fit(potMesh, .72f), root);
                Part("Water", MeshFactory.Disc(.27f, 24), MaterialLibrary.Transparent(new Color(.86f, .8f, .62f, .38f), .95f), new Vector3(0, .34f, 0), Vector3.one, root, null, false);
                Part("Flame", MeshFactory.Torus(.18f, .025f), MaterialLibrary.Emissive(new Color(.2f, .3f, 1f), new Color(.3f, .5f, 1f), 2f), new Vector3(0, .015f, 0), Vector3.one, root, null, false);
                var collider = root.gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .25f, 0); collider.size = new Vector3(.75f, .5f, .75f);
                root.gameObject.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Pot, i);
                PotRoots.Add(root);
            }
            for (int i = 0; i < Content.Soups.Length; i++)
            {
                var soup = Content.Soups[i];
                var root = new GameObject("Kettle " + soup.Id).transform; root.SetParent(Root, false); root.localPosition = StoreLayout.KettleTop(i);
                var potMesh = MeshFactory.Pot();
                Part("Kettle", potMesh, MaterialLibrary.Lit(new Color(.82f, .5f, .3f), .7f, .7f), Vector3.zero, Vector3.one * Fit(potMesh, .42f), root);
                SoupVisuals.CreateSurface(root, soup.Id, 0, .16f, .2f);
                var tag = Label(soup.Id == "dry" ? "汁なし" : soup.Name.Length > 4 ? soup.Name.Substring(0, 2) : soup.Name, new Vector3(0, .1f, -.22f), Quaternion.Euler(10, 0, 0), .0042f, Color.white, root);
                tag.gameObject.name = "Tag";
                var collider = root.gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .15f, 0); collider.size = new Vector3(.46f, .4f, .46f);
                root.gameObject.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Kettle, i);
                Kettles.Add(root);
            }
            Box("Condiment Shelf", new Vector3(5.3f, StoreLayout.BackCounterHeight + .4f, 4.95f), new Vector3(1.7f, .04f, .4f), MaterialLibrary.Lit(Wood, .4f));
            for (int i = 0; i < Content.Condiments.Length; i++)
            {
                var condiment = Content.Condiments[i];
                var root = new GameObject("Jar " + condiment.Id).transform; root.SetParent(Root, false); root.localPosition = StoreLayout.JarTop(i);
                var jarMesh = MeshFactory.CondimentJar();
                Part("Jar", jarMesh, MaterialLibrary.Transparent(new Color(.95f, .97f, 1f, .45f), .9f), Vector3.zero, Vector3.one * Fit(jarMesh, .28f), root, null, false);
                Part("Content", MeshFactory.Cylinder(.09f, .16f, 14), MaterialLibrary.Lit(new Color(condiment.R, condiment.G, condiment.B), .5f), new Vector3(0, .09f, 0), Vector3.one, root);
                var collider = root.gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .14f, 0); collider.size = new Vector3(.3f, .32f, .3f);
                root.gameObject.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Jar, i);
                Jars.Add(root);
            }
            ScaleRoot = new GameObject("Scale").transform; ScaleRoot.SetParent(Root, false); ScaleRoot.localPosition = StoreLayout.ScaleTop;
            var scaleMesh = MeshFactory.Scale();
            Part("Scale Body", scaleMesh, MaterialLibrary.Lit(new Color(.92f, .92f, .9f), .6f, .3f), Vector3.zero, Vector3.one * Fit(scaleMesh, .5f), ScaleRoot);
            var scaleCollider = ScaleRoot.gameObject.AddComponent<BoxCollider>(); scaleCollider.center = new Vector3(0, .1f, 0); scaleCollider.size = new Vector3(.55f, .25f, .5f);
            ScaleRoot.gameObject.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Scale, 0);
            var cooler = new GameObject("Ingredient Cooler").transform; cooler.SetParent(Root, false); cooler.localPosition = new Vector3(-5.1f, StoreLayout.BackCounterHeight, StoreLayout.BackCounterZ);
            Part("Cooler Glass", StoreBuilder.RoundedBox(new Vector3(2.6f, .5f, .75f), .04f), MaterialLibrary.Transparent(new Color(.8f, .92f, 1f, .2f), .95f), new Vector3(0, .28f, 0), Vector3.one, cooler, null, false);
            Part("Cooler Base", StoreBuilder.RoundedBox(new Vector3(2.7f, .06f, .8f), .02f), steel, new Vector3(0, .03f, 0), Vector3.one, cooler);
            var ladleMesh = MeshFactory.Ladle();
            Ladle = Part("Ladle", ladleMesh, MaterialLibrary.Lit(Steel, .9f, .9f), StoreLayout.KettleTop(0) + new Vector3(.1f, .45f, .05f), Vector3.one * Fit(ladleMesh, .5f), Root).transform;
            Box("Cutting Board", new Vector3(-4.0f, StoreLayout.BackCounterHeight + .02f, 4.55f), new Vector3(.6f, .04f, .4f), MaterialLibrary.Lit(WoodLight, .3f));
        }

        private void BuildDecor(float front, float back, float side)
        {
            var sign = new GameObject("Kitchen Sign").transform; sign.SetParent(Root, false); sign.localPosition = new Vector3(0, 3.15f, back - .16f);
            Part("Sign Board", StoreBuilder.RoundedBox(new Vector3(4.6f, .62f, .08f), .05f), theme.Get(StoreTheme.Surface.Accent), Vector3.zero, Vector3.one, sign);
            Part("Sign Trim", StoreBuilder.RoundedBox(new Vector3(4.75f, .7f, .05f), .05f), theme.Get(StoreTheme.Surface.Wood), new Vector3(0, 0, .03f), Vector3.one, sign);
            Label("麻 辣 湯", new Vector3(0, 0, -.06f), Quaternion.identity, .06f, new Color(1f, .87f, .45f), sign);
            for (int i = 0; i < 2; i++)
            {
                var light = new GameObject("Lantern Light " + i).AddComponent<Light>();
                light.transform.SetParent(Root, false); light.transform.localPosition = new Vector3(i == 0 ? -3f : 3f, 2.6f, -.8f);
                light.type = LightType.Point; light.range = 8; light.color = new Color(1f, .62f, .35f); light.intensity = 0; light.shadows = LightShadows.None;
                NightLights.Add(light);
            }
        }

        /// <summary>金額表示、客向け量り、キャッシュトレーとQR決済をカウンター上に追加します。</summary>
        private void BuildRegisterProps()
        {
            Transform register = Root.Find("Register");
            RegisterDisplay = Label("0.00", new Vector3(0, .38f, .032f), Quaternion.Euler(-15, 0, 0), .0055f, new Color(.65f, 1f, .73f), register);
            RegisterDisplay.gameObject.name = "Register Amount";
            RegisterScale = new GameObject("Customer Register Scale").transform;
            RegisterScale.SetParent(Root, false);
            RegisterScale.localPosition = StoreLayout.RegisterTop + new Vector3(.55f, 0, -.05f);
            CashTray = new GameObject("Cash Tray").transform;
            CashTray.SetParent(Root, false);
            CashTray.localPosition = StoreLayout.RegisterTop + new Vector3(-.72f, .03f, -.12f);
            Transform qr = new GameObject("QR Payment Stand").transform;
            qr.SetParent(Root, false);
            qr.localPosition = StoreLayout.RegisterTop + new Vector3(1.08f, 0, -.1f);
            Material steel = MaterialLibrary.Lit(Steel, .65f, .5f);
            Material black = MaterialLibrary.Lit(Charcoal, .3f);
            using (CityGeometry g = new CityGeometry())
            {
                g.Box(new Vector3(0, .07f, 0), new Vector3(.42f, .14f, .4f), black);
                g.Box(new Vector3(0, .155f, .04f), new Vector3(.4f, .035f, .3f), steel);
                g.Box(new Vector3(0, .1f, -.21f), new Vector3(.28f, .08f, .02f), MaterialLibrary.Emissive(new Color(.07f, .16f, .13f), new Color(.2f, 1f, .55f), .6f));
                g.Flush(RegisterScale, "Customer Scale", false);
            }
            RegisterScaleDisplay = Label("0.00 g", new Vector3(0, .1f, -.225f), Quaternion.identity, .0035f, new Color(.65f, 1f, .73f), RegisterScale);
            RegisterScaleDisplay.gameObject.name = "Customer Scale Display";
            using (CityGeometry g = new CityGeometry())
            {
                g.Box(Vector3.zero, new Vector3(.42f, .025f, .27f), MaterialLibrary.Lit(new Color(.18f, .32f, .3f), .5f));
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    g.Box(new Vector3(sign * .205f, .025f, 0), new Vector3(.025f, .04f, .27f), steel);
                    g.Box(new Vector3(0, .025f, sign * .13f), new Vector3(.42f, .04f, .025f), steel);
                }
                g.Flush(CashTray, "Tray", false);
            }
            using (CityGeometry g = new CityGeometry())
            {
                g.Box(new Vector3(0, .18f, 0), new Vector3(.23f, .34f, .035f), MaterialLibrary.Lit(Cream, .3f));
                g.Box(new Vector3(0, .02f, -.01f), new Vector3(.26f, .04f, .17f), black);
                for (int y = 0; y < 9; y++)
                    for (int x = 0; x < 9; x++)
                    {
                        bool finder = ((x < 3 || x > 5) && y > 5) || (x < 3 && y < 3);
                        if (finder || (x * 11 + y * 7 + x * y) % 3 == 0)
                            g.Box(new Vector3((x - 4) * .017f, .2f + (y - 4) * .017f, -.021f), new Vector3(.016f, .016f, .004f), black);
                    }
                g.Flush(qr, "Payment", false);
            }
            Label("QR決済", new Vector3(0, .06f, -.024f), Quaternion.identity, .003f, Charcoal, qr);
        }

        /// <summary>客席グリッドを避け、壁際と厨房・カウンターだけに生活用品を置きます。</summary>
        private void BuildInteriorProps()
        {
            Transform props = new GameObject("Interior Everyday Props").transform;
            props.SetParent(Root, false);
            Material steel = MaterialLibrary.Lit(Steel, .6f, .4f);
            Material ivory = theme.Get(StoreTheme.Surface.Paper);
            Material wood = theme.Get(StoreTheme.Surface.Wood);
            Material dark = MaterialLibrary.Lit(Charcoal, .3f);
            using (CityGeometry g = new CityGeometry())
            {
                Vector3 water = new Vector3(6.3f, 0, .3f);
                g.Box(water + new Vector3(0, .55f, 0), new Vector3(.55f, 1.1f, .58f), ivory);
                g.Cylinder(water + new Vector3(0, 1.3f, 0), .23f, .38f, MaterialLibrary.Lit(new Color(.4f, .68f, .82f), .6f));
                g.Box(water + new Vector3(0, .88f, -.3f), new Vector3(.3f, .12f, .03f), dark);
                g.Box(water + new Vector3(0, .65f, -.34f), new Vector3(.4f, .03f, .16f), steel);
                AddObstacle(water + new Vector3(0, .75f, 0), new Vector3(.6f, 1.5f, .65f));
                for (int group = 0; group < 2; group++)
                {
                    float x = group == 0 ? .0f : 6.1f;
                    g.Cylinder(new Vector3(x, 1.1f, CounterZForProps()), .07f, .2f, wood);
                    for (int chopstick = 0; chopstick < 7; chopstick++)
                        g.Line(new Vector3(x - .04f + chopstick * .013f, 1.04f, CounterZForProps()), new Vector3(x - .04f + chopstick * .013f, 1.33f, CounterZForProps() + .01f), .006f, wood);
                    g.Box(new Vector3(x + .3f, 1.06f, CounterZForProps()), new Vector3(.22f, .13f, .14f), dark);
                    g.Box(new Vector3(x + .3f, 1.16f, CounterZForProps()), new Vector3(.16f, .15f, .025f), ivory);
                }
                for (int shelf = 0; shelf < 2; shelf++)
                {
                    float y = 1.75f + shelf * .52f;
                    g.Box(new Vector3(-4.8f, y, 5.15f), new Vector3(2.8f, .055f, .38f), steel);
                    for (int bottle = 0; bottle < 5; bottle++)
                    {
                        float x = -5.85f + bottle * .44f;
                        g.Cylinder(new Vector3(x, y + .14f, 5.12f), .07f, .24f, bottle % 2 == 0 ? dark : MaterialLibrary.Lit(Lacquer, .35f));
                        g.Cylinder(new Vector3(x, y + .29f, 5.12f), .03f, .06f, ivory);
                        g.Box(new Vector3(x, y + .15f, 5.045f), new Vector3(.09f, .1f, .012f), ivory);
                    }
                }
                Mesh bowl = MeshFactory.Bowl();
                for (int stack = 0; stack < 3; stack++)
                    for (int level = 0; level < 4; level++)
                        g.Add(bowl, new Vector3(3.4f + stack * .35f, .97f + level * .045f, 5.0f), Vector3.one * .24f, Quaternion.identity, ivory);
                for (int menu = 0; menu < 5; menu++)
                    g.Box(new Vector3(2.8f + menu * .55f, 2.55f, 5.21f), new Vector3(.4f, .85f, .025f), ivory);
                g.Flush(props, "Interior", false);
            }
            string[] menuNames = { "麻辣湯", "牛肉", "青菜", "春雨", "水餃子" };
            for (int menu = 0; menu < menuNames.Length; menu++)
                Label(menuNames[menu] + "\nおすすめ", new Vector3(2.8f + menu * .55f, 2.55f, 5.19f), Quaternion.identity, .006f, Lacquer, props);
            ParticleSystem brothSteam = EffectsFactory.Steam(props, .18f);
            brothSteam.gameObject.name = "Broth Ambient Steam";
            brothSteam.transform.localPosition = StoreLayout.KettleTop(1) + Vector3.up * .3f;
            ParticleSystem.MainModule main = brothSteam.main;
            main.maxParticles = 12;
            ParticleSystem.ShapeModule shape = brothSteam.shape;
            shape.radius = .26f;
            brothSteam.Play();
        }

        /// <summary>カウンター奥行きの範囲に収めた小物用の座標です。</summary>
        private static float CounterZForProps() => StoreLayout.CounterZ - .12f;

        /// <summary>板天井、梁、ペンダント、照明器具と換気扇を一人称表示用に生成します。</summary>
        private void BuildCeiling(float front, float back, float side)
        {
            Ceiling = new GameObject("First Person Ceiling").transform;
            Ceiling.SetParent(Root, false);
            Material ivory = theme.Get(StoreTheme.Surface.Ceiling);
            Material dark = theme.Get(StoreTheme.Surface.Ink);
            Material warm = theme.Get(StoreTheme.Surface.Glow);
            using (CityGeometry g = new CityGeometry())
            {
                g.Box(new Vector3(0, 3.9f, (front + back) * .5f), new Vector3(side * 2, .12f, back - front), ivory);
                for (int i = 0; i < 4; i++)
                {
                    float z = Mathf.Lerp(front + .8f, back - .7f, i / 3f);
                    g.Box(new Vector3(0, 3.75f, z), new Vector3(side * 2, .25f, .16f), theme.Get(StoreTheme.Surface.Wood));
                }
                Mesh shade = MeshFactory.Lathe(new[] { new Vector2(.04f, .25f), new Vector2(.12f, .19f), new Vector2(.28f, 0), new Vector2(.26f, -.03f) }, 12);
                for (int i = 0; i < 3; i++)
                {
                    float x = -4.2f + i * 4.2f;
                    float z = (front + 1f) * .5f;
                    g.Line(new Vector3(x, 3.82f, z), new Vector3(x, 3.18f, z), .01f, dark);
                    g.Add(shade, new Vector3(x, 3.02f, z), Vector3.one, Quaternion.identity, theme.Get(StoreTheme.Surface.Accent));
                    g.Cylinder(new Vector3(x, 3.0f, z), .23f, .025f, warm);
                }
                for (int i = -1; i <= 1; i += 2)
                {
                    g.Box(new Vector3(i * 3.5f, 3.78f, 3.4f), new Vector3(1.5f, .1f, .32f), dark);
                    g.Box(new Vector3(i * 3.5f, 3.71f, 3.4f), new Vector3(1.3f, .03f, .25f), warm);
                }
                g.Box(new Vector3(5.8f, 3.77f, 4.2f), new Vector3(.8f, .08f, .8f), dark);
                for (int blade = 0; blade < 5; blade++)
                    g.Box(new Vector3(5.8f, 3.715f, 4.2f), new Vector3(.63f, .03f, .08f), ivory, Quaternion.Euler(0, blade * 36, 0));
                g.Flush(Ceiling, "Ceiling", false);
            }
            Ceiling.gameObject.SetActive(false);
        }

        /// <summary>内装テーマに登録せず、街と入口・軒先の季節装飾を独立に更新します。</summary>
        public void ApplySeason(string seasonId, string weatherId, bool highQuality)
        {
            city?.ApplySeason(seasonId, weatherId, highQuality);
            Street?.SetSeason(seasonId, weatherId, highQuality);
            bool snow = seasonId == "winter" && weatherId == "cold";
            if (seasonalEntrance != null && entranceSeason == seasonId && entranceSnow == snow) return;
            if (seasonalEntrance != null)
            {
                for (int i = CutawayWalls.Count - 1; i >= 0; i--)
                    if (CutawayWalls[i].wall == seasonalEntrance) CutawayWalls.RemoveAt(i);
                seasonalEntrance.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(seasonalEntrance.gameObject);
            }
            if (seasonalGroundEntrance != null)
            {
                seasonalGroundEntrance.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(seasonalGroundEntrance.gameObject);
            }
            entranceSeason = seasonId; entranceSnow = snow;
            seasonalEntrance = new GameObject("Seasonal Awning Entrance").transform;
            seasonalEntrance.SetParent(Root, false);
            CutawayWalls.Add((seasonalEntrance, Vector3.forward, 1));
            float front = StoreLayout.FrontZ(rows);
            Material cord = MaterialLibrary.Lit(new Color(.74f, .08f, .04f), .3f);
            Material bamboo = MaterialLibrary.Lit(new Color(.66f, .48f, .24f), .3f);
            using (CityGeometry g = new CityGeometry())
            {
                if (seasonId == "summer")
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = side * 3.4f;
                        for (int slat = 0; slat < 18; slat++)
                            g.Box(new Vector3(x, 2.85f - slat * .04f, front - .92f), new Vector3(1.6f, .022f, .025f), bamboo);
                        g.Line(new Vector3(x - .6f, 2.15f, front - .93f), new Vector3(x - .6f, 2.9f, front - .93f), .008f, cord);
                        g.Line(new Vector3(x + .6f, 2.15f, front - .93f), new Vector3(x + .6f, 2.9f, front - .93f), .008f, cord);
                    }
                }
                if (seasonId == "winter")
                {
                    Material gold = MaterialLibrary.Lit(Gold, .45f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 p = new Vector3(side * 1.5f, 2.35f, front - .48f);
                        g.Line(p + Vector3.up * .55f, p + Vector3.up * .18f, .014f, cord);
                        g.Sphere(p, new Vector3(.4f, .48f, .4f), cord);
                        g.Cylinder(p + Vector3.up * .24f, .12f, .035f, gold);
                        g.Cylinder(p - Vector3.up * .24f, .12f, .035f, gold);
                        Vector3 knot = p - Vector3.up * .53f;
                        for (int j = 0; j < 4; j++)
                        {
                            Vector3 a = knot + Quaternion.Euler(0, 0, j * 90) * Vector3.up * .15f;
                            Vector3 b = knot + Quaternion.Euler(0, 0, (j + 1) * 90) * Vector3.up * .15f;
                            g.Line(a, b, .018f, cord);
                        }
                        for (int tassel = 0; tassel < 3; tassel++)
                            g.Line(knot + new Vector3((tassel - 1) * .035f, -.15f, 0), knot + new Vector3((tassel - 1) * .035f, -.35f, 0), .01f, cord);
                    }
                }
                if (snow)
                {
                    Material frost = MaterialLibrary.Lit(new Color(.98f, 1f, 1f), .2f);
                    g.Box(new Vector3(0, 3.18f, front - .55f), new Vector3(12.4f, .075f, 1.06f), frost, Quaternion.Euler(-18, 0, 0));
                }
                g.Flush(seasonalEntrance, "Seasonal Entrance", false);
            }
            if (seasonId == "summer") BuildWindChimes(front, bamboo);
            BuildGroundSeasonalEntrance(front, seasonId, snow, bamboo, cord);
        }

        /// <summary>庇の切り欠きでは隠さない鉢植え・季節の枝・低い飾りを入口の外側に結合生成します。</summary>
        private void BuildGroundSeasonalEntrance(float front, string seasonId, bool snow, Material bamboo, Material red)
        {
            seasonalGroundEntrance = new GameObject("Ground Seasonal Entrance").transform;
            seasonalGroundEntrance.SetParent(Root, false);
            Material clay = MaterialLibrary.Lit(new Color(.64f, .32f, .18f), .3f);
            Material bark = MaterialLibrary.Lit(new Color(.32f, .2f, .12f), .25f);
            Color foliage = seasonId == "spring" ? new Color(1f, .48f, .68f)
                : seasonId == "summer" ? new Color(.12f, .45f, .17f) : new Color(.98f, .35f, .035f);
            Material leaves = MaterialLibrary.Lit(foliage, .25f);
            Material alternate = MaterialLibrary.Lit(seasonId == "autumn" ? new Color(.8f, .08f, .035f)
                : seasonId == "spring" ? new Color(1f, .77f, .83f) : new Color(.35f, .62f, .24f), .25f);
            Material gold = MaterialLibrary.Lit(Gold, .45f);
            Material frost = MaterialLibrary.Lit(new Color(.98f, 1f, 1f), .2f);
            using (CityGeometry g = new CityGeometry())
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 pot = new Vector3(side * 2.6f, 0, front - 1.6f);
                    g.Cylinder(pot + Vector3.up * .26f, .3f, .5f, clay);
                    g.Cylinder(pot + Vector3.up * .52f, .26f, .035f, bark);
                    g.Line(pot + Vector3.up * .5f, pot + Vector3.up * 1.65f, .045f, bark);
                    for (int branch = 0; branch < 6; branch++)
                    {
                        float angle = branch * 2.4f;
                        Vector3 tip = pot + new Vector3(Mathf.Cos(angle) * .4f, 1.15f + branch % 3 * .23f, Mathf.Sin(angle) * .36f);
                        g.Line(pot + Vector3.up * (.8f + branch * .12f), tip, .028f, bark);
                        if (seasonId == "winter")
                        {
                            g.Line(tip, tip + new Vector3(.22f, .18f, -.16f), .02f, bark);
                            g.Line(tip, tip + new Vector3(-.18f, .2f, .12f), .02f, bark);
                        }
                        else g.Sphere(tip, new Vector3(.35f, .27f, .33f), branch % 2 == 0 ? leaves : alternate);
                    }
                    if (seasonId == "summer")
                    {
                        Vector3 panel = pot + new Vector3(side * .55f, 0, -.15f);
                        g.Line(panel + Vector3.up * .04f, panel + Vector3.up * 1.3f, .022f, bamboo);
                        for (int slat = 0; slat < 12; slat++)
                            g.Box(panel + Vector3.up * (1.25f - slat * .045f), new Vector3(.55f, .024f, .035f), bamboo);
                        g.Box(panel + new Vector3(0, .52f, 0), new Vector3(.34f, .45f, .025f), alternate);
                    }
                    if (seasonId == "winter")
                    {
                        Vector3 ornament = pot + new Vector3(side * .58f, 1.02f, -.18f);
                        g.Line(ornament - Vector3.up * .95f, ornament + Vector3.up * .7f, .025f, bamboo);
                        g.Sphere(ornament + Vector3.up * .38f, new Vector3(.37f, .4f, .37f), red);
                        g.Cylinder(ornament + Vector3.up * .57f, .11f, .045f, gold);
                        g.Cylinder(ornament + Vector3.up * .19f, .11f, .045f, gold);
                        g.Box(ornament, new Vector3(.24f, .24f, .035f), red, Quaternion.Euler(0, 0, 45));
                        for (int tassel = 0; tassel < 3; tassel++)
                            g.Line(ornament + new Vector3((tassel - 1) * .04f, -.13f, 0), ornament + new Vector3((tassel - 1) * .04f, -.35f, 0), .012f, red);
                    }
                    if (seasonId == "spring" || seasonId == "autumn")
                    {
                        Mesh leaf = MeshFactory.Leaf(.12f, .19f, .007f);
                        for (int i = 0; i < 10; i++)
                            g.Add(leaf, pot + new Vector3(Mathf.Cos(i * 2.4f) * (.45f + i * .025f), .018f,
                                Mathf.Sin(i * 2.4f) * .55f), Vector3.one, Quaternion.Euler(0, i * 137, 0), i % 2 == 0 ? leaves : alternate);
                    }
                    if (snow)
                    {
                        g.Cylinder(pot + Vector3.up * .555f, .285f, .055f, frost);
                        g.Box(pot + new Vector3(0, .045f, 0), new Vector3(1.25f, .075f, 1.05f), frost);
                        g.Box(new Vector3(side * 6.05f, 3.18f, front - .55f), new Vector3(.65f, .075f, 1.06f), frost, Quaternion.Euler(-18, 0, 0));
                    }
                }
                g.Flush(seasonalGroundEntrance, "Ground Seasonal Entrance", false);
            }
        }

        /// <summary>二個の風鈴を軒先に吊り、一個の結合Rendererずつで緩やかに揺らします。</summary>
        private void BuildWindChimes(float front, Material paper)
        {
            Material glass = MaterialLibrary.Lit(new Color(.52f, .79f, .82f), .8f, .15f);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform chime = new GameObject("Wind Chime").transform;
                chime.SetParent(seasonalEntrance, false);
                chime.localPosition = new Vector3(side * 2.05f, 2.95f, front - 1.02f);
                using (CityGeometry g = new CityGeometry())
                {
                    g.Line(Vector3.zero, Vector3.down * .18f, .006f, paper);
                    g.Sphere(Vector3.down * .24f, new Vector3(.15f, .15f, .15f), glass);
                    g.Line(Vector3.down * .25f, Vector3.down * .45f, .005f, paper);
                    g.Box(Vector3.down * .51f, new Vector3(.065f, .14f, .008f), paper);
                    g.Flush(chime, "Wind Chime", false);
                }
                chime.gameObject.AddComponent<SeasonalChime>().Phase = side;
            }
        }

        /// <summary>軒先の既存の日よけを生成します。</summary>
        private void BuildStreet(float front, float side, string storeName)
        {
            Transform awning = new GameObject("Awning").transform; awning.SetParent(Root, false); awning.localPosition = new Vector3(0, 3.1f, front - .55f);
            for (int i = 0; i < 9; i++)
                Part("Stripe", StoreBuilder.RoundedBox(new Vector3(1.4f, .06f, 1.1f), .02f), theme.Get(i % 2 == 0 ? StoreTheme.Surface.Accent : StoreTheme.Surface.Ceiling), new Vector3(-5.6f + i * 1.4f, 0, 0), Vector3.one, awning, Quaternion.Euler(-18, 0, 0));
            ShopSign = new GameObject("Shop Sign").transform; ShopSign.SetParent(Root, false); ShopSign.localPosition = new Vector3(0, 3.65f, front - .2f);
            Part("Board", StoreBuilder.RoundedBox(new Vector3(5.4f, .6f, .1f), .05f), theme.Get(StoreTheme.Surface.Wood), Vector3.zero, Vector3.one, ShopSign);
            storeNameLabel = Label(storeName, new Vector3(0, 0, -.07f), Quaternion.identity, .05f, new Color(1f, .85f, .45f), ShopSign);
            storeNameLabel.gameObject.name = "Store Name";
            CutawayWalls.Add((awning, Vector3.forward, 1));
            CutawayWalls.Add((ShopSign, Vector3.forward, 1));
            city = new CityBuilder(this, front);
            Street = city.Build();
        }
    }
    /// <summary>物理演算や点光源を追加せず、風鈴だけを小角度で揺らします。</summary>
    internal sealed class SeasonalChime : MonoBehaviour
    {
        internal float Phase;
        /// <summary>時間から風の揺れを決定し、毎フレームの割り当てを避けます。</summary>
        private void Update() => transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 1.7f + Phase) * 7f);
    }

}
