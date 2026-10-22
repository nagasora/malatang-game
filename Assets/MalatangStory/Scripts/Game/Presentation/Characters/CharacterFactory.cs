using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>
    /// 色と可動部ごとにメッシュを合成し、吹き出しを除いて最大23レンダラーのちびキャラを生成する。
    /// 正面は+Z、通常身長は約1.45。マテリアルの所有権は供給側にあり、生成メッシュはRigが解放する。
    /// </summary>
    public static class CharacterFactory
    {
        /// <summary>Rigの吹き出し1個を含む24個制限のため、Factoryには23個だけ割り当てる。</summary>
        private const int MaximumRenderers = 23;
        /// <summary>キャラクター共通の表面の滑らかさ。</summary>
        private const float Smoothness = 0.35f;
        /// <summary>肌色のパレット。</summary>
        private static readonly Color[] SkinColors =
        {
            new Color(1f, 0.83f, 0.70f), new Color(0.94f, 0.72f, 0.54f),
            new Color(0.80f, 0.55f, 0.37f), new Color(0.62f, 0.39f, 0.25f),
            new Color(0.41f, 0.25f, 0.17f)
        };
        /// <summary>髪色のパレット。</summary>
        private static readonly Color[] HairColors =
        {
            new Color(0.12f, 0.09f, 0.08f), new Color(0.29f, 0.17f, 0.11f),
            new Color(0.57f, 0.34f, 0.16f), new Color(0.87f, 0.70f, 0.37f),
            new Color(0.47f, 0.45f, 0.47f), new Color(0.66f, 0.29f, 0.30f)
        };
        /// <summary>上着色のパレット。</summary>
        private static readonly Color[] TopColors =
        {
            new Color(0.85f, 0.28f, 0.25f), new Color(0.96f, 0.64f, 0.27f),
            new Color(0.96f, 0.83f, 0.43f), new Color(0.39f, 0.64f, 0.43f),
            new Color(0.31f, 0.64f, 0.68f), new Color(0.32f, 0.47f, 0.76f),
            new Color(0.57f, 0.40f, 0.68f), new Color(0.88f, 0.57f, 0.65f),
            new Color(0.91f, 0.88f, 0.81f), new Color(0.26f, 0.29f, 0.33f)
        };
        /// <summary>ボトムとエプロンで使用するパレット。</summary>
        private static readonly Color[] BottomColors =
        {
            new Color(0.19f, 0.24f, 0.35f), new Color(0.33f, 0.39f, 0.48f),
            new Color(0.53f, 0.40f, 0.30f), new Color(0.72f, 0.66f, 0.53f),
            new Color(0.28f, 0.36f, 0.28f), new Color(0.52f, 0.29f, 0.34f)
        };
        /// <summary>靴のパレット。旧保存データの0は従来の墨色。</summary>
        private static readonly Color[] ShoeColors =
        {
            new Color(0.10f, 0.11f, 0.14f), new Color(0.40f, 0.24f, 0.15f),
            new Color(0.75f, 0.22f, 0.22f), new Color(0.24f, 0.40f, 0.65f),
            new Color(0.91f, 0.85f, 0.69f), new Color(0.26f, 0.44f, 0.31f)
        };
        /// <summary>頬の色。怒り時はRigがこのレンダラーだけを染める。</summary>
        private static readonly Color CheekColor = new Color(0.93f, 0.48f, 0.45f);
        /// <summary>目、靴、小物の共通色。</summary>
        private static readonly Color DarkColor = new Color(0.10f, 0.11f, 0.14f);
        /// <summary>帽子と小物の共通色。</summary>
        private static readonly Color LightColor = new Color(0.96f, 0.94f, 0.87f);
        /// <summary>供給側の共有マテリアルを色単位で保持する。</summary>
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        /// <summary>既定供給関数が所有するマテリアルを色と滑らかさで共有する。</summary>
        private static readonly Dictionary<Tuple<Color, float>, Material> DefaultMaterials = new Dictionary<Tuple<Color, float>, Material>();
        /// <summary>visualごとにFactory自身が生成した合成メッシュだけを追跡する。</summary>
        private static readonly Dictionary<Transform, List<Mesh>> OwnedVisualMeshes = new Dictionary<Transform, List<Mesh>>();
        /// <summary>マテリアルを生成または取得する供給関数。</summary>
        private static Func<Color, float, Material> materialProvider = DefaultMaterial;
        /// <summary>合成元として共有する球メッシュ。個体の解放時には破棄しない。</summary>
        private static Mesh sphereMesh;
        /// <summary>合成元として共有する箱メッシュ。個体の解放時には破棄しない。</summary>
        private static Mesh boxMesh;
        /// <summary>合成元として共有する上半球メッシュ。個体の解放時には破棄しない。</summary>
        private static Mesh hairCapMesh;
        /// <summary>帽子とヘッドホン用の全周上半球。髪の顔開口メッシュとは分離する。</summary>
        private static Mesh hatCapMesh;
        /// <summary>尖った毛束に使う共有円錐。全個体で再利用する。</summary>
        private static Mesh spikeMesh;

        /// <summary>
        /// 色と滑らかさから共有マテリアルを返す供給関数。未設定またはnull設定時は既定実装を使う。
        /// 供給関数を変更すると取得キャッシュのみ消去し、外部所有のマテリアルは破棄しない。
        /// </summary>
        public static Func<Color, float, Material> MaterialProvider
        {
            get { return materialProvider; }
            set
            {
                Func<Color, float, Material> provider = value ?? new Func<Color, float, Material>(DefaultMaterial);
                if (materialProvider == provider) return;
                materialProvider = provider;
                Materials.Clear();
            }
        }

        /// <summary>指定した親のローカル原点にキャラクターを生成し、初期外見をRigへ適用する。</summary>
        public static CharacterRig Create(CharacterAppearance appearance, string name, Transform parent)
        {
            if (appearance == null) throw new ArgumentNullException(nameof(appearance));
            RequireMaterialProvider();
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            try
            {
                CharacterRig rig = root.AddComponent<CharacterRig>();
                rig.ApplyAppearance(appearance);
                return rig;
            }
            catch
            {
                ReleaseVisual(root.transform);
                OwnedObjects.Destroy(root);
                throw;
            }
        }

        /// <summary>
        /// 新しい外見を構築してRigへ渡す。旧visualの解放と破棄はRig.Configureが担当する。
        /// Configureの末尾2引数は座位で下腿を折るための左右膝である。
        /// </summary>
        internal static void Rebuild(CharacterRig rig, CharacterAppearance appearance)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (appearance == null) throw new ArgumentNullException(nameof(appearance));
            RequireMaterialProvider();
            Transform visual = CreateTransform("Visual", rig.transform, Vector3.zero);
            visual.gameObject.SetActive(false);
            visual.localScale = Vector3.one * SafeHeight(appearance.height);
            try
            {
                OwnedVisualMeshes.Add(visual, new List<Mesh>());
                Transform body = CreateTransform("Body", visual, new Vector3(0f, 0.72f, 0f));
                Transform head = CreateTransform("Head", body, new Vector3(0f, 0.44f, 0f));
                float width = BuildWidth(appearance.build);
                Transform leftShoulder = CreateTransform("LeftShoulder", body, new Vector3(-0.23f * width, 0.22f, 0f));
                Transform rightShoulder = CreateTransform("RightShoulder", body, new Vector3(0.23f * width, 0.22f, 0f));
                Transform leftHip = CreateTransform("LeftHip", body, new Vector3(-0.12f, -0.12f, 0f));
                Transform rightHip = CreateTransform("RightHip", body, new Vector3(0.12f, -0.12f, 0f));
                Transform leftKnee = CreateTransform("LeftKnee", leftHip, new Vector3(0f, -0.27f, 0f));
                Transform rightKnee = CreateTransform("RightKnee", rightHip, new Vector3(0f, -0.27f, 0f));
                Transform handAnchor = CreateTransform("HandAnchor", body, new Vector3(0f, 0.02f, 0.34f));
                Color skin = Palette(SkinColors, appearance.skinTone);
                Color hair = appearance.elderly ? LightColor : Palette(HairColors, appearance.hairColor);
                Color top = Palette(TopColors, appearance.topColor);
                Color bottom = Palette(BottomColors, appearance.bottomColor);
                int rendererCount = 0;
                BuildBody(body, appearance, skin, top, bottom, ref rendererCount);
                BuildHead(head, appearance, skin, hair, ref rendererCount);
                BuildArm(leftShoulder, skin, top, width, ref rendererCount);
                BuildArm(rightShoulder, skin, top, width, ref rendererCount);
                Color trousers = NormalizeIndex(appearance.outfit, 13) == 4 ? top : bottom;
                BuildLeg(leftHip, leftKnee, trousers, Palette(ShoeColors, appearance.shoeColor), ref rendererCount);
                BuildLeg(rightHip, rightKnee, trousers, Palette(ShoeColors, appearance.shoeColor), ref rendererCount);
                Transform heldItem = BuildHeldItem(rightShoulder, appearance.holdItem, ref rendererCount);
                Transform businessBag = BuildBusinessBag(leftShoulder, appearance.bag, ref rendererCount);
                Transform leftLid = BuildLid("LeftLid", head, -0.105f, skin, ref rendererCount);
                Transform rightLid = BuildLid("RightLid", head, 0.105f, skin, ref rendererCount);
                Transform mouth = CreateTransform("Mouth", head, new Vector3(0f, -0.11f, 0.267f));
                Mesh[] mouths = BuildMouth(mouth, ref rendererCount);
                Transform leftBrow = BuildBrow("LeftBrow", head, -0.105f, hair, appearance.browStyle, ref rendererCount);
                Transform rightBrow = BuildBrow("RightBrow", head, 0.105f, hair, appearance.browStyle, ref rendererCount);
                Transform happyEyes = BuildHappyEyes(head, ref rendererCount);
                Transform sweat = BuildSweat(head, ref rendererCount);
                rig.Configure(visual, body, head, leftShoulder, rightShoulder, leftHip, rightHip,
                    handAnchor, leftLid, rightLid, mouth, leftBrow, rightBrow, leftKnee, rightKnee);
                rig.ConfigureDetails(heldItem, businessBag, appearance.elderly);
                rig.ConfigureExpressions(mouth.GetComponentInChildren<MeshFilter>(), mouths, happyEyes, sweat,
                    FindCheekRenderer(head), appearance.mask, NormalizeIndex(appearance.eyewear, 3) == 2);
                leftLid.localScale = new Vector3(1f, 0.001f, 1f);
                rightLid.localScale = new Vector3(1f, 0.001f, 1f);
                visual.gameObject.SetActive(true);
            }
            catch
            {
                ReleaseVisual(visual);
                OwnedObjects.Destroy(visual.gameObject);
                throw;
            }
        }

        /// <summary>
        /// Rig.ConfigureとOnDestroyから呼び、旧visualを非表示にしてFactory所有の合成メッシュだけを破棄する。
        /// 外部小物のメッシュ、共有部品、マテリアルは解放対象にしない。visual自体の破棄はRigが担当する。
        /// </summary>
        internal static void ReleaseVisual(Transform visual)
        {
            if (visual == null) return;
            visual.gameObject.SetActive(false);
            List<Mesh> ownedMeshes;
            if (!OwnedVisualMeshes.TryGetValue(visual, out ownedMeshes)) return;
            OwnedVisualMeshes.Remove(visual);
            foreach (Mesh mesh in ownedMeshes) OwnedObjects.Destroy(mesh);
            ownedMeshes.Clear();
        }

        /// <summary>構築先の祖先visualから合成メッシュの所有リストを取得し、未登録の階層への生成を拒否する。</summary>
        private static List<Mesh> FindOwnedMeshes(Transform parent)
        {
            Transform owner = parent;
            while (owner != null)
            {
                List<Mesh> ownedMeshes;
                if (OwnedVisualMeshes.TryGetValue(owner, out ownedMeshes)) return ownedMeshes;
                owner = owner.parent;
            }
            throw new InvalidOperationException("合成メッシュの所有visualが登録されていません。");
        }

        /// <summary>胴体の上着、腰帯、制服、小物と任意のエプロンを体格に合わせて生成する。</summary>
        private static void BuildBody(Transform body, CharacterAppearance appearance, Color skin, Color top, Color bottom, ref int count)
        {
            float width = BuildWidth(appearance.build);
            float depth = NormalizeIndex(appearance.build, 4) == 3 ? 1.25f : width;
            MeshBatch batch = new MeshBatch(new Vector3(width, 1f, depth));
            batch.Sphere(top, new Vector3(0f, 0.08f, 0f), new Vector3(0.24f, 0.25f, 0.15f));
            batch.Sphere(bottom, new Vector3(0f, -0.105f, 0f), new Vector3(0.22f, 0.10f, 0.145f));
            batch.Sphere(skin, new Vector3(0f, 0.30f, 0f), new Vector3(0.085f, 0.10f, 0.085f));
            batch.Line(LightColor, new Vector3(-0.09f, 0.265f, 0.105f), new Vector3(0f, 0.215f, 0.15f), 0.022f);
            batch.Line(LightColor, new Vector3(0.09f, 0.265f, 0.105f), new Vector3(0f, 0.215f, 0.15f), 0.022f);
            BuildOutfit(batch, NormalizeIndex(appearance.outfit, 13), top, bottom, appearance.looseTie);
            BuildBag(batch, NormalizeIndex(appearance.bag, 6), bottom);
            if (appearance.apron)
            {
                Color apron = Palette(BottomColors, appearance.apronColor);
                float front = NormalizeIndex(appearance.outfit, 13) == 0 ? 0.153f : 0.205f;
                batch.Sphere(apron, new Vector3(0f, 0.015f, front - 0.025f), new Vector3(0.195f, 0.205f, 0.062f));
                for (int side = -1; side <= 1; side += 2)
                {
                    batch.Line(apron, new Vector3(side * 0.09f, 0.17f, front), new Vector3(side * 0.10f, 0.29f, 0.04f), 0.032f);
                    batch.Line(apron, new Vector3(side * 0.17f, 0.025f, front), new Vector3(side * 0.22f, 0.025f, -0.06f), 0.027f);
                }
                batch.Sphere(bottom, new Vector3(0f, -0.075f, front + 0.035f), new Vector3(0.09f, 0.057f, 0.014f));
                batch.Line(LightColor, new Vector3(-0.078f, -0.025f, front + 0.045f), new Vector3(0.078f, -0.025f, front + 0.045f), 0.009f);
            }
            batch.Build(body, ref count, true);
        }

        /// <summary>標準時の寸法を維持し、体格番号に応じて胴体と腕の幅を取得する。</summary>
        private static float BuildWidth(int build)
        {
            switch (NormalizeIndex(build, 4))
            {
                case 1: return 1.18f;
                case 2: return 0.85f;
                case 3: return 1.32f;
                default: return 1f;
            }
        }

        /// <summary>襟、前開き、柄、ポケットで13種類の服装を描き分ける。</summary>
        private static void BuildOutfit(MeshBatch batch, int outfit, Color top, Color bottom, bool looseTie)
        {
            switch (outfit)
            {
                case 1:
                case 7:
                case 11:
                case 12:
                    batch.Box(LightColor, new Vector3(0f, 0.11f, 0.155f), new Vector3(0.13f, 0.28f, 0.027f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        batch.Line(bottom, new Vector3(side * 0.125f, 0.26f, 0.13f),
                            new Vector3(side * 0.05f, 0.055f, 0.18f), 0.045f);
                        batch.Line(LightColor, new Vector3(side * 0.05f, 0.27f, 0.10f),
                            new Vector3(side * 0.075f, 0.20f, 0.165f), 0.026f);
                    }
                    if (outfit == 12)
                    {
                        batch.Sphere(DarkColor, new Vector3(-0.037f, 0.218f, 0.183f), new Vector3(0.043f, 0.024f, 0.015f));
                        batch.Sphere(DarkColor, new Vector3(0.037f, 0.218f, 0.183f), new Vector3(0.043f, 0.024f, 0.015f));
                    }
                    else if (outfit != 11)
                    {
                        batch.Sphere(bottom, new Vector3(looseTie ? 0.015f : 0f, looseTie ? 0.175f : 0.225f, 0.184f), new Vector3(0.026f, 0.026f, 0.015f));
                        batch.Box(bottom, new Vector3(looseTie ? 0.025f : 0f, looseTie ? 0.075f : 0.12f, 0.183f), new Vector3(0.037f, 0.16f, 0.02f));
                    }
                    if (outfit == 7)
                        batch.Box(LightColor, new Vector3(-0.12f, 0.145f, 0.164f), new Vector3(0.047f, 0.045f, 0.016f));
                    AddButtons(batch, DarkColor, -0.055f, 2);
                    break;
                case 2:
                    batch.Sphere(top, new Vector3(0f, 0.24f, -0.09f), new Vector3(0.205f, 0.11f, 0.15f));
                    batch.Sphere(DarkColor, new Vector3(0f, 0.28f, -0.09f), new Vector3(0.14f, 0.025f, 0.10f));
                    batch.Box(LightColor, new Vector3(-0.05f, 0.20f, 0.149f), new Vector3(0.012f, 0.12f, 0.018f));
                    batch.Box(LightColor, new Vector3(0.05f, 0.20f, 0.149f), new Vector3(0.012f, 0.12f, 0.018f));
                    batch.Box(bottom, new Vector3(0f, -0.04f, 0.16f), new Vector3(0.21f, 0.065f, 0.018f));
                    break;
                case 3:
                case 8:
                    batch.Box(DarkColor, new Vector3(0f, 0.09f, 0.16f), new Vector3(0.022f, 0.31f, 0.023f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        batch.Box(LightColor, new Vector3(side * 0.12f, 0.12f, 0.145f), new Vector3(0.025f, 0.25f, 0.027f));
                        batch.Box(bottom, new Vector3(side * 0.14f, -0.035f, 0.144f), new Vector3(0.10f, 0.085f, 0.028f));
                    }
                    batch.Box(LightColor, new Vector3(0f, 0f, 0.173f), new Vector3(0.36f, 0.027f, 0.021f));
                    if (outfit == 8)
                        batch.Box(LightColor, new Vector3(-0.075f, 0.205f, 0.152f), new Vector3(0.055f, 0.04f, 0.022f));
                    break;
                case 4:
                    batch.Line(LightColor, new Vector3(-0.085f, 0.255f, 0.10f), new Vector3(0f, 0.155f, 0.169f), 0.022f);
                    batch.Line(LightColor, new Vector3(0.085f, 0.255f, 0.10f), new Vector3(0f, 0.155f, 0.169f), 0.022f);
                    batch.Box(LightColor, new Vector3(-0.12f, 0.09f, 0.15f), new Vector3(0.077f, 0.067f, 0.023f));
                    batch.Box(top, new Vector3(-0.12f, 0.09f, 0.168f), new Vector3(0.04f, 0.016f, 0.01f));
                    batch.Box(top, new Vector3(-0.12f, 0.09f, 0.168f), new Vector3(0.016f, 0.04f, 0.01f));
                    break;
                case 5:
                    batch.Box(LightColor, new Vector3(0f, 0.08f, 0.16f), new Vector3(0.018f, 0.32f, 0.022f));
                    for (int side = -1; side <= 1; side += 2)
                        batch.Line(LightColor, new Vector3(side * 0.10f, 0.28f, 0.08f),
                            new Vector3(side * 0.21f, 0.15f, 0.09f), 0.03f);
                    break;
                case 6:
                    batch.Sphere(top, new Vector3(0f, -0.15f, 0f), new Vector3(0.255f, 0.17f, 0.17f));
                    batch.Box(bottom, new Vector3(0f, -0.045f, 0.164f), new Vector3(0.39f, 0.033f, 0.021f));
                    batch.Sphere(LightColor, new Vector3(0f, 0.245f, 0.125f), new Vector3(0.075f, 0.028f, 0.018f));
                    break;
                case 9:
                    for (int row = 0; row < 3; row++)
                    {
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 flower = new Vector3(side * (row == 1 ? 0.14f : 0.10f), 0.19f - row * 0.10f, 0.151f);
                            batch.Sphere(LightColor, flower, new Vector3(0.039f, 0.024f, 0.014f));
                            batch.Sphere(LightColor, flower, new Vector3(0.024f, 0.039f, 0.014f));
                            batch.Sphere(bottom, flower + Vector3.forward * 0.011f, new Vector3(0.011f, 0.011f, 0.009f));
                        }
                    }
                    AddButtons(batch, LightColor, 0f, 4);
                    break;
                case 10:
                    batch.Box(LightColor, new Vector3(0f, 0.10f, 0.156f), new Vector3(0.075f, 0.28f, 0.024f));
                    batch.Line(bottom, new Vector3(-0.08f, 0.25f, 0.11f), new Vector3(-0.04f, -0.08f, 0.15f), 0.022f);
                    batch.Line(bottom, new Vector3(0.08f, 0.25f, 0.11f), new Vector3(0.04f, -0.08f, 0.15f), 0.022f);
                    AddButtons(batch, DarkColor, 0.04f, 4);
                    break;
            }
        }

        /// <summary>前面に一定間隔の小さなボタンを追加する。</summary>
        private static void AddButtons(MeshBatch batch, Color color, float x, int number)
        {
            for (int index = 0; index < number; index++)
                batch.Sphere(color, new Vector3(x, 0.12f - index * 0.055f, 0.185f), new Vector3(0.01f, 0.01f, 0.008f));
        }

        /// <summary>背負い鞄と肩掛け鞄、首掛けカメラを胴体に固定する。</summary>
        private static void BuildBag(MeshBatch batch, int bag, Color color)
        {
            if (bag == 1 || bag == 4)
            {
                if (bag == 1)
                {
                    batch.Sphere(color, new Vector3(0f, 0.05f, -0.21f), new Vector3(0.19f, 0.22f, 0.10f));
                    batch.Sphere(color, new Vector3(0f, -0.035f, -0.285f), new Vector3(0.145f, 0.07f, 0.05f));
                    batch.Box(LightColor, new Vector3(0f, -0.025f, -0.33f), new Vector3(0.20f, 0.018f, 0.014f));
                }
                else
                {
                    batch.Box(color, new Vector3(0f, 0.055f, -0.265f), new Vector3(0.39f, 0.39f, 0.23f));
                    batch.Box(LightColor, new Vector3(0f, -0.025f, -0.385f), new Vector3(0.28f, 0.025f, 0.018f));
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    batch.Line(DarkColor, new Vector3(side * 0.14f, 0.27f, 0.07f),
                        new Vector3(side * 0.16f, -0.08f, 0.12f), 0.032f);
                    batch.Line(DarkColor, new Vector3(side * 0.14f, 0.27f, -0.19f),
                        new Vector3(side * 0.14f, 0.27f, 0.07f), 0.032f);
                }
            }
            else if (bag == 2)
            {
                batch.Line(DarkColor, new Vector3(-0.18f, 0.26f, 0.08f), new Vector3(0.23f, -0.10f, 0.12f), 0.03f);
                batch.Box(color, new Vector3(0.235f, -0.12f, 0.02f), new Vector3(0.12f, 0.18f, 0.20f));
                batch.Box(LightColor, new Vector3(0.25f, -0.09f, 0.127f), new Vector3(0.036f, 0.027f, 0.015f));
            }
            else if (bag == 5)
            {
                for (int side = -1; side <= 1; side += 2)
                    batch.Line(DarkColor, new Vector3(side * 0.07f, 0.29f, 0.08f), new Vector3(side * 0.07f, 0.02f, 0.20f), 0.018f);
                batch.Box(DarkColor, new Vector3(0f, 0.005f, 0.21f), new Vector3(0.19f, 0.12f, 0.075f));
                batch.Sphere(LightColor, new Vector3(0f, 0.005f, 0.258f), new Vector3(0.045f, 0.045f, 0.02f));
                batch.Sphere(DarkColor, new Vector3(0f, 0.005f, 0.277f), new Vector3(0.028f, 0.028f, 0.013f));
            }
        }

        /// <summary>手持ち小物を右手の関節に追従させ、全色を1レンダラーにまとめる。</summary>
        private static Transform BuildHeldItem(Transform shoulder, int holdItem, ref int count)
        {
            int item = NormalizeIndex(holdItem, 4);
            if (item == 0) return null;
            Transform held = CreateTransform("HeldItem", shoulder, new Vector3(0f, -0.275f, 0.045f));
            MeshBatch batch = new MeshBatch();
            if (item == 1)
            {
                batch.Box(DarkColor, new Vector3(0f, 0.012f, 0.025f), new Vector3(0.075f, 0.13f, 0.023f));
                batch.Box(LightColor, new Vector3(0f, 0.02f, 0.038f), new Vector3(0.057f, 0.095f, 0.006f));
            }
            else if (item == 2)
            {
                batch.Box(DarkColor, new Vector3(0f, -0.045f, 0f), new Vector3(0.016f, 0.15f, 0.016f));
                batch.Sphere(LightColor, new Vector3(0f, -0.22f, 0f), new Vector3(0.04f, 0.18f, 0.04f));
                batch.Sphere(DarkColor, new Vector3(0.016f, 0.025f, 0f), new Vector3(0.022f, 0.018f, 0.016f));
            }
            else
            {
                batch.Sphere(LightColor, new Vector3(0f, 0.04f, 0.018f), new Vector3(0.046f, 0.065f, 0.046f));
                batch.Box(DarkColor, new Vector3(0f, 0.10f, 0.018f), new Vector3(0.10f, 0.015f, 0.10f));
                batch.Box(LightColor, new Vector3(0.02f, 0.145f, 0.018f), new Vector3(0.009f, 0.08f, 0.009f));
            }
            batch.Build(held, ref count, true);
            return held;
        }

        /// <summary>ビジネスバッグを左手に配置し、運搬時に独立して隠せるようにする。</summary>
        private static Transform BuildBusinessBag(Transform shoulder, int bag, ref int count)
        {
            if (NormalizeIndex(bag, 6) != 3) return null;
            Transform held = CreateTransform("BusinessBag", shoulder, new Vector3(0f, -0.275f, 0f));
            MeshBatch batch = new MeshBatch();
            batch.Box(DarkColor, new Vector3(0f, -0.11f, 0f), new Vector3(0.22f, 0.15f, 0.09f));
            batch.Line(DarkColor, new Vector3(-0.04f, -0.04f, 0f), new Vector3(-0.04f, 0f, 0f), 0.018f);
            batch.Line(DarkColor, new Vector3(0.04f, -0.04f, 0f), new Vector3(0.04f, 0f, 0f), 0.018f);
            batch.Box(DarkColor, Vector3.zero, new Vector3(0.095f, 0.018f, 0.018f));
            batch.Box(LightColor, new Vector3(0f, -0.065f, 0.052f), new Vector3(0.028f, 0.023f, 0.014f));
            batch.Build(held, ref count, true);
            return held;
        }

        /// <summary>頭、耳、目、頬、髪、小物、帽子を最大5色で合成する。</summary>
        private static void BuildHead(Transform head, CharacterAppearance appearance, Color skin, Color hair, ref int count)
        {
            MeshBatch batch = new MeshBatch();
            batch.Sphere(skin, Vector3.zero, new Vector3(0.29f, 0.29f, 0.265f));
            batch.Sphere(skin, new Vector3(-0.285f, -0.015f, 0f), new Vector3(0.047f, 0.065f, 0.045f));
            batch.Sphere(skin, new Vector3(0.285f, -0.015f, 0f), new Vector3(0.047f, 0.065f, 0.045f));
            batch.Sphere(skin, new Vector3(0f, -0.055f, 0.263f), new Vector3(0.029f, 0.026f, 0.025f));
            for (int side = -1; side <= 1; side += 2)
            {
                BuildEye(batch, side, NormalizeIndex(appearance.eyeStyle, 5));
                batch.Sphere(CheekColor, new Vector3(side * 0.174f, -0.065f, 0.218f),
                    appearance.blush ? new Vector3(0.067f, 0.047f, 0.016f) : new Vector3(0.045f, 0.025f, 0.012f));
                if (appearance.freckles)
                    for (int dot = 0; dot < 3; dot++)
                        batch.Sphere(hair, new Vector3(side * (0.15f + dot * 0.017f), -0.055f + (dot % 2) * 0.013f, 0.241f - dot * 0.008f), Vector3.one * 0.004f);
            }
            BuildFacialHair(batch, hair, NormalizeIndex(appearance.facialHair, 4));
            int headwear = NormalizeIndex(appearance.headwear, 7);
            int hairStyle = NormalizeIndex(appearance.hairStyle, 11);
            BuildHair(batch, hair, NormalizeIndex(appearance.hatStyle, 4) == 0 && headwear != 0 && hairStyle == 1 ? 0 : hairStyle);
            int accessory = NormalizeIndex(appearance.accessory, 5);
            int eyewear = NormalizeIndex(appearance.eyewear, 3);
            BuildAccessory(batch, accessory == 1 && eyewear != 0 ? 0 : accessory);
            if (eyewear != 0) BuildAccessory(batch, 1);
            if (eyewear == 2)
            {
                for (int side = -1; side <= 1; side += 2)
                    batch.Box(DarkColor, new Vector3(side * 0.105f, 0.02f, 0.289f), new Vector3(0.103f, 0.098f, 0.019f));
            }
            if (appearance.mask)
            {
                batch.Sphere(LightColor, new Vector3(0f, -0.085f, 0.263f), new Vector3(0.145f, 0.079f, 0.046f));
                for (int side = -1; side <= 1; side += 2)
                    batch.Line(LightColor, new Vector3(side * 0.12f, -0.065f, 0.268f),
                        new Vector3(side * 0.28f, -0.015f, 0.035f), 0.013f);
            }
            int hat = NormalizeIndex(appearance.hatStyle, 4);
            if (hat != 0) BuildHat(batch, hat);
            else if (headwear != 0) BuildHeadwear(batch, headwear);
            else if (accessory == 2) BuildHat(batch, 3);
            batch.Build(head, ref count);
        }

        /// <summary>5種類の目を輪郭と2点のハイライトで描き分ける。</summary>
        private static void BuildEye(MeshBatch batch, int side, int style)
        {
            float x = side * 0.105f;
            float height = style == 3 ? 0.021f : 0.052f;
            float tilt = style == 1 ? side * 0.016f : style == 2 ? -side * 0.016f : 0f;
            batch.Sphere(LightColor, new Vector3(x, 0.018f, 0.243f), new Vector3(0.043f, height, 0.023f));
            batch.Sphere(DarkColor, new Vector3(x, 0.018f, 0.263f), new Vector3(0.027f, height * 0.67f, 0.012f));
            if (style == 1 || style == 2 || style == 4)
                batch.Line(DarkColor, new Vector3(x - 0.037f, 0.045f + tilt, 0.273f),
                    new Vector3(x + 0.037f, 0.045f - tilt, 0.273f), 0.009f);
            batch.Sphere(LightColor, new Vector3(x - 0.009f, 0.018f + height * 0.30f, 0.278f), new Vector3(0.007f, 0.008f, 0.004f));
            batch.Sphere(LightColor, new Vector3(x + 0.010f, 0.012f, 0.278f), Vector3.one * 0.004f);
            if (style == 4)
                for (int lash = 0; lash < 2; lash++)
                    batch.Line(DarkColor, new Vector3(x + side * (0.024f + lash * 0.008f), 0.043f, 0.275f),
                        new Vector3(x + side * (0.037f + lash * 0.009f), 0.061f, 0.275f), 0.006f);
        }

        /// <summary>口ひげ、あごひげ、点状の無精ひげを髪色に統合する。</summary>
        private static void BuildFacialHair(MeshBatch batch, Color hair, int style)
        {
            if (style == 1)
                for (int side = -1; side <= 1; side += 2)
                    batch.Sphere(hair, new Vector3(side * 0.033f, -0.085f, 0.267f), new Vector3(0.04f, 0.015f, 0.013f));
            if (style == 2)
                batch.Sphere(hair, new Vector3(0f, -0.185f, 0.198f), new Vector3(0.075f, 0.044f, 0.03f));
            if (style == 3)
                for (int dot = 0; dot < 11; dot++)
                {
                    float x = (dot - 5) * 0.024f;
                    batch.Sphere(hair, new Vector3(x, -0.16f - (dot % 2) * 0.019f, 0.224f - Mathf.Abs(x) * 0.3f), new Vector3(0.004f, 0.008f, 0.004f));
                }
        }

        /// <summary>眉の柔らかい弧、太い直線、折れた輪郭を1レンダラーで生成する。</summary>
        private static Transform BuildBrow(string name, Transform head, float x, Color color, int style, ref int count)
        {
            Transform brow = CreateTransform(name, head, new Vector3(x, 0.089f, 0.267f));
            MeshBatch batch = new MeshBatch();
            int normalized = NormalizeIndex(style, 3);
            if (normalized == 1) batch.Line(color, new Vector3(-0.035f, 0f, 0f), new Vector3(0.035f, 0f, 0f), 0.017f);
            else if (normalized == 2)
            {
                batch.Line(color, new Vector3(-0.036f, 0f, 0f), new Vector3(-0.010f, 0.015f, 0f), 0.012f);
                batch.Line(color, new Vector3(-0.010f, 0.015f, 0f), new Vector3(0.036f, 0.005f, 0f), 0.012f);
            }
            else AddArc(batch, color, 0.036f, 0.009f, 0.010f, Vector3.zero);
            batch.Build(brow, ref count);
            return brow;
        }

        /// <summary>短い線分の連続で弧を描く。正の高さは山、負はにっこりした谷になる。</summary>
        private static void AddArc(MeshBatch batch, Color color, float halfWidth, float height, float thickness, Vector3 center)
        {
            for (int segment = 0; segment < 8; segment++)
            {
                float a = segment / 4f - 1f;
                float b = (segment + 1) / 4f - 1f;
                batch.Line(color, center + new Vector3(a * halfWidth, height * (1f - a * a), 0f),
                    center + new Vector3(b * halfWidth, height * (1f - b * b), 0f), thickness);
            }
        }

        /// <summary>気分4種と食事用の口を先に生成し、描画用の1個だけを関節に配置する。</summary>
        private static Mesh[] BuildMouth(Transform mouth, ref int count)
        {
            if (count >= MaximumRenderers)
                throw new InvalidOperationException("キャラクターのレンダラー数が23個を超えました。");
            Mesh[] shapes = new Mesh[5];
            for (int shape = 0; shape < shapes.Length; shape++)
            {
                MeshBatch batch = new MeshBatch();
                if (shape == 4) batch.Sphere(DarkColor, Vector3.zero, new Vector3(0.028f, 0.031f, 0.012f));
                else if (shape == 3)
                    for (int segment = 0; segment < 4; segment++)
                        batch.Line(DarkColor, new Vector3(-0.044f + segment * 0.022f, segment % 2 == 0 ? 0f : 0.012f, 0f),
                            new Vector3(-0.022f + segment * 0.022f, segment % 2 == 0 ? 0.012f : 0f, 0f), 0.010f);
                else AddArc(batch, DarkColor, shape == 0 ? 0.052f : 0.032f,
                    shape == 0 ? -0.027f : shape == 1 ? -0.008f : 0.016f, 0.009f, Vector3.zero);
                shapes[shape] = batch.BakeSingle(mouth);
            }
            Transform part = CreateTransform("ColorMesh", mouth, Vector3.zero);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = shapes[1];
            MeshRenderer renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SharedMaterial(DarkColor);
            count++;
            return shapes;
        }

        /// <summary>両目の喜びの弧を同じ可動パーツに合成する。</summary>
        private static Transform BuildHappyEyes(Transform head, ref int count)
        {
            Transform eyes = CreateTransform("HappyEyes", head, Vector3.zero);
            MeshBatch batch = new MeshBatch();
            for (int side = -1; side <= 1; side += 2)
                AddArc(batch, DarkColor, 0.034f, 0.022f, 0.010f, new Vector3(side * 0.105f, 0.007f, 0.319f));
            batch.Build(eyes, ref count);
            eyes.gameObject.SetActive(false);
            return eyes;
        }

        /// <summary>頭上アイコンと区別する小さなこめかみの汗粒を生成する。</summary>
        private static Transform BuildSweat(Transform head, ref int count)
        {
            Transform sweat = CreateTransform("FaceSweat", head, new Vector3(0.24f, 0.075f, 0.158f));
            MeshBatch batch = new MeshBatch();
            batch.Sphere(new Color(0.40f, 0.72f, 0.84f), Vector3.zero, new Vector3(0.015f, 0.024f, 0.012f));
            batch.Build(sweat, ref count);
            sweat.gameObject.SetActive(false);
            return sweat;
        }

        /// <summary>頬だけを含む既存の色別レンダラーを初回生成時に探す。</summary>
        private static MeshRenderer FindCheekRenderer(Transform head)
        {
            MeshRenderer[] renderers = head.GetComponentsInChildren<MeshRenderer>(true);
            Material cheek = SharedMaterial(CheekColor);
            foreach (MeshRenderer renderer in renderers)
                if (renderer.sharedMaterial == cheek) return renderer;
            return null;
        }

        /// <summary>11種類の髪型を同じ髪色の合成パーツで描き分ける。</summary>
        private static void BuildHair(MeshBatch batch, Color hair, int style)
        {
            float capHeight = style == 4 ? 0.286f : 0.305f;
            batch.Add(hair, HairCap(), new Vector3(0f, 0f, -0.015f), new Vector3(0.307f, capHeight, 0.286f));
            if (style == 4) return;
            for (int lockIndex = 0; lockIndex < 5; lockIndex++)
            {
                float x = -0.19f + lockIndex * 0.088f;
                float y = style == 9 ? 0.177f + lockIndex * 0.013f : 0.185f + Mathf.Abs(x) * 0.12f;
                float z = 0.265f * Mathf.Sqrt(Mathf.Max(0.015f, 1f - (x * x + y * y) / (0.29f * 0.29f))) - 0.008f;
                batch.Sphere(hair, new Vector3(x, y, z), new Vector3(0.075f, style == 9 ? 0.075f : 0.065f, 0.058f));
            }
            if (style == 1)
                batch.Sphere(hair, new Vector3(0f, 0.29f, -0.09f), new Vector3(0.115f, 0.10f, 0.11f));
            if (style == 2 || style == 5)
            {
                float length = style == 5 ? 0.26f : 0.16f;
                batch.Sphere(hair, new Vector3(-0.252f, -0.04f, -0.025f), new Vector3(0.071f, length, 0.20f));
                batch.Sphere(hair, new Vector3(0.252f, -0.04f, -0.025f), new Vector3(0.071f, length, 0.20f));
                batch.Sphere(hair, new Vector3(0f, -0.04f, -0.22f), new Vector3(0.255f, length, 0.085f));
            }
            if (style == 3)
            {
                batch.Sphere(hair, new Vector3(0f, 0.13f, -0.25f), new Vector3(0.12f, 0.11f, 0.10f));
                batch.Sphere(hair, new Vector3(0f, -0.09f, -0.31f), new Vector3(0.105f, 0.24f, 0.10f));
            }
            if (style == 6 || style == 10)
                for (int side = -1; side <= 1; side += 2)
                    for (int lockIndex = 0; lockIndex < 4; lockIndex++)
                        batch.Sphere(hair, new Vector3(side * (0.27f + Mathf.Sin(lockIndex * 1.8f) * 0.025f),
                            0.06f - lockIndex * 0.10f, style == 6 ? -0.12f : -0.04f), new Vector3(0.082f, 0.105f, 0.13f));
            if (style == 7)
                for (int spike = 0; spike < 7; spike++)
                {
                    float angle = (spike - 3) * 0.32f;
                    batch.Spike(hair, new Vector3(Mathf.Sin(angle) * 0.22f, 0.22f, -0.04f),
                        new Vector3(Mathf.Sin(angle) * 0.34f, 0.32f + Mathf.Cos(angle) * 0.045f, -0.04f), 0.10f);
                }
            if (style == 8)
            {
                for (int curl = 0; curl < 12; curl++)
                {
                    float angle = curl * Mathf.PI * 2f / 12f;
                    batch.Sphere(hair, new Vector3(Mathf.Cos(angle) * 0.25f, 0.16f, Mathf.Sin(angle) * 0.22f - 0.035f), Vector3.one * 0.095f);
                }
                for (int curl = 0; curl < 6; curl++)
                {
                    float angle = curl * Mathf.PI / 3f;
                    batch.Sphere(hair, new Vector3(Mathf.Cos(angle) * 0.14f, 0.265f, Mathf.Sin(angle) * 0.13f - 0.035f), Vector3.one * 0.085f);
                }
            }
        }

        /// <summary>眼鏡、ヘッドホン、マフラーを目と帽子の既存色に統合する。</summary>
        private static void BuildAccessory(MeshBatch batch, int accessory)
        {
            if (accessory == 1)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * 0.105f;
                    batch.Box(DarkColor, new Vector3(x, 0.077f, 0.28f), new Vector3(0.11f, 0.012f, 0.017f));
                    batch.Box(DarkColor, new Vector3(x, -0.036f, 0.28f), new Vector3(0.11f, 0.012f, 0.017f));
                    batch.Box(DarkColor, new Vector3(x - 0.055f, 0.02f, 0.28f), new Vector3(0.012f, 0.12f, 0.017f));
                    batch.Box(DarkColor, new Vector3(x + 0.055f, 0.02f, 0.28f), new Vector3(0.012f, 0.12f, 0.017f));
                }
                batch.Box(DarkColor, new Vector3(0f, 0.026f, 0.28f), new Vector3(0.10f, 0.012f, 0.017f));
            }
            else if (accessory == 3)
            {
                batch.Sphere(DarkColor, new Vector3(-0.32f, 0f, 0f), new Vector3(0.048f, 0.093f, 0.082f));
                batch.Sphere(DarkColor, new Vector3(0.32f, 0f, 0f), new Vector3(0.048f, 0.093f, 0.082f));
                batch.Add(DarkColor, HatCap(), Vector3.zero, new Vector3(0.335f, 0.325f, 0.08f));
            }
            else if (accessory == 4)
            {
                batch.Sphere(LightColor, new Vector3(0f, -0.265f, 0f), new Vector3(0.19f, 0.057f, 0.17f));
                batch.Box(LightColor, new Vector3(0.105f, -0.35f, 0.145f), new Vector3(0.065f, 0.19f, 0.045f));
            }
        }

        /// <summary>コック帽、バンダナ、キャップを共通の明色で生成する。</summary>
        private static void BuildHat(MeshBatch batch, int style)
        {
            if (style == 0) return;
            if (style == 1)
            {
                batch.Box(LightColor, new Vector3(0f, 0.285f, 0f), new Vector3(0.43f, 0.13f, 0.39f));
                batch.Sphere(LightColor, new Vector3(0f, 0.39f, 0f), new Vector3(0.25f, 0.12f, 0.22f));
            }
            else if (style == 2)
            {
                batch.Add(LightColor, HatCap(), new Vector3(0f, 0.025f, -0.02f), new Vector3(0.316f, 0.296f, 0.293f));
                batch.Sphere(LightColor, new Vector3(0.27f, 0.015f, -0.10f), new Vector3(0.065f, 0.045f, 0.065f));
            }
            else
            {
                batch.Add(LightColor, HatCap(), new Vector3(0f, 0.018f, 0f), new Vector3(0.315f, 0.31f, 0.285f));
                batch.Sphere(LightColor, new Vector3(0f, 0.12f, 0.265f), new Vector3(0.25f, 0.028f, 0.15f));
            }
        }

        /// <summary>客用の6種類の帽子を顔の既存2色だけで描き分ける。</summary>
        private static void BuildHeadwear(MeshBatch batch, int style)
        {
            if (style == 3)
            {
                BuildHat(batch, 3);
                return;
            }
            if (style == 6)
            {
                batch.Sphere(DarkColor, new Vector3(0.04f, 0.285f, -0.015f), new Vector3(0.32f, 0.093f, 0.28f));
                batch.Box(DarkColor, new Vector3(0.055f, 0.38f, -0.02f), new Vector3(0.024f, 0.034f, 0.024f));
                return;
            }
            batch.Add(LightColor, HatCap(), new Vector3(0f, 0.027f, -0.005f), new Vector3(0.326f, 0.325f, 0.303f));
            if (style == 1)
            {
                batch.Sphere(LightColor, new Vector3(0f, 0.13f, 0f), new Vector3(0.35f, 0.025f, 0.32f));
                batch.Box(LightColor, new Vector3(0f, 0.34f, 0f), new Vector3(0.055f, 0.065f, 0.32f));
                batch.Box(DarkColor, new Vector3(0f, 0.20f, 0.297f), new Vector3(0.065f, 0.033f, 0.015f));
            }
            else if (style == 2)
            {
                for (int side = -1; side <= 1; side += 2)
                    batch.Sphere(LightColor, new Vector3(side * 0.31f, 0.025f, 0f), new Vector3(0.045f, 0.11f, 0.12f));
                batch.Box(DarkColor, new Vector3(0f, 0.125f, 0.285f), new Vector3(0.40f, 0.028f, 0.10f));
                batch.Line(DarkColor, new Vector3(-0.28f, 0f, 0f), new Vector3(0f, -0.25f, 0.10f), 0.018f);
                batch.Line(DarkColor, new Vector3(0.28f, 0f, 0f), new Vector3(0f, -0.25f, 0.10f), 0.018f);
            }
            else if (style == 4)
            {
                batch.Sphere(LightColor, new Vector3(0f, 0.13f, 0f), new Vector3(0.333f, 0.055f, 0.302f));
                batch.Sphere(LightColor, new Vector3(0f, 0.36f, -0.015f), new Vector3(0.062f, 0.065f, 0.062f));
                batch.Box(DarkColor, new Vector3(-0.10f, 0.145f, 0.293f), new Vector3(0.048f, 0.043f, 0.013f));
            }
            else if (style == 5)
            {
                batch.Sphere(LightColor, new Vector3(0f, 0.155f, 0f), new Vector3(0.41f, 0.026f, 0.37f));
                batch.Sphere(DarkColor, new Vector3(0f, 0.20f, -0.005f), new Vector3(0.309f, 0.025f, 0.285f));
            }
        }

        /// <summary>肩原点から下へ伸びる袖、袖口、親指付きの手を体格に合わせて1レンダラーで生成する。</summary>
        private static void BuildArm(Transform shoulder, Color skin, Color top, float width, ref int count)
        {
            MeshBatch batch = new MeshBatch(new Vector3(width, 1f, width));
            batch.Sphere(top, new Vector3(0f, -0.10f, 0f), new Vector3(0.085f, 0.145f, 0.09f));
            batch.Sphere(LightColor, new Vector3(0f, -0.21f, 0f), new Vector3(0.071f, 0.026f, 0.075f));
            batch.Sphere(skin, new Vector3(0f, -0.275f, 0f), new Vector3(0.060f, 0.075f, 0.062f));
            float side = shoulder.localPosition.x < 0f ? 1f : -1f;
            batch.Sphere(skin, new Vector3(side * 0.05f, -0.26f, 0.026f), new Vector3(0.029f, 0.038f, 0.032f));
            batch.Build(shoulder, ref count, true);
        }

        /// <summary>股関節の腿と膝の下腿・丸いつま先の靴を合計2レンダラーで生成する。</summary>
        private static void BuildLeg(Transform hip, Transform knee, Color bottom, Color shoe, ref int count)
        {
            MeshBatch thigh = new MeshBatch();
            thigh.Sphere(bottom, new Vector3(0f, -0.135f, 0f), new Vector3(0.093f, 0.155f, 0.10f));
            thigh.Build(hip, ref count);
            MeshBatch lower = new MeshBatch();
            lower.Sphere(bottom, new Vector3(0f, -0.12f, 0f), new Vector3(0.078f, 0.145f, 0.083f));
            lower.Sphere(shoe, new Vector3(0f, -0.275f, 0.045f), new Vector3(0.095f, 0.055f, 0.145f));
            lower.Sphere(LightColor, new Vector3(0f, -0.311f, 0.045f), new Vector3(0.098f, 0.015f, 0.145f));
            lower.Line(LightColor, new Vector3(-0.042f, -0.229f, 0.08f), new Vector3(0.042f, -0.229f, 0.08f), 0.011f);
            lower.Build(knee, ref count, true);
        }

        /// <summary>眼球を覆う肌色の瞼を閉眼時の単位倍率で生成し、Rigが基準値を取得した後で開眼する。</summary>
        private static Transform BuildLid(string name, Transform head, float x, Color skin, ref int count)
        {
            return BuildFacePart(name, head, new Vector3(x, 0.018f, 0.292f),
                new Vector3(0.058f, 0.066f, 0.017f), skin, ref count);
        }

        /// <summary>Rigが独立に変形できる口、眉、瞼の球状メッシュを生成する。</summary>
        private static Transform BuildFacePart(string name, Transform parent, Vector3 position, Vector3 radii, Color color, ref int count)
        {
            Transform part = CreateTransform(name, parent, position);
            MeshBatch batch = new MeshBatch();
            batch.Sphere(color, Vector3.zero, radii);
            batch.Build(part, ref count);
            return part;
        }

        /// <summary>親のローカル座標に単位スケールの空Transformを生成する。</summary>
        private static Transform CreateTransform(string name, Transform parent, Vector3 position)
        {
            Transform transform = new GameObject(name).transform;
            transform.SetParent(parent, false);
            transform.localPosition = position;
            return transform;
        }

        /// <summary>NaNと無限大は標準身長に戻し、有限値は子どもを含む安全な倍率に制限する。</summary>
        private static float SafeHeight(float height)
        {
            if (float.IsNaN(height) || float.IsInfinity(height)) return 1f;
            return Mathf.Clamp(height, 0.5f, 1.5f);
        }

        /// <summary>負値や整数最小値もオーバーフローなしで有効な番号に正規化する。</summary>
        private static int NormalizeIndex(int index, int length)
        {
            int remainder = index % length;
            return remainder < 0 ? remainder + length : remainder;
        }

        /// <summary>範囲外を正規化してパレットの色を取得する。</summary>
        private static Color Palette(Color[] palette, int index)
        {
            return palette[NormalizeIndex(index, palette.Length)];
        }

        /// <summary>マテリアル供給関数の存在を構築開始前に確認する。</summary>
        private static void RequireMaterialProvider()
        {
            if (materialProvider == null)
                throw new InvalidOperationException("CharacterFactory.MaterialProviderを生成前に設定してください。");
        }

        /// <summary>ResourcesのLitテンプレートを優先し、色と滑らかさごとに既定の共有マテリアルを生成する。</summary>
        private static Material DefaultMaterial(Color color, float smoothness)
        {
            Tuple<Color, float> key = Tuple.Create(color, smoothness);
            Material cached;
            if (DefaultMaterials.TryGetValue(key, out cached) && cached != null) return cached;
            Material template = Resources.Load<Material>("Materials/RestaurantLit");
            Material material;
            if (template != null)
            {
                if (template.shader == null)
                    throw new InvalidOperationException("RestaurantLitのシェーダーが設定されていません。");
                material = new Material(template);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    throw new InvalidOperationException("RestaurantLitテンプレートとURP/Litシェーダーが見つかりません。");
                material = new Material(shader);
            }
            try
            {
                material.name = "CharacterSharedMaterial";
                material.hideFlags = HideFlags.DontSave;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                else if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
                DefaultMaterials[key] = material;
                return material;
            }
            catch
            {
                OwnedObjects.Destroy(material);
                throw;
            }
        }

        /// <summary>共有マテリアルを取得し、供給側が破棄した場合は再取得する。</summary>
        private static Material SharedMaterial(Color color)
        {
            Material material;
            if (Materials.TryGetValue(color, out material) && material != null && material.shader != null) return material;
            material = materialProvider(color, Smoothness);
            if (material == null || material.shader == null)
                throw new InvalidOperationException("MaterialProviderが有効なシェーダー付きマテリアルを返しませんでした。");
            Materials[color] = material;
            return material;
        }

        /// <summary>合成用の共有球メッシュを遅延生成する。</summary>
        private static Mesh Sphere()
        {
            if (sphereMesh == null) sphereMesh = CreateSphere("CharacterSharedSphere", Mathf.PI);
            return sphereMesh;
        }

        /// <summary>正面の下端を額まで持ち上げた髪用の共有上半球を遅延生成する。</summary>
        private static Mesh HairCap()
        {
            if (hairCapMesh == null) hairCapMesh = CreateSphere("CharacterSharedHairCap", Mathf.PI * 0.53f, true);
            return hairCapMesh;
        }

        /// <summary>顔を覆わない額の高さまでの、帽子とヘッドホン用共有キャップを生成する。</summary>
        private static Mesh HatCap()
        {
            if (hatCapMesh == null) hatCapMesh = CreateSphere("CharacterSharedHatCap", Mathf.PI * 0.43f);
            return hatCapMesh;
        }

        /// <summary>
        /// 緯度経度から外向きの単位球面を生成する。faceOpening時は+Zの正面ほど最大極角を0.34πへ減らす。
        /// 最大極角はLerp(maxTheta,0.34π,SmoothStep(0,1,Max(0,sin(phi))))で、目の上方を開口する。
        /// </summary>
        private static Mesh CreateSphere(string name, float maxTheta, bool faceOpening = false)
        {
            const int latitude = 10;
            const int longitude = 16;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            for (int row = 0; row <= latitude; row++)
            {
                for (int column = 0; column <= longitude; column++)
                {
                    float phi = Mathf.PI * 2f * column / longitude;
                    float opening = faceOpening ? Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, Mathf.Sin(phi))) : 0f;
                    float thetaLimit = Mathf.Lerp(maxTheta, Mathf.PI * 0.34f, opening);
                    float theta = thetaLimit * row / latitude;
                    vertices.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)));
                }
            }
            for (int row = 0; row < latitude; row++)
            {
                for (int column = 0; column < longitude; column++)
                {
                    int a = row * (longitude + 1) + column;
                    int b = a + longitude + 1;
                    if (row > 0) AddTriangle(triangles, a, a + 1, b);
                    if (row < latitude - 1 || maxTheta < Mathf.PI) AddTriangle(triangles, a + 1, b + 1, b);
                }
            }
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            Vector3[] normals = new Vector3[vertices.Count];
            for (int index = 0; index < normals.Length; index++) normals[index] = vertices[index].normalized;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>三角形の頂点番号を順番どおりに追加する。</summary>
        private static void AddTriangle(List<int> triangles, int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>底面y=-0.5、先端y=0.5の低ポリ円錐を毛束として共有する。</summary>
        private static Mesh Spike()
        {
            if (spikeMesh != null) return spikeMesh;
            const int segments = 8;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            vertices.Add(new Vector3(0f, -0.5f, 0f));
            vertices.Add(new Vector3(0f, 0.5f, 0f));
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / segments;
                vertices.Add(new Vector3(Mathf.Cos(angle) * 0.5f, -0.5f, Mathf.Sin(angle) * 0.5f));
            }
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments + 2;
                AddTriangle(triangles, 0, segment + 2, next);
                AddTriangle(triangles, 1, next, segment + 2);
            }
            spikeMesh = new Mesh { name = "CharacterSharedSpike", hideFlags = HideFlags.HideAndDontSave };
            spikeMesh.SetVertices(vertices);
            spikeMesh.SetTriangles(triangles, 0);
            spikeMesh.RecalculateNormals();
            spikeMesh.RecalculateBounds();
            return spikeMesh;
        }

        /// <summary>面ごとに法線を分離した一辺1の共有箱を遅延生成する。</summary>
        private static Mesh Box()
        {
            if (boxMesh != null) return boxMesh;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 normal in normals)
            {
                Vector3 tangent = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 bitangent = Vector3.Cross(normal, tangent);
                int start = vertices.Count;
                vertices.Add((normal - tangent - bitangent) * 0.5f);
                vertices.Add((normal + tangent - bitangent) * 0.5f);
                vertices.Add((normal + tangent + bitangent) * 0.5f);
                vertices.Add((normal - tangent + bitangent) * 0.5f);
                AddTriangle(triangles, start, start + 1, start + 2);
                AddTriangle(triangles, start, start + 2, start + 3);
            }
            boxMesh = new Mesh { name = "CharacterSharedBox", hideFlags = HideFlags.HideAndDontSave };
            boxMesh.SetVertices(vertices);
            boxMesh.SetTriangles(triangles, 0);
            boxMesh.RecalculateNormals();
            boxMesh.RecalculateBounds();
            return boxMesh;
        }

        /// <summary>可動Transform内でパーツを色ごとに合成し、従来の色別レンダラーまたは色別サブメッシュを生成する。</summary>
        private sealed class MeshBatch
        {
            /// <summary>色ごとの合成対象を保持する。</summary>
            private readonly Dictionary<Color, List<CombineInstance>> parts = new Dictionary<Color, List<CombineInstance>>();

            /// <summary>関節階層を伸縮せずに各パーツへ適用する体格倍率。</summary>
            private readonly Vector3 geometryScale;

            /// <summary>標準体格でパーツの合成を準備する。</summary>
            internal MeshBatch() : this(Vector3.one) { }

            /// <summary>胴体または腕の形状だけに適用する倍率を指定する。</summary>
            internal MeshBatch(Vector3 geometryScale)
            {
                this.geometryScale = geometryScale;
            }

            /// <summary>中心と各軸の半径から球パーツを追加する。</summary>
            internal void Sphere(Color color, Vector3 position, Vector3 radii)
            {
                Add(color, CharacterFactory.Sphere(), position, radii);
            }

            /// <summary>中心と各軸の全長から箱パーツを追加する。</summary>
            internal void Box(Color color, Vector3 position, Vector3 size)
            {
                Add(color, CharacterFactory.Box(), position, size);
            }

            /// <summary>共有メッシュとローカル変換を色単位の合成対象へ追加する。</summary>
            internal void Add(Color color, Mesh mesh, Vector3 position, Vector3 scale)
            {
                AddRotated(color, mesh, position, scale, Quaternion.identity);
            }

            /// <summary>2点を結ぶ箱形の細いベルトや襟を追加する。</summary>
            internal void Line(Color color, Vector3 start, Vector3 end, float thickness)
            {
                Vector3 direction = end - start;
                if (direction.sqrMagnitude < 0.000001f) return;
                AddRotated(color, CharacterFactory.Box(), (start + end) * 0.5f,
                    new Vector3(thickness, direction.magnitude, thickness), Quaternion.FromToRotation(Vector3.up, direction));
            }

            /// <summary>根元と先端から円錐形の毛束を追加する。</summary>
            internal void Spike(Color color, Vector3 start, Vector3 end, float width)
            {
                Vector3 direction = end - start;
                if (direction.sqrMagnitude < 0.000001f) return;
                AddRotated(color, CharacterFactory.Spike(), (start + end) * 0.5f,
                    new Vector3(width, direction.magnitude, width), Quaternion.FromToRotation(Vector3.up, direction));
            }

            /// <summary>形状倍率と回転を合成し、色単位の合成対象へ追加する。</summary>
            private void AddRotated(Color color, Mesh mesh, Vector3 position, Vector3 scale, Quaternion rotation)
            {
                List<CombineInstance> instances;
                if (!parts.TryGetValue(color, out instances))
                {
                    instances = new List<CombineInstance>();
                    parts.Add(color, instances);
                }
                instances.Add(new CombineInstance { mesh = mesh,
                    transform = Matrix4x4.Scale(geometryScale) * Matrix4x4.TRS(position, rotation, scale) });
            }

            /// <summary>単色の表情メッシュを描画オブジェクトなしで生成し、visualの所有物として登録する。</summary>
            internal Mesh BakeSingle(Transform parent)
            {
                Mesh mesh = new Mesh { name = "CharacterExpression", hideFlags = HideFlags.DontSave };
                FindOwnedMeshes(parent).Add(mesh);
                foreach (KeyValuePair<Color, List<CombineInstance>> group in parts)
                    mesh.CombineMeshes(group.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }

            /// <summary>色別に合成し、必要なら色別サブメッシュを1レンダラーに集約する。</summary>
            internal void Build(Transform parent, ref int count, bool mergeColors = false)
            {
                if (mergeColors)
                {
                    BuildMultiColor(parent, ref count);
                    return;
                }
                List<Mesh> ownedMeshes = FindOwnedMeshes(parent);
                foreach (KeyValuePair<Color, List<CombineInstance>> group in parts)
                {
                    if (count >= MaximumRenderers)
                        throw new InvalidOperationException("キャラクターのレンダラー数が23個を超えました。");
                    Material material = SharedMaterial(group.Key);
                    Transform part = CreateTransform("ColorMesh", parent, Vector3.zero);
                    MeshFilter filter = part.gameObject.AddComponent<MeshFilter>();
                    Mesh mesh = new Mesh { name = "CharacterCombinedMesh", hideFlags = HideFlags.DontSave };
                    ownedMeshes.Add(mesh);
                    filter.sharedMesh = mesh;
                    mesh.CombineMeshes(group.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    MeshRenderer renderer = part.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    count++;
                }
            }

            /// <summary>色別の一時メッシュをサブメッシュとしてまとめ、一時所有物を成功・失敗とも解放する。</summary>
            private void BuildMultiColor(Transform parent, ref int count)
            {
                if (parts.Count == 0) return;
                if (count >= MaximumRenderers)
                    throw new InvalidOperationException("キャラクターのレンダラー数が23個を超えました。");
                List<Mesh> ownedMeshes = FindOwnedMeshes(parent);
                List<Mesh> temporaryMeshes = new List<Mesh>();
                List<CombineInstance> combined = new List<CombineInstance>();
                List<Material> materials = new List<Material>();
                try
                {
                    foreach (KeyValuePair<Color, List<CombineInstance>> group in parts)
                    {
                        materials.Add(SharedMaterial(group.Key));
                        Mesh colorMesh = new Mesh { name = "CharacterTemporaryColor", hideFlags = HideFlags.DontSave };
                        temporaryMeshes.Add(colorMesh);
                        colorMesh.CombineMeshes(group.Value.ToArray(), true, true);
                        combined.Add(new CombineInstance { mesh = colorMesh, transform = Matrix4x4.identity });
                    }
                    Transform part = CreateTransform("ColorMesh", parent, Vector3.zero);
                    MeshFilter filter = part.gameObject.AddComponent<MeshFilter>();
                    Mesh mesh = new Mesh { name = "CharacterCombinedMesh", hideFlags = HideFlags.DontSave };
                    ownedMeshes.Add(mesh);
                    filter.sharedMesh = mesh;
                    mesh.CombineMeshes(combined.ToArray(), false, true);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    MeshRenderer renderer = part.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = materials.ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    count++;
                }
                finally
                {
                    foreach (Mesh mesh in temporaryMeshes) OwnedObjects.Destroy(mesh);
                }
            }
        }
    }
}
