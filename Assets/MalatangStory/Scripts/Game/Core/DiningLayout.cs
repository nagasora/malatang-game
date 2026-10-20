using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>客席1つ分の情報。</summary>
    public readonly struct SeatInfo
    {
        public readonly int SeatId;
        public readonly PlacedFurniture Table;
        public readonly int Index;
        public SeatInfo(int seatId, PlacedFurniture table, int index) { SeatId = seatId; Table = table; Index = index; }
    }

    /// <summary>
    /// 客席フロアのグリッド配置。列 0〜11（ワールド x = -5.5 + 列）、行 0 がカウンター側（ワールド z = -1.5 - 行）。
    /// 入口は最前列の中央2セルで、常に空けておく必要がある。
    /// </summary>
    public static class DiningLayout
    {
        public const int Columns = 12;
        public const int WallSlots = 8;
        public static readonly int[] EntranceColumns = { 5, 6 };

        /// <summary>現在の客席の行数。</summary>
        public static int Rows(GameState s) => 4 + 2 * (Math.Max(1, s.EquipmentLevel("expansion")) - 1);

        /// <summary>ワールド座標でのセル中心。</summary>
        public static (float x, float z) CellCenter(int column, int row) => (-5.5f + column, -1.5f - row);

        /// <summary>ワールド座標から最寄りのセルを求める。</summary>
        public static (int column, int row) CellAt(float x, float z) => ((int)Math.Floor(x + 6f), (int)Math.Floor(-1f - z));

        /// <summary>回転を考慮した占有サイズ。</summary>
        public static (int width, int depth) Footprint(FurnitureDefinition def, int rotation)
            => rotation % 2 == 0 ? (def.Width, def.Depth) : (def.Depth, def.Width);

        /// <summary>家具が占有するセル。</summary>
        public static IEnumerable<(int column, int row)> Cells(PlacedFurniture item)
        {
            var def = Content.FurnitureById(item.defId);
            if (def.Kind == FurnitureKind.Wall) yield break;
            var (w, d) = Footprint(def, item.rotation);
            for (int c = 0; c < w; c++) for (int r = 0; r < d; r++) yield return (item.x + c, item.z + r);
        }

        /// <summary>入口セル（最前列の中央）。</summary>
        public static IEnumerable<(int column, int row)> EntranceCells(GameState s)
            => EntranceColumns.Select(c => (c, Rows(s) - 1));

        /// <summary>配置できるかを検証し、できない理由を返す。</summary>
        public static bool CanPlace(GameState s, string defId, int x, int z, int rotation, int ignoreUid, out string reason)
        {
            var def = Content.FurnitureById(defId);
            if (def.Kind == FurnitureKind.Wall)
            {
                if (x < 0 || x >= WallSlots) { reason = "壁の飾り場所を選んでください。"; return false; }
                if (s.furniture.Any(f => f.uid != ignoreUid && Content.FurnitureById(f.defId).Kind == FurnitureKind.Wall && f.x == x))
                { reason = "その壁にはすでに飾りがあります。"; return false; }
                reason = ""; return true;
            }
            var candidate = new PlacedFurniture { uid = -999, defId = defId, x = x, z = z, rotation = rotation };
            int rows = Rows(s);
            var cells = Cells(candidate).ToArray();
            if (cells.Any(c => c.column < 0 || c.column >= Columns || c.row < 0 || c.row >= rows)) { reason = "フロアからはみ出しています。"; return false; }
            var entrance = new HashSet<(int, int)>(EntranceCells(s));
            if (cells.Any(entrance.Contains)) { reason = "入口はふさげません。"; return false; }
            var others = s.furniture.Where(f => f.uid != ignoreUid).ToList();
            var occupied = new HashSet<(int, int)>(others.SelectMany(Cells));
            if (cells.Any(occupied.Contains)) { reason = "他の家具と重なっています。"; return false; }
            others.Add(candidate);
            if (!AllTablesReachable(s, others)) { reason = "通路がふさがり、席にたどり着けなくなります。"; return false; }
            reason = ""; return true;
        }

        /// <summary>入口から全テーブルとカウンター前（行0の上）に通路でつながっているか。</summary>
        public static bool AllTablesReachable(GameState s, IList<PlacedFurniture> layout)
        {
            int rows = Rows(s);
            var blocked = new HashSet<(int, int)>(layout.SelectMany(Cells));
            var reached = new HashSet<(int, int)>();
            var queue = new Queue<(int, int)>();
            foreach (var cell in EntranceCells(s)) if (!blocked.Contains(cell)) { reached.Add(cell); queue.Enqueue(cell); }
            bool reachesCounter = false;
            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                if (r == 0) reachesCounter = true;
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var next = (c + dc, r + dr);
                    if (next.Item1 < 0 || next.Item1 >= Columns || next.Item2 < 0 || next.Item2 >= rows) continue;
                    if (blocked.Contains(next) || !reached.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }
            if (!reachesCounter) return false;
            foreach (var table in layout.Where(f => Content.FurnitureById(f.defId).Kind == FurnitureKind.Table))
            {
                bool adjacent = Cells(table).Any(cell => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }
                    .Any(d => reached.Contains((cell.column + d.Item1, cell.row + d.Item2))));
                // 行0のテーブルはカウンター前の通路からも座れる。
                bool counterSide = Cells(table).Any(cell => cell.row == 0);
                if (!adjacent && !counterSide) return false;
            }
            return true;
        }

        /// <summary>全座席を列挙する。</summary>
        public static IEnumerable<SeatInfo> Seats(GameState s)
        {
            foreach (var table in s.furniture.OrderBy(f => f.uid))
            {
                var def = Content.FurnitureById(table.defId);
                if (def.Kind != FurnitureKind.Table) continue;
                for (int i = 0; i < def.Seats; i++) yield return new SeatInfo(table.uid * 10 + i, table, i);
            }
        }

        /// <summary>座席数の合計。</summary>
        public static int SeatCount(GameState s) => Seats(s).Count();

        /// <summary>座席IDからテーブルの uid を求める。</summary>
        public static int TableOfSeat(int seatId) => seatId / 10;

        /// <summary>注文がその座席を使っているか（食べ終えて帰った客は使っていない）。</summary>
        public static bool OccupiesSeat(CustomerOrder o)
            => o.seat >= 0 && o.stage != OrderStage.Cancelled && o.visualState != "leave";

        /// <summary>空いていて、片付け済みの座席。</summary>
        public static IEnumerable<SeatInfo> FreeSeats(GameState s)
        {
            var used = new HashSet<int>(s.orders.Where(OccupiesSeat).Select(o => o.seat));
            return Seats(s).Where(x => !used.Contains(x.SeatId) && !s.dirtyTables.Contains(x.Table.uid));
        }

        /// <summary>雰囲気ポイント（家具と内装テーマの合計から、片付けていない席の分を引く）。</summary>
        public static int Ambience(GameState s)
            => s.furniture.Sum(f => Content.FurnitureById(f.defId).Ambience) + Content.Theme(s.theme).Ambience - s.dirtyTables.Count * 2;

        /// <summary>新規開店時の配置（丸テーブル4卓と観葉植物）。</summary>
        public static List<PlacedFurniture> DefaultLayout(ref int nextUid)
        {
            var layout = new List<PlacedFurniture>();
            foreach (var (x, z) in new[] { (1, 0), (8, 0), (1, 2), (8, 2) })
                layout.Add(new PlacedFurniture { uid = nextUid++, defId = "table_round2", x = x, z = z });
            layout.Add(new PlacedFurniture { uid = nextUid++, defId = "plant", x = 11, z = 3 });
            layout.Add(new PlacedFurniture { uid = nextUid++, defId = "poster_menu", x = 0 });
            return layout;
        }
    }
}
