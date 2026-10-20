using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>プレイ終了やアプリ終了時に、共有食材リソースを解放します。</summary>
    [DisallowMultipleComponent]
    public sealed class FoodCacheLifetime : MonoBehaviour
    {
        /// <summary>シーン間で維持したキャッシュのネイティブリソースを解放します。</summary>
        private void OnDestroy()
        {
            FoodRegistry.ReleaseAll();
        }
    }
}
