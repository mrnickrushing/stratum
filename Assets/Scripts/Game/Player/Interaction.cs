using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Stratum.Game
{
    // Aiming, mining, placing and using blocks, plus what that looks like: the outline, cracks, the item in hand
    // and the dust a block leaves.
    public sealed class Interaction : MonoBehaviour
    {
        public Player P;
        public VoxelWorld World;
        public Sky Sky;
        public bool InputEnabled = true;
        public bool HideHeld;                          // backpack open or HUD hidden (F1)
        public Renderer HeldRenderer => _heldMr;
        public bool Swinging => _swing < 0.95f;
        public System.Action<string> OpenStation;      // "workbench" / "furnace"
        public System.Action<string> Toast;
        public System.Action<Vector3Int> OpenBox;      // a chest
        public System.Action<Vector3Int> UseBed;       // the bed's foot
        public Link Link;
        public Creatures Creatures;

        public bool HasMob { get; private set; }
        public int TargetMob { get; private set; }
        public float EatProgress => _eat / EatTime;
        public float DrawProgress => _drawing ? Mathf.Clamp01(_draw / 1f) : 0;
        public float AttackReady => Mathf.Clamp01(1 - _attackCool / Mathf.Max(0.01f, _attackEvery));
        const float EatTime = 1.6f;
        float _eat, _draw, _attackCool, _attackEvery = 0.5f, _crunch;
        bool _drawing;

        public bool HasTarget { get; private set; }
        public Vector3Int Target { get; private set; }
        public Vector3Int TargetNormal { get; private set; }
        public float Progress { get; private set; }

        GameObject _outline, _crack, _held;
        Material _crackMat, _heldBlockMat, _heldItemMat;
        MeshFilter _heldMf; MeshRenderer _heldMr;
        Texture2D[] _cracks;
        ParticleSystem _dust;
        string _heldItem;
        float _swing = 1, _repeat, _lightTimer;
        Vector2 _sway;
        Vector4 _light = new Vector4(1, 0, 0, 1);

        public static Interaction Create(Player p, VoxelWorld w, Material blockMat)
        {
            var i = p.gameObject.AddComponent<Interaction>();
            i.P = p; i.World = w;
            i.Build(blockMat);
            return i;
        }

        void Build(Material blockMat)
        {
            var overlay = Shader.Find("Stratum/Overlay");
            _outline = new GameObject("Outline");
            _outline.AddComponent<MeshFilter>().sharedMesh = Edges();
            var om = new Material(overlay); om.SetColor("_Color", new Color(0.02f, 0.02f, 0.03f, 0.7f)); om.SetFloat("_Mode", 0);
            var omr = _outline.AddComponent<MeshRenderer>(); omr.sharedMaterial = om; omr.shadowCastingMode = ShadowCastingMode.Off;
            _outline.SetActive(false);

            _cracks = CrackStages();
            _crack = new GameObject("Cracks");
            _crack.AddComponent<MeshFilter>().sharedMesh = UnitCube(1.003f);
            _crackMat = new Material(overlay); _crackMat.SetFloat("_Mode", 1);
            var cmr = _crack.AddComponent<MeshRenderer>(); cmr.sharedMaterial = _crackMat; cmr.shadowCastingMode = ShadowCastingMode.Off;
            _crack.SetActive(false);

            _held = new GameObject("Held");
            _held.transform.SetParent(P.Cam.transform, false);
            _heldMf = _held.AddComponent<MeshFilter>();
            _heldMr = _held.AddComponent<MeshRenderer>();
            _heldMr.shadowCastingMode = ShadowCastingMode.Off;
            _heldBlockMat = new Material(blockMat);
            _heldItemMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _heldItemMat.SetFloat("_AlphaClip", 1); _heldItemMat.SetFloat("_Cutoff", 0.5f); _heldItemMat.EnableKeyword("_ALPHATEST_ON");
            _heldItemMat.SetFloat("_Cull", 0);

            var dgo = new GameObject("Dust");
            _dust = dgo.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _dust.main; main.loop = false; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.2f); main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.gravityModifier = 1.4f; main.maxParticles = 400; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _dust.emission; em.enabled = false;
            var sh = _dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = Vector3.one * 0.8f;
            var col = _dust.collision; col.enabled = false;
            var pr = dgo.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh; pr.mesh = UnitCube(1);
            var pm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            pr.sharedMaterial = pm; pr.shadowCastingMode = ShadowCastingMode.Off;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Aim();
            if (InputEnabled && Cursor.lockState == CursorLockMode.Locked) Act(dt); else { Progress = 0; }
            Visuals(dt);
        }

        void Aim()
        {
            HasTarget = false;
            var o = P.Cam.transform.position; var d = P.Cam.transform.forward;
            float reach = P.Creative ? 7f : 5f;
            // Grid walk (Amanatides & Woo).
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int sx = d.x > 0 ? 1 : -1, sy = d.y > 0 ? 1 : -1, sz = d.z > 0 ? 1 : -1;
            float tdx = Mathf.Abs(1 / d.x), tdy = Mathf.Abs(1 / d.y), tdz = Mathf.Abs(1 / d.z);
            float tx = (d.x > 0 ? x + 1 - o.x : o.x - x) * tdx, ty = (d.y > 0 ? y + 1 - o.y : o.y - y) * tdy, tz = (d.z > 0 ? z + 1 - o.z : o.z - z) * tdz;
            var n = Vector3Int.zero;
            float t = 0;
            HasMob = false;
            float mobDist = reach;
            if (Creatures != null && Creatures.Raycast(o, d, P.Creative ? 7f : 4.5f, out int mob, out float md)) { HasMob = true; TargetMob = mob; mobDist = md; }
            while (t <= reach && t <= mobDist)
            {
                ushort b = World.Get(x, y, z);
                if (b != 0 && !Blocks.IsFluid(b))
                {
                    bool hit = true;
                    var bd = Blocks.Get(b);
                    float at = t; var nrm = n;
                    if (bd.Shape == Shape.Boxes)
                    {
                        // Partial blocks are hit where their pieces are, on the face of the piece.
                        var shape = Blocks.ShapeAt(World.Get, x, y, z, bd);
                        float bt = Body.RayHit(shape, o.x - x, o.y - y, o.z - z, d.x, d.y, d.z, out int face);
                        hit = bt >= 0 && bt <= reach && bt <= mobDist;
                        if (hit) { at = bt; if (face >= 0) nrm = FaceNormal[face]; }
                    }
                    if (hit) { HasTarget = true; HasMob = false; Target = new Vector3Int(x, y, z); TargetNormal = nrm; TargetPoint = o + d * at; return; }
                }
                if (tx < ty && tx < tz) { x += sx; t = tx; tx += tdx; n = new Vector3Int(-sx, 0, 0); }
                else if (ty < tz) { y += sy; t = ty; ty += tdy; n = new Vector3Int(0, -sy, 0); }
                else { z += sz; t = tz; tz += tdz; n = new Vector3Int(0, 0, -sz); }
            }
        }

        Vector3Int _mining;
        readonly System.Random _rng = new System.Random();
        public Vector3 TargetPoint { get; private set; }
        static readonly Vector3Int[] FaceNormal = { new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
        public System.Action<Vector3Int> EditSign;
        public System.Action<int, int> OpenTrade;      // a villager (id, job) was right-clicked     // a sign was placed or right-clicked

        // Which way (0 +Z, 1 +X, 2 -Z, 3 -X) a horizontal direction mostly points.
        static int DirOf(Vector3 v) => Mathf.Abs(v.x) > Mathf.Abs(v.z) ? (v.x > 0 ? 1 : 3) : (v.z > 0 ? 0 : 2);
        static int DirOf(Vector3Int v) => v.x > 0 ? 1 : v.x < 0 ? 3 : v.z > 0 ? 0 : 2;

        // The first fluid source (or anything solid) along the view, for buckets.
        bool FluidAim(out Vector3Int cell)
        {
            cell = default;
            var o = P.Cam.transform.position; var d = P.Cam.transform.forward;
            for (float t = 0.2f; t < (P.Creative ? 7f : 5f); t += 0.05f)
            {
                var p = o + d * t;
                var c = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
                var bd = Blocks.Get(World.Get(c.x, c.y, c.z));
                if (bd.Fluid != 0 && bd.Level == 0) { cell = c; return true; }
                if (bd.Solid) return false;
            }
            return false;
        }

        // The first water along the view (any depth), for putting a boat on.
        bool WaterAim(out Vector3Int cell)
        {
            cell = default;
            var o = P.Cam.transform.position; var d = P.Cam.transform.forward;
            for (float t = 0.2f; t < 5.5f; t += 0.05f)
            {
                var p = o + d * t;
                var c = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
                ushort id = World.Get(c.x, c.y, c.z);
                if (Blocks.IsWater(id))
                {
                    while (Blocks.IsWater(World.Get(c.x, c.y + 1, c.z))) c.y++;
                    cell = c; return true;
                }
                if (Blocks.Get(id).Solid) return false;
            }
            return false;
        }

        // Swap the held item for another (a bucket filled or emptied).
        void SwapHeld(string to)
        {
            if (P.Creative) return;
            if (P.Inv.Held.Count <= 1) { P.Inv.Slots[P.Inv.Selected] = new Stack(to, 1); return; }
            P.Inv.UseHeld();
            Gain(to, 1);
        }

        // Something earned (a mined block, a filled bucket): into the pack, and whatever doesn't fit is handed to
        // Spill, which puts it on the ground or keeps it waiting. Returns how many didn't fit in the pack.
        public System.Action<string, int, int> Spill;      // item, count, wear
        int Gain(string item, int n)
        {
            int left = P.Inv.Add(item, n);
            if (_edit != null && n > left) _edit.Got.Add((item, n - left));
            if (left > 0) { if (Spill != null) Spill(item, left, 0); else Toast?.Invoke("Your pack is full"); }
            return left;
        }

        void Act(float dt)
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null) return;

            // Hotbar.
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.1f) P.Inv.Selected = (P.Inv.Selected + Inventory.Hotbar - 1) % Inventory.Hotbar;
            else if (scroll < -0.1f) P.Inv.Selected = (P.Inv.Selected + 1) % Inventory.Hotbar;
            if (kb != null)
                for (int i = 0; i < 9; i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) P.Inv.Selected = i;

            _repeat -= dt;
            _attackCool -= dt;
            bool lmb = mouse.leftButton.isPressed, rmb = mouse.rightButton.isPressed;
            var heldDef = Items.Get(P.Inv.Held.Item);

            // Fighting: a hit as often as the weapon allows.
            if (lmb && HasMob)
            {
                Progress = 0;
                if (_attackCool <= 0 && (mouse.leftButton.wasPressedThisFrame || _attackCool < -0.05f))
                {
                    _attackEvery = heldDef != null && heldDef.Tool == Tool.Sword ? 0.55f : heldDef != null && heldDef.Tool == Tool.Axe ? 0.9f : 0.5f;
                    _attackCool = _attackEvery;
                    float dmg = heldDef?.Damage ?? 1;
                    if (!P.OnGround && P.Velocity.y < -1 && !P.InWater) { dmg *= 1.5f; Sfx.I?.Play("hit_hard", 0.6f, 1.3f); }
                    Link?.Strike(TargetMob, dmg);
                    Sfx.I?.Play(dmg >= 6 ? "hit_hard" : "hit", 0.7f);
                    _swing = 0;
                    P.Exhaustion += 0.1f;
                    if (heldDef != null && heldDef.Durability > 0 && !P.Creative && P.Inv.WearHeld()) { Sfx.I?.Play("break_tool", 0.8f); Toast?.Invoke(heldDef.Name + " broke"); }
                }
                return;
            }
            if (mouse.leftButton.wasPressedThisFrame && !HasTarget) { _swing = 0; Sfx.I?.Play("swish", 0.3f); }

            // Feeding an animal its food (to breed it, or help a young one grow).
            if (heldDef != null && rmb && mouse.rightButton.wasPressedThisFrame && HasMob && Creatures != null && Creatures.TryGet(TargetMob, out var fed)
                && MobSim.FoodOf(fed.Kind) == heldDef.Id)
            {
                bool baby = (fed.More & Mob.MBaby) != 0, love = (fed.More & Mob.MLove) != 0;
                if (baby || !love && (fed.More & Mob.MRest) == 0)
                {
                    Link?.UseMob(TargetMob, heldDef.Id);
                    if (!P.Creative) P.Inv.UseHeld();
                    _swing = 0;
                }
                return;
            }

            // Into a boat or cart (right-click it), or putting one down.
            if (rmb && mouse.rightButton.wasPressedThisFrame && HasMob && P.Riding == null && Creatures != null && Creatures.TryGet(TargetMob, out var ride)
                && MobSim.Def(ride.Kind).Vehicle)
            {
                Link?.Mount(TargetMob); _swing = 0;
                return;
            }
            if (heldDef != null && heldDef.Id == "boat" && mouse.rightButton.wasPressedThisFrame && P.Riding == null)
            {
                Vector3? spot = null;
                if (WaterAim(out var w)) spot = new Vector3(w.x + 0.5f, w.y + 0.875f - 0.32f, w.z + 0.5f);
                else if (HasTarget && TargetNormal.y > 0) spot = new Vector3(Target.x + 0.5f, Target.y + 1, Target.z + 0.5f);
                if (spot != null)
                {
                    Link?.Park(MobKind.Boat, spot.Value, P.Yaw);
                    if (!P.Creative) P.Inv.UseHeld();
                    Sfx.I?.Play("splash", 0.35f, 1.2f); _swing = 0;
                    return;
                }
            }
            if (heldDef != null && heldDef.Id == "minecart" && mouse.rightButton.wasPressedThisFrame && HasTarget && Rails.Is(World.Get(Target.x, Target.y, Target.z)))
            {
                var (a, _, up) = Rails.Ends(World.Get(Target.x, Target.y, Target.z));
                float yaw = a * 90;
                Link?.Park(MobKind.Cart, new Vector3(Target.x + 0.5f, Target.y + (up >= 0 ? 0.5f : 1 / 16f), Target.z + 0.5f), yaw);
                if (!P.Creative) P.Inv.UseHeld();
                Sfx.I?.Block("place", Blocks.Rail, 0.8f); _swing = 0;
                return;
            }

            // An empty bucket scoops up a source of water or lava, or milks a cow.
            if (heldDef != null && heldDef.Id == "bucket" && mouse.rightButton.wasPressedThisFrame)
            {
                if (HasMob && Creatures != null && Creatures.TryGet(TargetMob, out var cow) && cow.Kind == MobKind.Cow) { SwapHeld("milk_bucket"); Sfx.I?.Play("moo", 0.5f, 1.1f); _swing = 0; return; }
                if (FluidAim(out var src))
                {
                    bool water = Blocks.IsWater(World.Get(src.x, src.y, src.z));
                    if (World.Set(src.x, src.y, src.z, 0)) { SwapHeld(water ? "water_bucket" : "lava_bucket"); Sfx.I?.Play(water ? "splash" : "sizzle", 0.5f); _swing = 0; }
                    return;
                }
            }

            // Eating: hold the right button with food in hand.
            bool canEat = heldDef != null && (heldDef.Food > 0 && (P.Hungry || P.Creative) || heldDef.Drink);
            if (rmb && canEat && !(HasTarget && IsUsable(World.Get(Target.x, Target.y, Target.z))))
            {
                _eat += dt;
                if ((_crunch -= dt) <= 0) { _crunch = 0.22f; Sfx.I?.Play("eat", 0.5f); _swing = 0.6f; }
                if (_eat >= EatTime)
                {
                    _eat = 0;
                    P.Eat(heldDef);
                    if (heldDef.Drink) { P.Burning = 0; SwapHeld("bucket"); }
                    else if (!P.Creative) P.Inv.UseHeld();
                    Sfx.I?.Play("burp", 0.4f);
                }
                return;
            }
            _eat = 0;

            // The bow: hold to draw, let go to loose an arrow.
            if (heldDef != null && heldDef.Ranged)
            {
                bool hasArrow = P.Creative || P.Inv.Count("arrow") > 0;
                if (rmb && hasArrow) { _drawing = true; _draw += dt; return; }
                if (_drawing && !rmb)
                {
                    _drawing = false;
                    float power = Mathf.Clamp01(_draw / 1f);
                    _draw = 0;
                    if (power > 0.15f)
                    {
                        var dir = P.Cam.transform.forward;
                        var from = P.Cam.transform.position + dir * 0.4f;
                        Link?.Shoot(from, dir * (14 + 40 * power) + P.Velocity * 0.5f, (1.5f + 7.5f * power * power) * (1 + 0.25f * heldDef.Ench('w')));
                        Sfx.I?.Play("bow", 0.7f, 0.9f + power * 0.3f);
                        if (!P.Creative) { P.Inv.Take("arrow", 1); if (P.Inv.WearHeld()) { Sfx.I?.Play("break_tool", 0.8f); Toast?.Invoke("Bow broke"); } }
                        _swing = 0.3f;
                    }
                    return;
                }
                if (rmb && !hasArrow && mouse.rightButton.wasPressedThisFrame) Toast?.Invoke("You need arrows");
            }
            else { _drawing = false; _draw = 0; }

            // Shears on a sheep, and other things done to a creature.
            if (rmb && HasMob && _repeat <= 0)
            {
                _repeat = 0.3f;
                if (heldDef != null && heldDef.Tool == Tool.Shears)
                {
                    if (Creatures != null && Creatures.TryGet(TargetMob, out var woolly) && woolly.Kind == MobKind.Sheep && (woolly.Flags & Mob.FSheared) == 0 && (woolly.More & Mob.MBaby) == 0)
                    { Link?.UseMob(TargetMob, heldDef.Id); _swing = 0; if (!P.Creative && P.Inv.WearHeld()) { Sfx.I?.Play("break_tool", 0.8f); Toast?.Invoke(heldDef.Name + " broke"); } }
                }
                else if (Creatures != null && Creatures.TryGet(TargetMob, out var who) && who.Kind == MobKind.Villager) OpenTrade?.Invoke(TargetMob, Mob.JobOf(who.More));
                return;
            }

            if (lmb && HasTarget)
            {
                if (_mining != Target) { _mining = Target; Progress = 0; }
                var b = Blocks.Get(World.Get(Target.x, Target.y, Target.z));
                var held = Items.Get(P.Inv.Held.Item);
                float time = Mining.Time(b, held, P.Creative);
                if (float.IsInfinity(time)) { Progress = 0; }
                else if (P.Creative)
                {
                    if (_repeat <= 0) { Break(Target); _repeat = 0.2f; }
                }
                else
                {
                    Progress += dt / time;
                    if (Mathf.Repeat(Progress * time, 0.25f) < dt) { Sfx.I?.Block("dig", b.Id, 0.45f); _swing = 0; }
                    if (Progress >= 1) { Break(Target); Progress = 0; _repeat = 0.15f; }
                }
            }
            else { Progress = 0; _mining = new Vector3Int(int.MinValue, 0, 0); }
            if (!lmb) _repeat = Mathf.Min(_repeat, 0);

            if (rmb && _repeat <= 0 && HasTarget)
            {
                _repeat = 0.22f;
                Use();
            }
            if (mouse.middleButton.wasPressedThisFrame && HasTarget) Pick();
        }

        static readonly Vector3Int[] Dir4 = { new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, -1), new Vector3Int(-1, 0, 0) };

        // Blocks the right button does something to (rather than placing against).
        static bool IsUsable(ushort id)
        {
            var d = Blocks.Get(id);
            return d.BaseId == Blocks.Lever || d.BaseId == Blocks.Button || id == Blocks.Workbench || d.BaseId == Blocks.Furnace || id == Blocks.EnchantingTable || d.BaseId == Blocks.Anvil || d.Container || d.BaseId == Blocks.Bed || d.Opens || d.BaseId == Blocks.Sign || d.BaseId == Blocks.WallSign;
        }

        void Break(Vector3Int p)
        {
            ushort id = World.Get(p.x, p.y, p.z);
            var def = Blocks.Get(id);
            if (id == 0 || def.Hardness < 0 && !P.Creative) return;
            if (!World.Set(p.x, p.y, p.z, 0)) return;
            bool outer = _edit == null;
            if (outer) _edit = Remember(p, 0);
            try { Broke(p, id, def); } finally { if (outer) _edit = null; }
        }

        void Broke(Vector3Int p, ushort id, BlockDef def)
        {
            // A door goes as a whole.
            if (def.Door)
            {
                var other = (def.Variant & 1) == 1 ? p + Vector3Int.down : p + Vector3Int.up;
                if (Blocks.Get(World.Get(other.x, other.y, other.z)).Door) World.Set(other.x, other.y, other.z, 0);
            }
            // A bed goes as a whole.
            if (def.BaseId == Blocks.Bed)
            {
                var other = def.Variant == 0 ? p + Dir4[def.Facing] : p - Dir4[def.Facing];
                if (Blocks.Get(World.Get(other.x, other.y, other.z)).BaseId == Blocks.Bed) World.Set(other.x, other.y, other.z, 0);
            }
            if (id == Blocks.Obsidian || Ember.IsPortal(id)) Ember.Collapse(World.Get, (x, y, z, b) => World.Set(x, y, z, b), p.x, p.y, p.z);
            Sfx.I?.Block("break", id, 0.9f);
            Burst(p, def);
            _swing = 0;
            // A plant on top goes with it.
            ushort above = World.Get(p.x, p.y + 1, p.z);
            if (above != 0 && (Blocks.Get(above).Shape == Shape.Cross || Blocks.Get(above).Shape == Shape.Torch && !Blocks.Get(above).OnWall)) Break(p + Vector3Int.up);
            if (P.Creative) { Unsupported(p); return; }
            var held = Items.Get(P.Inv.Held.Item);
            if (Mining.Drops(def, held))
            {
                var (item, n) = Mining.Drop(def, held);
                n = Mining.Bounty(def, held, _rng, n);
                if (!P.Creative) P.AddXp(Mining.XpFor(def, _rng));
                if (item != null) Fall(p, item, n);
            }
            else if (def.TierNeeded > 0) Toast?.Invoke("Needs a better pickaxe");
            var bonus = Mining.Bonus(def, _rng);
            if (bonus != null) Fall(p, bonus, 1);
            var bonus2 = Mining.Bonus2(def, _rng);
            if (bonus2 != null) Fall(p, bonus2, 1);
            Unsupported(p);
            if (held != null && held.Durability > 0 && def.Hardness > 0 && P.Inv.WearHeld())
            {
                Sfx.I?.Play("break_tool", 0.8f);
                Toast?.Invoke(held.Name + " broke");
            }
        }

        void Use()
        {
            var kb = Keyboard.current;
            bool sneak = Keys.Down(Stratum.Game.Act.Sneak);
            ushort target = World.Get(Target.x, Target.y, Target.z);
            var tdef = Blocks.Get(target);
            var held = P.Inv.Held;
            var item = Items.Get(held.Item);
            if (!sneak)
            {
                if (target == Blocks.Workbench) { OpenStation?.Invoke("workbench"); return; }
                if (tdef.BaseId == Blocks.Furnace) { OpenBox?.Invoke(Target); return; }
                if (target == Blocks.EnchantingTable) { OpenStation?.Invoke("enchant"); return; }
                if (tdef.BaseId == Blocks.Anvil) { OpenStation?.Invoke("anvil"); return; }
                if (tdef.Container && target != Blocks.Satchel) { OpenBox?.Invoke(Target); return; }
                if (target == Blocks.Satchel) { World.Set(Target.x, Target.y, Target.z, 0); Sfx.I?.Play("pickup", 0.6f); _swing = 0; return; }
                if (tdef.BaseId == Blocks.Bed) { UseBed?.Invoke(tdef.Variant == 0 ? Target : Target - Dir4[tdef.Facing]); return; }
                if (tdef.Opens) { Toggle(Target, tdef); return; }
                if (tdef.BaseId == Blocks.Lever) { World.Set(Target.x, Target.y, Target.z, Blocks.Flip(target, 1)); Sfx.I?.Play("door_close", 0.4f, 1.8f); _swing = 0; return; }
                if (tdef.BaseId == Blocks.Button) { if ((tdef.Variant & 1) == 0) { World.Set(Target.x, Target.y, Target.z, (ushort)(target + 1)); Sfx.I?.Play("door_close", 0.35f, 2.2f); } _swing = 0; return; }
                if (tdef.BaseId == Blocks.Sign || tdef.BaseId == Blocks.WallSign) { EditSign?.Invoke(Target); return; }
            }
            if (item == null) return;
            if (item.Tool == Tool.Hoe)
            {
                // Grass or dirt with nothing on top turns to farmland.
                bool open = World.Get(Target.x, Target.y + 1, Target.z) == 0;
                if ((target == Blocks.Grass || target == Blocks.Dirt || target == Blocks.SnowyGrass) && TargetNormal.y >= 0 && open)
                {
                    World.Set(Target.x, Target.y, Target.z, Blocks.Farmland);
                    Sfx.I?.Block("step", Blocks.Dirt, 0.9f);
                    if (!P.Creative) P.Inv.WearHeld();
                    _swing = 0;
                }
                return;
            }
            if (item.Id == "fire_striker")
            {
                var spot = Target + TargetNormal;
                if (target == Blocks.Obsidian && Ember.Light(World.Get, (x, y, z, b) => World.Set(x, y, z, b), spot.x, spot.y, spot.z))
                {
                    Sfx.I?.Play("portal", 0.7f, 1.2f);
                    if (!P.Creative) P.Inv.WearHeld();
                }
                else if (target == Blocks.BlastKeg)
                {
                    // Lights the fuse.
                    World.Set(Target.x, Target.y, Target.z, Blocks.KegLit);
                    Sfx.I?.Play("sizzle", 0.6f, 1.3f);
                    if (!P.Creative) P.Inv.WearHeld();
                }
                else if (target != 0 && (World.Get(spot.x, spot.y, spot.z) == 0 || Blocks.Get(World.Get(spot.x, spot.y, spot.z)).Replaceable && Blocks.Get(World.Get(spot.x, spot.y, spot.z)).Fluid == 0)
                         && (Blocks.Get(target).Burns || Blocks.Get(World.Get(spot.x, spot.y - 1, spot.z)).Solid))
                {
                    // A flame on the ground, or against something that burns.
                    if (World.Set(spot.x, spot.y, spot.z, Blocks.Fire))
                    {
                        Sfx.I?.Play("sizzle", 0.5f, 1.6f);
                        if (!P.Creative) P.Inv.WearHeld();
                    }
                }
                else Sfx.I?.Play("swish", 0.3f, 1.6f);
                _swing = 0;
                return;
            }
            if (item.Id == "bone_meal")
            {
                bool works = tdef.BaseId == Blocks.Wheat && tdef.Variant < Blocks.WheatStages - 1 || tdef.Grows == 2 || target == Blocks.Grass && World.Get(Target.x, Target.y + 1, Target.z) == 0;
                if (!works) return;
                Link?.Fertilize(Target);
                if (!P.Creative) P.Inv.UseHeld();
                Sfx.I?.Play("pickup", 0.4f, 0.7f);
                Burst(Target, Blocks.Get(Blocks.OakLeaves));
                _swing = 0;
                return;
            }
            if (item.Id == "water_bucket" || item.Id == "lava_bucket")
            {
                var spot = tdef.Replaceable && target != 0 ? Target : Target + TargetNormal;
                ushort there = World.Get(spot.x, spot.y, spot.z);
                if (there != 0 && !Blocks.Get(there).Replaceable) return;
                if (World.Set(spot.x, spot.y, spot.z, item.Id == "water_bucket" ? Blocks.Water : Blocks.Lava))
                {
                    Sfx.I?.Play(item.Id == "water_bucket" ? "splash" : "sizzle", 0.6f);
                    SwapHeld("bucket"); _swing = 0;
                }
                return;
            }
            if (item.Block == 0) return;
            ushort id = item.Block;
            var def = Blocks.Get(id);
            var look = P.Cam.transform.forward;

            // A slab on a slab makes the whole block.
            if (Blocks.Slabs.Contains(id))
            {
                bool merge = target == id && TargetNormal.y > 0 || target == id + 1 && TargetNormal.y < 0;
                var at0 = Target + TargetNormal;
                ushort there = World.Get(at0.x, at0.y, at0.z);
                if (merge) { Place(Target, def.Pair, id); return; }
                if (there == id || there == id + 1) { Place(at0, def.Pair, id); return; }
            }

            // Into the block itself when it can be replaced (grass, water), else beside the face.
            var at = tdef.Replaceable && target != 0 ? Target : Target + TargetNormal;
            ushort cur = World.Get(at.x, at.y, at.z);
            if (cur != 0 && !Blocks.Get(cur).Replaceable) return;
            if (at.y < 1 || at.y >= Chunk.Height) return;
            if (def.Solid && P.Overlaps(at.x, at.y, at.z)) return;
            ushort below = World.Get(at.x, at.y - 1, at.z);
            bool isFlowerPot = Blocks.Keys[def.BaseId].StartsWith("flower_pot");
            if (def.Shape == Shape.Cross && !isFlowerPot && !CanStand(id, at)) return;
            if (def.Shape == Shape.Cross && isFlowerPot && !Blocks.Get(below).Solid) return;
            // A torch held against the side of something solid hangs on it.
            if (id == Blocks.Torch && TargetNormal.y == 0 && target != 0 && Blocks.Get(target).Solid && at == Target + TargetNormal)
            {
                Place(at, (ushort)(Blocks.WallTorch + DirOf(-TargetNormal)), id);
                return;
            }
            if (def.Shape == Shape.Torch && !Blocks.Get(below).Solid) return;
            if ((Carpet(def) || def.BaseId == Blocks.Candle) && below == 0) return;
            int f = DirOf(look);
            if (id == Blocks.Bed)
            {
                // Two blocks: the foot here and the head one further on, the way the player is looking.
                var head = at + Dir4[f];
                ushort hc = World.Get(head.x, head.y, head.z);
                if (hc != 0 && !Blocks.Get(hc).Replaceable || P.Overlaps(head.x, head.y, head.z) || P.Overlaps(at.x, at.y, at.z)) { Toast?.Invoke("No room for a bed here"); return; }
                if (!Blocks.Get(below).Solid || !Blocks.Get(World.Get(head.x, head.y - 1, head.z)).Solid) { Toast?.Invoke("A bed needs flat ground"); return; }
                if (!World.Set(at.x, at.y, at.z, (ushort)(Blocks.Bed + f * 2))) return;
                World.Set(head.x, head.y, head.z, (ushort)(Blocks.Bed + f * 2 + 1));
                Placed(id);
                return;
            }
            if (def.Door)
            {
                var up = at + Vector3Int.up;
                ushort uc = World.Get(up.x, up.y, up.z);
                if (uc != 0 && !Blocks.Get(uc).Replaceable || P.Overlaps(at.x, at.y, at.z) || P.Overlaps(up.x, up.y, up.z)) { Toast?.Invoke("No room for a door here"); return; }
                if (!Blocks.Get(below).Solid) return;
                if (!World.Set(at.x, at.y, at.z, (ushort)(id + f * 4))) return;
                World.Set(up.x, up.y, up.z, (ushort)(id + f * 4 + 1));
                Placed(id);
                return;
            }
            ushort place;
            if (Blocks.Slabs.Contains(id))
            {
                bool top = TargetNormal.y < 0 || TargetNormal.y == 0 && TargetPoint.y - Mathf.Floor(TargetPoint.y) > 0.5f;
                place = (ushort)(id + (top ? 1 : 0));
            }
            else if (Blocks.Stairs.Contains(id) || def.BaseId == Blocks.FenceGate) place = (ushort)(id + (def.BaseId == Blocks.FenceGate ? f * 2 : f));
            else if (id == Blocks.Trapdoor) place = (ushort)(Blocks.Trapdoor + f * 2);
            else if (def.OnWall)
            {
                // Pictures, mirrors, clocks and shelves hang on the side of something solid.
                if (TargetNormal.y != 0 || !Blocks.Get(target).Solid) return;
                place = (ushort)(id + DirOf(-TargetNormal));
            }
            else if (id == Blocks.Ladder)
            {
                // On the side of something solid.
                if (TargetNormal.y != 0 || !Blocks.Get(target).Solid) return;
                place = (ushort)(Blocks.Ladder + DirOf(-TargetNormal));
            }
            else if (id == Blocks.SparkWire || id == Blocks.Lever || id == Blocks.PressurePlate || id == Blocks.Repeater)
            {
                var bb = Blocks.Get(below);
                if (!(bb.Solid && bb.Opaque && bb.Shape == Shape.Cube) && !Blocks.Slabs.Contains(below)) return;
                place = id == Blocks.Lever ? (ushort)(Blocks.Lever + (f & 1) * 2) : id == Blocks.Repeater ? (ushort)(Blocks.Repeater + f * 2) : id;
            }
            else if (id == Blocks.Button)
            {
                // On the side of something solid.
                if (TargetNormal.y != 0 || !Blocks.Get(target).Opaque) return;
                place = (ushort)(Blocks.Button + DirOf(-TargetNormal) * 2);
            }
            else if (id == Blocks.Rail || id == Blocks.BoosterRail)
            {
                if (!Blocks.Get(below).Solid || !Blocks.Get(below).Opaque && !Blocks.Slabs.Contains(below)) return;
                place = Rails.Shape(World.Get, at.x, at.y, at.z, id, f);
                if (!World.Set(at.x, at.y, at.z, place)) return;
                Rails.Settle(World.Get, (x, y, z, b) => World.Set(x, y, z, b), at.x, at.y, at.z);
                Placed(id);
                return;
            }
            else if (id == Blocks.Sign)
            {
                if (TargetNormal.y > 0) place = (ushort)(Blocks.Sign + f);
                else if (TargetNormal.y == 0) place = (ushort)(Blocks.WallSign + DirOf(-TargetNormal));
                else return;
            }
            else place = Blocks.Facing(id, look.x, look.z);
            if (!World.Set(at.x, at.y, at.z, place)) return;
            Placed(id);
            if (id == Blocks.Sign) EditSign?.Invoke(at);
        }

        // Can this plant live here? Seeds want farmland; sugar cane wants water beside its ground (or more cane).
        bool CanStand(ushort id, Vector3Int at)
        {
            ushort below = World.Get(at.x, at.y - 1, at.z);
            var bd = Blocks.Get(below);
            if (id == Blocks.Wheat) return bd.BaseId == Blocks.Farmland;
            if (id == Blocks.SugarCane)
            {
                if (below == Blocks.SugarCane) return true;
                if (below != Blocks.Grass && below != Blocks.Dirt && below != Blocks.Sand) return false;
                for (int i = 0; i < 4; i++) if (Blocks.IsWater(World.Get(at.x + Dir4[i].x, at.y - 1, at.z + Dir4[i].z))) return true;
                return false;
            }
            if (id == Blocks.DeadBush) return below == Blocks.Sand;
            return below == Blocks.Grass || below == Blocks.Dirt || below == Blocks.SnowyGrass || bd.BaseId == Blocks.Farmland;
        }

        static bool Carpet(BlockDef d) => d.Shape == Shape.Boxes && !d.Solid && Blocks.Keys[d.BaseId].StartsWith("carpet_");
        static bool NeedsFloor(ushort id, BlockDef d) =>
            d.Shape == Shape.Cross || d.Shape == Shape.Torch && !d.OnWall || d.BaseId == Blocks.Sign || d.Door && (d.Variant & 1) == 0 || Rails.Is(id) || Signal.NeedsFloor(id)
            || Carpet(d) || d.BaseId == Blocks.Candle;
        static bool OnWall(BlockDef d) => d.OnWall || d.BaseId == Blocks.Ladder || d.BaseId == Blocks.WallSign || d.BaseId == Blocks.Button;

        // Things that stand on a block go when it does (plants, torches, signs, doors, carpets), and so do the
        // things hung on its sides (ladders, pictures, buttons).
        void Unsupported(Vector3Int p)
        {
            for (int k = 0; k < 4; k++)
            {
                var q = p - Dir4[k];
                var hung = Blocks.Get(World.Get(q.x, q.y, q.z));
                if (hung.Id != 0 && OnWall(hung) && hung.Facing == k && World.Set(q.x, q.y, q.z, 0)) GiveDrop(hung, q);
            }
            for (int guard = 0; guard < 64; guard++)
            {
                var up = p + Vector3Int.up;
                ushort id = World.Get(up.x, up.y, up.z);
                var d = Blocks.Get(id);
                if (id == 0 || !NeedsFloor(id, d)) return;
                if (d.Door) World.Set(up.x, up.y + 1, up.z, 0);
                World.Set(up.x, up.y, up.z, 0);
                GiveDrop(d, up);
                if (id != Blocks.SugarCane) return;
                p = up;                      // a column of cane comes down together
            }
        }

        // What each of the last few edits gave or cost, so one the server refuses can be undone.
        sealed class Edit { public Vector3Int At; public float When; public ushort Set; public string Spent; public readonly List<(string item, int n)> Got = new List<(string, int)>(); }
        readonly List<Edit> _edits = new List<Edit>();
        Edit _edit;

        Edit Remember(Vector3Int at, ushort set)
        {
            _edits.RemoveAll(e => Time.unscaledTime - e.When > 6);
            var edit = new Edit { At = at, When = Time.unscaledTime, Set = set };
            _edits.Add(edit);
            return edit;
        }

        // The server put a block back: if that undoes an edit of ours, hand back what it cost or take what it gave.
        public void Corrected(Vector3Int at, ushort now)
        {
            int i = _edits.FindLastIndex(e => e.At == at && Time.unscaledTime - e.When <= 6);
            if (i < 0) return;
            var edit = _edits[i]; _edits.RemoveAt(i);
            if (edit.Set == now || P.Creative) return;
            foreach (var (item, n) in edit.Got) P.Inv.Take(item, n);
            if (edit.Spent != null) P.Inv.Add(edit.Spent, 1);
        }

        void GiveDrop(BlockDef d, Vector3Int at)
        {
            if (P.Creative) return;
            var (item, n) = Mining.Drop(d);
            if (item != null && n > 0) Fall(at, item, n);
            var b2 = Mining.Bonus2(d, _rng);
            if (b2 != null) Fall(at, b2, 1);
        }

        // What a broken block gives falls out where it stood, to be walked over.
        void Fall(Vector3Int at, string item, int n)
        {
            if (Link != null) Link.Spill(new Vector3(at.x + 0.5f, at.y + 0.35f, at.z + 0.5f), new Stack(item, n));
            else Gain(item, n);
        }

        void Place(Vector3Int at, ushort block, ushort item)
        {
            if (P.Overlaps(at.x, at.y, at.z) && Blocks.Get(block).Solid && !Blocks.Slabs.Contains(World.Get(at.x, at.y, at.z))) return;
            if (!World.Set(at.x, at.y, at.z, block)) return;
            if (!P.Creative && P.Inv.Held.Item != null) Remember(at, block).Spent = P.Inv.Held.Item;
            Placed(item);
        }

        void Placed(ushort id)
        {
            Sfx.I?.Block("place", id, 0.8f);
            _swing = 0;
            if (!P.Creative) P.Inv.UseHeld();
        }

        // Doors (both halves), trapdoors and gates swing open and shut.
        void Toggle(Vector3Int p, BlockDef d)
        {
            ushort id = World.Get(p.x, p.y, p.z);
            if (d.Door)
            {
                bool upper = (d.Variant & 1) == 1;
                var other = upper ? p + Vector3Int.down : p + Vector3Int.up;
                World.Set(p.x, p.y, p.z, Blocks.Flip(id, 2));
                ushort oid = World.Get(other.x, other.y, other.z);
                if (Blocks.Get(oid).Door) World.Set(other.x, other.y, other.z, Blocks.Flip(oid, 2));
                Sfx.I?.Play((d.Variant & 2) == 0 ? "door_open" : "door_close", 0.7f);
            }
            else
            {
                World.Set(p.x, p.y, p.z, Blocks.Flip(id, 1));
                Sfx.I?.Play((d.Variant & 1) == 0 ? "door_open" : "door_close", 0.6f, 1.15f);
            }
            _swing = 0;
        }

        void Pick()
        {
            ushort id = World.Get(Target.x, Target.y, Target.z);
            var key = Blocks.Keys[Blocks.Get(id).BaseId];
            if (Items.Get(key) == null) return;
            // Already in the hotbar: select it.
            for (int i = 0; i < Inventory.Hotbar; i++) if (P.Inv.Slots[i].Item == key) { P.Inv.Selected = i; return; }
            if (P.Creative) P.Inv.Slots[P.Inv.Selected] = new Stack(key, Items.Get(key).MaxStack);
        }

        // ------------------------------------------------------------------ visuals

        void Visuals(float dt)
        {
            bool show = HasTarget && InputEnabled;
            _outline.SetActive(show);
            if (show)
            {
                ushort b = World.Get(Target.x, Target.y, Target.z);
                var sh = Blocks.Get(b).Shape;
                _outline.transform.position = Target;
                _outline.transform.localScale = Vector3.one;
                if (sh == Shape.Torch) { _outline.transform.position = Target + new Vector3(0.4f, 0, 0.4f); _outline.transform.localScale = new Vector3(0.2f, 0.65f, 0.2f); }
                else if (sh == Shape.Cross) { _outline.transform.position = Target + new Vector3(0.1f, 0, 0.1f); _outline.transform.localScale = new Vector3(0.8f, 0.85f, 0.8f); }
                else if (sh == Shape.Boxes)
                {
                    var bd = Blocks.Get(b);
                    var shape = Blocks.ShapeAt(World.Get, Target.x, Target.y, Target.z, bd);
                    if (shape != null && shape.Length > 0)
                    {
                        Vector3 lo = Vector3.one, hi = Vector3.zero;
                        foreach (var bx in shape) { lo = Vector3.Min(lo, new Vector3(bx.X0, bx.Y0, bx.Z0)); hi = Vector3.Max(hi, new Vector3(bx.X1, bx.Y1, bx.Z1)); }
                        _outline.transform.position = Target + lo; _outline.transform.localScale = hi - lo;
                    }
                }
            }
            bool cracking = show && Progress > 0.02f;
            _crack.SetActive(cracking);
            if (cracking)
            {
                _crack.transform.position = Target + Vector3.one * 0.5f;
                int stage = Mathf.Clamp((int)(Progress * _cracks.Length), 0, _cracks.Length - 1);
                _crackMat.SetTexture("_MainTex", _cracks[stage]);
            }

            // What's in hand.
            var held = P.Inv.Held;
            if (held.Item != _heldItem) { _heldItem = held.Item; SetHeld(); _swing = 0.5f; }
            _lightTimer -= dt;
            if (_lightTimer <= 0) { _lightTimer = 0.15f; _light = LightAt(P.EyePos); _heldBlockMat.SetVector("_LightOverride", new Vector4(_light.x, _light.y, 0, 1)); }
            _heldMr.enabled = !HideHeld;
            if (_held.activeSelf)
            {
                _swing = Mathf.Min(1, _swing + dt * 4.5f);
                float s = Mathf.Sin(_swing * Mathf.PI);
                var md = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
                _sway = Vector2.Lerp(_sway, md * 0.0008f, 1 - Mathf.Exp(-dt * 10));
                var it = Items.Get(_heldItem);
                bool block = it != null && it.Block != 0 && !BlockMesh.IsFlat(it.Block);
                var basePos = block ? new Vector3(0.46f, -0.33f, 0.88f) : new Vector3(0.44f, -0.36f, 0.70f);   // the block clears the hotbar
                _held.transform.localPosition = basePos + new Vector3(-_sway.x - s * 0.12f, -_sway.y - s * 0.18f, s * 0.1f);
                var baseRot = block ? Quaternion.Euler(8, 40, 0) : Quaternion.Euler(0, -95, 25);
                _held.transform.localRotation = Quaternion.Euler(-s * 45, 0, 0) * baseRot;
                float day = Sky != null ? Sky.DayFactor * 0.85f + 0.15f : 1;
                float lit = Mathf.Max(Mathf.Pow(0.8f, (1 - _light.x) * 15) * day, Mathf.Pow(0.8f, (1 - _light.y) * 15));
                if (!block) _heldItemMat.SetColor("_BaseColor", new Color(lit, lit, lit, 1) * 1.1f);
            }
        }

        void SetHeld()
        {
            var it = Items.Get(_heldItem);
            if (it == null) { _held.SetActive(false); return; }
            _held.SetActive(true);
            if (it.Block != 0 && !BlockMesh.IsFlat(it.Block))
            {
                _heldMf.sharedMesh = BlockMesh.Cube(it.Block);
                _heldMr.sharedMaterial = _heldBlockMat;
                _held.transform.localScale = Vector3.one * 0.21f;
            }
            else
            {
                _heldMf.sharedMesh = FlatQuad();
                var tex = Icons.TextureOf(it.Id);
                _heldItemMat.SetTexture("_BaseMap", tex);
                _heldMr.sharedMaterial = _heldItemMat;
                _held.transform.localScale = Vector3.one * 0.38f;
            }
        }

        // Rough light at a point: open sky above or not, and the brightest light source nearby.
        Vector4 LightAt(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            if (World.LightAt(x, y, z, out float ls, out float lb)) return new Vector4(ls, lb, 0, 1);
            // Not meshed yet: a rough guess from the blocks overhead and torches nearby.
            float sky = 1;
            for (int yy = y + 1; yy < Mathf.Min(y + 64, Chunk.Height); yy++)
            {
                var d = Blocks.Get(World.Get(x, yy, z));
                if (d.Opaque) { sky = y > Chunk.SeaLevel - 4 ? 0.7f : 0.25f; break; }
            }
            int best = 0;
            for (int dy = -4; dy <= 4; dy++)
                for (int dz = -5; dz <= 5; dz++)
                    for (int dx = -5; dx <= 5; dx++)
                    {
                        int e = Blocks.Get(World.Get(x + dx, y + dy, z + dz)).Emit;
                        if (e == 0) continue;
                        int l = e - Mathf.Abs(dx) - Mathf.Abs(dy) - Mathf.Abs(dz);
                        if (l > best) best = l;
                    }
            return new Vector4(sky, best / 15f, 0, 1);
        }

        // A blast: smoke and sparks thrown out from the middle.
        public void Puff(Vector3 at, float power)
        {
            if (_dust == null) return;
            _dust.transform.position = at;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            int n = Mathf.Clamp((int)(power * 26), 30, 160);
            for (int i = 0; i < n; i++)
            {
                float k = Random.Range(0.25f, 1f);
                ep.startColor = i % 4 == 0 ? new Color(1f, 0.6f + k * 0.3f, 0.2f, 1) : new Color(k * 0.8f, k * 0.78f, k * 0.75f, 1);
                ep.velocity = Random.onUnitSphere * Random.Range(1.5f, 3.2f) * power + Vector3.up * 1.5f;
                _dust.Emit(ep, 1);
            }
        }

        void Burst(Vector3Int p, BlockDef def)
        {
            var c = BlockTextures.Average[(int)def.Side];
            if (def.Side == Tex.GrassSide) c = BlockTextures.Average[(int)Tex.Dirt];
            float l = Mathf.Pow(0.8f, (1 - _light.x) * 15) * 0.9f + 0.1f;
            _dust.transform.position = p + Vector3.one * 0.5f;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < 18; i++)
            {
                float k = Random.Range(0.75f, 1.1f) * l;
                ep.startColor = new Color(c.r * k, c.g * k, c.b * k, 1);
                ep.velocity = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(1f, 3.5f), Random.Range(-1.5f, 1.5f));
                _dust.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ meshes

        static Mesh _edges, _flat;
        static readonly System.Collections.Generic.Dictionary<float, Mesh> _cubes = new System.Collections.Generic.Dictionary<float, Mesh>();

        static Mesh UnitCube(float size)
        {
            if (_cubes.TryGetValue(size, out var m)) return m;
            m = new Mesh();
            float o = (size - 1) / 2, a = -0.5f - o, b = 0.5f + o;
            var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var t = new System.Collections.Generic.List<int>();
            void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Face(new(b, a, a), new(b, b, a), new(b, b, b), new(b, a, b));
            Face(new(a, a, b), new(a, b, b), new(a, b, a), new(a, a, a));
            Face(new(a, b, a), new(a, b, b), new(b, b, b), new(b, b, a));
            Face(new(a, a, a), new(b, a, a), new(b, a, b), new(a, a, b));
            Face(new(b, a, b), new(b, b, b), new(a, b, b), new(a, a, b));
            Face(new(a, a, a), new(a, b, a), new(b, b, a), new(b, a, a));
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            _cubes[size] = m;
            return m;
        }

        // Twelve thin bars along the edges of a unit cell (slightly larger than the block).
        static Mesh Edges()
        {
            if (_edges != null) return _edges;
            var combine = new System.Collections.Generic.List<CombineInstance>();
            const float w = 0.012f, e = 0.004f;
            var bar = UnitCube(1);
            for (int axis = 0; axis < 3; axis++)
                for (int i = 0; i < 4; i++)
                {
                    float u = (i & 1) == 0 ? -e : 1 + e, vv = (i & 2) == 0 ? -e : 1 + e;
                    Vector3 pos, scale;
                    if (axis == 0) { pos = new Vector3(0.5f, u, vv); scale = new Vector3(1 + 2 * e + w, w, w); }
                    else if (axis == 1) { pos = new Vector3(u, 0.5f, vv); scale = new Vector3(w, 1 + 2 * e + w, w); }
                    else { pos = new Vector3(u, vv, 0.5f); scale = new Vector3(w, w, 1 + 2 * e + w); }
                    combine.Add(new CombineInstance { mesh = bar, transform = Matrix4x4.TRS(pos, Quaternion.identity, scale) });
                }
            _edges = new Mesh();
            _edges.CombineMeshes(combine.ToArray(), true, true);
            return _edges;
        }

        static Mesh FlatQuad()
        {
            if (_flat != null) return _flat;
            _flat = new Mesh();
            _flat.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
            _flat.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _flat.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _flat.RecalculateNormals();
            return _flat;
        }

        static Texture2D[] CrackStages()
        {
            var p = new Paint(99);
            var stages = new Texture2D[8];
            // Crack lines grow outward from the middle as the stage rises.
            var field = new float[Paint.S * Paint.S];
            for (int y = 0; y < Paint.S; y++)
                for (int x = 0; x < Paint.S; x++)
                {
                    p.Cells(x, y, 5, out float f1, out float f2, out _);
                    float edge = f2 - f1;
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 45f + p.V(x, y, 8, 2) * 0.35f;
                    field[x + y * Paint.S] = edge < 1.3f ? dist : 99;
                }
            for (int s = 0; s < 8; s++)
            {
                var t = new Texture2D(Paint.S, Paint.S, TextureFormat.R8, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[Paint.S * Paint.S];
                float reach = (s + 1) / 8f * 1.1f;
                for (int i = 0; i < px.Length; i++) { float v = field[i] < reach ? 1 : 0; px[i] = new Color(v, v, v, 1); }
                t.SetPixels(px); t.Apply();
                stages[s] = t;
            }
            return stages;
        }
    }
}
