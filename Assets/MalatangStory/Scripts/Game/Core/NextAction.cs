using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>「次にやること」の種類。</summary>
    public enum NextActionKind
    {
        /// <summary>やることがない。</summary>
        None,
        /// <summary>完成した一杯を届ける。</summary>
        Serve,
        /// <summary>レジで注文を聞く・会計する。</summary>
        Register,
        /// <summary>厨房で調理を進める。</summary>
        Cook,
        /// <summary>食べ終えた席を片付ける。</summary>
        Clean
    }

    /// <summary>次にやることの提案。</summary>
    public readonly struct NextActionHint
    {
        public readonly NextActionKind Kind;
        /// <summary>対象の注文 ID（片付けは空）。</summary>
        public readonly string OrderId;
        /// <summary>片付けるテーブルの uid（片付け以外は -1）。</summary>
        public readonly int Table;
        public readonly string Label;

        public NextActionHint(NextActionKind kind, string orderId, int table, string label)
        {
            Kind = kind; OrderId = orderId; Table = table; Label = label;
        }
    }

    /// <summary>
    /// 営業中に「次にやること」を1つ選ぶ。冷めると品質が落ちる提供を最優先にし、
    /// 次に忍耐の少ない客の多い順（会計待ち→調理→片付け）。スタッフが担当中の注文は除く。
    /// </summary>
    public static class NextAction
    {
        /// <summary>いまの状態からおすすめの行動を選ぶ。</summary>
        public static NextActionHint Suggest(RestaurantSimulation sim)
        {
            var s = sim.State;
            if (s.phase != DayPhase.Open && s.phase != DayPhase.Closing) return default;
            bool StaffBusy(CustomerOrder o) => s.staff.Any(m => !string.IsNullOrEmpty(m.task) && m.taskTarget == o.id);
            // 調理補助が仕上げた一杯も Ready になれば店主が届けられるため、提供だけは handledByStaff で除外しない。
            var ready = sim.Active.Where(o => o.stage == OrderStage.Ready && (o.takeout || o.seat >= 0) && !StaffBusy(o))
                .OrderByDescending(o => o.elapsed).FirstOrDefault();
            if (ready != null)
                return new NextActionHint(NextActionKind.Serve, ready.id, -1, ready.customerName + "に" + (ready.takeout ? "持ち帰りを渡す" : "届ける"));
            var active = sim.Active.Where(o => !o.handledByStaff && !StaffBusy(o)).ToList();
            var talking = active.FirstOrDefault(o => o.stage == OrderStage.Ordering);
            if (talking != null) return new NextActionHint(NextActionKind.Register, talking.id, -1, talking.customerName + "の会計を済ませる");
            bool freePot = sim.FreePot() >= 0;
            var cooking = active.Where(o => o.stage == OrderStage.Heating || o.stage == OrderStage.Drained || o.stage == OrderStage.Finishing
                    || o.stage == OrderStage.Selecting || (o.stage == OrderStage.Weighed && freePot))
                .OrderByDescending(o => o.stage == OrderStage.Heating ? 1 : 0).ThenByDescending(o => o.elapsed / System.Math.Max(1f, o.patience)).FirstOrDefault();
            var queue = sim.Queue.FirstOrDefault(o => !StaffBusy(o));
            // 鍋が全部ふさがっていると新しい注文を受けても待たせるだけなので、そのときは調理を先にする。
            if (queue != null && (cooking == null || freePot))
                return new NextActionHint(NextActionKind.Register, queue.id, -1, "レジで" + queue.customerName + "の注文を聞く");
            if (cooking != null) return new NextActionHint(NextActionKind.Cook, cooking.id, -1, cooking.customerName + "の麻辣湯を" + StageVerb(cooking.stage));
            if (s.dirtyTables.Count > 0) return new NextActionHint(NextActionKind.Clean, "", s.dirtyTables[0], "食べ終えた席を片付ける");
            return default;
        }

        private static string StageVerb(OrderStage stage)
        {
            switch (stage)
            {
                case OrderStage.Heating: return "茹で上げる";
                case OrderStage.Drained: case OrderStage.Finishing: return "仕上げる";
                case OrderStage.Weighed: return "茹でる";
                case OrderStage.Selecting: return "盛り付ける";
                default: return "作る";
            }
        }
    }
}
