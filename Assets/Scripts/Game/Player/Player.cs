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
        public Vector3 LastGround;            // where the feet last stood on something (for a satchel after a fall into the sky)
        public float Yaw, Pitch;
        public System.Action Died;
        public System.Action<float> Hurt;

        // In a boat or a minecart: the vehicle goes where the player goes until they get out (sneak).
        public MobKind? Riding;
        public float RideYaw, RideSpeed;
        public System.Action<MobKind, Vector3, float> GotOut;         // leave it parked here
        public System.Func<int, int, int, bool> Powered;              // is a booster rail switched on
        Vector3Int _railCell; int _railFrom; float _railT; bool _onRail;

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

        float _lastForward = -9; bool _tapSprint;
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
                if (Keys.Down(Act.Forward)) move.y += 1;
                if (Keys.Down(Act.Back)) move.y -= 1;
                if (Keys.Down(Act.Right)) move.x += 1;
                if (Keys.Down(Act.Left)) move.x -= 1;
                jump = Keys.Down(Act.Jump);
                jumpPressed = Keys.Pressed(Act.Jump);
                // Tapping forward twice quickly sprints for as long as forward is held.
                if (Keys.Pressed(Act.Forward)) { if (Time.unscaledTime - _lastForward < 0.3f) _tapSprint = true; _lastForward = Time.unscaledTime; }
                if (!Keys.Down(Act.Forward)) _tapSprint = false;
                sprintKey = Keys.Down(Act.Sprint) || _tapSprint;
                sneakKey = Keys.Down(Act.Sneak);
                down = sneakKey;
            }
            if (move.sqrMagnitude > 1) move.Normalize();

            if (Riding != null)
            {
                Sneaking = Sprinting = false; Flying = false;
                InWater = InLava = OnLadder = false;
                HeadInWater = Blocks.IsWater(World.Get(Mathf.FloorToInt(EyePos.x), Mathf.FloorToInt(EyePos.y), Mathf.FloorToInt(EyePos.z)));
                if (InputEnabled && kb != null && Keys.Pressed(Act.Sneak)) GetOut();
                else
                {
                    if (Riding == MobKind.Boat) RideBoat(dt, move); else RideCart(dt, move);
                    _fallStart = Feet.y;
                    Survival(dt);
                    UpdateCamera(dt, 0);
                    return;
                }
            }

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
                    ushort under = World.Get(fx, Mathf.FloorToInt(Feet.y - 0.2f), fz);
                    if (under != 0) Sfx.I?.Block("step", under, Sneaking ? 0.25f : 0.5f);
                }
                if (Settings.ViewBob) _bob += horiz * dt * 1.9f;
            }

            Survival(dt);
            UpdateCamera(dt, horiz);
        }

        // ------------------------------------------------------------------ boats and carts

        public void GetIn(MobKind kind, Vector3 at, float yaw)
        {
            Riding = kind; Feet = at; RideYaw = yaw; RideSpeed = 0; Velocity = Vector3.zero; _onRail = false;
            Sfx.I?.Play("door_close", 0.4f, 0.8f);
        }

        public void GetOut()
        {
            if (Riding == null) return;
            var kind = Riding.Value; var at = Feet;
            Riding = null; RideSpeed = 0; Velocity = Vector3.zero;
            GotOut?.Invoke(kind, at, RideYaw);
            // Step off to the side (or up on top if there's no room).
            var side = Quaternion.Euler(0, RideYaw, 0) * Vector3.right;
            foreach (var off in new[] { side * 1.2f, -side * 1.2f, Quaternion.Euler(0, RideYaw, 0) * Vector3.forward * 1.4f, Quaternion.Euler(0, RideYaw, 0) * Vector3.back * 1.4f })
            {
                for (int up = 0; up <= 1; up++)
                {
                    var p = at + off + Vector3.up * (up + 0.05f);
                    if (!Blocked(p, Tall)) { Feet = p; return; }
                }
            }
            Feet = at + Vector3.up * 0.9f;
        }

        void RideBoat(float dt, Vector2 move)
        {
            RideYaw += move.x * 95 * dt;
            int fx = Mathf.FloorToInt(Feet.x), fz = Mathf.FloorToInt(Feet.z), fy = Mathf.FloorToInt(Feet.y + 0.3f);
            float surface = float.NaN;
            for (int y = fy + 1; y >= fy - 2; y--)
                if (Blocks.IsWater(World.Get(fx, y, fz)) && !Blocks.IsWater(World.Get(fx, y + 1, fz))) { surface = y + 0.875f; break; }
            var fwd = Quaternion.Euler(0, RideYaw, 0) * Vector3.forward;
            bool afloat = !float.IsNaN(surface) && Feet.y < surface + 0.2f;
            if (afloat)
            {
                RideSpeed += move.y * 7 * dt;
                RideSpeed *= Mathf.Exp(-dt * 0.7f);
                RideSpeed = Mathf.Clamp(RideSpeed, -2.5f, 8f);
                Velocity.y = (surface - 0.32f - Feet.y) * 6;
            }
            else
            {
                // Dragged over land: barely moves.
                RideSpeed += move.y * 2 * dt;
                RideSpeed *= Mathf.Exp(-dt * 5);
                RideSpeed = Mathf.Clamp(RideSpeed, -0.6f, 1.2f);
                Velocity.y -= Gravity * dt;
                if (Velocity.y < -40) Velocity.y = -40;
            }
            Velocity.x = fwd.x * RideSpeed; Velocity.z = fwd.z * RideSpeed;
            _hitWall = false;
            MoveAll(Velocity * dt);
            if (_hitWall) RideSpeed *= 0.2f;
            if (afloat && Mathf.Abs(RideSpeed) > 1.5f && Random.value < dt * 3) Sfx.I?.Play("splash", 0.08f, 1.4f);
        }

        void RideCart(float dt, Vector2 move)
        {
            if (!_onRail)
            {
                var f = Vector3Int.FloorToInt(Feet + Vector3.up * 0.1f);
                foreach (var c in new[] { f, f + Vector3Int.down })
                    if (Rails.Is(World.Get(c.x, c.y, c.z)))
                    {
                        _railCell = c; _onRail = true;
                        var (a, b, _) = Rails.Ends(World.Get(c.x, c.y, c.z));
                        // Start out heading the way the player looks.
                        var look = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
                        float da = look.x * Rails.DX[a] + look.z * Rails.DZ[a];
                        _railFrom = da > 0 ? b : a;
                        _railT = 0.5f;
                        break;
                    }
                if (!_onRail)
                {
                    Velocity.x = Velocity.z = 0;
                    Velocity.y -= Gravity * dt;
                    MoveAll(Velocity * dt);
                    return;
                }
            }
            ushort id = World.Get(_railCell.x, _railCell.y, _railCell.z);
            if (!Rails.Is(id)) { _onRail = false; return; }
            var (ea, eb, up) = Rails.Ends(id);
            int to = _railFrom == ea ? eb : ea;
            var travel = new Vector3(Rails.DX[to] - Rails.DX[_railFrom], 0, Rails.DZ[to] - Rails.DZ[_railFrom]).normalized;
            // Downhill speeds up, uphill slows.
            if (up >= 0) RideSpeed += (up == to ? -1 : 1) * 6f * dt;
            // The rider can give it a shove the way they're looking.
            var lookFwd = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            if (Mathf.Abs(RideSpeed) < 3.5f) RideSpeed += Vector3.Dot(lookFwd, travel) * move.y * 4f * dt;
            if (Rails.Booster(id))
            {
                bool on = Powered == null || Powered(_railCell.x, _railCell.y, _railCell.z);
                if (on) RideSpeed = Mathf.Abs(RideSpeed) < 0.05f ? 0.5f * Mathf.Sign(Vector3.Dot(lookFwd, travel) + 0.001f) : RideSpeed + Mathf.Sign(RideSpeed) * 18f * dt;
                else RideSpeed *= Mathf.Exp(-dt * 6);
            }
            RideSpeed *= Mathf.Exp(-dt * 0.12f);
            if (Mathf.Abs(RideSpeed) < 0.02f && up < 0) RideSpeed = 0;
            RideSpeed = Mathf.Clamp(RideSpeed, -10, 10);
            if (RideSpeed < 0) { _railFrom = to; _railT = 1 - _railT; RideSpeed = -RideSpeed; to = _railFrom == ea ? eb : ea; }
            _railT += RideSpeed * dt / Rails.Length(id);
            int guard = 0;
            while (_railT >= 1 && guard++ < 4)
            {
                var (a2, b2, up2) = Rails.Ends(id);
                int exit = _railFrom == a2 ? b2 : a2;
                var next = new Vector3Int(_railCell.x + Rails.DX[exit], _railCell.y + (up2 == exit ? 1 : 0), _railCell.z + Rails.DZ[exit]);
                ushort nid = World.Get(next.x, next.y, next.z);
                if (!Rails.Is(nid) && Rails.Is(World.Get(next.x, next.y - 1, next.z))) { next.y--; nid = World.Get(next.x, next.y, next.z); }
                int entry = (exit + 2) & 3;
                if (!Rails.Is(nid) || !Rails.Joins(nid, entry))
                {
                    // The end of the line: it bumps to a stop.
                    if (RideSpeed > 2) Sfx.I?.Play("anvil", 0.15f, 0.6f);
                    _railT = 1; RideSpeed = 0;
                    break;
                }
                _railT = (_railT - 1) * Rails.Length(id) / Rails.Length(nid);
                _railCell = next; _railFrom = entry; id = nid;
            }
            var (lx, ly, lz) = Rails.Along(id, _railFrom, Mathf.Clamp01(_railT));
            var (ta, tb, _) = Rails.Ends(id);
            int toNow = _railFrom == ta ? tb : ta;
            var p0 = Rails.Along(id, _railFrom, Mathf.Clamp01(_railT - 0.05f)); var p1 = Rails.Along(id, _railFrom, Mathf.Clamp01(_railT + 0.05f));
            float yawNow = Mathf.Atan2(p1.x - p0.x, p1.z - p0.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(p1.x - p0.x) + Mathf.Abs(p1.z - p0.z) > 0.001f) RideYaw = yawNow;
            _ = toNow;
            var target = new Vector3(_railCell.x + lx, _railCell.y + ly, _railCell.z + lz);
            Velocity = (target - Feet) / Mathf.Max(dt, 0.001f);
            Feet = target;
            if (RideSpeed > 1 && Random.value < dt * RideSpeed * 0.6f) Sfx.I?.Block("step", Blocks.Rail, 0.12f);
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
            float eye = Riding == MobKind.Boat ? 1.05f : Riding == MobKind.Cart ? 1.25f : Sneaking ? EyeSneak : Eye;
            _eye = Mathf.Lerp(_eye, eye, 1 - Mathf.Exp(-dt * 14));
            float bobAmt = OnGround && !Flying ? Mathf.Clamp01(horiz / Walk) : 0;
            float bx = Mathf.Sin(_bob * Mathf.PI) * 0.035f * bobAmt, by = Mathf.Abs(Mathf.Cos(_bob * Mathf.PI)) * 0.05f * bobAmt;
            Head.localPosition = new Vector3(bx, _eye + by, 0);
            Head.localRotation = Quaternion.Euler(Pitch, 0, bx * 6);
            float fov = Settings.Fov + (Sprinting && horiz > Walk ? 8 : 0) + (Flying && Sprinting ? 6 : 0);
            _fov = Mathf.Lerp(_fov, fov, 1 - Mathf.Exp(-dt * 8));
            Cam.fieldOfView = _fov;
            // Seen from behind or from in front (F5): the camera stands off along the view, stopping short of walls.
            if (View == 0) { Cam.transform.localPosition = Vector3.zero; Cam.transform.localRotation = Quaternion.identity; ViewBack = 0; }
            else
            {
                var dir = View == 1 ? -Head.forward : Head.forward;
                var eyeAt = Head.position;
                float d = 0.3f;
                for (; d < 4.2f; d += 0.15f)
                {
                    var p = eyeAt + dir * (d + 0.3f);
                    var def = Blocks.Get(World.Get(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)));
                    if (def.Solid && def.Shape == Shape.Cube) break;
                }
                ViewBack = d;
                Cam.transform.position = eyeAt + dir * d;
                Cam.transform.rotation = View == 1 ? Head.rotation : Quaternion.LookRotation(-Head.forward, Vector3.up);
            }
        }

        public int View;                 // 0 through the eyes, 1 from behind, 2 from in front
        public float ViewBack;           // how far the camera stands off

        void Land()
        {
            float fall = _fallStart - Feet.y;
            ushort under = World.Get(Mathf.FloorToInt(Feet.x), Mathf.FloorToInt(Feet.y - 0.2f), Mathf.FloorToInt(Feet.z));
            if (fall > 1.2f && under != 0) Sfx.I?.Block("step", under, Mathf.Clamp01(fall / 6) * 0.8f + 0.3f);
            if (!Creative && !InWater && fall > 3.6f && under != Blocks.HayBale) Damage(Mathf.Floor(fall - 3) * (1 - 0.12f * Inv.SoftLanding));
            else if (!Creative && !InWater && fall > 3.6f) Damage(Mathf.Floor((fall - 3) * 0.2f));
        }

        void Survival(float dt)
        {
            if (OnGround && Feet.y > 1) LastGround = Feet;
            if (Creative)
            {
                Health = MaxHealth; Air = MaxAir; Food = 20; _lastFeet = Feet;
                // Building can't kill you, and neither can a slip off a sky island: back to the last ground.
                if (Feet.y < -40) { Feet = LastGround + Vector3.up * 0.5f; Velocity = Vector3.zero; _fallStart = Feet.y; Toast?.Invoke("Caught you!"); }
                return;
            }
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
            if (!Creative && World.Get(Mathf.FloorToInt(Feet.x), Mathf.FloorToInt(Feet.y + 0.2f), Mathf.FloorToInt(Feet.z)) == Blocks.Fire) Burning = Mathf.Max(Burning, 4);
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
        public int XpLevel, XpPoints;
        public System.Action<int> LevelledUp;
        public void AddXp(int n)
        {
            if (n <= 0) return;
            int before = XpLevel;
            Xp.Add(ref XpLevel, ref XpPoints, n);
            Sfx.I?.Play("xp", 0.35f, 0.9f + Random.value * 0.4f);
            if (XpLevel > before && XpLevel % 5 == 0) Sfx.I?.Play("levelup", 0.6f);
            if (XpLevel > before) LevelledUp?.Invoke(XpLevel);
        }

        public void Hit(float amount, Vector3 from)
        {
            if (Creative || Health <= 0) return;
            int pts = Inv.ArmourPoints;
            float taken = amount * (1 - Mathf.Min(20, pts) * 0.04f) * (1 - Mathf.Min(0.64f, Inv.Warding * 0.04f));
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

        ushort CollideGet(int x, int y, int z)
        {
            if (y < 0) return World.Gen.Kind == WorldKind.Sky ? (ushort)0 : Blocks.Bedrock;    // under the sky islands there's nothing
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
