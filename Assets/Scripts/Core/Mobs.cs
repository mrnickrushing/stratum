using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    public enum MobKind : byte { Cow, Pig, Sheep, Chicken, Ghoul, BoneArcher, Crawler, Arrow, EmberImp, Colossus, Villager, Boat, Cart,
        // 0.12: more wildlife
        Deer, Fox, Rabbit, Penguin, Goat, Camel,
        // 0.14
        Bloat, Hound,
        // 0.15
        Glob, Shade, Snapper, King, Count }

    public sealed class MobDef
    {
        public MobKind Kind;
        public string Name;
        public float Health = 10, Speed = 1.4f, HalfW = 0.4f, Height = 1.2f;
        public bool Hostile, Burns, Climbs, Ranged, Floats, Boss, Fireproof, Vehicle;
        public bool Hops, Blinks, Swims;       // gets about in bounds; steps through the air from place to place; lives in water
        public float Damage, Reach = 1.7f, AttackEvery = 1.1f, Sight = 18;
        public (string item, int min, int max, float chance)[] Drops = Array.Empty<(string, int, int, float)>();
        public int Xp;
    }

    public sealed class Mob
    {
        public int Id;
        public MobKind Kind;
        public MobDef Def;
        public float X, Y, Z, VX, VY, VZ, Yaw, Pitch;
        public float Health;
        public bool OnGround, InWater, InLava;
        public int Target = -1;               // player id
        public float Think, Cool, Hurt, Dead, Age, Flee, Burn, Windup, Idle;
        public float GoalX, GoalZ; public bool HasGoal;
        public float FromX, FromZ;            // where the last hit came from
        public bool Sheared; public float Regrow;
        public int Owner;                     // arrows: the player (>0) or creature (<0) that loosed it
        public float ArrowDamage;
        public bool Stuck;
        public bool Angry;                    // neutral creatures that were hit
        public float Phase;                   // bosses: what they're doing
        public float Baby;                    // seconds until grown (0: grown up)
        public float Love, BreedCool;         // fed: looking for a mate; resting after
        public bool Kept;                     // fed or born here: stays when no one is about, saved with the world
        public byte Job;                      // villagers: farmer, smith, librarian, shepherd, butcher
        public float HomeX, HomeZ;            // villagers: the middle of their village
        public bool Tame, Sit;                // a friend to someone; told to stay
        public string OwnerKey = "";          // whose (their account, or "me" in a single-player world)
        public float SwimVY;                  // swimmers: how fast they mean to rise or sink
        public float WayX, WayZ, WayAt; public bool HasWay;      // the next step of a path round what's in the way

        public const byte MBaby = 1, MLove = 2, MRest = 32, MTame = 64, MSit = 128;       // MRest: bred a little while ago, not hungry yet
        public byte More => (byte)((Baby > 0 ? MBaby : 0) | (Love > 0 ? MLove : 0) | (Job << 2) | (BreedCool > 0 ? MRest : 0) | (Tame ? MTame : 0) | (Sit ? MSit : 0));
        public static int JobOf(byte more) => (more >> 2) & 7;
        // A sheep's colour (which of Blocks.Dyes) rides in its job and its sit flag, neither of which a sheep has a use for.
        public int Fleece { get => Job | (Sit ? 8 : 0); set { Job = (byte)(value & 7); Sit = (value & 8) != 0; } }
        public static int FleeceOf(byte more) => JobOf(more) | ((more & MSit) != 0 ? 8 : 0);
        public static string WoolOf(int fleece) => Blocks.Dyes[Math.Clamp(fleece, 0, Blocks.Dyes.Length - 1)].key + "_wool";

        public const byte FHurt = 1, FDead = 2, FAttack = 4, FBurning = 8, FSheared = 16, FWater = 32, FAngry = 64, FStuck = 128;

        public byte Flags =>
            (byte)((Hurt > 0 ? FHurt : 0) | (Dead > 0 ? FDead : 0) | (Windup > 0 || Cool > Def.AttackEvery - 0.25f ? FAttack : 0) |
                   (Burn > 0 ? FBurning : 0) | (Sheared ? FSheared : 0) | (InWater ? FWater : 0) | (Angry ? FAngry : 0) | (Stuck ? FStuck : 0));
    }

    // A player as the creatures see them.
    public struct Seen
    {
        public int Id; public float X, Y, Z; public bool Sneaking, Creative, Alive, Sleeping;
        public string Held;
        public string Key;                    // who they are between visits (pets know their owner by it)
    }

    // Every creature in a world: where they wander, who they chase and hit, when they appear and leave. One copy
    // runs in a single-player game and one on a server (which tells the players where everyone is).
    public sealed class MobSim
    {
        public static readonly MobDef[] Defs = new MobDef[(int)MobKind.Count];

        static MobSim()
        {
            void D(MobDef d) => Defs[(int)d.Kind] = d;
            D(new MobDef { Kind = MobKind.Cow, Name = "Cow", Health = 10, Speed = 1.3f, HalfW = 0.45f, Height = 1.4f, Xp = 2,
                Drops = new[] { ("raw_beef", 1, 3, 1f), ("hide", 0, 2, 1f) } });
            D(new MobDef { Kind = MobKind.Pig, Name = "Pig", Health = 10, Speed = 1.3f, HalfW = 0.45f, Height = 0.9f, Xp = 2,
                Drops = new[] { ("raw_pork", 1, 3, 1f) } });
            D(new MobDef { Kind = MobKind.Sheep, Name = "Sheep", Health = 8, Speed = 1.25f, HalfW = 0.45f, Height = 1.3f, Xp = 2,
                Drops = new[] { ("raw_mutton", 1, 2, 1f) } });
            D(new MobDef { Kind = MobKind.Chicken, Name = "Chicken", Health = 4, Speed = 1.2f, HalfW = 0.25f, Height = 0.7f, Floats = true, Xp = 1,
                Drops = new[] { ("raw_chicken", 1, 1, 1f), ("feather", 0, 2, 1f) } });
            D(new MobDef { Kind = MobKind.Ghoul, Name = "Ghoul", Health = 20, Speed = 2.3f, HalfW = 0.3f, Height = 1.85f, Hostile = true, Burns = true,
                Damage = 3, Reach = 1.7f, AttackEvery = 1.0f, Sight = 24, Xp = 5,
                Drops = new[] { ("coal", 0, 2, 1f), ("bone", 0, 1, 1f), ("iron_ingot", 1, 1, 0.04f), ("copper_ingot", 1, 1, 0.06f), ("carrot", 1, 1, 0.04f), ("potato", 1, 1, 0.04f) } });
            D(new MobDef { Kind = MobKind.BoneArcher, Name = "Bone Archer", Health = 20, Speed = 2.2f, HalfW = 0.3f, Height = 1.9f, Hostile = true, Burns = true,
                Ranged = true, Damage = 3, Reach = 14, AttackEvery = 2.4f, Sight = 22, Xp = 5,
                Drops = new[] { ("bone", 0, 2, 1f), ("arrow", 0, 3, 1f), ("bow", 1, 1, 0.06f) } });
            D(new MobDef { Kind = MobKind.Crawler, Name = "Crawler", Health = 16, Speed = 3.6f, HalfW = 0.6f, Height = 0.8f, Hostile = true, Climbs = true,
                Damage = 2, Reach = 1.9f, AttackEvery = 0.9f, Sight = 18, Xp = 5,
                Drops = new[] { ("thread", 0, 2, 1f) } });
            D(new MobDef { Kind = MobKind.Arrow, Name = "Arrow", Health = 1, HalfW = 0.1f, Height = 0.2f });
            D(new MobDef { Kind = MobKind.EmberImp, Name = "Ember Imp", Health = 16, Speed = 2.6f, HalfW = 0.35f, Height = 1.5f, Hostile = true, Ranged = true,
                Fireproof = true, Damage = 4, Reach = 16, AttackEvery = 2.2f, Sight = 26, Xp = 8,
                Drops = new[] { ("ember", 1, 2, 1f), ("sunstone_shard", 0, 1, 0.5f) } });
            D(new MobDef { Kind = MobKind.Colossus, Name = "Cinder Colossus", Health = 300, Speed = 2.4f, HalfW = 1.3f, Height = 4.6f, Hostile = true, Boss = true,
                Fireproof = true, Damage = 9, Reach = 3.4f, AttackEvery = 1.6f, Sight = 48, Xp = 200,
                Drops = new[] { ("colossus_heart", 1, 1, 1f), ("gold_ingot", 6, 12, 1f), ("ember", 6, 10, 1f) } });
            D(new MobDef { Kind = MobKind.Villager, Name = "Villager", Health = 20, Speed = 1.1f, HalfW = 0.3f, Height = 1.9f });
            D(new MobDef { Kind = MobKind.Boat, Name = "Boat", Health = 4, HalfW = 0.7f, Height = 0.6f, Vehicle = true, Drops = new[] { ("boat", 1, 1, 1f) } });
            D(new MobDef { Kind = MobKind.Deer, Name = "Deer", Health = 10, Speed = 1.9f, HalfW = 0.4f, Height = 1.5f, Xp = 2,
                Drops = new[] { ("raw_beef", 1, 2, 1f), ("hide", 0, 2, 1f) } });
            D(new MobDef { Kind = MobKind.Fox, Name = "Fox", Health = 8, Speed = 2.0f, HalfW = 0.3f, Height = 0.75f, Xp = 2,
                Drops = new[] { ("hide", 0, 1, 1f) } });
            D(new MobDef { Kind = MobKind.Rabbit, Name = "Rabbit", Health = 3, Speed = 2.1f, HalfW = 0.2f, Height = 0.5f, Xp = 1,
                Drops = new[] { ("hide", 0, 1, 0.6f) } });
            D(new MobDef { Kind = MobKind.Penguin, Name = "Penguin", Health = 6, Speed = 0.9f, HalfW = 0.25f, Height = 0.9f, Floats = true, Xp = 1,
                Drops = new[] { ("feather", 0, 2, 1f) } });
            D(new MobDef { Kind = MobKind.Goat, Name = "Goat", Health = 10, Speed = 1.5f, HalfW = 0.4f, Height = 1.2f, Xp = 2,
                Drops = new[] { ("raw_mutton", 1, 2, 1f), ("hide", 0, 1, 1f) } });
            D(new MobDef { Kind = MobKind.Camel, Name = "Camel", Health = 16, Speed = 1.2f, HalfW = 0.5f, Height = 2.1f, Xp = 3,
                Drops = new[] { ("hide", 1, 3, 1f) } });
            // The Bloat: a swollen bog creature that creeps up without a sound, swells and bursts.
            D(new MobDef { Kind = MobKind.Bloat, Name = "Bloat", Health = 20, Speed = 2.1f, HalfW = 0.4f, Height = 1.5f, Hostile = true,
                Damage = 0, Reach = 2.6f, AttackEvery = 1, Sight = 16, Xp = 5,
                Drops = new[] { ("blast_powder", 0, 2, 1f) } });
            // The Hound: runs wild in the woods; a few bones make it a friend that follows, sits and fights.
            D(new MobDef { Kind = MobKind.Hound, Name = "Hound", Health = 20, Speed = 2.5f, HalfW = 0.3f, Height = 0.85f, Damage = 3, Reach = 1.6f, Sight = 16, Xp = 2 });
            // The Glob: a wobbling lump of jelly that gets about in bounds and breaks into smaller ones.
            D(new MobDef { Kind = MobKind.Glob, Name = "Glob", Health = 16, Speed = 2.2f, HalfW = 0.5f, Height = 1.0f, Hostile = true, Hops = true,
                Damage = 3, Reach = 1.5f, AttackEvery = 1.2f, Sight = 16, Xp = 3, Drops = new[] { ("jelly", 0, 2, 1f) } });
            // The Shade: tall, thin and dark; it steps through the air to wherever it likes, and away when struck.
            D(new MobDef { Kind = MobKind.Shade, Name = "Shade", Health = 30, Speed = 3.2f, HalfW = 0.3f, Height = 2.6f, Hostile = true, Blinks = true,
                Damage = 5, Reach = 2.0f, AttackEvery = 1.1f, Sight = 28, Xp = 8, Drops = new[] { ("shade_shard", 0, 1, 0.7f) } });
            // The Snapper: a toothy fish of deep water that goes for swimmers.
            D(new MobDef { Kind = MobKind.Snapper, Name = "Snapper", Health = 12, Speed = 3.6f, HalfW = 0.4f, Height = 0.5f, Hostile = true, Swims = true,
                Damage = 3, Reach = 1.5f, AttackEvery = 1.0f, Sight = 14, Xp = 4, Drops = new[] { ("raw_fish", 1, 2, 1f) } });
            // The Hollow King: lord of the Quiet. He steps through the air, calls Shades to him and, wounded, throws bolts.
            D(new MobDef { Kind = MobKind.King, Name = "Hollow King", Health = 400, Speed = 3.0f, HalfW = 0.8f, Height = 4.2f, Hostile = true, Boss = true, Blinks = true,
                Fireproof = true, Damage = 8, Reach = 3.2f, AttackEvery = 1.4f, Sight = 70, Xp = 500,
                Drops = new[] { ("hollow_crown", 1, 1, 1f), ("starstone", 4, 8, 1f), ("shade_shard", 6, 10, 1f) } });
            D(new MobDef { Kind = MobKind.Cart, Name = "Minecart", Health = 4, HalfW = 0.5f, Height = 0.8f, Vehicle = true, Drops = new[] { ("minecart", 1, 1, 1f) } });
        }

        public static MobDef Def(MobKind k) => Defs[(int)k];

        public readonly List<Mob> All = new List<Mob>();
        readonly Dictionary<int, Mob> _byId = new Dictionary<int, Mob>();
        readonly Func<int, int, int, ushort> _get;
        readonly Random _r;
        int _nextId = 1;
        float _spawnTimer;

        public Func<int, int, bool> Loaded = (x, z) => true;      // is the ground there (columns, world coords)
        public Func<int, int, Biome> BiomeAt;
        public int Difficulty = 2;                                // 0 peaceful, 1 easy, 2 normal
        public float Time01 = 0.3f;                               // time of day (0.25 sunrise, 0.75 sunset)
        public bool Spawning = true;
        public string Dimension = "";                             // "" the overworld, "ember" the Ember Deep
        public int PassiveCap = 9, HostileCap = 7;

        // A creature struck a player: (player, damage, from x, from z, who).
        public event Action<int, float, float, float, Mob> HitPlayer;
        // A creature died: (it, the player who killed it, or 0).
        public event Action<Mob, int> Died;
        // A hen laid an egg where it stands.
        public event Action<Mob> Laid;
        // A Bloat burst: the host makes the blast.
        public event Action<Mob> Burst;
        // A creature made a sound worth hearing: (it, "hurt"/"die"/"say"/"shoot").
        public event Action<Mob, string> Sound;

        public MobSim(Func<int, int, int, ushort> get, int seed)
        {
            _get = get;
            _r = new Random(seed);
        }

        public bool TryGet(int id, out Mob m) => _byId.TryGetValue(id, out m);

        public bool IsDay => Time01 > 0.27f && Time01 < 0.73f;
        public int Weather;
        public Func<int, int, Villages.Plan> VillageNear;         // the village (if any) near a spot
        float _villageTimer = 3;                                       // WeatherSim state: no sunburn in rain, storms are dark enough for monsters

        float Rand() => (float)_r.NextDouble();
        readonly Dictionary<int, string> _keys = new Dictionary<int, string>();      // who each player is, as of the last tick

        public Mob Spawn(MobKind k, float x, float y, float z)
        {
            var d = Def(k);
            var m = new Mob { Id = _nextId++, Kind = k, Def = d, X = x, Y = y, Z = z, Health = d.Health, Yaw = Rand() * 360, Think = Rand() * 2 };
            All.Add(m); _byId[m.Id] = m;
            return m;
        }

        void Remove(Mob m) { All.Remove(m); _byId.Remove(m.Id); }

        public void Clear() { All.Clear(); _byId.Clear(); }

        // ------------------------------------------------------------------ what players do to them

        // Getting into a boat or cart: it leaves the world (the player carries it) until they get out again.
        public MobKind? Mount(int id, float px, float py, float pz, out float x, out float y, out float z, out float yaw)
        {
            x = y = z = yaw = 0;
            if (!_byId.TryGetValue(id, out var m) || m.Dead > 0 || !(m.Def.Vehicle || m.Tame && m.Kind == MobKind.Camel)) return null;
            float dx = m.X - px, dy = m.Y - py, dz = m.Z - pz;
            if (dx * dx + dy * dy + dz * dz > 7 * 7) return null;
            x = m.X; y = m.Y; z = m.Z; yaw = m.Yaw;
            Remove(m);
            return m.Kind;
        }

        // A boat or cart put down (from the hand, or got out of).
        public Mob Park(MobKind kind, float x, float y, float z, float yaw, string who = "")
        {
            if (!Def(kind).Vehicle && kind != MobKind.Camel) return null;
            var m = Spawn(kind, x, y, z);
            m.Yaw = yaw; m.Kept = true;
            if (kind == MobKind.Camel) { m.Tame = true; m.OwnerKey = who ?? ""; }      // the camel someone rode here
            return m;
        }

        // A melee hit by a player standing at (px, py, pz). False if it's out of reach or already gone.
        public bool Strike(int id, int player, float damage, float px, float py, float pz)
        {
            if (!_byId.TryGetValue(id, out var m) || m.Dead > 0 || m.Kind == MobKind.Arrow) return false;
            float dx = m.X - px, dz = m.Z - pz, dy = m.Y + m.Def.Height / 2 - (py + 1.4f);
            float reach = 4.6f + m.Def.HalfW;
            if (dx * dx + dy * dy + dz * dz > reach * reach) return false;
            if (m.Tame && _keys.TryGetValue(player, out var whose) && whose == m.OwnerKey) return false;     // nobody hits their own friend
            Hurt(m, damage, px, pz, player, 1);
            return true;
        }

        void Hurt(Mob m, float damage, float fromX, float fromZ, int player, float knock)
        {
            if (m.Dead > 0 || m.Hurt > 0.35f) return;           // a moment of grace between hits
            m.Health -= damage;
            m.Hurt = 0.5f;
            m.FromX = fromX; m.FromZ = fromZ;
            float dx = m.X - fromX, dz = m.Z - fromZ, len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len > 0.01f && !m.Def.Boss) { m.VX += dx / len * 6 * knock; m.VZ += dz / len * 6 * knock; if (m.OnGround) m.VY = 4.5f * knock; }
            if (!m.Def.Hostile && m.Kind != MobKind.Hound) m.Flee = 5;
            else if (player > 0 && !m.Tame) { m.Target = player; m.Angry = true; }      // a wild hound bites back
            if (m.Def.Vehicle) { m.VX = m.VZ = m.VY = 0; m.Flee = 0; }
            if (m.Health <= 0)
            {
                m.Health = 0; m.Dead = m.Def.Vehicle ? 0.05f : 1f;
                Sound?.Invoke(m, "die");
                if (m.Kind == MobKind.Glob && m.Job < 2)
                    for (int i = 0; i < 2; i++)
                    {
                        var piece = Spawn(MobKind.Glob, m.X + (i == 0 ? 0.3f : -0.3f), m.Y + 0.2f, m.Z + (i == 0 ? -0.3f : 0.3f));
                        piece.Job = (byte)(m.Job + 1); piece.Health = m.Job == 0 ? 6 : 2; piece.VY = 4; piece.Target = m.Target;
                    }
                if (m.Def.Boss) _bossBack[((int)m.HomeX, (int)m.HomeZ)] = _clock + 1200;
                Died?.Invoke(m, player);
            }
            else { Sound?.Invoke(m, "hurt"); if (m.Def.Blinks && (!m.Def.Boss || Rand() < 0.3f)) Blink(m, m.X, m.Z, 6, 14); }
        }

        // A blast at (x, y, z): everything near is thrown back and hurt, less the further away it stands.
        public void Blast(float x, float y, float z, float power, List<Seen> players)
        {
            float reach = power * 2.2f;
            var cause = new Mob { Kind = MobKind.Bloat, Def = Def(MobKind.Bloat), X = x, Y = y, Z = z };
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var m = All[i];
                if (m.Dead > 0 || m.Kind == MobKind.Arrow) continue;
                float dx = m.X - x, dy = m.Y + m.Def.Height / 2 - y, dz = m.Z - z, dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist > reach) continue;
                m.Hurt = 0;
                Hurt(m, (1 - dist / reach) * power * 6, x, z, 0, 1.6f);
            }
            if (players == null) return;
            foreach (var p in players)
            {
                if (!p.Alive || p.Creative) continue;
                float dx = p.X - x, dy = p.Y + 0.9f - y, dz = p.Z - z, dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist > reach) continue;
                float dmg = (1 - dist / reach) * power * 5 * (Difficulty == 1 ? 0.6f : 1);
                if (dmg >= 0.5f) HitPlayer?.Invoke(p.Id, dmg, x, z, cause);
            }
        }

        // An arrow loosed by a player (or by a creature, owner < 0).
        public Mob Shoot(int owner, float x, float y, float z, float vx, float vy, float vz, float damage)
        {
            var a = Spawn(MobKind.Arrow, x, y, z);
            a.VX = vx; a.VY = vy; a.VZ = vz; a.Owner = owner; a.ArrowDamage = damage;
            AimArrow(a);
            return a;
        }

        static void AimArrow(Mob a)
        {
            float h = (float)Math.Sqrt(a.VX * a.VX + a.VZ * a.VZ);
            a.Yaw = (float)(Math.Atan2(a.VX, a.VZ) * 180 / Math.PI);
            a.Pitch = (float)(-Math.Atan2(a.VY, h) * 180 / Math.PI);
        }

        // What each animal eats (and follows): the food that breeds them.
        public static string FoodOf(MobKind k) => k switch
        {
            MobKind.Cow => "wheat", MobKind.Sheep => "wheat", MobKind.Pig => "berries", MobKind.Chicken => "seeds",
            MobKind.Deer => "wheat", MobKind.Goat => "wheat", MobKind.Camel => "wheat", MobKind.Rabbit => "seeds", MobKind.Fox => "berries", _ => null
        };

        public const float GrowUp = 1200, LoveFor = 30, BreedRest = 300;

        // Would feeding this creature that item do anything? (The player only spends the food if so.)
        public bool WouldEat(int id, string item)
        {
            if (!_byId.TryGetValue(id, out var m) || m.Dead > 0 || item == null || FoodOf(m.Kind) != item) return false;
            return m.Baby > 0 || m.Love <= 0 && m.BreedCool <= 0;
        }

        // A player uses an item on a creature (shears on a sheep, food). Returns what they get, or null.
        public (string item, int count)? Use(int id, string item, float px, float pz, string who = "")
        {
            if (!_byId.TryGetValue(id, out var m) || m.Dead > 0) return null;
            float dx = m.X - px, dz = m.Z - pz;
            if (dx * dx + dz * dz > 36) return null;
            if (item != null && item == FoodOf(m.Kind))
            {
                m.Kept = true;
                if (m.Kind == MobKind.Camel && !m.Tame) { m.Tame = true; m.OwnerKey = who ?? ""; }      // a fed camel lets itself be ridden
                if (m.Baby > 0) m.Baby = Math.Max(0, m.Baby - GrowUp * 0.1f);
                else if (m.Love <= 0 && m.BreedCool <= 0) m.Love = LoveFor;
                Sound?.Invoke(m, "eat");
                return null;
            }
            if (m.Kind == MobKind.Hound)
            {
                if (!m.Tame)
                {
                    if (item != "bone") return null;
                    Sound?.Invoke(m, "eat");
                    if (Rand() < 0.34f) { m.Tame = true; m.Kept = true; m.OwnerKey = who ?? ""; m.Angry = false; m.Target = -1; m.Love = 1.2f; m.Health = m.Def.Health; }
                    return null;
                }
                if (item != null && (item.StartsWith("raw_") || item == "steak" || item.StartsWith("roast_")) && m.Health < m.Def.Health)
                { m.Health = Math.Min(m.Def.Health, m.Health + 6); m.Love = 0.8f; Sound?.Invoke(m, "eat"); return null; }
                if ((who ?? "") == m.OwnerKey) m.Sit = !m.Sit;
                return null;
            }
            if (m.Kind == MobKind.Sheep && item != null && item.StartsWith("dye_"))
            {
                // Dyed: its wool grows that colour from now on.
                int c = Blocks.DyeIndex(item.Substring(4));
                if (c >= 0) { m.Fleece = c; m.Kept = true; Sound?.Invoke(m, "say"); }
                return null;
            }
            if (item == "shears" && m.Kind == MobKind.Sheep && !m.Sheared)
            {
                m.Sheared = true; m.Regrow = 240 + Rand() * 240;
                Sound?.Invoke(m, "say");
                return (Mob.WoolOf(m.Fleece), 1 + _r.Next(3));
            }
            return null;
        }

        public void SaveFile(string path)
        {
            var tmp = path + ".tmp";
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(tmp))) Write(w);
            SafeFile.Swap(tmp, path);
        }

        public void LoadFile(string path)
        {
            if (!System.IO.File.Exists(path)) return;
            using var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path));
            Read(r);
        }

        public List<(string item, int count)> Loot(Mob m)
        {
            var l = new List<(string, int)>();
            if (m.Baby > 0) return l;
            if (m.Kind == MobKind.Glob && m.Job < 2) return l;        // only the smallest pieces leave jelly
            foreach (var (item, min, max, chance) in m.Def.Drops)
            {
                if (Rand() > chance) continue;
                int n = min + _r.Next(max - min + 1);
                if (n > 0) l.Add((item, n));
            }
            if (m.Kind == MobKind.Sheep && !m.Sheared) l.Add((Mob.WoolOf(m.Fleece), 1));
            if (m.Def.Xp > 0) l.Add(("xp", m.Def.Xp + _r.Next(m.Def.Xp + 1)));
            return l;
        }

        // ------------------------------------------------------------------ the world turning

        public void Tick(float dt, List<Seen> players)
        {
            if (dt <= 0) return;
            if (dt > 0.1f) dt = 0.1f;
            _clock += dt;
            _keys.Clear(); foreach (var p in players) _keys[p.Id] = p.Key ?? "";
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var m = All[i];
                if (!Loaded((int)Math.Floor(m.X), (int)Math.Floor(m.Z)))
                {
                    // Frozen until its ground is back; wild ones nobody is near any more are let go instead.
                    if (m.Kind == MobKind.Arrow || !m.Def.Vehicle && !m.Def.Boss && m.Kind != MobKind.Villager && !m.Kept && Alone(m, players)) Remove(m);
                    continue;
                }
                m.Age += dt;
                if (m.Kind == MobKind.Arrow) { Arrow(m, dt, players); continue; }
                if (m.Def.Vehicle)
                {
                    // A boat or cart that nobody is in stays where it was left.
                    m.Hurt = Math.Max(0, m.Hurt - dt);
                    if (m.Dead > 0 && (m.Dead -= dt) <= 0) Remove(m);
                    continue;
                }
                if (m.Dead > 0)
                {
                    m.Dead -= dt;
                    m.VX *= 0.9f; m.VZ *= 0.9f;
                    Physics(m, dt);
                    if (m.Dead <= 0) Remove(m);
                    continue;
                }
                m.Hurt = Math.Max(0, m.Hurt - dt);
                m.Cool = Math.Max(0, m.Cool - dt);
                if (m.Sheared && (m.Regrow -= dt) <= 0) m.Sheared = false;
                // A grown hen lays an egg every few minutes.
                if (m.Kind == MobKind.Chicken && m.Baby <= 0)
                {
                    if (m.Regrow <= 0) m.Regrow = 120 + Rand() * 400;
                    else if ((m.Regrow -= dt) <= 0) { Laid?.Invoke(m); m.Regrow = 300 + Rand() * 300; }
                }
                if (m.Baby > 0) m.Baby = Math.Max(0, m.Baby - dt);
                if (m.BreedCool > 0) m.BreedCool = Math.Max(0, m.BreedCool - dt);
                if (m.Love > 0) { m.Love = Math.Max(0, m.Love - dt); Mate(m); }
                Think(m, dt, players);
                Physics(m, dt);
                Hazards(m, dt);
                if (Despawn(m, players)) Remove(m);
            }
            if ((_cageTimer -= dt) <= 0)
            {
                _cageTimer = 4 + Rand() * 6;
                if (Difficulty > 0 && Spawning) foreach (var p in players) if (p.Alive && !Ember.In(p.X)) { Cages(p); Waters(p); }
            }
            if ((_villageTimer -= dt) <= 0)
            {
                _villageTimer = 5;
                foreach (var p in players) if (Ember.In(p.X)) Keeper(p); else if (Quiet.In(p.X)) King(p); else if (VillageNear != null) Villagers(p);
            }
            if (Spawning && players.Count > 0 && (_spawnTimer -= dt) <= 0)
            {
                _spawnTimer = 1.5f;
                foreach (var p in players) if (p.Alive) TrySpawn(p, players);
            }
        }

        bool Lure(Mob m, List<Seen> players, out float x, out float z)
        {
            x = z = 0;
            if (m.Love > 0)
            {
                float best = 8 * 8;
                foreach (var o in All)
                {
                    if (o == m || o.Kind != m.Kind || o.Love <= 0 || o.Baby > 0 || o.Dead > 0) continue;
                    float dx = o.X - m.X, dz = o.Z - m.Z, d2 = dx * dx + dz * dz;
                    if (d2 < best) { best = d2; x = o.X; z = o.Z; }
                }
                if (best < 8 * 8) return true;
            }
            string food = FoodOf(m.Kind);
            if (food != null)
            {
                float best = 10 * 10; bool any = false;
                foreach (var p in players)
                {
                    if (!p.Alive || p.Held != food) continue;
                    float d2 = Dist2(m, p);
                    if (d2 < best) { best = d2; x = p.X; z = p.Z; any = true; }
                }
                if (any) return true;
            }
            if (m.Baby > 0)
            {
                // Young ones keep near a grown one.
                float best = 16 * 16, near = 3 * 3; bool any = false;
                foreach (var o in All)
                {
                    if (o == m || o.Kind != m.Kind || o.Baby > 0 || o.Dead > 0) continue;
                    float dx = o.X - m.X, dz = o.Z - m.Z, d2 = dx * dx + dz * dz;
                    if (d2 < best) { best = d2; x = o.X; z = o.Z; any = true; }
                }
                if (any && best > near) return true;
            }
            return false;
        }

        static float Dist2(Mob m, in Seen p) { float dx = m.X - p.X, dz = m.Z - p.Z, dy = m.Y - p.Y; return dx * dx + dy * dy + dz * dz; }

        // Two of a kind in love near each other make a baby.
        void Mate(Mob m)
        {
            foreach (var o in All)
            {
                if (o == m || o.Kind != m.Kind || o.Love <= 0 || o.Dead > 0 || o.Baby > 0) continue;
                float dx = o.X - m.X, dz = o.Z - m.Z, d2 = dx * dx + dz * dz;
                if (d2 > 1.6f * 1.6f) continue;
                m.Love = o.Love = 0; m.BreedCool = o.BreedCool = BreedRest;
                var b = Spawn(m.Kind, (m.X + o.X) / 2, Math.Max(m.Y, o.Y), (m.Z + o.Z) / 2);
                b.Baby = GrowUp; b.Kept = true; b.Yaw = m.Yaw;
                Sound?.Invoke(b, "say");
                Born?.Invoke(b);
                return;
            }
        }
        public event Action<Mob> Born;

        // The animals worth keeping between visits: anything fed or born to a player.
        public void Write(System.IO.BinaryWriter w)
        {
            w.Write(3);
            int n = 0; foreach (var m in All) if (m.Kept && m.Dead <= 0) n++;
            w.Write(n);
            foreach (var m in All)
            {
                if (!m.Kept || m.Dead > 0) continue;
                w.Write((byte)m.Kind); w.Write(m.X); w.Write(m.Y); w.Write(m.Z); w.Write(m.Yaw); w.Write(m.Health);
                w.Write(m.Baby); w.Write(m.BreedCool); w.Write(m.Sheared); w.Write(m.Regrow);
                w.Write(m.Job); w.Write(m.HomeX); w.Write(m.HomeZ);
                w.Write(m.Tame); w.Write(m.Sit); w.Write(m.OwnerKey ?? "");
            }
        }

        public void Read(System.IO.BinaryReader r)
        {
            int ver = r.ReadInt32();
            if (ver < 1 || ver > 3) return;
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var k = (MobKind)r.ReadByte();
                float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle();
                float yaw = r.ReadSingle(), health = r.ReadSingle(), baby = r.ReadSingle(), breedCool = r.ReadSingle();
                bool sheared = r.ReadBoolean(); float regrow = r.ReadSingle();
                byte job = 0; float homeX = 0, homeZ = 0;
                if (ver >= 2) { job = r.ReadByte(); homeX = r.ReadSingle(); homeZ = r.ReadSingle(); }
                bool tame = false, sit = false; string owner = "";
                if (ver >= 3) { tame = r.ReadBoolean(); sit = r.ReadBoolean(); owner = r.ReadString(); }
                if ((int)k < 0 || (int)k >= Defs.Length) continue;
                var m = Spawn(k, x, y + 0.05f, z);
                m.Yaw = yaw; m.Health = health; m.Baby = baby; m.BreedCool = breedCool;
                m.Sheared = sheared; m.Regrow = regrow; m.Kept = true;
                if (ver >= 2) { m.Job = job; m.HomeX = homeX; m.HomeZ = homeZ; }
                m.Tame = tame; m.Sit = sit; m.OwnerKey = owner;
            }
        }

        // Deep water near someone on or in it has a snapper or two in it.
        void Waters(in Seen p)
        {
            int px = (int)Math.Floor(p.X), pz = (int)Math.Floor(p.Z), py = (int)Math.Floor(p.Y);
            if (!Blocks.IsWater(_get(px, py - 1, pz)) && !Blocks.IsWater(_get(px, py, pz))) return;
            int near = 0; foreach (var m in All) if (m.Kind == MobKind.Snapper && Math.Abs(m.X - p.X) < 24 && Math.Abs(m.Z - p.Z) < 24) near++;
            if (near >= 2 || Rand() > 0.3f) return;
            float a = Rand() * 6.283f, r = 9 + Rand() * 8;
            int x = (int)Math.Floor(p.X + Math.Cos(a) * r), z = (int)Math.Floor(p.Z + Math.Sin(a) * r);
            if (!Loaded(x, z)) return;
            // Three blocks of water at least, and it starts well under.
            for (int y = Math.Min(py + 1, Chunk.Height - 2); y > py - 12 && y > 3; y--)
            {
                if (!Blocks.IsWater(_get(x, y, z))) continue;
                if (Blocks.IsWater(_get(x, y - 1, z)) && Blocks.IsWater(_get(x, y - 2, z))) Spawn(MobKind.Snapper, x + 0.5f, y - 1.6f, z + 0.5f);
                return;
            }
        }

        // Steps a creature through the air to somewhere it can stand, between rmin and rmax of (cx, cz).
        bool Blink(Mob m, float cx, float cz, float rmin, float rmax)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                float a = Rand() * 6.283f, r = rmin + Rand() * (rmax - rmin);
                int x = (int)Math.Floor(cx + Math.Cos(a) * r), z = (int)Math.Floor(cz + Math.Sin(a) * r);
                if (!Loaded(x, z)) continue;
                int y = Ground(x, z, (int)m.Y + 6, 14, out ushort floor);
                if (y == int.MinValue || Blocks.IsFluid(floor) || Blocks.IsWater(_get(x, y, z)) || Body.Blocked(_get, x + 0.5f, y, z + 0.5f, m.Def.HalfW, m.Def.Height)) continue;
                m.X = x + 0.5f; m.Y = y; m.Z = z + 0.5f; m.VX = m.VY = m.VZ = 0; m.HasWay = false; m.WayAt = 0;
                Sound?.Invoke(m, "say");
                return true;
            }
            return false;
        }

        float _cageTimer = 3;
        readonly List<(int x, int y, int z)> _cages = new List<(int, int, int)>();

        // A monster cage with someone within sixteen blocks lets another creature out beside it (no more than four at once).
        void Cages(in Seen p)
        {
            _cages.Clear();
            Ruins.CagesNear(Seed, p.X, p.Z, 16, _cages);
            foreach (var (x, y, z) in _cages)
            {
                if (Math.Abs(p.Y - y) > 10 || !Loaded(x, z) || _get(x, y, z) != Blocks.Cage) continue;
                int near = 0;
                foreach (var m in All) if (m.Def.Hostile && m.Dead <= 0 && Math.Abs(m.X - x) < 20 && Math.Abs(m.Z - z) < 20 && Math.Abs(m.Y - y) < 8) near++;
                if (near >= 4) continue;
                var kind = Ruins.CageKind(Seed, x, y, z);
                for (int tries = 0; tries < 6; tries++)
                {
                    float sx = x + 0.5f + _r.Next(-3, 4), sz = z + 0.5f + _r.Next(-3, 4);
                    if (Body.Blocked(_get, sx, y, sz, Def(kind).HalfW, Def(kind).Height) || !Blocks.Get(_get((int)Math.Floor(sx), y - 1, (int)Math.Floor(sz))).Solid) continue;
                    Spawn(kind, sx, y, sz);
                    break;
                }
            }
        }

        bool Alone(Mob m, List<Seen> players)
        {
            float far = m.Def.Hostile ? 72 : 110;
            foreach (var p in players) if (Dist2(m, p) <= far * far) return false;
            return true;
        }

        bool Despawn(Mob m, List<Seen> players)
        {
            if (m.Def.Boss || m.Kind == MobKind.Villager || m.Kept) return false;
            float best = float.MaxValue;
            foreach (var p in players) best = Math.Min(best, Dist2(m, p));
            if (players.Count == 0) return false;
            float far = m.Def.Hostile ? 72 : 110;
            if (best > far * far) return true;
            // Night creatures far from anyone drift away by day.
            if (m.Def.Hostile && !Ember.In(m.X) && !Quiet.In(m.X) && IsDay && best > 32 * 32 && m.Age > 30 && Rand() < 0.002f) return true;
            return Difficulty == 0 && m.Def.Hostile;
        }

        void Think(Mob m, float dt, List<Seen> players)
        {
            var d = m.Def;
            float speed = d.Speed;
            float wx = 0, wz = 0;                       // where it wants to go (a direction, not normalised)
            bool jumpy = false;

            // A Shade caught out in daylight slips away for good.
            if (d.Blinks && !d.Boss && IsDay && !Ember.In(m.X) && !Quiet.In(m.X) && Rand() < dt * 0.2f && SkyOpen((int)Math.Floor(m.X), (int)Math.Floor(m.Y + 1), (int)Math.Floor(m.Z))) { m.Health = 0; m.Dead = 0.01f; return; }
            bool pet = m.Tame && m.Kind == MobKind.Hound;
            if (pet) Pet(m, dt, players, ref wx, ref wz, ref speed);
            Seen? target = null;
            if (!pet && (d.Hostile && Difficulty > 0 || m.Angry))
            {
                // Pick (or keep) someone to go after.
                float best = float.MaxValue;
                foreach (var p in players)
                {
                    if (!p.Alive || p.Creative) continue;
                    float sight = d.Sight * (p.Sneaking ? 0.5f : 1) * (m.Angry || m.Target == p.Id ? 1.6f : 1);
                    if (m.Kind == MobKind.Crawler && IsDay && !Ember.In(m.X) && !m.Angry) sight = 0;   // drowsy by day
                    float dd = Dist2(m, p);
                    if (dd < sight * sight && dd < best && (m.Target == p.Id || See(m, p))) { best = dd; target = p; }
                }
                m.Target = target?.Id ?? -1;
                if (target == null) m.Angry = false;
            }

            if (pet) { }
            else if (target is Seen t)
            {
                float dx = t.X - m.X, dz = t.Z - m.Z, dist = (float)Math.Sqrt(dx * dx + dz * dz);
                float dy = t.Y - m.Y;
                m.Yaw = (float)(Math.Atan2(dx, dz) * 180 / Math.PI);
                if (d.Ranged)
                {
                    // Keep at a distance and shoot.
                    if (dist > 11) { wx = dx; wz = dz; }
                    else if (dist < 6) { wx = -dx; wz = -dz; speed *= 0.8f; }
                    else { wx = -dz * 0.3f; wz = dx * 0.3f; speed *= 0.5f; }   // circle a little
                    if (m.Cool <= 0 && dist < d.Reach && See(m, t))
                    {
                        m.Cool = d.AttackEvery * (0.85f + Rand() * 0.3f);
                        float ex = m.X, ey = m.Y + d.Height * 0.8f, ez = m.Z;
                        float tx = t.X - ex, ty = t.Y + 1.3f - ey, tz = t.Z - ez;
                        float flat = (float)Math.Sqrt(tx * tx + tz * tz);
                        float v = m.Kind == MobKind.EmberImp ? 18 : 24;
                        float time = flat / v;
                        float vy = ty / Math.Max(0.1f, time) + 10 * time;      // lift for the fall
                        float spread = (3 - Difficulty) * 0.5f;
                        Shoot(-m.Id, ex, ey, ez, tx / flat * v + (Rand() - 0.5f) * spread, vy, tz / flat * v + (Rand() - 0.5f) * spread,
                            d.Damage * (Difficulty == 1 ? 0.6f : 1));
                        Sound?.Invoke(m, "shoot");
                    }
                }
                else if (m.Kind == MobKind.Bloat)
                {
                    // Close enough: it stops and swells; back off in time and it calms down again.
                    wx = dx; wz = dz;
                    bool close = dist < d.Reach && Math.Abs(dy) < 2.5f;
                    if (close || m.Phase > 0 && dist < 5.5f)
                    {
                        if (m.Phase <= 0) Sound?.Invoke(m, "say");
                        m.Phase += dt; wx = wz = 0;
                        if (m.Phase >= 1.5f) { m.Dead = 0.05f; m.Health = 0; Burst?.Invoke(m); }
                    }
                    else m.Phase = Math.Max(0, m.Phase - dt * 2);
                    m.Windup = m.Phase > 0 ? 0.2f : 0;
                }
                else if (d.Swims)
                {
                    // Only after someone who is in the water; it rises and sinks to their level.
                    bool wet = Blocks.IsWater(_get((int)Math.Floor(t.X), (int)Math.Floor(t.Y + 0.3f), (int)Math.Floor(t.Z)));
                    if (wet)
                    {
                        wx = dx; wz = dz; m.SwimVY = Math.Clamp((t.Y + 0.5f - m.Y) * 2.5f, -3, 3);
                        if (dist < d.Reach + 0.3f && Math.Abs(dy) < 2 && m.Cool <= 0)
                        {
                            m.Cool = d.AttackEvery; m.Windup = 0.2f;
                            HitPlayer?.Invoke(t.Id, d.Damage * (Difficulty == 1 ? 0.6f : 1), m.X, m.Z, m);
                        }
                    }
                    else { m.Target = -1; m.SwimVY = -0.6f; }
                }
                else
                {
                    wx = dx; wz = dz;
                    if (d.Blinks && (m.InWater || dist > 8 && (m.Phase -= dt) <= 0)) { m.Phase = 2.5f + Rand() * 3; Blink(m, t.X, t.Z, 2, 5); }
                    if (m.Kind == MobKind.King && (m.Idle -= dt) <= 0)
                    {
                        m.Idle = 7 + Rand() * 4;
                        Sound?.Invoke(m, "say");
                        // He calls Shades to his side (four at most), and once half his strength is gone throws a fan of bolts.
                        int shades = 0; foreach (var o in All) if (o.Kind == MobKind.Shade && o.Dead <= 0 && Math.Abs(o.X - m.X) < 40 && Math.Abs(o.Z - m.Z) < 40) shades++;
                        for (int i = shades; i < Math.Min(4, shades + 2); i++) { var sh = Spawn(MobKind.Shade, m.X, m.Y, m.Z); if (!Blink(sh, t.X, t.Z, 4, 9)) { sh.Health = 0; sh.Dead = 0.01f; } else sh.Target = t.Id; }
                        if (m.Health < d.Health * 0.5f && dist > 3)
                            for (int i = -2; i <= 2; i++)
                            {
                                float ang = (float)Math.Atan2(dx, dz) + i * 0.16f, ey = m.Y + d.Height * 0.7f, flat = Math.Max(1, dist);
                                Shoot(-m.Id, m.X, ey, m.Z, (float)Math.Sin(ang) * 22, (t.Y + 1.2f - ey) / (flat / 22) + 10 * (flat / 22), (float)Math.Cos(ang) * 22, 5);
                            }
                    }
                    if (!d.Climbs && dist > 2.5f) Way(m, t.X, t.Y, t.Z, ref wx, ref wz);
                    if (m.Kind == MobKind.Crawler && dist < 4.5f && dist > 2 && m.OnGround && Rand() < dt * 2) { m.VY = 6; m.VX += dx / dist * 4; m.VZ += dz / dist * 4; }
                    if (dist < d.Reach + 0.3f && Math.Abs(dy) < 2.2f && m.Cool <= 0)
                    {
                        if (m.Windup <= 0) m.Windup = 0.3f;
                    }
                    if (m.Windup > 0)
                    {
                        m.Windup -= dt;
                        if (m.Windup <= 0)
                        {
                            m.Cool = d.AttackEvery;
                            if (dist < d.Reach + 0.6f && Math.Abs(dy) < 2.4f)
                            {
                                float dmg = d.Damage * (Difficulty == 1 ? 0.6f : 1) * (m.Kind == MobKind.Glob ? (m.Job == 0 ? 1 : m.Job == 1 ? 0.6f : 0.2f) : 1);
                                HitPlayer?.Invoke(t.Id, dmg, m.X, m.Z, m);
                            }
                        }
                    }
                    if (dist < 0.9f) { wx = 0; wz = 0; }
                }
                speed *= 1.25f;
            }
            else if (m.Flee > 0)
            {
                m.Flee -= dt;
                wx = m.X - m.FromX; wz = m.Z - m.FromZ;
                speed *= 1.9f;
                jumpy = true;
            }
            else if (!d.Hostile && Lure(m, players, out float lx, out float lz))
            {
                // Following someone holding its food, or looking for a mate; stops close by.
                wx = lx - m.X; wz = lz - m.Z;
                if (wx * wx + wz * wz < (m.Love > 0 ? 0.6f : 4f)) { wx = 0; wz = 0; m.Yaw = LerpAngle(m.Yaw, (float)(Math.Atan2(lx - m.X, lz - m.Z) * 180 / Math.PI), Math.Min(1, dt * 4)); }
                speed *= m.Love > 0 ? 0.8f : 0.9f;
                m.HasGoal = false;
            }
            else
            {
                // Wander: a goal now and then, or stand and graze.
                m.Think -= dt;
                if (m.Think <= 0)
                {
                    m.Think = 2 + Rand() * 6;
                    if (Rand() < 0.55f)
                    {
                        float a = Rand() * 6.283f, r = 3 + Rand() * 7;
                        m.GoalX = m.X + (float)Math.Cos(a) * r; m.GoalZ = m.Z + (float)Math.Sin(a) * r; m.HasGoal = true;
                        // Villagers stay about their village.
                        if (m.Kind == MobKind.Villager && (m.X - m.HomeX) * (m.X - m.HomeX) + (m.Z - m.HomeZ) * (m.Z - m.HomeZ) > 22 * 22)
                        { m.GoalX = m.HomeX + (Rand() - 0.5f) * 12; m.GoalZ = m.HomeZ + (Rand() - 0.5f) * 12; }
                    }
                    else m.HasGoal = false;
                    if (!d.Hostile && Rand() < 0.25f) Sound?.Invoke(m, "say");
                }
                if (m.HasGoal)
                {
                    wx = m.GoalX - m.X; wz = m.GoalZ - m.Z;
                    if (wx * wx + wz * wz < 0.5f) m.HasGoal = false;
                    speed *= 0.6f;
                }
                // Wanderers don't walk off cliffs or into the sea.
                if (m.HasGoal && !SafeAhead(m, wx, wz)) { m.HasGoal = false; wx = wz = 0; }
            }

            float len = (float)Math.Sqrt(wx * wx + wz * wz);
            // A swimmer keeps to the water: it turns back at the shore and stays under the surface.
            if (d.Swims)
            {
                if (len > 0.01f && !Blocks.IsWater(_get((int)Math.Floor(m.X + wx / len * 1.2f), (int)Math.Floor(m.Y + 0.2f), (int)Math.Floor(m.Z + wz / len * 1.2f)))) { wx = wz = 0; len = 0; m.HasGoal = false; }
                if (target == null) m.SwimVY = (float)Math.Sin(m.Age * 0.7f + m.Id) * 0.5f;
                if (!Blocks.IsWater(_get((int)Math.Floor(m.X), (int)Math.Floor(m.Y + 0.9f), (int)Math.Floor(m.Z)))) m.SwimVY = Math.Min(m.SwimVY, -0.8f);
            }
            float ax = 0, az = 0;
            if (len > 0.01f)
            {
                ax = wx / len * speed; az = wz / len * speed;
                if (target == null) m.Yaw = LerpAngle(m.Yaw, (float)(Math.Atan2(wx, wz) * 180 / Math.PI), Math.Min(1, dt * 6));
            }
            // A hopper only gets anywhere in the air: on the ground it gathers itself, then bounds.
            bool bound = false;
            if (d.Hops && m.OnGround)
            {
                if (len > 0.01f && (m.Phase -= dt) <= 0) { m.Phase = 0.6f + Rand() * 0.9f; bound = true; }
                else { ax = az = 0; }
            }
            float k = m.OnGround || m.InWater ? 1 - (float)Math.Exp(-dt * 10) : 1 - (float)Math.Exp(-dt * 1.5f);
            m.VX += (ax - m.VX) * k; m.VZ += (az - m.VZ) * k;
            if (bound) { m.VY = 6.4f; m.VX = wx / len * speed * 1.7f; m.VZ = wz / len * speed * 1.7f; }

            // Hop up a step when something's in the way; climbers go straight up walls.
            if (len > 0.01f && (m.OnGround || d.Climbs || m.InWater))
            {
                float fx = m.X + ax / speed * (d.HalfW + 0.35f), fz = m.Z + az / speed * (d.HalfW + 0.35f);
                bool wall = Body.Blocked(_get, fx, m.Y + 0.1f, fz, 0.05f, Math.Min(d.Height, 0.9f) - 0.1f);
                if (wall)
                {
                    bool high = Body.Blocked(_get, fx, m.Y + 1.05f, fz, 0.05f, Math.Max(0.1f, d.Height - 0.95f));
                    if (d.Climbs) m.VY = 3.2f;
                    else if (!high && m.OnGround) m.VY = 7.6f;
                }
                if (jumpy && m.OnGround && Rand() < dt * 1.2f) m.VY = 6;
            }
            if (m.InWater && !d.Swims) m.VY = Math.Max(m.VY, d.Floats ? 1.5f : 1.0f);
        }

        // A friend's turn: stay if told, see off anything hostile nearby, otherwise keep up with its owner.
        void Pet(Mob m, float dt, List<Seen> players, ref float wx, ref float wz, ref float speed)
        {
            m.Flee = 0; m.Angry = false; m.Target = -1; m.HasGoal = false;
            Seen? owner = null;
            foreach (var p in players) if ((p.Key ?? "") == m.OwnerKey && p.Alive) owner = p;
            if (m.Sit) return;
            Mob foe = null; float best = 9 * 9;
            if (owner != null)
                foreach (var o in All)
                {
                    if (!o.Def.Hostile || o.Dead > 0 || o.Kind == MobKind.Bloat || o.Def.Boss || o.Kind == MobKind.Arrow) continue;
                    float fx = o.X - m.X, fy = o.Y - m.Y, fz = o.Z - m.Z, d2 = fx * fx + fy * fy * 4 + fz * fz;
                    if (d2 < best) { best = d2; foe = o; }
                }
            if (foe != null)
            {
                wx = foe.X - m.X; wz = foe.Z - m.Z; speed *= 1.5f;
                m.Yaw = (float)(Math.Atan2(wx, wz) * 180 / Math.PI);
                if (wx * wx + wz * wz > 6) Way(m, foe.X, foe.Y, foe.Z, ref wx, ref wz);
                else if (m.Cool <= 0) { m.Cool = 1; m.Windup = 0.2f; Hurt(foe, m.Def.Damage + 1, m.X, m.Z, 0, 0.6f); }
                return;
            }
            if (owner is Seen who)
            {
                float dx = who.X - m.X, dz = who.Z - m.Z, dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if ((dist > 22 || Math.Abs(who.Y - m.Y) > 12) && Loaded((int)Math.Floor(who.X), (int)Math.Floor(who.Z)))
                {
                    // Left far behind: it turns up at its owner's side.
                    m.X = who.X + 0.8f; m.Y = who.Y + 0.1f; m.Z = who.Z + 0.8f; m.VX = m.VY = m.VZ = 0; m.HasWay = false;
                }
                else if (dist > 3.5f)
                {
                    wx = dx; wz = dz; speed *= dist > 8 ? 1.7f : 1.1f;
                    Way(m, who.X, who.Y, who.Z, ref wx, ref wz);
                }
            }
        }

        // ------------------------------------------------------------------ finding a way round
        // Where to head next on the way to (tx, ty, tz): the next step of a path over ground that can be walked
        // (up one block, down three), worked out a few times a second. With no path it stays the straight line.
        const int PathReach = 18, PathMost = 320;
        readonly Dictionary<long, (long from, int g)> _seenNodes = new Dictionary<long, (long, int)>();
        readonly List<(long key, int f)> _open = new List<(long, int)>();

        static long NodeKey(int x, int y, int z) => ((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (long)(y & 0xFFF);
        static (int x, int y, int z) NodeAt(long k)
        {
            int x = (int)(k >> 38); if (x >= 1 << 25) x -= 1 << 26;
            int z = (int)((k >> 12) & 0x3FFFFFF); if (z >= 1 << 25) z -= 1 << 26;
            return (x, (int)(k & 0xFFF), z);
        }

        bool Stand(int x, int y, int z, bool tall)
        {
            if (y < 1 || y >= Chunk.Height - 2) return false;
            var here = Blocks.Get(_get(x, y, z));
            if (here.Solid || Blocks.IsLava(here.Id) || here.Id == Blocks.Fire) return false;
            if (tall && Blocks.Get(_get(x, y + 1, z)).Solid) return false;
            var below = Blocks.Get(_get(x, y - 1, z));
            return below.Solid || here.Fluid == 1;
        }

        void Way(Mob m, float tx, float ty, float tz, ref float wx, ref float wz)
        {
            if (m.WayAt > _clock)
            {
                if (m.HasWay) { wx = m.WayX - m.X; wz = m.WayZ - m.Z; if (wx * wx + wz * wz < 0.2f) m.WayAt = 0; }
                return;
            }
            m.WayAt = _clock + 0.5f + Rand() * 0.4f; m.HasWay = false;
            bool tall = m.Def.Height > 1.05f;
            int sx = (int)Math.Floor(m.X), sy = (int)Math.Floor(m.Y + 0.3f), sz = (int)Math.Floor(m.Z);
            int gx = (int)Math.Floor(tx), gy = (int)Math.Floor(ty + 0.3f), gz = (int)Math.Floor(tz);
            if (Math.Abs(gx - sx) > PathReach || Math.Abs(gz - sz) > PathReach || !Loaded(gx, gz)) return;
            // Standing in the air or the water: fall or float first.
            if (!Stand(sx, sy, sz, tall)) { if (Stand(sx, sy - 1, sz, tall)) sy--; else return; }
            _seenNodes.Clear(); _open.Clear();
            long start = NodeKey(sx, sy, sz), found = -1;
            _seenNodes[start] = (-1, 0); _open.Add((start, 0));
            int looked = 0; long nearest = start; int nearestH = int.MaxValue;
            while (_open.Count > 0 && looked++ < PathMost)
            {
                int bi = 0; for (int i = 1; i < _open.Count; i++) if (_open[i].f < _open[bi].f) bi = i;
                long cur = _open[bi].key; _open.RemoveAt(bi);
                var (x, y, z) = NodeAt(cur);
                int h = Math.Abs(x - gx) + Math.Abs(z - gz) + Math.Abs(y - gy);
                if (h < nearestH) { nearestH = h; nearest = cur; }
                if (Math.Abs(x - gx) + Math.Abs(z - gz) <= 1 && Math.Abs(y - gy) <= 2) { found = cur; break; }
                int g = _seenNodes[cur].g;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (Math.Abs(nx - sx) > PathReach || Math.Abs(nz - sz) > PathReach) continue;
                    int ny = int.MinValue;
                    // Up one (with room to jump), level, or down as far as three.
                    if (Stand(nx, y + 1, nz, tall) && !Blocks.Get(_get(x, y + (tall ? 2 : 1), z)).Solid) ny = y + 1;
                    if (Stand(nx, y, nz, tall)) ny = y;
                    else if (ny == int.MinValue && !Blocks.Get(_get(nx, y, nz)).Solid && (!tall || !Blocks.Get(_get(nx, y + 1, nz)).Solid))
                        for (int down = 1; down <= 3; down++) if (Stand(nx, y - down, nz, tall)) { ny = y - down; break; } else if (Blocks.Get(_get(nx, y - down, nz)).Solid) break;
                    if (ny == int.MinValue) continue;
                    long nk = NodeKey(nx, ny, nz);
                    int ng = g + 10 + (ny > y ? 4 : 0) + (Blocks.Get(_get(nx, ny, nz)).Fluid == 1 ? 12 : 0);
                    if (_seenNodes.TryGetValue(nk, out var old) && old.g <= ng) continue;
                    _seenNodes[nk] = (cur, ng);
                    _open.Add((nk, ng + (Math.Abs(nx - gx) + Math.Abs(nz - gz) + Math.Abs(ny - gy)) * 10));
                }
            }
            if (found < 0) found = nearest;           // no way all the way: as near as can be reached
            if (found == start) return;
            // Walk back to the step after the start.
            long step = found;
            while (_seenNodes[step].from != start && _seenNodes[step].from != -1) step = _seenNodes[step].from;
            var (wxi, _, wzi) = NodeAt(step);
            m.WayX = wxi + 0.5f; m.WayZ = wzi + 0.5f; m.HasWay = true;
            wx = m.WayX - m.X; wz = m.WayZ - m.Z;
        }

        static float LerpAngle(float a, float b, float t)
        {
            float d = ((b - a) % 360 + 540) % 360 - 180;
            return a + d * t;
        }

        bool SafeAhead(Mob m, float wx, float wz)
        {
            float len = (float)Math.Sqrt(wx * wx + wz * wz);
            if (len < 0.01f) return true;
            int x = (int)Math.Floor(m.X + wx / len * 1.2f), z = (int)Math.Floor(m.Z + wz / len * 1.2f), y = (int)Math.Floor(m.Y + 0.2f);
            if (!Loaded(x, z)) return false;
            for (int dy = 0; dy <= 3; dy++)
            {
                ushort b = _get(x, y - 1 - dy, z);
                if (Blocks.IsFluid(b)) return (m.Kind == MobKind.Chicken || m.Kind == MobKind.Penguin) && Blocks.IsWater(b);
                if (Blocks.Get(b).Solid) return true;
            }
            return false;
        }

        // Can the creature see the player (no solid block between their eyes)?
        bool See(Mob m, in Seen p)
        {
            float ox = m.X, oy = m.Y + m.Def.Height * 0.85f, oz = m.Z;
            float dx = p.X - ox, dy = p.Y + 1.5f - oy, dz = p.Z - oz;
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            int steps = (int)(len / 0.5f);
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                var b = Blocks.Get(_get((int)Math.Floor(ox + dx * t), (int)Math.Floor(oy + dy * t), (int)Math.Floor(oz + dz * t)));
                if (b.Opaque) return false;
            }
            return true;
        }

        void Physics(Mob m, float dt)
        {
            var d = m.Def;
            int cx = (int)Math.Floor(m.X), cz = (int)Math.Floor(m.Z);
            ushort at = _get(cx, (int)Math.Floor(m.Y + 0.3f), cz);
            m.InWater = Blocks.IsWater(at);
            m.InLava = Blocks.IsLava(at);
            float g = m.InWater || m.InLava ? 8 : d.Floats && m.VY < 0 ? 12 : 26;
            if (d.Swims && m.InWater) m.VY += (m.SwimVY - m.VY) * Math.Min(1, dt * 6);       // it swims where it means to
            else m.VY -= g * dt;
            if (m.InWater || m.InLava) { m.VY = Math.Max(m.VY, -2.5f); m.VX *= 1 - dt * 2; m.VZ *= 1 - dt * 2; }
            if (d.Floats && m.VY < -3) m.VY = -3;
            if (m.VY < -40) m.VY = -40;

            float hw = d.HalfW * 0.9f, h = d.Height * 0.95f;
            bool wasGround = m.OnGround;
            m.OnGround = false;
            float my = Body.Sweep(_get, m.X, m.Y, m.Z, hw, h, 1, m.VY * dt);
            if (Math.Abs(my) < Math.Abs(m.VY * dt) - 1e-5f) { if (m.VY < 0) { m.OnGround = true; if (!wasGround && m.VY < -16 && !d.Floats) Hurt(m, (-m.VY - 16) * 0.5f, m.X, m.Z, 0, 0); } m.VY = 0; }
            m.Y += my;
            float mx = Body.Sweep(_get, m.X, m.Y, m.Z, hw, h, 0, m.VX * dt);
            if (Math.Abs(mx) < Math.Abs(m.VX * dt) - 1e-5f) m.VX = 0;
            m.X += mx;
            float mz = Body.Sweep(_get, m.X, m.Y, m.Z, hw, h, 2, m.VZ * dt);
            if (Math.Abs(mz) < Math.Abs(m.VZ * dt) - 1e-5f) m.VZ = 0;
            m.Z += mz;
            if (!m.OnGround && m.VY <= 0 && Body.Blocked(_get, m.X - hw, m.Y - 0.04f, m.Z - hw, m.X + hw, m.Y, m.Z + hw)) m.OnGround = true;
            // Stuck inside something (a block placed on it): nudge it up out of it.
            if (Body.Blocked(_get, m.X, m.Y + 0.05f, m.Z, hw * 0.8f, h * 0.8f)) m.Y += 2 * dt;
            if (m.Y < -30) { m.Health = 0; m.Dead = 0.01f; }
        }

        void Hazards(Mob m, float dt)
        {
            var d = m.Def;
            // The undead burn in sunshine (but not in water or under cover).
            bool burning = false;
            if (d.Burns && !Ember.In(m.X) && IsDay && Weather == 0 && !m.InWater && SkyOpen((int)Math.Floor(m.X), (int)Math.Floor(m.Y + d.Height), (int)Math.Floor(m.Z))) burning = true;
            if (m.InLava && !d.Fireproof) burning = true;
            if (!d.Fireproof && m.Y >= 0 && m.Y < Chunk.Height && _get((int)Math.Floor(m.X), (int)Math.Floor(m.Y + 0.2f), (int)Math.Floor(m.Z)) == Blocks.Fire) burning = true;
            if (burning) m.Burn = 1.5f; else m.Burn = Math.Max(0, m.Burn - dt);
            if (m.Burn > 0 && !d.Fireproof && m.Hurt <= 0) Hurt(m, m.InLava ? 4 : 1, m.X + 0.01f, m.Z, 0, 0.1f);
            if (m.InWater && m.Kind == MobKind.EmberImp && m.Hurt <= 0) Hurt(m, 1, m.X, m.Z, 0, 0.1f);
            if (d.Swims && !m.InWater && m.Hurt <= 0) Hurt(m, 2, m.X, m.Z, 0, 0.3f);          // out of water it gasps
        }

        bool SkyOpen(int x, int y, int z)
        {
            for (int yy = Math.Max(y, 0); yy < Chunk.Height; yy++)
                if (Blocks.Get(_get(x, yy, z)).Opaque) return false;
            return true;
        }

        void Arrow(Mob a, float dt, List<Seen> players)
        {
            if (a.Stuck) { if (a.Age > 20) Remove(a); return; }
            if (a.Age > 12) { Remove(a); return; }
            a.VY -= 20 * dt;
            float dx = a.VX * dt, dy = a.VY * dt, dz = a.VZ * dt;
            int steps = Math.Max(1, (int)(Math.Sqrt(dx * dx + dy * dy + dz * dz) / 0.25f) + 1);
            for (int s = 0; s < steps; s++)
            {
                a.X += dx / steps; a.Y += dy / steps; a.Z += dz / steps;
                var b = Blocks.Get(_get((int)Math.Floor(a.X), (int)Math.Floor(a.Y), (int)Math.Floor(a.Z)));
                if (b.Solid && Body.Blocked(_get, a.X - 0.02f, a.Y - 0.02f, a.Z - 0.02f, a.X + 0.02f, a.Y + 0.02f, a.Z + 0.02f))
                {
                    a.Stuck = true; a.Age = 0; a.VX = a.VY = a.VZ = 0;
                    Sound?.Invoke(a, "stick");
                    return;
                }
                if (a.Owner > 0)
                {
                    foreach (var m in All)
                    {
                        if (m.Kind == MobKind.Arrow || m.Dead > 0) continue;
                        float hw = m.Def.HalfW + 0.1f;
                        if (a.X > m.X - hw && a.X < m.X + hw && a.Z > m.Z - hw && a.Z < m.Z + hw && a.Y > m.Y && a.Y < m.Y + m.Def.Height + 0.1f)
                        {
                            Hurt(m, a.ArrowDamage, a.X - a.VX, a.Z - a.VZ, a.Owner, 0.6f);
                            Remove(a);
                            return;
                        }
                    }
                }
                else
                {
                    foreach (var p in players)
                    {
                        if (!p.Alive || p.Creative) continue;
                        if (a.X > p.X - 0.4f && a.X < p.X + 0.4f && a.Z > p.Z - 0.4f && a.Z < p.Z + 0.4f && a.Y > p.Y && a.Y < p.Y + 1.9f)
                        {
                            _byId.TryGetValue(-a.Owner, out var from);
                            HitPlayer?.Invoke(p.Id, a.ArrowDamage, a.X - a.VX, a.Z - a.VZ, from ?? a);
                            Remove(a);
                            return;
                        }
                    }
                }
            }
            AimArrow(a);
        }

        // ------------------------------------------------------------------ appearing

        void TrySpawn(in Seen p, List<Seen> players)
        {
            int passive = 0, hostile = 0;
            foreach (var m in All)
            {
                if (m.Kind == MobKind.Arrow || m.Kind == MobKind.Villager || m.Def.Boss || m.Def.Vehicle) continue;
                float dx = m.X - p.X, dz = m.Z - p.Z;
                if (dx * dx + dz * dz > 80 * 80) continue;
                if (m.Def.Hostile) hostile++; else passive++;
            }
            int hostileCap = Difficulty == 0 ? 0 : Difficulty == 1 ? HostileCap - 3 : HostileCap;
            if (Quiet.In(p.X))
            {
                // Only Shades walk the Quiet, by day or night.
                if (hostile < 5 && Difficulty > 0)
                {
                    float qa = Rand() * 6.283f, qr = 16 + Rand() * 20;
                    int qx = (int)Math.Floor(p.X + Math.Cos(qa) * qr), qz = (int)Math.Floor(p.Z + Math.Sin(qa) * qr);
                    if (Loaded(qx, qz))
                    {
                        int qy = Ground(qx, qz, (int)p.Y + 14, 40, out ushort qf);
                        if (qy != int.MinValue && qf == Blocks.Stillstone && !Body.Blocked(_get, qx + 0.5f, qy, qz + 0.5f, 0.3f, 2.6f)) Spawn(MobKind.Shade, qx + 0.5f, qy, qz + 0.5f);
                    }
                }
                return;
            }
            if (Ember.In(p.X)) { if (hostile < hostileCap) TrySpawnEmber(p, players); return; }
            if (passive < PassiveCap && Rand() < 0.35f) TrySpawnAnimals(p, players);
            if (hostile < hostileCap) TrySpawnMonster(p, players);
        }

        bool FarFromAll(float x, float y, float z, List<Seen> players, float min)
        {
            foreach (var o in players) { float dx = o.X - x, dy = o.Y - y, dz = o.Z - z; if (dx * dx + dy * dy + dz * dz < min * min) return false; }
            return true;
        }

        // The top of the ground in a column near a height, or int.MinValue.
        int Ground(int x, int z, int fromY, int down, out ushort floor)
        {
            floor = 0;
            for (int y = Math.Min(fromY, Chunk.Height - 3); y > Math.Max(1, fromY - down); y--)
            {
                ushort b = _get(x, y, z);
                if (!Blocks.Get(b).Solid) continue;
                ushort a1 = _get(x, y + 1, z), a2 = _get(x, y + 2, z);
                if (Blocks.Get(a1).Solid || Blocks.Get(a2).Solid || Blocks.IsFluid(a1)) continue;
                floor = b;
                return y + 1;
            }
            return int.MinValue;
        }

        // A village near a player has its people (one or two a house); they're kept with the world after that.
        void Villagers(in Seen p)
        {
            var v = VillageNear((int)p.X, (int)p.Z);
            if (v == null || !Loaded(v.X, v.Z)) return;
            int want = Math.Max(2, v.Houses + 1), have = 0;
            foreach (var m in All) if (m.Kind == MobKind.Villager && Math.Abs(m.HomeX - v.X) < 1 && Math.Abs(m.HomeZ - v.Z) < 1) have++;
            if (have >= want) return;
            for (int tries = 0; tries < 6; tries++)
            {
                int x = v.X + _r.Next(-10, 11), z = v.Z + _r.Next(-10, 11);
                if (!Loaded(x, z)) continue;
                int y = Ground(x, z, v.Y + 12, 24, out ushort floor);
                if (y == int.MinValue || Blocks.Get(floor).Fluid != 0) continue;
                var m = Spawn(MobKind.Villager, x + 0.5f, y, z + 0.5f);
                m.Kept = true; m.HomeX = v.X; m.HomeZ = v.Z; m.Job = (byte)(((have + (v.X * 31 + v.Z)) % Trades.Jobs.Length + Trades.Jobs.Length) % Trades.Jobs.Length & 7);
                if (m.Job >= Trades.Jobs.Length) m.Job = 0;
                return;
            }
        }

        // The Cinder Colossus guards each keep in the Deep; once beaten, it's twenty minutes before another comes.
        public int Seed;
        float _clock;
        readonly Dictionary<(int, int), float> _bossBack = new Dictionary<(int, int), float>();

        // The Hollow King stands at his throne whenever someone comes near (and comes back twenty minutes after a fall).
        void King(in Seen p)
        {
            if (Difficulty == 0) return;
            var (kx, ky, kz) = Quiet.Throne;
            if (Math.Abs(p.X - kx) > 70 || Math.Abs(p.Z - kz) > 70 || !Loaded(kx, kz)) return;
            foreach (var m in All) if (m.Kind == MobKind.King && m.Dead <= 0) return;
            if (_bossBack.TryGetValue((kx, kz), out float back) && back > _clock) return;
            var king = Spawn(MobKind.King, kx + 0.5f, ky, kz + 0.5f);
            king.HomeX = kx; king.HomeZ = kz; king.Idle = 6;
            Sound?.Invoke(king, "say");
        }

        void Keeper(in Seen p)
        {
            if (Difficulty == 0) return;
            var k = Ember.KeepNear(p.X, p.Z, 48, Seed);
            if (k == null) return;
            var (kx, ky, kz) = k.Value;
            if (!Loaded(kx, kz)) return;
            foreach (var m in All) if (m.Kind == MobKind.Colossus && m.Dead <= 0 && Math.Abs(m.X - kx) < 90 && Math.Abs(m.Z - kz) < 90) return;
            if (_bossBack.TryGetValue((kx, kz), out float back) && back > _clock) return;
            var boss = Spawn(MobKind.Colossus, kx + 0.5f, ky, kz + 0.5f);
            boss.HomeX = kx; boss.HomeZ = kz;
            Sound?.Invoke(boss, "say");
        }

        void TrySpawnAnimals(in Seen p, List<Seen> players)
        {
            float a = Rand() * 6.283f, r = 24 + Rand() * 32;
            int x = (int)Math.Floor(p.X + Math.Cos(a) * r), z = (int)Math.Floor(p.Z + Math.Sin(a) * r);
            if (!Loaded(x, z)) return;
            int y = Ground(x, z, (int)p.Y + 24, 48, out ushort floor);
            if (y == int.MinValue) return;
            var biome = BiomeAt?.Invoke(x, z) ?? Biome.Plains;
            // Most animals want grass; camels walk the sand and goats the bare rock.
            bool Stands(ushort f) => f == Blocks.Grass || f == Blocks.SnowyGrass || biome == Biome.Desert && f == Blocks.Sand
                                     || biome == Biome.Peaks && (f == Blocks.Stone || f == Blocks.Snow);
            if (!Stands(floor)) return;
            if (!SkyOpen(x, y, z) || !FarFromAll(x, y, z, players, 22)) return;
            MobKind kind;
            float k = Rand();
            switch (biome)
            {
                case Biome.Taiga: kind = k < 0.3f ? MobKind.Sheep : k < 0.45f ? MobKind.Cow : k < 0.65f ? MobKind.Fox : k < 0.82f ? MobKind.Hound : MobKind.Deer; break;
                case Biome.Tundra: kind = k < 0.5f ? MobKind.Penguin : k < 0.8f ? MobKind.Sheep : MobKind.Fox; break;
                case Biome.Forest: case Biome.BirchWood:
                    kind = k < 0.22f ? MobKind.Pig : k < 0.4f ? MobKind.Chicken : k < 0.55f ? MobKind.Cow : k < 0.75f ? MobKind.Deer : k < 0.88f ? MobKind.Hound : MobKind.Rabbit; break;
                case Biome.Desert: if (Rand() > 0.35f) return; kind = MobKind.Camel; break;
                case Biome.Peaks: kind = MobKind.Goat; break;
                case Biome.Beach: case Biome.Ocean: return;
                default: kind = k < 0.25f ? MobKind.Cow : k < 0.5f ? MobKind.Sheep : k < 0.65f ? MobKind.Pig : k < 0.8f ? MobKind.Chicken : MobKind.Rabbit; break;
            }
            int n = 2 + _r.Next(3);
            for (int i = 0; i < n; i++)
            {
                int sx = x + _r.Next(-2, 3), sz = z + _r.Next(-2, 3);
                int sy = Ground(sx, sz, y + 3, 6, out ushort f2);
                if (sy == int.MinValue || !Stands(f2)) continue;
                Spawn(kind, sx + 0.5f, sy, sz + 0.5f);
            }
        }

        void TrySpawnMonster(in Seen p, List<Seen> players)
        {
            for (int tries = 0; tries < 3; tries++)
            {
                float a = Rand() * 6.283f, r = 22 + Rand() * 26;
                int x = (int)Math.Floor(p.X + Math.Cos(a) * r), z = (int)Math.Floor(p.Z + Math.Sin(a) * r);
                if (!Loaded(x, z)) continue;
                // Underground below the player as well as on the surface.
                int from = (int)p.Y + (Rand() < 0.5f ? 10 : -6 + _r.Next(-10, 6));
                int y = Ground(x, z, from, 24, out ushort floor);
                if (y == int.MinValue || floor == Blocks.Bedrock || floor == Blocks.OakLeaves || floor == Blocks.BirchLeaves || floor == Blocks.PineNeedles || Blocks.Get(floor).Container) continue;
                bool open = SkyOpen(x, y, z);
                if (open && IsDay) continue;                       // the sun keeps the surface safe
                if (BlockLight(x, y, z) > 3) continue;            // torches keep a place safe
                if (!FarFromAll(x, y, z, players, 20)) continue;
                float k = Rand();
                var kind = k < 0.34f ? MobKind.Ghoul : k < 0.56f ? MobKind.BoneArcher : k < 0.70f ? MobKind.Crawler : k < 0.82f ? MobKind.Bloat : k < 0.92f ? MobKind.Glob : MobKind.Shade;
                if ((kind == MobKind.Bloat || kind == MobKind.Glob || kind == MobKind.Shade) && Ember.In(x)) kind = MobKind.Ghoul;
                // Globs keep to caves and swamps; Shades walk the open night.
                if (kind == MobKind.Glob && open && (BiomeAt == null || BiomeAt(x, z) != Biome.Swamp)) kind = MobKind.Ghoul;
                if (kind == MobKind.Shade && (!open || Body.Blocked(_get, x + 0.5f, y, z + 0.5f, 0.3f, 2.6f))) kind = MobKind.Ghoul;
                if (kind == MobKind.Crawler && Body.Blocked(_get, x + 0.5f, y, z + 0.5f, 0.6f, 0.9f)) kind = MobKind.Ghoul;
                if (Body.Blocked(_get, x + 0.5f, y, z + 0.5f, Def(kind).HalfW, Def(kind).Height)) continue;
                Spawn(kind, x + 0.5f, y, z + 0.5f);
                return;
            }
        }

        void TrySpawnEmber(in Seen p, List<Seen> players)
        {
            float a = Rand() * 6.283f, r = 18 + Rand() * 22;
            int x = (int)Math.Floor(p.X + Math.Cos(a) * r), z = (int)Math.Floor(p.Z + Math.Sin(a) * r);
            if (!Loaded(x, z)) return;
            int y = Ground(x, z, (int)p.Y + 12, 30, out ushort floor);
            if (y == int.MinValue || !FarFromAll(x, y, z, players, 16)) return;
            if (Body.Blocked(_get, x + 0.5f, y, z + 0.5f, 0.4f, 1.6f)) return;
            Spawn(MobKind.EmberImp, x + 0.5f, y, z + 0.5f);
        }

        // Torch light reaching a cell (a quick guess: the brightest source within reach, less its distance).
