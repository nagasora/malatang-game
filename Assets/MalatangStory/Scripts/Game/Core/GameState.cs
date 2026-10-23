using System;
using System.Collections.Generic;
using System.Linq;
using MalatangStory.Ingredients;

namespace MalatangStory.Game
{
    /// <summary>一日の進行段階。</summary>
    public enum DayPhase { Preparation, Open, Closing, Report }

    /// <summary>
    /// 注文の段階。保存データ互換のため、値は末尾にのみ追加する。
    /// 実際の流れ：Waiting →（レジ）Ordering → Selecting → Weighed → Heating → Drained → Finishing → Ready → Served。
    /// セルフ式はレジで量って会計するため Ordering から直接 Weighed へ進む。
    /// </summary>
    public enum OrderStage { Waiting, Selecting, Heating, Drained, Ready, Served, Cancelled, Weighed, Finishing, Ordering }

    /// <summary>支払い方法。</summary>
    public enum PaymentMethod { Cash, QrCode, Card }

    /// <summary>注文方式。セルフ式は客が具材を取ったボウルを持参する。</summary>
    public enum OrderStyle { Ordered, SelfServe }

    /// <summary>在庫。</summary>
    [Serializable] public sealed class Stock
    {
        public string ingredientId;
        public int quantity;
        public int ageDays;
        /// <summary>在庫1個あたりの平均取得原価（分）。0 は標準原価（旧データ）。</summary>
        public long unitCostFen;
    }

    /// <summary>品質評価の内訳（0〜100）。</summary>
    [Serializable] public sealed class QualityReport
    {
        public float total, accuracy, doneness, drain, soup, seasoning, speed, freshness, satisfaction;
        public List<string> comments = new List<string>();
    }

