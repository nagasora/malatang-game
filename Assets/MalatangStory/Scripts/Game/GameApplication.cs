using System;
using System.IO;
using System.Linq;
using MalatangStory.Ingredients;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MalatangStory.Game
{
    /// <summary>
    /// ゲーム全体の起点。シミュレーション・ワールド・UI・オーディオ・保存を結び付け、
    /// シミュレーションの出来事を効果音・演出・通知に振り分ける。
    /// </summary>
    public sealed partial class GameApplication : MonoBehaviour
    {
        public static string SavePathOverride;
        public RestaurantSimulation Simulation { get; private set; }
        public RestaurantWorld World { get; private set; }
        public UIManager UI { get; private set; }
        public AudioDirector Audio { get; private set; }
        public IngredientIconRenderer Icons { get; private set; }
        public SaveRepository Saves { get; private set; }
        public IngredientCatalog Catalog { get; private set; }
        public GameScreen Screen { get; private set; } = GameScreen.Title;
        public string SelectedOrderId;
        public bool Paused;
        /// <summary>保存を一時的に止める（自動プレイで状態を撮影用に一時変更している間など）。</summary>
        public bool SuspendSaving;
        public bool Started;
        public float DevelopmentSpeed = 1;
        public string SaveMessage = "";
        private Font font;
        private float autosaveSeconds;
        private bool dirty;
        private DayPhase lastPhase;

        public CustomerOrder Selected => Simulation?.Find(SelectedOrderId);

        /// <summary>共有描画資源をロード中に準備し、アプリの各所有者を初期化します。</summary>
        private void Awake()
        {
            Application.targetFrameRate = 60;
            string smokeOutput = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = Environment.GetCommandLineArgs();
            int smokeIndex = Array.IndexOf(args, "--malatang-smoke");
            smokeOutput = Environment.GetEnvironmentVariable("MALATANG_SMOKE_OUTPUT");
            if (smokeIndex >= 0 && smokeIndex + 1 < args.Length) smokeOutput = args[smokeIndex + 1];
            // iOS シミュレーターでは書き込める場所がアプリ内に限られるため、"persistent" はアプリの保存領域を指す。
            if (smokeOutput == "persistent")
            {
                smokeOutput = Path.Combine(Application.persistentDataPath, "smoke");
                Directory.CreateDirectory(smokeOutput);
            }
            if (!SmokeOutputPath.TryResolve(smokeOutput, out smokeOutput)) smokeOutput = null;
            if (!string.IsNullOrEmpty(smokeOutput)) { SavePathOverride = Path.Combine(smokeOutput, "smoke-save.json"); Application.runInBackground = true; }
#endif
            UiText.Initialize();
            font = Resources.Load<Font>("Fonts/NotoSansJP") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            UIKit.HeadingFont = Resources.Load<Font>("Fonts/ZenMaruGothic-Regular") ?? font;
            Catalog = IngredientCatalog.Load();
            campaignPath = SavePathOverride ?? Path.Combine(Application.persistentDataPath, "malatang-save.json");
            Saves = campaignSaves = new SaveRepository(campaignPath);
            CharacterFactory.MaterialProvider = (color, smoothness) => MaterialLibrary.Lit(color, smoothness);
            Audio = gameObject.AddComponent<AudioDirector>();
            Audio.Initialize();
            UIKit.ClickSound = () => Audio.Play(Sfx.Click, .6f);
            Icons = new GameObject("Ingredient Icons").AddComponent<IngredientIconRenderer>();
            Icons.transform.SetParent(transform);
            UI = gameObject.AddComponent<UIManager>();
            UI.Initialize(this, font);
            SoupVisuals.PrewarmReport soupWarmup = SoupVisuals.PrewarmAll();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[SoupPrewarm] elapsedMs={soupWarmup.ElapsedMilliseconds:F2} textureBytes={soupWarmup.TextureBytes} "
                + $"meshBytes={soupWarmup.SeasoningMeshBytes} totalBytes={soupWarmup.TotalResourceBytes} nativeBytes={soupWarmup.NativeMemoryBytes}");
#endif
            ResetSimulation(null);
            StartCoroutine(Icons.RenderAll(Catalog.ingredients, 3));
            Open(GameScreen.Title);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!string.IsNullOrEmpty(smokeOutput))
            { var smoke = gameObject.AddComponent<SmokePlaythrough>(); smoke.App = this; smoke.Output = smokeOutput; }
#endif
        }

        private void ResetSimulation(GameState state)
        {
            UI?.ClearToasts();
            if (Simulation != null) { Simulation.Event -= OnSimEvent; }
            Simulation = new RestaurantSimulation(Catalog, state);
            Simulation.Changed += () => dirty = true;
            Simulation.Checkpoint += Save;
            Simulation.Event += OnSimEvent;
            if (state == null) DailyEvents.Roll(Simulation);
            if (World != null) { World.gameObject.SetActive(false); Destroy(World.gameObject); }
            World = new GameObject("Malatang Restaurant").AddComponent<RestaurantWorld>();
            World.Initialize(this, font);
            lastPhase = Simulation.State.phase;
            ApplySettings();
        }

        public void NewGame()
        {
            ResetSimulation(null);
            Started = true; Paused = false; SelectedOrderId = null;
            Save();
            Open(GameScreen.Morning);
        }

        public void Continue()
        {
            var state = Saves.Load();
            SaveMessage = Saves.LastError ?? "";
            if (state == null) { UI.Refresh(); return; }
            try
            {
                foreach (var stock in state.stock) Catalog.Get(stock.ingredientId);
                foreach (var order in state.orders) foreach (var id in order.requested.Concat(order.selected)) Catalog.Get(id);
            }
            catch (Exception e) { SaveMessage = "保存データを復元できません：" + e.Message; UI.Refresh(); return; }
            try
            {
                ResetSimulation(state);
                Started = true; Paused = false;
                SelectedOrderId = Simulation.Active.FirstOrDefault(o => o.stage != OrderStage.Waiting)?.id;
                Open(state.phase == DayPhase.Report ? GameScreen.Report : state.phase == DayPhase.Preparation ? GameScreen.Morning : state.festivalStall ? GameScreen.Stall : GameScreen.Store);
            }
            catch (Exception e)
            {
                // 復元途中の状態を見せないよう、新規状態に戻してタイトルで知らせる。
                Debug.LogWarning("保存データの復元に失敗しました：" + e);
                Started = false;
                ResetSimulation(null);
                SaveMessage = "保存データを復元できません：" + e.Message;
                Open(GameScreen.Title);
            }
        }

        /// <summary>画面を切り替え、カメラと移動可否を合わせる。</summary>
        public void Open(GameScreen screen)
        {
            Screen = screen;
            if (screen != GameScreen.Pause && screen != GameScreen.Settings && screen != GameScreen.Accessibility && screen != GameScreen.Help) Paused = false;
            if (screen != GameScreen.Cooking && screen != GameScreen.Register) Simulation.PlayerFocusOrderId = null;
            World.FocusPlayer(screen == GameScreen.Wardrobe);
            World.SetMode(screen == GameScreen.Cooking ? WorldMode.Cooking : screen == GameScreen.Build ? WorldMode.Build
                : screen == GameScreen.Register ? WorldMode.Register : screen == GameScreen.Stall ? WorldMode.Stall : screen == GameScreen.Photo ? WorldMode.Photo : screen == GameScreen.Title ? WorldMode.Title : WorldMode.Overview);
            UIKit.SetTheme(Simulation.State.theme);
            UI.Show(screen);
            UpdateMusic();
        }

        /// <summary>店内画面へ戻る（営業状態に応じて準備・レポートへ）。</summary>
        public void BackToStore()
        {
            var phase = Simulation.State.phase;
            Open(phase == DayPhase.Report ? GameScreen.Report : Simulation.State.festivalStall && phase != DayPhase.Preparation ? GameScreen.Stall : GameScreen.Store);
        }

        /// <summary>注文票を押した時の動作。段階に応じて目的の場所へ歩いて操作する。</summary>
        public void SelectOrder(string id)
        {
            SelectedOrderId = id;
            var order = Selected;
            if (order == null) return;
            bool walksAway = order.IsAtRegister || order.stage == OrderStage.Ready;
            if (walksAway && (Screen == GameScreen.Cooking || Screen == GameScreen.Register)) Open(GameScreen.Store);
            switch (order.stage)
            {
                case OrderStage.Waiting:
                case OrderStage.Ordering:
                    World.Visit(Station.Register, () => OpenRegister(id));
                    break;
                case OrderStage.Delivering:
                    UI.Toast("配達中です。到着まで " + Math.Max(0, order.deliveryDuration - order.deliverySeconds).ToString("0") + "秒", IconKind.ArrowRight);
                    break;
                case OrderStage.Ready:
                    if (order.takeout) World.Visit(Station.Register, () => Simulation.Serve(id));
                    else if (order.seat < 0) Simulation.Serve(id);
                    else World.Visit(Station.Pass, () => { World.CarryBowl(order); World.VisitSeat(order.seat, () => { World.CarryBowl(null); Simulation.Serve(id); }); });
                    break;
                default:
                    World.Visit(Station.Kitchen, () => OpenCooking(id));
                    break;
            }
        }

        /// <summary>レジでお客さんの注文を聞く（接客画面を開く）。</summary>
        public void OpenRegister(string id)
        {
            if (!Simulation.TakeOrder(id)) return;
            SelectedOrderId = id;
            Open(GameScreen.Register);
        }

        /// <summary>調理画面を開く。</summary>
        public void OpenCooking(string id)
        {
            SelectedOrderId = id;
            Open(GameScreen.Cooking);
            Simulation.SetPlayerFocus(id);
        }

        /// <summary>ワールドの設備を操作した時。</summary>
        public void Interact(Station station, int target = -1)
        {
            if (!Started || Simulation.State.phase == DayPhase.Report) return;
            var sim = Simulation;
            switch (station)
            {
                case Station.Register:
                    var ready = sim.Active.FirstOrDefault(o => o.stage == OrderStage.Ready && o.takeout);
                    if (ready != null) { sim.Serve(ready.id); break; }
                    var talking = sim.Active.FirstOrDefault(o => o.stage == OrderStage.Ordering);
                    var waiting = talking ?? sim.Queue.FirstOrDefault();
                    if (waiting != null) OpenRegister(waiting.id);
                    else if (sim.State.phase == DayPhase.Preparation) Open(GameScreen.Morning);
                    break;
                case Station.Fridge:
                    if (sim.CanManage) Open(GameScreen.Shop); else OpenKitchenForActive();
                    break;
                case Station.Kitchen:
                    OpenKitchenForActive();
                    break;
                case Station.Pass:
                    var bowl = sim.Active.FirstOrDefault(o => o.stage == OrderStage.Ready);
                    if (bowl != null) SelectOrder(bowl.id);
                    break;
                case Station.Table:
                    if (sim.State.dirtyTables.Contains(target)) sim.ClearTable(target);
                    break;
                case Station.Door:
                    if (sim.State.phase == DayPhase.Preparation) Open(GameScreen.Morning);
                    break;
            }
        }

        private void OpenKitchenForActive()
        {
            bool Cookable(CustomerOrder o) => o.stage != OrderStage.Delivering && o.stage != OrderStage.Waiting && o.stage != OrderStage.Ordering && o.stage != OrderStage.Ready;
            var order = Selected != null && Cookable(Selected) && Simulation.Active.Contains(Selected)
                ? Selected : Simulation.Active.FirstOrDefault(o => Cookable(o) && !o.handledByStaff);
            if (order != null) OpenCooking(order.id);
            else UI.Toast("調理中の注文はありません。レジでお客さんの注文を受けましょう。", IconKind.Info);
        }

        public void Save()
        {
            if (!Started || SuspendSaving) return;
            try { Saves.Save(Simulation.State); SaveMessage = "保存済み"; }
            catch (FutureSaveVersionException e) { SaveMessage = e.Message; }
            catch (Exception e) { SaveMessage = "保存できませんでした：" + e.Message; Debug.LogWarning(SaveMessage); }
        }

        public void SuspendToTitle() { if (InChallenge) { LeaveChallenge(); return; } Save(); Started = false; Paused = false; Open(GameScreen.Title); }

        public void ApplySettings()
        {
            var s = Simulation.State;
            UIKit.TextScale = s.textSize / 20f; UIKit.ReduceMotion = s.reducedMotion;
            QualitySettings.shadows = s.highQuality ? ShadowQuality.All : ShadowQuality.Disable;
            Resources.Load<UnityEngine.Rendering.Universal.ScriptableRendererFeature>("RestaurantSSAO")?.SetActive(s.highQuality);
            Application.targetFrameRate = Application.isMobilePlatform ? (s.highQuality ? 60 : 30) : 60;
            Audio.EffectsVolume = s.effectsVolume;
            Audio.MusicVolume = s.musicVolume;
            World?.ApplyQuality(s.highQuality);
        }

        private void UpdateMusic()
        {
            if (!Started || Screen == GameScreen.Title) { Audio.SetMusic(MusicMood.Title); return; }
            var s = Simulation.State;
            if (s.phase == DayPhase.Report) Audio.SetMusic(MusicMood.Report);
            else if (s.phase == DayPhase.Preparation) Audio.SetMusic(MusicMood.Preparation);
            else if (BusinessRhythm.IsRush(s.daySeconds) || Simulation.Active.Count() >= 4) Audio.SetMusic(MusicMood.Busy);
            else if (s.daySeconds / RestaurantSimulation.DayDuration > .7777f) Audio.SetMusic(MusicMood.Night);
            else Audio.SetMusic(s.daySeconds / RestaurantSimulation.DayDuration > .55f ? MusicMood.Evening : MusicMood.Business);
        }

        private void Update()
        {
            if (Keyboard.current != null && Started)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    if (Screen == GameScreen.Store) { Paused = true; Open(GameScreen.Pause); }
                    else if (Screen == GameScreen.Photo) EndPhoto();
                    else if (Screen == GameScreen.Register) BackToStore();
                    else if (Screen == GameScreen.Pause) { Paused = false; BackToStore(); }
                    else if (Screen != GameScreen.Title) BackToStore();
                }
            }
            bool modalPause = UI.Current != null && UI.Current.PausesGame;
            if (Started && !Paused && !modalPause)
            {
                Simulation.Tick(Time.deltaTime * DevelopmentSpeed);
                autosaveSeconds += Time.deltaTime;
                if (autosaveSeconds > 30) { autosaveSeconds = 0; Save(); }
                var phase = Simulation.State.phase;
                if (phase != lastPhase)
                {
                    lastPhase = phase;
                    if (phase == DayPhase.Report && (Screen == GameScreen.Store || Screen == GameScreen.Cooking || Screen == GameScreen.Register)) Open(GameScreen.Report);
                }
            }
            if (Started && Time.frameCount % 60 == 0) UpdateMusic();
            if (Started)
            {
                float boil = .12f;
                foreach (var order in Simulation.State.orders)
                    if (order.stage == OrderStage.Heating) boil += CookingFeel.Cues(order.heatSeconds / Math.Max(1, Simulation.TargetHeat(order))).Bubbles * .18f;
                Audio.SetAmbient(AmbientLoop.Boil, Simulation.State.phase == DayPhase.Open || Simulation.State.phase == DayPhase.Closing ? Mathf.Clamp01(boil) : 0);
                bool dry = Simulation.State.weather != "rain";
                Audio.SetAmbient(AmbientLoop.Birds, dry && (Simulation.State.phase == DayPhase.Preparation || Simulation.State.daySeconds < 160) ? .18f : 0);
                Audio.SetAmbient(AmbientLoop.Insects, dry && Seasons.Of(Simulation.State.day).Id == "summer" && Simulation.State.daySeconds >= 560 ? .18f : 0);
                Audio.SetAmbient(AmbientLoop.Crowd, Mathf.Clamp01(Simulation.Seated.Count() * .12f + Simulation.Queue.Count() * .08f));
                Audio.SetAmbient(AmbientLoop.Rain, Simulation.State.weather == "rain" && Screen != GameScreen.Title ? .55f : 0);
                Audio.SetAmbient(AmbientLoop.Street, World.FirstPersonActive ? .45f : .22f);
            }
            else { Audio.SetAmbient(AmbientLoop.Birds, 0); Audio.SetAmbient(AmbientLoop.Insects, 0); Audio.SetAmbient(AmbientLoop.Boil, 0); Audio.SetAmbient(AmbientLoop.Crowd, 0); Audio.SetAmbient(AmbientLoop.Rain, 0); Audio.SetAmbient(AmbientLoop.Street, Screen == GameScreen.Title ? .25f : 0); }
            if (dirty) { dirty = false; UI.Refresh(); }
        }

        /// <summary>シミュレーションの出来事を音・演出・通知へ振り分ける。</summary>
        private void OnSimEvent(SimEvent e)
        {
            var order = Simulation.Find(e.Id);
            switch (e.Kind)
            {
                case SimEventKind.OrderArrived: Audio.Play(Sfx.DoorChime, .7f); break;
                case SimEventKind.Accepted: Audio.Play(Sfx.Notification, .6f); break;
                case SimEventKind.IngredientAdded: Audio.Play(Sfx.TongClick, .8f); World.Kitchen?.OnIngredientAdded(e.Text); break;
                case SimEventKind.IngredientRemoved: Audio.Play(Sfx.Pop, .6f); World.Kitchen?.RebuildBowl(); break;
                case SimEventKind.Weighed:
                    Audio.Play(Sfx.ScaleBeep);
                    if (e.Value > 0) { Audio.Play(Sfx.Register, .8f); World.FloatAtStation(Station.Register, "+" + RestaurantSimulation.Money(e.Value), UIKit.Gold); }
                    break;
                case SimEventKind.Paid:
                    Audio.Play(Sfx.CashDrawer, .8f); Audio.Play(Sfx.CoinJingle, .5f);
                    World.FloatAtStation(Station.Register, "+" + RestaurantSimulation.Money(e.Value), UIKit.Gold);
                    break;
                case SimEventKind.CustomerSpeech:
                    Audio.Play(e.Value == 0 ? Sfx.Grumble : Sfx.Voice, .6f);
                    World.Speak(e.Id, e.Text, e.Value == 0);
                    (UI.Current as RegisterScreen)?.Say(e.Id, e.Text);
                    break;
                case SimEventKind.Discount:
                    Audio.Play(Sfx.Grumble, .8f);
                    if (order != null) World.FloatAtCustomer(order.id, "値引き -" + RestaurantSimulation.Money(e.Value), UIKit.Bad);
                    break;
                case SimEventKind.Combo:
                    if (e.Value > 0) { Audio.Play(Sfx.Success, .7f); UI.Toast(e.Text, IconKind.Fire, UIKit.Gold, 3.5f); }
                    else UI.Toast(e.Text, IconKind.Warning, UIKit.Warn, 2.6f);
                    break;
                case SimEventKind.MissionComplete:
                    Audio.Play(Sfx.Unlock, .8f);
                    UI.Toast("ミッション達成！" + e.Text + "　+" + RestaurantSimulation.Money(e.Value), IconKind.Check, UIKit.Good, 4.5f);
                    break;
                case SimEventKind.ContestResult:
                    Audio.Play(e.Value > 0 ? Sfx.LevelUp : Sfx.Notification);
                    UI.Toast(e.Text, IconKind.Trophy, UIKit.Gold, 6f);
                    if (e.Value > 0) World.Celebrate();
                    break;
                case SimEventKind.HeatingStarted: Audio.Play(Sfx.PotSplash); World.Kitchen?.RebuildBowl(); break;
                case SimEventKind.Dropped: Audio.Play(Sfx.IngredientDrop, .8f); World.Kitchen?.OnDropped((int)e.Value); break;
                case SimEventKind.Drained: Audio.Play(Sfx.PotSplash, .8f); World.Kitchen?.RebuildBowl(); break;
                case SimEventKind.Shaken: Audio.PlayPitched(Sfx.StrainerShake, 1 + (order?.handRhythm ?? 0) * .08f); World.Kitchen?.OnShake(); break;
                case SimEventKind.SoupPoured: break;
                case SimEventKind.Condiment: Audio.Play(Sfx.CondimentShake); World.Kitchen?.OnCondiment(e.Text); break;
                case SimEventKind.Finished:
                    Audio.Play(e.Value >= 80 ? Sfx.Success : Sfx.Plate);
                    World.Kitchen?.OnFinished(e.Value);
                    break;
                case SimEventKind.Served:
                    Audio.Play(Sfx.Serve);
                    if (order != null)
                    {
                        World.OnServed(order);
                        World.Speak(order.id, order.reaction, order.discountFen > 0, 4f);
                    }
                    break;
                case SimEventKind.Tip:
                    Audio.Play(Sfx.CoinJingle, .8f); Audio.Play(Sfx.Cheer, .6f);
                    if (order != null) World.FloatAtCustomer(order.id, "チップ +" + RestaurantSimulation.Money(e.Value), UIKit.Gold);
                    break;
                case SimEventKind.Cancelled: Audio.Play(Sfx.Cancel, .6f); break;
                case SimEventKind.CustomerLeft: UI.Toast(e.Text, IconKind.Warning, UIKit.Warn); Audio.Play(Sfx.Fail, .6f); break;
                case SimEventKind.MealFinished:
                    World.AgentsFinishMeal(e.Id);
                    World.Speak(e.Id, e.Text, order != null && !order.leftovers.FinishedFood, 7f);
                    break;
                case SimEventKind.TableDirty: World.Furniture.SetDirty((int)e.Value, true); break;
                case SimEventKind.TableCleared: Audio.Play(Sfx.Dishes, .8f); World.Furniture.SetDirty((int)e.Value, false); break;
                case SimEventKind.LevelUp: Audio.Play(Sfx.LevelUp); UI.Toast(e.Text, IconKind.Star, UIKit.Gold, 5f); World.Celebrate(); break;
                case SimEventKind.Unlock: Audio.Play(Sfx.Unlock); UI.Toast(e.Text, IconKind.Sparkle, UIKit.Gold, 4f); break;
                case SimEventKind.Achievement: Audio.Play(Sfx.Unlock); UI.Toast("実績達成：" + e.Text, IconKind.Trophy, UIKit.Gold, 4.5f); break;
                case SimEventKind.BoilOver: Audio.Play(Sfx.Warning); UI.Toast("鍋が吹きこぼれそう！早く引き上げて！", IconKind.Fire, UIKit.Bad); World.Kitchen?.OnBoilOver((int)e.Value); break;
                case SimEventKind.Purchased: Audio.Play(Sfx.Register, .5f); break;
                case SimEventKind.Upgraded:
                case SimEventKind.FurnitureChanged: Audio.Play(Sfx.Confirm, .8f); World.RebuildLayout(); break;
                case SimEventKind.StaffChanged: Audio.Play(Sfx.Confirm, .8f); World.RebuildStaff(); break;
                case SimEventKind.StaffAction: if (e.Value == 1) UI.Toast(e.Text, IconKind.Heart, UIKit.Jade, 3); break;
                case SimEventKind.Review: if (e.Value < 60) World.FloatAtCustomer(e.Id, "…", UIKit.Bad); break;
                case SimEventKind.SpecialEvent: Audio.Play(Sfx.Notification); UI.Toast(e.Text, IconKind.Star, UIKit.Gold, 5f); break;
                case SimEventKind.DayOpened: Audio.Play(Sfx.LevelUp, .5f); break;
                case SimEventKind.NewDay: World.Furniture.ClearDirtyBowls(); World.ApplyWeather(); break;
                case SimEventKind.Closing: Audio.Play(Sfx.Notification); UI.Toast("20:00 受付終了。残りの注文を仕上げましょう。", IconKind.Clock); break;
                case SimEventKind.Regular: if (e.Value >= 1) { Audio.Play(Sfx.Unlock, .7f); UI.Toast(e.Text, IconKind.Heart, UIKit.Lacquer, 4.5f); } break;
                case SimEventKind.Signature: Audio.Play(Sfx.Unlock); break;
                case SimEventKind.Failed: Audio.Play(Sfx.Warning, .5f); UI.Toast(e.Text, IconKind.Warning, UIKit.Warn, 2.6f); break;
                case SimEventKind.Tutorial: Audio.Play(Sfx.Confirm, .7f); break;
                case SimEventKind.RecipeDiscovered:
                    Audio.Play(Sfx.Unlock);
                    var recipe = Recipes.Find(e.Id);
                    UI.Announce("隠しレシピ発見！", "「" + recipe.Name + "」\n" + recipe.Description + "\nこれからはお客さんが名前で頼んでくれることがあります。", IconKind.Sparkle, 6f);
                    break;
                case SimEventKind.PairingDiscovered: Audio.Play(Sfx.Unlock, .6f); UI.Toast(e.Text, IconKind.Heart, UIKit.Lacquer, 4.5f); break;
                case SimEventKind.AlbumAdded: Audio.Play(Sfx.Unlock, .6f); UI.Toast(e.Text, IconKind.Book, UIKit.Gold, 4.5f); break;
                case SimEventKind.ChapterComplete:
                    Audio.Play(Sfx.LevelUp); World.Celebrate();
                    int newline = e.Text.IndexOf('\n');
                    UI.Announce(newline > 0 ? e.Text.Substring(0, newline) : e.Text, newline > 0 ? e.Text.Substring(newline + 1) : "", IconKind.Book, 12f);
                    break;
                case SimEventKind.ThemeChanged: UIKit.SetTheme(e.Id); Audio.Play(Sfx.Confirm); World.ApplyTheme(e.Id); break;
            }
        }

        private void OnApplicationPause(bool paused) { if (paused) Save(); }
        private void OnApplicationFocus(bool focus) { if (!focus) Save(); }
        private void OnApplicationQuit() { Save(); }
        /// <summary>単一アプリを前提に、利用者を先に無効化してから共有描画リソースを解放します。</summary>
        private void OnDestroy()
        {
            StopAllCoroutines();
            if (Simulation != null)
            {
                Simulation.Event -= OnSimEvent;
                Simulation.Checkpoint -= Save;
            }
            // コンポーネントだけの破棄でもUIやアイコン生成が残るため、遅延Destroyより先にホスト全体を停止する。
            if (gameObject.activeSelf) gameObject.SetActive(false);
            if (World != null)
            {
                World.gameObject.SetActive(false);
                Destroy(World.gameObject);
                World = null;
            }
            if (UI != null)
            {
                UI.enabled = false;
                if (UI.Safe != null)
                {
                    Canvas canvas = UI.Safe.GetComponentInParent<Canvas>(true);
                    if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); }
                }
                Destroy(UI);
            }
            if (Icons != null)
            {
                Icons.gameObject.SetActive(false);
                Destroy(Icons.gameObject);
            }
            UIKit.ClickSound = null;
            CharacterFactory.MaterialProvider = null;
            BowlPortrait.ReleaseAll();
            MaterialLibrary.ReleaseAll();
            SoupVisuals.ReleaseAll();
        }
    }
}
