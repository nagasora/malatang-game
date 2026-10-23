using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 客とスタッフのキャラクターをシミュレーションの状態に同期させる。
    /// 来店→行列→（セルフ式は具材コーナー）→席→食事→退店の動きと、表情・吹き出しを担当する。
    /// </summary>
    public sealed class AgentDirector
    {
        private sealed class Actor
        {
            public CharacterRig Rig;
            public NavMeshAgent Agent;
            public bool Seated, Leaving, Arrived, VisitedFridge, ReachedDoor;
            /// <summary>来店・退店で使う歩道の端（左右どちらから来たか）。</summary>
            public Vector3 Street;
            public GameObject Bowl;
            public MealBowlView Meal;
            public float EmoteCooldown;
            public OrderStage LastStage = (OrderStage)(-1);
        }

        private readonly RestaurantWorld world;
        private readonly Transform root;
        private readonly Dictionary<string, Actor> customers = new Dictionary<string, Actor>();
        private readonly Dictionary<string, Actor> staff = new Dictionary<string, Actor>();
        private readonly List<GameObject> passBowls = new List<GameObject>();
        private readonly HashSet<string> orderIds = new HashSet<string>();
        private readonly List<string> staleIds = new List<string>();
        private readonly List<CustomerOrder> queue = new List<CustomerOrder>();
        private readonly List<CustomerOrder> waiting = new List<CustomerOrder>();
        private readonly List<CustomerOrder> ready = new List<CustomerOrder>(4);
        private readonly List<string> passIds = new List<string>(4);

        public AgentDirector(RestaurantWorld world, Transform parent)
        {
            this.world = world;
            root = new GameObject("Agents").transform;
            root.SetParent(parent, false);
        }

        private RestaurantSimulation Sim => world.App.Simulation;

        /// <summary>ナビメッシュの作り直し等で外れたエージェントを最寄りの歩ける場所へ戻す。</summary>
        private static void EnsureOnNav(NavMeshAgent agent)
        {
            if (agent == null || !agent.enabled || agent.isOnNavMesh) return;
            if (NavMesh.SamplePosition(agent.transform.position, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        public static void Configure(NavMeshAgent agent, float speed)
        {
            agent.speed = speed; agent.angularSpeed = 720; agent.acceleration = 14; agent.radius = .24f; agent.height = 1.4f;
            agent.stoppingDistance = .08f; agent.autoBraking = true; agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        }

        /// <summary>客の位置（浮かぶ文字用）。</summary>
        public Vector3? CustomerPosition(string orderId) => orderId != null && customers.TryGetValue(orderId, out var a) && a.Rig != null ? a.Rig.transform.position + Vector3.up * 1.6f : (Vector3?)null;

        public void Talk(string id, float seconds)
        {
            if (id != null && customers.TryGetValue(id, out var actor) && actor.Rig != null) actor.Rig.Talk(seconds);
        }

        /// <summary>客の反応を出す。</summary>
        public void React(string orderId, float quality)
        {
            if (!customers.TryGetValue(orderId, out var actor) || actor.Rig == null) return;
            actor.Rig.PlayReaction(quality >= 60);
            actor.Rig.ShowEmote(quality >= 90 ? EmoteKind.Star : quality >= 70 ? EmoteKind.Heart : quality >= 50 ? EmoteKind.Note : EmoteKind.Sweat, 2.5f);
            actor.Rig.Mood = quality >= 70 ? CharacterMood.Happy : quality >= 50 ? CharacterMood.Neutral : CharacterMood.Impatient;
        }

        public void Update(float deltaTime, bool paused)
        {
            var sim = Sim;
            var orders = sim.State.orders;
            orderIds.Clear(); queue.Clear(); waiting.Clear(); staleIds.Clear();
            foreach (var order in orders)
            {
                orderIds.Add(order.id);
                if (order.stage == OrderStage.Waiting) queue.Add(order);
                if (!order.takeout && order.seat < 0 && order.AwaitsBowl) waiting.Add(order);
            }
            foreach (var order in orders) if (order.takeout && order.AwaitsBowl) waiting.Add(order);
            foreach (string id in customers.Keys) if (!orderIds.Contains(id)) staleIds.Add(id);
            foreach (string id in staleIds) Remove(id);
            int rows = DiningLayout.Rows(sim.State);
            foreach (var order in orders)
            {
                bool done = order.stage == OrderStage.Cancelled || (order.stage == OrderStage.Served && order.visualState == "leave");
                if (!customers.TryGetValue(order.id, out var actor))
                {
                    if (done) continue;
                    actor = Spawn(order, rows);
                }
                if (actor.Agent == null) continue;
                EnsureOnNav(actor.Agent);
                if (paused) { if (actor.Agent.enabled && actor.Agent.isOnNavMesh) actor.Agent.isStopped = true; continue; }
                if (actor.Agent.enabled && actor.Agent.isOnNavMesh) actor.Agent.isStopped = false;
                UpdateCustomer(order, actor, queue, waiting, rows, deltaTime);
            }
            UpdateStaff(deltaTime, paused);
            UpdatePassBowls();
        }

        private Actor Spawn(CustomerOrder order, int rows)
        {
            var archetype = Content.Archetype(order.archetype);
            var appearance = CharacterAppearance.ForArchetype(order.archetype, order.appearanceSeed, archetype.Child);
            var rig = CharacterFactory.Create(appearance, order.customerName, root);
            var target = rig.gameObject.AddComponent<CustomerTarget>(); target.OrderId = order.id;
            rig.gameObject.layer = RestaurantWorld.CustomerLayer;
            var capsule = rig.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up * .8f; capsule.radius = .28f; capsule.height = 1.6f; capsule.isTrigger = true;
            Vector3 door = StoreLayout.Door(rows) + new Vector3((order.appearanceSeed % 7 - 3) * .15f, 0, 0);
            Vector3 street = Street(order.appearanceSeed, rows);
            // 歩道から歩いて来るが、ナビメッシュが歩道まで届かない店の形では入口に直接出す。
            Vector3 start = NavMesh.SamplePosition(street, out var sidewalk, .8f, NavMesh.AllAreas) ? sidewalk.position : door;
            rig.transform.position = start;
            var agent = rig.gameObject.AddComponent<NavMeshAgent>();
            float speed = order.archetype == "rider" ? 3.0f : order.archetype == "elder" ? 1.5f : order.archetype == "drunk" ? 1.6f : 2.1f + (order.appearanceSeed % 5) * .1f;
            Configure(agent, speed);
            if (NavMesh.SamplePosition(start, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
            var actor = new Actor { Rig = rig, Agent = agent, VisitedFridge = order.style != OrderStyle.SelfServe, Street = start, ReachedDoor = start == door };
            customers[order.id] = actor;
            return actor;
        }

        /// <summary>歩道の左右どちらかの端（客ごとに決まる）。</summary>
        private static Vector3 Street(int seed, int rows)
            => new Vector3((seed & 1) == 0 ? -9.2f : 9.2f, 0, StoreLayout.FrontZ(rows) - 1.9f - (seed % 3) * .3f);

        /// <summary>客の頭上の位置（吹き出し用）。</summary>
        public Vector3? CustomerHead(string orderId) => orderId != null && customers.TryGetValue(orderId, out var a) && a.Rig != null ? a.Rig.transform.position + Vector3.up * (a.Seated ? 1.45f : 1.85f) : (Vector3?)null;

        /// <summary>食事終了イベントで元の器を即座に隠し、同じ席の二重表示を防ぎます。</summary>
        public void FinishMeal(string id)
        {
            if (!customers.TryGetValue(id, out Actor actor) || actor.Bowl == null) return;
            actor.Bowl.SetActive(false);
            Object.Destroy(actor.Bowl); actor.Bowl = null; actor.Meal = null;
        }

        private void Remove(string id)
        {
            if (!customers.TryGetValue(id, out var actor)) return;
            if (actor.Bowl != null) Object.Destroy(actor.Bowl);
            if (actor.Rig != null) Object.Destroy(actor.Rig.gameObject);
            customers.Remove(id);
        }

        private void MoveTo(Actor actor, Vector3 target)
        {
            if (actor.Seated) Stand(actor);
            if (!actor.Agent.enabled || !actor.Agent.isOnNavMesh) return;
            if (!NavMesh.SamplePosition(target, out var hit, 1.2f, NavMesh.AllAreas)) return;
            if ((actor.Agent.destination - hit.position).sqrMagnitude > .04f) actor.Agent.SetDestination(hit.position);
        }

        private bool Reached(Actor actor, Vector3 target, float tolerance = .35f)
        {
            if (actor.Seated) return true;
            if (!actor.Agent.enabled || actor.Agent.pathPending) return false;
            var flat = actor.Rig.transform.position - target; flat.y = 0;
            return flat.magnitude <= tolerance;
        }

        private void Sit(Actor actor, SeatPose pose)
        {
            if (actor.Seated) return;
            actor.Seated = true;
            actor.Agent.enabled = false;
            actor.Rig.transform.SetPositionAndRotation(pose.Position, pose.Facing);
            actor.Rig.SetPose(CharacterPose.Sit);
            actor.Rig.SetMoveSpeed(0);
        }

        private void Stand(Actor actor)
        {
            if (!actor.Seated) return;
            actor.Seated = false;
            var pos = actor.Rig.transform.position;
            actor.Agent.enabled = true;
            if (NavMesh.SamplePosition(pos, out var hit, 1.5f, NavMesh.AllAreas)) actor.Agent.Warp(hit.position);
            actor.Rig.SetPose(CharacterPose.Idle);
            if (actor.Bowl != null) { Object.Destroy(actor.Bowl); actor.Bowl = null; }
        }

        private void UpdateCustomer(CustomerOrder order, Actor actor, List<CustomerOrder> queue, List<CustomerOrder> waiting, int rows, float deltaTime)
        {
            var rig = actor.Rig;
            actor.EmoteCooldown -= deltaTime;
            if (!actor.Seated) rig.SetMoveSpeed(actor.Agent.enabled ? actor.Agent.velocity.magnitude : 0);
            bool stageChanged = actor.LastStage != order.stage;
            actor.LastStage = order.stage;
            bool leaving = order.stage == OrderStage.Cancelled || (order.stage == OrderStage.Served && order.visualState == "leave");
            if (leaving)
            {
                if (!actor.Leaving)
                {
                    actor.Leaving = true;
                    if (order.stage == OrderStage.Cancelled) { rig.Mood = CharacterMood.Angry; rig.ShowEmote(EmoteKind.Anger, 2.5f); }
                    else
                    {
                        rig.SetPose(CharacterPose.Wave);
                        rig.ShowEmote(order.tipFen > 0 ? EmoteKind.Coin : EmoteKind.Heart, 2f);
                    }
                }
                Stand(actor);
                if (order.stage == OrderStage.Served && order.visualSeconds < 1.5f) rig.SetPose(CharacterPose.Wave);
                Vector3 exit = StoreLayout.Door(rows) + Vector3.back * .8f;
                if (!actor.ReachedDoor && Reached(actor, exit, .9f)) actor.ReachedDoor = true;
                // 店を出たあとは来た方向の歩道へ歩き去る（歩道へ道が無い時は入口で消える）。
                Vector3 away = actor.ReachedDoor && actor.Street != Vector3.zero ? actor.Street : exit;
                MoveTo(actor, away);
                if (Reached(actor, away, .9f) || order.visualSeconds > 18) Remove(order.id);
                return;
            }
            if (!actor.ReachedDoor)
            {
                Vector3 entrance = StoreLayout.Door(rows) + Vector3.forward * .4f;
                MoveTo(actor, entrance);
                if (Reached(actor, entrance, .8f)) actor.ReachedDoor = true;
                else return;
            }
            switch (order.stage)
            {
                case OrderStage.Waiting:
                    if (!actor.VisitedFridge)
                    {
                        MoveTo(actor, StoreLayout.FridgeFront + new Vector3((order.appearanceSeed % 3 - 1) * .5f, 0, 0));
                        if (Reached(actor, StoreLayout.FridgeFront, .9f)) { actor.VisitedFridge = true; rig.SetPose(CharacterPose.Think); rig.LookAt(StoreLayout.FridgeFront + Vector3.forward * 2); }
                        break;
                    }
                    rig.SetPose(CharacterPose.Idle);
                    int index = queue.IndexOf(order);
                    var spot = StoreLayout.QueueSpot(Mathf.Max(0, index));
                    MoveTo(actor, spot);
                    if (Reached(actor, spot)) rig.LookAt(StoreLayout.RegisterTop);
                    break;
                case OrderStage.Ordering:
                    MoveTo(actor, StoreLayout.RegisterCustomer);
                    if (Reached(actor, StoreLayout.RegisterCustomer))
                    {
                        rig.LookAt(StoreLayout.RegisterTop + Vector3.forward);
                        rig.SetPose(order.ticketConfirmed ? CharacterPose.Idle : CharacterPose.Think);
                    }
                    else rig.SetPose(CharacterPose.Idle);
                    break;
                case OrderStage.Selecting when order.paidFen == 0:
                    rig.SetPose(CharacterPose.Idle);
                    MoveTo(actor, StoreLayout.RegisterCustomer);
                    if (Reached(actor, StoreLayout.RegisterCustomer)) rig.LookAt(StoreLayout.RegisterTop + Vector3.forward);
                    break;
                case OrderStage.Served:
                    if (world.Furniture.TryGetSeat(order.seat, out var eatSeat))
                    {
                        if (!Reached(actor, eatSeat.Position, .45f)) { MoveTo(actor, eatSeat.Position); break; }
                        Sit(actor, eatSeat);
                        rig.SetPose(CharacterPose.Eat);
                        if (actor.Bowl == null)
                        {
                            actor.Bowl = world.Kitchen.CreateServedBowl(order, BowlSpot(eatSeat, order.seat));
                            actor.Bowl.transform.rotation = eatSeat.Facing;
                            actor.Meal = actor.Bowl.GetComponent<MealBowlView>();
                        }
                        float duration = Sim.State.tutorialComplete ? RestaurantSimulation.EatDuration : 6f;
                        actor.Meal.ApplyProgress(order.eatSeconds / duration, order.eatSeconds, true);
                        rig.SetMealTime(order.eatSeconds);
                        if (actor.EmoteCooldown <= 0 && Random.value < .004f) { rig.ShowEmote(order.quality >= 80 ? EmoteKind.Heart : EmoteKind.Steam, 1.6f); actor.EmoteCooldown = 6; }
                    }
                    break;
                default:
                    if (order.seat >= 0 && world.Furniture.TryGetSeat(order.seat, out var seat))
                    {
                        if (!Reached(actor, seat.Position, .45f)) MoveTo(actor, seat.Position);
                        else { Sit(actor, seat); rig.SetPose(CharacterPose.Sit); }
                    }
                    else
                    {
                        rig.SetPose(CharacterPose.Idle);
                        int w = Mathf.Max(0, waiting.IndexOf(order));
                        MoveTo(actor, StoreLayout.WaitingSpot(w));
                    }
                    break;
            }
            float patience = order.paidFen == 0 ? order.elapsed / Mathf.Max(1, order.patience) : order.elapsed / Mathf.Max(1, order.patience * 2);
            if (order.stage != OrderStage.Served)
            {
                var mood = patience < .3f ? CharacterMood.Happy : patience < .6f ? CharacterMood.Neutral : patience < .85f ? CharacterMood.Impatient : CharacterMood.Angry;
                if (rig.Mood != mood) { rig.Mood = mood; if (mood == CharacterMood.Impatient && actor.EmoteCooldown <= 0) { rig.ShowEmote(EmoteKind.Sweat, 2f); actor.EmoteCooldown = 8; } }
                if (stageChanged && order.stage == OrderStage.Waiting && actor.EmoteCooldown <= 0) { rig.ShowEmote(order.archetype == "critic" ? EmoteKind.Exclamation : EmoteKind.Question, 2f); actor.EmoteCooldown = 4; }
                if (stageChanged && (order.stage == OrderStage.Weighed || (order.stage == OrderStage.Selecting && order.paidFen > 0))) rig.ShowEmote(EmoteKind.Coin, 1.5f);
            }
        }

        private Vector3 BowlSpot(SeatPose pose, int seatId)
        {
            var table = world.Furniture.Item(DiningLayout.TableOfSeat(seatId));
            Vector3 spot = Vector3.Lerp(pose.Position, pose.TableSpot, .62f);
            spot.y = FurnitureView.TableTop(table != null ? table.name : "") + .01f;
            return spot;
        }

        /// <summary>スタッフの人数・見た目を作り直す。</summary>
        public void RebuildStaff()
        {
            foreach (var actor in staff.Values) if (actor.Rig != null) Object.Destroy(actor.Rig.gameObject);
            staff.Clear();
            foreach (var member in Sim.State.staff)
            {
                var appearance = CharacterAppearance.FromSeed(member.appearanceSeed);
                appearance.apron = true; appearance.apronColor = member.role == StaffRole.Cook ? 2 : member.role == StaffRole.Server ? 1 : 3;
                appearance.hatStyle = member.role == StaffRole.Cook ? 1 : 2; appearance.accessory = 0;
                var rig = CharacterFactory.Create(appearance, member.name, root);
                Vector3 home = Home(member.role) + Vector3.right * (Sim.State.staff.IndexOf(member) % 3) * .5f;
                rig.transform.position = home;
                var agent = rig.gameObject.AddComponent<NavMeshAgent>();
                Configure(agent, 3.2f);
                if (NavMesh.SamplePosition(home, out var hit, 2f, NavMesh.AllAreas)) agent.Warp(hit.position);
                staff[member.id] = new Actor { Rig = rig, Agent = agent };
            }
        }

        private static Vector3 Home(StaffRole role) => role == StaffRole.Cashier ? StoreLayout.RegisterStaff + Vector3.left * .6f : role == StaffRole.Server ? StoreLayout.PassStaff + Vector3.right * .6f : StoreLayout.CookStaff;

        private void UpdateStaff(float deltaTime, bool paused)
        {
            foreach (var member in Sim.State.staff)
            {
                if (!staff.TryGetValue(member.id, out var actor) || actor.Rig == null) continue;
                EnsureOnNav(actor.Agent);
                if (!actor.Agent.isOnNavMesh) continue;
                actor.Agent.isStopped = paused;
                if (paused) continue;
                actor.Rig.SetMoveSpeed(actor.Agent.velocity.magnitude);
                Vector3 target = Home(member.role);
                var pose = member.resting ? CharacterPose.Sit : CharacterPose.Idle;
                var order = Sim.Find(member.taskTarget);
                switch (member.task)
                {
                    case "serve":
                        if (order != null && order.seat >= 0 && world.Furniture.TryGetSeat(order.seat, out var seat)) target = seat.Position + (seat.Position - seat.TableSpot).normalized * .2f;
                        else target = StoreLayout.RegisterStaff;
                        pose = CharacterPose.Carry;
                        break;
                    case "clean":
                        if (int.TryParse(member.taskTarget, out int uid))
                        {
                            var table = world.Furniture.Item(uid);
                            if (table != null) target = NearestFree(table.position);
                        }
                        pose = CharacterPose.Cook;
                        break;
                    case "register": target = StoreLayout.RegisterStaff; pose = CharacterPose.Wave; break;
                    case "heat": case "drain": case "finish": target = StoreLayout.CookStaff; pose = CharacterPose.Cook; break;
                }
                if (NavMesh.SamplePosition(target, out var hit, 1.5f, NavMesh.AllAreas) && (actor.Agent.destination - hit.position).sqrMagnitude > .04f) actor.Agent.SetDestination(hit.position);
                actor.Rig.SetPose(actor.Agent.velocity.magnitude > .2f && pose != CharacterPose.Carry ? CharacterPose.Idle : pose);
            }
        }

        private static Vector3 NearestFree(Vector3 position)
            => NavMesh.SamplePosition(position, out var hit, 1.6f, NavMesh.AllAreas) ? hit.position : position;

        private void UpdatePassBowls()
        {
            ready.Clear();
            foreach (var order in Sim.State.orders)
                if (order.stage == OrderStage.Ready && order.id != world.CarriedOrderId)
                {
                    ready.Add(order);
                    if (ready.Count == 4) break;
                }
            bool changed = ready.Count != passIds.Count;
            for (int i = 0; !changed && i < ready.Count; i++) changed = ready[i].id != passIds[i];
            if (!changed) return;
            passIds.Clear(); foreach (var order in ready) passIds.Add(order.id);
            foreach (var b in passBowls) if (b != null) Object.Destroy(b);
            passBowls.Clear();
            for (int i = 0; i < ready.Count; i++)
                passBowls.Add(world.Kitchen.CreateServedBowl(ready[i], StoreLayout.PassTop + new Vector3(-.55f + i * .37f, .35f, 0)));
        }
    }
}
