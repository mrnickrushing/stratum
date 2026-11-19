using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stratum.Game
{
    // First-person body: walking, sprinting, sneaking (won't step off edges), jumping, swimming, flying in
    // Creative, and collision against the voxel grid (no physics engine involved).
    public sealed class Player : MonoBehaviour
    {
        public VoxelWorld World;
        public Camera Cam;
        public Transform Head;
        public readonly Inventory Inv = new Inventory();
        public bool Creative;
        public bool Flying;
        public bool InputEnabled = true;

        public Vector3 Velocity;
        public bool OnGround { get; private set; }
        public bool InWater { get; private set; }
        public bool InLava { get; private set; }
        public bool OnLadder { get; private set; }
        public float Burning;                                   // seconds left on fire
        public bool HeadInWater { get; private set; }
        public bool Sneaking { get; private set; }
        public bool Sprinting { get; private set; }

        public float Health = 20, MaxHealth = 20;
        public float Air = 10, MaxAir = 10;
        public float Food = 20, Saturation = 5, Exhaustion;   // hunger: 20 is full
        public int Difficulty = 2;                            // 0 peaceful, 1 easy, 2 normal
        public bool Asleep;
        public System.Action<string> Toast;
        public float Yaw, Pitch;
        public System.Action Died;
        public System.Action<float> Hurt;

        const float HalfW = 0.3f, Tall = 1.8f, TallSneak = 1.5f, Eye = 1.62f, EyeSneak = 1.32f;
        const float Gravity = 30f, JumpSpeed = 8.6f, Walk = 4.4f, Run = 6.2f, SneakSpeed = 1.6f, FlySpeed = 11f, FlyFast = 24f;

        float _fallStart;
        float _lastSpace = -1;
        float _stepDist;
        float _bob, _eye = Eye, _fov;
        float _regen, _drown, _sinceHurt = 10, _starve, _lavaHurt, _burnHurt;
        bool _hitWall;
        Vector3 _lastFeet;
        bool _wasInWater;

        float Height => Sneaking ? TallSneak : Tall;

        public static Player Create(VoxelWorld world, Camera cam)
        {
            var go = new GameObject("Player");
            var p = go.AddComponent<Player>();
            p.World = world;
            p.Cam = cam;
            var head = new GameObject("Head").transform;
            head.SetParent(go.transform, false);
            head.localPosition = new Vector3(0, Eye, 0);
            p.Head = head;
            cam.transform.SetParent(head, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            p._fov = Settings.Fov;
            return p;
        }

        public Vector3 Feet { get => transform.position; set => transform.position = value; }
        public Vector3 EyePos => Head.position;

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0) return;
            if (!World.IsReady(Mathf.FloorToInt(Feet.x), Mathf.FloorToInt(Feet.z))) return;   // wait for the ground to exist
            var kb = Keyboard.current; var mouse = Mouse.current;

            Look(mouse);
            Vector2 move = Vector2.zero;
            bool jump = false, jumpPressed = false, down = false, sprintKey = false, sneakKey = false;
            if (InputEnabled && kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed) move.y -= 1;
                if (kb.dKey.isPressed) move.x += 1;
                if (kb.aKey.isPressed) move.x -= 1;
                jump = kb.spaceKey.isPressed;
                jumpPressed = kb.spaceKey.wasPressedThisFrame;
                sprintKey = kb.leftShiftKey.isPressed;
                sneakKey = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
                down = sneakKey;
            }
            if (move.sqrMagnitude > 1) move.Normalize();

            // Double-tap space to fly in Creative.
            if (jumpPressed && Creative)
            {
                if (Time.time - _lastSpace < 0.3f) { Flying = !Flying; Velocity.y = 0; _lastSpace = -1; }
                else _lastSpace = Time.time;
            }
            if (!Creative) Flying = false;

            var fwd = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, Yaw, 0) * Vector3.right;
            var wish = fwd * move.y + right * move.x;

            Sprinting = sprintKey && move.y > 0.1f && !Sneaking && (Food > 6 || Creative);
            bool wantSneak = sneakKey && !Flying;
            if (!wantSneak && Sneaking && Blocked(Feet, Tall)) wantSneak = true;   // no standing up under a slab
            Sneaking = wantSneak;

            int fx = Mathf.FloorToInt(Feet.x), fz = Mathf.FloorToInt(Feet.z);
            InWater = Blocks.IsWater(World.Get(fx, Mathf.FloorToInt(Feet.y + 0.4f), fz));
            InLava = Blocks.IsLava(World.Get(fx, Mathf.FloorToInt(Feet.y + 0.4f), fz)) || Blocks.IsLava(World.Get(fx, Mathf.FloorToInt(Feet.y + 1.2f), fz));
            HeadInWater = Blocks.IsWater(World.Get(Mathf.FloorToInt(EyePos.x), Mathf.FloorToInt(EyePos.y), Mathf.FloorToInt(EyePos.z)));
            OnLadder = Blocks.Get(World.Get(fx, Mathf.FloorToInt(Feet.y + 0.1f), fz)).Climb || Blocks.Get(World.Get(fx, Mathf.FloorToInt(Feet.y + 1.0f), fz)).Climb;
            if (InWater && !_wasInWater && Velocity.y < -4) Sfx.I?.Play("splash", 0.7f);
            _wasInWater = InWater;

            if (Flying)
            {
                float sp = Sprinting ? FlyFast : FlySpeed;
                var target = wish * sp;
                target.y = (jump ? 1 : 0) * sp * 0.8f - (down ? 1 : 0) * sp * 0.8f;
                Velocity = Vector3.Lerp(Velocity, target, 1 - Mathf.Exp(-dt * 10));
                if (OnGround && down) Flying = false;
            }
            else if (InWater || InLava)
            {
                float sp = Walk * (InLava ? 0.3f : 0.55f);
                var target = wish * sp;
                Velocity.x = Mathf.Lerp(Velocity.x, target.x, 1 - Mathf.Exp(-dt * 6));
                Velocity.z = Mathf.Lerp(Velocity.z, target.z, 1 - Mathf.Exp(-dt * 6));
                Velocity.y -= 9f * dt;
                if (jump) Velocity.y = Mathf.Lerp(Velocity.y, 4f, 1 - Mathf.Exp(-dt * 8));
                Velocity.y = Mathf.Max(Velocity.y * (1 - dt * 2.5f), -3f);
                _fallStart = Feet.y;
            }
            else
            {
                float sp = Sneaking ? SneakSpeed : Sprinting ? Run : Walk;
                var target = wish * sp;
                float accel = OnGround ? 14 : 3;
                Velocity.x = Mathf.Lerp(Velocity.x, target.x, 1 - Mathf.Exp(-dt * accel));
                Velocity.z = Mathf.Lerp(Velocity.z, target.z, 1 - Mathf.Exp(-dt * accel));
                Velocity.y -= Gravity * dt;
                if (Velocity.y < -60) Velocity.y = -60;
                if (jump && OnGround) { Velocity.y = JumpSpeed; Exhaustion += Sprinting ? 0.2f : 0.05f; if (Sprinting) { Velocity.x += fwd.x * 1.2f; Velocity.z += fwd.z * 1.2f; } }
                // Ladders: walk into them or jump to climb, sneak to hold on, otherwise slide down slowly.
                if (OnLadder)
                {
                    _fallStart = Feet.y;
                    if (jump || move.sqrMagnitude > 0.01f && _hitWall) Velocity.y = 2.6f;
                    else if (Sneaking) Velocity.y = 0;
                    else Velocity.y = Mathf.Max(Velocity.y, -2.6f);
                }
            }

            bool wasGround = OnGround;
            _hitWall = false;
            MoveAll(Velocity * dt);
            if (!wasGround && OnGround) Land();
            if (OnGround || Flying) _fallStart = Feet.y;
            else if (Feet.y > _fallStart) _fallStart = Feet.y;

            // Steps.
            float horiz = new Vector2(Velocity.x, Velocity.z).magnitude;
            if (OnGround && horiz > 0.5f)
            {
                _stepDist += horiz * dt;
                if (_stepDist > (Sprinting ? 2.6f : 2.1f))
                {
                    _stepDist = 0;
                    byte under = World.Get(fx, Mathf.FloorToInt(Feet.y - 0.2f), fz);
                    if (under != 0) Sfx.I?.Block("step", under, Sneaking ? 0.25f : 0.5f);
                }
                if (Settings.ViewBob) _bob += horiz * dt * 1.9f;
            }

            Survival(dt);
            UpdateCamera(dt, horiz);
        }

        void Look(Mouse mouse)
        {
            if (!InputEnabled || mouse == null || Cursor.lockState != CursorLockMode.Locked) return;
            var d = mouse.delta.ReadValue() * 0.08f * Settings.Sensitivity;
            Yaw += d.x;
            Pitch += Settings.InvertY ? d.y : -d.y;
            Pitch = Mathf.Clamp(Pitch, -89.5f, 89.5f);
        }

        void UpdateCamera(float dt, float horiz)
        {
            transform.rotation = Quaternion.Euler(0, Yaw, 0);
            float eye = Sneaking ? EyeSneak : Eye;
            _eye = Mathf.Lerp(_eye, eye, 1 - Mathf.Exp(-dt * 14));
            float bobAmt = OnGround && !Flying ? Mathf.Clamp01(horiz / Walk) : 0;
            float bx = Mathf.Sin(_bob * Mathf.PI) * 0.035f * bobAmt, by = Mathf.Abs(Mathf.Cos(_bob * Mathf.PI)) * 0.05f * bobAmt;
            Head.localPosition = new Vector3(bx, _eye + by, 0);
            Head.localRotation = Quaternion.Euler(Pitch, 0, bx * 6);
            float fov = Settings.Fov + (Sprinting && horiz > Walk ? 8 : 0) + (Flying && Sprinting ? 6 : 0);
            _fov = Mathf.Lerp(_fov, fov, 1 - Mathf.Exp(-dt * 8));
            Cam.fieldOfView = _fov;
        }

        void Land()
        {
            float fall = _fallStart - Feet.y;
            byte under = World.Get(Mathf.FloorToInt(Feet.x), Mathf.FloorToInt(Feet.y - 0.2f), Mathf.FloorToInt(Feet.z));
            if (fall > 1.2f && under != 0) Sfx.I?.Block("step", under, Mathf.Clamp01(fall / 6) * 0.8f + 0.3f);
            if (!Creative && !InWater && fall > 3.6f) Damage(Mathf.Floor(fall - 3));
        }

        void Survival(float dt)
        {
            if (Creative) { Health = MaxHealth; Air = MaxAir; Food = 20; _lastFeet = Feet; return; }
            _sinceHurt += dt;
            if (HeadInWater)
            {
                Air -= dt;
                if (Air <= 0) { Air = 0; _drown += dt; if (_drown > 1) { _drown = 0; Damage(2); } }
            }
            else Air = Mathf.Min(MaxAir, Air + dt * 4);
            if (Feet.y < -20) Damage(4);

            // Lava burns hard and sets you alight; water puts the fire out.
            if (InLava) { Burning = 8; if ((_lavaHurt -= dt) <= 0) { _lavaHurt = 0.5f; Damage(4); } }
            else _lavaHurt = 0;
            if (InWater) Burning = 0;
            if (Burning > 0)
            {
                Burning -= dt;
                if ((_burnHurt -= dt) <= 0) { _burnHurt = 1; Damage(1, false); }
            }

            // Hunger: moving, swimming, fighting and healing all use it up; a full stomach heals, an empty one hurts.
            var moved = Feet - _lastFeet; moved.y = 0; _lastFeet = Feet;
            float dist = Mathf.Min(moved.magnitude, 2);
            if (Sprinting) Exhaustion += dist * 0.1f; else if (InWater) Exhaustion += dist * 0.015f;
            if (Difficulty == 0) { Food = Mathf.Min(20, Food + dt * 0.5f); Exhaustion = 0; }
            while (Exhaustion >= 4)
            {
                Exhaustion -= 4;
                if (Saturation > 0) Saturation = Mathf.Max(0, Saturation - 1); else Food = Mathf.Max(0, Food - 1);
            }
            if (Food >= 18 && Health < MaxHealth && _sinceHurt > 1)
            {
                _regen += dt;
                float every = Saturation > 0 && Food >= 20 ? 0.5f : 4f;
                if (_regen > every) { _regen = 0; Health = Mathf.Min(MaxHealth, Health + 1); Exhaustion += 6; }
            }
            else if (Difficulty == 0 && Health < MaxHealth) { _regen += dt; if (_regen > 2) { _regen = 0; Health = Mathf.Min(MaxHealth, Health + 1); } }
            if (Food <= 0)
            {
                _starve += dt;
                // Hunger alone never kills: on easy it stops at half health, on normal at half a heart.
                float floor = Difficulty == 1 ? 10 : 1;
                if (_starve > 4) { _starve = 0; if (Health > floor) Damage(1, false); }
            }
            else _starve = 0;
        }

        public bool Hungry => Food < 20;
        public void ResetFall() => _fallStart = Feet.y;

        public void Eat(ItemDef food)
        {
            Food = Mathf.Min(20, Food + food.Food);
            Saturation = Mathf.Min(Food, Saturation + food.Saturation);
        }

        // A blow from outside (a creature): armour takes some of it and the body is knocked back.
        public void Hit(float amount, Vector3 from)
        {
            if (Creative || Health <= 0) return;
            int pts = Inv.ArmourPoints;
            float taken = amount * (1 - Mathf.Min(20, pts) * 0.04f);
            if (pts > 0) { var broke = Inv.WearArmour(); if (broke != null) { Sfx.I?.Play("break_tool", 0.8f); Toast?.Invoke(broke + " broke"); } }
            var away = Feet - from; away.y = 0;
            if (away.sqrMagnitude > 0.001f) { away.Normalize(); Velocity.x += away.x * 7; Velocity.z += away.z * 7; }
            if (OnGround) Velocity.y = Mathf.Max(Velocity.y, 5.5f);
            Exhaustion += 0.1f;
            Damage(taken);
        }

        public void Damage(float amount) => Damage(amount, true);

        public void Damage(float amount, bool flinch)
        {
            if (Creative || Health <= 0 || amount <= 0) return;
            Health -= amount;
            _sinceHurt = 0;
            Sfx.I?.Play("hurt", 0.8f);
            Hurt?.Invoke(amount);
            if (Health <= 0) { Health = 0; Died?.Invoke(); }
        }

        public void Respawn(Vector3 at)
        {
            Health = MaxHealth; Air = MaxAir; Velocity = Vector3.zero; Feet = at; _fallStart = at.y; Flying = false;
            Food = 20; Saturation = 5; Exhaustion = 0; _lastFeet = at; Asleep = false; Burning = 0;
        }

        // ------------------------------------------------------------------ collision

        byte CollideGet(int x, int y, int z)
        {
            if (y < 0) return Blocks.Bedrock;
            if (y >= Chunk.Height) return 0;
            // A chunk that isn't here yet counts as solid, so nothing falls out of the world.
            return World.IsReady(x, z) ? World.Get(x, y, z) : Blocks.Bedrock;
        }

        bool Blocked(Vector3 feet, float height) => Body.Blocked(CollideGet, feet.x, feet.y, feet.z, HalfW, height);

        bool Supported(Vector3 feet) => Body.Blocked(CollideGet, feet.x - HalfW, feet.y - 0.05f, feet.z - HalfW, feet.x + HalfW, feet.y - 0.001f, feet.z + HalfW);

        void MoveAll(Vector3 delta)
        {
            int steps = Mathf.CeilToInt(delta.magnitude / 0.35f);
            if (steps < 1) steps = 1;
            var d = delta / steps;
            bool wasGround = OnGround;
            OnGround = false;
            for (int i = 0; i < steps; i++)
            {
                MoveAxis(1, d.y);
                MoveAxis(0, d.x, wasGround);
                MoveAxis(2, d.z, wasGround);
            }
            if (!OnGround && Velocity.y <= 0 && Supported(Feet)) OnGround = true;
        }

        void MoveAxis(int axis, float d, bool canStep = false)
        {
            if (d == 0) return;
            var p = Feet;
            float h = Height;
            float moved = Body.Sweep(CollideGet, p.x, p.y, p.z, HalfW, h, axis, d);
            var next = p; next[axis] += moved;
            // Sneaking: don't walk off an edge.
            if (axis != 1 && Sneaking && OnGroundNow(p) && !Supported(next)) { Velocity[axis] = 0; return; }
            if (Mathf.Abs(moved) < Mathf.Abs(d) - 1e-5f)
            {
                // Walk up anything half a block or so high (slabs, beds, stairs) without jumping.
                if (axis != 1 && canStep && !Flying)
                {
                    for (float up = 0.125f; up <= 0.625f; up += 0.125f)
                    {
                        var raised = p; raised.y += up;
                        if (Blocked(raised, h)) break;
                        float m2 = Body.Sweep(CollideGet, raised.x, raised.y, raised.z, HalfW, h, axis, d);
                        if (Mathf.Abs(m2) >= Mathf.Abs(d) - 1e-5f)
                        {
                            raised[axis] += m2;
                            float down = Body.Sweep(CollideGet, raised.x, raised.y, raised.z, HalfW, h, 1, -up);
                            raised.y += down;
                            Feet = raised;
                            OnGround = true;
                            _eye -= up + down;          // the view eases up the step instead of jumping
                            return;
                        }
                    }
                }
                if (axis == 1 && d < 0) OnGround = true;
                if (axis != 1) _hitWall = true;
                Velocity[axis] = 0;
            }
            Feet = next;
        }

        bool OnGroundNow(Vector3 p) => Supported(p);

        // Would a block at (x, y, z) overlap the body?
        public bool Overlaps(int x, int y, int z)
        {
            var f = Feet;
            return x + 1 > f.x - HalfW && x < f.x + HalfW && z + 1 > f.z - HalfW && z < f.z + HalfW && y + 1 > f.y && y < f.y + Height;
        }
    }
}
