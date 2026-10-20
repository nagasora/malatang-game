using System;
using System.Collections.Generic;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

namespace MalatangStory.Ingredients.Tests
{
    /// <summary>壊れた薬味IDが保存移行を通り抜けず、正常な薬味が保持されることを保証します。</summary>
    public sealed class SaveCondimentRecoveryTests
    {
        /// <summary>注文と有効なアルバムを持つ独立した状態を作ります。</summary>
        private static GameState CreateState()
        {
            RestaurantSimulation simulation = new RestaurantSimulation(IngredientCatalog.Load());
            GameState state = simulation.State;
            state.orders.Clear();
            state.orders.Add(new CustomerOrder { id = "condiment-recovery", stage = OrderStage.Selecting, seat = -1, takeout = true });
            state.album.Add(new AlbumEntry
            {
                day = 1, soup = "mala", score = 90f, quality = 90,
                ingredients = new List<string> { "enoki" }
            });
            return state;
        }

        /// <summary>旧形式でも現形式でも未知・null・空IDだけを除去し、順序と有効な重複を保ちます。</summary>
        [TestCase(7)]
        [TestCase(8)]
        public void MigrationRemovesCorruptedCondimentIdsFromOrdersAndAlbum(int version)
        {
            GameState state = CreateState();
            state.saveVersion = version;
            state.orders[0].condiments = new List<string> { "garlic", null, "unknown-condiment", "", "scallion", "garlic" };
            state.orders[0].requestedCondiments = new List<string> { null, "sesame_paste", "unknown-condiment" };
            state.orders[0].dislikedCondiments = new List<string> { "unknown-condiment", "vinegar", null };
            state.album[0].condiments = new List<string>(state.orders[0].condiments);
            GameState loaded = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(state));

            SaveMigration.Migrate(loaded);

            CollectionAssert.AreEqual(new[] { "garlic", "scallion", "garlic" }, loaded.orders[0].condiments);
            CollectionAssert.AreEqual(new[] { "sesame_paste" }, loaded.orders[0].requestedCondiments);
            CollectionAssert.AreEqual(new[] { "vinegar" }, loaded.orders[0].dislikedCondiments);
            Assert.AreEqual(1, loaded.album.Count);
            CollectionAssert.AreEqual(loaded.orders[0].condiments, loaded.album[0].condiments);
            Assert.DoesNotThrow(() => SaveRepository.Validate(loaded));
            SaveMigration.Migrate(loaded);
            CollectionAssert.AreEqual(new[] { "garlic", "scallion", "garlic" }, loaded.album[0].condiments);
        }

        /// <summary>欠けた薬味リストは空リストとして復元します。</summary>
        [Test]
        public void MigrationInitializesMissingCondimentLists()
        {
            GameState state = CreateState();
            state.orders[0].condiments = null;
            state.orders[0].requestedCondiments = null;
            state.orders[0].dislikedCondiments = null;
            state.album[0].condiments = null;
            SaveMigration.Migrate(state);
            Assert.IsEmpty(state.orders[0].condiments);
            Assert.IsEmpty(state.orders[0].requestedCondiments);
            Assert.IsEmpty(state.orders[0].dislikedCondiments);
            Assert.IsEmpty(state.album[0].condiments);
            Assert.DoesNotThrow(() => SaveRepository.Validate(state));
        }

        /// <summary>移行を経ない不正な注文薬味を新しく保存しないことを保証します。</summary>
        [TestCase("selected")]
        [TestCase("requested")]
        [TestCase("disliked")]
        public void ValidationRejectsUnknownOrderCondiments(string list)
        {
            GameState state = CreateState();
            CustomerOrder order = state.orders[0];
            List<string> target = list == "selected" ? order.condiments
                : list == "requested" ? order.requestedCondiments : order.dislikedCondiments;
            target.Add("unknown-condiment");
            Assert.Throws<InvalidOperationException>(() => SaveRepository.Validate(state));
        }

        /// <summary>常連と名物でも保存時の検証と読み込み時の修復を一貫させます。</summary>
        [TestCase("liked")]
        [TestCase("disliked")]
        [TestCase("signature")]
        public void RegularAndSignatureCondimentsAreValidatedAndRecovered(string list)
        {
            GameState state = CreateState();
            RegularCustomer regular = new RegularCustomer { id = "condiment-regular" };
            SignatureDish signature = new SignatureDish { id = "condiment-signature" };
            state.regulars.Add(regular);
            state.signatures.Add(signature);
            List<string> target = list == "liked" ? regular.likedCondiments
                : list == "disliked" ? regular.dislikedCondiments : signature.condiments;
            target.Add("scallion");
            target.Add(null);
            target.Add("unknown-condiment");
            Assert.Throws<InvalidOperationException>(() => SaveRepository.Validate(state));
            SaveMigration.Migrate(state);
            CollectionAssert.AreEqual(new[] { "scallion" }, target);
            Assert.DoesNotThrow(() => SaveRepository.Validate(state));
        }

        /// <summary>アルバムにも未知の薬味を保存できないことを保証します。</summary>
        [Test]
        public void ValidationRejectsUnknownAlbumCondiments()
        {
            GameState state = CreateState();
            state.album[0].condiments.Add(null);
            Assert.Throws<InvalidOperationException>(() => SaveRepository.Validate(state));
        }
    }
}
