using System;

namespace MalatangStory.Game
{
    /// <summary>
    /// キャラクターの見た目。店主・常連客・スタッフで共通に使い、保存データにも含める。
    /// 各値はパレットや形状の番号で、範囲外の値は表示側で剰余を取って扱う。
    /// </summary>
    [Serializable]
    public sealed class CharacterAppearance
    {
        /// <summary>肌の色（0〜4）。</summary>
        public int skinTone;
        /// <summary>髪型（0:短髪 1:お団子 2:ボブ 3:ポニーテール 4:坊主 5:長髪 6:ツインテール 7:ツンツン 8:パーマ 9:七三分け 10:ロングウェーブ）。</summary>
        public int hairStyle;
        /// <summary>髪色（0〜5）。</summary>
        public int hairColor;
        /// <summary>上着の色（0〜9）。</summary>
        public int topColor;
        /// <summary>ズボン・スカートの色（0〜5）。</summary>
        public int bottomColor;
        /// <summary>小物（0:なし 1:眼鏡 2:帽子 3:ヘッドホン 4:マフラー）。</summary>
        public int accessory;
        /// <summary>帽子（0:なし 1:コック帽 2:バンダナ 3:キャップ）。店主・スタッフ向け。</summary>
        public int hatStyle;
        /// <summary>エプロンを着けるか。</summary>
        public bool apron;
        /// <summary>エプロンの色（0〜5）。</summary>
        public int apronColor;
        /// <summary>服装（0:普段着 1:スーツ 2:パーカー 3:作業着ベスト 4:スクラブ 5:ジャージ 6:ワンピース 7:学生服 8:配達員ジャケット 9:アロハ 10:カーディガン 11:ジャケットとTシャツ 12:蝶ネクタイのフォーマル）。</summary>
        public int outfit;
        /// <summary>客用帽子（0:なし 1:工事ヘルメット 2:配達ヘルメット 3:キャップ 4:ニット帽 5:観光帽 6:ベレー帽）。hatStyleが優先される。</summary>
        public int headwear;
        /// <summary>鞄（0:なし 1:リュック 2:ショルダーバッグ 3:手持ちビジネスバッグ 4:配達ボックス 5:首掛けカメラ）。</summary>
        public int bag;
        /// <summary>眼鏡（0:なし 1:眼鏡 2:サングラス）。既存accessoryと併用できる。</summary>
        public int eyewear;
        /// <summary>マスクを着けるか。</summary>
        public bool mask;
        /// <summary>体格（0:標準 1:がっしり 2:細身 3:ふくよか）。胴体と腕の太さに適用する。</summary>
        public int build;
        /// <summary>白髪と軽い前かがみを適用するか。</summary>
        public bool elderly;
        /// <summary>ほろ酔いの赤ら顔にするか。</summary>
        public bool blush;
        /// <summary>手持ち小物（0:なし 1:スマホ 2:閉じた傘 3:飲み物カップ）。運搬中は非表示。</summary>
        public int holdItem;
        /// <summary>目（0:丸目 1:たれ目 2:つり目 3:細目 4:まつげ）。欠落時は丸目。</summary>
        public int eyeStyle;
        /// <summary>眉（0:柔らかい弧 1:太い直線 2:きりっとした眉）。</summary>
        public int browStyle;
        /// <summary>ひげ（0:なし 1:口ひげ 2:あごひげ 3:無精ひげ）。</summary>
        public int facialHair;
        /// <summary>靴色（0:墨色 1:茶 2:赤 3:青 4:クリーム 5:緑）。</summary>
        public int shoeColor;
        /// <summary>頬に小さなそばかすを付けるか。</summary>
        public bool freckles;
        /// <summary>スーツのネクタイを緩めるか。</summary>
        public bool looseTie;
        /// <summary>身長の倍率（0.85〜1.1）。子どもは 0.7 程度。</summary>
        public float height = 1f;
        public bool embroidery;

        /// <summary>値を複製する。</summary>
        public CharacterAppearance Clone() => (CharacterAppearance)MemberwiseClone();

