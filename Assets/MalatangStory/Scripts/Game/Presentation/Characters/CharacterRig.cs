using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>手続きアニメーションで使用する姿勢。</summary>
    public enum CharacterPose
    {
        /// <summary>呼吸しながら待機する。</summary>
        Idle,
        /// <summary>腕と脚を交互に振って歩く。</summary>
        Walk,
        /// <summary>座面の高さまで腰を下ろす。</summary>
        Sit,
        /// <summary>座って食事する。</summary>
        Eat,
        /// <summary>前方で調理する。</summary>
        Cook,
        /// <summary>両手で物を持つ。</summary>
        Carry,
        /// <summary>片手を振る。</summary>
        Wave,
        /// <summary>両手を上げて喜ぶ。</summary>
        Cheer,
        /// <summary>怒った姿勢を取る。</summary>
        Angry,
        /// <summary>口元に手を添えて考える。</summary>
        Think,
        WaitFidget,
        WaitSigh,
        Pray
    }

    /// <summary>目、眉、口に反映する気分。</summary>
    public enum CharacterMood
    {
        /// <summary>嬉しい表情。</summary>
        Happy,
        /// <summary>穏やかな表情。</summary>
        Neutral,
        /// <summary>待ちくたびれた表情。</summary>
        Impatient,
        /// <summary>怒った表情。</summary>
        Angry
    }

    /// <summary>頭上の手続き描画アイコン。</summary>
    public enum EmoteKind
    {
        /// <summary>表示しない。</summary>
        None,
        /// <summary>ハート。</summary>
        Heart,
        /// <summary>音符。</summary>
        Note,
        /// <summary>汗。</summary>
        Sweat,
        /// <summary>怒り。</summary>
        Anger,
        /// <summary>星。</summary>
        Star,
        /// <summary>疑問。</summary>
        Question,
        /// <summary>驚き。</summary>
        Exclamation,
        /// <summary>眠気。</summary>
        Sleep,
        /// <summary>唐辛子。</summary>
        Chili,
        /// <summary>硬貨。</summary>
        Coin,
        /// <summary>湯気。</summary>
        Steam
    }

    /// <summary>関節と表情を毎フレーム補間し、外部アセットなしでキャラクターを動かす。</summary>
    public sealed partial class CharacterRig : MonoBehaviour
    {
        private const int IconSize = 64;
        private static readonly Dictionary<EmoteKind, Material> EmoteMaterials = new Dictionary<EmoteKind, Material>();
        private static Mesh emoteMesh;
        private Transform visual;
        private Transform body;
        private Transform head;
        private Transform leftShoulder;
        private Transform rightShoulder;
        private Transform leftHip;
        private Transform rightHip;
        private Transform leftKnee;
        private Transform rightKnee;
        public float MotionLean;
        private float talkingSeconds;
        public void Talk(float seconds) { talkingSeconds = Mathf.Max(talkingSeconds, seconds); }
        private Transform handAnchor;
        private Transform leftLid;
        private Transform rightLid;
        private Transform mouth;
        private Transform leftBrow;
        private Transform rightBrow;
        /// <summary>生成時に用意した口形状。同じフィルターで交換し、フレーム中にメッシュを生成しない。</summary>
        private Mesh[] mouthShapes;
        private MeshFilter mouthFilter;
        private int mouthShape = -1;
        private Transform happyEyes;
        private Transform faceSweat;
        private MeshRenderer cheekRenderer;
        private bool faceMasked;
        private bool eyesHidden;
        private float angerTint = -1f;
        // フィールド初期化子で生成すると AddComponent 時に Unity が例外を出すため、初回使用時に作る。
        private MaterialPropertyBlock cheekBlock;
        private MaterialPropertyBlock cheekProperties => cheekBlock ??= new MaterialPropertyBlock();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Vector3 bodyRest;
        private Vector3 mouthRestScale;
        private Vector3 leftLidRestScale;
        private Vector3 rightLidRestScale;
        private Vector3 anchorRest;
        private Bounds headBounds;
        private Transform emote;
        private MeshRenderer emoteRenderer;
        private Camera emoteCamera;
        private CharacterPose pose;
        private CharacterMood mood = CharacterMood.Neutral;
        private float speed;
        private float cycle;
        private float clock;
        private float blinkTimer = 2f;
        private float blinkElapsed = -1f;
        private float reactionTime;
        private bool positiveReaction;
        private float emoteRemaining;
        private float emoteDuration;
        private float emoteAge;
        private Vector3 lookPoint;
        private bool hasLookPoint;
        private Transform carriedItem;
        private Transform carryOriginalParent;
        private Vector3 carryOriginalPosition;
        private Quaternion carryOriginalRotation;
        private Vector3 carryOriginalScale;
        /// <summary>外見が所有する右手小物。外部の運搬物とは所有権を分離する。</summary>
        private Transform heldAppearanceItem;
        /// <summary>外見が所有する左手のビジネスバッグ。</summary>
        private Transform businessBag;
        /// <summary>高齢者の軽い前傾を姿勢の基準として保持する。</summary>
        private bool elderly;

        /// <summary>目、口、眉に反映する気分を取得または設定する。</summary>
        public CharacterMood Mood
        {
            get { return mood; }
            set { mood = value >= CharacterMood.Happy && value <= CharacterMood.Angry ? value : CharacterMood.Neutral; }
        }

        /// <summary>両手の前に置かれた、小物を取り付けるためのアンカー。</summary>
        private float mealSeconds;
        private bool spicyMeal;
        public void SetSpicyMeal(bool value) => spicyMeal = value;
        /// <summary>器と同じ食事秒を使い、一口と頭・腕の動きを同期します。</summary>
        public void SetMealTime(float seconds) => mealSeconds = seconds;

        public Transform HandAnchor { get { return handAnchor; } }

        /// <summary>現在の姿勢から指定した姿勢へ滑らかに切り替える。</summary>
        public void SetPose(CharacterPose pose)
        {
            this.pose = pose >= CharacterPose.Idle && pose <= CharacterPose.Pray ? pose : CharacterPose.Idle;
            UpdateAppearanceItems();
        }

        /// <summary>移動速度をメートル毎秒で指定する。正の値で歩行周期を進める。</summary>
        public void SetMoveSpeed(float metersPerSecond)
        {
            speed = float.IsFinite(metersPerSecond) ? Mathf.Max(0f, metersPerSecond) : 0f;
        }

        /// <summary>指定時間だけ頭上に吹き出しを表示する。None または非正の時間で解除する。</summary>
        public void ShowEmote(EmoteKind kind, float seconds = 2f)
        {
            if (kind <= EmoteKind.None || kind > EmoteKind.Steam || !float.IsFinite(seconds) || seconds <= 0f)
            {
                emoteRemaining = 0f;
                if (emote != null) emote.gameObject.SetActive(false);
                return;
            }
            EnsureEmote();
            emoteRenderer.sharedMaterial = GetEmoteMaterial(kind);
            emoteDuration = seconds;
            emoteRemaining = seconds;
            emoteAge = 0f;
            emote.localScale = Vector3.zero;
            emote.gameObject.SetActive(true);
            emoteCamera = Camera.main;
            if (emoteCamera == null) emoteCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        }

        /// <summary>小物を両手の前に取り付ける。null で元の親とローカル姿勢に戻す。</summary>
        public void SetCarry(Transform item)
        {
            if (item == carriedItem) return;
            if (item != null && (item == transform || transform.IsChildOf(item) || item.IsChildOf(transform)))
                throw new ArgumentException("キャラクター自身の階層は持ち物にできません。", nameof(item));
            RestoreCarry();
            if (item == null) return;
            if (handAnchor == null) throw new InvalidOperationException("外見の生成後に持ち物を設定してください。");
            carriedItem = item;
            carryOriginalParent = item.parent;
            carryOriginalPosition = item.localPosition;
            carryOriginalRotation = item.localRotation;
            carryOriginalScale = item.localScale;
            AttachCarry();
        }

        /// <summary>頭を指定したワールド位置に軽く向ける。</summary>
        public void LookAt(Vector3 worldPoint)
        {
            if (!float.IsFinite(worldPoint.x) || !float.IsFinite(worldPoint.y) || !float.IsFinite(worldPoint.z)) return;
            lookPoint = worldPoint;
            hasLookPoint = true;
        }

        /// <summary>喜びのジャンプ、または落胆して肩を落とす反応を再生する。</summary>
        public void PlayReaction(bool positive)
        {
            positiveReaction = positive;
            reactionTime = 0.85f;
        }

        /// <summary>外見を再生成し、姿勢と持ち物を維持しながら色や小物を変更する。</summary>
        public void ApplyAppearance(CharacterAppearance appearance)
        {
            if (appearance == null) throw new ArgumentNullException(nameof(appearance));
            CharacterFactory.Rebuild(this, appearance);
        }

        /// <summary>生成済みの関節を受け取り、古い外見を安全に置き換える。</summary>
        internal void Configure(Transform newVisual, Transform newBody, Transform newHead,
            Transform newLeftShoulder, Transform newRightShoulder, Transform newLeftHip, Transform newRightHip,
            Transform newHandAnchor, Transform newLeftLid, Transform newRightLid, Transform newMouth,
            Transform newLeftBrow, Transform newRightBrow, Transform newLeftKnee, Transform newRightKnee)
        {
            Transform oldVisual = visual;
            Transform oldHandAnchor = handAnchor;
            visual = newVisual;
            body = newBody;
            head = newHead;
            ResetMealUtensils();
            leftShoulder = newLeftShoulder;
            rightShoulder = newRightShoulder;
            leftHip = newLeftHip;
            rightHip = newRightHip;
            leftKnee = newLeftKnee;
            rightKnee = newRightKnee;
            handAnchor = newHandAnchor;
            leftLid = newLeftLid;
            rightLid = newRightLid;
            mouth = newMouth;
            leftBrow = newLeftBrow;
            rightBrow = newRightBrow;
            bodyRest = body.localPosition;
            mouthRestScale = mouth.localScale;
            leftLidRestScale = leftLid.localScale;
            rightLidRestScale = rightLid.localScale;
            anchorRest = handAnchor.localPosition;
            headBounds = CalculateHeadBounds(head);
            AttachCarry();
            if (oldHandAnchor != null)
                while (oldHandAnchor.childCount > 0) oldHandAnchor.GetChild(0).SetParent(handAnchor, false);
            if (oldVisual != null)
            {
                oldVisual.gameObject.SetActive(false);
                CharacterFactory.ReleaseVisual(oldVisual);
                OwnedObjects.Destroy(oldVisual.gameObject);
            }
        }

        /// <summary>外見所有の手持ち小物と高齢者の前傾を、従来の関節設定とは独立して更新する。</summary>
        internal void ConfigureDetails(Transform newHeldItem, Transform newBusinessBag, bool isElderly)
        {
            heldAppearanceItem = newHeldItem;
            businessBag = newBusinessBag;
            elderly = isElderly;
            if (elderly) body.localRotation = Quaternion.Euler(8f, 0f, 0f);
            UpdateAppearanceItems();
        }

        /// <summary>生成済みの表情パーツを受け取り、外見更新後の参照と染色状態を初期化する。</summary>
        internal void ConfigureExpressions(MeshFilter newMouthFilter, Mesh[] newMouthShapes,
            Transform newHappyEyes, Transform newSweat, MeshRenderer newCheekRenderer, bool masked, bool hideEyes)
        {
            mouthFilter = newMouthFilter;
            mouthShapes = newMouthShapes;
            mouthShape = -1;
            happyEyes = newHappyEyes;
            faceSweat = newSweat;
            cheekRenderer = newCheekRenderer;
            faceMasked = masked;
            eyesHidden = hideEyes;
            leftLid.gameObject.SetActive(!hideEyes);
            rightLid.gameObject.SetActive(!hideEyes);
            angerTint = -1f;
            mouth.gameObject.SetActive(!masked);
            cheekProperties.Clear();
        }

        /// <summary>両手を使う運搬・食事・調理では外見の手持ち小物を隠す。</summary>
        private void UpdateAppearanceItems()
        {
            bool visible = carriedItem == null && pose != CharacterPose.Carry
                && pose != CharacterPose.Eat && pose != CharacterPose.Cook;
            if (heldAppearanceItem != null) heldAppearanceItem.gameObject.SetActive(visible);
            if (businessBag != null) businessBag.gameObject.SetActive(visible);
        }

        /// <summary>フレーム時間を使い、配列や一時オブジェクトを生成せず姿勢を更新する。</summary>
        private void Update()
        {
            if (body == null) return;
            float delta = Time.deltaTime;
            clock = Mathf.Repeat(clock + delta, 1000f);
            float normalizedSpeed = speed / Mathf.Max(0.1f, visual.localScale.y);
            float walkRate = normalizedSpeed > 0f ? Mathf.Min(normalizedSpeed / 0.55f, 20f) : 1.4f;
            cycle = Mathf.Repeat(cycle + delta * walkRate * Mathf.PI * 2f, Mathf.PI * 2f);
            reactionTime = Mathf.Max(0f, reactionTime - delta);
            talkingSeconds = Mathf.Max(0, talkingSeconds - delta);
            float blend = 1f - Mathf.Exp(-12f * delta);
            UpdateAppearanceItems();
            AnimatePose(blend, normalizedSpeed);
            AnimateFace(delta, blend);
            AnimateHead(blend);
            AnimateEmote(delta);
        }

        /// <summary>姿勢ごとの関節角度と腰の高さを計算して補間する。</summary>
        private void AnimatePose(float blend, float normalizedSpeed)
        {
            bool sitting = pose == CharacterPose.Sit || pose == CharacterPose.Eat || pose == CharacterPose.Pray;
            bool walking = !sitting && (speed > 0f || pose == CharacterPose.Walk);
            float stride = Mathf.Sin(cycle) * Mathf.Lerp(16f, 34f, Mathf.Clamp01(normalizedSpeed));
            Vector3 leftArm = new Vector3(0f, 0f, -7f);
            Vector3 rightArm = new Vector3(0f, 0f, 7f);
            Vector3 leftLeg = Vector3.zero;
            Vector3 rightLeg = Vector3.zero;
            float kneeAngle = sitting ? 85f : 0f;
            Vector3 position = bodyRest;
            float bodyTilt = elderly ? 8f : 0f;
            if (sitting)
            {
                // 子どもでも椅子座面は固定寸法なので、腰高を身長倍率で割り戻す。
                position.y = 0.45f / Mathf.Max(0.1f, visual.localScale.y) + 0.12f;
                leftLeg.x = -85f;
                rightLeg.x = -85f;
            }
            else
            {
                position.y += Mathf.Sin(clock * 2.4f) * 0.009f;
                if (walking)
                {
                    leftLeg.x = stride;
                    rightLeg.x = -stride;
                    leftArm.x = -stride * 0.75f;
                    rightArm.x = stride * 0.75f;
                    position.y += Mathf.Abs(Mathf.Sin(cycle)) * 0.035f;
                }
            }
            float gesture = Mathf.Sin(clock * 5f);
            switch (pose)
            {
                case CharacterPose.Eat:
                    float bite = Mathf.Repeat(mealSeconds, 3.5f);
                    EatingStage mealStage = CookingFeel.MealStage(mealSeconds);
                    float lifting = mealStage == EatingStage.Lift ? Mathf.Sin((bite - .8f) / .7f * Mathf.PI) : 0f;
                    if (mealStage == EatingStage.Sip) { lifting = .7f; bodyTilt += 8f; }
                    rightArm = new Vector3(-78f - lifting * 35f, -41f, 0f);
                    leftArm.x = -55f;
                    bodyTilt += 4f;
                    break;
                case CharacterPose.Pray:
                    leftArm = new Vector3(-90, 0, -30); rightArm = new Vector3(-90, 0, 30); bodyTilt += 8; break;
                case CharacterPose.WaitFidget:
                    leftArm = new Vector3(-45, 0, -40); rightArm = new Vector3(-75, 0, 45); rightLeg.x = Mathf.Sin(clock * 8) * 2; break;
                case CharacterPose.WaitSigh:
                    leftArm.x = -12; rightArm.x = -12; bodyTilt += 4 + Mathf.Sin(clock * 2) * 2; break;
                case CharacterPose.Cook:
                    leftArm = new Vector3(-65f + gesture * 20f, -15f, -15f);
                    rightArm = new Vector3(-75f - gesture * 25f, 15f, 15f);
                    bodyTilt += 7f;
                    break;
                case CharacterPose.Wave:
                    rightArm = new Vector3(-150f, 0f, 25f + gesture * 18f);
                    break;
                case CharacterPose.Cheer:
                    leftArm = new Vector3(0f, 0f, -155f + gesture * 7f);
                    rightArm = new Vector3(0f, 0f, 155f - gesture * 7f);
                    position.y += Mathf.Max(0f, gesture) * 0.13f;
                    break;
                case CharacterPose.Angry:
                    leftArm = new Vector3(-20f, -20f, -25f);
                    rightArm = new Vector3(-20f, 20f, 25f);
                    bodyTilt += 5f;
                    break;
                case CharacterPose.Think:
                    rightArm = new Vector3(-112f, -35f, 0f);
                    break;
            }
            if (pose == CharacterPose.Carry || carriedItem != null)
            {
                leftArm = new Vector3(-75f, 25f, 10f);
                rightArm = new Vector3(-75f, -25f, -10f);
            }
            if (reactionTime > 0f)
            {
                float reaction = Mathf.Sin((1f - reactionTime / 0.85f) * Mathf.PI);
                if (positiveReaction)
                {
                    if (!sitting) position.y += reaction * 0.2f;
                    leftArm.z -= reaction * 70f;
                    rightArm.z += reaction * 70f;
                }
                else
                {
                    bodyTilt += reaction * 13f;
                    position.y -= reaction * 0.04f;
                    leftArm.z *= 1f - reaction;
                    rightArm.z *= 1f - reaction;
                }
            }
            body.localPosition = Vector3.Lerp(body.localPosition, position, blend);
            RotateJoint(body, new Vector3(bodyTilt, 0f, MotionLean), blend);
            RotateJoint(leftShoulder, leftArm, blend);
            RotateJoint(rightShoulder, rightArm, blend);
            UpdateMealUtensils(blend);
            RotateJoint(leftHip, leftLeg, blend);
            RotateJoint(rightHip, rightLeg, blend);
            RotateJoint(leftKnee, new Vector3(kneeAngle, 0f, 0f), blend);
            RotateJoint(rightKnee, new Vector3(kneeAngle, 0f, 0f), blend);
            handAnchor.localPosition = Vector3.Lerp(handAnchor.localPosition,
                anchorRest + new Vector3(0f, (pose == CharacterPose.Carry || carriedItem != null ? 0.10f : 0f)
                    + Mathf.Sin(clock * 2.4f) * 0.005f, 0f), blend);
        }

        /// <summary>ランダム間隔のまばたきと気分に合わせた眉・口を更新する。</summary>
        private void AnimateFace(float delta, float blend)
        {
            blinkTimer -= delta;
            if (blinkTimer <= 0f && blinkElapsed < 0f)
            {
                blinkElapsed = 0f;
                blinkTimer = UnityEngine.Random.Range(2.3f, 5.2f);
            }
            float closure = 0f;
            if (blinkElapsed >= 0f)
            {
                blinkElapsed += delta;
                closure = Mathf.Sin(Mathf.Clamp01(blinkElapsed / 0.18f) * Mathf.PI);
                if (blinkElapsed >= 0.18f) blinkElapsed = -1f;
            }
            bool joyful = mood == CharacterMood.Happy || pose == CharacterPose.Cheer || (reactionTime > 0f && positiveReaction);
            bool angry = mood == CharacterMood.Angry || pose == CharacterPose.Angry;
            float restingLid = mood == CharacterMood.Impatient ? 0.4f : (mood == CharacterMood.Angry ? 0.2f : 0f);
            float lid = joyful ? 1f : Mathf.Max(0.001f, Mathf.Max(restingLid, closure));
            ScaleLid(leftLid, leftLidRestScale, lid);
            ScaleLid(rightLid, rightLidRestScale, lid);
            float browAngle = angry ? -22f : joyful ? 12f : mood == CharacterMood.Impatient ? -9f : 0f;
            RotateJoint(leftBrow, new Vector3(0f, 0f, browAngle), blend);
            RotateJoint(rightBrow, new Vector3(0f, 0f, -browAngle), blend);
            if (happyEyes != null) SetVisible(happyEyes, joyful && !eyesHidden && closure < 0.5f);
            int shape = joyful ? 0 : angry ? 3 : (int)mood;
            bool chewing = pose == CharacterPose.Eat && CookingFeel.MealStage(mealSeconds) == EatingStage.Chew && Mathf.Sin(clock * 5f) > 0f;
            if (chewing) shape = 4;
            if (mouthFilter != null && mouthShapes != null && mouthShape != shape)
            {
                mouthFilter.sharedMesh = mouthShapes[shape];
                mouthShape = shape;
            }
            Vector3 target = mouthRestScale;
            if (talkingSeconds > 0) target.y *= 1 + Mathf.Abs(Mathf.Sin(clock * 18)) * 1.2f;
            if (chewing) target.y *= 0.55f + Mathf.Max(0f, Mathf.Sin(clock * 5f)) * 0.45f;
            mouth.localScale = Vector3.Lerp(mouth.localScale, target, blend);
            RotateJoint(mouth, Vector3.zero, blend);
            AnimateFaceDetails(angry, blend);
        }

        /// <summary>頬の赤みを共有材質を複製せず更新し、待ちくたびれた時だけ汗粒を揺らす。</summary>
        private void AnimateFaceDetails(bool angry, float blend)
        {
            float target = angry || spicyMeal ? 1f : 0f;
            float tint = angerTint < 0f ? target : Mathf.Lerp(angerTint, target, blend);
            if (cheekRenderer != null && Mathf.Abs(tint - angerTint) > 0.001f)
            {
                Color color = Color.Lerp(new Color(0.93f, 0.48f, 0.45f), new Color(0.96f, 0.25f, 0.23f), tint);
                cheekProperties.SetColor(BaseColorId, color);
                cheekProperties.SetColor(ColorId, color);
                cheekRenderer.SetPropertyBlock(cheekProperties);
                angerTint = tint;
            }
            if (faceSweat != null)
            {
                bool visible = (mood == CharacterMood.Impatient || spicyMeal) && !faceMasked;
                SetVisible(faceSweat, visible);
                if (visible)
                    faceSweat.localPosition = new Vector3(0.24f, 0.075f - Mathf.Repeat(clock * 0.025f, 0.04f), 0.158f);
            }
        }

        /// <summary>可視状態が変わった時だけGameObjectを切り替える。</summary>
        private static void SetVisible(Transform part, bool visible)
        {
            if (part.gameObject.activeSelf != visible) part.gameObject.SetActive(visible);
        }

        /// <summary>注視位置を頭の親の座標へ変換し、首の可動域内で補間する。</summary>
        private void AnimateHead(float blend)
        {
            Vector3 rotation = Vector3.zero;
            if (hasLookPoint)
            {
                Vector3 direction = head.parent.InverseTransformDirection(lookPoint - head.position);
                if (direction.sqrMagnitude > 0.0001f)
                {
                    rotation.y = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -32f, 32f);
                    rotation.x = Mathf.Clamp(-Mathf.Atan2(direction.y,
                        Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z)) * Mathf.Rad2Deg, -18f, 18f);
                }
            }
            bool walking = pose != CharacterPose.Sit && pose != CharacterPose.Eat && (speed > 0f || pose == CharacterPose.Walk);
            if (walking)
            {
                rotation.z += Mathf.Sin(cycle) * 3f;
                rotation.x += Mathf.Abs(Mathf.Sin(cycle)) * 2f;
            }
            else if (!hasLookPoint && pose == CharacterPose.Idle)
            {
                float glance = Mathf.Repeat(clock, 9f);
                if (glance > 5f && glance < 8f)
                    rotation.y = Mathf.Sin((glance - 5f) * Mathf.PI * 2f / 3f) * 19f;
            }
            if (pose == CharacterPose.Eat)
            {
                float bitePhase = Mathf.Repeat(mealSeconds, 3.5f);
                rotation.x += bitePhase < 1.3f ? Mathf.Sin(bitePhase / 1.3f * Mathf.PI) * 12f : 0f;
            }
            if (pose == CharacterPose.Think) rotation.z = 10f;
            if (pose == CharacterPose.WaitSigh) rotation.x += 10 + Mathf.Sin(clock * 2) * 3;
            if (reactionTime > 0f && !positiveReaction) rotation.x += 12f * Mathf.Sin(reactionTime / 0.85f * Mathf.PI);
            RotateJoint(head, rotation, blend);
        }

        /// <summary>吹き出しをカメラへ向け、出現と消失の大きさを更新する。</summary>
        private void AnimateEmote(float delta)
        {
            if (emote == null || emoteRemaining <= 0f) return;
            emoteRemaining = Mathf.Max(0f, emoteRemaining - delta);
            emoteAge += delta;
            if (emoteRemaining <= 0f)
            {
                emote.gameObject.SetActive(false);
                return;
            }
            Matrix4x4 headMatrix = head.localToWorldMatrix;
            float worldExtentY = Mathf.Abs(headMatrix.m10) * headBounds.extents.x
                + Mathf.Abs(headMatrix.m11) * headBounds.extents.y + Mathf.Abs(headMatrix.m12) * headBounds.extents.z;
            float worldTop = head.TransformPoint(headBounds.center).y + worldExtentY;
            Vector3 bubblePosition = head.position;
            bubblePosition.y = worldTop + 0.23f + Mathf.Sin(emoteAge * 3f) * 0.015f;
            emote.position = bubblePosition;
            float pop = Mathf.Clamp01(emoteAge / 0.2f);
            float size = Mathf.Sin(pop * Mathf.PI * 0.5f) * (1f + Mathf.Sin(pop * Mathf.PI) * 0.18f);
            size *= Mathf.Clamp01(emoteRemaining / Mathf.Min(0.2f, emoteDuration));
            emote.localScale = Vector3.one * (0.36f * size);
            if (emoteCamera == null) emoteCamera = Camera.main;
            if (emoteCamera != null) emote.rotation = emoteCamera.transform.rotation;
        }

        /// <summary>持ち物を現在のアンカーへ移す。</summary>
        private void AttachCarry()
        {
            UpdateAppearanceItems();
            if (carriedItem == null || handAnchor == null) return;
            carriedItem.SetParent(handAnchor, false);
            carriedItem.localPosition = Vector3.zero;
            carriedItem.localRotation = Quaternion.identity;
            // 身長変更で持ち物まで伸縮すると器のワールド寸法が壊れる。
            Vector3 scale = handAnchor.lossyScale;
            Vector3 originalParentScale = carryOriginalParent != null ? carryOriginalParent.lossyScale : Vector3.one;
            carriedItem.localScale = new Vector3(
                carryOriginalScale.x * originalParentScale.x / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
                carryOriginalScale.y * originalParentScale.y / Mathf.Max(0.001f, Mathf.Abs(scale.y)),
                carryOriginalScale.z * originalParentScale.z / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
        }

        /// <summary>持ち物の元の親とローカル姿勢を復元する。</summary>
        private void RestoreCarry()
        {
            if (carriedItem != null)
            {
                carriedItem.SetParent(carryOriginalParent, false);
                carriedItem.localPosition = carryOriginalPosition;
                carriedItem.localRotation = carryOriginalRotation;
                carriedItem.localScale = carryOriginalScale;
            }
            carriedItem = null;
            carryOriginalParent = null;
            UpdateAppearanceItems();
        }

        /// <summary>共有メッシュを利用して吹き出し用レンダラーを一度だけ生成する。</summary>
        private void EnsureEmote()
        {
            if (emote != null) return;
            GameObject bubble = new GameObject("Emote");
            emote = bubble.transform;
            emote.SetParent(transform, false);
            MeshFilter filter = bubble.AddComponent<MeshFilter>();
            if (emoteMesh == null)
            {
                emoteMesh = CreateBubbleMesh();
            }
            filter.sharedMesh = emoteMesh;
            emoteRenderer = bubble.AddComponent<MeshRenderer>();
            emoteRenderer.shadowCastingMode = ShadowCastingMode.Off;
            emoteRenderer.receiveShadows = false;
        }

        /// <summary>頭部の全パーツを包含する、頭ローカル座標の境界を取得する。</summary>
        private static Bounds CalculateHeadBounds(Transform head)
        {
            Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
            MeshFilter[] filters = head.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Bounds bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                        (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                        (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                    Vector3 point = head.InverseTransformPoint(filter.transform.TransformPoint(bounds.center + offset));
                    combined.Encapsulate(point);
                }
            }
            return combined;
        }

        /// <summary>テクスチャの円と尾に合わせた平面ビルボードの共有メッシュを生成する。</summary>
        private static Mesh CreateBubbleMesh()
        {
            const int segments = 48;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            AddBubbleVertex(vertices, uv, 32f, 34f);
            for (int index = 0; index < segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                AddBubbleVertex(vertices, uv, 32f + Mathf.Cos(angle) * 28f, 34f + Mathf.Sin(angle) * 28f);
            }
            for (int index = 0; index < segments; index++)
            {
                triangles.Add(0);
                triangles.Add((index + 1) % segments + 1);
                triangles.Add(index + 1);
            }
            int tail = vertices.Count;
            AddBubbleVertex(vertices, uv, 21f, 10f);
            AddBubbleVertex(vertices, uv, 18f, 3f);
            AddBubbleVertex(vertices, uv, 28f, 8f);
            triangles.Add(tail);
            triangles.Add(tail + 2);
            triangles.Add(tail + 1);
            Mesh mesh = new Mesh { name = "CharacterEmoteBillboard" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>画素座標から平面頂点と対応する UV を追加する。</summary>
        private static void AddBubbleVertex(List<Vector3> vertices, List<Vector2> uv, float x, float y)
        {
            Vector2 coordinate = new Vector2(x / IconSize, y / IconSize);
            vertices.Add(new Vector3(coordinate.x - 0.5f, coordinate.y - 0.5f, 0f));
            uv.Add(coordinate);
        }

        /// <summary>既存の不透明シェーダーを使い、アイコンの材質とテクスチャを共有する。</summary>
        private static Material GetEmoteMaterial(EmoteKind kind)
        {
            Material cached;
            if (EmoteMaterials.TryGetValue(kind, out cached) && cached != null) return cached;
            Material template = Resources.Load<Material>("Materials/RestaurantUnlit");
            if (template == null) template = Resources.Load<Material>("Materials/RestaurantLit");
            Shader shader = template != null ? template.shader : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("吹き出し用 URP シェーダーが見つかりません。");
            Material material = template != null ? new Material(template) : new Material(shader);
            material.name = "CharacterEmote_" + kind;
            // 実行時の透明キーワードはビルドで除去されるため、輪郭をメッシュで切り抜く。
            material.SetFloat("_Cull", (float)CullMode.Off);
            Texture2D texture = DrawIcon(kind);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0f);
            EmoteMaterials[kind] = material;
            return material;
        }

        /// <summary>吹き出し背景と各アイコンを CPU 上で初回だけ描画する。</summary>
        private static Texture2D DrawIcon(EmoteKind kind)
        {
            Color32[] pixels = new Color32[IconSize * IconSize];
            Color32 paper = new Color32(255, 248, 229, 255);
            Color32 outline = new Color32(102, 73, 62, 255);
            for (int index = 0; index < pixels.Length; index++) pixels[index] = paper;
            Line(pixels, 18, 3, 28, 8, 1, outline);
            Circle(pixels, 32, 34, 29, outline);
            Circle(pixels, 32, 34, 27, paper);
            Line(pixels, 21, 10, 18, 3, 3, outline);
            Line(pixels, 22, 11, 19, 5, 2, paper);
            Color32 red = new Color32(212, 87, 88, 255);
            Color32 gold = new Color32(233, 164, 54, 255);
            Color32 blue = new Color32(86, 153, 183, 255);
            switch (kind)
            {
                case EmoteKind.Heart:
                    Circle(pixels, 24, 39, 9, red);
                    Circle(pixels, 40, 39, 9, red);
                    for (int y = 21; y < 40; y++) Line(pixels, 32 - (y - 20), y, 32 + (y - 20), y, 1, red);
                    break;
                case EmoteKind.Note:
                    Line(pixels, 28, 27, 28, 47, 2, outline);
                    Line(pixels, 28, 47, 43, 43, 3, outline);
                    Line(pixels, 43, 43, 43, 25, 2, outline);
                    Circle(pixels, 23, 25, 6, outline);
                    Circle(pixels, 38, 23, 6, outline);
                    break;
                case EmoteKind.Sweat:
                    for (int y = 30; y <= 49; y++) Line(pixels, 32 - (49 - y) / 2, y, 32 + (49 - y) / 2, y, 1, blue);
                    Circle(pixels, 32, 28, 10, blue);
                    Circle(pixels, 28, 30, 2, paper);
                    break;
                case EmoteKind.Anger:
                    for (int i = 0; i < 4; i++)
                    {
                        float angle = i * Mathf.PI * 0.5f;
                        int x = 32 + Mathf.RoundToInt(Mathf.Cos(angle) * 9f);
                        int y = 34 + Mathf.RoundToInt(Mathf.Sin(angle) * 9f);
                        Line(pixels, x, y, x + Mathf.RoundToInt(Mathf.Cos(angle) * 8f - Mathf.Sin(angle) * 7f), y + Mathf.RoundToInt(Mathf.Sin(angle) * 8f + Mathf.Cos(angle) * 7f), 2, red);
                        Line(pixels, x, y, x + Mathf.RoundToInt(Mathf.Cos(angle) * 8f + Mathf.Sin(angle) * 7f), y + Mathf.RoundToInt(Mathf.Sin(angle) * 8f - Mathf.Cos(angle) * 7f), 2, red);
                    }
                    break;
                case EmoteKind.Star:
                    for (int i = 0; i < 5; i++)
                    {
                        float a = Mathf.PI * 0.5f + i * Mathf.PI * 0.4f;
                        float b = a + Mathf.PI * 0.8f;
                        Line(pixels, 32 + Mathf.RoundToInt(Mathf.Cos(a) * 18f), 34 + Mathf.RoundToInt(Mathf.Sin(a) * 18f), 32 + Mathf.RoundToInt(Mathf.Cos(b) * 18f), 34 + Mathf.RoundToInt(Mathf.Sin(b) * 18f), 2, gold);
                    }
                    break;
                case EmoteKind.Question:
                    Line(pixels, 23, 44, 29, 49, 2, outline);
                    Line(pixels, 29, 49, 40, 45, 2, outline);
                    Line(pixels, 40, 45, 39, 37, 2, outline);
                    Line(pixels, 39, 37, 32, 32, 2, outline);
                    Line(pixels, 32, 32, 32, 28, 2, outline);
                    Circle(pixels, 32, 20, 3, outline);
                    break;
                case EmoteKind.Exclamation:
                    Line(pixels, 32, 49, 32, 29, 3, red);
                    Circle(pixels, 32, 20, 3, red);
                    break;
                case EmoteKind.Sleep:
                    DrawZ(pixels, 19, 23, 12, outline);
                    DrawZ(pixels, 34, 36, 15, blue);
                    break;
                case EmoteKind.Chili:
                    Line(pixels, 23, 45, 37, 37, 6, red);
                    Line(pixels, 37, 37, 39, 27, 4, red);
                    Line(pixels, 39, 27, 32, 19, 2, red);
                    Line(pixels, 23, 47, 26, 52, 2, new Color32(90, 137, 78, 255));
                    break;
                case EmoteKind.Coin:
                    Circle(pixels, 32, 34, 17, gold);
                    Circle(pixels, 32, 34, 13, paper);
                    Circle(pixels, 32, 34, 11, gold);
                    Line(pixels, 32, 42, 32, 26, 2, outline);
                    Line(pixels, 27, 38, 37, 38, 1, outline);
                    Line(pixels, 27, 30, 37, 30, 1, outline);
                    break;
                case EmoteKind.Steam:
                    for (int i = 0; i < 3; i++)
                    {
                        int x = 21 + i * 11;
                        Line(pixels, x, 23, x - 3, 32, 2, blue);
                        Line(pixels, x - 3, 32, x + 3, 41, 2, blue);
                        Line(pixels, x + 3, 41, x, 49, 2, blue);
                    }
                    break;
            }
            Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            texture.name = "CharacterIcon_" + kind;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>眠気アイコンの Z 字を描画する。</summary>
        private static void DrawZ(Color32[] pixels, int x, int y, int size, Color32 color)
        {
            Line(pixels, x, y + size, x + size, y + size, 2, color);
            Line(pixels, x + size, y + size, x, y, 2, color);
            Line(pixels, x, y, x + size, y, 2, color);
        }

        /// <summary>線分を円の連続として描画する。</summary>
        private static void Line(Color32[] pixels, int x0, int y0, int x1, int y1, int radius, Color32 color)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (int step = 0; step <= steps; step++)
            {
                float ratio = steps == 0 ? 0f : (float)step / steps;
                Circle(pixels, Mathf.RoundToInt(Mathf.Lerp(x0, x1, ratio)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, ratio)), radius, color);
            }
        }

        /// <summary>テクスチャ境界内に塗りつぶした円を描画する。</summary>
        private static void Circle(Color32[] pixels, int cx, int cy, int radius, Color32 color)
        {
            for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(IconSize - 1, cy + radius); y++)
                for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(IconSize - 1, cx + radius); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                        pixels[y * IconSize + x] = color;
        }

        /// <summary>関節の回転を指数補間する。</summary>
        private static void RotateJoint(Transform joint, Vector3 euler, float blend)
        {
            joint.localRotation = Quaternion.Slerp(joint.localRotation, Quaternion.Euler(euler), blend);
        }

        /// <summary>まぶたの閉じ具合を基準サイズに適用する。</summary>
        private static void ScaleLid(Transform lid, Vector3 restScale, float closure)
        {
            restScale.y *= closure;
            lid.localScale = restScale;
        }

        /// <summary>外部の持ち物を戻し、インスタンス固有メッシュを解放する。</summary>
        private void OnDestroy()
        {
            RestoreCarry();
            if (visual != null) CharacterFactory.ReleaseVisual(visual);
        }
    }
}
