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
        public bool HeadInWater { get; private set; }
        public bool Sneaking { get; private set; }
        public bool Sprinting { get; private set; }

        public float Health = 20, MaxHealth = 20;
        public float Air = 10, MaxAir = 10;
        public float Yaw, Pitch;
        public System.Action Died;
        public System.Action<float> Hurt;

        const float HalfW = 0.3f, Tall = 1.8f, TallSneak = 1.5f, Eye = 1.62f, EyeSneak = 1.32f;
        const float Gravity = 30f, JumpSpeed = 8.6f, Walk = 4.4f, Run = 6.2f, SneakSpeed = 1.6f, FlySpeed = 11f, FlyFast = 24f;

        float _fallStart;
        float _lastSpace = -1;
        float _stepDist;
        float _bob, _eye = Eye, _fov;
        float _regen, _drown, _sinceHurt = 10;
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

            Sprinting = sprintKey && move.y > 0.1f && !Sneaking;
            bool wantSneak = sneakKey && !Flying;
            if (!wantSneak && Sneaking && Blocked(Feet, Tall)) wantSneak = true;   // no standing up under a slab
            Sneaking = wantSneak;

            int fx = Mathf.FloorToInt(Feet.x), fz = Mathf.FloorToInt(Feet.z);
            InWater = World.Get(fx, Mathf.FloorToInt(Feet.y + 0.4f), fz) == Blocks.Water;
            HeadInWater = World.Get(Mathf.FloorToInt(EyePos.x), Mathf.FloorToInt(EyePos.y), Mathf.FloorToInt(EyePos.z)) == Blocks.Water;
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
            else if (InWater)
            {
                float sp = Walk * 0.55f;
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
                if (jump && OnGround) { Velocity.y = JumpSpeed; if (Sprinting) { Velocity.x += fwd.x * 1.2f; Velocity.z += fwd.z * 1.2f; } }
            }

            bool wasGround = OnGround;
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
            if (Creative) { Health = MaxHealth; Air = MaxAir; return; }
            _sinceHurt += dt;
            if (HeadInWater)
            {
                Air -= dt;
                if (Air <= 0) { Air = 0; _drown += dt; if (_drown > 1) { _drown = 0; Damage(2); } }
            }
            else Air = Mathf.Min(MaxAir, Air + dt * 4);
            // Heal slowly when out of harm.
            if (_sinceHurt > 6 && Health < MaxHealth)
            {
                _regen += dt;
                if (_regen > 3) { _regen = 0; Health = Mathf.Min(MaxHealth, Health + 1); }
            }
            if (Feet.y < -20) Damage(4);
        }

        public void Damage(float amount)
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
        }

        // ------------------------------------------------------------------ collision

        bool Blocked(Vector3 feet, float height)
        {
            int x0 = Mathf.FloorToInt(feet.x - HalfW + 0.001f), x1 = Mathf.FloorToInt(feet.x + HalfW - 0.001f);
            int y0 = Mathf.FloorToInt(feet.y + 0.001f), y1 = Mathf.FloorToInt(feet.y + height - 0.001f);
            int z0 = Mathf.FloorToInt(feet.z - HalfW + 0.001f), z1 = Mathf.FloorToInt(feet.z + HalfW - 0.001f);
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if (World.SolidAt(x, y, z)) return true;
            return false;
        }

        bool Supported(Vector3 feet)
        {
            var below = feet; below.y -= 0.05f;
            return Blocked(below, 0.04f);
        }

        void MoveAll(Vector3 delta)
        {
            int steps = Mathf.CeilToInt(delta.magnitude / 0.35f);
            if (steps < 1) steps = 1;
            var d = delta / steps;
            OnGround = false;
            for (int i = 0; i < steps; i++)
            {
                MoveAxis(1, d.y);
                MoveAxis(0, d.x);
                MoveAxis(2, d.z);
            }
            if (!OnGround && Velocity.y <= 0 && Supported(Feet)) OnGround = true;
        }

        void MoveAxis(int axis, float d)
        {
            if (d == 0) return;
            var p = Feet;
            var next = p; next[axis] += d;
            float h = Height;
            if (!Blocked(next, h))
            {
                // Sneaking: don't walk off an edge.
                if (axis != 1 && Sneaking && OnGroundNow(p) && !Supported(next)) { Velocity[axis] = 0; return; }
                Feet = next;
                return;
            }
            // Slide flush against what we hit.
            if (axis == 1)
            {
                if (d < 0) { next.y = Mathf.Floor(p.y + d) + 1; OnGround = true; }
                else next.y = Mathf.Floor(p.y + h + d) - h - 0.001f;
                if (Blocked(next, h)) next.y = p.y;
            }
            else
            {
                float half = HalfW;
                if (d > 0) next[axis] = Mathf.Floor(p[axis] + half + d) - half - 0.001f;
                else next[axis] = Mathf.Floor(p[axis] - half + d) + 1 + half + 0.001f;
                if (Blocked(next, h)) next[axis] = p[axis];
            }
            Feet = next;
            Velocity[axis] = 0;
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
