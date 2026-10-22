using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MalatangStory.Game
{
    /// <summary>薬味を置いた位置。量の評価とは分け、器の半径を1とする座標だけを保存する。</summary>
    [Serializable] public sealed class GarnishStroke
    {
        public string id;
        public float x, z;
        public const int Limit = 64;
        public static List<GarnishStroke> Normalize(List<GarnishStroke> values)
        {
            values ??= new List<GarnishStroke>();
            values.RemoveAll(p => p == null || !Content.TryCondiment(p.id, out _) || !float.IsFinite(p.x) || !float.IsFinite(p.z));
            if (values.Count > Limit) values.RemoveRange(Limit, values.Count - Limit);
            foreach (var point in values)
            {
                double radius = Math.Sqrt((double)point.x * point.x + (double)point.z * point.z);
                if (radius > 1) { point.x = (float)(point.x / radius); point.z = (float)(point.z / radius); }
            }
            return values;
        }
        public static List<GarnishStroke> Copy(List<GarnishStroke> values)
        {
            var copy = new List<GarnishStroke>();
            if (values != null) foreach (var point in values) if (point != null) copy.Add(new GarnishStroke { id = point.id, x = point.x, z = point.z });
            return copy;
        }
        public static void AppendKey(StringBuilder builder, List<GarnishStroke> values)
        {
            if (values == null) return;
            foreach (var point in values)
                if (point != null) builder.Append('|').Append(point.id).Append(':').Append(point.x.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(point.z.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    public sealed partial class RestaurantSimulation
    {
        public bool PaintCondiment(string orderId, string condimentId, float x, float z)
        {
            var order = Find(orderId);
            if (order == null || order.stage != OrderStage.Finishing || !float.IsFinite(x) || !float.IsFinite(z)
                || !Content.TryCondiment(condimentId, out _) || order.garnishStrokes.Count >= GarnishStroke.Limit) return false;
            if (condimentId == Content.ChiliOil ? order.chosenSpice <= 0 : !order.condiments.Contains(condimentId)) return false;
            double radius = Math.Sqrt((double)x * x + (double)z * z);
            order.garnishStrokes.Add(new GarnishStroke { id = condimentId, x = radius > 1 ? (float)(x / radius) : x, z = radius > 1 ? (float)(z / radius) : z });
            return true;
        }
    }
}