    /// <summary>客1人の注文と、その調理の進み具合。</summary>
    [Serializable] public sealed class CustomerOrder
    {
        public string id, customerName;
        /// <summary>v1 互換の表示用ラベル（例：会社員・常連）。</summary>
        public string type;
        public string archetype = "office";
        public string regularId = "";
        public string wish = "", returnNote = "";
        public string signatureId = "";
        public OrderStyle style;
        public bool takeout;
        public int appearanceSeed;
        public string groupId = "";
        /// <summary>要望のスープ ID。</summary>
        public string soup = "mala";
        public int spice;
        public List<string> requested = new List<string>();
        public List<string> requestedCondiments = new List<string>();
        public List<string> dislikedCondiments = new List<string>();
        public List<string> selected = new List<string>();
        /// <summary>selected と同じ並びで、鍋に入れた時点の加熱秒（未投入は -1）。</summary>
        public List<float> dropTimes = new List<float>();
        public float weighedGrams;
        public string chosenSoup = "";
        public float soupMl;
        public int chosenSpice;
        public List<string> condiments = new List<string>();
        public int shakes;
        public OrderStage stage;
        public int pot = -1;
        public int seat = -1;
        public float elapsed, patience = 150, heatSeconds, eatSeconds, quality;
        public long paidFen, consumedCostFen, tipFen;
        public bool stockConsumed, boiledOver, handledByStaff;
        /// <summary>レジでの請求額（分）。0 は未計算。</summary>
        public long billFen;
        public PaymentMethod payment;
        /// <summary>現金払いで客が出したお金（分）。</summary>
        public long tenderedFen;
        /// <summary>注文票（スープ・辛さ・持ち帰り）をレジで確定したか。</summary>
        public bool ticketConfirmed;
        /// <summary>レジでの聞き間違い・おつり間違いの回数。</summary>
        public int registerMistakes;
        /// <summary>接客の良し悪しによる満足度の加減（レジ対応など）。</summary>
        public float serviceBonus;
        /// <summary>満足度が低くて値引き（返金）した額（分）。</summary>
        public long discountFen;
        /// <summary>常連の「いつもの」注文か。usedHint は常連ノートを見て注文票を書いたか。</summary>
        public bool usual, usedHint;
        /// <summary>提供後の客の一言（チップ・値引きのせりふ）。</summary>
        public string reaction = "";
        /// <summary>おまかせ注文か（具材の指定なし。予算と好みだけ）。</summary>
        public bool omakase;
        /// <summary>おまかせの予算（分）と、その予算で入る重さの目安（g）。</summary>
        public long budgetFen;
        public float targetGrams;
        /// <summary>おまかせで好きな食感（FoodTexture の番号、-1 は指定なし）。</summary>
        public int likeTexture = -1;
        /// <summary>おまかせで野菜を多めにしてほしいか。</summary>
        public bool wantVeg;
        /// <summary>おまかせで苦手な具材のカテゴリ（空は指定なし）。</summary>
        public string avoidCategory = "";
        /// <summary>完成した一杯が当てはまった隠しレシピ ID（無ければ空）。</summary>
        public string recipe = "";
        /// <summary>客が名前で頼んだ隠しレシピ ID（無ければ空）。</summary>
        public string recipeRequest = "";
        /// <summary>完成した一杯に入っていた相性の組み合わせ ID。</summary>
        public List<string> pairings = new List<string>();
        public QualityReport report = new QualityReport();
        /// <summary>食後の記録。mealFinished が偽なら旧保存の未記録です。</summary>
        public bool mealFinished;
        public MealLeftoverResult leftovers = new MealLeftoverResult();
        public string review = "";
        public string visualState = "enter";
        public float visualSeconds;
        public int handRhythm;
        public float lastHandShake = -1;
        public bool usedVisibleGauge = true, expertBowl;
        public List<GarnishStroke> garnishStrokes = new List<GarnishStroke>();

        /// <summary>提供済みまたは取消済みで、これ以上進まない注文か。</summary>
        public bool IsClosed => stage == OrderStage.Served || stage == OrderStage.Cancelled;

        /// <summary>レジの前にいて、まだ会計を済ませていない（列に並んでいる・注文を話している）か。</summary>
        public bool IsAtRegister => stage == OrderStage.Waiting || stage == OrderStage.Ordering;

        /// <summary>会計を済ませ、一杯ができあがるのを待っている（受け取り前）か。</summary>
        public bool AwaitsBowl => (stage == OrderStage.Selecting && paidFen > 0) || stage == OrderStage.Weighed || stage == OrderStage.Heating
            || stage == OrderStage.Drained || stage == OrderStage.Finishing || stage == OrderStage.Ready;
    }

    /// <summary>一日分の営業成績。</summary>
    [Serializable] public sealed class DailyReport
    {
        public long revenueFen, foodCostFen, purchasesFen, fixedCostFen, refundsFen, supportFen, wagesFen, tipsFen, investmentFen;
        public int served, lost, perfect, experience, reputationDelta;
        public int mealsFinished, finishedFood, finishedSoup;
        /// <summary>具材IDと理由IDを縦線で区切った、その日の残食個数です。</summary>
        public List<NamedCount> leftovers = new List<NamedCount>();
        /// <summary>不満による値引き額（分。売上から差し引き済み）と件数。</summary>
        public long discountsFen;
        public int complaints;
        /// <summary>おつりの渡しすぎによる損失（分）。</summary>
        public long changeLossFen;
        /// <summary>ミッション報酬とグランプリの賞金（分）。</summary>
        public long missionRewardsFen, prizeFen;
        /// <summary>その日のグランプリの結果を確定済みか。</summary>
        public bool contestConcluded;
        public int excellent, exactChange, bestCombo, signatureSold, spicyServed;
        /// <summary>本日提供した客タイプ（重複あり）。</summary>
        public List<string> servedArchetypes = new List<string>();
        public float qualitySum;
        public AlbumEntry bestBowl;
        public List<string> reviews = new List<string>();
        public List<string> highlights = new List<string>();
        /// <summary>閉店時に人気へ加える増減（配信者など）。途中保存でも失われないよう日報に持つ。</summary>
        public float popularityDelta;
        public long ProfitFen => revenueFen + tipsFen + missionRewardsFen + prizeFen - foodCostFen - fixedCostFen - wagesFen - changeLossFen;
        public float AverageQuality => served == 0 ? 0 : qualitySum / served;
    }

