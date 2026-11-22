using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stratum.Core;

namespace Stratum.Server
{
    // Player accounts: a name and a password, kept in <world>/accounts.json. Passwords are stored only as salted
    // PBKDF2 hashes. Logging in hands the game a "remember me" token so it needn't keep the password; only a hash
    // of each token is stored. Thread-safe: hashing is slow on purpose, so the server calls this off its game loop.
    public sealed class Accounts
    {
        public const int MinName = 3, MinPassword = 6, MaxPassword = 64, MaxAccounts = 500, TokensKept = 8;
        const int Iterations = 120_000;

        public sealed class Account
        {
            public string name { get; set; }                // as typed when made; unique ignoring case
            public string salt { get; set; } = "";
            public string hash { get; set; } = "";          // empty: password was reset, the next login sets it
            public long created { get; set; }
            public long lastLogin { get; set; }
            public List<string> tokens { get; set; } = new List<string>();
            public int fails { get; set; }
            public long lockedUntil { get; set; }
            public bool op { get; set; }                    // may /give, set the time, and make others operators
            [System.Text.Json.Serialization.JsonIgnore] public long nextTry;
        }

        sealed class Store
        {
            public List<Account> accounts { get; set; } = new List<Account>();
            public string appliedReset { get; set; } = "";
        }

        public enum Mode : byte { Login = 0, Create = 1, Token = 2 }

        public readonly struct Result
        {
            public readonly string Error, Name, Key, Token;
            public bool Ok => Error == null;
            public Result(string error, string name = null, string key = null, string token = null) { Error = error; Name = name; Key = key; Token = token; }
        }

        readonly string _file;
        readonly object _lock = new object();
        Store _store;

        public Accounts(string dir)
        {
            _file = Path.Combine(dir, "accounts.json");
            if (File.Exists(_file) || File.Exists(_file + ".bak"))
            {
                _store = SafeFile.Load(_file, f => JsonSerializer.Deserialize<Store>(File.ReadAllText(f)), out string from);
                if (_store != null && from != _file) Log.Warn("accounts.json was unreadable; using accounts.json.bak");
                if (_store == null)
                {
                    Store found = null;
                    string stamp = Backups.Recover(_file, cand => { found = JsonSerializer.Deserialize<Store>(File.ReadAllText(cand)) ?? throw new InvalidDataException("empty"); });
                    _store = found;
                    Log.Warn(found != null ? $"accounts.json was unreadable; using the copy from backup {stamp}" : "accounts.json and its backups are unreadable: starting with no accounts (the damaged file is kept as accounts.json.bad)");
                }
            }
            _store ??= new Store();
        }

        public int Count { get { lock (_lock) return _store.accounts.Count; } }

        // The file name a player's save goes under (stable, safe as a file name, can't be guessed back to the name).
        public static string KeyFor(string name) =>
            "a_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("stratum-account:" + name.ToLowerInvariant()))).ToLowerInvariant().Substring(0, 32);

        // STRATUM_RESET="Name1,Name2": forget those accounts' passwords once, so each owner sets a new one by
        // logging in. Applied once per value, so leaving the variable set doesn't keep wiping new passwords.
        public void ApplyReset(string names)
        {
            if (string.IsNullOrWhiteSpace(names)) return;
            lock (_lock)
            {
                if (_store.appliedReset == names) return;
                foreach (var n in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var a = Find(n);
                    if (a == null) { Log.Warn($"reset: no account called {n}"); continue; }
                    a.hash = ""; a.salt = ""; a.tokens.Clear(); a.fails = 0; a.lockedUntil = 0;
                    Log.Info($"reset: {a.name}'s password is cleared; the next login with any password sets it");
                }
                _store.appliedReset = names;
                Save();
            }
        }

