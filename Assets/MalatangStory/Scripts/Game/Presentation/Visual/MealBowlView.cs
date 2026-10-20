using System.Collections.Generic;
using UnityEngine;

/// <summary>食事の進行を、生成済みの器の部品へ反映します。</summary>
namespace MalatangStory.Game
{
    /// <summary>具材・液面・一口の道具を事前に保持し、更新時に割り当てを行いません。</summary>
    public sealed class MealBowlView : MonoBehaviour
    {
        private Transform[] pieces;
        private Vector3[] initialPositions, settledPositions;
        private bool[] left;
        private int[] eatenRank;
        private int edibleCount;
        private Transform surface, garnishes, chopsticks;
        private GameObject bite;
        private Vector3 soupOrigin, soupScale, garnishScale;
        private float finalSoup;
        private float depth;
        public int VisibleIngredientCount { get; private set; }
        public float SoupRemaining { get; private set; } = 1f;

        /// <summary>具材位置と残食判定を生成時にキャッシュし、箸と一口を一度だけ作ります。</summary>
        public void Initialize(List<Transform> ingredients, MealLeftoverResult result, Transform soup, Transform garnish, float bowlDepth)
        {
            pieces = ingredients.ToArray(); left = new bool[pieces.Length]; eatenRank = new int[pieces.Length];
            initialPositions = new Vector3[pieces.Length]; settledPositions = new Vector3[pieces.Length];
            surface = soup; garnishes = garnish; depth = bowlDepth; finalSoup = result.soupRemaining;
            garnishScale = garnishes.localScale; soupScale = surface.localScale; soupOrigin = surface.localPosition;
            for (int i = 0; i < pieces.Length; i++)
            {
                for (int k = 0; k < result.ingredients.Count; k++) if (result.ingredients[k].index == i) { left[i] = true; break; }
                eatenRank[i] = left[i] ? -1 : edibleCount++;
                PrepareDiningPose(pieces[i], soupOrigin.y);
                initialPositions[i] = pieces[i].localPosition;
                settledPositions[i] = SettledPosition(pieces[i]);
            }
            chopsticks = new GameObject("Meal Chopsticks").transform;
            chopsticks.SetParent(transform, false);
            for (int i = 0; i < 2; i++)
                StoreBuilder.Part("Chopstick", MeshFactory.Cylinder(.5f, 1f), MaterialLibrary.Lit(new Color(.46f, .22f, .08f)),
                    new Vector3(i * .025f, 0f, 0f), new Vector3(.009f, .38f, .009f), chopsticks, null, false);
            chopsticks.localRotation = Quaternion.Euler(65f, 0f, -12f);
            bite = StoreBuilder.Part("Mouthful", MeshFactory.Sphere(), MaterialLibrary.Lit(new Color(.73f, .43f, .19f)),
                new Vector3(.013f, -.19f, 0f), Vector3.one * .055f, chopsticks, null, false);
            chopsticks.gameObject.SetActive(false);
            ApplyProgress(0f, 0f, false);
        }

        /// <summary>食べる順序で具材を隠し、液面と薬味を下げ、約3.5秒ごとに一口を上げます。</summary>
        public void ApplyProgress(float progress, float seconds, bool eating)
        {
            if (pieces == null) return;
            progress = Mathf.Clamp01(progress);
            int eaten = Mathf.FloorToInt(progress * edibleCount);
            int visible = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                bool active = left[i] || eatenRank[i] >= eaten;
                if (pieces[i].gameObject.activeSelf != active) pieces[i].gameObject.SetActive(active);
                if (active) visible++;
                pieces[i].localPosition = Vector3.Lerp(initialPositions[i], settledPositions[i], progress);
            }
            VisibleIngredientCount = visible;
            SoupRemaining = Mathf.Lerp(1f, finalSoup, progress);
            bool hasSoup = SoupRemaining > .001f;
            SetVisible(surface.gameObject, hasSoup); SetVisible(garnishes.gameObject, hasSoup);
            float lowered = depth * .48f * (1f - SoupRemaining);
            surface.localPosition = soupOrigin - Vector3.up * lowered;
            garnishes.localPosition = surface.localPosition - Vector3.up * .003f;
            float radius = Mathf.Lerp(.58f, 1f, SoupRemaining);
            surface.localScale = new Vector3(soupScale.x * radius, soupScale.y, soupScale.z * radius);
            garnishes.localScale = new Vector3(garnishScale.x * radius, garnishScale.y, garnishScale.z * radius);
            float phase = Mathf.Repeat(seconds, 3.5f);
            float lift = phase < 1.3f ? Mathf.Sin(phase / 1.3f * Mathf.PI) : 0f;
            bool lifting = eating && progress < 1f && phase < 1.3f;
            SetVisible(chopsticks.gameObject, lifting);
            SetVisible(bite, lifting && visible > LeftCount());
            chopsticks.localPosition = new Vector3(.1f, depth * .82f + lift * 1.05f, -.08f - lift * .14f);
        }

        /// <summary>細長い束を寝かせ、器の内底にも収まる大きさと液面に接する初期位置へ整えます。</summary>
        private static void PrepareDiningPose(Transform piece, float soupHeight)
        {
            if (!piece.TryGetComponent(out BoxCollider box)) return;
            Vector3 size = Vector3.Scale(box.size, piece.localScale);
            if (size.y > Mathf.Max(size.x, size.z) * 1.5f)
                piece.localRotation = Quaternion.Euler(82f, piece.localEulerAngles.y, 12f);
            Bounds bounds = DiningBounds(piece, box);
            float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            float fit = Mathf.Min(1f, .19f / Mathf.Max(.001f, radius), .12f / Mathf.Max(.001f, bounds.size.y));
            piece.localScale *= fit;
            bounds = DiningBounds(piece, box);
            Vector3 position = piece.localPosition;
            Vector2 center = Vector2.ClampMagnitude(new Vector2(position.x + bounds.center.x, position.z + bounds.center.z), .38f - radius * fit);
            position.x = center.x - bounds.center.x; position.z = center.y - bounds.center.z;
            position.y = soupHeight - .025f - bounds.min.y;
            piece.localPosition = position;
        }

        /// <summary>回転後の具材の箱を器のローカル座標で求めます。</summary>
        private static Bounds DiningBounds(Transform piece, BoxCollider box)
        {
            Vector3 half = Vector3.Scale(box.size * .5f, piece.localScale);
            Quaternion rotation = piece.localRotation;
            Vector3 x = rotation * new Vector3(half.x, 0f, 0f);
            Vector3 y = rotation * new Vector3(0f, half.y, 0f);
            Vector3 z = rotation * new Vector3(0f, 0f, half.z);
            Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(rotation * Vector3.Scale(box.center, piece.localScale), extents * 2f);
        }

        /// <summary>残す具材の底面を内底へ接触させ、空中に留まる補間を避けます。</summary>
        private static Vector3 SettledPosition(Transform piece)
        {
            Vector3 position = piece.localPosition;
            if (piece.TryGetComponent(out BoxCollider box))
            {
                Bounds bounds = DiningBounds(piece, box);
                position = new Vector3(-bounds.center.x, .075f - bounds.min.y, -bounds.center.z);
            }
            return position;
        }

        /// <summary>食べ残し以外の具材が残っているかの判定に用いる個数です。</summary>
        private int LeftCount() => pieces.Length - edibleCount;

        /// <summary>可視状態が変わった時だけ切り替えます。</summary>
        private static void SetVisible(GameObject part, bool visible)
        {
            if (part.activeSelf != visible) part.SetActive(visible);
        }
    }
}