    /// <summary>客席に置いた家具。</summary>
    [Serializable] public sealed class PlacedFurniture
    {
        public int uid;
        public string defId;
        /// <summary>グリッド上の左下セル（壁装飾は壁スロット番号を x に入れる）。</summary>
        public int x, z;
        /// <summary>0 または 1（90度回転）。</summary>
        public int rotation;
    }

    /// <summary>雇っているスタッフ。</summary>
    [Serializable] public sealed class HiredStaff
    {
        public string id = "";
        public int personality = 2;
        public int praisedDay, mealDay;
        public float fatigue, motivation = 70;
        public bool resting;
        public StaffRole role;
        public string name;
        public int appearanceSeed;
        public int trainingLevel = 1;
        public float busySeconds;
        public string task = "";
        public string taskTarget = "";
    }

    /// <summary>常連客。好みと親密度を覚えている。</summary>
    [Serializable] public sealed class RegularCustomer
    {
        public string id, name, archetype;
        public int appearanceSeed;
        public int visits;
        public float affinity;
        public string favoriteSoup;
        public int favoriteSpice;
        public List<string> favorites = new List<string>();
        public List<string> likedCondiments = new List<string>();
        public List<string> dislikedCondiments = new List<string>();
        public int storyIndex;
        public int answeredStory, lastVisitDay;
        public bool concerned, introducedFriend;
    }

    /// <summary>名物メニュー。</summary>
    [Serializable] public sealed class SignatureDish
    {
        public string id, name;
        /// <summary>登録元の注文 ID（同じ一杯の二重登録を防ぐ）。</summary>
        public string sourceOrderId = "";
        public List<string> ingredients = new List<string>();
        public string soup;
        public int spice;
        public List<string> condiments = new List<string>();
        public int timesSold;
        public float qualitySum;
        public AlbumEntry bestBowl;
    }

    /// <summary>設備のレベル。</summary>
    [Serializable] public sealed class EquipmentLevel
    {
        public string id;
        public int level;
    }

    /// <summary>累計の統計。実績判定に使う。</summary>
    [Serializable] public sealed class LifetimeStats
    {
        public int served, perfectBowls, criticsSatisfied, flawlessDays, spicyPerfect, cancelled, groupsServed;
        public long tipsFen;
        public int bestAmbience;
        public int exactChange, bestCombo, missionsCompleted, complaints, contestGold, contestSilver, contestBronze, bestDayServed, vegetarianPleased, celebrities;
        public long discountsFen;
        /// <summary>隠しレシピの一杯を出した累計数。</summary>
        public int recipeBowls;
        /// <summary>店内で食べ終えた累計と、完食・完飲・全員完食日の累計です。</summary>
        public int mealsFinished, finishedFood, finishedSoup, allFinishedDays;
        /// <summary>旬の具材が入った一杯を出した累計数。</summary>
        public int seasonalServed;
        /// <summary>おまかせ注文で品質85点以上を出した回数。</summary>
        public int omakaseGreat;
        /// <summary>5色以上の具材が入った一杯（彩り満点）を出した回数。</summary>
        public int rainbowBowls;
        /// <summary>異なる希少食材を2種類以上入れた同一の一杯を提供した累計数。</summary>
        public int rareCombinationBowls;
        /// <summary>営業の評価で S を取った回数。</summary>
        public int gradeS;
        /// <summary>具材ごとの累計使用数（食材図鑑用）。</summary>
        public List<NamedCount> ingredientsServed = new List<NamedCount>();
        /// <summary>客タイプごとの累計提供数（図鑑用）。</summary>
        public List<NamedCount> archetypesServed = new List<NamedCount>();
        /// <summary>スープごとの最高品質（レシピ帳用）。</summary>
        public List<NamedCount> bestQualityBySoup = new List<NamedCount>();

        /// <summary>名前付きカウンターを加算する（無ければ作る）。</summary>
        public static void Add(List<NamedCount> list, string id, int amount = 1)
        {
            var entry = list.FirstOrDefault(x => x.id == id);
            if (entry == null) list.Add(entry = new NamedCount { id = id });
            entry.count += amount;
        }

        /// <summary>名前付きカウンターを最大値で更新する。</summary>
        public static void Max(List<NamedCount> list, string id, int value)
        {
            var entry = list.FirstOrDefault(x => x.id == id);
            if (entry == null) list.Add(entry = new NamedCount { id = id });
            entry.count = Math.Max(entry.count, value);
        }

        /// <summary>名前付きカウンターの値（無ければ 0）。</summary>
        public static int Get(List<NamedCount> list, string id) => list.FirstOrDefault(x => x.id == id)?.count ?? 0;
    }

