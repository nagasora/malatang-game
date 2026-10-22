using System.Collections.Generic;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>通知と会話をセーフエリア内に収め、既存パネルとの重なりを避ける。</summary>
    public static class OverlayLayout
    {
        public static Vector2 Clamp(Rect area, Vector2 size, Vector2 pivot, Vector2 position)
        {
            return new Vector2(Mathf.Clamp(position.x, area.xMin + size.x * pivot.x + 8, area.xMax - size.x * (1 - pivot.x) - 8),
                Mathf.Clamp(position.y, area.yMin + size.y * pivot.y + 150, area.yMax - size.y * (1 - pivot.y) - 190));
        }

        public static bool TryPlace(Rect area, Vector2 size, Vector2 pivot, Vector2 desired, IList<Rect> obstacles, out Vector2 position)
        {
            position = Clamp(area, size, pivot, desired);
            if (Free(position, size, pivot, obstacles)) return true;
            float closest = float.MaxValue;
            bool found = false;
            for (int index = 0; index < obstacles.Count; index++)
            {
                Rect obstacle = obstacles[index];
                for (int side = 0; side < 4; side++)
                {
                    Vector2 candidate = desired;
                    if (side == 0) candidate.y = obstacle.yMax + size.y * pivot.y;
                    if (side == 1) candidate.y = obstacle.yMin - size.y * (1 - pivot.y);
                    if (side == 2) candidate.x = obstacle.xMax + size.x * pivot.x;
                    if (side == 3) candidate.x = obstacle.xMin - size.x * (1 - pivot.x);
                    candidate = Clamp(area, size, pivot, candidate);
                    float distance = (candidate - desired).sqrMagnitude;
                    if (distance >= closest || !Free(candidate, size, pivot, obstacles)) continue;
                    position = candidate; closest = distance; found = true;
                }
            }
            return found;
        }

        private static bool Free(Vector2 position, Vector2 size, Vector2 pivot, IList<Rect> obstacles)
        {
            Rect rect = new Rect(position - Vector2.Scale(size, pivot), size);
            for (int index = 0; index < obstacles.Count; index++) if (rect.Overlaps(obstacles[index])) return false;
            return true;
        }
    }
}
