using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MalatangStory.Game
{
    /// <summary>ワールドの表示モード。</summary>
    public enum WorldMode { Title, Overview, Cooking, Build, Register }

    /// <summary>店内の設備。</summary>
    public enum Station { Register, Fridge, Kitchen, Pass, Table, Door }

    /// <summary>タップで操作できる設備の目印。</summary>
    public sealed class WorldTarget : MonoBehaviour
    {
        public Station Station;
        public int Target = -1;
        public WorldTarget Set(Station station, int target = -1) { Station = station; Target = target; return this; }
    }

    public sealed class CustomerTarget : MonoBehaviour
    {
        public string OrderId;
    }

    /// <summary>内装画面でのポインター位置の判定結果。</summary>
    public struct BuildPick
    {
        public bool valid;
        public int column, row, wallSlot, furnitureUid;
    }

    /// <summary>
    /// 3D の店舗全体。建物・家具・厨房・キャラクター・照明・カメラ・入力をまとめる。
    /// 表示モード（タイトル／店内／調理／内装／レジ）に応じてカメラと操作を切り替える。
    /// 店内は見下ろし視点と一人称視点を切り替えられる。
    /// </summary>
    public sealed class RestaurantWorld : MonoBehaviour
    {
        public GameApplication App { get; private set; }
        public Camera View { get; private set; }
        public KitchenStation Kitchen { get; private set; }
        public FurnitureView Furniture { get; private set; }
        public Transform Player => playerRig != null ? playerRig.transform : null;
        /// <summary>店舗の建物（レジの表示などを書き換えるため）。</summary>
        public StoreBuilder Store => store;
        public WorldMode Mode { get; private set; } = WorldMode.Title;
        public Vector2 TouchMovement;
        public Station? TutorialTarget;
        public string CarriedOrderId { get; private set; }

        public const int InteractionLayer = 24, CustomerLayer = 25;
        private Vector3 manualVelocity;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);
        private PointerEventData uiPointer;
        private EventSystem uiEventSystem;
        private Transform destinationMarker, unavailableMarker, interactionMarker;
        private float unavailableSeconds, footstepDistance, lastMovementSpeed = 3.8f;
        private NavMeshPath walkingPath;
        private Font font;
        private StoreBuilder store;
        private string themeId;
        private bool seasonHighQuality;
        private readonly List<Renderer> focusFurnitureRenderers = new List<Renderer>();
        private readonly List<(Renderer renderer, bool forceRenderingOff)> hiddenFocusFurniture = new List<(Renderer renderer, bool forceRenderingOff)>();
        private AgentDirector agents;
        private DayLighting lighting;
        private CharacterRig playerRig;
        private NavMeshAgent playerAgent;
        private NavMeshDataInstance navInstance;
        private int builtRows;
        private float yaw = 20, zoom = 1, titleAngle;
        private Vector2 pointerDown;
        private bool pointerPressed, dragging;
        private float lastPinch;
        private Action pendingAction;
        private Vector3? pendingTarget;
        private GameObject carriedBowl;
        private Transform ghost, grid, marker;
        private string ghostKey = "";
        private bool ghostOk;
        private bool focusPlayer;
        private bool firstPerson;
        private float lookYaw, lookPitch = 4;
        private bool playerHidden;
        private readonly List<Vector3> markerSpots = new List<Vector3>();
        private readonly List<Transform> needMarkers = new List<Transform>();

        /// <summary>一人称視点で操作しているか（店内モードのときだけ有効）。</summary>
        public bool FirstPerson => firstPerson;

        /// <summary>一人称視点が実際にカメラに効いているか。</summary>
        public bool FirstPersonActive => firstPerson && Mode == WorldMode.Overview && !focusPlayer;

        /// <summary>見えている設備（一人称視点の照準の先）。</summary>
        public WorldTarget LookTarget { get; private set; }

        public void Initialize(GameApplication app, Font font)
        {
            App = app; this.font = font;
            walkingPath = new NavMeshPath();
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
            var cameraObject = new GameObject("Restaurant Camera");
            cameraObject.transform.SetParent(transform, false);
            View = cameraObject.AddComponent<Camera>();
            View.clearFlags = CameraClearFlags.SolidColor;
            View.fieldOfView = 22; View.nearClipPlane = .1f; View.farClipPlane = 160;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
            yaw = app.Simulation.State.cameraYaw == 0 ? 20 : app.Simulation.State.cameraYaw;
            BuildEnvironment();
            var s = app.Simulation.State;
            playerRig = CharacterFactory.Create(s.chef, "店主", transform);
            playerRig.transform.position = new Vector3(s.playerX, 0, s.playerZ == 0 && s.playerX == 0 ? StoreLayout.KitchenSpot.z - 1.2f : s.playerZ);
            playerAgent = playerRig.gameObject.AddComponent<NavMeshAgent>();
            AgentDirector.Configure(playerAgent, 3.8f);
            playerAgent.avoidancePriority = 10;
            WarpToNav(playerAgent, playerRig.transform.position);
            agents = new AgentDirector(this, transform);
            agents.RebuildStaff();
            marker = BuildMarker(UIKit.Gold, 1.2f);
            destinationMarker = BuildWalkMarker(UIKit.Jade, .55f);
            unavailableMarker = BuildWalkMarker(UIKit.Bad, .55f);
            interactionMarker = BuildWalkMarker(UIKit.Gold, .8f);
            destinationMarker.name = "Walk Destination"; unavailableMarker.name = "Unreachable Destination";
            store.Label("×", Vector3.up * .15f, Quaternion.Euler(90, 0, 0), .18f, UIKit.Bad, unavailableMarker);
            destinationMarker.gameObject.SetActive(false); unavailableMarker.gameObject.SetActive(false); interactionMarker.gameObject.SetActive(false);
            firstPerson = s.firstPerson;
            lookYaw = playerRig.transform.eulerAngles.y;
            PlaceCamera(true);
            ApplyTheme(App.Simulation.State.theme);
            ApplySeason(Seasons.Of(App.Simulation.State.day).Id);
        }

        /// <summary>一人称視点で指定の位置の方を向く（演出・撮影用）。</summary>
        public void FaceTowards(Vector3 point)
        {
            Vector3 dir = point - Player.position; dir.y = 0;
            if (dir.sqrMagnitude < .0001f) return;
            lookYaw = Quaternion.LookRotation(dir).eulerAngles.y;
            lookPitch = 4;
        }

        /// <summary>一人称視点と見下ろし視点を切り替える（設定として保存される）。</summary>
        public void SetFirstPerson(bool on)
        {
            if (firstPerson == on) return;
            firstPerson = on;
            App.Simulation.State.firstPerson = on;
            lookYaw = playerRig.transform.eulerAngles.y;
            lookPitch = 4;
            if (CarriedOrderId != null) CarryBowl(App.Simulation.Find(CarriedOrderId));
        }

        /// <summary>店主の体を隠す（一人称視点では影だけ残す）。</summary>
        private void UpdatePlayerVisibility()
        {
            bool hide = FirstPersonActive;
            if (hide == playerHidden) return;
            playerHidden = hide;
            foreach (var r in playerRig.GetComponentsInChildren<Renderer>(true))
            {
                if (carriedBowl != null && r.transform.IsChildOf(carriedBowl.transform)) continue;
                r.shadowCastingMode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        private void BuildEnvironment()
        {
            var s = App.Simulation.State;
            builtRows = DiningLayout.Rows(s);
            themeId = StoreTheme.NormalizeId(themeId);
            environment = new GameObject("Environment").transform;
            environment.SetParent(transform, false);
            store = new StoreBuilder(environment, font, builtRows);
            store.Build(s.storeName);
            store.ApplyTheme(themeId);
            Kitchen = new KitchenStation(this, environment, store, App.Catalog);
            Furniture = new FurnitureView(environment, store, font) { CreateMealBowl = Kitchen.CreateLeftoverBowl };
            furnitureSources.Clear();
            Furniture.Rebuild(s, furnitureSources);
            BuildNavigation();
            lighting = new DayLighting(environment, View, store.NightLights, store.Lanterns, store.NightEmitters, store.Street);
            lighting.ApplyTheme(themeId);
            ApplyWeather();
            AddStationTargets();
            BuildGrid();
        }

        private void AddStationTargets()
        {
            void Target(string name, Vector3 center, Vector3 size, Station station)
            {
                var go = new GameObject(name + " Target");
                go.transform.SetParent(store.Root, false); go.transform.position = center;
                go.layer = InteractionLayer;
                var collider = go.AddComponent<BoxCollider>(); collider.size = size; collider.isTrigger = true;
                go.AddComponent<WorldTarget>().Set(station);
            }
            Target("Register", StoreLayout.RegisterTop + new Vector3(0, .1f, 0), new Vector3(1.2f, 1.0f, 1.0f), Station.Register);
            Target("Pass", StoreLayout.PassTop + new Vector3(0, .2f, 0), new Vector3(1.8f, .9f, .9f), Station.Pass);
            Target("Kitchen", new Vector3(0, StoreLayout.BackCounterHeight, StoreLayout.BackCounterZ), new Vector3(6f, 1.2f, 1f), Station.Kitchen);
            Target("Fridge", new Vector3(-5.1f, 1, StoreLayout.BackCounterZ), new Vector3(2.6f, 1f, 1f), Station.Fridge);
            Target("Self Fridge", new Vector3(-5.2f, .8f, StoreLayout.CounterZ), new Vector3(2.9f, 1.6f, .9f), Station.Fridge);
        }

        private void BuildNavigation()
        {
            if (navInstance.valid) navInstance.Remove();
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .25f; settings.agentHeight = 1.4f; settings.agentClimb = .2f;
            float front = StoreLayout.FrontZ(builtRows);
            var bounds = new Bounds(new Vector3(0, 0, (StoreLayout.BackWallZ + front) / 2 - 1.5f), new Vector3(20, 6, StoreLayout.BackWallZ - front + 8));
            var sources = new List<NavMeshBuildSource>(store.Sources);
            sources.AddRange(furnitureSources);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("店舗のナビゲーションを作れませんでした。");
            navInstance = NavMesh.AddNavMeshData(data);
        }

        private static void WarpToNav(NavMeshAgent agent, Vector3 position)
        {
            if (agent == null || !agent.enabled) return;
            if (NavMesh.SamplePosition(position, out var hit, 4f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        /// <summary>内装や拡張の変更後に建物・家具・厨房・ナビを作り直す。</summary>
        public void RebuildLayout()
        {
            RestoreFocusFurnitureVisibility();
            var s = App.Simulation.State;
            if (DiningLayout.Rows(s) != builtRows)
            {
                ClearBuildGhost();
                Destroy(environment.gameObject);
                BuildEnvironment();
                lighting.ApplyQuality(s.highQuality);
                if (seasonHighQuality != s.highQuality) ApplySeason(Seasons.Of(s.day).Id);
            }
            else
            {
                furnitureSources.Clear();
                Furniture.Rebuild(s, furnitureSources);
                BuildNavigation();
            }
            WarpToNav(playerAgent, playerRig.transform.position);
        }

        private readonly List<NavMeshBuildSource> furnitureSources = new List<NavMeshBuildSource>();
        private Transform environment;

        private void BuildGrid()
        {
            if (grid != null) Destroy(grid.gameObject);
            grid = new GameObject("Build Grid").transform;
            grid.SetParent(environment, false);
            var line = MaterialLibrary.Unlit(new Color(1, 1, 1, 1));
            int rows = builtRows;
            for (int c = 0; c <= DiningLayout.Columns; c++)
                StoreBuilder.Part("Grid V", StoreBuilder.RoundedBox(new Vector3(.025f, .01f, rows), .002f), line, new Vector3(-6 + c, .012f, -1 - rows / 2f), Vector3.one, grid, null, false);
            for (int r = 0; r <= rows; r++)
                StoreBuilder.Part("Grid H", StoreBuilder.RoundedBox(new Vector3(DiningLayout.Columns, .01f, .025f), .002f), line, new Vector3(0, .012f, -1 - r), Vector3.one, grid, null, false);
            foreach (int col in DiningLayout.EntranceColumns)
                StoreBuilder.Part("Entrance", StoreBuilder.RoundedBox(new Vector3(.9f, .01f, .9f), .02f), MaterialLibrary.Unlit(new Color(.9f, .3f, .2f)), new Vector3(-5.5f + col, .014f, -1.5f - (rows - 1)), Vector3.one, grid, null, false);
            grid.gameObject.SetActive(Mode == WorldMode.Build);
        }

        public void SetMode(WorldMode mode)
        {
            Mode = mode;
            if (grid != null) grid.gameObject.SetActive(mode == WorldMode.Build);
            if (mode != WorldMode.Overview) { pendingAction = null; pendingTarget = null; }
            if (mode == WorldMode.Cooking && playerAgent.isOnNavMesh) { playerAgent.SetDestination(StoreLayout.KitchenSpot); }
            if (mode == WorldMode.Register && playerAgent.isOnNavMesh) { playerAgent.SetDestination(StoreLayout.RegisterStaff); }
        }

        public void ApplyQuality(bool high) => lighting?.ApplyQuality(high);

        /// <summary>店舗と照明にテーマを適用し、未生成時や再生成後にも選択を保持する。</summary>
        public void ApplyTheme(string themeId)
        {
            this.themeId = StoreTheme.NormalizeId(themeId);
            store?.ApplyTheme(this.themeId);
            lighting?.ApplyTheme(this.themeId);
        }

        /// <summary>今日の天気を街の通行人（雨なら傘）に反映する。</summary>
        /// <summary>毎日の天気と日付に対応する季節を、街と店先へ反映します。</summary>
        public void ApplyWeather()
        {
            if (App == null) return;
            if (store != null && store.Street != null) store.Street.SetWeather(App.Simulation.State.weather);
            ApplySeason(Seasons.Of(App.Simulation.State.day).Id);
        }

        /// <summary>季節IDを街・店先へ反映します。不明なIDは春として扱い、天気と画質は現在の状態から読み取ります。</summary>
        public void ApplySeason(string seasonId)
        {
            if (App == null || store == null) return;
            string normalized = seasonId == "summer" || seasonId == "autumn" || seasonId == "winter" ? seasonId : "spring";
            seasonHighQuality = App.Simulation.State.highQuality;
            store.ApplySeason(normalized, App.Simulation.State.weather, seasonHighQuality);
            store.Street?.SetSeasonContext(View, Player);
            lighting?.ApplySeason(normalized);
        }

        // ───────── 移動と設備 ─────────

        public bool WalkTo(Vector3 destination)
        {
            bool success = WalkTo(destination, out _);
            if (!success) CannotReach(destination);
            return success;
        }

        private void CannotReach(Vector3 point)
        {
            pendingAction = null; pendingTarget = null; manualVelocity = Vector3.zero;
            if (playerAgent != null && playerAgent.isOnNavMesh) playerAgent.ResetPath();
            destinationMarker.gameObject.SetActive(false);
            App.Audio.Play(Sfx.Cancel, .4f);
            unavailableMarker.position = point + Vector3.up * .06f;
            unavailableSeconds = .8f;
            unavailableMarker.gameObject.SetActive(true);
            App.UI.Toast("近づけません。通路を確認してください。", IconKind.Info);
        }

        /// <summary>歩ける位置へ補正して歩き出す。actual は実際の目的地（到着判定に使う）。</summary>
        private bool WalkTo(Vector3 destination, out Vector3 actual)
        {
            actual = destination;
            if (playerAgent == null || !playerAgent.isOnNavMesh) return false;
            if (!NavMesh.SamplePosition(destination, out var hit, .9f, NavMesh.AllAreas)) return false;
            if (!playerAgent.CalculatePath(hit.position, walkingPath) || walkingPath.status != NavMeshPathStatus.PathComplete) return false;
            playerAgent.SetPath(walkingPath);
            manualVelocity = Vector3.zero;
            actual = hit.position;
            if (destinationMarker != null) { destinationMarker.position = actual + Vector3.up * .035f; destinationMarker.gameObject.SetActive(true); }
            return true;
        }

        /// <summary>設備の前の立ち位置。</summary>
        public Vector3 StationSpot(Station station, int target = -1)
        {
            switch (station)
            {
                case Station.Register: return StoreLayout.RegisterStaff;
                case Station.Fridge: return new Vector3(-4.8f, 0, 3.75f);
                case Station.Kitchen: return StoreLayout.KitchenSpot;
                case Station.Pass: return StoreLayout.PassStaff;
                case Station.Table:
                    var table = Furniture.Item(target);
                    return table != null && NavMesh.SamplePosition(table.position, out var hit, 1.6f, NavMesh.AllAreas) ? hit.position : Player.position;
                default: return new Vector3(0, 0, StoreLayout.FrontZ(builtRows) + .8f);
            }
        }

        /// <summary>設備まで歩き、到着してから操作する。</summary>
        public void Visit(Station station, Action onArrive = null, int target = -1)
        {
            pendingAction = null; pendingTarget = null;
            Vector3 spot = StationSpot(station, target);
            Action action = onArrive ?? (() => App.Interact(station, target));
            if (Vector3.Distance(Flat(Player.position), Flat(spot)) < .5f) { action(); return; }
            if (!WalkTo(spot, out var actual))
            {
                if (station == Station.Kitchen || station == Station.Register) action();
                else CannotReach(spot);
                return;
            }
            pendingTarget = actual; pendingAction = action;
        }

        /// <summary>席まで歩き、着いたら onArrive を実行する。</summary>
        public void VisitSeat(int seatId, Action onArrive)
        {
            if (!Furniture.TryGetSeat(seatId, out var seat)) { CannotReach(Player.position); return; }
            Vector3 approach = seat.Position + (seat.Position - seat.TableSpot).normalized * .35f;
            if (!WalkTo(approach, out var actual) && !WalkTo(seat.Position, out actual)) { CannotReach(seat.Position); return; }
            pendingTarget = actual; pendingAction = onArrive;
        }

        /// <summary>完成品を持つ（null で手放す）。</summary>
        public void CarryBowl(CustomerOrder order)
        {
            playerRig.SetCarry(null);
            if (carriedBowl != null) Destroy(carriedBowl);
            carriedBowl = null; CarriedOrderId = null;
            if (order == null) return;
            carriedBowl = Kitchen.CreateServedBowl(order, playerRig.HandAnchor.position);
            CarriedOrderId = order.id;
            if (firstPerson)
            {
                // 一人称では手が画面外になるため、丼をカメラの前に持つ。
                carriedBowl.transform.SetParent(View.transform, false);
                carriedBowl.transform.localPosition = new Vector3(.25f, -.36f, .72f);
                carriedBowl.transform.localRotation = Quaternion.Euler(18, 0, 0);
            }
            else playerRig.SetCarry(carriedBowl.transform);
            playerHidden = !FirstPersonActive;
            UpdatePlayerVisibility();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        /// <summary>近くにある操作可能な設備。</summary>
        public Station? NearestStation(out int target)
        {
            target = -1;
            if (Player == null || Mode != WorldMode.Overview) return null;
            if (FirstPersonActive && LookTarget != null) { target = LookTarget.Target; return LookTarget.Station; }
            Vector3 p = Flat(Player.position);
            Station? best = null; float bestDistance = float.MaxValue; int bestTarget = -1;
            void Consider(Station station, Vector3 spot, int t = -1)
            {
                float d = Vector3.Distance(p, Flat(spot));
                if (d > 1.7f) return;
                Vector3 direction = (Flat(spot) - p).normalized;
                float score = MovementFeel.TargetScore(d, Vector3.Dot(Player.forward, direction));
                if (score < bestDistance) { bestDistance = score; best = station; bestTarget = t; }
            }
            Consider(Station.Register, StoreLayout.RegisterStaff);
            Consider(Station.Kitchen, StoreLayout.KitchenSpot);
            Consider(Station.Pass, StoreLayout.PassStaff);
            Consider(Station.Fridge, StationSpot(Station.Fridge));
            foreach (int uid in App.Simulation.State.dirtyTables)
            {
                var table = Furniture.Item(uid);
                if (table != null) Consider(Station.Table, table.position, uid);
            }
            target = bestTarget;
            return best;
        }

        // ───────── 演出 ─────────

        public void FloatAtStation(Station station, string text, Color color)
        {
            Vector3 position = station == Station.Register ? StoreLayout.RegisterTop + Vector3.up * .8f : StationSpot(station) + Vector3.up * 1.8f;
            App.UI.FloatText(position, text, color);
        }

        /// <summary>客の頭上にせりふの吹き出しを出す。</summary>
        /// <summary>食事終了時に客の一杯を片付け前の器へ引き継ぐため消します。</summary>
        public void AgentsFinishMeal(string orderId) => agents.FinishMeal(orderId);

        public void Talk(string orderId, float seconds) => agents.Talk(orderId, seconds);
        public void Speak(string orderId, string text, bool unhappy = false, float seconds = 3.2f)
        {
            agents.Talk(orderId, Mathf.Min(seconds, 2));
            App.UI.Speech(orderId, () => agents.CustomerHead(orderId), text, seconds, unhappy);
        }

        public void FloatAtCustomer(string orderId, string text, Color color)
        {
            var pos = agents.CustomerPosition(orderId);
            if (pos.HasValue) App.UI.FloatText(pos.Value, text, color);
        }

        public void OnServed(CustomerOrder order)
        {
            agents.React(order.id, order.quality);
            var pos = agents.CustomerPosition(order.id);
            if (pos.HasValue)
            {
                EffectsFactory.CoinBurst(pos.Value);
                App.UI.FloatText(pos.Value + Vector3.up * .3f, UIKit.Stars(order.quality), order.quality >= 80 ? UIKit.Gold : Color.white);
            }
        }

        public void Celebrate()
        {
            if (Player != null) EffectsFactory.Confetti(Player.position + Vector3.up * 2);
            playerRig?.PlayReaction(true);
            playerRig?.SetPose(CharacterPose.Cheer);
        }

        public void RebuildStaff() => agents.RebuildStaff();

        /// <summary>着せ替え用に店主を客席の空いた場所へ立たせ、正面から映す。</summary>
        public void FocusPlayer(bool value)
        {
            if (value && !focusPlayer && playerAgent != null && playerAgent.isOnNavMesh)
            {
                pendingAction = null; pendingTarget = null;
                playerAgent.ResetPath();
                WarpToNav(playerAgent, new Vector3(0, 0, -.4f));
                playerRig.transform.rotation = Quaternion.Euler(0, 180, 0);
                playerRig.SetPose(CharacterPose.Wave);
            }
            focusPlayer = value;
            if (!value) RestoreFocusFurnitureVisibility();
        }

        /// <summary>着せ替え中に頭・胴・脚への視線を遮る家具の描画だけを停止する。</summary>
        private void UpdateFocusFurnitureVisibility()
        {
            RestoreFocusFurnitureVisibility();
            if (!focusPlayer || View == null || Player == null || Furniture == null || environment == null) return;
            Transform furnitureRoot = environment.Find("Furniture");
            if (furnitureRoot == null) return;

            furnitureRoot.GetComponentsInChildren<Renderer>(true, focusFurnitureRenderers);
            Vector3 cameraPosition = View.transform.position;
            (Vector3 head, Vector3 torso, Vector3 legs) samples = GetFocusPlayerSamples();
            foreach (Renderer renderer in focusFurnitureRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds bounds = renderer.bounds;
                bool obstructsPlayer = IntersectsFocusSegment(bounds, cameraPosition, samples.head)
                    || IntersectsFocusSegment(bounds, cameraPosition, samples.torso)
                    || IntersectsFocusSegment(bounds, cameraPosition, samples.legs);
                if (!obstructsPlayer) continue;
                hiddenFocusFurniture.Add((renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
            }
        }

        /// <summary>現在の外見と身長を反映した頭・胴・脚のワールド座標を取得する。</summary>
        private (Vector3 head, Vector3 torso, Vector3 legs) GetFocusPlayerSamples()
        {
            Transform visual = Player.Find("Visual");
            Transform sampleRoot = visual != null ? visual : Player;
            Transform body = visual != null ? visual.Find("Body") : null;
            Transform head = body != null ? body.Find("Head") : null;
            Vector3 headPosition = head != null ? head.position : sampleRoot.TransformPoint(new Vector3(0f, 1.16f, 0f));
            Vector3 torsoPosition = body != null ? body.position : sampleRoot.TransformPoint(new Vector3(0f, .72f, 0f));
            Vector3 legsPosition = sampleRoot.TransformPoint(new Vector3(0f, .25f, 0f));
            return (headPosition, torsoPosition, legsPosition);
        }

        /// <summary>家具のワールド境界がカメラから店主までの有限線分と交差するか判定する。</summary>
        private static bool IntersectsFocusSegment(Bounds bounds, Vector3 origin, Vector3 target)
        {
            if (bounds.Contains(origin)) return true;
            Vector3 direction = target - origin;
            float length = direction.magnitude;
            if (length <= .0001f) return false;
            Ray ray = new Ray(origin, direction / length);
            return bounds.IntersectRay(ray, out float distance) && distance >= 0f && distance <= length;
        }

        /// <summary>遮蔽対策で変更した描画停止状態を元に戻し、破棄済み家具の参照を解放する。</summary>
        private void RestoreFocusFurnitureVisibility()
        {
            foreach ((Renderer renderer, bool forceRenderingOff) state in hiddenFocusFurniture)
            {
                if (state.renderer != null) state.renderer.forceRenderingOff = state.forceRenderingOff;
            }
            hiddenFocusFurniture.Clear();
            focusFurnitureRenderers.Clear();
        }

        public void PreviewChef(CharacterAppearance appearance)
        {
            playerRig.ApplyAppearance(appearance);
            playerHidden = !FirstPersonActive;
            UpdatePlayerVisibility();
        }

        private Transform BuildWalkMarker(Color color, float size)
        {
            var root = new GameObject("Ground Ring").transform; root.SetParent(transform, false);
            StoreBuilder.Part("Ring", MeshFactory.Torus(size * .5f, .018f, 32, 6), MaterialLibrary.Emissive(color, color, .6f), Vector3.zero, Vector3.one, root, null, false);
            return root;
        }

        private Transform BuildMarker(Color color, float size)
        {
            var root = new GameObject("Marker").transform;
            root.SetParent(transform, false);
            var gem = StoreBuilder.Part("Gem", StoreBuilder.RoundedBox(Vector3.one * .28f * size, .05f), MaterialLibrary.Emissive(color, color, 1.5f), Vector3.zero, Vector3.one, root, Quaternion.Euler(45, 0, 45), false);
            gem.AddComponent<Spin>();
            root.gameObject.SetActive(false);
            return root;
        }

        private void UpdateMarkers()
        {
            var sim = App.Simulation;
            bool show = Mode == WorldMode.Overview && App.Started;
            var spots = markerSpots; spots.Clear();
            if (show)
            {
                if (sim.Queue.Any()) spots.Add(StoreLayout.RegisterTop + Vector3.up * 1.3f);
                if (sim.Active.Any(o => o.stage == OrderStage.Ready)) spots.Add(StoreLayout.PassTop + Vector3.up * 1.4f);
                foreach (int uid in sim.State.dirtyTables) { var t = Furniture.Item(uid); if (t != null) spots.Add(t.position + Vector3.up * 1.4f); }
                if (sim.Active.Any(o => o.stage == OrderStage.Heating && sim.SecondsUntilDone(o) <= 0 && !o.handledByStaff)) spots.Add(new Vector3(0, 2.2f, StoreLayout.BackCounterZ));
            }
            while (needMarkers.Count < spots.Count) needMarkers.Add(BuildMarker(new Color(1f, .55f, .2f), .8f));
            for (int i = 0; i < needMarkers.Count; i++)
            {
                bool active = i < spots.Count;
                needMarkers[i].gameObject.SetActive(active);
                if (active) needMarkers[i].position = spots[i] + Vector3.up * Mathf.Sin(Time.time * 4 + i) * .08f;
            }
            Vector3? tutorialSpot = null;
            if (show && TutorialTarget.HasValue)
            {
                if (TutorialTarget == Station.Table) { int uid = sim.State.dirtyTables.FirstOrDefault(); var t = Furniture.Item(uid); if (t != null) tutorialSpot = t.position + Vector3.up * 1.9f; }
                else tutorialSpot = (TutorialTarget == Station.Register ? StoreLayout.RegisterTop : TutorialTarget == Station.Pass ? StoreLayout.PassTop : StationSpot(TutorialTarget.Value)) + Vector3.up * 2f;
            }
            marker.gameObject.SetActive(tutorialSpot.HasValue);
            if (tutorialSpot.HasValue) marker.position = tutorialSpot.Value + Vector3.up * Mathf.Abs(Mathf.Sin(Time.time * 3)) * .3f;
        }

        // ───────── 内装 ─────────

        public BuildPick PickBuild(Vector2 screen)
        {
            var pick = new BuildPick { wallSlot = -1, furnitureUid = -1 };
            var ray = View.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var hit, 80))
            {
                var tag = hit.collider.GetComponentInParent<FurnitureTag>();
                if (tag != null) pick.furnitureUid = tag.Uid;
            }
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float distance)) return pick;
            Vector3 point = ray.GetPoint(distance);
            var (column, row) = DiningLayout.CellAt(point.x, point.z);
            pick.column = column; pick.row = row;
            pick.valid = true;
            float best = 1.6f;
            for (int slot = 0; slot < DiningLayout.WallSlots; slot++)
            {
                var (pos, _) = StoreLayout.WallSlot(slot, builtRows);
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(point.x, point.z));
                if (d < best) { best = d; pick.wallSlot = slot; }
            }
            return pick;
        }

        public void SetBuildGhost(string defId, int x, int z, int rotation, bool ok)
        {
            string key = defId + ":" + rotation;
            if (key != ghostKey || ghost == null)
            {
                ClearBuildGhost();
                ghostKey = key;
                ghost = Furniture.Create(new PlacedFurniture { uid = -1, defId = defId, x = 0, z = 0, rotation = rotation }, builtRows, null, true);
                ghost.name = "Build Ghost";
                ghostOk = !ok;
            }
            var def = Content.FurnitureById(defId);
            if (def.Kind == FurnitureKind.Wall)
            {
                var (pos, rot) = StoreLayout.WallSlot(Mathf.Clamp(x, 0, DiningLayout.WallSlots - 1), builtRows);
                ghost.position = pos; ghost.rotation = rot;
            }
            else
            {
                ghost.position = FurnitureView.Center(new PlacedFurniture { defId = defId, x = x, z = z, rotation = rotation }, builtRows) + Vector3.up * .02f;
                ghost.rotation = Quaternion.Euler(0, rotation % 2 == 1 ? 90 : 0, 0);
            }
            if (ghostOk != ok)
            {
                ghostOk = ok;
                var material = MaterialLibrary.Transparent(ok ? new Color(.3f, 1f, .4f, .5f) : new Color(1f, .25f, .2f, .5f), .5f);
                foreach (var r in ghost.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
            }
        }

        public void ClearBuildGhost()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null; ghostKey = "";
        }

        // ───────── 毎フレーム ─────────

        private void Update()
        {
            store?.SetStoreName(App.Simulation.State.storeName);
            if (App == null || App.Simulation == null || View == null) return;
            var sim = App.Simulation;
            bool paused = !App.Started || App.Paused || (App.UI.Current != null && App.UI.Current.PausesGame);
            float dt = Time.deltaTime;
            lighting.Update(sim.State.daySeconds / RestaurantSimulation.DayDuration, sim.State.phase, Mode == WorldMode.Title);
            if (playerAgent.isOnNavMesh) playerAgent.isStopped = paused;
            else WarpToNav(playerAgent, playerRig.transform.position);
            if (!paused) HandleInput(dt);
            playerRig.SetMoveSpeed(playerAgent.isOnNavMesh ? Mathf.Max(playerAgent.velocity.magnitude, manualVelocity.magnitude) : 0);
            UpdatePlayerVisibility();
            if (!paused)
            {
                footstepDistance += Mathf.Max(playerAgent.velocity.magnitude, manualVelocity.magnitude) * dt;
                if (footstepDistance >= .65f) { footstepDistance -= .65f; App.Audio.PlayAt(Sfx.Footstep, Player.position, .15f); }
            }
            if (store.Ceiling != null && store.Ceiling.gameObject.activeSelf != FirstPersonActive) store.Ceiling.gameObject.SetActive(FirstPersonActive);
            if (focusPlayer) playerRig.transform.rotation = Quaternion.Slerp(playerRig.transform.rotation, Quaternion.Euler(0, 180, 0), dt * 8);
            else if (Mode == WorldMode.Cooking && playerAgent.velocity.magnitude < .1f) { playerRig.SetPose(CharacterPose.Cook); playerRig.LookAt(Kitchen.Focus); }
            else if (Mode == WorldMode.Register && playerAgent.velocity.magnitude < .1f) { playerRig.SetPose(CharacterPose.Idle); playerRig.transform.rotation = Quaternion.Slerp(playerRig.transform.rotation, Quaternion.Euler(0, 180, 0), dt * 8); }
            else if (CarriedOrderId != null) playerRig.SetPose(CharacterPose.Carry);
            else if (playerAgent.velocity.magnitude > .1f) playerRig.SetPose(CharacterPose.Idle);
            UpdatePending();
            agents.Update(dt, paused);
            Kitchen.Update(dt, Mode == WorldMode.Cooking);
            if (CarriedOrderId != null && sim.Find(CarriedOrderId)?.stage != OrderStage.Ready) CarryBowl(null);
            UpdateMarkers();
            UpdateInteractionMarker(dt);
            PlaceCamera(false);
            UpdateCutaway();
            UpdateFocusFurnitureVisibility();
            var s = sim.State;
            s.playerX = Player.position.x; s.playerZ = Player.position.z; s.cameraYaw = yaw;
        }

        private void UpdateInteractionMarker(float dt)
        {
            unavailableSeconds -= dt;
            if (unavailableMarker != null && unavailableSeconds <= 0) unavailableMarker.gameObject.SetActive(false);
            if (destinationMarker != null && destinationMarker.gameObject.activeSelf && (!playerAgent.hasPath || Vector3.Distance(Flat(Player.position), Flat(destinationMarker.position)) < .4f)) destinationMarker.gameObject.SetActive(false);
            var station = NearestStation(out int target);
            bool show = station.HasValue && Mode == WorldMode.Overview;
            interactionMarker.gameObject.SetActive(show);
            if (show) interactionMarker.position = StationSpot(station.Value, target) + Vector3.up * .04f;
        }

        private void UpdatePending()
        {
            if (pendingAction == null || !pendingTarget.HasValue || playerAgent.pathPending) return;
            bool arrived = Vector3.Distance(Flat(Player.position), Flat(pendingTarget.Value)) < .45f
                || (playerAgent.isOnNavMesh && playerAgent.remainingDistance <= playerAgent.stoppingDistance + .15f);
            if (arrived)
            {
                var action = pendingAction;
                pendingAction = null; pendingTarget = null;
                action();
            }
        }

        private void HandleInput(float dt)
        {
            var sim = App.Simulation;
            Vector2 move = TouchMovement;
            var gamepad = Gamepad.current;
            if (gamepad != null && Mode == WorldMode.Overview)
            {
                move += gamepad.leftStick.ReadValue();
                Vector2 look = gamepad.rightStick.ReadValue();
                if (FirstPersonActive) { lookYaw += look.x * 110 * dt; lookPitch = Mathf.Clamp(lookPitch - look.y * 80 * dt, -55, 60); }
                else yaw += look.x * 90 * dt;
                if (gamepad.buttonSouth.wasPressedThisFrame)
                {
                    var station = NearestStation(out int id);
                    if (station.HasValue) App.Interact(station.Value, id);
                }
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && Mode == WorldMode.Overview)
            {
                move += new Vector2((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
                if (Enum.TryParse<Key>(sim.State.interactKey, out var interactKey) && keyboard[interactKey].wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                {
                    var near = NearestStation(out int target);
                    if (near.HasValue) App.Interact(near.Value, target);
                }
                if (keyboard.vKey.wasPressedThisFrame) SetFirstPerson(!firstPerson);
                float turn = (keyboard.rKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0);
                if (FirstPersonActive) lookYaw += turn * 110 * dt; else yaw += turn * 90 * dt;
            }
            if (Mode != WorldMode.Overview) { manualVelocity = Vector3.zero; playerRig.MotionLean = 0; }
            if (Mode == WorldMode.Overview && (move.sqrMagnitude > .01f || manualVelocity.sqrMagnitude > .0001f) && playerAgent.isOnNavMesh)
            {
                pendingAction = null; pendingTarget = null;
                if (playerAgent.hasPath) playerAgent.ResetPath();
                Quaternion heading = Quaternion.Euler(0, FirstPersonActive ? lookYaw : View.transform.eulerAngles.y, 0);
                Vector3 right = heading * Vector3.right, forward = heading * Vector3.forward;
                Vector3 delta = Vector3.ClampMagnitude(right * move.x + forward * move.y, 1);
                bool running = (keyboard != null && keyboard.leftShiftKey.isPressed) || TouchMovement.magnitude > .95f || (gamepad != null && gamepad.leftStick.ReadValue().magnitude > .95f);
                float speed = playerAgent.speed * (FirstPersonActive ? .8f : 1f) * (running ? 1.35f : 1);
                if (delta.sqrMagnitude > .001f) lastMovementSpeed = speed; else speed = lastMovementSpeed;
                Vector3 desired = delta * speed;
                if (Vector3.Dot(manualVelocity, desired) < 0) desired *= .55f;
                manualVelocity.x = MovementFeel.Step(manualVelocity.x, desired.x, dt, speed);
                manualVelocity.z = MovementFeel.Step(manualVelocity.z, desired.z, dt, speed);
                playerAgent.Move(manualVelocity * dt);
                if (!FirstPersonActive && delta.sqrMagnitude > .001f) playerRig.transform.rotation = Quaternion.Slerp(playerRig.transform.rotation, Quaternion.LookRotation(delta), dt * 12);
                playerRig.MotionLean = sim.State.reducedMotion ? 0 : Mathf.Clamp(Vector3.Dot(Player.right, desired - manualVelocity) * -2, -8, 8);
                playerRig.SetMoveSpeed(manualVelocity.magnitude);
                if (!sim.State.tutorialComplete && sim.State.tutorialStep == RestaurantSimulation.Tutorial.Move) sim.TutorialAction(RestaurantSimulation.Tutorial.Move);
            }
            if (!sim.State.tutorialComplete && sim.State.tutorialStep == RestaurantSimulation.Tutorial.Move && playerAgent.velocity.magnitude > .5f) sim.TutorialAction(RestaurantSimulation.Tutorial.Move);
            if (FirstPersonActive)
            {
                // 自動で歩いている間は進行方向へ視線をゆっくり向ける（酔いにくいよう遅めに追従）。
                if (pendingAction != null && playerAgent.velocity.sqrMagnitude > .09f)
                    lookYaw = Mathf.LerpAngle(lookYaw, Quaternion.LookRotation(playerAgent.velocity).eulerAngles.y, dt * 2.5f);
                playerRig.transform.rotation = Quaternion.Euler(0, lookYaw, 0);
                UpdateLookTarget();
            }
            else LookTarget = null;
            HandlePointer();
            if (App.Simulation.State.snapCamera && !dragging && !pointerPressed && Mode == WorldMode.Overview && !FirstPersonActive)
                yaw = Mathf.LerpAngle(yaw, Mathf.Round(yaw / 45f) * 45f, 1 - Mathf.Exp(-dt * 4));
        }

        private void HandlePointer()
        {
            var pointer = Pointer.current;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > .01f && !OverUI(mouse.position.ReadValue())) zoom = Mathf.Clamp(zoom - Mathf.Sign(scroll) * .08f, .55f, 1.35f);
            }
            TrackUiTouches();
            if (FirstPersonActive && Touch.activeTouches.Count >= 2)
            {
                // 一人称では2本指＝スティック＋視点操作。ピンチ拡大は使わない。
                TouchLook();
                pointerPressed = false;
                lastPinch = 0;
                return;
            }
            if (Touch.activeTouches.Count >= 2)
            {
                float pinch = Vector2.Distance(Touch.activeTouches[0].screenPosition, Touch.activeTouches[1].screenPosition);
                if (lastPinch > 0) zoom = Mathf.Clamp(zoom * lastPinch / Mathf.Max(1, pinch), .55f, 1.35f);
                lastPinch = pinch; pointerPressed = false;
                return;
            }
            lastPinch = 0;
            if (pointer == null) return;
            Vector2 position = pointer.position.ReadValue();
            bool pressed = pointer.press.wasPressedThisFrame, held = pointer.press.isPressed, released = pointer.press.wasReleasedThisFrame;
            if (Mode == WorldMode.Cooking)
            {
                if (pressed && OverUI(position)) return;
                Kitchen.HandlePointer(View, position, pressed, held, released);
                return;
            }
            if (Mode != WorldMode.Overview) return;
            if (pressed && !OverUI(position)) { pointerDown = position; pointerPressed = true; dragging = false; }
            if (pointerPressed && held)
            {
                Vector2 travel = position - pointerDown;
                if (Mathf.Abs(travel.x) > 18 || (FirstPersonActive && Mathf.Abs(travel.y) > 18)) dragging = true;
                if (dragging)
                {
                    Vector2 delta = pointer.delta.ReadValue();
                    if (FirstPersonActive) ApplyLook(delta);
                    else yaw += delta.x * .25f;
                }
            }
            if (pointerPressed && released)
            {
                pointerPressed = false;
                if (dragging) { dragging = false; return; }
                Ray ray = View.ScreenPointToRay(position);
                if (Physics.Raycast(ray, out var customerHit, 120, 1 << CustomerLayer, QueryTriggerInteraction.Collide))
                {
                    var customer = customerHit.collider.GetComponent<CustomerTarget>();
                    if (customer != null)
                    {
                        var order = App.Simulation.Find(customer.OrderId);
                        App.SelectedOrderId = customer.OrderId;
                        if (order != null) Speak(order.id, string.IsNullOrEmpty(order.reaction) ? order.customerName + "さんの注文票を選びました。" : order.reaction);
                        App.UI.Refresh();
                        return;
                    }
                }
                if (!Physics.Raycast(ray, out var hit, 120)) return;
                var target = hit.collider.GetComponentInParent<WorldTarget>();
                if (target != null)
                {
                    // 一人称では手の届く設備はその場で使う（歩き直すと視点が勝手に回るため）。
                    if (FirstPersonActive && Vector3.Distance(Flat(Player.position), Flat(hit.point)) < 2.4f) { App.Interact(target.Station, target.Target); return; }
                    Visit(target.Station, null, target.Target);
                    return;
                }
                var furniture = hit.collider.GetComponentInParent<FurnitureTag>();
                if (furniture != null && App.Simulation.State.dirtyTables.Contains(furniture.Uid)) { Visit(Station.Table, null, furniture.Uid); return; }
                pendingAction = null; pendingTarget = null;
                WalkTo(hit.point);
            }
        }

        private readonly HashSet<int> uiTouches = new HashSet<int>();

        /// <summary>UI（移動スティックなど）の上で始まった指を毎フレーム記録する（2本目の指が来る前から追跡するため）。</summary>
        private void TrackUiTouches()
        {
            foreach (var touch in Touch.activeTouches)
            {
                var phase = touch.phase;
                if (phase == UnityEngine.InputSystem.TouchPhase.Began && OverUI(touch.screenPosition)) uiTouches.Add(touch.touchId);
                if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled) uiTouches.Remove(touch.touchId);
            }
            if (Touch.activeTouches.Count == 0) uiTouches.Clear();
        }

        /// <summary>UI（移動スティックなど）の上で始まっていない指のドラッグで見回す。</summary>
        private void TouchLook()
        {
            foreach (var touch in Touch.activeTouches)
            {
                if (uiTouches.Contains(touch.touchId) || touch.phase != UnityEngine.InputSystem.TouchPhase.Moved) continue;
                ApplyLook(touch.delta);
            }
        }

        private void ApplyLook(Vector2 delta)
        {
            var settings = App.Simulation.State;
            lookYaw += delta.x * .22f * settings.lookSensitivity;
            lookPitch = Mathf.Clamp(lookPitch + delta.y * .18f * settings.lookSensitivity * (settings.invertLook ? 1 : -1), -55, 60);
        }

        /// <summary>画面中央の照準の先にある設備を調べる（手の届く範囲だけ）。</summary>
        private void UpdateLookTarget()
        {
            LookTarget = PickLookTarget(new Ray(View.transform.position, View.transform.forward));
        }

        public static WorldTarget PickLookTarget(Ray ray)
        {
            if (!Physics.SphereCast(ray, .15f, out var hit, 3.2f, 1 << InteractionLayer, QueryTriggerInteraction.Collide)) return null;
            return hit.collider.GetComponentInParent<WorldTarget>();
        }

        private bool OverUI(Vector2 position)
        {
            var current = EventSystem.current;
            if (current == null) return false;
            if (uiPointer == null || uiEventSystem != current) { uiEventSystem = current; uiPointer = new PointerEventData(current); }
            uiPointer.Reset(); uiPointer.position = position;
            uiHits.Clear(); current.RaycastAll(uiPointer, uiHits);
            return uiHits.Count > 0;
        }

        private void PlaceCamera(bool immediate)
        {
            float front = StoreLayout.FrontZ(builtRows);
            Vector3 center = new Vector3(0, 0, (StoreLayout.BackWallZ + front) / 2 - .4f);
            float storeSize = StoreLayout.BackWallZ - front;
            Vector3 target, position;
            float fov;
            switch (Mode)
            {
                case WorldMode.Title:
                    if (!App.Simulation.State.reducedMotion) titleAngle += Time.deltaTime * 4;
                    target = center + new Vector3(0, .6f, 0);
                    position = target + Quaternion.Euler(28, titleAngle - 30, 0) * new Vector3(0, 0, -storeSize * 2.1f);
                    fov = 30;
                    break;
                case WorldMode.Cooking:
                    var focus = Kitchen.Focus;
                    bool heating = App.Selected != null && App.Selected.stage == OrderStage.Heating;
                    position = focus + (heating ? new Vector3(0, 1.15f, -1.45f) : new Vector3(0, 1.45f, -1.85f));
                    var shift = new Vector3(.62f, 0, 0);
                    target = focus + shift;
                    position += shift;
                    fov = App.Selected != null && App.Selected.stage == OrderStage.Ready && !App.Simulation.State.reducedMotion ? 34 : 38;
                    break;
                case WorldMode.Register:
                    // レジの内側から、カウンター越しにお客さんを見る（店主の目線）。
                    position = StoreLayout.RegisterStaff + new Vector3(-.55f, 1.75f, .5f);
                    target = (agents.CustomerHead(App.SelectedOrderId) ?? StoreLayout.RegisterCustomer + Vector3.up * 1.85f) + new Vector3(-.55f, -.3f, 0);
                    fov = 46;
                    break;
                case WorldMode.Build:
                    target = new Vector3(0, 0, -1 - builtRows / 2f);
                    position = target + new Vector3(0, 11 + builtRows * .9f, -2.5f);
                    fov = 50;
                    break;
                default:
                    if (FirstPersonActive && Player != null)
                    {
                        position = Player.position + Vector3.up * 1.52f + Quaternion.Euler(0, lookYaw, 0) * Vector3.forward * .12f;
                        if (!App.Simulation.State.reducedMotion) position.y += Mathf.Sin(Time.time * 10) * Mathf.Clamp01(playerAgent.velocity.magnitude) * .012f;
                        target = position + Quaternion.Euler(lookPitch, lookYaw, 0) * Vector3.forward;
                        fov = 62;
                        break;
                    }
                    if (focusPlayer && Player != null)
                    {
                        target = Player.position + new Vector3(.75f, .8f, 0);
                        position = Player.position + new Vector3(.2f, 1.5f, -3.6f);
                        fov = 30;
                        break;
                    }
                    target = Vector3.Lerp(center, Player != null ? Flat(Player.position) : center, .25f) + new Vector3(0, .4f, 0);
                    position = target + Quaternion.Euler(48, yaw, 0) * new Vector3(0, 0, -storeSize * 2.55f * zoom);
                    fov = 24;
                    break;
            }
            // 一人称は入力に遅れなく追従させる（遅れると酔いやすい）。それ以外は滑らかに寄せる。
            float t = immediate ? 1 : FirstPersonActive && Vector3.Distance(View.transform.position, position) < .6f ? 1 - Mathf.Exp(-Time.deltaTime * 30) : 1 - Mathf.Exp(-Time.deltaTime * 6);
            View.transform.position = Vector3.Lerp(View.transform.position, position, t);
            var look = Quaternion.LookRotation(target - View.transform.position);
            View.transform.rotation = immediate ? look : Quaternion.Slerp(View.transform.rotation, look, t);
            View.fieldOfView = Mathf.Lerp(View.fieldOfView, fov, t);
        }

        private void UpdateCutaway()
        {
            Vector3 forward = View.transform.forward; forward.y = 0; forward.Normalize();
            bool cooking = Mode == WorldMode.Cooking || Mode == WorldMode.Register || FirstPersonActive;
            foreach (var (wall, normal, height) in store.CutawayWalls)
            {
                if (wall == null) continue;
                bool facing = (Vector3.Dot(normal, forward) > .25f || Mode == WorldMode.Build) && !FirstPersonActive;
                float targetScale = facing && !cooking ? .12f : 1f;
                var scale = wall.localScale;
                scale.y = Mathf.Lerp(scale.y, targetScale, 1 - Mathf.Exp(-Time.deltaTime * 8));
                wall.localScale = scale;
                bool hide = facing && scale.y < .2f && (wall.name.Contains("Awning") || wall.name.Contains("Sign") || wall.name.Contains("Frame"));
                if (wall.gameObject.activeSelf == hide) wall.gameObject.SetActive(!hide);
            }
        }

        /// <summary>ワールドの無効化時にも家具の描画状態を直ちに復元する。</summary>
        private void OnDisable()
        {
            RestoreFocusFurnitureVisibility();
        }

        /// <summary>ワールド破棄時に家具の描画状態とナビゲーションを解放する。</summary>
        private void OnDestroy()
        {
            RestoreFocusFurnitureVisibility();
            if (navInstance.valid) navInstance.Remove();
        }
    }

    /// <summary>ゆっくり回転する（目印用）。</summary>
    public sealed class Spin : MonoBehaviour
    {
        private void Update() { transform.Rotate(0, 120 * Time.deltaTime, 0, Space.World); }
    }
}
