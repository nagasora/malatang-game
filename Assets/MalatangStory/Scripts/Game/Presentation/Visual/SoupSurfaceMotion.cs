using UnityEngine;

/// <summary>液面の描画状態と、割り当てを伴わない小さな対流表現を管理します。</summary>
namespace MalatangStory.Game
{
    /// <summary>共有素材を変更せず、液面ごとのUVと緩い傾きを更新します。</summary>
    [DisallowMultipleComponent]
    public sealed class SoupSurfaceMotion : MonoBehaviour
    {
        private static readonly int BaseMapTransform = Shader.PropertyToID("_BaseMap_ST");
        private Renderer surfaceRenderer;
        private Renderer meniscusRenderer;
        private ParticleSystem steam;
        private MaterialPropertyBlock properties;
        private string soupId;
        private int spice = -1;
        private bool animated;
        private float phase;

        /// <summary>参照とプロパティブロックを生成時に一度だけ準備します。</summary>
        internal void Initialize(SoupAppearanceParameters appearance, bool animate)
        {
            surfaceRenderer = GetComponent<Renderer>();
            Transform rim = transform.Find("Meniscus");
            meniscusRenderer = rim == null ? null : rim.GetComponent<Renderer>();
            properties = new MaterialPropertyBlock(); animated = animate;
            phase = (CustomerDirector.StableHash(appearance.SoupId) & 1023) * .013f;
            Configure(appearance);
        }

        /// <summary>生成済みの湯気を保持し、以後のスープ変更に追従させます。</summary>
        internal void SetSteam(ParticleSystem vapor) => steam = vapor;

        /// <summary>同じ注文状態なら純粋パラメータの再計算も行わず戻ります。</summary>
        internal void Configure(string id, int level)
        {
            int normalized = Mathf.Clamp(level, 0, 5);
            if (soupId == id && spice == normalized) return;
            Configure(SoupAppearance.ForSoup(id, normalized));
        }

        /// <summary>種類または辛さが変わったときのみ共有素材を取得します。</summary>
        internal void Configure(SoupAppearanceParameters appearance)
        {
            if (soupId == appearance.SoupId && spice == appearance.Spice) return;
            soupId = appearance.SoupId; spice = appearance.Spice;
            surfaceRenderer.sharedMaterial = SoupVisuals.SurfaceMaterial(appearance);
            if (steam != null) EffectsFactory.SetIntensity(steam, appearance.SteamIntensity);
            if (meniscusRenderer != null) meniscusRenderer.sharedMaterial = SoupVisuals.MeniscusMaterial(appearance);
            SoupVisuals.ConfigureSeasoning(transform, appearance);
        }

        /// <summary>固定振幅の微小な液面対流を、配列や素材を生成せず更新します。</summary>
        private void LateUpdate()
        {
            if (!animated || surfaceRenderer == null) return;
            float time = Time.time * .42f + phase;
            float a = Mathf.Sin(time), b = Mathf.Cos(time * .73f);
            properties.SetVector(BaseMapTransform, new Vector4(1f, 1f, a * .0025f, b * .0025f));
            surfaceRenderer.SetPropertyBlock(properties);
            transform.localRotation = Quaternion.Euler(a * .12f, 0f, b * .12f);
        }
    }
}
