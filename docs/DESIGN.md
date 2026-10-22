# Malatang Story v1.0 設計書

目標：Japanese Ramen Simulator 級の「手触りのある調理」と「店を育てる経営」を、Mac / iPhone の両方で遊べるようにする。

## 前提（ユーザー未回答のため採用した既定方針）
- 対象機種：Mac（マウス）と iPhone（タッチ）の両方。操作はすべて「タップ・ドラッグ・長押し」で完結させ、キーボードは補助。
- 視点：店全体は見下ろしのミニチュア視点（三人称）。調理は調理台の寄りカメラ（一人称に近い手元視点）で行う。
- 素材：外部アセットの購入はしない。3Dモデル・テクスチャ・効果音・BGMはすべて実行時に手続き的に生成する。

## コアループ
1. 開店準備：天気と本日のイベントを確認、仕入れ、名物メニュー登録、内装・スタッフ・設備。
2. 営業（11:00〜20:00 / 実時間12分）：
   - 客が来店し列に並ぶ（セルフ式は自分で具材を取ったボウルを持参、オーダー式は口頭で注文）。
   - 【取る】冷蔵ケースから具材をボウルへ。
   - 【量る】秤に載せて重量課金（会計）。
   - 【茹でる】網じゃくに具材を入れて鍋へ。具材ごとに茹で時間が異なり、投入順が腕の見せどころ。
   - 【湯切り】上下に振って水気を切る（回数で評価）。
   - 【仕上げ】スープを注ぎ（量）、ラー油（辛さ）・ごまだれ・酢・にんにく・パクチー等をかける。
   - 【提供】客の席へ運ぶ。食べ終わった席は片付けが必要。
   - 評価は内訳つきで表示し、客は吹き出しと口コミで反応する。
3. 閉店後：営業レポート、口コミ、レベルアップ、翌日へ。

## 注文ステージ（`OrderStage`、保存互換のため値は末尾追加）
`Waiting(0)` 列に並ぶ → `Selecting(1)` 具材を取る → `Weighed(7)` 会計済み → `Heating(2)` 茹で中 → `Drained(3)` 湯切り済み → `Finishing(8)` 仕上げ中 → `Ready(4)` 完成 → `Served(5)` 提供済み（食事中→退店） / `Cancelled(6)`

## 主要システム（Core = UnityEngine 非依存の純粋ロジック。EditMode テスト対象）
| ファイル | 内容 |
|---|---|
| `Core/GameState.cs` | 保存状態 v2（v1 から移行） |
| `Core/Content.cs` | スープ・調味料・家具・スタッフ・実績・客タイプ・天気・イベントの定義 |
| `Core/RestaurantSimulation.cs` | 営業・調理・会計・在庫・経営の中核 |
| `Core/CustomerDirector.cs` | 客の生成（タイプ・好み・見た目・忍耐）と注文生成 |
| `Core/QualityEvaluator.cs` | 品質の内訳評価とコメント生成 |
| `Core/DiningLayout.cs` | 客席グリッド・家具配置・通路の連結性検証・席管理 |
| `Core/StaffSystem.cs` | NPCスタッフの自動作業 |
| `Core/Progression.cs` | 店舗レベル・解禁・実績 |
| `Core/DailyEvents.cs` | 天気・日替わりイベント・営業中イベント |

## 表示層（Presentation）
| フォルダ | 内容 |
|---|---|
| `Presentation/Audio` | 手続き生成の効果音・環境音・BGM |
| `Presentation/Visual` | メッシュ・テクスチャ・マテリアル生成、69食材の3Dモデル、アイコン、パーティクル |
| `Presentation/Characters` | ちびキャラの生成と手続きアニメーション |
| `Presentation/World` | 店舗・照明（時間帯）・客席・客/スタッフのエージェント |
| `Presentation/Kitchen` | 調理台の寄り視点と3D操作（取る・量る・茹でる・振る・注ぐ・かける） |
| `Presentation/UI` | 画面単位の保持型UI（全破棄・再生成をしない） |

