using System;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>
    /// NPCスタッフの自動作業。役割ごとに空いている仕事を拾い、所要時間のあとでシミュレーションの操作を実行する。
    /// プレイヤーが調理画面で扱っている注文には手を出さない。
    /// </summary>
    public sealed class StaffSystem
    {
        private readonly RestaurantSimulation sim;
        private float chatterSeconds;
        public StaffSystem(RestaurantSimulation simulation) { sim = simulation; }

        private GameState S => sim.State;

        /// <summary>1作業の所要秒（研修レベルで短くなる）。</summary>
        public static float TaskSeconds(HiredStaff member)
            => Content.StaffFor(member.role).BaseTaskSeconds * (1f - .2f * (Math.Max(1, member.trainingLevel) - 1))
                * (member.personality == 0 ? .9f : member.personality == 1 ? 1.05f : 1f)
                * (1 + member.fatigue * .005f + Math.Max(0, 50 - member.motivation) * .005f);

        public void Tick(float seconds)
        {
            chatterSeconds += seconds;
            if (chatterSeconds >= 90)
            {
                chatterSeconds %= 90;
                var speaker = S.staff.FirstOrDefault(x => x.personality == 2 && !x.resting && x.busySeconds <= 0);
                var listener = speaker == null ? null : S.staff.FirstOrDefault(x => x != speaker && !x.resting && x.busySeconds <= 0);
                if (listener != null)
                {
                    speaker.motivation = Math.Min(100, speaker.motivation + 2); listener.motivation = Math.Min(100, listener.motivation + 2);
                    sim.Emit(SimEventKind.StaffAction, speaker.id, 1, speaker.name + "「" + listener.name + "さん、ひと息つきましょう」");
                }
            }
            foreach (var member in S.staff)
            {
                if (member.resting)
                {
                    member.fatigue = Math.Max(0, member.fatigue - seconds * .2f); continue;
                }
                member.fatigue = Math.Min(100, member.fatigue + seconds * .02f);
                if (member.busySeconds > 0)
                {
                    member.busySeconds -= seconds;
                    if (member.busySeconds <= 0) Complete(member);
                }
                else Assign(member);
            }
        }

        private bool Available(CustomerOrder o) => o.id != sim.PlayerFocusOrderId && !S.staff.Any(x => x.busySeconds > 0 && x.taskTarget == o.id);

        private void Assign(HiredStaff member)
        {
            string task = "", target = "";
            switch (member.role)
            {
                case StaffRole.Server:
                    var ready = S.orders.FirstOrDefault(o => o.stage == OrderStage.Ready && !o.delivery && Available(o) && (o.takeout || o.seat >= 0));
                    if (ready != null) { task = "serve"; target = ready.id; }
                    else if (S.dirtyTables.Count > 0 && !S.staff.Any(x => x != member && x.task == "clean")) { task = "clean"; target = S.dirtyTables[0].ToString(); }
                    break;
                case StaffRole.Courier:
                    if (S.orders.Count(o => o.stage == OrderStage.Delivering) >= S.staff.Count(x => x.role == StaffRole.Courier && !x.resting)) break;
                    var delivery = S.orders.FirstOrDefault(o => o.stage == OrderStage.Ready && o.delivery && Available(o));
                    if (delivery != null) { task = "serve"; target = delivery.id; }
                    break;
                case StaffRole.Cashier:
                    var waiting = S.orders.FirstOrDefault(o => o.IsAtRegister && Available(o));
                    if (waiting != null) { task = "register"; target = waiting.id; }
                    break;
                case StaffRole.Cook:
                    var done = S.orders.FirstOrDefault(o => o.stage == OrderStage.Heating && o.handledByStaff && Available(o) && Cooked(o));
                    var drained = S.orders.FirstOrDefault(o => o.stage == OrderStage.Drained && o.handledByStaff && Available(o));
                    var weighed = S.orders.FirstOrDefault(o => o.stage == OrderStage.Weighed && Available(o));
                    if (done != null) { task = "drain"; target = done.id; }
                    else if (drained != null) { task = "finish"; target = drained.id; }
                    else if (weighed != null && sim.FreePot() >= 0) { task = "heat"; target = weighed.id; }
                    break;
            }
            if (task == "") { member.task = ""; member.taskTarget = ""; return; }
            member.task = task; member.taskTarget = target;
            member.busySeconds = task == "heat" || task == "drain" ? TaskSeconds(member) * .5f : TaskSeconds(member);
            sim.Emit(SimEventKind.StaffAction, member.role.ToString(), 0, task + ":" + target);
        }

        private bool Cooked(CustomerOrder o)
            => o.dropTimes.All(t => t >= 0) && Enumerable.Range(0, o.selected.Count).All(i => sim.Doneness(o, i) >= 1f);

        /// <summary>指定の注文を対象にしているスタッフの作業を取り消す（プレイヤーが引き継いだ時）。</summary>
        public void Release(string orderId)
        {
            foreach (var member in S.staff.Where(m => m.taskTarget == orderId))
            { member.task = ""; member.taskTarget = ""; member.busySeconds = 0; }
        }

        private void Complete(HiredStaff member)
        {
            string task = member.task, target = member.taskTarget;
            member.task = ""; member.taskTarget = ""; member.busySeconds = 0;
            // 作業中にプレイヤーが同じ注文を開いた場合は、プレイヤーの操作を優先して手を引く。
            if (target == sim.PlayerFocusOrderId) return;
            var o = sim.Find(target);
            switch (task)
            {
                case "serve":
                    if (o != null && o.stage == OrderStage.Ready) sim.Serve(o.id, true);
                    break;
                case "clean":
                    if (int.TryParse(target, out int table)) sim.ClearTable(table);
                    break;
                case "register":
                    if (o == null || (o.stage != OrderStage.Waiting && o.stage != OrderStage.Ordering)) break;
                    if (!sim.Accept(o.id, false)) break;
                    if (o.stage != OrderStage.Selecting) break;
                    var picks = o.omakase ? Omakase.AutoPick(o, sim.Unlocks.NormalAvailable, x => sim.Available(x, o)) : o.requested;
                    foreach (string id in picks) sim.AddIngredient(o.id, id);
                    if (!sim.Weigh(o.id)) sim.Cancel(o.id);
                    break;
                case "heat":
                    if (o == null || o.stage != OrderStage.Weighed) break;
                    o.handledByStaff = true;
                    if (!sim.StartHeating(o.id, false)) o.handledByStaff = false;
                    else AutoDrop(o);
                    break;
                case "drain":
                    if (o == null || o.stage != OrderStage.Heating || !sim.Drain(o.id)) break;
                    int shakes = member.trainingLevel >= 2 ? 3 : 2;
                    for (int i = 0; i < shakes; i++) sim.Shake(o.id);
                    break;
                case "finish":
                    if (o == null || o.stage != OrderStage.Drained) break;
                    FinishBowl(member, o);
                    break;
            }
        }

        private void FinishBowl(HiredStaff member, CustomerOrder o)
        {
            var soup = Content.Soup(o.soup);
            if (!sim.Customers.AvailableSoups.Any(x => x.Id == soup.Id)) soup = Content.Soups[0];
            float error = member.trainingLevel >= 3 || member.personality == 1 ? 0 : member.trainingLevel == 2 ? 15 : 35;
            float sign = CustomerDirector.StableHash(o.id) % 2 == 0 ? 1 : -1;
            sim.PourSoup(o.id, soup.Id, (soup.IdealMinMl + soup.IdealMaxMl) / 2 + sign * error);
            for (int i = 0; i < o.spice; i++) sim.AddCondiment(o.id, Content.ChiliOil);
            foreach (string id in o.requestedCondiments.Distinct()) sim.AddCondiment(o.id, id);
            sim.Finish(o.id);
        }

        /// <summary>スタッフが茹でている注文で、茹で時間の長い具材から順に、同時に仕上がるよう投入する。</summary>
        public void AutoDrop(CustomerOrder o)
        {
            if (!o.handledByStaff || o.stage != OrderStage.Heating || o.selected.Count == 0) return;
            float slowest = o.selected.Max(x => sim.Catalog.Get(x).cookingSeconds);
            for (int i = 0; i < o.selected.Count && i < o.dropTimes.Count; i++)
            {
                if (o.dropTimes[i] >= 0) continue;
                float cook = sim.Catalog.Get(o.selected[i]).cookingSeconds;
                // 大きな時間刻みで投入が遅れても全具材が同時に仕上がるよう、実際の時刻ではなく予定時刻で記録する。
                float scheduled = Math.Max(0, slowest - cook);
                if (o.heatSeconds >= scheduled) o.dropTimes[i] = Math.Min(o.heatSeconds, scheduled);
            }
        }
    }
}
