using UnityEngine;
using UnityEngine.UI;
namespace MalatangStory.Game
{
    public sealed class PanelExit : MonoBehaviour
    {
        private CanvasGroup group;
        private RectTransform rect;
        private Vector2 origin;
        private float elapsed, alpha;
        private void Awake()
        {
            foreach (var animation in GetComponentsInChildren<PopIn>()) animation.enabled = false;
            group = TryGetComponent<CanvasGroup>(out var existing) ? existing : gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false; alpha = group.alpha;
            rect = (RectTransform)transform; origin = rect.anchoredPosition;
        }
        private void Update()
        {
            elapsed += Time.unscaledDeltaTime; float t = Mathf.Clamp01(elapsed / .12f);
            group.alpha = alpha * (1 - t); rect.anchoredPosition = origin + Vector2.up * (18 * t);
            if (t >= 1) Destroy(gameObject);
        }
    }
}
