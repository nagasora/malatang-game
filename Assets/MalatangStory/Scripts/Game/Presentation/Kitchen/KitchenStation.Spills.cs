using UnityEngine;
namespace MalatangStory.Game
{
    public sealed partial class KitchenStation
    {
        private Transform counterSpill, cleaningCloth;
        private float wipeSeconds, spillWidth = .18f;
        private string spillSoup;
        private void UpdateCounterSpill(float seconds, bool visible)
        {
            wipeSeconds = Mathf.Max(0, wipeSeconds - seconds);
            bool show = visible && order != null && (order.counterSpillMl > 0 || wipeSeconds > 0);
            if (show && counterSpill == null)
            {
                counterSpill = StoreBuilder.Part("Soup Spill", MeshFactory.Disc(.5f, 24), MaterialLibrary.Transparent(new Color(.7f, .2f, .08f, .65f), .85f), Vector3.zero, Vector3.one, root, null, false).transform;
                var hit = counterSpill.gameObject.AddComponent<BoxCollider>(); hit.center = Vector3.up * .01f; hit.size = new Vector3(1, .06f, 1);
                counterSpill.gameObject.AddComponent<KitchenTarget>().Set(KitchenTargetKind.Spill, 0);
                cleaningCloth = StoreBuilder.Part("Cleaning Cloth", StoreBuilder.RoundedBox(new Vector3(.18f, .015f, .12f), .015f), MaterialLibrary.Textured(TextureFactory.Fabric(32, new Color(.9f, .9f, .76f)), Color.white, .2f, Vector2.one), Vector3.zero, Vector3.one, root, null, false).transform;
            }
            if (counterSpill == null) return;
            counterSpill.gameObject.SetActive(show); cleaningCloth.gameObject.SetActive(show && wipeSeconds > 0);
            if (!show) return;
            if (order.counterSpillMl > 0) spillWidth = .15f + order.counterSpillMl * .001f;
            float width = order.counterSpillMl > 0 ? spillWidth : spillWidth * Mathf.Clamp01(wipeSeconds / .45f);
            counterSpill.localPosition = StoreLayout.FinishBowl + new Vector3(-.5f, .016f, -.2f);
            counterSpill.localScale = new Vector3(width, .05f, width * .7f);
            cleaningCloth.localPosition = counterSpill.localPosition + new Vector3(Mathf.Sin(Time.time * 18) * .12f, .02f, 0);
            if (spillSoup != order.chosenSoup)
            {
                spillSoup = order.chosenSoup; var soup = Content.Soup(spillSoup);
                counterSpill.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Transparent(new Color(soup.R, soup.G, soup.B, .65f), .85f);
            }
        }
        private void WipeCounterNow()
        {
            if (order == null || !Sim.WipeCounter(order.id)) return;
            wipeSeconds = UIKit.ReduceMotion ? 0 : .45f; world.App.Audio.Play(Sfx.Dishes, .18f);
        }
    }
}
