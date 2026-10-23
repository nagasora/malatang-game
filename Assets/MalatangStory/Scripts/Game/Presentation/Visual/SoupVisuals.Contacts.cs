using System.Collections.Generic;
using UnityEngine;
namespace MalatangStory.Game
{
    public static partial class SoupVisuals
    {
        public static void AddContactOil(Transform surface, IReadOnlyList<Transform> pieces)
        {
            var material = MaterialLibrary.Transparent(new Color(.85f, .47f, .13f, .27f), .85f);
            var mesh = MeshFactory.Torus(.5f, .013f, 20, 4);
            for (int i = 0; i < pieces.Count; i++)
            {
                var point = surface.InverseTransformPoint(pieces[i].position);
                if (new Vector2(point.x, point.z).magnitude > .39f) continue;
                point.y = .003f + i * .0001f;
                float size = .07f + i % 3 * .025f;
                StoreBuilder.Part("Ingredient Oil Ring", mesh, material, point, new Vector3(size, .35f, size * .8f), surface, null, false);
            }
        }
    }
}