// The light lamps and torches throw on a place, the way it is drawn: it spreads cell by cell through open
        // space and loses one each step, so a torch behind a wall lights nothing on the dark side. (Counting straight
        // through the wall kept monsters out of rooms that looked pitch black.)
        const int LightReach = 12, LightSide = LightReach * 2 + 1;
        readonly byte[] _lit = new byte[LightSide * LightSide * LightSide];
        readonly int[] _litQueue = new int[LightSide * LightSide * LightSide];
        byte _litMark;

        public int BlockLight(int x, int y, int z)
        {
            if (++_litMark == 0) { Array.Clear(_lit, 0, _lit.Length); _litMark = 1; }
            int best = Blocks.Get(_get(x, y, z)).Emit;
            int head = 0, tail = 0;
            int start = LightReach + LightReach * LightSide + LightReach * LightSide * LightSide;
            _lit[start] = _litMark; _litQueue[tail++] = start;
            int layerEnd = tail, dist = 0;
            while (head < tail)
            {
                if (head == layerEnd) { dist++; layerEnd = tail; if (dist >= LightReach || 15 - (dist + 1) <= best) break; }
                int c = _litQueue[head++];
                int cx = c % LightSide, cy = c / LightSide % LightSide, cz = c / (LightSide * LightSide);
                for (int d = 0; d < 6; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = cz + (d == 4 ? 1 : d == 5 ? -1 : 0);
                    if ((uint)nx >= LightSide || (uint)ny >= LightSide || (uint)nz >= LightSide) continue;
                    int n = nx + ny * LightSide + nz * LightSide * LightSide;
                    if (_lit[n] == _litMark) continue;
                    _lit[n] = _litMark;
                    var def = Blocks.Get(_get(x + nx - LightReach, y + ny - LightReach, z + nz - LightReach));
                    if (def.Emit > 0) { int l = def.Emit - (dist + 1); if (l > best) best = l; }
                    if (!def.Opaque) _litQueue[tail++] = n;
                }
            }
            return best;
        }

        public int Living(MobKind k) { int n = 0; foreach (var m in All) if (m.Kind == k && m.Dead <= 0) n++; return n; }
    }
}
