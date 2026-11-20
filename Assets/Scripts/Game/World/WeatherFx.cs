using Stratum.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratum.Game
{
    // What the weather looks and sounds like round the player: streaks of rain (or flakes of snow where it's
    // cold) that stop at the first thing they hit, the rain's hiss (muffled under a roof), and in a storm,
    // lightning flashes with thunder a moment later. Rain puts out a burning player standing in it.
    public sealed class WeatherFx : MonoBehaviour
    {
        VoxelWorld _world; Sky _sky; Transform _cam; Player _player;
        ParticleSystem _rain, _snow;
        float _carry, _nextFlash = 8, _thunderIn = -1, _coverCheck;
        float _open;      // 0..1: how much open sky is round the listener

        public static WeatherFx Create(VoxelWorld world, Sky sky, Transform cam, Player player)
        {
            var go = new GameObject("Weather");
            var w = go.AddComponent<WeatherFx>();
            w._world = world; w._sky = sky; w._cam = cam; w._player = player;
            w._rain = w.Make("Rain", new Color(0.7f, 0.78f, 0.9f, 0.55f), true);
            w._snow = w.Make("Snow", new Color(1, 1, 1, 0.95f), false);
            return w;
        }

        ParticleSystem Make(string name, Color col, bool rain)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.maxParticles = rain ? 3000 : 2500;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = col;
            main.startSize = rain ? 0.035f : 0.09f; main.gravityModifier = 0;
            var em = ps.emission; em.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = rain ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r.velocityScale = rain ? 0.045f : 0; r.lengthScale = rain ? 1.2f : 1;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            mat.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        // The first thing a raindrop falling from above would land on in this column (or far below the camera).
        int Roof(int x, int z, int fromY, int toY)
        {
            for (int y = fromY; y > toY; y--)
            {
                var d = Blocks.Get(_world.Get(x, y, z));
                if (d.Solid || d.Fluid != 0 || d.Opaque) return y;
            }
            return toY;
        }

        bool Cold(Vector3 p)
        {
            var b = _world.Gen.BiomeAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
            return b == Biome.Tundra || b == Biome.Taiga || b == Biome.Peaks || p.y > 150;
        }

        bool Dry(Vector3 p) => _world.Gen.BiomeAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z)) == Biome.Desert;

        void Update()
        {
            if (_world == null || _cam == null) return;
            float dt = Time.deltaTime;
            float rain = _sky.Rain;
            var c = _cam.position;
            bool cold = Cold(c), dry = Dry(c);

            // How open is the sky round here? A few columns, now and then.
            if ((_coverCheck -= dt) <= 0)
            {
                _coverCheck = 0.4f;
                int open = 0, n = 0;
                for (int dz = -4; dz <= 4; dz += 4)
                    for (int dx = -4; dx <= 4; dx += 4)
                    {
                        n++;
                        int x = Mathf.FloorToInt(c.x) + dx, z = Mathf.FloorToInt(c.z) + dz, y0 = Mathf.FloorToInt(c.y);
                        if (Roof(x, z, y0 + 30, y0) <= y0) open++;
                    }
                _open = Mathf.Lerp(_open, open / (float)n, 0.6f);
            }
            if (Sfx.I != null) Sfx.I.RainLevel = dry ? 0 : rain * (0.25f + 0.75f * _open) * (cold ? 0.3f : 1);

            // Rain over a burning player puts them out.
            if (rain > 0.3f && !dry && _player != null && _player.Burning > 0)
            {
                var f = _player.Feet;
                if (Roof(Mathf.FloorToInt(f.x), Mathf.FloorToInt(f.z), Mathf.FloorToInt(f.y) + 40, Mathf.FloorToInt(f.y) + 1) <= Mathf.FloorToInt(f.y) + 1) _player.Burning = 0;
            }

            // Drops and flakes.
            if (rain > 0.02f && !dry && !_sky.Underwater)
            {
                var ps = cold ? _snow : _rain;
                float rate = (cold ? 700 : 1600) * rain;
                _carry += rate * dt;
                int count = Mathf.Min(200, (int)_carry); _carry -= count;
                float fall = cold ? 2.2f : 16f;
                for (int i = 0; i < count; i++)
                {
                    float a = Random.value * Mathf.PI * 2, r = Mathf.Sqrt(Random.value) * 22;
                    float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
                    float top = c.y + (cold ? 10 : 14);
                    int roof = Roof(Mathf.FloorToInt(x), Mathf.FloorToInt(z), Mathf.FloorToInt(top), Mathf.FloorToInt(c.y) - 24);
                    float land = roof + 1;
                    if (land >= top - 0.5f) continue;
                    var drift = cold ? new Vector3(Mathf.Sin(Time.time + i) * 0.4f, 0, 0.3f) : new Vector3(0.6f, 0, 0.3f);
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(x, top, z), velocity = new Vector3(drift.x, -fall, drift.z),
                        startLifetime = (top - land) / fall,
                    };
                    ps.Emit(ep, 1);
                }
            }

            // Lightning, and thunder a little after (sound is slower than light).
            if (_sky.Weather == WeatherSim.Thunder && rain > 0.6f)
            {
                if ((_nextFlash -= dt) <= 0)
                {
                    _nextFlash = 6 + Random.value * 18;
                    _sky.Flash = 0.6f + Random.value * 0.4f;
                    _thunderIn = 0.4f + Random.value * 2.5f;
                }
            }
            if (_thunderIn > 0 && (_thunderIn -= dt) <= 0) Sfx.I?.Play("thunder", 0.55f + Random.value * 0.35f, 0.8f + Random.value * 0.4f);
        }

        void OnDestroy() { if (Sfx.I != null) Sfx.I.RainLevel = 0; }
    }
}