        public Result Enter(Mode mode, string name, string secret, string invite, string serverInvite)
        {
            name = (name ?? "").Trim();
            secret ??= "";
            if (!Net.ValidName(name) || name.Length < MinName) return new Result($"Usernames are {MinName}-{Net.MaxNameLength} letters, numbers, spaces, - or _.");
            long now = DateTime.UtcNow.Ticks;
            lock (_lock)
            {
                var a = Find(name);
                switch (mode)
                {
                    case Mode.Create:
                        if (serverInvite != "" && !Same(invite ?? "", serverInvite)) return new Result(string.IsNullOrEmpty(invite) ? "This server needs an invite code to make an account." : "That invite code isn't right.");
                        if (a != null) return new Result($"There's already an account called {a.name}. Pick another name, or log in.");
                        if (secret.Length < MinPassword || secret.Length > MaxPassword) return new Result($"Passwords are {MinPassword}-{MaxPassword} characters.");
                        if (_store.accounts.Count >= MaxAccounts) return new Result("This server can't take any more accounts.");
                        // Whoever makes the first account runs the place.
                        a = new Account { name = name, created = now, op = _store.accounts.Count == 0 };
                        SetPassword(a, secret);
                        _store.accounts.Add(a);
                        Log.Info($"account made: {name} ({_store.accounts.Count} in all)");
                        return Finish(a, NewToken(a), now);

                    case Mode.Login:
                        if (a == null) return new Result($"There's no account called {name} here. Check the spelling, or make one.");
                        // After too many wrong passwords, guesses are slowed to one every few seconds for a minute. The
                        // right password still gets in, so nobody can lock someone else out by guessing at their name.
                        if (a.lockedUntil > now)
                        {
                            if (now < a.nextTry) return new Result("Too many wrong passwords. Wait a few seconds and try again.");
                            a.nextTry = now + 3 * TimeSpan.TicksPerSecond;
                        }
                        if (a.hash == "")
                        {
                            if (secret.Length < MinPassword || secret.Length > MaxPassword) return new Result($"Your password was reset. Type a new one of {MinPassword}-{MaxPassword} characters.");
                            SetPassword(a, secret);
                            Log.Info($"{a.name} set a new password after a reset");
                        }
                        else if (!Matches(a, secret))
                        {
                            if (++a.fails >= 5) { a.fails = 0; a.lockedUntil = now + TimeSpan.TicksPerMinute; a.nextTry = now + 3 * TimeSpan.TicksPerSecond; }
                            Save();
                            return new Result("Wrong password.");
                        }
                        return Finish(a, NewToken(a), now);

                    case Mode.Token:
                        if (a == null || a.hash == "" || secret.Length < 32 || !a.tokens.Contains(TokenHash(secret)))
                            return new Result("Please log in again with your password.");
                        return Finish(a, "", now);
                }
                return new Result("Unknown login.");
            }
        }

        public bool IsOp(string name) { lock (_lock) { var a = Find(name ?? ""); return a != null && a.op; } }

        // Makes someone an operator or takes it away. Null if done, else why not (the last operator can't step down).
        public string SetOp(string name, bool on)
        {
            lock (_lock)
            {
                var a = Find((name ?? "").Trim());
                if (a == null) return $"There's no account called {name}.";
                if (a.op == on) return on ? $"{a.name} is already an operator." : $"{a.name} isn't an operator.";
                if (!on && _store.accounts.Count(x => x.op) <= 1) return "There has to be one operator left.";
                a.op = on; Save();
                Log.Info($"{a.name} {(on ? "is now" : "is no longer")} an operator");
                return null;
            }
        }

        public List<string> Ops() { lock (_lock) return _store.accounts.Where(x => x.op).Select(x => x.name).ToList(); }

        Result Finish(Account a, string token, long now)
        {
            a.fails = 0; a.lockedUntil = 0; a.lastLogin = now;
            Save();
            return new Result(null, a.name, KeyFor(a.name), token);
        }

        // Two names are the same account when they give the same save file (KeyFor), so a look-alike letter that
        // lower-cases to an ordinary one can't open a second account over someone's save.
        Account Find(string name)
        {
            string low = name.ToLowerInvariant();
            return _store.accounts.FirstOrDefault(x => string.Equals(x.name.ToLowerInvariant(), low, StringComparison.Ordinal));
        }

        static void SetPassword(Account a, string pw)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            a.salt = Convert.ToBase64String(salt);
            a.hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pw, salt, Iterations, HashAlgorithmName.SHA256, 32));
            a.tokens.Clear();                               // a new password logs out every other computer
        }

        static bool Matches(Account a, string pw)
        {
            try
            {
                var want = Convert.FromBase64String(a.hash);
                var got = Rfc2898DeriveBytes.Pbkdf2(pw, Convert.FromBase64String(a.salt), Iterations, HashAlgorithmName.SHA256, want.Length);
                return CryptographicOperations.FixedTimeEquals(want, got);
            }
            catch { return false; }
        }

        static string NewToken(Account a)
        {
            var t = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            a.tokens.Add(TokenHash(t));
            while (a.tokens.Count > TokensKept) a.tokens.RemoveAt(0);
            return t;
        }

        static string TokenHash(string t) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("stratum-token:" + t))).ToLowerInvariant();

        public static bool Same(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

        void Save()
        {
            try
            {
                File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true }));
                SafeFile.Swap(_file + ".tmp", _file, keep: true);
            }
            catch (Exception e) { Log.Warn("could not save accounts: " + e.Message); }
        }
    }
}
