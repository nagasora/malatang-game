using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalatangStory.Game
{
    /// <summary>識別に使う野菜の葉形です。</summary>
    public enum FoodLeafShape { Arrow, Rounded, Lobed, Cut }

    /// <summary>食材専用の低ポリゴン部品を生成します。返却メッシュは呼び出し元が所有します。</summary>
    public static class FoodKit
    {
        /// <summary>経緯線UVと解析的な滑らか法線を持つ、ノイズで歪んだ単位楕円体を生成します。</summary>
        public static Mesh Ellipsoid(int seed = 0, float noise = .04f, int segments = 10, int rings = 6)
        {
            if (segments < 3 || segments > 64 || rings < 2 || rings > 32 || !Finite(noise) || noise < 0f || noise > .5f)
                throw new ArgumentOutOfRangeException(nameof(segments), "球の分割数または歪み量が不正です。");
            List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>();
            List<int> t = new List<int>(); List<Vector3> normals = new List<Vector3>();
            for (int y = 0; y <= rings; y++)
                for (int x = 0; x <= segments; x++)
                {
                    float a = x * Mathf.PI * 2f / segments; float b = y * Mathf.PI / rings;
                    Vector3 direction = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a));
                    float n = Mathf.Clamp01(Mathf.PerlinNoise(direction.x * 3f + seed * .13f, direction.z * 3f + direction.y * 2f + seed * .37f));
                    v.Add(direction * (.5f + noise * (n - .5f))); normals.Add(direction);
                    uv.Add(new Vector2((float)x / segments, 1f - (float)y / rings));
                    if (x == segments || y == rings) continue;
                    int i = y * (segments + 1) + x;
                    if (y != 0) Triangle(t, i, i + 1, i + segments + 1);
                    if (y != rings - 1) Triangle(t, i + 1, i + segments + 2, i + segments + 1);
                }
            Mesh mesh = Finish("Food Ellipsoid", v, t, uv); mesh.SetNormals(normals); mesh.RecalculateTangents(); return mesh;
        }

        /// <summary>半径と高さの断面をY軸回転します。硬い断面には別部品を使ってください。</summary>
        public static Mesh Lathe(Vector2[] profile, int segments = 10)
        {
            if (profile == null || profile.Length < 2 || segments < 3) throw new ArgumentException("断面と分割数が不正です。");
            List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
            List<Vector3> normals = new List<Vector3>();
            for (int y = 0; y < profile.Length; y++)
            {
                Vector2 slope = profile[Mathf.Min(y + 1, profile.Length - 1)] - profile[Mathf.Max(0, y - 1)];
                for (int x = 0; x <= segments; x++)
                {
                    float a = x * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Cos(a) * profile[y].x, profile[y].y, Mathf.Sin(a) * profile[y].x));
                    uv.Add(new Vector2((float)x / segments, (float)y / (profile.Length - 1)));
                    normals.Add(new Vector3(slope.y * Mathf.Cos(a), -slope.x, slope.y * Mathf.Sin(a)).normalized);
                    if (y == profile.Length - 1 || x == segments) continue;
                    int i = y * (segments + 1) + x;
                    Triangle(t, i, i + segments + 1, i + 1); Triangle(t, i + 1, i + segments + 1, i + segments + 2);
                }
            }
            Mesh mesh = Finish("Food Lathe", v, t, uv); mesh.SetNormals(normals); mesh.RecalculateTangents(); return mesh;
        }

        /// <summary>中心線に沿うチューブを生成します。断面フレームを平行輸送し端面を硬い法線で閉じます。</summary>
        public static Mesh Tube(Vector3[] path, float startRadius, float endRadius, int sides = 6)
        {
            if (path == null || path.Length < 2 || sides < 3 || startRadius <= 0f || endRadius <= 0f)
                throw new ArgumentException("チューブの中心線・半径が不正です。");
            List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
            List<Vector3> normals = new List<Vector3>(); Vector3 previous = Vector3.right;
            float length = 0f;
            for (int i = 1; i < path.Length; i++) length += Vector3.Distance(path[i - 1], path[i]);
            if (length < .000001f) throw new ArgumentException("中心線に長さが必要です。");
            float distance = 0f;
            for (int y = 0; y < path.Length; y++)
            {
                Vector3 tangent = (path[Mathf.Min(y + 1, path.Length - 1)] - path[Mathf.Max(0, y - 1)]).normalized;
                Vector3 axis = Vector3.ProjectOnPlane(previous, tangent).normalized;
                if (axis.sqrMagnitude < .01f) axis = Vector3.Cross(tangent, Vector3.forward).normalized;
                if (axis.sqrMagnitude < .01f) axis = Vector3.Cross(tangent, Vector3.up).normalized;
                Vector3 cross = Vector3.Cross(tangent, axis); previous = axis;
                if (y > 0) distance += Vector3.Distance(path[y - 1], path[y]);
                for (int x = 0; x <= sides; x++)
                {
                    float angle = x * Mathf.PI * 2f / sides;
                    Vector3 normal = axis * Mathf.Cos(angle) + cross * Mathf.Sin(angle);
                    v.Add(path[y] + normal * Mathf.Lerp(startRadius, endRadius, distance / length)); normals.Add(normal);
                    uv.Add(new Vector2((float)x / sides, distance / length));
                    if (y == path.Length - 1 || x == sides) continue;
                    int index = y * (sides + 1) + x;
                    Triangle(t, index, index + 1, index + sides + 1); Triangle(t, index + 1, index + sides + 2, index + sides + 1);
                }
            }
            for (int end = 0; end < 2; end++)
            {
                int row = end == 0 ? 0 : path.Length - 1; int center = v.Count;
                Vector3 normal = (end == 0 ? path[0] - path[1] : path[row] - path[row - 1]).normalized;
                v.Add(path[row]); normals.Add(normal); uv.Add(new Vector2(.5f, .5f));
                for (int x = 0; x <= sides; x++)
                {
                    v.Add(v[row * (sides + 1) + x]); normals.Add(normal);
                    float angle = x * Mathf.PI * 2f / sides; uv.Add(new Vector2(.5f + .5f * Mathf.Cos(angle), .5f + .5f * Mathf.Sin(angle)));
                    if (x == sides) continue;
                    if (end == 0) Triangle(t, center, center + x + 2, center + x + 1);
                    else Triangle(t, center, center + x + 1, center + x + 2);
                }
            }
            Mesh mesh = Finish("Food Tube", v, t, uv); mesh.SetNormals(normals); mesh.RecalculateTangents(); return mesh;
        }

        /// <summary>XZ面の葉身を生成します。Vは根元から先端、U=.5が中央葉脈です。</summary>
        public static Mesh Leaf(float width, float length, float curl = .2f, float serration = .1f, int rows = 8, int columns = 4)
        {
            ValidateGrid(width, length, rows, columns);
            List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    float s = (float)y / rows; float u = (float)x / columns; float across = 2f * u - 1f;
                    float sine = y == 0 || y == rows ? 0f : Mathf.Max(0f, Mathf.Sin(s * Mathf.PI));
                    float envelope = .08f + .92f * Mathf.Pow(sine, .7f);
                    float edge = 1f + serration * Mathf.Cos(s * Mathf.PI * rows);
                    float lift = curl * length * (s * s + .25f * across * across * Mathf.Sin(s * Mathf.PI));
                    lift += length * .025f * Mathf.Cos(s * 28f + Mathf.Abs(across) * 8f) * Mathf.Abs(across);
                    v.Add(new Vector3(across * width * .5f * envelope * edge, lift, s * length)); uv.Add(new Vector2(u, s));
                    if (y == rows || x == columns) continue;
                    int i = y * (columns + 1) + x;
                    Triangle(t, i, i + columns + 1, i + 1); Triangle(t, i + 1, i + columns + 1, i + columns + 2);
                }
            return Finish("Food Leaf", v, t, uv);
        }

        /// <summary>波打つしわのある矩形シートをXZ面に生成します。</summary>
        public static Mesh Sheet(float width, float length, float folds = 3f, float amplitude = .01f, int rows = 8, int columns = 6)
        {
            ValidateGrid(width, length, rows, columns);
            List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    float u = (float)x / columns; float s = (float)y / rows;
                    float wave = amplitude * (Mathf.Sin(u * folds * Mathf.PI * 2f + s * 3f) + .35f * Mathf.Sin(s * 17f));
                    v.Add(new Vector3((u - .5f) * width, wave, (s - .5f) * length)); uv.Add(new Vector2(u, s));
                    if (y == rows || x == columns) continue;
                    int i = y * (columns + 1) + x;
                    Triangle(t, i, i + columns + 1, i + 1); Triangle(t, i + 1, i + columns + 1, i + columns + 2);
                }
            return Finish("Food Sheet", v, t, uv);
        }

        /// <summary>穴付き輪切りを生成します。表裏と穴壁を分離して切断面を硬い法線にします。</summary>
        public static Mesh PerforatedSlice(float radius, float thickness, int holes = 7, int resolution = 14)
        {
            if (radius <= 0f || thickness <= 0f || holes < 1 || resolution < 4) throw new ArgumentException("輪切り寸法が不正です。");
            bool[,] cells = new bool[resolution, resolution]; float step = radius * 2f / resolution;
            for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
            {
                Vector2 p = new Vector2(-radius + (x + .5f) * step, -radius + (z + .5f) * step);
                bool solid = p.magnitude < radius - step * .2f && p.magnitude > radius * .13f;
                for (int h = 0; h < holes; h++)
                {
                    float a = h * Mathf.PI * 2f / holes;
                    Vector2 center = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * .60f;
                    if (Vector2.Distance(p, center) < radius * .23f) solid = false;
                }
                cells[x, z] = solid;
            }
            List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
            for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
            {
                if (!cells[x, z]) continue;
                float left = -radius + x * step; float back = -radius + z * step;
                Vector3 a = new Vector3(left, thickness * .5f, back); Vector3 b = a + Vector3.forward * step;
                Vector3 c = b + Vector3.right * step; Vector3 d = a + Vector3.right * step;
                Quad(v, t, uv, a, b, c, d, radius);
                Vector3 down = Vector3.down * thickness;
                Quad(v, t, uv, d + down, c + down, b + down, a + down, radius);
                if (x == 0 || !cells[x - 1, z]) Quad(v, t, uv, b, a, a + down, b + down, radius, true);
                if (x == resolution - 1 || !cells[x + 1, z]) Quad(v, t, uv, d, c, c + down, d + down, radius, true);
                if (z == 0 || !cells[x, z - 1]) Quad(v, t, uv, a, d, d + down, a + down, radius, true);
                if (z == resolution - 1 || !cells[x, z + 1]) Quad(v, t, uv, c, b, b + down, c + down, radius, true);
            }
            return Finish("Food Perforated Slice", v, t, uv);
        }

        /// <summary>円い外周、中央孔と放射状の楕円孔を持つ輪切りを作ります。格子を使わず孔の輪郭を直接接続します。</summary>
        public static Mesh RadialPerforatedSlice(float radius, float thickness, int holes = 8, int resolution = 14)
        {
            if (!Finite(radius) || !Finite(thickness) || radius <= 0f || thickness <= 0f || holes < 7 || holes > 9 || resolution < 8 || resolution > 20)
                throw new ArgumentOutOfRangeException(nameof(radius), "輪切りの寸法または孔の分割数が不正です。");
            List<Vector3> vertices = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> triangles = new List<int>();
            float halfSector = Mathf.PI / holes;
            float centerRadius = radius * .56f; float centralHole = radius * .17f;
            for (int hole = 0; hole < holes; hole++)
            {
                float angle = hole * Mathf.PI * 2f / holes;
                List<float> directions = new List<float>();
                for (int i = 0; i < resolution; i++) directions.Add(i * Mathf.PI * 2f / resolution);
                foreach (float boundaryRadius in new float[] { radius, centralHole })
                    foreach (float sign in new float[] { -1f, 1f })
                    {
                        Vector2 corner = new Vector2(boundaryRadius * Mathf.Cos(halfSector) - centerRadius, sign * boundaryRadius * Mathf.Sin(halfSector));
                        float direction = Mathf.Atan2(corner.y / .155f, corner.x / .255f);
                        directions.Add(Mathf.Repeat(direction, Mathf.PI * 2f));
                    }
                directions.Sort();
                List<Vector3> inner = new List<Vector3>(); List<Vector3> outer = new List<Vector3>();
                float previous = -1f;
                foreach (float direction in directions)
                {
                    if (Mathf.Abs(direction - previous) < .0001f) continue;
                    previous = direction;
                    Vector2 ellipse = new Vector2(radius * .255f * Mathf.Cos(direction), radius * .155f * Mathf.Sin(direction));
                    float extent = SectorExtent(centerRadius, ellipse, radius, centralHole, halfSector);
                    inner.Add(RadialPoint(new Vector2(centerRadius, 0f) + ellipse, angle, thickness * .5f));
                    outer.Add(RadialPoint(new Vector2(centerRadius, 0f) + ellipse * extent, angle, thickness * .5f));
                }
                Vector3 down = Vector3.down * thickness;
                for (int i = 0; i < inner.Count; i++)
                {
                    int next = (i + 1) % inner.Count;
                    Quad(vertices, triangles, uv, inner[i], inner[next], outer[next], outer[i], radius);
                    Quad(vertices, triangles, uv, outer[i] + down, outer[next] + down, inner[next] + down, inner[i] + down, radius);
                    Quad(vertices, triangles, uv, inner[next], inner[i], inner[i] + down, inner[next] + down, radius, true);
                    float r0 = new Vector2(outer[i].x, outer[i].z).magnitude;
                    float r1 = new Vector2(outer[next].x, outer[next].z).magnitude;
                    bool exterior = Mathf.Abs(r0 - radius) < radius * .0001f && Mathf.Abs(r1 - radius) < radius * .0001f;
                    bool center = Mathf.Abs(r0 - centralHole) < radius * .0001f && Mathf.Abs(r1 - centralHole) < radius * .0001f;
                    if (exterior || center)
                        Quad(vertices, triangles, uv, outer[i], outer[next], outer[next] + down, outer[i] + down, radius, true);
                }
            }
            return Finish("Food Radial Slice", vertices, triangles, uv);
        }

        /// <summary>楕円孔から出る半直線と、扇形の円弧・直線境界との最初の交点を求めます。</summary>
        private static float SectorExtent(float center, Vector2 direction, float outer, float inner, float halfSector)
        {
            float a = direction.sqrMagnitude; float b = 2f * center * direction.x;
            float extent = (-b + Mathf.Sqrt(Mathf.Max(0f, b * b - 4f * a * (center * center - outer * outer)))) / (2f * a);
            float discriminant = b * b - 4f * a * (center * center - inner * inner);
            if (discriminant >= 0f)
            {
                float hit = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
                if (hit > 0f) extent = Mathf.Min(extent, hit);
            }
            float tangent = Mathf.Tan(halfSector);
            foreach (float sign in new float[] { -1f, 1f })
            {
                float denominator = sign * direction.y - direction.x * tangent;
                if (denominator > 0f) extent = Mathf.Min(extent, center * tangent / denominator);
            }
            return extent;
        }

        /// <summary>局所的な放射・接線座標をXZ平面へ回します。</summary>
        private static Vector3 RadialPoint(Vector2 point, float angle, float height)
        {
            return new Vector3(point.x * Mathf.Cos(angle) - point.y * Mathf.Sin(angle), height,
                point.x * Mathf.Sin(angle) + point.y * Mathf.Cos(angle));
        }

        /// <summary>矢じり・丸葉・深い裂け葉・ざく切りの葉を、既存Leafの契約と独立して生成します。</summary>
        public static Mesh VegetableLeaf(float width, float length, FoodLeafShape shape, float curl = .2f, float serration = .1f, int rows = 10, int columns = 4)
        {
            ValidateGrid(width, length, rows, columns);
            List<Vector3> vertices = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> triangles = new List<int>();
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                float s = (float)y / rows; float u = (float)x / columns; float across = u * 2f - 1f;
                float sine = y == 0 || y == rows ? 0f : Mathf.Max(0f, Mathf.Sin(s * Mathf.PI));
                float envelope;
                switch (shape)
                {
                    case FoodLeafShape.Rounded: envelope = .06f + .94f * Mathf.Pow(sine, .38f); break;
                    case FoodLeafShape.Arrow:
                        envelope = .035f + .83f * Mathf.Pow(sine, 1.05f) + .27f * Mathf.Exp(-Mathf.Pow((s - .15f) * 9f, 2f)); break;
                    case FoodLeafShape.Cut: envelope = .68f + .32f * Mathf.Sin(s * Mathf.PI * .85f); break;
                    default: envelope = .08f + .92f * Mathf.Pow(sine, .6f); break;
                }
                float edge = 1f + serration * Mathf.Cos(s * Mathf.PI * rows);
                float height = curl * length * (s * s + .28f * across * across * sine);
                height += length * .035f * Mathf.Sin(s * 25f + Mathf.Abs(across) * 11f) * Mathf.Abs(across);
                float z = s * length;
                if (shape == FoodLeafShape.Arrow) z -= length * .10f * across * across * Mathf.Exp(-Mathf.Pow((s - .15f) * 9f, 2f));
                vertices.Add(new Vector3(across * width * .5f * envelope * edge, height, z)); uv.Add(new Vector2(u, s));
                if (y == rows || x == columns) continue;
                int i = y * (columns + 1) + x;
                Triangle(triangles, i, i + columns + 1, i + 1); Triangle(triangles, i + 1, i + columns + 1, i + columns + 2);
            }
            return Finish("Food " + shape + " Leaf", vertices, triangles, uv);
        }

        /// <summary>切り口に平面UVを付けた四角形を追加します。</summary>
        private static void Quad(List<Vector3> v, List<int> t, List<Vector2> uv, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float radius, bool wall = false)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            if (wall)
            {
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.one); uv.Add(Vector2.up);
            }
            else
                foreach (Vector3 point in new Vector3[] { a, b, c, d }) uv.Add(new Vector2(point.x / (2f * radius) + .5f, point.z / (2f * radius) + .5f));
            Triangle(t, i, i + 1, i + 2); Triangle(t, i, i + 2, i + 3);
        }

        /// <summary>三角形の頂点番号を登録します。</summary>
        private static void Triangle(List<int> triangles, int a, int b, int c)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }

        /// <summary>UV、法線、接線と境界を確定します。</summary>
        private static Mesh Finish(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uv)
        {
            foreach (Vector3 point in vertices)
                if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                    throw new ArgumentException("メッシュの頂点には有限値が必要です。");
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            try
            {
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
            }
            catch { Release(mesh); throw; }
        }

        /// <summary>面の寸法と分割数を検査し、空面と過大な生成を防ぎます。</summary>
        private static void ValidateGrid(float width, float length, int rows, int columns)
        {
            if (!Finite(width) || !Finite(length) || width <= 0f || length <= 0f || rows < 1 || columns < 2 || rows > 32 || columns > 32)
                throw new ArgumentOutOfRangeException(nameof(width), "面の寸法または分割数が不正です。");
        }

        /// <summary>NaNと無限大を除外します。</summary>
        internal static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>プレイ中はフレーム終端、それ以外は直ちに所有物を破棄します。</summary>
        internal static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
