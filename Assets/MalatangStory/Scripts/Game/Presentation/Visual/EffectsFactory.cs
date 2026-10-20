using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>モバイル向けの小粒子数で調理と報酬のエフェクトを構成します。</summary>
    public static class EffectsFactory
    {
        /// <summary>粒子ごとの基準放出量を保持します。</summary>
        private sealed class EmissionSettings
        {
            internal readonly float Rate;
            internal readonly short Burst;
            /// <summary>基準の連続放出量と瞬間放出量を記録します。</summary>
            internal EmissionSettings(float rate, short burst) { Rate = rate; Burst = burst; }
        }

        private static readonly ConditionalWeakTable<ParticleSystem, EmissionSettings> Settings = new ConditionalWeakTable<ParticleSystem, EmissionSettings>();

        /// <summary>ふわっと上昇して消える暖かな湯気を作ります。</summary>
        public static ParticleSystem Steam(Transform parent, float intensity)
        {
            Validate(intensity, nameof(intensity));
            ParticleSystem ps = Create("Steam", parent, Vector3.zero, true, 48, 2.2f, 0.08f, 0.12f, new Color(0.96f, 0.94f, 0.88f, 0.22f), 12f, 0);
            Cone(ps, 0.13f, 12f);
            ParticleSystem.NoiseModule noise = ps.noise; noise.enabled = true; noise.strength = 0.045f; noise.frequency = 0.65f; noise.scrollSpeed = 0.2f; noise.quality = ParticleSystemNoiseQuality.Low;
            SizeCurve(ps, 0.35f, 1.6f);
            SetIntensity(ps, intensity);
            return ps;
        }

        /// <summary>鍋の液面付近で小さく弾ける泡を作ります。</summary>
        public static ParticleSystem Bubbles(Transform parent, float radius)
        {
            Validate(radius, nameof(radius));
            if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius));
            ParticleSystem ps = Create("Bubbles", parent, Vector3.zero, true, 48, 0.65f, 0f, 0.025f, new Color(1f, 0.92f, 0.76f, 0.62f), 24f, 0);
            ParticleSystem.ShapeModule shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radius; shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = MaterialLibrary.Particles(Color.white, TextureFactory.BubbleRing(32));
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeCurve(ps, 0.2f, 1.5f);
            ps.Play();
            return ps;
        }

        /// <summary>店先と屋根の上を漂う、大きめの花びら・落ち葉・白い雪を一つの粒子系で生成します。</summary>
        public static ParticleSystem SeasonalFall(Transform parent, string seasonId)
        {
            bool snow = seasonId == "winter";
            bool petal = seasonId == "spring";
            Color color = snow ? Color.white
                : petal ? new Color(1f, .57f, .73f, 1f) : new Color(.95f, .28f, .035f, 1f);
            ParticleSystem ps = Create("Seasonal Fall", parent, Vector3.zero, true, snow ? 80 : 48,
                5f, 0f, snow ? .16f : .18f, color, snow ? 18f : 10f, 0);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(18f, 1.6f, 11f);
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(.08f, .35f);
            velocity.y = new ParticleSystem.MinMaxCurve(-.7f, -.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.07f, .07f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true; noise.strength = snow ? .06f : .2f;
            noise.frequency = .55f; noise.scrollSpeed = .25f; noise.quality = ParticleSystemNoiseQuality.Low;
            ParticleSystem.MainModule main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            Gradient visibility = new Gradient();
            visibility.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .06f), new GradientAlphaKey(1f, .8f), new GradientAlphaKey(0f, 1f) });
            fade.color = visibility;
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = MaterialLibrary.Particles(Color.white, snow ? TextureFactory.SoftCircle(32) : TextureFactory.FallingLeaf(32, petal));
            ps.Play();
            return ps;
        }

        /// <summary>投入時だけ少量の水滴を出し、低画質では最大八粒に抑えます。</summary>
        public static ParticleSystem IngredientSplash(Vector3 position, bool highQuality)
        {
            short count = (short)(highQuality ? 14 : 8);
            ParticleSystem ps = Create("Ingredient Splash", null, position, false, count, .45f, .38f, .018f,
                new Color(.85f, .93f, 1f, .65f), 0, count);
            Cone(ps, .04f, 58f);
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = .65f;
            ps.Play();
            return ps;
        }

        /// <summary>一度だけ水しぶきを飛ばし、粒子の終了後に自動破棄します。</summary>
        public static ParticleSystem Splash(Vector3 position)
        {
            ParticleSystem ps = Create("Splash", null, position, false, 28, 0.65f, 0.65f, 0.025f, new Color(0.85f, 0.93f, 1f, 0.7f), 0f, 22);
            Cone(ps, 0.035f, 48f);
            ParticleSystem.MainModule main = ps.main; main.gravityModifier = 0.8f;
            ps.Play(); return ps;
        }

        /// <summary>指定色の加算式のきらめきを一度だけ放ちます。</summary>
        public static ParticleSystem Sparkles(Vector3 position, Color color)
        {
            ParticleSystem ps = Create("Sparkles", null, position, false, 24, 0.9f, 0.2f, 0.035f, color, 0f, 18, true);
            SphereShape(ps, 0.08f);
            SizeCurve(ps, 1f, 0f);
            ps.Play(); return ps;
        }

        /// <summary>会計時に金色の粒子が跳ねる報酬演出を作ります。</summary>
        public static ParticleSystem CoinBurst(Vector3 position)
        {
            ParticleSystem ps = Create("Coin Burst", null, position, false, 24, 1f, 0.5f, 0.045f, new Color(1f, 0.72f, 0.16f), 0f, 16);
            Cone(ps, 0.05f, 34f);
            ParticleSystem.MainModule main = ps.main; main.gravityModifier = 0.5f; main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = 4f;
            ps.Play(); return ps;
        }

        /// <summary>柔らかな四色の紙吹雪を少量だけ放ちます。</summary>
        public static ParticleSystem Confetti(Vector3 position)
        {
            ParticleSystem ps = Create("Confetti", null, position, false, 48, 1.8f, 0.6f, 0.035f, Color.white, 0f, 40);
            Cone(ps, 0.1f, 42f);
            Gradient palette = new Gradient();
            palette.SetKeys(new[] { new GradientColorKey(new Color(0.96f, 0.45f, 0.38f), 0f), new GradientColorKey(new Color(1f, 0.8f, 0.28f), 0.33f), new GradientColorKey(new Color(0.46f, 0.78f, 0.65f), 0.67f), new GradientColorKey(new Color(0.54f, 0.68f, 0.9f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            ParticleSystem.MainModule main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(palette) { mode = ParticleSystemGradientMode.RandomColor };
            main.gravityModifier = 0.22f; main.startSize3D = true; main.startSizeX = 0.024f; main.startSizeY = 0.048f; main.startSizeZ = 0.024f;
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
            ParticleSystem.NoiseModule noise = ps.noise; noise.enabled = true; noise.strength = 0.06f; noise.quality = ParticleSystemNoiseQuality.Low;
            ps.Play(); return ps;
        }

        /// <summary>吹きこぼれを知らせる、くすんだ煙を作ります。</summary>
        public static ParticleSystem Smoke(Transform parent)
        {
            ParticleSystem ps = Create("Smoke", parent, Vector3.zero, true, 40, 2f, 0.12f, 0.14f, new Color(0.56f, 0.53f, 0.49f, 0.35f), 14f, 0);
            Cone(ps, 0.1f, 18f); SizeCurve(ps, 0.5f, 1.8f);
            ParticleSystem.NoiseModule noise = ps.noise; noise.enabled = true; noise.strength = 0.065f; noise.quality = ParticleSystemNoiseQuality.Low;
            ps.Play(); return ps;
        }

        /// <summary>基準の放出量に対する倍率を設定します。零は放出停止、最大倍率は三です。</summary>
        public static void SetIntensity(ParticleSystem ps, float intensity)
        {
            if (ps == null) throw new ArgumentNullException(nameof(ps));
            Validate(intensity, nameof(intensity));
            if (!Settings.TryGetValue(ps, out EmissionSettings settings)) throw new ArgumentException("このファクトリーの粒子を指定してください。", nameof(ps));
            float multiplier = Mathf.Clamp(intensity, 0f, 3f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = settings.Rate * multiplier;
            if (settings.Burst > 0)
            {
                short count = (short)Mathf.Min(ps.main.maxParticles, Mathf.RoundToInt(settings.Burst * multiplier));
                emission.SetBursts(count > 0 ? new[] { new ParticleSystem.Burst(0f, count) } : Array.Empty<ParticleSystem.Burst>());
            }
            if (multiplier > 0f && !ps.isPlaying) ps.Play();
            else if (multiplier <= 0f) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>粒子の共通設定とライフサイクルを構成します。</summary>
        private static ParticleSystem Create(string name, Transform parent, Vector3 position, bool loop, int maximum, float lifetime, float speed, float size, Color color, float rate, short burst, bool additive = false)
        {
            GameObject effect = new GameObject(name);
            if (parent != null) { effect.transform.SetParent(parent, false); effect.transform.localPosition = position; }
            else effect.transform.position = position;
            ParticleSystem ps = effect.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.duration = loop ? 4f : 0.15f; main.loop = loop; main.playOnAwake = false;
            main.maxParticles = maximum; main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed); main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
            main.startColor = color; main.simulationSpace = loop ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.stopAction = loop ? ParticleSystemStopAction.None : ParticleSystemStopAction.Destroy;
            ParticleSystem.EmissionModule emission = ps.emission; emission.rateOverTime = rate;
            if (burst > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burst) });
            ParticleSystem.ShapeModule shape = ps.shape; shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime; fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.7f, 0.65f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystemRenderer renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = MaterialLibrary.Particles(Color.white, TextureFactory.SoftCircle(32), additive);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            Settings.Add(ps, new EmissionSettings(rate, burst));
            return ps;
        }

        /// <summary>上方向に放出する円錐形状を設定します。</summary>
        private static void Cone(ParticleSystem ps, float radius, float angle)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.radius = radius; shape.angle = angle;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        /// <summary>球面から放出する形状を設定します。</summary>
        private static void SphereShape(ParticleSystem ps, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = radius;
        }

        /// <summary>寿命に沿って粒子サイズを補間します。</summary>
        private static void SizeCurve(ParticleSystem ps, float start, float end)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, start, 1f, end));
        }

        /// <summary>入力値が有限であることを確認します。</summary>
        private static void Validate(float value, string name)
        { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name); }
    }
}
