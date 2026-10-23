using UnityEngine;
namespace MalatangStory.Game
{
    public sealed partial class CharacterRig
    {
        private Transform mealChopsticks, mealSpoon, mealCup;
        private void ResetMealUtensils() { mealChopsticks = null; mealSpoon = null; mealCup = null; }
        private void CreateMealUtensils()
        {
            var wood = MaterialLibrary.Lit(new Color(.46f, .26f, .12f), .35f);
            var ceramic = MaterialLibrary.Lit(new Color(.91f, .92f, .86f), .65f);
            mealChopsticks = new GameObject("Eating Chopsticks").transform; mealChopsticks.SetParent(rightShoulder, false);
            for (int side = -1; side <= 1; side += 2)
                StoreBuilder.Part("Chopstick", MeshFactory.Cylinder(.005f, .22f), wood, new Vector3(side * .012f, 0, .11f), Vector3.one, mealChopsticks, Quaternion.Euler(90, 0, 0), false);
            var tip = new GameObject("Eating Tip").transform; tip.SetParent(mealChopsticks, false); tip.localPosition = Vector3.forward * .22f;
            mealSpoon = new GameObject("Eating Spoon").transform; mealSpoon.SetParent(leftShoulder, false);
            StoreBuilder.Part("Spoon Handle", MeshFactory.Cylinder(.006f, .12f), ceramic, new Vector3(0, 0, .06f), Vector3.one, mealSpoon, Quaternion.Euler(90, 0, 0), false);
            StoreBuilder.Part("Spoon Head", MeshFactory.Sphere(), ceramic, new Vector3(0, 0, .15f), new Vector3(.045f, .01f, .075f), mealSpoon, null, false);
            mealCup = new GameObject("Drinking Water").transform; mealCup.SetParent(leftShoulder, false);
            StoreBuilder.Part("Glass", MeshFactory.Cylinder(.035f, .07f), MaterialLibrary.Transparent(new Color(.7f, .86f, .95f, .35f), .7f), Vector3.up * .035f, Vector3.one, mealCup, null, false);
            StoreBuilder.Part("Water", MeshFactory.Cylinder(.03f, .003f), MaterialLibrary.Lit(new Color(.6f, .8f, .94f), .8f), Vector3.up * .058f, Vector3.one, mealCup, null, false);
        }
        private void UpdateMealUtensils(float blend)
        {
            bool eating = pose == CharacterPose.Eat;
            if (!eating)
            {
                if (mealChopsticks != null) { SetVisible(mealChopsticks, false); SetVisible(mealSpoon, false); SetVisible(mealCup, false); }
                leftShoulder.localScale = Vector3.Lerp(leftShoulder.localScale, Vector3.one, blend);
                rightShoulder.localScale = Vector3.Lerp(rightShoulder.localScale, Vector3.one, blend); return;
            }
            if (mealChopsticks == null) CreateMealUtensils();
            EatingStage stage = CookingFeel.MealStage(mealSeconds);
            bool sip = stage == EatingStage.Sip;
            float lift = stage == EatingStage.Pick ? 0 : stage == EatingStage.Lift ? Mathf.Clamp01((mealSeconds % 3.5f - .8f) / .7f) : 1;
            Vector3 bowlPoint = body.TransformPoint(new Vector3(.03f, -.04f, .48f));
            Vector3 mouthPoint = mouth.position;
            Vector3 rightTarget = Vector3.Lerp(new Vector3(.13f, -.01f, .36f), new Vector3(.12f, .29f, .19f), lift);
            if (sip) rightTarget = new Vector3(.24f, .04f, .24f);
            AimMealArm(rightShoulder, rightTarget, blend);
            AimMealArm(leftShoulder, sip ? new Vector3(-.06f, .29f, .18f) : new Vector3(-.18f, .01f, .32f), blend);
            SetVisible(mealChopsticks, !sip); SetVisible(mealSpoon, sip && !spicyMeal); SetVisible(mealCup, sip && spicyMeal);
            PointUtensil(mealChopsticks, rightShoulder, Vector3.Lerp(bowlPoint, mouthPoint, lift));
            PointUtensil(mealSpoon, leftShoulder, mouthPoint);
            mealCup.position = leftShoulder.TransformPoint(0, -.29f, 0); mealCup.rotation = body.rotation * Quaternion.Euler(-25, 0, 0);
        }
        private static void AimMealArm(Transform arm, Vector3 target, float blend)
        {
            Vector3 direction = target - arm.localPosition;
            arm.localRotation = Quaternion.Slerp(arm.localRotation, Quaternion.FromToRotation(Vector3.down, direction), blend);
            arm.localScale = Vector3.Lerp(arm.localScale, new Vector3(1, Mathf.Clamp(direction.magnitude / .275f, .8f, 1.8f), 1), blend);
        }
        private static void PointUtensil(Transform utensil, Transform arm, Vector3 target)
        {
            utensil.position = arm.TransformPoint(0, -.29f, 0);
            Vector3 direction = target - utensil.position;
            if (direction.sqrMagnitude > .0001f) utensil.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }
}
