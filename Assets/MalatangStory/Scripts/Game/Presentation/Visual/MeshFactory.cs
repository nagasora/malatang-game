/// <summary>調理器具と食材の表示に使用する手続きメッシュ生成機能を提供します。</summary>
namespace MalatangStory.Game
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>
    /// ローカル座標のメッシュを構造値でキャッシュして生成します。
    /// Unityのメインスレッドから呼び出してください。返却メッシュは共有されるため変更・破棄せず、
    /// 編集する場合は複製してください。キャッシュはReleaseAllまで全生成メッシュを保持します。
    /// 回転体の軸はY軸、平面の基準はXZ面です。
    /// </summary>
    public static class MeshFactory
    {
        private const int MaximumSegments = 512;
        private const int MaximumVertices = 1000000;
        private const float FullTurn = Mathf.PI * 2f;
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();
        private static readonly Dictionary<Mesh, int> StructureIds = new Dictionary<Mesh, int>();
        private static int nextStructureId;

        /// <summary>
        /// キャッシュが所有する全メッシュを重複なく破棄します。
        /// 使用中の描画オブジェクトを先に破棄または切り離してから、メインスレッドで呼び出してください。
        /// プレイ中のネイティブオブジェクト破棄はフレーム終端まで遅延します。
        /// </summary>
        public static void ReleaseAll()
        {
            HashSet<Mesh> meshes = new HashSet<Mesh>(StructureIds.Keys);
            Cache.Clear(); StructureIds.Clear(); nextStructureId = 0;
            foreach (Mesh mesh in meshes)
            {
                if (mesh == null) continue;
                OwnedObjects.Destroy(mesh);
            }
        }

        /// <summary>
        /// (半径, 高さ)の連続断面をY軸の周りに回転します。断面の向きは自動補正されます。
        /// 端点を半径0にすると閉じ、開いた端点には自動で蓋を付けません。
        /// 断面の各線分境界は硬い法線、周方向は滑らかな法線とUVの継ぎ目を持ちます。
        /// </summary>
        public static Mesh Lathe(Vector2[] profile, int segments)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            ValidateSegments(segments, 3, nameof(segments));
            if (profile.Length < 2) throw new ArgumentException("断面には2点以上必要です。", nameof(profile));
            EnsureBudget((long)(profile.Length - 1) * (segments + 1) * 2);
            Vector2[] points = (Vector2[])profile.Clone();
            bool hasRadius = false;
            for (int i = 0; i < points.Length; i++)
            {
                OwnedObjects.ValidateFinite(points[i].x, nameof(profile));
                OwnedObjects.ValidateFinite(points[i].y, nameof(profile));
                if (points[i].x < 0f) throw new ArgumentOutOfRangeException(nameof(profile), "半径は0以上にしてください。");
                hasRadius |= points[i].x > 0f;
                if (i > 0 && Exact(points[i], points[i - 1]))
                    throw new ArgumentException("断面の連続点は異なる位置にしてください。", nameof(profile));
            }
            if (!hasRadius) throw new ArgumentException("断面には正の半径が必要です。", nameof(profile));
            Key key = new Key("Lathe");
            key.Add(segments);
            key.Add(points);
            return Cached(key, () => BuildLathe(points, segments));
        }

        /// <summary>中心原点で各辺の長さがsizeの角丸直方体を生成します。radiusは最短辺の半分以下です。</summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int segments = 3)
        {
            ValidatePositive(size.x, nameof(size));
            ValidatePositive(size.y, nameof(size));
            ValidatePositive(size.z, nameof(size));
            ValidateNonnegative(radius, nameof(radius));
            ValidateSegments(segments, 1, nameof(segments));
            if (radius > Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f)
                throw new ArgumentOutOfRangeException(nameof(radius), "角丸半径は最短辺の半分以下にしてください。");
            EnsureBudget(6L * (2 * segments + 2) * (2 * segments + 2));
            Key key = new Key("RoundedBox");
            key.Add(size); key.Add(radius); key.Add(segments);
            return Cached(key, () => BuildRoundedBox(size, radius, segments));
        }

        /// <summary>中心原点の閉じた円柱を生成します。bevelは上下端の面取り幅です。</summary>
        public static Mesh Cylinder(float radius, float height, int segments = 24, float bevel = 0f)
        {
            ValidatePositive(radius, nameof(radius));
            ValidatePositive(height, nameof(height));
            ValidateNonnegative(bevel, nameof(bevel));
            ValidateSegments(segments, 3, nameof(segments));
            if (bevel > Mathf.Min(radius, height * 0.5f))
                throw new ArgumentOutOfRangeException(nameof(bevel), "面取り幅は半径と半高さ以下にしてください。");
            Key key = new Key("Cylinder");
            key.Add(radius); key.Add(height); key.Add(segments); key.Add(bevel);
            return Cached(key, () =>
            {
                float half = height * 0.5f;
                List<Vector2> profile = new List<Vector2> { new Vector2(0f, -half) };
                AddDistinct(profile, new Vector2(radius - bevel, -half));
                AddDistinct(profile, new Vector2(radius, -half + bevel));
                AddDistinct(profile, new Vector2(radius, half - bevel));
                AddDistinct(profile, new Vector2(radius - bevel, half));
                AddDistinct(profile, new Vector2(0f, half));
                return BuildLathe(profile.ToArray(), segments);
            });
        }

        /// <summary>原点を中心とする半径0.5の球を緯度・経度方向に分割して生成します。</summary>
        public static Mesh Sphere(int lat = 12, int lon = 16)
        {
            ValidateSegments(lat, 2, nameof(lat));
            ValidateSegments(lon, 3, nameof(lon));
            Key key = new Key("Sphere"); key.Add(lat); key.Add(lon);
            return Cached(key, () =>
            {
                Builder builder = new Builder();
                for (int y = 0; y <= lat; y++)
                {
                    float phi = Mathf.PI * y / lat;
                    float ring = y == 0 || y == lat ? 0f : Mathf.Sin(phi);
                    for (int x = 0; x <= lon; x++)
                    {
                        float theta = Angle(x, lon);
                        Vector3 normal = new Vector3(ring * Mathf.Cos(theta), Mathf.Cos(phi), ring * Mathf.Sin(theta));
                        builder.Vertex(normal * 0.5f, normal, new Vector2((float)x / lon, 1f - (float)y / lat));
                    }
                }
                for (int y = 0; y < lat; y++)
                    for (int x = 0; x < lon; x++)
                    {
                        int a = y * (lon + 1) + x;
                        builder.Quad(a, a + 1, a + lon + 2, a + lon + 1);
                    }
                return builder.Build("Sphere");
            });
        }

        /// <summary>Y軸を中心とする輪を生成します。majorは管中心半径、minorは管半径でmajor未満です。</summary>
        public static Mesh Torus(float major, float minor, int seg = 24, int side = 10)
        {
            ValidatePositive(major, nameof(major)); ValidatePositive(minor, nameof(minor));
            ValidateSegments(seg, 3, nameof(seg)); ValidateSegments(side, 3, nameof(side));
            if (minor >= major) throw new ArgumentOutOfRangeException(nameof(minor), "管半径は管中心半径未満にしてください。");
            Key key = new Key("Torus"); key.Add(major); key.Add(minor); key.Add(seg); key.Add(side);
            return Cached(key, () =>
            {
                Builder builder = new Builder();
                for (int i = 0; i <= seg; i++)
                {
                    float theta = Angle(i, seg);
                    Vector3 radial = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                    for (int j = 0; j <= side; j++)
                    {
                        float phi = Angle(j, side);
                        Vector3 normal = radial * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi);
                        builder.Vertex(radial * major + normal * minor, normal, new Vector2((float)i / seg, (float)j / side));
                    }
                }
                for (int i = 0; i < seg; i++)
                    for (int j = 0; j < side; j++)
                    {
                        int a = i * (side + 1) + j;
                        builder.Quad(a, a + 1, a + side + 2, a + side + 1);
                    }
                return builder.Build("Torus");
            });
        }

        /// <summary>原点を中心に上向き法線を持つXZ面の円盤を生成します。</summary>
        public static Mesh Disc(float radius, int segments = 24)
        {
            ValidatePositive(radius, nameof(radius)); ValidateSegments(segments, 3, nameof(segments));
            Key key = new Key("Disc"); key.Add(radius); key.Add(segments);
            return Cached(key, () =>
            {
                Builder builder = new Builder();
                AddCap(builder, Vector3.zero, Vector3.right, Vector3.forward, Vector3.up, radius, segments);
                return builder.Build("Disc");
            });
        }

        /// <summary>幅width、長さlengthで、根元から先端のY変位がcurlの両面葉を生成します。根元は原点です。</summary>
        public static Mesh Leaf(float width, float length, float curl)
        {
            ValidatePositive(width, nameof(width)); ValidatePositive(length, nameof(length)); OwnedObjects.ValidateFinite(curl, nameof(curl));
            Key key = new Key("Leaf"); key.Add(width); key.Add(length); key.Add(curl);
            return Cached(key, () => BuildLeaf(width, length, curl));
        }

        /// <summary>ローカル乱数で再現可能な麺束を生成します。lengthは各麺の中心線の長さ、thicknessは直径です。</summary>
        public static Mesh Noodles(int strands, float length, float thickness, int seed)
        {
            if (strands < 1 || strands > 2048) throw new ArgumentOutOfRangeException(nameof(strands), "本数は1〜2048にしてください。");
            ValidatePositive(length, nameof(length)); ValidatePositive(thickness, nameof(thickness));
            EnsureBudget((long)strands * 190);
            Key key = new Key("Noodles"); key.Add(strands); key.Add(length); key.Add(thickness); key.Add(seed);
            return Cached(key, () =>
            {
                System.Random random = new System.Random(seed);
                Builder builder = new Builder();
                for (int strand = 0; strand < strands; strand++)
                {
                    Vector3[] path = new Vector3[25];
                    float phase = (float)random.NextDouble() * FullTurn;
                    float offsetX = ((float)random.NextDouble() - 0.5f) * length * 0.3f;
                    float offsetZ = ((float)random.NextDouble() - 0.5f) * length * 0.3f;
                    float amplitude = length * (0.04f + (float)random.NextDouble() * 0.035f);
                    for (int i = 0; i < path.Length; i++)
                    {
                        float t = (float)i / (path.Length - 1);
                        path[i] = new Vector3(amplitude * Mathf.Sin(t * FullTurn * 1.5f + phase), amplitude * 0.35f * Mathf.Cos(t * FullTurn + phase), t * length);
                    }
                    float measured = PathLength(path);
                    Vector3 origin = path[0];
                    for (int i = 0; i < path.Length; i++)
                        path[i] = (path[i] - origin) * (length / measured) + new Vector3(offsetX, thickness * strand * 0.08f, offsetZ - length * 0.5f);
                    AppendTube(builder, path, thickness * 0.5f, 6);
                }
                return builder.Build("Noodles");
            });
        }

        /// <summary>
        /// 連続する中心線に沿う閉じた管を生成します。重複点と180度の折り返しは受け付けません。
        /// 半径が曲率に対して大きく側面が反転する場合も例外を返します。
        /// </summary>
        public static Mesh Tube(Vector3[] path, float radius, int sides = 6)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            ValidatePositive(radius, nameof(radius)); ValidateSegments(sides, 3, nameof(sides));
            if (path.Length < 2) throw new ArgumentException("中心線には2点以上必要です。", nameof(path));
            EnsureBudget((long)path.Length * (sides + 1) + 2 * (sides + 2));
            Vector3[] points = (Vector3[])path.Clone();
            ValidatePath(points);
            Key key = new Key("Tube"); key.Add(radius); key.Add(sides); key.Add(points);
            return Cached(key, () =>
            {
                Builder builder = new Builder(); AppendTube(builder, points, radius, sides);
                return builder.Build("Tube");
            });
        }

        /// <summary>最大外径1の、内壁・底面・厚みのある上縁を持つ開口ボウルを生成します。</summary>
        public static Mesh Bowl()
        {
            return Cached(new Key("Bowl"), () => BuildLathe(new Vector2[]
            {
                new Vector2(0f, 0f), new Vector2(0.20f, 0f), new Vector2(0.26f, 0.04f),
                new Vector2(0.34f, 0.13f), new Vector2(0.43f, 0.26f), new Vector2(0.50f, 0.40f),
                new Vector2(0.47f, 0.41f), new Vector2(0.40f, 0.27f), new Vector2(0.31f, 0.14f),
                new Vector2(0.23f, 0.07f), new Vector2(0f, 0.07f)
            }, 32));
        }

        /// <summary>本体最大外径0.6の内外壁付き開口鍋と、左右の取っ手を生成します。</summary>
        public static Mesh Pot()
        {
            return Cached(new Key("Pot"), () =>
            {
                Mesh body = Lathe(new Vector2[]
                {
                    new Vector2(0f, 0f), new Vector2(0.27f, 0f), new Vector2(0.30f, 0.03f),
                    new Vector2(0.30f, 0.34f), new Vector2(0.28f, 0.34f),
                    new Vector2(0.28f, 0.04f), new Vector2(0f, 0.04f)
                }, 32);
                Mesh handle = Torus(0.07f, 0.012f, 20, 8);
                return Combine((body, Matrix4x4.identity),
                    (handle, Transform(new Vector3(-0.34f, 0.26f, 0f), new Vector3(90f, 0f, 0f), Vector3.one)),
                    (handle, Transform(new Vector3(0.34f, 0.26f, 0f), new Vector3(90f, 0f, 0f), Vector3.one)));
            });
        }

        /// <summary>開口した網じゃくを、交差する格子網・縦線・縁・長柄で生成します。</summary>
        public static Mesh Strainer()
        {
            return Cached(new Key("Strainer"), () =>
            {
                Builder builder = new Builder();
                const float radius = 0.19f;
                const float wire = 0.0035f;
                for (int axis = 0; axis < 2; axis++)
                    for (int line = -5; line <= 5; line++)
                    {
                        float offset = line * 0.03f;
                        float reach = Mathf.Sqrt(radius * radius - offset * offset);
                        Vector3 a = axis == 0 ? new Vector3(-reach, 0.015f, offset) : new Vector3(offset, 0.02f, -reach);
                        Vector3 b = axis == 0 ? new Vector3(reach, 0.015f, offset) : new Vector3(offset, 0.02f, reach);
                        AppendTube(builder, new Vector3[] { a, b }, wire, 5);
                    }
                for (int ring = 0; ring <= 4; ring++)
                {
                    float y = 0.015f + ring * 0.045f;
                    float r = radius + ring * 0.008f;
                    AppendTube(builder, CirclePath(r, y, 32), wire, 5);
                }
                for (int i = 0; i < 24; i++)
                {
                    float theta = FullTurn * i / 24;
                    Vector3 radial = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                    AppendTube(builder, new Vector3[] { radial * radius + Vector3.up * 0.015f, radial * 0.222f + Vector3.up * 0.195f }, wire, 5);
                }
                AppendTube(builder, new Vector3[] { new Vector3(0.21f, 0.195f, 0f), new Vector3(0.32f, 0.24f, 0f), new Vector3(0.64f, 0.27f, 0f) }, 0.013f, 8);
                return builder.Build("Strainer");
            });
        }

        /// <summary>浅い内外壁付きの杓と曲がった長柄を持つお玉を生成します。</summary>
        public static Mesh Ladle()
        {
            return Cached(new Key("Ladle"), () =>
            {
                Mesh cup = Lathe(new Vector2[]
                {
                    new Vector2(0f, 0f), new Vector2(0.065f, 0.014f), new Vector2(0.105f, 0.045f),
                    new Vector2(0.13f, 0.085f), new Vector2(0.122f, 0.088f),
                    new Vector2(0.098f, 0.049f), new Vector2(0.057f, 0.023f), new Vector2(0f, 0.014f)
                }, 24);
                Mesh handle = Tube(new Vector3[] { new Vector3(0.12f, 0.08f, 0f), new Vector3(0.20f, 0.19f, 0f), new Vector3(0.51f, 0.25f, 0f), new Vector3(0.56f, 0.23f, 0f) }, 0.012f, 8);
                return Combine((cup, Matrix4x4.identity), (handle, Matrix4x4.identity));
            });
        }

        /// <summary>二本の腕・平たい先端・付け根を持つ開いたトングを生成します。</summary>
        public static Mesh Tongs()
        {
            return Cached(new Key("Tongs"), () =>
            {
                Mesh arm = RoundedBox(new Vector3(0.025f, 0.018f, 0.40f), 0.007f);
                Mesh tip = RoundedBox(new Vector3(0.065f, 0.015f, 0.09f), 0.006f);
                Mesh hinge = Cylinder(0.028f, 0.028f, 16);
                return Combine(
                    (arm, Transform(new Vector3(-0.043f, 0f, 0.20f), new Vector3(0f, -12f, 0f), Vector3.one)),
                    (arm, Transform(new Vector3(0.043f, 0f, 0.20f), new Vector3(0f, 12f, 0f), Vector3.one)),
                    (tip, Transform(new Vector3(-0.087f, 0f, 0.41f), new Vector3(0f, -12f, 0f), Vector3.one)),
                    (tip, Transform(new Vector3(0.087f, 0f, 0.41f), new Vector3(0f, 12f, 0f), Vector3.one)),
                    (hinge, Matrix4x4.identity));
            });
        }

        /// <summary>丸い胴、上下の口金、吊り輪、房を持つ提灯を生成します。</summary>
        public static Mesh Lantern()
        {
            return Cached(new Key("Lantern"), () =>
            {
                List<(Mesh mesh, Matrix4x4 transform)> parts = new List<(Mesh mesh, Matrix4x4 transform)>();
                parts.Add((Sphere(16, 24), Transform(new Vector3(0f, 0.35f, 0f), Vector3.zero, new Vector3(0.46f, 0.60f, 0.46f))));
                Mesh collar = Cylinder(0.08f, 0.035f, 20, 0.005f);
                parts.Add((collar, Transform(new Vector3(0f, 0.055f, 0f), Vector3.zero, Vector3.one)));
                parts.Add((collar, Transform(new Vector3(0f, 0.645f, 0f), Vector3.zero, Vector3.one)));
                parts.Add((Torus(0.045f, 0.007f, 16, 6), Transform(new Vector3(0f, 0.705f, 0f), new Vector3(90f, 0f, 0f), Vector3.one)));
                for (int i = 0; i < 8; i++)
                {
                    float theta = FullTurn * i / 8;
                    Vector3 end = new Vector3(Mathf.Cos(theta) * 0.025f, -0.13f, Mathf.Sin(theta) * 0.025f);
                    parts.Add((Tube(new Vector3[] { new Vector3(0f, 0.045f, 0f), end }, 0.004f, 5), Matrix4x4.identity));
                }
                return Combine(parts.ToArray());
            });
        }

        /// <summary>台座、計量皿、斜めの表示部を持つ卓上秤を生成します。</summary>
        public static Mesh Scale()
        {
            return Cached(new Key("Scale"), () => Combine(
                (RoundedBox(new Vector3(0.42f, 0.10f, 0.36f), 0.025f), Transform(new Vector3(0f, 0.05f, 0f), Vector3.zero, Vector3.one)),
                (Cylinder(0.06f, 0.04f, 20), Transform(new Vector3(0f, 0.12f, 0.02f), Vector3.zero, Vector3.one)),
                (Cylinder(0.20f, 0.025f, 32, 0.008f), Transform(new Vector3(0f, 0.1525f, 0.02f), Vector3.zero, Vector3.one)),
                (RoundedBox(new Vector3(0.16f, 0.05f, 0.06f), 0.008f), Transform(new Vector3(0f, 0.09f, -0.16f), new Vector3(-25f, 0f, 0f), Vector3.one))));
        }

        /// <summary>内外壁付きの壺と取り外し形状の蓋・つまみを持つ調味料容器を生成します。</summary>
        public static Mesh CondimentJar()
        {
            return Cached(new Key("CondimentJar"), () =>
            {
                Mesh body = Lathe(new Vector2[]
                {
                    new Vector2(0f, 0f), new Vector2(0.09f, 0f), new Vector2(0.11f, 0.025f),
                    new Vector2(0.11f, 0.16f), new Vector2(0.085f, 0.20f), new Vector2(0.085f, 0.23f),
                    new Vector2(0.074f, 0.23f), new Vector2(0.074f, 0.195f), new Vector2(0.097f, 0.15f),
                    new Vector2(0.097f, 0.028f), new Vector2(0f, 0.028f)
                }, 24);
                return Combine((body, Matrix4x4.identity),
                    (Cylinder(0.09f, 0.025f, 24, 0.006f), Transform(new Vector3(0f, 0.245f, 0f), Vector3.zero, Vector3.one)),
                    (Sphere(8, 12), Transform(new Vector3(0f, 0.275f, 0f), Vector3.zero, new Vector3(0.045f, 0.04f, 0.045f))));
            });
        }

        /// <summary>
        /// 三角形メッシュをアフィン変換して単一サブメッシュへ統合します。頂点・法線・UV0を保持し、
        /// 非一様スケールと鏡映に対応します。法線のない入力は三角形から補完します。
        /// キャッシュキーは入力順、メッシュの構造値および変換行列を含みます。
        /// 変更禁止のFactory共有メッシュは登録済み構造IDで比較し、外部メッシュは毎回属性を読み取ります。
        /// </summary>
        public static Mesh Combine(params (Mesh mesh, Matrix4x4 transform)[] parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (parts.Length == 0) throw new ArgumentException("統合するメッシュを指定してください。", nameof(parts));
            (Mesh mesh, Matrix4x4 transform)[] inputs = ((Mesh mesh, Matrix4x4 transform)[])parts.Clone();
            Key key = new Key("Combine"); key.Add(inputs.Length);
            MeshPart[] snapshots = new MeshPart[inputs.Length];
            long count = 0;
            for (int i = 0; i < inputs.Length; i++)
            {
                Mesh mesh = inputs[i].mesh;
                if (mesh == null) throw new ArgumentException("メッシュがnullまたは破棄済みです。", nameof(parts));
                if (!mesh.isReadable) throw new ArgumentException("統合には読み取り可能なメッシュが必要です。", nameof(parts));
                Matrix4x4 transform = inputs[i].transform;
                ValidateTransform(transform);
                int structureId;
                if (StructureIds.TryGetValue(mesh, out structureId))
                {
                    key.Add(1); key.Add(structureId);
                    for (int element = 0; element < 16; element++) key.Add(transform[element]);
                }
                else
                {
                    key.Add(0);
                    snapshots[i] = new MeshPart(mesh, transform, key);
                }
                count += mesh.vertexCount;
                EnsureBudget(count);
            }
            return Cached(key, () =>
            {
                Builder builder = new Builder();
                for (int partIndex = 0; partIndex < inputs.Length; partIndex++)
                {
                    MeshPart part = snapshots[partIndex] ?? new MeshPart(inputs[partIndex].mesh, inputs[partIndex].transform, null);
                    int offset = builder.Count;
                    Matrix4x4 normalMatrix = part.Transform.inverse.transpose;
                    for (int i = 0; i < part.Positions.Length; i++)
                        builder.Vertex(part.Transform.MultiplyPoint3x4(part.Positions[i]), normalMatrix.MultiplyVector(part.Normals[i]), part.Uvs[i]);
                    bool mirrored = part.Transform.determinant < 0f;
                    for (int i = 0; i < part.Triangles.Length; i += 3)
                    {
                        int a = part.Triangles[i] + offset;
                        int b = part.Triangles[i + 1] + offset;
                        int c = part.Triangles[i + 2] + offset;
                        builder.Triangle(a, mirrored ? c : b, mirrored ? b : c);
                    }
                }
                return builder.Build("Combined");
            });
        }

        /// <summary>回転断面の各帯を周方向に滑らかな独立頂点で作成します。</summary>
        private static Mesh BuildLathe(Vector2[] points, int segments)
        {
            Builder builder = new Builder();
            double signedArea = 0d;
            double total = 0d;
            double[] distances = new double[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 next = points[(i + 1) % points.Length];
                signedArea += (double)points[i].x * next.y - (double)next.x * points[i].y;
                if (i > 0)
                {
                    total += Distance(points[i], points[i - 1]);
                    distances[i] = total;
                }
            }
            float orientation = signedArea < 0d ? -1f : 1f;
            if (signedArea == 0d && points[points.Length - 1].y < points[0].y) orientation = -1f;
            for (int row = 0; row < points.Length - 1; row++)
            {
                Vector2 a = points[row]; Vector2 b = points[row + 1];
                double dx = (double)b.x - a.x; double dy = (double)b.y - a.y;
                double magnitude = Math.Sqrt(dx * dx + dy * dy);
                float radialNormal = (float)(dy / magnitude) * orientation;
                float verticalNormal = (float)(-dx / magnitude) * orientation;
                int start = builder.Count;
                for (int end = 0; end < 2; end++)
                {
                    Vector2 p = end == 0 ? a : b;
                    for (int col = 0; col <= segments; col++)
                    {
                        float angle = Angle(col, segments);
                        Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        builder.Vertex(radial * p.x + Vector3.up * p.y, radial * radialNormal + Vector3.up * verticalNormal,
                            new Vector2((float)col / segments, (float)(distances[row + end] / total)));
                    }
                }
                for (int col = 0; col < segments; col++)
                {
                    int p = start + col; int q = p + segments + 1;
                    if (orientation > 0f) builder.Quad(p, q, q + 1, p + 1);
                    else builder.Quad(p, p + 1, q + 1, q);
                }
            }
            return builder.Build("Lathe");
        }

        /// <summary>直方体の面グリッドを内側直方体から半径分だけ押し出して角を丸めます。</summary>
        private static Mesh BuildRoundedBox(Vector3 size, float radius, int segments)
        {
            Builder builder = new Builder();
            Vector3 half = size * 0.5f;
            Vector3 inner = half - Vector3.one * radius;
            Vector3[] normals = new Vector3[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] us = new Vector3[] { Vector3.back, Vector3.forward, Vector3.right, Vector3.right, Vector3.right, Vector3.left };
            Vector3[] vs = new Vector3[] { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
            for (int face = 0; face < normals.Length; face++)
            {
                Vector3 normal = normals[face]; Vector3 u = us[face]; Vector3 v = vs[face];
                float normalHalf = Extent(half, normal);
                float[] xs = FaceCoordinates(Extent(half, u), radius, segments);
                float[] ys = FaceCoordinates(Extent(half, v), radius, segments);
                int start = builder.Count;
                for (int y = 0; y < ys.Length; y++)
                    for (int x = 0; x < xs.Length; x++)
                    {
                        Vector3 point = normal * normalHalf + u * xs[x] + v * ys[y];
                        Vector3 center = new Vector3(Mathf.Clamp(point.x, -inner.x, inner.x), Mathf.Clamp(point.y, -inner.y, inner.y), Mathf.Clamp(point.z, -inner.z, inner.z));
                        Vector3 surfaceNormal = radius == 0f ? normal : Normalize(point - center);
                        Vector3 surface = radius == 0f ? point : center + surfaceNormal * radius;
                        builder.Vertex(surface, surfaceNormal, new Vector2((xs[x] + Extent(half, u)) / (2f * Extent(half, u)), (ys[y] + Extent(half, v)) / (2f * Extent(half, v))));
                    }
                for (int y = 0; y < ys.Length - 1; y++)
                    for (int x = 0; x < xs.Length - 1; x++)
                    {
                        int a = start + y * xs.Length + x;
                        builder.Quad(a, a + 1, a + xs.Length + 1, a + xs.Length);
                    }
            }
            return builder.Build("RoundedBox");
        }

        /// <summary>角丸領域を分割し、中央の平面領域を大きな一区画として残します。</summary>
        private static float[] FaceCoordinates(float half, float radius, int segments)
        {
            if (radius == 0f) return new float[] { -half, half };
            List<float> coordinates = new List<float>();
            for (int i = 0; i <= segments; i++) coordinates.Add(-half + radius * i / segments);
            float inner = half - radius;
            if (inner > 0f) coordinates.Add(inner);
            for (int i = 1; i <= segments; i++) coordinates.Add(inner + radius * i / segments);
            return coordinates.ToArray();
        }

        /// <summary>葉の表裏に別々の頂点を割り当てて法線と巻き順を反転します。</summary>
        private static Mesh BuildLeaf(float width, float length, float curl)
        {
            const int rows = 16;
            const int columns = 6;
            Builder builder = new Builder();
            for (int side = 0; side < 2; side++)
            {
                int start = builder.Count;
                for (int row = 0; row <= rows; row++)
                {
                    float t = (float)row / rows;
                    float envelope = row == 0 || row == rows ? 0f : Mathf.Sin(Mathf.PI * t);
                    for (int col = 0; col <= columns; col++)
                    {
                        float s = 2f * col / columns - 1f;
                        float x = width * 0.5f * envelope * s;
                        float y = curl * t * t + width * 0.08f * envelope * (1f - s * s);
                        Vector3 ds = new Vector3(width * 0.5f * envelope, -width * 0.16f * envelope * s, 0f);
                        Vector3 dt = new Vector3(width * 0.5f * Mathf.PI * Mathf.Cos(Mathf.PI * t) * s,
                            2f * curl * t + width * 0.08f * Mathf.PI * Mathf.Cos(Mathf.PI * t) * (1f - s * s), length);
                        Vector3 normal = envelope == 0f ? Normalize(new Vector3(0f, length, -2f * curl * t)) : Normalize(Vector3.Cross(dt, ds));
                        builder.Vertex(new Vector3(x, y, length * t), side == 0 ? normal : -normal, new Vector2((float)col / columns, t));
                    }
                }
                for (int row = 0; row < rows; row++)
                    for (int col = 0; col < columns; col++)
                    {
                        int a = start + row * (columns + 1) + col;
                        if (side == 0) builder.Quad(a, a + columns + 1, a + columns + 2, a + 1);
                        else builder.Quad(a, a + 1, a + columns + 2, a + columns + 1);
                    }
            }
            return builder.Build("Leaf");
        }

        /// <summary>平行移動フレームでねじれを抑えた管の側面と独立法線の端面を追加します。</summary>
        private static void AppendTube(Builder builder, Vector3[] path, float radius, int sides)
        {
            ValidatePath(path);
            Vector3[] tangents = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                Vector3 before = i == 0 ? Direction(path[1], path[0]) : Direction(path[i], path[i - 1]);
                Vector3 after = i == path.Length - 1 ? before : Direction(path[i + 1], path[i]);
                tangents[i] = Normalize(before + after);
            }
            bool closed = Exact(path[0], path[path.Length - 1]);
            if (closed)
            {
                Vector3 tangent = Normalize(Direction(path[1], path[0]) + Direction(path[path.Length - 1], path[path.Length - 2]));
                tangents[0] = tangent; tangents[path.Length - 1] = tangent;
            }
            Vector3[] frames = new Vector3[path.Length];
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangents[0], Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            frames[0] = Normalize(Vector3.Cross(reference, tangents[0]));
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 transported = Quaternion.FromToRotation(tangents[i - 1], tangents[i]) * frames[i - 1];
                frames[i] = Normalize(transported - tangents[i] * Vector3.Dot(transported, tangents[i]));
            }
            float total = PathLength(path);
            if (closed)
            {
                float twist = Vector3.SignedAngle(frames[frames.Length - 1], frames[0], tangents[0]);
                float distance = 0f;
                for (int i = 1; i < frames.Length; i++)
                {
                    distance += SegmentLength(path[i], path[i - 1]);
                    frames[i] = Quaternion.AngleAxis(twist * distance / total, tangents[i]) * frames[i];
                }
                frames[frames.Length - 1] = frames[0];
            }
            int start = builder.Count;
            float traveled = 0f;
            for (int i = 0; i < path.Length; i++)
            {
                if (i > 0) traveled += SegmentLength(path[i], path[i - 1]);
                Vector3 binormal = Normalize(Vector3.Cross(tangents[i], frames[i]));
                for (int j = 0; j <= sides; j++)
                {
                    float theta = Angle(j, sides);
                    Vector3 normal = frames[i] * Mathf.Cos(theta) + binormal * Mathf.Sin(theta);
                    builder.Vertex(path[i] + normal * radius, normal, new Vector2((float)j / sides, traveled / total));
                }
            }
            for (int i = 0; i < path.Length - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = start + i * (sides + 1) + j;
                    builder.TubeQuad(a, a + 1, a + sides + 2, a + sides + 1);
                }
            if (!closed)
            {
                AddCap(builder, path[0], frames[0], Vector3.Cross(tangents[0], frames[0]), -tangents[0], radius, sides);
                int last = path.Length - 1;
                AddCap(builder, path[last], frames[last], Vector3.Cross(tangents[last], frames[last]), tangents[last], radius, sides);
            }
        }

        /// <summary>指定された円形基底と法線からUV付きの三角形扇を追加します。</summary>
        private static void AddCap(Builder builder, Vector3 center, Vector3 u, Vector3 v, Vector3 normal, float radius, int segments)
        {
            int origin = builder.Vertex(center, normal, new Vector2(0.5f, 0.5f));
            int rim = builder.Count;
            for (int i = 0; i <= segments; i++)
            {
                float theta = Angle(i, segments);
                float x = Mathf.Cos(theta); float y = Mathf.Sin(theta);
                builder.Vertex(center + (u * x + v * y) * radius, normal, new Vector2(0.5f + x * 0.5f, 0.5f + y * 0.5f));
            }
            bool reverse = Vector3.Dot(Vector3.Cross(u, v), normal) < 0f;
            for (int i = 0; i < segments; i++)
                builder.Triangle(origin, rim + i + (reverse ? 1 : 0), rim + i + (reverse ? 0 : 1));
        }

        /// <summary>始終点が一致する円形の中心線を生成します。</summary>
        private static Vector3[] CirclePath(float radius, float y, int segments)
        {
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i < segments; i++)
            {
                float theta = Angle(i, segments);
                points[i] = new Vector3(radius * Mathf.Cos(theta), y, radius * Mathf.Sin(theta));
            }
            points[segments] = points[0];
            return points;
        }

        /// <summary>変換行列を位置・度単位の回転・拡大率から生成します。</summary>
        private static Matrix4x4 Transform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            return Matrix4x4.TRS(position, Quaternion.Euler(rotation), scale);
        }

        /// <summary>所有する共有メッシュを取得し、外部で破棄済みなら再作成します。</summary>
        private static Mesh Cached(Key key, Func<Mesh> create)
        {
            string value = key.ToString();
            Mesh existing;
            if (Cache.TryGetValue(value, out existing) && existing != null) return existing;
            Mesh mesh = create();
            if (!StructureIds.ContainsKey(mesh)) StructureIds.Add(mesh, checked(++nextStructureId));
            if (Cache.ContainsKey(value))
            {
                Cache[value] = mesh;
                return mesh;
            }
            Cache.Add(value, mesh);
            return mesh;
        }

        /// <summary>閉じた周方向UVの継ぎ目に同一座標を割り当てます。</summary>
        private static float Angle(int index, int segments)
        {
            return index == segments ? 0f : FullTurn * index / segments;
        }

        /// <summary>軸方向の半寸法を取得します。</summary>
        private static float Extent(Vector3 half, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y + Mathf.Abs(axis.z) * half.z;
        }

        /// <summary>断面に同一の連続点を追加しません。</summary>
        private static void AddDistinct(List<Vector2> points, Vector2 point)
        {
            if (points.Count == 0 || !Exact(points[points.Count - 1], point)) points.Add(point);
        }

        /// <summary>近似比較を使用せず二次元座標の一致を判定します。</summary>
        private static bool Exact(Vector2 a, Vector2 b) { return a.x == b.x && a.y == b.y; }

        /// <summary>近似比較を使用せず三次元座標の一致を判定します。</summary>
        private static bool Exact(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }

        /// <summary>倍精度で断面の線分長を算出します。</summary>
        private static double Distance(Vector2 a, Vector2 b)
        {
            double x = (double)a.x - b.x; double y = (double)a.y - b.y;
            return Math.Sqrt(x * x + y * y);
        }

        /// <summary>倍精度で差分を計算して単位方向を取得します。</summary>
        private static Vector3 Direction(Vector3 a, Vector3 b)
        {
            double x = (double)a.x - b.x; double y = (double)a.y - b.y; double z = (double)a.z - b.z;
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length == 0d) throw new ArgumentException("連続点は異なる位置にしてください。");
            return new Vector3((float)(x / length), (float)(y / length), (float)(z / length));
        }

        /// <summary>単精度の二乗による桁あふれを避けてベクトルを正規化します。</summary>
        private static Vector3 Normalize(Vector3 value)
        {
            ValidateVector(value, nameof(value));
            double length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y + (double)value.z * value.z);
            if (length == 0d) throw new ArgumentException("ゼロ方向の法線または折り返しは使用できません。");
            return new Vector3((float)(value.x / length), (float)(value.y / length), (float)(value.z / length));
        }

        /// <summary>有限範囲に収まる中心線の線分長を計算します。</summary>
        private static float SegmentLength(Vector3 a, Vector3 b)
        {
            double x = (double)a.x - b.x; double y = (double)a.y - b.y; double z = (double)a.z - b.z;
            float length = (float)Math.Sqrt(x * x + y * y + z * z);
            ValidatePositive(length, "path"); return length;
        }

        /// <summary>中心線全体の長さを取得します。</summary>
        private static float PathLength(Vector3[] path)
        {
            double length = 0d;
            for (int i = 1; i < path.Length; i++) length += SegmentLength(path[i], path[i - 1]);
            float result = (float)length;
            ValidatePositive(result, nameof(path)); return result;
        }

        /// <summary>中心線の有限性と連続点・折り返しの制約を検査します。</summary>
        private static void ValidatePath(Vector3[] path)
        {
            for (int i = 0; i < path.Length; i++)
            {
                ValidateVector(path[i], nameof(path));
                if (i > 0 && Exact(path[i], path[i - 1])) throw new ArgumentException("中心線に連続する重複点があります。", nameof(path));
                if (i > 0 && i < path.Length - 1)
                {
                    Vector3 before = Direction(path[i], path[i - 1]);
                    Vector3 after = Direction(path[i + 1], path[i]);
                    if (Exact(before + after, Vector3.zero)) throw new ArgumentException("中心線の180度折り返しには対応していません。", nameof(path));
                }
            }
        }

        /// <summary>有限な可逆アフィン変換のみ受け付けます。</summary>
        private static void ValidateTransform(Matrix4x4 transform)
        {
            for (int i = 0; i < 16; i++) OwnedObjects.ValidateFinite(transform[i], nameof(transform));
            if (transform.m30 != 0f || transform.m31 != 0f || transform.m32 != 0f || transform.m33 != 1f)
                throw new ArgumentException("透視変換ではなくアフィン変換を指定してください。", nameof(transform));
            float determinant = transform.determinant;
            OwnedObjects.ValidateFinite(determinant, nameof(transform));
            if (determinant == 0f) throw new ArgumentException("特異行列は使用できません。", nameof(transform));
            Matrix4x4 inverse = transform.inverse;
            for (int i = 0; i < 16; i++) OwnedObjects.ValidateFinite(inverse[i], nameof(transform));
        }

        /// <summary>各座標が有限か検査します。</summary>
        private static void ValidateVector(Vector3 value, string name)
        {
            OwnedObjects.ValidateFinite(value.x, name); OwnedObjects.ValidateFinite(value.y, name); OwnedObjects.ValidateFinite(value.z, name);
        }

        /// <summary>正の有限値を検査します。</summary>
        private static void ValidatePositive(float value, string name)
        {
            OwnedObjects.ValidateFinite(value, name);
            if (value <= 0f) throw new ArgumentOutOfRangeException(name, "正の値を指定してください。");
        }

        /// <summary>非負の有限値を検査します。</summary>
        private static void ValidateNonnegative(float value, string name)
        {
            OwnedObjects.ValidateFinite(value, name);
            if (value < 0f) throw new ArgumentOutOfRangeException(name, "0以上の値を指定してください。");
        }

        /// <summary>分割数の下限と過大な割り当てを防ぐ上限を検査します。</summary>
        private static void ValidateSegments(int value, int minimum, string name)
        {
            if (value < minimum || value > MaximumSegments) throw new ArgumentOutOfRangeException(name, "分割数が許容範囲外です。");
        }

        /// <summary>生成頂点数に上限を設けて予期しないメモリ消費を防ぎます。</summary>
        private static void EnsureBudget(long count)
        {
            if (count > MaximumVertices) throw new ArgumentOutOfRangeException("vertexCount", "生成頂点数が上限を超えています。");
        }

        /// <summary>ハッシュ衝突や浮動小数点の丸めによる混同を避ける完全構造キーです。</summary>
        private sealed class Key
        {
            private readonly StringBuilder text;

            /// <summary>生成方式をキーの接頭辞として登録します。</summary>
            public Key(string kind) { text = new StringBuilder(kind); text.Append('|'); }

            /// <summary>整数を区切り付きで登録します。</summary>
            public void Add(int value) { text.Append(value.ToString(CultureInfo.InvariantCulture)); text.Append('|'); }

            /// <summary>浮動小数点をビット列のまま登録します。</summary>
            public void Add(float value) { Add(BitConverter.SingleToInt32Bits(value)); }

            /// <summary>二次元座標を登録します。</summary>
            public void Add(Vector2 value) { Add(value.x); Add(value.y); }

            /// <summary>三次元座標を登録します。</summary>
            public void Add(Vector3 value) { Add(value.x); Add(value.y); Add(value.z); }

            /// <summary>二次元配列を長さと要素値で登録します。</summary>
            public void Add(Vector2[] values) { Add(values.Length); foreach (Vector2 value in values) Add(value); }

            /// <summary>三次元配列を長さと要素値で登録します。</summary>
            public void Add(Vector3[] values) { Add(values.Length); foreach (Vector3 value in values) Add(value); }

            /// <summary>完全な構造文字列を返します。</summary>
            public override string ToString() { return text.ToString(); }
        }

        /// <summary>入力メッシュを読み取り時点の構造値に固定します。</summary>
        private sealed class MeshPart
        {
            public readonly Vector3[] Positions;
            public readonly Vector3[] Normals;
            public readonly Vector2[] Uvs;
            public readonly int[] Triangles;
            public readonly Matrix4x4 Transform;

            /// <summary>頂点・属性・各サブメッシュを検証し、完全キーと統合用スナップショットを生成します。</summary>
            public MeshPart(Mesh mesh, Matrix4x4 transform, Key key)
            {
                Transform = transform; Positions = mesh.vertices; Normals = mesh.normals; Uvs = mesh.uv;
                if (Positions.Length == 0) throw new ArgumentException("空のメッシュは統合できません。", nameof(mesh));
                EnsureBudget(Positions.Length);
                foreach (Vector3 position in Positions) ValidateVector(position, nameof(mesh));
                if (Normals.Length != 0 && Normals.Length != Positions.Length) throw new ArgumentException("法線数が頂点数と一致しません。", nameof(mesh));
                if (Uvs.Length != 0 && Uvs.Length != Positions.Length) throw new ArgumentException("UV数が頂点数と一致しません。", nameof(mesh));
                if (key != null)
                {
                    key.Add(Positions); key.Add(Normals); key.Add(Uvs); key.Add(mesh.subMeshCount);
                    AddAdditionalAttributes(mesh, key);
                }
                List<int> indices = new List<int>();
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) throw new ArgumentException("三角形以外のトポロジーには対応していません。", nameof(mesh));
                    int[] triangles = mesh.GetTriangles(submesh);
                    if (triangles.Length % 3 != 0) throw new ArgumentException("三角形インデックス数が不正です。", nameof(mesh));
                    if (key != null) key.Add(triangles.Length);
                    foreach (int index in triangles)
                    {
                        if (index < 0 || index >= Positions.Length) throw new ArgumentException("頂点インデックスが範囲外です。", nameof(mesh));
                        if (key != null) key.Add(index);
                        indices.Add(index);
                    }
                }
                if (key != null) for (int i = 0; i < 16; i++) key.Add(transform[i]);
                Triangles = indices.ToArray();
                if (Triangles.Length == 0) throw new ArgumentException("三角形を持つメッシュを指定してください。", nameof(mesh));
                if (Normals.Length == 0) Normals = CalculateNormals(Positions, Triangles);
                else for (int i = 0; i < Normals.Length; i++) Normals[i] = Normalize(Normals[i]);
                if (Uvs.Length == 0) Uvs = new Vector2[Positions.Length];
                foreach (Vector2 uv in Uvs) { OwnedObjects.ValidateFinite(uv.x, nameof(mesh)); OwnedObjects.ValidateFinite(uv.y, nameof(mesh)); }
            }

            /// <summary>統合では保持しない副属性もキーに含め、異なる入力構造を混同しません。</summary>
            private static void AddAdditionalAttributes(Mesh mesh, Key key)
            {
                List<Vector4> channel = new List<Vector4>();
                for (int i = 0; i < 8; i++)
                {
                    channel.Clear(); mesh.GetUVs(i, channel); key.Add(channel.Count);
                    foreach (Vector4 value in channel) { key.Add(value.x); key.Add(value.y); key.Add(value.z); key.Add(value.w); }
                }
                Vector4[] tangents = mesh.tangents; key.Add(tangents.Length);
                foreach (Vector4 value in tangents) { key.Add(value.x); key.Add(value.y); key.Add(value.z); key.Add(value.w); }
                Color[] colors = mesh.colors; key.Add(colors.Length);
                foreach (Color value in colors) { key.Add(value.r); key.Add(value.g); key.Add(value.b); key.Add(value.a); }
                Matrix4x4[] poses = mesh.bindposes; key.Add(poses.Length);
                foreach (Matrix4x4 pose in poses) for (int i = 0; i < 16; i++) key.Add(pose[i]);
                BoneWeight[] weights = mesh.boneWeights; key.Add(weights.Length);
                foreach (BoneWeight weight in weights)
                {
                    key.Add(weight.boneIndex0); key.Add(weight.boneIndex1); key.Add(weight.boneIndex2); key.Add(weight.boneIndex3);
                    key.Add(weight.weight0); key.Add(weight.weight1); key.Add(weight.weight2); key.Add(weight.weight3);
                }
            }

            /// <summary>面積で重み付けした頂点法線を補完します。孤立頂点には上向き法線を与えます。</summary>
            private static Vector3[] CalculateNormals(Vector3[] positions, int[] triangles)
            {
                Vector3[] normals = new Vector3[positions.Length];
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i]; int b = triangles[i + 1]; int c = triangles[i + 2];
                    Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                    normals[a] += normal; normals[b] += normal; normals[c] += normal;
                }
                for (int i = 0; i < normals.Length; i++) normals[i] = Exact(normals[i], Vector3.zero) ? Vector3.up : Normalize(normals[i]);
                return normals;
            }
        }

        /// <summary>検証済みの頂点属性と三角形を蓄積してUnityメッシュへ変換します。</summary>
        private sealed class Builder
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            /// <summary>蓄積済みの頂点数を取得します。</summary>
            public int Count { get { return positions.Count; } }

            /// <summary>有限座標と単位法線を持つ頂点を追加してインデックスを返します。</summary>
            public int Vertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                EnsureBudget((long)positions.Count + 1);
                ValidateVector(position, nameof(position));
                OwnedObjects.ValidateFinite(uv.x, nameof(uv)); OwnedObjects.ValidateFinite(uv.y, nameof(uv));
                Vector3 unit = Normalize(normal);
                int index = positions.Count;
                positions.Add(position); normals.Add(unit); uvs.Add(uv);
                return index;
            }

            /// <summary>面積ゼロの三角形を除いて指定巻き順の面を追加します。</summary>
            public void Triangle(int a, int b, int c)
            {
                Vector3 p = positions[a]; Vector3 q = positions[b]; Vector3 r = positions[c];
                double ux = (double)q.x - p.x; double uy = (double)q.y - p.y; double uz = (double)q.z - p.z;
                double vx = (double)r.x - p.x; double vy = (double)r.y - p.y; double vz = (double)r.z - p.z;
                double nx = uy * vz - uz * vy; double ny = uz * vx - ux * vz; double nz = ux * vy - uy * vx;
                if (nx == 0d && ny == 0d && nz == 0d) return;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            /// <summary>指定された境界順の四角形を二つの三角形に分割します。</summary>
            public void Quad(int a, int b, int c, int d) { Triangle(a, b, c); Triangle(a, c, d); }

            /// <summary>管の曲がりによる面の反転・潰れを拒否して四角形を追加します。</summary>
            public void TubeQuad(int a, int b, int c, int d)
            {
                ValidateTubeTriangle(a, b, c); ValidateTubeTriangle(a, c, d);
                Quad(a, b, c, d);
            }

            /// <summary>管側面の幾何法線が外向きの頂点法線と同じ側を向くことを検査します。</summary>
            private void ValidateTubeTriangle(int a, int b, int c)
            {
                Vector3 p = positions[a]; Vector3 q = positions[b]; Vector3 r = positions[c];
                double ux = (double)q.x - p.x; double uy = (double)q.y - p.y; double uz = (double)q.z - p.z;
                double vx = (double)r.x - p.x; double vy = (double)r.y - p.y; double vz = (double)r.z - p.z;
                Vector3 expected = normals[a] + normals[b] + normals[c];
                double alignment = (uy * vz - uz * vy) * expected.x + (uz * vx - ux * vz) * expected.y + (ux * vy - uy * vx) * expected.z;
                if (alignment <= 0d) throw new ArgumentException("管の半径を小さくするか、中心線の急な曲がりを緩めてください。");
            }

            /// <summary>頂点数に適したインデックス形式で法線・UV・境界を持つメッシュを完成します。</summary>
            public Mesh Build(string name)
            {
                if (triangles.Count == 0) throw new ArgumentException("入力の寸法では有効な面を生成できません。");
                Mesh mesh = new Mesh { name = "MeshFactory." + name, indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                try
                {
                    mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
                    mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                    return mesh;
                }
                catch
                {
                    OwnedObjects.Destroy(mesh);
                    throw;
                }
            }
        }
    }
}
