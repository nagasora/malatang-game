using System;
using System.Collections.Generic;
using System.Text;
using MalatangStory.Ingredients;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 茹で上がった一杯を専用スタジオで斜め上から撮影します。Unity メインスレッド専用です。
    /// 返却画像はこのクラスが所有します。有効な RawImage の使用画像を保護し、未使用画像は LRU で解放します。
    /// 非表示画面の再表示時は Get で画像を再取得してください。
    /// </summary>
    public sealed class BowlPortrait : MonoBehaviour
    {
        private const int ImageSize = 512;
        private const int PortraitLayer = 30;
        private const uint PortraitRenderingLayer = 1u << 8;
        private const int MaxPieces = 10;
        /// <summary>512角RGBA画像の通常予算を約24MiBに制限します。表示中画像の保護による一時超過は許容します。</summary>
        private const int CacheCapacity = 24;
        private const float BowlSize = .3f;
        private const float PieceScale = .8f * 1.35f * (BowlSize / .44f);
        private static readonly Color StudioAmbient = new Color(.3f, .3f, .3f);
        private static readonly Dictionary<string, Texture2D> Images = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly List<string> AccessOrder = new List<string>();
        private static readonly List<Light> StudioLights = new List<Light>();
        private static GameObject studio;
        private static Camera captureCamera;
        private static DepthOfField studioFocus;
        private static IngredientCatalog catalog;
        private static bool capturing;

        /// <summary>
        /// アルバムのスープ・辛さ・具材・調味料に対応する木の卓上背景の画像を返します。
        /// 同じ内容は同じ画像を共有します。呼び出し元で画像を破棄しないでください。
        /// </summary>
        /// <exception cref="ArgumentNullException">記録が null の場合。</exception>
        public static Texture2D Get(AlbumEntry entry, bool includeTable = true)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            return GetPortrait(entry.soup, entry.spice, entry.ingredients, entry.condiments, entry.garnishStrokes, includeTable, entry.bowlStyle);
        }

        /// <summary>
        /// 注文で選んだ具材を茹で上がり状態で撮影します。未選択のスープは注文の希望を使います。
        /// 注文そのものは変更せず、内容の変化した注文は別の画像を取得します。
        /// </summary>
        /// <exception cref="ArgumentNullException">注文が null の場合。</exception>
        public static Texture2D Get(CustomerOrder order)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            string soupId = string.IsNullOrEmpty(order.chosenSoup) ? order.soup : order.chosenSoup;
            return GetPortrait(soupId, order.chosenSpice, order.selected, order.condiments, order.garnishStrokes, true, order.bowlStyle);
        }

        /// <summary>キャッシュ画像と撮影台を破棄します。表示中の UI は破棄後に画像を再取得してください。</summary>
        public static void ReleaseAll()
        {
            if (capturing) throw new InvalidOperationException("一杯の撮影中にはキャッシュを破棄できません。");
            foreach (Texture2D image in Images.Values)
                if (image != null) OwnedObjects.Destroy(image);
            Images.Clear();
            AccessOrder.Clear();
            GameObject previousStudio = studio;
            studio = null;
            captureCamera = null;
            catalog = null;
            StudioLights.Clear();
            if (previousStudio != null)
            {
                previousStudio.SetActive(false);
                OwnedObjects.Destroy(previousStudio);
            }
        }

        /// <summary>ドメイン再読み込みを省略したプレイ開始時も、前回の所有物を解放します。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            ReleaseAll();
            Application.quitting -= ReleaseAll;
            Application.quitting += ReleaseAll;
        }

        /// <summary>撮影台がプレイ終了などで外部から破棄されたときだけ、対応するキャッシュを解放します。</summary>
        private void OnDestroy()
        {
            // Destroy の遅延中に作り直した新しい撮影台を、旧撮影台の破棄通知で解放しない。
            if (ReferenceEquals(studio, gameObject)) ReleaseAll();
        }

        /// <summary>長さ付きの内容キーで画像を再利用し、成功した撮影だけを登録します。</summary>
        private static Texture2D GetPortrait(string soupId, int spice, List<string> ingredients, List<string> condiments, List<GarnishStroke> strokes, bool includeTable = true, string bowlStyle = "")
        {
            if (ingredients == null) throw new ArgumentException("具材一覧がありません。", nameof(ingredients));
            if (condiments == null) throw new ArgumentException("調味料一覧がありません。", nameof(condiments));
            SoupDefinition soup = Content.Soup(soupId);
            StringBuilder keyBuilder = new StringBuilder();
            AppendKey(keyBuilder, soup.Id);
            AppendKey(keyBuilder, spice.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendListKey(keyBuilder, ingredients);
            AppendListKey(keyBuilder, condiments);
            GarnishStroke.AppendKey(keyBuilder, strokes);
            string key = (includeTable ? "photo:" : "cutout:") + bowlStyle + ":" + keyBuilder;
            if (Images.TryGetValue(key, out Texture2D cached) && cached != null)
            {
                TouchCache(key);
                TrimCache(cached, CacheCapacity);
                return cached;
            }
            if (capturing) throw new InvalidOperationException("一杯の撮影を再入呼び出しできません。");
            capturing = true;
            Texture2D image = null;
            try
            {
                TrimCache(null, CacheCapacity - 1);
                EnsureStudio();
                image = Capture(soup, spice, ingredients, condiments, CustomerDirector.StableHash(key), strokes, includeTable, bowlStyle);
                Images[key] = image;
                TouchCache(key);
                return image;
            }
            catch
            {
                if (image != null) OwnedObjects.Destroy(image);
                throw;
            }
            finally { capturing = false; }
        }

        /// <summary>取得した画像を LRU の末尾へ移し、参照順を更新します。</summary>
        private static void TouchCache(string key)
        {
            AccessOrder.Remove(key);
            AccessOrder.Add(key);
        }

        /// <summary>
        /// 有効な RawImage と今回返す画像を保護し、古い未使用画像から指定件数まで解放します。
        /// 全画像が使用中なら表示破壊を避けて一時超過を許し、次の取得時に再度整理します。
        /// </summary>
        private static void TrimCache(Texture2D requested, int limit)
        {
            if (Images.Count <= limit) return;
            HashSet<Texture> protectedImages = new HashSet<Texture>();
            if (requested != null) protectedImages.Add(requested);
            foreach (RawImage image in UnityEngine.Object.FindObjectsByType<RawImage>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (image.isActiveAndEnabled && image.texture != null) protectedImages.Add(image.texture);
            for (int index = 0; index < AccessOrder.Count && Images.Count > limit;)
            {
                string key = AccessOrder[index];
                Texture2D image = Images[key];
                if (image != null && protectedImages.Contains(image)) { index++; continue; }
                AccessOrder.RemoveAt(index);
                Images.Remove(key);
                if (image != null) OwnedObjects.Destroy(image);
            }
        }

        /// <summary>区切り文字を含む ID でも衝突しないよう、文字列の長さを先行させます。</summary>
        private static void AppendKey(StringBuilder builder, string value)
        {
            if (value == null) { builder.Append("-1:"); return; }
            builder.Append(value.Length).Append(':').Append(value);
        }

        /// <summary>並び順と重複を保持したまま一覧をキーへ追加します。</summary>
        private static void AppendListKey(StringBuilder builder, List<string> values)
        {
            builder.Append(values.Count).Append('[');
            foreach (string value in values) AppendKey(builder, value);
            builder.Append(']');
        }

        /// <summary>撮影専用のレイヤー・カメラ・局所照明を遅延生成します。</summary>
        private static void EnsureStudio()
        {
            if (studio != null) return;
            StudioLights.Clear();
            studio = new GameObject("Bowl Portrait Studio") { layer = PortraitLayer, hideFlags = HideFlags.DontSave };
            try
            {
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(studio);
                studio.AddComponent<BowlPortrait>();
                studio.transform.position = new Vector3(0f, -550f, 0f);
                GameObject cameraObject = new GameObject("Bowl Portrait Camera") { layer = PortraitLayer, hideFlags = HideFlags.DontSave };
                cameraObject.transform.SetParent(studio.transform, false);
                captureCamera = cameraObject.AddComponent<Camera>();
                captureCamera.enabled = false;
                captureCamera.orthographic = true;
                captureCamera.aspect = 1f;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = Color.clear;
                captureCamera.cullingMask = 1 << PortraitLayer;
                captureCamera.allowHDR = false;
                captureCamera.allowMSAA = false;
                UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true; data.volumeLayerMask = 1 << PortraitLayer;
                var volumeObject = new GameObject("Food Focus") { layer = PortraitLayer }; volumeObject.transform.SetParent(studio.transform, false);
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                studioFocus = profile.Add<DepthOfField>(true); studioFocus.mode.Override(DepthOfFieldMode.Gaussian); studioFocus.gaussianMaxRadius.Override(.5f);
                var volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 1000; volume.sharedProfile = profile;
                volumeObject.AddComponent<OwnedVolumeProfile>().Profile = profile;
                data.renderShadows = false;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
                CreateLight("Key", new Color(1f, .97f, .93f));
                CreateLight("Fill", new Color(.95f, .97f, 1f));
                CreateLight("Top", Color.white);
                catalog = IngredientCatalog.Load();
            }
            catch
            {
                GameObject failedStudio = studio;
                studio = null;
                captureCamera = null;
                StudioLights.Clear();
                failedStudio.SetActive(false);
                OwnedObjects.Destroy(failedStudio);
                throw;
            }
        }

        /// <summary>小さな具材を自己影で潰さない、撮影中だけ有効なスポット光を作ります。</summary>
        private static void CreateLight(string name, Color color)
        {
            GameObject lightObject = new GameObject(name) { layer = PortraitLayer, hideFlags = HideFlags.DontSave };
            lightObject.transform.SetParent(studio.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.enabled = false;
            light.type = LightType.Spot;
            light.spotAngle = 85f;
            light.innerSpotAngle = 55f;
            light.color = color;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PortraitLayer;
            light.renderingLayerMask = (int)PortraitRenderingLayer;
            StudioLights.Add(light);
        }

        /// <summary>提供用の器と同じ寸法・盛り付け計算で、濡れた茹で上がり具材を配置します。</summary>
        private static void BuildBowl(Transform parent, SoupDefinition soup, int spice, List<string> ingredients, List<string> condiments, int seed, List<GarnishStroke> strokes, string bowlStyle)
        {
            Mesh mesh = MeshFactory.Bowl();
            GameObject bowl = StoreBuilder.Part("Portrait Bowl", mesh, MaterialLibrary.Lit(KitchenStation.Glaze(bowlStyle), bowlStyle == "paper" ? .15f : .8f),
                Vector3.zero, Vector3.one * StoreBuilder.Fit(mesh, BowlSize), parent);
            BowlAccessories.Add(bowl.transform, bowlStyle);
            float soupHeight = mesh.bounds.size.y * .72f;
            float surfaceRadius = soup.Id == "dry" ? .32f : .44f;
            var surface = SoupVisuals.CreateSurface(bowl.transform, soup.Id, spice, surfaceRadius, soupHeight, false, false);
            Transform garnishes = SoupVisuals.BuildGarnishes(bowl.transform, condiments, spice, seed, surfaceRadius, strokes);
            garnishes.localPosition = Vector3.up * (soupHeight + .006f);
            float localScale = PieceScale / Mathf.Max(.0001f, bowl.transform.localScale.x);
            List<PlatingInput> inputs = new List<PlatingInput>();
            List<PlatingPlacement> placements = new List<PlatingPlacement>();
            List<Transform> pieces = new List<Transform>();
            for (int index = 0; index < Mathf.Min(MaxPieces, ingredients.Count); index++)
            {
                IngredientDefinition item = catalog.Get(ingredients[index]);
                GameObject piece = IngredientVisuals.Build(item, bowl.transform, localScale);
                Bounds bounds = piece.TryGetComponent(out BoxCollider box)
                    ? new Bounds(box.center, box.size) : new Bounds(Vector3.zero, Vector3.one * .1f);
                foreach (Collider collider in piece.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                IngredientView view = piece.GetComponent<IngredientView>();
                if (view != null) { view.SetDoneness(1f); view.SetWet(1f); }
                inputs.Add(new PlatingInput { Category = item.category, LocalBounds = bounds, RelativeScale = localScale, WeightGrams = item.portionGrams });
                pieces.Add(piece.transform);
            }
            BowlPlating.Arrange(inputs, soupHeight, seed, placements);
            for (int index = 0; index < pieces.Count; index++)
            {
                PlatingPlacement placement = placements[index];
                pieces[index].SetLocalPositionAndRotation(placement.Position, placement.Rotation);
                pieces[index].localScale = Vector3.one * (localScale * placement.Scale);
                pieces[index].GetComponent<IngredientView>()?.SetSoupLevel(bowl.transform.TransformPoint(Vector3.up * soupHeight).y, new Color(soup.R, soup.G, soup.B));
            }
            SoupVisuals.AddContactOil(surface.transform, pieces);
        }

        /// <summary>全描画物を専用レイヤーへ隔離し、器と具材を含む撮影範囲を返します。</summary>
        private static Bounds IsolateModel(GameObject model)
        {
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PortraitLayer;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("一杯に描画物がありません。");
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                renderer.renderingLayerMask = PortraitRenderingLayer;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        /// <summary>俯角40度で境界を投影し、器の正面軸を維持して斜めAABBによる過剰な左右余白を避けます。</summary>
        private static void FrameModel(Bounds bounds)
        {
            Quaternion rotation = Quaternion.Euler(40f, 0f, 0f);
            Quaternion inverse = Quaternion.Inverse(rotation);
            Bounds projected = new Bounds(Vector3.zero, Vector3.zero);
            for (int index = 0; index < 8; index++)
            {
                Vector3 corner = Vector3.Scale(bounds.extents, new Vector3(
                    (index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                projected.Encapsulate(inverse * corner);
            }
            float radius = Mathf.Max(.01f, bounds.extents.magnitude);
            float distance = radius * 4f;
            captureCamera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
            studioFocus.gaussianStart.Override(distance + projected.max.z + .01f); studioFocus.gaussianEnd.Override(distance + projected.max.z + .25f);
            captureCamera.orthographicSize = Mathf.Max(projected.extents.x, projected.extents.y) / .90f;
            captureCamera.nearClipPlane = Mathf.Max(.001f, distance + projected.min.z - radius * .2f);
            captureCamera.farClipPlane = distance + projected.max.z + radius * .2f;
            PositionLight(StudioLights[0], bounds.center, new Vector3(-.9f, 1.3f, -1.5f), radius, .45f);
            PositionLight(StudioLights[1], bounds.center, new Vector3(1.3f, .4f, -1.3f), radius, .16f);
            PositionLight(StudioLights[2], bounds.center, new Vector3(.1f, 1.8f, .2f), radius, .14f);
        }

        /// <summary>距離の逆二乗と範囲端の減衰を避け、器の大きさによらず一定の照度にします。</summary>
        private static void PositionLight(Light light, Vector3 center, Vector3 direction, float radius, float illuminance)
        {
            Vector3 offset = captureCamera.transform.rotation * direction * (radius * 3f);
            light.transform.position = center + offset;
            light.transform.LookAt(center, Vector3.up);
            light.range = offset.magnitude * 4f;
            light.intensity = illuminance * offset.sqrMagnitude;
        }

        /// <summary>同期撮影し、成功・失敗にかかわらず環境と照明を復元して一時リソースを解放します。</summary>
        private static Texture2D Capture(SoupDefinition soup, int spice, List<string> ingredients, List<string> condiments, int seed, List<GarnishStroke> strokes, bool includeTable, string bowlStyle)
        {
            GameObject model = null;
            RenderTexture target = null;
            Texture2D readback = null;
            Texture2D image = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = captureCamera.targetTexture;
            SceneLightingState lighting = new SceneLightingState();
            TransparentBlendState transparentBlends = null;
            try
            {
                model = new GameObject("Portrait Model") { hideFlags = HideFlags.DontSave };
                model.transform.SetParent(studio.transform, false);
                BuildBowl(model.transform, soup, spice, ingredients, condiments, seed, strokes, bowlStyle);
                Bounds bowlBounds = IsolateModel(model);
                FrameModel(bowlBounds);
                captureCamera.GetUniversalAdditionalCameraData().renderPostProcessing = includeTable;
                if (includeTable)
                {
                Material tableMaterial = MaterialLibrary.Textured(TextureFactory.WoodPlanks(128, new Color(.64f, .43f, .26f), new Color(.36f, .21f, .12f), 737), Color.white, .35f, new Vector2(2, 2));
                var table = StoreBuilder.Part("Photo Table", StoreBuilder.RoundedBox(new Vector3(2, .02f, 2), .008f), tableMaterial, new Vector3(0, -.02f, 0), Vector3.one, model.transform, null, false);
                table.layer = PortraitLayer;
                var tableRenderer = table.GetComponent<Renderer>(); tableRenderer.renderingLayerMask = PortraitRenderingLayer; tableRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                captureCamera.nearClipPlane = .01f; captureCamera.farClipPlane += 2;
                var steam = EffectsFactory.Steam(model.transform, .25f); steam.transform.localPosition = new Vector3(0, bowlBounds.size.y * .5f, 0);
                steam.gameObject.layer = PortraitLayer; steam.GetComponent<Renderer>().renderingLayerMask = PortraitRenderingLayer;
                steam.Simulate(1.2f, true, true);
                transparentBlends = new TransparentBlendState(model);
                target = RenderTexture.GetTemporary(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                captureCamera.targetTexture = target;
                transparentBlends.Apply();
                lighting.Apply();
                foreach (Light light in StudioLights) light.enabled = true;
                UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (RenderPipeline.SupportsRenderRequest(captureCamera, request)) RenderPipeline.SubmitRenderRequest(captureCamera, request);
                else captureCamera.Render();
                RenderTexture.active = target;
                readback = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.DontSave };
                readback.ReadPixels(new Rect(0f, 0f, ImageSize, ImageSize), 0, 0, false);
                Color[] pixels = readback.GetPixels();
                CorrectPixels(pixels);
                image = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false)
                {
                    name = "Bowl Portrait", hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                image.SetPixels(pixels);
                image.Apply(false, true);
                Texture2D result = image;
                image = null;
                return result;
            }
            finally
            {
                try
                {
                    try
                    {
                        if (transparentBlends != null) transparentBlends.Restore();
                    }
                    finally
                    {
                        foreach (Light light in StudioLights)
                            if (light != null) light.enabled = false;
                        lighting.Restore();
                    }
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    if (captureCamera != null) captureCamera.targetTexture = previousTarget;
                    if (target != null) RenderTexture.ReleaseTemporary(target);
                    if (model != null) { model.SetActive(false); OwnedObjects.Destroy(model); }
                    if (readback != null) OwnedObjects.Destroy(readback);
                    if (image != null) OwnedObjects.Destroy(image);
                }
            }
        }

        /// <summary>被覆アルファを維持して乗算済みの色を割り戻し、線形撮影値を UI 用の sRGB へ変換します。</summary>
        private static void CorrectPixels(Color[] pixels)
        {
            bool gammaRendering = QualitySettings.activeColorSpace == ColorSpace.Gamma;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color source = pixels[index];
                float alpha = Mathf.Clamp01(source.a);
                if (alpha < .0001f) { pixels[index] = Color.clear; continue; }
                if (gammaRendering) source = source.linear;
                Color corrected = (source / alpha).gamma;
                corrected.r = Mathf.Clamp01(corrected.r);
                corrected.g = Mathf.Clamp01(corrected.g);
                corrected.b = Mathf.Clamp01(corrected.b);
                corrected.a = alpha;
                pixels[index] = corrected;
            }
        }

        /// <summary>撮影に使う透明共有マテリアルのアルファ合成設定を重複なく退避します。</summary>
        private sealed class TransparentBlendState
        {
            private readonly Dictionary<Material, Tuple<float, float>> originalBlends = new Dictionary<Material, Tuple<float, float>>();

            /// <summary>撮影モデルの透明マテリアルを列挙し、変更前の送出側・宛先側アルファ係数を保存します。</summary>
            internal TransparentBlendState(GameObject model)
            {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null || originalBlends.ContainsKey(material)
                            || !material.HasProperty("_Surface") || material.GetFloat("_Surface") <= 0f
                            || !material.HasProperty("_SrcBlendAlpha") || !material.HasProperty("_DstBlendAlpha")) continue;
                        originalBlends.Add(material, Tuple.Create(material.GetFloat("_SrcBlendAlpha"), material.GetFloat("_DstBlendAlpha")));
                    }
                }
            }

            /// <summary>透明面の重なりを、既存のアルファを上書きしない通常の被覆合成へ切り替えます。</summary>
            internal void Apply()
            {
                // URP Lit の既定の宛先アルファ係数 Zero は重なった透明面の被覆を失わせるため、撮影中だけ置き換える。
                foreach (Material material in originalBlends.Keys)
                {
                    material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                }
            }

            /// <summary>世界側と共有するマテリアルへ、撮影前の両アルファ係数を必ず復元します。</summary>
            internal void Restore()
            {
                foreach (KeyValuePair<Material, Tuple<float, float>> original in originalBlends)
                {
                    if (original.Key == null) continue;
                    original.Key.SetFloat("_SrcBlendAlpha", original.Value.Item1);
                    original.Key.SetFloat("_DstBlendAlpha", original.Value.Item2);
                }
            }
        }

        /// <summary>変更前の環境光・霧・反射と平行光源の有効状態を保持します。</summary>
        private sealed class SceneLightingState
        {
            private readonly AmbientMode ambientMode = RenderSettings.ambientMode;
            private readonly Color ambientLight = RenderSettings.ambientLight;
            private readonly float ambientIntensity = RenderSettings.ambientIntensity;
            private readonly SphericalHarmonicsL2 ambientProbe = RenderSettings.ambientProbe;
            private readonly float reflectionIntensity = RenderSettings.reflectionIntensity;
            private readonly bool fog = RenderSettings.fog;
            private readonly List<Light> suspended = new List<Light>();

            /// <summary>復元用の状態を確定してから、撮影に干渉する有効な平行光源を列挙します。</summary>
            internal SceneLightingState()
            {
                foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (light.type == LightType.Directional && light.enabled) suspended.Add(light);
            }

            /// <summary>撮影用の中立環境を設定します。呼び出しは必ず Restore を含む finally の内側で行います。</summary>
            internal void Apply()
            {
                // URP のメインライトは cullingMask だけでは撮影レイヤーから確実に除外できないため、一時停止する。
                foreach (Light light in suspended) light.enabled = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = StudioAmbient;
                RenderSettings.ambientIntensity = 1f;
                SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
                probe.AddAmbientLight(StudioAmbient);
                RenderSettings.ambientProbe = probe;
                RenderSettings.reflectionIntensity = 0f;
                RenderSettings.fog = false;
            }

            /// <summary>撮影前の環境と、有効だった平行光源をすべて元へ戻します。</summary>
            internal void Restore()
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.ambientProbe = ambientProbe;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.fog = fog;
                foreach (Light light in suspended)
                    if (light != null) light.enabled = true;
            }
        }
    }

}
