using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MalatangStory.Ingredients;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>
    /// 保存データの読み書き。一時ファイルに書き切ってから置換し、チェックサムと1世代前のバックアップで破損から復旧する。
    /// 旧形式（v1）は読み込み時に v2 へ移行する。未知の新しい形式は上書きせずエラーにする。
    /// </summary>
    /// <summary>アプリより新しい形式の保存データ。破損とは区別し、復元にも上書きにも使わない。</summary>
    public sealed class FutureSaveVersionException : InvalidOperationException
    {
        public FutureSaveVersionException() : base("新しいバージョンの保存データです。アプリを更新してください。") { }
    }

    public sealed class SaveRepository
    {
        [Serializable] private sealed class Envelope { public int version = GameState.CurrentVersion; public string payload, checksum; }
        private readonly string path;
        public string LastError { get; private set; }
        public bool Exists => File.Exists(path) || File.Exists(path + ".bak");
        public SaveRepository(string path) { this.path = path; }

        /// <summary>主ファイルが新しい形式で、保存を禁止しているか。</summary>
        public bool BlockedByFutureVersion { get; private set; }

        public void Save(GameState state)
        {
            if (BlockedByFutureVersion || IsFutureVersion(path)) { BlockedByFutureVersion = true; throw new FutureSaveVersionException(); }
            Validate(state);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string payload = JsonUtility.ToJson(state);
            string text = JsonUtility.ToJson(new Envelope { payload = payload, checksum = Hash(payload) });
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                // 壊れた主ファイルでバックアップを上書きしないよう、読めることを確かめてから世代を回す。
                try { Read(path); File.Copy(path, path + ".bak", true); }
                catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException) { }
                File.Replace(temporary, path, null);
            }
            else File.Move(temporary, path);
            LastError = null;
        }

        public GameState Load()
        {
            LastError = null;
            BlockedByFutureVersion = false;
            if (IsFutureVersion(path))
            {
                // 新しい形式の主ファイルを古いバックアップで置き換えると進捗が消えるため、復元もしない。
                BlockedByFutureVersion = true;
                LastError = new FutureSaveVersionException().Message;
                return null;
            }
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    var state = Read(candidate);
                    if (candidate != path) LastError = "直前のバックアップから復元しました。";
                    return state;
                }
                catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException)
                { LastError = "保存データを読み込めませんでした：" + e.Message; }
            }
            return null;
        }

        /// <summary>ファイルがアプリより新しい保存形式か（読めない・壊れている場合は false）。</summary>
        private static bool IsFutureVersion(string filename)
        {
            if (!File.Exists(filename)) return false;
            try
            {
                var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(filename));
                if (envelope == null || envelope.payload == null || envelope.checksum != Hash(envelope.payload)) return false;
                if (envelope.version > GameState.CurrentVersion) return true;
                var state = JsonUtility.FromJson<GameState>(envelope.payload);
                return state != null && state.saveVersion > GameState.CurrentVersion;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { return false; }
        }

        private static GameState Read(string filename)
        {
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(filename));
            if (envelope == null || envelope.payload == null || envelope.checksum != Hash(envelope.payload))
                throw new InvalidOperationException("保存データの形式またはチェックサムが不正です。");
            if (envelope.version < 1 || envelope.version > GameState.CurrentVersion)
                throw new InvalidOperationException("新しいバージョンの保存データです。アプリを更新してください。");
            var state = JsonUtility.FromJson<GameState>(envelope.payload);
            if (state == null) throw new InvalidOperationException("保存データが空です。");
            if (state.saveVersion > GameState.CurrentVersion) throw new InvalidOperationException("新しいバージョンの保存データです。アプリを更新してください。");
            SaveMigration.Migrate(state);
            Validate(state);
            return state;
        }

        /// <summary>保存する状態の整合性と、描画で利用する薬味IDの定義を検証します。</summary>
        public static void Validate(GameState s)
        {
            if (s == null || s.saveVersion != GameState.CurrentVersion || s.day < 1 || string.IsNullOrEmpty(s.storeId)
                || !Enum.IsDefined(typeof(DayPhase), s.phase) || s.pots < 1 || s.pots > 4
                || s.storageCapacity < 30 || s.storageCapacity > 120 || s.pricePer100gFen < 900 || s.pricePer100gFen > 3000
                || float.IsNaN(s.daySeconds) || s.daySeconds < 0 || s.daySeconds > RestaurantSimulation.DayDuration
                || s.stock == null || s.orders == null || s.report == null || s.ingredients == null
                || s.ingredients.unlockedIds == null || s.ingredients.settledOrderIds == null || s.ingredients.settledRevenueFen < 0
                || s.tutorialStep < 0 || s.tutorialStep > RestaurantSimulation.TutorialFinished
                || s.shopLevel < 1 || s.shopLevel > Content.MaxShopLevel || s.experience < 0
                || s.furniture == null || s.staff == null || s.regulars == null || s.signatures == null || s.equipment == null
                || s.achievements == null || s.dirtyTables == null || s.stats == null || s.chef == null)
                throw new InvalidOperationException("保存データに無効なゲーム状態があります。");
            if (s.stock.Any(x => x == null || string.IsNullOrEmpty(x.ingredientId) || x.quantity < 0 || x.quantity > s.storageCapacity || x.ageDays < 0)
                || s.stock.Select(x => x.ingredientId).Distinct().Count() != s.stock.Count
                || s.orders.Any(x => x == null || string.IsNullOrEmpty(x.id) || x.requested == null || x.selected == null || x.dropTimes == null
                    || SaveMigration.HasUnknownCondiments(x.condiments) || SaveMigration.HasUnknownCondiments(x.requestedCondiments)
                    || SaveMigration.HasUnknownCondiments(x.dislikedCondiments) || x.report == null
                    || !Enum.IsDefined(typeof(OrderStage), x.stage) || float.IsNaN(x.heatSeconds) || x.heatSeconds < 0 || x.paidFen < 0)
                || s.orders.Select(x => x.id).Distinct().Count() != s.orders.Count)
                throw new InvalidOperationException("保存データの注文または在庫が不正です。");
            if (s.furniture.Any(f => f == null || Content.Furniture.All(d => d.Id != f.defId))
                || s.furniture.Select(f => f.uid).Distinct().Count() != s.furniture.Count)
                throw new InvalidOperationException("保存データの内装が不正です。");
            if (s.staff.Any(m => m == null || !Enum.IsDefined(typeof(StaffRole), m.role) || m.trainingLevel < 1 || m.trainingLevel > 3)
                || s.staff.Select(m => m.role).Distinct().Count() != s.staff.Count
                || s.equipment.Any(e => e == null || Content.Equipment.All(d => d.Id != e.id) || e.level < 0 || e.level > Content.EquipmentById(e.id).MaxLevel)
                || s.regulars.Any(r => r == null || string.IsNullOrEmpty(r.id) || r.favorites == null
                    || SaveMigration.HasUnknownCondiments(r.likedCondiments) || SaveMigration.HasUnknownCondiments(r.dislikedCondiments))
                || s.signatures.Any(d => d == null || string.IsNullOrEmpty(d.id) || d.ingredients == null || SaveMigration.HasUnknownCondiments(d.condiments))
                || s.signatures.Select(d => d.id).Distinct().Count() != s.signatures.Count)
                throw new InvalidOperationException("保存データのスタッフ・設備・常連・名物が不正です。");
            if (s.album != null && s.album.Any(e => e != null && SaveMigration.HasUnknownCondiments(e.condiments)))
                throw new InvalidOperationException("保存データのアルバムに未知の薬味があります。");
            var seats = new HashSet<int>(DiningLayout.Seats(s).Select(x => x.SeatId));
            var occupied = s.orders.Where(DiningLayout.OccupiesSeat).Select(o => o.seat).ToList();
            var heatingPots = s.orders.Where(o => o.stage == OrderStage.Heating).Select(o => o.pot).ToList();
            if (occupied.Any(seat => !seats.Contains(seat)) || occupied.Distinct().Count() != occupied.Count
                || heatingPots.Any(p => p < 0 || p >= s.pots) || heatingPots.Distinct().Count() != heatingPots.Count)
                throw new InvalidOperationException("保存データの席または鍋の割り当てが不正です。");
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
    }

    /// <summary>旧形式の保存データを現在の形式へ移行する。</summary>
    public static class SaveMigration
    {
        private static readonly Dictionary<string, string> SoupNames = new Dictionary<string, string> { ["麻辣"] = "mala", ["白湯"] = "baitang", ["トマト"] = "tomato" };

        /// <summary>移行と欠損値の補完を行う。移行済みなら補完のみ。</summary>
        public static void Migrate(GameState s)
        {
            if (s.saveVersion <= 7)
            {
                s.marketDay = 0; s.marketIngredient = ""; s.marketPurchased = false;
                if (s.stats != null) s.stats.rareCombinationBowls = 0;
                if (s.ingredients != null) s.ingredients.rareObtainedIds = new List<string>();
            }
            if (s.saveVersion <= 8)
            {
                s.mealRemains = new List<MealRemains>();
                if (s.orders != null) foreach (CustomerOrder order in s.orders.Where(x => x != null)) { order.mealFinished = false; order.leftovers = new MealLeftoverResult(); }
            }
            Normalize(s);
            if (s.saveVersion >= GameState.CurrentVersion) return;
            if (s.saveVersion == 1) FromVersion1(s);
            // v3 以前には案内の設定が無く、JsonUtility が欠落した bool を false にし得るため、明示的に既定値へ戻す。
            if (s.saveVersion <= 3) { s.navigation = true; s.invertLook = false; s.lookSensitivity = 1f; }
            // v6 以前に完成していた一杯は相性を記録していないため、提供時に発見できるよう具材から組み直す（品質は確定済みなので加点しない）。
            if (s.saveVersion <= 6)
                foreach (var o in s.orders.Where(o => o != null && o.stage == OrderStage.Ready && o.pairings.Count == 0))
                    o.pairings = Pairings.In(o.selected).Select(p => p.Id).ToList();
            s.saveVersion = GameState.CurrentVersion;
        }

        private static void Normalize(GameState s)
        {
            s.stock ??= new List<Stock>();
            s.orders ??= new List<CustomerOrder>();
            s.report ??= new DailyReport();
            s.report.reviews ??= new List<string>();
            s.report.highlights ??= new List<string>();
            s.ingredients ??= new IngredientProgress();
            MorningMarket.Normalize(s);
            s.furniture ??= new List<PlacedFurniture>();
            s.dirtyTables ??= new List<int>();
            s.staff ??= new List<HiredStaff>();
            s.regulars ??= new List<RegularCustomer>();
            s.signatures ??= new List<SignatureDish>();
            s.equipment ??= new List<EquipmentLevel>();
            s.achievements ??= new List<string>();
            s.recentReviews ??= new List<string>();
            s.stats ??= new LifetimeStats();
            s.stats.rareCombinationBowls = Math.Max(0, Math.Min(s.stats.served, s.stats.rareCombinationBowls));
            s.stats.archetypesServed ??= new List<NamedCount>();
            s.stats.bestQualityBySoup ??= new List<NamedCount>();
            s.stats.ingredientsServed ??= new List<NamedCount>();
            s.report.servedArchetypes ??= new List<string>();
            s.missions ??= new List<Mission>();
            s.missions.RemoveAll(m => m == null || string.IsNullOrEmpty(m.kind));
            s.chef ??= new CharacterAppearance { apron = true, hatStyle = 2 };
            s.recipes ??= new List<NamedCount>();
            s.skills ??= new List<NamedCount>();
            s.pairings ??= new List<NamedCount>();
            s.pairings.RemoveAll(p => p == null || Pairings.Find(p.id) == null);
            s.album ??= new List<AlbumEntry>();
            foreach (var e in s.album.Where(e => e != null))
            {
                e.condiments ??= new List<string>(); e.pairings ??= new List<string>();
                e.condiments.RemoveAll(IsUnknownCondiment);
                e.customerName ??= ""; e.archetype ??= ""; e.recipe ??= "";
                e.pairings.RemoveAll(p => Pairings.Find(p) == null);
            }
            // JsonUtility は JSON の null を既定値のオブジェクトにして読むため、条件を満たさない記録は壊れたものとして除く。
            s.album.RemoveAll(e => !BowlAlbum.IsValid(e));
            BowlAlbum.Sort(s);
            s.albumSeq = Math.Max(s.albumSeq, s.album.Count == 0 ? 0 : s.album.Max(e => e.seq));
            s.history ??= new List<DaySummary>();
            // JsonUtility は JSON の null を既定値のオブジェクトにして読むため、日付の無い要素も壊れた記録として除く。
            s.history.RemoveAll(h => h == null || h.day <= 0);
            s.featuredIngredient ??= "";
            s.skills.RemoveAll(x => x == null || Skills.Find(x.id) == null);
            s.recipes.RemoveAll(r => r == null || Recipes.Find(r.id) == null);
            s.ownedThemes ??= new List<string>();
            s.ownedThemes.RemoveAll(t => Content.Theme(t).Id != t);
            if (!s.ownedThemes.Contains(Content.DefaultTheme)) s.ownedThemes.Insert(0, Content.DefaultTheme);
            if (!s.ownedThemes.Contains(s.theme)) s.theme = Content.DefaultTheme;
            if (!(s.lookSensitivity >= .4f && s.lookSensitivity <= 2f)) s.lookSensitivity = 1f;
            s.chapter = Math.Max(0, Math.Min(Chapters.All.Length, s.chapter));
            if (string.IsNullOrEmpty(s.storeName)) s.storeName = "麻辣湯の小さなお店";
            if (string.IsNullOrEmpty(s.weather)) s.weather = "sunny";
            if (string.IsNullOrEmpty(s.dailyEvent)) s.dailyEvent = "none";
            if (s.shopLevel < 1) s.shopLevel = 1;
            foreach (var o in s.orders.Where(o => o != null))
            {
                o.requested ??= new List<string>(); o.selected ??= new List<string>(); o.dropTimes ??= new List<float>();
                o.condiments ??= new List<string>(); o.requestedCondiments ??= new List<string>(); o.dislikedCondiments ??= new List<string>();
                o.condiments.RemoveAll(IsUnknownCondiment);
                o.requestedCondiments.RemoveAll(IsUnknownCondiment);
                o.dislikedCondiments.RemoveAll(IsUnknownCondiment);
                o.report ??= new QualityReport(); o.report.comments ??= new List<string>();
                o.reaction ??= "";
                o.recipe ??= ""; o.recipeRequest ??= ""; o.avoidCategory ??= ""; o.pairings ??= new List<string>();
                while (o.dropTimes.Count < o.selected.Count) o.dropTimes.Add(-1);
            }
            foreach (var o in s.orders.Where(o => o != null))
            {
                // 食事の表示状態は保存の途中で欠けても安全に戻せるため、拒否せず正規化する。
                if (o.stage == OrderStage.Served && o.visualState != "eat" && o.visualState != "leave") o.visualState = o.takeout || o.seat < 0 ? "leave" : "eat";
                if (o.stage == OrderStage.Cancelled) o.visualState = "leave";
            }
            if (s.furniture != null && s.furniture.All(f => f != null && Content.Furniture.Any(d => d.Id == f.defId)))
            {
                var validSeats = new HashSet<int>(DiningLayout.Seats(s).Select(x => x.SeatId));
                foreach (var o in s.orders.Where(o => o != null && o.seat >= 0 && !validSeats.Contains(o.seat))) o.seat = -1;
            }
            MealSaveNormalization.Normalize(s, IngredientCatalog.Load());
            foreach (var r in s.regulars.Where(r => r != null))
            {
                r.favorites ??= new List<string>(); r.likedCondiments ??= new List<string>(); r.dislikedCondiments ??= new List<string>();
                r.likedCondiments.RemoveAll(IsUnknownCondiment);
                r.dislikedCondiments.RemoveAll(IsUnknownCondiment);
            }
            foreach (var d in s.signatures.Where(d => d != null))
            {
                d.ingredients ??= new List<string>(); d.condiments ??= new List<string>();
                d.condiments.RemoveAll(IsUnknownCondiment);
            }
        }

        /// <summary>定義のない薬味を除去するための共通判定です。</summary>
        private static bool IsUnknownCondiment(string id) => !Content.TryCondiment(id, out _);

        /// <summary>保存時にも未知の薬味や欠けたリストを拒否します。</summary>
        internal static bool HasUnknownCondiments(List<string> ids)
        {
            if (ids == null) return true;
            for (int index = 0; index < ids.Count; index++)
                if (IsUnknownCondiment(ids[index])) return true;
            return false;
        }

        private static void FromVersion1(GameState s)
        {
            if (s.furniture.Count == 0)
            {
                int uid = Math.Max(1, s.nextFurnitureUid);
                s.furniture.AddRange(DiningLayout.DefaultLayout(ref uid));
                s.nextFurnitureUid = uid;
            }
            if (!s.tutorialComplete)
            {
                // v1 の練習手順は v2 と工程が違うため、初日の練習は最初からやり直す。
                s.phase = DayPhase.Preparation;
                s.tutorialStep = 0;
                s.daySeconds = 0; s.spawnedToday = 0;
                s.moneyFen -= s.orders.Where(o => o.stage != OrderStage.Cancelled).Sum(o => o.paidFen);
                s.orders.Clear();
                s.report = new DailyReport();
                s.ingredients.settledOrderIds.Clear();
                return;
            }
            s.tutorialStep = RestaurantSimulation.TutorialFinished;
            if (s.phase == DayPhase.Open || s.phase == DayPhase.Closing)
            {
                // v1 は受付時に会計しており v2 の工程に対応しないため、営業中の注文は返金して閉店扱いにする。
                foreach (var o in s.orders.Where(o => o.stage != OrderStage.Served && o.stage != OrderStage.Cancelled))
                {
                    s.moneyFen -= o.paidFen; s.report.revenueFen -= o.paidFen; s.report.refundsFen += o.paidFen; s.report.lost++;
                    o.stage = OrderStage.Cancelled;
                }
                // v1 は閉店処理で固定費を徴収していたため、営業中の移行でもその日の固定費を一度だけ精算する。
                s.report.fixedCostFen = 1200 + (s.pots - 1) * 300;
                s.moneyFen -= s.report.fixedCostFen;
                s.phase = DayPhase.Report;
            }
            foreach (var o in s.orders)
            {
                o.style = o.type == "セルフ式" ? OrderStyle.SelfServe : OrderStyle.Ordered;
                o.soup = SoupNames.TryGetValue(o.soup ?? "", out var soup) ? soup : "mala";
                o.chosenSoup = SoupNames.TryGetValue(o.chosenSoup ?? "", out var chosen) ? chosen : "";
                o.archetype = "office";
                o.visualState = "leave";
            }
        }
    }
}
