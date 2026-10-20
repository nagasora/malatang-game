using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalatangStory.Game
{
    /// <summary>ナビメッシュを使わず、路地の歩行者・車・自転車を決定的なレーンで動かします。</summary>
    public sealed class StreetLife : MonoBehaviour
    {
        /// <summary>歩行レーンと雨天用の傘を保持します。</summary>
        private sealed class Pedestrian
        {
            internal CharacterRig Rig;
            internal Transform Root;
            internal GameObject Umbrella;
            internal Transform Distant;
            internal float Phase;
            internal float Speed;
            internal float LaneZ;
            internal int Direction;
        }

        /// <summary>車両の移動状態と出現間隔を保持します。</summary>
        private sealed class Vehicle
        {
            internal Transform Root;
            internal float Speed;
            internal float LaneZ;
            internal int Direction;
            internal float Delay;
            internal bool Bicycle;
        }

        private readonly List<Pedestrian> pedestrians = new List<Pedestrian>();
        private readonly List<Vehicle> vehicles = new List<Vehicle>();
        private readonly List<(Renderer renderer, Color color)> lamps = new List<(Renderer, Color)>();
        // MaterialPropertyBlock はフィールド初期化子（MonoBehaviour の構築中）では作れないため、初回使用時に作る。
        private MaterialPropertyBlock block;
        private const float HalfStreet = 48f;
        private bool rainy;
        private float night = -1;
        private Camera view;
        private float detailTimer;
        private float streetFront;
        private string fallingSeason;
        private ParticleSystem seasonalParticles;
        private string requestedSeason;
        private string fallingWeather;
        private bool fallingQuality;
        private Transform seasonalPlayer;
        private ParticleSystem.Particle[] seasonalBuffer;
        private bool seasonalOverview;
        private bool seasonalPlacementConfigured;

        /// <summary>八人の通行人、三台の車と時々通る自転車を生成します。</summary>
        public void Initialize(float front, int seed, Camera camera = null)
        {
            if (pedestrians.Count != 0) return;
            streetFront = front;
            view = camera != null ? camera : Camera.main;
            System.Random random = new System.Random(seed);
            for (int i = 0; i < 8; i++)
            {
                int direction = i % 2 == 0 ? 1 : -1;
                float lane = front - (i % 3 == 0 ? 12.3f + i % 2 * .55f : 2.25f + i % 2 * .6f);
                Transform person = new GameObject("Street Pedestrian " + i).transform;
                person.SetParent(transform, false);
                CharacterAppearance appearance = CharacterAppearance.FromSeed(seed + i * 137);
                CharacterRig rig = CharacterFactory.Create(appearance, "Passerby " + i, person);
                Transform distant = BuildDistantPerson(person, rig, appearance);
                distant.gameObject.SetActive(false);
                person.localPosition = new Vector3(-37 + i * 10.1f, 0, lane);
                person.localRotation = Quaternion.Euler(0, direction * 90, 0);
                float speed = .65f + (float)random.NextDouble() * .45f;
                rig.SetMoveSpeed(speed);
                foreach (Renderer renderer in person.GetComponentsInChildren<Renderer>())
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                GameObject umbrella = BuildUmbrella(person, i);
                umbrella.SetActive(false);
                pedestrians.Add(new Pedestrian { Rig = rig, Root = person, Umbrella = umbrella, Distant = distant, Phase = i * .8f, Speed = speed, LaneZ = lane, Direction = direction });
            }
            for (int i = 0; i < 3; i++)
            {
                int direction = i == 1 ? -1 : 1;
                Transform car = new GameObject("City Car " + i).transform;
                car.SetParent(transform, false);
                car.localPosition = new Vector3(-35 + i * 27, 0, front - (direction > 0 ? 5.6f : 9.6f));
                car.localRotation = Quaternion.Euler(0, direction * 90, 0);
                BuildCar(car, i);
                vehicles.Add(new Vehicle { Root = car, Speed = 4.3f + i * .8f, Direction = direction, LaneZ = car.localPosition.z });
            }
            Transform bicycle = new GameObject("Passing Bicycle").transform;
            bicycle.SetParent(transform, false);
            bicycle.localPosition = new Vector3(-HalfStreet, 0, front - 4.35f);
            bicycle.localRotation = Quaternion.Euler(0, 90, 0);
            BuildBicycle(bicycle);
            bicycle.gameObject.SetActive(false);
            vehicles.Add(new Vehicle { Root = bicycle, Speed = 3.1f, Direction = 1, LaneZ = bicycle.localPosition.z, Delay = 8, Bicycle = true });
            SetNight(0);
            UpdateDetail();
        }

        /// <summary>雨では傘を表示して通行人を八人から五人へ減らし、自転車を止めます。</summary>
        public void SetWeather(string weatherId)
        {
            bool rain = weatherId == "rain";
            if (rain == rainy) return;
            rainy = rain;
            for (int i = 0; i < pedestrians.Count; i++)
            {
                pedestrians[i].Root.gameObject.SetActive(!rain || i < 5);
                pedestrians[i].Umbrella.SetActive(rain);
            }
            foreach (Vehicle vehicle in vehicles)
            {
                if (!vehicle.Bicycle) continue;
                vehicle.Root.gameObject.SetActive(false);
                vehicle.Delay = rain ? 0 : 8;
                vehicle.Root.localPosition = new Vector3(-HalfStreet, 0, vehicle.LaneZ);
            }
        }

        /// <summary>一つの粒子系で花びら・落ち葉・寒い冬の雪を切り替え、雨天と低画質では放出を抑えます。</summary>
        public void SetSeason(string seasonId, string weatherId, bool highQuality)
        {
            if (requestedSeason == seasonId && fallingWeather == weatherId && fallingQuality == highQuality) return;
            requestedSeason = seasonId; fallingWeather = weatherId; fallingQuality = highQuality;
            bool snow = seasonId == "winter" && weatherId == "cold";
            string active = seasonId == "spring" || seasonId == "autumn" || snow ? seasonId : "";
            if (active != fallingSeason)
            {
                if (seasonalParticles != null)
                {
                    seasonalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    seasonalParticles.gameObject.SetActive(false);
                    Destroy(seasonalParticles.gameObject);
                    seasonalParticles = null;
                }
                fallingSeason = active;
                if (active.Length > 0)
                {
                    seasonalParticles = EffectsFactory.SeasonalFall(transform, active);
                    seasonalPlacementConfigured = false;
                    seasonalParticles.transform.localPosition = new Vector3(0, 5.3f, streetFront - 2f);
                    seasonalBuffer ??= new ParticleSystem.Particle[80];
                }
            }
            if (seasonalParticles == null) return;
            ParticleSystem.MainModule main = seasonalParticles.main;
            main.maxParticles = snow ? (highQuality ? 80 : 32) : (highQuality ? 48 : 20);
            // 既存の雨と重ねて大量に描画しないため、天気更新時には残留粒子も消す。
            seasonalParticles.Clear(true);
            float intensity = highQuality ? 1f : .4f;
            if (weatherId == "rain") intensity *= .2f;
            EffectsFactory.SetIntensity(seasonalParticles, intensity);
            UpdateSeasonalPlacement();
            seasonalParticles.Simulate(2f, true, true, false);
            seasonalParticles.Play();
        }

        /// <summary>見下ろしカメラとプレイヤーを受け取り、粒子の限られた数を現在の視野へ集中させます。</summary>
        public void SetSeasonContext(Camera camera, Transform player)
        {
            if (camera != null) view = camera;
            seasonalPlayer = player;
            UpdateSeasonalPlacement();
        }

        /// <summary>俯瞰では入口・道路・屋根、一人称ではプレイヤー付近へ放出範囲だけを移し、既存粒子はワールドに残します。</summary>
        private void UpdateSeasonalPlacement()
        {
            if (seasonalParticles == null) return;
            bool overview = view == null || view.transform.position.y > 5f;
            Vector3 center = new Vector3(0, 5.3f, streetFront - 2f);
            if (overview && view != null)
            {
                Ray sight = view.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                Plane ground = new Plane(Vector3.up, transform.position);
                if (ground.Raycast(sight, out float distance))
                    center.x = Mathf.Clamp(transform.InverseTransformPoint(sight.GetPoint(distance)).x, -24f, 24f);
            }
            else if (seasonalPlayer != null)
            {
                Vector3 player = transform.InverseTransformPoint(seasonalPlayer.position);
                center = new Vector3(player.x, Mathf.Max(4.6f, player.y + 4.6f), player.z);
            }
            seasonalParticles.transform.localPosition = center;
            if (!seasonalPlacementConfigured || seasonalOverview != overview)
            {
                ParticleSystem.ShapeModule shape = seasonalParticles.shape;
                shape.scale = overview ? new Vector3(18f, 1.6f, 11f) : new Vector3(9f, 1.3f, 7f);
                seasonalOverview = overview;
                seasonalPlacementConfigured = true;
            }
        }

        /// <summary>切り欠きで屋根を非表示にしても、店舗の屋根より下では雪や花びらを描かず室内への降り込みを防ぎます。</summary>
        private void ClipIndoorSeasonalParticles()
        {
            if (seasonalParticles == null || seasonalBuffer == null) return;
            int count = seasonalParticles.GetParticles(seasonalBuffer);
            bool clipped = false;
            for (int i = 0; i < count; i++)
            {
                Vector3 local = transform.InverseTransformPoint(seasonalBuffer[i].position);
                if (Mathf.Abs(local.x) >= 7.1f || local.z <= streetFront || local.z >= 6f || local.y >= 3.2f) continue;
                seasonalBuffer[i].remainingLifetime = 0;
                clipped = true;
            }
            if (clipped) seasonalParticles.SetParticles(seasonalBuffer, count);
        }

        /// <summary>夜係数を制限して車のヘッドライトと尾灯の発光を補間します。</summary>
        public void SetNight(float night01)
        {
            float value = float.IsNaN(night01) ? 0 : Mathf.Clamp01(night01);
            if (Mathf.Abs(value - night) < .001f) return;
            night = value;
            block ??= new MaterialPropertyBlock();
            foreach ((Renderer renderer, Color color) lamp in lamps)
            {
                if (lamp.renderer == null) continue;
                block.Clear();
                lamp.renderer.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", lamp.color * Mathf.Lerp(0, 4, night));
                lamp.renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>画面外でレーン端へ戻し、非アクティブな雨天歩行者は更新しません。</summary>
        private void Update()
        {
            UpdateSeasonalPlacement();
            ClipIndoorSeasonalParticles();
            float delta = Time.deltaTime;
            detailTimer -= delta;
            if (detailTimer <= 0)
            {
                UpdateDetail();
                detailTimer = .25f;
            }
            foreach (Pedestrian person in pedestrians)
            {
                if (!person.Root.gameObject.activeSelf) continue;
                float x = Wrap(person.Root.localPosition.x + person.Direction * person.Speed * delta);
                person.Root.localPosition = new Vector3(x, 0, person.LaneZ);
                person.Phase += delta * person.Speed * 5f;
                if (person.Distant.gameObject.activeSelf)
                    person.Distant.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(person.Phase)) * .025f, 0);
            }
            foreach (Vehicle vehicle in vehicles)
            {
                if (vehicle.Bicycle && rainy) continue;
                if (vehicle.Delay > 0)
                {
                    vehicle.Delay = Mathf.Max(0, vehicle.Delay - delta);
                    if (vehicle.Delay == 0) vehicle.Root.gameObject.SetActive(true);
                    continue;
                }
                float x = vehicle.Root.localPosition.x + vehicle.Direction * vehicle.Speed * delta;
                if (vehicle.Bicycle && x > HalfStreet)
                {
                    vehicle.Root.gameObject.SetActive(false);
                    vehicle.Delay = 15;
                }
                vehicle.Root.localPosition = new Vector3(Wrap(x), 0, vehicle.LaneZ);
            }
        }

        /// <summary>近距離の二人だけを詳細Rigで描き、俯瞰と遠景は三マテリアルの人影に切り替えます。</summary>
        private void UpdateDetail()
        {
            if (view == null) view = Camera.main;
            Pedestrian first = null, second = null;
            float nearest = 144f, next = 144f;
            if (view != null)
            {
                foreach (Pedestrian person in pedestrians)
                {
                    if (!person.Root.gameObject.activeSelf) continue;
                    float distance = (person.Root.position - view.transform.position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        second = first; next = nearest; first = person; nearest = distance;
                    }
                    else if (distance < next) { second = person; next = distance; }
                }
            }
            foreach (Pedestrian person in pedestrians)
            {
                bool detailed = person == first || person == second;
                person.Rig.gameObject.SetActive(detailed);
                person.Distant.gameObject.SetActive(!detailed);
            }
        }

        /// <summary>近距離Rigの共有色を使って、遠景用の低頂点な頭・服・脚を生成します。</summary>
        private Transform BuildDistantPerson(Transform parent, CharacterRig rig, CharacterAppearance appearance)
        {
            Transform distant = new GameObject("Distant Pedestrian").transform;
            distant.SetParent(parent, false);
            distant.localScale = Vector3.one * Mathf.Clamp(appearance.height, .7f, 1.2f);
            Transform visual = rig.transform.Find("Visual");
            Material top = SampleMaterial(visual != null ? visual.Find("Body") : null, new Color(.4f, .5f, .6f));
            Material skin = SampleMaterial(visual != null ? visual.Find("Body/Head") : null, new Color(.9f, .7f, .55f));
            Material dark = MaterialLibrary.Lit(new Color(.16f, .18f, .22f), .3f);
            using (CityGeometry g = new CityGeometry())
            {
                float width = appearance.build == 2 ? .85f : appearance.build == 3 ? 1.2f : 1f;
                g.Sphere(new Vector3(0, 1.16f, 0), new Vector3(.56f, .56f, .5f), skin);
                g.Sphere(new Vector3(0, 1.32f, -.035f), new Vector3(.57f, .3f, .5f), dark);
                g.Box(new Vector3(0, .78f, 0), new Vector3(.43f * width, .5f, .3f), top);
                for (int side = -1; side <= 1; side += 2)
                {
                    g.Line(new Vector3(side * .11f, .55f, 0), new Vector3(side * .13f, .1f, side * .06f), .075f, dark);
                    g.Line(new Vector3(side * .23f * width, .99f, 0), new Vector3(side * .28f * width, .64f, .05f), .065f, top);
                    g.Sphere(new Vector3(side * .28f * width, .6f, .05f), Vector3.one * .11f, skin);
                }
                if (appearance.headwear != 0)
                    g.Box(new Vector3(0, 1.43f, .025f), new Vector3(.58f, .14f, .53f), top);
                if (appearance.bag != 0)
                    g.Box(new Vector3(0, .84f, -.2f), new Vector3(.3f, .35f, .18f), dark);
                g.Flush(distant, "Distant Person", false);
            }
            return distant;
        }

        /// <summary>Factoryの共有マテリアルを参照し、内部メッシュを読み出さずに遠景の色を合わせます。</summary>
        private static Material SampleMaterial(Transform part, Color fallback)
        {
            if (part != null)
            {
                Renderer renderer = part.GetComponentInChildren<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null) return renderer.sharedMaterial;
            }
            return MaterialLibrary.Lit(fallback, .35f);
        }

        /// <summary>路地の外側で移動距離の余りを保って反対端へ循環させます。</summary>
        private static float Wrap(float x) => Mathf.Repeat(x + HalfStreet, HalfStreet * 2) - HalfStreet;

        /// <summary>傘の柄と低頂点の笠を歩行者の頭上にまとめます。</summary>
        private GameObject BuildUmbrella(Transform parent, int index)
        {
            Transform umbrella = new GameObject("Rain Umbrella").transform;
            umbrella.SetParent(parent, false);
            Material cloth = MaterialLibrary.Lit(index % 2 == 0 ? new Color(.27f, .39f, .61f) : new Color(.76f, .29f, .26f), .3f);
            using (CityGeometry g = new CityGeometry())
            {
                Mesh canopy = MeshFactory.Lathe(new[] { new Vector2(0, .18f), new Vector2(.18f, .15f), new Vector2(.48f, .03f), new Vector2(.68f, -.1f) }, 12);
                g.Add(canopy, new Vector3(0, 1.85f, .05f), Vector3.one, Quaternion.identity, cloth);
                g.Line(new Vector3(.18f, .95f, .1f), new Vector3(.18f, 1.88f, .1f), .013f, MaterialLibrary.Lit(StoreBuilder.Steel, .5f));
                g.Flush(umbrella, "Umbrella", false);
            }
            return umbrella.gameObject;
        }

        /// <summary>車体、窓、タイヤを結合し、ライトだけ色別に制御できるようにします。</summary>
        private void BuildCar(Transform car, int index)
        {
            Color[] colors = { new Color(.85f, .82f, .72f), new Color(.32f, .43f, .52f), new Color(.63f, .21f, .18f) };
            Material body = MaterialLibrary.Lit(colors[index], .65f, .15f);
            Material glass = MaterialLibrary.Lit(new Color(.13f, .22f, .28f), .8f, .25f);
            Material rubber = MaterialLibrary.Lit(new Color(.08f, .09f, .1f), .2f);
            Material head = MaterialLibrary.Emissive(new Color(.88f, .88f, .7f), new Color(1, .94f, .75f), 0);
            Material tail = MaterialLibrary.Emissive(new Color(.65f, .05f, .04f), new Color(1, .08f, .04f), 0);
            using (CityGeometry g = new CityGeometry())
            {
                g.Box(new Vector3(0, .65f, 0), new Vector3(1.55f, .65f, 3.25f), body);
                g.Box(new Vector3(0, 1.08f, -.15f), new Vector3(1.36f, .67f, 1.8f), glass);
                g.Box(new Vector3(0, 1.46f, -.15f), new Vector3(1.4f, .11f, 1.85f), body);
                for (int side = -1; side <= 1; side += 2)
                {
                    g.Box(new Vector3(side * .69f, 1.13f, -.15f), new Vector3(.05f, .6f, .08f), body);
                    g.Box(new Vector3(side * .54f, .78f, 1.64f), new Vector3(.35f, .2f, .04f), head);
                    g.Box(new Vector3(side * .54f, .78f, -1.64f), new Vector3(.32f, .17f, .04f), tail);
                    for (int end = -1; end <= 1; end += 2)
                        g.AddWheelCar(new Vector3(side * .78f, .36f, end * 1.05f), rubber);
                }
                Dictionary<Material, Renderer> renderers = g.Flush(car, "Car", false);
                lamps.Add((renderers[head], new Color(1, .94f, .75f)));
                lamps.Add((renderers[tail], new Color(1, .08f, .04f)));
            }
        }

        /// <summary>自転車と簡素な乗り手を一体化して、歩行者とは異なる街の動きを作ります。</summary>
        private void BuildBicycle(Transform bicycle)
        {
            Material body = MaterialLibrary.Lit(new Color(.2f, .38f, .5f), .4f);
            Material metal = MaterialLibrary.Lit(StoreBuilder.Steel, .5f, .3f);
            Material dark = MaterialLibrary.Lit(StoreBuilder.Charcoal, .3f);
            Material skin = MaterialLibrary.Lit(new Color(.85f, .63f, .47f), .3f);
            using (CityGeometry g = new CityGeometry())
            {
                CityBuilder.AddBicycle(g, Vector3.zero, Quaternion.identity, body, metal, dark);
                g.Sphere(new Vector3(0, 1.39f, -.1f), new Vector3(.28f, .32f, .28f), skin);
                g.Box(new Vector3(0, 1.07f, -.08f), new Vector3(.33f, .43f, .26f), body, Quaternion.Euler(20, 0, 0));
                for (int side = -1; side <= 1; side += 2)
                {
                    g.Line(new Vector3(side * .16f, 1.22f, -.02f), new Vector3(side * .22f, .98f, .37f), .045f, body);
                    g.Line(new Vector3(side * .14f, .85f, -.2f), new Vector3(side * .18f, .55f, .15f), .065f, dark);
                    g.Line(new Vector3(side * .18f, .55f, .15f), new Vector3(side * .14f, .36f, -.06f), .055f, dark);
                }
                g.Flush(bicycle, "Cyclist", false);
            }
        }
    }
}
