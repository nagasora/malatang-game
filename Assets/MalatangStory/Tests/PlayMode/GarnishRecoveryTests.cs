using System.Collections;
using System.Collections.Generic;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MalatangStory.Tests
{
    /// <summary>保存移行を経ない未知の薬味IDでも、描画が例外を出さないことを保証します。</summary>
    public sealed class GarnishRecoveryTests
    {
        private GameObject root;

        /// <summary>描画の所有者を先に停止してから共有資源を解放します。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null)
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
            yield return null;
            MaterialLibrary.ReleaseAll();
        }

        /// <summary>null・空・未知IDを無視し、正常な薬味と同じ結合形状を生成します。</summary>
        [Test]
        public void UnknownCondimentIdsDoNotChangeValidGarnishGeometry()
        {
            root = new GameObject("Garnish Recovery Test");
            Transform expected = SoupVisuals.BuildGarnishes(root.transform, new List<string> { "garlic", "scallion" }, 0, 73);
            Transform actual = null;
            Assert.DoesNotThrow(() => actual = SoupVisuals.BuildGarnishes(root.transform,
                new List<string> { null, "garlic", "unknown-condiment", "", "scallion" }, 0, 73));
            MeshFilter[] expectedMeshes = expected.GetComponentsInChildren<MeshFilter>();
            MeshFilter[] actualMeshes = actual.GetComponentsInChildren<MeshFilter>();
            Assert.AreEqual(expectedMeshes.Length, actualMeshes.Length);
            for (int index = 0; index < expectedMeshes.Length; index++)
            {
                Assert.AreEqual(expectedMeshes[index].sharedMesh.vertexCount, actualMeshes[index].sharedMesh.vertexCount);
                Assert.AreEqual(expectedMeshes[index].sharedMesh.bounds, actualMeshes[index].sharedMesh.bounds);
            }
        }

        /// <summary>有効な薬味が一つもなくても空の描画ルートとして扱えます。</summary>
        [Test]
        public void OnlyUnknownCondimentsProduceNoGarnishRenderers()
        {
            root = new GameObject("Unknown Garnish Test");
            Transform actual = null;
            Assert.DoesNotThrow(() => actual = SoupVisuals.BuildGarnishes(root.transform,
                new List<string> { null, "", "unknown-condiment" }, 0, 73));
            Assert.IsEmpty(actual.GetComponentsInChildren<MeshRenderer>());
        }
    }
}