## マテリアルの規約
プレイヤービルドでは `Shader.Find` で参照されていないシェーダーが取り除かれるため、`Resources/Materials` のテンプレートを複製して使う。
- `RestaurantLit`（不透明 Lit）、`RestaurantLitTransparent`（半透明 Lit）、`RestaurantLitEmissive`（発光 Lit）、`RestaurantParticles`（Particles/Unlit 加算なし・アルファ）、`RestaurantUnlit`。
- テンプレートは `ProjectBuilder.Setup` が生成する。

## 品質目標
- EditMode：営業100日、移行、配置検証、スタッフ、イベント、評価、保存復旧。
- PlayMode：実際の画面操作で1日を完走。
- Mac 自動プレイ：主要画面のキャプチャ。

## 実装後の補足（v1.0）

### 在庫と原価
- 在庫は「鍋に入れた時」に減るが、具材をボウルに入れた時点で注文ごとに**確保**し（`RestaurantSimulation.Reserved/Available`）、同じ在庫の二重販売を防ぐ。
- 原価は在庫ごとの**平均取得原価**（`Stock.unitCostFen`）。セールで安く仕入れた分は安い原価で計上する。スープ・薬味の原価は完成時に計上する。

### スタッフとプレイヤーの所有権
- プレイヤーが調理画面で扱う注文は `SetPlayerFocus` で予約し、スタッフはその注文の作業を取り消して手を引く。
- スタッフが調理中の注文は閲覧のみ。「自分で引き継ぐ」（`TakeOver`）で所有権を移す。

### 保存
- v1→v2 は `SaveMigration` で移行。食事の表示状態や存在しない席は読み込み時に正規化する。
- アプリより新しい形式の主ファイルがある場合は、バックアップへのフォールバックも上書きもしない（`FutureSaveVersionException`）。

### 表示層の要点
- UI は画面ごとの保持型。状態変化（`Changed`）では「署名」が変わった部分だけを作り直し、ゲージ等は毎フレーム更新する。長押し中のボタンが作り直されないよう、湯切り〜仕上げは同じ署名にしている。
- 具材の盛り付けは剛体。補間つき剛体は Transform ではなく剛体の姿勢を直接設定し、器の内側に厚い受け止めコライダーを置く。
- 実行時に生成したメッシュ・テクスチャ・マテリアルの解放と生成パラメーターの有限値検証は `Visual/OwnedObjects` に集約する（各クラスで `Destroy`/`DestroyImmediate` の切り替えを書かない）。
- 注文ステージの組み合わせ判定は `CustomerOrder.IsClosed`（提供・取消済み）・`IsAtRegister`（レジ前）・`AwaitsBowl`（会計後〜完成）を使う。
- カメラは常に透視投影（店内は狭い画角の見下ろし、調理は寄り）。手前の壁はカメラ方向に応じて低くする（カットアウェイ）。

### 検証
- `Tools/run-tests.sh EditMode|PlayMode`、`Tools/smoke.sh`（Mac で16画面を撮影）、iOS はシミュレーター向け書き出し（`ProjectBuilder.BuildIOSSimulator`）と環境変数 `MALATANG_SMOKE_OUTPUT=persistent` で同じ自動プレイを実行できる。

### 保存の信頼境界

SHA-256は破損検知用であり、改ざん防止ではない。オンライン順位などを追加する際はサーバー側で結果を検証する。クライアント内の鍵によるHMACだけでは不正を防げない。読み込みは8 MiBまでとし、非致命的例外はバックアップ復旧へ進む。復旧できない保存は退避し、退避に失敗した場合は上書きしない。

iOSの保存先は persistentDataPath（Documents）。iCloudバックアップは利用できるが、Finder経由の保存書き換えを公開する UIFileSharingEnabled は有効にしない。
