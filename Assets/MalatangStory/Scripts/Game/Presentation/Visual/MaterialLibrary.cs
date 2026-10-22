using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>ビルドに保持したテンプレートから、共有する描画マテリアルを生成します。</summary>
    public static class MaterialLibrary
    {
        /// <summary>生成条件の値を厳密に比較するキャッシュキーです。</summary>
        private readonly struct Key : IEquatable<Key>
        {
            private readonly string kind;
            private readonly Color color;
            private readonly Color emission;
            private readonly float smoothness;
            private readonly float metallic;
            private readonly Texture2D texture;
            private readonly int textureId;
            private readonly Vector2 tiling;
            private readonly bool additive;

            /// <summary>生成条件とテクスチャの安定した識別子を記録します。</summary>
            internal Key(string kind, Color color, Color emission, float smoothness, float metallic, Texture2D texture, Vector2 tiling, bool additive)
            {
                this.kind = kind; this.color = color; this.emission = emission;
                this.smoothness = smoothness; this.metallic = metallic; this.texture = texture;
                this.tiling = tiling; this.additive = additive;
                textureId = texture == null ? 0 : texture.GetInstanceID();
            }

            /// <summary>生成条件が完全に一致するか判定します。</summary>
            public bool Equals(Key other) => kind == other.kind && color.Equals(other.color) && emission.Equals(other.emission)
                && smoothness.Equals(other.smoothness) && metallic.Equals(other.metallic)
                && ReferenceEquals(texture, other.texture) && tiling.Equals(other.tiling) && additive == other.additive;
            /// <summary>同じ型の生成条件を比較します。</summary>
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            /// <summary>生成条件の安定したハッシュ値を返します。</summary>
            public override int GetHashCode() => HashCode.Combine(kind, color, emission, smoothness, metallic, textureId, tiling, additive);
        }

        private static readonly Dictionary<Key, Material> Cache = new Dictionary<Key, Material>();

        /// <summary>不透明な陶器・木・食材向けの共有マテリアルを返します。</summary>
        public static Material Lit(Color color, float smoothness = 0.3f, float metallic = 0f)
            => Get("RestaurantLit", color, Color.black, smoothness, metallic, null, Vector2.one, false);

        /// <summary>アルファブレンドする艶のある共有マテリアルを返します。</summary>
        public static Material Transparent(Color color, float smoothness = 0.6f)
            => Get("RestaurantLitTransparent", color, Color.black, smoothness, 0f, null, Vector2.one, false);

        /// <summary>発光色と強度を持つ共有マテリアルを返します。</summary>
        public static Material Emissive(Color baseColor, Color emission, float intensity = 1f)
        {
            OwnedObjects.ValidateFinite(intensity, nameof(intensity));
            return Get("RestaurantLitEmissive", baseColor, emission * Mathf.Max(0f, intensity), 0.3f, 0f, null, Vector2.one, false);
        }

        /// <summary>湯気や加算式のきらめきに使う共有マテリアルを返します。</summary>
        public static Material Particles(Color tint, Texture2D texture = null, bool additive = false)
            => Get("RestaurantParticles", tint, Color.black, 0f, 0f, texture, Vector2.one, additive);

        /// <summary>照明の影響を受けない共有マテリアルを返します。</summary>
        public static Material Unlit(Color color, Texture2D texture = null)
            => Get("RestaurantUnlit", color, Color.black, 0f, 0f, texture, Vector2.one, false);

        /// <summary>テクスチャと繰り返し倍率を持つ不透明マテリアルを返します。</summary>
        public static Material Textured(Texture2D texture, Color tint, float smoothness = 0.25f, Vector2? tiling = null)
            => Get("RestaurantLit", tint, Color.black, smoothness, 0f, texture, tiling ?? Vector2.one, false);

        /// <summary>このライブラリが所有するマテリアルを全解放します。使用中の描画物を先に破棄してください。</summary>
        public static void ReleaseAll()
        {
            foreach (Material material in Cache.Values)
            {
                if (material == null) continue;
                OwnedObjects.Destroy(material);
            }
            Cache.Clear();
        }

        /// <summary>生成条件に一致する共有素材を取得または構築します。</summary>
        private static Material Get(string kind, Color color, Color emission, float smoothness, float metallic, Texture2D texture, Vector2 tiling, bool additive)
        {
            OwnedObjects.ValidateFinite(smoothness, nameof(smoothness)); OwnedObjects.ValidateFinite(metallic, nameof(metallic));
            OwnedObjects.ValidateFinite(tiling.x, nameof(tiling)); OwnedObjects.ValidateFinite(tiling.y, nameof(tiling));
            OwnedObjects.ValidateColor(color, nameof(color)); OwnedObjects.ValidateColor(emission, nameof(emission));
            smoothness = Mathf.Clamp01(smoothness); metallic = Mathf.Clamp01(metallic);
            Key key = new Key(kind, color, emission, smoothness, metallic, texture, tiling, additive);
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material template = Resources.Load<Material>("Materials/" + kind);
            Material material;
            if (template != null) material = new Material(template);
            else
            {
                string shaderName = kind == "RestaurantParticles" ? "Universal Render Pipeline/Particles/Unlit"
                    : kind == "RestaurantUnlit" ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit";
                Shader shader = Shader.Find(shaderName);
                if (shader == null) throw new InvalidOperationException("描画シェーダーがありません。Resources/Materials/" + kind + " をビルドに含めてください。");
                material = new Material(shader);
            }
            material.name = kind + " (Procedural)";
            material.hideFlags = HideFlags.DontSave;
            SetColor(material, "_BaseColor", color);
            SetFloat(material, "_Smoothness", smoothness); SetFloat(material, "_Metallic", metallic);
            if (texture != null && material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", tiling);
            }
            bool transparent = kind == "RestaurantLitTransparent" || kind == "RestaurantParticles";
            if (transparent) ConfigureTransparent(material, additive);
            if (kind == "RestaurantLitEmissive")
            {
                material.EnableKeyword("_EMISSION"); SetColor(material, "_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            Cache[key] = material;
            return material;
        }

        /// <summary>URP の透明描画設定と加算方式を揃えます。</summary>
        private static void ConfigureTransparent(Material material, bool additive)
        {
            SetFloat(material, "_Surface", 1f); SetFloat(material, "_Blend", additive ? 2f : 0f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(material, "_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloat(material, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, "_ZWrite", 0f); SetFloat(material, "_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
        }

        /// <summary>存在するシェーダープロパティへ数値を設定します。</summary>
        private static void SetFloat(Material material, string property, float value)
        { if (material.HasProperty(property)) material.SetFloat(property, value); }
        /// <summary>存在するシェーダープロパティへ色を設定します。</summary>
        private static void SetColor(Material material, string property, Color value)
        { if (material.HasProperty(property)) material.SetColor(property, value); }
    }
}
