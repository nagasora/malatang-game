using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>厨房の3D操作対象の種類。</summary>
    public enum KitchenTargetKind { Pot, Kettle, Jar, Scale, Basket, Piece, Bowl, Ingredient }

    /// <summary>厨房で直接タップ・ドラッグできる物の目印。</summary>
    public sealed class KitchenTarget : MonoBehaviour
    {
        public KitchenTargetKind Kind;
        public int Index;
        public KitchenTarget Set(KitchenTargetKind kind, int index) { Kind = kind; Index = index; return this; }
    }

    /// <summary>
    /// 厨房の寄り視点での調理表現と直接操作。
    /// ・具材は物理演算でボウルに落ち、毎回違う盛り付けになる
    /// ・茹で中の網じゃくの具材は茹で加減に応じて色と艶が変わり、湯気と泡が立つ
    /// ・湯切りは網じゃくを上下にドラッグ、スープは鍋を長押し、調味料は瓶をタップ
    /// </summary>
    public sealed class KitchenStation
    {
        private const float BowlSize = .44f, PieceScale = .8f;
        /// <summary>白い具材が映えるよう、器は淡い青磁色にする。</summary>
        public static readonly Color BowlGlaze = new Color(.78f, .87f, .9f);

        private readonly RestaurantWorld world;
        private readonly StoreBuilder store;
        private readonly Transform root;
        private readonly IngredientCatalog catalog;
        private CustomerOrder order;
        private Transform bowl, soupSurface, toppings;
        private readonly List<GameObject> bowlPieces = new List<GameObject>();
        private readonly KitchenDisplaySnapshot displaySnapshot;
        private readonly Transform[] baskets = new Transform[4];
        private readonly CustomerOrder[] basketOrders = new CustomerOrder[4];
        private readonly List<(string id, bool dropped)>[] basketContents = new List<(string, bool)>[4];
        private readonly float[] boilThresholds = new float[4];
        private readonly Transform[] potWater = new Transform[4];
        private readonly float[] potWaterLevels = new float[4];
        private float displayedSoupMl;
        private float soupBowlHeight;
        /// <summary>盛り付けの基準にするスープ量。これより多く注いだ分だけ具材を持ち上げる。</summary>
        private const float PlatingReferenceMl = 320f;
        private readonly List<PlatingInput> platingInputs = new List<PlatingInput>();
        private readonly List<PlatingPlacement> platingTargets = new List<PlatingPlacement>();
        private readonly List<(Transform piece, Vector3 position, Quaternion rotation, Vector3 scale)> platingStarts =
            new List<(Transform, Vector3, Quaternion, Vector3)>();
        private bool platingActive;
        private float platingBlend, appliedPlatingBlend = -1f, appliedPlatingLift = -1f, bowlBuiltTime;
        private readonly List<(int index, IngredientView view)>[] basketPieces = new List<(int, IngredientView)>[4];
        private readonly ParticleSystem[] steam = new ParticleSystem[4];
        private readonly ParticleSystem[] bubbles = new ParticleSystem[4];
        private readonly ParticleSystem[] smoke = new ParticleSystem[4];
        private Transform liftedBasket;
        private float shakeOffset, shakeVelocity;
        private bool pouring;
        private ParticleSystem pourStream;
        private Vector3 ladleHome;
        private Transform tongs, tongLeft, tongRight, soupTarget;
        private readonly Queue<(string id, int index)> tongDrops = new Queue<(string, int)>();
        private (string id, int index) tongDrop;
        private Vector3 tongStart;
        private float tongElapsed, tongDuration;
        private bool tongActive;
        private GameObject tongFood;
        private LineRenderer liquidStream;
        private string heldIngredient;
        private float ingredientHold;
        private Vector2 ingredientPress;
        private float overflowAt, paintAt;
        private string paintCondiment;
        private Transform livePaint;
        private int paintedCount;
        private readonly TextMesh[] potTimers = new TextMesh[4];
        private readonly int[] shownPotSeconds = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        private readonly bool[] potWasReady = new bool[4];
        private int pointerHeldKettle = -1;
        private float lastDragY;
        private int dragDirection;
        private bool draggingBasket;

        /// <summary>器の中に収まっている具材の数（自動テスト・診断用）。</summary>
        public int PiecesInBowl => bowl == null ? 0 : bowlPieces.Count(p => p != null
            && Vector3.Distance(new Vector3(p.transform.position.x, 0, p.transform.position.z), new Vector3(bowl.position.x, 0, bowl.position.z)) < BowlSize * .55f
            && p.transform.position.y > bowl.position.y - .03f);

        /// <summary>器と具材の位置関係（診断用）。</summary>
        public string DescribeBowl() => bowl == null ? "bowl=null" : "bowl=" + bowl.position.ToString("F3") + " pieces=" + bowlPieces.Count + " "
            + string.Join(" ", bowlPieces.Select(p => p == null ? "null" : p.name + (p.transform.position - bowl.position).ToString("F3") + (p.TryGetComponent<Rigidbody>(out var b) ? (b.isKinematic ? "K" : "D") : "-")));

        /// <summary>選択中のスープ（UI と 3D 操作で共有）。</summary>
        public string SelectedSoup = "";

        /// <summary>既存の鍋に共有エフェクトを取り付け、液面の参照を一度だけ取得します。</summary>
        public KitchenStation(RestaurantWorld world, Transform parent, StoreBuilder store, IngredientCatalog catalog)
        {
            this.world = world; this.store = store; this.catalog = catalog;
            displaySnapshot = new KitchenDisplaySnapshot(catalog);
            root = new GameObject("Kitchen Station").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < 4; i++)
            {
                basketPieces[i] = new List<(int, IngredientView)>();
                basketContents[i] = new List<(string, bool)>();
                var pot = store.PotRoots[i];
                potWater[i] = pot.Find("Water");
                Transform shell = pot.Find("Pot");
                MeshFilter shellMesh = shell != null ? shell.GetComponent<MeshFilter>() : null;
                float waterLevel = shellMesh != null && shellMesh.sharedMesh != null
                    ? shell.localPosition.y + shellMesh.sharedMesh.bounds.max.y * shell.localScale.y - .02f : .32f;
                potWaterLevels[i] = waterLevel;
                if (potWater[i] != null) potWater[i].localPosition = new Vector3(0, waterLevel, 0);
                steam[i] = EffectsFactory.Steam(pot, 0); steam[i].transform.localPosition = new Vector3(0, waterLevel + .02f, 0);
                bubbles[i] = EffectsFactory.Bubbles(pot, .24f); bubbles[i].transform.localPosition = new Vector3(0, waterLevel + .006f, 0);
                smoke[i] = null;
                potTimers[i] = store.Label("", new Vector3(0, .9f, 0), Quaternion.identity, .018f, UIKit.Gold, pot);
            }
            ladleHome = store.Ladle.position;
            tongs = new GameObject("Serving Tongs").transform; tongs.SetParent(root, false);
            Material steel = MaterialLibrary.Lit(new Color(.62f, .67f, .7f), .8f, .65f);
            tongLeft = StoreBuilder.Part("Left Jaw", StoreBuilder.RoundedBox(new Vector3(.025f, .27f, .035f), .008f), steel, new Vector3(-.035f, 0, 0), Vector3.one, tongs, null, false).transform;
            tongRight = StoreBuilder.Part("Right Jaw", StoreBuilder.RoundedBox(new Vector3(.025f, .27f, .035f), .008f), steel, new Vector3(.035f, 0, 0), Vector3.one, tongs, null, false).transform;
            StoreBuilder.Part("Spring", StoreBuilder.RoundedBox(new Vector3(.1f, .025f, .035f), .008f), steel, new Vector3(0, .13f, 0), Vector3.one, tongs, null, false);
            tongs.gameObject.SetActive(false);
            liquidStream = new GameObject("Soup Stream").AddComponent<LineRenderer>(); liquidStream.transform.SetParent(root, false);
            liquidStream.positionCount = 2; liquidStream.startWidth = .028f; liquidStream.endWidth = .018f;
            liquidStream.numCapVertices = 3; liquidStream.sharedMaterial = MaterialLibrary.Lit(new Color(.93f, .5f, .16f, .8f), .75f);
            liquidStream.enabled = false;
        }

        private RestaurantSimulation Sim => world.App.Simulation;

        /// <summary>カメラの注視点（工程に応じて移動）。</summary>
        public Vector3 Focus
        {
            get
            {
                if (order == null) return new Vector3(0, 1.1f, StoreLayout.BackCounterZ);
                switch (order.stage)
                {
                    case OrderStage.Selecting: return new Vector3(-3.1f, 1.05f, 4.5f);
                    case OrderStage.Weighed: return new Vector3(-1.2f, 1.05f, 4.6f);
                    case OrderStage.Heating: return StoreLayout.PotTop(Mathf.Max(0, order.pot)) + new Vector3(0, .15f, -.1f);
                    default: return StoreLayout.FinishBowl + new Vector3(.1f, .1f, .15f);
                }
            }
        }

        public void SetOrder(CustomerOrder value)
        {
            if (value?.id != order?.id) SelectedSoup = value?.chosenSoup ?? "";
            order = value;
        }

        // ───────── 表示の同期 ─────────

        /// <summary>構成の変化だけを検出し、定常フレームに署名文字列や候補リストを生成せず同期します。</summary>
        public void Update(float deltaTime, bool cookingMode)
        {
            var sim = Sim;
            for (int p = 0; p < 4; p++) UpdatePot(p, sim, deltaTime);
            if (!cookingMode) order = null;
            if (displaySnapshot.SynchronizeBowl(order)) RebuildBowlInternal();
            if (bowl != null && soupSurface != null && !MatchesToppings(order))
            {
                if (toppings != null) Object.Destroy(toppings.gameObject);
                toppings = null;
                BuildToppings();
                toppingContents.Clear();
                toppingContents.AddRange(order.condiments);
                toppingSpice = order.chosenSpice;
            }
            UpdateSoupSurface(deltaTime);
            UpdateCooler(sim);
            UpdateLiftedBasket(deltaTime);
            UpdateLadle(deltaTime);
            UpdateScaleDisplay();
            UpdateTongs(deltaTime);
            UpdatePourStream();
            UpdatePaintedGarnishes();
        }

        private const float GarnishReferenceRadius = .38f;
        private readonly List<string> toppingContents = new List<string>();
        private int toppingSpice = -1;

        /// <summary>保存済みの薬味列を逐次比較し、毎フレームの署名文字列生成を避けます。</summary>
        private bool MatchesToppings(CustomerOrder current)
        {
            if (current == null || current.chosenSpice != toppingSpice || current.condiments.Count != toppingContents.Count) return false;
            for (int index = 0; index < toppingContents.Count; index++)
                if (toppingContents[index] != current.condiments[index]) return false;
            return true;
        }
        private Transform cooler;

        /// <summary>厨房の冷蔵ケースに、解禁済みで在庫のある人気の具材を並べる（最大12種）。</summary>
        private void UpdateCooler(RestaurantSimulation sim)
        {
            if (!displaySnapshot.SynchronizeCooler(sim.Unlocks, sim.State.stock)) return;
            IReadOnlyList<IngredientDefinition> items = displaySnapshot.CoolerItems;
            if (cooler != null) Object.Destroy(cooler.gameObject);
            cooler = new GameObject("Cooler Display").transform;
            cooler.SetParent(root, false);
            var tray = MaterialLibrary.Lit(new Color(.9f, .91f, .9f), .7f, .4f);
            for (int i = 0; i < items.Count; i++)
            {
                int column = i % 6, row = i / 6;
                Vector3 center = new Vector3(-6.2f + column * .42f, StoreLayout.BackCounterHeight + .07f, 4.55f + row * .3f);
                var bin = StoreBuilder.Part("Tray", StoreBuilder.RoundedBox(new Vector3(.38f, .04f, .26f), .015f), tray, center, Vector3.one, cooler, null, false);
                var binCollider = bin.AddComponent<BoxCollider>(); binCollider.size = new Vector3(.38f, .14f, .26f); binCollider.center = Vector3.up * .05f;
                bin.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Ingredient, i);
                for (int k = 0; k < 2; k++)
                {
                    var piece = IngredientVisuals.Build(items[i], cooler, .55f);
                    piece.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Ingredient, i);
                    piece.transform.position = center + new Vector3(-.07f + k * .14f, .04f, (k % 2) * .03f);
                    piece.transform.rotation = Quaternion.Euler(0, (i * 47 + k * 90) % 360, 0);
                }
            }
        }

        /// <summary>注文を読み取り、投入構成が変わった時だけ網を再生成し、湯面・食材・泡を同期します。</summary>
        private void UpdatePot(int p, RestaurantSimulation sim, float deltaTime)
        {
            Transform pot = store.PotRoots[p];
            bool available = p < sim.State.pots;
            pot.gameObject.SetActive(available);
            if (!available) return;
            CustomerOrder heating = null;
            for (int i = 0; i < sim.State.orders.Count; i++)
            {
                CustomerOrder candidate = sim.State.orders[i];
                if (candidate.stage == OrderStage.Heating && candidate.pot == p) { heating = candidate; break; }
            }
            bool changedOrder = basketOrders[p] != heating;
            if (changedOrder || !MatchesContents(heating, basketContents[p]))
            {
                int previousCount = changedOrder ? 0 : basketPieces[p].Count;
                basketOrders[p] = heating;
                CaptureContents(heating, basketContents[p]);
                if (baskets[p] != null) Object.Destroy(baskets[p].gameObject);
                baskets[p] = null; basketPieces[p].Clear();
                if (heating != null)
                {
                    baskets[p] = BuildBasket(heating, pot, basketPieces[p], true);
                    float slowest = 0;
                    for (int i = 0; i < heating.selected.Count; i++)
                        slowest = Mathf.Max(slowest, catalog.Get(heating.selected[i]).cookingSeconds);
                    boilThresholds[p] = slowest * 2.2f + 15f;
                    if (basketPieces[p].Count > previousCount)
                        EffectsFactory.IngredientSplash(pot.TransformPoint(new Vector3(0, potWaterLevels[p] + .008f, 0)), sim.State.highQuality);
                }
            }
            bool open = sim.State.phase == DayPhase.Open || sim.State.phase == DayPhase.Closing;
            float progress = 0;
            if (heating != null && heating.selected.Count > 0)
            {
                for (int i = 0; i < heating.selected.Count; i++) progress += sim.Doneness(heating, i);
                progress /= heating.selected.Count;
            }
            CookingCue cue = CookingFeel.Cues(progress);
            float warning = heating == null ? 0 : Mathf.InverseLerp(boilThresholds[p] - 10f, boilThresholds[p], heating.heatSeconds);
            float quality = sim.State.highQuality ? 1f : .45f;
            ParticleSystem.MainModule steamMain = steam[p].main;
            ParticleSystem.MainModule bubbleMain = bubbles[p].main;
            int steamMaximum = sim.State.highQuality ? 48 : 24;
            int bubbleMaximum = sim.State.highQuality ? 48 : 20;
            if (steamMain.maxParticles != steamMaximum) steamMain.maxParticles = steamMaximum;
            if (bubbleMain.maxParticles != bubbleMaximum) bubbleMain.maxParticles = bubbleMaximum;
            EffectsFactory.SetIntensity(steam[p], quality * (open ? (heating != null ? cue.Steam + warning * .5f : .35f) : .1f));
            EffectsFactory.SetIntensity(bubbles[p], quality * (open ? (heating != null ? cue.Bubbles + warning * 1.65f : .3f) : 0));
            if (potWater[p] != null)
                potWater[p].localPosition = new Vector3(0, potWaterLevels[p] + (heating != null ? Mathf.Sin(Time.time * 5f + p) * .003f : 0), 0);
            Transform flame = pot.Find("Flame");
            if (flame != null) flame.gameObject.SetActive(open);
            if (potTimers[p] != null)
            {
                bool ready = heating != null && sim.SecondsUntilDone(heating) <= 0;
                bool show = heating != null && world.Mode == WorldMode.Overview;
                potTimers[p].gameObject.SetActive(show);
                if (show)
                {
                    int seconds = Mathf.CeilToInt(Mathf.Max(0, sim.SecondsUntilDone(heating)));
                    if (seconds != shownPotSeconds[p]) { shownPotSeconds[p] = seconds; potTimers[p].text = sim.State.gaugeMode == 2 ? "♨" : ready ? "✓" : seconds + "秒"; }
                    potTimers[p].transform.rotation = world.View.transform.rotation;
                }
                if (ready && !potWasReady[p]) world.App.Audio.Play(Sfx.ScaleBeep, .3f);
                potWasReady[p] = ready;
            }
            if (heating != null)
            {
                if (baskets[p] != null) baskets[p].localPosition = new Vector3(0, potWaterLevels[p] - .175f + Mathf.Sin(Time.time * 2.4f + p) * .004f, 0);
                foreach ((int index, IngredientView view) piece in basketPieces[p])
                {
                    if (piece.view == null || piece.index >= heating.selected.Count) continue;
                    piece.view.SetDoneness(sim.Doneness(heating, piece.index));
                    float phase = Time.time * 2.8f + piece.index * 1.7f;
                    float angle = piece.index * 2.39f;
                    float radius = .035f + piece.index % 3 * .028f;
                    piece.view.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius + Mathf.Sin(phase) * .006f,
                        piece.index % 2 * .012f + Mathf.Sin(phase * .8f) * .006f, Mathf.Sin(angle) * radius + Mathf.Cos(phase) * .006f);
                    piece.view.transform.localRotation = Quaternion.Euler(Mathf.Sin(phase) * 8f, piece.index * 137f, Mathf.Cos(phase * .7f) * 7f);
                }
                if (heating.boiledOver && smoke[p] == null) { smoke[p] = EffectsFactory.Smoke(pot); smoke[p].transform.localPosition = new Vector3(0, potWaterLevels[p] + .04f, 0); }
                if (smoke[p] != null)
                {
                    ParticleSystem.MainModule smokeMain = smoke[p].main;
                    smokeMain.maxParticles = sim.State.highQuality ? 40 : 16;
                    EffectsFactory.SetIntensity(smoke[p], quality);
                }
            }
            if ((heating == null || !heating.boiledOver) && smoke[p] != null) { Object.Destroy(smoke[p].gameObject); smoke[p] = null; }
        }

        /// <summary>ハッシュ衝突や文字列生成を避け、食材IDと投入フラグを直接比較します。</summary>
        private static bool MatchesContents(CustomerOrder value, List<(string id, bool dropped)> snapshot)
        {
            if (value == null) return snapshot.Count == 0;
            if (value.selected.Count != snapshot.Count) return false;
            for (int i = 0; i < snapshot.Count; i++)
            {
                bool dropped = i < value.dropTimes.Count && value.dropTimes[i] >= 0;
                if (snapshot[i].id != value.selected[i] || snapshot[i].dropped != dropped) return false;
            }
            return true;
        }

        /// <summary>構成が変わった時だけ再利用リストへ食材IDと投入状態を保存します。</summary>
        private static void CaptureContents(CustomerOrder value, List<(string id, bool dropped)> snapshot)
        {
            snapshot.Clear();
            if (value == null) return;
            for (int i = 0; i < value.selected.Count; i++)
                snapshot.Add((value.selected[i], i < value.dropTimes.Count && value.dropTimes[i] >= 0));
        }

        /// <summary>柄を含む全体幅では縮尺を決めず、網の開口を湯面に合わせて中身を網の直下から見せます。</summary>
        private Transform BuildBasket(CustomerOrder o, Transform parent, List<(int, IngredientView)> pieces, bool onlyDropped)
        {
            Mesh strainerMesh = MeshFactory.Strainer();
            float basketHeight = onlyDropped ? potWaterLevels[Mathf.Clamp(o.pot, 0, 3)] - .175f : .16f;
            Transform basket = StoreBuilder.Part("Strainer", strainerMesh, MaterialLibrary.Lit(StoreBuilder.Steel, .85f, .85f),
                new Vector3(0, basketHeight, 0), Vector3.one * .95f, parent).transform;
            Transform holder = new GameObject("Contents").transform;
            holder.SetParent(basket, false);
            holder.localPosition = new Vector3(0, .175f / .95f, 0);
            holder.localScale = Vector3.one / .95f;
            for (int i = 0; i < o.selected.Count; i++)
            {
                if (onlyDropped && (i >= o.dropTimes.Count || o.dropTimes[i] < 0)) continue;
                IngredientDefinition item = catalog.Get(o.selected[i]);
                GameObject piece = IngredientVisuals.Build(item, holder, PieceScale);
                BoxCollider collider = piece.GetComponent<BoxCollider>();
                if (collider != null)
                {
                    float span = Mathf.Max(collider.size.x, collider.size.z);
                    float fit = Mathf.Min(PieceScale, .16f / Mathf.Max(.001f, span));
                    piece.transform.localScale = Vector3.one * fit;
                    // 網の中身は視覚専用であり、独立した操作コライダーを残すと鍋へのタップを遮る。
                    collider.enabled = false;
                }
                float angle = i * 2.39f, radius = .035f + i % 3 * .028f;
                piece.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, i % 2 * .012f, Mathf.Sin(angle) * radius);
                piece.transform.localRotation = Quaternion.Euler(0, i * 137f, 0);
                IngredientView view = piece.GetComponent<IngredientView>();
                view.SetWet(1);
                pieces.Add((i, view));
            }
            basket.gameObject.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Basket, o.pot);
            SphereCollider target = basket.gameObject.AddComponent<SphereCollider>(); target.radius = .23f; target.center = new Vector3(0, .13f, 0);
            return basket;
        }

        /// <summary>選択中の注文のボウル表示を作り直す。</summary>
        public void RebuildBowl() => displaySnapshot.InvalidateBowl();

        private void ClearBowl()
        {
            tongDrops.Clear(); tongActive = false; if (tongFood != null) Object.Destroy(tongFood); if (tongs != null) tongs.gameObject.SetActive(false);
            foreach (var p in bowlPieces) if (p != null) Object.Destroy(p);
            bowlPieces.Clear();
            if (bowl != null) Object.Destroy(bowl.gameObject);
            if (liftedBasket != null) Object.Destroy(liftedBasket.gameObject);
            if (toppings != null) Object.Destroy(toppings.gameObject);
            bowl = null; soupSurface = null; toppings = null; liftedBasket = null;
            toppingContents.Clear(); toppingSpice = -1;
            soupTarget = null; livePaint = null; paintedCount = 0;
            displayedSoupMl = 0;
            platingActive = false; platingBlend = 0f; appliedPlatingBlend = -1f; appliedPlatingLift = -1f;
            platingInputs.Clear(); platingTargets.Clear(); platingStarts.Clear();
        }

        private void RebuildBowlInternal()
        {
            ClearBowl();
            if (order == null) return;
            switch (order.stage)
            {
                case OrderStage.Selecting:
                    CreateWorkBowl(StoreLayout.PrepBowl);
                    for (int i = 0; i < order.selected.Count; i++) DropPiece(order.selected[i], i, false, i);
                    break;
                case OrderStage.Weighed:
                    CreateWorkBowl(StoreLayout.ScaleTop + new Vector3(0, .09f, 0));
                    for (int i = 0; i < order.selected.Count; i++) DropPiece(order.selected[i], i, false, i);
                    break;
                case OrderStage.Heating:
                    CreateWorkBowl(StoreLayout.PrepBowl);
                    for (int i = 0, n = 0; i < order.selected.Count; i++) if (i >= order.dropTimes.Count || order.dropTimes[i] < 0) DropPiece(order.selected[i], i, false, n++);
                    break;
                case OrderStage.Drained:
                    CreateWorkBowl(StoreLayout.FinishBowl);
                    var lift = new GameObject("Lifted").transform; lift.SetParent(root, false); lift.position = StoreLayout.FinishBowl + new Vector3(0, .38f, 0);
                    var temp = new List<(int, IngredientView)>();
                    liftedBasket = BuildBasket(order, lift, temp, false);
                    liftedBasket.SetParent(lift, true);
                    foreach (var (index, view) in temp) if (view != null) { view.SetDoneness(Sim.Doneness(order, index)); view.SetWet(1); }
                    liftedBasket = lift;
                    break;
                default:
                    CreateWorkBowl(StoreLayout.FinishBowl);
                    for (int i = 0; i < order.selected.Count; i++) DropPiece(order.selected[i], i, true, i);
                    BuildSoupSurface();
                    break;
            }
        }

        private void CreateWorkBowl(Vector3 position)
        {
            var bowlMesh = MeshFactory.Bowl();
            var go = StoreBuilder.Part("Work Bowl", bowlMesh, MaterialLibrary.Lit(BowlGlaze, .8f), position, Vector3.one * StoreBuilder.Fit(bowlMesh, BowlSize), root);
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = bowlMesh;
            AddCatchColliders(go.transform);
            go.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Bowl, 0);
            var rim = StoreBuilder.Part("Rim", MeshFactory.Torus(.5f, .025f, 32, 6), MaterialLibrary.Lit(new Color(.75f, .2f, .12f), .6f), new Vector3(0, bowlMesh.bounds.max.y * .97f, 0), Vector3.one, go.transform, null, false);
            rim.transform.localScale = new Vector3(1, 1, 1) * (bowlMesh.bounds.size.x / 1.02f);
            bowl = go.transform;
            bowlBuiltTime = Time.time;
        }

        /// <summary>
        /// 器の内側に厚みのある受け止め用コライダーを並べる。
        /// 器のメッシュは壁が薄く、小さな具材が高速で当たると突き抜けるため、メッシュコライダーだけに頼らない。
        /// </summary>
        private static void AddCatchColliders(Transform bowlTransform)
        {
            var catcher = new GameObject("Catch Colliders").transform;
            catcher.SetParent(bowlTransform, false);
            var bottom = new GameObject("Bottom").AddComponent<BoxCollider>();
            bottom.transform.SetParent(catcher, false);
            bottom.center = new Vector3(0, .02f, 0); bottom.size = new Vector3(.5f, .1f, .5f);
            for (int i = 0; i < 10; i++)
            {
                var wall = new GameObject("Wall").AddComponent<BoxCollider>();
                wall.transform.SetParent(catcher, false);
                float angle = i * 36f;
                wall.transform.localRotation = Quaternion.Euler(0, angle, 0) * Quaternion.Euler(38, 0, 0);
                wall.transform.localPosition = Quaternion.Euler(0, angle, 0) * new Vector3(0, .22f, .42f);
                wall.size = new Vector3(.3f, .38f, .1f);
            }
        }

        private void DropPiece(string ingredientId, int index, bool cooked, int order = 0)
        {
            if (bowl == null) return;
            var item = catalog.Get(ingredientId);
            float volume = Mathf.Clamp(Mathf.Pow(item.portionGrams / 35f, 1f / 3f) * 1.25f, 1, 1.5f);
            var piece = IngredientVisuals.Build(item, root, PieceScale * volume * Random.Range(.95f, 1.05f), true);
            float angle = index * 2.39f + Random.Range(-.3f, .3f), radius = Random.Range(.01f, .05f);
            // 先に入った具材と重なった位置に出すと、物理演算の押し戻しで器の外へ弾き飛ばされるため、縁より上から順に落とす。
            Vector3 spawn = bowl.position + new Vector3(Mathf.Cos(angle) * radius, .32f + order * .09f, Mathf.Sin(angle) * radius);
            Quaternion spin = Quaternion.Euler(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360));
            piece.transform.SetPositionAndRotation(spawn, spin);
            if (piece.TryGetComponent<Rigidbody>(out var body))
            {
                // 補間つきの剛体は Transform を書き換えても剛体側の位置で上書きされるため、剛体の姿勢も直接設定する。
                body.position = spawn; body.rotation = spin;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.maxLinearVelocity = 2.2f;
                body.maxDepenetrationVelocity = .4f;
                body.linearDamping = .4f; body.angularDamping = .6f;
            }
            var view = piece.GetComponent<IngredientView>();
            if (cooked && view != null) { view.SetDoneness(Sim.Doneness(this.order, index)); view.SetWet(.8f); }
            piece.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Piece, index);
            piece.AddComponent<SettleBody>();
            bowlPieces.Add(piece);
        }

        /// <summary>液面の共有素材と描画プロパティを準備し、注ぎ始めは空の位置から表示します。</summary>
        private void BuildSoupSurface()
        {
            if (bowl == null) return;
            string soupId = string.IsNullOrEmpty(order?.chosenSoup) ? "mala" : order.chosenSoup;
            GameObject go = SoupVisuals.CreateSurface(bowl, soupId, order?.chosenSpice ?? 0, .5f, 0f);
            soupSurface = go.transform;
            soupBowlHeight = MeshFactory.Bowl().bounds.size.y;
            displayedSoupMl = order != null && order.stage == OrderStage.Ready ? order.soupMl : 0;
            UpdateSoupSurface(0);
        }

        /// <summary>状態のスープ量には書き込まず、表示量だけを追従させ、油膜と薬味を液面に載せます。</summary>
        private void UpdateSoupSurface(float deltaTime)
        {
            if (soupSurface == null || order == null) return;
            string soupId = string.IsNullOrEmpty(order.chosenSoup) ? "mala" : order.chosenSoup;
            SoupVisuals.ConfigureSurface(soupSurface, soupId, order.chosenSpice);
            displayedSoupMl = Mathf.MoveTowards(displayedSoupMl, order.soupMl, Mathf.Max(0, deltaTime) * 300f);
            float visualMl = VisualSoupMl;
            soupSurface.gameObject.SetActive(visualMl > 5);
            float height = SoupHeight(visualMl);
            float radius = (BowlPlating.InnerRadius(height) - .008f) * (soupId == "dry" ? .74f : 1f);
            soupSurface.localPosition = new Vector3(0, height, 0);
            float width = radius * 2f;
            soupSurface.localScale = new Vector3(width, 1, width);
            if (toppings != null)
            {
                toppings.gameObject.SetActive(visualMl > 5);
                toppings.localPosition = new Vector3(0f, height + .006f, 0f);
                toppings.localScale = Vector3.one * (radius / GarnishReferenceRadius);
            }
            UpdatePlating(deltaTime);
        }

        /// <summary>汁なしは実スープ量を増やさず、和えだれと完成盛り付けの表示基準だけを使います。</summary>
        private float VisualSoupMl => order != null && order.chosenSoup == "dry"
            ? Mathf.Max(displayedSoupMl, PlatingReferenceMl) : displayedSoupMl;

        /// <summary>表示スープ量に対する器メッシュのローカル液面高さ。</summary>
        private float SoupHeight(float milliliters) => Mathf.Lerp(.08f, soupBowlHeight * .89f, Mathf.Clamp01(milliliters / 450f));

        /// <summary>
        /// スープが注がれたら、物理演算で沈んだ具材を盛り付け姿勢へ持ち上げます。
        /// 具材が落ち着くまで（完成済みの器は即座に）待ち、注いだ量に応じて滑らかに移し、変化がないフレームは何もしません。
        /// </summary>
        private void UpdatePlating(float deltaTime)
        {
            if (bowl == null || order == null || bowlPieces.Count == 0) return;
            bool ready = order.stage == OrderStage.Ready;
            if (!platingActive)
            {
                if (VisualSoupMl <= 5f || (!ready && !PiecesSettled())) return;
                BeginPlating();
                platingBlend = ready ? 1f : 0f;
            }
            float target = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(VisualSoupMl / 220f));
            platingBlend = Mathf.MoveTowards(platingBlend, target, Mathf.Max(0f, deltaTime) * 2.5f);
            float lift = SoupHeight(Mathf.Max(VisualSoupMl, PlatingReferenceMl)) - SoupHeight(PlatingReferenceMl);
            if (Mathf.Approximately(platingBlend, appliedPlatingBlend) && Mathf.Approximately(lift, appliedPlatingLift)) return;
            appliedPlatingBlend = platingBlend; appliedPlatingLift = lift;
            Quaternion bowlRotation = bowl.rotation;
            for (int i = 0; i < platingStarts.Count; i++)
            {
                (Transform piece, Vector3 position, Quaternion rotation, Vector3 scale) start = platingStarts[i];
                if (start.piece == null) continue;
                PlatingPlacement placement = platingTargets[i];
                Vector3 position = bowl.TransformPoint(placement.Position + new Vector3(0f, lift, 0f));
                start.piece.SetPositionAndRotation(Vector3.Lerp(start.position, position, platingBlend),
                    Quaternion.Slerp(start.rotation, bowlRotation * placement.Rotation, platingBlend));
                start.piece.localScale = start.scale * Mathf.Lerp(1f, placement.Scale, platingBlend);
                var soup = Content.Soup(string.IsNullOrEmpty(order.chosenSoup) ? order.soup : order.chosenSoup);
                start.piece.GetComponent<IngredientView>()?.SetSoupLevel(bowl.TransformPoint(Vector3.up * SoupHeight(VisualSoupMl)).y, new Color(soup.R, soup.G, soup.B));
            }
        }

        /// <summary>落とした具材がすべて静止したか、落下が終わる程度（約1秒）の時間が経ったかを返します。</summary>
        private bool PiecesSettled()
        {
            if (Time.time - bowlBuiltTime > 1f) return true;
            foreach (GameObject piece in bowlPieces)
                if (piece != null && piece.TryGetComponent(out Rigidbody body) && !body.isKinematic) return false;
            return true;
        }

        /// <summary>具材の物理演算を止めて現在の姿勢を保存し、盛り付け先を一度だけ計算します。</summary>
        private void BeginPlating()
        {
            platingActive = true;
            platingInputs.Clear(); platingStarts.Clear();
            float bowlScale = Mathf.Max(.0001f, bowl.lossyScale.x);
            foreach (GameObject piece in bowlPieces)
            {
                if (piece == null) continue;
                if (piece.TryGetComponent(out SettleBody settle)) Object.Destroy(settle);
                if (piece.TryGetComponent(out Rigidbody body))
                {
                    // 補間を残すと、Transform で動かした盛り付け位置が剛体側の補間姿勢で上書きされる。
                    body.interpolation = RigidbodyInterpolation.None;
                    body.isKinematic = true;
                }
                int index = piece.TryGetComponent(out KitchenTarget mark) ? mark.Index : -1;
                string category = index >= 0 && index < order.selected.Count ? catalog.Get(order.selected[index]).category : "";
                platingInputs.Add(new PlatingInput { Category = category, LocalBounds = LocalBounds(piece), RelativeScale = piece.transform.lossyScale.x / bowlScale, WeightGrams = index >= 0 && index < order.selected.Count ? catalog.Get(order.selected[index]).portionGrams : 0 });
                platingStarts.Add((piece.transform, piece.transform.position, piece.transform.rotation, piece.transform.localScale));
            }
            BowlPlating.Arrange(platingInputs, SoupHeight(PlatingReferenceMl), CustomerDirector.StableHash(order.id), platingTargets);
        }

        /// <summary>具材ルートのローカル外形を BoxCollider から得ます（無ければ標準寸法）。</summary>
        private static Bounds LocalBounds(GameObject piece) =>
            piece.TryGetComponent(out BoxCollider box) ? new Bounds(box.center, box.size) : new Bounds(Vector3.zero, Vector3.one * .1f);

        /// <summary>定義に対応した薬味を、器のローカル空間に固定シードで結合します。</summary>
        private void BuildToppings()
        {
            if (bowl == null || order == null) return;
            toppings = SoupVisuals.BuildGarnishes(bowl, order.condiments, order.chosenSpice, CustomerDirector.StableHash(order.id), GarnishReferenceRadius, order.stage == OrderStage.Ready ? order.garnishStrokes : null);
            toppings.localPosition = Vector3.up * (SoupHeight(displayedSoupMl) + .006f);
        }

        // ───────── 出来事への反応 ─────────

        public void OnIngredientAdded(string ingredientId)
        {
            if (order == null || order.stage != OrderStage.Selecting || bowl == null) return;
            tongDrops.Enqueue((ingredientId, order.selected.Count - 1));
            displaySnapshot.CaptureBowl(order);
        }

        /// <summary>水しぶきは投入状態の同期で一度だけ生成し、手動投入とスタッフ投入の二重放出を避けます。</summary>
        public void OnDropped(int index) { }

        public void OnShake()
        {
            shakeVelocity += 2.6f;
            if (liftedBasket != null) EffectsFactory.Splash(liftedBasket.position + Vector3.down * .15f);
        }

        public void OnCondiment(string id)
        {
            if (bowl != null) EffectsFactory.Sparkles(bowl.position + Vector3.up * .3f, id == Content.ChiliOil ? new Color(1, .3f, .1f) : Color.white);
        }

        public void OnFinished(long quality)
        {
            if (bowl == null) return;
            EffectsFactory.Sparkles(bowl.position + Vector3.up * .35f, new Color(1f, .85f, .4f));
            if (quality >= 90) EffectsFactory.Confetti(bowl.position + Vector3.up * .6f);
            world.App.UI.FloatText(bowl.position + Vector3.up * .5f, UIKit.Stars(quality) + " " + quality + "点", quality >= 80 ? UIKit.Gold : quality >= 60 ? Color.white : UIKit.Bad, 30);
        }

        public void OnBoilOver(int pot) { }

        public void SetPouring(bool value)
        {
            if (pouring == value) return;
            pouring = value;
            if (pouring && pourStream == null && bowl != null)
            {
                pourStream = EffectsFactory.Splash(bowl.position + Vector3.up * .25f);
                world.App.Audio.Play(Sfx.Pour, .8f);
            }
            if (!pouring) pourStream = null;
        }

        private void UpdatePaintedGarnishes()
        {
            if (bowl == null || order == null || order.stage != OrderStage.Finishing) return;
            if (livePaint == null) { livePaint = new GameObject("Painted Garnish Trail").transform; livePaint.SetParent(bowl, false); }
            while (paintedCount < order.garnishStrokes.Count)
                SoupVisuals.AppendPaintedStroke(livePaint, order.garnishStrokes[paintedCount++], GarnishReferenceRadius);
            livePaint.localPosition = Vector3.up * (SoupHeight(displayedSoupMl) + .008f);
        }

        private void UpdateTongs(float dt)
        {
            if (order == null || order.stage != OrderStage.Selecting || bowl == null) { tongDrops.Clear(); tongActive = false; tongs.gameObject.SetActive(false); return; }
            if (!tongActive && tongDrops.Count > 0)
            {
                tongDrop = tongDrops.Dequeue();
                if (tongDrop.index >= order.selected.Count || order.selected[tongDrop.index] != tongDrop.id) return;
                tongStart = new Vector3(-5.2f, StoreLayout.BackCounterHeight + .3f, 4.7f);
                for (int i = 0; i < displaySnapshot.CoolerItems.Count; i++)
                    if (displaySnapshot.CoolerItems[i].id == tongDrop.id) tongStart = new Vector3(-6.2f + i % 6 * .42f, StoreLayout.BackCounterHeight + .3f, 4.55f + i / 6 * .3f);
                if (tongFood != null) Object.Destroy(tongFood);
                tongFood = IngredientVisuals.Build(catalog.Get(tongDrop.id), tongs, .35f);
                tongFood.transform.localPosition = Vector3.down * .13f;
                tongDuration = tongDrops.Count > 0 ? .14f : .3f; tongElapsed = 0; tongActive = true; tongs.gameObject.SetActive(true);
            }
            if (!tongActive) return;
            tongElapsed += dt;
            float progress = Mathf.Clamp01(tongElapsed / tongDuration);
            tongs.position = Vector3.Lerp(tongStart, bowl.position + Vector3.up * .42f, Mathf.SmoothStep(0, 1, progress)) + Vector3.up * Mathf.Sin(progress * Mathf.PI) * .2f;
            tongs.rotation = Quaternion.Euler(15, 0, -15);
            float open = progress < .15f || progress > .8f ? 12 : 3;
            tongLeft.localRotation = Quaternion.Euler(0, 0, -open); tongRight.localRotation = Quaternion.Euler(0, 0, open);
            if (progress < 1) return;
            if (tongFood != null) Object.Destroy(tongFood);
            DropPiece(tongDrop.id, tongDrop.index, false);
            world.App.Audio.Play(Sfx.IngredientDrop, .35f);
            tongActive = false; tongs.gameObject.SetActive(false);
        }

        private void UpdatePourStream()
        {
            bool show = pouring && bowl != null && order != null;
            liquidStream.enabled = show;
            if (show)
            {
                liquidStream.SetPosition(0, store.Ladle.position + Vector3.down * .03f);
                liquidStream.SetPosition(1, bowl.TransformPoint(Vector3.up * (SoupHeight(displayedSoupMl) + .02f)));
                var soup = Content.Soup(string.IsNullOrEmpty(order.chosenSoup) ? order.soup : order.chosenSoup);
                if (order.soupMl > soup.IdealMaxMl && Time.time >= overflowAt)
                { EffectsFactory.Splash(bowl.position + Vector3.right * .12f); overflowAt = Time.time + .6f; }
            }
            if (order == null || bowl == null || (order.stage != OrderStage.Drained && order.stage != OrderStage.Finishing)) return;
            if (soupTarget == null)
                soupTarget = StoreBuilder.Part("Ideal Soup Level", MeshFactory.Torus(.34f, .004f, 48, 4), MaterialLibrary.Emissive(UIKit.Gold, UIKit.Gold, .4f), Vector3.zero, Vector3.one, bowl, null, false).transform;
            var targetSoup = Content.Soup(string.IsNullOrEmpty(order.chosenSoup) ? order.soup : order.chosenSoup);
            soupTarget.localPosition = Vector3.up * SoupHeight((targetSoup.IdealMinMl + targetSoup.IdealMaxMl) * .5f);
        }

        private void UpdateLiftedBasket(float deltaTime)
        {
            if (liftedBasket == null) return;
            shakeVelocity -= shakeOffset * 120 * deltaTime;
            shakeVelocity *= Mathf.Exp(-6 * deltaTime);
            shakeOffset += shakeVelocity * deltaTime;
            liftedBasket.position = StoreLayout.FinishBowl + new Vector3(0, .38f + shakeOffset * .12f, 0);
        }

        private void UpdateLadle(float deltaTime)
        {
            if (store.Ladle == null) return;
            Vector3 target = ladleHome;
            Quaternion rotation = Quaternion.identity;
            if (pouring && bowl != null) { target = bowl.position + new Vector3(.12f, .42f, 0); rotation = Quaternion.Euler(0, 0, 55); }
            store.Ladle.position = Vector3.Lerp(store.Ladle.position, target, deltaTime * 10);
            store.Ladle.rotation = Quaternion.Slerp(store.Ladle.rotation, rotation, deltaTime * 10);
        }

        private TextMesh scaleText;
        private int displayedScaleGrams = int.MinValue;

        /// <summary>表示する整数重量が変わった時だけ文字列を更新します。</summary>
        private void UpdateScaleDisplay()
        {
            if (scaleText == null)
            {
                scaleText = store.Label("0g", new Vector3(0, .16f, -.2f), Quaternion.Euler(35, 0, 0), .008f, new Color(.3f, 1f, .5f), store.ScaleRoot);
                scaleText.name = "Scale Display";
            }
            float grams = order != null && order.stage == OrderStage.Weighed ? order.weighedGrams : 0;
            int roundedGrams = Mathf.RoundToInt(grams);
            if (roundedGrams == displayedScaleGrams) return;
            displayedScaleGrams = roundedGrams;
            scaleText.text = roundedGrams.ToString() + "g";
        }

        /// <summary>席や提供台に置く完成品の器（物理演算なし）。</summary>
        public GameObject CreateServedBowl(CustomerOrder o, Vector3 position)
        {
            var bowlMesh = MeshFactory.Bowl();
            var go = StoreBuilder.Part("Served Bowl " + o.id, bowlMesh, MaterialLibrary.Lit(BowlGlaze, .8f), position, Vector3.one * StoreBuilder.Fit(bowlMesh, ServedSize), root);
            var soup = Content.Soup(string.IsNullOrEmpty(o.chosenSoup) ? o.soup : o.chosenSoup);
            float h = bowlMesh.bounds.size.y;
            float surfaceRadius = soup.Id == "dry" ? .32f : .44f;
            GameObject surface = SoupVisuals.CreateSurface(go.transform, soup.Id, o.chosenSpice, surfaceRadius, h * .72f, true, false);
            Transform garnishes = SoupVisuals.BuildGarnishes(go.transform, o.condiments, o.chosenSpice, CustomerDirector.StableHash(o.id), surfaceRadius, o.garnishStrokes);
            garnishes.localPosition = Vector3.up * (h * .72f + .006f);
            PlateServedPieces(o, go.transform, h * .72f, surface.transform, garnishes);
            var steamFx = EffectsFactory.Steam(go.transform, o.mealFinished ? 0f : SoupAppearance.ForSoup(soup.Id, o.chosenSpice).SteamIntensity);
            steamFx.transform.localPosition = new Vector3(0, h * .74f, 0);
            steamFx.gameObject.AddComponent<BowlSteam>().Initialize(steamFx, o, Sim.State);
            return go;
        }

        /// <summary>完成品の器に、厨房と同じ盛り付け計算で具材を並べます。</summary>
        private void PlateServedPieces(CustomerOrder o, Transform servedBowl, float soupHeight, Transform surface, Transform garnishes)
        {
            int count = o.selected.Count;
            float localScale = PieceScale * ServedPieceBoost * (ServedSize / BowlSize) / Mathf.Max(.0001f, servedBowl.localScale.x);
            servedInputs.Clear(); servedPieces.Clear();
            for (int i = 0; i < count; i++)
            {
                IngredientDefinition item = catalog.Get(o.selected[i]);
                GameObject piece = IngredientVisuals.Build(item, servedBowl, localScale);
                // 器の具材は見た目専用で、席や提供台へのタップを遮らないようコライダーは止める。
                if (piece.TryGetComponent(out BoxCollider box)) box.enabled = false;
                piece.GetComponent<IngredientView>()?.SetDoneness(1);
                servedInputs.Add(new PlatingInput { Category = item.category, LocalBounds = LocalBounds(piece), RelativeScale = localScale, WeightGrams = item.portionGrams });
                servedPieces.Add(piece.transform);
            }
            BowlPlating.Arrange(servedInputs, soupHeight, CustomerDirector.StableHash(o.id), servedPlacements);
            for (int i = 0; i < servedPieces.Count; i++)
            {
                PlatingPlacement placement = servedPlacements[i];
                servedPieces[i].SetLocalPositionAndRotation(placement.Position, placement.Rotation);
                servedPieces[i].localScale = Vector3.one * (localScale * placement.Scale);
                var view = servedPieces[i].GetComponent<IngredientView>();
                var soup = Content.Soup(string.IsNullOrEmpty(o.chosenSoup) ? o.soup : o.chosenSoup);
                view.SetWet(1); view.SetSoupLevel(surface.position.y, new Color(soup.R, soup.G, soup.B));
            }
            MealBowlView meal = servedBowl.gameObject.AddComponent<MealBowlView>();
            meal.Initialize(servedPieces, o.mealFinished ? o.leftovers : MealLeftovers.Evaluate(o, catalog), surface, garnishes, soupHeight / .72f);
            if (o.mealFinished) meal.ApplyProgress(1f, 0f, false);
            servedPieces.Clear();
        }

        /// <summary>片付け前の記録から、元の盛り付け位置で残った具材を復元します。</summary>
        public GameObject CreateLeftoverBowl(MealRemains remains, Vector3 position)
        {
            CustomerOrder order = new CustomerOrder { id = remains.orderId, soup = remains.soup, chosenSoup = remains.soup,
                chosenSpice = remains.spice, selected = remains.selected, condiments = remains.condiments, garnishStrokes = remains.garnishStrokes,
                mealFinished = true, leftovers = remains.leftovers };
            GameObject bowl = CreateServedBowl(order, position);
            bowl.name = "Leftover Bowl " + remains.orderId;
            return bowl;
        }

        private const float ServedSize = .3f;
        /// <summary>引きの店内視点でも具材が判別できるよう、客席の器では具材を厨房より一回り大きく盛る。</summary>
        private const float ServedPieceBoost = 1.35f;
        private const int MaxServedPieces = 10;
        private readonly List<PlatingInput> servedInputs = new List<PlatingInput>();
        private readonly List<PlatingPlacement> servedPlacements = new List<PlatingPlacement>();
        private readonly List<Transform> servedPieces = new List<Transform>();

        // ───────── 直接操作 ─────────

        /// <summary>寄り視点での指・マウスの操作を処理する。操作対象に当たったら true。</summary>
        public bool HandlePointer(Camera camera, Vector2 position, bool pressed, bool held, bool released)
        {
            var o = order;
            if (released) { pointerHeldKettle = -1; draggingBasket = false; heldIngredient = null; paintCondiment = null; }
            if (held && heldIngredient != null && o != null && o.stage == OrderStage.Selecting)
            {
                if ((position - ingredientPress).sqrMagnitude > 900) heldIngredient = null;
                else
                {
                    ingredientHold += Time.deltaTime;
                    if (ingredientHold >= .45f) { ingredientHold = .25f; Sim.AddIngredient(o.id, heldIngredient); }
                    return true;
                }
            }
            if (held && paintCondiment != null && bowl != null && o != null && o.stage == OrderStage.Finishing)
            {
                Ray ray = camera.ScreenPointToRay(position);
                Plane plane = new Plane(Vector3.up, bowl.TransformPoint(Vector3.up * SoupHeight(displayedSoupMl)));
                if (Time.unscaledTime >= paintAt && plane.Raycast(ray, out float distance))
                {
                    Vector3 local = bowl.InverseTransformPoint(ray.GetPoint(distance));
                    if (local.x * local.x + local.z * local.z <= GarnishReferenceRadius * GarnishReferenceRadius)
                    {
                        if (Sim.State.highQuality || o.garnishStrokes.Count < 32) Sim.PaintCondiment(o.id, paintCondiment, local.x / GarnishReferenceRadius, local.z / GarnishReferenceRadius);
                        paintAt = Time.unscaledTime + .075f;
                    }
                }
                return true;
            }
            if (held && pointerHeldKettle >= 0 && o != null && !string.IsNullOrEmpty(SelectedSoup))
            {
                Sim.PourSoup(o.id, SelectedSoup, 150f * Time.deltaTime);
                SetPouring(true);
                return true;
            }
            if (held && draggingBasket && o != null && o.stage == OrderStage.Drained)
            {
                float dy = position.y - lastDragY;
                if (Mathf.Abs(dy) > 14)
                {
                    int dir = dy > 0 ? 1 : -1;
                    if (dir != dragDirection && dragDirection != 0 && dir < 0) Sim.ShakeByHand(o.id, Time.unscaledTime);
                    dragDirection = dir; lastDragY = position.y;
                }
                return true;
            }
            if (!pressed) return false;
            if (!Physics.Raycast(camera.ScreenPointToRay(position), out var hit, 30)) return false;
            var target = hit.collider.GetComponentInParent<KitchenTarget>();
            if (target == null) return false;
            var sim = Sim;
            switch (target.Kind)
            {
                case KitchenTargetKind.Ingredient:
                    if (o == null || o.stage != OrderStage.Selecting || target.Index < 0 || target.Index >= displaySnapshot.CoolerItems.Count) return true;
                    var ingredient = displaySnapshot.CoolerItems[target.Index];
                    sim.AddIngredient(o.id, ingredient.id);
                    if (ingredient.category == "vegetable" || ingredient.category == "noodle") { heldIngredient = ingredient.id; ingredientHold = 0; ingredientPress = position; }
                    return true;
                case KitchenTargetKind.Pot:
                    var inPot = sim.State.orders.FirstOrDefault(x => x.stage == OrderStage.Heating && x.pot == target.Index);
                    if (inPot != null && inPot != o) { world.App.SelectedOrderId = inPot.id; sim.PlayerFocusOrderId = inPot.id; world.App.UI.Refresh(); }
                    else if (o != null && o.stage == OrderStage.Weighed) sim.StartHeating(o.id, false);
                    else if (o != null && o.stage == OrderStage.Heating && o.pot == target.Index) sim.Drain(o.id);
                    return true;
                case KitchenTargetKind.Basket:
                    if (o != null && o.stage == OrderStage.Heating) { sim.Drain(o.id); return true; }
                    if (o != null && o.stage == OrderStage.Drained) { draggingBasket = true; lastDragY = position.y; dragDirection = 0; return true; }
                    return false;
                case KitchenTargetKind.Scale:
                    if (o != null && o.stage == OrderStage.Selecting) sim.Weigh(o.id);
                    return true;
                case KitchenTargetKind.Kettle:
                    if (o == null || (o.stage != OrderStage.Drained && o.stage != OrderStage.Finishing)) return true;
                    var soup = Content.Soups[Mathf.Clamp(target.Index, 0, Content.Soups.Length - 1)];
                    if (!sim.Customers.AvailableSoups.Contains(soup)) { world.App.UI.Toast(soup.Name + "スープは店舗レベル" + soup.UnlockLevel + "で解禁されます。", IconKind.Lock); return true; }
                    SelectedSoup = soup.Id; pointerHeldKettle = target.Index;
                    world.App.UI.Refresh();
                    return true;
                case KitchenTargetKind.Jar:
                    if (o == null) return true;
                    var condiment = Content.Condiments[Mathf.Clamp(target.Index, 0, Content.Condiments.Length - 1)];
                    if (!sim.Customers.AvailableCondiments.Contains(condiment)) { world.App.UI.Toast(condiment.Name + "は店舗レベル" + condiment.UnlockLevel + "で解禁されます。", IconKind.Lock); return true; }
                    if (sim.AddCondiment(o.id, condiment.Id)) paintCondiment = condiment.Id;
                    return true;
                case KitchenTargetKind.Piece:
                    if (o != null && o.stage == OrderStage.Selecting && o.style == OrderStyle.Ordered && target.Index < o.selected.Count) sim.RemoveIngredient(o.id, o.selected[target.Index]);
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>物理演算で落とした具材が落ち着いたら静止させ、負荷を減らす。</summary>
    public sealed class SettleBody : MonoBehaviour
    {
        private float time;
        private Rigidbody body;
        private void Start() { body = GetComponent<Rigidbody>(); }
        private void Update()
        {
            time += Time.deltaTime;
            if (body == null) { Destroy(this); return; }
            if (transform.position.y < -1) { body.isKinematic = true; transform.position += Vector3.up * 2; }
            if (time > 2.5f && body.linearVelocity.sqrMagnitude < .01f) { body.isKinematic = true; Destroy(this); }
        }
    }

    /// <summary>盛り付け計算に渡す 1 具材の情報。</summary>
    public struct PlatingInput
    {
        /// <summary>食材カテゴリ（noodle / vegetable など）。麺は底寄り、ほかはスープ面の上に置く。</summary>
        public string Category;
        /// <summary>具材ルートのローカル座標での外形（BoxCollider の範囲）。</summary>
        public Bounds LocalBounds;
        /// <summary>具材の倍率を器メッシュのローカル単位で表した値。</summary>
        public float RelativeScale;
        public float WeightGrams;
    }

    /// <summary>器メッシュのローカル座標での 1 具材の盛り付け姿勢。</summary>
    public struct PlatingPlacement
    {
        /// <summary>具材ルートの位置。</summary>
        public Vector3 Position;
        /// <summary>具材ルートの回転。</summary>
        public Quaternion Rotation;
        /// <summary>現在の倍率に掛ける縮小率（器からはみ出す大きな具材だけ 1 未満）。</summary>
        public float Scale;
    }

    /// <summary>
    /// MeshFactory.Bowl の器に、本物の麻辣湯のようにスープ面の上へ具材をこんもり盛る配置を計算します。
    /// 麺は中心のスープ面すれすれ、葉物・団子・肉はその上と周りに半分以上が液面から出る高さで並べます。
    /// </summary>
    public static class BowlPlating
    {
        private const float GoldenAngle = 2.39996f;
        private const float RimSafeRadius = .45f;
        /// <summary>配置順の作業領域（メインスレッド専用）。呼び出し毎の割り当てを避けるため共有する。</summary>
        private static readonly List<int> SlotOrder = new List<int>();

        /// <summary>MeshFactory.Bowl の内壁断面を補間し、指定高さの内径（半径）を返します。</summary>
        public static float InnerRadius(float height)
        {
            if (height <= .14f) return Mathf.Lerp(.23f, .31f, Mathf.InverseLerp(.07f, .14f, height));
            if (height <= .27f) return Mathf.Lerp(.31f, .4f, Mathf.InverseLerp(.14f, .27f, height));
            return Mathf.Lerp(.4f, .47f, Mathf.InverseLerp(.27f, .41f, height));
        }

        /// <summary>入力と同じ順で盛り付け姿勢を output に書き込みます。output は再利用されます。</summary>
        public static void Arrange(List<PlatingInput> inputs, float soupHeight, int seed, List<PlatingPlacement> output)
        {
            List<int> slotOrder = SlotOrder;
            output.Clear(); slotOrder.Clear();
            int count = inputs.Count;
            for (int i = 0; i < count; i++) { output.Add(default); slotOrder.Add(i); }
            // 安定ソートが必要だが List.Sort は不安定なので、件数が少ない前提で挿入ソートにする。
            for (int i = 1; i < count; i++)
            {
                int value = slotOrder[i], j = i - 1;
                while (j >= 0 && Priority(inputs[slotOrder[j]].Category) > Priority(inputs[value].Category)) { slotOrder[j + 1] = slotOrder[j]; j--; }
                slotOrder[j + 1] = value;
            }
            float surfaceRadius = InnerRadius(soupHeight);
            float moundRadius = surfaceRadius * .6f;
            for (int slot = 0; slot < count; slot++)
            {
                int index = slotOrder[slot];
                PlatingInput input = inputs[index];
                float spread = count <= 1 ? 0f : Mathf.Sqrt((slot + .35f) / count);
                float angle = slot * GoldenAngle + seed * .61f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float fullness = input.WeightGrams > 0 ? Mathf.Clamp(Mathf.Pow(input.WeightGrams / 35f, 1f / 3f) * 1.35f, 1, 1.6f) : 1;
                Vector3 size = input.LocalBounds.size * input.RelativeScale * fullness;
                bool upright = size.y > 1.25f * Mathf.Max(size.x, size.z) || (input.WeightGrams > 0 && input.Category == "mushroom" && size.y > .9f * Mathf.Max(size.x, size.z));
                bool noodle = input.Category == "noodle";
                float tilt = upright ? 66f : 8f + 16f * spread;
                Quaternion rotation = Quaternion.LookRotation(radial, Vector3.up) * Quaternion.Euler(tilt, 0f, 0f)
                    * Quaternion.Euler(0f, (slot * 53 + seed * 17) % 360, 0f);
                Vector3 extents = RotatedExtents(size * .5f, rotation);
                float footprint = 2f * Mathf.Max(extents.x, extents.z);
                float shrink = Mathf.Min(1f, surfaceRadius * 1.15f / Mathf.Max(.0001f, footprint));
                extents *= shrink;
                float radius = Mathf.Min(moundRadius * spread, Mathf.Max(0f, RimSafeRadius - Mathf.Max(extents.x, extents.z)));
                float mound = (1f - spread) * surfaceRadius * .32f + slot * .004f;
                float centerY = soupHeight + (noodle ? -extents.y * .1f : extents.y * .35f) + mound;
                Vector3 center = radial * radius + new Vector3(0f, centerY, 0f);
                shrink *= fullness;
                Vector3 pivotOffset = rotation * (input.LocalBounds.center * input.RelativeScale * shrink);
                output[index] = new PlatingPlacement { Position = center - pivotOffset, Rotation = rotation, Scale = shrink };
            }
        }

        /// <summary>麺→野菜→その他の順に中心から外へ並べるための優先度。</summary>
        private static int Priority(string category) => category == "noodle" ? 0 : category == "vegetable" ? 1 : 2;

        /// <summary>回転させた箱の軸並行な半径を求めます。</summary>
        private static Vector3 RotatedExtents(Vector3 extents, Quaternion rotation)
        {
            Vector3 x = rotation * new Vector3(extents.x, 0f, 0f);
            Vector3 y = rotation * new Vector3(0f, extents.y, 0f);
            Vector3 z = rotation * new Vector3(0f, 0f, extents.z);
            return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }
    }
}
