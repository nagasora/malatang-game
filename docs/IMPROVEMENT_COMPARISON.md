# 改善前後のMac画像比較

作業開始時のv1.7と、最終改善版のMac開発ビルドを比較。ゲームが進行するため、客・通知・料理の状態と時刻は同一ではありません。画面の切り替え完了後を撮影し、PNG原本を加工せず掲載しています。iPhone/iPad表記はMacウィンドウの縦横比を変えた撮影で、端末上の実行ではありません。

| 対象 | 改善前 | 改善後 |
|---|---|---|
| 料理モデル | <img src="images/improvements/before/00-food-1.png" width="380"> | <img src="images/improvements/after/00-food-1.png" width="380"> |
| タイトル | <img src="images/improvements/before/01-title.png" width="380"> | <img src="images/improvements/after/01-title.png" width="380"> |
| 朝の黒板 | <img src="images/improvements/before/02-morning.png" width="380"> | <img src="images/improvements/after/02-morning.png" width="380"> |
| レジの会話 | <img src="images/improvements/before/03b-register-order.png" width="380"> | <img src="images/improvements/after/03b-register-order.png" width="380"> |
| 盛り付けと写真 | <img src="images/improvements/before/07b-bowl-portrait.png" width="380"> | <img src="images/improvements/after/07b-bowl-portrait.png" width="380"> |
| 食事の動き | <img src="images/improvements/before/08b-eating-closeup.png" width="380"> | <img src="images/improvements/after/08b-eating-closeup.png" width="380"> |
| 営業日報 | <img src="images/improvements/before/09-report.png" width="380"> | <img src="images/improvements/after/09-report.png" width="380"> |
| 一人称と通知 | <img src="images/improvements/before/17-first-person.png" width="380"> | <img src="images/improvements/after/17-first-person.png" width="380"> |
| 冬の街 | <img src="images/improvements/before/25-winter-street.png" width="380"> | <img src="images/improvements/after/25-winter-street.png" width="380"> |
| スープ：麻辣 | <img src="images/improvements/before/07d-soup-mala.png" width="380"> | <img src="images/improvements/after/07d-soup-mala.png" width="380"> |
| スープ：白湯 | <img src="images/improvements/before/07d-soup-baitang.png" width="380"> | <img src="images/improvements/after/07d-soup-baitang.png" width="380"> |
| スープ：トマト | <img src="images/improvements/before/07d-soup-tomato.png" width="380"> | <img src="images/improvements/after/07d-soup-tomato.png" width="380"> |
| スープ：きのこ | <img src="images/improvements/before/07d-soup-mushroom.png" width="380"> | <img src="images/improvements/after/07d-soup-mushroom.png" width="380"> |
| スープ：酸菜 | <img src="images/improvements/before/07d-soup-sour.png" width="380"> | <img src="images/improvements/after/07d-soup-sour.png" width="380"> |
| スープ：汁なし | <img src="images/improvements/before/07d-soup-dry.png" width="380"> | <img src="images/improvements/after/07d-soup-dry.png" width="380"> |

## 比較で確認したこと

- 油の粒径と連なり、水面の縁、重量で変わる盛り付け、スープごとの色、木の背景と添え物を確認。
- 朝の黒板・伝票・帳簿へ表示を変更。日報の口コミが料理写真へ入り込む問題は、本文の幅を制限して修正。
- 食事の箸と口の動き、待つ客の動作、冬の轍、通知の専用領域を確認。動作の時間的な保証はPlayModeテストも併用。
- 実機の操作・性能・写真の許可ダイアログは未確認。

## 追加機能の実行画像

### 仕込み

![仕込み](images/improvements/after/30b-house-soup.png)

### 複数スタッフ

![複数スタッフ](images/improvements/after/31-staff.png)

### 屋台での営業

![屋台での営業](images/improvements/after/33c-stall-service.png)

### 出前スクーター

![出前スクーター](images/improvements/after/34b-delivery-scooter.png)

### 図鑑の収集報酬

![図鑑の収集報酬](images/improvements/after/36-collection-rewards.png)

### 写真モード

![写真モード](images/improvements/after/37-photo-mode.png)

### 実際に書き出したPNG

![保存した写真](images/improvements/after/photo-export.png)

## 性能計測

Mac開発版の各10秒間。高・低画質は連続する別区間で客数が異なるため、同一負荷での厳密な比較ではありません。単発の最大値を端末の性能保証には使いません。

| 計測 | 平均ms | p95 ms | 最大ms | GPU平均ms | 50ms超 |
|---|---:|---:|---:|---:|---:|
| 開始時 | 11.95 | 未計測 | 33.88 | 未計測 | 未計測 |
| 最終・高画質 | 10.23 | 10.83 | 71.98 | 3.88 | 1.00 |
| 最終・低画質 | 10.14 | 10.82 | 22.38 | 2.15 | 0.00 |

描画は既存のマテリアル別結合メッシュと簡略遠景を使用。品質設定でSSAO・粒子等を切り替えます。Monoのスレッド割り当てカウンターは配列確保でも増加しなかったため、未対応（-1）を記録し、GCゼロの証明には使っていません。