    /// <summary>1日の営業の要約（レポートのグラフと評価の記録用）。</summary>
    [Serializable] public sealed class DaySummary
    {
        public int day;
        public long revenueFen, profitFen;
        public int served, lost;
        public float satisfaction;
        /// <summary>営業の評価（S・A・B・C・D）。</summary>
        public string grade = "";
    }

    /// <summary>名前付きの数（JsonUtility は辞書を保存できないため一覧で持つ）。</summary>
    [Serializable] public sealed class NamedCount
    {
        public string id;
        public int count;
    }

    /// <summary>日替わりミッション。</summary>
    [Serializable] public sealed class Mission
    {
        public string id;
        /// <summary>種類（serve, excellent, tips, exact, combo, archetype, signature, spicy, revenue, noComplaint）。</summary>
        public string kind;
        /// <summary>種類ごとの補足（客タイプ ID など）。</summary>
        public string param = "";
        public int target, progress;
        public long rewardFen;
        public int rewardXp;
        public bool done;
    }

    /// <summary>ゲーム全体の保存状態（保存形式 v10）。</summary>
    [Serializable] public sealed class GameState
    {
        public const int CurrentVersion = 10;
        public int saveVersion = CurrentVersion;
        public string storeId = "store-001";
        public string storeName = "麻辣湯の小さなお店";
        public int day = 1;
        public DayPhase phase = DayPhase.Preparation;
        public float daySeconds, spawnSeconds;
        public int nextOrderNumber = 1, spawnedToday, nextFurnitureUid = 1, nextSignatureNumber = 1;
        public long moneyFen = 50000;
        public int pricePer100gFen = 1800, pots = 1, storageCapacity = 30;
        public float reputation = 50, popularity = 30;
        public int shopLevel = 1, experience;
        public bool tutorialComplete;
        public int tutorialStep;
        public float playerX = 0, playerZ = 0;
        public float cameraYaw = 0;
        /// <summary>一人称視点で操作するか。</summary>
        public bool firstPerson;
        /// <summary>品質の高い一杯が続いている数（チップに倍率がかかる）。</summary>
        public int combo;
        public float musicVolume = .5f, effectsVolume = .7f;
        public int textSize = 20;
        public bool highQuality = true;
        /// <summary>視点操作（ドラッグ・一人称の見回し）の感度倍率（0.4〜2.0）。</summary>
        public float lookSensitivity = 1f;
        /// <summary>一人称の上下の見回しを反転するか。</summary>
        public bool invertLook;
        /// <summary>「次にやること」の案内と目印を出すか。</summary>
        public bool navigation = true;
        public int regularVisits;
        public string weather = "sunny";
        public string dailyEvent = "none";
        public bool eventTriggered;
        public float eventSeconds = -1;
        public CharacterAppearance chef = new CharacterAppearance { apron = true, hatStyle = 2, topColor = 0, apronColor = 0, hairStyle = 0, hairColor = 0 };
        public IngredientProgress ingredients = new IngredientProgress();
        public List<Stock> stock = new List<Stock>();
        public List<CustomerOrder> orders = new List<CustomerOrder>();
        public DailyReport report = new DailyReport();
        public List<PlacedFurniture> furniture = new List<PlacedFurniture>();
        public List<int> dirtyTables = new List<int>();
        /// <summary>注文を日報確定時に消した後も、片付けまで保持する器です。</summary>
        public List<MealRemains> mealRemains = new List<MealRemains>();
        public List<HiredStaff> staff = new List<HiredStaff>();
        public List<RegularCustomer> regulars = new List<RegularCustomer>();
        public List<SignatureDish> signatures = new List<SignatureDish>();
        public List<EquipmentLevel> equipment = new List<EquipmentLevel>();
        public List<string> achievements = new List<string>();
        public List<string> recentReviews = new List<string>();
        public LifetimeStats stats = new LifetimeStats();
        public List<Mission> missions = new List<Mission>();
        /// <summary>適用中の内装テーマ ID。</summary>
        public string theme = Content.DefaultTheme;
        /// <summary>購入済みの内装テーマ ID。</summary>
        public List<string> ownedThemes = new List<string> { Content.DefaultTheme };
        /// <summary>見つけた隠しレシピと、作った回数。</summary>
        public List<NamedCount> recipes = new List<NamedCount>();
        /// <summary>完了した「店の物語」の章の数（＝いま進めている章の番号）。</summary>
        public int chapter;
        /// <summary>今週の旬の食材 ID（無ければ空）。</summary>
        public string featuredIngredient = "";
        /// <summary>旬を選んだ週の番号（-1 は未選択）。</summary>
        public int featuredWeek = -1;
        /// <summary>これまでの営業日の記録（新しいものが末尾。最大 HistoryLimit 日）。</summary>
        public List<DaySummary> history = new List<DaySummary>();
        /// <summary>店主スキルの段階。</summary>
        public List<NamedCount> skills = new List<NamedCount>();
        /// <summary>出したことのある相性の組み合わせと、出した回数。</summary>
        public List<NamedCount> pairings = new List<NamedCount>();
        /// <summary>一杯アルバム（品質の高い順）。</summary>
        public List<AlbumEntry> album = new List<AlbumEntry>();
        /// <summary>一杯アルバムの最後の通し番号。</summary>
        public long albumSeq;
        /// <summary>朝市を選出した日。0は未選出。</summary>
        public int marketDay;
        /// <summary>当日の朝市の希少食材ID。</summary>
        public string marketIngredient = "";
        /// <summary>当日の限定5個を購入済みか。</summary>
        public bool marketPurchased;

        public bool reducedMotion, snapCamera, leftHanded;
        public int gaugeMode;
        public string interactKey = "E", buildRotateKey = "T";
        public PreparationState preparation = new PreparationState();
        public List<HouseSoup> houseSoups = new List<HouseSoup>();
        public string activeHouseSoup = "";
        public int nextHouseSoup = 1;
        public int nextStaffId = 1;
        public int rushWarnings;
        public string challengeMode = "", challengeRule = "";
        public int challengeSeed;
        public List<ChallengeRecord> challengeRecords = new List<ChallengeRecord>();
        public List<string> introducedArchetypes = new List<string>();

        /// <summary>設備レベルを返す（未購入は鍋・保管棚が1、その他は0）。</summary>
        public int EquipmentLevel(string id)
        {
            if (id == "pot") return pots;
            if (id == "storage") return storageCapacity / 30;
            var entry = equipment.FirstOrDefault(x => x.id == id);
            return entry == null ? (id == "expansion" ? 1 : id == "fridge" || id == "stove" ? 1 : 0) : entry.level;
        }

        /// <summary>設備レベルを設定する。</summary>
        public void SetEquipmentLevel(string id, int level)
        {
            if (id == "pot") { pots = level; return; }
            if (id == "storage") { storageCapacity = level * 30; return; }
            var entry = equipment.FirstOrDefault(x => x.id == id);
            if (entry == null) equipment.Add(entry = new EquipmentLevel { id = id });
            entry.level = level;
        }
    }
}
