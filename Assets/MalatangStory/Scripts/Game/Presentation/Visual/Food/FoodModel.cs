using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>カテゴリ実装がIDに対応する共有モデルを生成する契約です。</summary>
    public interface IFoodBuilder
    {
        /// <summary>対応IDのモデルを返します。非対応ならnullを返します。</summary>
        FoodModel Build(string id);
    }

    /// <summary>テクスチャに掛ける加熱色と艶の三段階です。</summary>
    public sealed class FoodSurface
    {
        public readonly Color RawTint;
        public readonly Color CookedTint;
        public readonly Color OvercookedTint;
        public readonly float RawSmoothness;
        public readonly float CookedSmoothness;
        public readonly float OvercookedSmoothness;

        /// <summary>原色はテクスチャに保存し、加熱による倍率と透明度だけを設定します。</summary>
        public FoodSurface(Color cookedTint, Color overcookedTint, float rawSmoothness = .3f,
            float cookedSmoothness = .65f, float alpha = 1f, float overcookedSmoothness = .48f)
        {
            RawTint = new Color(1f, 1f, 1f, alpha);
            CookedTint = cookedTint; OvercookedTint = overcookedTint;
            RawSmoothness = rawSmoothness; CookedSmoothness = cookedSmoothness;
            OvercookedSmoothness = overcookedSmoothness;
        }
    }

    /// <summary>ID単位で所有・共有する完成メッシュ、マテリアル、テクスチャです。</summary>
    public sealed class FoodModel : IDisposable
    {
        public readonly Mesh[] Meshes;
        public readonly Material[] Materials;
        public readonly FoodSurface[] Surfaces;
        public readonly Bounds Bounds;
        public readonly int TriangleCount;
        private readonly Texture2D[] textures;

        /// <summary>生成済みリソースの所有権を受け取ります。</summary>
        internal FoodModel(Mesh[] meshes, Material[] materials, Texture2D[] textures,
            FoodSurface[] surfaces, Bounds bounds, int triangles)
        {
            Meshes = meshes; Materials = materials; this.textures = textures;
            Surfaces = surfaces; Bounds = bounds; TriangleCount = triangles;
        }

        /// <summary>キャッシュが保持する描画リソースの生存を確認します。</summary>
        public bool IsAlive()
        {
            foreach (Mesh mesh in Meshes) if (mesh == null) return false;
            foreach (Material material in Materials) if (material == null) return false;
            foreach (Texture2D texture in textures) if (texture == null) return false;
            return true;
        }

        /// <summary>利用中のインスタンスを破棄した後に所有リソースを解放します。</summary>
        public void Dispose()
        {
            foreach (Mesh mesh in Meshes) OwnedObjects.Destroy(mesh);
            foreach (Material material in Materials) OwnedObjects.Destroy(material);
            foreach (Texture2D texture in textures) OwnedObjects.Destroy(texture);
        }
    }

    /// <summary>最大3表面に部品を結合し、中間メッシュの所有権を管理します。</summary>
    public sealed class FoodModelBuilder : IDisposable
    {
        private readonly List<CombineInstance>[] groups =
            { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
        private readonly List<Mesh> owned = new List<Mesh>();
        private readonly Texture2D[] textures = new Texture2D[3];
        private readonly Material[] materials = new Material[3];
        private readonly FoodSurface[] surfaces = new FoodSurface[3];

        /// <summary>表面の色柄、加熱特性と透明描画を設定します。テクスチャの所有権を受け取ります。</summary>
        public void SetSurface(int group, Texture2D texture, FoodSurface surface, bool transparent = false)
        {
            ValidateGroup(group);
            if (texture == null || surface == null) throw new ArgumentNullException(nameof(texture));
            if (textures[group] != null) throw new InvalidOperationException("表面は一度だけ設定してください。");
            textures[group] = texture; surfaces[group] = surface;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Litが必要です。");
            Material material = new Material(shader) { name = texture.name + " Material", hideFlags = HideFlags.DontSave };
            materials[group] = material;
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", surface.RawSmoothness);
            material.SetFloat("_Cull", (float)CullMode.Off);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f); material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
        }

        /// <summary>専用の中間メッシュを位置・回転・倍率付きで追加します。共有メッシュは渡さないでください。</summary>
        public void Add(int group, Mesh mesh, Vector3 position, Vector3 scale, Vector3 rotation = default(Vector3))
        {
            ValidateGroup(group);
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (!owned.Contains(mesh)) owned.Add(mesh);
            groups[group].Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, Quaternion.Euler(rotation), scale) });
        }

        /// <summary>結合し、1500三角形と実寸上限を検査して完成モデルへ所有権を移します。</summary>
        public FoodModel Finish(string id)
        {
            List<Mesh> result = new List<Mesh>(); List<Material> mats = new List<Material>();
            List<Texture2D> tex = new List<Texture2D>(); List<FoodSurface> states = new List<FoodSurface>();
            Bounds bounds = new Bounds(); int triangles = 0;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    if (groups[i].Count == 0) continue;
                    if (materials[i] == null) throw new InvalidOperationException("表面を設定してください。");
                    Mesh mesh = new Mesh { name = "Food " + id + " " + i, hideFlags = HideFlags.DontSave };
                    result.Add(mesh); mesh.CombineMeshes(groups[i].ToArray(), true, true);
                    mesh.RecalculateBounds(); triangles += mesh.triangles.Length / 3;
                    if (result.Count == 1) bounds = mesh.bounds; else bounds.Encapsulate(mesh.bounds);
                    mats.Add(materials[i]); tex.Add(textures[i]); states.Add(surfaces[i]);
                }
                float maximum = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (result.Count == 0 || triangles > 1500 || !float.IsFinite(maximum) || maximum < .1f || maximum > .25f)
                    throw new InvalidOperationException($"{id}: 食材予算を超えています tris={triangles}, size={maximum}");
                FoodModel model = new FoodModel(result.ToArray(), mats.ToArray(), tex.ToArray(), states.ToArray(), bounds, triangles);
                for (int i = 0; i < 3; i++)
                    if (groups[i].Count > 0) { materials[i] = null; textures[i] = null; }
                return model;
            }
            catch { foreach (Mesh mesh in result) OwnedObjects.Destroy(mesh); throw; }
        }

        /// <summary>一時部品と、完成モデルへ移譲されなかったリソースを解放します。</summary>
        public void Dispose()
        {
            foreach (Mesh mesh in owned) OwnedObjects.Destroy(mesh); owned.Clear();
            foreach (Texture2D texture in textures) OwnedObjects.Destroy(texture);
            foreach (Material material in materials) OwnedObjects.Destroy(material);
        }

        /// <summary>描画グループの範囲を検査します。</summary>
        private static void ValidateGroup(int group)
        {
            if (group < 0 || group > 2) throw new ArgumentOutOfRangeException(nameof(group));
        }
    }

    /// <summary>カテゴリビルダーと通常69＋希少6IDの共有モデルを管理します。</summary>
    public static class FoodRegistry
    {
        private static readonly Dictionary<string, IFoodBuilder> builders = new Dictionary<string, IFoodBuilder>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FoodModel> models = new Dictionary<string, FoodModel>(StringComparer.Ordinal);
        private static bool initialized;
        private static FoodCacheLifetime lifetime;

        /// <summary>対応IDを登録します。同じIDを異なるビルダーで上書きすることは禁止します。</summary>
        public static void Register(IFoodBuilder builder, params string[] ids)
        {
            if (builder == null || ids == null) throw new ArgumentNullException(nameof(builder));
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("IDが必要です。");
                if (builders.TryGetValue(id, out IFoodBuilder existing) && existing.GetType() != builder.GetType())
                    throw new InvalidOperationException("IDの登録が重複しています: " + id);
                if (!builders.ContainsKey(id) && builders.Count >= 75) throw new InvalidOperationException("登録上限は75IDです。");
                builders[id] = builder;
            }
        }

        /// <summary>対応モデルを遅延生成します。未登録IDは従来描画へ戻します。</summary>
        public static bool TryGet(string id, out FoodModel model)
        {
            EnsureInitialized();
            if (Application.isPlaying && lifetime == null)
            {
                GameObject host = new GameObject("Food Cache Lifetime") { hideFlags = HideFlags.DontSave };
                UnityEngine.Object.DontDestroyOnLoad(host);
                lifetime = host.AddComponent<FoodCacheLifetime>();
            }
            if (models.TryGetValue(id, out model) && model.IsAlive()) return true;
            if (!builders.TryGetValue(id, out IFoodBuilder builder)) { model = null; return false; }
            if (model != null) { model.Dispose(); models.Remove(id); }
            model = builder.Build(id);
            if (model == null) return false;
            models[id] = model; return true;
        }

        /// <summary>利用者が存在しない時だけ、共有キャッシュ全体を解放します。</summary>
        public static void ReleaseAll()
        {
            foreach (FoodModel model in models.Values) model.Dispose(); models.Clear();
        }

        /// <summary>プレイ開始時に古いキャッシュを解放し、終了時の解放を登録します。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            ReleaseAll(); builders.Clear(); initialized = false;
            Application.quitting -= ReleaseAll; Application.quitting += ReleaseAll;
        }

        /// <summary>エディターで直接Buildする場合にも標準の野菜を登録します。</summary>
        private static void EnsureInitialized()
        {
            if (initialized) return;
            FoodVegetables.Register(); FoodRare.Register(); initialized = true;
        }
    }
}
