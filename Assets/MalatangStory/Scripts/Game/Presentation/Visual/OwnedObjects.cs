using System;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>
    /// 表示層が実行時に生成して所有する Unity オブジェクト（メッシュ・テクスチャ・マテリアルなど）の解放と、
    /// 生成パラメーターの検証をまとめた共通処理。
    /// </summary>
    internal static class OwnedObjects
    {
        /// <summary>実行中は遅延破棄し、編集時（EditMode テストなど）は即座に破棄する。null は無視する。</summary>
        public static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        /// <summary>NaN と無限大を拒否する。</summary>
        public static void ValidateFinite(float value, string name)
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(name, "有限値を指定してください。");
        }

        /// <summary>色の全成分が有限値であることを検証する。</summary>
        public static void ValidateColor(Color color, string name)
        {
            ValidateFinite(color.r, name); ValidateFinite(color.g, name);
            ValidateFinite(color.b, name); ValidateFinite(color.a, name);
        }
    }
}
