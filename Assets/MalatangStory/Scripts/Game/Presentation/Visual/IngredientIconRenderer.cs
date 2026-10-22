using System;
using System.Collections;
using System.Collections.Generic;
using MalatangStory.Ingredients;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MalatangStory.Game
{
    /// <summary>食材を隔離した撮影台で描画し、所有するスプライトをキャッシュします。</summary>
    public sealed class IngredientIconRenderer : MonoBehaviour
    {
        private const int IconSize = 128;
        private const int IconLayer = 31;
        private const uint IconRenderingLayer = 1u << 7;
        private const float FramePadding = 0.07f;
        private const float SlenderPadding = 0.035f;
        /// <summary>白い面が受ける照度（URP Lit の拡散は albedo×照度×N・L）。</summary>
        private const float KeyIlluminance = 0.95f;
        private const float FillIlluminance = 0.38f;
        private const float TopIlluminance = 0.26f;
        private const float RimIlluminance = 0.55f;
        private const float ShoulderStart = 0.72f;
        private const float OutlineAlpha = 0.3f;
        /// <summary>シーンの時刻に左右されない、色温度を中立に寄せた撮影用の環境光。</summary>
        private static readonly Color StudioAmbient = new Color(0.36f, 0.355f, 0.35f);
        private static readonly Vector3 KeyDirection = new Vector3(-0.9f, 1.3f, -1.5f);
        private static readonly Vector3 FillDirection = new Vector3(1.3f, 0.2f, -1.3f);
        private static readonly Vector3 TopDirection = new Vector3(0.1f, 1.8f, 0.2f);
        private static readonly Vector3 RimDirection = new Vector3(0.8f, 0.9f, 1.6f);
        private readonly List<Light> studioLights = new List<Light>();
        private readonly List<Vector3> silhouettePoints = new List<Vector3>();
        private readonly List<Vector3> vertexBuffer = new List<Vector3>();
        private readonly List<Light> suspendedLights = new List<Light>();
        private readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly Dictionary<string, IngredientDefinition> definitions = new Dictionary<string, IngredientDefinition>(StringComparer.Ordinal);
        private readonly Queue<string> pending = new Queue<string>();
        private readonly HashSet<string> scheduled = new HashSet<string>(StringComparer.Ordinal);
        private GameObject studio;
        private Camera captureCamera;
        private bool catalogLoaded;
        private bool renderingAll;

        /// <summary>撮影済みの食材 ID を通知します。</summary>
        public event Action<string> IconReady;

        /// <summary>撮影済みアイコンを返します。未撮影の既知食材はキューに登録して null を返します。</summary>
        public Sprite Get(string ingredientId)
        {
            if (string.IsNullOrWhiteSpace(ingredientId)) throw new ArgumentException("食材 ID が必要です。", nameof(ingredientId));
            if (icons.TryGetValue(ingredientId, out Sprite sprite) && sprite != null) return sprite;
            LoadDefinitions();
            if (definitions.ContainsKey(ingredientId) && scheduled.Add(ingredientId)) pending.Enqueue(ingredientId);
            return null;
        }

        /// <summary>食材一覧を登録し、指定件数ずつフレームに分けて未撮影アイコンを生成します。</summary>
        public IEnumerator RenderAll(IEnumerable<IngredientDefinition> items, int perFrame = 4)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (perFrame < 1) throw new ArgumentOutOfRangeException(nameof(perFrame));
            foreach (IngredientDefinition item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id)) throw new ArgumentException("食材定義に ID がありません。", nameof(items));
                definitions[item.id] = item;
                if (!icons.ContainsKey(item.id) && scheduled.Add(item.id)) pending.Enqueue(item.id);
            }
            while (renderingAll) yield return null;
            renderingAll = true;
            try
            {
                while (pending.Count > 0)
                {
                    ProcessQueue(perFrame);
                    yield return null;
                }
            }
            finally { renderingAll = false; }
        }

        /// <summary>自動撮影キューをフレーム毎に処理します。</summary>
        private void Update()
        {
            if (!renderingAll && pending.Count > 0) ProcessQueue(4);
        }

        /// <summary>既定カタログを一度だけ登録します。</summary>
        private void LoadDefinitions()
        {
            if (catalogLoaded) return;
            IngredientCatalog catalog = IngredientCatalog.Load();
            foreach (IngredientDefinition item in catalog.ingredients)
                if (!definitions.ContainsKey(item.id)) definitions.Add(item.id, item);
            catalogLoaded = true;
        }

        /// <summary>指定件数を撮影し、成功した食材を通知します。</summary>
        private void ProcessQueue(int count)
        {
            for (int index = 0; index < count && pending.Count > 0; index++)
            {
                string id = pending.Dequeue();
                scheduled.Remove(id);
                if (icons.ContainsKey(id) || !definitions.TryGetValue(id, out IngredientDefinition definition)) continue;
                try
                {
                    Sprite sprite = Capture(definition);
                    icons.Add(id, sprite);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    continue;
                }
                IconReady?.Invoke(id);
            }
        }

        /// <summary>隔離した撮影カメラと照明を準備します。</summary>
        private void EnsureStudio()
        {
            if (studio != null) return;
            studio = new GameObject("Ingredient Icon Studio") { hideFlags = HideFlags.DontSave, layer = IconLayer };
            try
            {
                InitializeStudio();
            }
            catch
            {
                studio.SetActive(false);
                OwnedObjects.Destroy(studio);
                studio = null;
                captureCamera = null;
                studioLights.Clear();
                throw;
            }
        }

        /// <summary>遅延生成した撮影台に、無効状態のカメラと局所照明を設定します。</summary>
        private void InitializeStudio()
        {
            studio.transform.position = new Vector3(0f, -500f, 0f);
            GameObject cameraObject = new GameObject("Icon Camera") { layer = IconLayer, hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(studio.transform, false);
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.cullingMask = 1 << IconLayer;
            captureCamera.nearClipPlane = 0.01f;
            captureCamera.farClipPlane = 4f;
            captureCamera.allowHDR = true;
            captureCamera.allowMSAA = false;
            UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = true;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            CreateLight("Key", new Color(1f, 0.97f, 0.93f), KeyIlluminance);
            CreateLight("Fill", new Color(0.95f, 0.97f, 1f), FillIlluminance);
            CreateLight("Top", Color.white, TopIlluminance);
            CreateLight("Rim", new Color(0.97f, 0.98f, 1f), RimIlluminance);
        }

        /// <summary>撮影中だけ有効にする影なしの局所スポット光を設定します。</summary>
        private void CreateLight(string lightName, Color color, float intensity)
        {
            GameObject lightObject = new GameObject(lightName) { layer = IconLayer, hideFlags = HideFlags.DontSave };
            lightObject.transform.SetParent(studio.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.enabled = false;
            light.type = LightType.Spot;
            light.spotAngle = 75f;
            light.innerSpotAngle = 45f;
            light.intensity = intensity;
            light.color = color;
            // 0.1 単位の食材ではスポット影の解像度・バイアスが足りず、えのきの柄などが自己影で黒く潰れるため影は使わない。
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << IconLayer;
            light.renderingLayerMask = (int)IconRenderingLayer;
            studioLights.Add(light);
        }

        /// <summary>実頂点の投影範囲で余白を確保し、細長い食材は長軸を対角線に向けて枠いっぱいに収めます。</summary>
        private void FrameModel(Bounds bounds)
        {
            Quaternion rotation = Quaternion.Euler(30f, -45f, 0f);
            Bounds projected = ProjectSilhouette(bounds, rotation);
            float shortSide = Mathf.Max(0.001f, Mathf.Min(projected.size.x, projected.size.y));
            bool slender = Mathf.Max(projected.size.x, projected.size.y) / shortSide > 1.8f;
            if (slender)
            {
                Vector3 majorAxis = bounds.size.y >= bounds.size.x && bounds.size.y >= bounds.size.z
                    ? Vector3.up : bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
                Vector3 projectedAxis = Quaternion.Inverse(rotation) * majorAxis;
                float angle = Mathf.Repeat(Mathf.Atan2(projectedAxis.y, projectedAxis.x) * Mathf.Rad2Deg, 180f);
                float diagonal = angle < 90f ? 45f : 135f;
                rotation *= Quaternion.AngleAxis(angle - diagonal, Vector3.forward);
                projected = ProjectSilhouette(bounds, rotation);
            }
            float radius = Mathf.Max(bounds.extents.magnitude, 0.001f);
            float distance = Mathf.Max(radius * 4f, 0.1f);
            Vector3 projectedCenter = projected.center;
            captureCamera.transform.SetPositionAndRotation(bounds.center + rotation *
                new Vector3(projectedCenter.x, projectedCenter.y, -distance), rotation);
            captureCamera.aspect = 1f;
            float padding = slender ? SlenderPadding : FramePadding;
            captureCamera.orthographicSize = Mathf.Max(0.001f,
                Mathf.Max(projected.extents.y, projected.extents.x / captureCamera.aspect) / (1f - 2f * padding));
            captureCamera.nearClipPlane = Mathf.Max(0.001f, distance + projected.min.z - radius * 0.2f);
            captureCamera.farClipPlane = Mathf.Max(captureCamera.nearClipPlane + 0.01f,
                distance + projected.max.z + radius * 0.2f);
            PositionLight(studioLights[0], bounds.center, KeyDirection, radius, KeyIlluminance);
            PositionLight(studioLights[1], bounds.center, FillDirection, radius, FillIlluminance);
            PositionLight(studioLights[2], bounds.center, TopDirection, radius, TopIlluminance);
            PositionLight(studioLights[3], bounds.center, RimDirection, radius, RimIlluminance);
        }

        /// <summary>撮影モデルの実頂点を食材中心からの相対座標で集めます。読めないメッシュは境界の８頂点で代用します。</summary>
        private void CollectSilhouettePoints(GameObject model, Bounds bounds)
        {
            silhouettePoints.Clear();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                mesh.GetVertices(vertexBuffer);
                foreach (Vector3 vertex in vertexBuffer) silhouettePoints.Add(matrix.MultiplyPoint3x4(vertex) - bounds.center);
            }
            vertexBuffer.Clear();
            if (silhouettePoints.Count > 0) return;
            for (int index = 0; index < 8; index++)
                silhouettePoints.Add(Vector3.Scale(bounds.extents, new Vector3(
                    (index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f)));
        }

        /// <summary>収集した頂点を、食材中心を原点とするカメラ空間へ投影した範囲を返します。</summary>
        private Bounds ProjectSilhouette(Bounds bounds, Quaternion cameraRotation)
        {
            Quaternion inverse = Quaternion.Inverse(cameraRotation);
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            foreach (Vector3 point in silhouettePoints)
            {
                Vector3 projected = inverse * point;
                minimum = Vector3.Min(minimum, projected);
                maximum = Vector3.Max(maximum, projected);
            }
            Bounds result = new Bounds();
            if (silhouettePoints.Count == 0) result.extents = bounds.extents;
            else result.SetMinMax(minimum, maximum);
            return result;
        }

        /// <summary>
        /// 食材寸法に合わせて照明を置き、距離の逆二乗を打ち消して中心の照度を指定値にします。
        /// 照明範囲は距離の数倍にし、URP の範囲端の減衰が照度に掛からないようにします。
        /// </summary>
        private void PositionLight(Light light, Vector3 center, Vector3 cameraOffset, float radius, float illuminance)
        {
            Vector3 offset = captureCamera.transform.rotation * cameraOffset * (radius * 3f);
            float distance = offset.magnitude;
            light.intensity = illuminance * distance * distance;
            light.transform.position = center + offset;
            light.transform.LookAt(center, Vector3.up);
            light.range = distance * 4f;
        }

        /// <summary>他のカメラが撮影台の照明を使用しないよう、同期撮影の前後で有効状態を切り替えます。</summary>
        private void SetStudioLighting(bool enabled)
        {
            foreach (Light light in studioLights)
                if (light != null) light.enabled = enabled;
        }

        /// <summary>
        /// 同期撮影の間だけ、時刻で変わる環境光とシーンの平行光源を撮影用の中立な状態に置き換えます。
        /// 戻り値の状態を <see cref="RestoreSceneLighting"/> に渡して必ず元へ戻します。
        /// </summary>
        private SceneLightingState OverrideSceneLighting()
        {
            SceneLightingState state = new SceneLightingState
            {
                AmbientMode = RenderSettings.ambientMode,
                AmbientLight = RenderSettings.ambientLight,
                AmbientIntensity = RenderSettings.ambientIntensity,
                AmbientProbe = RenderSettings.ambientProbe,
                Fog = RenderSettings.fog
            };
            suspendedLights.Clear();
            // 太陽の cullingMask は撮影レイヤーを除外しているが、URP のメインライトは cullingMask を必ずしも尊重しないため一時的に消す。
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional || !light.enabled || studioLights.Contains(light)) continue;
                light.enabled = false;
                suspendedLights.Add(light);
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = StudioAmbient;
            RenderSettings.ambientIntensity = 1f;
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(StudioAmbient);
            RenderSettings.ambientProbe = probe;
            RenderSettings.fog = false;
            return state;
        }

        /// <summary>撮影前に保存した環境光・霧・平行光源を元に戻します。</summary>
        private void RestoreSceneLighting(SceneLightingState state)
        {
            foreach (Light light in suspendedLights)
                if (light != null) light.enabled = true;
            suspendedLights.Clear();
            RenderSettings.ambientMode = state.AmbientMode;
            RenderSettings.ambientLight = state.AmbientLight;
            RenderSettings.ambientIntensity = state.AmbientIntensity;
            RenderSettings.ambientProbe = state.AmbientProbe;
            RenderSettings.fog = state.Fog;
        }

        /// <summary>撮影中に置き換えるシーン照明設定の退避先。</summary>
        private struct SceneLightingState
        {
            public AmbientMode AmbientMode;
            public Color AmbientLight;
            public float AmbientIntensity;
            public SphericalHarmonicsL2 AmbientProbe;
            public bool Fog;
        }

        /// <summary>食材を撮影し、一時リソースを必ず解放します。</summary>
        private Sprite Capture(IngredientDefinition item)
        {
            EnsureStudio();
            GameObject model = null;
            Texture2D texture = null;
            Texture2D readback = null;
            RenderTexture target = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                model = IngredientVisuals.Build(item, studio.transform);
                foreach (Transform child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = IconLayer;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new InvalidOperationException("食材モデルに描画物がありません。");
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    renderer.renderingLayerMask = IconRenderingLayer;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    bounds.Encapsulate(renderer.bounds);
                }
                CollectSilhouettePoints(model, bounds);
                FrameModel(bounds);
                RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                    ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                target = RenderTexture.GetTemporary(IconSize, IconSize, 24, format, RenderTextureReadWrite.Linear);
                captureCamera.targetTexture = target;
                UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                SceneLightingState sceneLighting = OverrideSceneLighting();
                SetStudioLighting(true);
                try
                {
                    if (RenderPipeline.SupportsRenderRequest(captureCamera, request)) RenderPipeline.SubmitRenderRequest(captureCamera, request);
                    else captureCamera.Render();
                }
                finally
                {
                    SetStudioLighting(false);
                    RestoreSceneLighting(sceneLighting);
                }
                RenderTexture.active = target;
                texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
                { name = "Ingredient Icon " + item.id, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                readback = new Texture2D(IconSize, IconSize, TextureFormat.RGBAFloat, false, true)
                { name = "Ingredient Icon Readback", hideFlags = HideFlags.DontSave };
                readback.ReadPixels(new Rect(0f, 0f, IconSize, IconSize), 0, 0, false);
                Color[] pixels = readback.GetPixels();
                CompositeIcon(pixels);
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, IconSize, IconSize), new Vector2(0.5f, 0.5f), 128f);
                sprite.name = "Ingredient Icon " + item.id;
                sprite.hideFlags = HideFlags.DontSave;
                texture = null;
                return sprite;
            }
            finally
            {
                RenderTexture.active = previous;
                captureCamera.targetTexture = null;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (model != null) { model.SetActive(false); OwnedObjects.Destroy(model); }
                if (texture != null) OwnedObjects.Destroy(texture);
                if (readback != null) OwnedObjects.Destroy(readback);
            }
        }

        /// <summary>
        /// 線形の撮影結果に肩だけのトーンカーブを掛けて本来の色を保ち、透明な外周を残したまま
        /// 白い食材でも明るい背景から輪郭が分かる淡い縁取りと、画面空間の接地影を合成します。
        /// 透明背景へ SrcAlpha 合成された半透明素材は色が α 倍・不透明度が α² になるため、
        /// 不透明度の平方根で α を復元し、色を割り戻してから合成します（不透明画素は α=1 で不変）。
        /// </summary>
        private static void CompositeIcon(Color[] pixels)
        {
            Rect silhouette = FindSilhouetteBounds(pixels);
            if (silhouette.width <= 0f || silhouette.height <= 0f) return;
            float[] outline = OutlineMask(pixels);
            Vector2 center = silhouette.center;
            Vector2 backgroundRadius = new Vector2(Mathf.Max(IconSize * 0.2f, silhouette.width * 0.65f),
                Mathf.Max(IconSize * 0.2f, silhouette.height * 0.65f));
            backgroundRadius = Vector2.Min(backgroundRadius, new Vector2(
                Mathf.Max(1f, Mathf.Min(center.x, IconSize - center.x) - 1f),
                Mathf.Max(1f, Mathf.Min(center.y, IconSize - center.y) - 1f)));
            Vector2 shadowCenter = new Vector2(center.x, silhouette.yMin + silhouette.height * 0.03f);
            Vector2 shadowRadius = new Vector2(Mathf.Max(8f, silhouette.width * 0.46f),
                Mathf.Max(3f, silhouette.height * 0.07f));
            Color shade = new Color(0.3f, 0.25f, 0.21f, 1f).linear;
            bool gammaRendering = QualitySettings.activeColorSpace == ColorSpace.Gamma;
            for (int index = 0; index < pixels.Length; index++)
            {
                Vector2 position = new Vector2(index % IconSize + 0.5f, index / IconSize + 0.5f);
                float backgroundAlpha = EllipticalFalloff(position, center, backgroundRadius) * 0.04f;
                float shadowAlpha = EllipticalFalloff(position, shadowCenter, shadowRadius) * 0.24f;
                float shadeAlpha = 1f - (1f - backgroundAlpha) * (1f - shadowAlpha) * (1f - outline[index] * OutlineAlpha);
                if (shadeAlpha < 0.002f) shadeAlpha = 0f;
                Color source = pixels[index];
                if (gammaRendering) source = source.linear;
                float sourceAlpha = Mathf.Sqrt(Mathf.Clamp01(source.a));
                if (sourceAlpha > 0.0001f)
                {
                    float alphaBefore = source.a;
                    source = ToneMap(source / sourceAlpha);
                    source.a = alphaBefore;
                }
                float visibleShade = shadeAlpha * (1f - sourceAlpha);
                float alpha = sourceAlpha + visibleShade;
                if (alpha <= 0.0001f)
                {
                    pixels[index] = Color.clear;
                    continue;
                }
                Color composite = (source * sourceAlpha + shade * visibleShade) / alpha;
                composite = composite.gamma;
                composite.a = alpha;
                pixels[index] = composite;
            }
        }

        /// <summary>不透明度を約２画素だけ外側へ広げ、食材の外周にだけ残る縁取りの強さ（0〜1）を求めます。</summary>
        private static float[] OutlineMask(Color[] pixels)
        {
            float[] mask = new float[pixels.Length];
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    float own = Mathf.Sqrt(Mathf.Clamp01(pixels[y * IconSize + x].a));
                    if (own >= 0.98f) continue;
                    float spread = 0f;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int sy = y + dy;
                        if (sy < 0 || sy >= IconSize) continue;
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int sx = x + dx;
                            if (sx < 0 || sx >= IconSize || (dx == 0 && dy == 0)) continue;
                            int distance = dx * dx + dy * dy;
                            float weight = distance <= 2 ? 1f : distance <= 5 ? 0.45f : 0f;
                            spread = Mathf.Max(spread, Mathf.Sqrt(Mathf.Clamp01(pixels[sy * IconSize + sx].a)) * weight);
                        }
                    }
                    mask[y * IconSize + x] = spread;
                }
            }
            return mask;
        }

        /// <summary>実際に描画された画素から食材の輪郭範囲を求めます。</summary>
        private static Rect FindSilhouetteBounds(Color[] pixels)
        {
            int minimumX = IconSize;
            int minimumY = IconSize;
            int maximumX = -1;
            int maximumY = -1;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a < 0.02f) continue;
                int x = index % IconSize;
                int y = index / IconSize;
                minimumX = Mathf.Min(minimumX, x);
                minimumY = Mathf.Min(minimumY, y);
                maximumX = Mathf.Max(maximumX, x);
                maximumY = Mathf.Max(maximumY, y);
            }
            return maximumX < minimumX ? new Rect() :
                Rect.MinMaxRect(minimumX, minimumY, maximumX + 1f, maximumY + 1f);
        }

        /// <summary>楕円の外側を完全透明にし、内側の陰影を滑らかに減衰させます。</summary>
        private static float EllipticalFalloff(Vector2 position, Vector2 center, Vector2 radius)
        {
            Vector2 offset = position - center;
            float squaredDistance = offset.x * offset.x / (radius.x * radius.x) +
                offset.y * offset.y / (radius.y * radius.y);
            float falloff = Mathf.Clamp01(1f - squaredDistance);
            return falloff * falloff;
        }

        /// <summary>
        /// 輝度に肩カーブを掛けて色相・彩度を保ったまま明るさだけを丸め、はみ出した成分は個別に丸めます。
        /// 成分ごとに丸めるだけだと明るい緑や黄が白っぽく褪せるため、まず輝度で縮めます。
        /// </summary>
        private static Color ToneMap(Color linear)
        {
            float luminance = 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
            if (luminance > ShoulderStart) linear *= Shoulder(luminance) / luminance;
            return new Color(Shoulder(linear.r), Shoulder(linear.g), Shoulder(linear.b), linear.a);
        }

        /// <summary>
        /// 白い面の照度に相当する値までは線形のまま残し、それを超える分だけを指数的に 1 へ寄せます。
        /// 全域を圧縮すると白が灰色に沈み、圧縮しないと白飛びするため、肩だけを丸めます。
        /// </summary>
        private static float Shoulder(float value)
        {
            float linear = Mathf.Max(0f, value);
            if (linear <= ShoulderStart) return linear;
            float headroom = 1f - ShoulderStart;
            return ShoulderStart + headroom * (1f - Mathf.Exp(-(linear - ShoulderStart) / headroom));
        }

        /// <summary>撮影コルーチン停止時の占有状態を解除します。</summary>
        private void OnDisable()
        {
            StopAllCoroutines();
            renderingAll = false;
            SetStudioLighting(false);
        }

        /// <summary>所有するスプライト、テクスチャ、撮影台を解放します。</summary>
        private void OnDestroy()
        {
            foreach (Sprite sprite in icons.Values)
            {
                if (sprite == null) continue;
                Texture2D texture = sprite.texture;
                OwnedObjects.Destroy(sprite);
                if (texture != null) OwnedObjects.Destroy(texture);
            }
            icons.Clear(); pending.Clear(); scheduled.Clear();
            SetStudioLighting(false);
            if (studio != null)
            {
                studio.SetActive(false);
                OwnedObjects.Destroy(studio);
            }
            studioLights.Clear();
            studio = null;
            captureCamera = null;
        }
    }
}
