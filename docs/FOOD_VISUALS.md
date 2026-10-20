# 手続き食材モデル（第1段階）

## API と追加方法

名前空間は `MalatangStory.Game`。Unity メインスレッドから利用する。
既存の `IngredientVisuals.Build(item, parent, scale = 1f, physics = false)`、
`IngredientView.SetDoneness(float)`、`SetWet(float)` は変更していない。
登録済み ID は Food 基盤、未登録 ID は従来モデルで生成される。

第2段階は `Food/FoodMushroomsNoodlesSoy.cs` と
`Food/FoodMeatSeafoodProcessed.cs` だけを追加すればよい。
`IFoodBuilder.Build(string id)` を実装し、対応 ID を登録する。
登録の例（クラス名と ID は担当カテゴリに置き換える）:

```csharp
/// <summary>担当カテゴリをプレイ開始時に登録します。</summary>
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
public static void Register()
{
    FoodRegistry.Register(new FoodMushroomsNoodlesSoy(), "enoki", "king_oyster");
}
```

`Build` 内で `using (FoodModelBuilder model = new FoodModelBuilder())` を使い、
`SetSurface(group, texture, foodSurface, transparent)` →
`Add(group, mesh, position, scale, rotation)` → `Finish(id)` の順で組み立てる。
`Build` は完成した共有モデルを返し、非対応 ID は null を返す。
Unity オブジェクトをフィールド初期化子で生成しない。
登録は実行時属性で行うため、IL2CPP でも反射や型探索は不要。
編集モードで直接呼ぶ独自ツールでは、担当クラスの登録関数を先に明示的に呼ぶ。
標準野菜は `TryGet` からも登録される。

## 部品と所有権

- `FoodKit.Ellipsoid(seed, noise, segments, rings)` は単位直径の歪んだ球。
  `Add` の倍率で楕円体にする。
- `Lathe(profile, segments)` は `(半径, Y高さ)` の回転体。
- `Tube(path, startRadius, endRadius, sides)` は曲線チューブ。端面は硬い法線。
- `Leaf(width, length, curl, serration, rows, columns)` は XZ 葉身。
  根元 Z=0、先端 Z=length、U=.5 が中央葉脈、V は根元から先端。
- `Sheet(width, length, folds, amplitude, rows, columns)` は XZ のしわシート。
- `PerforatedSlice(radius, thickness, holes, resolution)` は孔と壁のある輪切り。
  上下面に平面 UV、切断面・穴壁の法線は分離。
- 束・花房・重ねた切り身は `Add` を複数回呼び、同じ表面へ結合する。
  円柱切片には短い `Tube`、薄い切り身には `Sheet` が使える。

FoodKit は非キャッシュの新規メッシュを返す。`Add` に渡したメッシュは
FoodModelBuilder が所有し、結合後に破棄する。既存 MeshFactory の共有メッシュを
直接 `Add` に渡してはいけない（必要なら Instantiate で複製）。
テクスチャは `SetSurface` で所有権を渡す。完成モデルのメッシュ、テクスチャ、
マテリアルは FoodRegistry が保持する。インスタンスごとに複製しない。
失敗時・未使用表面の一時リソースも Dispose で解放される。

登録上限は69 ID、同じ ID の異種ビルダーによる上書きは禁止。
`FoodRegistry.ReleaseAll()` は使用中インスタンスをすべて破棄した後だけ呼ぶ。
プレイ開始時・プレイ終了時・アプリ終了時はキャッシュを解放する。
実行時の寿命管理オブジェクトは DontDestroyOnLoad でシーン間の共有を維持する。
編集モードの独自撮影ツールでは、撮影モデル破棄後に ReleaseAll を明示的に呼ぶ。

## 予算・寸法・色

- scale=1 で最大寸法 **0.1〜0.25 Unity 単位**。ローカル座標で設計する。
- 1食材 **1500 tris 以下、Renderer 最大3**。Finish が寸法・三角形数を検査する。
  野菜20種は静的算定で186〜1184 tris（最大baby_corn）、最大寸法約0.12〜0.214。
  ノイズ変位は0〜1に制限し、頂点の有限値も検査する。Unity実描画・実測は未実施。
- group は0〜2。同じ表面の部品は1メッシュ・1マテリアルへ結合する。
- テクスチャは **128px 推奨、256px 上限**、RGBA32、ミップあり、Trilinear。
  野菜は1〜3枚/ID、計40枚（すべて128px）。CPU画素は Apply 後に解放。
- `FoodTextures.Create(id, dark, light, pattern)` は多層ノイズと模様を生成する。
  `FoodPattern` は Mottled / Veins / Fibers / Pores / Marbling / Gradient /
  Toasted / Translucent / Florets。ID種は決定論的で UnityEngine.Random を変更しない。
- 食材の原色はテクスチャに保存。生の tint は白（透明食材だけ alpha を設定）。
  生の tint に食材色を再指定すると二重に暗くなるため避ける。
- 透け感は透明Litの alpha と淡いテクスチャで近似する。屈折・散乱は実装しない。
  NormalMap は現段階で使わない。

## 加熱

`FoodSurface(cookedTint, overcookedTint, rawSmoothness, cookedSmoothness, alpha, overcookedSmoothness)`
で表面ごとの加熱特性を設定する。SetDoneness の0→1→1.5で
生→茹で上がり→過加熱の tint と艶を補間する。
MaterialPropertyBlock の `_BaseColor` でテクスチャに乗算し、共有マテリアルは変更しない。
SetWet は現在の艶から .92 へ補間する。
葉物は濃緑・黄褐色化、海藻は濡れた艶と透明度を使う。
肉はテクスチャの赤味を抑えた灰褐色の tint、麺は透明マテリアルの alpha を
加熱段階ごとに変える。形状変形・頂点アニメーションは行わない。

## 参照実装

`FoodVegetables.Tomato`: 果皮・ヘタの分離と艶。
`Greens`: 白い厚い葉柄、葉脈テクスチャ、波打つ葉の組み合わせ。
`Broccoli`: 分岐軸とノイズ球の小房・花蕾模様。
`Sprouts`: 曲線チューブと豆・根の束。
`Lotus`: 貫通孔のある切断面と皮の縁。
`Seaweed`: 不透明な昆布と透明なわかめの使い分け。

アイコンは俯角30度・方位45度、bounds 8頂点をカメラ空間で自動フレーミングする。
専用キー・フィル・リムは撮影中だけ有効化し、背景は透明を維持する。
ソフト自己影の実描画は URP アセットの追加ライト影設定に依存する。
Unity・実機での色、影、透明ソートの見え方は統合後に確認する。
