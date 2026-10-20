using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MalatangStory.Game
{
    /// <summary>
    /// 営業時間に合わせて太陽・環境光・空の色・提灯の灯りを変化させ、ポストエフェクト（ブルーム等）を設定する。
    /// 11時の明るい昼から、夕焼けを経て、20時には提灯が灯る夜になる。
    /// </summary>
    public sealed class DayLighting
    {
        private readonly Light sun;
        private readonly Camera camera;
        private readonly List<Light> nightLights;
        private readonly List<Renderer> lanterns;
        private readonly List<(Renderer renderer, Color color)> nightEmitters;
        private readonly StreetLife street;
        /// <summary>現在の夜間係数（昼0、夜1）です。</summary>
        public float Night01 { get; private set; }
        private readonly Volume volume;
        private readonly Bloom bloom;
        private readonly ColorAdjustments color;
        private readonly MaterialPropertyBlock block;
        private Color interiorColor = new Color(1f, .62f, .35f);
        private bool highQuality = true;
        private float current = -1;
        private string seasonId;
        private Color seasonalSun, seasonalAmbient, seasonalSky;
        private float seasonalBrightness = 1f, seasonalExposure;

        private static readonly Gradient SunColor = Make(new[] { (0f, new Color(1f, .95f, .86f)), (.55f, new Color(1f, .9f, .75f)), (.8f, new Color(1f, .62f, .38f)), (1f, new Color(.5f, .55f, .9f)) });
        private static readonly Gradient SkyColor = Make(new[] { (0f, new Color(.55f, .75f, .92f)), (.6f, new Color(.62f, .74f, .88f)), (.82f, new Color(.95f, .6f, .45f)), (1f, new Color(.12f, .14f, .28f)) });
        private static readonly Gradient AmbientColor = Make(new[] { (0f, new Color(.78f, .76f, .72f)), (.7f, new Color(.74f, .68f, .62f)), (.85f, new Color(.62f, .5f, .48f)), (1f, new Color(.36f, .36f, .5f)) });

        /// <summary>時刻と色の制御点から補間用のグラデーションを作ります。</summary>
        private static Gradient Make((float t, Color c)[] keys)
        {
            var g = new Gradient();
            var colors = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) colors[i] = new GradientColorKey(keys[i].c, keys[i].t);
            g.SetKeys(colors, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }

        /// <summary>従来の提灯に加えて、色付き夜間発光体と街の車両ライトを受け取ります。</summary>
        public DayLighting(Transform parent, Camera camera, List<Light> nightLights, List<Renderer> lanterns,
            List<(Renderer renderer, Color color)> nightEmitters = null, StreetLife street = null)
        {
            block = new MaterialPropertyBlock();
            this.camera = camera; this.nightLights = nightLights; this.lanterns = lanterns;
            this.nightEmitters = nightEmitters ?? new List<(Renderer, Color)>();
            this.street = street;
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(parent, false);
            sun.type = LightType.Directional; sun.cullingMask = ~(1 << 31); sun.shadows = LightShadows.Soft; sun.shadowStrength = .75f; sun.shadowBias = .04f; sun.shadowNormalBias = .3f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 45; RenderSettings.fogEndDistance = 110;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var volumeObject = new GameObject("Post Processing");
            volumeObject.transform.SetParent(parent, false);
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            bloom = profile.Add<Bloom>(true); bloom.threshold.Override(.95f); bloom.intensity.Override(.45f); bloom.scatter.Override(.65f);
            color = profile.Add<ColorAdjustments>(true); color.saturation.Override(12); color.contrast.Override(8); color.postExposure.Override(.1f);
            var vignette = profile.Add<Vignette>(true); vignette.intensity.Override(.22f); vignette.smoothness.Override(.5f);
            volume.sharedProfile = profile;
        }

        /// <summary>街の灯りには触れず、既存の室内灯二灯と提灯の色味だけを切り替えます。</summary>
        public void ApplyTheme(string themeId)
        {
            interiorColor = StoreTheme.LightColor(themeId);
            for (int i = 0; i < Mathf.Min(2, nightLights.Count); i++)
                if (nightLights[i] != null) nightLights[i].color = interiorColor;
            foreach (Renderer lantern in lanterns)
            {
                if (lantern == null) continue;
                block.Clear();
                lantern.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", interiorColor * Mathf.Lerp(.45f, 3.2f, Night01));
                lantern.SetPropertyBlock(block);
            }
            current = -1;
        }

        /// <summary>昼間の太陽・環境光・空の季節色を設定し、夕焼け以降は既存の時間帯色へ戻します。</summary>
        public void ApplySeason(string value)
        {
            string normalized = value == "summer" || value == "autumn" || value == "winter" ? value : "spring";
            if (seasonId == normalized) return;
            seasonId = normalized;
            switch (normalized)
            {
                case "summer":
                    seasonalSun = new Color(1f, .99f, .88f);
                    seasonalAmbient = new Color(.85f, .84f, .77f);
                    seasonalSky = new Color(.4f, .73f, .97f);
                    seasonalBrightness = 1.2f; seasonalExposure = .12f;
                    break;
                case "autumn":
                    seasonalSun = new Color(1f, .78f, .46f);
                    seasonalAmbient = new Color(.82f, .71f, .53f);
                    seasonalSky = new Color(.73f, .76f, .79f);
                    seasonalBrightness = .98f; seasonalExposure = .02f;
                    break;
                case "winter":
                    seasonalSun = new Color(.7f, .84f, 1f);
                    seasonalAmbient = new Color(.6f, .7f, .86f);
                    seasonalSky = new Color(.56f, .69f, .84f);
                    seasonalBrightness = .92f; seasonalExposure = -.04f;
                    break;
                default:
                    seasonalSun = new Color(1f, .88f, .84f);
                    seasonalAmbient = new Color(.82f, .74f, .7f);
                    seasonalSky = new Color(.66f, .8f, .91f);
                    seasonalBrightness = 1f; seasonalExposure = 0;
                    break;
            }
            current = -1;
        }

        /// <summary>描画品質に応じて影とポストエフェクトを切り替えます。発光色は低品質でも保持します。</summary>
        public void ApplyQuality(bool high)
        {
            highQuality = high;
            sun.shadows = high ? LightShadows.Soft : LightShadows.None;
            volume.enabled = high;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = high;
            current = -1;
        }

        /// <summary>時刻（0=開店11時、1=閉店20時）と段階に応じて照明を更新する。</summary>
        public void Update(float dayFraction, DayPhase phase, bool title)
        {
            float t = title ? .82f : phase == DayPhase.Preparation ? .05f : phase == DayPhase.Report ? 1f : Mathf.Clamp01(dayFraction);
            if (Mathf.Abs(t - current) < .002f) return;
            current = t;
            float elevation = Mathf.Lerp(58, 8, Mathf.SmoothStep(0, 1, t));
            sun.transform.rotation = Quaternion.Euler(elevation, -35 + t * 70, 0);
            float daylight = seasonId == null ? 0 : 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .85f, t));
            sun.color = Color.Lerp(SunColor.Evaluate(t), seasonalSun, daylight * .85f);
            sun.intensity = Mathf.Lerp(1.35f, .45f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1f, t)))
                * Mathf.Lerp(1f, seasonalBrightness, daylight);
            RenderSettings.ambientLight = Color.Lerp(AmbientColor.Evaluate(t), seasonalAmbient, daylight * .65f);
            Color sky = Color.Lerp(SkyColor.Evaluate(t), seasonalSky, daylight * .6f);
            camera.backgroundColor = sky;
            RenderSettings.fogColor = sky;
            float night = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.7f, 1f, t));
            foreach (var light in nightLights) { light.enabled = highQuality && night > .05f; light.intensity = night * 2.2f; }
            foreach (var lantern in lanterns)
            {
                if (lantern == null) continue;
                lantern.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", interiorColor * Mathf.Lerp(.45f, 3.2f, night));
                lantern.SetPropertyBlock(block);
            }
            foreach ((Renderer renderer, Color color) emitter in nightEmitters)
            {
                if (emitter.renderer == null) continue;
                block.Clear();
                emitter.renderer.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", emitter.color * Mathf.Lerp(0, 4.5f, night));
                emitter.renderer.SetPropertyBlock(block);
            }
            Night01 = night;
            if (street != null) street.SetNight(night);
            if (color != null) color.postExposure.Override(Mathf.Lerp(.15f, -.1f, night) + seasonalExposure * daylight);
        }
    }
}
