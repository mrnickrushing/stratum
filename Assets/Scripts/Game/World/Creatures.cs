using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratum.Game
{
    // A creature as the game draws it: from the local simulation (single player) or the server's updates.
    public struct MobState
    {
        public int Id; public MobKind Kind; public Vector3 Pos; public float Yaw, Pitch; public byte Flags; public float Health; public byte More;
    }

    // Draws every creature as a blocky model built from boxes (no art files), lit by the world's own light where it
    // stands. Walk cycles follow how fast each one moves; hits flash red, the dying topple over, the burning smoke.
    public sealed class Creatures : MonoBehaviour
    {
        sealed class Model
        {
            public Transform Root, Body, Head, ArmL, ArmR;
            public readonly List<Transform> Legs = new List<Transform>();
            public readonly List<Transform> Wings = new List<Transform>();
            public readonly List<Renderer> Parts = new List<Renderer>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<float> Glows = new List<float>();
            public GameObject Wool, Shorn, Collar;
            public float Swing = 35, Stride = 1;
        }

        sealed class View
        {
            public MobState S;
            public Model M;
            public Vector3 Pos;
            public float Yaw, Pitch, Phase, Speed, Die, Attack, Hurt, Bob, Swell, Sat;
            public Vector2 Light = new Vector2(1, 0);
            public bool Seen;
            public float LightTimer;
        }

        public VoxelWorld World;
        public Transform Cam;
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        Material _mat;
        Mesh _cube;
        MaterialPropertyBlock _mpb;
        ParticleSystem _fire;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), LightId = Shader.PropertyToID("_Light"),
            FlashId = Shader.PropertyToID("_Flash"), GlowId = Shader.PropertyToID("_Glow");

        public static Creatures Create(VoxelWorld world, Transform cam)
        {
            var go = new GameObject("Creatures");
            var c = go.AddComponent<Creatures>();
            c.World = world; c.Cam = cam;
            c._mat = new Material(Shader.Find("Stratum/Avatar")) { name = "Creature" };
            c._cube = Avatars.Cube();
            c._mpb = new MaterialPropertyBlock();
            c.BuildFire();
            return c;
        }

        void BuildFire()
        {
            var go = new GameObject("Flames"); go.transform.SetParent(transform, false);
            _fire = go.AddComponent<ParticleSystem>();
            _fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _fire.main; main.loop = false; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f); main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.gravityModifier = -0.35f; main.maxParticles = 600; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _fire.emission; em.enabled = false;
            var col = _fire.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1, 0.85f, 0.4f), 0), new GradientColorKey(new Color(1, 0.35f, 0.05f), 0.5f), new GradientColorKey(new Color(0.2f, 0.18f, 0.16f), 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = _fire.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = _cube;
            r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ keeping up with the list

        // Called each frame with every creature that should be shown.
        public void Show(List<MobState> list, float dt)
        {
            foreach (var v in _views.Values) v.Seen = false;
            foreach (var s in list)
            {
                if (!_views.TryGetValue(s.Id, out var v))
                {
                    v = new View { S = s, Pos = s.Pos, Yaw = s.Yaw, Pitch = s.Pitch, M = Build(s.Kind) };
                    v.M.Root.SetParent(transform, false);
                    _views[s.Id] = v;
                }
                if ((s.Flags & Mob.FHurt) != 0 && (v.S.Flags & Mob.FHurt) == 0) v.Hurt = 0.35f;
                if ((s.Flags & Mob.FAttack) != 0 && (v.S.Flags & Mob.FAttack) == 0) v.Attack = 1;
                v.S = s; v.Seen = true;
            }
            List<int> gone = null;
            foreach (var kv in _views) if (!kv.Value.Seen) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null) foreach (int id in gone) { Destroy(_views[id].M.Root.gameObject); _views.Remove(id); }
            foreach (var v in _views.Values) Animate(v, dt);
        }

        public void Clear()
        {
            foreach (var v in _views.Values) if (v.M.Root != null) Destroy(v.M.Root.gameObject);
            _views.Clear();
        }

        // The nearest living creature along a ray within reach.
        public bool Raycast(Vector3 o, Vector3 d, float reach, out int id, out float dist)
        {
            id = 0; dist = reach;
            bool hit = false;
            foreach (var v in _views.Values)
            {
                if (v.S.Kind == MobKind.Arrow || (v.S.Flags & Mob.FDead) != 0 || v.S.Id < 0) continue;
                var def = MobSim.Def(v.S.Kind);
                var p = v.Pos;
                float hw = def.HalfW + 0.08f;
                var b = new Core.Box(p.x - hw, p.y, p.z - hw, p.x + hw, p.y + def.Height + 0.05f, p.z + hw);
                float t = Body.RayBox(b, o.x, o.y, o.z, d.x, d.y, d.z);
                if (t >= 0 && t < dist) { dist = t; id = v.S.Id; hit = true; }
            }
            return hit;
        }

        public bool TryGet(int id, out MobState s) { s = default; if (!_views.TryGetValue(id, out var v)) return false; s = v.S; s.Pos = v.Pos; return true; }

        // ------------------------------------------------------------------ moving

        void Animate(View v, float dt)
        {
            var m = v.M; var s = v.S; var def = MobSim.Def(s.Kind);
            var before = v.Pos;
            float k = 1 - Mathf.Exp(-dt * 14);
            if ((v.Pos - s.Pos).sqrMagnitude > 64) v.Pos = s.Pos; else v.Pos = Vector3.Lerp(v.Pos, s.Pos, k);
            v.Yaw = Mathf.LerpAngle(v.Yaw, s.Yaw, k);
            v.Pitch = Mathf.Lerp(v.Pitch, s.Pitch, k);
            var flat = v.Pos - before; flat.y = 0;
            float speed = dt > 0 ? flat.magnitude / dt : 0;
            v.Speed = Mathf.Lerp(v.Speed, speed, 1 - Mathf.Exp(-dt * 8));
            bool dead = (s.Flags & Mob.FDead) != 0;

            if (s.Id < 0) { v.Pos = s.Pos; v.Yaw = s.Yaw; }               // a boat or cart someone's in: no easing
            m.Root.position = v.Pos;
            if (def.Vehicle)
            {
                // Afloat, a boat rocks a little; a cart rolls level.
                float rock = s.Kind == MobKind.Boat ? Mathf.Sin(Time.time * 1.6f + s.Id) * 2.5f : 0;
                m.Root.rotation = Quaternion.Euler(0, v.Yaw, rock);
                v.Hurt = Mathf.Max(0, v.Hurt - dt);
                Light(v, dt);
                Paint(v, v.Hurt > 0 ? 0.4f : 0, 0);
                return;
            }
            if (s.Kind == MobKind.Arrow)
            {
                m.Root.rotation = Quaternion.Euler(v.Pitch, v.Yaw, 0);
                if ((s.Flags & Mob.FStuck) == 0) m.Root.position = s.Pos;   // fast: no easing, or it trails
                Light(v, dt);
                Paint(v, 0, 0);
                return;
            }

            if (dead) v.Die += dt;
            float tip = Mathf.Clamp01(v.Die / 0.35f);
            m.Root.rotation = Quaternion.Euler(0, v.Yaw, 0) * Quaternion.Euler(0, 0, tip * 90);
            if (dead) m.Root.position += Vector3.up * (tip * def.HalfW * 0.6f) - Vector3.up * Mathf.Max(0, v.Die - 0.8f) * 1.5f;

            float walk = Mathf.Clamp01(v.Speed / Mathf.Max(0.8f, def.Speed));
            v.Phase += dt * (3 + v.Speed * 3.2f / m.Stride) * (walk > 0.05f ? 1 : 0);
            float sw = Mathf.Sin(v.Phase) * m.Swing * walk;
            for (int i = 0; i < m.Legs.Count; i++)
            {
                if (s.Kind == MobKind.Crawler)
                {
                    float ph = v.Phase * 1.6f + i * 1.9f;
                    m.Legs[i].localRotation = Quaternion.Euler(0, Mathf.Sin(ph) * 22 * walk, (i % 2 == 0 ? 1 : -1) * (30 + Mathf.Max(0, Mathf.Cos(ph)) * 18 * walk));
                }
                else
                {
                    bool pairA = m.Legs.Count == 4 ? (i == 0 || i == 3) : i % 2 == 0;
                    m.Legs[i].localRotation = Quaternion.Euler(pairA ? sw : -sw, 0, 0);
                }
            }
            v.Attack = Mathf.Max(0, v.Attack - dt * 2.5f);
            float atk = Mathf.Sin(Mathf.Clamp01(1 - v.Attack) * Mathf.PI) * (v.Attack > 0 ? 1 : 0);
            if (m.ArmL != null)
            {
                bool reach = s.Kind == MobKind.Ghoul || s.Kind == MobKind.Colossus || s.Kind == MobKind.King;
                float baseX = s.Kind == MobKind.BoneArcher && (s.Flags & Mob.FAttack) != 0 ? -80 : reach ? -12 : 0;
                m.ArmL.localRotation = Quaternion.Euler(baseX - sw * 0.7f - atk * 70, 0, -4);
                m.ArmR.localRotation = Quaternion.Euler((s.Kind == MobKind.BoneArcher && (s.Flags & Mob.FAttack) != 0 ? -80 : reach ? -12 : 0) + sw * 0.7f - atk * 90, 0, 4);
            }
            if (m.Head != null)
            {
                float nod = s.Kind == MobKind.Chicken ? Mathf.Sin(v.Phase * 2) * 10 * walk : Mathf.Sin(v.Phase * 2) * 3 * walk;
                if (atk > 0 && m.ArmL == null) nod += atk * 25;     // animals and crawlers lunge with the head
                m.Head.localRotation = Quaternion.Euler(nod, 0, 0);
            }
            if (m.Wings.Count > 0)
            {
                bool flap = walk > 0.6f || v.Speed > 2.5f || s.Kind == MobKind.EmberImp;
                float f = flap ? Mathf.Sin(Time.time * 26) * 40 : 0;
                for (int i = 0; i < m.Wings.Count; i++) m.Wings[i].localRotation = Quaternion.Euler(0, 0, (i == 0 ? 1 : -1) * (10 + f));
            }
            if (s.Kind == MobKind.EmberImp) m.Body.localPosition = new Vector3(0, m.Body.localPosition.y, 0) + Vector3.up * (Mathf.Sin(Time.time * 3 + s.Id) * 0.06f);
            if (m.Wool != null) { bool shorn = (s.Flags & Mob.FSheared) != 0; m.Wool.SetActive(!shorn); m.Shorn.SetActive(shorn); }

            // Young ones are small; ones looking for a mate give off little pink sparks.
            float scale = (s.More & Mob.MBaby) != 0 ? 0.55f : 1;
            if (m.Collar != null) { bool tame = (s.More & Mob.MTame) != 0; if (m.Collar.activeSelf != tame) m.Collar.SetActive(tame); }
            // Told to stay: it sits back on its haunches.
            if (s.Kind == MobKind.Hound && m.Body != null)
            {
                bool sit = (s.More & Mob.MSit) != 0;
                v.Sat = Mathf.MoveTowards(v.Sat, sit ? 1 : 0, dt * 5);
                m.Body.localRotation = Quaternion.Euler(-28 * v.Sat, 0, 0);
                m.Body.localPosition = new Vector3(0, 0.42f - 0.12f * v.Sat, -0.06f * v.Sat);
            }
            if (s.Kind == MobKind.King && m.Body != null) m.Body.localPosition = new Vector3(0, 0.6f + Mathf.Sin(Time.time * 1.4f) * 0.16f, 0);
            if (s.Kind == MobKind.Glob) { int size = Mob.JobOf(s.More); scale *= size == 0 ? 1 : size == 1 ? 0.62f : 0.36f; }
            // A swimmer wags its tail end as it goes.
            if (s.Kind == MobKind.Snapper && m.Body != null) m.Body.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 9 + s.Id) * 10, 0);
            if (s.Kind == MobKind.Bloat)
            {
                // About to burst: it swells and shudders.
                v.Swell = Mathf.MoveTowards(v.Swell, (s.Flags & Mob.FAttack) != 0 ? 1 : 0, dt * ((s.Flags & Mob.FAttack) != 0 ? 0.7f : 2f));
                scale *= 1 + v.Swell * 0.4f + Mathf.Sin(Time.time * 38) * 0.035f * v.Swell;
            }
            if (Mathf.Abs(m.Root.localScale.x - scale) > 0.001f) m.Root.localScale = Vector3.one * scale;
            if ((s.More & Mob.MLove) != 0 && Random.value < dt * 4)
            {
                var ep = new ParticleSystem.EmitParams { position = v.Pos + new Vector3(Random.Range(-0.3f, 0.3f), def.Height + 0.2f, Random.Range(-0.3f, 0.3f)),
                    startColor = new Color(1f, 0.35f, 0.55f), startSize = 0.16f, startLifetime = 1.1f, velocity = new Vector3(0, 0.6f, 0) };
                _fire.Emit(ep, 1);
            }

            v.Hurt = Mathf.Max(0, v.Hurt - dt);
            bool burning = (s.Flags & Mob.FBurning) != 0;
            if (burning && Random.value < dt * 30)
            {
                var ep = new ParticleSystem.EmitParams { position = v.Pos + new Vector3(Random.Range(-def.HalfW, def.HalfW), Random.Range(0, def.Height), Random.Range(-def.HalfW, def.HalfW)) };
                _fire.Emit(ep, 1);
            }
            Light(v, dt);
            float flash = dead ? 0.45f : v.Hurt > 0 ? 0.55f : burning ? 0.18f + Mathf.PerlinNoise(Time.time * 8, s.Id) * 0.2f : 0;
            bool hide = dead && v.Die > 1.3f;
            if (m.Root.gameObject.activeSelf == hide) m.Root.gameObject.SetActive(!hide);
            Paint(v, flash, burning ? 1 : 0);
        }

        void Light(View v, float dt)
        {
            if ((v.LightTimer -= dt) > 0 || World == null) return;
            v.LightTimer = 0.12f;
            var def = MobSim.Def(v.S.Kind);
            bool any = false; float sk = 0, bl = 0;
            for (int h = 0; h < 2; h++)
            {
                var q = v.Pos + Vector3.up * (h == 0 ? 0.3f : Mathf.Max(0.5f, def.Height - 0.2f));
                if (!World.LightAt(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y), Mathf.FloorToInt(q.z), out float a, out float b)) continue;
                any = true; sk = Mathf.Max(sk, a); bl = Mathf.Max(bl, b);
            }
            if (def.Fireproof) bl = Mathf.Max(bl, 0.6f);
            if (any) v.Light = Vector2.Lerp(v.Light, new Vector2(sk, bl), 0.5f);
        }

        void Paint(View v, float flash, float fire)
        {
            var m = v.M;
            bool villager = v.S.Kind == MobKind.Villager, sheep = v.S.Kind == MobKind.Sheep;
            Color fleece = default;
            if (sheep) { uint rgb = Blocks.Dyes[Mathf.Clamp(Mob.FleeceOf(v.S.More), 0, Blocks.Dyes.Length - 1)].rgb; fleece = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1); }
            var robe = villager ? JobRobe[Mathf.Clamp(Mob.JobOf(v.S.More), 0, JobRobe.Length - 1)] : default;
            for (int i = 0; i < m.Parts.Count; i++)
            {
                _mpb.SetColor(BaseColor, villager && m.Colors[i] == RobeBase ? robe : sheep && m.Colors[i] == WoolBase ? fleece : m.Colors[i]);
                _mpb.SetVector(LightId, new Vector4(v.Light.x, v.Light.y, 0, 0));
                _mpb.SetColor(FlashId, fire > 0 && v.Hurt <= 0 ? new Color(1, 0.45f, 0.1f, flash) : new Color(0.9f, 0.08f, 0.05f, flash));
                _mpb.SetFloat(GlowId, m.Glows[i]);
                m.Parts[i].SetPropertyBlock(_mpb);
            }
        }

        // ------------------------------------------------------------------ building the models

        Transform Pivot(Transform parent, Vector3 at)
        {
            var t = new GameObject("Pivot").transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        Renderer B(Model m, Transform parent, float x, float y, float z, float w, float h, float d, Color c, float glow = 0, bool shadow = true)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, z);
            go.transform.localScale = new Vector3(w, h, d);
            go.AddComponent<MeshFilter>().sharedMesh = _cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m.Parts.Add(r); m.Colors.Add(c); m.Glows.Add(glow);
            return r;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

        Model Build(MobKind kind)
        {
            var m = new Model { Root = new GameObject(kind.ToString()).transform };
            switch (kind)
            {
                case MobKind.Cow: Cow(m); break;
                case MobKind.Pig: Pig(m); break;
                case MobKind.Sheep: Sheep(m); break;
                case MobKind.Chicken: Chicken(m); break;
                case MobKind.Ghoul: Ghoul(m); break;
                case MobKind.BoneArcher: BoneArcher(m); break;
                case MobKind.Crawler: Crawler(m); break;
                case MobKind.Arrow: Arrow(m); break;
                case MobKind.EmberImp: Imp(m); break;
                case MobKind.Colossus: Colossus(m); break;
                case MobKind.Villager: Villager(m); break;
                case MobKind.Boat: Boat(m); break;
                case MobKind.Cart: Cart(m); break;
                case MobKind.Deer: Deer(m); break;
                case MobKind.Fox: Fox(m); break;
                case MobKind.Rabbit: Rabbit(m); break;
                case MobKind.Penguin: Penguin(m); break;
                case MobKind.Goat: Goat(m); break;
                case MobKind.Camel: Camel(m); break;
                case MobKind.Bloat: Bloat(m); break;
                case MobKind.Hound: Hound(m); break;
                case MobKind.Glob: Glob(m); break;
                case MobKind.Shade: Shade(m); break;
                case MobKind.Snapper: Snapper(m); break;
                case MobKind.King: King(m); break;
                case MobKind.ChestCart: Cart(m); B(m, m.Body, 0, 0.86f, 0, 0.84f, 0.7f, 0.9f, Hex(0x9a6c3c)); B(m, m.Body, 0, 0.98f, 0, 0.88f, 0.1f, 0.94f, Hex(0x3a3c42)); B(m, m.Body, 0, 0.9f, 0.46f, 0.14f, 0.18f, 0.03f, Hex(0xd8b04a), 0, false); break;
            }
            return m;
        }

        void Legs4(Model m, float x, float y, float z, float w, float len, Color c, Color hoof)
        {
            foreach (var (sx, sz) in new[] { (-1, 1), (1, 1), (-1, -1), (1, -1) })
            {
                var p = Pivot(m.Root, new Vector3(sx * x, y, sz * z));
                B(m, p, 0, -len / 2, 0, w, len, w, c);
                B(m, p, 0, -len + 0.04f, 0, w + 0.02f, 0.08f, w + 0.02f, hoof);
                m.Legs.Add(p);
            }
        }

        void Eyes(Model m, Transform head, float x, float y, float z, float size, Color c, float glow = 0)
        {
            B(m, head, -x, y, z, size, size, 0.02f, c, glow, false);
            B(m, head, x, y, z, size, size, 0.02f, c, glow, false);
        }

        void Cow(Model m)
        {
            Color hide = Hex(0x4e3322), patch = Hex(0xece6da), dark = Hex(0x2a1a10);
            Legs4(m, 0.28f, 0.62f, 0.45f, 0.22f, 0.62f, hide, dark);
            m.Body = Pivot(m.Root, new Vector3(0, 0.62f, 0));
            B(m, m.Body, 0, 0.38f, 0, 0.9f, 0.76f, 1.35f, hide);
            B(m, m.Body, 0.452f, 0.42f, 0.15f, 0.02f, 0.42f, 0.5f, patch);
            B(m, m.Body, -0.452f, 0.34f, -0.25f, 0.02f, 0.38f, 0.42f, patch);
            B(m, m.Body, 0.1f, 0.765f, -0.3f, 0.5f, 0.02f, 0.45f, patch);
            B(m, m.Body, 0, 0.0f, -0.25f, 0.28f, 0.1f, 0.3f, Hex(0xe8a0a0));            // udder
            B(m, m.Body, 0, 0.45f, -0.69f, 0.08f, 0.5f, 0.06f, dark);                    // tail
            m.Head = Pivot(m.Root, new Vector3(0, 1.08f, 0.66f));
            B(m, m.Head, 0, 0.04f, 0.2f, 0.56f, 0.56f, 0.44f, hide);
            B(m, m.Head, 0, -0.12f, 0.45f, 0.46f, 0.28f, 0.08f, Hex(0xc8a088));         // muzzle
            B(m, m.Head, -0.1f, -0.1f, 0.495f, 0.06f, 0.08f, 0.02f, dark, 0, false);
            B(m, m.Head, 0.1f, -0.1f, 0.495f, 0.06f, 0.08f, 0.02f, dark, 0, false);
            B(m, m.Head, 0, 0.2f, 0.42f, 0.2f, 0.16f, 0.02f, patch, 0, false);         // blaze
            Eyes(m, m.Head, 0.19f, 0.1f, 0.425f, 0.08f, dark);
            B(m, m.Head, -0.33f, 0.3f, 0.16f, 0.16f, 0.1f, 0.1f, Hex(0xe6dcc4));       // horns
            B(m, m.Head, 0.33f, 0.3f, 0.16f, 0.16f, 0.1f, 0.1f, Hex(0xe6dcc4));
            B(m, m.Head, -0.33f, 0.15f, 0.12f, 0.12f, 0.06f, 0.16f, hide);              // ears
            B(m, m.Head, 0.33f, 0.15f, 0.12f, 0.12f, 0.06f, 0.16f, hide);
        }

        void Deer(Model m)
        {
            Color coat = Hex(0xa8743e), belly = Hex(0xe8dcc4), dark = Hex(0x3a2818), antler = Hex(0xd8c8a0);
            Legs4(m, 0.2f, 0.72f, 0.42f, 0.13f, 0.72f, coat, dark);
            m.Body = Pivot(m.Root, new Vector3(0, 0.72f, 0));
            B(m, m.Body, 0, 0.26f, 0, 0.62f, 0.56f, 1.2f, coat);
            B(m, m.Body, 0, 0.0f, 0, 0.5f, 0.06f, 1.0f, belly);
            B(m, m.Body, 0, 0.42f, -0.63f, 0.16f, 0.2f, 0.08f, belly);                  // white tail
            foreach (var (sx, z) in new[] { (-1, 0.2f), (1, -0.1f), (-1, -0.35f), (1, 0.35f) }) B(m, m.Body, sx * 0.312f, 0.36f, z, 0.02f, 0.08f, 0.08f, belly);   // spots
            m.Head = Pivot(m.Root, new Vector3(0, 1.22f, 0.56f));
            B(m, m.Head, 0, -0.12f, 0.02f, 0.22f, 0.5f, 0.22f, coat);                   // neck
            B(m, m.Head, 0, 0.2f, 0.14f, 0.3f, 0.3f, 0.4f, coat);
            B(m, m.Head, 0, 0.14f, 0.38f, 0.2f, 0.18f, 0.12f, belly);
            B(m, m.Head, 0, 0.16f, 0.445f, 0.1f, 0.07f, 0.02f, dark, 0, false);
            Eyes(m, m.Head, 0.155f, 0.24f, 0.22f, 0.06f, dark);
            foreach (int sx in new[] { -1, 1 })
            {
                B(m, m.Head, sx * 0.2f, 0.3f, 0.0f, 0.14f, 0.07f, 0.1f, coat);           // ears
                B(m, m.Head, sx * 0.1f, 0.5f, 0.02f, 0.05f, 0.34f, 0.05f, antler);
                B(m, m.Head, sx * 0.2f, 0.56f, 0.02f, 0.18f, 0.05f, 0.05f, antler);
                B(m, m.Head, sx * 0.27f, 0.66f, 0.02f, 0.05f, 0.18f, 0.05f, antler);
            }
        }

        void Fox(Model m)
        {
            Color fur = Hex(0xd8702a), white = Hex(0xf2ece0), dark = Hex(0x2a1c14);
            Legs4(m, 0.14f, 0.3f, 0.28f, 0.1f, 0.3f, dark, dark);
            m.Body = Pivot(m.Root, new Vector3(0, 0.3f, 0));
            B(m, m.Body, 0, 0.16f, 0, 0.4f, 0.34f, 0.8f, fur);
            B(m, m.Body, 0, 0.0f, 0.05f, 0.3f, 0.04f, 0.6f, white);
            B(m, m.Body, 0, 0.2f, -0.6f, 0.22f, 0.22f, 0.44f, fur);                    // brush
            B(m, m.Body, 0, 0.2f, -0.86f, 0.18f, 0.18f, 0.12f, white);
            m.Head = Pivot(m.Root, new Vector3(0, 0.52f, 0.4f));
            B(m, m.Head, 0, 0.06f, 0.12f, 0.4f, 0.32f, 0.32f, fur);
            B(m, m.Head, 0, -0.02f, 0.34f, 0.2f, 0.14f, 0.16f, white);
            B(m, m.Head, 0, 0.02f, 0.425f, 0.08f, 0.06f, 0.02f, dark, 0, false);
            Eyes(m, m.Head, 0.11f, 0.1f, 0.285f, 0.06f, dark);
            foreach (int sx in new[] { -1, 1 }) { B(m, m.Head, sx * 0.14f, 0.28f, 0.08f, 0.12f, 0.16f, 0.06f, fur); B(m, m.Head, sx * 0.14f, 0.34f, 0.08f, 0.07f, 0.06f, 0.07f, dark); }
        }

        void Rabbit(Model m)
        {
            Color fur = Hex(0xb8a48a), white = Hex(0xf4f0e8), dark = Hex(0x2a2018);
            Legs4(m, 0.09f, 0.14f, 0.14f, 0.08f, 0.14f, fur, fur);
            m.Body = Pivot(m.Root, new Vector3(0, 0.14f, 0));
            B(m, m.Body, 0, 0.14f, -0.02f, 0.28f, 0.28f, 0.42f, fur);
            B(m, m.Body, 0, 0.16f, -0.25f, 0.12f, 0.12f, 0.08f, white);                // scut
            m.Head = Pivot(m.Root, new Vector3(0, 0.34f, 0.18f));
            B(m, m.Head, 0, 0.06f, 0.08f, 0.24f, 0.22f, 0.24f, fur);
            B(m, m.Head, 0, 0.02f, 0.205f, 0.07f, 0.05f, 0.02f, Hex(0xe8a0a0), 0, false);
            Eyes(m, m.Head, 0.085f, 0.09f, 0.205f, 0.045f, dark);
            foreach (int sx in new[] { -1, 1 }) { B(m, m.Head, sx * 0.07f, 0.3f, 0.02f, 0.07f, 0.28f, 0.05f, fur); B(m, m.Head, sx * 0.07f, 0.3f, 0.047f, 0.04f, 0.22f, 0.01f, Hex(0xe8b8b0), 0, false); }
        }

        void Penguin(Model m)
        {
            Color black = Hex(0x1e2228), white = Hex(0xf2f2ee), orange = Hex(0xf0a020);
            foreach (int sx in new[] { -1, 1 })
            {
                var p = Pivot(m.Root, new Vector3(sx * 0.1f, 0.1f, 0));
                B(m, p, 0, -0.05f, 0, 0.08f, 0.1f, 0.08f, orange);
                B(m, p, 0, -0.09f, 0.06f, 0.14f, 0.02f, 0.18f, orange);
                m.Legs.Add(p);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 0.1f, 0));
            B(m, m.Body, 0, 0.3f, 0, 0.42f, 0.6f, 0.36f, black);
            B(m, m.Body, 0, 0.28f, 0.171f, 0.3f, 0.5f, 0.02f, white);
            foreach (int sx in new[] { -1, 1 })
            {
                var w = Pivot(m.Body, new Vector3(sx * 0.22f, 0.52f, 0));
                B(m, w, sx * 0.02f, -0.18f, 0, 0.05f, 0.4f, 0.24f, black);
                m.Wings.Add(w);
            }
            m.Head = Pivot(m.Root, new Vector3(0, 0.7f, 0.02f));
            B(m, m.Head, 0, 0.12f, 0, 0.34f, 0.28f, 0.32f, black);
            B(m, m.Head, 0, 0.08f, 0.161f, 0.24f, 0.14f, 0.02f, white);
            B(m, m.Head, 0, 0.06f, 0.22f, 0.1f, 0.06f, 0.14f, orange);
            Eyes(m, m.Head, 0.085f, 0.16f, 0.165f, 0.05f, black);
        }

        void Goat(Model m)
        {
            Color coat = Hex(0xe2dccc), dark = Hex(0x5a5248), horn = Hex(0x8a7a60);
            Legs4(m, 0.2f, 0.5f, 0.34f, 0.13f, 0.5f, coat, dark);
            m.Body = Pivot(m.Root, new Vector3(0, 0.5f, 0));
            B(m, m.Body, 0, 0.28f, 0, 0.6f, 0.58f, 1.0f, coat);
            B(m, m.Body, 0, 0.5f, -0.52f, 0.1f, 0.14f, 0.06f, coat);
            m.Head = Pivot(m.Root, new Vector3(0, 0.96f, 0.5f));
            B(m, m.Head, 0, 0.04f, 0.14f, 0.34f, 0.36f, 0.4f, coat);
            B(m, m.Head, 0, -0.06f, 0.36f, 0.22f, 0.18f, 0.08f, Hex(0xcfc6b2));
            B(m, m.Head, 0, -0.24f, 0.3f, 0.08f, 0.2f, 0.06f, coat);                    // beard
            Eyes(m, m.Head, 0.175f, 0.1f, 0.25f, 0.06f, dark);
            foreach (int sx in new[] { -1, 1 })
            {
                B(m, m.Head, sx * 0.22f, 0.12f, 0.04f, 0.14f, 0.06f, 0.1f, coat);
                B(m, m.Head, sx * 0.1f, 0.3f, 0.02f, 0.07f, 0.22f, 0.07f, horn);
                B(m, m.Head, sx * 0.1f, 0.42f, -0.08f, 0.06f, 0.06f, 0.2f, horn);
            }
        }

        void Camel(Model m)
        {
            Color coat = Hex(0xc89a5a), dark = Hex(0x7a5830), light = Hex(0xdcb47a);
            Legs4(m, 0.24f, 1.05f, 0.5f, 0.16f, 1.05f, coat, dark);
            m.Body = Pivot(m.Root, new Vector3(0, 1.05f, 0));
            B(m, m.Body, 0, 0.3f, 0, 0.72f, 0.62f, 1.5f, coat);
            B(m, m.Body, 0, 0.74f, 0.3f, 0.44f, 0.32f, 0.4f, light);                   // two humps
            B(m, m.Body, 0, 0.74f, -0.3f, 0.44f, 0.32f, 0.4f, light);
            B(m, m.Body, 0, 0.3f, -0.78f, 0.07f, 0.44f, 0.06f, dark);
            m.Head = Pivot(m.Root, new Vector3(0, 1.5f, 0.74f));
            B(m, m.Head, 0, 0.26f, 0.04f, 0.24f, 0.8f, 0.24f, coat);                    // the long neck
            B(m, m.Head, 0, 0.72f, 0.2f, 0.3f, 0.3f, 0.5f, coat);
            B(m, m.Head, 0, 0.66f, 0.47f, 0.24f, 0.2f, 0.06f, light);
            B(m, m.Head, -0.06f, 0.68f, 0.505f, 0.04f, 0.05f, 0.02f, dark, 0, false); B(m, m.Head, 0.06f, 0.68f, 0.505f, 0.04f, 0.05f, 0.02f, dark, 0, false);
            Eyes(m, m.Head, 0.155f, 0.78f, 0.3f, 0.06f, Hex(0x201810));
            foreach (int sx in new[] { -1, 1 }) B(m, m.Head, sx * 0.17f, 0.9f, 0.02f, 0.1f, 0.1f, 0.06f, coat);
        }

        // The Hollow King: a towering figure in a tattered dark robe that doesn't reach the ground, a pale mask for a
        // face, long hands, and a crown of gold that glows.
        void King(Model m)
        {
            Color robe = Hex(0x1c1626), trim = Hex(0x4a2e7a), mask = Hex(0xe6e0d2), gold = Hex(0xffd24a), eye = Hex(0xd8a8ff);
            m.Body = Pivot(m.Root, new Vector3(0, 0.6f, 0));
            B(m, m.Body, 0, 1.6f, 0, 1.3f, 1.8f, 0.8f, robe);                              // shoulders and chest
            B(m, m.Body, 0, 0.55f, 0, 1.0f, 1.1f, 0.66f, robe);                            // the robe narrowing
            for (int i = -2; i <= 2; i++) B(m, m.Body, i * 0.2f, -0.1f - (i & 1) * 0.16f, 0, 0.16f, 0.5f, 0.5f, robe);      // tatters
            B(m, m.Body, 0, 1.6f, 0.405f, 0.2f, 1.7f, 0.02f, trim, 0.4f, false);           // a stripe down the front
            B(m, m.Body, 0, 2.42f, 0, 1.5f, 0.14f, 0.9f, trim);                            // a collar
            m.ArmL = Pivot(m.Body, new Vector3(-0.8f, 2.3f, 0));
            B(m, m.ArmL, 0, -0.75f, 0, 0.3f, 1.5f, 0.3f, robe); B(m, m.ArmL, 0, -1.62f, 0, 0.2f, 0.34f, 0.2f, mask);
            m.ArmR = Pivot(m.Body, new Vector3(0.8f, 2.3f, 0));
            B(m, m.ArmR, 0, -0.75f, 0, 0.3f, 1.5f, 0.3f, robe); B(m, m.ArmR, 0, -1.62f, 0, 0.2f, 0.34f, 0.2f, mask);
            m.Head = Pivot(m.Root, new Vector3(0, 3.2f, 0));
            B(m, m.Head, 0, 0.36f, -0.04f, 0.74f, 0.8f, 0.7f, robe);                       // hood
            B(m, m.Head, 0, 0.32f, 0.32f, 0.5f, 0.6f, 0.04f, mask);                        // the mask
            Eyes(m, m.Head, 0.12f, 0.4f, 0.345f, 0.1f, eye, 1.2f);
            B(m, m.Head, 0, 0.82f, 0, 0.82f, 0.12f, 0.78f, gold, 0.8f);                    // crown band
            foreach (var (cx, cz) in new[] { (-0.34f, 0.32f), (0.34f, 0.32f), (-0.34f, -0.32f), (0.34f, -0.32f), (0f, 0.34f) })
                B(m, m.Head, cx, 1.0f, cz, 0.12f, 0.26f, 0.12f, gold, 0.8f);
            m.Swing = 24;
        }

        // The Glob: a cube of green jelly with a darker heart and two wide eyes.
        void Glob(Model m)
        {
            Color jelly = Hex(0x6fcf5a), heart = Hex(0x2f7a34);
            m.Body = Pivot(m.Root, new Vector3(0, 0, 0));
            B(m, m.Body, 0, 0.5f, 0, 1.0f, 1.0f, 1.0f, jelly, 0.15f);
            B(m, m.Body, 0, 0.45f, 0, 0.56f, 0.56f, 0.56f, heart, 0, false);
            m.Head = Pivot(m.Root, new Vector3(0, 0.6f, 0.5f));
            Eyes(m, m.Head, 0.22f, 0.08f, 0.012f, 0.18f, Hex(0xf2f6e0));
            Eyes(m, m.Head, 0.22f, 0.06f, 0.024f, 0.08f, Hex(0x10200c));
            B(m, m.Head, 0, -0.2f, 0.012f, 0.3f, 0.06f, 0.02f, Hex(0x1c3c18), 0, false);
        }

        // The Shade: very tall, very thin, near black, with long arms and eyes that glow violet.
        void Shade(Model m)
        {
            Color dark = Hex(0x15121c), eye = Hex(0xc890ff);
            foreach (int sx in new[] { -1, 1 })
            {
                var leg = Pivot(m.Root, new Vector3(sx * 0.12f, 1.2f, 0));
                B(m, leg, 0, -0.6f, 0, 0.14f, 1.2f, 0.14f, dark);
                m.Legs.Add(leg);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 1.2f, 0));
            B(m, m.Body, 0, 0.45f, 0, 0.42f, 0.9f, 0.24f, dark);
            m.ArmL = Pivot(m.Body, new Vector3(-0.28f, 0.84f, 0));
            B(m, m.ArmL, 0, -0.6f, 0, 0.12f, 1.2f, 0.12f, dark);
            m.ArmR = Pivot(m.Body, new Vector3(0.28f, 0.84f, 0));
            B(m, m.ArmR, 0, -0.6f, 0, 0.12f, 1.2f, 0.12f, dark);
            m.Head = Pivot(m.Root, new Vector3(0, 2.14f, 0));
            B(m, m.Head, 0, 0.2f, 0, 0.4f, 0.4f, 0.4f, dark);
            Eyes(m, m.Head, 0.11f, 0.22f, 0.205f, 0.1f, eye, 1f);
            m.Swing = 30;
        }

        // The Snapper: a deep-bodied grey-green fish with a pale belly, a tall tail and a jaw full of teeth.
        void Snapper(Model m)
        {
            Color back = Hex(0x4a6a62), belly = Hex(0xc8d2c0), fin = Hex(0x2e4640);
            m.Body = Pivot(m.Root, new Vector3(0, 0.25f, 0));
            B(m, m.Body, 0, 0.05f, 0, 0.34f, 0.46f, 0.9f, back);
            B(m, m.Body, 0, -0.1f, 0.05f, 0.3f, 0.2f, 0.7f, belly);
            B(m, m.Body, 0, 0.34f, -0.05f, 0.05f, 0.2f, 0.4f, fin);                         // back fin
            B(m, m.Body, 0, 0.05f, -0.62f, 0.05f, 0.5f, 0.34f, fin);                        // tail
            m.Head = Pivot(m.Root, new Vector3(0, 0.25f, 0.45f));
            B(m, m.Head, 0, 0.08f, 0.14f, 0.3f, 0.22f, 0.3f, back);
            B(m, m.Head, 0, -0.1f, 0.12f, 0.28f, 0.12f, 0.3f, belly);                       // jaw
            for (int i = -1; i <= 1; i++) B(m, m.Head, i * 0.09f, -0.02f, 0.28f, 0.05f, 0.07f, 0.03f, Hex(0xf6f6ee), 0, false);    // teeth
            Eyes(m, m.Head, 0.155f, 0.1f, 0.2f, 0.07f, Hex(0xf2e060));
        }

        // The Hound: a rangy grey dog with a pale muzzle and a brush of a tail; a red collar once it's a friend.
        void Hound(Model m)
        {
            Color coat = Hex(0x6e6a64), pale = Hex(0xcfc8ba), dark = Hex(0x3a3632);
            Legs4(m, 0.15f, 0.42f, 0.3f, 0.13f, 0.42f, coat, dark);
            m.Body = Pivot(m.Root, new Vector3(0, 0.42f, 0));
            B(m, m.Body, 0, 0.17f, 0, 0.4f, 0.36f, 0.86f, coat);
            B(m, m.Body, 0, 0.06f, 0.1f, 0.34f, 0.16f, 0.5f, pale);                        // chest and belly
            B(m, m.Body, 0, 0.32f, -0.52f, 0.12f, 0.12f, 0.34f, coat);                     // tail
            B(m, m.Body, 0, 0.36f, -0.7f, 0.1f, 0.1f, 0.1f, pale);
            m.Head = Pivot(m.Root, new Vector3(0, 0.72f, 0.42f));
            B(m, m.Head, 0, 0.04f, 0.12f, 0.36f, 0.32f, 0.32f, coat);
            B(m, m.Head, 0, -0.04f, 0.36f, 0.2f, 0.16f, 0.2f, pale);                       // muzzle
            B(m, m.Head, 0, 0.0f, 0.47f, 0.08f, 0.07f, 0.03f, dark, 0, false);              // nose
            B(m, m.Head, -0.12f, 0.26f, 0.06f, 0.1f, 0.16f, 0.08f, dark);                  // ears
            B(m, m.Head, 0.12f, 0.26f, 0.06f, 0.1f, 0.16f, 0.08f, dark);
            Eyes(m, m.Head, 0.1f, 0.1f, 0.285f, 0.06f, Hex(0x2a1c10));
            var collar = B(m, m.Body, 0, 0.26f, 0.4f, 0.44f, 0.1f, 0.1f, Hex(0xc8281e));
            m.Collar = collar.gameObject;
        }

        // The Bloat: a swollen, warty bog creature on four stubby legs, with pale eyes and seams that glow.
        void Bloat(Model m)
        {
            Color skin = Hex(0x5e6b3a), belly = Hex(0x8a9058), wart = Hex(0x3e4a28), seam = Hex(0xffa23a);
            Legs4(m, 0.24f, 0.3f, 0.24f, 0.2f, 0.3f, wart, Hex(0x2a3018));
            m.Body = Pivot(m.Root, new Vector3(0, 0.3f, 0));
            B(m, m.Body, 0, 0.42f, 0, 0.86f, 0.84f, 0.86f, skin);
            B(m, m.Body, 0, 0.3f, 0, 0.96f, 0.5f, 0.96f, belly);                       // the bulge
            B(m, m.Body, 0, 0.92f, 0, 0.6f, 0.2f, 0.6f, skin);
            B(m, m.Body, 0, 0.56f, 0.435f, 0.5f, 0.05f, 0.02f, seam, 0.9f, false);     // glowing seams
            B(m, m.Body, 0, 0.3f, 0.485f, 0.7f, 0.05f, 0.02f, seam, 0.9f, false);
            B(m, m.Body, 0.435f, 0.56f, 0, 0.02f, 0.05f, 0.5f, seam, 0.9f, false);
            B(m, m.Body, -0.435f, 0.56f, 0, 0.02f, 0.05f, 0.5f, seam, 0.9f, false);
            foreach (var (wx, wy, wz) in new[] { (0.3f, 0.86f, 0.2f), (-0.26f, 0.88f, -0.18f), (0.1f, 1.04f, -0.1f), (-0.44f, 0.7f, 0.2f), (0.44f, 0.66f, -0.22f) })
                B(m, m.Body, wx, wy, wz, 0.12f, 0.1f, 0.12f, wart);
            m.Head = Pivot(m.Root, new Vector3(0, 1.0f, 0.3f));
            B(m, m.Head, -0.2f, 0.06f, 0.12f, 0.2f, 0.2f, 0.2f, skin);                 // eye stalks
            B(m, m.Head, 0.2f, 0.06f, 0.12f, 0.2f, 0.2f, 0.2f, skin);
            Eyes(m, m.Head, 0.2f, 0.07f, 0.225f, 0.13f, Hex(0xf4f0c8), 0.5f);
            Eyes(m, m.Head, 0.2f, 0.07f, 0.24f, 0.05f, Hex(0x141008));
            B(m, m.Head, 0, -0.2f, 0.135f, 0.44f, 0.05f, 0.02f, Hex(0x1e2410), 0, false);   // a wide mouth
        }

        void Pig(Model m)
        {
            Color skin = Hex(0xe9a3a0), dark = Hex(0xb86a6a);
            Legs4(m, 0.22f, 0.34f, 0.32f, 0.2f, 0.34f, skin, Hex(0x7a4a40));
            m.Body = Pivot(m.Root, new Vector3(0, 0.34f, 0));
            B(m, m.Body, 0, 0.28f, 0, 0.76f, 0.56f, 1.06f, skin);
            B(m, m.Body, 0, 0.45f, -0.55f, 0.1f, 0.1f, 0.06f, dark);                    // curly tail
            m.Head = Pivot(m.Root, new Vector3(0, 0.66f, 0.5f));
            B(m, m.Head, 0, 0.02f, 0.2f, 0.56f, 0.5f, 0.42f, skin);
            B(m, m.Head, 0, -0.04f, 0.44f, 0.3f, 0.2f, 0.08f, Hex(0xf2b4b2));
            B(m, m.Head, -0.06f, -0.04f, 0.482f, 0.05f, 0.07f, 0.02f, dark, 0, false);
            B(m, m.Head, 0.06f, -0.04f, 0.482f, 0.05f, 0.07f, 0.02f, dark, 0, false);
            Eyes(m, m.Head, 0.17f, 0.1f, 0.415f, 0.07f, Hex(0x201010));
            B(m, m.Head, -0.22f, 0.29f, 0.12f, 0.16f, 0.08f, 0.14f, dark);
            B(m, m.Head, 0.22f, 0.29f, 0.12f, 0.16f, 0.08f, 0.14f, dark);
        }

        static readonly Color WoolBase = new Color(0.921f, 0.910f, 0.878f, 1);       // the parts of a sheep that take its dye

        void Sheep(Model m)
        {
            Color wool = WoolBase, face = Hex(0xcbb796), legs = Hex(0x9c8a72);
            Legs4(m, 0.25f, 0.56f, 0.38f, 0.17f, 0.56f, legs, Hex(0x3a3028));
            m.Body = Pivot(m.Root, new Vector3(0, 0.56f, 0));
            m.Wool = new GameObject("Wool"); m.Wool.transform.SetParent(m.Body, false);
            B(m, m.Wool.transform, 0, 0.38f, 0, 0.92f, 0.78f, 1.22f, wool);
            B(m, m.Wool.transform, 0, 0.8f, 0.05f, 0.7f, 0.08f, 0.9f, wool);
            m.Shorn = new GameObject("Shorn"); m.Shorn.transform.SetParent(m.Body, false);
            B(m, m.Shorn.transform, 0, 0.36f, 0, 0.74f, 0.6f, 1.04f, Hex(0xdcc6a8));
            m.Head = Pivot(m.Root, new Vector3(0, 1.08f, 0.6f));
            B(m, m.Head, 0, -0.02f, 0.17f, 0.42f, 0.46f, 0.42f, face);
            B(m, m.Head, 0, 0.21f, 0.1f, 0.48f, 0.14f, 0.36f, wool);
            Eyes(m, m.Head, 0.13f, 0.06f, 0.385f, 0.07f, Hex(0x201810));
            B(m, m.Head, 0, -0.14f, 0.385f, 0.14f, 0.05f, 0.02f, Hex(0x5a4436), 0, false);
            B(m, m.Head, -0.27f, 0.08f, 0.12f, 0.16f, 0.07f, 0.12f, face);
            B(m, m.Head, 0.27f, 0.08f, 0.12f, 0.16f, 0.07f, 0.12f, face);
        }

        void Chicken(Model m)
        {
            Color white = Hex(0xf2efe8), orange = Hex(0xe8a02a);
            foreach (int sx in new[] { -1, 1 })
            {
                var p = Pivot(m.Root, new Vector3(sx * 0.09f, 0.26f, 0));
                B(m, p, 0, -0.13f, 0, 0.05f, 0.26f, 0.05f, orange);
                B(m, p, 0, -0.25f, 0.05f, 0.12f, 0.02f, 0.14f, orange);
                m.Legs.Add(p);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 0.26f, 0));
            B(m, m.Body, 0, 0.2f, -0.02f, 0.38f, 0.34f, 0.5f, white);
            B(m, m.Body, 0, 0.36f, -0.28f, 0.22f, 0.2f, 0.08f, white);                 // tail
            foreach (int sx in new[] { -1, 1 })
            {
                var w = Pivot(m.Body, new Vector3(sx * 0.2f, 0.32f, 0));
                B(m, w, sx * 0.02f, -0.1f, -0.02f, 0.05f, 0.24f, 0.36f, Hex(0xe0dcd2));
                m.Wings.Add(w);
            }
            m.Head = Pivot(m.Root, new Vector3(0, 0.6f, 0.2f));
            B(m, m.Head, 0, 0.1f, 0.03f, 0.24f, 0.3f, 0.2f, white);
            B(m, m.Head, 0, 0.12f, 0.17f, 0.14f, 0.07f, 0.1f, orange);
            B(m, m.Head, 0, 0.02f, 0.14f, 0.07f, 0.1f, 0.05f, Hex(0xc8241e));
            B(m, m.Head, 0, 0.27f, 0.04f, 0.06f, 0.06f, 0.14f, Hex(0xc8241e));
            Eyes(m, m.Head, 0.08f, 0.18f, 0.135f, 0.05f, Hex(0x101010));
        }

        // A hunched, long-armed walker with grey-green skin, a ragged tunic and pale lamp eyes.
        void Ghoul(Model m)
        {
            Color skin = Hex(0x6d7d5e), tunic = Hex(0x3b3946), trousers = Hex(0x2a2a30), claw = Hex(0x3a3a2a);
            foreach (int sx in new[] { -1, 1 })
            {
                var p = Pivot(m.Root, new Vector3(sx * 0.13f, 0.8f, 0));
                B(m, p, 0, -0.4f, 0, 0.2f, 0.8f, 0.22f, trousers);
                B(m, p, 0, -0.76f, 0.02f, 0.21f, 0.08f, 0.26f, skin);
                m.Legs.Add(p);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 0.8f, 0));
            m.Body.localRotation = Quaternion.Euler(16, 0, 0);
            B(m, m.Body, 0, 0.34f, 0, 0.5f, 0.68f, 0.28f, tunic);
            B(m, m.Body, -0.12f, -0.02f, 0.01f, 0.18f, 0.12f, 0.29f, tunic);            // ragged hem
            B(m, m.Body, 0.14f, -0.05f, 0.01f, 0.12f, 0.16f, 0.29f, tunic);
            B(m, m.Body, 0.05f, 0.42f, 0.145f, 0.16f, 0.2f, 0.01f, skin, 0, false);    // a tear in the tunic
            m.Head = Pivot(m.Body, new Vector3(0, 0.7f, 0.06f));
            B(m, m.Head, 0, 0.22f, 0.04f, 0.44f, 0.46f, 0.44f, skin);
            B(m, m.Head, 0, 0.42f, -0.02f, 0.46f, 0.08f, 0.46f, Hex(0x3a3a2e));          // lank hair
            Eyes(m, m.Head, 0.11f, 0.27f, 0.265f, 0.09f, Hex(0xf2e47a), 2.2f);
            B(m, m.Head, 0, 0.1f, 0.265f, 0.2f, 0.05f, 0.01f, Hex(0x1a1a14), 0, false);
            m.ArmL = Pivot(m.Body, new Vector3(-0.34f, 0.62f, 0));
            B(m, m.ArmL, 0, -0.4f, 0, 0.16f, 0.84f, 0.16f, skin);
            B(m, m.ArmL, 0, -0.84f, 0.04f, 0.18f, 0.08f, 0.22f, claw);
            m.ArmR = Pivot(m.Body, new Vector3(0.34f, 0.62f, 0));
            B(m, m.ArmR, 0, -0.4f, 0, 0.16f, 0.84f, 0.16f, skin);
            B(m, m.ArmR, 0, -0.84f, 0.04f, 0.18f, 0.08f, 0.22f, claw);
            m.Swing = 28;
        }

        // Bones held together by nothing much, with cold blue sparks in the eye holes and a bow.
        void BoneArcher(Model m)
        {
            Color bone = Hex(0xe0dbcc), shade = Hex(0xb8b2a2), hole = Hex(0x14141a);
            foreach (int sx in new[] { -1, 1 })
            {
                var p = Pivot(m.Root, new Vector3(sx * 0.12f, 0.86f, 0));
                B(m, p, 0, -0.43f, 0, 0.11f, 0.86f, 0.11f, bone);
                m.Legs.Add(p);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 0.86f, 0));
            B(m, m.Body, 0, 0.02f, 0, 0.36f, 0.1f, 0.16f, shade);                      // pelvis
            B(m, m.Body, 0, 0.32f, -0.05f, 0.1f, 0.6f, 0.08f, shade);                  // spine
            for (int i = 0; i < 4; i++) B(m, m.Body, 0, 0.36f + i * 0.1f, 0.01f, 0.42f - i * 0.02f, 0.05f, 0.22f, bone);
            m.Head = Pivot(m.Body, new Vector3(0, 0.72f, 0));
            B(m, m.Head, 0, 0.22f, 0, 0.42f, 0.4f, 0.42f, bone);
            B(m, m.Head, 0, 0.0f, 0.04f, 0.32f, 0.1f, 0.3f, shade);                    // jaw
            Eyes(m, m.Head, 0.1f, 0.24f, 0.212f, 0.11f, hole);
            Eyes(m, m.Head, 0.1f, 0.24f, 0.222f, 0.04f, Hex(0x8ad0ff), 2.5f);
            B(m, m.Head, 0, 0.12f, 0.212f, 0.06f, 0.06f, 0.01f, hole, 0, false);
            m.ArmL = Pivot(m.Body, new Vector3(-0.27f, 0.62f, 0));
            B(m, m.ArmL, 0, -0.36f, 0, 0.09f, 0.74f, 0.09f, bone);
            B(m, m.ArmL, 0, -0.72f, 0.12f, 0.05f, 0.9f, 0.07f, Hex(0x6a4424));        // bow
            B(m, m.ArmL, 0, -0.72f, 0.06f, 0.01f, 0.86f, 0.01f, Hex(0xd8d4c8), 0, false);
            m.ArmR = Pivot(m.Body, new Vector3(0.27f, 0.62f, 0));
            B(m, m.ArmR, 0, -0.36f, 0, 0.09f, 0.74f, 0.09f, bone);
            m.Swing = 30;
        }

        // A dark, six-legged cave beetle with a violet sheen and a cluster of red eyes.
        void Crawler(Model m)
        {
            Color shell = Hex(0x2a2234), sheen = Hex(0x4a3a62), legC = Hex(0x1c1820);
            m.Body = Pivot(m.Root, new Vector3(0, 0.42f, 0));
            B(m, m.Body, 0, 0.1f, -0.32f, 1.0f, 0.56f, 0.86f, shell);
            B(m, m.Body, 0, 0.39f, -0.32f, 0.7f, 0.04f, 0.6f, sheen);
            B(m, m.Body, 0, 0.36f, -0.32f, 0.04f, 0.04f, 0.84f, Hex(0x120e16), 0, false);   // where the wing cases meet
            B(m, m.Body, 0, 0.02f, 0.24f, 0.7f, 0.42f, 0.42f, shell);
            m.Head = Pivot(m.Body, new Vector3(0, 0.02f, 0.45f));
            B(m, m.Head, 0, 0, 0.12f, 0.5f, 0.36f, 0.3f, Hex(0x221c2a));
            foreach (var (ex, ey) in new[] { (-0.14f, 0.07f), (0.14f, 0.07f), (-0.07f, 0.12f), (0.07f, 0.12f) })
                B(m, m.Head, ex, ey, 0.27f, 0.07f, 0.07f, 0.02f, Hex(0xff3a2a), 2.5f, false);
            B(m, m.Head, -0.12f, -0.12f, 0.32f, 0.06f, 0.06f, 0.18f, Hex(0x3a2a20));       // mandibles
            B(m, m.Head, 0.12f, -0.12f, 0.32f, 0.06f, 0.06f, 0.18f, Hex(0x3a2a20));
            foreach (float z in new[] { 0.3f, 0.05f, -0.22f })
                foreach (int sx in new[] { -1, 1 })
                {
                    var p = Pivot(m.Root, new Vector3(sx * 0.32f, 0.44f, z));
                    var upper = Pivot(p, Vector3.zero);
                    B(m, upper, sx * 0.3f, 0, 0, 0.6f, 0.08f, 0.08f, legC);
                    B(m, upper, sx * 0.62f, -0.2f, 0, 0.08f, 0.44f, 0.08f, legC);
                    m.Legs.Add(p);
                }
            m.Stride = 0.6f;
        }

        void Arrow(Model m)
        {
            B(m, m.Root, 0, 0, 0, 0.035f, 0.035f, 0.62f, Hex(0x9a7448), 0, false);
            B(m, m.Root, 0, 0, 0.33f, 0.07f, 0.07f, 0.1f, Hex(0x8a8c94), 0, false);
            B(m, m.Root, 0, 0, -0.26f, 0.12f, 0.01f, 0.12f, Hex(0xeae6de), 0, false);
            B(m, m.Root, 0, 0, -0.26f, 0.01f, 0.12f, 0.12f, Hex(0xeae6de), 0, false);
        }

        // A small horned imp of the Ember Deep that hovers on stubby wings and throws fire.
        void Imp(Model m)
        {
            Color skin = Hex(0xc8401c), dark = Hex(0x3a1410), horn = Hex(0x2a2220);
            foreach (int sx in new[] { -1, 1 })
            {
                var p = Pivot(m.Root, new Vector3(sx * 0.12f, 0.55f, 0));
                B(m, p, 0, -0.25f, 0, 0.14f, 0.5f, 0.16f, dark);
                m.Legs.Add(p);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 0.55f, 0));
            B(m, m.Body, 0, 0.25f, 0, 0.42f, 0.5f, 0.26f, skin, 0.35f);
            B(m, m.Body, 0, 0.28f, 0.135f, 0.2f, 0.22f, 0.01f, Hex(0xffb040), 1.8f, false);   // the fire in its chest
            m.Head = Pivot(m.Body, new Vector3(0, 0.52f, 0));
            B(m, m.Head, 0, 0.18f, 0, 0.4f, 0.36f, 0.38f, skin, 0.35f);
            B(m, m.Head, -0.16f, 0.42f, -0.02f, 0.08f, 0.18f, 0.08f, horn);
            B(m, m.Head, 0.16f, 0.42f, -0.02f, 0.08f, 0.18f, 0.08f, horn);
            Eyes(m, m.Head, 0.09f, 0.2f, 0.195f, 0.08f, Hex(0xfff070), 3);
            m.ArmL = Pivot(m.Body, new Vector3(-0.28f, 0.44f, 0));
            B(m, m.ArmL, 0, -0.22f, 0, 0.12f, 0.44f, 0.12f, skin, 0.3f);
            m.ArmR = Pivot(m.Body, new Vector3(0.28f, 0.44f, 0));
            B(m, m.ArmR, 0, -0.22f, 0, 0.12f, 0.44f, 0.12f, skin, 0.3f);
            foreach (int sx in new[] { -1, 1 })
            {
                var w = Pivot(m.Body, new Vector3(sx * 0.18f, 0.4f, -0.14f));
                B(m, w, sx * 0.25f, 0, -0.04f, 0.5f, 0.3f, 0.03f, dark);
                m.Wings.Add(w);
            }
        }

        // The guardian of the Ember Deep: a giant of black stone with molten seams.
        void Colossus(Model m)
        {
            Color rock = Hex(0x2a2426), rock2 = Hex(0x3a3234), magma = Hex(0xff7a20);
            foreach (int sx in new[] { -1, 1 })
            {
                var p = Pivot(m.Root, new Vector3(sx * 0.5f, 1.8f, 0));
                B(m, p, 0, -0.9f, 0, 0.8f, 1.8f, 0.85f, rock);
                B(m, p, 0, -0.9f, 0.43f, 0.12f, 1.2f, 0.01f, magma, 2.5f, false);
                m.Legs.Add(p);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 1.8f, 0));
            B(m, m.Body, 0, 0.95f, 0, 2.2f, 1.9f, 1.2f, rock);
            B(m, m.Body, 0, 1.4f, 0.1f, 2.4f, 0.8f, 1.1f, rock2);                      // shoulders
            B(m, m.Body, 0, 1.0f, 0.61f, 0.6f, 0.6f, 0.02f, magma, 3, false);         // molten heart
            B(m, m.Body, -0.6f, 0.6f, 0.61f, 0.08f, 0.9f, 0.01f, magma, 2.5f, false);
            B(m, m.Body, 0.7f, 1.3f, 0.61f, 0.5f, 0.08f, 0.01f, magma, 2.5f, false);
            m.Head = Pivot(m.Body, new Vector3(0, 1.9f, 0.15f));
            B(m, m.Head, 0, 0.4f, 0, 1.0f, 0.9f, 0.9f, rock2);
            B(m, m.Head, 0, 0.82f, -0.1f, 1.1f, 0.12f, 0.6f, rock);
            Eyes(m, m.Head, 0.24f, 0.45f, 0.455f, 0.18f, Hex(0xffd060), 3.5f);
            B(m, m.Head, 0, 0.15f, 0.455f, 0.5f, 0.08f, 0.01f, magma, 2, false);
            m.ArmL = Pivot(m.Body, new Vector3(-1.3f, 1.55f, 0));
            B(m, m.ArmL, 0, -1.05f, 0, 0.7f, 2.2f, 0.7f, rock);
            B(m, m.ArmL, 0, -2.25f, 0.05f, 0.9f, 0.5f, 0.9f, rock2);
            m.ArmR = Pivot(m.Body, new Vector3(1.3f, 1.55f, 0));
            B(m, m.ArmR, 0, -1.05f, 0, 0.7f, 2.2f, 0.7f, rock);
            B(m, m.ArmR, 0, -2.25f, 0.05f, 0.9f, 0.5f, 0.9f, rock2);
            m.Swing = 18; m.Stride = 2.2f;
        }

        void Boat(Model m)
        {
            Color wood = Hex(0x9c7446), dark = Hex(0x6e4e2e), rim = Hex(0x5a3e24);
            m.Body = Pivot(m.Root, Vector3.zero);
            B(m, m.Body, 0, 0.06f, 0, 1.2f, 0.12f, 2.3f, dark);                        // the bottom
            foreach (int sx in new[] { -1, 1 }) B(m, m.Body, sx * 0.64f, 0.3f, 0, 0.12f, 0.48f, 2.3f, wood);
            foreach (int sz in new[] { -1, 1 }) B(m, m.Body, 0, 0.3f, sz * 1.1f, 1.4f, 0.48f, 0.12f, wood);
            foreach (int sx in new[] { -1, 1 }) B(m, m.Body, sx * 0.66f, 0.57f, 0, 0.16f, 0.06f, 2.34f, rim, 0, false);   // the gunwale
            foreach (int sz in new[] { -1, 1 }) B(m, m.Body, 0, 0.57f, sz * 1.12f, 1.42f, 0.06f, 0.16f, rim, 0, false);
            B(m, m.Body, 0, 0.36f, -0.2f, 1.2f, 0.07f, 0.36f, wood);                   // the seat
            foreach (int sx in new[] { -1, 1 })
            {
                var oar = Pivot(m.Body, new Vector3(sx * 0.72f, 0.52f, -0.1f));
                B(m, oar, sx * 0.45f, -0.08f, 0, 0.9f, 0.06f, 0.06f, wood, 0, false);
                B(m, oar, sx * 0.95f, -0.12f, 0, 0.32f, 0.04f, 0.18f, dark, 0, false);
            }
        }

        void Cart(Model m)
        {
            Color iron = Hex(0x6a6e76), dark = Hex(0x3a3c42), rust = Hex(0x5a4a40);
            m.Body = Pivot(m.Root, Vector3.zero);
            B(m, m.Body, 0, 0.26f, 0, 0.96f, 0.1f, 1.24f, dark);
            foreach (int sx in new[] { -1, 1 }) B(m, m.Body, sx * 0.48f, 0.52f, 0, 0.08f, 0.55f, 1.3f, iron);
            foreach (int sz in new[] { -1, 1 }) B(m, m.Body, 0, 0.52f, sz * 0.62f, 1.04f, 0.55f, 0.08f, iron);
            foreach (int sx in new[] { -1, 1 }) B(m, m.Body, sx * 0.5f, 0.8f, 0, 0.12f, 0.04f, 1.34f, rust, 0, false);
            foreach (int sz in new[] { -1, 1 }) B(m, m.Body, 0, 0.8f, sz * 0.64f, 1.08f, 0.04f, 0.12f, rust, 0, false);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) B(m, m.Body, sx * 0.42f, 0.16f, sz * 0.4f, 0.1f, 0.3f, 0.3f, Hex(0x222326));
        }

        static readonly Color RobeBase = Hex(0x6a4a2e);
        static readonly Color[] JobRobe = { Hex(0x9a7a3a), Hex(0x3a3c44), Hex(0xa83232), Hex(0x5a7aa8), Hex(0xe0dcd0) };

        void Villager(Model m)
        {
            Color robe = RobeBase, skin = Hex(0xc89a7a), sash = Hex(0x2e6a4a);
            foreach (int sx in new[] { -1, 1 })
            {
                var p = Pivot(m.Root, new Vector3(sx * 0.12f, 0.75f, 0));
                B(m, p, 0, -0.375f, 0, 0.2f, 0.75f, 0.22f, Hex(0x4a3420));
                m.Legs.Add(p);
            }
            m.Body = Pivot(m.Root, new Vector3(0, 0.75f, 0));
            B(m, m.Body, 0, 0.3f, 0, 0.5f, 0.9f, 0.32f, robe);
            B(m, m.Body, 0, 0.05f, 0, 0.52f, 0.1f, 0.34f, sash);
            B(m, m.Body, 0, 0.45f, 0.2f, 0.56f, 0.16f, 0.16f, robe);                    // folded arms
            B(m, m.Body, 0, 0.45f, 0.285f, 0.2f, 0.12f, 0.01f, skin, 0, false);
            m.Head = Pivot(m.Body, new Vector3(0, 0.76f, 0));
            B(m, m.Head, 0, 0.24f, 0, 0.42f, 0.48f, 0.42f, skin);
            B(m, m.Head, 0, 0.18f, 0.25f, 0.1f, 0.2f, 0.1f, Hex(0xb88a6a));            // nose
            B(m, m.Head, 0, 0.47f, 0, 0.44f, 0.06f, 0.44f, Hex(0x5a3a24));
            Eyes(m, m.Head, 0.1f, 0.3f, 0.212f, 0.07f, Hex(0x2a4a2a));
        }
    }
}