        /// <summary>
        /// 種による基本外見に客タイプ固有の服装を追加する。未知のIDではFromSeedと全フィールドが一致する。
        /// 子どもの身長はタイプを問わず0.7で、familyの子どもには学生服を適用しない。
        /// </summary>
        public static CharacterAppearance ForArchetype(string archetypeId, int seed, bool child)
        {
            CharacterAppearance appearance = FromSeed(seed, child);
            Random random = new Random(seed);
            switch (archetypeId)
            {
                case "office":
                    appearance.outfit = 1;
                    appearance.topColor = 9;
                    appearance.bag = random.Next(3) == 0 ? 3 : 0;
                    break;
                case "student":
                    appearance.outfit = random.Next(2) == 0 ? 7 : 2;
                    appearance.bag = 1;
                    break;
                case "spicy":
                    appearance.topColor = random.Next(4) < 3 ? 0 : appearance.topColor;
                    break;
                case "healthy":
                    appearance.outfit = random.Next(2) == 0 ? 5 : 6;
                    appearance.topColor = 3;
                    break;
                case "meatlover":
                    appearance.build = 1;
                    break;
                case "family":
                    if (child)
                    {
                        appearance.accessory = 0;
                        appearance.topColor = random.Next(3);
                    }
                    break;
                case "elder":
                    appearance.elderly = true;
                    appearance.outfit = 10;
                    appearance.eyeStyle = 3;
                    appearance.facialHair = child ? 0 : random.Next(4);
                    break;
                case "tourist":
                    appearance.outfit = 9;
                    appearance.headwear = 5;
                    appearance.bag = 5;
                    break;
                case "critic":
                    appearance.outfit = 1;
                    appearance.topColor = 9;
                    appearance.eyewear = 1;
                    appearance.headwear = 6;
                    break;
                case "influencer":
                    appearance.outfit = 11;
                    appearance.holdItem = 1;
                    break;
                case "worker":
                    appearance.outfit = 3;
                    appearance.headwear = 1;
                    appearance.topColor = 1;
                    appearance.build = 1;
                    appearance.browStyle = 1;
                    appearance.facialHair = child ? 0 : 1 + random.Next(3);
                    break;
                case "rider":
                    appearance.outfit = 8;
                    appearance.headwear = 2;
                    appearance.bag = 4;
                    appearance.topColor = 4;
                    break;
                case "nurse":
                    appearance.outfit = 4;
                    appearance.topColor = random.Next(2) == 0 ? 4 : 7;
                    appearance.bottomColor = 1;
                    break;
                case "couple":
                    appearance.outfit = random.Next(2) == 0 ? 11 : 6;
                    break;
                case "vegetarian":
                    appearance.outfit = random.Next(2) == 0 ? 6 : 10;
                    appearance.bag = 2;
                    appearance.topColor = 3;
                    break;
                case "athlete":
                    appearance.outfit = 5;
                    appearance.build = 1;
                    appearance.headwear = 3;
                    break;
                case "bigeater":
                    appearance.build = 3;
                    break;
                case "foreigner":
                    appearance.bag = 1;
                    appearance.headwear = 3;
                    appearance.skinTone = random.Next(5);
                    appearance.hairColor = random.Next(6);
                    break;
                case "celebrity":
                    appearance.outfit = 11;
                    appearance.headwear = 3;
                    appearance.eyewear = 2;
                    appearance.mask = true;
                    break;
                case "drunk":
                    appearance.outfit = 1;
                    appearance.topColor = 9;
                    appearance.blush = true;
                    appearance.looseTie = true;
                    appearance.eyeStyle = 3;
                    break;
                case "judge":
                    appearance.outfit = 12;
                    appearance.topColor = 9;
                    appearance.eyewear = 1;
                    break;
                default:
                    return appearance;
            }
            // 乱数の旧帽子やマフラーでタイプ固有の制服を隠さない。
            if (archetypeId != "family" && archetypeId != "spicy" && archetypeId != "meatlover" && archetypeId != "bigeater")
                appearance.accessory = 0;
            return appearance;
        }

        /// <summary>乱数の種から決定的に見た目を作る。</summary>
        public static CharacterAppearance FromSeed(int seed, bool child = false)
        {
            Random random = new Random(seed);
            return new CharacterAppearance
            {
                skinTone = random.Next(5), hairStyle = random.Next(11), hairColor = random.Next(6),
                topColor = random.Next(10), bottomColor = random.Next(6),
                accessory = random.Next(10) < 6 ? 0 : 1 + random.Next(4),
                height = child ? 0.7f : 0.88f + (float)random.NextDouble() * 0.2f,
                eyeStyle = random.Next(5), browStyle = random.Next(3), shoeColor = random.Next(6),
                facialHair = child ? 0 : (random.Next(5) == 0 ? 1 + random.Next(3) : 0),
                freckles = random.Next(5) == 0
            };
        }
    }
}
