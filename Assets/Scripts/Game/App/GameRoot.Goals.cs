using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stratum.Game
{
    // Goals: things done for the first time in this world, noticed by looking at the player once a second and
    // remembered on this computer.
    public sealed partial class GameRoot
    {
        readonly HashSet<string> _goals = new HashSet<string>();
        string _goalsKey, _myName; float _goalsTimer;

        // Statistics: counts of what has been done in this world, kept beside the goals.
        readonly Dictionary<string, double> _stats = new Dictionary<string, double>();
        float _statSave; Vector3 _statAt; int _page;
        static readonly (string key, string label)[] StatRows =
        {
            ("seconds", "Time played"), ("walked", "Distance travelled"), ("mined", "Blocks mined"), ("placed", "Blocks placed"), ("kills", "Creatures defeated"),
            ("fish", "Fish caught"), ("eaten", "Meals eaten"), ("deaths", "Times fallen"),
        };

        void Stat(string name, double n = 1)
        {
            if (_goalsKey == null) return;
            _stats.TryGetValue(name, out double v); _stats[name] = v + n;
        }

        void SaveStats()
        {
            if (_goalsKey == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _stats) sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            PlayerPrefs.SetString("stats" + _goalsKey.Substring(5), sb.ToString()); PlayerPrefs.Save();
        }

        string StatsText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (key, label) in StatRows)
            {
                _stats.TryGetValue(key, out double v);
                string val = key == "seconds" ? $"{(int)(v / 3600)} h {(int)(v / 60) % 60} min" : key == "walked" ? (v >= 1000 ? $"{v / 1000:0.0} km" : $"{v:0} m") : ((long)v).ToString("N0");
                sb.Append("<color=#c4c4cc>").Append(label).Append("</color>\n<b>      ").Append(val).Append("</b>\n");
            }
            sb.Append("<color=#c4c4cc>Goals reached</color>\n<b>      ").Append(_goals.Count).Append(" of ").Append(Hud.Goals.Length).Append("</b>\n");
            return sb.ToString();
        }

        string GoalsKey => "goals_" + (_meta != null ? _meta.name + "_" + _meta.seed : "online");

        void LoadGoals()
        {
            _goalsKey = GoalsKey; _goals.Clear();
            foreach (var id in PlayerPrefs.GetString(_goalsKey, "").Split(',')) if (id.Length > 0) _goals.Add(id);
            _stats.Clear();
            foreach (var pair in PlayerPrefs.GetString("stats" + _goalsKey.Substring(5), "").Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && double.TryParse(pair.Substring(eq + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) _stats[pair.Substring(0, eq)] = v;
            }
            _statAt = _player != null ? _player.Feet : Vector3.zero;
        }

        void Reach(string id)
        {
            if (!_goals.Add(id)) return;
            PlayerPrefs.SetString(_goalsKey, string.Join(",", _goals)); PlayerPrefs.Save();
            foreach (var (gid, title, how) in Hud.Goals)
                if (gid == id) { _hud.Toast("Goal reached: " + title); _hud.AddChat("", "Goal reached: " + title + "  (G shows them all)"); }
            Sfx.I?.Play("levelup", 0.5f, 1.2f);
        }

        void GoalsTick(Keyboard kb)
        {
            if (_player == null || _hud == null || _state != State.Playing) return;
            if (_goalsKey != GoalsKey) { SaveStats(); LoadGoals(); }
            // Time and distance (a jump of more than a few steps is a teleport, not a walk).
            Stat("seconds", Time.unscaledDeltaTime);
            var moved = _player.Feet - _statAt; _statAt = _player.Feet;
            if (moved.sqrMagnitude < 4 * 4 && !_player.Spectator) Stat("walked", moved.magnitude);
            if ((_statSave -= Time.unscaledDeltaTime) <= 0) { _statSave = 30; SaveStats(); }
            // G opens and closes the list; Tab held shows who is here.
            if (kb != null && !_hud.Typing && !_hud.ChatOpen)
            {
                // G: the goals, then the statistics, then closed again.
                if (!_hud.GoalsOpen) _page = 0;
                if (kb.gKey.wasPressedThisFrame && (!_hud.InventoryOpen || _hud.GoalsOpen))
                {
                    _page = (_page + 1) % 3;
                    if (_page == 2) _hud.ShowStats(StatsText()); else _hud.ShowGoals(_page == 1, _goals);
                }
                else if (kb.escapeKey.wasPressedThisFrame && _hud.GoalsOpen) { _page = 0; _hud.ShowGoals(false, _goals); }
                _hud.ShowWho(kb.tabKey.isPressed && !_hud.InventoryOpen, Online ? _myName : null, _avatars != null ? _avatars.Names : null);
            }
            if (_player.Creative || (_goalsTimer -= Time.unscaledDeltaTime) > 0) return;
            _goalsTimer = 1;
            var inv = _player.Inv;
            bool potion = false; int worn = 0;
            for (int i = 0; i < Inventory.Main; i++)
            {
                string it = inv.Slots[i].Item;
                if (it == null) continue;
                if (it.EndsWith("_log")) Reach("wood");
                else if (it == "workbench") Reach("bench");
                else if (it.StartsWith("wood_pickaxe")) Reach("pick");
                else if (it.StartsWith("stone_pickaxe")) Reach("stone");
                else if (it == "furnace") Reach("furnace");
                else if (it == "iron_ingot") Reach("iron");
                else if (it == "bread") Reach("bread");
                else if (it == "raw_fish") Reach("fish");
                else if (it == "blast_keg") Reach("keg");
                else if (it == "piston" || it == "sticky_piston") Reach("piston");
                else if (it == "starstone") Reach("star");
                else if (it == "colossus_heart") Reach("heart");
                else if (it == "hollow_crown") Reach("crown");
                else if (it.StartsWith("potion_")) potion = true;
            }
            if (potion) Reach("potion");
            for (int i = Inventory.ArmourStart; i < Inventory.Size; i++) if (!inv.Slots[i].Empty) worn++;
            if (worn == 4) Reach("armour");
            if (inv.HasShield) Reach("shield");
            if (_player.Asleep) Reach("bed");
            if (_player.Riding == MobKind.Camel) Reach("camel");
            if (Ember.In(_player.Feet.x)) Reach("ember");
            if (Quiet.In(_player.Feet.x)) Reach("quiet");
            if (!inv.Slots[Inventory.ArmourStart].Empty && inv.Slots[Inventory.ArmourStart].Item.StartsWith("hollow_crown")) Reach("crown");
            if (_player.XpLevel >= 15) Reach("level");
            if (_link != null)
                foreach (var m in _link.Mobs)
                    if (m.Kind == MobKind.Hound && (m.More & Mob.MTame) != 0 && (m.Pos - _player.Feet).sqrMagnitude < 8 * 8) { Reach("hound"); break; }
        }
    }
}
