using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;

namespace MalatangStory.Ingredients.Tests
{
    public sealed class SaveHardeningTests
    {
        private string folder, path;
        [SetUp] public void SetUp() { folder = Path.Combine(Path.GetTempPath(), "malatang-hardening-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); path = Path.Combine(folder, "save.json"); }
        [TearDown] public void TearDown() { Directory.Delete(folder, true); }
        [Serializable] private sealed class Envelope { public int version = GameState.CurrentVersion; public string payload, checksum; }
        private void Write(string payload)
        {
            using (var hash = SHA256.Create())
                File.WriteAllText(path, JsonUtility.ToJson(new Envelope { payload = payload, checksum = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(payload))) }));
        }
        private static IEnumerable<string> DamagedPayloads()
        {
            yield return "null"; yield return ""; yield return "{"; yield return "[]"; yield return "false";
            yield return "{\"saveVersion\":9,\"phase\":999}";
            yield return "{\"saveVersion\":9,\"pots\":999}";
            yield return "{\"saveVersion\":9,\"storageCapacity\":-99}";
            yield return "{\"saveVersion\":9,\"pricePer100gFen\":1}";
            yield return "{\"saveVersion\":9,\"tutorialStep\":999}";
            yield return "{\"saveVersion\":9,\"orders\":[null]}";
            yield return "{\"saveVersion\":9,\"stock\":[null]}";
            yield return "{\"saveVersion\":9,\"staff\":[null]}";
            yield return "{\"saveVersion\":9,\"furniture\":[null]}";
            yield return "{\"saveVersion\":9,\"regulars\":[null]}";
            yield return "{\"saveVersion\":9,\"history\":[null]}";
            yield return "{\"saveVersion\":9,\"orders\":[{\"id\":\"x\",\"stage\":999}]}";
            yield return "{\"saveVersion\":9,\"moneyFen\":9223372036854775807}";
            yield return "{\"saveVersion\":9,\"experience\":2147483647}";
            yield return "{\"saveVersion\":9,\"orders\":\"wrong type\"}";
            yield return "{\"saveVersion\":9,\"day\":{\"wrong\":1}}";
            yield return "{\"saveVersion\":9,\"report\":null}";
            yield return "{\"saveVersion\":9,\"orders\":[{\"id\":\"x\",\"selected\":[null],\"dropTimes\":null}]}";
            yield return "{\"saveVersion\":9,\"regulars\":[{\"id\":\"r\",\"favorites\":null}],\"ingredients\":null}";
        }
        [TestCaseSource(nameof(DamagedPayloads))]
        public void DamagedPayloadNeverEscapesAndPreservesARecoverableState(string payload)
        {
            var repository = new SaveRepository(path);
            repository.Save(new RestaurantSimulation(IngredientCatalog.Load()).State);
            File.Copy(path, path + ".bak");
            Write(payload);
            GameState loaded = null;
            Assert.DoesNotThrow(() => loaded = repository.Load());
            Assert.NotNull(loaded, "正常なバックアップがあるとき新規に戻さない");
            Assert.DoesNotThrow(() => SaveRepository.Validate(loaded));
        }
        [Test] public void TwoCorruptFilesAreQuarantinedBeforeStartingAgain()
        {
            File.WriteAllText(path, "broken"); File.WriteAllText(path + ".bak", "also broken");
            var repository = new SaveRepository(path);
            Assert.IsNull(repository.Load());
            Assert.IsFalse(repository.Exists);
            var files = Directory.GetFiles(folder, "*.corrupt-*.json");
            Assert.AreEqual(2, files.Length);
            CollectionAssert.AreEquivalent(new[] { "broken", "also broken" }, files.Select(File.ReadAllText));
            StringAssert.Contains("退避", repository.LastError);
        }
        [Test] public void OversizedFileUsesBackupWithoutReadingThePayload()
        {
            var repository = new SaveRepository(path);
            repository.Save(new RestaurantSimulation(IngredientCatalog.Load()).State);
            File.Copy(path, path + ".bak");
            using (var stream = File.OpenWrite(path)) stream.SetLength(SaveRepository.MaximumFileBytes + 1);
            Assert.NotNull(repository.Load()); StringAssert.Contains("バックアップ", repository.LastError);
        }
        [Test] public void AbandonedBackupTemporaryDoesNotReplaceTheLastValidBackup()
        {
            var repository = new SaveRepository(path); var state = new RestaurantSimulation(IngredientCatalog.Load()).State;
            repository.Save(state); state.moneyFen = 12345; repository.Save(state);
            File.WriteAllText(path + ".bak.tmp", "incomplete copy"); File.WriteAllText(path, "broken");
            Assert.AreEqual(50000, repository.Load().moneyFen);
        }
        [Test] public void LimitsClampValuesRemoveInvalidFurnitureAndRetainRecentHistory()
        {
            var state = new RestaurantSimulation(IngredientCatalog.Load()).State;
            state.moneyFen = long.MaxValue; state.experience = int.MaxValue;
            state.storeName = "<size=300>店\n名\u200b";
            state.furniture.Add(new PlacedFurniture { uid = 500, defId = "plant", x = int.MaxValue });
            state.history = Enumerable.Range(1, 100).Select(d => new DaySummary { day = d }).ToList();
            state.recentReviews = Enumerable.Repeat(new string('a', 1000), 100).ToList();
            SaveMigration.Migrate(state);
            Assert.AreEqual(SaveLimits.MaximumMoney, state.moneyFen); Assert.AreEqual(SaveLimits.MaximumExperience, state.experience);
            Assert.IsFalse(state.furniture.Any(f => f.uid == 500));
            Assert.AreEqual(DayGrade.HistoryLimit, state.history.Count); Assert.AreEqual(100, state.history.Last().day);
            Assert.AreEqual(20, state.recentReviews.Count); Assert.AreEqual(300, state.recentReviews[0].Length);
            Assert.DoesNotThrow(() => SaveRepository.Validate(state));
        }
        [TestCase("abc\n\r\t\u200bdef", "abcdef")]
        [TestCase("<color=red>店</color>", "color=red店/color")]
        [TestCase("123456789012345😀x", "123456789012345😀")]
        [TestCase("123456789012345e\u0301x", "123456789012345e\u0301")]
        public void PlayerNamesKeepWholeGraphemesAndRemoveControls(string value, string expected)
        {
            Assert.AreEqual(expected, PlayerText.Clean(value));
            var simulation = new RestaurantSimulation(IngredientCatalog.Load()); simulation.RenameStore(value);
            Assert.AreEqual(expected, simulation.State.storeName);
            SaveMigration.Migrate(simulation.State); Assert.AreEqual(expected, simulation.State.storeName);
        }
        [TestCase(-1000000001L, -1000000000L)]
        [TestCase(-1000000000L, -1000000000L)]
        [TestCase(1000000000000L, 1000000000000L)]
        [TestCase(1000000000001L, 1000000000000L)]
        public void MoneyLimitsIncludeTheBoundaryAndClampOutside(long value, long expected)
        {
            var state = new RestaurantSimulation(IngredientCatalog.Load()).State; state.moneyFen = value;
            SaveMigration.Migrate(state); Assert.AreEqual(expected, state.moneyFen);
            Assert.DoesNotThrow(() => SaveRepository.Validate(state));
        }
        [Test] public void InterruptedBackupCopyDoesNotDamageEitherCommittedGeneration()
        {
            var repository = new SaveRepository(path); var state = new RestaurantSimulation(IngredientCatalog.Load()).State;
            repository.Save(state); state.moneyFen = 12345; repository.Save(state);
            byte[] primary = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
            Directory.CreateDirectory(path + ".bak.tmp");
            state.moneyFen = 9999;
            Assert.Catch(() => repository.Save(state));
            CollectionAssert.AreEqual(primary, File.ReadAllBytes(path)); CollectionAssert.AreEqual(backup, File.ReadAllBytes(path + ".bak"));
        }
        [Test] public void SmokeOutputRequiresExistingDirectoryAndRejectsTraversal()
        {
            Assert.IsTrue(SmokeOutputPath.TryResolve(folder, out _));
            Assert.IsFalse(SmokeOutputPath.TryResolve(Path.Combine(folder, "missing"), out _));
            Assert.IsFalse(SmokeOutputPath.TryResolve(Path.Combine(folder, ".."), out _));
        }
    }
}
