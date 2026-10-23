using System.Collections;
using System.Linq;
using MalatangStory.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace MalatangStory.Tests
{
    public sealed class CharacterMealTests
    {
        [UnityTest] public IEnumerator ChopsticksApproachTheMouthAndSoupAndWaterUseTheLeftHand()
        {
            var rig = CharacterFactory.Create(CharacterAppearance.FromSeed(123), "食事テスト", null);
            try
            {
                rig.SetPose(CharacterPose.Eat); rig.SetMealTime(.2f); yield return new WaitForSeconds(.2f);
                var tip = rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Eating Tip");
                var mouth = rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Mouth");
                float pickingDistance = Vector3.Distance(tip.position, mouth.position);
                rig.SetMealTime(2f); yield return new WaitForSeconds(.3f);
                Assert.Less(Vector3.Distance(tip.position, mouth.position), pickingDistance);
                rig.SetMealTime(3f); yield return null;
                Assert.IsTrue(rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Eating Spoon").gameObject.activeInHierarchy);
                Assert.IsFalse(tip.gameObject.activeInHierarchy);
                rig.SetSpicyMeal(true); yield return null;
                Assert.IsTrue(rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Drinking Water").gameObject.activeInHierarchy);
                rig.SetPose(CharacterPose.Walk); yield return null;
                Assert.IsFalse(rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Drinking Water").gameObject.activeInHierarchy);
            }
            finally { Object.Destroy(rig.gameObject); }
            yield return null;
        }
    }
}
