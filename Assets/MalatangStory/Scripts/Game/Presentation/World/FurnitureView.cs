using System.Collections.Generic;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>家具のクリック判定用の目印。</summary>
    public sealed class FurnitureTag : MonoBehaviour { public int Uid; }

    /// <summary>座席のワールド座標と向き（テーブル側を向く）。</summary>
    public readonly struct SeatPose
    {
        public readonly Vector3 Position, TableSpot;
        public readonly Quaternion Facing;
        public SeatPose(Vector3 position, Vector3 tableSpot, Quaternion facing) { Position = position; TableSpot = tableSpot; Facing = facing; }
    }

    /// <summary>
    /// 客席の家具を見た目として生成し、座席の位置と片付け前の器を管理する。
    /// 配置データ（DiningLayout）から毎回作り直せるようにしている。
    /// </summary>
    public sealed class FurnitureView
    {
        private readonly Transform root;
        private readonly StoreBuilder builder;
        private readonly Dictionary<int, Transform> items = new Dictionary<int, Transform>();
        private readonly Dictionary<int, SeatPose> seats = new Dictionary<int, SeatPose>();
        private readonly Dictionary<int, List<GameObject>> dirtyBowls = new Dictionary<int, List<GameObject>>();
        private readonly Font font;
        private GameState state;
        /// <summary>実際の厨房と同じ器を作る生成時の依存性です。</summary>
        public System.Func<MealRemains, Vector3, GameObject> CreateMealBowl { get; set; }

        public FurnitureView(Transform parent, StoreBuilder builder, Font font)
        {
            root = new GameObject("Furniture").transform;
            root.SetParent(parent, false);
            this.builder = builder; this.font = font;
        }

        public bool TryGetSeat(int seatId, out SeatPose pose) => seats.TryGetValue(seatId, out pose);
        public Transform Item(int uid) => items.TryGetValue(uid, out var t) ? t : null;

        /// <summary>家具の中心のワールド座標。</summary>
        public static Vector3 Center(PlacedFurniture item, int rows)
        {
            var def = Content.FurnitureById(item.defId);
            if (def.Kind == FurnitureKind.Wall) return StoreLayout.WallSlot(item.x, rows).position;
            var (w, d) = DiningLayout.Footprint(def, item.rotation);
            return new Vector3(-5.5f + item.x + (w - 1) / 2f, 0, -1.5f - item.z - (d - 1) / 2f);
        }

        /// <summary>全家具を作り直す。</summary>
        public void Rebuild(GameState state, List<UnityEngine.AI.NavMeshBuildSource> obstacles)
        {
            this.state = state;
            foreach (var t in items.Values) if (t != null) Object.Destroy(t.gameObject);
            foreach (var list in dirtyBowls.Values) foreach (var b in list) if (b != null) Object.Destroy(b);
            items.Clear(); seats.Clear(); dirtyBowls.Clear();
            int rows = DiningLayout.Rows(state);
            foreach (var item in state.furniture) items[item.uid] = Create(item, rows, obstacles);
            foreach (int table in state.dirtyTables) SetDirty(table, true);
        }

        /// <summary>1つの家具を作る（内装画面のゴーストにも使う）。</summary>
        public Transform Create(PlacedFurniture item, int rows, List<UnityEngine.AI.NavMeshBuildSource> obstacles, bool ghost = false)
        {
            var def = Content.FurnitureById(item.defId);
            var go = new GameObject(def.Name).transform;
            go.SetParent(root, false);
            if (def.Kind == FurnitureKind.Wall)
            {
                var (pos, rot) = StoreLayout.WallSlot(item.x, rows);
                go.localPosition = pos; go.localRotation = rot;
                BuildWall(def.Id, go);
            }
            else
            {
                go.localPosition = Center(item, rows);
                go.localRotation = Quaternion.Euler(0, item.rotation % 2 == 1 ? 90 : 0, 0);
                BuildFloorItem(def, go, item.uid, obstacles, ghost);
            }
            if (!ghost)
            {
                var tag = go.gameObject.AddComponent<FurnitureTag>(); tag.Uid = item.uid;
                var collider = go.gameObject.AddComponent<BoxCollider>();
                var (w, d) = def.Kind == FurnitureKind.Wall ? (1, 1) : (def.Width, def.Depth);
                collider.center = new Vector3(0, .4f, 0); collider.size = def.Kind == FurnitureKind.Wall ? new Vector3(1, 1, .3f) : new Vector3(w * .95f, .8f, d * .95f);
                if (def.Kind == FurnitureKind.Table) go.gameObject.AddComponent<WorldTarget>().Set(Station.Table, item.uid);
            }
            return go;
        }

        private void AddSeat(int uid, int index, Transform parent, Vector3 local, Vector3 tableLocal)
        {
            Vector3 world = parent.TransformPoint(local);
            Vector3 table = parent.TransformPoint(tableLocal);
            Vector3 dir = table - world; dir.y = 0;
            seats[uid * 10 + index] = new SeatPose(world, table, Quaternion.LookRotation(dir.sqrMagnitude > .001f ? dir.normalized : Vector3.forward));
        }

        private Material M(Color c, float s = .35f, float m = 0) => MaterialLibrary.Lit(c, s, m);

        /// <summary>卓上の小物（箸立て・酢とラー油の瓶・紙ナプキン・メニュー立て）を置く。</summary>
        private void TableProps(Transform go, Vector3 center, float topY, bool ghost)
        {
            if (ghost) return;
            var holder = M(new Color(.42f, .26f, .16f), .4f);
            StoreBuilder.Part("Chopstick Holder", MeshFactory.Cylinder(.045f, .12f, 12), holder, center + new Vector3(-.12f, topY + .06f, 0), Vector3.one, go);
            for (int i = 0; i < 4; i++)
                StoreBuilder.Part("Chopstick", MeshFactory.Cylinder(.006f, .16f, 5), M(new Color(.88f, .75f, .5f), .3f), center + new Vector3(-.13f + (i % 2) * .02f, topY + .14f, (i / 2) * .02f - .01f), Vector3.one, go);
            StoreBuilder.Part("Vinegar", MeshFactory.Cylinder(.028f, .13f, 10), M(new Color(.18f, .1f, .06f), .8f), center + new Vector3(.02f, topY + .065f, .03f), Vector3.one, go);
            StoreBuilder.Part("Chili Oil", MeshFactory.Cylinder(.034f, .09f, 10), M(new Color(.75f, .12f, .05f), .7f), center + new Vector3(.1f, topY + .045f, .02f), Vector3.one, go);
            StoreBuilder.Part("Napkins", StoreBuilder.RoundedBox(new Vector3(.12f, .06f, .08f), .01f), M(new Color(.95f, .93f, .88f), .2f), center + new Vector3(.02f, topY + .03f, -.09f), Vector3.one, go);
            StoreBuilder.Part("Menu Stand", StoreBuilder.RoundedBox(new Vector3(.12f, .15f, .01f), .005f), M(StoreBuilder.Lacquer, .3f), center + new Vector3(.17f, topY + .075f, -.07f), Vector3.one, go, Quaternion.Euler(-12, 25, 0));
        }

        private void BuildFloorItem(FurnitureDefinition def, Transform go, int uid, List<UnityEngine.AI.NavMeshBuildSource> obstacles, bool ghost)
        {
            var wood = M(StoreBuilder.Wood, .4f);
            var red = M(StoreBuilder.Lacquer, .45f);
            var cream = M(StoreBuilder.Cream, .4f);
            void Obstacle(Vector3 local, Vector3 size)
            {
                if (ghost || obstacles == null) return;
                obstacles.Add(new UnityEngine.AI.NavMeshBuildSource { shape = UnityEngine.AI.NavMeshBuildSourceShape.Box, size = size, area = 1,
                    transform = Matrix4x4.TRS(go.TransformPoint(local), go.rotation, Vector3.one) });
            }
            switch (def.Id)
            {
                case "table_round2":
                    StoreBuilder.Part("Top", MeshFactory.Cylinder(.48f, .06f, 28, .02f), wood, new Vector3(0, .5f, 0), Vector3.one, go);
                    StoreBuilder.Part("Leg", MeshFactory.Cylinder(.06f, .48f, 10), M(StoreBuilder.Charcoal, .5f, .5f), new Vector3(0, .25f, 0), Vector3.one, go);
                    StoreBuilder.Part("Foot", MeshFactory.Cylinder(.24f, .03f, 16), M(StoreBuilder.Charcoal, .5f, .5f), new Vector3(0, .015f, 0), Vector3.one, go);
                    for (int i = 0; i < 2; i++)
                    {
                        float x = i == 0 ? -.68f : .68f;
                        StoreBuilder.Part("Stool", MeshFactory.Cylinder(.2f, .08f, 18, .02f), red, new Vector3(x, .4f, 0), Vector3.one, go);
                        StoreBuilder.Part("Stool Leg", MeshFactory.Cylinder(.05f, .38f, 8), M(StoreBuilder.Charcoal, .5f, .5f), new Vector3(x, .19f, 0), Vector3.one, go);
                        if (!ghost) AddSeat(uid, i, go, new Vector3(x, 0, 0), Vector3.zero);
                    }
                    TableProps(go, new Vector3(0, 0, .2f), .53f, ghost);
                    Obstacle(new Vector3(0, .4f, 0), new Vector3(.8f, .8f, .8f));
                    break;
                case "table_counter3":
                    StoreBuilder.Part("Bar", StoreBuilder.RoundedBox(new Vector3(2.8f, .07f, .5f), .03f), wood, new Vector3(0, .6f, .22f), Vector3.one, go);
                    StoreBuilder.Part("Bar Body", StoreBuilder.RoundedBox(new Vector3(2.7f, .56f, .3f), .03f), red, new Vector3(0, .29f, .3f), Vector3.one, go);
                    for (int i = 0; i < 3; i++)
                    {
                        float x = -1f + i;
                        StoreBuilder.Part("Stool", MeshFactory.Cylinder(.18f, .07f, 16, .02f), cream, new Vector3(x, .42f, -.2f), Vector3.one, go);
                        StoreBuilder.Part("Stool Leg", MeshFactory.Cylinder(.04f, .4f, 8), M(StoreBuilder.Charcoal, .5f, .5f), new Vector3(x, .2f, -.2f), Vector3.one, go);
                        if (!ghost) AddSeat(uid, i, go, new Vector3(x, 0, -.22f), new Vector3(x, 0, .3f));
                    }
                    TableProps(go, new Vector3(-.5f, 0, .38f), .635f, ghost);
                    TableProps(go, new Vector3(.75f, 0, .38f), .635f, ghost);
                    Obstacle(new Vector3(0, .4f, .28f), new Vector3(2.8f, .8f, .45f));
                    break;
                case "table_square4":
                case "table_booth4":
                    bool booth = def.Id == "table_booth4";
                    StoreBuilder.Part("Top", StoreBuilder.RoundedBox(new Vector3(1.7f, .07f, .82f), .03f), booth ? M(new Color(.3f, .18f, .12f), .5f) : wood, new Vector3(0, .5f, 0), Vector3.one, go);
                    for (int i = 0; i < 4; i++)
                        StoreBuilder.Part("Leg", MeshFactory.Cylinder(.04f, .48f, 8), M(StoreBuilder.Charcoal, .5f, .5f), new Vector3(i % 2 == 0 ? -.75f : .75f, .24f, i < 2 ? -.33f : .33f), Vector3.one, go);
                    for (int side = 0; side < 2; side++)
                    {
                        float z = side == 0 ? .66f : -.66f;
                        if (booth)
                        {
                            StoreBuilder.Part("Sofa", StoreBuilder.RoundedBox(new Vector3(1.8f, .42f, .5f), .12f), red, new Vector3(0, .21f, z * 1.05f), Vector3.one, go);
                            StoreBuilder.Part("Back", StoreBuilder.RoundedBox(new Vector3(1.8f, .6f, .16f), .07f), red, new Vector3(0, .62f, z * 1.38f), Vector3.one, go);
                        }
                        for (int k = 0; k < 2; k++)
                        {
                            float x = k == 0 ? -.45f : .45f;
                            if (!booth)
                            {
                                StoreBuilder.Part("Chair", StoreBuilder.RoundedBox(new Vector3(.38f, .06f, .38f), .03f), red, new Vector3(x, .42f, z), Vector3.one, go);
                                StoreBuilder.Part("Chair Back", StoreBuilder.RoundedBox(new Vector3(.38f, .38f, .05f), .02f), wood, new Vector3(x, .64f, z + Mathf.Sign(z) * .19f), Vector3.one, go);
                            }
                            if (!ghost) AddSeat(uid, side * 2 + k, go, new Vector3(x, 0, z), new Vector3(x, 0, 0));
                        }
                    }
                    TableProps(go, new Vector3(-.1f, 0, 0), .535f, ghost);
                    Obstacle(new Vector3(0, .4f, 0), new Vector3(1.7f, .8f, .82f));
                    break;
                case "plant":
                    StoreBuilder.Part("Pot", MeshFactory.Lathe(new[] { new Vector2(0, 0), new Vector2(.2f, 0), new Vector2(.26f, .4f), new Vector2(.22f, .42f), new Vector2(0, .42f) }, 18), M(new Color(.75f, .45f, .3f)), Vector3.zero, Vector3.one, go);
                    for (int i = 0; i < 7; i++)
                    {
                        var leaf = StoreBuilder.Part("Leaf", MeshFactory.Leaf(.22f, .7f, .3f), M(new Color(.25f, .55f + i * .02f, .25f), .3f), new Vector3(0, .4f, 0), Vector3.one, go);
                        leaf.transform.localRotation = Quaternion.Euler(-55 + (i % 3) * 12, i * 51, 0);
                    }
                    Obstacle(new Vector3(0, .5f, 0), new Vector3(.6f, 1f, .6f));
                    break;
                case "lantern_stand":
                    StoreBuilder.Part("Base", MeshFactory.Cylinder(.26f, .12f, 6, .02f), M(new Color(.6f, .6f, .58f), .2f), new Vector3(0, .06f, 0), Vector3.one, go);
                    StoreBuilder.Part("Pillar", MeshFactory.Cylinder(.1f, .7f, 6), M(new Color(.62f, .62f, .6f), .2f), new Vector3(0, .47f, 0), Vector3.one, go);
                    StoreBuilder.Part("Light", StoreBuilder.RoundedBox(new Vector3(.36f, .3f, .36f), .03f), MaterialLibrary.Emissive(new Color(1f, .9f, .7f), new Color(1f, .7f, .35f), 1.2f), new Vector3(0, .97f, 0), Vector3.one, go);
                    StoreBuilder.Part("Roof", MeshFactory.Cylinder(.34f, .1f, 6, .03f), M(new Color(.5f, .5f, .48f), .2f), new Vector3(0, 1.17f, 0), Vector3.one, go);
                    Obstacle(new Vector3(0, .6f, 0), new Vector3(.6f, 1.2f, .6f));
                    break;
                case "maneki":
                    StoreBuilder.Part("Stand", StoreBuilder.RoundedBox(new Vector3(.5f, .5f, .5f), .05f), red, new Vector3(0, .25f, 0), Vector3.one, go);
                    StoreBuilder.Part("Body", MeshFactory.Sphere(), M(Color.white, .6f), new Vector3(0, .68f, 0), new Vector3(.36f, .38f, .32f), go);
                    StoreBuilder.Part("Head", MeshFactory.Sphere(), M(Color.white, .6f), new Vector3(0, .98f, 0), new Vector3(.32f, .28f, .28f), go);
                    StoreBuilder.Part("Paw", MeshFactory.Sphere(), M(Color.white, .6f), new Vector3(.17f, 1.0f, -.06f), new Vector3(.1f, .14f, .1f), go);
                    StoreBuilder.Part("Bell", MeshFactory.Sphere(), M(StoreBuilder.Gold, .8f, .8f), new Vector3(0, .82f, -.15f), Vector3.one * .07f, go);
                    StoreBuilder.Part("Collar", MeshFactory.Torus(.12f, .02f), red, new Vector3(0, .86f, 0), Vector3.one, go);
                    for (int i = -1; i <= 1; i += 2) StoreBuilder.Part("Ear", MeshFactory.Sphere(), M(Color.white, .6f), new Vector3(i * .1f, 1.12f, 0), new Vector3(.08f, .1f, .06f), go);
                    Obstacle(new Vector3(0, .5f, 0), new Vector3(.6f, 1f, .6f));
                    break;
                case "bamboo":
                    StoreBuilder.Part("Pot", MeshFactory.Cylinder(.24f, .3f, 16, .02f), M(new Color(.25f, .3f, .35f), .5f), new Vector3(0, .15f, 0), Vector3.one, go);
                    for (int i = 0; i < 5; i++)
                    {
                        var stalk = StoreBuilder.Part("Stalk", MeshFactory.Cylinder(.025f, 1.4f, 8), M(new Color(.45f, .65f, .3f), .4f), new Vector3((i - 2) * .06f, .95f, (i % 2) * .06f), Vector3.one, go);
                        stalk.transform.localRotation = Quaternion.Euler((i - 2) * 3, 0, (i - 2) * 4);
                        StoreBuilder.Part("Leaf", MeshFactory.Leaf(.08f, .3f, .2f), M(new Color(.35f, .6f, .25f), .3f), new Vector3((i - 2) * .08f, 1.5f + i * .05f, 0), Vector3.one, go).transform.localRotation = Quaternion.Euler(-40, i * 70, 0);
                    }
                    Obstacle(new Vector3(0, .6f, 0), new Vector3(.6f, 1.2f, .6f));
                    break;
                case "fish_tank":
                    StoreBuilder.Part("Cabinet", StoreBuilder.RoundedBox(new Vector3(.8f, .55f, .55f), .04f), wood, new Vector3(0, .275f, 0), Vector3.one, go);
                    StoreBuilder.Part("Glass", StoreBuilder.RoundedBox(new Vector3(.75f, .5f, .5f), .03f), MaterialLibrary.Transparent(new Color(.55f, .8f, .95f, .35f), .95f), new Vector3(0, .82f, 0), Vector3.one, go, null, false);
                    for (int i = 0; i < 3; i++)
                        StoreBuilder.Part("Fish", MeshFactory.Sphere(), M(new Color(1f, .45f, .15f), .7f), new Vector3(-.2f + i * .2f, .75f + (i % 2) * .1f, 0), new Vector3(.1f, .05f, .04f), go).AddComponent<Bobbing>().Speed = 1 + i * .3f;
                    Obstacle(new Vector3(0, .5f, 0), new Vector3(.8f, 1f, .6f));
                    break;
                case "folding_screen":
                    for (int i = 0; i < 4; i++)
                    {
                        var panel = StoreBuilder.Part("Panel", StoreBuilder.RoundedBox(new Vector3(.46f, 1.4f, .04f), .02f), M(new Color(.95f, .9f, .78f), .2f), new Vector3(-.72f + i * .48f, .72f, (i % 2) * .08f), Vector3.one, go);
                        panel.transform.localRotation = Quaternion.Euler(0, i % 2 == 0 ? 12 : -12, 0);
                        StoreBuilder.Part("Mountain", StoreBuilder.RoundedBox(new Vector3(.3f, .35f, .01f), .01f), M(new Color(.35f, .45f, .4f), .2f), new Vector3(0, .1f, -.03f), Vector3.one, panel.transform, Quaternion.Euler(0, 0, 45));
                    }
                    Obstacle(new Vector3(0, .7f, 0), new Vector3(1.9f, 1.4f, .4f));
                    break;
            }
        }

        private void BuildWall(string id, Transform go)
        {
            switch (id)
            {
                case "poster_menu":
                    StoreBuilder.Part("Poster", StoreBuilder.RoundedBox(new Vector3(.8f, 1.0f, .02f), .01f), M(new Color(.98f, .93f, .78f), .2f), Vector3.zero, Vector3.one, go);
                    StoreBuilder.Part("Photo", StoreBuilder.RoundedBox(new Vector3(.6f, .4f, .01f), .01f), M(new Color(.75f, .2f, .1f), .4f), new Vector3(0, .15f, .015f), Vector3.one, go);
                    WallText("人気No.1", go, -.25f);
                    break;
                case "calligraphy":
                    StoreBuilder.Part("Scroll", StoreBuilder.RoundedBox(new Vector3(.55f, 1.2f, .02f), .01f), M(new Color(.97f, .94f, .86f), .2f), Vector3.zero, Vector3.one, go);
                    StoreBuilder.Part("Rod", MeshFactory.Cylinder(.025f, .7f, 8), M(StoreBuilder.Wood), new Vector3(0, .62f, .02f), Vector3.one, go, Quaternion.Euler(0, 0, 90));
                    WallText("福", go, 0, .09f, new Color(.1f, .08f, .08f));
                    break;
                case "red_knot":
                    StoreBuilder.Part("Knot", MeshFactory.Torus(.18f, .04f), M(StoreBuilder.Lacquer, .5f), new Vector3(0, .1f, .03f), Vector3.one, go, Quaternion.Euler(90, 45, 0));
                    StoreBuilder.Part("Tassel", MeshFactory.Cylinder(.05f, .4f, 8), M(StoreBuilder.Lacquer, .3f), new Vector3(0, -.3f, .03f), Vector3.one, go);
                    break;
                case "paper_cut":
                    for (int i = 0; i < 4; i++)
                        StoreBuilder.Part("Cut", MeshFactory.Disc(.2f, 8), M(StoreBuilder.Lacquer, .2f), new Vector3((i % 2 - .5f) * .45f, (i / 2 - .5f) * .45f, .02f), Vector3.one, go, Quaternion.Euler(-90, 0, 22.5f));
                    break;
                case "television":
                    StoreBuilder.Part("TV", StoreBuilder.RoundedBox(new Vector3(1.1f, .66f, .08f), .03f), M(new Color(.1f, .1f, .1f), .6f), Vector3.zero, Vector3.one, go);
                    StoreBuilder.Part("Screen", StoreBuilder.RoundedBox(new Vector3(1f, .56f, .01f), .01f), MaterialLibrary.Emissive(new Color(.2f, .4f, .6f), new Color(.4f, .7f, 1f), .8f), new Vector3(0, 0, .045f), Vector3.one, go);
                    break;
                case "neon_sign":
                    StoreBuilder.Part("Back", StoreBuilder.RoundedBox(new Vector3(1.4f, .6f, .04f), .03f), M(new Color(.1f, .08f, .1f), .5f), Vector3.zero, Vector3.one, go);
                    WallText("麻辣湯", go, 0, .07f, new Color(1f, .35f, .55f));
                    StoreBuilder.Part("Glow", StoreBuilder.RoundedBox(new Vector3(1.2f, .04f, .02f), .01f), MaterialLibrary.Emissive(new Color(1f, .3f, .5f), new Color(1f, .3f, .6f), 3f), new Vector3(0, -.22f, .03f), Vector3.one, go);
                    break;
            }
        }

        private void WallText(string text, Transform parent, float y, float size = .03f, Color? color = null)
        {
            var go = new GameObject("Wall Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, y, .04f);
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text; mesh.font = font; mesh.fontSize = 64; mesh.characterSize = size; mesh.anchor = TextAnchor.MiddleCenter; mesh.color = color ?? StoreBuilder.Lacquer;
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        /// <summary>日付変更時などに、片付け前の器をすべて即座に隠して破棄します。</summary>
        public void ClearDirtyBowls()
        {
            foreach (List<GameObject> list in dirtyBowls.Values) DestroyBowls(list);
            dirtyBowls.Clear();
        }

        /// <summary>遅延破棄を待つフレームでも、古い器が重ならないよう非表示にします。</summary>
        private static void DestroyBowls(List<GameObject> bowls)
        {
            foreach (GameObject bowl in bowls)
            {
                if (bowl == null) continue;
                bowl.SetActive(false);
                Object.Destroy(bowl);
            }
        }

        /// <summary>席ごとの記録に従って器を置き、情報のない席だけを従来表示へ戻します。</summary>
        public void SetDirty(int tableUid, bool dirty)
        {
            if (dirtyBowls.TryGetValue(tableUid, out List<GameObject> list)) { DestroyBowls(list); dirtyBowls.Remove(tableUid); }
            if (!dirty || !items.TryGetValue(tableUid, out Transform table) || table == null) return;
            list = new List<GameObject>();
            foreach (KeyValuePair<int, SeatPose> pair in seats)
            {
                if (DiningLayout.TableOfSeat(pair.Key) != tableUid) continue;
                MealRemains detail = null;
                bool occupied = false;
                if (state != null)
                {
                    foreach (MealRemains remains in state.mealRemains) if (remains.seat == pair.Key) { detail = remains; break; }
                    foreach (CustomerOrder order in state.orders)
                        if (order.seat == pair.Key && DiningLayout.OccupiesSeat(order)) { occupied = true; break; }
                }
                if (occupied) continue;
                Vector3 spot = Vector3.Lerp(pair.Value.Position, pair.Value.TableSpot, .62f);
                spot.y = TableTop(table.name) + .01f;
                GameObject bowl;
                if (detail != null && CreateMealBowl != null) bowl = CreateMealBowl(detail, spot);
                else
                {
                    Mesh bowlMesh = MeshFactory.Bowl();
                    bowl = StoreBuilder.Part("Dirty Bowl", bowlMesh, MaterialLibrary.Lit(new Color(.92f, .9f, .85f), .6f), spot, Vector3.one * StoreBuilder.Fit(bowlMesh, .26f), root);
                    StoreBuilder.Part("Leftover", MeshFactory.Disc(.09f, 14), MaterialLibrary.Lit(new Color(.55f, .25f, .12f), .7f), new Vector3(0, .03f, 0), Vector3.one / bowl.transform.localScale.x, bowl.transform, null, false);
                }
                bowl.transform.rotation = pair.Value.Facing;
                list.Add(bowl);
            }
            dirtyBowls[tableUid] = list;
        }

        /// <summary>テーブル天板の高さ。</summary>
        public static float TableTop(string defNameOrId) => defNameOrId.Contains("カウンター") || defNameOrId == "table_counter3" ? .64f : .54f;
    }

    /// <summary>上下にゆらゆら動く（水槽の魚など）。</summary>
    public sealed class Bobbing : MonoBehaviour
    {
        public float Speed = 1, Amount = .04f;
        private Vector3 origin;
        private void Start() { origin = transform.localPosition; }
        private void Update() { transform.localPosition = origin + new Vector3(Mathf.Sin(Time.time * Speed) * Amount * 2, Mathf.Sin(Time.time * Speed * 1.7f) * Amount, 0); }
    }
}
