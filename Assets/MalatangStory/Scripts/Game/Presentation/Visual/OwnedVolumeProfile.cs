using UnityEngine;
using UnityEngine.Rendering;
namespace MalatangStory.Game
{
    public sealed class OwnedVolumeProfile : MonoBehaviour
    {
        public VolumeProfile Profile;
        private void OnDestroy()
        {
            if (Profile == null) return;
            foreach (var component in Profile.components) if (component != null) OwnedObjects.Destroy(component);
            Profile.components.Clear(); OwnedObjects.Destroy(Profile); Profile = null;
        }
    }
}
