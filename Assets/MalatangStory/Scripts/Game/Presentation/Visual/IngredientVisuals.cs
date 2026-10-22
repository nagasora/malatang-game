using System;
using System.Collections.Generic;
using MalatangStory.Ingredients;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>通常・希少食材の共有メッシュと模様付きマテリアルから、生・茹で上がりを表現できるモデルを生成します。</summary>
    public static class IngredientVisuals
    {
        /// <summary>食材の識別に必要な形状の種類です。</summary>
        private enum Shape
        {
            MushroomBundle, MushroomFan, Frill, Noodles, Ribbon, Block, Feet,
            Ball, CrabStick, HollowStick, RiceStick, Dumpling, FriedBread, Egg,
            Meat, Offal, Tripe, Shell, Squid, Shrimp, Tofu, Leafy, Okra,
            RootStick, Potato, Broccoli, Tomato, Sprout, Corn, Lotus, Seaweed
        }

        /// <summary>表面の色と加熱特性を分離する描画グループです。</summary>
        private enum Surface { Body, Accent, Detail }

        /// <summary>IDごとの形状、色、実寸と形状差分を保持します。</summary>
        private sealed class VisualDefinition
        {
            internal readonly Shape Shape;
            internal readonly Color Color;
            internal readonly Color Accent;
            internal readonly Vector3 Size;
            internal readonly int Count;
            internal readonly float Variation;
            internal readonly string Category;

            /// <summary>カタログに依存しない専用の描画仕様を設定します。</summary>
            internal VisualDefinition(Shape shape, uint color, uint accent, Vector3 size,
                int count, float variation, string category)
            {
                Shape = shape;
                Color = FromRgb(color);
                Accent = FromRgb(accent);
                Size = size;
                Count = count;
                Variation = variation;
                Category = category;
            }
        }

        /// <summary>インスタンス間で共有するメッシュと表面状態を保持します。</summary>
        private sealed class VisualTemplate
        {
            internal readonly Mesh[] Meshes;
            internal readonly Color[] Raw;
            internal readonly Color[] Cooked;
            internal readonly Color[] Overcooked;
            internal readonly float[] DrySmoothness;
            internal readonly float[] CookedSmoothness;
            internal readonly Bounds Bounds;
            internal readonly bool Transparent;

            /// <summary>結合済みのモデルと原色を保存します。</summary>
            internal VisualTemplate(Mesh[] meshes, Color[] raw, Color[] cooked,
                Color[] overcooked, float[] drySmoothness, float[] cookedSmoothness,
                Bounds bounds, bool transparent)
            {
                Meshes = meshes;
                Raw = raw;
                Cooked = cooked;
                Overcooked = overcooked;
                DrySmoothness = drySmoothness;
                CookedSmoothness = cookedSmoothness;
                Bounds = bounds;
                Transparent = transparent;
            }
        }

        /// <summary>同色の部品を結合し、中間生成メッシュの所有権を管理します。</summary>
        private sealed class ModelParts : IDisposable
        {
            internal readonly List<(Mesh mesh, Matrix4x4 transform)>[] Groups =
            {
                new List<(Mesh mesh, Matrix4x4 transform)>(),
                new List<(Mesh mesh, Matrix4x4 transform)>(),
                new List<(Mesh mesh, Matrix4x4 transform)>()
            };
            private readonly List<Mesh> ownedMeshes = new List<Mesh>();

            /// <summary>共有メッシュを変換行列とともに登録します。</summary>
            internal void Add(Surface surface, Mesh mesh, Vector3 position,
                Vector3 size, Vector3 rotation = default(Vector3))
            {
                Groups[(int)surface].Add((mesh,
                    Matrix4x4.TRS(position, Quaternion.Euler(rotation), size)));
            }

            /// <summary>結合後に破棄できる中間メッシュを登録します。</summary>
            internal Mesh Own(Mesh mesh)
            {
                ownedMeshes.Add(mesh);
                return mesh;
            }

            /// <summary>結合に使用した中間メッシュを解放します。</summary>
            public void Dispose()
            {
                foreach (Mesh mesh in ownedMeshes) OwnedObjects.Destroy(mesh);
                ownedMeshes.Clear();
            }
        }

        private static readonly Dictionary<string, VisualDefinition> Definitions = CreateDefinitions();
        private static readonly Dictionary<string, VisualDefinition> Fallbacks = CreateFallbacks();
        private static readonly Dictionary<string, VisualTemplate> Templates =
            new Dictionary<string, VisualTemplate>(StringComparer.Ordinal);
        private static Mesh sphere;
        private static Mesh box;
        private static Mesh cylinder;
        private static Material opaqueMaterial;
        private static Material transparentMaterial;

        /// <summary>食材モデルを生成します。scale=1の最大寸法は0.1〜0.25 Unity単位です。</summary>
        /// <param name="item">IDとカテゴリを持つ食材定義です。</param>
        /// <param name="parent">生成先の親です。nullの場合はシーンのルートに生成します。</param>
        /// <param name="scale">正の有限値で指定する相対倍率です。</param>
        /// <param name="physics">軽量の動的剛体を追加するかどうかです。</param>
        /// <returns>IngredientViewと単純なコライダーを備えた食材のルートオブジェクトです。</returns>
        /// <exception cref="ArgumentNullException">食材定義がnullの場合です。</exception>
        /// <exception cref="ArgumentException">IDが空の場合です。</exception>
        /// <exception cref="ArgumentOutOfRangeException">倍率または物理用の重量が不正な場合です。</exception>
        public static GameObject Build(IngredientDefinition item, Transform parent,
            float scale = 1f, bool physics = false)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.id))
                throw new ArgumentException("食材IDを指定してください。", nameof(item));
            if (!float.IsFinite(scale) || scale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(scale), "倍率には正の有限値が必要です。");
            if (physics && (!float.IsFinite(item.portionGrams) || item.portionGrams <= 0f))
                throw new ArgumentOutOfRangeException(nameof(item), "物理モデルには正の有限重量が必要です。");

            if (FoodRegistry.TryGet(item.id, out FoodModel food))
                return BuildFood(item, parent, scale, physics, food);

            VisualDefinition definition;
            string key = item.id;
            if (!Definitions.TryGetValue(key, out definition))
            {
                string category = item.category ?? string.Empty;
                if (!Fallbacks.TryGetValue(category, out definition)) definition = Fallbacks["other"];
                key = "fallback:" + definition.Category;
            }
            VisualTemplate template;
            if (!Templates.TryGetValue(key, out template) || !IsTemplateAlive(template))
            {
                VisualTemplate replacement = CreateTemplate(key, definition);
                if (template != null)
                    foreach (Mesh mesh in template.Meshes) OwnedObjects.Destroy(mesh);
                template = replacement;
                Templates[key] = template;
            }

            Material material = GetSharedMaterial(template.Transparent);
            GameObject root = new GameObject("Ingredient_" + item.id);
            try
            {
                root.transform.SetParent(parent, false);
                root.transform.localScale = Vector3.one * scale;
                Renderer[] renderers = new Renderer[template.Meshes.Length];
                for (int index = 0; index < template.Meshes.Length; index++)
                {
                    GameObject surface = new GameObject("Surface_" + index);
                    surface.transform.SetParent(root.transform, false);
                    surface.AddComponent<MeshFilter>().sharedMesh = template.Meshes[index];
                    MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderers[index] = renderer;
                }
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = template.Bounds.center;
                Vector3 boundsSize = template.Bounds.size;
                collider.size = new Vector3(Mathf.Max(0.008f, boundsSize.x),
                    Mathf.Max(0.008f, boundsSize.y), Mathf.Max(0.008f, boundsSize.z));
                if (physics)
                {
                    Rigidbody body = root.AddComponent<Rigidbody>();
                    body.mass = Mathf.Clamp(item.portionGrams * 0.001f, 0.01f, 0.15f);
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    body.linearDamping = 0.35f;
                    body.angularDamping = 0.6f;
                }
                IngredientView view = root.AddComponent<IngredientView>();
                view.Initialize(renderers, template.Raw, template.Cooked, template.Overcooked,
                    template.DrySmoothness, template.CookedSmoothness);
                return root;
            }
            catch
            {
                OwnedObjects.Destroy(root);
                throw;
            }
        }

        /// <summary>共有のテクスチャ付き食材を、従来と同じビューと軽量物理で生成します。</summary>
        private static GameObject BuildFood(IngredientDefinition item, Transform parent, float scale,
            bool physics, FoodModel model)
        {
            GameObject root = new GameObject("Ingredient_" + item.id);
            try
            {
                root.transform.SetParent(parent, false);
                root.transform.localScale = Vector3.one * scale;
                Renderer[] renderers = new Renderer[model.Meshes.Length];
                for (int i = 0; i < model.Meshes.Length; i++)
                {
                    GameObject surface = new GameObject("Surface_" + i);
                    surface.transform.SetParent(root.transform, false);
                    surface.AddComponent<MeshFilter>().sharedMesh = model.Meshes[i];
                    MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = model.Materials[i];
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderers[i] = renderer;
                }
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = model.Bounds.center;
                Vector3 size = model.Bounds.size;
                collider.size = new Vector3(Mathf.Max(.008f, size.x), Mathf.Max(.008f, size.y), Mathf.Max(.008f, size.z));
                if (physics)
                {
                    Rigidbody body = root.AddComponent<Rigidbody>();
                    body.mass = Mathf.Clamp(item.portionGrams * .001f, .01f, .15f);
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    body.linearDamping = .35f; body.angularDamping = .6f;
                }
                root.AddComponent<IngredientView>().InitializeTextured(renderers, model.Surfaces);
                return root;
            }
            catch { OwnedObjects.Destroy(root); throw; }
        }

        /// <summary>69個のカタログIDに対応する専用の形状仕様を定義します。</summary>
        private static Dictionary<string, VisualDefinition> CreateDefinitions()
        {
            return new Dictionary<string, VisualDefinition>(StringComparer.Ordinal)
            {
                { "enoki", D(Shape.MushroomBundle, 0xF4EDDA, 0xF9F5E9, .085f, .22f, .075f, 17, .12f, "mushroom") },
                { "king_oyster", D(Shape.MushroomBundle, 0xE8DDC6, 0xA78A64, .11f, .19f, .10f, 2, .65f, "mushroom") },
                { "shimeji", D(Shape.MushroomBundle, 0xECE1CC, 0x826044, .12f, .15f, .10f, 8, .42f, "mushroom") },
                { "maitake", D(Shape.MushroomFan, 0xA28C74, 0xD1BCA0, .18f, .10f, .16f, 14, .30f, "mushroom") },
                { "black_wood_ear", D(Shape.Frill, 0x302320, 0x654A3B, .16f, .07f, .13f, 5, .85f, "mushroom") },
                { "brown_enoki", D(Shape.MushroomBundle, 0xD6BF92, 0xA27B42, .095f, .20f, .08f, 13, .17f, "mushroom") },
                { "white_wood_ear", D(Shape.Frill, 0xF4EBD1, 0xDDD2AC, .17f, .09f, .14f, 10, .65f, "mushroom") },
                { "sweet_potato_noodle", D(Shape.Noodles, 0xECE1D2, 0xD3C2A4, .20f, .045f, .16f, 9, .0048f, "noodle") },
                { "shirataki", D(Shape.Noodles, 0xFAF9ED, 0xDFE4D5, .17f, .05f, .13f, 18, .0025f, "noodle") },
                { "corn_noodle", D(Shape.Noodles, 0xF5D34F, 0xDFC056, .19f, .045f, .15f, 13, .0038f, "noodle") },
                { "mung_bean_noodle", D(Shape.Noodles, 0xF4F1E4, 0xDDE3D5, .21f, .04f, .16f, 22, .0018f, "noodle") },
                { "chinese_noodle", D(Shape.Noodles, 0xF2E2A0, 0xD7BE6D, .20f, .055f, .16f, 14, .004f, "noodle") },
                { "knife_cut_noodle", D(Shape.Ribbon, 0xF6EACF, 0xE6D7B7, .22f, .045f, .15f, 6, .020f, "noodle") },
                { "wide_glass_noodle", D(Shape.Ribbon, 0xEEE8D9, 0xD9D3C3, .23f, .035f, .13f, 4, .032f, "noodle") },
                { "rice_noodle", D(Shape.Noodles, 0xFFF9EB, 0xE9E2D1, .19f, .05f, .16f, 12, .0045f, "noodle") },
                { "duck_blood", D(Shape.Block, 0x5C2026, 0x853C40, .13f, .08f, .09f, 1, .008f, "other") },
                { "chicken_feet", D(Shape.Feet, 0xEFD6B6, 0xC69B79, .14f, .045f, .20f, 4, .15f, "other") },
                { "shrimp_ball", D(Shape.Ball, 0xF2D6CA, 0xE98E7C, .115f, .10f, .10f, 11, .10f, "processed") },
                { "crab_stick", D(Shape.CrabStick, 0xF4EEE0, 0xE9604D, .055f, .05f, .18f, 7, .12f, "processed") },
                { "crab_ball", D(Shape.Ball, 0xEDD4BA, 0xDB7857, .12f, .10f, .105f, 8, .14f, "processed") },
                { "chikuwa", D(Shape.HollowStick, 0xF3E4CA, 0xA86632, .08f, .08f, .18f, 24, .23f, "processed") },
                { "fish_ball", D(Shape.Ball, 0xC6C1B2, 0x888879, .11f, .095f, .10f, 15, .16f, "processed") },
                { "tteokbokki", D(Shape.RiceStick, 0xFFF8E8, 0xE8DECB, .04f, .04f, .15f, 3, .08f, "processed") },
                { "hanpen", D(Shape.Block, 0xFFF9EE, 0xEDE8DA, .14f, .055f, .12f, 1, .018f, "processed") },
                { "wonton", D(Shape.Dumpling, 0xEEE1BC, 0xC9BA91, .15f, .09f, .12f, 4, .85f, "processed") },
                { "dumpling", D(Shape.Dumpling, 0xF4E9D3, 0xD8C9AB, .16f, .08f, .095f, 11, .20f, "processed") },
                { "meat_ball", D(Shape.Ball, 0xB7756D, 0xEEE0BD, .12f, .105f, .11f, 18, .19f, "processed") },
                { "youtiao", D(Shape.FriedBread, 0xD7993D, 0xA96D26, .09f, .065f, .22f, 2, .25f, "processed") },
                { "quail_egg", D(Shape.Egg, 0xEADDBB, 0x69513E, .075f, .11f, .075f, 26, .36f, "meat") },
                { "lamb", D(Shape.Meat, 0xCD7476, 0xF5E5CC, .18f, .027f, .12f, 5, .65f, "meat") },
                { "beef_offal", D(Shape.Offal, 0xD6B9A4, 0xF1DCC1, .14f, .065f, .13f, 3, .45f, "meat") },
                { "beef_slice", D(Shape.Meat, 0xB94F59, 0xF5DCCB, .20f, .025f, .14f, 8, .28f, "meat") },
                { "beef_tripe", D(Shape.Tripe, 0xC3B69B, 0xEEE0BE, .17f, .035f, .14f, 5, .20f, "meat") },
                { "chicken_breast", D(Shape.Meat, 0xE8B3A6, 0xFAE6CB, .15f, .055f, .11f, 7, .08f, "meat") },
                { "chicken_thigh", D(Shape.Meat, 0xCE8D84, 0xF1D9B4, .14f, .065f, .12f, 4, .48f, "meat") },
                { "pork_belly", D(Shape.Meat, 0xE29D9A, 0xFFF0D6, .20f, .035f, .105f, 5, .90f, "meat") },
                { "egg", D(Shape.Egg, 0xF6E9CD, 0xCEAE7D, .09f, .14f, .09f, 7, .06f, "meat") },
                { "clam", D(Shape.Shell, 0x6D7772, 0xDBCEB0, .13f, .075f, .11f, 9, .20f, "seafood") },
                { "squid", D(Shape.Squid, 0xB9C3C1, 0xE5DBCF, .13f, .045f, .15f, 3, .26f, "seafood") },
                { "shrimp", D(Shape.Shrimp, 0xA3B3B1, 0x667B7C, .17f, .055f, .13f, 8, .65f, "seafood") },
                { "scallop", D(Shape.Shell, 0xB7A195, 0xF4E4CF, .17f, .08f, .15f, 14, .80f, "seafood") },
                { "dried_tofu_strips", D(Shape.Ribbon, 0xE4CD8D, 0xC2A36C, .18f, .035f, .13f, 12, .006f, "soy") },
                { "silken_tofu", D(Shape.Tofu, 0xFFF6E1, 0xF1E6D1, .115f, .08f, .10f, 0, .006f, "soy") },
                { "atsuage", D(Shape.Tofu, 0xC99947, 0xFFF0CD, .14f, .085f, .10f, 15, .014f, "soy") },
                { "yuba", D(Shape.Ribbon, 0xF3DBA0, 0xDBC185, .17f, .045f, .13f, 4, .045f, "soy") },
                { "soy_sheet", D(Shape.Ribbon, 0xE7D09A, 0xCDB480, .19f, .03f, .145f, 2, .075f, "soy") },
                { "fuzhu", D(Shape.Ribbon, 0xDEBE76, 0xBFA05F, .20f, .06f, .08f, 5, .015f, "soy") },
                { "firm_tofu", D(Shape.Tofu, 0xF5EBD0, 0xD9CFB7, .125f, .085f, .11f, 22, .003f, "soy") },
                { "aburaage", D(Shape.Tofu, 0xD8AE55, 0xF3DBAA, .17f, .025f, .12f, 18, .009f, "soy") },
                { "okra", D(Shape.Okra, 0x558A38, 0xF0E6B7, .07f, .075f, .12f, 5, .48f, "vegetable") },
                { "burdock", D(Shape.RootStick, 0xBBA17A, 0xE1D1B0, .035f, .035f, .20f, 3, .12f, "vegetable") },
                { "potato", D(Shape.Potato, 0xE7D397, 0xB19A69, .13f, .07f, .11f, 7, .25f, "vegetable") },
                { "bok_choy", D(Shape.Leafy, 0x398545, 0xDCEBC0, .13f, .065f, .22f, 4, .70f, "vegetable") },
                { "cilantro", D(Shape.Leafy, 0x3F973D, 0x8EBF64, .14f, .045f, .17f, 7, .18f, "vegetable") },
                { "broccoli", D(Shape.Broccoli, 0x3E7135, 0x91AC5E, .14f, .14f, .13f, 13, .18f, "vegetable") },
                { "spinach", D(Shape.Leafy, 0x35683D, 0x9BAC66, .11f, .05f, .23f, 5, .42f, "vegetable") },
                { "cherry_tomato", D(Shape.Tomato, 0xDC4431, 0x498343, .10f, .095f, .10f, 5, .14f, "vegetable") },
                { "bean_sprout", D(Shape.Sprout, 0xF7F0D9, 0xE2D28C, .14f, .035f, .20f, 13, .20f, "vegetable") },
                { "baby_corn", D(Shape.Corn, 0xEDD99A, 0xFFF0B9, .045f, .045f, .19f, 10, .16f, "vegetable") },
                { "lettuce", D(Shape.Leafy, 0x94BE61, 0xDDE5AC, .18f, .07f, .17f, 5, .95f, "vegetable") },
                { "lotus_root", D(Shape.Lotus, 0xF0DEB5, 0xCEB78C, .17f, .035f, .17f, 8, .24f, "vegetable") },
                { "wakame", D(Shape.Seaweed, 0x2F5040, 0x4B6B42, .17f, .04f, .13f, 7, .75f, "vegetable") },
                { "kombu", D(Shape.Seaweed, 0x454E2E, 0x6A7444, .14f, .025f, .21f, 2, .18f, "vegetable") },
                { "stem_lettuce", D(Shape.RootStick, 0x8FA869, 0xB5C88A, .025f, .025f, .19f, 6, .40f, "vegetable") },
                { "crown_daisy", D(Shape.Leafy, 0x407C38, 0x97B95A, .14f, .05f, .20f, 6, .26f, "vegetable") },
                { "komatsuna", D(Shape.Leafy, 0x3A7C3B, 0xB3CF7C, .12f, .055f, .21f, 4, .56f, "vegetable") },
                { "mizuna", D(Shape.Leafy, 0x65A14B, 0xD1DCAD, .15f, .04f, .23f, 9, .10f, "vegetable") },
                { "pea_shoot", D(Shape.Leafy, 0x73A94E, 0xB6CE7B, .13f, .05f, .19f, 8, .12f, "vegetable") },
                { "napa_cabbage", D(Shape.Leafy, 0xB4CC72, 0xFFF0CF, .17f, .075f, .23f, 3, .88f, "vegetable") }
            };
        }

        /// <summary>未知のIDをカテゴリごとの有限個の共有モデルに対応させます。</summary>
        private static Dictionary<string, VisualDefinition> CreateFallbacks()
        {
            return new Dictionary<string, VisualDefinition>(StringComparer.Ordinal)
            {
                { "mushroom", D(Shape.MushroomBundle, 0xE8DBBF, 0x987751, .13f, .17f, .11f, 5, .4f, "mushroom") },
                { "noodle", D(Shape.Noodles, 0xF5EDD7, 0xD9CCAD, .18f, .04f, .14f, 12, .003f, "noodle") },
                { "processed", D(Shape.Ball, 0xE3C8A8, 0xBD9978, .11f, .10f, .11f, 9, .1f, "processed") },
                { "meat", D(Shape.Meat, 0xD58D8C, 0xF0D9BD, .15f, .035f, .11f, 5, .3f, "meat") },
                { "seafood", D(Shape.Squid, 0xAEBAB8, 0xE4DAC9, .13f, .04f, .13f, 2, .2f, "seafood") },
                { "soy", D(Shape.Tofu, 0xF3E4C3, 0xD8CAA9, .12f, .075f, .10f, 10, .005f, "soy") },
                { "vegetable", D(Shape.Leafy, 0x60934D, 0xBED296, .13f, .05f, .18f, 4, .5f, "vegetable") },
                { "other", D(Shape.Block, 0xC7AB86, 0x9E8165, .12f, .065f, .095f, 1, .01f, "other") }
            };
        }

        /// <summary>専用表の寸法と色を型付きの定義にまとめます。</summary>
        private static VisualDefinition D(Shape shape, uint color, uint accent,
            float x, float y, float z, int count, float variation, string category)
        {
            return new VisualDefinition(shape, color, accent, new Vector3(x, y, z), count, variation, category);
        }

        /// <summary>キャッシュされたモデルの全描画メッシュが存在するか確認します。</summary>
        private static bool IsTemplateAlive(VisualTemplate template)
        {
            foreach (Mesh mesh in template.Meshes)
                if (mesh == null) return false;
            return true;
        }

        /// <summary>RGB値を不透明色に変換します。</summary>
        private static Color FromRgb(uint value)
        {
            return new Color(((value >> 16) & 255) / 255f,
                ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1f);
        }

        /// <summary>カテゴリの共有マテリアルを必要なときだけ取得します。</summary>
        private static Material GetSharedMaterial(bool transparent)
        {
            if (transparent)
            {
                if (transparentMaterial == null) transparentMaterial = MaterialLibrary.Transparent(Color.white);
                if (transparentMaterial == null) throw new InvalidOperationException("半透明マテリアルを取得できません。");
                return transparentMaterial;
            }
            if (opaqueMaterial == null) opaqueMaterial = MaterialLibrary.Lit(Color.white);
            if (opaqueMaterial == null) throw new InvalidOperationException("食材マテリアルを取得できません。");
            return opaqueMaterial;
        }

        /// <summary>単位寸法の基本メッシュを共有します。</summary>
        private static void EnsurePrimitives()
        {
            if (sphere == null) sphere = MeshFactory.Sphere();
            if (box == null) box = MeshFactory.RoundedBox(Vector3.one, .06f);
            if (cylinder == null) cylinder = MeshFactory.Cylinder(.5f, 1f, 16, .02f);
        }

        /// <summary>ID専用の形状を同色単位で結合し、規定の実寸に正規化します。</summary>
        private static VisualTemplate CreateTemplate(string key, VisualDefinition definition)
        {
            EnsurePrimitives();
            using (ModelParts parts = new ModelParts())
            {
                Populate(parts, definition, StableSeed(key));
                List<Mesh> meshes = new List<Mesh>();
                List<Color> raw = new List<Color>();
                List<Color> cooked = new List<Color>();
                List<Color> overcooked = new List<Color>();
                List<float> dry = new List<float>();
                List<float> cookedGloss = new List<float>();
                bool transparent = definition.Category == "noodle";
                bool hasBounds = false;
                Bounds bounds = new Bounds();
                try
                {
                    for (int group = 0; group < parts.Groups.Length; group++)
                    {
                        if (parts.Groups[group].Count == 0) continue;
                        // 共有キャッシュの結合結果を直接正規化すると、他の利用者の形状まで変わるため複製します。
                        Mesh mesh = UnityEngine.Object.Instantiate(MeshFactory.Combine(parts.Groups[group].ToArray()));
                        mesh.name = "IngredientMesh_" + key + "_" + group;
                        meshes.Add(mesh);
                        if (!hasBounds) { bounds = mesh.bounds; hasBounds = true; }
                        else bounds.Encapsulate(mesh.bounds);
                        Color baseColor = group == (int)Surface.Body ? definition.Color : definition.Accent;
                        if (group == (int)Surface.Detail) baseColor = Color.Lerp(baseColor, Color.white, .3f);
                        Color cookedColor = CookingColor(definition, (Surface)group, baseColor);
                        raw.Add(baseColor);
                        cooked.Add(cookedColor);
                        Color overColor = Color.Lerp(cookedColor, new Color(.27f, .23f, .14f, cookedColor.a), .48f);
                        if (transparent) overColor.a = Mathf.Min(1f, cookedColor.a + .15f);
                        overcooked.Add(overColor);
                        dry.Add(definition.Shape == Shape.Egg || definition.Shape == Shape.Tomato ? .38f : .22f);
                        cookedGloss.Add(transparent ? .80f : .38f);
                    }
                    if (!hasBounds) throw new InvalidOperationException("食材メッシュに部品がありません。");
                    float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                    if (!float.IsFinite(largest) || largest <= 0f)
                        throw new InvalidOperationException("食材メッシュの寸法が不正です。");
                    float nominal = Mathf.Clamp(Mathf.Max(definition.Size.x,
                        Mathf.Max(definition.Size.y, definition.Size.z)), .1f, .25f);
                    float factor = nominal / largest;
                    Vector3 center = bounds.center;
                    foreach (Mesh mesh in meshes)
                    {
                        Vector3[] vertices = mesh.vertices;
                        for (int index = 0; index < vertices.Length; index++)
                            vertices[index] = (vertices[index] - center) * factor;
                        mesh.vertices = vertices;
                        mesh.RecalculateBounds();
                    }
                    bounds = new Bounds(Vector3.zero, bounds.size * factor);
                    return new VisualTemplate(meshes.ToArray(), raw.ToArray(), cooked.ToArray(),
                        overcooked.ToArray(), dry.ToArray(), cookedGloss.ToArray(), bounds, transparent);
                }
                catch
                {
                    foreach (Mesh mesh in meshes) OwnedObjects.Destroy(mesh);
                    throw;
                }
            }
        }

        /// <summary>形状に応じて脂肪、殻、葉、麺の加熱色を区別します。</summary>
        private static Color CookingColor(VisualDefinition definition, Surface surface, Color raw)
        {
            if (definition.Category == "noodle")
            {
                Color color = Color.Lerp(raw, new Color(.96f, .92f, .80f), .18f);
                color.a = definition.Shape == Shape.Ribbon ? .72f : .60f;
                return color;
            }
            if (definition.Shape == Shape.Egg) return Color.Lerp(raw, Color.white, .12f);
            if (definition.Shape == Shape.Meat || definition.Shape == Shape.Offal ||
                definition.Shape == Shape.Tripe || definition.Shape == Shape.Feet ||
                (definition.Shape == Shape.Ball && definition.Color.r > definition.Color.g * 1.4f))
            {
                return surface == Surface.Body
                    ? new Color(.56f, .38f, .25f, 1f)
                    : new Color(.84f, .75f, .57f, 1f);
            }
            if (definition.Category == "seafood")
            {
                if (definition.Shape == Shape.Shrimp)
                    return surface == Surface.Body ? new Color(1f, .49f, .26f) : new Color(.93f, .69f, .45f);
                if (definition.Shape == Shape.Shell && surface != Surface.Detail)
                    return Color.Lerp(raw, new Color(.73f, .64f, .51f), .12f);
                return new Color(.98f, .94f, .83f, 1f);
            }
            if (definition.Shape == Shape.Leafy || definition.Shape == Shape.Seaweed ||
                definition.Shape == Shape.Broccoli || definition.Shape == Shape.Okra)
                return Color.Lerp(raw, new Color(.43f, .44f, .22f), .48f);
            return Color.Lerp(raw, new Color(.88f, .76f, .51f), .15f);
        }

        /// <summary>プロセスによらず同じ形状になる整数シードを生成します。</summary>
        private static int StableSeed(string key)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in key) hash = hash * 31 + character;
                return hash & int.MaxValue;
            }
        }

        /// <summary>専用形状ごとに識別用の部品を組み立てます。</summary>
        private static void Populate(ModelParts parts, VisualDefinition d, int seed)
        {
            switch (d.Shape)
            {
                case Shape.MushroomBundle: AddMushrooms(parts, d); break;
                case Shape.MushroomFan:
                case Shape.Frill: AddFrills(parts, d); break;
                case Shape.Noodles:
                    parts.Add(Surface.Body, MeshFactory.Noodles(d.Count, d.Size.x, d.Variation, seed),
                        Vector3.zero, Vector3.one);
                    break;
                case Shape.Ribbon:
                case Shape.Seaweed: AddRibbons(parts, d); break;
                case Shape.Feet: AddFeet(parts, d); break;
                case Shape.Ball: AddBall(parts, d, seed); break;
                case Shape.CrabStick: AddCrabStick(parts, d); break;
                case Shape.HollowStick: AddHollowStick(parts, d); break;
                case Shape.RiceStick:
                case Shape.FriedBread:
                case Shape.RootStick: AddSticks(parts, d); break;
                case Shape.Dumpling: AddDumpling(parts, d); break;
                case Shape.Egg: AddEgg(parts, d, seed); break;
                case Shape.Meat: AddMeat(parts, d); break;
                case Shape.Offal: AddOffal(parts, d); break;
                case Shape.Tripe: AddTripe(parts, d); break;
                case Shape.Shell: AddShell(parts, d); break;
                case Shape.Squid: AddSquid(parts, d); break;
                case Shape.Shrimp: AddShrimp(parts, d); break;
                case Shape.Tofu: AddTofu(parts, d, seed); break;
                case Shape.Leafy: AddLeaves(parts, d); break;
                case Shape.Okra: AddOkra(parts, d); break;
                case Shape.Potato: AddPotato(parts, d, seed); break;
                case Shape.Broccoli: AddBroccoli(parts, d); break;
                case Shape.Tomato: AddTomato(parts, d); break;
                case Shape.Sprout: AddSprouts(parts, d); break;
                case Shape.Corn: AddCorn(parts, d); break;
                case Shape.Lotus:
                    parts.Add(Surface.Body, parts.Own(CreateLotusMesh(d)), Vector3.zero, Vector3.one);
                    break;
                default:
                    parts.Add(Surface.Body, MeshFactory.RoundedBox(d.Size, d.Variation),
                        Vector3.zero, Vector3.one);
                    break;
            }
        }

        /// <summary>太さと傘径の違う茎を束ね、きのこの種類を表します。</summary>
        private static void AddMushrooms(ModelParts parts, VisualDefinition d)
        {
            float stemRadius = Mathf.Lerp(.002f, .022f, d.Variation);
            for (int index = 0; index < d.Count; index++)
            {
                float angle = index * 2.399963f;
                float spread = Mathf.Sqrt((index + .5f) / d.Count);
                float height = d.Size.y * (.78f + .20f * Mathf.Sin(index * 1.7f + 1f));
                Vector3 tip = new Vector3(Mathf.Cos(angle) * d.Size.x * .34f * spread,
                    height, Mathf.Sin(angle) * d.Size.z * .34f * spread);
                Vector3 start = new Vector3(tip.x * .45f, 0f, tip.z * .45f);
                Vector3[] path = { start, Vector3.Lerp(start, tip, .55f), tip };
                parts.Add(Surface.Body, MeshFactory.Tube(path, stemRadius, 6), Vector3.zero, Vector3.one);
                float capDiameter = stemRadius * (d.Variation > .55f ? 3.1f : 5.1f);
                parts.Add(Surface.Accent, sphere, tip,
                    new Vector3(capDiameter, capDiameter * .48f, capDiameter));
                parts.Add(Surface.Detail, sphere, tip - Vector3.up * capDiameter * .10f,
                    new Vector3(capDiameter * .83f, capDiameter * .09f, capDiameter * .83f));
            }
        }

        /// <summary>扇状の傘や折れ曲がった耳状のひだを重ねます。</summary>
        private static void AddFrills(ModelParts parts, VisualDefinition d)
        {
            for (int index = 0; index < d.Count; index++)
            {
                float angle = index * 137.5f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(Mathf.Cos(radians) * d.Size.x * .18f,
                    index % 3 * d.Size.y * .18f, Mathf.Sin(radians) * d.Size.z * .18f);
                Mesh leaf = MeshFactory.Leaf(d.Size.x * .52f, d.Size.z * .70f,
                    d.Size.z * d.Variation * (.25f + .08f * (index % 3)));
                parts.Add(index % 4 == 0 ? Surface.Accent : Surface.Body, leaf, position,
                    Vector3.one, new Vector3(15f + index % 3 * 14f, angle, 12f));
            }
        }

        /// <summary>幅と折り曲げ具合の異なる麺、豆皮、海藻の帯を生成します。</summary>
        private static void AddRibbons(ModelParts parts, VisualDefinition d)
        {
            for (int index = 0; index < d.Count; index++)
            {
                float width = d.Shape == Shape.Seaweed ? d.Size.x / (d.Count > 3 ? 3f : 1.8f) : d.Variation;
                Mesh ribbon = parts.Own(CreateRibbonMesh(width, d.Size.x * .82f,
                    d.Size.y * (d.Category == "soy" ? .75f : .45f), index));
                parts.Add(index % 5 == 4 ? Surface.Accent : Surface.Body, ribbon,
                    new Vector3((index % 3 - 1) * width * .65f, (index % 2) * .006f,
                        (index / 3 - .5f) * .012f), Vector3.one,
                    new Vector3(0f, index * 23f - 35f, d.Shape == Shape.Seaweed ? index * 7f : 0f));
            }
        }

        /// <summary>鶏足の掌、足首と関節のある指を組み立てます。</summary>
        private static void AddFeet(ModelParts parts, VisualDefinition d)
        {
            parts.Add(Surface.Body, sphere, new Vector3(0f, 0f, -.02f), new Vector3(.055f, .03f, .08f));
            Vector3[] ankle = { new Vector3(0f, 0f, -.015f), new Vector3(0f, .012f, -.08f) };
            parts.Add(Surface.Body, MeshFactory.Tube(ankle, .011f), Vector3.zero, Vector3.one);
            for (int index = 0; index < d.Count; index++)
            {
                float x = (index - 1.5f) * .022f;
                Vector3[] path = { new Vector3(x * .4f, 0f, 0f), new Vector3(x, .004f, .045f),
                    new Vector3(x * 1.25f, -.003f, index == 3 ? .055f : .095f) };
                parts.Add(Surface.Body, MeshFactory.Tube(path, .0065f), Vector3.zero, Vector3.one);
                for (int joint = 0; joint < 3; joint++)
                    parts.Add(Surface.Accent, sphere, Vector3.Lerp(path[0], path[2], .25f + joint * .25f),
                        new Vector3(.014f, .012f, .012f));
            }
        }

        /// <summary>団子の球体表面に食材片を埋め込みます。</summary>
        private static void AddBall(ModelParts parts, VisualDefinition d, int seed)
        {
            parts.Add(Surface.Body, sphere, Vector3.zero, d.Size);
            AddSurfaceSpots(parts, d.Size, d.Count, seed, .003f + d.Variation * .018f, Surface.Accent);
        }

        /// <summary>かにかまの白い芯、赤い表皮と細い繊維を生成します。</summary>
        private static void AddCrabStick(ModelParts parts, VisualDefinition d)
        {
            parts.Add(Surface.Body, box, Vector3.zero, d.Size);
            parts.Add(Surface.Accent, box, new Vector3(0f, d.Size.y * .46f, 0f),
                new Vector3(d.Size.x, .006f, d.Size.z));
            for (int index = 0; index < d.Count; index++)
            {
                float x = (index / (float)(d.Count - 1) - .5f) * d.Size.x * .85f;
                parts.Add(Surface.Detail, cylinder, new Vector3(x, d.Size.y * .53f, 0f),
                    new Vector3(.002f, d.Size.z * .95f, .002f), new Vector3(90f, 0f, 0f));
            }
        }

        /// <summary>中空の閉じた管と外周の焼き色でちくわを表します。</summary>
        private static void AddHollowStick(ModelParts parts, VisualDefinition d)
        {
            Mesh tube = parts.Own(CreateHollowCylinder(.032f, .016f, d.Size.z, d.Count));
            parts.Add(Surface.Body, tube, Vector3.zero, Vector3.one, new Vector3(90f, 0f, 0f));
            Mesh band = parts.Own(CreateHollowCylinder(.0328f, .032f, d.Size.z * .50f, d.Count));
            parts.Add(Surface.Accent, band, Vector3.zero, Vector3.one, new Vector3(90f, 0f, 0f));
        }

        /// <summary>棒状の根菜、餅、二本合わせの揚げパンを生成します。</summary>
        private static void AddSticks(ModelParts parts, VisualDefinition d)
        {
            for (int index = 0; index < d.Count; index++)
            {
                float x = (index - (d.Count - 1) * .5f) * d.Size.x * .65f;
                Vector3 size = new Vector3(d.Size.x, d.Size.z * (.90f + .05f * (index % 2)), d.Size.y);
                parts.Add(Surface.Body, cylinder, new Vector3(x, 0f, index % 2 * .008f),
                    size, new Vector3(90f, 0f, 0f));
                for (int line = 0; line < 3; line++)
                    parts.Add(Surface.Accent, cylinder, new Vector3(x + (line - 1) * d.Size.x * .22f,
                        d.Size.y * .48f, 0f), new Vector3(.002f, d.Size.z * .85f, .002f),
                        new Vector3(90f, 0f, 0f));
            }
        }

        /// <summary>餃子の半月形の腹とひだ、ワンタンの折り畳んだ皮を生成します。</summary>
        private static void AddDumpling(ModelParts parts, VisualDefinition d)
        {
            parts.Add(Surface.Body, sphere, Vector3.zero, d.Size);
            if (d.Variation > .5f)
            {
                for (int index = 0; index < d.Count; index++)
                    parts.Add(Surface.Body, MeshFactory.Leaf(.075f, .11f, .035f),
                        new Vector3(0f, .02f, 0f), Vector3.one,
                        new Vector3(index * 14f, index * 90f + 45f, 24f));
                return;
            }
            for (int index = 0; index < d.Count; index++)
            {
                float angle = Mathf.PI * index / (d.Count - 1);
                Vector3 crest = new Vector3(Mathf.Cos(angle) * d.Size.x * .43f,
                    Mathf.Sin(angle) * d.Size.y * .47f, 0f);
                Vector3[] fold = { crest + new Vector3(-.006f, -.012f, -.025f), crest,
                    crest + new Vector3(.006f, -.012f, .025f) };
                parts.Add(Surface.Accent, MeshFactory.Tube(fold, .003f), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>上下で幅が異なる卵形と、殻に沿った斑点を生成します。</summary>
        private static void AddEgg(ModelParts parts, VisualDefinition d, int seed)
        {
            Mesh egg = parts.Own(CreateEggMesh());
            parts.Add(Surface.Body, egg, Vector3.zero, d.Size);
            System.Random random = new System.Random(seed);
            for (int index = 0; index < d.Count; index++)
            {
                float y = (float)random.NextDouble() * 1.7f - .85f;
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(1f - y * y) * (.94f - .12f * y) * .5f;
                Vector3 normal = new Vector3(Mathf.Cos(angle) * Mathf.Sqrt(1f - y * y), y,
                    Mathf.Sin(angle) * Mathf.Sqrt(1f - y * y)).normalized;
                Vector3 position = Vector3.Scale(new Vector3(Mathf.Cos(angle) * radius,
                    y * .5f, Mathf.Sin(angle) * radius), d.Size);
                float diameter = .002f + d.Variation * .019f * (float)random.NextDouble();
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up,
                    new Vector3(normal.x / d.Size.x, normal.y / d.Size.y, normal.z / d.Size.z).normalized);
                parts.Groups[(int)Surface.Accent].Add((sphere,
                    Matrix4x4.TRS(position, rotation, new Vector3(diameter, .0015f, diameter * .72f))));
            }
        }

        /// <summary>薄切り肉の曲がった面と脂肪筋を配置します。</summary>
        private static void AddMeat(ModelParts parts, VisualDefinition d)
        {
            if (d.Size.y > .04f)
                parts.Add(Surface.Body, sphere, Vector3.zero, d.Size);
            else
                parts.Add(Surface.Body, parts.Own(CreateRibbonMesh(d.Size.z, d.Size.x, d.Size.y * .4f, 2)),
                    Vector3.zero, Vector3.one);
            for (int index = 0; index < d.Count; index++)
            {
                float z = (index / (float)Mathf.Max(1, d.Count - 1) - .5f) * d.Size.z * .75f;
                Vector3[] path = new Vector3[9];
                for (int step = 0; step < path.Length; step++)
                {
                    float t = step / (float)(path.Length - 1);
                    float x = (t - .5f) * d.Size.x * .86f;
                    float y = d.Size.y > .04f
                        ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(x / (d.Size.x * .5f), 2f)
                            - Mathf.Pow(z / (d.Size.z * .5f), 2f))) * d.Size.y * .5f
                        : RibbonHeight(t, d.Size.y * .4f, 2) + .002f;
                    path[step] = new Vector3(x, y + .001f,
                        z + Mathf.Sin(t * 6f + index) * d.Size.z * .045f);
                }
                float fat = d.Variation > .8f ? .007f : .002f + d.Variation * .0025f;
                parts.Add(Surface.Accent, MeshFactory.Tube(path, fat), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>もつの曲がった管と外側の脂肪を生成します。</summary>
        private static void AddOffal(ModelParts parts, VisualDefinition d)
        {
            for (int index = 0; index < d.Count; index++)
            {
                Vector3[] path = new Vector3[11];
                for (int step = 0; step < path.Length; step++)
                {
                    float angle = step / 10f * Mathf.PI * 1.5f + index;
                    path[step] = new Vector3(Mathf.Cos(angle) * .04f + index * .013f,
                        Mathf.Sin(step * .5f) * .013f, Mathf.Sin(angle) * .045f);
                }
                parts.Add(Surface.Body, MeshFactory.Tube(path, .012f, 8), Vector3.zero, Vector3.one);
                for (int fat = 0; fat < 5; fat++)
                    parts.Add(Surface.Accent, sphere, path[fat * 2] + Vector3.up * .01f,
                        new Vector3(.015f, .009f, .02f));
            }
        }

        /// <summary>ハチノスの六角形の隆起を薄い土台に配置します。</summary>
        private static void AddTripe(ModelParts parts, VisualDefinition d)
        {
            parts.Add(Surface.Body, box, Vector3.zero, d.Size);
            for (int row = 0; row < d.Count; row++)
            {
                for (int column = 0; column < 6; column++)
                {
                    Vector3 center = new Vector3((column - 2.5f) * .025f + row % 2 * .0125f,
                        d.Size.y * .52f, (row - 2f) * .023f);
                    Vector3[] hexagon = new Vector3[7];
                    for (int vertex = 0; vertex < hexagon.Length; vertex++)
                    {
                        float angle = vertex * Mathf.PI / 3f;
                        hexagon[vertex] = center + new Vector3(Mathf.Cos(angle) * .014f,
                            0f, Mathf.Sin(angle) * .014f);
                    }
                    parts.Add(Surface.Accent, MeshFactory.Tube(hexagon, .002f, 4), Vector3.zero, Vector3.one);
                }
            }
        }

        /// <summary>蝶番で接した二枚の湾曲殻、殻の筋と貝柱を生成します。</summary>
        private static void AddShell(ModelParts parts, VisualDefinition d)
        {
            Mesh shell = parts.Own(CreateShellMesh(d.Variation > .5f));
            for (int side = 0; side < 2; side++)
            {
                Vector3 rotation = new Vector3(side == 0 ? 18f : -145f, 0f, 0f);
                Vector3 size = new Vector3(d.Size.x, d.Size.y, d.Size.z);
                parts.Add(Surface.Body, shell, Vector3.zero, size, rotation);
                Matrix4x4 transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(rotation), size);
                for (int ridge = 0; ridge < d.Count; ridge++)
                {
                    float angle = Mathf.Lerp(-1.45f, 1.45f, ridge / (float)(d.Count - 1));
                    Vector3[] path = new Vector3[8];
                    for (int step = 0; step < path.Length; step++)
                    {
                        float t = .07f + step / 7f * .92f;
                        path[step] = transform.MultiplyPoint3x4(ShellPoint(t, angle,
                            d.Variation > .5f) + Vector3.up * .009f);
                    }
                    parts.Add(Surface.Accent, MeshFactory.Tube(path, .0012f, 4), Vector3.zero, Vector3.one);
                }
            }
            parts.Add(Surface.Detail, sphere, new Vector3(0f, .008f, d.Size.z * .28f),
                new Vector3(d.Size.x * .38f, .022f, d.Size.z * .36f));
        }

        /// <summary>イカの開いた輪切りと短い触腕を生成します。</summary>
        private static void AddSquid(ModelParts parts, VisualDefinition d)
        {
            Mesh ring = parts.Own(CreateHollowCylinder(.044f, .031f, .026f, 24));
            for (int index = 0; index < d.Count; index++)
                parts.Add(Surface.Body, ring, new Vector3((index - 1) * .038f, index * .009f, index * .023f),
                    new Vector3(1f, 1f, .83f), new Vector3(index * 8f, index * 21f, 0f));
            for (int index = 0; index < 3; index++)
            {
                Vector3[] tentacle = { new Vector3(-.025f + index * .017f, 0f, -.03f),
                    new Vector3(-.04f + index * .02f, .012f, -.06f),
                    new Vector3(-.018f + index * .018f, .005f, -.08f) };
                parts.Add(Surface.Accent, MeshFactory.Tube(tentacle, .004f), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>曲がったエビの体に殻節、尾扇、眼と触角を配置します。</summary>
        private static void AddShrimp(ModelParts parts, VisualDefinition d)
        {
            Vector3[] path = new Vector3[17];
            for (int index = 0; index < path.Length; index++)
            {
                float angle = index / 16f * Mathf.PI * 1.35f - .35f;
                path[index] = new Vector3(Mathf.Cos(angle) * .055f, .004f * Mathf.Sin(angle * 2f),
                    Mathf.Sin(angle) * .055f);
            }
            for (int segment = 0; segment < d.Count; segment++)
            {
                int index = segment * 2;
                float radius = Mathf.Lerp(.015f, .006f, segment / (float)(d.Count - 1));
                Vector3[] section = { path[index], path[index + 1], path[index + 2] };
                parts.Add(Surface.Body, MeshFactory.Tube(section, radius, 8), Vector3.zero, Vector3.one);
                Vector3 normal = (path[index + 1] - path[index]).normalized;
                Vector3 side = Vector3.Cross(normal, Vector3.up).normalized;
                Vector3[] seam = new Vector3[7];
                for (int edge = 0; edge < seam.Length; edge++)
                {
                    float angle = edge / 6f * Mathf.PI;
                    seam[edge] = path[index + 1] + side * Mathf.Cos(angle) * radius * 1.02f
                        + Vector3.up * Mathf.Sin(angle) * radius * 1.02f;
                }
                parts.Add(Surface.Accent, MeshFactory.Tube(seam, .0013f, 4), Vector3.zero, Vector3.one);
            }
            for (int fan = 0; fan < 3; fan++)
                parts.Add(Surface.Body, MeshFactory.Leaf(.012f, .034f, .003f),
                    path[path.Length - 1], Vector3.one, new Vector3(0f, 45f + fan * 28f, 0f));
            for (int eye = 0; eye < 2; eye++)
                parts.Add(Surface.Accent, sphere, path[0] + new Vector3(0f, .012f, (eye * 2 - 1) * .008f),
                    Vector3.one * .005f);
            for (int antenna = 0; antenna < 2; antenna++)
            {
                Vector3[] feeler = { path[0], path[0] + new Vector3(.024f, .003f, antenna == 0 ? .023f : -.02f),
                    path[0] + new Vector3(.052f, .002f, antenna == 0 ? .033f : -.024f) };
                parts.Add(Surface.Accent, MeshFactory.Tube(feeler, .0008f, 4), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>豆腐の角丸ブロック、揚げ皮の断面と表面の粒を生成します。</summary>
        private static void AddTofu(ModelParts parts, VisualDefinition d, int seed)
        {
            parts.Add(Surface.Body, MeshFactory.RoundedBox(d.Size, d.Variation),
                Vector3.zero, Vector3.one);
            if (d.Color.r > d.Color.g * 1.2f)
                parts.Add(Surface.Detail, box, new Vector3(0f, 0f, d.Size.z * .502f),
                    new Vector3(d.Size.x * .84f, d.Size.y * .75f, .002f));
            System.Random random = new System.Random(seed);
            for (int index = 0; index < d.Count; index++)
            {
                Vector3 position = new Vector3(((float)random.NextDouble() - .5f) * d.Size.x * .85f,
                    d.Size.y * .501f, ((float)random.NextDouble() - .5f) * d.Size.z * .85f);
                parts.Add(Surface.Accent, sphere, position, new Vector3(.003f, .0015f, .003f));
            }
        }

        /// <summary>茎、葉脈と種類別の幅や切れ込みを持つ葉を束ねます。</summary>
        private static void AddLeaves(ModelParts parts, VisualDefinition d)
        {
            for (int index = 0; index < d.Count; index++)
            {
                float angle = (index - (d.Count - 1) * .5f) * 11f;
                float x = (index - (d.Count - 1) * .5f) * d.Size.x / (d.Count * 2f);
                float stemLength = d.Size.z * (d.Variation > .6f ? .44f : .64f);
                Vector3[] stem = { new Vector3(x * .25f, 0f, -d.Size.z * .45f),
                    new Vector3(x, .003f, -d.Size.z * .45f + stemLength),
                    new Vector3(x * 1.1f, .008f, d.Size.z * .23f) };
                float radius = d.Variation > .6f ? .006f + d.Variation * .006f : .002f;
                parts.Add(Surface.Accent, MeshFactory.Tube(stem, radius), Vector3.zero, Vector3.one);
                float width = d.Size.x * (.12f + d.Variation * .58f);
                float length = d.Size.z * (.40f + d.Variation * .28f);
                Vector3 leafPosition = stem[1] - new Vector3(0f, 0f, length * .10f);
                if (d.Variation < .3f)
                {
                    for (int lobe = 0; lobe < 5; lobe++)
                    {
                        float side = lobe % 2 == 0 ? -1f : 1f;
                        parts.Add(Surface.Body, MeshFactory.Leaf(width * 1.5f, length * .43f, length * .12f),
                            leafPosition + new Vector3(side * width * .35f, .004f, lobe * length * .10f),
                            Vector3.one, new Vector3(8f, angle + side * 52f, 0f));
                    }
                }
                else
                {
                    parts.Add(Surface.Body, MeshFactory.Leaf(width, length, length * (.08f + d.Variation * .20f)),
                        leafPosition, Vector3.one, new Vector3(-12f + index * 4f, angle, index % 2 * 8f));
                }
                parts.Add(Surface.Detail, MeshFactory.Tube(new[] { leafPosition + Vector3.up * .004f,
                    leafPosition + new Vector3(0f, .012f, length * .60f) }, .0013f, 4), Vector3.zero, Vector3.one);
            }
        }

        /// <summary>オクラの五角星断面と種を生成します。</summary>
        private static void AddOkra(ModelParts parts, VisualDefinition d)
        {
            Mesh star = parts.Own(CreateStarPrism(.035f, .023f, d.Size.y, d.Count));
            parts.Add(Surface.Body, star, Vector3.zero, Vector3.one);
            for (int index = 0; index < d.Count; index++)
            {
                float angle = index * Mathf.PI * 2f / d.Count;
                parts.Add(Surface.Accent, sphere,
                    new Vector3(Mathf.Cos(angle) * .015f, d.Size.y * .505f, Mathf.Sin(angle) * .015f),
                    new Vector3(.008f, .002f, .010f), new Vector3(0f, -angle * Mathf.Rad2Deg, 0f));
            }
        }

        /// <summary>じゃがいもの切片に小さな芽の跡を配置します。</summary>
        private static void AddPotato(ModelParts parts, VisualDefinition d, int seed)
        {
            parts.Add(Surface.Body, sphere, Vector3.zero, d.Size);
            AddSurfaceSpots(parts, d.Size, d.Count, seed, .004f, Surface.Accent);
        }

        /// <summary>枝分かれする茎と粒の集合からブロッコリーを生成します。</summary>
        private static void AddBroccoli(ModelParts parts, VisualDefinition d)
        {
            parts.Add(Surface.Accent, cylinder, new Vector3(0f, -.025f, 0f), new Vector3(.032f, .085f, .028f));
            for (int index = 0; index < d.Count; index++)
            {
                float angle = index * 2.399963f;
                float radius = .047f * Mathf.Sqrt(index / (float)d.Count);
                Vector3 tip = new Vector3(Mathf.Cos(angle) * radius, .025f + .015f * Mathf.Cos(index),
                    Mathf.Sin(angle) * radius);
                parts.Add(Surface.Accent, MeshFactory.Tube(new[] { new Vector3(0f, -.015f, 0f), tip }, .006f),
                    Vector3.zero, Vector3.one);
                parts.Add(Surface.Body, sphere, tip, Vector3.one * .041f);
                for (int bud = 0; bud < 6; bud++)
                {
                    float budAngle = bud * Mathf.PI / 3f;
                    parts.Add(Surface.Body, sphere, tip + new Vector3(Mathf.Cos(budAngle) * .015f,
                        .016f, Mathf.Sin(budAngle) * .015f), Vector3.one * .011f);
                }
            }
        }

        /// <summary>トマトの赤い球体に星状のへたを配置します。</summary>
        private static void AddTomato(ModelParts parts, VisualDefinition d)
        {
            parts.Add(Surface.Body, sphere, Vector3.zero, d.Size);
            for (int index = 0; index < d.Count; index++)
                parts.Add(Surface.Accent, MeshFactory.Leaf(.008f, .027f, .004f),
                    new Vector3(0f, d.Size.y * .49f, 0f), Vector3.one,
                    new Vector3(-8f, index * 360f / d.Count, 0f));
        }

        /// <summary>もやしの曲がった白い茎、豆の頭と根先を生成します。</summary>
        private static void AddSprouts(ModelParts parts, VisualDefinition d)
        {
            for (int index = 0; index < d.Count; index++)
            {
                float offset = (index - d.Count * .5f) * .007f;
                Vector3[] path = { new Vector3(offset, 0f, -.07f), new Vector3(offset + .008f, .007f, -.015f),
                    new Vector3(offset - .006f, .006f, .04f), new Vector3(offset + .003f, .003f, .065f) };
                parts.Add(Surface.Body, MeshFactory.Tube(path, .002f), Vector3.zero, Vector3.one);
                parts.Add(Surface.Accent, sphere, path[path.Length - 1], new Vector3(.009f, .006f, .012f));
                parts.Add(Surface.Detail, MeshFactory.Tube(new[] { path[0], path[0] + new Vector3(.006f, 0f, -.02f) }, .0008f, 4),
                    Vector3.zero, Vector3.one);
            }
        }

        /// <summary>芯の周囲に独立した粒を螺旋状の列で並べます。</summary>
        private static void AddCorn(ModelParts parts, VisualDefinition d)
        {
            parts.Add(Surface.Body, cylinder, Vector3.zero, new Vector3(.025f, d.Size.z, .025f),
                new Vector3(90f, 0f, 0f));
            for (int row = 0; row < d.Count; row++)
            {
                float t = row / (float)(d.Count - 1);
                float radius = .018f * (1f - t * .28f);
                for (int column = 0; column < 8; column++)
                {
                    float angle = column * Mathf.PI / 4f + row % 2 * .12f;
                    parts.Add(Surface.Accent, sphere, new Vector3(Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius, (t - .5f) * d.Size.z * .90f),
                        new Vector3(.012f, .012f, .016f));
                }
            }
        }

        /// <summary>楕円体の表面に固定シードの小さな斑点を配置します。</summary>
        private static void AddSurfaceSpots(ModelParts parts, Vector3 size, int count, int seed,
            float diameter, Surface surface)
        {
            System.Random random = new System.Random(seed);
            for (int index = 0; index < count; index++)
            {
                float y = (float)random.NextDouble() * 2f - 1f;
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radial = Mathf.Sqrt(1f - y * y);
                Vector3 normal = new Vector3(Mathf.Cos(angle) * radial, y, Mathf.Sin(angle) * radial);
                Vector3 position = Vector3.Scale(normal, size) * .498f;
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up,
                    new Vector3(normal.x / size.x, normal.y / size.y, normal.z / size.z).normalized);
                parts.Groups[(int)surface].Add((sphere, Matrix4x4.TRS(position, rotation,
                    new Vector3(diameter, .002f, diameter * .72f))));
            }
        }

        /// <summary>帯の中心線の高さを計算します。</summary>
        private static float RibbonHeight(float t, float curl, int variation)
        {
            return Mathf.Sin(t * Mathf.PI * 2f + variation * .7f) * curl
                + Mathf.Sin(t * Mathf.PI * 4f) * curl * .22f;
        }

        /// <summary>両面を持つ波打った帯メッシュを生成します。</summary>
        private static Mesh CreateRibbonMesh(float width, float length, float curl, int variation)
        {
            const int segments = 20;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uv = new List<Vector2>();
            for (int index = 0; index < segments; index++)
            {
                float t0 = index / (float)segments;
                float t1 = (index + 1) / (float)segments;
                float w0 = width * (.48f + .035f * Mathf.Sin(index * .8f));
                float w1 = width * (.48f + .035f * Mathf.Sin((index + 1) * .8f));
                Vector3 a = new Vector3((t0 - .5f) * length, RibbonHeight(t0, curl, variation), -w0);
                Vector3 b = new Vector3((t0 - .5f) * length, RibbonHeight(t0, curl, variation), w0);
                Vector3 c = new Vector3((t1 - .5f) * length, RibbonHeight(t1, curl, variation), w1);
                Vector3 e = new Vector3((t1 - .5f) * length, RibbonHeight(t1, curl, variation), -w1);
                AddQuad(vertices, triangles, uv, a, b, c, e);
                AddQuad(vertices, triangles, uv, e, c, b, a);
            }
            return FinishMesh("IngredientRibbon", vertices, triangles, uv);
        }

        /// <summary>内壁と切断面を備えた、穴を塞がない管を生成します。</summary>
        private static Mesh CreateHollowCylinder(float outer, float inner, float height, int segments)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uv = new List<Vector2>();
            for (int index = 0; index < segments; index++)
            {
                float a = index * Mathf.PI * 2f / segments;
                float b = (index + 1) * Mathf.PI * 2f / segments;
                Vector3 oa = new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer);
                Vector3 ob = new Vector3(Mathf.Cos(b) * outer, 0f, Mathf.Sin(b) * outer);
                Vector3 ia = new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner);
                Vector3 ib = new Vector3(Mathf.Cos(b) * inner, 0f, Mathf.Sin(b) * inner);
                Vector3 up = Vector3.up * height * .5f;
                AddQuad(vertices, triangles, uv, oa - up, oa + up, ob + up, ob - up);
                AddQuad(vertices, triangles, uv, ib - up, ib + up, ia + up, ia - up);
                AddQuad(vertices, triangles, uv, oa + up, ia + up, ib + up, ob + up);
                AddQuad(vertices, triangles, uv, ob - up, ib - up, ia - up, oa - up);
            }
            return FinishMesh("IngredientHollowCylinder", vertices, triangles, uv);
        }

        /// <summary>上部の細い卵形を単位寸法で生成します。</summary>
        private static Mesh CreateEggMesh()
        {
            Mesh mesh = UnityEngine.Object.Instantiate(sphere);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 point = vertices[index];
                Vector3 normal = normals[index];
                float factor = .94f - .24f * point.y;
                // 再計算では未使用の極頂点がゼロ法線になるため、変形の逆転置で法線を変換します。
                normals[index] = new Vector3(normal.x / factor,
                    normal.y + .24f * (point.x * normal.x + point.z * normal.z) / factor,
                    normal.z / factor).normalized;
                point.x *= factor;
                point.z *= factor;
                vertices[index] = point;
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            mesh.name = "IngredientEgg";
            return mesh;
        }

        /// <summary>殻の中心から縁までの湾曲した表面点を計算します。</summary>
        private static Vector3 ShellPoint(float radial, float angle, bool scallop)
        {
            float width = scallop ? .50f : .46f;
            return new Vector3(Mathf.Sin(angle) * radial * width,
                Mathf.Sin(radial * Mathf.PI) * .25f,
                Mathf.Cos(angle) * radial * .80f);
        }

        /// <summary>蝶番側を共有し、内面も見える扇状の殻を生成します。</summary>
        private static Mesh CreateShellMesh(bool scallop)
        {
            const int radialSegments = 10;
            const int angleSegments = 24;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uv = new List<Vector2>();
            for (int radial = 0; radial < radialSegments; radial++)
            {
                for (int angle = 0; angle < angleSegments; angle++)
                {
                    float r0 = radial / (float)radialSegments;
                    float r1 = (radial + 1) / (float)radialSegments;
                    float a0 = Mathf.Lerp(-1.5f, 1.5f, angle / (float)angleSegments);
                    float a1 = Mathf.Lerp(-1.5f, 1.5f, (angle + 1) / (float)angleSegments);
                    Vector3 a = ShellPoint(r0, a0, scallop);
                    Vector3 b = ShellPoint(r0, a1, scallop);
                    Vector3 c = ShellPoint(r1, a1, scallop);
                    Vector3 d = ShellPoint(r1, a0, scallop);
                    if (radial == 0)
                    {
                        AddTriangle(vertices, triangles, uv, a, c, d);
                        AddTriangle(vertices, triangles, uv, d, c, a);
                    }
                    else
                    {
                        AddQuad(vertices, triangles, uv, a, b, c, d);
                        AddQuad(vertices, triangles, uv, d, c, b, a);
                    }
                }
            }
            return FinishMesh("IngredientShell", vertices, triangles, uv);
        }

        /// <summary>切断面まで星形の輪郭を持つ柱を生成します。</summary>
        private static Mesh CreateStarPrism(float outer, float inner, float height, int points)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uv = new List<Vector2>();
            Vector3 up = Vector3.up * height * .5f;
            for (int index = 0; index < points * 2; index++)
            {
                float a = index * Mathf.PI / points;
                float b = (index + 1) * Mathf.PI / points;
                float ra = index % 2 == 0 ? outer : inner;
                float rb = index % 2 == 0 ? inner : outer;
                Vector3 va = new Vector3(Mathf.Cos(a) * ra, 0f, Mathf.Sin(a) * ra);
                Vector3 vb = new Vector3(Mathf.Cos(b) * rb, 0f, Mathf.Sin(b) * rb);
                AddQuad(vertices, triangles, uv, va - up, va + up, vb + up, vb - up);
                AddTriangle(vertices, triangles, uv, up, vb + up, va + up);
                AddTriangle(vertices, triangles, uv, -up, va - up, vb - up);
            }
            return FinishMesh("IngredientOkra", vertices, triangles, uv);
        }

        /// <summary>穴に頂点も面も配置せず、内壁を持つれんこん断面を生成します。</summary>
        private static Mesh CreateLotusMesh(VisualDefinition d)
        {
            const int resolution = 64;
            float radius = d.Size.x * .5f;
            float cell = radius * 2f / resolution;
            bool[,] solid = new bool[resolution, resolution];
            for (int x = 0; x < resolution; x++)
                for (int z = 0; z < resolution; z++)
                    solid[x, z] = LotusSolid((x + .5f) * cell - radius,
                        (z + .5f) * cell - radius, radius, d.Count);
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uv = new List<Vector2>();
            for (int x = 0; x < resolution; x++)
            {
                for (int z = 0; z < resolution; z++)
                {
                    if (!solid[x, z]) continue;
                    float x0 = x * cell - radius;
                    float z0 = z * cell - radius;
                    Vector3 a = new Vector3(x0, d.Size.y * .5f, z0);
                    Vector3 b = a + Vector3.forward * cell;
                    Vector3 c = b + Vector3.right * cell;
                    Vector3 e = a + Vector3.right * cell;
                    Vector3 depth = Vector3.down * d.Size.y;
                    AddQuad(vertices, triangles, uv, a, b, c, e);
                    AddQuad(vertices, triangles, uv, e + depth, c + depth, b + depth, a + depth);
                    if (x == 0 || !solid[x - 1, z]) AddQuad(vertices, triangles, uv, b, a, a + depth, b + depth);
                    if (x == resolution - 1 || !solid[x + 1, z]) AddQuad(vertices, triangles, uv, e, c, c + depth, e + depth);
                    if (z == 0 || !solid[x, z - 1]) AddQuad(vertices, triangles, uv, a, e, e + depth, a + depth);
                    if (z == resolution - 1 || !solid[x, z + 1]) AddQuad(vertices, triangles, uv, c, b, b + depth, c + depth);
                }
            }
            return FinishMesh("IngredientLotusWithHoles", vertices, triangles, uv);
        }

        /// <summary>外周、中央穴と放射状の八穴に対する実体の有無を判定します。</summary>
        private static bool LotusSolid(float x, float z, float radius, int holes)
        {
            float distance = x * x + z * z;
            if (distance > radius * radius || distance < radius * radius * .013f) return false;
            for (int index = 0; index < holes; index++)
            {
                float angle = index * Mathf.PI * 2f / holes;
                float radial = x * Mathf.Cos(angle) + z * Mathf.Sin(angle) - radius * .60f;
                float lateral = -x * Mathf.Sin(angle) + z * Mathf.Cos(angle);
                if (radial * radial / (radius * radius * .060f)
                    + lateral * lateral / (radius * radius * .024f) < 1f) return false;
            }
            return true;
        }

        /// <summary>表面法線を分離した四角面を追加します。</summary>
        private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uv,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one); uv.Add(Vector2.right);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        /// <summary>切断面に三角形を追加します。</summary>
        private static void AddTriangle(List<Vector3> vertices, List<int> triangles, List<Vector2> uv,
            Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            uv.Add(new Vector2(.5f, .5f)); uv.Add(Vector2.zero); uv.Add(Vector2.one);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        /// <summary>大きい頂点集合も扱えるメッシュを確定します。</summary>
        private static Mesh FinishMesh(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uv)
        {
            Mesh mesh = new Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>共有マテリアルを複製せず、食材の加熱状態と表面の濡れを更新します。</summary>
    [DisallowMultipleComponent]
    public sealed class IngredientView : MonoBehaviour
    {
        /// <summary>描画対象と加熱前後の表面状態を保持します。</summary>
        private sealed class RendererState
        {
            internal readonly Renderer Renderer;
            internal readonly Color Raw;
            internal readonly Color Cooked;
            internal readonly Color Overcooked;
            internal readonly float DrySmoothness;
            internal readonly float CookedSmoothness;
            internal readonly float OvercookedSmoothness;
            internal readonly MaterialPropertyBlock Block;

            /// <summary>原色と加熱色をコピーし、描画プロパティを一度だけ確保します。</summary>
            internal RendererState(Renderer renderer, Color raw, Color cooked, Color overcooked,
                float drySmoothness, float cookedSmoothness, float overcookedSmoothness)
            {
                Renderer = renderer;
                Raw = raw;
                Cooked = cooked;
                Overcooked = overcooked;
                DrySmoothness = drySmoothness;
                CookedSmoothness = cookedSmoothness;
                OvercookedSmoothness = overcookedSmoothness;
                Block = new MaterialPropertyBlock();
            }
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private RendererState[] states = Array.Empty<RendererState>();
        private float doneness;
        private float wetness;
        private bool initialized;

        /// <summary>生成時にレンダラー別の原色、加熱色、艶を登録します。</summary>
        internal void Initialize(Renderer[] renderers, Color[] raw, Color[] cooked, Color[] overcooked,
            float[] drySmoothness, float[] cookedSmoothness, float[] overcookedSmoothness = null)
        {
            if (initialized) throw new InvalidOperationException("食材ビューは既に初期化されています。");
            if (renderers == null || raw == null || cooked == null || overcooked == null ||
                drySmoothness == null || cookedSmoothness == null)
                throw new ArgumentException("描画状態の配列はnullにできません。");
            int count = renderers.Length;
            if (count == 0 || raw.Length != count || cooked.Length != count || overcooked.Length != count ||
                drySmoothness.Length != count || cookedSmoothness.Length != count ||
                (overcookedSmoothness != null && overcookedSmoothness.Length != count))
                throw new ArgumentException("レンダラー数と各表面状態の要素数を一致させてください。");
            RendererState[] validated = new RendererState[count];
            for (int index = 0; index < count; index++)
            {
                if (renderers[index] == null) throw new ArgumentException("描画対象が失われています。");
                OwnedObjects.ValidateColor(raw[index], nameof(raw)); OwnedObjects.ValidateColor(cooked[index], nameof(cooked)); OwnedObjects.ValidateColor(overcooked[index], nameof(overcooked));
                OwnedObjects.ValidateFinite(drySmoothness[index], nameof(drySmoothness));
                OwnedObjects.ValidateFinite(cookedSmoothness[index], nameof(cookedSmoothness));
                float overGloss = overcookedSmoothness == null ? cookedSmoothness[index] : overcookedSmoothness[index];
                OwnedObjects.ValidateFinite(overGloss, nameof(overcookedSmoothness));
                validated[index] = new RendererState(renderers[index], raw[index], cooked[index], overcooked[index],
                    Mathf.Clamp01(drySmoothness[index]), Mathf.Clamp01(cookedSmoothness[index]), Mathf.Clamp01(overGloss));
            }
            states = validated;
            initialized = true;
            ApplyState();
        }

        /// <summary>テクスチャの原色を維持し、加熱時に乗算する色と艶を登録します。</summary>
        internal void InitializeTextured(Renderer[] renderers, FoodSurface[] surfaces)
        {
            if (renderers == null || surfaces == null || renderers.Length != surfaces.Length)
                throw new ArgumentException("表面数とレンダラー数を一致させてください。");
            int count = surfaces.Length;
            Color[] raw = new Color[count]; Color[] cooked = new Color[count]; Color[] over = new Color[count];
            float[] dry = new float[count]; float[] gloss = new float[count]; float[] overGloss = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (surfaces[i] == null) throw new ArgumentException("表面状態が必要です。");
                raw[i] = surfaces[i].RawTint; cooked[i] = surfaces[i].CookedTint; over[i] = surfaces[i].OvercookedTint;
                dry[i] = surfaces[i].RawSmoothness; gloss[i] = surfaces[i].CookedSmoothness;
                overGloss[i] = surfaces[i].OvercookedSmoothness;
            }
            Initialize(renderers, raw, cooked, over, dry, gloss, overGloss);
        }

        /// <summary>加熱度を更新します。0は生、1は適温、1.5は過加熱で、範囲外の有限値は制限します。</summary>
        /// <param name="doneness">0〜1.5の加熱度です。</param>
        /// <exception cref="ArgumentOutOfRangeException">NaNまたは無限大の場合です。</exception>
        /// <exception cref="InvalidOperationException">生成処理で初期化されていない場合です。</exception>
        public void SetDoneness(float doneness)
        {
            OwnedObjects.ValidateFinite(doneness, nameof(doneness));
            EnsureInitialized();
            this.doneness = Mathf.Clamp(doneness, 0f, 1.5f);
            ApplyState();
        }

        /// <summary>表面の濡れ具合を更新します。乾燥時の艶から濡れた艶へ補間します。</summary>
        /// <param name="wetness">0は乾燥、1は濡れた状態です。範囲外の有限値は制限します。</param>
        /// <exception cref="ArgumentOutOfRangeException">NaNまたは無限大の場合です。</exception>
        /// <exception cref="InvalidOperationException">生成処理で初期化されていない場合です。</exception>
        public void SetWet(float wetness)
        {
            OwnedObjects.ValidateFinite(wetness, nameof(wetness));
            EnsureInitialized();
            this.wetness = Mathf.Clamp01(wetness);
            ApplyState();
        }

        /// <summary>色、透明度と艶をレンダラーのプロパティブロックだけで反映します。</summary>
        private void ApplyState()
        {
            float cookedAmount = Mathf.Clamp01(doneness);
            float overAmount = Mathf.Clamp01((doneness - 1f) * 2f);
            foreach (RendererState state in states)
            {
                if (state.Renderer == null) continue;
                Color color = Color.Lerp(Color.Lerp(state.Raw, state.Cooked, cookedAmount), state.Overcooked, overAmount);
                float dry = Mathf.Lerp(Mathf.Lerp(state.DrySmoothness, state.CookedSmoothness, cookedAmount),
                    state.OvercookedSmoothness, overAmount);
                float smoothness = Mathf.Lerp(dry, .92f, wetness);
                state.Renderer.GetPropertyBlock(state.Block);
                state.Block.SetColor(BaseColorId, color);
                state.Block.SetColor(ColorId, color);
                state.Block.SetFloat(SmoothnessId, smoothness);
                state.Block.SetFloat(GlossinessId, smoothness);
                state.Renderer.SetPropertyBlock(state.Block);
            }
        }

        /// <summary>未初期化の手動追加コンポーネントを検出します。</summary>
        private void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("IngredientVisuals.Buildで生成してください。");
        }
    }
}
