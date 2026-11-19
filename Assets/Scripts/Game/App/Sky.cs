using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Stratum.Game
{
    // Day and night: moves the sun and moon, colours the sky, ambient light, fog and clouds, and grades the image.
    public sealed class Sky : MonoBehaviour
    {
        public const float DayLength = 1200f;   // seconds for a full day
        // How bright block light (torches, lamps) is next to its source, in the same units as daylight.
        public static float TorchStrength = 4f;

        public float Time01 = 0.3f;             // 0 midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset
        public bool Frozen;
        public Light Sun { get; private set; }
        Material _sky, _clouds;
        Transform _cloudPlane;
        Volume _volume;
        ColorAdjustments _grade;
        public float ViewDistanceMeters = 160;
        public bool Underwater;
        public int Day = 1;

        static readonly int SkyAmbient = Shader.PropertyToID("_SkyAmbient"), TorchColor = Shader.PropertyToID("_TorchColor"),
            SkyZenith = Shader.PropertyToID("_SkyZenith"), SkyHorizon = Shader.PropertyToID("_SkyHorizon"),
            SunGlow = Shader.PropertyToID("_SunGlow"), SunDir = Shader.PropertyToID("_SunDir"), Night = Shader.PropertyToID("_Night"),
            CloudLit = Shader.PropertyToID("_CloudLit"), CloudShade = Shader.PropertyToID("_CloudShade"),
            CloudCover = Shader.PropertyToID("_CloudCover"), Wind = Shader.PropertyToID("_Wind");

        public static Sky Create()
        {
            var go = new GameObject("Sky");
            var s = go.AddComponent<Sky>();
            s.Build();
            return s;
        }

        void Build()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.92f;
            Sun.shadowBias = 0.06f;
            Sun.shadowNormalBias = 0.5f;
            var add = sunGo.AddComponent<UniversalAdditionalLightData>();
            add.usePipelineSettings = true;
            RenderSettings.sun = Sun;

            _sky = new Material(Shader.Find("Stratum/Sky"));
            RenderSettings.skybox = _sky;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            // Clouds: one big plane high up that follows the camera.
            _clouds = new Material(Shader.Find("Stratum/Clouds")) { mainTexture = CloudMap() };
            var plane = new GameObject("Clouds");
            plane.transform.SetParent(transform, false);
            var mf = plane.AddComponent<MeshFilter>();
            var mr = plane.AddComponent<MeshRenderer>();
            mf.sharedMesh = Quad(3200);
            mr.sharedMaterial = _clouds;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cloudPlane = plane.transform;

            // Colour grading.
            var vgo = new GameObject("Grade");
            vgo.transform.SetParent(transform, false);
            _volume = vgo.AddComponent<Volume>();
            _volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.55f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.65f);
            _grade = profile.Add<ColorAdjustments>(true); _grade.postExposure.Override(0.25f); _grade.saturation.Override(12f); _grade.contrast.Override(8f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.18f); vig.smoothness.Override(0.5f);
            _volume.profile = profile;
        }

        static Mesh Quad(float size)
        {
            var m = new Mesh();
            float h = size / 2;
            m.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(size, 10, size));
            return m;
        }

        static Texture2D CloudMap()
        {
            const int N = 256;
            var p = new Paint(4242);
            var t = new Texture2D(N, N, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x * 64f / N, fy = y * 64f / N;
                    float v = p.Fbm(fx, fy, 4, 5, 0.55f);
                    px[x + y * N] = new Color(v, v, v, 1);
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        static Color Grad(float t, Color night, Color dawn, Color day)
        {
            // t: sun elevation -1..1
            if (t < -0.1f) return night;
            if (t < 0.15f) return Color.Lerp(night, dawn, (t + 0.1f) / 0.25f);
            if (t < 0.4f) return Color.Lerp(dawn, day, (t - 0.15f) / 0.25f);
            return day;
        }

        void Update()
        {
            if (!Frozen)
            {
                Time01 += UnityEngine.Time.deltaTime / DayLength;
                if (Time01 >= 1) { Time01 -= 1; Day++; }
            }
            Apply();
        }

        public float DayFactor { get; private set; }

        public void Apply()
        {
            float ang = (Time01 - 0.25f) * Mathf.PI * 2;           // 0 at sunrise
            var sunDir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0.32f).normalized;   // tilted so noon shadows aren't straight down
            float elev = sunDir.y;
            float day = Mathf.Clamp01((elev + 0.08f) / 0.35f);
            DayFactor = day;
            float night = 1 - Mathf.Clamp01((elev + 0.18f) / 0.2f);

            // The one light is the sun by day and the moon by night.
            bool moon = elev < -0.02f;
            var lightDir = moon ? -sunDir : sunDir;
            Sun.transform.rotation = Quaternion.LookRotation(-lightDir);
            float sunUp = Mathf.Clamp01(elev / 0.25f);
            Color sunCol = Color.Lerp(new Color(1f, 0.55f, 0.3f), new Color(1f, 0.95f, 0.86f), sunUp);
            Sun.color = moon ? new Color(0.55f, 0.65f, 0.9f) : sunCol;
            Sun.intensity = moon ? Mathf.Clamp01((-elev - 0.02f) / 0.2f) * 0.22f : Mathf.Clamp01(elev / 0.08f) * 1.25f;

            Color zenith = Grad(elev, new Color(0.012f, 0.018f, 0.045f), new Color(0.22f, 0.32f, 0.55f), new Color(0.24f, 0.46f, 0.86f));
            Color horizon = Grad(elev, new Color(0.03f, 0.045f, 0.09f), new Color(0.95f, 0.52f, 0.32f), new Color(0.66f, 0.80f, 0.95f));
            Color glow = Grad(elev, Color.black, new Color(1.0f, 0.45f, 0.18f), new Color(0.35f, 0.3f, 0.2f));
            if (Underwater) { zenith = horizon = new Color(0.04f, 0.16f, 0.28f) * (0.25f + day * 0.75f); }

            Color ambient = Color.Lerp(new Color(0.05f, 0.07f, 0.14f), new Color(0.62f, 0.68f, 0.78f), day);
            ambient = Color.Lerp(ambient, new Color(0.75f, 0.55f, 0.45f), Mathf.Clamp01(1 - Mathf.Abs(elev - 0.05f) / 0.15f) * 0.35f);

            Shader.SetGlobalColor(SkyZenith, zenith);
            Shader.SetGlobalColor(SkyHorizon, horizon);
            Shader.SetGlobalColor(SunGlow, glow);
            Shader.SetGlobalVector(SunDir, sunDir);
            Shader.SetGlobalFloat(Night, night);
            Shader.SetGlobalColor(SkyAmbient, ambient);
            Shader.SetGlobalColor(TorchColor, new Color(1.0f, 0.70f, 0.40f) * TorchStrength);
            Shader.SetGlobalFloat(Wind, 1f);
            Shader.SetGlobalColor(CloudLit, Color.Lerp(new Color(0.08f, 0.09f, 0.13f), Color.Lerp(new Color(1f, 0.72f, 0.55f), Color.white, sunUp), day));
            Shader.SetGlobalColor(CloudShade, Color.Lerp(new Color(0.04f, 0.05f, 0.08f), new Color(0.62f, 0.66f, 0.74f), day));
            Shader.SetGlobalFloat(CloudCover, 0.52f);
            RenderSettings.ambientLight = ambient;

            // Fog fades the far edge of the loaded world into the horizon.
            Color fog = Color.Lerp(horizon, zenith, 0.25f);
            RenderSettings.fogColor = fog;
            if (Underwater)
            {
                RenderSettings.fogStartDistance = 0; RenderSettings.fogEndDistance = 28;
            }
            else
            {
                // Fully fogged a chunk and a half inside the loaded edge (fog runs on depth, so the screen's
                // sides see a little further), so the world never visibly ends.
                float end = Mathf.Max(40, ViewDistanceMeters - 24);
                RenderSettings.fogStartDistance = end * 0.42f;
                RenderSettings.fogEndDistance = end;
            }
            if (_grade != null) _grade.postExposure.value = Underwater ? 0.0f : 0.25f;

            var cam = Camera.main;
            if (cam != null && _cloudPlane != null)
            {
                var p = cam.transform.position;
                _cloudPlane.position = new Vector3(p.x, 196, p.z);
                cam.backgroundColor = fog;
            }
        }

        public string Clock()
        {
            float h = (Time01 * 24f + 24f) % 24f;
            int hh = (int)h, mm = (int)((h - hh) * 60);
            return $"Day {Day}, {hh:00}:{mm:00}";
        }
    }
}
