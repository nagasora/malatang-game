using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    /// <summary>スープの定義。</summary>
    public sealed class SoupDefinition
    {
        public string Id, Name, Description;
        /// <summary>表示色（RGB 0〜1）。</summary>
        public float R, G, B;
        /// <summary>理想のスープ量の範囲（ml）。汁なしは 0〜40。</summary>
        public float IdealMinMl, IdealMaxMl;
        public int UnlockLevel;
        /// <summary>1杯あたりのスープ原価（分）。</summary>
        public long CostFen;
    }

    /// <summary>仕上げにかける調味料・薬味の定義。</summary>
    public sealed class CondimentDefinition
    {
        public string Id, Name;
        public float R, G, B;
        public int UnlockLevel;
        public long CostFen;
    }

    /// <summary>家具の種類。</summary>
    public enum FurnitureKind { Table, Decoration, Wall }

    /// <summary>客席に置く家具・装飾の定義。</summary>
    public sealed class FurnitureDefinition
    {
        public string Id, Name, Description;
        public FurnitureKind Kind;
        /// <summary>占有セル数（回転0のときの幅・奥行）。壁装飾は 0。</summary>
        public int Width, Depth;
        public int Seats;
        /// <summary>雰囲気ポイント。客の忍耐・評判・チップに効く。</summary>
        public int Ambience;
        public long PriceFen;
        public int UnlockLevel;
    }

    /// <summary>スタッフの役割。</summary>
    public enum StaffRole { Cashier, Server, Cook, Courier }

    /// <summary>雇用できるスタッフの定義。</summary>
    public sealed class StaffDefinition
    {
        public StaffRole Role;
        public string Title, Description;
        public long DailyWageFen, HireFeeFen;
        public int UnlockLevel;
        /// <summary>1作業あたりの所要秒（研修レベル1）。</summary>
        public float BaseTaskSeconds;
    }

    /// <summary>設備の定義。</summary>
    public sealed class EquipmentDefinition
    {
        public string Id, Name, Description;
        public int MaxLevel;
        /// <summary>レベル n→n+1 の価格（分）。添字は現在レベル。</summary>
        public long[] PriceFen;
        /// <summary>レベル n→n+1 に必要な店舗レベル。添字は現在レベル。</summary>
        public int[] RequiredShopLevel;
    }

    /// <summary>客のタイプ（性格・好み）。</summary>
    public sealed class CustomerArchetype
    {
        public string Id, Label;
        /// <summary>カテゴリーごとの選びやすさ。</summary>
        public Dictionary<string, float> CategoryWeights;
        public int MinItems, MaxItems, MinSpice, MaxSpice;
        public Dictionary<string, float> SoupWeights;
        /// <summary>基本の忍耐（秒）。</summary>
        public float Patience;
        /// <summary>セルフ式（自分で具材を取る）になる確率。</summary>
        public float SelfServeChance;
        public float TakeoutChance;
        public string[] LikedCondiments, DislikedCondiments;
        /// <summary>出現の重み。</summary>
        public float SpawnWeight;
        public bool Child;
        /// <summary>評価の厳しさ（1 が標準）。</summary>
        public float Strictness = 1f;
        /// <summary>図鑑の説明。</summary>
        public string Description = "";
        /// <summary>チップの気前のよさ（1 が標準、0 はチップを払わない）。</summary>
        public float Generosity = 1f;
        /// <summary>不満なときに値引きを求める強さ（1 が標準、0 は求めない）。</summary>
        public float Complaint = 1f;
        /// <summary>支払い方法の重み（現金・QR・カード）。</summary>
        public float CashWeight = 1f, QrWeight = 1f, CardWeight = .3f;
        /// <summary>時間帯（11時〜19時台の9区分）ごとの来店しやすさの倍率。null は終日一定。</summary>
        public float[] HourWeights;
        /// <summary>食べられない具材のカテゴリー。</summary>
        public string[] ForbiddenCategories = new string[0];
        /// <summary>ふたり連れで来るか（カップル）。</summary>
        public bool Pair;
        /// <summary>出現し始める店舗レベル。</summary>
        public int MinShopLevel = 1;

        /// <summary>1日の進み具合（0〜1）での来店しやすさの倍率。</summary>
        public float HourWeight(float dayFraction)
        {
            if (HourWeights == null || HourWeights.Length == 0) return 1f;
            int index = Math.Max(0, Math.Min(HourWeights.Length - 1, (int)(dayFraction * HourWeights.Length)));
            return HourWeights[index];
        }
    }

    /// <summary>店の格付け（星）。</summary>
    /// <summary>内装テーマ（壁・床・照明・装飾の組み合わせ）。見た目は表示層が ID から作る。</summary>
    public sealed class ThemeDefinition
    {
        public string Id, Name, Description;
        /// <summary>雰囲気ポイントへの加算。</summary>
        public int Ambience;
        public long PriceFen;
        /// <summary>購入に必要な「店の物語」の完了章数。</summary>
        public int ChapterRequired;
    }

    public sealed class RankDefinition
    {
        public int Stars;
        public string Title, Description;
        public float MinPoints;
    }

    /// <summary>天気。</summary>
    public sealed class WeatherDefinition
    {
        public string Id, Name;
        public float DemandMultiplier;
        /// <summary>辛さ要望への加算。</summary>
        public int SpiceShift;
        public string FavoredSoup;
        public float TakeoutBonus;
    }

    /// <summary>日替わりイベント。</summary>
    public sealed class DailyEventDefinition
    {
        public string Id, Name, Description;
        public int MinDay;
        public float Weight;
        public float DemandMultiplier = 1f;
        /// <summary>仕入れ価格の倍率。</summary>
        public float PurchaseMultiplier = 1f;
    }

    /// <summary>実績の定義。</summary>
    public sealed class AchievementDefinition
    {
        public string Id, Name, Description;
        public Func<GameState, bool> Condition;
    }

    /// <summary>ゲーム内容（スープ・調味料・家具・スタッフ・客・天気・イベント・実績）の静的カタログ。</summary>
    public static class Content
    {
        public const int MaxShopLevel = 15;
        public const string ChiliOil = "chili_oil";

        public static readonly SoupDefinition[] Soups =
        {
            new SoupDefinition { Id = "mala", Name = "麻辣", Description = "花椒と唐辛子が香る看板スープ", R = .66f, G = .14f, B = .07f, IdealMinMl = 300, IdealMaxMl = 380, UnlockLevel = 1, CostFen = 60 },
            new SoupDefinition { Id = "baitang", Name = "骨湯（白湯）", Description = "豚骨と鶏ガラのまろやかな白濁スープ", R = .95f, G = .88f, B = .72f, IdealMinMl = 300, IdealMaxMl = 380, UnlockLevel = 1, CostFen = 60 },
            new SoupDefinition { Id = "tomato", Name = "トマト", Description = "完熟トマトの甘酸っぱいスープ", R = .9f, G = .3f, B = .13f, IdealMinMl = 300, IdealMaxMl = 380, UnlockLevel = 1, CostFen = 70 },
            new SoupDefinition { Id = "sour", Name = "酸菜", Description = "発酵白菜の爽やかな酸味", R = .86f, G = .78f, B = .42f, IdealMinMl = 300, IdealMaxMl = 380, UnlockLevel = 3, CostFen = 70 },
            new SoupDefinition { Id = "mushroom", Name = "きのこ", Description = "干し椎茸ときのこの旨味たっぷり", R = .55f, G = .38f, B = .2f, IdealMinMl = 300, IdealMaxMl = 380, UnlockLevel = 5, CostFen = 80 },
            new SoupDefinition { Id = "dry", Name = "麻辣拌（汁なし）", Description = "ごまだれで和える汁なし麻辣", R = .62f, G = .32f, B = .14f, IdealMinMl = 0, IdealMaxMl = 40, UnlockLevel = 7, CostFen = 50 },
        };

        public static readonly CondimentDefinition[] Condiments =
        {
            new CondimentDefinition { Id = ChiliOil, Name = "ラー油", R = .8f, G = .12f, B = .05f, UnlockLevel = 1, CostFen = 10 },
            new CondimentDefinition { Id = "sesame_paste", Name = "ごまだれ", R = .74f, G = .55f, B = .32f, UnlockLevel = 1, CostFen = 15 },
            new CondimentDefinition { Id = "vinegar", Name = "黒酢", R = .25f, G = .14f, B = .08f, UnlockLevel = 1, CostFen = 5 },
            new CondimentDefinition { Id = "scallion", Name = "青ねぎ", R = .35f, G = .7f, B = .25f, UnlockLevel = 1, CostFen = 5 },
            new CondimentDefinition { Id = "garlic", Name = "にんにく", R = .95f, G = .92f, B = .8f, UnlockLevel = 2, CostFen = 8 },
            new CondimentDefinition { Id = "coriander", Name = "香菜（パクチー）", R = .25f, G = .6f, B = .2f, UnlockLevel = 2, CostFen = 10 },
            new CondimentDefinition { Id = "peanut", Name = "砕きピーナッツ", R = .85f, G = .66f, B = .4f, UnlockLevel = 4, CostFen = 10 },
            new CondimentDefinition { Id = "sichuan_pepper", Name = "花椒", R = .5f, G = .2f, B = .12f, UnlockLevel = 6, CostFen = 12 },
        };

        public static readonly FurnitureDefinition[] Furniture =
        {
            new FurnitureDefinition { Id = "table_round2", Name = "丸テーブル（2席）", Description = "小さな丸テーブルと丸椅子", Kind = FurnitureKind.Table, Width = 2, Depth = 2, Seats = 2, Ambience = 0, PriceFen = 30000, UnlockLevel = 1 },
            new FurnitureDefinition { Id = "table_counter3", Name = "カウンター席（3席）", Description = "壁沿いに置ける細長い席", Kind = FurnitureKind.Table, Width = 3, Depth = 1, Seats = 3, Ambience = 0, PriceFen = 45000, UnlockLevel = 2 },
            new FurnitureDefinition { Id = "table_square4", Name = "四角テーブル（4席）", Description = "家族や団体向けの席", Kind = FurnitureKind.Table, Width = 3, Depth = 2, Seats = 4, Ambience = 1, PriceFen = 60000, UnlockLevel = 3 },
            new FurnitureDefinition { Id = "table_booth4", Name = "ボックス席（4席）", Description = "ゆったり座れる赤いソファ席", Kind = FurnitureKind.Table, Width = 3, Depth = 2, Seats = 4, Ambience = 4, PriceFen = 120000, UnlockLevel = 7 },
            new FurnitureDefinition { Id = "plant", Name = "観葉植物", Description = "緑があると落ち着く", Kind = FurnitureKind.Decoration, Width = 1, Depth = 1, Ambience = 2, PriceFen = 8000, UnlockLevel = 1 },
            new FurnitureDefinition { Id = "lantern_stand", Name = "灯籠", Description = "柔らかな灯りの石灯籠", Kind = FurnitureKind.Decoration, Width = 1, Depth = 1, Ambience = 3, PriceFen = 15000, UnlockLevel = 2 },
            new FurnitureDefinition { Id = "maneki", Name = "招き猫", Description = "お客さんを招く縁起物", Kind = FurnitureKind.Decoration, Width = 1, Depth = 1, Ambience = 4, PriceFen = 20000, UnlockLevel = 3 },
            new FurnitureDefinition { Id = "bamboo", Name = "竹の鉢", Description = "涼やかな竹の鉢植え", Kind = FurnitureKind.Decoration, Width = 1, Depth = 1, Ambience = 3, PriceFen = 12000, UnlockLevel = 4 },
            new FurnitureDefinition { Id = "fish_tank", Name = "金魚の水槽", Description = "眺めていると待ち時間も楽しい", Kind = FurnitureKind.Decoration, Width = 1, Depth = 1, Ambience = 5, PriceFen = 35000, UnlockLevel = 5 },
            new FurnitureDefinition { Id = "folding_screen", Name = "屏風", Description = "山水画の屏風", Kind = FurnitureKind.Decoration, Width = 2, Depth = 1, Ambience = 6, PriceFen = 40000, UnlockLevel = 6 },
            new FurnitureDefinition { Id = "poster_menu", Name = "メニューポスター", Description = "人気の具材を紹介", Kind = FurnitureKind.Wall, Ambience = 2, PriceFen = 6000, UnlockLevel = 1 },
            new FurnitureDefinition { Id = "calligraphy", Name = "書「福」", Description = "福を呼ぶ書", Kind = FurnitureKind.Wall, Ambience = 3, PriceFen = 10000, UnlockLevel = 2 },
            new FurnitureDefinition { Id = "red_knot", Name = "中国結び", Description = "赤い飾り結び", Kind = FurnitureKind.Wall, Ambience = 2, PriceFen = 7000, UnlockLevel = 2 },
            new FurnitureDefinition { Id = "paper_cut", Name = "切り紙細工", Description = "窓に貼る赤い切り紙", Kind = FurnitureKind.Wall, Ambience = 3, PriceFen = 9000, UnlockLevel = 4 },
            new FurnitureDefinition { Id = "television", Name = "テレビ", Description = "待ち時間の退屈しのぎ", Kind = FurnitureKind.Wall, Ambience = 4, PriceFen = 30000, UnlockLevel = 5 },
            new FurnitureDefinition { Id = "neon_sign", Name = "ネオン看板", Description = "「麻辣湯」が光る看板", Kind = FurnitureKind.Wall, Ambience = 7, PriceFen = 60000, UnlockLevel = 8 },
        };

        public static readonly StaffDefinition[] Staff =
        {
            new StaffDefinition { Role = StaffRole.Server, Title = "ホール係", Description = "完成した麻辣湯を運び、空いた席を片付けます。", DailyWageFen = 9000, HireFeeFen = 20000, UnlockLevel = 3, BaseTaskSeconds = 6 },
            new StaffDefinition { Role = StaffRole.Cashier, Title = "レジ係", Description = "注文を受け、具材を取り、計量と会計を行います。", DailyWageFen = 9000, HireFeeFen = 25000, UnlockLevel = 4, BaseTaskSeconds = 8 },
            new StaffDefinition { Role = StaffRole.Cook, Title = "調理補助", Description = "空いた鍋で茹で、湯切りと仕上げを行います。", DailyWageFen = 12000, HireFeeFen = 40000, UnlockLevel = 6, BaseTaskSeconds = 5 },
            new StaffDefinition { Role = StaffRole.Courier, Title = "配達係", Description = "完成した出前をスクーターで届けます。45秒以内なら温かいまま。", DailyWageFen = 10000, HireFeeFen = 30000, UnlockLevel = 5, BaseTaskSeconds = 4 },
        };

        public static readonly EquipmentDefinition[] Equipment =
        {
            new EquipmentDefinition { Id = "pot", Name = "調理鍋", Description = "同時に茹でられる数が増えます。", MaxLevel = 4, PriceFen = new long[] { 0, 40000, 80000, 160000 }, RequiredShopLevel = new[] { 0, 2, 4, 7 } },
            new EquipmentDefinition { Id = "storage", Name = "保管棚", Description = "食材ごとの保管上限が30個増えます。", MaxLevel = 4, PriceFen = new long[] { 0, 20000, 40000, 60000 }, RequiredShopLevel = new[] { 0, 2, 3, 5 } },
            new EquipmentDefinition { Id = "fridge", Name = "業務用冷蔵庫", Description = "食材の鮮度が1日長持ちします。", MaxLevel = 2, PriceFen = new long[] { 0, 90000 }, RequiredShopLevel = new[] { 0, 4 } },
            new EquipmentDefinition { Id = "stove", Name = "高火力コンロ", Description = "茹で上がりが25%速くなります。", MaxLevel = 2, PriceFen = new long[] { 0, 120000 }, RequiredShopLevel = new[] { 0, 5 } },
            new EquipmentDefinition { Id = "expansion", Name = "客席の拡張", Description = "客席のフロアを2列広げます。", MaxLevel = 3, PriceFen = new long[] { 0, 200000, 400000 }, RequiredShopLevel = new[] { 0, 5, 9 } },
        };

        private static Dictionary<string, float> W(params (string key, float weight)[] items) => items.ToDictionary(x => x.key, x => x.weight);

        //                                         11  12  13  14  15  16  17  18  19時
        private static readonly float[] Lunch = { 1.3f, 2.2f, 1.6f, .7f, .5f, .5f, .8f, 1.2f, .9f };
        private static readonly float[] AfterSchool = { .3f, .6f, .5f, .7f, 1.6f, 2f, 1.6f, 1f, .6f };
        private static readonly float[] Evening = { .4f, .6f, .6f, .6f, .7f, .9f, 1.4f, 1.8f, 1.8f };
        private static readonly float[] LateNight = { 0, 0, 0, 0, 0, .1f, .5f, 1.6f, 3f };
        private static readonly float[] Daytime = { 1.2f, 1.2f, 1.3f, 1.3f, 1.2f, 1f, .7f, .4f, .3f };

        public static readonly CustomerArchetype[] Archetypes =
        {
            new CustomerArchetype { Id = "office", Label = "会社員", SpawnWeight = 3, HourWeights = Lunch, Description = "昼休みに急いで食べに来る。お昼どきに多い。",
                CategoryWeights = W(("noodle", 3), ("vegetable", 2), ("meat", 2), ("processed", 2), ("soy", 1), ("mushroom", 1)),
                MinItems = 4, MaxItems = 6, MinSpice = 1, MaxSpice = 3, SoupWeights = W(("mala", 3), ("baitang", 1), ("tomato", 1), ("sour", 1)),
                Patience = 140, SelfServeChance = .55f, TakeoutChance = .2f, LikedCondiments = new[] { "vinegar", "garlic" }, DislikedCondiments = new string[0],
                CashWeight = .5f, QrWeight = 2f, CardWeight = .8f },
            new CustomerArchetype { Id = "student", Label = "学生", SpawnWeight = 2.5f, HourWeights = AfterSchool, Description = "放課後にお腹をすかせてやって来る。麺が大好き。お財布は小銭が多め。",
                CategoryWeights = W(("noodle", 4), ("processed", 3), ("vegetable", 1), ("soy", 1)),
                MinItems = 3, MaxItems = 5, MinSpice = 1, MaxSpice = 4, SoupWeights = W(("mala", 3), ("tomato", 2), ("dry", 1)),
                Patience = 170, SelfServeChance = .7f, TakeoutChance = .15f, LikedCondiments = new[] { "sesame_paste" }, DislikedCondiments = new[] { "coriander" },
                Generosity = .5f, CashWeight = 1.5f, QrWeight = 2f, CardWeight = 0 },
            new CustomerArchetype { Id = "spicy", Label = "辛党", SpawnWeight = 1.5f, Description = "辛さ4〜5しか認めない。花椒のしびれも大好物。",
                CategoryWeights = W(("meat", 3), ("other", 2), ("noodle", 2), ("vegetable", 1), ("mushroom", 1)),
                MinItems = 4, MaxItems = 6, MinSpice = 4, MaxSpice = 5, SoupWeights = W(("mala", 5), ("dry", 2)),
                Patience = 160, SelfServeChance = .5f, TakeoutChance = .1f, LikedCondiments = new[] { "sichuan_pepper", "garlic" }, DislikedCondiments = new string[0] },
            new CustomerArchetype { Id = "healthy", Label = "健康志向", SpawnWeight = 1.5f, HourWeights = Daytime, Description = "野菜ときのこをたっぷり。辛いのは苦手。",
                CategoryWeights = W(("vegetable", 5), ("mushroom", 3), ("soy", 3), ("seafood", 1)),
                MinItems = 5, MaxItems = 7, MinSpice = 0, MaxSpice = 1, SoupWeights = W(("baitang", 3), ("mushroom", 3), ("tomato", 2)),
                Patience = 170, SelfServeChance = .6f, TakeoutChance = .2f, LikedCondiments = new[] { "coriander", "vinegar" }, DislikedCondiments = new[] { ChiliOil },
                QrWeight = 1.5f, CardWeight = .8f },
            new CustomerArchetype { Id = "meatlover", Label = "肉好き", SpawnWeight = 1.2f, Description = "とにかく肉。牛肉の火の通しにうるさい。",
                CategoryWeights = W(("meat", 5), ("processed", 2), ("noodle", 2), ("seafood", 1)),
                MinItems = 4, MaxItems = 6, MinSpice = 2, MaxSpice = 4, SoupWeights = W(("mala", 3), ("baitang", 2)),
                Patience = 150, SelfServeChance = .5f, TakeoutChance = .1f, LikedCondiments = new[] { "garlic", "sesame_paste" }, DislikedCondiments = new string[0] },
            new CustomerArchetype { Id = "family", Label = "親子連れ", SpawnWeight = 1, Child = true, HourWeights = Daytime, Description = "子どもは辛いのも香菜もだめ。トマトスープが人気。",
                CategoryWeights = W(("noodle", 3), ("processed", 3), ("vegetable", 2), ("soy", 1)),
                MinItems = 3, MaxItems = 5, MinSpice = 0, MaxSpice = 0, SoupWeights = W(("tomato", 4), ("baitang", 2)),
                Patience = 140, SelfServeChance = .3f, TakeoutChance = .05f, LikedCondiments = new[] { "sesame_paste" }, DislikedCondiments = new[] { ChiliOil, "coriander", "sichuan_pepper" },
                Generosity = 1.2f },
            new CustomerArchetype { Id = "elder", Label = "ご年配", SpawnWeight = 1, HourWeights = Daytime, Description = "のんびり待ってくれる。現金払いで、ていねいな接客を喜ぶ。",
                CategoryWeights = W(("soy", 3), ("vegetable", 3), ("mushroom", 2), ("seafood", 2), ("noodle", 1)),
                MinItems = 3, MaxItems = 5, MinSpice = 0, MaxSpice = 1, SoupWeights = W(("baitang", 4), ("mushroom", 2), ("sour", 1)),
                Patience = 220, SelfServeChance = .2f, TakeoutChance = .05f, LikedCondiments = new[] { "scallion" }, DislikedCondiments = new[] { "sichuan_pepper" },
                Generosity = 1.3f, Complaint = .6f, CashWeight = 4f, QrWeight = .2f, CardWeight = 0 },
            new CustomerArchetype { Id = "tourist", Label = "観光客", SpawnWeight = 1, HourWeights = Daytime, Description = "珍しい具材に興味しんしん。旅の気分で気前がいい。",
                CategoryWeights = W(("other", 2), ("seafood", 2), ("meat", 2), ("noodle", 2), ("vegetable", 1), ("mushroom", 1), ("processed", 1)),
                MinItems = 5, MaxItems = 7, MinSpice = 1, MaxSpice = 3, SoupWeights = W(("mala", 2), ("sour", 2), ("mushroom", 1), ("tomato", 1)),
                Patience = 180, SelfServeChance = .4f, TakeoutChance = .1f, LikedCondiments = new[] { "peanut", "coriander" }, DislikedCondiments = new string[0],
                Generosity = 1.6f, CashWeight = 1.5f, QrWeight = .5f, CardWeight = 1.2f },
            new CustomerArchetype { Id = "worker", Label = "作業員", SpawnWeight = 1.3f, HourWeights = Lunch, MinShopLevel = 2, Description = "現場からひと休み。量が多めで、待たされるのが苦手。",
                CategoryWeights = W(("meat", 3), ("noodle", 4), ("processed", 3), ("vegetable", 1)),
                MinItems = 6, MaxItems = 8, MinSpice = 2, MaxSpice = 4, SoupWeights = W(("mala", 3), ("baitang", 2)),
                Patience = 115, SelfServeChance = .6f, TakeoutChance = .15f, LikedCondiments = new[] { "garlic" }, DislikedCondiments = new string[0],
                CashWeight = 2.5f, QrWeight = 1f, CardWeight = 0 },
            new CustomerArchetype { Id = "rider", Label = "配達員", SpawnWeight = .9f, MinShopLevel = 3, Description = "いつも急いでいる持ち帰り専門。早く渡せば喜ぶ。",
                CategoryWeights = W(("noodle", 3), ("meat", 2), ("vegetable", 2), ("processed", 2)),
                MinItems = 4, MaxItems = 6, MinSpice = 1, MaxSpice = 3, SoupWeights = W(("mala", 2), ("dry", 2), ("tomato", 1)),
                Patience = 95, SelfServeChance = 0, TakeoutChance = 1, LikedCondiments = new string[0], DislikedCondiments = new string[0],
                Generosity = .7f, Complaint = .8f, CashWeight = .2f, QrWeight = 3f, CardWeight = 0 },
            new CustomerArchetype { Id = "nurse", Label = "夜勤明けの看護師", SpawnWeight = .8f, HourWeights = Daytime, MinShopLevel = 3, Description = "疲れた体にやさしいスープを求めて。白湯ときのこが好き。",
                CategoryWeights = W(("vegetable", 3), ("soy", 3), ("mushroom", 2), ("noodle", 2), ("seafood", 1)),
                MinItems = 4, MaxItems = 6, MinSpice = 0, MaxSpice = 2, SoupWeights = W(("baitang", 3), ("mushroom", 2), ("tomato", 1)),
                Patience = 160, SelfServeChance = .4f, TakeoutChance = .35f, LikedCondiments = new[] { "scallion" }, DislikedCondiments = new string[0],
                Generosity = 1.2f, QrWeight = 2f, CardWeight = .5f },
            new CustomerArchetype { Id = "couple", Label = "カップル", SpawnWeight = .9f, HourWeights = Evening, Pair = true, MinShopLevel = 2, Description = "ふたりで来店。同じテーブルに座りたい。雰囲気のいい店が好き。",
                CategoryWeights = W(("seafood", 2), ("vegetable", 2), ("noodle", 2), ("mushroom", 2), ("meat", 1)),
                MinItems = 4, MaxItems = 6, MinSpice = 1, MaxSpice = 3, SoupWeights = W(("tomato", 3), ("mala", 2), ("mushroom", 1)),
                Patience = 160, SelfServeChance = .6f, TakeoutChance = 0, LikedCondiments = new[] { "sesame_paste", "peanut" }, DislikedCondiments = new string[0],
                Generosity = 1.3f, QrWeight = 2f, CardWeight = 1f },
            new CustomerArchetype { Id = "vegetarian", Label = "ベジタリアン", SpawnWeight = .7f, MinShopLevel = 4, Description = "肉・魚介・加工品は一切食べない。間違えると大変なことに。",
                CategoryWeights = W(("vegetable", 5), ("mushroom", 3), ("soy", 3), ("noodle", 2), ("other", 1)),
                MinItems = 5, MaxItems = 7, MinSpice = 0, MaxSpice = 3, SoupWeights = W(("tomato", 3), ("mushroom", 3), ("sour", 1)),
                Patience = 170, SelfServeChance = .5f, TakeoutChance = .15f, LikedCondiments = new[] { "coriander", "vinegar", "peanut" }, DislikedCondiments = new string[0],
                ForbiddenCategories = new[] { "meat", "seafood", "processed" }, Generosity = 1.3f, Complaint = 1.4f, QrWeight = 1.5f, CardWeight = 1f },
            new CustomerArchetype { Id = "athlete", Label = "筋トレ好き", SpawnWeight = .8f, HourWeights = Evening, MinShopLevel = 3, Description = "たんぱく質命。肉と豆腐をとにかく多めに。",
                CategoryWeights = W(("meat", 5), ("soy", 4), ("vegetable", 2), ("seafood", 1)),
                MinItems = 5, MaxItems = 7, MinSpice = 1, MaxSpice = 3, SoupWeights = W(("baitang", 3), ("mala", 2)),
                Patience = 150, SelfServeChance = .6f, TakeoutChance = .25f, LikedCondiments = new[] { "garlic" }, DislikedCondiments = new[] { "sesame_paste" },
                QrWeight = 2f },
            new CustomerArchetype { Id = "bigeater", Label = "大食い", SpawnWeight = .5f, MinShopLevel = 5, Description = "ボウルいっぱい10品！重さで売上も大きい。",
                CategoryWeights = W(("noodle", 3), ("meat", 3), ("processed", 3), ("vegetable", 2), ("soy", 2), ("mushroom", 1)),
                MinItems = 9, MaxItems = 10, MinSpice = 2, MaxSpice = 4, SoupWeights = W(("mala", 3), ("baitang", 2)),
                Patience = 170, SelfServeChance = .7f, TakeoutChance = 0, LikedCondiments = new[] { "sesame_paste", "garlic" }, DislikedCondiments = new string[0],
                Generosity = 1.1f, CashWeight = 1.5f, QrWeight = 1f },
            new CustomerArchetype { Id = "foreigner", Label = "海外からの旅行者", SpawnWeight = .7f, HourWeights = Daytime, MinShopLevel = 4, Description = "初めての麻辣湯にわくわく。カード払いが多く、チップの習慣がある。",
                CategoryWeights = W(("meat", 2), ("vegetable", 2), ("noodle", 2), ("seafood", 2), ("mushroom", 1)),
                MinItems = 4, MaxItems = 6, MinSpice = 0, MaxSpice = 2, SoupWeights = W(("tomato", 2), ("mala", 2), ("baitang", 2)),
                Patience = 175, SelfServeChance = .3f, TakeoutChance = .1f, LikedCondiments = new[] { "peanut", "sesame_paste" }, DislikedCondiments = new string[0],
                Generosity = 2.2f, Complaint = .7f, CashWeight = .6f, QrWeight = 0, CardWeight = 3f },
            new CustomerArchetype { Id = "drunk", Label = "ほろ酔いの会社員", SpawnWeight = .9f, HourWeights = LateNight, MinShopLevel = 2, Description = "飲んだあとのしめの一杯。夜にだけ現れる。気前はいいが、からむと長い。",
                CategoryWeights = W(("noodle", 4), ("meat", 2), ("processed", 2), ("vegetable", 1)),
                MinItems = 3, MaxItems = 5, MinSpice = 2, MaxSpice = 5, SoupWeights = W(("mala", 3), ("sour", 2), ("baitang", 1)),
                Patience = 190, SelfServeChance = .2f, TakeoutChance = .1f, LikedCondiments = new[] { "vinegar", "garlic" }, DislikedCondiments = new string[0],
                Generosity = 1.8f, Complaint = 1.3f, CashWeight = 3f, QrWeight = 1f, CardWeight = .5f },
            new CustomerArchetype { Id = "celebrity", Label = "お忍びの芸能人", SpawnWeight = .15f, MinShopLevel = 6, Description = "帽子とサングラスで変装中。満足すればチップがすごい。",
                CategoryWeights = W(("seafood", 3), ("vegetable", 2), ("mushroom", 2), ("noodle", 1), ("soy", 1)),
                MinItems = 5, MaxItems = 7, MinSpice = 1, MaxSpice = 3, SoupWeights = W(("mushroom", 2), ("tomato", 2), ("mala", 2)),
                Patience = 150, SelfServeChance = 0, TakeoutChance = .3f, LikedCondiments = new[] { "sesame_paste" }, DislikedCondiments = new[] { "garlic" },
                Strictness = 1.15f, Generosity = 4f, Complaint = .5f, CashWeight = .5f, QrWeight = 1f, CardWeight = 2f },
            new CustomerArchetype { Id = "critic", Label = "グルメ評論家", SpawnWeight = 0, Strictness = 1.6f, Description = "記事ひとつで評判が大きく動く。チップは払わない。",
                CategoryWeights = W(("vegetable", 2), ("meat", 2), ("seafood", 2), ("noodle", 2), ("mushroom", 1), ("soy", 1), ("other", 1)),
                MinItems = 6, MaxItems = 7, MinSpice = 2, MaxSpice = 3, SoupWeights = W(("mala", 2), ("mushroom", 1), ("sour", 1)),
                Patience = 170, SelfServeChance = 0, TakeoutChance = 0, LikedCondiments = new[] { "sichuan_pepper", "vinegar" }, DislikedCondiments = new string[0],
                Generosity = 0, Complaint = 0, CashWeight = 1f, QrWeight = 0, CardWeight = 1f },
            new CustomerArchetype { Id = "influencer", Label = "インフルエンサー", SpawnWeight = 0, Strictness = 1.2f, Description = "映える一杯なら人気が急上昇。",
                CategoryWeights = W(("seafood", 3), ("vegetable", 2), ("noodle", 2), ("mushroom", 1)),
                MinItems = 5, MaxItems = 7, MinSpice = 1, MaxSpice = 2, SoupWeights = W(("tomato", 2), ("mala", 2), ("dry", 1)),
                Patience = 160, SelfServeChance = .5f, TakeoutChance = 0, LikedCondiments = new[] { "peanut", "sesame_paste" }, DislikedCondiments = new string[0],
                CashWeight = 0, QrWeight = 3f, CardWeight = .5f },
            new CustomerArchetype { Id = "judge", Label = "グランプリ審査員", SpawnWeight = 0, Strictness = 1.5f, Description = "麻辣湯グランプリの審査員。3人の平均点で賞が決まる。",
                CategoryWeights = W(("vegetable", 2), ("meat", 2), ("seafood", 2), ("noodle", 2), ("mushroom", 2), ("soy", 1), ("other", 1)),
                MinItems = 5, MaxItems = 7, MinSpice = 1, MaxSpice = 4, SoupWeights = W(("mala", 2), ("baitang", 1), ("tomato", 1), ("sour", 1), ("mushroom", 1)),
                Patience = 200, SelfServeChance = 0, TakeoutChance = 0, LikedCondiments = new[] { "sichuan_pepper", "scallion" }, DislikedCondiments = new string[0],
                Generosity = 0, Complaint = 0, CashWeight = 0, QrWeight = 0, CardWeight = 1f },
        };

        /// <summary>店の格付け。</summary>
        public static readonly RankDefinition[] Ranks =
        {
            new RankDefinition { Stars = 1, Title = "街角の小さな店", Description = "まだ知る人ぞ知るお店。", MinPoints = 0 },
            new RankDefinition { Stars = 2, Title = "近所で評判の店", Description = "近所の人が通ってくれるようになった。", MinPoints = 48 },
            new RankDefinition { Stars = 3, Title = "行列のできる店", Description = "昼どきには行列ができる人気店。", MinPoints = 62 },
            new RankDefinition { Stars = 4, Title = "街の名店", Description = "グルメガイドに載る名店。", MinPoints = 76 },
            new RankDefinition { Stars = 5, Title = "伝説の麻辣湯", Description = "遠方から客が訪れる伝説のお店。", MinPoints = 90 },
        };

        public static readonly WeatherDefinition[] Weathers =
        {
            new WeatherDefinition { Id = "sunny", Name = "晴れ", DemandMultiplier = 1.1f, SpiceShift = 0, FavoredSoup = "", TakeoutBonus = 0 },
            new WeatherDefinition { Id = "cloudy", Name = "くもり", DemandMultiplier = 1f, SpiceShift = 0, FavoredSoup = "", TakeoutBonus = 0 },
            new WeatherDefinition { Id = "rain", Name = "雨", DemandMultiplier = .8f, SpiceShift = 0, FavoredSoup = "baitang", TakeoutBonus = .2f },
            new WeatherDefinition { Id = "cold", Name = "寒波", DemandMultiplier = 1.2f, SpiceShift = 1, FavoredSoup = "mala", TakeoutBonus = 0 },
            new WeatherDefinition { Id = "hot", Name = "猛暑", DemandMultiplier = .9f, SpiceShift = -1, FavoredSoup = "tomato", TakeoutBonus = .1f },
        };

        public static readonly DailyEventDefinition[] DailyEvents =
        {
            new DailyEventDefinition { Id = "none", Name = "いつもの一日", Description = "特別なことはなさそうです。", MinDay = 1, Weight = 4 },
            new DailyEventDefinition { Id = "critic", Name = "評論家の来店", Description = "グルメ評論家が来るという噂。満足させれば評判が大きく上がります。", MinDay = 4, Weight = 1 },
            new DailyEventDefinition { Id = "group", Name = "団体客の予約", Description = "お昼に4人組の団体客が来ます。席を空けておきましょう。", MinDay = 3, Weight = 1.2f },
            new DailyEventDefinition { Id = "sale", Name = "仕入れ先のセール", Description = "今日の仕入れは3割引きです。", MinDay = 2, Weight = 1.2f, PurchaseMultiplier = .7f },
            new DailyEventDefinition { Id = "festival", Name = "商店街のお祭り", Description = "人通りが多く、お客さんが4割増えます。", MinDay = 5, Weight = .8f, DemandMultiplier = 1.4f },
            new DailyEventDefinition { Id = "influencer", Name = "インフルエンサー", Description = "人気の配信者が来るかも。高品質な一杯で人気が急上昇します。", MinDay = 6, Weight = .8f },
            new DailyEventDefinition { Id = "night_market", Name = "夜市", Description = "夕方から夜市が開かれ、夜のお客さんが増えます。", MinDay = 8, Weight = .7f, DemandMultiplier = 1.2f },
            new DailyEventDefinition { Id = "contest", Name = "麻辣湯グランプリ", Description = "審査員3人が来店します。平均点で金・銀・銅賞が決まり、賞金がもらえます。", MinDay = 14, Weight = 0 },
        };

        public static readonly AchievementDefinition[] Achievements =
        {
            new AchievementDefinition { Id = "rare_first", Name = "朝市の目利き", Description = "希少食材を初めて仕入れる", Condition = s => s.ingredients.rareObtainedIds.Count > 0 },
            new AchievementDefinition { Id = "rare_all", Name = "希少食材蒐集家", Description = "希少食材6種類をすべて入手する", Condition = s => MorningMarket.RareIds.All(s.ingredients.rareObtainedIds.Contains) },
            new AchievementDefinition { Id = "first_empty_soup", Name = "最後の一滴", Description = "はじめて完飲してもらう", Condition = s => s.stats.finishedSoup >= 1 },
            new AchievementDefinition { Id = "finished_food50", Name = "空っぽの器", Description = "累計50杯を完食してもらう", Condition = s => s.stats.finishedFood >= 50 },
            new AchievementDefinition { Id = "all_finished_day", Name = "みんな完食", Description = "1日10杯以上を提供し全員に完食してもらう", Condition = s => s.stats.allFinishedDays >= 1 },
            new AchievementDefinition { Id = "rare_served", Name = "特別な一杯", Description = "異なる希少食材を2種類以上入れた一杯を提供する", Condition = s => s.stats.rareCombinationBowls >= 1 },
            new AchievementDefinition { Id = "first_bowl", Name = "はじめの一杯", Description = "最初の麻辣湯を提供する", Condition = s => s.stats.served >= 1 },
            new AchievementDefinition { Id = "bowls_100", Name = "百杯の道", Description = "累計100杯を提供する", Condition = s => s.stats.served >= 100 },
            new AchievementDefinition { Id = "bowls_500", Name = "人気店", Description = "累計500杯を提供する", Condition = s => s.stats.served >= 500 },
            new AchievementDefinition { Id = "perfect", Name = "会心の一杯", Description = "品質95点以上の麻辣湯を作る", Condition = s => s.stats.perfectBowls >= 1 },
            new AchievementDefinition { Id = "perfect_20", Name = "職人の手", Description = "品質95点以上を20杯作る", Condition = s => s.stats.perfectBowls >= 20 },
            new AchievementDefinition { Id = "level_5", Name = "街の評判", Description = "店舗レベル5に到達する", Condition = s => s.shopLevel >= 5 },
            new AchievementDefinition { Id = "level_10", Name = "行列のできる店", Description = "店舗レベル10に到達する", Condition = s => s.shopLevel >= 10 },
            new AchievementDefinition { Id = "level_max", Name = "麻辣湯の名店", Description = "店舗レベル15に到達する", Condition = s => s.shopLevel >= MaxShopLevel },
            new AchievementDefinition { Id = "all_ingredients", Name = "食材コンプリート", Description = "全69種類の食材を解禁する", Condition = s => s.ingredients.unlockedIds.Count >= 69 },
            new AchievementDefinition { Id = "revenue_10k", Name = "一万元の店", Description = "累計売上1万元を達成する", Condition = s => s.ingredients.settledRevenueFen >= 1000000 },
            new AchievementDefinition { Id = "critic", Name = "評論家のお墨付き", Description = "評論家を満足させる", Condition = s => s.stats.criticsSatisfied >= 1 },
            new AchievementDefinition { Id = "team", Name = "頼れる仲間", Description = "3人のスタッフを雇う", Condition = s => s.staff.Count >= 3 },
            new AchievementDefinition { Id = "decorator", Name = "こだわりの内装", Description = "雰囲気ポイントを30以上にする", Condition = s => s.stats.bestAmbience >= 30 },
            new AchievementDefinition { Id = "regulars_5", Name = "なじみの顔", Description = "常連客を5人つくる", Condition = s => s.regulars.Count >= 5 },
            new AchievementDefinition { Id = "signature", Name = "看板メニュー", Description = "名物メニューを20杯売る", Condition = s => s.signatures.Sum(x => x.timesSold) >= 20 },
            new AchievementDefinition { Id = "tips", Name = "心づけ", Description = "チップを累計100元受け取る", Condition = s => s.stats.tipsFen >= 10000 },
            new AchievementDefinition { Id = "day_30", Name = "ひと月の営業", Description = "30日目を迎える", Condition = s => s.day >= 30 },
            new AchievementDefinition { Id = "no_loss_day", Name = "取りこぼしゼロ", Description = "10杯以上提供し、取消なしで一日を終える", Condition = s => s.stats.flawlessDays >= 1 },
            new AchievementDefinition { Id = "spicy_master", Name = "激辛の匠", Description = "辛さ5の注文を完璧に作る", Condition = s => s.stats.spicyPerfect >= 1 },
            new AchievementDefinition { Id = "expansion", Name = "大きなお店", Description = "客席を最大まで拡張する", Condition = s => s.EquipmentLevel("expansion") >= 3 },
            new AchievementDefinition { Id = "exact_change_30", Name = "レジの達人", Description = "おつりをぴったり30回渡す", Condition = s => s.stats.exactChange >= 30 },
            new AchievementDefinition { Id = "combo_10", Name = "絶好調", Description = "満足度の高い一杯を10杯続ける", Condition = s => s.stats.bestCombo >= 10 },
            new AchievementDefinition { Id = "combo_25", Name = "神がかった厨房", Description = "満足度の高い一杯を25杯続ける", Condition = s => s.stats.bestCombo >= 25 },
            new AchievementDefinition { Id = "tips_1000", Name = "愛されるお店", Description = "チップを累計1,000元受け取る", Condition = s => s.stats.tipsFen >= 100000 },
            new AchievementDefinition { Id = "missions_10", Name = "今日の目標", Description = "ミッションを10回達成する", Condition = s => s.stats.missionsCompleted >= 10 },
            new AchievementDefinition { Id = "missions_50", Name = "目標の鬼", Description = "ミッションを50回達成する", Condition = s => s.stats.missionsCompleted >= 50 },
            new AchievementDefinition { Id = "contest_any", Name = "グランプリ入賞", Description = "麻辣湯グランプリで入賞する", Condition = s => s.stats.contestGold + s.stats.contestSilver + s.stats.contestBronze >= 1 },
            new AchievementDefinition { Id = "contest_gold", Name = "金賞の一杯", Description = "麻辣湯グランプリで金賞を取る", Condition = s => s.stats.contestGold >= 1 },
            new AchievementDefinition { Id = "contest_gold_3", Name = "殿堂入り", Description = "麻辣湯グランプリで金賞を3回取る", Condition = s => s.stats.contestGold >= 3 },
            new AchievementDefinition { Id = "all_customers", Name = "お客さん図鑑コンプリート", Description = "すべてのタイプのお客さんをもてなす", Condition = s => Archetypes.Where(a => a.Id != "judge").All(a => LifetimeStats.Get(s.stats.archetypesServed, a.Id) > 0) },
            new AchievementDefinition { Id = "vegetarian_10", Name = "やさしい厨房", Description = "ベジタリアンのお客さんを10回満足させる", Condition = s => s.stats.vegetarianPleased >= 10 },
            new AchievementDefinition { Id = "celebrity", Name = "サインをもらった", Description = "お忍びの芸能人をもてなす", Condition = s => s.stats.celebrities >= 1 },
            new AchievementDefinition { Id = "rush_25", Name = "大繁盛", Description = "1日に25杯提供する", Condition = s => s.stats.bestDayServed >= 25 },
            new AchievementDefinition { Id = "bowls_1000", Name = "千杯の店", Description = "累計1,000杯を提供する", Condition = s => s.stats.served >= 1000 },
            new AchievementDefinition { Id = "day_100", Name = "百日の歩み", Description = "100日目を迎える", Condition = s => s.day >= 100 },
            new AchievementDefinition { Id = "stars_5", Name = "伝説の麻辣湯", Description = "店の格付けで星5つになる", Condition = s => Progression.Stars(s) >= 5 },
            new AchievementDefinition { Id = "rich", Name = "大富豪", Description = "所持金10万元を達成する", Condition = s => s.moneyFen >= 10000000 },
            new AchievementDefinition { Id = "recipe_1", Name = "ひらめきの一杯", Description = "隠しレシピをひとつ見つける", Condition = s => s.recipes.Count >= 1 },
            new AchievementDefinition { Id = "recipe_8", Name = "レシピ研究家", Description = "隠しレシピを8つ見つける", Condition = s => s.recipes.Count >= 8 },
            new AchievementDefinition { Id = "recipe_all", Name = "麻辣湯大全", Description = "隠しレシピをすべて見つける", Condition = s => s.recipes.Count >= Recipes.All.Length },
            new AchievementDefinition { Id = "theme_1", Name = "模様替え", Description = "内装テーマを買う", Condition = s => s.ownedThemes.Count >= 2 },
            new AchievementDefinition { Id = "theme_all", Name = "どれも自慢の店構え", Description = "すべての内装テーマをそろえる", Condition = s => s.ownedThemes.Count >= Themes.Length },
            new AchievementDefinition { Id = "chapter_3", Name = "評判の店", Description = "「店の物語」第3章を完了する", Condition = s => s.chapter >= 3 },
            new AchievementDefinition { Id = "chapter_all", Name = "物語の続きへ", Description = "「店の物語」をすべて完了する", Condition = s => s.chapter >= Chapters.All.Length },
            new AchievementDefinition { Id = "skill_first", Name = "店主の修行", Description = "店主スキルを覚える", Condition = s => s.skills.Any(x => x.count > 0) },
            new AchievementDefinition { Id = "skill_master", Name = "達人の域", Description = "店主スキルをひとつ最大まで上げる", Condition = s => Skills.All.Any(d => Skills.Rank(s, d.Id) >= d.MaxRank) },
            new AchievementDefinition { Id = "grade_s", Name = "最高の一日", Description = "営業の評価でSを取る", Condition = s => s.stats.gradeS >= 1 },
            new AchievementDefinition { Id = "grade_s10", Name = "名店の日常", Description = "営業の評価でSを10回取る", Condition = s => s.stats.gradeS >= 10 },
            new AchievementDefinition { Id = "seasonal_30", Name = "旬を味わう店", Description = "旬の具材が入った一杯を30杯出す", Condition = s => s.stats.seasonalServed >= 30 },
            new AchievementDefinition { Id = "four_seasons", Name = "四季をめぐる", Description = "春夏秋冬をひと巡りする（57日目を迎える）", Condition = s => s.day >= Seasons.Length * 4 + 1 },
            new AchievementDefinition { Id = "omakase_1", Name = "おまかせあれ", Description = "おまかせ注文で品質85点以上を出す", Condition = s => s.stats.omakaseGreat >= 1 },
            new AchievementDefinition { Id = "omakase_30", Name = "目利きの店主", Description = "おまかせ注文で品質85点以上を30回出す", Condition = s => s.stats.omakaseGreat >= 30 },
            new AchievementDefinition { Id = "rainbow", Name = "虹色の一杯", Description = "5色以上の具材が入った一杯を出す", Condition = s => s.stats.rainbowBowls >= 1 },
            new AchievementDefinition { Id = "pairing_5", Name = "食べ合わせ上手", Description = "相性のいい組み合わせを5つ見つける", Condition = s => s.pairings.Count >= 5 },
            new AchievementDefinition { Id = "pairing_all", Name = "相性博士", Description = "相性のいい組み合わせをすべて見つける", Condition = s => s.pairings.Count >= Pairings.All.Length },
            new AchievementDefinition { Id = "album_full", Name = "自慢の12杯", Description = "一杯アルバムを12杯で埋める", Condition = s => s.album.Count >= BowlAlbum.Limit },
            new AchievementDefinition { Id = "no_complaint_300", Name = "クレームゼロ", Description = "値引きなしで累計300杯を提供する", Condition = s => s.stats.served >= 300 && s.stats.complaints == 0 },
        };

        /// <summary>既定の内装テーマ ID。</summary>
        public const string DefaultTheme = "shitamachi";

        /// <summary>内装テーマ。章を進めると買えるようになり、雰囲気が上がる。</summary>
        public static readonly ThemeDefinition[] Themes =
        {
            new ThemeDefinition { Id = DefaultTheme, Name = "下町食堂", Description = "白いタイルと花柄の壁紙。開店以来の素朴な店構え。", Ambience = 0, PriceFen = 0, ChapterRequired = 0 },
            new ThemeDefinition { Id = "lantern", Name = "赤提灯の夜市", Description = "提灯の列と木の屋台。夜市のにぎわいを店の中に。", Ambience = 4, PriceFen = 150000, ChapterRequired = 1 },
            new ThemeDefinition { Id = "cafe", Name = "モダンカフェ", Description = "白木とミントグリーン。若い人や女性客に人気。", Ambience = 6, PriceFen = 300000, ChapterRequired = 2 },
            new ThemeDefinition { Id = "teahouse", Name = "老舗の茶館", Description = "濃い木目と格子窓。落ち着いた老舗の風格。", Ambience = 8, PriceFen = 500000, ChapterRequired = 3 },
            new ThemeDefinition { Id = "neon", Name = "ネオン屋台", Description = "ピンクと水色のネオン管。夜の街でいちばん目立つ。", Ambience = 10, PriceFen = 800000, ChapterRequired = 4 },
        };

        public static readonly string[] GivenNames =
        {
            "リン", "チェン", "メイ", "ワン", "リュウ", "シャオ", "ヤン", "ジャン", "ホァン", "チャオ", "ウー", "スン",
            "佐藤", "鈴木", "高橋", "田中", "伊藤", "渡辺", "山本", "中村", "小林", "加藤", "吉田", "山田",
            "ミン", "ユイ", "ハル", "ソラ", "アオイ", "レン", "ヒナ", "カイ"
        };

        /// <summary>海外からの旅行者の名前。</summary>
        public static readonly string[] ForeignNames = { "エマ", "ノア", "リアム", "ソフィア", "ルーカス", "オリビア", "マテオ", "ハンナ", "ミア", "レオ", "ジョン", "アナ" };

        public static SoupDefinition Soup(string id) => Soups.FirstOrDefault(x => x.Id == id) ?? Soups[0];
        public static CondimentDefinition Condiment(string id) => Condiments.First(x => x.Id == id);

        /// <summary>壊れた保存データの未知・欠損IDは例外にせず、定義の有無を返します。</summary>
        public static bool TryCondiment(string id, out CondimentDefinition definition)
        {
            for (int index = 0; index < Condiments.Length; index++)
            {
                if (Condiments[index].Id != id) continue;
                definition = Condiments[index];
                return true;
            }
            definition = null;
            return false;
        }
        public static FurnitureDefinition FurnitureById(string id) => Furniture.First(x => x.Id == id);
        public static StaffDefinition StaffFor(StaffRole role) => Staff.First(x => x.Role == role);
        public static EquipmentDefinition EquipmentById(string id) => Equipment.First(x => x.Id == id);
        public static CustomerArchetype Archetype(string id) => Archetypes.FirstOrDefault(x => x.Id == id) ?? Archetypes[0];
        public static ThemeDefinition Theme(string id) => Themes.FirstOrDefault(x => x.Id == id) ?? Themes[0];
        public static RankDefinition Rank(int stars) => Ranks[Math.Max(1, Math.Min(5, stars)) - 1];
        public static WeatherDefinition Weather(string id) => Weathers.FirstOrDefault(x => x.Id == id) ?? Weathers[1];
        public static DailyEventDefinition DailyEvent(string id) => DailyEvents.FirstOrDefault(x => x.Id == id) ?? DailyEvents[0];

        /// <summary>店舗レベル n に必要な累計経験値。</summary>
        public static int ExperienceForLevel(int level) => level <= 1 ? 0 : 120 * (level - 1) * level / 2 + 60 * (level - 1);
    }
}
